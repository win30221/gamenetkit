Shader "TowerDuel/ProjectedSpriteShadow"
{
    Properties
    {
        [PerRendererData] _MainTex("Sprite", 2D) = "white" {}
        _Opacity("Opacity", Range(0,1)) = 1
        _GroundHeight("Foot plane", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent-40" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            Cull Off ZWrite Off Blend SrcAlpha OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RealtimeLights.hlsl"
            struct Attributes { float4 positionOS:POSITION; float2 uv:TEXCOORD0; };
            struct Varyings { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; };
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex); float4 _MainTex_TexelSize;
            CBUFFER_START(UnityPerMaterial)
                half _Opacity; float _GroundHeight;
            CBUFFER_END
            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 positionWS=TransformObjectToWorld(input.positionOS.xyz);
                float3 ray=-GetMainLight().direction;
                // Project the transparent padding below the pivot as well. A
                // vertex clamp would warp interpolated UVs and lift the boots.
                positionWS+=ray*((positionWS.y-_GroundHeight)/max(.1,-ray.y));
                positionWS.y=_GroundHeight;
                output.positionCS=TransformWorldToHClip(positionWS);output.uv=input.uv;return output;
            }
            half4 Frag(Varyings input):SV_Target
            {
                float2 pixel=_MainTex_TexelSize.xy*.65;
                half alpha=SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,input.uv).a*.4h;
                alpha+=SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,input.uv+float2(pixel.x,0)).a*.15h;
                alpha+=SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,input.uv-float2(pixel.x,0)).a*.15h;
                alpha+=SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,input.uv+float2(0,pixel.y)).a*.15h;
                alpha+=SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,input.uv-float2(0,pixel.y)).a*.15h;
                half lightStrength=saturate(dot(GetMainLight().color,half3(.2126,.7152,.0722)));
                return half4(.025h,.03h,.045h,alpha*_Opacity*.43h*lightStrength);
            }
            ENDHLSL
        }
    }
}
