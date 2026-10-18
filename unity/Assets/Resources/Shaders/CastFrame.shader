// The Enhanced cast screen's stone frame (TASK-878), drawn as ONE piece the size it is shown at:
// FRAME.SCX's frame, a band of light stone round the outside and a bar down the middle where the
// bench and the parchment meet, mitred at the corners, worn round along its length (convex across it)
// and lit from the top-left, with the soft shadow it throws on the marble. Blitted into a
// RenderTexture whenever the layout changes size; everything is in that texture's pixels.
//
// The shape is a signed distance field (negative inside the stone), so the corners and the joins
// with the middle bar fall out of the distance itself, and its depth is what makes the bar convex.
//
// Lives under Resources/ so Shader.Find resolves it in a player build without a material asset.
Shader "BakAgain/CastFrame" {
    Properties {
        _MainTex ("Stone", 2D) = "white" {}
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

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            float4 _Size;      // xy: the texture in pixels
            float4 _Outer;     // the frame's outer edge: x0, y0, x1, y1 (pixels, y down)
            float4 _Bar;       // x: band width, y: middle bar centre x (< 0: none)
            float4 _Tile;      // xy: one tile of the stone texture, in pixels
            float4 _Shadow;    // xy: offset, z: softness, w: strength

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings vert(Attributes v) {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv;
                return o;
            }

            float sdBox(float2 p, float2 lo, float2 hi) {
                float2 c = (lo + hi) * 0.5, e = (hi - lo) * 0.5;
                float2 d = abs(p - c) - e;
                return length(max(d, 0.0)) + min(max(d.x, d.y), 0.0);
            }

            // Negative inside the stone.
            float sdFrame(float2 p) {
                float b = _Bar.x;
                float ring = max(sdBox(p, _Outer.xy, _Outer.zw), -sdBox(p, _Outer.xy + b, _Outer.zw - b));
                if (_Bar.y >= 0.0) {
                    float mid = sdBox(p, float2(_Bar.y - b * 0.5, _Outer.y), float2(_Bar.y + b * 0.5, _Outer.w));
                    ring = min(ring, mid);
                }
                return ring;
            }

            // Height of the worn top: 0 at the edges, 1 along the middle of the band.
            float height(float2 p) {
                float t = saturate(-sdFrame(p) / (_Bar.x * 0.5));
                return pow(saturate(1.0 - pow(1.0 - t, 2.2)), 0.6);
            }

            half4 frag(Varyings i) : SV_Target {
                float2 p = float2(i.uv.x, 1.0 - i.uv.y) * _Size.xy;
                float sd = sdFrame(p);
                if (sd <= 0.0) {
                    float h = height(p);
                    float relief = _Bar.x * 0.18;
                    float gx = (height(p + float2(1, 0)) - height(p - float2(1, 0))) * 0.5 * relief;
                    float gy = (height(p + float2(0, 1)) - height(p - float2(0, 1))) * 0.5 * relief;
                    float3 n = normalize(float3(-gx, -gy, 1.0));
                    float3 l = normalize(float3(-0.55, -0.65, 0.95));
                    float lambert = saturate(dot(n, l));
                    float flat = l.z;
                    float3 albedo = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, p / _Tile.xy).rgb;
                    float ambient = 0.4;
                    float3 c = albedo * (ambient + (1.0 - ambient) * lambert) / (ambient + (1.0 - ambient) * flat);
                    c *= 0.55 + 0.45 * saturate(-sd / (_Bar.x * 0.12));     // the edges sink into shade
                    return half4(c, 1.0);
                }
                float s = sdFrame(p - _Shadow.xy);
                float a = _Shadow.w * (1.0 - smoothstep(-_Shadow.z * 0.5, _Shadow.z, s));
                return half4(0, 0, 0, a);
            }
            ENDHLSL
        }
    }
}
