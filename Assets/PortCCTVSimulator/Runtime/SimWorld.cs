using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using Unity.Mathematics;

namespace PortCCTV
{
    public class PlacementReport
    {
        public string object_id;
        public string asset_id;
        public string water_region;         // region containing the hull centre, or null
        public List<string> warnings = new List<string>();
        public bool ok => warnings.Count == 0;
        /// <summary>Informational notes that do not make the placement invalid (e.g. immersion far from the asset default).</summary>
        public List<string> notes = new List<string>();
    }

    public class PlacedVessel
    {
        public VesselPlacement placement;
        public VesselAssetEntry entry;
        public GameObject root;
        public List<Collider> colliders = new List<Collider>();
        public Vector2[] footprint_xz;
        public PlacementReport report;
        public int instance_index; // 1..N, value written in the instance mask
        public PaintRecord paint = new PaintRecord();
        /// <summary>Wave motion applied on top of the mean-plane pose: "off" | "ok" | "partial" (some hull points had no water
        /// height) | "unavailable" (water query not ready; vessel left on the mean plane).</summary>
        public string wave_status = "off";
        public float wave_heave_m, wave_pitch_deg, wave_roll_deg;

        public float HullReferenceMinWorldY => root.transform.position.y + entry.geometry.hull_reference_min_y;
        public float RenderMinWorldY => root.transform.position.y + entry.geometry.render_min_y;
    }

    /// <summary>Resolved environment values actually applied (written to metadata).</summary>
    [Serializable]
    public class ResolvedEnvironment
    {
        public string preset_id;
        public string water_preset_id;
        public float sun_elevation_deg;
        public float sun_azimuth_deg;
        public Vector3 sun_light_forward;
        public float sun_illuminance_lux;
        public float exposure_ev100;
        public bool fog_enabled;
        public float fog_mean_free_path_m;
        public float cloud_opacity;
        public string sky;
        public float sky_hdri_rotation_deg;
        public string sky_hdri_sha256;
        public float sky_lux;
        public float water_reference_y;
        public float water_large_wind_speed_kmh;
        public float water_ripples_wind_speed_kmh;
        public float water_repetition_size_m;
        public float water_large_band0_multiplier;
        public float water_large_band1_multiplier;
        public float water_time_requested_s;
        public float water_time_actual_s = float.NaN;
        public bool water_time_frozen;
        public bool vessel_wave_motion;
        /// <summary>Ground wear decals (oil, tyre marks, patches, grime) on paved surfaces; deterministic per harbor.</summary>
        public int ground_wear_decals;
        /// <summary>Cross-billboard trees on the forest hills and coastline scan pieces (background, deterministic per harbor).</summary>
        public int forest_billboard_trees, forest_harbor_extra_trees, forest_camera_zone_trees, town_billboard_trees, flat_land_trees, near_3d_trees, coastline_pieces, lighthouse_paths, lighthouse_landings;
        public float lighthouse_path_max_grade, lighthouse_path_length_m;
        public ResolvedWeather weather = new ResolvedWeather();
        public ResolvedLights lights = new ResolvedLights();
    }

    /// <summary>Owns all runtime scene objects: harbor, sun/sky/volume, water, fixed CCTV camera, vessels.</summary>
    public class SimWorld
    {
        public HarborDefinition Harbor { get; private set; }
        public GameObject HarborRoot { get; private set; }
        public List<Collider> HarborColliders { get; private set; } = new List<Collider>();
        public Dictionary<string, GameObject> HarborObjects { get; private set; } = new Dictionary<string, GameObject>();
        public Camera CctvCamera { get; private set; }
        public Camera DevCamera { get; private set; }
        public Light Sun { get; private set; }
        public HDAdditionalLightData SunHD { get; private set; }
        public WaterSurface Water { get; private set; }
        /// <summary>Number of ground wear decals placed on the current harbor (background only).</summary>
        public int GroundWearDecals { get; private set; }
        public Vegetation.Result VegetationResult { get; private set; }
        public Volume Volume { get; private set; }
        public VesselCatalog Catalog { get; private set; }
        public HarborTheme Theme { get; private set; }
        public List<PlacedVessel> Vessels { get; } = new List<PlacedVessel>();
        public ResolvedEnvironment Environment { get; private set; } = new ResolvedEnvironment();
        public CameraPreset ActiveCamera { get; private set; }
        public ScenarioConfig Scenario { get; private set; }
        /// <summary>Navigation state of the applied scenario (poses at Scenario.navigation.sim_time_s).</summary>
        public NavigationSim Nav { get; private set; }
        public WeatherRig Weather { get; private set; }
        public GpuInstanceMask GpuMask { get; private set; }
        public LightRig Lights { get; private set; }
        public SensorProcessor Sensor { get; private set; }
        public const string NightOvercastSky = "kloppenheim_07_puresky";
        public const string OvercastSky = "kloofendal_overcast_puresky";

        GameObject _root, _vesselRoot;
        VisualEnvironment _visualEnv;
        Exposure _exposure;
        Bloom _bloom;
        Fog _fog;
        CloudLayer _clouds;
        HDRISky _hdri;
        SkyLibrary _skies;
        // Calibration of HDRP's HDRISky rotation direction (verified with a backlit render, see DEV_LOG).
        public static float HdriRotationSign = -1f;

