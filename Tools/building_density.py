"""Building density per zone from two harbor dumps (before / after).
Counts building-like elements (prefab buildings / houses, building boxes taller than 3 m, shopfront modules) and their plan
footprint coverage (union area on a 1 m grid) inside fixed zone rectangles.
usage: python -I Tools/building_density.py <before_dump_dir> <after_dump_dir> [out.json]"""
import json, math, sys
import numpy as np

ZONES = {
    'fishing_port_01': {'fp_backland_shops (x -700..700, z 120..200)': (-700, 700, 120, 200),
                        'fp_fishing_town (x 256..532, z 125..170)': (256, 532, 125, 170),
                        'fp_west_village (x -980..-430, z -30..40)': (-980, -430, -30, 40),
                        'fp_hill_village (x -680..-360, z 174..240)': (-680, -360, 174, 240)},
    'marina_01': {'mr_west_hamlets (x -3100..-2760, z -1200..-600)': (-3100, -2760, -1200, -600),
                  'mr_city_west (x -1500..-720, z 100..900)': (-1500, -720, 100, 900),
                  'mr_central_city (x -100..470, z 232..560)': (-100, 470, 232, 560),
                  'mr_promenade_shops (x -1250..90, z -170..140)': (-1250, 90, -170, 140)},
    'container_terminal_01': {'ct_hinterland (x -1500..1500, z 400..1000)': (-1500, 1500, 400, 1000)},
}
BKINDS = {'building', 'house', 'apartment'}
FOOT = {'house': 9.0, 'building': 18.0}


def items(d):
    for e in d['elements']:
        k = e.get('kind')
        c, s = e['center'], e['size']
        if e['shape'] == 'prefab' and (k in BKINDS or k == 'facade_detail'):
            if k == 'facade_detail':
                yield 'shopfront', c['x'], c['z'], 3.0, 1.2
            else:
                f = FOOT.get(k, 12.0) * abs(s['x'])
                yield 'prefab_' + k, c['x'], c['z'], f, f
        elif e['shape'] == 'box' and k in BKINDS and s['y'] > 3.0 and min(abs(s['x']), abs(s['z'])) > 2.0:   # skip doors, sign bands, glass strips
            yield 'box_' + k, c['x'], c['z'], abs(s['x']), abs(s['z'])


def stats(path, rect):
    d = json.load(open(path, encoding='utf-8'))
    x0, x1, z0, z1 = rect
    grid = np.zeros((int(z1 - z0), int(x1 - x0)), bool)
    n = {}
    for kind, x, z, w, dd in items(d):
        if not (x0 <= x < x1 and z0 <= z < z1): continue
        if kind != 'shopfront': n[kind] = n.get(kind, 0) + 1
        else: n['shopfront_modules'] = n.get('shopfront_modules', 0) + 1
        a0, a1 = int(max(0, x - w / 2 - x0)), int(min(x1 - x0, x + w / 2 - x0))
        b0, b1 = int(max(0, z - dd / 2 - z0)), int(min(z1 - z0, z + dd / 2 - z0))
        grid[b0:b1, a0:a1] = True
    buildings = sum(v for k, v in n.items() if k != 'shopfront_modules')
    return {'buildings': buildings, 'by_kind': n, 'footprint_cover_pct': round(100.0 * grid.mean(), 2)}


out = {}
for m, zones in ZONES.items():
    for zn, rect in zones.items():
        b = stats(f'{sys.argv[1]}/{m}.json', rect); a = stats(f'{sys.argv[2]}/{m}.json', rect)
        out[f'{m} / {zn}'] = {'before': b, 'after': a}
        print(f'{m:22s} {zn:48s} buildings {b["buildings"]:5d} -> {a["buildings"]:5d}   cover {b["footprint_cover_pct"]:6.2f}% -> {a["footprint_cover_pct"]:6.2f}%   shopfronts {a["by_kind"].get("shopfront_modules", 0)}')
if len(sys.argv) > 3: json.dump(out, open(sys.argv[3], 'w'), indent=1)
