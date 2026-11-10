"""Map / asset freeze manifest for the research tooling (schema portcctv.research.freeze.v1).

create: hashes every frozen file (map / asset content, map-generation code, camera presets, render settings) and records the
        runtime fingerprints of the baseline build (harbor definition SHA-256 and camera pose per CCTV preset, read from
        finished captures made with that build).
check:  re-hashes the same file list and reports added / removed / changed frozen files. Exit 1 when anything changed.

Only reads the project; writes the manifest / check report it is told to write. Run from anywhere:
  python -I Tools/research/freeze_manifest.py create --out Research/freeze/freeze_20261110.json --baseline-captures Builds/Windows/Captures/apply_after2
  python -I Tools/research/freeze_manifest.py check  --freeze Research/freeze/freeze_20261110.json [--out report.json]
"""
import argparse
import datetime
import hashlib
import json
import os
import subprocess
import sys

ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
A = "Assets/PortCCTVSimulator/"

# Frozen content: everything that defines map geometry / placement / assets / their look.
FROZEN_DIRS = [A + d for d in ("HarborPrefabs", "Materials", "Meshes", "Physics", "Textures", "ThirdParty", "Vessels",
                               "Scenes", "Shaders", "Settings")]
FROZEN_RESOURCE_FILES = [A + "Resources/PortCCTV/" + f for f in ("HarborTheme.asset", "SkyLibrary.asset", "VesselCatalog.asset")]
# Map generation, placement, asset look and camera presets (runtime code). Capture / label / batch / UI code is NOT frozen:
# it may change, and its effect on the RGB is checked by re-rendering the baseline scenarios.
FROZEN_CODE = [A + "Runtime/" + f for f in (
    "Harbor.cs", "HarborGeneric02.cs", "HarborParts.cs", "HarborTheme.cs", "ContainerTerminal01.cs", "FishingPort01.cs",
    "Marina01.cs", "RoadNetwork.cs", "Roads.cs", "Parking.cs", "Vegetation.cs", "HillFeatures.cs", "GroundWear.cs",
    "NightLights.cs", "Presets.cs", "VesselCatalog.cs", "VesselGeometry.cs", "VesselPaint.cs", "CarPaint.cs",
    "Replacements.cs", "ProcNoise.cs", "Weather.cs", "SkyLibrary.cs", "SimWorld.cs", "Navigation.cs", "SensorModel.cs",
    "GpuInstanceMask.cs")] + [A + "Editor/" + f for f in (
    "ProceduralAssets.cs", "ProceduralVessels.cs", "VesselDetails.cs", "VillageAssets.cs", "VillageAssets.Vehicles.cs",
    "WeatherAssets.cs", "PhotoAssets.cs", "HillBaker.cs", "ReplacementAssets.cs", "TreeImpostors.cs", "TextureDiet.cs",
    "ColliderRepair.cs", "ProjectBootstrap.cs")]
FROZEN_SETTINGS = ["ProjectSettings/GraphicsSettings.asset", "ProjectSettings/QualitySettings.asset",
                   "ProjectSettings/ProjectSettings.asset", "ProjectSettings/TagManager.asset",
                   "ProjectSettings/HDRPProjectSettings.asset", "ProjectSettings/ShaderGraphSettings.asset",
                   "Packages/manifest.json", "Packages/packages-lock.json"]
MAPS = ["container_terminal_01", "fishing_port_01", "marina_01"]


