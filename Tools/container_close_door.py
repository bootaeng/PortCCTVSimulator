"""Closes the half-open door leaf of the Classic Shipping Container model (2026-11-09).
The model (Sebastian Webster, CC BY 4.0) shows one leaf shut and the other swung ~145 deg outward; in a yard stack that leaf
would stick out of the box. The open leaf's vertices (beyond the closed door plane x > 2.335 on the +z half, glTF frame) are
rotated about the vertical hinge line back into the door plane; normals / tangents are rotated with them. In place on the
derived GLB written by glb_derive.py (single mesh, float POSITION / NORMAL / TANGENT accessors).
Usage: container_close_door.py <derived.glb>"""
import json, math, struct, sys, hashlib
import numpy as np

p = sys.argv[1]
b = bytearray(open(p, 'rb').read())
jl = struct.unpack('<I', b[12:16])[0]; g = json.loads(b[20:20 + jl]); bin_off = 20 + jl + 8
pr = g['meshes'][0]['primitives'][0]


def view(ai):
    a = g['accessors'][ai]; bv = g['bufferViews'][a['bufferView']]
    n = {'VEC3': 3, 'VEC4': 4}[a['type']]
    off = bin_off + bv.get('byteOffset', 0) + a.get('byteOffset', 0)
    return off, a['count'], n


sys.path.insert(0, __import__('os').path.dirname(__file__))
from process_manabashima_lighthouse import node_mat
M = None


def find(ni, Pm):
    global M
    nd = g['nodes'][ni]; Mm = Pm @ node_mat(nd)
    if nd.get('mesh') == 0: M = Mm
    for ch in nd.get('children', []): find(ch, Mm)


for r in g['scenes'][g.get('scene', 0)]['nodes']: find(r, np.eye(4))
Minv = np.linalg.inv(M)
po, cnt, _ = view(pr['attributes']['POSITION'])
Pl = np.frombuffer(bytes(b[po:po + cnt * 12]), np.float32).reshape(cnt, 3).astype(np.float64)
P = (M[:3, :3] @ Pl.T).T + M[:3, 3]
sel = (P[:, 0] > 2.335) & (P[:, 2] > 0.55)
L = P[sel]
# hinge: the leaf corner closest to the door frame corner (x 2.33, z 0.77)
hinge = np.array([2.33, 0.0, 0.70])
d = L[:, [0, 2]] - hinge[[0, 2]]
far = d[np.argmax(np.hypot(d[:, 0], d[:, 1]))]
a_open = math.atan2(far[1], far[0]); a_closed = math.atan2(-1.0, 0.0)          # shut: from the hinge towards -z
th = a_closed - a_open
c, s = math.cos(th), math.sin(th)


def rot(v):            # rotation in the x-z plane about the hinge (vertical axis)
    x, z = v[:, 0] - hinge[0], v[:, 2] - hinge[2]
    out = v.copy(); out[:, 0] = hinge[0] + c * x - s * z; out[:, 2] = hinge[2] + s * x + c * z
    return out


def rotdir(v):
    out = v.copy(); out[:, 0] = c * v[:, 0] - s * v[:, 2]; out[:, 2] = s * v[:, 0] + c * v[:, 2]
    return out


P[sel] = rot(L)
Pl = (Minv[:3, :3] @ P.T).T + Minv[:3, 3]
b[po:po + cnt * 12] = Pl.astype(np.float32).tobytes()
R3 = M[:3, :3] / np.linalg.norm(M[:3, :3], axis=0)
for key in ('NORMAL', 'TANGENT'):
    if key not in pr['attributes']: continue
    o, n_, k = view(pr['attributes'][key])
    A = np.frombuffer(bytes(b[o:o + n_ * k * 4]), np.float32).reshape(n_, k).copy()
    W = (R3 @ A[sel, :3].T).T
    A[sel, :3] = (R3.T @ rotdir(W).T).T
    b[o:o + n_ * k * 4] = A.astype(np.float32).tobytes()
acc = g['accessors'][pr['attributes']['POSITION']]
acc['min'] = Pl.min(0).astype(float).tolist(); acc['max'] = Pl.max(0).astype(float).tolist()
js = json.dumps(g, separators=(',', ':')).encode()
while len(js) % 4: js += b' '
binchunk = bytes(b[20 + jl:])
out = b'glTF' + struct.pack('<II', 2, 12 + 8 + len(js) + len(binchunk)) + struct.pack('<I', len(js)) + b'JSON' + js + binchunk
open(p, 'wb').write(out)
print(json.dumps({'leaf_vertices': int(sel.sum()), 'rotation_deg': round(math.degrees(th), 1), 'new_bounds': [P.min(0).round(3).tolist(), P.max(0).round(3).tolist()], 'sha256': hashlib.sha256(out).hexdigest()}))
