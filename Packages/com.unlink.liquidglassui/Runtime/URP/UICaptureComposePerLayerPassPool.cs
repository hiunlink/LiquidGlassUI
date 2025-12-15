using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Unlink.LiquidGlassUI
{
    // ======== Pass 池：获取或创建 ==========
    internal static class UICaptureComposePerLayerPassPool
    {
        // --- Pass 池（长生命周期；按层索引复用） ---
        static List<DrawWithLightModePass> _poolDrawLM = new();
        static List<DrawWithLightModePass> _poolDrawAlpha = new();
        static List<DrawDefaultPass>       _poolDrawDef = new();
        static List<MipChainBlurPass>      _poolMipBlur = new();
        static List<GenMipsPass>           _poolGenMips = new();
        static List<GenMipsPass>           _poolGenMips2 = new();
        static List<GaussianWrapperPass>   _poolGaussian = new();
        static List<GaussianWrapperPass>   _poolGaussian2 = new();
        static List<OneShot>               _poolOneShot = new();
        static List<OneShot>               _poolOneShot2 = new();
        static List<OneShot>               _poolOneShot3 = new();
        static List<CompositePass>         _poolComposite = new();
        
        
        public static DrawWithLightModePass GetOrCreateDrawWithLightMode(int i, string tag, RenderPassEvent evt)
        {
            while (_poolDrawLM.Count <= i) _poolDrawLM.Add(null);
            var p = _poolDrawLM[i];
            if (p == null) { p = new DrawWithLightModePass(tag, evt); _poolDrawLM[i] = p; }
            else { p.renderPassEvent = evt; }
            return p;
        }
        public static DrawWithLightModePass GetOrCreateDrawAlphaOnly(int i, string tag, RenderPassEvent evt)
        {
            while (_poolDrawAlpha.Count <= i) _poolDrawAlpha.Add(null);
            var p = _poolDrawAlpha[i];
            if (p == null) { p = new DrawWithLightModePass(tag, evt); _poolDrawAlpha[i] = p; }
            else
            {
                p.renderPassEvent = evt;
            }
            return p;
        }
        public static DrawDefaultPass GetOrCreateDrawDefault(int i, string tag, RenderPassEvent evt)
        {
            while (_poolDrawDef.Count <= i) _poolDrawDef.Add(null);
            var p = _poolDrawDef[i];
            if (p == null) { p = new DrawDefaultPass(tag, evt); _poolDrawDef[i] = p; }
            else { p.renderPassEvent = evt; }
            return p;
        }

        public static MipChainBlurPass GetOrCreateMipChainBlur(int i, string tag, RenderPassEvent evt)
        {
            while (_poolMipBlur.Count <= i) _poolMipBlur.Add(null);
            var p = _poolMipBlur[i];
            if (p == null) { p = new MipChainBlurPass(tag, evt); _poolMipBlur[i] = p; }
            else { p.renderPassEvent = evt; }
            return p;
        }

        public static GenMipsPass GetOrCreateGenMip(int i, string tag, RenderPassEvent evt)
        {
            while (_poolGenMips.Count <= i) _poolGenMips.Add(null);
            var p = _poolGenMips[i];
            if (p == null) { p = new GenMipsPass(tag, evt); _poolGenMips[i] = p; }
            else { p.renderPassEvent = evt; }
            return p;
        }
        public static GenMipsPass GetOrCreateGenMip2(int i, string tag, RenderPassEvent evt)
        {
            while (_poolGenMips2.Count <= i) _poolGenMips2.Add(null);
            var p = _poolGenMips2[i];
            if (p == null) { p = new GenMipsPass(tag, evt); _poolGenMips2[i] = p; }
            else { p.renderPassEvent = evt; }
            return p;
        }
        public static CompositePass GetOrCreateComposite(int i, string tag, RenderPassEvent evt)
        {
            while (_poolComposite.Count <= i) _poolComposite.Add(null);
            var p = _poolComposite[i];
            if (p == null) { p = new CompositePass(tag, evt); _poolComposite[i] = p; }
            else { p.renderPassEvent = evt; }
            return p;
        }

        // 如果你能修改 GaussianBlurPass：建议提供如下包装接口
        internal class GaussianWrapperPass : ScriptableRenderPass
        {
            GaussianBlurPass _inner;
            public GaussianWrapperPass(GaussianBlurPass inner){ _inner = inner; }
            public void Setup(RTHandle src, RTHandle tmp, RTHandle dst, RTHandle baseCol, RTHandle baseDS)
                => _inner.Setup(src,tmp,dst,baseCol,baseDS);
            public void SetSharedMaterials(Material gauss, Material copy)
                => _inner.SetSharedMaterials(gauss, copy); // 需要在你的类里添加
            public void SetParams(int iteration, float sigma, float mipMap, Color alphaBlendColor,
                bool useStencilNotEqual,bool useStencilNotEqualComposite, int stencilVal)
                => _inner.SetParams(iteration, sigma, mipMap, alphaBlendColor, useStencilNotEqual, useStencilNotEqualComposite, stencilVal);
            public override void Execute(ScriptableRenderContext ctx, ref RenderingData data) => _inner.Execute(ctx, ref data);
        }
        public static GaussianWrapperPass GetOrCreateGaussian(int i, string tag, RenderPassEvent evt)
        {
            // 简化：每层一个 wrapper（内部持有同一个 GaussianBlurPass）
            while (_poolGaussian.Count <= i) _poolGaussian.Add(null);
            var p = _poolGaussian[i];
            if (p == null)
            {
                var inner = new GaussianBlurPass(tag, evt); 
                p = new GaussianWrapperPass(inner) { renderPassEvent = evt };
                _poolGaussian[i] = p;
            }
            else { p.renderPassEvent = evt; }
            return p;
        }
        public static GaussianWrapperPass GetOrCreateGaussian2(int i, string tag, RenderPassEvent evt)
        {
            // 简化：每层一个 wrapper（内部持有同一个 GaussianBlurPass）
            while (_poolGaussian2.Count <= i) _poolGaussian2.Add(null);
            var p = _poolGaussian2[i];
            if (p == null)
            {
                var inner = new GaussianBlurPass(tag, evt); 
                p = new GaussianWrapperPass(inner) { renderPassEvent = evt };
                _poolGaussian2[i] = p;
            }
            else { p.renderPassEvent = evt; }
            return p;
        }

        public static OneShot GetOrCreateOneShot(int i, CommandBuffer cmd, RenderPassEvent evt)
        {
            //return new OneShot(cmd, evt);
            while (_poolOneShot.Count <= i) _poolOneShot.Add(null);
            var p = _poolOneShot[i];
            if (p == null) { p = new OneShot(cmd, evt); _poolOneShot[i] = p; }
            else { p.renderPassEvent = evt; p.Setup(cmd); }
            return p;
        }
        public static OneShot GetOrCreateOneShot2(int i, CommandBuffer cmd, RenderPassEvent evt)
        {
            while (_poolOneShot2.Count <= i) _poolOneShot2.Add(null);
            var p = _poolOneShot2[i];
            if (p == null) { p = new OneShot(cmd, evt); _poolOneShot2[i] = p; }
            else { p.renderPassEvent = evt; p.Setup(cmd); }
            return p;
        }
        public static OneShot GetOrCreateOneShot3(int i, CommandBuffer cmd, RenderPassEvent evt)
        {
            while (_poolOneShot3.Count <= i) _poolOneShot3.Add(null);
            var p = _poolOneShot3[i];
            if (p == null) { p = new OneShot(cmd, evt); _poolOneShot3[i] = p; }
            else { p.renderPassEvent = evt; p.Setup(cmd); }
            return p;
        }
        
        internal class OneShot : ScriptableRenderPass
        {
            CommandBuffer _cmd;
            public OneShot(CommandBuffer cmd, RenderPassEvent evt) { _cmd = cmd; renderPassEvent = evt; }

            public void Setup(CommandBuffer cmd)
            {
                _cmd = cmd;
            }
            public override void Execute(ScriptableRenderContext ctx, ref RenderingData data)
            { ctx.ExecuteCommandBuffer(_cmd); CommandBufferPool.Release(_cmd); }
        }
        
        internal class GenMipsPass : ScriptableRenderPass
        {
            RTHandle _target; bool _doGen; readonly string _tag;
            private ProfilingSampler _profilingSampler;

            public GenMipsPass(string tag, RenderPassEvent evt)
            {
                this._tag=tag; renderPassEvent=evt;
                _profilingSampler = new ProfilingSampler(_tag);
            }
            public void Setup(RTHandle rt, bool gen){ _target=rt; _doGen=gen; }
            public override void Execute(ScriptableRenderContext ctx, ref RenderingData data)
            {
                if (!_doGen || _target==null) return;
                var cmd = CommandBufferPool.Get(_tag);
                using (new ProfilingScope(cmd, _profilingSampler)) cmd.GenerateMips(_target);
                ctx.ExecuteCommandBuffer(cmd); CommandBufferPool.Release(cmd);
            }
        }
    }
}