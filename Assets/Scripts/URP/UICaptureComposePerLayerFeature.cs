// Assets/Scripts/URP/UICaptureComposePerLayerFeature.cs
// URP 2022.3+
// 需要你已有的 ReplaceFeature + Hidden/UIBGCompositeStencilMip.shader（_SourceTex/_Mip）
// 需要前景 Shader 提供 LightMode = "StencilPrepass" 与 "AlphaOnly" 两个 Pass
// 在 StencilPrepass 内使用关键字 FG_PREPASS_DRAW_COLOR 控制是否写颜色（否则仅写模板）

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Experimental.Rendering;
using URP.Passes;

namespace URP
{
    // 模糊算法
    public enum BlurAlgorithm
    {
        MipChain,          // 使用 MIP 链
        GaussianSeparable  // 高斯分离（H+V）
    }
    
    [System.Flags]
    public enum GlobalBlurAlgorithm
    {
        None = 0,
        MipChain = 1 << 0,          // 使用 MIP 链
        GaussianSeparable = 1 << 1  // 高斯分离（H+V）
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

            [Header("多层模糊叠加")] public bool multiLayerMix;
            
            [Header("MIP 链合成材质（必须内置 Stencil NotEqual）")]
            public Material mipCompositeMat; // e.g. Hidden/UIBGCompositeStencilMip

            [Header("Gaussian 模糊材质（H/V 两个pass）")]
            public Material gaussianSeparableMat; // e.g. Hidden/UIBG_GaussianSeparable

            [Header("Gaussian 合成 Copy 材质（必须内置 Stencil NotEqual）")]
            public Material gaussianCompositeMat; // e.g. Hidden/UIBGCompositeCopyStencil

            [Header("按从远到近排序（config[0] 最底层）")]
            public LayerConfig[] layers;

            public GlobalBlurAlgorithm blurAlgorithm = GlobalBlurAlgorithm.MipChain;
            [Range(0, 8)] public float blurMip = 3;
            [Range(0f, 6f)] public float gaussianSigma = 2.0f;
            [Range(1, 5)] public int iteration = 1;

            [Header("注入时机")]
            public RenderPassEvent injectEvent = RenderPassEvent.BeforeRenderingTransparents;
        }

        // -------------------- Runtime Cache --------------------
        private bool _dirty = true;
        public void SetDirty(bool v) => _dirty = v;
        public bool IsDirty => _dirty;
        
        // ========== 通用小工具 Pass ==========
        // 通用 GenerateMips（可反复复用）
        class GenMipsPass : ScriptableRenderPass
        {
            RTHandle _target; bool _doGen; readonly string _tag;
            public GenMipsPass(string tag, RenderPassEvent evt){ this._tag=tag; renderPassEvent=evt; }
            public void Setup(RTHandle rt, bool gen){ _target=rt; _doGen=gen; }
            public override void Execute(ScriptableRenderContext ctx, ref RenderingData data)
            {
                if (!_doGen || _target==null) return;
                var cmd = CommandBufferPool.Get(_tag);
                using (new ProfilingScope(cmd, new ProfilingSampler(_tag))) cmd.GenerateMips(_target);
                ctx.ExecuteCommandBuffer(cmd); CommandBufferPool.Release(cmd);
            }
        }

        // ========== Feature 本体 ==========
        public Settings settings = new Settings();

        // RT
        RTHandle _baseCol, _baseDS, _blurRT;
        // Gaussian ping-pong
        RTHandle _gaussTmp, _gaussDst;

        // --- 材质缓存（长生命周期，禁止每帧 new） ---
        Material _matMip;    // Hidden/UIBGCompositeStencilMip
        Material _matGauss;  // Hidden/UIBG_GaussianSeparable
        Material _matCopy;   // Hidden/UIBGCompositeCopyStencil
        
        // --- Pass 池（长生命周期；按层索引复用） ---
        readonly List<DrawWithLightModePass> _poolDrawLM = new();
        readonly List<DrawWithLightModePass> _poolDrawAlpha = new();
        readonly List<DrawDefaultPass>       _poolDrawDef = new();
        readonly List<MipChainBlurPass>      _poolMipBlur = new();
        readonly List<GenMipsPass>           _poolGenMips = new();
        readonly List<GaussianWrapperPass>   _poolGaussian = new();
        readonly List<OneShot>               _poolOneShot = new();
        
        // 临时排队列表（仅存引用，不 new 实例）
        readonly List<ScriptableRenderPass> _tempPasses = new();

        // 全局名
        int _gid, _blurGid, _uvScaleId;
        
