"""Manabashima lighthouse scan -> breakwater-light tower meshes + textures (PortCCTVSimulator, 2026-11-09).

Input : the Sketchfab GLB (CC BY 4.0, "Manabashima Lighthouse" by tokiwatari), kept unmodified in ThirdParty_raw/.
Output: Assets/PortCCTVSimulator/ThirdParty/SketchfabCCBY/lighthouse2/
          manabashima_tower_lod{0,1,2}.obj   tower only (rocks, sea floor and the island removed), metres, base at y=0,
                                             tower axis at x=z=0, glTF axes (Unity's OBJ importer flips x)
          manabashima_tower_albedo_white.png repacked texture (only the tower's UV islands, original texel density)
          manabashima_tower_albedo_red.png   same, white paint re-coloured red (luminance kept, so dirt / streaks /
                                             rust stay; dark parts — lantern, rails, door — untouched)
          manabashima_tower_normal.png       tangent-space detail normal from the albedo luminance (weak)
          manabashima_tower_meta.json        scale, axis, height, door direction, triangle counts, hashes
Steps : 1) keep triangles whose three vertices lie within 0.42 scan units of the tower axis and above the concrete pad
           (y > -3.80); the island rock (114k + 89k triangles) is dropped;
        2) UV islands (vertex-connected) are cut out of the 4096 px atlas with a 3 px margin and shelf-packed into a new
           2048 px atlas (no resampling of the kept texels);
        3) scale: the scan's tower stands 1.676 units from pad to cap; the maker states ~10 m (tokiwatari.jp), so
           1 unit = 5.966 m;
        4) LOD1/LOD2 by quadric decimation (fast-simplification), UVs carried by nearest original vertex.
usage: python -I Tools/process_manabashima_lighthouse.py <glb> <outdir>
"""
import hashlib, io, json, struct, sys, os
import numpy as np
from PIL import Image, ImageFilter

CT = {5120: np.int8, 5121: np.uint8, 5122: np.int16, 5123: np.uint16, 5125: np.uint32, 5126: np.float32}
NC = {'SCALAR': 1, 'VEC2': 2, 'VEC3': 3, 'VEC4': 4}


def load(p):
    b = open(p, 'rb').read()
    n = struct.unpack('<I', b[12:16])[0]
    j = json.loads(b[20:20 + n]); off = 20 + n
    bl = struct.unpack('<I', b[off:off + 4])[0]
    return j, b[off + 8: off + 8 + bl]


def acc(j, bin_, i):
    a = j['accessors'][i]; bv = j['bufferViews'][a['bufferView']]
    dt = CT[a['componentType']]; nc = NC[a['type']]
    o = bv.get('byteOffset', 0) + a.get('byteOffset', 0)
    stride = bv.get('byteStride', 0); isz = np.dtype(dt).itemsize * nc
    if stride and stride != isz:
        raw = np.frombuffer(bin_, np.uint8, count=stride * (a['count'] - 1) + isz, offset=o)
        return np.lib.stride_tricks.as_strided(raw, shape=(a['count'], isz), strides=(stride, 1)).copy().view(dt).reshape(a['count'], nc)
    return np.frombuffer(bin_, dt, count=a['count'] * nc, offset=o).reshape(a['count'], nc)


def node_mat(nd):
    if 'matrix' in nd: return np.array(nd['matrix']).reshape(4, 4).T
    t = nd.get('translation', [0, 0, 0]); x, y, z, w = nd.get('rotation', [0, 0, 0, 1]); s = nd.get('scale', [1, 1, 1])
    R = np.array([[1 - 2 * (y * y + z * z), 2 * (x * y - z * w), 2 * (x * z + y * w)],
                  [2 * (x * y + z * w), 1 - 2 * (x * x + z * z), 2 * (y * z - x * w)],
                  [2 * (x * z - y * w), 2 * (y * z + x * w), 1 - 2 * (x * x + y * y)]])
    M = np.eye(4); M[:3, :3] = R * np.array(s); M[:3, 3] = t
    return M


