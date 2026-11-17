"""Checks and contact sheets for the container-ship batch (Tools/make_container_ship_jobs.py). Reads capture folders only.

Per container ship (and the feeder): visible-mask pixels, mask bbox height / projected above-water box height (deck cargo that
fell out of the vessel's instance would leave the mask top at the deck, well below the stack top), GPU / CPU mask IoU,
placement warnings. Sheets: same-framing crops per comparison camera, and the demo frames.

usage: python -X utf8 Tools/check_container_ship_captures.py <capture batch dir> <out dir>
"""
import json
import os
import sys
from pathlib import Path

from PIL import Image, ImageDraw

SHIP_PREFIXES = ("container_ship", "feeder_ship")


def main():
    base, out = Path(sys.argv[1]), Path(sys.argv[2])
    out.mkdir(parents=True, exist_ok=True)
    rows = []
    for d in sorted(p for p in base.iterdir() if (p / "labels.json").exists()):
        lab = json.loads((d / "labels.json").read_text(encoding="utf-8"))
        for o in lab["objects"]:
            if not o["asset_id"].startswith(SHIP_PREFIXES) or (d.name.startswith("demo") and o["asset_id"] == "feeder_ship"):
                if o["placement_warnings"]: rows.append(dict(capture=d.name, object_id=o["object_id"], asset_id=o["asset_id"], warnings=o["placement_warnings"]))
                continue
            vm, aw = o["visible_mask"], o["above_water_box"]["clipped"]
            b = vm["bbox"]
            ratio = (b["y_max"] - b["y_min"]) / max(1.0, aw["y_max"] - aw["y_min"]) if b["valid"] and aw["valid"] else None
            rows.append(dict(capture=d.name, object_id=o["object_id"], asset_id=o["asset_id"], mask_px=vm["pixel_count"],
                             mask_h_over_above_water_h=None if ratio is None else round(ratio, 3),
                             mask_top_y=b["y_min"] if b["valid"] else None, above_water_top_y=round(aw["y_min"], 1) if aw["valid"] else None,
                             cpu_gpu_iou=round(o["cpu_gpu_mask_iou"], 4), in_frustum=o["in_camera_frustum"], warnings=o["placement_warnings"]))
    (out / "ship_mask_check.json").write_text(json.dumps(rows, indent=1), encoding="utf-8")
    for r in rows: print(r)

    # same-framing sheets: crop around the union of the ship boxes of each comparison camera
    for tag in sorted({d.name.split("_")[0] for d in base.iterdir() if d.name.startswith("cmp")}):
        caps = sorted(d for d in base.iterdir() if d.name.startswith(tag + "_"))
        order = ["feeder_ship", "container_ship_loaded", "container_ship_rm02", "container_ship_gogi"]
        caps.sort(key=lambda d: (next(i for i, a in enumerate(order) if a in d.name), "side" in d.name))
        boxes = [json.loads((d / "labels.json").read_text(encoding="utf-8"))["objects"][0]["full_mesh_box"]["clipped"] for d in caps]
        x0 = max(0, min(b["x_min"] for b in boxes) - 60); x1 = min(1920, max(b["x_max"] for b in boxes) + 60)
        y0 = max(0, min(b["y_min"] for b in boxes) - 50); y1 = min(1080, max(b["y_max"] for b in boxes) + 40)
        tiles = []
        for d in caps:
            im = Image.open(d / "rgb.png").convert("RGB").crop((int(x0), int(y0), int(x1), int(y1)))
            dr = ImageDraw.Draw(im); dr.rectangle((0, 0, 330, 16), fill=(0, 0, 0)); dr.text((4, 3), d.name, fill="white")
            tiles.append(im)
        w, h = tiles[0].size
        sheet = Image.new("RGB", (w * 2, h * ((len(tiles) + 1) // 2)), "white")
        for i, t in enumerate(tiles): sheet.paste(t, ((i % 2) * w, (i // 2) * h))
        sheet.save(out / f"compare_{tag}.jpg", quality=90)
    demos = sorted(d for d in base.iterdir() if d.name.startswith("demo"))
    if demos:
        sheet = Image.new("RGB", (1920, 1080 * len(demos)))
        for i, d in enumerate(demos):
            im = Image.open(d / "rgb.png").convert("RGB"); dr = ImageDraw.Draw(im); dr.rectangle((0, 0, 360, 30), fill=(0, 0, 0)); dr.text((8, 8), d.name, fill="white")
            sheet.paste(im, (0, 1080 * i))
        sheet.thumbnail((1600, 900 * len(demos))); sheet.save(out / "demos.jpg", quality=88)
    print("sheets ->", out)


if __name__ == "__main__":
    main()
