Shader "Rumblebound/Slash Flipbook"
{
    Properties
    {
        _MainTex ("Artist animation atlas", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _ArcCos ("Melee half-angle cosine", Float) = 0.258819
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" "RenderPipeline"="UniversalPipeline" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off
        Pass
        {
            Name "SlashUnlit"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
            float4 _MainTex_ST;
            half4 _Color;
            float _ArcCos;
            CBUFFER_END
            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 vertex : SV_POSITION; float2 atlas : TEXCOORD0; float2 local : TEXCOORD1; };
            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = TransformObjectToHClip(v.vertex.xyz);
                o.atlas = TRANSFORM_TEX(v.uv, _MainTex);
                o.local = v.uv - .5;
                return o;
            }
            half4 frag(v2f i) : SV_Target
            {
                clip(i.local.y - length(i.local) * _ArcCos);
                return SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.atlas) * _Color;
            }
            ENDHLSL
        }
    }
}
