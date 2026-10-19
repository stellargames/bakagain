Shader "BakAgain/EnhancedSky" {
    // The Enhanced sky dome (spec 2026-10-15-enhanced-sky-design): scattering gradient, sun disc,
    // stars, three moons and two cloud layers, on a sphere centred on the camera. Drawn before the
    // mountain quad (Background-1) with no depth write, so everything else lands in front of it.
    // Under Resources so Shader.Find reaches it in a build. All time and position rules are in
    // SkyClock (GameData); EnhancedSky.cs hands them over as globals.
    //
    // On mobile (SHADER_API_MOBILE, so no variant at runtime) the clouds' cover mask is read from the
    // cirrus texture instead of value noise, and their ambient is the gradient already computed: the
    // dome covers most of the screen, and two scattering evaluations plus 24 hashes a pixel is more
    // than a phone GPU should pay for it.
    Properties {
        _CloudsA ("Cumulus (R density, G thickness)", 2D) = "black" {}
        _CloudsB ("Cirrus (R density, G thickness)", 2D) = "black" {}
        _Moon0 ("Large Moon", 2D) = "black" {}
        _Moon1 ("Middle Moon", 2D) = "black" {}
        _Moon2 ("Small Moon", 2D) = "black" {}
    }
    SubShader {
        Tags { "RenderType"="Background" "RenderPipeline"="UniversalPipeline" "Queue"="Background-1" "PreviewType"="Skybox" }
        Pass {
            Name "EnhancedSky"
            Tags { "LightMode"="UniversalForward" }
            Cull Front
            ZWrite Off
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Assets/Shaders/Custom/World/EnhancedSkyCommon.hlsl"
            #include "Assets/Shaders/Custom/World/BakLighting.hlsl"

            TEXTURE2D(_CloudsA); SAMPLER(sampler_CloudsA);
            TEXTURE2D(_CloudsB); SAMPLER(sampler_CloudsB);
            TEXTURE2D(_Moon0); SAMPLER(sampler_Moon0);
            TEXTURE2D(_Moon1); SAMPLER(sampler_Moon1);
            TEXTURE2D(_Moon2); SAMPLER(sampler_Moon2);

            float4 _BakSkyMoonDir0;    // xyz = unit direction, w = angular radius (radians)
            float4 _BakSkyMoonDir1;
            float4 _BakSkyMoonDir2;
            float4 _BakSkyMoonPhase;   // xyz = phase per moon, 0 new .. 0.5 full
            float4 _BakSkyMoonBright;  // xyz = brightness per moon
            float4 _BakSkyCloud;       // x = cover 0..1, y = layers (1|2)
            float4 _BakSkyMoonLight;   // rgb = the Large Moon's light, a = its fullness 0..1 (SkyClock.MoonFullness)
            float4 _Moon0_TexelSize, _Moon1_TexelSize, _Moon2_TexelSize;
            float  _BakSkyStarRot;     // radians the star field has turned with the hour

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 dir : TEXCOORD0; };

            Varyings vert(Attributes input) {
                Varyings o;
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                o.dir = input.positionOS.xyz;   // the dome is centred on the camera, unrotated
                return o;
            }

            float Hash31(float3 p) {
                p = frac(p * 0.1031);
                p += dot(p, p.zyx + 31.32);
                return frac((p.x + p.y) * p.z);
            }

            float ValueNoise(float2 p) {
                float2 i = floor(p), f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float a = Hash31(float3(i, 1.0));
                float b = Hash31(float3(i + float2(1, 0), 1.0));
                float c = Hash31(float3(i + float2(0, 1), 1.0));
                float d = Hash31(float3(i + float2(1, 1), 1.0));
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            float Fbm(float2 p) {
                return ValueNoise(p) * 0.55 + ValueNoise(p * 2.03) * 0.3 + ValueNoise(p * 4.1) * 0.15;
            }

            // Rodrigues rotation of v about unit axis k by angle a.
            float3 Rotate(float3 v, float3 k, float a) {
                float c = cos(a), s = sin(a);
                return v * c + cross(k, v) * s + k * dot(k, v) * (1.0 - c);
            }

            // One moon: its disc over col, lit as a sphere by its phase.
            float3 Moon(float3 col, float3 v, float4 md, float phase, float bright, float night, float texSize,
                        TEXTURE2D_PARAM(tex, samp)) {
                float3 m = normalize(md.xyz);
                float r = md.w;
                float cosD = dot(v, m);
                if (cosD < cos(r) || m.y < -0.05) return col;
                float3 right = normalize(abs(m.y) > 0.99 ? float3(1, 0, 0) : cross(float3(0, 1, 0), m));
                float3 up = cross(m, right);
                float2 uv = float2(dot(v, right), dot(v, up)) / sin(r);
                // An explicit mip: inside this branch the screen-space derivatives are undefined at the
                // disc's edge, so it is worked out from the disc's size on screen (radius r over a pixel's
                // angle, 2 / (P11 * height) at the centre of the view).
                float discPixels = max(r * abs(UNITY_MATRIX_P._m11) * _ScreenParams.y, 1.0);
                float4 t = SAMPLE_TEXTURE2D_LOD(tex, samp, uv * 0.5 + 0.5, max(log2(texSize / discPixels), 0.0));
                float3 n = float3(uv, sqrt(saturate(1.0 - dot(uv, uv))));
                float a = 6.2831853 * phase;
                float lit = saturate(dot(n, float3(sin(a), 0.0, -cos(a)))) + 0.03;   // + earthshine
                return lerp(col, t.rgb * lit * bright * (0.15 + 0.85 * night), t.a);
            }

            half4 frag(Varyings input) : SV_Target {
                float3 v = normalize(input.dir);
                float3 s = normalize(_BakSkySunDir.xyz);
                float sunUp = BakSunUp();
                float night = saturate((-s.y - 0.02) / 0.15);

                // The gradient; below the horizon a darkening skirt the ground normally hides.
                float3 col = v.y >= 0.0
                    ? SkyRadiance(v)
                    : SkyRadiance(normalize(float3(v.x, 0.0, v.z))) * saturate(1.0 + v.y * 4.0);
                float3 gradient = col;

                // Stars: a hash per cell of a field turned about a pole 40 degrees up in the north.
                float3 pole = float3(0.0, 0.6427876, 0.7660444);
                float3 rv = Rotate(v, pole, _BakSkyStarRot);
                float3 cell = rv * 300.0;
                float h = Hash31(floor(cell));
                // A soft point at the cell's centre, not the whole cell: the cells are ~10 px at travel FOV.
                // Distance across the view, not in 3D: a cell's centre is rarely on the sphere itself.
                float3 o = frac(cell) - 0.5;
                float spot = saturate(1.0 - length(o - dot(o, rv) * rv) * 2.5);
                // Brightness from a second hash: frac(h * k) of an h this close to 1 spans only a sliver.
                float star = step(0.9985, h) * pow(Hash31(floor(cell) + 17.13), 3.0) * spot * spot;
                // Added after the night darkening (below): points of light on the black sky, which the
                // darkened pen alone would hide entirely. Hidden behind the moons and the clouds.
                float3 starLight = star * 0.8 * night * saturate(v.y * 8.0)
                    * step(dot(v, normalize(_BakSkyMoonDir0.xyz)), cos(_BakSkyMoonDir0.w))
                    * step(dot(v, normalize(_BakSkyMoonDir1.xyz)), cos(_BakSkyMoonDir1.w))
                    * step(dot(v, normalize(_BakSkyMoonDir2.xyz)), cos(_BakSkyMoonDir2.w));

                // The sun: an HDR disc so Lighting's bloom catches it, plus a tight glow.
                float d = acos(clamp(dot(v, s), -1.0, 1.0));
                float aboveGround = step(-0.02, s.y);
                col += _BakSkySunColor.rgb * 30.0 * smoothstep(0.0095, 0.0085, d) * aboveGround;
                col += _BakSkySunColor.rgb * 0.25 * exp(-d * 18.0) * sunUp;

                // The moons, small to large so the Large Moon is drawn over the others.
                col = Moon(col, v, _BakSkyMoonDir2, _BakSkyMoonPhase.z, _BakSkyMoonBright.z, night, _Moon2_TexelSize.z, TEXTURE2D_ARGS(_Moon2, sampler_Moon2));
                col = Moon(col, v, _BakSkyMoonDir1, _BakSkyMoonPhase.y, _BakSkyMoonBright.y, night, _Moon1_TexelSize.z, TEXTURE2D_ARGS(_Moon1, sampler_Moon1));
                col = Moon(col, v, _BakSkyMoonDir0, _BakSkyMoonPhase.x, _BakSkyMoonBright.x, night, _Moon0_TexelSize.z, TEXTURE2D_ARGS(_Moon0, sampler_Moon0));
                float3 m0 = normalize(_BakSkyMoonDir0.xyz);
                float dm = acos(clamp(dot(v, m0), -1.0, 1.0)) - _BakSkyMoonDir0.w;
                col += _BakSkyMoonLight.rgb * 0.05 * _BakSkyMoonLight.a * night * exp(-max(dm, 0.0) * 60.0) * step(-0.05, m0.y);

                // Clouds, projected on a flat layer overhead; they drift in real time (wind), while
                // the cover follows the game day (SkyClock.CloudCover).
                float alpha = 0.0;
                if (v.y > 0.02) {
                    float2 uv = v.xz / (v.y + 0.08);
                    float t = _Time.y;
                    float cover = _BakSkyCloud.x;
                #if defined(SHADER_API_MOBILE)
                    float coverNoise = SAMPLE_TEXTURE2D(_CloudsB, sampler_CloudsB, uv * 0.07 + t * 0.0006).r;
                #else
                    float coverNoise = Fbm(uv * 0.25 + t * 0.002);
                #endif
                    float mask = smoothstep(1.0 - cover - 0.15, 1.0 - cover + 0.15, coverNoise);
                    float4 a = SAMPLE_TEXTURE2D(_CloudsA, sampler_CloudsA, uv * 0.35 + float2(0.004, 0.002) * t);
                    float density = a.r * mask;
                    float thick = a.g;
                    if (_BakSkyCloud.y > 1.5) {
                        float4 b = SAMPLE_TEXTURE2D(_CloudsB, sampler_CloudsB, uv * 0.12 + float2(0.0015, -0.001) * t);
                        float mb = smoothstep(1.0 - cover - 0.25, 1.0 - cover + 0.25, Fbm(uv * 0.11 + 7.3 + t * 0.001));
                        float db = b.r * mb * 0.6 * (1.0 - density);
                        density += db;
                        thick = lerp(thick, b.g * 0.5, db / max(density, 1e-3));
                    }
                    float mu = dot(v, s);
                #if defined(SHADER_API_MOBILE)
                    float3 ambient = gradient * 0.6;
                #else
                    float3 ambient = SkyRadiance(normalize(float3(v.x, 0.15, v.z))) * 0.6;
                #endif
                    float3 sunlit = _BakSkySunColor.rgb * sunUp * (0.5 + 0.5 * pow(saturate(mu), 4.0)) * (1.0 - 0.5 * thick);
                    float3 cloudCol = ambient + sunlit * 0.8 + float3(0.02, 0.025, 0.04) * night;
                    alpha = saturate(density) * smoothstep(0.02, 0.12, v.y);
                    col = lerp(col, cloudCol, alpha);
                }

                // The dome stands in for the sky pen, so it takes the pen's lighting: dark at night,
                // moons and clouds included, as the original darkens every palette entry.
                return half4(ApplyBakLighting(col, 0.0) + starLight * (1.0 - alpha), 1.0);
            }
            ENDHLSL
        }
    }
}
