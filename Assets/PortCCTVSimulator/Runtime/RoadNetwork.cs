using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static PortCCTV.HarborGeneric02;

namespace PortCCTV
{
    /// <summary>
    /// Layout clean-up run once at the end of a map build (user 2026-11-09: "길이 갑자기 중간에 나있거나 ... 디테일"):
    /// every road / lane / path strip end that meets no other paved surface is joined to the network, either by extending the
    /// strip straight on or by a perpendicular connector of the same width and surface, whichever is shorter, provided the
    /// connector stays out of the water, hills, buildings, house plots, fields and container stacks. Ends that cannot be joined
    /// within reach are left as they are and reported in the log. Axis-aligned strips only (all map roads are).
    /// Offline check of the result: Tools/check_layout.py on the HarborDump export.
    /// </summary>
    internal static class RoadNetwork
    {
        struct Rect
        {
            public float x0, x1, z0, z1; public int idx;
            public bool Overlaps(float ax0, float ax1, float az0, float az1) => ax0 < x1 && ax1 > x0 && az0 < z1 && az1 > z0;
        }

        static readonly HashSet<string> StripKinds = new HashSet<string> { "road", "lane", "path" };
        static readonly HashSet<string> TargetKinds = new HashSet<string>
        { "road", "lane", "path", "sidewalk", "parking", "quay", "pier", "breakwater", "land", "market", "loading_pad", "slipway", "shop_forecourt", "turning_head" };
        static readonly HashSet<string> PavedMats = new HashSet<string> { "road_asphalt", "lane_concrete", "concrete", "asphalt" };
        static readonly HashSet<string> BlockBoxKinds = new HashSet<string> { "pad", "field", "container", "laydown", "reefer_rack", "factory", "retaining", "tank", "apartment" };
        static readonly Dictionary<string, float> BlockPrefabRadius = new Dictionary<string, float>
        { { "building", 16f }, { "house", 7f }, { "greenhouse", 6f }, { "tank", 8f }, { "crane", 8f }, { "harbor_crane", 10f }, { "portal_crane", 10f }, { "fuel_station", 8f } };

        const float CellSize = 64f;

        public static int ConnectDeadEnds(HarborDefinition h)
        {
            var els = h.elements;
            var targets = new List<Rect>(); var blockers = new List<Rect>();
            var hills = new List<(Vector2 c, Vector2 r)>();
            for (int i = 0; i < els.Count; i++)
            {
                var e = els[i];
                if (e.shape == "prefab")
                {
                    if (e.kind == "hill") { hills.Add((new Vector2(e.center.x, e.center.z), new Vector2(e.size.x, e.size.z) * 0.42f)); continue; }
                    if (BlockPrefabRadius.TryGetValue(e.kind ?? "", out var r))
                        blockers.Add(new Rect { x0 = e.center.x - r, x1 = e.center.x + r, z0 = e.center.z - r, z1 = e.center.z + r, idx = i });
                    continue;
                }
                if (e.shape != "box" && e.shape != "cylinder") continue;
                if (!Axis(e)) continue;
                var rc = RectOf(e, i);
                if (IsTarget(e)) targets.Add(rc);
                else if (BlockBoxKinds.Contains(e.kind ?? "") || e.shape == "cylinder") blockers.Add(rc);
                else if (e.kind == "breakwater" && e.center.y + e.size.y / 2f > 4.5f) blockers.Add(rc);      // crown / wave walls
            }
            var tGrid = Grid(targets); var bGrid = Grid(blockers);

            // targets lying in the strip's own corridor (its sidewalks, kerbs, parallel lanes: the strip rect widened by 7 m each
            // side, not beyond its ends) do not count as a connection
            Rect corridor = default, selfR = default; bool corridorAlongX = false;
            bool Beside(Rect r)
            {
                float dx = r.x1 - r.x0, dz = r.z1 - r.z0;
                bool narrowParallel = corridorAlongX ? (dx >= dz && dz <= 15f) : (dz >= dx && dx <= 15f);
                return narrowParallel && r.Overlaps(corridor.x0, corridor.x1, corridor.z0, corridor.z1);
            }
            // a carriageway is joined to a carriageway (or a paved yard / quay / car park): a sidewalk beside the road it meets
            // is not a connection for a road, the link runs across it to the asphalt (the sidewalk gets a lowered crossing)
            bool roadSelf = false; float selfTop = 0f; Rect hitRect = default;
            bool HitsTarget(float x0, float x1, float z0, float z1, int self)
            {
                foreach (var r in Query(tGrid, targets, x0, x1, z0, z1))
                    if (r.idx != self && r.Overlaps(x0, x1, z0, z1) && !Beside(r) && !r.Overlaps(selfR.x0, selfR.x1, selfR.z0, selfR.z1)
                        && !(roadSelf && r.idx >= 0 && els[r.idx].kind == "sidewalk")
                        // a breakwater crown road a metre above the street is not reached by a flat link (2026-11-09: a link
                        // ran through the crown wall onto the crown at +0.94 m); breakwaters are joined by BreakwaterRamps
                        && !(r.idx >= 0 && els[r.idx].kind == "breakwater" && Mathf.Abs(els[r.idx].center.y + els[r.idx].size.y / 2f - selfTop) > 0.35f))
                    { hitRect = r; return true; }
                return false;
            }
            bool Blocked(float x0, float x1, float z0, float z1)
            {
                foreach (var r in Query(bGrid, blockers, x0, x1, z0, z1)) if (r.Overlaps(x0, x1, z0, z1)) return true;
                float cx = (x0 + x1) / 2f, cz = (z0 + z1) / 2f;
                foreach (var (c, rr) in hills)
                {
                    float dx = (cx - c.x) / rr.x, dz = (cz - c.y) / rr.y;
                    if (dx * dx + dz * dz < 1f) return true;
                }
                return h.InAnyWaterRegion(new Vector2(cx, cz));
            }

            int joined = 0, left = 0; var leftLog = new List<string>();
            var pendingCuts = new List<(UnityEngine.Rect r, Vector2 dir, string id)>();      // applied after the loop (cutting rebuilds the element list)
            // connectors laid so far: a new one mostly on top of an earlier one (two dead ends facing each other) is dropped,
            // and every laid connector counts as a target for the ends still to be processed
            var links = new List<Rect>();
            bool TryLink(bool ax, float a0, float a1, float b0, float b1)
            {
                var r = ax ? new Rect { x0 = a0, x1 = a1, z0 = b0, z1 = b1 } : new Rect { x0 = b0, x1 = b1, z0 = a0, z1 = a1 };
                float area = (r.x1 - r.x0) * (r.z1 - r.z0);
                foreach (var l in links)
                {
                    float ox = Mathf.Min(r.x1, l.x1) - Mathf.Max(r.x0, l.x0), oz = Mathf.Min(r.z1, l.z1) - Mathf.Max(r.z0, l.z0);
                    if (ox > 0f && oz > 0f && ox * oz > 0.5f * area) return false;
                }
                r.idx = -2 - links.Count;
                links.Add(r); targets.Add(r);
                for (int cx = Mathf.FloorToInt(r.x0 / CellSize); cx <= Mathf.FloorToInt(r.x1 / CellSize); cx++)
                    for (int cz = Mathf.FloorToInt(r.z0 / CellSize); cz <= Mathf.FloorToInt(r.z1 / CellSize); cz++) Add(tGrid, (cx, cz), targets.Count - 1);
                return true;
            }
            int n0 = els.Count;
            for (int i = 0; i < n0; i++)
            {
                var e = els[i];
                if (e.shape != "box" || !StripKinds.Contains(e.kind ?? "") || !IsTarget(e) || !Axis(e)) continue;
                bool alongX = e.size.x >= e.size.z;
                float len = alongX ? e.size.x : e.size.z, w = alongX ? e.size.z : e.size.x;
                if (len < 2.5f * w) continue;
                float reach = e.kind == "road" ? (w >= 12f ? 250f : 150f) : e.kind == "lane" ? 80f : 30f;
                float y0 = e.center.y - e.size.y / 2f, y1 = e.center.y + e.size.y / 2f;
                var self = RectOf(e, i); corridorAlongX = alongX; roadSelf = e.kind == "road"; selfTop = y1;
                selfR = new Rect { x0 = self.x0 + 0.3f, x1 = self.x1 - 0.3f, z0 = self.z0 + 0.3f, z1 = self.z1 - 0.3f };
                corridor = alongX ? new Rect { x0 = self.x0 + 0.5f, x1 = self.x1 - 0.5f, z0 = self.z0 - 7f, z1 = self.z1 + 7f }
                                  : new Rect { x0 = self.x0 - 7f, x1 = self.x1 + 7f, z0 = self.z0 + 0.5f, z1 = self.z1 - 0.5f };
                foreach (float sg in new[] { -1f, 1f })
                {
                    // end-cap centre and the across-axis centre line
                    float endA = (alongX ? e.center.x : e.center.z) + sg * len / 2f, c = alongX ? e.center.z : e.center.x;
                    if (Mathf.Abs(endA) > 1350f || Mathf.Abs(c) > 1350f) continue;                // map edge
                    float pr = Mathf.Max(0.6f, w * 0.3f);
                    var keepSelf = selfR; selfR = new Rect { x0 = 1e9f, x1 = 1e9f, z0 = 1e9f, z1 = 1e9f };
                    bool capped = HitsTarget2(alongX, endA + sg * 1f - pr, endA + sg * 1f + pr, c - pr, c + pr, i);
                    // an end already given a turning head (cut back at a hill foot) is a deliberate terminus
                    var endP = alongX ? new Vector2(endA, c) : new Vector2(c, endA);
                    if (_heads.Any(hd => ((hd.alongX ? new Vector2(hd.end, hd.c) : new Vector2(hd.c, hd.end)) - endP).sqrMagnitude < 4f)) capped = true;
                    selfR = keepSelf;
                    if (!capped && EndsInCrossing(els, i, alongX, endA, sg, c, w, y1)) capped = true;     // T-junction stem
                    if (capped) continue;

                    // (a) straight on
                    float best = float.MaxValue; int mode = 0; float side = 0f; Rect perpHit = default;
                    for (float d = 1f; d <= reach; d += 1f)
                    {
                        float a0 = endA + sg * (d - 1f), a1 = endA + sg * d;
                        if (BlockedA(alongX, Mathf.Min(a0, a1), Mathf.Max(a0, a1), c - w / 2f, c + w / 2f)) break;
                        if (HitsTarget2(alongX, Mathf.Min(a0, a1), Mathf.Max(a0, a1), c - w / 2f, c + w / 2f, i)) { best = d; mode = 1; break; }
                    }
                    // (b) perpendicular from the last `w` metres of the strip, to either side
                    foreach (float s in new[] { -1f, 1f })
                        for (float d = 1f; d <= Mathf.Min(reach, best - 1f); d += 1f)
                        {
                            float a0 = Mathf.Min(endA, endA - sg * w), a1 = Mathf.Max(endA, endA - sg * w);
                            float b0 = c + s * (w / 2f + d - 1f), b1 = c + s * (w / 2f + d);
                            if (BlockedA(alongX, a0, a1, Mathf.Min(b0, b1), Mathf.Max(b0, b1))) break;
                            if (HitsTarget2(alongX, a0, a1, Mathf.Min(b0, b1), Mathf.Max(b0, b1), i)) { best = d; mode = 2; side = s; perpHit = hitRect; break; }
                        }
                    if (mode == 0)
                    {
                        left++; if (leftLog.Count < 12) leftLog.Add($"{e.id}@({(alongX ? endA : c):F0},{(alongX ? c : endA):F0})");
                        // a street that stays a dead end ends in a paved turning head (hammerhead) with delineator posts, so it
                        // reads as a deliberate terminus and a vehicle can turn (2026-11-09: a row of barriers only hid the gap)
                        if (w >= 3.5f && (e.kind == "road" || e.kind == "lane"))
                        {
                            float hw = w / 2f + (w >= 6f ? 6f : 2.5f), d = w >= 6f ? 9f : 6f;
                            float a0 = Mathf.Min(endA, endA - sg * d), a1 = Mathf.Max(endA, endA - sg * d);
                            bool room = !BlockedA(alongX, a0, a1, c - hw, c - w / 2f) && !BlockedA(alongX, a0, a1, c + w / 2f, c + hw);
                            QueueHead(e, alongX, endA, c, sg, room ? hw : w / 2f, d, y0, y1);
                        }
                        continue;
                    }
                    if (best <= 2f) continue;
                    if (mode == 1)
                    {
                        float a0 = Mathf.Min(endA - sg * 0.5f, endA + sg * (best + 0.5f)), a1 = Mathf.Max(endA - sg * 0.5f, endA + sg * (best + 0.5f));
                        if (!TryLink(alongX, a0, a1, c - w / 2f, c + w / 2f)) continue;
                        if (roadSelf) pendingCuts.Add((alongX ? UnityEngine.Rect.MinMaxRect(a0, c - w / 2f, a1, c + w / 2f) : UnityEngine.Rect.MinMaxRect(c - w / 2f, a0, c + w / 2f, a1), alongX ? new Vector2(sg, 0f) : new Vector2(0f, sg), e.id));
                        AddStrip(e, alongX, a0, a1, c - w / 2f, c + w / 2f, y0, y1);
                    }
                    else
                    {
                        float a0 = Mathf.Min(endA, endA - sg * w), a1 = Mathf.Max(endA, endA - sg * w);
                        // a link wider than the carriageway it meets would leave a stub of asphalt against the verge beside
                        // that road: narrow it to the target's width (2026-11-09, CT link_road_73180 14 m into a 7 m road)
                        if (perpHit.idx >= 0 && (els[perpHit.idx].kind == "road" || els[perpHit.idx].kind == "lane"))
                        {
                            float ta0 = alongX ? perpHit.x0 : perpHit.z0, ta1 = alongX ? perpHit.x1 : perpHit.z1;
                            float o0 = Mathf.Max(a0, ta0), o1 = Mathf.Min(a1, ta1);
                            if (o1 - o0 >= 3f && o1 - o0 < a1 - a0 - 0.5f) { a0 = o0; a1 = o1; }
                        }
                        float b0 = c + side * (w / 2f - 0.5f), b1 = c + side * (w / 2f + best + 0.5f);
                        if (!TryLink(alongX, a0, a1, Mathf.Min(b0, b1), Mathf.Max(b0, b1))) continue;
                        if (roadSelf) pendingCuts.Add((alongX ? UnityEngine.Rect.MinMaxRect(a0, Mathf.Min(b0, b1), a1, Mathf.Max(b0, b1)) : UnityEngine.Rect.MinMaxRect(Mathf.Min(b0, b1), a0, Mathf.Max(b0, b1), a1), alongX ? new Vector2(0f, side) : new Vector2(side, 0f), e.id));
                        AddStrip(e, alongX, a0, a1, Mathf.Min(b0, b1), Mathf.Max(b0, b1), y0, y1);
                    }
                    joined++;
                }
            }
            foreach (var (r, dir, id) in pendingCuts) HarborParts.CutAndPave(r, dir, id, false);
            ApplyHeads();
            Debug.Log($"[RoadNetwork] {h.harbor_id}: joined {joined} dead ends, {left} left (no connection within reach): {string.Join(" ", leftLog)}");
            return joined;

            bool HitsTarget2(bool ax, float a0, float a1, float b0, float b1, int self)
                => ax ? HitsTarget(a0, a1, b0, b1, self) : HitsTarget(b0, b1, a0, a1, self);
            bool BlockedA(bool ax, float a0, float a1, float b0, float b1)
                => ax ? Blocked(a0, a1, b0, b1) : Blocked(b0, b1, a0, a1);
        }

