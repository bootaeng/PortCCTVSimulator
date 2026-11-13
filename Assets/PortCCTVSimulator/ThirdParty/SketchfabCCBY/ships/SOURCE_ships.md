# Sketchfab CC BY ship models (2026-11-06)

Found through the public Sketchfab search API (downloadable, licence CC BY 4.0, no NoAI tag), 375 results screened on thumbnail contact sheets;
downloaded from Sketchfab, GLB format, 1k textures where offered. Unity Asset Store items were not considered (ML clause).
Skipped on purpose: models of real, named ships or companies (e.g. Svitzer tug, Riva boat, MSC/Maersk boxes, Scandlines, GTA). Textures were
inspected for logos: the bulk carrier carries fictional names ("TFCC HERITAGE", "IBEE CORP"); container side text on the feeder is a few
pixels high and unreadable; the coast guard / police boats carry the generic words "COAST GUARD" / "POLICE". A Sketchfab "Ferry Concept"
model was not received (receiver count reached with a duplicate LNG transfer) and is not used.

Changes made in the project: uniform scale to a plausible length, -90 deg about X (the sources are Z-up; not for the speedboat), rotation about Y so the bow points to +X (checked on side views, `Captures/hull_check_r3b`, `_r3c`), recentring, removal of scenery nodes
listed below, Unity material conversion by glTFast, MeshColliders. Source files are unmodified (SHA256 below).

