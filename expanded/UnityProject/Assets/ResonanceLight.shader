Shader "Resonance/Light"
{
    Properties { _Soft ("Soft points",Float)=1 }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        Blend SrcAlpha One
        ZWrite Off
        Cull Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float _Soft;
            struct Input { float4 vertex:POSITION; float4 color:COLOR; float2 uv:TEXCOORD0; };
            struct Output { float4 position:SV_POSITION; float4 color:COLOR; float2 uv:TEXCOORD0; };
            Output vert(Input v) { Output o; o.position=UnityObjectToClipPos(v.vertex); o.color=v.color; o.uv=v.uv; return o; }
            fixed4 frag(Output i):SV_Target
            {
                float radius=length((i.uv-.5)*2);
                float glow=exp(-radius*radius*4)*saturate((1-radius)*3);
                return fixed4(i.color.rgb*1.8,i.color.a*lerp(1,glow,_Soft));
            }
            ENDCG
        }
    }
}
