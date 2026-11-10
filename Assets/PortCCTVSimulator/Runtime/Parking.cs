using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static PortCCTV.HarborGeneric02;

namespace PortCCTV
{
    /// <summary>One marked parking bay. Lines, wheel stop and vehicle are all generated from this record.</summary>
    [Serializable]
    public class ParkingBay
    {
        public string lot_id;
        public Vector3 center;          // bay centre on the surface
        public float yaw_deg;           // bay depth axis: local +z points from the back of the bay to its open edge
        public float width, depth;
        public string open_edge;        // "+x" | "-x" | "+z" | "-z": the side the vehicle enters from
        public int serving_aisle;       // index into ParkingLotInfo.aisles of the same lot
        public string use;              // car | small_truck | truck
        public string vehicle;          // prefab key, empty = free bay
    }

    /// <summary>Axis-aligned drive surface inside a lot (aisle or end cross-aisle), xz rectangle.</summary>
    [Serializable]
    public class ParkingAisle
    {
        public Vector2 min, max;
        public bool cross;              // end cross-aisle linking the row aisles
    }

    [Serializable]
    public class Driveway
    {
        public Vector2 min, max;        // from the lot edge to the road edge
        public string road_id;          // the road element reached
        public float width;
    }

    [Serializable]
    public class ParkingLotInfo
    {
        public string id, kind;         // kind: car_park | truck_park | service_row
        public Vector2 min, max;
        public float occupancy_requested;
        public int bays, occupied;
        public List<ParkingAisle> aisles = new List<ParkingAisle>();
        public List<Driveway> driveways = new List<Driveway>();
        public bool needs_driveway = true;
        public List<string> problems = new List<string>();
    }

    internal static partial class HarborParts
    {
        // Car bay 2.8 x 5.8 m and 6.5 m aisle, truck bay 4.5 x 20 m and 16 m aisle: design values carried over from the earlier
        // generator (not certified dimensions). Below the minimum size a lot is not built (the caller's surface stays as it is).
        public const float CarBayW = 2.8f, CarBayD = 5.8f, CarAisle = 6.5f, TruckBayW = 4.5f, TruckBayD = 20f, TruckAisle = 16f, Median = 1.6f;

        internal static readonly List<ParkingLotInfo> Lots = new List<ParkingLotInfo>();
        internal static readonly List<ParkingBay> Bays = new List<ParkingBay>();
        static int _lotN;

        internal static void ResetParking() { Lots.Clear(); Bays.Clear(); _lotN = 0; }

        static string OpenEdgeOf(float yaw)
        {
            float y = Mathf.Repeat(yaw, 360f);
            return y < 45f || y >= 315f ? "+z" : y < 135f ? "+x" : y < 225f ? "-z" : "-x";
        }

        /// <summary>Exactly round(occupancy x n) of the n bays get a vehicle (0 = none, 1 = all); `preferFront` biases the choice
        /// towards bays with a low `rank` (near the entrance / centre) without changing the count.</summary>
        static bool[] ChooseOccupied(IList<float> rank, float occupancy, float preferFront)
        {
            int n = rank.Count, k = Mathf.Clamp(Mathf.RoundToInt(Mathf.Clamp01(occupancy) * n), 0, n);
            var key = new float[n];
            for (int i = 0; i < n; i++)
            {
                float w = 1f + preferFront * (1f - Mathf.Clamp01(rank[i]));      // Efraimidis–Spirakis weighted sampling
                key[i] = Mathf.Pow(Mathf.Max(1e-6f, (float)Rng.NextDouble()), 1f / w);
            }
            var chosen = new bool[n];
            foreach (var i in Enumerable.Range(0, n).OrderByDescending(i => key[i]).Take(k)) chosen[i] = true;
            return chosen;
        }

        /// <summary>Lines (both sides + back), wheel stop and optional vehicle of one bay, all from the same record.</summary>
        static void EmitBay(ParkingBay b, float surfaceY)
        {
            var q = Quaternion.Euler(0f, b.yaw_deg, 0f);
            Vector3 P(float lx, float lz, float dy) => b.center + q * new Vector3(lx, 0f, lz) + Vector3.up * dy;
            float hw = b.width / 2f, hd = b.depth / 2f;
            foreach (float s in new[] { -1f, 1f })                                // side lines, back to front
                BoxC("parking_lines", P(s * hw, 0f, 0.012f), new Vector3(0.12f, 0.012f, b.depth - 0.1f), "paint_white", b.yaw_deg);
            BoxC("parking_lines", P(0f, -hd + 0.06f, 0.012f), new Vector3(b.width, 0.012f, 0.12f), "paint_white", b.yaw_deg);   // back line
            BoxC("wheel_stop", P(0f, -hd + 0.75f, 0.06f), new Vector3(1.7f, 0.12f, 0.16f), "concrete", b.yaw_deg);
            if (string.IsNullOrEmpty(b.vehicle)) return;
            // 60 % reversed in (nose towards the aisle), the rest nose in; squarely in the bay, small offset inside it only
            bool reversed = Rng.NextDouble() < 0.6;
            float yawNose = b.yaw_deg - 90f + (reversed ? 0f : 180f);
            var pos = P(R(-0.08f, 0.08f), R(-0.15f, 0.15f) + 0.1f, 0f);
            Vehicle("car", b.vehicle, new Vector3(pos.x, surfaceY, pos.z), yawNose);
        }