        public void Initialize()
        {
            Catalog = VesselCatalog.Load();
            Theme = HarborTheme.Load();
            if (Catalog == null) throw new InvalidOperationException("VesselCatalog missing from Resources (run PortCCTV bootstrap).");
            if (Theme == null) throw new InvalidOperationException("HarborTheme missing from Resources (run PortCCTV bootstrap).");

            _root = new GameObject("PortCCTV_World");
            Weather = new WeatherRig(_root.transform, Theme);
            GpuMask = new GpuInstanceMask(_root.transform, Theme.Get("instance_id")?.shader ?? Shader.Find("Hidden/PortCCTV/InstanceId"));
            Lights = new LightRig(_root.transform, Theme);
            Sensor = new SensorProcessor(Theme.Get("cctv_degrade")?.shader ?? Shader.Find("Hidden/PortCCTV/CctvDegrade"));
            _vesselRoot = new GameObject("Vessels");
            _vesselRoot.transform.SetParent(_root.transform, false);

            // Sun.
            var sunGo = new GameObject("Sun");
            sunGo.transform.SetParent(_root.transform, false);
            Sun = sunGo.AddComponent<Light>();
            Sun.type = LightType.Directional;
            SunHD = sunGo.AddComponent<HDAdditionalLightData>();
            Sun.shadows = LightShadows.Soft;

            // Volume with explicit overrides (everything image-relevant is set, not inherited from defaults).
            var volGo = new GameObject("GlobalVolume");
            volGo.transform.SetParent(_root.transform, false);
            Volume = volGo.AddComponent<Volume>();
            Volume.isGlobal = true;
            Volume.priority = 100;
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.name = "PortCCTV_RuntimeProfile";
            _visualEnv = profile.Add<VisualEnvironment>(true);
            _visualEnv.skyType.Override((int)SkyType.PhysicallyBased);
            _visualEnv.skyAmbientMode.Override(SkyAmbientMode.Dynamic);
            profile.Add<PhysicallyBasedSky>(true);
            _visualEnv.cloudType.Override((int)CloudType.CloudLayer);
            _clouds = profile.Add<CloudLayer>(true);
            _hdri = profile.Add<HDRISky>(true);
            _hdri.skyIntensityMode.Override(SkyIntensityMode.Lux);
            _skies = SkyLibrary.Load();
            var signArg = GetArg("-hdri-rot-sign");
            if (signArg != null && float.TryParse(signArg, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var sg)) HdriRotationSign = sg;
            var waterRendering = profile.Add<WaterRendering>(true);
            waterRendering.enable.Override(true); // HDRP 16: water is only drawn when this override is enabled
            _exposure = profile.Add<Exposure>(true);
            _exposure.mode.Override(ExposureMode.Fixed);
            _fog = profile.Add<Fog>(true);
            _fog.maximumHeight.Override(450f);
            _fog.maxFogDistance.Override(2500f); // keeps the upper sky blue; haze still builds over the sea and land
            _fog.baseHeight.Override(0f);
            _fog.enableVolumetricFog.Override(false);
            var tm = profile.Add<Tonemapping>(true);
            tm.mode.Override(TonemappingMode.ACES);
            _bloom = profile.Add<Bloom>(true);
            _bloom.intensity.Override(0.05f);
            var mb = profile.Add<MotionBlur>(true);
            mb.intensity.Override(0f);
            var vig = profile.Add<Vignette>(true);
            vig.intensity.Override(0f);
            Volume.sharedProfile = profile;

            // Water (HDRP built-in ocean surface; infinite, at y = harbor water reference).
            var waterGo = new GameObject("WaterSurface");
            waterGo.transform.SetParent(_root.transform, false);
            Water = waterGo.AddComponent<WaterSurface>();
            Water.surfaceType = WaterSurfaceType.OceanSeaLake;
            Water.geometryType = WaterGeometryType.Infinite;
            Water.caustics = false;
            Water.underWater = false;
            // Coastal blue-green look (engine colour parameters, not a measured water optical property).
            Water.refractionColor = new Color(0.10f, 0.42f, 0.48f);
            Water.scatteringColor = new Color(0.04f, 0.30f, 0.30f);
            Water.absorptionDistance = 8f;
            Water.ambientScattering = 0.25f;
            Water.heightScattering = 0.25f;
            Water.scriptInteractions = true;   // height queries for vessel wave motion

            // Fixed CCTV camera.
            var camGo = new GameObject("CCTVCamera");
            camGo.transform.SetParent(_root.transform, false);
            CctvCamera = camGo.AddComponent<Camera>();
            var hdCam = camGo.AddComponent<HDAdditionalCameraData>();
            hdCam.antialiasing = HDAdditionalCameraData.AntialiasingMode.None;
            CctvCamera.allowDynamicResolution = false;
            CctvCamera.usePhysicalProperties = false;

            // Development free camera (never used for captures).
            var devGo = new GameObject("DevCamera_NotCCTV");
            devGo.transform.SetParent(_root.transform, false);
            DevCamera = devGo.AddComponent<Camera>();
            var hdDev = devGo.AddComponent<HDAdditionalCameraData>();
            hdDev.antialiasing = HDAdditionalCameraData.AntialiasingMode.FastApproximateAntialiasing;
            DevCamera.fieldOfView = 50f;
            DevCamera.nearClipPlane = 0.3f;
            DevCamera.farClipPlane = 6000f;
            DevCamera.enabled = false;
        }

        readonly Dictionary<(Material, Color32), Material> _paintCache = new Dictionary<(Material, Color32), Material>();

