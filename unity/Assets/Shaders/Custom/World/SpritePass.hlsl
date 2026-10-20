#ifndef BAK_SPRITE_PASS_INCLUDED
#define BAK_SPRITE_PASS_INCLUDED

// The sprite pass shared by ClassicSprite.shader and EnhancedSprite.shader. The Enhanced twin defines
// BAK_ENHANCED before including it; the Classic one never compiles any Enhanced code.

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Assets/Shaders/Custom/World/BakLighting.hlsl"
#ifdef BAK_ENHANCED
#include "Assets/Shaders/Custom/World/EnhancedShadows.hlsl"
#endif

struct Attributes {
    float4 positionOS : POSITION;
    float2 uv : TEXCOORD0;
};

struct Varyings {
    float4 positionCS : SV_POSITION;
    float2 uv : TEXCOORD0;
    float fogCoord : TEXCOORD1;
    float spriteFog : TEXCOORD2;
    float nearFade : TEXCOORD3;
#ifdef BAK_ENHANCED
    // The sun shadow at the entity's foot, sampled once in the vertex stage (spec Q3).
    half sunShade : TEXCOORD4;
    half2 sunSide : TEXCOORD5;   // x = sun from the view's right (+) / left (-); y = sun behind the viewer (+)
    half3 tint : TEXCOORD6;      // per-instance colour variation
#endif
};

CBUFFER_START(UnityPerMaterial)
    float _AlphaCutoff;
    // One actor's tint (a spell hit's RED/WHITE/BLUE.RMP remap, WORLDRND.C:208), set per
    // renderer through a property block by SpellVfx. a = 0 leaves the sprite alone.
    float4 _FlashColor;
    // An HD frame's (HdFrames): 1 when it has the four sun-lit versions. Per material, or the SRP
    // Batcher leaks it into every other sprite's draw.
    float _SunSet;
    // Per-instance variation (0 = off): size as a fraction (0.08 = up to +-8%), colour likewise.
    float _VarySize;
    float _VaryColor;
CBUFFER_END

// Three stable random numbers in [-1, 1] for the sprite standing at worldPos (its pivot), so a tree
// keeps its own size and tint from frame to frame and across saves.
float3 InstanceRandom(float3 worldPos) {
    uint2 q = (uint2)(int2)floor(worldPos.xz * 4);
    uint h = q.x * 1597334677u ^ q.y * 3812015801u;
    h = (h ^ (h >> 16)) * 0x7feb352du;
    uint3 r = uint3(h, h * 0x846ca68bu, h * 0x9e3779b9u);
    r ^= r >> 15;
    return float3(r & 0xffffu) / 32767.5 - 1;
}

TEXTURE2D(_MainTex);
SAMPLER(sampler_MainTex);
// An HD frame's sun-lit versions: lit from the front, left, right and behind; _MainTex is soft front light.
TEXTURE2D(_SunTexL); TEXTURE2D(_SunTexR); TEXTURE2D(_SunTexB); TEXTURE2D(_SunTexF);

// The zone's own sprite haze range (Z##DEF.DAT, TASK-434), set as globals by the render
// profile. Only sprites fade on this range, as only sprites are remapped in the original;
// with no range set (end <= start) the shared URP fog applies as before.
float _BakSpriteFogStart;
float _BakSpriteFogEnd;
// Near fade (TASK-746, SpriteNearFade): hidden within _BakSpriteNearHide, whole from
// _BakSpriteNearShow, Unity units. Unset (0,0) means no fade.
float _BakSpriteNearHide;
float _BakSpriteNearShow;

// The quad's world position: about the pivot, along `right` and world up, honouring the transform's
// scale and mirror.
float3 BillboardWorldPos(float3 worldPos, float3 right, float2 positionOS) {
    // Object-to-world lossy scale (columns of M are scaled basis vectors)
    float scaleX = length(float3(UNITY_MATRIX_M._m00, UNITY_MATRIX_M._m10, UNITY_MATRIX_M._m20));
    float scaleY = length(float3(UNITY_MATRIX_M._m01, UNITY_MATRIX_M._m11, UNITY_MATRIX_M._m21));
    // A length has no sign, so a mirrored sprite (negative x scale: the left-facing
    // octants, TASK-694) drew unmirrored. The matrix's handedness carries the flip.
    if (determinant((float3x3)UNITY_MATRIX_M) < 0) scaleX = -scaleX;
    // Scaled about the pivot at the sprite's foot, so a bigger tree still stands on the ground.
    float vary = 1 + _VarySize * InstanceRandom(worldPos).x;
    return worldPos + (right * positionOS.x * scaleX + float3(0, 1, 0) * positionOS.y * scaleY) * vary;
}

float3 CameraRightFlat() {
    float3 camRight = UNITY_MATRIX_V[0].xyz;
    camRight.y = 0;
    // Degenerate only if the camera is rolled onto its side, which the game never
    // does; fall back to world X rather than hand normalize() a zero vector.
    float rightLen = length(camRight);
    return rightLen > 1e-4 ? camRight / rightLen : float3(1, 0, 0);
}

