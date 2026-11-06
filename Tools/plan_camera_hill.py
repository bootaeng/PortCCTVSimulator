"""Offline planner for shore camera hills (no Unity needed).

Ports ProcNoise.HillShape (Runtime/ProcNoise.cs) to numpy and, for a candidate camera hill (hill index, centre, size,
camera pose), reports: the unit height under the camera and the resulting vertical scale sy, how far the hill rises
above the water inside protected water regions, and a low-resolution ray-cast of the camera view (sea / hill / flat land
/ sky fractions). Writes a top-down map + view PNG so a placement can be checked before running Unity.

Usage: python -X utf8 Tools/plan_camera_hill.py <out_dir>   (the candidates are defined in CANDIDATES below)
"""
import math
import os
import sys

import numpy as np
from PIL import Image, ImageDraw

M32 = 0xFFFFFFFF


def hash_(x, y, seed):
    h = (x * 374761393 + y * 668265263 + seed * 2147483647) & M32
    h = ((h ^ (h >> 13)) * 1274126177) & M32
    return ((h ^ (h >> 16)) & 0xFFFFFF) / float(0xFFFFFF)


hash_v = np.vectorize(hash_, otypes=[np.float64])


def vnoise(u, v, cells, seed):
    x = u * cells; y = v * cells
    x0 = np.floor(x).astype(np.int64); y0 = np.floor(y).astype(np.int64)
    fx = x - x0; fy = y - y0
    fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy)
    W = lambda a: np.mod(a, cells)
    a00 = hash_v(W(x0), W(y0), seed); a10 = hash_v(W(x0 + 1), W(y0), seed)
    a01 = hash_v(W(x0), W(y0 + 1), seed); a11 = hash_v(W(x0 + 1), W(y0 + 1), seed)
    return (a00 + (a10 - a00) * fx) * (1 - fy) + (a01 + (a11 - a01) * fx) * fy


def fbm(u, v, base, octaves, seed):
    s = 0.0; a = 0.5; n = 0.0
    for o in range(octaves):
        s = s + a * vnoise(u, v, base << o, seed + o * 31); n += a; a *= 0.5
    return s / n


def hill_height(i, u, w):
    u = np.asarray(u, dtype=np.float64); w = np.asarray(w, dtype=np.float64)
    seed = 2000 + i; ridge = 0.45 if i % 2 == 0 else 0.65
    dx = u - 0.5; dz = w - 0.5
    r = np.sqrt(dx * dx + dz * dz) * 2
    warp = (fbm(u, w, 2, 3, seed + 19) - 0.5) * 0.5
    rw = r * (1 + warp)
    fall = np.clip(1 - rw, 0, 1)
    fall = (fall * fall * (3 - 2 * fall)) ** 0.75
    n = fbm(u, w, 3, 6, seed)
    rid = 1 - np.abs(fbm(u, w, 2, 5, seed + 7) * 2 - 1); rid = rid * rid
    fine = 1 - np.abs(fbm(u, w, 9, 3, seed + 13) * 2 - 1)
    mix = (1 - ridge) * n + ridge * rid
    amp = 0.15 + 0.85 * mix ** 1.3
    h = fall * amp * (0.88 + 0.12 * fine)
    out = np.maximum(h, 0) ** 1.15
    edge = (u <= 0) | (u >= 1) | (w <= 0) | (w >= 1)
    return np.where(edge, -0.05, out)


class HillGrid:
    """World-space height of one hill instance (yaw 0), precomputed on its 129x129 mesh grid, bilinear like the mesh."""
    def __init__(self, idx, cx, cz, sx, sz, base, sy, n=128):
        self.cx, self.cz, self.sx, self.sz, self.base, self.sy, self.n = cx, cz, sx, sz, base, sy, n
        g = np.arange(n + 1) / n
        U, Wg = np.meshgrid(g, g)
        h = hill_height(idx, U, Wg); h[0, :] = h[-1, :] = h[:, 0] = h[:, -1] = -0.05
        self.h = h

    def y(self, x, z):
        u = (np.asarray(x) - self.cx) / self.sx + 0.5; w = (np.asarray(z) - self.cz) / self.sz + 0.5
        inside = (u >= 0) & (u <= 1) & (w >= 0) & (w <= 1)
        fu = np.clip(u, 0, 1) * self.n; fw = np.clip(w, 0, 1) * self.n
        i0 = np.clip(np.floor(fu).astype(int), 0, self.n - 1); j0 = np.clip(np.floor(fw).astype(int), 0, self.n - 1)
        tu = fu - i0; tw = fw - j0
        h = (self.h[j0, i0] * (1 - tu) + self.h[j0, i0 + 1] * tu) * (1 - tw) + (self.h[j0 + 1, i0] * (1 - tu) + self.h[j0 + 1, i0 + 1] * tu) * tw
        return np.where(inside, self.base + self.sy * h, -1e9)


