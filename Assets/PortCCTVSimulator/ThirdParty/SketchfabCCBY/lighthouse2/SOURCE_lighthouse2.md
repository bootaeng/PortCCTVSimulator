# Breakwater lighthouse: Manabashima Lighthouse scan (CC BY 4.0) — attribution required

Source: "Manabashima Lighthouse" by tokiwatari, https://sketchfab.com/3d-models/manabashima-lighthouse-bfdc407529c84c2080c5dad5b88ec40d
(photogrammetry; maker's page https://tokiwatari.jp/site/mabanajima/ states a small white cylindrical lighthouse of about 10 m).
Downloaded 2026-11-09 through the user's logged-in Sketchfab session (same loopback procedure as `../equipment/SOURCE_equipment.md`).
Licence at download: "CC Attribution", downloadable, no NoAI tag. Original GLB (4k texture) kept outside Assets:
`ThirdParty_raw/sketchfab_assets_20261109/lighthouse_manabashima_tokiwatari_4096.glb`,
SHA256 5bb56bccbd64e871b64482037a5e3001b5316605e18b33c986d6fc3813c36bab (232,506 triangles, one 4096 px atlas).

## Processing (`Tools/process_manabashima_lighthouse.py`, all scripted)
1. Tower cut out of the scan: triangles within 0.42 scan units of the tower axis and above the concrete pad; the island rock,
   sea floor and vegetation dropped; loose debris fragments removed. 27,452 triangles kept.
2. Levelling (added after the first build showed the tower leaning): the scan's vertical axis is fitted from circle fits of
   horizontal slices of the shaft and the tower rotated upright (5.24 deg correction).
   Scale from the maker's height: pad-to-cap = 10 m (5.7737 m per scan unit); base at y 0, axis at x = z = 0.
3. The tower's 121 UV islands cut out of the atlas (3 px margin, no resampling) and shelf-packed into a 1024 px atlas.
4. De-lighting: the scan had the survey's sun / sky baked in (one side of the shaft 4x darker). Every texel is mapped back to
   the tower surface; the paint luminance per 10 deg x 0.5 m cell is smoothed and divided out (correction 0.85..2.42); then a
   white balance (overcast blue cast) and exposure to a white paint luminance of 0.80. Scan vegetation on the pad (3,517 texels)
   turned to concrete grey. Local dirt, seams, rust streaks, the door, windows and the gallery rails are kept.
5. Colour variants: white paint (bright, low-saturation texels) re-coloured to signal red (port-hand light) and green (legacy
   IALA map) with the luminance pattern kept, so the stains stay; dark parts (lantern, rails, door) untouched.
6. Weak tangent-space detail normal from the albedo luminance. LOD1 6,038 and LOD2 1,371 triangles by quadric decimation.

Outputs (SHA256, final levelled run 2026-11-09 23:33): lod0.obj 657bf595…, lod1.obj 7c3748f8…, lod2.obj 5725cd41…,
albedo_white df892cdc…, albedo_red 586bc666…, albedo_green bed43056…, normal 327399ab… (full hashes and parameters in
`manabashima_tower_meta.json`; the hashes of the earlier un-levelled run are superseded).

## Prefabs (`Editor/ReplacementAssets.cs`)
`breakwater_light_white`, `breakwater_light_red`, `breakwater_light_green`: concrete octagonal plinth 5.6 m x 0.6 m with three
steps on +z, tower in a LODGroup (screen 20 % / 4.5 % / 0.4 %) rotated so the scanned door faces the steps (+z), MeshCollider
on LOD2, HDRP/Lit (smoothness 0.32). Placed by `HarborParts.Lighthouse` (all six breakwater lights of the three themed maps,
door turned to the breakwater road) and by the legacy map's `Beacon` (harbor_generic_02); uniform scale height/10.6 clamped
0.85..1.25.

## Credit line
"Manabashima Lighthouse" by tokiwatari is licensed under Creative Commons Attribution 4.0
(https://sketchfab.com/3d-models/manabashima-lighthouse-bfdc407529c84c2080c5dad5b88ec40d); used in PortCCTVSimulator as a cut-out,
re-lit, recoloured and decimated tower on a new concrete base.
