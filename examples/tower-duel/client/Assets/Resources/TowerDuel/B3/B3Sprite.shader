Shader "TowerDuel/B3Sprite"
{
    Properties
    {
        [PerRendererData] _MainTex("Sprite", 2D) = "white" {}
        _BaseColor("Tint", Color) = (1,1,1,1)
        _Opacity("Opacity", Range(0,1)) = 1
        _Enemy("Red team", Float) = 0
        _TeamMaskTop("Team recolor upper limit", Float) = 2
        _Hit("Red damage flash", Range(0,1)) = 0
        _Baked("Use authored fixed lighting",Float)=0
        _SpriteVolume("Body radius, center and height", Vector) = (.25,.38,.46,0)
        _Cutoff("Alpha cutoff", Range(0,1)) = .08
        _ZWrite("Depth write", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType"="TransparentCutout" "Queue"="AlphaTest" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Cull Off ZWrite [_ZWrite] Blend SrcAlpha OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            struct Attributes { float4 positionOS:POSITION; float2 uv:TEXCOORD0; half4 color:COLOR; };
            struct Varyings { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; half4 color:COLOR;
                float3 positionWS:TEXCOORD1; float2 localXY:TEXCOORD2; float localY:TEXCOORD3; };
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                float4 _SpriteVolume;
                half _Opacity, _Enemy, _Cutoff, _Hit, _TeamMaskTop, _Baked;
            CBUFFER_END
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS=TransformObjectToHClip(input.positionOS.xyz);
                output.positionWS=TransformObjectToWorld(input.positionOS.xyz);
                output.localY=input.positionOS.y;
                output.localXY=input.positionOS.xy;
                output.uv=input.uv; output.color=input.color;
                return output;
            }
            half4 Frag(Varyings input):SV_Target
            {
                half4 color=SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,input.uv);
                half blue=step(.28h,color.b)*step(.10h,color.b-color.r)*step(.04h,color.b-color.g)*step(color.r*.9h,color.g);
                half value=max(color.r,max(color.g,color.b));
                color.rgb=lerp(color.rgb,value*half3(1,.22h,.15h),blue*_Enemy*step(input.localY,_TeamMaskTop));
                // A red overlay stays readable on blue and green equipment;
                // retain the darkest ink rather than multiplying it into black.
                color.rgb=lerp(color.rgb,half3(1,.10h,.08h)*max(.4h,value),_Hit*.8h*smoothstep(.07h,.18h,value));
                color*=input.color*_BaseColor; color.a*=_Opacity;
                clip(color.a-_Cutoff);
                if(_Baked>.5h)
                {
                    // Self-lighting is in the atlas. Only actual environmental
                    // occluders remain live, without re-shading the whole body.
                    Light environment=GetMainLight();
                    float4 coordinate=TransformWorldToShadowCoord(input.positionWS+environment.direction*.025);
                    environment=GetMainLight(coordinate,input.positionWS,half4(1,1,1,1));
                    color.rgb*=lerp(.48h,1.h,environment.shadowAttenuation);
                    return color;
                }
                // Evaluate the curved volume per pixel, inside the painted body
                // rather than across the mostly empty atlas tile. The elevated
                // game view sees its upper surface as well as its front.
                half2 curved=clamp(half2(input.localXY.x/max(.01,_SpriteVolume.x),
                    (input.localXY.y-_SpriteVolume.y)/max(.01,_SpriteVolume.z)),-.95h,.95h);
                half3 normalOS=normalize(half3(curved.x,.85h+curved.y*.55h,
                    -.5h*sqrt(max(.12h,1-curved.x*curved.x))));
                half3 normalWS=TransformObjectToWorldNormal(normalOS);
                Light sun=GetMainLight();
                // Move the receiver a little toward the light to avoid the flat
                // alpha card shadowing itself; building shadows still reach it.
                float4 shadowCoord=TransformWorldToShadowCoord(input.positionWS+sun.direction*.025);
                sun=GetMainLight(shadowCoord,input.positionWS,half4(1,1,1,1));
                half facing=saturate(dot(normalWS,sun.direction));
                half cel=lerp(.035h,.60h,smoothstep(.06h,.16h,facing));
                cel=lerp(cel,.90h,smoothstep(.36h,.48h,facing));
                half3 illumination=SampleSH(normalWS)*.45h + sun.color*cel*sun.distanceAttenuation*sun.shadowAttenuation;
                color.rgb*=illumination;
                return color;
            }
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            Cull Off ZWrite On ZTest LEqual ColorMask 0
            HLSLPROGRAM
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            struct ShadowAttributes { float4 positionOS:POSITION; float2 uv:TEXCOORD0; };
            struct ShadowVaryings { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; };
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            float3 _LightDirection, _LightPosition;
            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                float4 _SpriteVolume;
                half _Opacity, _Enemy, _Cutoff, _Hit, _TeamMaskTop, _Baked;
            CBUFFER_END
            ShadowVaryings ShadowVert(ShadowAttributes input)
            {
                ShadowVaryings output;
                float3 positionWS=TransformObjectToWorld(input.positionOS.xyz);
                // A two-sided alpha card needs no mesh-normal bias. That bias
                // displaces its entire silhouette and detaches it from the feet.
                output.positionCS=ApplyShadowClamping(TransformWorldToHClip(positionWS));
                output.uv=input.uv;return output;
            }
            half4 ShadowFrag(ShadowVaryings input):SV_Target
            {
                half alpha=SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,input.uv).a*_BaseColor.a;
                clip(alpha-_Cutoff);
                if(_Opacity<.999h)
                {
                    float dither=frac(sin(dot(input.positionCS.xy,float2(12.9898,78.233)))*43758.5453);
                    clip(_Opacity-.0001h-dither);
                }
                return 0;
            }
            ENDHLSL
        }
    }
}
