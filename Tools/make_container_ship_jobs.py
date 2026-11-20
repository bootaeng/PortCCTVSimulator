"""Batch for the loaded container ships (2026-11-16): same-CCTV comparison and container-terminal demos.

1. cmp<cam>_<asset>_<view>: the existing feeder and the three new ships, one at a time, at the same spot seen by an unmodified
   CCTV preset (CMP_CAMERAS: B against the sun, E with the sun behind), bow-oblique (bow 45 deg toward the camera) and broadside.
   GPU mask on, CPU collider mask also written for comparison.
2. demo_berth_<cam>: the default terminal scene with the 262 m loaded ship at the east STS berth instead of the second feeder.
3. demo_transit_<cam>: the default scene plus the two Sketchfab container ships passing outside the breakwater.

The two demo scenarios are also written to Scenarios/ as examples (example_ct_loaded_*.json; not overwritten when present).

usage: python -X utf8 Tools/make_container_ship_jobs.py [run_id]     (default container_ships_20261116_r2)
"""
import json
import math
import os
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
RUN = sys.argv[1] if len(sys.argv) > 1 else "container_ships_20261116_r2"
OUT = os.path.join(ROOT, "Scenarios", RUN)

# comparison cameras (unmodified presets, Runtime/ContainerTerminal01.cs): tag -> (preset, (x, z, yaw), distance of the ship)
# B: breakwater head mast 13.5 m looking south-east at the outer approach (sun ahead, ship sides in shade);
# E: south breakwater mast 13.5 m looking north over the basin (sun behind the camera, sides lit). r1 had B only.
CMP_CAMERAS = {"B": ("ct_B_breakwater_head", (232.0, -463.0, 125.0), 650.0),
               "E": ("ct_E_basin_breakwater", (100.0, -463.0, 345.0), 350.0)}
SHIPS = {  # asset id -> default immersion (catalog)
    "feeder_ship": 4.0, "container_ship_loaded": 11.6, "container_ship_rm02": 11.0, "container_ship_gogi": 9.4,
}


def heading_for(bow_dir):
    """Scenario heading (deg) whose bow points along bow_dir (x, z): heading 0 = bow +X, rotation about Y (Unity, clockwise from above)."""
    return math.degrees(math.atan2(-bow_dir[1], bow_dir[0])) % 360.0


def vessel(oid, asset, x, z, heading, imm, motion=None):
    return {"object_id": oid, "asset_id": asset, "x": round(x, 2), "z": round(z, 2), "heading_deg": round(heading, 2), "immersion_m": imm,
            "motion": motion or {"mode": "none"}}


def scenario(sid, cam, vessels, note, mask=True, cpu=False):
    return {"schema_version": 1, "scenario_id": sid, "harbor_id": "container_terminal_01", "camera_preset_id": cam,
            "environment": {"preset_id": "day_clear", "water_preset_id": "calm", "water_time_s": 12.0},
            "vessels": vessels,
            "capture": {"width": 1920, "height": 1080, "warmup_frames": 6, "write_mask": mask, "compare_cpu_mask": cpu, "mask_stride": 1,
                        "strict_placement": False},
            "notes": note}


def default_fleet():
    """ContainerTerminal01.DefaultScenario() (Runtime/ContainerTerminal01.cs), 2026-11-16."""
    berth = 60 - 1.6
    anchor = {"mode": "anchor"}
    return [
        vessel("v00", "feeder_ship", -270, berth - 7.8, 180, 4.0),
        vessel("v01", "general_cargo", 40, -230, 110, 5.0),
        vessel("v02", "tug_harbor", 150, -200, 120, 6.0),
        vessel("v03", "tug_harbor", -80, -150, 170, 6.0),
        vessel("v04", "rib", -30, -40, 250, 0.1),
        vessel("v05", "barge", 100, -90, 10, 0.15),
        vessel("v06", "tanker", 420, -1150, 80, 6.0, dict(anchor)),
        vessel("v07", "pilot_boat", -100, -90, 75, 1.1),
        vessel("v08", "police_boat", 45, -130, 255, 0.6),
        vessel("v09", "barge", 100, -950, 65, 0.15, dict(anchor)),
        vessel("v11", "feeder_ship", 150, berth - 7.8, 180, 4.0),
        vessel("v10", "feeder_ship", 880, -1150, 245, 4.0),
        vessel("v12", "bulk_carrier", -350, -1350, 60, 7.0, dict(anchor)),
    ]


