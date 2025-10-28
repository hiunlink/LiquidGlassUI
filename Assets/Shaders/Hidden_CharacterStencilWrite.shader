Shader "Hidden/CharacterStencilWrite"
{
    Properties
    {
        _MainTex ("Sprite Tex", 2D) = "white" {}
        _Cutoff  ("Alpha Cutoff", Range(0,1)) = 0.5
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" }
        Cull Off
        ZWrite Off
        ZTest Always
        ColorMask 0

        Stencil
        {
            Ref 1
            Comp Always
            Pass Replace
            ZFail Keep
        }

        Pass
        {
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex   vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex; float4 _MainTex_ST;
            float _Cutoff;

            struct appdata { float4 vertex:POSITION; float2 uv:TEXCOORD0; };
            struct v2f     { float4 pos:SV_POSITION; float2 uv:TEXCOORD0; };

            v2f vert(appdata v){
                v2f o; o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            fixed4 frag(v2f i):SV_Target
            {
                fixed a = tex2D(_MainTex, i.uv).a;
                clip(a - _Cutoff);     // 仅人物不透明处写模板
                return 0;              // ColorMask 0 → 不写颜色
            }
            ENDCG
        }
    }
    Fallback Off
}
