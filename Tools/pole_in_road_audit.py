"""List street furniture prefabs (poles, lamps, signals, signs, benches) whose base lies on a carriageway box.
usage: python -I Tools/pole_in_road_audit.py [map ...]"""
import sys, json, math
DRIVE = {'road', 'lane', 'turning_head', 'street_road', 'boulevard', 'ramp_road', 'access_road', 'bridge_deck'}
FURN = {'power_pole', 'street_light', 'signal', 'street', 'bench', 'sign', 'road_sign', 'tree'}
maps = sys.argv[1:] or ['fishing_port_01', 'marina_01', 'container_terminal_01']
for m in maps:
    d = json.load(open(f'Logs/harbor_dump/{m}.json', encoding='utf-8'))
    E = d['elements']
    boxes = []
    for e in E:
        if e['shape'] == 'prefab' or e['kind'] not in DRIVE: continue
        c, s = e['center'], e['size']
        yaw = math.radians(e.get('yaw_deg', 0) + (e.get('euler_deg') or {}).get('y', 0))
        boxes.append((c['x'], c['z'], abs(s['x']) / 2, abs(s['z']) / 2, math.cos(yaw), math.sin(yaw), c['y'] + s['y'] / 2, e['id'], e['kind'], e.get('material')))
    hits = []
    for e in E:
        if e['shape'] != 'prefab' or e['kind'] not in FURN: continue
        x, z, y = e['center']['x'], e['center']['z'], e['center']['y']
        for bx, bz, hx, hz, co, si, top, bid, bk, bm in boxes:
            dx, dz = x - bx, z - bz
            lx, lz = dx * co - dz * si, dx * si + dz * co
            m_ = 0.3
            if abs(lx) <= hx - m_ and abs(lz) <= hz - m_ and abs(y - top) < 3:
                hits.append((e['kind'], e.get('prefab'), round(x, 1), round(z, 1), bid, bk, bm, round(min(hx - abs(lx), hz - abs(lz)), 1)))
                break
    from collections import Counter
    print(f'== {m}: {len(hits)} furniture on carriageway', Counter(h[0] for h in hits))
    for h in hits[:60]: print('  ', h)
