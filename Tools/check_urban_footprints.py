"""Footprint check for the Urban-collection buildings on the marina map (reads Logs/harbor_dump/marina_01.json from
PortCCTV.EditorTools.HarborDump.RunBatch). For every bld_urban_* prefab: the wrapped bounds (SOURCE_urban.md) x scale, turned by
yaw, against axis-aligned road / street / sidewalk / kerb / lane boxes and against every other building footprint (known keys).
Trees and street lights inside a footprint are counted too. Writes JSON to stdout or to the given path.

usage: python -X utf8 Tools/check_urban_footprints.py [out.json]
"""
import json
import math
import sys

FOOT = {"bld_urban_office_tower": (32.61, 23.17), "bld_urban_midrise": (32.03, 33.32), "bld_urban_ny_hirise": (44.05, 44.05),
        "bld_hirise": (38.0, 38.0), "bld_officetel": (33.0, 26.0), "bld_apt_complex": (26.0, 60.0), "bld_apt_slab": (17.9, 43.4)}
HARD = {"road", "street", "sidewalk", "kerb", "lane", "driveway", "parking", "parking_lines"}


def rect(e):
    if e["shape"] == "prefab":
        fx, fz = FOOT[e["prefab"]]
        s = e["size"]["x"]
        swap = abs((e["yaw_deg"] % 180.0) - 90.0) < 45.0
        hx, hz = (fz if swap else fx) * s / 2, (fx if swap else fz) * s / 2
    else:
        hx, hz = e["size"]["x"] / 2, e["size"]["z"] / 2
    c = e["center"]
    return (c["x"] - hx, c["z"] - hz, c["x"] + hx, c["z"] + hz)


def overlap(a, b):
    w = min(a[2], b[2]) - max(a[0], b[0]); h = min(a[3], b[3]) - max(a[1], b[1])
    return max(w, 0) * max(h, 0)


def main():
    d = json.load(open("Logs/harbor_dump/marina_01.json", encoding="utf-8"))
    els = d["elements"]
    urban = [e for e in els if e.get("prefab", "").startswith("bld_urban")]
    hard = [e for e in els if e["kind"] in HARD and e["shape"] == "box" and abs(e["yaw_deg"]) < 1e-3]
    blds = [e for e in els if e["kind"] == "building" and e.get("prefab") in FOOT]
    small = [e for e in els if e["kind"] in ("tree", "street_light", "bench", "signal")]
    rows = []
    for u in urban:
        r = rect(u)
        hits = [(h["id"], h["kind"], round(overlap(r, rect(h)), 2)) for h in hard if overlap(r, rect(h)) > 0.01]
        bh = [(b["id"], b["prefab"], round(overlap(r, rect(b)), 2)) for b in blds if b is not u and overlap(r, rect(b)) > 0.01]
        inside = [s["kind"] for s in small if r[0] < s["center"]["x"] < r[2] and r[1] < s["center"]["z"] < r[3]]
        rows.append(dict(prefab=u["prefab"], x=u["center"]["x"], z=u["center"]["z"], yaw=u["yaw_deg"], scale=round(u["size"]["x"], 3),
                         rect=[round(v, 2) for v in r], height_m=round({"bld_urban_office_tower": 105, "bld_urban_midrise": 49.5, "bld_urban_ny_hirise": 120}[u["prefab"]] * u["size"]["x"], 1),
                         surface_overlaps=hits, building_overlaps=bh, small_objects_inside=len(inside), small_kinds=sorted(set(inside))))
    out = dict(map="marina_01", urban_buildings=len(rows), with_surface_overlap=sum(1 for r in rows if r["surface_overlaps"]),
               with_building_overlap=sum(1 for r in rows if r["building_overlaps"]), with_small_objects_inside=sum(1 for r in rows if r["small_objects_inside"]),
               rows=rows)
    s = json.dumps(out, indent=1)
    if len(sys.argv) > 1: open(sys.argv[1], "w", encoding="utf-8").write(s)
    print(json.dumps({k: v for k, v in out.items() if k != "rows"}))
    for r in rows:
        if r["surface_overlaps"] or r["building_overlaps"] or r["small_objects_inside"]:
            print(r["prefab"], r["x"], r["z"], r["surface_overlaps"][:4], r["building_overlaps"][:4], r["small_objects_inside"], r["small_kinds"])


if __name__ == "__main__":
    main()
