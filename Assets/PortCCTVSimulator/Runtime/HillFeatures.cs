using System;
using System.Collections.Generic;
using UnityEngine;

namespace PortCCTV
{
    /// <summary>
    /// Deterministic roads, house sites and field sectors on a procedural hill, defined in the hill's unit space (u,w in [0,1]^2)
    /// from the same ProcNoise.HillShape height field the mesh and the baked texture use. The Editor bake (HillBaker) draws the
    /// roads / house pads / terraces into the hill texture and the runtime map builders place the 3-D houses, retaining walls
    /// and poles at exactly the same sites, so texture and geometry agree.
    ///
    /// A camera hill is registered once per hill index (HillSpec) by the map builder; HillBaker builds every map definition
    /// first so the specs exist when it bakes. Non-camera hills get a single contour ring road (no village).
    /// </summary>
    public static class HillFeatures
    {
        /// <summary>Instance geometry of a camera hill: scale, base, flat-land level and the seaward direction (unit-space angle).</summary>
        [Serializable]
        public class HillSpec
        {
            public int hillIndex;
            public float sx, sy, sz, baseY, groundY;
            public float camU, camW;            // camera position in unit space
            public float seawardAngleDeg;       // atan2(dw, du) of the camera view direction in unit space
            /// <summary>Unit-space angle (about the hill centre) of the village sector: landward, i.e. behind the camera, so the
            /// shore cameras never look at the houses (user decision 2026-11-05).</summary>
            public float VillageAngleDeg => seawardAngleDeg + 180f;
            public float GroundUnit => (groundY - baseY) / sy;
            public string Hash => $"{hillIndex}_{Mathf.RoundToInt(sx)}x{Mathf.RoundToInt(sz)}_{Mathf.RoundToInt(sy * 10)}_{Mathf.RoundToInt(seawardAngleDeg)}_{Mathf.RoundToInt(camU * 1000)}_{Mathf.RoundToInt(camW * 1000)}_L";
        }

        public enum LaneType { MainRoad, Lane, Stair }

        public class Lane
        {
            public LaneType type;
            public List<Vector2> pts = new List<Vector2>();   // unit space
            public bool closed;
            public float WidthM => type == LaneType.MainRoad ? 6f : type == LaneType.Lane ? 3.5f : 2.4f;
        }

        public struct House
        {
            public Vector2 uv;       // unit position of the footprint centre
            public float yawDeg;     // Unity yaw (hill instance yaw 0): long axis along the lane
            public int kind;         // index into HouseKinds
            public float scale;
        }

        public class Village
        {
            public HillSpec spec;
            public List<Lane> lanes = new List<Lane>();
            public List<House> houses = new List<House>();
            public List<Vector2> poles = new List<Vector2>();     // unit positions along the main road
            public float sectorHalfDeg = 38f;                     // houses within +-sectorHalf of the seaward angle
            public float fieldFromDeg = 44f, fieldToDeg = 95f;    // terraced fields either side of the village sector
            public float fieldLoUnit, fieldHiUnit;                 // height band (unit) of the terraces

            /// <summary>True within 5 m of a lane or inside a house pad: used to keep trees off roads and yards.</summary>
            public bool Blocked(float u, float w)
            {
                var pm = new Vector2(u * spec.sx, w * spec.sz);
                foreach (var l in lanes)
                    if (DistToLaneM(l, pm) < l.WidthM / 2f + 5f) return true;
                foreach (var h in houses)
                    if (InPad(h, pm, 2.5f)) return true;
                return false;
            }

            public float DistToLaneM(Lane l, Vector2 pm)
            {
                float best = float.MaxValue;
                int n = l.closed ? l.pts.Count : l.pts.Count - 1;
                for (int i = 0; i < n; i++)
                {
                    var a = ToM(l.pts[i]); var b = ToM(l.pts[(i + 1) % l.pts.Count]);
                    float d = DistSeg(pm, a, b); if (d < best) best = d;
                }
                return best;
            }

