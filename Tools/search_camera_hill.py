"""Grid search for a shore camera hill (uses plan_camera_hill's ProcNoise port; no Unity).

For a fixed camera position/yaw near the coast it searches hill centre and size so that
  * the camera stands on the hill's flank (unit height 0.22..0.6, so sy stays sane),
  * the hill stays out of the protected water regions and away from default-scenario vessels,
  * the sea is reached within 130 m in front of the camera (the camera really is at the coast),
then ray-casts the best few and prints them, smallest lower-image hill coverage first.
Usage: python -X utf8 Tools/search_camera_hill.py <name> [out_dir]
"""
import math
import os
import sys

import numpy as np

sys.path.insert(0, os.path.dirname(__file__))
import plan_camera_hill as P  # noqa: E402

_grids = {}


def unit_grid(idx):
    if idx not in _grids:
        _grids[idx] = P.HillGrid(idx, 0, 0, 1, 1, 0, 1).h
    return _grids[idx]


class FastHill(P.HillGrid):
    def __init__(self, idx, cx, cz, sx, sz, base, sy, n=128):
        self.cx, self.cz, self.sx, self.sz, self.base, self.sy, self.n = cx, cz, sx, sz, base, sy, n
        self.h = unit_grid(idx)


VESSELS = {
    'container_terminal_01': [(-270, 45, 100), (40, -230, 100), (150, -200, 20), (-80, -150, 20), (100, -90, 30), (330, -680, 170)],
    'fishing_port_01': [(-98, 27, 15), (10, -70, 15), (-40, -20, 15), (120, -300, 20), (-60, -420, 15), (30, -260, 10)],
    'marina_01': [(60, -150, 10), (320, -235, 10), (345, -250, 10), (200, -320, 10), (40, -420, 10), (150, -520, 10)],
}

SETUPS = {  # name: map, hill idx, camera (x, y, z), (pitch, yaw), vfov
    'ct_D': ('container_terminal_01', 2, (470, 58, -142), (8, 245), 50),
    'fp_B': ('fishing_port_01', 0, (-238, 40, -36), (9, 128), 50),
    'mr_B': ('marina_01', 3, (912, 60, -230), (9, 262), 48),
}


def evaluate(name, cx, cz, sx, sz):
    mp, idx, (px, py, pz), (pitch, yaw), vfov = SETUPS[name]
    m = P.MAPS[mp]
    h = unit_grid(idx)
    u = (px - cx) / sx + 0.5; w = (pz - cz) / sz + 0.5
    if not (0.05 < u < 0.95 and 0.05 < w < 0.95):
        return None
    hu = float(P.hill_height(idx, u, w))
    if not (0.22 <= hu <= 0.6):
        return None
    sy = (py - P.MAST + 5.0) / hu
    hill = FastHill(idx, cx, cz, sx, sz, -5.0, sy)
    for poly in m['protect'].values():
        xs = np.array([q[0] for q in poly]); zs = np.array([q[1] for q in poly])
        gx, gz = np.meshgrid(np.linspace(xs.min(), xs.max(), 90), np.linspace(zs.min(), zs.max(), 90))
        ins = P.point_in_poly(gx, gz, poly)
        if (hill.y(gx[ins], gz[ins]) > -1.0).any():
            return None
    for (vx, vz, rad) in VESSELS[mp]:
        a = np.linspace(0, 2 * math.pi, 24)
        rr = np.array([0.0, rad * 0.5, rad])
        X = vx + np.outer(rr, np.cos(a)).ravel(); Z = vz + np.outer(rr, np.sin(a)).ravel()
        if (hill.y(X, Z) > -1.5).any():
            return None
    # distance to the sea along the view direction
    ts = np.arange(0, 400, 2.0)
    X = px + math.sin(math.radians(yaw)) * ts; Z = pz + math.cos(math.radians(yaw)) * ts
    land = np.zeros_like(X, bool)
    for (x0, x1, z0, z1) in m['land']:
        land |= (X >= x0) & (X <= x1) & (Z >= z0) & (Z <= z1)
    ground = np.maximum(hill.y(X, Z), np.where(land, P.DECK, -1.0))
    wet = np.nonzero(ground < 0)[0]
    if len(wet) == 0 or ts[wet[0]] > 130:
        return None
    peak = float(h.max() * sy - 5)
    return dict(cx=cx, cz=cz, sx=sx, sz=sz, hu=hu, sy=sy, peak=peak, sea_m=float(ts[wet[0]]))


def main():
    name = sys.argv[1]
    out = sys.argv[2] if len(sys.argv) > 2 else '.'
    mp, idx, (px, py, pz), (pitch, yaw), vfov = SETUPS[name]
    back = math.radians(yaw + 180)
    res = []
    for dev in range(-60, 61, 15):
        a = back + math.radians(dev)
        for d in range(30, 260, 15):
            cx = px + math.sin(a) * d; cz = pz + math.cos(a) * d
            for sx in (240, 300, 360, 420, 480):
                for sz in (240, 300, 360, 420, 480):
                    r = evaluate(name, round(cx), round(cz), sx, sz)
                    if r and r['peak'] < (py - P.MAST) * 3.2:
                        res.append(r)
    print(f'{name}: {len(res)} feasible')
    # prefer camera mid-flank, short sea distance, modest peak
    res.sort(key=lambda r: (r['sea_m'] / 130 + abs(r['hu'] - 0.38) * 2 + r['peak'] / ((py - P.MAST) * 3.2)))
    best = []
    os.makedirs(out, exist_ok=True)
    for r in res[:8]:
        c = (name, mp, idx, (r['cx'], r['cz']), (r['sx'], r['sz']), (px, py, pz), (pitch, yaw), vfov)
        rep = P.plan(c, out)
        cov = float(rep[-1].split()[-1].rstrip('%'))
        best.append((cov, r, rep))
        os.replace(os.path.join(out, f'{name}_view.png'), os.path.join(out, f'{name}_view_{len(best)}.png'))
        os.replace(os.path.join(out, f'{name}_top.png'), os.path.join(out, f'{name}_top_{len(best)}.png'))
    for k, (cov, r, rep) in enumerate(best, 1):
        print(f'#{k} sea {r["sea_m"]:.0f} m', '\n'.join(rep))


if __name__ == '__main__':
    main()
