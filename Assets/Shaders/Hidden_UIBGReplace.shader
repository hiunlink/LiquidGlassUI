Shader "Hidden/UIBGReplace"
{
    Properties { _MainTex("BG Source",2D)="white"{} }
    SubShader
    {
        Tags {"RenderType"="Opaque"}
        ZWrite Off ZTest Always Cull Off
        Blend One Zero
        Pass
        {
            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);

            struct V{ float4 p:SV_POSITION; float2 uv:TEXCOORD0; };
            V vert(uint vid:SV_VertexID){ V o; o.p=GetFullScreenTriangleVertexPosition(vid); o.uv=GetFullScreenTriangleTexCoord(vid); return o; }
            half4 frag(V i):SV_Target { return SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,i.uv); }
            ENDHLSL
        }
    }
}