        /// <summary>Analytic height of the map's hills (the same ProcNoise.HillHeight field the hill meshes are built from):
        /// keys hill_i / hill_L_i are ridged shapes, hill_m_i meadow domes. NaN where no hill stands.</summary>
        internal sealed class HillTerrain
        {
            readonly List<(Vector3 c, Vector3 s, Quaternion inv, int idx, bool ridged)> _h = new List<(Vector3, Vector3, Quaternion, int, bool)>();
            public HillTerrain(List<HarborElement> els)
            {
                foreach (var e in els)
                {
                    if (e.kind != "hill" || e.shape != "prefab" || string.IsNullOrEmpty(e.prefab)) continue;
                    var key = e.prefab; bool ridged = !key.StartsWith("hill_m_");
                    var num = key.Substring(key.LastIndexOf('_') + 1);
                    if (!int.TryParse(num, out var idx)) continue;
                    _h.Add((e.center, e.size, Quaternion.Inverse(e.Rotation), idx, ridged));
                }
            }
            public float Height(float x, float z)
            {
                float best = float.NaN;
                foreach (var (c, s, inv, idx, ridged) in _h)
                {
                    var l = inv * new Vector3(x - c.x, 0f, z - c.z);
                    float u = l.x / s.x + 0.5f, w = l.z / s.z + 0.5f;
                    if (u <= 0.01f || u >= 0.99f || w <= 0.01f || w >= 0.99f) continue;
                    float y = c.y + s.y * ProcNoise.HillHeight(idx, u, w, ridged);
                    if (float.IsNaN(best) || y > best) best = y;
                }
                return best;
            }
            /// <summary>True when the hill surface rises more than `clear` above `top` anywhere on the strip's cross-section at (x, z).</summary>
            public bool Covers(float x, float z, float top, float clear, Vector2 across, float halfW)
            {
                foreach (float t in new[] { 0f, -halfW, halfW })
                {
                    float y = Height(x + across.x * t, z + across.y * t);
                    if (!float.IsNaN(y) && y > top + clear) return true;
                }
                return false;
            }
        }

        static readonly List<(HarborElement src, bool alongX, float end, float c, float sg, float hw, float d, float y0, float y1)> _heads
            = new List<(HarborElement, bool, float, float, float, float, float, float, float)>();

        /// <summary>A paved turning head (hammerhead) at a street's deliberate end: queued while the element list is being walked,
        /// laid by ApplyHeads (footway cut out of it, head box, delineator posts across the end).</summary>
        static void QueueHead(HarborElement src, bool alongX, float end, float c, float sg, float hw, float d, float y0, float y1)
            => _heads.Add((src, alongX, end, c, sg, hw, d, y0, y1));

        static void ApplyHeads()
        {
            foreach (var (src, alongX, end, c, sg, hw, d, y0, y1) in _heads)
            {
                float a0 = Mathf.Min(end, end - sg * d), a1 = Mathf.Max(end, end - sg * d);
                var rr = alongX ? UnityEngine.Rect.MinMaxRect(a0, c - hw, a1, c + hw) : UnityEngine.Rect.MinMaxRect(c - hw, a0, c + hw, a1);
                // one head per street end (2026-11-10: the marina west coastal road got two identical heads from two passes)
                if (E.Any(o => o.kind == "turning_head" && Mathf.Abs(o.center.x - rr.center.x) < 1.5f && Mathf.Abs(o.center.z - rr.center.y) < 1.5f)) continue;
                // lane / centre / edge paint stops at the head; a transverse white end line marks the end of the carriageway
                CutPaint(alongX ? UnityEngine.Rect.MinMaxRect(a0 - 0.05f, c - hw - 0.5f, a1 + 0.05f, c + hw + 0.5f) : UnityEngine.Rect.MinMaxRect(c - hw - 0.5f, a0 - 0.05f, c + hw + 0.5f, a1 + 0.05f), alongX);
                float wHalf = alongX ? src.size.z / 2f : src.size.x / 2f, la = end - sg * d - sg * 0.2f;
                if (alongX) Box("marking", la - 0.2f, la + 0.2f, y1, y1 + 0.015f, c - wHalf + 0.2f, c + wHalf - 0.2f, "paint_white");
                else Box("marking", c - wHalf + 0.2f, c + wHalf - 0.2f, y1, y1 + 0.015f, la - 0.2f, la + 0.2f, "paint_white");
                E[E.Count - 1].id = Uid("road_end_line");
                // worn yellow hatching over the head (RoadLines026B, alpha-tested): marks the end zone as no-stopping
                if (alongX) Box("marking", a0 + 0.3f, a1 - 0.3f, y1, y1 + 0.012f, c - hw + 0.3f, c + hw - 0.3f, "paint_hatch_yellow");
                else Box("marking", c - hw + 0.3f, c + hw - 0.3f, y1, y1 + 0.012f, a0 + 0.3f, a1 - 0.3f, "paint_hatch_yellow");
                E[E.Count - 1].id = Uid("road_end_hatch");
                HarborParts.CutAndPave(rr, alongX ? new Vector2(0f, 1f) : new Vector2(1f, 0f), "head_" + src.id, false, false);
                if (alongX) Box("turning_head", a0, a1, y0, y1 - 0.005f, c - hw, c + hw, src.material);
                else Box("turning_head", c - hw, c + hw, y0, y1 - 0.005f, a0, a1, src.material);
                E[E.Count - 1].id = Uid("turning_head_" + src.id);
                for (float t = -hw + 1f; t <= hw - 1f; t += 2.5f)
                {
                    float a = end - sg * 0.6f;
                    var pp = alongX ? new Vector3(a, y1 + 0.45f, c + t) : new Vector3(c + t, y1 + 0.45f, a);
                    E.Add(new HarborElement { id = Uid("terminus_post"), kind = "bollard", shape = "cylinder", material = "beacon_white_band", center = pp, size = new Vector3(0.18f, 0.9f, 0.18f) });
                }
            }
            _heads.Clear();
        }

