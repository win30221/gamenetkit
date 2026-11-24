Shader "Hidden/TowerDuel/CharacterIdlePreview"
{
    Properties
    {
        _MainTex ("Sprite atlas", 2D) = "white" {}
        _FrameRect ("UV rect", Vector) = (0,0,1,1)
        _Pose ("Time, foot, height, flight", Vector) = (0,.1,.8,0)
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
            float4 _FrameRect, _Pose;
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
                float weight=smoothstep(.12,.90,h)*(1-flight);
                float sway=sin(t*2.4)*.016*weight;
                float breathe=sin(t*3.4)*.017*weight;
                float top=smoothstep(.72,1,h)*(1-flight);
                uv.x-=sway+sin(t*4.0+uv.x*5.0)*.008*top;
                uv.y-=breathe+sin(t*2.4+uv.x*3.0)*.004*top;
                if(any(uv<.002)||any(uv>.998))return fixed4(0,0,0,0);
                return tex2D(_MainTex,_FrameRect.xy+uv*_FrameRect.zw);
            }
            ENDCG
        }
    }
    Fallback Off
}
