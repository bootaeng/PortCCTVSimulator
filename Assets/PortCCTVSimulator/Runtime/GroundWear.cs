using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace PortCCTV
{
    /// <summary>
    /// Wear on paved harbour surfaces: oil stains, tyre marks, repaired patches and grime blotches as HDRP decal projectors on
    /// the top faces of large concrete / asphalt boxes (quay aprons, yards, breakwater crowns). Deterministic per harbor id, so a
    /// map always looks the same. Background only: decals change albedo/smoothness, never geometry, colliders, masks or labels.
    /// The projector boxes are thin (1 m around the deck top), so vessels on the water are never inside them.
    /// </summary>
    public static class GroundWear
    {
        public static readonly string[] Kinds = { "wear_grime", "wear_oil", "wear_tyre", "wear_patch" };
        static readonly float[] Weights = { 0.36f, 0.24f, 0.22f, 0.18f };
        static readonly HashSet<string> Surfaces = new HashSet<string> { "concrete", "asphalt", "breakwater" };
        public const int MaxDecals = 480;   // HDRP default maxDecalsOnScreen 512 (2 weather decals)

        public static int Scatter(HarborDefinition def, HarborTheme theme, Transform parent)
        {
            if (theme == null) return 0;
            var mats = Kinds.Select(k => theme.Get(k)).ToArray();
            if (mats.Any(m => m == null)) return 0;
            uint h = 2166136261u; foreach (var c in def.harbor_id) h = (h ^ c) * 16777619u;
            var rng = new System.Random(unchecked((int)h));
            var root = new GameObject("GroundWear").transform;
            root.SetParent(parent, false);
            var cand = def.elements.Where(e => e.shape == "box" && Surfaces.Contains(e.material) && e.size.x * e.size.z > 300f)
                .OrderBy(e => e.id, System.StringComparer.Ordinal).ToList();
            float totalArea = cand.Sum(e => e.size.x * e.size.z);
            float perM2 = totalArea > 0f ? Mathf.Min(1f / 150f, MaxDecals / totalArea) : 0f;
            int n = 0;
            foreach (var e in cand)
            {
                int count = Mathf.Min(60, Mathf.RoundToInt(e.size.x * e.size.z * perM2 + (float)rng.NextDouble()));
                float top = e.center.y + e.size.y / 2f;
                var rot = e.Rotation;
                for (int i = 0; i < count && n < MaxDecals; i++)
                {
                    double r = rng.NextDouble(); int k = 0;
                    for (float acc = Weights[0]; r > acc && k < Kinds.Length - 1; acc += Weights[++k]) { }
                    float w, l;
                    switch (k)
                    {
                        case 0: w = Mathf.Lerp(4f, 12f, (float)rng.NextDouble()); l = w * Mathf.Lerp(0.7f, 1.4f, (float)rng.NextDouble()); break;
                        case 1: w = Mathf.Lerp(1f, 4f, (float)rng.NextDouble()); l = w * Mathf.Lerp(0.8f, 1.5f, (float)rng.NextDouble()); break;
                        case 2: w = Mathf.Lerp(2.2f, 2.8f, (float)rng.NextDouble()); l = Mathf.Lerp(6f, 16f, (float)rng.NextDouble()); break;
                        default: w = Mathf.Lerp(2f, 7f, (float)rng.NextDouble()); l = w * Mathf.Lerp(0.6f, 1.8f, (float)rng.NextDouble()); break;
                    }
                    float hx = Mathf.Max(0f, e.size.x / 2f - Mathf.Max(w, l) * 0.3f), hz = Mathf.Max(0f, e.size.z / 2f - Mathf.Max(w, l) * 0.3f);
                    var local = new Vector3((float)(rng.NextDouble() * 2 - 1) * hx, 0f, (float)(rng.NextDouble() * 2 - 1) * hz);
                    var pos = e.center + rot * local; pos.y = top + 0.4f;
                    // tyre marks and patches follow the paving axes (+-12 deg), stains and grime any direction
                    float yaw = k >= 2 ? e.yaw_deg + (rng.NextDouble() < 0.5 ? 0f : 90f) + (float)(rng.NextDouble() * 24 - 12) : (float)(rng.NextDouble() * 360);
                    var go = new GameObject(Kinds[k]);
                    go.transform.SetParent(root, false);
                    go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f) * Quaternion.Euler(90f, 0f, 0f));
                    var d = go.AddComponent<DecalProjector>();
                    d.material = mats[k];
                    d.size = new Vector3(w, l, 1.0f);
                    d.pivot = new Vector3(0f, 0f, 0.5f);
                    d.drawDistance = 20000f;
                    d.fadeFactor = Mathf.Lerp(0.55f, 1f, (float)rng.NextDouble());
                    d.startAngleFade = 30f; d.endAngleFade = 60f;          // horizontal surfaces only
                    n++;
                }
            }
            return n;
        }
    }
}
