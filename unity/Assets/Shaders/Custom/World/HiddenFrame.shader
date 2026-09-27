// Draws nothing at all.
//
// *** THIS EXISTS BECAUSE A NULL MATERIAL DOES NOT HIDE A SUB-MESH. *** Unity draws a sub-mesh whose
// material is null with its magenta error material, so WorldMeshFrames' way of hiding the inactive
// frames of a flip-book entity displayed them instead — gate2's rift showed a large magenta blob
// between its posts, and the combat grid marker painted a magenta border around its live frame.
//
// A frame is hidden by giving it this material: the geometry is still submitted, and then discarded
// with no colour and no depth written. Hiding by emptying the sub-mesh would be cheaper at draw
// time, but world entities are built once as a TEMPLATE and Instantiated per placement, so the mesh
// is shared and editing it would move every clone's frame at once.
Shader "BakAgain/HiddenFrame"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            ColorMask 0
            ZWrite Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            float4 vert(float4 positionOS : POSITION) : SV_POSITION
            {
                return TransformObjectToHClip(positionOS.xyz);
            }

            half4 frag() : SV_Target
            {
                discard;
                return 0;
            }
            ENDHLSL
        }
    }
}
