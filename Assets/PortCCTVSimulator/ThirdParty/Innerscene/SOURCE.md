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

## Added 2026-11-16: loaded container ship

Downloaded directly from the public Innerscene library (not from the research project), unmodified. Details, licence check,
logo check and processing: `../SketchfabCCBY/ships/SOURCE_container_ships.md`.

| file | upstream page | license (source-stated) | SHA256 |
|---|---|---|---|
| innerscene_container_ship_loaded.glb | https://www.innerscene.com/tools/library/3d-parts/container-ship-loaded-5ee3b06d | CC0 | 95b5020347eadb53d4e3cbdbe845b7fdde718435964384954c516db05a532680 |

- Bow +X in the glTF (yaw 180 in the simulator, like the other Innerscene hulls), metres as authored: 262.2 x 32.7 x 49.0 m.
- Hull reference node `Hull` (unique; its lowest vertex is the keel at y = 0). Default `immersion_m` 11.6 m = the top of the
  material "Antifouling red bottom paint" (a design load line, not a measured draft).
