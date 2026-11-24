Shader "Hidden/TowerDuel/CharacterIdlePreview"
{
    Properties
    {
        _MainTex ("Sprite atlas", 2D) = "white" {}
        _FrameRect ("UV rect", Vector) = (0,0,1,1)
        _Pose ("Time, foot, height, flight", Vector) = (0,.1,.8,0)
        _AuthoredIdle ("Authored idle loop", Float) = 0
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float4 _FrameRect, _Pose, _MainTex_TexelSize;
            float _AuthoredIdle;
            fixed4 frag(v2f_img i) : SV_Target
            {
                float t=_Pose.x;
                float flight=_Pose.w;
                float foot=_Pose.y;
                float body=max(.25,_Pose.z);
                // Reframe each unit by its actual authored height, not atlas size.
                float scale=clamp(.69/body,.78,1.12);
                float targetFoot=lerp(.24,.34,flight)+flight*.025*sin(t*4.5);
                float2 uv=float2(.5+(i.uv.x-.5)/scale,foot+(i.uv.y-targetFoot)/scale);
                float h=saturate((uv.y-foot)/body);
                // Lower legs are fixed; torso and upper silhouette breathe/sway.
                // Different offsets across the pose make this a local deformation,
                // not a uniform resize or a rotation through the eight directions.
                float weight=smoothstep(.12,.90,h)*(1-flight)*(1-_AuthoredIdle);
                float sway=sin(t*2.4)*.016*weight;
                float breathe=sin(t*3.4)*.017*weight;
                float top=smoothstep(.72,1,h)*(1-flight)*(1-_AuthoredIdle);
                uv.x-=sway+sin(t*4.0+uv.x*5.0)*.008*top;
                uv.y-=breathe+sin(t*2.4+uv.x*3.0)*.004*top;
                if(any(uv<.002)||any(uv>.998))return fixed4(0,0,0,0);
                // Sharper, subpixel-smoothed sampling of the authored tile.
                // Clamp within the chosen frame so neighboring atlas cells cannot bleed.
                float2 atlasSize=_MainTex_TexelSize.zw;
                float2 p=(_FrameRect.xy+uv*_FrameRect.zw)*atlasSize-.5;
                float2 f=frac(p);
                float2 sampleUV=(floor(p)+smoothstep(.18,.82,f)+.5)*_MainTex_TexelSize.xy;
                float2 halfTexel=_MainTex_TexelSize.xy*.5;
                sampleUV=clamp(sampleUV,_FrameRect.xy+halfTexel,_FrameRect.xy+_FrameRect.zw-halfTexel);
                return tex2D(_MainTex,sampleUV);
            }
            ENDCG
        }
    }
    Fallback Off
}
