"""Write a decimated copy of an untextured GLB (quadric simplification per primitive, fast-simplification).
Keeps the node hierarchy, transforms and materials; each primitive keeps POSITION + indices only (normals are recomputed by
the importer). Primitives under `keep` triangles are copied unchanged. Usage: decimate_glb.py <in.glb> <out.glb> <reduction> [keep]
reduction = fraction of triangles to remove (e.g. 0.85)."""
import hashlib
import json
import struct
import sys

import fast_simplification
import numpy as np

src, dst, red = sys.argv[1], sys.argv[2], float(sys.argv[3])
keep = int(sys.argv[4]) if len(sys.argv) > 4 else 60
b = open(src, 'rb').read()
jl = struct.unpack('<I', b[12:16])[0]; g = json.loads(b[20:20 + jl]); bl = struct.unpack('<I', b[20 + jl:24 + jl])[0]
bin0 = b[28 + jl:28 + jl + bl]
CT = {5121: np.uint8, 5123: np.uint16, 5125: np.uint32, 5126: np.float32}
NC = {'SCALAR': 1, 'VEC2': 2, 'VEC3': 3, 'VEC4': 4}


def read(ai):
    a = g['accessors'][ai]; bv = g['bufferViews'][a['bufferView']]
    n = NC[a['type']]; dt = np.dtype(CT[a['componentType']]); off = bv.get('byteOffset', 0) + a.get('byteOffset', 0)
    stride = bv.get('byteStride', 0)
    if stride and stride != n * dt.itemsize:
        raw = np.frombuffer(bin0, np.uint8, stride * a['count'], off).reshape(a['count'], stride)
        return np.ascontiguousarray(raw[:, :n * dt.itemsize]).view(dt).reshape(a['count'], n)
    return np.frombuffer(bin0, dt, a['count'] * n, off).reshape(a['count'], n).copy()


nb = bytearray(); views = []; accs = []


def write(arr, target, typ, comp):
    while len(nb) % 4: nb.append(0)
    off = len(nb); nb.extend(arr.tobytes())
    views.append({'buffer': 0, 'byteOffset': off, 'byteLength': arr.nbytes, 'target': target})
    acc = {'bufferView': len(views) - 1, 'componentType': comp, 'count': int(arr.shape[0] if typ == 'VEC3' else arr.size), 'type': typ}
    if typ == 'VEC3': acc['min'] = arr.min(0).tolist(); acc['max'] = arr.max(0).tolist()
    accs.append(acc); return len(accs) - 1


t_in = t_out = 0
for m in g['meshes']:
    for pr in m['primitives']:
        P = read(pr['attributes']['POSITION']).astype(np.float32)
        F = read(pr['indices']).reshape(-1, 3).astype(np.int64) if 'indices' in pr else np.arange(len(P)).reshape(-1, 3)
        t_in += len(F)
        if len(F) > keep:
            P2, F2 = fast_simplification.simplify(P, F, target_reduction=red)
            if len(F2) >= 4: P, F = P2.astype(np.float32), F2.astype(np.int64)
        # drop unreferenced vertices
        used, inv = np.unique(F.ravel(), return_inverse=True)
        P = P[used]; F = inv.reshape(-1, 3)
        t_out += len(F)
        pr['attributes'] = {'POSITION': write(np.ascontiguousarray(P), 34962, 'VEC3', 5126)}
        pr['indices'] = write(np.ascontiguousarray(F.astype(np.uint32).ravel()), 34963, 'SCALAR', 5125)
        pr.pop('targets', None)
g['bufferViews'] = views; g['accessors'] = accs
g['buffers'] = [{'byteLength': len(nb)}]
for k in ('images', 'textures', 'samplers'): g.pop(k, None)
for mat in g.get('materials', []):
    pbr = mat.get('pbrMetallicRoughness', {})
    for t in ('baseColorTexture', 'metallicRoughnessTexture'): pbr.pop(t, None)
    for t in ('normalTexture', 'occlusionTexture', 'emissiveTexture'): mat.pop(t, None)
g.setdefault('asset', {})['extras'] = {'derived_by': 'PortCCTVSimulator Tools/decimate_glb.py', 'source_sha256': hashlib.sha256(b).hexdigest(), 'reduction': red}
js = json.dumps(g, separators=(',', ':')).encode()
while len(js) % 4: js += b' '
while len(nb) % 4: nb.append(0)
out = struct.pack('<III', 0x46546C67, 2, 12 + 8 + len(js) + 8 + len(nb)) + struct.pack('<II', len(js), 0x4E4F534A) + js + struct.pack('<II', len(nb), 0x004E4942) + bytes(nb)
open(dst, 'wb').write(out)
print(f'triangles {t_in} -> {t_out} ({t_out / t_in:.1%}); {len(b) / 1e6:.1f} MB -> {len(out) / 1e6:.1f} MB; sha256 {hashlib.sha256(out).hexdigest()}')