def gather(j, bin_):
    out = []

    def walk(ni, P):
        nd = j['nodes'][ni]; M = P @ node_mat(nd)
        if 'mesh' in nd:
            for pr in j['meshes'][nd['mesh']]['primitives']:
                pos = acc(j, bin_, pr['attributes']['POSITION']).astype(np.float64)
                out.append(((M[:3, :3] @ pos.T).T + M[:3, 3], acc(j, bin_, pr['indices']).reshape(-1, 3).astype(np.int64),
                            acc(j, bin_, pr['attributes']['TEXCOORD_0']).astype(np.float64)))
        for c in nd.get('children', []): walk(c, M)
    for r in j['scenes'][j.get('scene', 0)]['nodes']: walk(r, np.eye(4))
    return out


def write_obj(path, V, UV, I, name):
    # per-vertex normals (area weighted)
    N = np.zeros_like(V)
    fn = np.cross(V[I[:, 1]] - V[I[:, 0]], V[I[:, 2]] - V[I[:, 0]])
    for k in range(3): np.add.at(N, I[:, k], fn)
    N /= np.maximum(np.linalg.norm(N, axis=1, keepdims=True), 1e-12)
    with open(path, 'w') as f:
        f.write(f'# {name}: derived from "Manabashima Lighthouse" by tokiwatari (CC BY 4.0), see SOURCE_lighthouse2.md\no {name}\n')
        f.write(''.join(f'v {x:.5f} {y:.5f} {z:.5f}\n' for x, y, z in V))
        f.write(''.join(f'vt {u:.6f} {v:.6f}\n' for u, v in UV))
        f.write(''.join(f'vn {x:.5f} {y:.5f} {z:.5f}\n' for x, y, z in N))
        f.write(''.join(f'f {a + 1}/{a + 1}/{a + 1} {b + 1}/{b + 1}/{b + 1} {c + 1}/{c + 1}/{c + 1}\n' for a, b, c in I))


def islands(I, nv):
    parent = np.arange(nv)

    def find(a):
        while parent[a] != a:
            parent[a] = parent[parent[a]]; a = parent[a]
        return a
    for a, b, c in I:
        ra, rb, rc = find(a), find(b), find(c)
        parent[rb] = ra; parent[find(rc)] = ra
    return np.array([find(k) for k in range(nv)])