Varyings vert(Attributes input) {
    Varyings output;

    // Y-axis billboard: turn to face the camera, but stay UPRIGHT.
    //
    // Taking camUp from UNITY_MATRIX_V[1] is a SCREEN-ALIGNED billboard, which tilts
    // the quad flat as soon as the camera pitches. On the overhead map — the same
    // world camera tipped straight down — that left every tree face-on to the camera
    // and painted the map with trees the original's map does not show.
    //
    // This is also what BillboardSprite.cs (the CPU path used by enhanced mode) has
    // always done: "Face camera but stay upright", flattening the look direction. The
    // two paths render the same entities and disagreed; world up is the one that
    // matches the original, so the shader moves to it.
    float3 worldPos = TransformObjectToWorld(float3(0, 0, 0)); // pivot
    float3 billboardPos = BillboardWorldPos(worldPos, CameraRightFlat(), input.positionOS.xy);

    output.positionCS = TransformWorldToHClip(billboardPos);
    output.uv = input.uv;
    output.fogCoord = ComputeFogFactor(output.positionCS.z);
    float3 toSprite = worldPos - _WorldSpaceCameraPos;
    float spriteDistance = length(toSprite.xz);
    output.spriteFog = _BakSpriteFogEnd > _BakSpriteFogStart
        ? saturate((spriteDistance - _BakSpriteFogStart) / (_BakSpriteFogEnd - _BakSpriteFogStart))
        : -1;
    output.nearFade = _BakSpriteNearShow > _BakSpriteNearHide
        ? saturate((spriteDistance - _BakSpriteNearHide) / (_BakSpriteNearShow - _BakSpriteNearHide))
        : 1;
#ifdef BAK_ENHANCED
    // Sampled a little toward the light from the foot and above the ground, clear of where the
    // sprite's own shadow begins: at the foot itself only the shadow bias keeps it lit.
    float3 toLight = normalize(float3(_MainLightPosition.x, 0, _MainLightPosition.z) + float3(1e-4, 0, 0));
    float height = length(float3(UNITY_MATRIX_M._m01, UNITY_MATRIX_M._m11, UNITY_MATRIX_M._m21));
    output.sunShade = BakSunShade(worldPos + (toLight + float3(0, 0.5, 0)) * 0.1 * height);
    float3 toCam = normalize(float3(-toSprite.x, 0, -toSprite.z) + float3(1e-4, 0, 0));
    output.sunSide = half2(dot(CameraRightFlat(), toLight), dot(toCam, toLight));
    // Brightness and warmth, each up to +-_VaryColor: some trees a little darker, some a little yellower.
    float3 rnd = InstanceRandom(worldPos);
    output.tint = 1 + _VaryColor * (rnd.y + float3(rnd.z, 0.3 * rnd.z, -rnd.z));
#endif
    return output;
}

half4 frag(Varyings input) : SV_Target {
    half4 color = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
    clip(color.a - _AlphaCutoff);
#ifdef BAK_ENHANCED
    if (_SunSet > 0) {
        // Daylight: the four sun-direction photos, weighted by where the sun is relative to the view
        // (x: from the right +, y: behind the viewer +). Night and low sun fade to the soft-lit base.
        half wR = saturate(input.sunSide.x), wL = saturate(-input.sunSide.x);
        half wF = saturate(input.sunSide.y), wB = saturate(-input.sunSide.y);
        half3 lit = (SAMPLE_TEXTURE2D(_SunTexR, sampler_MainTex, input.uv).rgb * wR
                   + SAMPLE_TEXTURE2D(_SunTexL, sampler_MainTex, input.uv).rgb * wL
                   + SAMPLE_TEXTURE2D(_SunTexF, sampler_MainTex, input.uv).rgb * wF
                   + SAMPLE_TEXTURE2D(_SunTexB, sampler_MainTex, input.uv).rgb * wB) / max(wR + wL + wF + wB, 1e-3);
        color.rgb = lerp(color.rgb, lit, (half)_BakSunShadow);
    }
#endif
    // Ordered 4x4 dither: a screen-door fade that stays in the opaque queue.
    if (input.nearFade < 1) {
        uint2 px = (uint2)input.positionCS.xy & 3;
        const float bayer[16] = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };
        clip(input.nearFade - (bayer[px.y * 4 + px.x] + 0.5) / 16.0);
    }
    color.rgb = lerp(color.rgb, _FlashColor.rgb, _FlashColor.a);
#ifdef BAK_ENHANCED
    color.rgb *= input.tint;
    color.rgb *= input.sunShade;
#endif
    // The sprite haze when a range is set, else URP distance fog.
    color.rgb = input.spriteFog >= 0
        ? lerp(color.rgb, unity_FogColor.rgb, input.spriteFog)
        : MixFog(color.rgb, input.fogCoord);
    // Lighting AFTER fog: the original darkens the whole palette, the fog pens included,
    // so distant fogged terrain darkens at night like everything else (TASK-761).
    color.rgb = ApplyBakLighting(color.rgb);
    return color;
}

#ifdef BAK_ENHANCED
// The shadow pass: the same quad turned to face the sun (spec: a billboard turns to the sun in its
// shadow pass), with the drawn pass's alpha cut-out.
struct ShadowVaryings {
    float4 positionCS : SV_POSITION;
    float2 uv : TEXCOORD0;
};

ShadowVaryings shadowVert(Attributes input) {
    ShadowVaryings output;
    float3 worldPos = TransformObjectToWorld(float3(0, 0, 0));
    float3 right = BakBillboardRightToLight(CameraRightFlat());
    float3 positionWS = BillboardWorldPos(worldPos, right, input.positionOS.xy);
    float3 normalWS = normalize(cross(right, float3(0, 1, 0)));
    output.positionCS = BakShadowCasterClip(positionWS, normalWS);
    output.uv = input.uv;
    return output;
}

half4 shadowFrag(ShadowVaryings input) : SV_Target {
    clip(SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv).a - _AlphaCutoff);
    return 0;
}
#endif

#endif
