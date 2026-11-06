using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PortCCTV.EditorTools
{
    public static class BuildScript
    {
        public const string OutputExe = "Builds/Windows/PortCCTVSimulator.exe";

        [MenuItem("PortCCTV/Build Windows player")]
        public static void BuildWindows()
        {
            var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            if (scenes.Length == 0) throw new InvalidOperationException("No scenes in build settings (run bootstrap).");
            Directory.CreateDirectory(Path.GetDirectoryName(OutputExe));
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes, locationPathName = OutputExe,
                target = BuildTarget.StandaloneWindows64, targetGroup = BuildTargetGroup.Standalone,
                options = BuildOptions.None,
            });
            var s = report.summary;
            Debug.Log($"[Build] result={s.result} errors={s.totalErrors} warnings={s.totalWarnings} size={s.totalSize} time={s.totalTime} -> {Path.GetFullPath(OutputExe)}");
            if (s.result != BuildResult.Succeeded) throw new Exception("Build failed: " + s.result);
            // Ship example scenarios next to the exe (never overwrite user files there).
            var dst = Path.Combine(Path.GetDirectoryName(OutputExe), "Scenarios");
            Directory.CreateDirectory(dst);
            foreach (var f in Directory.GetFiles("Scenarios", "example_*.json"))
            {
                var t = Path.Combine(dst, Path.GetFileName(f));
                if (!File.Exists(t)) File.Copy(f, t);
            }
        }

        public static void BuildWindowsBatch()
        {
            try { BuildWindows(); EditorApplication.Exit(0); }
            catch (Exception ex) { Debug.LogError("[Build] FAILED: " + ex); EditorApplication.Exit(1); }
        }
    }

    /// <summary>
    /// Edit-mode runner for quick iteration without a player build:
    ///   Unity.exe -batchmode -projectPath <root> -executeMethod PortCCTV.EditorTools.EditorRunner.RunBatchFromArgs -jobs <jobs.json>
    ///   ... -executeMethod PortCCTV.EditorTools.EditorRunner.WriteRegressionFromArgs -dir Scenarios/regression_x -out Captures/regression_x
    /// Note: no -nographics (rendering is required).
    /// </summary>
    public static class EditorRunner
    {
        static string Arg(string name)
        {
            var a = Environment.GetCommandLineArgs();
            for (int i = 0; i < a.Length - 1; i++) if (a[i] == name) return a[i + 1];
            return null;
        }

        public static void WriteRegressionFromArgs()
        {
            try
            {
                var jobs = RegressionSuite.Write(AppPaths.ResolveUnderRoot(Arg("-dir")), Arg("-out") ?? "Captures/regression_editor");
                Debug.Log("[EditorRunner] wrote " + jobs);
                EditorApplication.Exit(0);
            }
            catch (Exception ex) { Debug.LogError("[EditorRunner] FAILED: " + ex); EditorApplication.Exit(1); }
        }

        public static void WriteMapSuiteFromArgs()
        {
            try
            {
                var jobs = MapSuite.Write(AppPaths.ResolveUnderRoot(Arg("-dir")), Arg("-out") ?? "Captures/mapsuite_editor");
                Debug.Log("[EditorRunner] wrote " + jobs);
                EditorApplication.Exit(0);
            }
            catch (Exception ex) { Debug.LogError("[EditorRunner] FAILED: " + ex); EditorApplication.Exit(1); }
        }

        /// <summary>Asset showcase: places every theme prefab whose key matches `-prefix` (comma list) in a row on the container
        /// terminal's north land, renders it from a camera_override with the normal capture path and writes a lineup PNG.
        /// Args: -prefix car_,van_,truck_ -out Captures/showcase_x [-spacing 8] [-height 30]</summary>
        public static void ShowcaseFromArgs()
        {
            int code = 1;
            SimWorld world = null;
            try
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                world = new SimWorld();
                world.Initialize();
                var prefixes = (Arg("-prefix") ?? "car_").Split(',');
                float spacing = float.TryParse(Arg("-spacing") ?? "8", System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var sp) ? sp : 8f;
                float camH = float.TryParse(Arg("-height") ?? "30", System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var ch) ? ch : 30f;
                var keys = world.Theme.prefabs.Where(e => prefixes.Any(p => e.key.StartsWith(p))).Select(e => e.key).OrderBy(k => k).ToList();
                var cfg = ContainerTerminal01.DefaultScenario(); cfg.vessels.Clear(); cfg.capture.write_mask = false; cfg.scenario_id = "showcase";
                float x0 = -spacing * (keys.Count - 1) / 2f, z = Arg("-z") != null ? float.Parse(Arg("-z")) : 1200f, deck = 2.5f;
                bool top = Arg("-top") != null;
                cfg.camera_override = top
                    ? new CameraOverride { enabled = true, position = new Vector3(0, deck + camH, z), euler_deg = new Vector3(89.9f, 0, 0), vertical_fov_deg = 2f * Mathf.Atan2(spacing * keys.Count * 0.55f, camH) * Mathf.Rad2Deg * 9f / 16f + 4f }
                    : new CameraOverride { enabled = true, position = new Vector3(0, deck + camH, z - 1.6f * camH - spacing * keys.Count * 0.35f), euler_deg = new Vector3(Mathf.Atan2(camH, 1.6f * camH + spacing * keys.Count * 0.35f) * Mathf.Rad2Deg, 0, 0), vertical_fov_deg = 45f };
                world.Apply(cfg);
                var root = new GameObject("Showcase");
                for (int i = 0; i < keys.Count; i++)
                {
                    var pf = world.Theme.GetPrefab(keys[i]);
                    var go = UnityEngine.Object.Instantiate(pf, new Vector3(x0 + i * spacing, deck, z), Quaternion.Euler(0f, Arg("-yaw") != null ? float.Parse(Arg("-yaw")) : 30f, 0f), root.transform);
                    go.name = keys[i];
                    var rs = go.GetComponentsInChildren<Renderer>(); var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
                    Debug.Log($"[Showcase] {keys[i]} size={b.size} min_y={b.min.y - deck:F2}");
                    if (top) { var mk = GameObject.CreatePrimitive(PrimitiveType.Cube); mk.transform.SetParent(root.transform); mk.transform.position = new Vector3(x0 + i * spacing + 4f, deck + 0.5f, z); mk.transform.localScale = new Vector3(0.6f, 1f, 0.6f); }   // marker at +x of each item
                }
                var res = CaptureService.Capture(world, AppPaths.ResolveUnderRoot(Arg("-out") ?? "Captures/showcase"), "showcase");
                Debug.Log(res.success ? "[Showcase] " + string.Join(",", keys) + " -> " + res.output_dir : "[Showcase] FAILED " + res.error);
                code = res.success ? 0 : 1;
                UnityEngine.Object.DestroyImmediate(root);
            }
            catch (Exception ex) { Debug.LogError("[Showcase] FAILED: " + ex); }
            finally { world?.Dispose(); }
            EditorApplication.Exit(code);
        }

        public static void RunBatchFromArgs()
        {
            int code = 1;
            SimWorld world = null;
            try
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                world = new SimWorld();
                world.Initialize();
                BatchSummary summary = null;
                var it = BatchRunner.Run(world, AppPaths.ResolveUnderRoot(Arg("-jobs")), s => Debug.Log("[EditorRunner] " + s), s => summary = s);
                while (it.MoveNext()) { }
                code = summary != null && summary.unexpected == 0 ? 0 : 1;
                Debug.Log($"[EditorRunner] done={summary?.done} skipped={summary?.skipped_existing} failed={summary?.failed} unexpected={summary?.unexpected}");
            }
            catch (Exception ex) { Debug.LogError("[EditorRunner] FAILED: " + ex); }
            finally { world?.Dispose(); }
            EditorApplication.Exit(code);
        }
    }
}
