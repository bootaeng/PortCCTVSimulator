using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using PortCCTV.Research;
using UnityEngine;

namespace PortCCTV.Tests
{
    /// <summary>Research tooling: leakage rejection, shared nested subsets, contract / hash checks on resume, partial outputs,
    /// ignore quarantine, letterbox / lens coordinates, episode rules, release gate, sampler stream separation.</summary>
    public class ResearchTests
    {
        string _tmp;

        [SetUp] public void SetUp() { _tmp = Path.Combine(Path.GetTempPath(), "portcctv_research_tests_" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(_tmp); }
        [TearDown] public void TearDown() { try { Directory.Delete(_tmp, true); } catch { } }

        // ---------------------------------------------------------------- fixtures
        static RealItem Frame(string clip, int frame, int fold, string group = null, string dataset = "smd_plus_v2_extract", string dup = null, params string[] exposure)
            => new RealItem
            {
                item_id = $"{dataset}:{clip}:f{frame:00000}", dataset = dataset, kind = "frame", clip_id = clip, frame_index = frame, fold = fold,
                group_id = group ?? "g_" + clip, group_confidence = "conservative", duplicate_group = dup ?? "smd_video:" + clip, has_media = true, has_labels = true,
                width = 1920, height = 1080, exposure = exposure.ToList(), vessel_boxes = 1,
                boxes = new List<SourceBox> { new SourceBox { source_class = "Boat", x = 100 + frame, y = 500, w = 60, h = 30 } },
            };

        static List<RealItem> SmallManifest()
        {
            var l = new List<RealItem>();
            for (int c = 0; c < 6; c++)
                for (int f = 0; f < 20; f++) l.Add(Frame("clip" + c, f * 30, 1 + c % 3));
            l.Add(Frame("clipT", 0, -1, null, "smd_plus_v2_extract", null, "v2_test_exposed_in_past_experiments"));
            return l;
        }

        static ResearchContract Contract() { var c = ContractTemplates.Pilot("unit_test"); c.data.fold = 1; return c; }

        // ---------------------------------------------------------------- contract
        [Test]
        public void Contract_PilotValid_AndStageMismatchRejected()
        {
            var c = Contract();
            var v = ContractValidator.Validate(c);
            Assert.IsTrue(v.Ok, v.ToString());
            c.arms.First(a => a.arm_id == "SR_C").stages[1].lr0 = 0.02f;
            Assert.IsFalse(ContractValidator.Validate(c).Ok, "different fine-tune lr must be rejected");
            c = Contract(); c.arms.First(a => a.arm_id == "RR").stages[0].optimizer_updates = 10;
            Assert.IsFalse(ContractValidator.Validate(c).Ok, "stage 1 must equal B_s");
            c = Contract(); c.labels.adapter_supports_ignore = true;
            Assert.IsFalse(ContractValidator.Validate(c).Ok, "ultralytics txt cannot claim ignore support");
            c = Contract(); c.status = "approved";
            Assert.IsFalse(ContractValidator.Validate(c).Ok, "the tooling never approves");
        }

        [Test]
        public void Contract_HashChangesWithContent()
        {
            var a = Contract(); var b = ContractIO.FromJson(ContractIO.ToJson(a));
            Assert.AreEqual(ContractIO.Hash(a), ContractIO.Hash(b));
            b.generation.retry_limit++;
            Assert.AreNotEqual(ContractIO.Hash(a), ContractIO.Hash(b));
        }

        // ---------------------------------------------------------------- split / leakage
        [Test]
        public void Split_DefaultRules_NoLeakage_ExposedTestExcluded()
        {
            var items = SmallManifest(); var c = Contract();
            var a = SplitPlanner.Assign(items, c);
            var rep = SplitPlanner.Check(items, a, c);
            Assert.IsTrue(rep.ok, string.Join("\n", rep.errors));
            Assert.AreEqual(Roles.Excluded, a.role["smd_plus_v2_extract:clipT:f00000"]);
            Assert.IsTrue(items.Where(i => i.fold == 1).All(i => a.role[i.item_id] == Roles.InnerVal));
            Assert.IsTrue(rep.pending.Any(p => p.Contains("sealed test")));
        }

        [Test]
        public void Split_LeakageInputsAreRejected()
        {
            var items = SmallManifest(); var c = Contract();
            // one frame of a train clip assigned to sealed_test: the clip spans two roles
            c.data.role_overrides.Add(new RoleOverride { selector = "item:smd_plus_v2_extract:clip1:f00030", role = Roles.SealedTest });
            var rep = SplitPlanner.Check(items, SplitPlanner.Assign(items, c), c);
            Assert.IsFalse(rep.ok);
            Assert.IsTrue(rep.errors.Any(e => e.Contains("clip 'clip1'")), string.Join("\n", rep.errors));

            // the exposed v2 test frames cannot become a sealed test
            c = Contract(); c.data.role_overrides.Add(new RoleOverride { selector = "clip:clipT", role = Roles.SealedTest });
            rep = SplitPlanner.Check(items, SplitPlanner.Assign(items, c), c);
            Assert.IsTrue(rep.errors.Any(e => e.Contains("exposed")), string.Join("\n", rep.errors));

            // the same image in two dataset versions (ABOships / PLUS) on both sides
            items.Add(Frame("abo1", 0, -1, "abo_rec:1", "aboships", "abo_img:x.png"));
            items.Add(Frame("abo1p", 0, -1, "abo_rec:1p", "aboships_plus", "abo_img:x.png"));
            c = Contract();
            c.data.role_overrides.Add(new RoleOverride { selector = "dataset:aboships", role = Roles.Train });
            c.data.role_overrides.Add(new RoleOverride { selector = "dataset:aboships_plus", role = Roles.SealedTest });
            rep = SplitPlanner.Check(items, SplitPlanner.Assign(items, c), c);
            Assert.IsTrue(rep.errors.Any(e => e.Contains("duplicate_group 'abo_img:x.png'")), string.Join("\n", rep.errors));
            // duplicate copies collapse to one independent unit: 7 videos (frames of one video are not independent) + 1 ABO image
            Assert.AreEqual(8, rep.datasets.First(d => d.dataset == "ALL").independent_units);
            Assert.AreEqual(items.Count, rep.datasets.First(d => d.dataset == "ALL").items);
        }

        // ---------------------------------------------------------------- subsets / training plan
        [Test]
        public void Subsets_AreNested_AndSharedByEveryArm()
        {
            var items = SmallManifest(); var c = Contract();
            var a = SplitPlanner.Assign(items, c);
            var subs = SubsetBuilder.Build(items, a, 1, c.data.fractions, 42);
            for (int i = 1; i < subs.Count; i++)
                Assert.IsTrue(subs[i - 1].item_ids.All(subs[i].item_ids.Contains), $"{subs[i - 1].subset_id} must be inside {subs[i].subset_id}");
            Assert.IsTrue(subs.All(s => s.item_ids.All(id => a.role[id] == Roles.Train)), "only train items");
            Assert.AreEqual(80, subs.Last().n_items);
            Assert.AreEqual(8, subs[0].n_items);   // ceil(0.1 * 80)
            var again = SubsetBuilder.Build(items, a, 1, c.data.fractions, 42);
            Assert.AreEqual(subs.Select(s => s.items_sha256), again.Select(s => s.items_sha256), "same seed reproduces the subsets");
            Assert.AreNotEqual(subs[1].items_sha256, SubsetBuilder.Build(items, a, 1, c.data.fractions, 17)[1].items_sha256);

            var all = c.data.subset_seeds.SelectMany(s => SubsetBuilder.Build(items, a, 1, c.data.fractions, s)).ToList();
            var plan = TrainingPlanner.Build(c, all);
            Assert.AreEqual(c.arms.Count * c.data.fractions.Count * c.data.subset_seeds.Count, plan.runs.Count);
            foreach (var g in plan.runs.GroupBy(r => (r.fraction, r.seed)))
            {
                Assert.AreEqual(1, g.Select(r => r.real_subset_sha256).Distinct().Count(), "every arm of one fraction / seed uses the same real subset");
                var two = g.Where(r => r.arm_id == "RR" || r.arm_id.StartsWith("SR")).ToList();
                Assert.AreEqual(1, two.Select(r => r.total_optimizer_updates).Distinct().Count(), "RR / SR_G / SR_C have the same total updates");
            }
            Assert.IsTrue(plan.runs.Where(r => r.arm_id == "SR_C").All(r => r.synthetic_set.Contains(r.real_subset_id)), "SR_C names its own subset's profile");
        }

        // ---------------------------------------------------------------- profile
        [Test]
        public void Profile_RefusesNonTrainInput_AndChecksSubset()
        {
            var items = SmallManifest(); var c = Contract();
            var a = SplitPlanner.Assign(items, c);
            var sub = SubsetBuilder.Build(items, a, 1, new List<float> { 1f }, 17)[0];
            var p = ProfileBuilder.FromSubset(items, a, sub, new[] { "x" });
            Assert.AreEqual("real_train_subset", p.profile_kind);
            Assert.AreEqual(sub.n_items, p.n_frames);
            Assert.AreEqual(sub.n_items, p.n_boxes);
            // smuggle an inner-val item into the subset
            var bad = JsonUtility.FromJson<SubsetRecord>(JsonUtility.ToJson(sub));
            bad.item_ids.Add(items.First(i => a.role[i.item_id] == Roles.InnerVal).item_id);
            Assert.Throws<InvalidOperationException>(() => ProfileBuilder.FromSubset(items, a, bad, new[] { "x" }));
            // a smaller-fraction run may not use the 100% profile
            Assert.Throws<InvalidOperationException>(() => ProfileBuilder.CheckUsable(p, c, SubsetBuilder.SubsetId(1, 0.1f, 17)));
            Assert.DoesNotThrow(() => ProfileBuilder.CheckUsable(p, c, sub.subset_id));
            var c2 = Contract(); c2.data.fold = 2;
            Assert.Throws<InvalidOperationException>(() => ProfileBuilder.CheckUsable(p, c2, null));
            Assert.AreEqual("fixture_not_real_data", ProfileBuilder.Fixture().profile_kind);
        }

        // ---------------------------------------------------------------- samplers
        [Test]
        public void Sampler_IsDeterministic_AndStreamsAreSeparate()
        {
            var g = Contract().generation;
            ResearchJobInfo Make(GenerationSpec gs, string mode = Samplers.G)
            {
                var j = new ResearchJobInfo { experiment_id = "e", mode = mode, harbor_id = "h", camera_preset_id = "c", generation_seed = 7 };
                ConditionSampler.Sample(mode, mode == Samplers.C ? ProfileBuilder.Fixture() : null, gs, j);
                return j;
            }
            var a = Make(g); var b = Make(g);
            Assert.AreEqual(JsonUtility.ToJson(a), JsonUtility.ToJson(b), "same seed, same request");
            var g2 = JsonUtility.FromJson<GenerationSpec>(JsonUtility.ToJson(g));
            g2.factors.lighting.Add("night_clear");
            g2.factors.sensor.Add("low_quality");
            var c = Make(g2);
            Assert.AreEqual("night_clear", c.environment.lighting);
            Assert.AreEqual(JsonUtility.ToJson(a.objects), JsonUtility.ToJson(c.objects), "an environment / sensor override must not move the vessels");
            Assert.AreEqual(a.layout_seed, c.layout_seed);
            var cc = Make(g, Samplers.C);
            Assert.IsTrue(cc.objects.All(o => o.target_height_frac >= ProfileBuilder.HeightEdges[0] && o.target_height_frac <= ProfileBuilder.HeightEdges.Last()));
        }

        // ---------------------------------------------------------------- episodes
        [Test]
        public void Episodes_SplitDecidedPerEpisode_AndViolationsDetected()
        {
            var ep = "ep_test_000";
            Assert.AreEqual(EpisodeRules.Split(ep, 0.2f), EpisodeRules.Split(ep, 0.2f));
            var frames = Enumerable.Range(0, 3).Select(k => new ResearchJobInfo { episode_id = ep, frame_index = k, sim_time_s = 2 * k, split = EpisodeRules.Split(ep, 0.2f), harbor_id = "h", camera_preset_id = "c",
                objects = new List<ObjectRequest> { new ObjectRequest { object_id = "t00" } } }).ToList();
            Assert.IsEmpty(EpisodeRules.Validate(frames));
            frames[2].split = frames[2].split == "train" ? "val" : "train";
            Assert.IsTrue(EpisodeRules.Validate(frames).Any(e => e.Contains("split across")));
            frames[2].split = frames[0].split; frames[1].camera_preset_id = "other";
            Assert.IsTrue(EpisodeRules.Validate(frames).Any(e => e.Contains("changes camera")));
            frames[1].camera_preset_id = "c"; frames[2].objects[0].object_id = "t01";
            Assert.IsTrue(EpisodeRules.Validate(frames).Any(e => e.Contains("object ids change")));
        }

        [Test]
        public void JobIds_StayShortEnoughForWindowsPaths()
        {
            var i = new ResearchJobInfo { mode = Samplers.G, camera_preset_id = "ct_B_breakwater_head", generation_seed = 5000, scene_kind = "paired",
                changed_factor = "fog_visibility_scale", changed_value = "0.3" };
            var id = JobFreezer.JobId(i);
            Assert.AreEqual("G_ct_B_breakwater_head_s5000_pfog_0_3", id);
            // the path that failed in diag r3 (260 characters) with the old id
            var root = @"C:\Users\USER\Desktop\DronePerception\simulator\PortCCTVSimulator\Builds\Windows\Captures\research\pilot_20261110_diag\jobs_diag_r3\captures";
            Assert.Greater(JobFreezer.LongestCapturePath(root, "G_ct_B_breakwater_head_s5000_pair_fog_visibility_scale_0_3"), JobFreezer.MaxCapturePath);
            Assert.LessOrEqual(JobFreezer.LongestCapturePath(root, id), JobFreezer.MaxCapturePath + 10);
        }

        // ---------------------------------------------------------------- coordinates
        [Test]
        public void Letterbox_BoxAndResizedMaskAgreeWithinOnePixel()
        {
            const int W = 1920, H = 1080;
            var m = new byte[W * H];
            var rects = new[] { new RectInt(100, 200, 37, 11), new RectInt(1500, 900, 300, 120), new RectInt(0, 0, 9, 7), new RectInt(1911, 1070, 9, 10) };
            for (int k = 0; k < rects.Length; k++)
                for (int y = rects[k].yMin; y < rects[k].yMax; y++) for (int x = rects[k].xMin; x < rects[k].xMax; x++) m[y * W + x] = (byte)(k + 1);
            foreach (int S in new[] { 640, 1280 })
            {
                var lb = ResearchReports.MakeLetterbox(W, H, S);
                var lm = ResearchReports.LetterboxMask(m, W, H, lb);
                for (int k = 0; k < rects.Length; k++)
                {
                    var src = ResearchReports.MaskBox(m, W, H, k + 1, out _);
                    var tb = ResearchReports.TransformBox(src, lb);
                    var rb = ResearchReports.MaskBox(lm, S, S, k + 1, out _);
                    Assert.IsTrue(rb.valid, $"rect {k} vanished at {S}");
                    Assert.LessOrEqual(Mathf.Abs(tb.x_min - rb.x_min), 1f); Assert.LessOrEqual(Mathf.Abs(tb.x_max - rb.x_max), 1f);
                    Assert.LessOrEqual(Mathf.Abs(tb.y_min - rb.y_min), 1f); Assert.LessOrEqual(Mathf.Abs(tb.y_max - rb.y_max), 1f);
                }
                Assert.AreEqual(0, lm[0], "padding stays background");
            }
        }

        [Test]
        public void Letterbox_ThinMastKeepsAlignmentWithCoverageResize()
        {
            // a hull with a 1-px mast: a nearest-neighbour resize drops the mast (box shrinks), the coverage resize keeps it
            const int W = 1920, H = 1080;
            var m = new byte[W * H];
            for (int y = 600; y < 620; y++) for (int x = 900; x < 960; x++) m[y * W + x] = 1;
            for (int y = 560; y < 600; y++) m[y * W + 930] = 1;   // column 930 is not a nearest sample at s = 1/3 (928, 931, 934 are)
            var lb = ResearchReports.MakeLetterbox(W, H, 640);
            var tb = ResearchReports.TransformBox(ResearchReports.MaskBox(m, W, H, 1, out _), lb);
            var cb = ResearchReports.MaskBox(ResearchReports.LetterboxMaskCoverage(m, W, H, lb, 1), 640, 640, 1, out _);
            var nb = ResearchReports.MaskBox(ResearchReports.LetterboxMask(m, W, H, lb), 640, 640, 1, out _);
            Assert.LessOrEqual(Mathf.Abs(tb.y_min - cb.y_min), 1f, "coverage resize keeps the mast top");
            Assert.LessOrEqual(Mathf.Abs(tb.x_min - cb.x_min), 1f); Assert.LessOrEqual(Mathf.Abs(tb.x_max - cb.x_max), 1f); Assert.LessOrEqual(Mathf.Abs(tb.y_max - cb.y_max), 1f);
            Assert.Greater(nb.y_min - tb.y_min, 5f, "nearest resize loses the 1-px mast (reported as shrink, not an alignment error)");
        }

        [Test]
        public void LensWarp_WarpedMaskMatchesProjectedPoints()
        {
            const int W = 960, H = 540;
            var lens = LensModel.Make(SensorConfig.Preset("low_quality"), W, H);
            Assert.IsNotNull(lens);
            var m = new byte[W * H];
            var r = new RectInt(700, 380, 120, 60);
            for (int y = r.yMin; y < r.yMax; y++) for (int x = r.xMin; x < r.xMax; x++) m[y * W + x] = 3;
            var wm = lens.WarpMask(m, W, H);
            var wb = ResearchReports.MaskBox(wm, W, H, 3, out int n);
            Assert.Greater(n, 0);
            float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
            for (int y = r.yMin; y < r.yMax; y++)
                for (int x = r.xMin; x < r.xMax; x++)
                {
                    if (y != r.yMin && y != r.yMax - 1 && x != r.xMin && x != r.xMax - 1) continue;
                    if (!lens.OutputOf(new Vector2(x + 0.5f, y + 0.5f), out var p)) continue;
                    x0 = Mathf.Min(x0, p.x); y0 = Mathf.Min(y0, p.y); x1 = Mathf.Max(x1, p.x); y1 = Mathf.Max(y1, p.y);
                }
            // pixel centres of the source edge map into the warped box (pixel-edge) within ~1.5 px
            Assert.LessOrEqual(Mathf.Abs(wb.x_min - (x0 - 0.5f)), 1.5f); Assert.LessOrEqual(Mathf.Abs(wb.x_max - (x1 + 0.5f)), 1.5f);
            Assert.LessOrEqual(Mathf.Abs(wb.y_min - (y0 - 0.5f)), 1.5f); Assert.LessOrEqual(Mathf.Abs(wb.y_max - (y1 + 0.5f)), 1.5f);
        }

        // ---------------------------------------------------------------- fake captures for resume / export / gate
        static void Png(string path, int W, int H, Func<int, int, Color32> px)
        {
            var t = new Texture2D(W, H, TextureFormat.RGBA32, false, true);
            var a = new Color32[W * H];
            for (int y = 0; y < H; y++) for (int x = 0; x < W; x++) a[(H - 1 - y) * W + x] = px(x, y);
            t.SetPixels32(a); t.Apply(false);
            File.WriteAllBytes(path, t.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(t);
        }

        /// <summary>A finished capture folder with two vessels (one label, optionally one ignore) and research.json.</summary>
        static string FakeCapture(string root, string jobId, ResearchJobInfo job, bool withIgnore, bool complete = true)
        {
            const int W = 64, H = 36;
            var dir = Path.Combine(root, jobId); Directory.CreateDirectory(dir);
            var box1 = new PixelBox { valid = true, x_min = 10, y_min = 12, x_max = 20, y_max = 18 };
            var box2 = new PixelBox { valid = true, x_min = 40, y_min = 5, x_max = 44, y_max = 8 };
            Png(Path.Combine(dir, "rgb.png"), W, H, (x, y) => new Color32((byte)(x * 3), (byte)(y * 5), 90, 255));
            Png(Path.Combine(dir, "overlay.png"), W, H, (x, y) => new Color32(0, 0, 0, 255));
            Png(Path.Combine(dir, "mask_instances.png"), W, H, (x, y) =>
                new Color32((byte)(x >= 10 && x < 20 && y >= 12 && y < 18 ? 1 : withIgnore && x >= 40 && x < 44 && y >= 5 && y < 8 ? 2 : 0), 0, 0, 255));
            var labels = new LabelSet { image_width = W, image_height = H };
            labels.objects.Add(new ObjectLabel { object_id = "t00", asset_id = "rib", instance_index = 1, detectability = "label",
                visible_mask = new VisibleMaskLabel { computed = true, pixel_count = 60, bbox = box1 } });
            if (withIgnore)
                labels.objects.Add(new ObjectLabel { object_id = "t01", asset_id = "rib", instance_index = 2, detectability = "ignore", detectability_reasons = new List<string> { "luma_contrast" },
                    visible_mask = new VisibleMaskLabel { computed = true, pixel_count = 12, bbox = box2 } });
            File.WriteAllText(Path.Combine(dir, "labels.json"), JsonUtility.ToJson(labels));
            File.WriteAllText(Path.Combine(dir, "scenario.json"), "{}");
            string L(PixelBox b) => string.Format(CultureInfo.InvariantCulture, "0 {0:F7} {1:F7} {2:F7} {3:F7}\n", (b.x_min + b.x_max) / 2 / W, (b.y_min + b.y_max) / 2 / H, b.Width / W, b.Height / H);
            File.WriteAllText(Path.Combine(dir, "yolo_visible_bbox_provisional.txt"), L(box1));
            File.WriteAllText(Path.Combine(dir, "yolo_ignore_regions_provisional.txt"), withIgnore ? L(box2) : "");
            var rec = new ResearchCaptureRecord { job_id = jobId, contract_sha256 = job.contract_sha256, job_scenario_sha256 = job.job_scenario_sha256, job = job, positive = true, has_ignore_objects = withIgnore };
            File.WriteAllText(Path.Combine(dir, "research.json"), JsonUtility.ToJson(rec));
            var meta = new CaptureMetadata { scenario_id = jobId, complete = complete };
            foreach (var f in Directory.GetFiles(dir).OrderBy(f => f, StringComparer.Ordinal))
            { meta.files.Add(Path.GetFileName(f)); meta.file_sha256.Add(ScenarioIO.Sha256Hex(File.ReadAllBytes(f))); }
            File.WriteAllText(Path.Combine(dir, "metadata.json"), JsonUtility.ToJson(meta));
            return dir;
        }

        static BatchJob Job(string id, string contract = "c1", string scen = "s1") => new BatchJob
        {
            job_id = id, max_attempts = 2,
            research = new ResearchJobInfo { experiment_id = "e", contract_sha256 = contract, job_scenario_sha256 = scen, mode = Samplers.G, scene_kind = "planned", plan_status = "planned" },
        };

        [Test]
        public void Resume_AcceptsOnlySameContractScenarioAndFinishedCapture()
        {
            var j = Job("G_cam_s0001");
            var dir = FakeCapture(_tmp, j.job_id, j.research, false);
            Assert.IsTrue(BatchRunner.VerifyResearchOutput(dir, j, "", out var why), why);
            Assert.IsFalse(BatchRunner.VerifyResearchOutput(dir, Job(j.job_id, contract: "c2"), "", out why)); StringAssert.Contains("contract hash", why);
            Assert.IsFalse(BatchRunner.VerifyResearchOutput(dir, Job(j.job_id, scen: "s2"), "", out why)); StringAssert.Contains("scenario hash", why);
            var jb = Job(j.job_id); jb.research.required_build_content_sha256 = "build_b";
            Assert.IsFalse(BatchRunner.VerifyResearchOutput(dir, jb, "build_b", out why), "capture of another build is not reused"); StringAssert.Contains("build", why);
            // tampered file -> SHA mismatch
            File.AppendAllText(Path.Combine(dir, "labels.json"), " ");
            Assert.IsFalse(BatchRunner.VerifyResearchOutput(dir, j, "", out why)); StringAssert.Contains("SHA-256", why);
            // partial / interrupted capture (complete=false)
            var d2 = FakeCapture(_tmp, "G_cam_s0002", Job("G_cam_s0002").research, false, complete: false);
            Assert.IsFalse(BatchRunner.VerifyResearchOutput(d2, Job("G_cam_s0002"), "", out why)); StringAssert.Contains("complete", why);
            // capture without research.json (made outside the contract)
            var d3 = FakeCapture(_tmp, "G_cam_s0003", Job("G_cam_s0003").research, false);
            File.Delete(Path.Combine(d3, "research.json"));
            Assert.IsFalse(BatchRunner.VerifyResearchOutput(d3, Job("G_cam_s0003"), "", out why));
        }

        [Test]
        public void RetryCount_ReadsFailuresAndToleratesATornLine()
        {
            var log = Path.Combine(_tmp, "batch_log.jsonl");
            File.WriteAllText(log,
                JsonUtility.ToJson(new BatchLogEntry { job_id = "a", status = "failed" }) + "\n" +
                JsonUtility.ToJson(new BatchLogEntry { job_id = "a", status = "done" }) + "\n" +
                JsonUtility.ToJson(new BatchLogEntry { job_id = "b", status = "failed" }) + "\n" +
                "{\"job_id\":\"a\",\"sta");
            Assert.AreEqual(1, BatchRunner.PreviousFailures(log, "a"));
            Assert.AreEqual(1, BatchRunner.PreviousFailures(log, "b"));
            Assert.AreEqual(0, BatchRunner.PreviousFailures(log, "c"));
        }

        [Test]
        public void Export_QuarantinesIgnoreFrames_KeepsIgnoreInCoco_AndGateRefusesWithoutHumanRecords()
        {
            var cap = Path.Combine(_tmp, "captures");
            var batch = new BatchFile { batch_id = "t", output_dir = cap, research_batch = true };
            var jClean = Job("G_cam_s0001"); var jIgn = Job("G_cam_s0002");
            batch.jobs.Add(jClean); batch.jobs.Add(jIgn);
            FakeCapture(cap, jClean.job_id, jClean.research, false);
            FakeCapture(cap, jIgn.job_id, jIgn.research, true);
            var jobsFile = Path.Combine(_tmp, "jobs.json"); File.WriteAllText(jobsFile, JsonUtility.ToJson(batch));
            var c = Contract(); c.labels.letterbox_check_size = 32;
            var exp = Path.Combine(_tmp, "export");
            var man = ResearchReports.ExportProvisional(batch, jobsFile, cap, c, exp, null);
            Assert.AreEqual("provisional_not_for_training", man.release_status);
            Assert.IsFalse(man.training_ready);
            Assert.IsTrue(man.integrity.All(i => i.ok), string.Join("\n", man.integrity.SelectMany(i => i.errors)));
            var fIgn = man.frames.First(f => f.job_id == jIgn.job_id);
            Assert.AreEqual("quarantined", fIgn.status);
            StringAssert.Contains("ignore_regions_unsupported_by_adapter", string.Join(";", fIgn.reasons));
            Assert.AreEqual("included", man.frames.First(f => f.job_id == jClean.job_id).status);
            Assert.IsFalse(File.Exists(Path.Combine(exp, "yolo", "images", "synthetic_G", jIgn.job_id + ".png")), "quarantined frame not in the YOLO set");
            var coco = File.ReadAllText(Path.Combine(exp, "coco", "annotations_provisional.json"));
            StringAssert.Contains("\"ignore_regions\":[{", coco);
            StringAssert.DoesNotContain("\"iscrowd\":1", coco);

            // release gate: refuses without records; test fixtures count only when explicitly allowed and are flagged
            var gate = ResearchReports.TrainingRelease(exp, null, false, null);
            Assert.IsFalse(gate.released); Assert.AreEqual(1, gate.missing);
            var rr = Path.Combine(_tmp, "reviews.jsonl");
            var fClean = man.frames.First(f => f.job_id == jClean.job_id);
            File.WriteAllText(rr, JsonUtility.ToJson(new ReviewRecord { frame_rgb_sha256 = fClean.rgb_sha256, policy_version = fClean.policy_version, decision = "approved", record_source = "test_fixture" }) + "\n");
            Assert.IsFalse(ResearchReports.TrainingRelease(exp, rr, false, null).released, "test fixture records are not human records");
            var t = ResearchReports.TrainingRelease(exp, rr, true, null);
            Assert.IsTrue(t.released); Assert.IsTrue(t.test_fixture); StringAssert.Contains("TEST FIXTURE", t.reason);
            File.WriteAllText(rr, JsonUtility.ToJson(new ReviewRecord { frame_rgb_sha256 = fClean.rgb_sha256, policy_version = "old_policy", decision = "approved", record_source = "human" }) + "\n");
            var pm = ResearchReports.TrainingRelease(exp, rr, false, null);
            Assert.IsFalse(pm.released); Assert.AreEqual(1, pm.policy_mismatch);
        }

        [Test]
        public void Integrity_DetectsMaskLabelDisagreementAndInstanceLimit()
        {
            var j = Job("G_cam_s0009");
            var dir = FakeCapture(_tmp, j.job_id, j.research, false);
            var v = new CaptureView { job_id = j.job_id, dir = dir, job = j,
                labels = JsonUtility.FromJson<LabelSet>(File.ReadAllText(Path.Combine(dir, "labels.json"))), meta = new CaptureMetadata() };
            Assert.IsTrue(ResearchReports.CheckIntegrity(v, 32).ok);
            v.labels.objects[0].visible_mask.bbox.x_max = 21;      // off by one pixel against the mask
            var r = ResearchReports.CheckIntegrity(v, 0);
            Assert.IsFalse(r.ok); Assert.IsTrue(r.errors.Any(e => e.Contains("differs from the mask")), string.Join("\n", r.errors));
            v.labels.objects[0].visible_mask.bbox.x_max = 20;
            v.labels.objects[0].instance_index = 300;
            Assert.IsTrue(ResearchReports.CheckIntegrity(v, 0).errors.Any(e => e.Contains("1..254")));
            v.labels.objects[0].instance_index = 1;
            v.labels.training_ready = true;
            Assert.IsTrue(ResearchReports.CheckIntegrity(v, 0).errors.Any(e => e.Contains("label status")));
        }
    }
}
