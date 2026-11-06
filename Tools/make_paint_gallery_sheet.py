"""Grid of the paint gallery: one row per vessel, columns = original + random seeds (from make_paint_gallery_jobs.py).

usage: python -X utf8 Tools/make_paint_gallery_sheet.py <capture_dir> <out.png> [first_row] [n_rows] [crop]
crop = 1 crops each capture to the vessel's full-mesh box (with margin) so small boats fill the tile.
"""
import glob
import json
import os
import sys

from PIL import Image, ImageDraw, ImageFont

TW, TH, LW = 360, 203, 150


def tile(d, crop):
    im = Image.open(os.path.join(d, "rgb.png")).convert("RGB")
    if crop:
        lab = json.load(open(os.path.join(d, "labels.json"), encoding="utf-8"))
        b = lab["objects"][0]["full_mesh_box"]["clipped"]
        if b["valid"]:
            cx, cy = (b["x_min"] + b["x_max"]) / 2, (b["y_min"] + b["y_max"]) / 2
            w = max(b["x_max"] - b["x_min"], (b["y_max"] - b["y_min"]) * 16 / 9) * 1.25
            h = w * 9 / 16
            im = im.crop((int(cx - w / 2), int(cy - h / 2), int(cx + w / 2), int(cy + h / 2)))
    return im.resize((TW, TH), Image.LANCZOS)


def main(root, out, first=0, n=999, crop=1):
    dirs = sorted(d for d in glob.glob(os.path.join(root, "*")) if os.path.exists(os.path.join(d, "rgb.png")))
    rows = {}
    for d in dirs:
        aid, var = os.path.basename(d).split("__")
        rows.setdefault(aid, {})[var] = d
    cat = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "Assets", "PortCCTVSimulator", "Resources", "PortCCTV", "VesselCatalog.asset")
    order = [l.split(": ", 1)[1].strip() for l in open(cat, encoding="utf-8") if l.startswith("  - asset_id: ")]
    aids = [a for a in order if a in rows][first:first + n]
    cols = ["orig"] + sorted({v for r in rows.values() for v in r if v != "orig"}, key=lambda s: int(s[1:]))
    W, H = LW + len(cols) * (TW + 4), 30 + len(aids) * (TH + 4)
    sheet = Image.new("RGB", (W, H), (18, 20, 24))
    dr = ImageDraw.Draw(sheet)
    f = ImageFont.truetype("C:/Windows/Fonts/malgun.ttf", 16)
    for j, c in enumerate(cols):
        dr.text((LW + j * (TW + 4) + 6, 6), "원본" if c == "orig" else f"무작위 seed {c[1:]}", fill=(230, 232, 236), font=f)
    for i, a in enumerate(aids):
        y = 30 + i * (TH + 4)
        dr.text((6, y + TH // 2 - 10), a, fill=(230, 232, 236), font=f)
        for j, c in enumerate(cols):
            if c in rows[a]:
                sheet.paste(tile(rows[a][c], crop), (LW + j * (TW + 4), y))
    sheet.save(out)
    print(out, sheet.size, len(aids), "rows")


if __name__ == "__main__":
    a = sys.argv
    main(a[1], a[2], int(a[3]) if len(a) > 3 else 0, int(a[4]) if len(a) > 4 else 999, int(a[5]) if len(a) > 5 else 1)
