Shader "UI/BackgroundStencil"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Color   ("Tint Color", Color) = (1,1,1,1)

        // ▼ 关键Stencil属性（Inspector可见）
        _Stencil          ("Stencil Ref", Int) = 0
        _StencilComp      ("Stencil Comp", Int) = 8    // 8 = Always
        _StencilReadMask  ("Stencil ReadMask", Int) = 255
        _StencilWriteMask ("Stencil WriteMask", Int) = 255
        _ColorMask        ("Color Mask", Int) = 15
    }
    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="True"
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest Always
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            float4 _MainTex_ST;
            float4 _Color;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv     : TEXCOORD0;
                float4 color  : COLOR;
            };

            struct v2f
            {
                float4 pos  : SV_POSITION;
                float2 uv   : TEXCOORD0;
                float4 color: COLOR;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos   = TransformObjectToHClip(v.vertex);
                o.uv    = TRANSFORM_TEX(v.uv, _MainTex);
                o.color = v.color * _Color;
                return o;
            }

            half4 frag(v2f i) : SV_Target
            {
                half4 c = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                return c * i.color;
            }
            ENDHLSL
        }
    }
}
