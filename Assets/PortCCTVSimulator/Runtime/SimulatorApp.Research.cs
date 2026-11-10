using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using PortCCTV.Research;
using UnityEngine;

namespace PortCCTV
{
    /// <summary>Research panel (tab "연구") and the research command-line flags. Flow: contract → data / split check → profile →
    /// conditions → preview → freeze jobs → batch (stop / resume) → coverage + QA → provisional export → release gate.</summary>
    public partial class SimulatorApp
    {
        ResearchContract _rc;
        string _rcPath = "Research/configs/pilot_contract.json";
        string _rcStatus = "";
        LoadedRealData _rcData;
        SplitReport _rcSplit;
        SplitAssignment _rcAssign;
        ScreenProfile _rcProfile;
        string _rcProfilePath = "";
        string _rcJobsName = "jobs_pilot";
        string _rcJobsFile = "";
        PlanReport _rcPlan;
        CoverageReport _rcCoverage;
        ExportManifest _rcExport;
        ReleaseResult _rcRelease;
        string _rcReviewPath = "Research/review/review_records.jsonl";
        string _rcSeeds = "1,2";
        int _rcSubsetSeedIdx;
        ResearchJobInfo _rcPreview;
        Vector2 _rcScroll;

        // ------------------------------------------------------------------ helpers shared by UI and CLI
        string RcFull(string p) => AppPaths.ResolveUnderRoot(p);

        bool RcLoad(string path)
        {
            try
            {
                _rc = ContractIO.Load(RcFull(path)); _rcPath = path; _rcSeeds = string.Join(",", _rc.generation.seeds);
                _rcData = null; _rcSplit = null; _rcProfile = null; _rcPlan = null; _rcCoverage = null; _rcExport = null; _rcRelease = null;
                var v = ContractValidator.Validate(_rc);
                _rcStatus = $"계약 {_rc.experiment_id}: {(v.Ok ? "유효" : "오류 " + v.errors.Count)}, 경고 {v.warnings.Count}, 사람 결정 대기 {_rc.pending_human_decisions.Count}";
                Log("research: contract " + path + " sha " + ContractIO.Hash(_rc).Substring(0, 12) + " | " + v.ToString().Replace("\n", " "));
                return v.Ok;
            }
            catch (Exception ex) { _rcStatus = "계약 오류: " + ex.Message; Log("ERROR research contract: " + ex.Message); return false; }
        }

        bool RcSave(bool overwrite)
        {
            try { ContractIO.Save(_rc, RcFull(_rcPath), overwrite); Log("research: contract saved " + _rcPath + " sha " + ContractIO.Hash(_rc).Substring(0, 12)); return true; }
            catch (Exception ex) { Log("ERROR saving contract: " + ex.Message); return false; }
        }

        /// <summary>Loads the real manifests named in the contract, assigns roles, checks leakage, builds nested subsets.</summary>
        bool RcCheckData(string reportOut)
        {
            try
            {
                _rcData = LoadedRealData.Load(_rc.data.real_manifest_paths);
                _rc.data.real_manifest_sha256 = _rcData.sha256.ToList();
                _rcAssign = SplitPlanner.Assign(_rcData.items, _rc);
                _rcSplit = SplitPlanner.Check(_rcData.items, _rcAssign, _rc);
                _rcSplit.contract_sha256 = ContractIO.Hash(_rc);
                _rcSplit.manifest_sha256 = _rcData.sha256.ToList();
                foreach (var seed in _rc.data.subset_seeds)
                    _rcSplit.subsets.AddRange(SubsetBuilder.Build(_rcData.items, _rcAssign, _rc.data.fold, _rc.data.fractions, seed));
                if (!string.IsNullOrEmpty(reportOut))
                {
                    var full = RcFull(reportOut);
                    Directory.CreateDirectory(Path.GetDirectoryName(full));
                    if (File.Exists(full)) throw new IOException("refusing to overwrite " + reportOut);
                    File.WriteAllText(full, JsonUtility.ToJson(_rcSplit, true), new UTF8Encoding(false));
                    var plan = TrainingPlanner.Build(_rc, _rcSplit.subsets);
                    var planPath = Path.Combine(Path.GetDirectoryName(full), Path.GetFileNameWithoutExtension(full) + "_training_plan.json");
                    if (File.Exists(planPath)) throw new IOException("refusing to overwrite " + planPath);
                    File.WriteAllText(planPath, JsonUtility.ToJson(plan, true), new UTF8Encoding(false));
                    Log($"research: training plan (NOT run) {plan.runs.Count} runs -> {planPath}");
                }
                Log($"research: data check {(_rcSplit.ok ? "OK" : "FAILED")} — " + string.Join(", ", _rcSplit.roles.Select(r => $"{r.role} {r.items}")) +
                    $"; errors {_rcSplit.errors.Count}, warnings {_rcSplit.warnings.Count}, pending {_rcSplit.pending.Count}; subsets {_rcSplit.subsets.Count}");
                foreach (var e in _rcSplit.errors.Take(5)) Log("  leakage/error: " + e);
                return _rcSplit.ok;
            }
            catch (Exception ex) { Log("ERROR data check: " + ex.Message); _rcSplit = null; return false; }
        }

