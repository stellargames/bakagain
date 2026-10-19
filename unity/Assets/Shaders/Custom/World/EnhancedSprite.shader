// ClassicSprite.shader for Enhanced mode: the same pass (SpritePass.hlsl) compiled with BAK_ENHANCED.
// Properties and render state stay in step with ClassicSprite (WorldShaders swaps one for the other
// on live materials); the comments explaining them are there.
Shader "BakAgain/EnhancedSprite" {
    Properties {
        _MainTex ("Sprite Texture", 2D) = "white" {}
        _AlphaCutoff ("Alpha Cutoff", Float) = 0.01
        _FlashColor ("Flash Colour", Color) = (0,0,0,0)
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
            #define BAK_ENHANCED
            #include "Assets/Shaders/Custom/World/SpritePass.hlsl"
            ENDHLSL
        }
    }
}