        /// <summary>Removes the parts of thin paint strips (road edge / centre lines: kind road or marking with a paint material,
        /// at most 0.3 m wide) running along `alongX` that lie inside `r`; the pieces outside are kept.</summary>
        static void CutPaint(UnityEngine.Rect r, bool alongX)
        {
            var keep = new List<HarborElement>(E.Count);
            foreach (var e in E)
            {
                if (e.shape != "box" || !(e.kind == "road" || e.kind == "marking") || e.material == null || !e.material.StartsWith("paint") || !Axis(e)) { keep.Add(e); continue; }
                var q = RectOf(e, -1); bool ex = q.x1 - q.x0 >= q.z1 - q.z0;
                if (ex != alongX || Mathf.Min(q.x1 - q.x0, q.z1 - q.z0) > 0.31f || !q.Overlaps(r.xMin, r.xMax, r.yMin, r.yMax)) { keep.Add(e); continue; }
                float u0 = ex ? q.x0 : q.z0, u1 = ex ? q.x1 : q.z1, c0 = ex ? r.xMin : r.yMin, c1 = ex ? r.xMax : r.yMax;
                void Piece(float p0, float p1, int k)
                {
                    if (p1 - p0 < 0.5f) return;
                    keep.Add(new HarborElement
                    {
                        id = e.id + "_e" + k, kind = e.kind, shape = "box", material = e.material,
                        center = ex ? new Vector3((p0 + p1) / 2f, e.center.y, e.center.z) : new Vector3(e.center.x, e.center.y, (p0 + p1) / 2f),
                        size = ex ? new Vector3(p1 - p0, e.size.y, q.z1 - q.z0) : new Vector3(q.x1 - q.x0, e.size.y, p1 - p0),
                    });
                }
                Piece(u0, Mathf.Min(u1, c0), 0); Piece(Mathf.Max(u0, c1), u1, 1);
            }
            E.Clear(); E.AddRange(keep);
        }

        static readonly HashSet<string> StubTargets = new HashSet<string>
        { "road", "lane", "driveway", "parking", "quay", "pier", "pad", "loading_pad", "market", "turning_head", "slipway", "shop_forecourt", "breakwater" };
        static readonly HashSet<string> HeadBlockers = new HashSet<string>
        { "building", "house", "apartment", "factory", "tank", "container", "greenhouse", "laydown", "reefer_rack", "fuel_station", "crane", "shed" };

        /// <summary>
        /// Deliberate street ends (2026-11-09, surface audit): a street that ends against a footway or open ground with no
        /// carriageway beyond (grid streets at the edge of a district, service roads at the shore) gets a paved hammerhead turning
        /// head over its last 9 m (6 m wide each side, narrower when a building is in the way) and a row of delineator posts
        /// across the end, so it reads and works as a terminus instead of a cut-off strip. Footpath lanes (< 3.5 m) may end
        /// as they are. Returns the number of heads queued (laid by ApplyHeads).
        /// </summary>
        public static int TerminateStubs(HarborDefinition h)
        {
            var els = h.elements; int n = 0;
            var corners = new List<(UnityEngine.Rect r, bool alongX)>();
            var tg = new List<Rect>(); var tgTop = new List<float>(); var bl = new List<Rect>();
            for (int i = 0; i < els.Count; i++)
            {
                var e = els[i];
                if (e.shape == "box" && Axis(e) && (StubTargets.Contains(e.kind ?? "") || (e.kind == "land" && e.material != null && PavedMats.Contains(e.material))))
                { var r = RectOf(e, tg.Count); tg.Add(r); tgTop.Add(e.center.y + e.size.y / 2f); }
                else if (HeadBlockers.Contains(e.kind ?? ""))
                {
                    if (e.shape == "prefab") { var rr = HarborParts.RectXZ(e); bl.Add(new Rect { x0 = rr.xMin, x1 = rr.xMax, z0 = rr.yMin, z1 = rr.yMax }); }
                    else if (e.shape == "box" || e.shape == "cylinder") bl.Add(RectOf(e, -1));
                }
            }
            // W-beam guardrails (4 m segments, ~0.5 m deep): a head must not be paved through the rail onto the slope beyond
            foreach (var e in els)
                if (e.shape == "prefab" && e.kind == "guard_rail")
                {
                    bool sw = Mathf.Abs(Mathf.Repeat(e.yaw_deg, 180f) - 90f) < 45f;
                    float gx = sw ? 0.3f : 2f, gz = sw ? 2f : 0.3f;
                    bl.Add(new Rect { x0 = e.center.x - gx, x1 = e.center.x + gx, z0 = e.center.z - gz, z1 = e.center.z + gz });
                }
            var tgGrid = Grid(tg); var blGrid = Grid(bl);
            var ground = els.Where(e => e.shape == "box" && Axis(e) && (e.kind == "land" || e.kind == "quay" || e.kind == "terrace" || e.kind == "promenade")
                                        && e.center.y + e.size.y / 2f > Deck - 0.2f).Select(e => RectOf(e, -1)).ToList();
            for (int i = 0; i < els.Count; i++)
            {
                var e = els[i];
                if (e.shape != "box" || !Axis(e) || !(e.kind == "road" || e.kind == "lane")) continue;
                if (e.material != "road_asphalt" && e.material != "lane_concrete") continue;
                var r = RectOf(e, -1); bool alongX = r.x1 - r.x0 >= r.z1 - r.z0;
                float len = alongX ? r.x1 - r.x0 : r.z1 - r.z0, w = alongX ? r.z1 - r.z0 : r.x1 - r.x0;
                if (w < (e.kind == "lane" ? 4f : 3.5f) || len < 2.5f * w) continue;     // village alleys (3.5 m lanes) just end
                float top = e.center.y + e.size.y / 2f, c = alongX ? (r.z0 + r.z1) / 2f : (r.x0 + r.x1) / 2f;
                foreach (float sg in new[] { -1f, 1f })
                {
                    float end = sg > 0 ? (alongX ? r.x1 : r.z1) : (alongX ? r.x0 : r.z0);
                    if (Mathf.Abs(end) > 1340f || Mathf.Abs(c) > 1340f) continue;
                    // anything drivable just beyond the end (or overlapping it, a crossing street) -> not a stub
                    float b0 = c - w * 0.3f, b1 = c + w * 0.3f, a0 = Mathf.Min(end, end + sg * 2.0f), a1 = Mathf.Max(end, end + sg * 2.0f);
                    bool joined = false;
                    foreach (var t in alongX ? Query(tgGrid, tg, a0, a1, b0, b1) : Query(tgGrid, tg, b0, b1, a0, a1))
                    {
                        if (ReferenceEquals(els[i], null)) break;
                        var te = t; if (Mathf.Abs(tgTop[t.idx] - top) > 0.15f) continue;
                        bool ov = alongX ? te.Overlaps(a0 + (sg > 0 ? 0.3f : 0f), a1 - (sg < 0 ? 0.3f : 0f), b0, b1) : te.Overlaps(b0, b1, a0 + (sg > 0 ? 0.3f : 0f), a1 - (sg < 0 ? 0.3f : 0f));
                        // the strip itself (and its own collinear pieces) do not count
                        bool self = alongX ? (te.z0 <= r.z0 + 0.05f && te.z1 >= r.z1 - 0.05f && te.x0 >= r.x0 - 0.05f && te.x1 <= r.x1 + 0.05f)
                                           : (te.x0 <= r.x0 + 0.05f && te.x1 >= r.x1 - 0.05f && te.z0 >= r.z0 - 0.05f && te.z1 <= r.z1 + 0.05f);
                        if (ov && !self) { joined = true; break; }
                    }
                    if (!joined && EndsInCrossing(els, i, alongX, end, sg, c, w, top)) joined = true;       // T-junction stem
                    if (!joined && EndsInCorner(els, i, alongX, end, sg, c, w, top, out var corner))
                    {
                        // L corner (2026-11-10: the fishing-port east coast road turns north into the access road at the breakwater
                        // root; it was read as a stub and got a head + posts against the crown wall). Paint stops at the corner.
                        corners.Add((corner, alongX));                                 // applied after the walk (E is els)
                        Trace.Add($"corner at {e.id} end ({(alongX ? end : c):F0},{(alongX ? c : end):F0})");
                        continue;
                    }
                    if (joined) continue;
                    if (_heads.Any(hd => ((hd.alongX ? new Vector2(hd.end, hd.c) : new Vector2(hd.c, hd.end)) - (alongX ? new Vector2(end, c) : new Vector2(c, end))).sqrMagnitude < 9f)) continue;
                    float hw = w / 2f + (w >= 6f ? 6f : 2.5f), d = w >= 6f ? 9f : 6f;
                    float h0 = Mathf.Min(end, end - sg * d), h1 = Mathf.Max(end, end - sg * d);
                    bool Blocked(float hwTry)
                    {
                        foreach (var bq in alongX ? Query(blGrid, bl, h0, h1, c - hwTry, c + hwTry) : Query(blGrid, bl, c - hwTry, c + hwTry, h0, h1))
                            if (alongX ? bq.Overlaps(h0, h1, c - hwTry, c + hwTry) : bq.Overlaps(c - hwTry, c + hwTry, h0, h1)) return true;
                        foreach (var aa in new[] { h0, h1 })
                            foreach (var bb in new[] { c - hwTry, c, c + hwTry })
                            {
                                var pt = alongX ? new Vector2(aa, bb) : new Vector2(bb, aa);
                                if (h.InAnyWaterRegion(pt)) return true;                                   // never out over the water
                                if (!ground.Any(gr => pt.x >= gr.x0 && pt.x <= gr.x1 && pt.y >= gr.z0 && pt.y <= gr.z1)) return true;   // nor off the land
                            }
                        return false;
                    }
                    if (Blocked(hw)) hw = w / 2f + 2.5f;
                    if (Blocked(hw)) hw = w / 2f;
                    if (Blocked(hw)) { Trace.Add($"no turning head at {e.id} (water)"); continue; }       // a road ending at the shore edge
                    QueueHead(e, alongX, end, c, sg, hw, d, e.center.y - e.size.y / 2f, top);
                    Trace.Add($"turning head {e.id} end ({(alongX ? end : c):F0},{(alongX ? c : end):F0}) half-width {hw:F1}");
                    n++;
                }
            }
            foreach (var (cr, ax) in corners) CutPaint(cr, ax);
            ApplyHeads();
            Debug.Log($"[RoadNetwork] {h.harbor_id}: {n} street ends finished with a turning head (deliberate termini), {corners.Count} corners");
            return n;
        }

