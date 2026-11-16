Shader "TowerDuel/ContactShadow"
{
    Properties { _Opacity("Opacity", Range(0,1)) = 1 }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent-30" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            Cull Off ZWrite Off Blend SrcAlpha OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float4 positionOS:POSITION; float2 uv:TEXCOORD0; };
            struct Varyings { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; };
            CBUFFER_START(UnityPerMaterial)
                half _Opacity;
            CBUFFER_END
            Varyings Vert(Attributes input)
            {
                Varyings output; output.positionCS=TransformObjectToHClip(input.positionOS.xyz);
                output.uv=input.uv; return output;
            }
            half4 Frag(Varyings input):SV_Target
            {
                half2 offset=input.uv*2-1;
                half feather=saturate(1-dot(offset,offset));
                return half4(.025h,.03h,.045h,feather*feather*.32h*_Opacity);
            }
            ENDHLSL
        }
    }
}
