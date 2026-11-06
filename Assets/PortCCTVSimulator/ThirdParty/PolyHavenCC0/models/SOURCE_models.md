# Poly Haven CC0 models (props, trees, rocks)

Downloaded 2026-11-04 via https://api.polyhaven.com (glTF, 1k textures), MD5+size verified, SHA256 in `download_manifest_models.json`. License CC0 (https://polyhaven.com/license).

`pine_tree_01` (16.9M vertices) and `fir_tree_01` (9.0M) were downloaded but NOT copied (too heavy).


| asset | authors | use |
|---|---|---|
| Barrel_01 | Jorge Camacho | quay props |
| lifebuoy | Hank Kaamura | pontoon/quay props |
| old_tyre | MP | tyre fenders on small quays |
| plastic_crate_01 | PierreB3D | fishing gear |
| plastic_crate_02 | Fabi_G | fishing gear |
| street_lamp_01 | Josh Dean | promenade / quay lamps |
| security_light | Maximilian Schuster | warehouse lights |
| modular_chainlink_fence | James Ray Cock, Amal Kumar | terminal fence |
| modular_electricity_poles | James Ray Cock | village poles |
| island_tree_01 | Rob Tuytel, Rico Cilliers | trees (decimated copy) |
| island_tree_02 | Rob Tuytel, Rico Cilliers | trees (decimated copy) |
| coast_rocks_01 | Rob Tuytel, Rico Cilliers | hero shore rocks (decimated copy) |
| coast_land_rocks_02 | Rob Tuytel, Rico Cilliers | hero shore rocks (decimated copy) |

## Derived files (decimated, `Tools/decimate_gltf.py`, grid clustering; textures unchanged)

- `island_tree_01_dec\island_tree_01_1k_dec.gltf` sha256 785b9d2a79837a0ac67bf5f56d3f82633ad99c68eb666b78f0e575aaf24c2057 — from `island_tree_01/island_tree_01_1k.gltf` (sha256 78e905a40670bfabb2760601d4d9a6c17193a50d68e6271c66893eb7bd75906d), cell 0.06 m
- `island_tree_01_dec\island_tree_01_1k_dec.bin` sha256 2ad80709aa69a30b203f1f44353c62aebbefb57e899656b6f325bc7f9b86a71e — from `island_tree_01/island_tree_01_1k.gltf` (sha256 78e905a40670bfabb2760601d4d9a6c17193a50d68e6271c66893eb7bd75906d), cell 0.06 m
- `island_tree_02_dec\island_tree_02_1k_dec.gltf` sha256 1b8ab2ebc06d27f709ec7c2d0d87175b29dfa4933f0ffd6e8d761cf6799f3727 — from `island_tree_02/island_tree_02_1k.gltf` (sha256 d8c5d9ead41cdbef91a648f40ff60470faa19eb3a7010e59e44cb1a01250f75d), cell 0.05 m
- `island_tree_02_dec\island_tree_02_1k_dec.bin` sha256 ad432a95c1ff2b22658d8df12c79011e8bb7cbe48ece3323f4a866484c09c40f — from `island_tree_02/island_tree_02_1k.gltf` (sha256 d8c5d9ead41cdbef91a648f40ff60470faa19eb3a7010e59e44cb1a01250f75d), cell 0.05 m
- `coast_rocks_01_dec\coast_rocks_01_1k_dec.gltf` sha256 e0bda779ebbbbabcbf7f1830e40cf7e567b7a4d1f2bbab91aa8c9835ae477c5a — from `coast_rocks_01/coast_rocks_01_1k.gltf` (sha256 9d45b232fe8bfc8f9d84aaa900ba28f150764e4b40accf7d50fbb45a3de54c51), cell 0.35 m
- `coast_rocks_01_dec\coast_rocks_01_1k_dec.bin` sha256 51ec246f72cfb36a3ad43b3c37fba33ec7016e2aa40314800dd2d8269e62a9cf — from `coast_rocks_01/coast_rocks_01_1k.gltf` (sha256 9d45b232fe8bfc8f9d84aaa900ba28f150764e4b40accf7d50fbb45a3de54c51), cell 0.35 m
- `coast_land_rocks_02_dec\coast_land_rocks_02_1k_dec.gltf` sha256 1f2bc5b6c6925588455d3247ee89509ea6e386bc6266974dbce6acdcbfa8784e — from `coast_land_rocks_02/coast_land_rocks_02_1k.gltf` (sha256 f99e5073bf2df714da68e09d80d8546ded2aad0b73f50df8d48e732d4a452774), cell 0.08 m
- `coast_land_rocks_02_dec\coast_land_rocks_02_1k_dec.bin` sha256 0905ce67862f6eca529bc9f045e9827cbdfbe4a1e10c2f481fe1d4333bd7ad87 — from `coast_land_rocks_02/coast_land_rocks_02_1k.gltf` (sha256 f99e5073bf2df714da68e09d80d8546ded2aad0b73f50df8d48e732d4a452774), cell 0.08 m