        bool RcProfile(string subsetId, string outPath)
        {
            try
            {
                if (subsetId == "fixture") _rcProfile = ProfileBuilder.Fixture();
                else
                {
                    if (_rcSplit == null && !RcCheckData(null)) throw new InvalidOperationException("data check failed");
                    var sub = _rcSplit.subsets.FirstOrDefault(s => s.subset_id == subsetId) ?? throw new ArgumentException("unknown subset " + subsetId);
                    _rcProfile = ProfileBuilder.FromSubset(_rcData.items, _rcAssign, sub, _rcData.sha256);
                }
                if (!string.IsNullOrEmpty(outPath))
                {
                    var full = RcFull(outPath);
                    if (File.Exists(full)) throw new IOException("refusing to overwrite " + outPath);
                    Directory.CreateDirectory(Path.GetDirectoryName(full));
                    File.WriteAllText(full, ProfileBuilder.ToJson(_rcProfile), new UTF8Encoding(false));
                    _rcProfilePath = outPath;
                }
                Log($"research: profile {_rcProfile.profile_id} ({_rcProfile.profile_kind}) frames {_rcProfile.n_frames} boxes {_rcProfile.n_boxes}" + (outPath != null ? " -> " + outPath : ""));
                return true;
            }
            catch (Exception ex) { Log("ERROR profile: " + ex.Message); return false; }
        }

        bool RcLoadProfileFromContract()
        {
            if (_rcProfile != null) return true;
            var p = _rc.generation.c_profile_path;
            if (string.IsNullOrEmpty(p)) return !_rc.generation.modes.Contains(Samplers.C);
            try
            {
                _rcProfile = ProfileBuilder.Load(RcFull(p)); _rcProfilePath = p;
                var sha = ScenarioIO.Sha256Hex(ProfileBuilder.ToJson(_rcProfile));
                if (!string.IsNullOrEmpty(_rc.generation.c_profile_sha256) && ContractIO.FileSha(RcFull(p)) != _rc.generation.c_profile_sha256)
                    throw new InvalidDataException("profile file hash differs from contract.generation.c_profile_sha256");
                Log($"research: profile loaded {_rcProfile.profile_id} ({_rcProfile.profile_kind})");
                return true;
            }
            catch (Exception ex) { Log("ERROR profile load: " + ex.Message); return false; }
        }

        IEnumerator RcPlan(string jobsDir, Action<bool> done)
        {
            if (!RcLoadProfileFromContract()) { done?.Invoke(false); yield break; }
            _busy = true;
            string jf = null; PlanReport rep = null;
            yield return JobFreezer.PlanAndFreeze(_world, _rc, RcFull(_rcPath), _rcProfile, RcFull(jobsDir), Log, (f, r) => { jf = f; rep = r; });
            _busy = false;
            _rcJobsFile = jf ?? ""; _rcPlan = rep;
            ApplyScenario();
            done?.Invoke(jf != null);
        }

        string RcCapturesDir(string jobsFile)
        {
            var b = BatchRunner.LoadBatch(jobsFile);
            return AppPaths.ResolveUnderRoot(b.output_dir);
        }

        bool RcReport(string jobsFile, string exportDir, string qaDir)
        {
            try
            {
                var batch = BatchRunner.LoadBatch(jobsFile);
                var cap = AppPaths.ResolveUnderRoot(batch.output_dir);
                var jobsDir = Path.GetDirectoryName(jobsFile);
                if (_rc == null && File.Exists(Path.Combine(jobsDir, "contract.json"))) _rc = ContractIO.Load(Path.Combine(jobsDir, "contract.json"));
                var prof = File.Exists(Path.Combine(jobsDir, "profile.json")) ? ProfileBuilder.Load(Path.Combine(jobsDir, "profile.json")) : _rcProfile;
                _rcCoverage = ResearchReports.Coverage(batch, jobsFile, cap, prof);
                var stamp = DateTime.UtcNow.ToString("yyyyMMddTHHmmss");
                var repDir = Path.Combine(jobsDir, "reports");
                Directory.CreateDirectory(repDir);
                File.WriteAllText(Path.Combine(repDir, $"coverage_{stamp}.json"), JsonUtility.ToJson(_rcCoverage, true), new UTF8Encoding(false));
                File.WriteAllText(Path.Combine(repDir, $"coverage_{stamp}.md"), ResearchReports.CoverageMarkdown(_rcCoverage), new UTF8Encoding(false));
                Log("research: coverage -> " + Path.Combine(repDir, $"coverage_{stamp}.md"));
                if (!string.IsNullOrEmpty(exportDir))
                {
                    if (_rc == null) throw new InvalidOperationException("no contract for the export");
                    _rcExport = ResearchReports.ExportProvisional(batch, jobsFile, cap, _rc, exportDir, Log);
                    var views = ResearchReports.Load(batch, cap, out _);
                    ResearchReports.WriteQaPackage(_rcExport, views, string.IsNullOrEmpty(qaDir) ? Path.Combine(exportDir, "qa") : qaDir, 3, Log);
                }
                return true;
            }
            catch (Exception ex) { Log("ERROR research report: " + ex.Message); Debug.LogException(ex); return false; }
        }