def delight(img, Vm, UVn, I, S):
    """Removes the scan's baked sun / sky shading. Every atlas texel is mapped back to the tower surface (azimuth, height) by
    rasterising the triangles in the new UV space; the luminance of the white paint is averaged per 10 deg x 0.5 m cell and
    smoothed; every texel is multiplied by target / smoothed paint luminance (clamped 0.7..2.6). Local detail (seams, rust
    streaks, door, rails) is kept; the side that was in shadow during the survey no longer reads darker. Then a white balance
    makes the paint neutral (the overcast survey sky made it blue-grey)."""
    a = np.asarray(img).astype(np.float32) / 255.0
    az = np.full((S, S), np.nan, np.float32); hy = np.full((S, S), np.nan, np.float32)
    P = UVn * S
    for t in I:
        p0, p1, p2 = P[t[0]], P[t[1]], P[t[2]]
        x0, y0 = np.floor(np.minimum(np.minimum(p0, p1), p2)).astype(int); x1, y1 = np.ceil(np.maximum(np.maximum(p0, p1), p2)).astype(int)
        x0, y0 = max(x0, 0), max(y0, 0); x1, y1 = min(x1, S - 1), min(y1, S - 1)
        if x1 < x0 or y1 < y0: continue
        xs, ys = np.meshgrid(np.arange(x0, x1 + 1) + 0.5, np.arange(y0, y1 + 1) + 0.5)
        d = (p1[1] - p2[1]) * (p0[0] - p2[0]) + (p2[0] - p1[0]) * (p0[1] - p2[1])
        if abs(d) < 1e-9: continue
        l0 = ((p1[1] - p2[1]) * (xs - p2[0]) + (p2[0] - p1[0]) * (ys - p2[1])) / d
        l1 = ((p2[1] - p0[1]) * (xs - p2[0]) + (p0[0] - p2[0]) * (ys - p2[1])) / d
        l2 = 1 - l0 - l1
        inside = (l0 >= -0.02) & (l1 >= -0.02) & (l2 >= -0.02)
        if not inside.any(): continue
        X = l0 * Vm[t[0], 0] + l1 * Vm[t[1], 0] + l2 * Vm[t[2], 0]
        Z = l0 * Vm[t[0], 2] + l1 * Vm[t[1], 2] + l2 * Vm[t[2], 2]
        Y = l0 * Vm[t[0], 1] + l1 * Vm[t[1], 1] + l2 * Vm[t[2], 1]
        sl = (slice(y0, y1 + 1), slice(x0, x1 + 1))
        az[sl] = np.where(inside, np.degrees(np.arctan2(X, Z)), az[sl]); hy[sl] = np.where(inside, Y, hy[sl])
    lum = a @ np.array([0.2126, 0.7152, 0.0722])
    mx, mn = a.max(2), a.min(2); sat = (mx - mn) / np.maximum(mx, 1e-4)
    known = ~np.isnan(az)
    paint = known & (sat < 0.30) & (lum > 0.25)
    nb_a, nb_h = 36, 22
    ai = np.clip(((np.nan_to_num(az) + 180) / 10).astype(int), 0, nb_a - 1); hi = np.clip((np.nan_to_num(hy) / 0.5).astype(int), 0, nb_h - 1)
    sumL = np.zeros((nb_h, nb_a)); cnt = np.zeros((nb_h, nb_a))
    np.add.at(sumL, (hi[paint], ai[paint]), lum[paint]); np.add.at(cnt, (hi[paint], ai[paint]), 1)
    grid = np.where(cnt > 20, sumL / np.maximum(cnt, 1), np.nan)
    # fill and smooth (wrap in azimuth)
    for _ in range(40):
        g2 = grid.copy()
        for k in range(nb_h):
            for m in range(nb_a):
                if np.isnan(grid[k, m]):
                    nb = [grid[k, (m - 1) % nb_a], grid[k, (m + 1) % nb_a]] + ([grid[k - 1, m]] if k > 0 else []) + ([grid[k + 1, m]] if k < nb_h - 1 else [])
                    nb = [v for v in nb if not np.isnan(v)]
                    if nb: g2[k, m] = np.mean(nb)
        grid = g2
    grid = np.nan_to_num(grid, nan=np.nanmean(grid))
    sm = grid.copy()
    for _ in range(3):
        sm = (np.roll(sm, 1, 1) + np.roll(sm, -1, 1) + sm * 2) / 4
        sm[1:-1] = (sm[:-2] + sm[2:] + sm[1:-1] * 2) / 4
    target = np.percentile(sm, 85)
    corr = np.clip(target / np.maximum(sm, 1e-3), 0.7, 2.6)
    f = np.where(known, corr[hi, ai], 1.0)
    out = np.clip(a * f[..., None], 0, 1)
    print('delight: paint luminance per azimuth (mid height) min/max', round(float(sm[8].min()), 3), round(float(sm[8].max()), 3), 'correction range', round(float(corr.min()), 2), round(float(corr.max()), 2))
    # vegetation caught by the scan on the base pad (green-dominant texels) -> concrete grey of the same luminance
    green = known & (out[..., 1] > out[..., 0] * 1.08) & (out[..., 1] > out[..., 2] * 1.02)
    gl = (out @ np.array([0.2126, 0.7152, 0.0722]))[..., None]
    out = np.where(green[..., None], np.concatenate([gl * 1.02, gl, gl * 0.95], 2), out)
    print('vegetation texels greyed', int(green.sum()))
    # white balance: make the bright tower texels neutral (the survey sky left the white paint blue-grey)
    l1 = out @ np.array([0.2126, 0.7152, 0.0722])
    bright = known & (l1 > 0.45)
    med = np.median(out[bright], axis=0); gain = med.mean() / med
    out = np.clip(out * gain[None, None, :], 0, 1)
    l2 = out @ np.array([0.2126, 0.7152, 0.0722])
    out = np.clip(out * (0.86 / max(float(np.median(l2[paint])), 1e-3)), 0, 1)
    print('white balance gain', gain.round(3))
    return Image.fromarray((out * 255).astype(np.uint8))


