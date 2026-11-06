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
