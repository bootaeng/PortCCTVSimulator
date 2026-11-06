"""Grid-clustering decimation of a Poly Haven glTF (POSITION/NORMAL/TEXCOORD_0 + indices) -> new .gltf/.bin next to the original.
Usage: decimate_gltf.py <in.gltf> <out_dir> <cell_size_m>
Texture references are kept (relative 'textures/...' paths). Materials unchanged."""
import json, struct, sys, hashlib
from pathlib import Path
import numpy as np

src = Path(sys.argv[1]); out = Path(sys.argv[2]); cell = float(sys.argv[3])
g = json.loads(src.read_text(encoding="utf-8"))
bin_path = src.parent / g["buffers"][0]["uri"]
buf = bin_path.read_bytes()
CT = {5120: np.int8, 5121: np.uint8, 5122: np.int16, 5123: np.uint16, 5125: np.uint32, 5126: np.float32}
NC = {"SCALAR": 1, "VEC2": 2, "VEC3": 3, "VEC4": 4}


def read(acc_i):
    a = g["accessors"][acc_i]; bv = g["bufferViews"][a["bufferView"]]
    n = NC[a["type"]]; dt = CT[a["componentType"]]
    off = bv.get("byteOffset", 0) + a.get("byteOffset", 0)
    stride = bv.get("byteStride", 0)
    if stride and stride != n * np.dtype(dt).itemsize:
        raw = np.frombuffer(buf, dtype=np.uint8, count=stride * a["count"], offset=off).reshape(a["count"], stride)
        return np.ascontiguousarray(raw[:, :n * np.dtype(dt).itemsize]).view(dt).reshape(a["count"], n)
    return np.frombuffer(buf, dtype=dt, count=a["count"] * n, offset=off).reshape(a["count"], n).copy()


new_bin = bytearray(); accessors = []; views = []


def write(arr, target, acc_type, comp):
    arr = np.ascontiguousarray(arr)
    while len(new_bin) % 4: new_bin.append(0)
    off = len(new_bin); new_bin.extend(arr.tobytes())
    views.append({"buffer": 0, "byteOffset": off, "byteLength": arr.nbytes, "target": target})
    acc = {"bufferView": len(views) - 1, "componentType": comp, "count": int(arr.shape[0]), "type": acc_type}
    if acc_type == "VEC3" and comp == 5126:
        acc["min"] = arr.min(0).tolist(); acc["max"] = arr.max(0).tolist()
    accessors.append(acc); return len(accessors) - 1


tot_in = tot_out = 0
for mesh in g["meshes"]:
    for prim in mesh["primitives"]:
        P = read(prim["attributes"]["POSITION"]).astype(np.float64)
        N = read(prim["attributes"]["NORMAL"]).astype(np.float32) if "NORMAL" in prim["attributes"] else None
        T = read(prim["attributes"]["TEXCOORD_0"]).astype(np.float32) if "TEXCOORD_0" in prim["attributes"] else None
        I = read(prim["indices"]).reshape(-1).astype(np.int64)
        key = np.floor(P / cell).astype(np.int64)
        _, inv = np.unique(key, axis=0, return_inverse=True)
        inv = inv.reshape(-1)
        k = inv.max() + 1
        # representative: mean position, first vertex's normal/uv
        sums = np.zeros((k, 3)); cnt = np.zeros(k)
        np.add.at(sums, inv, P); np.add.at(cnt, inv, 1)
        Pn = (sums / cnt[:, None]).astype(np.float32)
        first = np.full(k, -1, dtype=np.int64)
        order = np.arange(len(inv))[::-1]
        first[inv[order]] = order
        Nn = N[first] if N is not None else None
        Tn = T[first] if T is not None else None
        tri = inv[I].reshape(-1, 3)
        keep = (tri[:, 0] != tri[:, 1]) & (tri[:, 1] != tri[:, 2]) & (tri[:, 0] != tri[:, 2])
        tri = tri[keep]
        # drop unused vertices
        used = np.unique(tri); remap = np.full(k, -1, dtype=np.int64); remap[used] = np.arange(len(used))
        tri = remap[tri]; Pn = Pn[used]; Nn = Nn[used] if Nn is not None else None; Tn = Tn[used] if Tn is not None else None
        tot_in += len(P); tot_out += len(Pn)
        attrs = {"POSITION": write(Pn, 34962, "VEC3", 5126)}
        if Nn is not None:
            Nn = Nn / np.maximum(np.linalg.norm(Nn, axis=1, keepdims=True), 1e-9)
            attrs["NORMAL"] = write(Nn.astype(np.float32), 34962, "VEC3", 5126)
        if Tn is not None: attrs["TEXCOORD_0"] = write(Tn, 34962, "VEC2", 5126)
        prim["attributes"] = attrs
        prim["indices"] = write(tri.reshape(-1).astype(np.uint32), 34963, "SCALAR", 5125)
        prim.pop("extensions", None)

g["accessors"] = accessors; g["bufferViews"] = views
out.mkdir(parents=True, exist_ok=True)
name = src.stem + "_dec"
(out / (name + ".bin")).write_bytes(bytes(new_bin))
g["buffers"] = [{"uri": name + ".bin", "byteLength": len(new_bin)}]
g.setdefault("asset", {})["generator"] = "PortCCTVSimulator decimate_gltf.py (grid clustering)"
(out / (name + ".gltf")).write_text(json.dumps(g), encoding="utf-8")
# copy textures
import shutil
if (src.parent / "textures").exists():
    shutil.copytree(src.parent / "textures", out / "textures", dirs_exist_ok=True)
print(f"{src.stem}: {tot_in} -> {tot_out} vertices, {len(new_bin)/1e6:.1f} MB; source gltf sha256 {hashlib.sha256(src.read_bytes()).hexdigest()[:16]}")