        public void Dispose()
        {
            foreach (var m in _paintCache.Values) if (m != null) Util.SafeDestroy(m);
            _paintCache.Clear();
            if (_root != null) Util.SafeDestroy(_root);
            if (HarborRoot != null)
            {
                foreach (var mf in HarborRoot.GetComponentsInChildren<MeshFilter>(true))     // runtime-merged meshes are owned by the harbor
                    if (mf.sharedMesh != null && mf.sharedMesh.name.StartsWith("merged_")) Util.SafeDestroy(mf.sharedMesh);
                Util.SafeDestroy(HarborRoot);
            }
        }

        void EnsureHarbor(string harborId)
        {
            if (Harbor != null && Harbor.harbor_id == harborId) return;
            if (HarborRoot != null)
            {
                foreach (var mf in HarborRoot.GetComponentsInChildren<MeshFilter>(true))     // runtime-merged meshes are owned by the harbor
                    if (mf.sharedMesh != null && mf.sharedMesh.name.StartsWith("merged_")) UnityEngine.Object.DestroyImmediate(mf.sharedMesh);
                UnityEngine.Object.DestroyImmediate(HarborRoot);
            }
            Harbor = HarborLibrary.Get(harborId);
            HarborRoot = HarborBuilder.Build(Harbor, Theme, out var cols, out var objs);
            GroundWearDecals = GroundWear.Scatter(Harbor, Theme, HarborRoot.transform);
            Physics.SyncTransforms();
            VegetationResult = Vegetation.Scatter(Harbor, Theme, HarborRoot.transform, objs, cols);
            HarborColliders = cols;
            HarborObjects = objs;
            Water.transform.position = new Vector3(0, Harbor.water_reference_y, 0);
        }

        /// <summary>Applies a full scenario. Throws on unknown ids; returns placement reports (warnings never silently dropped).</summary>
        public List<PlacementReport> Apply(ScenarioConfig cfg)
        {
            Validate(cfg);
            Scenario = cfg.Clone();
            EnsureHarbor(cfg.harbor_id);
            ApplyCamera(cfg);
            ApplyEnvironment(cfg);
            var reports = SpawnVessels(cfg);
            ApplyWeatherParticles(cfg);
            Lights.Configure(this, cfg, EnvironmentPresetLibrary.Get(cfg.environment.preset_id), Environment.sun_elevation_deg);
            Environment.lights = Lights.Resolved;
            Lights.UpdateForCamera(CctvCamera, cfg.capture.height);
            return reports;
        }

        Bounds _landBounds; string _landBoundsFor;
        /// <summary>World bounds of the harbour geometry (cached per harbour): the area the wet / snow decals cover.</summary>
        Bounds HarborLandBounds()
        {
            if (_landBoundsFor == Harbor.harbor_id && HarborRoot != null) return _landBounds;
            var b = new Bounds(new Vector3(0, Harbor.water_reference_y, 0), Vector3.zero); bool any = false;
            if (HarborRoot != null)
                foreach (var r in HarborRoot.GetComponentsInChildren<Renderer>(true))
                {
                    if (r is ParticleSystemRenderer) continue;
                    if (r.bounds.size.x > 20000f || r.bounds.size.z > 20000f) continue;   // sea-bed / horizon planes
                    if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
                }
            _landBounds = b; _landBoundsFor = Harbor.harbor_id;
            return b;
        }

        /// <summary>Rain / snow particles in front of the CCTV camera, drifting with the navigation wind (deterministic).</summary>
        public void ApplyWeatherParticles(ScenarioConfig cfg)
        {
            var nav = cfg.navigation ?? new NavigationConfig();
            var windTo = NavigationSim.Dir(nav.wind_from_deg + 180f) * Mathf.Max(0f, nav.wind_speed_kn) * NavigationSim.Knot;
            Weather.Configure(cfg.environment.weather, windTo, Harbor, HarborLandBounds(), CctvCamera);
            Environment.weather = Weather.Resolved;
        }

