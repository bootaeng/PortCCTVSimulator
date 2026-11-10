using System.Collections.Generic;
using UnityEngine;

namespace PortCCTV
{
    /// <summary>
    /// Body paint of background cars and vans (never labelled). The vehicle packs come in saturated demo colours (red, lime,
    /// sky blue); Korean car parks and streets are mostly white, grey/silver and black (reference R04/R05,
    /// docs/detail_review_20261109). The body-paint material of each instance (car_a "Body*", car_b "CarPaint*", van_a "Paint*")
    /// is swapped for a cached copy in a colour picked from a fixed share table by a hash of the element id, so the colour of a
    /// given car does not depend on the builder RNG. Glass, lights, tyres and trim keep their materials.
    /// </summary>
    public static class CarPaint
    {
        static readonly (float share, Color c)[] Table =
        {
            (0.24f, new Color(0.93f, 0.93f, 0.92f)),    // white
            (0.09f, new Color(0.86f, 0.85f, 0.81f)),    // pearl white
            (0.12f, new Color(0.67f, 0.68f, 0.69f)),    // silver
            (0.11f, new Color(0.43f, 0.44f, 0.46f)),    // grey
            (0.07f, new Color(0.23f, 0.24f, 0.25f)),    // dark grey
            (0.22f, new Color(0.04f, 0.04f, 0.05f)),    // black
            (0.05f, new Color(0.10f, 0.14f, 0.26f)),    // dark blue
            (0.03f, new Color(0.52f, 0.07f, 0.07f)),    // dark red
            (0.03f, new Color(0.62f, 0.56f, 0.46f)),    // beige
            (0.04f, Color.clear),                       // keep the model's own colour
        };
        static readonly int[] ColorIds = { Shader.PropertyToID("baseColorFactor"), Shader.PropertyToID("_BaseColor"), Shader.PropertyToID("_Color") };
        static readonly Dictionary<(Material, Color32), Material> Cache = new Dictionary<(Material, Color32), Material>();

        static bool IsBody(string mat) => mat.StartsWith("Body") || mat.StartsWith("CarPaint") || mat.StartsWith("Paint");

        public static Color? PickFor(string id)
        {
            uint h = 2166136261u; foreach (var ch in id) h = (h ^ ch) * 16777619u;
            float u = (h % 100000u) / 100000f, acc = 0f;
            foreach (var (share, c) in Table) { acc += share; if (u < acc) return c.a == 0f ? (Color?)null : c; }
            return null;
        }

        public static void Apply(GameObject go, string id)
        {
            var target = PickFor(id);
            if (!target.HasValue) return;
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials; bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    var src = mats[i];
                    if (src == null || !IsBody(src.name)) continue;
                    var key = (src, (Color32)target.Value);
                    if (!Cache.TryGetValue(key, out var m) || m == null)
                    {
                        m = new Material(src) { name = src.name + " (car paint)" };
                        foreach (var cid in ColorIds) if (m.HasProperty(cid)) { var a = m.GetColor(cid).a; m.SetColor(cid, new Color(target.Value.r, target.Value.g, target.Value.b, a)); break; }
                        Cache[key] = m;
                    }
                    mats[i] = m; changed = true;
                }
                if (changed) r.sharedMaterials = mats;
            }
        }
    }
}
