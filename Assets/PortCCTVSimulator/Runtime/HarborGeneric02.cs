using System;
using System.Collections.Generic;
using UnityEngine;

namespace PortCCTV
{
    /// <summary>
    /// Detailed anonymised container/ferry port. Same water layout and camera mounts as generic_harbor_01
    /// (so camera presets and regression geometry stay valid) but with port-like structures:
    /// ship-to-shore gantry cranes, container yard with RTGs, quay furniture, rubble-mound breakwaters,
    /// beacons, navigation buoys, warehouses, terminal building, high-mast lighting, skyline and headlands.
    /// Fully deterministic (fixed seed). Every element has a collider matching what is rendered.
    /// </summary>
    public static class HarborGeneric02
    {
        public const string Id = "generic_harbor_02";
        internal const float Deck = 2.5f, Crest = 3.5f, Bottom = -12f;

        /// <summary>Procedural box buildings (apartments, houses, offices, sheds) are crude; off by default (user decision 2026-11-04).</summary>
        public static bool Buildings = false;
        internal const int MaxTrees = 2000;   // 560 -> 800 (2026-11-05): car parks, city blocks and farmland edges
        internal const float CameraMastOnHill = 8f;
        internal static int _treeCount;
        internal static List<HarborElement> E;
        internal static System.Random Rng;
        internal static int _n;

        internal static float R(float a, float b) => a + (float)Rng.NextDouble() * (b - a);
        internal static string Uid(string p) => $"{p}_{_n++:0000}";

        internal static void Box(string kind, float x0, float x1, float y0, float y1, float z0, float z1, string mat, float yaw = 0, string id = null)
            => E.Add(new HarborElement
            {
                id = id ?? Uid(kind), kind = kind, shape = "box",
                center = new Vector3((x0 + x1) / 2, (y0 + y1) / 2, (z0 + z1) / 2),
                size = new Vector3(x1 - x0, y1 - y0, z1 - z0), material = mat, yaw_deg = yaw
            });

        internal static void BoxC(string kind, Vector3 c, Vector3 size, string mat, float yaw = 0, Vector3 euler = default)
            => E.Add(new HarborElement { id = Uid(kind), kind = kind, shape = "box", center = c, size = size, material = mat, yaw_deg = yaw, euler_deg = euler });

        internal static void Cyl(string kind, float x, float z, float y0, float y1, float d, string mat, string id = null, bool nonVessel = false)
            => E.Add(new HarborElement
            {
                id = id ?? Uid(kind), kind = kind, shape = "cylinder", center = new Vector3(x, (y0 + y1) / 2, z),
                size = new Vector3(d, y1 - y0, d), material = mat, is_vessel_like_non_vessel = nonVessel
            });

        internal static void Prefab(string kind, string key, Vector3 pos, float yaw, Vector3 scale, Vector3 euler = default, string id = null, bool nonVessel = false)
            => E.Add(new HarborElement
            {
                id = id ?? Uid(kind), kind = kind, shape = "prefab", prefab = key, center = pos, size = scale, yaw_deg = yaw,
                euler_deg = euler, is_vessel_like_non_vessel = nonVessel
            });

        /// <summary>Straight beam between two points (box whose local z runs a -> b).</summary>
        internal static void Beam(string kind, Vector3 a, Vector3 b, float w, float h, string mat)
        {
            var d = b - a;
            var rot = Quaternion.LookRotation(d.normalized, Mathf.Abs(Vector3.Dot(d.normalized, Vector3.up)) > 0.99f ? Vector3.forward : Vector3.up);
            BoxC(kind, (a + b) / 2, new Vector3(w, h, d.magnitude), mat, 0, rot.eulerAngles);
        }

        /// <summary>Starts a new element list for a harbor builder (shared by all maps).</summary>
        internal static List<HarborElement> Begin(int seed)
        {
            E = new List<HarborElement>();
            Rng = new System.Random(seed);
            _n = 0; _treeCount = 0;
            return E;
        }

        internal static void End()
        {
            if (_treeCount > MaxTrees) Debug.Log($"[Harbor] {_treeCount} trees requested, {MaxTrees} placed (cap); later Tree() calls were dropped");
            E = null;
        }

        public static HarborDefinition Build()
        {
            E = new List<HarborElement>();
            Rng = new System.Random(20261104);
            _n = 0;
            var h = new HarborDefinition
            {
                harbor_id = Id,
                description = "Detailed anonymised port: container terminal (2 STS cranes, 8 yard blocks with RTGs) on the west half of the north quay, " +
                              "ferry/general-cargo side with warehouses and terminal building on the east half, rubble-mound breakwaters with " +
                              "crown walls and entrance beacons, channel buoys, high-mast lighting, distant skyline and headlands. Water reference y=0.",
                water_reference_y = 0f,
                elements = E,
            };

            Ground();
            QuayEdges();
            Breakwaters();
            StsCrane("sts_1", -100f);
            StsCrane("sts_2", -75f);
            ContainerYard();
            QuayTraffic();
            EastSide();
            Lighting();
            NavigationMarks();
            Backdrop();
            CameraMasts();
            WaterRegions(h);
            E = null;
            return h;
        }

