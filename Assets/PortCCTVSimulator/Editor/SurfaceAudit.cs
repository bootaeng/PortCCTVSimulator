using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Unity.Collections;
using UnityEngine;

namespace PortCCTV.EditorTools
{
    /// <summary>
    /// Dev tool (2026-11-09, user: "AABB가 닿거나 검사 로그가 통과했다는 이유만으로 연결 완료라 하지 마"): audits the paved network of
    /// each themed map on the FINAL built scene (harbor boxes, prefabs, hills, vegetation, lighthouse paths, parking cut-outs),
    /// by casting a ray down every 1 m over the whole map and reading the top surface that is actually there.
    ///   * vehicle graph: 4-neighbour cells on drivable surfaces (road, lane, driveway, parking, quay, paved land/pads, gates,
    ///     breakwater crowns) whose heights differ by at most 0.13 m (a kerb or a step blocks a car);
    ///   * pedestrian graph: drivable + sidewalks, paths, piers, pontoons, gangways, lighthouse paths / landings and stairs,
    ///     steps up to 0.26 m (a kerb), up to 0.55 m per metre on stairs;
    ///   * every road / lane / driveway strip must lie in the main vehicle component along its centre line (a gap, a step, a
    ///     strip buried under a hill or covered by something else shows up as cells outside it);
    ///   * every strip end is classified: joins the network, map edge, deliberate terminus (turning head / gate), or dead end;
    ///   * every parking lot must be in the main vehicle component;
    ///   * every lighthouse (breakwater lights and headland landmark) must have its door ring in the main pedestrian
    ///     component, or in a component that holds a boat landing (island lights);
    ///   * thin paved surfaces (ribbons, slabs) with no support within 0.8 m below are reported as floating.
    /// Output: Logs/surface_audit/&lt;map&gt;.json (report) and &lt;map&gt;.bin (grid: class u8, flags u8, height f32 per cell) for the
    /// top-down plots of Tools/plot_surface_audit.py. Batch: -executeMethod PortCCTV.EditorTools.SurfaceAudit.RunBatch
    /// </summary>
    public static class SurfaceAudit
    {
        const float Half = 1350f, Cell = 1f;
        const int N = (int)(2 * Half / Cell);

        // cell classes
        const byte None = 0, Ground = 1, Drive = 2, Walk = 3, Block = 4, Stair = 5, WaterC = 6;

        static readonly HashSet<string> DriveKinds = new HashSet<string>
        { "road", "lane", "driveway", "link", "parking", "parking_lines", "marking", "quay", "pad", "gate", "loading_pad", "market", "fuel_station",
          "bridge", "causeway", "apron", "yard", "shop_forecourt", "slipway", "lighthouse_landing", "turning_head" };
        static readonly HashSet<string> WalkKinds = new HashSet<string>
        { "sidewalk", "path", "kerb", "pier", "pontoon", "gangway", "promenade", "plaza", "walkway", "terrace_pad", "lighthouse_path",
          "lighthouse_base_pad", "coping", "wheel_stop", "breakwater_walk" };
        static readonly HashSet<string> StairKinds = new HashSet<string> { "stair", "stairs", "lighthouse_stair", "ramp" };
        static readonly HashSet<string> GroundKinds = new HashSet<string> { "hill", "field", "beach", "terrace", "retaining", "bund", "rock", "coast", "headland" };
        static readonly HashSet<string> PavedMats = new HashSet<string> { "road_asphalt", "lane_concrete", "concrete", "paving", "asphalt", "sidewalk_paving" };

        public static void RunBatch()
        {
            int code = 0;
            try { Run(); }
            catch (System.Exception ex) { Debug.LogException(ex); code = 1; }
            UnityEditor.EditorApplication.Exit(code);
        }

        public static void Run()
        {
            var dir = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "surface_audit");
            Directory.CreateDirectory(dir);
            var w = new SimWorld();
            w.Initialize();
            var summary = new StringBuilder();
            foreach (var id in new[] { ContainerTerminal01.Id, FishingPort01.Id, Marina01.Id })
            {
                                w.Apply(SimulatorApp.DefaultScenarioFor(id));
                var rep = Audit(w, id, dir);
                summary.AppendLine(rep);
                Debug.Log("[SurfaceAudit] " + rep);
            }
            w.Dispose();
            File.WriteAllText(Path.Combine(dir, "summary.txt"), summary.ToString());
        }

