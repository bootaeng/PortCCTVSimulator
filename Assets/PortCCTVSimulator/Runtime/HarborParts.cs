using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static PortCCTV.HarborGeneric02;

namespace PortCCTV
{
    /// <summary>Parametric parts shared by the map builders (uses the element list started by HarborGeneric02.Begin).</summary>
    internal static class HarborParts
    {
        // ------------------------------------------------------------ quays
        /// <summary>Quay face along X at z (water towards dir = -1 / +1 in z): wet band, coping, fenders, bollards, ladders.</summary>
        public static void QuayFaceZ(float x0, float x1, float z, float dir, float fenderStep = 12f, float bollardStep = 15f, float deck = Deck)
        {
            float zf = z + dir * 0.08f;
            Box("quay_wall", x0, x1, -2.5f, 1.0f, Mathf.Min(z, zf), Mathf.Max(z, zf), "quay_wet");
            Box("coping", x0, x1, deck, deck + 0.08f, Mathf.Min(z, z - dir * 0.35f), Mathf.Max(z, z - dir * 0.35f), "paint_yellow");
            for (float x = x0 + fenderStep / 2; x < x1 - 2f; x += fenderStep) Cyl("fender", x, z + dir * 0.6f, -0.6f, deck - 0.6f, 1.1f, "rubber");
            for (float x = x0 + bollardStep * 0.66f; x < x1 - 2f; x += bollardStep)
            {
                Cyl("bollard", x, z - dir * 1.3f, deck, deck + 0.6f, 0.55f, "bollard");
                Cyl("bollard", x, z - dir * 1.3f, deck + 0.6f, deck + 0.75f, 0.85f, "bollard");
            }
            for (float x = x0 + 20f; x < x1 - 2f; x += 40f)
                Box("ladder", x - 0.22f, x + 0.22f, -1.8f, deck, Mathf.Min(z, z + dir * 0.18f), Mathf.Max(z, z + dir * 0.18f), "paint_yellow");
        }

        public static void QuayFaceX(float z0, float z1, float x, float dir, float fenderStep = 12f, float bollardStep = 15f, float deck = Deck)
        {
            float xf = x + dir * 0.08f;
            Box("quay_wall", Mathf.Min(x, xf), Mathf.Max(x, xf), -2.5f, 1.0f, z0, z1, "quay_wet");
            Box("coping", Mathf.Min(x, x - dir * 0.35f), Mathf.Max(x, x - dir * 0.35f), deck, deck + 0.08f, z0, z1, "paint_yellow");
            for (float z = z0 + fenderStep / 2; z < z1 - 2f; z += fenderStep) Cyl("fender", x + dir * 0.6f, z, -0.6f, deck - 0.6f, 1.1f, "rubber");
            for (float z = z0 + bollardStep * 0.66f; z < z1 - 2f; z += bollardStep)
            {
                Cyl("bollard", x - dir * 1.3f, z, deck, deck + 0.6f, 0.55f, "bollard");
                Cyl("bollard", x - dir * 1.3f, z, deck + 0.6f, deck + 0.75f, 0.85f, "bollard");
            }
        }

        // ------------------------------------------------------------ STS crane on a quay whose face is at z = quayZ (water at -z)
        public static void StsCrane(float x, float quayZ)
        {
            float o = quayZ - 60f; // same geometry as HarborGeneric02.StsCrane, shifted
            float z0 = 63.5f + o, z1 = 93.5f + o, top = 41f, boomY0 = 43.5f, boomY1 = 47f, tipZ = 12f + o, backZ = 111f + o;
            string kind = "sts_crane";
            foreach (var dx in new[] { -9f, 9f })
                foreach (var z in new[] { z0, z1 })
                {
                    Box(kind, x + dx - 0.9f, x + dx + 0.9f, Deck, top, z - 0.9f, z + 0.9f, "crane_yellow");
                    Box(kind, x + dx - 1.4f, x + dx + 1.4f, Deck, Deck + 1.6f, z - 2.2f, z + 2.2f, "crane_blue");
                }
            foreach (var dx in new[] { -9f, 9f })
            {
                Box(kind, x + dx - 0.8f, x + dx + 0.8f, 11f, 12.6f, z0, z1, "crane_blue");
                Box(kind, x + dx - 0.9f, x + dx + 0.9f, top - 2.5f, top, z0, z1, "crane_yellow");
            }
            foreach (var z in new[] { z0, z1 })
            {
                Box(kind, x - 10f, x + 10f, top - 2.5f, top + 0.4f, z - 1f, z + 1f, "crane_blue");
                Beam(kind, new Vector3(x - 9, 12.6f, z), new Vector3(x + 9, top - 2.5f, z), 0.6f, 0.6f, "crane_yellow");
                Beam(kind, new Vector3(x + 9, 12.6f, z), new Vector3(x - 9, top - 2.5f, z), 0.6f, 0.6f, "crane_yellow");
            }
            foreach (var dx in new[] { -1.7f, 1.7f }) Box(kind, x + dx - 0.45f, x + dx + 0.45f, boomY0, boomY1, tipZ, backZ, "crane_yellow");
            Box(kind, x - 2.6f, x + 2.6f, boomY0 - 0.3f, boomY0, tipZ + 2, backZ - 2, "steel_dark");
            for (float z = tipZ + 3; z < backZ; z += 6f) Box(kind, x - 2.2f, x + 2.2f, boomY1 - 0.3f, boomY1, z, z + 0.5f, "crane_yellow");
            float apexZ = 70f + o, apexY = 70f;
            foreach (var dx in new[] { -3.5f, 3.5f })
            {
                Beam(kind, new Vector3(x + dx * 2f, boomY1, apexZ - 6f), new Vector3(x + dx, apexY, apexZ), 1.0f, 1.0f, "crane_yellow");
                Beam(kind, new Vector3(x + dx * 2f, boomY1, apexZ + 10f), new Vector3(x + dx, apexY, apexZ), 1.0f, 1.0f, "crane_yellow");
                Beam(kind, new Vector3(x + dx, apexY, apexZ), new Vector3(x + dx * 0.5f, boomY1, tipZ + 1f), 0.35f, 0.35f, "steel_dark");
                Beam(kind, new Vector3(x + dx, apexY, apexZ), new Vector3(x + dx * 0.5f, boomY1, tipZ + 22f), 0.35f, 0.35f, "steel_dark");
                Beam(kind, new Vector3(x + dx, apexY, apexZ), new Vector3(x + dx * 0.5f, boomY1, backZ - 1f), 0.35f, 0.35f, "steel_dark");
            }
            Box(kind, x - 4.5f, x + 4.5f, apexY - 1f, apexY + 1f, apexZ - 1f, apexZ + 1f, "crane_blue");
            Box(kind, x - 4.2f, x + 4.2f, boomY1, boomY1 + 5.5f, 97f + o, 109f + o, "crane_house");
            Box(kind, x - 4.3f, x + 4.3f, boomY1 + 5.5f, boomY1 + 6.0f, 96.8f + o, 109.2f + o, "crane_blue");
            float tz = 38f + o + R(-10f, 10f);
            Box(kind, x - 2.3f, x + 2.3f, boomY0 - 2.2f, boomY0 - 0.3f, tz - 3f, tz + 3f, "crane_blue");
            Box(kind, x + 2.3f, x + 4.6f, boomY0 - 4.8f, boomY0 - 1.6f, tz - 2f, tz + 2f, "glass_dark");
            float sy = R(24f, 34f);
            foreach (var dx in new[] { -1f, 1f })
                foreach (var dz in new[] { -2f, 2f })
                    Box(kind, x + dx - 0.04f, x + dx + 0.04f, sy + 0.8f, boomY0 - 2.2f, tz + dz - 0.04f, tz + dz + 0.04f, "steel_dark");
            Box(kind, x - 1.3f, x + 1.3f, sy, sy + 0.8f, tz - 6.2f, tz + 6.2f, "crane_yellow");
        }

        // ------------------------------------------------------------ container block with an RTG
        static readonly string[] Colours =
            { "container_red", "container_blue", "container_green", "container_grey", "container_orange", "container_white", "container_teal", "container_brown", "container_navy", "container_maroon" };

        public static void ContainerBlock(float bx, float bz, int bays, int rows, int maxTiers, bool rtg)
        {
            const float bay = 12.6f, row = 2.75f;
            for (int b = 0; b < bays; b++)
                for (int r = 0; r < rows; r++)
                {
                    float x = bx + b * bay, z = bz + r * row;
                    int tiers = Rng.NextDouble() < 0.06 ? 0 : 1 + Rng.Next(maxTiers);
                    for (int t = 0; t < tiers; t++)
                    {
                        float y = Deck + t * 2.59f;
                        if (Rng.NextDouble() < 0.3)
                        {
                            BoxC("container", new Vector3(x + 3.03f, y + 1.295f, z + 1.22f), new Vector3(6.06f, 2.59f, 2.44f), Colours[Rng.Next(Colours.Length)]);
                            BoxC("container", new Vector3(x + 6.13f + 3.03f, y + 1.295f, z + 1.22f), new Vector3(6.06f, 2.59f, 2.44f), Colours[Rng.Next(Colours.Length)]);
                        }
                        else BoxC("container", new Vector3(x + 6.095f, y + 1.295f, z + 1.22f), new Vector3(12.19f, 2.59f, 2.44f), Colours[Rng.Next(Colours.Length)]);
                    }
                }
            if (!rtg) return;
            float gx = bx + (1 + Rng.Next(Mathf.Max(1, bays - 2))) * bay + bay / 2;
            // RTG model (Sketchfab CC BY "Gantry Crane", decimated, mm -> m): span axis along z over the rows, travels along x.
            Prefab("rtg", "prop_rtg", new Vector3(gx, Deck, bz + rows * row / 2f), 90f, Vector3.one);
        }

