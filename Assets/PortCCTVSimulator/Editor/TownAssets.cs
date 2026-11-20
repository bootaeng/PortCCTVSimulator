using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;
using Object = UnityEngine.Object;

namespace PortCCTV.EditorTools
{
    /// <summary>
    /// Town asset batch of 2026-11-10 (user: denser fishing-port / residential zones, Korean road signs, harbour guards).
    /// Sources and changes: ThirdParty/SketchfabCCBY/town3/SOURCE_town3.md (CC BY 4.0), PolyHavenCC0/textures_20261110 and
    /// AmbientCG/RoadLines026B (CC0).
    ///   * materials: plastered / board-formed concrete walls (Poly Haven concrete_wall_001 / _004) in five paint tints,
    ///     corrugated iron (corrugated_iron_04) in five colours, worn yellow road paint and a yellow hatched road-end
    ///     marking (ambientCG RoadLines026B colour + opacity, alpha-tested);
    ///   * shopfront_kr: "Asian Shop 4" (abhayexe) 3 m shopfront module, phone number on the glass inpainted;
    ///   * house_jp03: "Japanese Residential Home 03" (reckzilla), 1k textures, authored in cm -> metres;
    ///   * bld_hk_tower: the one brand-free apartment tower of "Full Gameready City Buildings III [HongKong]" (abhayexe),
    ///     extracted with Tools/glb_subset.py (the rest of the set carries real brand signage and is not in the project);
    ///   * kr_sign_*: single signs out of "South Korean Road Signs" (notbadworks), 256 px plates;
    ///   * guard_soldier: "AMERICAN SOLDIER RIGGED" (matheofrelin3), T-pose rig: upper arms rotated down along the body and
    ///     the skinned mesh baked to a static mesh (no animation at capture time), 1.78 m.
    /// None of these is ever labelled; they are background / non-vessel scenery.
    /// </summary>
    public static class TownAssets
    {
        const string Root = "Assets/PortCCTVSimulator";
        const string PrefabDir = Root + "/HarborPrefabs";
        const string T3 = Root + "/ThirdParty/SketchfabCCBY/town3";
        const string TX = Root + "/ThirdParty/PolyHavenCC0/textures_20261110";
        const string RL = Root + "/ThirdParty/AmbientCG/RoadLines026B";
        const string MeshDir = Root + "/HarborPrefabs/Baked";

        public static readonly string[] WallTints = { "wall_conc_grey", "wall_conc_cream", "wall_conc_blue", "wall_conc_pink", "wall_conc_green" };
        public static readonly string[] IronTints = { "corrugated_blue", "corrugated_green", "corrugated_red", "corrugated_grey", "corrugated_white" };

        /// <summary>Korean road signs taken from the pack: key -> sign root node in the GLB.</summary>
        public static readonly (string key, string node)[] Signs =
        {
            ("kr_sign_stop", "Stop"), ("kr_sign_slow", "Slow_"), ("kr_sign_yield", "Yield"), ("kr_sign_danger", "Other_dangers"),
            ("kr_sign_sl30", "Speed_limit_30"), ("kr_sign_sl50", "Speed_limit_50"), ("kr_sign_sl60", "Speed_limit_60"),
            ("kr_sign_no_parking", "Give_way_to_oncoming_traffic_8"), ("kr_sign_no_stopping", "Give_way_to_oncoming_traffic_9_2"),
            ("kr_sign_no_entry", "Give_way_to_oncoming_traffic_1"), ("kr_sign_no_u_turn", "Give_way_to_oncoming_traffic_7"),
            ("kr_sign_no_trucks", "Give_way_to_oncoming_traffic_4"), ("kr_sign_no_left", "Give_way_to_oncoming_traffic_5"), ("kr_sign_one_way", "Crosswalk_3"), ("kr_sign_parking", "Crosswalk_4"),
        };

        static readonly List<string> Report = new List<string>();

