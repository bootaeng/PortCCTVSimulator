"""Synthesise a tileable forest-canopy texture (albedo + tangent normal) as seen from a distance:
overlapping tree crowns with per-crown shading and colour variation, dark gaps, and clearings/rock outcrops
blended in from CC0 aerial scans. Deterministic (seed).
Usage: make_canopy_texture.py <out_dir> <aerial_grass_rock_diff.jpg> <aerial_rocks_02_diff.jpg> [size=2048] [seed=7]"""
import sys, numpy as np
from pathlib import Path
from PIL import Image

out = Path(sys.argv[1]); grass_path, rock_path = sys.argv[2], sys.argv[3]
N = int(sys.argv[4]) if len(sys.argv) > 4 else 2048
seed = int(sys.argv[5]) if len(sys.argv) > 5 else 7
rng = np.random.default_rng(seed)
out.mkdir(parents=True, exist_ok=True)

# ---- tileable low-frequency noise (for clearings and colour drift)
def tile_noise(n, cells, octaves, rng):
    acc = np.zeros((n, n)); amp = 1.0; tot = 0
    for o in range(octaves):
        c = cells << o
        g = rng.random((c, c))
        # bilinear upsample with wrap
        ys = np.linspace(0, c, n, endpoint=False); xs = ys
        y0 = np.floor(ys).astype(int); x0 = np.floor(xs).astype(int); fy = (ys - y0)[:, None]; fx = (xs - x0)[None, :]
        fy = fy * fy * (3 - 2 * fy); fx = fx * fx * (3 - 2 * fx)
        y1 = (y0 + 1) % c; x1 = (x0 + 1) % c
        v = (g[np.ix_(y0, x0)] * (1 - fy) * (1 - fx) + g[np.ix_(y0, x1)] * (1 - fy) * fx + g[np.ix_(y1, x0)] * fy * (1 - fx) + g[np.ix_(y1, x1)] * fy * fx)
        acc += amp * v; tot += amp; amp *= 0.5
    return acc / tot

# ---- crowns: height field by max-combination of hemispherical caps (wrapped)
H = np.zeros((N, N), dtype=np.float32)
crown_id = np.full((N, N), -1, dtype=np.int32)
n_crowns = int(N * N / 420)
cx = rng.random(n_crowns) * N; cy = rng.random(n_crowns) * N
rad = rng.uniform(9, 30, n_crowns) * (N / 2048)
height = rad * rng.uniform(0.8, 1.3, n_crowns)
order = np.argsort(height)  # tall crowns last so they win the max
for i in order:
    r = rad[i]; R = int(np.ceil(r)) + 1
    x0 = int(np.floor(cx[i])) - R; y0 = int(np.floor(cy[i])) - R
    xs = np.arange(x0, x0 + 2 * R + 1); ys = np.arange(y0, y0 + 2 * R + 1)
    dx = xs[None, :] - cx[i]; dy = ys[:, None] - cy[i]
    d2 = dx * dx + dy * dy
    inside = d2 < r * r
    cap = np.where(inside, height[i] * np.sqrt(np.clip(1 - d2 / (r * r), 0, 1)) + height[i] * 0.15, -1)
    yi = ys % N; xi = xs % N
    sub = H[np.ix_(yi, xi)]
    better = cap > sub
    sub[better] = cap[better]; H[np.ix_(yi, xi)] = sub
    ids = crown_id[np.ix_(yi, xi)]; ids[better] = i; crown_id[np.ix_(yi, xi)] = ids
# leaf-scale roughness
H += (tile_noise(N, 256, 3, rng) - 0.5) * 2.0 * (N / 2048)

# ---- normals from height (wrapped gradients), light shading
scale = 0.5
gx = (np.roll(H, -1, 1) - np.roll(H, 1, 1)) * scale
gy = (np.roll(H, -1, 0) - np.roll(H, 1, 0)) * scale
nrm = np.stack([-gx, -gy, np.ones_like(H)], -1); nrm /= np.linalg.norm(nrm, axis=-1, keepdims=True)
light = np.array([-0.45, -0.55, 0.70]); light /= np.linalg.norm(light)
shade = np.clip((nrm @ light), 0, 1)
# occlusion: gaps between crowns are dark
hmax = H.max(); ao = np.clip((np.clip(H, 0, None) / max(hmax, 1e-6)) ** 1.3, 0.12, 1.0)

# ---- colour per crown
palette = np.array([[0.06, 0.14, 0.05], [0.08, 0.19, 0.06], [0.11, 0.22, 0.08], [0.15, 0.23, 0.08], [0.07, 0.17, 0.07], [0.19, 0.25, 0.09], [0.05, 0.12, 0.05]])  # darker, slightly desaturated: distant Korean hillsides read dark green
crown_col = palette[rng.integers(0, len(palette), n_crowns)] * rng.uniform(0.8, 1.2, (n_crowns, 1))
col = np.where(crown_id[..., None] >= 0, crown_col[np.clip(crown_id, 0, None)], np.array([0.05, 0.08, 0.04]))
drift = tile_noise(N, 3, 3, rng)[..., None]
col = col * (0.85 + 0.3 * drift) * (0.35 + 0.75 * shade[..., None]) * ao[..., None]

# ---- clearings (aerial grass/rock scan) and rock outcrops
def load_tile(p):
    im = Image.open(p).convert('RGB').resize((N, N), Image.LANCZOS)
    return (np.asarray(im).astype(np.float32) / 255.0) ** 2.2  # to linear
grass = load_tile(grass_path); rock = load_tile(rock_path)
mask_c = tile_noise(N, 2, 4, rng); clearing = np.clip((mask_c - 0.62) * 9.0, 0, 1)[..., None]
mask_r = tile_noise(N, 4, 3, np.random.default_rng(seed + 5)); outcrop = np.clip((mask_r - 0.74) * 12.0, 0, 1)[..., None]
col = col * (1 - clearing) + grass * 0.9 * clearing
col = col * (1 - outcrop) + rock * outcrop
flat = (1 - clearing[..., 0]) * (1 - outcrop[..., 0])
nrm_out = nrm.copy(); nrm_out[..., :2] *= flat[..., None]; nrm_out /= np.linalg.norm(nrm_out, axis=-1, keepdims=True)

alb = np.clip(col, 0, 1) ** (1 / 2.2)
Image.fromarray((alb * 255).astype(np.uint8)).save(out / 'T_canopy_albedo.png')
Image.fromarray(((nrm_out * 0.5 + 0.5) * 255).astype(np.uint8)).save(out / 'T_canopy_normal.png')
print('wrote', out, 'crowns', n_crowns, 'clearing %.1f%% outcrop %.1f%%' % (clearing.mean() * 100, outcrop.mean() * 100))
