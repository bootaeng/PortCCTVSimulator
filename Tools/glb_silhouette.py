"""Side / top silhouette of a GLB (triangles rasterised in long-axis vs height and long-axis vs beam), per node colour, for
checking bow direction and duplicated nodes before writing a HullSpec. usage: glb_silhouette.py <glb> <out.png>"""
import sys

import numpy as np
from PIL import Image, ImageDraw

sys.path.insert(0, __import__("os").path.dirname(__file__))
from glb_ship_metrics import load, prims  # noqa: E402

p, out = sys.argv[1], sys.argv[2]
js, bin_ = load(p)
ps = prims(js, bin_)
allv = np.concatenate([v for _, _, v, _ in ps]); lo, hi = allv.min(0), allv.max(0); size = hi - lo
la = 0 if size[0] >= size[2] else 2; ba = 2 if la == 0 else 0
W = 1600; k = (W - 20) / size[la]; Hs = int(size[1] * k) + 20; Ht = int(size[ba] * k) + 20
img = Image.new("RGB", (W, Hs + Ht + 30), "white"); d = ImageDraw.Draw(img)
cols = [(200, 40, 40), (40, 120, 200), (40, 160, 60), (200, 140, 20), (140, 60, 180), (20, 160, 160), (120, 120, 120), (220, 90, 160)]
names = sorted({(n, m) for n, m, _, _ in ps})
for n, m, v, t in ps:
    c = cols[names.index((n, m)) % len(cols)]
    sel = t if len(t) <= 40000 else t[np.random.default_rng(0).choice(len(t), 40000, replace=False)]
    for tri in sel:
        a = v[tri]
        d.polygon([(10 + (q[la] - lo[la]) * k, 10 + (hi[1] - q[1]) * k) for q in a], fill=c)
        d.polygon([(10 + (q[la] - lo[la]) * k, Hs + 30 + (q[ba] - lo[ba]) * k) for q in a], fill=c)
d.text((10, Hs + 12), f"side (long axis {'xyz'[la]} left=min) / top; size {np.round(size, 3)}", fill="black")
img.save(out); print(out, img.size, "nodes:", len(names))
for i, (n, m) in enumerate(names[:12]): print("  ", cols[i % len(cols)], n, m)