        // ------------------------------------------------------------------ ground
        internal static void Ground()
        {
            // Quay aprons (concrete) and yard (asphalt); everything extends below water to hide the infinite ocean.
            Box("quay", -170, 150, Bottom, Deck, 60, 100, "concrete", id: "quay_north");
            Box("quay", -170, -110, Bottom, Deck, -240, 60, "concrete", id: "quay_west");
            Box("pier", -25, -5, Bottom, Deck, 0, 60, "concrete", id: "pier_finger");
            Box("quay", 125, 150, Bottom, Deck, 60, 100, "concrete", id: "quay_north_east_end");
            Box("land", -500, 500, Bottom, Deck, 100, 260, "asphalt", id: "yard_north");
            Box("land", -900, -170, Bottom, Deck, -240, 100, "asphalt", id: "land_west");
            Box("land", -900, 900, Bottom, Deck - 0.05f, 260, 1200, "grass", id: "land_far_north");
            Box("land", 500, 900, Bottom, Deck - 0.05f, 100, 260, "grass", id: "land_north_east");
            Box("land", -900, -500, Bottom, Deck - 0.05f, 100, 260, "grass", id: "land_north_west");
            Box("land", 150, 500, Bottom, Deck, 60, 100, "asphalt", id: "land_east_apron");

            // Painted yard markings: lanes along x between yard blocks, yellow walkway along the quay.
            for (float z = 104f; z < 250f; z += 30f)
                for (float x = -165f; x < 140f; x += 7f)
                    Box("marking", x, x + 3.5f, Deck, Deck + 0.02f, z, z + 0.15f, "paint_white");
            Box("marking", -170, 150, Deck, Deck + 0.02f, 97.6f, 97.9f, "paint_yellow", id: "walkway_line_n");
            Box("marking", -170, 150, Deck, Deck + 0.02f, 98.3f, 98.6f, "paint_yellow", id: "walkway_line_s");
        }

        // ------------------------------------------------------------------ quay edges
        internal static void QuayEdges()
        {
            // North quay face z=60 (minus the finger pier), pier faces, west quay face x=-110.
            void FaceZ(float x0, float x1, float z, float dir) // dir: -1 water is towards -z
            {
                float zf = z + dir * 0.08f;
                Box("quay_wall", x0, x1, -2.5f, 1.0f, Mathf.Min(z, zf), Mathf.Max(z, zf), "quay_wet");
                Box("coping", x0, x1, Deck, Deck + 0.08f, Mathf.Min(z, z - dir * 0.35f), Mathf.Max(z, z - dir * 0.35f), "paint_yellow");
                for (float x = x0 + 6f; x < x1 - 2f; x += 12f)
                    Cyl("fender", x, z + dir * 0.6f, -0.6f, 1.9f, 1.1f, "rubber");
                for (float x = x0 + 10f; x < x1 - 2f; x += 15f)
                {
                    Cyl("bollard", x, z - dir * 1.3f, Deck, Deck + 0.6f, 0.55f, "bollard");
                    Cyl("bollard", x, z - dir * 1.3f, Deck + 0.6f, Deck + 0.75f, 0.85f, "bollard");
                }
                for (float x = x0 + 20f; x < x1 - 2f; x += 40f)
                    Box("ladder", x - 0.22f, x + 0.22f, -1.8f, Deck, Mathf.Min(z, z + dir * 0.18f), Mathf.Max(z, z + dir * 0.18f), "paint_yellow");
            }
            void FaceX(float z0, float z1, float x, float dir)
            {
                float xf = x + dir * 0.08f;
                Box("quay_wall", Mathf.Min(x, xf), Mathf.Max(x, xf), -2.5f, 1.0f, z0, z1, "quay_wet");
                Box("coping", Mathf.Min(x, x - dir * 0.35f), Mathf.Max(x, x - dir * 0.35f), Deck, Deck + 0.08f, z0, z1, "paint_yellow");
                for (float z = z0 + 6f; z < z1 - 2f; z += 12f)
                    Cyl("fender", x + dir * 0.6f, z, -0.6f, 1.9f, 1.1f, "rubber");
                for (float z = z0 + 10f; z < z1 - 2f; z += 15f)
                {
                    Cyl("bollard", x - dir * 1.3f, z, Deck, Deck + 0.6f, 0.55f, "bollard");
                    Cyl("bollard", x - dir * 1.3f, z, Deck + 0.6f, Deck + 0.75f, 0.85f, "bollard");
                }
            }
            FaceZ(-110f, -25f, 60f, -1f);
            FaceZ(-5f, 110f, 60f, -1f);
            FaceZ(-25f, -5f, 0f, -1f);
            FaceX(0f, 60f, -25f, -1f);
            FaceX(0f, 60f, -5f, 1f);
            FaceX(-240f, 60f, -110f, 1f);

            // Crane rails on the container quay.
            Box("crane_rail", -170, -30, Deck, Deck + 0.12f, 63.42f, 63.58f, "steel_dark", id: "rail_seaside");
            Box("crane_rail", -170, -30, Deck, Deck + 0.12f, 93.42f, 93.58f, "steel_dark", id: "rail_landside");
        }

        // ------------------------------------------------------------------ breakwaters
        internal static void Rock(float x, float z, float baseY, float size)
        {
            int k = Rng.Next(0, 6);
            var s = new Vector3(size * R(0.8f, 1.3f), size * R(0.55f, 0.85f), size * R(0.8f, 1.3f));
            Prefab("rock", "rock_" + k, new Vector3(x, baseY, z), R(0, 360), s, new Vector3(R(-15, 15), 0, R(-15, 15)));
        }

