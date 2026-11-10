"""Side-by-side before / after sheet of same-camera captures (rgb.png).
  python -I Tools/research/make_before_after.py --before <dir> --after <dir> --out <jpg> name1 name2 ... [--crop x0,y0,x1,y1]"""
import argparse
import os
import sys

from PIL import Image, ImageDraw, ImageFont


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--before", required=True)
    ap.add_argument("--after", required=True)
    ap.add_argument("--out", required=True)
    ap.add_argument("--crop", default="")
    ap.add_argument("--width", type=int, default=880)
    ap.add_argument("names", nargs="+")
    a = ap.parse_args()
    crop = tuple(int(v) for v in a.crop.split(",")) if a.crop else None
    f = ImageFont.truetype("C:/Windows/Fonts/malgun.ttf", 16) if os.path.exists("C:/Windows/Fonts/malgun.ttf") else ImageFont.load_default()
    tiles = []
    for n in a.names:
        row = []
        for d in (a.before, a.after):
            im = Image.open(os.path.join(d, n, "rgb.png")).convert("RGB")
            if crop:
                im = im.crop(crop)
            h = int(im.height * a.width / im.width)
            row.append(im.resize((a.width, h)))
        tiles.append((n, row))
    H = sum(r[0].height + 26 for _, r in tiles)
    sheet = Image.new("RGB", (a.width * 2 + 8, H), (20, 20, 20))
    dr = ImageDraw.Draw(sheet)
    y = 0
    for n, (b, af) in tiles:
        dr.text((6, y + 4), f"{n}  before", fill=(230, 230, 230), font=f)
        dr.text((a.width + 14, y + 4), f"{n}  after", fill=(230, 230, 230), font=f)
        sheet.paste(b, (0, y + 26)); sheet.paste(af, (a.width + 8, y + 26))
        y += b.height + 26
    os.makedirs(os.path.dirname(os.path.abspath(a.out)), exist_ok=True)
    sheet.save(a.out, quality=88)
    print(a.out)
    return 0


if __name__ == "__main__":
    sys.exit(main())
