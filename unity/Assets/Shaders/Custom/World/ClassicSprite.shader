Shader "BakAgain/ClassicSprite" {
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
            Name "ClassicSprite"
            Tags { "LightMode"="UniversalForward" }
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Assets/Shaders/Custom/World/SpritePass.hlsl"
            ENDHLSL
        }
    }
}
