"""Filmstrip + animated GIF of a lens-rain capture sequence (<captures>/<prefix>_tNNN/rgb.png).
The crop follows the first running drop found in the metadata (or the image centre).
usage: python -I Tools/lensrain_filmstrip.py <captures_dir> <prefix> <out_stem> [crop_w crop_h]"""
import glob, json, os, sys
from PIL import Image, ImageDraw

root, prefix, stem = sys.argv[1], sys.argv[2], sys.argv[3]
cw, ch = (int(sys.argv[4]), int(sys.argv[5])) if len(sys.argv) > 5 else (480, 540)
dirs = sorted(d for d in glob.glob(os.path.join(root, prefix + '_t*')) if os.path.isdir(d))
metas = [json.load(open(os.path.join(d, 'metadata.json'), encoding='utf-8'))['sensor'] for d in dirs]
cx, cy = None, None
for m in metas:
    run = [x for x in m['lens_drops'] if x.get('kind') == 'running']
    if run: cx, cy = run[0]['x'], run[0]['y'] + 120; break
first = Image.open(os.path.join(dirs[0], 'rgb.png'))
W, H = first.size
if cx is None: cx, cy = W / 2, H / 2
x0 = int(min(max(cx - cw / 2, 0), W - cw)); y0 = int(min(max(cy - ch / 2, 0), H - ch))
frames = []
for d, m in zip(dirs, metas):
    im = Image.open(os.path.join(d, 'rgb.png')).convert('RGB').crop((x0, y0, x0 + cw, y0 + ch))
    ImageDraw.Draw(im).text((6, 6), f"t={m['lens_drop_time_s']:.2f}s", fill='yellow')
    frames.append(im)
frames[0].save(stem + '.gif', save_all=True, append_images=frames[1:], duration=int(1000 * max(0.1, (metas[1]['lens_drop_time_s'] - metas[0]['lens_drop_time_s']) if len(metas) > 1 else 0.25)), loop=0)
step = max(1, len(frames) // 8)
sel = frames[::step][:8]
strip = Image.new('RGB', (cw // 2 * len(sel), ch // 2))
for i, f in enumerate(sel): strip.paste(f.resize((cw // 2, ch // 2)), (i * cw // 2, 0))
strip.save(stem + '_strip.jpg', quality=88)
print(len(frames), 'frames, crop', (x0, y0, cw, ch), '->', stem + '.gif', stem + '_strip.jpg')
