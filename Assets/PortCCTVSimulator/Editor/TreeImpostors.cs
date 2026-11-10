using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace PortCCTV.EditorTools
{
    /// <summary>
    /// Cross-billboard ("impostor") trees for distant hill forests, baked from decimated Poly Haven CC0 tree models
    /// (pine_tree_01 variants a/b/c, tree_small_02; ThirdParty/PolyHavenCC0/models, SOURCE_models.md). The full models are
    /// 4.6–17 M triangles; each impostor is 3 vertical cards crossed at 60 deg (12 triangles) with an alpha-cut atlas of three
    /// orthographic views rendered in HDRP. Alpha is recovered by rendering on black and on white (a = 1 - (white - black)),
    /// so no alpha output is needed from the pipeline. Lighting is a fixed neutral key + fill with fixed exposure and no
    /// tonemapping; card normals point up so the runtime sun shades the cards uniformly.
    /// Prefabs: treeimp_pine_a/b/c, treeimp_broadleaf. Pivot at the trunk base, native model height.
    /// </summary>
    public static class TreeImpostors
    {
        const string Root = "Assets/PortCCTVSimulator";
        const string PHM = Root + "/ThirdParty/PolyHavenCC0/models";
        const string OutDir = Root + "/Textures/Generated/TreeImpostors";
        const string PrefabDir = Root + "/HarborPrefabs";
        const int Layer = 31, H = 1024;
        static readonly float[] ViewYaw = { 0f, 60f, 120f };

        public static void Build(HarborTheme theme)
        {
            Directory.CreateDirectory(OutDir);
            // albedo: mean linear luminance of the foliage diffuse texture (pine twig 0.0546, tree_small_02 leaves 0.0857, measured
            // from the 1k originals); targets were then lowered after renders: an up-facing card is fully sunlit, a canopy is not (self-shadow).
            var specs = new List<(string key, string gltfDir, string node, float albedo)>
            {
                ("treeimp_pine_a", "pine_tree_01_dec", "pine_tree_01_a_LOD0", 0.04f),
                ("treeimp_pine_b", "pine_tree_01_dec", "pine_tree_01_b_LOD0", 0.04f),
                ("treeimp_pine_c", "pine_tree_01_dec", "pine_tree_01_c_LOD0", 0.04f),
                ("treeimp_broadleaf", "tree_small_02_dec", null, 0.05f),
            };
            foreach (var s in specs)
            {
                var dir = $"{PHM}/{s.gltfDir}";
                if (!Directory.Exists(dir)) { Debug.LogWarning("[TreeImpostors] missing " + dir); continue; }
                var gltf = Directory.GetFiles(dir, "*.gltf")[0].Replace('\\', '/');
                var prefab = Bake(s.key, gltf, s.node, s.albedo, out var note);
                if (prefab == null) continue;
                theme.prefabs.RemoveAll(e => e.key == s.key);
                theme.prefabs.Add(new HarborTheme.PrefabEntry { key = s.key, prefab = prefab, source = note });
            }
            EditorUtility.SetDirty(theme);
        }

        static GameObject Bake(string key, string gltfPath, string node, float targetAlbedo, out string note)
        {
            note = "";
            AssetDatabase.ImportAsset(gltfPath, ImportAssetOptions.ForceSynchronousImport);
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(gltfPath);
            if (src == null) { Debug.LogError("[TreeImpostors] not imported " + gltfPath); return null; }
            var stage = new GameObject("ImpostorStage") { hideFlags = HideFlags.DontSave };
            stage.transform.position = new Vector3(0, 20000f, 0);
            var inst = Object.Instantiate(src, stage.transform);
            if (node != null)
                foreach (Transform c in inst.GetComponentsInChildren<Transform>(true))
                    if (c.GetComponent<Renderer>() != null && !c.name.StartsWith(node) && !(c.parent != null && c.parent.name.StartsWith(node)))
                        c.gameObject.SetActive(false);
            var rs = inst.GetComponentsInChildren<Renderer>(false);
            var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
            // pivot: trunk base = xz centre of the bounds, lowest point
            inst.transform.position -= new Vector3(b.center.x, b.min.y - stage.transform.position.y, b.center.z);
            b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
            foreach (var t in stage.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = Layer;
            float halfW = 0f;
            foreach (var r in rs)
                foreach (var corner in Corners(r.bounds))
                    halfW = Mathf.Max(halfW, new Vector2(corner.x - stage.transform.position.x, corner.z - stage.transform.position.z).magnitude);
            float hq = b.size.y, wq = 2f * halfW;
            int W = Mathf.Clamp(Mathf.RoundToInt(H * wq / hq / 4f) * 4, 64, 1024);

            // lighting / camera / volume for the bake
            var lights = new List<GameObject>();
            GameObject Light(Vector3 euler, float lux)
            {
                var go = new GameObject("ImpLight") { hideFlags = HideFlags.DontSave };
                go.transform.rotation = Quaternion.Euler(euler);
                var l = go.AddComponent<Light>(); l.type = LightType.Directional;
                var hd = go.AddComponent<HDAdditionalLightData>(); hd.SetIntensity(lux, LightUnit.Lux);
                l.shadows = LightShadows.None;
                lights.Add(go); return go;
            }
            Light(new Vector3(45f, 30f, 0f), 3.0f); Light(new Vector3(30f, 210f, 0f), 1.4f); Light(new Vector3(-80f, 0f, 0f), 0.4f);
            var volGo = new GameObject("ImpVolume") { hideFlags = HideFlags.DontSave };
            var vol = volGo.AddComponent<Volume>(); vol.isGlobal = true; vol.priority = 10000f;
            var prof = ScriptableObject.CreateInstance<VolumeProfile>();
            var ex = prof.Add<Exposure>(true); ex.mode.Override(ExposureMode.Fixed); ex.fixedExposure.Override(0f);
            var tm = prof.Add<Tonemapping>(true); tm.mode.Override(TonemappingMode.None);
            var fog = prof.Add<Fog>(true); fog.enabled.Override(false);
            var ve = prof.Add<VisualEnvironment>(true); ve.skyType.Override(0);
            prof.Add<Bloom>(true).intensity.Override(0f);
            prof.Add<Vignette>(true).intensity.Override(0f);
            vol.sharedProfile = prof;
            var camGo = new GameObject("ImpCam") { hideFlags = HideFlags.DontSave };
            var cam = camGo.AddComponent<Camera>();
            var hdc = camGo.AddComponent<HDAdditionalCameraData>();
            hdc.antialiasing = HDAdditionalCameraData.AntialiasingMode.None;
            hdc.clearColorMode = HDAdditionalCameraData.ClearColorMode.Color;
            cam.orthographic = true; cam.orthographicSize = hq / 2f; cam.cullingMask = 1 << Layer;
            cam.nearClipPlane = 0.1f; cam.farClipPlane = 4f * halfW + 20f;
            var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear) { antiAliasing = 1 };
            cam.targetTexture = rt; cam.aspect = (float)W / H;

            var atlas = new Texture2D(W * ViewYaw.Length, H, TextureFormat.RGBA32, true, false);
            Color[] Grab(Color bg)
            {
                hdc.backgroundColorHDR = bg;
                for (int i = 0; i < 4; i++) cam.Render();
                var prev = RenderTexture.active; RenderTexture.active = rt;
                var t = new Texture2D(W, H, TextureFormat.RGBAHalf, false, true);
                t.ReadPixels(new Rect(0, 0, W, H), 0, 0); t.Apply();
                RenderTexture.active = prev;
                var px = t.GetPixels(); Object.DestroyImmediate(t); return px;
            }
            int covered = 0;
            var lin = new Color[W * ViewYaw.Length * H];
            for (int v = 0; v < ViewYaw.Length; v++)
            {
                var dir = Quaternion.Euler(0, ViewYaw[v], 0) * Vector3.forward;   // camera looks along +dir
                var centre = stage.transform.position + Vector3.up * (hq / 2f);
                camGo.transform.SetPositionAndRotation(centre - dir * (2f * halfW + 10f), Quaternion.LookRotation(dir, Vector3.up));
                var blk = Grab(Color.black); var wht = Grab(Color.white);
                for (int i = 0; i < blk.Length; i++)
                {
                    var cb = blk[i]; var cw = wht[i];
                    float a = Mathf.Clamp01(1f - ((cw.r - cb.r) + (cw.g - cb.g) + (cw.b - cb.b)) / 3f);
                    var col = a > 0.02f ? new Color(cb.r / a, cb.g / a, cb.b / a, a) : new Color(0, 0, 0, a);
                    if (a > 0.5f) covered++;
                    lin[(i / W) * W * ViewYaw.Length + v * W + i % W] = col;
                }
            }
            double lsum = 0; int ln = 0;
            foreach (var c in lin) if (c.a > 0.5f) { lsum += 0.2126 * c.r + 0.7152 * c.g + 0.0722 * c.b; ln++; }
            float gain = ln > 0 && lsum > 0 ? (float)(targetAlbedo / (lsum / ln)) : 1f;
            var outPx = new Color[lin.Length];
            for (int i = 0; i < lin.Length; i++)
            {
                var c = lin[i];
                outPx[i] = new Color(Mathf.LinearToGammaSpace(Mathf.Clamp01(c.r * gain)), Mathf.LinearToGammaSpace(Mathf.Clamp01(c.g * gain)), Mathf.LinearToGammaSpace(Mathf.Clamp01(c.b * gain)), c.a);
            }
            // transparent pixels take the mean foliage colour (after the edge dilation below), so distant mip levels, which average
            // over many texels, fade to foliage colour instead of black
            Color mean = Color.clear; int mn = 0;
            foreach (var c in outPx) if (c.a > 0.5f) { mean += c; mn++; }
            if (mn > 0) { mean /= mn; for (int i = 0; i < outPx.Length; i++) if (outPx[i].a <= 0.02f) outPx[i] = new Color(mean.r, mean.g, mean.b, outPx[i].a); }
            atlas.SetPixels(outPx);
            // dilate colour into transparent pixels so mip/bilinear sampling at the cut edge does not bleed black
            Dilate(atlas, 4);
            atlas.Apply();
            var texPath = $"{OutDir}/{key}.png";
            File.WriteAllBytes(texPath, atlas.EncodeToPNG());
            Object.DestroyImmediate(atlas);
            cam.targetTexture = null; rt.Release(); Object.DestroyImmediate(rt);
            Object.DestroyImmediate(camGo); Object.DestroyImmediate(volGo); Object.DestroyImmediate(prof);
            foreach (var l in lights) Object.DestroyImmediate(l);
            Object.DestroyImmediate(stage);
            AssetDatabase.ImportAsset(texPath, ImportAssetOptions.ForceSynchronousImport);
            var imp = (TextureImporter)AssetImporter.GetAtPath(texPath);
            imp.sRGBTexture = true; imp.alphaIsTransparency = true; imp.mipmapEnabled = true; imp.mipMapsPreserveCoverage = true;
            imp.alphaTestReferenceValue = 0.5f; imp.wrapMode = TextureWrapMode.Clamp; imp.maxTextureSize = 4096;
            imp.SaveAndReimport();
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);

            var matPath = $"{Root}/Materials/M_{key}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null) { mat = new Material(Shader.Find("HDRP/Lit")); AssetDatabase.CreateAsset(mat, matPath); }
            // the pine twig texture is a brown-grey (sRGB ~0.22/0.20/0.17); pull it towards the dark green of Korean pines
            mat.SetTexture("_BaseColorMap", tex); mat.SetColor("_BaseColor", key.Contains("pine") ? new Color(0.55f, 0.80f, 0.48f) : Color.white);
            mat.SetFloat("_AlphaCutoffEnable", 1f); mat.SetFloat("_AlphaCutoff", 0.5f);
            mat.SetFloat("_DoubleSidedEnable", 1f); mat.SetFloat("_DoubleSidedNormalMode", 2f); /* none: back faces keep the up normal (flip made half the cards black) */ mat.SetFloat("_Smoothness", 0.1f); mat.SetFloat("_Metallic", 0f);
            // no specular: cards seen edge-on to their (up-tilted) shading normal are at grazing incidence, and the Fresnel
            // reflection of a bright sky turned backlit forests into a pale grey blanket. Specular-colour workflow, black F0.
            mat.SetFloat("_MaterialID", 4f); mat.SetColor("_SpecularColor", Color.black); mat.SetFloat("_EnergyConservingSpecularColor", 0f);
            HDMaterial.ValidateMaterial(mat); EditorUtility.SetDirty(mat);

            var mesh = CardMesh(key, wq, hq);
            var go = new GameObject(key);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = mat;
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, $"{PrefabDir}/{key}.prefab");
            Object.DestroyImmediate(go);
            note = $"impostor of {gltfPath}{(node != null ? " node " + node : "")} sha256={ScenarioIO.Sha256Hex(File.ReadAllBytes(gltfPath))}; " +
                   $"height {hq:F2} m, card width {wq:F2} m, albedo gain {gain:F2} -> {targetAlbedo}, atlas {W * ViewYaw.Length}x{H}, coverage {covered / (float)(W * H * ViewYaw.Length):P1}";
            Debug.Log("[TreeImpostors] " + key + ": " + note);
            return prefab;
        }

        static IEnumerable<Vector3> Corners(Bounds b)
        {
            for (int i = 0; i < 8; i++)
                yield return new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z);
        }

        static void Dilate(Texture2D t, int passes)
        {
            int w = t.width, h = t.height; var px = t.GetPixels();
            for (int p = 0; p < passes; p++)
            {
                var src = (Color[])px.Clone();
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        int i = y * w + x; if (src[i].a > 0.02f) continue;
                        Color acc = Color.clear; int n = 0;
                        for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++)
                            {
                                int xx = x + dx, yy = y + dy; if (xx < 0 || yy < 0 || xx >= w || yy >= h) continue;
                                var c = src[yy * w + xx]; if (c.a <= 0.02f) continue; acc += c; n++;
                            }
                        if (n > 0) { var c = acc / n; px[i] = new Color(c.r, c.g, c.b, src[i].a); }
                    }
            }
            t.SetPixels(px);
        }

        /// <summary>Three vertical cards through the trunk axis at the bake view yaws; card v shows atlas column v. The camera
        /// for view v looked along +dir(yaw), so the card lies in the plane perpendicular to dir with u running along "right".</summary>
        static Mesh CardMesh(string key, float wq, float hq)
        {
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var n = new List<Vector3>(); var t = new List<int>();
            int views = ViewYaw.Length;
            for (int i = 0; i < views; i++)
            {
                var right = Quaternion.Euler(0, ViewYaw[i], 0) * Vector3.right;
                int b0 = v.Count;
                float u0 = (float)i / views, u1 = (float)(i + 1) / views;
                v.Add(-right * wq / 2f); v.Add(right * wq / 2f); v.Add(right * wq / 2f + Vector3.up * hq); v.Add(-right * wq / 2f + Vector3.up * hq);
                uv.Add(new Vector2(u0, 0)); uv.Add(new Vector2(u1, 0)); uv.Add(new Vector2(u1, 1)); uv.Add(new Vector2(u0, 1));
                for (int k = 0; k < 4; k++) n.Add(Vector3.up);
                t.AddRange(new[] { b0, b0 + 2, b0 + 1, b0, b0 + 3, b0 + 2 });
            }
            var m = new Mesh { name = key + "_cards" };
            m.SetVertices(v); m.SetUVs(0, uv); m.SetNormals(n); m.SetTriangles(t, 0); m.RecalculateBounds(); m.RecalculateTangents();
            var path = $"{Root}/Meshes/{key}_cards.asset";
            var old = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (old != null) { EditorUtility.CopySerialized(m, old); return old; }
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        /// <summary>Batch entry for iterating on the bake alone: -executeMethod PortCCTV.EditorTools.TreeImpostors.RunBatch</summary>
        public static void RunBatch()
        {
            try
            {
                var theme = HarborTheme.Load();
                Build(theme);
                AssetDatabase.SaveAssets();
                EditorApplication.Exit(0);
            }
            catch (System.Exception ex) { Debug.LogException(ex); EditorApplication.Exit(1); }
        }
    }
}