def sha256_file(p):
    h = hashlib.sha256()
    with open(p, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def frozen_files():
    out = []
    for d in FROZEN_DIRS:
        full = os.path.join(ROOT, d)
        for dp, dn, fn in os.walk(full):
            dn.sort()
            for f in sorted(fn):
                out.append(os.path.relpath(os.path.join(dp, f), ROOT).replace("\\", "/"))
    for f in FROZEN_RESOURCE_FILES + FROZEN_CODE + FROZEN_SETTINGS:
        for g in (f, f + ".meta"):
            if os.path.isfile(os.path.join(ROOT, g)):
                out.append(g)
    return sorted(set(out))


def git(*args):
    try:
        return subprocess.run(["git", "-C", ROOT] + list(args), capture_output=True, text=True, timeout=60).stdout.strip()
    except Exception:
        return ""


def runtime_baseline(captures_dir):
    """Harbor definition hash + camera pose per CCTV preset from finished baseline captures."""
    maps, cams, builds = {}, [], set()
    if not captures_dir:
        return maps, cams, sorted(builds)
    base = os.path.join(ROOT, captures_dir) if not os.path.isabs(captures_dir) else captures_dir
    for name in sorted(os.listdir(base)):
        mp = os.path.join(base, name, "metadata.json")
        if not name.startswith("cctv_") or not os.path.isfile(mp):
            continue
        m = json.load(open(mp, encoding="utf-8"))
        if not m.get("complete"):
            continue
        builds.add(m["build"]["content_sha256"])
        hid = m["harbor_id"]
        prev = maps.get(hid)
        if prev and prev != m["harbor_definition_sha256"]:
            raise SystemExit(f"baseline captures disagree on the harbor hash of {hid}")
        maps[hid] = m["harbor_definition_sha256"]
        c = m["camera"]
        cams.append({"harbor_id": hid, "preset_id": c["preset_id"], "position_world": c["position_world"],
                     "euler_deg": c["euler_deg"], "vertical_fov_deg": c["vertical_fov_deg"], "width": c["width"],
                     "height": c["height"], "source_capture": os.path.relpath(os.path.join(base, name), ROOT).replace("\\", "/")})
    return maps, cams, sorted(builds)


def create(a):
    files = frozen_files()
    entries = [{"path": f, "sha256": sha256_file(os.path.join(ROOT, f)), "bytes": os.path.getsize(os.path.join(ROOT, f))}
               for f in files]
    listing = "\n".join(f"{e['path']}\t{e['sha256']}" for e in entries)
    maps, cams, builds = runtime_baseline(a.baseline_captures)
    man = {
        "freeze_schema": "portcctv.research.freeze.v1",
        "freeze_id": a.freeze_id,
        "created_utc": datetime.datetime.now(datetime.timezone.utc).isoformat(),
        "git_commit": git("rev-parse", "HEAD"),
        "git_uncommitted_frozen_files": len([l for l in git("status", "--porcelain", "--", *FROZEN_DIRS, *FROZEN_CODE).splitlines() if l.strip()]),
        "frozen_dirs": FROZEN_DIRS, "frozen_code": FROZEN_CODE, "frozen_settings": FROZEN_SETTINGS,
        "frozen_resource_files": FROZEN_RESOURCE_FILES,
        "files": entries,
        "file_count": len(entries),
        "content_sha256": hashlib.sha256(listing.encode("utf-8")).hexdigest(),
        "baseline_build_content_sha256": builds,
        "baseline_captures": a.baseline_captures,
        "harbor_definition_sha256": [{"harbor_id": k, "sha256": v} for k, v in sorted(maps.items())],
        "camera_presets": cams,
        "not_frozen": "capture / label / batch / UI / research code; scenario files; research configs and outputs",
        "note": "The build content hash changes with any code change (the research tooling included); the freeze is checked by "
                "content_sha256 over the frozen files plus the runtime harbor hash and camera poses of each map.",
    }
    os.makedirs(os.path.dirname(os.path.abspath(a.out)), exist_ok=True)
    if os.path.exists(a.out):
        raise SystemExit("refusing to overwrite " + a.out)
    with open(a.out, "w", encoding="utf-8", newline="\n") as f:
        json.dump(man, f, indent=1)
    print(f"freeze {a.freeze_id}: {len(entries)} files, content {man['content_sha256'][:16]}, maps {len(maps)}, cameras {len(cams)}")


def check(a):
    man = json.load(open(a.freeze, encoding="utf-8"))
    base = {e["path"]: e["sha256"] for e in man["files"]}
    now_files = frozen_files()
    now = {f: sha256_file(os.path.join(ROOT, f)) for f in now_files}
    changed = sorted(p for p in base if p in now and now[p] != base[p])
    removed = sorted(p for p in base if p not in now)
    added = sorted(p for p in now if p not in base)
    rep = {"check_schema": "portcctv.research.freezecheck.v1", "freeze_id": man["freeze_id"],
           "checked_utc": datetime.datetime.now(datetime.timezone.utc).isoformat(), "git_commit": git("rev-parse", "HEAD"),
           "files_checked": len(now), "changed": changed, "removed": removed, "added": added,
           "frozen_files_unchanged": not (changed or removed or added)}
    if a.out:
        with open(a.out, "w", encoding="utf-8", newline="\n") as f:
            json.dump(rep, f, indent=1)
    print(f"freeze check {man['freeze_id']}: {len(now)} files, changed {len(changed)}, removed {len(removed)}, added {len(added)}"
          f" -> {'UNCHANGED' if rep['frozen_files_unchanged'] else 'CHANGED'}")
    for p in (changed + removed + added)[:40]:
        print("  ", p)
    return 0 if rep["frozen_files_unchanged"] else 1


def main():
    ap = argparse.ArgumentParser()
    sp = ap.add_subparsers(dest="cmd", required=True)
    c = sp.add_parser("create")
    c.add_argument("--out", required=True)
    c.add_argument("--freeze-id", default="freeze_" + datetime.date.today().strftime("%Y%m%d"))
    c.add_argument("--baseline-captures", default="")
    k = sp.add_parser("check")
    k.add_argument("--freeze", required=True)
    k.add_argument("--out", default="")
    a = ap.parse_args()
    if a.cmd == "create":
        create(a)
        return 0
    return check(a)


if __name__ == "__main__":
    sys.exit(main())
