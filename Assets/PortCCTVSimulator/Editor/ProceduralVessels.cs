using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace PortCCTV.EditorTools
{
    /// <summary>
    /// Procedural harbour work boats seen in Korean port CCTV (tugboat, flat-deck barge with an aggregate load).
    /// Lofted hull meshes + primitive superstructure. Long axis +X (bow at +X), keel bottom at y = 0, centred in X/Z.
    /// The hull GameObject is named "Procedural lofted hull" and is the vertical reference node.
    /// </summary>
    public static class ProceduralVessels
    {
        public const string HullNodeName = "Procedural lofted hull";
        const string MeshDir = "Assets/PortCCTVSimulator/Meshes";
        const string MatDir = "Assets/PortCCTVSimulator/Materials";

        internal static Material Mat(string key, Color c, float smooth, float metal)
        {
            var path = $"{MatDir}/M_vessel_{key}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(Shader.Find("HDRP/Lit")); AssetDatabase.CreateAsset(m, path); }
            m.SetColor("_BaseColor", c); m.SetFloat("_Smoothness", smooth); m.SetFloat("_Metallic", metal);
            HDMaterial.ValidateMaterial(m);
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>Half-breadth of the lofted hull at station u (0 stern .. 1 bow); shared with VesselDetails.</summary>
        public static float HalfBreadth(float u, float B, float bowFullness, float sternFullness)
        {
            float bf = u > 0.6f ? 1f - Mathf.Pow((u - 0.6f) / 0.4f, bowFullness) : 1f;
            float sf = u < 0.15f ? 1f - Mathf.Pow((0.15f - u) / 0.15f, sternFullness) * 0.25f : 1f;
            return B / 2 * Mathf.Clamp01(bf) * sf;
        }

        /// <summary>Deck (sheer line) height at station u.</summary>
        public static float DeckY(float u, float D, float sheerRise) => D * (1 + sheerRise * Mathf.Pow(Mathf.Max(0, u - 0.5f) / 0.5f, 2f));

        /// <summary>
        /// Lofted hull: stations along X, each a half-section (keel -> bilge -> side -> sheer) mirrored to both sides,
        /// closed with a flat deck and a transom. fullness: 1 = very fine bow, 4+ = barge-like.
        /// With <paramref name="waterline"/> >= 0 (local y of the design waterline = default immersion above the keel) two extra
        /// rings are inserted at the boot-top edges and the triangles are split by height into three sub-meshes:
        /// 0 topside (and deck), 1 boot-top band, 2 bottom (anti-fouling). UVs are metres / 6 (x, y) for a tiling weathering map.
        /// </summary>
        static Mesh LoftedHull(string name, float L, float B, float D, float bowFullness, float sternFullness, float bowRake, float sheerRise, float deadrise, float waterline = -1f)
        {
            const int N = 40;              // stations
            float[] sy = { 0f, 0.0f, 0.12f, 0.35f, 0.7f, 1f };     // section height fractions
            float[] sz = { 0f, 0.55f, 0.88f, 1f, 1f, 1f };         // half-breadth fractions
            int P0 = sy.Length;
            bool split = waterline >= 0f;
            float boot = Mathf.Clamp(0.08f * D, 0.25f, 1.4f);
            float bandLo = waterline - 0.3f * boot, bandHi = waterline + 0.7f * boot;
            var extra = split ? new[] { bandLo, bandHi } : new float[0];
            int P = P0 + extra.Length;
            var v = new List<Vector3>();
            var t0 = new List<int>(); var t1 = new List<int>(); var t2 = new List<int>();
            Vector3 Base(int i, int k, float side)
            {
                float u = (float)i / N;                        // 0 stern .. 1 bow
                float x = -L / 2 + u * L;
                float half = HalfBreadth(u, B, bowFullness, sternFullness);
                float keelLift = u > 0.6f ? Mathf.Pow((u - 0.6f) / 0.4f, 2f) * D * bowRake : 0f;
                float sheer = DeckY(u, D, sheerRise);
                float y = Mathf.Lerp(keelLift, sheer, sy[k]);
                if (k == 1) y += deadrise * half;
                if (k == 2) y = Mathf.Max(y, keelLift + deadrise * half + 0.04f * D);   // keep the section monotonic above the deadrise
                return new Vector3(x, y, side * half * sz[k]);
            }
            // One station section: base rings plus the boot-top rings, inserted on the side between the bilge and the sheer at
            // their absolute height (clamped inside the section where the keel rises above the waterline at the bow).
            List<Vector3> Section(int i, float side)
            {
                var pts = new List<Vector3>();
                for (int k = 0; k < P0; k++) pts.Add(Base(i, k, side));
                foreach (var ty in extra)
                {
                    float lo = pts[2].y + 1e-3f, hi = pts[pts.Count - 1].y - 1e-3f;
                    float y = Mathf.Clamp(ty, lo, Mathf.Max(lo, hi));
                    int seg = 2;
                    while (seg < pts.Count - 2 && pts[seg + 1].y < y) seg++;
                    var a = pts[seg]; var b = pts[seg + 1];
                    float f = b.y > a.y ? Mathf.Clamp01((y - a.y) / (b.y - a.y)) : 0.5f;
                    var q = Vector3.Lerp(a, b, f); q.y = y;
                    pts.Insert(seg + 1, q);
                }
                return pts;
            }
            void Tri(int a, int b, int c)
            {
                float cy = (v[a].y + v[b].y + v[c].y) / 3f;
                var list = !split || cy >= bandHi ? t0 : cy >= bandLo ? t1 : t2;
                list.Add(a); list.Add(b); list.Add(c);
            }
            foreach (var side in new[] { 1f, -1f })
            {
                int baseIdx = v.Count;
                for (int i = 0; i <= N; i++) v.AddRange(Section(i, side));
                for (int i = 0; i < N; i++)
                    for (int k = 0; k < P - 1; k++)
                    {
                        int a = baseIdx + i * P + k, b = a + P, c = a + 1, d = b + 1;
                        // Outward winding: +Z starboard / -Z port. Inward faces were
                        // culled above water, making barges look like submerged decks.
                        if (side > 0) { Tri(a, b, c); Tri(b, d, c); } else { Tri(a, c, b); Tri(b, c, d); }
                    }
            }
            // Deck (top) and transom (stern) caps.
            int s0 = 0, s1 = (N + 1) * P;
            for (int i = 0; i < N; i++)
            {
                int a = s0 + i * P + (P - 1), b = s0 + (i + 1) * P + (P - 1), c = s1 + i * P + (P - 1), d = s1 + (i + 1) * P + (P - 1);
                t0.AddRange(new[] { a, b, c, c, b, d });
            }
            for (int k = 0; k < P - 1; k++)
            {
                int a = s0 + k, b = s0 + k + 1, c = s1 + k, d = s1 + k + 1;
                Tri(a, b, c); Tri(b, d, c);
            }
            var uv = new List<Vector2>(v.Count);
            foreach (var p in v) uv.Add(new Vector2(p.x / 6f, p.y / 6f));
            var m = new Mesh { name = name };
            m.SetVertices(v); m.SetUVs(0, uv);
            if (split) { m.subMeshCount = 3; m.SetTriangles(t0, 0); m.SetTriangles(t1, 1); m.SetTriangles(t2, 2); }
            else m.SetTriangles(t0, 0);
            m.RecalculateNormals(); m.RecalculateBounds(); m.RecalculateTangents();
            Directory.CreateDirectory(MeshDir);
            var path = $"{MeshDir}/{name}.asset";
            var old = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (old != null) { EditorUtility.CopySerialized(m, old); return old; }
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        const string TexDir = "Assets/PortCCTVSimulator/Textures/Generated";

        /// <summary>Tileable greyscale hull weathering (1 tile = 6 m): welded plate seams, rust/grime streaks running down from
        /// seams and scuppers, waterline scuffs at the bottom of the tile and fine noise. Multiplied with the paint colour, so the
        /// paint randomiser still recolours the hull. Deterministic (fixed seed).</summary>
        static Texture2D s_weathering;
        internal static Texture2D HullWeathering()
        {
            if (s_weathering != null) return s_weathering;
            var path = $"{TexDir}/hull_weathering.png";
            const int S = 1024;
            var rng = new System.Random(7713);
            var g = new float[S * S];
            for (int i = 0; i < g.Length; i++) g[i] = 0.93f;
            float Noise(float x, float y) => Mathf.PerlinNoise(x, y);
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float u = x / (float)S, w = y / (float)S;
                    // tileable low-frequency mottling (sum of periodic perlin at integer periods)
                    float n = 0.5f * Noise(u * 4f, w * 4f) + 0.5f * Noise((u + 0.37f) * 4f, (w + 0.11f) * 4f);
                    g[y * S + x] += (n - 0.5f) * 0.08f + (float)(rng.NextDouble() - 0.5) * 0.03f;
                }
            // plate seams: horizontal every 2 m, vertical every 3 m (tile = 6 m)
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    bool hs = (y % (S / 3)) < 3, vs = (x % (S / 2)) < 3;
                    if (hs || vs) g[y * S + x] -= 0.07f;
                }
            // streaks: start at a random point (often a seam), run downwards (v decreasing), fade, wrap vertically
            for (int k = 0; k < 140; k++)
            {
                int x0 = rng.Next(S), y0 = rng.Next(S);
                if (rng.NextDouble() < 0.5) y0 = (y0 / (S / 3)) * (S / 3);
                int len = rng.Next(S / 12, S / 3), wid = rng.Next(2, 9);
                float strength = (float)(0.08 + rng.NextDouble() * 0.22);
                for (int d = 0; d < len; d++)
                {
                    float fade = 1f - (float)d / len;
                    int yy = ((y0 - d) % S + S) % S;
                    int jitter = (int)((Noise(k * 3.1f, d * 0.02f) - 0.5f) * 6f);
                    for (int dx = -wid; dx <= wid; dx++)
                    {
                        int xx = ((x0 + dx + jitter) % S + S) % S;
                        float edge = 1f - Mathf.Abs(dx) / (wid + 1f);
                        g[yy * S + xx] -= strength * fade * edge;
                    }
                }
            }
            var t = new Texture2D(S, S, TextureFormat.RGBA32, false, false);
            var px = new Color32[S * S];
            for (int i = 0; i < px.Length; i++) { byte b = (byte)Mathf.Clamp(Mathf.RoundToInt(g[i] * 255f), 120, 255); px[i] = new Color32(b, b, b, 255); }
            t.SetPixels32(px);
            Directory.CreateDirectory(TexDir);
            File.WriteAllBytes(path, t.EncodeToPNG());
            Object.DestroyImmediate(t);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.sRGBTexture = true; imp.wrapMode = TextureWrapMode.Repeat; imp.mipmapEnabled = true; imp.anisoLevel = 8;
            imp.SaveAndReimport();
            return s_weathering = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>Material with the hull weathering map multiplied into the base colour.</summary>
        internal static Material WeatheredMat(string key, Color c, float smooth, float metal)
        {
            var m = Mat(key, c, smooth, metal);
            m.SetTexture("_BaseColorMap", HullWeathering());
            HDMaterial.ValidateMaterial(m);
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>Hull paint zones below the topside: black boot-top and red-brown anti-fouling, weathered.</summary>
        static Material[] HullZones(Material topside, string prefix) => new[]
        {
            topside,
            WeatheredMat(prefix + "_boottop", new Color(0.05f, 0.05f, 0.05f), 0.35f, 0.1f),
            WeatheredMat(prefix + "_antifouling", new Color(0.42f, 0.12f, 0.09f), 0.3f, 0.1f),
        };

        static GameObject Part(Transform parent, PrimitiveType type, string name, Vector3 pos, Vector3 scale, Material m, Vector3 euler = default)
        {
            var go = GameObject.CreatePrimitive(type);
            Object.DestroyImmediate(go.GetComponent<Collider>()); // colliders are added uniformly by the bootstrap
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos; go.transform.localScale = scale; go.transform.localEulerAngles = euler;
            go.GetComponent<MeshRenderer>().sharedMaterial = m;
            return go;
        }

        static GameObject Hull(Transform root, Mesh mesh, Material m) => Hull(root, mesh, new[] { m });

        static GameObject Hull(Transform root, Mesh mesh, Material[] mats)
        {
            var h = new GameObject(HullNodeName);
            h.transform.SetParent(root, false);
            h.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = h.AddComponent<MeshRenderer>();
            r.sharedMaterials = mesh.subMeshCount > 1 && mats.Length >= mesh.subMeshCount ? mats : new[] { mats[0] };
            return h;
        }

        /// <summary>~24 m harbour tug: dark red hull, white deckhouse, wheelhouse with dark glazing, funnel, mast, tyre fenders.</summary>
        public static GameObject BuildTug()
        {
            var root = new GameObject("tug_model");
            var hullRed = Mat("tug_hull", new Color(0.45f, 0.08f, 0.07f), 0.45f, 0.2f);
            var white = Mat("tug_white", new Color(0.86f, 0.87f, 0.86f), 0.4f, 0.1f);
            var blue = Mat("tug_blue", new Color(0.10f, 0.22f, 0.45f), 0.4f, 0.1f);
            var black = Mat("tug_black", new Color(0.04f, 0.04f, 0.04f), 0.3f, 0f);
            var glass = Mat("glass", new Color(0.06f, 0.08f, 0.10f), 0.9f, 0.1f);
            var mesh = LoftedHull("hull_tug_24m", 24f, 9f, 3.6f, 1.6f, 2f, 0.35f, 0.25f, 0.15f, 1.0f);
            Hull(root.transform, mesh, HullZones(hullRed, "tug"));
            var t = root.transform;
            Part(t, PrimitiveType.Cube, "bulwark_band", new Vector3(0, 3.55f, 0), new Vector3(17.5f, 0.35f, 8.2f), blue);
            Part(t, PrimitiveType.Cube, "deckhouse", new Vector3(1.0f, 4.9f, 0), new Vector3(10f, 2.6f, 6.4f), white);
            Part(t, PrimitiveType.Cube, "wheelhouse", new Vector3(2.4f, 7.4f, 0), new Vector3(5.2f, 2.4f, 5.6f), white);
            Part(t, PrimitiveType.Cube, "wheelhouse_glass_f", new Vector3(5.02f, 7.6f, 0), new Vector3(0.06f, 1.2f, 5.0f), glass);
            Part(t, PrimitiveType.Cube, "wheelhouse_glass_s1", new Vector3(2.4f, 7.6f, 2.82f), new Vector3(4.6f, 1.2f, 0.06f), glass);
            Part(t, PrimitiveType.Cube, "wheelhouse_glass_s2", new Vector3(2.4f, 7.6f, -2.82f), new Vector3(4.6f, 1.2f, 0.06f), glass);
            Part(t, PrimitiveType.Cube, "wheelhouse_roof", new Vector3(2.4f, 8.7f, 0), new Vector3(5.6f, 0.2f, 6.0f), blue);
            Part(t, PrimitiveType.Cylinder, "funnel", new Vector3(-2.6f, 7.3f, 0), new Vector3(1.6f, 1.6f, 1.6f), blue);
            Part(t, PrimitiveType.Cylinder, "mast", new Vector3(2.4f, 10.6f, 0), new Vector3(0.18f, 1.8f, 0.18f), white);
            Part(t, PrimitiveType.Cube, "mast_yard", new Vector3(2.4f, 11.6f, 0), new Vector3(0.12f, 0.12f, 2.2f), white);
            Part(t, PrimitiveType.Cylinder, "towing_hook", new Vector3(-6.5f, 4.0f, 0), new Vector3(0.8f, 0.5f, 0.8f), black);
            for (int i = 0; i < 7; i++)
            {
                float x = -8f + i * 2.6f;
                foreach (var s in new[] { 1f, -1f })
                    Part(t, PrimitiveType.Cylinder, $"tyre_{i}_{s}", new Vector3(x, 2.4f, s * (Mathf.Lerp(4.4f, 4.5f, i / 6f))), new Vector3(1.0f, 0.18f, 1.0f), black, new Vector3(90, 0, 0));
            }
            Part(t, PrimitiveType.Cylinder, "bow_fender", new Vector3(11.6f, 2.8f, 0), new Vector3(1.4f, 2.2f, 1.4f), black, new Vector3(90, 0, 0));
            VesselDetails.Tug(t);
            return root;
        }

        /// <summary>~172 m container feeder: navy hull, hatch covers, deck container stacks (merged per colour), aft accommodation,
        /// bridge with wings, funnel. Container colours reuse the harbor container materials.</summary>
        public static GameObject BuildContainerShip(HarborTheme theme)
        {
            var root = new GameObject("container_ship_model");
            // muted navy (merchant hull paint fades and chalks), weathered; white superstructure with grime streaks
            var hull = WeatheredMat("cs_hull", new Color(0.10f, 0.14f, 0.22f), 0.3f, 0.15f);
            var deckGrey = WeatheredMat("cs_deck", new Color(0.35f, 0.36f, 0.36f), 0.3f, 0.3f);
            var white = WeatheredMat("cs_white", new Color(0.84f, 0.84f, 0.81f), 0.3f, 0.1f);
            var funnel = Mat("cs_funnel", new Color(0.70f, 0.12f, 0.10f), 0.4f, 0.1f);
            var glass = Mat("glass", new Color(0.06f, 0.08f, 0.10f), 0.9f, 0.1f);
            const float L = 172f, Bm = 27f, D = 14.5f;
            Hull(root.transform, LoftedHull("hull_container_172m", L, Bm, D, 2.4f, 1.5f, 0.45f, 0.12f, 0.02f, 4.2f), HullZones(hull, "cs"));
            var t = root.transform;
            Part(t, PrimitiveType.Cylinder, "fore_mast", new Vector3(72f, D + 5f, 0), new Vector3(0.3f, 3f, 0.3f), white);
            // Stepped marine accommodation, individually framed bridge windows and deck brows.
            // Apartment facade textures and the full-height red chimney made this read as a toy building.
            Part(t, PrimitiveType.Cube, "accommodation", new Vector3(-64f, D + 8.5f, 0), new Vector3(16f, 17f, 21f), white);
            for (int level = 0; level < 5; level++)
            {
                float y = D + 2f + level * 3.1f;
                foreach (float side in new[] { -1f, 1f })
                {
                    for (int w = 0; w < 5; w++)
                        Part(t, PrimitiveType.Cube, "cabin_window", new Vector3(-70f + w * 3f, y, side * 10.53f), new Vector3(1.0f, 1.1f, 0.08f), glass);
                    Part(t, PrimitiveType.Cube, "deck_brow", new Vector3(-64f, y + 1.05f, side * 10.65f), new Vector3(16.4f, 0.16f, 0.5f), white);
                }
                for (int w = 0; w < 7; w++)
                    Part(t, PrimitiveType.Cube, "front_window", new Vector3(-55.96f, y, -8.7f + w * 2.9f), new Vector3(0.08f, 1.1f, 1.05f), glass);
            }
            Part(t, PrimitiveType.Cube, "bridge_deck", new Vector3(-62f, D + 17.3f, 0), new Vector3(10.5f, 0.6f, 30f), white);
            Part(t, PrimitiveType.Cube, "wheelhouse", new Vector3(-62f, D + 18.6f, 0), new Vector3(9f, 2.6f, 22f), white);
            Part(t, PrimitiveType.Cube, "bridge_roof", new Vector3(-62f, D + 20.2f, 0), new Vector3(10.5f, 0.3f, 30f), white);
            for (int w = 0; w < 11; w++)
                Part(t, PrimitiveType.Cube, "bridge_window", new Vector3(-57.46f, D + 18.9f, -9.5f + w * 1.9f), new Vector3(0.08f, 1.35f, 1.55f), glass);
            foreach (float side in new[] { -1f, 1f })
                for (int w = 0; w < 4; w++)
                    Part(t, PrimitiveType.Cube, "bridge_side_window", new Vector3(-65f + w * 2f, D + 18.9f, side * 11.04f), new Vector3(1.6f, 1.35f, 0.08f), glass);
            Part(t, PrimitiveType.Cylinder, "radar_mast", new Vector3(-63f, D + 23f, 0), new Vector3(0.42f, 3f, 0.42f), white);
            Part(t, PrimitiveType.Cube, "radar_yard", new Vector3(-63f, D + 25.5f, 0), new Vector3(0.4f, 0.25f, 7f), white);
            Part(t, PrimitiveType.Cube, "funnel_casing", new Vector3(-76f, D + 5f, 0), new Vector3(6f, 10f, 7f), white);
            Part(t, PrimitiveType.Cube, "funnel", new Vector3(-76f, D + 13f, 0), new Vector3(5f, 6f, 5.5f), funnel);
            Part(t, PrimitiveType.Cube, "funnel_top", new Vector3(-76f, D + 16.3f, 0), new Vector3(5.1f, 0.6f, 5.6f), Mat("cs_black", new Color(0.04f, 0.04f, 0.04f), 0.3f, 0));
            // hatch covers + container stacks (merged into one mesh per colour)
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var cubeMesh = cube.GetComponent<MeshFilter>().sharedMesh;
            Object.DestroyImmediate(cube);
            string[] colours = { "container_red", "container_blue", "container_green", "container_grey", "container_orange", "container_white", "container_teal", "container_brown", "container_navy", "container_maroon" };
            var groups = new Dictionary<string, List<CombineInstance>>();
            var rng = new System.Random(4242);
            for (int b = 0; b < 9; b++)
            {
                float bx = -45f + b * 13.2f;
                // rows follow the local hull half-breadth (same law as LoftedHull, bow fullness 1.3) so nothing overhangs the bow
                float u = (bx + 6.5f + L / 2) / L;
                float bf = HalfBreadth(u, Bm, 2.4f, 1.5f) / (Bm / 2f);
                int rows = Mathf.Clamp(Mathf.FloorToInt((Bm * bf - 1.5f) / 2.5f), 1, 9);
                Part(t, PrimitiveType.Cube, "hatch_" + b, new Vector3(bx, D + 0.5f, 0), new Vector3(12.6f, 1.0f, rows * 2.5f + 0.5f), deckGrey);
                for (int r = 0; r < rows; r++)
                {
                    int tiers = new[] { 4, 4, 5, 5, 4, 4, 3, 2, 1 }[b] - (r == 0 || r == rows - 1 ? 1 : 0);
                    tiers = Mathf.Max(1, tiers);
                    for (int k = 0; k < tiers; k++)
                    {
                        var col = colours[rng.Next(colours.Length)];
                        var m = Matrix4x4.TRS(new Vector3(bx, D + 1.0f + 1.295f + k * 2.59f, -(rows - 1) * 1.25f + r * 2.5f), Quaternion.identity, new Vector3(12.19f, 2.59f, 2.44f));
                        if (!groups.TryGetValue(col, out var list)) groups[col] = list = new List<CombineInstance>();
                        list.Add(new CombineInstance { mesh = cubeMesh, transform = m });
                        // Real relief at the outer stack faces: vertical corrugations, top/bottom rails,
                        // corner posts and door locking bars, combined by colour to keep draw calls bounded.
                        var centre = m.MultiplyPoint3x4(Vector3.zero);
                        void Detail(Vector3 offset, Vector3 scale) => list.Add(new CombineInstance { mesh = cubeMesh,
                            transform = Matrix4x4.TRS(centre + offset, Quaternion.identity, scale) });
                        foreach (float side in new[] { -1f, 1f })
                        {
                            if ((side < 0 && r == 0) || (side > 0 && r == rows - 1))
                            {
                                for (float rib = -5.8f; rib < 6f; rib += 0.32f)
                                    Detail(new Vector3(rib, 0, side * 1.245f), new Vector3(0.075f, 2.4f, 0.06f));
                                foreach (float yy in new[] { -1.23f, 1.23f })
                                    Detail(new Vector3(0, yy, side * 1.25f), new Vector3(12.19f, 0.1f, 0.1f));
                            }
                            foreach (float zz in new[] { -0.85f, -0.3f, 0.3f, 0.85f })
                                Detail(new Vector3(side * 6.12f, 0, zz), new Vector3(0.06f, 2.35f, 0.055f));
                        }
                    }
                }
            }
            foreach (var kv in groups)
            {
                var mesh = new Mesh { name = "ship_containers_" + kv.Key, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                mesh.CombineMeshes(kv.Value.ToArray(), true, true);
                var path = $"{MeshDir}/ship_containers_{kv.Key}.asset";
                var old = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if (old != null) { EditorUtility.CopySerialized(mesh, old); mesh = old; } else AssetDatabase.CreateAsset(mesh, path);
                var go = new GameObject("containers_" + kv.Key);
                go.transform.SetParent(t, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                go.AddComponent<MeshRenderer>().sharedMaterial = theme != null ? theme.Get(kv.Key) : white;
            }
            VesselDetails.ContainerShip(t, deckGrey, white);
            return root;
        }

        /// <summary>~19 m Korean coastal fishing boat (연근해 어선): high bow, white hull with blue band, aft-mid wheelhouse, tall mast with
        /// lamps, stern gantry and net drum.</summary>
        public static GameObject BuildKoreanFishingBoat()
        {
            var root = new GameObject("korean_fishing_boat_model");
            var white = Mat("kf_white", new Color(0.88f, 0.89f, 0.88f), 0.45f, 0.1f);
            var blue = Mat("kf_blue", new Color(0.10f, 0.25f, 0.55f), 0.45f, 0.1f);
            var red = Mat("kf_red", new Color(0.60f, 0.10f, 0.08f), 0.4f, 0.1f);
            var glass = Mat("glass", new Color(0.06f, 0.08f, 0.10f), 0.9f, 0.1f);
            var lampMat = Mat("kf_lamp", new Color(0.95f, 0.85f, 0.5f), 0.6f, 0);
            Hull(root.transform, LoftedHull("hull_kfish_19m", 19f, 5.2f, 2.4f, 1.4f, 2f, 0.6f, 0.6f, 0.25f), white);
            var t = root.transform;
            Part(t, PrimitiveType.Cube, "sheer_band", new Vector3(-2.5f, 2.25f, 0), new Vector3(12.5f, 0.35f, 5.25f), blue);
            Part(t, PrimitiveType.Cube, "wheelhouse", new Vector3(-3.0f, 3.8f, 0), new Vector3(4.6f, 2.8f, 3.6f), white);
            Part(t, PrimitiveType.Cube, "wheelhouse_glass", new Vector3(-0.68f, 4.4f, 0), new Vector3(0.06f, 0.9f, 3.3f), glass);
            Part(t, PrimitiveType.Cube, "wheelhouse_glass_p", new Vector3(-3.0f, 4.4f, 1.82f), new Vector3(3.8f, 0.8f, 0.06f), glass);
            Part(t, PrimitiveType.Cube, "wheelhouse_glass_s", new Vector3(-3.0f, 4.4f, -1.82f), new Vector3(3.8f, 0.8f, 0.06f), glass);
            Part(t, PrimitiveType.Cube, "wheelhouse_roof", new Vector3(-3.0f, 5.3f, 0), new Vector3(5.0f, 0.2f, 4.0f), blue);
            Part(t, PrimitiveType.Cylinder, "main_mast", new Vector3(-1.5f, 8.2f, 0), new Vector3(0.22f, 2.9f, 0.22f), white);
            for (int i = 0; i < 3; i++)
            {
                Part(t, PrimitiveType.Cube, "mast_bar_" + i, new Vector3(-1.5f, 6.4f + i * 1.6f, 0), new Vector3(0.12f, 0.12f, 3.0f - i * 0.6f), white);
                foreach (var s in new[] { -1f, 1f })
                    Part(t, PrimitiveType.Sphere, $"lamp_{i}_{s}", new Vector3(-1.5f, 6.15f + i * 1.6f, s * (1.4f - i * 0.3f)), Vector3.one * 0.35f, lampMat);
            }
            Part(t, PrimitiveType.Cylinder, "fore_mast", new Vector3(6.8f, 4.6f, 0), new Vector3(0.15f, 1.6f, 0.15f), white);
            Part(t, PrimitiveType.Cube, "gantry_p", new Vector3(-8.3f, 4.0f, 1.9f), new Vector3(0.25f, 3.2f, 0.25f), blue);
            Part(t, PrimitiveType.Cube, "gantry_s", new Vector3(-8.3f, 4.0f, -1.9f), new Vector3(0.25f, 3.2f, 0.25f), blue);
            Part(t, PrimitiveType.Cube, "gantry_top", new Vector3(-8.3f, 5.6f, 0), new Vector3(0.3f, 0.3f, 4.0f), blue);
            Part(t, PrimitiveType.Cylinder, "net_drum", new Vector3(-6.4f, 3.2f, 0), new Vector3(1.4f, 1.3f, 1.4f), red, new Vector3(90, 0, 0));
            Part(t, PrimitiveType.Cube, "fish_hold_hatch", new Vector3(2.5f, 2.55f, 0), new Vector3(3f, 0.3f, 2.4f), blue);
            VesselDetails.KoreanFishingBoat(t, blue);
            return root;
        }

        /// <summary>Stockpile height field on a length x width deck patch: three cones (angle of repose ~33 deg), crest noise, a
        /// scooped face at +x (where the excavator works), rubble-scale noise; edges come down to the deck.</summary>
        static Mesh AggregatePile(float len, float wid, float peak, int seed)
        {
            const int nx = 72, nz = 28;
            var rnd = new System.Random(seed);
            var cones = new (float x, float z, float h, float r)[3];
            for (int i = 0; i < 3; i++)
                cones[i] = (-len * 0.3f + i * len * 0.27f + (float)(rnd.NextDouble() - 0.5) * 3f, (float)(rnd.NextDouble() - 0.5) * 2.5f,
                            peak * (0.75f + 0.25f * (float)rnd.NextDouble()), wid * (0.55f + 0.1f * (float)rnd.NextDouble()));
            var v = new Vector3[(nx + 1) * (nz + 1)]; var uv = new Vector2[v.Length];
            for (int j = 0; j <= nz; j++)
                for (int i = 0; i <= nx; i++)
                {
                    float x = -len / 2f + len * i / nx, z = -wid / 2f + wid * j / nz;
                    float h = 0f;
                    foreach (var c in cones)
                    {
                        float d = Mathf.Sqrt((x - c.x) * (x - c.x) * 0.55f + (z - c.z) * (z - c.z));
                        h = Mathf.Max(h, c.h - d * 0.65f);                          // ~33 deg repose
                    }
                    h += 0.35f * (ProcNoise.Fbm(x / len + 0.5f, z / wid + 0.5f, 4, 4, seed) - 0.5f);   // uneven crest / ridges
                    h += 0.12f * (ProcNoise.Hash(i, j, seed + 7) - 0.5f);                                // rubble
                    if (x > len * 0.28f) h -= Mathf.Clamp01((x - len * 0.28f) / 3f) * 1.4f * (0.6f + 0.4f * Mathf.Sin(z * 1.3f));   // dug face
                    float edge = Mathf.Min(Mathf.Min(x + len / 2f, len / 2f - x), Mathf.Min(z + wid / 2f, wid / 2f - z));
                    h = Mathf.Min(h, edge * 0.9f);
                    v[j * (nx + 1) + i] = new Vector3(x, Mathf.Max(h, 0.02f), z);
                    uv[j * (nx + 1) + i] = new Vector2(x / 3f, z / 3f);
                }
            var tri = new int[nx * nz * 6]; int k = 0;
            for (int j = 0; j < nz; j++)
                for (int i = 0; i < nx; i++)
                {
                    int a = j * (nx + 1) + i;
                    tri[k++] = a; tri[k++] = a + nx + 1; tri[k++] = a + 1; tri[k++] = a + 1; tri[k++] = a + nx + 1; tri[k++] = a + nx + 2;
                }
            var m = new Mesh { name = "barge_aggregate_pile", vertices = v, uv = uv, triangles = tri };
            m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }

        static Mesh SaveMeshAsset(Mesh m, string name)
        {
            var path = $"Assets/PortCCTVSimulator/Meshes/{name}.asset";
            var old = UnityEditor.AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (old != null) UnityEditor.AssetDatabase.DeleteAsset(path);
            UnityEditor.AssetDatabase.CreateAsset(m, path);
            return m;
        }

        /// <summary>~50 m flat-deck barge with an aggregate heap and a deck coaming (as in Korean harbour construction work).</summary>
        public static GameObject BuildBarge(Material aggregate)
        {
            var root = new GameObject("barge_model");
            var rust = Mat("barge_hull", new Color(0.30f, 0.20f, 0.15f), 0.35f, 0.3f);
            var deck = Mat("barge_deck", new Color(0.36f, 0.33f, 0.30f), 0.3f, 0.3f);
            var mesh = LoftedHull("hull_barge_50m", 50f, 14f, 3.2f, 6f, 6f, 0.5f, 0.0f, 0.0f);
            Hull(root.transform, mesh, rust);
            var t = root.transform;
            Part(t, PrimitiveType.Cube, "deck_plate", new Vector3(-1f, 3.25f, 0), new Vector3(44f, 0.1f, 13.4f), deck);
            Part(t, PrimitiveType.Cube, "coaming_p", new Vector3(-1f, 3.65f, 6.6f), new Vector3(44f, 0.8f, 0.25f), rust);
            Part(t, PrimitiveType.Cube, "coaming_s", new Vector3(-1f, 3.65f, -6.6f), new Vector3(44f, 0.8f, 0.25f), rust);
            // aggregate as a worked stockpile (reference R01, Wando): three overlapping cones with an uneven crest, a bucket-dug
            // face towards the excavator and a fine rubble surface, instead of one smooth half-ellipsoid
            var heap = new GameObject("aggregate_heap");
            heap.transform.SetParent(t, false);
            heap.transform.localPosition = new Vector3(-4f, 3.28f, 0f);
            heap.AddComponent<MeshFilter>().sharedMesh = SaveMeshAsset(AggregatePile(30f, 11.4f, 4.2f, 9071), "barge_aggregate_pile");
            heap.AddComponent<MeshRenderer>().sharedMaterial = aggregate;
            VesselDetails.Barge(t);
            return root;
        }
    }
}
