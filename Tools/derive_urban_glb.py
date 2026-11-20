"""Derived copies of the Sketchfab "Urban" collection building GLBs (ThirdParty/SketchfabCCBY/urban/SOURCE_urban.md).

Originals stay byte-identical in ThirdParty_raw/sketchfab_urban_20261113/ (outside Assets, git-ignored like the other raw downloads). Edits, all logged:
- real or possibly real shop names / brand signs in the atlas texture are filled with the median colour of a 2 px ring around each
  rectangle (a blank sign board, geometry and UVs unchanged);
- optional alphaMode change (a whole building drawn as BLEND -> MASK, cutoff 0.5: the texture's alpha is a cut-out, not glass).
The GLB binary buffer is rebuilt with the edited image; every other buffer view is copied unchanged.

usage: python -X utf8 Tools/derive_urban_glb.py            (writes the derived GLBs + derive_log.json)
"""
import hashlib
import io
import json
import os
import struct

import numpy as np
from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SRC = os.path.join(ROOT, "ThirdParty_raw", "sketchfab_urban_20261113")
DST = os.path.join(ROOT, "Assets", "PortCCTVSimulator", "ThirdParty", "SketchfabCCBY", "urban")

# rectangles (x0, y0, x1, y1) in texture pixels (top-left origin) located by eye on the atlas, see SOURCE_urban.md
JOBS = {
    "ny_hirise_apartment_aitortilla01.glb": dict(
        out="ny_hirise_apartment_aitortilla01_nobrand.glb", alpha_mode="MASK",
        rects=[(382, 218, 546, 255),     # "euro concepts, ltd. kitchen design studio" sign
               (546, 210, 772, 247),     # "Dragon's Lair comics - cards - games" sign
               (823, 220, 905, 255),     # "Sperry Top-Sider" sign (real brand)
               (957, 193, 1024, 227),    # "Dunkin' Donuts" sign (real brand)
               (430, 58, 510, 72)]),     # small address line under the window
    "midrise_w2w_office_aitortilla01.glb": dict(
        out="midrise_w2w_office_aitortilla01_nobrand.glb", alpha_mode=None,
        rects=[(284, 535, 512, 556),     # "MAX RENDA" fascia (both bays)
               (284, 663, 512, 685),     # "GAULLE" fascia (both bays)
               (316, 444, 378, 466), (426, 444, 494, 466)]),   # "MAX RENDA" signs inside the windows
    # untextured; the author's gloss comes from KHR_materials_clearcoat (roughness 0.04), which the glTFast HDRP shader does not
    # render, so the facade read as matte black from a distance: the clear-coat gloss is folded into the base roughness and the
    # panel colour is raised x1.5 (still dark blue-grey glass)
    "office_tower_tryfield.glb": dict(out="office_tower_tryfield_gloss.glb", alpha_mode=None, rects=[], materials={
        "02_-_Defaultww": dict(roughnessFactor=0.08),
        "02_-_Default": dict(roughnessFactor=0.15, baseColorScale=1.5)}),
}


def read_glb(b):
    assert b[:4] == b"glTF"
    off, js, bin_ = 12, None, b""
    while off < len(b):
        ln, typ = struct.unpack_from("<II", b, off)
        chunk = b[off + 8: off + 8 + ln]
        if typ == 0x4E4F534A: js = json.loads(chunk)
        elif typ == 0x004E4942: bin_ = chunk
        off += 8 + ln
    return js, bin_


def write_glb(js, bin_):
    j = json.dumps(js, separators=(",", ":")).encode()
    j += b" " * ((4 - len(j) % 4) % 4)
    bin_ += b"\0" * ((4 - len(bin_) % 4) % 4)
    total = 12 + 8 + len(j) + 8 + len(bin_)
    return struct.pack("<III", 0x46546C67, 2, total) + struct.pack("<II", len(j), 0x4E4F534A) + j + struct.pack("<II", len(bin_), 0x004E4942) + bin_


def sha(b): return hashlib.sha256(b).hexdigest()


