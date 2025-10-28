Shader "UI/Debug/UIBackgroundPreview"
{
    Properties
    {
        _Tint ("Tint", Color) = (1,1,1,0.6)
    }
    SubShader
    {
        Tags{ "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _UI_RT_CHAR;
            float4 _Tint;

            struct appdata
            {
                float4 vertex:POSITION;
                float2 uv:TEXCOORD0;
            };
            struct v2f
            {
                float4 pos:SV_POSITION;
                float2 uv:TEXCOORD0;
                float2 screenUV : TEXCOORD1;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos=UnityObjectToClipPos(v.vertex);
                o.uv=v.uv;
                // NDC ([-1,1] -> [0,1])
                float2 ndc = o.pos.xy / o.pos.w;
                o.screenUV = ndc * 0.5 + 0.5;
                //o.screenUV.y = 1 - o.screenUV.y;
                return o;
            }
            fixed4 frag(v2f i):SV_Target
            {
                fixed4 bg = tex2D(_UI_RT_CHAR, i.screenUV);
                return bg * _Tint;
            }
            ENDHLSL
        }
    }
}
