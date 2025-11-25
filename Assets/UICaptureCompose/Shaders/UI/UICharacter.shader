Shader "UI/Character"
{
    Properties
    {
        _MainTex("Sprite",2D)="white"{}
        _Color("Tint",Color)=(1,1,1,1)
    }
    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "RenderType"="Transparent"
            "CanUseSpriteAtlas"="True"
        }
        Cull Off ZWrite Off ZTest Always
        Blend SrcAlpha OneMinusSrcAlpha
        
        Pass
        {
            Name "Normal"
            Tags
            {
                "LightMode" = "UniversalForward"
            }
            
            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma shader_feature _ FG_PREPASS_DRAW_COLOR
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            half _StencilVal;
            float4 _MainTex_ST;
            float4 _Color;
            
            struct A
            {
                float4 v:POSITION;
                float2 uv:TEXCOORD0;
                float4 c:COLOR;
            };

            struct V
            {
                float4 p:SV_POSITION;
                float2 uv:TEXCOORD0;
                float4 c:COLOR;
            };

            V vert(A i)
            {
                V o;
                o.p = TransformObjectToHClip(i.v);
                o.uv = TRANSFORM_TEX(i.uv, _MainTex);
                o.c = i.c * _Color;
                return o;
            }

            half4 frag(V i):SV_Target
            {
                half4 t = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                return t * i.c;
            }
            ENDHLSL
        }

        Pass
        {
            Name "StencilPrepass"
            Tags
            {
                "LightMode" = "StencilPrepass"
            }
            Stencil
            {
                Ref 1
                Comp Always
                Pass Replace
                ReadMask 255
                WriteMask 255
            }
            
            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma shader_feature _ FG_PREPASS_DRAW_COLOR
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            half _StencilVal;
            float4 _MainTex_ST;
            float4 _Color;
            float _Cutoff;

            struct A
            {
                float4 v:POSITION;
                float2 uv:TEXCOORD0;
                float4 c:COLOR;
            };

            struct V
            {
                float4 p:SV_POSITION;
                float2 uv:TEXCOORD0;
                float4 c:COLOR;
            };

            V vert(A i)
            {
                V o;
                o.p = TransformObjectToHClip(i.v);
                o.uv = TRANSFORM_TEX(i.uv, _MainTex);
                o.c = i.c * _Color;
                return o;
            }

            half4 frag(V i):SV_Target
            {
                half4 t = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                // 仅“不透明”部分
                clip(t.a - 1);
                #if defined(FG_PREPASS_DRAW_COLOR)
                return t * i.c;
                #else
                return 0;
                #endif
                
            }
            ENDHLSL
        }

        Pass
        {
            Name "AlphaOnly"
            Tags
            {
                "LightMode" = "AlphaOnly"
            }
            Stencil
            {
                Ref 1
                Comp NotEqual
                ReadMask 255
                WriteMask 255
            }
            
            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma shader_feature _ FG_PREPASS_DRAW_COLOR
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            half _StencilVal;
            float4 _MainTex_ST;
            float4 _Color;

            struct A
            {
                float4 v:POSITION;
                float2 uv:TEXCOORD0;
                float4 c:COLOR;
            };

            struct V
            {
                float4 p:SV_POSITION;
                float2 uv:TEXCOORD0;
                float4 c:COLOR;
            };

            V vert(A i)
            {
                V o;
                o.p = TransformObjectToHClip(i.v);
                o.uv = TRANSFORM_TEX(i.uv, _MainTex);
                o.c = i.c * _Color;
                return o;
            }

            half4 frag(V i):SV_Target
            {
                half4 t = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                // 仅“半透明”部分
                clip(1 - t.a);
                return t * i.c;
                
            }
            ENDHLSL
        }
    }
}