        internal static void Breakwaters()
        {
            // East breakwater (x 110..125, z -255..60): core, roadway, seaward crown wall, armour rock both sides.
            Box("breakwater", 111, 124, Bottom, 1.6f, -255, 60, "breakwater", id: "breakwater_east_core");
            Box("breakwater", 112.5f, 121, 1.6f, Crest, -255, 60, "concrete", id: "breakwater_east_road");
            Box("breakwater", 121, 123.2f, 1.6f, 6.0f, -255, 60, "breakwater", id: "breakwater_east_crownwall");
            for (float z = -253f; z < 59f; z += 2.6f)
            {
                Rock(R(110.2f, 111.4f), z + R(-0.6f, 0.6f), R(-0.4f, 0.6f), R(2.2f, 3.2f));
                Rock(R(108.6f, 109.6f), z + 1.3f + R(-0.6f, 0.6f), R(-1.4f, -0.6f), R(2.0f, 3.0f));
                Rock(R(124.4f, 125.6f), z + R(-0.6f, 0.6f), R(0.2f, 1.6f), R(2.6f, 3.6f));
                Rock(R(126.4f, 127.8f), z + 1.3f + R(-0.6f, 0.6f), R(-1.2f, 0.2f), R(2.4f, 3.4f));
            }
            // South arms (z -255..-240) with the 55 m entrance at x 40..95.
            void SouthArm(string name, float x0, float x1)
            {
                Box("breakwater", x0, x1, Bottom, 1.6f, -254, -241, "breakwater", id: name + "_core");
                Box("breakwater", x0, x1, 1.6f, Crest, -251.5f, -242.5f, "concrete", id: name + "_road");
                Box("breakwater", x0, x1, 1.6f, 6.0f, -254, -251.5f, "breakwater", id: name + "_crownwall");
                for (float x = x0 + 1.2f; x < x1 - 1f; x += 2.6f)
                {
                    Rock(x + R(-0.6f, 0.6f), R(-240.8f, -239.6f), R(-0.4f, 0.6f), R(2.2f, 3.2f));
                    Rock(x + 1.3f + R(-0.6f, 0.6f), R(-238.8f, -238.0f), R(-1.4f, -0.6f), R(2.0f, 3.0f));
                    Rock(x + R(-0.6f, 0.6f), R(-256.2f, -255.0f), R(0.2f, 1.6f), R(2.6f, 3.6f));
                    Rock(x + 1.3f + R(-0.6f, 0.6f), R(-258.2f, -257.0f), R(-1.2f, 0.2f), R(2.4f, 3.4f));
                }
            }
            SouthArm("breakwater_south_w", -170f, 40f);
            SouthArm("breakwater_south_e", 95f, 110f);
            // Rocks rounding the entrance heads.
            for (int i = 0; i < 6; i++) { Rock(R(40f, 43f), R(-255f, -240f), R(-0.8f, 0.8f), R(2.4f, 3.2f)); Rock(R(92f, 95f), R(-255f, -240f), R(-0.8f, 0.8f), R(2.4f, 3.2f)); }

            // Entrance beacons (IALA-A, entering northbound: red to port = west head, green to starboard = east head).
            void Beacon(string id, float x, float z, string colour)
            {
                Cyl("beacon", x, z, Crest, Crest + 3.5f, 2.2f, "beacon_white", id + "_base");
                Cyl("beacon", x, z, Crest + 3.5f, Crest + 9.0f, 2.0f, colour, id + "_tower");
                Box("beacon", x - 0.9f, x + 0.9f, Crest + 9.0f, Crest + 10.6f, z - 0.9f, z + 0.9f, "glass_dark", id: id + "_lantern");
                Cyl("beacon", x, z, Crest + 10.6f, Crest + 11.0f, 2.2f, colour, id + "_cap");
            }
            Beacon("beacon_red_w", 36.5f, -247.5f, "beacon_red");
            Beacon("beacon_green_e", 98.5f, -247.5f, "beacon_green");
        }

