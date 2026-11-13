"""Render jobs for the Urban-collection marina buildings: the existing close view (Scenarios/beach_preview_20261113/scenario_close.json,
unchanged) plus the three marina CCTV presets, a ground-level view from the beach towards the front row and a top-down view of
the beach-city plots (road / sidewalk overlap check).

usage: python -X utf8 Tools/make_urban_collection_jobs.py <scenario_dir> <capture_dir_rel_to_app_root>
"""
import copy
import json
import os
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
BASE = os.path.join(ROOT, "Scenarios", "beach_preview_20261113", "scenario_close.json")


def main(out_dir, cap_out):
    os.makedirs(out_dir, exist_ok=True)
    base = json.load(open(BASE, encoding="utf-8"))
    views = {
        "urban_close": None,                                                     # the user's close view, as saved
        "preset_mr_B_tower": ("mr_B_tower", None),
        "preset_mr_A_promenade": ("mr_A_promenade", None),
        "preset_mr_C_breakwater": ("mr_C_breakwater", None),
        "ground_front_row": ("mr_B_tower", dict(position=dict(x=470, y=6, z=40), euler_deg=dict(x=-8, y=8, z=0), vertical_fov_deg=60)),
        "ground_front_row_east": ("mr_B_tower", dict(position=dict(x=700, y=6, z=55), euler_deg=dict(x=-10, y=-12, z=0), vertical_fov_deg=60)),
        "topdown_beach_city": ("mr_B_tower", dict(position=dict(x=580, y=650, z=200), euler_deg=dict(x=90, y=0, z=0), vertical_fov_deg=40)),
    }
    jobs = []
    for jid, v in views.items():
        s = copy.deepcopy(base)
        s["scenario_id"] = jid
        if v is not None:
            preset, ov = v
            s["camera_preset_id"] = preset
            s["camera_override"] = dict(enabled=ov is not None, **(ov or dict(position=dict(x=0, y=0, z=0), euler_deg=dict(x=0, y=0, z=0), vertical_fov_deg=40)))
        json.dump(s, open(os.path.join(out_dir, jid + ".json"), "w", encoding="utf-8"), indent=1)
        jobs.append({"job_id": jid, "scenario_path": jid + ".json"})
    json.dump({"batch_schema": "portcctv.batch.v1", "batch_id": os.path.basename(os.path.normpath(out_dir)), "output_dir": cap_out, "jobs": jobs},
              open(os.path.join(out_dir, "jobs.json"), "w", encoding="utf-8"), indent=1)
    print(len(jobs), "jobs")


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])
