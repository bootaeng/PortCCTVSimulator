# Sketchfab CC BY 4.0 harbor props — attribution required

Downloaded 2026-11-05 from Sketchfab. Licence and tags checked on the Sketchfab API the same day: all four are
CC Attribution (`license.slug == by`) with no NoAI tag. GLB = Sketchfab-converted format. Unity Asset Store not used.

| file in project | title / author | URL | original GLB SHA256 | project file SHA256 | changes |
|---|---|---|---|---|---|
| harbor_crane_1k.glb | "Harbor crane" by MisterH (@TGVMisterH) | https://sketchfab.com/3d-models/d4d582ebd707426f93f1f5a9cdd0ce03 | fa35489f44b95b15383570ca8043d77761b0b2053dffb71957028dd3ab70db05 | same (unmodified) | none on the GLB; prefab wrapper only |
| port_crane_sokol_1k.glb | "Port crane \"Sokol\"" by Vitaliy Kovalchuk (@Domender) | https://sketchfab.com/3d-models/4964eaa7634e47c9bac4c2a74fa91c20 | 664f41625715fc3ef450e2d618596431201f91228b854dbc79a8334c66d6f2f0 | same (unmodified) | none on the GLB; prefab wrapper only |
| gantry_crane_rtg_dec15.glb | "Gantry Crane" by alanUscanga | https://sketchfab.com/3d-models/76119b46c26f436ba78f4b53c98fc935 | ae0bf0e38b03428ba134116f3ce582cf110f90e6abc091cd3441576854f1a08f | b1ed9c19fc69b10666297a2c2786a75a8171189d12560d965a6f10faaeb6a5df | **decimated** 996,777 -> 170,307 triangles (`Tools/decimate_glb.py`, reduction 0.85; positions + indices only, normals recomputed on import); prefab scaled x0.001 (source units are millimetres) |
| breakwater_coast_2k.glb | "Breakwater coast" by SKMT (@srkmt) | https://sketchfab.com/3d-models/1750dcf5748549ec9a50795d75f48241 | 134fd23127c8d56b91cc71666804d6039e9bb15576dcccfe88d6bd603f914f8b | a7af6cb4f2aca6c3ba288fe8e5abc2a2f6cf9008f67068b2db3966ef37ec9261 | **textures resized** 4096 -> 2048 px (`Tools/glb_downscale_textures.py`); geometry unchanged |

## Credit lines (carry with any published render or dataset)
- "Harbor crane" by MisterH is licensed under Creative Commons Attribution 4.0 (https://sketchfab.com/3d-models/d4d582ebd707426f93f1f5a9cdd0ce03); placed and recentred by PortCCTVSimulator.
- "Port crane "Sokol"" by Vitaliy Kovalchuk (Domender) is licensed under Creative Commons Attribution 4.0 (https://sketchfab.com/3d-models/4964eaa7634e47c9bac4c2a74fa91c20); placed and recentred by PortCCTVSimulator.
- "Gantry Crane" by alanUscanga is licensed under Creative Commons Attribution 4.0 (https://sketchfab.com/3d-models/76119b46c26f436ba78f4b53c98fc935); decimated to 17 % of its triangles and scaled from millimetres to metres by PortCCTVSimulator.
- "Breakwater coast" by SKMT is licensed under Creative Commons Attribution 4.0 (https://sketchfab.com/3d-models/1750dcf5748549ec9a50795d75f48241); textures resized to 2048 px by PortCCTVSimulator.

## Content checks (2026-11-05)
- Scale: RTG tyre 1.63 m, overall 21.5 x 25.1 x 12.5 m after x0.001; harbor crane bogie height 1.2 m, undercarriage 6.9 x 10.4 m (metres as authored); Sokol 42 m high, hook radius 28.5 m (metres); breakwater strip 92 x 17 m, rocks ~1 m (metres).
- Logos: the harbor crane texture carries the author's fictional "MRTECHS CRANES" mark and "C MRH 7766" numbers; no real manufacturer logo was found in any atlas. Sokol and breakwater textures carry no text.
- No vessel is part of any of the four models (the breakwater scan is rocks and sand only).
- The originals stay outside the project (scratchpad of the 2026-11-05 session); only the files above are in the build.
