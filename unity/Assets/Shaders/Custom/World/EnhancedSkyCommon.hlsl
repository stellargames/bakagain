#ifndef BAK_ENHANCED_SKY_COMMON
#define BAK_ENHANCED_SKY_COMMON

// The Enhanced sky's single-scattering model (spec 2026-10-15-enhanced-sky-design), shared by the
// dome and by the mountains' haze so the two agree at the horizon. Analytic, no LUT pass: Rayleigh +
// Mie with an air-mass approximation (Kasten-Young), evaluated per pixel. Invented for Enhanced — the
// original's sky is one flat palette pen.
//
// Globals, all written by EnhancedSky.cs. Directions are world space: +X east, +Y up, +Z north.
float4 _BakSkySunDir;     // xyz = unit vector toward the sun
float4 _BakSkySunColor;   // rgb = the sun's light colour (SkyClock.SunColor), linear
float4 _BakSkyZoneColor;  // rgb = the zone's resolved sky pen, linear
float  _BakSkyAnchor;     // how far the horizon's hue is pulled to the zone's (0..1, already x noon factor)
float  _BakSkyExposure;   // 2^-EV of Lighting's grading exposure, 1 without it: undoes it for the sky

static const float3 BAK_RAYLEIGH = float3(5.8e-3, 13.5e-3, 33.1e-3);
static const float  BAK_MIE = 2.1e-3;
static const float  BAK_SKY_GAIN = 12.0;
static const float3 BAK_LUMA = float3(0.2126, 0.7152, 0.0722);

float BakAirMass(float cosZenith) {
    float c = max(cosZenith, 0.0);
    float zenithDeg = degrees(acos(c));
    return 1.0 / (c + 0.15 * pow(max(93.885 - zenithDeg, 1e-3), -1.253));
}

// How far above the horizon the sun is, as a 0..1 fade: the twilight glow dies out below it.
float BakSunUp() {
    return smoothstep(-0.25, 0.05, _BakSkySunDir.y);   // glow lasts to about -14 degrees (nautical twilight)
}

// Linear radiance seen along unit direction v. Above the horizon only; callers clamp v.y.
float3 SkyRadiance(float3 v) {
    float3 s = normalize(_BakSkySunDir.xyz);
    float mu = dot(v, s);
    float3 thickness = BAK_RAYLEIGH * 8.4 + BAK_MIE * 1.25;
    float3 ext = exp(-thickness * BakAirMass(v.y));
    float3 sunExt = exp(-thickness * BakAirMass(s.y));
    float pr = 0.0596831 * (1.0 + mu * mu);
    const float g = 0.76;
    float pm = 0.0795775 * (1.0 - g * g) / pow(max(1.0 + g * g - 2.0 * g * mu, 1e-4), 1.5);
    float3 scatter = (BAK_RAYLEIGH * pr + BAK_MIE * pm) / (BAK_RAYLEIGH + BAK_MIE);
    float3 col = scatter * (1.0 - ext) * sunExt * BAK_SKY_GAIN * BakSunUp();
    col += float3(0.004, 0.006, 0.015);   // night floor: deep blue, never pure black

    // Zone anchor: near the horizon, by day, take the zone pen's HUE at the model's own brightness,
    // so each zone keeps its character without the sky going darker toward the horizon.
    float horizonBand = 1.0 - smoothstep(0.0, 0.35, max(v.y, 0.0));
    float zoneLum = max(dot(_BakSkyZoneColor.rgb, BAK_LUMA), 1e-4);
    float3 zoneHue = _BakSkyZoneColor.rgb / zoneLum * dot(col, BAK_LUMA);
    return lerp(col, zoneHue, saturate(_BakSkyAnchor) * horizonBand);
}

#endif