        // ------------------------------------------------------------ multipurpose berth on a quay face along Z (water towards +x when dir = 1)
        /// <summary>Two level-luffing portal cranes ("Sokol") on rails and one mobile harbour crane (Sketchfab CC BY models), booms
        /// slewed out over the water at different angles.</summary>
        /// <summary>Container block perpendicular to the quay: `rows` stacks across x (2.75 m pitch), `bays` along z (12.6 m:
        /// one 40 ft or two 20 ft per tier), up to maxTiers high (most stacks 2+ high), with ARMG rails on both sides and
        /// `cranes` rail-mounted gantries (procedural prefab, 34 m span) parked at random bays.</summary>
        public static void ContainerBlockZ(float x0, float z0, int rows, int bays, int maxTiers, int cranes, bool reefer = false)
        {
            const float bay = 12.6f, row = 2.75f;
            string[] reeferCols = { "container_white", "container_white", "container_white", "container_grey", "container_teal" };
            for (int b = 0; b < bays; b++)
                for (int r = 0; r < rows; r++)
                {
                    float x = x0 + r * row, z = z0 + b * bay;
                    int tiers = Rng.NextDouble() < 0.04 ? 0 : 2 + Rng.Next(Mathf.Max(1, maxTiers - 1));
                    for (int t = 0; t < tiers; t++)
                    {
                        float y = Deck + t * 2.59f;
                        if (Rng.NextDouble() < 0.3)
                        {
                            BoxC("container", new Vector3(x + 1.22f, y + 1.295f, z + 3.03f), new Vector3(2.44f, 2.59f, 6.06f), reefer ? reeferCols[Rng.Next(reeferCols.Length)] : Colours[Rng.Next(Colours.Length)]);
                            BoxC("container", new Vector3(x + 1.22f, y + 1.295f, z + 6.13f + 3.03f), new Vector3(2.44f, 2.59f, 6.06f), reefer ? reeferCols[Rng.Next(reeferCols.Length)] : Colours[Rng.Next(Colours.Length)]);
                        }
                        else BoxC("container", new Vector3(x + 1.22f, y + 1.295f, z + 6.095f), new Vector3(2.44f, 2.59f, 12.19f), reefer ? reeferCols[Rng.Next(reeferCols.Length)] : Colours[Rng.Next(Colours.Length)]);
                    }
                }
            float cx = x0 + rows * row / 2f, len = bays * bay;
            if (reefer)
            {
                // reefer power racks: steel frames in the 0.31 m gaps between every second row, outlet walkways at tiers 2 and 3
                for (int r = 2; r < rows; r += 2)
                {
                    float rx = x0 + r * row - 0.155f;
                    for (int b = 0; b <= bays; b += 2) Box("reefer_rack", rx - 0.12f, rx + 0.12f, Deck, Deck + 9.2f, z0 + b * bay - 0.12f, z0 + b * bay + 0.12f, "steel_galv");
                    foreach (var h in new[] { 2.59f, 5.18f, 9.0f }) Box("reefer_rack", rx - 0.14f, rx + 0.14f, Deck + h, Deck + h + 0.2f, z0, z0 + len, h > 8f ? "crane_yellow" : "steel_galv");
                }
            }
            // interchange areas at both block ends (end-loading ARMG block): five truck lanes each, yard tractors on the
            // seaside, road trucks on the landside
            for (int k = 0; k <= 5; k++)
                foreach (var (za, zb) in new[] { (z0 - 21f, z0 - 3f), (z0 + len + 3f, z0 + len + 21f) })
                    Box("marking", cx - 13.75f + k * 5.5f - 0.08f, cx - 13.75f + k * 5.5f + 0.08f, Deck, Deck + 0.02f, za, zb, "paint_white");
            for (int k = 0; k < 5; k++)
            {
                float lx = cx - 13.75f + k * 5.5f + 2.75f;
                if (Rng.NextDouble() < 0.35) ContainerTruck(lx, z0 - 12f, 90f, Rng.NextDouble() < 0.6);
                if (Rng.NextDouble() < 0.3) ContainerTruck(lx, z0 + len + 12f, -90f, Rng.NextDouble() < 0.6, Rng.NextDouble() < 0.8);
            }
            foreach (var s in new[] { -17f, 17f }) Box("crane_rail", cx + s - 0.1f, cx + s + 0.1f, Deck, Deck + 0.12f, z0 - 8f, z0 + len + 8f, "steel_dark");
            for (int c = 0; c < cranes; c++)
            {
                float f = cranes == 1 ? R(0.15f, 0.85f) : (c == 0 ? R(0.1f, 0.4f) : R(0.6f, 0.9f));
                Prefab("crane", "armg_crane", new Vector3(cx, Deck, z0 + len * f), 0f, Vector3.one);
            }
        }

        public static void MultipurposeBerth(float x, float z0, float z1, float dir)
        {
            float rail0 = x - dir * 4.5f, rail1 = x - dir * 15.0f;
            foreach (var rx in new[] { rail0, rail1 }) Box("crane_rail", rx - 0.08f, rx + 0.08f, Deck, Deck + 0.12f, z0, z1, "steel_dark");
            float face = dir > 0 ? 0f : 180f;   // prefab boom direction is calibrated in DEV_LOG ("harbor props")
            Prefab("portal_crane", "prop_portal_crane", new Vector3(x - dir * 9.75f, Deck, z0 + (z1 - z0) * 0.25f), PortalCraneYaw + face + 25f, Vector3.one);
            Prefab("portal_crane", "prop_portal_crane", new Vector3(x - dir * 9.75f, Deck, z0 + (z1 - z0) * 0.55f), PortalCraneYaw + face - 10f, Vector3.one);
            Prefab("harbor_crane", "prop_harbor_crane", new Vector3(x - dir * 12f, Deck, z0 + (z1 - z0) * 0.85f), HarborCraneYaw + face + 15f, Vector3.one);
        }

        /// <summary>Yaw that points each crane's boom towards world +x (from the prop's authored axis).</summary>
        // Measured on top-down renders (Captures/props_check_r2 ct_berth_top, r4 ct_hc_top): the imported Sokol boom points along
        // local -x, the harbour crane boom along local +z (= the yaw direction).
        public const float PortalCraneYaw = 180f, HarborCraneYaw = 90f;

        // ------------------------------------------------------------ breakwater pieces
        /// <summary>Rubble/caisson breakwater along X (z0..z1 across), crown wall on the seaward side, tetrapods seaward, rocks harbourside.</summary>
        public static void BreakwaterX(string id, float x0, float x1, float z0, float z1, bool seaIsMinusZ, float crest = Crest)
        {
            Box("breakwater", x0, x1, Bottom, 1.6f, z0, z1, "breakwater", id: id + "_core");
            float wall0 = seaIsMinusZ ? z0 : z1 - 2.4f, wall1 = seaIsMinusZ ? z0 + 2.4f : z1;
            Box("breakwater", x0, x1, 1.6f, crest, seaIsMinusZ ? z0 + 2.4f : z0, seaIsMinusZ ? z1 : z1 - 2.4f, "concrete", id: id + "_road");
            Box("breakwater", x0, x1, 1.6f, crest + 2.5f, wall0, wall1, "breakwater", id: id + "_crownwall");
            float sea = seaIsMinusZ ? z0 : z1, port = seaIsMinusZ ? z1 : z0, s = seaIsMinusZ ? -1f : 1f;
            for (float x = x0 + 1.5f; x < x1 - 1f; x += 3.2f)
            {
                Tetrapod(x + R(-0.6f, 0.6f), sea + s * R(1.0f, 2.2f), R(-0.4f, 1.2f));
                Tetrapod(x + 1.6f + R(-0.6f, 0.6f), sea + s * R(3.4f, 4.6f), R(-1.2f, 0.2f));
                Rock(x + R(-0.6f, 0.6f), port - s * R(0.4f, 1.4f), R(-0.6f, 0.4f), R(2.0f, 3.0f));
            }
            for (float x = x0 + 32f; x < x1 - 24f; x += 55f)
                Prefab("breakwater_light", "street_lamp", new Vector3(x, crest, port + s * 3f), seaIsMinusZ ? 0f : 180f, Vector3.one * 1.4f);
        }

