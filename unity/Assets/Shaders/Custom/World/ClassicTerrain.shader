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
            #include "Assets/Shaders/Custom/World/TerrainPass.hlsl"
            ENDHLSL
        }
    }
}
