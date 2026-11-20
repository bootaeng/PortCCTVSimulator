using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine;

namespace PortCCTV.EditorTools
{
    /// <summary>
    /// Fix for the 2026-11-16 "black grid in fog" defect. Diagnosis (docs/scene_fix_20261116/FOG_DEFECT.md): in the Windows player,
    /// glTFast shader-graph TRANSPARENT passes (window glass of the city towers, car glass, lamp glass) are drawn without the
    /// atmospheric fog, although the material carries _ENABLE_FOG_ON_TRANSPARENT and the variant exists in the build (strict
    /// variant matching reports no missing transparent/fog variant). The same scene renders correctly in the Editor. Replacing
    /// those slots by opaque HDRP/Lit materials makes them take the opaque fog path, verified in the player.
    ///
    /// Every theme prefab (harbour / town / background, NOT vessels: those already use M_vessel_glazing_opaque) gets, on the
    /// prefab copy only, its transparent slots replaced by derived opaque HDRP/Lit materials:
    ///   * slot with a base-colour texture  -> alpha-clipped opaque (cutoff 0.5, texture kept: fence wire, signs);
    ///   * slot without texture (glass)      -> opaque dark reflective glass, colour lerp(dark grey, base colour, base alpha).
    /// Source GLBs are untouched. Idempotent (derived materials keyed by source material). Report: Logs/transparent_glass_fix_report.json.
    ///   Unity.exe -batchmode -projectPath <root> -executeMethod PortCCTV.EditorTools.TransparentGlassFix.RunBatch [-dry-run]
    /// </summary>
    public static class TransparentGlassFix
    {
        public const string Version = "transparent_to_opaque_v1";
        const string Dir = "Assets/PortCCTVSimulator/Materials/Derived/OpaqueGlass";

        static float F(Material m, string p) => m.HasProperty(p) ? m.GetFloat(p) : -1f;
        public static bool IsTransparent(Material m) => m != null && (m.renderQueue >= 2501 || m.IsKeywordEnabled("_SURFACE_TYPE_TRANSPARENT") || F(m, "_SurfaceType") == 1f);

        [Serializable] class Row { public string prefab, renderer, source_material, shader, derived, kind; }
        [Serializable] class Report { public string version = Version; public bool dry_run; public int prefabs_scanned, prefabs_changed, slots_replaced; public List<Row> rows = new List<Row>(); }

        static Material Derive(Material src, Dictionary<Material, (Material m, string kind)> cache, bool dry, out string kind)
        {
            if (cache.TryGetValue(src, out var c)) { kind = c.kind; return c.m; }
            Texture tex = null;
            foreach (var p in new[] { "baseColorTexture", "_BaseColorMap", "_MainTex", "diffuseTexture" })
                if (src.HasProperty(p) && src.GetTexture(p) != null) { tex = src.GetTexture(p); break; }
            Color col = Color.white;
            foreach (var p in new[] { "baseColorFactor", "_BaseColor", "diffuseFactor", "_Color" })
                if (src.HasProperty(p)) { col = src.GetColor(p); break; }
            kind = tex != null ? "alpha_clipped_textured" : "opaque_glass";
            if (dry) { cache[src] = (null, kind); return null; }
            var m = new Material(Shader.Find("HDRP/Lit")) { name = src.name + (tex != null ? " (opaque clip)" : " (opaque glass)") };
            m.SetFloat("_SurfaceType", 0f);
            if (tex != null)
            {
                m.SetTexture("_BaseColorMap", tex);
                m.SetColor("_BaseColor", col);
                m.SetFloat("_AlphaCutoffEnable", 1f);
                m.SetFloat("_AlphaCutoff", 0.5f);
                m.SetFloat("_Smoothness", 0.4f);
            }
            else
            {
                var dark = new Color(0.05f, 0.06f, 0.07f);
                float a = Mathf.Clamp01(col.a);
                m.SetColor("_BaseColor", new Color(Mathf.Lerp(dark.r, col.r, a), Mathf.Lerp(dark.g, col.g, a), Mathf.Lerp(dark.b, col.b, a), 1f));
                m.SetFloat("_Smoothness", 0.85f);
                m.SetFloat("_Metallic", 0.1f);
            }
            if (F(src, "_DoubleSidedEnable") == 1f || src.IsKeywordEnabled("_DOUBLESIDED_ON")) m.SetFloat("_DoubleSidedEnable", 1f);
            HDMaterial.ValidateMaterial(m);
            Directory.CreateDirectory(Dir);
            string safe = new string(src.name.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_').ToArray());
            string hash = ScenarioIO.Sha256Hex(Encoding.UTF8.GetBytes(AssetDatabase.GetAssetPath(src) + "|" + src.name + "|" + src.GetInstanceID())).Substring(0, 10);
            var path = $"{Dir}/{safe}_{hash}.mat";
            AssetDatabase.CreateAsset(m, path);
            cache[src] = (m, kind);
            return m;
        }

        public static string Run(bool dry)
        {
            var theme = AssetDatabase.LoadAssetAtPath<HarborTheme>("Assets/PortCCTVSimulator/Resources/PortCCTV/HarborTheme.asset")
                        ?? throw new InvalidOperationException("HarborTheme.asset missing (run bootstrap)");
            var rep = new Report { dry_run = dry };
            var cache = new Dictionary<Material, (Material m, string kind)>();
            var paths = theme.prefabs.Where(p => p.prefab != null).Select(p => AssetDatabase.GetAssetPath(p.prefab))
                .Where(p => p.EndsWith(".prefab") && !p.Contains("/Vessels/")).Distinct().OrderBy(p => p, StringComparer.Ordinal).ToList();
            foreach (var path in paths)
            {
                rep.prefabs_scanned++;
                var root = PrefabUtility.LoadPrefabContents(path);
                bool changed = false;
                try
                {
                    foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                    {
                        var mats = r.sharedMaterials; bool rc = false;
                        for (int i = 0; i < mats.Length; i++)
                        {
                            if (!IsTransparent(mats[i])) continue;
                            var d = Derive(mats[i], cache, dry, out var kind);
                            rep.rows.Add(new Row { prefab = path, renderer = r.name, source_material = mats[i].name, shader = mats[i].shader.name, derived = d != null ? AssetDatabase.GetAssetPath(d) : "(dry run)", kind = kind });
                            rep.slots_replaced++;
                            if (!dry) { mats[i] = d; rc = true; }
                        }
                        if (rc) { r.sharedMaterials = mats; changed = true; }
                    }
                    if (changed) { PrefabUtility.SaveAsPrefabAsset(root, path); rep.prefabs_changed++; }
                    else if (dry && rep.rows.Any(x => x.prefab == path)) rep.prefabs_changed++;
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            AssetDatabase.SaveAssets();
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/transparent_glass_fix_report.json", JsonUtility.ToJson(rep, true), new UTF8Encoding(false));
            var msg = $"{Version}: scanned {rep.prefabs_scanned} prefabs, {(dry ? "would change" : "changed")} {rep.prefabs_changed}, slots {rep.slots_replaced}, derived materials {cache.Count(c => c.Value.m != null)}";
            Debug.Log("[TransparentGlassFix] " + msg);
            return msg;
        }

        public static void RunBatch()
        {
            bool dry = Environment.GetCommandLineArgs().Contains("-dry-run");
            try { Run(dry); EditorApplication.Exit(0); }
            catch (Exception ex) { Debug.LogError("[TransparentGlassFix] FAILED: " + ex); EditorApplication.Exit(1); }
        }
    }
}
