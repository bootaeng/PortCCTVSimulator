using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace PortCCTV.Research
{
    // =====================================================================================================================
    // Integrity checks, coverage report, provisional export (YOLO / COCO + manifest), human QA queue and the training-release
    // gate. Everything here reads finished capture folders; nothing modifies a capture. Generation statistics are reported as
    // such — never as detection performance.
    // =====================================================================================================================

    public class CaptureView
    {
        public string job_id, dir;
        public BatchJob job;
        public ResearchCaptureRecord rec;
        public LabelSet labels;
        public CaptureMetadata meta;
    }

    [Serializable]
    public class IntegrityResult
    {
        public string job_id;
        public bool ok;
        public List<string> errors = new List<string>();
        public List<string> warnings = new List<string>();
        public int objects, label_objects, ignore_objects;
        public float max_mask_bbox_error_px = -1f;
        public float max_letterbox_error_px = -1f;
        /// <summary>Largest edge shrink of a nearest-neighbour resized mask vs the transformed box (thin masts / rails vanish); a
        /// property of nearest resizing, reported, not an alignment error.</summary>
        public float max_nearest_resize_shrink_px = -1f;
        public float max_yolo_error = -1f;
        public int letterbox_size;
        public string letterbox_definition = "scale s = min(S/W, S/H), resized size round(W s) x round(H s), pad floor((S - size)/2); boxes x' = x s + pad; alignment error = max |edge(transformed box) - edge(bbox of the coverage-resized mask)| in output pixels (an output pixel belongs to an instance if any source pixel inside its footprint does), must be <= 1 px";
    }

    [Serializable] public class NamedCount { public string name; public int count; }

    [Serializable]
    public class CompareHist
    {
        public string name;
        public string note = "";
        public List<string> bins = new List<string>();
        public List<int> requested = new List<int>();
        public List<int> achieved = new List<int>();
    }

    [Serializable]
    public class ModeCoverage
    {
        public string mode;
        public int jobs, planned, planning_failed, rendered, render_failed, hash_mismatch, not_rendered;
        public int targets_requested, targets_unmet_in_planning, targets_rendered, size_met, size_missed, occlusion_met, occlusion_missed, occlusion_unknown, truncation_met, truncation_missed;
        public int occluders, background_vessels;
        public int position_adjusted_targets;
        public float mean_abs_position_dv;
        public int positive_frames, negative_frames, frames_with_ignore;
        public int detect_label, detect_ignore, detect_absent;
        public double planning_ms, render_ms_total, capture_ms_total, reference_ms_total;
        public double disk_mb;
        public int planning_attempts;
        public List<CompareHist> histograms = new List<CompareHist>();
        public List<NamedCount> lighting = new List<NamedCount>(), water = new List<NamedCount>(), fog = new List<NamedCount>(),
            sensor = new List<NamedCount>(), precipitation = new List<NamedCount>(), assets = new List<NamedCount>(), cameras = new List<NamedCount>(),
            negative_status = new List<NamedCount>(), planning_failure_reasons = new List<NamedCount>();
    }

    [Serializable]
    public class CoverageReport
    {
        public string report_schema = "portcctv.research.coverage.v1";
        public string statement = "Generation coverage statistics (requested vs rendered conditions). NOT detection performance; no detector was trained or evaluated.";
        public string experiment_id, contract_sha256, jobs_file, captures_dir, created_utc, profile_id, profile_kind;
        public List<ModeCoverage> modes = new List<ModeCoverage>();
        public List<NamedCount> distances = new List<NamedCount>();
        public List<string> distance_definitions = new List<string>();
        public List<string> notes = new List<string>();
    }

    [Serializable]
    public class ExportFrame
    {
        public string job_id, mode, scene_kind, split, episode_id, pair_group_id, changed_factor, changed_value;
        public int frame_index = -1;
        public string rgb_sha256, policy_version, status;      // included | quarantined | excluded
        public List<string> reasons = new List<string>();
        public int labels, ignores;
        public string negative_status, source_dir, image_path, label_path;
        public string clean_image = "";
    }

    [Serializable]
    public class ExportManifest
    {
        public string export_schema = "portcctv.research.export.v1";
        public string release_status = "provisional_not_for_training";
        public string label_status = "provisional_diagnostic";
        public bool training_ready = false;
        public string human_review = "not_performed";
        public string experiment_id, contract_sha256, jobs_file_sha256, created_utc;
        public string class_map_version, class_map, primary_bbox_policy, policy_version, trainer_adapter;
        public bool adapter_supports_ignore;
        public string ignore_handling = "frames with ignore objects are quarantined from the YOLO release (the adapter cannot read ignore regions); COCO keeps them with ignore_regions (NOT iscrowd)";
        public int frames_included, frames_quarantined, frames_excluded;
        public List<ExportFrame> frames = new List<ExportFrame>();
        public List<IntegrityResult> integrity = new List<IntegrityResult>();
    }

    [Serializable]
    public class ReviewRecord
    {
        public string frame_rgb_sha256, policy_version, reviewer, decision, reviewed_utc, note;
        /// <summary>human | test_fixture. The simulator never writes these records.</summary>
        public string record_source;
    }

    [Serializable]
    public class ReleaseResult
    {
        public string result_schema = "portcctv.research.release.v1";
        public bool released;
        public string reason;
        public int frames_considered, approved, missing, rejected, policy_mismatch;
        public List<string> missing_records = new List<string>();
        public bool test_fixture;
    }

    public static class ResearchReports
    {
        public static string PolicyVersion(LabelSpec l) => $"{l.class_map_version}+{l.primary_bbox_policy}/{l.primary_bbox_policy_version}+{l.visibility_policy_version}";

        // ------------------------------------------------------------------------------------------------ loading
        public static List<CaptureView> Load(BatchFile batch, string capturesDir, out List<string> problems)
        {
            problems = new List<string>();
            var list = new List<CaptureView>();
            foreach (var j in batch.jobs)
            {
                var d = Path.Combine(capturesDir, j.job_id);
                if (!Directory.Exists(d)) continue;
                if (!CaptureService.VerifyExistingOutput(d, null, out var why)) { problems.Add(j.job_id + ": " + why); continue; }
                var v = new CaptureView { job_id = j.job_id, dir = d, job = j };
                v.meta = JsonUtility.FromJson<CaptureMetadata>(File.ReadAllText(Path.Combine(d, "metadata.json"), Encoding.UTF8));
                v.labels = JsonUtility.FromJson<LabelSet>(File.ReadAllText(Path.Combine(d, "labels.json"), Encoding.UTF8));
                var rp = Path.Combine(d, "research.json");
                if (File.Exists(rp)) v.rec = JsonUtility.FromJson<ResearchCaptureRecord>(File.ReadAllText(rp, Encoding.UTF8));
                list.Add(v);
            }
            return list;
        }

        /// <summary>Reads mask_instances.png (R channel ids) into a top-left-origin byte array.</summary>
        public static byte[] LoadMask(string path, out int W, out int H)
        {
            var t = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            try
            {
                if (!t.LoadImage(File.ReadAllBytes(path), false)) throw new InvalidDataException("cannot decode " + path);
                W = t.width; H = t.height;
                var px = t.GetPixels32();
                var m = new byte[W * H];
                for (int y = 0; y < H; y++) { int src = (H - 1 - y) * W, dst = y * W; for (int x = 0; x < W; x++) m[dst + x] = px[src + x].r; }
                return m;
            }
            finally { Util.SafeDestroy(t); }
        }

        public static PixelBox MaskBox(byte[] m, int W, int H, int id, out int count)
        {
            int x0 = int.MaxValue, y0 = int.MaxValue, x1 = -1, y1 = -1; count = 0;
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                    if (m[y * W + x] == id) { count++; if (x < x0) x0 = x; if (x > x1) x1 = x; if (y < y0) y0 = y; if (y > y1) y1 = y; }
            return count == 0 ? PixelBox.Empty : new PixelBox { valid = true, x_min = x0, y_min = y0, x_max = x1 + 1, y_max = y1 + 1 };
        }

        public struct Letterbox { public float s; public int newW, newH, padX, padY, S; }

        public static Letterbox MakeLetterbox(int W, int H, int S)
        {
            float s = Mathf.Min((float)S / W, (float)S / H);
            int nw = Mathf.RoundToInt(W * s), nh = Mathf.RoundToInt(H * s);
            return new Letterbox { s = s, newW = nw, newH = nh, padX = (S - nw) / 2, padY = (S - nh) / 2, S = S };
        }

        public static PixelBox TransformBox(PixelBox b, Letterbox lb) => !b.valid ? PixelBox.Empty : new PixelBox
        {
            valid = true, x_min = b.x_min * lb.s + lb.padX, x_max = b.x_max * lb.s + lb.padX, y_min = b.y_min * lb.s + lb.padY, y_max = b.y_max * lb.s + lb.padY,
        };

        /// <summary>Coverage letterbox: an output pixel takes an instance id if any source pixel within its footprint has it (first
        /// id found wins). Keeps 1-px structures, so its bbox matches the transformed box to within one output pixel.</summary>
        public static byte[] LetterboxMaskCoverage(byte[] m, int W, int H, Letterbox lb, int id)
        {
            var o = new byte[lb.S * lb.S];
            for (int y = 0; y < lb.newH; y++)
            {
                int y0 = Mathf.Clamp(Mathf.FloorToInt(y / lb.s), 0, H - 1), y1 = Mathf.Clamp(Mathf.CeilToInt((y + 1) / lb.s), y0 + 1, H);
                for (int x = 0; x < lb.newW; x++)
                {
                    int x0 = Mathf.Clamp(Mathf.FloorToInt(x / lb.s), 0, W - 1), x1 = Mathf.Clamp(Mathf.CeilToInt((x + 1) / lb.s), x0 + 1, W);
                    bool hit = false;
                    for (int yy = y0; yy < y1 && !hit; yy++) for (int xx = x0; xx < x1; xx++) if (m[yy * W + xx] == id) { hit = true; break; }
                    if (hit) o[(y + lb.padY) * lb.S + x + lb.padX] = (byte)id;
                }
            }
            return o;
        }

        /// <summary>Nearest-neighbour letterbox of an instance mask (pixel centre sampling).</summary>
        public static byte[] LetterboxMask(byte[] m, int W, int H, Letterbox lb)
        {
            var o = new byte[lb.S * lb.S];
            for (int y = 0; y < lb.newH; y++)
            {
                int sy = Mathf.Clamp((int)((y + 0.5f) / lb.s), 0, H - 1);
                for (int x = 0; x < lb.newW; x++)
                {
                    int sx = Mathf.Clamp((int)((x + 0.5f) / lb.s), 0, W - 1);
                    o[(y + lb.padY) * lb.S + x + lb.padX] = m[sy * W + sx];
                }
            }
            return o;
        }

        // ------------------------------------------------------------------------------------------------ integrity
        public static IntegrityResult CheckIntegrity(CaptureView v, int letterboxSize)
        {
            var r = new IntegrityResult { job_id = v.job_id, letterbox_size = letterboxSize };
            var L = v.labels;
            r.objects = L.objects.Count;
            r.label_objects = L.objects.Count(o => o.detectability == "label");
            r.ignore_objects = L.objects.Count(o => o.detectability == "ignore");
            if (L.training_ready || L.label_status != "provisional_diagnostic" || L.human_review != "not_performed")
                r.errors.Add("label status fields were changed (must stay provisional / training_ready=false / not_performed)");
            if (L.objects.Select(o => o.object_id).Distinct().Count() != L.objects.Count) r.errors.Add("duplicate object_id");
            if (L.objects.Select(o => o.instance_index).Distinct().Count() != L.objects.Count) r.errors.Add("duplicate instance_index");
            if (L.objects.Any(o => o.instance_index < 1 || o.instance_index > CaptureService.MaxMaskInstances)) r.errors.Add("instance index outside 1..254");
            var maskPath = Path.Combine(v.dir, "mask_instances.png");
            if (!File.Exists(maskPath)) { r.errors.Add("mask_instances.png missing"); r.ok = false; return r; }
            var m = LoadMask(maskPath, out int W, out int H);
            if (W != L.image_width || H != L.image_height) r.errors.Add($"mask {W}x{H} != labels {L.image_width}x{L.image_height}");
            var known = new HashSet<int>(L.objects.Select(o => o.instance_index));
            var present = new HashSet<int>(m.Where(b => b != 0).Select(b => (int)b));
            foreach (var id in present) if (!known.Contains(id)) r.errors.Add("mask contains unknown instance id " + id);
            float maxErr = 0f;
            foreach (var o in L.objects)
            {
                var mb = MaskBox(m, W, H, o.instance_index, out int cnt);
                var lbx = o.visible_mask?.bbox ?? PixelBox.Empty;
                if (o.visible_mask != null && o.visible_mask.computed && cnt != o.visible_mask.pixel_count)
                    r.errors.Add($"{o.object_id}: mask has {cnt} px, labels say {o.visible_mask.pixel_count}");
                if (mb.valid != lbx.valid) r.errors.Add($"{o.object_id}: bbox validity differs between mask and labels");
                else if (mb.valid)
                    maxErr = Mathf.Max(maxErr, Mathf.Max(Mathf.Max(Mathf.Abs(mb.x_min - lbx.x_min), Mathf.Abs(mb.x_max - lbx.x_max)),
                        Mathf.Max(Mathf.Abs(mb.y_min - lbx.y_min), Mathf.Abs(mb.y_max - lbx.y_max))));
                if (o.detectability == "label" && (!lbx.valid || lbx.Width <= 0f || lbx.Height <= 0f)) r.errors.Add($"{o.object_id}: label object with a zero-size box");
                if (o.detectability == "label" && cnt == 0) r.errors.Add($"{o.object_id}: label object without mask pixels");
            }
            r.max_mask_bbox_error_px = maxErr;
            if (maxErr > 1e-3f) r.errors.Add($"visible bbox differs from the mask by {maxErr:F3} px");
            // letterbox (resize + pad) consistency
            if (letterboxSize > 0)
            {
                var lb = MakeLetterbox(W, H, letterboxSize);
                var lm = LetterboxMask(m, W, H, lb);
                float le = 0f, shrink = 0f;
                foreach (var o in L.objects.Where(o => o.visible_mask != null && o.visible_mask.bbox.valid))
                {
                    var tb = TransformBox(o.visible_mask.bbox, lb);
                    var cb = MaskBox(LetterboxMaskCoverage(m, W, H, lb, o.instance_index), lb.S, lb.S, o.instance_index, out _);
                    if (cb.valid)
                        le = Mathf.Max(le, Mathf.Max(Mathf.Max(Mathf.Abs(tb.x_min - cb.x_min), Mathf.Abs(tb.x_max - cb.x_max)), Mathf.Max(Mathf.Abs(tb.y_min - cb.y_min), Mathf.Abs(tb.y_max - cb.y_max))));
                    else r.errors.Add($"{o.object_id}: no pixels after coverage letterbox");
                    var rb = MaskBox(lm, lb.S, lb.S, o.instance_index, out _);
                    if (!rb.valid) { if (o.visible_mask.pixel_count >= 9) r.warnings.Add($"{o.object_id}: vanishes in a nearest-neighbour resize to {letterboxSize} ({o.visible_mask.pixel_count} px)"); continue; }
                    shrink = Mathf.Max(shrink, Mathf.Max(Mathf.Max(rb.x_min - tb.x_min, tb.x_max - rb.x_max), Mathf.Max(rb.y_min - tb.y_min, tb.y_max - rb.y_max)));
                }
                r.max_letterbox_error_px = le;
                r.max_nearest_resize_shrink_px = shrink;
                if (le > 1.0f + 1e-3f) r.errors.Add($"letterbox alignment error {le:F3} px > 1 px (transformed box vs coverage-resized mask)");
                if (shrink > 1.0f + 1e-3f) r.warnings.Add($"nearest-neighbour resize to {letterboxSize} drops thin structures: box edge shrinks up to {shrink:F2} px (resize masks with coverage, not nearest)");
            }
            // provisional YOLO file = label objects' visible boxes, normalised
            var yp = Path.Combine(v.dir, "yolo_visible_bbox_provisional.txt");
            if (File.Exists(yp))
            {
                var lines = File.ReadAllLines(yp).Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
                var lab = L.objects.Where(o => o.detectability == "label" && o.visible_mask.computed && o.visible_mask.bbox.valid).ToList();
                if (lines.Count != lab.Count) r.errors.Add($"YOLO file has {lines.Count} lines, {lab.Count} label objects");
                float ye = 0f;
                for (int k = 0; k < Mathf.Min(lines.Count, lab.Count); k++)
                {
                    var p = lines[k].Split(' ').Skip(1).Select(t => float.Parse(t, CultureInfo.InvariantCulture)).ToArray();
                    var b = lab[k].visible_mask.bbox;
                    ye = Mathf.Max(ye, Mathf.Max(Mathf.Max(Mathf.Abs(p[0] - (b.x_min + b.x_max) / 2 / W), Mathf.Abs(p[1] - (b.y_min + b.y_max) / 2 / H)),
                        Mathf.Max(Mathf.Abs(p[2] - b.Width / W), Mathf.Abs(p[3] - b.Height / H))));
                    if (p.Any(q => q < 0f || q > 1f)) r.errors.Add("YOLO value outside [0,1]");
                }
                r.max_yolo_error = ye;
                if (ye > 1e-5f) r.errors.Add($"YOLO line differs from the visible box by {ye:E2}");
            }
            if (v.meta.sensor != null && v.meta.sensor.enabled && v.meta.sensor.config != null &&
                (Mathf.Abs(v.meta.sensor.config.distortion_k1) > 1e-6f || Mathf.Abs(v.meta.sensor.config.distortion_k2) > 1e-6f))
                r.warnings.Add("lens distortion: labels / mask are in the distorted rgb.png; rgb_clean.png is the undistorted render and has no exported labels");
            r.ok = r.errors.Count == 0;
            return r;
        }

        // ------------------------------------------------------------------------------------------------ coverage
        static void Count(List<NamedCount> l, string name) { var e = l.FirstOrDefault(x => x.name == name); if (e == null) l.Add(e = new NamedCount { name = name }); e.count++; }
        static CompareHist NewHist(string name, IEnumerable<string> bins, string note = "") => new CompareHist { name = name, note = note, bins = bins.ToList(), requested = bins.Select(_ => 0).ToList(), achieved = bins.Select(_ => 0).ToList() };
        static void Inc(CompareHist h, List<int> arr, string bin) { int k = h.bins.IndexOf(bin); if (k >= 0) arr[k]++; }
        static string Bin5(float v) => v < 0f ? "" : v < 0.2f ? "0.0-0.2" : v < 0.4f ? "0.2-0.4" : v < 0.6f ? "0.4-0.6" : v < 0.8f ? "0.6-0.8" : "0.8-1.0";
        static readonly string[] Bins5 = { "0.0-0.2", "0.2-0.4", "0.4-0.6", "0.6-0.8", "0.8-1.0" };

        public static CoverageReport Coverage(BatchFile batch, string jobsFile, string capturesDir, ScreenProfile profile)
        {
            var views = Load(batch, capturesDir, out var problems);
            var byJob = views.ToDictionary(v => v.job_id);
            var logs = new Dictionary<string, BatchLogEntry>();
            var lp = Path.Combine(capturesDir, "batch_log.jsonl");
            if (File.Exists(lp))
                foreach (var line in File.ReadAllLines(lp, Encoding.UTF8))
                    try { var e = JsonUtility.FromJson<BatchLogEntry>(line); if (e != null && !string.IsNullOrEmpty(e.job_id)) logs[e.job_id] = e; } catch { }
            var first = batch.jobs.FirstOrDefault(j => j.research.IsResearch)?.research;
            var rep = new CoverageReport
            {
                experiment_id = first?.experiment_id, contract_sha256 = first?.contract_sha256, jobs_file = jobsFile, captures_dir = capturesDir,
                created_utc = DateTime.UtcNow.ToString("o"), profile_id = profile?.profile_id ?? "", profile_kind = profile?.profile_kind ?? "",
            };
            if (profile != null && profile.profile_kind == "fixture_not_real_data") rep.notes.Add("C was sampled from a FIXTURE profile (not real statistics)");
            rep.notes.AddRange(problems.Select(p => "unfinished output: " + p));
            var achievedFrac = new Dictionary<string, List<float>>();
            foreach (var mg in batch.jobs.GroupBy(j => j.research.mode))
            {
                var mc = new ModeCoverage { mode = mg.Key };
                var hSize = NewHist("visible_height_px_at_1080", ResearchMeasure.SizeBinNames, "requested = target height x 1080; achieved = rendered visible-mask bbox height");
                var hU = NewHist("center_u", Bins5); var hV = NewHist("bottom_v", Bins5);
                var hOcc = NewHist("occlusion", new[] { "none", "partial" }.Concat(ResearchMeasure.OcclusionBinNames).Concat(new[] { "unknown" }), "requested none/partial; achieved = geometric occlusion bins (reference pass)");
                var hTr = NewHist("truncation", new[] { "none", "left", "right", "partial", "heavy", "unknown" }, "requested none/left/right; achieved = above-water box area outside the image");
                foreach (var j in mg)
                {
                    var r = j.research;
                    mc.jobs++;
                    if (r.plan_status == "planning_failed") { mc.planning_failed++; foreach (var fr in r.plan_failure_reasons) Count(mc.planning_failure_reasons, Regex(fr)); }
                    else mc.planned++;
                    mc.planning_ms += r.planning_ms; mc.planning_attempts += r.planning_attempts;
                    foreach (var o in r.objects.Where(o => o.role == "target"))
                    {
                        mc.targets_requested++;
                        if (o.plan_status != "planned") mc.targets_unmet_in_planning++;
                        else if (Mathf.Abs(o.position_adjusted_dv) > 1e-4f) { mc.position_adjusted_targets++; mc.mean_abs_position_dv += Mathf.Abs(o.position_adjusted_dv); }
                        Inc(hSize, hSize.requested, ResearchMeasure.SizeBin(o.target_height_frac * 1080f));
                        Inc(hU, hU.requested, Bin5(Mathf.Clamp01(o.target_center_u))); Inc(hV, hV.requested, Bin5(Mathf.Clamp01(o.target_bottom_v)));
                        Inc(hOcc, hOcc.requested, o.occlusion_target); Inc(hTr, hTr.requested, o.truncation_target);
                    }
                    // a capture folder counts for THIS job list only if it verifies against this job's contract / scenario / build
                    if (byJob.TryGetValue(j.job_id, out var vv) && j.research.IsResearch && !BatchRunner.VerifyResearchOutput(vv.dir, j, "", out _))
                    { mc.hash_mismatch++; continue; }
                    if (!byJob.TryGetValue(j.job_id, out var v))
                    {
                        if (r.plan_status != "planning_failed") { if (logs.TryGetValue(j.job_id, out var l2) && l2.status == "failed") mc.render_failed++; else mc.not_rendered++; }
                        continue;
                    }
                    mc.rendered++;
                    mc.render_ms_total += v.meta.render_ms; mc.capture_ms_total += v.meta.total_ms;
                    mc.disk_mb += Directory.GetFiles(v.dir).Sum(f => new FileInfo(f).Length) / 1048576.0;
                    var rec = v.rec;
                    if (rec == null) continue;
                    mc.reference_ms_total += rec.reference_ms;
                    if (rec.positive) mc.positive_frames++; else mc.negative_frames++;
                    if (rec.has_ignore_objects) mc.frames_with_ignore++;
                    Count(mc.negative_status, rec.negative_status);
                    Count(mc.lighting, rec.environment.lighting); Count(mc.water, rec.environment.water);
                    Count(mc.fog, rec.environment.visibility_scale.ToString("0.##", CultureInfo.InvariantCulture));
                    Count(mc.sensor, rec.environment.sensor_enabled ? rec.environment.sensor_preset : "off");
                    Count(mc.precipitation, rec.environment.precipitation);
                    Count(mc.cameras, r.harbor_id + "/" + r.camera_preset_id);
                    foreach (var o in rec.objects)
                    {
                        Count(mc.assets, o.asset_id);
                        if (o.detectability == "label") mc.detect_label++; else if (o.detectability == "ignore") mc.detect_ignore++; else mc.detect_absent++;
                        if (o.role == "occluder") { mc.occluders++; continue; }
                        if (o.role != "target") { mc.background_vessels++; continue; }
                        mc.targets_rendered++;
                        if (o.size_met == "met") mc.size_met++; else mc.size_missed++;
                        if (o.occlusion_met == "met") mc.occlusion_met++; else if (o.occlusion_met == "missed") mc.occlusion_missed++; else mc.occlusion_unknown++;
                        if (o.truncation_met == "met") mc.truncation_met++; else if (o.truncation_met == "missed") mc.truncation_missed++;
                        if (o.ach_height_px_at_1080 >= 0f) Inc(hSize, hSize.achieved, ResearchMeasure.SizeBin(o.ach_height_px_at_1080));
                        if (o.ach_height_frac > 0f) { if (!achievedFrac.ContainsKey(mg.Key)) achievedFrac[mg.Key] = new List<float>(); achievedFrac[mg.Key].Add(o.ach_height_frac); }
                        Inc(hU, hU.achieved, Bin5(o.ach_center_u)); Inc(hV, hV.achieved, Bin5(o.ach_bottom_v));
                        Inc(hOcc, hOcc.achieved, o.occlusion_bin);
                        Inc(hTr, hTr.achieved, o.truncation_bin);
                    }
                }
                if (mc.position_adjusted_targets > 0) mc.mean_abs_position_dv /= mc.position_adjusted_targets;
                hV.note = "requested bottom_v vs achieved; rows moved by the size-primary rule: see position_adjusted_targets / mean_abs_position_dv";
                mc.histograms.AddRange(new[] { hSize, hU, hV, hOcc, hTr });
                rep.modes.Add(mc);
            }
            // distances between achieved distributions (total variation distance, 0 = identical, 1 = disjoint)
            var g = rep.modes.FirstOrDefault(m => m.mode == Samplers.G); var c = rep.modes.FirstOrDefault(m => m.mode == Samplers.C);
            if (g != null && c != null)
                foreach (var name in new[] { "visible_height_px_at_1080", "center_u", "bottom_v" })
                    rep.distances.Add(new NamedCount { name = "TVDx1000(G_achieved, C_achieved) " + name, count = Mathf.RoundToInt(1000 * Tvd(g.histograms.First(h => h.name == name).achieved, c.histograms.First(h => h.name == name).achieved)) });
            foreach (var m in rep.modes)
                foreach (var name in new[] { "visible_height_px_at_1080", "center_u", "bottom_v" })
                {
                    var h = m.histograms.First(x => x.name == name);
                    rep.distances.Add(new NamedCount { name = $"TVDx1000({m.mode} requested, {m.mode} achieved) {name}", count = Mathf.RoundToInt(1000 * Tvd(h.requested, h.achieved)) });
                }
            if (profile != null)
                foreach (var kv in achievedFrac)
                {
                    var bins = profile.height_frac.weights.Select(_ => 0).ToList();
                    foreach (var f in kv.Value) bins[profile.height_frac.BinOf(Mathf.Clamp(f, ProfileBuilder.HeightEdges[0], ProfileBuilder.HeightEdges.Last() - 1e-6f))]++;
                    var pw = profile.height_frac.weights.Select(w => Mathf.RoundToInt(w * 1000f)).ToList();
                    rep.distances.Add(new NamedCount { name = $"TVDx1000(profile {profile.profile_kind} height_frac, {kv.Key} achieved) [{kv.Value.Count} targets]", count = Mathf.RoundToInt(1000 * Tvd(pw, bins)) });
                }
            rep.distance_definitions.Add("TVD = 0.5 * sum |p_i - q_i| over normalised bin counts, reported x1000 as an integer; small samples (pilot) make it very noisy");
            return rep;
        }

        static string Regex(string reason)
        {
            var r = reason.Contains(": ") ? reason.Substring(reason.IndexOf(": ", StringComparison.Ordinal) + 2) : reason;
            int k = r.IndexOfAny(new[] { ':', '(' });
            return (k > 0 ? r.Substring(0, k) : r).Trim();
        }

        public static float Tvd(List<int> a, List<int> b)
        {
            float sa = a.Sum(), sb = b.Sum();
            if (sa <= 0 || sb <= 0) return -1f;
            float t = 0f;
            for (int i = 0; i < a.Count; i++) t += Mathf.Abs(a[i] / sa - b[i] / sb);
            return 0.5f * t;
        }

        public static string CoverageMarkdown(CoverageReport r)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Generation coverage — " + r.experiment_id);
            sb.AppendLine();
            sb.AppendLine("> " + r.statement);
            sb.AppendLine();
            foreach (var n in r.notes) sb.AppendLine("- " + n);
            foreach (var m in r.modes)
            {
                sb.AppendLine();
                sb.AppendLine($"## mode {m.mode}");
                sb.AppendLine($"jobs {m.jobs}: planned {m.planned}, planning_failed {m.planning_failed}, rendered {m.rendered}, render_failed {m.render_failed}, hash_mismatch {m.hash_mismatch}, not rendered {m.not_rendered}");
                sb.AppendLine($"targets requested {m.targets_requested} (unmet in planning {m.targets_unmet_in_planning}), rendered {m.targets_rendered}: size met {m.size_met} / missed {m.size_missed}; occlusion met {m.occlusion_met} / missed {m.occlusion_missed} / unknown {m.occlusion_unknown}; truncation met {m.truncation_met} / missed {m.truncation_missed}; occluders {m.occluders}");
                sb.AppendLine($"position rule moved {m.position_adjusted_targets} target rows (mean |dv| {m.mean_abs_position_dv:F3} of the image height)");
                sb.AppendLine($"frames positive {m.positive_frames}, negative {m.negative_frames}, with ignore objects {m.frames_with_ignore}; objects label {m.detect_label} / ignore {m.detect_ignore} / absent {m.detect_absent}");
                sb.AppendLine($"time: planning {m.planning_ms / 1000:F1} s ({m.planning_attempts} attempts), render {m.render_ms_total / 1000:F1} s, capture total {m.capture_ms_total / 1000:F1} s (reference passes {m.reference_ms_total / 1000:F1} s); disk {m.disk_mb:F1} MB");
                foreach (var h in m.histograms)
                {
                    sb.AppendLine();
                    sb.AppendLine($"| {h.name} | requested | achieved |");
                    sb.AppendLine("|---|---:|---:|");
                    for (int i = 0; i < h.bins.Count; i++) sb.AppendLine($"| {h.bins[i]} | {h.requested[i]} | {h.achieved[i]} |");
                    if (!string.IsNullOrEmpty(h.note)) sb.AppendLine("\n" + h.note);
                }
                void L(string name, List<NamedCount> l) { if (l.Count > 0) sb.AppendLine($"\n{name}: " + string.Join(", ", l.OrderByDescending(x => x.count).Select(x => $"{x.name} {x.count}"))); }
                L("lighting", m.lighting); L("water", m.water); L("fog visibility_scale", m.fog); L("sensor", m.sensor); L("precipitation", m.precipitation);
                L("assets", m.assets); L("cameras", m.cameras); L("negative status", m.negative_status); L("planning failure reasons", m.planning_failure_reasons);
            }
            sb.AppendLine("\n## distribution distances");
            foreach (var d in r.distances) sb.AppendLine($"- {d.name}: {d.count / 1000f:F3}");
            foreach (var d in r.distance_definitions) sb.AppendLine("\n" + d);
            return sb.ToString();
        }

        // ------------------------------------------------------------------------------------------------ export
        public static ExportManifest ExportProvisional(BatchFile batch, string jobsFile, string capturesDir, ResearchContract c, string exportDir, Action<string> log)
        {
            if (Directory.Exists(exportDir)) throw new IOException("export folder exists (not overwriting): " + exportDir);
            var views = Load(batch, capturesDir, out var problems);
            Directory.CreateDirectory(exportDir);
            var man = new ExportManifest
            {
                experiment_id = c.experiment_id, contract_sha256 = ContractIO.Hash(c), jobs_file_sha256 = ScenarioIO.Sha256Hex(File.ReadAllBytes(jobsFile)),
                created_utc = DateTime.UtcNow.ToString("o"), class_map_version = c.labels.class_map_version, class_map = ClassMaps.Describe(c.labels.class_map_version),
                primary_bbox_policy = c.labels.primary_bbox_policy + "/" + c.labels.primary_bbox_policy_version, policy_version = PolicyVersion(c.labels),
                trainer_adapter = c.labels.trainer_adapter, adapter_supports_ignore = c.labels.adapter_supports_ignore,
            };
            var coco = new StringBuilder();
            var cocoImages = new List<string>(); var cocoAnns = new List<string>(); var cocoIgnore = new List<string>();
            int annId = 1, imgId = 1;
            var quarantine = new StringBuilder("job_id,rgb_sha256,reasons\n");
            foreach (var v in views)
            {
                var r = v.rec;
                var f = new ExportFrame
                {
                    job_id = v.job_id, mode = r?.job.mode ?? "", scene_kind = r?.job.scene_kind ?? "", episode_id = r?.job.episode_id ?? "", frame_index = r?.job.frame_index ?? -1,
                    pair_group_id = r?.job.pair_group_id ?? "", changed_factor = r?.job.changed_factor ?? "", changed_value = r?.job.changed_value ?? "",
                    rgb_sha256 = ScenarioIO.Sha256Hex(File.ReadAllBytes(Path.Combine(v.dir, "rgb.png"))), policy_version = man.policy_version,
                    negative_status = r?.negative_status ?? "", source_dir = v.dir,
                };
                f.split = r == null ? "unknown" : r.job.scene_kind == "sequence" ? r.job.split
                    : r.job.scene_kind == "planned" ? "synthetic_" + r.job.mode : "diagnostic_" + r.job.scene_kind;
                var integ = CheckIntegrity(v, c.labels.letterbox_check_size);
                man.integrity.Add(integ);
                if (r == null) f.reasons.Add("no research.json");
                if (!integ.ok) f.reasons.Add("integrity_failed: " + string.Join("; ", integ.errors));
                if (r != null && r.has_ignore_objects && !c.labels.adapter_supports_ignore) f.reasons.Add("ignore_regions_unsupported_by_adapter:" + c.labels.trainer_adapter);
                if (r != null && r.negative_status == "hard_negative_candidate_pending_policy") f.reasons.Add("negative_pending_policy: on-land / dry-storage boats in view");
                if (r != null && r.job.scene_kind == "paired") f.reasons.Add("paired_factor_diagnostic (kept out of the training sets)");
                f.labels = v.labels.objects.Count(o => o.detectability == "label");
                f.ignores = v.labels.objects.Count(o => o.detectability == "ignore");
                f.status = f.reasons.Count == 0 ? "included" : f.reasons.Any(x => x.StartsWith("integrity_failed") || x.StartsWith("no research")) ? "excluded" : "quarantined";
                // COCO keeps every frame that passed integrity (ignore regions as a separate list); YOLO only "included" frames
                if (integ.ok)
                {
                    int W = v.labels.image_width, H = v.labels.image_height;
                    cocoImages.Add($"{{\"id\":{imgId},\"file_name\":\"images/{f.split}/{v.job_id}.png\",\"width\":{W},\"height\":{H},\"job_id\":\"{v.job_id}\",\"rgb_sha256\":\"{f.rgb_sha256}\",\"export_status\":\"{f.status}\"}}");
                    foreach (var o in v.labels.objects.Where(o => o.visible_mask != null && o.visible_mask.bbox.valid))
                    {
                        var b = o.visible_mask.bbox;
                        string bb = string.Format(CultureInfo.InvariantCulture, "[{0:F2},{1:F2},{2:F2},{3:F2}]", b.x_min, b.y_min, b.Width, b.Height);
                        if (o.detectability == "label")
                            cocoAnns.Add(string.Format(CultureInfo.InvariantCulture, "{{\"id\":{0},\"image_id\":{1},\"category_id\":0,\"bbox\":{2},\"area\":{3},\"iscrowd\":0,\"sim_category\":\"{4}\",\"object_id\":\"{5}\"}}",
                                annId++, imgId, bb, o.visible_mask.pixel_count, o.category, o.object_id));
                        else if (o.detectability == "ignore")
                            cocoIgnore.Add(string.Format(CultureInfo.InvariantCulture, "{{\"image_id\":{0},\"bbox\":{1},\"object_id\":\"{2}\",\"reasons\":\"{3}\"}}",
                                imgId, bb, o.object_id, string.Join("; ", o.detectability_reasons).Replace("\"", "'")));
                    }
                    imgId++;
                }
                if (f.status == "included")
                {
                    var imgDir = Path.Combine(exportDir, "yolo", "images", f.split); var labDir = Path.Combine(exportDir, "yolo", "labels", f.split);
                    Directory.CreateDirectory(imgDir); Directory.CreateDirectory(labDir);
                    f.image_path = Path.Combine("yolo", "images", f.split, v.job_id + ".png").Replace('\\', '/');
                    f.label_path = Path.Combine("yolo", "labels", f.split, v.job_id + ".txt").Replace('\\', '/');
                    File.Copy(Path.Combine(v.dir, "rgb.png"), Path.Combine(exportDir, f.image_path));
                    File.Copy(Path.Combine(v.dir, "yolo_visible_bbox_provisional.txt"), Path.Combine(exportDir, f.label_path));
                    if (File.Exists(Path.Combine(v.dir, "rgb_clean.png"))) f.clean_image = "source rgb_clean.png kept in the capture folder (undistorted; no labels exported for it)";
                }
                else quarantine.AppendLine($"{v.job_id},{f.rgb_sha256},\"{string.Join(" | ", f.reasons).Replace("\"", "'")}\"");
                man.frames.Add(f);
            }
            foreach (var p in problems) man.frames.Add(new ExportFrame { job_id = p.Split(':')[0], status = "excluded", reasons = new List<string> { "unfinished capture: " + p } });
            man.frames_included = man.frames.Count(f => f.status == "included");
            man.frames_quarantined = man.frames.Count(f => f.status == "quarantined");
            man.frames_excluded = man.frames.Count(f => f.status == "excluded");
            var splits = man.frames.Where(f => f.status == "included").Select(f => f.split).Distinct().OrderBy(s => s).ToList();
            var yaml = new StringBuilder();
            yaml.AppendLine("# PROVISIONAL export (label_status provisional_diagnostic, training_ready false). Not a training release.");
            yaml.AppendLine("# class map " + c.labels.class_map_version + "; primary bbox " + man.primary_bbox_policy + "; frames with ignore objects are quarantined (adapter has no ignore support)");
            yaml.AppendLine("path: .");
            foreach (var s in splits) yaml.AppendLine($"{s}: images/{s}");
            yaml.AppendLine("names:\n  0: vessel");
            if (splits.Count > 0) { Directory.CreateDirectory(Path.Combine(exportDir, "yolo")); File.WriteAllText(Path.Combine(exportDir, "yolo", "data_provisional.yaml"), yaml.ToString(), new UTF8Encoding(false)); }
            Directory.CreateDirectory(Path.Combine(exportDir, "coco"));
            coco.Append("{\"info\":{\"description\":\"PROVISIONAL synthetic export\",\"label_status\":\"provisional_diagnostic\",\"training_ready\":false,");
            coco.Append($"\"policy_version\":\"{man.policy_version}\",\"ignore_semantics\":\"ignore_regions are visible vessels that fail the uncalibrated visibility rule; they are NOT COCO crowd regions (iscrowd stays 0) and need an evaluator / trainer that reads this list\"}},");
            coco.Append("\"categories\":[{\"id\":0,\"name\":\"vessel\"}],");
            coco.Append("\"images\":[" + string.Join(",", cocoImages) + "],");
            coco.Append("\"annotations\":[" + string.Join(",", cocoAnns) + "],");
            coco.Append("\"ignore_regions\":[" + string.Join(",", cocoIgnore) + "]}");
            File.WriteAllText(Path.Combine(exportDir, "coco", "annotations_provisional.json"), coco.ToString(), new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(exportDir, "quarantine.csv"), quarantine.ToString(), new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(exportDir, "export_manifest.json"), JsonUtility.ToJson(man, true), new UTF8Encoding(false));
            log?.Invoke($"provisional export: included {man.frames_included}, quarantined {man.frames_quarantined}, excluded {man.frames_excluded} -> {exportDir}");
            return man;
        }

        // ------------------------------------------------------------------------------------------------ QA package
        [Serializable]
        class QaItem { public string category, job_id, object_id, reason; public PixelBox bbox; public string overlay; }

        public static int WriteQaPackage(ExportManifest man, List<CaptureView> views, string qaDir, int perCategory, Action<string> log)
        {
            if (Directory.Exists(qaDir)) throw new IOException("QA folder exists (not overwriting): " + qaDir);
            Directory.CreateDirectory(Path.Combine(qaDir, "crops"));
            var picks = new List<QaItem>();
            void Add(string cat, CaptureView v, ObjectOutcome o, ObjectLabel l, string why)
            {
                if (picks.Count(p => p.category == cat) >= perCategory) return;
                if (picks.Any(p => p.category == cat && p.job_id == v.job_id)) return;
                picks.Add(new QaItem { category = cat, job_id = v.job_id, object_id = o?.object_id ?? "", reason = why, bbox = l?.visible_mask?.bbox });
            }
            foreach (var v in views.Where(v => v.rec != null).OrderBy(v => v.job_id, StringComparer.Ordinal))
            {
                var e = v.rec.environment;
                foreach (var o in v.rec.objects)
                {
                    var l = v.labels.objects.FirstOrDefault(x => x.object_id == o.object_id);
                    if (o.ach_height_px_at_1080 >= 0f && o.ach_height_px_at_1080 < 16f) Add("small_lt16px", v, o, l, $"visible height {o.ach_height_px_at_1080:F1} px@1080");
                    if (o.occlusion_fraction >= 0.3f) Add("occluded", v, o, l, $"geometric occlusion {o.occlusion_fraction:F2}");
                    if (o.truncation_bin == "partial" || o.truncation_bin == "heavy") Add("image_border", v, o, l, $"above-water box {o.truncation_fraction_above_water_box:P0} outside");
                    if (o.detectability == "ignore") Add("ignore_rule", v, o, l, string.Join("; ", o.detectability_reasons));
                    if (o.asset_id == "barge" || o.asset_id.Contains("container")) Add("equipment_cargo_scope", v, o, l, "barge equipment / cargo inside the vessel box?");
                    if (o.visible_px > 0 && (e.visibility_scale <= 0.35f || o.identifiability_fog_transmittance < 0.5f)) Add("heavy_fog", v, o, l, $"visibility_scale {e.visibility_scale}, fog transmittance {o.identifiability_fog_transmittance:F2}");
                    if (o.visible_px > 0 && e.night) Add("night", v, o, l, "night preset, luma contrast " + o.identifiability_luma_contrast.ToString("F3"));
                    if (o.visible_px > 0 && e.sensor_enabled && e.sensor_distortion) Add("distortion_blur", v, o, l, "sensor " + e.sensor_preset);
                    if (o.visible_px > 0 && e.water == "light_chop") Add("waves", v, o, l, "light_chop water");
                }
                if (v.rec.negative_status != "not_negative") Add("negative_candidate", v, null, null, v.rec.negative_status + ": " + string.Join(",", v.rec.non_vessels_in_view.Take(5)));
            }
            var csv = new StringBuilder("qa_id,category,job_id,object_id,frame_rgb_sha256,policy_version,bbox_xyxy,crop,overlay,auto_reason,geometric_visible,human_identifiable,human_box_ok,human_decision,human_note,reviewer\n");
            int k = 0;
            var html = new StringBuilder("<!doctype html><meta charset=utf-8><title>QA queue</title><style>body{font:13px sans-serif;background:#111;color:#ddd}img{max-width:420px;margin:4px;border:1px solid #444}.c{display:inline-block;vertical-align:top;width:440px}</style>");
            html.AppendLine("<h1>Provisional label QA queue</h1><p>Auto-selected samples. Every human_* column in qa_queue.csv is EMPTY and must be filled by a person (never by the tool). Geometric visibility (mask) and human identifiability are separate questions.</p>");
            foreach (var p in picks)
            {
                var v = views.First(x => x.job_id == p.job_id);
                var frame = man.frames.FirstOrDefault(f => f.job_id == p.job_id);
                string id = $"qa{k++:000}";
                string crop = "";
                if (p.bbox != null && p.bbox.valid) { crop = $"crops/{id}.png"; WriteCrop(Path.Combine(v.dir, "overlay.png"), p.bbox, Path.Combine(qaDir, crop), 256); }
                else { crop = $"crops/{id}_overlay.png"; File.Copy(Path.Combine(v.dir, "overlay.png"), Path.Combine(qaDir, crop)); }
                string bb = p.bbox != null && p.bbox.valid ? string.Format(CultureInfo.InvariantCulture, "{0:F0} {1:F0} {2:F0} {3:F0}", p.bbox.x_min, p.bbox.y_min, p.bbox.x_max, p.bbox.y_max) : "";
                csv.AppendLine($"{id},{p.category},{p.job_id},{p.object_id},{frame?.rgb_sha256},{man.policy_version},{bb},{crop},{Path.Combine(v.dir, "overlay.png").Replace('\\', '/')},\"{p.reason.Replace("\"", "'")}\",,,,,,");
                html.AppendLine($"<div class=c><b>{id} {p.category}</b><br>{p.job_id} {p.object_id}<br>{System.Net.WebUtility.HtmlEncode(p.reason)}<br><img src=\"{crop}\"></div>");
            }
            File.WriteAllText(Path.Combine(qaDir, "qa_queue.csv"), csv.ToString(), new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(qaDir, "index.html"), html.ToString(), new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(qaDir, "README.txt"),
                "QA queue for PROVISIONAL synthetic labels. Fill human_* columns by hand (decision: approved | rejected | needs_fix), then convert approved\n" +
                "rows to review records (JSONL, one ReviewRecord per frame: frame_rgb_sha256, policy_version, reviewer, decision, reviewed_utc, record_source=human).\n" +
                "The simulator only reads such records (training-release gate); it never writes or fills them.\n", new UTF8Encoding(false));
            log?.Invoke($"QA package: {picks.Count} samples in {picks.Select(p => p.category).Distinct().Count()} categories -> {qaDir}");
            return picks.Count;
        }

        static void WriteCrop(string png, PixelBox b, string outPath, int size)
        {
            var t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                t.LoadImage(File.ReadAllBytes(png), false);
                int W = t.width, H = t.height;
                float side = Mathf.Max(64f, 3f * Mathf.Max(b.Width, b.Height));
                float cx = (b.x_min + b.x_max) / 2, cy = (b.y_min + b.y_max) / 2;
                int x0 = Mathf.Clamp(Mathf.RoundToInt(cx - side / 2), 0, Mathf.Max(0, W - 1)), y0 = Mathf.Clamp(Mathf.RoundToInt(cy - side / 2), 0, Mathf.Max(0, H - 1));
                int sw = Mathf.Clamp(Mathf.RoundToInt(side), 1, W - x0), sh = Mathf.Clamp(Mathf.RoundToInt(side), 1, H - y0);
                var src = t.GetPixels32();
                var o = new Texture2D(size, size, TextureFormat.RGBA32, false);
                var dst = new Color32[size * size];
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                    {
                        int sx = x0 + Mathf.Min(sw - 1, x * sw / size), syTop = y0 + Mathf.Min(sh - 1, y * sh / size);
                        dst[(size - 1 - y) * size + x] = src[(H - 1 - syTop) * W + sx];
                    }
                o.SetPixels32(dst); o.Apply(false);
                File.WriteAllBytes(outPath, o.EncodeToPNG());
                Util.SafeDestroy(o);
            }
            finally { Util.SafeDestroy(t); }
        }

        // ------------------------------------------------------------------------------------------------ training release gate
        /// <summary>Builds a training release only if EVERY included frame has a human-approved review record for its RGB hash and
        /// the current policy version. Otherwise refuses and lists what is missing. Test-fixture records are accepted only with
        /// <paramref name="allowTestFixture"/> (unit tests) and the result is then marked as a test, never as a release.</summary>
        public static ReleaseResult TrainingRelease(string exportDir, string reviewRecordsPath, bool allowTestFixture, string releaseDir)
        {
            var man = JsonUtility.FromJson<ExportManifest>(File.ReadAllText(Path.Combine(exportDir, "export_manifest.json"), Encoding.UTF8));
            var res = new ReleaseResult();
            var recs = new List<ReviewRecord>();
            if (!string.IsNullOrEmpty(reviewRecordsPath) && File.Exists(reviewRecordsPath))
                foreach (var line in File.ReadAllLines(reviewRecordsPath, Encoding.UTF8).Where(l => !string.IsNullOrWhiteSpace(l)))
                    recs.Add(JsonUtility.FromJson<ReviewRecord>(line));
            var frames = man.frames.Where(f => f.status == "included").ToList();
            res.frames_considered = frames.Count;
            foreach (var f in frames)
            {
                var mine = recs.Where(r => r.frame_rgb_sha256 == f.rgb_sha256).ToList();
                var ok = mine.Where(r => r.policy_version == f.policy_version && (r.record_source == "human" || (allowTestFixture && r.record_source == "test_fixture"))).ToList();
                if (mine.Count > 0 && ok.Count == 0 && mine.Any(r => r.policy_version != f.policy_version)) { res.policy_mismatch++; res.missing_records.Add(f.job_id + ": record for another policy version"); continue; }
                if (ok.Count == 0) { res.missing++; res.missing_records.Add(f.job_id + ": no human review record"); continue; }
                if (ok.Any(r => r.decision != "approved")) { res.rejected++; res.missing_records.Add(f.job_id + ": not approved (" + string.Join(",", ok.Select(r => r.decision)) + ")"); continue; }
                res.approved++;
                if (ok.Any(r => r.record_source == "test_fixture")) res.test_fixture = true;
            }
            if (frames.Count == 0) res.reason = "no included frames";
            else if (res.approved != frames.Count) res.reason = $"refused: {res.approved}/{frames.Count} frames human-approved for policy {man.policy_version}";
            else
            {
                res.released = true;
                res.reason = res.test_fixture ? "TEST FIXTURE ONLY: approval records are test fixtures; this is not a release" : "all included frames human-approved";
            }
            if (!string.IsNullOrEmpty(releaseDir))
            {
                Directory.CreateDirectory(releaseDir);
                File.WriteAllText(Path.Combine(releaseDir, res.released ? "release_check.json" : "release_refusal.json"), JsonUtility.ToJson(res, true), new UTF8Encoding(false));
            }
            return res;
        }
    }
}
