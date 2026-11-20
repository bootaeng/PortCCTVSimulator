"""Sheets for the IR / wave validation (2026-11-12).  usage: irsheet.py <captures dir> <out dir>"""
import json, os, sys
from PIL import Image, ImageDraw, ImageFont
C, OUT = sys.argv[1], sys.argv[2]
os.makedirs(OUT, exist_ok=True)
try:
    F = ImageFont.truetype('C:/Windows/Fonts/malgun.ttf', 22)
except Exception:
    F = ImageFont.load_default()


def tile(path, w=960, label=None):
    im = Image.open(path).convert('RGB'); im = im.resize((w, int(im.height * w / im.width)))
    if label:
        d = ImageDraw.Draw(im); d.rectangle((0, 0, 12 + 12 * len(label), 34), fill=(0, 0, 0)); d.text((8, 4), label, fill=(255, 255, 255), font=F)
    return im


def grid(rows, out, w=960):
    ims = [[tile(p, w, l) for p, l in r] for r in rows]
    H = sum(r[0].height for r in ims); W = max(sum(i.width for i in r) for r in ims)
    s = Image.new('RGB', (W, H)); y = 0
    for r in ims:
        x = 0
        for i in r: s.paste(i, (x, y)); x += i.width
        y += r[0].height
    s.save(out, quality=88); print(out)


j = lambda n, f='rgb.png': os.path.join(C, n, f)
if os.path.isdir(j('ir_clear_on', '')):
    grid([[(j('ir_clear_off'), '맑은 밤 IR 끔'), (j('ir_clear_on'), '맑은 밤 IR 켬')],
          [(j('ir_rain_off'), '비 오는 밤 IR 끔'), (j('ir_rain_on'), '비 오는 밤 IR 켬 (젖은 돔)')]], os.path.join(OUT, 'ir_onoff.jpg'))
    grid([[(j('ir_clear_on', 'ir_component.png'), 'IR 성분만 (맑은 밤)'), (j('ir_rain_on', 'ir_component.png'), 'IR 성분만 (비)')]], os.path.join(OUT, 'ir_component.jpg'))
    # crops: near rail (bottom), mid boat, far sea / horizon
    for n in ('ir_rain_off', 'ir_rain_on', 'ir_clear_off', 'ir_clear_on'):
        im = Image.open(j(n)).convert('RGB')
        im.crop((560, 380, 1240, 760)).resize((1020, 570)).save(os.path.join(OUT, f'crop_{n}.jpg'), quality=88)
    if os.path.isdir(j('ir_clear_on_lowmount', '')):
        grid([[(j('ir_clear_on'), '렌즈 일체형 IR (0.25 m 아래)'), (j('ir_clear_on_lowmount'), '외장 IR 3 m 아래 장착')],
              [(j('ir_clear_on', 'ir_component.png'), 'IR 성분: 일체형'), (j('ir_clear_on_lowmount', 'ir_component.png'), 'IR 성분: 3 m 아래 (가림체 뒤 그림자)')]],
             os.path.join(OUT, 'ir_mount.jpg'))
    rows = []
    for n in ('ir_clear_off', 'ir_clear_on', 'ir_rain_off', 'ir_rain_on', 'ir_clear_on_lowmount', 'ir_rain_on_lowmount'):
        if not os.path.isdir(j(n, '')): continue
        m = json.load(open(j(n, 'metadata.json'))); ir = m['sensor'].get('ir') or {}
        rows.append({'job': n, 'ir_on': ir.get('on', False), 'checks': {k: v for k, v in (ir.get('checks') or {}).items() if k not in ('shadow_samples', 'method')},
                     'dome_reflection': ir.get('dome_reflection'), 'lens_drop_cover_fraction_image': m['sensor'].get('lens_drop_cover_fraction_image'),
                     'ir_render_ms': ir.get('render_ms')})
    json.dump(rows, open(os.path.join(OUT, 'ir_checks.json'), 'w'), indent=1)
if os.path.isdir(j('wave_rough_day_t000', '')):
    grid([[(j('wave_calm_day_t000'), '잔잔함 주간'), (j('wave_rough_day_t000'), '거친 파랑 주간')],
          [(j('wave_calm_irnight_t000'), '잔잔함 야간 IR'), (j('wave_rough_irnight_t000'), '거친 파랑 야간 IR')]], os.path.join(OUT, 'wave_calm_rough.jpg'))
    if os.path.isdir(j('wave_rough_nomask_day_t000', '')):
        grid([[(j('wave_rough_day_t000'), '거친 파랑 + 항내 마스크'), (j('wave_rough_nomask_day_t000'), '거친 파랑, 마스크 끔')]], os.path.join(OUT, 'wave_mask_onoff.jpg'))