        /// <summary>
        /// Surface car park on an axis-aligned site (user 2026-11-09, docs/detail_review_20261109/parking_layout_target.png):
        /// rows of bays along x, each module = row A | aisle | row B with both rows opening onto the aisle; modules are
        /// joined back to back over a planted median (trees and lamps stay out of the drive space); a cross-aisle at each end
        /// links all aisles. Entrances are cut to the nearest road at the end of the map build (FinalizeAccess). Below one
        /// module the site gets a single row facing an aisle, and below that no car park at all.
        /// occupancy is exact (0 = empty, 1 = every bay); preferFront biases which bays are taken.
        /// smallTruckBays: that many bays at the end of the first row are reserved for 1-ton work trucks.
        /// </summary>
        public static ParkingLotInfo ParkingLot(float x0, float x1, float z0, float z1, float occupancy, string id = null, float smallTruckShare = 0f,
            bool trees = true, float preferFront = 1.5f)
        {
            float w = x1 - x0, d = z1 - z0;
            float endAisle = CarAisle;
            int n = Mathf.FloorToInt((w - 2f * endAisle - 0.4f) / CarBayW);
            float module = 2f * CarBayD + CarAisle;
            bool single = d < module + 0.4f;
            if (n < 3 || d < CarBayD + CarAisle + 0.4f) return null;            // too small: no car park here
            int modules = single ? 1 : Mathf.Max(1, Mathf.FloorToInt((d - 0.4f + Median) / (module + Median)));
            var info = new ParkingLotInfo { id = id ?? $"carpark_{_lotN++:00}", kind = "car_park", min = new Vector2(x0, z0), max = new Vector2(x1, z1), occupancy_requested = occupancy };
            const float y = Deck + 0.03f;
            Box("parking", x0, x1, Deck, y, z0, z1, "asphalt", id: info.id);
            // perimeter kerb as four boxes; FinalizeAccess cuts it where a driveway leaves the lot
            Box("kerb", x0 - 0.3f, x1 + 0.3f, Deck, Deck + 0.15f, z0 - 0.3f, z0, "lane_concrete");
            Box("kerb", x0 - 0.3f, x1 + 0.3f, Deck, Deck + 0.15f, z1, z1 + 0.3f, "lane_concrete");
            Box("kerb", x0 - 0.3f, x0, Deck, Deck + 0.15f, z0, z1, "lane_concrete");
            Box("kerb", x1, x1 + 0.3f, Deck, Deck + 0.15f, z0, z1, "lane_concrete");
            float runW = n * CarBayW, sx0 = (x0 + x1) / 2f - runW / 2f;
            float used = single ? CarBayD + CarAisle : modules * module + (modules - 1) * Median;
            float zs = (z0 + z1) / 2f - used / 2f;
            var bays = new List<ParkingBay>(); var rank = new List<float>();
            for (int m = 0; m < modules; m++)
            {
                float zA = zs + m * (module + Median);
                // row A: back at zA, opens +z onto the aisle; aisle; row B: opens -z onto the aisle, back at the module top
                float aisle0 = zA + CarBayD, aisle1 = aisle0 + CarAisle;
                int ai = info.aisles.Count;
                info.aisles.Add(new ParkingAisle { min = new Vector2(x0, aisle0), max = new Vector2(x1, aisle1) });
                for (int i = 0; i < n; i++)
                {
                    float cx = sx0 + (i + 0.5f) * CarBayW;
                    float r = Mathf.Abs(i - (n - 1) / 2f) / Mathf.Max(1f, n / 2f);
                    bays.Add(new ParkingBay { lot_id = info.id, center = new Vector3(cx, y, zA + CarBayD / 2f), yaw_deg = 0f, width = CarBayW, depth = CarBayD, open_edge = "+z", serving_aisle = ai, use = "car" });
                    rank.Add(r);
                    if (single) continue;
                    bays.Add(new ParkingBay { lot_id = info.id, center = new Vector3(cx, y, aisle1 + CarBayD / 2f), yaw_deg = 180f, width = CarBayW, depth = CarBayD, open_edge = "-z", serving_aisle = ai, use = "car" });
                    rank.Add(r);
                }
                // planted median behind row B of this module (= behind row A of the next): kerbed island, lamps, trees
                if (m < modules - 1)
                {
                    float zm = aisle1 + CarBayD;
                    Box("kerb", sx0, sx0 + runW, Deck, Deck + 0.15f, zm, zm + Median, "lane_concrete");
                    for (float x = sx0 + 6f; x < sx0 + runW - 2f; x += 28f) Prefab("street_light", "street_lamp", new Vector3(x, Deck + 0.15f, zm + Median / 2f), 0, Vector3.one * 2f);
                    if (trees) for (float x = sx0 + 20f; x < sx0 + runW - 6f; x += 28f) Tree(x, zm + Median / 2f, Deck + 0.15f, R(4.5f, 6.5f));
                }
            }
            // end cross-aisles linking every row aisle
            info.aisles.Add(new ParkingAisle { min = new Vector2(x0, z0), max = new Vector2(sx0, z1), cross = true });
            info.aisles.Add(new ParkingAisle { min = new Vector2(sx0 + runW, z0), max = new Vector2(x1, z1), cross = true });
            // reserved small-truck bays at the far end of the first row (kept apart from the cars)
            int nTruck = Mathf.Clamp(Mathf.RoundToInt(smallTruckShare * bays.Count), 0, n / 2);
            for (int i = 0; i < nTruck; i++) bays[(n - 1 - i) * (single ? 1 : 2)].use = "small_truck";
            var take = ChooseOccupied(rank, occupancy, preferFront);
            for (int i = 0; i < bays.Count; i++)
            {
                if (take[i]) bays[i].vehicle = bays[i].use == "small_truck" ? "truck_kei" : CarKey(0f);
                EmitBay(bays[i], y);
            }
            // lamps along the lot edge when there is no median
            if (modules == 1) for (float x = sx0 + 6f; x < sx0 + runW - 2f; x += 30f) Prefab("street_light", "street_lamp", new Vector3(x, Deck, z1 + 0.9f), 0, Vector3.one * 2f);
            if (trees)                                                            // edge trees outside the kerb (cut at driveways)
                for (float x = x0 + 4f; x < x1 - 3f; x += 9f) { Tree(x, z0 - 2.2f, Deck, R(5f, 7.5f)); Tree(x, z1 + 2.2f, Deck, R(5f, 7.5f)); }
            info.bays = bays.Count; info.occupied = bays.Count(b => !string.IsNullOrEmpty(b.vehicle));
            Bays.AddRange(bays); Lots.Add(info);
            return info;
        }

