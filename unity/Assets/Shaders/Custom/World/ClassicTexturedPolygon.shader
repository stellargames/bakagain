Shader "BakAgain/ClassicTexturedPolygon" {
    Properties {
        _MainTex ("Slot Bitmap", 2D) = "white" {}
        // Palette index 0 is transparent in BMX sub-images (the original's drawTexturedPolygon skips
        // src byte 0). The sprite converter writes alpha 0 there, so cutout it — lets you see through
        // gaps in the bridge planks etc. Solid objects (chest) have no index-0 texels, so unaffected.
        _AlphaCutoff ("Alpha Cutoff", Float) = 0.01
        // Depth-space polygon offset — parity with ClassicPolygon (set per-material when a slot
        // sub-mesh needs biasing). Unused for flat slot quads but kept so material state matches.
        _OffsetFactor ("Depth Offset Factor", Float) = 0
        _OffsetUnits ("Depth Offset Units", Float) = 0
        // Paint-order depth bias — same mechanism as ClassicPolygon (see there). Textured slot quads
        // (bridge planks, signs) are coplanar decals too; without this they lose the depth tie to any
        // positively-ranked flat face in the same plane and get partially hidden.
        _PaintBias ("Paint Bias", Float) = 0
    }
    SubShader {
        Tags { "RenderType"="TransparentCutout" "RenderPipeline"="UniversalPipeline" "Queue"="AlphaTest" }
        Pass {
            Name "ClassicTexturedPolygon"
            Tags { "LightMode"="UniversalForward" }
            // Backface culling, matching ClassicPolygon — the winding IS guaranteed (12790/12790)
            // and double-sided faces carry their own reversed copy, so one flag covers every face.
            Cull Back
            Offset [_OffsetFactor], [_OffsetUnits]

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Assets/Shaders/Custom/World/BakLighting.hlsl"

            struct Attributes {
                float4 positionOS : POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
                float2 uv1 : TEXCOORD1;   // paint-order rank in .x (see ClassicPolygon)
                float3 normalOS : NORMAL; // face normal — bias facing (see ClassicPolygon)
            };

            struct Varyings {
                float4 positionCS : SV_POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
                float fogCoord : TEXCOORD1;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float _AlphaCutoff;
                float _PaintBias;
            CBUFFER_END

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            Varyings vert(Attributes input) {
                Varyings output;
                VertexPositionInputs posInputs = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = posInputs.positionCS;
                // Paint-order depth bias — identical to ClassicPolygon.vert: constant eye-space
                // nudge per rank (NDC offset = rank*_PaintBias/w², capped), signed by facing so
                // back-facing faces are pushed away instead of toward the camera. Depth only, no
                // parallax. Rationale in ClassicPolygon.shader.
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                float3 viewDirWS = GetCameraPositionWS() - posInputs.positionWS;
                float facing = dot(normalWS, viewDirWS) >= 0.0 ? 1.0 : -1.0;
                float w = output.positionCS.w;
                float nudgeNdc = facing * min(input.uv1.x * _PaintBias / (w * w), 2e-3);
                float paintNudge = nudgeNdc * w;
                #if UNITY_REVERSED_Z
                    output.positionCS.z += paintNudge;
                #else
                    output.positionCS.z -= paintNudge;
                #endif
                output.color = input.color;
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                output.fogCoord = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            half4 frag(Varyings input) : SV_Target {
                half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                clip(tex.a - _AlphaCutoff); // palette index 0 = transparent (see-through gaps)
                // The bitmap alone, as the original's drawTexturedPolygon draws it. On a slot-textured
                // face VgaColor is the slot INDEX, so the vertex colour is an unrelated pen; multiplying
                // by it turned chests near-black (TASK-750).
                half4 color = tex;
                color.rgb = ApplyBakLighting(color.rgb);
                // URP distance fog
                color.rgb = MixFog(color.rgb, input.fogCoord);
                return color;
            }
            ENDHLSL
        }
    }
}
