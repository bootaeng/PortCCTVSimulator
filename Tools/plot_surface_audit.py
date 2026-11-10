"""Top-down plots of the surface audit grid (Logs/surface_audit/<map>.bin from PortCCTV.EditorTools.SurfaceAudit).
usage: python -I Tools/plot_surface_audit.py <map> [out.png] [x0 z0 x1 z1]   (crop in world metres; default whole map at 2 m/px)
colours: water blue, natural ground green, obstruction / building dark grey,
         drivable in the main road network light grey, drivable OFF the main network red,
         footway in the main pedestrian network pale yellow, footway off it orange, footway reaching a boat landing violet,
         stairs cyan."""
import sys, struct, numpy as np
from PIL import Image

m = sys.argv[1]
out = sys.argv[2] if len(sys.argv) > 2 else f'Logs/surface_audit/{m}.png'
b = open(f'Logs/surface_audit/{m}.bin', 'rb').read()
N, half, cell = struct.unpack('<iff', b[:12])
a = np.frombuffer(b[12:], dtype=np.dtype([('c', 'u1'), ('f', 'u1'), ('k', '<i2'), ('h', '<f4'), ('vc', '<i4'), ('pc', '<i4')]), count=N * N).reshape(N, N)
c, f = a['c'], a['f']
img = np.zeros((N, N, 3), np.uint8)
img[c == 6] = (60, 110, 170)
img[c == 1] = (70, 105, 60)
img[c == 4] = (55, 55, 55)
img[c == 0] = (0, 0, 0)
d = c == 2
img[d & (f & 1 > 0)] = (200, 200, 200)
img[d & (f & 1 == 0)] = (230, 40, 40)
w = c == 3
img[w & (f & 2 > 0)] = (235, 225, 160)
img[w & (f & 2 == 0)] = (255, 140, 0)
img[(c == 3) & (f & 4 > 0)] = (170, 80, 220)
img[(c == 2) & (f & 4 > 0) & (f & 1 == 0)] = (170, 80, 220)
img[c == 5] = (0, 220, 230)
img = img[::-1]          # north (+z) up
if len(sys.argv) > 6:
    x0, z0, x1, z1 = map(float, sys.argv[3:7])
    i0, i1 = int((x0 + half) / cell), int((x1 + half) / cell)
    j0, j1 = int((half - z1) / cell), int((half - z0) / cell)
    im = Image.fromarray(img[j0:j1, i0:i1])
    s = max(1, int(900 / max(im.size)))
    im = im.resize((im.size[0] * s, im.size[1] * s), Image.NEAREST)
else:
    im = Image.fromarray(img).resize((N // 2, N // 2), Image.NEAREST)
im.save(out)
print(out, im.size)
