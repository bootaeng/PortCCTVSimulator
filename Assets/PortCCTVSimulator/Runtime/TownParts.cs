using System.Collections.Generic;
using UnityEngine;
using static PortCCTV.HarborGeneric02;

namespace PortCCTV
{
    /// <summary>
    /// Town batch of 2026-11-10 (user photos: crude shop row and an empty grey strip in the fishing-port backland; sparse
    /// villages). Every function here draws from its own seeded System.Random, never from the map's shared Rng, so the rest
    /// of a map (trees, cars, houses placed afterwards) keeps its layout.
    /// Assets: shopfront_kr (Asian Shop 4), house_jp03, bld_hk_tower, kr_sign_*, guard_soldier (ThirdParty/SketchfabCCBY/
    /// town3/SOURCE_town3.md); wall_conc_* / corrugated_* / paint_hatch_yellow materials (CC0, Editor/TownAssets.cs).
    /// All background, never labelled.
    /// </summary>
    internal static partial class HarborParts
    {
        // Facing conventions of the imported models (checked on captures 2026-11-10): the yaw that turns the model's front
        // towards +z of the placement frame.
        internal const float ShopfrontYaw = 180f;     // Asian Shop 4: shop window faces local -z
        internal const float KrSignYaw = 0f;          // Korean sign pack: plate face = local +z (checked on fix_town_extra 2026-11-10)
        internal const float JpHouseYaw = 0f;

        static readonly string[] WallMats = { "wall_conc_grey", "wall_conc_cream", "wall_conc_blue", "wall_conc_pink", "wall_conc_green" };
        static readonly string[] IronMats = { "corrugated_blue", "corrugated_green", "corrugated_red", "corrugated_grey", "corrugated_white" };
        static readonly string[] SignBoardMats = { "roof_blue", "roof_red", "roof_teal", "crane_yellow", "truck_white", "beacon_green" };

        static float RR(System.Random r, float a, float b) => a + (float)r.NextDouble() * (b - a);

        /// <summary>Marks the elements added since `first` as one lot ("lot&lt;n&gt;_" id prefix): a lot touching a carriageway is
        /// removed as a whole by RoadNetwork.ClearStreetFurniture (shop rows inside city blocks can straddle a cross street).</summary>
        static int _shopLotN;
        internal const string LotPrefix = "lot";
        static void TagLot(int first)
        {
            string p = $"{LotPrefix}{_shopLotN++}_";
            for (int i = first; i < E.Count; i++) E[i].id = p + E[i].id;
        }
        static T Pick<T>(System.Random r, T[] a) => a[r.Next(a.Length)];

        /// <summary>
        /// Harbour shop street (회센터 / 수산물 직판장 row): buildings with their fronts at z = zFront facing -z (the port road),
        /// lots of 7-12 m frontage. Mix: 2-4 storey shophouses (CC0 concrete walls in five paint tints, shopfront modules on the
        /// ground floor, a blank sign band, awning, roof parapet, some rooftop water tanks), single-storey corrugated fish
        /// stores with roller doors, Korean bakery / multi-family models and the Japanese-style house; setbacks +/-0.8 m and yaw
        /// +/-2 deg. Depth stays within zFront .. zFront + maxDepth. Returns the number of buildings.
        /// </summary>
        public static int HarbourShopStreet(float x0, float x1, float zFront, float maxDepth, int seed, float frameYaw = 0f)
        {
            // frameYaw 0: fronts face -z and the lots run from zFront towards +z; 180: fronts face +z, lots towards -z
            var r = new System.Random(seed); int n = 0; float sz = Mathf.Abs(frameYaw) < 90f ? 1f : -1f;
            for (float x = x0; x < x1 - 6f;)
            {
                float w = Mathf.Min(RR(r, 7f, 12f), x1 - x);
                if (w < 6f) break;
                if (r.NextDouble() < 0.08) { x += 3.5f; continue; }                     // alley between two lots
                float cx = x + w / 2f, set = RR(r, -0.8f, 0.8f), yaw = frameYaw + RR(r, -2f, 2f), zf = zFront + sz * set;
                double u = r.NextDouble();
                // a street already crossing this lot (shop rows of city blocks can straddle a cross street): leave the lot empty
                if (OnStreet(cx - w / 2f, cx + w / 2f, Mathf.Min(zf, zf + sz * maxDepth), Mathf.Max(zf, zf + sz * maxDepth))) { x += w; continue; }
                int first = E.Count;
                if (u < 0.5) ShopHouse(r, cx, zf, w - 0.3f, Mathf.Min(maxDepth, RR(r, 10f, 13f)), yaw);
                else if (u < 0.68) FishStore(r, cx, zf, w - 0.3f, Mathf.Min(maxDepth, RR(r, 9f, 12f)), yaw);
                else if (u < 0.8 && w >= 8.5f && maxDepth >= 9f) Prefab("building", "bld_bakery_kr", new Vector3(cx, Deck + 0.02f, zf + sz * 4.5f), 180f + yaw, Vector3.one * 0.9f);
                else if (u < 0.9 && w >= 10f) { float hs = Mathf.Min((w - 0.5f) / 11.8f, (maxDepth - 0.3f) / 11.5f); Prefab("building", "house_jp03", new Vector3(cx, Deck + 0.02f, zf + sz * (11.5f * hs / 2f + 0.15f)), 180f + JpHouseYaw + yaw, Vector3.one * hs); }
                else ShopHouse(r, cx, zf, w - 0.3f, Mathf.Min(maxDepth, RR(r, 10f, 13f)), yaw);
                TagLot(first);
                x += w; n++;
            }
            return n;
        }

