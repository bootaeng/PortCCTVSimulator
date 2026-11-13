using System;
using System.Collections.Generic;
using UnityEngine;

namespace PortCCTV
{
    /// <summary>A wet streak left by a running drop: one straight piece of the trail, in SOURCE pixels (top-left origin).</summary>
    [Serializable]
    public class LensTrail
    {
        public float x0, y0, x1, y1;
        /// <summary>Half width of the wet film (pixels) and its wetness 0..1 at (x0, y0) and (x1, y1), linear in between (it
        /// fades after the drop has passed).</summary>
        public float width, wet, wet1;
        /// <summary>Running drop the piece belongs to (pieces of one trail are combined with max, not stacked).</summary>
        public int group;
    }

    /// <summary>
    /// Water on the window in front of the lens at one simulation time (2026-11-12, replaces the static random drops).
    /// Stateless and deterministic: the state is a function of (seed, density, simulation time, image size) only, so the
    /// preview and a capture at the same camera / seed / navigation.sim_time_s show the same drops, and a sequence of
    /// captures shows the same drops moving smoothly.
    ///   * small static drops: impact (0.6 s fade-in), stay, then evaporate (shrink and fade over the last 30 % of their
    ///     life of 45-140 s); each slot starts a new drop at a new hashed position per life cycle;
    ///   * running drops: a drop grows while three satellite drops merge into it (4-10 s), then runs down (accelerating to a
    ///     capped speed, slight sideways meander), absorbs the small drops on its path, stops at a hashed length or leaves the
    ///     image, and leaves a wet streak plus a few beads that fade with a time constant of 6-12 s;
    ///   * snow: melted flakes only (no running drops).
    /// The same alpha function drives the GPU pass (CctvDegrade.shader) and the label statistic
    /// (lens_drop_cover_fraction = share of a vessel's visible pixels where the combined alpha is >= 0.5).
    /// </summary>
    public sealed class LensRainState
    {
        public const string Model = "lens_rain_v2: static drops (impact / evaporation), merging drops that run down with a meander, " +
                                    "wet trails and beads fading with time; stateless function of (seed, density, sim_time_s, image size)";
        public const float TrailOpacity = 0.55f;
        public float time_s, density;
        public int seed;
        public readonly List<LensDrop> drops = new List<LensDrop>();
        public readonly List<LensTrail> trails = new List<LensTrail>();

        public static float SS(float e0, float e1, float x)
        {
            float t = Mathf.Clamp01((x - e0) / (e1 - e0));
            return t * t * (3f - 2f * t);
        }

        /// <summary>Blend weight of one drop at a source pixel (same formula as the shader).</summary>
        public static float DropAlpha(LensDrop d, Vector2 p)
        {
            if (Mathf.Abs(p.x - d.x) >= d.rx || Mathf.Abs(p.y - d.y) >= d.ry) return 0f;
            float ex = (p.x - d.x) / d.rx, ey = (p.y - d.y) / d.ry;
            float len = Mathf.Sqrt(ex * ex + ey * ey);
            return len >= 1f ? 0f : d.opacity * SS(1f, 0.6f, len);
        }

        /// <summary>Blend weight of one trail piece at a source pixel (same formula as the shader).</summary>
        public static float TrailAlpha(LensTrail t, Vector2 p)
        {
            var a = new Vector2(t.x0, t.y0); var b = new Vector2(t.x1, t.y1);
            var ab = b - a; float l2 = ab.sqrMagnitude;
            float u = l2 > 1e-6f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / l2) : 0f;
            float d = (p - (a + ab * u)).magnitude / Mathf.Max(t.width, 0.5f);
            return d >= 1f ? 0f : Mathf.Lerp(t.wet, t.wet1, u) * TrailOpacity * SS(1f, 0.35f, d);
        }

        /// <summary>Combined coverage of the water film at a source pixel: 1 - prod(1 - a), trail pieces of one drop by max.</summary>
        public float CoverAlpha(Vector2 p)
        {
            float keep = 1f;
            int g = int.MinValue; float gmax = 0f;
            foreach (var t in trails)
            {
                if (t.group != g) { keep *= 1f - gmax; g = t.group; gmax = 0f; }
                gmax = Mathf.Max(gmax, TrailAlpha(t, p));
            }
            keep *= 1f - gmax;
            foreach (var d in drops)
            {
                float a = DropAlpha(d, p);
                if (a > 0f) keep *= 1f - a;
            }
            return 1f - keep;
        }

        public bool Covered(Vector2 p) => CoverAlpha(p) >= 0.5f;

