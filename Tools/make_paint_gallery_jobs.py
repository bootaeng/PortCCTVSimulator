"""Batch for a per-vessel paint gallery: every catalog vessel alone on open water (container terminal outer basin), 3/4 view,
once with original colours and with random paint for several seeds. Camera distance follows the vessel length.

usage: python -X utf8 Tools/make_paint_gallery_jobs.py <scenario_dir> <capture_dir_relative_to_app_root> [seeds...]
"""
import json
import math
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
VESSELS = os.path.join(ROOT, "Assets", "PortCCTVSimulator", "Vessels")
CATALOG = os.path.join(ROOT, "Assets", "PortCCTVSimulator", "Resources", "PortCCTV", "VesselCatalog.asset")


def vec(s, key):
    m = re.search(key + r": \{x: ([-\d.e]+), y: ([-\d.e]+), z: ([-\d.e]+)\}", s)
    return [float(v) for v in m.groups()]


def main(out_dir, cap_out, seeds):
    os.makedirs(out_dir, exist_ok=True)
    cat = open(CATALOG, encoding="utf-8").read()
    ids = re.findall(r"\n  - asset_id: (\S+)", cat)
    imm = [float(x) for x in re.findall(r"\n    default_immersion_m: ([-\d.e]+)", cat)]
    jobs = []
    for aid, im in zip(ids, imm):
        g = open(os.path.join(VESSELS, f"vessel_{aid}_geometry.asset"), encoding="utf-8").read()
        bmin, bmax = vec(g, "bounds_min"), vec(g, "bounds_max")
        L = bmax[0] - bmin[0]
        H = bmax[1] - bmin[1]
        vfov = 18.0
        hfov = 2 * math.degrees(math.atan(math.tan(math.radians(vfov / 2)) * 16 / 9))
        d = max(L * 0.62 / math.tan(math.radians(hfov / 2)), H * 0.75 / math.tan(math.radians(vfov / 2)), 6.0)
        vx, vz, heading = 0.0, -1500.0, 25.0
        cy = max(1.5, 0.12 * d + 0.3 * H)
        look_y = 0.3 * H
        pitch = math.degrees(math.atan2(cy - look_y, d))
        for mode in ["original"] + [f"random:{s}" for s in seeds]:
            paint, seed = (mode, 1) if mode == "original" else ("random", int(mode.split(":")[1]))
            sid = f"{aid}__{'orig' if paint == 'original' else 's' + str(seed)}"
            sc = {
                "schema_version": 1, "scenario_id": sid, "harbor_id": "container_terminal_01", "camera_preset_id": "ct_A_quay_east_mast",
                "camera_override": {"enabled": True, "position": {"x": vx, "y": cy, "z": vz - d}, "euler_deg": {"x": pitch, "y": 0, "z": 0}, "vertical_fov_deg": vfov},
                "environment": {"preset_id": "day_clear", "water_preset_id": "calm", "water_time_s": 12.0, "lights": "off",
                                "weather": {"precipitation": "none", "intensity": 0.5, "overcast": False, "visibility_scale": 1.0, "wetness": -1, "snow_cover": -1, "seed": 0}},
                "vessels": [{"object_id": "v00", "asset_id": aid, "x": vx, "z": vz, "heading_deg": heading, "immersion_m": im, "paint": "",
                             "motion": {"mode": "none", "speed_kn": 0, "route": [], "loop": True, "keep_in_view": True, "seed": 0}}],
                "capture": {"width": 960, "height": 540, "warmup_frames": 6, "write_mask": True, "mask_source": "gpu", "compare_cpu_mask": False, "mask_stride": 1, "strict_placement": False},
                "appearance": {"paint": paint, "paint_seed": seed},
                "notes": f"paint gallery: {aid} L={L:.1f} m",
            }
            json.dump(sc, open(os.path.join(out_dir, sid + ".json"), "w", encoding="utf-8"), indent=1)
            jobs.append({"job_id": sid, "scenario_path": sid + ".json", "use_inline": False})
    batch = {"batch_schema": "portcctv.batch.v1", "batch_id": os.path.basename(os.path.normpath(out_dir)), "output_dir": cap_out, "jobs": jobs}
    json.dump(batch, open(os.path.join(out_dir, "jobs.json"), "w", encoding="utf-8"), indent=1)
    print(len(ids), "vessels,", len(jobs), "jobs")


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2], [int(s) for s in sys.argv[3:]] or [1, 2, 3])
