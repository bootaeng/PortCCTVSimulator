using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace PortCCTV
{
    /// <summary>
    /// Runtime dressing of every procedural hill (hill_N, hill_L_N and the camera hills hill_m_N; user 2026-11-09: denser
    /// forest on all hills):
    ///  * a forest of cross-billboard trees (TreeImpostors: pine a/b/c 75 %, broadleaf 25 %) on gentle slopes above the shore,
    ///    so the baked canopy texture no longer shows one repeating pattern; merged into one mesh per hill and tree type;
    ///  * pieces of the Poly Haven coast_line_01 scan (shore_line_01) where the hill meets the water, laid along the shoreline,
    ///    joining the hill to the sea instead of a clean cut.
    /// Deterministic per harbor id. Background only (never labelled); the coast pieces get colliders like other structures.
    /// </summary>
    public static class Vegetation
    {
        static readonly string[] TreeKeys = { "treeimp_pine_a", "treeimp_pine_b", "treeimp_pine_c", "treeimp_broadleaf" };
        public const int MaxTrees = 300000, MaxTreesPerHill = 40000, MaxCoastPieces = 36, MaxNear3D = 900;
        const float Near3DRange = 110f;
        /// <summary>Open grass land (box elements with material "grass"): woods in low-frequency patches, hedgerow-like scattered
        /// trees at their edges and lone trees elsewhere, only where the grass itself is the ground (not under roads, fields, car
        /// parks or buildings) and nothing else stands within 3 m (user 2026-11-09: "에셋 뭐든 다 빽빽하게").</summary>
        public const int MaxFlatTrees = 160000;
        const float FlatTriesPerM2 = 1f / 30f, FlatReach = 2200f;
        const float TreesPerM2 = 1f / 14f;
        /// <summary>Camera hills: no trees within CameraKeepout of a CCTV preset camera (its mast/pad). Closer than ViewKeepout,
        /// a tree that appears in that camera's frame (preset vertical FOV at 16:9, 15 % margin) is planted only if the camera
        /// ray through its crown top hits hill ground again within 80 m (it hides only hill, never water, quay or vessels);
        /// otherwise low scrub is tried, otherwise nothing (user 2026-11-09: "아직 빈부분이 좀 있네").</summary>
        const float CameraKeepout = 15f, ViewKeepout = 160f, FrameMargin = 1.15f, Aspect = 16f / 9f;
        /// <summary>Harbour-facing slopes (user 2026-11-09: denser trees on the harbour side): within HarborNear m of the inner
        /// harbour water (every water region except the outer sea) the forest reaches lower and steeper, has no clearings, and
        /// gets a second scatter pass at HarborExtraPerM2 on top of the normal density, fading out by HarborFar m.</summary>
        const float HarborNear = 150f, HarborFar = 700f, HarborExtraPerM2 = 1f / 9f;
        public const int MaxHarborTreesPerHill = 30000;

        public struct Result { public int trees, harbor_trees, camera_zone_trees, town_trees, flat_trees, coast_pieces, hills, near_3d_trees, lighthouse_paths, lighthouse_landings; public float lighthouse_path_max_grade, lighthouse_path_length_m; }

        static float GroundY(Vector2 p, HashSet<Collider> skip, out Collider top)
        {
            top = null; float y = float.NegativeInfinity;
            foreach (var hh in Physics.RaycastAll(new Vector3(p.x, 1500f, p.y), Vector3.down, 2000f))
                if (!skip.Contains(hh.collider) && hh.point.y > y) { y = hh.point.y; top = hh.collider; }
            return y;
        }

        /// <summary>Base pad and footpath for a landmark lighthouse (see Scatter).</summary>
        static void LighthouseAccess(HarborDefinition def, HarborTheme theme, Dictionary<string, GameObject> objects, Transform root, List<Collider> colliders, HashSet<Collider> treeCols,
                                     GameObject tower, float wy, HashSet<(int, int)> cells, ref Result res, Vector3 startPos = default)
        {
            var paveMat = theme.Get("sidewalk_paving") ?? theme.Get("concrete");
            var skip = new HashSet<Collider>(treeCols);
            if (tower != null) foreach (var c in tower.GetComponentsInChildren<Collider>(true)) skip.Add(c);       // the tower itself is not ground
            var lh = tower != null ? new Vector2(tower.transform.position.x, tower.transform.position.z) : new Vector2(startPos.x, startPos.z);
            if (tower != null) {
            // base pad: 10 m octagon whose top is 0.1 m above the highest ground under it, with a skirt into the slope
            float padTop = GroundY(lh, skip, out _) + 0.1f;
            for (int i = 0; i < 8; i++) { float a = (i + 0.5f) * Mathf.PI / 4f; padTop = Mathf.Max(padTop, GroundY(lh + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 5f, skip, out _) + 0.05f); }
            var pv = new List<Vector3> { new Vector3(lh.x, padTop, lh.y) }; var pt = new List<int>();
            for (int i = 0; i < 8; i++) { float a = (i + 0.5f) * Mathf.PI / 4f; pv.Add(new Vector3(lh.x + Mathf.Cos(a) * 5f, padTop, lh.y + Mathf.Sin(a) * 5f)); }
            for (int i = 0; i < 8; i++) { float a = (i + 0.5f) * Mathf.PI / 4f; var q = new Vector2(lh.x + Mathf.Cos(a) * 5f, lh.y + Mathf.Sin(a) * 5f); pv.Add(new Vector3(q.x, GroundY(q, skip, out _) - 0.3f, q.y)); }
            for (int i = 0; i < 8; i++) { int a = 1 + i, b = 1 + (i + 1) % 8; pt.AddRange(new[] { 0, b, a, a, b, 8 + b, a, 8 + b, 8 + a }); }
            var pm = new Mesh { name = "lighthouse_base_pad" }; pm.SetVertices(pv); pm.SetTriangles(pt, 0); pm.RecalculateNormals(); pm.RecalculateBounds();
            var pad = new GameObject("lighthouse_base_pad"); pad.transform.SetParent(root, false);
            pad.AddComponent<MeshFilter>().sharedMesh = pm; pad.AddComponent<MeshRenderer>().sharedMaterial = theme.Get("concrete") ?? paveMat;
            colliders.Add(pad.AddComponent<MeshCollider>());
            tower.transform.position = new Vector3(tower.transform.position.x, padTop - 0.05f, tower.transform.position.z);   // stands on the pad
            for (int ox = -2; ox <= 2; ox++) for (int oz = -2; oz <= 2; oz++) cells.Add((Mathf.FloorToInt(lh.x / 4f) + ox, Mathf.FloorToInt(lh.y / 4f) + oz));
            }
            // Footpath route: A* over a 4 m grid on whatever ground is there (hill mesh, land, terraces), never over water,
            // cost = distance x (1 + 6 x grade), steps steeper than 0.45 not allowed (a hill path with steps); the goal is any
            // cell standing on a road / path / lane / sidewalk / quay box. A straight line usually crossed a bay or a cliff.
            var goalCols = new HashSet<Collider>();
            foreach (var e in def.elements)
                if (e.shape == "box" && (e.kind == "road" || e.kind == "path" || e.kind == "lane" || e.kind == "sidewalk" || e.kind == "quay" || e.kind == "promenade")
                    && objects.TryGetValue(e.id, out var eg))
                    foreach (var c in eg.GetComponentsInChildren<Collider>()) goalCols.Add(c);
            const float cell = 4f; const int R = 200;                                    // search radius 800 m
            var hgt = new Dictionary<(int, int), (float y, bool ok, bool goal)>();
            (float y, bool ok, bool goal) Cell((int, int) k)
            {
                if (hgt.TryGetValue(k, out var v)) return v;
                var q = lh + new Vector2(k.Item1, k.Item2) * cell;
                float y = GroundY(q, skip, out var col);
                v = (y, col != null && y > wy + 0.6f, col != null && goalCols.Contains(col));
                hgt[k] = v; return v;
            }
            var open = new SortedSet<(float f, int i, int j)>();
            var g = new Dictionary<(int, int), float>(); var from = new Dictionary<(int, int), (int, int)>();
            var startK = (0, 0); g[startK] = 0f; open.Add((0f, 0, 0));
            (int, int)? found = null; int expanded = 0;
            // shore cells (walkable, next to water) seen during the search: the boat-landing site if the hill is an island
            (int, int)? landing = null; Vector2 landingWater = Vector2.zero; float landingScore = float.MaxValue;
            while (open.Count > 0 && expanded < 60000)
            {
                var cur = open.Min; open.Remove(cur); var ck = (cur.i, cur.j);
                if (cur.f > g[ck] + 1e-3f) continue;
                expanded++;
                var cv = Cell(ck);
                if (cv.goal && ck != startK) { found = ck; break; }
                if (cv.ok && cv.y < wy + 4f)
                    for (int q = 0; q < 4; q++)
                    {
                        var wk = (ck.Item1 + (q == 0 ? 1 : q == 1 ? -1 : 0), ck.Item2 + (q == 2 ? 1 : q == 3 ? -1 : 0));
                        var wv = Cell(wk);
                        if (wv.ok || wv.y > wy - 0.2f) continue;                      // neighbour is open water
                        var wp = lh + new Vector2(wk.Item1, wk.Item2) * cell;
                        float score = wp.magnitude + 0.5f * g[ck];                    // facing the harbour (map origin), short walk
                        if (score < landingScore) { landingScore = score; landing = ck; landingWater = new Vector2(wk.Item1 - ck.Item1, wk.Item2 - ck.Item2); }
                    }
                for (int di = -1; di <= 1; di++)
                    for (int dj = -1; dj <= 1; dj++)
                    {
                        if (di == 0 && dj == 0) continue;
                        var nk = (ck.Item1 + di, ck.Item2 + dj);
                        if (Mathf.Abs(nk.Item1) > R || Mathf.Abs(nk.Item2) > R) continue;
                        // the first 6 m round the tower are the pad
                        bool onPad = Mathf.Abs(nk.Item1) <= 1 && Mathf.Abs(nk.Item2) <= 1;
                        var nv = Cell(nk);
                        if (!nv.ok && !onPad) continue;
                        float d = cell * Mathf.Sqrt(di * di + dj * dj);
                        float grade = onPad || (Mathf.Abs(ck.Item1) <= 1 && Mathf.Abs(ck.Item2) <= 1) ? 0f : Mathf.Abs(nv.y - cv.y) / d;
                        if (grade > 0.35f) continue;
                        // no cliff between the two cells (a land-box edge or a quay wall can hide between 4 m samples)
                        if (!onPad)
                        {
                            var mid = lh + new Vector2(ck.Item1 + nk.Item1, ck.Item2 + nk.Item2) * (cell * 0.5f);
                            float ym = GroundY(mid, skip, out var mcol);
                            if (mcol == null || Mathf.Abs(ym - cv.y) / (d * 0.5f) > 0.5f || Mathf.Abs(nv.y - ym) / (d * 0.5f) > 0.5f) continue;
                        }
                        float ng = g[ck] + d * (1f + 6f * grade);
                        if (g.TryGetValue(nk, out var old) && old <= ng) continue;
                        g[nk] = ng; from[nk] = ck; open.Add((ng, nk.Item1, nk.Item2));
                    }
            }
            bool island = false;
            if (found == null)
            {
                if (landing == null)
                {
                    Debug.Log($"[Vegetation] {def.harbor_id}: no walkable lighthouse path found from {lh} (expanded {expanded} cells)");
                    return;
                }
                // no road reachable over ground: the hill is an island. Island lights are reached by boat, so the path ends at a
                // small concrete landing stage on the harbour side (12 x 3 m, deck 1.2 m above the water, on piles).
                found = landing; island = true;
                var lp = lh + new Vector2(landing.Value.Item1, landing.Value.Item2) * cell;
                var wd = landingWater.normalized;
                float landY = Cell(landing.Value).y;
                var deckC = lp + wd * 6.5f;
                var jetty = GameObject.CreatePrimitive(PrimitiveType.Cube); jetty.name = "lighthouse_landing";
                jetty.transform.SetParent(root, false);
                float deckY = Mathf.Max(wy + 0.9f, landY);                   // flush with the path's last ground point (the audit found a 0.5 m step)
                jetty.transform.SetPositionAndRotation(new Vector3(deckC.x, deckY - 0.3f, deckC.y), Quaternion.Euler(0f, Mathf.Atan2(wd.x, wd.y) * Mathf.Rad2Deg, 0f));
                jetty.transform.localScale = new Vector3(3f, 0.6f, 13f);
                jetty.GetComponent<MeshRenderer>().sharedMaterial = theme.Get("concrete") ?? paveMat;
                colliders.Add(jetty.GetComponent<Collider>());
                foreach (var f in new[] { -4.5f, 0f, 4.5f })
                    foreach (var sd in new[] { -1.2f, 1.2f })
                    {
                        var pile = GameObject.CreatePrimitive(PrimitiveType.Cylinder); pile.name = "lighthouse_landing_pile";
                        pile.transform.SetParent(jetty.transform, false);
                        pile.transform.localPosition = new Vector3(sd / 3f, -(deckY - wy + 2f) / 0.6f / 2f, f / 13f);
                        pile.transform.localScale = new Vector3(0.4f / 3f, (deckY - wy + 2f) / 0.6f / 2f, 0.4f / 13f);
                        pile.GetComponent<MeshRenderer>().sharedMaterial = theme.Get("concrete") ?? paveMat;
                        Object.DestroyImmediate(pile.GetComponent<Collider>());
                    }
                foreach (var sd in new[] { -1f, 1f })                                   // bollards at the outer end
                {
                    var b2 = lp + wd * 11.5f + new Vector2(-wd.y, wd.x) * sd * 1.1f;
                    var bol = GameObject.CreatePrimitive(PrimitiveType.Cylinder); bol.name = "lighthouse_landing_bollard";
                    bol.transform.SetParent(root, false);
                    bol.transform.SetPositionAndRotation(new Vector3(b2.x, deckY + 0.3f, b2.y), Quaternion.identity);
                    bol.transform.localScale = new Vector3(0.35f, 0.3f, 0.35f);
                    bol.GetComponent<MeshRenderer>().sharedMaterial = theme.Get("bollard") ?? paveMat;
                    Object.DestroyImmediate(bol.GetComponent<Collider>());
                }
                res.lighthouse_landings++;
            }
            var route = new List<Vector2>();
            for (var k = found.Value; ; k = from[k]) { route.Add(lh + new Vector2(k.Item1, k.Item2) * cell); if (k == startK) break; }
            route.Reverse();
            // smooth the grid zig-zags (two Chaikin passes), keep the ends
            for (int pass = 0; pass < 1; pass++)
            {
                var sm = new List<Vector2> { route[0] };
                for (int i = 0; i + 1 < route.Count; i++) { sm.Add(Vector2.Lerp(route[i], route[i + 1], 0.25f)); sm.Add(Vector2.Lerp(route[i], route[i + 1], 0.75f)); }
                sm.Add(route[route.Count - 1]); route = sm;
            }
            // resample every ~2 m on the ground
            var pts = new List<Vector3>(); float length = 0f, maxGrade = 0f;
            for (int i = 0; i + 1 < route.Count; i++)
            {
                float segLen = (route[i + 1] - route[i]).magnitude; int nSeg = Mathf.Max(1, Mathf.CeilToInt(segLen / 2f));
                for (int k = 0; k < nSeg; k++)
                {
                    var q = Vector2.Lerp(route[i], route[i + 1], k / (float)nSeg);
                    if (tower != null && (q - lh).magnitude < 5f) continue;                 // inside the pad
                    float y = GroundY(q, skip, out _);
                    if (pts.Count > 0) { var pp = pts[pts.Count - 1]; float dd = (q - new Vector2(pp.x, pp.z)).magnitude; length += dd; if (dd > 0.5f) maxGrade = Mathf.Max(maxGrade, Mathf.Abs(y - pp.y) / dd); }
                    pts.Add(new Vector3(q.x, y, q.y));
                }
            }
            var endQ = route[route.Count - 1]; pts.Add(new Vector3(endQ.x, GroundY(endQ, skip, out _), endQ.y));
            if (pts.Count < 2) return;
            // ribbon 2.4 m wide, 0.08 m above the ground, with a skirt on each side so its edges never float
            var v = new List<Vector3>(); var t = new List<int>(); var uv = new List<Vector2>(); float along = 0f;
            for (int i = 0; i < pts.Count; i++)
            {
                var a3 = pts[Mathf.Max(0, i - 1)]; var b3 = pts[Mathf.Min(pts.Count - 1, i + 1)];
                var dir = new Vector2(b3.x - a3.x, b3.z - a3.z).normalized;
                var side = new Vector3(-dir.y, 0f, dir.x) * 1.2f;
                var c = pts[i] + Vector3.up * 0.08f;
                if (i > 0) along += (pts[i] - pts[i - 1]).magnitude;
                v.Add(c - side); v.Add(c + side); v.Add(c - side * 1.2f - Vector3.up * 0.5f); v.Add(c + side * 1.2f - Vector3.up * 0.5f);
                float u = along / 3f; uv.Add(new Vector2(0f, u)); uv.Add(new Vector2(0.8f, u)); uv.Add(new Vector2(-0.1f, u)); uv.Add(new Vector2(0.9f, u));
                if (i > 0)
                {
                    int a = (i - 1) * 4, b = i * 4;
                    t.AddRange(new[] { a, a + 1, b, a + 1, b + 1, b, a + 2, a, b + 2, a, b, b + 2, a + 1, a + 3, b + 1, a + 3, b + 3, b + 1 });   // top up, skirts outward
                }
                for (int ox = -1; ox <= 1; ox++) for (int oz = -1; oz <= 1; oz++) cells.Add((Mathf.FloorToInt(pts[i].x / 4f) + ox, Mathf.FloorToInt(pts[i].z / 4f) + oz));
            }
            var m = new Mesh { name = "lighthouse_path", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            m.SetVertices(v); m.SetUVs(0, uv); m.SetTriangles(t, 0); m.RecalculateNormals(); m.RecalculateBounds();
            var go = new GameObject("lighthouse_path"); go.transform.SetParent(root, false);
            go.AddComponent<MeshFilter>().sharedMesh = m; go.AddComponent<MeshRenderer>().sharedMaterial = paveMat;
            colliders.Add(go.AddComponent<MeshCollider>());
            res.lighthouse_paths++; res.lighthouse_path_max_grade = Mathf.Max(res.lighthouse_path_max_grade, maxGrade); res.lighthouse_path_length_m += length;
            int steps = StairsAndRails(theme, root, colliders, pts, def.harbor_id + "_" + res.lighthouse_paths);
            Debug.Log($"[Vegetation] {def.harbor_id}: lighthouse path {length:F0} m ({steps} stair steps with handrails) from {lh} to {(island ? "a boat landing" : "the road network")} at {pts[pts.Count - 1]}, max grade {maxGrade:F2}, {expanded} cells searched");
        }

        /// <summary>
        /// Steep parts of a footpath become real stairs (2026-11-09, user: steep slopes need real stairs, landings and handrails):
        /// every path segment steeper than 1:7 gets concrete steps (risers at most 0.17 m, treads sharing the segment), the
        /// flatter samples between runs act as landings, and galvanised handrails (posts at both ends of each run, top rail at
        /// 0.95 m, knee rail) line both sides of every stair run. Steps and rails are one combined mesh per path
        /// ("lighthouse_stair_*" with a MeshCollider, "lighthouse_stair_rail_*"). Returns the number of steps.
        /// </summary>
        static int StairsAndRails(HarborTheme theme, Transform root, List<Collider> colliders, List<Vector3> pts, string tag)
        {
            var conc = theme.Get("concrete"); var steel = theme.Get("steel_galv") ?? conc;
            var tmp = GameObject.CreatePrimitive(PrimitiveType.Cube); var cube = tmp.GetComponent<MeshFilter>().sharedMesh; Object.DestroyImmediate(tmp);
            var stepCI = new List<CombineInstance>(); var railCI = new List<CombineInstance>();
            int steps = 0;
            void Box(List<CombineInstance> l, Vector3 c, Vector3 size, Quaternion q) => l.Add(new CombineInstance { mesh = cube, transform = Matrix4x4.TRS(c, q, size) });
            for (int i = 0; i + 1 < pts.Count; i++)
            {
                var a = pts[i]; var b = pts[i + 1];
                var flat = new Vector2(b.x - a.x, b.z - a.z); float len = flat.magnitude; if (len < 0.3f) continue;
                float dy = b.y - a.y; if (Mathf.Abs(dy) / len <= 1f / 7f) continue;
                var dir = flat / len; var q = Quaternion.Euler(0f, Mathf.Atan2(dir.x, dir.y) * Mathf.Rad2Deg, 0f);
                int n = Mathf.Max(1, Mathf.CeilToInt(Mathf.Abs(dy) / 0.17f)); float tread = len / n, riser = dy / n;
                float bot = Mathf.Min(a.y, b.y) - 0.4f;
                for (int k = 0; k < n; k++)
                {
                    var pc = Vector2.Lerp(new Vector2(a.x, a.z), new Vector2(b.x, b.z), (k + 0.5f) / n);
                    float topY = a.y + (dy > 0 ? (k + 1) : k) * riser;          // ascent: rise before the tread; descent: after it
                    Box(stepCI, new Vector3(pc.x, (topY + 0.08f + bot) / 2f, pc.y), new Vector3(2.2f, topY + 0.08f - bot, tread + 0.02f), q);
                    steps++;
                }
                foreach (float s in new[] { -1.15f, 1.15f })
                {
                    var off = new Vector3(dir.y, 0f, -dir.x) * s;
                    var pa = a + off + Vector3.up * 0.08f; var pb = b + off + Vector3.up * 0.08f;
                    Box(railCI, pa + Vector3.up * 0.47f, new Vector3(0.06f, 0.95f, 0.06f), Quaternion.identity);
                    Box(railCI, pb + Vector3.up * 0.47f, new Vector3(0.06f, 0.95f, 0.06f), Quaternion.identity);
                    var ta = pa + Vector3.up * 0.95f; var tb = pb + Vector3.up * 0.95f; var mid = (ta + tb) / 2f; var d3 = tb - ta;
                    Box(railCI, mid, new Vector3(0.05f, 0.05f, d3.magnitude), Quaternion.LookRotation(d3.normalized, Vector3.up));
                    Box(railCI, mid - Vector3.up * 0.45f, new Vector3(0.04f, 0.04f, d3.magnitude), Quaternion.LookRotation(d3.normalized, Vector3.up));
                }
            }
            if (steps == 0) return 0;
            void Make(string name, List<CombineInstance> l, Material mat, bool collide)
            {
                var m = new Mesh { name = name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                m.CombineMeshes(l.ToArray(), true, true); m.RecalculateBounds();
                var go = new GameObject(name); go.transform.SetParent(root, false);
                go.AddComponent<MeshFilter>().sharedMesh = m; go.AddComponent<MeshRenderer>().sharedMaterial = mat;
                if (collide) colliders.Add(go.AddComponent<MeshCollider>());
            }
            Make("lighthouse_stair_" + tag, stepCI, conc, true);
            Make("lighthouse_stair_rail_" + tag, railCI, steel, false);
            return steps;
        }

        public static Result Scatter(HarborDefinition def, HarborTheme theme, Transform parent, Dictionary<string, GameObject> objects, List<Collider> colliders)
        {
            var res = new Result();
            if (theme == null) return res;
            var prefabs = TreeKeys.Select(k => theme.GetPrefab(k)).ToArray();
            bool haveTrees = prefabs.All(p => p != null);
            var coast = theme.GetPrefab("shore_line_01");
            uint h = 2166136261u; foreach (var c in def.harbor_id + "|veg") h = (h ^ c) * 16777619u;
            var rng = new System.Random(unchecked((int)h));
            float wy = def.water_reference_y;
            var root = new GameObject("Vegetation").transform; root.SetParent(parent, false);
            var hills = def.elements.Where(e => e.shape == "prefab" && e.prefab != null && e.prefab.StartsWith("hill_"))
                .OrderBy(e => e.id, System.StringComparer.Ordinal).ToList();
            var cams = CameraPresetLibrary.ForHarbor(def.harbor_id);
            var camView = cams.Select(c => (pos: c.position, inv: Quaternion.Inverse(Quaternion.Euler(c.euler_deg)),
                tanV: Mathf.Tan(0.5f * Mathf.Deg2Rad * Mathf.Max(c.vertical_fov_deg, 1f)))).ToArray();
            bool InFrame(Vector3 w, (Vector3 pos, Quaternion inv, float tanV) c)
            {
                var v = c.inv * (w - c.pos);                                   // camera space, +z forward
                if (v.z <= 0.3f) return false;
                return Mathf.Abs(v.y) <= v.z * c.tanV * FrameMargin && Mathf.Abs(v.x) <= v.z * c.tanV * Aspect * FrameMargin;
            }
            // Only hill ground may sit behind a tree in a camera's frame: the camera ray through the tree's crown top (centre and
            // +-4 m) must hit a hill again within 80 m, else the tree would cover water, quay or vessels and is not planted.
            var hillCols = new HashSet<Collider>();
            foreach (var e in hills) if (objects.TryGetValue(e.id, out var hg)) foreach (var c in hg.GetComponentsInChildren<Collider>()) hillCols.Add(c);
            // the scanned trees/scrub the map already places (kind "tree") do not stop the forest growing around them
            var treeCols = new HashSet<Collider>();
            foreach (var e in def.elements) if (e.kind == "tree" && objects.TryGetValue(e.id, out var tg)) foreach (var c in tg.GetComponentsInChildren<Collider>()) treeCols.Add(c);
            // Landmark lighthouses (prefab "lighthouse_tall", placed by the map at a headland hill's centre): walk from the hill
            // centre towards the harbour (map origin) and stand the tower on the last level spot >= 8 m above the water,
            // i.e. on the seaward brow of the headland; trees keep 12 m clear of it.
            var lighthouseSpots = new List<Vector2>();
            foreach (var le in def.elements)
            {
                if (le.kind != "landmark_lighthouse" || !objects.TryGetValue(le.id, out var lgo)) continue;
                var start = new Vector2(le.center.x, le.center.z); var dir = (-start).normalized;
                Collider hc = null;
                foreach (var c in hillCols) { var bb = c.bounds; if (start.x > bb.min.x && start.x < bb.max.x && start.y > bb.min.z && start.y < bb.max.z) { hc = c; break; } }
                Vector3? best = null;
                if (hc != null)
                    for (float d = 0f; d < 2000f; d += 4f)
                    {
                        var p = start + dir * d;
                        if (!hc.Raycast(new Ray(new Vector3(p.x, hc.bounds.max.y + 50f, p.y), Vector3.down), out var lh, hc.bounds.size.y + 200f)) break;
                        if (lh.point.y - wy < 8f) break;
                        if (lh.normal.y > 0.9f) best = lh.point;
                    }
                if (best == null) { lgo.SetActive(false); Debug.Log($"[Vegetation] {def.harbor_id}: no site for {le.id}"); continue; }
                lgo.transform.SetPositionAndRotation(best.Value - Vector3.up * 0.4f, Quaternion.Euler(0f, Mathf.Atan2(dir.x, dir.y) * Mathf.Rad2Deg, 0f));
                lighthouseSpots.Add(new Vector2(best.Value.x, best.Value.z));
                Debug.Log($"[Vegetation] {def.harbor_id}: {le.id} at {best.Value}");
            }
            if (lighthouseSpots.Count > 0) Physics.SyncTransforms();
            // Each landmark lighthouse gets a concrete base pad and a 2.4 m footpath following the ground down to the nearest
            // paved land (road / path / quay / terrace) that can be reached without crossing water (detail review 2026-11-09 §6);
            // trees keep 4 m off the path.
            var pathCells = new HashSet<(int, int)>();
            foreach (var le in def.elements)
                if (le.kind == "landmark_lighthouse" && objects.TryGetValue(le.id, out var lgo2) && lgo2.activeSelf)
                    LighthouseAccess(def, theme, objects, root, colliders, treeCols, lgo2, wy, pathCells, ref res);
            // footpaths from breakwater roots that end at a hill instead of the quay (marker elements, kind foot_access)
            foreach (var fe in def.elements)
                if (fe.kind == "foot_access" && fe.shape == "marker")
                    LighthouseAccess(def, theme, objects, root, colliders, treeCols, null, wy, pathCells, ref res, fe.center);
            // near-field 3-D trees (Poly Haven island_tree_01 / 02, native heights 5.03 / 3.41 m)
            var near3dPrefabs = new[] { theme.GetPrefab("tree_01"), theme.GetPrefab("tree_02") };
            var near3dHeights = new[] { 5.03f, 3.41f };
            int near3d = 0;
            bool SeenNear(Vector3 p, float height)
            {
                foreach (var c in camView)
                {
                    if (new Vector2(p.x - c.pos.x, p.z - c.pos.z).sqrMagnitude > Near3DRange * Near3DRange) continue;
                    if (InFrame(p, c) || InFrame(p + Vector3.up * height, c) || InFrame(p + Vector3.up * height * 0.5f, c)) return true;
                }
                return false;
            }
            var hit1 = new RaycastHit[8];
            bool BehindIsHill(Vector3 from, Vector3 dir)
            {
                int nh = Physics.RaycastNonAlloc(new Ray(from, dir), hit1, 80f);
                for (int q = 0; q < nh; q++) if (hillCols.Contains(hit1[q].collider)) return true;
                return false;
            }
            bool CameraOk(Vector3 p, float height)
            {
                foreach (var c in camView)
                {
                    float dist = new Vector2(p.x - c.pos.x, p.z - c.pos.z).magnitude;
                    if (dist < CameraKeepout) return false;
                    if (dist >= ViewKeepout) continue;
                    // the tree (crown ~4 m radius): base, middle and top, centre and crown edges
                    bool seen = false;
                    for (int hk = 0; hk <= 2 && !seen; hk++)
                    {
                        var q = p + Vector3.up * (0.5f * height * hk);
                        seen = InFrame(q, c) || InFrame(q + Vector3.right * 4f, c) || InFrame(q - Vector3.right * 4f, c)
                            || InFrame(q + Vector3.forward * 4f, c) || InFrame(q - Vector3.forward * 4f, c);
                    }
                    if (!seen) continue;
                    var top = p + Vector3.up * height;
                    var side = Vector3.Cross(Vector3.up, (top - c.pos).normalized).normalized * 4f;
                    foreach (var t in new[] { top, top + side, top - side })
                        if (!BehindIsHill(t + (t - c.pos).normalized * 0.5f, (t - c.pos).normalized)) return false;
                }
                return true;
            }
            var overlap = new Collider[8];
            var rej = new int[8];   // diagnostics, camera-view region only: alt, normal, clearing, camera, blocked, placed-full, placed-scrub, placed-shrub
            // inner harbour extent: union of the non-outer water regions
            var inner = def.water_regions.Where(r => r.id == null || !r.id.Contains("outer")).SelectMany(r => r.polygon_xz).ToList();
            bool haveHarbor = inner.Count > 0;
            var hMin = haveHarbor ? new Vector2(inner.Min(v => v.x), inner.Min(v => v.y)) : Vector2.zero;
            var hMax = haveHarbor ? new Vector2(inner.Max(v => v.x), inner.Max(v => v.y)) : Vector2.zero;
            float Harborness(float x, float z)
            {
                if (!haveHarbor) return 0f;
                float dx = Mathf.Max(hMin.x - x, 0f, x - hMax.x), dz = Mathf.Max(hMin.y - z, 0f, z - hMax.y);
                return 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(HarborNear, HarborFar, Mathf.Sqrt(dx * dx + dz * dz)));
            }
            var meshes = haveTrees ? prefabs.Select(p => p.GetComponent<MeshFilter>().sharedMesh).ToArray() : null;
            var mats = haveTrees ? prefabs.Select(p => p.GetComponent<MeshRenderer>().sharedMaterial).ToArray() : null;
            var coastPlaced = new List<Vector2>();
            void MergeTrees(List<(Matrix4x4 trs, Vector3 n)> list, int k, string tag, Transform parentT)
            {
                if (list.Count == 0) return;
                var m = new Mesh { name = $"merged_trees_{tag}_{TreeKeys[k]}", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                var sv = meshes[k].vertices; var suv = meshes[k].uv; var st = meshes[k].triangles;
                var vv = new Vector3[sv.Length * list.Count]; var nn = new Vector3[vv.Length]; var uu = new Vector2[vv.Length];
                var tt = new int[st.Length * list.Count];
                for (int g = 0; g < list.Count; g++)
                {
                    var (trs, nrm) = list[g]; int ob = g * sv.Length;
                    for (int q = 0; q < sv.Length; q++) { vv[ob + q] = trs.MultiplyPoint3x4(sv[q]); nn[ob + q] = nrm; uu[ob + q] = suv[q]; }
                    for (int q = 0; q < st.Length; q++) tt[g * st.Length + q] = st[q] + ob;
                }
                m.vertices = vv; m.normals = nn; m.uv = uu; m.triangles = tt; m.RecalculateBounds();
                var go = new GameObject(TreeKeys[k] + "_" + tag);
                go.transform.SetParent(parentT, false);
                go.AddComponent<MeshFilter>().sharedMesh = m;
                go.AddComponent<MeshRenderer>().sharedMaterial = mats[k];
            }
            // the map budget is shared in proportion to hill area, so hills late in the order are not starved
            float totalArea = 0f;
            foreach (var e in hills) if (objects.TryGetValue(e.id, out var hg2)) { var c2 = hg2.GetComponentInChildren<Collider>(); if (c2 != null) totalArea += c2.bounds.size.x * c2.bounds.size.z; }
            foreach (var e in hills)
            {
                if (!objects.TryGetValue(e.id, out var hillGo)) continue;
                var col = hillGo.GetComponentInChildren<Collider>();
                if (col == null) continue;
                res.hills++;
                var b = col.bounds;
                // per tree type: transforms and the shading normal (ground normal blended towards up), so a tree on a slope facing
                // away from the sun is as dark as that slope (cards with a plain up normal lit backlit hills like full sun)
                var groups = new List<(Matrix4x4 trs, Vector3 n)>[TreeKeys.Length];
                for (int k = 0; k < groups.Length; k++) groups[k] = new List<(Matrix4x4, Vector3)>();
                float area = b.size.x * b.size.z;
                int tries = Mathf.Min(MaxTreesPerHill * 3, Mathf.RoundToInt(area * TreesPerM2 * 1.6f));
                int placed = 0;
                int budget = Mathf.Min(MaxTreesPerHill, Mathf.CeilToInt(MaxTrees * area / Mathf.Max(totalArea, 1f)));
                var shore = new List<(Vector3 p, Vector3 n)>();
                bool TryPlace(float x, float z, bool harborPass)
                {
                    float hb = Harborness(x, z);
                    if (harborPass && rng.NextDouble() > hb) return false;
                    if (!col.Raycast(new Ray(new Vector3(x, b.max.y + 50f, z), Vector3.down), out var hit, b.size.y + 200f)) return false;
                    float alt = hit.point.y - wy;
                    if (alt < 1.5f && alt > -1.0f) { if (!harborPass && shore.Count < 400) shore.Add((hit.point, hit.normal)); if (hb < 0.5f) return false; }
                    // shore band and steep rock faces stay bare; on the harbour side the forest comes down to ~1.8 m and onto
                    // steeper ground
                    bool inCamZone = false;
                    foreach (var c in camView) if (new Vector2(hit.point.x - c.pos.x, hit.point.z - c.pos.z).sqrMagnitude < ViewKeepout * ViewKeepout) { inCamZone = true; break; }
                    // the forest reaches the shore everywhere, as a low scrub edge (no bare band showing the hill texture, R03/R08)
                    if (alt < 1.8f) { if (inCamZone) rej[0]++; return false; }
                    if (hit.normal.y < Mathf.Lerp(0.60f, 0.55f, hb)) { if (inCamZone) rej[1]++; return false; }   // forest also holds the steep coastal flanks (~53-57 deg), as on Korean headlands (R03)
                    // a few small clearings from a low-frequency mask (none on the harbour side)
                    float patch = Mathf.PerlinNoise(x * 0.004f + 31f, z * 0.004f + 7f);
                    if (hb < 0.3f && patch < 0.18f && rng.NextDouble() < 0.8) { if (inCamZone) rej[2]++; return false; }
                    // lighthouse: bare pad within 6 m, knee-high shrubs to 12 m (keeps the tower in view), trees beyond
                    bool lhRing = false;
                    foreach (var ls in lighthouseSpots)
                    {
                        float d2 = (new Vector2(hit.point.x, hit.point.z) - ls).sqrMagnitude;
                        if (d2 < 36f) return false;
                        if (d2 < 144f) lhRing = true;
                    }
                    if (pathCells.Count > 0 && pathCells.Contains((Mathf.FloorToInt(hit.point.x / 4f), Mathf.FloorToInt(hit.point.z / 4f)))) return false;
                    bool camHill = false;
                    foreach (var c in camView) if (new Vector2(hit.point.x - c.pos.x, hit.point.z - c.pos.z).sqrMagnitude < ViewKeepout * ViewKeepout) { camHill = true; break; }
                    // not on houses, roads, terraces or other structures standing on the hill
                    int no = Physics.OverlapSphereNonAlloc(hit.point + Vector3.up * 2.5f, 2.2f, overlap);
                    bool blocked = false;
                    for (int q = 0; q < no; q++) if (!hillCols.Contains(overlap[q]) && !treeCols.Contains(overlap[q])) { blocked = true; break; }
                    if (blocked) { if (inCamZone) rej[4]++; return false; }
                    // stands, not salt-and-pepper (R07): pine / broadleaf share and canopy height follow low-frequency patches,
                    // pines a little more common higher up; the shore edge is low scrub, rising to full height by ~12 m
                    float stand = Mathf.PerlinNoise(x * 0.0045f + 71f, z * 0.0045f + 13f);
                    float pineShare = Mathf.Clamp(Mathf.Lerp(0.1f, 0.9f, stand) + (alt - 60f) / 400f, 0.05f, 0.95f);
                    int k = rng.NextDouble() < pineShare ? rng.Next(0, 3) : 3;
                    float height = k == 3 ? Mathf.Lerp(7f, 12f, (float)rng.NextDouble()) : Mathf.Lerp(9f, 15f, (float)rng.NextDouble());
                    height *= 0.75f + 0.5f * Mathf.PerlinNoise(x * 0.012f + 5f, z * 0.012f + 91f);
                    float spread = 1f;                                              // crown width / height factor (scrub is wider)
                    if (alt < 12f)
                    {
                        // shore scrub: patchy (gaps where the clump noise is low), mixed broadleaf / low pine, sizes 1.5-5 m and
                        // crowns up to 1.7x wider than tall, so it reads as one irregular thicket instead of rows of equal domes
                        // (detail review 2026-11-09: repeating band on the coast)
                        float clump = Mathf.PerlinNoise(x * 0.03f + 17f, z * 0.03f + 3f);
                        if (alt < 7f)
                        {
                            if (clump < 0.32f && rng.NextDouble() < 0.7) return false;
                            // sparse: dense low cross-card impostors overlapped into rows of pale "shells" when seen from above
                            // (detail review 2026-11-09); the olive scrub band of the hill texture carries the shore thicket
                            if (rng.NextDouble() < 0.75) return false;
                            k = rng.NextDouble() < 0.6 ? 3 : 2;
                            height = Mathf.Lerp(1.5f, 5f, Mathf.Pow((float)rng.NextDouble(), 0.8f)) * (0.7f + 0.6f * clump);
                            spread = Mathf.Lerp(1.1f, 1.7f, (float)rng.NextDouble());
                        }
                        else { height *= Mathf.Lerp(0.55f, 1f, (alt - 7f) / 5f); spread = Mathf.Lerp(1.0f, 1.3f, (float)rng.NextDouble()); }
                    }
                    if (lhRing) { k = 3; height = Mathf.Lerp(0.9f, 1.6f, (float)rng.NextDouble()); spread = 1.4f; }
                    // in front of a camera: a full tree if only hill lies behind it, otherwise low scrub (2.5-4 m), otherwise none
                    if (camHill && !CameraOk(hit.point, height))
                    {
                        height = Mathf.Lerp(2.5f, 4f, (float)rng.NextDouble());
                        if (!CameraOk(hit.point, height))
                        {
                            // knee-to-head-high ground shrubs on the camera's own slope, at least 6 m above the water, so the
                            // slope it looks down is not a bare strip; too low to hide anything that floats
                            if (alt < 6f) { rej[3]++; return false; }
                            foreach (var c in camView) if (new Vector2(hit.point.x - c.pos.x, hit.point.z - c.pos.z).sqrMagnitude < CameraKeepout * CameraKeepout) return false;
                            height = Mathf.Lerp(1.0f, 1.8f, (float)rng.NextDouble());
                            rej[7]++;
                        }
                        else rej[6]++;
                    }
                    else if (camHill) rej[5]++;
                    // close to a camera and in its frame: a real 3-D tree instead of the crossed-card impostor (detail review §4)
                    if (near3d < MaxNear3D && SeenNear(hit.point, height) && near3dPrefabs[0] != null)
                    {
                        int k3 = k == 3 ? 0 : 1;
                        var go3 = Object.Instantiate(near3dPrefabs[k3], root);
                        go3.name = "tree3d_" + near3d;
                        go3.transform.SetPositionAndRotation(hit.point - Vector3.up * 0.3f, Quaternion.Euler(0, (float)rng.NextDouble() * 360f, 0));
                        go3.transform.localScale = Vector3.one * (height / near3dHeights[k3]);
                        foreach (var cc in go3.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(cc);
                        near3d++; res.trees++; res.near_3d_trees++;
                        return true;
                    }
                    float s = height / Mathf.Max(meshes[k].bounds.size.y, 0.1f);
                    var rot = Quaternion.Euler((float)(rng.NextDouble() - 0.5) * 6f, (float)rng.NextDouble() * 360f, (float)(rng.NextDouble() - 0.5) * 6f);
                    var trs = Matrix4x4.TRS(hit.point - Vector3.up * 0.3f, rot, new Vector3(s * spread, s, s * spread));
                    groups[k].Add((trs, Vector3.Normalize(hit.normal * 0.75f + Vector3.up * 0.25f)));
                    res.trees++;
                    return true;
                }
                for (int i = 0; i < tries && placed < budget; i++)
                    if (TryPlace(Mathf.Lerp(b.min.x, b.max.x, (float)rng.NextDouble()), Mathf.Lerp(b.min.z, b.max.z, (float)rng.NextDouble()), false)) placed++;
                // second pass on the part of the hill that faces the harbour
                if (haveHarbor)
                {
                    float x0 = Mathf.Max(b.min.x, hMin.x - HarborFar), x1 = Mathf.Min(b.max.x, hMax.x + HarborFar);
                    float z0 = Mathf.Max(b.min.z, hMin.y - HarborFar), z1 = Mathf.Min(b.max.z, hMax.y + HarborFar);
                    if (x1 > x0 && z1 > z0)
                    {
                        int extraTries = Mathf.Min(MaxHarborTreesPerHill * 4, Mathf.RoundToInt((x1 - x0) * (z1 - z0) * HarborExtraPerM2 * 1.6f));
                        int extra = 0;
                        for (int i = 0; i < extraTries && extra < MaxHarborTreesPerHill; i++)
                            if (TryPlace(Mathf.Lerp(x0, x1, (float)rng.NextDouble()), Mathf.Lerp(z0, z1, (float)rng.NextDouble()), true)) extra++;
                        res.harbor_trees += extra;
                    }
                }
                // third pass in front of each camera (within ViewKeepout): the slope a camera looks down only takes low scrub and
                // shrubs, so it is planted ~4x denser to cover the pale meadow ground there
                foreach (var c in camView)
                {
                    float x0 = Mathf.Max(b.min.x, c.pos.x - ViewKeepout), x1 = Mathf.Min(b.max.x, c.pos.x + ViewKeepout);
                    float z0 = Mathf.Max(b.min.z, c.pos.z - ViewKeepout), z1 = Mathf.Min(b.max.z, c.pos.z + ViewKeepout);
                    if (x1 <= x0 || z1 <= z0) continue;
                    int camTries = Mathf.RoundToInt((x1 - x0) * (z1 - z0) / 4f);
                    for (int i = 0; i < camTries; i++)
                    {
                        float x = Mathf.Lerp(x0, x1, (float)rng.NextDouble()), z = Mathf.Lerp(z0, z1, (float)rng.NextDouble());
                        if ((x - c.pos.x) * (x - c.pos.x) + (z - c.pos.z) * (z - c.pos.z) >= ViewKeepout * ViewKeepout) continue;
                        if (TryPlace(x, z, false)) res.camera_zone_trees++;
                    }
                }
                if (haveTrees) for (int k = 0; k < groups.Length; k++) MergeTrees(groups[k], k, e.id, root);
                // coastline pieces along the hill's shore band, at least 40 m apart, long axis along the shoreline
                if (coast != null)
                    foreach (var (p, n) in shore.OrderBy(_ => rng.Next()))
                    {
                        if (coastPlaced.Count >= MaxCoastPieces) break;
                        var xz = new Vector2(p.x, p.z);
                        if (coastPlaced.Any(q => (q - xz).sqrMagnitude < 40f * 40f)) continue;
                        if (def.InAnyWaterRegion(xz)) continue;                     // never into navigable / vessel water
                        // natural shore only: no quay wall, walk, breakwater or road within 25 m (the flat scan sheet read as a slab
                        // floating beside the CT east walk, detail review 2026-11-09)
                        int nb = Physics.OverlapSphereNonAlloc(p, 25f, overlap);
                        bool built = false;
                        for (int q = 0; q < nb; q++) if (!hillCols.Contains(overlap[q]) && !treeCols.Contains(overlap[q])) { built = true; break; }
                        if (built) continue;
                        // and not in the near field of a camera, where the flat scan sheet's edge shows (it reads as a slab on the water)
                        if (camView.Any(c => new Vector2(p.x - c.pos.x, p.z - c.pos.z).sqrMagnitude < 300f * 300f)) continue;
                        var down = new Vector3(n.x, 0, n.z);                         // horizontal downslope = towards the sea
                        if (down.sqrMagnitude < 1e-4f) continue;
                        float yaw = Mathf.Atan2(down.x, down.z) * Mathf.Rad2Deg + 90f;   // long axis (x) along the shore
                        var go = Object.Instantiate(coast, root);
                        go.name = "shore_line_" + coastPlaced.Count;
                        go.transform.SetPositionAndRotation(new Vector3(p.x, wy - 1.2f, p.z), Quaternion.Euler(0, yaw, 0));
                        go.transform.localScale = Vector3.one * Mathf.Lerp(0.6f, 0.85f, (float)rng.NextDouble());
                        colliders.AddRange(go.GetComponentsInChildren<Collider>(true));
                        coastPlaced.Add(xz);
                    }
            }
            res.coast_pieces = coastPlaced.Count;
            if (haveTrees)
            {
                var flat = new List<(Matrix4x4 trs, Vector3 n)>[TreeKeys.Length];
                for (int k = 0; k < flat.Length; k++) flat[k] = new List<(Matrix4x4, Vector3)>();
                // reach: FlatReach around the inner harbour (or the map origin)
                Vector2 c0 = haveHarbor ? (hMin + hMax) / 2f : Vector2.zero;
                var lands = def.elements.Where(el => el.shape == "box" && el.material == "grass" && el.size.x * el.size.z > 4000f)
                    .OrderBy(el => el.id, System.StringComparer.Ordinal).ToList();
                foreach (var el in lands)
                {
                    if (!objects.TryGetValue(el.id, out var lg)) continue;
                    var lcol = lg.GetComponent<Collider>(); if (lcol == null) continue;
                    var lb = lcol.bounds;
                    float x0 = Mathf.Max(lb.min.x, c0.x - FlatReach), x1 = Mathf.Min(lb.max.x, c0.x + FlatReach);
                    float z0 = Mathf.Max(lb.min.z, c0.y - FlatReach), z1 = Mathf.Min(lb.max.z, c0.y + FlatReach);
                    if (x1 <= x0 || z1 <= z0) continue;
                    int fTries = Mathf.RoundToInt((x1 - x0) * (z1 - z0) * FlatTriesPerM2);
                    for (int i = 0; i < fTries && res.flat_trees < MaxFlatTrees; i++)
                    {
                        float x = Mathf.Lerp(x0, x1, (float)rng.NextDouble()), z = Mathf.Lerp(z0, z1, (float)rng.NextDouble());
                        float patch = Mathf.PerlinNoise(x * 0.0032f + 113f, z * 0.0032f + 57f);
                        double keep = patch > 0.56f ? 1.0 : patch > 0.44f ? 0.22 : 0.025;
                        if (rng.NextDouble() > keep) continue;
                        if (!Physics.Raycast(new Vector3(x, lb.max.y + 300f, z), Vector3.down, out var fh, 400f) || fh.collider != lcol) continue;
                        if (def.InAnyWaterRegion(new Vector2(x, z))) continue;
                        int no = Physics.OverlapSphereNonAlloc(fh.point + Vector3.up * 3f, 3f, overlap);
                        bool blocked = false;
                        for (int q = 0; q < no; q++) if (overlap[q] != lcol && !hillCols.Contains(overlap[q]) && !treeCols.Contains(overlap[q])) { blocked = true; break; }
                        if (blocked) continue;
                        int k = rng.NextDouble() < 0.6 ? 3 : rng.Next(0, 3);
                        float height = k == 3 ? Mathf.Lerp(7f, 12f, (float)rng.NextDouble()) : Mathf.Lerp(9f, 15f, (float)rng.NextDouble());
                        if (!CameraOk(fh.point, height)) continue;      // in a camera frame only hill may stand behind a tree
                        float sc = height / Mathf.Max(meshes[k].bounds.size.y, 0.1f);
                        flat[k].Add((Matrix4x4.TRS(fh.point - Vector3.up * 0.2f, Quaternion.Euler(0, (float)rng.NextDouble() * 360f, 0), Vector3.one * sc), Vector3.up));
                        res.flat_trees++;
                    }
                }
                for (int k = 0; k < flat.Length; k++) MergeTrees(flat[k], k, "flat", root);
            }
            // flat-land trees from the map builders beyond the scanned-prefab budget (towns, parks, car parks, scrub): billboard
            // impostors, broadleaf mostly, one merged mesh per type
            if (haveTrees)
            {
                var town = new List<(Matrix4x4 trs, Vector3 n)>[TreeKeys.Length];
                for (int k = 0; k < town.Length; k++) town[k] = new List<(Matrix4x4, Vector3)>();
                foreach (var el in def.elements)
                {
                    if (el.shape != "billboard_tree") continue;
                    int k = rng.NextDouble() < 0.7 ? 3 : rng.Next(0, 3);
                    float s = Mathf.Max(el.size.y, 1f) / Mathf.Max(meshes[k].bounds.size.y, 0.1f);
                    town[k].Add((Matrix4x4.TRS(el.center, Quaternion.Euler(0, (float)rng.NextDouble() * 360f, 0), Vector3.one * s), Vector3.up));
                    res.town_trees++;
                }
                for (int k = 0; k < town.Length; k++) MergeTrees(town[k], k, "town", root);
            }
            Debug.Log($"[Vegetation] {def.harbor_id} camera-zone samples: alt {rej[0]} steep {rej[1]} clearing {rej[2]} camera {rej[3]} blocked {rej[4]} full {rej[5]} scrub {rej[6]} shrub {rej[7]}");
            return res;
        }
    }
}
