Shader "Hidden/UI_LiquidGlass"
{
    Properties
    {
        [NoScaleOffset]_MainTex("UI Source (mask/color)", 2D) = "white" {}
        [NoScaleOffset]_UI_BG("UI BG", 2D) = "white" {}
        [NoScaleOffset]_UI_BG_BLUR("UI BG Blur", 2D) = "white" {}
        _Color("Tint Multiply", Color) = (1,1,1,1)

        _UseBlurBG("Use Blur Background", Float) = 1
        _UIBG_UVScale("_UIBG_UVScale (xy)", Vector) = (1,1,0,0)
        _UIBG_Lod("UIBG Base Lod", Range(0,8)) = 0

        // Refraction controls (match Shadertoy semantics)
        _RefrDim("Refraction Edge Width", Float) = 0.05
        _RefrMag("Refraction Magnitude", Float) = 0.10
        _RefrAberration("Chromatic Aberration (0-10)", Float) = 5.0
        _IOR("IOR (RGB)", Vector) = (1.51, 1.52, 1.53, 0)
        _RefrLODBias("Refraction LOD Bias (0-4)", Range(0,4)) = 1.0

        // Edge + Tint
        _EdgeDim("Edge Rim Width", Float) = 0.003
        _TintColor("Glass Tint RGBA", Color) = (0,0,0,0)
        _RimLightVec("Rim Light Dir (xy)", Vector) = (-0.707, 0.707, 0, 0)
        _RimLightColor("Rim Light RGBA", Color) = (1,1,1,0.15)
        _Highlight("Rim Highlight", Range(0,10)) = 1
    }

    SubShader
    {
        Tags{ "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" "RenderPipeline"="UniversalPipeline" }
        ZWrite Off
        Cull Off
        Blend SrcAlpha OneMinusSrcAlpha

        // -------------------------------
        //    Main Liquid Glass pass
        //    LightMode = SRPDefaultUnlit
        //    Samples _UI_BG with RGB-offset refraction
        // -------------------------------
        Pass
        {
            Name "LiquidGlass"
            Tags{ "LightMode" = "SRPDefaultUnlit" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_local _ WITHOUT_UI_BG
            //#pragma multi_compile_local __ DBG_SDF DBG_REFR_REGION DBG_REFR_UV DBG_TINT_REGION DBG_TINT_UV DBG_TINT_EDGE_LIGHT DBG_TINT_EDGE_REFL

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            // Background published by UICaptureComposePerLayerFeature
            TEXTURE2D(_UI_BG);   SAMPLER(sampler_UI_BG);
            TEXTURE2D(_UI_BG_BLUR);   SAMPLER(sampler_UI_BG_BLUR);
            //#define DBG_SDF
            //#define DBG_TINT_EDGE_REFL
            //#define WITHOUT_UI_BG
            
            float2 _RectUVOffset;   // 界面元素位置
            float4 _Color;
            float _UIBG_Lod;
            float _UseBlurBG;
            float2 _UIBG_UVScale; // xy
            float  _RefrDim, _RefrMag, _RefrAberration;
            float4 _IOR; // xyz = RGB
            float  _EdgeDim;
            float4 _TintColor;
            float2 _RimLightVec; // normalized in C# if needed
            float4 _RimLightColor;
            float _Highlight;

            struct VIn { float4 vertex:POSITION; float2 uv:TEXCOORD0; float4 color:COLOR; };
            struct VOut { float4 pos:SV_Position; float2 uv:TEXCOORD0; float4 col:COLOR; float2 screenUV:TEXCOORD1; };

            VOut Vert(VIn v){
                VOut o; o.pos = TransformObjectToHClip(v.vertex.xyz);
                o.uv = v.uv; o.col = v.color * _Color;
                float2 ndc = o.pos.xy / o.pos.w;            // -1..1
                float2 uv  = ndc * 0.5 + 0.5;               //  0..1
                #if UNITY_UV_STARTS_AT_TOP
                // 输出 flip，否则 backbuffer 写入时就无法正对
                if (_ProjectionParams.x < 0) 
                        uv.y = 1.0 - uv.y;
                #endif
                o.screenUV = uv * _UIBG_UVScale;            // match _UI_BG scale
                
                return o;
            }

            // Helpers
            float Lerp01(float minV, float maxV, float v){ return saturate( (v - minV) / max(maxV - minV, 1e-6) ); }

            // Compute pseudo SDF from UI alpha: center (alpha=1) interior, edge falloff
            // Use derivatives to approximate gradient.
            // void // SDF Texture Version
            // Real-time screen-space SDF from UI alpha (no SDF texture)
            // Rounded Rect SDF (replaces alpha based pseudo SDF)
            float2 _RoundedRectHalfSize; // UI set in px normalized uv space
            float  _RoundedRadius;
            float  _BorderWidth;
            half   _RefrLODBias;
            
            void ComputeDistanceAndNormal(float2 uv, out float d, out float2 nrm)
            {
                // remap uv to -0.5~0.5 center space
                float2 p = uv - 0.5;
                p.x *= _ScreenParams.x / _ScreenParams.y; // height做基准
                float minHS = min(_RoundedRectHalfSize.x, _RoundedRectHalfSize.y);
                float rad = min(_RoundedRadius, minHS);
                float2 q = abs(p) - _RoundedRectHalfSize + rad;
                float dist = length(max(q,0.0)) + min(max(q.x,q.y),0.0) - _RoundedRadius;
                // auto circle: if radius >= min half size => clamp
                dist = length(max(q,0.0)) + min(max(q.x,q.y),0.0) - rad;

                // border shrink
                float distInner = dist + _BorderWidth;
                d = distInner;
                
                // gradient approximate from analytic gradient
                float2 g;
                if(q.x>q.y)
                    g = float2(sign(p.x),0);
                else
                    g = float2(0,sign(p.y));
                
                if(max(q.x,q.y)>0)
                    g = normalize(p);
                
                nrm = g;
            }
            float3 SampleBG(float2 uv, float lod){
                // SRP macro for explicit LOD
                #if defined(UNITY_NO_DXT5nm)
                    return SAMPLE_TEXTURE2D_LOD(_UI_BG, sampler_UI_BG, uv, lod).rgb;
                #else
                    return SAMPLE_TEXTURE2D_LOD(_UI_BG, sampler_UI_BG, uv, lod).rgb;
                #endif
            }
            float3 SampleBlurBG(float2 uv, float lod){
                if (_UseBlurBG > 0)
                {
                    // SRP macro for explicit LOD
                    #if defined(UNITY_NO_DXT5nm)
                    return SAMPLE_TEXTURE2D_LOD(_UI_BG_BLUR, sampler_UI_BG_BLUR, uv, lod).rgb;
                    #else
                    return SAMPLE_TEXTURE2D_LOD(_UI_BG_BLUR, sampler_UI_BG_BLUR, uv, lod).rgb;
                    #endif
                }
                else
                {
                    return SampleBG(uv, lod+_UIBG_Lod);
                }
            }

            float3 BlendScreen(float3 a, float3 b){ return 1.0 - (1.0 - a) * (1.0 - b); }
            float3 BlendLighten(float3 a, float3 b){ return max(a, b); }

            float4 Frag(VOut i):SV_Target
            {
                float2 uv = i.uv;
                float2 bgUV = i.screenUV;

                float eps = 3.0 / _ScreenParams.y; // 2px like Shadertoy EPS

                // Distance/normal from UI alpha
                float d; float2 nrm;
                ComputeDistanceAndNormal(bgUV-_RectUVOffset, d, nrm);

                // Boundary strength near edges
                float boundary = Lerp01(-_RefrDim, eps, d);
                boundary = lerp(boundary, 0.0, smoothstep(0.0, eps, d));
                float cosBoundary = 1.0 - cos(boundary * 3.14159265 * 0.5);
                //return float4(cosBoundary,cosBoundary,cosBoundary,1);
                
                float interior = smoothstep(eps, 0.0, d);
               
                // --- Refraction layer ---
                #if defined(DBG_SDF)
                {
                    float s = saturate(0.5 + d*19);
                    float3 cOutside = float3(1,0,0);
                    float3 cInside  = float3(0,0,1);
                    float3 c = lerp(cInside, cOutside, s);
                    return float4(c, 1);
                }
                #endif
                // rim
                float rimIntensity = abs( dot( normalize(nrm), normalize(_RimLightVec) ) );
                float3 rimLight = _RimLightColor.rgb * _RimLightColor.a * rimIntensity;
                // --- Tint / Rim / Reflection ---
                float a = interior;
                float bnd = Lerp01(-_EdgeDim, 0.0, d);
                float edge = min(a, bnd);
                float cosEdge = 1.0 - cos(edge * 3.14159265 * 0.5);
                // --- without UI_BG ---
                #if defined(WITHOUT_UI_BG)
                // interior tint for legibility
                float3 rimLightCol = rimLight * cosEdge;
                float rimAlpha = cosEdge * rimIntensity * _RimLightColor.a;
                float4 colWoBG = float4(BlendScreen(_TintColor.rgb, rimLightCol), _TintColor.a * interior + rimAlpha);
                return colWoBG;
                #endif
                
                float3 baseCol = SampleBG(bgUV, 0.0);

                float3 ior = lerp( float3(_IOR.y, _IOR.y, _IOR.y), _IOR.xyz, _RefrAberration);
                float3 ratios = pow( cosBoundary.xxx, ior );
                float2 offset = -nrm * _RefrMag;
                float2 oR = offset * ratios.r;
                float2 oG = offset * ratios.g;
                float2 oB = offset * ratios.b;

                // increase LOD near edge for extra blur feel; 0..(~3)
                float lodEdge = _RefrMag * 50.0 * cosBoundary * _RefrLODBias;

                float r = SampleBlurBG(bgUV + oR, lodEdge).r;
                float g = SampleBlurBG(bgUV + oG, lodEdge).g;
                float b = SampleBlurBG(bgUV + oB, lodEdge).b;
                float3 blurWarped = float3(r,g,b);

                float3 col = lerp(baseCol, blurWarped, interior);

                // interior tint for legibility
                col = lerp(col, _TintColor.rgb, _TintColor.a * interior);

                // simple reflection sample: offset outward along normal
                #if defined(ENABLE_REFLECTION)
                float2 reflOffset = (_EdgeDim*12.0 + 0.5*_RefrMag * cosEdge) * nrm;
                float3 reflCol = SampleBlurBG(bgUV + reflOffset, 4);
                // simulate bloom effect
                float threthold = 0.2;
                float intensity = _Highlight;
                float3 highlight = clamp(reflCol - threthold, 0, 1) / (1 - threthold) * intensity;
                reflCol = BlendScreen(reflCol, highlight);
                reflCol = lerp(reflCol, _TintColor.rgb, _TintColor.a);
                #else
                float3 reflCol = float3(0,0,0);
                #endif
                

                #if defined(DBG_TINT_EDGE_LIGHT)
                    float3 mergedEdge = rimLight;
                #elif defined(DBG_TINT_EDGE_REFL)
                    float3 mergedEdge = reflCol;
                #else
                    float3 mergedEdge = BlendScreen(rimLight, reflCol);
                #endif

                float3 edgeColor = BlendLighten(col, mergedEdge);
                col = lerp(col, edgeColor, cosEdge);

                // Multiply UI vertex color
                float4 uiTex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv) * i.col;
                col *= uiTex.rgb;
                float outA = uiTex.a; // preserve UI alpha pipeline

                return float4(col, outA);
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
                #if defined(FG_PREPASS_DRAW_COLOR)
                return t * i.c;
                #else
                return 0;
                #endif
                
            }
            ENDHLSL
        }
    }
}
