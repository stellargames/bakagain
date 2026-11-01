// The Enhanced board's marks and grid lines, lit by the world's light level: plain vertex colour, alpha
// blended like Sprites/Default, then moved part of the way along the world's lighting chain
// (BakLighting.hlsl), so at night or underground they dim with the ground instead of glowing over it
// (owner, TASK-972, 2026-11-01). _LightFollow is how far: 0 unlit, 1 lit exactly as the world.
//
// Lives under Resources/ so Shader.Find resolves it in a player build without a material asset.
Shader "BakAgain/LitMark" {
    Properties {
        _LightFollow ("Light follow", Range(0, 1)) = 0.75
    }
    SubShader {
        Tags { "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" }
        Pass {
            Name "LitMark"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Assets/Shaders/Custom/World/BakLighting.hlsl"

            float _LightFollow;

            struct Attributes {
                float4 positionOS : POSITION;
                half4 color : COLOR;
            };

            struct Varyings {
                float4 positionCS : SV_POSITION;
                half4 color : COLOR;
            };

            Varyings vert(Attributes input) {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.color = input.color;
                return output;
            }

            half4 frag(Varyings input) : SV_Target {
                half3 lit = ApplyBakLighting(input.color.rgb);
                return half4(lerp(input.color.rgb, lit, _LightFollow), input.color.a);
            }
            ENDHLSL
        }
    }
}
