// Additive glow for combat spell effects (SpellVfx): sparks, motes, twinkles, lightning, the wire box
// and halos. The original plots single pixels and 1-px lines in one palette pen; the port draws the
// same points and lines as soft, additive light so they read at modern resolutions (see
// project_graphics_are_being_improved_not_matched). Vertex colour carries the pen's RGB.
//
// Lives under Resources/ so Shader.Find resolves it in a player build without a material asset.
Shader "BakAgain/SpellGlow" {
    Properties {
        _MainTex ("Glow", 2D) = "white" {}
        _Billboard ("Billboard points (1) or plain geometry (0)", Float) = 0
        // One = additive light (sparks, bolts); 10 = OneMinusSrcAlpha, for the sparks' dark shadows.
        _DstBlend ("Destination blend", Float) = 1
    }
    SubShader {
        Tags { "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" }
        Pass {
            Name "SpellGlow"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha [_DstBlend]
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                // A point's corner offset in world units: GlowCloud writes the quad's centre as the
                // position and this as the offset, so the quad faces the camera. Only the points'
                // material sets _Billboard: a LineRenderer fills this channel with data of its own,
                // and reading it as an offset smeared every edge into a crossing ribbon.
                float2 corner : TEXCOORD1;
                half4 color : COLOR;
            };

            struct Varyings {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
            };

            CBUFFER_START(UnityPerMaterial)
                float _Billboard;
            CBUFFER_END

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            Varyings vert(Attributes input) {
                Varyings output;
                float3 centre = TransformObjectToWorld(input.positionOS.xyz);
                float2 corner = input.corner * _Billboard;
                float3 world = centre + UNITY_MATRIX_V[0].xyz * corner.x
                                      + UNITY_MATRIX_V[1].xyz * corner.y;
                output.positionCS = TransformWorldToHClip(world);
                output.uv = input.uv;
                output.color = input.color;
                return output;
            }

            half4 frag(Varyings input) : SV_Target {
                half4 glow = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                return glow * input.color;
            }
            ENDHLSL
        }
    }
}