        [Serializable] class FreezeHarbor { public string harbor_id, sha256; }
        [Serializable] class FreezeCam { public string harbor_id, preset_id; public Vector3 position_world, euler_deg; public float vertical_fov_deg; }
        [Serializable] class FreezeFile { public string freeze_id, git_commit; public List<FreezeHarbor> harbor_definition_sha256 = new List<FreezeHarbor>(); public List<FreezeCam> camera_presets = new List<FreezeCam>(); }
        [Serializable] class FreezeRuntimeCheck
        {
            public string check_schema = "portcctv.research.freezecheck.runtime.v1";
            public string freeze_id, checked_utc, build_content_sha256;
            public List<string> results = new List<string>();
            public int maps_checked, maps_changed, cameras_checked, cameras_changed;
            public bool runtime_unchanged;
        }

        /// <summary>Builds every frozen map in this build and compares the harbour definition hash and every preset camera pose with
        /// the freeze manifest (values recorded from the baseline build's captures).</summary>
        IEnumerator RcFreezeCheck(string freezePath, string outPath, Action<bool> done)
        {
            FreezeFile fz;
            try { fz = JsonUtility.FromJson<FreezeFile>(File.ReadAllText(RcFull(freezePath), Encoding.UTF8)); }
            catch (Exception ex) { Log("ERROR freeze file: " + ex.Message); done?.Invoke(false); yield break; }
            var rep = new FreezeRuntimeCheck { freeze_id = fz.freeze_id, checked_utc = DateTime.UtcNow.ToString("o"), build_content_sha256 = BuildManifestInfo.Record().content_sha256 };
            foreach (var h in fz.harbor_definition_sha256)
            {
                var cfg = DefaultScenarioFor(h.harbor_id);
                _world.Apply(cfg);
                yield return null;
                var now = _world.Harbor.ComputeHash();
                rep.maps_checked++;
                bool same = now == h.sha256;
                if (!same) rep.maps_changed++;
                rep.results.Add($"{h.harbor_id}: harbor definition {(same ? "UNCHANGED" : "CHANGED")} ({now.Substring(0, 16)} vs {h.sha256.Substring(0, 16)})");
                foreach (var c in fz.camera_presets.Where(c => c.harbor_id == h.harbor_id))
                {
                    var s = cfg.Clone(); s.camera_preset_id = c.preset_id;
                    _world.ApplyCamera(s);
                    var t = _world.CctvCamera.transform;
                    float dp = Vector3.Distance(t.position, c.position_world);
                    float de = Vector3.Distance(t.eulerAngles, c.euler_deg);
                    if (de > 180f) de = Mathf.Abs(Mathf.DeltaAngle(t.eulerAngles.x, c.euler_deg.x)) + Mathf.Abs(Mathf.DeltaAngle(t.eulerAngles.y, c.euler_deg.y)) + Mathf.Abs(Mathf.DeltaAngle(t.eulerAngles.z, c.euler_deg.z));
                    float df = Mathf.Abs(_world.CctvCamera.fieldOfView - c.vertical_fov_deg);
                    bool ok = dp < 1e-3f && de < 1e-2f && df < 1e-3f;
                    rep.cameras_checked++; if (!ok) rep.cameras_changed++;
                    rep.results.Add($"  camera {c.preset_id}: {(ok ? "UNCHANGED" : "CHANGED")} (dpos {dp:E1} m, deuler {de:E1} deg, dfov {df:E1})");
                }
            }
            rep.runtime_unchanged = rep.maps_changed == 0 && rep.cameras_changed == 0;
            foreach (var r in rep.results) Log("freeze: " + r);
            Log($"freeze runtime check: maps {rep.maps_checked} changed {rep.maps_changed}, cameras {rep.cameras_checked} changed {rep.cameras_changed} -> {(rep.runtime_unchanged ? "UNCHANGED" : "CHANGED")}");
            if (!string.IsNullOrEmpty(outPath))
            {
                var full = RcFull(outPath); Directory.CreateDirectory(Path.GetDirectoryName(full));
                File.WriteAllText(full, JsonUtility.ToJson(rep, true), new UTF8Encoding(false));
            }
            ApplyScenario();
            done?.Invoke(rep.runtime_unchanged);
        }