            public bool InPad(House h, Vector2 pm, float margin)
            {
                var k = HouseKinds[h.kind];
                var c = ToM(h.uv); var d = pm - c;
                float yaw = h.yawDeg * Mathf.Deg2Rad;
                // Unity yaw t maps local +X to (cos t, -sin t) in (x,z); invert.
                float lx = d.x * Mathf.Cos(yaw) - d.y * Mathf.Sin(yaw), lz = d.x * Mathf.Sin(yaw) + d.y * Mathf.Cos(yaw);
                return Mathf.Abs(lx) < k.w * h.scale / 2f + margin && Mathf.Abs(lz) < k.d * h.scale / 2f + margin;
            }

            public Vector2 ToM(Vector2 uv) => new Vector2(uv.x * spec.sx, uv.y * spec.sz);
            public Vector2 ToU(Vector2 m) => new Vector2(m.x / spec.sx, m.y / spec.sz);
        }

        public struct HouseKind { public string key; public float w, d; public HouseKind(string k, float w_, float d_) { key = k; w = w_; d = d_; } }

        /// <summary>House prefab keys and footprints (long axis x, depth z) shared by the Editor prefab builder and placement.</summary>
        public static readonly HouseKind[] HouseKinds =
        {
            new HouseKind("house_hip_orange", 9.5f, 7.5f),
            new HouseKind("house_hip_blue", 8.0f, 7.0f),
            new HouseKind("house_flat_2f", 8.5f, 8.5f),
            new HouseKind("house_gable_slate", 11.0f, 6.5f),
            new HouseKind("house_flat_1f", 7.0f, 6.0f),
            new HouseKind("shed_steel", 6.0f, 4.0f),
        };

        static readonly Dictionary<int, HillSpec> _specs = new Dictionary<int, HillSpec>();
        static readonly Dictionary<string, Village> _cache = new Dictionary<string, Village>();

        public static IReadOnlyDictionary<int, HillSpec> Specs => _specs;

        /// <summary>Called by the map builders for every camera hill (one spec per hill index; a different spec replaces it with a warning).</summary>
        public static HillSpec Register(HillSpec s)
        {
            if (_specs.TryGetValue(s.hillIndex, out var old) && old.Hash != s.Hash)
                Debug.LogWarning($"[PortCCTV] camera hill {s.hillIndex} registered twice with different geometry ({old.Hash} -> {s.Hash}); the baked village texture follows the last one");
            _specs[s.hillIndex] = s;
            return s;
        }

        // ------------------------------------------------------------------ height field helpers (unit space)
        static float H(int i, float u, float w) => ProcNoise.HillHeight(i, u, w);

        static Vector2 Grad(int i, Vector2 p)
        {
            const float e = 1f / 512f;
            return new Vector2((H(i, p.x + e, p.y) - H(i, p.x - e, p.y)) / (2 * e), (H(i, p.x, p.y + e) - H(i, p.x, p.y - e)) / (2 * e));
        }

        static float DistSeg(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a; float l2 = ab.sqrMagnitude;
            float t = l2 < 1e-9f ? 0f : Mathf.Clamp01(Vector2.Dot(p - a, ab) / l2);
            return (p - (a + ab * t)).magnitude;
        }

        /// <summary>Traces the contour h == level counter-clockwise (increasing unit angle) from unit angle thetaStart over arcDeg degrees.</summary>
        public static List<Vector2> Contour(int i, float level, float thetaStartDeg, float arcDeg, float step = 0.005f)
        {
            var c = new Vector2(0.5f, 0.5f);
            float th = thetaStartDeg * Mathf.Deg2Rad;
            var dir = new Vector2(Mathf.Cos(th), Mathf.Sin(th));
            if (H(i, c.x + dir.x * 0.01f, c.y + dir.y * 0.01f) <= level) return null;
            float r = 0.01f;
            while (r < 0.49f && H(i, c.x + dir.x * r, c.y + dir.y * r) > level) r += 0.003f;
            if (r >= 0.49f) return null;
            var p = c + dir * (r - 0.0015f);
            var pts = new List<Vector2> { p };
            float total = 0f, prev = Mathf.Atan2(p.y - c.y, p.x - c.x);
            for (int k = 0; k < 6000; k++)
            {
                var g = Grad(i, p);
                if (g.sqrMagnitude < 1e-8f) break;
                var t = new Vector2(-g.y, g.x).normalized;
                var rad = p - c;
                if (rad.x * t.y - rad.y * t.x < 0) t = -t;          // counter-clockwise about the centre
                p += t * step;
                for (int n = 0; n < 2; n++)
                {
                    g = Grad(i, p); float g2 = g.sqrMagnitude; if (g2 < 1e-8f) break;
                    var corr = g * ((H(i, p.x, p.y) - level) / g2);
                    if (corr.magnitude > step) corr = corr.normalized * step;
                    p -= corr;
                }
                if (p.x < 0.03f || p.x > 0.97f || p.y < 0.03f || p.y > 0.97f) break;
                float ang = Mathf.Atan2(p.y - c.y, p.x - c.x);
                float d = Mathf.DeltaAngle(prev * Mathf.Rad2Deg, ang * Mathf.Rad2Deg);
                prev = ang; total += d;
                pts.Add(p);
                if (total >= arcDeg) break;
            }
            // light smoothing (keeps the ends)
            for (int pass = 0; pass < 2; pass++)
            {
                var s = new List<Vector2>(pts);
                for (int k = 2; k < pts.Count - 2; k++) s[k] = (pts[k - 2] + pts[k - 1] + pts[k] + pts[k + 1] + pts[k + 2]) / 5f;
                pts = s;
            }
            return pts.Count > 4 ? pts : null;
        }

