"""Builds the research real-data manifest (schema portcctv.research.realmanifest.v1) from the existing data, READ-ONLY.

Inputs (never modified):
  experiments/oceans_baseline_v2_2026-10-21/{manifest.csv, video_split.csv, original_objects.csv}   SMD-Plus v2 extract
  experiments/oceans_cv4_v2_2026-10-21/folds.csv                                                   identity groups -> CV fold
  simulator/.../docs/data_audit_20261106/{video_inventory.csv, shared_cctv_inventory.json}          video / Korean CCTV inventory
  dataset/external_public/{aboships,aboships_plus,shipsg}/{archive_inventory.csv, extracted annotations}
Output: one JSON file inside the simulator project (default Research/data/real_manifest_v1.json).

What the manifest states and does not state:
  * site / camera / session / capture date are "unknown" unless a source records them; clip count is not a site count.
  * duplicate_group joins copies that must not be counted as independent data (SMD-Plus vs dataset/VIS_Onshore videos,
    ABOships vs ABOships-PLUS images: filename + size + ZIP CRC32, not an image SHA-256 audit).
  * group_id is the strongest known grouping (v2 identity group; ABO recording-time prefix is only a PROXY).
  * availability separates has_media / has_labels / human_reviewed. No label in this manifest is human reviewed.
  * role_hint is the role in the SOURCE (e.g. the v2 split); research roles (train / inner_val / calibration /
    sealed_test) are assigned by the simulator's split planner from the contract, never here.
"""
import argparse
import csv
import datetime
import hashlib
import json
import os
import sys
from collections import defaultdict

SIM = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
WS = os.path.abspath(os.path.join(SIM, "..", ".."))           # DronePerception workspace
V2 = os.path.join(WS, "experiments", "oceans_baseline_v2_2026-10-21")
CV = os.path.join(WS, "experiments", "oceans_cv4_v2_2026-10-21")
EXT = os.path.join(WS, "dataset", "external_public")
AUDIT = os.path.join(SIM, "docs", "data_audit_20261106")
VESSEL_TYPES = {"Boat", "Vessel/ship", "Ferry", "Kayak", "Sail boat"}


def sha_file(p):
    h = hashlib.sha256()
    with open(p, "rb") as f:
        for c in iter(lambda: f.read(1 << 20), b""):
            h.update(c)
    return h.hexdigest()


def rel(p):
    return os.path.relpath(p, WS).replace("\\", "/")


def read_csv(p):
    with open(p, encoding="utf-8-sig", newline="") as f:
        return list(csv.DictReader(f))


