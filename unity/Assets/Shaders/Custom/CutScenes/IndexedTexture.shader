Shader "Custom/CutScenes/IndexedTexture"
{
    Properties
    {
        _MainTex ("Index Texture", 2D) = "white" {}
        _PaletteTex ("Palette Texture", 2D) = "white" {}
    }
    SubShader
    {
        Tags
        {
            "RenderType"="Transparent"
            "Queue"="Transparent"
        }
        LOD 100

        // No blending - we'll handle it manually
        Blend Off
        ZWrite Off

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
            sampler2D _PaletteTex;

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // Sample the index texture - R channel contains the raw index (0-1)
                float4 indexColor = tex2D(_MainTex, i.uv);

                // Use R channel directly as the index (from 0-1 to 0-255)
                float rawIndex = indexColor.r * 255.0;

                // Use original alpha to determine transparency
                if (indexColor.a < 0.01)
                    discard;

                // Map the raw index to palette UV coordinates (256x1 texture)
                float2 paletteUV = float2((rawIndex + 0.5) / 256.0, 0.5);

                // Sample the palette texture
                fixed4 col = tex2D(_PaletteTex, paletteUV);

                return col;
            }
            ENDCG
        }
    }
}