        public static void Validate(ScenarioConfig cfg)
        {
            if (!Array.Exists(HarborLibrary.Ids, i => i == cfg.harbor_id)) throw new ArgumentException("Unknown harbor_id " + cfg.harbor_id);
            if (!cfg.camera_override.enabled && !CameraPresetLibrary.BelongsTo(cfg.camera_preset_id, cfg.harbor_id))
                throw new ArgumentException($"camera_preset_id {cfg.camera_preset_id} does not belong to harbor {cfg.harbor_id}");
            if (!EnvironmentPresetLibrary.Exists(cfg.environment.preset_id)) throw new ArgumentException("Unknown environment preset " + cfg.environment.preset_id);
            if (!WaterPresetLibrary.Exists(cfg.environment.water_preset_id)) throw new ArgumentException("Unknown water preset " + cfg.environment.water_preset_id);
            if (cfg.environment.lights != null && cfg.environment.lights != "auto" && cfg.environment.lights != "on" && cfg.environment.lights != "off")
                throw new ArgumentException("environment.lights must be auto | on | off");
            if (cfg.sensor != null && (cfg.sensor.jpeg_quality < 0 || cfg.sensor.jpeg_quality > 100)) throw new ArgumentException("sensor.jpeg_quality must be 0..100");
            if (cfg.appearance != null && !VesselPaint.IsValidMode(cfg.appearance.paint)) throw new ArgumentException("appearance.paint must be original | random | hull=#RRGGBB;super=#RRGGBB");
            var wxv = cfg.environment.weather;
            if (wxv != null && wxv.precipitation != "none" && wxv.precipitation != "rain" && wxv.precipitation != "snow")
                throw new ArgumentException("environment.weather.precipitation must be none | rain | snow");
            if (cfg.capture.mask_source != "gpu" && cfg.capture.mask_source != "cpu") throw new ArgumentException("capture.mask_source must be gpu | cpu");
            if (cfg.capture.width < 16 || cfg.capture.height < 16 || cfg.capture.width > 8192 || cfg.capture.height > 8192)
                throw new ArgumentException("capture width/height out of range");
            var ids = new HashSet<string>();
            foreach (var v in cfg.vessels)
            {
                if (string.IsNullOrWhiteSpace(v.object_id)) throw new ArgumentException("vessel object_id empty");
                if (!ids.Add(v.object_id)) throw new ArgumentException("duplicate vessel object_id " + v.object_id);
                if (!string.IsNullOrEmpty(v.paint) && !VesselPaint.IsValidMode(v.paint)) throw new ArgumentException($"vessel {v.object_id}: paint must be empty | original | random | hull=#RRGGBB;super=#RRGGBB");
            }
            if (cfg.vessels.Count > 254) throw new ArgumentException("at most 254 vessels (8-bit instance mask)");
            var nav = cfg.navigation ?? new NavigationConfig();
            if (nav.sim_time_s < 0f || nav.sim_time_s > 7200f) throw new ArgumentException("navigation.sim_time_s must be within 0..7200 s");
            if (nav.dt_s < 0.05f || nav.dt_s > 2f) throw new ArgumentException("navigation.dt_s must be within 0.05..2 s");
            foreach (var v in cfg.vessels)
            {
                var m = v.motion ?? new VesselMotion();
                if (m.mode != "none" && m.mode != "auto" && m.mode != "route" && m.mode != "roam" && m.mode != "anchor") throw new ArgumentException($"vessel {v.object_id}: motion.mode must be none | auto | roam | route | anchor");
                if (m.mode == "route" && (m.route == null || m.route.Count == 0)) throw new ArgumentException($"vessel {v.object_id}: motion.mode route needs waypoints");
                if (m.speed_kn < 0f || m.speed_kn > 40f) throw new ArgumentException($"vessel {v.object_id}: motion.speed_kn must be within 0..40");
            }
        }

        public void ApplyCamera(ScenarioConfig cfg)
        {
            CameraPreset p;
            if (cfg.camera_override.enabled)
                p = new CameraPreset
                {
                    id = "custom_override", display_name = "custom override", position = cfg.camera_override.position,
                    euler_deg = cfg.camera_override.euler_deg, vertical_fov_deg = cfg.camera_override.vertical_fov_deg,
                    mount_note = "scenario camera_override"
                };
            else p = CameraPresetLibrary.Get(cfg.camera_preset_id);
            ActiveCamera = p;
            var t = CctvCamera.transform;
            t.SetPositionAndRotation(p.position, Quaternion.Euler(p.euler_deg));
            CctvCamera.fieldOfView = p.vertical_fov_deg;
            CctvCamera.nearClipPlane = p.near_m;
            CctvCamera.farClipPlane = p.far_m;
            CctvCamera.aspect = (float)cfg.capture.width / cfg.capture.height;
        }

