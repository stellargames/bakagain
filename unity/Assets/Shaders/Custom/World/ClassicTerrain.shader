Shader "BakAgain/ClassicTerrain" {
    Properties {
        _MainTex ("Terrain Texture", 2D) = "white" {}
        _TexScale ("Texture Scale (world units per tile)", Float) = 10
        _ColorBlend ("Vertex Color Blend", Range(0, 1)) = 0.3
        // Depth-space polygon offset — drives DrawPriority overlay layering (road/river biased
        // toward camera to win the coplanar depth test vs ground). Set per-material by drawPriority.
        _OffsetFactor ("Depth Offset Factor", Float) = 0
        _OffsetUnits ("Depth Offset Units", Float) = 0
        // Paint-order depth bias — same mechanism as ClassicPolygon (see there). Depth-sorted
        // models carry pen-textured faces too (fall1's River/Waterfall stream): without this,
        // coplanar water faces have no tie-break and z-fight. Terrain tiles bake rank 0 on every
        // vertex, so the bias is inert for them.
        _PaintBias ("Paint Bias", Float) = 0
    }
    SubShader {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        Pass {
            Name "ClassicTerrain"
            Tags { "LightMode"="UniversalForward" }
            // Double-sided (see ClassicPolygon, whose comment carries the detail). BOTH CLAIMS IN
            // Backface culling, as the original did — see ClassicPolygon for why a single flag
            // suffices (TblMeshConverter already emits both windings for double-sided faces, so
            // no cull mode needs to reach the per-pen terrain material caches).
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
                float3 normalOS   : NORMAL;
                float4 color      : COLOR;
                float2 uv1        : TEXCOORD1; // paint-order rank in .x (see ClassicPolygon)
                float4 mapColor   : TEXCOORD2; // what this face is drawn as on the overhead map
            };

            struct Varyings {
                float4 positionCS : SV_POSITION;
                float4 color      : COLOR;
                float3 worldPos   : TEXCOORD0;
                float3 worldNormal: TEXCOORD1;
                float  fogCoord   : TEXCOORD2;
                float4 mapColor   : TEXCOORD3;
            };

            CBUFFER_START(UnityPerMaterial)
                float _TexScale;
                float _ColorBlend;
                float _PaintBias;
            CBUFFER_END

            // GLOBAL, not per-material: the overhead map is a mode the whole world enters at once,
            // and every terrain material would otherwise need setting and unsetting in step.
            // ZoneEnvironment.SetOverheadMapMode is the only writer. 0 while travelling.
            float _MapMode;

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_point_repeat);

            Varyings vert(Attributes input) {
                Varyings output;
                VertexPositionInputs posInputs = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = posInputs.positionCS;
                output.color = input.color;
                output.worldPos = TransformObjectToWorld(input.positionOS.xyz);
                output.worldNormal = TransformObjectToWorldNormal(input.normalOS);
                // Paint-order depth bias — identical to ClassicPolygon.vert: constant eye-space
                // nudge per rank (NDC offset = rank*_PaintBias/w², capped), signed by facing.
                // Rationale in ClassicPolygon.shader.
                float3 viewDirWS = GetCameraPositionWS() - posInputs.positionWS;
                float facing = dot(output.worldNormal, viewDirWS) >= 0.0 ? 1.0 : -1.0;
                float w = output.positionCS.w;
                float nudgeNdc = facing * min(input.uv1.x * _PaintBias / (w * w), 2e-3);
                float paintNudge = nudgeNdc * w;
                #if UNITY_REVERSED_Z
                    output.positionCS.z += paintNudge;
                #else
                    output.positionCS.z -= paintNudge;
                #endif
                output.fogCoord = ComputeFogFactor(output.positionCS.z);
                output.mapColor = input.mapColor;
                return output;
            }

            half4 frag(Varyings input) : SV_Target {
                // Top-down (XZ) triplanar projection — terrain is mostly horizontal.
                // For steep faces, blend in Y-axis projections weighted by normal.
                float3 blendWeights = abs(input.worldNormal);
                blendWeights /= (blendWeights.x + blendWeights.y + blendWeights.z + 1e-5);

                float invScale = 1.0 / max(_TexScale, 0.01);
                float2 uvXZ = input.worldPos.xz * invScale;
                float2 uvXY = input.worldPos.xy * invScale;
                float2 uvYZ = input.worldPos.yz * invScale;

                half4 texXZ = SAMPLE_TEXTURE2D(_MainTex, sampler_point_repeat, uvXZ);
                half4 texXY = SAMPLE_TEXTURE2D(_MainTex, sampler_point_repeat, uvXY);
                half4 texYZ = SAMPLE_TEXTURE2D(_MainTex, sampler_point_repeat, uvYZ);

                half4 texColor = texXZ * blendWeights.y + texXY * blendWeights.z + texYZ * blendWeights.x;

                // Blend with vertex color to preserve palette tinting
                half4 color = lerp(texColor, texColor * input.color, _ColorBlend);

                // *** THE OVERHEAD MAP'S PEN REMAP. *** Z##.DAT redraws each terrain pen as another
                // one, which is what gives the map its flatter palette instead of the world's. The
                // replacement colour is baked per face (UV2) rather than looked up here, because a
                // colour-keyed lookup cannot tell pen 0 apart — it is black, and black repeats in
                // every zone palette. Texture drops out entirely: the map draws flat fills.
                color = lerp(color, half4(input.mapColor.rgb, 1.0), _MapMode);
                color.a = 1.0;
                color.rgb = ApplyBakLighting(color.rgb);

                // URP distance fog
                color.rgb = MixFog(color.rgb, input.fogCoord);
                return color;
            }
            ENDHLSL
        }
    }
}
