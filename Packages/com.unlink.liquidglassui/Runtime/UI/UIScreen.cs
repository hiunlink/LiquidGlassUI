using System.Collections.Generic;
#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;

namespace Unlink.LiquidGlassUI
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
    
    [ExecuteInEditMode]
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

            private LiquidGlassUIEffect[] _cachedLiquidGlasses;

            public void SetCachedLiquidGlasses(LiquidGlassUIEffect[] liquidGlasses)
            {
                _cachedLiquidGlasses = liquidGlasses;
            }
            public LiquidGlassUIEffect[] cachedLiquidGlasses => _cachedLiquidGlasses;
        }
        [Header("是否开启底层模糊效果")]
        public bool lowerBlur = true;
        [Range(0, 1)]
        public float lowerBlurStrength = 1;
        [ShowIf("lowerBlur")]
        public CanvasBlurConfig lowerCanvasBlurConfig;
        [Header("子画布配置")]
        public List<CanvasConfig> canvasConfigs;

        public bool isActive => gameObject.activeInHierarchy;

        private int _gid;
        public int Gid => _gid;

#if UNITY_EDITOR
        private static bool _isExitingPlayMode = false;
        private static bool _isExitingEditorMode = false;

        static UIScreen()
        {
            // 监听编辑器 playmode 切换
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            // 正在从 PlayMode 回编辑器模式
            if (state == PlayModeStateChange.ExitingPlayMode)
                _isExitingPlayMode = true;
            // 完成转换后重置标记
            if (state == PlayModeStateChange.EnteredEditMode)
            {
                _isExitingPlayMode = false;
                _isExitingEditorMode = false;
            }

            if (state == PlayModeStateChange.ExitingEditMode)
                _isExitingEditorMode = true;
            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                _isExitingEditorMode = false;
                _isExitingPlayMode = false;
            }
        }
#endif
        
        private void Init()
        {
            _gid = gameObject.GetInstanceID();
            FindLiquidGlassAndUpdateStates();
            UIScreenManager.Instance.AddUIScreen(this);
        }
        
        private void OnEnable()
        {
            Init();
            UIScreenManager.Instance.UpdateRendererFeature(true);
            UIScreenManager.Instance.SetLowerUIScreenDirty(this);
        }

        private void OnDisable()
        {
            UIScreenManager.Instance.RemoveUIScreen(this);

#if UNITY_EDITOR
            var willChange = EditorApplication.isPlayingOrWillChangePlaymode && !Application.isPlaying;
            if (willChange && !_isExitingEditorMode)
                _isExitingEditorMode = true;
            if (_isExitingPlayMode || _isExitingEditorMode)
            {
                return;
            }
#endif
            UIScreenManager.Instance.SetLowerUIScreenDirty(this,true);
        }

        private void FindLiquidGlassAndUpdateStates()
        {
            foreach (var canvasConfig in canvasConfigs)
            {
                var glassUIEffects = canvasConfig.canvas.transform.GetComponentsInChildren<LiquidGlassUIEffect>();
                canvasConfig.SetCachedLiquidGlasses(glassUIEffects);
                canvasConfig.hasLiquidGlass = glassUIEffects.Length > 0;
            }
        }

        #region Public

        public void SetLowerBlurStrength(float blurStrength)
        {
            if (!Mathf.Approximately(lowerBlurStrength, blurStrength))
            {
                lowerBlurStrength = blurStrength;
                UIScreenManager.Instance.SetLowerUIScreenDirty(this);
            }
        }

        #endregion

        [ContextMenu("更新渲染管线")]
        private void UpdateRendererFeature()
        {
            UIScreenManager.Instance.UpdateRendererFeature(true);
        }
    }
}