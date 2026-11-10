using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace PortCCTV.Research
{
    // =====================================================================================================================
    // Real-data manifest (written READ-ONLY from the upstream data by Tools/research/import_real_data.py), research split
    // roles, leakage checks, nested real-label subsets and the train-only screen-space profile that feeds the C sampler.
    // =====================================================================================================================

    [Serializable]
    public class SourceBox
    {
        public string source_class;
        public float x, y, w, h;      // top-left pixel + size in source pixels
        public int outside, invalid;
    }

    [Serializable]
    public class RealItem
    {
        public string item_id, dataset, dataset_version, kind, source_file, clip_id;
        public int frame_index = -1;
        public string image_sha256, label_sha256;
        public int width, height;
        public string site = "unknown", camera = "unknown", session = "unknown", capture_date = "unknown";
        public string group_id, group_confidence, duplicate_group, role_hint;
        public int fold = -1;
        public List<string> exposure = new List<string>();
        public bool has_media, has_labels, human_reviewed;
        public string label_policy, license, usage_restriction;
        public int vessel_boxes;
        public List<SourceBox> boxes = new List<SourceBox>();
    }

    [Serializable]
    public class ManifestInput { public string path, sha256; public long bytes; }

    [Serializable]
    public class RealManifest
    {
        public string manifest_schema;
        public int manifest_version;
        public string manifest_part;
        public string created_utc, created_by, box_convention, statement;
        public List<ManifestInput> inputs = new List<ManifestInput>();
        public List<RealItem> items = new List<RealItem>();
    }

    public static class Roles
    {
        public const string Train = "train", InnerVal = "inner_val", Calibration = "calibration", SealedTest = "sealed_test";
        public const string Excluded = "excluded", Inventory = "inventory";
        /// <summary>Roles that take part in an experiment; nothing may cross between them.</summary>
        public static readonly string[] Research = { Train, InnerVal, Calibration, SealedTest };
        public static bool IsResearch(string r) => Array.IndexOf(Research, r) >= 0;
    }

    public class LoadedRealData
    {
        public readonly List<RealItem> items = new List<RealItem>();
        public readonly List<string> paths = new List<string>();
        public readonly List<string> sha256 = new List<string>();

        public static LoadedRealData Load(IEnumerable<string> paths)
        {
            var d = new LoadedRealData();
            foreach (var p in paths)
            {
                var full = AppPaths.ResolveUnderRoot(p);
                var bytes = File.ReadAllBytes(full);
                var m = JsonUtility.FromJson<RealManifest>(Encoding.UTF8.GetString(bytes));
                if (m == null || m.manifest_schema != "portcctv.research.realmanifest.v1")
                    throw new InvalidDataException("not a portcctv.research.realmanifest.v1 file: " + p);
                d.items.AddRange(m.items);
                d.paths.Add(p);
                d.sha256.Add(ScenarioIO.Sha256Hex(bytes));
            }
            var dup = d.items.GroupBy(i => i.item_id).FirstOrDefault(g => g.Count() > 1);
            if (dup != null) throw new InvalidDataException("item id in two manifests: " + dup.Key);
            return d;
        }
    }

    [Serializable]
    public class RoleCount { public string role; public int items; public int clips; public int groups; public int vessel_boxes; }

    [Serializable]
    public class DatasetCount
    {
        public string dataset;
        public int items, independent_units, with_media, with_labels, human_reviewed, unknown_site;
        public string independence_note;
    }

    [Serializable]
    public class SplitReport
    {
        public string report_schema = "portcctv.research.splitcheck.v1";
        public string experiment_id, contract_sha256, protocol_id;
        public int fold;
        public List<string> manifest_sha256 = new List<string>();
        public List<RoleCount> roles = new List<RoleCount>();
        public List<DatasetCount> datasets = new List<DatasetCount>();
        public List<string> errors = new List<string>();
        public List<string> warnings = new List<string>();
        public List<string> pending = new List<string>();
        public bool ok;
        public List<SubsetRecord> subsets = new List<SubsetRecord>();
    }

    [Serializable]
    public class ItemRole { public string item_id; public string role; public string reason; }

    public class SplitAssignment
    {
        public readonly Dictionary<string, string> role = new Dictionary<string, string>();
        public readonly Dictionary<string, string> reason = new Dictionary<string, string>();
        public IEnumerable<RealItem> ItemsIn(IEnumerable<RealItem> items, string r) => items.Where(i => role.TryGetValue(i.item_id, out var x) && x == r);
    }

    public static class SplitPlanner
    {
        /// <summary>Default rules + contract overrides. SMD v2 development pool: CV fold == contract fold → inner_val, other folds →
        /// train. v2 test frames (exposed in past experiments) → excluded. Everything else (videos, other datasets, Korean CCTV) →
        /// inventory until the contract assigns it a role. Calibration / sealed test exist only through explicit overrides.</summary>
        public static SplitAssignment Assign(IList<RealItem> items, ResearchContract c)
        {
            var a = new SplitAssignment();
            foreach (var it in items)
            {
                string r, why;
                if (it.dataset == "smd_plus_v2_extract")
                {
                    if (it.exposure.Contains("v2_test_exposed_in_past_experiments")) { r = Roles.Excluded; why = "v2 test exposed in past experiments"; }
                    else if (it.fold <= 0) { r = Roles.Excluded; why = "not in the CV development pool"; }
                    else if (it.fold == c.data.fold) { r = Roles.InnerVal; why = "CV fold " + it.fold + " held out"; }
                    else { r = Roles.Train; why = "CV fold " + it.fold; }
                }
                else { r = Roles.Inventory; why = "no research role assigned"; }
                a.role[it.item_id] = r; a.reason[it.item_id] = why;
            }
            foreach (var ov in c.data.role_overrides)
            {
                var parts = ov.selector.Split(new[] { ':' }, 2);
                if (parts.Length != 2) throw new InvalidDataException("bad role override selector " + ov.selector);
                Func<RealItem, bool> sel = parts[0] switch
                {
                    "dataset" => i => i.dataset == parts[1],
                    "group" => i => i.group_id == parts[1],
                    "clip" => i => i.clip_id == parts[1],
                    "item" => i => i.item_id == parts[1],
                    _ => throw new InvalidDataException("bad role override selector " + ov.selector),
                };
                if (!Roles.IsResearch(ov.role) && ov.role != Roles.Excluded && ov.role != Roles.Inventory)
                    throw new InvalidDataException("bad role " + ov.role);
                foreach (var it in items.Where(sel)) { a.role[it.item_id] = ov.role; a.reason[it.item_id] = "override " + ov.selector + ": " + ov.reason; }
            }
            return a;
        }

        /// <summary>Leakage and validity checks. Errors make the split unusable; warnings record uncertainty.</summary>
        public static SplitReport Check(IList<RealItem> items, SplitAssignment a, ResearchContract c)
        {
            var rep = new SplitReport { experiment_id = c.experiment_id, protocol_id = c.protocol.protocol_id, fold = c.data.fold };
            var research = items.Where(i => Roles.IsResearch(a.role[i.item_id])).ToList();
            // 1. no clip / group / duplicate group may span two research roles (adjacent frames share the clip; vessel passes
            //    and identity groups share the group; the same video / image in two dataset versions shares the duplicate group)
            foreach (var (key, label) in new (Func<RealItem, string>, string)[]
                     { (i => i.clip_id, "clip"), (i => i.group_id, "group"), (i => i.duplicate_group, "duplicate_group") })
                foreach (var g in research.Where(i => !string.IsNullOrEmpty(key(i))).GroupBy(key))
                {
                    var roles = g.Select(i => a.role[i.item_id]).Distinct().ToList();
                    if (roles.Count > 1) rep.errors.Add($"{label} '{g.Key}' spans roles {string.Join("+", roles)} (leakage)");
                }
            // 2. a duplicate copy outside the experiment still may not be used as independent evaluation data
            foreach (var g in items.Where(i => !string.IsNullOrEmpty(i.duplicate_group)).GroupBy(i => i.duplicate_group))
            {
                var rs = g.Select(i => a.role[i.item_id]).Where(Roles.IsResearch).Distinct().ToList();
                var ds = g.Select(i => i.dataset).Distinct().ToList();
                if (rs.Contains(Roles.SealedTest) && ds.Count > 1 && rs.Count > 1)
                    rep.errors.Add($"duplicate_group '{g.Key}' ({string.Join(",", ds)}) is in sealed_test and in {string.Join("+", rs)}");
            }
            // 3. sealed test: labelled, never exposed during development; unseen protocol: no map-design / target-site exposure
            foreach (var it in a.ItemsIn(items, Roles.SealedTest))
            {
                if (it.exposure.Count > 0) rep.errors.Add($"{it.item_id} cannot be sealed_test: exposed ({string.Join(",", it.exposure)})");
                if (!it.has_labels) rep.errors.Add($"{it.item_id} cannot be sealed_test: no labels");
                if (!it.human_reviewed) rep.warnings.Add($"{it.item_id}: sealed_test labels not human reviewed");
            }
            if (c.protocol.protocol_id == Protocols.Unseen)
            {
                var testSites = new HashSet<string>(a.ItemsIn(items, Roles.SealedTest).Select(i => i.site).Where(s => s != "unknown"));
                foreach (var it in research.Where(i => a.role[i.item_id] != Roles.SealedTest && testSites.Contains(i.site)))
                    rep.errors.Add($"unseen protocol: {it.item_id} ({a.role[it.item_id]}) is from sealed-test site {it.site}");
                if (a.ItemsIn(items, Roles.SealedTest).Any(i => i.site == "unknown"))
                    rep.warnings.Add("unseen protocol: sealed_test items with unknown site cannot be shown to be unseen");
            }
            // 4. training data: labels exist; group certainty is recorded
            foreach (var it in a.ItemsIn(items, Roles.Train).Concat(a.ItemsIn(items, Roles.InnerVal)))
                if (!it.has_labels) rep.errors.Add($"{it.item_id} is {a.role[it.item_id]} without labels");
            if (research.Any(i => i.group_confidence == "unknown" || (i.group_confidence ?? "").StartsWith("proxy")))
                rep.warnings.Add("some research items have only a proxy / unknown group: independence across roles is not established");
            if (!a.ItemsIn(items, Roles.Calibration).Any()) rep.pending.Add("no calibration split (threshold / FP operating points cannot be fixed yet)");
            if (!a.ItemsIn(items, Roles.SealedTest).Any()) rep.pending.Add("no sealed test split (final evaluation data not chosen)");
            if (research.Any(i => !i.human_reviewed)) rep.pending.Add("research items are not human reviewed (provisional source labels)");
            foreach (var r in Roles.Research.Concat(new[] { Roles.Excluded, Roles.Inventory }))
            {
                var its = a.ItemsIn(items, r).ToList();
                rep.roles.Add(new RoleCount { role = r, items = its.Count, clips = its.Select(i => i.clip_id).Where(s => !string.IsNullOrEmpty(s)).Distinct().Count(),
                    groups = its.Select(i => i.group_id).Where(s => !string.IsNullOrEmpty(s)).Distinct().Count(), vessel_boxes = its.Sum(i => i.vessel_boxes) });
            }
            foreach (var g in items.GroupBy(i => i.dataset))
            {
                // independent units: distinct duplicate groups (copies collapse); items without one count individually
                int indep = g.Select(i => string.IsNullOrEmpty(i.duplicate_group) ? "item:" + i.item_id : i.duplicate_group).Distinct().Count();
                rep.datasets.Add(new DatasetCount
                {
                    dataset = g.Key, items = g.Count(), independent_units = indep, with_media = g.Count(i => i.has_media),
                    with_labels = g.Count(i => i.has_labels), human_reviewed = g.Count(i => i.human_reviewed), unknown_site = g.Count(i => i.site == "unknown"),
                    independence_note = "units = distinct duplicate groups inside this dataset; copies across datasets are counted once in total_independent_units",
                });
            }
            rep.datasets.Add(new DatasetCount
            {
                dataset = "ALL", items = items.Count,
                independent_units = items.Select(i => string.IsNullOrEmpty(i.duplicate_group) ? "item:" + i.item_id : i.duplicate_group).Distinct().Count(),
                with_media = items.Count(i => i.has_media), with_labels = items.Count(i => i.has_labels), human_reviewed = items.Count(i => i.human_reviewed),
                unknown_site = items.Count(i => i.site == "unknown"),
                independence_note = "SMD-Plus vs dataset/VIS_Onshore videos and ABOships vs ABOships-PLUS images collapse to one unit each; frames of one video are not independent of that video",
            });
            rep.ok = rep.errors.Count == 0;
            return rep;
        }
    }

    [Serializable]
    public class GroupCount { public string group_id; public int items; }

    [Serializable]
    public class SubsetRecord
    {
        public string subset_id;
        public int fold;
        public float fraction;
        public int seed;
        public string denominator = "annotated_frames";
        public int population_items, n_items, n_clips, n_groups, n_vessel_boxes;
        public List<GroupCount> group_counts = new List<GroupCount>();
        public string items_sha256;
        public List<string> item_ids = new List<string>();
    }

    /// <summary>Nested real-label subsets: one seeded permutation of the train items, every fraction takes a prefix of it, so
    /// 10% ⊂ 25% ⊂ 50% ⊂ 100% for the same (fold, seed). All arms of a (fold, fraction, seed) reference the same subset_id.</summary>
    public static class SubsetBuilder
    {
        public const string Method = "nested_prefix_of_splitmix64_permutation_v1 (denominator: annotated frames; ceil(f*N))";

        public static string SubsetId(int fold, float fraction, int seed) => $"f{fold}_p{Mathf.RoundToInt(fraction * 100):000}_s{seed}";

        public static List<SubsetRecord> Build(IList<RealItem> items, SplitAssignment a, int fold, IList<float> fractions, int seed)
        {
            var train = a.ItemsIn(items, Roles.Train).Where(i => i.kind == "frame" && i.has_labels).OrderBy(i => i.item_id, StringComparer.Ordinal).ToList();
            var rng = new Rng64(Rng64.SeedOf("subset", fold, seed));
            for (int i = train.Count - 1; i > 0; i--)
            {
                int j = rng.Int(0, i + 1);
                (train[i], train[j]) = (train[j], train[i]);
            }
            var list = new List<SubsetRecord>();
            foreach (var f in fractions)
            {
                int n = Mathf.Clamp(Mathf.CeilToInt(f * train.Count - 1e-6f), train.Count > 0 ? 1 : 0, train.Count);
                var pick = train.Take(n).ToList();
                var ids = pick.Select(i => i.item_id).OrderBy(s => s, StringComparer.Ordinal).ToList();
                list.Add(new SubsetRecord
                {
                    subset_id = SubsetId(fold, f, seed), fold = fold, fraction = f, seed = seed, population_items = train.Count, n_items = n,
                    n_clips = pick.Select(i => i.clip_id).Distinct().Count(), n_groups = pick.Select(i => i.group_id).Distinct().Count(),
                    n_vessel_boxes = pick.Sum(i => i.vessel_boxes),
                    group_counts = pick.GroupBy(i => i.group_id).OrderBy(g => g.Key).Select(g => new GroupCount { group_id = g.Key, items = g.Count() }).ToList(),
                    items_sha256 = ScenarioIO.Sha256Hex(string.Join("\n", ids)), item_ids = ids,
                });
            }
            return list;
        }
    }

    // ------------------------------------------------------------------------------------------------------------------
    // Profiles: screen-space statistics of the vessel boxes of ONE train subset (or an explicit fixture).
    // ------------------------------------------------------------------------------------------------------------------

    [Serializable]
    public class Histogram
    {
        public string name;
        public List<float> edges = new List<float>();
        public List<float> weights = new List<float>();
        public string within_bin = "uniform";   // uniform | log_uniform | integer

        public float Sample(Rng64 r)
        {
            float total = weights.Sum();
            if (total <= 0f || edges.Count != weights.Count + 1) throw new InvalidDataException("histogram " + name + " is empty or malformed");
            double t = r.NextDouble() * total; int k = 0;
            for (; k < weights.Count - 1; k++) { t -= weights[k]; if (t < 0) break; }
            float a = edges[k], b = edges[k + 1];
            if (within_bin == "log_uniform" && a > 0f) return Mathf.Exp(r.Range(Mathf.Log(a), Mathf.Log(b)));
            if (within_bin == "integer") return Mathf.Floor(r.Range(a, b));
            return r.Range(a, b);
        }

        public int BinOf(float v)
        {
            for (int k = 0; k < weights.Count; k++) if (v >= edges[k] && v < edges[k + 1]) return k;
            return v >= edges[edges.Count - 1] ? weights.Count - 1 : 0;
        }

        public float[] Normalised() { float s = weights.Sum(); return weights.Select(w => s > 0 ? w / s : 0f).ToArray(); }
    }

    [Serializable]
    public class ScreenProfile
    {
        public const string Schema = "portcctv.research.profile.v1";
        public string profile_schema = Schema;
        /// <summary>real_train_subset | fixture_not_real_data</summary>
        public string profile_kind;
        public string profile_id;
        public string created_utc;
        public string statement;
        public List<string> source_manifest_sha256 = new List<string>();
        public string subset_id = "";
        public string subset_items_sha256 = "";
        public int fold = -1;
        public List<string> roles_used = new List<string>();
        public int n_frames, n_boxes;
        public string label_review_status = "";
        public string coordinate_note = "height_frac = box height / image height; center_u = box centre x / width; bottom_v = box bottom y / height (top-left origin)";
        public Histogram height_frac, center_u, bottom_v, count_per_frame;
        public float truncated_fraction;
        public string environment_source = "none: no environment labels in the real data; C uses the same environment prior as G";
    }

    public static class ProfileBuilder
    {
        public static readonly float[] HeightEdges = { 0.004f, 0.008f, 0.015f, 0.03f, 0.06f, 0.12f, 0.25f };
        public static readonly float[] CountEdges = { 1, 2, 3, 4, 6, 9, 13, 21 };
        static Histogram Hist(string name, float[] edges, string within) => new Histogram { name = name, edges = edges.ToList(), weights = new float[edges.Length - 1].ToList(), within_bin = within };
        static List<float> Uniform(int bins, float lo, float hi) => Enumerable.Range(0, bins + 1).Select(k => lo + (hi - lo) * k / bins).ToList();

        /// <summary>Profile of the vessel boxes of the subset. Refuses any item that is not a train item of this subset.</summary>
        public static ScreenProfile FromSubset(IList<RealItem> items, SplitAssignment a, SubsetRecord subset, IList<string> manifestSha)
        {
            var byId = items.ToDictionary(i => i.item_id);
            var p = new ScreenProfile
            {
                profile_kind = "real_train_subset", profile_id = "profile_" + subset.subset_id, created_utc = DateTime.UtcNow.ToString("o"),
                subset_id = subset.subset_id, subset_items_sha256 = subset.items_sha256, fold = subset.fold,
                roles_used = new List<string> { Roles.Train }, source_manifest_sha256 = manifestSha.ToList(),
                label_review_status = "source labels not human reviewed in this project (provisional)",
                statement = "Statistics of one real TRAIN subset only; no inner-val / calibration / test / held-out item was read.",
                height_frac = Hist("height_frac", HeightEdges, "log_uniform"),
                center_u = new Histogram { name = "center_u", edges = Uniform(10, 0f, 1f), weights = new float[10].ToList() },
                bottom_v = new Histogram { name = "bottom_v", edges = Uniform(10, 0f, 1f), weights = new float[10].ToList() },
                count_per_frame = Hist("count_per_frame", CountEdges, "integer"),
            };
            int truncated = 0;
            foreach (var id in subset.item_ids)
            {
                if (!byId.TryGetValue(id, out var it)) throw new InvalidDataException("subset item missing from the manifest: " + id);
                if (!a.role.TryGetValue(id, out var r) || r != Roles.Train)
                    throw new InvalidOperationException($"profile input {id} has role '{r}', only train items may shape the C profile");
                if (it.width <= 0 || it.height <= 0) throw new InvalidDataException("item without image size " + id);
                int n = 0;
                foreach (var b in it.boxes)
                {
                    if (!ClassMaps.IsVesselSourceClass(b.source_class) || b.invalid != 0 || b.h <= 0 || b.w <= 0) continue;
                    n++;
                    p.n_boxes++;
                    float hf = b.h / it.height;
                    p.height_frac.weights[p.height_frac.BinOf(Mathf.Clamp(hf, HeightEdges[0], HeightEdges[HeightEdges.Length - 1] - 1e-6f))] += 1f;
                    p.center_u.weights[p.center_u.BinOf(Mathf.Clamp01((b.x + b.w / 2) / it.width))] += 1f;
                    p.bottom_v.weights[p.bottom_v.BinOf(Mathf.Clamp01((b.y + b.h) / it.height))] += 1f;
                    if (b.outside != 0 || b.x <= 0.5f || b.y <= 0.5f || b.x + b.w >= it.width - 0.5f || b.y + b.h >= it.height - 0.5f) truncated++;
                }
                p.n_frames++;
                if (n > 0) p.count_per_frame.weights[p.count_per_frame.BinOf(Mathf.Min(n, CountEdges[CountEdges.Length - 1] - 1))] += 1f;
            }
            p.truncated_fraction = p.n_boxes > 0 ? (float)truncated / p.n_boxes : 0f;
            return p;
        }

        /// <summary>Hand-set fixture for testing the C path before a real profile is approved. NOT derived from real data.</summary>
        public static ScreenProfile Fixture()
        {
            var p = new ScreenProfile
            {
                profile_kind = "fixture_not_real_data", profile_id = "profile_fixture_v1", created_utc = DateTime.UtcNow.ToString("o"),
                statement = "FIXTURE: hand-set numbers to exercise the C sampler. Not statistics of any real dataset; results made with it say nothing about real CCTV.",
                label_review_status = "not applicable (fixture)",
                height_frac = Hist("height_frac", HeightEdges, "log_uniform"),
                center_u = new Histogram { name = "center_u", edges = Uniform(10, 0f, 1f), weights = new List<float> { 2, 4, 6, 8, 9, 9, 8, 6, 4, 2 } },
                bottom_v = new Histogram { name = "bottom_v", edges = Uniform(10, 0f, 1f), weights = new List<float> { 0, 0, 0, 2, 6, 9, 8, 5, 3, 1 } },
                count_per_frame = Hist("count_per_frame", CountEdges, "integer"),
                truncated_fraction = 0.08f,
            };
            p.height_frac.weights = new List<float> { 3, 6, 8, 6, 3, 1 };
            p.count_per_frame.weights = new List<float> { 3, 3, 3, 3, 2, 0, 0 };
            return p;
        }

        public static string ToJson(ScreenProfile p) => JsonUtility.ToJson(p, true);

        public static ScreenProfile Load(string path)
        {
            var p = JsonUtility.FromJson<ScreenProfile>(File.ReadAllText(path, Encoding.UTF8));
            if (p == null || p.profile_schema != ScreenProfile.Schema) throw new InvalidDataException("not a profile file: " + path);
            return p;
        }

        /// <summary>Checks that a profile may drive the C sampler under this contract. Throws with the reason otherwise.</summary>
        public static void CheckUsable(ScreenProfile p, ResearchContract c, string expectedSubsetId)
        {
            if (p.profile_kind == "fixture_not_real_data") return;   // allowed, flagged as fixture in every output
            if (p.profile_kind != "real_train_subset") throw new InvalidOperationException("unknown profile_kind " + p.profile_kind);
            if (p.roles_used.Count != 1 || p.roles_used[0] != Roles.Train)
                throw new InvalidOperationException("profile was built from non-train roles: " + string.Join(",", p.roles_used));
            if (p.fold != c.data.fold) throw new InvalidOperationException($"profile fold {p.fold} != contract fold {c.data.fold}");
            if (!string.IsNullOrEmpty(expectedSubsetId) && p.subset_id != expectedSubsetId)
                throw new InvalidOperationException($"profile subset {p.subset_id} != run subset {expectedSubsetId} (a smaller-subset run may not use a larger subset's statistics)");
            if (c.data.real_manifest_sha256.Count > 0 && !p.source_manifest_sha256.All(c.data.real_manifest_sha256.Contains))
                throw new InvalidOperationException("profile comes from a different real manifest than the contract");
        }
    }
}
