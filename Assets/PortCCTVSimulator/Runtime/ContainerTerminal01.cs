using System.Collections.Generic;
using UnityEngine;
using static PortCCTV.HarborGeneric02;
using static PortCCTV.HarborParts;

namespace PortCCTV
{
    /// <summary>
    /// Container terminal: 840 m straight quay with 6 STS cranes, 24 yard blocks with RTGs, admin building, gate,
    /// tank farm and industry on the west side, detached rubble breakwater with tetrapods and a south-east entrance.
    /// Plausible traffic: container ships, tugs, barges, pilot/patrol craft; tankers only outside the breakwater.
    /// </summary>
    public static class ContainerTerminal01
    {
        public const string Id = "container_terminal_01";

        public static readonly CameraPreset[] Cameras =
        {
            new CameraPreset { id = "ct_A_quay_east_mast", display_name = "A - quay mast 20 m (basin -> entrance)", position = new Vector3(-60f, 22.5f, 66f),
                euler_deg = new Vector3(9f, 150f, 0f), vertical_fov_deg = 38f, mount_note = "lighting mast at the quay edge; looks across the basin at the entrance gap 600 m away" },
            new CameraPreset { id = "ct_B_breakwater_head", display_name = "B - breakwater head 10 m (entrance approach)", position = new Vector3(232f, 13.5f, -463f),
                euler_deg = new Vector3(8f, 125f, 0f), vertical_fov_deg = 42f, mount_note = "mast beside the red entrance light; the approach to the entrance gap and the anchorage ahead" },
            new CameraPreset { id = "ct_C_admin_roof", display_name = "C - east quay mast 30 m (entrance)", position = new Vector3(340f, 31f, 65f),
                euler_deg = new Vector3(9f, 182f, 0f), vertical_fov_deg = 34f, mount_note = "30 m mast on the east quay; straight down the basin to the entrance gap, clear of the crane booms" },
            new CameraPreset { id = "ct_D_hill_tower", display_name = "D - east headland 58 m (terminal panorama)", position = new Vector3(470f, 58f, -142f),
                euler_deg = new Vector3(9f, 300f, 0f), vertical_fov_deg = 50f, mount_note = "mast on the headland at the east breakwater root; looks across the basin at the container berths and industrial waterfront" },
            new CameraPreset { id = "ct_E_basin_breakwater", display_name = "E - south breakwater mast 13 m (berths)", position = new Vector3(100f, 13.5f, -463f),
                euler_deg = new Vector3(5f, 345f, 0f), vertical_fov_deg = 52f, mount_note = "mast on the detached breakwater; the berths and basin traffic ahead (terminal-side watch)" },
        };

        static readonly Dictionary<string, float> MastBases = new Dictionary<string, float>
        {
            { "ct_A_quay_east_mast", Deck }, { "ct_B_breakwater_head", Crest }, { "ct_C_admin_roof", Deck }, { "ct_D_hill_tower", 58f - CameraMastOnHill },
            { "ct_E_basin_breakwater", Crest },
        };

