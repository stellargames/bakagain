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
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Assets/Shaders/Custom/World/BakLighting.hlsl"
            #include "Assets/Shaders/Custom/World/EnhancedSkyCommon.hlsl"

            // Enhanced sky (spec 2026-10-15): 1 while the dome is shown. Then the panel's sky pixels are
            // discarded so the dome shows above the silhouettes, and the hills are hazed toward the
            // sky behind them (_BakHaze: x = strength, y = extra at the strip's base). 0 = as before.
            float _BakSkyOn;
            float4 _BakHaze;

            struct Attributes {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
            };

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _SkyKey;
            CBUFFER_END

            Varyings vert(Attributes input) {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                return output;
            }

            half4 frag(Varyings input) : SV_Target {
                half4 color = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
                if (_BakSkyOn > 0.5) {
                    clip(distance(color.rgb, _SkyKey.rgb) - 0.002);   // discard the sky-coloured pixels
                }
                color.rgb = ApplyBakLighting(color.rgb);
                if (_BakSkyOn > 0.5) {
                    float3 v = normalize(input.positionWS - _WorldSpaceCameraPos);
                    float haze = _BakHaze.x + _BakHaze.y * (1.0 - input.uv.y);
                    color.rgb = lerp(color.rgb, SkyRadiance(float3(v.x, max(v.y, 0.01), v.z)), saturate(haze));
                }
                return color;
            }
            ENDHLSL
        }
    }
}
