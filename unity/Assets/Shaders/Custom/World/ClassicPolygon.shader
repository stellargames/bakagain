Shader "BakAgain/ClassicPolygon" {
    Properties {
        // Depth-space polygon offset — DrawPriority overlay layering for terrain (ground/road/river).
        // Set per-material by GetLayeredTerrainMaterial.
        _OffsetFactor ("Depth Offset Factor", Float) = 0
        _OffsetUnits ("Depth Offset Units", Float) = 0
        // Paint-order depth bias — nudges each vertex toward the camera by its UV1 rank (paint order
        // within its coplanar face group), so later-painted coplanar faces win the depth tie. The
        // nudge is a constant EYE-space offset (≈ _PaintBias/nearClip units per rank), applied in
        // depth only. 0 = inert. Set per-material by the profiles; only depth-sorted models carry
        // non-zero ranks (terrain UV1 = 0).
        _PaintBias ("Paint Bias", Float) = 0
    }
    SubShader {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        Pass {
            Name "ClassicPolygon"
            Tags { "LightMode"="UniversalForward" }
            // Backface culling, as the original did. On 2026-09-06 the TBL winding was measured
            // consistent — 12790/12790 faces agree with their NormalVertexIndex, zero exceptions
            // (docs/re-notes/2026-09-06-face-winding-is-consistent.md, guarded by
            // BetrayalAtKrondor.Tests FaceWindingInvariantTests).
            //
            // WHY ONE FLAG IS ENOUGH, since this looks too cheap to be right. Double-sidedness is
            // encoded in the GEOMETRY, not in the material: TblMeshConverter.AppendFace emits a
            // second copy of every DoubleSided face wound the other way, and AppendLine emits its
            // ribbons both ways. Under Cull Off those copies were pure duplication; under Cull Back
            // they are exactly what keeps those faces visible from behind. So no cull mode has to
            // reach MaterialKey or the terrain material caches — the 937 double-sided and 9 skip
            // faces in the corpus already carry their own answer, and the 12790 single-sided ones
            // are the ones this culls.
            //
            // Measured effect (frpcnon in the model viewer, and Z01 in the travel world): back
            // faces stop bleeding through silhouettes — a stray crease across the distant mountain
            // and a cyan fringe on the cannon's edges both disappear. It does NOT change any
            // silhouette, so TASK-327's predicted pyramid did not appear; that difference is
            // placement, not geometry.
            //
            // The facing-signed paint bias below is therefore near-inert now (a rendered fragment
            // is almost always front-facing). It is kept because a double-sided face's back copy
            // carries a flipped normal, so the sign still resolves those coplanar pairs.
            Cull Back
            Offset [_OffsetFactor], [_OffsetUnits]

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
            #include "Assets/Shaders/Custom/World/BakLighting.hlsl"

            struct Attributes {
                float4 positionOS : POSITION;
                float4 color : COLOR;
                float2 uv1 : TEXCOORD1;
                float4 mapColor : TEXCOORD2; // what this face is drawn as on the overhead map
                float3 normalOS : NORMAL; // face normal (per-face duplicated verts) — bias facing
            };

            struct Varyings {
                float4 positionCS : SV_POSITION;
                float4 color : COLOR;
                float fogCoord : TEXCOORD0;
                float4 mapColor : TEXCOORD1;
            };

            CBUFFER_START(UnityPerMaterial)
                float _PaintBias;
            CBUFFER_END

            // GLOBAL — see ClassicTerrain. ZoneEnvironment.SetOverheadMapMode is the only writer.
            float _MapMode;


            Varyings vert(Attributes input) {
                Varyings output;
                VertexPositionInputs posInputs = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = posInputs.positionCS;
                // Paint-order depth bias: push later-painted (higher-rank) coplanar faces toward the
                // camera so they win the depth tie. The post-divide NDC offset is rank*_PaintBias/w²,
                // i.e. a constant EYE-space depth offset per rank (≈ _PaintBias/nearClip world units) —
                // a distance-independent NDC offset (the v1 `* w` form) outgrows the real NDC gap
                // between distinct surfaces at range (gap ∝ 1/z²) and let rear faces bleed through
                // walls. x/y and w are untouched, so there is no parallax. The NDC cap keeps extreme
                // close-ups (w → nearClip) from pushing depth past the near plane.
                //
                // The bias is SIGNED by facing: back-facing faces are pushed AWAY from the camera.
                // The original culled back-facing single-sided faces, and models carry coplanar
                // top/bottom pairs (bridge deck) where the bottom face is emitted last — under Cull
                // Off an unsigned bias let that high-rank back face beat the whole visible side.
                // Sign-flipping reproduces the original's per-side outcome: each side's front-facing
                // faces win, ordered among themselves by paint order.
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
                // Vertex colours are the palette's sRGB bytes and Unity does not convert them, so in
                // Linear colour space they are linearised here or the output encode brightens every
                // flat face a second time (TASK-747: pen 190 (73,44,24) drew as (146,115,86)).
                output.color = input.color;
                #if !defined(UNITY_COLORSPACE_GAMMA)
                    output.color.rgb = SRGBToLinear(input.color.rgb);
                    input.mapColor.rgb = SRGBToLinear(input.mapColor.rgb);
                #endif
                output.fogCoord = ComputeFogFactor(output.positionCS.z);
                output.mapColor = input.mapColor;
                return output;
            }

            half4 frag(Varyings input) : SV_Target {
                // Flat-filled faces take the overhead map's pen remap the same way terrain does
                // — baked per face, switched globally. See ClassicTerrain for why it is not a lookup.
                half4 color = lerp(input.color, half4(input.mapColor.rgb, 1.0), _MapMode);
                color.rgb = ApplyBakLighting(color.rgb);
                // URP distance fog
                color.rgb = MixFog(color.rgb, input.fogCoord);
                return color;
            }
            ENDHLSL
        }
    }
}