        public void ApplyEnvironment(ScenarioConfig cfg)
        {
            var e = EnvironmentPresetLibrary.Get(cfg.environment.preset_id);
            var w = WaterPresetLibrary.Get(cfg.environment.water_preset_id);
            float az = e.sun_azimuth_deg;
            if (e.sun_relative_to_camera) az = Mathf.Repeat(ActiveCamera.euler_deg.y + e.sun_azimuth_deg, 360f);
            float elev = e.sun_elevation_deg;
            var wx = cfg.environment.weather ?? new WeatherConfig();
            var sky = string.IsNullOrEmpty(e.sky_hdri) || _skies == null ? null : _skies.Find(e.sky_hdri);
            // overcast / precipitation: grey overcast HDRI (Poly Haven CC0), dim diffuse sun, lower exposure (engine values)
            string overcastKey = e.night ? NightOvercastSky : OvercastSky;
            if (wx.Overcast && _skies != null && _skies.Find(overcastKey) != null) sky = _skies.Find(overcastKey);
            float sunLux = e.sun_illuminance_lux * (wx.Overcast ? Mathf.Lerp(0.22f, 0.08f, wx.Precipitating ? wx.I : 0f) : 1f);
            float skyLux = e.sky_lux * (wx.Overcast ? Mathf.Lerp(0.7f, 0.45f, wx.Precipitating ? wx.I : 0f) : 1f);
            float ev = e.exposure_ev100 - (wx.Overcast ? 0.7f + (wx.Precipitating ? 0.4f * wx.I : 0f) : 0f);   // partial compensation: rain/snow scenes stay darker than clear ones
            float hdriRotation = 0f;
            if (sky != null && sky.cubemap != null)
            {
                elev = sky.sun_elevation_deg; // light follows the photographed sun
                hdriRotation = Mathf.Repeat(HdriRotationSign * (az - sky.sun_azimuth_cube_deg), 360f);
                _visualEnv.skyType.Override((int)SkyType.HDRI);
                _visualEnv.cloudType.Override(0);
                _hdri.hdriSky.Override(sky.cubemap);
                _hdri.rotation.Override(hdriRotation);
                _hdri.desiredLuxValue.Override(skyLux);
                _hdri.upperHemisphereLuxValue.Override(Mathf.Max(sky.upper_hemisphere_lux, 1e-3f));
            }
            else
            {
                _visualEnv.skyType.Override((int)SkyType.PhysicallyBased);
                _visualEnv.cloudType.Override((int)CloudType.CloudLayer);
            }
            Sun.transform.rotation = Quaternion.Euler(elev, az + 180f, 0f);
            SunHD.SetIntensity(sunLux, UnityEngine.Rendering.HighDefinition.LightUnit.Lux);
            Sun.useColorTemperature = false;
            Sun.color = e.night ? new Color(0.72f, 0.8f, 1f) : Color.white;     // moonlight is bluish
            _bloom.intensity.Override(e.night ? 0.15f : 0.05f);
            _exposure.fixedExposure.Override(ev);
            float mfp = e.fog_enabled ? e.fog_mean_free_path_m : e.aerial_haze_mfp_m;
            if (mfp <= 0f && (wx.Precipitating || wx.visibility_scale < 1f)) mfp = 3000f;
            mfp *= Mathf.Clamp(wx.visibility_scale, 0.05f, 4f) * wx.PrecipitationVisibilityFactor;
            _fog.enabled.Override(mfp > 0f);
            _fog.meanFreePath.Override(Mathf.Max(1f, mfp > 0f ? mfp : 400f));
            _clouds.opacity.Override(wx.Overcast ? 0.95f : e.cloud_opacity);

            Water.largeWindSpeed = w.large_wind_speed_kmh;
            Water.ripplesWindSpeed = w.ripples_wind_speed_kmh;
            Water.repetitionSize = w.repetition_size_m;
            Water.largeBand0Multiplier = w.large_band0_multiplier;
            Water.largeBand1Multiplier = w.large_band1_multiplier;

            Environment = new ResolvedEnvironment
            {
                preset_id = e.id, water_preset_id = w.id,
                sun_elevation_deg = elev, sun_azimuth_deg = az, sun_light_forward = Sun.transform.forward,
                sky = sky != null ? "hdri:" + sky.key : "physically_based", sky_hdri_rotation_deg = hdriRotation,
                sky_hdri_sha256 = sky != null ? sky.source_sha256 : "", sky_lux = sky != null ? skyLux : 0f,
                sun_illuminance_lux = sunLux, exposure_ev100 = ev,
                fog_enabled = mfp > 0f, fog_mean_free_path_m = mfp > 0f ? mfp : 0f, cloud_opacity = wx.Overcast ? 0.95f : e.cloud_opacity,
                water_reference_y = Harbor.water_reference_y, ground_wear_decals = GroundWearDecals,
                forest_billboard_trees = VegetationResult.trees, forest_harbor_extra_trees = VegetationResult.harbor_trees, forest_camera_zone_trees = VegetationResult.camera_zone_trees, town_billboard_trees = VegetationResult.town_trees, flat_land_trees = VegetationResult.flat_trees, near_3d_trees = VegetationResult.near_3d_trees, coastline_pieces = VegetationResult.coast_pieces, lighthouse_paths = VegetationResult.lighthouse_paths, lighthouse_landings = VegetationResult.lighthouse_landings, lighthouse_path_max_grade = VegetationResult.lighthouse_path_max_grade, lighthouse_path_length_m = VegetationResult.lighthouse_path_length_m,
                water_large_wind_speed_kmh = w.large_wind_speed_kmh, water_ripples_wind_speed_kmh = w.ripples_wind_speed_kmh,
                water_repetition_size_m = w.repetition_size_m, water_large_band0_multiplier = w.large_band0_multiplier,
                water_large_band1_multiplier = w.large_band1_multiplier, water_time_requested_s = cfg.environment.water_time_s,
            };
        }

        static string GetArg(string name)
        {
            var a = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < a.Length - 1; i++) if (a[i] == name) return a[i + 1];
            return null;
        }

        public void ClearVessels()
        {
            foreach (var v in Vessels) if (v.root != null) UnityEngine.Object.DestroyImmediate(v.root);
            Vessels.Clear();
        }

        public List<PlacementReport> SpawnVessels(ScenarioConfig cfg)
        {
            ClearVessels();
            var reports = new List<PlacementReport>();
            int idx = 1;
            Nav = new NavigationSim(Harbor, HarborColliders, CctvCamera, Catalog, cfg);
            Nav.AdvanceTo(cfg.navigation?.sim_time_s ?? 0f);
            foreach (var p0 in cfg.vessels)
            {
                // the placed vessel carries its pose at the simulated time; the scenario keeps the start pose
                var p = JsonUtility.FromJson<VesselPlacement>(JsonUtility.ToJson(p0));
                if (Nav.TryGetPose(p.object_id, out var xz, out var hd)) { p.x = xz.x; p.z = xz.y; p.heading_deg = hd; }
                var entry = Catalog.Find(p.asset_id) ?? throw new ArgumentException($"Unknown vessel asset_id '{p.asset_id}' for {p.object_id}");
                var go = UnityEngine.Object.Instantiate(entry.prefab, _vesselRoot.transform);
                go.name = p.object_id;
                var paint = VesselPaint.Apply(go, entry, p, cfg.appearance, _paintCache);
                float y = Harbor.water_reference_y - p.immersion_m - entry.geometry.hull_reference_min_y;
                go.transform.SetPositionAndRotation(new Vector3(p.x, y, p.z), Quaternion.Euler(0f, p.heading_deg, 0f));
                var pv = new PlacedVessel
                {
                    placement = p, entry = entry, root = go, instance_index = idx++, paint = paint,
                    colliders = go.GetComponentsInChildren<Collider>(true).ToList(),
                    footprint_xz = Geometry2D.OrientedRect(new Vector2(p.x, p.z), p.heading_deg,
                        new Vector2(entry.geometry.bounds_min.x, entry.geometry.bounds_min.z),
                        new Vector2(entry.geometry.bounds_max.x, entry.geometry.bounds_max.z)),
                    report = new PlacementReport { object_id = p.object_id, asset_id = p.asset_id }
                };
                Vessels.Add(pv);
                reports.Add(pv.report);
            }
            Physics.SyncTransforms();
            foreach (var v in Vessels) CheckPlacement(v);
            SpawnMooringLines();
            return reports;
        }