        /// <summary>Truck park: modules of truck row | 16 m aisle | truck row (bays 4.5 x 20 m along z), end cross-aisles,
        /// entrances cut at the end of the build. occupancy exact; loaded = share of parked trucks carrying a box.</summary>
        public static ParkingLotInfo TruckLot(float x0, float x1, float z0, float z1, float occupancy, float loaded, string id = null)
        {
            float w = x1 - x0, d = z1 - z0;
            int n = Mathf.FloorToInt((w - 2f * TruckAisle - 0.4f) / TruckBayW);
            float module = 2f * TruckBayD + TruckAisle;
            bool single = d < module + 0.4f;
            if (n < 2 || d < TruckBayD + TruckAisle + 0.4f) return null;
            int modules = single ? 1 : Mathf.Max(1, Mathf.FloorToInt((d - 0.4f + 1f) / (module + 1f)));
            var info = new ParkingLotInfo { id = id ?? $"truckpark_{_lotN++:00}", kind = "truck_park", min = new Vector2(x0, z0), max = new Vector2(x1, z1), occupancy_requested = occupancy };
            const float y = Deck + 0.03f;
            Box("parking", x0, x1, Deck, y, z0, z1, "asphalt", id: info.id);
            Box("kerb", x0 - 0.3f, x1 + 0.3f, Deck, Deck + 0.15f, z0 - 0.3f, z0, "lane_concrete");
            Box("kerb", x0 - 0.3f, x1 + 0.3f, Deck, Deck + 0.15f, z1, z1 + 0.3f, "lane_concrete");
            Box("kerb", x0 - 0.3f, x0, Deck, Deck + 0.15f, z0, z1, "lane_concrete");
            Box("kerb", x1, x1 + 0.3f, Deck, Deck + 0.15f, z0, z1, "lane_concrete");
            float runW = n * TruckBayW, sx0 = (x0 + x1) / 2f - runW / 2f;
            float used = single ? TruckBayD + TruckAisle : modules * module + (modules - 1) * 1f;
            float zs = (z0 + z1) / 2f - used / 2f;
            var bays = new List<ParkingBay>(); var rank = new List<float>();
            for (int m = 0; m < modules; m++)
            {
                float zA = zs + m * (module + 1f), a0 = zA + TruckBayD, a1 = a0 + TruckAisle;
                int ai = info.aisles.Count;
                info.aisles.Add(new ParkingAisle { min = new Vector2(x0, a0), max = new Vector2(x1, a1) });
                for (int i = 0; i < n; i++)
                {
                    float cx = sx0 + (i + 0.5f) * TruckBayW, r = Mathf.Abs(i - (n - 1) / 2f) / Mathf.Max(1f, n / 2f);
                    bays.Add(new ParkingBay { lot_id = info.id, center = new Vector3(cx, y, zA + TruckBayD / 2f), yaw_deg = 0f, width = TruckBayW, depth = TruckBayD, open_edge = "+z", serving_aisle = ai, use = "truck" }); rank.Add(r);
                    if (single) continue;
                    bays.Add(new ParkingBay { lot_id = info.id, center = new Vector3(cx, y, a1 + TruckBayD / 2f), yaw_deg = 180f, width = TruckBayW, depth = TruckBayD, open_edge = "-z", serving_aisle = ai, use = "truck" }); rank.Add(r);
                }
            }
            info.aisles.Add(new ParkingAisle { min = new Vector2(x0, z0), max = new Vector2(sx0, z1), cross = true });
            info.aisles.Add(new ParkingAisle { min = new Vector2(sx0 + runW, z0), max = new Vector2(x1, z1), cross = true });
            // lamps outside the bay backs (never in an aisle)
            for (float x = sx0 + 8f; x < sx0 + runW; x += 40f) { Prefab("street_light", "street_lamp", new Vector3(x, Deck, z0 - 0.9f), 0, Vector3.one * 2f); Prefab("street_light", "street_lamp", new Vector3(x, Deck, z1 + 0.9f), 0, Vector3.one * 2f); }
            var take = ChooseOccupied(rank, occupancy, 1f);
            for (int i = 0; i < bays.Count; i++)
            {
                if (take[i]) bays[i].vehicle = "truck_semi_a";
                EmitBayTruck(bays[i], y, loaded);
            }
            info.bays = bays.Count; info.occupied = bays.Count(b => !string.IsNullOrEmpty(b.vehicle));
            Bays.AddRange(bays); Lots.Add(info);
            return info;
        }

