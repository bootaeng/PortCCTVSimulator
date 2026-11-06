using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace PortCCTV.EditorTools
{
    /// <summary>
    /// Idempotent project setup. Run from the menu or:
    ///   Unity.exe -batchmode -projectPath <root> -executeMethod PortCCTV.EditorTools.ProjectBootstrap.RunBatch -logFile Logs/bootstrap.log
    /// </summary>
    public static class ProjectBootstrap
    {
        const string Root = "Assets/PortCCTVSimulator";
        const string SettingsDir = Root + "/Settings";
        const string MaterialsDir = Root + "/Materials";
        const string VesselsDir = Root + "/Vessels";
        const string ResourcesDir = Root + "/Resources/PortCCTV";
        const string ScenePath = Root + "/Scenes/PortCCTV_Main.unity";
        const string ThirdParty = Root + "/ThirdParty/Innerscene";
        public const string RenderMinReference = "__render_min__";

        struct HullSpec
        {
            public string id, display, category, file, sha, url, hullNode;
            public float immersion;
            public string dir;        // folder under ThirdParty (default Innerscene)
            public string license;    // default CC0 (Innerscene, source-stated)
            public float scale;       // uniform scale of the model (0 = 1)
            public float yaw;         // rotation applied to the model so its BOW points to +X (heading 0 = bow +X); checked on side views
            public float pitch;       // rotation about X before the yaw (-90: Z-up source -> Y-up)
            public bool recenter;     // move the model so bounds are centred in X/Z with the lowest vertex at y=0
            public string procedural; // "tug" | "barge" -> built by ProceduralVessels
            public string[] drop;     // node-name fragments removed after import (ground planes, sky spheres, interiors)
            public string[] paint;    // repaint overrides by material name: "hull:NAME" | "nohull:NAME" | "keep:NAME" (VesselPaintAnalysis)
        }

        static readonly HullSpec[] Hulls =
        {
            new HullSpec { id = "rib", display = "RIB (~7 m)", category = "rib", file = "innerscene_rib.glb",
                sha = "fbd157b846a0bcf6e0a73158a7f0dc55366ae962cb903c101ef613cbefc6fa83",
                url = "https://www.innerscene.com/tools/library/3d-parts/rigid-inflatable-boat-7618a394",
                hullNode = "RIB finite rigid V-round hull", immersion = 0.10f, yaw = 180f },
            new HullSpec { id = "motorboat", display = "Motorboat, outboard", category = "motorboat", file = "innerscene_motorboat_outboard.glb",
                sha = "3289e000792d55f10c45b99bf703756d4fddb4e9c309448ebb33de0161729443",
                url = "https://www.innerscene.com/tools/library/3d-parts/motorboat-with-outboard-engine-ad5a12bb",
                hullNode = "Finite lofted hull and transom", immersion = 0.10f, yaw = 180f },
            new HullSpec { id = "ferry", display = "Passenger ferry 25 m", category = "passenger_ferry", file = "innerscene_passenger_ferry_25m.glb",
                sha = "b96dd250fc61cb6ac69fb4f8962a4b6894e298c83f1d4228f2612092b4d9209a",
                url = "https://www.innerscene.com/tools/library/3d-parts/small-passenger-ferry-25-m-bb6ba356",
                hullNode = "Finite lofted hull and transom", immersion = 0.40f, yaw = 180f },
            new HullSpec { id = "fishing_boat", display = "Fishing boat 8.7 m", category = "fishing_boat", file = "innerscene_fishing_boat.glb",
                sha = "2a930e03264b49b056f7525151cddacab88e6d1398c9cff254dc5240267e0882",
                url = "https://www.innerscene.com/tools/library/3d-parts/fishing-boat-3c5a6bfd", hullNode = "Finite lofted hull and transom", immersion = 0.40f, yaw = 180f },
            new HullSpec { id = "cabin_cruiser", display = "Cabin cruiser 10.2 m", category = "cabin_cruiser", file = "innerscene_cabin_cruiser.glb",
                sha = "b40cc619c7e6ee76966813ff4de5a8f3f346cad124980aec0f2d2344a112ce5b",
                url = "https://www.innerscene.com/tools/library/3d-parts/cabin-cruiser-597d60da", hullNode = "Finite lofted hull and transom", immersion = 0.30f, yaw = 180f },
            new HullSpec { id = "sailing_yacht", display = "Sailing yacht 8 m", category = "sailing_yacht", file = "innerscene_sailing_yacht_8m.glb",
                sha = "62d3fbbfaa15044a777f5ffb44f02641e7e8942d1aeefd801ea47a6f0864fe38",
                url = "https://www.innerscene.com/tools/library/3d-parts/sailing-yacht-8-m-aa9dbd30", hullNode = "Yacht exact8m finite hull", immersion = 0.30f, yaw = 180f },
            new HullSpec { id = "sailing_dinghy", display = "Sailing dinghy 4.2 m", category = "sailing_dinghy", file = "innerscene_sailing_dinghy_4_2m.glb",
                sha = "f7e7de6ce9607ee2e30c8b7c9395246142444d70c2ff0a1187546b625e0c349c",
                url = "https://www.innerscene.com/tools/library/3d-parts/sailing-dinghy-4-2-m-b14c2dce", hullNode = "Original finite hollow hull", immersion = 0.12f, yaw = 180f },
            new HullSpec { id = "kayak", display = "Kayak (single)", category = "kayak", file = "innerscene_kayak_single.glb",
                sha = "d63a33957312fc242799e46f00e2a80c31d3d5eed994726973e36ae825a26379",
                url = "https://www.innerscene.com/tools/library/3d-parts/single-seat-kayak-31da268f", hullNode = "Original finite hollow hull", immersion = 0.10f, yaw = 180f },
            new HullSpec { id = "canoe", display = "Open canoe", category = "canoe", file = "innerscene_open_canoe.glb",
                sha = "3b0de8023ee4128d34d169e4f10a2eae80470ea086aa5e4543cd90bab077b9c0",
                url = "https://www.innerscene.com/tools/library/3d-parts/open-canoe-b46d64a0", hullNode = "Original finite hollow hull", immersion = 0.10f, yaw = 180f },
            new HullSpec { id = "tugboat", display = "Harbour tug 24 m (procedural)", category = "tugboat", procedural = "tug",
                hullNode = ProceduralVessels.HullNodeName, immersion = 1.0f, license = "procedural (this project)" },
            new HullSpec { id = "barge", display = "Deck barge 50 m (procedural)", category = "barge", procedural = "barge",
                hullNode = ProceduralVessels.HullNodeName, immersion = 0.15f, license = "procedural (this project)" },
            new HullSpec { id = "container_ship", display = "Container feeder 172 m (procedural)", category = "container_ship", procedural = "container_ship",
                hullNode = ProceduralVessels.HullNodeName, immersion = 4.2f, license = "procedural (this project)" },
            new HullSpec { id = "korean_fishing_boat", display = "Korean coastal fishing boat 19 m (procedural)", category = "fishing_boat_coastal", procedural = "kfish",
                hullNode = ProceduralVessels.HullNodeName, immersion = 0.45f, license = "procedural (this project)" },
            new HullSpec { id = "tanker", display = "Tanker 322 m (CC BY)", category = "tanker", file = "sketchfab_tanker_suezmax_artblender.glb",
                dir = "SketchfabCCBY", sha = "4b9fdd1dad65acae86b28a2434937070d869a97d7e3ee0a0b14e2f05274fa02d",
                url = "https://sketchfab.com/3d-models/96ebf61af42b4062ae98a6ad848e1a25", hullNode = RenderMinReference, immersion = 6f,
                license = "CC BY 4.0 - \"Tanker Ship\" by Art Blender (https://sketchfab.com/3d-models/96ebf61af42b4062ae98a6ad848e1a25); scaled x1.9845 (stated length 322 m), rotated (bow +X), recentred",
                scale = 1.9845f, yaw = -90f, recenter = true },
            // Sketchfab CC BY ships (2026-11-06; ThirdParty/SketchfabCCBY/ships/SOURCE_ships.md): more realistic, textured hulls
            new HullSpec { id = "tug_harbor", display = "Harbour tug 33 m (CC BY)", category = "tugboat", file = "tug_rastar3200_davidbroutian.glb",
                dir = "SketchfabCCBY/ships", sha = "44465db4733e55ff67bf2e2417e76051e65b750aabb3ccae3728628310aa3b78",
                url = "https://sketchfab.com/3d-models/1bbadbe4ab0a4b2599cd3f450942e6fe", hullNode = RenderMinReference, immersion = 6.0f,
                license = "CC BY 4.0 - \"Rastar 3200 tugboat\" by davidbroutian (https://sketchfab.com/3d-models/1bbadbe4ab0a4b2599cd3f450942e6fe); as authored (32.9 m), rotated (bow +X), recentred",
                pitch = -90f, scale = 1.0f, yaw = 90f, recenter = true },
            new HullSpec { id = "general_cargo", display = "General cargo ship 127 m (CC BY)", category = "general_cargo", file = "cargo_ship_hungrydrifter.glb",
                dir = "SketchfabCCBY/ships", sha = "20de84b0404a6e414543caeb7fc988fe12ef92c4b4b15bd5a94afcacfa628f2e",
                url = "https://sketchfab.com/3d-models/b7c97df584824ca682d26daabf401f87", hullNode = RenderMinReference, immersion = 5.0f,
                license = "CC BY 4.0 - \"Cargo ship\" by hungry_drifter (https://sketchfab.com/3d-models/b7c97df584824ca682d26daabf401f87); as authored (127 m), rotated (bow +X), recentred",
                pitch = -90f, scale = 1.0f, yaw = 180f, recenter = true },
            new HullSpec { id = "feeder_ship", display = "Container feeder 95 m (CC BY)", category = "container_ship", file = "small_cargo_ship_588276.glb",
                dir = "SketchfabCCBY/ships", sha = "ff9c2b913f106f2b1e6b8964e6dfe9fa69bf80d4f50f12def6cf1f4a5ac93f46",
                url = "https://sketchfab.com/3d-models/6362d3e9b11e4396aaafa854c3aa6f06", hullNode = RenderMinReference, immersion = 4.0f,
                license = "CC BY 4.0 - \"Small Cargo Ship\" by 588276 (https://sketchfab.com/3d-models/6362d3e9b11e4396aaafa854c3aa6f06); x47.5 (2.0 units -> 95 m), rotated (bow +X), recentred",
                pitch = -90f, scale = 47.5f, yaw = 0f, recenter = true },
            new HullSpec { id = "bulk_carrier", display = "Bulk carrier 190 m (CC BY)", category = "bulk_carrier", file = "bulk_carrier_gwenaelherve.glb",
                dir = "SketchfabCCBY/ships", sha = "8b02bef2bf097a478c04c024feab255c381d1f7eff2497efb35ab931a4d76e90",
                url = "https://sketchfab.com/3d-models/e78b4f1fdc094a63bb7d6c24d8e7e7dd", hullNode = RenderMinReference, immersion = 7.0f,
                license = "CC BY 4.0 - \"Bulk Carrier - TFCC HERITAGE\" by Gwenael_Herve (https://sketchfab.com/3d-models/e78b4f1fdc094a63bb7d6c24d8e7e7dd); x2.53 (75 m -> 190 m), rotated (bow +X), recentred",
                pitch = -90f, scale = 2.53f, yaw = -90f, recenter = true },
            new HullSpec { id = "lng_carrier", display = "LNG carrier 280 m (CC BY)", category = "lng_carrier", file = "lng_ship_artblender.glb",
                dir = "SketchfabCCBY/ships", sha = "9f38a2e69d5a087b5d6fefe50d86a9db324476fc25594672d4f3c252eff8e117",
                url = "https://sketchfab.com/3d-models/fa335b96450d4344863bbf5d912a0288", hullNode = RenderMinReference, immersion = 8.0f,
                paint = new[] { "nohull:Material.005" },   // one white material covers the cargo tanks and hull parts; tanks must not take the hull colour
                license = "CC BY 4.0 - \"LNG Ship\" by ArtBlender (https://sketchfab.com/3d-models/fa335b96450d4344863bbf5d912a0288); x6.43 (43.5 units -> 280 m), rotated (bow +X), recentred",
                pitch = -90f, scale = 6.43f, yaw = 90f, recenter = true },
            new HullSpec { id = "trawler", display = "Fishing trawler 28 m (CC BY)", category = "fishing_trawler", file = "fishing_trawler_zoherrose7.glb",
                dir = "SketchfabCCBY/ships", sha = "81412c74b1a571b5558bedef288942888705c74adb868fd9a3360652953b9d08",
                url = "https://sketchfab.com/3d-models/fd28c35b76754c9386b32aa281a0e238", hullNode = RenderMinReference, immersion = 2.0f,
                license = "CC BY 4.0 - \"Salt-Beaten Fishing Trawler\" by zoherrose7 (https://sketchfab.com/3d-models/fd28c35b76754c9386b32aa281a0e238); x37.8 (0.74 units -> 28 m), rotated (bow +X), recentred",
                pitch = -90f, scale = 37.8f, yaw = 90f, recenter = true },
            new HullSpec { id = "fishing_vessel", display = "Fishing vessel 12 m (CC BY)", category = "fishing_boat", file = "fishing_ship_crazyyuan.glb",
                dir = "SketchfabCCBY/ships", sha = "97cd675d9fbf51a5d185f9bae6a72850873422e319a17637b76feb305b1a6c69",
                url = "https://sketchfab.com/3d-models/f791868a8b5f4996a49a3c8cd99336f6", hullNode = RenderMinReference, immersion = 0.9f,
                license = "CC BY 4.0 - \"Fishing Ship game-ready asset\" by crazyyuan (https://sketchfab.com/3d-models/f791868a8b5f4996a49a3c8cd99336f6); x0.01415 (848 cm -> 12 m), rotated (bow +X), recentred",
                drop = new[] { "Interior", "Light" }, pitch = -90f, scale = 0.01415f, yaw = 90f, recenter = true },
            new HullSpec { id = "inshore_fishing_boat", display = "Inshore fishing boat 9 m (CC BY)", category = "fishing_boat", file = "fisher_boat_batuhan13.glb",
                dir = "SketchfabCCBY/ships", sha = "2d6a29637424b6aa4b105c6aa609d51a66c6549857353001f6e7afe520e4ff31",
                url = "https://sketchfab.com/3d-models/385b039e95754dca8d8c045ed0c515cf", hullNode = RenderMinReference, immersion = 0.5f,
                license = "CC BY 4.0 - \"Fisher boat\" by Batuhan13 (https://sketchfab.com/3d-models/385b039e95754dca8d8c045ed0c515cf); x0.0196 (460 cm -> 9 m), rotated (bow +X), recentred",
                pitch = -90f, scale = 0.0196f, yaw = 90f, recenter = true },
            new HullSpec { id = "sar_boat", display = "Search and rescue boat 20 m (CC BY)", category = "rescue_boat", file = "sar_boat_20m_nutical3d.glb",
                dir = "SketchfabCCBY/ships", sha = "70a90d2b219f502b37bcca9f19570d9b101952e2178d54d468529cbeb2e25d39",
                url = "https://sketchfab.com/3d-models/6d53e735521f44af82142e6292b2f340", hullNode = RenderMinReference, immersion = 1.0f,
                license = "CC BY 4.0 - \"20m search and rescue boat\" by Nutical3D (https://sketchfab.com/3d-models/6d53e735521f44af82142e6292b2f340); x1.87 (10.7 units -> 20 m), rotated (bow +X), recentred",
                pitch = -90f, scale = 1.87f, yaw = 90f, recenter = true },
            new HullSpec { id = "pilot_boat", display = "Pilot boat 18 m (CC BY)", category = "pilot_boat", file = "pilot_boat_agt14032013.glb",
                dir = "SketchfabCCBY/ships", sha = "16353395c3e11b153fb969c90e7fecea6359d550ca7902fb7fdcda68083f0d63",
                url = "https://sketchfab.com/3d-models/6ea031237b6b4382ab362790f8af4e66", hullNode = RenderMinReference, immersion = 1.1f,
                license = "CC BY 4.0 - \"Pilot II\" by agt14032013 (https://sketchfab.com/3d-models/6ea031237b6b4382ab362790f8af4e66); x0.973 (18.5 m -> 18 m), rotated (bow +X), recentred",
                pitch = -90f, scale = 0.973f, yaw = 90f, recenter = true },
            new HullSpec { id = "patrol_boat", display = "Coast guard patrol boat 23 m (CC BY)", category = "patrol_boat", file = "coast_guard_05_agt14032013.glb",
                dir = "SketchfabCCBY/ships", sha = "e8b7472b52fa1e0ff59c0486ed6faba58714b2db7fa938d911f66dbd74e329ad",
                url = "https://sketchfab.com/3d-models/7e29c40c92774a5cb534613bd5dfb3c7", hullNode = RenderMinReference, immersion = 1.3f,
                license = "CC BY 4.0 - \"COAST GUARD 05\" by agt14032013 (https://sketchfab.com/3d-models/7e29c40c92774a5cb534613bd5dfb3c7); x1.6 (14.4 m -> 23 m), rotated (bow +X), recentred",
                pitch = -90f, scale = 1.6f, yaw = 90f, recenter = true },
            new HullSpec { id = "police_boat", display = "Police boat 12 m (CC BY)", category = "patrol_boat", file = "police_boat_agt14032013.glb",
                dir = "SketchfabCCBY/ships", sha = "5906cd6e02ac450aa07a899aaa5d3c14b14f62319c02374f60968464408a8361",
                url = "https://sketchfab.com/3d-models/959e50ef12a546d8a203182c4ccef17a", hullNode = RenderMinReference, immersion = 0.6f,
                license = "CC BY 4.0 - \"Police Boat\" by agt14032013 (https://sketchfab.com/3d-models/959e50ef12a546d8a203182c4ccef17a); x1.22 (9.8 m -> 12 m), rotated (bow +X), recentred",
                pitch = -90f, scale = 1.22f, yaw = 0f, recenter = true },
            new HullSpec { id = "speedboat", display = "Speedboat 7 m (CC BY)", category = "speedboat", file = "speedboat_vrpspu.glb",
                dir = "SketchfabCCBY/ships", sha = "5a86b7e5cbd4081f136cc289504cc2622a084994b5af274a086d3e2fddff7ddf",
                url = "https://sketchfab.com/3d-models/9916bd4433024202ab84e43426fc2139", hullNode = RenderMinReference, immersion = 0.35f,
                license = "CC BY 4.0 - \"Speedboat\" by vrpspu (https://sketchfab.com/3d-models/9916bd4433024202ab84e43426fc2139); x0.65 (10.7 units -> 7 m); ground plane, sky sphere and backdrop plane removed, rotated (bow +X), recentred",
                drop = new[] { "Plane001", "Sphere001", "flag", "~1" }, scale = 0.65f, yaw = 180f, recenter = true },
            new HullSpec { id = "motor_yacht", display = "Motor yacht 40 m (CC BY)", category = "motor_yacht", file = "yacht_ii_agt14032013.glb",
                dir = "SketchfabCCBY/ships", sha = "c161d183afaa20f64857ffb60086292053fd2deb5f249733630549adca87f152",
                url = "https://sketchfab.com/3d-models/dadc775d1d044aee9f6744fa7b47648e", hullNode = RenderMinReference, immersion = 1.6f,
                license = "CC BY 4.0 - \"Yacht II\" by agt14032013 (https://sketchfab.com/3d-models/dadc775d1d044aee9f6744fa7b47648e); x0.38 (105 units -> 40 m), rotated (bow +X), recentred",
                pitch = -90f, scale = 0.38f, yaw = 90f, recenter = true },
        };

        static readonly (string key, Color color, float smooth, float metal)[] MaterialSpecs =
        {
            ("asphalt", new Color(0.18f, 0.18f, 0.19f), 0.25f, 0f),
            ("concrete", new Color(0.55f, 0.54f, 0.51f), 0.2f, 0f),
            ("breakwater", new Color(0.45f, 0.44f, 0.42f), 0.15f, 0f),
            ("fender", new Color(0.05f, 0.05f, 0.05f), 0.3f, 0f),
            ("warehouse_wall", new Color(0.62f, 0.66f, 0.70f), 0.35f, 0.2f),
            ("warehouse_roof", new Color(0.30f, 0.33f, 0.38f), 0.4f, 0.4f),
            ("container_red", new Color(0.55f, 0.10f, 0.08f), 0.35f, 0.3f),
            ("container_blue", new Color(0.08f, 0.20f, 0.45f), 0.35f, 0.3f),
            ("container_green", new Color(0.12f, 0.35f, 0.18f), 0.35f, 0.3f),
            ("container_grey", new Color(0.50f, 0.50f, 0.50f), 0.35f, 0.3f),
            ("crane", new Color(0.80f, 0.55f, 0.10f), 0.4f, 0.5f),
            ("steel_dark", new Color(0.15f, 0.15f, 0.16f), 0.45f, 0.7f),
            ("buoy_red", new Color(0.75f, 0.06f, 0.05f), 0.5f, 0f),
            ("buoy_green", new Color(0.05f, 0.45f, 0.12f), 0.5f, 0f),
        };

        [MenuItem("PortCCTV/Bootstrap project (idempotent)")]
        public static void Run()
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                throw new InvalidOperationException("Bootstrap needs a graphics device for texture analysis. Remove -nographics before regenerating assets.");
            Log("Bootstrap start, project " + Path.GetFullPath("."));
            EnsureFolder(SettingsDir); EnsureFolder(MaterialsDir); EnsureFolder(VesselsDir); EnsureFolder(ResourcesDir); EnsureFolder(Root + "/Scenes");
            EnsureHdrp();
            EnsurePlayerSettings();
            TextureDiet.ResetCache();
            AssetDatabase.DeleteAsset(Root + "/Materials/Derived"); // regenerated below; avoids stale copies
            var theme = EnsureTheme();
            EnsureVessels();
            EnsureScene();
            AssetDatabase.SaveAssets();
            Log("Bootstrap done. Theme materials=" + theme.materials.Count);
        }

        public static void RunBatch()
        {
            try { Run(); EditorApplication.Exit(0); }
            catch (Exception ex) { Debug.LogError("[Bootstrap] FAILED: " + ex); EditorApplication.Exit(1); }
        }

        static void Log(string s) => Debug.Log("[Bootstrap] " + s);

        static void EnsureFolder(string path)
        {
            var parts = path.Split('/');
            var cur = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                var next = cur + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(cur, parts[i]);
                cur = next;
            }
        }

        // ---------------- HDRP ----------------
        static void EnsureHdrp()
        {
            var path = SettingsDir + "/PortCCTV_HDRP.asset";
            var asset = AssetDatabase.LoadAssetAtPath<HDRenderPipelineAsset>(path);
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<HDRenderPipelineAsset>();
                AssetDatabase.CreateAsset(asset, path);
                Log("Created " + path);
            }
            var so = new SerializedObject(asset);
            SetBool(so, "m_RenderPipelineSettings.supportWater", true);
            SetBool(so, "m_RenderPipelineSettings.supportVolumetrics", true);
            SetBool(so, "m_RenderPipelineSettings.supportDecals", true);
            SetBool(so, "m_RenderPipelineSettings.supportDecalLayers", true);          // decal angle fade (snow only on upward faces)
            var dd = so.FindProperty("m_RenderPipelineSettings.decalSettings.drawDistance");
            if (dd != null) dd.intValue = 20000; else Log("WARNING: decalSettings.drawDistance not found");
            // night: hundreds of lamps and navigation lights (many close together along quays)
            foreach (var (k, v) in new[] { ("m_RenderPipelineSettings.lightLoopSettings.maxPunctualLightsOnScreen", 1024), ("m_RenderPipelineSettings.lightLoopSettings.maxLightsPerClusterCell", 24) })
            {
                var lp = so.FindProperty(k);
                if (lp != null) lp.intValue = v; else Log("WARNING: " + k + " not found");
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);

            GraphicsSettings.defaultRenderPipeline = asset;
            int cur = QualitySettings.GetQualityLevel();
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = null; // use the default pipeline everywhere
            }
            QualitySettings.SetQualityLevel(cur, false);

            var gsType = typeof(HDRenderPipelineAsset).Assembly.GetType("UnityEngine.Rendering.HighDefinition.HDRenderPipelineGlobalSettings");
            var ensure = gsType?.GetMethod("Ensure", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            if (ensure != null)
            {
                var gs = ensure.Invoke(null, new object[] { true });
                Log("HDRP global settings: " + (gs != null ? AssetDatabase.GetAssetPath((UnityEngine.Object)gs) : "null"));
            }
            else Log("WARNING: HDRenderPipelineGlobalSettings.Ensure not found");
            Log("HDRP asset set: supportWater=" + asset.currentPlatformRenderPipelineSettings.supportWater);
        }

        static void SetBool(SerializedObject so, string prop, bool v)
        {
            var p = so.FindProperty(prop);
            if (p == null) throw new InvalidOperationException("HDRP asset property not found: " + prop);
            p.boolValue = v;
        }

        static void EnsurePlayerSettings()
        {
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.companyName = "DronePerception";
            PlayerSettings.productName = "PortCCTVSimulator";
            PlayerSettings.bundleVersion = PortCCTV.CaptureService.AppVersion;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 1600;
            PlayerSettings.defaultScreenHeight = 900;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.runInBackground = true;
            PlayerSettings.visibleInBackground = true;
            PlayerSettings.bakeCollisionMeshes = true;
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64, new[] { UnityEngine.Rendering.GraphicsDeviceType.Direct3D11 });
        }

        // ---------------- Materials ----------------
        static HarborTheme EnsureTheme()
        {
            var lit = Shader.Find("HDRP/Lit") ?? throw new InvalidOperationException("HDRP/Lit shader not found");
            var themePath = ResourcesDir + "/HarborTheme.asset";
            var theme = AssetDatabase.LoadAssetAtPath<HarborTheme>(themePath);
            if (theme == null) { theme = ScriptableObject.CreateInstance<HarborTheme>(); AssetDatabase.CreateAsset(theme, themePath); }
            theme.materials.Clear();
            foreach (var (key, color, smooth, metal) in MaterialSpecs)
            {
                var mp = $"{MaterialsDir}/M_{key}.mat";
                var m = AssetDatabase.LoadAssetAtPath<Material>(mp);
                if (m == null) { m = new Material(lit); AssetDatabase.CreateAsset(m, mp); }
                m.shader = lit;
                m.SetColor("_BaseColor", color);
                m.SetFloat("_Smoothness", smooth);
                m.SetFloat("_Metallic", metal);
                HDMaterial.ValidateMaterial(m);
                EditorUtility.SetDirty(m);
                theme.materials.Add(new HarborTheme.Entry { key = key, material = m });
            }
            WeatherAssets.Build(theme);
            ProceduralAssets.Run(theme);
            PhotoAssets.ApplyMaterials(theme);
            PhotoAssets.BuildSkyLibrary();
            EditorUtility.SetDirty(theme);
            return theme;
        }

        // ---------------- Vessels ----------------
        /// <summary>
        /// glTF glazing materials in these hulls have base alpha ~0 with alpha test; the Editor and the player resolved them
        /// differently (opaque wall colour vs. see-through), while the collider mask treats them as solid. Prefab copies get one
        /// opaque dark-glass material instead. Source GLBs are untouched.
        /// </summary>
        static string ReplaceTransparentMaterials(GameObject root, Material glazing)
        {
            var replaced = new SortedSet<string>();
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    if (m == null) continue;
                    float alpha = 1f;
                    if (m.HasProperty("baseColorFactor")) alpha = m.GetColor("baseColorFactor").a;
                    else if (m.HasProperty("_BaseColor")) alpha = m.GetColor("_BaseColor").a;
                    if (alpha < 0.5f) { replaced.Add($"{m.name}(a={alpha:F3})"); mats[i] = glazing; changed = true; }
                }
                if (changed) r.sharedMaterials = mats;
            }
            return replaced.Count == 0 ? "none" : "replaced with M_vessel_glazing_opaque: " + string.Join(", ", replaced);
        }

        static void EnsureVessels()
        {
            var lit = Shader.Find("HDRP/Lit");
            var gp = MaterialsDir + "/M_vessel_glazing_opaque.mat";
            var glazing = AssetDatabase.LoadAssetAtPath<Material>(gp);
            if (glazing == null) { glazing = new Material(lit); AssetDatabase.CreateAsset(glazing, gp); }
            glazing.SetColor("_BaseColor", new Color(0.06f, 0.08f, 0.10f, 1f));
            glazing.SetFloat("_Smoothness", 0.9f);
            glazing.SetFloat("_Metallic", 0f);
            HDMaterial.ValidateMaterial(glazing);
            EditorUtility.SetDirty(glazing);
            var catPath = ResourcesDir + "/VesselCatalog.asset";
            var cat = AssetDatabase.LoadAssetAtPath<VesselCatalog>(catPath);
            if (cat == null) { cat = ScriptableObject.CreateInstance<VesselCatalog>(); AssetDatabase.CreateAsset(cat, catPath); }
            cat.entries.Clear();
            var theme = AssetDatabase.LoadAssetAtPath<HarborTheme>(ResourcesDir + "/HarborTheme.asset");
            foreach (var h in Hulls)
            {
                string glbPath, sha;
                var root = new GameObject("vessel_" + h.id);
                GameObject inst;
                if (!string.IsNullOrEmpty(h.procedural))
                {
                    glbPath = "procedural:Editor/ProceduralVessels.cs#" + h.procedural;
                    sha = PortCCTV.ScenarioIO.Sha256Hex(File.ReadAllBytes(Root + "/Editor/ProceduralVessels.cs"));
                    inst = h.procedural switch
                    {
                        "tug" => ProceduralVessels.BuildTug(),
                        "container_ship" => ProceduralVessels.BuildContainerShip(theme),
                        "kfish" => ProceduralVessels.BuildKoreanFishingBoat(),
                        _ => ProceduralVessels.BuildBarge(theme != null ? theme.Get("sand") : null),
                    };
                }
                else
                {
                    glbPath = $"{Root}/ThirdParty/{(string.IsNullOrEmpty(h.dir) ? "Innerscene" : h.dir)}/{h.file}";
                    var bytes = File.ReadAllBytes(glbPath);
                    sha = PortCCTV.ScenarioIO.Sha256Hex(bytes);
                    if (sha != h.sha) throw new InvalidOperationException($"SHA mismatch for {glbPath}: {sha}");
                    // Re-import now that HDRP is the active pipeline so glTFast generates HDRP materials.
                    AssetDatabase.ImportAsset(glbPath, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
                    var glb = AssetDatabase.LoadAssetAtPath<GameObject>(glbPath) ?? throw new InvalidOperationException("GLB not imported: " + glbPath);
                    inst = (GameObject)PrefabUtility.InstantiatePrefab(glb) ?? UnityEngine.Object.Instantiate(glb);
                }
                inst.transform.SetParent(root.transform, false);
                // drop: "frag" removes nodes whose name contains frag (with children); "~name" removes only the mesh of the node named
                // exactly name (its children, e.g. the hull under a backdrop-plane parent, stay)
                if (h.drop != null)
                    foreach (var tr in inst.GetComponentsInChildren<Transform>(true).ToArray())
                    {
                        if (tr == null || tr == inst.transform) continue;
                        if (h.drop.Any(d => d.StartsWith("~") && tr.name == d.Substring(1)))
                        { foreach (var c in tr.GetComponents<Renderer>()) UnityEngine.Object.DestroyImmediate(c); foreach (var c in tr.GetComponents<MeshFilter>()) UnityEngine.Object.DestroyImmediate(c); }
                        else if (h.drop.Any(d => !d.StartsWith("~") && tr.name.IndexOf(d, StringComparison.OrdinalIgnoreCase) >= 0))
                            UnityEngine.Object.DestroyImmediate(tr.gameObject);
                    }
                inst.transform.localPosition = Vector3.zero;
                inst.transform.localRotation = Quaternion.Euler(h.pitch, h.yaw, 0);
                if (h.scale > 0) inst.transform.localScale = inst.transform.localScale * h.scale;
                if (h.recenter)
                {
                    var rs = inst.GetComponentsInChildren<Renderer>(true);
                    var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
                    inst.transform.localPosition = new Vector3(-b.center.x, -b.min.y, -b.center.z);
                }

                var geom = ExtractGeometry(root, h.hullNode, out int meshCount);
                var glassNote = ReplaceTransparentMaterials(root, glazing);
                glassNote += "; " + TextureDiet.Apply(root, h.id);
                var paintSlots = VesselPaintAnalysis.Analyze(root, geom, h.id, h.immersion, h.paint, out var paintNote);
                foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (mf.sharedMesh == null) continue;
                    var mc = mf.gameObject.AddComponent<MeshCollider>();
                    mc.sharedMesh = mf.sharedMesh;
                    mc.convex = false;
                }

                var prefabPath = $"{VesselsDir}/vessel_{h.id}.prefab";
                var prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                UnityEngine.Object.DestroyImmediate(root);

                var geomPath = $"{VesselsDir}/vessel_{h.id}_geometry.asset";
                var oldGeom = AssetDatabase.LoadAssetAtPath<VesselGeometry>(geomPath);
                if (oldGeom != null) { EditorUtility.CopySerialized(geom, oldGeom); geom = oldGeom; }
                else AssetDatabase.CreateAsset(geom, geomPath);
                EditorUtility.SetDirty(geom);

                cat.entries.Add(new VesselAssetEntry
                {
                    asset_id = h.id, display_name = h.display, category = h.category, prefab = prefab, geometry = geom,
                    source_file = glbPath, source_sha256 = sha, license = string.IsNullOrEmpty(h.license) ? "CC0 (Innerscene, source-stated)" : h.license, source_url = h.url,
                    default_immersion_m = h.immersion, material_note = glassNote, paint_slots = paintSlots, paint_note = paintNote,
                });
                Log($"Vessel {h.id} paint: {paintNote}");
                Log($"Vessel {h.id}: meshes={meshCount} label_vertices={geom.vertices_root_space.Length} " +
                    $"bounds=({geom.bounds_min} .. {geom.bounds_max}) render_min_y={geom.render_min_y:F4} hull_ref_min_y={geom.hull_reference_min_y:F4} found={geom.hull_reference_found} materials: {glassNote}");
            }
            EditorUtility.SetDirty(cat);
        }

        static VesselGeometry ExtractGeometry(GameObject root, string hullNode, out int meshCount)
        {
            var geom = ScriptableObject.CreateInstance<VesselGeometry>();
            var w2l = root.transform.worldToLocalMatrix;
            var unique = new HashSet<Vector3>();
            var list = new List<Vector3>();
            int total = 0;
            meshCount = 0;
            var bmin = Vector3.positiveInfinity; var bmax = Vector3.negativeInfinity;
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                meshCount++;
                var m = w2l * mf.transform.localToWorldMatrix;
                foreach (var v in mf.sharedMesh.vertices)
                {
                    total++;
                    var p = m.MultiplyPoint3x4(v);
                    if (unique.Add(p)) list.Add(p);
                    bmin = Vector3.Min(bmin, p); bmax = Vector3.Max(bmax, p);
                }
            }
            if (list.Count == 0) throw new InvalidOperationException("No mesh vertices in " + root.name);
            geom.vertices_root_space = list.ToArray();
            geom.source_vertex_count = total;
            geom.bounds_min = bmin; geom.bounds_max = bmax;
            geom.render_min_y = bmin.y;
            geom.hull_reference_node = hullNode;

            if (hullNode == RenderMinReference)
            {
                // Large ship without named parts: the keel is the lowest point, so the render minimum is the hull reference.
                geom.hull_reference_found = true;
                geom.hull_reference_min_y = geom.render_min_y;
                geom.hull_reference_node = "(render minimum: keel is the lowest point)";
                return geom;
            }
            var nodes = root.GetComponentsInChildren<Transform>(true).Where(t => t.name == hullNode).ToList();
            float hy = float.PositiveInfinity;
            if (nodes.Count == 1)
            {
                foreach (var mf in nodes[0].GetComponentsInChildren<MeshFilter>(true))
                {
                    if (mf.sharedMesh == null) continue;
                    var m = w2l * mf.transform.localToWorldMatrix;
                    foreach (var v in mf.sharedMesh.vertices) hy = Mathf.Min(hy, m.MultiplyPoint3x4(v).y);
                }
            }
            geom.hull_reference_found = nodes.Count == 1 && !float.IsInfinity(hy);
            geom.hull_reference_min_y = geom.hull_reference_found ? hy : geom.render_min_y;
            if (nodes.Count != 1) Debug.LogWarning($"[Bootstrap] hull node '{hullNode}' matches {nodes.Count} transforms in {root.name}");
            return geom;
        }

        // ---------------- Scene ----------------
        static void EnsureScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var app = new GameObject("PortCCTVApp");
            app.AddComponent<SimulatorApp>();
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            Log("Scene saved " + ScenePath);
        }
    }
}
