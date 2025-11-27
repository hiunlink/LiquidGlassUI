using System;
using System.Collections.Generic;
using UICaptureCompose.UIComponent;
using UICaptureCompose.URP;
using UnityEngine;

namespace UICaptureCompose.UIScreen
{
    [System.Serializable]
    public class CanvasBlurConfig
    {
        public BlurAlgorithm blurAlgorithm = BlurAlgorithm.MipChain;
        [ShowIf("blurAlgorithm", (int)BlurAlgorithm.GaussianSeparable)]
        public Color alphaBlendColor = Color.clear;
        [ShowIf("blurAlgorithm", (int)BlurAlgorithm.MipChain)]
        [Range(0, 8)] public float blurMip = 3;
        [ShowIf("blurAlgorithm", (int)BlurAlgorithm.GaussianSeparable)]
        [Range(0f, 6f)] public float gaussianSigma = 2.0f;
        [ShowIf("blurAlgorithm", (int)BlurAlgorithm.GaussianSeparable)]
        [Range(1, 5)] public int iteration = 1;
    }
    
    public class UIScreen: MonoBehaviour
    {
        [System.Serializable]
        public class CanvasConfig
        {
            public Canvas canvas;
            [Tooltip("是否为前景层（大面积 Opaque 遮挡）")]
            public bool isForeground = false;

            [Tooltip("该层是否需要模糊")]
            public bool blur = false;

            [HideInInspector]
            public bool hasLiquidGlass;

            [ShowIf("blur")] 
            public CanvasBlurConfig blurConfig;
        }
        [Header("底层模糊效果（0代表不做底层模糊）")]
        [Range(0, 1)] 
        public int prevLayer = 1;
        [Range(0, 1)]
        public float lowerBlurStrength = 1;
        [ShowIf("prevLayer", 1)]
        public CanvasBlurConfig lowerCanvasBlurConfig;
        [Header("子画布配置")]
        public List<CanvasConfig> canvasConfigs;

        public bool isActive => gameObject.activeInHierarchy;

        private int _gid;
        public int Gid => _gid;
        
        private void Awake()
        {
            _gid = gameObject.GetInstanceID();
            FindLiquidGlassAndUpdateStates();
        }

        private void FindLiquidGlassAndUpdateStates()
        {
            foreach (var canvasConfig in canvasConfigs)
            {
                var glassUIEffects = canvasConfig.canvas.transform.GetComponentsInChildren<LiquidGlassUIEffect>();
                canvasConfig.hasLiquidGlass = glassUIEffects.Length > 0;
            }
        }

        [ContextMenu("加入到管理器")]
        private void AddToUIScreenManager()
        {
            _gid = gameObject.GetInstanceID();
            FindLiquidGlassAndUpdateStates();
            UIScreenManager.Instance.AddUIScreen(this);
        }
        [ContextMenu("更新渲染管线")]
        private void UpdateRendererFeature()
        {
            UIScreenManager.Instance.UpdateRendererFeature();
        }
    }
}