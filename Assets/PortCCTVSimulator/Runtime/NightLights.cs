using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace PortCCTV
{
    /// <summary>Resolved artificial lighting written to metadata.</summary>
    [Serializable]
    public class ResolvedLights
    {
        public string mode = "auto";
        public bool on;
        public int harbor_lamps, harbor_lights_enabled, vessel_nav_lights, vessel_deck_lights;
        public string note = "Navigation lights after COLREG Annex I arcs (masthead 225 deg, sidelights 112.5 deg, stern 135 deg, all-round); " +
            "mount points found by ray casts on each hull (approximate, not the ship's real fittings). Bulbs are drawn at least ~2 px wide so " +
            "distant lights stay visible (a rendering floor, not photometry). Bulbs are excluded from vessel labels. Intensities are engine values.";
    }

    /// <summary>
    /// Night lighting: harbour street / breakwater lamps and high-mast floodlights (lights added to the existing lamp props), and
    /// vessel navigation / deck lights. Navigation-light bulbs are switched by the camera bearing so only the lights facing the
    /// camera show (the screens of real lanterns), and are scaled to stay at least a couple of pixels wide.
    /// </summary>
    public sealed class LightRig
    {
        public const string BulbPrefix = "navlight_";
        const float NavNits = 600f;   // bulb luminance (engine value; saturates at night exposure, faint by day)

        static readonly Color Red = new Color(1f, 0.08f, 0.03f), Green = new Color(0.1f, 1f, 0.45f), White = new Color(1f, 0.93f, 0.82f);
        static readonly Color Lamp = new Color(1f, 0.82f, 0.6f), Flood = new Color(1f, 0.95f, 0.88f), Deck = new Color(1f, 0.78f, 0.52f);

        sealed class Lit
        {
            public Transform anchor;          // light position follows the parent (vessel / lamp)
            public Light light;
            public Renderer bulb;
            public float bulbDiam;            // physical diameter (m)
            public string kind;               // masthead | starboard | port | stern | allround | deck | lamp | flood
            public PlacedVessel vessel;
            public float arcFrom, arcTo;      // relative bearing range (deg, 0 = bow, + = starboard); all-round: -180..180
        }

        struct Mount { public string kind; public Vector3 local; public float diam; }

        readonly Transform _root;
        readonly Material _bulbBase;
        readonly Dictionary<Color, Material> _bulbMats = new Dictionary<Color, Material>();
        readonly List<Lit> _harbor = new List<Lit>();
        readonly List<Lit> _vessel = new List<Lit>();
        GameObject _harborFor;
        static readonly Dictionary<string, Mount[]> s_Mounts = new Dictionary<string, Mount[]>();
        static readonly Dictionary<string, (float y, float hull)[]> s_Profiles = new Dictionary<string, (float, float)[]>();
        public ResolvedLights Resolved { get; private set; } = new ResolvedLights();
        public bool On => Resolved.on;

        public LightRig(Transform parent, HarborTheme theme)
        {
            var go = new GameObject("NightLights");
            go.transform.SetParent(parent, false);
            _root = go.transform;
            _bulbBase = theme != null ? theme.Get("light_bulb") : null;
        }

        static float LengthOf(PlacedVessel v) => v.entry.geometry.bounds_max.x - v.entry.geometry.bounds_min.x;

        public static bool IsBulb(Renderer r) => r != null && r.name.StartsWith(BulbPrefix, StringComparison.Ordinal);

        public static bool ShouldBeOn(string mode, EnvironmentPreset e, float sunElevation)
        {
            if (mode == "on") return true;
            if (mode == "off") return false;
            return e.night || sunElevation < 4f;
        }

        Material BulbMat(Color c, float nits)
        {
            var key = c * nits;
            if (_bulbMats.TryGetValue(key, out var m)) return m;
            m = _bulbBase != null ? new Material(_bulbBase) : new Material(Shader.Find("HDRP/Lit"));
            m.name = "bulb_" + _bulbMats.Count; m.hideFlags = HideFlags.DontSave;
            m.SetColor("_BaseColor", Color.black);
            m.SetColor("_EmissiveColor", c.linear * nits);
            m.SetFloat("_EmissiveExposureWeight", 1f);
            _bulbMats[key] = m;
            return m;
        }

        Lit Make(Transform parent, Vector3 worldPos, string kind, Color c, float lumen, float range, float bulbDiam, float bulbNits, bool spotDown = false, float spotAngle = 120f)
        {
            var go = new GameObject("light_" + kind);
            go.transform.SetParent(parent, true);
            go.transform.position = worldPos;
            var lit = new Lit { anchor = go.transform, kind = kind, bulbDiam = bulbDiam, arcFrom = -180f, arcTo = 180f };
            if (lumen > 0f)
            {
                var l = go.AddComponent<Light>();
                l.type = spotDown ? LightType.Spot : LightType.Point;
                if (spotDown) { go.transform.rotation = Quaternion.Euler(90f, 0f, 0f); l.spotAngle = spotAngle; l.innerSpotAngle = spotAngle * 0.6f; }
                l.useColorTemperature = false;
                l.color = c;
                l.range = range;
                l.shadows = LightShadows.None;
                var hd = go.AddComponent<HDAdditionalLightData>();
                hd.SetIntensity(lumen, LightUnit.Lumen);
                hd.affectsVolumetric = false;
                lit.light = l;
            }
            if (bulbDiam > 0f)
            {
                var b = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Util.SafeDestroy(b.GetComponent<Collider>());
                b.name = BulbPrefix + kind;
                b.transform.SetParent(go.transform, false);
                b.transform.localScale = Vector3.one * bulbDiam;
                var r = b.GetComponent<MeshRenderer>();
                r.sharedMaterial = BulbMat(c, bulbNits);
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
                lit.bulb = r;
            }
            return lit;
        }

        /// <summary>Turns lights on/off for the applied scenario; vessel lights are rebuilt for the new vessel instances.</summary>
        public void Configure(SimWorld w, ScenarioConfig cfg, EnvironmentPreset e, float sunElevation)
        {
            string mode = string.IsNullOrEmpty(cfg.environment.lights) ? "auto" : cfg.environment.lights;
            bool on = ShouldBeOn(mode, e, sunElevation);
            _vessel.Clear();                                    // the old vessel instances (and their light children) are gone
            if (on) BuildHarbor(w.HarborRoot);
            foreach (var l in _harbor) if (l.anchor != null) l.anchor.gameObject.SetActive(on);
            int nav = 0, deck = 0;
            if (on)
                foreach (var v in w.Vessels)
                {
                    bool alongside = w.IsAlongside(v);
                    bool anchored = v.placement.motion != null && v.placement.motion.Anchored;
                    foreach (var m in MountsFor(v))
                    {
                        bool use = m.kind == "deck" || (alongside ? false : anchored ? m.kind.StartsWith("anchor") : !m.kind.StartsWith("anchor"));
                        if (!use) continue;
                        var wp = v.root.transform.TransformPoint(m.local);
                        Lit lit = m.kind switch
                        {
                            "deck" => Make(v.root.transform, wp, "deck", Deck, 1500f + 30f * LengthOf(v), 12f + 0.35f * LengthOf(v), 0f, 0f),
                            "port" => Make(v.root.transform, wp, "port", Red, 150f, 8f, m.diam, NavNits),
                            "starboard" => Make(v.root.transform, wp, "starboard", Green, 150f, 8f, m.diam, NavNits),
                            _ => Make(v.root.transform, wp, m.kind, White, 200f, 10f, m.diam, NavNits),
                        };
                        lit.vessel = v;
                        (lit.arcFrom, lit.arcTo) = m.kind switch
                        {
                            "masthead" or "masthead_aft" => (-112.5f, 112.5f),
                            "starboard" => (0f, 112.5f),
                            "port" => (-112.5f, 0f),
                            "stern" => (112.5f, 247.5f),
                            _ => (-180f, 180f),
                        };
                        if (m.kind == "deck") deck++; else nav++;
                        _vessel.Add(lit);
                    }
                }
            Resolved = new ResolvedLights
            {
                mode = mode, on = on, harbor_lamps = _harbor.Count, vessel_nav_lights = nav, vessel_deck_lights = deck,
                harbor_lights_enabled = on ? _harbor.Count(l => l.light != null) : 0,
            };
        }

        void BuildHarbor(GameObject harborRoot)
        {
            if (harborRoot == null || _harborFor == harborRoot) return;
            foreach (var l in _harbor) if (l.anchor != null) Util.SafeDestroy(l.anchor.gameObject);
            _harbor.Clear();
            _harborFor = harborRoot;
            foreach (Transform group in harborRoot.transform)
            {
                if (group.name == "street_light" || group.name == "breakwater_light")
                    foreach (Transform lamp in group)
                    {
                        var p = LampBulbPosition(lamp);
                        var lit = Make(lamp, p, "lamp", Lamp, 3000f, 25f, 0.45f, 400f, spotDown: true, spotAngle: 140f);
                        _harbor.Add(lit);
                    }
                else if (group.name == "light_mast")
                    foreach (Transform head in group)
                    {
                        var b = head.GetComponent<Renderer>()?.bounds ?? new Bounds(head.position, Vector3.one);
                        var lit = Make(head, new Vector3(b.center.x, b.min.y - 0.3f, b.center.z), "flood", Flood, 30000f, 100f, 1.6f, 600f, spotDown: true, spotAngle: 150f);
                        _harbor.Add(lit);
                    }
            }
        }

        /// <summary>Bulb of the Poly Haven street lamp: centre of the submesh whose material is called "*bulb*".</summary>
        static Vector3 LampBulbPosition(Transform lamp)
        {
            foreach (var r in lamp.GetComponentsInChildren<MeshRenderer>())
            {
                var mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length && i < mf.sharedMesh.subMeshCount; i++)
                    if (mats[i] != null && mats[i].name.IndexOf("bulb", StringComparison.OrdinalIgnoreCase) >= 0)
                        return r.transform.TransformPoint(mf.sharedMesh.GetSubMesh(i).bounds.center);
            }
            var rs = lamp.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return lamp.position + Vector3.up * 6f;
            var bb = rs[0].bounds; foreach (var rr in rs) bb.Encapsulate(rr.bounds);
            return new Vector3(bb.center.x, bb.max.y - 0.15f * bb.size.y, bb.center.z);
        }

        /// <summary>Highest hull surface (root space y) above the local point (x, z), from ray casts on the vessel's colliders.</summary>
        static float TopAt(PlacedVessel v, float x, float z, float yStart)
        {
            var t = v.root.transform;
            var ray = new Ray(t.TransformPoint(new Vector3(x, yStart, z)), -t.up);
            float best = float.NaN;
            foreach (var c in v.colliders)
                if (c != null && c.Raycast(ray, out var hit, yStart * 2f + 200f))
                {
                    float y = t.InverseTransformPoint(hit.point).y;
                    if (float.IsNaN(best) || y > best) best = y;
                }
            return best;
        }

        /// <summary>Navigation / anchor / deck light mount points of a hull (root space, bow +X, starboard -Z), cached per asset.</summary>
        static Mount[] MountsFor(PlacedVessel v)
        {
            if (s_Mounts.TryGetValue(v.placement.asset_id, out var cached)) return cached;
            var g = v.entry.geometry;
            Physics.SyncTransforms();
            float x0 = g.bounds_min.x, x1 = g.bounds_max.x, L = Mathf.Max(0.5f, x1 - x0), zc = (g.bounds_min.z + g.bounds_max.z) / 2f;
            float halfB = Mathf.Max(0.3f, (g.bounds_max.z - g.bounds_min.z) / 2f), top = g.bounds_max.y + 5f;
            const int N = 40;
            var prof = new float[N];
            float waterLocal = g.hull_reference_min_y + v.placement.immersion_m;           // root-space water level (approx.)
            for (int i = 0; i < N; i++)
            {
                float y = TopAt(v, x0 + (i + 0.5f) / N * L, zc, top);
                prof[i] = float.IsNaN(y) ? waterLocal : y;
            }
            float X(int i) => x0 + (i + 0.5f) / N * L;
            int ArgMax(float a, float b) { int best = -1; for (int i = 0; i < N; i++) { float f = (i + 0.5f) / N; if (f < a || f > b) continue; if (best < 0 || prof[i] > prof[best]) best = i; } return Mathf.Max(0, best); }
            float At(float f) => prof[Mathf.Clamp((int)(f * N), 0, N - 1)];
            bool small = L < 12f, big = L >= 50f;
            float lift = small ? 0.25f : big ? 1.5f : 0.6f, diam = small ? 0.12f : big ? 0.45f : 0.25f;
            var m = new List<Mount>();
            int iBridge = ArgMax(0.25f, 0.8f);
            if (small)
            {
                int i = ArgMax(0.2f, 0.85f);
                m.Add(new Mount { kind = "allround", local = new Vector3(X(i), prof[i] + lift, zc), diam = diam });
            }
            else
            {
                int iF = big ? ArgMax(0.6f, 0.92f) : ArgMax(0.3f, 0.85f);
                var fwd = new Vector3(X(iF), prof[iF] + lift, zc);
                m.Add(new Mount { kind = "masthead", local = fwd, diam = diam });
                if (big)
                {
                    int iA = ArgMax(0.1f, 0.55f);
                    m.Add(new Mount { kind = "masthead_aft", local = new Vector3(X(iA), Mathf.Max(prof[iA] + lift, fwd.y + 4.5f), zc), diam = diam });
                }
                m.Add(new Mount { kind = "stern", local = new Vector3(x0 + 0.02f * L, At(0.02f) + lift * 0.6f, zc), diam = diam });
            }
            // sidelights: deck edge abreast the bridge (small craft: near the bow)
            float xs = small ? x0 + 0.75f * L : X(iBridge);
            foreach (var side in new[] { ("starboard", -1f), ("port", 1f) })
            {
                float z = zc, y = At((xs - x0) / L);
                foreach (var f in new[] { 0.95f, 0.8f, 0.6f, 0.4f })
                {
                    float yy = TopAt(v, xs, zc + side.Item2 * f * halfB, top);
                    if (!float.IsNaN(yy)) { z = zc + side.Item2 * f * halfB; y = yy; break; }
                }
                m.Add(new Mount { kind = side.Item1, local = new Vector3(xs, y + lift * 0.6f, z), diam = diam });
            }
            // anchor lights (all-round): forward, and aft for big ships
            m.Add(new Mount { kind = "anchor_fwd", local = new Vector3(x0 + 0.92f * L, At(0.92f) + lift * 1.5f, zc), diam = diam });
            if (big) m.Add(new Mount { kind = "anchor_aft", local = new Vector3(x0 + 0.03f * L, At(0.03f) + lift, zc), diam = diam });
            // deck / working lights above the deck (no visible bulb)
            if (L >= 20f)
            {
                int nDeck = big ? 3 : 1;
                for (int k = 0; k < nDeck; k++)
                {
                    float f = nDeck == 1 ? 0.5f : 0.2f + 0.3f * k;
                    m.Add(new Mount { kind = "deck", local = new Vector3(x0 + f * L, At(f) + 4f, zc), diam = 0f });
                }
            }
            var arr = m.ToArray();
            s_Mounts[v.placement.asset_id] = arr;
            return arr;
        }

        /// <summary>Per camera: switches navigation-light bulbs by the camera bearing and keeps bulbs >= ~2 px wide; enables harbour
        /// lights near the camera only (HDRP punctual-light budget). Call before every render of <paramref name="cam"/>.</summary>
        public void UpdateForCamera(Camera cam, int imageHeight)
        {
            if (!On || cam == null) return;
            var cp = cam.transform.position;
            float pixAngle = 2f * Mathf.Tan(cam.fieldOfView * Mathf.Deg2Rad / 2f) / Mathf.Max(1, imageHeight);
            foreach (var l in _vessel)
            {
                if (l.anchor == null) continue;
                bool facing = true;
                if (l.arcTo - l.arcFrom < 359f)
                {
                    var rel = l.vessel.root.transform.InverseTransformDirection(cp - l.anchor.position);
                    float a = Mathf.Atan2(-rel.z, rel.x) * Mathf.Rad2Deg;          // 0 = bow, + = starboard
                    if (l.arcTo > 180f && a < 0f) a += 360f;                      // stern sector 112.5..247.5
                    facing = a >= l.arcFrom && a <= l.arcTo;
                }
                if (l.bulb != null) l.bulb.enabled = facing;
                if (l.light != null && l.kind != "deck") l.light.enabled = facing;
                Scale(l, cp, pixAngle);
            }
            foreach (var l in _harbor)
            {
                if (l.anchor == null) continue;
                float d = Vector3.Distance(cp, l.anchor.position);
                if (l.light != null) l.light.enabled = d < (l.kind == "flood" ? 2500f : 1300f);
                Scale(l, cp, pixAngle);
            }
        }

        static void Scale(Lit l, Vector3 cp, float pixAngle)
        {
            if (l.bulb == null) return;
            float d = Vector3.Distance(cp, l.anchor.position);
            float diam = Mathf.Max(l.bulbDiam, d * pixAngle * 2.0f);
            var parentScale = l.anchor.lossyScale;
            float s = Mathf.Max(1e-4f, (parentScale.x + parentScale.y + parentScale.z) / 3f);
            l.bulb.transform.localScale = Vector3.one * (diam / s);
        }

        /// <summary>Navigation lights of <paramref name="v"/> whose bulbs currently face the camera (after UpdateForCamera).</summary>
        public List<string> VisibleNavLights(PlacedVessel v) =>
            _vessel.Where(l => l.vessel == v && l.bulb != null && l.bulb.enabled).Select(l => l.kind).ToList();

        public void Destroy()
        {
            foreach (var m in _bulbMats.Values) Util.SafeDestroy(m);
            _bulbMats.Clear();
            if (_root != null) Util.SafeDestroy(_root.gameObject);
        }
    }
}
