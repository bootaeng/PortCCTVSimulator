"""Compact subset of a GLB (2026-11-10): keeps the given nodes (with their subtrees and the chain of ancestors up to the
scene root), drops every other node, and writes only the meshes / materials / textures / images / accessors they use.
Images are re-encoded at most --max-px (JPEG when opaque). Used to take single buildings out of a large city set whose
other parts carry real brand signage (the brand textures are then not in the file at all).
Usage: glb_subset.py <in.glb> <out.glb> --nodes 648,651,... [--max-px 512]
Prints a JSON report (kept nodes, triangles, images, source/destination SHA-256)."""
import argparse, hashlib, io, json, struct
import numpy as np
from PIL import Image

ap = argparse.ArgumentParser()
ap.add_argument('src'); ap.add_argument('dst')
ap.add_argument('--nodes', required=True)
ap.add_argument('--max-px', type=int, default=512)
A = ap.parse_args()

b = open(A.src, 'rb').read()
jl = struct.unpack('<I', b[12:16])[0]; g = json.loads(b[20:20 + jl])
bl = struct.unpack('<I', b[20 + jl:24 + jl])[0]; bin0 = b[28 + jl:28 + jl + bl]
keep_roots = [int(x) for x in A.nodes.split(',') if x.strip()]

parent = {}
for i, n in enumerate(g['nodes']):
    for c in n.get('children', []): parent[c] = i
keep = set()
def sub(i):
    keep.add(i)
    for c in g['nodes'][i].get('children', []): sub(c)
for r in keep_roots:
    sub(r); p = parent.get(r)
    while p is not None: keep.add(p); p = parent.get(p)

nb = bytearray(); views = []; accs = []; out = {'nodes': [], 'meshes': [], 'materials': [], 'textures': [], 'images': [], 'samplers': g.get('samplers', [])}
def add_view(data, target=None):
    while len(nb) % 4: nb.append(0)
    v = {'buffer': 0, 'byteOffset': len(nb), 'byteLength': len(data)}
    if target: v['target'] = target
    nb.extend(data); views.append(v); return len(views) - 1

amap = {}
def acc(ai, target=None):
    if ai in amap: return amap[ai]
    a = dict(g['accessors'][ai]); bv = g['bufferViews'][a['bufferView']]
    comp = {5120: 1, 5121: 1, 5122: 2, 5123: 2, 5125: 4, 5126: 4}[a['componentType']]
    n = {'SCALAR': 1, 'VEC2': 2, 'VEC3': 3, 'VEC4': 4, 'MAT4': 16}[a['type']]
    stride = bv.get('byteStride', 0) or comp * n
    off = bv.get('byteOffset', 0) + a.get('byteOffset', 0)
    rows = [bin0[off + k * stride: off + k * stride + comp * n] for k in range(a['count'])] if stride != comp * n else [bin0[off: off + a['count'] * comp * n]]
    a['bufferView'] = add_view(b''.join(rows), target); a.pop('byteOffset', None)
    accs.append(a); amap[ai] = len(accs) - 1; return amap[ai]

imap, tmap, mmap, mshmap, report_imgs = {}, {}, {}, {}, []
def image(ii):
    if ii in imap: return imap[ii]
    im = g['images'][ii]; bv = g['bufferViews'][im['bufferView']]
    data = bin0[bv.get('byteOffset', 0): bv.get('byteOffset', 0) + bv['byteLength']]
    pil = Image.open(io.BytesIO(data)); size0 = pil.size
    if max(pil.size) > A.max_px: pil.thumbnail((A.max_px, A.max_px), Image.LANCZOS)
    alpha = pil.mode in ('RGBA', 'LA') and np.asarray(pil.convert('RGBA'))[..., 3].min() < 250
    buf = io.BytesIO()
    if alpha: pil.convert('RGBA').save(buf, 'PNG'); mime = 'image/png'
    else: pil.convert('RGB').save(buf, 'JPEG', quality=90); mime = 'image/jpeg'
    out['images'].append({'bufferView': add_view(buf.getvalue()), 'mimeType': mime})
    imap[ii] = len(out['images']) - 1; report_imgs.append({'src': ii, 'size0': size0, 'size': pil.size}); return imap[ii]
