#ifndef BAK_HORIZON_PASS_INCLUDED
#define BAK_HORIZON_PASS_INCLUDED

// The horizon pass shared by ClassicHorizon.shader and EnhancedHorizon.shader. The Enhanced twin defines
// BAK_ENHANCED before including it; the Classic one never compiles any Enhanced code.

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Assets/Shaders/Custom/World/BakLighting.hlsl"
#ifdef BAK_ENHANCED
#include "Assets/Shaders/Custom/World/EnhancedSkyCommon.hlsl"

// Enhanced sky (spec 2026-10-15): 1 while the dome is shown. Then the panel's sky pixels are
// discarded so the dome shows above the silhouettes, and the hills are hazed toward the
// sky behind them (_BakHaze: x = strength, y = extra at the strip's base). 0 = as before.
float _BakSkyOn;
float4 _BakHaze;
#endif

struct Attributes {
    float4 positionOS : POSITION;
    float2 uv : TEXCOORD0;
};

struct Varyings {
    float4 positionCS : SV_POSITION;
    float2 uv : TEXCOORD0;
    float3 positionWS : TEXCOORD1;
};

TEXTURE2D(_BaseMap);
SAMPLER(sampler_BaseMap);

CBUFFER_START(UnityPerMaterial)
    float4 _BaseMap_ST;
    float4 _SkyKey;
CBUFFER_END

Varyings vert(Attributes input) {
    Varyings output;
    output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
    output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
    output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
    return output;
}

half4 frag(Varyings input) : SV_Target {
    half4 color = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
#ifdef BAK_ENHANCED
    if (_BakSkyOn > 0.5) {
        clip(distance(color.rgb, _SkyKey.rgb) - 0.002);   // discard the sky-coloured pixels
    }
#endif
    color.rgb = ApplyBakLighting(color.rgb);
#ifdef BAK_ENHANCED
    if (_BakSkyOn > 0.5) {
        float3 v = normalize(input.positionWS - _WorldSpaceCameraPos);
        float haze = _BakHaze.x + _BakHaze.y * (1.0 - input.uv.y);
        // Toward the sky the dome shows, which is darkened as the pen is.
        color.rgb = lerp(color.rgb, ApplyBakLighting(SkyRadiance(float3(v.x, max(v.y, 0.01), v.z)), 0.0), saturate(haze));
    }
#endif
    return color;
}

#endif
