"""Derived copy of a textured GLB for the simulator (2026-11-09).
  * primitives with more than --decimate-over triangles are simplified (quadric, fast-simplification) to --keep fraction;
    every attribute (NORMAL, TEXCOORD_n, TANGENT, COLOR) is carried from the nearest original vertex, so the texture mapping
    stays (good enough for small, dense parts such as track links);
  * embedded images resized to at most --max-px (JPEG for opaque images, PNG with alpha);
  * --inpaint "img:x0,y0,x1,y1;..." removes marks (e.g. a manufacturer logo) inside the given pixel rectangles of the original
    image: the white / dark logo pixels in the rectangle are found by colour distance from the rectangle's dominant paint
    colour and filled with OpenCV Telea inpainting; the surrounding paint, dirt and wear stay.
Usage: glb_derive.py <in.glb> <out.glb> [--max-px 1024] [--decimate-over 20000] [--keep 0.12] [--inpaint "11:195,60,350,290"]
Prints a JSON report (triangles before/after per primitive, images changed, inpainted pixels)."""
import argparse, io, json, struct, hashlib
import numpy as np
from PIL import Image

ap = argparse.ArgumentParser()
ap.add_argument('src'); ap.add_argument('dst')
ap.add_argument('--max-px', type=int, default=1024)
ap.add_argument('--decimate-over', type=int, default=20000)
ap.add_argument('--keep', type=float, default=0.12)
ap.add_argument('--inpaint', default='')
A = ap.parse_args()

b = open(A.src, 'rb').read()
jl = struct.unpack('<I', b[12:16])[0]; g = json.loads(b[20:20 + jl])
bl = struct.unpack('<I', b[20 + jl:24 + jl])[0]; bin0 = b[28 + jl:28 + jl + bl]
CT = {5120: np.int8, 5121: np.uint8, 5122: np.int16, 5123: np.uint16, 5125: np.uint32, 5126: np.float32}
NC = {'SCALAR': 1, 'VEC2': 2, 'VEC3': 3, 'VEC4': 4}
report = {'src_sha256': hashlib.sha256(b).hexdigest(), 'primitives': [], 'images': [], 'inpaint': []}


def read(ai):
    a = g['accessors'][ai]; bv = g['bufferViews'][a['bufferView']]
    n = NC[a['type']]; dt = np.dtype(CT[a['componentType']]); off = bv.get('byteOffset', 0) + a.get('byteOffset', 0)
    stride = bv.get('byteStride', 0)
    if stride and stride != n * dt.itemsize:
        raw = np.frombuffer(bin0, np.uint8, stride * (a['count'] - 1) + n * dt.itemsize, off)
        return np.lib.stride_tricks.as_strided(raw, shape=(a['count'], n * dt.itemsize), strides=(stride, 1)).copy().view(dt).reshape(a['count'], n)
    return np.frombuffer(bin0, dt, a['count'] * n, off).reshape(a['count'], n).copy()


# ---- new buffer: images first (possibly re-encoded), then all accessors (rewritten)
nb = bytearray(); new_views = []; new_accs = []


def add_view(data, target=None):
    while len(nb) % 4: nb.append(0)
    off = len(nb); nb.extend(data)
    v = {'buffer': 0, 'byteOffset': off, 'byteLength': len(data)}
    if target: v['target'] = target
    new_views.append(v); return len(new_views) - 1


def add_acc(arr, typ, comp, target, normalized=False):
    vi = add_view(np.ascontiguousarray(arr).tobytes(), target)
    a = {'bufferView': vi, 'componentType': comp, 'count': int(arr.shape[0]), 'type': typ}
    if normalized: a['normalized'] = True
    if typ == 'VEC3' and comp == 5126: a['min'] = arr.min(0).astype(float).tolist(); a['max'] = arr.max(0).astype(float).tolist()
    new_accs.append(a); return len(new_accs) - 1


inp = {}
for part in [p for p in A.inpaint.split(';') if p.strip()]:
    k, r = part.split(':'); inp.setdefault(int(k), []).append(tuple(int(v) for v in r.split(',')))

