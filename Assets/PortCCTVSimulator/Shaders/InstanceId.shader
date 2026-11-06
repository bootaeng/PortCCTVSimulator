// Instance-ID pass for the render-consistent visible mask (Runtime/GpuInstanceMask.cs). Drawn by a custom pass after post-processing
// into the camera colour buffer, depth-tested (LessEqual, no depth write) against the camera depth buffer of the same frame, so a
// vessel pixel survives only where the vessel is the front-most opaque surface (water, quays, other hulls hide it). Outputs the raw
// instance index in R as a UNorm byte (no exposure, no lighting).
Shader "Hidden/PortCCTV/InstanceId"
{
    Properties { _InstanceId("Instance id", Float) = 1 }
    SubShader
    {
        Tags { "RenderPipeline" = "HDRenderPipeline" }
        Pass
        {
            Name "InstanceId"
            Blend Off
            ZWrite Off
            ZTest LEqual
            Cull Off
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment FragId
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float _InstanceId;
            CBUFFER_END
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/RenderPass/CustomPass/CustomPassRenderersV2.hlsl"
            void GetSurfaceAndBuiltinData(FragInputs f, float3 direction, inout PositionInputs p, out SurfaceData surfaceData, out BuiltinData builtinData)
            {
                ZERO_BUILTIN_INITIALIZE(builtinData);
                ZERO_INITIALIZE(SurfaceData, surfaceData);
                builtinData.opacity = 1;
                surfaceData.color = float3(_InstanceId / 255.0, 0, 0);
            }
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/ShaderPass/ShaderPassForwardUnlit.hlsl"
            float4 FragId(PackedVaryingsToPS input) : SV_Target { return float4(_InstanceId / 255.0, 0, 0, 1); }
            ENDHLSL
        }
    }
}