        // ------------------------------------------------------------------ village layout
        public static Village Get(HillSpec spec)
        {
            if (_cache.TryGetValue(spec.Hash, out var v)) return v;
            v = Build(spec);
            _cache[spec.Hash] = v;
            return v;
        }

        /// <summary>Ring road only (non-camera hills): one contour at an absolute unit level, width in metres for an assumed scale.</summary>
        public static List<Vector2> RingRoad(int hillIndex, float level) => Contour(hillIndex, level, 0f, 360f, 0.006f);

        static Village Build(HillSpec s)
        {
            int i = s.hillIndex;
            var v = new Village { spec = s };
            var rng = new System.Random(7000 + i);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            float g = s.GroundUnit;
            float sea = s.VillageAngleDeg;   // houses / lanes / fields on the landward slope
            // Lanes: a coastal road just above the flat land around most of the hill, then three village lanes on the seaward
            // slope (contours ~7, 14, 21 m above the ground for a 110 m hill) and a stair lane straight up the slope.
            float[] levels = { g + 0.015f, g + 7f / s.sy, g + 14f / s.sy, g + 21f / s.sy };
            float half = v.sectorHalfDeg + 12f;
            float SlopeM(Vector2 p) { var gr = Grad(i, p); return new Vector2(gr.x / s.sx, gr.y / s.sz).magnitude * s.sy; }
            bool steepHill = false;
            for (int k = 0; k < levels.Length; k++)
            {
                bool main = k == 0;
                var pts = main ? Contour(i, levels[k], sea - 150f, 300f, 0.005f) : Contour(i, levels[k], sea - half, 2f * half, 0.004f);
                if (pts == null) continue;
                if (!main)
                {
                    // village lanes only where the slope is buildable (< ~45 deg over most of the lane); steep camera hills get the road only
                    int ok = 0; foreach (var p in pts) if (SlopeM(p) < 1.0f) ok++;
                    if (ok < pts.Count * 0.5f) { steepHill = true; continue; }
                }
                v.lanes.Add(new Lane { type = main ? LaneType.MainRoad : LaneType.Lane, pts = pts, closed = false });
            }
            if (steepHill) v.sectorHalfDeg = 0f;
            // Stair lane: from the main road up the gradient through the sector centre to the top lane.
            {
                var dir = new Vector2(Mathf.Cos(sea * Mathf.Deg2Rad), Mathf.Sin(sea * Mathf.Deg2Rad));
                var c = new Vector2(0.5f, 0.5f);
                float rTop = 0.01f; while (rTop < 0.49f && H(i, c.x + dir.x * rTop, c.y + dir.y * rTop) > levels[levels.Length - 1]) rTop += 0.003f;
                float rBot = rTop; while (rBot < 0.49f && H(i, c.x + dir.x * rBot, c.y + dir.y * rBot) > levels[0]) rBot += 0.003f;
                if (rBot > rTop + 0.01f && !steepHill)
                {
                    var st = new Lane { type = LaneType.Stair };
                    for (float r = rBot; r >= rTop; r -= 0.004f) st.pts.Add(c + dir * r + new Vector2(0.004f * Mathf.Sin(r * 90f), 0f));
                    v.lanes.Add(st);
                }
            }
            v.fieldLoUnit = g + 0.004f; v.fieldHiUnit = g + 24f / s.sy;

            // Houses along the village lanes (both sides) and the first row above the coastal road, inside the seaward sector.
            var cam = new Vector2(s.camU * s.sx, s.camW * s.sz);
            foreach (var l in v.lanes)
            {
                if (l.type == LaneType.Stair) continue;
                bool main = l.type == LaneType.MainRoad;
                float next = R(6f, 14f);
                float acc = 0f;
                for (int k = 1; k < l.pts.Count; k++)
                {
                    var a = v.ToM(l.pts[k - 1]); var b = v.ToM(l.pts[k]);
                    float seg = (b - a).magnitude; acc += seg;
                    if (acc < next) continue;
                    acc = 0f; next = R(11f, 17f);
                    var pm = b; var t = (b - a).normalized;
                    var ang = Mathf.Atan2(l.pts[k].y - 0.5f, l.pts[k].x - 0.5f) * Mathf.Rad2Deg;
                    if (Mathf.Abs(Mathf.DeltaAngle(ang, sea)) > v.sectorHalfDeg) continue;
                    // uphill normal (metres)
                    var gu = Grad(i, l.pts[k]); var gm = new Vector2(gu.x / s.sx, gu.y / s.sz);
                    var n = new Vector2(-t.y, t.x); if (Vector2.Dot(n, gm) < 0) n = -n;   // n points uphill
                    float slope = gm.magnitude * s.sy;                                    // dy/dxz in metres
                    if (slope > 0.8f) continue;
                    int kind = rng.Next(HouseKinds.Length);
                    if (main) kind = rng.NextDouble() < 0.3 ? 5 : rng.Next(5);           // sheds and shops along the coastal road
                    var hk = HouseKinds[kind];
                    float scale = R(0.92f, 1.08f);
                    float side = main ? 1f : (rng.NextDouble() < 0.55 ? 1f : -1f);        // main road: land side only
                    float off = l.WidthM / 2f + 1.5f + hk.d * scale / 2f;
                    var hm = pm + n * (side * off);
                    var huv = v.ToU(hm);
                    if (huv.x < 0.05f || huv.x > 0.95f || huv.y < 0.05f || huv.y > 0.95f) continue;
                    float hh = H(i, huv.x, huv.y);
                    if (hh < g + 0.004f) continue;
                    if ((hm - cam).magnitude < 42f) continue;
                    float yaw = Mathf.Atan2(-t.y, t.x) * Mathf.Rad2Deg + R(-4f, 4f);
                    var h = new House { uv = huv, yawDeg = yaw, kind = kind, scale = scale };
                    bool clash = false;
                    foreach (var o in v.houses)
                        if ((v.ToM(o.uv) - hm).magnitude < (Mathf.Max(hk.w, hk.d) + Mathf.Max(HouseKinds[o.kind].w, HouseKinds[o.kind].d)) * 0.55f) { clash = true; break; }
                    if (clash) continue;
                    foreach (var ol in v.lanes) if (ol != l && v.DistToLaneM(ol, hm) < ol.WidthM / 2f + Mathf.Min(hk.w, hk.d) * 0.5f) { clash = true; break; }
                    if (clash) continue;
                    v.houses.Add(h);
                }
            }
            // Utility poles along the uphill side of the coastal road, every ~32 m.
            foreach (var l in v.lanes)
            {
                if (l.type != LaneType.MainRoad) continue;
                float acc = 20f;
                for (int k = 1; k < l.pts.Count; k++)
                {
                    var a = v.ToM(l.pts[k - 1]); var b = v.ToM(l.pts[k]);
                    acc += (b - a).magnitude; if (acc < 32f) continue; acc = 0f;
                    var t = (b - a).normalized; var gu = Grad(i, l.pts[k]); var gm = new Vector2(gu.x / s.sx, gu.y / s.sz);
                    var n = new Vector2(-t.y, t.x); if (Vector2.Dot(n, gm) < 0) n = -n;
                    v.poles.Add(v.ToU(b + n * (l.WidthM / 2f + 0.8f)));
                }
            }
            return v;
        }
    }
}
