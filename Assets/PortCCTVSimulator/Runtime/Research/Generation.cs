using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace PortCCTV.Research
{
    // =====================================================================================================================
    // G / C condition samplers (pure, seeded) and the scene planner that turns a requested screen condition into a placement
    // on the frozen map (needs the loaded SimWorld for the camera and the water regions). Requests and plan results are kept
    // in the job; achieved values are measured from the rendered mask after capture (ResearchMeasure).
    // =====================================================================================================================

    public static class Samplers
    {
        public const string G = "G", C = "C", CU = "C_U";
        public static readonly string[] Modes = { G, C, CU };
        public const string GVersion = "sampler_G_v1_fixed_prior";
        public const string CVersion = "sampler_C_v1_train_profile_match";
        public const string CUVersion = "sampler_CU_v1_rare_condition_uniform";

        public static readonly string[] Lighting = { "day_clear", "day_backlit", "dusk_low_light", "light_haze", "night_clear", "night_overcast" };
        public static readonly string[] Water = { "calm", "light_chop" };
        public static readonly float[] Fog = { 1.0f, 0.6f, 0.3f };
        public static readonly string[] Sensor = { "off", "hd_ip", "typical", "low_quality" };
        public static readonly string[] Precip = { "none", "none", "none", "rain" };

        public static string VersionOf(string mode) => mode == G ? GVersion : mode == C ? CVersion : CUVersion;

        /// <summary>The fixed G prior: chosen before any real profile was computed and never fitted to one.</summary>
        public const string GPriorDescription =
            "count U{1..6}; height_frac log-U[0.008,0.2]; center_u U[0.05,0.95]; bottom_v U[0.40,0.95]; heading U[0,360); " +
            "lighting U{6 presets}; water U{calm,light_chop}; fog visibility U{1,0.6,0.3}; precipitation none 3/4, rain 1/4; " +
            "sensor U{off,hd_ip,typical,low_quality} (night_ir replaces 'typical' at night); occlusion partial 0.2; truncation 0.1";
    }

    [Serializable]
    public class ObjectRequest
    {
        public string object_id;
        /// <summary>target (sampled condition) | occluder (placed to produce a requested partial occlusion)</summary>
        public string role = "target";
        public string occludes = "";
        // requested
        public float target_height_frac, target_center_u, target_bottom_v, heading_deg;
        public string occlusion_target = "none";
        public string truncation_target = "none";
        // plan result
        public string plan_status = "pending";        // planned | unmet
        public string unmet_reason = "";
        public int attempts;
        public string asset_id = "";
        public float x, z, immersion_m;
        public string water_region = "";
        public float predicted_height_frac = -1f, predicted_center_u = -1f, predicted_bottom_v = -1f;
        public float predicted_truncation_fraction = -1f;
        public float predicted_box_overlap_fraction = -1f;
        /// <summary>Row actually used minus requested bottom_v (position rule size_primary_nearest_feasible_row); 0 = as requested.</summary>
        public float position_adjusted_dv;
        public int rows_tried;
        public List<string> attempt_log = new List<string>();
    }

    [Serializable]
    public class EnvRequest
    {
        public string lighting = "day_clear", water = "calm", precipitation = "none", sensor = "off";
        public float fog_visibility_scale = 1f, precipitation_intensity = 0f, water_time_s = 12f;
    }

    [Serializable]
    public class ResearchJobInfo
    {
        public string experiment_id = "";
        public string contract_sha256 = "";
        public string freeze_id = "";
        public string mode = "";
        public string sampler_version = "";
        /// <summary>planned | empty_negative | paired | sequence | boundary</summary>
        public string scene_kind = "";
        public string harbor_id = "", camera_preset_id = "";
        public int generation_seed, layout_seed, env_seed, sensor_seed;
        public string profile_id = "", profile_kind = "", profile_sha256 = "";
        /// <summary>SHA-256 of the scenario file written at freeze time; a changed file is refused, not silently re-rendered.</summary>
        public string job_scenario_sha256 = "";
        /// <summary>When set, a capture is only reused / accepted from a build with this content hash.</summary>
        public string required_build_content_sha256 = "";
        public string plan_status = "";               // planned | planning_failed
        public List<string> plan_failure_reasons = new List<string>();
        public int planning_attempts;
        public double planning_ms;
        public List<ObjectRequest> objects = new List<ObjectRequest>();
        public EnvRequest environment = new EnvRequest();
        public string pair_group_id = "", changed_factor = "", changed_value = "";
        public string episode_id = "";
        public int frame_index = -1;
        public float sim_time_s;
        public string split = "";                    // synthetic sets: pretrain; episodes: train | val (assigned before generation)
        public string occlusion_reference = "per_object_water_only";
        public float size_tolerance_rel = 0.35f;
        public int retry_limit;
        public string label_policy_version = "", class_map_version = "", primary_bbox_policy = "";
        public bool IsResearch => !string.IsNullOrEmpty(contract_sha256);
    }

    /// <summary>Pure condition sampling. Layout, environment and sensor draws use separate seed streams, so changing one factor
    /// never changes the others.</summary>
    public static class ConditionSampler
    {
        public static void Sample(string mode, ScreenProfile profile, GenerationSpec g, ResearchJobInfo job)
        {
            var lay = new Rng64(Rng64.SeedOf("layout", job.experiment_id, job.mode, job.harbor_id, job.camera_preset_id, job.generation_seed));
            var env = new Rng64(Rng64.SeedOf("env", job.experiment_id, job.mode, job.harbor_id, job.camera_preset_id, job.generation_seed));
            var sen = new Rng64(Rng64.SeedOf("sensor", job.experiment_id, job.mode, job.harbor_id, job.camera_preset_id, job.generation_seed));
            job.layout_seed = (int)(lay.Next() & 0x7FFFFFFF);
            job.env_seed = (int)(env.Next() & 0x7FFFFFFF);
            job.sensor_seed = (int)(sen.Next() & 0x7FFFFFFF);
            var f = g.factors;
            if (mode == Samplers.C && profile == null) throw new InvalidOperationException("mode C needs a profile");

            // ---------- environment (G prior for G and C: the real data has no environment labels) ----------
            var e = job.environment;
            if (mode == Samplers.CU)
            {
                e.lighting = Samplers.Lighting[env.Int(0, Samplers.Lighting.Length)];
                e.fog_visibility_scale = Samplers.Fog[env.Int(0, Samplers.Fog.Length)];
            }
            else
            {
                e.lighting = env.Pick(Samplers.Lighting);
                e.fog_visibility_scale = env.Pick(Samplers.Fog);
            }
            e.water = env.Pick(Samplers.Water);
            e.precipitation = env.Pick(Samplers.Precip);
            e.precipitation_intensity = e.precipitation == "none" ? 0f : env.Range(0.3f, 0.8f);
            e.sensor = sen.Pick(Samplers.Sensor);
            bool night = e.lighting.StartsWith("night");
            if (night && e.sensor == "typical") e.sensor = "night_ir";
            if (f.lighting.Count > 0) e.lighting = env.Pick(f.lighting);
            if (f.water.Count > 0) e.water = env.Pick(f.water);
            if (f.fog_visibility_scale.Count > 0) e.fog_visibility_scale = env.Pick(f.fog_visibility_scale);
            if (f.precipitation.Count > 0) { e.precipitation = env.Pick(f.precipitation); e.precipitation_intensity = e.precipitation == "none" ? 0f : 0.6f; }
            if (f.sensor.Count > 0) e.sensor = sen.Pick(f.sensor);
            e.water_time_s = lay.Range(4f, 40f);

            // ---------- objects ----------
            int n;
            if (mode == Samplers.C) n = Mathf.RoundToInt(profile.count_per_frame.Sample(lay));
            else n = lay.Int(1, 7);
            if (f.count_min >= 0 || f.count_max >= 0)
                n = lay.Int(Mathf.Max(1, f.count_min < 0 ? 1 : f.count_min), Mathf.Max(1, f.count_max < 0 ? g.max_vessels : f.count_max) + 1);
            n = Mathf.Clamp(n, 1, g.max_vessels);
            float truncP = mode == Samplers.C ? profile.truncated_fraction : 0.1f;
            for (int k = 0; k < n; k++)
            {
                var o = new ObjectRequest { object_id = $"t{k:00}" };
                if (mode == Samplers.C)
                {
                    o.target_height_frac = profile.height_frac.Sample(lay);
                    o.target_center_u = profile.center_u.Sample(lay);
                    o.target_bottom_v = profile.bottom_v.Sample(lay);
                }
                else if (mode == Samplers.CU)
                {
                    // rare-condition coverage: equal weight per height bin (log-uniform inside), uniform position
                    var edges = ProfileBuilder.HeightEdges;
                    int b = lay.Int(0, edges.Length - 1);
                    o.target_height_frac = Mathf.Exp(lay.Range(Mathf.Log(edges[b]), Mathf.Log(edges[b + 1])));
                    o.target_center_u = lay.Range(0.05f, 0.95f);
                    o.target_bottom_v = lay.Range(0.40f, 0.95f);
                }
                else
                {
                    o.target_height_frac = Mathf.Exp(lay.Range(Mathf.Log(0.008f), Mathf.Log(0.20f)));
                    o.target_center_u = lay.Range(0.05f, 0.95f);
                    o.target_bottom_v = lay.Range(0.40f, 0.95f);
                }
                if (f.height_frac_min > 0f || f.height_frac_max > 0f)
                {
                    float lo = f.height_frac_min > 0f ? f.height_frac_min : 0.004f, hi = f.height_frac_max > 0f ? f.height_frac_max : 0.25f;
                    o.target_height_frac = Mathf.Exp(lay.Range(Mathf.Log(lo), Mathf.Log(Mathf.Max(lo * 1.0001f, hi))));
                }
                o.heading_deg = f.heading_deg >= 0f ? f.heading_deg : lay.Range(0f, 360f);
                o.occlusion_target = mode == Samplers.CU ? (lay.NextDouble() < 0.5 ? "partial" : "none") : (lay.NextDouble() < 0.2 ? "partial" : "none");
                if (f.occlusion.Count > 0) o.occlusion_target = lay.Pick(f.occlusion);
                o.truncation_target = lay.NextDouble() < truncP ? (lay.NextDouble() < 0.5 ? "left" : "right") : "none";
                if (f.truncation.Count > 0) o.truncation_target = lay.Pick(f.truncation);
                if (o.truncation_target == "left") o.target_center_u = lay.Range(-0.02f, 0.03f);
                if (o.truncation_target == "right") o.target_center_u = lay.Range(0.97f, 1.02f);
                job.objects.Add(o);
            }
        }
    }

    /// <summary>Places the requested objects on the loaded map. Never replaces a request with an easier one: an object whose
    /// condition cannot be met within the retry limit makes the whole job "planning_failed" (kept in the job list, not rendered).</summary>
    public static class ScenePlanner
    {
        public static ScenarioConfig BaseScenario(ResearchJobInfo job, GenerationSpec g)
        {
            var s = SimulatorApp.DefaultScenarioFor(job.harbor_id);
            s.vessels.Clear();
            s.scenario_id = "untitled";
            s.camera_preset_id = job.camera_preset_id;
            s.camera_override = new CameraOverride();
            s.appearance = new AppearanceConfig { paint = VesselPaint.Original };
            var e = job.environment;
            s.environment.preset_id = e.lighting;
            s.environment.water_preset_id = e.water;
            s.environment.water_time_s = e.water_time_s;
            s.environment.lights = "auto";
            s.environment.weather = new WeatherConfig { precipitation = e.precipitation, intensity = e.precipitation_intensity, visibility_scale = e.fog_visibility_scale, seed = job.env_seed };
            s.sensor = SensorConfig.Preset(e.sensor);
            s.sensor.seed = job.sensor_seed;
            s.sensor.save_clean = s.sensor.enabled;
            s.capture.width = g.width; s.capture.height = g.height;
            s.capture.strict_placement = true;
            s.capture.write_mask = true;
            s.capture.mask_source = "gpu";
            s.navigation = new NavigationConfig { sim_time_s = job.sim_time_s };
            s.notes = "research job " + job.experiment_id + " / " + job.mode;
            return s;
        }

        struct Pred { public bool ok; public float h, cu, bv, trunc; public Rect box; public Rect unclipped; }

        static readonly Dictionary<string, Vector3[]> s_Reduced = new Dictionary<string, Vector3[]>();

        /// <summary>At most ~800 vertices (stride sample + the extreme vertex on each axis) for the row search; the chosen row is
        /// confirmed with every vertex.</summary>
        static Vector3[] Reduced(VesselAssetEntry e)
        {
            if (s_Reduced.TryGetValue(e.asset_id, out var r)) return r;
            var v = e.geometry.vertices_root_space;
            int stride = Mathf.Max(1, v.Length / 800);
            var l = new List<Vector3>();
            for (int i = 0; i < v.Length; i += stride) l.Add(v[i]);
            for (int ax = 0; ax < 3; ax++)
            {
                int lo = 0, hi = 0;
                for (int i = 1; i < v.Length; i++) { if (v[i][ax] < v[lo][ax]) lo = i; if (v[i][ax] > v[hi][ax]) hi = i; }
                l.Add(v[lo]); l.Add(v[hi]);
            }
            return s_Reduced[e.asset_id] = l.ToArray();
        }

        static Pred Predict(SimWorld w, LabelComputer.Projection proj, VesselAssetEntry e, float x, float z, float heading, float immersion, int W, int H, Vector3[] verts = null)
        {
            float waterY = w.Harbor.water_reference_y;
            float y = waterY - immersion - e.geometry.hull_reference_min_y;
            var m = Matrix4x4.TRS(new Vector3(x, y, z), Quaternion.Euler(0f, heading, 0f), Vector3.one);
            float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
            int n = 0;
            foreach (var v in verts ?? e.geometry.vertices_root_space)
            {
                var p = m.MultiplyPoint3x4(v);
                if (p.y <= waterY) continue;
                if (!LabelComputer.Project(proj, p, out var px)) return new Pred { ok = false };
                x0 = Mathf.Min(x0, px.x); y0 = Mathf.Min(y0, px.y); x1 = Mathf.Max(x1, px.x); y1 = Mathf.Max(y1, px.y); n++;
            }
            if (n == 0) return new Pred { ok = false };
            var un = Rect.MinMaxRect(x0, y0, x1, y1);
            var cl = Rect.MinMaxRect(Mathf.Clamp(x0, 0, W), Mathf.Clamp(y0, 0, H), Mathf.Clamp(x1, 0, W), Mathf.Clamp(y1, 0, H));
            float area = Mathf.Max(1e-6f, un.width * un.height);
            return new Pred
            {
                ok = cl.width > 0 && cl.height > 0, h = cl.height / H, cu = cl.center.x / W, bv = cl.yMax / H, box = cl, unclipped = un,
                trunc = 1f - cl.width * cl.height / area,
            };
        }

        static bool WaterHit(Camera cam, float u, float v, float waterY, out Vector2 xz, out float dist)
        {
            xz = default; dist = 0;
            var ray = cam.ViewportPointToRay(new Vector3(u, 1f - v, 0f));
            if (ray.direction.y >= -1e-4f) return false;
            float t = (waterY - ray.origin.y) / ray.direction.y;
            if (t <= 0f) return false;
            var p = ray.GetPoint(t);
            xz = new Vector2(p.x, p.z);
            dist = Vector2.Distance(xz, new Vector2(ray.origin.x, ray.origin.z));
            return true;
        }

        static Vector2[] Footprint(VesselAssetEntry e, float x, float z, float heading, float grow) =>
            Geometry2D.OrientedRect(new Vector2(x, z), heading,
                new Vector2(e.geometry.bounds_min.x, e.geometry.bounds_min.z) * grow, new Vector2(e.geometry.bounds_max.x, e.geometry.bounds_max.z) * grow);

        static List<VesselAssetEntry> Candidates(SimWorld w, string region)
        {
            var rules = w.Harbor.vessel_rules;
            var ids = rules.Count == 0 ? w.Catalog.entries.Select(e => e.asset_id) : rules.Where(r => r.regions.Count == 0 || (region != null && r.regions.Contains(region))).Select(r => r.asset_id);
            return ids.Distinct().OrderBy(s => s, StringComparer.Ordinal).Select(id => w.Catalog.Find(id)).Where(e => e != null && e.geometry != null).ToList();
        }

        /// <summary>Plans every object of the job on the currently loaded map. Returns the scenario (null when planning failed).</summary>
        public static ScenarioConfig Plan(SimWorld w, ResearchJobInfo job, GenerationSpec g)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var cfg = BaseScenario(job, g);
            cfg.scenario_id = job.experiment_id + "__plan";
            w.Apply(cfg);                     // empty scene: harbour, camera, environment
            var cam = w.CctvCamera;
            int W = g.width, H = g.height;
            float prevAspect = cam.aspect;
            cam.aspect = (float)W / H;
            var proj = LabelComputer.MakeProjection(cam, W, H);
            float waterY = w.Harbor.water_reference_y;
            var tol = Mathf.Log(1f + g.size_tolerance_rel);
            var placed = new List<(ObjectRequest o, VesselAssetEntry e)>();
            var lay = new Rng64(Rng64.SeedOf("solver", job.layout_seed));
            var targets = job.objects.Where(o => o.role == "target").OrderByDescending(o => o.target_height_frac).ToList();
            bool failed = false;
            try
            {
                foreach (var o in targets)
                {
                    if (!PlaceObject(w, cam, proj, o, placed, g, lay, tol, W, H)) { failed = true; continue; }
                    if (o.occlusion_target == "partial")
                    {
                        var occ = new ObjectRequest { object_id = o.object_id + "_occ", role = "occluder", occludes = o.object_id, heading_deg = o.heading_deg };
                        job.objects.Add(occ);
                        if (!PlaceOccluder(w, cam, proj, o, occ, placed, g, lay, W, H)) { failed = true; o.plan_status = "unmet"; o.unmet_reason = "occluder_unplaceable: " + occ.unmet_reason; }
                    }
                }
            }
            finally { cam.aspect = prevAspect; }
            job.planning_attempts = job.objects.Sum(o => o.attempts);
            job.planning_ms = sw.Elapsed.TotalMilliseconds;
            job.plan_failure_reasons = job.objects.Where(o => o.plan_status != "planned").Select(o => $"{o.object_id}: {o.unmet_reason}").ToList();
            if (failed || job.plan_failure_reasons.Count > 0) { job.plan_status = "planning_failed"; return null; }
            foreach (var (o, e) in placed)
                cfg.vessels.Add(new VesselPlacement { object_id = o.object_id, asset_id = e.asset_id, x = o.x, z = o.z, heading_deg = o.heading_deg, immersion_m = o.immersion_m });
            // final check with the real placement rules (land, structures, region realism) on the spawned hulls
            var reports = w.Apply(cfg);
            var bad = reports.Where(r => !r.ok).ToList();
            if (bad.Count > 0)
            {
                foreach (var r in bad)
                {
                    var o = job.objects.FirstOrDefault(x => x.object_id == r.object_id);
                    if (o != null) { o.plan_status = "unmet"; o.unmet_reason = "placement_rule: " + string.Join(",", r.warnings); }
                }
                job.plan_failure_reasons = job.objects.Where(o => o.plan_status != "planned").Select(o => $"{o.object_id}: {o.unmet_reason}").ToList();
                job.plan_status = "planning_failed";
                return null;
            }
            job.plan_status = "planned";
            job.planning_ms = sw.Elapsed.TotalMilliseconds;
            return cfg;
        }

        static bool Overlaps(SimWorld w, VesselAssetEntry e, float x, float z, float heading, List<(ObjectRequest o, VesselAssetEntry e)> placed)
        {
            var fp = Footprint(e, x, z, heading, 1.25f);
            foreach (var (po, pe) in placed)
                if (Geometry2D.ConvexOverlap(fp, Footprint(pe, po.x, po.z, po.heading_deg, 1.25f))) return true;
            // the same tests SimWorld.CheckPlacement applies after spawning: footprint inside the water regions and no harbour
            // structure (fenders excepted) in the waterline band [-1, +1] m of the oriented hull box
            var g = e.geometry;
            var raw = Footprint(e, x, z, heading, 1f);
            for (int i = 0; i < 4; i++)
                if (!w.Harbor.InAnyWaterRegion(raw[i]) || !w.Harbor.InAnyWaterRegion((raw[i] + raw[(i + 1) % 4]) * 0.5f)) return true;
            var rot = Quaternion.Euler(0, heading, 0);
            var center = new Vector3(x, w.Harbor.water_reference_y, z) + rot * new Vector3((g.bounds_min.x + g.bounds_max.x) / 2, 0, (g.bounds_min.z + g.bounds_max.z) / 2);
            var half = new Vector3((g.bounds_max.x - g.bounds_min.x) / 2 * 1.05f, 1f, (g.bounds_max.z - g.bounds_min.z) / 2 * 1.05f);
            var harborSet = new HashSet<Collider>(w.HarborColliders);
            foreach (var col in Physics.OverlapBox(center, half, rot, ~0, QueryTriggerInteraction.Ignore))
                if (harborSet.Contains(col) && !col.gameObject.name.StartsWith("fender")) return true;
            return false;
        }

        /// <summary>Size is the primary condition. With position_rule size_primary_nearest_feasible_row the requested image column (u)
        /// is kept and the row (bottom_v) moves to the nearest one where an allowed asset meets the size within the tolerance; the
        /// shift is recorded (position_adjusted_dv) and shows up in the requested-vs-achieved position histograms. strict_position
        /// keeps the requested row and fails instead. Nothing is replaced by a different size, truncation or occlusion target.</summary>
        static bool PlaceObject(SimWorld w, Camera cam, LabelComputer.Projection proj, ObjectRequest o, List<(ObjectRequest, VesselAssetEntry)> placed,
            GenerationSpec g, Rng64 rng, float tol, int W, int H)
        {
            float waterY = w.Harbor.water_reference_y;
            string last = "no_attempt";
            bool strict = g.position_rule == "strict_position";
            for (int a = 0; a < g.retry_limit; a++)
            {
                o.attempts++;
                float j = a == 0 ? 0f : 0.01f * a;
                float u = o.target_center_u + rng.Range(-j, j);
                float vReq = Mathf.Clamp(o.target_bottom_v, 0.005f, 0.995f);
                var rows = new List<float> { vReq };
                if (!strict)
                    for (float v = 0.01f; v < 0.996f; v += 0.005f) if (Mathf.Abs(v - vReq) > 1e-4f) rows.Add(v);
                rows = rows.OrderBy(v => Mathf.Abs(v - vReq)).ToList();
                float closestErr = float.MaxValue, closestV = -1f; string closestAsset = "";
                int water = 0, overl = 0, trunc = 0;
                foreach (var v in rows)
                {
                    o.rows_tried++;
                    if (!WaterHit(cam, u, v, waterY, out var xz, out var dist)) continue;
                    string region = w.Harbor.WaterRegionAt(xz);
                    if (region == null) continue;
                    var cands = Candidates(w, region);
                    if (cands.Count == 0) continue;
                    water++;
                    VesselAssetEntry best = null; float bestErr = float.MaxValue;
                    foreach (var e in cands)
                    {
                        var p = Predict(w, proj, e, xz.x, xz.y, o.heading_deg, e.default_immersion_m, W, H, Reduced(e));
                        if (!p.ok) continue;
                        float err = Mathf.Abs(Mathf.Log(Mathf.Max(1e-6f, p.h) / o.target_height_frac));
                        if (err < bestErr - 1e-6f) { bestErr = err; best = e; }
                    }
                    if (best == null) continue;
                    if (bestErr < closestErr) { closestErr = bestErr; closestV = v; closestAsset = best.asset_id; }
                    if (bestErr > tol * 1.15f) continue;
                    var fp = Predict(w, proj, best, xz.x, xz.y, o.heading_deg, best.default_immersion_m, W, H);
                    float ferr = fp.ok ? Mathf.Abs(Mathf.Log(Mathf.Max(1e-6f, fp.h) / o.target_height_frac)) : float.MaxValue;
                    if (ferr > tol) continue;
                    if (o.truncation_target != "none" && fp.trunc < 0.05f) { trunc++; continue; }
                    if (o.truncation_target == "none" && fp.trunc > 0.02f) { trunc++; continue; }
                    if (Overlaps(w, best, xz.x, xz.y, o.heading_deg, placed)) { overl++; continue; }
                    o.asset_id = best.asset_id; o.x = xz.x; o.z = xz.y; o.immersion_m = best.default_immersion_m; o.water_region = region;
                    o.predicted_height_frac = fp.h; o.predicted_center_u = fp.cu; o.predicted_bottom_v = fp.bv; o.predicted_truncation_fraction = fp.trunc;
                    o.position_adjusted_dv = v - vReq;
                    o.plan_status = "planned"; o.unmet_reason = "";
                    o.attempt_log.Add($"a{a}: planned {best.asset_id} at {dist:F0} m, row v {v:F3} (requested {vReq:F3}, dv {v - vReq:+0.000;-0.000}), predicted h {fp.h:F4}");
                    placed.Add((o, best));
                    return true;
                }
                last = closestV < 0f ? (water == 0 ? "column_has_no_water_region" : "no_candidate_projects_into_view")
                    : $"size_unreachable{(strict ? "_at_requested_row" : "_on_column")}: closest {closestAsset} |log ratio| {closestErr:F2} at v {closestV:F3} (tol {tol:F2}; truncation rejects {trunc}, overlap rejects {overl})";
                o.attempt_log.Add($"a{a}: u {u:F3}: {last}");
            }
            o.plan_status = "unmet"; o.unmet_reason = last;
            return false;
        }

        /// <summary>Puts a vessel nearer to the camera so that its predicted box covers 30–70 % of the target's predicted box width.
        /// Box overlap is a planning proxy; the achieved geometric occlusion is measured after rendering.</summary>
        static bool PlaceOccluder(SimWorld w, Camera cam, LabelComputer.Projection proj, ObjectRequest target, ObjectRequest occ,
            List<(ObjectRequest, VesselAssetEntry)> placed, GenerationSpec g, Rng64 rng, int W, int H)
        {
            float waterY = w.Harbor.water_reference_y;
            var tEntry = w.Catalog.Find(target.asset_id);
            var tP = Predict(w, proj, tEntry, target.x, target.z, target.heading_deg, target.immersion_m, W, H);
            string last = "no_attempt";
            for (int a = 0; a < g.retry_limit; a++)
            {
                occ.attempts++;
                // a point in front of the target along the same image column band, lower in the image (closer to the camera)
                float dv = rng.Range(0.02f, 0.10f), du = rng.Range(-0.6f, 0.6f) * tP.box.width / W;
                float u = tP.cu + du, v = Mathf.Min(0.985f, tP.bv + dv);
                if (!WaterHit(cam, u, v, waterY, out var xz, out _)) { last = "ray_does_not_reach_water"; occ.attempt_log.Add($"a{a}: {last}"); continue; }
                string region = w.Harbor.WaterRegionAt(xz);
                if (region == null) { last = "occluder_not_on_water"; occ.attempt_log.Add($"a{a}: {last}"); continue; }
                VesselAssetEntry best = null; Pred bestP = default; float bestScore = float.MaxValue, bestOv = 0f;
                foreach (var e in Candidates(w, region))
                {
                    var p = Predict(w, proj, e, xz.x, xz.y, occ.heading_deg, e.default_immersion_m, W, H);
                    if (!p.ok || p.trunc > 0.02f) continue;
                    float ox = Mathf.Max(0f, Mathf.Min(p.box.xMax, tP.box.xMax) - Mathf.Max(p.box.xMin, tP.box.xMin));
                    float oy = Mathf.Max(0f, Mathf.Min(p.box.yMax, tP.box.yMax) - Mathf.Max(p.box.yMin, tP.box.yMin));
                    float ov = ox * oy / Mathf.Max(1e-6f, tP.box.width * tP.box.height);
                    if (ov < 0.3f || ov > 0.7f) continue;
                    float score = Mathf.Abs(ov - 0.5f);
                    if (score < bestScore) { bestScore = score; best = e; bestP = p; bestOv = ov; }
                }
                if (best == null) { last = "no_candidate_gives_30_70pct_box_overlap"; occ.attempt_log.Add($"a{a}: {last}"); continue; }
                if (Overlaps(w, best, xz.x, xz.y, occ.heading_deg, placed)) { last = "overlaps_vessel_or_structure"; occ.attempt_log.Add($"a{a}: {last}"); continue; }
                occ.asset_id = best.asset_id; occ.x = xz.x; occ.z = xz.y; occ.immersion_m = best.default_immersion_m; occ.water_region = region;
                occ.predicted_height_frac = bestP.h; occ.predicted_center_u = bestP.cu; occ.predicted_bottom_v = bestP.bv;
                occ.predicted_truncation_fraction = bestP.trunc;
                target.predicted_box_overlap_fraction = bestOv;
                occ.plan_status = "planned";
                occ.attempt_log.Add($"a{a}: planned {best.asset_id}, predicted box overlap {bestOv:F2}");
                placed.Add((occ, best));
                return true;
            }
            occ.plan_status = "unmet"; occ.unmet_reason = last;
            return false;
        }
    }

    // ------------------------------------------------------------------------------------------------------------------
    // Freezing a job list: scenario files + jobs.json (portcctv.batch.v1 with research info), plan report, estimate.
    // ------------------------------------------------------------------------------------------------------------------

    [Serializable]
    public class PlanReport
    {
        public string report_schema = "portcctv.research.planreport.v1";
        public string experiment_id, contract_sha256, created_utc, jobs_file, profile_id, profile_kind;
        public int jobs_total, jobs_planned, jobs_planning_failed, objects_requested, objects_unmet, render_jobs;
        public float estimated_mb, estimated_minutes;
        public int render_cap;
        public List<string> failures = new List<string>();
        public List<string> notes = new List<string>();
    }

    public static class JobFreezer
    {
        /// <summary>Plans every (mode, map, camera, seed, capture) cell of the contract plus the diagnostic extras, writes the
        /// frozen job folder and returns the jobs file. Yields between jobs (call from a coroutine).</summary>
        public static IEnumerator PlanAndFreeze(SimWorld w, ResearchContract c, string contractPath, ScreenProfile profile, string jobsDir,
            Action<string> log, Action<string, PlanReport> done, bool includeExtras = true)
        {
            var g = c.generation;
            var csha = ContractIO.Hash(c);
            if (Directory.Exists(jobsDir)) { log?.Invoke("ERROR jobs folder exists (not overwriting): " + jobsDir); done?.Invoke(null, null); yield break; }
            var v = ContractValidator.Validate(c);
            if (!v.Ok) { log?.Invoke("ERROR contract invalid: " + v); done?.Invoke(null, null); yield break; }
            if (g.modes.Contains(Samplers.C))
            {
                if (profile == null) { log?.Invoke("ERROR mode C needs a profile"); done?.Invoke(null, null); yield break; }
                try { ProfileBuilder.CheckUsable(profile, c, null); }
                catch (Exception ex) { log?.Invoke("ERROR profile not usable: " + ex.Message); done?.Invoke(null, null); yield break; }
            }
            Directory.CreateDirectory(Path.Combine(jobsDir, "scenarios"));
            var batch = new BatchFile { batch_id = c.experiment_id + "_" + Path.GetFileName(jobsDir), output_dir = c.output_dir + "/" + Path.GetFileName(jobsDir) + "/captures" };
            var rep = new PlanReport
            {
                experiment_id = c.experiment_id, contract_sha256 = csha, created_utc = DateTime.UtcNow.ToString("o"),
                profile_id = profile?.profile_id ?? "", profile_kind = profile?.profile_kind ?? "", render_cap = g.render_budget.max_captures_this_run,
            };
            if (profile != null && profile.profile_kind == "fixture_not_real_data") rep.notes.Add("C uses a FIXTURE profile: not real-data statistics");
            string profileSha = profile != null ? ScenarioIO.Sha256Hex(ProfileBuilder.ToJson(profile)) : "";
            var jobs = new List<(ResearchJobInfo info, ScenarioConfig cfg)>();
            var buildRec = BuildManifestInfo.Record();
            rep.notes.Add(buildRec.origin == "player" ? "jobs require build " + buildRec.content_sha256 : "Editor run: no build hash requirement (use a player build for research batches)");

            ResearchJobInfo NewJob(string mode, string hid, string cam, int seed) => new ResearchJobInfo
            {
                experiment_id = c.experiment_id, contract_sha256 = csha, freeze_id = c.freeze.freeze_id, mode = mode,
                sampler_version = Samplers.VersionOf(mode), scene_kind = "planned", harbor_id = hid, camera_preset_id = cam, generation_seed = seed,
                profile_id = mode == Samplers.C ? profile?.profile_id ?? "" : "", profile_kind = mode == Samplers.C ? profile?.profile_kind ?? "" : "",
                profile_sha256 = mode == Samplers.C ? profileSha : "", occlusion_reference = g.occlusion_reference, size_tolerance_rel = g.size_tolerance_rel,
                retry_limit = g.retry_limit, label_policy_version = c.labels.visibility_policy_version, class_map_version = c.labels.class_map_version,
                primary_bbox_policy = c.labels.primary_bbox_policy + "/" + c.labels.primary_bbox_policy_version, split = "pretrain",
                // a player freezes jobs for its own build: captures from any other build are never reused or accepted
                required_build_content_sha256 = buildRec.origin == "player" ? buildRec.content_sha256 : "",
            };

            foreach (var m in g.maps)
                foreach (var cam in m.camera_preset_ids)
                    foreach (var mode in g.modes)
                        foreach (var seed in g.seeds)
                            for (int k = 0; k < g.captures_per_cell; k++)
                            {
                                int s = seed * 1000 + k;
                                var job = NewJob(mode, m.harbor_id, cam, s);
                                ConditionSampler.Sample(mode, mode == Samplers.C ? profile : null, g, job);
                                ScenarioConfig cfg = null;
                                try { cfg = ScenePlanner.Plan(w, job, g); }
                                catch (Exception ex) { job.plan_status = "planning_failed"; job.plan_failure_reasons.Add("exception: " + ex.Message); }
                                jobs.Add((job, cfg));
                                log?.Invoke($"plan {mode} {cam} seed {s}: {job.plan_status} ({job.objects.Count} objects, {job.planning_attempts} attempts)");
                                yield return null;
                            }

            if (includeExtras)
            {
                // empty-harbour negatives (vessel list empty; hard-negative status decided after rendering from what is in view)
                foreach (var m in g.maps)
                    foreach (var cam in m.camera_preset_ids.Take(g.empty_negatives_per_camera > 0 ? m.camera_preset_ids.Count : 0))
                        for (int k = 0; k < g.empty_negatives_per_camera; k++)
                        {
                            var job = NewJob(Samplers.G, m.harbor_id, cam, 900 + k);
                            ConditionSampler.Sample(Samplers.G, null, g, job);
                            job.objects.Clear(); job.scene_kind = "empty_negative"; job.mode = "NEG"; job.sampler_version = "empty_scene_v1";
                            var cfg = ScenePlanner.BaseScenario(job, g);
                            job.plan_status = "planned";
                            jobs.Add((job, cfg));
                        }
                // boundary: an unreachable size request must fail planning (recorded), not be replaced by an easier scene
                for (int k = 0; k < g.boundary_unreachable_cases && g.maps.Count > 0; k++)
                {
                    var m = g.maps[0];
                    var job = NewJob(Samplers.G, m.harbor_id, m.camera_preset_ids[0], 800 + k);
                    ConditionSampler.Sample(Samplers.G, null, g, job);
                    job.scene_kind = "boundary";
                    job.objects.RemoveRange(1, job.objects.Count - 1);
                    job.objects[0].target_height_frac = 0.9f; job.objects[0].occlusion_target = "none"; job.objects[0].truncation_target = "none";
                    job.objects[0].target_center_u = 0.5f; job.objects[0].target_bottom_v = 0.95f;
                    ScenarioConfig cfg = null;
                    try { cfg = ScenePlanner.Plan(w, job, g); } catch (Exception ex) { job.plan_status = "planning_failed"; job.plan_failure_reasons.Add(ex.Message); }
                    jobs.Add((job, cfg));
                    yield return null;
                }
                // paired factor variants of the first planned job: same camera / vessels / poses / water time / seeds; one factor changes
                var basePair = jobs.FirstOrDefault(j => j.cfg != null && j.info.scene_kind == "planned");
                if (basePair.cfg != null && g.paired.Count > 0)
                {
                    var gid = "pair_" + basePair.info.mode + "_" + basePair.info.camera_preset_id + "_" + basePair.info.generation_seed;
                    basePair.info.pair_group_id = gid; basePair.info.changed_factor = "base";
                    foreach (var pf in g.paired)
                        foreach (var val in pf.values)
                        {
                            var info = JsonUtility.FromJson<ResearchJobInfo>(JsonUtility.ToJson(basePair.info));
                            var cfg = basePair.cfg.Clone();
                            info.scene_kind = "paired"; info.pair_group_id = gid; info.changed_factor = pf.factor; info.changed_value = val;
                            ApplyFactor(cfg, info, pf.factor, val);
                            jobs.Add((info, cfg));
                        }
                }
                // sequences: one planned scene per episode with vessels under way; frames at fixed intervals; split per episode
                for (int ep = 0; ep < g.sequence.episodes; ep++)
                {
                    var m = g.maps[ep % g.maps.Count];
                    var cam = m.camera_preset_ids[0];
                    var job = NewJob(Samplers.G, m.harbor_id, cam, 700 + ep);
                    ConditionSampler.Sample(Samplers.G, null, g, job);
                    // vessels move during an episode: a static occluder / border placement would not hold, so episodes request
                    // neither (declared rule; at most 3 moving vessels)
                    if (job.objects.Count > 3) job.objects.RemoveRange(3, job.objects.Count - 3);
                    foreach (var o in job.objects) { o.occlusion_target = "none"; o.truncation_target = "none"; o.target_center_u = Mathf.Clamp(o.target_center_u, 0.15f, 0.85f); }
                    ScenarioConfig cfg = null;
                    try { cfg = ScenePlanner.Plan(w, job, g); } catch (Exception ex) { job.plan_status = "planning_failed"; job.plan_failure_reasons.Add(ex.Message); }
                    string episode = $"ep_{c.experiment_id}_{ep:000}";
                    string split = EpisodeRules.Split(episode, g.sequence.val_fraction);   // decided before any frame is generated
                    for (int fi = 0; fi < g.sequence.frames_per_episode; fi++)
                    {
                        var info = JsonUtility.FromJson<ResearchJobInfo>(JsonUtility.ToJson(job));
                        info.scene_kind = "sequence"; info.episode_id = episode; info.frame_index = fi; info.split = split;
                        info.sim_time_s = fi * g.sequence.interval_s;
                        ScenarioConfig f = null;
                        if (cfg != null)
                        {
                            f = cfg.Clone();
                            foreach (var vp in f.vessels) vp.motion = new VesselMotion { mode = g.sequence.vessel_motion, speed_kn = 6f, seed = job.layout_seed };
                            f.navigation.sim_time_s = info.sim_time_s;
                        }
                        jobs.Add((info, f));
                    }
                    yield return null;
                }
            }

            // write
            foreach (var (info, cfg) in jobs)
            {
                string id = JobId(info);
                var bj = new BatchJob { job_id = id, research = info, max_attempts = 2 };
                if (cfg == null || info.plan_status == "planning_failed")
                {
                    info.plan_status = "planning_failed";
                    bj.expected_status = "planning_failed";
                    bj.use_inline = false; bj.scenario_path = "";
                    rep.jobs_planning_failed++;
                    rep.failures.Add(id + ": " + string.Join(" | ", info.plan_failure_reasons));
                }
                else
                {
                    cfg.scenario_id = id;
                    var json = ScenarioIO.ToJson(cfg);
                    var file = "scenarios/" + id + ".json";
                    File.WriteAllText(Path.Combine(jobsDir, file), json, new UTF8Encoding(false));
                    info.job_scenario_sha256 = ScenarioIO.Sha256Hex(json);
                    bj.scenario_path = file; bj.expected_status = "done";
                    rep.jobs_planned++;
                }
                rep.objects_requested += info.objects.Count(o => o.role == "target");
                rep.objects_unmet += info.objects.Count(o => o.plan_status == "unmet");
                batch.jobs.Add(bj);
            }
            if (batch.jobs.GroupBy(j => j.job_id).Any(x => x.Count() > 1)) { log?.Invoke("ERROR duplicate job ids"); done?.Invoke(null, null); yield break; }
            var outRoot = AppPaths.ResolveUnderRoot(batch.output_dir);
            var longest = batch.jobs.Select(j => (j.job_id, n: LongestCapturePath(outRoot, j.job_id))).OrderByDescending(x => x.n).First();
            if (longest.n > MaxCapturePath)
            {
                log?.Invoke($"ERROR capture paths too long: {longest.job_id} needs {longest.n} characters (max {MaxCapturePath}); use a shorter experiment id / jobs folder / app folder");
                done?.Invoke(null, null); yield break;
            }
            var epErr = EpisodeRules.Validate(batch.jobs.Select(j => j.research));
            if (epErr.Count > 0) { log?.Invoke("ERROR episode rules: " + string.Join("; ", epErr)); done?.Invoke(null, null); yield break; }
            batch.research_batch = true;
            batch.max_captures_this_run = g.render_budget.max_captures_this_run;
            rep.jobs_total = batch.jobs.Count;
            rep.render_jobs = rep.jobs_planned;
            rep.estimated_mb = rep.render_jobs * g.render_budget.estimated_mb_per_capture;
            rep.estimated_minutes = rep.render_jobs * g.render_budget.estimated_s_per_capture / 60f;
            var jobsFile = Path.Combine(jobsDir, "jobs.json");
            File.WriteAllText(jobsFile, JsonUtility.ToJson(batch, true), new UTF8Encoding(false));
            rep.jobs_file = jobsFile;
            File.WriteAllText(Path.Combine(jobsDir, "plan_report.json"), JsonUtility.ToJson(rep, true), new UTF8Encoding(false));
            if (!string.IsNullOrEmpty(contractPath) && File.Exists(contractPath))
                File.Copy(contractPath, Path.Combine(jobsDir, "contract.json"));
            else File.WriteAllText(Path.Combine(jobsDir, "contract.json"), ContractIO.ToJson(c), new UTF8Encoding(false));
            if (profile != null) File.WriteAllText(Path.Combine(jobsDir, "profile.json"), ProfileBuilder.ToJson(profile), new UTF8Encoding(false));
            log?.Invoke($"jobs frozen: {rep.jobs_total} jobs ({rep.jobs_planned} to render, {rep.jobs_planning_failed} planning_failed), est. {rep.estimated_mb:F0} MB / {rep.estimated_minutes:F1} min -> {jobsFile}");
            if (rep.render_jobs > rep.render_cap) log?.Invoke($"WARNING {rep.render_jobs} renders exceed the cap {rep.render_cap}: the batch runner will refuse this list");
            done?.Invoke(jobsFile, rep);
        }

        public static string JobId(ResearchJobInfo i)
        {
            string b = $"{i.mode}_{i.camera_preset_id}_s{i.generation_seed:0000}";
            if (i.scene_kind == "paired")
            {
                var v = ScenarioIO.SanitizeId(i.changed_value);
                b += $"_p{FactorCode(i.changed_factor)}_{(v.Length > 12 ? v.Substring(0, 12) : v)}";
            }
            if (i.scene_kind == "sequence") b = $"SEQ_{i.camera_preset_id}_{i.episode_id.Substring(i.episode_id.LastIndexOf('_') + 1)}_f{i.frame_index:000}";
            if (i.scene_kind == "boundary") b = "BND_" + b;
            return ScenarioIO.SanitizeId(b);
        }

        static string FactorCode(string f) => f switch
        {
            "lighting" => "lt", "water" => "wt", "fog_visibility_scale" => "fog", "precipitation" => "pr", "sensor" => "sn", _ => ScenarioIO.SanitizeId(f),
        };

        /// <summary>Windows MAX_PATH guard: the longest file a capture writes (inside the ".partial_&lt;utc&gt;" folder) must stay below
        /// 240 characters, otherwise the job list is refused at freeze time instead of failing during rendering.</summary>
        public const int MaxCapturePath = 240;
        public static int LongestCapturePath(string outputRoot, string jobId) =>
            Path.Combine(outputRoot, jobId + ".partial_20261110T000000000", "yolo_ignore_regions_provisional.txt").Length;

        public static void ApplyFactor(ScenarioConfig cfg, ResearchJobInfo info, string factor, string val)
        {
            var e = info.environment;
            switch (factor)
            {
                case "lighting":
                    if (!EnvironmentPresetLibrary.Exists(val)) throw new ArgumentException("unknown lighting " + val);
                    cfg.environment.preset_id = val; e.lighting = val; break;
                case "water":
                    if (!WaterPresetLibrary.Exists(val)) throw new ArgumentException("unknown water " + val);
                    cfg.environment.water_preset_id = val; e.water = val; break;
                case "fog_visibility_scale":
                    var f = float.Parse(val, CultureInfo.InvariantCulture);
                    cfg.environment.weather.visibility_scale = f; e.fog_visibility_scale = f; break;
                case "precipitation":
                    cfg.environment.weather.precipitation = val; cfg.environment.weather.intensity = val == "none" ? 0f : 0.6f;
                    e.precipitation = val; e.precipitation_intensity = cfg.environment.weather.intensity; break;
                case "sensor":
                    if (Array.IndexOf(SensorConfig.PresetIds, val) < 0) throw new ArgumentException("unknown sensor preset " + val);
                    int seed = cfg.sensor.seed;
                    cfg.sensor = SensorConfig.Preset(val); cfg.sensor.seed = seed; cfg.sensor.save_clean = cfg.sensor.enabled; e.sensor = val; break;
                default: throw new ArgumentException("unsupported paired factor " + factor);
            }
        }
    }
}
