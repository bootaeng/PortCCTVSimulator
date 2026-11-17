"""Geometry metrics of a ship GLB (read only), used to set the HullSpec scale / yaw / immersion of a new hull.

- bounds in glTF units (Y up) after node transforms, triangle counts per material;
- where along the long axis the highest geometry stands (accommodation block -> stern side);
- per material: y range and area (antifouling paint top = loaded waterline candidate);
- optional container analysis: connected components (shared vertex index) of the given materials, their box sizes
  (a 40 ft container is 12.19 x 2.44 x 2.59 m, a 20 ft one 6.06 m long).

usage: python -X utf8 Tools/glb_ship_metrics.py <glb> [--containers MATNAME_SUBSTR ...]
"""
import collections
import json
import struct
import sys

import numpy as np

CT = {5120: np.int8, 5121: np.uint8, 5122: np.int16, 5123: np.uint16, 5125: np.uint32, 5126: np.float32}
NC = {"SCALAR": 1, "VEC2": 2, "VEC3": 3, "VEC4": 4}


def load(p):
    b = open(p, "rb").read()
    off, js, bin_ = 12, None, b""
    while off < len(b):
        ln, typ = struct.unpack_from("<II", b, off)
        c = b[off + 8: off + 8 + ln]
        if typ == 0x4E4F534A: js = json.loads(c)
        elif typ == 0x004E4942: bin_ = c
        off += 8 + ln
    return js, bin_


def acc(js, bin_, i):
    a = js["accessors"][i]
    bv = js["bufferViews"][a["bufferView"]]
    n, k = a["count"], NC[a["type"]]
    dt = np.dtype(CT[a["componentType"]])
    stride = bv.get("byteStride", 0) or dt.itemsize * k
    start = bv.get("byteOffset", 0) + a.get("byteOffset", 0)
    raw = np.frombuffer(bin_, dtype=np.uint8, count=stride * (n - 1) + dt.itemsize * k, offset=start)
    out = np.lib.stride_tricks.as_strided(raw.view(dt) if stride % dt.itemsize == 0 else raw, shape=(n, k), strides=(stride, dt.itemsize)) if stride % dt.itemsize == 0 else None
    if out is None: raise ValueError("unaligned accessor")
    return np.array(out, dtype=np.float64 if a["componentType"] == 5126 else np.int64)


def mat4(n):
    if "matrix" in n: return np.array(n["matrix"], float).reshape(4, 4).T
    t = np.array(n.get("translation", [0, 0, 0]), float); x, y, z, w = n.get("rotation", [0, 0, 0, 1]); s = np.array(n.get("scale", [1, 1, 1]), float)
    R = np.array([[1 - 2 * (y * y + z * z), 2 * (x * y - z * w), 2 * (x * z + y * w)], [2 * (x * y + z * w), 1 - 2 * (x * x + z * z), 2 * (y * z - x * w)], [2 * (x * z - y * w), 2 * (y * z + x * w), 1 - 2 * (x * x + y * y)]])
    M = np.eye(4); M[:3, :3] = R * s; M[:3, 3] = t; return M


def prims(js, bin_):
    out = []
    def walk(i, P):
        n = js["nodes"][i]; M = P @ mat4(n)
        if "mesh" in n:
            for pr in js["meshes"][n["mesh"]]["primitives"]:
                if pr.get("mode", 4) != 4: continue
                v = acc(js, bin_, pr["attributes"]["POSITION"])
                v = (M @ np.c_[v, np.ones(len(v))].T).T[:, :3]
                idx = acc(js, bin_, pr["indices"]).reshape(-1) if "indices" in pr else np.arange(len(v))
                m = js["materials"][pr["material"]]["name"] if "material" in pr else "-"
                out.append((n.get("name", "?"), m, v, idx.reshape(-1, 3)))
        for c in n.get("children", []): walk(c, M)
    for s in js["scenes"][js.get("scene", 0)]["nodes"]: walk(s, np.eye(4))
    return out


def components(tris, nv):
    parent = np.arange(nv)
    def find(a):
        while parent[a] != a: parent[a] = parent[parent[a]]; a = parent[a]
        return a
    for t in tris:
        r = [find(x) for x in t]
        for x in r[1:]: parent[x] = r[0]
    roots = np.array([find(i) for i in range(nv)])
    return roots


def main():
    p = sys.argv[1]
    cont = sys.argv[sys.argv.index("--containers") + 1:] if "--containers" in sys.argv else []
    js, bin_ = load(p)
    ps = prims(js, bin_)
    allv = np.concatenate([v for _, _, v, _ in ps])
    lo, hi = allv.min(0), allv.max(0)
    size = hi - lo
    long_axis = int(np.argmax([size[0], 0, size[2]]))
    print("bounds", np.round(lo, 4), np.round(hi, 4), "size", np.round(size, 4), "long axis", "xyz"[long_axis])
    tri = collections.Counter(); yr = {}
    for nm, m, v, t in ps:
        tri[m] += len(t)
        a = yr.setdefault(m, [np.inf, -np.inf, 0.0])
        a[0] = min(a[0], v[:, 1].min()); a[1] = max(a[1], v[:, 1].max())
        e1 = v[t[:, 1]] - v[t[:, 0]]; e2 = v[t[:, 2]] - v[t[:, 0]]
        a[2] += float(np.linalg.norm(np.cross(e1, e2), axis=1).sum() / 2)
    print("triangles total", sum(tri.values()), "materials", len(tri))
    for m, c in tri.most_common():
        print(f"  {c:9d} tris  y {yr[m][0]:9.4f}..{yr[m][1]:9.4f}  area {yr[m][2]:10.3f}  {m}")
    # highest geometry position along the long axis (top 2 % of vertices by height)
    h = allv[:, 1]; top = allv[h >= np.quantile(h, 0.98)]
    L = allv[:, long_axis]
    print("top-2% vertices along long axis: mean", round(float(top[:, long_axis].mean()), 4), "range", round(float(L.min()), 4), "..", round(float(L.max()), 4),
          "-> accommodation at the", "max" if top[:, long_axis].mean() > (L.min() + L.max()) / 2 else "min", "end (stern side)")
    if cont:
        sizes = []
        for nm, m, v, t in ps:
            if not any(c.lower() in m.lower() for c in cont): continue
            roots = components(t, len(v))
            used = np.unique(t.reshape(-1))
            for r in np.unique(roots[used]):
                vs = v[(roots == r)]
                if len(vs) < 8: continue
                s = np.sort(vs.max(0) - vs.min(0))[::-1]
                sizes.append(s)
        if sizes:
            S = np.array(sizes)
            print("container components", len(S), "median sorted box (long, mid, short)", np.round(np.median(S, 0), 4))
            hist = collections.Counter(tuple(np.round(s, 2)) for s in S)
            for k, c in hist.most_common(6): print("   ", c, k)


if __name__ == "__main__":
    main()
