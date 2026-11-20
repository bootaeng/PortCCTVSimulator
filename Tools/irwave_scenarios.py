"""IR / wave validation set (2026-11-12) on the fishing-port east coast road: W-beam guard rail in the foreground, a fishing boat
at mid range, vessels far out at sea.  usage: python Tools/irwave_scenarios.py <tag> <set>   set = view | ir | wave"""
import copy, json, math, os, sys

ROOT = 'C:/Users/USER/Desktop/DronePerception/simulator/PortCCTVSimulator/Builds/Windows/Scenarios/'
SRC = ROOT + 'mapsuite_reference_20261106_r2/fishing_port_01_fp_A_market_roof_default.json'
tag, which = sys.argv[1], sys.argv[2]
OUT = ROOT + 'irwave_' + tag + '/'
os.makedirs(OUT, exist_ok=True)
base = json.load(open(SRC))


def look(pos, tgt):
    dx, dy, dz = tgt[0] - pos[0], tgt[1] - pos[1], tgt[2] - pos[2]
    return {'x': round(-math.degrees(math.atan2(dy, math.hypot(dx, dz))), 3), 'y': round(math.degrees(math.atan2(dx, dz)), 3), 'z': 0.0}


def vessel(oid, asset, x, z, hdg, imm, mode='none'):
    return {'object_id': oid, 'asset_id': asset, 'x': x, 'z': z, 'heading_deg': hdg, 'immersion_m': imm,
            'motion': {'mode': mode, 'speed_kn': 0.0, 'route': [], 'loop': True, 'keep_in_view': True, 'seed': 0}, 'paint': ''}


VIEWS = {
    # pole 4.5 m above the coast road, just behind the sea-side guard rail; looks SW across the outer sea to the breakwater head
    'road': ((300.0, 7.0, -7.5), (230.0, 0.0, -150.0), 46),
    'road2': ((330.0, 7.0, -7.5), (220.0, 0.0, -120.0), 50),
}
EXTRA = [vessel('ir_mid', 'korean_fishing_boat', 282.0, -58.0, 300.0, 0.45),      # ~50 m, inside the wide beam
         vessel('ir_far1', 'ferry', 150.0, -620.0, 250.0, 0.4),                    # ~640 m
         vessel('ir_far2', 'fishing_vessel', 330.0, -420.0, 200.0, 0.9)]           # ~410 m
SENS = {'enabled': True, 'preset': 'night_ir', 'distortion_k1': 0.12, 'distortion_k2': 0.0, 'distortion_fill': True, 'blur_sigma_px': 1.1,
        'motion_blur_px': 0.0, 'motion_blur_angle_deg': 0.0, 'noise_read_dn': 1.0, 'noise_shot': 0.2, 'noise_chroma': 0.3, 'gain': 4.0,
        'vignette': 0.3, 'monochrome': True, 'jpeg_quality': 50, 'lens_drops': -1.0, 'seed': 7, 'save_clean': True}
DAY = {'enabled': True, 'preset': 'typical', 'distortion_k1': 0.12, 'distortion_k2': 0.0, 'distortion_fill': True, 'blur_sigma_px': 0.9,
       'motion_blur_px': 0.0, 'motion_blur_angle_deg': 0.0, 'noise_read_dn': 1.0, 'noise_shot': 0.1, 'noise_chroma': 0.3, 'gain': -1.0,
       'vignette': 0.25, 'monochrome': False, 'jpeg_quality': 55, 'lens_drops': -1.0, 'seed': 7, 'save_clean': True}
jobs = []


