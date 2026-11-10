using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace PortCCTV.Research
{
    // =====================================================================================================================
    // research.json: requested vs achieved conditions of one capture, measured on the RENDERED frame (GPU instance mask).
    // Geometric occlusion = 1 - visible_px / reference_px, where reference_px comes from an extra ID pass of the same vessel
    // with every other vessel and all harbour geometry hidden (rendered water kept, so the waterline is not "occlusion").
    // Image-border truncation is reported separately from occlusion. Identifiability (fog / night / contrast) is a separate,
    // uncalibrated proxy and is never folded into the geometric numbers.
    // =====================================================================================================================

    [Serializable]
    public class ObjectOutcome
    {
        public string object_id, role, asset_id, detectability;
        public List<string> detectability_reasons = new List<string>();
        // requested (copied from the job; -1 / "" when the object was not requested, e.g. a background vessel)
        public float req_height_frac = -1f, req_center_u = -1f, req_bottom_v = -1f;
        public string req_occlusion = "", req_truncation = "";
        // achieved (rendered)
        public int visible_px;
        public float ach_height_frac = -1f, ach_center_u = -1f, ach_bottom_v = -1f, ach_height_px_at_1080 = -1f;
        public int reference_px = -1;
        public float occlusion_fraction = -1f;
        public string occlusion_definition = "";
        public float truncation_fraction_above_water_box = -1f;
        public float truncation_fraction_full_mesh_box = -1f;
        public string truncation_definition = "1 - clipped area / unclipped area of the projected box (box area, NOT visible area)";
        public string size_met = "", occlusion_met = "", truncation_met = "";
        public string size_bin = "", occlusion_bin = "", truncation_bin = "";
        public float identifiability_luma_contrast = -1f, identifiability_fog_transmittance = -1f;
        public string identifiability_status = "uncalibrated_proxy_not_human_identifiability";
    }

    [Serializable]
    public class AchievedEnvironment
    {
        public string lighting, water, sky, precipitation, sensor_preset;
        public bool night, fog_enabled, sensor_enabled, sensor_distortion;
        public float sun_elevation_deg, fog_mean_free_path_m, visibility_scale, exposure_ev100;
        public string note = "engine values, not calibrated meteorology / sea state / camera exposure";
    }

    [Serializable]
    public class ResearchCaptureRecord
    {
        public string record_schema = "portcctv.research.capture.v1";
        public string job_id, experiment_id, contract_sha256, job_scenario_sha256, capture_scenario_sha256, freeze_id;
        public string build_content_sha256, build_origin;
        public string label_status = "provisional_diagnostic";
        public bool training_ready = false;
        public string human_review = "not_performed";
        public ResearchJobInfo job;
        public AchievedEnvironment environment = new AchievedEnvironment();
        public List<ObjectOutcome> objects = new List<ObjectOutcome>();
        public int targets_requested, targets_met_all;
        public bool positive;
        public bool has_ignore_objects;
        /// <summary>empty_scene_candidate | hard_negative_candidate | hard_negative_candidate_pending_policy | not_negative</summary>
        public string negative_status = "not_negative";
        public List<string> non_vessels_in_view = new List<string>();
        public double reference_ms;
        public int mask_instance_limit = 254;
    }

    public static class ResearchMeasure
    {
        public static readonly float[] SizeBinsPx1080 = { 0, 8, 16, 32, 64, 128, 99999 };
        public static readonly string[] SizeBinNames = { "<8px", "8-16px", "16-32px", "32-64px", "64-128px", ">=128px" };
        public static readonly float[] OcclusionEdges = { 0f, 0.1f, 0.5f, 0.9f, 1.0001f };
        public static readonly string[] OcclusionBinNames = { "occ<0.1", "occ0.1-0.5", "occ0.5-0.9", "occ>=0.9" };

        public static string SizeBin(float px1080)
        {
            for (int i = 0; i < SizeBinNames.Length; i++) if (px1080 >= SizeBinsPx1080[i] && px1080 < SizeBinsPx1080[i + 1]) return SizeBinNames[i];
            return SizeBinNames[SizeBinNames.Length - 1];
        }

        public static string OcclusionBin(float occ)
        {
            if (occ < 0f) return "unknown";
            for (int i = 0; i < OcclusionBinNames.Length; i++) if (occ >= OcclusionEdges[i] && occ < OcclusionEdges[i + 1]) return OcclusionBinNames[i];
            return OcclusionBinNames[OcclusionBinNames.Length - 1];
        }

        static float Trunc(ProjectedBoxLabel b)
        {
            if (b == null || !b.unclipped.valid) return -1f;
            float ua = Mathf.Max(1e-6f, b.unclipped.Width * b.unclipped.Height);
            float ca = b.clipped.valid ? b.clipped.Width * b.clipped.Height : 0f;
            return Mathf.Clamp01(1f - ca / ua);
        }

        /// <summary>Reference pixel count per vessel: same frozen frame and camera, only this vessel and the water rendered.</summary>
        public static Dictionary<int, int> ReferencePixels(SimWorld world, Camera cam, int W, int H, LensModel lens)
        {
            var res = new Dictionary<int, int>();
            if (world.GpuMask == null || !world.GpuMask.Available) return res;
            var harbor = world.HarborRoot;
            var mooring = harbor != null && harbor.transform.parent != null ? harbor.transform.parent.Find("MooringLines") : null;
            bool harborWas = harbor != null && harbor.activeSelf, mooringWas = mooring != null && mooring.gameObject.activeSelf;
            var states = world.Vessels.Select(v => v.root != null && v.root.activeSelf).ToList();
            try
            {
                if (harbor != null) harbor.SetActive(false);
                if (mooring != null) mooring.gameObject.SetActive(false);
                for (int k = 0; k < world.Vessels.Count; k++)
                {
                    var v = world.Vessels[k];
                    for (int j = 0; j < world.Vessels.Count; j++)
                        if (world.Vessels[j].root != null) world.Vessels[j].root.SetActive(j == k);
                    var m = world.GpuMask.Render(cam, new[] { v }, W, H);
                    if (m == null) continue;
                    if (lens != null) m = lens.WarpMask(m, W, H);
                    int n = 0; byte id = (byte)v.instance_index;
                    for (int i = 0; i < m.Length; i++) if (m[i] == id) n++;
                    res[v.instance_index] = n;
                }
            }
            finally
            {
                for (int j = 0; j < world.Vessels.Count; j++) if (world.Vessels[j].root != null) world.Vessels[j].root.SetActive(states[j]);
                if (mooring != null) mooring.gameObject.SetActive(mooringWas);
                if (harbor != null) harbor.SetActive(harborWas);
            }
            return res;
        }

        public static ResearchCaptureRecord Build(SimWorld world, ScenarioConfig cfg, LabelSet labels, ResearchJobInfo job, string jobId,
            Dictionary<int, int> referencePx, bool referenceComputed, string captureScenarioSha)
        {
            int W = labels.image_width, H = labels.image_height;
            var build = BuildManifestInfo.Record();
            var rec = new ResearchCaptureRecord
            {
                job_id = jobId, experiment_id = job.experiment_id, contract_sha256 = job.contract_sha256, job_scenario_sha256 = job.job_scenario_sha256,
                capture_scenario_sha256 = captureScenarioSha, freeze_id = job.freeze_id, build_content_sha256 = build.content_sha256, build_origin = build.origin,
                job = job,
            };
            var env = world.Environment;
            rec.environment = new AchievedEnvironment
            {
                lighting = env.preset_id, water = env.water_preset_id, sky = env.sky, precipitation = cfg.environment.weather.precipitation,
                night = env.sun_elevation_deg < 0f || (EnvironmentPresetLibrary.Exists(env.preset_id) && EnvironmentPresetLibrary.Get(env.preset_id).night),
                fog_enabled = env.fog_enabled, fog_mean_free_path_m = env.fog_mean_free_path_m, visibility_scale = cfg.environment.weather.visibility_scale,
                sun_elevation_deg = env.sun_elevation_deg, exposure_ev100 = env.exposure_ev100,
                sensor_enabled = cfg.sensor.enabled, sensor_preset = cfg.sensor.preset,
                sensor_distortion = cfg.sensor.enabled && (Mathf.Abs(cfg.sensor.distortion_k1) > 1e-6f || Mathf.Abs(cfg.sensor.distortion_k2) > 1e-6f),
            };
            float tol = Mathf.Log(1f + Mathf.Max(0.01f, job.size_tolerance_rel));
            foreach (var o in labels.objects)
            {
                var req = job.objects.FirstOrDefault(r => r.object_id == o.object_id);
                var vm = o.visible_mask;
                var oc = new ObjectOutcome
                {
                    object_id = o.object_id, role = req?.role ?? "background", asset_id = o.asset_id, detectability = o.detectability,
                    detectability_reasons = new List<string>(o.detectability_reasons),
                    visible_px = vm != null && vm.computed ? vm.pixel_count : 0,
                    truncation_fraction_above_water_box = Trunc(o.above_water_box), truncation_fraction_full_mesh_box = Trunc(o.full_mesh_box),
                    identifiability_luma_contrast = o.visible_luma_contrast, identifiability_fog_transmittance = o.fog_transmittance,
                };
                if (vm != null && vm.computed && vm.bbox.valid)
                {
                    oc.ach_height_frac = vm.bbox.Height / H; oc.ach_center_u = (vm.bbox.x_min + vm.bbox.x_max) / 2f / W; oc.ach_bottom_v = vm.bbox.y_max / H;
                    oc.ach_height_px_at_1080 = vm.bbox.Height * 1080f / H;
                }
                oc.size_bin = oc.ach_height_px_at_1080 >= 0f ? SizeBin(oc.ach_height_px_at_1080) : "not_visible";
                if (referenceComputed && referencePx.TryGetValue(o.instance_index, out var refPx))
                {
                    oc.reference_px = refPx;
                    oc.occlusion_fraction = refPx > 0 ? Mathf.Clamp01(1f - (float)oc.visible_px / refPx) : -1f;
                    oc.occlusion_definition = "1 - visible_px / reference_px; reference = GPU ID pass of this vessel alone (other vessels and all harbour geometry hidden, rendered water kept), same frame / camera / lens warp";
                }
                else oc.occlusion_definition = "unknown: no reference pass";
                oc.occlusion_bin = OcclusionBin(oc.occlusion_fraction);
                float tr = oc.truncation_fraction_above_water_box;
                oc.truncation_bin = tr < 0f ? "unknown" : tr <= 0.02f ? "none" : tr < 0.5f ? "partial" : "heavy";
                if (req != null)
                {
                    oc.req_height_frac = req.target_height_frac; oc.req_center_u = req.target_center_u; oc.req_bottom_v = req.target_bottom_v;
                    oc.req_occlusion = req.occlusion_target; oc.req_truncation = req.truncation_target;
                    if (req.role == "target")
                    {
                        oc.size_met = oc.ach_height_frac > 0f && Mathf.Abs(Mathf.Log(oc.ach_height_frac / req.target_height_frac)) <= tol ? "met" : "missed";
                        oc.occlusion_met = oc.occlusion_fraction < 0f ? "unknown"
                            : req.occlusion_target == "partial" ? (oc.occlusion_fraction >= 0.1f && oc.occlusion_fraction <= 0.9f ? "met" : "missed")
                            : (oc.occlusion_fraction < 0.1f ? "met" : "missed");
                        oc.truncation_met = tr < 0f ? "unknown" : req.truncation_target != "none" ? (tr >= 0.05f ? "met" : "missed") : (tr <= 0.02f ? "met" : "missed");
                    }
                }
                rec.objects.Add(oc);
            }
            var targets = rec.objects.Where(x => x.role == "target").ToList();
            rec.targets_requested = targets.Count;
            rec.targets_met_all = targets.Count(x => x.size_met == "met" && x.occlusion_met != "missed" && x.truncation_met != "missed");
            rec.positive = labels.objects.Any(x => x.detectability == "label");
            rec.has_ignore_objects = labels.objects.Any(x => x.detectability == "ignore");
            foreach (var nv in labels.non_vessel_objects.Where(n => n.full_box_clipped != null && n.full_box_clipped.valid)) rec.non_vessels_in_view.Add(nv.kind + ":" + nv.id);
            bool anyVesselPixels = labels.objects.Any(x => x.visible_mask != null && x.visible_mask.pixel_count > 0);
            if (anyVesselPixels) rec.negative_status = "not_negative";
            else if (rec.non_vessels_in_view.Any(ClassMaps.NonVesselNeedsPolicy)) rec.negative_status = "hard_negative_candidate_pending_policy";
            else if (rec.non_vessels_in_view.Count > 0) rec.negative_status = "hard_negative_candidate";
            else rec.negative_status = "empty_scene_candidate";
            return rec;
        }
    }
}
