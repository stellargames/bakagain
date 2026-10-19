#ifndef BAK_TEXTURED_POLYGON_PASS_INCLUDED
#define BAK_TEXTURED_POLYGON_PASS_INCLUDED

// The texturedpolygon pass shared by ClassicTexturedPolygon.shader and EnhancedTexturedPolygon.shader. The Enhanced twin defines
// BAK_ENHANCED before including it; the Classic one never compiles any Enhanced code.

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Assets/Shaders/Custom/World/BakLighting.hlsl"
#ifdef BAK_ENHANCED
#include "Assets/Shaders/Custom/World/EnhancedShadows.hlsl"
#endif

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
#ifdef BAK_ENHANCED
    float3 positionWS : TEXCOORD2;
#endif
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
#ifdef BAK_ENHANCED
    output.positionWS = posInputs.positionWS;
#endif
    return output;
}

half4 frag(Varyings input) : SV_Target {
    half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
    clip(tex.a - _AlphaCutoff); // palette index 0 = transparent (see-through gaps)
    // The bitmap alone, as the original's drawTexturedPolygon draws it. On a slot-textured
    // face VgaColor is the slot INDEX, so the vertex colour is an unrelated pen; multiplying
    // by it turned chests near-black (TASK-750).
    half4 color = tex;
#ifdef BAK_ENHANCED
    color.rgb *= BakSunShade(input.positionWS);
#endif
    // URP distance fog
    color.rgb = MixFog(color.rgb, input.fogCoord);
    // Lighting AFTER fog: the original darkens the whole palette, the fog pens included,
    // so distant fogged terrain darkens at night like everything else (TASK-761).
    color.rgb = ApplyBakLighting(color.rgb);
    return color;
}

#ifdef BAK_ENHANCED
// The shadow pass, with the drawn pass's cut-out: a see-through gap lets the sun through.
struct ShadowVaryings {
    float4 positionCS : SV_POSITION;
    float2 uv : TEXCOORD0;
};

ShadowVaryings shadowVert(Attributes input) {
    ShadowVaryings output;
    output.positionCS = BakShadowCasterClip(TransformObjectToWorld(input.positionOS.xyz), TransformObjectToWorldNormal(input.normalOS));
    output.uv = TRANSFORM_TEX(input.uv, _MainTex);
    return output;
}

half4 shadowFrag(ShadowVaryings input) : SV_Target {
    clip(SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv).a - _AlphaCutoff);
    return 0;
}
#endif

#endif
