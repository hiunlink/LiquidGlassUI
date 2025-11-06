Shader "Hidden/UIBG_GaussianSeparable"
{
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Overlay" }

        Pass
        {   // --- Horizontal ---
            Name "Horizontal"
            ZWrite Off
            ZTest Always
            Cull Off
            Blend One Zero // override

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_SourceTex);
            SAMPLER(sampler_SourceTex);
            float4 _TexelSize;
            float _Sigma;

            struct v2f { float4 pos:SV_POSITION; float2 uv:TEXCOORD0; };

            v2f vert(uint id:SV_VertexID)
            {
                v2f o;
                o.uv = float2((id<<1)&2, id&2);
                o.pos = float4(o.uv*2-1,0,1);
                return o;
            }

            half gaussian(float x) { return exp(-(x*x)/(2*_Sigma*_Sigma)); }

            half4 frag(v2f i):SV_Target
            {
                float w = _TexelSize.x;
                half4 sum = 0;
                const int TAP=5;
                half total=0;
                for(int k=-TAP;k<=TAP;k++)
                {
                    half g=gaussian(k);
                    total+=g;
                    sum+=SAMPLE_TEXTURE2D(_SourceTex,sampler_SourceTex,i.uv+float2(k*w,0))*g;
                }
                return sum/total;
            }
            ENDHLSL
        }

        Pass
        {   // --- Vertical ---
            Name "Vertical"
            ZWrite Off
            ZTest Always
            Cull Off
            Blend One Zero // override

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_SourceTex);
            SAMPLER(sampler_SourceTex);
            float4 _TexelSize;
            float _Sigma;

            struct v2f { float4 pos:SV_POSITION; float2 uv:TEXCOORD0; };

            v2f vert(uint id:SV_VertexID)
            {
                v2f o;
                o.uv = float2((id<<1)&2, id&2);
                o.pos = float4(o.uv*2-1,0,1);
                return o;
            }

            half gaussian(float x) { return exp(-(x*x)/(2*_Sigma*_Sigma)); }

            half4 frag(v2f i):SV_Target
            {
                float h=_TexelSize.y;
                half4 sum=0;
                const int TAP=5;
                half total=0;
                for(int k=-TAP;k<=TAP;k++)
                {
                    half g=gaussian(k);
                    total+=g;
                    sum+=SAMPLE_TEXTURE2D(_SourceTex,sampler_SourceTex,i.uv+float2(0,k*h))*g;
                }
                return sum/total;
            }
            ENDHLSL
        }
    }
}