        // ------------------------------------------------------------------ command line
        IEnumerator RunResearchCommandLine(Action<bool> setFailed)
        {
            bool failed = false;
            var stopAfter = ArgValue("-batch-stop-after");
            if (stopAfter != null) { BatchRunner.StopAfterRendered = int.Parse(stopAfter, CultureInfo.InvariantCulture); Log("batch will stop after " + stopAfter + " renders (interruption test)"); }
            var fz = ArgValue("-research-freeze-check");
            if (fz != null)
            {
                bool ok = false;
                yield return RcFreezeCheck(fz, ArgValue("-freeze-check-out"), r => ok = r);
                failed |= !ok;
            }
            var nc = ArgValue("-research-new-contract");
            if (nc != null)
            {
                _rc = ContractTemplates.Pilot(ArgValue("-experiment-id") ?? "pilot_" + DateTime.UtcNow.ToString("yyyyMMdd"));
                _rcPath = nc;
                failed |= !RcSave(false);
            }
            var cp = ArgValue("-research-contract");
            if (cp != null) failed |= !RcLoad(cp);
            if (HasArg("-research-check-data") && _rc != null) failed |= !RcCheckData(ArgValue("-research-check-data-out"));
            var po = ArgValue("-research-profile-out");
            if (po != null && _rc != null) failed |= !RcProfile(ArgValue("-research-profile-subset") ?? "fixture", po);
            var plan = ArgValue("-research-plan");
            if (plan != null && _rc != null && !failed)
            {
                bool ok = false;
                yield return RcPlan(plan, r => ok = r);
                failed |= !ok;
            }
            setFailed(failed);
        }

        /// <summary>Runs after -batch: coverage / export / QA and the release gate.</summary>
        bool RunResearchPostBatch()
        {
            bool failed = false;
            var rep = ArgValue("-research-report");
            if (rep != null) failed |= !RcReport(RcFull(rep), ArgValue("-research-export") is string ex ? RcFull(ex) : null, ArgValue("-research-qa") is string q ? RcFull(q) : null);
            var rel = ArgValue("-research-release");
            if (rel != null)
            {
                _rcRelease = ResearchReports.TrainingRelease(RcFull(rel), ArgValue("-review-records") is string rr ? RcFull(rr) : null, false, Path.Combine(RcFull(rel), "release"));
                Log($"training release: {(_rcRelease.released ? "RELEASED" : "REFUSED")} — {_rcRelease.reason}; approved {_rcRelease.approved}, missing {_rcRelease.missing}, rejected {_rcRelease.rejected}, policy mismatch {_rcRelease.policy_mismatch}");
            }
            return failed;
        }

        // ------------------------------------------------------------------ UI
        bool _rfData = true, _rfCond, _rfPlan = true, _rfRun = true, _rfOut = true;

