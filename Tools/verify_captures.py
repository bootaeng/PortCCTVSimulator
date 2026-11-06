"""Independent checker for PortCCTVSimulator capture folders (reads files only, never writes into them).

Usage:
    python -X utf8 Tools/verify_captures.py <capture_dir_or_batch_dir> [--json report.json]

Checks per capture folder (a folder containing metadata.json):
  * required files exist; metadata.complete is true; listed SHA256 match file bytes
  * rgb.png / mask_instances.png size == metadata camera width/height
  * mask values only in {0} U {instance_index of objects}
  * labels.json visible_mask.pixel_count and bbox recomputed from mask_instances.png
  * label status flags: provisional_diagnostic, training_ready == false
  * YOLO provisional lines == objects with a non-empty visible bbox, values in [0,1]
  * camera intrinsics consistent with vertical FOV
Exit code 0 if every folder passes, 1 otherwise.
"""
import argparse
import hashlib
import json
import math
import sys
from pathlib import Path

import numpy as np
from PIL import Image

REQUIRED = ["rgb.png", "labels.json", "metadata.json", "scenario.json", "overlay.png", "yolo_visible_bbox_provisional.txt"]


def sha256(p: Path) -> str:
    return hashlib.sha256(p.read_bytes()).hexdigest()


def check_capture(d: Path) -> dict:
    errs, notes = [], []
    meta = json.loads((d / "metadata.json").read_text(encoding="utf-8"))
    for f in REQUIRED:
        if not (d / f).exists():
            errs.append(f"missing {f}")
    if not meta.get("complete"):
        errs.append("metadata.complete is not true")
    for name, h in zip(meta.get("files", []), meta.get("file_sha256", [])):
        p = d / name
        if not p.exists():
            errs.append(f"listed file missing {name}")
        elif sha256(p) != h:
            errs.append(f"sha mismatch {name}")
    cam = meta["camera"]
    W, H = cam["width"], cam["height"]
    rgb = Image.open(d / "rgb.png")
    if rgb.size != (W, H):
        errs.append(f"rgb size {rgb.size} != {(W, H)}")
    arr = np.asarray(rgb.convert("RGB"))
    notes.append(f"rgb mean={arr.mean():.1f} std={arr.std():.1f}")
    if arr.std() < 2:
        errs.append("rgb looks blank (std<2)")
    fy = H / (2 * math.tan(math.radians(cam["vertical_fov_deg"]) / 2))
    if abs(fy - cam["fy_px"]) > 1e-2 * fy:
        errs.append("fy inconsistent with vFOV")

    labels = json.loads((d / "labels.json").read_text(encoding="utf-8"))
    if labels.get("label_status") != "provisional_diagnostic" or labels.get("training_ready") is not False:
        errs.append("label status flags wrong")
    if meta.get("training_ready") is not False:
        errs.append("metadata training_ready must be false")
    objs = labels["objects"]
    mask_path = d / "mask_instances.png"
    if mask_path.exists():
        m = np.asarray(Image.open(mask_path).convert("RGBA"))[:, :, 0]
        if m.shape != (H, W):
            errs.append(f"mask shape {m.shape}")
        valid = {0} | {o["instance_index"] for o in objs}
        extra = set(np.unique(m).tolist()) - valid
        if extra:
            errs.append(f"mask has unknown ids {sorted(extra)}")
        for o in objs:
            vm = o["visible_mask"]
            ys, xs = np.nonzero(m == o["instance_index"])
            if len(xs) != vm["pixel_count"]:
                errs.append(f"{o['object_id']}: pixel_count {vm['pixel_count']} != mask {len(xs)}")
            if len(xs):
                bb = (xs.min(), ys.min(), xs.max() + 1, ys.max() + 1)
                got = (vm["bbox"]["x_min"], vm["bbox"]["y_min"], vm["bbox"]["x_max"], vm["bbox"]["y_max"])
                if any(abs(a - b) > 1e-6 for a, b in zip(bb, got)):
                    errs.append(f"{o['object_id']}: bbox {got} != mask {bb}")
                # visible pixels must lie inside the full-mesh box (allow 1 px for ray vs vertex sampling)
                fb = o["full_mesh_box"]["clipped"]
                if fb["valid"] and (bb[0] < fb["x_min"] - 1 or bb[1] < fb["y_min"] - 1 or bb[2] > fb["x_max"] + 1 or bb[3] > fb["y_max"] + 1):
                    errs.append(f"{o['object_id']}: visible bbox outside full-mesh box")
    yolo = [l for l in (d / "yolo_visible_bbox_provisional.txt").read_text(encoding="utf-8").splitlines() if l.strip()]
    n_vis = sum(1 for o in objs if o["visible_mask"]["computed"] and o["visible_mask"]["bbox"]["valid"])
    if len(yolo) != n_vis:
        errs.append(f"yolo lines {len(yolo)} != visible objects {n_vis}")
    for l in yolo:
        v = [float(x) for x in l.split()[1:]]
        if any(x < 0 or x > 1 for x in v):
            errs.append("yolo value out of [0,1]")
    summary = [
        f"{o['object_id']}:{o['asset_id']} vis={o['visible_mask']['pixel_count']} "
        f"full={'Y' if o['full_mesh_box']['clipped']['valid'] else '-'} trunc={o['full_mesh_box']['truncated_by_image_border']} "
        f"warn={len(o['placement_warnings'])}"
        for o in objs
    ]
    return {"dir": str(d), "ok": not errs, "errors": errs, "notes": notes, "objects": summary,
            "env": meta["environment"]["preset_id"], "camera": cam["preset_id"],
            "water_time": meta["environment"].get("water_time_actual_s")}


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("path")
    ap.add_argument("--json")
    a = ap.parse_args()
    root = Path(a.path)
    dirs = sorted({p.parent for p in root.rglob("metadata.json") if ".partial_" not in str(p.parent)})
    partials = sorted({str(p) for p in root.rglob("*") if p.is_dir() and ".partial_" in p.name})
    results = [check_capture(d) for d in dirs]
    for r in results:
        print(("OK  " if r["ok"] else "FAIL") + f" {Path(r['dir']).name} [{r['camera']}, {r['env']}, water_t={r['water_time']}] " + "; ".join(r["notes"]))
        for o in r["objects"]:
            print("      " + o)
        for e in r["errors"]:
            print("      ERROR " + e)
    print(f"\n{sum(r['ok'] for r in results)}/{len(results)} capture folders passed; partial (failed/incomplete) folders: {len(partials)}")
    for p in partials:
        print("  partial: " + p)
    if a.json:
        Path(a.json).write_text(json.dumps({"results": results, "partials": partials}, indent=1), encoding="utf-8")
    sys.exit(0 if results and all(r["ok"] for r in results) else 1)


if __name__ == "__main__":
    main()
