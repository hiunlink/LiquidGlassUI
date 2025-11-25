Shader "Hidden/UIBGCompositeStencilMip"
{
    Properties { _SourceTex("Source",2D)="white"{} _Mip("Mip",Float)=3 }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        ZWrite Off ZTest Always Cull Off
        Blend SrcAlpha OneMinusSrcAlpha

        // 这里也可以直接写 Stencil，但我们在 Feature 用 RenderStateBlock 注入了 NotEqual 1。
        // 若你偏好在Shader写死，可取消注释以下块：
        // Stencil { Ref 1 Comp NotEqual ReadMask 255 WriteMask 255 }
        Pass
        {
            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_SourceTex); SAMPLER(sampler_SourceTex);
            float _Mip;

            struct V { float4 p:SV_POSITION; float2 uv:TEXCOORD0; };
            V vert(uint vid:SV_VertexID){ V o; o.p=GetFullScreenTriangleVertexPosition(vid); o.uv=GetFullScreenTriangleTexCoord(vid); return o; }

            half4 frag(V i):SV_Target
            {
                return SAMPLE_TEXTURE2D_LOD(_SourceTex, sampler_SourceTex, i.uv, _Mip);
            }
            ENDHLSL
        }
                
        Pass
        {
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
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_SourceTex); SAMPLER(sampler_SourceTex);
            float _Mip;

            struct V { float4 p:SV_POSITION; float2 uv:TEXCOORD0; };
            V vert(uint vid:SV_VertexID){ V o; o.p=GetFullScreenTriangleVertexPosition(vid); o.uv=GetFullScreenTriangleTexCoord(vid); return o; }

            half4 frag(V i):SV_Target
            {
                return SAMPLE_TEXTURE2D_LOD(_SourceTex, sampler_SourceTex, i.uv, _Mip);
            }
            ENDHLSL
        }
    }
}