def point_in_poly(px, pz, poly):
    inside = np.zeros_like(px, dtype=bool)
    n = len(poly)
    for k in range(n):
        x1, z1 = poly[k]; x2, z2 = poly[(k + 1) % n]
        cond = ((z1 > pz) != (z2 > pz)) & (px < (x2 - x1) * (pz - z1) / (z2 - z1 + 1e-12) + x1)
        inside ^= cond
    return inside


DECK = 2.5
MAST = 8.0

# Flat land boxes (top at Deck) and water regions copied from the map builders (x0, x1, z0, z1).
MAPS = {
    'container_terminal_01': dict(
        land=[(-420, 420, 60, 130), (-600, 600, 130, 430), (-1600, -420, -250, 430), (420, 1600, -150, 430), (-1600, 1600, 430, 1600),
              (-450, 250, -470, -455), (-450, -435, -455, -250), (420, 435, -400, -150)],
        protect={'terminal_basin': [(-420, 60), (420, 60), (420, -400), (250, -455), (-435, -455), (-435, -250), (-420, -250)],
                 'entrance': [(250, -455), (420, -400), (435, -400), (435, -480), (250, -480)]}),
    'fishing_port_01': dict(
        land=[(-185, 185, 60, 120), (-700, 700, 120, 700), (-1500, -185, -40, 500), (185, 1500, -20, 500),
              (-185, -170, -135, 60), (-185, -30, -135, -120), (170, 185, -75, 60), (40, 185, -75, -60)],
        protect={'inner_harbor': [(-170, 58), (170, 58), (170, -60), (40, -60), (-30, -120), (-170, -120)],
                 'entrance': [(-30, -120), (40, -60), (60, -80), (-30, -140)]}),
    'marina_01': dict(
        land=[(-265, 260, 60, 110), (-1500, 1500, 110, 1500), (-1500, -265, -200, 110), (260, 900, -10, 60), (900, 1500, -400, 110),
              (-265, -250, -215, 60), (-265, 120, -215, -200)],
        protect={'marina_basin': [(-250, 58), (250, 58), (250, -200), (-250, -200)]}),
}

# name, map, hill idx, centre x,z, size sx,sz, camera pos, euler (pitch, yaw), vfov
CANDIDATES = [
    ('ct_D', 'container_terminal_01', 2, (640, -70), (380, 300), (468, 58, -150), (8, 245), 50),
    ('fp_B', 'fishing_port_01', 0, (-330, 30), (300, 260), (-232, 40, -40), (9, 128), 50),
    ('mr_B', 'marina_01', 3, (1060, -230), (340, 300), (918, 60, -230), (9, 262), 48),
]


