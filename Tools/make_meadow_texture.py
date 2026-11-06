"""Tileable 'meadow' macro texture (2048 px meant for ~120 m) for flat land and camera-hill slopes.
v3 (2026-11-05): stochastic patch synthesis instead of a 4x4 repeat. ~500 crops of the aerial grass/rock scan (each scan tile
first flattened: its own low-frequency light/dark blotches divided out) are pasted at jittered grid positions with random
90-degree rotations and flips (normals rotated to match) and blended with narrow raised-cosine weights, wrapping at the edges
so the result still tiles. Patches of a second grass scan and two scales of brightness/hue drift are layered on top.
v1 (plain 4x4 repeat) read as a checkerboard on large flats; v2 (flattened 4x4 repeat) still showed the scan's own period.
Usage: make_meadow_texture.py <out_dir> <aerial_grass_rock_diff.jpg> <aerial_grass_rock_nor_gl.jpg> <grass_path_2_diff.jpg> <grass_path_2_nor_gl.jpg> [seed]"""
import sys
from pathlib import Path

import numpy as np
from PIL import Image

out = Path(sys.argv[1]); a_diff, a_nor, b_diff, b_nor = sys.argv[2:6]
seed = int(sys.argv[6]) if len(sys.argv) > 6 else 11
N = 2048; SRC = N // 4            # source scan resampled to 512 px per 30 m, as in v1/v2 (same ground scale)
P = 200; STEP = 92                # patch size and grid pitch (px)
rng = np.random.default_rng(seed); out.mkdir(parents=True, exist_ok=True)


def tile_noise(n, cells, octaves, rng):
    acc = np.zeros((n, n)); amp = 1.0; tot = 0
    for o in range(octaves):
        c = cells << o; g = rng.random((c, c))
        ys = np.linspace(0, c, n, endpoint=False); y0 = np.floor(ys).astype(int); fy = (ys - y0)[:, None]; fy = fy * fy * (3 - 2 * fy)
        x0 = y0; fx = fy.T; y1 = (y0 + 1) % c; x1 = y1
        v = g[np.ix_(y0, x0)] * (1 - fy) * (1 - fx) + g[np.ix_(y0, x1)] * (1 - fy) * fx + g[np.ix_(y1, x0)] * fy * (1 - fx) + g[np.ix_(y1, x1)] * fy * fx
        acc += amp * v; tot += amp; amp *= 0.5
    return acc / tot


def flatten(t, sigma):
    """Divide out the tile's own low-frequency brightness (periodic gaussian blur via FFT), keeping its mean."""
    n = t.shape[0]; f = np.fft.fftfreq(n)
    g = np.exp(-2 * (np.pi * sigma) ** 2 * (f[:, None] ** 2 + f[None, :] ** 2))
    o = np.empty_like(t)
    for c in range(t.shape[2]):
        low = np.real(np.fft.ifft2(np.fft.fft2(t[..., c]) * g))
        o[..., c] = t[..., c] / np.maximum(low, 1e-3) * t[..., c].mean()
    return o


def load(p, normal):
    t = np.asarray(Image.open(p).convert('RGB').resize((SRC, SRC), Image.LANCZOS)).astype(np.float32) / 255.0
    return t * 2 - 1 if normal else flatten(t ** 2.2, 24.0)


def orient(c, n, k, flip):
    """Rotate a colour crop and its tangent-space normals by k*90 deg CCW (visual), then optionally mirror left-right."""
    n = n.copy()
    for _ in range(k):
        c = np.rot90(c); n = np.rot90(n)
        nx, ny = n[..., 0].copy(), n[..., 1].copy(); n[..., 0], n[..., 1] = -ny, nx
    if flip:
        c = c[:, ::-1]; n = n[:, ::-1].copy(); n[..., 0] = -n[..., 0]
    return c, n


def synth(col_src, nrm_src, rng):
    acc = np.zeros((N, N, 3)); nacc = np.zeros((N, N, 3)); w = np.zeros((N, N))
    win1 = np.sin(np.linspace(0, np.pi, P)) ** 3; win = np.outer(win1, win1) + 1e-6
    for gy in range(0, N, STEP):
        for gx in range(0, N, STEP):
            y = (gy + rng.integers(-STEP // 3, STEP // 3)) % N; x = (gx + rng.integers(-STEP // 3, STEP // 3)) % N
            sy, sx = rng.integers(0, SRC, 2)
            iy = (np.arange(P) + sy) % SRC; ix = (np.arange(P) + sx) % SRC
            c, n = orient(col_src[np.ix_(iy, ix)], nrm_src[np.ix_(iy, ix)], int(rng.integers(4)), bool(rng.integers(2)))
            oy = (np.arange(P) + y) % N; ox = (np.arange(P) + x) % N
            acc[np.ix_(oy, ox)] += c * win[..., None]; nacc[np.ix_(oy, ox)] += n * win[..., None]; w[np.ix_(oy, ox)] += win
    nn = nacc / w[..., None]; nn /= np.linalg.norm(nn, axis=-1, keepdims=True)
    return acc / w[..., None], nn


A, An = synth(load(a_diff, False), load(a_nor, True), rng)
Bm, Bn = synth(load(b_diff, False), load(b_nor, True), np.random.default_rng(seed + 7))
patch = np.clip((tile_noise(N, 2, 4, np.random.default_rng(seed)) - 0.55) * 6.0, 0, 1)[..., None]
col = A * (1 - patch) + Bm * patch
nrm = An * (1 - patch) + Bn * patch; nrm /= np.linalg.norm(nrm, axis=-1, keepdims=True)
drift = 0.6 * tile_noise(N, 3, 4, np.random.default_rng(seed + 1))[..., None] + 0.4 * tile_noise(N, 5, 3, np.random.default_rng(seed + 3))[..., None]
hue = tile_noise(N, 2, 3, np.random.default_rng(seed + 2))[..., None]
col = col * (0.75 + 0.5 * drift) * np.concatenate([0.9 + 0.2 * hue, 1.0 + 0.1 * hue, 0.85 + 0.2 * hue], -1)
col = np.clip(col, 0, 1) ** (1 / 2.2)
Image.fromarray((col * 255).astype(np.uint8)).save(out / 'T_meadow_albedo.png')
Image.fromarray(((nrm * 0.5 + 0.5).clip(0, 1) * 255).astype(np.uint8)).save(out / 'T_meadow_normal.png')
print('wrote meadow v3', out, 'patch %.1f%%' % (patch.mean() * 100))
