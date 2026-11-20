using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace PortCCTV.Research
{
    /// <summary>Request for one moving-vessel episode in front of a FIXED CCTV camera (2026-11-16, JMSE scene rework).</summary>
    [Serializable]
    public class EpisodeSpec
    {
        public string experiment_id = "jmse_wx";
        public string harbor_id, camera_preset_id;
        public int seed;
        public string split = "";
        public int frames = 8;
        public int moving_min = 1, moving_max = 3, static_max = 1;
        public float height_frac_min = 0.025f, height_frac_max = 0.16f;   // target above-water box height of the primary mover / image height
        public float max_cross_angle_deg = 40f;                          // route direction vs. the image-plane direction
        public int retry_limit = 30;
        public int width = 1920, height = 1080;
        public string water_preset = "";                                 // "" = calm / light_chop by seed
        public string sensor_preset = "hd_ip";
        public float min_frame_motion_px = 2f;                           // in-view movers must move at least this much between frames

        // ---- framing profile (2026-11-19). With the defaults below the planner behaves exactly as on 2026-11-16.
        /// <summary>"" = fields as given (2026-11-16 behaviour). "auto" = <see cref="EpisodeProfiles.Apply"/> fills the fields
        /// below, the vessel counts, the height range and the crossing angle from the framing group of the camera preset.</summary>
        public string profile = "";
        /// <summary>Name of the profile that was applied (written by the planner; "" = none).</summary>
        public string profile_applied = "";
        /// <summary>Median (output pixels at the planned image height) and log-normal sigma of the target above-water box height.
        /// 0 = pick a row uniformly among the feasible rows (2026-11-16 behaviour).</summary>
        public float target_height_px_median = 0f, target_height_ln_sigma = 0f;
        /// <summary>A hull is used only if a feasible position gives a box height within this factor of the drawn target (0 = any).
        /// Without it a 240 m ship drawn for a 55 px target would always be placed as a frame-filling hull at near_max_m.</summary>
        public float target_height_tolerance = 0f;
        /// <summary>Moving vessels and "near" static vessels are placed at most this far from the camera (0 = no limit). It keeps
        /// most vessels inside the range that stays visible in the dense-fog cell of the weather matrix.</summary>
        public float near_max_m = 0f;
        /// <summary>Planned positions are at least this far from the camera (0 = no limit). From a low camera a vessel closer than
        /// about 100 m sits far below the horizon line at the bottom of the frame, which the real on-shore footage does not show.</summary>
        public float near_min_m = 0f;
        /// <summary>Static vessels placed beyond near_max_m, up to far_max_m (ships at anchor near the horizon).</summary>
        public int far_static_max = 0;
        public float far_max_m = 0f;
        public int static_min = 0;
        /// <summary>Static vessels of a sized profile share one heading per episode (vessels at anchor lie to the same wind and
        /// current) with this standard deviation in degrees; 0 = every static vessel gets its own uniform random heading.</summary>
        public float anchor_heading_sigma_deg = 0f;
        public float row_min = 0.30f, row_step = 0.01f;
    }

    /// <summary>
    /// Planner settings per framing group (framing_v2_20261119). The numbers are design choices taken from the measured real
    /// reference (Research/jmse/audit/framing_20261119): SMD VIS_Onshore train / validation boxes have a median height of 55 px
    /// (p10 24, p90 163 at 1080 rows) and 4 / 7 / 10 vessels per frame (p10 / median / p90). No SeaShips image was measured; the
    /// side-view profile only follows the published description (few, larger, mostly broadside vessels).
    /// </summary>
    public static class EpisodeProfiles
    {
        // v2 (same day): near range 340 -> 280 m and at most one far static vessel. The verification render of v1
        // (Captures/jmse/framing_20261119/verify_wx_r1) kept only 63% of the clear-weather labels in the dense-fog cell: 32% at
        // 310-340 m (luma contrast below the label threshold) against >= 84% inside 280 m.
        // v3 (same day): 3-6 near static vessels for the on-shore type (v2 planned a median of 4 vessels per frame against 7 in SMD).
        public const string Version = "episode_profiles_v3_20261119";

        public static void Apply(EpisodeSpec s, CameraPreset cam)
        {
            if (s.profile != "auto" || cam == null) return;
            if (cam.framing == CameraPresetLibrary.FramingSmd)
            {
                s.profile_applied = Version + ":smd_onshore";
                s.moving_min = 2; s.moving_max = 4; s.static_min = 3; s.static_max = 6; s.far_static_max = 1;
                s.height_frac_min = 0.020f; s.height_frac_max = 0.30f;
                s.target_height_px_median = 55f; s.target_height_ln_sigma = 0.75f; s.target_height_tolerance = 1.6f;
                s.near_min_m = 110f; s.near_max_m = 280f; s.far_max_m = 1500f;
                s.max_cross_angle_deg = 55f; s.anchor_heading_sigma_deg = 20f;
                s.row_min = 0.30f; s.row_step = 0.004f;
            }
            else if (cam.framing == CameraPresetLibrary.FramingSeaShips)
            {
                s.profile_applied = Version + ":seaships_side";
                s.moving_min = 1; s.moving_max = 2; s.static_min = 0; s.static_max = 2; s.far_static_max = 1;
                s.height_frac_min = 0.030f; s.height_frac_max = 0.36f;
                s.target_height_px_median = 100f; s.target_height_ln_sigma = 0.60f; s.target_height_tolerance = 1.8f;
                s.near_min_m = 60f; s.near_max_m = 280f; s.far_max_m = 900f;
                s.max_cross_angle_deg = 35f;
                s.row_min = 0.30f; s.row_step = 0.004f;
            }
            else s.profile_applied = Version + ":none(" + (cam.framing ?? "") + ")";
        }
    }

    [Serializable]
    public class EpisodeFrameVessel
    {
        public string object_id, asset_id, role, nav_state;
        public float x, z, heading_deg, speed_kn, camera_distance_m;
        public bool in_view; public float x_min, y_min, x_max, y_max;   // predicted above-water box (planning; labels come from the render)
    }

    [Serializable]
    public class EpisodeFrame
    {
        public int frame_index; public float sim_time_s; public string scenario_id;
        public int movers_in_view_underway;
        public List<EpisodeFrameVessel> vessels = new List<EpisodeFrameVessel>();
        public List<string> layout_issues = new List<string>();
    }

    [Serializable]
    public class EpisodeVesselPlan { public string object_id, asset_id, role, route_kind = ""; public Vector2 entry, exit; public float speed_kn, lead_in_m, length_m; public string region; }

    [Serializable]
    public class EpisodePlan
    {
        public string schema = "portcctv.research.episode.v1";
        public string episode_id, status = "pending", failure = "";
        public int attempts;
        public EpisodeSpec spec;
        public string placement_rules = PlacementQuality.Version;
        public List<EpisodeVesselPlan> vessels = new List<EpisodeVesselPlan>();
        public List<EpisodeFrame> frames = new List<EpisodeFrame>();
        public List<string> attempt_log = new List<string>();
        [NonSerialized] public ScenarioConfig scenario;   // start poses + routes; frame k = this with navigation.sim_time_s = frames[k].sim_time_s
        public string checks = "every frame: >=1 mover underway and in view; no frame-to-frame jump > 1.3 x speed x dt + 2 m; in-view movers move >= min_frame_motion_px; hull clearance between vessels >= 0.5 x (L1+L2)/2; no PlacementQuality same-asset issue";
    }

    /// <summary>
    /// Plans a reproducible episode: 1..N vessels enter the fixed CCTV view from outside, cross it on a straight route through the
    /// water (perpendicular to the line of sight +- max_cross_angle_deg) and leave it; optional static vessels. The planned scenario
    /// is simulated with the project's NavigationSim at the frame times and accepted only if every check holds (see EpisodePlan.checks);
    /// otherwise the next attempt draws new routes. Nothing is replaced silently: a failed episode is returned with its log.
    /// </summary>
    public static class EpisodePlanner
    {
        const float Knot = 0.514444f;

        public static ScenarioConfig BaseScenario(EpisodeSpec spec, System.Random rng)
        {
            var s = SimulatorApp.DefaultScenarioFor(spec.harbor_id);
            s.vessels.Clear();
            s.camera_preset_id = spec.camera_preset_id;
            s.camera_override = new CameraOverride();
            s.appearance = new AppearanceConfig { paint = VesselPaint.Original };
            s.environment.preset_id = "day_clear";
            s.environment.water_preset_id = string.IsNullOrEmpty(spec.water_preset) ? (rng.Next(2) == 0 ? "calm" : "light_chop") : spec.water_preset;
            s.environment.water_time_s = 4f + (float)rng.NextDouble() * 36f;
            s.environment.weather = new WeatherConfig { precipitation = "none", intensity = 0f, visibility_scale = 1f, seed = rng.Next(1, int.MaxValue) };
            s.environment.vessel_wave_motion = true;
            s.sensor = SensorConfig.Preset(spec.sensor_preset);
            s.sensor.seed = rng.Next(1, int.MaxValue);
            s.sensor.save_clean = s.sensor.enabled;
            s.capture.width = spec.width; s.capture.height = spec.height;
            s.capture.strict_placement = true; s.capture.write_mask = true; s.capture.mask_source = "gpu";
            s.navigation = new NavigationConfig { sim_time_s = 0f, collision_avoidance = true };
            s.notes = $"research episode {spec.experiment_id} / {spec.camera_preset_id} / seed {spec.seed}";
            return s;
        }

        static float Gauss(System.Random rng)
        {
            double u1 = 1.0 - rng.NextDouble(), u2 = rng.NextDouble();
            return (float)(Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2));
        }

        /// <summary>Target above-water box height in pixels (log-normal, clipped to the allowed range); -1 = no target.</summary>
        static float DrawTargetHeight(EpisodeSpec spec, System.Random rng)
        {
            if (spec.target_height_px_median <= 0f) return -1f;
            float h = spec.target_height_px_median * Mathf.Exp(spec.target_height_ln_sigma * Gauss(rng));
            return Mathf.Clamp(h, spec.height_frac_min * spec.height, spec.height_frac_max * spec.height);
        }

        static bool WaterHit(Camera cam, float u, float v, float waterY, out Vector2 xz)
        {
            xz = default;
            var ray = cam.ViewportPointToRay(new Vector3(u, 1f - v, 0f));
            if (ray.direction.y >= -1e-4f) return false;
            float t = (waterY - ray.origin.y) / ray.direction.y;
            if (t <= 0f) return false;
            var p = ray.GetPoint(t); xz = new Vector2(p.x, p.z); return true;
        }

        static bool PointClear(SimWorld w, VesselAssetEntry e, Vector2 p, float heading, string asset, HashSet<Collider> harbor)
        {
            var g = e.geometry;
            float L = g.bounds_max.x - g.bounds_min.x, B = g.bounds_max.z - g.bounds_min.z;
            var reg = w.Harbor.WaterRegionAt(p);
            if (reg == null) return false;
            var warn = w.Harbor.RealismWarning(asset, reg);
            if (warn != null && warn.StartsWith("vessel_unusual_in_region:")) return false;
            var rot = Quaternion.Euler(0, heading, 0);
            foreach (var corner in Geometry2D.OrientedRect(p, heading, new Vector2(g.bounds_min.x, g.bounds_min.z), new Vector2(g.bounds_max.x, g.bounds_max.z)))
                if (!w.Harbor.InAnyWaterRegion(corner)) return false;
            var c = new Vector3(p.x, w.Harbor.water_reference_y, p.y) + rot * new Vector3((g.bounds_min.x + g.bounds_max.x) / 2, 0, (g.bounds_min.z + g.bounds_max.z) / 2);
            var half = new Vector3(L / 2 + 3f, 1f, B / 2 + 3f);
            return !Physics.OverlapBox(c, half, rot, ~0, QueryTriggerInteraction.Ignore).Any(col => harbor.Contains(col) && !col.gameObject.name.StartsWith("fender"));
        }

        /// <summary>Clear distance from p along dir (stops at the first blocked sample, at most maxS).</summary>
        static float ClearExtent(SimWorld w, VesselAssetEntry e, Vector2 p, Vector2 dir, float maxS, HashSet<Collider> harbor)
        {
            float heading = NavigationSim.HeadingOf(dir), step = Mathf.Max(4f, (e.geometry.bounds_max.x - e.geometry.bounds_min.x) * 0.25f), last = 0f;
            for (float s = 0f; s <= maxS; s += step) { if (!PointClear(w, e, p + dir * s, heading, e.asset_id, harbor)) return last; last = s; }
            return maxS;
        }

        static bool RouteClear(SimWorld w, VesselAssetEntry e, Vector2 a, Vector2 b, string asset)
        {
            var g = e.geometry;
            float L = g.bounds_max.x - g.bounds_min.x, B = g.bounds_max.z - g.bounds_min.z;
            var dir = (b - a).normalized; float heading = NavigationSim.HeadingOf(dir);
            var rot = Quaternion.Euler(0, heading, 0);
            var harbor = new HashSet<Collider>(w.HarborColliders);
            float len = (b - a).magnitude;
            for (float s = 0; s <= len; s += Mathf.Max(4f, L * 0.25f))
            {
                var p = a + dir * s;
                var reg = w.Harbor.WaterRegionAt(p);
                if (reg == null) return false;
                var warn = w.Harbor.RealismWarning(asset, reg);
                if (warn != null && warn.StartsWith("vessel_unusual_in_region:")) return false;
                // the same hull-box / structure test the planners use, with a margin for the swept path
                var c = new Vector3(p.x, w.Harbor.water_reference_y, p.y) + rot * new Vector3((g.bounds_min.x + g.bounds_max.x) / 2, 0, (g.bounds_min.z + g.bounds_max.z) / 2);
                var half = new Vector3(L / 2 + 3f, 1f, B / 2 + 3f);
                if (Physics.OverlapBox(c, half, rot, ~0, QueryTriggerInteraction.Ignore).Any(col => harbor.Contains(col) && !col.gameObject.name.StartsWith("fender"))) return false;
            }
            return true;
        }

        public static EpisodePlan Plan(SimWorld w, EpisodeSpec spec)
        {
            var plan = new EpisodePlan { spec = spec, episode_id = $"ep_{spec.camera_preset_id}_s{spec.seed:0000}" };
            var rng = new System.Random(unchecked((int)Rng64.SeedOf("episode", spec.experiment_id, spec.harbor_id, spec.camera_preset_id, spec.seed.ToString())));
            EpisodeProfiles.Apply(spec, CameraPresetLibrary.Exists(spec.camera_preset_id) ? CameraPresetLibrary.Get(spec.camera_preset_id) : null);
            var baseCfg = BaseScenario(spec, rng);
            var probe = JsonUtility.FromJson<ScenarioConfig>(JsonUtility.ToJson(baseCfg));
            w.Apply(probe);
            var cam = w.CctvCamera;
            float prevAspect = cam.aspect; cam.aspect = (float)spec.width / spec.height;
            try
            {
                var pool = DefaultSceneLayout.Pools.TryGetValue(spec.harbor_id, out var pl) ? pl : w.Catalog.entries.Select(e => e.asset_id).ToArray();
                var proj = LabelComputer.MakeProjection(cam, spec.width, spec.height);
                float waterY = w.Harbor.water_reference_y;
                var camXZ = new Vector2(cam.transform.position.x, cam.transform.position.z);
                var fwd = new Vector2(cam.transform.forward.x, cam.transform.forward.z).normalized;
                var right = new Vector2(fwd.y, -fwd.x);
                float hHalf = Mathf.Atan(Mathf.Tan(cam.fieldOfView * Mathf.Deg2Rad * 0.5f) * cam.aspect);
                for (int attempt = 0; attempt < spec.retry_limit; attempt++)
                {
                    plan.attempts = attempt + 1;
                    plan.vessels.Clear(); plan.frames.Clear();
                    var cfg = JsonUtility.FromJson<ScenarioConfig>(JsonUtility.ToJson(baseCfg));
                    var used = new HashSet<string>();
                    int nMove = rng.Next(spec.moving_min, spec.moving_max + 1);
                    string fail = null;
                    float tEnter0 = 0f, tExit0 = 0f;
                    var harborCols = new HashSet<Collider>(w.HarborColliders);
                    for (int k = 0; k < nMove && fail == null; k++)
                    {
                        bool placed = false;
                        for (int vt = 0; vt < 8 && !placed; vt++)
                        {
                            var cands = pool.Where(a => !used.Contains(a)).Select(a => w.Catalog.Find(a)).Where(e0 => e0?.geometry != null).ToList();
                            if (cands.Count == 0) break;
                            var e = cands[rng.Next(cands.Count)];
                            float L = e.geometry.bounds_max.x - e.geometry.bounds_min.x;
                            float sign = rng.Next(2) == 0 ? 1f : -1f;
                            float ang = ((float)rng.NextDouble() * 2f - 1f) * spec.max_cross_angle_deg * Mathf.Deg2Rad;
                            var dir = (right * Mathf.Cos(ang) + fwd * Mathf.Sin(ang)).normalized * sign;
                            float heading = NavigationSim.HeadingOf(dir);
                            // rows of a random image column whose water point gives a box height inside [min, max] (feasible for this camera)
                            float u0 = 0.2f + 0.6f * (float)rng.NextDouble();
                            float hTarget = DrawTargetHeight(spec, rng);
                            var rows = new List<(Vector2 xz, string reg, float h)>();
                            for (float v = spec.row_min; v <= 0.97f; v += spec.row_step)
                            {
                                if (!WaterHit(cam, u0, v, waterY, out var xz)) continue;
                                if (spec.near_max_m > 0f && (xz - camXZ).magnitude > spec.near_max_m) continue;
                                if (spec.near_min_m > 0f && (xz - camXZ).magnitude < spec.near_min_m) continue;
                                var reg = w.Harbor.WaterRegionAt(xz);
                                if (reg == null) continue;
                                if (!ScenePlanner.PredictBox(w, proj, e, xz.x, xz.y, heading, e.default_immersion_m, spec.width, spec.height, out var box)) continue;
                                float h = box.height / spec.height;
                                if (h >= spec.height_frac_min && h <= spec.height_frac_max) rows.Add((xz, reg, h));
                            }
                            if (rows.Count == 0) { plan.attempt_log.Add($"a{attempt} m{k} {e.asset_id}: no row with height in range at u {u0:F2}"); continue; }
                            var pick = hTarget > 0f ? rows.OrderBy(r => Mathf.Abs(Mathf.Log(r.h * spec.height / hTarget))).First() : rows[rng.Next(rows.Count)];
                            if (hTarget > 0f && spec.target_height_tolerance > 1f && Mathf.Abs(Mathf.Log(pick.h * spec.height / hTarget)) > Mathf.Log(spec.target_height_tolerance))
                            { plan.attempt_log.Add($"a{attempt} m{k} {e.asset_id}: no position within x{spec.target_height_tolerance:F1} of the target height {hTarget:F0} px"); continue; }
                            if (!PointClear(w, e, pick.xz, heading, e.asset_id, harborCols)) { plan.attempt_log.Add($"a{attempt} m{k} {e.asset_id}: centre point blocked"); continue; }
                            float depth = Vector2.Dot(pick.xz - camXZ, fwd);
                            float halfW = Mathf.Max(20f, depth * Mathf.Tan(hHalf));
                            float S = halfW * 1.15f + L;          // a full crossing starts and ends outside the view
                            float lead = k == 0 ? 0f : (float)rng.NextDouble() * S;
                            float fwdClear = ClearExtent(w, e, pick.xz, dir, S, harborCols);
                            float backClear = ClearExtent(w, e, pick.xz, -dir, S + lead, harborCols);
                            // accepted: full crossing, or a departure / arrival that leaves the view on one side and covers >= 2.5 L and 0.8 x half the view width
                            bool exitsAhead = fwdClear >= S - 1e-3f, entersBehind = backClear >= S - 1e-3f;
                            float covered = fwdClear + backClear;
                            string kind = exitsAhead && entersBehind ? "through" : exitsAhead ? "departure_in_view" : entersBehind ? "arrival_in_view" : "";
                            if (kind == "" || covered < Mathf.Max(2.5f * L, 0.8f * halfW)) { plan.attempt_log.Add($"a{attempt} m{k} {e.asset_id}: route blocked (clear {backClear:F0}+{fwdClear:F0} m of {2 * S:F0})"); continue; }
                            var entry = pick.xz - dir * Mathf.Min(backClear, S + lead); var exit = pick.xz + dir * fwdClear;
                            float spd = NavigationSim.DefaultSpeedKn(e.asset_id, L) * (0.8f + 0.4f * (float)rng.NextDouble());
                            string id = $"m{k:00}";
                            cfg.vessels.Add(new VesselPlacement
                            {
                                object_id = id, asset_id = e.asset_id, x = entry.x, z = entry.y, heading_deg = heading, immersion_m = e.default_immersion_m,
                                motion = new VesselMotion { mode = "route", speed_kn = spd, route = new List<Vector2> { exit }, loop = false, keep_in_view = false, seed = spec.seed },
                            });
                            plan.vessels.Add(new EpisodeVesselPlan { object_id = id, asset_id = e.asset_id, role = "moving", entry = entry, exit = exit, speed_kn = spd, lead_in_m = lead, length_m = L, region = pick.reg, route_kind = kind });
                            used.Add(e.asset_id);
                            placed = true;
                            if (k == 0)
                            {
                                float vms = spd * Knot, total = (exit - entry).magnitude;
                                float distToCentre = (entry - pick.xz).magnitude;
                                float inView = Mathf.Max(0f, distToCentre - halfW - 0.5f * L);
                                tEnter0 = inView / vms + Mathf.Min(L / Mathf.Max(0.5f, vms), 20f) * 0.5f;    // includes the speed-up from rest
                                tExit0 = Mathf.Min(total, distToCentre + Mathf.Min(fwdClear, halfW + 0.5f * L)) / vms;
                            }
                        }
                        if (!placed) fail = "mover_unplaceable";
                    }
                    if (fail != null) continue;
                    // optional static vessels (moored / drifting at rest) in view
                    bool sized = spec.target_height_px_median > 0f;
                    int nStatic = sized ? 0 : rng.Next(0, spec.static_max + 1);
                    if (sized)
                    {
                        // framing profile: static vessels get a target size like the movers; "near" ones stay inside near_max_m,
                        // "far" ones lie between near_max_m and far_max_m (ships at anchor close to the horizon line)
                        int nNear = rng.Next(spec.static_min, spec.static_max + 1), nFar = rng.Next(0, spec.far_static_max + 1);
                        float anchorHeading = (float)rng.NextDouble() * 360f;
                        for (int k = 0; k < nNear + nFar; k++)
                        {
                            bool far = k >= nNear;
                            for (int tries = 0; tries < 60; tries++)
                            {
                                var cands = pool.Where(a => !used.Contains(a)).Select(a => w.Catalog.Find(a)).Where(e0 => e0?.geometry != null).ToList();
                                if (cands.Count == 0) break;
                                var e = cands[rng.Next(cands.Count)];
                                float L = e.geometry.bounds_max.x - e.geometry.bounds_min.x;
                                float hd = spec.anchor_heading_sigma_deg > 0f ? Mathf.Repeat(anchorHeading + spec.anchor_heading_sigma_deg * Gauss(rng), 360f) : (float)rng.NextDouble() * 360f;
                                float u0 = 0.06f + 0.88f * (float)rng.NextDouble();
                                float hTarget = DrawTargetHeight(spec, rng);
                                (Vector2 xz, string reg, float h) best = default; float bestErr = float.MaxValue;
                                for (float v = spec.row_min; v <= 0.97f; v += spec.row_step)
                                {
                                    if (!WaterHit(cam, u0, v, waterY, out var xz)) continue;
                                    float dist = (xz - camXZ).magnitude;
                                    if (far ? (dist <= spec.near_max_m || dist > spec.far_max_m) : (spec.near_max_m > 0f && dist > spec.near_max_m)) continue;
                                    if (dist < spec.near_min_m) continue;
                                    var reg = w.Harbor.WaterRegionAt(xz);
                                    if (reg == null) continue;
                                    if (!ScenePlanner.PredictBox(w, proj, e, xz.x, xz.y, hd, e.default_immersion_m, spec.width, spec.height, out var box)) continue;
                                    float h = box.height / spec.height;
                                    if (h < spec.height_frac_min || h > spec.height_frac_max) continue;
                                    float err = Mathf.Abs(Mathf.Log(h * spec.height / hTarget));
                                    if (err < bestErr) { bestErr = err; best = (xz, reg, h); }
                                }
                                if (bestErr == float.MaxValue) continue;
                                if (spec.target_height_tolerance > 1f && bestErr > Mathf.Log(spec.target_height_tolerance)) continue;
                                if (!PointClear(w, e, best.xz, hd, e.asset_id, harborCols)) continue;
                                if (plan.vessels.Any(pv => DistToSegment(best.xz, pv.entry, pv.exit) < (pv.length_m + L) * 0.75f + 15f)) continue;
                                string id = $"s{k:00}";
                                cfg.vessels.Add(new VesselPlacement { object_id = id, asset_id = e.asset_id, x = best.xz.x, z = best.xz.y, heading_deg = hd, immersion_m = e.default_immersion_m });
                                plan.vessels.Add(new EpisodeVesselPlan { object_id = id, asset_id = e.asset_id, role = "static", entry = best.xz, exit = best.xz, region = best.reg, length_m = L, route_kind = far ? "static_far" : "static_near" });
                                used.Add(e.asset_id);
                                break;
                            }
                        }
                    }
                    for (int k = 0; k < nStatic; k++)
                    {
                        var cands = pool.Where(a => !used.Contains(a)).Select(a => w.Catalog.Find(a)).Where(e => e?.geometry != null).ToList();
                        if (cands.Count == 0) break;
                        var e = cands[rng.Next(cands.Count)];
                        for (int tries = 0; tries < 20; tries++)
                        {
                            if (!WaterHit(cam, 0.1f + 0.8f * (float)rng.NextDouble(), 0.4f + 0.55f * (float)rng.NextDouble(), waterY, out var xz)) continue;
                            var reg = w.Harbor.WaterRegionAt(xz);
                            if (reg == null) continue;
                            float hd = (float)rng.NextDouble() * 360f;
                            if (!RouteClear(w, e, xz, xz + NavigationSim.Dir(hd) * 0.01f, e.asset_id)) continue;
                            if (plan.vessels.Any(pv => DistToSegment(xz, pv.entry, pv.exit) < (pv.length_m + e.geometry.bounds_max.x - e.geometry.bounds_min.x) * 0.75f + 15f)) continue;
                            string id = $"s{k:00}";
                            cfg.vessels.Add(new VesselPlacement { object_id = id, asset_id = e.asset_id, x = xz.x, z = xz.y, heading_deg = hd, immersion_m = e.default_immersion_m });
                            plan.vessels.Add(new EpisodeVesselPlan { object_id = id, asset_id = e.asset_id, role = "static", entry = xz, exit = xz, region = reg, length_m = e.geometry.bounds_max.x - e.geometry.bounds_min.x });
                            used.Add(e.asset_id);
                            break;
                        }
                    }
                    // frame times spanning the primary mover's pass through the view
                    var times = Enumerable.Range(0, spec.frames).Select(f => tEnter0 + (f + 0.5f) / spec.frames * (tExit0 - tEnter0)).ToList();
                    string check = Simulate(w, cfg, spec, plan, times, proj, camXZ);
                    if (check != null) { plan.attempt_log.Add($"a{attempt}: rejected after simulation: {check}"); continue; }
                    for (int f = 0; f < plan.frames.Count; f++) plan.frames[f].scenario_id = $"{plan.episode_id}__f{f:00}";
                    plan.status = "planned";
                    plan.failure = "";
                    plan.attempt_log.Add($"a{attempt}: planned {nMove} moving + {plan.vessels.Count(v => v.role == "static")} static, frames {times.First():F1}..{times.Last():F1} s");
                    plan.scenario = cfg;
                    return plan;
                }
                plan.status = "planning_failed";
                plan.failure = plan.attempt_log.LastOrDefault() ?? "no attempt";
                return plan;
            }
            finally { cam.aspect = prevAspect; }
        }

        static float DistToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a; float t = ab.sqrMagnitude > 1e-6f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
            return (p - (a + t * ab)).magnitude;
        }

        static string Simulate(SimWorld w, ScenarioConfig cfg, EpisodeSpec spec, EpisodePlan plan, List<float> times, LabelComputer.Projection proj, Vector2 camXZ)
        {
            var sim = new NavigationSim(w.Harbor, w.HarborColliders, w.CctvCamera, w.Catalog, cfg);
            EpisodeFrame prev = null; float prevT = 0f;
            for (int f = 0; f < times.Count; f++)
            {
                sim.AdvanceTo(times[f]);
                var rec = sim.Record();
                var fr = new EpisodeFrame { frame_index = f, sim_time_s = sim.Time };
                var poses = new List<VesselPlacement>();
                foreach (var r in rec.vessels)
                {
                    var pv = plan.vessels.First(v => v.object_id == r.object_id);
                    var e = w.Catalog.Find(r.asset_id);
                    var placement = cfg.vessels.First(v => v.object_id == r.object_id);
                    var fv = new EpisodeFrameVessel
                    {
                        object_id = r.object_id, asset_id = r.asset_id, role = pv.role, nav_state = r.state, x = r.position_xz.x, z = r.position_xz.y,
                        heading_deg = r.heading_deg, speed_kn = r.speed_kn, camera_distance_m = Vector2.Distance(r.position_xz, camXZ),
                    };
                    if (ScenePlanner.PredictBox(w, proj, e, fv.x, fv.z, fv.heading_deg, placement.immersion_m, spec.width, spec.height, out var box) && box.height >= 3f)
                    { fv.in_view = true; fv.x_min = box.xMin; fv.y_min = box.yMin; fv.x_max = box.xMax; fv.y_max = box.yMax; }
                    fr.vessels.Add(fv);
                    poses.Add(new VesselPlacement { object_id = r.object_id, asset_id = r.asset_id, x = fv.x, z = fv.z, heading_deg = fv.heading_deg, immersion_m = placement.immersion_m });
                }
                fr.movers_in_view_underway = fr.vessels.Count(v => v.role == "moving" && v.in_view && v.speed_kn > 0.3f && v.nav_state == "underway");
                if (fr.movers_in_view_underway == 0) return $"frame {f} t {sim.Time:F1}: no mover underway in view";
                var lay = PlacementQuality.Evaluate(w, poses, spec.width, spec.height);
                fr.layout_issues = lay.issues.Select(i => $"{i.rule}:{i.a}/{i.b}:{i.value:F2}").ToList();
                if (lay.issues.Any(i => i.rule.StartsWith("same_asset"))) return $"frame {f}: {string.Join(";", fr.layout_issues)}";
                for (int i = 0; i < fr.vessels.Count; i++)
                    for (int j = i + 1; j < fr.vessels.Count; j++)
                    {
                        var a = fr.vessels[i]; var b = fr.vessels[j];
                        float need = 0.25f * (plan.vessels.First(v => v.object_id == a.object_id).length_m + plan.vessels.First(v => v.object_id == b.object_id).length_m);
                        if (new Vector2(a.x - b.x, a.z - b.z).magnitude < need) return $"frame {f}: {a.object_id} and {b.object_id} closer than {need:F0} m";
                    }
                if (prev != null)
                {
                    float dt = sim.Time - prevT;
                    foreach (var v in fr.vessels.Where(v => v.role == "moving"))
                    {
                        var pv = prev.vessels.First(q => q.object_id == v.object_id);
                        float d = new Vector2(v.x - pv.x, v.z - pv.z).magnitude;
                        float vmax = plan.vessels.First(q => q.object_id == v.object_id).speed_kn * Knot * 1.3f;
                        if (d > vmax * dt + 2f) return $"frame {f}: {v.object_id} jumped {d:F1} m in {dt:F1} s";
                        if (v.in_view && pv.in_view)
                        {
                            float px = Mathf.Sqrt(Mathf.Pow((v.x_min + v.x_max - pv.x_min - pv.x_max) * 0.5f, 2) + Mathf.Pow((v.y_min + v.y_max - pv.y_min - pv.y_max) * 0.5f, 2));
                            if (px < spec.min_frame_motion_px) return $"frame {f}: {v.object_id} moved only {px:F1} px";
                        }
                    }
                }
                plan.frames.Add(fr); prev = fr; prevT = sim.Time;
            }
            return null;
        }
    }
}
