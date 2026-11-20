# Loaded container ships (2026-11-16)

Three container ships with stacked deck cargo, added 2026-11-16 at the user's request ("갑판에 컨테이너를 빽빽하게 싣고 있는 선박").
They replace the procedural 172 m `container_ship`, which was removed from the catalog. That id is kept as an alias:
`VesselCatalog.Retired` maps it to `container_ship_loaded` with that hull's default immersion, and `ScenarioIO.FromJson`
rewrites old scenario files on load. `feeder_ship` (95 m, `SOURCE_ships.md`) is unchanged.

Downloaded 2026-11-16. Sketchfab models were downloaded through the user's signed-in Sketchfab session. The licence shown at
download was "CC Attribution", the model was downloadable, and no NoAI tag was present (all four Sketchfab candidates checked,
including the unused backup jhervianto "Container Ship"). The Innerscene model came from the public Innerscene library
download. Originals are kept, unmodified, in `ThirdParty_raw/container_ships_20261116/`, which is git-ignored. Unity Asset Store
items were not considered (ML clause).

Like every asset in this simulator, these hulls only produce provisional / diagnostic labels; research outputs stay
`training_ready=false`.

| asset_id | model (author) | licence / credit | original file, SHA256 | file in project, SHA256 | processing |
|---|---|---|---|---|---|
| container_ship_loaded | [Container ship loaded](https://www.innerscene.com/tools/library/3d-parts/container-ship-loaded-5ee3b06d) (Innerscene, "original model", library entry dated 2026-10-28) | CC0 (source-stated; [terms](https://www.innerscene.com/tools/library/terms)) | innerscene_container_ship_loaded.glb, 95b5020347eadb53d4e3cbdbe845b7fdde718435964384954c516db05a532680 (page `fileHash` d6b5d792a5055ca75e442278f24898499e79e86f0cee38d0b5664774fe9aed1f, a different digest than SHA256 of the file) | `../../Innerscene/innerscene_container_ship_loaded.glb`, same file | unmodified; metres as authored (page: 262.2 x 32.7 x 49 m, 820 x 40 ft containers, up to 6 tiers); yaw 180 (bow +X), recentred |
| container_ship_rm02 | [Container Ship](https://sketchfab.com/3d-models/container-ship-aaa41cca946b4a08bc08cf692b7757be) (RM02) | CC BY 4.0, "Container Ship" by RM02 | container_ship_rm02_1k.glb (1k textures), 27b74e02d6c578a935fef6b355c1d97793c58dc1834c25b9beb0bc2648db7f91 | container_ship_rm02_nobrand.glb, a4b1cff1a36f4e94155ae40db88b93a062267c135fadd164116c7fc12f823bed | shipping-line marks removed from the textures, container metallic cleared (below); x2.639 (40 ft container pitch -> 241 m long); -90 deg about X (Z-up source), yaw 180 (bow +X), recentred |
| container_ship_gogi | [Container Ship 03 FBX](https://sketchfab.com/3d-models/container-ship-03-fbx-931fb30ec63a40c6a1ebfe51a454263c) (gogiart, Sketchfab user agt14032013) | CC BY 4.0, "Container Ship 03 FBX" by gogiart (agt14032013) | container_ship_03_agt14032013_1k.glb (1k textures), 29afd4c180052b5c027bcb01c84443995523c238e3c300946776d25f12f774ee | container_ship_03_gogiart_opt.glb, 61e38fc8c7f9f0ad05d4eea9c84e5cf8b6881dd93f6af8c5edd512a3be0288c5 | geometry reduced (below); x34 (container tier 0.0762 units = 2.59 m, 9.5 ft high cube -> 146 m long); -90 deg about X, yaw 90 (bow +X), recentred |

Glass materials are swapped for one opaque dark-glass material, and textures pass through `TextureDiet`, as for every hull
(`Editor/ProjectBootstrap.cs`). MeshColliders are built on the same meshes that are rendered and masked.

## Logos and marks

- **Innerscene**: textures checked (27 images, 2k): no company names. The container atlas holds eight plain container colours.
- **RM02**: the container atlas carries real shipping-line marks ("msc", "CMA CGM", "tex"/Textainer) and "MRC", which imitates MSC.
  The containers' metallic-roughness map also carries "MRC". The hull atlas carries "MRC" on both sides, small lettering, a name
  plate and a winged emblem. `Tools/derive_container_ship_glb.py` fills each marked rectangle with the median colour of the 2 px ring
  around it: 8 rectangles on the container atlas (one covers the whole navy block face with the CMA CGM logos), 4 on the
  metallic-roughness map, 6 on the hull atlas (8 + 4 + 6 = 18 edits). Every rectangle is listed in `derive_log_container_ships.json`. The cleaned
  textures were checked at full size and with raised contrast. Geometry and UVs are unchanged.
- **RM02 container metallic**: the same metallic-roughness map gives the painted containers a metallic value of 0.23 on
  average (blue channel), which darkened their colours in the first CCTV renders (`Captures/container_ships_20261116_r1`).
  The derived copy sets that channel to 0 (painted steel boxes are dielectric); roughness is unchanged. This is logged as the
  19th entry of `derive_log_container_ships.json`.
- **gogiart**: container door textures carry only generic markings (an owner-code-style string "MCBU 2120969", weight plates),
  no company name or logo; kept.

## Geometry reduction (container_ship_gogi)

The 1k download has 1,270,589 triangles. Most of them sit in small stern details and in one flat deck plate.
`Tools/derive_container_ship_gogi_glb.py` (log: `derive_log_container_ship_gogi.json`):

- Primitives above 4,000 triangles are welded by position and reduced with quadric edge collapse (`fast_simplification` 0.2.0,
  open borders kept) to 10 % of their triangles, at least 1,500. UVs follow the collapse map. Normals are dropped, so glTFast
  computes flat normals (hard-surface look).
- 21 dense micro-detail primitives (stern gratings, bolt clusters, small winches) with less than 10 m2 of total surface at ship
  scale are removed: 0.09-4.2 m2 each, 5,120-116,830 triangles each, 464,105 in total.
- The hull, superstructure and container stacks are below the threshold and stay as authored. Side and top silhouettes were
  compared before and after.
- Materials identical except for their name are merged.

Result: 153,211 triangles, 49 materials (from 59). There is one level of detail. The simulator renders, masks, collides and
labels the same meshes, so it has no runtime LOD switch that could make the RGB frame and the labels disagree.
