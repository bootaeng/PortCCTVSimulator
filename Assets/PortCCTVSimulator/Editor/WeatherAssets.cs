using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace PortCCTV.EditorTools
{
    /// <summary>
    /// Precipitation particle materials (HDRP/Lit, transparent, lit by sun and sky so their brightness follows the exposure).
    /// One procedural soft-disc texture; rain uses stretched billboards (streaks), snow plain billboards. Registered in the theme as
    /// "weather_rain" / "weather_snow". Generated here, no external asset.
    /// </summary>
    public static class WeatherAssets
    {
        const string Root = "Assets/PortCCTVSimulator";

        public static void Build(HarborTheme theme)
        {
            var lit = Shader.Find("HDRP/Lit");
            var texDir = Root + "/Textures/Weather";
            Directory.CreateDirectory(texDir);
            var texPath = texDir + "/weather_soft_disc.png";
            const int N = 64;
            var t = new Texture2D(N, N, TextureFormat.RGBA32, false, true);
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float dx = (x + 0.5f) / N * 2f - 1f, dy = (y + 0.5f) / N * 2f - 1f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01(1f - r); a = a * a * (3f - 2f * a);
                    t.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            File.WriteAllBytes(texPath, t.EncodeToPNG());
            Object.DestroyImmediate(t);
            AssetDatabase.ImportAsset(texPath, ImportAssetOptions.ForceSynchronousImport);
            var imp = (TextureImporter)AssetImporter.GetAtPath(texPath);
            imp.alphaIsTransparency = true; imp.wrapMode = TextureWrapMode.Clamp; imp.mipmapEnabled = true; imp.sRGBTexture = true;
            imp.textureCompression = TextureImporterCompression.Uncompressed;
            imp.SaveAndReimport();
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);

            Add(theme, "weather_rain", Make(lit, tex, "M_weather_rain", new Color(0.80f, 0.84f, 0.90f, 0.32f), 0.75f));
            Add(theme, "weather_ripple", Make(lit, RippleSheet(texDir), "M_weather_ripple", new Color(0.92f, 0.95f, 1f, 0.7f), 0.5f));
            var decal = Shader.Find("HDRP/Decal");
            // wet: darker (albedo blended towards dark grey) and glossy; snow: white, matte. Strength = projector fade factor.
            Add(theme, "weather_wet_decal", MakeDecal(decal, "M_weather_wet_decal", new Color(0.09f, 0.09f, 0.10f, 1f), 0.55f, 0.92f));
            Add(theme, "weather_snow_decal", MakeDecal(decal, "M_weather_snow_decal", new Color(0.93f, 0.94f, 0.96f, 1f), 0.97f, 0.3f));
            Add(theme, "weather_snow", Make(lit, tex, "M_weather_snow", new Color(0.97f, 0.97f, 0.98f, 0.92f), 0.15f));
            // instance-ID material for the GPU visible mask (keeps the hidden shader in the build; per-instance copies at runtime)
            var idShader = Shader.Find("Hidden/PortCCTV/InstanceId");
            if (idShader != null)
            {
                var idPath = $"{Root}/Materials/M_instance_id.mat";
                var idm = AssetDatabase.LoadAssetAtPath<Material>(idPath);
                if (idm == null) { idm = new Material(idShader); AssetDatabase.CreateAsset(idm, idPath); }
                idm.shader = idShader; EditorUtility.SetDirty(idm);
                Add(theme, "instance_id", idm);
            }
            else Debug.LogError("[Bootstrap] Hidden/PortCCTV/InstanceId shader not found");
            // CCTV degradation pass (keeps the hidden shader in the player)
            var degShader = Shader.Find("Hidden/PortCCTV/CctvDegrade");
            if (degShader != null)
            {
                var dPath = $"{Root}/Materials/M_cctv_degrade.mat";
                var dm = AssetDatabase.LoadAssetAtPath<Material>(dPath);
                if (dm == null) { dm = new Material(degShader); AssetDatabase.CreateAsset(dm, dPath); }
                dm.shader = degShader; EditorUtility.SetDirty(dm);
                Add(theme, "cctv_degrade", dm);
            }
            else Debug.LogError("[Bootstrap] Hidden/PortCCTV/CctvDegrade shader not found");
            // emissive bulb for lamps and navigation lights (opaque HDRP/Lit, black base, emission set per colour at runtime)
            {
                var bPath = $"{Root}/Materials/M_light_bulb.mat";
                var bm = AssetDatabase.LoadAssetAtPath<Material>(bPath);
                if (bm == null) { bm = new Material(lit); AssetDatabase.CreateAsset(bm, bPath); }
                bm.shader = lit;
                bm.SetColor("_BaseColor", Color.black);
                bm.SetFloat("_Smoothness", 0.2f); bm.SetFloat("_Metallic", 0f);
                bm.SetColor("_EmissiveColor", new Color(1f, 1f, 1f) * 10000f);
                bm.SetFloat("_EmissiveExposureWeight", 1f);
                bm.SetFloat("_UseEmissiveIntensity", 0f);
                HDMaterial.ValidateMaterial(bm);
                EditorUtility.SetDirty(bm);
                Add(theme, "light_bulb", bm);
            }
        }

        static Material Make(Shader lit, Texture2D tex, string name, Color color, float smooth)
        {
            var path = $"{Root}/Materials/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(lit); AssetDatabase.CreateAsset(m, path); }
            m.shader = lit;
            m.SetFloat("_SurfaceType", 1f);              // transparent
            m.SetFloat("_BlendMode", 0f);                // alpha
            m.SetFloat("_AlphaCutoffEnable", 0f);
            m.SetFloat("_EnableFogOnTransparent", 1f);
            m.SetFloat("_DoubleSidedEnable", 1f);
            m.SetFloat("_ReceivesSSRTransparent", 0f);
            m.SetTexture("_BaseColorMap", tex);
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Smoothness", smooth);
            m.SetFloat("_Metallic", 0f);
            m.SetFloat("_EnableBlendModePreserveSpecularLighting", 0f);   // otherwise the transparent parts of the quads still reflect the sky
            HDMaterial.ValidateMaterial(m);
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>4x4 flip-book of an expanding, fading ring (rain drops hitting the water), 64 px per frame.</summary>
        static Texture2D RippleSheet(string texDir)
        {
            var path = texDir + "/weather_ripple_sheet.png";
            const int F = 64, N = 4 * F;
            var t = new Texture2D(N, N, TextureFormat.RGBA32, false, true);
            for (int fy = 0; fy < 4; fy++)
                for (int fx = 0; fx < 4; fx++)
                {
                    int frame = (3 - fy) * 4 + fx;                       // Unity sheets start at the top-left
                    float u = frame / 15f;
                    float radius = 0.12f + 0.80f * u, width = 0.05f + 0.05f * u, alpha = Mathf.Pow(1f - u, 1.4f);
                    for (int y = 0; y < F; y++)
                        for (int x = 0; x < F; x++)
                        {
                            float dx = (x + 0.5f) / F * 2f - 1f, dy = (y + 0.5f) / F * 2f - 1f;
                            float r = Mathf.Sqrt(dx * dx + dy * dy);
                            float a = Mathf.Clamp01(1f - Mathf.Abs(r - radius) / width); a = a * a * alpha;
                            float inner = r < radius ? 0.25f * alpha * Mathf.Clamp01(1f - (radius - r) / 0.2f) : 0f;   // faint second ring
                            t.SetPixel(fx * F + x, fy * F + y, new Color(1f, 1f, 1f, Mathf.Clamp01(a + inner * 0.3f)));
                        }
                }
            File.WriteAllBytes(path, t.EncodeToPNG());
            Object.DestroyImmediate(t);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.alphaIsTransparency = true; imp.wrapMode = TextureWrapMode.Clamp; imp.mipmapEnabled = false; imp.sRGBTexture = true;
            imp.textureCompression = TextureImporterCompression.Uncompressed;
            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static Material MakeDecal(Shader decal, string name, Color color, float blend, float smooth)
        {
            var path = $"{Root}/Materials/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(decal); AssetDatabase.CreateAsset(m, path); }
            m.shader = decal;
            m.SetColor("_BaseColor", color);
            m.SetFloat("_DecalBlend", blend);
            m.SetFloat("_AffectAlbedo", 1f); m.SetFloat("_AffectNormal", 0f); m.SetFloat("_AffectSmoothness", 1f);
            m.SetFloat("_AffectMetal", 0f); m.SetFloat("_AffectAO", 0f); m.SetFloat("_AffectEmission", 0f);
            m.SetFloat("_Smoothness", smooth); m.SetFloat("_Metallic", 0f);
            m.SetFloat("_MaskBlendSrc", 1f);
            HDMaterial.ValidateMaterial(m);
            EditorUtility.SetDirty(m);
            return m;
        }

        static void Add(HarborTheme theme, string key, Material m)
        {
            theme.materials.RemoveAll(e => e.key == key);
            theme.materials.Add(new HarborTheme.Entry { key = key, material = m });
        }
    }
}