def texture(ti):
    if ti in tmap: return tmap[ti]
    t = dict(g['textures'][ti]); t['source'] = image(t['source']); out['textures'].append(t); tmap[ti] = len(out['textures']) - 1; return tmap[ti]
def material(mi):
    if mi in mmap: return mmap[mi]
    m = json.loads(json.dumps(g['materials'][mi]))
    for key in ('pbrMetallicRoughness', 'normalTexture', 'occlusionTexture', 'emissiveTexture'):
        if key in m:
            if key == 'pbrMetallicRoughness':
                for tk in ('baseColorTexture', 'metallicRoughnessTexture'):
                    if tk in m[key]: m[key][tk]['index'] = texture(m[key][tk]['index'])
            else: m[key]['index'] = texture(m[key]['index'])
    m.pop('extensions', None)
    out['materials'].append(m); mmap[mi] = len(out['materials']) - 1; return mmap[mi]
tris = 0
def mesh(mi):
    global tris
    if mi in mshmap: return mshmap[mi]
    m = {'name': g['meshes'][mi].get('name', ''), 'primitives': []}
    for p in g['meshes'][mi]['primitives']:
        q = {'attributes': {k: acc(v, 34962) for k, v in p['attributes'].items()}, 'mode': p.get('mode', 4)}
        if 'indices' in p: q['indices'] = acc(p['indices'], 34963); tris += g['accessors'][p['indices']]['count'] // 3
        if 'material' in p: q['material'] = material(p['material'])
        m['primitives'].append(q)
    out['meshes'].append(m); mshmap[mi] = len(out['meshes']) - 1; return mshmap[mi]

nmap = {}
order = sorted(keep)
for i in order: nmap[i] = len(nmap)
for i in order:
    n = {k: v for k, v in g['nodes'][i].items() if k in ('name', 'matrix', 'translation', 'rotation', 'scale')}
    if 'mesh' in g['nodes'][i]: n['mesh'] = mesh(g['nodes'][i]['mesh'])
    ch = [nmap[c] for c in g['nodes'][i].get('children', []) if c in keep]
    if ch: n['children'] = ch
    out['nodes'].append(n)
roots = [nmap[r] for r in g['scenes'][g.get('scene', 0)]['nodes'] if r in keep]
gl = {'asset': {'version': '2.0', 'generator': 'glb_subset.py (PortCCTVSimulator)', 'copyright': g.get('asset', {}).get('copyright', '')},
      'scene': 0, 'scenes': [{'nodes': roots}], 'nodes': out['nodes'], 'meshes': out['meshes'], 'materials': out['materials'],
      'textures': out['textures'], 'images': out['images'], 'accessors': accs, 'bufferViews': views,
      'buffers': [{'byteLength': len(nb)}]}
if out['samplers']: gl['samplers'] = out['samplers']
if g.get('extensionsUsed'): gl['extensionsUsed'] = [e for e in g['extensionsUsed'] if e in json.dumps(out['materials'])]
js = json.dumps(gl, separators=(',', ':')).encode()
while len(js) % 4: js += b' '
while len(nb) % 4: nb.append(0)
glb = struct.pack('<III', 0x46546C67, 2, 12 + 8 + len(js) + 8 + len(nb)) + struct.pack('<II', len(js), 0x4E4F534A) + js + struct.pack('<II', len(nb), 0x004E4942) + bytes(nb)
open(A.dst, 'wb').write(glb)
print(json.dumps({'src_sha256': hashlib.sha256(b).hexdigest(), 'dst_sha256': hashlib.sha256(glb).hexdigest(), 'kept_roots': keep_roots,
                  'nodes': len(out['nodes']), 'meshes': len(out['meshes']), 'materials': len(out['materials']), 'triangles': tris,
                  'images': report_imgs, 'bytes': len(glb)}, indent=1))
