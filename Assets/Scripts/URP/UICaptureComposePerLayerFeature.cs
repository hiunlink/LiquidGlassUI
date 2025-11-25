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
using TagUtils = URP.UICaptureComposePerLayerTagUtils;
using PassPool = URP.UICaptureComposePerLayerPassPool;

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
        // ========== 静态tags ==========
        private const string ClearRtTag = "ClearRT";
        private const string BlitRtTag = "BlitRT";
        private const string PGlobalmipsBasertTag = "P.GlobalMips(baseRT)";
        private const string PGlobalblurBlurrtTag = "P.GlobalBlur(blurRT)";
        private const string MGlobalmipsBlurrtTag = "M.GlobalMips(blurRT)";
        private const string LightModeTag = "StencilPrepass";
        private const string AlphaOnlyTag = "AlphaOnly";

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
            
            // 本层模糊
            [ShowIf("blur")]
            public BlurAlgorithm blurAlgorithm = BlurAlgorithm.MipChain;
            [ShowIf("blur","blurAlgorithm", (int)BlurAlgorithm.GaussianSeparable)]
            public Color alphaBlendColor = Color.white;
            [ShowIf("blur","blurAlgorithm", (int)BlurAlgorithm.MipChain)]
            [Range(0, 8)] public float blurMip = 3;
            [ShowIf("blur","blurAlgorithm", (int)BlurAlgorithm.GaussianSeparable)]
            [Range(0f, 6f)] public float gaussianSigma = 2.0f;
            [ShowIf("blur","blurAlgorithm", (int)BlurAlgorithm.GaussianSeparable)]
            [Range(1, 5)] public int iteration = 1;
            
            // 输出全局贴图，填空不输出
            [Header("输出全局贴图名（供 UI 玻璃采样 / Replace 使用）")]
            public string globalTextureName = "_UI_BG";
            [Header("输出分辨率缩放")]
            [Range(0.25f, 1f)] public float resolutionScale = 1f;
            public GlobalBlurAlgorithm globalBlurAlgorithm = GlobalBlurAlgorithm.MipChain;
            [Range(0f, 6f)] public float globalGaussianSigma = 2.0f;
            [Range(1, 5)] public int globalIteration = 1;
            // 是否需要重新绘制
            private bool _dirty = true;
            public void SetDirty(bool v) => _dirty = v;
            public bool IsDirty => _dirty || string.IsNullOrEmpty(globalTextureName); // 没有输出贴图就没办法缓存，需要重新绘制
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

            [Header("注入时机")]
            public RenderPassEvent injectEvent = RenderPassEvent.BeforeRenderingTransparents;
        }

        private readonly Dictionary<string, GlobalTextureInfo> _globalTextureInfos = new();
        private class GlobalTextureInfo
        {
            public string GlobalTextureName = "_UI_BG";
            public float ResolutionScale = 1f;
        }

        private GlobalTextureInfo GetOrCreateGlobalTextureInfo(string globalTextureName, float resolutionScale)
        {
            if (!_globalTextureInfos.ContainsKey(globalTextureName))
            {
                _globalTextureInfos[globalTextureName] = new GlobalTextureInfo
                {
                    GlobalTextureName = globalTextureName
                };
            }

            var result = _globalTextureInfos[globalTextureName];
            result.ResolutionScale = resolutionScale;
            return result;
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
            var layers = settings.layers;
            var evt = settings.injectEvent;
            
            // =========== 缓存系统 ============
            // === 在编辑器但未运行时强制每帧重绘 ==
            if (!Application.isPlaying)
            {
                for (var i = 0; i < layers.Length; i++)
                {
                    var config = layers[i];
                    config.SetDirty(true);
                }
            }
            var firstLayerToRender = layers.Length;
            var firstLayerRenderTextureName = string.Empty;
            // - 找到第一个有变化的层
            for (var i = 0; i < layers.Length; i++)
            {
                var config = layers[i];
                if (config.IsDirty)
                {
                    firstLayerToRender = i;
                    firstLayerRenderTextureName = config.globalTextureName;
                    break;
                }
            }
            // 重绘层最下层
            for (var i = firstLayerToRender-1; i >= 0; i--)
            {
                var config = layers[i];
                if (firstLayerRenderTextureName == config.globalTextureName)
                    firstLayerToRender = i;
            }

            // - 之后的层都需要标记重新渲染
            for (var i = firstLayerToRender; i < layers.Length; i++)
            {
                var config = layers[i];
                config.SetDirty(true);
            }
            
            _tempPasses.Clear();
            var prevLayerToRender = Mathf.Max(firstLayerToRender - 1, 0);

            GlobalTextureInfo firstGlobalTextureInfo;
            if (settings.layers.Length > prevLayerToRender)
            {
                var firstLayer = settings.layers[prevLayerToRender];
                firstGlobalTextureInfo = GetOrCreateGlobalTextureInfo(firstLayer.globalTextureName,
                    firstLayer.resolutionScale);
            }
            else
            {
                firstGlobalTextureInfo = GetOrCreateGlobalTextureInfo(settings.globalTextureName,
                    settings.resolutionScale);
            }

            var useHDR = settings.useHDR;
            var camDesc = data.cameraData.cameraTargetDescriptor;
            EnsureGlobalTextures(camDesc, firstGlobalTextureInfo, useHDR, out var w, out var h);

            int curRtFrom = 0, curRtTo = 0, fgLayer = 0;
            for (var layerIndex = firstLayerToRender; layerIndex < layers.Length; layerIndex++)
            {
                var config = layers[layerIndex];
                var prevLayer = layerIndex - 1;
                var hasPrevLayer = prevLayer >= 0;
                var prevLayerConfig = hasPrevLayer? layers[prevLayer]: null;
                var rtSwitch = prevLayerConfig!=null && config.globalTextureName != prevLayerConfig.globalTextureName;
                // 做 StencilPrepass
                if (rtSwitch || layerIndex == 0)
                {
                    curRtFrom = layerIndex;
                    curRtTo = layerIndex;
                    EnsureGlobalTextures(camDesc, GetOrCreateGlobalTextureInfo(
                        config.globalTextureName,
                        config.resolutionScale),
                        useHDR,out var w2, out var h2);
                    for (var i = layerIndex + 1; i < layers.Length; i++)
                    {
                        var nextConfig = layers[i];
                        if (nextConfig.globalTextureName == config.globalTextureName)
                            curRtTo = i;
                    }
                    _tempPasses.Add(PassPool.GetOrCreateOneShot(layerIndex, CmdClearRT(_baseCol,_baseDS), evt));
                    evt++;
                    // ---------- Phase S：前景模板预写（并按规则决定是否同时画Opaque） ----------
                    for (var i = curRtFrom; i <= curRtTo; i++)
                    {
                        var layerConfig = layers[i];
                        if (!layerConfig.isForeground) continue;

                        fgLayer = i;
                        var canOpaqueOptimize = ShouldDoOpaquePrepass(i, layers, settings.multiLayerMix, curRtFrom, curRtTo); // 由“之前是否存在模糊层”决定
                        var p = PassPool.GetOrCreateDrawWithLightMode(i,
                            tag: TagUtils.GetStencilPrepassTag(i, canOpaqueOptimize),
                            evt: evt
                        );
                        p.Setup(lightMode: LightModeTag,
                            layer: layerConfig.layer,
                            enableKeywordDrawColor: canOpaqueOptimize); // true=写颜色；false=仅写模板
                        p.SetupTargets(_baseCol, _baseDS);
                        _tempPasses.Add(p);
                    }
                }
                // 下层合成到本层 or 没变化的层选最上层的合成到本层
                if (hasPrevLayer && rtSwitch)
                {
                    EnsureGlobalTextures(camDesc, GetOrCreateGlobalTextureInfo(
                        prevLayerConfig.globalTextureName,
                        prevLayerConfig.resolutionScale),
                        useHDR,out var w0, out var h0);
                    var fromCol = _baseCol;
                    EnsureGlobalTextures(camDesc, GetOrCreateGlobalTextureInfo(
                            config.globalTextureName,
                            config.resolutionScale),
                        useHDR,out var w2, out var h2);
                    
                    var compositePass = PassPool.GetOrCreateComposite(layerIndex,
                        TagUtils.GetCompositeTag(prevLayerConfig.globalTextureName, config.globalTextureName),evt);
                    var compositeStencilOpt = fgLayer >= layerIndex && settings.multiLayerMix;
                    compositePass.Setup(fromCol, _baseCol, _baseDS, compositeStencilOpt, 1);
                    compositePass.SetMaterial(_matCopy);
                    _tempPasses.Add(compositePass);
                    evt++;
                }
                else
                {
                    var layerGlobalTextureInfo =
                        GetOrCreateGlobalTextureInfo(config.globalTextureName, config.resolutionScale);
                    EnsureGlobalTextures(camDesc, layerGlobalTextureInfo, useHDR, out var w1, out var h1);
                }
                
                var fgColRT = config.blur ? _blurRT : _baseCol;
                var fgDepthRT = config.blur ? null : _baseDS;

                // 模糊层是否叠加
                if (config.blur && !settings.multiLayerMix)
                {
                    var clear = PassPool.GetOrCreateOneShot2(layerIndex, CmdClearRT(_blurRT), evt);
                    _tempPasses.Add(clear);
                    evt++;
                }
                
                var useStencilClip = UseStencilClip(layerIndex, layers, settings.multiLayerMix, false,curRtFrom,curRtTo); 
                var useStencilClipComposite = UseStencilClip(layerIndex, layers, settings.multiLayerMix, true,curRtFrom,curRtTo); 
                // ======== Draw layer ==========
                // 在模糊层叠加渲染时需要先blit
                if (config.blur && settings.multiLayerMix)
                {
                    _tempPasses.Add(PassPool.GetOrCreateOneShot3(layerIndex, CmdBlitRT(_baseCol, _blurRT), evt));
                    evt++;
                }
                if (config.isForeground)
                {
                    _tempPasses.Add(RenderForeground(fgColRT, fgDepthRT, layerIndex, layers, evt, settings.multiLayerMix,
                        curRtFrom,curRtTo));
                }
                else
                {
                    // 非模糊层 → 直接画到 baseRT（NotEqual 1）
                    // 模糊层 → 直接画到 baseRT （Full）
                    var p5 = PassPool.GetOrCreateDrawDefault(layerIndex,
                        TagUtils.GetDrawDefaultTag(layerIndex,useStencilClip), evt);
                    p5.Setup(config.layer, useStencilClip);
                    p5.SetupTargets(fgColRT, fgDepthRT);
                    _tempPasses.Add(p5);
                }
                evt++;

                var stencilVal = 1;
                // 模糊算法
                if (config.blur)
                {
                    switch (config.blurAlgorithm)
                    {
                        case BlurAlgorithm.MipChain:
                        {
                            // 使用 MIP 链
                            var p = PassPool.GetOrCreateMipChainBlur(layerIndex, tag: TagUtils.GetMipsTag(layerIndex),  evt);
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
                            var p = PassPool.GetOrCreateGaussian(layerIndex, TagUtils.GetGaussianTag(layerIndex), evt);
                            p.Setup(_blurRT, tmpRT, dstRT, _baseCol, _baseDS);
                            p.SetSharedMaterials(_matGauss, _matCopy);
                            p.SetParams(config.iteration, config.gaussianSigma, 
                                Mathf.Min(config.gaussianSigma, 4f), config.alphaBlendColor,
                                useStencilClip, useStencilClipComposite, stencilVal);
                            _tempPasses.Add(p);
                            evt++;
                            break;
                        }
                    }
                }
                
                // 需要输出模糊贴图
                var generateMips = (config.globalBlurAlgorithm & GlobalBlurAlgorithm.MipChain) > 0;
                var generateGaussian = (config.globalBlurAlgorithm & GlobalBlurAlgorithm.GaussianSeparable) > 0;
                // ---------- Phase M：最终 baseRT 也要有 MIP ----------
                if (generateMips)
                {
                    var pm = PassPool.GetOrCreateGenMip(layerIndex,PGlobalmipsBasertTag, evt);
                    pm.Setup(_baseCol, generateMips);
                    _tempPasses.Add(pm);
                    evt++;
                }
                // ---------- Phase P：最终 baseRT 做模糊到 blurRT ----------
                if (generateGaussian)
                {
                    EnsureGaussianPingPongRT(out var tmpRT, out var dstRT);
                    var globalBlur = PassPool.GetOrCreateGaussian2(layerIndex, PGlobalblurBlurrtTag, evt);
                    globalBlur.Setup(_baseCol, tmpRT, dstRT, _blurRT, null);
                    globalBlur.SetSharedMaterials(_matGauss, _matCopy);
                    globalBlur.SetParams(config.globalIteration, config.globalGaussianSigma, 
                        Mathf.Min(config.globalGaussianSigma, 4f), Color.clear,  
                        false, false, 0);
                    _tempPasses.Add(globalBlur);
                    evt++;
                
                    var pm = PassPool.GetOrCreateGenMip2(layerIndex,MGlobalmipsBlurrtTag, evt);
                    pm.Setup(_blurRT, generateMips);
                    _tempPasses.Add(pm);
                    evt++;
                    Shader.SetGlobalTexture(_blurGid, _blurRT);
                }
                
                // 曝光全局
                Shader.SetGlobalTexture(_gid, _baseCol);
                Shader.SetGlobalVector(_uvScaleId, new Vector4((float)w / camDesc.width, (float)h / camDesc.height, 0, 0));
                
                // --- 继续执行完整渲染 ---
                config.SetDirty(false);
            }

            // 入队执行
            foreach (var p in _tempPasses) renderer.EnqueuePass(p);
        }

        #region Public

        public void SetDirty(LayerMask layer, bool dirty)
        {
            foreach (var layerConfig in settings.layers)
            {
                if (layerConfig.layer == layer)
                {
                    layerConfig.SetDirty(dirty);
                    break;
                }
            }
        }
        public RenderTexture GetRenderTexture(string textureName)
        {
            if (textureName == null || !_tempRTMap.TryGetValue(textureName, out var textures))
                return null;
            return textures.BaseCol.rt;
        }
        public RenderTexture GetBlurRenderTexture(string textureName)
        {
            if (!_tempRTMap.TryGetValue(textureName, out var textures))
                return null;
            return textures.BlurRT.rt;
        }

        #endregion

        private class GlobalTextures
        {
            public RTHandle BaseCol;
            public RTHandle BaseDS;
            public RTHandle BlurRT;
        }

        private readonly Dictionary<string, GlobalTextures> _tempRTMap = new ();
        private void EnsureGlobalTextures(RenderTextureDescriptor camDesc, GlobalTextureInfo layerGlobalTextureInfo,
            bool useHDR, out int w, out int h)
        {
            w = Mathf.Max(1, Mathf.RoundToInt(camDesc.width  * layerGlobalTextureInfo.ResolutionScale));
            h = Mathf.Max(1, Mathf.RoundToInt(camDesc.height * layerGlobalTextureInfo.ResolutionScale));

            var tempRT = _tempRTMap.GetValueOrDefault(layerGlobalTextureInfo.GlobalTextureName);
            
            // (重)建 RT
            if (tempRT==null || tempRT.BaseCol.rt.width!=w || tempRT.BaseCol.rt.height!=h )
            {
                //判断RT释放
                if (tempRT != null)
                {
                    tempRT.BaseCol?.Release();
                    tempRT.BaseDS?.Release();
                    tempRT.BlurRT?.Release();
                }

                var col = new RenderTextureDescriptor(w,h){
                    graphicsFormat = useHDR ? GraphicsFormat.B10G11R11_UFloatPack32 : GraphicsFormat.R8G8B8A8_UNorm,
                    depthStencilFormat = GraphicsFormat.None,
                    msaaSamples=1, useMipMap=true, autoGenerateMips=false,
                    sRGB = (QualitySettings.activeColorSpace==ColorSpace.Linear)
                };
                var ds = new RenderTextureDescriptor(w,h){
                    graphicsFormat=GraphicsFormat.None, depthStencilFormat=GraphicsFormat.D24_UNorm_S8_UInt,
                    msaaSamples=1, useMipMap=false, autoGenerateMips=false, sRGB=false
                };
                var blur = new RenderTextureDescriptor(w,h){
                    graphicsFormat = useHDR ? GraphicsFormat.B10G11R11_UFloatPack32 : GraphicsFormat.R8G8B8A8_UNorm,
                    depthStencilFormat = GraphicsFormat.None,
                    msaaSamples=1, useMipMap=true, autoGenerateMips=false,
                    sRGB = (QualitySettings.activeColorSpace==ColorSpace.Linear)
                };

                
                _baseCol = RTHandles.Alloc(col, FilterMode.Bilinear, name: layerGlobalTextureInfo.GlobalTextureName, wrapMode: TextureWrapMode.Clamp);
                _baseDS  = RTHandles.Alloc(ds,  name: TagUtils.GetDsString(layerGlobalTextureInfo.GlobalTextureName), wrapMode: TextureWrapMode.Clamp);
                _blurRT  = RTHandles.Alloc(blur,FilterMode.Bilinear, name: TagUtils.GetBlurString(layerGlobalTextureInfo.GlobalTextureName), wrapMode: TextureWrapMode.Clamp);
                _tempRTMap[layerGlobalTextureInfo.GlobalTextureName] = new GlobalTextures()
                {
                    BaseCol = _baseCol,
                    BaseDS = _baseDS,
                    BlurRT = _blurRT
                };
            }

            var rtInfo = _tempRTMap[layerGlobalTextureInfo.GlobalTextureName];
            _baseCol = rtInfo.BaseCol;
            _baseDS = rtInfo.BaseDS;
            _blurRT = rtInfo.BlurRT;
            
            _gid = Shader.PropertyToID(layerGlobalTextureInfo.GlobalTextureName);
            _blurGid = Shader.PropertyToID(TagUtils.GetBlurString(layerGlobalTextureInfo.GlobalTextureName));
        }
        
        // ========== 小工具 ==========
        private static CommandBuffer CmdClearRT(RTHandle rt, RTHandle ds = null)
        {
            var cmd = CommandBufferPool.Get(ClearRtTag);
            if (ds == null)
                cmd.SetRenderTarget(rt);
            else
            {
                cmd.SetRenderTarget(rt,ds);
            }
            cmd.ClearRenderTarget(true, true, Color.clear);
            return cmd;
        }

        private static CommandBuffer CmdBlitRT(RTHandle src, RTHandle dst)
        {
            var cmd = CommandBufferPool.Get(BlitRtTag);
            cmd.Blit(src,dst);
            return cmd;
        }

        private static bool UseStencilClip(int layer, LayerConfig[] layerConfigs, bool multilayerMix, bool composite,
            int from, int to)
        {
            var config = layerConfigs[layer];
            var isForeground = config.isForeground;
            var fgLayer = 0;
            for (var j = from; j <= to; j++)
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
                for (var j = from; j <= to; j++){
                    if (layerConfigs[j].blur)
                        return false;
                }
            }
            // 叠加时，模糊层只能在前景之后
            else
            {
                for (var j = from; j < fgLayer; j++){
                    if (layerConfigs[j].blur)
                        return false;
                }
            }

            // 当前层的上层有前景且自己不模糊时才做裁剪
            for (var i = layer + 1; i <= to; i++)
            {
                if (layerConfigs[i].isForeground && !isForeground && !config.blur)
                    return true;
            }

            return false;
        }

        private static bool ShouldDoOpaquePrepass(int i, LayerConfig[] layers, bool multilayerMix, int from, int to)
        {
            // 不叠加时，只要有模糊层就不能做Opaque优化
            if (!multilayerMix)
            {
                for (var j = from; j <= to; j++){
                    if (layers[j].blur)
                        return false;
                }
            }
            // 叠加时，前面有模糊层就不做Opaque优化
            else
            {
                for (var j = from; j < i; j++){
                    if (layers[j].blur)
                        return false;
                }
            }
            // 第一层的前景不做opaque优化
            if (i == from) return false;
            // 前景才需要 Opaque+Stencil
            if (!layers[i].isForeground) return false;
            // 规则：该前景层 i 只有在“之前没有模糊层”时，才可做 Opaque+Stencil 预画优化
            for (var j = from; j < i; j++)
                if (layers[j].blur) return false;
            return true;
        }

        private ScriptableRenderPass RenderForeground(RTHandle src, RTHandle depth, int layer, LayerConfig[] layers, 
            RenderPassEvent evt, bool multilayerMix, int from, int to)
        {
            var config = layers[layer];
            bool canOpaqueOptimize = ShouldDoOpaquePrepass(layer, layers, settings.multiLayerMix, from, to);
            ScriptableRenderPass renderPass;
            // ---------- Phase F：前景层 ----------
            if (canOpaqueOptimize)
            {
                // 该前景层已在 Phase S 画过 Opaque，这里只补 AlphaOnly（不加 NotEqual）
                var pf = PassPool.GetOrCreateDrawAlphaOnly(layer,
                    tag: TagUtils.GetAlphaOnlyTag(layer),
                    evt: evt
                );
                pf.Setup(lightMode: AlphaOnlyTag,
                    layer: config.layer,
                    enableKeywordDrawColor: false);
                pf.SetupTargets(src, depth);
                renderPass = pf;
            }
            else
            {
                var useStencilClip = UseStencilClip(layer, layers,
                    multilayerMix, false, from, to);
                // 该前景层之前有模糊层 → 不能提前画 Opaque，这里做完整正常渲染（默认Tag，不加 NotEqual）
                var pf = PassPool.GetOrCreateDrawDefault(layer,
                    TagUtils.GetFullTag(layer), evt);
                pf.Setup(config.layer, useStencilClip);
                pf.SetupTargets(src, depth);
                renderPass = pf;
            }

            return renderPass;
        }

        private void EnsureGaussianPingPongRT(out RTHandle tmpRT, out RTHandle dstRT)
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
    }
}