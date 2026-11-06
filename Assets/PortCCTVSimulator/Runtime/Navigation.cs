using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace PortCCTV
{
    /// <summary>Scenario-level navigation settings. The vessel poses in <see cref="VesselPlacement"/> are the poses at t = 0;
    /// the poses used for rendering and labels are those at <see cref="sim_time_s"/>.</summary>
    [Serializable]
    public class NavigationConfig
    {
        /// <summary>Simulated time since the start poses (s). 0 = start poses (no motion applied).</summary>
        public float sim_time_s = 0f;
        /// <summary>Fixed integration step (s). Poses at time t are always obtained by stepping from 0, so t reproduces exactly.</summary>
        public float dt_s = 0.25f;
        /// <summary>Simple COLREG-like give-way (turn to starboard, slow down) between vessels.</summary>
        public bool collision_avoidance = true;
        /// <summary>Wind: direction it blows FROM (heading convention: 0 = +X, clockwise from above) and speed (kn). Moving vessels
        /// make leeway (a few % of the wind speed, downwind), anchored vessels turn their bow into wind and current.</summary>
        public float wind_from_deg = 300f;
        public float wind_speed_kn = 8f;
        /// <summary>Tidal / harbour current: direction it sets TO and drift speed (kn); carried fully by every vessel that is not
        /// moored or holding position. 0 = no current.</summary>
        public float current_set_deg = 60f;
        public float current_drift_kn = 0.4f;
    }

    /// <summary>Per-vessel motion. mode: "none" (fixed, e.g. moored) | "auto" (4-waypoint loop generated from the water regions,
    /// structures and the CCTV view; deterministic from object_id + seed) | "roam" (free roaming: a new waypoint anywhere in the
    /// water is drawn whenever the current one is reached, so the vessel wanders the whole water area indefinitely; same
    /// determinism) | "route" (the given waypoints) | "anchor" (riding at anchor: the bow points into wind and current, the hull
    /// swings slowly about the anchor point ahead of the start pose; no propulsion).</summary>
    [Serializable]
    public class VesselMotion
    {
        public string mode = "none";
        /// <summary>Cruise speed through the water (knots). 0 = asset default (harbour speeds, see NavigationSim.DefaultSpeedKn).</summary>
        public float speed_kn = 0f;
        /// <summary>World (x, z) waypoints for mode "route". Ignored for "auto" (the generated route is reported in metadata).</summary>
        public List<Vector2> route = new List<Vector2>();
        /// <summary>true: return to the first waypoint and repeat; false: stop at the last one.</summary>
        public bool loop = true;
        /// <summary>auto: prefer waypoints inside the fixed CCTV image so the vessel stays observable.</summary>
        public bool keep_in_view = true;
        public int seed = 0;

        public bool Moving => mode == "auto" || mode == "route" || mode == "roam";
        public bool Anchored => mode == "anchor";
    }

    /// <summary>Navigation state of one vessel at the current simulated time (also written to capture metadata).</summary>
    [Serializable]
    public class NavVesselRecord
    {
        public string object_id;
        public string asset_id;
        public string mode;
        public string state;                 // fixed | at_anchor | underway | give_way | slowing | holding_obstacle | holding_vessel | arrived | no_route
        public float cruise_speed_kn;
        public float speed_kn;
        public float heading_deg;            // same convention as VesselPlacement.heading_deg
        public Vector2 position_xz;
        public Vector2 start_xz;
        public float start_heading_deg;
        public float distance_travelled_m;
        public int waypoint_index;
        public List<Vector2> route = new List<Vector2>();
        public bool route_loop;
        public string route_note = "";
        public int give_way_steps;
        public float turn_radius_m;
        public float rudder;                 // -1 (hard to port) .. +1 (hard to starboard), 0 for fixed vessels
        public float yaw_rate_deg_s;
        public Vector2 drift_xz_m_s;         // wind leeway + current actually applied this step (0 while holding position / moored)
        public Vector2 anchor_xz;            // mode "anchor": anchor position (world x, z)
        public float anchor_scope_m;         // mode "anchor": bow-to-anchor distance (swing radius = scope + half length)
    }

    [Serializable]
    public class NavigationRecord
    {
        public string model = "kinematic: fixed-step pure pursuit steered through a rudder with travel-time lag and a yaw-rate time constant (max yaw rate = speed / turn radius), " +
                              "speed with acceleration limit, slow coasting when the engine is eased and faster astern stops, turn slowdown, " +
                              "COLREG-like give-way (starboard turn + slow down when a vessel on the starboard bow / ahead has CPA < safe distance within 90 s), " +
                              "lookahead stop near structures and outside water regions, swept vessel-footprint collision guard, uniform wind leeway + current drift (cancelled while holding position), " +
                              "anchored vessels swing about the anchor with the bow into the wind/current resultant plus a slow seeded yaw. " +
                              "No hydrodynamics, no wake, no waves under the hull; poses on the mean water plane.";
        public float sim_time_s;
        public float dt_s;
        public int steps;
        public bool collision_avoidance;
        public float wind_from_deg, wind_speed_kn, current_set_deg, current_drift_kn;
        public List<NavVesselRecord> vessels = new List<NavVesselRecord>();
    }

    /// <summary>
    /// Deterministic vessel navigation. Created from a scenario after the harbor and the CCTV camera are set up (needs the
    /// harbor colliders for route/obstacle checks), stepped with a fixed dt from t = 0. Steering proposals use the previous
    /// state of all vessels; the footprint guard resolves simultaneous conflicts in scenario order. Replaying a scenario
    /// from t = 0 reproduces its poses at the same time.
    /// </summary>
    public class NavigationSim
    {
        class V
        {
            public VesselPlacement p;
            public VesselAssetEntry e;
            public NavVesselRecord rec;
            public Vector2 pos; public float heading; public float speed; public float cruise;
            public float L, B, turnR, accept, accel;
            public List<Vector2> route; public int wp; public int dirWp = 1; public bool loop; public bool pingPong;
            public bool moving; public float bias; public string state;
            public int vesselHoldSteps;
            public bool roam; public System.Random roamRng; public float roamR, roamClear; public int roamDraws;
            public float rudder, yawRate, rudderT, yawT, leeway; public Vector2 legStart;
            public bool anchored; public Vector2 anchor; public float scope, anchorAmp, anchorPeriod, anchorPhase, anchorT;
        }

        readonly List<V> _vs = new List<V>();
        readonly List<(string id, Vector2 pos, float radius)> _routeObstacles = new List<(string, Vector2, float)>();
        readonly HarborDefinition _harbor;
        readonly HashSet<Collider> _harborCols;
        readonly Collider[] _buf = new Collider[32];
        readonly float _waterY;
        readonly Vector2 _wind, _current;    // world (x, z) velocity vectors in m/s: wind blowing TO, current setting TO
        public Vector2 Wind => _wind;
        public Vector2 Current => _current;
        public float Time { get; private set; }
        public int Steps { get; private set; }
        public float Dt { get; }
        public bool Avoid { get; }
        public bool AnyMoving => _vs.Any(v => v.moving);

        public static float DefaultSpeedKn(string assetId, float lengthM) => assetId switch
        {
            "kayak" => 2.5f, "canoe" => 2.5f, "sailing_dinghy" => 4f, "sailing_yacht" => 5f,
            "rib" => 12f, "motorboat" => 10f, "cabin_cruiser" => 8f,
            "fishing_boat" => 7f, "korean_fishing_boat" => 8f, "ferry" => 10f, "tugboat" => 7f, "barge" => 5f,
            "container_ship" => 7f, "tanker" => 6f,
            _ => lengthM < 6f ? 3f : lengthM < 30f ? 8f : 6f,
        };

        public const float Knot = 0.514444f;

        /// <summary>Leeway factor: fraction of the wind speed a hull drifts downwind (sail and high-freeboard hulls more).</summary>
        public static float LeewayFactor(string assetId) => assetId switch
        {
            "sailing_yacht" => 0.05f, "sailing_dinghy" => 0.06f, "kayak" => 0.04f, "canoe" => 0.04f,
            "rib" => 0.035f, "motorboat" => 0.03f, "cabin_cruiser" => 0.03f, "ferry" => 0.025f,
            "fishing_boat" => 0.03f, "korean_fishing_boat" => 0.025f, "tugboat" => 0.02f, "barge" => 0.03f,
            "container_ship" => 0.02f, "tanker" => 0.015f,
            _ => 0.025f,
        };

        /// <summary>Unity heading convention used by VesselPlacement: 0 = +X, positive = clockwise seen from above.</summary>
        public static Vector2 Dir(float headingDeg) { float r = headingDeg * Mathf.Deg2Rad; return new Vector2(Mathf.Cos(r), -Mathf.Sin(r)); }
        public static float HeadingOf(Vector2 d) => Mathf.Atan2(-d.y, d.x) * Mathf.Rad2Deg;

        public NavigationSim(HarborDefinition harbor, IEnumerable<Collider> harborColliders, Camera cctv, VesselCatalog catalog, ScenarioConfig cfg)
        {
            _harbor = harbor;
            _harborCols = new HashSet<Collider>(harborColliders.Where(c => c != null));
            _waterY = harbor.water_reference_y;
            var nav = cfg.navigation ?? new NavigationConfig();
            Dt = Mathf.Clamp(nav.dt_s, 0.05f, 2f);
            Avoid = nav.collision_avoidance;
            _wind = Dir(nav.wind_from_deg + 180f) * Mathf.Max(0f, nav.wind_speed_kn) * Knot;
            _current = Dir(nav.current_set_deg) * Mathf.Max(0f, nav.current_drift_kn) * Knot;
            foreach (var p in cfg.vessels)
            {
                if (p.motion != null && p.motion.Moving) continue;
                var e = catalog.Find(p.asset_id);
                if (e == null) continue;
                var g = e.geometry;
                float length = g.bounds_max.x - g.bounds_min.x, beam = g.bounds_max.z - g.bounds_min.z;
                _routeObstacles.Add((p.object_id, new Vector2(p.x, p.z), 0.5f * Mathf.Sqrt(length * length + beam * beam) + 4f));
            }
            foreach (var p in cfg.vessels)
            {
                var e = catalog.Find(p.asset_id);
                if (e == null) continue;                     // SimWorld reports unknown assets
                var g = e.geometry;
                var m = p.motion ?? new VesselMotion();
                var v = new V
                {
                    p = p, e = e, pos = new Vector2(p.x, p.z), heading = p.heading_deg,
                    L = g.bounds_max.x - g.bounds_min.x, B = g.bounds_max.z - g.bounds_min.z,
                };
                v.turnR = Mathf.Clamp(2f * v.L, 6f, 150f);                 // large ships: tug-assisted harbour turns
                v.accept = 0.8f * v.turnR + 10f;
                v.cruise = (m.speed_kn > 0f ? m.speed_kn : DefaultSpeedKn(p.asset_id, v.L)) * Knot;
                v.accel = v.cruise / Mathf.Clamp(v.L * 0.6f, 6f, 90f);   // small craft reach speed in seconds, ships in a minute or more
                v.rudderT = Mathf.Clamp(v.L / 15f, 1f, 12f);             // seconds for the rudder to travel from amidships to hard over
                v.yawT = Mathf.Clamp(v.L / 20f, 0.5f, 8f);               // yaw-rate time constant (hull inertia)
                v.leeway = LeewayFactor(p.asset_id);
                v.rec = new NavVesselRecord
                {
                    object_id = p.object_id, asset_id = p.asset_id, mode = m.mode ?? "none", start_xz = v.pos, start_heading_deg = p.heading_deg,
                    cruise_speed_kn = v.cruise / Knot, turn_radius_m = v.turnR, route_loop = m.loop,
                };
                if (m.Moving)
                {
                    if (m.mode == "route") { v.route = m.route?.ToList() ?? new List<Vector2>(); v.rec.route_note = "given waypoints"; }
                    else if (m.mode == "roam")
                    {
                        v.roam = true;
                        v.roamRng = new System.Random(unchecked((int)(Fnv(p.object_id) ^ (uint)(m.seed * 7919) ^ 0x5bd1e995u)));
                        v.roamR = Mathf.Max(Mathf.Clamp(v.cruise * 240f, 200f, 1800f), 3f * v.turnR);
                        v.roamClear = v.B * 0.5f + Mathf.Max(6f, 0.3f * v.L);
                        v.route = new List<Vector2>();
                        if (NextRoamTarget(v, out var first)) v.route.Add(first);
                        v.rec.route_note = v.route.Count > 0 ? $"roam: free wandering, draw radius {v.roamR:F0} m" : "roam: no clear waypoint found (vessel holds its start pose)";
                    }
                    else v.route = AutoRoute(v, m, cctv, out v.rec.route_note);
                    v.loop = m.loop;
                    if (v.route.Count > 0 && v.loop && v.route.Count >= 2 && (!SegmentClear(v.route[v.route.Count - 1], v.route[0], SweptHalfWidth(v)) ||
                        !TurnArcClear(v, v.route[v.route.Count - 1], HeadingOf(v.route[v.route.Count - 1] - v.route[v.route.Count - 2]), v.route[0])))
                    { v.pingPong = true; v.rec.route_note += "; closing leg blocked -> back and forth"; }
                    v.moving = v.route.Count > 0;
                    v.legStart = v.pos;
                    v.speed = v.moving ? v.cruise : 0f;       // start already underway (the scene is a snapshot of ongoing traffic)
                    v.state = v.moving ? "underway" : "no_route";
                }
                else if (m.Anchored)
                {
                    // anchor ahead of the bow; the hull then swings about it on a circle of radius scope + L/2
                    v.anchored = true;
                    v.scope = Mathf.Clamp(1.5f * v.L, 15f, 120f);
                    v.anchor = v.pos + Dir(v.heading) * (0.5f * v.L + v.scope);
                    var arng = new System.Random(unchecked((int)(Fnv(p.object_id) ^ (uint)(m.seed * 7919) ^ 0x27d4eb2fu)));
                    v.anchorAmp = Mathf.Lerp(5f, 12f, (float)arng.NextDouble());
                    v.anchorPeriod = Mathf.Lerp(180f, 420f, (float)arng.NextDouble());
                    v.anchorPhase = (float)(arng.NextDouble() * 360.0);
                    v.anchorT = Mathf.Clamp(2f * v.L, 60f, 400f);        // seconds to swing the bow towards the wind/current resultant
                    v.state = "at_anchor";
                    v.rec.anchor_xz = v.anchor; v.rec.anchor_scope_m = v.scope;
                    v.rec.route_note = $"anchor: scope {v.scope:F0} m, swing radius {v.scope + 0.5f * v.L:F0} m";
                }
                else v.state = "fixed";
                v.rec.route = v.route ?? new List<Vector2>();
                _vs.Add(v);
            }
            UpdateRecords();
        }

        // ------------------------------------------------------------------ route generation
        static uint Fnv(string s) { uint h = 2166136261u; foreach (var c in s) h = (h ^ c) * 16777619u; return h; }

        /// <summary>Roam: draw a waypoint within roamR of the current position, in water, clear of structures, with a clear
        /// straight leg; prefer a heading change of 30..150 deg so the vessel covers the area instead of shuttling.</summary>
        bool NextRoamTarget(V v, out Vector2 target)
        {
            target = default;
            for (int attempt = 0; attempt < 160; attempt++)
            {
                bool wide = attempt < 100;
                float turn = (float)(v.roamRng.NextDouble() * 120 + 30) * (v.roamRng.NextDouble() < 0.5 ? 1f : -1f);
                float ang = wide ? v.heading + turn : (float)(v.roamRng.NextDouble() * 360);
                float dist = Mathf.Lerp(0.3f * v.roamR, v.roamR, (float)v.roamRng.NextDouble());
                var c = v.pos + Dir(ang) * dist;
                if (!_harbor.InAnyWaterRegion(c) || Blocked(c, v.roamClear)) continue;
                float look = v.roamClear + Lookahead(v, v.cruise);
                if (!_harbor.InAnyWaterRegion(c + new Vector2(look, 0)) || !_harbor.InAnyWaterRegion(c - new Vector2(look, 0)) ||
                    !_harbor.InAnyWaterRegion(c + new Vector2(0, look)) || !_harbor.InAnyWaterRegion(c - new Vector2(0, look))) continue;
                if (!SegmentClear(v.pos, c, SweptHalfWidth(v)) || !TurnArcClear(v, v.pos, v.heading, c) || !ClearOfVessels(v, v.pos, c)) continue;
                target = c; v.roamDraws++;
                return true;
            }
            return false;
        }

        List<Vector2> AutoRoute(V v, VesselMotion m, Camera cam, out string note)
        {
            var rng = new System.Random(unchecked((int)(Fnv(v.p.object_id) ^ (uint)(m.seed * 7919))));
            float R0 = Mathf.Max(Mathf.Clamp(v.cruise * 150f, 120f, 1200f), 3f * v.turnR);
            float clear = v.B * 0.5f + Mathf.Max(6f, 0.3f * v.L);
            var route = new List<Vector2>();
            bool relaxedView = false;
            float R = R0;
            // try the speed-scaled radius first, then smaller ones (vessels in an enclosed basin)
            foreach (var scale in new[] { 1f, 0.6f, 0.35f, 0.2f })
            {
                R = Mathf.Max(R0 * scale, 2.2f * v.accept);
                route = TryRoute(v, m, cam, rng, R, clear, ref relaxedView);
                if (route.Count >= 2) break;
            }
            note = route.Count == 0 ? "no clear waypoint found (vessel holds its start pose)" :
                $"auto: {route.Count} waypoints, radius {R:F0} m, clearance {clear:F1} m" + (relaxedView ? ", some waypoints outside the CCTV view" : "");
            return route;
        }

        List<Vector2> TryRoute(V v, VesselMotion m, Camera cam, System.Random rng, float R, float clear, ref bool relaxedView)
        {
            var route = new List<Vector2>();
            var prev = v.pos; float prevHeading = v.heading;
            for (int k = 0; k < 4; k++)
            {
                Vector2? best = null;
                for (int attempt = 0; attempt < 120 && best == null; attempt++)
                {
                    bool view = m.keep_in_view && cam != null && attempt < 80;
                    float ang = k == 0 ? prevHeading + (float)(rng.NextDouble() * 120 - 60) : (float)(rng.NextDouble() * 360);
                    float dist = Mathf.Lerp(0.35f * R, R, (float)rng.NextDouble());
                    var c = prev + Dir(ang) * dist;
                    if (!_harbor.InAnyWaterRegion(c) || Blocked(c, clear)) continue;
                    float look = clear + Lookahead(v, v.cruise);                            // room for the lookahead check around the waypoint
                    if (!_harbor.InAnyWaterRegion(c + new Vector2(look, 0)) || !_harbor.InAnyWaterRegion(c - new Vector2(look, 0)) ||
                        !_harbor.InAnyWaterRegion(c + new Vector2(0, look)) || !_harbor.InAnyWaterRegion(c - new Vector2(0, look))) continue;
                    if (route.Any(q => (q - c).sqrMagnitude < 0.09f * R * R) || (c - v.pos).sqrMagnitude < 0.09f * R * R && k > 0 && k < 3) continue;
                    if (view && !InView(cam, c, v.L)) continue;
                    if (!SegmentClear(prev, c, SweptHalfWidth(v)) || !TurnArcClear(v, prev, prevHeading, c) || !ClearOfVessels(v, prev, c)) continue;
                    if (!view && m.keep_in_view) relaxedView = true;
                    best = c;
                }
                if (best == null) break;
                route.Add(best.Value);
                prevHeading = HeadingOf(best.Value - prev);
                prev = best.Value;
            }
            return route;
        }

        bool ClearOfVessels(V v, Vector2 start, Vector2 end)
        {
            var delta = end - start;
            float length2 = delta.sqrMagnitude;
            foreach (var o in _routeObstacles)
            {
                if (o.id == v.p.object_id) continue;
                float t = length2 > 1e-4f ? Mathf.Clamp01(Vector2.Dot(o.pos - start, delta) / length2) : 0f;
                if (Vector2.Distance(o.pos, start + delta * t) < o.radius + v.B * 0.5f + 3f) return false;
            }
            return true;
        }

        static bool InView(Camera cam, Vector2 xz, float L)
        {
            var vp = cam.WorldToViewportPoint(new Vector3(xz.x, 0f, xz.y));
            if (vp.z <= 0f || vp.x < 0.06f || vp.x > 0.94f || vp.y < 0.06f || vp.y > 0.94f) return false;
            float px = L / Mathf.Max(vp.z, 1f) / (2f * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad)) * cam.pixelHeight;
            return px >= 6f || cam.pixelHeight == 0;   // at least a few pixels long (preview height)
        }

        /// <summary>Any harbor collider inside a flat box (half size radius x 1 m x radius) on the waterline band [+0.0, +2.0] m:
        /// quays, piers, pontoons, breakwaters, land, buoys; not the sea bed or hill bases below the water.</summary>
        bool Blocked(Vector2 xz, float radius)
        {
            int n = Physics.OverlapBoxNonAlloc(new Vector3(xz.x, _waterY + 1.0f, xz.y), new Vector3(radius, 1.0f, radius), _buf, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++) if (_harborCols.Contains(_buf[i])) return true;
            return false;
        }

        static float SweptHalfWidth(V v) => v.B * 0.5f + Mathf.Max(3f, 0.1f * v.L);

        /// <summary>Hull extremes (bow, stern) at a pose; both must be in water and clear (r = swept half width).</summary>
        bool HullClear(V v, Vector2 pos, float heading, float r)
        {
            var bow = pos + Dir(heading) * 0.5f * v.L; var stern = pos - Dir(heading) * 0.5f * v.L;
            return _harbor.InAnyWaterRegion(pos) && !Blocked(pos, r) && _harbor.InAnyWaterRegion(bow) && !Blocked(bow, r) &&
                   _harbor.InAnyWaterRegion(stern) && !Blocked(stern, r);
        }

        /// <summary>Like HullClear but samples the whole hull length every ~12 m (catches a buoy or pile between bow and stern of a
        /// long ship); used once per vessel per step, not in the route searches.</summary>
        bool HullClearDense(V v, Vector2 pos, float heading, float r)
        {
            int n = Mathf.Max(2, Mathf.CeilToInt(v.L / 12f));
            var d = Dir(heading);
            for (int i = 0; i <= n; i++)
            {
                var q = pos + d * (-0.5f * v.L + v.L * i / n);
                if (!_harbor.InAnyWaterRegion(q) || Blocked(q, r)) return false;
            }
            return true;
        }

        /// <summary>The turning arc (radius turnR) from (pos, heading) onto the bearing of target, sampled every ~8 m of arc with the
        /// hull extremes: a big ship must not sweep its stern or bow into a breakwater while turning towards the next waypoint.</summary>
        bool TurnArcClear(V v, Vector2 pos, float heading, Vector2 target)
        {
            float err = Mathf.DeltaAngle(heading, HeadingOf(target - pos));
            if (Mathf.Abs(err) < 3f) return true;
            float r = v.turnR, sgn = Mathf.Sign(err), hw = SweptHalfWidth(v);
            float step = Mathf.Max(2f, 8f / r * Mathf.Rad2Deg);
            var centre = pos + Dir(heading + sgn * 90f) * r;
            for (float a = step; a <= Mathf.Abs(err) + step * 0.5f; a += step)
            {
                float aa = Mathf.Min(a, Mathf.Abs(err));
                var p = centre - Dir(heading + sgn * (90f + aa)) * r;
                if (!HullClear(v, p, heading + sgn * aa, hw)) return false;
            }
            return true;
        }

        /// <summary>Distance ahead of the bow that must be clear: hull half length + max(8 s of travel, stopping distance) + 5 m.</summary>
        static float Lookahead(V v, float speed) => 0.6f * v.L + Mathf.Max(speed * 8f, speed * speed / (2f * v.accel)) + 5f;

        bool SegmentClear(Vector2 a, Vector2 b, float r)
        {
            float len = (b - a).magnitude;
            int n = Mathf.Max(1, Mathf.CeilToInt(len / 8f));
            for (int i = 0; i <= n; i++)
            {
                var q = Vector2.Lerp(a, b, (float)i / n);
                if (!_harbor.InAnyWaterRegion(q) || Blocked(q, r)) return false;
            }
            return true;
        }

        // ------------------------------------------------------------------ stepping
        public void AdvanceTo(float t)
        {
            int target = Mathf.RoundToInt(Mathf.Max(0f, t) / Dt);
            if (target < Steps) throw new InvalidOperationException("NavigationSim cannot step backwards; build a new one from the scenario");
            while (Steps < target) Step();
            UpdateRecords();
        }

        void Step()
        {
            int n = _vs.Count;
            var newPos = new Vector2[n]; var newHead = new float[n]; var newSpeed = new float[n];
            for (int i = 0; i < n; i++)
            {
                var v = _vs[i];
                newPos[i] = v.pos; newHead[i] = v.heading; newSpeed[i] = v.speed;
                if (v.anchored) { StepAnchored(v, out newPos[i], out newHead[i]); continue; }
                if (!v.moving) continue;
                if (v.roam && v.vesselHoldSteps >= 40 && NextRoamTarget(v, out var detour))
                {
                    v.route.Add(detour);
                    if (v.route.Count > 8) v.route.RemoveAt(0);
                    v.wp = v.route.Count - 1;
                    v.legStart = v.pos;
                    v.vesselHoldSteps = 0;
                }
                // waypoint progress
                var target = v.route[v.wp];
                if ((target - v.pos).magnitude < v.accept && v.state != "arrived")
                {
                    if (v.roam)
                    {
                        // free roaming: replace the reached waypoint with a fresh one (the record keeps the last 8 for metadata)
                        if (NextRoamTarget(v, out var nextWp)) { v.route.Add(nextWp); if (v.route.Count > 8) v.route.RemoveAt(0); v.wp = v.route.Count - 1; }
                        else v.state = "arrived";
                    }
                    else if (v.route.Count == 1) { if (!v.loop) v.state = "arrived"; }
                    else if (v.pingPong)
                    {
                        if (v.wp + v.dirWp < 0 || v.wp + v.dirWp >= v.route.Count) v.dirWp = -v.dirWp;
                        v.wp += v.dirWp;
                    }
                    else if (v.wp + 1 < v.route.Count) v.wp++;
                    else if (v.loop) v.wp = 0;
                    else v.state = "arrived";
                    target = v.route[v.wp];
                    v.legStart = v.pos;
                }
                // track the leg line (not just the waypoint): aim at the point on the leg 1.5 turn radii ahead of the vessel's
                // projection, so wind leeway / current are compensated by a crab angle instead of a long curved set-down
                var aim = target;
                var leg = target - v.legStart; float legLen = leg.magnitude;
                if (legLen > 1f && v.bias < 1f)
                {
                    var u = leg / legLen;
                    float along = Vector2.Dot(v.pos - v.legStart, u);
                    aim = v.legStart + u * Mathf.Clamp(along + Mathf.Max(1.5f * v.turnR, 40f), 0f, legLen);
                }
                float desired = HeadingOf(aim - v.pos);
                float speedGoal = v.state == "arrived" ? 0f : v.cruise;
                string state = v.state == "arrived" ? "arrived" : "underway";

                // collision avoidance against all other vessels (previous state)
                float bias = 0f;
                if (Avoid)
                    for (int j = 0; j < n; j++)
                    {
                        if (j == i) continue;
                        var o = _vs[j];
                        var rel = o.pos - v.pos; float d = rel.magnitude;
                        if (d > Mathf.Max(6f * v.L, 150f) + o.L) continue;
                        float safe = 0.5f * (v.L + o.L) + Mathf.Max(15f, 0.15f * Mathf.Max(v.L, o.L));
                        var relV = Dir(o.heading) * o.speed - Dir(v.heading) * v.speed;
                        float rv2 = relV.sqrMagnitude;
                        float tcpa = rv2 > 1e-4f ? -Vector2.Dot(rel, relV) / rv2 : 0f;
                        float dcpa = rv2 > 1e-4f ? (rel + relV * Mathf.Max(0f, tcpa)).magnitude : d;
                        float beta = Mathf.DeltaAngle(v.heading, HeadingOf(rel));   // + = starboard side
                        // keep giving way while still closing and the other vessel is not yet abeam (otherwise the route pull swings
                        // back too early); released as soon as the two are opening
                        bool closing = Vector2.Dot(rel, relV) < 0f;
                        bool conflict = (tcpa > 0f && tcpa < 90f && dcpa < safe * 1.5f) || (d < safe && closing) ||
                            (d < safe * 1.6f && closing && Mathf.Abs(beta) < 100f) ||
                            (v.state == "holding_vessel" && d < safe * 1.8f && Mathf.Abs(beta) < 130f);
                        if (!conflict) continue;
                        bool headOn = Mathf.Abs(beta) < 12f && Mathf.Abs(Mathf.DeltaAngle(v.heading, o.heading + 180f)) < 20f;
                        if (!o.moving || headOn || (beta >= -10f && beta <= 112.5f))
                        {
                            float urg = Mathf.Clamp01(1.5f - dcpa / (safe * 1.5f)) * Mathf.Clamp01(1.2f - tcpa / 90f);
                            bias = Mathf.Max(bias, 25f + 35f * urg);
                            speedGoal = Mathf.Min(speedGoal, d < safe * 1.2f ? 0f : v.cruise * 0.7f);
                            state = "give_way";
                        }
                        else if (d < safe * 1.2f) { speedGoal = Mathf.Min(speedGoal, 0f); state = "slowing"; }
                    }
                v.bias = Mathf.MoveTowards(v.bias, bias, 30f * Dt);
                desired += v.bias;

                // structures / leaving the water ahead
                if (Steps % 2 == 0 || v.state == "holding_obstacle")
                {
                    var ahead = v.pos + Dir(v.heading) * Lookahead(v, v.speed);
                    if (!_harbor.InAnyWaterRegion(ahead) || Blocked(ahead, v.B * 0.5f + 2f))
                    {
                        var near = v.pos + Dir(v.heading) * (0.6f * v.L + 4f);
                        bool close = !_harbor.InAnyWaterRegion(near) || Blocked(near, v.B * 0.5f + 1f);
                        speedGoal = Mathf.Min(speedGoal, close ? 0f : v.cruise * 0.2f);
                        desired += close ? 70f : 25f;                                         // swing to starboard, away from the obstacle
                        state = "holding_obstacle";
                    }
                }
                v.state = state;
                if (state == "give_way") v.rec.give_way_steps++;

                // steering: a proportional + rate-damped rudder order, rudder travel lag, then a yaw-rate time constant (hull inertia);
                // the maximum yaw rate is speed / turn radius as before
                float err = Mathf.DeltaAngle(v.heading, desired);
                float maxYaw = (Mathf.Max(v.speed, 0.3f * v.cruise) / v.turnR) * Mathf.Rad2Deg;      // deg/s
                float kd = Mathf.Clamp(v.L / 10f, 0.5f, 2f);                                              // yaw-rate damping (s): small craft answer the helm fast, ships need damping
                float order = Mathf.Clamp((err - kd * v.yawRate) / 25f, -1f, 1f);
                float rudder = Mathf.MoveTowards(v.rudder, order, Dt / v.rudderT);
                float yaw = Mathf.Lerp(v.yawRate, rudder * maxYaw, 1f - Mathf.Exp(-Dt / v.yawT));
                yaw = Mathf.Clamp(yaw, -maxYaw, maxYaw);
                float h = v.heading + yaw * Dt;
                // speed: acceleration limit; easing the engine lets the hull coast (slow), a stop order (obstacle / arrived) uses astern power
                speedGoal *= Mathf.Lerp(1f, 0.4f, Mathf.Clamp01(Mathf.Abs(err) / 120f));
                float rate = speedGoal >= v.speed ? v.accel : (speedGoal <= 0.05f * v.cruise ? v.accel : 0.45f * v.accel);
                float s = Mathf.MoveTowards(v.speed, speedGoal, rate * Dt);
                // wind leeway + current, carried while under way; cancelled while holding position (engine / anchor holds the vessel)
                var drift = (state == "holding_obstacle" || state == "arrived") ? Vector2.zero : _current + _wind * v.leeway;
                // hull guard: if the turned / drifted pose would put the bow or stern into a structure / out of the water, hold the
                // heading (no swing), stop and cancel the drift; the vessel then waits for the obstacle hold / give-way logic
                var next = v.pos + Dir(h) * s * Dt + drift * Dt;
                if (!HullClearDense(v, next, h, v.B * 0.5f + 1f) && HullClearDense(v, v.pos, v.heading, v.B * 0.5f + 1f))
                {
                    h = v.heading; yaw = 0f; rudder = 0f; drift = Vector2.zero;
                    s = Mathf.MoveTowards(v.speed, 0f, v.accel * Dt); v.state = "holding_obstacle";
                    next = v.pos + Dir(h) * s * Dt;
                    if (!HullClearDense(v, next, h, v.B * 0.5f + 1f)) next = v.pos;
                }
                v.rudder = rudder; v.yawRate = yaw; v.rec.drift_xz_m_s = drift;
                newHead[i] = Mathf.Repeat(h, 360f);
                newSpeed[i] = s;
                newPos[i] = next;
            }
            if (Avoid) GuardVesselFootprints(newPos, newHead, newSpeed);
            for (int i = 0; i < n; i++)
            {
                var v = _vs[i];
                if (v.state != "holding_vessel") v.vesselHoldSteps = 0;
                if (v.moving) v.rec.distance_travelled_m += (newPos[i] - v.pos).magnitude;
                v.pos = newPos[i]; v.heading = newHead[i]; v.speed = newSpeed[i];
            }
            Steps++;
            Time = Steps * Dt;
        }

        // All steering proposals are computed from the same previous state. Check the entire step before committing any
        // proposal, including anchored boats, so order of the vessel list cannot make a collision disappear.
        void GuardVesselFootprints(Vector2[] positions, float[] headings, float[] speeds)
        {
            int n = _vs.Count;
            for (int pass = 0; pass < n; pass++)
            {
                bool changed = false;
                for (int i = 0; i < n; i++) for (int j = i + 1; j < n; j++)
                {
                    var a = _vs[i]; var b = _vs[j];
                    if (!a.moving && !a.anchored && !b.moving && !b.anchored) continue;
                    float reach = 0.5f * (a.L + b.L) + a.B + b.B + 4f;
                    if (Vector2.Distance(a.pos, b.pos) > reach + (a.speed + b.speed + a.cruise + b.cruise) * Dt) continue;
                    if (FootprintsOverlap(a, a.pos, a.heading, b, b.pos, b.heading, 0f)) continue; // invalid starting placement is reported separately
                    if (!WouldOverlapDuringStep(i, j, positions, headings)) continue;
                    // Let one vessel pass when its swept path clears a stopped counterpart. Stopping both at a crossing
                    // can deadlock their route followers indefinitely.
                    int first = a.moving && !b.moving ? i : b.moving && !a.moving ? j
                        : a.state == "give_way" && b.state != "give_way" ? i
                        : b.state == "give_way" && a.state != "give_way" ? j
                        : a.L <= b.L ? i : j;
                    int second = first == i ? j : i;
                    if (_vs[first].moving || _vs[first].anchored)
                    {
                        Hold(first, positions, headings, speeds); changed = true;
                    }
                    if (WouldOverlapDuringStep(i, j, positions, headings) && (_vs[second].moving || _vs[second].anchored))
                    {
                        Hold(second, positions, headings, speeds); changed = true;
                    }
                }
                if (!changed) break;
            }
        }

        bool WouldOverlapDuringStep(int i, int j, Vector2[] positions, float[] headings)
        {
            var a = _vs[i]; var b = _vs[j];
            for (int k = 1; k <= 4; k++)
            {
                float t = k * 0.25f;
                if (FootprintsOverlap(a, Vector2.Lerp(a.pos, positions[i], t), Mathf.LerpAngle(a.heading, headings[i], t),
                                      b, Vector2.Lerp(b.pos, positions[j], t), Mathf.LerpAngle(b.heading, headings[j], t))) return true;
            }
            return false;
        }

        static bool FootprintsOverlap(V a, Vector2 pa, float ha, V b, Vector2 pb, float hb, float margin = 0.75f)
        {
            var ga = a.e.geometry; var gb = b.e.geometry;
            var fa = Geometry2D.OrientedRect(pa, ha,
                new Vector2(ga.bounds_min.x - margin, ga.bounds_min.z - margin),
                new Vector2(ga.bounds_max.x + margin, ga.bounds_max.z + margin));
            var fb = Geometry2D.OrientedRect(pb, hb,
                new Vector2(gb.bounds_min.x - margin, gb.bounds_min.z - margin),
                new Vector2(gb.bounds_max.x + margin, gb.bounds_max.z + margin));
            return Geometry2D.ConvexOverlap(fa, fb);
        }

        void Hold(int i, Vector2[] positions, float[] headings, float[] speeds)
        {
            var v = _vs[i];
            positions[i] = v.pos; headings[i] = v.heading; speeds[i] = 0f;
            v.state = v.anchored ? "at_anchor" : "holding_vessel"; v.rudder = 0f; v.yawRate = 0f;
            if (v.moving) v.vesselHoldSteps++;
            v.rec.drift_xz_m_s = Vector2.zero;
        }

        /// <summary>At anchor: the bow is pulled towards the wind + current resultant (first-order lag), with a slow seeded yaw
        /// oscillation; the hull lies on the circle of radius scope + L/2 about the anchor. A pose that would touch a structure or
        /// leave the water is not taken (the previous pose stays).</summary>
        void StepAnchored(V v, out Vector2 pos, out float heading)
        {
            var env = _current + _wind * Mathf.Max(v.leeway, 0.02f) * 2f;      // anchored hulls present more windage than under way
            float tNext = Time + Dt;
            float osc = v.anchorAmp * Mathf.Sin((tNext / v.anchorPeriod * 360f + v.anchorPhase) * Mathf.Deg2Rad);
            float want = env.magnitude > 0.02f ? HeadingOf(-env) + osc : v.heading;
            float h = v.heading + Mathf.DeltaAngle(v.heading, want) * (1f - Mathf.Exp(-Dt / v.anchorT));
            var p = v.anchor - Dir(h) * (0.5f * v.L + v.scope);
            if (HullClearDense(v, p, h, v.B * 0.5f + 1f)) { pos = p; heading = Mathf.Repeat(h, 360f); }
            else { pos = v.pos; heading = v.heading; }
            v.rec.drift_xz_m_s = Vector2.zero;
        }

        public bool IsAnchored(string objectId) => _vs.Any(q => q.p.object_id == objectId && q.anchored);

        void UpdateRecords()
        {
            foreach (var v in _vs)
            {
                v.rec.position_xz = v.pos; v.rec.heading_deg = v.heading; v.rec.speed_kn = v.speed / Knot;
                v.rec.waypoint_index = v.wp; v.rec.state = v.state;
                v.rec.rudder = v.rudder; v.rec.yaw_rate_deg_s = v.yawRate;
                if (v.roam) v.rec.route = v.route.ToList();
            }
        }

        /// <summary>Pose at the current time for a scenario vessel (start pose for unknown ids).</summary>
        public bool TryGetPose(string objectId, out Vector2 xz, out float headingDeg)
        {
            var v = _vs.FirstOrDefault(q => q.p.object_id == objectId);
            if (v == null) { xz = default; headingDeg = 0; return false; }
            xz = v.pos; headingDeg = v.heading; return true;
        }

        public bool IsMoving(string objectId) => _vs.Any(q => q.p.object_id == objectId && q.moving);

        public NavigationRecord Record() => new NavigationRecord
        {
            sim_time_s = Time, dt_s = Dt, steps = Steps, collision_avoidance = Avoid,
            wind_from_deg = _wind.magnitude > 0f ? Mathf.Repeat(HeadingOf(-_wind), 360f) : 0f, wind_speed_kn = _wind.magnitude / Knot,
            current_set_deg = _current.magnitude > 0f ? Mathf.Repeat(HeadingOf(_current), 360f) : 0f, current_drift_kn = _current.magnitude / Knot,
            vessels = _vs.Select(v => JsonUtility.FromJson<NavVesselRecord>(JsonUtility.ToJson(v.rec))).ToList(),
        };
    }
}