def main():
    src, outdir = sys.argv[1], sys.argv[2]
    os.makedirs(outdir, exist_ok=True)
    j, bin_ = load(src)
    im = j['images'][0]; bv = j['bufferViews'][im['bufferView']]
    atlas = Image.open(io.BytesIO(bin_[bv.get('byteOffset', 0): bv.get('byteOffset', 0) + bv['byteLength']])).convert('RGB')
    W, H = atlas.size
    CX, CZ, YMIN = -0.917, 3.037, -3.80
    Vs, Is, UVs, off = [], [], [], 0
    for P, T, UV in gather(j, bin_):
        r = np.hypot(P[:, 0] - CX, P[:, 2] - CZ)
        keep = (r < 0.42) & (P[:, 1] > YMIN)
        T2 = T[keep[T].all(1)]
        if len(T2) == 0: continue
        used = np.unique(T2); remap = -np.ones(len(P), np.int64); remap[used] = np.arange(len(used))
        Vs.append(P[used]); UVs.append(UV[used]); Is.append(remap[T2] + off); off += len(used)
    V = np.vstack(Vs); I = np.vstack(Is); UV = np.vstack(UVs)
    # Level the tower: the scan's up axis is not plumb (the tower leant ~15 deg in the first render). Fit a circle (Kasa least
    # squares) to the shaft points in thin slices, fit a straight line through the slice centres, and rotate the mesh so that
    # line is vertical.
    def circle(xz):
        A = np.c_[2 * xz[:, 0], 2 * xz[:, 1], np.ones(len(xz))]; bb = (xz ** 2).sum(1)
        sol = np.linalg.lstsq(A, bb, rcond=None)[0]; return sol[0], sol[1], np.sqrt(sol[2] + sol[0] ** 2 + sol[1] ** 2)
    ys, cs = [], []
    for y0 in np.arange(-3.62, -2.92, 0.05):
        sl = (V[:, 1] > y0) & (V[:, 1] < y0 + 0.05)
        if sl.sum() < 40: continue
        cxs, czs, rr = circle(V[sl][:, [0, 2]])
        if 0.12 < rr < 0.45: ys.append(y0 + 0.025); cs.append((cxs, czs))
    ys, cs = np.array(ys), np.array(cs)
    kx = np.polyfit(ys, cs[:, 0], 1); kz = np.polyfit(ys, cs[:, 1], 1)
    axis = np.array([kx[0], 1.0, kz[0]]); axis /= np.linalg.norm(axis)
    tilt = np.degrees(np.arccos(axis[1]))
    piv = np.array([np.polyval(kx, ys.mean()), ys.mean(), np.polyval(kz, ys.mean())])
    v = np.cross(axis, [0, 1, 0]); sn, cs_ = np.linalg.norm(v), axis[1]
    if sn > 1e-9:
        v /= sn; K = np.array([[0, -v[2], v[1]], [v[2], 0, -v[0]], [-v[1], v[0], 0]])
        Rm = np.eye(3) + np.sin(np.arccos(cs_)) * K + (1 - cs_) * K @ K
        V = (Rm @ (V - piv).T).T + piv
    print('tower axis tilt corrected:', round(float(tilt), 2), 'deg over', len(ys), 'slices')
    # scan debris on the pad right round the base flange (rock, grass bits) dropped
    low = V[:, 1] < np.percentile(V[:, 1], 0.5) + 0.07
    r0 = np.hypot(V[:, 0] - piv[0], V[:, 2] - piv[2])
    bad = low & (r0 > 0.37)
    keepT = ~bad[I].any(1); I = I[keepT]
    cx, cz = piv[0], piv[2]
    used = np.unique(I); pad_top = np.percentile(V[used, 1], 0.5)
    top = V[:, 1].max()
    scale = 10.0 / (top - pad_top)
    Vm = np.stack([(V[:, 0] - cx) * scale, (V[:, 1] - pad_top) * scale, (V[:, 2] - cz) * scale], 1)
    Vm[:, 1] = np.maximum(Vm[:, 1], 0.0)
    # keep only the vertices still referenced, re-index
    used = np.unique(I); remap = -np.ones(len(V), np.int64); remap[used] = np.arange(len(used))
    V, Vm, UV, I = V[used], Vm[used], UV[used], remap[I]

    # UV islands -> pixel boxes -> shelf pack into a new atlas
    lab = islands(I, len(V))
    boxes = {}
    px = np.stack([UV[:, 0] * W, UV[:, 1] * H], 1)            # glTF uv: v down from the image top
    for k in np.unique(lab):
        sel = lab == k
        x0, y0 = np.floor(px[sel].min(0)).astype(int) - 3
        x1, y1 = np.ceil(px[sel].max(0)).astype(int) + 3
        boxes[k] = (max(0, x0), max(0, y0), min(W, x1), min(H, y1))
    order = sorted(boxes, key=lambda k: -(boxes[k][3] - boxes[k][1]))
    S = 2048
    for S in (1024, 2048, 4096):
        pos, x, y, rowh, ok = {}, 0, 0, 0, True
        for k in order:
            bx0, by0, bx1, by1 = boxes[k]; w, h = bx1 - bx0, by1 - by0
            if x + w > S: x, y, rowh = 0, y + rowh, 0
            if y + h > S: ok = False; break
            pos[k] = (x, y); x += w; rowh = max(rowh, h)
        if ok: break
    new = Image.new('RGB', (S, S), (200, 200, 200))
    for k, (nx, ny) in pos.items():
        bx0, by0, bx1, by1 = boxes[k]
        new.paste(atlas.crop((bx0, by0, bx1, by1)), (nx, ny))
    UVn = np.zeros_like(UV)
    for k, (nx, ny) in pos.items():
        sel = lab == k; bx0, by0 = boxes[k][0], boxes[k][1]
        UVn[sel, 0] = (px[sel, 0] - bx0 + nx) / S
        UVn[sel, 1] = (px[sel, 1] - by0 + ny) / S
    # OBJ uses v up from the bottom
    UVobj = np.stack([UVn[:, 0], 1.0 - UVn[:, 1]], 1)
    # bleed the islands a little into the grey background (mip filtering)
    mask = Image.new('L', (S, S), 0)
    for k, (nx, ny) in pos.items():
        bx0, by0, bx1, by1 = boxes[k]; mask.paste(255, (nx, ny, nx + bx1 - bx0, ny + by1 - by0))
    bg = new.filter(ImageFilter.GaussianBlur(6))
    new = Image.composite(new, bg, mask)
    new = delight(new, Vm, UVn, I, S)
    new.save(os.path.join(outdir, 'manabashima_tower_albedo_white.png'))

    # red variant: white paint (bright, low saturation) -> signal red with the same luminance pattern
    a = np.asarray(new).astype(np.float32) / 255.0
    mx, mn = a.max(2), a.min(2); sat = (mx - mn) / np.maximum(mx, 1e-4); lum = a @ np.array([0.2126, 0.7152, 0.0722])
    paint = np.clip((0.40 - sat) / 0.15, 0, 1) * np.clip((lum - 0.38) / 0.15, 0, 1)
    red = np.array([0.72, 0.09, 0.07])
    rel = np.clip(lum / max(np.percentile(lum[paint > 0.5], 70) if (paint > 0.5).any() else 0.8, 1e-3), 0, 1.25)
    tint = red[None, None, :] * rel[..., None]
    out = a * (1 - paint[..., None]) + tint * paint[..., None]
    Image.fromarray((np.clip(out, 0, 1) * 255).astype(np.uint8)).save(os.path.join(outdir, 'manabashima_tower_albedo_red.png'))
    green = np.array([0.08, 0.42, 0.16])                       # starboard-hand light of the legacy IALA map
    outg = a * (1 - paint[..., None]) + green[None, None, :] * rel[..., None] * paint[..., None]
    Image.fromarray((np.clip(outg, 0, 1) * 255).astype(np.uint8)).save(os.path.join(outdir, 'manabashima_tower_albedo_green.png'))
    # weak detail normal from luminance (Sobel), tangent space (OpenGL convention, +y up)
    g = np.asarray(new.convert('L').filter(ImageFilter.GaussianBlur(1.0))).astype(np.float32) / 255.0
    dx = np.zeros_like(g); dy = np.zeros_like(g)
    dx[:, 1:-1] = (g[:, 2:] - g[:, :-2]) * 0.5; dy[1:-1, :] = (g[2:, :] - g[:-2, :]) * 0.5
    k = 2.0
    n = np.stack([-dx * k, dy * k, np.ones_like(g)], 2); n /= np.linalg.norm(n, axis=2, keepdims=True)
    Image.fromarray(((n * 0.5 + 0.5) * 255).astype(np.uint8)).save(os.path.join(outdir, 'manabashima_tower_normal.png'))

    write_obj(os.path.join(outdir, 'manabashima_tower_lod0.obj'), Vm, UVobj, I, 'manabashima_tower_lod0')
    counts = {'lod0': int(len(I))}
    try:
        import fast_simplification
        from scipy.spatial import cKDTree
        kd = cKDTree(Vm)
        for name, frac in (('lod1', 0.78), ('lod2', 0.95)):
            pts, faces = fast_simplification.simplify(Vm.astype(np.float32), I.astype(np.int32), target_reduction=frac)
            _, nn = kd.query(pts)
            write_obj(os.path.join(outdir, f'manabashima_tower_{name}.obj'), pts.astype(np.float64), UVobj[nn], faces.astype(np.int64), f'manabashima_tower_{name}')
            counts[name] = int(len(faces))
    except ImportError as e:
        print('no decimation:', e)
    # the scan's door: the darkest vertical band low on the shaft (y 0.3..2.2 m), reported as a yaw in the OBJ frame
    lowv = (Vm[:, 1] > 0.3) & (Vm[:, 1] < 2.2)
    tex = np.asarray(new.convert('L')).astype(np.float32)
    ui = np.clip((UVn[lowv, 0] * S).astype(int), 0, S - 1); vi = np.clip((UVn[lowv, 1] * S).astype(int), 0, S - 1)
    L = tex[vi, ui]; ang = np.degrees(np.arctan2(Vm[lowv, 0], Vm[lowv, 2]))
    bins = np.arange(-180, 181, 15); means = [L[(ang >= b) & (ang < b + 15)].mean() if ((ang >= b) & (ang < b + 15)).any() else 999 for b in bins[:-1]]
    door = float(bins[int(np.argmin(means))] + 7.5)
    meta = {'source_glb_sha256': hashlib.sha256(open(src, 'rb').read()).hexdigest(), 'scale_m_per_unit': scale,
            'axis_scan': [cx, cz], 'pad_top_scan_y': float(pad_top), 'height_m': float(Vm[:, 1].max()),
            'base_radius_m': float(np.percentile(np.hypot(Vm[Vm[:, 1] < 0.5, 0], Vm[Vm[:, 1] < 0.5, 2]), 90)),
            'triangles': counts, 'atlas_px': S, 'islands': len(pos), 'door_yaw_deg_gltf_frame': door,
            'door_band_luminance': {int(b): (None if m == 999 else round(float(m), 1)) for b, m in zip(bins[:-1], means)}}
    for fn in sorted(os.listdir(outdir)):
        if fn.endswith(('.obj', '.png')): meta.setdefault('outputs', {})[fn] = hashlib.sha256(open(os.path.join(outdir, fn), 'rb').read()).hexdigest()
    json.dump(meta, open(os.path.join(outdir, 'manabashima_tower_meta.json'), 'w'), indent=1)
    print(json.dumps({k: meta[k] for k in ('scale_m_per_unit', 'height_m', 'base_radius_m', 'triangles', 'atlas_px', 'islands', 'door_yaw_deg_gltf_frame')}))


if __name__ == '__main__':
    main()