        public static void BreakwaterZ(string id, float z0, float z1, float x0, float x1, bool seaIsMinusX, float crest = Crest)
        {
            Box("breakwater", x0, x1, Bottom, 1.6f, z0, z1, "breakwater", id: id + "_core");
            float wall0 = seaIsMinusX ? x0 : x1 - 2.4f, wall1 = seaIsMinusX ? x0 + 2.4f : x1;
            Box("breakwater", seaIsMinusX ? x0 + 2.4f : x0, seaIsMinusX ? x1 : x1 - 2.4f, 1.6f, crest, z0, z1, "concrete", id: id + "_road");
            Box("breakwater", wall0, wall1, 1.6f, crest + 2.5f, z0, z1, "breakwater", id: id + "_crownwall");
            float sea = seaIsMinusX ? x0 : x1, port = seaIsMinusX ? x1 : x0, s = seaIsMinusX ? -1f : 1f;
            for (float z = z0 + 1.5f; z < z1 - 1f; z += 3.2f)
            {
                Tetrapod(sea + s * R(1.0f, 2.2f), z + R(-0.6f, 0.6f), R(-0.4f, 1.2f));
                Tetrapod(sea + s * R(3.4f, 4.6f), z + 1.6f + R(-0.6f, 0.6f), R(-1.2f, 0.2f));
                Rock(port - s * R(0.4f, 1.4f), z + R(-0.6f, 0.6f), R(-0.6f, 0.4f), R(2.0f, 3.0f));
            }
            for (float z = z0 + 32f; z < z1 - 24f; z += 55f)
                Prefab("breakwater_light", "street_lamp", new Vector3(port + s * 3f, crest, z), seaIsMinusX ? 90f : 270f, Vector3.one * 1.4f);
        }

        /// <summary>Concrete tetrapod armour unit (~2.5 m), random orientation.</summary>
        public static void Tetrapod(float x, float z, float baseY)
            => Prefab("tetrapod", "tetrapod", new Vector3(x, baseY, z), R(0, 360), Vector3.one * R(0.9f, 1.15f), new Vector3(R(-25, 25), 0, R(-25, 25)));

        /// <summary>Korean breakwater lighthouse: white base, coloured tower, lantern (red = port side entering, white = starboard).</summary>
        public static void Lighthouse(string id, float x, float z, float baseY, string colour, float height = 13f)
        {
            Cyl("lighthouse", x, z, baseY, baseY + 2.2f, 3.4f, "beacon_white", id + "_base");
            Cyl("lighthouse", x, z, baseY + 2.2f, baseY + height, 2.4f, colour, id + "_tower");
            Cyl("lighthouse", x, z, baseY + height, baseY + height + 0.3f, 3.0f, "steel_dark", id + "_gallery");
            Box("lighthouse", x - 0.9f, x + 0.9f, baseY + height + 0.3f, baseY + height + 2.0f, z - 0.9f, z + 0.9f, "glass_dark", id: id + "_lantern");
            Cyl("lighthouse", x, z, baseY + height + 2.0f, baseY + height + 2.6f, 2.2f, colour, id + "_cap");
        }

        // ------------------------------------------------------------ marina pontoon
        /// <summary>Floating pontoon deck (top 0.5 m above the mean water plane).</summary>
        public static void Pontoon(float x0, float x1, float z0, float z1)
            => Box("pontoon", x0, x1, -0.45f, 0.5f, z0, z1, "pontoon");

        // ------------------------------------------------------------ industry
        public static void Tank(float x, float z, float d, float h)
        {
            Cyl("tank", x, z, Deck, Deck + h, d, "tank_white");
            Cyl("tank", x, z, Deck + h, Deck + h + 0.6f, d * 0.96f, "steel_galv");
            Box("tank", x + d / 2 - 0.3f, x + d / 2 + 0.3f, Deck, Deck + h, z - 0.4f, z + 0.4f, "steel_dark"); // stair
        }

        public static void Factory(float x0, float x1, float z0, float z1, float h, bool chimney)
        {
            if (HarborGeneric02.Buildings)
            {
                Box("factory", x0, x1, Deck, Deck + h, z0, z1, "warehouse_wall");
                Box("factory", x0 - 0.3f, x1 + 0.3f, Deck + h, Deck + h + 0.5f, z0 - 0.3f, z1 + 0.3f, "warehouse_roof");
            }
            else
            {
                // CC BY industrial hall model scaled to the plot width, repeated across the plot depth, on an asphalt pad
                Box("pad", x0, x1, Deck, Deck + 0.02f, z0, z1, "road_asphalt");
                float s = Mathf.Clamp((x1 - x0) / 155f, 0.5f, 1.6f), d = 34.1f * s;
                for (float z = z0 + d / 2f + 4f; z + d / 2f < z1 - 4f; z += d + 10f)
                    Prefab("building", "bld_warehouse3", new Vector3((x0 + x1) / 2f, Deck + 0.02f, z), 0f, Vector3.one * s);
            }
            if (chimney) Cyl("factory", x1 - 6, z1 - 6, Deck, Deck + h + R(35, 70), R(3.5f, 5f), "chimney");
        }

        /// <summary>Grid of port-industrial blocks: warehouses with trucks at the loading docks, truck parks, empty-container
        /// depots, light-industry rows with a tank, or laydown yards with steel stock and a mobile crane. Roads every `block` m.</summary>
        public static void IndustrialBlocks(float x0, float x1, float z0, float z1, float block = 115f, float roadW = 12f)
        {
            int nx = Mathf.Max(1, (int)((x1 - x0) / block)), nz = Mathf.Max(1, (int)((z1 - z0) / block));
            float bx = (x1 - x0) / nx, bz = (z1 - z0) / nz;
            for (int i = 0; i <= nx; i++) Boulevard(z0 - 4f, z1 + 4f, x0 + i * bx, false, 4, false);
            for (int k = 0; k <= nz; k++) Boulevard(x0 - 4f, x1 + 4f, z0 + k * bz, true, 4, false);
            for (int i = 0; i <= nx; i++) for (int k = 0; k <= nz; k++) if ((i + k) % 2 == 0) SignalledCrossing(new Vector2(x0 + i * bx, z0 + k * bz), true, 14f, 14f, true);
            float m = 7f + 3f + 3f;
            for (int i = 0; i < nx; i++)
                for (int k = 0; k < nz; k++)
                {
                    float ax = x0 + i * bx + m, ex = x0 + (i + 1) * bx - m, az = z0 + k * bz + m, ez = z0 + (k + 1) * bz - m;
                    float cx = (ax + ex) / 2f, cz = (az + ez) / 2f, wx = ex - ax, wz = ez - az;
                    double u = Rng.NextDouble();
                    if (u < 0.34)
                    {
                        Box("pad", ax, ex, Deck, Deck + 0.02f, az, ez, "road_asphalt");
                        float s = Mathf.Min(1f, (wx - 6f) / 63.2f);
                        Prefab("building", "bld_warehouses", new Vector3(cx, Deck + 0.02f, ez - 16f * s - 4f), 0f, Vector3.one * s);
                        for (float x = cx - 26f * s; x <= cx + 26f * s; x += 6f)                 // trucks backed onto the dock doors
                            if (Rng.NextDouble() < 0.3) ContainerTruck(x, ez - 32f * s - 10f, 90f, Rng.NextDouble() < 0.6, Rng.NextDouble() < 0.5);
                        for (float x = ax + 4f; x < ex - 4f; x += 4.5f)
                            if (Rng.NextDouble() < 0.08) ParkedVehicle( CarKey(0.05f), new Vector3(x, Deck + 0.02f, az + 3.5f), 90f);
                        for (float x = ax + 6f; x < ex - 10f; x += 9f)                                         // delivery vans at the side doors
                            if (Rng.NextDouble() < 0.08) ParkedVehicle( $"van_a_{Rng.Next(6):00}", new Vector3(x, Deck + 0.02f, ez - 32f * s - 22f), -90f);
                    }
                    else if (u < 0.40) TruckLot(ax, ex, az, ez, R(0.2f, 0.4f), R(0.3f, 0.7f));
                    else if (u < 0.68)
                    {
                        Box("pad", ax, ex, Deck, Deck + 0.02f, az, ez, "concrete");
                        int bays = Mathf.Max(1, (int)((wx - 12f) / 12.6f)), rows = Mathf.Clamp((int)((wz - 30f) / 2.75f / 2f), 2, 8);
                        ContainerBlock(ax + 6f, az + 6f, bays, rows, 5, false);
                        ContainerBlock(ax + 6f, ez - 6f - rows * 2.75f, bays, rows, 5, false);
                        Prefab("crane", "mobile_crane", new Vector3(cx, Deck + 0.02f, cz), R(0f, 360f), Vector3.one);
                    }
                    else if (u < 0.84)
                    {
                        Box("pad", ax, ex, Deck, Deck + 0.02f, az, ez, "road_asphalt");
                        for (float z = az + 10f; z < ez - 22f; z += 18f)
                            for (float x = ax + 16f; x < ex - 12f; x += 32f) Prefab("building", Rng.NextDouble() < 0.6 ? "bld_ind_warehouse" : "bld_warehouse_blue", new Vector3(x, Deck + 0.02f, z), 0f, Vector3.one);
                        Prefab("building", "bld_brick_factory", new Vector3(ax + 12f, Deck + 0.02f, ez - 12f), 0f, Vector3.one);
                        Tank(ex - 12f, ez - 12f, R(8f, 14f), R(8f, 14f));
                        for (float x = ax + 4f; x < ex - 30f; x += 4.5f)
                            if (Rng.NextDouble() < 0.06) ParkedVehicle( CarKey(0.05f), new Vector3(x, Deck + 0.02f, ez - 4f), -90f);
                    }
                    else
                    {
                        Box("pad", ax, ex, Deck, Deck + 0.02f, az, ez, "concrete");
                        for (float z = az + 6f; z < ez - 6f; z += 9f)                           // steel stock: plate stacks and pipe bundles
                            for (float x = ax + 6f; x < ex - 14f; x += 16f)
                                if (Rng.NextDouble() < 0.7)
                                    BoxC("laydown", new Vector3(x + 6f, Deck + 0.6f + R(0f, 0.6f), z), new Vector3(12f, R(1.0f, 2.4f), R(2.2f, 4f)), Rng.NextDouble() < 0.5 ? "steel_dark" : "steel_galv", R(-2f, 2f));
                        Prefab("crane", "mobile_crane", new Vector3(ex - 10f, Deck + 0.02f, cz), R(0f, 360f), Vector3.one);
                    }
                }
        }

