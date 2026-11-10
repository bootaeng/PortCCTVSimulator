using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace PortCCTV.Research
{
    // =====================================================================================================================
    // Run contract (schema portcctv.research.contract.v1): one experiment = one immutable description of the comparison
    // arms, the real-data split / subsets, the generation (G / C) settings, the label policy and the planned (NOT executed)
    // training budget. Jobs, captures and exports carry the contract SHA-256; a capture made under another contract is never
    // reused as "done".
    // =====================================================================================================================

    [Serializable]
    public class ResearchContract
    {
        public const string Schema = "portcctv.research.contract.v1";
        public string contract_schema = Schema;
        public int contract_version = 1;
        public string experiment_id = "exp_untitled";
        /// <summary>draft: open human decisions remain (see pending_human_decisions). The tooling never sets "approved".</summary>
        public string status = "draft";
        public string created_utc = "";
        public string notes = "";
        public ProtocolSpec protocol = new ProtocolSpec();
        public FreezeRef freeze = new FreezeRef();
        public DataSpec data = new DataSpec();
        public List<ArmSpec> arms = new List<ArmSpec>();
        public TrainingBudget training = new TrainingBudget();
        public GenerationSpec generation = new GenerationSpec();
        public LabelSpec labels = new LabelSpec();
        public EvaluationSchema evaluation = new EvaluationSchema();
        /// <summary>Relative to the app root (project root in the Editor, exe folder in the player).</summary>
        public string output_dir = "Captures/research/exp_untitled";
        public List<string> pending_human_decisions = new List<string>();
        public List<LegacyName> legacy_names = new List<LegacyName>();
    }

    [Serializable]
    public class ProtocolSpec
    {
        /// <summary>adaptation_seen_camera: the target camera's training period / images may shape the C profile.
        /// unseen_generalization: no image or statistic of the target site may be used anywhere before the sealed test.</summary>
        public string protocol_id = Protocols.Adaptation;
        public string status = "draft";
        public List<string> target_sites = new List<string>();
        public string note = "";
    }

    public static class Protocols
    {
        public const string Adaptation = "adaptation_seen_camera";
        public const string Unseen = "unseen_generalization";
        public static readonly string[] All = { Adaptation, Unseen };
    }

    [Serializable]
    public class FreezeRef
    {
        public string freeze_id = "";
        public string freeze_manifest_path = "";
        public string freeze_manifest_sha256 = "";
        public string freeze_git_commit = "";
    }

    [Serializable]
    public class RoleOverride
    {
        /// <summary>dataset:&lt;id&gt; | group:&lt;id&gt; | clip:&lt;id&gt; | item:&lt;id&gt;</summary>
        public string selector = "";
        /// <summary>train | inner_val | calibration | sealed_test | excluded | inventory</summary>
        public string role = "";
        public string reason = "";
    }

    [Serializable]
    public class DataSpec
    {
        public List<string> real_manifest_paths = new List<string>();
        public List<string> real_manifest_sha256 = new List<string>();
        /// <summary>CV fold of the SMD v2 development pool held out as inner validation (1..4); the other folds are train.</summary>
        public int fold = 1;
        public string fraction_denominator = "annotated_frames";
        public List<float> fractions = new List<float> { 0.10f, 0.25f, 0.50f, 1.00f };
        public List<int> subset_seeds = new List<int> { 17, 42, 73 };
        public string subset_method = SubsetBuilder.Method;
        public string inner_val_use = "declared_in_advance: model selection / early-stop only";
        public string calibration_source = "pending";
        public string sealed_test_source = "pending";
        public string unlabelled_train_video_use = "not_used";
        /// <summary>Explicit role assignments on top of the default rules (e.g. a future calibration site).</summary>
        public List<RoleOverride> role_overrides = new List<RoleOverride>();
    }

    [Serializable]
    public class StageSpec
    {
        public string stage_id = "";
        /// <summary>real_subset | synthetic_G | synthetic_C</summary>
        public string data = "real_subset";
        /// <summary>basic | strong_2d</summary>
        public string augmentation = "basic";
        public int optimizer_updates;
        public string optimizer = "SGD";
        public float lr0 = 0.01f;
        public string lr_schedule = "linear_to_0.01";
        public int warmup_updates = 100;
        public bool reset_optimizer = true;
        public bool reset_lr_schedule = true;
        public string weight_selection = "last";
    }

    [Serializable]
    public class ArmSpec
    {
        public string arm_id;
        public string meaning;
        public List<StageSpec> stages = new List<StageSpec>();
    }

    [Serializable]
    public class LegacyName
    {
        public string arm_id;
        public string legacy;
        public string source_document;
    }

    [Serializable]
    public class TrainingBudget
    {
        /// <summary>The simulator never trains; the plan is exported for the training project.</summary>
        public string status = "planned_not_run";
        public int B_s = 3000;
        public int B_r = 3000;
        public int batch_size = 16;
        public int imgsz = 640;
        public string detector = "yolov8n (not run here)";
        public string base_weights = "COCO-pretrained yolov8n.pt (not fetched here)";
        public string augmentation_basic = "ultralytics defaults: hsv, fliplr 0.5, mosaic 1.0, scale 0.5";
        public string augmentation_strong_2d = "basic + blur / jpeg / noise / random fog-contrast / colour jitter (exact list pending)";
        public List<string> record_fields = new List<string>
        {
            "optimizer_updates_per_stage", "real_frame_exposures", "real_box_exposures", "synthetic_images_seen",
            "class_ratio", "size_bin_ratio", "positive_frame_ratio", "gpu_hours", "wall_hours"
        };
        public string note = "RR / SR_G / SR_C share B_s + B_r and every stage setting except the stage-1 data; R and A run B_r only, " +
                             "so their total compute is NOT equal to the two-stage arms.";
    }

    [Serializable]
    public class MapCameras
    {
        public string harbor_id;
        public List<string> camera_preset_ids = new List<string>();
    }

    /// <summary>Per-factor control. Empty list / negative number = the sampler decides.</summary>
    [Serializable]
    public class FactorOverrides
    {
        public List<string> lighting = new List<string>();
        public List<string> water = new List<string>();
        public List<float> fog_visibility_scale = new List<float>();
        public List<string> precipitation = new List<string>();
        public List<string> sensor = new List<string>();
        public int count_min = -1, count_max = -1;
        public float height_frac_min = -1f, height_frac_max = -1f;
        public float heading_deg = -1f;
        /// <summary>none | partial</summary>
        public List<string> occlusion = new List<string>();
        /// <summary>none | left | right</summary>
        public List<string> truncation = new List<string>();
    }

    [Serializable]
    public class PairedFactor
    {
        /// <summary>lighting | water | fog_visibility_scale | precipitation | sensor</summary>
        public string factor;
        public List<string> values = new List<string>();
    }

    [Serializable]
    public class SequenceSpec
    {
        public int episodes = 0;
        public int frames_per_episode = 3;
        public float interval_s = 2f;
        public string vessel_motion = "auto";
        /// <summary>Episode-level split assigned BEFORE generation (hash of the episode id); never per frame.</summary>
        public float val_fraction = 0.2f;
    }

    [Serializable]
    public class GenerationSpec
    {
        public List<string> modes = new List<string> { Samplers.G, Samplers.C };
        public List<MapCameras> maps = new List<MapCameras>();
        public List<int> seeds = new List<int> { 1, 2 };
        /// <summary>Planned captures per (mode, map, camera, seed); G and C always share this, the resolution and the retry limit.</summary>
        public int captures_per_cell = 1;
        public int width = 1920, height = 1080;
        public int retry_limit = 6;
        public int max_vessels = 6;
        /// <summary>harbor_vessel_rules: the map's plausible types per water region (the frozen asset list).</summary>
        public string asset_candidates = "harbor_vessel_rules";
        /// <summary>Requested vs achieved visible-box height must agree within this relative tolerance to count as "met".</summary>
        public float size_tolerance_rel = 0.35f;
        /// <summary>size_primary_nearest_feasible_row (default; same for G and C): keep the requested size and image column, move the row
        /// to the nearest feasible one and record the shift. strict_position: requested row only, otherwise planning_failed.</summary>
        public string position_rule = "size_primary_nearest_feasible_row";
        public string sampler_G_version = Samplers.GVersion;
        public string sampler_C_version = Samplers.CVersion;
        /// <summary>train_distribution_match (default C) | rare_condition_uniform (separate ablation, mode C_U)</summary>
        public string c_variant = "train_distribution_match";
        public string c_profile_path = "";
        public string c_profile_sha256 = "";
        public FactorOverrides factors = new FactorOverrides();
        /// <summary>per_object_water_only: a reference ID pass per vessel with every other vessel and all harbour geometry hidden
        /// (water kept) gives the denominator of the geometric occlusion fraction. off: occlusion = unknown.</summary>
        public string occlusion_reference = "per_object_water_only";
        public int empty_negatives_per_camera = 0;
        /// <summary>Boundary cases: deliberately unreachable requests (a vessel 90 % of the frame high) on the first camera; they must
        /// end as planning_failed with the reason recorded, never replaced by an easier scene.</summary>
        public int boundary_unreachable_cases = 0;
        public List<PairedFactor> paired = new List<PairedFactor>();
        public SequenceSpec sequence = new SequenceSpec();
        public RenderBudget render_budget = new RenderBudget();
    }

    [Serializable]
    public class RenderBudget
    {
        /// <summary>Hard cap of captures one run may render (this development phase: diagnostics only).</summary>
        public int max_captures_this_run = 36;
        public float estimated_mb_per_capture = 14f;
        public float estimated_s_per_capture = 12f;
    }

    [Serializable]
    public class LabelSpec
    {
        public string class_map_version = ClassMaps.V1;
        /// <summary>visible_mask_bbox | above_water_box | full_mesh_box (all three are always kept in labels.json).</summary>
        public string primary_bbox_policy = "visible_mask_bbox";
        public string primary_bbox_policy_version = "primary_bbox_v1";
        public string visibility_policy_version = "visibility_rule_v2_uncalibrated";
        /// <summary>Trainer adapter. ultralytics_yolo_txt has no ignore regions: frames with ignore objects are quarantined.</summary>
        public string trainer_adapter = "ultralytics_yolo_txt";
        public bool adapter_supports_ignore = false;
        public List<string> export_formats = new List<string> { "yolo", "coco" };
        public int letterbox_check_size = 640;
    }

    /// <summary>Future evaluation output schema. Every value is null / -1 until a real evaluation exists (none in this tool).</summary>
    [Serializable]
    public class EvaluationSchema
    {
        public string schema = "portcctv.research.eval.v1";
        public List<string> metrics = new List<string> { "AP50_95", "AP50", "recall_by_size_bin", "recall_at_fp_threshold", "fp_per_1000_frames" };
        public string threshold_rule = "confidence threshold fixed on the calibration split, then applied unchanged to the test split";
        public List<string> grouping_keys = new List<string> { "seed", "fold", "site", "clip_id", "episode_id" };
        public string fa_per_hour = "not computed from short clips";
        public string results = "none (no evaluation has been run)";
    }

    public static class ClassMaps
    {
        public const string V1 = "class_map_v1";

        /// <summary>Sim categories and source-dataset classes → common class. Source classes are kept in the exports.</summary>
        public static string Describe(string version) => version == V1
            ? "vessel=0 for every simulator vessel category and for SMD Boat / Vessel/ship / Ferry / Kayak / Sail boat; " +
              "SMD Other = ignore_pending_policy; buoys / channel marks = non_vessel (hard-negative candidates); " +
              "dry-storage (on-land) boats = pending_human_policy; wake / reflection = never labelled; barge cargo / equipment = part of the barge box"
            : "unknown class map";

        public static readonly string[] SmdVessel = { "Boat", "Vessel/ship", "Ferry", "Kayak", "Sail boat" };
        public static bool IsVesselSourceClass(string c) => Array.IndexOf(SmdVessel, c) >= 0;

        /// <summary>non_vessel kinds whose treatment needs a human decision (an image with them is not a clean negative).</summary>
        public static bool NonVesselNeedsPolicy(string kindOrId) =>
            kindOrId != null && (kindOrId.Contains("dry_boat") || kindOrId.Contains("dry_storage") || kindOrId.Contains("covered_boat"));
    }

    public static class ContractIO
    {
        public static string ToJson(ResearchContract c) => JsonUtility.ToJson(c, true);

        public static ResearchContract FromJson(string json)
        {
            var c = JsonUtility.FromJson<ResearchContract>(json);
            if (c == null) throw new InvalidDataException("contract JSON could not be parsed");
            if (c.contract_schema != ResearchContract.Schema)
                throw new InvalidDataException($"unsupported contract schema '{c.contract_schema}' (expected {ResearchContract.Schema})");
            return c;
        }

        public static ResearchContract Load(string path) => FromJson(File.ReadAllText(path, Encoding.UTF8));

        public static void Save(ResearchContract c, string path, bool overwrite)
        {
            if (File.Exists(path) && !overwrite) throw new IOException("refusing to overwrite " + path);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            File.WriteAllText(path, ToJson(c), new UTF8Encoding(false));
        }

        /// <summary>SHA-256 of the canonical (compact) JSON. The value jobs / captures / exports carry.</summary>
        public static string Hash(ResearchContract c) => ScenarioIO.Sha256Hex(JsonUtility.ToJson(c, false));

        public static string FileSha(string path) => ScenarioIO.Sha256Hex(File.ReadAllBytes(path));
    }

    /// <summary>Default contract: the five arms with the shared two-stage settings, SMD v2 fold 1, the pilot map / camera set.</summary>
    public static class ContractTemplates
    {
        public static ResearchContract Pilot(string experimentId)
        {
            var c = new ResearchContract
            {
                experiment_id = experimentId, created_utc = DateTime.UtcNow.ToString("o"),
                output_dir = "Captures/research/" + experimentId,
                notes = "Technical pilot of the research tooling. No detector is trained and no test split is evaluated.",
            };
            c.freeze = new FreezeRef { freeze_id = "freeze_20261110b", freeze_manifest_path = "Research/freeze/freeze_20261110b.json" };   // b: barge pile fix + CT east shore walk (2026-11-10)
            c.data.real_manifest_paths.Add("Research/data/real_manifest_v1.json");
            c.data.real_manifest_paths.Add("Research/data/external_inventory_v1.json");
            c.arms = DefaultArms(c.training.B_s, c.training.B_r);
            c.legacy_names = DefaultLegacyNames();
            c.generation.maps = new List<MapCameras>
            {
                new MapCameras { harbor_id = ContainerTerminal01.Id, camera_preset_ids = new List<string> { "ct_A_quay_east_mast", "ct_B_breakwater_head" } },
                new MapCameras { harbor_id = FishingPort01.Id, camera_preset_ids = new List<string> { "fp_A_market_roof", "fp_C_breakwater" } },
                new MapCameras { harbor_id = Marina01.Id, camera_preset_ids = new List<string> { "mr_A_promenade", "mr_C_breakwater" } },
            };
            c.generation.c_profile_path = "Research/profiles/profile_fixture_v1.json";
            c.pending_human_decisions = new List<string>
            {
                "protocol: adaptation_seen_camera vs unseen_generalization and the target CCTV site(s)",
                "calibration split source (no independent calibration data assigned)",
                "sealed test source (the v2 test 139 frames are exposed and cannot be sealed)",
                "human label policy: primary bbox (visible / above-water / full), minimum size, fog / night identifiability, SMD Other, dry-storage boats",
                "human review of the provisional synthetic labels (training_ready stays false)",
                "C profile approval: which real train subset may shape the C sampler (fixture used until then)",
                "strong 2D augmentation list for arm A and the training budget B_s / B_r",
            };
            return c;
        }

        public static List<ArmSpec> DefaultArms(int bs, int br)
        {
            StageSpec Stage(string id, string data, string aug, int n) => new StageSpec { stage_id = id, data = data, augmentation = aug, optimizer_updates = n };
            return new List<ArmSpec>
            {
                new ArmSpec { arm_id = "R", meaning = "real only, basic augmentation", stages = { Stage("real", "real_subset", "basic", br) } },
                new ArmSpec { arm_id = "A", meaning = "same real data + strong 2D augmentation", stages = { Stage("real", "real_subset", "strong_2d", br) } },
                new ArmSpec { arm_id = "RR", meaning = "real for B_s updates, then the same real for B_r updates (compute control)",
                    stages = { Stage("pretrain", "real_subset", "basic", bs), Stage("finetune", "real_subset", "basic", br) } },
                new ArmSpec { arm_id = "SR_G", meaning = "generic synthetic (G) for B_s, then the same real for B_r",
                    stages = { Stage("pretrain", "synthetic_G", "basic", bs), Stage("finetune", "real_subset", "basic", br) } },
                new ArmSpec { arm_id = "SR_C", meaning = "CCTV-matched synthetic (C) for B_s, then the same real for B_r",
                    stages = { Stage("pretrain", "synthetic_C", "basic", bs), Stage("finetune", "real_subset", "basic", br) } },
            };
        }

        public static List<LegacyName> DefaultLegacyNames() => new List<LegacyName>
        {
            new LegacyName { arm_id = "R", legacy = "R (real only)", source_document = "docs/RESEARCH_EXPERIMENT_DESIGN_20261106.md" },
            new LegacyName { arm_id = "A", legacy = "R+A (real + 2D augmentation)", source_document = "docs/RESEARCH_EXPERIMENT_DESIGN_20261106.md" },
            new LegacyName { arm_id = "A", legacy = "B (strong 2D) in earlier A/B naming", source_document = "paper/oceans plans (A=real, B=2D)" },
            new LegacyName { arm_id = "R", legacy = "A (real resampling) in earlier A/B naming", source_document = "paper/oceans plans (A=real, B=2D)" },
            new LegacyName { arm_id = "SR_G", legacy = "S->R with R+S-U style uniform synthetic", source_document = "docs/RESEARCH_EXPERIMENT_DESIGN_20261106.md" },
            new LegacyName { arm_id = "SR_C", legacy = "S->R with R+S-C condition-coverage synthetic", source_document = "docs/RESEARCH_EXPERIMENT_DESIGN_20261106.md" },
        };
    }

    public class ValidationResult
    {
        public readonly List<string> errors = new List<string>();
        public readonly List<string> warnings = new List<string>();
        public bool Ok => errors.Count == 0;
        public override string ToString() => $"errors {errors.Count}, warnings {warnings.Count}" +
            (errors.Count > 0 ? "\n  E " + string.Join("\n  E ", errors) : "") + (warnings.Count > 0 ? "\n  W " + string.Join("\n  W ", warnings) : "");
    }

    public static class ContractValidator
    {
        public static readonly string[] ArmIds = { "R", "A", "RR", "SR_G", "SR_C" };

        static string StageKeyWithoutData(StageSpec s) =>
            $"{s.augmentation}|{s.optimizer_updates}|{s.optimizer}|{s.lr0:R}|{s.lr_schedule}|{s.warmup_updates}|{s.reset_optimizer}|{s.reset_lr_schedule}|{s.weight_selection}";

        public static ValidationResult Validate(ResearchContract c)
        {
            var r = new ValidationResult();
            if (string.IsNullOrWhiteSpace(c.experiment_id) || ScenarioIO.SanitizeId(c.experiment_id) != c.experiment_id)
                r.errors.Add("experiment_id must be non-empty [A-Za-z0-9_-]");
            if (Array.IndexOf(Protocols.All, c.protocol.protocol_id) < 0) r.errors.Add("unknown protocol " + c.protocol.protocol_id);
            foreach (var id in ArmIds)
                if (c.arms.Count(a => a.arm_id == id) != 1) r.errors.Add("arm " + id + " must be defined exactly once");
            foreach (var a in c.arms) if (Array.IndexOf(ArmIds, a.arm_id) < 0) r.errors.Add("unknown arm " + a.arm_id);
            var arm = c.arms.ToDictionary(a => a.arm_id, a => a);
            if (r.errors.Count == 0)
            {
                foreach (var id in new[] { "R", "A" })
                    if (arm[id].stages.Count != 1 || arm[id].stages[0].data != "real_subset" || arm[id].stages[0].optimizer_updates != c.training.B_r)
                        r.errors.Add(id + " must be one real_subset stage of B_r updates");
                if (arm["R"].stages.Count == 1 && arm["R"].stages[0].augmentation != "basic") r.errors.Add("R must use basic augmentation");
                if (arm["A"].stages.Count == 1 && arm["A"].stages[0].augmentation != "strong_2d") r.errors.Add("A must use strong_2d augmentation");
                var two = new[] { "RR", "SR_G", "SR_C" };
                var stage1Data = new Dictionary<string, string> { { "RR", "real_subset" }, { "SR_G", "synthetic_G" }, { "SR_C", "synthetic_C" } };
                foreach (var id in two)
                {
                    var s = arm[id].stages;
                    if (s.Count != 2) { r.errors.Add(id + " must have exactly two stages"); continue; }
                    if (s[0].data != stage1Data[id]) r.errors.Add($"{id} stage 1 data must be {stage1Data[id]}");
                    if (s[0].optimizer_updates != c.training.B_s) r.errors.Add($"{id} stage 1 must be B_s={c.training.B_s} optimizer updates");
                    if (s[1].data != "real_subset" || s[1].optimizer_updates != c.training.B_r) r.errors.Add($"{id} stage 2 must be real_subset with B_r updates");
                }
                if (r.errors.Count == 0)
                {
                    var k1 = two.Select(id => StageKeyWithoutData(arm[id].stages[0])).Distinct().ToList();
                    var k2 = two.Select(id => StageKeyWithoutData(arm[id].stages[1]) + "|" + arm[id].stages[1].data).Distinct().ToList();
                    if (k1.Count != 1) r.errors.Add("RR / SR_G / SR_C stage-1 settings differ in something other than the data source");
                    if (k2.Count != 1) r.errors.Add("RR / SR_G / SR_C stage-2 (real fine-tune) settings differ");
                    if (!arm["RR"].stages[1].reset_optimizer || !arm["SR_G"].stages[1].reset_optimizer || !arm["SR_C"].stages[1].reset_optimizer)
                        r.warnings.Add("stage boundary does not reset the optimizer (allowed, but must be identical for the three arms)");
                }
                r.warnings.Add("R and A use B_r updates only: total compute differs from RR / SR_G / SR_C (report it, do not call it equal)");
            }
            var fr = c.data.fractions;
            if (fr.Count == 0 || fr.Any(f => f <= 0f || f > 1f)) r.errors.Add("fractions must be in (0, 1]");
            if (!fr.Contains(1f)) r.warnings.Add("fractions do not include 1.0 (no full-label reference)");
            for (int i = 1; i < fr.Count; i++) if (fr[i] <= fr[i - 1]) r.errors.Add("fractions must be strictly increasing (nested subsets)");
            if (c.data.subset_seeds.Count == 0) r.errors.Add("at least one subset seed is required");
            if (c.data.subset_seeds.Distinct().Count() != c.data.subset_seeds.Count) r.errors.Add("duplicate subset seeds");
            var g = c.generation;
            if (g.modes.Any(m => Array.IndexOf(Samplers.Modes, m) < 0)) r.errors.Add("unknown generation mode in " + string.Join(",", g.modes));
            if (g.retry_limit < 1 || g.retry_limit > 50) r.errors.Add("retry_limit must be 1..50");
            if (g.width < 64 || g.height < 64 || g.width > 4096 || g.height > 4096) r.errors.Add("capture size out of range");
            if (g.max_vessels < 1 || g.max_vessels > 60) r.errors.Add("max_vessels must be 1..60 (the instance mask holds at most 254 ids)");
            foreach (var m in g.maps)
                foreach (var cam in m.camera_preset_ids)
                    if (!CameraPresetLibrary.BelongsTo(cam, m.harbor_id)) r.errors.Add($"camera {cam} is not an existing preset of {m.harbor_id}");
            if (c.labels.trainer_adapter == "ultralytics_yolo_txt" && c.labels.adapter_supports_ignore)
                r.errors.Add("ultralytics_yolo_txt does not read ignore regions; adapter_supports_ignore must be false");
            if (c.status == "approved") r.errors.Add("status 'approved' is never set by the tooling");
            return r;
        }
    }

    [Serializable]
    public class TrainingRun
    {
        public string run_id, arm_id;
        public int fold;
        public float fraction;
        public int seed;
        public string real_subset_id, real_subset_sha256;
        public int real_subset_frames;
        public string synthetic_set = "";
        public List<StageSpec> stages = new List<StageSpec>();
        public int total_optimizer_updates;
        public string config_sha256;
        public string status = "planned_not_run";
    }

    [Serializable]
    public class TrainingPlanFile
    {
        public string plan_schema = "portcctv.research.trainingplan.v1";
        public string experiment_id, contract_sha256, created_utc;
        public string status = "planned_not_run: the simulator never trains";
        public List<TrainingRun> runs = new List<TrainingRun>();
        public List<string> notes = new List<string>();
    }

    /// <summary>Expands arms x fractions x seeds for the contract fold. Every arm of one (fold, fraction, seed) references the SAME
    /// real subset (id + item hash); SR_C references a C set whose profile must come from that same subset (or the fixture).</summary>
    public static class TrainingPlanner
    {
        public static TrainingPlanFile Build(ResearchContract c, IList<SubsetRecord> subsets)
        {
            var plan = new TrainingPlanFile { experiment_id = c.experiment_id, contract_sha256 = ContractIO.Hash(c), created_utc = DateTime.UtcNow.ToString("o") };
            foreach (var seed in c.data.subset_seeds)
                foreach (var f in c.data.fractions)
                {
                    var sub = subsets.FirstOrDefault(s => s.fold == c.data.fold && Mathf.Approximately(s.fraction, f) && s.seed == seed)
                              ?? throw new InvalidOperationException($"no subset for fold {c.data.fold} fraction {f} seed {seed}");
                    foreach (var a in c.arms)
                    {
                        var run = new TrainingRun
                        {
                            arm_id = a.arm_id, fold = c.data.fold, fraction = f, seed = seed, real_subset_id = sub.subset_id,
                            real_subset_sha256 = sub.items_sha256, real_subset_frames = sub.n_items,
                            stages = a.stages.Select(st => JsonUtility.FromJson<StageSpec>(JsonUtility.ToJson(st))).ToList(),
                        };
                        if (a.stages.Any(st => st.data == "synthetic_G")) run.synthetic_set = "synthetic_G: one generic set per experiment (sampler " + c.generation.sampler_G_version + ")";
                        if (a.stages.Any(st => st.data == "synthetic_C")) run.synthetic_set = "synthetic_C@" + sub.subset_id + ": C profile must be built from this subset (fixture only for tooling tests)";
                        run.total_optimizer_updates = run.stages.Sum(st => st.optimizer_updates);
                        run.config_sha256 = ScenarioIO.Sha256Hex(JsonUtility.ToJson(run));
                        run.run_id = $"{a.arm_id}_{sub.subset_id}";
                        plan.runs.Add(run);
                    }
                }
            plan.notes.Add("Primary contrast SR_C - RR; CCTV-matching effect SR_C - SR_G (package difference, not a single-factor causal effect).");
            plan.notes.Add("R and A run B_r updates only: their total compute differs from the two-stage arms.");
            plan.notes.Add("Record per run: actual optimizer updates per stage, real frame / box exposures, synthetic images seen, class / size / positive ratios, GPU hours.");
            return plan;
        }
    }

    /// <summary>Episode rules for sequences: the split is decided per episode before generation; one camera per episode.</summary>
    public static class EpisodeRules
    {
        public static string Split(string episodeId, float valFraction) =>
            Rng64.SeedOf("episode_split", episodeId) % 1000UL < (ulong)Mathf.RoundToInt(Mathf.Clamp01(valFraction) * 1000f) ? "val" : "train";

        public static List<string> Validate(IEnumerable<ResearchJobInfo> jobs)
        {
            var errors = new List<string>();
            foreach (var g in jobs.Where(j => !string.IsNullOrEmpty(j.episode_id)).GroupBy(j => j.episode_id))
            {
                var splits = g.Select(j => j.split).Distinct().ToList();
                if (splits.Count > 1) errors.Add($"episode {g.Key} is split across {string.Join("+", splits)}");
                var cams = g.Select(j => j.harbor_id + "/" + j.camera_preset_id).Distinct().ToList();
                if (cams.Count > 1) errors.Add($"episode {g.Key} changes camera ({string.Join(",", cams)})");
                var idx = g.Select(j => j.frame_index).ToList();
                if (idx.Distinct().Count() != idx.Count) errors.Add($"episode {g.Key} repeats a frame index");
                var times = g.OrderBy(j => j.frame_index).Select(j => j.sim_time_s).ToList();
                for (int i = 1; i < times.Count; i++) if (times[i] <= times[i - 1]) { errors.Add($"episode {g.Key} sim time not increasing"); break; }
                var ids = g.Select(j => string.Join(",", j.objects.Select(o => o.object_id).OrderBy(x => x))).Distinct().ToList();
                if (ids.Count > 1) errors.Add($"episode {g.Key} object ids change between frames");
            }
            return errors;
        }
    }

    /// <summary>Deterministic 64-bit generator (SplitMix64); identical sequence on every platform and runtime.</summary>
    public sealed class Rng64
    {
        ulong _s;
        public Rng64(ulong seed) { _s = seed; }
        public ulong Next()
        {
            _s += 0x9E3779B97F4A7C15UL;
            ulong z = _s;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }
        public double NextDouble() => (Next() >> 11) * (1.0 / 9007199254740992.0);
        public float Range(float lo, float hi) => lo + (float)NextDouble() * (hi - lo);
        public int Int(int lo, int hiExclusive) => hiExclusive <= lo ? lo : lo + (int)(NextDouble() * (hiExclusive - lo));
        public T Pick<T>(IList<T> list) => list[Int(0, list.Count)];

        /// <summary>Seed from a list of strings (first 8 bytes of SHA-256 of "a|b|c").</summary>
        public static ulong SeedOf(params object[] parts)
        {
            using var sha = SHA256.Create();
            var h = sha.ComputeHash(Encoding.UTF8.GetBytes(string.Join("|", parts.Select(p => Convert.ToString(p, System.Globalization.CultureInfo.InvariantCulture)))));
            return BitConverter.ToUInt64(h, 0);
        }

        /// <summary>Same as SeedOf but as a non-negative int (for engine fields that take an int seed).</summary>
        public static int IntSeedOf(params object[] parts) => (int)(SeedOf(parts) & 0x7FFFFFFF);
    }
}
