// Lightens whatever is drawn under it by a factor, keeping its colour: the Enhanced reach squares read as
// the ground itself, a little lighter (owner, TASK-972 round 6). Double multiply: src*dst + dst*src, so a
// vertex colour of k/2 gives k times the ground. Vertex colour carries k/2 (ArenaBoardMarks).
//
// Lives under Resources/ so Shader.Find resolves it in a player build without a material asset.
Shader "BakAgain/GroundLighten" {
    SubShader {
        Tags { "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" }
        Pass {
            Name "GroundLighten"
            Tags { "LightMode"="UniversalForward" }
            Blend DstColor SrcColor
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

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
                return half4(input.color.rgb, 1);
            }
            ENDHLSL
        }
    }
}
