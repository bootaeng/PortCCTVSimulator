"""Write a derived GLB whose embedded images are resized to at most <max_px> (JPEG for opaque, PNG if alpha).
Usage: glb_downscale_textures.py <in.glb> <out.glb> <max_px>"""
import io, json, struct, sys, hashlib
from PIL import Image

src, dst, max_px = sys.argv[1], sys.argv[2], int(sys.argv[3])
b = open(src, 'rb').read()
jl = struct.unpack('<I', b[12:16])[0]; g = json.loads(b[20:20 + jl])
bl = struct.unpack('<I', b[20 + jl:24 + jl])[0]; bin0 = b[28 + jl:28 + jl + bl]
views = g['bufferViews']
img_views = {im['bufferView']: i for i, im in enumerate(g.get('images', []))}
new_bin = bytearray(); changed = 0
for vi, bv in enumerate(views):
    off, ln = bv.get('byteOffset', 0), bv['byteLength']
    data = bin0[off:off + ln]
    if vi in img_views:
        im = Image.open(io.BytesIO(data)); w, h = im.size
        if max(w, h) > max_px:
            k = max_px / max(w, h); im = im.resize((max(1, round(w * k)), max(1, round(h * k))), Image.LANCZOS)
            out = io.BytesIO()
            if im.mode in ('RGBA', 'LA') and im.getextrema()[-1][0] < 255:
                im.save(out, 'PNG', optimize=True); g['images'][img_views[vi]]['mimeType'] = 'image/png'
            else:
                im.convert('RGB').save(out, 'JPEG', quality=90); g['images'][img_views[vi]]['mimeType'] = 'image/jpeg'
            data = out.getvalue(); changed += 1
    while len(new_bin) % 4: new_bin.append(0)
    bv['byteOffset'] = len(new_bin); bv['byteLength'] = len(data); new_bin.extend(data)
while len(new_bin) % 4: new_bin.append(0)
g['buffers'][0]['byteLength'] = len(new_bin)
g.setdefault('asset', {})['generator'] = 'PortCCTVSimulator glb_downscale_textures.py'
js = json.dumps(g, separators=(',', ':')).encode('utf-8')
while len(js) % 4: js += b' '
total = 12 + 8 + len(js) + 8 + len(new_bin)
with open(dst, 'wb') as f:
    f.write(b'glTF' + struct.pack('<II', 2, total)); f.write(struct.pack('<II', len(js), 0x4E4F534A) + js)
    f.write(struct.pack('<II', len(new_bin), 0x004E4942) + bytes(new_bin))
print(f"{src}: {changed} images resized, {len(b)/1e6:.1f} -> {total/1e6:.1f} MB, source sha256 {hashlib.sha256(b).hexdigest()}")