        static readonly HashSet<string> BuriedBoxKinds = new HashSet<string> { "sidewalk", "kerb", "marking", "green_strip", "parking_lines", "wheel_stop", "guard_rail" };
        static readonly HashSet<string> BuriedPrefabKinds = new HashSet<string>
        { "street_light", "tree", "bench", "bin", "street", "signal", "car", "guard_rail", "barrier", "bus_stop", "sign", "planter" };

        /// <summary>
        /// Roads under hills (2026-11-09, surface audit): grid streets were laid over the whole grid rectangle and the backdrop
        /// hills were placed afterwards, so several streets ran into or right under a hill (the hill mesh drawn over the asphalt,
        /// a road visible as a cut-off strip at the hill foot). Each road / lane / driveway strip is cut where the analytic hill
        /// surface rises more than 0.25 m above it; pieces shorter than 20 m are dropped; a street left ending at a hill gets a
        /// paved turning head (hammerhead, kind "turning_head") so it reads as a deliberate dead end, and its sidewalks, kerbs,
        /// markings, lamps, trees and cars standing in the hill are removed. Returns the number of strips cut.
        /// </summary>
        public static int TrimUnderHills(HarborDefinition h)
        {
            var hills = new HillTerrain(h.elements);
            var els = h.elements; var keep = new List<HarborElement>(els.Count); var add = new List<HarborElement>();
            int cut = 0, dropped = 0, heads = 0, buried = 0;
            foreach (var e in els)
            {
                if (e.shape == "box" && Axis(e) && (e.kind == "road" || e.kind == "lane" || e.kind == "driveway")
                    && (e.material == "road_asphalt" || e.material == "lane_concrete"))
                {
                    var r = RectOf(e, 0); bool alongX = r.x1 - r.x0 >= r.z1 - r.z0;
                    float a0 = alongX ? r.x0 : r.z0, a1 = alongX ? r.x1 : r.z1, cc = alongX ? (r.z0 + r.z1) / 2f : (r.x0 + r.x1) / 2f;
                    float halfW = (alongX ? r.z1 - r.z0 : r.x1 - r.x0) / 2f, top = e.center.y + e.size.y / 2f;
                    var across = alongX ? Vector2.up : Vector2.right;
                    // uncovered intervals sampled every 2 m
                    var spans = new List<(float s0, float s1)>(); float? open = null; bool anyCover = false;
                    for (float a = a0; ; a = Mathf.Min(a + 2f, a1))
                    {
                        float x = alongX ? a : cc, z = alongX ? cc : a;
                        bool cov = hills.Covers(x, z, top, 0.25f, across, halfW);
                        anyCover |= cov;
                        if (!cov && open == null) open = a;
                        if (cov && open != null) { spans.Add((open.Value, Mathf.Max(open.Value, a - 2f))); open = null; }
                        if (a >= a1) break;
                    }
                    if (open != null) spans.Add((open.Value, a1));
                    if (!anyCover) { keep.Add(e); continue; }
                    cut++;
                    int k = 0;
                    // the longest kept piece keeps the strip's id (car-park driveways and junction records refer to it)
                    float longest = spans.Select(sp => sp.s1 - sp.s0).DefaultIfEmpty(0f).Max();
                    bool idKept = false;
                    foreach (var (s0raw, s1raw) in spans)
                    {
                        // pull a cut end 3 m back from the hill foot
                        float s0 = s0raw > a0 + 0.01f ? s0raw + 3f : s0raw, s1 = s1raw < a1 - 0.01f ? s1raw - 3f : s1raw;
                        if (s1 - s0 < 20f) { dropped++; continue; }
                        var piece = new HarborElement
                        {
                            id = (!idKept && Mathf.Abs((s1raw - s0raw) - longest) < 0.01f) ? e.id : e.id + "_h" + k, kind = e.kind, shape = "box", material = e.material, center = alongX
                                ? new Vector3((s0 + s1) / 2f, e.center.y, cc) : new Vector3(cc, e.center.y, (s0 + s1) / 2f),
                            size = alongX ? new Vector3(s1 - s0, e.size.y, 2f * halfW) : new Vector3(2f * halfW, e.size.y, s1 - s0),
                        };
                        if (piece.id == e.id) idKept = true;
                        k++;
                        add.Add(piece);
                        // turning head at each end cut by a hill (streets 6 m+; lanes / paths just end)
                        foreach (var (end, sg, wasCut) in new[] { (s0, -1f, s0raw > a0 + 0.01f), (s1, 1f, s1raw < a1 - 0.01f) })
                        {
                            if (!wasCut || e.kind != "road" || 2f * halfW < 6f) continue;
                            float hw = halfW + 6f, d = 9f;
                            float b0 = cc - hw, b1 = cc + hw, e0 = sg > 0 ? end - d : end, e1 = sg > 0 ? end : end + d;
                            bool clear = true;
                            for (float t = -hw; t <= hw && clear; t += 2f)
                                for (float a = e0; a <= e1 && clear; a += 2f)
                                {
                                    float x = alongX ? a : cc + t, z = alongX ? cc + t : a;
                                    float y = hills.Height(x, z); if (!float.IsNaN(y) && y > top + 0.25f) clear = false;
                                }
                            QueueHead(e, alongX, end, cc, sg, clear ? hw : halfW + 2.5f, d, e.center.y - e.size.y / 2f, top);
                        }
                    }
                    continue;
                }
                // roadside furniture buried in a hill
                bool buriedHere = false;
                if (e.shape == "box" && BuriedBoxKinds.Contains(e.kind ?? ""))
                {
                    float y = hills.Height(e.center.x, e.center.z);
                    buriedHere = !float.IsNaN(y) && y > e.center.y + e.size.y / 2f + 0.25f;
                }
                else if ((e.shape == "prefab" || e.shape == "billboard_tree") && BuriedPrefabKinds.Contains(e.kind ?? ""))
                {
                    float y = hills.Height(e.center.x, e.center.z);
                    // street trees on a slope are fine (scattered forest trees are set on the hill surface); only those set at
                    // the flat street level but under the hill surface are buried
                    buriedHere = !float.IsNaN(y) && y > e.center.y + 0.6f;
                }
                if (buriedHere) { buried++; continue; }
                keep.Add(e);
            }
            els.Clear(); els.AddRange(keep); els.AddRange(add);
            // car parks / yards whose access road was cut at a hill foot and that lie largely under the hill themselves are removed
            // with everything standing on them (bays, lines, wheel stops, vehicles, stock); a yard that is still open keeps its
            // driveway to the surviving road piece
            int lotsRemoved = 0;
            foreach (var lot in h.parking_lots.ToList())
            {
                int cov = 0, tot = 0;
                for (int i = 0; i <= 4; i++)
                    for (int j = 0; j <= 4; j++)
                    {
                        float x = Mathf.Lerp(lot.min.x, lot.max.x, i / 4f), z = Mathf.Lerp(lot.min.y, lot.max.y, j / 4f);
                        float y = hills.Height(x, z); tot++;
                        if (!float.IsNaN(y) && y > Deck + 0.3f) cov++;
                    }
                bool roadGone = lot.driveways.Count > 0 && lot.driveways.All(d => !els.Any(e => (e.id == d.road_id || (e.id != null && e.id.StartsWith(d.road_id + "_h")))
                    && RectOf(e, 0).Overlaps(d.min.x - 0.6f, d.max.x + 0.6f, d.min.y - 0.6f, d.max.y + 0.6f)));
                if (!roadGone || cov < tot * 0.15f) continue;
                var r = new UnityEngine.Rect(lot.min.x - 1f, lot.min.y - 1f, lot.max.x - lot.min.x + 2f, lot.max.y - lot.min.y + 2f);
                foreach (var d in lot.driveways) r = UnityEngine.Rect.MinMaxRect(Mathf.Min(r.xMin, d.min.x), Mathf.Min(r.yMin, d.min.y), Mathf.Max(r.xMax, d.max.x), Mathf.Max(r.yMax, d.max.y));
                els.RemoveAll(e => e.kind != "land" && e.kind != "hill" && e.kind != "road" && e.kind != "sidewalk" && e.kind != "kerb" && r.Contains(new Vector2(e.center.x, e.center.z)));
                h.parking_lots.Remove(lot);
                h.parking_bays.RemoveAll(b => b.lot_id == lot.id);
                HarborParts.Lots.RemoveAll(l => l.id == lot.id); HarborParts.Bays.RemoveAll(b => b.lot_id == lot.id);
                lotsRemoved++; Trace.Add($"lot {lot.id} removed: {cov}/{tot} samples under the hill, its road cut");
            }
            if (lotsRemoved > 0) Debug.Log($"[RoadNetwork] {h.harbor_id}: {lotsRemoved} car parks / yards under a hill removed with their contents");
            Debug.Log($"[RoadNetwork] {h.harbor_id}: roads under hills: {cut} strips cut at the hill foot ({dropped} short pieces dropped), {heads} turning heads, {buried} buried kerbs / lamps / trees / cars removed");
            return cut;
        }

