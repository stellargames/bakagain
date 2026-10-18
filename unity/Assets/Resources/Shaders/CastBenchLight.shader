// The Enhanced cast screen's marble bench, lit by the spell symbols hovering over it (TASK-878).
// Blitted into a RenderTexture whenever the lights change: the marble's own colour times the light
// that reaches it, so its veins catch the glow rather than a tinted veil lying over them. A light is
// a point a little above the bench; its falloff is smooth and it never darkens (ambient stays 1).
//
// Lives under Resources/ so Shader.Find resolves it in a player build without a material asset.
Shader "BakAgain/CastBenchLight" {
    Properties {
        _MainTex ("Marble", 2D) = "white" {}
    }
    SubShader {
        Tags { "RenderPipeline"="UniversalPipeline" }
        Pass {
            ZTest Always
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            #define MAX_LIGHTS 16

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            float4 _Tile;                    // xy: marble repeats across the bench, zw: offset
            float _Gain;                     // how far a full-strength light lifts the marble
            int _LightCount;
            float4 _LightPos[MAX_LIGHTS];    // xy: bench uv, z: radius in uv, w: strength
            float4 _LightColor[MAX_LIGHTS];  // linear rgb
            float4 _Ring;                    // xy: ring centre (uv), z: inner radius, w: where the band's lip ends

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings vert(Attributes v) {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv;
                return o;
            }

            half4 frag(Varyings i) : SV_Target {
                float2 uv = i.uv;
                // The blit's uv runs bottom-up; the bench is laid out top-down like the UI.
                float2 bench = float2(uv.x, 1.0 - uv.y);
                float3 albedo = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, bench * _Tile.xy + _Tile.zw).rgb;
                // The symbols hover inside the ring; its stone band stops their light reaching the
                // bench beyond it.
                float reach = 1.0 - smoothstep(_Ring.z, _Ring.w, length(bench - _Ring.xy));
                float3 light = 1.0;
                float3 sheen = 0.0;
                for (int k = 0; k < _LightCount; k++) {
                    float d = length(bench - _LightPos[k].xy) / _LightPos[k].z;
                    float f = reach * _LightPos[k].w * (exp(-d * d * 2.5) + 0.12 * exp(-d * 1.8));
                    light += _LightColor[k].rgb * f * _Gain;
                    sheen += _LightColor[k].rgb * f * f * 0.04;
                }
                return half4(albedo * light + sheen, 1.0);
            }
            ENDHLSL
        }
    }
}