def main():
    os.makedirs(DST, exist_ok=True)
    log = {}
    for src_name, job in JOBS.items():
        raw = open(os.path.join(SRC, src_name), "rb").read()
        js, bin_ = read_glb(raw)
        entry = dict(source=src_name, source_sha256=sha(raw), output=job["out"], edits=[])
        if job["rects"]:
            assert len(js["images"]) == 1, "one atlas expected"
            im_def = js["images"][0]
            bv_idx = im_def["bufferView"]
            bv = js["bufferViews"][bv_idx]
            data = bin_[bv.get("byteOffset", 0): bv.get("byteOffset", 0) + bv["byteLength"]]
            img = Image.open(io.BytesIO(data))
            mode, fmt = img.mode, (img.format or "PNG")
            a = np.array(img.convert("RGBA" if "A" in mode else "RGB"))
            for (x0, y0, x1, y1) in job["rects"]:
                ring = np.concatenate([a[max(y0 - 2, 0):y0, x0:x1].reshape(-1, a.shape[2]), a[y1:y1 + 2, x0:x1].reshape(-1, a.shape[2]),
                                       a[y0:y1, max(x0 - 2, 0):x0].reshape(-1, a.shape[2]), a[y0:y1, x1:x1 + 2].reshape(-1, a.shape[2])])
                med = np.median(ring[:, :3], axis=0).astype(np.uint8)
                a[y0:y1, x0:x1, :3] = med
                entry["edits"].append(dict(rect=[x0, y0, x1, y1], fill_rgb=med.tolist()))
            buf = io.BytesIO()
            out_img = Image.fromarray(a)
            if fmt.upper() in ("JPEG", "JPG"): out_img.save(buf, "JPEG", quality=92); mime = "image/jpeg"
            else: out_img.save(buf, "PNG", optimize=True); mime = "image/png"
            new_img = buf.getvalue()
            im_def["mimeType"] = mime
            # rebuild the binary buffer: copy every view, swap in the edited image
            new_bin = b""
            for i, v in enumerate(js["bufferViews"]):
                chunk = new_img if i == bv_idx else bin_[v.get("byteOffset", 0): v.get("byteOffset", 0) + v["byteLength"]]
                new_bin += b"\0" * ((4 - len(new_bin) % 4) % 4)
                v["byteOffset"] = len(new_bin); v["byteLength"] = len(chunk)
                new_bin += chunk
            js["buffers"][0]["byteLength"] = len(new_bin)
            bin_ = new_bin
            entry["image"] = dict(format=fmt, size=list(img.size), mode=mode, original_bytes=len(data), new_bytes=len(new_img))
        for m in js.get("materials", []):
            e = job.get("materials", {}).get(m.get("name"))
            if not e: continue
            pbr = m.setdefault("pbrMetallicRoughness", {})
            if "roughnessFactor" in e:
                entry["edits"].append(dict(material=m["name"], roughnessFactor=[pbr.get("roughnessFactor", 1.0), e["roughnessFactor"]])); pbr["roughnessFactor"] = e["roughnessFactor"]
            if "baseColorScale" in e:
                old = pbr.get("baseColorFactor", [1, 1, 1, 1]); new = [min(1.0, c * e["baseColorScale"]) for c in old[:3]] + old[3:]
                entry["edits"].append(dict(material=m["name"], baseColorFactor=[old, new])); pbr["baseColorFactor"] = new
        if job["alpha_mode"]:
            for m in js["materials"]:
                if m.get("alphaMode") == "BLEND":
                    m["alphaMode"] = job["alpha_mode"]; m["alphaCutoff"] = 0.5
                    entry["edits"].append(dict(material=m.get("name"), alphaMode="BLEND->" + job["alpha_mode"], alphaCutoff=0.5))
        out = raw if not entry["edits"] else write_glb(js, bin_)
        open(os.path.join(DST, job["out"]), "wb").write(out)
        entry["output_sha256"] = sha(out)
        log[job["out"]] = entry
        print(job["out"], len(entry["edits"]), "edits", entry["output_sha256"][:16])
    json.dump(log, open(os.path.join(DST, "derive_log.json"), "w", encoding="utf-8"), indent=1)


if __name__ == "__main__":
    main()