        // ------------------------------------------------------------------ STS crane
        internal static void StsCrane(string name, float x)
        {
            const float z0 = 63.5f, z1 = 93.5f, top = 41f, boomY0 = 43.5f, boomY1 = 47f;
            const float tipZ = 12f, backZ = 111f;
            string kind = "sts_crane";
            foreach (var dx in new[] { -9f, 9f })
                foreach (var z in new[] { z0, z1 })
                {
                    Box(kind, x + dx - 0.9f, x + dx + 0.9f, Deck, top, z - 0.9f, z + 0.9f, "crane_white");
                    Box(kind, x + dx - 1.4f, x + dx + 1.4f, Deck, Deck + 1.6f, z - 2.2f, z + 2.2f, "crane_blue"); // bogie
                }
            foreach (var dx in new[] { -9f, 9f })
            {
                Box(kind, x + dx - 0.8f, x + dx + 0.8f, 11f, 12.6f, z0, z1, "crane_blue");           // sill beam
                Box(kind, x + dx - 0.9f, x + dx + 0.9f, top - 2.5f, top, z0, z1, "crane_white");     // upper girder
            }
            foreach (var z in new[] { z0, z1 })
                Box(kind, x - 10f, x + 10f, top - 2.5f, top + 0.4f, z - 1f, z + 1f, "crane_blue");   // portal beam
            // Portal bracing (seaside and landside frames).
            foreach (var z in new[] { z0, z1 })
            {
                Beam(kind, new Vector3(x - 9, 12.6f, z), new Vector3(x + 9, top - 2.5f, z), 0.6f, 0.6f, "crane_white");
                Beam(kind, new Vector3(x + 9, 12.6f, z), new Vector3(x - 9, top - 2.5f, z), 0.6f, 0.6f, "crane_white");
            }
            // Boom (two girders + walkway), extending seaward over the water and back over the yard.
            foreach (var dx in new[] { -1.7f, 1.7f })
                Box(kind, x + dx - 0.45f, x + dx + 0.45f, boomY0, boomY1, tipZ, backZ, "crane_white");
            Box(kind, x - 2.6f, x + 2.6f, boomY0 - 0.3f, boomY0, tipZ + 2, backZ - 2, "steel_dark");
            for (float z = tipZ + 3; z < backZ; z += 6f) Box(kind, x - 2.2f, x + 2.2f, boomY1 - 0.3f, boomY1, z, z + 0.5f, "crane_white");
            // A-frame apex and stays.
            const float apexZ = 70f, apexY = 70f;
            foreach (var dx in new[] { -3.5f, 3.5f })
            {
                Beam(kind, new Vector3(x + dx * 2f, boomY1, apexZ - 6f), new Vector3(x + dx, apexY, apexZ), 1.0f, 1.0f, "crane_white");
                Beam(kind, new Vector3(x + dx * 2f, boomY1, apexZ + 10f), new Vector3(x + dx, apexY, apexZ), 1.0f, 1.0f, "crane_white");
                Beam(kind, new Vector3(x + dx, apexY, apexZ), new Vector3(x + dx * 0.5f, boomY1, tipZ + 1f), 0.35f, 0.35f, "steel_dark");   // forestay
                Beam(kind, new Vector3(x + dx, apexY, apexZ), new Vector3(x + dx * 0.5f, boomY1, tipZ + 22f), 0.35f, 0.35f, "steel_dark");  // mid stay
                Beam(kind, new Vector3(x + dx, apexY, apexZ), new Vector3(x + dx * 0.5f, boomY1, backZ - 1f), 0.35f, 0.35f, "steel_dark");  // backstay
            }
            Box(kind, x - 4.5f, x + 4.5f, apexY - 1f, apexY + 1f, apexZ - 1f, apexZ + 1f, "crane_blue");
            // Machinery house on the back reach, trolley + cab + spreader over the water.
            Box(kind, x - 4.2f, x + 4.2f, boomY1, boomY1 + 5.5f, 97f, 109f, "crane_house");
            Box(kind, x - 4.3f, x + 4.3f, boomY1 + 5.5f, boomY1 + 6.0f, 96.8f, 109.2f, "crane_blue");
            float tz = 38f + R(-10f, 10f);
            Box(kind, x - 2.3f, x + 2.3f, boomY0 - 2.2f, boomY0 - 0.3f, tz - 3f, tz + 3f, "crane_blue");
            Box(kind, x + 2.3f, x + 4.6f, boomY0 - 4.8f, boomY0 - 1.6f, tz - 2f, tz + 2f, "glass_dark");
            float sy = R(14f, 30f);
            foreach (var dx in new[] { -1f, 1f })
                foreach (var dz in new[] { -2f, 2f })
                    Box(kind, x + dx - 0.04f, x + dx + 0.04f, sy + 0.8f, boomY0 - 2.2f, tz + dz - 0.04f, tz + dz + 0.04f, "steel_dark");
            Box(kind, x - 1.3f, x + 1.3f, sy, sy + 0.8f, tz - 6.2f, tz + 6.2f, "crane_yellow");
        }

        // ------------------------------------------------------------------ container yard
        internal static readonly string[] ContainerColours =
            { "container_red", "container_blue", "container_green", "container_grey", "container_orange", "container_white", "container_teal", "container_brown", "container_navy", "container_maroon" };

        internal static void Container(float x0, float z0, float y0, bool twentyFoot, float yaw = 0)
        {
            float len = twentyFoot ? 6.06f : 12.19f;
            var mat = ContainerColours[Rng.Next(ContainerColours.Length)];
            BoxC("container", new Vector3(x0 + len / 2, y0 + 1.295f, z0 + 1.22f), new Vector3(len, 2.59f, 2.44f), mat, yaw);
        }

        internal static void ContainerYard()
        {
            const float bay = 12.6f, row = 2.75f;
            foreach (var bx in new[] { -165f, -88f })
                foreach (var bz in new[] { 110f, 140f, 170f, 200f })
                {
                    for (int b = 0; b < 6; b++)
                        for (int r = 0; r < 6; r++)
                        {
                            float x = bx + b * bay, z = bz + r * row;
                            int tiers = Rng.NextDouble() < 0.08 ? 0 : 1 + Rng.Next(4);
                            for (int t = 0; t < tiers; t++)
                            {
                                float y = Deck + t * 2.59f;
                                if (Rng.NextDouble() < 0.3) { Container(x, z, y, true); Container(x + 6.13f, z, y, true); }
                                else Container(x, z, y, false);
                            }
                        }
                    // One rubber-tyred gantry per block, parked over a random bay.
                    float gx = bx + (1 + Rng.Next(4)) * bay + bay / 2;
                    float za = bz - 3.2f, zb = bz + 6 * row + 2.8f;
                    foreach (var dx in new[] { -6.5f, 6.5f })
                        foreach (var z in new[] { za, zb })
                        {
                            Box("rtg", gx + dx - 0.6f, gx + dx + 0.6f, Deck + 1.2f, Deck + 19f, z - 0.6f, z + 0.6f, "crane_yellow");
                            Box("rtg", gx + dx - 0.7f, gx + dx + 0.7f, Deck, Deck + 1.2f, z - 1.4f, z + 1.4f, "rubber");
                        }
                    foreach (var z in new[] { za, zb })
                        Box("rtg", gx - 7.2f, gx + 7.2f, Deck + 19f, Deck + 21f, z - 0.8f, z + 0.8f, "crane_yellow");
                    foreach (var dx in new[] { -6.5f, 6.5f })
                        Box("rtg", gx + dx - 0.7f, gx + dx + 0.7f, Deck + 19f, Deck + 21f, za, zb, "crane_yellow");
                    Box("rtg", gx - 2.5f, gx + 2.5f, Deck + 21f, Deck + 23.2f, za + 1f, za + 5f, "crane_house");
                }
        }

