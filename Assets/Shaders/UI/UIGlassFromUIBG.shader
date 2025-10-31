Shader "UI/GlassFromUIBG"
{
    Properties
    {
        _Color ("Tint", Color) = (1,1,1,0.9)
        _Mip   ("Blur Mip", Range(0,8)) = 3.0   // 直接用 MIP 等级做“虚化强度”
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "CanUseSpriteAtlas"="True" }
        Cull Off ZWrite Off ZTest Always
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // 采样离屏背景（全局由 Feature 设置）
            TEXTURE2D(_UI_BG); SAMPLER(sampler_UI_BG);
            float4 _UI_BG_TexelSize;   // Unity 会自动填
            float4 _UIBG_UVScale;      // 由 Feature 设置：RTSize/ScreenSize
            float4 _Color;
            float  _Mip;

            struct A{ float4 v:POSITION; float2 uv:TEXCOORD0; float4 c:COLOR; };
            struct V{ float4 pos:SV_POSITION; float2 uv:TEXCOORD0; float4 c:COLOR; };

            V vert(A i){ V o; o.pos=TransformObjectToHClip(i.v); o.uv=i.uv; o.c=i.c*_Color; return o; }

            half4 frag(V i):SV_Target
            {
                // 屏幕UV：从SV_POSITION还原NDC → [0,1]，再按RT与屏幕分辨率比例缩放
                float2 ndc = i.pos.xy / i.pos.w;     // [-1,1]
                float2 suv = ndc * 0.5 + 0.5;        // [0,1]
            #if UNITY_UV_STARTS_AT_TOP
                // 不需要手动flip：URP Blit & camera backbuffer 已处理；如项目有特殊翻转，可在此处理
            #endif
                float2 uvBG = suv * _UIBG_UVScale.xy;

                half4 bg = SAMPLE_TEXTURE2D_LOD(_UI_BG, sampler_UI_BG, uvBG, _Mip);
                return bg * i.c; // 可加高光/边缘等效果
            }
            ENDHLSL
        }
    }
}
