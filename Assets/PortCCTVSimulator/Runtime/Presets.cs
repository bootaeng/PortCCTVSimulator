using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace PortCCTV
{
    [Serializable]
    public class CameraPreset
    {
        public string id;
        public string display_name;
        public Vector3 position;
        /// <summary>Unity Euler (pitch down positive, yaw clockwise from +Z, roll).</summary>
        public Vector3 euler_deg;
        public float vertical_fov_deg;
        public float near_m = 0.3f;
        public float far_m = 6000f;
        public string mount_note;
        /// <summary>IR illuminator of this camera (2026-11-12): "" = from the mount height (>= 35 m: "long" narrow long-range
        /// beam, else "wide" short dome beam); beam angle (deg), range (m), relative power; 0 = profile default.</summary>
        public string ir_profile = "";
        public float ir_beam_deg = 0f, ir_range_m = 0f, ir_power = 0f;
    }

    /// <summary>Fixed CCTV poses per map. Values are design choices, not surveyed installs.</summary>
    public static class CameraPresetLibrary
    {
        public const string DefaultId = "cam_A_quay_mast";

        /// <summary>Cameras of the legacy/mixed harbors (generic_harbor_01/02).</summary>
        public static readonly CameraPreset[] Generic =
        {
            new CameraPreset
            {
                id = "cam_A_quay_mast", display_name = "A - north quay mast (12 m)",
                position = new Vector3(-60f, 14.5f, 62f), euler_deg = new Vector3(8f, 160f, 0f),
                vertical_fov_deg = 42f, mount_note = "pole 12 m above north quay deck (deck y=2.5)"
            },
            new CameraPreset
            {
                id = "cam_B_breakwater_head", display_name = "B - east breakwater head (8 m)",
                position = new Vector3(117.5f, 11.5f, -236f), euler_deg = new Vector3(6f, 320f, 0f),
                vertical_fov_deg = 38f, mount_note = "pole 8 m above east breakwater crest (crest y=3.5)"
            },
            new CameraPreset
            {
                id = "cam_C_warehouse_roof", display_name = "C - warehouse roof, wide (22 m)",
                position = new Vector3(45f, 22.6f, 116f), euler_deg = new Vector3(10f, 190f, 0f),
                vertical_fov_deg = 55f, mount_note = "mast 10 m above east warehouse roof (roof y=12)"
            },
            new CameraPreset
            {
                id = "cam_D_tower_overview", display_name = "D - apartment tower roof, overview (78 m)",
                position = new Vector3(230f, 84f, 270f), euler_deg = new Vector3(10f, 212f, 0f),
                vertical_fov_deg = 50f, mount_note = "mast on a 74 m apartment tower roof north-east of the port (generic_harbor_02)"
            },
        };

        public static CameraPreset[] ForHarbor(string harborId) => harborId switch
        {
            ContainerTerminal01.Id => ContainerTerminal01.Cameras,
            FishingPort01.Id => FishingPort01.Cameras,
            Marina01.Id => Marina01.Cameras,
            _ => Generic,
        };

        public static CameraPreset[] All =>
            Generic.Concat(ContainerTerminal01.Cameras).Concat(FishingPort01.Cameras).Concat(Marina01.Cameras).ToArray();

        public static CameraPreset Get(string id) =>
            All.FirstOrDefault(p => p.id == id) ?? throw new ArgumentException("Unknown camera preset: " + id);

        public static bool Exists(string id) => All.Any(p => p.id == id);

        public static bool BelongsTo(string id, string harborId) => ForHarbor(harborId).Any(p => p.id == id);
    }

    [Serializable]
    public class EnvironmentPreset
    {
        public string id;
        public string display_name;
        public float sun_elevation_deg;
        /// <summary>Sun azimuth (deg, clockwise from +Z). If sun_relative_to_camera, an offset from the camera yaw.</summary>
        public float sun_azimuth_deg;
        public bool sun_relative_to_camera;
        public float sun_illuminance_lux;
        /// <summary>Fixed exposure EV100 (Exposure override, mode Fixed). Engine value, not a calibrated camera.</summary>
        public float exposure_ev100;
        public bool fog_enabled;
        /// <summary>HDRP fog mean free path (m). Engine parameter; NOT a meteorological visibility.</summary>
        public float fog_mean_free_path_m;
        /// <summary>Background aerial haze (fog mean free path, m) used when fog_enabled is false; 0 = none.</summary>
        public float aerial_haze_mfp_m = 2200f;
        /// <summary>HDRP cloud layer opacity (0 = no clouds). Only used with the physically based sky.</summary>
        public float cloud_opacity = 0.4f;
        /// <summary>SkyLibrary key of a Poly Haven HDRI sky; empty = physically based sky. With an HDRI, the light elevation is the
        /// HDRI's measured sun elevation and the HDRI is rotated so its sun sits at this preset's azimuth.</summary>
        public string sky_hdri = "";
        /// <summary>HDRI sky intensity (HDRP 'desired lux' of the upper hemisphere).</summary>
        public float sky_lux = 40000f;
        /// <summary>Night preset: artificial lights on in "auto", moonlight colour, night overcast sky for rain / snow.</summary>
        public bool night;
        public string note;
    }

    public static class EnvironmentPresetLibrary
    {
        public const string DefaultId = "day_clear";

        public static readonly EnvironmentPreset[] All =
        {
            new EnvironmentPreset
            {
                id = "day_clear", display_name = "Day, clear",
                sun_elevation_deg = 48f, sun_azimuth_deg = 140f, sun_illuminance_lux = 100000f,
                exposure_ev100 = 13.0f, aerial_haze_mfp_m = 3200f, cloud_opacity = 0.35f,
                sky_hdri = "kloofendal_48d_partly_cloudy_puresky", sky_lux = 35000f, note = "partly cloudy midday sky (Poly Haven CC0 HDRI)"
            },
            new EnvironmentPreset
            {
                id = "day_backlit", display_name = "Day, backlit (sun ahead of camera)",
                sun_elevation_deg = 18f, sun_azimuth_deg = 0f, sun_relative_to_camera = true,
                sun_illuminance_lux = 70000f, exposure_ev100 = 13.5f, aerial_haze_mfp_m = 1800f, cloud_opacity = 0.5f,
                sky_hdri = "syferfontein_18d_clear_puresky", sky_lux = 25000f, note = "sun azimuth = camera yaw"
            },
            new EnvironmentPreset
            {
                id = "dusk_low_light", display_name = "Dusk, low light",
                sun_elevation_deg = 1f, sun_azimuth_deg = 250f, sun_illuminance_lux = 4000f,
                exposure_ev100 = 10.5f, aerial_haze_mfp_m = 1600f, cloud_opacity = 0.45f,
                sky_hdri = "syferfontein_1d_clear_puresky", sky_lux = 3000f, note = "sun 1 deg above the horizon"
            },
            new EnvironmentPreset
            {
                id = "light_haze", display_name = "Day, light haze",
                sun_elevation_deg = 28f, sun_azimuth_deg = 140f, sun_illuminance_lux = 50000f,
                exposure_ev100 = 13.0f, fog_enabled = true, fog_mean_free_path_m = 350f, cloud_opacity = 0.7f,
                sky_hdri = "kloofendal_28d_misty_puresky", sky_lux = 25000f,
                note = "fog mean free path 350 m (engine parameter, not visibility)"
            },
            new EnvironmentPreset
            {
                id = "night_clear", display_name = "Night, clear (moonlight, harbour lights)", night = true,
                sun_elevation_deg = 35f, sun_azimuth_deg = 200f, sun_illuminance_lux = 0.25f,
                exposure_ev100 = -1.5f, aerial_haze_mfp_m = 2600f, cloud_opacity = 0.2f,
                sky_hdri = "kloppenheim_02_puresky", sky_lux = 0.15f,
                note = "moonlit clear night sky (Poly Haven CC0 HDRI); directional light = moon (measured HDRI brightest point), 0.25 lux; lamps and navigation lights on"
            },
            new EnvironmentPreset
            {
                id = "night_overcast", display_name = "Night, overcast (town glow)", night = true,
                sun_elevation_deg = 30f, sun_azimuth_deg = 200f, sun_illuminance_lux = 0.03f,
                exposure_ev100 = -1.5f, aerial_haze_mfp_m = 1800f, cloud_opacity = 0.9f,
                sky_hdri = "kloppenheim_07_puresky", sky_lux = 0.15f,
                note = "overcast night lit from below by town light (Poly Haven CC0 HDRI); lamps and navigation lights on"
            },
        };

        public static EnvironmentPreset Get(string id) =>
            All.FirstOrDefault(p => p.id == id) ?? throw new ArgumentException("Unknown environment preset: " + id);

        public static bool Exists(string id) => All.Any(p => p.id == id);
    }

    [Serializable]
    public class WaterPreset
    {
        public string id;
        public string display_name;
        /// <summary>HDRP WaterSurface.largeWindSpeed (km/h). Engine spectrum parameter, not a Beaufort calibration.</summary>
        public float large_wind_speed_kmh;
        public float ripples_wind_speed_kmh;
        public float repetition_size_m;
        public float large_band0_multiplier;
        public float large_band1_multiplier;
        /// <summary>HDRP simulation foam (whitecaps) and its amount; harbour water mask on by default (2026-11-12: moderate and
        /// rough; calm / light chop keep the earlier unmasked, foam-free surface so older captures reproduce).</summary>
        public bool foam;
        public float foam_amount;
        public bool harbor_mask_default;
        /// <summary>Render-only distance fade of the two large bands (HDRP "Automatic" fades the agitation band from ~300 m and the
        /// swell from ~1.5 km on the GPU only; the CPU height query that moves the vessels has no fade). &gt; 0: custom fade start
        /// (m) for both large bands, so the rendered waves and the vessels agree within the view; 0 = HDRP automatic (older presets).</summary>
        public float large_fade_start_m;
    }

    public static class WaterPresetLibrary
    {
        public const string DefaultId = "calm";

        public static readonly WaterPreset[] All =
        {
            new WaterPreset
            {
                id = "calm", display_name = "Calm harbor",
                large_wind_speed_kmh = 5f, ripples_wind_speed_kmh = 4f, repetition_size_m = 300f,
                large_band0_multiplier = 0.2f, large_band1_multiplier = 0.5f
            },
            new WaterPreset
            {
                id = "light_chop", display_name = "Light chop",
                large_wind_speed_kmh = 25f, ripples_wind_speed_kmh = 10f, repetition_size_m = 300f,
                large_band0_multiplier = 0.5f, large_band1_multiplier = 1.0f
            },
            new WaterPreset
            {
                id = "moderate", display_name = "Moderate sea",
                large_wind_speed_kmh = 50f, ripples_wind_speed_kmh = 18f, repetition_size_m = 500f,
                large_band0_multiplier = 0.8f, large_band1_multiplier = 1.0f, foam = true, foam_amount = 0.35f, harbor_mask_default = true, large_fade_start_m = 2500f
            },
            new WaterPreset
            {
                id = "rough", display_name = "Rough sea",
                large_wind_speed_kmh = 85f, ripples_wind_speed_kmh = 30f, repetition_size_m = 800f,
                large_band0_multiplier = 1.0f, large_band1_multiplier = 1.0f, foam = true, foam_amount = 0.6f, harbor_mask_default = true, large_fade_start_m = 2500f
            },
        };

        public static WaterPreset Get(string id) =>
            All.FirstOrDefault(p => p.id == id) ?? throw new ArgumentException("Unknown water preset: " + id);

        public static bool Exists(string id) => All.Any(p => p.id == id);
    }
}
