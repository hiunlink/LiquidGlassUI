Shader "UI/CharacterAlphaOnly"
{
    Properties { _MainTex("Sprite",2D)="white"{} _Color("Tint",Color)=(1,1,1,1) _Cutoff("Alpha Cutoff",Range(0,1))=0.5 }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "CanUseSpriteAtlas"="True" }
        Cull Off ZWrite Off ZTest Always
        Blend SrcAlpha OneMinusSrcAlpha
        // 受人物不透明模板剔除
        Stencil { Ref 1 Comp NotEqual ReadMask 255 WriteMask 255 }

        Pass
        {
            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma shader_feature_local _ UI_HIDE_ON_MAIN
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            float4 _MainTex_ST; float4 _Color; float _Cutoff;

            struct A{ float4 v:POSITION; float2 uv:TEXCOORD0; float4 c:COLOR; };
            struct V{ float4 p:SV_POSITION; float2 uv:TEXCOORD0; float4 c:COLOR; };

            V vert(A i){ V o; o.p=TransformObjectToHClip(i.v); o.uv=TRANSFORM_TEX(i.uv,_MainTex); o.c=i.c*_Color; return o; }

            half4 frag(V i):SV_Target
            {
            #if defined(UI_HIDE_ON_MAIN)
                // 主相机默认阶段：不再上色（防重复绘制）
                clip(-1);
            #endif
                half4 t = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                // 仅“半透明”部分
                clip(_Cutoff - t.a);
                return t * i.c;
            }
            ENDHLSL
        }
    }
}