        static void EmitBayTruck(ParkingBay b, float y, float loaded)
        {
            var q = Quaternion.Euler(0f, b.yaw_deg, 0f);
            Vector3 P(float lx, float lz, float dy) => b.center + q * new Vector3(lx, 0f, lz) + Vector3.up * dy;
            float hw = b.width / 2f, hd = b.depth / 2f;
            foreach (float s in new[] { -1f, 1f })
                BoxC("parking_lines", P(s * hw, 0f, 0.012f), new Vector3(0.15f, 0.012f, b.depth - 0.1f), "paint_white", b.yaw_deg);
            BoxC("parking_lines", P(0f, -hd + 0.08f, 0.012f), new Vector3(b.width, 0.012f, 0.15f), "paint_white", b.yaw_deg);
            if (string.IsNullOrEmpty(b.vehicle)) return;
            // nose towards the aisle (driven out forwards). ContainerTruck's yaw is its travel direction (Prefab yaw 0 = +x) and the
            // rig spans about -6.2 .. +9.5 m along it from its origin, so the origin sits 1.6 m behind the bay centre.
            float travel = b.yaw_deg - 90f;
            var c = P(0f, -1.6f, 0f);
            ContainerTruck(c.x, c.z, travel, Rng.NextDouble() < loaded, Rng.NextDouble() < 0.8);
        }

        /// <summary>A row of marked bays along x opening onto a paved yard (market front, depot, warehouse forecourt): every bay
        /// is lined, exactly round(occupancy x n) carry a vehicle of `use`. zBack is the back edge, openDir +1 = opens to +z.
        /// The yard itself must have access (RegisterYardAccess).</summary>
        public static void ServiceRow(float x0, float x1, float zBack, float openDir, float occupancy, string use = "car", string lotId = null)
        {
            int n = Mathf.FloorToInt((x1 - x0) / CarBayW);
            if (n < 1) return;
            var info = new ParkingLotInfo { id = lotId ?? $"servicerow_{_lotN++:00}", kind = "service_row", min = new Vector2(x0, Mathf.Min(zBack, zBack + openDir * CarBayD)), max = new Vector2(x1, Mathf.Max(zBack, zBack + openDir * CarBayD)), occupancy_requested = occupancy, needs_driveway = false };
            float sx0 = (x0 + x1) / 2f - n * CarBayW / 2f, y = Deck + 0.03f;
            var bays = new List<ParkingBay>(); var rank = new List<float>();
            for (int i = 0; i < n; i++)
            {
                bays.Add(new ParkingBay { lot_id = info.id, center = new Vector3(sx0 + (i + 0.5f) * CarBayW, y, zBack + openDir * CarBayD / 2f), yaw_deg = openDir > 0 ? 0f : 180f, width = CarBayW, depth = CarBayD, open_edge = openDir > 0 ? "+z" : "-z", serving_aisle = -1, use = use });
                rank.Add(Mathf.Abs(i - (n - 1) / 2f) / Mathf.Max(1f, n / 2f));
            }
            var take = ChooseOccupied(rank, occupancy, 0.5f);
            for (int i = 0; i < n; i++)
            {
                if (take[i]) bays[i].vehicle = use == "small_truck" ? "truck_kei" : use == "van" ? $"van_a_{Rng.Next(6):00}" : CarKey(0f);
                EmitBay(bays[i], y);
            }
            info.bays = n; info.occupied = bays.Count(b => !string.IsNullOrEmpty(b.vehicle));
            Bays.AddRange(bays); Lots.Add(info);
        }

