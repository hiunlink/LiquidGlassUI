Shader "Hidden/KawaseBlur"
{
    Properties { }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Overlay" }
        ZWrite Off ZTest Always Cull Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            // 模式：0=直接采样 _SourceTex；1=Kawase 模糊
            int _Mode;
            float2 _TexelSize;     // 目标 RT 的 1/width, 1/height
            float  _KawaseRadius;  // 步长半径：1.5, 2.5 ...

            sampler2D _SourceTex;  // 源纹理（downsample 时 = _UIBackgroundRT；模糊时 = 上一步 RT）

            struct appdata { float4 vertex:POSITION; float2 uv:TEXCOORD0; };
            struct v2f { float4 pos:SV_POSITION; float2 uv:TEXCOORD0; };

            v2f vert(appdata v){
                v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.uv; return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                if (_Mode == 0) {
                    // 直接采样源纹理（用于 Downsample）
                    return tex2D(_SourceTex, i.uv);
                }

                // --- Kawase 采样模式 ---
                float2 offsets[8] = {
                    float2( 1,  1), float2(-1,  1),
                    float2( 1, -1), float2(-1, -1),
                    float2( 2,  0), float2(-2,  0),
                    float2( 0,  2), float2( 0, -2)
                };

                float4 sum = 0;
                [unroll] for (int k=0; k<8; k++){
                    float2 uv = i.uv + offsets[k] * _TexelSize * _KawaseRadius;
                    sum += tex2D(_SourceTex, uv);
                }
                return sum * (1.0/8.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