def scen(name, view='road', env='night_clear', rain=0.0, ir=None, water='calm', t=120.0, wt=12.0, motion=False, sensor=None, mask=None, drop=0.0):
    d = copy.deepcopy(base)
    d['scenario_id'] = name
    if view in VIEWS:
        pos, tgt, fov = VIEWS[view]
        d['camera_override'] = {'enabled': True, 'position': {'x': pos[0], 'y': pos[1], 'z': pos[2]}, 'euler_deg': look(pos, tgt), 'vertical_fov_deg': fov}
    else:
        d['camera_preset_id'] = view; d['camera_override']['enabled'] = False
    d['vessels'] = d['vessels'] + copy.deepcopy(EXTRA)
    e = d['environment']
    e['preset_id'] = env; e['water_preset_id'] = water; e['water_time_s'] = wt
    e['weather'].update({'precipitation': 'rain' if rain > 0 else 'none', 'intensity': rain if rain > 0 else 0.5, 'overcast': rain > 0, 'seed': 0})
    if motion: e['vessel_wave_motion'] = True
    if mask: e['water_mask'] = mask
    s = copy.deepcopy(sensor if sensor is not None else SENS)
    if ir is not None:
        s['ir_mode'] = 'on' if ir else 'off'
        if ir: s['preset'] = 'night_ir_illum'; s['blur_sigma_px'] = 1.0
        if drop: s['ir_mount_drop_m'] = drop
    d['sensor'] = s
    d['navigation']['sim_time_s'] = t
    json.dump(d, open(OUT + name + '.json', 'w'), indent=1)
    jobs.append(name)


if which == 'view':
    for v in VIEWS:
        scen('view_' + v + '_day', v, 'day_clear', sensor={**DAY, 'enabled': False})
        scen('view_' + v + '_night', v, 'night_clear', ir=False)
elif which == 'ir':
    for cond, env, rain in (('clear', 'night_clear', 0.0), ('rain', 'night_overcast', 0.7)):
        for ir in (False, True):
            scen(f'ir_{cond}_{"on" if ir else "off"}', 'road', env, rain, ir)
    scen('ir_clear_on_lowmount', 'road', 'night_clear', 0.0, True, drop=3.0)
    scen('ir_rain_on_lowmount', 'road', 'night_overcast', 0.7, True, drop=3.0)
    for k in range(12):
        scen(f'seq_ir_rain_on_t{k:03d}', 'road', 'night_overcast', 0.7, True, t=120.0 + 0.25 * k, wt=12.0 + 0.25 * k)
    for k in range(12):
        scen(f'seq_ir_rain_off_t{k:03d}', 'road', 'night_overcast', 0.7, False, t=120.0 + 0.25 * k, wt=12.0 + 0.25 * k)
elif which == 'wave':
    for water in ('calm', 'rough'):
        for lt in ('day', 'irnight'):
            for k in range(8):
                kw = dict(water=water, motion=True, t=120.0 + 0.5 * k, wt=12.0 + 0.5 * k)
                if lt == 'day': scen(f'wave_{water}_{lt}_t{k:03d}', 'road', 'day_clear', sensor=DAY, **kw)
                else: scen(f'wave_{water}_{lt}_t{k:03d}', 'road', 'night_clear', ir=True, **kw)
    scen('wave_rough_nomask_day_t000', 'road', 'day_clear', sensor=DAY, water='rough', motion=True, mask='off')
    # west headland camera (40 m, long-range IR profile): inner harbour and entrance in one view
    for water in ('calm', 'rough'):
        for lt in ('day', 'irnight'):
            for k in range(4):
                kw = dict(water=water, motion=True, t=120.0 + 0.5 * k, wt=12.0 + 0.5 * k)
                if lt == 'day': scen(f'hill_{water}_{lt}_t{k:03d}', 'fp_B_hillside', 'day_clear', sensor=DAY, **kw)
                else: scen(f'hill_{water}_{lt}_t{k:03d}', 'fp_B_hillside', 'night_clear', ir=True, **kw)
    scen('hill_rough_nomask_day_t000', 'fp_B_hillside', 'day_clear', sensor=DAY, water='rough', motion=True, mask='off')

tmpl = json.load(open(ROOT + 'parking_r1/jobs_before.json'))['jobs'][0]
out = {'batch_schema': 'portcctv.batch.v1', 'batch_id': 'irwave_' + tag, 'output_dir': 'Captures/irwave_' + tag, 'jobs': []}
for j in jobs:
    jj = copy.deepcopy(tmpl); jj['job_id'] = j; jj['scenario_path'] = j + '.json'; out['jobs'].append(jj)
json.dump(out, open(OUT + 'jobs.json', 'w'), indent=1)
print(len(jobs), OUT + 'jobs.json')