        /// <summary>A paved yard (depot / warehouse pad) that needs a gate to the street: FinalizeAccess cuts one driveway.</summary>
        public static void RegisterYardAccess(float x0, float x1, float z0, float z1, string id, float width = 8f)
        {
            Lots.Add(new ParkingLotInfo { id = id, kind = "yard", min = new Vector2(x0, z0), max = new Vector2(x1, z1), aisles = { new ParkingAisle { min = new Vector2(x0, z0), max = new Vector2(x1, z1), cross = true } } });
            _yardWidth[id] = width;
        }
        static readonly Dictionary<string, float> _yardWidth = new Dictionary<string, float>();

        static readonly HashSet<string> CuttableBoxKinds = new HashSet<string> { "sidewalk", "kerb", "guard_rail", "fence", "path", "lane", "green_strip" };
        static readonly HashSet<string> CuttablePrefabKinds = new HashSet<string>
        { "street_light", "tree", "street", "bench", "power_pole", "guard_rail", "barrier", "fence", "signal", "shrub" };
        static readonly HashSet<string> DrivewayBlockers = new HashSet<string>
        { "building", "house", "greenhouse", "tank", "crane", "container", "laydown", "hill", "apartment", "retaining", "pad", "field", "factory", "parking", "quay_wall" };

        /// <summary>
        /// End-of-build pass: every car / truck park and registered yard gets driveways to the nearest road. Candidate exits:
        /// each row aisle at both lot ends and each end cross-aisle at both lot sides; a driveway runs straight out from the
        /// lot edge until it meets a road carriageway (asphalt road strip), at most 40 m, and must not pass through buildings,
        /// house plots, fields, other parking surfaces or water. The best exit (shortest) is taken, plus a second one on another
        /// side for lots with more than one aisle. Kerbs, sidewalks, guardrails, fences and street furniture in the driveway
        /// are cut; the sidewalk keeps a lowered crossing so pedestrians still have a continuous path.
        /// </summary>
        internal static void FinalizeAccess(HarborDefinition h)
        {
            var roads = new List<(HarborElement el, Rect r)>(); var paved = new List<(HarborElement el, Rect r)>();
            var blockers = new List<Rect>();
            for (int i = 0; i < E.Count; i++)
            {
                var e = E[i];
                if (e.shape != "box" && e.shape != "prefab" && e.shape != "cylinder") continue;
                var r = RectXZ(e);
                if (e.kind == "road" && e.material == "road_asphalt" && e.shape == "box") roads.Add((e, r));
                else if ((e.kind == "land" || e.kind == "terrace" || e.kind == "quay") && e.shape == "box" && (e.material == "asphalt" || e.material == "concrete")) paved.Add((e, r));
                else if (DrivewayBlockers.Contains(e.kind ?? "")) blockers.Add(e.kind == "hill" ? Shrink(r, 0.42f) : r);
            }
            foreach (var lot in Lots)
            {
                if (!lot.needs_driveway) continue;
                var lotR = new Rect(lot.min, lot.max - lot.min);
                var cands = new List<(Vector2 p, Vector2 dir, float width)>();
                foreach (var a in lot.aisles)
                {
                    float cz = (a.min.y + a.max.y) / 2f, cx = (a.min.x + a.max.x) / 2f;
                    if (!a.cross)
                    {
                        float wa = a.max.y - a.min.y;
                        cands.Add((new Vector2(lot.min.x, cz), Vector2.left, wa)); cands.Add((new Vector2(lot.max.x, cz), Vector2.right, wa));
                    }
                    else
                    {
                        float wa = Mathf.Min(a.max.x - a.min.x, lot.kind == "yard" && _yardWidth.TryGetValue(lot.id, out var yw) ? yw : 99f);
                        cands.Add((new Vector2(cx, lot.min.y), Vector2.down, wa)); cands.Add((new Vector2(cx, lot.max.y), Vector2.up, wa));
                        if (lot.kind == "yard")
                        {
                            float wy = Mathf.Min(a.max.y - a.min.y, wa);
                            cands.Add((new Vector2(lot.min.x, cz), Vector2.left, wy)); cands.Add((new Vector2(lot.max.x, cz), Vector2.right, wy));
                        }
                    }
                }
                var found = new List<(float len, Vector2 p, Vector2 dir, float w, HarborElement road)>();
                foreach (var (p, dir, wa) in cands)
                {
                    float w = Mathf.Clamp(wa, 4f, 9f);
                    var perp = new Vector2(dir.y, dir.x);
                    for (float dlen = 0.5f; dlen <= 40f; dlen += 0.5f)
                    {
                        var q0 = p; var q1 = p + dir * dlen;
                        var rr = RectFrom(q0 - perp * (w / 2f), q1 + perp * (w / 2f));
                        if (h.InAnyWaterRegion(q1)) break;
                        var tip = RectFrom(q1 - dir * 0.5f - perp * (w / 2f), q1 + perp * (w / 2f));
                        if (blockers.Any(b => b.Overlaps(tip) && !Contains(Expand(lotR, 0.5f), b))) break;
                        int hit = roads.FindIndex(t => t.r.Overlaps(tip));
                        if (hit >= 0) { found.Add((dlen, p, dir, w, roads[hit].el)); break; }
                        int land = paved.FindIndex(t => t.r.Overlaps(tip) && t.r.Overlaps(lotR));
                        if (land >= 0 && dlen <= 0.5f) found.Add((dlen + 30f, p, dir, w, paved[land].el));        // opens straight onto the yard it stands on (roads preferred)
                    }
                }
                if (found.Count == 0) { lot.problems.Add("no driveway to a road within 40 m"); continue; }
                found.Sort((a, b) => a.len.CompareTo(b.len));
                var picks = new List<(float len, Vector2 p, Vector2 dir, float w, HarborElement road)> { found[0] };
                if (lot.aisles.Count(a => !a.cross) > 1 || lot.kind == "truck_park")
                {
                    var second = found.FirstOrDefault(f => f.dir != found[0].dir || (f.p - found[0].p).magnitude > 25f);
                    if (second.w > 0f) picks.Add(second);
                }
                foreach (var (len, p, dir, w, road) in picks)
                {
                    var perp = new Vector2(dir.y, dir.x);
                    var rr = RectFrom(p - perp * (w / 2f), p + dir * len + perp * (w / 2f));
                    // stop the driveway at the near edge of the carriageway (no overlap with the road surface)
                    var rd = RectXZ(road);
                    bool onYard = len >= 30f;
                    if (onYard) rr = RectFrom(p - perp * (w / 2f) - dir * 0.4f, p + dir * 0.4f + perp * (w / 2f));   // kerb opening only
                    else if (dir.x > 0) rr.xMax = Mathf.Min(rr.xMax, rd.xMin); else if (dir.x < 0) rr.xMin = Mathf.Max(rr.xMin, rd.xMax);
                    if (!onYard) { if (dir.y > 0) rr.yMax = Mathf.Min(rr.yMax, rd.yMin); else if (dir.y < 0) rr.yMin = Mathf.Max(rr.yMin, rd.yMax); }
                    CutAndPave(rr, dir, lot.id, !onYard);
                    lot.driveways.Add(new Driveway { min = rr.min, max = rr.max, road_id = road.id, width = w });
                }
            }
        }