        GameObject _mooringRoot;
        Material _ropeMat;

        /// <summary>Bow and stern mooring lines for vessels lying within 8 m of a quay/pier/pontoon. Thin cylinders without colliders
        /// under the harbor root: they appear in RGB but are not vessel geometry (labels/masks ignore them).</summary>
        void SpawnMooringLines()
        {
            if (_mooringRoot != null) UnityEngine.Object.DestroyImmediate(_mooringRoot);
            _mooringRoot = new GameObject("MooringLines");
            _mooringRoot.transform.SetParent(_root.transform, false);
            if (_ropeMat == null)
            {
                _ropeMat = new Material(Shader.Find("HDRP/Lit")) { name = "M_rope_runtime" };
                _ropeMat.SetColor("_BaseColor", new Color(0.16f, 0.15f, 0.13f));
                _ropeMat.SetFloat("_Smoothness", 0.2f);
            }
            float waterY = Harbor.water_reference_y;
            var candidates = HarborColliders.Where(c => c != null && !c.gameObject.name.StartsWith("fender") && c.bounds.max.y > waterY + 0.3f && c.bounds.max.y < waterY + 12f).ToList();
            foreach (var v in Vessels)
            {
                if (v.placement.motion != null && v.placement.motion.Moving) continue;   // underway: no lines
                var g = v.entry.geometry;
                float L = g.bounds_max.x - g.bounds_min.x, H = g.bounds_max.y - g.bounds_min.y;
                float deckY = Mathf.Clamp(waterY + 0.22f * H, waterY + 0.5f, waterY + 12f);
                foreach (var xl in new[] { g.bounds_max.x - 0.08f * L, g.bounds_min.x + 0.08f * L })
                {
                    var p = v.root.transform.TransformPoint(new Vector3(xl, 0, 0)); p.y = deckY;
                    Collider best = null; var bestPt = Vector3.zero; float bestD = 8f;
                    foreach (var c in candidates)
                    {
                        if (c.bounds.SqrDistance(p) > bestD * bestD) continue;
                        var q = (c is BoxCollider || (c is MeshCollider mc && mc.convex)) ? c.ClosestPoint(p) : c.bounds.ClosestPoint(p);
                        float d = Vector2.Distance(new Vector2(q.x, q.z), new Vector2(p.x, p.z));
                        if (d < bestD) { bestD = d; best = c; bestPt = q; }
                    }
                    if (best == null) continue;
                    var quay = new Vector3(bestPt.x, Mathf.Min(best.bounds.max.y, deckY + 4f), bestPt.z);
                    var dir = quay - p; float len = dir.magnitude; if (len < 0.5f) continue;
                    var rope = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    UnityEngine.Object.DestroyImmediate(rope.GetComponent<Collider>());
                    rope.name = "mooring_" + v.placement.object_id;
                    rope.transform.SetParent(_mooringRoot.transform, false);
                    rope.transform.position = (p + quay) * 0.5f - Vector3.up * Mathf.Min(0.35f, 0.08f * len);
                    rope.transform.rotation = Quaternion.FromToRotation(Vector3.up, dir.normalized);
                    rope.transform.localScale = new Vector3(0.06f, len * 0.5f, 0.06f);
                    rope.GetComponent<MeshRenderer>().sharedMaterial = _ropeMat;
                }
            }
        }

        /// <summary>Advances navigation to time t (forward only) and moves the vessel objects; no placement re-check, no re-spawn.
        /// Used by the interactive playback. A capture re-applies the scenario at the same time, which gives the same poses.</summary>
        /// <summary>Updates the sensor settings of the applied scenario without re-spawning anything (UI sliders).</summary>
        public void SetSensor(SensorConfig s) { if (Scenario != null && s != null) Scenario.sensor = s.Clone(); }

        public void AdvanceNavigation(float t)
        {
            if (Nav == null) return;
            Nav.AdvanceTo(t);
            if (Scenario != null) Scenario.navigation.sim_time_s = Nav.Time;
            foreach (var v in Vessels)
            {
                if (!Nav.TryGetPose(v.placement.object_id, out var xz, out var hd)) continue;
                v.placement.x = xz.x; v.placement.z = xz.y; v.placement.heading_deg = hd;
                var pos = v.root.transform.position;
                v.root.transform.SetPositionAndRotation(new Vector3(xz.x, pos.y, xz.y), Quaternion.Euler(0f, hd, 0f));
                var g = v.entry.geometry;
                v.footprint_xz = Geometry2D.OrientedRect(xz, hd, new Vector2(g.bounds_min.x, g.bounds_min.z), new Vector2(g.bounds_max.x, g.bounds_max.z));
            }
        }

