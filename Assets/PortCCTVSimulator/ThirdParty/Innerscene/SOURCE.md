# ThirdParty — Innerscene hulls (CC0)

Copied 2026-11-04 (unmodified) from an internal asset folder (`Vessels_v3/innerscene/`).
Original `.meta` files were NOT copied; this project generates its own importer settings.

| file | upstream page | license (source-stated) | SHA256 (verified equal to source copy and upstream SOURCE.md) |
|---|---|---|---|
| innerscene_rib.glb | https://www.innerscene.com/tools/library/3d-parts/rigid-inflatable-boat-7618a394 | CC0 | fbd157b846a0bcf6e0a73158a7f0dc55366ae962cb903c101ef613cbefc6fa83 |
| innerscene_motorboat_outboard.glb | https://www.innerscene.com/tools/library/3d-parts/motorboat-with-outboard-engine-ad5a12bb | CC0 | 3289e000792d55f10c45b99bf703756d4fddb4e9c309448ebb33de0161729443 |
| innerscene_passenger_ferry_25m.glb | https://www.innerscene.com/tools/library/3d-parts/small-passenger-ferry-25-m-bb6ba356 | CC0 | b96dd250fc61cb6ac69fb4f8962a4b6894e298c83f1d4228f2612092b4d9209a |

License terms: https://www.innerscene.com/tools/library/terms ("Free to use, modify and redistribute for any purpose, including commercially").

Notes carried from the research project (not re-verified here):
- Long axis is +x in the source; pivot at length/width centre and y=0 at the lowest renderer vertex.
- The lowest point of all three hulls is a propulsion part (propeller blade / outboard skeg), not the hull.
- Main-hull reference node names are **name-based candidates** (`semantic_region_confirmed=false`):
  ferry `Finite lofted hull and transom`, RIB `RIB finite rigid V-round hull`, motorboat `Finite lofted hull and transom`.
- No physical draft is known. The simulator's `immersion_m` is a geometric placement parameter, not a measured draft.