        public override void Create()
        {
            // —— 材质只在这里构建一次；优先使用 Inspector 赋值，否则回退到内置 Shader
            _matMip  = settings.mipCompositeMat ?? new Material(Shader.Find("Hidden/UIBGCompositeStencilMip"));
            _matGauss= settings.gaussianSeparableMat ?? new Material(Shader.Find("Hidden/UIBG_GaussianSeparable"));
            _matCopy = settings.gaussianCompositeMat ?? new Material(Shader.Find("Hidden/UIBGCompositeCopyStencil"));
            
            _gid = Shader.PropertyToID(settings.globalTextureName);
            _blurGid = Shader.PropertyToID(settings.globalTextureName+"_BLUR");
            _uvScaleId = Shader.PropertyToID("_UIBG_UVScale");
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
            
            _tempPasses.Clear();

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
                renderer.EnqueuePass(GetOrCreateOneShot(0, cmd, settings.injectEvent));
            }

            var layers = settings.layers;
            var evt = settings.injectEvent;

            
            // ---------- Phase S：前景模板预写（并按规则决定是否同时画Opaque） ----------
            for (int i = 0; i < layers.Length; i++)
            {
                var layerConfig = layers[i];
                if (!layerConfig.isForeground) continue;

                bool canOpaqueOptimize = ShouldDoOpaquePrepass(i, layers, settings.multiLayerMix); // 由“之前是否存在模糊层”决定
                var p = GetOrCreateDrawWithLightMode(i,
                    tag: $"S.FG_StencilPrepass[{i}] (drawOpaque={canOpaqueOptimize})",
                    evt: evt
                );
                p.Setup(lightMode: "StencilPrepass",
                    layer: layerConfig.layer,
                    enableKeywordDrawColor: canOpaqueOptimize); // true=写颜色；false=仅写模板
                p.SetupTargets(_baseCol, _baseDS);
                _tempPasses.Add(p);
            }

            for (int j = 0; j < layers.Length; j++)
            {
                var config = layers[j];
                var fgColRT = config.blur ? _blurRT : _baseCol;
                var fgDepthRT = config.blur ? null : _baseDS;

                // 模糊层是否叠加
                if (config.blur && !settings.multiLayerMix)
                {
                    var clear = GetOrCreateOneShot(1, CmdClearRT(_blurRT), evt);
                    _tempPasses.Add(clear);
                    evt++;
                }
                
                bool useStencilClip = UseStencilClip(j, layers, settings.multiLayerMix, false); 
                bool useStencilClipComposite = UseStencilClip(j, layers, settings.multiLayerMix, true); 
                // ======== Draw layer ==========
                // 在模糊层叠加渲染时需要先blit
                if (config.blur && settings.multiLayerMix)
                {
                    _tempPasses.Add(GetOrCreateOneShot(2, CmdBlitRT(_baseCol, _blurRT), evt));
                    evt++;
                }
                if (config.isForeground)
                {
                    _tempPasses.Add(RenderForeground(fgColRT, fgDepthRT, j, layers, evt, settings.multiLayerMix));
                }
                else
                {
                    // 非模糊层 → 直接画到 baseRT（NotEqual 1）
                    // 模糊层 → 直接画到 baseRT （Full）
                    var p5 = GetOrCreateDrawDefault(j,
                        $"B.NonBlur[{j}]→base(NotEqual1={useStencilClip})", evt);
                    p5.Setup(config.layer, useStencilClip);
                    p5.SetupTargets(fgColRT, fgDepthRT);
                    _tempPasses.Add(p5);
                }
                evt++;

                int  stencilVal = 1;
                // 模糊算法
                if (config.blur)
                {
                    switch (config.blurAlgorithm)
                    {
                        case BlurAlgorithm.MipChain:
                        {
                            // 使用 MIP 链
                            var p = GetOrCreateMipChainBlur(j, tag: $"B[{j}].MipChain",  evt);
                            p.SetSharedMaterial(_matMip);
                            p.Setup(srcRT: _blurRT,
                                baseCol: _baseCol,
                                baseDS: _baseDS);
                            p.SetParams(
                                mipLevel: config.blurMip, 
                                useStencilClip,
                                stencilVal,
                                true);
                            _tempPasses.Add(p);
                            evt++;
                            break;
                        }
                        case BlurAlgorithm.GaussianSeparable:
                        {
                            // 使用高斯分离（需要一个 ping-pong 中间RT）
                            EnsureGaussianPingPongRT(out var tmpRT, out var dstRT);
                            // —— 建议修改 UIEffects.Passes.GaussianBlurPass 为支持共享材质注入
                            var p = GetOrCreateGaussian(j, $"B[{j}].Gaussian", evt);
                            p.Setup(_blurRT, tmpRT, dstRT, _baseCol, _baseDS);
                            p.SetSharedMaterials(_matGauss, _matCopy);
                            p.SetParams(config.iteration, config.gaussianSigma, 
                                Mathf.Min(config.gaussianSigma, 4f), 
                                useStencilClip, useStencilClipComposite, stencilVal);
                            _tempPasses.Add(p);
                            evt++;
                            break;
                        }
                    }
                }
            }