        /// <summary>
        /// Breakwater roots (2026-11-09, surface audit): the crown road of a breakwater stands 1.0 m above the quay / land it
        /// starts from (crest 3.5 m vs deck 2.5 m) and its end simply met the quay edge: no vehicle or walker could get onto the
        /// breakwater, so none of its lights was reachable. Every crown-road end that meets a paved quay / land surface at deck
        /// level gets a 12 m concrete ramp (8.6 %) down onto it, the full crown-road width; footway, fences and small furniture in
        /// the ramp's footprint are cleared. Ends that already continue into another breakwater piece (corners, the container
        /// terminal's east ramp) are left alone. Returns the number of ramps.
        /// </summary>
        public static int BreakwaterRamps(HarborDefinition h)
        {
            var els = h.elements;
            var roads = els.Where(e => e.shape == "box" && e.kind == "breakwater" && e.material == "concrete" && e.id != null && e.id.EndsWith("_road") && Axis(e)).ToList();
            var hills = new HillTerrain(els);
            var ramps = new List<(UnityEngine.Rect rr, Vector2 dir, string id, Vector3 c, Vector3 size, Vector3 euler)>();
            foreach (var e in roads)
            {
                var r = RectOf(e, 0); bool alongX = r.x1 - r.x0 >= r.z1 - r.z0;
                float top = e.center.y + e.size.y / 2f, w = alongX ? r.z1 - r.z0 : r.x1 - r.x0, cc = alongX ? (r.z0 + r.z1) / 2f : (r.x0 + r.x1) / 2f;
                foreach (float sg in new[] { -1f, 1f })
                {
                    float end = sg > 0 ? (alongX ? r.x1 : r.z1) : (alongX ? r.x0 : r.z0);
                    var probe = alongX ? new Vector2(end + sg * 1.5f, cc) : new Vector2(cc, end + sg * 1.5f);
                    // continues as breakwater (corner piece / existing ramp)?
                    bool cont = els.Any(o => o != e && o.kind == "breakwater" && o.shape == "box" && o.center.y + o.size.y / 2f > 2.0f && Contains(o, probe));
                    if (cont) continue;
                    var land = els.FirstOrDefault(o => o.shape == "box" && (o.kind == "quay" || o.kind == "land" || o.kind == "promenade") && Contains(o, probe)
                                                       && Mathf.Abs(o.center.y + o.size.y / 2f - Deck) < 0.15f);
                    if (land == null) continue;
                    float landTop = land.center.y + land.size.y / 2f + 0.04f, dh = top - landTop, L = 12f;
                    if (dh < 0.2f) continue;
                    // a root that runs into a hill gets no ramp (it would be buried): Vegetation routes a footpath over the ground
                    bool buried = false;
                    for (float t = 1f; t <= 12f && !buried; t += 2f)
                    {
                        var pp = alongX ? new Vector2(end + sg * t, cc) : new Vector2(cc, end + sg * t);
                        buried = hills.Covers(pp.x, pp.y, Deck, 0.3f, alongX ? Vector2.up : Vector2.right, w / 2f);
                    }
                    if (buried) { Trace.Add($"breakwater ramp skipped at {e.id} end {end:F1}: hill"); continue; }
                    float tilt = Mathf.Atan2(dh, L) * Mathf.Rad2Deg, slope = Mathf.Sqrt(L * L + dh * dh);
                    var mid = alongX ? new Vector3(end + sg * L / 2f, (top + landTop) / 2f - 0.5f, cc) : new Vector3(cc, (top + landTop) / 2f - 0.5f, end + sg * L / 2f);
                    // descending outward: about z for x-runs (+x end down needs a negative angle), about x for z-runs
                    var euler = alongX ? new Vector3(0f, 0f, sg > 0 ? -tilt : tilt) : new Vector3(sg > 0 ? tilt : -tilt, 0f, 0f);
                    var size = alongX ? new Vector3(slope, 1.0f, w) : new Vector3(w, 1.0f, slope);
                    float a0 = Mathf.Min(end, end + sg * L), a1 = Mathf.Max(end, end + sg * L);
                    var rr = alongX ? UnityEngine.Rect.MinMaxRect(a0, cc - w / 2f, a1, cc + w / 2f) : UnityEngine.Rect.MinMaxRect(cc - w / 2f, a0, cc + w / 2f, a1);
                    ramps.Add((rr, alongX ? new Vector2(sg, 0f) : new Vector2(0f, sg), e.id, mid, size, euler));
                }
            }
            foreach (var (rr, dir, id, c, size, euler) in ramps)
            {
                HarborParts.CutAndPave(rr, dir, "ramp_" + id, false, false);
                // small furniture / gear standing where the ramp lands
                var pad = new UnityEngine.Rect(rr.xMin - 0.5f, rr.yMin - 0.5f, rr.width + 1f, rr.height + 1f);
                E.RemoveAll(o => (o.shape == "prefab" || o.shape == "billboard_tree") && RampClearKinds.Contains(o.kind ?? "") && pad.Contains(new Vector2(o.center.x, o.center.z)));
                BoxC("breakwater", c, size, "concrete", 0f, euler);
                E[E.Count - 1].id = "ramp_" + id;
                Trace.Add($"breakwater ramp {id} x{rr.xMin:F1}..{rr.xMax:F1} z{rr.yMin:F1}..{rr.yMax:F1}");
            }
            Debug.Log($"[RoadNetwork] {h.harbor_id}: {ramps.Count} breakwater root ramps (crown road -> quay)");
            return ramps.Count;
        }

        static readonly HashSet<string> RampClearKinds = new HashSet<string>
        { "fishing_gear", "bench", "bollard", "street_light", "tree", "bin", "lifebuoy", "barrel", "fence", "car", "crane" };

        static bool Contains(HarborElement o, Vector2 p)
        {
            var rr = RectOf(o, 0);
            return Axis(o) && p.x >= rr.x0 && p.x <= rr.x1 && p.y >= rr.z0 && p.y <= rr.z1;
        }

        static readonly HashSet<string> CarriagewayCut = new HashSet<string> { "sidewalk", "kerb", "green_strip" };
        static readonly HashSet<string> CarriagewayPrefabCut = new HashSet<string>
        { "street_light", "tree", "bench", "bin", "street", "planter", "bus_stop", "hydrant", "bollard", "fence", "tetrapod", "fishing_gear", "guard_rail" };

        /// <summary>
        /// Clears carriageways (2026-11-09, surface audit): every boulevard lays its sidewalks and kerbs over its whole length,
        /// so where two boulevards cross, each one's footway ran straight across the other's carriageway, 10-11 cm above the
        /// asphalt (a kerb wall across every junction; the boxes overlapped in plan, so the old plan-view check counted the
        /// streets as joined). Footway boxes (sidewalk / kerb / verge) lying on a carriageway at the same level are cut back to
        /// the carriageway edges, and street furniture / trees standing on the carriageway are removed. A road on a different
        /// level (bridge, ramp) is left alone. Returns the number of footway boxes cut.
        /// </summary>
        static readonly HashSet<string> FurnitureKinds = new HashSet<string>
        { "power_pole", "street_light", "signal", "street", "bench", "bin", "hydrant", "planter", "road_sign" };
        static readonly HashSet<string> FurnitureMovable = new HashSet<string> { "signal", "street", "road_sign" };