        // ------------------------------------------------------------------ trucks / quay traffic
        /// <summary>Terminal tractor + chassis (procedural prefabs, HarborParts.ContainerTruck); the chassis centre is at (x, z).</summary>
        internal static void Truck(float x, float z, float yaw, bool withContainer) => HarborParts.ContainerTruck(x, z, yaw, withContainer);

        internal static void QuayTraffic()
        {
            Truck(-100f, 78f, 0f, true);
            Truck(-75f, 84f, 180f, false);
            Truck(-130f, 133f, 0f, true);
            Truck(-40f, 163f, 180f, true);
            Truck(-120f, 193f, 0f, false);
            Truck(20f, 103.5f, 0f, true);
            // A few containers staged on the apron behind the cranes.
            for (int i = 0; i < 4; i++) Container(-160f + i * 13f, 95f - 2.6f, Deck, false);
        }

        // ------------------------------------------------------------------ east side buildings
        internal static void EastSide()
        {
            // Passenger/administration terminal (camera C mast stands on its roof, roof top y = 12.6).
            if (Buildings) Box("building", 20, 70, Deck, 12f, 114, 146, "facade_office", id: "terminal_building");
            if (Buildings) Box("building", 19.6f, 70.4f, 12f, 12.6f, 113.6f, 146.4f, "roof_concrete", id: "terminal_roof");
            if (Buildings) Box("building", 28, 34, 12.6f, 14.2f, 130, 136, "steel_dark");
            if (Buildings) Box("building", 52, 60, 12.6f, 13.8f, 136, 141, "steel_dark");
            if (Buildings) Box("building", 30, 60, Deck, 6.5f, 112, 114, "glass_dark", id: "terminal_entrance_glass");
            // Two godown warehouses (Innerscene CC0) on the east side.
            Prefab("warehouse", "godown_warehouse", new Vector3(98f, Deck, 128f), 0f, Vector3.one, id: "warehouse_godown_1");
            Prefab("warehouse", "godown_warehouse", new Vector3(98f, Deck, 170f), 0f, Vector3.one, id: "warehouse_godown_2");
            // Large transit shed with a pitched roof further east.
            if (Buildings) Box("building", 140, 210, Deck, 13f, 112, 152, "warehouse_wall", id: "transit_shed");
            float ang = 12f, half = 20f / Mathf.Cos(ang * Mathf.Deg2Rad);
            if (Buildings) BoxC("building", new Vector3(175, 13f + 2.1f, 122.1f), new Vector3(71, 0.5f, half + 0.6f), "warehouse_roof", 0, new Vector3(-ang, 0, 0));
            if (Buildings) BoxC("building", new Vector3(175, 13f + 2.1f, 141.9f), new Vector3(71, 0.5f, half + 0.6f), "warehouse_roof", 0, new Vector3(ang, 0, 0));
            if (Buildings) for (float x = 146; x < 206; x += 12) Box("building", x, x + 6, Deck, 8.5f, 111.7f, 112f, "steel_dark");
            // Harbor mobile crane (Innerscene CC0) parked on the north quay east of the pier.
            Prefab("mobile_crane", "mobile_crane", new Vector3(75f, Deck, 82f), 90f, Vector3.one, id: "mobile_crane_1");
            // Small gatehouse and offices at the yard gate.
            if (Buildings) Box("building", -18, -2, Deck, 6.5f, 232, 244, "facade_office");
            if (Buildings) Box("building", -18.3f, -1.7f, 6.5f, 6.9f, 231.7f, 244.3f, "roof_concrete");
            if (Buildings) Box("building", 0, 30, Deck, 9f, 228, 248, "facade_office");
            if (Buildings) Box("building", -0.3f, 30.3f, 9f, 9.4f, 227.7f, 248.3f, "roof_concrete");
        }

        // ------------------------------------------------------------------ lighting masts
        internal static void Lighting()
        {
            var masts = new[]
            {
                new Vector2(-168, 104), new Vector2(-90, 104), new Vector2(-12, 104), new Vector2(-168, 232),
                new Vector2(-90, 232), new Vector2(-12, 232), new Vector2(10, 98), new Vector2(60, 98), new Vector2(110, 98),
                new Vector2(-140, -100), new Vector2(-140, 0)
            };
            foreach (var m in masts)
            {
                Cyl("light_mast", m.x, m.y, Deck, Deck + 30f, 0.7f, "steel_galv");
                Box("light_mast", m.x - 2.2f, m.x + 2.2f, Deck + 30f, Deck + 30.6f, m.y - 2.2f, m.y + 2.2f, "steel_galv");
                foreach (var d in new[] { new Vector2(-1.6f, 0), new Vector2(1.6f, 0), new Vector2(0, -1.6f), new Vector2(0, 1.6f) })
                    Box("light_mast", m.x + d.x - 0.5f, m.x + d.x + 0.5f, Deck + 30.6f, Deck + 31.3f, m.y + d.y - 0.5f, m.y + d.y + 0.5f, "lamp");
            }
        }

        // ------------------------------------------------------------------ buoys
        internal static void NavigationMarks()
        {
            Prefab("buoy", "lateral_marker", new Vector3(88, -1.0f, -222), 0, Vector3.one * 0.8f, id: "buoy_red_in", nonVessel: true);
            Prefab("buoy", "lateral_marker", new Vector3(47, -1.0f, -222), 0, Vector3.one * 0.8f, id: "buoy_green_in", nonVessel: true);
            Prefab("buoy", "ocean_buoy", new Vector3(100, -0.6f, -280), 0, Vector3.one * 1.8f, id: "buoy_red_out", nonVessel: true);
            Prefab("buoy", "ocean_buoy", new Vector3(35, -0.6f, -280), 0, Vector3.one * 1.8f, id: "buoy_green_out", nonVessel: true);
        }

