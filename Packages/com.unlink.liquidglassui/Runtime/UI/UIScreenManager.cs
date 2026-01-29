using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Unlink.LiquidGlassUI
{
    public class UIScreenManager
    {
        private class WrapCanvasConfig
        {
            public float lowerBlurStrength = 1;
            public CanvasBlurConfig lowerCanvasBlurConfig = null;
            public UIScreen.CanvasConfig canvasConfig;

            public void Reset()
            {
                lowerBlurStrength = 1;
                lowerCanvasBlurConfig = null;
                canvasConfig = null;
            }
        }
        
        private static UIScreenManager _instance;
        public static UIScreenManager Instance
        {
            get
            {
                if (_instance != null) return _instance;
                _instance = new UIScreenManager();
                return _instance;
            }
        }
        
        private int _rtIndex = 0;
        private int _startLayer = 6;
        private int _endLayer = 31;
        private int _hiddenLayer = 3;
        private readonly List<UIScreen> _screens = new();
        
        private readonly List<UICaptureComposePerLayerFeature.LayerConfig> _featureLayerConfigPool = new();
        private readonly List<WrapCanvasConfig> _wrapCanvasConfigPool = new();
        private readonly List<CanvasConfigWrapLayer> _wrapCanvasConfigLayerPool = new();
        private readonly List<UICaptureComposePerLayerFeature.LayerConfig> _featureLayerConfigs = new();
        private readonly List<WrapCanvasConfig> _wrapCanvasConfigs = new();

        private UIScreenManager()
        {
            var config = UICaptureEffectManager.Instance?.GetLiquidGlassSettings();
            if (config == null)
                return;
            _startLayer = config.layerStart;
            _endLayer = config.layerEnd;
            _hiddenLayer = config.hiddenLayer;
            Graphic.OnRebuild += OnElementRebuild;
        }

        private void OnElementRebuild(Graphic graphic, CanvasUpdate update)
        {
           if (!graphic) return;
           var layer = graphic.gameObject.layer;
           UICaptureEffectManager.Instance?.SetDirty(1 << layer, true);
        }
        #region Public

        public void AddUIScreenAndUpdateRendererFeature(UIScreen screenConfig)
        {
            AddUIScreen(screenConfig);
            UpdateRendererFeature();
        }

        public void UpdateUIScreens()
        {
            _screens.Clear();
            var screens = GameObject.FindObjectsOfType<UIScreen>();
            foreach (var screen in screens)
            {
                _screens.Add(screen);
            }
        }
                
        public void UpdateRendererFeature(bool sortScreen = false)
        {
            _rtIndex = 0;
            var curLayer = _startLayer;
            ClearFeatureLayerConfigs(_featureLayerConfigs);
            ClearWrapCanvasConfigs(_wrapCanvasConfigs);
            // rearrange _screens;
            if (sortScreen)
                _screens.Sort(CompareHierarchy);
            // setup renderer feature layer configs
            for (var uiLayer = 0; uiLayer < _screens.Count; uiLayer++)
            {
                var uiScreen = _screens[uiLayer];
                InitLayerConfigs(uiScreen, _featureLayerConfigs, ref curLayer);
                
                for (var i = 0; i < uiScreen.canvasConfigs.Count; i++)
                {
                    var canvasConfig = uiScreen.canvasConfigs[i];
                    if (!CheckLayerIsValid(canvasConfig.canvas.gameObject.layer))
                        continue;
                    var wrapConfig = GetOrCreateWrapCanvasConfig();
                    wrapConfig.canvasConfig = canvasConfig;
                    _wrapCanvasConfigs.Add(wrapConfig);
                    // Check next uiScreen lower canvas blur config
                    if (i == uiScreen.canvasConfigs.Count - 1)
                    {
                        var nextUiLayer = uiLayer + 1;
                        var nextUiScreen = _screens.Count > nextUiLayer ? _screens[nextUiLayer] : null;
                        // first canvas is available
                        if (!nextUiScreen || !CheckLayerIsValid(nextUiScreen.canvasConfigs[0].canvas.gameObject.layer))
                            continue;
                        if (nextUiScreen is { lowerBlur: true })
                        {
                            wrapConfig.lowerBlurStrength = nextUiScreen.lowerBlurStrength;
                            wrapConfig.lowerCanvasBlurConfig = nextUiScreen.lowerCanvasBlurConfig;
                        }
                        else
                        {
                            wrapConfig.lowerBlurStrength = 1;
                            wrapConfig.lowerCanvasBlurConfig = null;
                        }
                    }
                }
            }
            // setup others
            for (var i = 0; i < _wrapCanvasConfigs.Count; i++)
            {
                var featureLayerConfig = _featureLayerConfigs[i];
                var nextFeatureLayerConfig = _featureLayerConfigs[Mathf.Min(i + 1, _featureLayerConfigs.Count - 1)];
                var wrapConfig = _wrapCanvasConfigs[i];
                var canvasConfig = wrapConfig.canvasConfig;
                // RenderTexture
                var nextConfig = i+1< _wrapCanvasConfigs.Count ? _wrapCanvasConfigs[i+1] : null;
                var nextLayerHasLiquidGlass = nextConfig != null && 
                                              nextConfig.canvasConfig.HasLiquidGlasses(nextConfig.canvasConfig.canvas.gameObject.layer);
                var changeRt = i == 0 || canvasConfig.HasLiquidGlasses(canvasConfig.canvas.gameObject.layer);
                if (changeRt) _rtIndex++;
                var rtName = GetRtName(_rtIndex);
                featureLayerConfig.globalTextureName = rtName;
                // === current blur ===
                // == assign config
                featureLayerConfig.blur = canvasConfig.blur;
                featureLayerConfig.blurAlgorithm = canvasConfig.blurConfig.blurAlgorithm;
                featureLayerConfig.alphaBlendColor = canvasConfig.blurConfig.alphaBlendColor;
                featureLayerConfig.blurMip = canvasConfig.blurConfig.blurMip;
                featureLayerConfig.gaussianSigma = canvasConfig.blurConfig.gaussianSigma;
                featureLayerConfig.iteration = canvasConfig.blurConfig.iteration;
                featureLayerConfig.blurStrength = 1;
                // == next UIScreen has BGBlur 
                if (wrapConfig.lowerCanvasBlurConfig != null)
                {
                    var lowBlurConfig = wrapConfig.lowerCanvasBlurConfig;
                    featureLayerConfig.blur = true;
                    featureLayerConfig.blurAlgorithm = lowBlurConfig.blurAlgorithm;
                    featureLayerConfig.alphaBlendColor = lowBlurConfig.alphaBlendColor;
                    featureLayerConfig.blurMip = lowBlurConfig.blurMip;
                    featureLayerConfig.gaussianSigma = lowBlurConfig.gaussianSigma;
                    featureLayerConfig.iteration = lowBlurConfig.iteration;
                    featureLayerConfig.blurStrength = wrapConfig.lowerBlurStrength;
                }

                // global blur (next layer has LiquidGlass)
                if (nextLayerHasLiquidGlass)
                {
                    // According to next layer liquid glasses, calculate global blur config
                    GetNextLayerLiquidGlassBlurConfig(nextConfig.canvasConfig,
                        nextFeatureLayerConfig.layer, ref _nextLayerGlobalBlurConfig);
                    featureLayerConfig.globalBlurAlgorithm = _nextLayerGlobalBlurConfig.globalBlurAlgorithm;
                    featureLayerConfig.globalGaussianSigma = _nextLayerGlobalBlurConfig.globalGaussianSigma;
                    featureLayerConfig.globalIteration = _nextLayerGlobalBlurConfig.globalIteration;
                }
                else
                {
                    featureLayerConfig.globalBlurAlgorithm = GlobalBlurAlgorithm.None;
                }
                // setup LiquidGlass's background rt name
                SetupLiquidGlass(canvasConfig, featureLayerConfig.layer, GetRtName(_rtIndex - 1));
            }
            // Update renderer feature configs
            UICaptureEffectManager.Instance?.SetupLayerConfigs(_featureLayerConfigs);
            // Update replace feature's rt name
            UICaptureEffectManager.Instance?.SetReplaceRtName(GetRtName(_rtIndex));
        }
        
        public void SetLowerUIScreenDirty(UIScreen uiScreen, bool sortScreen = false)
        {
            if (!uiScreen) return;
            for (var i = 0; i < _wrapCanvasConfigs.Count; i++)
            {
                var featureLayerConfig = _featureLayerConfigs[i];
                var wrapConfig = _wrapCanvasConfigs[i];
                if (wrapConfig.lowerCanvasBlurConfig != null
                    && wrapConfig.lowerCanvasBlurConfig == uiScreen.lowerCanvasBlurConfig)
                {
                    UpdateRendererFeature();
                    // set dirty
                    UICaptureEffectManager.Instance?.SetDirty(featureLayerConfig.layer,true);
                }
            }
        }
        
        public void AddUIScreen(UIScreen screenConfig)
        {
            var replaceIndex = -1;
            // 判断是否重复
            for (var i = 0; i < _screens.Count; i++)
            {
                var config = _screens[i];
                if (screenConfig.Gid == config.Gid)
                {
                    replaceIndex = i;
                    break;
                }
            }

            if (replaceIndex == -1)
            {
                _screens.Add(screenConfig);
            }
            else
            {
                _screens[replaceIndex] = screenConfig;
            }

            HandleGraphicRaycasters();
        }

        public void RemoveUIScreen(UIScreen uiScreen)
        {
            _screens.Remove(uiScreen);
            HandleGraphicRaycasters();
        }
        
        public void MarkAllDirty()
        {
            foreach (var featureLayerConfig in _featureLayerConfigs)
            {
                Debug.Log($"Set {(int)featureLayerConfig.layer} dirty");
                UICaptureEffectManager.Instance?.SetDirty(featureLayerConfig.layer, true);
            }
        }
        #endregion

        private void HandleGraphicRaycasters()
        {
            //TODO: cache GraphicRaycasters
            // find top UIScreen 
            var topIndex = _screens.Count - 1;
            // enable top GraphicRaycaster
            // close others
            foreach (var canvasConfig in _wrapCanvasConfigs)
            {
                if (canvasConfig.canvasConfig.canvas == null)
                    return;
                var grs = canvasConfig.canvasConfig.canvas.GetComponentsInChildren<GraphicRaycaster>(true);
                for (var i = 0; i < grs.Length; i++)
                {
                    var gr = grs[i];
                    gr.enabled = i == topIndex;
                }
            }
            
        }

        private int CompareHierarchy(CanvasConfigWrapLayer lhs, CanvasConfigWrapLayer rhs)
        {
            var lTrans = lhs.canvasConfig.canvas.transform;
            var rTrans = rhs.canvasConfig.canvas.transform;
            return CompareHierarchy(lTrans, rTrans);
        }
        private int CompareHierarchy(UIScreen x, UIScreen y)
        {
            return CompareHierarchy(x.transform, y.transform);
        }
        List<Transform> _xParents = new ();
        List<Transform> _yParents = new ();
        private int CompareHierarchy(Transform x, Transform y)
        {
            if (!x || !y)
                return 0;
            if (x.IsChildOf(y) )
                return 1;

            if (y.IsChildOf(x))
                return -1;
            if (y.parent == x.parent)
            {
                var val1 = x.GetSiblingIndex();
                var val2 = y.GetSiblingIndex();
                return val1 - val2;
            }
            // 由根点往下判断各自父节点的先后关系
            else
            {
                // 共同父节点
                FindParents(x, ref _xParents);
                FindParents(y, ref _yParents); 
                Transform xSibling = null;
                Transform ySibling = null;
                for (var i = 0; i < _xParents.Count; i++)
                {
                    var xParent = _xParents[i];
                    if (_yParents.Contains(xParent))
                    {
                        xSibling = i > 0 ? _xParents[i - 1] : x;
                        var ySiblingIndex = _yParents.IndexOf(xParent);
                        ySibling = ySiblingIndex > 0 ? _yParents[ySiblingIndex - 1] : y;
                        break;
                    }
                }
                if (!xSibling || !ySibling) return 0;
                var val1 = xSibling.GetSiblingIndex();
                var val2 = ySibling.GetSiblingIndex();
                return val1 - val2;
            }
        }

        private void FindParents(Transform trans, ref List<Transform> parents)
        {
            parents.Clear();
            var cur = trans.parent; 
            while (cur)
            {
                parents.Add(cur);
                cur = cur.parent;
            }
        }

        private void GetNextLayerLiquidGlassBlurConfig(UIScreen.CanvasConfig canvasConfig, LayerMask layer, ref GlobalBlurConfig result)
        {
            var liquidGlasses = canvasConfig.cachedLiquidGlasses;
            result.globalGaussianSigma = 0;
            result.globalBlurAlgorithm = GlobalBlurAlgorithm.None;
            foreach (var liquidGlass in liquidGlasses)
            {
                if ((1 << liquidGlass.gameObject.layer & layer) > 0)
                {
                    if (liquidGlass.blurAlgorithm == BlurEffectType.GaussianBlur)
                        result.globalBlurAlgorithm |= GlobalBlurAlgorithm.GaussianSeparable;
                    if (liquidGlass.blurAlgorithm == BlurEffectType.MipMapChain)
                        result.globalBlurAlgorithm |= GlobalBlurAlgorithm.MipChain;
                    if (liquidGlass.blurAlgorithm == BlurEffectType.GaussianBlur && liquidGlass.gaussianSigma > result.globalGaussianSigma)
                        result.globalGaussianSigma = liquidGlass.gaussianSigma;
                }
            }
        }

        private void SetupLiquidGlass(UIScreen.CanvasConfig canvasConfig, LayerMask layer, string rtName)
        {
            var liquidGlasses = canvasConfig.cachedLiquidGlasses;
            foreach (var liquidGlass in liquidGlasses)
            {
                if ((1 << liquidGlass.gameObject.layer & layer) > 0)
                {
                    liquidGlass.backgroundRTName = rtName;
                }
            }
        }
        
        private class GlobalBlurConfig
        {
            public GlobalBlurAlgorithm globalBlurAlgorithm;
            public float globalGaussianSigma;
            public int globalIteration = 1;
        }

        private GlobalBlurConfig _nextLayerGlobalBlurConfig = new();

        private readonly List<int> _layers = new ();
        private readonly List<int> _layerBeSet = new();
        private readonly List<CanvasConfigWrapLayer> _sortCanvasConfigs = new();

        private class CanvasConfigWrapLayer
        {
            public int layer;  
            public UIScreen.CanvasConfig canvasConfig;
        }
        private void InitLayerConfigs(UIScreen uiScreen, List<UICaptureComposePerLayerFeature.LayerConfig> configs, 
            ref int beginLayer)
        {
            _layerBeSet.Clear();
            // 全部Layer清除
            SetLayers(uiScreen.gameObject, _hiddenLayer, _layerBeSet);
            _layers.Clear();
            for (var i = 0; i < uiScreen.canvasConfigs.Count; i++)
            {
                var canvasConfig = uiScreen.canvasConfigs[i];
                var layerIndex = beginLayer + i;
                if (!CheckLayerIsValid(layerIndex))
                {
                    continue;
                }
                
                // recursive set gameObject layer
                var layerMask = 1 << layerIndex;
                // featureLayerConfig's layer and isForeground
                var featureLayerConfig = GetOrCreateFeatureLayerConfig();
                featureLayerConfig.layer = layerMask;
                featureLayerConfig.isForeground = canvasConfig.isForeground;
                configs.Add(featureLayerConfig);
                _layers.Add(layerIndex);
            }
            // 排序
            ClearCanvasConfigWrappers(_sortCanvasConfigs);
            for (var i = 0; i < uiScreen.canvasConfigs.Count; i++)
            {
                var canvasConfig = uiScreen.canvasConfigs[i];
                var canvasConfigWrapper = GetOrCreateCanvasConfigWrapLayer();
                canvasConfigWrapper.layer = _layers[i];
                canvasConfigWrapper.canvasConfig = canvasConfig;
                
                _sortCanvasConfigs.Add(canvasConfigWrapper);
            }
            _sortCanvasConfigs.Sort(CompareHierarchy);
            for (var i = _sortCanvasConfigs.Count - 1; i >= 0; i--)
            {
                var canvasConfig = _sortCanvasConfigs[i];
                if (i < _layers.Count)
                {
                    SetLayers(canvasConfig.canvasConfig.canvas.gameObject, canvasConfig.layer, _layerBeSet);
                    _layerBeSet.Add(canvasConfig.layer);
                }
                else
                {
                    SetLayers(canvasConfig.canvasConfig.canvas.gameObject, _hiddenLayer, _layerBeSet);
                }
            }
            // update begin layer index
            beginLayer += uiScreen.canvasConfigs.Count; 
        }

        private void SetLayers(GameObject go, LayerMask layer, List<int> layerBeSet)
        {
            for (var i = 0; i < go.transform.childCount; i++)
            {
                var child = go.transform.GetChild(i);
                SetLayers(child.gameObject, layer, layerBeSet);
            }
            // 过滤已经设置的层
            if (!layerBeSet.Contains(go.layer))
                go.layer = layer;
        }

        private bool CheckLayerIsValid(int layer)
        {
            return layer >= _startLayer && layer <= _endLayer;
        }

        private readonly Dictionary<int, string> _rtNameMap = new();
        private string GetRtName(int rtIndex)
        {
            if (!_rtNameMap.ContainsKey(rtIndex))
                _rtNameMap[rtIndex] = "UI_BG_" + rtIndex;
            return _rtNameMap[rtIndex];
        }
        
        #region Config Pool

        private void ClearFeatureLayerConfigs(List<UICaptureComposePerLayerFeature.LayerConfig> featureLayerConfigList)
        {
            for (var i = 0; i < featureLayerConfigList.Count; i++)
            {
                _featureLayerConfigPool.Add(featureLayerConfigList[i]);
            }
            featureLayerConfigList.Clear();
        }

        private void ClearWrapCanvasConfigs(List<WrapCanvasConfig> wrapCanvasConfigs)
        {
            for (var i = 0; i < wrapCanvasConfigs.Count; i++)
            {
                _wrapCanvasConfigPool.Add(wrapCanvasConfigs[i]);
            }
            wrapCanvasConfigs.Clear();
        }

        private void ClearCanvasConfigWrappers(List<CanvasConfigWrapLayer> canvasConfigWrappers)
        {
            for (var i = 0; i < canvasConfigWrappers.Count; i++)
            {
                _wrapCanvasConfigLayerPool.Add(canvasConfigWrappers[i]);
            } 
            canvasConfigWrappers.Clear();
        }

        private WrapCanvasConfig GetOrCreateWrapCanvasConfig()
        {
            WrapCanvasConfig result;
            if (_wrapCanvasConfigPool.Count > 0)
            {
                result = _wrapCanvasConfigPool[0];
                result.Reset();
                _wrapCanvasConfigPool.RemoveAt(0);
            }
            else
            {
                result = new WrapCanvasConfig();
            }

            return result;
        }
        
        private UICaptureComposePerLayerFeature.LayerConfig GetOrCreateFeatureLayerConfig()
        {
            UICaptureComposePerLayerFeature.LayerConfig result;
            if (_featureLayerConfigPool.Count > 0)
            {
                result = _featureLayerConfigPool[0];
                _featureLayerConfigPool.RemoveAt(0);
            }
            else
            {
                result = new UICaptureComposePerLayerFeature.LayerConfig();
            }

            return result;
        }

        private CanvasConfigWrapLayer GetOrCreateCanvasConfigWrapLayer()
        {
            CanvasConfigWrapLayer result;
            if (_wrapCanvasConfigLayerPool.Count > 0)
            {
               result = _wrapCanvasConfigLayerPool[0];
               _wrapCanvasConfigLayerPool.RemoveAt(0);
            }
            else
            {
                result = new CanvasConfigWrapLayer();
            }
            return result;
        }

        #endregion
    }
}