        static readonly HashSet<string> ObstacleKinds = new HashSet<string>
        { "street_light", "tree", "street", "bench", "power_pole", "guard_rail", "barrier", "fence", "signal", "building", "house", "crane", "tank", "kerb", "container" };

        /// <summary>Access / geometry check of every car park, truck park and yard of a built definition: each bay inside its lot
        /// with its open edge on its serving aisle, row aisles reaching both end cross-aisles, at least one driveway (>= 4 m wide)
        /// that touches the lot and a road (or the paved yard it stands on), no obstacle (lamp, tree, sign, barrier, kerb island,
        /// building) on an aisle or driveway, and occupancy exactly round(requested x bays). Returns one line per problem.</summary>
        internal static List<string> Check(HarborDefinition h)
        {
            var problems = new List<string>();
            var obstacles = h.elements.Where(e => ObstacleKinds.Contains(e.kind ?? "") && (e.shape != "box" || e.kind == "kerb")).ToList();
            foreach (var lot in h.parking_lots)
            {
                foreach (var pr in lot.problems) problems.Add($"{lot.id}: {pr}");
                var lotR = Expand(new Rect(lot.min, lot.max - lot.min), 0.05f);
                var bays = h.parking_bays.Where(b => b.lot_id == lot.id).ToList();
                if (lot.kind != "yard" && bays.Count != lot.bays) problems.Add($"{lot.id}: {bays.Count} bay records, info says {lot.bays}");
                int occ = bays.Count(b => !string.IsNullOrEmpty(b.vehicle));
                if (lot.kind != "yard" && occ != Mathf.RoundToInt(Mathf.Clamp01(lot.occupancy_requested) * bays.Count)) problems.Add($"{lot.id}: occupancy {occ}/{bays.Count} for requested {lot.occupancy_requested}");
                if (lot.kind == "service_row") continue;
                foreach (var b in bays)
                {
                    var q = Quaternion.Euler(0f, b.yaw_deg, 0f);
                    Vector2 C(float lx, float lz) { var v = b.center + q * new Vector3(lx, 0f, lz); return new Vector2(v.x, v.z); }
                    var bayR = RectFrom(C(-b.width / 2f, -b.depth / 2f), C(b.width / 2f, b.depth / 2f));
                    if (!Contains(lotR, bayR)) problems.Add($"{lot.id}: bay at {b.center} outside the lot");
                    var front = RectFrom(C(-b.width / 2f + 0.2f, b.depth / 2f + 0.05f), C(b.width / 2f - 0.2f, b.depth / 2f + 1.0f));
                    if (b.serving_aisle < 0 || b.serving_aisle >= lot.aisles.Count) { problems.Add($"{lot.id}: bay at {b.center} has no aisle"); continue; }
                    var a = lot.aisles[b.serving_aisle];
                    if (!Contains(Expand(new Rect(a.min, a.max - a.min), 0.05f), front)) problems.Add($"{lot.id}: bay at {b.center} does not open onto its aisle");
                }
                var cross = lot.aisles.Where(a => a.cross).Select(a => Expand(new Rect(a.min, a.max - a.min), 0.05f)).ToList();
                foreach (var a in lot.aisles.Where(a => !a.cross))
                {
                    var ar = new Rect(a.min, a.max - a.min);
                    if (cross.Count(c => c.Overlaps(ar)) < 2) problems.Add($"{lot.id}: aisle {ar} does not reach both end cross-aisles");
                }
                if (lot.driveways.Count == 0) problems.Add($"{lot.id}: no driveway");
                foreach (var d in lot.driveways)
                {
                    if (d.width < 4f) problems.Add($"{lot.id}: driveway narrower than 4 m");
                    var dr = new Rect(d.min, d.max - d.min);
                    if (!Expand(dr, 0.6f).Overlaps(lotR)) problems.Add($"{lot.id}: driveway does not touch the lot");
                    // the road may since have been split (RoadNetwork: cut back at a hill foot -> "<id>_h<n>", widened mouths, ...):
                    // any surviving piece of it counts
                    bool reaches = h.elements.Any(e => (e.id == d.road_id || (e.id != null && e.id.StartsWith(d.road_id + "_h"))) && Expand(dr, 0.6f).Overlaps(RectXZ(e)));
                    if (!reaches) problems.Add($"{lot.id}: driveway does not reach {d.road_id}");
                }
                // obstacles on the drive surfaces (aisles shrunk 0.3 m at their edges, driveways)
                var drive = (lot.kind == "yard" ? new List<ParkingAisle>() : lot.aisles).Select(a => Expand(new Rect(a.min, a.max - a.min), -0.3f)).Concat(lot.driveways.Select(d => Expand(new Rect(d.min, d.max - d.min), -0.3f))).ToList();
                foreach (var o in obstacles)
                {
                    if (o.kind == "kerb")
                    {
                        var kr = RectXZ(o);
                        foreach (var dr in drive) if (kr.Overlaps(dr) && kr.width * kr.height < 2000f) { problems.Add($"{lot.id}: kerb {o.id} on a drive surface"); break; }
                        continue;
                    }
                    var pt = new Vector2(o.center.x, o.center.z);
                    if (drive.Any(r => r.Contains(pt))) problems.Add($"{lot.id}: {o.kind} {o.id} on a drive surface at {pt}");
                }
            }
            return problems;
        }

