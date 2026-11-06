// CCTV image degradation (Runtime/SensorModel.cs). Full-screen pass from the rendered frame (sRGB render texture, sampled as
// linear) into an 8-bit UNorm target holding sRGB-encoded values. Order follows the optical path:
//   radial lens distortion (inverse map: output -> source) -> lens / dome-window water drops (out of focus, refracting)
//   -> lens blur (Gaussian) and camera shake (linear motion) -> vignetting -> sensor noise (read + shot, x gain, linear DN)
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
            float4 _Drops[64];     // centre uv (x, y), radii in pixels (rx, ry)
            float4 _DropP[64];     // opacity, refraction, blur fraction, -

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

            float3 Src(float2 uv) { return SAMPLE_TEXTURE2D_LOD(_MainTex, s_linear_clamp_sampler, uv, 0).rgb; }

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

                // water drops on the window in front of the lens (source image space): out of focus, each a small refracting lens
                [loop] for (int d = 0; d < _DropCount; d++)
                {
                    float2 e = (uvs - _Drops[d].xy) * _Size.xy / _Drops[d].zw;
                    float len = length(e);
                    if (len >= 1.0) continue;
                    float rpx = min(_Drops[d].z, _Drops[d].w);
                    // refraction: the drop shows a magnified, inverted and very soft view of the scene around it
                    float2 uvr = _Drops[d].xy - e * _Drops[d].zw * _Size.zw * _DropP[d].y;
                    float3 dc = Blurred(uvr, max(1.0, rpx * _DropP[d].z));
                    dc *= 1.0 - 0.35 * smoothstep(0.55, 1.0, len);                    // darker rim
                    dc += 0.06 * smoothstep(0.35, 0.0, length(e - float2(-0.35, 0.4)));  // small highlight
                    float a = _DropP[d].x * smoothstep(1.0, 0.6, len);                 // soft, out-of-focus edge
                    c = lerp(c, dc, a);
                }

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