        public static HarborDefinition Build()
        {
            var e = Begin(20261105);
            var h = new HarborDefinition
            {
                harbor_id = Id, display_name = "Container terminal", harbor_type = "container_terminal", water_reference_y = 0f, elements = e,
                description = "Container terminal: 840 m quay, 6 STS cranes, 24 RTG yard blocks, tank farm and industry, detached breakwater with tetrapods.",
            };

            // --- land and quays
            Box("quay", -420, 420, Bottom, Deck, 60, 130, "concrete", id: "quay_apron");
            Box("land", -600, 600, Bottom, Deck, 130, 430, "asphalt", id: "yard");
            Box("land", -1600, -420, Bottom, Deck, -250, 430, "asphalt", id: "land_west_industry");
            Box("land", 420, 1600, Bottom, Deck, -150, 430, "grass", id: "land_east");
            Box("land", -1600, 1600, Bottom, Deck - 0.05f, 430, 1600, "grass", id: "land_north");
            QuayFaceZ(-420, 420, 60, -1, 14f, 20f);
            QuayFaceX(-250, 60, -420, 1);
            QuayFaceX(-150, 60, 420, -1);
            Box("crane_rail", -420, 420, Deck, Deck + 0.12f, 63.42f, 63.58f, "steel_dark", id: "rail_seaside");
            Box("crane_rail", -420, 420, Deck, Deck + 0.12f, 93.42f, 93.58f, "steel_dark", id: "rail_landside");
            for (float x = -415; x < 415; x += 7f) Box("marking", x, x + 3.5f, Deck, Deck + 0.02f, 125f, 125.15f, "paint_white");
            for (float x = 330f; x < 418f; x += 3.9f) Prefab("fence", "fence", new Vector3(x, Deck, 127.5f), 0, Vector3.one);   // yard fence near camera A
            Lifebuoys(-400f, 400f, Deck + 1.0f, 61.6f, 80f);

            // --- breakwater (detached, south) + west arm + short east arm; entrance between x 250 and 420
            BreakwaterX("bw_south", -450, 250, -470, -455, seaIsMinusZ: true);
            BreakwaterZ("bw_west", -455, -250, -450, -435, seaIsMinusX: true);
            BreakwaterZ("bw_east", -400, -150, 420, 435, seaIsMinusX: false);
            Lighthouse("lh_red_w", 244f, -461f, Crest, "beacon_red");
            Lighthouse("lh_white_e", 427f, -394f, Crest, "beacon_white_band");
            MultipurposeBerth(-420f, -235f, 40f, 1f);     // west quay: multipurpose berth (portal + mobile harbour cranes)

            // --- cranes, yard, buildings
            // Layout after the user's reference (2026-11-05): ten yellow STS cranes along the quay, and a dense yard of 24 blocks
            // perpendicular to the quay (10 rows x 18 bays, up to 5 high) with rail-mounted gantries (ARMG), a gate / admin
            // corridor in the middle (x -103..65).
            foreach (var x in new[] { -385f, -325f, -265f, -205f, -145f, 30f, 90f, 150f, 210f, 270f }) StsCrane(x, 60f);
            const float blockPitch = 39.5f, yardZ0 = 145f; const int yardRows = 10, yardBays = 18;
            for (int i = 0; i < 12; i++)
            {
                // two ARMGs share each block's rails (Konecranes ARMG reference); the two blocks next to the gate corridor are
                // reefer blocks with power racks (reefer stacks near the gate / yard centre)
                ContainerBlockZ(-565f + i * blockPitch, yardZ0, yardRows, yardBays, 5, 2, reefer: i == 11);
                ContainerBlockZ(65f + i * blockPitch, yardZ0, yardRows, yardBays, 5, 2, reefer: i == 0);
            }
            Box("marking", -420, 420, Deck, Deck + 0.02f, 131.8f, 132.2f, "paint_yellow");          // yard / apron boundary
            Box("marking", -420, 420, Deck, Deck + 0.02f, 97.8f, 98.2f, "paint_yellow");            // STS back-reach lane
            // admin building (camera C) and gate
            if (Buildings) Box("building", -90, 10, Deck, Deck + 20f, 150, 186, "facade_office", id: "admin_building");
            if (Buildings) Box("building", -90.4f, 10.4f, Deck + 20f, Deck + 20.6f, 149.6f, 186.4f, "roof_concrete", id: "admin_roof");
            Box("gate", -53, 53, Deck + 7, Deck + 7.8f, 390, 402, "crane_blue", id: "gate_canopy");                 // columns: GateComplex islands
            if (Buildings) Box("building", 60, 140, Deck, Deck + 9, 300, 340, "warehouse_wall", id: "cfs_shed");
            if (Buildings) Box("building", 59.6f, 140.4f, Deck + 9, Deck + 9.5f, 299.6f, 340.4f, "warehouse_roof", id: "cfs_roof");
            for (float x = -420; x <= 420; x += 120) LightMast(x, 98f);                                       // apron masts behind the STS back reach
            for (int i = 1; i < 12; i += 3) foreach (var bx in new[] { -565f, 65f }) foreach (var z in new[] { 150f, 365f }) LightMast(bx + i * blockPitch - 2.6f, z);
            // real building models (Sketchfab CC BY): sheds behind the yard, admin building, port-city towers to the east
            Prefab("building", "bld_officetel", new Vector3(-40f, Deck, 330f), 180f, Vector3.one * 0.5f, id: "admin_building");   // terminal operations building
            Prefab("building", "bld_warehouse3", new Vector3(-250f, Deck, 410f), 0f, Vector3.one, id: "shed_row_3");   // CFS behind the yard
            Prefab("building", "bld_ind_warehouse", new Vector3(80f, Deck, 405f), 90f, Vector3.one, id: "shed_4");
            for (float x = 140f; x < 560f; x += 30f) Prefab("building", Rng.NextDouble() < 0.5 ? "bld_ind_warehouse" : "bld_warehouse_blue", new Vector3(x, Deck, 410f), 0f, Vector3.one);   // maintenance / reefer sheds
            foreach (var (fx, fz, fy) in new[] { (-640f, 452f, 0f), (-612f, 540f, 180f), (-250f, 455f, 90f) }) Vehicle("crane", "forklift_a", new Vector3(fx, Deck, fz), fy);
            // Real harbor assets in the cross-basin CCTV view: working sheds and a parked service crane.
            Prefab("building", "godown_warehouse", new Vector3(-150f, Deck, 130f), 180f, Vector3.one, id: "quay_godown");
            Prefab("building", "godown_warehouse", new Vector3(10f, Deck, 130f), 180f, Vector3.one, id: "quay_service_shed");
            Prefab("crane", "mobile_crane", new Vector3(-110f, Deck, 115f), 220f, Vector3.one, id: "quay_mobile_crane");
            Prefab("building", "godown_warehouse", new Vector3(365f, Deck, 132f), 180f, Vector3.one, id: "east_quay_godown");
            Prefab("crane", "mobile_crane", new Vector3(310f, Deck, 112f), 155f, Vector3.one, id: "east_quay_mobile_crane");
            // --- port town: separated from the terminal by the east boundary boulevard (x 700), a green buffer and a fence
            GreenBuffer(640f, 660f, 140f, 560f); // (narrow stub kept for the z-run below)
            for (float z = 150f; z < 560f; z += 3.9f) Prefab("fence", "fence", new Vector3(652f, Deck, z), 90f, Vector3.one);
            for (float z = 150f; z < 560f; z += R(7f, 11f)) Tree(658f + R(0f, 28f), z, Deck, R(7f, 12f));
            Scrub(656f, 690f, 150f, 560f, 70, 2f, 4f);
            Boulevard(100f, 980f, 700f, false, 4, true, 0f);                                                  // 항만 배후도로 (port boundary road)
            foreach (var z in new[] { 300f, 540f }) SignalledCrossing(new Vector2(700f, z), false, 14f, 8f, true);
            CityBlocks(715f, 1050f, 300f, 540f, 110f);                                                        // residential blocks
            CityBlocks(715f, 1050f, 130f, 300f, 110f);
            CityBlocks(715f, 1050f, 540f, 760f, 110f);
            BusStop(700f + 8.2f, 420f, 0f);
            for (int i = 0; i < 6; i++)                                                                      // spaced tractors in marked apron lanes
            {
                float x = -360f + (i % 3) * 290f, z = 103f + (i / 3) * 14f;
                Truck(x, z, 0f, i % 2 == 0);
            }
            GateComplex(-52f, 52f, 396f);

            // --- port hinterland behind the gate: access road, truck staging, empty-container depot, staff and visitor car
            // parks, logistics warehouses; a truck park and car park on the east land next to the yard
            Box("land", -620, 620, Deck - 0.04f, Deck - 0.005f, 430, 785, "concrete", id: "hinterland_apron");   // paved port back-up area
            Boulevard(407f, 985f, 0f, false, 4, false);                                                        // terminal access road (gate -> hinterland)
            Boulevard(-625f, 625f, 600f, true, 4, true);
            SignalledCrossing(new Vector2(0f, 600f), true, 14f, 14f, true);
            Prefab("street", "sign_gantry", new Vector3(0f, Deck, 450f), 0f, Vector3.one);                   // gate approach gantry
            for (float z = 470f; z < 590f; z += 2.1f) foreach (var s in new[] { -1f, 1f }) Prefab("street", "jersey_barrier", new Vector3(s * 7.5f, Deck, z), 90f, Vector3.one);   // median barriers
            TruckLot(40f, 190f, 440f, 520f, 0.22f, 0.6f, "ct_truck_staging");
            ContainerBlock(205f, 455f, 7, 7, 3, false);
            foreach (var bx in new[] { -340f, -250f }) foreach (var bz in new[] { 448f, 478f, 508f, 538f }) ContainerBlock(bx, bz, 6, 6, 5, false);
            foreach (var (hx, hz, hy) in new[] { (-262f, 470f, 90f), (-262f, 528f, 270f), (-162f, 500f, 0f) })      // empty handlers in the depot
                Prefab("crane", "empty_handler", new Vector3(hx, Deck, hz), hy, Vector3.one);
            ParkingLot(-150f, -65f, 440f, 480f, 0.2f, "ct_staff_car_park", 0.01f);
            ContainerBlock(-140f, 545f, 6, 6, 2, false);                                                       // empty-box overflow beside the depot
            Prefab("building", "bld_warehouse3", new Vector3(-320f, Deck, 650f), 0f, Vector3.one, id: "logistics_1");
            Prefab("building", "bld_warehouse3", new Vector3(320f, Deck, 650f), 0f, Vector3.one, id: "logistics_2");
            Prefab("building", "bld_warehouses", new Vector3(-110f, Deck, 645f), 0f, Vector3.one, id: "logistics_3");
            Prefab("building", "bld_warehouses", new Vector3(110f, Deck, 645f), 0f, Vector3.one, id: "logistics_4");
            Prefab("building", "bld_warehouse3", new Vector3(-320f, Deck, 725f), 0f, Vector3.one * 0.8f, id: "logistics_5");
            Prefab("building", "bld_warehouse3", new Vector3(320f, Deck, 725f), 0f, Vector3.one * 0.8f, id: "logistics_6");
            IndustrialBlocks(-600f, 600f, 790f, 940f, 120f);                 // logistics park behind the hinterland
            IndustrialBlocks(-1580f, -640f, 450f, 940f, 118f);
            GreenBuffer(-1580f, 1050f, 945f, 990f);                            // buffer between the port/logistics zone and the land north
            ContainerBlock(462f, 322f, 10, 8, 4, false);                                                       // east container buffer
            ContainerBlock(462f, 168f, 10, 8, 4, false);
            Prefab("crane", "empty_handler", new Vector3(603f, Deck, 375f), 270f, Vector3.one);

            // --- tank farm and industry on the west land (like Ulsan/Yeosu industrial ports)
            for (int i = 0; i < 4; i++) for (int j = 0; j < 3; j++) Tank(-640 - i * 48, -170 + j * 50, R(26, 36), R(14, 22));
            // bund wall around the tank farm, road-tanker loading lot, then port-industrial blocks over the rest of the west land
            foreach (var (bx0, bx1, bz0, bz1) in new[] { (-815f, -605f, -200f, -197f), (-815f, -605f, -43f, -40f), (-815f, -812f, -200f, -40f), (-608f, -605f, -200f, -40f) })
                Box("bund", bx0, bx1, Deck, Deck + 1.4f, bz0, bz1, "concrete");
            TruckLot(-590f, -475f, -232f, -40f, 0.4f, 0.3f, "ct_tanker_lot");
            IndustrialBlocks(-1580f, -870f, -235f, 415f, 118f);
            IndustrialBlocks(-850f, -470f, -20f, 415f, 110f);
            foreach (var (cx, cz) in new[] { (-1099f, -97.5f), (-1217.5f, 162.5f) }) Cyl("factory", cx, cz, Deck, Deck + R(45f, 80f), R(3.5f, 5f), "chimney");   // stacks of the plants

            // --- east town with apartment towers (camera D stands on the first one); coastal road + village on the east shore below it
            CameraHill(Cameras[3], 240f, 420f, 2, new Vector2(527f, -94f));
            CoastalVillage(760f, 1200f, -128f, -1f, 3, true);                                                // village starts 60 m past the buffer
            for (float z = -140f; z < 140f; z += 3.9f) Prefab("fence", "fence", new Vector3(652f, Deck, z), 90f, Vector3.one);
            for (float z = -140f; z < 140f; z += R(6f, 9f)) Tree(660f + R(0f, 60f), z, Deck, R(7f, 12f));
            Scrub(656f, 740f, -140f, 140f, 80, 2f, 4f);
            Scrub(760f, 1200f, -148f, -136f, 30); Scrub(1200f, 1500f, -140f, 100f, 40, 4f, 8f);
            for (float x = 436f; x < 1400f; x += 3.4f) Rock(x, -152f - R(0f, 3f), R(-1.4f, 0.2f), R(2.4f, 4.2f));   // rocky east shore
            HillIdx(1, 1320f, 560f, -4f, 560f, 130f, 460f); TreesOnHill(1, 1320f, 560f, -4f, 560f, 130f, 460f, 25);   // moved east so the port town is not inside it
            Farmland(760f, 1450f, -30f, 110f);
            // The detailed village and city blocks already occupy these plots. Random boxes here
            // used to intersect both the houses and the camera headland.

            // --- backdrop
            for (int i = 0; i < 9; i++) Hill(R(-2800, 2800), R(1700, 3000), -5, R(900, 1600), R(105, 210), R(700, 1200), R(0, 360));
            for (int i = 0; i < 5; i++) Hill(R(-1200, 1300), R(950, 1350), Deck - 3, R(350, 600), R(45, 85), R(300, 500), R(0, 360));
            Hill(-1300, -900, -6, 1200, 100, 800, 40);
            Hill(1500, -800, -6, 1300, 110, 800, -30);
            Hill(300, -2600, -8, 500, 110, 380, 20);
            Hill(-900, -3000, -8, 700, 150, 500, 70);
            Prefab("buoy", "lateral_marker", new Vector3(300, -1.0f, -520), 0, Vector3.one * 0.8f, id: "buoy_red_1", nonVessel: true);
            Prefab("buoy", "lateral_marker", new Vector3(390, -1.0f, -540), 0, Vector3.one * 0.8f, id: "buoy_green_1", nonVessel: true);
            Prefab("buoy", "ocean_buoy", new Vector3(380, -0.6f, -900), 0, Vector3.one * 1.8f, id: "buoy_fairway", nonVessel: true);

            CameraMasts(Cameras, MastBases);

            h.water_regions.Add(Region("terminal_basin", -420, 60, 420, 60, 420, -400, 250, -455, -435, -455, -435, -250, -420, -250));
            h.water_regions.Add(Region("entrance", 250, -455, 420, -400, 435, -400, 435, -480, 250, -480));
            h.water_regions.Add(Region("outer_sea", -3000, -470, 250, -470, 250, -480, 435, -480, 435, -150, 3000, -150, 3000, -4000, -3000, -4000));

            h.vessel_rules.Add(Rule("container_ship", "berths at the quay or transits the entrance"));
            foreach (var a in new[] { "feeder_ship", "general_cargo", "tug_harbor", "pilot_boat", "patrol_boat", "police_boat", "sar_boat" })
                h.vessel_rules.Add(Rule(a, "terminal traffic / port services"));
            h.vessel_rules.Add(Rule("bulk_carrier", "large ships anchor outside", "outer_sea"));
            h.vessel_rules.Add(Rule("lng_carrier", "large ships anchor outside", "outer_sea"));
            h.vessel_rules.Add(Rule("tugboat", "assists berthing"));
            h.vessel_rules.Add(Rule("barge", "port works / bunkering"));
            h.vessel_rules.Add(Rule("rib", "patrol / pilot transfer"));
            h.vessel_rules.Add(Rule("motorboat", "pilot launch / work boat"));
            h.vessel_rules.Add(Rule("tanker", "tankers use the anchorage outside, not the container basin", "outer_sea"));
            End();
            return h;
        }

