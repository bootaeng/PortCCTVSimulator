"""Contact sheets of research captures (overlay.png = QA visualisation, not a label) for the development report.

  python -I Tools/research/make_research_sheets.py --captures <captures dir> --out <png> [--title T] [--cols 4] [--only job1,job2]
"""
import argparse
import json
import os
import sys

from PIL import Image, ImageDraw, ImageFont


def font(sz):
    for f in ("C:/Windows/Fonts/malgun.ttf", "C:/Windows/Fonts/arial.ttf"):
        if os.path.exists(f):
            return ImageFont.truetype(f, sz)
    return ImageFont.load_default()


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--captures", required=True)
    ap.add_argument("--out", required=True)
    ap.add_argument("--title", default="")
    ap.add_argument("--cols", type=int, default=4)
    ap.add_argument("--only", default="")
    ap.add_argument("--image", default="overlay.png")
    a = ap.parse_args()
    names = sorted(d for d in os.listdir(a.captures) if os.path.isfile(os.path.join(a.captures, d, "research.json")))
    if a.only:
        want = a.only.split(",")
        names = [n for n in want if n in names]
    tw, th = 480, 270
    cap_h = 46
    rows = (len(names) + a.cols - 1) // a.cols
    W, H = a.cols * tw, rows * (th + cap_h) + (40 if a.title else 0)
    sheet = Image.new("RGB", (W, H), (18, 18, 18))
    dr = ImageDraw.Draw(sheet)
    f1, f2 = font(15), font(12)
    y0 = 0
    if a.title:
        dr.text((10, 8), a.title, fill=(235, 235, 235), font=font(20))
        y0 = 40
    for k, n in enumerate(names):
        d = os.path.join(a.captures, n)
        rec = json.load(open(os.path.join(d, "research.json"), encoding="utf-8"))
        im = Image.open(os.path.join(d, a.image)).convert("RGB").resize((tw, th))
        x, y = (k % a.cols) * tw, y0 + (k // a.cols) * (th + cap_h)
        sheet.paste(im, (x, y))
        e, j = rec["environment"], rec["job"]
        t = [o for o in rec["objects"] if o["role"] == "target"]
        met = sum(1 for o in t if o["size_met"] == "met")
        line1 = f"{n}"
        extra = f" [{j['changed_factor']}={j['changed_value']}]" if j.get("changed_factor") else ""
        extra += f" ep {j['episode_id'][-3:]} f{j['frame_index']} t={j['sim_time_s']:.0f}s {j['split']}" if j.get("episode_id") else ""
        line2 = f"{e['lighting']} / {e['water']} / vis x{e['visibility_scale']:g} / sensor {e['sensor_preset'] if e['sensor_enabled'] else 'off'}; size met {met}/{len(t)}; {rec['negative_status']}{extra}"
        dr.text((x + 4, y + th + 3), line1, fill=(240, 240, 240), font=f1)
        dr.text((x + 4, y + th + 24), line2[:95], fill=(170, 200, 240), font=f2)
    os.makedirs(os.path.dirname(os.path.abspath(a.out)), exist_ok=True)
    sheet.save(a.out, quality=88)
    print(a.out, len(names), "captures")
    return 0


if __name__ == "__main__":
    sys.exit(main())