        void TabResearch()
        {
            GUILayout.Label("연구 실험: 계약 → 분할 검사 → 프로필 → 조건 → 미리보기 → job 동결 → 배치 → coverage·QA → 잠정 export", _small);
            var badge = new GUIStyle(_small) { normal = { textColor = new Color(1f, 0.8f, 0.4f) } };
            GUILayout.Label("라벨: provisional_diagnostic · training_ready=false · 사람 검수 미실시 (도구가 해제하지 않음)", badge);

            Header("1. 실험 계약");
            GUILayout.BeginHorizontal();
            _rcPath = GUILayout.TextField(_rcPath, GUILayout.Width(ContentWidth - 128));
            if (GUILayout.Button("불러오기", GUILayout.Width(66))) RcLoad(_rcPath);
            if (GUILayout.Button("예시", GUILayout.Width(50)))
            {
                _rc = ContractTemplates.Pilot("pilot_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmm"));
                _rcStatus = "예시 계약 (저장 전)"; Log("research: new pilot contract (not saved)");
            }
            GUILayout.EndHorizontal();
            if (_rc == null) { GUILayout.Label("계약이 없습니다. 불러오거나 예시를 만드세요.", _warn); return; }
            GUILayout.Label(_rcStatus, _small);
            GUILayout.BeginHorizontal();
            GUILayout.Label("실험 ID", GUILayout.Width(70));
            var eid = GUILayout.TextField(_rc.experiment_id);
            if (eid.Length > 0) _rc.experiment_id = ScenarioIO.SanitizeId(eid); else _rc.experiment_id = "";
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label("프로토콜", GUILayout.Width(70));
            int pi = Array.IndexOf(Protocols.All, _rc.protocol.protocol_id);
            int npi = GUILayout.Toolbar(Mathf.Max(0, pi), new[] { "적응(본 카메라)", "미관측 일반화" }, GUILayout.Width(ContentWidth - 78));
            if (npi != pi) _rc.protocol.protocol_id = Protocols.All[npi];
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label($"fold {_rc.data.fold}", GUILayout.Width(70));
            int nf = GUILayout.Toolbar(_rc.data.fold - 1, new[] { "1", "2", "3", "4" }, GUILayout.Width(ContentWidth - 78));
            if (nf + 1 != _rc.data.fold) { _rc.data.fold = nf + 1; _rcSplit = null; }
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label("생성 모드", GUILayout.Width(70));
            foreach (var m in Samplers.Modes)
            {
                bool on = _rc.generation.modes.Contains(m), non = GUILayout.Toggle(on, m == "C_U" ? "C_U 희소균등" : m, GUILayout.Width(m == "C_U" ? 110 : 60));
                if (non != on) { if (non) _rc.generation.modes.Add(m); else _rc.generation.modes.Remove(m); }
            }
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label("seed", GUILayout.Width(70)); _rcSeeds = GUILayout.TextField(_rcSeeds, GUILayout.Width(80));
            GUILayout.Label("재시도", GUILayout.Width(44)); _rc.generation.retry_limit = IntField(_rc.generation.retry_limit, 1, 50);
            GUILayout.Label("선박수", GUILayout.Width(44)); _rc.generation.max_vessels = IntField(_rc.generation.max_vessels, 1, 60);
            GUILayout.EndHorizontal();
            try { _rc.generation.seeds = _rcSeeds.Split(',').Where(s => s.Trim().Length > 0).Select(s => int.Parse(s.Trim(), CultureInfo.InvariantCulture)).ToList(); } catch { /* keep previous */ }
            int cells = _rc.generation.maps.Sum(m => m.camera_preset_ids.Count) * _rc.generation.modes.Count * _rc.generation.seeds.Count * _rc.generation.captures_per_cell;
            GUILayout.Label("맵·카메라: " + string.Join(" / ", _rc.generation.maps.Select(m => m.harbor_id + ": " + string.Join(", ", m.camera_preset_ids))), _small);
            GUILayout.Label($"계획 캡처 {cells}개 (상한 {_rc.generation.render_budget.max_captures_this_run}), {_rc.generation.width}×{_rc.generation.height}, G/C 동일 해상도·재시도 상한", cells > _rc.generation.render_budget.max_captures_this_run ? _warn : _small);
            GUILayout.Label($"라벨 정책: {ResearchReports.PolicyVersion(_rc.labels).Replace("+", " + ").Replace("/", " / ")}; 학습기 {_rc.labels.trainer_adapter} (ignore 미지원 → 격리)", _small);
            if (_rc.pending_human_decisions.Count > 0) GUILayout.Label($"사람 결정 대기 {_rc.pending_human_decisions.Count}건: " + string.Join(" / ", _rc.pending_human_decisions.Take(2).Select(x => x.Split(':')[0])) + " …", new GUIStyle(_small) { normal = { textColor = new Color(1f, 0.55f, 0.45f) } });
            GUILayout.BeginHorizontal(GUILayout.Width(ContentWidth));
            if (GUILayout.Button("검증")) { var v = ContractValidator.Validate(_rc); _rcStatus = v.Ok ? $"유효 (경고 {v.warnings.Count})" : "오류: " + string.Join("; ", v.errors.Take(3)); Log("research: validate " + v.ToString().Replace("\n", " ")); }
            if (GUILayout.Button("저장(새 파일)")) RcSave(false);
            if (GUILayout.Button("덮어쓰기")) RcSave(true);
            GUILayout.EndHorizontal();
            GUILayout.Label("계약 해시 " + ContractIO.Hash(_rc).Substring(0, 16) + " — job·캡처·export에 기록, 다르면 재사용하지 않음", _small);

