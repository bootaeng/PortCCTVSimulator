using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace PortCCTV
{
    /// <summary>
    /// Player-side rendering diagnostics (CLI only, never used by research batches): `-diag-material <mode>[,<mode>...]` is applied
    /// to the loaded scene right before a `-capture`. Modes change in-memory material COPIES or renderer.enabled only:
    ///   hide_transparent | hide_alphatest | fogkw (fog-on-transparent keyword + float forced on) | hide_root:&lt;substring&gt;
    ///   | only_root:&lt;substring&gt; (hide every other building prefab instance) | glass_opaque (transparent slots -> the theme's opaque
///   HDRP/Lit "glass_dark") | log (list renderers in view per root and material class).
    /// Used to isolate the 2026-11-16 "black grid in fog" defect, which appears in the Windows player but not in Editor renders.
    /// </summary>
    public static class RenderDiagnostics
    {
        static float F(Material m, string p) => m.HasProperty(p) ? m.GetFloat(p) : -1f;
        public static bool IsTransparent(Material m) => m != null && (m.renderQueue >= 2501 || m.IsKeywordEnabled("_SURFACE_TYPE_TRANSPARENT") || F(m, "_SurfaceType") == 1f);
        public static bool IsAlphaTest(Material m) => m != null && !IsTransparent(m) && (m.IsKeywordEnabled("_ALPHATEST_ON") || F(m, "_AlphaCutoffEnable") == 1f);

        static string Root(Transform t)
        {
            string best = t.name;
            for (var r = t; r != null; r = r.parent) if (r.name.EndsWith("(Clone)")) best = r.name;
            return best;
        }

        public static string Apply(Camera cam, string modes, Material opaqueGlass = null)
        {
            var planes = GeometryUtility.CalculateFrustumPlanes(cam);
            var rs = Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None).Where(r => r.enabled && r.gameObject.activeInHierarchy && GeometryUtility.TestPlanesAABB(planes, r.bounds)).ToList();
            var log = new List<string>();
            foreach (var mode in modes.Split(','))
            {
                int n = 0;
                if (mode == "log")
                {
                    foreach (var g in rs.GroupBy(r => Root(r.transform) + " | " + string.Join("+", r.sharedMaterials.Where(m => m != null).Select(m => (IsTransparent(m) ? "T:" : IsAlphaTest(m) ? "A:" : "O:") + m.shader.name.Replace("Shader Graphs/", "") + (m.IsKeywordEnabled("_DOUBLESIDED_ON") ? "/2s" : "")).Distinct()))
                                  .OrderByDescending(g => g.Count()).Take(60))
                        log.Add($"diag log: {g.Count()} x {g.Key} dist {g.Min(r => Vector3.Distance(cam.transform.position, r.bounds.center)):F0}");
                    foreach (var m in rs.SelectMany(r => r.sharedMaterials).Where(IsTransparent).Distinct().Take(12))
                    {
                        var space = m.shader.keywordSpace;
                        bool declared = space.keywordNames.Contains("_ENABLE_FOG_ON_TRANSPARENT");
                        bool on = declared && m.IsKeywordEnabled(new UnityEngine.Rendering.LocalKeyword(m.shader, "_ENABLE_FOG_ON_TRANSPARENT"));
                        log.Add($"diag transparent material '{m.name}' shader '{m.shader.name}' queue {m.renderQueue} fog_float {F(m, "_EnableFogOnTransparent")} " +
                                $"fog_keyword_declared_in_player_shader {declared} enabled {on} enabledKeywords [{string.Join(" ", m.enabledKeywords.Select(k => k.name))}] " +
                                $"shader keyword space count {space.keywordCount}");
                    }
                    continue;
                }
                foreach (var r in rs)
                {
                    var mats = r.sharedMaterials;
                    string root = Root(r.transform);
                    if (mode == "hide_transparent" && mats.Any(IsTransparent)) { r.enabled = false; n++; }
                    else if (mode == "hide_alphatest" && mats.Any(IsAlphaTest)) { r.enabled = false; n++; }
                    else if (mode.StartsWith("hide_root:") && root.Contains(mode.Substring(10))) { r.enabled = false; n++; }
                    else if (mode.StartsWith("only_root:") && root.StartsWith("bld_") == false && root.Contains("(Clone)") && !root.Contains(mode.Substring(10))) { r.enabled = false; n++; }
                    else if (mode == "glass_opaque" && opaqueGlass != null && mats.Any(IsTransparent))
                    {
                        r.sharedMaterials = mats.Select(m => IsTransparent(m) ? opaqueGlass : m).ToArray();
                        n++;
                    }
                    else if (mode == "fogkw" && mats.Any(IsTransparent))
                    {
                        r.sharedMaterials = mats.Select(m =>
                        {
                            if (!IsTransparent(m)) return m;
                            var c = new Material(m) { name = m.name + "_fogkw" };
                            if (c.HasProperty("_EnableFogOnTransparent")) c.SetFloat("_EnableFogOnTransparent", 1f);
                            c.EnableKeyword("_ENABLE_FOG_ON_TRANSPARENT");
                            return c;
                        }).ToArray();
                        n++;
                    }
                }
                log.Add($"diag {mode}: {n} renderers changed (of {rs.Count} in view)");
            }
            return string.Join("\n", log);
        }
    }
}
