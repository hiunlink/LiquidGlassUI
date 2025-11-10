using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace URP.Passes
{
    // 使用指定 LightMode 绘制某个 Layer；可开关 FG_PREPASS_DRAW_COLOR 关键字
    internal class DrawWithLightModePass : BasePass
    {
        // ========== 公共 Keyword ==========
        private static ShaderKeyword KW_FG_PREPASS_DRAW_COLOR = new ShaderKeyword("FG_PREPASS_DRAW_COLOR");
        
        string lightMode;
        LayerMask layer;
        bool enableKeywordDrawColor; // 仅在 StencilPrepass 中用到

        public DrawWithLightModePass(string tag, RenderPassEvent evt) : base(tag, evt){}

        public void Setup(string lightMode, LayerMask layer, bool enableKeywordDrawColor)
        {
            this.lightMode = lightMode;
            this.layer = layer;
            this.enableKeywordDrawColor = enableKeywordDrawColor;
        }

        public override void Execute(ScriptableRenderContext ctx, ref RenderingData data)
        {
            var cmd = CommandBufferPool.Get(tag);
            using (new ProfilingScope(cmd, new ProfilingSampler(tag)))
            {
                if (enableKeywordDrawColor) cmd.EnableShaderKeyword(KW_FG_PREPASS_DRAW_COLOR.name);
                else cmd.DisableShaderKeyword(KW_FG_PREPASS_DRAW_COLOR.name);

                ctx.ExecuteCommandBuffer(cmd); cmd.Clear();

                var ds = CreateDrawingSettings(new ShaderTagId(lightMode), ref data, SortingCriteria.CommonTransparent);
                var fs = new FilteringSettings(RenderQueueRange.all, layer);

                // 不在这里强制 RenderState（Stencil 写入由 shader 完成：StencilPrepass/AlphaOnly 逻辑）
                ctx.DrawRenderers(data.cullResults, ref ds, ref fs);
            }
            ctx.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }
    }

}