using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace PortCCTV
{
    /// <summary>One (renderer, sub-mesh) of a vessel prefab, classified by the bootstrap (Editor/VesselPaintAnalysis.cs).</summary>
    [Serializable]
    public class PaintSlot
    {
        /// <summary>Sibling-index path from the prefab root, e.g. "0/3/1" (names are not unique in some GLBs).</summary>
        public string renderer_path;
        public int submesh;
        public string material_name;
        /// <summary>hull | superstructure | keep</summary>
        public string role;
        /// <summary>Why the slot got its role (name rule, textured, too small, height, colour...).</summary>
        public string reason;
        public float area_m2;
        /// <summary>Area-weighted mean height, 0 = lowest vertex of the vessel, 1 = highest.</summary>
        public float height_frac;
        public float height_min_frac, height_max_frac;
        /// <summary>Area above the design waterline (default immersion).</summary>
        public float above_water_area_m2;
        /// <summary>Area between the waterline and 12 % of the vessel height above it (the topsides); picks the hull.</summary>
        public float topside_band_area_m2;
        /// <summary>Original base colour (sRGB, texture mean included for flattened textures).</summary>
        public Color original_srgb;
        public bool textured;
        /// <summary>Textured slots: sRGB mean of the albedo texture (the tint is chosen so that tint x mean = target).</summary>
        public Color texture_mean_srgb = Color.white;
    }

    /// <summary>Hull colours for the whole scenario. Older files: "original".</summary>
    [Serializable]
    public class AppearanceConfig
    {
        /// <summary>original | random. Per vessel `VesselPlacement.paint` overrides it.</summary>
        public string paint = "original";
        /// <summary>Random paint: each vessel's colours come from hash(paint_seed, object_id), so a vessel keeps its colours when
        /// others are added, removed or moved.</summary>
        public int paint_seed = 1;
    }

    /// <summary>What was applied to one placed vessel (written to labels.json).</summary>
    [Serializable]
    public class PaintRecord
    {
        /// <summary>original | random | fixed</summary>
        public string mode = "original";
        public string scheme = "";
        /// <summary>"#RRGGBB" (sRGB) or "" when the hull keeps its original colour.</summary>
        public string hull_srgb = "";
        public string superstructure_srgb = "";
        public int hull_slots;
        public int superstructure_slots;
        /// <summary>Share of the vessel's surface area (all slots) that is repainted when mode != original. Textured atlases,
        /// glass, metal, wood, decks, small trim and accents keep their look.</summary>
        public float repaintable_area_fraction;
    }

    public static class VesselPaint
    {
        public const string Original = "original", Random = "random";

        // sRGB paint colours (hand-picked marine paints; weights = how often they are drawn). Not survey data.
        struct Swatch { public string name; public Color c; public float w; public Swatch(string n, string hex, float w) { name = n; ColorUtility.TryParseHtmlString(hex, out c); this.w = w; } }

        static readonly Swatch[] LargeHull =
        {
            new Swatch("black", "#1E1F21", 2f), new Swatch("navy", "#1F2D4A", 2f), new Swatch("dark_blue", "#24467A", 1.2f),
            new Swatch("dark_red", "#6E1F1C", 1.5f), new Swatch("red_brown", "#8A3324", 1f), new Swatch("dark_green", "#1F4A35", 1f),
            new Swatch("grey", "#5E6468", 1.2f), new Swatch("blue_grey", "#3B5468", 1f), new Swatch("teal", "#1E5E62", 0.6f),
            new Swatch("orange", "#C2571F", 0.4f), new Swatch("light_grey", "#9DA3A6", 0.6f), new Swatch("white", "#DCDCD8", 0.5f),
        };
        static readonly Swatch[] LargeSuper =
        {
            new Swatch("white", "#E6E6E1", 4f), new Swatch("cream", "#E0D8C0", 1.5f), new Swatch("light_grey", "#BFC3C2", 1.5f),
            new Swatch("pale_yellow", "#D9CF9A", 0.4f),
        };
        static readonly Swatch[] SmallHull =
        {
            new Swatch("white", "#EDEDE8", 3f), new Swatch("cream", "#E3DAC2", 1f), new Swatch("light_grey", "#B8BCBD", 1f),
            new Swatch("navy", "#1F2D4A", 1.2f), new Swatch("blue", "#2A55A0", 1.2f), new Swatch("red", "#A8281E", 1f),
            new Swatch("dark_green", "#23533A", 0.8f), new Swatch("yellow", "#D9B22A", 0.5f), new Swatch("orange", "#D96A1E", 0.5f),
            new Swatch("black", "#202124", 0.6f), new Swatch("teal", "#25747A", 0.6f), new Swatch("sky_blue", "#6FA3C8", 0.6f),
        };
        static readonly Swatch[] SmallSuper =
        {
            new Swatch("white", "#EDEDE8", 4f), new Swatch("cream", "#E3DAC2", 1f), new Swatch("light_grey", "#C2C6C7", 1f),
            new Swatch("blue_grey", "#AFC0CC", 0.6f),
        };

        /// <summary>Vessels at least this long (m) use the merchant-ship palette.</summary>
        public const float LargeLengthM = 40f;

        public static bool IsValidMode(string m) => m == Original || m == Random || TryParseFixed(m, out _, out _);

        /// <summary>"hull=#RRGGBB;super=#RRGGBB" (either part optional).</summary>
        public static bool TryParseFixed(string s, out Color? hull, out Color? sup)
        {
            hull = null; sup = null;
            if (string.IsNullOrEmpty(s) || !s.Contains("=")) return false;
            foreach (var part in s.Split(';'))
            {
                var kv = part.Split('=');
                if (kv.Length != 2 || !ColorUtility.TryParseHtmlString(kv[1].Trim(), out var c)) return false;
                var k = kv[0].Trim();
                if (k == "hull") hull = c; else if (k == "super" || k == "superstructure") sup = c; else return false;
            }
            return hull.HasValue || sup.HasValue;
        }

        static ulong Hash(string s, int seed)
        {
            ulong h = 1469598103934665603UL ^ (ulong)(uint)seed * 0x9E3779B97F4A7C15UL;
            foreach (char ch in s) { h ^= ch; h *= 1099511628211UL; }
            h ^= h >> 33; h *= 0xff51afd7ed558ccdUL; h ^= h >> 33;
            return h;
        }

        static Swatch Pick(Swatch[] set, ulong h)
        {
            float total = set.Sum(s => s.w);
            float u = (float)((h >> 11) * (1.0 / (1UL << 53))) * total;
            foreach (var s in set) { if (u < s.w) return s; u -= s.w; }
            return set[set.Length - 1];
        }

        public static string Hex(Color c) => "#" + ColorUtility.ToHtmlStringRGB(c);

        /// <summary>Colours this vessel gets under a scenario (null = keep original).</summary>
        public static void Resolve(string mode, int seed, string objectId, float lengthM, out Color? hull, out Color? sup, out string scheme)
        {
            hull = null; sup = null; scheme = "";
            if (mode == Random)
            {
                bool large = lengthM >= LargeLengthM;
                var a = Pick(large ? LargeHull : SmallHull, Hash(objectId + "|hull", seed));
                var b = Pick(large ? LargeSuper : SmallSuper, Hash(objectId + "|super", seed));
                hull = a.c; sup = b.c; scheme = $"{(large ? "large" : "small")}:{a.name}/{b.name}";
            }
            else if (TryParseFixed(mode, out hull, out sup)) scheme = "fixed";
        }

        // same priority as the bootstrap analysis (Editor/VesselPaintAnalysis.FactorSrgb)
        static readonly int[] ColorIds = { Shader.PropertyToID("baseColorFactor"), Shader.PropertyToID("_BaseColor"), Shader.PropertyToID("diffuseFactor"), Shader.PropertyToID("_Color") };

        /// <summary>Repaints a freshly instantiated vessel. Material copies are cached per (source material, colour) in
        /// <paramref name="cache"/>; the caller destroys them.</summary>
        public static PaintRecord Apply(GameObject root, VesselAssetEntry entry, VesselPlacement p, AppearanceConfig app, Dictionary<(Material, Color32), Material> cache)
        {
            var rec = new PaintRecord();
            var slots = entry.paint_slots ?? new List<PaintSlot>();
            float total = slots.Sum(s => s.area_m2), paintable = slots.Where(s => s.role != "keep").Sum(s => s.area_m2);
            rec.repaintable_area_fraction = total > 0f ? paintable / total : 0f;
            string mode = string.IsNullOrEmpty(p.paint) ? (app?.paint ?? Original) : p.paint;
            float length = entry.geometry != null ? entry.geometry.bounds_max.x - entry.geometry.bounds_min.x : 0f;
            Resolve(mode, app?.paint_seed ?? 1, p.object_id + "|" + p.asset_id, length, out var hull, out var sup, out var scheme);
            if (!hull.HasValue && !sup.HasValue) return rec;
            rec.mode = mode == Random ? Random : "fixed";
            rec.scheme = scheme;
            if (hull.HasValue) rec.hull_srgb = Hex(hull.Value);
            if (sup.HasValue) rec.superstructure_srgb = Hex(sup.Value);
            foreach (var s in slots)
            {
                Color? target = s.role == "hull" ? hull : s.role == "superstructure" ? sup : null;
                if (!target.HasValue) continue;
                var r = Find(root.transform, s.renderer_path)?.GetComponent<Renderer>();
                if (r == null) continue;
                var mats = r.sharedMaterials;
                if (s.submesh >= mats.Length || mats[s.submesh] == null) continue;
                var src = mats[s.submesh];
                var key = (src, (Color32)target.Value);
                if (!cache.TryGetValue(key, out var m))
                {
                    m = new Material(src) { name = src.name + " (paint " + Hex(target.Value) + ")" };
                    var c = target.Value;
                    if (s.textured)
                    {
                        // multiply tint in linear so that tint x texture mean = target; the texture's detail stays
                        Color t = c.linear, mean = s.texture_mean_srgb.linear;
                        c = new Color(Mathf.Min(t.r / Mathf.Max(mean.r, 0.05f), 2f), Mathf.Min(t.g / Mathf.Max(mean.g, 0.05f), 2f), Mathf.Min(t.b / Mathf.Max(mean.b, 0.05f), 2f)).gamma;
                    }
                    foreach (var id in ColorIds) if (m.HasProperty(id)) { var a = m.GetColor(id).a; m.SetColor(id, new Color(c.r, c.g, c.b, a)); break; }
                    cache[key] = m;
                }
                mats[s.submesh] = m;
                r.sharedMaterials = mats;
                if (s.role == "hull") rec.hull_slots++; else rec.superstructure_slots++;
            }
            return rec;
        }

        public static Transform Find(Transform root, string path)
        {
            var t = root;
            if (string.IsNullOrEmpty(path)) return t;
            foreach (var part in path.Split('/'))
            {
                if (!int.TryParse(part, out int i) || i < 0 || i >= t.childCount) return null;
                t = t.GetChild(i);
            }
            return t;
        }

        public static string PathOf(Transform root, Transform t)
        {
            var parts = new List<string>();
            while (t != null && t != root) { parts.Add(t.GetSiblingIndex().ToString()); t = t.parent; }
            parts.Reverse();
            return string.Join("/", parts);
        }
    }
}
