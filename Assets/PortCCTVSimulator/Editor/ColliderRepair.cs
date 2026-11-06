using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace PortCCTV.EditorTools
{
    /// <summary>
    /// Finds MeshColliders whose mesh PhysX refuses to cook ("One of the triangles is too large") and gives them a
    /// collider-only copy of the same surface with the large triangles split (longest-edge midpoint subdivision).
    /// The rendered mesh is untouched; the collider keeps the rendered shape, so placement checks, navigation obstacle
    /// tests and the CPU visibility mask see the object again. A collider that still fails after splitting, or whose mesh
    /// is not readable, is reported and replaced by a BoxCollider of the mesh bounds (flagged in the report).
    ///
    /// Batch:  Unity.exe -batchmode -projectPath &lt;root&gt; -executeMethod PortCCTV.EditorTools.ColliderRepair.RunBatch -logFile Logs/collider_repair.log
    /// Audit only (no changes): ...ColliderRepair.AuditBatch
    /// Report: Logs/collider_repair_report.json (overwritten per run; earlier reports keep their timestamped copies).
    /// </summary>
    public static class ColliderRepair
    {
        public const string OutDir = "Assets/PortCCTVSimulator/Physics/ColliderMeshes";
        const int MaxSplitTriangles = 3_000_000;
        /// <summary>PhysX build-time validation rejects triangles with an edge longer than 500 x tolerance length (1 m). The
        /// Editor's Physics.BakeMesh does not run that validation, so meshes are judged by edge length; 400 leaves a margin.</summary>
        public const float MaxEdgeLocal = 400f;

        [Serializable] public class Item
        {
            public string prefab, path, mesh, action, note;
            public float max_edge_before, max_edge_after;
            public int tris_before, tris_after;
        }
        [Serializable] public class Report
        {
            public string utc; public bool audit_only;
            public int prefabs_scanned, colliders_scanned, failing_meshes, repaired, box_fallback, unreadable;
            public List<Item> items = new List<Item>();
        }

        public static void RunBatch() => Batch(false);
        public static void AuditBatch() => Batch(true);

        static void Batch(bool auditOnly)
        {
            try { var r = Run(auditOnly); EditorApplication.Exit(r.failing_meshes > 0 && auditOnly ? 2 : 0); }
            catch (Exception ex) { Debug.LogError("[ColliderRepair] FAILED: " + ex); EditorApplication.Exit(1); }
        }

        /// <summary>True when PhysX cooks the mesh with these options (captures the cooking error log).</summary>
        public static bool Cooks(Mesh mesh, bool convex, MeshColliderCookingOptions opts)
        {
            bool failed = false;
            void H(string msg, string st, LogType t) { if (msg.Contains("Failed to create Physics Mesh") || msg.Contains("cooking")) failed = true; }
            Application.logMessageReceived += H;
            try { Physics.BakeMesh(mesh.GetInstanceID(), convex, opts); }
            finally { Application.logMessageReceived -= H; }
            return !failed;
        }

        public static Report Run(bool auditOnly)
        {
            var rep = new Report { utc = DateTime.UtcNow.ToString("o"), audit_only = auditOnly };
            if (!auditOnly && !AssetDatabase.IsValidFolder(OutDir))
            {
                var parts = OutDir.Split('/'); var cur = parts[0];
                for (int i = 1; i < parts.Length; i++) { var n = cur + "/" + parts[i]; if (!AssetDatabase.IsValidFolder(n)) AssetDatabase.CreateFolder(cur, parts[i]); cur = n; }
            }
            var cache = new Dictionary<(Mesh, bool, MeshColliderCookingOptions), (bool ok, Mesh fixedMesh, Item info)>();
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/PortCCTVSimulator" }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var root = PrefabUtility.LoadPrefabContents(path);
                bool changed = false;
                try
                {
                    rep.prefabs_scanned++;
                    foreach (var mc in root.GetComponentsInChildren<MeshCollider>(true))
                    {
                        if (mc.sharedMesh == null) continue;
                        rep.colliders_scanned++;
                        var key = (mc.sharedMesh, mc.convex, mc.cookingOptions);
                        if (!cache.TryGetValue(key, out var res))
                        {
                            res = Examine(mc.sharedMesh, mc.convex, mc.cookingOptions, auditOnly, rep);
                            cache[key] = res;
                        }
                        if (res.ok) continue;
                        var item = new Item
                        {
                            prefab = path, path = HierarchyPath(mc.transform, root.transform), mesh = mc.sharedMesh.name,
                            max_edge_before = res.info.max_edge_before, max_edge_after = res.info.max_edge_after,
                            tris_before = res.info.tris_before, tris_after = res.info.tris_after, note = res.info.note,
                        };
                        if (auditOnly) item.action = "fails_to_cook";
                        else if (res.fixedMesh != null) { mc.sharedMesh = res.fixedMesh; item.action = "split_large_triangles"; changed = true; rep.repaired++; }
                        else
                        {
                            var go = mc.gameObject; var b = mc.sharedMesh.bounds;
                            UnityEngine.Object.DestroyImmediate(mc);
                            var bc = go.AddComponent<BoxCollider>(); bc.center = b.center; bc.size = b.size;
                            item.action = "box_fallback"; changed = true; rep.box_fallback++;
                        }
                        rep.items.Add(item);
                    }
                    if (changed) PrefabUtility.SaveAsPrefabAsset(root, path);
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            rep.failing_meshes = cache.Values.Count(v => !v.ok);
            AssetDatabase.SaveAssets();
            Directory.CreateDirectory("Logs");
            var json = JsonUtility.ToJson(rep, true);
            File.WriteAllText("Logs/collider_repair_report.json", json, new UTF8Encoding(false));
            File.WriteAllText($"Logs/collider_repair_report_{DateTime.UtcNow:yyyyMMddTHHmmss}.json", json, new UTF8Encoding(false));
            Debug.Log($"[ColliderRepair] audit_only={auditOnly} prefabs={rep.prefabs_scanned} colliders={rep.colliders_scanned} failing_meshes={rep.failing_meshes} repaired_colliders={rep.repaired} box_fallback={rep.box_fallback} unreadable={rep.unreadable}");
            return rep;
        }

        static (bool ok, Mesh fixedMesh, Item info) Examine(Mesh mesh, bool convex, MeshColliderCookingOptions opts, bool auditOnly, Report rep)
        {
            var info = new Item();
            if (!mesh.isReadable) { info.note = "mesh not readable"; rep.unreadable++; return (Cooks(mesh, convex, opts), null, info); }
            var v = mesh.vertices; var t = TriangleIndices(mesh);
            info.tris_before = t.Length / 3;
            info.max_edge_before = MaxEdge(v, t);
            if (info.max_edge_before <= MaxEdgeLocal && Cooks(mesh, convex, opts)) return (true, null, info);
            if (auditOnly) return (false, null, info);
            if (convex) { info.note = "convex collider: splitting does not change the hull"; return (false, null, info); }
            // Halve the allowed edge length until PhysX accepts the mesh.
            float limit = Mathf.Min(MaxEdgeLocal, info.max_edge_before * 0.5f);
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(AssetPathFor(mesh));
            for (int round = 0; round < 12; round++)
            {
                var (nv, nt) = Split(v, t, limit);
                if (nt.Count / 3 > MaxSplitTriangles) { info.note = $"split would exceed {MaxSplitTriangles} triangles"; break; }
                var m = new Mesh { name = mesh.name + "_collider", indexFormat = nv.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
                m.SetVertices(nv); m.SetTriangles(nt, 0); m.RecalculateBounds();
                if (MaxEdge(nv.ToArray(), nt.ToArray()) <= MaxEdgeLocal && Cooks(m, false, opts))
                {
                    info.tris_after = nt.Count / 3; info.max_edge_after = MaxEdge(nv.ToArray(), nt.ToArray());
                    info.note = $"limit {limit:G4} local units, round {round}";
                    var p = AssetPathFor(mesh);
                    if (existing != null) { existing.Clear(); EditorUtility.CopySerialized(m, existing); UnityEngine.Object.DestroyImmediate(m); m = existing; }
                    else AssetDatabase.CreateAsset(m, p);
                    return (false, m, info);
                }
                UnityEngine.Object.DestroyImmediate(m);
                limit *= 0.5f;
            }
            return (false, null, info);
        }

        /// <summary>Triangle indices of all triangle submeshes (line/point submeshes are skipped; PhysX ignores them too).</summary>
        static int[] TriangleIndices(Mesh mesh)
        {
            var all = new List<int>();
            for (int s = 0; s < mesh.subMeshCount; s++)
                if (mesh.GetTopology(s) == MeshTopology.Triangles) all.AddRange(mesh.GetTriangles(s));
            return all.ToArray();
        }

        static string AssetPathFor(Mesh mesh)
        {
            var src = AssetDatabase.GetAssetPath(mesh);
            var id = ScenarioIO.Sha256Hex(Encoding.UTF8.GetBytes(src + "|" + mesh.name + "|" + mesh.vertexCount + "|" + mesh.GetIndexCount(0))).Substring(0, 12);
            var safe = new string(mesh.name.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());
            if (safe.Length > 40) safe = safe.Substring(0, 40);
            return $"{OutDir}/{safe}_{id}.asset";
        }

        static string HierarchyPath(Transform t, Transform root)
        {
            var parts = new List<string>();
            for (var c = t; c != null && c != root; c = c.parent) parts.Add(c.name);
            parts.Reverse(); return string.Join("/", parts);
        }

        public static float MaxEdge(Vector3[] v, int[] t)
        {
            float m = 0f;
            for (int i = 0; i < t.Length; i += 3)
            {
                var a = v[t[i]]; var b = v[t[i + 1]]; var c = v[t[i + 2]];
                m = Mathf.Max(m, (a - b).sqrMagnitude, (b - c).sqrMagnitude, (c - a).sqrMagnitude);
            }
            return Mathf.Sqrt(m);
        }

        /// <summary>Longest-edge midpoint subdivision until every edge is at most <paramref name="limit"/>. Midpoints are shared
        /// between neighbouring triangles (keyed by the edge's vertex pair), so the surface stays watertight where it was.</summary>
        public static (List<Vector3> v, List<int> t) Split(Vector3[] verts, int[] tris, float limit)
        {
            var v = new List<Vector3>(verts);
            var outT = new List<int>(tris.Length);
            var mid = new Dictionary<long, int>();
            float lim2 = limit * limit;
            int Mid(int a, int b)
            {
                long k = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                if (!mid.TryGetValue(k, out var i)) { i = v.Count; v.Add((v[a] + v[b]) * 0.5f); mid[k] = i; }
                return i;
            }
            var stack = new Stack<(int, int, int)>();
            for (int i = 0; i < tris.Length; i += 3)
            {
                stack.Push((tris[i], tris[i + 1], tris[i + 2]));
                while (stack.Count > 0)
                {
                    var (a, b, c) = stack.Pop();
                    float ab = (v[a] - v[b]).sqrMagnitude, bc = (v[b] - v[c]).sqrMagnitude, ca = (v[c] - v[a]).sqrMagnitude;
                    float mx = Mathf.Max(ab, Mathf.Max(bc, ca));
                    if (mx <= lim2 || outT.Count / 3 > MaxSplitTriangles) { outT.Add(a); outT.Add(b); outT.Add(c); continue; }
                    if (mx == ab) { int m = Mid(a, b); stack.Push((a, m, c)); stack.Push((m, b, c)); }
                    else if (mx == bc) { int m = Mid(b, c); stack.Push((a, b, m)); stack.Push((a, m, c)); }
                    else { int m = Mid(c, a); stack.Push((a, b, m)); stack.Push((m, b, c)); }
                }
            }
            return (v, outT);
        }
    }
}
