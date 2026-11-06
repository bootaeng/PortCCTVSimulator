using System.Collections.Generic;
using UnityEngine;
using static PortCCTV.HarborGeneric02;
using static PortCCTV.HarborParts;

namespace PortCCTV
{
    /// <summary>
    /// City marina on a beach-front (Busan Suyeong/Haeundae-like): breakwater-protected basin with floating pontoon docks,
    /// promenade, clubhouse, travel-lift dock, sand beach, dense high-rise apartments, a long bridge across the bay and
    /// mountains. Plausible traffic: yachts, dinghies, kayaks/canoes, cabin cruisers, motorboats, RIBs.
    /// </summary>
    public static class Marina01
    {
        public const string Id = "marina_01";

        public static readonly CameraPreset[] Cameras =
        {
            new CameraPreset { id = "mr_A_promenade", display_name = "A - promenade lamp post 7 m (berths)", position = new Vector3(-20f, 9.5f, 66f),
                euler_deg = new Vector3(11f, 185f, 0f), vertical_fov_deg = 45f, mount_note = "lamp post on the marina promenade; pontoon berths and the basin (berth watch)" },
            new CameraPreset { id = "mr_B_tower", display_name = "B - east headland 60 m (beach and coastal city)", position = new Vector3(912f, 60f, -230f),
                euler_deg = new Vector3(10f, 285f, 0f), vertical_fov_deg = 48f, mount_note = "mast on the headland at the east end of the beach; beachfront, high-rise shore, marina and distant bay bridge; Haeundae CCTV reference" },
            new CameraPreset { id = "mr_C_breakwater", display_name = "C - breakwater head 12 m (entrance approach)", position = new Vector3(96f, 12.5f, -209f),
                euler_deg = new Vector3(8f, 130f, 0f), vertical_fov_deg = 42f, mount_note = "mast on the south breakwater 18 m west of the red light; craft approaching the entrance from the bay" },
        };

        static readonly Dictionary<string, float> MastBases = new Dictionary<string, float>
        {
            { "mr_A_promenade", Deck }, { "mr_B_tower", 60f - CameraMastOnHill }, { "mr_C_breakwater", Crest },
        };

