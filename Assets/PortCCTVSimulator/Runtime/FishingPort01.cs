using System.Collections.Generic;
using UnityEngine;
using static PortCCTV.HarborGeneric02;
using static PortCCTV.HarborParts;

namespace PortCCTV
{
    /// <summary>
    /// Korean coastal fishing port (어항): compact basin, tetrapod breakwaters with a red (west/port) and white (east/starboard)
    /// lighthouse, two fishing-boat piers and a pontoon, open-sided fish market, ice plant, slipway, hillside village on
    /// stepped terraces, forested hills and an offshore island. Plausible traffic: fishing boats, leisure fishing boats,
    /// small craft, island ferry, construction barge.
    /// </summary>
    public static class FishingPort01
    {
        public const string Id = "fishing_port_01";

        public static readonly CameraPreset[] Cameras =
        {
            new CameraPreset { id = "fp_A_market_roof", display_name = "A - fish market roof 11 m (harbour -> entrance)", position = new Vector3(-30f, 15f, 65f),
                euler_deg = new Vector3(10f, 168f, 0f), vertical_fov_deg = 42f, mount_note = "mast on the fish market roof; across the inner harbour to the entrance between the lighthouses" },
            new CameraPreset { id = "fp_B_hillside", display_name = "B - west headland 40 m (entrance watch)", position = new Vector3(-238f, 40f, -36f),
                euler_deg = new Vector3(12f, 108f, 0f), vertical_fov_deg = 40f, mount_note = "mast on the headland beside the west breakwater root; looks down on the entrance and the harbour mouth" },
            new CameraPreset { id = "fp_C_breakwater", display_name = "C - east breakwater 18 m (fishing quay and town)", position = new Vector3(178f, 18f, -40f),
                euler_deg = new Vector3(9f, 295f, 0f), vertical_fov_deg = 50f, mount_note = "elevated harbour watch across the fishing berths, waterfront town and low hills; Wando CCTV reference" },
        };

        static readonly Dictionary<string, float> MastBases = new Dictionary<string, float>
        {
            { "fp_A_market_roof", Deck + 9.4f }, { "fp_B_hillside", 40f - CameraMastOnHill }, { "fp_C_breakwater", Crest },
        };