            if (Fold(ref _rfData, "2. 실제 데이터·분할 검사 / 3. 프로필"))
            {
                if (GUILayout.Button("데이터·분할·누수 검사")) RcCheckData(null);
                if (_rcSplit != null)
                {
                    GUILayout.Label((_rcSplit.ok ? "누수 검사 통과" : "누수/오류 " + _rcSplit.errors.Count) + $", 경고 {_rcSplit.warnings.Count}", _rcSplit.ok ? _small : _warn);
                    foreach (var r in _rcSplit.roles) GUILayout.Label($"  {r.role}: {r.items}개 (클립 {r.clips}, 그룹 {r.groups}, 선박 상자 {r.vessel_boxes})", _small);
                    foreach (var p in _rcSplit.pending) GUILayout.Label("  대기: " + p, _warn);
                    var all = _rcSplit.datasets.FirstOrDefault(d => d.dataset == "ALL");
                    if (all != null) GUILayout.Label($"  전체 항목 {all.items}, 독립 단위 {all.independent_units} (중복 합침), 라벨 {all.with_labels}, 사람 검수 {all.human_reviewed}, site 미상 {all.unknown_site}", _small);
                    var seeds = _rc.data.subset_seeds;
                    _rcSubsetSeedIdx = Mathf.Clamp(_rcSubsetSeedIdx, 0, seeds.Count - 1);
                    _rcSubsetSeedIdx = GUILayout.Toolbar(_rcSubsetSeedIdx, seeds.Select(s => "seed " + s).ToArray());
                    foreach (var s in _rcSplit.subsets.Where(s => s.seed == seeds[_rcSubsetSeedIdx]))
                        GUILayout.Label($"  {s.subset_id}: {s.n_items}/{s.population_items} 프레임, 클립 {s.n_clips}, 그룹 {s.n_groups}, 상자 {s.n_vessel_boxes}  #{s.items_sha256.Substring(0, 8)}", _small);
                    GUILayout.Label("같은 fold·비율·seed의 모든 비교군은 같은 subset_id를 참조합니다 (중첩: 10⊂25⊂50⊂100%).", _small);
                }
                GUILayout.BeginHorizontal(GUILayout.Width(ContentWidth));
                if (GUILayout.Button("fixture 프로필")) RcProfile("fixture", null);
                if (_rcSplit != null && GUILayout.Button("train 부분집합")) RcProfile(SubsetBuilder.SubsetId(_rc.data.fold, 1f, _rc.data.subset_seeds[_rcSubsetSeedIdx]), null);
                if (GUILayout.Button("계약 프로필")) { _rcProfile = null; RcLoadProfileFromContract(); }
                GUILayout.EndHorizontal();
                if (_rcProfile != null)
                    GUILayout.Label((_rcProfile.profile_kind == "fixture_not_real_data" ? "프로필: FIXTURE (실제 통계 아님)" : $"프로필: 실제 train 부분집합 {_rcProfile.subset_id} (프레임 {_rcProfile.n_frames}, 상자 {_rcProfile.n_boxes}; 라벨 사람 검수 전)"),
                        _rcProfile.profile_kind == "fixture_not_real_data" ? _warn : _small);
            }

            if (Fold(ref _rfCond, "4. 조건 (요인별 고정; 비우면 샘플러가 결정)"))
            {
                var f = _rc.generation.factors;
                GUILayout.Label("조명", _small);
                ToggleList(f.lighting, Samplers.Lighting, 3);
                GUILayout.Label("CCTV 화질", _small);
                ToggleList(f.sensor, SensorConfig.PresetIds, 3);
                GUILayout.Label("수면", _small);
                ToggleList(f.water, Samplers.Water, 3);
                GUILayout.Label("가림 / 화면 잘림", _small);
                ToggleList(f.occlusion, new[] { "none", "partial" }, 3);
                ToggleList(f.truncation, new[] { "none", "left", "right" }, 3);
                GUILayout.BeginHorizontal();
                GUILayout.Label("선박 수", GUILayout.Width(50)); f.count_min = IntField(f.count_min, -1, 60); f.count_max = IntField(f.count_max, -1, 60);
                GUILayout.EndHorizontal();
                GUILayout.Label("G 사전분포: " + Samplers.GPriorDescription, _small);
                GUILayout.Label("C: 화면 크기·위치·밀도·잘림을 프로필에서 추출, 환경은 G와 같은 사전분포(실제 환경 라벨 없음). C_U: 희소 조건 균등(별도 절제).", _small);
            }

