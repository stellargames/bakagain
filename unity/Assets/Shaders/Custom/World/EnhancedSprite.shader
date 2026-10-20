// ClassicSprite.shader for Enhanced mode: the same pass (SpritePass.hlsl) compiled with BAK_ENHANCED.
// Properties and render state stay in step with ClassicSprite (WorldShaders swaps one for the other
// on live materials); the comments explaining them are there.
Shader "BakAgain/EnhancedSprite" {
    Properties {
        _MainTex ("Sprite Texture", 2D) = "white" {}
        _AlphaCutoff ("Alpha Cutoff", Float) = 0.01
        _FlashColor ("Flash Colour", Color) = (0,0,0,0)
        _SunSet ("Sun Set", Float) = 0
        _VarySize ("Size Variation", Float) = 0
        _VaryColor ("Colour Variation", Float) = 0
        _SunTexF ("Sun Front", 2D) = "black" {}
        _SunTexL ("Sun Left", 2D) = "black" {}
        _SunTexR ("Sun Right", 2D) = "black" {}
        _SunTexB ("Sun Back", 2D) = "black" {}
    }
    SubShader {
        Tags { "RenderType"="TransparentCutout" "RenderPipeline"="UniversalPipeline" "Queue"="AlphaTest" }
        Pass {
            Name "EnhancedSprite"
            Tags { "LightMode"="UniversalForward" }
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #define BAK_ENHANCED
            #include "Assets/Shaders/Custom/World/SpritePass.hlsl"
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
            #include "Assets/Shaders/Custom/World/SpritePass.hlsl"
            ENDHLSL
        }
    }
}
