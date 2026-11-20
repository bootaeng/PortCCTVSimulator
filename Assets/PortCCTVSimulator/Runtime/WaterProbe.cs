using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace PortCCTV
{
    /// <summary>
    /// Diagnostic (2026-11-12): does the water the camera renders sit where the vessels' height query says it is?
    /// The camera renders once more; a custom pass (before post-processing) writes the absolute world position of the
    /// front-most rendered surface per pixel (camera depth, which includes the HDRP water surface). For water pixels (no collider
    /// on the camera ray, height within 6 m of the mean plane) the rendered height is compared with SimWorld.WaterHeightAt at
    /// the same world x, z (the HDRP CPU search the vessels use, including the water mask). Results are split by the harbour mask
    /// attenuation (inside: swell factor &lt; 0.5, outside: &gt; 0.95).
    /// </summary>
    public static class WaterProbe
    {
        [Serializable]
        public class Stats
        {
            public string zone;
            public int samples;
            public float mean_abs_diff_m, p95_abs_diff_m, mean_diff_m;
            public float rendered_height_std_m, query_height_std_m;
        }

        [Serializable]
        public class Report
        {
            public string scenario_id, water_preset_id;
            public bool water_mask_on;
            public float water_time_s;
            public List<Stats> zones = new List<Stats>();
            public List<string> debug = new List<string>();
            public string method = "GPU: world y of the front-most rendered surface (custom pass reading the camera depth-stencil buffer, which holds the water surface, before post-processing); " +
                                   "CPU: WaterSurface.ProjectPointOnWaterSurface at the rendered x, z (SimWorld.WaterHeightAt). Grid of 96 x 54 pixels.";
        }

        sealed class Pass : CustomPass
        {
            public Camera camera; public Material mat; public RTHandle target;
            protected override void Execute(CustomPassContext ctx)
            {
                if (camera == null || ctx.hdCamera.camera != camera || target == null) return;
                ctx.propertyBlock.SetTexture("_ProbeDepth", ctx.cameraDepthBuffer);
                CoreUtils.DrawFullScreen(ctx.cmd, mat, target, ctx.propertyBlock, shaderPassId: 0);
            }
        }

        public static Report Run(SimWorld w, int W = 960, int H = 540, bool debug = false)
        {
            var shader = Shader.Find("Hidden/PortCCTV/WorldHeightProbe");
            if (shader == null || !shader.isSupported) throw new InvalidOperationException("WorldHeightProbe shader not available");
            var cam = w.CctvCamera;
            var go = new GameObject("WaterProbePass");
            var vol = go.AddComponent<CustomPassVolume>();
            vol.isGlobal = true; vol.injectionPoint = CustomPassInjectionPoint.BeforePostProcess;
            var mat = new Material(shader) { hideFlags = HideFlags.DontSave };
            var probeRt = RTHandles.Alloc(W, H, colorFormat: GraphicsFormat.R32G32B32A32_SFloat, name: "PortCCTV_WaterProbe");
            var pass = new Pass { name = "PortCCTV water probe", camera = cam, mat = mat, target = probeRt };
            vol.customPasses.Add(pass);
            var colour = new RenderTexture(new RenderTextureDescriptor(W, H, GraphicsFormat.R8G8B8A8_SRGB, 24)); colour.Create();
            var prevT = cam.targetTexture; bool prevE = cam.enabled;
            var rep = new Report { scenario_id = w.Scenario?.scenario_id, water_preset_id = w.Environment.water_preset_id, water_mask_on = w.Environment.water_mask_on, water_time_s = w.Environment.water_time_requested_s };
            try
            {
                cam.enabled = true; cam.targetTexture = colour; cam.aspect = (float)W / H;
                cam.Render(); w.FreezeWaterAt(w.Scenario.environment.water_time_s);
                for (int i = 0; i < 3; i++) { cam.Render(); AsyncGPUReadback.WaitAllRequests(); }
                var prev = RenderTexture.active; RenderTexture.active = probeRt.rt;
                var tex = new Texture2D(W, H, TextureFormat.RGBAFloat, false, true);
                tex.ReadPixels(new Rect(0, 0, W, H), 0, 0); tex.Apply(false);
                RenderTexture.active = prev;
                var px = tex.GetPixels(); Util.SafeDestroy(tex);
                float waterY = w.Harbor.water_reference_y;
                var mask = w.Environment.water_mask_on ? HarborWaterMask.Get(w.Harbor) : null;
                var buckets = new Dictionary<string, List<(float d, float gy, float qy)>>();
                foreach (var zn in new[] { "inside_harbour", "open_sea", "open_sea_0_300m", "open_sea_300_700m", "open_sea_700m_plus", "all" }) buckets[zn] = new List<(float, float, float)>();
                Physics.SyncTransforms();
                for (int gy = 0; gy < 54; gy++)
                    for (int gx = 0; gx < 96; gx++)
                    {
                        int x = (int)((gx + 0.5f) * W / 96f), y = (int)((gy + 0.5f) * H / 54f);
                        var c = px[y * W + x];
                        var ray = cam.ScreenPointToRay(new Vector3(x + 0.5f, y + 0.5f, 0f));
                        var p = new Vector3(c.g, c.r, c.b);
                        float dist = Vector3.Distance(ray.origin, p);
                        bool hitAny = Physics.Raycast(ray, out var hit, dist + 1.5f, ~0, QueryTriggerInteraction.Ignore);
                        if (debug && gx % 16 == 8 && gy % 9 == 4)
                        {
                            float tPlane = ray.direction.y < -1e-4f ? (waterY - ray.origin.y) / ray.direction.y : -1f;
                            rep.debug.Add($"px({x},{y}) gpu=({c.g:F1},{c.r:F2},{c.b:F1}) a={c.a:F0} dist={dist:F1} planeT={tPlane:F1} hit={(hitAny ? hit.collider.name + "@" + hit.distance.ToString("F1") : "none")} offRay={Vector3.Cross(ray.direction, p - ray.origin).magnitude:F2}");
                        }
                        if (c.a < 0.5f || Mathf.Abs(c.r - waterY) > 6f) continue;
                        if (hitAny) continue;   // a collider at or before the rendered surface: not water
                        if (!w.WaterHeightAt(new Vector2(p.x, p.z), out var qy)) continue;
                        float f = mask != null ? HarborWaterMask.Sample(mask, new Vector2(p.x, p.z)).x : (w.Harbor.WaterRegionAt(new Vector2(p.x, p.z)) is string id && HarborWaterMask.IsInnerRegion(id) ? 0f : 1f);
                        var rec = (c.r - qy, c.r, qy);
                        buckets["all"].Add(rec);
                        if (f < 0.5f) buckets["inside_harbour"].Add(rec);
                        else if (f > 0.95f)
                        {
                            buckets["open_sea"].Add(rec);
                            float hd = new Vector2(p.x - ray.origin.x, p.z - ray.origin.z).magnitude;
                            buckets[hd < 300f ? "open_sea_0_300m" : hd < 700f ? "open_sea_300_700m" : "open_sea_700m_plus"].Add(rec);
                        }
                    }
                foreach (var kv in buckets)
                {
                    var l = kv.Value; var st = new Stats { zone = kv.Key, samples = l.Count };
                    if (l.Count > 0)
                    {
                        var abs = new List<float>(); double sd = 0, sg = 0, sq = 0, gg = 0, qq = 0;
                        foreach (var (d, g, q) in l) { abs.Add(Mathf.Abs(d)); sd += d; sg += g; sq += q; gg += g * g; qq += q * q; }
                        abs.Sort();
                        st.mean_abs_diff_m = (float)(abs.Count > 0 ? Sum(abs) / abs.Count : 0); st.p95_abs_diff_m = abs[Mathf.Min(abs.Count - 1, (int)(0.95f * abs.Count))];
                        st.mean_diff_m = (float)(sd / l.Count);
                        st.rendered_height_std_m = (float)Math.Sqrt(Math.Max(0, gg / l.Count - (sg / l.Count) * (sg / l.Count)));
                        st.query_height_std_m = (float)Math.Sqrt(Math.Max(0, qq / l.Count - (sq / l.Count) * (sq / l.Count)));
                    }
                    rep.zones.Add(st);
                }
            }
            finally
            {
                cam.targetTexture = prevT; cam.enabled = prevE;
                vol.customPasses.Clear(); Util.SafeDestroy(go); Util.SafeDestroy(mat);
                RTHandles.Release(probeRt); colour.Release(); Util.SafeDestroy(colour);
            }
            return rep;
        }

        static double Sum(List<float> l) { double s = 0; foreach (var v in l) s += v; return s; }
    }
}
