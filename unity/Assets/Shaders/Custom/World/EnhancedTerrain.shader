// ClassicTerrain.shader for Enhanced mode: the same pass (TerrainPass.hlsl) compiled with BAK_ENHANCED.
// Properties and render state stay in step with ClassicTerrain (WorldShaders swaps one for the other
// on live materials); the comments explaining them are there.
Shader "BakAgain/EnhancedTerrain" {
    Properties {
        _MainTex ("Terrain Texture", 2D) = "white" {}
        _TexScale ("Texture Scale (world units per tile)", Float) = 10
        _ColorBlend ("Vertex Color Blend", Range(0, 1)) = 0.3
        _OffsetFactor ("Depth Offset Factor", Float) = 0
        _OffsetUnits ("Depth Offset Units", Float) = 0
        _PaintBias ("Paint Bias", Float) = 0
        _LayerLift ("Layer Lift", Float) = 0
    }
    SubShader {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        Pass {
            Name "EnhancedTerrain"
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
            #include "Assets/Shaders/Custom/World/TerrainPass.hlsl"
            ENDHLSL
        }
    }
}
