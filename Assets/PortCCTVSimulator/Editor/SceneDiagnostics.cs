using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace PortCCTV.EditorTools
{
    /// <summary>
    /// Diagnostics for the 2026-11-16 scene-defect work (fog "black grid" on distant buildings).
    ///   Unity.exe -batchmode -projectPath <root> -executeMethod PortCCTV.EditorTools.SceneDiagnostics.FogMaterialAuditFromArgs
    ///             -scenario <scenario.json> -out <folder> [-variants baseline,fogkw,hide_transparent,hide_alphatest,no_alphaclip]
    /// Writes material_audit.json (every renderer in the view frustum: shader, queue, surface type, fog / alpha-test keywords,
    /// prefab root, distance) and renders the same scenario once per variant with in-memory material COPIES (assets are never
    /// modified or saved). The variants isolate one factor each; pixel comparison is done offline (Tools/research/jmse).
    /// </summary>
    public static class SceneDiagnostics
    {
        static string Arg(string name)
        {
            var a = Environment.GetCommandLineArgs();
            for (int i = 0; i < a.Length - 1; i++) if (a[i] == name) return a[i + 1];
            return null;
        }

        [Serializable] class MatRow
        {
            public string renderer, root, material, shader; public int queue; public float surface_type = -1, fog_on_transparent = -1, alpha_cutoff_enable = -1, zwrite = -1;
            public bool kw_transparent, kw_fog_transparent, kw_alphatest, in_frustum; public float distance_m; public string[] keywords;
        }
        [Serializable] class Audit { public string scenario; public List<MatRow> rows = new List<MatRow>(); public List<string> variants_done = new List<string>(); }

        static float F(Material m, string p) => m.HasProperty(p) ? m.GetFloat(p) : -1f;

        static bool IsTransparent(Material m) => m != null && (m.renderQueue >= 2501 || m.IsKeywordEnabled("_SURFACE_TYPE_TRANSPARENT") || F(m, "_SurfaceType") == 1f);
        static bool IsAlphaTest(Material m) => m != null && !IsTransparent(m) && (m.IsKeywordEnabled("_ALPHATEST_ON") || F(m, "_AlphaCutoffEnable") == 1f);

        static string RootName(Transform t)
        {
            var r = t; string best = t.name;
            while (r.parent != null) { r = r.parent; if (r.name.StartsWith("bld_") || r.name.Contains("prefab") || r.name.Contains("(Clone)")) best = r.name; }
            return best;
        }

        /// <summary>Diagnostic player build with strict shader variant matching (a missing variant is logged as an error instead
        /// of silently falling back to the nearest one). Output -out (default Builds/diag_strict); the project setting is restored
        /// to its previous value afterwards. Not the release build.</summary>
        public static void StrictVariantBuildFromArgs()
        {
            int code = 1;
            bool prev = PlayerSettings.strictShaderVariantMatching;
            try
            {
                var exe = Arg("-out") ?? "Builds/diag_strict/PortCCTVSimulator.exe";
                var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
                PlayerSettings.strictShaderVariantMatching = Arg("-strict") != "0";
                Directory.CreateDirectory(Path.GetDirectoryName(exe));
                BuildManifestGenerator.Generate();
                var report = UnityEditor.BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = scenes, locationPathName = exe, target = BuildTarget.StandaloneWindows64, targetGroup = BuildTargetGroup.Standalone, options = BuildOptions.None,
                });
                var s = report.summary;
                Debug.Log($"[SceneDiagnostics] strict build result={s.result} errors={s.totalErrors} time={s.totalTime} -> {exe}");
                code = s.result == UnityEditor.Build.Reporting.BuildResult.Succeeded ? 0 : 1;
            }
            catch (Exception ex) { Debug.LogError("[SceneDiagnostics] strict build FAILED: " + ex); }
            finally { PlayerSettings.strictShaderVariantMatching = prev; }
            EditorApplication.Exit(code);
        }

        [Serializable] class LayoutRow { public string source, scenario_id, harbor_id, camera_preset_id; public PlacementQuality.Report report; }

        /// <summary>Screen-layout audit (PlacementQuality, current camera presets): -mode defaults (every map default scene seen from
        /// every camera of its map) or -mode jobs -jobs a.json[,b.json] (each job's scenario as saved). Writes one JSON line per
        /// scene to -out (a .jsonl file). Evaluation only (vessels are not spawned for the check; Apply sets map and camera).</summary>
        public static void LayoutAuditFromArgs()
        {
            int code = 1; SimWorld world = null;
            try
            {
                var outPath = AppPaths.ResolveUnderRoot(Arg("-out"));
                if (File.Exists(outPath)) throw new IOException("refusing to overwrite " + outPath);
                Directory.CreateDirectory(Path.GetDirectoryName(outPath));
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                world = new SimWorld(); world.Initialize();
                var rows = new List<string>();
                var overrides = new Dictionary<string, CameraOverride>();
                if (Arg("-camera-file") != null)
                    foreach (var line in File.ReadAllLines(AppPaths.ResolveUnderRoot(Arg("-camera-file"))).Where(l => l.Trim().Length > 0))
                    {
                        // id px py pz pitch yaw fov   (whitespace separated; the pre-2026-11-16 presets)
                        var t = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                        float P(int i) => float.Parse(t[i], System.Globalization.CultureInfo.InvariantCulture);
                        overrides[t[0]] = new CameraOverride { enabled = true, position = new Vector3(P(1), P(2), P(3)), euler_deg = new Vector3(P(4), P(5), 0f), vertical_fov_deg = P(6) };
                    }
                void Audit(string source, ScenarioConfig cfg)
                {
                    if (overrides.TryGetValue(cfg.camera_preset_id, out var co)) { cfg.camera_override = co; source += "+old_camera"; }
                    var vs = cfg.vessels.ToList();
                    var probe = new ScenarioConfig(); JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(cfg), probe);
                    probe.vessels.Clear();
                    world.Apply(probe);
                    var rep = PlacementQuality.Evaluate(world, vs, cfg.capture.width, cfg.capture.height, (a, b) => a == b + "_occ");
                    rows.Add(JsonUtility.ToJson(new LayoutRow { source = source, scenario_id = cfg.scenario_id, harbor_id = cfg.harbor_id, camera_preset_id = cfg.camera_preset_id, report = rep }));
                }
                var defaults = new List<ScenarioConfig>();
                if ((Arg("-mode") ?? "defaults") == "defaults")
                {
                    foreach (var hid in new[] { ContainerTerminal01.Id, FishingPort01.Id, Marina01.Id })
                        foreach (var cam in CameraPresetLibrary.ForHarbor(hid))
                        {
                            var s = SimulatorApp.DefaultScenarioFor(hid); s.camera_preset_id = cam.id; s.scenario_id = "default_" + hid + "_" + cam.id;
                            if (Arg("-write-jobs") != null) defaults.Add(s.Clone());
                            Audit("default", s);
                        }
                    // -write-jobs <dir> -jobs-output <captures dir>: the default scene of every camera as scenario files + jobs.json (freeze baseline)
                    var wj = Arg("-write-jobs");
                    if (wj != null)
                    {
                        var dir = AppPaths.ResolveUnderRoot(wj);
                        if (Directory.Exists(dir)) throw new Exception("refusing to overwrite " + dir);
                        Directory.CreateDirectory(Path.Combine(dir, "scenarios"));
                        var bf = new BatchFile { batch_id = Path.GetFileName(dir), output_dir = Arg("-jobs-output") ?? ("Captures/" + Path.GetFileName(dir)) };
                        foreach (var d in defaults)
                        {
                            File.WriteAllText(Path.Combine(dir, "scenarios", d.scenario_id + ".json"), ScenarioIO.ToJson(d), new UTF8Encoding(false));
                            bf.jobs.Add(new BatchJob { job_id = d.scenario_id, scenario_path = "scenarios/" + d.scenario_id + ".json" });
                        }
                        File.WriteAllText(Path.Combine(dir, "jobs.json"), JsonUtility.ToJson(bf, true), new UTF8Encoding(false));
                        Debug.Log($"[SceneDiagnostics] wrote {defaults.Count} default-scene jobs -> {dir}");
                    }
                }
                else
                {
                    foreach (var jp in Arg("-jobs").Split(','))
                    {
                        var full = AppPaths.ResolveUnderRoot(jp);
                        var batch = JsonUtility.FromJson<BatchFile>(File.ReadAllText(full));
                        foreach (var j in batch.jobs)
                        {
                            if (j.research != null && j.research.plan_status == "planning_failed") continue;
                            var sp = Path.Combine(Path.GetDirectoryName(full), j.scenario_path);
                            if (!File.Exists(sp)) continue;
                            Audit(Path.GetFileName(Path.GetDirectoryName(full)), ScenarioIO.Load(sp));
                        }
                    }
                }
                File.WriteAllText(outPath, string.Join("\n", rows) + "\n", new UTF8Encoding(false));
                Debug.Log($"[SceneDiagnostics] layout audit {rows.Count} scenes -> {outPath}");
                code = 0;
            }
            catch (Exception ex) { Debug.LogError("[SceneDiagnostics] layout audit FAILED: " + ex); }
            finally { world?.Dispose(); }
            EditorApplication.Exit(code);
        }

        /// <summary>Bow-direction check: every catalog hull alone at heading 0 (bow must point to world +X) in open water, seen from
        /// the -Z side so +X is to the RIGHT of the image. One capture per hull under -out.</summary>
        public static void VesselBowLineupFromArgs()
        {
            int code = 1; SimWorld world = null;
            try
            {
                var outDir = AppPaths.ResolveUnderRoot(Arg("-out"));
                if (Directory.Exists(outDir) && Directory.EnumerateFileSystemEntries(outDir).Any()) throw new IOException("refusing to overwrite " + outDir);
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                world = new SimWorld(); world.Initialize();
                var only = Arg("-assets");
                foreach (var e in world.Catalog.entries.OrderBy(x => x.asset_id, StringComparer.Ordinal))
                {
                    if (only != null && !only.Split(',').Contains(e.asset_id)) continue;
                    var g = e.geometry; float L = g.bounds_max.x - g.bounds_min.x, Hh = g.bounds_max.y - g.bounds_min.y;
                    var s = SimulatorApp.DefaultScenarioFor(Marina01.Id);
                    s.vessels.Clear(); s.scenario_id = "bow_" + e.asset_id; s.capture.strict_placement = false; s.capture.write_mask = true;
                    s.environment.water_preset_id = "calm"; s.sensor = SensorConfig.Preset("off");
                    s.vessels.Add(new VesselPlacement { object_id = "v00", asset_id = e.asset_id, x = 0f, z = -1500f, heading_deg = 0f, immersion_m = e.default_immersion_m });
                    float dist = Mathf.Max(L, Hh) * 2.0f + 8f;
                    s.camera_override = new CameraOverride { enabled = true, position = new Vector3(0f, Hh * 0.35f + 2f, -1500f - dist), euler_deg = new Vector3(3f, 0f, 0f), vertical_fov_deg = 30f };
                    world.Apply(s);
                    var res = CaptureService.Capture(world, Path.Combine(outDir, e.asset_id), "bow_check");
                    Debug.Log($"[SceneDiagnostics] bow {e.asset_id}: {(res.success ? "ok" : res.error)}");
                }
                code = 0;
            }
            catch (Exception ex) { Debug.LogError("[SceneDiagnostics] bow lineup FAILED: " + ex); }
            finally { world?.Dispose(); }
            EditorApplication.Exit(code);
        }

        public static void FogMaterialAuditFromArgs()
        {
            int code = 1; SimWorld world = null;
            try
            {
                var outDir = AppPaths.ResolveUnderRoot(Arg("-out"));
                if (Directory.Exists(outDir) && Directory.EnumerateFileSystemEntries(outDir).Any()) throw new IOException("refusing to overwrite " + outDir);
                Directory.CreateDirectory(outDir);
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                world = new SimWorld(); world.Initialize();
                var cfg = ScenarioIO.Load(AppPaths.ResolveUnderRoot(Arg("-scenario")));
                world.Apply(cfg);
                var cam = world.CctvCamera;
                var planes = GeometryUtility.CalculateFrustumPlanes(cam);
                var audit = new Audit { scenario = Arg("-scenario") };
                var renderers = UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None);
                foreach (var r in renderers)
                {
                    if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
                    bool inF = GeometryUtility.TestPlanesAABB(planes, r.bounds);
                    if (!inF) continue;
                    foreach (var m in r.sharedMaterials)
                    {
                        if (m == null) continue;
                        audit.rows.Add(new MatRow
                        {
                            renderer = r.name, root = RootName(r.transform), material = m.name, shader = m.shader != null ? m.shader.name : "",
                            queue = m.renderQueue, surface_type = F(m, "_SurfaceType"), fog_on_transparent = F(m, "_EnableFogOnTransparent"),
                            alpha_cutoff_enable = F(m, "_AlphaCutoffEnable"), zwrite = F(m, "_ZWrite"),
                            kw_transparent = m.IsKeywordEnabled("_SURFACE_TYPE_TRANSPARENT"), kw_fog_transparent = m.IsKeywordEnabled("_ENABLE_FOG_ON_TRANSPARENT"),
                            kw_alphatest = m.IsKeywordEnabled("_ALPHATEST_ON"), in_frustum = inF,
                            distance_m = Vector3.Distance(cam.transform.position, r.bounds.center), keywords = m.shaderKeywords,
                        });
                    }
                }
                var variants = (Arg("-variants") ?? "baseline,fogkw,hide_transparent,hide_alphatest,no_alphaclip").Split(',');
                foreach (var v in variants)
                {
                    var restore = new List<(Renderer r, Material[] mats, bool enabled)>();
                    foreach (var r in renderers)
                    {
                        if (r == null || !GeometryUtility.TestPlanesAABB(planes, r.bounds)) continue;
                        var mats = r.sharedMaterials;
                        bool anyT = mats.Any(IsTransparent), anyA = mats.Any(IsAlphaTest);
                        if (v == "hide_transparent" && anyT) { restore.Add((r, mats, r.enabled)); r.enabled = false; }
                        else if (v == "hide_alphatest" && anyA) { restore.Add((r, mats, r.enabled)); r.enabled = false; }
                        else if (v == "fogkw" && anyT)
                        {
                            restore.Add((r, mats, r.enabled));
                            r.sharedMaterials = mats.Select(m =>
                            {
                                if (!IsTransparent(m)) return m;
                                var c = new Material(m) { name = m.name + "_fogkw" };
                                if (c.HasProperty("_EnableFogOnTransparent")) c.SetFloat("_EnableFogOnTransparent", 1f);
                                c.EnableKeyword("_ENABLE_FOG_ON_TRANSPARENT");
                                return c;
                            }).ToArray();
                        }
                        else if (v == "no_alphaclip" && anyA)
                        {
                            restore.Add((r, mats, r.enabled));
                            r.sharedMaterials = mats.Select(m =>
                            {
                                if (!IsAlphaTest(m)) return m;
                                var c = new Material(m) { name = m.name + "_noclip" };
                                c.DisableKeyword("_ALPHATEST_ON"); if (c.HasProperty("_AlphaCutoffEnable")) c.SetFloat("_AlphaCutoffEnable", 0f);
                                if (c.HasProperty("_AlphaClipThreshold")) c.SetFloat("_AlphaClipThreshold", 0f);
                                if (c.HasProperty("alphaCutoff")) c.SetFloat("alphaCutoff", 0f);
                                return c;
                            }).ToArray();
                        }
                    }
                    var res = CaptureService.Capture(world, Path.Combine(outDir, v), "diagnostic_" + v);
                    Debug.Log($"[SceneDiagnostics] variant {v}: changed renderers {restore.Count} -> {(res.success ? res.output_dir : "FAILED " + res.error)}");
                    audit.variants_done.Add($"{v}: {restore.Count} renderers, {(res.success ? "ok" : res.error)}");
                    foreach (var (r, mats, en) in restore) { if (r == null) continue; r.sharedMaterials = mats; r.enabled = en; }
                }
                File.WriteAllText(Path.Combine(outDir, "material_audit.json"), JsonUtility.ToJson(audit, true), new UTF8Encoding(false));
                code = 0;
            }
            catch (Exception ex) { Debug.LogError("[SceneDiagnostics] FAILED: " + ex); }
            finally { world?.Dispose(); }
            EditorApplication.Exit(code);
        }
    }
}
