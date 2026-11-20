using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static PortCCTV.HarborGeneric02;

namespace PortCCTV
{
    /// <summary>Parametric parts shared by the map builders (uses the element list started by HarborGeneric02.Begin).</summary>
    internal static partial class HarborParts
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
            Box("breakwater", x0, x1, 1.6f, crest + 0.03f, seaIsMinusZ ? z0 + 2.4f : z0, seaIsMinusZ ? z1 : z1 - 2.4f, "concrete", id: id + "_road");
            Box("breakwater", x0, x1, 1.6f, crest + 2.5f, wall0, wall1, "breakwater", id: id + "_crownwall");
            float sea = seaIsMinusZ ? z0 : z1, port = seaIsMinusZ ? z1 : z0, s = seaIsMinusZ ? -1f : 1f;
            TetrapodBank(x0, x1, (along, off, y) => Tetrapod(along, sea + s * off, y));
            for (float x = x0 + 1.5f; x < x1 - 1f; x += 3.2f)
                Rock(x + R(-0.6f, 0.6f), port - s * R(0.4f, 1.4f), R(-0.6f, 0.4f), R(2.0f, 3.0f));
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
            TetrapodBank(z0, z1, (along, off, y) => Tetrapod(sea + s * off, along, y));
            for (float z = z0 + 1.5f; z < z1 - 1f; z += 3.2f)
                Rock(port - s * R(0.4f, 1.4f), z + R(-0.6f, 0.6f), R(-0.6f, 0.4f), R(2.0f, 3.0f));
            for (float z = z0 + 32f; z < z1 - 24f; z += 55f)
                Prefab("breakwater_light", "street_lamp", new Vector3(port + s * 3f, crest, z), seaIsMinusX ? 90f : 270f, Vector3.one * 1.4f);
        }

        /// <summary>Densely packed tetrapod armour on the seaward face (user 2026-11-09: "테트라포드도 빽빽하게"): four staggered rows
        /// stepping down the slope away from the crown wall (offset from the sea face, base height) every 2.4 m, plus a second
        /// layer stacked on the two upper rows, so the mound reads as interlocked units rather than a sparse line.
        /// place(along, offsetFromSeaFace, baseY).</summary>
        static void TetrapodBank(float a0, float a1, System.Action<float, float, float> place)
        {
            var rows = new (float off0, float off1, float y0, float y1)[]
            {
                (0.5f, 1.4f, 0.2f, 1.2f), (2.3f, 3.2f, -0.4f, 0.6f), (4.1f, 5.0f, -1.0f, 0.0f), (5.9f, 6.8f, -1.7f, -0.8f),
            };
            int station = 0;
            for (float a = a0 + 1.2f; a < a1 - 0.8f; a += 2.4f, station++)
            {
                for (int r = 0; r < rows.Length; r++)
                {
                    float stagger = (r % 2 == 1) ? 1.2f : 0f;
                    float along = a + stagger + R(-0.4f, 0.4f);
                    if (along > a1 - 0.8f) continue;
                    var row = rows[r];
                    place(along, R(row.off0, row.off1), R(row.y0, row.y1));
                }
                // second layer over the upper rows (every station, alternating between the two)
                place(a + 0.6f + R(-0.4f, 0.4f), station % 2 == 0 ? R(1.0f, 1.9f) : R(2.6f, 3.4f), R(1.3f, 2.1f));
            }
        }

        /// <summary>Concrete tetrapod armour unit (~2.5 m), random orientation.</summary>
        public static void Tetrapod(float x, float z, float baseY)
            => Prefab("tetrapod", "tetrapod", new Vector3(x, baseY, z), R(0, 360), Vector3.one * R(0.9f, 1.15f), new Vector3(R(-25, 25), 0, R(-25, 25)));

        /// <summary>Korean breakwater lighthouse (2026-11-09: the cylinder + box placeholder replaced by the scanned Manabashima
        /// tower, prefabs breakwater_light_red / _white, ReplacementAssets): a thin concrete apron on the crown road (kept as
        /// `{id}_base` so earlier ids and the access audit stay valid), the tower on its plinth with steps up to the door, the door
        /// turned to `doorYaw` (0 = +z) i.e. towards the breakwater road it is reached by. Colour after the IALA side of the
        /// entrance (red = port hand entering, white = starboard). `height` (m, tower + plinth) sets a uniform scale of the 10.6 m
        /// prefab, clamped to 0.85..1.25 so the scan's proportions stay.</summary>
        public static void Lighthouse(string id, float x, float z, float baseY, string colour, float height = 13f, float doorYaw = 0f)
        {
            float k = Mathf.Clamp(height / 10.6f, 0.85f, 1.25f);
            Cyl("lighthouse", x, z, baseY, baseY + 0.04f, 7.6f * k, "concrete", id + "_base");
            Prefab("lighthouse", colour == "beacon_red" ? "breakwater_light_red" : "breakwater_light_white", new Vector3(x, baseY + 0.04f, z), doorYaw, Vector3.one * k, id: id + "_tower");
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
            var xs = new float[nx + 1]; var zs = new float[nz + 1];
            for (int i = 0; i <= nx; i++) xs[i] = Boulevard(z0 - 4f, z1 + 4f, x0 + i * bx, false, 4, false);
            for (int k = 0; k <= nz; k++) zs[k] = Boulevard(x0 - 4f, x1 + 4f, z0 + k * bz, true, 4, false);
            for (int i = 0; i <= nx; i++) for (int k = 0; k <= nz; k++) SignalledCrossing(new Vector2(xs[i], zs[k]), true, 14f, 14f, true, (i == 0 || i == nx || k == 0 || k == nz) && (i + k) % 2 == 0);
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
                            if (Rng.NextDouble() < 0.65) ContainerTruck(x, ez - 32f * s - 10f, 90f, Rng.NextDouble() < 0.6, Rng.NextDouble() < 0.5);
                        ServiceRow(ax + 4f, ex - 4f, az + 0.6f, 1f, 0.35f, "car");                               // staff bays on the yard edge
                        ServiceRow(ax + 6f, ex - 10f, ez - 32f * s - 19f, -1f, 0.3f, "van");                       // delivery vans at the doors
                        RegisterYardAccess(ax, ex, az, ez, Uid("yard"));
                    }
                    else if (u < 0.40) { if (TruckLot(ax, ex, az, ez, R(0.2f, 0.4f), R(0.3f, 0.7f)) == null) Box("pad", ax, ex, Deck, Deck + 0.02f, az, ez, "road_asphalt"); }
                    else if (u < 0.68)
                    {
                        Box("pad", ax, ex, Deck, Deck + 0.02f, az, ez, "concrete");
                        int bays = Mathf.Max(1, (int)((wx - 12f) / 12.6f)), rows = Mathf.Clamp((int)((wz - 30f) / 2.75f / 2f), 2, 8);
                        ContainerBlock(ax + 6f, az + 6f, bays, rows, 5, false);
                        ContainerBlock(ax + 6f, ez - 6f - rows * 2.75f, bays, rows, 5, false);
                        RegisterYardAccess(ax, ex, az, ez, Uid("depot"), 12f);
                        // concrete barriers along the street side of the depot, a 16 m gate in the middle (Poly Haven CC0)
                        for (float x = ax + 2f; x < ex - 2f; x += 1.65f)
                            if (Mathf.Abs(x - cx) > 8f) Prefab("barrier", "concrete_barrier", new Vector3(x, Deck + 0.02f, az + 1.2f), 0f, Vector3.one);
                        Prefab("crane", "mobile_crane", new Vector3(cx, Deck + 0.02f, cz), R(0f, 360f), Vector3.one);
                    }
                    else if (u < 0.84)
                    {
                        Box("pad", ax, ex, Deck, Deck + 0.02f, az, ez, "road_asphalt");
                        for (float z = az + 10f; z < ez - 22f; z += 18f)
                            for (float x = ax + 16f; x < ex - 12f; x += 32f) Prefab("building", Rng.NextDouble() < 0.3 ? "bld_ind_warehouse" : "bld_warehouse_blue", new Vector3(x, Deck + 0.02f, z), 0f, Vector3.one);
                        Prefab("building", "bld_brick_factory", new Vector3(ax + 12f, Deck + 0.02f, ez - 12f), 0f, Vector3.one);
                        Tank(ex - 12f, ez - 12f, R(8f, 14f), R(8f, 14f));
                        ServiceRow(ax + 4f, ex - 30f, ez - 0.6f, -1f, 0.3f, "car");
                        RegisterYardAccess(ax, ex, az, ez, Uid("yard"));
                    }
                    else
                    {
                        Box("pad", ax, ex, Deck, Deck + 0.02f, az, ez, "concrete");
                        for (float z = az + 6f; z < ez - 6f; z += 9f)                           // steel stock: plate stacks and pipe bundles
                            for (float x = ax + 6f; x < ex - 14f; x += 16f)
                                if (Rng.NextDouble() < 0.9)
                                    BoxC("laydown", new Vector3(x + 6f, Deck + 0.6f + R(0f, 0.6f), z), new Vector3(12f, R(1.0f, 2.4f), R(2.2f, 4f)), Rng.NextDouble() < 0.5 ? "steel_dark" : "steel_galv", R(-2f, 2f));
                        Prefab("crane", "mobile_crane", new Vector3(ex - 10f, Deck + 0.02f, cz), R(0f, 360f), Vector3.one);
                        RegisterYardAccess(ax, ex, az, ez, Uid("stockyard"), 12f);
                    }
                }
        }

        /// <summary>Farmland on flat land: field plots (alternating crops), farm tracks, rows of vinyl greenhouses, small orchards
        /// (tree rows) and farm sheds.</summary>
        public static void Farmland(float x0, float x1, float z0, float z1)
        {
            float zLast = z0;
            for (float z = z0; z < z1 - 20f; z += 70f) { Box("lane", x0, x1, Deck, Deck + 0.04f, z, z + 4f, "lane_concrete"); zLast = z; }   // farm tracks
            // access track along the west edge joining the farm tracks (2026-11-09: the E-W tracks had no way out; RoadNetwork
            // joins its ends to the nearest road)
            if (zLast > z0) Box("lane", x0 - 4f, x0, Deck, Deck + 0.04f, z0, zLast + 4f, "lane_concrete");
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
            for (int c = 0; c < clusters * 2; c++)                                   // 2026-11-09: twice the clusters
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

        /// <summary>Beach furniture in loose clusters, with clear access corridors and room at the waterline.</summary>
        public static void BeachFurniture(float x0, float x1, float z0, float z1, float y)
        {
            string[] cols = { "white", "white", "blue", "navy", "green", "red" };
            for (float z = z0 + 3f; z <= z1; z += 10f)
                for (float x = x0 + R(0f, 5f); x < x1; x += R(7f, 10f))
                {
                    // Broad pedestrian routes connect the promenade to the surf.
                    if (Mathf.Abs(x - (x0 + 110f)) < 8f || Mathf.Abs(x - (x0 + 300f)) < 8f ||
                        Mathf.Abs(x - (x0 + 480f)) < 8f || Rng.NextDouble() < 0.24) continue;
                    Prefab("beach", "beach_parasol_" + cols[Rng.Next(cols.Length)],
                        new Vector3(x + R(-0.8f, 0.8f), y, z + R(-1.2f, 1.2f)), R(-18f, 18f), Vector3.one);
                }
            for (float x = x0 + 60f; x < x1; x += 180f)
                Prefab("beach", "lifeguard_tower", new Vector3(x, y, z0 + 3f), 180f, Vector3.one);
        }

        /// <summary>Promenade furniture along x: benches facing the sea (seaDir = -1 -> sea at -z) and planted trees.</summary>
        public static void PromenadeFurniture(float x0, float x1, float z, float seaDir)
        {
            for (float x = x0 + 5f; x < x1; x += 14f)
            {
                Prefab("bench", "bench", new Vector3(x, Deck, z), seaDir < 0 ? 0f : 180f, Vector3.one);
                Tree(x + 7f, z - seaDir * 2.5f, Deck, R(5f, 7f));
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
            CoastalRoads.Add((x0, x1, roadZ, seaDir));
            // W-beam guardrail on the sea side ("GuardRail and Terminal", xplanepilot, CC BY): 4 m segments, end terminals at
            // both ends of every run (the run is split at the gap, e.g. a bridge junction)
            void Run(float r0, float r1)
            {
                if (r1 - r0 < 8f) return;
                int n = Mathf.FloorToInt((r1 - r0) / 4f);
                float s0 = (r0 + r1) / 2f - n * 2f;
                float yaw = seaDir < 0 ? 180f : 0f;                                    // rail face towards the road
                for (int k = 0; k < n; k++) Prefab("guard_rail", "guardrail_seg", new Vector3(s0 + k * 4f + 2f, Deck, gz), yaw, Vector3.one);
                Prefab("guard_rail", "guardrail_end", new Vector3(s0 - 0.3f, Deck, gz), yaw + 180f, Vector3.one);
                Prefab("guard_rail", "guardrail_end", new Vector3(s0 + n * 4f + 0.3f, Deck, gz), yaw, Vector3.one);
            }
            if (!gap) Run(x0, x1); else { Run(x0, gapX0); Run(gapX1, x1); }
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
            cross.Add(x0 + 6f);                                                    // end lanes close the grid at both village ends
            for (float x = x0 + R(26f, 50f); x < x1 - 30f; x += R(55f, 85f)) cross.Add(x);
            cross.Add(x1 - 6f);
            float rowPitch = 22f, firstRow = roadZ + land * 12f;
            var occupied = new List<(int row, float a, float b)>();                 // frontage taken per row (for the infill pass)
            for (int r = 0; r < rows; r++)
            {
                float rz = firstRow + land * r * rowPitch;
                float laneZ = rz + land * rowPitch / 2f;
                if (r < rows - 1) Box("lane", x0 + 4f, x1 - 4f, Deck + 0.02f, Deck + 0.05f, laneZ - 1.75f, laneZ + 1.75f, "lane_concrete");
                else Box("lane", x0 + 4f, x1 - 4f, Deck + 0.02f, Deck + 0.05f, rz + land * (rowPitch / 2f) - 1.75f, rz + land * (rowPitch / 2f) + 1.75f, "lane_concrete");   // back lane behind the last row
                for (float x = x0 + 9f; x < x1 - 9f; x += R(12f, 15f))
                {
                    bool nearCross = false; foreach (var cx in cross) if (Mathf.Abs(cx - x) < 13f || Mathf.Abs(cx - (x + 6f)) < 15f) nearCross = true;
                    if (nearCross || Rng.NextDouble() > 0.95) { if (Rng.NextDouble() < 0.8) Tree(x, rz + land * R(-4f, 4f), Deck, R(5f, 9f)); continue; }
                    float yaw = (land > 0 ? 0f : 180f) + R(-5f, 5f);              // front (-z) faces the road
                    if (Rng.NextDouble() < 0.3)
                    {
                        // 2-3 storey Korean multi-family building (CC BY scan-based model) at village scale
                        float vs = R(0.5f, 0.65f), vz = rz;
                        // front row: the 25 m-deep model at 0.5-0.65 reached 4 m from the road centre, over the lamp / pole line
                        // and the kerb (2026-11-10 photo). Front-row villas are kept at 0.42 and set 1.1 m back (pad front 6.35 m
                        // from the centre line, back 2.9 m clear of the lane behind).
                        if (r == 0) { vs = Mathf.Min(vs, 0.42f); vz = rz + land * 1.1f; }
                        BoxC("pad", new Vector3(x + 6f, Deck + 0.025f, vz), new Vector3(27f * vs + 3f, 0.05f, 25f * vs + 3f), "lane_concrete", yaw);
                        Prefab("house", "bld_villa_kr", new Vector3(x + 6f, Deck + 0.05f, vz), yaw, Vector3.one * vs);
                        occupied.Add((r, x + 6f - 13.5f * vs - 1.5f, x + 6f + 13.5f * vs + 1.5f));
                        x += 8f;
                        continue;
                    }
                    int kind = Rng.NextDouble() < 0.12 ? 5 : Rng.Next(5);
                    var k = kinds[kind]; float sc = R(0.92f, 1.08f);
                    BoxC("pad", new Vector3(x, Deck + 0.025f, rz), new Vector3(k.w * sc + 3f, 0.05f, k.d * sc + 3f), "lane_concrete", yaw);
                    Prefab("house", k.key, new Vector3(x, Deck + 0.05f, rz), yaw, Vector3.one * sc);
                    occupied.Add((r, x - k.w * sc / 2f - 1.5f, x + k.w * sc / 2f + 1.5f));
                }
            }
            VillageInfill(x0, x1, roadZ, land, rows, rowPitch, firstRow, cross, occupied);
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
                    else for (int t = 0; t < 14; t++) Tree(x + R(0, w), bz + land * R(2f, d), Deck, R(5f, 9f));
                }
            }
        }

        /// <summary>
        /// Village infill (2026-11-10, user: villages too sparse): every free stretch of a house row at least 11 m long and
        /// clear of the cross lanes (2 m from the lane edge) gets houses -- the Korean-style house kinds, the Japanese-style house
        /// (rows behind the first only: it is 11.5 m deep) and gear sheds -- with a paved plot, front towards the road; own
        /// seeded RNG (the shared random stream of the map is untouched). Then a stop sign at each cross-lane mouth on the coast
        /// road and a 30 km/h sign at both village ends.
        /// </summary>
        static void VillageInfill(float x0, float x1, float roadZ, float land, int rows, float rowPitch, float firstRow, List<float> cross, List<(int row, float a, float b)> occupied)
        {
            var rr = new System.Random(911 * Mathf.RoundToInt(x0) + 37 * Mathf.RoundToInt(roadZ) + 5);
            float RRf(float a, float b) => a + (float)rr.NextDouble() * (b - a);
            var kinds = HillFeatures.HouseKinds;
            for (int r = 0; r < rows; r++)
            {
                float rz = firstRow + land * r * rowPitch;
                var blocked = new List<(float a, float b)>();
                foreach (var o in occupied) if (o.row == r) blocked.Add((o.a, o.b));
                foreach (var cx in cross) blocked.Add((cx - 2f - 2f, cx + 2f + 2f));
                blocked.Add((float.MinValue, x0 + 8f)); blocked.Add((x1 - 8f, float.MaxValue));
                blocked.Sort((p, q) => p.a.CompareTo(q.a));
                float cur = float.MinValue;
                var gaps = new List<(float a, float b)>();
                foreach (var (a, b) in blocked) { if (a > cur && cur > float.MinValue) gaps.Add((cur, a)); cur = Mathf.Max(cur, b); }
                foreach (var (ga, gb) in gaps)
                {
                    for (float x = ga + 0.5f; ;)
                    {
                        bool jp = r > 0 && rr.NextDouble() < 0.3;
                        int kind = rr.NextDouble() < 0.15 ? 5 : rr.Next(5);
                        float w = jp ? 11.84f * 0.8f : kinds[kind].w, d = jp ? 11.48f * 0.8f : kinds[kind].d;
                        float sc = jp ? 0.8f : RRf(0.92f, 1.05f);
                        float fw = jp ? w : w * sc;
                        if (x + fw + 3f > gb) break;
                        float cxh = x + 1.5f + fw / 2f, yaw = (land > 0 ? 0f : 180f) + RRf(-4f, 4f);
                        float dz = jp ? d : d * sc;
                        if (r == 0 && dz / 2f + 1.5f > 12f - 6.2f) { x += 2f; continue; }   // first row: plot front must stay behind the pole line
                        BoxC("pad", new Vector3(cxh, Deck + 0.025f, rz), new Vector3(fw + 3f, 0.05f, dz + 3f), "lane_concrete", yaw);
                        float hx = fw / 2f + 2f, hz = dz / 2f + 2f;                              // trees left in the old empty slot
                        E.RemoveAll(e => e.kind == "tree" && Mathf.Abs(e.center.x - cxh) < hx && Mathf.Abs(e.center.z - rz) < hz);
                        if (jp) Prefab("house", "house_jp03", new Vector3(cxh, Deck + 0.05f, rz), yaw + 180f, Vector3.one * sc);
                        else Prefab("house", kinds[kind].key, new Vector3(cxh, Deck + 0.05f, rz), yaw, Vector3.one * sc);
                        x += fw + 3f + RRf(0.5f, 3f);
                    }
                }
            }
            // stop sign at each cross-lane mouth (lane traffic joining the coast road), 30 km/h at both village ends
            foreach (var cx in cross)
            {
                var p = new Vector3(cx + 2.9f, Deck, roadZ + land * 5.2f);
                if (!E.Exists(e => (e.kind == "power_pole" || e.kind == "street_light") && (new Vector2(e.center.x - p.x, e.center.z - p.z)).sqrMagnitude < 1.5f * 1.5f))
                    KrSign("kr_sign_stop", p, new Vector2(0f, land));
            }
            KrSign("kr_sign_sl30", new Vector3(x0 + 3f, Deck, roadZ - land * 4.8f), new Vector2(-1f, 0f));
            KrSign("kr_sign_sl30", new Vector3(x1 - 3f, Deck, roadZ + land * 4.8f), new Vector2(1f, 0f));
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
            if (key == "forklift_truck") return 90f;                 // ReplacementAssets: forks along +z
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
        /// <summary>Share of 9-14 m lane slots holding a car on boulevards (user 2026-11-09: denser, more lived-in maps).</summary>
        public static float TrafficDensity = 0.22f;

        /// <summary>Boulevards already laid in the current map build: (alongX, centre, a0, a1, half width incl. sidewalks).
        /// Neighbouring city / industrial grids share their edge streets, and grids are laid next to arterial roads; without
        /// this register those streets were built twice on top of each other (or 15 m apart with overlapping sidewalks).</summary>
        internal static readonly List<(bool alongX, float c, float a0, float a1, float half)> LaidBoulevards = new List<(bool, float, float, float, float)>();
        internal static readonly List<Vector2> LaidCrossings = new List<Vector2>();

        /// <summary>Lays a boulevard, skipping the parts already covered by a parallel boulevard closer than both half-widths + 3 m.
        /// Returns the centre line actually serving this alignment (an existing road's when this one is fully covered), so grid
        /// builders can put their junctions and bus stops on the road that is really there.</summary>
        public static float Boulevard(float a0, float a1, float c, bool alongX, int lanes = 4, bool trees = true, float signalsEvery = 0f)
        {
            float half = (lanes >= 4 ? 7f : 4f) + 3f;
            var covered = new List<(float s, float e)>(); float snap = c;
            foreach (var b in LaidBoulevards)
            {
                if (b.alongX != alongX || Mathf.Abs(b.c - c) >= b.half + half + 3f) continue;
                float s0 = Mathf.Max(a0, b.a0), s1 = Mathf.Min(a1, b.a1);
                if (s1 > s0) { covered.Add((s0, s1)); snap = b.c; }
            }
            if (covered.Count == 0) { LaidBoulevards.Add((alongX, c, a0, a1, half)); BoulevardRaw(a0, a1, c, alongX, lanes, trees, signalsEvery); return c; }
            covered.Sort((p, q) => p.s.CompareTo(q.s));
            float cur = a0; bool any = false;
            foreach (var (s0, s1) in covered)
            {
                if (s0 - cur >= 30f) { LaidBoulevards.Add((alongX, c, cur, s0, half)); BoulevardRaw(cur, s0, c, alongX, lanes, trees, signalsEvery); any = true; }
                cur = Mathf.Max(cur, s1);
            }
            if (a1 - cur >= 30f) { LaidBoulevards.Add((alongX, c, cur, a1, half)); BoulevardRaw(cur, a1, c, alongX, lanes, trees, signalsEvery); any = true; }
            return any ? c : snap;
        }

        static void BoulevardRaw(float a0, float a1, float c, bool alongX, int lanes, bool trees, float signalsEvery)
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
                B("sidewalk", a0, a1, Deck, Deck + (alongX ? 0.16f : 0.17f), s * hw, s * (hw + sw), "sidewalk_paving");   // 1 cm apart where sidewalks cross (no z-fight)
                B("kerb", a0, a1, Deck, Deck + 0.17f, s * hw, s * (hw + 0.25f), "concrete");
            }
            for (float u = a0 + 10f; u < a1 - 5f; u += 30f)
                foreach (var s in new[] { -1f, 1f })
                {
                    var p = alongX ? new Vector3(u, Deck + 0.16f, c + s * (hw + 0.8f)) : new Vector3(c + s * (hw + 0.8f), Deck + 0.16f, u);
                    Prefab("street_light", "street_lamp", p, alongX ? 0f : 90f, Vector3.one * 2f);
                }
            // road traffic: cars standing in the lanes (right-hand traffic), clear of the 25 m at each end (junctions)
            {
                float[] lanesC = lanes >= 4 ? new[] { -5.25f, -1.75f, 1.75f, 5.25f } : new[] { -2f, 2f };
                foreach (var lc in lanesC)
                    for (float u = a0 + 25f; u < a1 - 25f; u += R(9f, 14f))
                    {
                        if (Rng.NextDouble() > TrafficDensity) continue;
                        float travel = lc < 0 ? 0f : 180f;                              // -side lanes run +u, +side lanes run -u
                        var p = alongX ? new Vector3(u, Deck + 0.06f, c + lc) : new Vector3(c + lc, Deck + 0.06f, u);
                        Vehicle("car", CarKey(0.08f), p, (alongX ? 0f : -90f) + travel + R(-1.5f, 1.5f));
                    }
            }
            if (trees)
                for (float u = a0 + 12f; u < a1 - 6f; u += 10f)                                               // one tree per 10 m per side
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
        public static void SignalledCrossing(Vector2 centre, bool mainAlongX, float wMain, float wCross, bool full, bool signals = true)
        {
            foreach (var p in LaidCrossings) if ((p - centre).sqrMagnitude < 12f * 12f) return;     // a junction is laid once
            LaidCrossings.Add(centre);
            Junctions.Add(new Junction { c = centre, alongX = mainAlongX, wMain = wMain, wCross = wCross, full = full, signals = signals });
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
            // signals: vehicle signal on each far corner, facing the approach (prefab faces -z at yaw 0; mast arm over the road).
            // Junctions of local streets inside a grid have stop lines and crosswalks but no signals (2026-11-09).
            if (!signals) return;
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
            for (float x = x0 + 5f; x < x1 - 3f; x += R(5f, 8f)) Tree(x, z0 + d * R(0.2f, 0.8f), Deck, R(7f, 12f));
            Scrub(x0, x1, z0 + 2f, z1 - 2f, (int)((x1 - x0) * d / 150f), 2f, 4f);
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
        /// <summary>
        /// Grid of city blocks. zone (detail review 2026-11-09 §5, references R02 Haeundae / R01 Wando):
        ///  "mixed"       apartments, shop blocks with a customers' car park, parks, a few warehouses (the old behaviour);
        ///  "highrise"    point towers / officetels, shop rows at street level, pocket parks; few car parks (towers park underground);
        ///  "residential" apartment complexes and slabs, blocks of 3-4 storey villas (빌라) with alleys, neighbourhood shops, parks.
        /// </summary>
        public static void CityBlocks(float x0, float x1, float z0, float z1, float block = 115f, float roadW = 12f, string zone = "mixed")
        {
            int nx = Mathf.Max(1, (int)((x1 - x0) / block)), nz = Mathf.Max(1, (int)((z1 - z0) / block));
            float bx = (x1 - x0) / nx, bz = (z1 - z0) / nz;
            // streets: 2-lane boulevards with sidewalks, lamps and street trees; every intersection signalised
            var xs = new float[nx + 1]; var zs = new float[nz + 1];
            for (int i = 0; i <= nx; i++) xs[i] = Boulevard(z0 - 4f, z1 + 4f, x0 + i * bx, false, 2, true);
            for (int k = 0; k <= nz; k++) zs[k] = Boulevard(x0 - 4f, x1 + 4f, z0 + k * bz, true, 2, true);
            for (int i = 0; i <= nx; i++) for (int k = 0; k <= nz; k++) SignalledCrossing(new Vector2(xs[i], zs[k]), true, 8f, 8f, true, i == 0 || i == nx || k == 0 || k == nz);
            for (int i = 0; i < nx; i++) if (Rng.NextDouble() < 0.8 && Mathf.Abs(zs[0] - z0) < 0.5f) BusStop(x0 + i * bx + bx / 2f, z0 + 4f + 1.5f, 1f);
            float m = 4f + 3f + 3f;
            for (int i = 0; i < nx; i++)
                for (int k = 0; k < nz; k++)
                {
                    float ax = x0 + i * bx + m, ex = x0 + (i + 1) * bx - m, az = z0 + k * bz + m, ez = z0 + (k + 1) * bz - m;
                    float cx = (ax + ex) / 2f, cz = (az + ez) / 2f, wx = ex - ax, wz = ez - az;
                    double u = Rng.NextDouble();
                    if (zone == "highrise" || zone == "residential")
                    {
                        CityPlotZoned(zone, ax, ex, az, ez, u);
                        continue;
                    }
                    if (u < 0.50)
                    {
                        Box("pad", ax, ex, Deck, Deck + 0.02f, az, ez, "lane_concrete");
                        ApartmentPlot(ax, ex, az + 7f, ez);                        // 7 m residents' parking strip on the -z side
                        for (float x = ax + 3f; x < ex - 3f; x += 7f) Tree(x, az + 3.5f, Deck, R(5f, 8f));   // front planting (residents park inside the complex)
                    }
                    else if (u < 0.54) GreenBuffer(ax, ex, az, ez);
                    else if (u < 0.78)
                    {
                        Box("pad", ax, ex, Deck, Deck + 0.02f, az, az + 18f, "lane_concrete");
                        if (ez - az > 70f) Box("pad", ax, ex, Deck, Deck + 0.02f, ez - 22f, ez, "lane_concrete");
                        ShopRow(ax + 4f, ex - 4f, az + 6f, 180f);
                        bool backRow = ez - az > 70f;
                        if (backRow) ShopRow(ax + 4f, ex - 4f, ez - 6f, 0f);
                        // customers' car park filling the plot between the shop rows (2026-11-09: was an empty forecourt)
                        float pz1 = backRow ? ez - 24f : ez - 3f;
                        if (pz1 - (az + 20f) < 12.5f || ParkingLot(ax + 3f, ex - 3f, az + 20f, pz1, R(0.3f, 0.6f), null, 0.04f) == null)
                            Box("shop_forecourt", ax + 2f, ex - 2f, Deck, Deck + 0.03f, az + 16f, Mathf.Min(ez - 2f, az + 52f), "lane_concrete");
                    }
                    else if (u < 0.90)
                    {
                        for (int t = 0; t < 34; t++) Tree(R(ax + 4f, ex - 4f), R(az + 4f, ez - 4f), Deck, R(6f, 11f));
                        Box("path", cx - 1.5f, cx + 1.5f, Deck, Deck + 0.03f, az - 3.2f, ez + 3.2f, "lane_concrete");
                        Box("path", ax - 3.2f, ex + 3.2f, Deck, Deck + 0.03f, cz - 1.5f, cz + 1.5f, "lane_concrete");
                    }
                    else
                    {
                        Box("pad", ax, ex, Deck, Deck + 0.02f, az, ez, "road_asphalt");
                        for (float z = az + 10f; z < ez - 8f; z += 22f) Prefab("building", Rng.NextDouble() < 0.3 ? "bld_ind_warehouse" : "bld_warehouse_blue", new Vector3(cx, Deck + 0.02f, z), 0f, Vector3.one);
                        ServiceRow(ax + 4f, ex - 4f, az + 0.6f, 1f, 0.3f, "car");
                        RegisterYardAccess(ax, ex, az, ez, Uid("yard"));
                    }
                }
        }

        /// <summary>Apartment plot (Korean 아파트 단지): a grid of one building type filling the plot — 12-storey complexes
        /// (26 x 60 m), 9-storey slabs (18 x 43 m), 110 m point towers (38 x 38 m, scaled 0.8) or officetels (33 x 26 m) — with
        /// 8-10 m between buildings, trees in the gaps and along the plot edge (2026-11-09: was one building per plot).
        /// Footprints are the wrapped prefab bounds (theme notes), yaw 0.</summary>
        static void CityPlotZoned(string zone, float ax, float ex, float az, float ez, double u)
        {
            float cx = (ax + ex) / 2f, cz = (az + ez) / 2f;
            void Park()
            {
                for (int t = 0; t < 30; t++) Tree(R(ax + 4f, ex - 4f), R(az + 4f, ez - 4f), Deck, R(6f, 11f));
                Box("path", cx - 1.5f, cx + 1.5f, Deck, Deck + 0.03f, az - 3.2f, ez + 3.2f, "lane_concrete");
                Box("path", ax - 3.2f, ex + 3.2f, Deck, Deck + 0.03f, cz - 1.5f, cz + 1.5f, "lane_concrete");
            }
            if (zone == "highrise")
            {
                if (u < 0.62) { Box("pad", ax, ex, Deck, Deck + 0.02f, az, ez, "lane_concrete"); ApartmentPlot(ax, ex, az + 4f, ez, "tower"); for (float x = ax + 3f; x < ex - 3f; x += 7f) Tree(x, az + 2f, Deck, R(5f, 8f)); }
                else if (u < 0.84)
                {
                    // street-level shops on the front, towers behind; a small surface lot only on some blocks
                    Box("pad", ax, ex, Deck, Deck + 0.02f, az, ez, "lane_concrete");
                    ShopRow(ax + 4f, ex - 4f, az + 6f, 180f);
                    if (ez - az > 75f) ApartmentPlot(ax, ex, az + 24f, ez, "tower");
                    else if (Rng.NextDouble() < 0.5 && ParkingLot(ax + 3f, ex - 3f, az + 20f, ez - 3f, R(0.4f, 0.7f), null, 0.03f) != null) { }
                    else for (int t = 0; t < 10; t++) Tree(R(ax + 4f, ex - 4f), R(az + 22f, ez - 4f), Deck, R(5f, 9f));
                }
                else Park();
                return;
            }
            // residential
            if (u < 0.48) { Box("pad", ax, ex, Deck, Deck + 0.02f, az, ez, "lane_concrete"); ApartmentPlot(ax, ex, az + 7f, ez, "low"); for (float x = ax + 3f; x < ex - 3f; x += 7f) Tree(x, az + 3.5f, Deck, R(5f, 8f)); }
            else if (u < 0.80) VillaBlock(ax, ex, az, ez);
            else if (u < 0.90)
            {
                // neighbourhood shops facing the street, villas behind (no customers' car park: walk-in trade)
                Box("pad", ax, ex, Deck, Deck + 0.02f, az, az + 18f, "lane_concrete");
                ShopRow(ax + 4f, ex - 4f, az + 6f, 180f);
                VillaBlock(ax, ex, az + 20f, ez);
            }
            else Park();
        }

        /// <summary>Block of 3-4 storey multi-family villas (빌라, CC BY scan model scaled 0.55-0.7, ~16 x 15 m) in rows with
        /// 6 m alleys; alleys open onto the surrounding streets and residents park in the alleys.</summary>
        public static void VillaBlock(float ax, float ex, float az, float ez)
        {
            Box("pad", ax, ex, Deck, Deck + 0.02f, az, ez, "lane_concrete");
            const float px = 22f, pz = 22f;
            int nx = Mathf.Max(1, (int)((ex - ax) / px)), nz = Mathf.Max(1, (int)((ez - az) / pz));
            float ox = (ex - ax - nx * px) / 2f, oz = (ez - az - nz * pz) / 2f;
            for (int i = 0; i < nx; i++)
                for (int k = 0; k < nz; k++)
                {
                    float x = ax + ox + (i + 0.5f) * px, z = az + oz + (k + 0.5f) * pz;
                    if (Rng.NextDouble() < 0.08) { Tree(x, z, Deck, R(5f, 8f)); continue; }
                    Prefab("building", "bld_villa_kr", new Vector3(x, Deck + 0.02f, z), (k % 2 == 0 ? 180f : 0f) + R(-2f, 2f), Vector3.one * R(0.55f, 0.68f));
                }
            // alleys run through to the surrounding sidewalks (3 m beyond the plot edge)
            for (int i = 1; i < nx; i++) Box("lane", ax + ox + i * px - 1.6f, ax + ox + i * px + 1.6f, Deck + 0.02f, Deck + 0.05f, az - 3.2f, ez + 3.2f, "lane_concrete");
            for (int k = 1; k < nz; k++) Box("lane", ax - 3.2f, ex + 3.2f, Deck + 0.02f, Deck + 0.05f, az + oz + k * pz - 1.6f, az + oz + k * pz + 1.6f, "lane_concrete");
        }

        /// <summary>
        /// Low-rise fishing-port town on a strip between the port road (zRoad, sidewalk edge) and zBack (reference R01 Wando):
        /// a front row of 2-3 storey fishery shops / restaurants (회센터, 수산물 가게) facing the road with awnings and fish tanks,
        /// a 3.5 m alley, a back row of houses and gear sheds, and cross alleys every 35-45 m from the sidewalk to the back;
        /// fish boxes and drums stand in the alleys. Building fronts face -z (the road).
        /// </summary>
        public static void FishingTown(float x0, float x1, float zRoad, float zBack)
        {
            float frontZ = zRoad + 8f, alleyZ = zRoad + 20.5f, backZ = Mathf.Min(zBack - 11f, zRoad + 32f);
            var cross = new List<float> { x0 + 2f };
            for (float x = x0 + R(35f, 45f); x < x1 - 25f; x += R(35f, 45f)) cross.Add(x);
            cross.Add(x1 - 2f);
            float ax0 = cross[0] - 1.75f, ax1 = cross[cross.Count - 1] + 1.75f, backLane = backZ + 7.5f;
            Box("lane", ax0, ax1, Deck + 0.02f, Deck + 0.05f, alleyZ - 1.75f, alleyZ + 1.75f, "lane_concrete");
            Box("lane", ax0, ax1, Deck + 0.02f, Deck + 0.05f, backLane - 1.75f, backLane + 1.75f, "lane_concrete");   // lane behind the houses
            foreach (var cx in cross) Box("lane", cx - 1.75f, cx + 1.75f, Deck + 0.02f, Deck + 0.05f, zRoad, backLane + 1.75f, "lane_concrete");
            string[] awn = { "roof_blue", "roof_red", "roof_teal" };
            for (int c = 0; c + 1 < cross.Count; c++)
            {
                float a = cross[c] + 2.2f, b = cross[c + 1] - 2.2f;
                float ca = cross[c] + 1.75f + 2.0f, cb = cross[c + 1] - 1.75f - 2.0f;      // usable frontage between the lanes
                // front row: small shops of 7-10 m frontage
                for (float x = a; x < b - 6f;)
                {
                    float w = Mathf.Min(R(7f, 10f), b - x);
                    if (w < 6f) break;
                    double u = Rng.NextDouble();
                    float sx0 = Mathf.Max(x, ca), sx1 = Mathf.Min(x + w, cb), sw = sx1 - sx0, scx = (sx0 + sx1) / 2f;
                    if (u < 0.55)
                    {
                        float hgt = R(6.5f, 10f);
                        string wall = Rng.NextDouble() < 0.5 ? "wall_white" : "wall_cream"; string aw = awn[Rng.Next(awn.Length)]; bool tank = Rng.NextDouble() < 0.6;
                        if (sw >= 5f)
                        {
                            BoxC("building", new Vector3(scx, Deck + hgt / 2f, frontZ + 1.5f), new Vector3(sw - 0.4f, hgt, 9f), wall);
                            BoxC("awning", new Vector3(scx, Deck + 3.0f, frontZ - 3.6f), new Vector3(sw - 0.6f, 0.08f, 2.4f), aw, 0f, new Vector3(-12f, 0f, 0f));
                            if (tank) BoxC("fish_tank", new Vector3(scx, Deck + 0.55f, frontZ - 3.9f), new Vector3(Mathf.Min(3f, sw - 2f), 1.1f, 1.0f), "glass_dark");
                        }
                    }
                    else if (u < 0.8) { float yb = 180f + R(-2f, 2f); float bc = Mathf.Clamp(x + w / 2f, ca + 4.2f, cb - 4.2f); if (cb - ca >= 8.4f) Prefab("building", "bld_bakery_kr", new Vector3(bc, Deck + 0.02f, frontZ + 1.5f), yb, Vector3.one * 0.9f); }
                    else { float hc = Mathf.Clamp(x + w / 2f, ca + 4.3f, cb - 4.3f); if (cb - ca >= 8.6f) Prefab("house", HillFeatures.HouseKinds[2].key, new Vector3(hc, Deck + 0.05f, frontZ + 1f), 180f, Vector3.one); }
                    x += w;
                }
                // back row: houses and gear sheds facing the alley
                for (float x = a + 1f; x < b - 5f;)
                {
                    int kind = Rng.NextDouble() < 0.2 ? 5 : Rng.Next(5);
                    var k = HillFeatures.HouseKinds[kind];
                    if (x + k.w > b) break;
                    Prefab("house", k.key, new Vector3(x + k.w / 2f, Deck + 0.05f, backZ), 180f + R(-3f, 3f), Vector3.one * R(0.92f, 1.0f));
                    x += k.w + R(1f, 2.5f);
                }
                // fish boxes / drums along the alley
                for (int g = 0; g < 3; g++)
                {
                    var key = Rng.NextDouble() < 0.6 ? (Rng.NextDouble() < 0.5 ? "crate_01" : "crate_02") : "barrel";
                    Prefab("fishing_gear", key, new Vector3(R(a + 2f, b - 2f), Deck + 0.05f, alleyZ + (Rng.NextDouble() < 0.5 ? -1.2f : 1.2f)), R(0f, 360f), Vector3.one);
                }
            }
        }

        /// <summary>While true, "tower" apartment plots replace some cells with the Sketchfab "Urban" collection towers
        /// (bld_urban_ny_hirise, bld_urban_office_tower). Set only around the marina beach-city blocks.</summary>
        public static bool UrbanCollectionMix;
        /// <summary>Positions (x, z) of Urban office towers already placed while the mix is on; a new one keeps
        /// UrbanOfficeSpacing from all of them so the dark glass towers never stand side by side (seeded with the front row).</summary>
        public static readonly List<Vector2> UrbanOfficeTowers = new List<Vector2>();
        public const float UrbanOfficeSpacing = 110f;
        /// <summary>Wrapped prefab footprints (m, yaw 0): NY hi-rise 44.05 x 44.05 (with podium), office tower 32.61 x 23.17.</summary>
        public const float UrbanNyFootprint = 44.05f;
        public static readonly Vector2 UrbanOfficeFootprint = new Vector2(32.61f, 23.17f);

        public static void ApartmentPlot(float ax, float ex, float az, float ez, string mix = null)
        {
            float wx = ex - ax, wz = ez - az;
            double u = mix == "tower" ? 0.6 + 0.4 * Rng.NextDouble() : mix == "low" ? 0.6 * Rng.NextDouble() : Rng.NextDouble();
            string key; float fx, fz, sc, gap;
            if (u < 0.3) { key = "bld_apt_complex"; fx = 26f; fz = 60f; sc = 1f; gap = 9f; }
            else if (u < 0.6) { key = "bld_apt_slab"; fx = 17.9f; fz = 43.4f; sc = R(0.9f, 1.05f); gap = 9f; }
            else if (u < 0.82) { key = "bld_hirise"; fx = 38f; fz = 38f; sc = R(0.7f, 0.85f); gap = 10f; }
            else { key = "bld_officetel"; fx = 33f; fz = 26f; sc = 0.8f; gap = 9f; }
            float px = fx * sc + gap, pz = fz * sc + gap;
            int nx = Mathf.Max(1, (int)((wx - 4f + gap) / px)), nz = Mathf.Max(1, (int)((wz - 4f + gap) / pz));
            if (nx * px - gap > wx || nz * pz - gap > wz) sc *= Mathf.Min((wx - 4f) / (nx * px - gap), (wz - 4f) / (nz * pz - gap));
            float sx0 = (ax + ex) / 2f - (nx - 1) * px / 2f, sz0 = (az + ez) / 2f - (nz - 1) * pz / 2f;
            for (int i = 0; i < nx; i++)
                for (int k = 0; k < nz; k++)
                {
                    // A whole block of the same tower reads as a copied asset from the beach.
                    // Both alternatives fit the selected tower cell (38 x 38 m is the largest footprint).
                    string cellKey = key;
                    float cellScale = sc;
                    float cellYaw = 0f;
                    if (mix == "tower" && (i + 2 * k) % 3 == 1)
                    {
                        cellKey = key == "bld_hirise" ? "bld_officetel" : "bld_hirise";
                        cellScale = cellKey == "bld_hirise" ? Mathf.Min(sc, 0.65f) : Mathf.Min(sc, 0.8f);
                    }
                    if (mix == "tower" && UrbanCollectionMix)
                    {
                        // Sketchfab "Urban" collection towers (marina beach city only): picked from the cell position, no RNG draw,
                        // scaled so the wrapped bounds stay inside this cell's footprint (fx x fz at sc), so other maps are unchanged.
                        int sel = ((i * 3 + k * 5 + Mathf.RoundToInt(sx0 / 7f)) % 4 + 4) % 4;
                        float cellX = fx * sc, cellZ = fz * sc;
                        if (sel == 0) { cellKey = "bld_urban_ny_hirise"; cellScale = Mathf.Min(Mathf.Min(cellX, cellZ) / UrbanNyFootprint, 0.75f); }
                        else if (sel == 2 && (i + k) % 2 == 0)
                        {
                            var here = new Vector2(sx0 + i * px, sz0 + k * pz);
                            if (!UrbanOfficeTowers.Any(o => (o - here).magnitude < UrbanOfficeSpacing))
                            {
                                cellKey = "bld_urban_office_tower"; UrbanOfficeTowers.Add(here);
                                cellScale = Mathf.Min(Mathf.Min(cellX / UrbanOfficeFootprint.x, cellZ / UrbanOfficeFootprint.y), 1f);
                            }
                        }
                        if (cellKey.StartsWith("bld_urban") && (i + k) % 2 == 1) cellYaw = 180f;     // both fit the cell turned by 180 deg
                    }
                    Prefab("building", cellKey, new Vector3(sx0 + i * px, Deck + 0.02f, sz0 + k * pz), cellYaw,
                        Vector3.one * cellScale * R(0.97f, 1.03f));
                }
            // trees in the gaps between buildings and along the plot edge
            for (int i = 0; i + 1 < nx; i++)
                for (float z = az + 3f; z < ez - 3f; z += 6f) Tree(sx0 + (i + 0.5f) * px, z, Deck, R(5f, 8f));
            for (int k = 0; k + 1 < nz; k++)
                for (float x = ax + 3f; x < ex - 3f; x += 6f) Tree(x, sz0 + (k + 0.5f) * pz, Deck, R(5f, 8f));
            for (float x = ax + 2f; x < ex - 2f; x += 6f) { Tree(x, az + 1.2f, Deck, R(5f, 8f)); Tree(x, ez - 1.2f, Deck, R(5f, 8f)); }
        }

        /// <summary>Street-level shop row along x at depth z: Asian shop-pack rows mixed with the Korean bakery and small
        /// multi-family buildings (CC BY models), fronts facing -z when yaw = 180.</summary>
        public static void ShopRow(float x0, float x1, float z, float yaw)
        {
            for (float x = x0 + 14f; x < x1 - 10f;)
            {
                double u = Rng.NextDouble();
                if (u < 0.5)
                {
                    // the 34.6 x 9.2 m "asian shop pack" block (low-res billboard fronts, user photo 2026-11-10) is replaced by a
                    // harbour shop street inside the same footprint; own seeded RNG, so the shared draws here stay as before
                    float front = z + (yaw > 90f ? -4.58f : 4.58f);
                    HarbourShopStreet(x + 0.3f, x + 34.9f, front, 9.2f, 7919 * Mathf.RoundToInt(x) + 104729 * Mathf.RoundToInt(z) + 17, yaw > 90f ? 0f : 180f);
                    x += 38f;
                }
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
            for (int i = 0; i < count * 3; i++) Tree(R(x0, x1), R(z0, z1), Deck - 0.2f, R(hMin, hMax));   // 2026-11-09: 3x denser
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