        /// <summary>Terminal gate after common gate designs: lanes under the canopy (in = -x half towards the yard, out = +x
        /// half), gate booths between lane pairs, OCR / radiation portals and weigh-in-motion plates on the landside approach,
        /// lane markings, trucks queued in some lanes.</summary>
        static void GateComplex(float x0, float x1, float zCanopy)
        {
            const float lane = 4.6f;
            int n = (int)((x1 - x0) / lane);
            for (int k = 0; k <= n; k++) Box("marking", x0 + k * lane - 0.08f, x0 + k * lane + 0.08f, Deck, Deck + 0.02f, zCanopy - 6f, zCanopy + 34f, "paint_white");
            Box("marking", -0.15f, 0.15f, Deck, Deck + 0.02f, zCanopy - 6f, zCanopy + 34f, "paint_yellow");
            for (int k = 1; k < n; k += 2)
            {
                float bx = x0 + k * lane;
                Box("gate", bx - 0.7f, bx + 0.7f, Deck, Deck + 0.25f, zCanopy - 6f, zCanopy + 6f, "concrete");              // island
                Box("gate", bx - 0.55f, bx + 0.55f, Deck + 0.25f, Deck + 2.8f, zCanopy - 0.8f, zCanopy + 0.8f, "wall_white");  // booth
                Box("gate", bx - 0.58f, bx + 0.58f, Deck + 1.4f, Deck + 2.3f, zCanopy - 0.83f, zCanopy + 0.83f, "glass_dark");
                foreach (var pz in new[] { zCanopy - 4.5f, zCanopy + 4.5f }) Box("gate", bx - 0.3f, bx + 0.3f, Deck, Deck + 7f, pz - 0.3f, pz + 0.3f, "steel_galv");   // canopy columns on the islands
            }
            foreach (var pz in new[] { zCanopy + 16f, zCanopy + 26f })                                              // OCR and radiation portals
            {
                foreach (var px in new[] { x0 - 0.6f, 0f, x1 + 0.6f }) Box("gate", px - 0.35f, px + 0.35f, Deck, Deck + 6.5f, pz - 0.35f, pz + 0.35f, "steel_galv");
                Box("gate", x0 - 1f, x1 + 1f, Deck + 6.5f, Deck + 7.3f, pz - 0.5f, pz + 0.5f, pz < zCanopy + 20f ? "crane_blue" : "steel_galv");
                for (float cx = x0 + lane / 2f; cx < x1; cx += lane) Box("gate", cx - 0.2f, cx + 0.2f, Deck + 6.0f, Deck + 6.5f, pz - 0.25f, pz + 0.25f, "steel_dark");
            }
            for (float cx = x0 + lane / 2f; cx < x1; cx += lane) Box("marking", cx - 1.6f, cx + 1.6f, Deck, Deck + 0.03f, zCanopy + 10f, zCanopy + 13f, "steel_dark");   // weigh plates
            for (float cx = x0 + lane / 2f; cx < x1; cx += lane)                                                    // queues
                if (Rng.NextDouble() < 0.6)
                    ContainerTruck(cx, zCanopy + R(12f, 22f), cx < 0 ? 90f : -90f, Rng.NextDouble() < 0.65, Rng.NextDouble() < 0.8);
        }

