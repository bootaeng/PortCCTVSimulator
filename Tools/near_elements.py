"""List harbor-dump elements whose footprint (yaw-aware for boxes) contains / is near a point.
usage: python -I Tools/near_elements.py <map> x z [radius] [kind,kind]"""
import sys, json, math
m = sys.argv[1]; x, z = float(sys.argv[2]), float(sys.argv[3]); r = float(sys.argv[4]) if len(sys.argv) > 4 else 5
kinds = set(sys.argv[5].split(',')) if len(sys.argv) > 5 else None
d = json.load(open(f'Logs/harbor_dump/{m}.json', encoding='utf-8'))
for e in d['elements']:
    if kinds and e['kind'] not in kinds: continue
    c, s = e['center'], e['size']
    if e['shape'] == 'prefab':
        if math.hypot(c['x'] - x, c['z'] - z) <= r + 3:
            print(f"P {e['id']:40s} {e['kind']:14s} {e.get('prefab')} ({c['x']:.1f},{c['y']:.2f},{c['z']:.1f}) scale ({s['x']:.2f},{s['y']:.2f},{s['z']:.2f}) yaw {e.get('yaw_deg',0):.0f}")
        continue
    yaw = math.radians(e.get('yaw_deg', 0) + (e.get('euler_deg') or {}).get('y', 0))
    dx, dz = x - c['x'], z - c['z']
    lx, lz = dx * math.cos(yaw) - dz * math.sin(yaw), dx * math.sin(yaw) + dz * math.cos(yaw)
    if abs(lx) <= abs(s['x']) / 2 + r and abs(lz) <= abs(s['z']) / 2 + r:
        print(f"B {e['id']:40s} {e['kind']:14s} {e.get('material')} c({c['x']:.1f},{c['y']:.2f},{c['z']:.1f}) size({s['x']:.1f},{s['y']:.2f},{s['z']:.1f}) top {c['y']+s['y']/2:.2f} yaw {e.get('yaw_deg',0):.0f}")