            if (Fold(ref _rfPlan, "5. 미리보기 / 6. job 동결"))
            {
                if (GUILayout.Button("현재 맵·카메라로 1개 계획 (미리보기)"))
                {
                    if (RcLoadProfileFromContract())
                    {
                        var mode = _rc.generation.modes.FirstOrDefault() ?? Samplers.G;
                        var job = new ResearchJobInfo { experiment_id = _rc.experiment_id, contract_sha256 = "preview", mode = mode, sampler_version = Samplers.VersionOf(mode), scene_kind = "planned",
                            harbor_id = _cfg.harbor_id, camera_preset_id = _cfg.camera_preset_id, generation_seed = UnityEngine.Random.Range(0, 100000), retry_limit = _rc.generation.retry_limit, size_tolerance_rel = _rc.generation.size_tolerance_rel };
                        try
                        {
                            ConditionSampler.Sample(mode, mode == Samplers.C ? _rcProfile : null, _rc.generation, job);
                            var cfg = ScenePlanner.Plan(_world, job, _rc.generation);
                            _rcPreview = job;
                            if (cfg != null) { _cfg = cfg; _cfg.scenario_id = "research_preview"; ApplyScenario(); }
                            Log($"research preview {mode}: {job.plan_status}, {job.objects.Count} objects, {job.planning_attempts} attempts" + (job.plan_failure_reasons.Count > 0 ? " — " + string.Join(" | ", job.plan_failure_reasons) : ""));
                        }
                        catch (Exception ex) { Log("ERROR preview: " + ex.Message); }
                    }
                }
                if (_rcPreview != null)
                {
                    GUILayout.Label($"{_rcPreview.mode} {_rcPreview.plan_status}: 조명 {_rcPreview.environment.lighting}, 수면 {_rcPreview.environment.water}, 시정×{_rcPreview.environment.fog_visibility_scale}, 화질 {_rcPreview.environment.sensor}", _small);
                    foreach (var o in _rcPreview.objects)
                        GUILayout.Label($"  {o.object_id} {o.role} {o.asset_id}: 요청 h {o.target_height_frac:F3} u {o.target_center_u:F2} v {o.target_bottom_v:F2} {o.occlusion_target}/{o.truncation_target} → 예측 h {o.predicted_height_frac:F3} ({o.plan_status}{(o.unmet_reason != "" ? ": " + o.unmet_reason : "")})", _small);
                }
                GUILayout.BeginHorizontal();
                GUILayout.Label("job 폴더", GUILayout.Width(56));
                _rcJobsName = GUILayout.TextField(_rcJobsName);   // sanitised when the folder is created (an empty field must stay empty while typing)
                GUILayout.EndHorizontal();
                var jobsDir = "Research/jobs/" + _rc.experiment_id + "/" + ScenarioIO.SanitizeId(_rcJobsName);
                if (GUILayout.Button("계획 후 job 목록 동결 → " + jobsDir)) StartCoroutine(RcPlan(jobsDir, ok => { }));
                if (_rcPlan != null)
                    GUILayout.Label($"동결: job {_rcPlan.jobs_total} (렌더 {_rcPlan.render_jobs}, 계획 실패 {_rcPlan.jobs_planning_failed}), 목표 객체 {_rcPlan.objects_requested} (미충족 {_rcPlan.objects_unmet}), 예상 {_rcPlan.estimated_mb:F0} MB / {_rcPlan.estimated_minutes:F1}분",
                        _rcPlan.render_jobs > _rcPlan.render_cap ? _warn : _small);
                if (!string.IsNullOrEmpty(_rcJobsFile)) GUILayout.Label("jobs: " + _rcJobsFile.Replace('\\', '/'), _small);
            }

            if (Fold(ref _rfRun, "7. 배치 실행 / 중단 / 재개"))
            {
                GUILayout.BeginHorizontal();
                _rcJobsFile = GUILayout.TextField(_rcJobsFile ?? "", GUILayout.Width(ContentWidth - 4));
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("실행 / 재개") && File.Exists(RcFull(_rcJobsFile))) StartCoroutine(RunBatchUI(RcFull(_rcJobsFile)));
                bool wasEnabled = GUI.enabled; GUI.enabled = true;
                if (GUILayout.Button(BatchRunner.CancelRequested ? "중단 요청됨" : "중단")) { BatchRunner.CancelRequested = true; Log("research: stop requested (after the current job)"); }
                GUI.enabled = wasEnabled;
                GUILayout.EndHorizontal();
                GUILayout.Label("재개 = 같은 jobs 파일 재실행. 계약·scenario·빌드 해시가 다른 기존 결과는 hash_mismatch(건너뛰지 않음), 실패 폴더는 보존, 재시도 상한 2회.", _small);
            }