        public static ScenarioConfig DefaultScenario()
        {
            var s = new ScenarioConfig { scenario_id = "container_terminal_default", harbor_id = Id, camera_preset_id = "ct_E_basin_breakwater" };
            s.vessels.Add(new VesselPlacement { object_id = "v00", asset_id = "container_ship", x = -270, z = 60 - 1.6f - 13.6f, heading_deg = 180, immersion_m = 4.2f });
            s.vessels.Add(new VesselPlacement { object_id = "v01", asset_id = "general_cargo", x = 40, z = -230, heading_deg = 110, immersion_m = 5.0f });
            s.vessels.Add(new VesselPlacement { object_id = "v02", asset_id = "tug_harbor", x = 150, z = -200, heading_deg = 120, immersion_m = 6.0f });
            s.vessels.Add(new VesselPlacement { object_id = "v03", asset_id = "tug_harbor", x = -80, z = -150, heading_deg = 170, immersion_m = 6.0f });
            s.vessels.Add(new VesselPlacement { object_id = "v04", asset_id = "rib", x = -30, z = -40, heading_deg = 250, immersion_m = 0.1f });
            s.vessels.Add(new VesselPlacement { object_id = "v05", asset_id = "barge", x = 100, z = -90, heading_deg = 10, immersion_m = 0.15f });
            s.vessels.Add(new VesselPlacement { object_id = "v06", asset_id = "tanker", x = 420, z = -1150, heading_deg = 80, immersion_m = 6f, motion = new VesselMotion { mode = "anchor" } });
            s.vessels.Add(new VesselPlacement { object_id = "v07", asset_id = "pilot_boat", x = -100, z = -90, heading_deg = 75, immersion_m = 1.1f });
            s.vessels.Add(new VesselPlacement { object_id = "v08", asset_id = "police_boat", x = 45, z = -130, heading_deg = 255, immersion_m = 0.6f });
            s.vessels.Add(new VesselPlacement { object_id = "v09", asset_id = "barge", x = 100, z = -950, heading_deg = 65, immersion_m = 0.15f, motion = new VesselMotion { mode = "anchor" } });
            s.vessels.Add(new VesselPlacement { object_id = "v11", asset_id = "container_ship", x = 150, z = 60 - 1.6f - 13.6f, heading_deg = 180, immersion_m = 4.2f });   // second berth under the east STS group
            s.vessels.Add(new VesselPlacement { object_id = "v10", asset_id = "feeder_ship", x = 950, z = -1050, heading_deg = 245, immersion_m = 4.0f });
            s.vessels.Add(new VesselPlacement { object_id = "v12", asset_id = "bulk_carrier", x = -350, z = -1350, heading_deg = 60, immersion_m = 7f, motion = new VesselMotion { mode = "anchor" } });
            return s;
        }
    }
}
