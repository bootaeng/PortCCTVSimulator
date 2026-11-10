"""Compares re-rendered baseline scenarios (new build) with the captures made by the frozen baseline build.

For each capture name present in both folders: harbor definition hash, camera pose / intrinsics, vessel object ids and boxes
(labels.json), and RGB difference (mean |diff| over all channels, share of pixels with max-channel diff > 8 and > 32, and the
same restricted to vessel-mask pixels). Bit-identical HDRP output is NOT expected (GPU / water / particles); the numbers separate
an unchanged scene with render noise from an actual change of map, camera or assets.

  python -I Tools/research/compare_baseline_rgb.py --baseline Builds/Windows/Captures/apply_after2 --new Builds/Windows/Captures/freeze_rgb_check --out Research/freeze/rgb_compare.json
"""
import argparse
import json
import os
import sys

import numpy as np
from PIL import Image


def load(d, name):
    return json.load(open(os.path.join(d, name), encoding="utf-8"))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--baseline", required=True)
    ap.add_argument("--new", required=True)
    ap.add_argument("--out", required=True)
    a = ap.parse_args()
    rows = []
    for name in sorted(os.listdir(a.new)):
        nd, bd = os.path.join(a.new, name), os.path.join(a.baseline, name)
        if not (os.path.isfile(os.path.join(nd, "metadata.json")) and os.path.isfile(os.path.join(bd, "metadata.json"))):
            continue
        nm, bm = load(nd, "metadata.json"), load(bd, "metadata.json")
        nl, bl = load(nd, "labels.json"), load(bd, "labels.json")
        cam_n, cam_b = nm["camera"], bm["camera"]
        dpos = float(np.linalg.norm(np.array([cam_n["position_world"][k] for k in "xyz"]) - np.array([cam_b["position_world"][k] for k in "xyz"])))
        dfov = abs(cam_n["vertical_fov_deg"] - cam_b["vertical_fov_deg"])
        a_rgb = np.asarray(Image.open(os.path.join(nd, "rgb.png")).convert("RGB"), dtype=np.int16)
        b_rgb = np.asarray(Image.open(os.path.join(bd, "rgb.png")).convert("RGB"), dtype=np.int16)
        diff = np.abs(a_rgb - b_rgb)
        mx = diff.max(axis=2)
        mask_n = np.asarray(Image.open(os.path.join(nd, "mask_instances.png")))[..., 0]
        mask_b = np.asarray(Image.open(os.path.join(bd, "mask_instances.png")))[..., 0]
        vessel = (mask_n > 0) | (mask_b > 0)
        ob = {o["object_id"]: o for o in bl["objects"]}
        box_err = 0.0
        ids_same = sorted(ob) == sorted(o["object_id"] for o in nl["objects"])
        for o in nl["objects"]:
            p = ob.get(o["object_id"])
            if not p:
                continue
            for key in ("full_mesh_box", "above_water_box"):
                for e in ("x_min", "y_min", "x_max", "y_max"):
                    box_err = max(box_err, abs(o[key]["unclipped"][e] - p[key]["unclipped"][e]))
        rows.append({
            "capture": name,
            "harbor_hash_same": nm["harbor_definition_sha256"] == bm["harbor_definition_sha256"],
            "camera_dpos_m": dpos, "camera_dfov_deg": dfov,
            "object_ids_same": ids_same, "max_projected_box_diff_px": box_err,
            "mask_pixel_agreement": float((mask_n == mask_b).mean()),
            "rgb_mean_abs_diff": float(diff.mean()),
            "rgb_share_px_diff_gt8": float((mx > 8).mean()), "rgb_share_px_diff_gt32": float((mx > 32).mean()),
            "rgb_vessel_mean_abs_diff": float(diff[vessel].mean()) if vessel.any() else None,
            "baseline_build": bm["build"]["content_sha256"], "new_build": nm["build"]["content_sha256"],
        })
    out = {"compare_schema": "portcctv.research.rgbcompare.v1", "baseline": a.baseline, "new": a.new, "captures": rows,
           "note": "Same scenario files rendered by two builds. HDRP output is not bit-identical between runs; structure (harbor hash, "
                   "camera, objects, projected boxes, masks) must match exactly, RGB differences should stay at render-noise level."}
    os.makedirs(os.path.dirname(os.path.abspath(a.out)), exist_ok=True)
    with open(a.out, "w", encoding="utf-8", newline="\n") as f:
        json.dump(out, f, indent=1)
    for r in rows:
        print(f"{r['capture']}: harbor {'=' if r['harbor_hash_same'] else 'DIFF'} cam {r['camera_dpos_m']:.1e} m ids {'=' if r['object_ids_same'] else 'DIFF'} "
              f"box {r['max_projected_box_diff_px']:.3f}px mask {r['mask_pixel_agreement']:.5f} rgb mean {r['rgb_mean_abs_diff']:.2f} "
              f">8 {r['rgb_share_px_diff_gt8']:.4f} >32 {r['rgb_share_px_diff_gt32']:.4f}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