        /// <summary>Share of the image (W x H, sampled every `step` pixels) that is covered.</summary>
        public float ImageCoverFraction(int W, int H, int step = 4)
        {
            int n = 0, c = 0;
            for (int y = step / 2; y < H; y += step)
                for (int x = step / 2; x < W; x += step) { n++; if (Covered(new Vector2(x + 0.5f, y + 0.5f))) c++; }
            return n > 0 ? (float)c / n : 0f;
        }
    }

    public static class LensRain
    {
        public const int MaxDrops = 256, MaxTrails = 96, TrailPieces = 6;

        static uint Hash(uint a, uint b, uint c, uint d)
        {
            uint h = a * 0x9E3779B1u ^ (b + 0x7F4A7C15u) * 0x85EBCA77u ^ (c + 0x165667B1u) * 0xC2B2AE3Du ^ (d + 0x27D4EB2Fu) * 0x94D049BBu;
            h ^= h >> 15; h *= 0x2C1B3C6Du; h ^= h >> 12; h *= 0x297A2D39u; h ^= h >> 15;
            return h;
        }
        static float H01(int seed, int slot, int cycle, int k) => (Hash((uint)seed, (uint)slot, (uint)cycle, (uint)k) >> 8) / 16777216f;

        /// <summary>Distance travelled by a running drop after `tau` seconds (start speed v0, acceleration acc, top speed vmax).</summary>
        static float Travel(float tau, float v0, float acc, float vmax)
        {
            if (tau <= 0f) return 0f;
            float tc = (vmax - v0) / acc;                      // time to reach the top speed
            return tau <= tc ? v0 * tau + 0.5f * acc * tau * tau : v0 * tc + 0.5f * acc * tc * tc + vmax * (tau - tc);
        }

        static float TimeFor(float s, float v0, float acc, float vmax)
        {
            float tc = (vmax - v0) / acc, sc = v0 * tc + 0.5f * acc * tc * tc;
            if (s <= sc) return (-v0 + Mathf.Sqrt(v0 * v0 + 2f * acc * s)) / acc;
            return tc + (s - sc) / vmax;
        }

        struct Runner
        {
            public float x0, y0, rFinal, v0, acc, vmax, stopLen, grow, fadeTau, a1, k1, p1, a2, k2, p2, cycleStart;
            public float X(float y) { float s = y - y0; return x0 + a1 * (Mathf.Sin(k1 * s + p1) - Mathf.Sin(p1)) + a2 * (Mathf.Sin(k2 * s + p2) - Mathf.Sin(p2)); }
            /// <summary>Absolute time the drop's head reaches y (NaN if it never does).</summary>
            public float PassTime(float y)
            {
                float s = y - y0; if (s < 0f || s > stopLen) return float.NaN;
                return cycleStart + grow + TimeFor(s, v0, acc, vmax);
            }
        }

        static Runner MakeRunner(int seed, int j, int cyc, float cycleStart, int W, int H)
        {
            float U(int k) => H01(seed + 101, j, cyc, k);
            var r = new Runner
            {
                x0 = Mathf.Lerp(0.03f, 0.97f, U(0)) * W, y0 = Mathf.Lerp(-0.02f, 0.6f, U(1)) * H,
                rFinal = H * Mathf.Lerp(0.024f, 0.04f, U(2)),
                v0 = H * 0.012f, acc = H * Mathf.Lerp(0.02f, 0.05f, U(3)), vmax = H * Mathf.Lerp(0.12f, 0.26f, U(4)),
                stopLen = H * Mathf.Lerp(0.3f, 1.3f, U(5)), grow = Mathf.Lerp(4f, 10f, U(6)), fadeTau = Mathf.Lerp(6f, 12f, U(7)),
                k1 = Mathf.Lerp(2.5f, 6f, U(9)) / H * Mathf.PI * 2f, p1 = U(10) * 6.283f,
                k2 = Mathf.Lerp(9f, 16f, U(12)) / H * Mathf.PI * 2f, p2 = U(13) * 6.283f, cycleStart = cycleStart,
            };
            r.a1 = r.rFinal * Mathf.Lerp(0.3f, 0.9f, U(8)); r.a2 = r.rFinal * Mathf.Lerp(0.1f, 0.3f, U(11));
            return r;
        }

        /// <summary>Cycle of one running-drop slot: long enough for the longest growth (10 s) + the slowest longest run
        /// (1.3 H at the lowest speeds, 13.2 s) + the longest trail fade (3.5 x 12 s), plus a pause that is shorter in heavy rain.</summary>
        static float RunnerPeriod(int seed, int j, float density)
            => 10f + 13.5f + 3.5f * 12f + (8f + 30f * H01(seed + 101, j, -1, 0)) / (0.35f + density);

        /// <summary>The water on the window at simulation time t (seconds) for a W x H source image.</summary>
        public static LensRainState Make(int seed, float density, float t, int W, int H, bool runners = true)
        {
            var st = new LensRainState { seed = seed, density = density, time_s = t };
            if (density <= 0.001f || W <= 0 || H <= 0) return st;
            // ---- running drops (current and previous cycle: the previous one may still be fading / have absorbed drops)
            int nr = runners ? Mathf.Clamp(Mathf.RoundToInt(density * 12f), density > 0.05f ? 1 : 0, 8) : 0;
            var paths = new List<(Runner r, float headS, bool active)>();
            for (int j = 0; j < nr; j++)
            {
                float P = RunnerPeriod(seed, j, density), phase = H01(seed + 101, j, -1, 1) * P;
                float tt = t + phase; int cyc = Mathf.FloorToInt(tt / P);
                for (int back = 1; back >= 0; back--)
                {
                    int c = cyc - back;
                    float cycleStart = c * P - phase;
                    var r = MakeRunner(seed, j, c, cycleStart, W, H);
                    float age = t - cycleStart;
                    // keep the whole run + trail fade inside the cycle: shorten the run if the period is too short
                    float runT = TimeFor(r.stopLen, r.v0, r.acc, r.vmax), tail = 3.5f * r.fadeTau;
                    if (r.grow + runT + tail > P) { r.stopLen = Travel(Mathf.Max(1f, P - r.grow - tail), r.v0, r.acc, r.vmax); runT = TimeFor(r.stopLen, r.v0, r.acc, r.vmax); }
                    float headS = age <= r.grow ? 0f : Mathf.Min(r.stopLen, Travel(age - r.grow, r.v0, r.acc, r.vmax));
                    paths.Add((r, headS, back == 0));
                    if (back == 1) { AddTrail(st, r, headS, t, j * 2 + 1, W, H); continue; }   // previous cycle: only its fading trail
                    AddRunner(st, r, age, headS, runT, t, j, c, seed, W, H);
                    AddTrail(st, r, headS, t, j * 2, W, H);
                }
            }
            // ---- small static drops (absorbed by a running drop that crossed them after they landed)
            int ns = Mathf.Clamp(Mathf.RoundToInt(density * 300f), 0, 220);
            for (int i = 0; i < ns && st.drops.Count < LensRain.MaxDrops; i++)
            {
                float P = Mathf.Lerp(45f, 140f, H01(seed, i, -1, 0)) / (0.6f + density), phase = H01(seed, i, -1, 1) * P;
                float tt = t + phase; int cyc = Mathf.FloorToInt(tt / P); float age = tt - cyc * P;
                float U(int k) => H01(seed, i, cyc, k);
                float x = U(0) * W, y = U(1) * H, r = H * Mathf.Lerp(0.006f, 0.026f, Mathf.Pow(U(2), 1.8f));
                float fadeIn = LensRainState.SS(0f, 0.6f, age), life = 1f - LensRainState.SS(0.7f * P, P, age);
                bool absorbed = false;
                float born = t - age;
                foreach (var (rn, headS, _) in paths)
                {
                    if (y < rn.y0 || y > rn.y0 + headS) continue;
                    if (Mathf.Abs(x - rn.X(y)) > rn.rFinal * 0.9f + r) continue;
                    float tp = rn.PassTime(y);
                    if (!float.IsNaN(tp) && tp >= born && tp <= t) { absorbed = true; break; }
                }
                if (absorbed || fadeIn * life < 0.02f) continue;
                float rr = r * Mathf.Lerp(0.55f, 1f, life);
                st.drops.Add(new LensDrop
                {
                    x = x, y = y, rx = rr * Mathf.Lerp(0.9f, 1.08f, U(3)), ry = rr * Mathf.Lerp(0.98f, 1.18f, U(4)),
                    opacity = Mathf.Lerp(0.75f, 0.95f, U(5)) * fadeIn * life, refraction = Mathf.Lerp(0.55f, 0.9f, U(6)), blur = Mathf.Lerp(0.25f, 0.45f, U(7)),
                    kind = "static", age_s = age,
                });
            }
            return st;
        }

        static void AddRunner(LensRainState st, Runner r, float age, float headS, float runT, float t, int j, int cyc, int seed, int W, int H)
        {
            float U(int k) => H01(seed + 101, j, cyc, 20 + k);
            if (age < 0f) return;
            if (age < r.grow)
            {
                // growing drop with three satellites merging into it
                float g = LensRainState.SS(0f, r.grow, age);
                st.drops.Add(new LensDrop { x = r.x0, y = r.y0, rx = r.rFinal * Mathf.Lerp(0.45f, 1f, g), ry = r.rFinal * Mathf.Lerp(0.45f, 1.05f, g), opacity = 0.92f * LensRainState.SS(0f, 0.6f, age), refraction = 0.8f, blur = 0.4f, kind = "merging", age_s = age });
                for (int k = 0; k < 3; k++)
                {
                    float m = r.grow * Mathf.Lerp(0.25f, 0.95f, U(k)), ang = U(3 + k) * 6.283f, dist = r.rFinal * Mathf.Lerp(1.8f, 3.2f, U(6 + k));
                    if (age >= m) continue;
                    float approach = LensRainState.SS(m - 1.5f, m, age);   // slides into the main drop in the last 1.5 s
                    float rs = r.rFinal * Mathf.Lerp(0.3f, 0.5f, U(9 + k));
                    var o = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * dist * (1f - approach);
                    st.drops.Add(new LensDrop { x = r.x0 + o.x, y = r.y0 + o.y, rx = rs, ry = rs * 1.05f, opacity = 0.88f * LensRainState.SS(0f, 0.6f, age), refraction = 0.75f, blur = 0.35f, kind = "merging", age_s = age });
                }
                return;
            }
            float tau = age - r.grow;
            float y = r.y0 + headS, x = r.X(y);
            bool moving = tau < runT;
            float v = moving ? Mathf.Min(r.vmax, r.v0 + r.acc * tau) : 0f;
            // after stopping the drop stays and evaporates with the trail
            float life = moving ? 1f : 1f - LensRainState.SS(0f, 3f * r.fadeTau, tau - runT);   // gone before the cycle ends
            if (y - r.rFinal * 1.6f < H && life > 0.02f)
                st.drops.Add(new LensDrop
                {
                    x = x, y = y, rx = r.rFinal * 0.82f, ry = r.rFinal * (1.15f + 0.45f * v / r.vmax) * Mathf.Lerp(0.6f, 1f, life),
                    opacity = 0.93f * life, refraction = 0.85f, blur = 0.42f, kind = moving ? "running" : "stopped", age_s = age,
                });
            // beads left behind on the trail
            for (int k = 0; k < 3; k++)
            {
                float sb = r.stopLen * Mathf.Lerp(0.1f, 0.9f, U(12 + k));
                if (sb > headS - r.rFinal * 2f) continue;
                float tp = r.PassTime(r.y0 + sb);
                float bl = float.IsNaN(tp) ? 0f : 1f - LensRainState.SS(0f, 3.2f * r.fadeTau, t - tp);
                if (float.IsNaN(tp) || bl < 0.03f || r.y0 + sb > H + 4f) continue;
                float rb = r.rFinal * Mathf.Lerp(0.18f, 0.3f, U(15 + k));
                st.drops.Add(new LensDrop { x = r.X(r.y0 + sb), y = r.y0 + sb, rx = rb, ry = rb * 1.1f, opacity = 0.85f * bl, refraction = 0.7f, blur = 0.3f, kind = "bead", age_s = t - tp });
            }
        }

        static void AddTrail(LensRainState st, Runner r, float headS, float t, int group, int W, int H)
        {
            if (headS < r.rFinal * 0.5f || st.trails.Count + TrailPieces > MaxTrails) return;
            float sEnd = headS - r.rFinal * 0.6f;
            if (sEnd <= 0f) return;
            for (int k = 0; k < TrailPieces; k++)
            {
                float s0 = sEnd * k / TrailPieces, s1 = sEnd * (k + 1) / TrailPieces;
                float Wet(float s) { float tp = r.PassTime(r.y0 + s); return float.IsNaN(tp) ? 0f : Mathf.Exp(-Mathf.Max(0f, t - tp) / r.fadeTau) * (1f - LensRainState.SS(2.5f * r.fadeTau, 3.4f * r.fadeTau, t - tp)); }
                float w0 = Wet(s0), w1 = Wet(s1);
                if (Mathf.Max(w0, w1) < 0.02f || r.y0 + s0 > H + 4f) continue;
                st.trails.Add(new LensTrail { x0 = r.X(r.y0 + s0), y0 = r.y0 + s0, x1 = r.X(r.y0 + s1), y1 = r.y0 + s1, width = r.rFinal * 0.5f, wet = w0, wet1 = w1, group = group });
            }
        }
    }
}