            var generateMips = (settings.blurAlgorithm & GlobalBlurAlgorithm.MipChain) > 0;
            var generateGaussian = (settings.blurAlgorithm & GlobalBlurAlgorithm.GaussianSeparable) > 0;
            // ---------- Phase M：最终 baseRT 也要有 MIP ----------
            if (generateMips)
            {
                var pm = GetOrCreateGenMip(0,"M.Mips(baseRT)", evt);
                pm.Setup(_baseCol, generateMips);
                _tempPasses.Add(pm);
                evt++;
            }
            // ---------- Phase P：最终 baseRT 做模糊到 blurRT ----------
            if (generateGaussian)
            {
                EnsureGaussianPingPongRT(out var tmpRT, out var dstRT);
                var globalBlur = GetOrCreateGaussian(32, "P.GlobalBlur(blurRT)", evt);
                globalBlur.Setup(_baseCol, tmpRT, dstRT, _blurRT, null);
                globalBlur.SetSharedMaterials(_matGauss, _matCopy);
                globalBlur.SetParams(settings.iteration, settings.gaussianSigma, 
                    Mathf.Min(settings.gaussianSigma, 4f), false, false, 0);
                _tempPasses.Add(globalBlur);
                evt++;
                
                var pm = GetOrCreateGenMip(1,"M.Mips(BlurRT)", evt);
                pm.Setup(_blurRT, generateMips);
                _tempPasses.Add(pm);
                
                Shader.SetGlobalTexture(_blurGid, _blurRT);
            }

            // 入队执行
            foreach (var p in _tempPasses) renderer.EnqueuePass(p);

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

        static bool UseStencilClip(int layer, LayerConfig[] layerConfigs, bool multilayerMix, bool composite)
        {
            var config = layerConfigs[layer];
            var isForeground = config.isForeground;
            var fgLayer = 0;
            for (var j = 0; j < layerConfigs.Length; j++)
            {
                if (layerConfigs[j].isForeground)
                {
                    fgLayer = j;
                    break;
                }
            }
            
            // 合成阶段
            if (composite)
            {
                // 上层有前景且叠加时
                return fgLayer > layer && multilayerMix;
            }

            // 不叠加时，只要有模糊层就不能做Stencil优化
            if (!multilayerMix)
            {
                for (var j = 0; j < layerConfigs.Length; j++){
                    if (layerConfigs[j].blur)
                        return false;
                }
            }
            // 叠加时，模糊层只能在前景之后
            else
            {
                for (var j = 0; j < fgLayer; j++){
                    if (layerConfigs[j].blur)
                        return false;
                }
            }

            // 当前层的上层有前景且自己不模糊时才做裁剪
            for (var i = layer + 1; i < layerConfigs.Length; i++)
            {
                if (layerConfigs[i].isForeground && !isForeground && !config.blur)
                    return true;
            }

            return false;
        }
        
        static bool ShouldDoOpaquePrepass(int i, LayerConfig[] layers, bool multilayerMix)
        {
            // 不叠加时，只要有模糊层就不能做Opaque优化
            if (!multilayerMix)
            {
                for (var j = 0; j < layers.Length; j++){
                    if (layers[j].blur)
                        return false;
                }
            }
            // 叠加时，前面有模糊层就不做Opaque优化
            else
            {
                for (var j = 0; j < i; j++){
                    if (layers[j].blur)
                        return false;
                }
            }
            // 前景才需要 Opaque+Stencil
            if (!layers[i].isForeground) return false;
            // 规则：该前景层 i 只有在“之前没有模糊层”时，才可做 Opaque+Stencil 预画优化
            for (int j = 0; j < i; j++)
                if (layers[j].blur) return false;
            return true;
        }

