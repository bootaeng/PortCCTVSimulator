using System;
using System.Collections.Generic;
using UnityEngine;

namespace PortCCTV
{
    /// <summary>
    /// HDRP water mask of a harbour (2026-11-12): attenuates the swell (R), agitation (G) and ripple (B) bands inside the
    /// breakwaters and lets the waves grow smoothly from the harbour entrance out to the open sea.
    /// Built from the map's water regions on a 1024 x 1024 grid over 4096 m x 4096 m centred on the world origin (mask offset 0:
    /// HDRP 16 maps the mask offset with opposite z signs on the GPU and in the CPU height query, so a zero offset keeps the
    /// rendered surface and the vessels' height query on the same mask). Water cells = inside any water region; the inner
    /// regions (id contains "inner", "basin" or "marina") are the seeds; the geodesic distance through water cells from the
    /// seeds (around breakwaters, 8-neighbour chamfer) sets the attenuation f = smoothstep(0, RampM, d):
    /// mask = lerp(inner value, 1, f). Land cells take the value of the nearest water cell; one 5 x 5 box blur smooths the seams.
    /// Outside the 4 km square the mask is 1 (clamp).
    /// </summary>
    public static class HarborWaterMask
    {
        public const int N = 1024;
        public const float ExtentM = 4096f, RampM = 160f;   // ramp ends ~60-100 m outside the entrance mouth (320 m also damped the seaward side of the breakwaters)
        public static readonly Vector3 Inner = new Vector3(0.18f, 0.4f, 0.85f);   // swell, agitation, ripples inside the harbour
        static readonly Dictionary<string, Texture2D> s_Cache = new Dictionary<string, Texture2D>();

        public static bool IsInnerRegion(string id) => id != null && (id.Contains("inner") || id.Contains("basin") || id.Contains("marina"));

        /// <summary>World xz of the centre of mask cell (i, j) (row j = world z, bottom row first).</summary>
        public static Vector2 CellCentre(int i, int j) => new Vector2((i + 0.5f) / N * ExtentM - ExtentM / 2f, (j + 0.5f) / N * ExtentM - ExtentM / 2f);

        public static Texture2D Get(HarborDefinition h)
        {
            if (h == null) return null;
            string key = h.harbor_id + "|" + h.ComputeHash();
            if (s_Cache.TryGetValue(key, out var t) && t != null) return t;
            t = Build(h, out _);
            s_Cache[key] = t;
            return t;
        }

        /// <summary>Attenuation factor f (0 inside the harbour, 1 open sea) per cell, for statistics.</summary>
        public static Texture2D Build(HarborDefinition h, out float[] factor)
        {
            int n = N;
            var water = new bool[n * n];
            var seed = new bool[n * n];
            var inner = new List<WaterRegion>(); var all = h.water_regions;
            foreach (var r in all) if (IsInnerRegion(r.id)) inner.Add(r);
            for (int j = 0; j < n; j++)
                for (int i = 0; i < n; i++)
                {
                    var p = CellCentre(i, j);
                    bool w = false, s = false;
                    foreach (var r in all) if (r.Contains(p)) { w = true; if (IsInnerRegion(r.id)) s = true; break; }
                    water[j * n + i] = w; seed[j * n + i] = s;
                }
            // Dial's algorithm (integer chamfer costs 10 / 14 per cell) over water cells, capped past the ramp
            float cell = ExtentM / n;
            int cap = Mathf.CeilToInt(RampM / cell * 10f) + 20;
            var dist = new int[n * n];
            for (int k = 0; k < dist.Length; k++) dist[k] = int.MaxValue;
            var buckets = new List<int>[cap + 15];
            for (int b = 0; b < buckets.Length; b++) buckets[b] = new List<int>();
            for (int k = 0; k < seed.Length; k++) if (seed[k]) { dist[k] = 0; buckets[0].Add(k); }
            int[] dx = { 1, -1, 0, 0, 1, 1, -1, -1 }, dy = { 0, 0, 1, -1, 1, -1, 1, -1 }, dc = { 10, 10, 10, 10, 14, 14, 14, 14 };
            for (int b = 0; b < buckets.Length; b++)
            {
                var list = buckets[b];
                for (int q = 0; q < list.Count; q++)
                {
                    int k = list[q]; if (dist[k] != b) continue;
                    int x = k % n, y = k / n;
                    for (int d = 0; d < 8; d++)
                    {
                        int xx = x + dx[d], yy = y + dy[d];
                        if (xx < 0 || yy < 0 || xx >= n || yy >= n) continue;
                        int kk = yy * n + xx; if (!water[kk]) continue;
                        int nd = b + dc[d]; if (nd >= buckets.Length || nd >= dist[kk]) continue;
                        dist[kk] = nd; buckets[nd].Add(kk);
                    }
                }
                list.Clear();
            }
            factor = new float[n * n];
            var val = new Color[n * n];
            var filled = new bool[n * n];
            var queue = new Queue<int>();
            for (int k = 0; k < factor.Length; k++)
            {
                if (!water[k]) continue;
                float dm = dist[k] == int.MaxValue ? float.MaxValue : dist[k] / 10f * cell;
                float f = dm >= RampM ? 1f : LensRainState.SS(0f, RampM, dm);
                factor[k] = f;
                val[k] = new Color(Mathf.Lerp(Inner.x, 1f, f), Mathf.Lerp(Inner.y, 1f, f), Mathf.Lerp(Inner.z, 1f, f), 1f);
                filled[k] = true; queue.Enqueue(k);
            }
            // land: value of the nearest water cell (4-neighbour flood), so bilinear sampling at the shore stays consistent
            while (queue.Count > 0)
            {
                int k = queue.Dequeue(); int x = k % n, y = k / n;
                for (int d = 0; d < 4; d++)
                {
                    int xx = x + dx[d], yy = y + dy[d];
                    if (xx < 0 || yy < 0 || xx >= n || yy >= n) continue;
                    int kk = yy * n + xx; if (filled[kk]) continue;
                    filled[kk] = true; val[kk] = val[k]; factor[kk] = factor[k]; queue.Enqueue(kk);
                }
            }
            for (int k = 0; k < val.Length; k++) if (!filled[k]) { val[k] = Color.white; factor[k] = 1f; }
            // 5 x 5 box blur (separable)
            var tmp = new Color[n * n];
            for (int pass = 0; pass < 2; pass++)
            {
                var src = pass == 0 ? val : tmp; var dst = pass == 0 ? tmp : val;
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        Color acc = default; int c = 0;
                        for (int o = -2; o <= 2; o++)
                        {
                            int xx = pass == 0 ? Mathf.Clamp(x + o, 0, n - 1) : x, yy = pass == 0 ? y : Mathf.Clamp(y + o, 0, n - 1);
                            acc += src[yy * n + xx]; c++;
                        }
                        dst[y * n + x] = acc / c;
                    }
            }
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false, true)
            {
                name = "PortCCTV_WaterMask_" + h.harbor_id, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.DontSave,
            };
            tex.SetPixels(val); tex.Apply(false, false);
            return tex;
        }

        /// <summary>Mask value (swell, agitation, ripples) at world xz, bilinear like HDRP (for metadata / checks).</summary>
        public static Vector3 Sample(Texture2D t, Vector2 xz)
        {
            float u = xz.x / ExtentM + 0.5f, v = xz.y / ExtentM + 0.5f;
            var c = t.GetPixelBilinear(u, v);
            return new Vector3(c.r, c.g, c.b);
        }
    }
}
