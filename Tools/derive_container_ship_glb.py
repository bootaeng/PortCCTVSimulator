"""Brand-free derived copy of the RM02 "Container Ship" GLB (ThirdParty/SketchfabCCBY/ships/SOURCE_container_ships.md).

The download (ThirdParty_raw/container_ships_20261116/, git-ignored) carries real shipping-line / leasing marks on its
container atlas ("msc", "CMA CGM", "tex") and an "MRC" mark that imitates MSC on the hull and containers, plus a winged emblem
and small lettering on the hull atlas. Each rectangle (texture pixels, top-left origin, located by eye on a 64 px grid) is filled
with the median colour of a 2 px ring around it, so the container / hull paint stays and only the mark disappears. The same
rectangles are cleared on the containers' metallic-roughness map, where the "MRC" mark is also baked, and that map's metallic
channel is set to 0 (painted containers; the source's ~0.23 metallic made them dark). Geometry and UVs untouched;
the GLB binary buffer is rebuilt with the edited images.

usage: python -X utf8 Tools/derive_container_ship_glb.py   (writes the derived GLB and derive_log_container_ships.json)
"""
import hashlib
import io
import json
import os
import struct

import numpy as np
from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SRC = os.path.join(ROOT, "ThirdParty_raw", "container_ships_20261116", "container_ship_rm02_1k.glb")
DST_DIR = os.path.join(ROOT, "Assets", "PortCCTVSimulator", "ThirdParty", "SketchfabCCBY", "ships")
DST = os.path.join(DST_DIR, "container_ship_rm02_nobrand.glb")

CONTAINER_MARKS = [(0, 200, 216, 320),                               # "msc" logos (yellow boxes)
                   (222, 24, 300, 74), (430, 24, 500, 74),           # "tex" (leasing company) marks
                   (330, 0, 372, 112), (546, 0, 590, 112),           # "MRC" vertical marks
                   (650, 176, 868, 326),                             # "CMA CGM" logos (whole navy block face)
                   (922, 822, 962, 870), (842, 956, 898, 998)]       # small marks on the lower blocks
EDITS = {
    7: CONTAINER_MARKS,                                              # Containers base colour
    8: [(330, 0, 372, 112), (546, 0, 590, 112), (222, 24, 300, 74), (430, 24, 500, 74)],   # Containers metallic-roughness
    4: [(400, 896, 624, 1024),                                       # "MRC" on the hull sides
        (4, 8, 98, 38), (440, 10, 514, 38), (518, 532, 580, 552),    # small lettering
        (592, 580, 640, 600),                                        # name plate lettering
        (176, 12, 324, 88)],                                         # winged emblem
}
# images whose metallic channel (glTF: blue) is cleared: the containers' metallic-roughness map gives the painted boxes metallic
# ~0.23 on average, which darkens their colours in the CCTV views (painted steel is a dielectric surface)
METAL_ZERO = {8}


def read_glb(b):
    off, js, bin_ = 12, None, b""
    while off < len(b):
        ln, typ = struct.unpack_from("<II", b, off)
        c = b[off + 8: off + 8 + ln]
        if typ == 0x4E4F534A: js = json.loads(c)
        elif typ == 0x004E4942: bin_ = c
        off += 8 + ln
    return js, bin_


def write_glb(js, bin_):
    j = json.dumps(js, separators=(",", ":")).encode(); j += b" " * ((4 - len(j) % 4) % 4)
    bin_ += b"\0" * ((4 - len(bin_) % 4) % 4)
    return struct.pack("<III", 0x46546C67, 2, 12 + 8 + len(j) + 8 + len(bin_)) + struct.pack("<II", len(j), 0x4E4F534A) + j + struct.pack("<II", len(bin_), 0x004E4942) + bin_


def main():
    raw = open(SRC, "rb").read()
    js, bin_ = read_glb(raw)
    log = dict(source=os.path.relpath(SRC, ROOT).replace("\\", "/"), source_sha256=hashlib.sha256(raw).hexdigest(), edits=[])
    new_images = {}
    for img_idx, rects in EDITS.items():
        bv = js["bufferViews"][js["images"][img_idx]["bufferView"]]
        data = bin_[bv.get("byteOffset", 0): bv.get("byteOffset", 0) + bv["byteLength"]]
        img = Image.open(io.BytesIO(data)); fmt = img.format or "PNG"
        a = np.array(img.convert("RGBA" if "A" in img.mode else "RGB"))
        for (x0, y0, x1, y1) in rects:
            ring = np.concatenate([a[max(y0 - 2, 0):y0, x0:x1].reshape(-1, a.shape[2]), a[y1:y1 + 2, x0:x1].reshape(-1, a.shape[2]),
                                   a[y0:y1, max(x0 - 2, 0):x0].reshape(-1, a.shape[2]), a[y0:y1, x1:x1 + 2].reshape(-1, a.shape[2])])
            med = np.median(ring[:, :3], axis=0).astype(np.uint8)
            a[y0:y1, x0:x1, :3] = med
            log["edits"].append(dict(image=img_idx, rect=[x0, y0, x1, y1], fill_rgb=med.tolist()))
        if img_idx in METAL_ZERO:
            log["edits"].append(dict(image=img_idx, metallic_channel_mean_before=round(float(a[..., 2].mean()) / 255, 4), metallic_channel="cleared"))
            a[..., 2] = 0
        buf = io.BytesIO()
        if fmt.upper() in ("JPEG", "JPG"): Image.fromarray(a).save(buf, "JPEG", quality=92); mime = "image/jpeg"
        else: Image.fromarray(a).save(buf, "PNG", optimize=True); mime = "image/png"
        js["images"][img_idx]["mimeType"] = mime
        new_images[js["images"][img_idx]["bufferView"]] = buf.getvalue()
    out_bin = b""
    for i, v in enumerate(js["bufferViews"]):
        chunk = new_images.get(i, bin_[v.get("byteOffset", 0): v.get("byteOffset", 0) + v["byteLength"]])
        out_bin += b"\0" * ((4 - len(out_bin) % 4) % 4)
        v["byteOffset"] = len(out_bin); v["byteLength"] = len(chunk); out_bin += chunk
    js["buffers"][0]["byteLength"] = len(out_bin)
    out = write_glb(js, out_bin)
    os.makedirs(DST_DIR, exist_ok=True)
    open(DST, "wb").write(out)
    log["output"] = os.path.relpath(DST, ROOT).replace("\\", "/"); log["output_sha256"] = hashlib.sha256(out).hexdigest()
    json.dump(log, open(os.path.join(DST_DIR, "derive_log_container_ships.json"), "w", encoding="utf-8"), indent=1)
    print(log["output"], len(log["edits"]), "edits", log["output_sha256"])


if __name__ == "__main__":
    main()
