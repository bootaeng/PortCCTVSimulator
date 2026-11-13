# Sketchfab "Urban" collection buildings (CC BY 4.0) — attribution required

User request (2026-11-13): use the buildings of the Sketchfab collection **"Urban" by pineapp1es**
(https://sketchfab.com/pineapp1es/collections/urban-9102073221854cc5b5f5b429e42b57b5) for the marina beach city
(directive `docs/CLAUDE_URBAN_COLLECTION_DIRECTIVE_20261113.md`).

## Selection (2026-11-13)

- Collection listed through the public API `GET /v3/collections/9102073221854cc5b5f5b429e42b57b5/models` (curl). The paging stopped
  with HTTP 500 at a later cursor after 408 models; 323 of those are CC Attribution, downloadable and carry no `noai` tag. The
  two candidates named in the directive were in the listed part.
- Re-checked per model on `GET /v3/models/{uid}` the same day: licence `CC Attribution` (http://creativecommons.org/licenses/by/4.0/),
  `isDownloadable` true, no NoAI tag.
- **Kept (3):** "Office Tower" (tryfield), "Mid rise wall to wall office building" (aitortilla01), "New York styled hi rise
  apartment building" (aitortilla01) — modern glass office tower, mid-rise office with a street-front podium, slender residential
  tower, all plausible in a Korean coastal city.
- **Not used:** "High rise building (improved SketchUp reupload)" (alwayshasbean) — its description says it is a reupload of
  another author's SketchUp 3D Warehouse model, so the CC BY grant is uncertain; "HK Plaza B320421647701063A0" (patrick.young) —
  a replica of a real Hong Kong building built from HKSAR government (CSDI) data, separate terms; "Asian Buildings Set" and
  "Hotel Opium Nights" — NoAI tag; "Building pack" (Pasha.) — real bank logo (CITIC) on the signs; "Low-poly City Buildings"
  (smooth998) and "Downtown Buildings Set" — real landmark replicas (Empire State, Flatiron); "Free Standard" licence models
  (abhayexe, Potato_Pizza) — not CC.
- Download: GLB (Sketchfab-converted, lowest texture size offered: none / 1k / 1k) from the model download dialog of the user's
  signed-in Sketchfab account, 2026-11-13. SHA256 checked in the browser and on disk.

| model | author | URL | unmodified GLB (ThirdParty_raw/sketchfab_urban_20261113/, git-ignored) | SHA256 | faces |
|---|---|---|---|---|---|
| Office Tower | tryfield | https://sketchfab.com/3d-models/office-tower-83a2d83e4c7d42de97fa800d025a2bdf | office_tower_tryfield.glb (550,700 B) | 23846890b988cc5c52e13751ad5e03bc3cc92935febc6760dab25f59da7ff83e | 7,329 |
| Mid rise wall to wall office building | aitortilla01 | https://sketchfab.com/3d-models/mid-rise-wall-to-wall-office-building-4d2c3f3450ef4e849056b88b49873ce2 | midrise_w2w_office_aitortilla01.glb (912,136 B) | 91beb16c4462ae79d5118972e5a6ff5ad673fb089d45200481bfb9f278b80933 | 1,893 |
| New York styled hi rise apartment building | aitortilla01 | https://sketchfab.com/3d-models/new-york-styled-hi-rise-apartment-building-14e3d5743f7546d5bb7f3befc72c9057 | ny_hirise_apartment_aitortilla01.glb (1,819,848 B) | 83d803d772e4ee1062e8c67b63bc3b4358c845ff539d964327de6940b5a4f670 | 5,591 |

The unmodified downloads are kept outside `Assets` (`ThirdParty_raw/sketchfab_urban_20261113/`, never imported and git-ignored like the other raw downloads). Only the
derived copies below are in the Unity project.

## Derived copies (this folder, `Tools/derive_urban_glb.py`, log `derive_log.json`)

- `office_tower_tryfield_gloss.glb` — untextured. The author's gloss comes from `KHR_materials_clearcoat` (roughness 0.04) and
  `KHR_materials_transmission`, which the glTFast HDRP shader does not render, so the tower read as matte black. Clear-coat gloss
  folded into the base roughness (glass overlay 0.6 -> 0.08, panels 0.45 -> 0.15) and the panel colour raised x1.5. Geometry
  unchanged.
- `midrise_w2w_office_aitortilla01_nobrand.glb` — the shop fascias "MAX RENDA" / "GAULLE" and the two signs inside the windows
  (possibly real shop names) are filled with the median colour of a 2 px ring (blank sign boards). JPEG atlas 512 x 1024.
- `ny_hirise_apartment_aitortilla01_nobrand.glb` — real brand signs "Dunkin' Donuts" and "Sperry Top-Sider", the shop signs
  "Dragon's Lair" and "euro concepts, ltd." and a small address line filled the same way; material BLEND -> MASK (cutoff 0.5;
  the texture alpha is a cut-out, the whole building was sorted as transparent). The generic "PARKING / ENTRANCE / EXIT ONLY" sign
  is kept. PNG atlas 1024 x 1024 RGBA.
- GLB binary buffers rebuilt with the edited image; all other buffer views copied unchanged; UVs and meshes untouched.

## Unity prefabs (`Editor/ProceduralAssets.cs` BuildUrbanCollection; dev loop `RebuildUrbanCollectionBatch`)

| key | source | target height | wrapped bounds (m, x / y / z) | scale |
|---|---|---|---|---|
| bld_urban_office_tower | office_tower_tryfield_gloss.glb | 105 m | 32.61 / 105.00 / 23.17 | 0.0029938 (unit-less source) |
| bld_urban_midrise | midrise_w2w_office_aitortilla01_nobrand.glb | 49.5 m | 32.03 / 49.50 / 33.32 | 0.01001 (authored in cm, ~1:1) |
| bld_urban_ny_hirise | ny_hirise_apartment_aitortilla01_nobrand.glb | 120 m | 44.05 / 120.00 / 44.05 (incl. podium) | 0.0081569 (authored 147 m) |

Pivot bottom centre, MeshColliders on every mesh (9 / 1 / 1), TextureDiet on the prefab copies. Placement: `Runtime/Marina01.cs`
(front row) and `HarborParts.ApartmentPlot` with `UrbanCollectionMix` (beach-city tower cells only). Background scenery, never
labelled.

## Credit lines (carry with any published render or dataset)

- "Office Tower" by tryfield is licensed under Creative Commons Attribution 4.0 (https://sketchfab.com/3d-models/office-tower-83a2d83e4c7d42de97fa800d025a2bdf); used in PortCCTVSimulator with scale/pivot changes and material roughness / colour changes on a derived copy; the downloaded GLB is kept unmodified.
- "Mid rise wall to wall office building" by aitortilla01 is licensed under Creative Commons Attribution 4.0 (https://sketchfab.com/3d-models/mid-rise-wall-to-wall-office-building-4d2c3f3450ef4e849056b88b49873ce2); used in PortCCTVSimulator with scale/pivot changes and shop signs blanked on a derived copy; the downloaded GLB is kept unmodified.
- "New York styled hi rise apartment building" by aitortilla01 is licensed under Creative Commons Attribution 4.0 (https://sketchfab.com/3d-models/new-york-styled-hi-rise-apartment-building-14e3d5743f7546d5bb7f3befc72c9057); used in PortCCTVSimulator with scale/pivot changes, brand signs blanked and alpha mode changed on a derived copy; the downloaded GLB is kept unmodified.
- Collection: "Urban" by pineapp1es (https://sketchfab.com/pineapp1es/collections/urban-9102073221854cc5b5f5b429e42b57b5).
