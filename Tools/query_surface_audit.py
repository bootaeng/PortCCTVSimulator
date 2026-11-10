"""Print the audit cells around world points: python -I Tools/query_surface_audit.py <map> x z [radius]"""
import sys, struct, json, numpy as np
m = sys.argv[1]; x, z = float(sys.argv[2]), float(sys.argv[3]); r = int(sys.argv[4]) if len(sys.argv) > 4 else 3
b = open(f'Logs/surface_audit/{m}.bin', 'rb').read()
N, half, cell = struct.unpack('<iff', b[:12])
a = np.frombuffer(b[12:], dtype=np.dtype([('c', 'u1'), ('f', 'u1'), ('k', '<i2'), ('h', '<f4'), ('vc', '<i4'), ('pc', '<i4')]), count=N * N).reshape(N, N)
kinds = json.load(open(f'Logs/surface_audit/{m}.json'))['kinds']
CL = 'none ground DRIVE walk BLOCK stair water'.split()
i0, j0 = int((x + half) / cell), int((z + half) / cell)
for j in range(j0 + r, j0 - r - 1, -1):
    row = []
    for i in range(i0 - r, i0 + r + 1):
        e = a[j, i]
        row.append(f"{kinds[e['k']][:7] if e['k'] >= 0 else '-':7s}{CL[e['c']][0]}{e['h']:5.2f}v{e['vc'] if e['vc']>=0 else '-'}{'*' if e['f']&1 else ''}")
    print(f"z{-half + j + 0.5:7.1f} " + ' '.join(row))