        internal static void CutAndPave(Rect rr, Vector2 dir, string lotId, bool pave = true, bool crossingSlab = true)
        {
            var sidewalkSpans = new List<Rect>();
            var keep = new List<HarborElement>();
            var add = new List<HarborElement>();
            var pad = Expand(rr, 0.6f);
            foreach (var e in E)
            {
                bool cut = false;
                if (e.shape == "box" && CuttableBoxKinds.Contains(e.kind ?? "") && Axis(e))
                {
                    var r = RectXZ(e);
                    if (r.Overlaps(rr))
                    {
                        cut = true;
                        if (e.kind == "sidewalk") { var ov = Intersect(r, rr); sidewalkSpans.Add(ov); }
                        // keep the parts of the box either side of the driveway (split along the box's long axis)
                        bool alongX = r.width >= r.height;
                        if (alongX)
                        {
                            if (r.xMin < rr.xMin - 0.05f) add.Add(Piece(e, r.xMin, rr.xMin, r.yMin, r.yMax));
                            if (r.xMax > rr.xMax + 0.05f) add.Add(Piece(e, rr.xMax, r.xMax, r.yMin, r.yMax));
                        }
                        else
                        {
                            if (r.yMin < rr.yMin - 0.05f) add.Add(Piece(e, r.xMin, r.xMax, r.yMin, rr.yMin));
                            if (r.yMax > rr.yMax + 0.05f) add.Add(Piece(e, r.xMin, r.xMax, rr.yMax, r.yMax));
                        }
                    }
                }
                else if ((e.shape == "prefab" || e.shape == "billboard_tree" || e.shape == "cylinder") && CuttablePrefabKinds.Contains(e.kind ?? ""))
                    cut = pad.Contains(new Vector2(e.center.x, e.center.z));
                if (!cut) keep.Add(e);
            }
            // guardrail runs cut by the driveway get an end terminal on each side of the opening
            var cutRails = E.Where(e => e.prefab == "guardrail_seg" && !keep.Contains(e)).ToList();
            E.Clear(); E.AddRange(keep); E.AddRange(add);
            if (cutRails.Count > 0)
            {
                bool rx = Mathf.Abs(dir.y) > 0f;                                     // driveway crosses an x-running rail
                float y0 = cutRails[0].center.y, yaw = cutRails[0].yaw_deg;
                if (rx)
                {
                    float zr = cutRails[0].center.z;
                    Prefab("guard_rail", "guardrail_end", new Vector3(rr.xMin - 0.5f, y0, zr), yaw + 180f, Vector3.one);
                    Prefab("guard_rail", "guardrail_end", new Vector3(rr.xMax + 0.5f, y0, zr), yaw, Vector3.one);
                }
                else
                {
                    float xr = cutRails[0].center.x;
                    Prefab("guard_rail", "guardrail_end", new Vector3(xr, y0, rr.yMin - 0.5f), yaw + 180f, Vector3.one);
                    Prefab("guard_rail", "guardrail_end", new Vector3(xr, y0, rr.yMax + 0.5f), yaw, Vector3.one);
                }
            }
            if (pave) Box("driveway", rr.xMin, rr.xMax, Deck, Deck + 0.06f, rr.yMin, rr.yMax, "road_asphalt", id: Uid("driveway_" + lotId));
            if (crossingSlab) foreach (var s in sidewalkSpans)                   // lowered sidewalk crossing over the driveway
                Box("sidewalk", s.xMin, s.xMax, Deck, Deck + 0.09f, s.yMin, s.yMax, "sidewalk_paving", id: Uid("sidewalk_crossing"));
        }