        // ------------------------------------------------------------------ backdrop (Korean coastal city look)
        internal static void Sphere(string kind, Vector3 c, float d, string mat) =>
            E.Add(new HarborElement { id = Uid(kind), kind = kind, shape = "sphere", center = c, size = Vector3.one * d, material = mat });

        internal static void Tree(float x, float z, float baseY, float hgt)
        {
            if (_treeCount++ >= MaxTrees) return;
            int k = Rng.Next(1, 3);
            float sc = hgt / (k == 1 ? 5.03f : 3.41f);  // Poly Haven island_tree_01/02 native heights
            Prefab("tree", "tree_0" + k, new Vector3(x, baseY, z), R(0, 360), new Vector3(sc, sc, sc));
        }

        /// <summary>Forested hill whose peak sits CameraMastOnHill below a camera preset (replaces roof-mounted cameras).</summary>
        internal static void CameraHill(CameraPreset p, float sx, float sz, int hillIndex, Vector2? centre = null)
        {
            // The hill centre sits behind the camera (opposite its view direction) so the camera stands on the seaward slope
            // and its own hill does not fill the lower half of the image. Shore cameras pass an explicit centre so the camera
            // stands on a headland flank right above the water (placement checked offline with Tools/search_camera_hill.py:
            // hill out of the protected water regions and default vessels, sea within ~60 m in front of the camera).
            float yaw = p.euler_deg.y * Mathf.Deg2Rad;
            var fwd = new Vector2(Mathf.Sin(yaw), Mathf.Cos(yaw));
            float d = 0.22f * Mathf.Min(sx, sz);
            float hx = centre?.x ?? p.position.x - fwd.x * d, hz = centre?.y ?? p.position.z - fwd.y * d, baseY = -5f;
            float u = (p.position.x - hx) / sx + 0.5f, w = (p.position.z - hz) / sz + 0.5f;
            float hUnit = Mathf.Max(ProcNoise.HillHeight(hillIndex, u, w), 0.05f);
            float sy = (p.position.y - CameraMastOnHill - baseY) / hUnit;
            // Meadow material (hill_m_*): the canopy macro texture only works beyond ~150 m; the camera sees this slope up close.
            Prefab("hill", "hill_m_" + hillIndex, new Vector3(hx, baseY, hz), 0f, new Vector3(sx, sy, sz), id: "camera_hill_" + p.id);
            // Village on the seaward slope: lanes / pads / terraces are baked into the hill texture from the same HillSpec.
            var spec = HillFeatures.Register(new HillFeatures.HillSpec
            {
                hillIndex = hillIndex, sx = sx, sy = sy, sz = sz, baseY = baseY, groundY = Deck, camU = u, camW = w,
                seawardAngleDeg = Mathf.Atan2(fwd.y / sz, fwd.x / sx) * Mathf.Rad2Deg,
            });
            var vil = HillFeatures.Get(spec);
            HillVillage(vil, hx, hz, baseY, sx, sy, sz);
            TreesOnHill(hillIndex, hx, hz, baseY, sx, sy, sz, 70, 1f, p.position, 60f, vil);
            // Dense trees around the camera: full-size outside its +-40 deg view cone, low scrub (3-5 m) inside it so the
            // foreground slope is dressed without walling off the harbour view from the 8 m mast.
            for (int i = 0; i < 150; i++)
            {
                bool inCone = i % 3 == 0;
                float ang = (inCone ? R(-55f, 55f) : R(55f, 305f)) * Mathf.Deg2Rad + yaw, dist = inCone ? R(60f, 130f) : R(60f, 130f);
                float px = p.position.x + Mathf.Sin(ang) * dist, pz = p.position.z + Mathf.Cos(ang) * dist;
                float y = HillY(hillIndex, hx, hz, baseY, sx, sy, sz, px, pz);
                if (float.IsNaN(y) || y < 0.5f) continue;
                if (vil.Blocked((px - hx) / sx + 0.5f, (pz - hz) / sz + 0.5f)) continue;
                Tree(px, pz, y - 0.3f, inCone ? R(3f, 5f) : R(6, 10));
            }
        }

        /// <summary>Hill with a known index and yaw 0, so slope heights can be evaluated for placing trees.</summary>
        internal static void HillIdx(int idx, float x, float z, float baseY, float sx, float sy, float sz)
            => Prefab("hill", (Mathf.Max(sx, sz) > 700f ? "hill_L_" : "hill_") + idx, new Vector3(x, baseY, z), 0f, new Vector3(sx, sy, sz));

        internal static float HillY(int idx, float hx, float hz, float baseY, float sx, float sy, float sz, float px, float pz)
        {
            float u = (px - hx) / sx + 0.5f, w = (pz - hz) / sz + 0.5f;
            if (u < 0.02f || u > 0.98f || w < 0.02f || w > 0.98f) return float.NaN;
            return baseY + sy * ProcNoise.HillHeight(idx, u, w);
        }

        internal static void TreesOnHill(int idx, float hx, float hz, float baseY, float sx, float sy, float sz, int count, float minY = 1f, Vector3 avoid = default, float avoidR = 0f, HillFeatures.Village vil = null)
        {
            for (int i = 0; i < count; i++)
            {
                float px = hx + R(-0.42f, 0.42f) * sx, pz = hz + R(-0.42f, 0.42f) * sz;
                float y = HillY(idx, hx, hz, baseY, sx, sy, sz, px, pz);
                if (float.IsNaN(y) || y < minY) continue;
                if (avoidR > 0f && Vector2.Distance(new Vector2(px, pz), new Vector2(avoid.x, avoid.z)) < avoidR) continue;
                if (vil != null && vil.Blocked((px - hx) / sx + 0.5f, (pz - hz) / sz + 0.5f)) continue;
                Tree(px, pz, y - 0.3f, R(6, 11));
            }
        }

