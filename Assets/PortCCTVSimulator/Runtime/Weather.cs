using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace PortCCTV
{
    /// <summary>Scenario weather on top of the environment preset (part of <see cref="EnvironmentConfig"/>). Engine values, not
    /// calibrated meteorology: intensity is a 0..1 visual scale, visibility_scale multiplies the HDRP fog mean free path.</summary>
    [Serializable]
    public class WeatherConfig
    {
        /// <summary>none | rain | snow</summary>
        public string precipitation = "none";
        /// <summary>0 (drizzle / light flurries) .. 1 (downpour / heavy snowfall).</summary>
        public float intensity = 0.5f;
        /// <summary>Grey overcast sky and dim sun. Always on while it rains or snows.</summary>
        public bool overcast = false;
        /// <summary>Multiplies the fog mean free path of the preset (1 = preset; 0.2 = much worse; 2 = clearer). Precipitation lowers
        /// it further.</summary>
        public float visibility_scale = 1f;
        /// <summary>Wet ground / decks 0..1 (darker, glossy). -1 = automatic (rain: 0.5 + 0.5 x intensity, otherwise 0).</summary>
        public float wetness = -1f;
        /// <summary>Snow lying on upward-facing surfaces 0..1. -1 = automatic (snow: 0.25 + 0.75 x intensity, otherwise 0).</summary>
        public float snow_cover = -1f;
        /// <summary>Particle random seed (precipitation is reproducible for a given scenario + camera pose).</summary>
        public int seed = 0;

        public bool Precipitating => (precipitation == "rain" || precipitation == "snow") && intensity > 0.001f;
        public bool Overcast => overcast || Precipitating;
        public float I => Mathf.Clamp01(intensity);
        public float Wetness => wetness >= 0f ? Mathf.Clamp01(wetness) : precipitation == "rain" && Precipitating ? 0.5f + 0.5f * I : 0f;
        public float SnowCover => snow_cover >= 0f ? Mathf.Clamp01(snow_cover) : precipitation == "snow" && Precipitating ? 0.25f + 0.75f * I : 0f;

        /// <summary>Fog mean free path factor from precipitation (rain: 0.65 .. 0.2, snow: 0.5 .. 0.12 of the preset). The falling
        /// particles in every distance band carry the rest of the effect.</summary>
        public float PrecipitationVisibilityFactor =>
            precipitation == "rain" && Precipitating ? Mathf.Lerp(0.65f, 0.2f, I) :
            precipitation == "snow" && Precipitating ? Mathf.Lerp(0.5f, 0.12f, I) : 1f;
    }

    /// <summary>Resolved weather written to capture metadata.</summary>
    [Serializable]
    public class ResolvedWeather
    {
        public string precipitation = "none";
        public float intensity;
        public bool overcast;
        public float visibility_scale = 1f;
        public float precipitation_visibility_factor = 1f;
        public float wetness;
        public float snow_cover;
        public int max_particles;
        public int layers;
        public float layer_far_m;
        public int water_ripple_particles;
        public float fall_speed_m_s;
        public Vector2 wind_drift_xz_m_s;
        public int seed;
        public string note = "Rain/snow particles in three distance bands up to layer_far_m in front of the camera, rain rings on the water, " +
                             "wet / snow decals on upward surfaces (visual). The CPU visible mask and the boxes ignore precipitation occlusion and " +
                             "the decals.";
    }

    /// <summary>
    /// Weather rig: falling rain / snow in three distance bands in front of the viewing camera (near: thin drops, mid and far: larger
    /// particles so they stay about a pixel wide), rain rings on the water near the camera, and two large downward decal projectors
    /// over the harbour that make upward-facing ground, quays and decks wet (dark + glossy) or snow-covered (angle-faded so walls stay
    /// clear). World-space particle systems with fixed seeds and prewarm, reset before every capture, so a capture of the same
    /// scenario and camera pose sees the same drops.
    /// </summary>
    public class WeatherRig
    {
        struct Band { public float near, far, sizeMul, share; }
        static readonly Band[] RainBands = { new Band { near = -8, far = 60, sizeMul = 1, share = 0.45f }, new Band { near = 60, far = 320, sizeMul = 4, share = 0.33f }, new Band { near = 320, far = 1200, sizeMul = 14, share = 0.22f } };
        static readonly Band[] SnowBands = { new Band { near = -8, far = 70, sizeMul = 1, share = 0.45f }, new Band { near = 70, far = 320, sizeMul = 3, share = 0.33f }, new Band { near = 320, far = 1000, sizeMul = 9, share = 0.22f } };

        readonly GameObject _root;
        readonly List<ParticleSystem> _rain = new List<ParticleSystem>(), _snow = new List<ParticleSystem>();
        readonly ParticleSystem _rings;
        readonly DecalProjector _wet, _snowCover;
        WeatherConfig _cfg = new WeatherConfig();
        HarborDefinition _harbor;
        Vector2 _wind;
        float _waterY;
        Vector3 _anchorPos; float _anchorYaw;
        ParticleSystem.Particle[] _buf = new ParticleSystem.Particle[0];
        public ResolvedWeather Resolved { get; private set; } = new ResolvedWeather();
        public bool Active => _cfg.Precipitating;

        public WeatherRig(Transform parent, HarborTheme theme)
        {
            _root = new GameObject("Weather");
            _root.transform.SetParent(parent, false);
            Material M(string k) => theme != null ? theme.Get(k) : null;
            for (int i = 0; i < RainBands.Length; i++) _rain.Add(Make("rain_" + i, M("weather_rain"), ParticleSystemRenderMode.Stretch));
            for (int i = 0; i < SnowBands.Length; i++) _snow.Add(Make("snow_" + i, M("weather_snow"), ParticleSystemRenderMode.Billboard));
            _rings = Make("rain_rings", M("weather_ripple"), ParticleSystemRenderMode.HorizontalBillboard);
            var tsa = _rings.textureSheetAnimation; tsa.enabled = true; tsa.numTilesX = 4; tsa.numTilesY = 4;
            tsa.animation = ParticleSystemAnimationType.WholeSheet; tsa.frameOverTime = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 0, 1, 1));
            _wet = MakeDecal("wet_ground", M("weather_wet_decal"), 0f, 180f);          // no angle fade
            _snowCover = MakeDecal("snow_cover", M("weather_snow_decal"), 35f, 60f);    // degrees from horizontal-up: walls stay clear
        }

        ParticleSystem Make(string name, Material mat, ParticleSystemRenderMode mode)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_root.transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true; main.prewarm = true; main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startSpeed = 0f; main.gravityModifier = 0f;
            main.scalingMode = ParticleSystemScalingMode.Shape;
            ps.useAutoRandomSeed = false;
            var shape = ps.shape; shape.enabled = true; shape.shapeType = ParticleSystemShapeType.Box;
            var vel = ps.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.renderMode = mode;
            r.alignment = ParticleSystemRenderSpace.View;
            r.sortMode = ParticleSystemSortMode.None;
            r.minParticleSize = 0f; r.maxParticleSize = 1f;
            go.SetActive(false);
            return ps;
        }

        DecalProjector MakeDecal(string name, Material mat, float startFade, float endFade)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_root.transform, false);
            go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);       // projects straight down
            var d = go.AddComponent<DecalProjector>();
            d.material = mat;
            d.drawDistance = 20000f;
            d.startAngleFade = startFade; d.endAngleFade = endFade;   // degrees (HDRP: needs Decal Layers)
            d.fadeFactor = 0f;
            go.SetActive(false);
            return d;
        }

        public void Destroy() { if (_root != null) UnityEngine.Object.DestroyImmediate(_root); }

        /// <summary>Configure for a scenario. <paramref name="windXZ"/> = wind velocity the air moves TO (m/s); <paramref name="land"/> =
        /// world bounds of the harbour geometry the wet / snow decals must cover.</summary>
        public void Configure(WeatherConfig cfg, Vector2 windXZ, HarborDefinition harbor, Bounds land, Camera cam)
        {
            _cfg = cfg ?? new WeatherConfig();
            _wind = windXZ; _harbor = harbor; _waterY = harbor != null ? harbor.water_reference_y : 0f;
            bool rain = _cfg.Precipitating && _cfg.precipitation == "rain";
            bool snow = _cfg.Precipitating && _cfg.precipitation == "snow";
            foreach (var p in _rain) p.gameObject.SetActive(rain);
            foreach (var p in _snow) p.gameObject.SetActive(snow);
            _rings.gameObject.SetActive(rain);
            Resolved = new ResolvedWeather
            {
                precipitation = _cfg.Precipitating ? _cfg.precipitation : "none", intensity = _cfg.Precipitating ? _cfg.I : 0f,
                overcast = _cfg.Overcast, visibility_scale = _cfg.visibility_scale,
                precipitation_visibility_factor = _cfg.PrecipitationVisibilityFactor, seed = _cfg.seed,
                wetness = _cfg.Wetness, snow_cover = _cfg.SnowCover,
            };
            PlaceDecal(_wet, land, _cfg.Wetness);
            PlaceDecal(_snowCover, land, _cfg.SnowCover);
            if (cam != null) ResetDeterministic(cam);
        }

        static void PlaceDecal(DecalProjector d, Bounds land, float amount)
        {
            bool on = amount > 0.001f && land.size.sqrMagnitude > 1f;
            d.gameObject.SetActive(on);
            if (!on) return;
            float depth = land.size.y + 40f;
            d.transform.position = new Vector3(land.center.x, land.max.y + 20f, land.center.z);
            d.size = new Vector3(land.size.x + 40f, land.size.z + 40f, depth);
            d.pivot = new Vector3(0f, 0f, depth * 0.5f);
            d.fadeFactor = amount;
        }

        /// <summary>Re-anchor in front of <paramref name="cam"/> and restart the prewarmed particles from the fixed seeds.</summary>
        public void ResetDeterministic(Camera cam)
        {
            if (!_cfg.Precipitating || cam == null) return;
            bool rain = _cfg.precipitation == "rain";
            var bands = rain ? RainBands : SnowBands;
            var systems = rain ? _rain : _snow;
            float I = _cfg.I;
            var ct = cam.transform;
            _anchorPos = ct.position; _anchorYaw = ct.eulerAngles.y;
            float camH = Mathf.Max(2f, ct.position.y - _waterY);
            float vHalf = cam.fieldOfView * 0.5f, hHalf = Mathf.Atan(Mathf.Tan(vHalf * Mathf.Deg2Rad) * cam.aspect) * Mathf.Rad2Deg;
            float pitchDown = Mathf.DeltaAngle(0f, ct.eulerAngles.x);
            float fall = rain ? Mathf.Lerp(6.5f, 9.5f, I) : Mathf.Lerp(0.9f, 1.4f, I);
            int total = rain ? Mathf.RoundToInt(Mathf.Lerp(4000, 30000, I)) : Mathf.RoundToInt(Mathf.Lerp(5000, 50000, I));
            var yawQ = Quaternion.Euler(0, _anchorYaw, 0);
            int sum = 0;
            for (int b = 0; b < bands.Length; b++)
            {
                var ps = systems[b]; var band = bands[b];
                // box covering the camera frustum between band.near and band.far; its top is above the highest visible ray there
                float halfW = Mathf.Tan(hHalf * Mathf.Deg2Rad) * band.far + 12f;
                float visTop = Mathf.Tan(Mathf.Clamp(vHalf - pitchDown, 0f, 80f) * Mathf.Deg2Rad) * band.far;
                float top = Mathf.Min(camH + 12f + visTop, 600f);
                float depth = band.far - band.near;
                float life = top / (fall * 1.1f);
                ps.transform.SetPositionAndRotation(new Vector3(ct.position.x, _waterY + top, ct.position.z) + yawQ * new Vector3(0, 0, band.near + depth * 0.5f), yawQ);
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = ps.main;
                main.duration = life; main.startLifetime = life;
                main.maxParticles = Mathf.Max(200, Mathf.RoundToInt(total * band.share));
                main.startSize3D = false;
                float s = band.sizeMul;
                main.startSize = rain ? new ParticleSystem.MinMaxCurve(0.04f * s, 0.075f * s) : new ParticleSystem.MinMaxCurve(0.09f * s, 0.22f * s);
                var em = ps.emission; em.enabled = true; em.rateOverTime = main.maxParticles / life;
                var shape = ps.shape; shape.scale = new Vector3(2f * halfW, 0.1f, depth);
                var vel = ps.velocityOverLifetime;
                float windShare = rain ? 0.55f : 0.9f, jitter = rain ? 0.2f : 0.5f;
                vel.x = new ParticleSystem.MinMaxCurve(_wind.x * windShare - jitter, _wind.x * windShare + jitter);
                vel.y = new ParticleSystem.MinMaxCurve(-fall * 1.1f, -fall * 0.9f);
                vel.z = new ParticleSystem.MinMaxCurve(_wind.y * windShare - jitter, _wind.y * windShare + jitter);
                var noise = ps.noise; noise.enabled = !rain; noise.strength = 0.35f * s; noise.frequency = 0.25f / s; noise.scrollSpeed = 0.1f;
                var r = ps.GetComponent<ParticleSystemRenderer>();
                if (rain) { r.velocityScale = 0.055f * s; r.lengthScale = 1f; }
                ps.randomSeed = (uint)(_cfg.seed * 7919 + b * 101 + (rain ? 17 : 31));
                ps.Simulate(0.02f, true, true, true);      // prewarm = one full lifetime, then a tiny fixed step; deterministic
                ps.Pause(true);
                sum += main.maxParticles;
                if (b == 0) { Resolved.fall_speed_m_s = fall; Resolved.wind_drift_xz_m_s = _wind * windShare; }
            }
            Resolved.max_particles = sum; Resolved.layers = bands.Length; Resolved.layer_far_m = bands[bands.Length - 1].far;
            if (rain) ResetRings(ct, hHalf, I);
        }

        void ResetRings(Transform ct, float hHalf, float I)
        {
            // expanding rings on the water within ~140 m (beyond that they are sub-pixel); 4x4 sheet: ring grows and fades
            const float far = 140f;
            var yawQ = Quaternion.Euler(0, _anchorYaw, 0);
            float halfW = Mathf.Tan(hHalf * Mathf.Deg2Rad) * far + 6f;
            _rings.transform.SetPositionAndRotation(new Vector3(ct.position.x, _waterY + 0.03f, ct.position.z) + yawQ * new Vector3(0, 0, far * 0.5f), yawQ);
            _rings.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = _rings.main;
            main.duration = 0.7f; main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 0.8f);
            main.maxParticles = Mathf.RoundToInt(Mathf.Lerp(1500, 9000, I));
            main.startSize = new ParticleSystem.MinMaxCurve(0.4f, 1.0f);
            var em = _rings.emission; em.enabled = true; em.rateOverTime = main.maxParticles / 0.65f;
            var shape = _rings.shape; shape.scale = new Vector3(2f * halfW, 0.01f, far + 8f);
            var vel = _rings.velocityOverLifetime; vel.x = 0f; vel.y = 0f; vel.z = 0f;
            _rings.randomSeed = (uint)(_cfg.seed * 7919 + 977);
            _rings.Simulate(0.02f, true, true, true);
            _rings.Pause(true);
            KeepRingsOnWater();
            Resolved.water_ripple_particles = _rings.particleCount;
        }

        /// <summary>Rings only where there is water (not on quays, decks or land).</summary>
        void KeepRingsOnWater()
        {
            if (_harbor == null) return;
            int n = _rings.particleCount;
            if (_buf.Length < n) _buf = new ParticleSystem.Particle[n];
            n = _rings.GetParticles(_buf);
            int k = 0;
            for (int i = 0; i < n; i++)
                if (_harbor.InAnyWaterRegion(new Vector2(_buf[i].position.x, _buf[i].position.z))) _buf[k++] = _buf[i];
            _rings.SetParticles(_buf, k);
        }

        /// <summary>Interactive playback: advance the paused systems; re-anchor when the viewing camera moved far.</summary>
        public void Tick(Camera cam, float dt)
        {
            if (!_cfg.Precipitating || cam == null) return;
            if (Vector3.Distance(cam.transform.position, _anchorPos) > 25f || Mathf.Abs(Mathf.DeltaAngle(cam.transform.eulerAngles.y, _anchorYaw)) > 35f)
            { ResetDeterministic(cam); return; }
            float step = Mathf.Clamp(dt, 0f, 0.1f);
            foreach (var ps in _cfg.precipitation == "rain" ? _rain : _snow) ps.Simulate(step, true, false, false);
            if (_cfg.precipitation == "rain") { _rings.Simulate(step, true, false, false); KeepRingsOnWater(); }
        }

        /// <summary>Test hook: positions of the live particles of one distance band (0 = near).</summary>
        public Vector3[] ParticlePositions(int band = 0)
        {
            var ps = (_cfg.precipitation == "rain" ? _rain : _snow)[band];
            var buf = new ParticleSystem.Particle[ps.particleCount];
            int n = ps.GetParticles(buf);
            var p = new Vector3[n];
            for (int i = 0; i < n; i++) p[i] = buf[i].position;
            return p;
        }

        public int RingCount => _rings.gameObject.activeSelf ? _rings.particleCount : 0;
        public Vector3[] RingPositions()
        {
            var buf = new ParticleSystem.Particle[_rings.particleCount];
            int n = _rings.GetParticles(buf);
            var p = new Vector3[n];
            for (int i = 0; i < n; i++) p[i] = buf[i].position;
            return p;
        }
        public float WetDecalFade => _wet.gameObject.activeSelf ? _wet.fadeFactor : 0f;
        public float SnowDecalFade => _snowCover.gameObject.activeSelf ? _snowCover.fadeFactor : 0f;
    }
}
