using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Experimental.Rendering;
using System.Collections.Generic;

public class UICaptureComposeFeature : ScriptableRendererFeature
{
    [System.Serializable]
    public class Settings
    {
        [Header("离屏输出（供玻璃/Replace使用）")]
        public string globalTextureName = "_UI_BG";
        [Range(0.25f,1f)] public float resolutionScale = 1f;
        public bool useHDR = false;
        public Color clearColor = new Color(0,0,0,0);

        [Header("Layer 过滤")]
        public LayerMask characterLayer;   // 人物层
        public LayerMask backgroundLayer;  // 背景层

        [Header("Override 材质")]
        public Material characterStencilWriteMat; // Hidden/CharacterStencilWrite
        public Material characterAlphaOnlyMat;    // UI/CharacterAlphaOnly（建议勾选）
        public bool useOverrideForCharacterAlpha = true;

        [Header("注入时机")]
        public RenderPassEvent injectEvent = RenderPassEvent.BeforeRenderingTransparents;

        [Header("MIP 生成")]
        public bool generateMips = true;
    }

    // ===== 基类 =====
    abstract class BasePass : ScriptableRenderPass
    {
        protected readonly string tag;
        protected readonly Settings s;
        protected readonly List<ShaderTagId> tags = new() {
            new ShaderTagId("UniversalForward"), new ShaderTagId("UniversalForwardOnly"), new ShaderTagId("SRPDefaultUnlit")
        };
        protected FilteringSettings filtering;
        protected RTHandle colorRT, depthRT;

        protected BasePass(string tag, Settings s, RenderQueueRange rq, LayerMask lm)
        { this.tag=tag; this.s=s; filtering = new FilteringSettings(rq, lm); renderPassEvent = s.injectEvent; }