        /// <summary>Farmland on flat land: field plots (alternating crops), farm tracks, rows of vinyl greenhouses, small orchards
        /// (tree rows) and farm sheds.</summary>
        public static void Farmland(float x0, float x1, float z0, float z1)
        {
            for (float z = z0; z < z1 - 20f; z += 70f) Box("lane", x0, x1, Deck, Deck + 0.04f, z, z + 4f, "lane_concrete");     // farm tracks
            for (float x = x0; x < x1 - 30f; x += R(36f, 48f))
                for (float z = z0 + 6f; z < z1 - 18f; z += 70f)
                {
                    double u = Rng.NextDouble();
                    float w = R(28f, 36f), d = 58f;
                    if (u < 0.55)
                        for (int b = 0; b < 2; b++) BoxC("field", new Vector3(x + w / 2f, Deck + 0.02f, z + 1f + b * 30f + 14f), new Vector3(w, 0.04f, 27f), "field", R(-2f, 2f));
                    else if (u < 0.78)
                    {
                        int n = Mathf.Max(1, (int)(w / 8.5f));
                        for (int t = 0; t < n; t++) Prefab("greenhouse", "greenhouse_vinyl", new Vector3(x + t * 8.5f + 3.5f, Deck, z + d / 2f + 1f), 0f, new Vector3(1f, 1f, (d - 4f) / 40f));
                    }
                    else if (u < 0.92)
                        for (float tx = x + 3f; tx < x + w; tx += 6f) for (float tz = z + 4f; tz < z + d; tz += 12f) Tree(tx, tz, Deck, R(3.5f, 5f));
                    else
                    {
                        Prefab("house", "shed_steel", new Vector3(x + 6f, Deck, z + 10f), R(-4f, 4f), Vector3.one);
                        ParkedVehicle( "truck_kei", new Vector3(x + 14f, Deck, z + 8f), 90f);
                    }
                }
        }

        /// <summary>Working fishing-quay gear in clusters between x0..x1, z0..z1: pallets of fish boxes (stacked side by side),
        /// net piles, drums and a parked 1-ton truck per cluster. Skips the x ranges in <paramref name="keepClear"/> (pier roots).</summary>
        public static void QuayGear(float x0, float x1, float z0, float z1, int clusters, params Vector2[] keepClear)
        {
            string[] boxCols = { "blue", "blue", "red", "green", "white" };
            for (int c = 0; c < clusters; c++)
            {
                float cx = R(x0 + 4f, x1 - 4f), cz = R(z0 + 2f, z1 - 2f);
                if (keepClear.Any(k => cx > k.x - 4f && cx < k.y + 4f)) continue;
                int n = Rng.Next(3, 9); var col = boxCols[Rng.Next(boxCols.Length)];
                for (int i = 0; i < n; i++) Prefab("fishing_gear", "fishbox_stack_" + col, new Vector3(cx + (i % 4) * 1.25f, Deck, cz + (i / 4) * 1.05f), R(-3f, 3f), Vector3.one);
                if (Rng.NextDouble() < 0.6) Prefab("fishing_gear", "net_pile", new Vector3(cx + R(-4f, -2.5f), Deck, cz + R(0f, 2f)), R(0f, 360f), Vector3.one * R(0.8f, 1.3f));
                if (Rng.NextDouble() < 0.5) for (int i = 0; i < 3; i++) Prefab("fishing_gear", "barrel", new Vector3(cx + 6f + i * 0.6f, Deck, cz + R(-0.3f, 0.3f)), 0f, Vector3.one);
                // Service vehicles use the marked market bays, never random gear-cluster positions.
            }
        }

        /// <summary>Beach furniture: rows of parasols (with a lounger pair) and lifeguard towers along x.</summary>
        public static void BeachFurniture(float x0, float x1, float z0, float z1, float y)
        {
            string[] cols = { "red", "blue", "white", "green", "navy", "red", "blue" };
            for (float z = z0; z <= z1; z += 7f)
                for (float x = x0 + R(0f, 3f); x < x1; x += R(5f, 8f))
                {
                    if (Rng.NextDouble() < 0.25) continue;
                    Prefab("beach", "beach_parasol_" + cols[Rng.Next(cols.Length)], new Vector3(x, y, z + R(-0.6f, 0.6f)), R(0f, 360f), Vector3.one);
                }
            for (float x = x0 + 60f; x < x1; x += 180f) Prefab("beach", "lifeguard_tower", new Vector3(x, y, z0 - 6f), 180f, Vector3.one);
        }

        /// <summary>Promenade furniture along x: benches facing the sea (seaDir = -1 -> sea at -z) and planted trees.</summary>
        public static void PromenadeFurniture(float x0, float x1, float z, float seaDir)
        {
            for (float x = x0 + 5f; x < x1; x += 14f)
            {
                Prefab("bench", "bench", new Vector3(x, Deck, z), seaDir < 0 ? 0f : 180f, Vector3.one);
                if (Rng.NextDouble() < 0.5) Tree(x + 7f, z - seaDir * 2.5f, Deck, R(5f, 7f));
            }
        }



        // ------------------------------------------------------------ masts for the camera presets of a map
        public static void CameraMasts(CameraPreset[] presets, IDictionary<string, float> baseY)
        {
            foreach (var p in presets)
            {
                float b = baseY.TryGetValue(p.id, out var v) ? v : Deck;
                Box("mast", p.position.x - 0.18f, p.position.x + 0.18f, b, p.position.y - 0.6f, p.position.z - 0.18f, p.position.z + 0.18f, "steel_galv", id: "mast_" + p.id);
                Box("mast", p.position.x - 0.3f, p.position.x + 0.3f, p.position.y - 0.6f, p.position.y - 0.4f, p.position.z - 0.3f, p.position.z + 0.3f, "steel_galv", id: "mast_head_" + p.id);
            }
        }

        public static void LightMast(float x, float z, float h = 30f, float baseY = Deck)
        {
            Cyl("light_mast", x, z, baseY, baseY + h, 0.7f, "steel_galv");
            Box("light_mast", x - 2.2f, x + 2.2f, baseY + h, baseY + h + 0.6f, z - 2.2f, z + 2.2f, "steel_galv");
            foreach (var d in new[] { new Vector2(-1.6f, 0), new Vector2(1.6f, 0), new Vector2(0, -1.6f), new Vector2(0, 1.6f) })
                Box("light_mast", x + d.x - 0.5f, x + d.x + 0.5f, baseY + h + 0.6f, baseY + h + 1.3f, z + d.y - 0.5f, z + d.y + 0.5f, "lamp");
        }

        /// <summary>Old tyres hung on a quay face along X (tyre faces the water).</summary>
        public static void TyreFendersZ(float x0, float x1, float z, float dir, float step = 6f)
        {
            for (float x = x0 + step / 2; x < x1; x += step)
                Prefab("fender", "tyre", new Vector3(x, 0.9f, z + dir * 0.12f), 0, Vector3.one * 1.6f, new Vector3(0, 0, R(-8, 8)));
        }

        public static void TyreFendersX(float z0, float z1, float x, float dir, float step = 6f)
        {
            for (float z = z0 + step / 2; z < z1; z += step)
                Prefab("fender", "tyre", new Vector3(x + dir * 0.12f, 0.9f, z), 90, Vector3.one * 1.6f, new Vector3(0, 0, R(-8, 8)));
        }

        public static void StreetLamps(float x0, float x1, float z, float step, float scale = 2f)
        {
            for (float x = x0; x <= x1; x += step) Prefab("street_light", "street_lamp", new Vector3(x, Deck, z), 0, Vector3.one * scale);
        }

        public static void Lifebuoys(float x0, float x1, float y, float z, float step)
        {
            for (float x = x0; x <= x1; x += step) Prefab("lifebuoy", "lifebuoy", new Vector3(x, y, z), 0, Vector3.one);
        }

