"""Before/after sheet for vessel paint: rows = scenes, columns = original + random seeds (job ids "<scene>__orig|s1|s2").
Each tile is cropped (same crop for the whole row) to the union of the vessels' visible boxes so that the hulls are readable.

usage: python -X utf8 Tools/make_paint_compare_sheet.py <capture_dir> <out.png>
"""
import glob
import json
import os
import sys

from PIL import Image, ImageDraw, ImageFont

TW, TH = 760, 428
NAMES = {"fp_A": "어항 · 시장 지붕 카메라", "ct_E": "컨테이너 터미널 · 내항 방파제", "ct_A": "컨테이너 터미널 · 동측 안벽"}
COLS = [("orig", "원본 색"), ("s1", "무작위 (seed 1)"), ("s2", "무작위 (seed 2)")]


def union_box(d):
    lab = json.load(open(os.path.join(d, "labels.json"), encoding="utf-8"))
    bs = [o["visible_mask"]["bbox"] for o in lab["objects"] if o["visible_mask"].get("computed") and o["visible_mask"]["pixel_count"] >= 150]
    W, H = lab["image_width"], lab["image_height"]
    if not bs:
        return (0, 0, W, H)
    x0, y0 = min(b["x_min"] for b in bs), min(b["y_min"] for b in bs)
    x1, y1 = max(b["x_max"] for b in bs), max(b["y_max"] for b in bs)
    cx, cy = (x0 + x1) / 2, (y0 + y1) / 2
    w = max((x1 - x0) * 1.15, (y1 - y0) * 1.15 * 16 / 9, W * 0.45)
    w = min(w, W); h = w * 9 / 16
    cx = min(max(cx, w / 2), W - w / 2); cy = min(max(cy, h / 2), H - h / 2)
    return (int(cx - w / 2), int(cy - h / 2), int(cx + w / 2), int(cy + h / 2))


def main(root, out):
    scenes = []
    for d in sorted(glob.glob(os.path.join(root, "*__orig"))):
        scenes.append(os.path.basename(d).split("__")[0])
    order = [s for s in ["fp_A", "ct_E", "ct_A"] if s in scenes] + [s for s in scenes if s not in NAMES]
    head, cap, gap = 104, 34, 12
    W = gap + len(COLS) * (TW + gap)
    H = head + len(order) * (TH + cap + gap) + gap
    sheet = Image.new("RGB", (W, H), (18, 20, 24))
    dr = ImageDraw.Draw(sheet)
    dr.text((gap + 4, 16), "선박 도장 무작위화", fill=(238, 240, 244), font=ImageFont.truetype("C:/Windows/Fonts/malgunbd.ttf", 40))
    dr.text((gap + 6, 68), "같은 장면 · 같은 배치 · 선체/상부구조 색만 seed로 결정 (유리·금속·갑판·화물·장식은 원본 유지, 라벨 마스크 동일)",
            fill=(150, 156, 166), font=ImageFont.truetype("C:/Windows/Fonts/malgun.ttf", 20))
    fc = ImageFont.truetype("C:/Windows/Fonts/malgunbd.ttf", 21)
    for i, sc in enumerate(order):
        crop = union_box(os.path.join(root, sc + "__orig"))
        y = head + i * (TH + cap + gap)
        for j, (c, label) in enumerate(COLS):
            d = os.path.join(root, f"{sc}__{c}")
            x = gap + j * (TW + gap)
            if os.path.exists(os.path.join(d, "rgb.png")):
                im = Image.open(os.path.join(d, "rgb.png")).convert("RGB").crop(crop).resize((TW, TH), Image.LANCZOS)
                sheet.paste(im, (x, y))
            dr.text((x + 4, y + TH + 5), f"{NAMES.get(sc, sc)} · {label}", fill=(238, 240, 244), font=fc)
    sheet.save(out)
    print(out, sheet.size)


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])
