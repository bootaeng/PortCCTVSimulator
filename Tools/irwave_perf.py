"""1080p capture time, IR on vs off (same jobs except ir_mode).  usage: irperf.py <captures> <out.json>"""
import json, os, sys, statistics as st
C = sys.argv[1]
pairs = [('ir_clear_off', 'ir_clear_on'), ('ir_rain_off', 'ir_rain_on')] + [(f'seq_ir_rain_off_t{k:03d}', f'seq_ir_rain_on_t{k:03d}') for k in range(12)]
rows = []
for a, b in pairs:
    pa, pb = os.path.join(C, a, 'metadata.json'), os.path.join(C, b, 'metadata.json')
    if not (os.path.isfile(pa) and os.path.isfile(pb)): continue
    ma, mb = json.load(open(pa)), json.load(open(pb))
    rows.append({'off': a, 'on': b, 'render_ms': [round(ma['render_ms'], 1), round(mb['render_ms'], 1)], 'total_ms': [round(ma['total_ms'], 1), round(mb['total_ms'], 1)],
                 'ir_render_ms': round((mb['sensor'].get('ir') or {}).get('render_ms', 0), 1), 'size': [ma['image']['width'] if 'image' in ma else None]})
out = {'pairs': len(rows),
       'total_ms_median_off_on': [st.median(r['total_ms'][0] for r in rows), st.median(r['total_ms'][1] for r in rows)],
       'render_ms_median_off_on': [st.median(r['render_ms'][0] for r in rows), st.median(r['render_ms'][1] for r in rows)],
       'ir_render_ms_median': st.median(r['ir_render_ms'] for r in rows), 'ir_render_ms_max': max(r['ir_render_ms'] for r in rows), 'rows': rows}
print(json.dumps({k: v for k, v in out.items() if k != 'rows'}))
json.dump(out, open(sys.argv[2], 'w'), indent=1)
