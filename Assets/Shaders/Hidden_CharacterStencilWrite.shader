Shader "Hidden/CharacterStencilWrite"
{
    Properties { _MainTex("Sprite",2D)="white"{} _Cutoff("Alpha Cutoff",Range(0,1))=0.5 }
    SubShader
    {
        Tags { "Queue"="Transparent" "CanUseSpriteAtlas"="True" }
        Cull Off ZWrite Off ZTest Always
        Stencil { Ref 1 Comp Always Pass Replace ReadMask 255 WriteMask 255 }

        Pass
        {
            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            float4 _MainTex_ST; float _Cutoff;

            struct A{ float4 v:POSITION; float2 uv:TEXCOORD0; };
            struct V{ float4 p:SV_POSITION; float2 uv:TEXCOORD0; };

            V vert(A i){ V o; o.p=TransformObjectToHClip(i.v); o.uv=TRANSFORM_TEX(i.uv,_MainTex); return o; }
            half4 frag(V i):SV_Target
            {
                half4 col=SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,i.uv);
                clip(col.a - _Cutoff); // 仅“不透明”写入模板
                return col;
            }
            ENDHLSL
        }
    }
}
