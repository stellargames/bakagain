Shader "BakAgain/EnhancedSky" {
    // The Enhanced sky dome (spec 2026-10-15-enhanced-sky-design): scattering gradient, sun disc,
    // stars, three moons and two cloud layers, on a sphere centred on the camera. Drawn before the
    // mountain quad (Background-1) with no depth write, so everything else lands in front of it.
    // Under Resources so Shader.Find reaches it in a build. All time and position rules are in
    // SkyClock (GameData); EnhancedSky.cs hands them over as globals.
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
            float4 _BakSkyCloud;       // x = cover 0..1, y = game hours (unused by drift), z = layers (1|2)
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
            float3 Moon(float3 col, float3 v, float4 md, float phase, float bright, float night,
                        TEXTURE2D_PARAM(tex, samp)) {
                float3 m = normalize(md.xyz);
                float r = md.w;
                float cosD = dot(v, m);
                if (cosD < cos(r) || m.y < -0.05) return col;
                float3 right = normalize(abs(m.y) > 0.99 ? float3(1, 0, 0) : cross(float3(0, 1, 0), m));
                float3 up = cross(m, right);
                float2 uv = float2(dot(v, right), dot(v, up)) / sin(r);
                float4 t = SAMPLE_TEXTURE2D(tex, samp, uv * 0.5 + 0.5);
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

                // Stars: a hash per cell of a field turned about a pole 40 degrees up in the north.
                float3 pole = float3(0.0, 0.6427876, 0.7660444);
                float3 rv = Rotate(v, pole, _BakSkyStarRot);
                float h = Hash31(floor(rv * 300.0));
                float star = step(0.9985, h) * pow(frac(h * 71.3), 4.0) * 3.0;
                col += star * night * saturate(v.y * 8.0);

                // The sun: an HDR disc so Lighting's bloom catches it, plus a tight glow.
                float d = acos(clamp(dot(v, s), -1.0, 1.0));
                float aboveGround = step(-0.02, s.y);
                col += _BakSkySunColor.rgb * 30.0 * smoothstep(0.0095, 0.0085, d) * aboveGround;
                col += _BakSkySunColor.rgb * 0.25 * exp(-d * 18.0) * sunUp;

                // The moons, small to large so the Large Moon is drawn over the others.
                col = Moon(col, v, _BakSkyMoonDir2, _BakSkyMoonPhase.z, _BakSkyMoonBright.z, night, TEXTURE2D_ARGS(_Moon2, sampler_Moon2));
                col = Moon(col, v, _BakSkyMoonDir1, _BakSkyMoonPhase.y, _BakSkyMoonBright.y, night, TEXTURE2D_ARGS(_Moon1, sampler_Moon1));
                col = Moon(col, v, _BakSkyMoonDir0, _BakSkyMoonPhase.x, _BakSkyMoonBright.x, night, TEXTURE2D_ARGS(_Moon0, sampler_Moon0));
                float3 m0 = normalize(_BakSkyMoonDir0.xyz);
                float dm = acos(clamp(dot(v, m0), -1.0, 1.0)) - _BakSkyMoonDir0.w;
                float fullness = 1.0 - abs(_BakSkyMoonPhase.x - 0.5) * 2.0;
                col += float3(0.55, 0.65, 1.0) * 0.05 * fullness * night * exp(-max(dm, 0.0) * 60.0) * step(-0.05, m0.y);

                // Clouds, projected on a flat layer overhead; they drift in real time (wind), while
                // the cover follows the game day (SkyClock.CloudCover).
                if (v.y > 0.02) {
                    float2 uv = v.xz / (v.y + 0.08);
                    float t = _Time.y;
                    float cover = _BakSkyCloud.x;
                    float mask = smoothstep(1.0 - cover - 0.15, 1.0 - cover + 0.15, Fbm(uv * 0.25 + t * 0.002));
                    float4 a = SAMPLE_TEXTURE2D(_CloudsA, sampler_CloudsA, uv * 0.35 + float2(0.004, 0.002) * t);
                    float density = a.r * mask;
                    float thick = a.g;
                    if (_BakSkyCloud.z > 1.5) {
                        float4 b = SAMPLE_TEXTURE2D(_CloudsB, sampler_CloudsB, uv * 0.12 + float2(0.0015, -0.001) * t);
                        float mb = smoothstep(1.0 - cover - 0.25, 1.0 - cover + 0.25, Fbm(uv * 0.11 + 7.3 + t * 0.001));
                        float db = b.r * mb * 0.6 * (1.0 - density);
                        density += db;
                        thick = lerp(thick, b.g * 0.5, db / max(density, 1e-3));
                    }
                    float mu = dot(v, s);
                    float3 ambient = SkyRadiance(normalize(float3(v.x, 0.15, v.z))) * 0.6;
                    float3 sunlit = _BakSkySunColor.rgb * sunUp * (0.5 + 0.5 * pow(saturate(mu), 4.0)) * (1.0 - 0.5 * thick);
                    float3 cloudCol = ambient + sunlit * 0.8 + float3(0.02, 0.025, 0.04) * night;
                    float alpha = saturate(density) * smoothstep(0.02, 0.12, v.y);
                    col = lerp(col, cloudCol, alpha);
                }

                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }
}