        static string KindOf(Collider c, Transform harborRoot)
        {
            var t = c.transform;
            // runtime-added objects named for what they are (vegetation / parking / lighthouse access)
            for (var p = t; p != null; p = p.parent)
            {
                var n = p.name;
                if (n.StartsWith("lighthouse_path")) return "lighthouse_path";
                if (n.StartsWith("lighthouse_landing")) return "lighthouse_landing";
                if (n.StartsWith("lighthouse_base_pad")) return "lighthouse_base_pad";
                if (n.StartsWith("lighthouse_stair") || n.StartsWith("stair")) return "stair";
                if (n.StartsWith("ramp_")) return "ramp";
                if (p.parent == harborRoot) return n;
            }
            return t.name;
        }

        static string MatOf(Collider c)
        {
            var r = c.GetComponent<Renderer>();
            if (r == null || r.sharedMaterial == null) return "";
            var n = r.sharedMaterial.name;
            return n.StartsWith("M_") ? n.Substring(2) : n;
        }

        class Report
        {
            public string map;
            public int cells_drive, cells_walk, main_vehicle_cells, main_walk_cells, vehicle_components;
            public List<string> strips_off_network = new List<string>();
            public List<string> dead_ends = new List<string>();
            public List<string> termini = new List<string>();
            public List<string> footpath_ends = new List<string>();
            public List<string> map_edge_ends = new List<string>();
            public List<string> parking_off_network = new List<string>();
            public List<string> lighthouses = new List<string>();
            public List<string> floating = new List<string>();
            public List<string> steps = new List<string>();
            public List<string> road_obstructions = new List<string>();
            public int strips_checked, strip_ends_joined, t_junctions;
            public List<string> kinds = new List<string>();
        }

