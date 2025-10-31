using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Experimental.Rendering; // GraphicsFormat
using System.Collections.Generic;

public class UIBGMaskAndBackgroundFeature : ScriptableRendererFeature
{
    [System.Serializable]
    public class Settings
    {
        [Header("目标RT（背景捕获+MIP来源）")]
        public string globalTextureName = "_UI_RT_BG"; // 全局曝光名（供 Shader.SetGlobalTexture）
        [Range(0.25f,1f)] public float resolutionScale = 1f;
        public bool useHDR = false;
        public Color clearColor = new Color(0,0,0,0);

        [Header("Layer 选择")]
        public LayerMask characterLayer;  // 人物（写模板）
        public LayerMask backgroundLayer; // 背景（读模板，Early-Stencil）

        [Header("人物 预通道材质（只写Stencil，不写颜色）")]
        public Material characterStencilWriteMat; // 你的 Hidden/CharacterStencilWrite

        [Header("注入时机（建议：BeforeRenderingTransparents）")]
        public RenderPassEvent injectEvent = RenderPassEvent.BeforeRenderingTransparents;

        [Header("MIP 生成")]
        public bool generateMips = true;          // 手动生成 MIP
    }

    class MaskPass : ScriptableRenderPass
    {
        readonly string _profilerTag;
        readonly Settings _settings;
        readonly List<ShaderTagId> _shaderTags = new(){
            new ShaderTagId("UniversalForward"),
            new ShaderTagId("UniversalForwardOnly"),
            new ShaderTagId("SRPDefaultUnlit")
        };

        FilteringSettings _filtering;
        Material _stencilWriteMat;

        RenderTargetIdentifier _colorRT;
        RenderTargetIdentifier _depthRT;

        public MaskPass(string tag, Settings s)
        {
            _profilerTag = tag;
            _settings = s;
            renderPassEvent = s.injectEvent;
            _filtering = new FilteringSettings(RenderQueueRange.all, s.characterLayer);
            _stencilWriteMat = s.characterStencilWriteMat;
        }

        public void Setup(RenderTargetIdentifier colorRT, RenderTargetIdentifier depthRT)
        {
            _colorRT = colorRT;
            _depthRT = depthRT;
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            ConfigureTarget(_colorRT, _depthRT);
            // 不清颜色，只写模板（材质里 ColorMask 0）
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData data)
        {
            if (_stencilWriteMat == null) return;

            var cmd = CommandBufferPool.Get(_profilerTag);
            using (new ProfilingScope(cmd, new ProfilingSampler(_profilerTag)))
            {
                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();

                var drawSettings = CreateDrawingSettings(_shaderTags, ref data, SortingCriteria.CommonTransparent);
                // 覆盖材质 = 仅写Stencil，不写颜色（alpha clip 由材质控制）
                drawSettings.overrideMaterial = _stencilWriteMat;

                context.DrawRenderers(data.cullResults, ref drawSettings, ref _filtering);
            }
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }
    }

    class BackgroundPass : ScriptableRenderPass
    {
        readonly string _profilerTag;
        readonly Settings _settings;
        readonly List<ShaderTagId> _shaderTags = new(){
            new ShaderTagId("UniversalForward"),
            new ShaderTagId("UniversalForwardOnly"),
            new ShaderTagId("SRPDefaultUnlit")
        };

        FilteringSettings _filtering;

        RenderTargetIdentifier _colorRT;
        RenderTargetIdentifier _depthRT;

        // 通过 RenderStateBlock 强制 Early-Stencil：NotEqual 1
        readonly StencilState _stencilState = new StencilState(
            enabled: true,
            readMask: 0xFF, writeMask: 0xFF,
            compareFunction: CompareFunction.NotEqual,
            passOperation: StencilOp.Keep,
            failOperation: StencilOp.Keep,
            zFailOperation: StencilOp.Keep
        );
        readonly int _stencilRef = 1;
        RenderStateBlock _rsb;

        public BackgroundPass(string tag, Settings s)
        {
            _profilerTag = tag;
            _settings = s;
            renderPassEvent = s.injectEvent;
            _filtering = new FilteringSettings(RenderQueueRange.all, s.backgroundLayer);

            _rsb = new RenderStateBlock(RenderStateMask.Stencil)
            {
                stencilReference = _stencilRef,
                stencilState = _stencilState
            };
        }

        public void Setup(RenderTargetIdentifier colorRT, RenderTargetIdentifier depthRT)
        {
            _colorRT = colorRT;
            _depthRT = depthRT;
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            ConfigureTarget(_colorRT, _depthRT);
            // 不清，因为我们可能已有基底/或上一帧内容，这里只画背景layer
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData data)
        {
            var cmd = CommandBufferPool.Get(_profilerTag);
            using (new ProfilingScope(cmd, new ProfilingSampler(_profilerTag)))
            {
                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();

                var drawSettings = CreateDrawingSettings(_shaderTags, ref data, SortingCriteria.CommonTransparent);
                // 不覆盖材质，保持背景自身着色，但强制 Stencil: NotEqual 1
                context.DrawRenderers(data.cullResults, ref drawSettings, ref _filtering, ref _rsb);
            }
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }
    }