        // ------------------------------------------------------------ coastal village on flat land
        /// <summary>Two-lane coastal road (edge / centre lines, guard rail on the sea side, lamps and poles on the land side) with
        /// rows of village houses on concrete pads behind it, back lanes between the rows, cross lanes, and fields with a few
        /// trees beyond. seaDir = +1/-1: which way (in z) the sea lies from the road. Deterministic from the shared Rng.</summary>
        /// <summary>Two-lane coastal road along x: asphalt, edge / centre lines, guard rail on the sea side (open between gapX0 and
        /// gapX1, e.g. where a bridge ramp joins), lamps and power poles on the land side. No Rng use.</summary>
        public static void CoastalRoad(float x0, float x1, float roadZ, float seaDir, float gapX0 = 0f, float gapX1 = 0f)
        {
            float land = -seaDir;
            Box("road", x0, x1, Deck, Deck + 0.06f, roadZ - 3.5f, roadZ + 3.5f, "road_asphalt");
            foreach (var e in new[] { -3.3f, 3.3f }) Box("road", x0, x1, Deck + 0.06f, Deck + 0.075f, roadZ + e - 0.08f, roadZ + e + 0.08f, "paint_white");
            Box("road", x0, x1, Deck + 0.06f, Deck + 0.075f, roadZ - 0.08f, roadZ + 0.08f, "paint_yellow");
            float gz = roadZ + seaDir * 4.3f;
            bool gap = gapX1 > gapX0;
            for (float x = x0 + 2f; x < x1; x += 4f)
                if (!gap || x < gapX0 || x > gapX1) Box("guard_rail", x - 0.06f, x + 0.06f, Deck, Deck + 0.75f, gz - 0.06f, gz + 0.06f, "guard_rail");
            if (!gap) Box("guard_rail", x0, x1, Deck + 0.5f, Deck + 0.82f, gz - 0.05f, gz + 0.05f, "guard_rail");
            else
            {
                Box("guard_rail", x0, gapX0, Deck + 0.5f, Deck + 0.82f, gz - 0.05f, gz + 0.05f, "guard_rail");
                Box("guard_rail", gapX1, x1, Deck + 0.5f, Deck + 0.82f, gz - 0.05f, gz + 0.05f, "guard_rail");
            }
            float lz = roadZ + land * 4.6f;
            for (float x = x0 + 12f; x < x1; x += 30f)
                if (!gap || x < gapX0 - 3f || x > gapX1 + 3f) Prefab("street_light", "street_lamp", new Vector3(x, Deck, lz), 0, Vector3.one * 2f);
            for (float x = x0 + 24f; x < x1; x += 36f)
                if (!gap || x < gapX0 - 3f || x > gapX1 + 3f) Prefab("power_pole", "power_pole", new Vector3(x, Deck, lz + land * 1.0f), 90, Vector3.one);
        }

        public static void CoastalVillage(float x0, float x1, float roadZ, float seaDir, int rows = 2, bool fields = true)
        {
            float land = -seaDir;
            CoastalRoad(x0, x1, roadZ, seaDir);
            // house rows and lanes
            var kinds = HillFeatures.HouseKinds;
            var cross = new List<float>();
            for (float x = x0 + R(20f, 50f); x < x1 - 15f; x += R(55f, 85f)) cross.Add(x);
            float rowPitch = 22f, firstRow = roadZ + land * 12f;
            for (int r = 0; r < rows; r++)
            {
                float rz = firstRow + land * r * rowPitch;
                float laneZ = rz + land * rowPitch / 2f;
                if (r < rows - 1) Box("lane", x0 + 4f, x1 - 4f, Deck + 0.02f, Deck + 0.05f, laneZ - 1.75f, laneZ + 1.75f, "lane_concrete");
                for (float x = x0 + 9f; x < x1 - 9f; x += R(12f, 15f))
                {
                    bool nearCross = false; foreach (var cx in cross) if (Mathf.Abs(cx - x) < 8f) nearCross = true;
                    if (nearCross || Rng.NextDouble() > 0.86) { if (Rng.NextDouble() < 0.5) Tree(x, rz + land * R(-4f, 4f), Deck, R(5f, 9f)); continue; }
                    float yaw = (land > 0 ? 0f : 180f) + R(-5f, 5f);              // front (-z) faces the road
                    if (Rng.NextDouble() < 0.3)
                    {
                        // 2-3 storey Korean multi-family building (CC BY scan-based model) at village scale
                        float vs = R(0.5f, 0.65f);
                        BoxC("pad", new Vector3(x + 6f, Deck + 0.025f, rz), new Vector3(27f * vs + 3f, 0.05f, 25f * vs + 3f), "lane_concrete", yaw);
                        Prefab("house", "bld_villa_kr", new Vector3(x + 6f, Deck + 0.05f, rz), yaw, Vector3.one * vs);
                        x += 8f;
                        continue;
                    }
                    int kind = Rng.NextDouble() < 0.12 ? 5 : Rng.Next(5);
                    var k = kinds[kind]; float sc = R(0.92f, 1.08f);
                    BoxC("pad", new Vector3(x, Deck + 0.025f, rz), new Vector3(k.w * sc + 3f, 0.05f, k.d * sc + 3f), "lane_concrete", yaw);
                    Prefab("house", k.key, new Vector3(x, Deck + 0.05f, rz), yaw, Vector3.one * sc);
                }
            }
            float backZ = firstRow + land * ((rows - 0.5f) * rowPitch);
            foreach (var cx in cross)
                Box("lane", cx - 2f, cx + 2f, Deck + 0.02f, Deck + 0.05f, Mathf.Min(roadZ + land * 3.5f, backZ), Mathf.Max(roadZ + land * 3.5f, backZ), "lane_concrete");
            if (!fields) return;
            // fields and tree clumps beyond the last row
            float fz0 = backZ + land * 4f;
            for (int band = 0; band < 2; band++)
            {
                float bz = fz0 + land * band * 26f;
                for (float x = x0 + 6f; x < x1 - 30f; x += R(28f, 40f))
                {
                    float w = R(22f, 34f), d = R(16f, 24f);
                    if (Rng.NextDouble() < 0.72) BoxC("field", new Vector3(x + w / 2f, Deck + 0.02f, bz + land * d / 2f), new Vector3(w, 0.04f, d), "field", R(-3f, 3f));
                    else for (int t = 0; t < 5; t++) Tree(x + R(0, w), bz + land * R(2f, d), Deck, R(5f, 9f));
                }
            }
        }

        // ------------------------------------------------------------ road vehicles and parking (background, never labelled)
        /// <summary>Random parked-car prefab key: Korean-like mix (sedans and SUVs dominate, white / black / grey most common,
        /// some 1-ton flatbed trucks).</summary>
        /// <summary>car_a pack colours (comrade1280): 00 yellow hatch, 01 blue convertible, 02 red coupe, 03 black SUV, 04 green
        /// pickup, 05 teal SUV, 06 purple sports, 07 red minivan, 08 blue hatch, 09 yellow sedan. Korean streets are mostly
        /// white / grey / black, so the quiet models are weighted up and the vivid ones kept rare.</summary>
        static readonly string[] CarKeysQuiet = { "car_a_03", "car_a_03", "car_a_05", "car_a_01", "car_a_08", "car_b_01", "car_b_03", "car_b_03", "car_b_07", "car_b_08", "car_b_09" };
        static readonly string[] CarKeysVivid = { "car_a_00", "car_a_02", "car_a_04", "car_a_07", "car_a_09", "car_b_00", "car_b_02", "car_b_04", "car_b_05", "car_b_06" };

        public static string CarKey(float truckShare = 0.07f)
        {
            if (Rng.NextDouble() < truckShare) return "truck_kei";
            return Rng.NextDouble() < 0.95 ? CarKeysQuiet[Rng.Next(CarKeysQuiet.Length)] : CarKeysVivid[Rng.Next(CarKeysVivid.Length)];
        }

        /// <summary>Vehicle packs are baked with the long axis/nose along +X. Single imports retain their source axes.</summary>
        public static float VehicleYawOffset(string key)
        {
            if (key.StartsWith("car_a_") || key.StartsWith("car_b_") || key.StartsWith("van_a_")) return 0f;
            if (key == "truck_kei" || key == "truck_semi_a") return 90f;
            if (key == "forklift_a") return -90f;
            return 0f;
        }

        /// <summary>Places a vehicle prefab with its nose along `yawTravel` (yaw convention of Prefab: 0 = +x).</summary>
        public static void Vehicle(string kind, string key, Vector3 pos, float yawTravel, float scale = 1f)
            => Prefab(kind, key, pos, yawTravel + VehicleYawOffset(key), Vector3.one * scale);

        /// <summary>A marked service bay, centred on the same pivot used to place its vehicle.</summary>
        public static void ParkedVehicle(string key, Vector3 pos, float yawTravel)
        {
            var q = Quaternion.Euler(0, yawTravel, 0);
            foreach (float side in new[] { -1f, 1f })
                BoxC("parking_lines", pos + q * new Vector3(0, 0.025f, side * 1.4f), new Vector3(6.5f, 0.02f, 0.10f), "paint_white", yawTravel);
            BoxC("parking_lines", pos + q * new Vector3(3.25f, 0.025f, 0), new Vector3(0.1f, 0.02f, 2.8f), "paint_white", yawTravel);
            Vehicle("car", key, pos, yawTravel);
        }

