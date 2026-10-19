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
        // Terrain overlay lift (road, river): world units the vertex moves toward the camera along
        // its view ray, so its screen position is unchanged and only its depth gets ahead of the
        // ground it lies on. The Offset state above cannot do this looking straight down (the
        // overhead map): the slope term is zero there and one unit is about one float depth step,
        // so roads z-fought the ground. Set per-material by GetLayeredTerrainMaterial. 0 = inert.
        _LayerLift ("Layer Lift", Float) = 0
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
            #include "Assets/Shaders/Custom/World/PolygonPass.hlsl"
            ENDHLSL
        }
    }
}