def plan(c, out):
    name, mp, idx, (cx, cz), (sx, sz), (px, py, pz), (pitch, yaw), vfov = c
    m = MAPS[mp]
    base = -5.0
    u = (px - cx) / sx + 0.5; w = (pz - cz) / sz + 0.5
    hu = float(hill_height(idx, u, w))
    sy = (py - MAST - base) / max(hu, 0.05)
    hill = HillGrid(idx, cx, cz, sx, sz, base, sy)
    rep = [f'{name}: hill {idx} centre ({cx},{cz}) size {sx}x{sz} cam ({px},{py},{pz}) yaw {yaw} pitch {pitch}',
           f'  unit height at camera {hu:.3f} (r={math.hypot(u - .5, w - .5) * 2:.2f}) -> sy {sy:.1f} m, peak {hill.h.max() * sy + base:.1f} m']
    # protected regions: max hill height above water inside them
    for rn, poly in m['protect'].items():
        xs = np.array([p[0] for p in poly]); zs = np.array([p[1] for p in poly])
        gx, gz = np.meshgrid(np.linspace(xs.min(), xs.max(), 220), np.linspace(zs.min(), zs.max(), 220))
        ins = point_in_poly(gx, gz, poly)
        hy = hill.y(gx, gz)
        top = hy[ins].max() if ins.any() else -1e9
        dry = int(((hy > -0.5) & ins).sum())
        rep.append(f'  region {rn}: max hill y {top:.2f} m, samples above -0.5 m: {dry}')
    # ray cast view 192x108
    W, H = 192, 108
    asp = 16 / 9; tv = math.tan(math.radians(vfov) / 2); th = tv * asp
    cyaw, syaw = math.cos(math.radians(yaw)), math.sin(math.radians(yaw)); cp, sp = math.cos(math.radians(pitch)), math.sin(math.radians(pitch))
    fwd = np.array([syaw * cp, -sp, cyaw * cp]); right = np.array([cyaw, 0, -syaw]); up = np.cross(fwd, right) * -1
    up = np.array([syaw * sp, cp, cyaw * sp])
    img = np.zeros((H, W, 3), np.uint8)
    cls_count = {'sky': 0, 'sea': 0, 'hill': 0, 'land': 0}
    for j in range(H):
        for i in range(W):
            x = (2 * (i + .5) / W - 1) * th; y = (1 - 2 * (j + .5) / H) * tv
            d = fwd + right * x + up * y; d /= np.linalg.norm(d)
            t = np.arange(1, 3000, 2.0) * np.maximum(1, np.arange(1, 3000, 2.0) / 400) ** 0.0
            ts = np.concatenate([np.arange(0.5, 400, 1.0), np.arange(400, 4000, 6.0)])
            X = px + d[0] * ts; Y = py + d[1] * ts; Z = pz + d[2] * ts
            hy = hill.y(X, Z)
            land = np.zeros_like(X, bool)
            for (x0, x1, z0, z1) in m['land']:
                land |= (X >= x0) & (X <= x1) & (Z >= z0) & (Z <= z1)
            ground = np.maximum(hy, np.where(land, DECK, 0.0))
            hit = np.nonzero(Y <= ground)[0]
            if len(hit) == 0:
                cls = 'sky'
            else:
                k = hit[0]
                cls = 'hill' if hy[k] >= max(DECK if land[k] else 0.0, 0.0) and hy[k] > -0.2 else ('land' if land[k] else 'sea')
            cls_count[cls] += 1
            img[j, i] = {'sky': (150, 180, 220), 'sea': (20, 70, 110), 'hill': (60, 110, 40), 'land': (180, 170, 120)}[cls]
    tot = W * H
    rep.append('  view: ' + ', '.join(f'{k} {v / tot * 100:.1f}%' for k, v in cls_count.items()))
    lower = img[H // 2:]
    rep.append(f'  lower half hill fraction {(np.all(lower == (60, 110, 40), axis=2)).mean() * 100:.1f}%')
    Image.fromarray(img).resize((576, 324), Image.NEAREST).save(os.path.join(out, f'{name}_view.png'))
    # top-down map 1600x1600 m around the camera
    S = 400; ext = 800.0
    gx, gz = np.meshgrid(np.linspace(px - ext, px + ext, S), np.linspace(pz + ext, pz - ext, S))
    hy = hill.y(gx, gz)
    land = np.zeros_like(gx, bool)
    for (x0, x1, z0, z1) in m['land']:
        land |= (gx >= x0) & (gx <= x1) & (gz >= z0) & (gz <= z1)
    top = np.zeros((S, S, 3), np.uint8); top[:] = (20, 70, 110)
    top[land] = (180, 170, 120)
    hh = hy > np.where(land, DECK, 0)
    shade = np.clip((hy - 0) / max(sy * 0.6, 1), 0, 1)
    top[hh] = (np.stack([60 + 120 * shade, 110 + 100 * shade, 40 + 60 * shade], -1)[hh]).astype(np.uint8)
    im = Image.fromarray(top); dr = ImageDraw.Draw(im)
    def P(x, z): return ((x - (px - ext)) / (2 * ext) * S, ((pz + ext) - z) / (2 * ext) * S)
    for poly in m['protect'].values():
        dr.polygon([P(*q) for q in poly], outline=(255, 80, 80))
    cx_, cz_ = P(px, pz); dr.ellipse([cx_ - 4, cz_ - 4, cx_ + 4, cz_ + 4], fill=(255, 0, 0))
    hf = math.degrees(math.atan(th))
    for a in (yaw - hf, yaw, yaw + hf):
        ex = px + math.sin(math.radians(a)) * 700; ez = pz + math.cos(math.radians(a)) * 700
        dr.line([P(px, pz), P(ex, ez)], fill=(255, 255, 0))
    im.save(os.path.join(out, f'{name}_top.png'))
    return rep


if __name__ == '__main__':
    out = sys.argv[1] if len(sys.argv) > 1 else '.'
    os.makedirs(out, exist_ok=True)
    only = sys.argv[2:] if len(sys.argv) > 2 else None
    for c in CANDIDATES:
        if only and c[0] not in only:
            continue
        print('\n'.join(plan(c, out)))
