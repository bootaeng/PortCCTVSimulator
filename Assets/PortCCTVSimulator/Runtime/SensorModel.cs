using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace PortCCTV
{
    /// <summary>
    /// CCTV camera image degradation applied after rendering (scenario field "sensor"). Engine-side image model, not a calibrated
    /// camera: distortion, blur, noise, vignetting and JPEG quality are set by hand or by the UI presets.
    /// Units are output pixels / 8-bit linear sensor counts (DN) at the capture resolution.
    /// </summary>
    [Serializable]
    public class SensorConfig
    {
        public bool enabled = false;
        /// <summary>UI preset that produced the values (informational; the numbers below are what is applied).</summary>
        public string preset = "off";
        /// <summary>Radial lens distortion, inverse model: source = centre + (p - centre) * s * (1 + k1 r^2 + k2 r^4), r = 1 at the
        /// image corner. k1 &gt; 0 = barrel (typical wide CCTV lens).</summary>
        public float distortion_k1 = 0f, distortion_k2 = 0f;
        /// <summary>Scale s chosen so the output has no empty (black) border; false = s = 1 (black corners for barrel).</summary>
        public bool distortion_fill = true;
        /// <summary>Gaussian lens / focus blur sigma (output pixels).</summary>
        public float blur_sigma_px = 0f;
        /// <summary>Camera shake / pole sway: linear motion blur length (pixels) and direction (deg, 0 = image x, 90 = image down).</summary>
        public float motion_blur_px = 0f, motion_blur_angle_deg = 0f;
        /// <summary>Read noise std (linear DN 0..255) and shot-noise coefficient (variance = k * DN); both multiplied by the gain.</summary>
        public float noise_read_dn = 0f, noise_shot = 0f;
        /// <summary>Share of per-channel (colour) noise, 0 = luminance only.</summary>
        public float noise_chroma = 0.3f;
        /// <summary>Sensor gain multiplying the noise; -1 = automatic from the exposure (dark scenes = higher gain).</summary>
        public float gain = -1f;
        public float vignette = 0f;
        /// <summary>IR night mode: grey image.</summary>
        public bool monochrome = false;
        /// <summary>JPEG round trip quality 1..100 (0 = none). Unity's JPEG encoder; stands in for the camera / recorder codec.</summary>
        public int jpeg_quality = 0;
        /// <summary>Water drops on the lens window: -1 = automatic (rain intensity, a few melted flakes in snow), 0..1 = density.</summary>
        public float lens_drops = -1f;
        public int seed = 7;
        /// <summary>Also write the undegraded image as rgb_clean.png (paired data).</summary>
        public bool save_clean = true;

        public SensorConfig Clone() => JsonUtility.FromJson<SensorConfig>(JsonUtility.ToJson(this));

        public static SensorConfig Preset(string id)
        {
            var s = new SensorConfig { preset = id, enabled = id != "off" };
            switch (id)
            {
                case "hd_ip":      // modern HD IP camera, good conditions
                    s.distortion_k1 = 0.04f; s.blur_sigma_px = 0.5f; s.noise_read_dn = 0.5f; s.noise_shot = 0.04f; s.vignette = 0.12f; s.jpeg_quality = 85; break;
                case "typical":    // ordinary port CCTV
                    s.distortion_k1 = 0.12f; s.blur_sigma_px = 0.9f; s.noise_read_dn = 1.0f; s.noise_shot = 0.10f; s.vignette = 0.25f; s.jpeg_quality = 55; break;
                case "low_quality":  // old / low-bitrate camera
                    s.distortion_k1 = 0.2f; s.distortion_k2 = 0.04f; s.blur_sigma_px = 1.6f; s.noise_read_dn = 2.0f; s.noise_shot = 0.3f;
                    s.noise_chroma = 0.5f; s.vignette = 0.35f; s.motion_blur_px = 2f; s.motion_blur_angle_deg = 10f; s.jpeg_quality = 25; break;
                case "night_ir":   // IR night mode: monochrome, noisy, soft
                    s.distortion_k1 = 0.12f; s.blur_sigma_px = 1.1f; s.noise_read_dn = 1.0f; s.noise_shot = 0.2f; s.vignette = 0.3f;
                    s.monochrome = true; s.jpeg_quality = 50; break;
            }
            return s;
        }

        public static readonly string[] PresetIds = { "off", "hd_ip", "typical", "low_quality", "night_ir" };
    }

    /// <summary>A water drop on the window in front of the lens, in SOURCE image pixels (top-left origin) at capture resolution.</summary>
    [Serializable]
    public class LensDrop
    {
        public float x, y, rx, ry;
        public float opacity, refraction, blur;
    }

    /// <summary>Resolved sensor values written to metadata.</summary>
    [Serializable]
    public class SensorRecord
    {
        public bool enabled;
        public SensorConfig config;
        public float gain_applied = 1f;
        public float distortion_scale = 1f;
        public string distortion_model = "source = c + (p - c) * s * (1 + k1 r^2 + k2 r^4); r = |p - c| / half image diagonal; p, c in output pixels (top-left origin); bilinear RGB sampling, nearest-neighbour label masks; boxes from projected vertices mapped with the forward (inverted) model.";
        public string intrinsics_note = "camera.fx/fy/cx/cy describe the UNDISTORTED pinhole image; rgb.png and all labels are in the distorted output image.";
        public float lens_drop_density;
        public List<LensDrop> lens_drops = new List<LensDrop>();
        public int jpeg_quality;
        public string pipeline = "render -> distortion -> lens drops -> lens blur + shake -> vignetting -> noise (read + shot, x gain) -> monochrome -> sRGB -> JPEG";
    }

    /// <summary>Radial distortion shared by the GPU image pass and the CPU label warp.</summary>
    public sealed class LensModel
    {
        public readonly float k1, k2, scale;
        readonly float _cx, _cy, _halfDiag;

        LensModel(float k1, float k2, float scale, int W, int H)
        {
            this.k1 = k1; this.k2 = k2; this.scale = scale;
            _cx = W / 2f; _cy = H / 2f; _halfDiag = 0.5f * Mathf.Sqrt((float)W * W + (float)H * H);
        }

        /// <summary>null when the sensor has no distortion.</summary>
        public static LensModel Make(SensorConfig s, int W, int H)
        {
            if (s == null || !s.enabled || (Mathf.Abs(s.distortion_k1) < 1e-6f && Mathf.Abs(s.distortion_k2) < 1e-6f)) return null;
            float scale = 1f;
            if (s.distortion_fill)
            {
                // largest source factor along the output border: s = 1 / max h(r) keeps every border pixel inside the source
                float hd = 0.5f * Mathf.Sqrt((float)W * W + (float)H * H), hmax = 0f;
                for (int i = 0; i <= 200; i++)
                {
                    float t = i / 200f;
                    foreach (var p in new[] { new Vector2(W / 2f, (t - 0.5f) * H), new Vector2((t - 0.5f) * W, H / 2f) })
                    {
                        float r2 = p.sqrMagnitude / (hd * hd);
                        hmax = Mathf.Max(hmax, 1f + s.distortion_k1 * r2 + s.distortion_k2 * r2 * r2);
                    }
                }
                scale = hmax > 1e-4f ? 1f / hmax : 1f;
            }
            return new LensModel(s.distortion_k1, s.distortion_k2, scale, W, H);
        }

        float Factor(float r2) => scale * (1f + k1 * r2 + k2 * r2 * r2);

        /// <summary>Output pixel coordinate -> source (undistorted render) pixel coordinate, top-left origin.</summary>
        public Vector2 SourceOf(Vector2 p)
        {
            float dx = p.x - _cx, dy = p.y - _cy;
            float f = Factor((dx * dx + dy * dy) / (_halfDiag * _halfDiag));
            return new Vector2(_cx + dx * f, _cy + dy * f);
        }

        /// <summary>Source pixel -> output pixel (Newton on the radius). False if the model folds (no unique inverse).</summary>
        public bool OutputOf(Vector2 s, out Vector2 p)
        {
            float dx = s.x - _cx, dy = s.y - _cy;
            float rs = Mathf.Sqrt(dx * dx + dy * dy) / _halfDiag;
            if (rs < 1e-9f) { p = s; return true; }
            float r = rs / scale;
            for (int i = 0; i < 30; i++)
            {
                float r2 = r * r;
                float g = Factor(r2), dg = scale * (2f * k1 * r + 4f * k2 * r2 * r);
                float d = g + r * dg;
                if (d <= 1e-6f) { p = s; return false; }
                float step = (r * g - rs) / d;
                r -= step;
                if (Mathf.Abs(step) < 1e-7f) break;
            }
            float k = r / rs;
            p = new Vector2(_cx + dx * k, _cy + dy * k);
            return true;
        }

        /// <summary>Nearest-neighbour warp of a top-left label mask into the distorted output image.</summary>
        public byte[] WarpMask(byte[] src, int W, int H)
        {
            var dst = new byte[W * H];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    var s = SourceOf(new Vector2(x + 0.5f, y + 0.5f));
                    int ix = Mathf.FloorToInt(s.x), iy = Mathf.FloorToInt(s.y);
                    if (ix >= 0 && iy >= 0 && ix < W && iy < H) dst[y * W + x] = src[iy * W + ix];
                }
            return dst;
        }
    }

    /// <summary>Applies SensorConfig to a rendered frame (GPU pass + CPU JPEG).</summary>
    public sealed class SensorProcessor
    {
        public const int MaxDrops = 64;
        /// <summary>True: texture v grows downwards in the blit's uv (top-left rows first). Determined by test
        /// SensorTests.LensDrop_LandsWhereItsLabelSaysItIs; Unity's default (v up) is false.</summary>
        public static bool UvYDown = false;
        readonly Material _mat;
        readonly Vector4[] _drops = new Vector4[MaxDrops], _dropP = new Vector4[MaxDrops];
        public bool Available => _mat != null && _mat.shader != null && _mat.shader.isSupported;

        public SensorProcessor(Shader shader)
        {
            if (shader != null) _mat = new Material(shader) { name = "PortCCTV_CctvDegrade", hideFlags = HideFlags.DontSave };
        }

        public static float AutoGain(float ev100) => Mathf.Clamp(Mathf.Pow(2f, (11f - ev100) / 5f), 1f, 4f);

        public static float DropDensity(SensorConfig s, ResolvedWeather wx, WeatherConfig wxCfg)
        {
            if (s.lens_drops >= 0f) return Mathf.Clamp01(s.lens_drops);
            if (wxCfg == null || !wxCfg.Precipitating) return 0f;
            return wxCfg.precipitation == "rain" ? 0.15f + 0.55f * wxCfg.I : 0.1f * wxCfg.I;
        }

        /// <summary>Deterministic drops for this seed and density, in source pixels of a W x H image.</summary>
        public static List<LensDrop> MakeDrops(int seed, float density, int W, int H)
        {
            var list = new List<LensDrop>();
            int n = Mathf.Clamp(Mathf.RoundToInt(density * 40f), 0, MaxDrops);
            var rng = new System.Random(seed * 7919 + 13);
            float U() => (float)rng.NextDouble();
            for (int i = 0; i < n; i++)
            {
                float r = H * Mathf.Lerp(0.02f, 0.075f, Mathf.Pow(U(), 1.5f));
                var d = new LensDrop { x = U() * W, y = U() * H, rx = r, ry = r, opacity = Mathf.Lerp(0.75f, 0.95f, U()), refraction = Mathf.Lerp(0.5f, 0.9f, U()), blur = Mathf.Lerp(0.25f, 0.45f, U()) };
                if (U() < 0.3f) { d.ry = r * Mathf.Lerp(1.6f, 2.8f, U()); d.rx = r * Mathf.Lerp(0.7f, 0.9f, U()); }   // running drop
                list.Add(d);
            }
            return list;
        }

        /// <summary>True if the source pixel lies inside a drop where the drop dominates the image (alpha &gt; ~0.5).</summary>
        public static bool UnderDrop(List<LensDrop> drops, Vector2 src)
        {
            if (drops == null) return false;
            foreach (var d in drops)
            {
                float ex = (src.x - d.x) / d.rx, ey = (src.y - d.y) / d.ry;
                if (ex * ex + ey * ey < 0.64f) return true;
            }
            return false;
        }

        /// <summary>GPU pass. <paramref name="pixelScale"/> = destination height / capture height (preview); drops and blur are
        /// given at capture resolution.</summary>
        public void Blit(Texture src, RenderTexture dst, SensorConfig s, float gain, List<LensDrop> drops, int captureW, int captureH, int frameSeed = 0)
        {
            if (!Available) { Graphics.Blit(src, dst); return; }
            int W = dst.width, H = dst.height;
            float ps = (float)H / captureH;
            var lens = LensModel.Make(s, W, H);
            _mat.SetTexture("_MainTex", src);
            _mat.SetVector("_Size", new Vector4(W, H, 1f / W, 1f / H));
            _mat.SetFloat("_K1", lens != null ? lens.k1 : 0f);
            _mat.SetFloat("_K2", lens != null ? lens.k2 : 0f);
            _mat.SetFloat("_DistScale", lens != null ? lens.scale : 1f);
            _mat.SetFloat("_BlurSigma", s.blur_sigma_px * ps);
            _mat.SetFloat("_MotionLen", s.motion_blur_px * ps);
            float a = s.motion_blur_angle_deg * Mathf.Deg2Rad;
            _mat.SetVector("_MotionDir", new Vector4(Mathf.Cos(a), UvYDown ? Mathf.Sin(a) : -Mathf.Sin(a), 0, 0));
            _mat.SetFloat("_NoiseRead", s.noise_read_dn);
            _mat.SetFloat("_NoiseShot", s.noise_shot);
            _mat.SetFloat("_NoiseChroma", Mathf.Clamp01(s.noise_chroma));
            _mat.SetFloat("_Gain", gain);
            _mat.SetFloat("_Vignette", Mathf.Clamp01(s.vignette));
            _mat.SetFloat("_Mono", s.monochrome ? 1f : 0f);
            _mat.SetFloat("_OutputLinear", dst.sRGB ? 1f : 0f);   // sRGB target encodes itself
            _mat.SetInteger("_SeedI", s.seed * 1000003 + frameSeed);
            int n = drops == null ? 0 : Mathf.Min(drops.Count, MaxDrops);
            for (int i = 0; i < MaxDrops; i++)
            {
                if (i < n)
                {
                    var d = drops[i];
                    float u = d.x / captureW, v = d.y / captureH;
                    _drops[i] = new Vector4(u, UvYDown ? v : 1f - v, d.rx * ps, d.ry * ps);
                    _dropP[i] = new Vector4(d.opacity, d.refraction, d.blur, 0);
                }
                else { _drops[i] = new Vector4(-10, -10, 1, 1); _dropP[i] = Vector4.zero; }
            }
            _mat.SetVectorArray("_Drops", _drops);
            _mat.SetVectorArray("_DropP", _dropP);
            _mat.SetInteger("_DropCount", n);
            var cmd = new CommandBuffer { name = "PortCCTV sensor" };
            cmd.SetRenderTarget(dst);
            cmd.DrawProcedural(Matrix4x4.identity, _mat, 0, MeshTopology.Triangles, 3);
            bool srgbWrite = GL.sRGBWrite;
            GL.sRGBWrite = true;                       // an sRGB target must encode the linear output
            Graphics.ExecuteCommandBuffer(cmd);
            GL.sRGBWrite = srgbWrite;
            cmd.Release();
        }

        /// <summary>Renders the degraded image of <paramref name="src"/> and reads it back (bottom-left origin pixels).</summary>
        public Color32[] Process(RenderTexture src, SensorConfig s, float gain, List<LensDrop> drops)
        {
            int W = src.width, H = src.height;
            var dst = new RenderTexture(new RenderTextureDescriptor(W, H, GraphicsFormat.R8G8B8A8_UNorm, 0) { msaaSamples = 1 }) { name = "PortCCTV_Sensor" };
            dst.Create();
            var prev = RenderTexture.active;
            try
            {
                Blit(src, dst, s, gain, drops, W, H);
                RenderTexture.active = dst;
                var tex = new Texture2D(W, H, TextureFormat.RGBA32, false, true);
                tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
                tex.Apply(false);
                var px = tex.GetPixels32();
                for (int i = 0; i < px.Length; i++) px[i].a = 255;
                if (s.jpeg_quality > 0) px = Jpeg(tex, px, W, H, s.jpeg_quality);
                Util.SafeDestroy(tex);
                return px;
            }
            finally
            {
                RenderTexture.active = prev;
                dst.Release(); Util.SafeDestroy(dst);
            }
        }

        /// <summary>JPEG encode + decode round trip (Unity encoder).</summary>
        public static Color32[] Jpeg(Texture2D scratch, Color32[] px, int W, int H, int quality)
        {
            scratch.SetPixels32(px);
            scratch.Apply(false);
            var bytes = scratch.EncodeToJPG(Mathf.Clamp(quality, 1, 100));
            var dec = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            dec.LoadImage(bytes, false);
            Color32[] outPx = dec.width == W && dec.height == H ? dec.GetPixels32() : px;
            Util.SafeDestroy(dec);
            for (int i = 0; i < outPx.Length; i++) outPx[i].a = 255;
            return outPx;
        }

        public void Destroy() { if (_mat != null) Util.SafeDestroy(_mat); }
    }
}
