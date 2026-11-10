using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace PortCCTV.EditorTools
{
    /// <summary>
    /// Targeted fix of the barge stockpile (2026-11-10, user: "바지선 모래부분이 지지직 거려"): the pile's flat floor was coplanar with
    /// the deck plate top and z-fought. Regenerates the pile mesh in place (same asset / GUID, so the prefab, its MeshCollider and
    /// the catalog keep their references), moves the heap origin onto the deck plate top, and recomputes the barge's label
    /// geometry with the bootstrap's own extractor. Nothing else is rebuilt (a full bootstrap would regenerate every asset).
    ///   Unity.exe -batchmode -projectPath &lt;root&gt; -executeMethod PortCCTV.EditorTools.BargePileFix.RunBatch -logFile Logs/barge_fix.log
    /// </summary>
    public static class BargePileFix
    {
        const string MeshPath = "Assets/PortCCTVSimulator/Meshes/barge_aggregate_pile.asset";
        const string PrefabPath = "Assets/PortCCTVSimulator/Vessels/vessel_barge.prefab";
        const string GeomPath = "Assets/PortCCTVSimulator/Vessels/vessel_barge_geometry.asset";

        public static void RunBatch()
        {
            int code = 0;
            try { Run(); }
            catch (Exception ex) { Debug.LogException(ex); code = 1; }
            EditorApplication.Exit(code);
        }

        public static void Run()
        {
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath) ?? throw new InvalidOperationException("missing " + MeshPath);
            var fresh = ProceduralVessels.AggregatePile(30f, 11.4f, 4.2f, 9071);       // same parameters as BuildBarge
            float oldMin = mesh.bounds.min.y, oldMax = mesh.bounds.max.y;
            mesh.Clear();
            mesh.vertices = fresh.vertices; mesh.uv = fresh.uv; mesh.triangles = fresh.triangles;
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            EditorUtility.SetDirty(mesh);
            UnityEngine.Object.DestroyImmediate(fresh);
            Debug.Log($"[BargePileFix] pile mesh y {oldMin:F3}..{oldMax:F3} -> {mesh.bounds.min.y:F3}..{mesh.bounds.max.y:F3}");

            var root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                var heap = root.GetComponentsInChildren<Transform>(true).Single(t => t.name == "aggregate_heap");
                var lp = heap.localPosition;
                heap.localPosition = new Vector3(lp.x, 3.30f, lp.z);
                Debug.Log($"[BargePileFix] heap origin y {lp.y:F3} -> 3.300 (deck plate top)");
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            try
            {
                inst.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                var extract = typeof(ProjectBootstrap).GetMethod("ExtractGeometry", BindingFlags.NonPublic | BindingFlags.Static)
                              ?? throw new MissingMethodException("ProjectBootstrap.ExtractGeometry");
                var args = new object[] { inst, ProceduralVessels.HullNodeName, 0 };
                var geom = (VesselGeometry)extract.Invoke(null, args);
                var old = AssetDatabase.LoadAssetAtPath<VesselGeometry>(GeomPath) ?? throw new InvalidOperationException("missing " + GeomPath);
                Debug.Log($"[BargePileFix] geometry vertices {old.vertices_root_space.Length} -> {geom.vertices_root_space.Length}; bounds " +
                          $"({old.bounds_min} .. {old.bounds_max}) -> ({geom.bounds_min} .. {geom.bounds_max}); hull ref min y {old.hull_reference_min_y:F4} -> {geom.hull_reference_min_y:F4}");
                EditorUtility.CopySerialized(geom, old);
                EditorUtility.SetDirty(old);
                UnityEngine.Object.DestroyImmediate(geom);
            }
            finally { UnityEngine.Object.DestroyImmediate(inst); }
            AssetDatabase.SaveAssets();
            Debug.Log("[BargePileFix] done");
        }
    }
}
