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
    /// Replacement of crude scenery assets with free imported models (user 2026-11-09, docs/asset_quality_review_20261109):
    ///   * breakwater_light_white / breakwater_light_red: the Manabashima lighthouse photogrammetry scan (tokiwatari, CC BY 4.0)
    ///     cut down to the tower (Tools/process_manabashima_lighthouse.py), 3 LODs, de-lit + white-balanced texture, a red variant
    ///     re-coloured from the white paint (luminance kept), on a concrete plinth with steps up to the door (door faces +z);
    ///   * equip_excavator: Excavator (CAT) by sarvesh777 (CC BY 4.0), logos inpainted, track links decimated, textures 1k
    ///     (Tools/glb_derive.py); boom faces +z, track bottoms at y = 0, no colliders (deck equipment of the barge);
    ///   * forklift_truck: Forklift Truck by louis-muir (CC BY 4.0); forks face +z, wheels on y = 0, one BoxCollider;
    ///   * container_detail_&lt;colour&gt;: Classic Shipping Container by Sebastian Webster (CC BY 4.0), text inpainted, resized to an
    ///     ISO 20 ft box (6.058 x 2.591 x 2.438 m), one material per yard colour (texture x tint, so seams / rust / doors stay);
    ///   * fishbox_stack_&lt;colour&gt;: pallet with a 2 x 2 x 4 stack of the Poly Haven plastic_crate_02 (CC0) in fish-box colours,
    ///     LOD1 = the old box stack.
    /// Provenance / hashes: ThirdParty/SketchfabCCBY/equipment/SOURCE_equipment.md, lighthouse2/SOURCE_lighthouse2.md.
    /// </summary>
    public static class ReplacementAssets
    {
        const string Root = "Assets/PortCCTVSimulator";
        const string PrefabDir = Root + "/HarborPrefabs";
        const string MatDir = Root + "/Materials/Replacement";
        const string EQ = Root + "/ThirdParty/SketchfabCCBY/equipment";
        const string LH = Root + "/ThirdParty/SketchfabCCBY/lighthouse2";
        public const string ExcavatorPrefabPath = PrefabDir + "/equip_excavator.prefab";

        static readonly List<string> Report = new List<string>();

        public static void Build(HarborTheme theme, Action<string, GameObject, string> add)
        {
            Directory.CreateDirectory(MatDir);
            Report.Clear();
            foreach (var colour in new[] { "white", "red", "green" })
                add("breakwater_light_" + colour, Lighthouse(colour, theme), $"Manabashima Lighthouse (tokiwatari, CC BY 4.0), tower cut from the scan, {colour} paint, 3 LODs + plinth (ReplacementAssets)");
            add("equip_excavator", Excavator(), "Excavator (CAT) (sarvesh777, CC BY 4.0), logos inpainted, tracks decimated, 1k textures (ReplacementAssets)");
            add("forklift_truck", Forklift(), "Forklift Truck (louis-muir, CC BY 4.0), 1k textures (ReplacementAssets)");
            foreach (var c in HarborGeneric02.ContainerColours)
                add(c.Replace("container_", "container_detail_"), ContainerDetail(c, theme), $"Classic Shipping Container (Sebastian Webster, CC BY 4.0), 20 ft, {c} tint (ReplacementAssets)");
            foreach (var c in new[] { "blue", "red", "green", "white" })
                add("fishbox_stack_" + c, FishBoxStack(c, theme), $"pallet of Poly Haven plastic_crate_02 (CC0) fish boxes, {c}; LOD1 box stack (ReplacementAssets)");
            FixPrefabMaterials(theme);
            File.WriteAllLines(Path.Combine(Directory.GetCurrentDirectory(), "Logs", "replacement_assets_build.txt"), Report);
            foreach (var r in Report) Debug.Log("[ReplacementAssets] " + r);
        }

        // ------------------------------------------------------------------ helpers
        static Material Lit(string name, Texture2D albedo, Texture2D normal, Color tint, float smooth, float metal, float normalScale = 1f)
        {
            var path = $"{MatDir}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(Shader.Find("HDRP/Lit")); AssetDatabase.CreateAsset(m, path); }
            m.shader = Shader.Find("HDRP/Lit"); m.SetFloat("_SurfaceType", 0f); m.renderQueue = -1;
            m.SetColor("_BaseColor", tint);
            m.SetTexture("_BaseColorMap", albedo);
            if (normal != null) { m.SetTexture("_NormalMap", normal); m.SetFloat("_NormalScale", normalScale); m.EnableKeyword("_NORMALMAP"); }
            m.SetFloat("_Smoothness", smooth); m.SetFloat("_Metallic", metal);
            HDMaterial.ValidateMaterial(m);
            EditorUtility.SetDirty(m);
            return m;
        }

        static Texture2D Tex(string path, bool normal, int max = 1024)
        {
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            imp.sRGBTexture = !normal; imp.maxTextureSize = max; imp.mipmapEnabled = true; imp.wrapMode = TextureWrapMode.Clamp;
            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static Mesh ObjMesh(string path)
        {
            var imp = (ModelImporter)AssetImporter.GetAtPath(path);
            imp.materialImportMode = ModelImporterMaterialImportMode.None;
            imp.importNormals = ModelImporterNormals.Import; imp.importTangents = ModelImporterTangents.CalculateMikk;
            imp.globalScale = 1f; imp.useFileScale = false; imp.isReadable = true; imp.addCollider = false;
            imp.SaveAndReimport();
            return AssetDatabase.LoadAllAssetsAtPath(path).OfType<Mesh>().First();
        }

        static Mesh SaveMesh(Mesh m, string name)
        {
            var path = $"{Root}/Meshes/{name}.asset";
            var old = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (old != null) { EditorUtility.CopySerialized(m, old); return old; }
            AssetDatabase.CreateAsset(m, path); return m;
        }

        static GameObject Save(GameObject root, string name)
        {
            var p = PrefabUtility.SaveAsPrefabAsset(root, $"{PrefabDir}/{name}.prefab");
            Object.DestroyImmediate(root);
            return p;
        }

        static Bounds RendererBounds(GameObject go, Func<Renderer, bool> filter = null)
        {
            var rs = go.GetComponentsInChildren<Renderer>(true).Where(r => filter == null || filter(r)).ToArray();
            var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds); return b;
        }

        /// <summary>Imported GLB instance under a fresh root, animation stripped, colliders removed.</summary>
        static (GameObject root, GameObject inst) Instance(string name, string glb)
        {
            AssetDatabase.ImportAsset(glb, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(glb) ?? throw new InvalidOperationException("not imported: " + glb);
            var root = new GameObject(name);
            var inst = Object.Instantiate(src); inst.name = "model";
            inst.transform.SetParent(root.transform, false);
            foreach (var a in inst.GetComponentsInChildren<Animation>(true)) Object.DestroyImmediate(a);
            foreach (var a in inst.GetComponentsInChildren<Animator>(true)) Object.DestroyImmediate(a);
            foreach (var c in inst.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
            return (root, inst);
        }

        static int Tris(GameObject go) => go.GetComponentsInChildren<MeshFilter>(true).Where(f => f.sharedMesh != null).Sum(f => (int)(f.sharedMesh.triangles.Length / 3));

        // ------------------------------------------------------------------ breakwater lighthouse
        static GameObject Lighthouse(string colour, HarborTheme theme)
        {
            string name = "breakwater_light_" + colour;
            var alb = Tex($"{LH}/manabashima_tower_albedo_{colour}.png", false);
            var nrm = Tex($"{LH}/manabashima_tower_normal.png", true);
            var mat = Lit("M_breakwater_light_" + colour, alb, nrm, Color.white, 0.32f, 0f, 0.6f);
            var lods = new[] { ObjMesh($"{LH}/manabashima_tower_lod0.obj"), ObjMesh($"{LH}/manabashima_tower_lod1.obj"), ObjMesh($"{LH}/manabashima_tower_lod2.obj") };
            var root = new GameObject(name);
            // concrete plinth: octagonal drum 5.6 m across, 0.6 m high (Korean breakwater lights stand on a cast base), three steps
            // up to the door on +z
            var conc = theme.Get("concrete");
            var plinth = GameObject.CreatePrimitive(PrimitiveType.Cylinder); plinth.name = "plinth";
            plinth.transform.SetParent(root.transform, false);
            plinth.transform.localPosition = new Vector3(0f, 0.3f, 0f); plinth.transform.localScale = new Vector3(5.6f, 0.3f, 5.6f);
            plinth.GetComponent<MeshRenderer>().sharedMaterial = conc;
            Object.DestroyImmediate(plinth.GetComponent<Collider>());
            var pc = root.AddComponent<BoxCollider>(); pc.center = new Vector3(0f, 0.3f, 0f); pc.size = new Vector3(5.2f, 0.6f, 5.2f);
            for (int s = 0; s < 3; s++)
            {
                var st = GameObject.CreatePrimitive(PrimitiveType.Cube); st.name = "door_step_" + s;
                st.transform.SetParent(root.transform, false);
                float d = 0.35f * (3 - s);
                st.transform.localPosition = new Vector3(0f, 0.1f + 0.2f * s - 0.1f * 0 , 2.65f + d / 2f);
                st.transform.localScale = new Vector3(1.6f, 0.2f * (s + 1), d);
                st.transform.localPosition = new Vector3(0f, 0.1f * (s + 1), 2.65f + d / 2f);
                st.GetComponent<MeshRenderer>().sharedMaterial = conc;
            }
            // tower: scan meshes in a LOD group, rotated so the scanned door (glTF azimuth 127.5 deg, OBJ import mirrors x) faces +z
            var tower = new GameObject("tower");
            tower.transform.SetParent(root.transform, false);
            tower.transform.localPosition = new Vector3(0f, 0.55f, 0f);
            tower.transform.localRotation = Quaternion.Euler(0f, 127.5f, 0f);
            var lg = tower.AddComponent<LODGroup>();
            var lodArr = new LOD[3]; float[] screen = { 0.20f, 0.045f, 0.004f };
            for (int i = 0; i < 3; i++)
            {
                var go = new GameObject("lod" + i); go.transform.SetParent(tower.transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = lods[i];
                var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = mat;
                lodArr[i] = new LOD(screen[i], new Renderer[] { r });
            }
            lg.SetLODs(lodArr); lg.RecalculateBounds();
            var col = tower.transform.Find("lod2").gameObject.AddComponent<MeshCollider>(); col.sharedMesh = lods[2];
            var b = RendererBounds(root);
            Report.Add($"{name}: bounds {b.size} (min y {b.min.y:F2}), LOD tris {lods[0].triangles.Length / 3}/{lods[1].triangles.Length / 3}/{lods[2].triangles.Length / 3}, albedo {AssetDatabase.GetAssetPath(alb)} {alb.width}px");
            return Save(root, name);
        }

        // ------------------------------------------------------------------ excavator
        static GameObject Excavator()
        {
            var (root, inst) = Instance("equip_excavator", $"{EQ}/excavator_cat_sarvesh777_1k.glb");
            var all = RendererBounds(inst);
            var tracks = RendererBounds(inst, r => r.name.Contains("tire"));
            // long horizontal axis of the tracks = travel direction; scale the tracks' outer width to 3.19 m (20 t class)
            bool alongX = tracks.size.x > tracks.size.z;
            float width = alongX ? tracks.size.z : tracks.size.x;
            float k = 3.19f / Mathf.Max(width, 1e-5f);
            inst.transform.localScale *= k;
            all = RendererBounds(inst); tracks = RendererBounds(inst, r => r.name.Contains("tire"));
            // the boom is where the whole machine reaches beyond the tracks: turn it to +z
            Vector3 d = all.center - tracks.center; float yaw;
            if (alongX) yaw = d.x > 0 ? -90f : 90f; else yaw = d.z > 0 ? 0f : 180f;
            root.transform.rotation = Quaternion.identity;
            var pivot = new GameObject("pivot"); pivot.transform.SetParent(root.transform, false);
            inst.transform.SetParent(pivot.transform, true);
            pivot.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            tracks = RendererBounds(inst, r => r.name.Contains("tire"));
            pivot.transform.localPosition -= new Vector3(tracks.center.x, tracks.min.y, tracks.center.z);
            all = RendererBounds(root); tracks = RendererBounds(root, r => r.name.Contains("tire"));
            Report.Add($"equip_excavator: scale {k:G5}, boom yaw {yaw}, overall {all.size} (z {all.min.z:F2}..{all.max.z:F2}, top {all.max.y:F2}), tracks {tracks.size}, tris {Tris(root)}");
            return Save(root, "equip_excavator");
        }

        // ------------------------------------------------------------------ forklift
        static GameObject Forklift()
        {
            var (root, inst) = Instance("forklift_truck", $"{EQ}/forklift_truck_louismuir_1k.glb");
            var all = RendererBounds(inst);
            float w = Mathf.Min(all.size.x, all.size.z);
            float k = (w > 0.9f && w < 1.7f) ? 1f : 1.2f / w;          // keep the authored metres unless the file is in other units
            inst.transform.localScale *= k;
            all = RendererBounds(inst);
            // forks: the lowest 12 % of the vertices sit on the fork side of the centre
            var low = new List<Vector3>();
            foreach (var mf in inst.GetComponentsInChildren<MeshFilter>(true))
                foreach (var v in mf.sharedMesh.vertices) { var p = mf.transform.TransformPoint(v); if (p.y < all.min.y + 0.12f * all.size.y) low.Add(p); }
            var lc = low.Count > 0 ? low.Aggregate(Vector3.zero, (a, p) => a + p) / low.Count : all.center;
            bool alongX = all.size.x > all.size.z; var dd = lc - all.center; float yaw;
            if (alongX) yaw = dd.x > 0 ? -90f : 90f; else yaw = dd.z > 0 ? 0f : 180f;
            var pivot = new GameObject("pivot"); pivot.transform.SetParent(root.transform, false);
            inst.transform.SetParent(pivot.transform, true);
            pivot.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            all = RendererBounds(inst);
            pivot.transform.localPosition -= new Vector3(all.center.x, all.min.y, all.center.z);
            all = RendererBounds(root);
            var bc = root.AddComponent<BoxCollider>(); bc.center = all.center; bc.size = all.size;
            Report.Add($"forklift_truck: scale {k:G4}, forks yaw {yaw}, size {all.size}, tris {Tris(root)}");
            return Save(root, "forklift_truck");
        }

        // ------------------------------------------------------------------ container (20 ft)
        static GameObject ContainerDetail(string colourKey, HarborTheme theme)
        {
            string name = colourKey.Replace("container_", "container_detail_");
            var (root, inst) = Instance(name, $"{EQ}/shipping_container_sebastianwebster_1k.glb");
            var b = RendererBounds(inst);
            // longest horizontal side -> 6.058 m along x; height 2.591 m; width 2.438 m (ISO 668 20 ft)
            if (b.size.z > b.size.x) { inst.transform.localRotation = Quaternion.Euler(0f, 90f, 0f) * inst.transform.localRotation; b = RendererBounds(inst); }
            var s = inst.transform.localScale;
            inst.transform.localScale = new Vector3(s.x * 6.058f / b.size.x, s.y * 2.591f / b.size.y, s.z * 2.438f / b.size.z);
            // (the GLB root may be rotated: rescale in world terms and verify)
            b = RendererBounds(inst);
            if (Mathf.Abs(b.size.x - 6.058f) > 0.05f || Mathf.Abs(b.size.y - 2.591f) > 0.05f || Mathf.Abs(b.size.z - 2.438f) > 0.05f)
            {
                // root rotation mixes axes: scale a wrapper instead
                inst.transform.localScale = s; b = RendererBounds(inst);
                var wrap = new GameObject("scale"); wrap.transform.SetParent(root.transform, false); inst.transform.SetParent(wrap.transform, true);
                wrap.transform.localScale = new Vector3(6.058f / b.size.x, 2.591f / b.size.y, 2.438f / b.size.z);
                b = RendererBounds(root);
            }
            var top = inst.transform.parent; b = RendererBounds(root);
            var off = new Vector3(-b.center.x, -b.min.y, -b.center.z);
            foreach (Transform c in root.transform) c.localPosition += off;
            // paint: the model's own (grey) texture times the yard colour, so panel seams, rust and door hardware stay
            var tint = theme.Get(colourKey)?.GetColor("_BaseColor") ?? Color.grey;
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    var src = mats[i];
                    var path = $"{MatDir}/M_{name}_{i}.mat";
                    var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                    if (m == null) { m = new Material(src); AssetDatabase.CreateAsset(m, path); } else m.CopyPropertiesFromMaterial(src);
                    // the texture is a dark grey (~0.24): x3.2 brings the yard colour back to its flat-material value
                    var c = new Color(Mathf.Min(1f, tint.r * 3.2f), Mathf.Min(1f, tint.g * 3.2f), Mathf.Min(1f, tint.b * 3.2f), 1f);
                    foreach (var prop in new[] { "baseColorFactor", "_BaseColor" }) if (m.HasProperty(prop)) m.SetColor(prop, c);
                    EditorUtility.SetDirty(m);
                    mats[i] = m;
                }
                r.sharedMaterials = mats;
            }
            b = RendererBounds(root);
            var bc = root.AddComponent<BoxCollider>(); bc.center = b.center; bc.size = b.size;
            // LOD1: a plain box in the yard colour (as the merged far boxes), so only near containers carry 17k triangles
            var lod0rs = root.GetComponentsInChildren<Renderer>(true);
            var box = GameObject.CreatePrimitive(PrimitiveType.Cube); box.name = "lod1_box"; box.transform.SetParent(root.transform, false);
            box.transform.localPosition = b.center; box.transform.localScale = b.size; Object.DestroyImmediate(box.GetComponent<Collider>());
            box.GetComponent<MeshRenderer>().sharedMaterial = theme.Get(colourKey);
            var lg = root.AddComponent<LODGroup>();
            lg.SetLODs(new[] { new LOD(0.035f, lod0rs), new LOD(0.0008f, new Renderer[] { box.GetComponent<Renderer>() }) }); lg.RecalculateBounds();
            if (colourKey == "container_red") Report.Add($"container_detail: size {b.size}, tris {Tris(root)}");
            return Save(root, name);
        }

        // ------------------------------------------------------------------ fish boxes
        static GameObject FishBoxStack(string colour, HarborTheme theme)
        {
            string name = "fishbox_stack_" + colour;
            var cratePf = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabDir + "/crate_02.prefab") ?? throw new InvalidOperationException("crate_02 prefab missing");
            var root = new GameObject(name);
            var lod0 = new GameObject("lod0"); lod0.transform.SetParent(root.transform, false);
            // pallet
            var wood = theme.Get("bench_wood") ?? theme.Get("timber") ?? theme.Get("concrete");
            var pal = GameObject.CreatePrimitive(PrimitiveType.Cube); pal.name = "pallet"; pal.transform.SetParent(lod0.transform, false);
            pal.transform.localPosition = new Vector3(0f, 0.07f, 0f); pal.transform.localScale = new Vector3(1.2f, 0.14f, 1.0f);
            pal.GetComponent<MeshRenderer>().sharedMaterial = wood; Object.DestroyImmediate(pal.GetComponent<Collider>());
            // one crate, measured, scaled to a 0.58 m fish box; its material tinted with the box colour
            var probe = Object.Instantiate(cratePf); var cb = RendererBounds(probe);
            float k = 0.58f / Mathf.Max(cb.size.x, cb.size.z); float h = cb.size.y * k;
            Object.DestroyImmediate(probe);
            Color col = colour switch { "blue" => new Color(0.10f, 0.32f, 0.70f), "red" => new Color(0.75f, 0.12f, 0.10f), "green" => new Color(0.15f, 0.50f, 0.25f), _ => new Color(0.86f, 0.86f, 0.84f) };
            Material tinted = null;
            var mats = new List<Material>();
            for (int lvl = 0; lvl < 4; lvl++)
                foreach (var sx in new[] { -0.3f, 0.3f })
                    foreach (var sz in new[] { -0.25f, 0.25f })
                    {
                        var c = Object.Instantiate(cratePf); c.name = "crate"; c.transform.SetParent(lod0.transform, false);
                        foreach (var cc in c.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(cc);
                        c.transform.localScale = Vector3.one * k;
                        var bb = RendererBounds(c);
                        c.transform.localPosition = new Vector3(sx, 0.14f + lvl * (h + 0.005f), sz) - new Vector3(bb.center.x, bb.min.y, bb.center.z) + c.transform.localPosition;
                        foreach (var r in c.GetComponentsInChildren<Renderer>(true))
                        {
                            if (tinted == null)
                            {
                                // moulded plastic: the crate's (grey) diffuse and normal map x the box colour (the glTF factor was
                                // orange), opaque (the source material was alpha BLEND without any alpha texture, so HDRP drew it in the
                                // transparent queue and walls vanished behind each other)
                                var path = $"{MatDir}/M_fishbox_{colour}.mat";
                                AssetDatabase.DeleteAsset(path);                   // an older copy carried the glTF transparent surface
                                tinted = new Material(Shader.Find("HDRP/Lit")); AssetDatabase.CreateAsset(tinted, path);
                                tinted.SetFloat("_SurfaceType", 0f);
                                tinted.SetTexture("_BaseColorMap", AssetDatabase.LoadAssetAtPath<Texture2D>(CrateTex + "_diff_1k.jpg"));
                                var nm = Tex(CrateTex + "_nor_gl_1k.jpg", true); tinted.SetTexture("_NormalMap", nm); tinted.EnableKeyword("_NORMALMAP");
                                tinted.SetColor("_BaseColor", col * 1.15f); tinted.SetFloat("_Smoothness", 0.45f); tinted.SetFloat("_Metallic", 0f);
                                tinted.SetFloat("_DoubleSidedEnable", 1f);
                                HDMaterial.ValidateMaterial(tinted);
                                EditorUtility.SetDirty(tinted);
                            }
                            r.sharedMaterial = tinted;
                        }
                    }
            // merge LOD0 into one mesh per material (32 crates would be 32 draw calls)
            var groups = new Dictionary<Material, List<CombineInstance>>();
            foreach (var mf in lod0.GetComponentsInChildren<MeshFilter>(true))
            {
                var mr = mf.GetComponent<MeshRenderer>(); if (mf.sharedMesh == null || mr == null) continue;
                if (!groups.TryGetValue(mr.sharedMaterial, out var l)) groups[mr.sharedMaterial] = l = new List<CombineInstance>();
                l.Add(new CombineInstance { mesh = mf.sharedMesh, transform = root.transform.worldToLocalMatrix * mf.transform.localToWorldMatrix });
            }
            Object.DestroyImmediate(lod0);
            lod0 = new GameObject("lod0"); lod0.transform.SetParent(root.transform, false);
            var lod0rs = new List<Renderer>(); int gi = 0;
            foreach (var kv in groups)
            {
                var m = new Mesh { name = $"{name}_lod0_{gi}", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                m.CombineMeshes(kv.Value.ToArray(), true, true); m.RecalculateBounds();
                m = SaveMesh(m, m.name);
                var go = new GameObject("m" + gi++); go.transform.SetParent(lod0.transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = m; var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = kv.Key; lod0rs.Add(r);
            }
            // LOD1: plain boxes in the box colour (far views)
            var lod1 = new GameObject("lod1"); lod1.transform.SetParent(root.transform, false);
            var flat = AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/M_fishbox_{colour}_flat.mat");
            if (flat == null) { flat = new Material(Shader.Find("HDRP/Lit")); AssetDatabase.CreateAsset(flat, $"{MatDir}/M_fishbox_{colour}_flat.mat"); }
            flat.SetColor("_BaseColor", col); flat.SetFloat("_Smoothness", 0.35f); HDMaterial.ValidateMaterial(flat); EditorUtility.SetDirty(flat);
            var lod1rs = new List<Renderer>();
            var p1 = GameObject.CreatePrimitive(PrimitiveType.Cube); p1.transform.SetParent(lod1.transform, false);
            p1.transform.localPosition = new Vector3(0, 0.07f, 0); p1.transform.localScale = new Vector3(1.2f, 0.14f, 1f); p1.GetComponent<MeshRenderer>().sharedMaterial = wood; Object.DestroyImmediate(p1.GetComponent<Collider>()); lod1rs.Add(p1.GetComponent<Renderer>());
            var p2 = GameObject.CreatePrimitive(PrimitiveType.Cube); p2.transform.SetParent(lod1.transform, false);
            p2.transform.localPosition = new Vector3(0, 0.14f + 2f * h, 0); p2.transform.localScale = new Vector3(1.16f, 4f * h, 0.96f); p2.GetComponent<MeshRenderer>().sharedMaterial = flat; Object.DestroyImmediate(p2.GetComponent<Collider>()); lod1rs.Add(p2.GetComponent<Renderer>());
            var lg = root.AddComponent<LODGroup>();
            lg.SetLODs(new[] { new LOD(0.035f, lod0rs.ToArray()), new LOD(0.002f, lod1rs.ToArray()) }); lg.RecalculateBounds();
            if (colour == "blue") Report.Add($"fishbox_stack: crate scale {k:G4}, box height {h:F3}, stack height {0.14f + 4f * h:F2} m, LOD0 tris {Tris(lod0)}");
            return Save(root, name);
        }

        const string PHM = Root + "/ThirdParty/PolyHavenCC0/models";
        const string CrateTex = PHM + "/plastic_crate_02/textures/plastic_crate_02";

        /// <summary>
        /// Material repairs found by DiagnoseBatch (2026-11-09):
        ///  * tree_01 / tree_02 (Poly Haven island trees): the 1k glTF ships the leaves diffuse as JPEG with alphaMode BLEND and no
        ///    alpha texture, so every leaf card drew as an opaque / semi-transparent grey-white triangle. The separate Poly Haven
        ///    leaves_alpha map (CC0, md5-checked, ThirdParty_raw/polyhaven/leaves_alpha_20261109) is merged into an RGBA diffuse and
        ///    the leaves get an HDRP/Lit alpha-clip (0.5), double-sided material;
        ///  * crate_02: alpha BLEND without any alpha texture -> opaque HDRP/Lit with its diffuse, normal map and orange factor;
        ///  * any harbor prefab renderer with an empty material slot (bld_gas_station: window frames, ceiling, metal plates drew
        ///    magenta) gets a theme material chosen by the slot's node name (metal -> galvanised steel, glass -> dark glass,
        ///    otherwise white wall).
        /// </summary>
        static void FixPrefabMaterials(HarborTheme theme)
        {
            foreach (var (key, asset) in new[] { ("tree_01", "island_tree_01"), ("tree_02", "island_tree_02") })
            {
                var alb = Tex($"{PHM}/{asset}_dec/textures/{asset}_leaves_diffalpha_1k.png", false);
                var nrm = Tex($"{PHM}/{asset}_dec/textures/{asset}_leaves_nor_gl_1k.jpg", true);
                var m = Lit("M_" + asset + "_leaves_cutout", alb, nrm, Color.white, 0.25f, 0f, 0.8f);
                m.SetFloat("_AlphaCutoffEnable", 1f); m.SetFloat("_AlphaCutoff", 0.5f);
                m.SetFloat("_DoubleSidedEnable", 1f); m.SetFloat("_DoubleSidedNormalMode", 1f);   // mirror: both faces lit as leaves
                HDMaterial.ValidateMaterial(m); EditorUtility.SetDirty(m);
                int n = ReplaceInPrefab(key, mat => mat != null && mat.name.Contains("leaves"), m);
                Report.Add($"{key}: leaves -> alpha-clip material ({n} slots), alpha texture {AssetDatabase.GetAssetPath(alb)}");
            }
            {
                var m = Lit("M_crate_02_opaque", AssetDatabase.LoadAssetAtPath<Texture2D>(CrateTex + "_diff_1k.jpg"), Tex(CrateTex + "_nor_gl_1k.jpg", true), new Color(1f, 0.408f, 0f), 0.4f, 0f);
                m.SetFloat("_DoubleSidedEnable", 1f); HDMaterial.ValidateMaterial(m); EditorUtility.SetDirty(m);
                int n = ReplaceInPrefab("crate_02", mat => mat != null && mat.name.Contains("crate"), m);
                Report.Add($"crate_02: BLEND material -> opaque ({n} slots)");
            }
            {
                // bld_ind_warehouse: its only material ("lambert1", glTF specular-glossiness) imports as pure magenta (1, 0, 1)
                var wall = theme.Get("warehouse_wall");
                int n = ReplaceInPrefab("bld_ind_warehouse", mat => mat != null && (mat.name.StartsWith("lambert1")
                    || (mat.HasProperty("baseColorFactor") && mat.GetColor("baseColorFactor") == new Color(1f, 0f, 1f, 1f))), wall);
                Report.Add($"bld_ind_warehouse: magenta lambert1 -> warehouse_wall ({n} slots)");
            }
            int fixedSlots = 0; var keys = new List<string>();
            foreach (var e in theme.prefabs)
            {
                if (e.prefab == null) continue;
                bool any = e.prefab.GetComponentsInChildren<Renderer>(true).Any(r => r.sharedMaterials.Any(mm => mm == null));
                if (!any) continue;
                var path = AssetDatabase.GetAssetPath(e.prefab);
                var root = PrefabUtility.LoadPrefabContents(path);
                foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                {
                    var mats = r.sharedMaterials; bool ch = false;
                    for (int i = 0; i < mats.Length; i++)
                    {
                        if (mats[i] != null) continue;
                        var nm = r.name.ToLowerInvariant();
                        mats[i] = nm.Contains("glass") ? theme.Get("glass_dark") : nm.Contains("metal") || nm.Contains("frame") ? theme.Get("steel_galv") : theme.Get("wall_white");
                        ch = true; fixedSlots++;
                    }
                    if (ch) r.sharedMaterials = mats;
                }
                PrefabUtility.SaveAsPrefabAsset(root, path); PrefabUtility.UnloadPrefabContents(root);
                keys.Add(e.key);
            }
            Report.Add($"empty material slots filled: {fixedSlots} in {string.Join(", ", keys)}");
        }

        static int ReplaceInPrefab(string key, Func<Material, bool> match, Material m)
        {
            var path = $"{PrefabDir}/{key}.prefab";
            var root = PrefabUtility.LoadPrefabContents(path); int n = 0;
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++) if (match(mats[i])) { mats[i] = m; n++; }
                r.sharedMaterials = mats;
            }
            PrefabUtility.SaveAsPrefabAsset(root, path); PrefabUtility.UnloadPrefabContents(root);
            return n;
        }

        // ------------------------------------------------------------------ previews (dev check)
        public static void PreviewBatch()
        {
            int code = 0;
            try
            {
                var outDir = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "replacement_previews");
                Directory.CreateDirectory(outDir);
                foreach (var key in new[] { "breakwater_light_white", "breakwater_light_red", "equip_excavator", "forklift_truck", "tree_01", "container_detail_blue", "fishbox_stack_blue", "crate_02", "tree_02", "bld_gas_station", "prop_rtg", "forklift_a" })
                {
                    var p = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabDir}/{key}.prefab"); if (p == null) continue;
                    var ed = UnityEditor.Editor.CreateEditor(p);
                    var tex = ed.RenderStaticPreview(AssetDatabase.GetAssetPath(p), null, 512, 512);
                    if (tex != null) File.WriteAllBytes(Path.Combine(outDir, key + ".png"), tex.EncodeToPNG());
                    Object.DestroyImmediate(ed);
                }
            }
            catch (Exception e) { Debug.LogException(e); code = 1; }
            EditorApplication.Exit(code);
        }

        /// <summary>Material / mesh diagnosis of the prefabs flagged in the asset review (magenta, black faces, leaf cards).</summary>
        public static void DiagnoseBatch()
        {
            var lines = new List<string>();
            foreach (var key in new[] { "bld_gas_station", "prop_rtg", "tree_01", "tree_02", "bld_ind_warehouse", "bld_warehouse_blue", "bld_brick_factory", "empty_handler", "net_pile", "armg_crane" })
            {
                var p = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabDir}/{key}.prefab");
                if (p == null) { lines.Add($"{key}: prefab missing"); continue; }
                foreach (var r in p.GetComponentsInChildren<Renderer>(true))
                {
                    var mf = r.GetComponent<MeshFilter>(); var mesh = mf ? mf.sharedMesh : null;
                    int inverted = 0, tris = 0;
                    if (mesh != null && mesh.isReadable)
                    {
                        var v = mesh.vertices; var n = mesh.normals; var t = mesh.triangles; tris = t.Length / 3;
                        if (n != null && n.Length == v.Length)
                            for (int i = 0; i < t.Length; i += 3)
                            {
                                var fn = Vector3.Cross(v[t[i + 1]] - v[t[i]], v[t[i + 2]] - v[t[i]]);
                                if (Vector3.Dot(fn, n[t[i]] + n[t[i + 1]] + n[t[i + 2]]) < 0) inverted++;
                            }
                    }
                    var scale = r.transform.lossyScale;
                    foreach (var m in r.sharedMaterials)
                    {
                        if (m == null) { lines.Add($"{key}/{r.name}: NULL material"); continue; }
                        string tex = m.HasProperty("_BaseColorMap") && m.GetTexture("_BaseColorMap") != null ? "map" : m.HasProperty("baseColorTexture") && m.GetTexture("baseColorTexture") != null ? "gltfmap" : "-";
                        Color bc = m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : m.HasProperty("baseColorFactor") ? m.GetColor("baseColorFactor") : Color.magenta;
                        lines.Add($"{key}/{r.name}: shader '{m.shader.name}' supported {m.shader.isSupported} mat '{m.name}' base {bc} tex {tex} alpha {(m.HasProperty("_AlphaCutoffEnable") ? m.GetFloat("_AlphaCutoffEnable") : -1)} cull {(m.HasProperty("_CullMode") ? m.GetFloat("_CullMode") : -1)} ds {(m.HasProperty("_DoubleSidedEnable") ? m.GetFloat("_DoubleSidedEnable") : -1)} | mesh {(mesh ? mesh.name : "-")} tris {tris} inverted-normal tris {inverted} readable {(mesh ? mesh.isReadable : false)} scale {scale}");
                    }
                }
            }
            File.WriteAllLines(Path.Combine(Directory.GetCurrentDirectory(), "Logs", "asset_diagnosis.txt"), lines);
            EditorApplication.Exit(0);
        }

        /// <summary>Rebuilds only these prefabs (dev loop): -executeMethod PortCCTV.EditorTools.ReplacementAssets.RebuildBatch</summary>
        public static void RebuildBatch()
        {
            int code = 0;
            try
            {
                var theme = HarborTheme.Load();
                Build(theme, (k, p, s) => { theme.prefabs.RemoveAll(e => e.key == k); theme.prefabs.Add(new HarborTheme.PrefabEntry { key = k, prefab = p, source = s }); });
                EditorUtility.SetDirty(theme); AssetDatabase.SaveAssets();
            }
            catch (Exception e) { Debug.LogException(e); code = 1; }
            EditorApplication.Exit(code);
        }
    }
}
