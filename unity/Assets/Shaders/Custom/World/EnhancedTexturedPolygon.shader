// ClassicTexturedPolygon.shader for Enhanced mode: the same pass (TexturedPolygonPass.hlsl) compiled with BAK_ENHANCED.
// Properties and render state stay in step with ClassicTexturedPolygon (WorldShaders swaps one for the other
// on live materials); the comments explaining them are there.
Shader "BakAgain/EnhancedTexturedPolygon" {
    Properties {
        _MainTex ("Slot Bitmap", 2D) = "white" {}
        _AlphaCutoff ("Alpha Cutoff", Float) = 0.01
        _OffsetFactor ("Depth Offset Factor", Float) = 0
        _OffsetUnits ("Depth Offset Units", Float) = 0
        _PaintBias ("Paint Bias", Float) = 0
    }
    SubShader {
        Tags { "RenderType"="TransparentCutout" "RenderPipeline"="UniversalPipeline" "Queue"="AlphaTest" }
        Pass {
            Name "EnhancedTexturedPolygon"
            Tags { "LightMode"="UniversalForward" }
            Cull Back
            Offset [_OffsetFactor], [_OffsetUnits]

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #define BAK_ENHANCED
            #include "Assets/Shaders/Custom/World/TexturedPolygonPass.hlsl"
            ENDHLSL
        }
        // The sun's shadow map (TASK-882): the same pass file's shadowVert/shadowFrag.
        Pass {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Off

            HLSLPROGRAM
            #pragma vertex shadowVert
            #pragma fragment shadowFrag
            #define BAK_ENHANCED
            #include "Assets/Shaders/Custom/World/TexturedPolygonPass.hlsl"
            ENDHLSL
        }
    }
}
