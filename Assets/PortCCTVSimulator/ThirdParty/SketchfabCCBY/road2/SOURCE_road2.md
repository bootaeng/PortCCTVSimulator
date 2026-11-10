# Sketchfab CC BY 4.0 road assets (second batch) — attribution required

Downloaded 2026-11-09 from Sketchfab through the user's logged-in session (page `i/models/<uid>/download` -> signed S3 URL ->
loopback-only receiver `127.0.0.1:8765`, CORS limited to sketchfab.com). Licence checked on the Sketchfab API: all
"CC Attribution", no NoAI tag. Unmodified originals (GLB as converted by Sketchfab) are kept outside Assets in
`ThirdParty_raw/sketchfab_road2_20261109/` (not in the repository).

| file in project | title / author | URL | SHA256 of the original GLB | change |
|---|---|---|---|---|
| guardrail_terminal_xplanepilot_1k.glb (sha256 af06925756a7e2e9e83bc1f02fd5dad22ce484cd229e1527ee3fa9ac94005636) | "GuardRail and Terminal" by xplanepilot | https://sketchfab.com/3d-models/693dee3dc6a1495f90ab0ba1f8237cb4 | 2fe68db658f6f429eaaba27bfaae4011819ca9bab204e7e1f1a6ab302cf74dae (2k textures) | textures downscaled to 1k (`Tools/glb_downscale_textures.py`); prefabs `guardrail_seg` (4 m segment) and `guardrail_end` (terminal) |
| road_sign_pack_awlokdev_2k.glb (sha256 18b340507bd4ff822fa151498a8eaaca0cb38179ffa9eb2c81066cada3c18ae6) | "Road Sign Pack (Free)" by awlok.dev | https://sketchfab.com/3d-models/af5ed08436ee4cb4ae2624a4c5f06a4a | bac54f35e4ba5775247105a9bfc67f5ef0f50b201a6e8ab410a4d648fe4758bd (4k textures) | textures downscaled to 2k; prefabs `road_sign_1..6` (1 no entry, 2 U-turn, 3 speed limit 60, 4 keep right, 5 no left turn, 6 no parking) |
| lighthouse_fabiolopez_2k.glb (sha256 b972512db27112c2478acf5e35542bef18d98d31989862b61dfa0b580e912606) | "Lighthouse" by Fabio | https://sketchfab.com/3d-models/3b475508402e4fb98d65fad6489b875e | 5d3b563a762df100258b5237fead78a7e9367388ec19bc1e71d6b6344f09ca05 (4k textures) | textures downscaled to 2k; prefab `lighthouse_tall` scaled to 26 m, the 1.1 km light-beam cylinder (material aiStandardSurface10) dropped |
| tex/road_pack_{road,sidewalk}_{diff,nor_gl}_2k.jpg | "Modular Road Pack (FREE) + 4K Textures" by intfloat (textures based on ambientCG) | https://sketchfab.com/3d-models/b84f5cc449ff49e38d853ea393e35885 | 063f5797606422679be859ff8e5538768a9321347a64c45ab0078deeea512913 (4k textures) | only the Road and Sidewalk material textures are used, resized to 2k JPEG: road_diff 0da953eb…, road_nor_gl caebb1d4…, sidewalk_diff 6ac062ed…, sidewalk_nor_gl 47d98d0a…; materials `road_asphalt` and `sidewalk_paving`. The road meshes are not used. |

## Credit lines (carry with any published render or dataset)
- "GuardRail and Terminal" by xplanepilot is licensed under Creative Commons Attribution 4.0 (https://sketchfab.com/3d-models/693dee3dc6a1495f90ab0ba1f8237cb4); used in PortCCTVSimulator with downscaled textures, scale/pivot changes and node selection on prefab copies.
- "Road Sign Pack (Free)" by awlok.dev is licensed under Creative Commons Attribution 4.0 (https://sketchfab.com/3d-models/af5ed08436ee4cb4ae2624a4c5f06a4a); used in PortCCTVSimulator with downscaled textures, scale/pivot changes and node selection on prefab copies.
- "Lighthouse" by Fabio is licensed under Creative Commons Attribution 4.0 (https://sketchfab.com/3d-models/3b475508402e4fb98d65fad6489b875e); used in PortCCTVSimulator with downscaled textures, the light-beam mesh removed and uniform scaling.
- "Modular Road Pack (FREE) + 4K Textures" by intfloat is licensed under Creative Commons Attribution 4.0 (https://sketchfab.com/3d-models/b84f5cc449ff49e38d853ea393e35885); its road and sidewalk textures are used (resized) as world-mapped materials in PortCCTVSimulator.

## Content checks (2026-11-09)
- Sign faces are Vienna-convention pictograms without text (the speed-limit sign shows "60"); they resemble Korean regulatory signs. No company marks in any texture. No vessel in any model.
- The lighthouse is styled after a US masonry tower (the author cites the Cape Florida Lighthouse); it is used only as a distant headland landmark (one per map), not as a Korean breakwater light.
