using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace UICaptureCompose.URP.Passes
{
    // 用默认 URP Tag 绘制某个 Layer；可选强制 Stencil NotEqual 1（用于背景回填/合成阶段）
    internal class DrawDefaultPass : BasePass
    {
        // ========== 通用绘制 Tag ==========
        private static List<ShaderTagId> k_DefaultTags = new List<ShaderTagId>();
        
        LayerMask _layer;
        bool _useStencilNotEqual1;

        RenderStateBlock _rsb;
        private ProfilingSampler _profilingSampler;
        private FilteringSettings _fs;

        public DrawDefaultPass(string tag, RenderPassEvent evt)
            : base(tag, evt)
        {
            {
                var ss = new StencilState(
                    enabled: true,
                    readMask: 0xFF, writeMask: 0xFF,
                    compareFunction: CompareFunction.NotEqual,
                    passOperation: StencilOp.Keep,
                    failOperation: StencilOp.Keep,
                    zFailOperation: StencilOp.Keep);
                _rsb = new RenderStateBlock(RenderStateMask.Stencil)
                {
                    stencilReference = 1,
                    stencilState = ss
                };
            }
            
            k_DefaultTags = new()
            {
                new ShaderTagId("UniversalForward"),
                new ShaderTagId("UniversalForwardOnly"),
                new ShaderTagId("SRPDefaultUnlit"),
            };
            
            _profilingSampler = new ProfilingSampler(tag);
        }

        public void Setup(LayerMask layer, bool useStencilNotEqual1)
        {
            _layer = layer;
            _useStencilNotEqual1 = useStencilNotEqual1;
            _fs = new FilteringSettings(RenderQueueRange.all, _layer);
        }

        public override void Execute(ScriptableRenderContext ctx, ref RenderingData data)
        {
            var cmd = CommandBufferPool.Get(tag);
            using (new ProfilingScope(cmd, _profilingSampler))
            {
                ctx.ExecuteCommandBuffer(cmd); cmd.Clear();

                var ds = CreateDrawingSettings(k_DefaultTags, ref data, SortingCriteria.CommonTransparent);
                if (_useStencilNotEqual1)
                    ctx.DrawRenderers(data.cullResults, ref ds, ref _fs, ref _rsb);
                else
                    ctx.DrawRenderers(data.cullResults, ref ds, ref _fs);
            }
            ctx.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }
    }
}