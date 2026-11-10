// URP adapter for the selected official legacy particle textures and colors.
// Additive and soft-additive retain their distinct blend equations and vertex
// alpha controls lifetime fading. No depth texture or lighting is required.
Shader "Rumblebound/Legacy Particle URP"
{
    Properties
    {
        [MainTexture] _MainTex ("Particle texture", 2D) = "white" {}
        [MainColor] _TintColor ("Authored tint", Color) = (.5,.5,.5,.5)
        [HideInInspector] _SrcBlend ("Source blend", Float) = 5
        [HideInInspector] _DstBlend ("Destination blend", Float) = 1
        [HideInInspector] _SoftAdditive ("Soft additive", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" "RenderPipeline"="UniversalPipeline" }
        Blend [_SrcBlend] [_DstBlend]
        ColorMask RGB
        ZWrite Off
        Cull Off
        Pass
        {
            Name "ParticleUnlit"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
            float4 _MainTex_ST;
            half4 _TintColor;
            half _SoftAdditive;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; half4 color : COLOR; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionHCS : SV_POSITION; half4 color : COLOR; float2 uv : TEXCOORD0; half fog : TEXCOORD1; };
            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                output.color = input.color;
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                output.fog = ComputeFogFactor(output.positionHCS.z);
                return output;
            }
            half4 frag(Varyings input) : SV_Target
            {
                half4 color = 2 * input.color * _TintColor * SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                color.a = saturate(color.a);
                // OneMinusDstColor blending does not consume source alpha.
                // Premultiply it here to preserve the authored particle fade.
                if (_SoftAdditive > .5h) color.rgb *= color.a;
                color.rgb = MixFogColor(color.rgb, half3(0,0,0), input.fog);
                return color;
            }
            ENDHLSL
        }
    }
}
