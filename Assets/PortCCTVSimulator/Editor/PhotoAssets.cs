using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace PortCCTV.EditorTools
{
    /// <summary>
    /// Wires the downloaded Poly Haven CC0 photo textures and HDRI skies (ThirdParty/PolyHavenCC0) into the project:
    /// photo-textured triplanar materials (overriding the procedural ones) and a SkyLibrary with measured sun directions.
    /// </summary>
    public static class PhotoAssets
    {
        const string Root = "Assets/PortCCTVSimulator";
        const string TexRoot = Root + "/ThirdParty/PolyHavenCC0/textures";
        const string HdriRoot = Root + "/ThirdParty/PolyHavenCC0/hdri";
        const string GenDir = Root + "/Textures/Generated";

        static Texture2D Tex(string id, string kind, bool normal)
        {
            var path = $"{TexRoot}/{id}/{id}_{kind}_2k.jpg";
            var imp = (TextureImporter)AssetImporter.GetAtPath(path) ?? throw new InvalidOperationException("missing " + path);
            imp.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            imp.sRGBTexture = !normal;
            imp.wrapMode = TextureWrapMode.Repeat;
            imp.anisoLevel = 8;
            imp.maxTextureSize = 2048;
            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>A texture outside the Poly Haven folder (e.g. extracted from a CC BY pack), imported like Tex().</summary>
        static Texture2D TexAt(string path, bool normal)
        {
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var imp = (TextureImporter)AssetImporter.GetAtPath(path) ?? throw new InvalidOperationException("missing " + path);
            imp.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            imp.sRGBTexture = !normal; imp.wrapMode = TextureWrapMode.Repeat; imp.anisoLevel = 8; imp.maxTextureSize = 2048;
            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static Texture2D ImportPng(string path, bool normal)
        {
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var imp = (TextureImporter)AssetImporter.GetAtPath(path) ?? throw new InvalidOperationException("missing " + path);
            imp.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            imp.sRGBTexture = !normal;
            imp.wrapMode = TextureWrapMode.Repeat;
            imp.anisoLevel = 8;
            imp.maxTextureSize = 2048;
            imp.mipmapEnabled = true;
            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>Greyscale copy of a photo albedo, normalised to mean 0.85, so it can be tinted per material colour.</summary>
        static Texture2D GreyAlbedo(string id, string outName)
        {
            var src = $"{TexRoot}/{id}/{id}_diff_2k.jpg";
            var t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            t.LoadImage(File.ReadAllBytes(src));
            var px = t.GetPixels();
            float mean = px.Average(p => p.grayscale);
            for (int i = 0; i < px.Length; i++) { float g = Mathf.Clamp01(px[i].grayscale / mean * 0.85f); px[i] = new Color(g, g, g, 1); }
            t.SetPixels(px);
            var path = $"{GenDir}/{outName}.png";
            File.WriteAllBytes(path, t.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(t);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.wrapMode = TextureWrapMode.Repeat; imp.anisoLevel = 8; imp.maxTextureSize = 2048; imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        public static void ApplyMaterials(HarborTheme theme)
        {
            void Set(string key, Material m)
            {
                theme.materials.RemoveAll(e => e.key == key);
                theme.materials.Add(new HarborTheme.Entry { key = key, material = m });
            }
            var concA = Tex("concrete_floor_worn_001", "diff", false); var concN = Tex("concrete_floor_worn_001", "nor_gl", true);
            var mossA = Tex("concrete_moss", "diff", false); var mossN = Tex("concrete_moss", "nor_gl", true);
            var layA = Tex("concrete_layers_02", "diff", false); var layN = Tex("concrete_layers_02", "nor_gl", true);
            var asphA = Tex("asphalt_02", "diff", false); var asphN = Tex("asphalt_02", "nor_gl", true);
            var contN = Tex("container_side", "nor_gl", true); Tex("container_side", "diff", false);
            var contG = GreyAlbedo("container_side", "T_container_side_grey");
            var corrN = Tex("corrugated_iron_02", "nor_gl", true); Tex("corrugated_iron_02", "diff", false);
            var corrG = GreyAlbedo("corrugated_iron_02", "T_corrugated_iron_grey");
            var plateN = Tex("metal_plate", "nor_gl", true); Tex("metal_plate", "diff", false);
            var plateG = GreyAlbedo("metal_plate", "T_metal_plate_grey");
            var sandA = Tex("coast_sand_01", "diff", false); var sandN = Tex("coast_sand_01", "nor_gl", true);
            var grassA = Tex("sparse_grass", "diff", false); var grassN = Tex("sparse_grass", "nor_gl", true);

            var mk = (Func<string, Color, float, float, Texture2D, Texture2D, float, Material>)((k, c, s, m, a, n, tile) => ProceduralAssets.MakeLit(k, c, s, m, a, n, tile));
            Set("concrete", mk("concrete", new Color(0.92f, 0.91f, 0.88f), 0.15f, 0, concA, concN, 4f));
            Set("roof_concrete", mk("roof_concrete", new Color(0.85f, 0.85f, 0.83f), 0.15f, 0, concA, concN, 5f));
            Set("quay_wet", mk("quay_wet", new Color(0.55f, 0.6f, 0.5f), 0.5f, 0, mossA, mossN, 3f));
            Set("breakwater", mk("breakwater", new Color(0.85f, 0.85f, 0.83f), 0.15f, 0, layA, layN, 4f));
            Set("asphalt", mk("asphalt", new Color(0.85f, 0.85f, 0.85f), 0.2f, 0, asphA, asphN, 5f));
            Set("sand", mk("sand", new Color(1, 1, 1), 0.15f, 0, sandA, sandN, 4f));
            // Paved surfaces, 2026-11-09: large aprons / yards / car parks use the Poly Haven aerial asphalt scan (one 2k tile
            // covers 30 x 30 m: cracks, patches and wear read at CCTV distance); streets use the asphalt of "Modular Road Pack
            // (FREE) + 4K Textures" (intfloat, CC BY; ambientCG-based) and sidewalks its paving
            // (ThirdParty/SketchfabCCBY/road2/SOURCE_road2.md).
            var aaA = Tex("aerial_asphalt_01", "diff", false); var aaN = Tex("aerial_asphalt_01", "nor_gl", true);
            Set("asphalt", mk("asphalt", new Color(0.92f, 0.92f, 0.92f), 0.18f, 0, aaA, aaN, 30f));
            const string RT = Root + "/ThirdParty/SketchfabCCBY/road2/tex";
            var rdA = TexAt($"{RT}/road_pack_road_diff_2k.jpg", false); var rdN = TexAt($"{RT}/road_pack_road_nor_gl_2k.jpg", true);
            Set("road_asphalt", mk("road_asphalt", new Color(0.78f, 0.78f, 0.79f), 0.22f, 0, rdA, rdN, 6f));
            var swA = TexAt($"{RT}/road_pack_sidewalk_diff_2k.jpg", false); var swN = TexAt($"{RT}/road_pack_sidewalk_nor_gl_2k.jpg", true);
            Set("sidewalk_paving", mk("sidewalk_paving", new Color(0.95f, 0.94f, 0.92f), 0.15f, 0, swA, swN, 3f));
            // land: aerial scan of grass with rocks (Poly Haven aerial_grass_rock), slight green tint
            var aerA = Tex("aerial_grass_rock", "diff", false); var aerN = Tex("aerial_grass_rock", "nor_gl", true);
            var meaA = ImportPng("Assets/PortCCTVSimulator/Textures/Canopy/T_meadow_albedo.png", false);
            var meaN = ImportPng("Assets/PortCCTVSimulator/Textures/Canopy/T_meadow_normal.png", true);
            Set("grass", mk("grass", new Color(0.62f, 0.72f, 0.54f), 0.1f, 0, meaA, meaN, 120f));   // duller than the scan: coastal scrub, not a lawn
            // hills/islands: synthesised forest canopy (Tools/make_canopy_texture.py), tile 600 m => crowns ~6-12 m
            var canA = ImportPng("Assets/PortCCTVSimulator/Textures/Canopy/T_canopy_albedo.png", false);
            var canN = ImportPng("Assets/PortCCTVSimulator/Textures/Canopy/T_canopy_normal.png", true);
            Set("forest", ProceduralAssets.MakeLit("forest", new Color(0.78f, 0.84f, 0.72f), 0.08f, 0, canA, canN, 300f, 0.45f));
            Set("tree_leaves", mk("tree_leaves", new Color(1f, 1f, 1f), 0.08f, 0, canA, canN, 40f));
            // Tinted families keep their colours but get photo detail.
            foreach (var e in theme.materials.ToList())
            {
                var m = e.material;
                if (m == null) continue;
                var c = m.GetColor("_BaseColor");
                float sm = m.GetFloat("_Smoothness"), me = m.GetFloat("_Metallic");
                if (e.key.StartsWith("container_")) Set(e.key, mk(e.key, c, 0.35f, 0.3f, contG, contN, 2.6f));
                else if (e.key == "warehouse_wall" || e.key == "crane_house") Set(e.key, mk(e.key, c, 0.35f, 0.3f, corrG, corrN, 3f));
                else if (e.key.StartsWith("crane_") || e.key == "steel_dark" || e.key == "steel_galv" || e.key == "bollard")
                    Set(e.key, mk(e.key, c, sm, me, plateG, plateN, 2f));
            }
            EditorUtility.SetDirty(theme);
        }

        // ------------------------------------------------------------------ HDRI skies
        public static void BuildSkyLibrary()
        {
            var libPath = Root + "/Resources/PortCCTV/SkyLibrary.asset";
            var lib = AssetDatabase.LoadAssetAtPath<SkyLibrary>(libPath);
            if (lib == null) { lib = ScriptableObject.CreateInstance<SkyLibrary>(); AssetDatabase.CreateAsset(lib, libPath); }
            lib.entries.Clear();
            foreach (var path in Directory.GetFiles(HdriRoot, "*.hdr").Select(p => p.Replace('\\', '/')).OrderBy(p => p))
            {
                var key = Path.GetFileNameWithoutExtension(path).Replace("_4k", "");
                var imp = (TextureImporter)AssetImporter.GetAtPath(path);
                // Pass 1: small, readable, uncompressed copy to measure the sun and the upper-hemisphere lux.
                imp.textureShape = TextureImporterShape.TextureCube;
                imp.generateCubemap = TextureImporterGenerateCubemap.Cylindrical;
                imp.sRGBTexture = false;
                imp.isReadable = true;
                imp.textureCompression = TextureImporterCompression.Uncompressed;
                imp.maxTextureSize = 256;
                imp.mipmapEnabled = false;
                imp.SaveAndReimport();
                var cube = AssetDatabase.LoadAssetAtPath<Cubemap>(path);
                Measure(cube, out var sunDir, out float lux, out bool flipped);
                float el = Mathf.Asin(Mathf.Clamp(sunDir.y, -1, 1)) * Mathf.Rad2Deg;
                float az = Mathf.Repeat(Mathf.Atan2(sunDir.x, sunDir.z) * Mathf.Rad2Deg, 360f);
                // Pass 2: final runtime import (compressed BC6H, 2048 faces, not readable).
                imp.isReadable = false;
                imp.textureCompression = TextureImporterCompression.CompressedHQ;
                imp.maxTextureSize = 2048;
                imp.mipmapEnabled = true;
                imp.SaveAndReimport();
                cube = AssetDatabase.LoadAssetAtPath<Cubemap>(path);
                lib.entries.Add(new SkyLibrary.Entry
                {
                    key = key, cubemap = cube, sun_azimuth_cube_deg = az, sun_elevation_deg = el, upper_hemisphere_lux = lux,
                    source_file = path, source_sha256 = ScenarioIO.Sha256Hex(File.ReadAllBytes(path)),
                });
                Debug.Log($"[Bootstrap] HDRI {key}: sun az(cube)={az:F1} el={el:F1} (vertical flip={flipped}) upperHemisphereLux={lux:F1}");
            }
            EditorUtility.SetDirty(lib);
        }

        /// <summary>Brightest texel direction and cosine-weighted upper-hemisphere integral (HDRI units).</summary>
        static void Measure(Cubemap cube, out Vector3 sun, out float lux, out bool flipped)
        {
            int n = cube.width;
            Vector3 best = Vector3.up; float bestL = -1;
            double sumTop = 0, sumBot = 0;
            // Cube face direction (OpenGL/D3D layout) for face-local coords s,t in [-1,1] with t pointing down the image.
            Vector3 Dir(CubemapFace f, float s, float t) => f switch
            {
                CubemapFace.PositiveX => new Vector3(1, -t, -s),
                CubemapFace.NegativeX => new Vector3(-1, -t, s),
                CubemapFace.PositiveY => new Vector3(s, 1, t),
                CubemapFace.NegativeY => new Vector3(s, -1, -t),
                CubemapFace.PositiveZ => new Vector3(s, -t, 1),
                _ => new Vector3(-s, -t, -1),
            };
            Vector3 bestTopDown = Vector3.up, bestBottomUp = Vector3.up;
            foreach (CubemapFace f in new[] { CubemapFace.PositiveX, CubemapFace.NegativeX, CubemapFace.PositiveY, CubemapFace.NegativeY, CubemapFace.PositiveZ, CubemapFace.NegativeZ })
            {
                var px = cube.GetPixels(f);
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        var c = px[y * n + x];
                        float L = 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;
                        float s = 2f * (x + 0.5f) / n - 1f, t = 2f * (y + 0.5f) / n - 1f;
                        var dTop = Dir(f, s, t).normalized;          // row 0 = top of the face
                        var dBot = Dir(f, s, -t).normalized;         // row 0 = bottom of the face
                        float dOmega = 4f / (n * n) / Mathf.Pow(1 + s * s + t * t, 1.5f);
                        if (L > bestL) { bestL = L; bestTopDown = dTop; bestBottomUp = dBot; }
                        if (dTop.y > 0) sumTop += L * dTop.y * dOmega;
                        if (dBot.y > 0) sumBot += L * dBot.y * dOmega;
                    }
            }
            // The sun is above the horizon in every pure-sky HDRI used here: pick the row convention that agrees.
            flipped = bestTopDown.y < 0 && bestBottomUp.y > 0;
            sun = flipped ? bestBottomUp : bestTopDown;
            best = sun;
            lux = (float)(flipped ? sumBot : sumTop);
        }
    }
}
