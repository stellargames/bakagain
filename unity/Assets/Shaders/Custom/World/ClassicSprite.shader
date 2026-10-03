Shader "BakAgain/ClassicSprite" {
    Properties {
        _MainTex ("Sprite Texture", 2D) = "white" {}
        _AlphaCutoff ("Alpha Cutoff", Float) = 0.01
        _FlashColor ("Flash Colour", Color) = (0,0,0,0)
    }
    SubShader {
        Tags { "RenderType"="TransparentCutout" "RenderPipeline"="UniversalPipeline" "Queue"="AlphaTest" }
        Pass {
            Name "ClassicSprite"
            Tags { "LightMode"="UniversalForward" }
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Assets/Shaders/Custom/World/BakLighting.hlsl"

            struct Attributes {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float fogCoord : TEXCOORD1;
                float spriteFog : TEXCOORD2;
            };

            CBUFFER_START(UnityPerMaterial)
                float _AlphaCutoff;
                // One actor's tint (a spell hit's RED/WHITE/BLUE.RMP remap, WORLDRND.C:208), set per
                // renderer through a property block by SpellVfx. a = 0 leaves the sprite alone.
                float4 _FlashColor;
            CBUFFER_END

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            // The zone's own sprite haze range (Z##DEF.DAT, TASK-434), set as globals by the render
            // profile. Only sprites fade on this range, as only sprites are remapped in the original;
            // with no range set (end <= start) the shared URP fog applies as before.
            float _BakSpriteFogStart;
            float _BakSpriteFogEnd;

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
                float3 camRight = UNITY_MATRIX_V[0].xyz;
                camRight.y = 0;
                // Degenerate only if the camera is rolled onto its side, which the game never
                // does; fall back to world X rather than hand normalize() a zero vector.
                float rightLen = length(camRight);
                camRight = rightLen > 1e-4 ? camRight / rightLen : float3(1, 0, 0);
                float3 camUp = float3(0, 1, 0);

                // Object-to-world lossy scale (columns of M are scaled basis vectors)
                float scaleX = length(float3(UNITY_MATRIX_M._m00, UNITY_MATRIX_M._m10, UNITY_MATRIX_M._m20));
                float scaleY = length(float3(UNITY_MATRIX_M._m01, UNITY_MATRIX_M._m11, UNITY_MATRIX_M._m21));
                // A length has no sign, so a mirrored sprite (negative x scale: the left-facing
                // octants, TASK-694) drew unmirrored. The matrix's handedness carries the flip.
                if (determinant((float3x3)UNITY_MATRIX_M) < 0) scaleX = -scaleX;

                // Offset vertex from pivot along camera plane, honoring transform scale
                float3 billboardPos = worldPos
                    + camRight * input.positionOS.x * scaleX
                    + camUp * input.positionOS.y * scaleY;

                output.positionCS = TransformWorldToHClip(billboardPos);
                output.uv = input.uv;
                output.fogCoord = ComputeFogFactor(output.positionCS.z);
                float3 toSprite = worldPos - _WorldSpaceCameraPos;
                float spriteDistance = length(toSprite.xz);
                output.spriteFog = _BakSpriteFogEnd > _BakSpriteFogStart
                    ? saturate((spriteDistance - _BakSpriteFogStart) / (_BakSpriteFogEnd - _BakSpriteFogStart))
                    : -1;
                return output;
            }

            half4 frag(Varyings input) : SV_Target {
                half4 color = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                clip(color.a - _AlphaCutoff);
                color.rgb = lerp(color.rgb, _FlashColor.rgb, _FlashColor.a);
                // The sprite haze when a range is set, else URP distance fog.
                color.rgb = input.spriteFog >= 0
                    ? lerp(color.rgb, unity_FogColor.rgb, input.spriteFog)
                    : MixFog(color.rgb, input.fogCoord);
                // Lighting AFTER fog: the original darkens the whole palette, the fog pens included,
                // so distant fogged terrain darkens at night like everything else (TASK-761).
                color.rgb = ApplyBakLighting(color.rgb);
                return color;
            }
            ENDHLSL
        }
    }
}
