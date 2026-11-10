"""Creates tampered copies of a frozen research job list to test that the batch runner refuses them (never skip, never overwrite).

  A  contract hash changed in every job, same output folder as the original     -> every existing capture: hash_mismatch
  C  one job whose scenario file was edited after freezing, fresh output folder  -> failed: scenario file changed after freezing

Only writes the new job folders given on the command line; the original job list and its captures are not touched.
  python -I Tools/research/make_tamper_cases.py --jobs <frozen jobs.json> --out-a <dir> --out-c <dir>
"""
import argparse
import json
import os
import shutil
import sys


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--jobs", required=True)
    ap.add_argument("--out-a", required=True)
    ap.add_argument("--out-c", required=True)
    a = ap.parse_args()
    src = os.path.dirname(os.path.abspath(a.jobs))
    for d in (a.out_a, a.out_c):
        if os.path.exists(d):
            raise SystemExit("refusing to overwrite " + d)
    batch = json.load(open(a.jobs, encoding="utf-8"))

    # A: contract hash changed, same output folder
    shutil.copytree(os.path.join(src, "scenarios"), os.path.join(a.out_a, "scenarios"))
    ba = json.loads(json.dumps(batch))
    ba["batch_id"] += "_tamperA"
    for j in ba["jobs"]:
        j["research"]["contract_sha256"] = "0" * 64
    json.dump(ba, open(os.path.join(a.out_a, "jobs.json"), "w", encoding="utf-8"), indent=1)

    # C: first planned job, scenario edited after freezing, fresh output folder
    first = next(j for j in batch["jobs"] if j["research"]["plan_status"] == "planned")
    os.makedirs(os.path.join(a.out_c, "scenarios"))
    sc = json.load(open(os.path.join(src, first["scenario_path"]), encoding="utf-8"))
    sc["notes"] += " | edited after freezing (tamper test)"
    sc["vessels"][0]["x"] += 5.0
    json.dump(sc, open(os.path.join(a.out_c, first["scenario_path"]), "w", encoding="utf-8"), indent=4)
    bc = {k: v for k, v in batch.items() if k != "jobs"}
    bc["batch_id"] += "_tamperC"
    bc["output_dir"] = batch["output_dir"].replace("/captures", "/captures_tamperC")
    bc["jobs"] = [first]
    json.dump(bc, open(os.path.join(a.out_c, "jobs.json"), "w", encoding="utf-8"), indent=1)
    print("A:", os.path.join(a.out_a, "jobs.json"), len(ba["jobs"]), "jobs; C:", os.path.join(a.out_c, "jobs.json"), first["job_id"])
    return 0


if __name__ == "__main__":
    sys.exit(main())