        public static HarborDefinition Build()
        {
            var e = Begin(20261107);
            var h = new HarborDefinition
            {
                harbor_id = Id, display_name = "Marina", harbor_type = "marina", water_reference_y = 0f, elements = e,
                description = "Beach-front city marina: pontoon docks, promenade, clubhouse, beach, high-rise apartments, bay bridge, mountains.",
            };

            // --- promenade / seawall and land
            Box("quay", -265, 260, Bottom, Deck, 60, 110, "concrete", id: "promenade");
            QuayFaceZ(-250, 250, 60, -1, 20f, 30f);
            Box("land", -1500, 1500, Bottom, Deck, 110, 1500, "grass", id: "city");
            HillIdx(2, -420f, 520f, -4f, 620f, 120f, 480f); TreesOnHill(2, -420f, 520f, -4f, 620f, 120f, 480f, 30);
            HillIdx(0, 760f, 560f, -4f, 560f, 100f, 420f); TreesOnHill(0, 760f, 560f, -4f, 560f, 100f, 420f, 25);
            for (int i = 0; i < 20; i++) Tree(R(-240, 90), R(112, 160), Deck, R(6, 10));
            ParkingLot(100f, 170f, 114f, 150f, 0.3f, "marina_car_park");          // marina users' car park behind the boat shed
            // dry storage next to the travel lift (Suyeong Bay yachting centre has ~155 dry berths): covered boats on cradles,
            // flagged as vessel-like non-vessel objects (on land, not labelled as vessels), and a boat forklift
            Box("pad", 174f, 252f, Deck, Deck + 0.02f, 113f, 152f, "lane_concrete");
            string[] tarps = { "blue", "white", "navy", "green", "blue" };
            int db = 0;
            foreach (var z in new[] { 123f, 141f })
                for (float x = 178f; x < 250f; x += 4.2f)
                    if (Rng.NextDouble() < 0.85) Prefab("dry_storage", "covered_boat_" + tarps[Rng.Next(tarps.Length)], new Vector3(x, Deck, z), 90f + R(-2f, 2f), Vector3.one * R(0.85f, 1.1f), id: $"dry_boat_{db++:00}", nonVessel: true);
            Prefab("crane", "empty_handler", new Vector3(240f, Deck, 132f), 180f, Vector3.one * 0.8f, id: "boat_forklift");
            Box("land", -1500, -265, Bottom, Deck, -200, 110, "grass", id: "land_west");
            StreetLamps(-240f, 240f, 66f, 24f, 1.8f);
            PromenadeFurniture(-250f, 250f, 72f, -1f);
            // real buildings (Sketchfab CC BY): beach-front towers (Haeundae-like) west of the camera hill, shops behind the promenade, boat shed
            // beach-front apartment / officetel line behind the promenade (Haeundae-like), shops at street level
            foreach (var (bx, key, sc) in new[] { (-250f, "bld_apt_complex", 0.9f), (-150f, "bld_hirise", 1f), (-80f, "bld_apt_slab", 1f), (-10f, "bld_hirise", 0.9f), (60f, "bld_officetel", 0.6f) })
                Prefab("building", key, new Vector3(bx, Deck, 205f), 0f, Vector3.one * sc);
            Boulevard(-265f, 260f, 112f, true, 4, true, 0f);                                                  // 해변로 (beach road) between the marina and the city
            foreach (var x in new[] { -130f, 130f }) SignalledCrossing(new Vector2(x, 112f), true, 14f, 8f, false);
            BusStop(0f, 112f + 8.5f, 1f);
            ShopRow(-120f, 130f, 128f, 180f);
            Prefab("building", "bld_warehouse3", new Vector3(190f, Deck, 92f), 0f, Vector3.one, id: "boat_shed");
            for (float x = -250; x <= 250; x += 25) Tree(x, 100, Deck, R(6, 9));
            Lifebuoys(-220f, 160f, 1.0f, 43.8f, 40f);

            // --- breakwaters: west arm and south arm; entrance at the south-east
            BreakwaterZ("bw_west", -215, 60, -265, -250, seaIsMinusX: true);
            BreakwaterX("bw_south", -265, 120, -215, -200, seaIsMinusZ: true);
            // Scanned coastal rock assets break up the otherwise uniform concrete seaward edge.
            foreach (float x in new[] { -205f, -105f, -5f, 85f })
                Prefab("shore", "shore_rock_01", new Vector3(x, -1.2f, -223f), R(0f, 360f), Vector3.one * 1.4f);
            Lighthouse("lh_red_s", 114f, -207.5f, Crest, "beacon_red", 11f);
            Lighthouse("lh_white_e", 262f, -150f, 0.8f, "beacon_white_band", 9f);

            // --- floating pontoon docks: two main walkways with finger pontoons, gangways to the promenade
            Pontoon(-235, 175, 40, 43.5f);
            Pontoon(-235, 175, -80, -76.5f);
            Pontoon(-10, -6.5f, -76.5f, 40);                 // spine linking the two walkways
            for (float x = -230; x <= 170; x += 13f)
            {
                Pontoon(x, x + 1.4f, 20, 40);                // fingers south of walkway 1
                Pontoon(x, x + 1.4f, -76.5f, -56.5f);         // fingers north of walkway 2
                Pontoon(x, x + 1.4f, -100, -80);             // fingers south of walkway 2
                Cyl("pile", x + 0.7f, 19.5f, -6, 3.5f, 0.45f, "steel_dark");
                Cyl("pile", x + 0.7f, -100.5f, -6, 3.5f, 0.45f, "steel_dark");
            }
            // Shore-power cabinets and cleats make the long walkways read as working berths at CCTV distance.
            for (float x = -215f; x < 165f; x += 26f)
            {
                Box("shore_power", x, x + 0.55f, 0.5f, 1.55f, 39.4f, 40.0f, "steel_galv");
                Box("shore_power", x, x + 0.55f, 1.55f, 1.68f, 39.4f, 40.0f, "crane_blue");
                Cyl("cleat", x + 4f, 42.7f, 0.5f, 0.72f, 0.42f, "steel_dark");
                Cyl("cleat", x + 4f, -77.2f, 0.5f, 0.72f, 0.42f, "steel_dark");
            }
            Box("gangway", -125, -122, 0.5f, Deck, 43.5f, 60, "steel_galv", id: "gangway_1");
            // fuel dock + travel lift dock at the east end
            Pontoon(195, 240, 30, 34);
            foreach (var x in new[] { 204f, 214f, 224f }) Prefab("fuel", "fuel_pump", new Vector3(x, 0.5f, 32f), 0f, Vector3.one);   // fuel dock
            Box("travel_lift", 238, 250, Deck, Deck + 0.5f, 25, 60, "concrete", id: "lift_dock_w");
            foreach (var x in new[] { 239f, 249f }) foreach (var z in new[] { 40f, 56f }) Box("travel_lift", x - 0.5f, x + 0.5f, Deck, Deck + 11, z - 0.5f, z + 0.5f, "crane_blue");
            foreach (var x in new[] { 239f, 249f }) Box("travel_lift", x - 0.6f, x + 0.6f, Deck + 11, Deck + 12.2f, 39, 57, "crane_blue");

            // --- clubhouse, boat storage shed, cafe strip
            if (Buildings) Box("building", -200, -130, Deck, Deck + 12, 70, 100, "facade_office", id: "clubhouse");
            if (Buildings) Box("building", -200.4f, -129.6f, Deck + 12, Deck + 12.6f, 69.6f, 100.4f, "roof_concrete");
            if (Buildings) Box("building", 150, 230, Deck, Deck + 14, 70, 105, "warehouse_wall", id: "boat_shed");
            if (Buildings) Box("building", 149.6f, 230.4f, Deck + 14, Deck + 14.6f, 69.6f, 105.4f, "warehouse_roof");
            if (Buildings) for (float x = -110; x < 130; x += 20) Box("cafe", x, x + 16, Deck, Deck + R(5, 9), 85, 100, Rng.NextDouble() < 0.5 ? "wall_white" : "wall_cream");

            // --- beach east of the marina
            for (int beachSegment = 0; beachSegment < 32; beachSegment++)
            {
                float x = 260f + beachSegment * 20f;
                float curve = -10f - 34f * Mathf.Sin((beachSegment + 0.5f) / 32f * Mathf.PI);
                Box("beach", x, x + 20.15f, Bottom, 0.7f, curve, 60f, "sand", id: "beach_" + beachSegment);
                BoxC("beach", new Vector3(x + 10f, -0.3f, curve - 6f), new Vector3(20.15f, 0.6f, 14f), "sand", 0, new Vector3(-6, 0, 0));
            }
            Box("land", 260, 900, Bottom, Deck, 60, 110, "concrete", id: "beach_promenade");
            BeachFurniture(300f, 880f, 5f, 40f, 0.7f);
            PromenadeFurniture(270f, 890f, 64f, -1f);
            Box("land", 900, 1500, Bottom, Deck, -400, 110, "grass", id: "land_east");
            for (int i = 0; i < 30; i++) Rock(R(890, 930), R(-390, -150), R(-1.5f, 0f), R(2.5f, 5f));
            Prefab("rock", "shore_rock_01", new Vector3(915f, -1.4f, -300f), 100, Vector3.one);
            Prefab("rock", "shore_rock_02", new Vector3(905f, -0.6f, -180f), 30, Vector3.one * 2f);
            // A varied tree/scrub fringe softens the otherwise straight beach-to-backland edge.
            for (float x = 300f; x < 870f; x += 21f) Tree(x + R(-4f, 4f), 121f + R(-3f, 7f), Deck, R(5f, 9f));
            Scrub(300f, 870f, 113f, 148f, 90, 2.5f, 5f);

            // --- dense high-rise apartments (Haeundae-like); camera B stands on the first one
            CameraHill(Cameras[1], 240f, 240f, 3, new Vector2(972f, -237f));
            // Detailed imported towers along the coastal road; no random box towers intersecting the city blocks.
            Boulevard(260f, 900f, 112f, true, 4, true);
            foreach (var (tx, scale) in new[] { (320f, 0.95f), (450f, 1.2f), (580f, 1.05f), (710f, 1.35f), (840f, 1.15f) })
                Prefab("building", "bld_hirise", new Vector3(tx, Deck, 142f), 0f, Vector3.one * scale);

            // --- west city shore: coastal road (joined by the bridge ramp at x = -330), beach car parks, shops, towers, a park
            CoastalRoad(-1500f, -268f, -185f, -1f, -345f, -315f);
            Box("road", -341f, -319f, Deck, Deck + 0.06f, -200f, -188.5f, "road_asphalt", id: "bridge_junction");
            Boulevard(-181.5f, 115f, -330f, false, 4, true);                                                  // bridge approach boulevard into the city
            SignalledCrossing(new Vector2(-330f, 0f), false, 14f, 8f, true);
            Prefab("street", "sign_gantry", new Vector3(-330f, Deck, -150f), 0f, Vector3.one);
            ParkingLot(-580f, -440f, -172f, -124f, 0.25f, "beach_car_park_w");
            GreenBuffer(-940f, -700f, -172f, -112f);
            ShopRow(-1250f, -960f, -160f, 180f);
            CityBlocks(-1340f, -372f, -88f, 100f, 100f);
            for (int i = 0; i < 45; i++) Tree(R(-1495f, -1350f), R(-170f, 100f), Deck, R(6f, 11f));          // park at the west end
            CityBlocks(-1500f, -720f, 100f, 450f, 112f);                                                      // city west of the hill
            CityBlocks(290f, 910f, 158f, 360f, 103f);                                                         // city behind the beach
            CityBlocks(-100f, 470f, 232f, 560f, 110f);                                                        // central city between the hills
            CityBlocks(1050f, 1500f, 120f, 560f, 110f);                                                       // east city
            CityBlocks(1050f, 1500f, 560f, 900f, 110f);
            CityBlocks(-1500f, -760f, 450f, 900f, 112f);
            CityBlocks(-700f, -290f, 105f, 270f, 105f);                                                       // between the west city and the promenade towers

            // --- bay bridge: cable-stayed main span on piers; approach viaducts land on the west headland and, after a quarter
            // turn, on the city shore road at x = -330 (the deck used to end in open water at both ends)
            float bz = -900f;
            Box("bridge", -2350, -560, 28, 31, bz - 12, bz + 12, "concrete", id: "bridge_deck");
            Box("bridge", -2350, -560, 31, 31.08f, bz - 11, bz + 11, "road_asphalt");
            foreach (var sd in new[] { -1f, 1f }) Box("bridge", -2350, -560, 31, 32, bz + sd * 11.7f - 0.3f, bz + sd * 11.7f + 0.3f, "concrete");
            for (float x = -2310; x <= -600; x += 110) Box("bridge", x - 3, x + 3, -10, 28, bz - 6, bz + 6, "concrete");
            // west end: approach viaduct descends 3.4 % onto a flat west shore (coast road + fishing hamlet), with a forested
            // headland behind it. The earlier tunnel-portal version read as a cut-off deck from camera B (user feedback).
            // The west shore starts at x -2450 so that, seen from camera B (east, 3.5 km away), the deck is seen descending onto
            // land in front of the headland instead of vanishing behind it.
            var westRamp = new List<Vector3>();
            for (float x = -2350f; x >= -2990f; x -= 20f) westRamp.Add(new Vector3(x, Mathf.Lerp(31f, Deck + 0.08f, (-2350f - x) / 640f), bz));
            Viaduct(westRamp, 22f, 60f);
            Box("land", -3700, -2450, Bottom, Deck, -1350, -450, "grass", id: "west_shore");
            for (float zz = -1340f; zz < -460f; zz += 3.4f) Rock(-2452f - R(0f, 3f), zz, R(-1.4f, 0.2f), R(2.4f, 4.2f));
            for (float xx = -3690f; xx < -2460f; xx += 3.4f) { Rock(xx, -1352f - R(0f, 3f), R(-1.4f, 0.2f), R(2.4f, 4.2f)); Rock(xx, -448f + R(0f, 3f), R(-1.4f, 0.2f), R(2.4f, 4.2f)); }
            Boulevard(-3700f, -2990f, bz, true, 4, false);                                                    // bridge road continues west
            Boulevard(-1330f, -470f, -3150f, false, 2, true);                                                 // coast road along the west shore
            SignalledCrossing(new Vector2(-3150f, bz), true, 14f, 8f, true);
            CoastalVillage(-3100f, -2760f, -1150f, -1f, 2, true);                                              // hamlet along a lane south of the bridge road
            CoastalVillage(-3100f, -2760f, -650f, 1f, 2, true);                                                // and north of it
            Farmland(-3650f, -3200f, -1330f, -960f);
            Farmland(-3650f, -3200f, -840f, -480f);
            Farmland(-2740f, -2470f, -1330f, -960f);
            Farmland(-2740f, -2470f, -840f, -480f);
            Scrub(-2740f, -2470f, -940f, -860f, 20, 3f, 6f);
            HillIdx(1, -4100f, -250f, -6f, 1300f, 220f, 1000f); TreesOnHill(1, -4100f, -250f, -6f, 1300f, 220f, 1000f, 60);   // headland north of the landing, off camera B's bridge axis
            var eastRamp = new List<Vector3>();
            const float turnR = 230f; float arc = Mathf.PI / 2f * turnR, straight = -200f - (bz + turnR), total = arc + straight;
            for (int k = 0; k <= 18; k++)
            {
                float ph = Mathf.PI / 2f * k / 18f, sAlong = turnR * ph;
                eastRamp.Add(new Vector3(-560f + turnR * Mathf.Sin(ph), Mathf.Lerp(31f, Deck + 0.08f, sAlong / total), bz + turnR - turnR * Mathf.Cos(ph)));
            }
            for (float zz = bz + turnR + 20f; zz <= -200f + 0.01f; zz += 20f)
                eastRamp.Add(new Vector3(-330f, Mathf.Lerp(31f, Deck + 0.08f, (arc + zz - (bz + turnR)) / total), zz));
            Viaduct(eastRamp, 20f);

            foreach (var tx in new[] { -1750f, -1150f })
            {
                foreach (var dz in new[] { -9f, 9f }) Box("bridge", tx - 3, tx + 3, -10, 105, bz + dz - 2, bz + dz + 2, "concrete");
                Box("bridge", tx - 3, tx + 3, 95, 100, bz - 11, bz + 11, "concrete");
                for (int k = 1; k <= 6; k++)
                    foreach (var s in new[] { -1f, 1f })
                        foreach (var dz in new[] { -9f, 9f })
                            Beam("bridge", new Vector3(tx, 100 - k * 4, bz + dz), new Vector3(tx + s * k * 45, 31, bz + dz), 0.5f, 0.5f, "steel_galv");
            }

            // --- mountains and headlands
            for (int i = 0; i < 8; i++) Hill(R(-2600, 2600), R(1500, 2800), -5, R(900, 1600), R(300, 600), R(700, 1200), R(0, 360));
            Hill(-200, 1100, Deck - 3, 900, 280, 600, 10);
            Hill(1200, -700, -6, 800, 160, 600, -20);
            Hill(-600, -1500, -8, 600, 120, 420, 40);
            Prefab("buoy", "ocean_buoy", new Vector3(180, -0.6f, -300), 0, Vector3.one * 1.6f, id: "buoy_1", nonVessel: true);
            Prefab("buoy", "lateral_marker", new Vector3(160, -1.0f, -235), 0, Vector3.one * 0.7f, id: "buoy_red_1", nonVessel: true);

            CameraMasts(Cameras, MastBases);

            h.water_regions.Add(Region("marina_basin", -250, 58, 250, 58, 250, -200, -250, -200));
            h.water_regions.Add(Region("bay", -2440, -215, 120, -215, 120, -200, 250, -200, 260, -200, 260, -22, 900, -22, 900, -400, 3000, -400, 3000, -4000, -2440, -4000));
            h.water_regions.Add(Region("beach_shallows", 250, 58, 260, 58, 260, -200, 250, -200));

            foreach (var a in new[] { "sailing_yacht", "sailing_dinghy", "kayak", "canoe", "cabin_cruiser", "motorboat", "rib", "speedboat", "motor_yacht", "police_boat", "sar_boat", "patrol_boat" })
                h.vessel_rules.Add(Rule(a, "leisure craft"));
            End();
            return h;
        }

