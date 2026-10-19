Shader "BakAgain/ClassicSprite" {
    Properties {
        _MainTex ("Sprite Texture", 2D) = "white" {}
        _AlphaCutoff ("Alpha Cutoff", Float) = 0.01
        _FlashColor ("Flash Colour", Color) = (0,0,0,0)
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
