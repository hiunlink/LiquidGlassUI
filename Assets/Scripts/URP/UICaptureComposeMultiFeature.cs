using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Experimental.Rendering;
using System.Collections.Generic;

public class UICaptureComposeMultiFeature : ScriptableRendererFeature
{
    [System.Serializable]
    public class LayerConfig
    {
        public LayerMask layer;
        public bool blur = false;       // 该层是否加入模糊
    }

    [System.Serializable]
    public class Settings
    {
        [Header("输出纹理（给 ReplaceFeature / 按钮采样）")]
        public string globalTextureName = "_UI_BG";
        [Range(0.25f, 1f)] public float resolutionScale = 1f;
        public bool useHDR = false;
        public Color clearColor = new Color(0,0,0,0);

        [Header("人物层（用于预通道与半透明）")]
        public LayerMask characterLayer;

        [Header("背景层数组（顺序 = 从远到近）")]
        public LayerConfig[] backgroundLayers;

        [Header("Override 材质")]
        public Material characterStencilWriteMat; // Hidden/CharacterStencilWrite
        public Material characterAlphaOnlyMat;    // UI/CharacterAlphaOnly
        public bool useOverrideForCharacterAlpha = true;

        [Header("模糊 & 合成")]
        public bool generateMips = true;          // 使用 MIP 链作为 blur
        [Range(0,8)] public int compositeMip = 3; // 合成到 baseRT 时使用的 MIP 等级（共享）

        [Header("注入时机")]
        public RenderPassEvent injectEvent = RenderPassEvent.BeforeRenderingTransparents;
    }

    // ─────────── Base Pass ───────────
    abstract class BasePass : ScriptableRenderPass
    {
        protected readonly string tag;
        protected readonly Settings s;
        protected readonly List<ShaderTagId> tags = new(){
            new ShaderTagId("UniversalForward"),
            new ShaderTagId("UniversalForwardOnly"),
            new ShaderTagId("SRPDefaultUnlit")
        };
        protected RTHandle colorRT, depthRT;

        protected BasePass(string tag, Settings s){ this.tag=tag; this.s=s; renderPassEvent = s.injectEvent; }

