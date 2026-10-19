#ifndef BAK_POLYGON_PASS_INCLUDED
#define BAK_POLYGON_PASS_INCLUDED

// The polygon pass shared by ClassicPolygon.shader and EnhancedPolygon.shader. The Enhanced twin defines
// BAK_ENHANCED before including it; the Classic one never compiles any Enhanced code.

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
    float _LayerLift;
CBUFFER_END

// GLOBAL — see ClassicTerrain. ZoneEnvironment.SetOverheadMapMode is the only writer.
float _MapMode;


Varyings vert(Attributes input) {
    Varyings output;
    VertexPositionInputs posInputs = GetVertexPositionInputs(input.positionOS.xyz);
    // _LayerLift: see Properties. Along the view ray, so x/y do not move.
    float3 liftedWS = posInputs.positionWS
        + normalize(GetCameraPositionWS() - posInputs.positionWS) * _LayerLift;
    output.positionCS = TransformWorldToHClip(liftedWS);
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
    // URP distance fog
    color.rgb = MixFog(color.rgb, input.fogCoord);
    // Lighting AFTER fog: the original darkens the whole palette, the fog pens included,
    // so distant fogged terrain darkens at night like everything else (TASK-761).
    color.rgb = ApplyBakLighting(color.rgb);
    return color;
}

#endif
