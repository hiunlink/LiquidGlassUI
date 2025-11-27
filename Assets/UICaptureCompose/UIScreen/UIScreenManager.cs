using System.Collections.Generic;
using UICaptureCompose.UIComponent;
using UICaptureCompose.URP;
using UnityEngine;

namespace UICaptureCompose.UIScreen
{
    public class UIScreenManager
    {
        private class WrapCanvasConfig
        {
            public int prevLayer = 1;
            public CanvasBlurConfig lowerCanvasBlurConfig = null;
            public UIScreen.CanvasConfig canvasConfig;
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

        private const float GlobalBlurSigma = 5;
        private const int GlobalBlurIteration = 1;
        
        private int _startLayer = 6;
        private const int EndLayer = 31;
        private readonly List<UIScreen> _screens = new();
        public void Setup(int startLayer)
        {
            _startLayer = startLayer;
        }

        public void AddUIScreenAndUpdateRendererFeature(UIScreen screenConfig)
        {
            AddUIScreen(screenConfig);
            UpdateRendererFeature();
        }

        private int _rtIndex = 0;
        public void UpdateRendererFeature()
        {
            _rtIndex = 0;
            var curLayer = _startLayer;
            var featureLayerConfigs = new List<UICaptureComposePerLayerFeature.LayerConfig>();
            var wrapCanvasConfigs = new List<WrapCanvasConfig>();
            // setup renderer feature layer configs
            for (var uiLayer = 0; uiLayer < _screens.Count; uiLayer++)
            {
                var uiScreen = _screens[uiLayer];
                if (!TryInitLayerConfigs(uiScreen, featureLayerConfigs, ref curLayer))
                    break;
                
                for (var i = 0; i < uiScreen.canvasConfigs.Count; i++)
                {
                    var canvasConfig = uiScreen.canvasConfigs[i];
                    var wrapConfig = new WrapCanvasConfig()
                    {
                        canvasConfig = canvasConfig,
                    };
                    wrapCanvasConfigs.Add(wrapConfig);
                    // Check next uiScreen lower canvas blur config
                    if (i == uiScreen.canvasConfigs.Count - 1)
                    {
                        var nextUiLayer = uiLayer + 1;
                        var nextUiScreen = _screens.Count > nextUiLayer ? _screens[nextUiLayer] : null;
                        if (nextUiScreen is { prevLayer: 1 })
                        {
                            wrapConfig.prevLayer = nextUiScreen.prevLayer;
                            wrapConfig.lowerCanvasBlurConfig = nextUiScreen.lowerCanvasBlurConfig;
                        }
                    }
                }
            }
            // setup others
            for (var i = 0; i < wrapCanvasConfigs.Count; i++)
            {
                var featureLayerConfig = featureLayerConfigs[i];
                var wrapConfig = wrapCanvasConfigs[i];
                var canvasConfig = wrapConfig.canvasConfig;
                // RenderTexture
                var nextConfig = i+1< wrapCanvasConfigs.Count ? wrapCanvasConfigs[i+1] : null;
                var nextLayerHasLiquidGlass = nextConfig != null && nextConfig.canvasConfig.hasLiquidGlass;
                var changeRt = i == 0 || canvasConfig.hasLiquidGlass;
                if (changeRt) _rtIndex++;
                var rtName = "UI_BG_" + _rtIndex;
                featureLayerConfig.globalTextureName = rtName;
                // === current blur ===
                // == assign config
                featureLayerConfig.blur = canvasConfig.blur;
                featureLayerConfig.blurAlgorithm = canvasConfig.blurConfig.blurAlgorithm;
                featureLayerConfig.alphaBlendColor = canvasConfig.blurConfig.alphaBlendColor;
                featureLayerConfig.blurMip = canvasConfig.blurConfig.blurMip;
                featureLayerConfig.gaussianSigma = canvasConfig.blurConfig.gaussianSigma;
                featureLayerConfig.iteration = canvasConfig.blurConfig.iteration;
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
                }

                // global blur (next layer has LiquidGlass)
                if (nextLayerHasLiquidGlass)
                {
                    featureLayerConfig.globalBlurAlgorithm =
                        GlobalBlurAlgorithm.GaussianSeparable | GlobalBlurAlgorithm.MipChain;
                    featureLayerConfig.globalGaussianSigma = GlobalBlurSigma;
                    featureLayerConfig.globalIteration = GlobalBlurIteration;
                }
                else
                {
                    featureLayerConfig.globalBlurAlgorithm = GlobalBlurAlgorithm.None;
                }
                // setup LiquidGlass's background rt name
                SetupLiquidGlass(canvasConfig.canvas.transform, featureLayerConfig.layer, "UI_BG_" + (_rtIndex - 1));
            }
            // Update renderer feature configs
            UICaptureEffectManager.Instance.SetupLayerConfigs(featureLayerConfigs);
            // Update replace feature's rt name
            UICaptureEffectManager.Instance.SetReplaceRtName("UI_BG_" + _rtIndex);
        }

        private void SetupLiquidGlass(Transform canvasTransform, LayerMask layer, string rtName)
        {
            var liquidGlasses = canvasTransform.GetComponentsInChildren<LiquidGlassUIEffect>(true);
            foreach (var liquidGlass in liquidGlasses)
            {
                if ((1 << liquidGlass.gameObject.layer & layer) > 0)
                {
                    liquidGlass.backgroundRTName = rtName;
                }
            }
        }

        private bool TryInitLayerConfigs(UIScreen uiScreen, List<UICaptureComposePerLayerFeature.LayerConfig> configs, 
            ref int beginLayer)
        {
            var result = true;
            var layers = new List<int>();
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
                var featureLayerConfig = new UICaptureComposePerLayerFeature.LayerConfig()
                {
                    layer = layerMask,
                    isForeground = canvasConfig.isForeground, 
                };
                configs.Add(featureLayerConfig);
                layers.Add(layerIndex);
            }
            for (var i = uiScreen.canvasConfigs.Count - 1; i >= 0; i--)
            {
                var canvasConfig = uiScreen.canvasConfigs[i];
                SetLayers(canvasConfig.canvas.gameObject, layers[i]);
            }
            // update begin layer index
            beginLayer += uiScreen.canvasConfigs.Count; 

            return result;
        }

        private void SetLayers(GameObject go, LayerMask layer)
        {
            foreach (Transform child in go.transform)
            {
                SetLayers(child.gameObject, layer);
            }

            go.layer = layer;
        }

        private bool CheckLayerIsValid(int layer)
        {
            return layer >= _startLayer && layer <= EndLayer;
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
        
    }
}