        /// <summary>Changes motion.mode on the given scenario vessels. A vessel that stops moving (mode "none" or "anchor") keeps the
        /// pose it has at the current simulated time: that pose becomes its new start pose. Without this the vessel would be
        /// recomputed from t = 0 and jump back to where it started. Returns how many vessels changed mode.</summary>
        public int SetMotionKeepingPose(ScenarioConfig cfg, IEnumerable<string> objectIds, string mode)
        {
            var ids = new HashSet<string>(objectIds);
            bool stops = mode == "none" || mode == "anchor";
            int n = 0;
            foreach (var p in cfg.vessels)
            {
                if (!ids.Contains(p.object_id)) continue;
                p.motion ??= new VesselMotion();
                if (p.motion.mode == mode) continue;
                if (stops && Nav != null && Nav.Time > 0f && (p.motion.Moving || p.motion.Anchored) &&
                    Nav.TryGetPose(p.object_id, out var xz, out var hd))
                { p.x = xz.x; p.z = xz.y; p.heading_deg = Mathf.Repeat(hd, 360f); }
                p.motion.mode = mode;
                n++;
            }
            return n;
        }

        /// <summary>Sets motion.mode = "auto" on every vessel of <paramref name="cfg"/> that is not lying alongside (uses the vessels
        /// currently placed from that scenario). Returns how many were switched.</summary>
        public int SetAutoNavigationForUnmoored(ScenarioConfig cfg, string mode = "auto")
        {
            int n = 0;
            foreach (var p in cfg.vessels)
            {
                var v = Vessels.FirstOrDefault(q => q.placement.object_id == p.object_id);
                if (v == null || IsAlongside(v)) continue;
                p.motion ??= new VesselMotion();
                if (p.motion.mode == "none" || (p.motion.mode != mode && p.motion.mode != "route" && p.motion.mode != "anchor")) { p.motion.mode = mode; n++; }
            }
            return n;
        }

        /// <summary>True when the vessel lies within 8 m of a quay / pier / pontoon (the same test that draws mooring lines).</summary>
        public bool IsAlongside(PlacedVessel v)
        {
            float waterY = Harbor.water_reference_y;
            var g = v.entry.geometry;
            var c = v.root.transform.TransformPoint((g.bounds_min + g.bounds_max) / 2f);
            var half = new Vector3((g.bounds_max.x - g.bounds_min.x) / 2f + 8f, 2f, (g.bounds_max.z - g.bounds_min.z) / 2f + 8f);
            var hits = Physics.OverlapBox(new Vector3(c.x, waterY + 1.5f, c.z), half, v.root.transform.rotation, ~0, QueryTriggerInteraction.Ignore);
            var set = new HashSet<Collider>(HarborColliders);
            return hits.Any(h => set.Contains(h) && h.bounds.max.y > waterY + 0.3f && h.bounds.max.y < waterY + 12f);
        }

        void CheckPlacement(PlacedVessel v)
        {
            var r = v.report;
            var c = new Vector2(v.placement.x, v.placement.z);
            r.water_region = Harbor.WaterRegionAt(c);
            if (r.water_region == null) r.warnings.Add("hull_centre_outside_water_regions");
            var realism = Harbor.RealismWarning(v.placement.asset_id, r.water_region);
            if (realism != null) r.warnings.Add(realism);
            // Sample the footprint (corners + edge midpoints) against the union of water regions.
            var fp = v.footprint_xz;
            for (int i = 0; i < 4; i++)
            {
                var a = fp[i]; var b = fp[(i + 1) % 4];
                if (!Harbor.InAnyWaterRegion(a) || !Harbor.InAnyWaterRegion((a + b) * 0.5f))
                {
                    r.warnings.Add("footprint_leaves_water_regions");
                    break;
                }
            }
            // Structures near the waterline band [-1, +1] m.
            var g = v.entry.geometry;
            var localCenter = new Vector3((g.bounds_min.x + g.bounds_max.x) / 2, 0, (g.bounds_min.z + g.bounds_max.z) / 2);
            var rot = Quaternion.Euler(0, v.placement.heading_deg, 0);
            var center = new Vector3(v.placement.x, Harbor.water_reference_y, v.placement.z) + rot * localCenter;
            var half = new Vector3((g.bounds_max.x - g.bounds_min.x) / 2, 1f, (g.bounds_max.z - g.bounds_min.z) / 2);
            var hits = Physics.OverlapBox(center, half, rot, ~0, QueryTriggerInteraction.Ignore);
            var harborSet = new HashSet<Collider>(HarborColliders);
            // Fenders exist to be touched: a hull resting on them is a berthed vessel, not a placement error.
            var structures = hits.Where(h => harborSet.Contains(h) && !h.gameObject.name.StartsWith("fender")).Select(h => h.gameObject.name).Distinct().ToList();
            if (structures.Count > 0) r.warnings.Add("intersects_structure:" + string.Join("|", structures));
            foreach (var o in Vessels)
            {
                if (o == v) continue;
                if (Geometry2D.ConvexOverlap(v.footprint_xz, o.footprint_xz)) r.warnings.Add("footprint_overlaps_vessel:" + o.placement.object_id);
            }
            if (v.placement.immersion_m < 0f) r.warnings.Add("negative_immersion_hull_reference_above_water");
            float d0 = v.entry.default_immersion_m;
            if (Mathf.Abs(v.placement.immersion_m - d0) > Mathf.Max(0.5f, 0.5f * Mathf.Abs(d0)))
                r.notes.Add($"immersion_far_from_asset_default:{v.placement.immersion_m:F2}_vs_{d0:F2}");
            if (!v.entry.geometry.hull_reference_found) r.warnings.Add("hull_reference_node_missing_used_render_min");
        }

