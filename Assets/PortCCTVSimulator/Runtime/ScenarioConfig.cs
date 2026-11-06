using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace PortCCTV
{
    /// <summary>
    /// A complete, serialisable description of one fixed-CCTV observation.
    /// Everything that changes the rendered image or the diagnostic labels must live here
    /// (or in a versioned preset referenced from here) so that save -> reload reproduces it.
    /// </summary>
    [Serializable]
    public class ScenarioConfig
    {
        public const int CurrentSchemaVersion = 1;

        public int schema_version = CurrentSchemaVersion;
        public string scenario_id = "untitled";
        public string harbor_id = HarborGeneric02.Id; // matches the default camera id; the app starts from HarborLibrary.DefaultHarborId
        public string camera_preset_id = CameraPresetLibrary.DefaultId;
        public CameraOverride camera_override = new CameraOverride();
        public EnvironmentConfig environment = new EnvironmentConfig();
        public List<VesselPlacement> vessels = new List<VesselPlacement>();
        public CaptureSettings capture = new CaptureSettings();
        /// <summary>Vessel motion: poses in `vessels` are the start poses (t = 0); rendering uses the poses at navigation.sim_time_s.</summary>
        public NavigationConfig navigation = new NavigationConfig();
        /// <summary>CCTV image degradation (distortion, blur, noise, lens drops, JPEG). Off in older files.</summary>
        public SensorConfig sensor = new SensorConfig();
        /// <summary>Vessel paint colours (original / random per vessel). Older files: original.</summary>
        public AppearanceConfig appearance = new AppearanceConfig();
        public string notes = "";

        public ScenarioConfig Clone() => JsonUtility.FromJson<ScenarioConfig>(JsonUtility.ToJson(this));
    }

    [Serializable]
    public class CameraOverride
    {
        /// <summary>When false the named preset is used verbatim.</summary>
        public bool enabled = false;
        public Vector3 position = Vector3.zero;
        public Vector3 euler_deg = Vector3.zero;
        public float vertical_fov_deg = 40f;
    }

    [Serializable]
    public class EnvironmentConfig
    {
        public string preset_id = EnvironmentPresetLibrary.DefaultId;
        public string water_preset_id = WaterPresetLibrary.DefaultId;
        /// <summary>Requested HDRP water simulation time at capture (seconds).</summary>
        public float water_time_s = 12f;
        /// <summary>Rain / snow / overcast / visibility on top of the preset (older files: none).</summary>
        public WeatherConfig weather = new WeatherConfig();
        /// <summary>Harbour lamps and vessel navigation / deck lights: "auto" (night presets and sun below 4 deg), "on", "off".</summary>
        public string lights = "auto";
    }

    [Serializable]
    public class VesselPlacement
    {
        public string object_id = "vessel_00";
        public string asset_id = "rib";
        public float x = 0f;
        public float z = 0f;
        /// <summary>Yaw in degrees. heading 0 = bow towards world +X, positive = Unity yaw (clockwise seen from above).</summary>
        public float heading_deg = 0f;
        /// <summary>
        /// Geometric placement parameter: distance of the named main-hull candidate's lowest vertex
        /// below the mean water plane (y=0). NOT a measured/hydrostatic draft.
        /// </summary>
        public float immersion_m = 0.10f;
        /// <summary>Motion from the start pose above ("none" = stays put). See NavigationSim.</summary>
        public VesselMotion motion = new VesselMotion();
        /// <summary>"" = follow scenario appearance.paint; "original"; "random"; or fixed "hull=#RRGGBB;super=#RRGGBB".</summary>
        public string paint = "";
    }

    [Serializable]
    public class CaptureSettings
    {
        public int width = 1920;
        public int height = 1080;
        public int warmup_frames = 6;
        public bool write_mask = true;
        /// <summary>"gpu" (default): render-consistent instance-ID pass, depth-tested against the rendered frame (waves, water line,
        /// every occluder). "cpu": legacy ray mask on the mean water plane.</summary>
        public string mask_source = "gpu";
        /// <summary>Also compute the CPU ray mask and write mask_instances_cpu.png + per-object agreement (diagnostic, slower).</summary>
        public bool compare_cpu_mask = false;
        /// <summary>CPU visible-mask ray spacing in output pixels (1 = every pixel centre). Not used by the GPU mask.</summary>
        public int mask_stride = 1;
        public bool strict_placement = false;
    }

    public static class ScenarioIO
    {
        public static string ToJson(ScenarioConfig cfg) => JsonUtility.ToJson(cfg, true);

        public static ScenarioConfig FromJson(string json)
        {
            var cfg = JsonUtility.FromJson<ScenarioConfig>(json);
            if (cfg == null) throw new InvalidDataException("Scenario JSON could not be parsed.");
            if (cfg.schema_version != ScenarioConfig.CurrentSchemaVersion)
                throw new InvalidDataException($"Unsupported scenario schema_version {cfg.schema_version} (expected {ScenarioConfig.CurrentSchemaVersion}).");
            cfg.vessels ??= new List<VesselPlacement>();
            cfg.environment ??= new EnvironmentConfig();
            cfg.environment.weather ??= new WeatherConfig();
            if (string.IsNullOrEmpty(cfg.environment.weather.precipitation)) cfg.environment.weather.precipitation = "none";
            cfg.capture ??= new CaptureSettings();
            if (string.IsNullOrEmpty(cfg.capture.mask_source)) cfg.capture.mask_source = "gpu";
            if (string.IsNullOrEmpty(cfg.environment.lights)) cfg.environment.lights = "auto";
            cfg.sensor ??= new SensorConfig();
            if (string.IsNullOrEmpty(cfg.sensor.preset)) cfg.sensor.preset = cfg.sensor.enabled ? "custom" : "off";
            cfg.camera_override ??= new CameraOverride();
            cfg.appearance ??= new AppearanceConfig();
            if (string.IsNullOrEmpty(cfg.appearance.paint)) cfg.appearance.paint = VesselPaint.Original;
            cfg.navigation ??= new NavigationConfig();
            if (cfg.navigation.dt_s <= 0f) cfg.navigation.dt_s = 0.25f;     // files written before navigation existed
            foreach (var v in cfg.vessels)
            {
                v.motion ??= new VesselMotion();
                if (string.IsNullOrEmpty(v.motion.mode)) v.motion.mode = "none";
                v.motion.route ??= new List<Vector2>();
                v.paint ??= "";
            }
            return cfg;
        }

        public static ScenarioConfig Load(string path) => FromJson(File.ReadAllText(path, Encoding.UTF8));

        /// <summary>Saves; refuses to overwrite unless <paramref name="overwrite"/>.</summary>
        public static void Save(ScenarioConfig cfg, string path, bool overwrite)
        {
            if (File.Exists(path) && !overwrite) throw new IOException($"Refusing to overwrite existing file: {path}");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, ToJson(cfg), new UTF8Encoding(false));
        }

        public static string Sha256Hex(string text) => Sha256Hex(Encoding.UTF8.GetBytes(text));

        public static string Sha256Hex(byte[] data)
        {
            using var sha = SHA256.Create();
            var h = sha.ComputeHash(data);
            var sb = new StringBuilder(h.Length * 2);
            foreach (var b in h) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }

        public static string SanitizeId(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "untitled";
            var sb = new StringBuilder();
            foreach (var c in s.Trim())
                sb.Append(char.IsLetterOrDigit(c) || c == '_' || c == '-' ? c : '_');
            return sb.ToString();
        }
    }

    /// <summary>Resolves where the app reads/writes files, both in the Editor and in a Windows player.</summary>
    public static class AppPaths
    {
        /// <summary>Editor: project root. Player: folder that contains the .exe.</summary>
        public static string Root
        {
            get
            {
                // Application.dataPath = <project>/Assets (editor) or <exeDir>/<name>_Data (player).
                return Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            }
        }

        public static string Scenarios => Path.Combine(Root, "Scenarios");
        public static string Captures => Path.Combine(Root, "Captures");

        public static string ResolveUnderRoot(string p)
        {
            if (string.IsNullOrEmpty(p)) return Root;
            return Path.IsPathRooted(p) ? p : Path.GetFullPath(Path.Combine(Root, p));
        }

        /// <summary>Returns <paramref name="dir"/> or dir_2, dir_3 ... that does not exist yet.</summary>
        public static string UniqueDir(string dir)
        {
            if (!Directory.Exists(dir) && !File.Exists(dir)) return dir;
            for (int i = 2; i < 10000; i++)
            {
                var c = dir + "_" + i;
                if (!Directory.Exists(c) && !File.Exists(c)) return c;
            }
            throw new IOException("Could not find a unique directory name for " + dir);
        }
    }
}
