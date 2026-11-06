using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace PortCCTV
{
    [Serializable]
    public class BatchJob
    {
        public string job_id;
        /// <summary>Path to a scenario JSON, relative to the jobs file. Ignored if use_inline.</summary>
        public string scenario_path;
        public bool use_inline;
        public ScenarioConfig inline_scenario;
        /// <summary>Expected outcome; empty means "done". A regression case may expect "placement_rejected".</summary>
        public string expected_status = "";
    }

    [Serializable]
    public class BatchFile
    {
        public string batch_schema = "portcctv.batch.v1";
        public string batch_id = "batch";
        /// <summary>Output directory; relative paths resolve against the app root (project root or exe folder).</summary>
        public string output_dir = "Captures/batch";
        public List<BatchJob> jobs = new List<BatchJob>();
    }

    [Serializable]
    public class BatchLogEntry
    {
        public string utc;
        public string job_id;
        public string status; // done | skipped_existing | failed | placement_rejected
        public string output;
        public string message;
        public double ms;
        public string expected_status;
        public bool as_expected;
    }

    [Serializable]
    public class BatchSummary
    {
        public string batch_id;
        public string jobs_file;
        public string jobs_file_sha256;
        public string started_utc, finished_utc;
        public int total, done, skipped_existing, failed, unexpected;
        public List<BatchLogEntry> entries = new List<BatchLogEntry>();
    }

    /// <summary>
    /// Frozen job list -> one output folder per job. Resume = re-run the same jobs file: finished job folders are skipped,
    /// failed attempts stay as "<job>.partial_<ts>" (never deleted, never overwritten).
    /// </summary>
    public static class BatchRunner
    {
        public static BatchFile LoadBatch(string path)
        {
            var b = JsonUtility.FromJson<BatchFile>(File.ReadAllText(path, Encoding.UTF8));
            if (b == null || b.jobs == null) throw new InvalidDataException("Invalid batch file " + path);
            var dup = b.jobs.GroupBy(j => j.job_id).FirstOrDefault(g => g.Count() > 1);
            if (dup != null) throw new InvalidDataException("Duplicate job_id " + dup.Key);
            foreach (var j in b.jobs)
                if (string.IsNullOrWhiteSpace(j.job_id) || ScenarioIO.SanitizeId(j.job_id) != j.job_id)
                    throw new InvalidDataException("job_id must be non-empty [A-Za-z0-9_-]: " + j.job_id);
            return b;
        }

        /// <summary>Player frames between applying a scenario and capturing it (HDRP sky / ambient-probe readback).</summary>
        public const int SettleFrames = 8;

        public static IEnumerator Run(SimWorld world, string jobsPath, Action<string> log, Action<BatchSummary> done)
        {
            jobsPath = Path.GetFullPath(jobsPath);
            var batch = LoadBatch(jobsPath);
            var outDir = AppPaths.ResolveUnderRoot(batch.output_dir);
            Directory.CreateDirectory(outDir);
            var summary = new BatchSummary
            {
                batch_id = batch.batch_id, jobs_file = jobsPath,
                jobs_file_sha256 = ScenarioIO.Sha256Hex(File.ReadAllBytes(jobsPath)),
                started_utc = DateTime.UtcNow.ToString("o"), total = batch.jobs.Count
            };
            var logPath = Path.Combine(outDir, "batch_log.jsonl");
            int i = 0;
            foreach (var job in batch.jobs)
            {
                i++;
                var entry = new BatchLogEntry { utc = DateTime.UtcNow.ToString("o"), job_id = job.job_id };
                var final = Path.Combine(outDir, job.job_id);
                var sw = System.Diagnostics.Stopwatch.StartNew();
                if (Directory.Exists(final))
                {
                    entry.status = "skipped_existing"; entry.output = final; summary.skipped_existing++;
                }
                else
                {
                    ScenarioConfig cfg = null;
                    bool applied = false;
                    try
                    {
                        cfg = job.use_inline
                            ? job.inline_scenario
                            : ScenarioIO.Load(Path.Combine(Path.GetDirectoryName(jobsPath), job.scenario_path));
                        if (cfg == null) throw new InvalidDataException("No scenario for job " + job.job_id);
                        var reports = world.Apply(cfg);
                        var bad = reports.Where(r => !r.ok).ToList();
                        if (cfg.capture.strict_placement && bad.Count > 0)
                        {
                            entry.status = "placement_rejected"; summary.failed++;
                            entry.message = string.Join("; ", bad.Select(r => r.object_id + ":" + string.Join(",", r.warnings)));
                        }
                        else applied = true;
                    }
                    catch (Exception ex)
                    {
                        entry.status = "failed"; entry.message = ex.Message; summary.failed++;
                    }
                    if (applied)
                    {
                        // Let HDRP settle on real player frames before the synchronous capture: the sky ambient probe (used by the
                        // water and the fog colour) is read back from the GPU asynchronously, so a capture in the same frame as an
                        // environment change (day -> night) was lit with the previous sky's ambient light.
                        var c = world.CctvCamera;
                        var settleRt = new RenderTexture(cfg.capture.width / 4, cfg.capture.height / 4, 24) { name = "PortCCTV_Settle" };
                        var prevTarget = c.targetTexture; float prevAspect = c.aspect;
                        for (int f = 0; f < SettleFrames; f++)
                        {
                            c.targetTexture = settleRt; c.aspect = (float)cfg.capture.width / cfg.capture.height;
                            c.Render();
                            c.targetTexture = prevTarget; c.aspect = prevAspect;
                            yield return null;
                        }
                        settleRt.Release(); if (Application.isPlaying) UnityEngine.Object.Destroy(settleRt); else UnityEngine.Object.DestroyImmediate(settleRt);
                        UnityEngine.Rendering.AsyncGPUReadback.WaitAllRequests();
                        try
                        {
                            var res = CaptureService.Capture(world, final, "batch");
                            entry.output = res.output_dir;
                            if (res.success) { entry.status = "done"; summary.done++; }
                            else { entry.status = "failed"; entry.message = res.error; summary.failed++; }
                        }
                        catch (Exception ex)
                        {
                            entry.status = "failed"; entry.message = ex.Message; summary.failed++;
                        }
                    }
                }
                entry.ms = sw.Elapsed.TotalMilliseconds;
                entry.expected_status = string.IsNullOrEmpty(job.expected_status) ? "done" : job.expected_status;
                entry.as_expected = entry.status == entry.expected_status || entry.status == "skipped_existing";
                if (!entry.as_expected) summary.unexpected++;
                summary.entries.Add(entry);
                File.AppendAllText(logPath, JsonUtility.ToJson(entry) + "\n", new UTF8Encoding(false));
                log?.Invoke($"[{i}/{batch.jobs.Count}] {job.job_id}: {entry.status}{(entry.as_expected ? "" : " (UNEXPECTED, expected " + entry.expected_status + ")")} {entry.message}");
                yield return null; // let the UI update between jobs
            }
            summary.finished_utc = DateTime.UtcNow.ToString("o");
            var sumPath = Path.Combine(outDir, "batch_summary_" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff") + ".json");
            File.WriteAllText(sumPath, JsonUtility.ToJson(summary, true), new UTF8Encoding(false));
            log?.Invoke($"Batch '{batch.batch_id}' finished: done={summary.done} skipped={summary.skipped_existing} failed={summary.failed} unexpected={summary.unexpected} -> {outDir}");
            done?.Invoke(summary);
        }
    }

    /// <summary>
    /// Map suite: for every themed map, each camera with the map's default (plausible) scene, each camera empty,
    /// and one deliberately implausible vessel with strict placement (expected: placement_rejected).
    /// </summary>
    public static class MapSuite
    {
        public static List<ScenarioConfig> Build(out List<string> expected)
        {
            var list = new List<ScenarioConfig>();
            expected = new List<string>();
            var maps = new (string id, ScenarioConfig def, string oddAsset, Vector2 oddPos)[]
            {
                (ContainerTerminal01.Id, ContainerTerminal01.DefaultScenario(), "kayak", new Vector2(200, -100)),
                (FishingPort01.Id, FishingPort01.DefaultScenario(), "container_ship", new Vector2(0, -600)),
                (Marina01.Id, Marina01.DefaultScenario(), "tanker", new Vector2(0, -900)),
            };
            foreach (var m in maps)
            {
                foreach (var cam in CameraPresetLibrary.ForHarbor(m.id))
                {
                    var s = m.def.Clone(); s.scenario_id = $"{m.id}_{cam.id}_default"; s.camera_preset_id = cam.id;
                    list.Add(s); expected.Add("done");
                    var e = m.def.Clone(); e.scenario_id = $"{m.id}_{cam.id}_empty"; e.camera_preset_id = cam.id; e.vessels.Clear();
                    list.Add(e); expected.Add("done");
                }
                var odd = m.def.Clone(); odd.scenario_id = $"{m.id}_implausible_{m.oddAsset}"; odd.vessels.Clear(); odd.capture.strict_placement = true;
                odd.vessels.Add(new VesselPlacement { object_id = "v00", asset_id = m.oddAsset, x = m.oddPos.x, z = m.oddPos.y, heading_deg = 0, immersion_m = 0.1f });
                list.Add(odd); expected.Add("placement_rejected");
            }
            return list;
        }

        public static string Write(string dir, string outputDir)
        {
            if (Directory.Exists(dir)) throw new IOException("Suite folder exists (not overwriting): " + dir);
            Directory.CreateDirectory(dir);
            var batch = new BatchFile { batch_id = "mapsuite_" + Path.GetFileName(dir), output_dir = outputDir };
            var list = Build(out var expected);
            for (int i = 0; i < list.Count; i++)
            {
                var file = list[i].scenario_id + ".json";
                ScenarioIO.Save(list[i], Path.Combine(dir, file), false);
                batch.jobs.Add(new BatchJob { job_id = list[i].scenario_id, scenario_path = file, expected_status = expected[i] });
            }
            var jobs = Path.Combine(dir, "jobs.json");
            File.WriteAllText(jobs, JsonUtility.ToJson(batch, true), new UTF8Encoding(false));
            return jobs;
        }
    }

    /// <summary>Time sequence of one scenario: N frames at sim_time = start + k * interval, written as ordinary scenario
    /// files + jobs.json (each frame reproduces on its own; the batch runner keeps the harbor loaded between frames).</summary>
    public static class SequenceSuite
    {
        public static string Write(ScenarioConfig baseCfg, string dir, string outputDir, int frames, float interval, float start)
        {
            if (frames < 1 || frames > 10000) throw new ArgumentException("frames must be within 1..10000");
            if (interval <= 0f) throw new ArgumentException("interval must be > 0");
            if (Directory.Exists(dir)) throw new IOException("Sequence folder exists (not overwriting): " + dir);
            Directory.CreateDirectory(dir);
            var id = ScenarioIO.SanitizeId(baseCfg.scenario_id);
            var batch = new BatchFile { batch_id = "sequence_" + Path.GetFileName(dir), output_dir = outputDir };
            for (int k = 0; k < frames; k++)
            {
                var s = baseCfg.Clone();
                s.navigation.sim_time_s = start + k * interval;
                s.scenario_id = $"{id}_t{k:0000}";
                var file = s.scenario_id + ".json";
                ScenarioIO.Save(s, Path.Combine(dir, file), false);
                batch.jobs.Add(new BatchJob { job_id = s.scenario_id, scenario_path = file, expected_status = "done" });
            }
            var jobs = Path.Combine(dir, "jobs.json");
            File.WriteAllText(jobs, JsonUtility.ToJson(batch, true), new UTF8Encoding(false));
            return jobs;
        }
    }

    /// <summary>
    /// Small regression suite: empty harbor x3 cameras, each hull alone, multi-vessel, structure occlusion (crane boom / pier),
    /// vessel-vessel occlusion, image-border truncation, environment variants. Positions chosen from the camera geometry.
    /// </summary>
    public static class RegressionSuite
    {
        static VesselPlacement V(string id, string asset, float x, float z, float hdg, float imm = 0.1f)
            => new VesselPlacement { object_id = id, asset_id = asset, x = x, z = z, heading_deg = hdg, immersion_m = imm };

        static ScenarioConfig S(string id, string cam, string env = "day_clear", string water = "calm", VesselPlacement[] vs = null)
        {
            var s = new ScenarioConfig { scenario_id = id, harbor_id = HarborGeneric02.Id, camera_preset_id = cam };
            s.environment.preset_id = env;
            s.environment.water_preset_id = water;
            if (vs != null) s.vessels.AddRange(vs);
            return s;
        }

        /// <summary>Ground point in front of a camera preset at horizontal distance d, offset by lateral metres (right = +).</summary>
        public static Vector2 AlongCamera(CameraPreset p, float d, float lateral = 0f)
        {
            float yaw = p.euler_deg.y * Mathf.Deg2Rad;
            var fwd = new Vector2(Mathf.Sin(yaw), Mathf.Cos(yaw));
            var right = new Vector2(fwd.y, -fwd.x);
            return new Vector2(p.position.x, p.position.z) + fwd * d + right * lateral;
        }

        /// <summary>Lateral offset (m) at distance d that puts a point at horizontal NDC u (-1..1).</summary>
        public static float LateralForNdc(CameraPreset p, float aspect, float d, float u)
        {
            float halfH = Mathf.Tan(Camera.VerticalToHorizontalFieldOfView(p.vertical_fov_deg, aspect) * Mathf.Deg2Rad / 2f);
            return u * halfH * d;
        }

        public static List<ScenarioConfig> Build()
        {
            var A = CameraPresetLibrary.Get("cam_A_quay_mast");
            var B = CameraPresetLibrary.Get("cam_B_breakwater_head");
            var C = CameraPresetLibrary.Get("cam_C_warehouse_roof");
            const float aspect = 16f / 9f;
            var list = new List<ScenarioConfig>();
            foreach (var cam in new[] { A, B, C })
                list.Add(S("empty_" + cam.id, cam.id));

            var a90 = AlongCamera(A, 90);
            list.Add(S("single_rib_A", A.id, vs: new[] { V("v00", "rib", a90.x, a90.y, 20) }));
            list.Add(S("single_motorboat_A", A.id, vs: new[] { V("v00", "motorboat", a90.x, a90.y, 200) }));
            list.Add(S("single_ferry_A", A.id, vs: new[] { V("v00", "ferry", a90.x, a90.y, 100, 0.4f) }));

            var b1 = AlongCamera(B, 70, -10); var b2 = AlongCamera(B, 130, 15); var b3 = AlongCamera(B, 200, -20);
            list.Add(S("multi_three_B", B.id, vs: new[] { V("v00", "rib", b1.x, b1.y, 60), V("v01", "motorboat", b2.x, b2.y, 150), V("v02", "ferry", b3.x, b3.y, 30, 0.4f) }));

            // Vessel-vessel occlusion: RIB behind ferry along camera A line of sight.
            var near = AlongCamera(A, 70); var far = AlongCamera(A, 120, 21f); // RIB partly behind the ferry's bow
            list.Add(S("occlusion_vessel_A", A.id, vs: new[] { V("v00", "ferry", near.x, near.y, A.euler_deg.y + 90, 0.4f), V("v01", "rib", far.x, far.y, A.euler_deg.y + 90) }));

            // Structure occlusion: motorboat west of the finger pier (pier x -25..-5, z 0..60) seen from camera B;
            // the line of sight crosses the pier at ~290 m, so the pier deck (y=2.5) hides the lower hull.
            list.Add(S("occlusion_pier_full_B", B.id, vs: new[] { V("v00", "motorboat", -40f, 40f, 90) }));
            // Partial structure occlusion: the 8 m tall ferry behind the same pier; only the superstructure shows.
            list.Add(S("occlusion_pier_partial_B", B.id, vs: new[] { V("v00", "ferry", -45f, 35f, 90, 0.4f) }));

            // Image-border truncation: hull centred on the right image edge (NDC u = 1) at 60 m.
            float lat = LateralForNdc(A, aspect, 60, 1.0f);
            var edge = AlongCamera(A, 60, lat);
            list.Add(S("truncation_right_edge_A", A.id, vs: new[] { V("v00", "ferry", edge.x, edge.y, A.euler_deg.y + 90, 0.4f) }));

            // Environment variants with the same 3-vessel layout from camera C.
            var c1 = AlongCamera(C, 160, -15); var c2 = AlongCamera(C, 230, 10); var c3 = AlongCamera(C, 300, -5);
            var layoutC = new[] { V("v00", "rib", c1.x, c1.y, 45), V("v01", "motorboat", c2.x, c2.y, 300), V("v02", "ferry", c3.x, c3.y, 10, 0.4f) };
            foreach (var env in new[] { "day_clear", "day_backlit", "dusk_low_light", "light_haze" })
                list.Add(S("env_" + env + "_C", C.id, env, vs: layoutC.Select(v => JsonUtility.FromJson<VesselPlacement>(JsonUtility.ToJson(v))).ToArray()));
            list.Add(S("water_light_chop_C", C.id, "day_clear", "light_chop", layoutC.Select(v => JsonUtility.FromJson<VesselPlacement>(JsonUtility.ToJson(v))).ToArray()));

            // Deliberately invalid placement: vessel on the north quay (must be flagged; strict mode rejects it).
            var bad = S("invalid_on_quay_A", A.id, vs: new[] { V("v00", "rib", -80f, 80f, 0) });
            bad.capture.strict_placement = true;
            list.Add(bad);
            return list;
        }

        /// <summary>Writes one scenario JSON per case and a jobs file. Refuses to overwrite an existing suite folder.</summary>
        public static string Write(string dir, string outputDir)
        {
            if (Directory.Exists(dir)) throw new IOException("Regression suite folder exists (not overwriting): " + dir);
            Directory.CreateDirectory(dir);
            var batch = new BatchFile { batch_id = "regression_" + Path.GetFileName(dir), output_dir = outputDir };
            foreach (var s in Build())
            {
                var file = s.scenario_id + ".json";
                ScenarioIO.Save(s, Path.Combine(dir, file), false);
                batch.jobs.Add(new BatchJob
                {
                    job_id = s.scenario_id, scenario_path = file,
                    expected_status = s.capture.strict_placement ? "placement_rejected" : "done"
                });
            }
            var jobs = Path.Combine(dir, "jobs.json");
            File.WriteAllText(jobs, JsonUtility.ToJson(batch, true), new UTF8Encoding(false));
            return jobs;
        }
    }
}