        static string Audit(SimWorld w, string mapId, string dir)
        {
            Physics.SyncTransforms();
            RoadNetwork.Trace.Clear();
            var def = HarborLibrary.Get(mapId);
            var wdef = w.Harbor;
            Debug.Log($"[SurfaceAudit] {mapId}: world definition {wdef.elements.Count} elements, rebuilt definition {def.elements.Count} elements (must be identical: deterministic build)");
            if (wdef.elements.Count != def.elements.Count)
            {
                var a = new HashSet<string>(wdef.elements.Select(e => $"{e.kind}|{e.center.x:F1}|{e.center.z:F1}|{e.size.x:F1}|{e.size.z:F1}"));
                var b = new HashSet<string>(def.elements.Select(e => $"{e.kind}|{e.center.x:F1}|{e.center.z:F1}|{e.size.x:F1}|{e.size.z:F1}"));
                Debug.Log("[SurfaceAudit] only in world: " + string.Join(" ; ", a.Except(b).Take(15)));
                Debug.Log("[SurfaceAudit] only in rebuilt: " + string.Join(" ; ", b.Except(a).Take(15)));
            }
            def = wdef;                                                  // audit what was actually built
            var harborRoot = GameObject.Find("Harbor_" + mapId)?.transform;
            var all = Object.FindObjectsByType<Collider>(FindObjectsSortMode.None).Where(c => c.enabled && c.gameObject.activeInHierarchy).ToList();
            var cls = new Dictionary<int, byte>(); var kindOf = new Dictionary<int, string>();
            var byId = new Dictionary<int, Collider>(); foreach (var c in all) byId[c.GetInstanceID()] = c;
            var vesselsT = GameObject.Find("Vessels")?.transform;
            foreach (var c in all)
            {
                string k = KindOf(c, harborRoot); string m = MatOf(c);
                byte b;
                if (vesselsT != null && c.transform.IsChildOf(vesselsT)) b = Block;
                else if (StairKinds.Contains(k)) b = Stair;
                // a drivable kind is a road surface only when it is paved (gate canopies, market roofs, bridge trusses, wave walls are not)
                else if (k == "marking" || k == "parking_lines" || k == "crane_rail") b = Drive;      // paint / flush rails on a paved surface
                else if (DriveKinds.Contains(k)) b = k == "lighthouse_landing" || PavedMats.Contains(m) ? Drive : Block;
                else if (k == "land" || k == "breakwater" || k == "terrace") b = PavedMats.Contains(m) ? Drive : (k == "breakwater" ? Block : Ground);
                else if (WalkKinds.Contains(k)) b = Walk;
                else if (GroundKinds.Contains(k) || k.StartsWith("hill")) b = Ground;
                else b = Block;
                cls[c.GetInstanceID()] = b; kindOf[c.GetInstanceID()] = k;
            }

            // ---- pass 1: surfaces only (paved, stairs, ground). Overhead objects (tree crowns, cranes, gantries, signs, cars)
            // must not hide the surface below them.
            int total = N * N;
            var disabled = new List<Collider>();
            foreach (var c in all) if (cls[c.GetInstanceID()] == Block) { c.enabled = false; disabled.Add(c); }
            Physics.SyncTransforms();
            var cmds = new NativeArray<RaycastCommand>(total, Allocator.TempJob);
            var hits = new NativeArray<RaycastHit>(total, Allocator.TempJob);
            var qp = new QueryParameters(Physics.DefaultRaycastLayers, false, QueryTriggerInteraction.Ignore, false);
            for (int j = 0; j < N; j++)
                for (int i = 0; i < N; i++)
                    cmds[j * N + i] = new RaycastCommand(new Vector3(-Half + (i + 0.5f) * Cell, 400f, -Half + (j + 0.5f) * Cell), Vector3.down, qp, 450f);
            RaycastCommand.ScheduleBatch(cmds, hits, 4096).Complete();
            var cl = new byte[total]; var hy = new float[total]; var kid = new int[total];
            var kinds = new List<string>(); var kindIdx = new Dictionary<string, int>();
            var colAt = new int[total];
            int KindId(string k) { if (!kindIdx.TryGetValue(k, out var ki)) { ki = kinds.Count; kinds.Add(k); kindIdx[k] = ki; } return ki; }
            for (int q = 0; q < total; q++)
            {
                var h = hits[q];
                if (h.colliderInstanceID == 0) { cl[q] = WaterC; hy[q] = 0f; kid[q] = -1; continue; }
                int iid = h.colliderInstanceID; colAt[q] = iid;
                hy[q] = h.point.y;
                cl[q] = cls.TryGetValue(iid, out var b) ? b : Block;
                kid[q] = KindId(kindOf.TryGetValue(iid, out var kk) ? kk : "?");
                if (cl[q] == Ground && h.point.y < def.water_reference_y + 0.05f) cl[q] = WaterC;
            }
            cmds.Dispose(); hits.Dispose();
            foreach (var c in disabled) c.enabled = true;
            Physics.SyncTransforms();

            // ---- pass 2: obstruction. From 2.4 m above each paved cell, down to 0.2 m above it: a building, container, barrier,
            // fence, wall, tank ... standing on the surface makes the cell impassable. Parked / moving vehicles, poles, signs and
            // bollards (footprint < 0.6 m2) do not close a way.
            var pavedQ = new List<int>();
            for (int q = 0; q < total; q++) if (cl[q] == Drive || cl[q] == Walk || cl[q] == Stair) pavedQ.Add(q);
            var cmd2 = new NativeArray<RaycastCommand>(pavedQ.Count * 2, Allocator.TempJob);
            var hit2 = new NativeArray<RaycastHit>(pavedQ.Count * 2, Allocator.TempJob);
            var qpBack = new QueryParameters(Physics.DefaultRaycastLayers, false, QueryTriggerInteraction.Ignore, true);
            var ovl = new NativeArray<OverlapBoxCommand>(pavedQ.Count, Allocator.TempJob);
            var ovr = new NativeArray<ColliderHit>(pavedQ.Count * 4, Allocator.TempJob);
            for (int k = 0; k < pavedQ.Count; k++)
            {
                int q = pavedQ[k];
                var c0 = new Vector3(-Half + (q % N + 0.5f) * Cell, hy[q], -Half + (q / N + 0.5f) * Cell);
                cmd2[2 * k] = new RaycastCommand(c0 + Vector3.up * 2.4f, Vector3.down, qp, 2.2f);          // something standing on it, top below 2.4 m
                cmd2[2 * k + 1] = new RaycastCommand(c0 + Vector3.up * 0.25f, Vector3.up, qpBack, 2.0f);    // inside a mesh volume (containers, buildings)
                ovl[k] = new OverlapBoxCommand(c0 + Vector3.up * 1.1f, new Vector3(0.3f, 0.8f, 0.3f), Quaternion.identity, qp);  // inside a box volume
            }
            RaycastCommand.ScheduleBatch(cmd2, hit2, 4096).Complete();
            OverlapBoxCommand.ScheduleBatch(ovl, ovr, 1024, 4).Complete();
            var obstructedBy = new Dictionary<string, int>();
            var roadObs = new Dictionary<string, (int n, float x, float z)>(); var roadObsKind = new Dictionary<string, string>();
            bool Obstacle(int iid, out string kk2)
            {
                kk2 = null;
                if (iid == 0 || !cls.TryGetValue(iid, out var b) || b != Block) return false;
                kk2 = kindOf[iid];
                if (kk2 == "car" || kk2 == "truck" || kk2 == "vehicle" || kk2 == "bus" || kk2 == "tree" || kk2 == "parking_vehicle"
                    || kk2 == "street_light" || kk2 == "signal" || kk2 == "street" || kk2 == "sign" || kk2 == "light_mast") return false;   // poles: thin in reality
                var bc = byId[iid].bounds;
                return bc.size.x * bc.size.z >= 0.6f;
            }
            for (int k = 0; k < pavedQ.Count; k++)
            {
                string k2 = null; bool ob = Obstacle(hit2[2 * k].colliderInstanceID, out k2);
                if (!ob) ob = Obstacle(hit2[2 * k + 1].colliderInstanceID, out k2);
                for (int r = 0; r < 4 && !ob; r++) ob = Obstacle(ovr[k * 4 + r].instanceID, out k2);
                if (!ob) continue;
                int q = pavedQ[k];
                var under = kinds[kid[q]];
                if (under == "road" || under == "lane" || under == "driveway" || under == "turning_head")
                {
                    int iidO = hit2[2 * k].colliderInstanceID != 0 && Obstacle(hit2[2 * k].colliderInstanceID, out _) ? hit2[2 * k].colliderInstanceID
                             : hit2[2 * k + 1].colliderInstanceID != 0 && Obstacle(hit2[2 * k + 1].colliderInstanceID, out _) ? hit2[2 * k + 1].colliderInstanceID : -1;
                    string nm = iidO != -1 && byId.TryGetValue(iidO, out var oc) ? $"{oc.name} [{(oc.transform.parent ? oc.transform.parent.name : "")}] bounds {oc.bounds.min:F0}..{oc.bounds.max:F0}" : k2;
                    // one entry per name and 40 m tile, so overlaps at different places with the same collider name are listed apart
                    nm += $" #{Mathf.FloorToInt((q % N) * Cell / 40f)},{Mathf.FloorToInt((q / N) * Cell / 40f)}";
                    roadObs[nm] = roadObs.TryGetValue(nm, out var ro) ? (ro.n + 1, ro.x, ro.z) : (1, -Half + (q % N + 0.5f) * Cell, -Half + (q / N + 0.5f) * Cell);
                    roadObsKind[nm] = k2 + " on " + under;
                }
                cl[q] = Block; kid[q] = KindId(k2);
                obstructedBy[k2] = (obstructedBy.TryGetValue(k2, out var o) ? o : 0) + 1;
            }
            ovl.Dispose(); ovr.Dispose();
            var roadObstructions = roadObs.OrderByDescending(kv => kv.Value.n).Select(kv => $"{roadObsKind[kv.Key]} '{kv.Key}' near ({kv.Value.x:F0},{kv.Value.z:F0}): {kv.Value.n} road cells").ToList();
            cmd2.Dispose(); hit2.Dispose();
            Debug.Log($"[SurfaceAudit] {mapId}: paved cells obstructed by " + string.Join(", ", obstructedBy.OrderByDescending(kv => kv.Value).Take(15).Select(kv => kv.Key + " " + kv.Value)));

            // ---- components
            int[] vc = Components(cl, hy, c => c == Drive, 0.13f, 0.13f);
            int[] pc = Components(cl, hy, c => c == Drive || c == Walk || c == Stair, 0.26f, 0.62f);
            int Main(int[] comp, System.Func<int, bool> want)
            {
                var cnt = new Dictionary<int, int>();
                for (int q = 0; q < total; q++) if (comp[q] >= 0 && want(q)) cnt[comp[q]] = (cnt.TryGetValue(comp[q], out var v) ? v : 0) + 1;
                return cnt.Count == 0 ? -1 : cnt.OrderByDescending(kv => kv.Value).First().Key;
            }
            int roadKind = kindIdx.TryGetValue("road", out var rk) ? rk : -9;
            int mainV = Main(vc, q => kid[q] == roadKind);
            // main pedestrian component = the one holding most of the main vehicle component
            int mainP = Main(pc, q => vc[q] == mainV);
            var rep = new Report { map = mapId, road_obstructions = roadObstructions };
            for (int q = 0; q < total; q++)
            {
                if (cl[q] == Drive) rep.cells_drive++;
                if (cl[q] == Walk) rep.cells_walk++;
                if (vc[q] == mainV) rep.main_vehicle_cells++;
                if (pc[q] == mainP) rep.main_walk_cells++;
            }
            rep.vehicle_components = vc.Where(v => v >= 0).Distinct().Count();

            int Q(Vector2 p)
            {
                int i = Mathf.FloorToInt((p.x + Half) / Cell), j = Mathf.FloorToInt((p.y + Half) / Cell);
                if (i < 0 || j < 0 || i >= N || j >= N) return -1;
                return j * N + i;
            }
            bool OnCarriageway(Vector2 p, HarborElement self, float top)
            {
                foreach (var o in def.elements)
                {
                    if (o == self || o.shape != "box" || !(o.kind == "road" || o.kind == "lane")) continue;
                    if (o.material != "road_asphalt" && o.material != "lane_concrete" && o.material != "concrete" && o.material != "asphalt") continue;
                    if (Mathf.Abs(o.center.y + o.size.y / 2f - top) > 0.06f) continue;
                    var inv = Quaternion.Inverse(Quaternion.Euler(0f, o.yaw_deg + o.euler_deg.y, 0f));
                    var l = inv * new Vector3(p.x - o.center.x, 0f, p.y - o.center.z);
                    if (Mathf.Abs(l.x) <= Mathf.Abs(o.size.x) / 2f && Mathf.Abs(l.z) <= Mathf.Abs(o.size.z) / 2f) return true;
                }
                return false;
            }
            string What(int q) => q < 0 ? "outside" : cl[q] == WaterC ? "water" : (kid[q] >= 0 ? kinds[kid[q]] : "?") + $"@{hy[q]:F2}";

            // ---- strips: centre line in the main vehicle component, ends joined
            var landings = new HashSet<int>();
            for (int q = 0; q < total; q++) if (kid[q] >= 0 && kinds[kid[q]] == "lighthouse_landing") landings.Add(pc[q]);
            foreach (var e in def.elements)
            {
                if (e.shape != "box" || !(e.kind == "road" || e.kind == "lane" || e.kind == "driveway")) continue;
                if (e.kind == "road" && e.material != "road_asphalt") continue;
                bool alongX = e.size.x >= e.size.z;
                float len = alongX ? e.size.x : e.size.z, wdt = alongX ? e.size.z : e.size.x;
                var rot = Quaternion.Euler(0f, e.yaw_deg + e.euler_deg.y, 0f);
                var ax3 = rot * (alongX ? Vector3.right : Vector3.forward); var ax = new Vector2(ax3.x, ax3.z);
                var c0 = new Vector2(e.center.x, e.center.z);
                float top = e.center.y + e.size.y / 2f;
                rep.strips_checked++;
                int off = 0, n = 0; string offWhat = null; Vector2 offAt = default;
                for (float s = -len / 2f + 0.5f; s <= len / 2f - 0.5f; s += 2f)
                {
                    var p = c0 + ax * s; int q = Q(p); if (q < 0) continue;
                    n++;
                    if (vc[q] != mainV)
                    {
                        // a marking / wheel stop on top is still the road surface: accept when the cell is drivable at road height
                        if (cl[q] == Drive && Mathf.Abs(hy[q] - top) < 0.06f && vc[q] >= 0 && vc[q] == mainV) continue;
                        off++; if (offWhat == null) { offWhat = What(q) + (cl[q] == Drive ? $" comp{vc[q]}" : ""); offAt = p; }
                    }
                }
                if (n > 0 && off > 0) rep.strips_off_network.Add($"{e.id} {e.kind} w{wdt:F1} len{len:F0}: {off}/{n} centre samples off the main network, first at ({offAt.x:F0},{offAt.y:F0}) top {top:F2} -> {offWhat}");
                if (e.kind == "driveway" || (e.id != null && e.id.StartsWith("mouth_"))) continue;   // short connectors: membership only
                // junction pads (a square-ish slab joining a road to a bridge / ramp across its short side): the long-axis "ends"
                // are its lateral edges, so only membership in the main network is checked (above)
                if (e.id != null && e.id.Contains("junction") && len < 2.5f * wdt) continue;
                var perp = new Vector2(-ax.y, ax.x);
                foreach (float sg in new[] { -1f, 1f })
                {
                    var end = c0 + ax * (sg * len / 2f);
                    if (Mathf.Abs(end.x) > Half - 5f || Mathf.Abs(end.y) > Half - 5f) { rep.map_edge_ends.Add($"{e.id}@({end.x:F0},{end.y:F0})"); continue; }
                    int qIn = Q(end - ax * sg * 1.0f), qOut = Q(end + ax * sg * 1.2f), qOut2 = Q(end + ax * sg * 2.5f);
                    // joined: the cells beyond the end are walkable / drivable at a reachable height in the same component
                    bool joinedV = qOut >= 0 && qIn >= 0 && vc[qOut] >= 0 && vc[qOut] == vc[qIn];
                    bool joinedP = qOut >= 0 && qIn >= 0 && pc[qOut] >= 0 && pc[qOut] == pc[qIn];
                    if (joinedV || (e.kind != "road" && joinedP)) { rep.strip_ends_joined++; continue; }
                    // T-junction: the stem runs into a crossing carriageway and stops at its far kerb. The cells beyond are the
                    // far sidewalk, but the end is joined when the strip's last half metre, widened past both its edges, lies on
                    // another paved carriageway at the same height (checked on the element geometry, not on the cells)
                    var inner = end - ax * sg * 0.5f;
                    if (OnCarriageway(inner + perp * (wdt / 2f + 0.6f), e, top) && OnCarriageway(inner - perp * (wdt / 2f + 0.6f), e, top))
                    { rep.strip_ends_joined++; rep.t_junctions++; continue; }
                    // something drivable a little further on but not joined -> a step / gap
                    string beyond = What(qOut) + " / " + What(qOut2);
                    // deliberate terminus: a turning head / gate / barrier element at the end
                    bool terminus = def.elements.Any(t => (t.kind == "turning_head" || t.kind == "gate" || (t.id != null && t.id.Contains("terminus"))) &&
                                                          Vector2.Distance(new Vector2(t.center.x, t.center.z), end) < Mathf.Max(t.size.x, t.size.z) / 2f + 3f);
                    var s2 = $"{e.id} {e.kind} w{wdt:F1} end ({end.x:F0},{end.y:F0}) top {top:F2} -> beyond {beyond}";
                    if (terminus) rep.termini.Add(s2);
                    else if (e.kind == "lane" && wdt < 3.9f) rep.footpath_ends.Add(s2);              // village alley / footpath ends (no vehicle turning)
                    else rep.dead_ends.Add(s2);
                }
            }
            // ---- parking lots
            foreach (var e in def.elements)
            {
                if (e.shape != "box" || e.kind != "parking") continue;
                int q = Q(new Vector2(e.center.x, e.center.z));
                int inMain = 0, cnt = 0;
                for (float dx = -e.size.x / 2f + 1f; dx < e.size.x / 2f - 1f; dx += 3f)
                    for (float dz = -e.size.z / 2f + 1f; dz < e.size.z / 2f - 1f; dz += 3f)
                    {
                        int qq = Q(new Vector2(e.center.x + dx, e.center.z + dz)); if (qq < 0 || cl[qq] != Drive) continue;
                        cnt++; if (vc[qq] == mainV) inMain++;
                    }
                if (cnt > 0 && inMain == 0) rep.parking_off_network.Add($"{e.id} ({e.center.x:F0},{e.center.z:F0}) {e.size.x:F0}x{e.size.z:F0}: no cell in the main vehicle network");
            }
            // ---- lighthouses
            var lhs = new List<(string id, Vector3 p, float r)>();
            foreach (var e in def.elements)
            {
                if (e.kind == "lighthouse" && e.id.EndsWith("_base")) lhs.Add((e.id, e.center, e.size.x / 2f));
                if (e.kind == "lighthouse" && e.shape == "prefab") lhs.Add((e.id, e.center, 2.5f));
            }
            foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
                if (t.name == "landmark_lighthouse" && t.gameObject.activeInHierarchy && harborRoot != null && t.IsChildOf(harborRoot) && t.parent != harborRoot) lhs.Add(("landmark_lighthouse", t.position, 5.5f));
            foreach (var (id, p, r) in lhs)
            {
                var comps = new HashSet<int>(); float minDy = 99f; string ring = ""; var ringKinds = new HashSet<string>();
                for (int a = 0; a < 24; a++)
                {
                    float ang = a * 15f * Mathf.Deg2Rad;
                    for (float d = r + 0.6f; d <= r + 2.6f; d += 1f)
                    {
                        int q = Q(new Vector2(p.x + Mathf.Cos(ang) * d, p.z + Mathf.Sin(ang) * d)); if (q < 0) continue;
                        if (ringKinds.Count < 6) ringKinds.Add(What(q));
                        if (pc[q] >= 0) { comps.Add(pc[q]); minDy = Mathf.Min(minDy, Mathf.Abs(hy[q] - p.y)); }
                    }
                }
                foreach (var cc in comps) ring += cc == mainP ? "main " : landings.Contains(cc) ? "landing " : $"c{cc} ";
                string status = comps.Contains(mainP) ? "REACHABLE(road network)" : comps.Any(landings.Contains) ? "REACHABLE(boat landing)" : comps.Count == 0 ? "NO_WALKABLE_RING" : "ISOLATED";
                rep.lighthouses.Add($"{id} at ({p.x:F0},{p.y:F1},{p.z:F0}): {status}; ring comps [{ring.Trim()}], min |dy| ring-base {minDy:F2}; ring surfaces {string.Join(",", ringKinds)}");
            }
            // ---- floating thin surfaces and steps between adjacent paved cells
            var floatSeen = new HashSet<int>();
            int stepCount = 0;
            for (int j = 1; j < N - 1; j++)
                for (int i = 1; i < N - 1; i++)
                {
                    int q = j * N + i;
                    if (cl[q] != Drive && cl[q] != Walk) continue;
                    // step: a drivable cell next to a drivable cell 0.2..1.5 m higher on a road/lane kind (not kerb/coping)
                    if (cl[q] == Drive && kid[q] == roadKind && stepCount < 400)
                        foreach (int nq in new[] { q + 1, q + N })
                            if (cl[nq] == Drive && hy[nq] - hy[q] is var dy && Mathf.Abs(dy) > 0.2f && Mathf.Abs(dy) < 1.5f && kinds[kid[nq]] != "marking")
                            { rep.steps.Add($"({-Half + i + 0.5f:F0},{-Half + j + 0.5f:F0}) road@{hy[q]:F2} next to {kinds[kid[nq]]}@{hy[nq]:F2}"); stepCount++; }
                    int iid = colAt[q];
                    if (floatSeen.Contains(iid) || (i + j) % 3 != 0) continue;
                    if (!byId.TryGetValue(iid, out var col) || col == null) { floatSeen.Add(iid); continue; }
                    if (col.bounds.size.y > 0.6f) { floatSeen.Add(iid); continue; }
                    var o = new Vector3(-Half + i + 0.5f, hy[q], -Half + j + 0.5f);
                    if (def.InAnyWaterRegion(new Vector2(o.x, o.z))) continue;           // pontoons / bridges over water
                    bool supported = false; float next = 99f; string nextName = "none";
                    foreach (var hb in Physics.RaycastAll(o + Vector3.up * 1f, Vector3.down, 60f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                    {
                        if (hb.colliderInstanceID == iid || hb.point.y > o.y - 0.001f) continue;
                        float dd = o.y - hb.point.y;
                        if (dd < next) { next = dd; nextName = hb.collider.name; }
                        if (dd <= 0.8f) supported = true;
                    }
                    if (!supported)
                    {
                        floatSeen.Add(iid);
                        rep.floating.Add($"{kinds[kid[q]]} '{col.name}' at ({o.x:F0},{o.y:F2},{o.z:F0}): nothing within 0.8 m below (next {next:F2} m, {nextName})");
                    }
                }

            // ---- optional probes: -auditProbe map:x,z;map:x,z  -> every collider under the point, top down
            var args = System.Environment.GetCommandLineArgs();
            for (int ai = 0; ai < args.Length - 1; ai++)
                if (args[ai] == "-auditProbe")
                    foreach (var pr in args[ai + 1].Split(';'))
                    {
                        var parts = pr.Split(':'); if (parts.Length != 2 || parts[0] != mapId) continue;
                        var xz = parts[1].Split(',');
                        var o = new Vector3(float.Parse(xz[0], System.Globalization.CultureInfo.InvariantCulture), 400f, float.Parse(xz[1], System.Globalization.CultureInfo.InvariantCulture));
                        foreach (var hh in Physics.RaycastAll(o, Vector3.down, 450f).OrderByDescending(hh => hh.point.y))
                        {
                            var t = hh.collider.transform; string path = t.name; for (var pp = t.parent; pp != null && pp != harborRoot; pp = pp.parent) path = pp.name + "/" + path;
                            Debug.Log($"[SurfaceAudit] probe {mapId} ({o.x},{o.z}) y {hh.point.y:F3} {path} kind {KindOf(hh.collider, harborRoot)} mat {MatOf(hh.collider)} bounds {hh.collider.bounds.min} {hh.collider.bounds.max}");
                        }
                    }

            // ---- write grid + report
            using (var bw = new BinaryWriter(File.Open(Path.Combine(dir, mapId + ".bin"), FileMode.Create)))
            {
                bw.Write(N); bw.Write(Half); bw.Write(Cell);
                for (int q = 0; q < total; q++)
                {
                    byte f = 0;
                    if (vc[q] == mainV) f |= 1;
                    if (pc[q] == mainP) f |= 2;
                    if (pc[q] >= 0 && landings.Contains(pc[q])) f |= 4;
                    bw.Write(cl[q]); bw.Write(f); bw.Write((short)kid[q]); bw.Write(hy[q]); bw.Write(vc[q]); bw.Write(pc[q]);
                }
            }
            rep.kinds = kinds;
            File.WriteAllLines(Path.Combine(dir, mapId + "_roadnetwork_trace.txt"), RoadNetwork.Trace);
            File.WriteAllText(Path.Combine(dir, mapId + ".json"), JsonUtility.ToJson(rep, true));
            return $"{mapId}: strips {rep.strips_checked}, off-network {rep.strips_off_network.Count}, ends joined {rep.strip_ends_joined} (T-junctions {rep.t_junctions}), dead ends {rep.dead_ends.Count}, termini {rep.termini.Count}, alley/footpath ends {rep.footpath_ends.Count}, map-edge {rep.map_edge_ends.Count}, parking off {rep.parking_off_network.Count}, floating {rep.floating.Count}, road steps {rep.steps.Count}, road obstructions {rep.road_obstructions.Count}; lighthouses: " +
                   string.Join(" | ", rep.lighthouses.Select(l => l.Split(';')[0]));
        }

        /// <summary>4-neighbour flood fill over cells passing `ok`, joining neighbours whose heights differ by at most `step`
        /// (`stairStep` when either cell is a stair).</summary>
        static int[] Components(byte[] cl, float[] hy, System.Func<byte, bool> ok, float step, float stairStep)
        {
            int total = N * N; var comp = new int[total];
            for (int q = 0; q < total; q++) comp[q] = -1;
            var stack = new Stack<int>(); int next = 0;
            for (int s = 0; s < total; s++)
            {
                if (comp[s] >= 0 || !ok(cl[s])) continue;
                comp[s] = next; stack.Push(s);
                while (stack.Count > 0)
                {
                    int q = stack.Pop(); int i = q % N, j = q / N;
                    for (int d = 0; d < 4; d++)
                    {
                        int ni = i + (d == 0 ? 1 : d == 1 ? -1 : 0), nj = j + (d == 2 ? 1 : d == 3 ? -1 : 0);
                        if (ni < 0 || nj < 0 || ni >= N || nj >= N) continue;
                        int nq = nj * N + ni;
                        if (comp[nq] >= 0 || !ok(cl[nq])) continue;
                        float lim = (cl[q] == Stair || cl[nq] == Stair) ? stairStep : step;
                        if (Mathf.Abs(hy[nq] - hy[q]) > lim) continue;
                        comp[nq] = next; stack.Push(nq);
                    }
                }
                next++;
            }
            return comp;
        }
    }
}
