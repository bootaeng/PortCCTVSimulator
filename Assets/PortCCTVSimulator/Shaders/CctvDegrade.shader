// CCTV image degradation (Runtime/SensorModel.cs). Full-screen pass from the rendered frame (sRGB render texture, sampled as
// linear) into an 8-bit UNorm target holding sRGB-encoded values. Order follows the optical path:
//   radial lens distortion (inverse map: output -> source) -> lens blur (Gaussian) and camera shake (linear motion)
//   -> water on the window in front of the lens (Runtime/LensRain.cs, 2026-11-12): wet trails of running drops, then drops
//      (spherical-cap surface: refraction offset and blur grow with the surface slope, darker steep rim, small sky highlight;
//      bright lights behind the water spread through it -- drops: disc halo, trails: streak along the trail)
//   -> vignetting -> sensor noise (read + shot, x gain, linear DN)
//   -> optional monochrome (IR night mode) -> sRGB encode. JPEG compression runs afterwards on the CPU.
// Drops and noise are deterministic (seeded hashes); identical inputs give identical outputs on the same GPU/driver.
Shader "Hidden/PortCCTV/CctvDegrade"
{
    Properties { _MainTex("Source", 2D) = "black" {} }
    SubShader
    {
        Tags { "RenderPipeline" = "HDRenderPipeline" }
        Pass
        {
            Name "CctvDegrade"
            ZTest Always ZWrite Off Cull Off Blend Off
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"

            TEXTURE2D(_MainTex);
            TEXTURE2D(_IrTex);     // IR illuminator component (linear, same view; IrIlluminator.cs), black when off
            SAMPLER(s_linear_clamp_sampler);

            float4 _Size;          // W, H, 1/W, 1/H (output == source size)
            float _K1, _K2, _DistScale;
            float _BlurSigma;      // output pixels
            float _MotionLen;      // output pixels
            float2 _MotionDir;     // unit, uv space orientation
            float _NoiseRead, _NoiseShot, _NoiseChroma, _Gain;   // read noise in linear DN (0..255), shot coefficient (var = k * DN)
            float _Vignette, _Mono, _OutputLinear;
            int _SeedI;
            int _DropCount;
            float4 _Drops[256];    // centre uv (x, y), radii in pixels (rx, ry)
            float4 _DropP[256];    // opacity, refraction, blur fraction, -
            int _TrailCount;
            float4 _Trails[96];    // trail piece: uv0 (x, y), uv1 (x, y)
            float4 _TrailP[96];    // half width (pixels), wetness at uv0, wetness at uv1, group (running drop)
            float _TrailOpacity, _Glow, _DebugCover, _UvYDown;
            float _IrOn, _IrDome;  // IR component on; strength of the camera's own IR reflected by water on a dome window
            #define UV_Y_DOWN_SIGN (_UvYDown > 0.5 ? 1.0 : -1.0)

            struct V2F { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            V2F Vert(uint id : SV_VertexID)
            {
                V2F o;
                o.pos = GetFullScreenTriangleVertexPosition(id);
                o.uv = GetFullScreenTriangleTexCoord(id);
                return o;
            }

            uint Pcg(uint v)
            {
                uint s = v * 747796405u + 2891336453u;
                uint w = ((s >> ((s >> 28u) + 4u)) ^ s) * 277803737u;
                return (w >> 22u) ^ w;
            }
            float Rnd(uint2 p, uint k) { return (Pcg(p.x + Pcg(p.y + Pcg((uint)_SeedI * 8u + k))) + 0.5) / 4294967296.0; }
            float Gauss(uint2 p, uint k)
            {
                float u1 = max(Rnd(p, k), 1e-7), u2 = Rnd(p, k + 1u);
                return sqrt(-2.0 * log(u1)) * cos(6.2831853 * u2);
            }

            // scene light reaching the sensor: visible render + IR illuminator component (an IR-mode camera without IR-cut filter)
            float3 Src(float2 uv)
            {
                float3 c = SAMPLE_TEXTURE2D_LOD(_MainTex, s_linear_clamp_sampler, uv, 0).rgb;
                if (_IrOn > 0.5) c += max(SAMPLE_TEXTURE2D_LOD(_IrTex, s_linear_clamp_sampler, uv, 0).rgb, 0.0);
                return c;
            }

            // Gaussian lens blur: golden-angle spiral of 24 taps within 2.5 sigma, Gaussian weights.
            float3 Blurred(float2 uv, float sigmaPx)
            {
                if (sigmaPx < 0.05) return Src(uv);
                float3 acc = Src(uv); float wsum = 1.0;
                float R = 2.5 * sigmaPx;
                [loop] for (int i = 1; i <= 24; i++)
                {
                    float r = R * sqrt(i / 24.0);
                    float a = i * 2.39996323;
                    float2 o = float2(cos(a), sin(a)) * r;
                    float w = exp(-0.5 * dot(o, o) / (sigmaPx * sigmaPx));
                    acc += Src(uv + o * _Size.zw) * w; wsum += w;
                }
                return acc / wsum;
            }

            float3 LensBlurAndShake(float2 uv)
            {
                if (_MotionLen < 0.5) return Blurred(uv, _BlurSigma);
                float3 acc = 0;
                [loop] for (int i = 0; i < 9; i++)
                {
                    float t = i / 8.0 - 0.5;
                    acc += Blurred(uv + _MotionDir * t * _MotionLen * _Size.zw, _BlurSigma);
                }
                return acc / 9.0;
            }

            // light above a threshold around uv (disc of radius R pixels, 12 taps): what a bright source behind the water spreads
            float3 BrightDisc(float2 uv, float R, float thr)
            {
                float3 acc = 0;
                [loop] for (int k = 0; k < 12; k++)
                {
                    float r = R * sqrt((k + 0.5) / 12.0), a = k * 2.39996323;
                    float3 v = Src(uv + float2(cos(a), sin(a)) * r * _Size.zw);
                    acc += max(v - thr, 0.0);
                }
                return acc / (12.0 * (1.0 - thr));
            }
            // bright light along a line (a wet trail smears lights along its length)
            float3 BrightLine(float2 uv, float2 dirPx, float thr)
            {
                float3 acc = 0;
                [loop] for (int k = 0; k < 9; k++) acc += max(Src(uv + dirPx * (k / 8.0 - 0.5) * _Size.zw) - thr, 0.0);
                return acc / (9.0 * (1.0 - thr));
            }

            float DropAlpha(int d, float2 uvs, out float2 e, out float len)
            {
                e = (uvs - _Drops[d].xy) * _Size.xy / _Drops[d].zw;
                len = length(e);
                return len >= 1.0 ? 0.0 : _DropP[d].x * smoothstep(1.0, 0.6, len);
            }

            float TrailAlpha(int t, float2 uvs, out float u, out float dn, out float2 dirPx)
            {
                float2 p = uvs * _Size.xy, a = _Trails[t].xy * _Size.xy, b = _Trails[t].zw * _Size.xy;
                float2 ab = b - a; float l2 = dot(ab, ab);
                u = l2 > 1e-6 ? saturate(dot(p - a, ab) / l2) : 0.0;
                float2 off = p - (a + ab * u);
                dirPx = l2 > 1e-6 ? ab / sqrt(l2) : float2(0, 1);
                float w = max(_TrailP[t].x, 0.5);
                dn = dot(off, float2(-dirPx.y, dirPx.x)) / w;           // signed distance across the film, -1..1
                float d = length(off) / w;
                return d >= 1.0 ? 0.0 : lerp(_TrailP[t].y, _TrailP[t].z, u) * _TrailOpacity * smoothstep(1.0, 0.35, d);
            }

            float3 Frag(V2F i) : SV_Target
            {
                float2 uv = i.uv;
                uint2 pix = (uint2)min(floor(uv * _Size.xy), _Size.xy - 1);
                // radial distortion, normalised so r = 1 at the image corner
                float halfDiag = 0.5 * sqrt(dot(_Size.xy, _Size.xy));
                float2 q = (uv - 0.5) * _Size.xy / halfDiag;
                float r2 = dot(q, q);
                float f = _DistScale * (1.0 + _K1 * r2 + _K2 * r2 * r2);
                float2 uvs = 0.5 + (uv - 0.5) * f;
                bool inside = all(uvs >= 0.0) && all(uvs <= 1.0);

                float3 c = inside ? LensBlurAndShake(uvs) : float3(0, 0, 0);

                // ---- water on the window in front of the lens (source image space, out of focus)
                float keep = 1.0;                                            // 1 - combined alpha (coverage, debug output)
                float glowThr = lerp(0.9, 0.6, saturate(_Glow - 0.35));       // brighter-than threshold for light spread
                // wet trails: a thin film -- small sideways refraction, slight blur, lights smeared along the trail; pieces of
                // one running drop combine with max (no darker joints)
                float gA = 0.0; float3 gC = c; float gId = -1.0;
                [loop] for (int t = 0; t < _TrailCount; t++)
                {
                    if (_TrailP[t].w != gId) { c = lerp(c, gC, gA); keep *= 1.0 - gA; gA = 0.0; gC = c; gId = _TrailP[t].w; }
                    float u, dn; float2 dirPx;
                    float ta = TrailAlpha(t, uvs, u, dn, dirPx);
                    if (ta <= gA) continue;
                    float w = _TrailP[t].x;
                    float2 side = float2(-dirPx.y, dirPx.x);
                    float slope = abs(dn) / max(sqrt(saturate(1.0 - dn * dn)), 0.25);    // film edge is steeper
                    float2 uvt = uvs - side * dn * w * (0.35 + 0.25 * slope) * _Size.zw;
                    float3 tc = Blurred(uvt, 0.8 + 0.12 * w * (1.0 + slope));
                    tc += _Glow * 0.8 * BrightLine(uvt, dirPx * w * 7.0, glowThr);
                    tc += 0.45 * _IrDome * (1.0 - 0.5 * r2) * (1.0 - abs(dn));          // own IR scattered back by the wet film
                    gA = ta; gC = tc;
                }
                c = lerp(c, gC, gA); keep *= 1.0 - gA;
                // drops: spherical cap h = sqrt(1 - |e|^2); the slope |e| / h sets the refraction offset and the blur
                [loop] for (int d = 0; d < _DropCount; d++)
                {
                    float2 e; float len;
                    float a = DropAlpha(d, uvs, e, len);
                    if (a <= 0.0) continue;
                    float rpx = min(_Drops[d].z, _Drops[d].w);
                    float h = sqrt(saturate(1.0 - len * len));
                    float slope = len / max(h, 0.2);                           // tan of the surface angle, 0 .. 4.9
                    // the drop is a short-focus lens: an inverted, minified view of a field several drop radii wide around it;
                    // steeper surface -> stronger deflection and a softer image
                    float k = 4.0 * _DropP[d].y * (1.0 + 0.35 * slope);
                    float2 uvr = _Drops[d].xy - e * _Drops[d].zw * _Size.zw * k;
                    float3 dc = Blurred(uvr, max(1.0, rpx * _DropP[d].z * (0.55 + 0.3 * slope)));
                    dc *= 1.0 - 0.45 * smoothstep(1.2, 4.0, slope);            // steep rim: total internal reflection, darker
                    dc += 0.10 * smoothstep(0.3, 0.0, length(e - float2(-0.35, 0.4)));   // small sky highlight (upper left)
                    float down = UV_Y_DOWN_SIGN * e.y / max(len, 1e-3);                    // +1 at the drop's lower edge
                    dc += 0.07 * smoothstep(0.55, 0.92, len) * saturate(down);             // light focused into a lower crescent
                    dc += _Glow * BrightDisc(uvr, rpx * (0.6 + 0.6 * _DropP[d].y), glowThr);  // lights spread over the drop
                    // dome camera with built-in IR: the drop reflects the illuminator back into the lens -> milky glow, brightest
                    // in the drop's thick centre, stronger near the image centre (LED ring around the lens)
                    dc += _IrDome * (0.3 + 0.7 * h) * (1.0 - 0.5 * r2);
                    c = lerp(c, dc, a);
                    keep *= 1.0 - a;
                }
                if (_DebugCover > 0.5) return (1.0 - keep).xxx;           // coverage test: combined alpha, unencoded

                c *= saturate(1.0 - _Vignette * r2);

                if (_Mono > 0.5) c = dot(c, float3(0.2126, 0.7152, 0.0722)).xxx;

                // sensor noise in linear DN: var = read^2 + shot * DN, scaled by the gain
                float3 dn = c * 255.0;
                float3 sigma = _Gain * sqrt(_NoiseRead * _NoiseRead + _NoiseShot * max(dn, 0));
                float nl = Gauss(pix, 0u);
                float3 nc = float3(Gauss(pix, 2u), Gauss(pix, 4u), Gauss(pix, 6u));
                float3 n = _Mono > 0.5 ? nl.xxx : lerp(nl.xxx, nc, _NoiseChroma);
                dn += n * sigma;
                c = saturate(dn / 255.0);

                return _OutputLinear > 0.5 ? c : LinearToSRGB(c);   // linear when the target is an sRGB texture (preview)
            }
            ENDHLSL
        }
    }
}
