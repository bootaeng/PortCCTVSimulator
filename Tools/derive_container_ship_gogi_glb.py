"""Lighter derived copy of gogiart's "Container Ship 03 FBX" GLB (ThirdParty/SketchfabCCBY/ships/SOURCE_container_ships.md).

The 1k download has 1.27 M triangles; about 90 % sit in a few small detail parts (stern mooring deck, winches, railings) and in
one flat deck plane, which at CCTV distances cover a handful of pixels. The capture pipeline renders, masks, collides and labels
the same meshes (no runtime LOD switch, so RGB and labels cannot disagree), so the reduction is done once here:

- every primitive with more than DECIMATE_ABOVE triangles is welded by position and reduced with quadric edge collapse
  (fast_simplification, open borders kept so flat plates do not open holes) to KEEP_FRACTION of its triangles (at least KEEP_MIN); UVs follow the collapse map, normals are left
  out so glTFast computes flat normals (hard-surface look kept);
- a primitive above the threshold whose whole surface is smaller than DROP_AREA_M2 at ship scale (stern deck gratings / bolt
  clusters: 2-5 m2 carrying 10^4-10^5 triangles, which the simplifier cannot reduce) is removed: it covers less than one pixel
  in every CCTV view used here and would otherwise dominate the triangle count;
- the hull, superstructure and container stacks (the silhouette) are below the threshold and stay exactly as authored;
- materials that are identical apart from their name are merged.

The GLB binary buffer is rebuilt with only the buffer views still referenced. Writes derive_log_container_ship_gogi.json with
per-primitive triangle counts before / after.

usage: python -X utf8 Tools/derive_container_ship_gogi_glb.py
"""
import hashlib
import json
import os
import struct
import sys

import numpy as np
import fast_simplification

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from glb_ship_metrics import load, acc, mat4  # noqa: E402

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SRC = os.path.join(ROOT, "ThirdParty_raw", "container_ships_20261116", "container_ship_03_agt14032013_1k.glb")
DST_DIR = os.path.join(ROOT, "Assets", "PortCCTVSimulator", "ThirdParty", "SketchfabCCBY", "ships")
DST = os.path.join(DST_DIR, "container_ship_03_gogiart_opt.glb")
DECIMATE_ABOVE = 4000
KEEP_FRACTION = 0.10
KEEP_MIN = 1500
SCALE_M = 34.0                 # HullSpec scale (container tier 0.0762 units = 2.59 m)
DROP_AREA_M2 = 10.0            # dense micro-detail (bolts, gratings) smaller than this in total surface is removed, see docstring


def write_glb(js, bin_):
    j = json.dumps(js, separators=(",", ":")).encode(); j += b" " * ((4 - len(j) % 4) % 4)
    bin_ += b"\0" * ((4 - len(bin_) % 4) % 4)
    return struct.pack("<III", 0x46546C67, 2, 12 + 8 + len(j) + 8 + len(bin_)) + struct.pack("<II", len(j), 0x4E4F534A) + j + struct.pack("<II", len(bin_), 0x004E4942) + bin_


