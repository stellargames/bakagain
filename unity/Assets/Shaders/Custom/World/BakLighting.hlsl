#ifndef BAK_LIGHTING_INCLUDED
#define BAK_LIGHTING_INCLUDED

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"

// The original's day/night lighting, applied to a rendered colour instead of to the palette.
//
// ApplyDynamicLighting (seg031 @0x2cdd9) walks a chain of palette lerps: first toward the
// dragon's-breath colour, then toward a tint if one applies, then toward black by the light level.
// Every step is BlendPaletteColors, whose arithmetic is
//   dest = source + (target - source) * (64 - light) / 64
// so a HIGHER light level moves LESS. Because our palettes are already RGBA by the time anything
// renders, running the same chain per pixel gives the same result as running it per palette entry.
//
// The three factors arrive as globals already reduced to "how far to move", so the shader carries
// no lighting rules of its own -- WorldLightingService owns them and they stay testable in C#.

float4 _BakLightDragonsBreath;   // rgb = target colour, a = movement 0..1
float4 _BakLightTint;            // rgb = target colour, a = movement 0..1
float4 _BakLightDarken;          // rgb = black, a = movement 0..1
// A combat spell's palette flash (cspell_play_palette_flash, CSPELL.C:336): the original tints DAC
// range 0x70..0xFE -- every world colour, none of the UI's -- toward one palette entry. Applied last,
// on top of the lighting, because palette_set_scaled re-derives the whole range from the lit palette.
float4 _BakFlash;                // rgb = target colour, a = movement 0..1

// The original blends PALETTE entries toward each target (docs/specs/lighting-system.md:58), i.e. in
// display space, and the targets arrive as raw sRGB vectors. So in Linear colour space the blend is
// done on the sRGB value and converted back; blending linear values left night far too bright
// (half darkness gave 70% of the palette value instead of 50%, TASK-748).
half3 ApplyBakLighting(half3 color) {
#if !defined(UNITY_COLORSPACE_GAMMA)
    color = LinearToSRGB(color);
#endif
    color = lerp(color, _BakLightDragonsBreath.rgb, _BakLightDragonsBreath.a);
    color = lerp(color, _BakLightTint.rgb, _BakLightTint.a);
    color = lerp(color, _BakLightDarken.rgb, _BakLightDarken.a);
    color = lerp(color, _BakFlash.rgb, _BakFlash.a);
#if !defined(UNITY_COLORSPACE_GAMMA)
    color = SRGBToLinear(color);
#endif

    return color;
}

#endif
