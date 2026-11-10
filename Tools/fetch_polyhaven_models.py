"""Download Poly Haven CC0 glTF models (all files of one resolution) into an out-of-Assets staging folder and write a manifest
with md5 (from the API) and sha256. Usage: fetch_polyhaven_models.py <out_dir> <res> <asset> [<asset> ...]"""
import hashlib, json, sys, urllib.request
from pathlib import Path

UA = {"User-Agent": "PortCCTVSimulator-asset-fetch"}
out = Path(sys.argv[1]); res = sys.argv[2]; assets = sys.argv[3:]
manifest = {"source": "https://api.polyhaven.com (CC0, https://polyhaven.com/license)", "files": []}

def get(url):
    return urllib.request.urlopen(urllib.request.Request(url, headers=UA), timeout=600)

for a in assets:
    files = json.load(get(f"https://api.polyhaven.com/files/{a}"))
    info = json.load(get(f"https://api.polyhaven.com/info/{a}"))
    g = files["gltf"][res]["gltf"]
    items = [(f"{a}_{res}.gltf", g)] + list(g.get("include", {}).items())
    for rel, meta in items:
        dst = out / a / rel
        dst.parent.mkdir(parents=True, exist_ok=True)
        if dst.exists() and hashlib.md5(dst.read_bytes()).hexdigest() == meta["md5"]:
            data = dst.read_bytes()
        else:
            data = get(meta["url"]).read()
            if hashlib.md5(data).hexdigest() != meta["md5"]:
                raise SystemExit(f"md5 mismatch {meta['url']}")
            dst.write_bytes(data)
        manifest["files"].append({"asset": a, "name": info.get("name"), "authors": list(info.get("authors", {}).keys()),
                                  "file": f"{a}/{rel}", "url": meta["url"], "md5": meta["md5"],
                                  "sha256": hashlib.sha256(data).hexdigest(), "bytes": len(data)})
        print(f"{a}/{rel} {len(data)/1e6:.1f} MB", flush=True)
(out / "download_manifest.json").write_text(json.dumps(manifest, indent=1), encoding="utf-8")
print("done", len(manifest["files"]), "files")
