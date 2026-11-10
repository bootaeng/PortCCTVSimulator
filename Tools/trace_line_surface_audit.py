"""Walk a straight line in the surface audit grid and print every change of surface / height / component.
usage: python -I Tools/trace_line_surface_audit.py <map> x0 z0 x1 z1"""
import sys, struct, json, numpy as np
m = sys.argv[1]; x0, z0, x1, z1 = map(float, sys.argv[2:6])
b = open(f'Logs/surface_audit/{m}.bin', 'rb').read()
N, half, cell = struct.unpack('<iff', b[:12])
a = np.frombuffer(b[12:], dtype=np.dtype([('c', 'u1'), ('f', 'u1'), ('k', '<i2'), ('h', '<f4'), ('vc', '<i4'), ('pc', '<i4')]), count=N * N).reshape(N, N)
kinds = json.load(open(f'Logs/surface_audit/{m}.json'))['kinds']
CL = 'none ground DRIVE walk BLOCK stair water'.split()
n = int(max(abs(x1 - x0), abs(z1 - z0)) / cell) + 1
prev = None
for t in np.linspace(0, 1, n):
    x, z = x0 + (x1 - x0) * t, z0 + (z1 - z0) * t
    e = a[int((z + half) / cell), int((x + half) / cell)]
    key = (kinds[e['k']] if e['k'] >= 0 else '-', CL[e['c']], round(float(e['h']), 2), int(e['vc']), int(e['pc']))
    if key != prev:
        print(f'({x:7.1f},{z:7.1f}) {key[0]:14s} {key[1]:6s} h {key[2]:6.2f} vcomp {key[3]:6d} pcomp {key[4]:6d}')
        prev = key
