Shader "TowerDuel/BakedSpriteShadow"
{
    Properties
    {
        [PerRendererData] _MainTex("Baked shadow frame",2D)="white"{}
        _B3ShadowTex("Environment overlay texture",2D)="white"{}
        _UseB3ShadowTex("Bind environment overlay",Float)=0
        _Opacity("Opacity",Range(0,1))=1
    }
    SubShader
    {
        Tags {"RenderPipeline"="UniversalPipeline" "Queue"="Transparent-40" "RenderType"="Transparent"}
        Pass
        {
            Tags {"LightMode"="SRPDefaultUnlit"}
            Cull Off ZWrite Off Blend SrcAlpha OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes{float4 positionOS:POSITION;float2 uv:TEXCOORD0;};
            struct Varyings{float4 positionCS:SV_POSITION;float2 uv:TEXCOORD0;};
            TEXTURE2D(_MainTex);SAMPLER(sampler_MainTex);
            TEXTURE2D(_B3ShadowTex);SAMPLER(sampler_B3ShadowTex);
            CBUFFER_START(UnityPerMaterial)
                half _Opacity;
                half _UseB3ShadowTex;
            CBUFFER_END
            Varyings Vert(Attributes i){Varyings o;o.positionCS=TransformObjectToHClip(i.positionOS.xyz);o.uv=i.uv;return o;}
            half4 Frag(Varyings i):SV_Target
            {
                half4 color=_UseB3ShadowTex>.5h
                    ? SAMPLE_TEXTURE2D(_B3ShadowTex,sampler_B3ShadowTex,i.uv)
                    : SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,i.uv);
                color.a*=_Opacity;return color;
            }
            ENDHLSL
        }
    }
}
