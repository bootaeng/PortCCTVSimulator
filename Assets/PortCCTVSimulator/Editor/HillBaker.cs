using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace PortCCTV.EditorTools
{
    /// <summary>
    /// Bakes one albedo + normal texture per procedural hill mesh (hill_0..3, forest and meadow variants) from the same
    /// ProcNoise.HillShape height field the mesh uses: rock on steep slopes / the shore band / outcrops, meadow on gentle
    /// low ground (or everywhere for the camera-hill variant), synthesised forest canopy elsewhere, with a cavity term so
    /// gullies read darker than ridges. Sources: Tools/make_canopy_texture.py / make_meadow_texture.py outputs and the
    /// Poly Haven CC0 aerial_rocks_02 scan. UV-mapped (not triplanar); a tiled detail map adds relief close to the camera.
    /// </summary>
    public static class HillBaker
    {
        const string Root = "Assets/PortCCTVSimulator";
        const string Dir = Root + "/Textures/Hills";
        const string MatDir = Root + "/Materials";
        const string Ver = "v10";   // v8: mask map so the canopy detail (crown relief) only lies on the forest layer
        const int N = 1024;
        const float AspectK = 0.3f;           // assumed sy/sx of hill instances for the baked macro normal
        public enum Variant { Forest, ForestLarge, Meadow }
        // texture repeats across the unit hill; "large" is for instances wider than ~700 m so crowns/rocks keep a sane size
        static float CanopyTiles(Variant v) => v == Variant.ForestLarge ? 6f : 2.5f;
        static float MeadowTiles(Variant v) => v == Variant.ForestLarge ? 10f : 4f;
        static float RockTiles(Variant v) => v == Variant.ForestLarge ? 14f : 6f;
        static Vector3 _canopyMean, _meadowMean;

        static Color[] _canopy, _meadow, _rock, _canopyN, _meadowN; static int _cs, _ms, _rs, _cns, _mns;

        static Color[] LoadFile(string path, out int size)
        {
            var t = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            if (!t.LoadImage(File.ReadAllBytes(path))) throw new InvalidDataException("cannot decode " + path);
            size = t.width; var px = t.GetPixels(); Object.DestroyImmediate(t); return px;
        }

        static void LoadSources()
        {
            if (_canopy != null) return;
            _canopy = LoadFile(Root + "/Textures/Canopy/T_canopy_albedo.png", out _cs);
            _canopyN = LoadFile(Root + "/Textures/Canopy/T_canopy_normal.png", out _cns);
            _meadow = LoadFile(Root + "/Textures/Canopy/T_meadow_albedo.png", out _ms);
            _meadowN = LoadFile(Root + "/Textures/Canopy/T_meadow_normal.png", out _mns);
            _rock = LoadFile(Root + "/ThirdParty/PolyHavenCC0/textures/aerial_rocks_02/aerial_rocks_02_diff_2k.jpg", out _rs);
            var acc = Vector3.zero; int n = 0;
            for (int k = 0; k < _canopy.Length; k += 97) { var c = _canopy[k]; acc += new Vector3(Mathf.GammaToLinearSpace(c.r), Mathf.GammaToLinearSpace(c.g), Mathf.GammaToLinearSpace(c.b)); n++; }
            _canopyMean = acc / n;
            acc = Vector3.zero; n = 0;
            for (int k = 0; k < _meadow.Length; k += 97) { var c = _meadow[k]; acc += new Vector3(Mathf.GammaToLinearSpace(c.r), Mathf.GammaToLinearSpace(c.g), Mathf.GammaToLinearSpace(c.b)); n++; }
            _meadowMean = acc / n;
        }

        /// <summary>Bilinear, wrapping sample; returns linear-light RGB from an sRGB-encoded source.</summary>
        static Vector3 Sample(Color[] px, int size, float u, float v)
        {
            u = (u % 1f + 1f) % 1f; v = (v % 1f + 1f) % 1f;
            float x = u * size - 0.5f, y = v * size - 0.5f;
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y); float fx = x - x0, fy = y - y0;
            Vector3 P(int xi, int yi)
            {
                var c = px[((yi % size + size) % size) * size + ((xi % size + size) % size)];
                return new Vector3(Mathf.GammaToLinearSpace(c.r), Mathf.GammaToLinearSpace(c.g), Mathf.GammaToLinearSpace(c.b));
            }
            return Vector3.Lerp(Vector3.Lerp(P(x0, y0), P(x0 + 1, y0), fx), Vector3.Lerp(P(x0, y0 + 1), P(x0 + 1, y0 + 1), fx), fy);
        }

        static float Smooth(float a, float b, float x) { float t = Mathf.Clamp01((x - a) / (b - a)); return t * t * (3 - 2 * t); }

        static Texture2D Import(string path, bool normal, bool srgb)
        {
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            imp.sRGBTexture = srgb && !normal;
            imp.wrapMode = TextureWrapMode.Clamp;
            imp.anisoLevel = 8; imp.maxTextureSize = 2048; imp.mipmapEnabled = true;
            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>HDRP detail map (R albedo, G normal.y, B smoothness, A normal.x) from a canopy/meadow albedo+normal pair, half size.</summary>
        static Texture2D DetailMap(string name, Color[] alb, int asz, Color[] nrm, int nsz)
        {
            var path = $"{Dir}/T_detail_{name}_{Ver}.png";
            if (!File.Exists(path))
            {
                int S = 1024; var t = new Texture2D(S, S, TextureFormat.RGBA32, false, true);
                var px = new Color[S * S];
                for (int y = 0; y < S; y++)
                    for (int x = 0; x < S; x++)
                    {
                        float u = (x + 0.5f) / S, v = (y + 0.5f) / S;
                        var a = alb[Mathf.Min((int)(v * asz), asz - 1) * asz + Mathf.Min((int)(u * asz), asz - 1)];
                        var n = nrm[Mathf.Min((int)(v * nsz), nsz - 1) * nsz + Mathf.Min((int)(u * nsz), nsz - 1)];
                        float grey = 0.299f * a.r + 0.587f * a.g + 0.114f * a.b;
                        px[y * S + x] = new Color(grey, n.g, 0.5f, n.r);
                    }
                t.SetPixels(px); Directory.CreateDirectory(Dir); File.WriteAllBytes(path, t.EncodeToPNG()); Object.DestroyImmediate(t);
            }
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.textureType = TextureImporterType.Default; imp.sRGBTexture = false; imp.wrapMode = TextureWrapMode.Repeat;
            imp.anisoLevel = 8; imp.mipmapEnabled = true; imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>Material for hill mesh index i (forest, or meadow for hills right under a camera). Bakes on first use.</summary>
        public static Material Material(int i, Variant variant)
        {
            bool meadow = variant == Variant.Meadow;
            string key = $"hill_{(meadow ? "m_" : variant == Variant.ForestLarge ? "L_" : "")}{i}";
            // camera hills bake their village (roads / pads / terraces) from the registered HillSpec: the texture name carries its hash
            string texKey = meadow && HillFeatures.Specs.TryGetValue(i, out var spec0) ? key + "_" + spec0.Hash : key;
            string albPath = $"{Dir}/T_{texKey}_albedo_{Ver}.png", nrmPath = $"{Dir}/T_{texKey}_normal_{Ver}.png", mskPath = $"{Dir}/T_{texKey}_mask_{Ver}.png";
            if (!File.Exists(albPath) || !File.Exists(nrmPath) || !File.Exists(mskPath)) Bake(i, variant, albPath, nrmPath, mskPath);
            LoadSources();
            var alb = Import(albPath, false, true); var nrm = Import(nrmPath, true, false); var msk = Import(mskPath, false, false);
            var det = DetailMap("canopy", _canopy, _cs, _canopyN, _cns);   // camera hills are forest with village clearings too
            var lit = Shader.Find("HDRP/Lit");
            var path = $"{MatDir}/M_{key}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(lit); AssetDatabase.CreateAsset(m, path); }
            m.shader = lit;
            m.SetColor("_BaseColor", Color.white);
            m.SetFloat("_Smoothness", meadow ? 0.15f : 0.10f);
            // mask map: R metallic 0, G AO 1, B detail mask = forest layer weight, A smoothness 1 (remapped to the value above).
            // Without it the canopy detail map (crown relief) also covered the shore rock / scrub band and read as rows of domes.
            m.SetTexture("_MaskMap", msk);
            m.SetFloat("_SmoothnessRemapMin", meadow ? 0.15f : 0.10f); m.SetFloat("_SmoothnessRemapMax", meadow ? 0.15f : 0.10f);
            m.SetFloat("_AORemapMin", 0f); m.SetFloat("_AORemapMax", 1f); m.SetFloat("_MetallicRemapMin", 0f); m.SetFloat("_MetallicRemapMax", 0f);
            m.SetFloat("_Metallic", 0f);
            m.SetFloat("_UVBase", 0f);
            m.SetTexture("_BaseColorMap", alb);
            m.SetTexture("_NormalMap", nrm);
            m.SetFloat("_NormalScale", 1f);
            m.SetTexture("_DetailMap", det);
            float tiles = CanopyTiles(variant);
            m.SetTextureScale("_DetailMap", new Vector2(tiles, tiles));
            m.SetFloat("_UVDetail", 0f);
            m.SetFloat("_LinkDetailsWithBase", 0f);
            m.SetFloat("_DetailAlbedoScale", 0.3f);
            m.SetFloat("_DetailNormalScale", meadow ? 0.35f : 0.35f);   // crown relief at full strength read as domes wherever no tree stood on it
            m.SetFloat("_DetailSmoothnessScale", 0f);
            HDMaterial.ValidateMaterial(m);
            EditorUtility.SetDirty(m);
            return m;
        }

        // ------------------------------------------------------------------ village overlay (from HillFeatures, shared with the runtime)
        enum Ov : byte { None, Asphalt, Shoulder, Lane, Pad, Stair }

        class Overlay
        {
            public Ov[] type; public float[] terrace; public int[] band; public float[] frac;
            public float[] laneDist;   // metres to the nearest lane (camera hills only; float.MaxValue elsewhere)
        }

        static float DistSeg(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a; float l2 = ab.sqrMagnitude;
            float t = l2 < 1e-9f ? 0f : Mathf.Clamp01(Vector2.Dot(p - a, ab) / l2);
            return (p - (a + ab * t)).magnitude;
        }

        /// <summary>Rasterises the hill's lanes, house pads and terraced-field sector into per-texel overlay codes. Widths are in
        /// metres: camera hills use the registered instance scale, other variants an assumed 500 m (1200 m for the large tiling).</summary>
        static Overlay BuildOverlay(int i, Variant variant, int n)
        {
            var ov = new Overlay { type = new Ov[n * n], terrace = new float[n * n], band = new int[n * n], frac = new float[n * n], laneDist = new float[n * n] };
            for (int k = 0; k < ov.laneDist.Length; k++) ov.laneDist[k] = float.MaxValue;
            HillFeatures.Village vil = null; float su, sw;
            if (variant == Variant.Meadow && HillFeatures.Specs.TryGetValue(i, out var spec)) { vil = HillFeatures.Get(spec); su = spec.sx; sw = spec.sz; }
            else { su = sw = variant == Variant.ForestLarge ? 1200f : 500f; }
            var lanes = new System.Collections.Generic.List<HillFeatures.Lane>();
            if (vil != null) lanes.AddRange(vil.lanes);
            else
            {
                var ring = HillFeatures.RingRoad(i, 0.12f);
                if (ring != null) lanes.Add(new HillFeatures.Lane { type = HillFeatures.LaneType.MainRoad, pts = ring, closed = true });
            }
            var dist = new float[n * n]; for (int k = 0; k < dist.Length; k++) dist[k] = float.MaxValue;
            foreach (var l in lanes)
            {
                float width = vil != null ? l.WidthM : 5f;
                float half = width / 2f + (l.type == HillFeatures.LaneType.MainRoad ? 1.0f : 0.3f);
                float reach = vil != null ? 34f : half;      // camera hills: also a coarse distance field for the clearing around the village
                int segs = l.closed ? l.pts.Count : l.pts.Count - 1;
                for (int k = 0; k < segs; k++)
                {
                    var a = l.pts[k]; var b = l.pts[(k + 1) % l.pts.Count];
                    var am = new Vector2(a.x * su, a.y * sw); var bm = new Vector2(b.x * su, b.y * sw);
                    int x0 = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(a.x, b.x) - reach / su) * n)), x1 = Mathf.Min(n - 1, Mathf.CeilToInt((Mathf.Max(a.x, b.x) + reach / su) * n));
                    int y0 = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(a.y, b.y) - reach / sw) * n)), y1 = Mathf.Min(n - 1, Mathf.CeilToInt((Mathf.Max(a.y, b.y) + reach / sw) * n));
                    for (int y = y0; y <= y1; y++)
                        for (int x = x0; x <= x1; x++)
                        {
                            var pm = new Vector2((x + 0.5f) / n * su, (y + 0.5f) / n * sw);
                            float d = DistSeg(pm, am, bm);
                            int idx = y * n + x;
                            if (d < ov.laneDist[idx]) ov.laneDist[idx] = d;
                            if (d >= half) continue;
                            Ov t = l.type == HillFeatures.LaneType.MainRoad ? (d < width / 2f ? Ov.Asphalt : Ov.Shoulder)
                                  : l.type == HillFeatures.LaneType.Lane ? Ov.Lane : Ov.Stair;
                            // asphalt wins over shoulder, roads win over pads
                            if (ov.type[idx] == Ov.Asphalt || (ov.type[idx] == Ov.Lane && t == Ov.Shoulder) || (ov.type[idx] == Ov.Stair && t == Ov.Shoulder)) continue;
                            if (d < dist[idx] || ov.type[idx] == Ov.None || ov.type[idx] == Ov.Shoulder) { dist[idx] = d; ov.type[idx] = t; }
                        }
                }
            }
            if (vil == null) return ov;
            foreach (var h in vil.houses)
            {
                var hk = HillFeatures.HouseKinds[h.kind];
                float rad = (Mathf.Max(hk.w, hk.d) * h.scale / 2f + 2f);
                int x0 = Mathf.Max(0, Mathf.FloorToInt((h.uv.x - rad / su) * n)), x1 = Mathf.Min(n - 1, Mathf.CeilToInt((h.uv.x + rad / su) * n));
                int y0 = Mathf.Max(0, Mathf.FloorToInt((h.uv.y - rad / sw) * n)), y1 = Mathf.Min(n - 1, Mathf.CeilToInt((h.uv.y + rad / sw) * n));
                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                    {
                        int idx = y * n + x;
                        if (ov.type[idx] != Ov.None && ov.type[idx] != Ov.Shoulder) continue;
                        var pm = new Vector2((x + 0.5f) / n * su, (y + 0.5f) / n * sw);
                        if (vil.InPad(h, pm, 1.5f)) ov.type[idx] = Ov.Pad;
                        else if (vil.InPad(h, pm, 9f)) ov.laneDist[idx] = Mathf.Min(ov.laneDist[idx], 4f);   // yard around the house
                    }
            }
            // terraced fields either side of the village sector, in a height band just above the flat land
            float sea = vil.spec.VillageAngleDeg, bandH = 1.8f / vil.spec.sy;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    int idx = y * n + x;
                    float u = (x + 0.5f) / n, w = (y + 0.5f) / n;
                    float ang = Mathf.Atan2(w - 0.5f, u - 0.5f) * Mathf.Rad2Deg;
                    float da = Mathf.Abs(Mathf.DeltaAngle(ang, sea));
                    if (da < vil.fieldFromDeg - 2f || da > vil.fieldToDeg + 2f) continue;
                    float hc = ProcNoise.HillHeight(i, u, w);
                    if (hc < vil.fieldLoUnit || hc > vil.fieldHiUnit + 0.01f) continue;
                    float t = Smooth(vil.fieldFromDeg - 2f, vil.fieldFromDeg + 5f, da) * Smooth(vil.fieldToDeg + 2f, vil.fieldToDeg - 8f, da)
                            * Smooth(vil.fieldLoUnit, vil.fieldLoUnit + 0.004f, hc) * Smooth(vil.fieldHiUnit + 0.01f, vil.fieldHiUnit - 0.012f, hc);
                    // terraces only where the real slope (metres) is below ~1:1
                    float e = 1f / n;
                    float gx = (ProcNoise.HillHeight(i, u + e, w) - ProcNoise.HillHeight(i, u - e, w)) / (2 * e) / su * vil.spec.sy;
                    float gy = (ProcNoise.HillHeight(i, u, w + e) - ProcNoise.HillHeight(i, u, w - e)) / (2 * e) / sw * vil.spec.sy;
                    t *= Smooth(1.0f, 0.55f, Mathf.Sqrt(gx * gx + gy * gy));
                    if (t <= 0.01f) continue;
                    float bb = (hc - vil.fieldLoUnit) / bandH;
                    ov.terrace[idx] = t; ov.band[idx] = Mathf.FloorToInt(bb); ov.frac[idx] = bb - Mathf.Floor(bb);
                }
            return ov;
        }

        static void Bake(int i, Variant variant, string albPath, string nrmPath, string mskPath)
        {
            bool meadow = variant == Variant.Meadow;
            float ct = CanopyTiles(variant), mt = MeadowTiles(variant), rt = RockTiles(variant);
            LoadSources();
            var ov = BuildOverlay(i, variant, N);
            int seed = 2000 + i;
            var h = new float[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                    h[y * N + x] = ProcNoise.HillHeight(i, (x + 0.5f) / N, (y + 0.5f) / N, !meadow);
            // box blur (radius 6) for the cavity term
            var tmp = new float[N * N]; var blur = new float[N * N]; const int R = 6;
            for (int y = 0; y < N; y++)
            {
                float s = 0; for (int x = -R; x <= R; x++) s += h[y * N + Mathf.Clamp(x, 0, N - 1)];
                for (int x = 0; x < N; x++)
                {
                    tmp[y * N + x] = s / (2 * R + 1);
                    s += h[y * N + Mathf.Clamp(x + R + 1, 0, N - 1)] - h[y * N + Mathf.Clamp(x - R, 0, N - 1)];
                }
            }
            for (int x = 0; x < N; x++)
            {
                float s = 0; for (int y = -R; y <= R; y++) s += tmp[Mathf.Clamp(y, 0, N - 1) * N + x];
                for (int y = 0; y < N; y++)
                {
                    blur[y * N + x] = s / (2 * R + 1);
                    s += tmp[Mathf.Clamp(y + R + 1, 0, N - 1) * N + x] - tmp[Mathf.Clamp(y - R, 0, N - 1) * N + x];
                }
            }
            var albedo = new Color32[N * N]; var normal = new Color32[N * N]; var mask = new Color32[N * N];
            float du = 1f / N;
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    int idx = y * N + x;
                    float u = (x + 0.5f) / N, w = (y + 0.5f) / N;
                    float hc = h[idx];
                    float gx = (h[y * N + Mathf.Min(x + 1, N - 1)] - h[y * N + Mathf.Max(x - 1, 0)]) / (2 * du);
                    float gy = (h[Mathf.Min(y + 1, N - 1) * N + x] - h[Mathf.Max(y - 1, 0) * N + x]) / (2 * du);
                    float g = Mathf.Sqrt(gx * gx + gy * gy);
                    // --- layer weights
                    float shoreRock = Smooth(0.065f, 0.035f, hc);
                    float shoreScrub = Smooth(0.13f, 0.07f, hc) * (1f - shoreRock);              // coastal scrub / grass above the rocks
                    float slopeRock = Smooth(4.5f, 7.5f, g);
                    float outcrop = Mathf.Clamp01((ProcNoise.Fbm(u, w, 6, 3, seed + 31) - 0.74f) * 8f);
                    float rock = Mathf.Clamp01(Mathf.Max(shoreRock, Mathf.Max(slopeRock, outcrop)));
                    float clearing = 0.7f * Mathf.Clamp01((ProcNoise.Fbm(u, w, 3, 3, seed + 23) - 0.66f) * 6f) * Smooth(2.0f, 0.8f, g) * Smooth(0.10f, 0.16f, hc);
                    if (meadow) clearing = Mathf.Max(clearing, Smooth(30f, 10f, ov.laneDist[idx]) * (0.75f + 0.25f * ProcNoise.Fbm(u, w, 12, 2, seed + 27)));
                    float mead = (1f - rock) * Mathf.Max(clearing, shoreScrub * (0.65f + 0.35f * ProcNoise.Fbm(u, w, 10, 2, seed + 29)));
                    float forest = 1f - rock - mead;
                    // Korean coast: dark grey-brown rock, mid-dark green forest, green (not straw) meadow
                    // canopy contrast is reduced (blend towards its mean): seen from afar a forest is a fairly even dark green
                    var can = Vector3.Lerp(_canopyMean, Sample(_canopy, _cs, u * ct, w * ct), 0.55f);
                    var meadCol = Vector3.Scale(Sample(_meadow, _ms, u * mt, w * mt), new Vector3(0.80f, 0.95f, 0.70f));
                    // the shore band is low coastal scrub (darker olive green), not a mown meadow: it read straw-yellow from above
                    // and its grass-clump pattern is flattened (the clumps tiled into rows of domes along the shore)
                    if (shoreScrub > clearing)
                    {
                        var flatM = Vector3.Lerp(_meadowMean, Sample(_meadow, _ms, u * mt, w * mt), 0.3f);
                        var scrub = Vector3.Lerp(Vector3.Scale(flatM, new Vector3(0.80f, 0.95f, 0.70f)), Vector3.Scale(_canopyMean, new Vector3(1.25f, 1.30f, 1.0f)), 0.6f);
                        meadCol = Vector3.Lerp(meadCol, scrub, Mathf.Clamp01(shoreScrub * 1.5f));
                    }
                    var col = Vector3.Scale(Sample(_rock, _rs, u * rt, w * rt), new Vector3(0.34f, 0.32f, 0.30f)) * rock
                            + meadCol * mead
                            + Vector3.Scale(can, new Vector3(1.10f, 1.25f, 1.00f)) * forest;
                    // cavity: gullies darker than ridges; slight altitude brightening
                    float cav = Mathf.Clamp01(0.5f + (hc - blur[idx]) * 6f);
                    float mult = (0.72f + 0.40f * cav) * (0.95f + 0.10f * Mathf.Clamp01(hc));
                    col *= mult;
                    bool flat = false;
                    // village overlay: roads, lanes, house pads, terraced fields (same HillFeatures layout the runtime places houses on)
                    switch (ov.type[idx])
                    {
                        case Ov.Asphalt: col = new Vector3(0.075f, 0.075f, 0.08f) * (0.85f + 0.3f * ProcNoise.Fbm(u, w, 40, 2, seed + 41)); flat = true; break;
                        case Ov.Shoulder: col = new Vector3(0.30f, 0.24f, 0.16f) * (0.8f + 0.4f * ProcNoise.Fbm(u, w, 40, 2, seed + 43)); break;
                        case Ov.Lane: col = new Vector3(0.40f, 0.39f, 0.36f) * (0.9f + 0.2f * ProcNoise.Fbm(u, w, 40, 2, seed + 45)); flat = true; break;
                        case Ov.Pad: col = new Vector3(0.44f, 0.43f, 0.40f) * (0.9f + 0.2f * ProcNoise.Fbm(u, w, 40, 2, seed + 47)); flat = true; break;
                        case Ov.Stair: col = new Vector3(0.46f, 0.45f, 0.42f) * (0.85f + 0.3f * ProcNoise.Fbm(u, w, 60, 1, seed + 49)); flat = true; break;
                        default:
                            if (ov.terrace[idx] > 0f)
                            {
                                int b = ((ov.band[idx] % 3) + 3) % 3;
                                var tc = b == 0 ? new Vector3(0.30f, 0.23f, 0.15f) : b == 1 ? new Vector3(0.19f, 0.33f, 0.12f) : new Vector3(0.35f, 0.36f, 0.17f);
                                tc *= 0.85f + 0.3f * ProcNoise.Fbm(u, w, 30, 2, seed + 51);
                                if (ov.frac[idx] < 0.12f) tc *= 0.4f;           // terrace riser in shadow
                                col = Vector3.Lerp(col, tc * mult, ov.terrace[idx]);
                            }
                            break;
                    }
                    albedo[idx] = new Color(Mathf.LinearToGammaSpace(Mathf.Clamp01(col.x)), Mathf.LinearToGammaSpace(Mathf.Clamp01(col.y)), Mathf.LinearToGammaSpace(Mathf.Clamp01(col.z)), 1f);
                    float det = flat ? 0f : Mathf.Clamp01(forest * (1f - (ov.terrace[idx] > 0f ? ov.terrace[idx] : 0f)));
                    mask[idx] = new Color32(0, 255, (byte)Mathf.RoundToInt(det * 255f), 255);
                    var n = flat ? Vector3.forward : new Vector3(-gx * AspectK, -gy * AspectK, 1f).normalized;
                    normal[idx] = new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f, 1f);
                }
            Directory.CreateDirectory(Dir);
            var ta = new Texture2D(N, N, TextureFormat.RGBA32, false, false); ta.SetPixels32(albedo); File.WriteAllBytes(albPath, ta.EncodeToPNG()); Object.DestroyImmediate(ta);
            var tn = new Texture2D(N, N, TextureFormat.RGBA32, false, true); tn.SetPixels32(normal); File.WriteAllBytes(nrmPath, tn.EncodeToPNG()); Object.DestroyImmediate(tn);
            var tm = new Texture2D(N, N, TextureFormat.RGBA32, false, true); tm.SetPixels32(mask); File.WriteAllBytes(mskPath, tm.EncodeToPNG()); Object.DestroyImmediate(tm);
            Debug.Log($"[PortCCTV] baked hill texture {albPath}");
        }
    }
}