    class GenerateMipsPass : ScriptableRenderPass
    {
        readonly string _profilerTag;
        RenderTargetIdentifier _colorRT;
        bool _doGenerate;

        public GenerateMipsPass(string tag, RenderPassEvent e)
        {
            _profilerTag = tag;
            renderPassEvent = e;
        }
        public void Setup(RenderTargetIdentifier colorRT, bool doGen)
        {
            _colorRT = colorRT;
            _doGenerate = doGen;
        }
        public override void Execute(ScriptableRenderContext context, ref RenderingData data)
        {
            if (!_doGenerate) return;
            var cmd = CommandBufferPool.Get(_profilerTag);
            using (new ProfilingScope(cmd, new ProfilingSampler(_profilerTag)))
            {
                cmd.GenerateMips(_colorRT);
            }
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }
    }

    // ====== Feature fields ======
    public Settings settings = new Settings();

    // 内部 RT
    static RenderTexture s_ColorRT;
    static RenderTexture s_DepthRT;

    MaskPass _maskPass;
    BackgroundPass _bgPass;
    GenerateMipsPass _mipsPass;

    public override void Create()
    {
        _maskPass = new MaskPass("UIBG Mask (StencilWrite)", settings);
        _bgPass   = new BackgroundPass("UIBG Background (EarlyStencil)", settings);
        _mipsPass = new GenerateMipsPass("UIBG GenerateMips", settings.injectEvent + 1); // 紧跟其后
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData data)
    {
        if (settings.characterStencilWriteMat == null) return;

        // 分辨率
        int w = Mathf.Max(1, Mathf.RoundToInt(data.cameraData.cameraTargetDescriptor.width  * settings.resolutionScale));
        int h = Mathf.Max(1, Mathf.RoundToInt(data.cameraData.cameraTargetDescriptor.height * settings.resolutionScale));

        // 创建/重建 RT：颜色（带mip，autoGenerate=false）& 深度模板 D24S8
        if (s_ColorRT == null || s_ColorRT.width != w || s_ColorRT.height != h)
        {
            if (s_ColorRT != null) s_ColorRT.Release();
            if (s_DepthRT != null) s_DepthRT.Release();

            var colorDesc = new RenderTextureDescriptor(w, h);
            colorDesc.graphicsFormat     = settings.useHDR ? GraphicsFormat.B10G11R11_UFloatPack32 : GraphicsFormat.R8G8B8A8_UNorm;
            colorDesc.depthStencilFormat = GraphicsFormat.None; // 颜色贴图不带DS
            colorDesc.msaaSamples        = 1;
            colorDesc.useMipMap          = true;
            colorDesc.autoGenerateMips   = false; // 我们手动GenerateMips
            colorDesc.sRGB               = (QualitySettings.activeColorSpace == ColorSpace.Linear);
            s_ColorRT = new RenderTexture(colorDesc){ name = settings.globalTextureName, filterMode = FilterMode.Bilinear };
            s_ColorRT.Create();

            var dsDesc = new RenderTextureDescriptor(w, h);
            dsDesc.graphicsFormat     = GraphicsFormat.None;
            dsDesc.depthStencilFormat = GraphicsFormat.D24_UNorm_S8_UInt; // ✅ 关键：独立的DS RT
            dsDesc.msaaSamples        = 1;
            dsDesc.useMipMap          = false;
            dsDesc.autoGenerateMips   = false;
            dsDesc.sRGB               = false;
            s_DepthRT = new RenderTexture(dsDesc){ name = settings.globalTextureName + "_DS" };
            s_DepthRT.Create();
        }

        // 每帧把全局纹理名曝光给 shader（按钮/合成可直接采样）
        Shader.SetGlobalTexture(settings.globalTextureName, s_ColorRT);

        // 先清一次颜色（可根据你需要调整清屏时机）
        {
            var cmd = CommandBufferPool.Get("UIBG Clear");
            cmd.SetRenderTarget(s_ColorRT, s_DepthRT);
            cmd.ClearRenderTarget(true, true, settings.clearColor);
            renderer.EnqueuePass(new OneShotCmdPass(cmd)); // 小工具pass（见下）
        }

        // 设置+入队两个Pass
        _maskPass.Setup(s_ColorRT, s_DepthRT);
        _bgPass.Setup(s_ColorRT, s_DepthRT);
        _mipsPass.Setup(s_ColorRT, settings.generateMips);

        renderer.EnqueuePass(_maskPass);
        renderer.EnqueuePass(_bgPass);
        renderer.EnqueuePass(_mipsPass);
    }

    class OneShotCmdPass : ScriptableRenderPass
    {
        CommandBuffer _cmd;
        public OneShotCmdPass(CommandBuffer cmd){ _cmd = cmd; renderPassEvent = RenderPassEvent.BeforeRenderingTransparents; }
        public override void Execute(ScriptableRenderContext context, ref RenderingData data)
        {
            context.ExecuteCommandBuffer(_cmd);
            CommandBufferPool.Release(_cmd);
        }
    }
}