| asset_id | model (author) | licence / credit | file | SHA256 | scale / changes |
|---|---|---|---|---|---|
| tug_harbor | [Rastar 3200 tugboat](https://sketchfab.com/3d-models/1bbadbe4ab0a4b2599cd3f450942e6fe) (davidbroutian) | CC BY 4.0, "Rastar 3200 tugboat" by davidbroutian | tug_rastar3200_davidbroutian.glb | 44465db4733e55ff67bf2e2417e76051e65b750aabb3ccae3728628310aa3b78 | as authored (32.9 m) |
| general_cargo | [Cargo ship](https://sketchfab.com/3d-models/b7c97df584824ca682d26daabf401f87) (hungry_drifter) | CC BY 4.0, "Cargo ship" by hungry_drifter | cargo_ship_hungrydrifter.glb | 20de84b0404a6e414543caeb7fc988fe12ef92c4b4b15bd5a94afcacfa628f2e | as authored (127 m) |
| feeder_ship | [Small Cargo Ship](https://sketchfab.com/3d-models/6362d3e9b11e4396aaafa854c3aa6f06) (588276) | CC BY 4.0, "Small Cargo Ship" by 588276 | small_cargo_ship_588276.glb | ff9c2b913f106f2b1e6b8964e6dfe9fa69bf80d4f50f12def6cf1f4a5ac93f46 | x47.5 (2.0 units -> 95 m) |
| bulk_carrier | [Bulk Carrier - TFCC HERITAGE](https://sketchfab.com/3d-models/e78b4f1fdc094a63bb7d6c24d8e7e7dd) (Gwenael_Herve) | CC BY 4.0, "Bulk Carrier - TFCC HERITAGE" by Gwenael_Herve | bulk_carrier_gwenaelherve.glb | 8b02bef2bf097a478c04c024feab255c381d1f7eff2497efb35ab931a4d76e90 | x2.53 (75 m -> 190 m) |
| lng_carrier | [LNG Ship](https://sketchfab.com/3d-models/fa335b96450d4344863bbf5d912a0288) (ArtBlender) | CC BY 4.0, "LNG Ship" by ArtBlender | lng_ship_artblender.glb | 9f38a2e69d5a087b5d6fefe50d86a9db324476fc25594672d4f3c252eff8e117 | x6.43 (43.5 units -> 280 m) |
| trawler | [Salt-Beaten Fishing Trawler](https://sketchfab.com/3d-models/fd28c35b76754c9386b32aa281a0e238) (zoherrose7) | CC BY 4.0, "Salt-Beaten Fishing Trawler" by zoherrose7 | fishing_trawler_zoherrose7.glb | 81412c74b1a571b5558bedef288942888705c74adb868fd9a3360652953b9d08 | x37.8 (0.74 units -> 28 m) |
| fishing_vessel | [Fishing Ship game-ready asset](https://sketchfab.com/3d-models/f791868a8b5f4996a49a3c8cd99336f6) (crazyyuan) | CC BY 4.0, "Fishing Ship game-ready asset" by crazyyuan | fishing_ship_crazyyuan.glb | 97cd675d9fbf51a5d185f9bae6a72850873422e319a17637b76feb305b1a6c69 | x0.01415 (848 cm -> 12 m); removed nodes containing Interior, Light |
| inshore_fishing_boat | [Fisher boat](https://sketchfab.com/3d-models/385b039e95754dca8d8c045ed0c515cf) (Batuhan13) | CC BY 4.0, "Fisher boat" by Batuhan13 | fisher_boat_batuhan13.glb | 2d6a29637424b6aa4b105c6aa609d51a66c6549857353001f6e7afe520e4ff31 | x0.0196 (460 cm -> 9 m) |
| sar_boat | [20m search and rescue boat](https://sketchfab.com/3d-models/6d53e735521f44af82142e6292b2f340) (Nutical3D) | CC BY 4.0, "20m search and rescue boat" by Nutical3D | sar_boat_20m_nutical3d.glb | 70a90d2b219f502b37bcca9f19570d9b101952e2178d54d468529cbeb2e25d39 | x1.87 (10.7 units -> 20 m) |
| pilot_boat | [Pilot II](https://sketchfab.com/3d-models/6ea031237b6b4382ab362790f8af4e66) (agt14032013) | CC BY 4.0, "Pilot II" by agt14032013 | pilot_boat_agt14032013.glb | 16353395c3e11b153fb969c90e7fecea6359d550ca7902fb7fdcda68083f0d63 | x0.973 (18.5 m -> 18 m) |
| patrol_boat | [COAST GUARD 05](https://sketchfab.com/3d-models/7e29c40c92774a5cb534613bd5dfb3c7) (agt14032013) | CC BY 4.0, "COAST GUARD 05" by agt14032013 | coast_guard_05_agt14032013.glb | e8b7472b52fa1e0ff59c0486ed6faba58714b2db7fa938d911f66dbd74e329ad | x1.6 (14.4 m -> 23 m) |
| police_boat | [Police Boat](https://sketchfab.com/3d-models/959e50ef12a546d8a203182c4ccef17a) (agt14032013) | CC BY 4.0, "Police Boat" by agt14032013 | police_boat_agt14032013.glb | 5906cd6e02ac450aa07a899aaa5d3c14b14f62319c02374f60968464408a8361 | x1.22 (9.8 m -> 12 m) |
| speedboat | [Speedboat](https://sketchfab.com/3d-models/9916bd4433024202ab84e43426fc2139) (vrpspu) | CC BY 4.0, "Speedboat" by vrpspu | speedboat_vrpspu.glb | 5a86b7e5cbd4081f136cc289504cc2622a084994b5af274a086d3e2fddff7ddf | x0.65 (10.7 units -> 7 m); ground plane, sky sphere and backdrop plane removed; removed the ground plane, sky sphere, the backdrop mesh of node "1" and the "flag" node (a naval ensign) |
| motor_yacht | [Yacht II](https://sketchfab.com/3d-models/dadc775d1d044aee9f6744fa7b47648e) (agt14032013) | CC BY 4.0, "Yacht II" by agt14032013 | yacht_ii_agt14032013.glb | c161d183afaa20f64857ffb60086292053fd2deb5f249733630549adca87f152 | x0.38 (105 units -> 40 m) |

## Vessel batch 2026-11-10

Downloaded 2026-11-10 the same way as the town assets (`../town3/SOURCE_town3.md`): user's logged-in Sketchfab session, licence
re-checked at download ("CC Attribution", downloadable, no NoAI tag), originals in `ThirdParty_raw/sketchfab_assets_20261110/`.
Catalog duplicates checked: none of the three is a model already in the catalog (the Innerscene `fishing_boat` 8.7 m, `motorboat`
and `rib` are different models). Orientation: the bow end was found from the hull width near both ends of the length axis (narrow
end = bow) in the glTF frame and mapped with the rule that fits the existing agt14032013 / Sketchfab models; checked on the
prefab previews (`Logs/town_asset_previews/vessel_*.png`).

| asset_id | model (author) | licence / credit | file in project | SHA256 in project (original) | scale / changes |
|---|---|---|---|---|---|
| fishing_boat_iii | [Fishing Boat III](https://sketchfab.com/3d-models/fishing-boat-iii-ade40638aaa14c708e6eac1e98022f63) (gogiart, agt14032013) | CC BY 4.0, "Fishing Boat III" by agt14032013 | fishing_boat_iii_agt14032013.glb | 0f966995c983d32f81407308256195aaaa84b979214cb08132e9839e2d693cd1 (ad0ba32398c39c40afc0a89ea1608ca522cecd3fc14b2fc0f0aa2897d3fd685a, 1k) | as authored (23.1 m); textures re-encoded <= 1k (`glb_derive.py --max-px 1024`, no decimation); -90 X, yaw -90, recentred; immersion 1.0 m |
| small_motorboat | [Small Motorboat](https://sketchfab.com/3d-models/small-motorboat-b488d632a4804cd783e9ef009976bcd4) (tijmenmatthys) | CC BY 4.0, "Small Motorboat" by tijmenmatthys | small_motorboat_tijmenmatthys.glb | 2ca7195cfac20c208cf88eb2589604a1622b1f5f1dfa9ea14c082698228b0abc (55e4c30f6b7166338c76984dc7be959129ccabcaf6a894737e2037e854eb403e, 1k) | as authored (6.04 m); the outboard maker's lettering (two marks on the cowling, texture 0) inpainted (`--inpaint "0:30,850,215,900;0:30,955,215,1005"`, 1,295 px, `Logs/derive_20261110/small_motorboat.json`); yaw 180, recentred; immersion 0.25 m |
| crrc | [CRRC (Combat Rubber Raiding Craft)](https://sketchfab.com/3d-models/crrc-combat-rubber-raiding-craft-04f24d42e8ad4446aac46af980568e85) (blarg1) | CC BY 4.0, "CRRC (Combat Rubber Raiding Craft)" by blarg1 | crrc_blarg1.glb | 7ea87cab0fd7801e69a3ab296bdc8423880edd4a11db5f8ecd5e19c6f96c9182 (ee0fff9cc8043f6decc9f5055fef5199e9d855f7eebe3adb686f00cd0a36fcc8, 1k) | x1.05 (4.47 m -> 4.7 m, the F470 class length); textures re-encoded <= 1k; yaw -90, recentred; immersion 0.15 m. The model is a rubber boat; it carries no crew. Textures hold no brand name (the real craft it is modelled after is not named on the textures). |

Credit lines:
- "Fishing Boat III" by agt14032013 is licensed under Creative Commons Attribution 4.0 (https://sketchfab.com/3d-models/fishing-boat-iii-ade40638aaa14c708e6eac1e98022f63); used in PortCCTVSimulator with re-encoded textures and a new pivot.
- "Small Motorboat" by tijmenmatthys is licensed under Creative Commons Attribution 4.0 (https://sketchfab.com/3d-models/small-motorboat-b488d632a4804cd783e9ef009976bcd4); used in PortCCTVSimulator with the engine maker's lettering removed and a new pivot.
- "CRRC (Combat Rubber Raiding Craft)" by blarg1 is licensed under Creative Commons Attribution 4.0 (https://sketchfab.com/3d-models/crrc-combat-rubber-raiding-craft-04f24d42e8ad4446aac46af980568e85); used in PortCCTVSimulator rescaled to 4.7 m with re-encoded textures.