            if (Fold(ref _rfOut, "8. coverage·QA / 9. 잠정 export / 학습용 release"))
            {
                if (GUILayout.Button("coverage 보고서 + 잠정 export + QA 표본") && File.Exists(RcFull(_rcJobsFile)))
                {
                    var exp = Path.Combine(Path.GetDirectoryName(RcFull(_rcJobsFile)), "export_" + DateTime.UtcNow.ToString("yyyyMMddTHHmmss"));
                    RcReport(RcFull(_rcJobsFile), exp, null);
                }
                if (_rcCoverage != null)
                {
                    GUILayout.Label("생성 통계이며 탐지 성능이 아닙니다.", _warn);
                    foreach (var m in _rcCoverage.modes)
                    {
                        GUILayout.Label($"{m.mode}: job {m.jobs}, 렌더 {m.rendered}, 계획 실패 {m.planning_failed}, 렌더 실패 {m.render_failed}; 목표 {m.targets_rendered} 크기 충족 {m.size_met}/미달 {m.size_missed}, 가림 충족 {m.occlusion_met}/미달 {m.occlusion_missed}; 양성 {m.positive_frames}/음성 {m.negative_frames}; {m.capture_ms_total / 1000:F0}s, {m.disk_mb:F0}MB", _small);
                        var h = m.histograms.FirstOrDefault(x => x.name == "visible_height_px_at_1080");
                        if (h != null) DrawHist(h);
                    }
                    foreach (var d in _rcCoverage.distances.Take(3)) GUILayout.Label($"  {d.name}: {d.count / 1000f:F3}", _small);
                }
                if (_rcExport != null)
                    GUILayout.Label($"잠정 export: 포함 {_rcExport.frames_included}, 격리 {_rcExport.frames_quarantined}, 제외 {_rcExport.frames_excluded} (release_status {_rcExport.release_status})", _small);
                GUILayout.BeginHorizontal();
                GUILayout.Label("검수 기록", GUILayout.Width(56)); _rcReviewPath = GUILayout.TextField(_rcReviewPath);
                GUILayout.EndHorizontal();
                if (GUILayout.Button("학습용 release 확인 (사람 검수 기록 필요)") && _rcExport != null)
                {
                    var expDir = Path.GetDirectoryName(_rcExport.frames.FirstOrDefault(f => !string.IsNullOrEmpty(f.image_path))?.source_dir ?? "");
                    var last = Directory.GetDirectories(Path.GetDirectoryName(RcFull(_rcJobsFile)), "export_*").OrderBy(d => d).LastOrDefault();
                    if (last != null)
                    {
                        _rcRelease = ResearchReports.TrainingRelease(last, RcFull(_rcReviewPath), false, Path.Combine(last, "release"));
                        Log($"training release: {(_rcRelease.released ? "RELEASED" : "REFUSED")} — {_rcRelease.reason}");
                    }
                }
                if (_rcRelease != null)
                    GUILayout.Label((_rcRelease.released ? "release 가능: " : "release 거부: ") + _rcRelease.reason + $" (누락 {_rcRelease.missing}, 미승인 {_rcRelease.rejected})", _rcRelease.released ? _small : _warn);
            }
        }

        void DrawHist(CompareHist h)
        {
            int max = Mathf.Max(1, Mathf.Max(h.requested.DefaultIfEmpty(0).Max(), h.achieved.DefaultIfEmpty(0).Max()));
            for (int i = 0; i < h.bins.Count; i++)
            {
                var r = GUILayoutUtility.GetRect(ContentWidth, 14);
                GUI.Label(new Rect(r.x, r.y - 2, 70, 16), h.bins[i], _small);
                float w = r.width - 110;
                var old = GUI.color;
                GUI.color = new Color(0.4f, 0.6f, 1f, 0.8f); GUI.DrawTexture(new Rect(r.x + 72, r.y + 1, w * h.requested[i] / max, 5), Texture2D.whiteTexture);
                GUI.color = new Color(1f, 0.7f, 0.3f, 0.9f); GUI.DrawTexture(new Rect(r.x + 72, r.y + 7, w * h.achieved[i] / max, 5), Texture2D.whiteTexture);
                GUI.color = old;
                GUI.Label(new Rect(r.xMax - 36, r.y - 2, 36, 16), $"{h.requested[i]}/{h.achieved[i]}", _small);
            }
            GUILayout.Label("파랑 = 요청, 주황 = 렌더 달성 (가시 마스크 높이, px@1080)", _small);
        }

        void ToggleList(List<string> list, IList<string> options, int perRow)
        {
            for (int i = 0; i < options.Count; i += perRow)
            {
                GUILayout.BeginHorizontal();
                for (int k = i; k < Mathf.Min(options.Count, i + perRow); k++)
                {
                    bool on = list.Contains(options[k]), non = GUILayout.Toggle(on, options[k], GUILayout.Width(ContentWidth / perRow - 4));
                    if (non != on) { if (non) list.Add(options[k]); else list.Remove(options[k]); }
                }
                GUILayout.EndHorizontal();
            }
        }

        static int IntField(int v, int min, int max)
        {
            var s = GUILayout.TextField(v.ToString(CultureInfo.InvariantCulture), GUILayout.Width(36));
            return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? Mathf.Clamp(n, min, max) : v;
        }
    }
}