        public void Setup(RTHandle col, RTHandle dep){ colorRT=col; depthRT=dep; }
        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData data){ ConfigureTarget(colorRT, depthRT); }
    }

    // 1) 人物Stencil预通道
    class StencilPrePass : BasePass
    {
        Material mat;
        public StencilPrePass(Settings s) : base("UI P1 StencilPre (Char Opaque)", s, RenderQueueRange.all, s.characterLayer)
        { mat = s.characterStencilWriteMat; }

        public override void Execute(ScriptableRenderContext ctx, ref RenderingData data)
        {
            if (mat == null) return;
            var cmd = CommandBufferPool.Get(tag);
            using (new ProfilingScope(cmd, new ProfilingSampler(tag)))
            {
                ctx.ExecuteCommandBuffer(cmd); cmd.Clear();
                var ds = CreateDrawingSettings(tags, ref data, SortingCriteria.CommonTransparent);
                ds.overrideMaterial = mat; // 只写模板
                ctx.DrawRenderers(data.cullResults, ref ds, ref filtering);
            }
            ctx.ExecuteCommandBuffer(cmd); CommandBufferPool.Release(cmd);
        }
    }

    // 2) 背景（受模板NotEqual 1）
    class BackgroundPass : BasePass
    {
        RenderStateBlock rsb;
        public BackgroundPass(Settings s) : base("UI P2 Background (EarlyStencil)", s, RenderQueueRange.all, s.backgroundLayer)
        {
            var ss = new StencilState(true, 0xFF,0xFF, CompareFunction.NotEqual, StencilOp.Keep, StencilOp.Keep, StencilOp.Keep);
            rsb = new RenderStateBlock(RenderStateMask.Stencil){ stencilReference=1, stencilState=ss };
        }
        public override void Execute(ScriptableRenderContext ctx, ref RenderingData data)
        {
            var cmd = CommandBufferPool.Get(tag);
            using (new ProfilingScope(cmd, new ProfilingSampler(tag)))
            {
                ctx.ExecuteCommandBuffer(cmd); cmd.Clear();
                var ds = CreateDrawingSettings(tags, ref data, SortingCriteria.CommonTransparent);
                // 背景用原材质，强制Stencil State
                ctx.DrawRenderers(data.cullResults, ref ds, ref filtering, ref rsb);
            }
            ctx.ExecuteCommandBuffer(cmd); CommandBufferPool.Release(cmd);
        }
    }

    // 3) 人物半透明（可 override，也可保留作者材质）
    class CharacterAlphaPass : BasePass
    {
        Material overrideMat;
        RenderStateBlock rsb; // 仍受 NotEqual 1
        bool useOverride;
        public CharacterAlphaPass(Settings s) : base("UI P3 Character Alpha", s, RenderQueueRange.all, s.characterLayer)
        {
            overrideMat = s.characterAlphaOnlyMat;
            useOverride = s.useOverrideForCharacterAlpha;
            var ss = new StencilState(true, 0xFF,0xFF, CompareFunction.NotEqual, StencilOp.Keep, StencilOp.Keep, StencilOp.Keep);
            rsb = new RenderStateBlock(RenderStateMask.Stencil){ stencilReference=1, stencilState=ss };
        }
        public override void Execute(ScriptableRenderContext ctx, ref RenderingData data)
        {
            var cmd = CommandBufferPool.Get(tag);
            using (new ProfilingScope(cmd, new ProfilingSampler(tag)))
            {
                ctx.ExecuteCommandBuffer(cmd); cmd.Clear();
                var ds = CreateDrawingSettings(tags, ref data, SortingCriteria.CommonTransparent);
                if (useOverride && overrideMat != null) ds.overrideMaterial = overrideMat;
                // 强制 NotEqual 1，保证不透明部分被剔除（不重复上色）
                ctx.DrawRenderers(data.cullResults, ref ds, ref filtering, ref rsb);
            }
            ctx.ExecuteCommandBuffer(cmd); CommandBufferPool.Release(cmd);
        }
    }

    // 4) 生成 MIP
    class GenMipsPass : ScriptableRenderPass
    {
        string tag; RTHandle color; bool gen; public GenMipsPass(string t, RenderPassEvent e){ tag=t; renderPassEvent=e; }
        public void Setup(RTHandle c, bool g){ color=c; gen=g; }
        public override void Execute(ScriptableRenderContext ctx, ref RenderingData data)
        {
            if (!gen || color==null) return;
            var cmd = CommandBufferPool.Get(tag);
            using (new ProfilingScope(cmd, new ProfilingSampler(tag))) cmd.GenerateMips(color);
            ctx.ExecuteCommandBuffer(cmd); CommandBufferPool.Release(cmd);
        }
    }

    public Settings settings = new Settings();
    StencilPrePass _p1; BackgroundPass _p2; CharacterAlphaPass _p3; GenMipsPass _p4;

    RTHandle _col, _ds;
    int _gid; // _UI_BG
    public override void Create()
    {
        _p1 = new StencilPrePass(settings);
        _p2 = new BackgroundPass(settings);
        _p3 = new CharacterAlphaPass(settings);
        _p4 = new GenMipsPass("UI P4 GenMips", settings.injectEvent+1);
        _gid = Shader.PropertyToID(settings.globalTextureName);
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData data)
    {
        var desc = data.cameraData.cameraTargetDescriptor;
        int w = Mathf.Max(1, Mathf.RoundToInt(desc.width  * settings.resolutionScale));
        int h = Mathf.Max(1, Mathf.RoundToInt(desc.height * settings.resolutionScale));

        // (重)建
        if (_col==null || _col.rt.width!=w || _col.rt.height!=h)
        {
            _col?.Release(); _ds?.Release();
            var cd = new RenderTextureDescriptor(w,h){
                graphicsFormat = settings.useHDR ? GraphicsFormat.B10G11R11_UFloatPack32 : GraphicsFormat.R8G8B8A8_UNorm,
                depthStencilFormat = GraphicsFormat.None, msaaSamples=1, useMipMap=true, autoGenerateMips=false,
                sRGB = (QualitySettings.activeColorSpace==ColorSpace.Linear)
            };
            var dd = new RenderTextureDescriptor(w,h){
                graphicsFormat=GraphicsFormat.None, depthStencilFormat=GraphicsFormat.D24_UNorm_S8_UInt, msaaSamples=1,
                useMipMap=false, autoGenerateMips=false, sRGB=false
            };
            _col = RTHandles.Alloc(cd, FilterMode.Bilinear, name: settings.globalTextureName);
            _ds  = RTHandles.Alloc(dd, name: settings.globalTextureName+"_DS");
        }

        // 清屏
        {
            var cmd = CommandBufferPool.Get("UIBG Clear");
            cmd.SetRenderTarget(_col, _ds);
            cmd.ClearRenderTarget(true, true, settings.clearColor);
            renderer.EnqueuePass(new OneShot(cmd, settings.injectEvent));
        }

        // 入队
        _p1.Setup(_col,_ds); renderer.EnqueuePass(_p1);
        _p2.Setup(_col,_ds); renderer.EnqueuePass(_p2);
        _p3.Setup(_col,_ds); renderer.EnqueuePass(_p3);
        _p4.Setup(_col, settings.generateMips); renderer.EnqueuePass(_p4);

        // 曝光全局
        Shader.SetGlobalTexture(_gid, _col);
        // 提供屏幕→RT比例（供按钮屏幕UV换算）
        Shader.SetGlobalVector("_UIBG_UVScale", new Vector4((float)w/desc.width, (float)h/desc.height, 0, 0));
    }

    class OneShot : ScriptableRenderPass
    {
        CommandBuffer _cmd; public OneShot(CommandBuffer c, RenderPassEvent e){_cmd=c; renderPassEvent=e;}
        public override void Execute(ScriptableRenderContext ctx, ref RenderingData data)
        { ctx.ExecuteCommandBuffer(_cmd); CommandBufferPool.Release(_cmd); }
    }
}