        /// <summary>Places the 3-D part of a hill village (houses on cut terraces with a retaining wall below, utility poles)
        /// at the sites HillFeatures laid out; the lanes / pads / fields under them are in the baked hill texture.</summary>
        internal static void HillVillage(HillFeatures.Village v, float hx, float hz, float baseY, float sx, float sy, float sz)
        {
            float Y(float px, float pz) => HillY(v.spec.hillIndex, hx, hz, baseY, sx, sy, sz, px, pz);
            foreach (var h in v.houses)
            {
                var k = HillFeatures.HouseKinds[h.kind];
                float x = hx + (h.uv.x - 0.5f) * sx, z = hz + (h.uv.y - 0.5f) * sz;
                float yc = Y(x, z); if (float.IsNaN(yc)) continue;
                float gx = (Y(x + 1f, z) - Y(x - 1f, z)) / 2f, gz = (Y(x, z + 1f) - Y(x, z - 1f)) / 2f;
                var up = new Vector2(gx, gz); if (up.sqrMagnitude < 1e-8f) up = Vector2.up; up.Normalize();
                float halfD = k.d * h.scale / 2f;
                float yUp = Y(x + up.x * halfD, z + up.y * halfD); if (float.IsNaN(yUp)) yUp = yc;
                float baseH = Mathf.Lerp(yc, yUp, 0.45f);           // floor cut into the slope: terrace with a wall below
                Prefab("house", k.key, new Vector3(x, baseH, z), h.yawDeg, Vector3.one * h.scale, id: Uid("house"));
                float wx = x - up.x * (halfD + 0.4f), wz = z - up.y * (halfD + 0.4f);
                float yWall = Y(wx, wz); if (float.IsNaN(yWall)) yWall = yc - 1f;
                float hWall = Mathf.Clamp(baseH - yWall + 0.6f, 0.6f, 7f);
                BoxC("retaining", new Vector3(wx, baseH + 0.15f - hWall / 2f, wz), new Vector3(k.w * h.scale + 1.6f, hWall, 0.7f), "retaining_wall", h.yawDeg);
            }
            foreach (var p in v.poles)
            {
                float x = hx + (p.x - 0.5f) * sx, z = hz + (p.y - 0.5f) * sz;
                float y = Y(x, z); if (float.IsNaN(y) || y < baseY + 0.5f) continue;
                Prefab("power_pole", "power_pole", new Vector3(x, y - 0.2f, z), R(0, 360), Vector3.one);
            }
        }

        internal static void Apartment(float x, float z, float w, float d, float hgt, float yaw, string facade = "facade_apartment", bool roofStructure = true)
        {
            if (!Buildings) return;
            BoxC("apartment", new Vector3(x, Deck + hgt / 2, z), new Vector3(w, hgt, d), facade, yaw);
            BoxC("apartment", new Vector3(x, Deck + hgt + 0.4f, z), new Vector3(w + 0.6f, 0.8f, d + 0.6f), "roof_concrete", yaw);
            string roofMat = Rng.NextDouble() < 0.5 ? "roof_green" : "roof_blue";
            if (roofStructure) BoxC("apartment", new Vector3(x, Deck + hgt + 2.4f, z), new Vector3(w * 0.45f, 3.2f, d * 0.6f), roofMat, yaw);
        }

        internal static void House(float x, float z, float baseY)
        {
            if (!Buildings) return;
            float w = R(7, 13), d = R(7, 12), hgt = R(5, 12), yaw = R(-12, 12);
            string[] walls = { "wall_cream", "wall_white", "wall_pink", "wall_brick", "wall_grey" };
            string[] roofs = { "roof_blue", "roof_green", "roof_red", "roof_orange", "roof_grey", "roof_teal" };
            BoxC("house", new Vector3(x, baseY + hgt / 2, z), new Vector3(w, hgt, d), walls[Rng.Next(walls.Length)], yaw);
            BoxC("house", new Vector3(x, baseY + hgt + 0.3f, z), new Vector3(w + 0.8f, 0.6f, d + 0.8f), roofs[Rng.Next(roofs.Length)], yaw);
        }

        internal static void Hill(float x, float z, float baseY, float sx, float sy, float sz, float yaw)
            => Prefab("hill", (Mathf.Max(sx, sz) > 700f ? "hill_L_" : "hill_") + Rng.Next(0, 4), new Vector3(x, baseY, z), yaw, new Vector3(sx, sy, sz));

