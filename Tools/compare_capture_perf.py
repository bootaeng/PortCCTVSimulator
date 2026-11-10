"""Per-job render / mask / total time and warm-up of two capture folders (same jobs, same camera / weather / seed).
usage: python -I Tools/compare_capture_perf.py <before_dir> <after_dir> [out.json]"""
import json, os, sys, statistics as st

a, b = sys.argv[1], sys.argv[2]
rows = []
for n in sorted(os.listdir(a)):
    pa, pb = os.path.join(a, n, 'metadata.json'), os.path.join(b, n, 'metadata.json')
    if not (os.path.isfile(pa) and os.path.isfile(pb)): continue
    ma, mb = json.load(open(pa)), json.load(open(pb))
    rows.append({'job': n, 'render_ms_before': round(ma['render_ms'], 1), 'render_ms_after': round(mb['render_ms'], 1),
                 'total_ms_before': round(ma['total_ms'], 1), 'total_ms_after': round(mb['total_ms'], 1),
                 'training_ready_after': mb.get('training_ready'), 'label_status_after': mb.get('label_status')})
out = {'jobs': len(rows),
       'render_ms_median': [st.median(r['render_ms_before'] for r in rows), st.median(r['render_ms_after'] for r in rows)],
       'render_ms_max': [max(r['render_ms_before'] for r in rows), max(r['render_ms_after'] for r in rows)],
       'total_ms_median': [st.median(r['total_ms_before'] for r in rows), st.median(r['total_ms_after'] for r in rows)],
       'training_ready_true_after': sum(1 for r in rows if r['training_ready_after']), 'rows': rows}
print(json.dumps({k: v for k, v in out.items() if k != 'rows'}))
for r in sorted(rows, key=lambda r: r['render_ms_after'] - r['render_ms_before'], reverse=True)[:8]:
    print(f"  {r['job']:45s} render {r['render_ms_before']:7.1f} -> {r['render_ms_after']:7.1f} ms   total {r['total_ms_before']:7.1f} -> {r['total_ms_after']:7.1f}")
if len(sys.argv) > 3: json.dump(out, open(sys.argv[3], 'w'), indent=1)
