using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static PortCCTV.HarborGeneric02;

namespace PortCCTV
{
    /// <summary>
    /// Road furniture tied to what the road is (user 2026-11-09, docs/detail_review_20261109 §6): junctions are recorded when
    /// they are laid; at the end of the build lane lines are cleared out of junction boxes, and signs are placed where a rule
    /// applies instead of at random intervals:
    ///  * speed limit 60 on each exit of a junction on a four-lane road (drivers' right sidewalk, 25 m past the junction);
    ///  * a car park with two driveways gets an entry and an exit: "no entry" at the exit mouth facing the road, and on a
    ///    four-lane road "no left turn" at the exit facing the driver leaving the lot;
    ///  * "no parking" along coastal roads (one per 150 m, land side, facing traffic);
    ///  * "keep right" at the start of each median barrier run.
    /// Street trees within 4.5 m of a sign are removed so the sign face is not hidden.
    /// </summary>
    internal static partial class HarborParts
    {
        internal struct Junction { public Vector2 c; public bool alongX, full, signals; public float wMain, wCross; }
        internal static readonly List<Junction> Junctions = new List<Junction>();
        internal static readonly List<(Vector3 p, float yaw)> MedianStarts = new List<(Vector3, float)>();
        internal static readonly List<(float x0, float x1, float z, float seaDir)> CoastalRoads = new List<(float, float, float, float)>();
        internal static void ResetRoads() { Junctions.Clear(); MedianStarts.Clear(); CoastalRoads.Clear(); _shopLotN = 0; }

        // Korean regulatory signs (South Korean Road Signs pack, 2026-11-10) where the pack has them; keep-right stays pictogram
        const string SignSpeed = "kr_sign_sl50", SignNoEntry = "kr_sign_no_entry", SignNoLeft = "kr_sign_no_left", SignNoParking = "kr_sign_no_parking", SignKeepRight = "road_sign_4";

        /// <summary>Mid-block signalled pedestrian crossing (zebra, stop lines, vehicle and pedestrian signals on both sides).</summary>
        public static void PedCrossing(Vector2 centre, bool mainAlongX, float wMain)
        {
            float hm = wMain / 2f;
            Vector3 P(float along, float across) => mainAlongX ? new Vector3(centre.x + along, Deck, centre.y + across) : new Vector3(centre.x + across, Deck, centre.y + along);
            for (float t = -hm + 0.3f; t < hm - 0.3f; t += 1.0f)
            {
                var p = P(0f, t + 0.25f);
                BoxC("marking", new Vector3(p.x, Deck + 0.065f, p.z), mainAlongX ? new Vector3(3f, 0.012f, 0.5f) : new Vector3(0.5f, 0.012f, 3f), "paint_white");
            }
            float yawMain = mainAlongX ? 0f : 90f;
            foreach (var s in new[] { -1f, 1f })
            {
                var sl = P(s * 3.4f, -s * hm / 2f);
                BoxC("marking", new Vector3(sl.x, Deck + 0.065f, sl.z), mainAlongX ? new Vector3(0.4f, 0.012f, hm) : new Vector3(hm, 0.012f, 0.4f), "paint_white");
                Prefab("signal", "signal_vehicle", P(s * 4.5f, -s * (hm + 1.2f)), yawMain + (s > 0 ? 180f : 0f), Vector3.one);
                Prefab("signal", "signal_ped", P(s * 2.2f, -s * (hm + 0.9f)), yawMain + (s > 0 ? 0f : 180f), Vector3.one);
            }
            Junctions.Add(new Junction { c = centre, alongX = mainAlongX, wMain = wMain, wCross = 3f, full = false, signals = true });
        }

