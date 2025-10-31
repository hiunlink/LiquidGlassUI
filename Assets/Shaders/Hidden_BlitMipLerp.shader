Shader "Hidden/BlitMipLerp"
{
    Properties
    {
        _MainTex ("Source", 2D) = "white" {}  // ★★ 必须要声明
        _MipLevel ("Mips Level", int) = 3
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        ZWrite Off
        ZTest Always
        Cull Off

        Pass
        {
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;  // y < 0 时表示需要翻转
            float _MipLevel;            // 支持小数

            struct appdata { float4 vertex:POSITION; float2 uv:TEXCOORD0; };
            struct v2f { float4 pos:SV_POSITION; float2 uv:TEXCOORD0; };

            v2f vert(appdata v){
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv  = v.uv;
                // 对齐 Unity 的 RT 约定：当源是 RT 且 Y 轴倒置时，需要翻转
                #if UNITY_UV_STARTS_AT_TOP
                if (_MainTex_TexelSize.y < 0)    // 关键：兼容 SRP/不同平台
                    o.uv.y = 1.0 - o.uv.y;
                #endif
                return o;
            }

            float4 SampleMipLerp(sampler2D tex, float2 uv, float mip)
            {
                float m0 = floor(mip);
                float t  = saturate(mip - m0);
                float4 c0 = tex2Dlod(tex, float4(uv, 0, m0));
                float4 c1 = tex2Dlod(tex, float4(uv, 0, m0 + 1));
                return lerp(c0, c1, t);
            }

            float4 frag(v2f i) : SV_Target
            {
                return SampleMipLerp(_MainTex, i.uv, _MipLevel);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
