"""Vessel motion and wave occlusion per sequence (calm vs rough, inside vs outside the harbour).  usage: wavestats.py <captures> <out.json>"""
import json, os, sys, statistics as st
C = sys.argv[1]
seqs = {}
for n in sorted(os.listdir(C)):
    p = os.path.join(C, n, 'labels.json')
    if not os.path.isfile(p) or '_t' not in n: continue
    seqs.setdefault(n.rsplit('_t', 1)[0], []).append(n)
out = {}
for s, frames in seqs.items():
    per = {}
    for f in frames:
        for o in json.load(open(os.path.join(C, f, 'labels.json')))['objects']:
            per.setdefault(o['object_id'], []).append(o)
    rows = []
    for oid, os_ in per.items():
        if len(os_) < 2: continue
        h = [o['wave_heave_m'] for o in os_]; pi = [o['wave_pitch_deg'] for o in os_]; ro = [o['wave_roll_deg'] for o in os_]
        vis = [o['visible_mask']['pixel_count'] for o in os_]; full = [o['above_water_box'].get('clipped', {}).get('area', 0) if isinstance(o.get('above_water_box'), dict) else 0 for o in os_]
        rows.append({'object_id': oid, 'asset': os_[0]['asset_id'], 'region': os_[0].get('water_region'), 'frames': len(os_),
                     'heave_range_m': round(max(h) - min(h), 3), 'pitch_range_deg': round(max(pi) - min(pi), 2), 'roll_range_deg': round(max(ro) - min(ro), 2),
                     'visible_px_mean': round(st.mean(vis), 1), 'visible_px_min': min(vis), 'visible_px_max': max(vis)})
    out[s] = rows
summ = {}
for s, rows in out.items():
    for zone in ('inner', 'outer'):
        rr = [r for r in rows if r['region'] and (('inner' in r['region'] or 'basin' in r['region']) == (zone == 'inner')) and r['visible_px_mean'] > 0]
        if rr: summ[f'{s}|{zone}'] = {'vessels': len(rr), 'heave_range_m_median': st.median(r['heave_range_m'] for r in rr),
                                      'pitch_range_deg_median': st.median(r['pitch_range_deg'] for r in rr), 'roll_range_deg_median': st.median(r['roll_range_deg'] for r in rr),
                                      'visible_px_rel_range_median': st.median((r['visible_px_max'] - r['visible_px_min']) / max(r['visible_px_mean'], 1) for r in rr)}
for k, v in summ.items(): print(k, v)
json.dump({'summary': summ, 'sequences': out}, open(sys.argv[2], 'w'), indent=1)
