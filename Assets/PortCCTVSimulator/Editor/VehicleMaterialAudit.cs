using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace PortCCTV.EditorTools
{
    /// <summary>Dev tool: lists, per car / van prefab, each renderer's materials with base colour, texture and the mesh's
    /// surface area, to pick the body-paint material for recolouring (Logs/vehicle_materials.txt).
    /// -executeMethod PortCCTV.EditorTools.VehicleMaterialAudit.RunBatch</summary>
    public static class VehicleMaterialAudit
    {
        public static void RunBatch()
        {
            var sb = new StringBuilder();
            foreach (var path in Directory.GetFiles("Assets/PortCCTVSimulator/HarborPrefabs", "*.prefab").Where(p => { var n = Path.GetFileNameWithoutExtension(p); return n.StartsWith("car_") || n.StartsWith("van_") || n == "truck_kei"; }).OrderBy(p => p))
            {
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path.Replace('\\', '/'));
                sb.AppendLine("== " + go.name);
                foreach (var r in go.GetComponentsInChildren<MeshRenderer>(true))
                {
                    var mf = r.GetComponent<MeshFilter>(); var m = mf != null ? mf.sharedMesh : null;
                    float area = 0f;
                    if (m != null && m.isReadable) { var v = m.vertices; var t = m.triangles; for (int i = 0; i < t.Length; i += 3) area += Vector3.Cross(v[t[i + 1]] - v[t[i]], v[t[i + 2]] - v[t[i]]).magnitude * 0.5f; area *= r.transform.lossyScale.x * r.transform.lossyScale.x; }
                    foreach (var mat in r.sharedMaterials)
                    {
                        if (mat == null) continue;
                        var c = mat.HasProperty("_BaseColor") ? mat.GetColor("_BaseColor") : Color.white;
                        var tex = mat.HasProperty("_BaseColorMap") ? mat.GetTexture("_BaseColorMap") : null;
                        sb.AppendLine($"  {r.name} | {mat.name} | col {c.r:F2},{c.g:F2},{c.b:F2} | tex {(tex != null ? tex.name + " " + tex.width : "-")} | area {area:F1} | mesh {(m != null ? m.name : "-")}");
                    }
                }
            }
            File.WriteAllText("Logs/vehicle_materials.txt", sb.ToString());
            EditorApplication.Exit(0);
        }
    }
}