        /// <summary>One 2-4 storey shophouse: front face at z = zf (facing -z), centre x = cx.</summary>
        static void ShopHouse(System.Random r, float cx, float zf, float w, float d, float yaw)
        {
            int floors = r.NextDouble() < 0.45 ? 2 : r.NextDouble() < 0.7 ? 3 : 4;
            float fh = 3.2f, h = floors * fh + 0.4f;
            string wall = Pick(r, WallMats);
            var q = Quaternion.Euler(0f, yaw, 0f);
            Vector3 P(float lx, float ly, float lz) => new Vector3(cx, 0f, zf) + q * new Vector3(lx, ly, lz);
            BoxC("building", P(0f, Deck + h / 2f, d / 2f), new Vector3(w, h, d), wall, yaw);
            BoxC("building", P(0f, Deck + h + 0.45f, d / 2f), new Vector3(w + 0.3f, 0.9f, d + 0.3f), "roof_concrete", yaw);     // parapet
            // upper-floor windows: dark glass bands set into the facade
            for (int f = 1; f < floors; f++)
                BoxC("building", P(0f, Deck + f * fh + 1.6f, -0.04f), new Vector3(w * 0.72f, 1.3f, 0.1f), "glass_dark", yaw);
            // ground floor: shopfront modules (3 m each) across the frontage, a blank colour sign band above, an awning
            int mods = Mathf.Max(1, Mathf.FloorToInt((w - 0.6f) / 3f));
            float s0 = -mods * 1.5f + 1.5f;
            for (int k = 0; k < mods; k++)
                Prefab("facade_detail", "shopfront_kr", P(s0 + k * 3f, Deck, -0.02f), ShopfrontYaw + yaw, Vector3.one);
            BoxC("building", P(0f, Deck + 3.45f, -0.15f), new Vector3(w * 0.96f, 0.75f, 0.12f), Pick(r, SignBoardMats), yaw);
            if (r.NextDouble() < 0.6) BoxC("awning", P(0f, Deck + 2.95f, -1.2f), new Vector3(w * 0.9f, 0.06f, 2.2f), Pick(r, IronMats), yaw, new Vector3(-10f, 0f, 0f));
            if (floors >= 3 && r.NextDouble() < 0.5)                                          // rooftop water tank (common on Korean low-rise)
            {
                var t = P(RR(r, -w / 4f, w / 4f), 0f, d * 0.65f);
                Cyl("building", t.x, t.z, Deck + h + 0.9f, Deck + h + 2.6f, 1.6f, "tank_white");
            }
        }