        public bool IsUseBgGaussianBlur
        {
            get
            {
                return (settings.blurAlgorithm & GlobalBlurAlgorithm.GaussianSeparable) > 0;
            }
        }
        ScriptableRenderPass RenderForeground(RTHandle src, RTHandle depth, int layer, LayerConfig[] layers, RenderPassEvent evt, bool multilayerMix)
        {
            var config = layers[layer];
            bool canOpaqueOptimize = ShouldDoOpaquePrepass(layer, layers, settings.multiLayerMix);
            ScriptableRenderPass renderPass;
            // ---------- Phase F：前景层 ----------
            if (canOpaqueOptimize)
            {
                // 该前景层已在 Phase S 画过 Opaque，这里只补 AlphaOnly（不加 NotEqual）
                var pf = GetOrCreateDrawAlphaOnly(layer,
                    tag: $"F.FG_AlphaOnly[{layer}]",
                    evt: evt
                );
                pf.Setup(lightMode: "AlphaOnly",
                    layer: config.layer,
                    enableKeywordDrawColor: false);
                pf.SetupTargets(src, depth);
                renderPass = pf;
            }
            else
            {
                var useStencilClip = UseStencilClip(layer, layers,
                    multilayerMix, false);
                // 该前景层之前有模糊层 → 不能提前画 Opaque，这里做完整正常渲染（默认Tag，不加 NotEqual）
                var pf = GetOrCreateDrawDefault(layer,
                    $"F.FG_Full[{layer}]", evt);
                pf.Setup(config.layer, useStencilClip);
                pf.SetupTargets(src, depth);
                renderPass = pf;
            }

            return renderPass;
        }
        
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
       
        // --------- Pass 池：获取或创建 ---------
        DrawWithLightModePass GetOrCreateDrawWithLightMode(int i, string tag, RenderPassEvent evt)
        {
            while (_poolDrawLM.Count <= i) _poolDrawLM.Add(null);
            var p = _poolDrawLM[i];
            if (p == null) { p = new DrawWithLightModePass(tag, evt); _poolDrawLM[i] = p; }
            else { p.renderPassEvent = evt; }
            return p;
        }
        DrawWithLightModePass GetOrCreateDrawAlphaOnly(int i, string tag, RenderPassEvent evt)
        {
            while (_poolDrawAlpha.Count <= i) _poolDrawAlpha.Add(null);
            var p = _poolDrawAlpha[i];
            if (p == null) { p = new DrawWithLightModePass(tag, evt); _poolDrawAlpha[i] = p; }
            else { p.renderPassEvent = evt; }
            return p;
        }
        DrawDefaultPass GetOrCreateDrawDefault(int i, string tag, RenderPassEvent evt)
        {
            while (_poolDrawDef.Count <= i) _poolDrawDef.Add(null);
            var p = _poolDrawDef[i];
            if (p == null) { p = new DrawDefaultPass(tag, evt); _poolDrawDef[i] = p; }
            else { p.renderPassEvent = evt; }
            return p;
        }

        MipChainBlurPass GetOrCreateMipChainBlur(int i, string tag, RenderPassEvent evt)
        {
            while (_poolMipBlur.Count <= i) _poolMipBlur.Add(null);
            var p = _poolMipBlur[i];
            if (p == null) { p = new MipChainBlurPass(tag, evt); _poolMipBlur[i] = p; }
            else { p.renderPassEvent = evt; }
            return p;
        }

        GenMipsPass GetOrCreateGenMip(int i, string tag, RenderPassEvent evt)
        {
            while (_poolGenMips.Count <= i) _poolGenMips.Add(null);
            var p = _poolGenMips[i];
            if (p == null) { p = new GenMipsPass(tag, evt); _poolGenMips[i] = p; }
            else { p.renderPassEvent = evt; }
            return p;
        }

        // 如果你能修改 GaussianBlurPass：建议提供如下包装接口
        class GaussianWrapperPass : ScriptableRenderPass
        {
            GaussianBlurPass _inner;
            public GaussianWrapperPass(GaussianBlurPass inner){ _inner = inner; }
            public void Setup(RTHandle src, RTHandle tmp, RTHandle dst, RTHandle baseCol, RTHandle baseDS)
                => _inner.Setup(src,tmp,dst,baseCol,baseDS);
            public void SetSharedMaterials(Material gauss, Material copy)
                => _inner.SetSharedMaterials(gauss, copy); // 需要在你的类里添加
            public void SetParams(int iteration, float sigma, float mipMap , bool useStencilNotEqual,bool useStencilNotEqualComposite, int stencilVal)
                => _inner.SetParams(iteration, sigma, mipMap, useStencilNotEqual, useStencilNotEqualComposite, stencilVal);
            public override void Execute(ScriptableRenderContext ctx, ref RenderingData data) => _inner.Execute(ctx, ref data);
        }
        GaussianWrapperPass GetOrCreateGaussian(int i, string tag, RenderPassEvent evt)
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

        OneShot GetOrCreateOneShot(int i, CommandBuffer cmd, RenderPassEvent evt)
        {
            return new OneShot(cmd, evt);
            while (_poolOneShot.Count <= i) _poolOneShot.Add(null);
            var p = _poolOneShot[i];
            if (p == null) { p = new OneShot(cmd, evt); _poolOneShot[i] = p; }
            else { p.renderPassEvent = evt; p.Setup(cmd); }
            return p;
        }
        
        class OneShot : ScriptableRenderPass
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
    }
}