// Read-only audit tool. Does not rebuild, save, or modify any existing assets.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace PortCCTV.EditorTools
{
    public static class AssetQualityReview
    {
        const string Root = "Assets/PortCCTVSimulator";
        const string Output = "docs/asset_quality_review_20261109";
        [Serializable] public class Row
        {
            public string key, group, path, source, source_url, license, image, preview_error;
            public long rendered_triangles;
            public int mesh_renderers, materials, textured_materials, lod_groups;
            public Vector3 bounds_size;
        }
        [Serializable] public class Report
        {
            public string utc, unity_version;
            public List<Row> assets = new List<Row>();
            public int preview_ok, preview_failed;
        }
        public static void Run()
        {
            Directory.CreateDirectory(Output + "/previews");
            var report = new Report { utc = DateTime.UtcNow.ToString("o"), unity_version = Application.unityVersion };
            var seen = new HashSet<string>();
            void Add(string key, string group, GameObject prefab, string source, string url = "", string license = "")
            {
                if (prefab == null) return;
                string path = AssetDatabase.GetAssetPath(prefab);
                if (!seen.Add(path)) return;
                var row = new Row { key = key, group = group, path = path, source = source, source_url = url, license = license };
                var renderers = prefab.GetComponentsInChildren<Renderer>(true);
                row.mesh_renderers = renderers.Length;
                var mats = renderers.SelectMany(r => r.sharedMaterials).Where(m => m != null).Distinct().ToArray();
                row.materials = mats.Length;
                row.textured_materials = mats.Count(m => (m.HasProperty("_BaseColorMap") && m.GetTexture("_BaseColorMap") != null) || (m.HasProperty("_MainTex") && m.GetTexture("_MainTex") != null));
                row.lod_groups = prefab.GetComponentsInChildren<LODGroup>(true).Length;
                bool first = true; Bounds bounds = default;
                foreach (var renderer in renderers)
                {
                    if (first) { bounds = renderer.bounds; first = false; } else bounds.Encapsulate(renderer.bounds);
                    Mesh mesh = renderer is SkinnedMeshRenderer skin ? skin.sharedMesh : renderer.GetComponent<MeshFilter>()?.sharedMesh;
                    if (mesh == null) continue;
                    for (int i = 0; i < mesh.subMeshCount; i++) if (mesh.GetTopology(i) == MeshTopology.Triangles) row.rendered_triangles += (long)mesh.GetIndexCount(i) / 3;
                }
                row.bounds_size = bounds.size;
                UnityEditor.Editor editor = null;
                try
                {
                    editor = UnityEditor.Editor.CreateEditor(prefab);
                    var tex = editor.RenderStaticPreview(path, null, 384, 384);
                    if (tex == null) throw new InvalidOperationException("RenderStaticPreview returned null");
                    row.image = "previews/" + group + "__" + key + ".png";
                    File.WriteAllBytes(Output + "/" + row.image, tex.EncodeToPNG());
                    UnityEngine.Object.DestroyImmediate(tex);
                    report.preview_ok++;
                }
                catch (Exception e) { row.preview_error = e.Message; report.preview_failed++; }
                finally { if (editor != null) UnityEngine.Object.DestroyImmediate(editor); }
                report.assets.Add(row);
                File.WriteAllText(Output + "/unity_asset_inventory.json", JsonUtility.ToJson(report, true));
                Debug.Log($"[AssetQualityReview] {report.assets.Count} {group}/{key} triangles={row.rendered_triangles} preview={(row.image ?? row.preview_error)}");
            }
            try
            {
                var theme = AssetDatabase.LoadAssetAtPath<HarborTheme>(Root + "/Resources/PortCCTV/HarborTheme.asset");
                var vessels = AssetDatabase.LoadAssetAtPath<VesselCatalog>(Root + "/Resources/PortCCTV/VesselCatalog.asset");
                foreach (var e in theme.prefabs.OrderBy(e => e.key)) Add(e.key, "harbor", e.prefab, e.source);
                foreach (var e in vessels.entries.OrderBy(e => e.asset_id)) Add(e.asset_id, "vessel", e.prefab, e.source_file, e.source_url, e.license);
                foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { Root }))
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    Add(Path.GetFileNameWithoutExtension(path), "uncatalogued", AssetDatabase.LoadAssetAtPath<GameObject>(path), "not in HarborTheme/VesselCatalog");
                }
                Debug.Log($"[AssetQualityReview] COMPLETE total={report.assets.Count} preview_ok={report.preview_ok} preview_failed={report.preview_failed}");
                EditorApplication.Exit(0);
            }
            catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }
    }
}