        /// <summary>Surface car park: asphalt with kerbs, stall rows along x (2.5 x 5 m stalls, 6.5 m aisles, rows back to back),
        /// painted stall lines, lamps on the medians, cars parked nose-in or reversed in with small offsets, trees on the long
        /// edges. occupancy = share of stalls taken.</summary>
        /// <summary>Surface car park laid out like a real one: double rows of 2.5 x 5 m stalls back to back (10 m), a 6.5 m
        /// drive aisle between every double row, a 1.5 m planted median with lamps every 30 m between the back-to-back stalls,
        /// kerbs around, entrance gaps on the long sides. Cars stand squarely in their stalls (nose-in or reversed, the whole
        /// row the same way, +-1.2 deg), occupancy fills stalls from the lot centre outwards so the lot reads as "parked in
        /// order" rather than scattered. Stall lines are painted per stall.</summary>
        public static void ParkingLot(float x0, float x1, float z0, float z1, float occupancy, string id = null, float truckShare = 0.07f, bool trees = true)
        {
            const float y = Deck + 0.03f, stallW = 2.8f, stallD = 5.8f, aisle = 6.5f, median = 1.5f;
            Box("parking", x0, x1, Deck, y, z0, z1, "road_asphalt", id: id);
            foreach (var (za, zb) in new[] { (z0 - 0.3f, z0), (z1, z1 + 0.3f) }) Box("kerb", x0, x1, Deck, Deck + 0.15f, za, zb, "lane_concrete");
            foreach (var (xa, xb) in new[] { (x0 - 0.3f, x0), (x1, x1 + 0.3f) }) Box("kerb", xa, xb, Deck, Deck + 0.15f, z0, z1, "lane_concrete");
            int nStalls = Mathf.Max(2, (int)((x1 - x0 - 8f) / stallW));
            float sx0 = (x0 + x1) / 2f - nStalls * stallW / 2f;
            float module = aisle + 2f * stallD + median;                                                     // aisle + double row + median
            int modules = Mathf.Max(1, (int)((z1 - z0 - 4f + median) / module));
            float zStart = (z0 + z1) / 2f - (modules * module - median) / 2f;
            int centre = nStalls / 2;
            for (int m = 0; m < modules; m++)
            {
                float zA = zStart + m * module;                                                               // aisle from zA to zA+aisle
                float rowA = zA + aisle, rowB = rowA + stallD;                                                // row A: zA+aisle .. +5, row B: +5 .. +10
                bool noseInA = true, noseInB = true;
                for (int i = 0; i <= nStalls; i++) foreach (var (za, zb) in new[] { (rowA, rowB), (rowB, rowB + stallD) })
                    Box("parking_lines", sx0 + i * stallW - 0.06f, sx0 + i * stallW + 0.06f, y, y + 0.01f, za + 0.2f, zb - 0.2f, "paint_white");
                Box("parking_lines", sx0, sx0 + nStalls * stallW, y, y + 0.01f, rowB - 0.06f, rowB + 0.06f, "paint_white");
                for (int i = 0; i < nStalls; i++)
                {
                    float fill = 1f - Mathf.Abs(i - centre) / (float)Mathf.Max(1, nStalls) * 0.8f;              // centre stalls fill first
                    float x = sx0 + i * stallW + stallW / 2f;
                    // row A (next to the aisle on the -z side): nose-in = nose away from the aisle (+z)
                    if (Rng.NextDouble() < occupancy * fill)
                        Vehicle("car", CarKey(truckShare), new Vector3(x, y, rowA + stallD / 2f), noseInA ? -90f : 90f);
                    if (m == modules - 1 && rowB + stallD > z1 - 1f) continue;
                    // row B (back to back, nose-in = nose towards -z)
                    if (Rng.NextDouble() < occupancy * fill)
                        Vehicle("car", CarKey(truckShare), new Vector3(x, y, rowB + stallD / 2f), noseInB ? 90f : -90f);
                }
                float zm = rowB + stallD;                                                                      // median after row B
                if (zm + median <= z1)
                {
                    Box("kerb", sx0, sx0 + nStalls * stallW, Deck, Deck + 0.15f, zm, zm + median, "lane_concrete");
                    for (float x = sx0 + 5f; x < sx0 + nStalls * stallW; x += 30f) Prefab("street_light", "street_lamp", new Vector3(x, Deck + 0.15f, zm + median / 2f), 0, Vector3.one * 2f);
                    if (trees) for (float x = sx0 + 15f; x < sx0 + nStalls * stallW - 5f; x += 30f) Tree(x, zm + median / 2f, Deck, R(4f, 6f));
                }
            }
            if (!trees) return;
            for (float x = x0 + 6f; x < x1 - 4f; x += 12f) { Tree(x, z0 - 2.5f, Deck, R(5f, 8f)); Tree(x, z1 + 2.5f, Deck, R(5f, 8f)); }
        }

        static readonly string[] TractorCabs = { "red", "white", "blue", "green" };

        /// <summary>Container truck: tractor unit + 40 ft skeletal chassis (prefabs), optionally carrying a 40 ft or 20 ft box in
        /// the shared terminal colours. yaw rotates +x (direction of travel) as for Prefab.</summary>
        public static void ContainerTruck(float x, float z, float yaw, bool loaded, bool fortyFoot = true)
        {
            var dir = Quaternion.Euler(0, yaw, 0);
            Vector3 P(float along, float yy) => new Vector3(x, yy, z) + dir * new Vector3(along, 0, 0);
            if (loaded && fortyFoot && Rng.NextDouble() < 0.5) { Vehicle("truck", "truck_container_a", P(1.5f, Deck), yaw); return; }   // CC BY tractor + 40 ft box
            Prefab("truck", "trailer_chassis", P(0f, Deck), yaw, Vector3.one);
            Vehicle("truck", "truck_semi_a", P(7.2f, Deck), yaw);                                                            // CC BY cab-over tractor
            if (!loaded) return;
            if (fortyFoot) BoxC("container", P(0f, Deck + 1.44f + 1.295f), new Vector3(12.19f, 2.59f, 2.44f), Colours[Rng.Next(Colours.Length)], yaw);
            else BoxC("container", P(-2.9f, Deck + 1.44f + 1.295f), new Vector3(6.06f, 2.59f, 2.44f), Colours[Rng.Next(Colours.Length)], yaw);
        }

        /// <summary>Truck park / staging lot: rows of 4.5 m x 20 m bays (long axis z) separated by 16 m aisles.
        /// occupancy = share of bays taken; loaded = share of parked trucks carrying a box.</summary>
        public static void TruckLot(float x0, float x1, float z0, float z1, float occupancy, float loaded, string id = null)
        {
            const float y = Deck + 0.03f;
            Box("parking", x0, x1, Deck, y, z0, z1, "road_asphalt", id: id);
            Box("kerb", x0, x1, Deck, Deck + 0.15f, z0 - 0.3f, z0, "lane_concrete");
            Box("kerb", x0, x1, Deck, Deck + 0.15f, z1, z1 + 0.3f, "lane_concrete");
            for (float zr = z0 + 3f; zr + 20f <= z1 - 3f; zr += 36f)
            {
                float zc = zr + 10f;
                float dir = Rng.NextDouble() < 0.5 ? 1f : -1f;                      // whole row parked the same way
                for (float x = x0 + 4f; x + 4.5f <= x1 - 4f; x += 4.5f)
                {
                    Box("parking_lines", x - 0.06f, x + 0.06f, y, y + 0.01f, zr, zr + 20f, "paint_white");
                    bool take = Rng.NextDouble() < occupancy;
                    if (!take) continue;
                    ContainerTruck(x + 2.25f, zc - dir * 1.9f, dir > 0 ? -90f : 90f, Rng.NextDouble() < loaded, Rng.NextDouble() < 0.8);
                }
                for (float x = x0 + 10f; x < x1; x += 40f) Prefab("street_light", "street_lamp", new Vector3(x, y, zr + 21.5f), 0, Vector3.one * 2f);
            }
        }

        /// <summary>Plain two-lane roads (asphalt + centre line) along x or z.</summary>
        public static void RoadX(float x0, float x1, float z, float width = 8f)
        {
            Box("road", x0, x1, Deck, Deck + 0.06f, z - width / 2, z + width / 2, "road_asphalt");
            Box("road", x0, x1, Deck + 0.06f, Deck + 0.075f, z - 0.08f, z + 0.08f, "paint_yellow");
        }
        public static void RoadZ(float z0, float z1, float x, float width = 8f)
        {
            Box("road", x - width / 2, x + width / 2, Deck, Deck + 0.06f, z0, z1, "road_asphalt");
            Box("road", x - 0.08f, x + 0.08f, Deck + 0.06f, Deck + 0.075f, z0, z1, "paint_yellow");
        }