        /// <summary>
        /// Street furniture off carriageways and house plots (2026-11-10 photo: utility poles standing in village lanes and a
        /// pole / lamp inside a house plot). Runs after every pass that adds carriageway (dead-end links, turning heads):
        /// a pole, lamp, signal, sign, bench or bin whose base lies inside a road / lane / driveway / turning head at its level
        /// is moved sideways to just outside the nearest long edge (signals and signs, which belong to the street) or removed
        /// (the rest); a pole or lamp inside a house plot pad or a house footprint is removed. Returns the number changed.
        /// </summary>
        public static int ClearStreetFurniture(HarborDefinition h)
        {
            var els = h.elements;
            var cw = new List<Rect>(); var cwTop = new List<float>();
            var plots = new List<(Vector2 c, Vector2 half, float yaw)>();
            foreach (var e in els)
            {
                if (e.shape == "box" && Axis(e) && (e.kind == "road" || e.kind == "lane" || e.kind == "driveway" || e.kind == "turning_head")
                    && (e.material == "road_asphalt" || e.material == "lane_concrete" || e.material == "paving" || e.material == "asphalt"))
                { cw.Add(RectOf(e, cw.Count)); cwTop.Add(e.center.y + e.size.y / 2f); }
                else if (e.shape == "box" && e.kind == "pad")
                    plots.Add((new Vector2(e.center.x, e.center.z), new Vector2(e.size.x / 2f, e.size.z / 2f), e.yaw_deg + e.euler_deg.y));
                else if (e.shape == "prefab" && e.kind == "house" && e.prefab != null)
                {
                    Vector2 fp = Vector2.zero;
                    foreach (var k in HillFeatures.HouseKinds) if (k.key == e.prefab) fp = new Vector2(k.w, k.d);
                    if (e.prefab == "bld_villa_kr") fp = new Vector2(27f, 25f);
                    if (fp != Vector2.zero) plots.Add((new Vector2(e.center.x, e.center.z), new Vector2(fp.x * Mathf.Abs(e.size.x) / 2f, fp.y * Mathf.Abs(e.size.z) / 2f), e.yaw_deg));
                }
            }
            var g = Grid(cw);
            int OnCw(float x, float z, float y, float margin)
            {
                foreach (var rr in Query(g, cw, x, x, z, z))
                    if (x > rr.x0 + margin && x < rr.x1 - margin && z > rr.z0 + margin && z < rr.z1 - margin && Mathf.Abs(y - cwTop[rr.idx]) < 0.6f) return rr.idx;
                return -1;
            }
            bool InPlot(float x, float z)
            {
                foreach (var (c, half, yaw) in plots)
                {
                    if (Mathf.Abs(x - c.x) > half.x + half.y || Mathf.Abs(z - c.y) > half.x + half.y) continue;
                    var l = Quaternion.Euler(0f, -yaw, 0f) * new Vector3(x - c.x, 0f, z - c.y);
                    if (Mathf.Abs(l.x) < half.x + 0.2f && Mathf.Abs(l.z) < half.y + 0.2f) return true;
                }
                return false;
            }
            // shop-street lots touching a carriageway are dropped whole; village / town house models whose footprint (+0.5 m
            // eaves) reaches into a carriageway are pushed back off it (<= 3 m, onto free ground) or dropped
            var badLots = new HashSet<string>(); int lotsDropped = 0, housesMoved = 0, housesDropped = 0;
            bool Overlaps(UnityEngine.Rect f, float y, float margin)
            {
                foreach (var rr in Query(g, cw, f.xMin, f.xMax, f.yMin, f.yMax))
                    if (rr.Overlaps(f.xMin + margin, f.xMax - margin, f.yMin + margin, f.yMax - margin) && y > cwTop[rr.idx] - 0.6f && y < cwTop[rr.idx] + 0.6f) return true;
                return false;
            }
            foreach (var e in els)
            {
                if (e.id == null || !e.id.StartsWith(HarborParts.LotPrefix) || !(e.kind == "building" || e.kind == "facade_detail" || e.kind == "awning" || e.kind == "market")) continue;
                int us = e.id.IndexOf('_'); if (us < 0) continue;
                var f = e.shape == "prefab" ? HarborParts.RectXZ(e) : HarborParts.RectXZ(e);
                if (e.shape == "prefab" && e.kind == "facade_detail") f = UnityEngine.Rect.MinMaxRect(e.center.x - 1.5f, e.center.z - 1.5f, e.center.x + 1.5f, e.center.z + 1.5f);
                float yb = e.shape == "prefab" ? e.center.y : e.center.y - e.size.y / 2f;
                if (yb < Deck + 1.0f && Overlaps(f, yb, 0.3f)) badLots.Add(e.id.Substring(0, us + 1));
            }
            if (badLots.Count > 0)
            {
                int before = els.Count;
                els.RemoveAll(e => e.id != null && e.id.StartsWith(HarborParts.LotPrefix) && badLots.Contains(e.id.Substring(0, e.id.IndexOf('_') + 1)));
                lotsDropped = badLots.Count;
            }
            foreach (var e in els.ToList())
            {
                if (e.shape != "prefab" || !(e.kind == "house" || e.kind == "building") || e.prefab == null) continue;
                Vector2 fp = Vector2.zero;
                foreach (var k in HillFeatures.HouseKinds) if (k.key == e.prefab) fp = new Vector2(k.w, k.d);
                if (e.prefab == "bld_villa_kr") fp = new Vector2(27f, 25f);
                if (e.prefab == "house_jp03") fp = new Vector2(11.84f, 11.48f);
                if (fp == Vector2.zero) continue;
                bool sw = Mathf.Abs(Mathf.Repeat(e.yaw_deg, 180f) - 90f) < 45f;
                // + roof eaves: the hip-roof models are ~3 m wider than their table width (house_hip_blue 8 m -> 11 m rendered)
                float eave = e.prefab.StartsWith("house_hip") ? 3.0f : 1.5f;
                float hx = (sw ? fp.y : fp.x) * Mathf.Abs(e.size.x) / 2f + eave, hz = (sw ? fp.x : fp.y) * Mathf.Abs(e.size.z) / 2f + eave;
                var f = UnityEngine.Rect.MinMaxRect(e.center.x - hx, e.center.z - hz, e.center.x + hx, e.center.z + hz);
                if (!Overlaps(f, e.center.y, 0.2f)) continue;
                bool done = false;
                foreach (var d in new[] { new Vector2(1, 0), new Vector2(-1, 0), new Vector2(0, 1), new Vector2(0, -1) })
                {
                    for (float step = 0.5f; step <= 3.01f && !done; step += 0.5f)
                    {
                        var g2 = new UnityEngine.Rect(f.position + d * step, f.size);
                        if (Overlaps(g2, e.center.y, 0.0f)) continue;
                        if (els.Any(o => o != e && o.shape == "prefab" && (o.kind == "house" || o.kind == "building") && HarborParts.RectXZ(o).Overlaps(UnityEngine.Rect.MinMaxRect(g2.xMin + 2f, g2.yMin + 2f, g2.xMax - 2f, g2.yMax - 2f)))) break;
                        e.center += new Vector3(d.x * step, 0f, d.y * step); done = true;
                    }
                    if (done) break;
                }
                if (done) housesMoved++; else { els.Remove(e); housesDropped++; }
            }
            Debug.Log($"[RoadNetwork] {h.harbor_id}: buildings off carriageways: {lotsDropped} shop lots dropped, {housesMoved} houses moved, {housesDropped} dropped");
            var keep = new List<HarborElement>(els.Count); int moved = 0, removed = 0;
            foreach (var e in els)
            {
                // trees on a carriageway laid after ClearCarriageways (dead-end link lanes) are removed too
                if (e.kind == "tree" && (e.shape == "prefab" || e.shape == "billboard_tree") && OnCw(e.center.x, e.center.z, e.center.y, 0.25f) >= 0) { removed++; continue; }
                if (e.shape != "prefab" || !FurnitureKinds.Contains(e.kind ?? "")) { keep.Add(e); continue; }
                float x = e.center.x, z = e.center.z, y = e.center.y;
                int hit = OnCw(x, z, y, 0.25f);
                if (hit >= 0)
                {
                    if (FurnitureMovable.Contains(e.kind))
                    {
                        var rr = cw[hit]; bool alongX = rr.x1 - rr.x0 >= rr.z1 - rr.z0;
                        float lo = alongX ? rr.z0 - 0.7f : rr.x0 - 0.7f, hi = alongX ? rr.z1 + 0.7f : rr.x1 + 0.7f, cur = alongX ? z : x;
                        bool placed = false;
                        foreach (float t in Mathf.Abs(cur - lo) <= Mathf.Abs(cur - hi) ? new[] { lo, hi } : new[] { hi, lo })
                        {
                            float nx = alongX ? x : t, nz = alongX ? t : z;
                            if (OnCw(nx, nz, y, -0.2f) >= 0 || InPlot(nx, nz)) continue;
                            e.center = new Vector3(nx, y, nz); placed = true; break;
                        }
                        if (placed) { moved++; keep.Add(e); continue; }
                    }
                    removed++; continue;
                }
                if ((e.kind == "power_pole" || e.kind == "street_light") && InPlot(x, z)) { removed++; continue; }
                keep.Add(e);
            }
            els.Clear(); els.AddRange(keep);
            Debug.Log($"[RoadNetwork] {h.harbor_id}: street furniture off carriageways / plots: {moved} moved, {removed} removed");
            return moved + removed;
        }

        public static int ClearCarriageways(HarborDefinition h)
        {
            var els = h.elements;
            var roads = new List<Rect>(); var roadTop = new List<float>();
            for (int i = 0; i < els.Count; i++)
            {
                var e = els[i];
                if (e.shape != "box" || !Axis(e) || !(e.kind == "road" || e.kind == "lane" || e.kind == "driveway")) continue;
                if (e.material != "road_asphalt" && e.material != "lane_concrete" && e.material != "paving") continue;
                var r = RectOf(e, roads.Count); roads.Add(r); roadTop.Add(e.center.y + e.size.y / 2f);
            }
            var g = Grid(roads);
            var keep = new List<HarborElement>(els.Count); int cut = 0, removed = 0;
            foreach (var e in els)
            {
                if (e.shape == "box" && CarriagewayCut.Contains(e.kind ?? "") && Axis(e))
                {
                    var r = RectOf(e, -1); float top = e.center.y + e.size.y / 2f;
                    var pieces = new List<Rect> { r }; bool changed = false;
                    foreach (var rr in Query(g, roads, r.x0, r.x1, r.z0, r.z1))
                    {
                        float rt = roadTop[rr.idx];
                        if (top < rt + 0.02f || top - rt > 0.4f) continue;                        // footway not above this road surface
                        // carriageway interior (2 cm in from its edges, so the footway along its own road is untouched)
                        var c = new Rect { x0 = rr.x0 + 0.02f, x1 = rr.x1 - 0.02f, z0 = rr.z0 + 0.02f, z1 = rr.z1 - 0.02f };
                        var next = new List<Rect>();
                        foreach (var pc in pieces)
                        {
                            if (!pc.Overlaps(c.x0, c.x1, c.z0, c.z1)) { next.Add(pc); continue; }
                            changed = true;
                            // rectangle difference: up to four pieces (left, right, below, above the carriageway)
                            if (pc.x0 < c.x0) next.Add(new Rect { x0 = pc.x0, x1 = c.x0, z0 = pc.z0, z1 = pc.z1 });
                            if (pc.x1 > c.x1) next.Add(new Rect { x0 = c.x1, x1 = pc.x1, z0 = pc.z0, z1 = pc.z1 });
                            float ix0 = Mathf.Max(pc.x0, c.x0), ix1 = Mathf.Min(pc.x1, c.x1);
                            if (pc.z0 < c.z0) next.Add(new Rect { x0 = ix0, x1 = ix1, z0 = pc.z0, z1 = c.z0 });
                            if (pc.z1 > c.z1) next.Add(new Rect { x0 = ix0, x1 = ix1, z0 = c.z1, z1 = pc.z1 });
                        }
                        pieces = next;
                    }
                    if (!changed) { keep.Add(e); continue; }
                    cut++;
                    int k = 0;
                    foreach (var pc in pieces)
                    {
                        if (pc.x1 - pc.x0 < 0.15f || pc.z1 - pc.z0 < 0.15f) continue;
                        keep.Add(new HarborElement
                        {
                            id = e.id + "_cw" + (k++), kind = e.kind, shape = "box", material = e.material,
                            center = new Vector3((pc.x0 + pc.x1) / 2f, e.center.y, (pc.z0 + pc.z1) / 2f), size = new Vector3(pc.x1 - pc.x0, e.size.y, pc.z1 - pc.z0),
                        });
                    }
                    continue;
                }
                if (e.shape == "box" && (e.kind == "house" || e.kind == "shed"))
                {
                    // village houses / gear sheds placed over an alley or a street (fishing town cross alleys)
                    var hr = RectOf(e, -1); bool onRoad = false;
                    foreach (var rr in Query(g, roads, hr.x0, hr.x1, hr.z0, hr.z1))
                    {
                        float ox = Mathf.Min(hr.x1, rr.x1) - Mathf.Max(hr.x0, rr.x0), oz = Mathf.Min(hr.z1, rr.z1) - Mathf.Max(hr.z0, rr.z0);
                        float dy = e.center.y - e.size.y / 2f - roadTop[rr.idx]; if (ox > 0.5f && oz > 0.5f && dy > -0.5f && dy < 15f) { onRoad = true; break; }   // walls and their roof slab
                    }
                    if (onRoad) { removed++; continue; }
                }
                if (e.shape == "prefab" && e.kind == "greenhouse")
                {
                    // vinyl tunnel 7 m x 40 m (long axis z, scaled by size.z): a farm plot laid over a road put tunnels on it
                    float hz = 20f * Mathf.Abs(e.size.z), hx = 3.5f * Mathf.Abs(e.size.x);
                    bool onRoad = false;
                    foreach (var rr in Query(g, roads, e.center.x - hx, e.center.x + hx, e.center.z - hz, e.center.z + hz))
                        if (rr.Overlaps(e.center.x - hx, e.center.x + hx, e.center.z - hz, e.center.z + hz) && Mathf.Abs(e.center.y + 0.06f - roadTop[rr.idx]) < 0.5f) { onRoad = true; break; }
                    if (onRoad) { removed++; continue; }
                }
                if ((e.shape == "prefab" || e.shape == "billboard_tree") && CarriagewayPrefabCut.Contains(e.kind ?? ""))
                {
                    bool onRoad = false;
                    foreach (var rr in Query(g, roads, e.center.x, e.center.x, e.center.z, e.center.z))
                        if (e.center.x > rr.x0 + 0.3f && e.center.x < rr.x1 - 0.3f && e.center.z > rr.z0 + 0.3f && e.center.z < rr.z1 - 0.3f
                            && (Mathf.Abs(e.center.y - roadTop[rr.idx]) < 0.5f || (e.kind == "tetrapod" && e.center.y > roadTop[rr.idx] - 4f && e.center.y < roadTop[rr.idx] + 1f))) { onRoad = true; break; }
                    // armour units are ~2.6 m across: one whose body reaches 0.3 m into a carriageway is on the road
                    if (!onRoad && e.kind == "tetrapod")
                        foreach (var rr in Query(g, roads, e.center.x - 1.3f, e.center.x + 1.3f, e.center.z - 1.3f, e.center.z + 1.3f))
                            if (rr.Overlaps(e.center.x - 1.0f, e.center.x + 1.0f, e.center.z - 1.0f, e.center.z + 1.0f) && e.center.y > roadTop[rr.idx] - 3f) { onRoad = true; break; }
                    if (onRoad) { removed++; continue; }
                }
                keep.Add(e);
            }
            els.Clear(); els.AddRange(keep);
            Debug.Log($"[RoadNetwork] {h.harbor_id}: cleared carriageways: {cut} footway boxes cut back from crossing roads, {removed} trees / street furniture removed from carriageways");
            return cut;
        }

