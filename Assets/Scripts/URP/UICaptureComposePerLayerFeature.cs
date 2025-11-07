// Assets/Scripts/URP/UICaptureComposePerLayerFeature.cs
// URP 2022.3+
// 需要你已有的 ReplaceFeature + Hidden/UIBGCompositeStencilMip.shader（_SourceTex/_Mip）
// 需要前景 Shader 提供 LightMode = "StencilPrepass" 与 "AlphaOnly" 两个 Pass
// 在 StencilPrepass 内使用关键字 FG_PREPASS_DRAW_COLOR 控制是否写颜色（否则仅写模板）

using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Experimental.Rendering;

// 模糊算法
public enum BlurAlgorithm
{
    MipChain,          // 使用 MIP 链（UIBGCompositeStencilMip）
    GaussianSeparable  // 高斯分离（H+V）
}

public class UICaptureComposePerLayerFeature : ScriptableRendererFeature
{
    // ========== 可配置结构 ==========
    [System.Serializable]
    public class LayerConfig
    {
        [Tooltip("该层对象所在 Layer")]
        public LayerMask layer;

        [Tooltip("是否为前景层（大面积 Opaque 遮挡）")]
        public bool isForeground = false;

        [Tooltip("该层是否需要模糊（blurRT→MIP→按本层blurMip合成回base）")]
        public bool blur = false;

        
        [ShowIf("blur")]
        public BlurAlgorithm blurAlgorithm = BlurAlgorithm.MipChain;
        [ShowIf("blur","blurAlgorithm", (int)BlurAlgorithm.MipChain)]
        [Range(0, 8)] public float blurMip = 3;
        [ShowIf("blur","blurAlgorithm", (int)BlurAlgorithm.GaussianSeparable)]
        [Range(0f, 6f)] public float gaussianSigma = 2.0f;
        [ShowIf("blur","blurAlgorithm", (int)BlurAlgorithm.GaussianSeparable)]
        [Range(1, 5)] public int iteration = 1;
    }

    [System.Serializable]
    public class Settings
    {
        [Header("输出全局贴图名（供 UI 玻璃采样 / Replace 使用）")]
        public string globalTextureName = "_UI_BG";

        [Header("输出分辨率缩放")]
        [Range(0.25f, 1f)] public float resolutionScale = 1f;

        [Header("HDR 输出")]
        public bool useHDR = false;

        [Header("清屏颜色")]
        public Color clearColor = new Color(0, 0, 0, 0);
        
        [Header("MIP 链合成材质（必须内置 Stencil NotEqual）")]
        public Material mipCompositeMat; // e.g. Hidden/UIBGCompositeStencilMip

        [Header("Gaussian 模糊材质（H/V 两个pass）")]
        public Material gaussianSeparableMat; // e.g. Hidden/UIBG_GaussianSeparable

        [Header("Gaussian 合成 Copy 材质（必须内置 Stencil NotEqual）")]
        public Material gaussianCompositeMat; // e.g. Hidden/UIBGCompositeCopyStencil

        [Header("按从远到近排序（config[0] 最底层）")]
        public LayerConfig[] layers;

        [Header("是否生成 MIP（对 blurRT 以及最终 baseRT 均生效）")]
        public bool generateMips = true;

        [Header("注入时机")]
        public RenderPassEvent injectEvent = RenderPassEvent.BeforeRenderingTransparents;
    }

    // -------------------- Runtime Cache --------------------
    private bool _dirty = true;
    public void SetDirty(bool v) => _dirty = v;
    public bool IsDirty => _dirty;
    
    
    // ========== 通用绘制 Tag ==========
    private static List<ShaderTagId> k_DefaultTags = new List<ShaderTagId>();

    // ========== 公共 Keyword ==========
    private static ShaderKeyword KW_FG_PREPASS_DRAW_COLOR;

    // ========== 通用小工具 Pass ==========
    abstract class BasePass : ScriptableRenderPass
    {
        protected readonly string tag;
        protected RTHandle colorRT, depthRT;

        protected BasePass(string tag, RenderPassEvent evt) { this.tag = tag; renderPassEvent = evt; }

