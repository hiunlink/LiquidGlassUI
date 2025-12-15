Shader "UI/Debug/TransparentBlur"
{
    Properties
    {
        _MainTex ("(Unused UI mask)", 2D) = "white" {}
        //_BlurTex ("Background RT", 2D)     = "white" {}
        _Opacity("Opacity", Range(0,1)) = 0.6
        _MipLevel("Mip Level", Range(0,8)) = 2.0
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off Cull Off

        Pass
        {
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _UI_BG;
            float4 _UI_BG_TexelSize;
            float _Opacity;
            float _MipLevel;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv     : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uvScreen : TEXCOORD0;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                
                // NDC（-1..1）→ UV（0..1）
                float2 uv = o.pos.xy / o.pos.w;
                uv = uv * 0.5 + 0.5;

            #if UNITY_UV_STARTS_AT_TOP
                // 输出 flip，否则 backbuffer 写入时就无法正对
                if (_ProjectionParams.x < 0) 
                    uv.y = 1.0 - uv.y;
            #endif

                o.uvScreen = uv;
                return o;
            }

            float4 SampleMip(sampler2D tex, float2 uv, float mip)
            {
                float m0 = floor(mip);
                float t  = saturate(mip - m0);
                float4 c0 = tex2Dlod(tex, float4(uv,0,m0));
                float4 c1 = tex2Dlod(tex, float4(uv,0,m0+1));
                return lerp(c0,c1,t);
            }

            float4 frag(v2f i) : SV_Target
            {
                float4 blurCol = SampleMip(_UI_BG, i.uvScreen, _MipLevel);
                blurCol.a = _Opacity;
                return blurCol;
            }
            ENDHLSL
        }
    }
}
