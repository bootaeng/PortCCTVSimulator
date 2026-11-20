"""Frame-to-frame check of a lens-rain capture sequence (2026-11-12).
For consecutive captures <prefix>_t000, _t001, ... in a capture folder:
  * rgb change: mean |dRGB| and share of pixels changing by > 24 levels (the scene, water time, sensor noise seed are fixed in
    these sequences, so changes come from the water on the window);
  * water state from metadata: drop / trail counts, image cover fraction, and per running drop the displacement between
    frames matched to the nearest running / stopped drop of the next frame (speed in px/s, largest jump);
  * label check: whether mask_instances.png / visible pixel counts stay identical (they change when vessels move, e.g. an
    anchored vessel swinging; the drops never change them).
usage: python -I Tools/lensrain_sequence_check.py <captures_dir> <prefix> [out.json]"""
import glob, json, math, os, sys
import numpy as np
from PIL import Image

root, prefix = sys.argv[1], sys.argv[2]
dirs = sorted(d for d in glob.glob(os.path.join(root, prefix + '_t*')) if os.path.isdir(d))
frames = []
for d in dirs:
    m = json.load(open(os.path.join(d, 'metadata.json'), encoding='utf-8'))
    s = m.get('sensor', {})
    frames.append({'dir': d, 't': s.get('lens_drop_time_s'), 'drops': s.get('lens_drops', []), 'trails': s.get('lens_trails', []),
                   'cover': s.get('lens_drop_cover_fraction_image'),
                   'rgb': np.asarray(Image.open(os.path.join(d, 'rgb.png')).convert('RGB'), np.int16),
                   'mask': np.asarray(Image.open(os.path.join(d, 'mask_instances.png'))),
                   'labels': json.load(open(os.path.join(d, 'labels.json'), encoding='utf-8'))})
rep = {'frames': len(frames), 'pairs': []}
H, W = frames[0]['rgb'].shape[:2]
mask_same = all(np.array_equal(frames[0]['mask'], f['mask']) for f in frames)
def vis(lab): return [(o.get('object_id'), (o.get('visible_mask') or {}).get('pixel_count')) for o in lab.get('objects', [])]
labels_same = all(vis(frames[0]['labels']) == vis(f['labels']) for f in frames)
covers = []
for f in frames:
    for o in f['labels'].get('objects', []): covers.append(o.get('lens_drop_cover_fraction', 0))
max_speed, max_jump = 0.0, 0.0
for a, b in zip(frames, frames[1:]):
    dt = (b['t'] or 0) - (a['t'] or 0)
    diff = np.abs(b['rgb'] - a['rgb']).sum(2)
    run_a = [d for d in a['drops'] if d.get('kind') == 'running']
    run_b = [d for d in b['drops'] if d.get('kind') in ('running', 'stopped')]
    jumps = []
    left = 0
    for d in run_a:
        # the same drop in the next frame: below or level with it (it runs down), close sideways (meander < 5 % of W)
        cand = [e for e in run_b if e['y'] >= d['y'] - 3 and abs(e['x'] - d['x']) < 0.05 * W]
        if not cand or d['y'] > H: left += 1; continue                # ran out of the image / stopped and faded
        jumps.append(min(math.hypot(e['x'] - d['x'], e['y'] - d['y']) for e in cand))
    if jumps and dt > 0:
        max_jump = max(max_jump, max(jumps)); max_speed = max(max_speed, max(jumps) / dt)
    rep['pairs'].append({'t0': a['t'], 't1': b['t'], 'mean_abs_drgb': round(float(diff.mean()) / 3, 3),
                         'changed_gt24_pct': round(100.0 * float((diff > 72).mean()), 3),
                         'drops': [len(a['drops']), len(b['drops'])], 'trails': [len(a['trails']), len(b['trails'])],
                         'cover_image': [a['cover'], b['cover']], 'running_matched_px': [round(j, 1) for j in jumps], 'running_left_or_ended': left})
rep.update({'mask_identical_all_frames': bool(mask_same), 'visible_pixel_counts_identical': bool(labels_same),
            'max_running_drop_jump_px': round(max_jump, 1), 'max_running_drop_speed_px_s': round(max_speed, 1),
            'max_changed_gt24_pct': max(p['changed_gt24_pct'] for p in rep['pairs']) if rep['pairs'] else 0,
            'max_cover_image_step': round(max(abs(p['cover_image'][1] - p['cover_image'][0]) for p in rep['pairs']), 4) if rep['pairs'] else 0,
            'vessel_cover_fraction_range': [round(min(covers), 4), round(max(covers), 4)] if covers else None})
print(json.dumps({k: v for k, v in rep.items() if k != 'pairs'}, indent=1))
for p in rep['pairs']:
    print(f"  t {p['t0']:.2f}->{p['t1']:.2f}  mean|d| {p['mean_abs_drgb']:6.3f}  changed>24 {p['changed_gt24_pct']:6.3f}%  drops {p['drops']} trails {p['trails']}  cover {p['cover_image'][0]:.4f}->{p['cover_image'][1]:.4f}  running {p['running_matched_px']}")
if len(sys.argv) > 3: json.dump(rep, open(sys.argv[3], 'w'), indent=1)
