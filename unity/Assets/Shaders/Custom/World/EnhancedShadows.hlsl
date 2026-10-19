#ifndef BAK_ENHANCED_SHADOWS_INCLUDED
#define BAK_ENHANCED_SHADOWS_INCLUDED

// Enhanced shadows (TASK-882, spec 2026-10-15-enhanced-shadows-design): what the Enhanced world
// shaders share to cast into and read from URP's main-light shadow map. The Sun light's shadowStrength
// carries the strength (35 % at noon, following the sun's elevation, EnhancedSky.SetSun), so nothing
// here knows the hour. Included only by the Enhanced twins (under BAK_ENHANCED).

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

// Sun shadows fade out from 300 to 400 units from the camera; blobs take over beyond (spec Q2).
// The URP assets' shadow distance is the 400.
#define BAK_SHADOW_FADE_START 300.0
#define BAK_SHADOW_FADE_END 400.0

// The sun shadows' weight, 0..1: the sun's elevation term in Sun mode, 0 otherwise (EnhancedSky).
// Only the blobs read it: 1 - weight x distance fade is their opacity.
float _BakSunShadow;

// URP sets it for the shadow pass: the direction TO the light.
float3 _LightDirection;

float BakSunShadowFade(float3 positionWS) {
    float d = distance(positionWS, GetCameraPositionWS());
    return saturate((BAK_SHADOW_FADE_END - d) / (BAK_SHADOW_FADE_END - BAK_SHADOW_FADE_START));
}

// The factor a receiving colour is multiplied by: 1 in full sun, 1 - strength in full shade.
half BakSunShade(float3 positionWS) {
#if defined(_MAIN_LIGHT_SHADOWS) || defined(_MAIN_LIGHT_SHADOWS_CASCADE)
    half shade = MainLightRealtimeShadow(TransformWorldToShadowCoord(positionWS));
    return lerp(1.0h, shade, (half)BakSunShadowFade(positionWS));
#else
    return 1.0h;
#endif
}

// A caster's clip position: URP's depth and normal bias, clamped to the near plane so casters behind
// the light's near plane still land in the map.
float4 BakShadowCasterClip(float3 positionWS, float3 normalWS) {
    float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, _LightDirection));
#if UNITY_REVERSED_Z
    positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
#else
    positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
#endif
    return positionCS;
}

// A billboard's right vector in its shadow pass: square to the light seen from above, so the quad
// faces the sun and casts its full silhouette from any sun heading. (The view matrix in this pass is
// the light's, so the drawn pass's camera-facing code would mostly do the same; this does not depend
// on how URP rolls that matrix.) With the sun straight overhead there is no such direction and the
// view's is kept.
float3 BakBillboardRightToLight(float3 cameraRight) {
    float3 toLight = float3(_LightDirection.x, 0, _LightDirection.z);
    float len = length(toLight);
    return len > 1e-3 ? float3(toLight.z, 0, -toLight.x) / len : cameraRight;
}

#endif