        // ------------------------------------------------------------ boulevards, intersections, buffers
        /// <summary>Urban road along x or z: asphalt with edge and dashed lane lines, raised sidewalks with kerbs on both sides
        /// (sidewalk 3 m), street lamps every 30 m, street trees every 15 m on the sidewalk. Lane count 2 or 4 (width 8 / 14 m).
        /// `signalsEvery` > 0 adds a signalised crossing (crosswalks, vehicle + pedestrian signals) at that spacing.</summary>
        public static void Boulevard(float a0, float a1, float c, bool alongX, int lanes = 4, bool trees = true, float signalsEvery = 0f)
        {
            float w = lanes >= 4 ? 14f : 8f, hw = w / 2f, sw = 3f;
            void B(string kind, float u0, float u1, float y0, float y1, float v0, float v1, string mat)
            {
                if (alongX) Box(kind, u0, u1, y0, y1, c + v0, c + v1, mat); else Box(kind, c + v0, c + v1, y0, y1, u0, u1, mat);
            }
            B("road", a0, a1, Deck, Deck + 0.06f, -hw, hw, "road_asphalt");
            foreach (var e in new[] { -hw + 0.3f, hw - 0.3f }) B("marking", a0, a1, Deck + 0.06f, Deck + 0.075f, e - 0.08f, e + 0.08f, "paint_white");
            B("marking", a0, a1, Deck + 0.06f, Deck + 0.075f, -0.1f, 0.1f, "paint_yellow");
            if (lanes >= 4) foreach (var e in new[] { -hw / 2f, hw / 2f }) for (float u = a0 + 2f; u < a1 - 3f; u += 8f) B("marking", u, u + 3f, Deck + 0.06f, Deck + 0.075f, e - 0.07f, e + 0.07f, "paint_white");
            foreach (var s in new[] { -1f, 1f })
            {
                B("sidewalk", a0, a1, Deck, Deck + 0.16f, s * hw, s * (hw + sw), "lane_concrete");
                B("kerb", a0, a1, Deck, Deck + 0.17f, s * hw, s * (hw + 0.25f), "concrete");
            }
            for (float u = a0 + 10f; u < a1 - 5f; u += 30f)
                foreach (var s in new[] { -1f, 1f })
                {
                    var p = alongX ? new Vector3(u, Deck + 0.16f, c + s * (hw + 0.8f)) : new Vector3(c + s * (hw + 0.8f), Deck + 0.16f, u);
                    Prefab("street_light", "street_lamp", p, alongX ? 0f : 90f, Vector3.one * 2f);
                }
            if (trees)
                for (float u = a0 + 18f; u < a1 - 8f; u += 30f)                                               // one tree per 30 m per side
                    foreach (var s in new[] { -1f, 1f })
                    {
                        var p = alongX ? new Vector3(u, Deck, c + s * (hw + 2.2f)) : new Vector3(c + s * (hw + 2.2f), Deck, u);
                        Tree(p.x, p.z, Deck, R(5f, 8f));
                    }
            if (signalsEvery > 0f)
                for (float u = a0 + signalsEvery / 2f; u < a1 - 20f; u += signalsEvery)
                    SignalledCrossing(alongX ? new Vector2(u, c) : new Vector2(c, u), alongX, w, 8f, false);
        }

        /// <summary>Road intersection at `centre` of a road along the main axis (width wMain) and a crossing road (wCross):
        /// stop lines, zebra crosswalks on all four arms, four vehicle signals on the far-side corners facing each approach,
        /// pedestrian signals beside each crosswalk. `full` = cross road exists on both sides, else a T-junction (cross road on
        /// the +side only).</summary>
        public static void SignalledCrossing(Vector2 centre, bool mainAlongX, float wMain, float wCross, bool full)
        {
            float hm = wMain / 2f, hc = wCross / 2f;
            Vector3 P(float along, float across) => mainAlongX ? new Vector3(centre.x + along, Deck, centre.y + across) : new Vector3(centre.x + across, Deck, centre.y + along);
            void Zebra(float along0, float along1, float across0, float across1)
            {
                for (float t = across0 + 0.3f; t < across1 - 0.3f; t += 1.0f)
                {
                    var p = P((along0 + along1) / 2f, t + 0.25f);
                    BoxC("marking", new Vector3(p.x, Deck + 0.065f, p.z), mainAlongX ? new Vector3(along1 - along0, 0.012f, 0.5f) : new Vector3(0.5f, 0.012f, along1 - along0), "paint_white");
                }
            }
            // crosswalks across the main road (on the +/- along sides of the junction) and across the cross road
            foreach (var s in new[] { -1f, 1f })
            {
                Zebra(s * (hc + 1.5f) - 1.5f, s * (hc + 1.5f) + 1.5f, -hm, hm);                                 // across main road
                if (full || s > 0) { var p0 = P(-hm, s * (hm + 1.5f)); var p1 = P(hm, s * (hm + 1.5f)); }
            }
            foreach (var s in full ? new[] { -1f, 1f } : new[] { 1f })
                for (float t = -hc + 0.3f; t < hc - 0.3f; t += 1.0f)
                {
                    var p = P(t + 0.25f, s * (hm + 1.5f));
                    BoxC("marking", new Vector3(p.x, Deck + 0.065f, p.z), mainAlongX ? new Vector3(0.5f, 0.012f, 3f) : new Vector3(3f, 0.012f, 0.5f), "paint_white");
                }
            // stop lines
            foreach (var s in new[] { -1f, 1f })
            {
                var p = P(s * (hc + 3.4f), -s * hm / 2f);
                BoxC("marking", new Vector3(p.x, Deck + 0.065f, p.z), mainAlongX ? new Vector3(0.4f, 0.012f, hm) : new Vector3(hm, 0.012f, 0.4f), "paint_white");
            }
            // signals: vehicle signal on each far corner, facing the approach (prefab faces -z at yaw 0; mast arm over the road)
            float yawMain = mainAlongX ? 0f : 90f;
            foreach (var s in new[] { -1f, 1f })
            {
                var corner = P(s * (hc + 4.5f), -s * (hm + 1.2f));
                Prefab("signal", "signal_vehicle", corner, yawMain + (s > 0 ? 180f : 0f), Vector3.one);
                var pc = P(s * (hc + 1.5f), -s * (hm + 0.9f));
                Prefab("signal", "signal_ped", pc, yawMain + (s > 0 ? 0f : 180f), Vector3.one);
                if (full || s > 0)
                {
                    var corner2 = P(s * (hc + 1.2f), s * (hm + 4.5f));
                    Prefab("signal", "signal_vehicle", corner2, yawMain + 90f + (s > 0 ? 180f : 0f), Vector3.one);
                    var pc2 = P(s * (hc + 0.9f), s * (hm + 1.5f));
                    Prefab("signal", "signal_ped", pc2, yawMain + 90f + (s > 0 ? 0f : 180f), Vector3.one);
                }
            }
        }

        /// <summary>Green buffer strip between the port and the town: hedge / tree rows, a 2 m fence line on the port side,
        /// scrub between. Along x between x0..x1, z0..z1 (z0 = port side).</summary>
        public static void GreenBuffer(float x0, float x1, float z0, float z1)
        {
            float d = z1 - z0;
            for (float x = x0 + 4f; x < x1; x += 3.9f) Prefab("fence", "fence", new Vector3(x, Deck, z0 + 1f), 0, Vector3.one);
            for (float x = x0 + 5f; x < x1 - 3f; x += R(12f, 18f)) Tree(x, z0 + d * R(0.2f, 0.8f), Deck, R(7f, 12f));
            Scrub(x0, x1, z0 + 2f, z1 - 2f, (int)((x1 - x0) * d / 400f), 2f, 4f);
            Box("lane", x0, x1, Deck, Deck + 0.04f, z1 - 3f, z1 - 1f, "lane_concrete");   // walking path on the town side
            for (float x = x0 + 12f; x < x1; x += 14f) Prefab("bench", "bench", new Vector3(x, Deck, z1 - 4.2f), 180f, Vector3.one);
        }

        /// <summary>Bus stop on the sidewalk of a boulevard along x at z (side = +1/-1), with a citylight panel and a bin.</summary>
        public static void BusStop(float x, float z, float side)
        {
            float yaw = side > 0 ? 180f : 0f;
            Prefab("street", "bus_shelter", new Vector3(x, Deck + 0.16f, z), yaw, Vector3.one);
            Prefab("street", "citylight_panel", new Vector3(x + 5f, Deck + 0.16f, z), yaw, Vector3.one);
            Prefab("street", "street_bin", new Vector3(x - 3.5f, Deck + 0.16f, z + side * 0.4f), yaw, Vector3.one * 0.6f);
        }

        /// <summary>Elevated road along a polyline of deck-top points: 3 m deck segments (0.4 m overlap at the joints), asphalt,
        /// parapets, and paired columns with a cross head down to groundY wherever the deck clears the ground by more than 4 m.</summary>
        public static void Viaduct(IList<Vector3> top, float width, float pierStep = 45f, float groundY = -10f)
        {
            float acc = 0f, nextPier = pierStep * 0.5f;
            for (int i = 0; i + 1 < top.Count; i++)
            {
                Vector3 a = top[i], b = top[i + 1], d = b - a; float len = d.magnitude; var u = d / len;
                Vector3 a2 = a - u * 0.4f, b2 = b + u * 0.4f;
                Beam("bridge", a2 + Vector3.down * 1.5f, b2 + Vector3.down * 1.5f, width, 3f, "concrete");
                Beam("bridge", a2 + Vector3.up * 0.04f, b2 + Vector3.up * 0.04f, width - 1.6f, 0.08f, "road_asphalt");
                var side = Vector3.Cross(Vector3.up, new Vector3(u.x, 0, u.z).normalized);
                foreach (var s in new[] { -1f, 1f })
                    Beam("bridge", a2 + side * s * (width / 2 - 0.3f) + Vector3.up * 0.5f, b2 + side * s * (width / 2 - 0.3f) + Vector3.up * 0.5f, 0.5f, 1.0f, "concrete");
                float yaw = Mathf.Atan2(u.x, u.z) * Mathf.Rad2Deg;
                while (nextPier <= acc + len)
                {
                    var p = a + u * (nextPier - acc);
                    if (p.y - 3f > groundY + 4f)
                    {
                        foreach (var s in new[] { -1f, 1f })
                        {
                            var c = p + side * s * (width / 2 - 3f);
                            float y0 = groundY, y1 = p.y - 3f;
                            BoxC("bridge", new Vector3(c.x, (y0 + y1) / 2, c.z), new Vector3(2.4f, y1 - y0, 2.4f), "concrete", yaw);
                        }
                        BoxC("bridge", new Vector3(p.x, p.y - 3.6f, p.z), new Vector3(width - 2f, 1.2f, 2.8f), "concrete", yaw + 90f);
                    }
                    nextPier += pierStep;
                }
                acc += len;
            }
        }

