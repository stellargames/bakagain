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
            #include "Assets/Shaders/Custom/World/TexturedPolygonPass.hlsl"
            ENDHLSL
        }
    }
}
