// Diagnostic (Runtime/WaterProbe.cs): writes the absolute world position (y, x, z, 1) of the front-most rendered surface (camera
// depth-stencil buffer incl. the HDRP water surface) into a float target; (-9999, 0, 0, 0) where nothing was drawn. Full-screen custom pass.
Shader "Hidden/PortCCTV/WorldHeightProbe"
{
    HLSLINCLUDE
    #pragma vertex Vert
    #pragma target 4.5
    #pragma only_renderers d3d11 vulkan metal
    #include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/RenderPass/CustomPass/CustomPassCommon.hlsl"

    // the live camera depth-stencil buffer (ctx.cameraDepthBuffer): unlike the depth pyramid (_CameraDepthTexture, copied
    // after the opaque pass) it holds the HDRP water surface, which writes depth in its own GBuffer pass
    TEXTURE2D_X(_ProbeDepth);

    float4 Probe(Varyings varyings) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(varyings);
        float depth = LOAD_TEXTURE2D_X(_ProbeDepth, uint2(varyings.positionCS.xy)).r;
        if (depth <= 0.0) return float4(-9999.0, 0, 0, 0);              // reversed Z: 0 = far plane, nothing drawn
        PositionInputs pi = GetPositionInput(varyings.positionCS.xy, _ScreenSize.zw, depth, UNITY_MATRIX_I_VP, UNITY_MATRIX_V);
        float3 ws = GetAbsolutePositionWS(pi.positionWS);
        return float4(ws.y, ws.x, ws.z, 1.0);
    }
    ENDHLSL

    SubShader
    {
        Tags { "RenderPipeline" = "HDRenderPipeline" }
        Pass
        {
            Name "Probe"
            ZWrite Off ZTest Always Blend Off Cull Off
            HLSLPROGRAM
            #pragma fragment Probe
            ENDHLSL
        }
    }
    Fallback Off
}