        internal static void Backdrop()
        {
            // --- mountains: a far ridge to the north, nearer forested hills behind the city.
            for (int i = 0; i < 9; i++)
                Hill(R(-2600, 2600), R(1500, 2800), -5, R(900, 1600), R(260, 520), R(700, 1200), R(0, 360));
            for (int i = 0; i < 6; i++)
                Hill(R(-1100, 1100), R(800, 1150), Deck - 3, R(350, 600), R(90, 190), R(300, 500), R(0, 360));

            // --- apartment tower clusters (high-rise residential), north-east and north-west of the port.
            CameraHill(CameraPresetLibrary.Get("cam_D_tower_overview"), 260f, 220f, 1);
            for (int i = 0; i < 26; i++)
            {
                float x = R(270, 760), z = R(290, 680);
                Apartment(x, z, R(18, 26), R(12, 16), R(55, 135), Rng.NextDouble() < 0.5 ? 0 : 90);
            }
            for (int i = 0; i < 14; i++)
            {
                float x = R(-780, -360), z = R(320, 640);
                Apartment(x, z, R(18, 26), R(12, 16), R(45, 110), Rng.NextDouble() < 0.5 ? 0 : 90);
            }
            for (int i = 0; i < (Buildings ? 8 : 0); i++)
            {
                float x = R(-250, 220), z = R(330, 520), w = R(25, 45);
                Box("office", x - w / 2, x + w / 2, Deck, Deck + R(25, 70), z - 15, z + 15, "facade_office");
            }

            // --- low-rise neighbourhood with coloured roofs on the west land and behind the yard.
            for (float x = -880; x < -210; x += 34f)
                for (float z = -225; z < 95; z += 34f)
                    if (Rng.NextDouble() < 0.55) House(x + R(-3, 3), z + R(-3, 3), Deck);
                    else if (Rng.NextDouble() < 0.25) Tree(x + R(-4, 4), z + R(-4, 4), Deck, R(6, 11));
            for (float x = -880; x < 900; x += 38f)
                for (float z = 268; z < 320; z += 26f)
                    if (Rng.NextDouble() < 0.5) House(x + R(-3, 3), z + R(-3, 3), Deck - 0.05f);
                    else if (Rng.NextDouble() < 0.4) Tree(x + R(-4, 4), z + R(-4, 4), Deck - 0.05f, R(6, 12));

            // --- east coast: beach, rocky point and a small resort district.
            Box("land", 560, 2200, Bottom, Deck, -380, 260, "grass", id: "land_east");
            Box("beach", 515, 560, Bottom, 0.6f, -380, 260, "sand", id: "beach_east");
            for (float z = -378; z < -260; z += 3f) Rock(R(512, 522), z, R(-1.2f, 0f), R(2.5f, 4.5f));
            for (int i = 0; i < 14; i++) Apartment(R(640, 980), R(-300, 200), R(16, 24), R(12, 15), R(30, 95), Rng.NextDouble() < 0.5 ? 0 : 90);
            for (int i = 0; i < 30; i++) Tree(R(575, 1100), R(-370, 250), Deck, R(7, 13));
            foreach (var (x, z) in new[] { (-215f, -275f), (-300f, -262f), (540f, -330f), (525f, -250f) })
                Prefab("rock", "shore_rock_01", new Vector3(x, -1.2f, z), R(0, 360), Vector3.one);

            // --- rocky shore along the west land south edge and the headlands either side of the open sea.
            for (float x = -900; x < -175; x += 3.2f) Rock(x + R(-1, 1), R(-242.5f, -240.5f), R(-1.4f, 0.2f), R(2.2f, 4.0f));
            Hill(-820, -560, -6, 760, 150, 620, 30);
            Hill(-1300, -950, -6, 900, 210, 760, 70);
            Hill(900, -620, -6, 700, 130, 560, -20);
            Hill(1450, -1050, -6, 950, 230, 800, 50);
            Prefab("headland", "coastal_cliff", new Vector3(-560, -2, -420), 62, new Vector3(4.5f, 4.5f, 4.5f));
            Prefab("headland", "coastal_cliff", new Vector3(660, -2, -470), -65, new Vector3(4.5f, 5f, 4.5f));

            // --- islands offshore (one rocky islet with a small white lighthouse).
            Hill(380, -1600, -8, 320, 85, 240, 10);
            Hill(-520, -2300, -8, 460, 120, 360, 80);
            Hill(1250, -2800, -8, 380, 70, 300, 30);
            Hill(-1500, -3200, -8, 600, 140, 420, 0);
            Prefab("islet", "rock_2", new Vector3(140, -3, -900), 25, new Vector3(30, 9, 22));
            Cyl("islet", 140, -900, 4.5f, 13f, 2.0f, "beacon_white", "islet_lighthouse");
            Box("islet", 139.2f, 140.8f, 13f, 14.4f, -900.8f, -899.2f, "glass_dark", id: "islet_lantern");
            Cyl("islet", 140, -900, 14.4f, 14.8f, 2.0f, "beacon_red", "islet_cap");

            // --- trees along the port's landside edge.
            for (float x = -160; x < 140; x += 14f) Tree(x + R(-2, 2), 255 + R(-2, 2), Deck, R(7, 11));
        }

        internal static readonly Dictionary<string, float> MastBase = new Dictionary<string, float>
        {
            { "cam_A_quay_mast", Deck }, { "cam_B_breakwater_head", Crest }, { "cam_C_warehouse_roof", Deck }, { "cam_D_tower_overview", 84f - CameraMastOnHill },
        };

        internal static void CameraMasts()
        {
            foreach (var p in CameraPresetLibrary.ForHarbor(Id))
            {
                float baseY = MastBase.TryGetValue(p.id, out var by) ? by : Deck;
                Box("mast", p.position.x - 0.18f, p.position.x + 0.18f, baseY, p.position.y - 0.6f, p.position.z - 0.18f, p.position.z + 0.18f, "steel_galv", id: "mast_" + p.id);
                Box("mast", p.position.x - 0.3f, p.position.x + 0.3f, p.position.y - 0.6f, p.position.y - 0.4f, p.position.z - 0.3f, p.position.z + 0.3f, "steel_galv", id: "mast_head_" + p.id);
            }
        }

        internal static void WaterRegions(HarborDefinition h)
        {
            var legacy = HarborLibrary.Get(HarborLibrary.LegacyHarborId);
            h.water_regions = legacy.water_regions;
        }
    }
}
