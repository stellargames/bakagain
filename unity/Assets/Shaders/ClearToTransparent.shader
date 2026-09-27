// Writes (0,0,0,0) over whatever it covers — a hole, not a blend.
//
// Unlit/Transparent CANNOT do this. It blends SrcAlpha OneMinusSrcAlpha, so drawing a fully
// transparent pixel through it contributes nothing and leaves the destination exactly as it was.
// DrawBorder used it to "create a transparent hole" and punched none (TASK-204).
//
// `Blend Zero Zero` ignores both source and destination and stores zero, which is what clearing
// means. ZWrite is off because these are 2D buffer writes with no depth.
Shader "Custom/ClearToTransparent"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
    }
    SubShader
    {
        Tags
        {
            "RenderType"="Transparent"
            "Queue"="Transparent"
        }

        Blend Zero Zero
        ZWrite Off
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            // The blend discards this, but a fragment shader must return something.
            float4 frag(v2f i) : SV_Target
            {
                return float4(0, 0, 0, 0);
            }
            ENDCG
        }
    }
}