        /// <summary>Pedestrian railing along a quay / walk edge (posts every 2 m, top and middle rail, galvanised steel): for
        /// walks beside water. Working-area boundaries use concrete barriers, temporary works use jersey barriers instead.</summary>
        public static void QuayRailing(float c, float a0, float a1, float y, bool alongX)
        {
            void B(float u0, float u1, float y0, float y1, float w)
            {
                if (alongX) Box("railing", u0, u1, y0, y1, c - w / 2f, c + w / 2f, "steel_galv");
                else Box("railing", c - w / 2f, c + w / 2f, y0, y1, u0, u1, "steel_galv");
            }
            for (float u = a0; u <= a1 + 0.01f; u += 2f) B(u - 0.04f, u + 0.04f, y, y + 1.1f, 0.08f);
            B(a0, a1, y + 1.04f, y + 1.1f, 0.06f);
            B(a0, a1, y + 0.55f, y + 0.6f, 0.04f);
        }

        /// <summary>A sign standing at `pos` whose face points along `face` (xz); street trees within 4.5 m are removed.</summary>
        static void PlaceSign(string key, Vector3 pos, Vector2 face)
        {
            float yaw = Mathf.Atan2(face.x, face.y) * Mathf.Rad2Deg + (key.StartsWith("kr_") ? KrSignYaw : 0f);   // pictogram pack: face = local +z
            E.RemoveAll(e => e.kind == "tree" && (new Vector2(e.center.x - pos.x, e.center.z - pos.z)).sqrMagnitude < 4.5f * 4.5f);
            Prefab("street", key, pos, yaw, Vector3.one, id: Uid("sign"));
        }

        static bool FreeSpot(Vector3 p, float r)
        {
            foreach (var e in E)
                if ((e.kind == "street" || e.kind == "road_sign" || e.kind == "signal" || e.kind == "street_light" || e.kind == "bench" || e.kind == "power_pole") && e.shape == "prefab"
                    && (new Vector2(e.center.x - p.x, e.center.z - p.z)).sqrMagnitude < r * r) return false;
            return true;
        }

        static bool OnSidewalk(Vector3 p)
        {
            foreach (var e in E)
                if (e.kind == "sidewalk" && e.shape == "box" && RectXZ(e).Contains(new Vector2(p.x, p.z))) return true;
            return false;
        }