        public static void Build(HarborTheme theme, Action<string, GameObject, string> add)
        {
            Report.Clear();
            Directory.CreateDirectory(MeshDir);
            Materials(theme);
            add("shopfront_kr", Wrap("shopfront_kr", $"{T3}/asian_shop_4_abhayexe.glb", 3.0f, null, true, out var n1), "Asian Shop 4 (abhayexe, CC BY 4.0), 3 m shopfront module, phone number inpainted: " + n1);
            add("house_jp03", Wrap("house_jp03", $"{T3}/jp_residential_home_03_reckzilla.glb", 7.1f, null, false, out var n2), "Japanese Residential Home 03 (reckzilla, CC BY 4.0), 1k textures: " + n2);
            add("bld_hk_tower", Wrap("bld_hk_tower", $"{T3}/hk_tower_abhayexe.glb", 53.7f, null, false, out var n3), "Full Gameready City Buildings III [HongKong] (abhayexe, CC BY 4.0), brand-free tower only (glb_subset.py): " + n3);
            foreach (var (key, node) in Signs)
                add(key, Wrap(key, $"{T3}/kr_road_signs_notbadworks.glb", 2.6f, node, true, out var ns), $"South Korean Road Signs (notbadworks, CC BY 4.0), {node}: " + ns);
            add("guard_soldier", Guard(out var ng), "AMERICAN SOLDIER RIGGED (matheofrelin3, CC BY 4.0), arms lowered, baked static mesh, 1.78 m: " + ng);
            File.WriteAllLines(Path.Combine(Directory.GetCurrentDirectory(), "Logs", "town_assets_build.txt"), Report);
            foreach (var r in Report) Debug.Log("[TownAssets] " + r);
        }

