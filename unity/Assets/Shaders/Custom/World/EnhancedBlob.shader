// A blob shadow (TASK-882, spec 2026-10-15-enhanced-shadows-design): a soft dark ellipse on the ground
// under a sprite or a model, multiplied onto what is drawn there. Enhanced only, with no Classic twin:
// blobs sit on their own layer, which the world camera draws only while Shadows is Blobs or Sun
// (ShadowBlobs.Show). Multiplying keeps the ground's own lighting, so a blob darkens with the night as
// the ground does; running the multiplier through ApplyBakLighting as well would darken it twice.
Shader "BakAgain/EnhancedBlob" {
    SubShader {
        Tags { "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "Queue"="AlphaTest+10" "IgnoreProjector"="True" }
        Pass {
            Name "EnhancedBlob"
            Tags { "LightMode"="UniversalForward" }
            Blend DstColor Zero
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Assets/Shaders/Custom/World/EnhancedShadows.hlsl"

            // How dark the middle of a blob is.
            #define BLOB_DARKNESS 0.45
            // World units the blob is lifted toward the camera along its view ray, so it wins the
            // depth test against the ground it lies on without moving on screen (see _LayerLift).
            #define BLOB_LIFT 0.05

            struct Attributes {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float fogCoord : TEXCOORD1;
                float opacity : TEXCOORD2;
            };

            Varyings vert(Attributes input) {
                Varyings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 lifted = positionWS + normalize(GetCameraPositionWS() - positionWS) * BLOB_LIFT;
                output.positionCS = TransformWorldToHClip(lifted);
                output.uv = input.uv * 2.0 - 1.0;
                output.fogCoord = ComputeFogFactor(output.positionCS.z);
                // Crossfade with the sun shadows (spec Q1, Q2): where they are, the blob is not.
                float3 pivot = TransformObjectToWorld(float3(0, 0, 0));
                output.opacity = 1.0 - _BakSunShadow * BakSunShadowFade(pivot);
                return output;
            }

            half4 frag(Varyings input) : SV_Target {
                float d = length(input.uv);
                half dark = BLOB_DARKNESS * (1.0 - smoothstep(0.35, 1.0, d)) * input.opacity;
                // Fogged with the ground: in the haze it fades to nothing.
                half3 multiplier = MixFogColor(half3(1.0 - dark, 1.0 - dark, 1.0 - dark), half3(1, 1, 1), input.fogCoord);
                return half4(multiplier, 1);
            }
            ENDHLSL
        }
    }
}