        internal static void FinalizeRoads(HarborDefinition h)
        {
            // 1) lane lines (thin markings) out of junction boxes; zebras / stop lines (wider than 0.3 m) stay
            foreach (var j in Junctions)
            {
                float hx = (j.alongX ? j.wCross : j.wMain) / 2f + 0.2f, hz = (j.alongX ? j.wMain : j.wCross) / 2f + 0.2f;
                var box = new Rect(j.c.x - hx, j.c.y - hz, 2f * hx, 2f * hz);
                if (!j.full && j.wCross <= 3.1f) continue;                        // mid-block crossing: the lines continue
                CutThinMarkings(box);
            }
            // 2) speed limit on each exit of four-lane junctions
            foreach (var j in Junctions.Where(j => j.signals && j.wMain >= 13f))
            {
                Vector2 along = j.alongX ? Vector2.right : Vector2.up, across = j.alongX ? Vector2.up : Vector2.right;
                foreach (float dir in new[] { 1f, -1f })
                {
                    // drivers leaving the junction along d = dir*along; seen from above (+x east, +z north) their right is (d.z, -d.x)
                    var d = along * dir; var right = new Vector2(d.y, -d.x);
                    var p2 = j.c + along * dir * (j.wCross / 2f + 25f) + right * (j.wMain / 2f + 1.3f);
                    var p = new Vector3(p2.x, Deck + 0.16f, p2.y);
                    for (int k = 0; k < 4 && !FreeSpot(p, 3.5f); k++) p += new Vector3(along.x, 0f, along.y) * dir * 4f;
                    if (OnSidewalk(p) && FreeSpot(p, 3.5f)) PlaceSign(SignSpeed, p, -along * dir);   // face the approaching driver
                }
            }
            // 3) car parks with two driveways: entry + exit
            foreach (var lot in h.parking_lots.Where(l => l.kind == "car_park" || l.kind == "truck_park"))
            {
                if (lot.driveways.Count < 2) continue;
                var exit = lot.driveways[1];
                var exitRoad = h.elements.FirstOrDefault(e => e.id == exit.road_id);
                if (exitRoad == null || exitRoad.kind != "road") continue;          // a lot opening onto its own yard has no street exit
                var lotC = (lot.min + lot.max) / 2f;
                var dc = (exit.min + exit.max) / 2f;
                var outDir = Mathf.Abs(dc.x - lotC.x) / (lot.max.x - lot.min.x) > Mathf.Abs(dc.y - lotC.y) / (lot.max.y - lot.min.y)
                    ? new Vector2(Mathf.Sign(dc.x - lotC.x), 0f) : new Vector2(0f, Mathf.Sign(dc.y - lotC.y));
                var side = new Vector2(outDir.y, outDir.x);
                float halfW = (Mathf.Abs(outDir.x) > 0 ? exit.max.y - exit.min.y : exit.max.x - exit.min.x) / 2f;
                // mouth of the driveway at the road end
                var mouth = outDir.x > 0 ? new Vector2(exit.max.x, dc.y) : outDir.x < 0 ? new Vector2(exit.min.x, dc.y) : outDir.y > 0 ? new Vector2(dc.x, exit.max.y) : new Vector2(dc.x, exit.min.y);
                var sp = mouth - outDir * 1.0f + side * (halfW + 0.8f);
                var p = new Vector3(sp.x, Deck + 0.1f, sp.y);
                if (FreeSpot(p, 2f)) PlaceSign(SignNoEntry, p, outDir);              // seen from the road: exit only
                var road = h.elements.FirstOrDefault(e => e.id == exit.road_id);
                bool fourLane = road != null && Mathf.Min(RectXZ(road).width, RectXZ(road).height) >= 13f;
                if (fourLane)
                {
                    var sp2 = mouth - outDir * 1.0f - side * (halfW + 0.8f);
                    var p2 = new Vector3(sp2.x, Deck + 0.1f, sp2.y);
                    if (FreeSpot(p2, 2f)) PlaceSign(SignNoLeft, p2, -outDir);         // seen by the driver leaving
                }
            }
            // 4) no parking along coastal roads (land side, facing traffic in the land-side lane)
            foreach (var (x0, x1, z, seaDir) in CoastalRoads)
                for (float x = x0 + 40f; x < x1 - 30f; x += 150f)
                {
                    var p = new Vector3(x, Deck, z - seaDir * 4.9f);
                    for (int k = 0; k < 6 && !FreeSpot(p, 2.5f); k++) p.x += 5f;
                    if (FreeSpot(p, 2.5f)) PlaceSign(SignNoParking, p, seaDir < 0 ? Vector2.right : Vector2.left);
                }
            // 5) keep right at the start of median barriers
            foreach (var (p, yaw) in MedianStarts) PlaceSign(SignKeepRight, p, new Vector2(Mathf.Sin(yaw * Mathf.Deg2Rad), Mathf.Cos(yaw * Mathf.Deg2Rad)));
        }

        static void CutThinMarkings(Rect box)
        {
            var keep = new List<HarborElement>(); var add = new List<HarborElement>();
            foreach (var e in E)
            {
                if (e.kind != "marking" || e.shape != "box") { keep.Add(e); continue; }
                var r = RectXZ(e);
                bool thin = Mathf.Min(r.width, r.height) <= 0.3f;
                if (!thin || !r.Overlaps(box)) { keep.Add(e); continue; }
                bool alongX = r.width >= r.height;
                if (alongX)
                {
                    if (r.xMin < box.xMin - 0.3f) add.Add(Piece(e, r.xMin, box.xMin, r.yMin, r.yMax));
                    if (r.xMax > box.xMax + 0.3f) add.Add(Piece(e, box.xMax, r.xMax, r.yMin, r.yMax));
                }
                else
                {
                    if (r.yMin < box.yMin - 0.3f) add.Add(Piece(e, r.xMin, r.xMax, r.yMin, box.yMin));
                    if (r.yMax > box.yMax + 0.3f) add.Add(Piece(e, r.xMin, r.xMax, box.yMax, r.yMax));
                }
            }
            E.Clear(); E.AddRange(keep); E.AddRange(add);
        }
    }
}
