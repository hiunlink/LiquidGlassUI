using System.Collections.Generic;
using UICaptureCompose.UIComponent;
using UICaptureCompose.URP;
using UnityEngine;
#if UNITY_EDITOR
using UICaptureCompose.UIComponent.Editor;
#endif

namespace UICaptureCompose.UIScreen
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
        private const int EndLayer = 31;
        private readonly List<UIScreen> _screens = new();
        
        private readonly List<UICaptureComposePerLayerFeature.LayerConfig> _featureLayerConfigPool = new();
        private readonly List<WrapCanvasConfig> _wrapCanvasConfigPool = new();
        private readonly List<UICaptureComposePerLayerFeature.LayerConfig> _featureLayerConfigs = new();
        private readonly List<WrapCanvasConfig> _wrapCanvasConfigs = new();
#if UNITY_EDITOR
        private UIScreenManager()
        {
            LiquidGlassUIEffectEditorHooks.OnPropertiesChanged += OnSigmaChanged;
        }

        private void OnSigmaChanged(UIScreen uiScreen)
        {
            UpdateRendererFeature();
            SetLowerUIScreenDirty(uiScreen);
        }
#endif

        #region Public
      
        public void Setup(int startLayer)
        {
            _startLayer = startLayer;
        }

        public void AddUIScreenAndUpdateRendererFeature(UIScreen screenConfig)
        {
            AddUIScreen(screenConfig);
            UpdateRendererFeature();
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
                if (!TryInitLayerConfigs(uiScreen, _featureLayerConfigs, ref curLayer))
                    break;
                
                for (var i = 0; i < uiScreen.canvasConfigs.Count; i++)
                {
                    var canvasConfig = uiScreen.canvasConfigs[i];
                    var wrapConfig = GetOrCreateWrapCanvasConfig();
                    wrapConfig.canvasConfig = canvasConfig;
                    _wrapCanvasConfigs.Add(wrapConfig);
                    // Check next uiScreen lower canvas blur config
                    if (i == uiScreen.canvasConfigs.Count - 1)
                    {
                        var nextUiLayer = uiLayer + 1;
                        var nextUiScreen = _screens.Count > nextUiLayer ? _screens[nextUiLayer] : null;
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
                var nextLayerHasLiquidGlass = nextConfig != null && nextConfig.canvasConfig.hasLiquidGlass;
                var changeRt = i == 0 || canvasConfig.hasLiquidGlass;
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
            UICaptureEffectManager.Instance.SetupLayerConfigs(_featureLayerConfigs);
            // Update replace feature's rt name
            UICaptureEffectManager.Instance.SetReplaceRtName(GetRtName(_rtIndex));
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
                    UICaptureEffectManager.Instance.SetDirty(featureLayerConfig.layer,true);
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
        }

        public void RemoveUIScreen(UIScreen uiScreen)
        {
            _screens.Remove(uiScreen);
        }
        
        #endregion

        private int CompareHierarchy(UIScreen x, UIScreen y)
        {
            if (!x || !y)
                return 0;
            if (x.transform.IsChildOf(y.transform) )
                return 1;

            if (y.transform.IsChildOf(x.transform))
                return -1;
            if (y.transform.parent == x.transform.parent)
            {
                var val1 = x.transform.GetSiblingIndex();
                var val2 = y.transform.GetSiblingIndex();
                return val1 - val2;
            }

            return 0;
        }

        private void GetNextLayerLiquidGlassBlurConfig(UIScreen.CanvasConfig canvasConfig, LayerMask layer, ref GlobalBlurConfig result)
        {
            var liquidGlasses = canvasConfig.cachedLiquidGlasses;
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
        private bool TryInitLayerConfigs(UIScreen uiScreen, List<UICaptureComposePerLayerFeature.LayerConfig> configs, 
            ref int beginLayer)
        {
            var result = true;
            _layers.Clear();
            for (var i = 0; i < uiScreen.canvasConfigs.Count; i++)
            {
                var canvasConfig = uiScreen.canvasConfigs[i];
                var layerIndex = beginLayer + i;
                if (!CheckLayerIsValid(layerIndex))
                {
                    result = false;
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
            for (var i = uiScreen.canvasConfigs.Count - 1; i >= 0; i--)
            {
                var canvasConfig = uiScreen.canvasConfigs[i];
                SetLayers(canvasConfig.canvas.gameObject, _layers[i]);
            }
            // update begin layer index
            beginLayer += uiScreen.canvasConfigs.Count; 

            return result;
        }

        private void SetLayers(GameObject go, LayerMask layer)
        {
            for (var i = 0; i < go.transform.childCount; i++)
            {
                var child = go.transform.GetChild(i);
                SetLayers(child.gameObject, layer);
            }

            go.layer = layer;
        }

        private bool CheckLayerIsValid(int layer)
        {
            return layer >= _startLayer && layer <= EndLayer;
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

        #endregion
    }
}