def item(**kw):
    base = dict(item_id="", dataset="", dataset_version="", kind="frame", source_file="", clip_id="", frame_index=-1,
                image_sha256="", label_sha256="", width=0, height=0, site="unknown", camera="unknown", session="unknown",
                capture_date="unknown", group_id="", group_confidence="unknown", duplicate_group="", role_hint="",
                fold=-1, exposure=[], has_media=True, has_labels=False, human_reviewed=False,
                label_policy="", license="", usage_restriction="", vessel_boxes=0, boxes=[])
    base.update(kw)
    return base


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", default=os.path.join(SIM, "Research", "data", "real_manifest_v1.json"))
    ap.add_argument("--external-out", default=os.path.join(SIM, "Research", "data", "external_inventory_v1.json"),
                    help="ABOships / ABOships-PLUS / ShipSG image inventory (large; same schema, loaded as a second manifest)")
    ap.add_argument("--skip-external", action="store_true")
    a = ap.parse_args()
    for o in (a.out, a.external_out):
        if os.path.exists(o):
            raise SystemExit("refusing to overwrite " + o)
    inputs, items, datasets = [], [], []

    def use(p):
        inputs.append({"path": rel(p), "sha256": sha_file(p), "bytes": os.path.getsize(p)})
        return p

    # ---------------- SMD-Plus v2 extract (frames with boxes) ----------------
    vsplit = {r["clip"]: r for r in read_csv(use(os.path.join(V2, "video_split.csv")))}
    folds = {r["identity_group"]: int(r["fold"]) for r in read_csv(use(os.path.join(CV, "folds.csv")))}
    boxes = defaultdict(list)
    for r in read_csv(use(os.path.join(V2, "original_objects.csv"))):
        boxes[(r["clip"], int(r["frame"]))].append({
            "source_class": r["original_type"], "x": float(r["x"]), "y": float(r["y"]), "w": float(r["width"]),
            "h": float(r["height"]), "outside": int(r["outside"]), "invalid": int(r["invalid"])})
    for r in read_csv(use(os.path.join(V2, "manifest.csv"))):
        clip, fr = r["clip"], int(r["frame"])
        vs = vsplit[clip]
        g = r["identity_group"]
        exposure = ["v2_test_exposed_in_past_experiments"] if r["split"] == "test" else ["v2_development_cv_pool"]
        bl = boxes.get((clip, fr), [])
        items.append(item(
            item_id=f"smdv2:{clip}:f{fr:05d}", dataset="smd_plus_v2_extract", dataset_version="oceans_baseline_v2_2026-10-21",
            source_file=r["image"], clip_id=clip, frame_index=fr, image_sha256=r["image_sha256"], label_sha256=r["label_sha256"],
            width=int(vs["width"]), height=int(vs["height"]), site=vs["site"], session=vs["session"], capture_date=vs["capture_time"],
            group_id=g, group_confidence="conservative_identity_group(" + vs["constraint_source"] + ")",
            duplicate_group="smd_video:" + clip, role_hint="v2_" + r["split"], fold=folds.get(g, -1), exposure=exposure,
            has_labels=True, human_reviewed=False, label_policy="smd_plus_original_types(Boat,Vessel/ship,Ferry,Kayak,Sail boat,Other,...)",
            license="SMD/SMD-Plus research use (see paper/oceans data notes)", usage_restriction="research",
            vessel_boxes=sum(1 for b in bl if b["source_class"] in VESSEL_TYPES), boxes=bl))
    datasets.append({"dataset": "smd_plus_v2_extract", "note": "612 frames sampled every 30 frames from 36 SMD-Plus VIS_Onshore clips; "
                     "473 frames / 11 identity groups form the CV development pool (folds 1-4); 139 test frames were used in past "
                     "experiments (exposed, never a sealed test); 4 quarantined clips are not in the extract.",
                     "human_review": "200/200 v2 review rows and 70/70 human queue rows pending (docs/data_audit_20261106)"})

    # ---------------- SMD-Plus videos and the duplicate dataset/VIS_Onshore copy ----------------
    for r in read_csv(use(os.path.join(AUDIT, "video_inventory.csv"))):
        clip = r["clip"]
        vs = vsplit.get(clip, {})
        mode = r["mode"]
        items.append(item(
            item_id=f"smdplus_video:{mode}:{clip}", dataset="smd_plus", dataset_version="SMD-Plus " + mode, kind="video",
            source_file=f"dataset/smd_plus/{mode}/{clip}", clip_id=clip, width=int(float(r["width"])), height=int(float(r["height"])),
            group_id=vs.get("identity_group", ""), group_confidence="conservative_identity_group" if vs else "unknown",
            duplicate_group="smd_video:" + clip, role_hint="v2_" + vs["split"] if vs else "not_in_v2",
            exposure=(["onboard_view_not_fixed_cctv"] if mode != "VIS_Onshore" else []),
            has_labels=True, license="SMD research use", usage_restriction="research"))
        if mode == "VIS_Onshore":
            items.append(item(
                item_id=f"vis_onshore_copy:{clip}", dataset="vis_onshore_copy", dataset_version="dataset/VIS_Onshore (older SMD labels)",
                kind="video", source_file=f"dataset/VIS_Onshore/{clip}", clip_id=clip, duplicate_group="smd_video:" + clip,
                group_id=vs.get("identity_group", ""), role_hint="duplicate_of_smd_plus", has_labels=True,
                usage_restriction="same video bytes as SMD-Plus (40/40 SHA-256 match reported by the 2026-11-06 audit; not re-hashed here)"))

    # ---------------- Korean CCTV (videos only, no ground truth) ----------------
    kor = json.load(open(use(os.path.join(AUDIT, "shared_cctv_inventory.json")), encoding="utf-8"))
    for k, r in enumerate(kor):
        items.append(item(
            item_id=f"korean_cctv:{k:02d}", dataset="korean_cctv_shared", dataset_version="Z: share 'CCTV 영상 샘플' (2026-11-06 inventory)",
            kind="video", source_file=r.get("folder", "") + "/" + r.get("file", ""), width=int(r.get("width") or 0), height=int(r.get("height") or 0),
            site="busan" if "부산" in r.get("folder", "") else ("incheon" if "인천" in r.get("folder", "") or "인천" in r.get("file", "") else "unknown"),
            group_id="korean_cctv_folder:" + r.get("folder", ""), group_confidence="folder_only",
            role_hint="unassigned", exposure=["map_design_reference_exposed"], has_labels=False,
            license="user provided; terms not recorded", usage_restriction="pending: owner terms, GT not available"))
    datasets.append({"dataset": "korean_cctv_shared", "note": "video only; no vessel GT; backgrounds were used to design the maps, so "
                     "these sites are never 'unseen' for generalisation protocols"})

    if not a.skip_external:
        # ---------------- ABOships (original) and ABOships-PLUS: same images ----------------
        for ds, folder, lic in (("aboships", "aboships", "CC BY 4.0"), ("aboships_plus", "aboships_plus", "CC BY 4.0")):
            inv = read_csv(use(os.path.join(EXT, folder, "archive_inventory.csv")))
            labelled, split_of = set(), {}
            if ds == "aboships":
                for r in read_csv(use(os.path.join(EXT, folder, "extracted", "ABOshipsDataset", "Labels", "Vesibussi_Labels.csv"))):
                    labelled.add(r["filename"] + ".png")
            else:
                for sp in ("train", "val", "test"):
                    j = json.load(open(use(os.path.join(EXT, folder, "extracted", "ABOships-PLUS", "annotations", sp + ".json")), encoding="utf-8"))
                    for im in j["images"]:
                        labelled.add(im["file_name"]); split_of[im["file_name"]] = sp
            for r in inv:
                n = r["name"]
                if not n.lower().endswith((".png", ".jpg")):
                    continue
                base = os.path.basename(n)
                items.append(item(
                    item_id=f"{ds}:{base}", dataset=ds, dataset_version="zenodo", source_file=f"dataset/external_public/{folder}/extracted/{n}",
                    width=1280, height=720, group_id="abo_recording_prefix:" + base.split("_")[0], group_confidence="proxy_filename_time_prefix",
                    duplicate_group=f"abo_img:{base}:{r['bytes']}:{r['crc32']}", role_hint=("plus_" + split_of[base]) if base in split_of else "unsplit",
                    has_labels=base in labelled, license=lic, usage_restriction="attribution; unlabelled images are NOT negatives without review"))
            datasets.append({"dataset": ds, "license": lic, "note": "image width/height in PLUS JSON are object sizes (fixed in derived adapters only); "
                             "ABOships and PLUS share all 9,880 images (duplicate_group)"})
        # ---------------- ShipSG ----------------
        inv = read_csv(use(os.path.join(EXT, "shipsg", "archive_inventory.csv")))
        sg_labelled = {}
        for sp in ("train", "val"):
            j = json.load(open(use(os.path.join(EXT, "shipsg", "extracted", "ShipSG", sp, "annotations_with_geo.json")), encoding="utf-8"))
            for im in j["images"]:
                sg_labelled["ShipSG/" + sp + "/" + im["file_name"]] = sp
        for r in inv:
            n = r["name"]
            if not n.lower().endswith(".jpg"):
                continue
            items.append(item(
                item_id=f"shipsg:{n}", dataset="shipsg", dataset_version="zenodo 15000157", source_file=f"dataset/external_public/shipsg/extracted/{n}",
                width=2028, height=1520, site="shipsg_port", camera="unknown(two cameras in the source paper)", group_id="shipsg_unknown_group",
                group_confidence="unknown", duplicate_group=f"shipsg_img:{n}:{r['crc32']}", role_hint="shipsg_" + sg_labelled.get(n, "unlisted"),
                has_labels=n in sg_labelled, license="CC BY-NC-ND 4.0", usage_restriction="non-commercial; no redistribution of modified data"))
        datasets.append({"dataset": "shipsg", "license": "CC BY-NC-ND 4.0", "note": "one port, two cameras; per-image camera not recorded"})

    ids = [i["item_id"] for i in items]
    if len(ids) != len(set(ids)):
        raise SystemExit("duplicate item ids")
    ext = {"aboships", "aboships_plus", "shipsg"}
    parts = [(a.out, [i for i in items if i["dataset"] not in ext], [d for d in datasets if d["dataset"] not in ext], "core")]
    if not a.skip_external:
        parts.append((a.external_out, [i for i in items if i["dataset"] in ext], [d for d in datasets if d["dataset"] in ext], "external"))
    for out, its, dss, part in parts:
        man = {"manifest_schema": "portcctv.research.realmanifest.v1", "manifest_version": 1, "manifest_part": part,
               "created_utc": datetime.datetime.now(datetime.timezone.utc).isoformat(),
               "created_by": "Tools/research/import_real_data.py (read-only import)",
               "inputs": inputs, "datasets": dss, "items": its,
               "box_convention": "SMD boxes: x, y = top-left pixel, w, h in source pixels (1920x1080)",
               "statement": "No item is human reviewed; roles are assigned by the split planner, not here."}
        os.makedirs(os.path.dirname(os.path.abspath(out)), exist_ok=True)
        with open(out, "w", encoding="utf-8", newline="\n") as f:
            json.dump(man, f, ensure_ascii=False, separators=(",", ":"))
        by = defaultdict(int)
        for i in its:
            by[i["dataset"]] += 1
        print(part, "items", len(its), dict(by), "->", out, f"{os.path.getsize(out)/1e6:.1f} MB")


if __name__ == "__main__":
    sys.exit(main())