        static HarborElement Piece(HarborElement src, float x0, float x1, float z0, float z1)
            => new HarborElement
            {
                id = src.id + "_p" + Mathf.RoundToInt(x0 * 10f) + "_" + Mathf.RoundToInt(z0 * 10f), kind = src.kind, shape = "box", material = src.material,
                center = new Vector3((x0 + x1) / 2f, src.center.y, (z0 + z1) / 2f), size = new Vector3(x1 - x0, src.size.y, z1 - z0),
            };

        static bool Axis(HarborElement e)
        {
            float y = Mathf.Repeat(e.yaw_deg + e.euler_deg.y, 90f);
            return (y < 0.5f || y > 89.5f) && Mathf.Abs(e.euler_deg.x) < 0.5f && Mathf.Abs(e.euler_deg.z) < 0.5f;
        }

        internal static Rect RectXZ(HarborElement e)
        {
            Vector3 s = e.size;
            if (e.shape == "prefab")
            {
                float r = e.kind == "building" ? 14f : e.kind == "house" ? 6f : e.kind == "greenhouse" ? 5f : e.kind == "tank" ? 7f : e.kind == "crane" ? 7f : e.kind == "hill" ? 0f : 1.5f;
                if (e.kind == "hill") return new Rect(e.center.x - s.x / 2f, e.center.z - s.z / 2f, s.x, s.z);
                // apartment-plot buildings: their wrapped prefab footprints (ApartmentPlot) times the instance scale; the 14 m
                // radius under-reached a 40 m point tower and a junction mouth was paved under it (2026-11-09, marina (597,125))
                if (e.kind == "building" && e.prefab != null && BuildingFootprint.TryGetValue(e.prefab, out var fp))
                {
                    bool sw = Mathf.Abs(Mathf.Repeat(e.yaw_deg, 180f) - 90f) < 45f;
                    float bx = (sw ? fp.y : fp.x) * Mathf.Abs(s.x) / 2f + 1f, bz = (sw ? fp.x : fp.y) * Mathf.Abs(s.z) / 2f + 1f;
                    return new Rect(e.center.x - bx, e.center.z - bz, 2f * bx, 2f * bz);
                }
                return new Rect(e.center.x - r, e.center.z - r, 2f * r, 2f * r);
            }
            bool swap = Mathf.Abs(Mathf.Repeat(e.yaw_deg + e.euler_deg.y, 180f) - 90f) < 1f;
            float hx = (swap ? s.z : s.x) / 2f, hz = (swap ? s.x : s.z) / 2f;
            return new Rect(e.center.x - hx, e.center.z - hz, 2f * hx, 2f * hz);
        }

        static readonly System.Collections.Generic.Dictionary<string, Vector2> BuildingFootprint = new System.Collections.Generic.Dictionary<string, Vector2>
        { { "bld_apt_complex", new Vector2(26f, 60f) }, { "bld_apt_slab", new Vector2(17.9f, 43.4f) }, { "bld_hirise", new Vector2(38f, 38f) }, { "bld_officetel", new Vector2(33f, 26f) } };

        static Rect RectFrom(Vector2 a, Vector2 b) => Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
        static Rect Expand(Rect r, float m) => Rect.MinMaxRect(r.xMin - m, r.yMin - m, r.xMax + m, r.yMax + m);
        static Rect Shrink(Rect r, float k) { var c = r.center; return new Rect(c.x - r.width * k / 2f, c.y - r.height * k / 2f, r.width * k, r.height * k); }
        static bool Contains(Rect outer, Rect inner) => outer.xMin <= inner.xMin && outer.yMin <= inner.yMin && outer.xMax >= inner.xMax && outer.yMax >= inner.yMax;
        static Rect Intersect(Rect a, Rect b) => Rect.MinMaxRect(Mathf.Max(a.xMin, b.xMin), Mathf.Max(a.yMin, b.yMin), Mathf.Min(a.xMax, b.xMax), Mathf.Min(a.yMax, b.yMax));
    }
}
