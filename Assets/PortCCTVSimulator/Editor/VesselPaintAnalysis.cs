using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace PortCCTV.EditorTools
{
    /// <summary>
    /// Classifies every (renderer, sub-mesh) of a vessel prefab copy as hull, superstructure or keep, for runtime repainting
    /// (Runtime/VesselPaint.cs). Heuristic, from the geometry and material only:
    /// keep = glass / metal / wood / rubber / deck / sail / light / rope / container... by material name, textured atlases,
    /// transparent, or under 1.5 % of the surface; hull = the largest low slot (area-weighted height below 42 % of the vessel
    /// height) plus other low slots of the same colour; superstructure = the largest light, unsaturated slot above it plus
    /// same-coloured slots. Two-tone hulls, boot-tops, funnels and accents keep their original colour.
    /// </summary>
    public static class VesselPaintAnalysis
    {
        static readonly string[] KeepWords =
        {
            "glass", "glaz", "window", "steel", "alumin", "iron", "chrome", "brass", "metal", "rubber", "tyre", "tire", "wood", "teak",
            "oak", "leather", "vinyl", "cord", "rope", "lens", "lamp", "light", "nav_", "beacon", "deck", "non_slip", "container",
            "woven", "sailcloth", "canvas", "fabric", "cushion", "seat", "engine", "propeller", "flag", "logo", "decal", "grat",
            "ladder", "antifoul", "bottom", "rust", "detail", "funnel", "crate",
        };
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor"), GltfBaseId = Shader.PropertyToID("baseColorFactor");

        const float HullMaxHeight = 0.42f, SameColourMaxHeight = 0.5f, SuperMinHeight = 0.3f;
        const float MinSlotShare = 0.015f, HullMinShare = 0.04f, SuperMinShare = 0.02f, SameColour = 0.08f, WaterlineReach = 0.08f;

        /// <summary>Base (albedo) texture of any shader: HDRP Lit _BaseColorMap, glTF baseColorTexture / diffuseTexture, _MainTex.</summary>
        static Texture AlbedoTexture(Material m)
        {
            foreach (var n in m.GetTexturePropertyNames())
            {
                var ln = n.ToLowerInvariant();
                if (!(ln.Contains("basecolor") || ln.Contains("diffuse") || ln.Contains("albedo") || ln == "_maintex") || ln.Contains("normal")) continue;
                var t = m.GetTexture(n);
                if (t != null) return t;
            }
            return null;
        }

        public static Color FactorSrgb(Material m)
        {
            if (m.HasProperty(GltfBaseId)) return m.GetColor(GltfBaseId);
            if (m.HasProperty(BaseColorId)) return m.GetColor(BaseColorId);
            if (m.HasProperty("diffuseFactor")) return m.GetColor("diffuseFactor");
            if (m.HasProperty("_Color")) return m.GetColor("_Color");
            return Color.white;
        }

        /// <summary>sRGB-encoded mean (averaged in linear) of a texture on a 64x64 GPU downsample.</summary>
        static Color TextureMeanSrgb(Texture tex)
        {
            var rt = RenderTexture.GetTemporary(64, 64, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Graphics.Blit(tex, rt);
            var prev = RenderTexture.active; RenderTexture.active = rt;
            var t = new Texture2D(64, 64, TextureFormat.RGBA32, false, false);
            t.ReadPixels(new Rect(0, 0, 64, 64), 0, 0); t.Apply();
            RenderTexture.active = prev; RenderTexture.ReleaseTemporary(rt);
            var px = t.GetPixels(); UnityEngine.Object.DestroyImmediate(t);
            Vector4 lin = Vector4.zero;
            foreach (var p in px) lin += new Vector4(Mathf.GammaToLinearSpace(p.r), Mathf.GammaToLinearSpace(p.g), Mathf.GammaToLinearSpace(p.b), p.a);
            lin /= px.Length;
            return new Color(Mathf.LinearToGammaSpace(lin.x), Mathf.LinearToGammaSpace(lin.y), Mathf.LinearToGammaSpace(lin.z), lin.w);
        }

        /// <summary>Effective sRGB albedo (factor x texture mean, multiplied in linear).</summary>
        static Color BaseColor(Material m, out bool textured, out Color texMean)
        {
            var f = FactorSrgb(m);
            var tex = AlbedoTexture(m);
            textured = tex != null; texMean = Color.white;
            if (!textured) return f;
            texMean = TextureMeanSrgb(tex);
            var g = new Color(f.linear.r * texMean.linear.r, f.linear.g * texMean.linear.g, f.linear.b * texMean.linear.b, 1f).gamma;
            g.a = f.a; return g;
        }

        static float Lum(Color c) => 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;
        static float Sat(Color c) { float mx = Mathf.Max(c.r, Mathf.Max(c.g, c.b)), mn = Mathf.Min(c.r, Mathf.Min(c.g, c.b)); return mx <= 1e-4f ? 0f : (mx - mn) / mx; }
        static bool Close(Color a, Color b) => Mathf.Abs(a.r - b.r) < SameColour && Mathf.Abs(a.g - b.g) < SameColour && Mathf.Abs(a.b - b.b) < SameColour;

        public static List<PaintSlot> Analyze(GameObject root, VesselGeometry geom, string assetId, float immersionM, string[] overrides, out string note)
        {
            var slots = new List<PaintSlot>();
            var meanCache = new Dictionary<Material, (Color, bool, Color)>();
            bool Has(string kind, PaintSlot s) => overrides != null && overrides.Contains(kind + ":" + s.material_name);
            var w2l = root.transform.worldToLocalMatrix;
            float y0 = geom.render_min_y, H = Mathf.Max(geom.bounds_max.y - y0, 0.01f);
            float yWater = geom.hull_reference_min_y + immersionM;          // design waterline of the default placement
            float tWater = Mathf.Clamp01((yWater - y0) / H);
            // topside band: from the waterline up to 12 % of the vessel height (the freeboard seen from a camera); deck cargo,
            // tanks, cranes and superstructure have little area there even when they dip below the deck
            float yBand = yWater + 0.12f * H;
            foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                var mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                var mesh = mf.sharedMesh;
                var m = w2l * r.transform.localToWorldMatrix;
                var v = mesh.vertices.Select(p => m.MultiplyPoint3x4(p)).ToArray();
                var mats = r.sharedMaterials;
                for (int sub = 0; sub < mesh.subMeshCount && sub < mats.Length; sub++)
                {
                    var mat = mats[sub];
                    if (mat == null) continue;
                    var tri = mesh.GetTriangles(sub);
                    double area = 0, ay = 0, above = 0, band = 0; float ymin = float.MaxValue, ymax = float.MinValue;
                    for (int i = 0; i + 2 < tri.Length; i += 3)
                    {
                        Vector3 a = v[tri[i]], b = v[tri[i + 1]], c = v[tri[i + 2]];
                        float t = 0.5f * Vector3.Cross(b - a, c - a).magnitude;
                        float cy = (a.y + b.y + c.y) / 3f;
                        area += t; ay += t * cy; if (cy > yWater) above += t; if (cy > yWater && cy < yBand) band += t;
                        ymin = Mathf.Min(ymin, Mathf.Min(a.y, Mathf.Min(b.y, c.y))); ymax = Mathf.Max(ymax, Mathf.Max(a.y, Mathf.Max(b.y, c.y)));
                    }
                    if (area <= 0) continue;
                    if (!meanCache.TryGetValue(mat, out var info)) { var c0 = BaseColor(mat, out bool tx, out var tm); info = (c0, tx, tm); meanCache[mat] = info; }
                    var (col, textured, texMean) = info;
                    slots.Add(new PaintSlot
                    {
                        renderer_path = VesselPaint.PathOf(root.transform, r.transform), submesh = sub, material_name = mat.name,
                        area_m2 = (float)area, above_water_area_m2 = (float)above, topside_band_area_m2 = (float)band,
                        height_min_frac = Mathf.Clamp01((ymin - y0) / H), height_max_frac = Mathf.Clamp01((ymax - y0) / H), height_frac = Mathf.Clamp01(((float)(ay / area) - y0) / H), original_srgb = col, textured = textured, texture_mean_srgb = texMean,
                        role = "keep", reason = "",
                    });
                }
            }
            float A = slots.Sum(s => s.area_m2);
            var cand = new List<PaintSlot>();
            foreach (var s in slots)
            {
                var n = s.material_name.ToLowerInvariant();
                var pre = assetId.ToLowerInvariant() + "_";
                if (n.StartsWith(pre)) n = n.Substring(pre.Length);     // derived copies carry the asset id (e.g. sailing_yacht_...)
                var word = KeepWords.FirstOrDefault(w => n.Contains(w));
                if (Has("keep", s)) s.reason = "override keep";
                else if (Has("hull", s)) cand.Add(s);
                else if (word != null) s.reason = "name:" + word;
                // textured paint is repainted by a multiply tint (keeps dirt / decals) only when the texture is light and nearly grey
                else if (s.textured && (Lum(s.texture_mean_srgb) < 0.45f || Sat(s.texture_mean_srgb) > 0.3f)) s.reason = "textured";
                else if (s.textured && s.height_max_frac - s.height_min_frac > 0.6f) s.reason = "textured atlas spans the vessel";
                else if (s.original_srgb.a < 0.95f) s.reason = "transparent";
                else cand.Add(s);
            }
            // dominant slots must be large (by material, summed over its slots); same-coloured small parts follow them
            // the hull must reach down to the waterline (excludes deck cargo, tanks, cranes) and is chosen by its area above water
            // (so a mostly submerged bottom paint does not win over the topsides)
            bool Wet(PaintSlot s) => s.height_min_frac <= tWater + WaterlineReach;
            float bandTotal = slots.Sum(s => s.topside_band_area_m2);
            var forced = cand.Where(s => Has("hull", s)).OrderByDescending(s => s.topside_band_area_m2).FirstOrDefault();
            var hullDom = forced ?? cand.Where(s => !Has("nohull", s) && Wet(s) && s.height_frac < HullMaxHeight && s.topside_band_area_m2 >= 0.1f * bandTotal && cand.Where(o => o.material_name == s.material_name && Wet(o)).Sum(o => o.above_water_area_m2) >= HullMinShare * A).OrderByDescending(s => s.topside_band_area_m2).FirstOrDefault();
            if (hullDom != null)
                foreach (var s in cand.Where(s => !Has("nohull", s) && s.height_frac < SameColourMaxHeight && Close(s.original_srgb, hullDom.original_srgb) && (!s.textured || Wet(s))))
                { s.role = "hull"; s.reason = s == hullDom ? "largest low slot" : "same colour as hull, low"; }
            var rest = cand.Where(s => s.role == "keep").ToList();
            var supDom = rest.Where(s => s.height_frac >= SuperMinHeight && rest.Where(o => o.material_name == s.material_name && o.height_frac >= SuperMinHeight).Sum(o => o.area_m2) >= SuperMinShare * A && Lum(s.original_srgb) >= 0.35f && Sat(s.original_srgb) < 0.35f)
                             .OrderByDescending(s => s.area_m2).FirstOrDefault();
            if (supDom != null)
                foreach (var s in rest.Where(s => s.height_frac >= SuperMinHeight && Close(s.original_srgb, supDom.original_srgb)))
                { s.role = "superstructure"; s.reason = s == supDom ? "largest light upper slot" : "same colour as superstructure"; }
            foreach (var s in cand.Where(s => s.role == "keep" && string.IsNullOrEmpty(s.reason)))
                s.reason = s.area_m2 < MinSlotShare * A ? "small" : s.height_frac < HullMaxHeight ? "low, other colour (boot-top / accent)" : "upper, other colour (accent)";
            float hullA = slots.Where(s => s.role == "hull").Sum(s => s.area_m2), supA = slots.Where(s => s.role == "superstructure").Sum(s => s.area_m2);
            note = $"slots={slots.Count} hull={slots.Count(s => s.role == "hull")} ({hullA / Mathf.Max(A, 1e-6f):P0} of area) " +
                   $"super={slots.Count(s => s.role == "superstructure")} ({supA / Mathf.Max(A, 1e-6f):P0}) textured_kept={slots.Count(s => s.reason == "textured")}";
            return slots;
        }
    }
}
