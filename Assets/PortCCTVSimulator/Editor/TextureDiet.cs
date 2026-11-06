using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace PortCCTV.EditorTools
{
    /// <summary>
    /// glTFast imports GLB textures uncompressed at full size (hundreds of MB per Innerscene model), although these paint
    /// textures are near-uniform colours. For prefab copies only, materials whose base-colour texture is flat are replaced by
    /// a copy with the texture removed and the base colour multiplied by the texture mean. Source GLBs are untouched.
    /// </summary>
    public static class TextureDiet
    {
        const string Dir = "Assets/PortCCTVSimulator/Materials/Derived";
        static readonly int BaseTex = Shader.PropertyToID("baseColorTexture");
        static readonly int BaseFac = Shader.PropertyToID("baseColorFactor");
        static readonly Dictionary<Material, Material> Cache = new Dictionary<Material, Material>();

        /// <summary>Mean and max per-channel std of a texture, measured on a 64x64 GPU downsample.</summary>
        static void Stats(Texture tex, out Color mean, out float std)
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                throw new System.InvalidOperationException("TextureDiet needs GPU texture readback; do not run asset generation with -nographics.");
            var rt = RenderTexture.GetTemporary(64, 64, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Graphics.Blit(tex, rt);
            var prev = RenderTexture.active; RenderTexture.active = rt;
            var t = new Texture2D(64, 64, TextureFormat.RGBA32, false, false);
            t.ReadPixels(new Rect(0, 0, 64, 64), 0, 0); t.Apply();
            RenderTexture.active = prev; RenderTexture.ReleaseTemporary(rt);
            var px = t.GetPixels(); Object.DestroyImmediate(t);
            var m = Vector4.zero; foreach (var p in px) m += (Vector4)p; m /= px.Length;
            var v = Vector4.zero; foreach (var p in px) { var d = (Vector4)p - m; v += Vector4.Scale(d, d); } v /= px.Length;
            mean = m; std = Mathf.Sqrt(Mathf.Max(v.x, Mathf.Max(v.y, v.z)));
        }

        /// <summary>Returns a note "flattened N, kept M (names)". Threshold: std of a sRGB 8-bit channel below 0.03.</summary>
        public static string Apply(GameObject root, string tag)
        {
            Directory.CreateDirectory(Dir);
            int flat = 0; var kept = new SortedSet<string>();
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials; bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    if (m == null || !m.HasProperty(BaseTex)) continue;
                    var tex = m.GetTexture(BaseTex);
                    if (tex == null) continue;
                    if (!Cache.TryGetValue(m, out var copy))
                    {
                        Stats(tex, out var mean, out float std);
                        if (std < 0.03f)
                        {
                            copy = new Material(m) { name = m.name + " (flat)" };
                            copy.SetTexture(BaseTex, null);
                            var f = m.HasProperty(BaseFac) ? m.GetColor(BaseFac) : Color.white;
                            // texture mean is sRGB-encoded; factor is linear
                            var lin = new Color(Mathf.GammaToLinearSpace(mean.r), Mathf.GammaToLinearSpace(mean.g), Mathf.GammaToLinearSpace(mean.b), mean.a);
                            // the factor property holds sRGB (glTFast writes baseColorLinear.gamma; Unity linearises it on upload),
                            // so the linear product factor x texture-mean is re-encoded to sRGB (before 2026-11-06 the linear value
                            // was stored directly, which rendered flattened paints darker than their textures)
                            var fl = f.linear;
                            var g = new Color(fl.r * lin.r, fl.g * lin.g, fl.b * lin.b, 1f).gamma; g.a = f.a;
                            copy.SetColor(BaseFac, g);
                            var safe = string.Concat((tag + "_" + m.name).Select(c => char.IsLetterOrDigit(c) ? c : '_'));
                            AssetDatabase.CreateAsset(copy, AssetDatabase.GenerateUniqueAssetPath($"{Dir}/{safe}.mat"));
                        }
                        else copy = null;
                        Cache[m] = copy;
                    }
                    if (copy != null) { mats[i] = copy; changed = true; flat++; }
                    else kept.Add(m.name);
                }
                if (changed) r.sharedMaterials = mats;
            }
            return $"flattened {flat} texture slots; kept textured: {(kept.Count == 0 ? "none" : string.Join(", ", kept))}";
        }

        public static void ResetCache() => Cache.Clear();
    }
}