        static readonly HashSet<string> MouthCuttable = new HashSet<string>
        { "sidewalk", "kerb", "guard_rail", "fence", "green_strip", "marking", "parking_lines", "wheel_stop", "path" };
        static readonly HashSet<string> MouthIgnore = new HashSet<string>
        { "road", "lane", "driveway", "land", "quay", "pad", "tree", "street_light", "street", "signal", "lamp", "sign", "bollard", "barrier", "hill", "field" };

        /// <summary>
        /// Opens junction mouths (2026-11-09, surface audit): where a road / lane / driveway strip ends a few metres short of
        /// another carriageway at the same height and only a sidewalk, kerb, verge, guardrail or fence lies between them (the
        /// through road's footway running on across a T-junction), that strip of footway is cut out across the mouth, the gap is
        /// paved with the ending road's surface at its height, and a zebra crossing is painted where the footway was. Before
        /// this the side street met a kerb: the boxes touched in plan (so the old AABB check passed) but no car could turn in.
        /// Runs before ConnectDeadEnds. Returns the number of mouths opened.
        /// </summary>
        /// <summary>Per-map log of every junction mouth, link, cut and turning head (written by SurfaceAudit next to its report).</summary>
        internal static readonly List<string> Trace = new List<string>();

        public static int OpenJunctionMouths(HarborDefinition h)
        {
            var els = h.elements; var hills = new HillTerrain(els);
            var strips = new List<(HarborElement e, Rect r, float top)>();
            for (int i = 0; i < els.Count; i++)
            {
                var e = els[i];
                if (e.shape != "box" || !Axis(e) || !(e.kind == "road" || e.kind == "lane" || e.kind == "driveway")) continue;
                if (e.material != "road_asphalt" && e.material != "lane_concrete") continue;
                strips.Add((e, RectOf(e, i), e.center.y + e.size.y / 2f));
            }
            var sRects = strips.Select(t => t.r).ToList(); var sGrid = Grid(sRects);
            var mouths = new List<(UnityEngine.Rect g, Vector2 dir, HarborElement src, bool alongX, float a0, float a1, float b0, float b1)>();
            int skipped = 0; var skipLog = new List<string>();
            var allRects = new List<Rect>(); for (int i = 0; i < els.Count; i++) allRects.Add(els[i].shape == "box" || els[i].shape == "cylinder" ? RectOf(els[i], i) : default);
            var aGrid = Grid(allRects.Select((r, i) => els[i].shape == "box" || els[i].shape == "cylinder" ? r : new Rect { x0 = 1e9f, x1 = 1e9f, z0 = 1e9f, z1 = 1e9f, idx = i }).ToList());
            foreach (var (e, r, top) in strips)
            {
                bool alongX = (r.x1 - r.x0) >= (r.z1 - r.z0);
                float len = alongX ? r.x1 - r.x0 : r.z1 - r.z0, w = alongX ? r.z1 - r.z0 : r.x1 - r.x0;
                if (len < 1.5f * w || w < 2.5f) continue;
                float c0 = alongX ? r.z0 : r.x0, c1 = alongX ? r.z1 : r.x1;
                foreach (float sg in new[] { -1f, 1f })
                {
                    float endA = sg > 0 ? (alongX ? r.x1 : r.z1) : (alongX ? r.x0 : r.z0);
                    float q0 = Mathf.Min(endA, endA + sg * 90f), q1 = Mathf.Max(endA, endA + sg * 90f);
                    // nearest other carriageway beyond the end that spans the whole mouth, at the same height
                    float best = float.MaxValue;
                    foreach (var o in (alongX ? Query(sGrid, sRects, q0, q1, c0, c1) : Query(sGrid, sRects, c0, c1, q0, q1)).Distinct())
                    {
                        var (oe, orr, otop) = strips[IndexOfRect(strips, o)];
                        if (oe == e || Mathf.Abs(otop - top) > 0.12f) continue;
                        float oa0 = alongX ? orr.x0 : orr.z0, oa1 = alongX ? orr.x1 : orr.z1, ob0 = alongX ? orr.z0 : orr.x0, ob1 = alongX ? orr.z1 : orr.x1;
                        if (ob0 > c0 + 0.05f || ob1 < c1 - 0.05f) continue;                         // must span the mouth across
                        if (Mathf.Min(orr.x1 - orr.x0, orr.z1 - orr.z0) < 3.4f) continue;           // a footpath lane is not a carriageway
                        float near = sg > 0 ? oa0 - endA : endA - oa1;                              // gap along the axis
                        if (near < -0.05f) { if (sg > 0 ? oa1 > endA : oa0 < endA) best = Mathf.Min(best, 0f); continue; }
                        if (near < best) best = near;
                    }
                    if (best <= 0.25f || best > 90f) continue;                                        // already touching / nothing near
                    float a0 = sg > 0 ? endA : endA - best, a1 = sg > 0 ? endA + best : endA;
                    var g = alongX ? UnityEngine.Rect.MinMaxRect(a0, c0, a1, c1) : UnityEngine.Rect.MinMaxRect(c0, a0, c1, a1);
                    // only footway furniture may lie in the gap (never a building, a container, a wall or water)
                    string blocker = null;
                    foreach (var orr in Query(aGrid, allRects, g.xMin, g.xMax, g.yMin, g.yMax))
                    {
                        var o = els[orr.idx];
                        if (o == e || MouthCuttable.Contains(o.kind ?? "") || MouthIgnore.Contains(o.kind ?? "")) continue;
                        if (orr.Overlaps(g.xMin + 0.05f, g.xMax - 0.05f, g.yMin + 0.05f, g.yMax - 0.05f) && o.center.y + o.size.y / 2f > top + 0.05f) { blocker = o.kind; break; }
                    }
                    if (blocker == null)
                        foreach (var o in els)
                            if (o.shape == "prefab" && !MouthIgnore.Contains(o.kind ?? "") && FootprintXZ(o).Overlaps(g)) { blocker = o.kind; break; }
                    if (blocker == null && best > 9f)
                    {
                        // a longer gap (a grid street stopping short of the next arterial) is bridged only over open flat ground
                        for (float a = a0; a <= a1 && blocker == null; a += 2f)
                            if (hills.Covers(alongX ? a : (c0 + c1) / 2f, alongX ? (c0 + c1) / 2f : a, top, 0.25f, alongX ? Vector2.up : Vector2.right, (c1 - c0) / 2f)) blocker = "hill";
                        if (blocker == null && h.InAnyWaterRegion(g.center)) blocker = "water";
                    }
                    if (blocker != null) { skipped++; if (skipLog.Count < 10) skipLog.Add($"{e.id}->{blocker}"); Trace.Add($"mouth-skip {e.id} end {endA:F1} gap {best:F1} blocked by {blocker}"); continue; }
                    if (h.InAnyWaterRegion(g.center)) continue;
                    mouths.Add((g, alongX ? new Vector2(sg, 0f) : new Vector2(0f, sg), e, alongX, a0, a1, c0, c1));
                }
            }
            // two strips facing each other across the same footway produce the same gap: keep the first
            var done = new List<UnityEngine.Rect>();
            int opened = 0;
            foreach (var m in mouths)
            {
                if (done.Any(d => Intersect(d, m.g) > 0.5f * m.g.width * m.g.height)) continue;
                done.Add(m.g);
                Trace.Add($"mouth {m.src.id} x{m.g.xMin:F1}..{m.g.xMax:F1} z{m.g.yMin:F1}..{m.g.yMax:F1}");
                HarborParts.CutAndPave(m.g, m.dir, "mouth_" + m.src.id, false, false);
                float y0 = m.src.center.y - m.src.size.y / 2f, y1 = m.src.center.y + m.src.size.y / 2f;
                AddStrip(m.src, m.alongX, m.a0 - 0.05f, m.a1 + 0.05f, m.b0, m.b1, y0, y1);
                E[E.Count - 1].id = "mouth_" + m.src.id + "_" + opened;
                // zebra crossing across the mouth where the footway was (stripes 0.5 m wide every 1 m)
                if (m.a1 - m.a0 >= 1.5f && m.a1 - m.a0 <= 9f)
                    for (float b = m.b0 + 0.6f; b + 0.5f < m.b1 - 0.4f; b += 1.0f)
                    {
                        if (m.alongX) Box("marking", m.a0 + 0.3f, m.a1 - 0.3f, y1, y1 + 0.02f, b, b + 0.5f, "paint_white");
                        else Box("marking", b, b + 0.5f, y1, y1 + 0.02f, m.a0 + 0.3f, m.a1 - 0.3f, "paint_white");
                    }
                opened++;
            }
            Debug.Log($"[RoadNetwork] {h.harbor_id}: opened {opened} junction mouths across footways, {skipped} gaps left (something other than footway in between): {string.Join(" ", skipLog)}");
            return opened;
        }

