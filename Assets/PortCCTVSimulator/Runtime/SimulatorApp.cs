using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace PortCCTV
{
    /// <summary>
    /// Entry point (only component in the main scene). Interactive IMGUI app, plus command-line modes:
    ///   -scenario <file.json>          load a scenario at start
    ///   -capture <outDir>              capture the loaded scenario into outDir (refuses to overwrite)
    ///   -batch <jobs.json>             run a batch (resume: finished jobs are skipped)
    ///   -write-regression <dir> [-regression-out <dir>]   write the regression suite jobs + scenarios
    ///   -write-mapsuite <dir> [-mapsuite-out <dir>]         write the per-map suite (3 themed maps)
    ///   -ui-screenshot <file.png>      save a screenshot of the UI after a few frames
    ///   -ui-add-vessels <ids>         QA: add comma-separated vessels through the UI placement path
    ///   -ui-dev-view                   QA: turn the dev free view on (before -ui-screenshot)
    ///   -ui-panel-scroll <px>          QA: scroll the control panel before -ui-screenshot
    ///   -ui-tab <name|0-4>             QA: open a panel tab (장면, 날씨, 선박, 항해, 캡처) before -ui-screenshot
    ///   -ui-open-folds                 QA: expand every folded section
    ///   -ui-dev-fly "w=2;look=40,-8;e=1;wheel=2"   QA: feed scripted fly-camera input (held seconds / degrees / notches)
    ///   -auto-navigate                 set motion "auto" (4-waypoint loop) on every vessel that is not alongside a quay
    ///   -free-roam                     set motion "roam" (free wandering over the whole water area) on those vessels
    ///   -sim-time <s>                  navigation time of the loaded scenario (vessel poses at that time)
    ///   -write-sequence <dir> [-frames N] [-interval s] [-sequence-out <dir>]   write a time sequence (scenario files + jobs.json)
    ///   -quit                          exit after the command(s) (exit code 0 ok, 1 failure)
    /// </summary>
    public class SimulatorApp : MonoBehaviour
    {
        SimWorld _world;
        ScenarioConfig _cfg;
        List<PlacementReport> _reports = new List<PlacementReport>();
        RenderTexture _preview;
        readonly List<string> _log = new List<string>();
        Vector2 _panelScroll, _logScroll;
        bool _overlay = true, _devView, _busy, _dirty;
        string _scenarioFile = "";
        string _status = "";
        string _lastOutput = "";
        string[] _scenarioFiles = Array.Empty<string>();
        int _selectedVessel = -1;
        readonly HashSet<string> _navigationSelection = new HashSet<string>();
        readonly DevFlyCam _fly = new DevFlyCam();
        bool _flyInit, _mouseLook, _dragLook;
        Rect _viewRect;
        float _lookSens = 2f;              // degrees per Input Manager mouse-axis unit (axis = pixels x 0.1)
        bool _hasError;
        string _fatal;
        readonly System.Random _spawnRandom = new System.Random();
        bool _placeOnPreview;
        VesselAssetEntry _placementEntry;
        bool _playing;
        float _playT, _playRate = 1f;
        int _seqFrames = 30;
        float _seqInterval = 1f;

        // ---------------- lifecycle ----------------
        void Start()
        {
            Application.targetFrameRate = 60;
            try
            {
                _world = new SimWorld();
                _world.Initialize();
                _cfg = DefaultScenarioFor(HarborLibrary.DefaultHarborId);
                _cfg.appearance.paint = VesselPaint.Random;   // the app starts with varied hull colours; scenario files default to original
                ApplyScenario();
                RefreshScenarioList();
                Log($"PortCCTVSimulator {CaptureService.AppVersion} | Unity {Application.unityVersion} | {SystemInfo.graphicsDeviceName} ({SystemInfo.graphicsDeviceType})");
                Log(UiText.KoreanFont() != null ? "UI font: " + UiText.FontName : "UI font: no Korean OS font found (Hangul may render as boxes)");
                Log("App root: " + AppPaths.Root);
            }
            catch (Exception ex)
            {
                _fatal = ex.ToString();
                Debug.LogError("[PortCCTV] init failed: " + ex);
                if (HasArg("-quit")) Quit(1);
                return;
            }
            StartCoroutine(RunCommandLine());
        }

        void OnDestroy()
        {
            if (_preview != null) { _preview.Release(); Destroy(_preview); }
        }

        /// <summary>Plausible starting scene for each map.</summary>
        public static ScenarioConfig DefaultScenarioFor(string harborId) => harborId switch
        {
            ContainerTerminal01.Id => ContainerTerminal01.DefaultScenario(),
            FishingPort01.Id => FishingPort01.DefaultScenario(),
            Marina01.Id => Marina01.DefaultScenario(),
            _ => DefaultScenario(),
        };

        /// <summary>Legacy mixed-harbor scene (generic_harbor_02) used by the tests.</summary>
        public static ScenarioConfig DefaultScenario()
        {
            var s = new ScenarioConfig { scenario_id = "default_three_vessels", harbor_id = HarborGeneric02.Id };
            s.vessels.Add(new VesselPlacement { object_id = "v00", asset_id = "ferry", x = 10, z = -20, heading_deg = 90, immersion_m = 0.4f });
            s.vessels.Add(new VesselPlacement { object_id = "v01", asset_id = "rib", x = -20, z = -60, heading_deg = 30, immersion_m = 0.1f });
            s.vessels.Add(new VesselPlacement { object_id = "v02", asset_id = "motorboat", x = 40, z = -120, heading_deg = 120, immersion_m = 0.1f });
            return s;
        }

        // ---------------- command line ----------------
        static string[] Args => Environment.GetCommandLineArgs();
        static bool HasArg(string a) => Args.Any(x => string.Equals(x, a, StringComparison.OrdinalIgnoreCase));
        static string ArgValue(string a)
        {
            var args = Args;
            for (int i = 0; i < args.Length - 1; i++)
                if (string.Equals(args[i], a, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
            return null;
        }

        IEnumerator RunCommandLine()
        {
            bool failed = false;
            yield return null;
            yield return null;
            var regDir = ArgValue("-write-regression");
            if (regDir != null)
            {
                try
                {
                    var jobs = RegressionSuite.Write(AppPaths.ResolveUnderRoot(regDir), ArgValue("-regression-out") ?? "Captures/regression");
                    Log("Regression suite written: " + jobs);
                }
                catch (Exception ex) { Log("ERROR writing regression suite: " + ex.Message); failed = true; }
            }
            var msDir = ArgValue("-write-mapsuite");
            if (msDir != null)
            {
                try
                {
                    var jobs = MapSuite.Write(AppPaths.ResolveUnderRoot(msDir), ArgValue("-mapsuite-out") ?? "Captures/mapsuite");
                    Log("Map suite written: " + jobs);
                }
                catch (Exception ex) { Log("ERROR writing map suite: " + ex.Message); failed = true; }
            }
            var sc = ArgValue("-scenario");
            if (sc != null)
            {
                try { LoadScenarioFile(AppPaths.ResolveUnderRoot(sc)); }
                catch (Exception ex) { Log("ERROR loading scenario: " + ex.Message); failed = true; }
            }
            var uiAdds = ArgValue("-ui-add-vessels");
            if (uiAdds != null && !failed)
            {
                foreach (var id in uiAdds.Split(','))
                {
                    var entry = _world.Catalog.Find(id.Trim());
                    if (entry == null) { Log("ERROR unknown UI vessel: " + id); failed = true; break; }
                    if (!AddVessel(entry)) { failed = true; break; }
                    yield return null;
                }
            }
            if ((HasArg("-auto-navigate") || HasArg("-free-roam")) && !failed)
            {
                string mode = HasArg("-free-roam") ? "roam" : "auto";
                int n = _world.SetAutoNavigationForUnmoored(_cfg, mode);
                Log($"{mode} navigation on {n} vessel(s) (vessels alongside stay put)");
                ApplyScenario();
            }
            var simT = ArgValue("-sim-time");
            if (simT != null && !failed)
            {
                if (float.TryParse(simT, NumberStyles.Float, CultureInfo.InvariantCulture, out var tt)) { _cfg.navigation.sim_time_s = tt; ApplyScenario(); Log($"sim time {tt:F2} s"); }
                else { Log("ERROR bad -sim-time " + simT); failed = true; }
            }
            var seqDir = ArgValue("-write-sequence");
            if (seqDir != null && !failed)
            {
                try
                {
                    int frames = int.TryParse(ArgValue("-frames") ?? "10", out var fr) ? fr : 10;
                    float interval = float.TryParse(ArgValue("-interval") ?? "1", NumberStyles.Float, CultureInfo.InvariantCulture, out var iv) ? iv : 1f;
                    var jobs = SequenceSuite.Write(_cfg, AppPaths.ResolveUnderRoot(seqDir), ArgValue("-sequence-out") ?? "Captures/" + Path.GetFileName(seqDir), frames, interval, _cfg.navigation.sim_time_s);
                    Log("Sequence written: " + jobs);
                }
                catch (Exception ex) { Log("ERROR writing sequence: " + ex.Message); failed = true; }
            }
            var cap = ArgValue("-capture");
            if (cap != null && !failed)
            {
                for (int i = 0; i < 5; i++) yield return null;
                var res = CaptureService.Capture(_world, AppPaths.ResolveUnderRoot(cap), "cli");
                Log(res.success ? "Captured -> " + res.output_dir : "Capture FAILED: " + res.error);
                failed |= !res.success;
            }
            var batch = ArgValue("-batch");
            if (batch != null && !failed)
            {
                BatchSummary summary = null;
                _busy = true;
                yield return BatchRunner.Run(_world, AppPaths.ResolveUnderRoot(batch), Log, s => summary = s);
                _busy = false;
                failed |= summary == null || summary.unexpected > 0;
                ApplyScenario();
            }
            if (HasArg("-ui-dev-view")) { SetDevView(true); Log("QA: dev free view on"); }
            var tab = ArgValue("-ui-tab");
            if (tab != null) { int ti = Array.IndexOf(Tabs, tab); _tab = ti >= 0 ? ti : int.TryParse(tab, out var tn) ? Mathf.Clamp(tn, 0, Tabs.Length - 1) : 0; Log("QA: tab " + Tabs[_tab]); }
            if (HasArg("-ui-open-folds")) { _foldMaps = _foldDev = _foldFiles = _foldWx = _foldVessel = _foldNav = _foldCap = _foldSensor = true; Log("QA: folds open"); }
            var scroll = ArgValue("-ui-panel-scroll");
            if (scroll != null && float.TryParse(scroll, NumberStyles.Float, CultureInfo.InvariantCulture, out var sy)) _panelScroll.y = sy;
            var flyScript = ArgValue("-ui-dev-fly");
            if (flyScript != null && _devView)
            {
                try { RunFlyScript(flyScript); }
                catch (Exception ex) { Log("ERROR in -ui-dev-fly: " + ex.Message); failed = true; }
                for (int i = 0; i < 3; i++) yield return null;
            }
            var shot = ArgValue("-ui-screenshot");
            if (shot != null)
            {
                for (int i = 0; i < 30; i++) yield return null;
                yield return new WaitForEndOfFrame();
                var path = AppPaths.ResolveUnderRoot(shot);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                var t = ScreenCapture.CaptureScreenshotAsTexture();
                File.WriteAllBytes(path, t.EncodeToPNG());
                Destroy(t);
                Log("UI screenshot -> " + path);
            }
            if (HasArg("-quit")) Quit(failed || _hasError ? 1 : 0);
        }

        void Quit(int code)
        {
            Debug.Log($"[PortCCTV] exiting with code {code}");
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit(code);
#endif
        }

        // ---------------- scenario handling ----------------
        void ApplyScenario()
        {
            try
            {
                _reports = _world.Apply(_cfg);
                _navigationSelection.RemoveWhere(id => !_cfg.vessels.Any(v => v.object_id == id));
                EnsurePreview();
                _dirty = false;
                int bad = _reports.Count(r => !r.ok);
                _status = bad == 0 ? "시나리오 적용됨." : $"시나리오 적용됨 — 배치 경고 {bad}건.";
            }
            catch (Exception ex)
            {
                _status = "오류: " + ex.Message;
                Log("ERROR applying scenario: " + ex.Message);
            }
        }

        void EnsurePreview()
        {
            int pw = 1280, ph = Mathf.Max(16, Mathf.RoundToInt(1280f * _cfg.capture.height / _cfg.capture.width));
            if (_preview == null || _preview.width != pw || _preview.height != ph)
            {
                if (_preview != null) { _preview.Release(); Destroy(_preview); }
                _preview = new RenderTexture(pw, ph, 24, RenderTextureFormat.ARGB32) { name = "CCTVPreview" };
                _preview.Create();
            }
            _world.CctvCamera.targetTexture = _preview;
            _world.CctvCamera.aspect = (float)_cfg.capture.width / _cfg.capture.height;
            _world.DevCamera.targetTexture = _preview;
            _world.DevCamera.aspect = _world.CctvCamera.aspect;
            SetDevView(_devView, resetPose: false);
        }

        void SetDevView(bool on, bool resetPose = true)
        {
            _devView = on;
            _world.CctvCamera.enabled = !on;
            _world.DevCamera.enabled = on;
            if (!on) { SetMouseLook(false); _dragLook = false; return; }
            _fly.minY = _world.Harbor != null ? _world.Harbor.water_reference_y + 1.5f : float.NegativeInfinity;
            if (resetPose || !_flyInit) { FlyToCctv(30f, 25f); _flyInit = true; }
            ApplyFly();
        }

        /// <summary>Dev camera at the CCTV position raised by <paramref name="up"/> m, same heading, given pitch (null = CCTV pitch).</summary>
        void FlyToCctv(float up, float? pitch)
        {
            var c = _world.CctvCamera.transform;
            _fly.SetPose(c.position + Vector3.up * up, c.eulerAngles.y, pitch ?? c.eulerAngles.x);
        }

        void ApplyFly() => _world.DevCamera.transform.SetPositionAndRotation(_fly.position, _fly.Rotation);

        void SetMouseLook(bool on)
        {
            _mouseLook = on;
            Cursor.lockState = on ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !on;
        }

        /// <summary>QA: scripted fly input through the same DevFlyCam.Step as the keyboard/mouse path (60 Hz steps).
        /// Tokens separated by ';': w/s/a/d/q/e=seconds held, look=yawDeg,pitchDeg, wheel=notches, fast=0|1, along=0|1.</summary>
        void RunFlyScript(string script)
        {
            bool fast = false;
            foreach (var tok in script.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var kv = tok.Split('=');
                var key = kv[0].Trim().ToLowerInvariant();
                var val = kv.Length > 1 ? kv[1].Trim() : "1";
                float F(string x) => float.Parse(x, NumberStyles.Float, CultureInfo.InvariantCulture);
                var inp = new FlyInput { fast = fast };
                switch (key)
                {
                    case "look": { var a = val.Split(','); inp.lookDeg = new Vector2(F(a[0]), a.Length > 1 ? F(a[1]) : 0f); _fly.Step(inp, 1f / 60f); break; }
                    case "wheel": inp.wheel = F(val); _fly.Step(inp, 1f / 60f); break;
                    case "fast": fast = F(val) > 0f; break;
                    case "along": _fly.moveAlongView = F(val) > 0f; break;
                    default:
                        var mv = key switch { "w" => new Vector2(0, 1), "s" => new Vector2(0, -1), "d" => new Vector2(1, 0), "a" => new Vector2(-1, 0), _ => Vector2.zero };
                        float vert = key == "e" ? 1f : key == "q" ? -1f : 0f;
                        if (mv == Vector2.zero && vert == 0f) throw new ArgumentException("unknown token " + tok);
                        int n = Mathf.RoundToInt(F(val) * 60f);
                        for (int i = 0; i < n; i++) _fly.Step(new FlyInput { move = mv, vertical = vert, fast = fast }, 1f / 60f);
                        for (int i = 0; i < 60; i++) _fly.Step(new FlyInput(), 1f / 60f);     // let the smoothed velocity settle
                        break;
                }
            }
            ApplyFly();
            Log($"QA: dev view at ({_fly.position.x:F1}, {_fly.position.y:F1}, {_fly.position.z:F1}) yaw {_fly.yaw:F1} pitch {_fly.pitch:F1} speed {_fly.baseSpeed:F1} m/s");
        }

        void LoadScenarioFile(string path)
        {
            _cfg = ScenarioIO.Load(path);
            _navigationSelection.Clear();
            _scenarioFile = Path.GetFileName(path);
            ApplyScenario();
            Log("Loaded " + path);
        }

        void SaveScenario(bool overwrite)
        {
            _cfg.scenario_id = ScenarioIO.SanitizeId(_cfg.scenario_id);
            var path = Path.Combine(AppPaths.Scenarios, _cfg.scenario_id + ".json");
            try
            {
                ScenarioIO.Save(_cfg, path, overwrite);
                _scenarioFile = Path.GetFileName(path);
                Log("Saved " + path);
                RefreshScenarioList();
            }
            catch (IOException ex) { Log("Not saved: " + ex.Message); _status = "저장 안 됨: " + ex.Message; }
        }

        void RefreshScenarioList()
        {
            _scenarioFiles = Directory.Exists(AppPaths.Scenarios)
                ? Directory.GetFiles(AppPaths.Scenarios, "*.json").Select(Path.GetFileName).Where(f => f != "jobs.json").OrderBy(f => f).ToArray()
                : Array.Empty<string>();
        }

        void DoCapture()
        {
            if (_dirty) ApplyScenario();
            var name = ScenarioIO.SanitizeId(_cfg.scenario_id) + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var dir = AppPaths.UniqueDir(Path.Combine(AppPaths.Captures, "interactive", name));
            var res = CaptureService.Capture(_world, dir, "interactive");
            EnsurePreview();
            if (res.success)
            {
                _lastOutput = res.output_dir;
                Log($"Captured -> {res.output_dir} ({res.metadata.total_ms:F0} ms)");
            }
            else Log("Capture FAILED: " + res.error);
        }

        void Log(string s)
        {
            if (s.StartsWith("ERROR") || s.Contains("FAILED")) _hasError = true;
            _log.Add(DateTime.Now.ToString("HH:mm:ss ") + s);
            if (_log.Count > 200) _log.RemoveAt(0);
            _logScroll.y = float.MaxValue;
            Debug.Log("[PortCCTV] " + s);
        }

        // ---------------- dev free view ----------------
        void Update()
        {
            if (_world != null && _playing && !_busy)
            {
                _playT += Time.deltaTime * _playRate;
                _world.AdvanceNavigation(_playT);
                _cfg.navigation.sim_time_s = _world.Nav.Time;
                if (_playT > 7200f) _playing = false;
            }
            if (_world != null && !_busy && _world.Weather != null && _world.Weather.Active)
                _world.Weather.Tick(_devView ? _world.DevCamera : _world.CctvCamera, Time.unscaledDeltaTime);
            if (_world == null || !_devView) return;
            bool typing = GUIUtility.keyboardControl != 0;          // a text field has focus: keys go to it, not to the camera
            var mpos = Input.mousePosition;
            bool overView = _viewRect.Contains(new Vector2(mpos.x, Screen.height - mpos.y));
            if (!typing && Input.GetKeyDown(KeyCode.F)) SetMouseLook(!_mouseLook);
            if (_mouseLook && Input.GetKeyDown(KeyCode.Escape)) SetMouseLook(false);
            if (overView && (Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1))) _dragLook = true;
            if (!Input.GetMouseButton(0) && !Input.GetMouseButton(1)) _dragLook = false;
            var inp = new FlyInput();
            if (_mouseLook || _dragLook)
                inp.lookDeg = new Vector2(Input.GetAxisRaw("Mouse X"), -Input.GetAxisRaw("Mouse Y")) * _lookSens;
            if (!typing)
            {
                float dt = Time.unscaledDeltaTime;
                if (Input.GetKey(KeyCode.LeftArrow)) inp.lookDeg.x -= 90f * dt;
                if (Input.GetKey(KeyCode.RightArrow)) inp.lookDeg.x += 90f * dt;
                if (Input.GetKey(KeyCode.UpArrow)) inp.lookDeg.y -= 60f * dt;
                if (Input.GetKey(KeyCode.DownArrow)) inp.lookDeg.y += 60f * dt;
                if (Input.GetKey(KeyCode.W)) inp.move.y += 1f;
                if (Input.GetKey(KeyCode.S)) inp.move.y -= 1f;
                if (Input.GetKey(KeyCode.D)) inp.move.x += 1f;
                if (Input.GetKey(KeyCode.A)) inp.move.x -= 1f;
                if (Input.GetKey(KeyCode.E) || Input.GetKey(KeyCode.Space)) inp.vertical += 1f;
                if (Input.GetKey(KeyCode.Q) || Input.GetKey(KeyCode.C)) inp.vertical -= 1f;
                inp.fast = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
                inp.slow = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            }
            if (overView || _mouseLook) inp.wheel = Input.mouseScrollDelta.y;
            _fly.Step(inp, Time.unscaledDeltaTime);
            ApplyFly();
        }

        // ---------------- IMGUI ----------------
        const float PanelWidth = 400f;
        const float ContentWidth = PanelWidth - 52f;   // inside the scroll view, left of its vertical scrollbar
        GUIStyle _warn, _small, _title;

        void InitStyles()
        {
            if (_warn != null) return;
            _warn = new GUIStyle(GUI.skin.label) { wordWrap = true };
            _warn.normal.textColor = new Color(1f, 0.55f, 0.35f);
            _small = new GUIStyle(GUI.skin.label) { fontSize = 11, wordWrap = true };
            _title = new GUIStyle(GUI.skin.label) { fontSize = 15, fontStyle = FontStyle.Bold };
        }

        void OnGUI()
        {
            var koFont = UiText.KoreanFont();
            if (koFont != null) GUI.skin.font = koFont;
            InitStyles();
            GUI.color = Color.white;
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.blackTexture);
            if (_fatal != null)
            {
                GUI.Label(new Rect(10, 10, Screen.width - 20, Screen.height - 20), "초기화 실패:\n" + _fatal, _warn);
                return;
            }
            if (_world == null) return;
            if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape)
            { _placeOnPreview = false; _placementEntry = null; Event.current.Use(); }

            // Preview area.
            var area = new Rect(PanelWidth + 10, 10, Screen.width - PanelWidth - 20, Screen.height - 115);
            float aspect = _preview != null ? (float)_preview.width / _preview.height : 16f / 9f;
            var view = FitRect(area, aspect);
            var shown = PreviewTexture();
            if (shown != null) GUI.DrawTexture(view, shown, ScaleMode.StretchToFill, false);
            _viewRect = view;
            if (_devView && Event.current.type == EventType.MouseDown && view.Contains(Event.current.mousePosition))
                GUIUtility.keyboardControl = 0;                    // clicking the view releases a focused text field so WASD reach the camera
            if (_placeOnPreview && _placementEntry != null && !_devView)
            {
                GUI.Label(new Rect(view.x + 10, view.yMax - 36, view.width - 20, 30),
                    $"빈 수면을 클릭해 {UiText.Vessel(_placementEntry.asset_id)} 배치  (Esc 취소)", _title);
                if (Event.current.type == EventType.MouseDown && Event.current.button == 0 && view.Contains(Event.current.mousePosition))
                {
                    var m = Event.current.mousePosition;
                    var uv = new Vector2((m.x - view.x) / view.width, 1f - (m.y - view.y) / view.height);
                    AddVessel(_placementEntry, uv);
                    Event.current.Use();
                }
            }
            var badge = !_devView
                ? $"CCTV {UiText.Camera(_world.ActiveCamera)}  |  자세·내부 파라미터 고정  |  수직 화각 {_world.ActiveCamera.vertical_fov_deg:F1}°"
                : _mouseLook
                    ? $"자유 시점 — 마우스 시점 고정 중 (F 또는 Esc로 해제)  |  WASD 이동, Q/E 상하, 휠 속도 {_fly.baseSpeed:F0} m/s"
                    : $"자유 시점 (캡처 아님)  |  WASD 이동, Q/E 상하, 드래그로 둘러보기, F 마우스 고정, 휠 속도 {_fly.baseSpeed:F0} m/s";
            var badgeStyle = _devView ? _warn : GUI.skin.label;
            var bsize = badgeStyle.CalcSize(new GUIContent(badge));
            var oldc = GUI.color; GUI.color = new Color(0f, 0f, 0f, 0.6f);
            GUI.DrawTexture(new Rect(view.x + 2, view.y + 2, Mathf.Min(bsize.x + 10, view.width - 4), 24), Texture2D.whiteTexture);
            GUI.color = oldc;
            GUI.Label(new Rect(view.x + 6, view.y + 4, view.width - 12, 22), badge, badgeStyle);
            if (_overlay && !_devView) DrawOverlay(view);

            // Log area.
            var logRect = new Rect(PanelWidth + 10, Screen.height - 98, Screen.width - PanelWidth - 20, 88);
            GUI.Box(logRect, GUIContent.none);
            GUILayout.BeginArea(new Rect(logRect.x + 4, logRect.y + 4, logRect.width - 8, logRect.height - 8));
            _logScroll = GUILayout.BeginScrollView(_logScroll);
            foreach (var l in _log) GUILayout.Label(l.Replace('\\', '/'), _small);   // Korean fonts draw a backslash as the won sign
            GUILayout.EndScrollView();
            GUILayout.EndArea();

            // Control panel.
            GUI.Box(new Rect(0, 0, PanelWidth, Screen.height), GUIContent.none);
            GUILayout.BeginArea(new Rect(6, 6, PanelWidth - 12, Screen.height - 12));
            // fixed content width (vertical scrollbar only): rows shrink to the panel instead of running off its right edge
            _panelScroll = GUILayout.BeginScrollView(_panelScroll, false, true, GUIStyle.none, GUI.skin.verticalScrollbar);
            GUILayout.BeginVertical(GUILayout.Width(ContentWidth), GUILayout.MaxWidth(ContentWidth));
            DrawPanel();
            GUILayout.EndVertical();
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        static Rect FitRect(Rect area, float aspect)
        {
            float w = area.width, h = w / aspect;
            if (h > area.height) { h = area.height; w = h * aspect; }
            return new Rect(area.x + (area.width - w) / 2, area.y, w, h);
        }

        // ---------------- panel: five tabs, rare settings folded away ----------------
        static readonly string[] Tabs = { "장면", "날씨", "선박", "항해", "캡처" };
        int _tab;
        bool _foldMaps, _foldDev, _foldFiles, _foldWx, _foldVessel, _foldNav, _foldCap, _pickType, _pickTypeEdit, _foldSensor;
        RenderTexture _previewFx;
        Texture2D _previewJpg, _jpgScratch;
        float _jpgAt = -1f;
        string _sensorKey = "";
        List<LensDrop> _previewDrops = new List<LensDrop>();
        int _addIdx;
        GUIStyle _fold;

        void Header(string s) { GUILayout.Space(8); GUILayout.Label("<b>" + s + "</b>", RichLabel()); }

        bool Fold(ref bool open, string label)
        {
            _fold ??= new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, normal = { textColor = new Color(0.75f, 0.8f, 0.9f) } };
            GUILayout.Space(6);
            if (GUILayout.Button((open ? "▼  " : "▶  ") + label, _fold)) open = !open;
            return open;
        }

        void DrawPanel()
        {
            GUI.enabled = !_busy;
            GUILayout.Label("항만 CCTV 시뮬레이터", _title);
            GUILayout.Label(string.IsNullOrEmpty(_status) ? " " : _status, _small);
            GUILayout.Space(4);
            _tab = GUILayout.Toolbar(_tab, Tabs, GUILayout.Height(30), GUILayout.Width(ContentWidth));
            switch (_tab)
            {
                case 0: TabScene(); break;
                case 1: TabWeather(); break;
                case 2: TabVessels(); break;
                case 3: TabNavigation(); break;
                default: TabCapture(); break;
            }
            GUI.enabled = true;
            if (_busy) GUILayout.Label("일괄 실행 중 ...", _warn);
            GUILayout.Space(12);
            GUILayout.Label("라벨은 잠정값입니다 (사람 검수 전, training_ready=false).", _small);
        }

        void SwitchMap(string hid)
        {
            var keepPaint = _cfg?.appearance;
            _cfg = DefaultScenarioFor(hid);
            if (keepPaint != null) _cfg.appearance = keepPaint;
            _navigationSelection.Clear(); _selectedVessel = -1;
            Log("UI: map -> " + hid + " (default scene loaded)");
            ApplyScenario();
        }

        // ---- 장면: map, camera, view, scenario file
        void TabScene()
        {
            Header("지도");
            var main = new[] { ContainerTerminal01.Id, FishingPort01.Id, Marina01.Id };
            int mi = Array.IndexOf(main, _cfg.harbor_id);
            int nm = GUILayout.Toolbar(mi, main.Select(UiText.Harbor).ToArray(), GUILayout.Width(ContentWidth));
            if (nm != mi && nm >= 0) SwitchMap(main[nm]);
            if (Fold(ref _foldMaps, "다른 지도"))
                foreach (var hid in HarborLibrary.Ids.Where(h => !main.Contains(h)))
                    if (Choice(_cfg.harbor_id == hid, UiText.Harbor(hid))) SwitchMap(hid);

            Header("CCTV 카메라");
            if (_cfg.camera_override.enabled) GUILayout.Label("시나리오 파일의 사용자 지정 자세 사용 중 — 아래에서 고르면 해제", _warn);
            foreach (var p in CameraPresetLibrary.ForHarbor(_cfg.harbor_id))
                if (Choice(_cfg.camera_preset_id == p.id && !_cfg.camera_override.enabled, UiText.Camera(p)))
                { _cfg.camera_preset_id = p.id; _cfg.camera_override.enabled = false; Log("UI: camera -> " + p.id); ApplyScenario(); }

            Header("보기");
            int view = GUILayout.Toolbar(_devView ? 1 : 0, new[] { "CCTV 화면", "자유 시점" }, GUILayout.Width(ContentWidth));
            if ((view == 1) != _devView) { Log("UI: dev free view " + (view == 1 ? "on" : "off")); SetDevView(view == 1); }
            if (_devView)
            {
                GUILayout.Label("WASD 이동 · Q/E 상하 · 드래그로 둘러보기 · F 마우스 고정 · 휠 속도", _small);
                if (Fold(ref _foldDev, "자유 시점 설정")) DrawDevControls();
            }
            _overlay = GUILayout.Toggle(_overlay, " 선박 상자 표시");

            Header("시나리오");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("저장")) SaveScenario(true);
            if (GUILayout.Button("기본 장면")) { var keepPaint = _cfg.appearance; _cfg = DefaultScenarioFor(_cfg.harbor_id); _cfg.appearance = keepPaint ?? new AppearanceConfig(); _navigationSelection.Clear(); _selectedVessel = -1; Log("UI: default scenario"); ApplyScenario(); }
            GUILayout.EndHorizontal();
            if (Fold(ref _foldFiles, "이름 바꾸기 · 불러오기"))
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("이름", GUILayout.Width(34));
                _cfg.scenario_id = GUILayout.TextField(_cfg.scenario_id);
                if (GUILayout.Button("새로 저장", GUILayout.Width(80))) SaveScenario(false);
                GUILayout.EndHorizontal();
                if (GUILayout.Button("목록 새로고침")) RefreshScenarioList();
                foreach (var f in _scenarioFiles)
                    if (Choice(f == _scenarioFile, Path.GetFileNameWithoutExtension(f)))
                    {
                        try { LoadScenarioFile(Path.Combine(AppPaths.Scenarios, f)); }
                        catch (Exception ex) { Log("ERROR loading: " + ex.Message); _status = "불러오기 오류: " + ex.Message; }
                    }
            }
        }

        // ---- 날씨: time of day, weather presets, details folded
        static readonly (string label, string p, float i)[] WeatherPresets =
            { ("맑음", "none", 0f), ("이슬비", "rain", 0.25f), ("비", "rain", 0.6f), ("폭우", "rain", 1f), ("약한 눈", "snow", 0.3f), ("폭설", "snow", 1f) };

        void TabWeather()
        {
            Header("시간대");
            var envs = EnvironmentPresetLibrary.All;
            int ei = Array.FindIndex(envs, e => e.id == _cfg.environment.preset_id);
            int ne = GUILayout.SelectionGrid(ei, envs.Select(e => UiText.Environment(e)).ToArray(), 1, GUILayout.Width(ContentWidth));
            if (ne != ei && ne >= 0) { _cfg.environment.preset_id = envs[ne].id; Log("UI: environment -> " + envs[ne].id); ApplyScenario(); }
            var lmodes = new[] { "auto", "on", "off" };
            int li = Mathf.Max(0, Array.IndexOf(lmodes, _cfg.environment.lights ?? "auto"));
            GUILayout.BeginHorizontal();
            GUILayout.Label("항만·선박 조명", GUILayout.Width(100));
            int nli = GUILayout.Toolbar(li, new[] { "자동", "켬", "끔" });
            GUILayout.EndHorizontal();
            if (nli != li) { _cfg.environment.lights = lmodes[nli]; Log("UI: lights -> " + lmodes[nli]); ApplyScenario(); }
            var rl = _world.Lights?.Resolved;
            if (rl != null && rl.on) GUILayout.Label($"켜짐: 항만 조명 {rl.harbor_lamps}개, 항해등 {rl.vessel_nav_lights}개, 갑판등 {rl.vessel_deck_lights}개", _small);

            var wx = _cfg.environment.weather ??= new WeatherConfig();
            Header("날씨");
            int cur = Array.FindIndex(WeatherPresets, p => p.p == (wx.Precipitating ? wx.precipitation : "none") && (p.p == "none" || Mathf.Abs(p.i - wx.I) < 0.051f));
            int np = GUILayout.SelectionGrid(cur, WeatherPresets.Select(p => p.label).ToArray(), 3, GUILayout.Width(ContentWidth));
            if (np != cur && np >= 0)
            {
                var p = WeatherPresets[np];
                wx.precipitation = p.p; wx.intensity = p.p == "none" ? 0.5f : p.i; if (p.p == "none") wx.overcast = false;
                wx.wetness = -1f; wx.snow_cover = -1f;
                Log($"UI: weather -> {p.label}"); _pendingApply = true;
            }
            if (wx.Precipitating) wx.intensity = NumberField("wx_i", "강도", wx.intensity, 0.05f, 1f, 0.1f);

            if (Fold(ref _foldWx, "세부 설정"))
            {
                bool oc = GUILayout.Toggle(wx.Overcast, wx.Precipitating ? " 흐림 (비·눈일 때 항상)" : " 흐림");
                if (!wx.Precipitating && oc != wx.overcast) { wx.overcast = oc; _pendingApply = true; }
                wx.visibility_scale = NumberField("wx_v", "시정 배율", wx.visibility_scale, 0.1f, 3f, 0.1f);
                bool autoWet = wx.wetness < 0f;
                bool aw = GUILayout.Toggle(autoWet, $" 젖은 노면 자동 ({wx.Wetness:F1})");
                if (aw != autoWet) { wx.wetness = aw ? -1f : wx.Wetness; _pendingApply = true; }
                if (!aw) wx.wetness = NumberField("wx_wet", "젖은 노면", wx.wetness, 0f, 1f, 0.1f);
                bool autoSnow = wx.snow_cover < 0f;
                bool asn = GUILayout.Toggle(autoSnow, $" 쌓인 눈 자동 ({wx.SnowCover:F1})");
                if (asn != autoSnow) { wx.snow_cover = asn ? -1f : wx.SnowCover; _pendingApply = true; }
                if (!asn) wx.snow_cover = NumberField("wx_snow", "쌓인 눈", wx.snow_cover, 0f, 1f, 0.1f);
                GUILayout.Label("수면", _small);
                var waters = WaterPresetLibrary.All;
                int wi = Array.FindIndex(waters, w => w.id == _cfg.environment.water_preset_id);
                int nw = GUILayout.Toolbar(wi, waters.Select(w => UiText.Water(w)).ToArray(), GUILayout.Width(ContentWidth));
                if (nw != wi && nw >= 0) { _cfg.environment.water_preset_id = waters[nw].id; Log("UI: water -> " + waters[nw].id); ApplyScenario(); }
                _cfg.environment.water_time_s = NumberField("water_t", "수면 시각(s)", _cfg.environment.water_time_s, 0f, 600f, 0.5f, applyOnChange: false);
                var r = _world.Environment;
                GUILayout.Label($"안개 {r.fog_mean_free_path_m:F0} m · 노출 EV {r.exposure_ev100:F1}" + (wx.Precipitating ? $" · 입자 {r.weather.max_particles}개" : "") + " (엔진 값)", _small);
            }
        }

        // ---- 선박: add, list, selected vessel
        void TabVessels()
        {
            Header($"선박 {_cfg.vessels.Count}척");
            var app = _cfg.appearance ??= new AppearanceConfig();
            GUILayout.BeginHorizontal();
            GUILayout.Label("선체 색", GUILayout.Width(60));
            int pm = app.paint == VesselPaint.Random ? 1 : 0;
            int npm = GUILayout.Toolbar(pm, new[] { "원본", "무작위" });
            if (npm != pm) { app.paint = npm == 1 ? VesselPaint.Random : VesselPaint.Original; Log("UI: paint -> " + app.paint); ApplyScenario(); }
            if (app.paint == VesselPaint.Random && GUILayout.Button("다시 섞기", GUILayout.Width(80))) { app.paint_seed++; Log("UI: paint seed -> " + app.paint_seed); ApplyScenario(); }
            GUILayout.EndHorizontal();
            var addable = _world.Catalog.entries.OrderBy(e => _world.Harbor == null || _world.Harbor.AllowsVessel(e.asset_id) ? 0 : 1).ToList();
            _addIdx = Mathf.Clamp(_addIdx, 0, Mathf.Max(0, addable.Count - 1));
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(UiText.Vessel(addable[_addIdx].asset_id) + "  ▼", GUILayout.Width(ContentWidth * 0.46f))) _pickType = !_pickType;
            if (GUILayout.Button("+ 추가")) { _pickType = false; AddVessel(addable[_addIdx]); }
            if (GUILayout.Button(_placeOnPreview ? "배치 취소" : "화면에 배치"))
            {
                _placeOnPreview = !_placeOnPreview; _pickType = false;
                _placementEntry = _placeOnPreview ? addable[_addIdx] : null;
                if (_placeOnPreview) _status = $"보이는 빈 수면을 클릭해 {UiText.Vessel(addable[_addIdx].asset_id)} 배치.";
            }
            GUILayout.EndHorizontal();
            if (_pickType)
            {
                int pick = GUILayout.SelectionGrid(_addIdx, addable.Select(e => UiText.Vessel(e.asset_id) + (_world.Harbor != null && !_world.Harbor.AllowsVessel(e.asset_id) ? " *" : "")).ToArray(), 2, GUILayout.Width(ContentWidth));
                if (pick != _addIdx) { _addIdx = pick; _pickType = false; if (_placeOnPreview) _placementEntry = addable[_addIdx]; }
                GUILayout.Label("* 이 항만에서는 드문 선종 (배치는 가능)", _small);
            }

            GUILayout.Space(6);
            for (int i = 0; i < _cfg.vessels.Count; i++)
            {
                var v = _cfg.vessels[i];
                var rep = _reports.FirstOrDefault(r => r.object_id == v.object_id);
                GUILayout.BeginHorizontal();
                string mode = v.motion != null && v.motion.mode != "none" ? "  · " + UiText.Motion(v.motion.mode) : "";
                bool sel = GUILayout.Toggle(_selectedVessel == i, $" {v.object_id}  {UiText.Vessel(v.asset_id)}{mode}", GUI.skin.button, GUILayout.Width(ContentWidth - 30));
                if (sel && _selectedVessel != i) _selectedVessel = i;
                else if (!sel && _selectedVessel == i) _selectedVessel = -1;
                if (GUILayout.Button("×", GUILayout.Width(24))) { Log("UI: remove " + v.object_id); _navigationSelection.Remove(v.object_id); _cfg.vessels.RemoveAt(i); _selectedVessel = -1; ApplyScenario(); GUILayout.EndHorizontal(); break; }
                GUILayout.EndHorizontal();
                var hard = rep?.warnings.Where(w => !w.StartsWith("vessel_type_unusual") && !w.StartsWith("vessel_unusual_in_region")).ToList();
                if (hard != null && hard.Count > 0) GUILayout.Label("  ! " + string.Join(", ", hard), _warn);
                if (_selectedVessel == i) DrawVesselEditor(v);
            }
        }

        void DrawVesselEditor(VesselPlacement v)
        {
            GUILayout.BeginVertical(GUI.skin.box);
            v.motion ??= new VesselMotion();
            var modes = UiText.MotionModes;
            int mi = Math.Max(0, Array.IndexOf(modes, v.motion.mode));
            int mp = GUILayout.Toolbar(v.motion.mode == "route" ? -1 : mi, modes.Select(UiText.Motion).ToArray(), GUILayout.Width(ContentWidth - 12));
            if (mp >= 0 && modes[mp] != v.motion.mode) { StopPlayback(); v.motion.mode = modes[mp]; Log($"UI: {v.object_id} motion -> {v.motion.mode}"); _pendingApply = true; }
            v.heading_deg = NumberField("vh" + v.object_id, "방위(°)", v.heading_deg, 0f, 360f, 5f);
            if (v.motion.Moving) v.motion.speed_kn = NumberField("vs" + v.object_id, "속력(kn)", v.motion.speed_kn, 0f, 40f, 1f);
            var rec = _world.Nav?.Record().vessels.FirstOrDefault(r => r.object_id == v.object_id);
            if (rec != null && (v.motion.Moving || v.motion.Anchored))
                GUILayout.Label($"지금 {UiText.State(rec.state)} · {rec.speed_kn:F1} kn · 방위 {rec.heading_deg:F0}°", _small);
            var placed = _world.Vessels.FirstOrDefault(pv => pv.placement.object_id == v.object_id);
            var pmodes = new[] { "", VesselPaint.Original, VesselPaint.Random };
            int vpi = Math.Max(0, Array.IndexOf(pmodes, v.paint ?? ""));
            GUILayout.BeginHorizontal();
            GUILayout.Label("색", GUILayout.Width(24));
            int nvp = GUILayout.Toolbar(Array.IndexOf(pmodes, v.paint ?? "") < 0 ? -1 : vpi, new[] { "장면 따름", "원본", "무작위" });
            if (nvp >= 0 && pmodes[nvp] != (v.paint ?? "")) { v.paint = pmodes[nvp]; Log($"UI: {v.object_id} paint -> '{v.paint}'"); _pendingApply = true; }
            GUILayout.EndHorizontal();
            if (placed != null) DrawPaintSwatches(placed.paint, placed.entry);
            if (Fold(ref _foldVessel, "위치 · 선종 · 세부"))
            {
                if (GUILayout.Button("선종 바꾸기: " + UiText.Vessel(v.asset_id) + "  ▼")) _pickTypeEdit = !_pickTypeEdit;
                if (_pickTypeEdit)
                {
                    var names = _world.Catalog.entries.Select(e => e.asset_id).ToArray();
                    int cur = Array.IndexOf(names, v.asset_id);
                    int pick = GUILayout.SelectionGrid(cur, names.Select(UiText.Vessel).ToArray(), 2, GUILayout.Width(ContentWidth - 12));
                    if (pick != cur && pick >= 0)
                    {
                        v.asset_id = names[pick]; v.immersion_m = _world.Catalog.Find(names[pick]).default_immersion_m;
                        _pickTypeEdit = false; Log($"UI: {v.object_id} asset -> {v.asset_id}"); _pendingApply = true;
                    }
                }
                v.x = NumberField("vx" + v.object_id, "x (m)", v.x, -3000f, 3000f, 5f);
                v.z = NumberField("vz" + v.object_id, "z (m)", v.z, -4000f, 1600f, 5f);
                v.immersion_m = NumberField("vi" + v.object_id, "침수(m)", v.immersion_m, -0.5f, 20f, 0.05f);
                if (GUILayout.Button("빈 수면으로 옮기기")) RepositionVessel(v);
                if (rec != null && !string.IsNullOrEmpty(rec.route_note)) GUILayout.Label(rec.route_note, _small);
            }
            GUILayout.EndVertical();
        }

        static Texture2D _swatchTex;
        void DrawPaintSwatches(PaintRecord p, VesselAssetEntry e)
        {
            if (_swatchTex == null) { _swatchTex = new Texture2D(1, 1); _swatchTex.SetPixel(0, 0, Color.white); _swatchTex.Apply(); }
            GUILayout.BeginHorizontal();
            void Sw(string label, string hex)
            {
                GUILayout.Label(label, _small, GUILayout.Width(52));
                var r = GUILayoutUtility.GetRect(28, 14, GUILayout.Width(28));
                if (!string.IsNullOrEmpty(hex) && ColorUtility.TryParseHtmlString(hex, out var c)) { var old = GUI.color; GUI.color = c; GUI.DrawTexture(r, _swatchTex); GUI.color = old; }
                else GUI.Label(r, "원본", _small);
            }
            Sw("선체", p?.hull_srgb); Sw("상부구조", p?.superstructure_srgb);
            GUILayout.EndHorizontal();
            int hs = e.paint_slots?.Count(s => s.role == "hull") ?? 0, ss = e.paint_slots?.Count(s => s.role == "superstructure") ?? 0;
            if (hs + ss == 0) GUILayout.Label("이 선종은 색을 바꿀 수 있는 부위가 없습니다 (텍스처 일체형).", _small);
            else GUILayout.Label($"바뀌는 면적 {(p?.repaintable_area_fraction ?? 0f):P0} (유리·금속·갑판·장식은 원본 유지)", _small);
        }

        // ---- 항해: play / all roam / all stop; the rest folded
        void TabNavigation()
        {
            int moving = _cfg.vessels.Count(v => v.motion != null && v.motion.Moving);
            int anchored = _cfg.vessels.Count(v => v.motion != null && v.motion.Anchored);
            Header($"항해  ·  {_cfg.navigation.sim_time_s:F0} s");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(_playing ? "■ 일시정지" : "▶ 재생", GUILayout.Height(32), GUILayout.Width(ContentWidth * 0.45f)))
            {
                if (_playing) StopPlayback();
                else { if (_dirty) ApplyScenario(); _playT = _cfg.navigation.sim_time_s; _playing = true; Log($"UI: play from t={_playT:F1}"); }
            }
            int rate = _playRate >= 20f ? 2 : _playRate >= 5f ? 1 : 0;
            int nr = GUILayout.Toolbar(rate, new[] { "1×", "5×", "20×" }, GUILayout.Height(32));
            _playRate = nr == 2 ? 20f : nr == 1 ? 5f : 1f;
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("모두 자유 항해"))
            {
                StopPlayback();
                int n = _world.SetAutoNavigationForUnmoored(_cfg, "roam");
                Log($"UI: free roaming on {n} vessel(s)");
                ApplyScenario();
                _playT = _cfg.navigation.sim_time_s; _playing = true;
            }
            if (GUILayout.Button("모두 멈춤"))
            {
                StopPlayback();
                foreach (var v in _cfg.vessels) if (v.motion != null && v.motion.Moving) v.motion.mode = "none";
                Log("UI: all moving vessels stopped");
                ApplyScenario();
            }
            if (GUILayout.Button("처음으로")) { StopPlayback(); _cfg.navigation.sim_time_s = 0f; ApplyScenario(); }
            GUILayout.EndHorizontal();
            GUILayout.Label($"항해 {moving}척 · 정박 {anchored}척 · 고정 {_cfg.vessels.Count - moving - anchored}척", _small);

            if (Fold(ref _foldNav, "고급 (선택 운항 · 바람 · 조류)"))
            {
                if (GUILayout.Button("카메라 근처 순찰 루프"))
                {
                    StopPlayback();
                    int n = _world.SetAutoNavigationForUnmoored(_cfg, "auto");
                    Log($"UI: auto navigation on {n} vessel(s)");
                    ApplyScenario();
                }
                DrawNavigationSelection();
                foreach (var v in _cfg.vessels)
                {
                    bool marked = _navigationSelection.Contains(v.object_id);
                    bool mark = GUILayout.Toggle(marked, $" {v.object_id}  {UiText.Vessel(v.asset_id)}");
                    if (mark != marked) { if (mark) _navigationSelection.Add(v.object_id); else _navigationSelection.Remove(v.object_id); }
                }
                _cfg.navigation.collision_avoidance = GUILayout.Toggle(_cfg.navigation.collision_avoidance, " 충돌 회피");
                _cfg.navigation.wind_from_deg = NumberField("navwd", "풍향(°)", _cfg.navigation.wind_from_deg, 0f, 360f, 15f);
                _cfg.navigation.wind_speed_kn = NumberField("navws", "풍속(kn)", _cfg.navigation.wind_speed_kn, 0f, 40f, 1f);
                _cfg.navigation.current_set_deg = NumberField("navcd", "조류 방향(°)", _cfg.navigation.current_set_deg, 0f, 360f, 15f);
                _cfg.navigation.current_drift_kn = NumberField("navcs", "조류(kn)", _cfg.navigation.current_drift_kn, 0f, 4f, 0.1f);
                if (!_playing)
                {
                    float t = NumberField("navt", "시각(s)", _cfg.navigation.sim_time_s, 0f, 3600f, 5f, applyOnChange: false);
                    if (!Mathf.Approximately(t, _cfg.navigation.sim_time_s)) { _cfg.navigation.sim_time_s = t; ApplyScenario(); }
                }
                GUILayout.Label("풍향 = 불어오는 방향, 조류 방향 = 흘러가는 방향 (0° = +X, 시계방향).", _small);
            }
        }

        void DrawNavigationSelection()
        {
            GUILayout.Label($"선택 {_navigationSelection.Count}척", _small);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("항해 가능 전체", GUILayout.Width(ContentWidth / 2 - 4)))
            {
                _navigationSelection.Clear();
                foreach (var v in _world.Vessels) if (!_world.IsAlongside(v)) _navigationSelection.Add(v.placement.object_id);
            }
            if (GUILayout.Button("선택 해제", GUILayout.Width(ContentWidth / 2 - 4))) _navigationSelection.Clear();
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("자유 항해", GUILayout.Width(ContentWidth / 4 - 3))) ApplySelectedNavigation("roam", true);
            if (GUILayout.Button("순찰", GUILayout.Width(ContentWidth / 4 - 3))) ApplySelectedNavigation("auto", true);
            if (GUILayout.Button("정박", GUILayout.Width(ContentWidth / 4 - 3))) ApplySelectedNavigation("anchor", false);
            if (GUILayout.Button("고정", GUILayout.Width(ContentWidth / 4 - 3))) ApplySelectedNavigation("none", false);
            GUILayout.EndHorizontal();
            if (GUILayout.Button("선택 선박 수면 높이 기본값"))
            {
                int count = 0;
                foreach (var v in _cfg.vessels)
                {
                    if (!_navigationSelection.Contains(v.object_id)) continue;
                    var entry = _world.Catalog.Find(v.asset_id);
                    if (entry == null) continue;
                    v.immersion_m = entry.default_immersion_m;
                    count++;
                }
                if (count > 0) { ApplyScenario(); Log($"UI: reset immersion on {count} selected vessel(s)"); }
                else _status = "높이를 바꿀 선박을 먼저 선택하세요.";
            }
        }

        void ApplySelectedNavigation(string mode, bool play)
        {
            if (_navigationSelection.Count == 0) { _status = "운항할 선박을 먼저 선택하세요."; return; }
            StopPlayback();
            int count = 0;
            foreach (var v in _cfg.vessels)
            {
                if (!_navigationSelection.Contains(v.object_id)) continue;
                v.motion ??= new VesselMotion();
                v.motion.mode = mode;
                count++;
            }
            ApplyScenario();
            if (play) { _playT = _cfg.navigation.sim_time_s; _playing = true; }
            Log($"UI: {count} selected vessel(s) -> {mode}" + (play ? " + play" : ""));
        }

        void StopPlayback()
        {
            if (!_playing) return;
            _playing = false;
            Log($"UI: paused at t={_cfg.navigation.sim_time_s:F1}");
        }

        IEnumerator RunBatchUI(string jobs)
        {
            _busy = true;
            yield return BatchRunner.Run(_world, jobs, Log, s => { });
            _busy = false;
            ApplyScenario();
        }

        // ---- 캡처: one button; sequence and suites folded
        static readonly (int w, int h)[] Resolutions = { (1280, 720), (1920, 1080), (3840, 2160) };

        void TabCapture()
        {
            GUILayout.Space(8);
            if (_dirty && GUILayout.Button("대기 중인 변경 적용")) ApplyScenario();
            if (GUILayout.Button("캡처", GUILayout.Height(44), GUILayout.Width(ContentWidth))) DoCapture();
            if (!string.IsNullOrEmpty(_lastOutput))
            {
                GUILayout.Label("최근: " + Path.GetFileName(_lastOutput), _small);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("결과 폴더 열기")) Application.OpenURL("file:///" + _lastOutput.Replace('\\', '/'));
                if (GUILayout.Button("Captures 폴더")) { Directory.CreateDirectory(AppPaths.Captures); Application.OpenURL("file:///" + AppPaths.Captures.Replace('\\', '/')); }
                GUILayout.EndHorizontal();
            }
            GUILayout.Label("RGB, 가시 마스크(GPU, 렌더 화면과 일치), 상자, 메타데이터를 저장합니다.", _small);

            Header("CCTV 화질 (열화)");
            var pids = SensorConfig.PresetIds;
            int spi = Array.IndexOf(pids, _cfg.sensor.preset);
            int nsp = GUILayout.SelectionGrid(spi, UiText.SensorPresetNames, 3, GUILayout.Width(ContentWidth));
            if (nsp != spi && nsp >= 0)
            {
                int keepSeed = _cfg.sensor.seed; bool keepClean = _cfg.sensor.save_clean;
                _cfg.sensor = SensorConfig.Preset(pids[nsp]); _cfg.sensor.seed = keepSeed; _cfg.sensor.save_clean = keepClean;
                Log("UI: sensor -> " + pids[nsp]); SyncSensor();
            }
            GUILayout.Label(UiText.SensorPresetNote(_cfg.sensor.preset) + (_cfg.sensor.enabled ? "  (미리보기에 반영)" : ""), _small);
            if (_cfg.sensor.enabled && Fold(ref _foldSensor, "화질 세부"))
            {
                var sc = _cfg.sensor;
                sc.distortion_k1 = SensorField("s_k1", "렌즈 왜곡", sc.distortion_k1, -0.3f, 0.5f, 0.02f);
                sc.blur_sigma_px = SensorField("s_blur", "흐림(px)", sc.blur_sigma_px, 0f, 4f, 0.1f);
                sc.motion_blur_px = SensorField("s_mb", "흔들림(px)", sc.motion_blur_px, 0f, 12f, 0.5f);
                sc.noise_read_dn = SensorField("s_nr", "잡음", sc.noise_read_dn, 0f, 8f, 0.1f);
                sc.noise_shot = SensorField("s_ns", "밝기 잡음", sc.noise_shot, 0f, 1f, 0.02f);
                bool autoGain = sc.gain < 0f;
                bool ag = GUILayout.Toggle(autoGain, $" 이득 자동 (어두울수록 잡음 증가, 지금 x{(autoGain ? SensorProcessor.AutoGain(_world.Environment.exposure_ev100) : sc.gain):F1})");
                if (ag != autoGain) { sc.gain = ag ? -1f : SensorProcessor.AutoGain(_world.Environment.exposure_ev100); sc.preset = "custom"; SyncSensor(); }
                if (!ag) sc.gain = SensorField("s_gain", "이득", sc.gain, 1f, 8f, 0.25f);
                sc.vignette = SensorField("s_vig", "주변 감광", sc.vignette, 0f, 1f, 0.05f);
                sc.jpeg_quality = Mathf.RoundToInt(SensorField("s_jpg", "JPEG 품질", sc.jpeg_quality, 0f, 100f, 5f));
                bool autoDrops = sc.lens_drops < 0f;
                float dd = SensorProcessor.DropDensity(sc, _world.Environment.weather, _cfg.environment.weather);
                bool adr = GUILayout.Toggle(autoDrops, $" 렌즈 물방울 자동 (비 강도에 따름, 지금 {Mathf.RoundToInt(dd * 40f)}개)");
                if (adr != autoDrops) { sc.lens_drops = adr ? -1f : dd; sc.preset = "custom"; SyncSensor(); }
                if (!adr) sc.lens_drops = SensorField("s_drop", "물방울 밀도", sc.lens_drops, 0f, 1f, 0.05f);
                bool mono = GUILayout.Toggle(sc.monochrome, " 흑백 (적외선 야간 모드)");
                if (mono != sc.monochrome) { sc.monochrome = mono; sc.preset = "custom"; SyncSensor(); }
                bool clean = GUILayout.Toggle(sc.save_clean, " 원본(rgb_clean.png)도 저장");
                if (clean != sc.save_clean) { sc.save_clean = clean; SyncSensor(); }
                GUILayout.Label("왜곡은 마스크와 상자에도 같이 적용됩니다. 물방울·흐림·잡음은 라벨을 바꾸지 않고, 물방울에 가린 비율은 라벨에 기록합니다.", _small);
            }

            Header("해상도");
            int ri = Array.FindIndex(Resolutions, r => r.w == _cfg.capture.width && r.h == _cfg.capture.height);
            int nri = GUILayout.Toolbar(ri, Resolutions.Select(r => $"{r.w}×{r.h}").ToArray(), GUILayout.Width(ContentWidth));
            if (nri != ri && nri >= 0) { _cfg.capture.width = Resolutions[nri].w; _cfg.capture.height = Resolutions[nri].h; ApplyScenario(); }

            Header("연속 캡처");
            GUILayout.BeginHorizontal();
            GUILayout.Label("장수", GUILayout.Width(34));
            int.TryParse(GUILayout.TextField(_seqFrames.ToString(), GUILayout.Width(44)), out var fr);
            if (fr >= 1 && fr <= 10000) _seqFrames = fr;
            GUILayout.Label("간격(s)", GUILayout.Width(50));
            float.TryParse(GUILayout.TextField(_seqInterval.ToString("0.##", CultureInfo.InvariantCulture), GUILayout.Width(44)), NumberStyles.Float, CultureInfo.InvariantCulture, out var iv);
            if (iv > 0f && iv <= 600f) _seqInterval = iv;
            if (GUILayout.Button("시작"))
            {
                StopPlayback();
                var dir = Path.Combine(AppPaths.Scenarios, "sequence_" + ScenarioIO.SanitizeId(_cfg.scenario_id) + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
                try { StartCoroutine(RunBatchUI(SequenceSuite.Write(_cfg, dir, "Captures/" + Path.GetFileName(dir), _seqFrames, _seqInterval, _cfg.navigation.sim_time_s))); }
                catch (Exception ex) { Log("ERROR: " + ex.Message); }
            }
            GUILayout.EndHorizontal();
            GUILayout.Label("항해 시각을 간격만큼 진행하며 저장합니다.", _small);

            if (Fold(ref _foldCap, "라벨 · 검사"))
            {
                _cfg.capture.write_mask = GUILayout.Toggle(_cfg.capture.write_mask, " 가시 마스크 저장");
                bool cpu = _cfg.capture.mask_source == "cpu";
                bool ncpu = GUILayout.Toggle(cpu, " 예전 CPU 마스크 사용 (평균 수면 기준)");
                if (ncpu != cpu) _cfg.capture.mask_source = ncpu ? "cpu" : "gpu";
                _cfg.capture.compare_cpu_mask = GUILayout.Toggle(_cfg.capture.compare_cpu_mask, " CPU 마스크도 함께 저장해 비교");
                if (GUILayout.Button("맵 세트 캡처 (지도 3종)"))
                {
                    var dir = Path.Combine(AppPaths.Scenarios, "mapsuite_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
                    try { StartCoroutine(RunBatchUI(MapSuite.Write(dir, "Captures/" + Path.GetFileName(dir)))); }
                    catch (Exception ex) { Log("ERROR: " + ex.Message); }
                }
                if (GUILayout.Button("회귀 검사 실행"))
                {
                    var dir = Path.Combine(AppPaths.Scenarios, "regression_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
                    try { StartCoroutine(RunBatchUI(RegressionSuite.Write(dir, "Captures/regression_" + Path.GetFileName(dir).Substring("regression_".Length)))); }
                    catch (Exception ex) { Log("ERROR: " + ex.Message); }
                }
            }
        }

        readonly Dictionary<string, string> _numBuf = new Dictionary<string, string>();
        bool _pendingApply;

        /// <summary>Label, -/+ step buttons, editable number field and a fixed-width slider.</summary>
        float NumberField(string key, string label, float value, float min, float max, float step, bool applyOnChange = true)
        {
            float v0 = value;
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(88));
            if (GUILayout.Button("-", GUILayout.Width(22))) value -= step;
            GUI.SetNextControlName(key);
            bool focused = GUI.GetNameOfFocusedControl() == key;
            if (!focused || !_numBuf.ContainsKey(key)) _numBuf[key] = value.ToString("0.###", CultureInfo.InvariantCulture);
            var txt = GUILayout.TextField(_numBuf[key], GUILayout.Width(62));
            if (txt != _numBuf[key])
            {
                _numBuf[key] = txt;
                if (float.TryParse(txt, NumberStyles.Float, CultureInfo.InvariantCulture, out var f)) value = f;
            }
            if (GUILayout.Button("+", GUILayout.Width(22))) value += step;
            var sr = GUILayoutUtility.GetRect(104, 18, GUILayout.Width(104));
            var old = GUI.color;
            GUI.color = new Color(0.55f, 0.55f, 0.6f);
            GUI.DrawTexture(new Rect(sr.x + 2, sr.y + sr.height / 2 - 2, sr.width - 4, 4), Texture2D.whiteTexture);
            GUI.color = old;
            var sv = GUI.HorizontalSlider(sr, value, min, max);
            if (!Mathf.Approximately(sv, value)) value = Mathf.Round(sv / step) * step;
            GUILayout.EndHorizontal();
            value = Mathf.Clamp(value, min, max);
            if (!Mathf.Approximately(value, v0))
            {
                if (applyOnChange) _pendingApply = true; else _dirty = true;
            }
            return value;
        }

        void LateUpdate()
        {
            if (_pendingApply && _world != null) { _pendingApply = false; ApplyScenario(); }
            if (_world != null && _preview != null && _world.Lights != null && _world.Lights.On)
                _world.Lights.UpdateForCamera(_devView ? _world.DevCamera : _world.CctvCamera, _preview.height);
            if (_world != null && !_busy) UpdatePreviewFx();
        }

        /// <summary>Preview with the CCTV degradation (GPU pass every frame; JPEG round trip refreshed 4 times a second).</summary>
        bool PreviewDegraded => _preview != null && _cfg?.sensor != null && _cfg.sensor.enabled && !_devView && _world?.Sensor != null && _world.Sensor.Available;

        /// <summary>Texture shown in the preview: the camera image, or its degraded copy made in LateUpdate.</summary>
        Texture PreviewTexture()
        {
            if (!PreviewDegraded || _previewFx == null) return _preview;
            return _cfg.sensor.jpeg_quality > 0 && _previewJpg != null ? _previewJpg : _previewFx;
        }

        /// <summary>CCTV degradation of the last camera frame (GPU pass every frame; JPEG round trip refreshed 4 times a second).
        /// Runs in LateUpdate, outside OnGUI: inside OnGUI Unity renders without the linear-to-sRGB write conversion.</summary>
        void UpdatePreviewFx()
        {
            if (!PreviewDegraded) return;
            var sc = _cfg.sensor;
            if (_previewFx == null || _previewFx.width != _preview.width || _previewFx.height != _preview.height)
            {
                if (_previewFx != null) { _previewFx.Release(); Destroy(_previewFx); }
                _previewFx = new RenderTexture(_preview.width, _preview.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { name = "CCTVPreviewFx" };
                _previewFx.Create();
            }
            var key = JsonUtility.ToJson(sc) + "|" + _cfg.environment.weather?.precipitation + _cfg.environment.weather?.intensity;
            if (key != _sensorKey)
            {
                _sensorKey = key; _jpgAt = -1f;
                _previewDrops = SensorProcessor.MakeDrops(sc.seed, SensorProcessor.DropDensity(sc, _world.Environment.weather, _cfg.environment.weather), _cfg.capture.width, _cfg.capture.height);
            }
            float gain = sc.gain >= 0f ? sc.gain : SensorProcessor.AutoGain(_world.Environment.exposure_ev100);
            _world.Sensor.Blit(_preview, _previewFx, sc, gain, _previewDrops, _cfg.capture.width, _cfg.capture.height, Time.frameCount);
            if (sc.jpeg_quality <= 0 || (_previewJpg != null && Time.unscaledTime - _jpgAt < 0.25f)) return;
            _jpgAt = Time.unscaledTime;
            int w = _previewFx.width, h = _previewFx.height;
            if (_jpgScratch == null || _jpgScratch.width != w || _jpgScratch.height != h) _jpgScratch = new Texture2D(w, h, TextureFormat.RGBA32, false, false);   // sRGB: ReadPixels keeps the encoded bytes
            var prev = RenderTexture.active; RenderTexture.active = _previewFx;
            _jpgScratch.ReadPixels(new Rect(0, 0, w, h), 0, 0); _jpgScratch.Apply(false);
            RenderTexture.active = prev;
            var px = SensorProcessor.Jpeg(_jpgScratch, _jpgScratch.GetPixels32(), w, h, sc.jpeg_quality);
            if (_previewJpg == null || _previewJpg.width != w || _previewJpg.height != h) _previewJpg = new Texture2D(w, h, TextureFormat.RGBA32, false, false);   // sRGB bytes
            _previewJpg.SetPixels32(px); _previewJpg.Apply(false);
        }

        void SyncSensor() { _world?.SetSensor(_cfg.sensor); _sensorKey = ""; }

        float SensorField(string key, string label, float value, float min, float max, float step)
        {
            bool d = _dirty;
            float v = NumberField(key, label, value, min, max, step, applyOnChange: false);
            _dirty = d;
            if (!Mathf.Approximately(v, value)) { _cfg.sensor.preset = "custom"; SyncSensor(); }
            return v;
        }

        void DrawDevControls()
        {
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label("WASD 이동 · Q/E(또는 C/Space) 하강/상승 · 화면을 좌/우클릭 드래그 = 둘러보기 · F = 마우스 시점 고정 (Esc 해제) · 방향키 회전 · 휠 = 이동 속도 · Shift 4배, Ctrl 1/4", _small);
            _fly.moveAlongView = GUILayout.Toggle(_fly.moveAlongView, " 시선 방향으로 이동 (끄면 수평 이동)");
            GUILayout.BeginHorizontal();
            GUILayout.Label($"이동 속도 {_fly.baseSpeed:F0} m/s", GUILayout.Width(140));
            float ls = TrackSlider(Mathf.Log(_fly.baseSpeed), Mathf.Log(DevFlyCam.MinSpeed), Mathf.Log(DevFlyCam.MaxSpeed));
            _fly.baseSpeed = Mathf.Clamp(Mathf.Exp(ls), DevFlyCam.MinSpeed, DevFlyCam.MaxSpeed);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label($"마우스 감도 {_lookSens:F1}", GUILayout.Width(140));
            _lookSens = TrackSlider(_lookSens, 0.5f, 6f);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(_mouseLook ? "마우스 고정 해제" : "마우스 시점 고정 (F)")) SetMouseLook(!_mouseLook);
            if (GUILayout.Button("CCTV 위 30 m")) { FlyToCctv(30f, 25f); ApplyFly(); }
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("CCTV와 같은 시점")) { FlyToCctv(0f, null); ApplyFly(); }
            if (GUILayout.Button("상공 조망")) { FlyToCctv(250f, 55f); ApplyFly(); }
            GUILayout.EndHorizontal();
            GUILayout.Label($"위치 ({_fly.position.x:F0}, {_fly.position.y:F0}, {_fly.position.z:F0})  방위 {_fly.yaw:F0}°  피치 {_fly.pitch:F0}°", _small);
            GUILayout.EndVertical();
        }

        /// <summary>Horizontal slider with a visible track (the default skin's track is invisible on the dark panel).</summary>
        static float TrackSlider(float value, float min, float max)
        {
            var r = GUILayoutUtility.GetRect(80, 18, GUILayout.ExpandWidth(true));
            var old = GUI.color;
            GUI.color = new Color(0.55f, 0.55f, 0.6f);
            GUI.DrawTexture(new Rect(r.x + 2, r.y + r.height / 2 - 2, r.width - 4, 4), Texture2D.whiteTexture);
            GUI.color = old;
            return GUI.HorizontalSlider(r, value, min, max);
        }

        bool AddVessel(VesselAssetEntry e, Vector2? targetViewport = null)
        {
            if (_cfg.vessels.Count >= 254) { _status = "라벨링 가능한 선박은 최대 254척입니다."; return false; }
            bool found = VesselSpawnPlanner.TryFind(_world, e, _cfg.vessels, _spawnRandom, out var place, targetViewport);
            string seenFrom = null;
            if (!found && !targetViewport.HasValue && !_cfg.camera_override.enabled)
            {
                // e.g. a 322 m tanker in a terminal view that only shows the basin: use the open water another camera of this map sees
                foreach (var p in CameraPresetLibrary.ForHarbor(_cfg.harbor_id).Where(p => p.id != _cfg.camera_preset_id))
                {
                    var tmp = _cfg.Clone(); tmp.camera_preset_id = p.id;
                    _world.ApplyCamera(tmp);
                    found = VesselSpawnPlanner.TryFind(_world, e, _cfg.vessels, _spawnRandom, out place);
                    if (found) { seenFrom = p.id; break; }
                }
                _world.ApplyCamera(_cfg);
            }
            if (!found)
            {
                _status = targetViewport.HasValue
                    ? $"그 근처에 {UiText.Vessel(e.asset_id)}을(를) 놓을 빈 수면이 없습니다. 다른 곳을 클릭하세요."
                    : $"이 카메라에서 {UiText.Vessel(e.asset_id)}을(를) 놓을 빈 수면을 찾지 못했습니다. 다른 카메라를 고르거나 선박을 지우세요.";
                Log($"UI: no clear water position for {e.asset_id}" + (targetViewport.HasValue ? " near the clicked point" : " in this camera"));
                return false;
            }
            int n = 0;
            string id;
            do id = $"v{n++:00}"; while (_cfg.vessels.Any(v => v.object_id == id));
            place.object_id = id;
            _cfg.vessels.Add(place);
            _selectedVessel = _cfg.vessels.Count - 1;
            Log($"UI: add {id} ({e.asset_id}) at ({place.x:F0}, {place.z:F0}), heading {place.heading_deg:F0}");
            ApplyScenario();
            _status = $"{id} ({UiText.Vessel(e.asset_id)}) 추가: ({place.x:F0}, {place.z:F0})." +
                (seenFrom != null ? $" 현재 카메라에는 자리가 없어 '{UiText.Camera(CameraPresetLibrary.Get(seenFrom))}' 시야의 수면에 배치했습니다." : "");
            if (seenFrom != null) Log($"UI: {id} placed in the view of {seenFrom} (no room in {_cfg.camera_preset_id})");
            return true;
        }

        void RepositionVessel(VesselPlacement vessel)
        {
            var entry = _world.Catalog.Find(vessel.asset_id);
            var others = _cfg.vessels.Where(v => v != vessel).ToList();
            if (entry == null || !VesselSpawnPlanner.TryFind(_world, entry, others, _spawnRandom, out var place))
            {
                _status = $"{UiText.Vessel(vessel.asset_id)}을(를) 옮길 빈 수면을 찾지 못했습니다.";
                Log("UI: no clear visible water position found for " + vessel.asset_id);
                return;
            }
            vessel.x = place.x; vessel.z = place.z; vessel.heading_deg = place.heading_deg;
            Log($"UI: reposition {vessel.object_id} to ({vessel.x:F0}, {vessel.z:F0})");
            _pendingApply = true;
        }

        GUIStyle _choiceOn, _choiceOff;

        /// <summary>Radio-like button: the selected entry is bold/yellow with a filled marker. True only when a NEW entry is clicked.</summary>
        bool Choice(bool on, string label)
        {
            if (_choiceOn == null)
            {
                _choiceOff = new GUIStyle(GUI.skin.button) { alignment = TextAnchor.MiddleLeft };
                _choiceOn = new GUIStyle(_choiceOff) { fontStyle = FontStyle.Bold };
                _choiceOn.normal.background = _choiceOff.active.background;
                _choiceOn.normal.textColor = new Color(1f, 0.85f, 0.3f);
            }
            return GUILayout.Button((on ? "[x]  " : "[  ]  ") + label, on ? _choiceOn : _choiceOff) && !on;
        }

        GUIStyle _rich;
        GUIStyle RichLabel() => _rich ??= new GUIStyle(GUI.skin.label) { richText = true, wordWrap = true };

        void DrawOverlay(Rect view)
        {
            var cam = _world.CctvCamera;
            var proj = LabelComputer.MakeProjection(cam, _preview.width, _preview.height);
            if (_cfg.sensor != null && _cfg.sensor.enabled) proj.lens = LensModel.Make(_cfg.sensor, _preview.width, _preview.height);   // boxes follow the distorted preview
            float sx = view.width / _preview.width, sy = view.height / _preview.height;
            foreach (var v in _world.Vessels)
            {
                LabelComputer.ComputeBoxes(v, proj, _world.Harbor.water_reference_y, out var full, out var above);
                DrawBox(view, full.clipped, sx, sy, new Color(1f, 0.86f, 0f));
                DrawBox(view, above.clipped, sx, sy, Color.cyan);
                if (full.clipped.valid)
                    GUI.Label(new Rect(view.x + full.clipped.x_min * sx, view.y + full.clipped.y_min * sy - 18, 200, 20),
                        $"{v.placement.object_id} {UiText.Vessel(v.placement.asset_id)}" + (v.report.ok ? "" : " !"));
            }
        }

        static void DrawBox(Rect view, PixelBox b, float sx, float sy, Color c)
        {
            if (!b.valid) return;
            var r = new Rect(view.x + b.x_min * sx, view.y + b.y_min * sy, b.Width * sx, b.Height * sy);
            var old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(new Rect(r.x, r.y, r.width, 2), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(r.x, r.yMax - 2, r.width, 2), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(r.x, r.y, 2, r.height), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(r.xMax - 2, r.y, 2, r.height), Texture2D.whiteTexture);
            GUI.color = old;
        }
    }
}
