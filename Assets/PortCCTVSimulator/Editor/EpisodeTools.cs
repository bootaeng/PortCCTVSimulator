using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using PortCCTV.Research;

namespace PortCCTV.EditorTools
{
    /// <summary>
    /// Plans moving-vessel episodes (Research/Episodes.cs) and writes one scenario per frame plus a portcctv.batch.v1 jobs file
    /// that the Windows player renders. Planning only; nothing is rendered here.
    ///   Unity.exe -batchmode -projectPath <root> -executeMethod PortCCTV.EditorTools.EpisodeTools.PlanFromArgs -spec <spec.json> -out <folder> [-capture-dir Captures/x]
    /// spec.json: {"experiment_id": "...", "cameras": ["ct_A_quay_east_mast", ...], "seeds": [1, 2, ...], "split_rule": "k=((seed-1) mod 40)+1 ...",
    ///             "frames": 8, "moving_min": 1, "moving_max": 3, "static_max": 1}
    /// </summary>
    public static class EpisodeTools
    {
        [Serializable] class Spec { public string experiment_id = "jmse_wx_ep"; public List<string> cameras = new List<string>(); public List<int> seeds = new List<int>(); public int frames = 8, moving_min = 1, moving_max = 3, static_max = 1; public float height_frac_min = 0.025f, height_frac_max = 0.16f; }

        static string Arg(string name)
        {
            var a = Environment.GetCommandLineArgs();
            for (int i = 0; i < a.Length - 1; i++) if (a[i] == name) return a[i + 1];
            return null;
        }

        public static string SplitOf(int seed)
        {
            int k = ((seed - 1) % 40) + 1;
            return k <= 24 ? "train" : k <= 28 ? "val" : k <= 32 ? "calibration" : "test";
        }

        static string HarborOf(string cam) => cam.StartsWith("ct_") ? ContainerTerminal01.Id : cam.StartsWith("fp_") ? FishingPort01.Id : Marina01.Id;

        public static void PlanFromArgs()
        {
            int code = 1; SimWorld world = null;
            try
            {
                var spec = JsonUtility.FromJson<Spec>(File.ReadAllText(AppPaths.ResolveUnderRoot(Arg("-spec"))));
                var outDir = AppPaths.ResolveUnderRoot(Arg("-out"));
                if (Directory.Exists(outDir) && Directory.EnumerateFileSystemEntries(outDir).Any()) throw new IOException("refusing to overwrite " + outDir);
                Directory.CreateDirectory(Path.Combine(outDir, "scenarios")); Directory.CreateDirectory(Path.Combine(outDir, "episodes"));
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                world = new SimWorld(); world.Initialize();
                var batch = new BatchFile { batch_id = Path.GetFileName(outDir), output_dir = Arg("-capture-dir") ?? "Captures/" + Path.GetFileName(outDir) };
                int ok = 0, failed = 0;
                foreach (var cam in spec.cameras.OrderBy(c => HarborOf(c)).ThenBy(c => c, StringComparer.Ordinal))
                    foreach (var seed in spec.seeds)
                    {
                        var es = new EpisodeSpec
                        {
                            experiment_id = spec.experiment_id, harbor_id = HarborOf(cam), camera_preset_id = cam, seed = seed, split = SplitOf(seed),
                            frames = spec.frames, moving_min = spec.moving_min, moving_max = spec.moving_max, static_max = spec.static_max,
                            height_frac_min = spec.height_frac_min, height_frac_max = spec.height_frac_max,
                        };
                        var plan = EpisodePlanner.Plan(world, es);
                        File.WriteAllText(Path.Combine(outDir, "episodes", plan.episode_id + ".json"), JsonUtility.ToJson(plan, true), new UTF8Encoding(false));
                        Debug.Log($"[EpisodeTools] {plan.episode_id}: {plan.status} after {plan.attempts} attempts {plan.failure}");
                        if (plan.status != "planned") { failed++; continue; }
                        ok++;
                        foreach (var fr in plan.frames)
                        {
                            var s = JsonUtility.FromJson<ScenarioConfig>(JsonUtility.ToJson(plan.scenario));
                            s.scenario_id = fr.scenario_id;
                            s.navigation.sim_time_s = fr.sim_time_s;
                            s.notes = $"research episode {plan.episode_id} frame {fr.frame_index} (split {es.split}); moving vessels follow their route under NavigationSim";
                            var rel = "scenarios/" + fr.scenario_id + ".json";
                            File.WriteAllText(Path.Combine(outDir, rel), ScenarioIO.ToJson(s), new UTF8Encoding(false));
                            batch.jobs.Add(new BatchJob
                            {
                                job_id = fr.scenario_id, scenario_path = rel, max_attempts = 2,
                                research = new ResearchJobInfo
                                {
                                    experiment_id = spec.experiment_id, harbor_id = es.harbor_id, camera_preset_id = cam, generation_seed = seed * 1000,
                                    episode_id = plan.episode_id, frame_index = fr.frame_index, sim_time_s = fr.sim_time_s, split = es.split,
                                    plan_status = "planned", scene_kind = "episode_frame", mode = "EP",
                                },
                            });
                        }
                    }
                File.WriteAllText(Path.Combine(outDir, "jobs.json"), JsonUtility.ToJson(batch, true), new UTF8Encoding(false));
                File.WriteAllText(Path.Combine(outDir, "spec.json"), JsonUtility.ToJson(spec, true), new UTF8Encoding(false));
                Debug.Log($"[EpisodeTools] planned {ok}, failed {failed}, frames {batch.jobs.Count} -> {outDir}");
                code = 0;
            }
            catch (Exception ex) { Debug.LogError("[EpisodeTools] FAILED: " + ex); }
            finally { world?.Dispose(); }
            EditorApplication.Exit(code);
        }
    }
}