        public static HarborDefinition Build()
        {
            var e = Begin(20261106);
            var h = new HarborDefinition
            {
                harbor_id = Id, display_name = "Fishing port", harbor_type = "fishing_port", water_reference_y = 0f, elements = e,
                description = "Korean fishing port: compact basin, tetrapod breakwaters with red/white lighthouses, fishing piers, fish market, hillside village.",
            };

            // --- shore, quay, piers
            Box("quay", -185, 185, Bottom, Deck, 60, 120, "concrete", id: "quay_shore");
            QuayFaceZ(-170, 170, 60, -1, 1000f, 12f);
            TyreFendersZ(-170, -92, 60, -1); TyreFendersZ(-80, 18, 60, -1); TyreFendersZ(30, 170, 60, -1);
            Box("pier", -92, -80, Bottom, Deck, -10, 60, "concrete", id: "pier_west");
            QuayFaceX(-10, 60, -92, -1, 1000f, 12f); QuayFaceX(-10, 60, -80, 1, 1000f, 12f); QuayFaceZ(-92, -80, -10, -1, 1000f, 12f);
            TyreFendersX(-10, 60, -92, -1); TyreFendersX(-10, 60, -80, 1);
            Box("pier", 18, 30, Bottom, Deck, 5, 60, "concrete", id: "pier_mid");
            QuayFaceX(5, 60, 18, -1, 1000f, 12f); QuayFaceX(5, 60, 30, 1, 1000f, 12f); QuayFaceZ(18, 30, 5, -1, 1000f, 12f);
            TyreFendersX(5, 60, 18, -1); TyreFendersX(5, 60, 30, 1);
            Lifebuoys(84f, 148f, 1.0f, 34.2f, 32f);
            QuayGear(-165f, 165f, 63f, 80f, 30, new Vector2(-92f, -80f), new Vector2(18f, 30f));
            Pontoon(80, 150, 30, 34); Pontoon(110, 114, 34, 58);
            // slipway (sloped ramp) at the east end of the quay
            BoxC("slipway", new Vector3(160, -0.4f, 40), new Vector3(18, 0.6f, 44), "concrete", 0, new Vector3(-7, 0, 0));

            // --- breakwaters: west arm turns east, east arm turns west -> offset entrance
            BreakwaterZ("bw_west", -135, 60, -185, -170, seaIsMinusX: true);
            BreakwaterX("bw_west_arm", -185, -30, -135, -120, seaIsMinusZ: true);
            BreakwaterZ("bw_east", -75, 60, 170, 185, seaIsMinusX: false);
            BreakwaterX("bw_east_arm", 40, 185, -75, -60, seaIsMinusZ: true);
            foreach (float x in new[] { -145f, -90f, 80f, 145f })
                Prefab("shore", "shore_rock_01", new Vector3(x, -1.3f, x < 0 ? -143f : -83f), R(0f, 360f), Vector3.one * 1.3f);
            Lighthouse("lh_red_w", -36f, -127.5f, Crest, "beacon_red", 12f, -90f);      // door along the arm towards the root (-x)
            Lighthouse("lh_white_e", 46f, -67.5f, Crest, "beacon_white_band", 12f, 90f); // door along the arm towards the root (+x)

            // --- fish market (open-sided shed), ice plant, cold store, fuel tank, gear piles
            Box("market", -70, 10, Deck + 8f, Deck + 8.6f, 64, 96, "warehouse_roof", id: "market_roof");
            for (float x = -68; x <= 8; x += 12) foreach (var z in new[] { 66f, 94f }) Box("market", x, x + 0.6f, Deck, Deck + 8f, z - 0.3f, z + 0.3f, "steel_galv");
            Box("market", -70, 10, Deck + 8.6f, Deck + 9.4f, 64, 96, "roof_blue", id: "market_parapet");
            if (Buildings) Box("building", 40, 70, Deck, Deck + 10, 70, 95, "wall_white", id: "ice_plant");
            if (Buildings) Box("building", 39.6f, 70.4f, Deck + 10, Deck + 10.5f, 69.6f, 95.4f, "roof_grey");
            if (Buildings) Box("building", 80, 115, Deck, Deck + 7, 75, 100, "warehouse_wall", id: "cold_store");
            if (Buildings) Box("building", 79.6f, 115.4f, Deck + 7, Deck + 7.5f, 74.6f, 100.4f, "roof_teal");
            Tank(140, 90, 8, 6);
            string[] gear = { "crate_01", "crate_02", "crate_01", "barrel", "tyre" };
            for (int i = 0; i < 70; i++)
            {
                float x = R(-160, 160), z = R(62.5f, 69);
                if (x > -95 && x < -77 || x > 15 && x < 33) continue;
                var key = gear[Rng.Next(gear.Length)];
                Prefab("fishing_gear", key, new Vector3(x, Deck, z), R(0, 360), Vector3.one, key == "tyre" ? new Vector3(90, 0, 0) : Vector3.zero);
            }
            // Working gear on the pier decks, in small clusters with clear vessel lanes on either side.
            foreach (float z in new[] { 12f, 36f, 52f })
            {
                Prefab("fishing_gear", "crate_01", new Vector3(-86f, Deck, z), 12f, Vector3.one);
                Prefab("fishing_gear", "crate_02", new Vector3(-85f, Deck, z + 1.3f), 82f, Vector3.one);
                Prefab("fishing_gear", "barrel", new Vector3(24f, Deck, z + 2f), 0f, Vector3.one);
            }
            BusStop(-60f, 118f + 5.5f, 1f);
            // light trucks and cars parked along the market road; market car park on the west end of the backland strip
            ServiceRow(-62f, 0f, 107f, -1f, 0.4f, "small_truck", "fp_market_truck_bays");                // 1-ton trucks at the market front
            ParkingLot(-330f, -240f, 126f, 167f, 0.45f, "fp_market_car_park", 0.08f, false);
            // real buildings (Sketchfab CC BY): shop row along the backland road, port office, cold store, towers on the east
            ShopRow(40f, 190f, 142f, 180f);                                                                    // 회센터 / shops facing the boundary road
            ShopRow(-180f, -20f, 142f, 180f);
            Prefab("building", "bld_villa_kr", new Vector3(240f, Deck, 150f), 180f, Vector3.one * 0.9f, id: "port_office");
            FishingTown(256f, 532f, 125f, 170f);                                                              // low-rise fishery shops, houses and alleys (R01)
            GreenBuffer(-700f, -330f, 126f, 160f);                                                             // buffer west of the shops
            Prefab("building", "bld_warehouse3", new Vector3(95f, Deck, 92f), 0f, Vector3.one * 0.6f, id: "cold_store");
            Prefab("building", "bld_ind_warehouse", new Vector3(140f, Deck, 100f), 0f, Vector3.one, id: "ice_plant");
            // ice plant: elevated ice conveyor from the plant to the quay edge (ice is chuted straight into the boats' holds)
            Beam("ice_conveyor", new Vector3(140f, Deck + 9f, 94f), new Vector3(140f, Deck + 5.5f, 61.5f), 1.6f, 1.4f, "steel_galv");
            foreach (var z in new[] { 86f, 76f, 66f }) Box("ice_conveyor", 139.6f, 140.4f, Deck, Deck + 5.5f + (z - 61.5f) * 3.5f / 32.5f, z - 0.4f, z + 0.4f, "steel_dark");
            Box("ice_conveyor", 139f, 141f, Deck + 4.2f, Deck + 5.6f, 60.6f, 62.4f, "crane_blue");
            // boat fuel station on the quay west of the market: tanks, pumps, canopy
            Tank(-150f, 72f, 3.2f, 4.5f); Tank(-144f, 72f, 3.2f, 4.5f);
            foreach (var x in new[] { -156f, -138f }) Prefab("fishing_gear", "fuel_pump", new Vector3(x, Deck, 63.2f), 0f, Vector3.one);
            Box("fuel_station", -160f, -134f, Deck + 4.6f, Deck + 5.0f, 61.5f, 66f, "roof_blue");
            foreach (var x in new[] { -159f, -135f }) Box("fuel_station", x - 0.15f, x + 0.15f, Deck, Deck + 4.6f, 63.6f, 63.9f, "steel_galv");
            // (stall-tent street removed at the user's request 2026-11-05: tents looked crude; a proper direct-sale market building is pending)
            ApartmentPlot(330f, 520f, 200f, 320f);                                                             // small apartment estate behind the port
            for (float x = -150f; x <= 150f; x += 60f) Prefab("power_pole", "power_pole", new Vector3(x, Deck, 130f), 90, Vector3.one);

            // --- hillside behind the port: backland strip, then forested hills (camera B on the central one)
            Box("terrace", -700, 700, Bottom, Deck, 120, 170, "asphalt", id: "backland");
            // port boundary road behind the backland strip, then the village beyond. (The two 97 m access streets north of it
            // ended in the hill-foot grass with nothing to serve; removed 2026-11-09.)
            Boulevard(-700f, 700f, 118f, true, 2, true, 0f);
            Box("terrace", -700, 700, Bottom, Deck - 0.05f, 170, 700, "grass", id: "hill_base");
            CoastalVillage(-680f, -360f, 174f, -1f, 2, false);
            RoadZ(124f, 177.5f, -356.5f, 7f);                                    // village street down to the port road
            Farmland(540f, 700f, 172f, 215f);                                                                 // fields on the hill foot strip
            CameraHill(Cameras[1], 240f, 240f, 0, new Vector2(-293f, -13f));
            HillIdx(1, -250f, 330f, -4f, 520f, 100f, 420f); TreesOnHill(1, -250f, 330f, -4f, 520f, 100f, 420f, 30);   // backdrop hill behind the port
            HillIdx(2, 240f, 420f, -4f, 520f, 110f, 420f); TreesOnHill(2, 240f, 420f, -4f, 520f, 110f, 420f, 35);
            HillIdx(3, -520f, 460f, -4f, 480f, 95f, 400f); TreesOnHill(3, -520f, 460f, -4f, 480f, 95f, 400f, 30);
            for (float x = -160; x <= 160; x += 40f) Tree(x + R(-6, 6), 122 + R(-2, 6), Deck, R(6, 10));

            // --- rocky shores outside the breakwaters, hills, island
            Box("land", -1500, -185, Bottom, Deck, -40, 500, "grass", id: "land_west");
            Box("land", 185, 1500, Bottom, Deck, -20, 500, "grass", id: "land_east");
            for (float z = -38; z < 58; z += 3.2f) { Rock(-188 - R(0, 3), z, R(-1.2f, 0.4f), R(2.2f, 4f)); }
            Prefab("rock", "shore_rock_01", new Vector3(-240f, -1.4f, -70f), 15, Vector3.one);
            Prefab("rock", "shore_rock_01", new Vector3(260f, -1.4f, -55f), 200, Vector3.one);
            Prefab("rock", "shore_rock_02", new Vector3(-210f, -0.6f, 20f), 80, Vector3.one * 2f);
            Prefab("rock", "shore_rock_02", new Vector3(215f, -0.6f, 10f), 260, Vector3.one * 2f);
            for (float x = -1400; x < -190; x += 3.4f) Rock(x, -42 - R(0, 3), R(-1.4f, 0.2f), R(2.4f, 4.2f));
            // coastal road + village on the west shore, in front of the hillside camera (between the hill foot and the rocky shore)
            CoastalVillage(-980f, -430f, -28f, -1f, 2, true);
            RoadZ(-31.5f, 114f, -426.5f, 7f);                                    // the village street joins the port road (was isolated)
            Scrub(-1000f, -420f, -38f, -34f, 30); Scrub(-1400f, -980f, -30f, 60f, 40, 4f, 8f);
            for (float x = 566; x < 1400; x += 3.4f) Rock(x, -22 - R(0, 3), R(-1.4f, 0.2f), R(2.4f, 4.2f));
            // scanned rubble + sand shore strips (Sketchfab CC BY "Breakwater coast", 92 m each): rock band against the land, sand seaward
            for (int i = 0; i < 4; i++) Prefab("shore", "prop_shore_scan", new Vector3(238f + i * 86f, 0.2f, -26f), 180f + R(-2f, 2f), Vector3.one);
            // Small shore-side service zone: sheds and fuel tanks rather than residential buildings in the CCTV view.
            Prefab("building", "bld_warehouse_blue", new Vector3(335f, Deck, 90f), 180f, Vector3.one, id: "east_gear_shed");
            Prefab("building", "bld_ind_warehouse", new Vector3(475f, Deck, 105f), 180f, Vector3.one, id: "east_service_shed");
            Tank(545f, 105f, 12f, 8f);
            // east shore land: coast road, anglers' car park, seafood processing plant with a truck yard, greenhouses and fields
            CoastalRoad(186.5f, 1450f, -11f, -1f);                                                            // flush with the west edge of the access road
            RoadZ(-7.5f, 120f, 190f, 7f);
            Prefab("building", "bld_warehouse_blue", new Vector3(265f, Deck, 43f), 180f, Vector3.one * 0.8f, id: "east_net_store");
            Box("loading_pad", 600f, 760f, Deck, Deck + 0.03f, 0f, 36f, "lane_concrete", id: "fp_plant_loading_pad");
            for (float x = 610f; x < 750f; x += 12f) Prefab("fishing_gear", "crate_02", new Vector3(x, Deck + 0.03f, 18f), 90f, Vector3.one);
            Prefab("building", "bld_warehouse3", new Vector3(680f, Deck, 60f), 0f, Vector3.one, id: "seafood_plant");
            Prefab("building", "bld_warehouse_blue", new Vector3(630f, Deck, 100f), 0f, Vector3.one, id: "plant_cold_store_1");
            Prefab("building", "bld_brick_factory", new Vector3(705f, Deck, 102f), 0f, Vector3.one, id: "plant_cold_store_2");
            ServiceRow(632f, 660f, 72f, 1f, 0.5f, "small_truck", "fp_plant_truck_bays");
            Greenhouses(790f, 6f, 12, 50f);
            Greenhouses(790f, 66f, 12, 50f);
            Greenhouses(910f, 6f, 9, 70f);
            for (float x = 1000f; x < 1430f; x += R(30f, 42f))
                for (float z = 6f; z < 300f; z += R(24f, 34f))
                    if (Rng.NextDouble() < 0.7) BoxC("field", new Vector3(x + 14f, Deck + 0.02f, z + 10f), new Vector3(R(22f, 30f), 0.04f, R(16f, 22f)), "field", R(-3f, 3f));
                    else if (Rng.NextDouble() < 0.5) Prefab("house", "shed_steel", new Vector3(x + 10f, Deck, z + 8f), R(-5f, 5f), Vector3.one);
                    else for (int t = 0; t < 4; t++) Tree(x + R(0f, 26f), z + R(2f, 18f), Deck, R(5f, 9f));
            for (float x = 790f; x < 1000f; x += R(30f, 40f)) BoxC("field", new Vector3(x + 14f, Deck + 0.02f, 160f), new Vector3(R(22f, 30f), 0.04f, 30f), "field", R(-2f, 2f));
            Scrub(560f, 740f, 125f, 175f, 30, 2.5f, 6.5f);
            Farmland(780f, 1440f, 310f, 470f);
            Farmland(-1480f, -780f, 82f, 250f);                                  // farmland behind the west-shore village
            Farmland(-760f, -520f, 82f, 250f);
            for (float x = 300f; x < 760f; x += R(32f, 44f))                                                    // farmland behind the sheds
                for (float z = 132f; z < 320f; z += R(26f, 34f))
                    if (Rng.NextDouble() < 0.75) BoxC("field", new Vector3(x + 14f, Deck + 0.02f, z + 10f), new Vector3(R(24f, 32f), 0.04f, R(18f, 24f)), "field", R(-3f, 3f));
                    else for (int t = 0; t < 3; t++) Tree(x + R(0f, 28f), z + R(2f, 20f), Deck, R(6f, 10f));
            Box("lane", 300f, 760f, Deck, Deck + 0.04f, 124f, 128f, "lane_concrete");
            Hill(-450, 900, 10, 900, 230, 700, 20);
            Hill(400, 950, 10, 1000, 260, 800, -10);
            for (int i = 0; i < 6; i++) Hill(R(-2500, 2500), R(1700, 2900), -5, R(900, 1600), R(260, 500), R(700, 1200), R(0, 360));
            Hill(-900, -350, -6, 700, 140, 500, 50);
            Hill(1000, -300, -6, 700, 130, 500, -40);
            Prefab("landmark_lighthouse", "lighthouse_tall", new Vector3(1000f, 0f, -300f), 0f, Vector3.one, id: "landmark_lighthouse");
            Hill(260, -1600, -8, 520, 52, 360, 15);                // offshore island
            Prefab("islet", "rock_3", new Vector3(-150, -3, -650), 25, new Vector3(28, 9, 22));
            Prefab("buoy", "lateral_marker", new Vector3(-10, -1.0f, -190), 0, Vector3.one * 0.7f, id: "buoy_red_1", nonVessel: true);
            Prefab("buoy", "lateral_marker", new Vector3(70, -1.0f, -150), 0, Vector3.one * 0.7f, id: "buoy_green_1", nonVessel: true);

            CameraMasts(Cameras, MastBases);

            h.water_regions.Add(Region("inner_harbor", -170, 58, 170, 58, 170, -60, 40, -60, -30, -120, -170, -120));
            h.water_regions.Add(Region("entrance", -30, -120, 40, -60, 60, -80, -30, -140));
            h.water_regions.Add(Region("outer_sea", -3000, -4000, 3000, -4000, 3000, -25, 190, -25, 190, -80, 60, -80, -30, -140, -190, -140, -190, -45, -3000, -45));

            h.vessel_rules.Add(Rule("korean_fishing_boat", "coastal fishing boat (근해/연안 어선)"));
            foreach (var a in new[] { "trawler", "fishing_vessel", "inshore_fishing_boat", "patrol_boat", "police_boat", "sar_boat", "speedboat" })
                h.vessel_rules.Add(Rule(a, "fishing port traffic"));
            h.vessel_rules.Add(Rule("fishing_boat", "small fishing / leisure fishing boat"));
            h.vessel_rules.Add(Rule("cabin_cruiser", "leisure fishing boat (낚싯배)"));
            h.vessel_rules.Add(Rule("motorboat", "small craft"));
            h.vessel_rules.Add(Rule("rib", "patrol / rescue"));
            h.vessel_rules.Add(Rule("ferry", "island ferry"));
            h.vessel_rules.Add(Rule("barge", "construction / dredging"));
            End(h);
            return h;
        }

