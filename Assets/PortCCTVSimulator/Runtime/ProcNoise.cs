using UnityEngine;

namespace PortCCTV
{
    /// <summary>Deterministic noise shared by the Editor generators and the runtime map builders (hill peak heights).</summary>
    public static class ProcNoise
    {
        public static float Hash(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + seed * 2147483647);
                h = (h ^ (h >> 13)) * 1274126177u;
                return ((h ^ (h >> 16)) & 0xFFFFFF) / (float)0xFFFFFF;
            }
        }

        /// <summary>Periodic value noise (period = cells), smooth interpolation.</summary>
        public static float VNoise(float u, float v, int cells, int seed)
        {
            float x = u * cells, y = v * cells;
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = x - x0, fy = y - y0;
            fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
            int W(int a) => ((a % cells) + cells) % cells;
            float a00 = Hash(W(x0), W(y0), seed), a10 = Hash(W(x0 + 1), W(y0), seed);
            float a01 = Hash(W(x0), W(y0 + 1), seed), a11 = Hash(W(x0 + 1), W(y0 + 1), seed);
            return Mathf.Lerp(Mathf.Lerp(a00, a10, fx), Mathf.Lerp(a01, a11, fx), fy);
        }

        public static float Fbm(float u, float v, int baseCells, int octaves, int seed)
        {
            float s = 0, a = 0.5f, n = 0;
            for (int o = 0; o < octaves; o++) { s += a * VNoise(u, v, baseCells << o, seed + o * 31); n += a; a *= 0.5f; }
            return s / n;
        }

        /// <summary>Unit hill height at (u,w) in [0,1]^2 for hill prefab index i (seed 2000+i). Irregular outline (warped radius),
        /// broad lumps + sharp ridges, fine gullies; slightly concave slopes (pow 1.15). Shared by the Editor mesh/texture bake and
        /// the runtime camera-hill placement.</summary>
        public static float HillHeight(int hillIndex, float u, float w, bool ridged = false) => HillShape(hillIndex, u, w, out _, ridged);

        /// <summary>
        /// ridged = true (background / headland hills, prefabs hill_N and hill_L_N; 2026-11-09 detail review, reference R07): the
        /// radial dome becomes a ridge — distance is measured to a gently curved ridge line along a per-index axis, the two
        /// flanks have different steepness, and the crest rises and falls along the ridge (saddles), so neighbouring hills read
        /// as a connected range with valleys rather than repeated cones. Camera hills (hill_m_N) keep the radial form
        /// (ridged = false) because their camera stands on a known flank.
        /// </summary>
        public static float HillShape(int hillIndex, float u, float w, out float warpedRadius, bool ridged = false)
        {
            int seed = 2000 + hillIndex; float ridge = hillIndex % 2 == 0 ? 0.45f : 0.65f;
            float dx = u - 0.5f, dz = w - 0.5f;
            float r = Mathf.Sqrt(dx * dx + dz * dz) * 2f;
            float crest = 1f;
            if (ridged)
            {
                float th = (37f + 71f * hillIndex) * Mathf.Deg2Rad, half = 0.20f + 0.05f * (hillIndex % 3);
                float along = dx * Mathf.Cos(th) + dz * Mathf.Sin(th), across = -dx * Mathf.Sin(th) + dz * Mathf.Cos(th);
                across -= 0.10f * (VNoise(along + 0.5f, 0.37f * hillIndex, 3, seed + 3) - 0.5f);   // the ridge line meanders
                float ea = Mathf.Max(Mathf.Abs(along) - half, 0f) / (0.5f - half);
                float steep = across > 0f ? 1.0f : 1.45f;                                          // asymmetric flanks
                float ec = across * steep / 0.5f;
                // ridge distance inside; near the mesh rim the radial distance takes over so the hill still reaches 0 at the edge
                r = Mathf.Max(Mathf.Sqrt(ea * ea + ec * ec), (r - 0.8f) / 0.2f);
                crest = 0.62f + 0.38f * Fbm(along + 0.5f, 0.21f * hillIndex + 0.5f, 3, 3, seed + 5);  // summits and saddles along the crest
            }
            float warp = (Fbm(u, w, 2, 3, seed + 19) - 0.5f) * 0.5f;       // bays and headlands
            float rw = r * (1f + warp);
            warpedRadius = rw;
            float fall = Mathf.Clamp01(1f - rw);
            fall = Mathf.Pow(fall * fall * (3 - 2 * fall), 0.75f);           // steeper flanks near the coast
            float n = Fbm(u, w, 3, 6, seed);
            float rid = 1f - Mathf.Abs(Fbm(u, w, 2, 5, seed + 7) * 2f - 1f); rid *= rid;
            float fine = 1f - Mathf.Abs(Fbm(u, w, 9, 3, seed + 13) * 2f - 1f);   // small ridges/gullies
            float mix = (1f - ridge) * n + ridge * rid;
            float amp = 0.15f + 0.85f * Mathf.Pow(mix, 1.3f);                   // separate peaks/spurs instead of one dome
            float h = fall * amp * (0.88f + 0.12f * fine) * crest;
            return Mathf.Pow(Mathf.Max(h, 0f), 1.15f);
        }

        public const int HillGrid = 128;

        /// <summary>Height of the hill prefab's centre vertex (grid N=HillGrid, centre vertex) in unit space.</summary>
        public static float HillPeakUnit(int hillIndex) => HillHeight(hillIndex, 0.5f, 0.5f);
    }
}
