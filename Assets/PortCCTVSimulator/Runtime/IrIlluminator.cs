using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace PortCCTV
{
    /// <summary>Resolved IR illuminator of one capture / preview frame (metadata "sensor.ir").</summary>
    [Serializable]
    public class IrRecord
    {
        public bool on;
        public string profile;                 // wide (fixed wide-angle / dome) | long (long-range external illuminator)
        public float wavelength_nm = 850f;
        public float beam_deg, range_m, power, intensity_cd, inner_percent;
        public Vector3 offset_camera_local;    // illuminator position relative to the camera (x right, y up, z forward)
        public float mount_drop_m;             // plus this far straight down (external illuminator lower on the pole)
        public int mount_unshadowed;           // renderers within IrIlluminator.MountClearM of the light (mast / housing) casting no IR shadow
        public bool dome;
        public float dome_reflection;
        public float ir_exposure_ev100;
        public int renderers_in_range, materials_cloned;
        public float render_ms;
        public string model = "Separate HDRP render of the CCTV view with only the IR spot light (inverse-square, smooth range window, " +
                              "soft cone edge, 2048 px shadow map), sky / ambient / fog off, near-IR grey materials (classes below) and " +
                              "near-black water body (specular glints and foam kept); added to the visible night image before the sensor " +
                              "noise. Engine approximation of an 850 nm illuminator, not a calibrated radiometric model.";
        public List<NirClass> nir_reflectance = new List<NirClass>();
        // checks filled by CaptureService (sampled IR-only image vs ray-cast geometry)
        public IrChecks checks;
    }

    [Serializable]
    public class NirClass { public string cls; public float reflectance; public string basis; }

    [Serializable]
    public class IrChecks
    {
        public int samples, near_lit, shadowed, far_sea;
        public float near_lit_mean, shadowed_mean, far_sea_mean, outside_beam_mean;
        public float shadow_leak_ratio;        // shadowed mean / near lit mean (0 = no light behind occluders)
        public List<string> shadow_samples = new List<string>();   // first shadowed samples: point, occluder, IR value
        public string method = "64x36 pixel grid; camera ray -> first collider hit (or the mean water plane); a hit inside 0.8 x beam and range is " +
                               "'shadowed' when the rays from the illuminator centre and from four points 0.3 m around it all meet another collider " +
                               "before it (umbra; penumbra samples are skipped); IR-only image luminance per class.";
    }

    /// <summary>
    /// 850 nm IR illuminator for night CCTV (2026-11-12). The IR component is a second HDRP render of the CCTV view by a
    /// dedicated camera that sees its own volume (layer 31: no sky, no fog, linear tone mapping, fixed exposure EV 0): every
    /// other light is switched off, only an HDRP spot light at the camera (offset per profile) lights the scene with shadows,
    /// renderers within reach use near-IR grey copies of their materials, and the water body is near black (absorbing) while its
    /// specular glints and foam remain. The beam "range" is the rated distance (usable image); the light itself reaches
    /// ReachFactor x range (inverse square + HDRP smooth range window) and the IR camera's far clip ends there, so nothing
    /// beyond it adds light.
    /// Per camera: wide fixed / dome cameras get a wide short beam, high long-range cameras a narrow long beam; the scenario can
    /// override power, beam angle, range and dome.
    /// </summary>
    public sealed class IrIlluminator
    {
        public const int VolumeLayer = 31;
        public const float IrEv100 = 0f;
        /// <summary>Renderers this close to the illuminator (its own mast / camera housing) cast no IR shadow.</summary>
        public const float MountClearM = 3f;
        /// <summary>The light reaches this multiple of the rated range (beyond it: no IR at all).</summary>
        public const float ReachFactor = 2f;
        /// <summary>Intensity (cd) per power unit and per square metre of rated range.</summary>
        public const float IntensityPerRange2 = 0.5f;
        // the rig (camera, spot light, volume) is created on the first IR render only, so scenes without IR contain exactly the
        // same objects as before this feature (byte-identical captures of older scenarios)
        readonly Transform _parent;
        GameObject _root;
        Camera _cam;
        Light _light;
        HDAdditionalLightData _lightHd;
        Volume _volume;
        readonly Dictionary<(Material, string), Material> _clones = new Dictionary<(Material, string), Material>();
        Shader _lit;
        RenderTexture _rt;

        /// <summary>Near-IR reflectance per surface class: visual approximations (no measured spectra for these assets).</summary>
        public static readonly NirClass[] Classes =
        {
            new NirClass { cls = "vegetation", reflectance = 0.55f, basis = "visual approximation: leaves and grass reflect strongly in the NIR plateau" },
            new NirClass { cls = "concrete", reflectance = 0.40f, basis = "visual approximation" },
            new NirClass { cls = "asphalt", reflectance = 0.12f, basis = "visual approximation" },
            new NirClass { cls = "rock_sand", reflectance = 0.35f, basis = "visual approximation" },
            new NirClass { cls = "metal", reflectance = 0.45f, basis = "visual approximation (painted / galvanised steel)" },
            new NirClass { cls = "rubber", reflectance = 0.06f, basis = "visual approximation: carbon-black rubber (RIB / CRRC tubes, fenders, tyres) absorbs NIR" },
            new NirClass { cls = "glass", reflectance = 0.05f, basis = "visual approximation" },
            new NirClass { cls = "hull_paint", reflectance = -1f, basis = "visual approximation: 0.25 + 0.6 x visible luminance (paints stay fairly bright in NIR)" },
            new NirClass { cls = "default", reflectance = -1f, basis = "visual approximation: 0.15 + 0.6 x visible luminance" },
            new NirClass { cls = "water", reflectance = 0.02f, basis = "visual approximation: body near black (HDRP water scattering / refraction colours ~0.01); specular and foam unchanged" },
        };

        public IrIlluminator(Transform parent) { _parent = parent; }

        void EnsureRig()
        {
            if (_root != null) return;
            var parent = _parent;
            _lit = Shader.Find("HDRP/Lit");
            _root = new GameObject("IrIlluminator");
            _root.transform.SetParent(parent, false);
            var camGo = new GameObject("IrCamera_NotCCTV");
            camGo.transform.SetParent(_root.transform, false);
            _cam = camGo.AddComponent<Camera>();
            var hd = camGo.AddComponent<HDAdditionalCameraData>();
            hd.antialiasing = HDAdditionalCameraData.AntialiasingMode.None;
            hd.volumeLayerMask = 1 << VolumeLayer;
            hd.clearColorMode = HDAdditionalCameraData.ClearColorMode.Color;
            hd.backgroundColorHDR = Color.black;
            _cam.allowDynamicResolution = false;
            _cam.enabled = false;

            var lightGo = new GameObject("IrSpot");
            lightGo.transform.SetParent(_root.transform, false);
            _light = lightGo.AddComponent<Light>();
            _light.type = LightType.Spot;
            _light.shadows = LightShadows.Soft;
            _light.color = Color.white;
            _lightHd = lightGo.AddComponent<HDAdditionalLightData>();
            _lightHd.EnableShadows(true);
            _lightHd.SetShadowResolution(2048);
            _lightHd.shadowNearPlane = 0.2f;
            _light.enabled = false;

            var volGo = new GameObject("IrVolume") { layer = VolumeLayer };
            volGo.transform.SetParent(_root.transform, false);
            _volume = volGo.AddComponent<Volume>();
            _volume.isGlobal = true;
            _volume.priority = 1000;
            var p = ScriptableObject.CreateInstance<VolumeProfile>();
            p.name = "PortCCTV_IrProfile";
            var ve = p.Add<VisualEnvironment>(true);
            ve.skyType.Override(0);                                   // no sky: black background, no sky ambient
            ve.cloudType.Override(0);
            ve.skyAmbientMode.Override(SkyAmbientMode.Static);
            var wr = p.Add<WaterRendering>(true); wr.enable.Override(true);
            var ex = p.Add<Exposure>(true); ex.mode.Override(ExposureMode.Fixed); ex.fixedExposure.Override(IrEv100);
            var tm = p.Add<Tonemapping>(true); tm.mode.Override(TonemappingMode.None);
            var bl = p.Add<Bloom>(true); bl.intensity.Override(0f);
            var fog = p.Add<Fog>(true); fog.enabled.Override(false);
            var il = p.Add<IndirectLightingController>(true);
            il.indirectDiffuseLightingMultiplier.Override(0f); il.reflectionLightingMultiplier.Override(0f);
            var mb = p.Add<MotionBlur>(true); mb.intensity.Override(0f);
            var vg = p.Add<Vignette>(true); vg.intensity.Override(0f);
            _volume.sharedProfile = p;
        }

        // ---------------------------------------------------------------- configuration
        public struct Beam { public string profile; public float beamDeg, rangeM, power, intensityCd, mountDrop; public Vector3 offset; public bool dome; }

        /// <summary>Camera defaults (preset fields win when set) plus scenario overrides.</summary>
        public static Beam Resolve(CameraPreset cam, SensorConfig s)
        {
            bool longRange = cam != null && (cam.ir_profile == "long" || (string.IsNullOrEmpty(cam.ir_profile) && cam.position.y >= 35f));
            var b = new Beam
            {
                profile = longRange ? "long" : "wide",
                beamDeg = cam != null && cam.ir_beam_deg > 0f ? cam.ir_beam_deg : longRange ? 26f : 80f,
                rangeM = cam != null && cam.ir_range_m > 0f ? cam.ir_range_m : longRange ? 400f : 70f,
                power = cam != null && cam.ir_power > 0f ? cam.ir_power : longRange ? 2f : 1f,
                offset = longRange ? new Vector3(0f, -0.9f, 0.1f) : new Vector3(0f, -0.25f, 0.05f),
                dome = !longRange,
            };
            if (s != null)
            {
                if (s.ir_beam_deg > 0f) b.beamDeg = Mathf.Clamp(s.ir_beam_deg, 5f, 160f);
                if (s.ir_range_m > 0f) b.rangeM = Mathf.Clamp(s.ir_range_m, 5f, 2000f);
                if (s.ir_power > 0f) b.power = s.ir_power;
                if (s.ir_dome >= 0) b.dome = s.ir_dome > 0;
                b.mountDrop = Mathf.Clamp(s.ir_mount_drop_m, 0f, 20f);
            }
            // rated range R: I = power x 0.5 cd/m^2 x R^2, i.e. power 1 puts ~0.5 lux on a surface facing the beam at R (a 0.4
            // reflector ~ linear 0.05 with the IR exposure; 4 x brighter at R / 2). Wide 70 m -> 2,450 cd; long 400 m, power 2 ->
            // 160,000 cd (long-range illuminators are rated for a usable image at their distance).
            b.intensityCd = b.power * IntensityPerRange2 * b.rangeM * b.rangeM;
            return b;
        }

        /// <summary>Strength of the dome-window reflection of the camera's own IR (drops on the dome glow).</summary>
        public static float DomeReflection(Beam b) => b.dome && b.mountDrop < 0.3f ? Mathf.Clamp(0.2f * b.power, 0.04f, 0.5f) : 0f;   // only an illuminator inside the dome lights its drops

        public static string ClassOf(Renderer r, Material m, string vesselCategory)
        {
            string n = ((m != null ? m.name : "") + " " + r.name).ToLowerInvariant();
            if (n.Contains("glass") || n.Contains("glazing") || n.Contains("window") || n.Contains("lens")) return "glass";
            if (vesselCategory != null)
            {
                if (vesselCategory == "rib" && !(n.Contains("metal") || n.Contains("steel") || n.Contains("console") || n.Contains("seat") || n.Contains("deck") || n.Contains("engine") || n.Contains("motor")))
                    return "rubber";
                return n.Contains("tyre") || n.Contains("tire") || n.Contains("fender") || n.Contains("rubber") ? "rubber" : "hull_paint";
            }
            if (n.Contains("tyre") || n.Contains("tire") || n.Contains("fender") || n.Contains("rubber")) return "rubber";
            if (n.Contains("grass") || n.Contains("forest") || n.Contains("tree") || n.Contains("leaf") || n.Contains("leaves") || n.Contains("meadow") ||
                n.Contains("field") || n.Contains("scrub") || n.Contains("shrub") || n.Contains("hill") || n.Contains("canopy") || n.Contains("bush")) return "vegetation";
            if (n.Contains("asphalt") || n.Contains("road")) return "asphalt";
            if (n.Contains("rock") || n.Contains("sand") || n.Contains("shore") || n.Contains("cliff")) return "rock_sand";
            if (n.Contains("concrete") || n.Contains("quay") || n.Contains("pier") || n.Contains("breakwater") || n.Contains("tetrapod") || n.Contains("wall") ||
                n.Contains("retaining") || n.Contains("sidewalk") || n.Contains("paving") || n.Contains("pontoon") || n.Contains("kerb") || n.Contains("stair")) return "concrete";
            if (n.Contains("steel") || n.Contains("metal") || n.Contains("galv") || n.Contains("crane") || n.Contains("corrugated") || n.Contains("rail") || n.Contains("container")) return "metal";
            return "default";
        }

        static float VisibleLuma(Material m)
        {
            Color c = Color.white;
            if (m.HasProperty("_BaseColor")) c = m.GetColor("_BaseColor");
            else if (m.HasProperty("baseColorFactor")) c = m.GetColor("baseColorFactor");
            else if (m.HasProperty("_Color")) c = m.GetColor("_Color");
            return Mathf.Clamp01(0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b);
        }

        static float Reflectance(string cls, Material m)
        {
            foreach (var c in Classes) if (c.cls == cls && c.reflectance >= 0f) return c.reflectance;
            float l = m != null ? VisibleLuma(m) : 0.5f;
            return cls == "hull_paint" ? 0.25f + 0.6f * l : 0.15f + 0.6f * l;
        }

        Material CloneFor(Material src, string cls)
        {
            if (src == null) return null;
            if (_clones.TryGetValue((src, cls), out var m)) return m;
            float rho = Reflectance(cls, src);
            bool unlit = src.shader == null || src.shader.name.IndexOf("unlit", StringComparison.OrdinalIgnoreCase) >= 0;
            m = unlit ? new Material(_lit) : new Material(src);
            m.name = src.name + "_nir_" + cls;
            m.hideFlags = HideFlags.DontSave;
            // textured materials keep their texture as detail; the texture's mean is taken as ~0.6 (grey multiplier rho / 0.6)
            bool textured = (m.HasProperty("_BaseColorMap") && m.GetTexture("_BaseColorMap") != null) || (m.HasProperty("baseColorTexture") && m.GetTexture("baseColorTexture") != null);
            float g = textured ? Mathf.Min(rho / 0.6f, 1.6f) : rho;
            var grey = new Color(g, g, g, 1f);
            foreach (var p in new[] { "_BaseColor", "baseColorFactor", "_Color" })
                if (m.HasProperty(p)) { var a = m.GetColor(p).a; m.SetColor(p, new Color(grey.r, grey.g, grey.b, a)); }
            foreach (var p in new[] { "_EmissiveColor", "emissiveFactor", "_EmissionColor", "_EmissiveColorLDR" })
                if (m.HasProperty(p)) m.SetColor(p, Color.black);
            if (m.HasProperty("_EmissiveColorMap")) m.SetTexture("_EmissiveColorMap", null);
            if (m.HasProperty("emissiveTexture")) m.SetTexture("emissiveTexture", null);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);
            if (m.HasProperty("metallicFactor")) m.SetFloat("metallicFactor", 0f);
            if (m.shader == _lit) HDMaterial.ValidateMaterial(m);
            _clones[(src, cls)] = m;
            return m;
        }

        // ---------------------------------------------------------------- render
        /// <summary>Renders the IR component of the view of <paramref name="cctv"/> at W x H (linear, float). Only the IR spot
        /// lights the scene. All scene lights, particles and the water body colours are restored afterwards.</summary>
        public RenderTexture Render(SimWorld w, Camera cctv, Beam b, int W, int H, IrRecord rec)
        {
            var sw = Stopwatch.StartNew();
            EnsureRig();
            if (_rt == null || _rt.width != W || _rt.height != H)
            {
                if (_rt != null) { _rt.Release(); Util.SafeDestroy(_rt); }
                _rt = new RenderTexture(new RenderTextureDescriptor(W, H, GraphicsFormat.R16G16B16A16_SFloat, 24) { msaaSamples = 1 }) { name = "PortCCTV_IrComponent" };
                _rt.Create();
            }
            _cam.transform.SetPositionAndRotation(cctv.transform.position, cctv.transform.rotation);
            _cam.fieldOfView = cctv.fieldOfView; _cam.aspect = (float)W / H;
            _cam.nearClipPlane = cctv.nearClipPlane;
            _cam.farClipPlane = Mathf.Min(cctv.farClipPlane, b.rangeM * ReachFactor);
            var lp = LightPose(cctv, b).pos;
            _light.transform.SetPositionAndRotation(lp, cctv.transform.rotation);
            _light.spotAngle = b.beamDeg;
            _light.innerSpotAngle = b.beamDeg * 0.45f;
            _light.range = b.rangeM * ReachFactor;
            _lightHd.SetIntensity(b.intensityCd, LightUnit.Candela);
            _lightHd.SetRange(b.rangeM * ReachFactor);
            _lightHd.innerSpotPercent = 45f;

            // switch every other light / particle off, swap materials of renderers within reach
            var lights = UnityEngine.Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).Where(l => l != _light && l.enabled).ToList();
            var parts = UnityEngine.Object.FindObjectsByType<ParticleSystemRenderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).Where(r => r.enabled).ToList();
            var vesselCat = new Dictionary<Transform, string>();
            foreach (var v in w.Vessels) if (v.root != null) vesselCat[v.root.transform] = v.entry.category == "rib" || v.placement.asset_id == "crrc" ? "rib" : v.entry.category;
            var reach = new Bounds(lp, Vector3.one * (2f * b.rangeM * ReachFactor));
            var swapped = new List<(Renderer r, Material[] mats)>();
            // the camera's own mast / housing next to the illuminator must not shadow the whole beam (a real illuminator is
            // mounted clear of it): renderers within MountClearM of the light cast no IR shadow
            var unshadowed = new List<(Renderer r, ShadowCastingMode m)>();
            var water = w.Water;
            var wRefr = water.refractionColor; var wScat = water.scatteringColor; float wAmb = water.ambientScattering, wHgt = water.heightScattering;
            try
            {
                foreach (var l in lights) l.enabled = false;
                foreach (var r in parts) r.enabled = false;
                foreach (var r in UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                {
                    if (!r.enabled || r is ParticleSystemRenderer || !r.bounds.Intersects(reach)) continue;
                    string cat = null;
                    for (var t = r.transform; t != null; t = t.parent) if (vesselCat.TryGetValue(t, out cat)) break;
                    var mats = r.sharedMaterials;
                    var nm = new Material[mats.Length];
                    for (int i = 0; i < mats.Length; i++) nm[i] = CloneFor(mats[i], ClassOf(r, mats[i], cat));
                    swapped.Add((r, mats));
                    r.sharedMaterials = nm;
                    if (r.bounds.SqrDistance(lp) < MountClearM * MountClearM && r.shadowCastingMode != ShadowCastingMode.Off)
                    { unshadowed.Add((r, r.shadowCastingMode)); r.shadowCastingMode = ShadowCastingMode.Off; }
                }
                water.refractionColor = new Color(0.01f, 0.01f, 0.01f);
                water.scatteringColor = new Color(0.005f, 0.005f, 0.005f);
                water.ambientScattering = 0f; water.heightScattering = 0f;
                _light.enabled = true;
                _cam.targetTexture = _rt;
                _cam.Render();
            }
            finally
            {
                _light.enabled = false;
                _cam.targetTexture = null;
                foreach (var (r, mats) in swapped) if (r != null) r.sharedMaterials = mats;
                foreach (var (r, m) in unshadowed) if (r != null) r.shadowCastingMode = m;
                foreach (var l in lights) if (l != null) l.enabled = true;
                foreach (var r in parts) if (r != null) r.enabled = true;
                water.refractionColor = wRefr; water.scatteringColor = wScat; water.ambientScattering = wAmb; water.heightScattering = wHgt;
            }
            sw.Stop();
            if (rec != null)
            {
                rec.on = true; rec.profile = b.profile; rec.beam_deg = b.beamDeg; rec.range_m = b.rangeM; rec.power = b.power; rec.intensity_cd = b.intensityCd;
                rec.inner_percent = 45f; rec.offset_camera_local = b.offset; rec.mount_drop_m = b.mountDrop; rec.dome = b.dome; rec.dome_reflection = DomeReflection(b);
                rec.ir_exposure_ev100 = IrEv100; rec.renderers_in_range = swapped.Count; rec.mount_unshadowed = unshadowed.Count; rec.materials_cloned = _clones.Count;
                rec.render_ms = (float)sw.Elapsed.TotalMilliseconds;
                rec.nir_reflectance = Classes.ToList();
            }
            return _rt;
        }

        /// <summary>Illuminator world position / forward for a camera (shadow checks).</summary>
        public static (Vector3 pos, Vector3 fwd) LightPose(Camera cctv, Beam b) => (cctv.transform.TransformPoint(b.offset) + Vector3.down * b.mountDrop, cctv.transform.forward);

        public void Destroy()
        {
            foreach (var m in _clones.Values) Util.SafeDestroy(m);
            _clones.Clear();
            if (_rt != null) { _rt.Release(); Util.SafeDestroy(_rt); }
            if (_root != null) Util.SafeDestroy(_root);
        }
    }
}