        public static ScenarioConfig DefaultScenario()
        {
            var s = new ScenarioConfig { scenario_id = "fishing_port_default", harbor_id = Id, camera_preset_id = "fp_C_breakwater" };
            // fishing boats moored alongside the west pier and the mid pier, two under way, a leisure boat and a ferry
            float[] wz = { 47, 27, 7 };
            for (int i = 0; i < 3; i++) s.vessels.Add(new VesselPlacement { object_id = $"v{i:00}", asset_id = "korean_fishing_boat", x = -98.5f, z = wz[i], heading_deg = 270, immersion_m = 0.45f });
            s.vessels.Add(new VesselPlacement { object_id = "v03", asset_id = "korean_fishing_boat", x = -73.5f, z = 40, heading_deg = 90, immersion_m = 0.45f });
            s.vessels.Add(new VesselPlacement { object_id = "v04", asset_id = "inshore_fishing_boat", x = 36, z = 45, heading_deg = 270, immersion_m = 0.5f });
            s.vessels.Add(new VesselPlacement { object_id = "v05", asset_id = "korean_fishing_boat", x = 10, z = -70, heading_deg = 300, immersion_m = 0.45f });
            s.vessels.Add(new VesselPlacement { object_id = "v06", asset_id = "cabin_cruiser", x = 95, z = 25, heading_deg = 0, immersion_m = 0.3f });
            s.vessels.Add(new VesselPlacement { object_id = "v07", asset_id = "korean_fishing_boat", x = -40, z = -20, heading_deg = 200, immersion_m = 0.45f });
            s.vessels.Add(new VesselPlacement { object_id = "v08", asset_id = "ferry", x = 120, z = -300, heading_deg = 160, immersion_m = 0.4f });
            s.vessels.Add(new VesselPlacement { object_id = "v09", asset_id = "trawler", x = -60, z = -420, heading_deg = 20, immersion_m = 2.0f, motion = new VesselMotion { mode = "anchor" } });
            s.vessels.Add(new VesselPlacement { object_id = "v10", asset_id = "fishing_vessel", x = 30, z = -260, heading_deg = 330, immersion_m = 0.9f });
            // rafted outboard of v01 (second boat alongside) and two boats at the mid pier lying at an angle (bow-in / stern-to)
            s.vessels.Add(new VesselPlacement { object_id = "v11", asset_id = "korean_fishing_boat", x = -105.8f, z = 27, heading_deg = 270, immersion_m = 0.45f });
            s.vessels.Add(new VesselPlacement { object_id = "v12", asset_id = "korean_fishing_boat", x = 10, z = 24, heading_deg = 300, immersion_m = 0.45f });
            s.vessels.Add(new VesselPlacement { object_id = "v13", asset_id = "korean_fishing_boat", x = 10, z = 46, heading_deg = 240, immersion_m = 0.45f });
            s.vessels.Add(new VesselPlacement { object_id = "v14", asset_id = "fishing_boat", x = -7, z = 17, heading_deg = 72, immersion_m = 0.4f });
            s.vessels.Add(new VesselPlacement { object_id = "v15", asset_id = "korean_fishing_boat", x = 64, z = -15, heading_deg = 215, immersion_m = 0.45f });
            s.vessels.Add(new VesselPlacement { object_id = "v16", asset_id = "barge", x = 115, z = -25, heading_deg = 0, immersion_m = 1.1f }); // working aggregate barge, Wando reference
            return s;
        }
    }
}