def main():
    os.makedirs(OUT, exist_ok=True)
    jobs = []
    placed = {}
    for tag, (cam, (cx, cz, yaw), dist) in CMP_CAMERAS.items():
        view = (math.sin(math.radians(yaw)), math.cos(math.radians(yaw)))          # camera forward on the water (x, z)
        px, pz = cx + dist * view[0], cz + dist * view[1]
        side = (-view[1], view[0])                                                 # perpendicular to the view
        bow_oblique = ((-view[0] + side[0]) / math.sqrt(2), (-view[1] + side[1]) / math.sqrt(2))
        views = {"bow": heading_for(bow_oblique), "side": heading_for(side)}
        placed[tag] = (round(px, 1), round(pz, 1), {k: round(v, 1) for k, v in views.items()})
        for asset, imm in SHIPS.items():
            for vname, hd in views.items():
                sid = f"cmp{tag}_{asset}_{vname}"
                s = scenario(sid, cam, [vessel("v00", asset, px, pz, hd, imm)],
                             f"Same CCTV framing ({cam}, unmodified preset), {dist:.0f} m out on the view axis; {vname}: "
                             + ("bow 45 deg toward the camera" if vname == "bow" else "broadside"), mask=True, cpu=True)
                json.dump(s, open(os.path.join(OUT, sid + ".json"), "w", encoding="utf-8"), indent=1)
                jobs.append({"job_id": sid, "scenario_path": sid + ".json"})

    # berth: the 262 m ship (beam 32.66 m) alongside the east STS quay, fender 1.6 m, in place of the second feeder
    berth = [v for v in default_fleet() if v["object_id"] != "v11"]
    berth.append(vessel("v11", "container_ship_loaded", 150, 60 - 1.6 - 32.66 / 2, 180, 11.6))
    # transit: the two Sketchfab container ships in the outer approach, heading west / north-west past the breakwater
    transit = default_fleet()
    transit.append(vessel("v13", "container_ship_rm02", 640, -653, 170, 11.0))
    transit.append(vessel("v14", "container_ship_gogi", 682, -1242, 165, 9.4))
    demos = {"berth": (berth, ["ct_E_basin_breakwater", "ct_D_hill_tower"], "Loaded 262 m container ship at the east STS berth (default traffic otherwise)"),
             "transit": (transit, ["ct_B_breakwater_head", "ct_C_admin_roof"], "Two loaded container ships passing outside the breakwater (default traffic otherwise)")}
    for name, (fleet, cams, note) in demos.items():
        for cam in cams:
            sid = f"demo_{name}_{cam.split('_')[1]}"
            json.dump(scenario(sid, cam, fleet, note), open(os.path.join(OUT, sid + ".json"), "w", encoding="utf-8"), indent=1)
            jobs.append({"job_id": sid, "scenario_path": sid + ".json"})
        ex = os.path.join(ROOT, "Scenarios", f"example_ct_loaded_{name}.json")
        if not os.path.exists(ex):
            s = scenario(f"example_ct_loaded_{name}", cams[0], fleet, note + " (example scene)", mask=False)
            json.dump(s, open(ex, "w", encoding="utf-8"), indent=1)
    batch = {"batch_schema": "portcctv.batch.v1", "batch_id": RUN, "output_dir": f"Captures/{RUN}", "jobs": jobs}
    json.dump(batch, open(os.path.join(OUT, "jobs.json"), "w", encoding="utf-8"), indent=1)
    print(OUT, len(jobs), "jobs; comparison placements", placed)


if __name__ == "__main__":
    main()
