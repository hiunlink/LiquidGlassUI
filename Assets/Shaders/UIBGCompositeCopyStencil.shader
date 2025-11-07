Shader "Hidden/UIBGCompositeCopyStencil"
{
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Overlay" }

        Pass
        {   // pass0 no stencil
            Name "Copy"
            ZWrite Off
            ZTest Always
            Cull Off
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_SourceTex);
            SAMPLER(sampler_SourceTex);

            struct V { float4 p:SV_POSITION; float2 uv:TEXCOORD0; };
            V vert(uint vid:SV_VertexID){ V o; o.p=GetFullScreenTriangleVertexPosition(vid); o.uv=GetFullScreenTriangleTexCoord(vid); return o; }


            half4 frag(V i):SV_Target
            {
                return SAMPLE_TEXTURE2D(_SourceTex,sampler_SourceTex,i.uv);
            }
            ENDHLSL
        }

        Pass
        {   // pass1 stencil NotEqual（Ref=1）
            Name "CopyStencil"
            Stencil { Ref 1 Comp NotEqual Pass Keep Fail Keep ZFail Keep }

            ZWrite Off
            ZTest Always
            Cull Off
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_SourceTex);
            SAMPLER(sampler_SourceTex);

            struct V { float4 p:SV_POSITION; float2 uv:TEXCOORD0; };
            V vert(uint vid:SV_VertexID){ V o; o.p=GetFullScreenTriangleVertexPosition(vid); o.uv=GetFullScreenTriangleTexCoord(vid); return o; }


            half4 frag(V i):SV_Target
            {
                return SAMPLE_TEXTURE2D(_SourceTex,sampler_SourceTex,i.uv);
            }
            ENDHLSL
        }
    }
}
