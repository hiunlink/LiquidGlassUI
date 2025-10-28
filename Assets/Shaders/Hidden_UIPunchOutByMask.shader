Shader "Hidden/UIPunchOutByMask"
{
    Properties
    {
        _SrcTex  ("Src (BG RT copy)", 2D) = "white" {}
        _MaskTex ("Mask (CHAR/Other RT)", 2D) = "white" {}
        _Cutoff  ("Alpha Cutoff", Range(0,1)) = 0.5
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex   vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_SrcTex);   SAMPLER(sampler_SrcTex);   float4 _SrcTex_TexelSize;
            TEXTURE2D(_MaskTex);  SAMPLER(sampler_MaskTex);  float4 _MaskTex_TexelSize;
            float _Cutoff;

            struct Varyings {
                float4 positionCS : SV_POSITION;
                float2 uvSrc  : TEXCOORD0;
                float2 uvMask : TEXCOORD1;
            };

            Varyings vert(uint vid : SV_VertexID)
            {
                Varyings o;
                o.positionCS = GetFullScreenTriangleVertexPosition(vid);

                // 屏幕UV（NDC→0..1），以最终输出目标(backbuffer)为准
                float2 uv = o.positionCS.xy / o.positionCS.w;
                uv = uv * 0.5 + 0.5;
            #if UNITY_UV_STARTS_AT_TOP
                if (_ProjectionParams.x < 0) uv.y = 1.0 - uv.y;
            #endif

                float2 screenSize = _ScreenParams.xy;

                // Src 铺满屏
                float2 srcSize  = 1.0 / abs(_SrcTex_TexelSize.xy);
                float2 srcScale = screenSize / srcSize;
                o.uvSrc  = uv * srcScale;

                // Mask 铺满屏（允许与 Src 尺寸不同）
                float2 mSize    = 1.0 / abs(_MaskTex_TexelSize.xy);
                float2 mScale   = screenSize / mSize;
                o.uvMask = uv * mScale;

                return o;
            }

            float4 frag(Varyings i) : SV_Target
            {
                float4 src   = SAMPLE_TEXTURE2D(_SrcTex,  sampler_SrcTex,  i.uvSrc);
                float  maskA = SAMPLE_TEXTURE2D(_MaskTex, sampler_MaskTex, i.uvMask).a;

                // 不用羽化：硬裁剪（人物/遮罩不透明区→清0）
                if (maskA >= _Cutoff) return float4(0,0,0,0);
                return src;
            }
            ENDHLSL
        }
    }
    Fallback Off
}
