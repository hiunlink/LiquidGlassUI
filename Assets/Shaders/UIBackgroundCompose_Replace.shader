Shader "Hidden/UIBackgroundCompose_Replace"
{
    Properties
    {
        _BlurTex ("Blur RT (global)", 2D) = "white" {}
        _MipLevel("Blur Mip Level", Range(0, 8)) = 2.0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        ZWrite Off ZTest Always Cull Off

        Pass
        {
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex   vert
            #pragma fragment frag

            // URP 全屏三角形支持
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_BlurTex);
            SAMPLER(sampler_BlurTex);
            float4 _BlurTex_TexelSize;

            float _MipLevel;

            struct Varyings {
                float4 positionCS : SV_POSITION;
                float2 uvScreen   : TEXCOORD0;
            };

            Varyings vert(uint vertexID : SV_VertexID)
            {
                Varyings o;
                o.positionCS = GetFullScreenTriangleVertexPosition(vertexID);

                // ScreenUV（NDC→0..1）
                float2 uv = o.positionCS.xy / o.positionCS.w;
                uv = uv * 0.5 + 0.5;

                // 先做翻转，再做放大（次序很关键）
                #if UNITY_UV_STARTS_AT_TOP
                // 输出 flip，否则 backbuffer 写入时就无法正对
                if (_ProjectionParams.x < 0) uv.y = 1.0 - uv.y;
                #endif

                // RT 尺寸 → 屏幕尺寸（resolutionScale < 1 时放大铺满）
                float2 rtSize     = 1.0 / abs(_BlurTex_TexelSize.xy);
                float2 screenSize = _ScreenParams.xy;
                float2 scale      = screenSize / rtSize; // = 1 / resolutionScale
                o.uvScreen = uv * scale;

                return o;
            }

            float4 SampleMipLerp(TEXTURE2D_PARAM(tex, samp), float2 uv, float mip)
            {
                float m0 = floor(mip);
                float t  = saturate(mip - m0);
                float4 c0 = SAMPLE_TEXTURE2D_LOD(tex, samp, uv, m0);
                float4 c1 = SAMPLE_TEXTURE2D_LOD(tex, samp, uv, m0 + 1);
                return lerp(c0, c1, t);
            }

            float4 frag(Varyings i) : SV_Target
            {
                // 直接用模糊 RT 覆盖输出（完全替换）
                return SampleMipLerp(TEXTURE2D_ARGS(_BlurTex, sampler_BlurTex), i.uvScreen, _MipLevel);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