for ii, im in enumerate(g.get('images', [])):
    bv = g['bufferViews'][im['bufferView']]; data = bin0[bv.get('byteOffset', 0): bv.get('byteOffset', 0) + bv['byteLength']]
    img = Image.open(io.BytesIO(data)); w, h = img.size; changed = False
    if ii in inp:
        import cv2
        rgb = np.asarray(img.convert('RGB')).copy()
        mask = np.zeros(rgb.shape[:2], np.uint8)
        for (x0, y0, x1, y1) in inp[ii]:
            reg = rgb[y0:y1, x0:x1].astype(np.float32)
            # dominant paint colour in the rectangle = the median of its saturated pixels
            mx, mn = reg.max(2), reg.min(2); sat = (mx - mn) / np.maximum(mx, 1)
            paint = np.median(reg[sat > 0.45], axis=0) if (sat > 0.45).mean() > 0.4 else np.median(reg.reshape(-1, 3), axis=0)
            dist = np.linalg.norm(reg - paint[None, None, :], axis=2)
            m = (dist > 80).astype(np.uint8) * 255
            m = cv2.dilate(m, np.ones((5, 5), np.uint8))
            mask[y0:y1, x0:x1] = np.maximum(mask[y0:y1, x0:x1], m)
            report['inpaint'].append({'image': ii, 'rect': [x0, y0, x1, y1], 'paint_rgb': paint.round(1).tolist(), 'pixels': int((m > 0).sum())})
        rgb = cv2.inpaint(rgb, mask, 7, cv2.INPAINT_TELEA)
        img = Image.fromarray(rgb); changed = True
    if max(w, h) > A.max_px:
        k = A.max_px / max(w, h); img = img.resize((max(1, round(w * k)), max(1, round(h * k))), Image.LANCZOS); changed = True
    if changed:
        out = io.BytesIO()
        if img.mode in ('RGBA', 'LA') and img.getextrema()[-1][0] < 255: img.save(out, 'PNG', optimize=True); im['mimeType'] = 'image/png'
        else: img.convert('RGB').save(out, 'JPEG', quality=90); im['mimeType'] = 'image/jpeg'
        data = out.getvalue()
    im['bufferView'] = add_view(data)
    report['images'].append({'image': ii, 'from': [w, h], 'to': list(img.size), 'changed': changed})

import fast_simplification
from scipy.spatial import cKDTree
done = {}
for mi, mesh in enumerate(g['meshes']):
    for pi, pr in enumerate(mesh['primitives']):
        attrs = {k: read(v) for k, v in pr['attributes'].items()}
        idx = read(pr['indices']).reshape(-1, 3).astype(np.int64) if 'indices' in pr else np.arange(len(attrs['POSITION'])).reshape(-1, 3)
        ntri = len(idx)
        if ntri > A.decimate_over:
            P = attrs['POSITION'].astype(np.float32)
            pts, faces = fast_simplification.simplify(P, idx.astype(np.int32), target_reduction=1 - A.keep)
            _, nn = cKDTree(P).query(pts)
            new_attrs = {k: (pts.astype(v.dtype) if k == 'POSITION' else v[nn]) for k, v in attrs.items()}
            idx = faces.astype(np.int64); attrs = new_attrs
        report['primitives'].append({'mesh': mi, 'name': mesh.get('name'), 'prim': pi, 'tris_before': int(ntri), 'tris_after': int(len(idx))})
        for k, v in attrs.items():
            src_a = g['accessors'][pr['attributes'][k]]
            comp = src_a['componentType']; typ = src_a['type']
            pr['attributes'][k] = add_acc(v.astype(CT[comp]), typ, comp, 34962, src_a.get('normalized', False))
        it = 5125 if idx.max() > 65535 else 5123
        pr['indices'] = add_acc(idx.reshape(-1).astype(CT[it]).reshape(-1, 1), 'SCALAR', it, 34963)
        new_accs[pr['indices']]['count'] = int(idx.size)
        pr.pop('targets', None)
# animations / skins reference accessors too: drop them (the simulator strips animation components anyway)
g.pop('animations', None); g.pop('skins', None)
for nd in g.get('nodes', []): nd.pop('skin', None)
g['accessors'] = new_accs; g['bufferViews'] = new_views
while len(nb) % 4: nb.append(0)
g['buffers'] = [{'byteLength': len(nb)}]
g.setdefault('asset', {})['generator'] = 'PortCCTVSimulator glb_derive.py'
js = json.dumps(g, separators=(',', ':')).encode()
while len(js) % 4: js += b' '
out = b'glTF' + struct.pack('<II', 2, 12 + 8 + len(js) + 8 + len(nb)) + struct.pack('<I', len(js)) + b'JSON' + js + struct.pack('<I', len(nb)) + b'BIN\x00' + bytes(nb)
open(A.dst, 'wb').write(out)
report['dst_sha256'] = hashlib.sha256(out).hexdigest(); report['dst_bytes'] = len(out)
report['tris_total_before'] = sum(p['tris_before'] for p in report['primitives']); report['tris_total_after'] = sum(p['tris_after'] for p in report['primitives'])
print(json.dumps(report))
