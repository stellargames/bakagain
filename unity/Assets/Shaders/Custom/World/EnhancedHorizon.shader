// ClassicHorizon.shader for Enhanced mode: the same pass (HorizonPass.hlsl) compiled with BAK_ENHANCED.
// Properties and render state stay in step with ClassicHorizon (WorldShaders swaps one for the other
// on live materials); the comments explaining them are there.
Shader "BakAgain/EnhancedHorizon" {
    Properties {
        [MainTexture] _BaseMap ("Texture", 2D) = "white" {}
        _SkyKey ("Sky key (the panel's sky pen)", Color) = (0, 0, 0, 0)
    }
    SubShader {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Background" }
        Pass {
            Name "EnhancedHorizon"
            Tags { "LightMode"="UniversalForward" }
            Cull Off
            ZWrite Off
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #define BAK_ENHANCED
            #include "Assets/Shaders/Custom/World/HorizonPass.hlsl"
            ENDHLSL
        }
    }
}
