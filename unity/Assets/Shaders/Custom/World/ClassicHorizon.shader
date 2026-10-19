Shader "BakAgain/ClassicHorizon" {
    // Skybox-like backdrop for the Z##H.BMX horizon panels. Renders in the Background queue with
    // ZWrite Off so it is ALWAYS behind world geometry — the quad sits a fixed distance in front of
    // the camera, so without this any terrain/entity farther than that distance would (wrongly)
    // draw behind it. Unlit + Cull Off (the panel is a flat, double-sided painted strip).
    Properties {
        [MainTexture] _BaseMap ("Texture", 2D) = "white" {}
        _SkyKey ("Sky key (the panel's sky pen)", Color) = (0, 0, 0, 0)
    }
    SubShader {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Background" }
        Pass {
            Name "ClassicHorizon"
            Tags { "LightMode"="UniversalForward" }
            Cull Off
            ZWrite Off
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Assets/Shaders/Custom/World/HorizonPass.hlsl"
            ENDHLSL
        }
    }
}