        public void SetupTargets(RTHandle color, RTHandle depth)
        {
            colorRT = color;
            depthRT = depth; // 可为 null
        }
        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData data)
        {
            if (colorRT == null) return;
            if (depthRT != null) ConfigureTarget(colorRT, depthRT);
            else ConfigureTarget(colorRT);
            ConfigureClear(ClearFlag.None, Color.clear);
        }
    }

    // 使用指定 LightMode 绘制某个 Layer；可开关 FG_PREPASS_DRAW_COLOR 关键字
    class DrawWithLightModePass : BasePass
    {
        readonly string lightMode;
        readonly LayerMask layer;
        readonly bool enableKeywordDrawColor; // 仅在 StencilPrepass 中用到
        readonly bool writeStencil;           // 仅保证你的 shader 内部去写；此处不强注RenderState
        public DrawWithLightModePass(string tag, RenderPassEvent evt,
            string lightMode, LayerMask layer, bool enableKeywordDrawColor, bool writeStencil = true)
            : base(tag, evt)
        {
            this.lightMode = lightMode;
            this.layer = layer;
            this.enableKeywordDrawColor = enableKeywordDrawColor;
            this.writeStencil = writeStencil;
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

    // 用默认 URP Tag 绘制某个 Layer；可选强制 Stencil NotEqual 1（用于背景回填/合成阶段）
    class DrawDefaultPass : BasePass
    {
        readonly LayerMask layer;
        readonly bool useStencilNotEqual1;

        RenderStateBlock rsb;

        public DrawDefaultPass(string tag, RenderPassEvent evt, LayerMask layer, bool useStencilNotEqual1)
            : base(tag, evt)
        {
            this.layer = layer;
            this.useStencilNotEqual1 = useStencilNotEqual1;

            if (useStencilNotEqual1)
            {
                var ss = new StencilState(
                    enabled: true,
                    readMask: 0xFF, writeMask: 0xFF,
                    compareFunction: CompareFunction.NotEqual,
                    passOperation: StencilOp.Keep,
                    failOperation: StencilOp.Keep,
                    zFailOperation: StencilOp.Keep);
                rsb = new RenderStateBlock(RenderStateMask.Stencil)
                {
                    stencilReference = 1,
                    stencilState = ss
                };
            }
        }

        public override void Execute(ScriptableRenderContext ctx, ref RenderingData data)
        {
            var cmd = CommandBufferPool.Get(tag);
            using (new ProfilingScope(cmd, new ProfilingSampler(tag)))
            {
                ctx.ExecuteCommandBuffer(cmd); cmd.Clear();

                var ds = CreateDrawingSettings(k_DefaultTags, ref data, SortingCriteria.CommonTransparent);
                var fs = new FilteringSettings(RenderQueueRange.all, layer);

                if (useStencilNotEqual1)
                    ctx.DrawRenderers(data.cullResults, ref ds, ref fs, ref rsb);
                else
                    ctx.DrawRenderers(data.cullResults, ref ds, ref fs);
            }
            ctx.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }
    }

    // 全屏合成：将 blurRT（取 mip）合成到 baseRT；RenderState 在外部可用 NotEqual 1 包裹
    class CompositeBlurToBasePass : BasePass
    {
        readonly Material mat; readonly int _Src = Shader.PropertyToID("_SourceTex"), _Mip = Shader.PropertyToID("_Mip");
        readonly float mipLevel;
        readonly bool useStencilNotEqual1;
        public CompositeBlurToBasePass(string tag, RenderPassEvent evt, Material mat, float mipLevel, bool useStencilNotEqual1)
            : base(tag, evt) { 
            this.mat = mat; 
            this.mipLevel = mipLevel;
            this.useStencilNotEqual1 = useStencilNotEqual1;
        }

        RTHandle src;
        public void SetSource(RTHandle blurRT) { src = blurRT; }

        public override void Execute(ScriptableRenderContext ctx, ref RenderingData data)
        {
            if (mat == null || src == null || colorRT == null) return;

            var cmd = CommandBufferPool.Get(tag);
            using (new ProfilingScope(cmd, new ProfilingSampler(tag)))
            {
                // 这里不直接设置 RenderStateBlock，而是建议外部用 DrawDefaultPass 做 NotEqual 包裹；
                // 但是我们用 full-screen procedural 直接写入 baseRT，需要在材质里读 src + mip。
                cmd.SetRenderTarget(colorRT, depthRT);
                var newMat = new Material(mat);
                newMat.SetTexture(_Src, src);
                newMat.SetFloat(_Mip, mipLevel);
                // 画一个全屏三角形
                if (useStencilNotEqual1)
                {
                    cmd.DrawProcedural(Matrix4x4.identity, newMat, 1, MeshTopology.Triangles, 3, 1);
                }
                else
                {
                    cmd.DrawProcedural(Matrix4x4.identity, newMat, 0, MeshTopology.Triangles, 3, 1);
                }
            }
            ctx.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }
    }

    // 通用 GenerateMips（可反复复用）
    class GenMipsPass : ScriptableRenderPass
    {
        RTHandle target; bool doGen; readonly string tag;
        public GenMipsPass(string tag, RenderPassEvent evt){ this.tag=tag; renderPassEvent=evt; }
        public void Setup(RTHandle rt, bool gen){ target=rt; doGen=gen; }
        public override void Execute(ScriptableRenderContext ctx, ref RenderingData data)
        {
            if (!doGen || target==null) return;
            var cmd = CommandBufferPool.Get(tag);
            using (new ProfilingScope(cmd, new ProfilingSampler(tag))) cmd.GenerateMips(target);
            ctx.ExecuteCommandBuffer(cmd); CommandBufferPool.Release(cmd);
        }
    }

    // ========== Feature 本体 ==========
    public Settings settings = new Settings();

    // RT
    RTHandle _baseCol, _baseDS, _blurRT;

    // 复用的通用 Pass 实例容器（每帧创建/复用）
    readonly List<ScriptableRenderPass> tempPasses = new();

    // 全局名
    int _gid, _uvScaleId;
    
    public override void Create()
    {
        if (settings.mipCompositeMat == null)
            settings.mipCompositeMat = new Material(Shader.Find("Hidden/UIBGCompositeStencilMip"));
        if (settings.gaussianSeparableMat == null)
            settings.gaussianSeparableMat = new Material(Shader.Find("Hidden/UIBG_GaussianSeparable"));
        if (settings.gaussianCompositeMat == null)
            settings.gaussianCompositeMat = new Material(Shader.Find("Hidden/UIBGCompositeCopyStencil"));

        _gid = Shader.PropertyToID(settings.globalTextureName);
        _uvScaleId = Shader.PropertyToID("_UIBG_UVScale");
        k_DefaultTags = new()
        {
            new ShaderTagId("UniversalForward"),
            new ShaderTagId("UniversalForwardOnly"),
            new ShaderTagId("SRPDefaultUnlit"),
        };
        KW_FG_PREPASS_DRAW_COLOR = new ShaderKeyword("FG_PREPASS_DRAW_COLOR");
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData data)
    {
        // 忽略 SceneView 相机（避免拉伸或重复渲染）
        if (data.cameraData.isSceneViewCamera)
        {
            return;
        }
        if (settings.layers == null || settings.layers.Length == 0) return;

#if UNITY_EDITOR
        // 在编辑器但未运行时强制每帧重绘
        if (!Application.isPlaying)
        {
            _dirty = true;
        }
#endif
        // --- 如果界面没变化并已有缓存，直接复用 ---
        if (!_dirty && _baseCol != null)
        {
            Shader.SetGlobalTexture(_gid, _baseCol);
            Shader.SetGlobalVector(_uvScaleId, new Vector4(
                (float)_baseCol.rt.width / data.cameraData.cameraTargetDescriptor.width,
                (float)_baseCol.rt.height / data.cameraData.cameraTargetDescriptor.height,
                0, 0));
            return;
        }
        
        // --- 继续执行完整渲染 ---
        _dirty = false; // 渲染后标记为“干净”
        
        tempPasses.Clear();

        var camDesc = data.cameraData.cameraTargetDescriptor;
        int w = Mathf.Max(1, Mathf.RoundToInt(camDesc.width  * settings.resolutionScale));
        int h = Mathf.Max(1, Mathf.RoundToInt(camDesc.height * settings.resolutionScale));

        // (重)建 RT
        if (_baseCol==null || _baseCol.rt.width!=w || _baseCol.rt.height!=h)
        {
            _baseCol?.Release(); _baseDS?.Release(); _blurRT?.Release();

            var col = new RenderTextureDescriptor(w,h){
                graphicsFormat = settings.useHDR ? GraphicsFormat.B10G11R11_UFloatPack32 : GraphicsFormat.R8G8B8A8_UNorm,
                depthStencilFormat = GraphicsFormat.None,
                msaaSamples=1, useMipMap=true, autoGenerateMips=false,
                sRGB = (QualitySettings.activeColorSpace==ColorSpace.Linear)
            };
            var ds = new RenderTextureDescriptor(w,h){
                graphicsFormat=GraphicsFormat.None, depthStencilFormat=GraphicsFormat.D24_UNorm_S8_UInt,
                msaaSamples=1, useMipMap=false, autoGenerateMips=false, sRGB=false
            };
            var blur = new RenderTextureDescriptor(w,h){
                graphicsFormat = settings.useHDR ? GraphicsFormat.B10G11R11_UFloatPack32 : GraphicsFormat.R8G8B8A8_UNorm,
                depthStencilFormat = GraphicsFormat.None,
                msaaSamples=1, useMipMap=true, autoGenerateMips=false,
                sRGB = (QualitySettings.activeColorSpace==ColorSpace.Linear)
            };

            _baseCol = RTHandles.Alloc(col, FilterMode.Bilinear, name: settings.globalTextureName);
            _baseDS  = RTHandles.Alloc(ds,  name: settings.globalTextureName+"_DS");
            _blurRT  = RTHandles.Alloc(blur,FilterMode.Bilinear, name: settings.globalTextureName+"_BLUR", wrapMode: TextureWrapMode.Clamp);
        }

        // 清 base / blur
        {
            var cmd = CommandBufferPool.Get("UIBG.Clear");
            cmd.SetRenderTarget(_baseCol, _baseDS);
            cmd.ClearRenderTarget(true, true, settings.clearColor);
            cmd.SetRenderTarget(_blurRT);
            cmd.ClearRenderTarget(false, true, Color.clear);
            renderer.EnqueuePass(new OneShot(cmd, settings.injectEvent));
        }

        var layers = settings.layers;
        var evt = settings.injectEvent;

        // ---------- Phase S：前景模板预写（并按规则决定是否同时画Opaque） ----------
        for (int i = 0; i < layers.Length; i++)
        {
            var L = layers[i];
            if (!L.isForeground) continue;

            bool canOpaqueOptimize = ShouldDoOpaquePrepass(i, layers); // 由“之前是否存在模糊层”决定
            var p = new DrawWithLightModePass(
                tag: $"S.FG_StencilPrepass[{i}] (drawOpaque={canOpaqueOptimize})",
                evt: evt,
                lightMode: "StencilPrepass",
                layer: L.layer,
                enableKeywordDrawColor: canOpaqueOptimize, // true=写颜色；false=仅写模板
                writeStencil: true
            );
            p.SetupTargets(_baseCol, _baseDS);
            tempPasses.Add(p);
        }

        for (int j = 0; j < layers.Length; j++)
        {
            var config = layers[j];
            var fgColRT = config.blur ? _blurRT : _baseCol;
            var fgDepthRT = config.blur ? null : _baseDS;

            // 模糊层是否叠加
            if (config.blur)
            {
                var clear = new OneShot(CmdClearRT(_blurRT), evt);
                tempPasses.Add(clear);
                evt++;
            }

            // Draw layer
            if (config.isForeground)
            {
                tempPasses.Add(RenderForeground(fgColRT, fgDepthRT, j, layers, evt));
            }
            else
            {
                // 非模糊层 → 直接画到 baseRT（NotEqual 1）
                // 模糊层 → 直接画到 baseRT （Full）
                var p5 = new DrawDefaultPass($"B.NonBlur[{j}]→base(NotEqual1)", evt, config.layer,
                    useStencilNotEqual1: !config.blur);
                p5.SetupTargets(fgColRT, fgDepthRT);
                tempPasses.Add(p5);
            }
            evt++;

            bool useStencilNotEqual = UseStencilClip(j, layers); 
            int  stencilVal = 1;
            // 模糊算法
            if (config.blur)
            {
                switch (config.blurAlgorithm)
                {
                    case BlurAlgorithm.MipChain:
                    {
                        // 使用 MIP 链
                        var p = new UIEffects.Passes.MipChainBlurPass(
                            tag: $"B[{j}].MipChain",
                            injectEvent: evt,
                            srcRT: _blurRT,
                            baseCol: _baseCol,
                            baseDS: _baseDS,
                            compositeMat: new Material(settings.mipCompositeMat),
                            mipLevel: config.blurMip, 
                            useStencilNotEqual,
                            stencilVal,
                            true
                        );
                        tempPasses.Add(p);
                        evt++;
                        break;
                    }
                    case BlurAlgorithm.GaussianSeparable:
                    {
                        // 使用高斯分离（需要一个 ping-pong 中间RT）
                        EnsureGaussianPingPongRT(out var tmpRT, out var dstRT); // 你实现：尺寸同 blurRT

                        var p = new UIEffects.Passes.GaussianBlurPass(
                            tagPrefix: $"B[{j}].Gaussian",
                            injectEvent: evt ,
                            srcRT: _blurRT, // 采集放到 blurRT 复用
                            tmpRT: tmpRT,
                            dstRT: dstRT,
                            baseCol: _baseCol,
                            baseDS: _baseDS,
                            gaussianSeparableMat: new Material(settings.gaussianSeparableMat),
                            compositeCopyMat: new Material(settings.gaussianCompositeMat),
                            sigma: config.gaussianSigma, 
                            0f,
                            useStencilNotEqual,
                            stencilVal
                        );
                        p.Setup(config.iteration, config.gaussianSigma);
                        tempPasses.Add(p);
                        evt++;
                        break;
                    }
                }
            }
        }

        // ---------- Phase M：最终 baseRT 也要有 MIP ----------
        {
            var pm = new GenMipsPass("M.Mips(baseRT)", evt);
            pm.Setup(_baseCol, settings.generateMips);
            tempPasses.Add(pm);
        }

        // 入队执行
        foreach (var p in tempPasses) renderer.EnqueuePass(p);

        // 曝光全局
        Shader.SetGlobalTexture(_gid, _baseCol);
        Shader.SetGlobalVector(_uvScaleId, new Vector4((float)w / camDesc.width, (float)h / camDesc.height, 0, 0));
    }

    // ========== 小工具 ==========
    static CommandBuffer CmdClearRT(RTHandle rt)
    {
        var cmd = CommandBufferPool.Get("ClearRT");
        cmd.SetRenderTarget(rt);
        cmd.ClearRenderTarget(false, true, Color.clear);
        return cmd;
    }
    static CommandBuffer CmdBlitRT(RTHandle src, RTHandle dst)
    {
        var cmd = CommandBufferPool.Get("BlitRT");
        cmd.Blit(src,dst);
        return cmd;
    }

    static bool UseStencilClip(int layer, LayerConfig[] layerConfigs)
    {
        // 当前层的上层有前景时才做裁剪
        for (var i = layer + 1; i < layerConfigs.Length; i++)
        {
            if (layerConfigs[i].isForeground)
                return true;
        }
        return false;
    }
    static ScriptableRenderPass RenderForeground(RTHandle src, RTHandle depth, int layer, LayerConfig[] layers, RenderPassEvent evt)
    {
        var config = layers[layer];
        bool canOpaqueOptimize = ShouldDoOpaquePrepass(layer, layers);
        ScriptableRenderPass renderPass;
        // ---------- Phase F：前景层 ----------
        if (canOpaqueOptimize)
        {
            // 该前景层已在 Phase S 画过 Opaque，这里只补 AlphaOnly（不加 NotEqual）
            var pf = new DrawWithLightModePass(
                tag: $"F.FG_AlphaOnly[{layer}]",
                evt: evt,
                lightMode: "AlphaOnly",
                layer: config.layer,
                enableKeywordDrawColor: false,
                writeStencil: false
            );
            pf.SetupTargets(src, depth);
            renderPass = pf;
        }
        else
        {
            var useStencilClip = UseStencilClip(layer, layers);
            // 该前景层之前有模糊层 → 不能提前画 Opaque，这里做完整正常渲染（默认Tag，不加 NotEqual）
            var pf = new DrawDefaultPass($"F.FG_Full[{layer}]", evt, config.layer, useStencilNotEqual1:useStencilClip);
            pf.SetupTargets(src, depth);
            renderPass = pf;
        }

        return renderPass;
    }
    
    RTHandle _gaussTmp, _gaussDst;
    void EnsureGaussianPingPongRT(out RTHandle tmpRT, out RTHandle dstRT)
    {
        int w = _blurRT.rt.width;
        int h = _blurRT.rt.height;

        if (_gaussTmp == null || _gaussTmp.rt.width != w || _gaussTmp.rt.height != h)
        {
            _gaussTmp?.Release();
            _gaussDst?.Release();

            var desc = _blurRT.rt.descriptor;
            desc.useMipMap = false;
            desc.autoGenerateMips = false;

            _gaussTmp = RTHandles.Alloc(desc, FilterMode.Bilinear, name: "_UI_BG_GaussTmp", wrapMode: TextureWrapMode.Clamp);
            _gaussDst = RTHandles.Alloc(desc, FilterMode.Bilinear, name: "_UI_BG_GaussDst", wrapMode: TextureWrapMode.Clamp);
        }
        tmpRT = _gaussTmp;
        dstRT = _gaussDst;
    }

    class OneShot : ScriptableRenderPass
    {
        CommandBuffer _cmd;
        public OneShot(CommandBuffer cmd, RenderPassEvent evt) { _cmd = cmd; renderPassEvent = evt; }
        public override void Execute(ScriptableRenderContext ctx, ref RenderingData data)
        { ctx.ExecuteCommandBuffer(_cmd); CommandBufferPool.Release(_cmd); }
    }

    // 规则：该前景层 i 只有在“之前没有模糊层”时，才可做 Opaque+Stencil 预画优化
    static bool ShouldDoOpaquePrepass(int i, LayerConfig[] layers)
    {
        if (!layers[i].isForeground) return false;
        for (int j = 0; j < i; j++)
            if (layers[j].blur) return false;
        return true;
    }
}
