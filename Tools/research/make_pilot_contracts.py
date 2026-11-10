"""Turns the template contract written by the app (-research-new-contract) into the two pilot contracts:

  pilot_contract.json       3 maps x 2 existing cameras x {G, C} x seeds {1, 2} = 24 captures (cap 24), C from the FIXTURE profile
  pilot_diag_contract.json  diagnostics (cap 12): G + C_U on 2 cameras, 2 empty-harbour negatives, 3 paired factor variants,
                            1 sequence episode of 3 frames, 1 deliberately unreachable request (planning_failed, no render)

Never overwrites an existing contract.
  python -I Tools/research/make_pilot_contracts.py --template <template.json> --profile <profile.json> --out-dir <dir>
"""
import argparse
import copy
import hashlib
import json
import os
import sys


def sha(path):
    return hashlib.sha256(open(path, "rb").read()).hexdigest()


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--template", required=True)
    ap.add_argument("--profile", required=True, help="profile file (path as seen from the app root)")
    ap.add_argument("--profile-rel", default="Research/profiles/profile_fixture_v1.json")
    ap.add_argument("--out-dir", required=True)
    ap.add_argument("--experiment-id", default="pilot_20261110")
    a = ap.parse_args()
    t = json.load(open(a.template, encoding="utf-8"))
    for name in ("pilot_contract.json", "pilot_diag_contract.json"):
        if os.path.exists(os.path.join(a.out_dir, name)):
            raise SystemExit("refusing to overwrite " + name)

    main_c = copy.deepcopy(t)
    main_c["experiment_id"] = a.experiment_id
    main_c["output_dir"] = "Captures/research/" + a.experiment_id
    g = main_c["generation"]
    g["modes"] = ["G", "C"]
    g["seeds"] = [1, 2]
    g["captures_per_cell"] = 1
    g["c_profile_path"] = a.profile_rel
    g["c_profile_sha256"] = sha(a.profile)
    g["render_budget"]["max_captures_this_run"] = 24
    main_c["notes"] = ("Technical pilot: 3 maps x 2 existing cameras x G/C x 2 seeds. C uses the FIXTURE profile (real profile approval pending). "
                       "No detector training, no test evaluation.")

    d = copy.deepcopy(main_c)
    d["experiment_id"] = a.experiment_id + "_diag"
    d["output_dir"] = "Captures/research/" + d["experiment_id"]
    g = d["generation"]
    g["modes"] = ["G", "C_U"]
    g["seeds"] = [5]
    g["maps"] = [{"harbor_id": "container_terminal_01", "camera_preset_ids": ["ct_B_breakwater_head"]},
                 {"harbor_id": "marina_01", "camera_preset_ids": ["mr_C_breakwater"]}]
    g["empty_negatives_per_camera"] = 1
    g["boundary_unreachable_cases"] = 1
    g["paired"] = [{"factor": "lighting", "values": ["night_clear"]}, {"factor": "sensor", "values": ["low_quality"]},
                   {"factor": "fog_visibility_scale", "values": ["0.3"]}]
    g["sequence"]["episodes"] = 1
    g["sequence"]["frames_per_episode"] = 3
    g["sequence"]["interval_s"] = 4.0
    g["render_budget"]["max_captures_this_run"] = 12
    d["notes"] = "Diagnostics for the tooling (boundary / paired / negative / sequence); not part of any comparison."

    os.makedirs(a.out_dir, exist_ok=True)
    for name, c in (("pilot_contract.json", main_c), ("pilot_diag_contract.json", d)):
        with open(os.path.join(a.out_dir, name), "w", encoding="utf-8", newline="\n") as f:
            json.dump(c, f, indent=4, ensure_ascii=False)
        print("wrote", os.path.join(a.out_dir, name))
    return 0


if __name__ == "__main__":
    sys.exit(main())