        /// <summary>Grid of city blocks on flat land: roads every `block` m in x and z, and per block one of: towers on a paved
        /// plot (bld_tower, 2-4), a surface car park, a shop row with a small car park, a pocket park, or warehouses on asphalt.
        /// Fills large flats with built-up land at CCTV distance without crude procedural boxes.</summary>
        public static void CityBlocks(float x0, float x1, float z0, float z1, float block = 115f, float roadW = 12f)
        {
            int nx = Mathf.Max(1, (int)((x1 - x0) / block)), nz = Mathf.Max(1, (int)((z1 - z0) / block));
            float bx = (x1 - x0) / nx, bz = (z1 - z0) / nz;
            // streets: 2-lane boulevards with sidewalks, lamps and street trees; every intersection signalised
            for (int i = 0; i <= nx; i++) Boulevard(z0 - 4f, z1 + 4f, x0 + i * bx, false, 2, i % 2 == 0);
            for (int k = 0; k <= nz; k++) Boulevard(x0 - 4f, x1 + 4f, z0 + k * bz, true, 2, k % 2 == 0);
            for (int i = 0; i <= nx; i++) for (int k = 0; k <= nz; k++) SignalledCrossing(new Vector2(x0 + i * bx, z0 + k * bz), true, 8f, 8f, true);
            for (int i = 0; i < nx; i++) if (Rng.NextDouble() < 0.5) BusStop(x0 + i * bx + bx / 2f, z0 + 4f + 1.5f, 1f);
            float m = 4f + 3f + 3f;
            for (int i = 0; i < nx; i++)
                for (int k = 0; k < nz; k++)
                {
                    float ax = x0 + i * bx + m, ex = x0 + (i + 1) * bx - m, az = z0 + k * bz + m, ez = z0 + (k + 1) * bz - m;
                    float cx = (ax + ex) / 2f, cz = (az + ez) / 2f, wx = ex - ax, wz = ez - az;
                    double u = Rng.NextDouble();
                    if (u < 0.50)
                    {
                        Box("pad", ax, ex, Deck, Deck + 0.02f, az, ez, "lane_concrete");
                        ApartmentPlot(ax, ex, az, ez);
                        for (int t = 0; t < 2; t++) Tree(R(ax + 2f, ex - 2f), Rng.NextDouble() < 0.5 ? az + 2f : ez - 2f, Deck, R(5f, 8f));
                        // residents' parking along both long sides of the plot
                        for (float x = ax + 4f; x < ex - 4f; x += 4.5f)
                            if (Rng.NextDouble() < 0.07) ParkedVehicle( CarKey(0.01f), new Vector3(x, Deck + 0.02f, az + 3.5f), 90f);
                    }
                    else if (u < 0.54) GreenBuffer(ax, ex, az, ez);
                    else if (u < 0.78)
                    {
                        Box("pad", ax, ex, Deck, Deck + 0.02f, az, ez, "lane_concrete");
                        ShopRow(ax + 4f, ex - 4f, az + 6f, 180f);
                        Box("shop_forecourt", ax + 2f, ex - 2f, Deck, Deck + 0.03f, az + 16f, Mathf.Min(ez - 2f, az + 52f), "lane_concrete");
                        if (ez - az > 70f) ShopRow(ax + 4f, ex - 4f, ez - 6f, 0f);
                    }
                    else if (u < 0.90)
                    {
                        for (int t = 0; t < 8; t++) Tree(R(ax + 4f, ex - 4f), R(az + 4f, ez - 4f), Deck, R(6f, 11f));
                        Box("path", cx - 1.5f, cx + 1.5f, Deck, Deck + 0.03f, az, ez, "lane_concrete");
                        Box("path", ax, ex, Deck, Deck + 0.03f, cz - 1.5f, cz + 1.5f, "lane_concrete");
                    }
                    else
                    {
                        Box("pad", ax, ex, Deck, Deck + 0.02f, az, ez, "road_asphalt");
                        for (float z = az + 10f; z < ez - 8f; z += 22f) Prefab("building", Rng.NextDouble() < 0.5 ? "bld_ind_warehouse" : "bld_warehouse_blue", new Vector3(cx, Deck + 0.02f, z), 0f, Vector3.one);
                        for (float x = ax + 4f; x < ex - 4f; x += 5f) if (Rng.NextDouble() < 0.06) ParkedVehicle( CarKey(0.05f), new Vector3(x, Deck + 0.02f, az + 3.5f), 90f);
                    }
                }
        }

        /// <summary>Apartment plot (Korean 아파트 단지 feel): one of the CC BY blocks sized to the plot — a 12-storey complex,
        /// two 9-storey slabs, a 110 m point tower with a small podium, or a scaled officetel — with trees along the edges.</summary>
        public static void ApartmentPlot(float ax, float ex, float az, float ez)
        {
            float cx = (ax + ex) / 2f, cz = (az + ez) / 2f, wx = ex - ax, wz = ez - az;
            double u = Rng.NextDouble();
            if (u < 0.35 && wx > 60f && wz > 60f) Prefab("building", "bld_apt_complex", new Vector3(cx, Deck + 0.02f, cz), Rng.NextDouble() < 0.5 ? 0f : 90f, Vector3.one * Mathf.Min(1f, Mathf.Min(wx, wz) / 90f));
            else if (u < 0.65)
                foreach (var ox in new[] { -0.25f, 0.25f })
                    Prefab("building", "bld_apt_slab", new Vector3(cx + ox * wx, Deck + 0.02f, cz + R(-0.08f, 0.08f) * wz), 0f, Vector3.one * R(0.9f, 1.1f));
            else if (u < 0.85) Prefab("building", "bld_hirise", new Vector3(cx + R(-0.1f, 0.1f) * wx, Deck + 0.02f, cz + R(-0.1f, 0.1f) * wz), R(0f, 90f), Vector3.one * R(0.8f, 1.0f));
            else Prefab("building", "bld_officetel", new Vector3(cx, Deck + 0.02f, cz), Rng.NextDouble() < 0.5 ? 0f : 180f, Vector3.one * 0.6f);
        }

        /// <summary>Street-level shop row along x at depth z: Asian shop-pack rows mixed with the Korean bakery and small
        /// multi-family buildings (CC BY models), fronts facing -z when yaw = 180.</summary>
        public static void ShopRow(float x0, float x1, float z, float yaw)
        {
            for (float x = x0 + 14f; x < x1 - 10f;)
            {
                double u = Rng.NextDouble();
                if (u < 0.5) { Prefab("building", "bld_shops", new Vector3(x + 17f, Deck + 0.02f, z), yaw, Vector3.one); x += 38f; }
                else if (u < 0.75) { Prefab("building", "bld_bakery_kr", new Vector3(x + 4f, Deck + 0.02f, z + (yaw > 90f ? 1f : -1f) * 1.5f), yaw + R(-3f, 3f), Vector3.one); x += 10f; }
                else { Prefab("building", "bld_villa_kr", new Vector3(x + 12f, Deck + 0.02f, z + (yaw > 90f ? 1f : -1f) * 7f), yaw, Vector3.one * R(0.75f, 0.95f)); x += 27f; }
            }
        }

        /// <summary>Row of vinyl greenhouses (half-round tunnels, prefab 7 m x 40 m, long axis z) starting at x0, 1.5 m apart.</summary>
        public static void Greenhouses(float x0, float z0, int count, float len)
        {
            for (int i = 0; i < count; i++)
                Prefab("greenhouse", "greenhouse_vinyl", new Vector3(x0 + i * 8.5f + 3.5f, Deck, z0 + len / 2f), 0f, new Vector3(1f, 1f, len / 40f));
        }

        /// <summary>Scattered scrub / small trees over open flat land so it does not read as a lawn.</summary>
        public static void Scrub(float x0, float x1, float z0, float z1, int count, float hMin = 3f, float hMax = 6f)
        {
            for (int i = 0; i < count; i++) Tree(R(x0, x1), R(z0, z1), Deck - 0.2f, R(hMin, hMax));
        }

        public static WaterRegion Region(string id, params float[] xz)
        {
            var r = new WaterRegion { id = id };
            for (int i = 0; i < xz.Length; i += 2) r.polygon_xz.Add(new Vector2(xz[i], xz[i + 1]));
            return r;
        }

        public static VesselRule Rule(string asset, string note, params string[] regions)
            => new VesselRule { asset_id = asset, note = note, regions = new List<string>(regions) };
    }
}
