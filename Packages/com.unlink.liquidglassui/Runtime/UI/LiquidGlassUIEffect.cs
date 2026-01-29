#if UNITY_EDITOR
using UnityEditor;
#endif
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace Unlink.LiquidGlassUI
{
    public enum BlurEffectType
    {
        None,
        MipMapChain,
        GaussianBlur,
    }

    /// <summary>
    /// Liquid Glass UI driver (Unity UI Effect)
    /// - Works on Graphic (Image/RawImage/TextMeshProUGUI via MaskableGraphic pipeline)
    /// - Computes analytic rounded-rect SDF params from RectTransform + px inputs
    /// - Pushes to material instance used by MaskableGraphic (supports Mask/RectMask)
    /// - Normalization base: Canvas.pixelRect.height
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Graphic))]
    public class LiquidGlassUIEffect : UIBehaviour
    {
        [Header("LiquidGlass SDF")] [Tooltip("Corner radius in pixels (UI designer units)")]
        public float cornerRadiusPx = 90;

        [Tooltip("Border width in pixels (inner shrink; 0 = no border")]
        public float borderWidthPx = 4f;

        [Header("Blur")] public BlurEffectType blurAlgorithm = BlurEffectType.GaussianBlur;
        [Range(0, 8)] public float mipMapLod = 1.4f;
        [Range(0, 6)] public float gaussianSigma = 4; 

        [Header("Refraction")] public float refractionEdgeWidth = 35f;
        public float refractionMagnitude = 100f;
        public float chromaticAberration = 33f;
        [Range(0, 4)] public float refractionLODBias = 1.32f;

        [Header("Appears")] public float edgeRimWidth = 6f;
        public Color rimLight = new Color(1, 1, 1, 0.5f);
        public Color glassTint = new Color(0 , 0, 0, 0.3f);

        [Header("Material")] public string backgroundRTName = "_UI_BG";

        [Tooltip("Auto-assign Hidden/UI_LiquidGlass when using default UI material")]
        public bool autoAssignMaterial = true;

        [Header("Advanced")] [Tooltip("Update parameters every LateUpdate (useful if animating size via layout)")]
        public bool continuousUpdate = true;

        RectTransform _rt;
        Graphic _graphic;
        private Material _instancedMat;
        Canvas _canvas;
        RectTransform _rootTransform;
        Camera _uiCamera;

        private RenderTexture _bgRT;
        private RenderTexture _blurRT;

        static readonly int ID_HalfSize = Shader.PropertyToID("_RoundedRectHalfSize");
        static readonly int ID_Radius = Shader.PropertyToID("_RoundedRadius");
        static readonly int ID_Border = Shader.PropertyToID("_BorderWidth");
        static readonly int ID_RectOffset = Shader.PropertyToID("_RectUVOffset");

        static readonly int ID_UseBlurBG = Shader.PropertyToID("_UseBlurBG");
        static readonly int ID_UIBG_Lod = Shader.PropertyToID("_UIBG_Lod");
        static readonly int ID_RefrDim = Shader.PropertyToID("_RefrDim");
        static readonly int ID_RefrMag = Shader.PropertyToID("_RefrMag");
        static readonly int ID_RefrAberration = Shader.PropertyToID("_RefrAberration");
        static readonly int ID_RefrLODBias = Shader.PropertyToID("_RefrLODBias");

        static readonly int ID_EdgeDim = Shader.PropertyToID("_EdgeDim");
        static readonly int ID_TintColor = Shader.PropertyToID("_TintColor");
        static readonly int ID_RimLightColor = Shader.PropertyToID("_RimLightColor");
        static readonly int ID_UI_BG = Shader.PropertyToID("_UI_BG");
        static readonly int ID_UI_BG_BLUR = Shader.PropertyToID("_UI_BG_BLUR");

        private LocalKeyword KW_WITHOUT_BG;

        public const string ShaderName = "Hidden/UI_LiquidGlass";
        [SerializeField] private bool forceRecreateMaterial = false;

        protected override void OnEnable()
        {
            base.OnEnable();
            EnsureMaterial();
            CacheComponents();
            ApplyParameters();
            _graphic.SetMaterialDirty();
        }


#if UNITY_EDITOR
        protected override void Reset()
        {
            base.Reset();
            // Called when component is added in Editor
            EnsureMaterial();
            ApplyParameters();
            if (_graphic) EditorUtility.SetDirty(_graphic);
        }
#endif

#if UNITY_EDITOR
        protected override void OnValidate()
        {
            base.OnValidate();
            // Also auto-assign in editor when modified
            EnsureMaterial();
            ApplyParameters();
            if (_graphic) _graphic.SetMaterialDirty();
        }
#endif

        protected override void OnDisable()
        {
            base.OnDisable();
            ReleaseInstancedMaterial();
        }

        void LateUpdate()
        {
            if (continuousUpdate)
            {
                ApplyParameters();
            }
        }

        protected override void OnRectTransformDimensionsChange()
        {
            base.OnRectTransformDimensionsChange();
            if(!isActiveAndEnabled)
                return;
            
            ApplyParameters();
            if (_graphic) _graphic.SetMaterialDirty();
        }

#if UNITY_EDITOR
        [ContextMenu("Force Assign LiquidGlass Material")]
        void ForceAssign()
        {
            autoAssignMaterial = true;
            EnsureMaterial();
            ApplyParameters();
            if (_graphic) EditorUtility.SetDirty(_graphic);
        }
#endif

        void CacheComponents()
        {
            if (!_graphic) _graphic = GetComponent<Graphic>();
            if (!_rt) _rt = GetComponent<RectTransform>();
            if (!_canvas && _graphic) _canvas = _graphic.canvas;
            if (!_rootTransform && _canvas)  _rootTransform = _canvas.rootCanvas.transform.GetComponent<RectTransform>();
            if (!_uiCamera && _rootTransform) _uiCamera = _rootTransform.GetComponent<Canvas>().worldCamera; 
        }

        bool _needNewChecked = false;
        bool _keyWordChecked = false;
        void EnsureMaterial()
        {
            if (_graphic == null) _graphic = GetComponent<Graphic>();

            // 用 sharedMaterial 检查，不要用 material（会实例化）
            var shared = _graphic.material;

            bool needNew =
                forceRecreateMaterial ||
                shared == null ||
                shared.shader == null ||
                shared.shader.name != ShaderName ||
                // 如果 shared 是别的系统共用材质，你希望每个控件单独参数，则必须实例化
                shared == _graphic.defaultMaterial;

            _needNewChecked = true;
            if (!needNew) return;

            // 释放旧实例
            ReleaseInstancedMaterial();

            // 优先从 Resources 默认模板创建实例
            _instancedMat = LiquidGlassMaterialProvider.CreateInstanceOrNull();
            if (_instancedMat == null)
            {
                // fallback：尝试 Shader.Find（可能失败，但作为兜底）
                var shader = Shader.Find(ShaderName);
                if (shader != null) _instancedMat = new Material(shader);
            }

            if (_instancedMat != null)
            {
                _graphic.material = _instancedMat;
            }

            forceRecreateMaterial = false;
        }

        void ApplyParameters()
        {
            CacheComponents();
            if (!_graphic) return;
            if (!_rt) _rt = GetComponent<RectTransform>();
            if (!_canvas) _canvas = _graphic.canvas;
            if (!_canvas) return;
            if (!_rootTransform) return;

            var mat = _graphic.materialForRendering; // per-instance instance used by UI
            if (!mat) return;
            
            Vector2 sizePx, targetSizePx;
            var rectUVOffset = CalcRectUVOffset01MinusHalf(_rt, _canvas, _uiCamera, out sizePx, out targetSizePx);
            
            // 你的 halfSize / radius / border 也建议用 sizePx 来算（更一致）
            var canvasH = targetSizePx.y; // 用渲染目标高度做基准（和 shader 的 _ScreenParams.y 对齐） 

            // Normalize by canvas height, then half-size for SDF space
            var halfSize = new Vector2(sizePx.x * 0.5f / canvasH, sizePx.y * 0.5f / canvasH);
            var radius = Mathf.Max(0f, cornerRadiusPx) / canvasH;
            var border = Mathf.Max(0f, borderWidthPx) / canvasH;

            _bgRT = UICaptureEffectManager.Instance?.GetRenderTexture(backgroundRTName);
            _blurRT = UICaptureEffectManager.Instance?.GetBlurRenderTexture(backgroundRTName);
            if (_needNewChecked && !_keyWordChecked)
            {
                if (mat.shader.keywordSpace.keywordNames.Contains("WITHOUT_UI_BG"))
                    KW_WITHOUT_BG = new LocalKeyword(mat.shader, "WITHOUT_UI_BG");
                else
                {
                    KW_WITHOUT_BG = new LocalKeyword();
                }

                _keyWordChecked = true;
            }
            if (KW_WITHOUT_BG == default)
                return;

            if (!_bgRT || !_blurRT || blurAlgorithm == BlurEffectType.None)
            {
                mat.EnableKeyword(KW_WITHOUT_BG);
            }
            else
            {
                mat.DisableKeyword(KW_WITHOUT_BG);
                mat.SetTexture(ID_UI_BG, _bgRT);
                mat.SetTexture(ID_UI_BG_BLUR, _blurRT);
            }

            // Push to material
            mat.SetVector(ID_HalfSize, halfSize);
            mat.SetFloat(ID_Radius, radius);
            mat.SetFloat(ID_Border, border);
            mat.SetVector(ID_RectOffset, rectUVOffset);

            // ======= Material properties ======== //
            // Blur 
            mat.SetFloat(ID_UseBlurBG, blurAlgorithm == BlurEffectType.GaussianBlur ? 1f : 0);
            mat.SetFloat(ID_UIBG_Lod, blurAlgorithm == BlurEffectType.None ? 0 : mipMapLod);
            // Refraction
            mat.SetFloat(ID_RefrDim, Mathf.Max(refractionEdgeWidth / canvasH, 0));
            mat.SetFloat(ID_RefrMag, Mathf.Max(refractionMagnitude / canvasH, 0));
            mat.SetFloat(ID_RefrAberration, Mathf.Max(chromaticAberration, 0));
            mat.SetFloat(ID_RefrLODBias, Mathf.Max(refractionLODBias, 0));
            // Appears
            mat.SetFloat(ID_EdgeDim, Mathf.Max(edgeRimWidth / canvasH, 0));
            mat.SetColor(ID_RimLightColor, rimLight);
            mat.SetColor(ID_TintColor, glassTint);
        }
        static Vector3[] corners = new Vector3[4];
        // 世界角点求中心 → 转屏幕像素 → 转 UV
        static Vector2 CalcRectUVOffset01MinusHalf(RectTransform rt, Canvas canvas, Camera uiCam,
            out Vector2 sizePx, out Vector2 targetSizePx)
        {
            // 1) 取世界角点（包含父节点、锚点、缩放、旋转等所有变换）
           rt.GetWorldCorners(corners);

            // 2) 元素中心点（世界）
            var centerWorld = (corners[0] + corners[2]) * 0.5f;

            // 3) 投影到屏幕像素坐标
            Vector2 centerPx;
            if (canvas.renderMode == RenderMode.ScreenSpaceOverlay || uiCam == null)
            {
                // Overlay：世界坐标已经在屏幕平面内（但仍建议走 RectTransformUtility）
                RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    canvas.transform as RectTransform,
                    RectTransformUtility.WorldToScreenPoint(null, centerWorld),
                    null,
                    out _); // 这里不需要 local 点
                centerPx = RectTransformUtility.WorldToScreenPoint(null, centerWorld);
            }
            else
            {
                centerPx = RectTransformUtility.WorldToScreenPoint(uiCam, centerWorld);
            }

            // 4) 目标渲染尺寸（用于把像素转 UV）
            //    最正确：用 uiCam 的 pixelRect（考虑 viewport）；否则用 Screen
            Rect pr = (uiCam != null) ? uiCam.pixelRect : new Rect(0,0,Screen.width, Screen.height);
            targetSizePx = new Vector2(pr.width, pr.height);

            // 5) 计算该元素屏幕像素尺寸（用于 halfSize）
            Vector2 p0 = RectTransformUtility.WorldToScreenPoint(uiCam, corners[0]);
            Vector2 p2 = RectTransformUtility.WorldToScreenPoint(uiCam, corners[2]);
            sizePx = new Vector2(Mathf.Abs(p2.x - p0.x), Mathf.Abs(p2.y - p0.y));

            // 6) 转 UV（0..1）并减 0.5 → -0.5..0.5
            //    注意：centerPx 是全屏坐标，要减去 pixelRect 的 origin
            Vector2 centerInRect = centerPx - new Vector2(pr.xMin, pr.yMin);
            Vector2 uv01 = new Vector2(centerInRect.x / targetSizePx.x, centerInRect.y / targetSizePx.y);

            return uv01 - new Vector2(0.5f, 0.5f);
        }
 
        private void ReleaseInstancedMaterial()
        {
            if (_instancedMat == null) return;

#if UNITY_EDITOR
            if (!Application.isPlaying) DestroyImmediate(_instancedMat);
            else Destroy(_instancedMat);
#else
            Destroy(_instancedMat);
#endif
            _instancedMat = null;
        }
    }
}