        /// <summary>Single-storey corrugated fish store / gear shed with a roller door: front at z = zf facing -z.</summary>
        static void FishStore(System.Random r, float cx, float zf, float w, float d, float yaw)
        {
            float h = RR(r, 4.8f, 6.2f);
            var q = Quaternion.Euler(0f, yaw, 0f);
            Vector3 P(float lx, float ly, float lz) => new Vector3(cx, 0f, zf) + q * new Vector3(lx, ly, lz);
            string iron = Pick(r, IronMats);
            BoxC("building", P(0f, Deck + h / 2f, d / 2f), new Vector3(w, h, d), iron, yaw);
            BoxC("building", P(0f, Deck + h + 0.25f, d / 2f), new Vector3(w + 0.6f, 0.5f, d + 0.6f), r.NextDouble() < 0.5 ? "warehouse_roof" : "corrugated_grey", yaw);
            float dw = Mathf.Min(w - 2f, 4.5f);
            BoxC("building", P(0f, Deck + 1.7f, -0.04f), new Vector3(dw, 3.4f, 0.1f), "steel_galv", yaw);           // roller door
            BoxC("building", P(0f, Deck + 3.8f, -0.12f), new Vector3(w * 0.8f, 0.6f, 0.1f), Pick(r, SignBoardMats), yaw);
            if (w > 8f) Prefab("facade_detail", "shopfront_kr", P(w / 2f - 1.9f, Deck, -0.02f), ShopfrontYaw + yaw, Vector3.one);
        }

        /// <summary>
        /// Direct-sale fish market hall (수산물 직판장): long corrugated hall with a gable-like stepped roof, an open front
        /// canopy over a row of stall counters and shopfront modules along the back of the canopy. Front at z = zf facing -z.
        /// </summary>
        public static void FishMarketHall(float x0, float x1, float zf, float depth, int seed)
        {
            var r = new System.Random(seed);
            float w = x1 - x0, cx = (x0 + x1) / 2f, h = 8.5f;
            BoxC("building", new Vector3(cx, Deck + h / 2f, zf + 4f + depth / 2f), new Vector3(w, h, depth), "corrugated_blue", 0f);
            BoxC("building", new Vector3(cx, Deck + h + 0.6f, zf + 4f + depth / 2f), new Vector3(w + 0.8f, 1.2f, depth * 0.7f), "warehouse_roof", 0f);
            BoxC("building", new Vector3(cx, Deck + h + 0.2f, zf + 4f + depth / 2f), new Vector3(w + 0.8f, 0.4f, depth + 0.8f), "warehouse_roof", 0f);
            // front canopy on columns, stall counters, shopfronts along the hall wall
            BoxC("market", new Vector3(cx, Deck + 4.6f, zf + 2f), new Vector3(w, 0.25f, 4.6f), "corrugated_white", 0f, new Vector3(-6f, 0f, 0f));
            for (float x = x0 + 0.4f; x <= x1 - 0.3f; x += 6f) Box("market", x - 0.15f, x + 0.15f, Deck, Deck + 4.4f, zf - 0.15f, zf + 0.15f, "steel_galv");
            for (float x = x0 + 3f; x < x1 - 3f; x += 6f)
            {
                Box("market", x - 1.8f, x + 1.8f, Deck, Deck + 0.95f, zf + 1.2f, zf + 2.4f, "tank_white");                 // tiled stall counter
                Box("market", x - 1.6f, x + 1.6f, Deck + 0.95f, Deck + 1.25f, zf + 1.3f, zf + 2.3f, "glass_dark");          // fish tank / ice tray
                if (r.NextDouble() < 0.7) Prefab("fishing_gear", r.NextDouble() < 0.5 ? "crate_01" : "crate_02", new Vector3(x + RR(r, -1.5f, 1.5f), Deck, zf + 3.2f), RR(r, 0f, 360f), Vector3.one);
            }
            for (float x = x0 + 2f; x < x1 - 2f; x += 3f) Prefab("facade_detail", "shopfront_kr", new Vector3(x + 1.5f, Deck, zf + 3.98f), ShopfrontYaw, Vector3.one);
            BoxC("building", new Vector3(cx, Deck + 6.2f, zf + 3.85f), new Vector3(w * 0.6f, 1.6f, 0.2f), "truck_white", 0f);   // blank name board
        }