        public static ScenarioConfig DefaultScenario()
        {
            var s = new ScenarioConfig { scenario_id = "marina_default", harbor_id = Id, camera_preset_id = "mr_B_tower" };
            string[] berth = { "sailing_yacht", "cabin_cruiser", "sailing_yacht", "motorboat", "sailing_yacht", "cabin_cruiser", "rib", "sailing_yacht", "motorboat", "cabin_cruiser" };
            int n = 0;
            for (int i = 0; i < berth.Length; i++)
            {
                float x = -223.5f + (i * 2 + 7) * 13f; // distribute the near berths across the camera's visible slips
                var a = berth[i];
                float imm = a == "sailing_yacht" ? 0.3f : a == "cabin_cruiser" ? 0.3f : 0.1f;
                s.vessels.Add(new VesselPlacement { object_id = $"v{n++:00}", asset_id = a, x = x, z = 30, heading_deg = 90, immersion_m = imm });
            }
            string[] outerBerth = { "cabin_cruiser", "sailing_yacht", "motorboat", "sailing_yacht", "rib", "cabin_cruiser", "sailing_yacht" };
            for (int i = 0; i < outerBerth.Length; i++)
            {
                float x = -223.5f + (i * 4 + 3) * 13f; // leave alternate slips open on the seaward walkway
                var a = outerBerth[i];
                float imm = a == "sailing_yacht" ? 0.3f : a == "cabin_cruiser" ? 0.3f : 0.1f;
                s.vessels.Add(new VesselPlacement { object_id = $"v{n++:00}", asset_id = a, x = x, z = -90, heading_deg = 90, immersion_m = imm });
            }
            s.vessels.Add(new VesselPlacement { object_id = $"v{n++:00}", asset_id = "sailing_dinghy", x = 60, z = -150, heading_deg = 40, immersion_m = 0.12f });
            s.vessels.Add(new VesselPlacement { object_id = $"v{n++:00}", asset_id = "kayak", x = 320, z = -235, heading_deg = 120, immersion_m = 0.1f });
            s.vessels.Add(new VesselPlacement { object_id = $"v{n++:00}", asset_id = "canoe", x = 345, z = -250, heading_deg = 200, immersion_m = 0.1f });
            s.vessels.Add(new VesselPlacement { object_id = $"v{n++:00}", asset_id = "sailing_yacht", x = 200, z = -320, heading_deg = 300, immersion_m = 0.3f });
            s.vessels.Add(new VesselPlacement { object_id = $"v{n++:00}", asset_id = "sailing_yacht", x = 40, z = -420, heading_deg = 120, immersion_m = 0.3f });
            s.vessels.Add(new VesselPlacement { object_id = $"v{n++:00}", asset_id = "speedboat", x = 150, z = -520, heading_deg = 60, immersion_m = 0.35f });
            s.vessels.Add(new VesselPlacement { object_id = $"v{n++:00}", asset_id = "sailing_yacht", x = 650, z = -450, heading_deg = 250, immersion_m = 0.3f });
            s.vessels.Add(new VesselPlacement { object_id = $"v{n++:00}", asset_id = "motor_yacht", x = 510, z = -610, heading_deg = 70, immersion_m = 1.6f });
            s.vessels.Add(new VesselPlacement { object_id = $"v{n++:00}", asset_id = "police_boat", x = 420, z = -300, heading_deg = 200, immersion_m = 0.6f });
            return s;
        }
    }
}
