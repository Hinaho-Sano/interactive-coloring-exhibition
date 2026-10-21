Shader "Resonance/Shadow"
{
    Properties { _MainTex ("Depth silhouette",2D)="black" {} _FlowTex ("Elastic memory",2D)="gray" {} _EchoTex ("Afterimage",2D)="black" {} }
    SubShader
    {
        Tags { "Queue"="Transparent-10" "RenderType"="Transparent" }
        Blend SrcAlpha One
        ZWrite Off
        Cull Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex,_FlowTex,_EchoTex;
            float4 _MainTex_TexelSize;
            float4 _Left,_Right;
            struct Input { float4 vertex:POSITION; float2 uv:TEXCOORD0; };
            struct Output { float4 pos:SV_POSITION; float2 uv:TEXCOORD0; };
            Output vert(Input v) { Output o; o.pos=UnityObjectToClipPos(v.vertex); o.uv=v.uv; return o; }
            float alphaAt(float2 p)
            {
                if(any(p<0)||any(p>1)) return 0;
                return tex2D(_MainTex,float2(1-p.x,p.y)).a;
            }
            fixed4 frag(Output i):SV_Target
            {
                float2 p=float2(i.uv.x,1-i.uv.y);
                float4 flow=tex2D(_FlowTex,saturate((p-float2(.055,.13))/float2(.89,.72)));
                float2 offset=(flow.rg-.5)*.4;
                float2 source=p-offset;
                float a=alphaAt(source);
                float2 px=_MainTex_TexelSize.xy*1.6;
                float ax=alphaAt(source+float2(px.x,0)),bx=alphaAt(source-float2(px.x,0));
                float ay=alphaAt(source+float2(0,px.y)),by=alphaAt(source-float2(0,px.y));
                float edge=saturate(abs(ax-bx)+abs(ay-by));
                float outer=max(max(ax,bx),max(ay,by));
                float d0=length((p-_Left.xy)*float2(1.77778,1)),d1=length((p-_Right.xy)*float2(1.77778,1));
                float touch0=exp(-d0*d0/.012)*_Left.z,touch1=exp(-d1*d1/.012)*_Right.z;
                float pulse=(sin(d0*90-_Time.y*8)*touch0+sin(d1*90-_Time.y*8)*touch1)*.12;
                float warmth=saturate(flow.a*.6+touch1*.7);
                float3 cool=float3(.12,.58,1),warm=float3(1,.55,.25);
                float3 color=lerp(cool,warm,warmth);
                float bands=.5+.5*sin(source.y*150+source.x*20+_Time.y*.35);
                float4 echo=tex2D(_EchoTex,source);
                float glow=edge*(.8+touch0+touch1)+a*(.20+bands*.07+flow.b*.2+touch0*.35+touch1*.35+pulse);
                float3 rgb=color*glow+echo.rgb*echo.a*.7;
                return fixed4(rgb,saturate(max(outer,a)*.85+echo.a*.6));
            }
            ENDCG
        }
    }
}
