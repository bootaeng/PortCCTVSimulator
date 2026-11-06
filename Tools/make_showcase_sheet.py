"""Compose a labelled showcase sheet from a capture batch (same camera, different environments).

usage: python -X utf8 Tools/make_showcase_sheet.py <capture_dir> <out.png>
Reads <capture_dir>/<job_id>/rgb.png (+ labels.json / mask_instances.png for the label tile). Display only; no label is changed.
"""
import json
import os
import sys

import numpy as np
from PIL import Image, ImageDraw, ImageFont

TILES = [
    ("01_day_clear", "맑은 낮"),
    ("02_backlit", "역광 (해가 카메라 정면)"),
    ("03_dusk", "해질녘"),
    ("04_haze", "옅은 연무"),
    ("05_fog", "짙은 안개"),
    ("06_rain", "비 · 물결 · 렌즈 물방울"),
    ("07_snow", "눈 (쌓인 눈 포함)"),
    ("08_night_clear", "야간 · 항만 조명과 항해등"),
    ("09_night_rain", "야간 · 비"),
    ("10_cctv_lowq", "저화질 CCTV (왜곡 · 잡음 · JPEG)"),
    ("11_night_ir", "야간 적외선 흑백 CCTV"),
    ("LABELS:01_day_clear", "자동 라벨 (GPU 가시 마스크 + 상자)"),
]
TW, TH, GAP, CAP, COLS = 800, 450, 14, 46, 3
BG, FG, SUB = (18, 20, 24), (238, 240, 244), (150, 156, 166)
FONT = "C:/Windows/Fonts/malgun.ttf"
FONT_B = "C:/Windows/Fonts/malgunbd.ttf"
PALETTE = [(255, 99, 71), (64, 196, 255), (255, 214, 0), (120, 230, 120), (255, 120, 220), (255, 160, 40), (150, 140, 255),
           (0, 230, 200), (240, 240, 240), (255, 80, 140), (170, 255, 80), (80, 160, 255), (255, 200, 140)]


def label_tile(d):
    rgb = Image.open(os.path.join(d, "rgb.png")).convert("RGB")
    lab = json.load(open(os.path.join(d, "labels.json"), encoding="utf-8"))
    m = np.array(Image.open(os.path.join(d, "mask_instances.png")))
    if m.ndim == 3:
        m = m[..., 0]
    a = np.asarray(rgb).astype(np.float32)
    out = a * 0.55
    color = {}
    for k, o in enumerate(lab["objects"]):
        c = np.array(PALETTE[k % len(PALETTE)], np.float32)
        color[o["object_id"]] = tuple(int(x) for x in c)
        sel = m == o["instance_index"]
        out[sel] = a[sel] * 0.35 + c * 0.65
    img = Image.fromarray(out.clip(0, 255).astype(np.uint8))
    dr = ImageDraw.Draw(img)
    f = ImageFont.truetype(FONT_B, 22)
    for o in lab["objects"]:
        vm = o["visible_mask"]
        if not vm.get("computed") or vm["pixel_count"] <= 0 or not vm["bbox"]["valid"]:
            continue
        b = vm["bbox"]
        c = color[o["object_id"]]
        dr.rectangle([b["x_min"], b["y_min"], b["x_max"], b["y_max"]], outline=c, width=3)
        if vm["pixel_count"] >= 400:
            dr.text((b["x_min"], b["y_min"] - 27), o["category"], fill=c, font=f, stroke_width=2, stroke_fill=(0, 0, 0))
    return img


def main(root, out):
    rows = (len(TILES) + COLS - 1) // COLS
    head = 120
    W = COLS * TW + (COLS + 1) * GAP
    H = head + rows * (TH + CAP) + (rows + 1) * GAP
    sheet = Image.new("RGB", (W, H), BG)
    dr = ImageDraw.Draw(sheet)
    dr.text((GAP + 4, 18), "Port CCTV Simulator", fill=FG, font=ImageFont.truetype(FONT_B, 44))
    dr.text((GAP + 6, 76), "고정 CCTV 한 대 · 같은 선박 배치 · 환경 조건만 바꿔 렌더  |  Unity 2023.2 HDRP · 컨테이너 터미널 언덕 카메라 · 1920×1080 캡처 축소",
            fill=SUB, font=ImageFont.truetype(FONT, 22))
    fc = ImageFont.truetype(FONT_B, 24)
    fn = ImageFont.truetype(FONT, 18)
    for i, (job, cap) in enumerate(TILES):
        x = GAP + (i % COLS) * (TW + GAP)
        y = head + GAP + (i // COLS) * (TH + CAP + GAP)
        if job.startswith("LABELS:"):
            im = label_tile(os.path.join(root, job.split(":", 1)[1]))
        else:
            im = Image.open(os.path.join(root, job, "rgb.png")).convert("RGB")
        sheet.paste(im.resize((TW, TH), Image.LANCZOS), (x, y))
        dr.text((x + 4, y + TH + 9), cap, fill=FG, font=fc)
        dr.text((x + TW - 34, y + TH + 13), f"{i + 1:02d}", fill=SUB, font=fn)
    sheet.save(out)
    print(out, sheet.size)


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])
