"""Offline layout check of the harbour maps (dev tool, 2026-11-09).

Reads Logs/harbor_dump/<map>.json (written by PortCCTV.EditorTools.HarborDump.RunBatch) and reports
  * dead ends: an end of a road / lane / path strip that does not meet any other paved surface
    (road, lane, path, sidewalk, pad, parking, quay, pier, breakwater crown, market, ...) and is not at the map edge;
  * coplanar overlaps: two paved boxes whose top faces are at the same height (+-2 cm) and overlap by more than
    4 m2 without being a plain crossing of two strips (these z-fight / look like one asset dropped on another);
  * lighthouse access: lighthouses whose paved surface is not connected (through touching paved boxes) to the
    landside paved network (quay / roads).
Usage: python -X utf8 Tools/check_layout.py [map ...]   (default: the three maps)
"""
import json
import math
import sys
from collections import defaultdict

from shapely.geometry import Point, Polygon, box as sbox
from shapely.affinity import rotate
from shapely.strtree import STRtree

STRIP_KINDS = {"road", "lane", "path"}
PAVED_KINDS = STRIP_KINDS | {"sidewalk", "pad", "parking", "quay", "pier", "breakwater", "market", "loading_pad", "slipway",
                             "apron", "shop_forecourt", "fuel_station", "bridge", "causeway", "walkway", "promenade", "plaza",
                             "terrace_pad", "yard", "gate", "dry_storage", "land", "terrace", "driveway"}
PAVED_MATS = {"road_asphalt", "lane_concrete", "concrete", "paving", "asphalt", "sidewalk_paving"}
MAP_EDGE = 1350.0


def poly(e):
    c, s = e["center"], e["size"]
    p = sbox(c["x"] - s["x"] / 2, c["z"] - s["z"] / 2, c["x"] + s["x"] / 2, c["z"] + s["z"] / 2)
    yaw = e.get("yaw_deg", 0.0) + (e.get("euler_deg") or {}).get("y", 0.0)
    if abs(yaw) > 1e-3:
        # Unity yaw rotates +x towards -z (clockwise seen from above with z up the page)
        p = rotate(p, -yaw, origin=(c["x"], c["z"]))
    return p


def top(e):
    return e["center"]["y"] + e["size"]["y"] / 2


def is_paved(e):
    if e["shape"] != "box":
        return False
    if e["kind"] in ("road",) and e.get("material") != "road_asphalt":
        return False                       # paint markings
    if e["kind"] in ("land", "terrace", "breakwater") and e.get("material") not in PAVED_MATS:
        return False                       # grass land, rubble cores
    return e["kind"] in PAVED_KINDS and (e.get("material") in PAVED_MATS or e["kind"] in STRIP_KINDS)


def ends(e):
    """Two end-cap centre points of a strip (along its long axis)."""
    c, s = e["center"], e["size"]
    yaw = math.radians(e.get("yaw_deg", 0.0))
    if s["x"] >= s["z"]:
        ax, half = (math.cos(yaw), -math.sin(yaw)), s["x"] / 2
    else:
        ax, half = (math.sin(yaw), math.cos(yaw)), s["z"] / 2
    return [(c["x"] + ax[0] * half * sg, c["z"] + ax[1] * half * sg, sg) for sg in (-1, 1)], ax


def check(name):
    d = json.load(open(f"Logs/harbor_dump/{name}.json", encoding="utf-8"))
    els = d["elements"]
    paved = [e for e in els if is_paved(e)]
    polys = [poly(e) for e in paved]
    tree = STRtree(polys)
    report = {"map": name, "dead_ends": [], "overlaps": [], "lighthouses": []}

    # dead ends
    for i, e in enumerate(paved):
        if e["kind"] not in STRIP_KINDS:
            continue
        pts, ax = ends(e)
        width = min(e["size"]["x"], e["size"]["z"])
        for x, z, sg in pts:
            if abs(x) > MAP_EDGE or abs(z) > MAP_EDGE:
                continue
            probe = Point(x + ax[0] * sg * 1.0, z + ax[1] * sg * 1.0).buffer(max(0.6, width * 0.3))
            hit = False
            for j in tree.query(probe):
                if j == i:
                    continue
                if polys[j].intersects(probe):
                    hit = True
                    break
            if not hit:
                report["dead_ends"].append({"id": e["id"], "kind": e["kind"], "x": round(x, 1), "z": round(z, 1), "width": round(width, 1),
                                            "length": round(max(e["size"]["x"], e["size"]["z"]), 1)})

    # coplanar overlaps (excluding strip x strip crossings and strip-on-pad junction ends)
    seen = set()
    for i, e in enumerate(paved):
        for j in tree.query(polys[i]):
            if j <= i or (i, j) in seen:
                continue
            seen.add((i, j))
            f = paved[j]
            if abs(top(e) - top(f)) > 0.02:
                continue
            inter = polys[i].intersection(polys[j])
            if inter.area < 4.0:
                continue
            a, b = e["kind"] in STRIP_KINDS, f["kind"] in STRIP_KINDS
            if a and b:
                # crossing: overlap bounded by both widths -> fine; parallel overlap -> report
                wa, wb = min(e["size"]["x"], e["size"]["z"]), min(f["size"]["x"], f["size"]["z"])
                if inter.area <= wa * wb * 1.05 + 1.0:
                    continue
            report["overlaps"].append({"a": e["id"], "b": f["id"], "kinds": [e["kind"], f["kind"]],
                                       "area": round(inter.area, 1), "x": round(inter.centroid.x, 1), "z": round(inter.centroid.y, 1)})

    # lighthouse access: connected components of touching paved boxes
    adj = defaultdict(set)
    for i in range(len(paved)):
        for j in tree.query(polys[i].buffer(0.3)):
            if j != i and polys[i].buffer(0.3).intersects(polys[j]):
                adj[i].add(j)
    comp = {}
    cid = 0
    for i in range(len(paved)):
        if i in comp:
            continue
        stack = [i]
        comp[i] = cid
        while stack:
            k = stack.pop()
            for n in adj[k]:
                if n not in comp:
                    comp[n] = cid
                    stack.append(n)
        cid += 1
    # a lighthouse is reachable when its paved surface is connected to the landside network (any quay, land or road box)
    landside = {comp[i] for i, e in enumerate(paved) if e["kind"] in ("quay", "land", "road")}
    for e in els:
        if e["kind"] == "lighthouse" and e["id"].endswith("_base"):
            p = Point(e["center"]["x"], e["center"]["z"])
            under = [i for i in tree.query(p.buffer(2.0)) if polys[i].intersects(p.buffer(2.0))]
            comps = {comp[i] for i in under}
            report["lighthouses"].append({"id": e["id"], "x": round(p.x, 1), "z": round(p.y, 1), "on_paved": bool(under),
                                          "connected_to_main": bool(comps & landside),
                                          "under": [paved[i]["id"] for i in under]})
    return report


if __name__ == "__main__":
    maps = sys.argv[1:] or ["container_terminal_01", "fishing_port_01", "marina_01"]
    out = [check(m) for m in maps]
    for r in out:
        print(f"== {r['map']}: dead ends {len(r['dead_ends'])}, overlaps {len(r['overlaps'])}, lighthouses "
              f"{sum(1 for l in r['lighthouses'] if not l['connected_to_main'])}/{len(r['lighthouses'])} not connected")
    json.dump(out, open("Logs/layout_check.json", "w", encoding="utf-8"), indent=1)
