Shader "BakAgain/ClassicHorizon" {
    // Skybox-like backdrop for the Z##H.BMX horizon panels. Renders in the Background queue with
    // ZWrite Off so it is ALWAYS behind world geometry — the quad sits a fixed distance in front of
    // the camera, so without this any terrain/entity farther than that distance would (wrongly)
    // draw behind it. Unlit + Cull Off (the panel is a flat, double-sided painted strip).
    Properties {
        [MainTexture] _BaseMap ("Texture", 2D) = "white" {}
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
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Assets/Shaders/Custom/World/BakLighting.hlsl"

            struct Attributes {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
            CBUFFER_END

            Varyings vert(Attributes input) {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                return output;
            }

            half4 frag(Varyings input) : SV_Target {
                half4 color = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
                color.rgb = ApplyBakLighting(color.rgb);
                return color;
            }
            ENDHLSL
        }
    }
}