        public void Setup(RTHandle col, RTHandle dep){ colorRT=col; depthRT=dep; }
        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData data)
        {
            // ⭐ 关键：depthRT 可能为 null
            if (depthRT != null)
                ConfigureTarget(colorRT, depthRT);
            else
                ConfigureTarget(colorRT); // 只绑定颜色
            ConfigureClear(ClearFlag.None, Color.clear);
        }
    }

    // P1 人物不透明 Stencil 预通道（只写模板，不写颜色）
    class StencilPrePass : BasePass
    {
        FilteringSettings filtering;
        Material mat;
        public StencilPrePass(Settings s) : base("UI P1 StencilPre (Char Opaque)", s)
        { filtering = new FilteringSettings(RenderQueueRange.all, s.characterLayer); mat = s.characterStencilWriteMat; }

        public override void Execute(ScriptableRenderContext ctx, ref RenderingData data)
        {
            if (mat == null) return;
            var cmd = CommandBufferPool.Get(tag);
            using (new ProfilingScope(cmd, new ProfilingSampler(tag)))
            {
                ctx.ExecuteCommandBuffer(cmd); cmd.Clear();
                var ds = CreateDrawingSettings(tags, ref data, SortingCriteria.CommonTransparent);
                ds.overrideMaterial = mat; // Hidden/CharacterStencilWrite
                ctx.DrawRenderers(data.cullResults, ref ds, ref filtering);
            }
            ctx.ExecuteCommandBuffer(cmd); CommandBufferPool.Release(cmd);
        }
    }

    // P2 收集“需要模糊”的背景 → 画到 blurRT（不做 StencilTest）
    class BGCollectBlurPass : BasePass
    {
        LayerConfig[] layers;
        public BGCollectBlurPass(Settings s) : base("UI P2 BG Collect(blur)", s){ layers = s.backgroundLayers; }

        public override void Execute(ScriptableRenderContext ctx, ref RenderingData data)
        {
            if (layers == null || layers.Length == 0) return;
            var cmd = CommandBufferPool.Get(tag);
            using (new ProfilingScope(cmd, new ProfilingSampler(tag)))
            {
                ctx.ExecuteCommandBuffer(cmd); cmd.Clear();
                var ds = CreateDrawingSettings(tags, ref data, SortingCriteria.CommonTransparent);

                foreach (var lc in layers)
                {
                    if (lc == null || !lc.blur) continue;
                    var filter = new FilteringSettings(RenderQueueRange.all, lc.layer);
                    // 不注入 Stencil 状态 → 不裁剪（解决模糊断裂）
                    ctx.DrawRenderers(data.cullResults, ref ds, ref filter);
                }
            }
            ctx.ExecuteCommandBuffer(cmd); CommandBufferPool.Release(cmd);
        }
    }

    // P3 对 blurRT 生成 MIP（A 型虚化）
    class GenMipsPass : ScriptableRenderPass
    {
        string tag; RTHandle blur; bool doGen;
        public GenMipsPass(string t, RenderPassEvent e){ tag=t; renderPassEvent=e; }
        public void Setup(RTHandle blurRT, bool gen){ blur=blurRT; doGen=gen; }
        public override void Execute(ScriptableRenderContext ctx, ref RenderingData data)
        {
            if (!doGen || blur==null) return;
            var cmd = CommandBufferPool.Get(tag);
            using (new ProfilingScope(cmd, new ProfilingSampler(tag))) cmd.GenerateMips(blur);
            ctx.ExecuteCommandBuffer(cmd); CommandBufferPool.Release(cmd);
        }
    }

    // P4 将 blurRT（取 MIP）合成到 baseRT（此时 NotEqual 1，避免人物不透明下重复）
    class CompositeBlurToBasePass : BasePass
    {
        Material mat; int _Src, _Mip;
        RenderStateBlock rsb;

        public CompositeBlurToBasePass(Settings s, Material composeMat) : base("UI P4 Composite(blur→base)", s)
        {
            mat = composeMat;
            _Src = Shader.PropertyToID("_SourceTex");
            _Mip = Shader.PropertyToID("_Mip");
            var ss = new StencilState(true, 0xFF,0xFF, CompareFunction.NotEqual, StencilOp.Keep, StencilOp.Keep, StencilOp.Keep);
            rsb = new RenderStateBlock(RenderStateMask.Stencil){ stencilReference=1, stencilState=ss };
        }

        RTHandle blurRT;
        public void SetSource(RTHandle blur){ blurRT = blur; }

        public override void Execute(ScriptableRenderContext ctx, ref RenderingData data)
        {
            if (mat == null || blurRT == null) return;

            var cmd = CommandBufferPool.Get(tag);
            using (new ProfilingScope(cmd, new ProfilingSampler(tag)))
            {
                // 用全屏网格 + 自带 Stencil（用 rsb 约束）
                ctx.ExecuteCommandBuffer(cmd); cmd.Clear();
                var ds = CreateDrawingSettings(new List<ShaderTagId>{ new ShaderTagId("SRPDefaultUnlit") }, ref data, SortingCriteria.None);
                mat.SetTexture(_Src, blurRT);
                mat.SetFloat(_Mip, s.compositeMip);

                // 画一个全屏三角形：使用 DrawProcedural
                cmd.SetRenderTarget(colorRT, depthRT);
                cmd.DrawProcedural(Matrix4x4.identity, mat, 0, MeshTopology.Triangles, 3, 1);
            }
            ctx.ExecuteCommandBuffer(cmd); CommandBufferPool.Release(cmd);
        }
    }

    // P5 绘制“非模糊”的背景层 → 直接画到 baseRT（NotEqual 1）
    class BGNonBlurToBasePass : BasePass
    {
        LayerConfig[] layers;
        RenderStateBlock rsb;

        public BGNonBlurToBasePass(Settings s) : base("UI P5 BG NonBlur→base", s)
        {
            layers = s.backgroundLayers;
            var ss = new StencilState(true, 0xFF,0xFF, CompareFunction.NotEqual, StencilOp.Keep, StencilOp.Keep, StencilOp.Keep);
            rsb = new RenderStateBlock(RenderStateMask.Stencil){ stencilReference=1, stencilState=ss };
        }

        public override void Execute(ScriptableRenderContext ctx, ref RenderingData data)
        {
            if (layers == null || layers.Length == 0) return;
            var cmd = CommandBufferPool.Get(tag);
            using (new ProfilingScope(cmd, new ProfilingSampler(tag)))
            {
                ctx.ExecuteCommandBuffer(cmd); cmd.Clear();
                var ds = CreateDrawingSettings(tags, ref data, SortingCriteria.CommonTransparent);

                foreach (var lc in layers)
                {
                    if (lc == null || lc.blur) continue; // 只画不模糊的
                    var filter = new FilteringSettings(RenderQueueRange.all, lc.layer);
                    ctx.DrawRenderers(data.cullResults, ref ds, ref filter, ref rsb);
                }
            }
            ctx.ExecuteCommandBuffer(cmd); CommandBufferPool.Release(cmd);
        }
    }

    // P6 人物半透明 → baseRT（NotEqual 1）
    class CharacterAlphaPass : BasePass
    {
        Material overrideMat; bool useOverride;
        RenderStateBlock rsb;

        public CharacterAlphaPass(Settings s) : base("UI P6 Character AlphaOnly", s)
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
                if (useOverride && overrideMat) ds.overrideMaterial = overrideMat;
                var filter = new FilteringSettings(RenderQueueRange.all, s.characterLayer);
                ctx.DrawRenderers(data.cullResults, ref ds, ref filter, ref rsb);
            }
            ctx.ExecuteCommandBuffer(cmd); CommandBufferPool.Release(cmd);
        }
    }

    // ─────────── Fields ───────────
    public Settings settings = new Settings();

    StencilPrePass      _p1;
    BGCollectBlurPass   _p2;
    GenMipsPass         _p3;
    CompositeBlurToBasePass _p4;
    BGNonBlurToBasePass _p5;
    CharacterAlphaPass  _p6;
    GenMipsPass _p7;

    RTHandle _baseCol, _baseDS, _blurRT;
    int _gid, _uvScaleId;

    // 合成材质（带 _SourceTex + _Mip，shader 在下节）
    [SerializeField] Material _composeMat;

    public override void Create()
    {
        _p1 = new StencilPrePass(settings);
        _p2 = new BGCollectBlurPass(settings);
        _p3 = new GenMipsPass("UI P3 GenMips(blurRT)", settings.injectEvent+1);
        if (_composeMat == null) _composeMat = new Material(Shader.Find("Hidden/UIBGCompositeStencilMip"));
        _p4 = new CompositeBlurToBasePass(settings, _composeMat);
        _p5 = new BGNonBlurToBasePass(settings);
        _p6 = new CharacterAlphaPass(settings);
        _p7 = new GenMipsPass("UI P7 GenMips(baseRT)", settings.injectEvent + 2);

        _gid = Shader.PropertyToID(settings.globalTextureName);
        _uvScaleId = Shader.PropertyToID("_UIBG_UVScale");
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData data)
    {
        var desc = data.cameraData.cameraTargetDescriptor;
        int w = Mathf.Max(1, Mathf.RoundToInt(desc.width  * settings.resolutionScale));
        int h = Mathf.Max(1, Mathf.RoundToInt(desc.height * settings.resolutionScale));

        // (重)建 RT：base / blur / DS
        if (_baseCol == null || _baseCol.rt.width!=w || _baseCol.rt.height!=h)
        {
            _baseCol?.Release(); _baseDS?.Release(); _blurRT?.Release();

            var col = new RenderTextureDescriptor(w,h){
                graphicsFormat = settings.useHDR ? GraphicsFormat.B10G11R11_UFloatPack32 : GraphicsFormat.R8G8B8A8_UNorm,
                depthStencilFormat = GraphicsFormat.None, msaaSamples=1, useMipMap=true, autoGenerateMips=false,
                sRGB = (QualitySettings.activeColorSpace==ColorSpace.Linear)
            };
            var ds = new RenderTextureDescriptor(w,h){
                graphicsFormat=GraphicsFormat.None, depthStencilFormat=GraphicsFormat.D24_UNorm_S8_UInt, msaaSamples=1,
                useMipMap=false, autoGenerateMips=false, sRGB=false
            };
            var blur = new RenderTextureDescriptor(w,h){
                graphicsFormat = settings.useHDR ? GraphicsFormat.B10G11R11_UFloatPack32 : GraphicsFormat.R8G8B8A8_UNorm,
                depthStencilFormat = GraphicsFormat.None, msaaSamples=1, useMipMap=true, autoGenerateMips=false,
                sRGB = (QualitySettings.activeColorSpace==ColorSpace.Linear)
            };
            _baseCol = RTHandles.Alloc(col, FilterMode.Bilinear, name: settings.globalTextureName);
            _baseDS  = RTHandles.Alloc(ds, name: settings.globalTextureName+"_DS");
            _blurRT  = RTHandles.Alloc(blur, FilterMode.Bilinear, name: settings.globalTextureName+"_BLUR");
        }

        // 清 base 与 blur
        {
            var cmd = CommandBufferPool.Get("UIBG Clear");
            cmd.SetRenderTarget(_baseCol, _baseDS);
            cmd.ClearRenderTarget(true, true, settings.clearColor);
            cmd.SetRenderTarget(_blurRT);
            cmd.ClearRenderTarget(false, true, Color.clear);
            renderer.EnqueuePass(new OneShot(cmd, settings.injectEvent));
        }

        // 顺序：P1(人模板) → P2(收集需模糊BG到blurRT) → P3(生成MIP) → P4(blur合成到base, NotEqual) → P5(非模糊BG到base, NotEqual) → P6(人物半透明 NotEqual)
        _p1.Setup(_baseCol, _baseDS);              renderer.EnqueuePass(_p1);
        _p2.Setup(_blurRT,  null);                 renderer.EnqueuePass(_p2);
        _p3.Setup (_blurRT, settings.generateMips);renderer.EnqueuePass(_p3);
        _p4.Setup (_baseCol, _baseDS); _p4.SetSource(_blurRT); renderer.EnqueuePass(_p4);
        _p5.Setup (_baseCol, _baseDS);             renderer.EnqueuePass(_p5);
        _p6.Setup (_baseCol, _baseDS);             renderer.EnqueuePass(_p6);
        _p7.Setup(_baseCol,  settings.generateMips);        renderer.EnqueuePass(_p7);

        // 曝光全局
        Shader.SetGlobalTexture(_gid, _baseCol);
        Shader.SetGlobalVector(_uvScaleId, new Vector4((float)w/desc.width, (float)h/desc.height, 0, 0));
    }

    class OneShot : ScriptableRenderPass
    {
        CommandBuffer _cmd; public OneShot(CommandBuffer c, RenderPassEvent e){_cmd=c; renderPassEvent=e;}
        public override void Execute(ScriptableRenderContext ctx, ref RenderingData data)
        { ctx.ExecuteCommandBuffer(_cmd); CommandBufferPool.Release(_cmd); }
    }
}