        /// <summary>Plan footprint of a prefab element: vinyl greenhouses are 7 x 40 m tunnels (long axis z, scaled), the rest use
        /// the per-kind radius of HarborParts.RectXZ.</summary>
        static UnityEngine.Rect FootprintXZ(HarborElement o)
        {
            if (o.kind == "greenhouse")
            {
                float hz = 20f * Mathf.Abs(o.size.z), hx = 3.5f * Mathf.Abs(o.size.x);
                return UnityEngine.Rect.MinMaxRect(o.center.x - hx, o.center.z - hz, o.center.x + hx, o.center.z + hz);
            }
            return HarborParts.RectXZ(o);
        }

        static int IndexOfRect(List<(HarborElement e, Rect r, float top)> strips, Rect r)
        {
            for (int k = 0; k < strips.Count; k++) if (strips[k].r.idx == r.idx) return k;
            return -1;
        }

        static float Intersect(UnityEngine.Rect a, UnityEngine.Rect b)
        {
            float w = Mathf.Min(a.xMax, b.xMax) - Mathf.Max(a.xMin, b.xMin), hh = Mathf.Min(a.yMax, b.yMax) - Mathf.Max(a.yMin, b.yMin);
            return w > 0f && hh > 0f ? w * hh : 0f;
        }

        static void AddStrip(HarborElement src, bool alongX, float a0, float a1, float b0, float b1, float y0, float y1)
        {
            if (alongX) Box(src.kind, a0, a1, y0, y1, b0, b1, src.material);
            else Box(src.kind, b0, b1, y0, y1, a0, a1, src.material);
            E[E.Count - 1].id = "link_" + E[E.Count - 1].id;
        }

        static bool Axis(HarborElement e)
        {
            float y = Mathf.Repeat(e.yaw_deg + e.euler_deg.y, 90f);
            return (y < 0.5f || y > 89.5f) && Mathf.Abs(e.euler_deg.x) < 0.5f && Mathf.Abs(e.euler_deg.z) < 0.5f;
        }

        /// <summary>
        /// True when a strip end is the stem of a T-junction: its last half metre, widened 0.6 m past both its edges, lies on
        /// another paved road / lane at the same height (the stem runs into the crossing street and stops at its far kerb, so
        /// the cells beyond are that street's sidewalk). Such ends are joined; they get no link and no turning head
        /// (2026-11-09: turning heads had been laid across the crossing street at 61 marina T-junctions).
        /// </summary>
        static bool EndsInCrossing(List<HarborElement> els, int self, bool alongX, float end, float sg, float c, float w, float top)
        {
            float a = end - sg * 0.5f;
            bool On(float b)
            {
                float px = alongX ? a : b, pz = alongX ? b : a;
                for (int k = 0; k < els.Count; k++)
                {
                    var o = els[k];
                    if (k == self || o.shape != "box" || !(o.kind == "road" || o.kind == "lane") || o.material == null || !PavedMats.Contains(o.material)) continue;
                    if (Mathf.Abs(o.center.y + o.size.y / 2f - top) > 0.06f || !Axis(o)) continue;
                    var r = RectOf(o, k);
                    if (px >= r.x0 && px <= r.x1 && pz >= r.z0 && pz <= r.z1) return true;
                }
                return false;
            }
            return On(c - w / 2f - 0.6f) && On(c + w / 2f + 0.6f);
        }

        /// <summary>True when another carriageway at the same level touches the strip's last `w` metres from the side (an L corner
        /// or a street leaving sideways at the end); `corner` is the end square, for cutting the paint there.</summary>
        static bool EndsInCorner(List<HarborElement> els, int self, bool alongX, float end, float sg, float c, float w, float top, out UnityEngine.Rect corner)
        {
            float a0 = Mathf.Min(end, end - sg * w), a1 = Mathf.Max(end, end - sg * w);
            corner = alongX ? UnityEngine.Rect.MinMaxRect(a0, c - w / 2f, a1, c + w / 2f) : UnityEngine.Rect.MinMaxRect(c - w / 2f, a0, c + w / 2f, a1);
            var probe = alongX ? UnityEngine.Rect.MinMaxRect(a0 + 0.5f, c - w / 2f - 1.0f, a1 - 0.5f, c + w / 2f + 1.0f)
                               : UnityEngine.Rect.MinMaxRect(c - w / 2f - 1.0f, a0 + 0.5f, c + w / 2f + 1.0f, a1 - 0.5f);
            var me = RectOf(els[self], self);
            for (int k = 0; k < els.Count; k++)
            {
                var o = els[k];
                if (k == self || o.shape != "box" || !(o.kind == "road" || o.kind == "lane") || o.material == null || !PavedMats.Contains(o.material)) continue;
                if (Mathf.Abs(o.center.y + o.size.y / 2f - top) > 0.06f || !Axis(o)) continue;
                var r = RectOf(o, k);
                if (!r.Overlaps(probe.xMin, probe.xMax, probe.yMin, probe.yMax)) continue;
                bool oAlongX = r.x1 - r.x0 >= r.z1 - r.z0;
                if (oAlongX == alongX) continue;                                         // collinear pieces / parallel strips
                float ow = oAlongX ? r.z1 - r.z0 : r.x1 - r.x0;
                if (ow < 3.4f) continue;                                                 // a footpath lane is no way on
                // the other street must leave the end square sideways (not lie across the strip's middle further back)
                bool beyondSide = alongX ? (r.z1 > me.z1 + 0.5f || r.z0 < me.z0 - 0.5f) : (r.x1 > me.x1 + 0.5f || r.x0 < me.x0 - 0.5f);
                // ... and it must reach the end line itself (a street joining a few metres before the end leaves a stub, which
                // still needs a head: fishing-port village end (-980,-28), lane at -976..-972)
                float e0 = Mathf.Min(end, end - sg * w) + 0.5f, e1 = Mathf.Max(end, end - sg * w) - 0.5f;   // whole end square side
                bool atEnd = alongX ? (r.x0 <= e0 && r.x1 >= e1) : (r.z0 <= e0 && r.z1 >= e1);
                if (beyondSide && atEnd) return true;
            }
            return false;
        }

        static bool IsTarget(HarborElement e)
        {
            if (!TargetKinds.Contains(e.kind ?? "")) return false;
            if (e.kind == "road" || e.kind == "breakwater" || e.kind == "land") return e.material != null && PavedMats.Contains(e.material);
            return true;
        }

        static Rect RectOf(HarborElement e, int i)
        {
            bool swap = Mathf.Abs(Mathf.Repeat(e.yaw_deg + e.euler_deg.y, 180f) - 90f) < 1f;
            float hx = (swap ? e.size.z : e.size.x) / 2f, hz = (swap ? e.size.x : e.size.z) / 2f;
            return new Rect { x0 = e.center.x - hx, x1 = e.center.x + hx, z0 = e.center.z - hz, z1 = e.center.z + hz, idx = i };
        }

        static Dictionary<(int, int), List<int>> Grid(List<Rect> rs)
        {
            var g = new Dictionary<(int, int), List<int>>();
            for (int k = 0; k < rs.Count; k++)
            {
                var r = rs[k];
                if ((r.x1 - r.x0) * (r.z1 - r.z0) > 4e6f) { Add(g, (int.MinValue, 0), k); continue; }   // huge (land) boxes
                for (int cx = Mathf.FloorToInt(r.x0 / CellSize); cx <= Mathf.FloorToInt(r.x1 / CellSize); cx++)
                    for (int cz = Mathf.FloorToInt(r.z0 / CellSize); cz <= Mathf.FloorToInt(r.z1 / CellSize); cz++) Add(g, (cx, cz), k);
            }
            return g;
        }

        static void Add(Dictionary<(int, int), List<int>> g, (int, int) key, int k)
        {
            if (!g.TryGetValue(key, out var l)) g[key] = l = new List<int>();
            l.Add(k);
        }

        static IEnumerable<Rect> Query(Dictionary<(int, int), List<int>> g, List<Rect> rs, float x0, float x1, float z0, float z1)
        {
            if (g.TryGetValue((int.MinValue, 0), out var big)) foreach (var k in big) yield return rs[k];
            for (int cx = Mathf.FloorToInt(x0 / CellSize); cx <= Mathf.FloorToInt(x1 / CellSize); cx++)
                for (int cz = Mathf.FloorToInt(z0 / CellSize); cz <= Mathf.FloorToInt(z1 / CellSize); cz++)
                    if (g.TryGetValue((cx, cz), out var l)) foreach (var k in l) yield return rs[k];
        }
    }
}