        // ------------------------------------------------------------------ materials
        static Texture2D Tex(string path, bool normal, bool alpha = false)
        {
            var imp = (TextureImporter)AssetImporter.GetAtPath(path) ?? throw new InvalidOperationException("texture not imported: " + path);
            imp.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            imp.sRGBTexture = !normal; imp.maxTextureSize = 1024; imp.mipmapEnabled = true; imp.wrapMode = TextureWrapMode.Repeat;
            imp.alphaSource = alpha ? TextureImporterAlphaSource.FromInput : TextureImporterAlphaSource.None;
            imp.alphaIsTransparency = alpha;
            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static void Materials(HarborTheme theme)
        {
            void Add(string key, Material m) { theme.materials.RemoveAll(e => e.key == key); theme.materials.Add(new HarborTheme.Entry { key = key, material = m }); }
            var c1a = Tex($"{TX}/concrete_wall_001_diff_1k.jpg", false); var c1n = Tex($"{TX}/concrete_wall_001_nor_gl_1k.jpg", true);
            var c4a = Tex($"{TX}/concrete_wall_004_diff_1k.jpg", false); var c4n = Tex($"{TX}/concrete_wall_004_nor_gl_1k.jpg", true);
            var ia = Tex($"{TX}/corrugated_iron_04_diff_1k.jpg", false); var inm = Tex($"{TX}/corrugated_iron_04_nor_gl_1k.jpg", true);
            var tints = new[] { new Color(0.86f, 0.86f, 0.84f), new Color(1.05f, 0.98f, 0.86f), new Color(0.84f, 0.93f, 1.05f), new Color(1.06f, 0.88f, 0.86f), new Color(0.88f, 1.0f, 0.88f) };
            for (int i = 0; i < WallTints.Length; i++)
                Add(WallTints[i], ProceduralAssets.MakeLit(WallTints[i], tints[i], 0.15f, 0f, i % 2 == 0 ? c1a : c4a, i % 2 == 0 ? c1n : c4n, 3.0f));
            var irons = new[] { new Color(0.35f, 0.55f, 0.85f), new Color(0.40f, 0.70f, 0.48f), new Color(0.85f, 0.38f, 0.32f), new Color(0.80f, 0.80f, 0.80f), new Color(1.05f, 1.05f, 1.02f) };
            for (int i = 0; i < IronTints.Length; i++)
                Add(IronTints[i], ProceduralAssets.MakeLit(IronTints[i], irons[i], 0.35f, 0.35f, ia, inm, 2.5f));
            // worn yellow centre-line paint (RoadLines026B colour map; replaces the flat steel-texture yellow on every map)
            var rlc = Tex($"{RL}/RoadLines026B_1K_Color.jpg", false);
            Add("paint_yellow", ProceduralAssets.MakeLit("paint_yellow", new Color(1f, 0.92f, 0.75f), 0.3f, 0f, rlc, null, 2.0f));
            // hatched road-end zone: colour + opacity, alpha-tested, world-space projection from above
            var rlo = Tex($"{RL}/RoadLines026B_1K_ColorOpacity.png", false, true); var rln = Tex($"{RL}/RoadLines026B_1K_NormalGL.png", true);
            var hatch = ProceduralAssets.MakeLit("paint_hatch_yellow", new Color(1f, 0.95f, 0.8f), 0.3f, 0f, rlo, rln, 4.0f);
            hatch.SetFloat("_AlphaCutoffEnable", 1f); hatch.SetFloat("_AlphaCutoff", 0.5f); hatch.EnableKeyword("_ALPHATEST_ON");
            HDMaterial.ValidateMaterial(hatch); EditorUtility.SetDirty(hatch);
            Add("paint_hatch_yellow", hatch);
            Report.Add($"materials: {WallTints.Length} wall tints, {IronTints.Length} corrugated colours, paint_yellow (RoadLines026B), paint_hatch_yellow");
        }

        // ------------------------------------------------------------------ prefabs
        /// <summary>Imports a GLB (optionally keeping only the subtree whose root is named `keepNode`), scales it uniformly to
        /// `targetHeight`, pivot bottom-centre, animations stripped, box collider (small props) or mesh colliders.</summary>
        static GameObject Wrap(string name, string assetPath, float targetHeight, string keepNode, bool boxCollider, out string note)
        {
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath) ?? throw new InvalidOperationException("not imported: " + assetPath);
            var root = new GameObject(name);
            var inst = Object.Instantiate(src);
            inst.transform.SetParent(root.transform, false);
            foreach (var a in inst.GetComponentsInChildren<Animation>(true)) Object.DestroyImmediate(a);
            foreach (var a in inst.GetComponentsInChildren<Animator>(true)) Object.DestroyImmediate(a);
            int dropped = 0;
            if (keepNode != null)
            {
                var keepT = inst.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == keepNode) ?? throw new InvalidOperationException($"{name}: node {keepNode} not found");
                foreach (var r in inst.GetComponentsInChildren<Renderer>(true).ToArray())
                    if (r != null && !r.transform.IsChildOf(keepT)) { Object.DestroyImmediate(r.gameObject); dropped++; }
            }
            var rs = inst.GetComponentsInChildren<Renderer>(true);
            if (rs.Length == 0) throw new InvalidOperationException($"{name}: no renderers");
            var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
            float k = targetHeight / Mathf.Max(b.size.y, 1e-4f);
            inst.transform.localScale = inst.transform.localScale * k;
            b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
            inst.transform.localPosition += new Vector3(-b.center.x, -b.min.y, -b.center.z);
            b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
            if (boxCollider) { var bc = root.AddComponent<BoxCollider>(); bc.center = b.center; bc.size = b.size; }
            else foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true)) { if (mf.sharedMesh == null) continue; var mc = mf.gameObject.AddComponent<MeshCollider>(); mc.sharedMesh = mf.sharedMesh; }
            int tris = root.GetComponentsInChildren<MeshFilter>(true).Where(f => f.sharedMesh != null).Sum(f => (int)(f.sharedMesh.triangles.Length / 3));
            note = $"{assetPath} sha256={ScenarioIO.Sha256Hex(File.ReadAllBytes(assetPath))} scale={k:G5} kept={rs.Length} dropped={dropped} bounds={b.size} tris={tris}; " + TextureDiet.Apply(root, name);
            var p = PrefabUtility.SaveAsPrefabAsset(root, $"{PrefabDir}/{name}.prefab");
            Object.DestroyImmediate(root);
            Report.Add($"{name}: size {b.size} tris {tris} scale {k:G5}");
            return p;
        }

        /// <summary>Harbour guard: the rigged soldier's upper arms are turned from the T-pose down along the body (8 degrees off
        /// the side), the skinned mesh is baked into a static mesh asset (one per material slot kept), scaled to 1.78 m.</summary>
        static GameObject Guard(out string note)
        {
            string assetPath = $"{T3}/american_soldier_matheofrelin3.glb";
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath) ?? throw new InvalidOperationException("not imported: " + assetPath);
            var inst = Object.Instantiate(src);
            foreach (var a in inst.GetComponentsInChildren<Animation>(true)) Object.DestroyImmediate(a);
            foreach (var a in inst.GetComponentsInChildren<Animator>(true)) Object.DestroyImmediate(a);
            var bones = inst.GetComponentsInChildren<Transform>(true);
            Transform Bone(string end) => bones.FirstOrDefault(t => t.name.Contains(end)) ?? throw new InvalidOperationException("bone " + end);
            var hips = Bone(":Hips_");
            int lowered = 0;
            foreach (var side in new[] { "Left", "Right" })
            {
                var arm = Bone($":{side}Arm_"); var fore = Bone($":{side}ForeArm_");
                var dir = (fore.position - arm.position).normalized;
                float outward = Mathf.Sign(Vector3.Dot(dir, inst.transform.right)) ;
                var target = (Vector3.down + inst.transform.right * outward * 0.14f).normalized;
                arm.rotation = Quaternion.FromToRotation(dir, target) * arm.rotation;
                // forearm slightly bent forward, hands by the thighs
                var hand = Bone($":{side}Hand_");
                var d2 = (hand.position - fore.position).normalized;
                var t2 = (target + inst.transform.forward * 0.25f).normalized;
                fore.rotation = Quaternion.FromToRotation(d2, t2) * fore.rotation;
                lowered++;
            }
            var root = new GameObject("guard_soldier");
            var holder = new GameObject("body"); holder.transform.SetParent(root.transform, false);
            int parts = 0;
            foreach (var smr in inst.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var baked = new Mesh { name = "guard_soldier_" + smr.name };
                smr.BakeMesh(baked, true);
                // BakeMesh returns vertices in the renderer's local space: bring them to the instance root space
                var m = inst.transform.worldToLocalMatrix * smr.transform.localToWorldMatrix;
                var v = baked.vertices; for (int i = 0; i < v.Length; i++) v[i] = m.MultiplyPoint3x4(v[i]);
                var nrm = baked.normals; for (int i = 0; i < nrm.Length; i++) nrm[i] = m.MultiplyVector(nrm[i]).normalized;
                baked.vertices = v; baked.normals = nrm; baked.RecalculateBounds();
                var mp = $"{MeshDir}/guard_soldier_{parts}.asset";
                AssetDatabase.DeleteAsset(mp); AssetDatabase.CreateAsset(baked, mp);
                var go = new GameObject(smr.name); go.transform.SetParent(holder.transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = baked;
                go.AddComponent<MeshRenderer>().sharedMaterials = smr.sharedMaterials;
                parts++;
            }
            Object.DestroyImmediate(inst);
            var rs = root.GetComponentsInChildren<Renderer>(true);
            var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
            float k = 1.78f / Mathf.Max(b.size.y, 1e-4f);
            holder.transform.localScale = Vector3.one * k;
            b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
            holder.transform.localPosition = new Vector3(-b.center.x, -b.min.y, -b.center.z);
            b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
            var cc = root.AddComponent<CapsuleCollider>(); cc.center = new Vector3(0f, b.size.y / 2f, 0f); cc.height = b.size.y; cc.radius = 0.25f;
            note = $"{assetPath} sha256={ScenarioIO.Sha256Hex(File.ReadAllBytes(assetPath))} arms lowered={lowered} parts={parts} scale={k:G5} bounds={b.size}; " + TextureDiet.Apply(root, "guard_soldier");
            var p = PrefabUtility.SaveAsPrefabAsset(root, $"{PrefabDir}/guard_soldier.prefab");
            Object.DestroyImmediate(root);
            Report.Add($"guard_soldier: size {b.size} parts {parts} arms lowered {lowered}");
            return p;
        }

        /// <summary>Rebuilds only these prefabs / materials (dev loop): -executeMethod PortCCTV.EditorTools.TownAssets.RebuildBatch</summary>
        public static void RebuildBatch()
        {
            int code = 0;
            try
            {
                var theme = HarborTheme.Load();
                Build(theme, (k, p, s) => { theme.prefabs.RemoveAll(e => e.key == k); theme.prefabs.Add(new HarborTheme.PrefabEntry { key = k, prefab = p, source = s }); });
                EditorUtility.SetDirty(theme); AssetDatabase.SaveAssets();
                Preview(new[] { "shopfront_kr", "house_jp03", "bld_hk_tower", "kr_sign_stop", "kr_sign_slow", "kr_sign_no_parking", "kr_sign_sl30", "guard_soldier" });
            }
            catch (Exception e) { Debug.LogException(e); code = 1; }
            EditorApplication.Exit(code);
        }

        public static void Preview(IEnumerable<string> keys)
        {
            var outDir = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "town_asset_previews");
            Directory.CreateDirectory(outDir);
            foreach (var key in keys)
            {
                var p = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabDir}/{key}.prefab") ?? AssetDatabase.LoadAssetAtPath<GameObject>($"{Root}/Vessels/{key}.prefab");
                if (p == null) continue;
                var ed = UnityEditor.Editor.CreateEditor(p);
                var tex = ed.RenderStaticPreview(AssetDatabase.GetAssetPath(p), null, 512, 512);
                if (tex != null) File.WriteAllBytes(Path.Combine(outDir, key + ".png"), tex.EncodeToPNG());
                Object.DestroyImmediate(ed);
            }
        }
    }
}
