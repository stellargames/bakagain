// ClassicPolygon.shader for Enhanced mode: the same pass (PolygonPass.hlsl) compiled with BAK_ENHANCED.
// Properties and render state stay in step with ClassicPolygon (WorldShaders swaps one for the other
// on live materials); the comments explaining them are there.
Shader "BakAgain/EnhancedPolygon" {
    Properties {
        _OffsetFactor ("Depth Offset Factor", Float) = 0
        _OffsetUnits ("Depth Offset Units", Float) = 0
        _PaintBias ("Paint Bias", Float) = 0
        _LayerLift ("Layer Lift", Float) = 0
    }
    SubShader {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        Pass {
            Name "EnhancedPolygon"
            Tags { "LightMode"="UniversalForward" }
            Cull Back
            Offset [_OffsetFactor], [_OffsetUnits]

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #define BAK_ENHANCED
            #include "Assets/Shaders/Custom/World/PolygonPass.hlsl"
            ENDHLSL
        }
    }
}