        // ---------------- Wave motion ----------------
        /// <summary>Water surface height at world (x, z) from the HDRP script-interaction query; false if not available yet.</summary>
        public bool WaterHeightAt(Vector2 xz, out float y)
        {
            float wy = Harbor.water_reference_y;
            y = wy;
            if (Water == null) return false;
            var p = new WaterSearchParameters
            {
                startPositionWS = new float3(xz.x, wy, xz.y), targetPositionWS = new float3(xz.x, wy, xz.y),
                error = 0.01f, maxIterations = 8, includeDeformation = true, excludeSimulation = false,
            };
            if (!Water.ProjectPointOnWaterSurface(p, out var r) || !(r.error < 0.25f)) return false;
            y = r.projectedPositionWS.y;
            return !float.IsNaN(y) && Mathf.Abs(y - wy) < 10f;
        }

        /// <summary>Moves every vessel off its mean-plane pose by the local wave: heave = mean water height of five hull points
        /// (bow, stern, both sides at 40 % of length / beam, centre), pitch and roll from the bow-stern and port-starboard height
        /// differences. Sampling over the hull acts as a low-pass filter, so long ships barely move; tilt is capped at
        /// clamp(60 / length, 1, 8) degrees. With environment.vessel_wave_motion off it restores the mean-plane pose.
        /// Returns true when any vessel moved.</summary>
        public bool ApplyWaveMotion()
        {
            bool on = Scenario?.environment != null && Scenario.environment.vessel_wave_motion;
            Environment.vessel_wave_motion = on;
            bool any = false;
            foreach (var v in Vessels)
            {
                var g = v.entry.geometry;
                float baseY = Harbor.water_reference_y - v.placement.immersion_m - g.hull_reference_min_y;
                var yaw = Quaternion.Euler(0f, v.placement.heading_deg, 0f);
                var pos = v.root.transform.position;
                float heave = 0f, pitch = 0f, roll = 0f; string status = "off";
                if (on)
                {
                    float L = g.bounds_max.x - g.bounds_min.x, B = g.bounds_max.z - g.bounds_min.z;
                    float cx = 0.5f * (g.bounds_min.x + g.bounds_max.x), cz = 0.5f * (g.bounds_min.z + g.bounds_max.z);
                    var local = new[] { new Vector3(cx + 0.4f * L, 0, cz), new Vector3(cx - 0.4f * L, 0, cz), new Vector3(cx, 0, cz + 0.4f * B), new Vector3(cx, 0, cz - 0.4f * B), new Vector3(cx, 0, cz) };
                    var h = new float[5]; int okN = 0;
                    for (int i = 0; i < 5; i++)
                    {
                        var w = pos + yaw * local[i];
                        if (WaterHeightAt(new Vector2(w.x, w.z), out var y)) { h[i] = y - Harbor.water_reference_y; okN++; }
                    }
                    if (okN == 0) status = "unavailable";
                    else
                    {
                        status = okN == 5 ? "ok" : "partial";
                        heave = Mathf.Clamp(h.Sum() / okN, -2f, 2f);
                        float maxTilt = Mathf.Clamp(60f / Mathf.Max(L, 1f), 1f, 8f);
                        pitch = Mathf.Clamp(Mathf.Atan2(h[0] - h[1], 0.8f * L) * Mathf.Rad2Deg, -maxTilt, maxTilt);
                        roll = Mathf.Clamp(Mathf.Atan2(h[2] - h[3], 0.8f * B) * Mathf.Rad2Deg, -maxTilt, maxTilt);
                    }
                }
                // local +x = bow, +z = port: bow up = +rotation about z; port up = -rotation about x
                var rot = yaw * Quaternion.Euler(-roll, 0f, pitch);
                var np = new Vector3(pos.x, baseY + heave, pos.z);
                if (np != pos || rot != v.root.transform.rotation) any = true;
                v.root.transform.SetPositionAndRotation(np, rot);
                v.wave_status = status; v.wave_heave_m = heave; v.wave_pitch_deg = pitch; v.wave_roll_deg = roll;
            }
            if (any) Physics.SyncTransforms();
            return any;
        }

        // ---------------- Water time control ----------------
        static readonly FieldInfo s_SimField = typeof(WaterSurface).GetField("simulation", BindingFlags.NonPublic | BindingFlags.Instance);

        /// <summary>Returns the simulation time HDRP will use, or NaN if the simulation is not allocated yet.</summary>
        public float ReadWaterSimulationTime()
        {
            var sim = s_SimField?.GetValue(Water);
            if (sim == null) return float.NaN;
            var f = sim.GetType().GetField("simulationTime", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            return f != null ? (float)f.GetValue(sim) : float.NaN;
        }

        /// <summary>Sets the simulation time and freezes it (timeMultiplier = 0). Requires one rendered frame beforehand.</summary>
        public bool FreezeWaterAt(float t)
        {
            var sim = s_SimField?.GetValue(Water);
            if (sim == null) return false;
            var f = sim.GetType().GetField("simulationTime", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (f == null) return false;
            f.SetValue(sim, t);
            Water.timeMultiplier = 0f;
            Environment.water_time_frozen = true;
            Environment.water_time_actual_s = ReadWaterSimulationTime();
            return true;
        }

        public void UnfreezeWater()
        {
            Water.timeMultiplier = 1f;
            Environment.water_time_frozen = false;
        }
    }
}