def main():
    raw = open(SRC, "rb").read()
    js, bin_ = load(SRC)
    new_chunks = []   # (accessor index, bytes) appended later
    log = dict(source=os.path.relpath(SRC, ROOT).replace("\\", "/"), source_sha256=hashlib.sha256(raw).hexdigest(),
               decimate_above=DECIMATE_ABOVE, keep_fraction=KEEP_FRACTION, keep_min=KEEP_MIN, primitives=[])

    def add_accessor(arr, ctype, typ, target):
        data = np.ascontiguousarray(arr).tobytes()
        js["bufferViews"].append(dict(buffer=0, byteLength=len(data), target=target, _new=len(new_chunks)))
        new_chunks.append(data)
        a = dict(bufferView=len(js["bufferViews"]) - 1, componentType=ctype, count=int(arr.shape[0]) if typ != "SCALAR" else int(arr.size), type=typ)
        if typ == "VEC3": a["min"] = arr.min(0).tolist(); a["max"] = arr.max(0).tolist()
        js["accessors"].append(a)
        return len(js["accessors"]) - 1

    # world matrix of the (first) node using each mesh: areas and weld tolerances are measured after node transforms
    mesh_world = {}
    def walk(i, P):
        nd = js["nodes"][i]; M = P @ mat4(nd)
        if "mesh" in nd: mesh_world.setdefault(nd["mesh"], M)
        for c in nd.get("children", []): walk(c, M)
    for r in js["scenes"][js.get("scene", 0)]["nodes"]: walk(r, np.eye(4))

    tri_before = tri_after = 0
    for mi, mesh in enumerate(js["meshes"]):
        M = mesh_world.get(mi, np.eye(4))
        for pr in mesh["primitives"]:
            idx = acc(js, bin_, pr["indices"]).reshape(-1, 3) if "indices" in pr else None
            pos = acc(js, bin_, pr["attributes"]["POSITION"])
            if idx is None: idx = np.arange(len(pos)).reshape(-1, 3)
            n = len(idx); tri_before += n
            if n <= DECIMATE_ABOVE:
                tri_after += n; continue
            wp = (M @ np.c_[pos, np.ones(len(pos))].T).T[:, :3]
            e1 = wp[idx[:, 1]] - wp[idx[:, 0]]; e2 = wp[idx[:, 2]] - wp[idx[:, 0]]
            area_m2 = float(np.linalg.norm(np.cross(e1, e2), axis=1).sum() / 2) * SCALE_M * SCALE_M
            mname = js["materials"][pr["material"]]["name"] if "material" in pr else None
            if area_m2 < DROP_AREA_M2:
                pr["_drop"] = True
                log["primitives"].append(dict(mesh=mesh.get("name", str(mi)), material=mname, triangles_before=int(n), triangles_after=0, area_m2=round(area_m2, 2), action="removed (micro-detail)"))
                continue
            uv = acc(js, bin_, pr["attributes"]["TEXCOORD_0"]) if "TEXCOORD_0" in pr["attributes"] else None
            # weld by position so the simplifier sees connected surfaces (the export splits every face)
            tol = 1e-5 * float(np.linalg.norm(pos.max(0) - pos.min(0)))   # 1e-5 of the part size (coincident face corners)
            key = np.round(pos / tol).astype(np.int64)
            _, first, inv = np.unique(key, axis=0, return_index=True, return_inverse=True)
            inv = inv.reshape(-1)
            wpos = pos[first]; wtri = inv[idx]
            wtri = wtri[(wtri[:, 0] != wtri[:, 1]) & (wtri[:, 1] != wtri[:, 2]) & (wtri[:, 0] != wtri[:, 2])]
            target = max(KEEP_MIN, int(round(n * KEEP_FRACTION)))
            if len(wtri) <= target:
                tri_after += n; continue
            _, _, coll = fast_simplification.simplify(wpos.astype(np.float64), wtri.astype(np.int32), target_count=target, agg=5.0, return_collapses=True, preserve_border=True)
            npos, ntri, mapping = fast_simplification.replay_simplification(wpos.astype(np.float32), wtri.astype(np.int32), coll)
            npos = np.asarray(npos, np.float32); ntri = np.asarray(ntri, np.uint32)
            nuv = None
            if uv is not None:
                wuv = uv[first]
                nuv = np.zeros((len(npos), 2), np.float32); seen = np.zeros(len(npos), bool)
                for o in range(len(mapping)):
                    j = mapping[o]
                    if 0 <= j < len(npos) and not seen[j]: nuv[j] = wuv[o]; seen[j] = True
            # unshare vertices so every face keeps its own normal (glTFast computes flat normals when NORMAL is absent)
            fpos = npos[ntri.reshape(-1)]
            attrs = {"POSITION": add_accessor(fpos, 5126, "VEC3", 34962)}
            if nuv is not None: attrs["TEXCOORD_0"] = add_accessor(nuv[ntri.reshape(-1)], 5126, "VEC2", 34962)
            pr["attributes"] = attrs
            pr.pop("indices", None)
            tri_after += len(ntri)
            log["primitives"].append(dict(mesh=mesh.get("name", str(mi)), material=mname, triangles_before=int(n), triangles_after=int(len(ntri)),
                                          area_m2=round(area_m2, 1), action="decimated"))

    # remove dropped primitives; meshes left empty are removed and their nodes lose the mesh reference
    mesh_map = {}; new_meshes = []
    for mi, mesh in enumerate(js["meshes"]):
        mesh["primitives"] = [pr for pr in mesh["primitives"] if not pr.get("_drop")]
        if mesh["primitives"]: mesh_map[mi] = len(new_meshes); new_meshes.append(mesh)
    for nd in js["nodes"]:
        if "mesh" in nd:
            if nd["mesh"] in mesh_map: nd["mesh"] = mesh_map[nd["mesh"]]
            else: del nd["mesh"]
    js["meshes"] = new_meshes
    tri_after = 0
    for me in js["meshes"]:
        for pr in me["primitives"]:
            tri_after += (js["accessors"][pr["indices"]]["count"] if "indices" in pr else js["accessors"][pr["attributes"]["POSITION"]]["count"]) // 3

    # merge materials that differ only by name
    canon = {}; remap = {}
    for i, m in enumerate(js["materials"]):
        k = json.dumps({a: b for a, b in m.items() if a != "name"}, sort_keys=True)
        remap[i] = canon.setdefault(k, i)
    for mesh in js["meshes"]:
        for pr in mesh["primitives"]:
            if "material" in pr: pr["material"] = remap[pr["material"]]
    used_mats = sorted({pr["material"] for me in js["meshes"] for pr in me["primitives"] if "material" in pr})
    log["materials_before"] = len(js["materials"]); log["materials_after"] = len(used_mats)

    # rebuild the binary buffer with the buffer views still referenced (accessors, images)
    used_acc = set()
    for me in js["meshes"]:
        for pr in me["primitives"]:
            used_acc.update(pr["attributes"].values())
            if "indices" in pr: used_acc.add(pr["indices"])
    used_bv = {js["accessors"][a]["bufferView"] for a in used_acc} | {im["bufferView"] for im in js.get("images", []) if "bufferView" in im}
    out_bin = b""; bv_map = {}; new_bvs = []
    for i, v in enumerate(js["bufferViews"]):
        if i not in used_bv: continue
        chunk = new_chunks[v["_new"]] if "_new" in v else bin_[v.get("byteOffset", 0): v.get("byteOffset", 0) + v["byteLength"]]
        out_bin += b"\0" * ((4 - len(out_bin) % 4) % 4)
        nv = {k: val for k, val in v.items() if k != "_new"}; nv["byteOffset"] = len(out_bin); nv["byteLength"] = len(chunk)
        bv_map[i] = len(new_bvs); new_bvs.append(nv); out_bin += chunk
    acc_map = {}; new_acc = []
    for i, a in enumerate(js["accessors"]):
        if i not in used_acc: continue
        a = dict(a); a["bufferView"] = bv_map[a["bufferView"]]; acc_map[i] = len(new_acc); new_acc.append(a)
    for me in js["meshes"]:
        for pr in me["primitives"]:
            pr["attributes"] = {k: acc_map[v] for k, v in pr["attributes"].items()}
            if "indices" in pr: pr["indices"] = acc_map[pr["indices"]]
    for im in js.get("images", []):
        if "bufferView" in im: im["bufferView"] = bv_map[im["bufferView"]]
    js["bufferViews"] = new_bvs; js["accessors"] = new_acc; js["buffers"][0]["byteLength"] = len(out_bin)

    out = write_glb(js, out_bin)
    os.makedirs(DST_DIR, exist_ok=True)
    open(DST, "wb").write(out)
    log.update(triangles_before=int(tri_before), triangles_after=int(tri_after), output=os.path.relpath(DST, ROOT).replace("\\", "/"),
               output_sha256=hashlib.sha256(out).hexdigest(), output_bytes=len(out))
    json.dump(log, open(os.path.join(DST_DIR, "derive_log_container_ship_gogi.json"), "w", encoding="utf-8"), indent=1)
    print(log["output"], "tris", tri_before, "->", tri_after, "materials", log["materials_before"], "->", log["materials_after"], log["output_sha256"], len(out))


if __name__ == "__main__":
    main()
