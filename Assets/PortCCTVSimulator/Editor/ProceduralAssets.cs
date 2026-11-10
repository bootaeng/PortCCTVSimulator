using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace PortCCTV.EditorTools
{
    /// <summary>
    /// Generates the harbor look: tileable textures (deterministic), HDRP/Lit triplanar materials, low-poly rock and
    /// hill meshes and wrapper prefabs for the imported CC0 background models. Called from ProjectBootstrap.
    /// </summary>
    public static class ProceduralAssets
    {
        const string Root = "Assets/PortCCTVSimulator";
        const string TexDir = Root + "/Textures/Generated";
        const string MatDir = Root + "/Materials";
        const string MeshDir = Root + "/Meshes";
        const string PrefabDir = Root + "/HarborPrefabs";
        const string PH = Root + "/ThirdParty/PolyHaven";
        const string IS = Root + "/ThirdParty/InnersceneHarbor";
        const string PHM = Root + "/ThirdParty/PolyHavenCC0/models";
        const string SB = Root + "/ThirdParty/SketchfabCCBY/buildings";
        const string SP = Root + "/ThirdParty/SketchfabCCBY/props";

        // ------------------------------------------------------------ tileable noise
        static float Hash(int x, int y, int seed) => ProcNoise.Hash(x, y, seed);
        static float VNoise(float u, float v, int cells, int seed) => ProcNoise.VNoise(u, v, cells, seed);
        static float Fbm(float u, float v, int baseCells, int octaves, int seed) => ProcNoise.Fbm(u, v, baseCells, octaves, seed);

        delegate Color PixelFn(float u, float v);
        delegate float HeightFn(float u, float v);

        static Texture2D SaveTex(string name, int size, PixelFn f, bool linear)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false, linear);
            var px = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    px[y * size + x] = f((x + 0.5f) / size, (y + 0.5f) / size);
            t.SetPixels(px);
            var path = $"{TexDir}/{name}.png";
            File.WriteAllBytes(path, t.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(t);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.sRGBTexture = !linear;
            imp.wrapMode = TextureWrapMode.Repeat;
            imp.mipmapEnabled = true;
            imp.anisoLevel = 8;
            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static Texture2D SaveNormal(string name, int size, HeightFn hf, float strength)
        {
            var hgt = new float[size, size];
            for (int y = 0; y < size; y++) for (int x = 0; x < size; x++) hgt[x, y] = hf((x + 0.5f) / size, (y + 0.5f) / size);
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false, true);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = (hgt[(x + 1) % size, y] - hgt[(x - 1 + size) % size, y]) * strength;
                    float dy = (hgt[x, (y + 1) % size] - hgt[x, (y - 1 + size) % size]) * strength;
                    var n = new Vector3(-dx, -dy, 1).normalized;
                    t.SetPixel(x, y, new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f, 1));
                }
            var path = $"{TexDir}/{name}.png";
            File.WriteAllBytes(path, t.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(t);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.textureType = TextureImporterType.NormalMap;
            imp.wrapMode = TextureWrapMode.Repeat;
            imp.anisoLevel = 8;
            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static Color G(float v) => new Color(v, v, v, 1);

        public class TexSet { public Texture2D albedo, normal; }

        public static Dictionary<string, TexSet> GenerateTextures()
        {
            Directory.CreateDirectory(TexDir);
            var d = new Dictionary<string, TexSet>();
            // Concrete: mid grey, cloudy stains, fine pores. Albedo ~0.85-1.0 multiplier (tinted by _BaseColor).
            d["concrete"] = new TexSet
            {
                albedo = SaveTex("T_concrete_albedo", 512, (u, v) =>
                {
                    float c = 0.86f + 0.14f * Fbm(u, v, 4, 5, 11) - 0.08f * Mathf.Pow(Fbm(u, v, 2, 3, 12), 3) * 2f;
                    if (Hash((int)(u * 512), (int)(v * 512), 13) > 0.985f) c -= 0.12f;
                    return G(Mathf.Clamp01(c));
                }, false),
                normal = SaveNormal("T_concrete_normal", 512, (u, v) => Fbm(u, v, 16, 4, 14), 3f)
            };
            d["asphalt"] = new TexSet
            {
                albedo = SaveTex("T_asphalt_albedo", 512, (u, v) =>
                {
                    float c = 0.8f + 0.12f * Fbm(u, v, 3, 4, 21) + 0.25f * (Hash((int)(u * 512), (int)(v * 512), 22) - 0.5f);
                    return G(Mathf.Clamp01(c));
                }, false),
                normal = SaveNormal("T_asphalt_normal", 512, (u, v) => Hash((int)(u * 256), (int)(v * 256), 23), 1.2f)
            };
            // Corrugated container steel: vertical ridges (period 1/16 of the tile), grime streaks and rust spots.
            d["corrugated"] = new TexSet
            {
                albedo = SaveTex("T_container_albedo", 512, (u, v) =>
                {
                    float streak = Fbm(u * 1f, v * 0.08f, 8, 3, 31);
                    float rust = Mathf.Clamp01((Fbm(u, v, 6, 4, 32) - 0.62f) * 4f);
                    float c = 0.92f - 0.18f * streak;
                    var col = G(c);
                    return Color.Lerp(col, new Color(0.45f, 0.27f, 0.16f), rust * 0.6f);
                }, false),
                normal = SaveNormal("T_container_normal", 512, (u, v) => 0.5f + 0.5f * Mathf.Sin(u * Mathf.PI * 2 * 16) + 0.05f * Fbm(u, v, 8, 3, 33), 2.5f)
            };
            d["steel"] = new TexSet
            {
                albedo = SaveTex("T_paint_albedo", 256, (u, v) => G(0.9f + 0.1f * Fbm(u, v * 0.2f, 6, 3, 41) - 0.15f * Mathf.Clamp01((Fbm(u, v, 4, 3, 42) - 0.65f) * 3f)), false),
                normal = SaveNormal("T_paint_normal", 256, (u, v) => Fbm(u, v, 8, 3, 43), 0.6f)
            };
            // Office facade: one 3.5 m storey x 3 m bay per tile; glass band with mullion.
            d["facade_office"] = new TexSet
            {
                albedo = SaveTex("T_facade_office_albedo", 256, (u, v) =>
                {
                    bool glass = v > 0.28f && v < 0.92f && (u % 1f) > 0.06f && (u % 1f) < 0.94f;
                    if (glass) { float r = 0.25f + 0.15f * v; return new Color(r * 0.75f, r * 0.9f, r * 1.05f); }
                    return G(0.82f + 0.06f * Fbm(u, v, 4, 2, 51));
                }, false),
                normal = SaveNormal("T_facade_office_normal", 256, (u, v) => v > 0.28f && v < 0.92f ? 0f : 1f, 1.5f)
            };
            // Korean apartment facade: white/cream wall, two windows + balcony rail per tile (2.8 m storey).
            d["facade_apartment"] = new TexSet
            {
                albedo = SaveTex("T_facade_apartment_albedo", 256, (u, v) =>
                {
                    float uu = u * 2f % 1f;
                    bool win = v > 0.35f && v < 0.85f && uu > 0.12f && uu < 0.88f;
                    bool rail = v > 0.28f && v < 0.36f;
                    if (win) return new Color(0.2f, 0.24f, 0.28f) * (0.8f + 0.4f * Hash((int)(u * 2), (int)(v * 3), 52));
                    if (rail) return G(0.6f);
                    return G(0.93f + 0.05f * Fbm(u, v, 4, 2, 53));
                }, false),
                normal = SaveNormal("T_facade_apartment_normal", 256, (u, v) => { float uu = u * 2f % 1f; return v > 0.35f && v < 0.85f && uu > 0.12f && uu < 0.88f ? 0f : 1f; }, 1.5f)
            };
            // Forested hill: dark/light green canopy blobs with occasional rock.
            d["forest"] = new TexSet
            {
                albedo = SaveTex("T_forest_albedo", 512, (u, v) =>
                {
                    float canopy = Fbm(u, v, 24, 3, 61);
                    float big = Fbm(u, v, 3, 3, 62);
                    var c = Color.Lerp(new Color(0.045f, 0.085f, 0.04f), new Color(0.11f, 0.17f, 0.07f), canopy);
                    c = Color.Lerp(c, new Color(0.17f, 0.19f, 0.11f), Mathf.Clamp01((big - 0.6f) * 2f));
                    return c;
                }, false),
                normal = SaveNormal("T_forest_normal", 512, (u, v) => Fbm(u, v, 24, 3, 61), 4f)
            };
            d["grass"] = new TexSet
            {
                albedo = SaveTex("T_grass_albedo", 256, (u, v) => Color.Lerp(new Color(0.22f, 0.32f, 0.12f), new Color(0.34f, 0.40f, 0.18f), Fbm(u, v, 8, 4, 71)), false),
                normal = SaveNormal("T_grass_normal", 256, (u, v) => Hash((int)(u * 128), (int)(v * 128), 72), 0.8f)
            };
            d["sand"] = new TexSet
            {
                albedo = SaveTex("T_sand_albedo", 256, (u, v) => Color.Lerp(new Color(0.70f, 0.62f, 0.48f), new Color(0.80f, 0.73f, 0.58f), Fbm(u, v, 8, 4, 81)), false),
                normal = SaveNormal("T_sand_normal", 256, (u, v) => Fbm(u, v, 32, 2, 82), 1f)
            };
            // Ploughed / planted field: 0.6 m crop rows (10 per 6 m tile) on brown earth.
            d["field"] = new TexSet
            {
                albedo = SaveTex("T_field_albedo", 256, (u, v) =>
                {
                    float row = Mathf.Abs(Mathf.Sin((v + 0.03f * Fbm(u, v, 3, 2, 95)) * Mathf.PI * 10f));
                    float crop = Mathf.Clamp01((row - 0.5f) / 0.3f);
                    var c = Color.Lerp(new Color(0.40f, 0.33f, 0.24f), new Color(0.30f, 0.46f, 0.18f), crop);
                    return c * (0.85f + 0.3f * Fbm(u, v, 12, 3, 96));
                }, false),
                normal = SaveNormal("T_field_normal", 256, (u, v) => Mathf.Abs(Mathf.Sin(v * Mathf.PI * 10f)) + 0.3f * Fbm(u, v, 24, 2, 97), 2f)
            };
            d["roof"] = new TexSet
            {
                albedo = SaveTex("T_roof_albedo", 256, (u, v) => G(0.85f + 0.15f * Mathf.Abs(Mathf.Sin(v * Mathf.PI * 8)) - 0.1f * Fbm(u, v, 4, 3, 91)), false),
                normal = SaveNormal("T_roof_normal", 256, (u, v) => Mathf.Abs(Mathf.Sin(v * Mathf.PI * 8)), 1.5f)
            };
            return d;
        }

        // ------------------------------------------------------------ materials
        public static Material MakeLit(string key, Color tint, float smooth, float metal, Texture2D albedo = null, Texture2D normal = null,
            float tileMeters = 4f, float normalScale = 1f, Color? emission = null)
        {
            var lit = Shader.Find("HDRP/Lit");
            var path = $"{MatDir}/M_{key}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(lit); AssetDatabase.CreateAsset(m, path); }
            m.shader = lit;
            m.SetColor("_BaseColor", tint);
            m.SetFloat("_Smoothness", smooth);
            m.SetFloat("_Metallic", metal);
            m.SetTexture("_BaseColorMap", albedo);
            m.SetTexture("_NormalMap", normal);
            m.SetFloat("_NormalScale", normalScale);
            if (albedo != null || normal != null)
            {
                m.SetFloat("_UVBase", 5f);             // Triplanar (world space): textures don't stretch on large boxes
                m.SetFloat("_TexWorldScale", 1f / tileMeters);
            }
            else m.SetFloat("_UVBase", 0f);
            if (emission.HasValue)
            {
                m.SetColor("_EmissiveColor", emission.Value);
                m.SetColor("_EmissiveColorLDR", emission.Value);
                m.SetFloat("_UseEmissiveIntensity", 0f);
                m.EnableKeyword("_EMISSIVE_COLOR_MAP");
            }
            HDMaterial.ValidateMaterial(m);
            EditorUtility.SetDirty(m);
            return m;
        }

        public static void BuildMaterials(HarborTheme theme, Dictionary<string, TexSet> t, Texture2D rockAlb, Texture2D rockNrm)
        {
            void Add(string key, Material m)
            {
                theme.materials.RemoveAll(e => e.key == key);
                theme.materials.Add(new HarborTheme.Entry { key = key, material = m });
            }
            Add("concrete", MakeLit("concrete", new Color(0.62f, 0.61f, 0.58f), 0.2f, 0, t["concrete"].albedo, t["concrete"].normal, 6f));
            Add("breakwater", MakeLit("breakwater", new Color(0.50f, 0.49f, 0.47f), 0.15f, 0, t["concrete"].albedo, t["concrete"].normal, 5f, 1.5f));
            Add("roof_concrete", MakeLit("roof_concrete", new Color(0.55f, 0.55f, 0.53f), 0.2f, 0, t["concrete"].albedo, t["concrete"].normal, 8f));
            Add("quay_wet", MakeLit("quay_wet", new Color(0.20f, 0.22f, 0.18f), 0.55f, 0, t["concrete"].albedo, t["concrete"].normal, 3f));
            Add("asphalt", MakeLit("asphalt", new Color(0.20f, 0.20f, 0.21f), 0.25f, 0, t["asphalt"].albedo, t["asphalt"].normal, 4f));
            Add("grass", MakeLit("grass", new Color(1, 1, 1), 0.1f, 0, t["grass"].albedo, t["grass"].normal, 12f));
            Add("forest", MakeLit("forest", new Color(1.15f, 1.25f, 1.0f), 0.05f, 0, t["forest"].albedo, t["forest"].normal, 60f, 1.5f));
            Add("sand", MakeLit("sand", new Color(1, 1, 1), 0.15f, 0, t["sand"].albedo, t["sand"].normal, 8f));
            Add("rock", MakeLit("rock", new Color(1, 1, 1), 0.2f, 0, rockAlb, rockNrm, 3.5f));
            Add("paint_yellow", MakeLit("paint_yellow", new Color(0.85f, 0.65f, 0.08f), 0.35f, 0, t["steel"].albedo, null, 2f));
            Add("paint_white", MakeLit("paint_white", new Color(0.85f, 0.85f, 0.82f), 0.3f, 0, t["steel"].albedo, null, 2f));
            Add("rubber", MakeLit("rubber", new Color(0.04f, 0.04f, 0.04f), 0.3f, 0));
            Add("bollard", MakeLit("bollard", new Color(0.08f, 0.08f, 0.09f), 0.4f, 0.4f, t["steel"].albedo, t["steel"].normal, 1f));
            Add("steel_dark", MakeLit("steel_dark", new Color(0.13f, 0.13f, 0.14f), 0.45f, 0.7f, t["steel"].albedo, t["steel"].normal, 2f));
            Add("steel_galv", MakeLit("steel_galv", new Color(0.55f, 0.57f, 0.58f), 0.5f, 0.8f, t["steel"].albedo, t["steel"].normal, 2f));
            Add("crane_white", MakeLit("crane_white", new Color(0.86f, 0.87f, 0.86f), 0.45f, 0.3f, t["steel"].albedo, t["steel"].normal, 4f));
            Add("crane_blue", MakeLit("crane_blue", new Color(0.12f, 0.33f, 0.62f), 0.45f, 0.3f, t["steel"].albedo, t["steel"].normal, 4f));
            Add("crane_yellow", MakeLit("crane_yellow", new Color(0.88f, 0.66f, 0.10f), 0.45f, 0.3f, t["steel"].albedo, t["steel"].normal, 4f));
            Add("crane_house", MakeLit("crane_house", new Color(0.80f, 0.82f, 0.84f), 0.4f, 0.2f, t["corrugated"].albedo, t["corrugated"].normal, 6f));
            Add("glass_dark", MakeLit("glass_dark", new Color(0.07f, 0.09f, 0.11f), 0.92f, 0.1f));
            Add("facade_office", MakeLit("facade_office", new Color(0.92f, 0.93f, 0.95f), 0.55f, 0.1f, t["facade_office"].albedo, t["facade_office"].normal, 3.5f));
            Add("facade_apartment", MakeLit("facade_apartment", new Color(0.95f, 0.93f, 0.88f), 0.35f, 0f, t["facade_apartment"].albedo, t["facade_apartment"].normal, 2.8f));
            Add("warehouse_wall", MakeLit("warehouse_wall", new Color(0.70f, 0.74f, 0.78f), 0.35f, 0.3f, t["corrugated"].albedo, t["corrugated"].normal, 6f));
            Add("warehouse_roof", MakeLit("warehouse_roof", new Color(0.30f, 0.42f, 0.55f), 0.4f, 0.4f, t["roof"].albedo, t["roof"].normal, 5f));
            Add("beacon_red", MakeLit("beacon_red", new Color(0.75f, 0.07f, 0.06f), 0.5f, 0));
            Add("beacon_green", MakeLit("beacon_green", new Color(0.05f, 0.45f, 0.18f), 0.5f, 0));
            Add("beacon_white", MakeLit("beacon_white", new Color(0.88f, 0.88f, 0.86f), 0.4f, 0, t["concrete"].albedo, null, 3f));
            Add("lamp", MakeLit("lamp", new Color(0.9f, 0.9f, 0.85f), 0.6f, 0, emission: new Color(1f, 0.92f, 0.75f) * 3000f));
            Add("truck_red", MakeLit("truck_red", new Color(0.55f, 0.08f, 0.06f), 0.5f, 0.2f));
            Add("truck_white", MakeLit("truck_white", new Color(0.82f, 0.82f, 0.80f), 0.5f, 0.2f));
            Add("truck_blue", MakeLit("truck_blue", new Color(0.10f, 0.20f, 0.45f), 0.5f, 0.2f));
            Add("tree_leaves", MakeLit("tree_leaves", new Color(1, 1, 1), 0.1f, 0, t["forest"].albedo, t["forest"].normal, 4f, 2f));
            Add("tree_trunk", MakeLit("tree_trunk", new Color(0.25f, 0.18f, 0.12f), 0.2f, 0));
            Add("pontoon", MakeLit("pontoon", new Color(0.72f, 0.70f, 0.66f), 0.25f, 0, t["concrete"].albedo, t["concrete"].normal, 2f));
            Add("tank_white", MakeLit("tank_white", new Color(0.86f, 0.86f, 0.84f), 0.4f, 0.3f, t["steel"].albedo, t["steel"].normal, 4f));
            Add("chimney", MakeLit("chimney", new Color(0.75f, 0.73f, 0.70f), 0.2f, 0, t["concrete"].albedo, t["concrete"].normal, 4f));
            Add("beacon_white_band", MakeLit("beacon_white_band", new Color(0.92f, 0.92f, 0.90f), 0.4f, 0));
            Add("tetrapod_concrete", MakeLit("tetrapod_concrete", new Color(0.70f, 0.69f, 0.66f), 0.15f, 0, t["concrete"].albedo, t["concrete"].normal, 2.5f));
            var containerColours = new (string, Color)[]
            {
                ("container_red", new Color(0.55f, 0.10f, 0.08f)), ("container_blue", new Color(0.08f, 0.22f, 0.48f)),
                ("container_green", new Color(0.12f, 0.36f, 0.20f)), ("container_grey", new Color(0.55f, 0.56f, 0.56f)),
                ("container_orange", new Color(0.80f, 0.36f, 0.08f)), ("container_white", new Color(0.82f, 0.82f, 0.80f)),
                ("container_teal", new Color(0.08f, 0.40f, 0.42f)), ("container_brown", new Color(0.36f, 0.22f, 0.13f)),
                ("container_navy", new Color(0.06f, 0.10f, 0.25f)), ("container_maroon", new Color(0.36f, 0.07f, 0.10f)),
            };
            foreach (var (k, c) in containerColours) Add(k, MakeLit(k, c, 0.35f, 0.3f, t["corrugated"].albedo, t["corrugated"].normal, 4.0f));
            var roofColours = new (string, Color)[]
            {
                ("roof_blue", new Color(0.15f, 0.32f, 0.55f)), ("roof_green", new Color(0.20f, 0.45f, 0.30f)), ("roof_red", new Color(0.55f, 0.18f, 0.12f)),
                ("roof_orange", new Color(0.72f, 0.38f, 0.15f)), ("roof_grey", new Color(0.45f, 0.46f, 0.47f)), ("roof_teal", new Color(0.18f, 0.48f, 0.48f)),
            };
            foreach (var (k, c) in roofColours) Add(k, MakeLit(k, c, 0.3f, 0.2f, t["roof"].albedo, t["roof"].normal, 3f));
            var wallColours = new (string, Color)[]
            {
                ("wall_cream", new Color(0.90f, 0.86f, 0.76f)), ("wall_white", new Color(0.92f, 0.92f, 0.90f)), ("wall_pink", new Color(0.86f, 0.70f, 0.66f)),
                ("wall_brick", new Color(0.55f, 0.32f, 0.24f)), ("wall_grey", new Color(0.70f, 0.70f, 0.70f)),
            };
            foreach (var (k, c) in wallColours) Add(k, MakeLit(k, c, 0.25f, 0, t["facade_apartment"].albedo, t["facade_apartment"].normal, 2.8f));
            EditorUtility.SetDirty(theme);
        }

        // ------------------------------------------------------------ meshes
        static Mesh SaveMesh(Mesh m, string name)
        {
            Directory.CreateDirectory(MeshDir);
            var path = $"{MeshDir}/{name}.asset";
            var old = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (old != null) { EditorUtility.CopySerialized(m, old); return old; }
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        /// <summary>Irregular low-poly rock: subdivided icosahedron with deterministic noise displacement, flat bottom-ish.</summary>
        static Mesh RockMesh(int seed)
        {
            var t = (1f + Mathf.Sqrt(5f)) / 2f;
            var v = new List<Vector3>
            {
                new(-1, t, 0), new(1, t, 0), new(-1, -t, 0), new(1, -t, 0), new(0, -1, t), new(0, 1, t),
                new(0, -1, -t), new(0, 1, -t), new(t, 0, -1), new(t, 0, 1), new(-t, 0, -1), new(-t, 0, 1)
            };
            var f = new List<int> { 0,11,5, 0,5,1, 0,1,7, 0,7,10, 0,10,11, 1,5,9, 5,11,4, 11,10,2, 10,7,6, 7,1,8, 3,9,4, 3,4,2, 3,2,6, 3,6,8, 3,8,9, 4,9,5, 2,4,11, 6,2,10, 8,6,7, 9,8,1 };
            for (int s = 0; s < 2; s++)
            {
                var mid = new Dictionary<long, int>();
                var nf = new List<int>();
                int M(int a, int b)
                {
                    long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                    if (mid.TryGetValue(key, out var i)) return i;
                    v.Add(((v[a] + v[b]) * 0.5f)); mid[key] = v.Count - 1; return v.Count - 1;
                }
                for (int i = 0; i < f.Count; i += 3)
                {
                    int a = f[i], b = f[i + 1], c = f[i + 2], ab = M(a, b), bc = M(b, c), ca = M(c, a);
                    nf.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
                }
                f = nf;
            }
            var rnd = new System.Random(seed);
            var stretch = new Vector3(0.8f + (float)rnd.NextDouble() * 0.5f, 0.6f + (float)rnd.NextDouble() * 0.3f, 0.8f + (float)rnd.NextDouble() * 0.5f);
            for (int i = 0; i < v.Count; i++)
            {
                var n = v[i].normalized;
                float d = 0.8f + 0.35f * Fbm(n.x * 0.5f + 0.5f, n.z * 0.5f + 0.5f + n.y * 0.3f, 3, 3, seed);
                var p = Vector3.Scale(n * d * 0.5f, stretch);
                if (p.y < -0.2f) p.y = -0.2f - (p.y + 0.2f) * 0.2f; // flatten the underside
                v[i] = p;
            }
            // Flat-shaded: unshared vertices per face.
            var fv = new List<Vector3>(); var ft = new List<int>();
            for (int i = 0; i < f.Count; i++) { fv.Add(v[f[i]]); ft.Add(i); }
            var m = new Mesh { name = "rock_" + seed };
            m.SetVertices(fv); m.SetTriangles(ft, 0); m.RecalculateNormals(); m.RecalculateBounds();
            // Rebase so the bottom is at y=0.
            var off = new Vector3(0, -m.bounds.min.y, 0);
            for (int i = 0; i < fv.Count; i++) fv[i] += off;
            m.SetVertices(fv); m.RecalculateBounds(); m.RecalculateTangents();
            return m;
        }

        /// <summary>Hill/mountain/island mound on a 64x64 grid, 1x1 footprint, base at y=0; heights from ProcNoise.HillHeight so the
        /// runtime can compute the peak (camera hills).</summary>
        static Mesh HillMesh(int hillIndex, bool ridged = false)
        {
            const int N = ProcNoise.HillGrid;
            var verts = new Vector3[(N + 1) * (N + 1)]; var uvs = new Vector2[verts.Length];
            for (int z = 0; z <= N; z++)
                for (int x = 0; x <= N; x++)
                {
                    float u = (float)x / N, w = (float)z / N;
                    float hgt = ProcNoise.HillHeight(hillIndex, u, w, ridged);
                    if (x == 0 || z == 0 || x == N || z == N) hgt = -0.05f;
                    verts[z * (N + 1) + x] = new Vector3(u - 0.5f, hgt, w - 0.5f);
                    uvs[z * (N + 1) + x] = new Vector2(u, w);     // baked per-hill textures (HillBaker) are UV-mapped
                }
            var tris = new List<int>();
            for (int z = 0; z < N; z++)
                for (int x = 0; x < N; x++)
                {
                    int i = z * (N + 1) + x;
                    tris.AddRange(new[] { i, i + N + 1, i + 1, i + 1, i + N + 1, i + N + 2 });
                }
            var m = new Mesh { name = "hill_" + hillIndex };
            m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            m.vertices = verts; m.uv = uvs; m.triangles = tris.ToArray(); m.RecalculateNormals(); m.RecalculateBounds(); m.RecalculateTangents();
            return m;
        }

        /// <summary>Tetrapod: four tapered legs from the centre towards the vertices of a tetrahedron (~2.6 m across).</summary>
        static Mesh TetrapodMesh()
        {
            var dirs = new[] { new Vector3(0, 1, 0), new Vector3(0.943f, -0.333f, 0), new Vector3(-0.471f, -0.333f, 0.816f), new Vector3(-0.471f, -0.333f, -0.816f) };
            var v = new List<Vector3>(); var tri = new List<int>();
            const int S = 10; const float len = 1.35f, r0 = 0.55f, r1 = 0.32f;
            foreach (var d in dirs)
            {
                var a = Vector3.Cross(d, Mathf.Abs(d.y) > 0.9f ? Vector3.right : Vector3.up).normalized; var b = Vector3.Cross(d, a);
                int baseI = v.Count;
                for (int i = 0; i < S; i++)
                {
                    float ang = i * Mathf.PI * 2 / S; var rd = a * Mathf.Cos(ang) + b * Mathf.Sin(ang);
                    v.Add(rd * r0); v.Add(d * len + rd * r1);
                }
                for (int i = 0; i < S; i++)
                {
                    int i0 = baseI + i * 2, i1 = baseI + ((i + 1) % S) * 2;
                    tri.AddRange(new[] { i0, i1, i0 + 1, i1, i1 + 1, i0 + 1 });
                }
                int tip = v.Count; v.Add(d * (len + 0.05f));
                for (int i = 0; i < S; i++) tri.AddRange(new[] { baseI + i * 2 + 1, baseI + ((i + 1) % S) * 2 + 1, tip });
            }
            var fv = new List<Vector3>(); var ft = new List<int>();
            for (int i = 0; i < tri.Count; i++) { fv.Add(v[tri[i]]); ft.Add(i); }
            var m = new Mesh { name = "tetrapod" };
            m.SetVertices(fv); m.SetTriangles(ft, 0); m.RecalculateNormals(); m.RecalculateBounds(); m.RecalculateTangents();
            return m;
        }

        static GameObject SaveMeshPrefab(string name, Mesh mesh, Material mat, bool convex)
        {
            Directory.CreateDirectory(PrefabDir);
            var go = new GameObject(name);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            var mc = go.AddComponent<MeshCollider>(); mc.sharedMesh = mesh; mc.convex = convex;
            var p = PrefabUtility.SaveAsPrefabAsset(go, $"{PrefabDir}/{name}.prefab");
            UnityEngine.Object.DestroyImmediate(go);
            return p;
        }

        /// <summary>Wraps an imported model so its pivot is the bottom centre of its render bounds; adds MeshColliders.</summary>
        /// <summary>Wraps an imported model and scales it uniformly so its bounds height equals targetHeight metres.</summary>
        static GameObject WrapImported(string name, string assetPath, float targetHeight, out string note)
        {
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath) ?? throw new InvalidOperationException("not imported: " + assetPath);
            var root = new GameObject(name);
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(src) ?? UnityEngine.Object.Instantiate(src);
            inst.transform.SetParent(root.transform, false);
            var rs = inst.GetComponentsInChildren<Renderer>(true);
            var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
            float k = targetHeight / Mathf.Max(b.size.y, 1e-4f);
            inst.transform.localScale = Vector3.one * k;
            b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
            inst.transform.localPosition = new Vector3(-b.center.x, -b.min.y, -b.center.z);
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                var mc = mf.gameObject.AddComponent<MeshCollider>(); mc.sharedMesh = mf.sharedMesh;
            }
            note = $"{assetPath} sha256={ScenarioIO.Sha256Hex(File.ReadAllBytes(assetPath))} scale={k:G5} bounds={b.size}; " + TextureDiet.Apply(root, name);
            var p = PrefabUtility.SaveAsPrefabAsset(root, $"{PrefabDir}/{name}.prefab");
            UnityEngine.Object.DestroyImmediate(root);
            Debug.Log($"[Bootstrap] wrapped {name}: scale={k:G5} size={b.size} meshes={rs.Length}");
            return p;
        }

        /// <summary>Splits a multi-object GLB (vehicle packs) into one prefab per object: renderers whose bounds, grown by `gapM`,
        /// touch transitively form one cluster. Each cluster becomes `<key>_NN` with the pivot at its bottom centre, world poses
        /// kept, animations stripped, one BoxCollider (vehicles) or MeshColliders. Clusters whose longest horizontal side is
        /// outside [minLen, maxLen] (ground planes, debris, text) are skipped. Returns (key, prefab, bounds) per kept cluster.</summary>
        static List<(string key, GameObject prefab, Bounds b)> WrapPack(string key, string assetPath, float scale, string[] dropNodes, float gapM, float minLen, float maxLen, bool boxCollider, out string note)
        {
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath) ?? throw new InvalidOperationException("not imported: " + assetPath);
            var inst = UnityEngine.Object.Instantiate(src);
            inst.transform.localScale = inst.transform.localScale * scale;
            foreach (var t in inst.GetComponentsInChildren<Transform>(true).ToArray())
                if (t != null && dropNodes != null && dropNodes.Any(d => t.name.IndexOf(d, StringComparison.OrdinalIgnoreCase) >= 0)) UnityEngine.Object.DestroyImmediate(t.gameObject);
            foreach (var a in inst.GetComponentsInChildren<Animation>(true)) UnityEngine.Object.DestroyImmediate(a);
            foreach (var a in inst.GetComponentsInChildren<Animator>(true)) UnityEngine.Object.DestroyImmediate(a);
            var rs = inst.GetComponentsInChildren<Renderer>(true).Where(r => r.GetComponent<MeshFilter>() != null && r.GetComponent<MeshFilter>().sharedMesh != null).ToArray();
            // seeds = big parts (bodies) whose centre lies in no earlier seed's bounds; every other part (wheels, glass, lights)
            // joins the seed whose bounds (grown by gapM) contain its centre, else the nearest seed centre. Packs arrange their
            // vehicles close together, so plain AABB-overlap clustering merged them all.
            var order = rs.OrderByDescending(r => r.bounds.size.x * r.bounds.size.y * r.bounds.size.z).ToList();
            var seeds = new List<Renderer>();
            foreach (var r in order)
            {
                bool inside = seeds.Any(sd => { var b = sd.bounds; b.Expand(gapM * 2f); return b.Contains(r.bounds.center); });
                if (!inside && r.bounds.size.y > 0.3f && r.bounds.size.magnitude > 0.5f * order[0].bounds.size.magnitude * 0.6f) seeds.Add(r);   // flat shadow planes are never seeds
            }
            var clusters = new Dictionary<int, List<Renderer>>();
            for (int i = 0; i < seeds.Count; i++) clusters[i] = new List<Renderer> { seeds[i] };
            foreach (var r in rs)
            {
                if (seeds.Contains(r)) continue;
                if (r.bounds.size.y < 0.03f && Mathf.Max(r.bounds.size.x, r.bounds.size.z) > 1f) continue;   // baked shadow planes
                // nearest body: distance from the part centre to the (unexpanded) body bounds, ties broken by the distance between
                // centres. The earlier first-containing-expanded-bounds rule handed wheels/mirrors of a neighbouring car to the wrong
                // body (car_a clusters 3-4 m wide, crossing the stall lines).
                int best = -1; float bestD = float.MaxValue;
                for (int i = 0; i < seeds.Count; i++)
                {
                    var b = seeds[i].bounds;
                    float d = Vector3.Distance(b.ClosestPoint(r.bounds.center), r.bounds.center) + 0.01f * Vector3.Distance(b.center, r.bounds.center);
                    if (d < bestD) { bestD = d; best = i; }
                }
                if (best >= 0) clusters[best].Add(r);
            }
            var result = new List<(string, GameObject, Bounds)>();
            var ordered = clusters.Values.Select(l => { var b = l[0].bounds; foreach (var r in l) b.Encapsulate(r.bounds); return (l, b); })
                .OrderBy(x => x.b.center.z).ThenBy(x => x.b.center.x).ToList();
            var log = new System.Text.StringBuilder();
            int n = 0, skipped = 0;
            foreach (var (l, b) in ordered)
            {
                float len = Mathf.Max(b.size.x, b.size.z);
                if (len < minLen || len > maxLen || b.size.y < 0.3f) { skipped++; log.Append($" skip[{b.size.x:F1}x{b.size.y:F1}x{b.size.z:F1} n={l.Count}]"); continue; }
                string ck = $"{key}_{n:00}";
                var root = new GameObject(ck);
                foreach (var r in l)
                {
                    var mf = r.GetComponent<MeshFilter>();
                    var go = new GameObject(r.gameObject.name);
                    go.transform.SetPositionAndRotation(r.transform.position, r.transform.rotation);
                    go.transform.localScale = r.transform.lossyScale;
                    go.transform.SetParent(root.transform, true);
                    go.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
                    go.AddComponent<MeshRenderer>().sharedMaterials = r.sharedMaterials;
                    if (!boxCollider) { var mc = go.AddComponent<MeshCollider>(); mc.sharedMesh = mf.sharedMesh; }
                }
                // Packs contain cars at different diagonal angles. Recover each vehicle's
                // long axis from the actual vertices before centring, rather than guessing yaw
                // from an axis-aligned bounding box that includes the original diagonal.
                var points = new List<Vector3>();
                foreach (var mf in root.GetComponentsInChildren<MeshFilter>())
                {
                    var vertices = mf.sharedMesh.vertices;
                    int step = Mathf.Max(1, vertices.Length / 1000);
                    for (int i = 0; i < vertices.Length; i += step) points.Add(mf.transform.TransformPoint(vertices[i]));
                }
                // Minimum-area horizontal rectangle is insensitive to mesh tessellation density
                // (a PCA can follow a highly tessellated diagonal windscreen instead of the car).
                float angle = 0f, bestArea = float.PositiveInfinity;
                for (int deg = 0; deg < 180; deg++)
                {
                    float rad = deg * Mathf.Deg2Rad, co = Mathf.Cos(rad), si = Mathf.Sin(rad);
                    float loX = float.PositiveInfinity, hiX = float.NegativeInfinity, loZ = loX, hiZ = hiX;
                    foreach (var point in points)
                    {
                        float px = point.x * co + point.z * si, pz = -point.x * si + point.z * co;
                        loX = Mathf.Min(loX, px); hiX = Mathf.Max(hiX, px); loZ = Mathf.Min(loZ, pz); hiZ = Mathf.Max(hiZ, pz);
                    }
                    float area = (hiX - loX) * (hiZ - loZ);
                    if (area < bestArea) { bestArea = area; angle = rad + (hiX - loX < hiZ - loZ ? Mathf.PI / 2 : 0f); }
                }
                var axis = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
                float oldOffset = key == "car_a" ? (n == 3 || n == 4 ? 180f : -90f) : 90f;
                var expectedNose = Quaternion.Euler(0, -oldOffset, 0) * Vector3.right;
                if (Vector3.Dot(axis, expectedNose) < 0) angle += Mathf.PI;
                var align = Matrix4x4.Rotate(Quaternion.Euler(0, angle * Mathf.Rad2Deg, 0));
                int partIndex = 0;
                foreach (var mf in root.GetComponentsInChildren<MeshFilter>())
                {
                    var sourceMesh = mf.sharedMesh;
                    var parts = new List<CombineInstance>();
                    for (int sub = 0; sub < sourceMesh.subMeshCount; sub++)
                        parts.Add(new CombineInstance { mesh = sourceMesh, subMeshIndex = sub, transform = align * mf.transform.localToWorldMatrix });
                    var baked = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                    baked.CombineMeshes(parts.ToArray(), false, true); baked.RecalculateBounds();
                    mf.sharedMesh = SaveMesh(baked, ck + "_part_" + partIndex++);
                    mf.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity); mf.transform.localScale = Vector3.one;
                    var collider = mf.GetComponent<MeshCollider>(); if (collider != null) collider.sharedMesh = mf.sharedMesh;
                }
                var rb = root.GetComponentsInChildren<Renderer>(true); var bb = rb[0].bounds; foreach (var r in rb) bb.Encapsulate(r.bounds);
                // Bake the pivot into children: HarborBuilder overwrites root placement.
                var pivot = new Vector3(bb.center.x, bb.min.y, bb.center.z);
                foreach (Transform child in root.transform) child.localPosition -= pivot;
                bb = rb[0].bounds; foreach (var r in rb) bb.Encapsulate(r.bounds);
                if (boxCollider) { var bc = root.AddComponent<BoxCollider>(); bc.center = bb.center; bc.size = bb.size; }
                TextureDiet.Apply(root, ck);
                var p = PrefabUtility.SaveAsPrefabAsset(root, $"{PrefabDir}/{ck}.prefab");
                UnityEngine.Object.DestroyImmediate(root);
                result.Add((ck, p, bb));
                log.Append($" {ck}[{bb.size.x:F2}x{bb.size.y:F2}x{bb.size.z:F2} n={l.Count}]");
                n++;
            }
            UnityEngine.Object.DestroyImmediate(inst);
            note = $"{assetPath} sha256={ScenarioIO.Sha256Hex(File.ReadAllBytes(assetPath))} scale={scale:G5} clusters={n} skipped={skipped};{log}";
            Debug.Log($"[Bootstrap] pack {key}: {note}");
            return result;
        }

        /// <summary>WrapImported with target height plus removal of named nodes (e.g. a baked site slab, foliage cards) and of
        /// flat renderers wider than `dropFlatWiderThan` m (ground planes under a building model).</summary>
        static GameObject WrapBuilding(string name, string assetPath, float targetHeight, string[] dropNodes, float dropFlatWiderThan, out string note)
        {
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath) ?? throw new InvalidOperationException("not imported: " + assetPath);
            var root = new GameObject(name);
            var inst = UnityEngine.Object.Instantiate(src);
            inst.transform.SetParent(root.transform, false);
            int dropped = 0;
            foreach (var t in inst.GetComponentsInChildren<Transform>(true).ToArray())
                if (t != null && dropNodes != null && dropNodes.Any(d => t.name.IndexOf(d, StringComparison.OrdinalIgnoreCase) >= 0)) { UnityEngine.Object.DestroyImmediate(t.gameObject); dropped++; }
            var rs0 = inst.GetComponentsInChildren<Renderer>(true);
            var b0 = rs0[0].bounds; foreach (var r in rs0) b0.Encapsulate(r.bounds);
            float k0 = targetHeight / Mathf.Max(b0.size.y, 1e-4f);
            if (dropFlatWiderThan > 0f)
                foreach (var r in rs0)
                    if (r != null && r.bounds.size.y * k0 < 6f && Mathf.Max(r.bounds.size.x, r.bounds.size.z) * k0 > dropFlatWiderThan) { UnityEngine.Object.DestroyImmediate(r.gameObject); dropped++; }
            var rs = inst.GetComponentsInChildren<Renderer>(true);
            var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
            float k = targetHeight / Mathf.Max(b.size.y, 1e-4f);
            inst.transform.localScale = Vector3.one * k;
            b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
            inst.transform.localPosition = new Vector3(-b.center.x, -b.min.y, -b.center.z);
            foreach (var a in inst.GetComponentsInChildren<Animation>(true)) UnityEngine.Object.DestroyImmediate(a);
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                var mc = mf.gameObject.AddComponent<MeshCollider>(); mc.sharedMesh = mf.sharedMesh;
            }
            note = $"{assetPath} sha256={ScenarioIO.Sha256Hex(File.ReadAllBytes(assetPath))} scale={k:G5} dropped={dropped} bounds={b.size}; " + TextureDiet.Apply(root, name);
            var p = PrefabUtility.SaveAsPrefabAsset(root, $"{PrefabDir}/{name}.prefab");
            UnityEngine.Object.DestroyImmediate(root);
            Debug.Log($"[Bootstrap] wrapped {name}: scale={k:G5} dropped={dropped} size={b.size} meshes={rs.Length}");
            return p;
        }

        /// <summary>Keeps only the renderers whose transform path contains one of `keep` (one object out of a scene / pack GLB),
        /// uniform scale, pivot bottom-centre, animations stripped; BoxCollider on the root (boxCollider) or MeshColliders.</summary>
        static GameObject WrapKeep(string name, string assetPath, float scale, string[] keep, bool boxCollider, out string note)
        {
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath) ?? throw new InvalidOperationException("not imported: " + assetPath);
            var root = new GameObject(name);
            var inst = UnityEngine.Object.Instantiate(src);
            inst.transform.SetParent(root.transform, false);
            inst.transform.localScale = inst.transform.localScale * scale;
            foreach (var a in inst.GetComponentsInChildren<Animation>(true)) UnityEngine.Object.DestroyImmediate(a);
            foreach (var a in inst.GetComponentsInChildren<Animator>(true)) UnityEngine.Object.DestroyImmediate(a);
            int dropped = 0;
            foreach (var r in inst.GetComponentsInChildren<Renderer>(true).ToArray())
            {
                if (r == null) continue;
                string path = ""; for (var t = r.transform; t != null && t != inst.transform; t = t.parent) path = t.name + "/" + path;
                if (!keep.Any(k => path.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0)) { UnityEngine.Object.DestroyImmediate(r.gameObject); dropped++; }
            }
            var rs = inst.GetComponentsInChildren<Renderer>(true);
            if (rs.Length == 0) throw new InvalidOperationException($"{name}: nothing kept for {string.Join(",", keep)}");
            var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
            inst.transform.localPosition = new Vector3(-b.center.x, -b.min.y, -b.center.z);
            b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
            if (boxCollider) { var bc = root.AddComponent<BoxCollider>(); bc.center = b.center; bc.size = b.size; }
            else foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true)) { if (mf.sharedMesh == null) continue; var mc = mf.gameObject.AddComponent<MeshCollider>(); mc.sharedMesh = mf.sharedMesh; }
            note = $"{assetPath} sha256={ScenarioIO.Sha256Hex(File.ReadAllBytes(assetPath))} scale={scale:G5} kept={rs.Length} dropped={dropped} bounds={b.size}; " + TextureDiet.Apply(root, name);
            var p = PrefabUtility.SaveAsPrefabAsset(root, $"{PrefabDir}/{name}.prefab");
            UnityEngine.Object.DestroyImmediate(root);
            Debug.Log($"[Bootstrap] wrapped keep {name}: kept={rs.Length} dropped={dropped} size={b.size}");
            return p;
        }

        static GameObject WrapImported(string name, string assetPath, out string note)
        {
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath) ?? throw new InvalidOperationException("not imported: " + assetPath);
            var root = new GameObject(name);
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(src) ?? UnityEngine.Object.Instantiate(src);
            inst.transform.SetParent(root.transform, false);
            var rs = inst.GetComponentsInChildren<Renderer>(true);
            var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
            inst.transform.localPosition = new Vector3(-b.center.x, -b.min.y, -b.center.z);
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                var mc = mf.gameObject.AddComponent<MeshCollider>(); mc.sharedMesh = mf.sharedMesh;
            }
            note = $"{assetPath} sha256={ScenarioIO.Sha256Hex(File.ReadAllBytes(assetPath))} bounds={b.size}; " + TextureDiet.Apply(root, name);
            var p = PrefabUtility.SaveAsPrefabAsset(root, $"{PrefabDir}/{name}.prefab");
            UnityEngine.Object.DestroyImmediate(root);
            Debug.Log($"[Bootstrap] wrapped {name}: size={b.size} meshes={rs.Length}");
            return p;
        }

        /// <summary>Wraps an imported harbor prop (ThirdParty/SketchfabCCBY/props, SOURCE_props.md): uniform scale, optional removal of
        /// named nodes (e.g. a baked ground plate), pivot = bottom of the render bounds and the xz centre of `pivotNode`'s renderers
        /// (or of everything), animation components stripped (captures must not depend on play time), optional merge into one mesh
        /// per material (models with hundreds of parts). MeshColliders match the rendered meshes.</summary>
        static GameObject WrapProp(string name, string assetPath, float scale, string[] dropNodes, string pivotNode, bool merge, out string note, float tint = 1f)
        {
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath) ?? throw new InvalidOperationException("not imported: " + assetPath);
            var root = new GameObject(name);
            var inst = UnityEngine.Object.Instantiate(src);
            inst.transform.SetParent(root.transform, false);
            inst.transform.localScale = inst.transform.localScale * scale;   // keep the GLB root's own scale (Sketchfab roots carry one)
            int dropped = 0;
            foreach (var t in inst.GetComponentsInChildren<Transform>(true).ToArray())
                if (t != null && dropNodes != null && dropNodes.Contains(t.name)) { UnityEngine.Object.DestroyImmediate(t.gameObject); dropped++; }
            foreach (var a in inst.GetComponentsInChildren<Animation>(true)) UnityEngine.Object.DestroyImmediate(a);
            foreach (var a in inst.GetComponentsInChildren<Animator>(true)) UnityEngine.Object.DestroyImmediate(a);
            GameObject body = inst;
            if (merge)
            {
                // one mesh per material, in the wrapper's frame
                var groups = new Dictionary<Material, List<CombineInstance>>();
                foreach (var mf in inst.GetComponentsInChildren<MeshFilter>(true))
                {
                    var mr = mf.GetComponent<MeshRenderer>(); if (mf.sharedMesh == null || mr == null) continue;
                    var mats = mr.sharedMaterials;
                    for (int s = 0; s < mf.sharedMesh.subMeshCount; s++)
                    {
                        var m = mats[Mathf.Min(s, mats.Length - 1)];
                        if (!groups.TryGetValue(m, out var l)) groups[m] = l = new List<CombineInstance>();
                        l.Add(new CombineInstance { mesh = mf.sharedMesh, subMeshIndex = s, transform = root.transform.worldToLocalMatrix * mf.transform.localToWorldMatrix });
                    }
                }
                body = new GameObject("merged");
                body.transform.SetParent(root.transform, false);
                int k = 0;
                foreach (var kv in groups)
                {
                    var mesh = new Mesh { name = $"{name}_m{k}", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                    mesh.CombineMeshes(kv.Value.ToArray(), true, true);
                    mesh.RecalculateBounds();
                    if (mesh.normals == null || mesh.normals.Length == 0) mesh.RecalculateNormals();
                    var path = $"{MeshDir}/{mesh.name}.asset";
                    var old = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                    if (old != null) { EditorUtility.CopySerialized(mesh, old); mesh = old; } else AssetDatabase.CreateAsset(mesh, path);
                    var go = new GameObject(mesh.name);
                    go.transform.SetParent(body.transform, false);
                    go.AddComponent<MeshFilter>().sharedMesh = mesh;
                    go.AddComponent<MeshRenderer>().sharedMaterial = kv.Key;
                    k++;
                }
                UnityEngine.Object.DestroyImmediate(inst);
            }
            var rs = body.GetComponentsInChildren<Renderer>(true);
            var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
            var pivot = b.center;
            if (!string.IsNullOrEmpty(pivotNode))
            {
                var pr = body.GetComponentsInChildren<Renderer>(true).Where(r => r.name == pivotNode || r.transform.parent != null && r.transform.parent.name == pivotNode).ToArray();
                if (pr.Length == 0) throw new InvalidOperationException($"{name}: pivot node {pivotNode} not found");
                var pb = pr[0].bounds; foreach (var r in pr) pb.Encapsulate(r.bounds); pivot = pb.center;
            }
            body.transform.localPosition = new Vector3(-pivot.x, -b.min.y, -pivot.z);
            int tris = 0;
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                tris += (int)(mf.sharedMesh.GetIndexCount(0) / 3);
                var mc = mf.gameObject.AddComponent<MeshCollider>(); mc.sharedMesh = mf.sharedMesh;
            }
            b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
            if (tint != 1f)
            {
                // prefab-copy materials only: scale the base colour (a bright scanned sand saturates under the 100 klx day preset)
                Directory.CreateDirectory(MatDir + "/Derived");
                foreach (var r in rs)
                {
                    var mats = r.sharedMaterials;
                    for (int i = 0; i < mats.Length; i++)
                    {
                        var c = new Material(mats[i]) { name = mats[i].name + " (tint)" };
                        foreach (var prop in new[] { "baseColorFactor", "_BaseColor" })
                            if (c.HasProperty(prop)) { var f = c.GetColor(prop); c.SetColor(prop, new Color(f.r * tint, f.g * tint, f.b * tint, f.a)); }
                        var path = $"{MatDir}/Derived/{name}_{i}_tint.mat";
                        var old = AssetDatabase.LoadAssetAtPath<Material>(path);
                        if (old != null) { EditorUtility.CopySerialized(c, old); c = old; } else AssetDatabase.CreateAsset(c, path);
                        mats[i] = c;
                    }
                    r.sharedMaterials = mats;
                }
            }
            note = $"{assetPath} sha256={ScenarioIO.Sha256Hex(File.ReadAllBytes(assetPath))} scale={scale:G5} tint={tint:G3} dropped={dropped} merged={merge} renderers={rs.Length} bounds={b.size} pivotNode={pivotNode ?? "-"}; " + TextureDiet.Apply(root, name);
            var p = PrefabUtility.SaveAsPrefabAsset(root, $"{PrefabDir}/{name}.prefab");
            UnityEngine.Object.DestroyImmediate(root);
            Debug.Log($"[Bootstrap] wrapped prop {name}: {note}");
            return p;
        }

        static void BuildVehiclePacks(HarborTheme theme)
        {
            const string SV = Root + "/ThirdParty/SketchfabCCBY/vehicles";
            void Add(string key, GameObject prefab, string source) => theme.prefabs.Add(new HarborTheme.PrefabEntry { key = key, prefab = prefab, source = source });
            theme.prefabs.RemoveAll(e => e.key.StartsWith("car_a_") || e.key.StartsWith("car_b_") || e.key.StartsWith("van_a_"));
            foreach (var (k, p, b) in WrapPack("car_a", $"{SV}/generic_passenger_car_pack_comrade1280.glb", 1f, null, 0.02f, 2.5f, 9f, true, out var v1)) Add(k, p, "Generic passenger car pack (comrade1280, CC BY): " + v1);
            foreach (var (k, p, b) in WrapPack("car_b", $"{SV}/traffic_car_pack_MaG80.glb", 0.5f, new[] { "Plane", "Text", "Asphalt", "Pavement", "Grass", "Road" }, 0.05f, 2.5f, 14f, true, out var v2)) Add(k, p, "Traffic Car Pack (MaG80, CC BY): " + v2);
            foreach (var (k, p, b) in WrapPack("van_a", $"{SV}/truck_van_pack_Batuhan13.glb", 0.075f, null, 0.02f, 2.5f, 14f, true, out var v3)) Add(k, p, "Truck/Van Pack (Batuhan13, CC BY): " + v3);
        }

        public static void RebuildVehiclePacksBatch()
        {
            try
            {
                var theme = HarborTheme.Load(); BuildVehiclePacks(theme);
                EditorUtility.SetDirty(theme); AssetDatabase.SaveAssets();
                EditorApplication.Exit(0);
            }
            catch (Exception ex) { Debug.LogException(ex); EditorApplication.Exit(1); }
        }

        public static void BuildPrefabs(HarborTheme theme)
        {
            void Add(string key, GameObject p, string src)
            {
                theme.prefabs.RemoveAll(e => e.key == key);
                theme.prefabs.Add(new HarborTheme.PrefabEntry { key = key, prefab = p, source = src });
            }
            // Build every map definition once so the camera hills register their HillSpec before the hill textures are baked.
            foreach (var id in HarborLibrary.Ids) HarborLibrary.Get(id);
            var rockMat = theme.Get("rock");
            for (int i = 0; i < 6; i++)
                Add("rock_" + i, SaveMeshPrefab("rock_" + i, SaveMesh(RockMesh(1000 + i), "rock_" + i), rockMat, true),
                    "procedural low-poly rock (seed " + (1000 + i) + "), texture Poly Haven coast_rocks_05 CC0");
            Add("tetrapod", SaveMeshPrefab("tetrapod", SaveMesh(TetrapodMesh(), "tetrapod"), theme.Get("tetrapod_concrete"), false),
                "procedural tetrapod armour unit (~2.6 m)");
            // Hills: per-mesh baked albedo/normal (HillBaker: rock on steep slopes and the shore band, meadow clearings, forest canopy).
            for (int i = 0; i < 4; i++)
                Add("hill_" + i, SaveMeshPrefab("hill_" + i, SaveMesh(HillMesh(i, true), "hill_" + i), HillBaker.Material(i, HillBaker.Variant.Forest), false),
                    "procedural hill mesh (seed " + (2000 + i) + "), unit footprint, scale in the harbor definition; baked forest/rock texture");
            for (int i = 0; i < 4; i++)
                Add("hill_L_" + i, SaveMeshPrefab("hill_L_" + i, SaveMesh(HillMesh(i, true), "hill_L_" + i), HillBaker.Material(i, HillBaker.Variant.ForestLarge), false),
                    "same hill mesh with the forest/rock bake tiled for instances wider than ~700 m");
            // Meadow variants for hills right under a camera: the canopy texture reads as flat "bubbles" at <100 m.
            for (int i = 0; i < 4; i++)
                Add("hill_m_" + i, SaveMeshPrefab("hill_m_" + i, SaveMesh(HillMesh(i), "hill_m_" + i), HillBaker.Material(i, HillBaker.Variant.Meadow), false),
                    "procedural hill mesh (seed " + (2000 + i) + ") with baked meadow/rock texture, for camera hills");
            // Procedural village houses (VillageAssets) for hill terraces and coastal roads.
            VillageAssets.BuildPrefabs(theme);
            // Poly Haven CC0 props / trees / rocks (ThirdParty/PolyHavenCC0/models, see SOURCE_models.md)
            foreach (var (key, dir) in new[]
            {
                ("tyre", "old_tyre"), ("lifebuoy", "lifebuoy"), ("crate_01", "plastic_crate_01"), ("crate_02", "plastic_crate_02"),
                ("barrel", "Barrel_01"), ("street_lamp", "street_lamp_01"), ("security_light", "security_light"),
                ("fence", "modular_chainlink_fence"), ("power_pole", "modular_electricity_poles"),
                ("tree_01", "island_tree_01_dec"), ("tree_02", "island_tree_02_dec"),
                ("shore_rock_01", "coast_rocks_01_dec"), ("shore_rock_02", "coast_land_rocks_02_dec"),
                ("shore_line_01", "coast_line_01_dec"), ("concrete_barrier", "concrete_road_barrier_02"),
            })
            {
                var gltf = Directory.GetFiles($"{PHM}/{dir}", "*.gltf")[0].Replace('\\', '/');
                Add(key, WrapImported(key, gltf, out var nk), nk);
            }
            // Cross-billboard trees for distant hill forests, baked from decimated Poly Haven pine_tree_01 / tree_small_02.
            TreeImpostors.Build(theme);
            // Sketchfab CC BY buildings (ThirdParty/SketchfabCCBY/buildings, SOURCE_buildings.md), scaled to a target height.
            foreach (var (key, file, hgt) in new[]
            {
                ("bld_warehouses", "warehouses.glb", 11f),
                ("bld_ind_warehouse", "low_poly_-_industrial_warehouse_2k.glb", 8.5f), ("bld_warehouse3", "pack_-_realistic_-_3_warehouse.glb", 12f),
                ("bld_tower", "low_poly_-_asian_tower_block_2k.glb", 48f), ("bld_shops", "asian_shop_pack_free_gameready.glb", 9f),
                ("bld_hospital", "jejungwon_hospital.glb", 12f),
            })
            {
                Add(key, WrapImported(key, $"{SB}/{file}", hgt, out var nb), nb);
            }
            // Sketchfab CC BY harbor props (ThirdParty/SketchfabCCBY/props/SOURCE_props.md): metres as authored except the RTG (mm).
            Add("prop_harbor_crane", WrapProp("prop_harbor_crane", $"{SP}/harbor_crane_1k.glb", 1f, null, "Object_5", false, out var p1), p1);
            Add("prop_portal_crane", WrapProp("prop_portal_crane", $"{SP}/port_crane_sokol_1k.glb", 1f, new[] { "Decor_Decor_0" }, "Body_Body_0", false, out var p2), p2);
            Add("prop_rtg", WrapProp("prop_rtg", $"{SP}/gantry_crane_rtg_dec15.glb", 0.001f, null, null, true, out var p3), p3);
            Add("prop_shore_scan", WrapProp("prop_shore_scan", $"{SP}/breakwater_coast_2k.glb", 1f, null, null, false, out var p4, 0.72f), p4);
            // Sketchfab CC BY vehicle packs and buildings, second batch (user: crude procedural cars/buildings must go;
            // ThirdParty/SketchfabCCBY/{vehicles,buildings2}/SOURCE_*.md). Packs are split into one prefab per vehicle.
            const string SV = Root + "/ThirdParty/SketchfabCCBY/vehicles", SB2 = Root + "/ThirdParty/SketchfabCCBY/buildings2";
            // a pack's cluster count can change between importer versions: forget all previous split prefabs of a pack first
            foreach (var pk in new[] { "car_a_", "car_b_", "van_a_" })
            {
                foreach (var e in theme.prefabs.Where(e => e.key.StartsWith(pk)).ToList())
                {
                    var path = e.prefab != null ? AssetDatabase.GetAssetPath(e.prefab) : null;
                    if (!string.IsNullOrEmpty(path)) AssetDatabase.DeleteAsset(path);
                }
                theme.prefabs.RemoveAll(e => e.key.StartsWith(pk));
            }
            BuildVehiclePacks(theme);
            Add("truck_semi_a", WrapProp("truck_semi_a", $"{SV}/semi_truck_baked_3DE_Study.glb", 0.72f, null, null, false, out var v4), "Semi Truck Baked (3DE_Study, CC BY): " + v4);
            Add("truck_kei", WrapProp("truck_kei", $"{SV}/simple_kei_truck_spookyghostboo.glb", 1.05f, null, null, false, out var v5), "Simple Kei Truck (spookyghostboo, CC BY): " + v5);
            Add("truck_container_a", WrapProp("truck_container_a", $"{SV}/dav_container_truck_jairobman.glb", 0.33f, null, null, false, out var v6), "DAV Container Truck (jairobman, CC BY): " + v6);
            Add("forklift_a", WrapProp("forklift_a", $"{SV}/forklift_low_poly_380660711785.glb", 0.01f, null, null, false, out var v7), "Forklift low poly (380660711785, CC BY): " + v7);
            Add("bld_apt_complex", WrapBuilding("bld_apt_complex", $"{SB2}/residential_complex_ziguratarchitect.glb", 42f, null, 40f, out var b1), "residential complex modern apartment building (ziguratarchitect, CC BY): " + b1);
            Add("bld_officetel", WrapBuilding("bld_officetel", $"{SB2}/modern_tower_office_apartment_ziguratarchitect.glb", 38f, null, 40f, out var b2), "modern tower office apartment (ziguratarchitect, CC BY): " + b2);
            Add("bld_hirise", WrapImported("bld_hirise", $"{SB2}/hi_rise_apartment_aitortilla01.glb", 110f, out var b3), "Hi rise apartment building (aitortilla01, CC BY): " + b3);
            Add("bld_villa_kr", WrapImported("bld_villa_kr", $"{SB2}/korean_multi_family_housing_clon6767.glb", 13f, out var b4), "Korean_multi-family housing lowpoly (clon6767, CC BY): " + b4);
            Add("bld_bakery_kr", WrapBuilding("bld_bakery_kr", $"{SB2}/korean_bakery_stokhuis.glb", 7.5f, new[] { "foilage", "particles", "alpha_", "trafic_board", "bike" }, 0f, out var b6), "Korean Bakery (stokhuis, CC BY): " + b6);
            Add("bld_warehouse_blue", WrapImported("bld_warehouse_blue", $"{SB2}/industrial_warehouse_prankscare99.glb", 10f, out var b7), "Industrial Warehouse (prankscare99, CC BY): " + b7);
            Add("bld_brick_factory", WrapImported("bld_brick_factory", $"{SB2}/industrial_building_spicybamer.glb", 12f, out var b8), "industrial building (spicybamer, CC BY): " + b8);
            Add("bld_city_towers", WrapImported("bld_city_towers", $"{SB2}/city_pack_7_Pasha.glb", 105f, out var b10), "City pack 7 (Pasha., CC BY): " + b10);
            Add("bld_apt_slab", WrapImported("bld_apt_slab", $"{SB2}/soviet_apartment_ColinGreenall.glb", 30f, out var b11), "LOW POLY - SOVIET APARTMENT BUILDING 8K (Colin.Greenall, CC BY): " + b11);
            // Street assets (ThirdParty/SketchfabCCBY/street/SOURCE_street.md)
            const string SS = Root + "/ThirdParty/SketchfabCCBY/street";
            Add("road_tile_x", WrapKeep("road_tile_x", $"{SS}/modular_roads_pack_SquadCell.glb", 1f, new[] { "Road_X_Intersection" }, false, out var s1), "Modular Roads Pack (SquadCell, CC BY), X tile: " + s1);
            Add("road_tile_t", WrapKeep("road_tile_t", $"{SS}/modular_roads_pack_SquadCell.glb", 1f, new[] { "Road_T_Intersection" }, false, out var s2), "Modular Roads Pack (SquadCell, CC BY), T tile: " + s2);
            Add("signal_vehicle", WrapKeep("signal_vehicle", $"{SS}/traffic_lights_whisper_alt.glb", 1.5f, new[] { "Light_" }, true, out var s3), "Traffic Lights [Street Assets Vol. 02] (whisper.alt, CC BY), vehicle signal: " + s3);
            Add("signal_ped", WrapKeep("signal_ped", $"{SS}/traffic_lights_whisper_alt.glb", 1.2f, new[] { "Pedestrian" }, true, out var s4), "Traffic Lights [Street Assets Vol. 02] (whisper.alt, CC BY), pedestrian signal: " + s4);
            Add("bus_shelter", WrapKeep("bus_shelter", $"{SS}/bus_stop_Elbolilloduro.glb", 1f, new[] { "Bus_stop_01" }, true, out var s5), "Bus stop (Elbolilloduro, CC BY), shelter: " + s5);
            Add("sign_gantry", WrapKeep("sign_gantry", $"{SS}/road_traffic_sign_jacky15621419.glb", 0.06f, new[] { "hang", "light" }, true, out var s6), "Road Traffic Sign (jacky15621419, CC BY), gantry: " + s6);
            Add("jersey_barrier", WrapKeep("jersey_barrier", $"{SS}/japanese_traffic_assets_erikkinc.glb", 150f, new[] { "jersey_shape1" }, true, out var s7), "Japanese Traffic Assets (erikkinc, CC BY), jersey barrier: " + s7);
            Add("vending_machine", WrapKeep("vending_machine", $"{SS}/japanese_traffic_assets_erikkinc.glb", 150f, new[] { "vending_machine" }, true, out var s8), "Japanese Traffic Assets (erikkinc, CC BY), vending machine: " + s8);
            Add("citylight_panel", WrapKeep("citylight_panel", $"{SS}/japanese_traffic_assets_erikkinc.glb", 150f, new[] { "citylight" }, true, out var s9), "Japanese Traffic Assets (erikkinc, CC BY), city-light panel: " + s9);
            Add("street_bin", WrapKeep("street_bin", $"{SS}/japanese_traffic_assets_erikkinc.glb", 150f, new[] { "street_bin_outside1", "street_bin_inside1" }, true, out var s10), "Japanese Traffic Assets (erikkinc, CC BY), street bin: " + s10);
            // Road assets, second batch (user 2026-11-09; ThirdParty/SketchfabCCBY/road2/SOURCE_road2.md): coastal-road guardrail
            // (4 m W-beam segment + end terminal), six pictogram road signs, and a tall coastal lighthouse for distant headlands.
            const string SR = Root + "/ThirdParty/SketchfabCCBY/road2";
            Add("guardrail_seg", WrapKeep("guardrail_seg", $"{SR}/guardrail_terminal_xplanepilot_1k.glb", 1f, new[] { "GuardRail_CrashBarrier" }, true, out var r1), "GuardRail and Terminal (xplanepilot, CC BY), 4 m segment: " + r1);
            Add("guardrail_end", WrapKeep("guardrail_end", $"{SR}/guardrail_terminal_xplanepilot_1k.glb", 1f, new[] { "GuardRail_terminal" }, true, out var r2), "GuardRail and Terminal (xplanepilot, CC BY), end terminal: " + r2);
            for (int i = 1; i <= 6; i++)
                Add($"road_sign_{i}", WrapKeep($"road_sign_{i}", $"{SR}/road_sign_pack_awlokdev_2k.glb", 1f, new[] { $"Road_Sign_0{i}" }, true, out var rs), $"Road Sign Pack (Free) (awlok.dev, CC BY), sign {i}: " + rs);
            Add("lighthouse_tall", WrapBuilding("lighthouse_tall", $"{SR}/lighthouse_fabiolopez_2k.glb", 26f, new[] { "aiStandardSurface10" }, 0f, out var r3), "Lighthouse (Fabio, CC BY), light beam cylinder dropped, scaled to 26 m: " + r3);
            Add("ocean_buoy", WrapImported("ocean_buoy", $"{PH}/ocean_buoy/ocean_buoy_2k.gltf", out var n1), n1);
            Add("lateral_marker", WrapImported("lateral_marker", $"{PH}/lateral_sea_marker/lateral_sea_marker_2k.gltf", out var n2), n2);
            Add("coastal_cliff", WrapImported("coastal_cliff", $"{PH}/coastal_cliff_01/coastal_cliff_01_2k.gltf", out var n3), n3);
            Add("godown_warehouse", WrapImported("godown_warehouse", $"{IS}/innerscene_godown_warehouse.glb", out var n4), n4);
            Add("mobile_crane", WrapImported("mobile_crane", $"{IS}/innerscene_mobile_crane.glb", out var n5), n5);
            // replacements of crude scenery (2026-11-09): breakwater lights, excavator, forklift, near containers, fish boxes
            ReplacementAssets.Build(theme, Add);
            EditorUtility.SetDirty(theme);
        }

        public static void Run(HarborTheme theme)
        {
            var tex = GenerateTextures();
            var rockAlb = ImportExisting($"{PH}/coast_rocks_05_textures/coast_rocks_05_diff_2k.jpg", false);
            var rockNrm = ImportExisting($"{PH}/coast_rocks_05_textures/coast_rocks_05_nor_gl_2k.jpg", true);
            BuildMaterials(theme, tex, rockAlb, rockNrm);
            VillageAssets.BuildMaterials(theme, tex);
            BuildPrefabs(theme);
        }

        static Texture2D ImportExisting(string path, bool normal)
        {
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            if (normal) imp.textureType = TextureImporterType.NormalMap;
            imp.wrapMode = TextureWrapMode.Repeat;
            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
    }
}