        /// <summary>Corrugated sheds (gear / ice stores) along a strip, fronts at z = zf facing -z.</summary>
        public static void GearSheds(float x0, float x1, float zf, float depth, int seed)
        {
            var r = new System.Random(seed);
            for (float x = x0; x < x1 - 8f;)
            {
                float w = Mathf.Min(RR(r, 10f, 18f), x1 - x);
                int first = E.Count;
                FishStore(r, x + w / 2f, zf + RR(r, 0f, 1.5f), w - 0.8f, Mathf.Min(depth, RR(r, 10f, 16f)), RR(r, -1.5f, 1.5f));
                TagLot(first);
                x += w + RR(r, 1.5f, 4f);
            }
        }

        /// <summary>Korean road sign at `pos` whose plate faces `face` (xz).</summary>
        internal static void KrSign(string key, Vector3 pos, Vector2 face)
        {
            float yaw = Mathf.Atan2(face.x, face.y) * Mathf.Rad2Deg + KrSignYaw;
            E.RemoveAll(e => e.kind == "tree" && (new Vector2(e.center.x - pos.x, e.center.z - pos.z)).sqrMagnitude < 3.5f * 3.5f);
            Prefab("road_sign", key, pos, yaw, Vector3.one, id: Uid("krsign"));
        }

        /// <summary>A harbour guard (security / military police post) standing at `pos` facing `face`.</summary>
        internal static void Guard(Vector3 pos, Vector2 face, string id)
            => Prefab("person", "guard_soldier", pos, Mathf.Atan2(face.x, face.y) * Mathf.Rad2Deg, Vector3.one, id: id, nonVessel: true);

        /// <summary>
        /// Distant tower blocks (the brand-free HK tower model, 54 m) on free flat land between x0..x1 / z0..z1: a spot is used
        /// only if no existing element footprint (roads, buildings, fields, lots) is within the tower footprint + 6 m and the
        /// ground there is land at deck level. For far backgrounds only; never placed near a camera preset (min distance
        /// `minCamDist`).
        /// </summary>
        public static int DistantTowers(float x0, float x1, float z0, float z1, int count, int seed, IEnumerable<Vector3> cams, float minCamDist = 600f)
        {
            var r = new System.Random(seed); int placed = 0;

            for (int t = 0; t < count * 30 && placed < count; t++)
            {
                float x = RR(r, x0, x1), z = RR(r, z0, z1), yaw = r.Next(4) * 90f + RR(r, -3f, 3f), sc = RR(r, 0.85f, 1.1f);
                var foot = Rect.MinMaxRect(x - 17f * sc, z - 17f * sc, x + 17f * sc, z + 17f * sc);
                bool near = false; foreach (var c in cams) if (new Vector2(c.x - x, c.z - z).sqrMagnitude < minCamDist * minCamDist) near = true;
                if (near || !FreeLand(foot)) continue;
                Prefab("building", "bld_hk_tower", new Vector3(x, Deck, z), yaw, Vector3.one * sc, id: Uid("hk_tower"));
                placed++;
            }
            return placed;
        }

        static bool OnStreet(float x0, float x1, float z0, float z1)
        {
            foreach (var e in E)
            {
                if (e.shape != "box" || !(e.kind == "road" || e.kind == "lane" || e.kind == "driveway") || !(e.material == "road_asphalt" || e.material == "lane_concrete")) continue;
                var q = RectXZ(e);
                if (q.xMin + 0.3f < x1 && q.xMax - 0.3f > x0 && q.yMin + 0.3f < z1 && q.yMax - 0.3f > z0) return true;
            }
            return false;
        }

        static readonly HashSet<string> FreeIgnore = new HashSet<string> { "land", "terrace", "hill_base", "tree", "shrub" };

        static bool FreeLand(Rect foot)
        {
            bool onLand = false;
            foreach (var e in E)
            {
                if (e.shape == "billboard_tree") continue;
                var rr = RectXZ(e);
                if (!rr.Overlaps(foot)) continue;
                if (e.shape == "box" && (e.kind == "land" || e.kind == "terrace") && e.center.y + e.size.y / 2f > Deck - 0.2f
                    && rr.xMin <= foot.xMin && rr.xMax >= foot.xMax && rr.yMin <= foot.yMin && rr.yMax >= foot.yMax) { onLand = true; continue; }
                if (FreeIgnore.Contains(e.kind ?? "")) continue;
                if (e.kind == "hill") return false;
                return false;
            }
            return onLand;
        }
    }
}
