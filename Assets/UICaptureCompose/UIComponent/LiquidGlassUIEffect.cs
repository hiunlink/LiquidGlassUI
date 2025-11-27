#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace UICaptureCompose.UIComponent
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

        [Header("Blur")] public BlurEffectType blurAlgorithm = BlurEffectType.MipMapChain;
        [Range(0, 8)] public float mipMapLod = 1.4f;

        [Header("Refraction")] public float refractionEdgeWidth = 35f;
        public float refractionMagnitude = 100f;
        public float chromaticAberration = 33f;
        [Range(0, 4)] public float refractionLODBias = 1.32f;

        [Header("Appears")] public float edgeRimWidth = 6f;
        public Color rimLight = new Color(1, 1, 1, 0.15f);
        public Color glassTint = Color.clear;

        [Header("Material")] public string backgroundRTName = "_UI_BG";

        [Tooltip("Auto-assign Hidden/UI_LiquidGlass when using default UI material")]
        public bool autoAssignMaterial = true;

        [Header("Advanced")] [Tooltip("Update parameters every LateUpdate (useful if animating size via layout)")]
        public bool continuousUpdate = true;

        RectTransform _rt;
        Graphic _graphic;
        Canvas _canvas;

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

        const string ShaderName = "Hidden/UI_LiquidGlass";

        protected override void OnEnable()
        {
            base.OnEnable();
            EnsureMaterialAssigned();
            CacheComponents();
            SetParamsOnMaterialInstance();
            _graphic.SetMaterialDirty();
        }


#if UNITY_EDITOR
        protected override void Reset()
        {
            base.Reset();
            // Called when component is added in Editor
            EnsureMaterialAssigned();
            SetParamsOnMaterialInstance();
            if (_graphic) EditorUtility.SetDirty(_graphic);
        }
#endif

#if UNITY_EDITOR
        protected override void OnValidate()
        {
            base.OnValidate();
            // Also auto-assign in editor when modified
            EnsureMaterialAssigned();
            SetParamsOnMaterialInstance();
            if (_graphic) _graphic.SetMaterialDirty();
        }
#endif

        protected override void OnDisable()
        {
            base.OnDisable();
            if (_graphic) _graphic.SetMaterialDirty();
        }

        void LateUpdate()
        {
            if (continuousUpdate)
            {
                SetParamsOnMaterialInstance();
            }
        }

        protected override void OnRectTransformDimensionsChange()
        {
            base.OnRectTransformDimensionsChange();
            if(!isActiveAndEnabled)
                return;
            
            SetParamsOnMaterialInstance();
            if (_graphic) _graphic.SetMaterialDirty();
        }

#if UNITY_EDITOR
        [ContextMenu("Force Assign LiquidGlass Material")]
        void ForceAssign()
        {
            autoAssignMaterial = true;
            EnsureMaterialAssigned();
            SetParamsOnMaterialInstance();
            if (_graphic) EditorUtility.SetDirty(_graphic);
        }
#endif

        void CacheComponents()
        {
            if (!_graphic) _graphic = GetComponent<Graphic>();
            if (!_rt) _rt = GetComponent<RectTransform>();
            if (!_canvas && _graphic) _canvas = _graphic.canvas;
        }


        void EnsureMaterialAssigned()
        {
            if (!autoAssignMaterial) return;
            CacheComponents();
            if (_graphic == null) return;

            var shader = Shader.Find(ShaderName);
            if (shader == null) return;
            var curr = _graphic.material;
            string currName = (curr != null && curr.shader != null) ? curr.shader.name : string.Empty;

            // Replace only if using standard UI defaults or missing
            bool shouldReplace = (curr == null) || (curr.shader == null) ||
                                 currName == "UI/Default" || currName == "Sprites/Default" ||
                                 currName == "TextMeshPro/Distance Field" ||
                                 currName == string.Empty;

            // Also allow upgrade if user explicitly wants LiquidGlass but a different shader is set
            if (!shouldReplace) return;

#if UNITY_EDITOR
            Undo.RecordObject(_graphic, "Assign LiquidGlass Material");
#endif
            var newMat = new Material(shader) { name = "UI_LiquidGlass (Auto)" };

#if UNITY_EDITOR
            if (!Application.isPlaying)
                EditorUtility.SetDirty(_graphic);
#endif

            _graphic.material = newMat;
#if UNITY_EDITOR
            if (!Application.isPlaying)
                EditorUtility.SetDirty(_graphic);
#endif
        }

        void SetParamsOnMaterialInstance()
        {
            CacheComponents();
            if (!_graphic) return;
            if (!_rt) _rt = GetComponent<RectTransform>();
            if (!_canvas) _canvas = _graphic.canvas;
            if (!_canvas) return;

            var mat = _graphic.materialForRendering; // per-instance instance used by UI
            if (!mat) return;

            var canvasH = Mathf.Max(1f, _canvas.pixelRect.height);
            var canvasW = Mathf.Max(1f, _canvas.pixelRect.width);

            // Rect in pixels
            var size = _rt.rect.size; // px
            // Normalize by canvas height, then half-size for SDF space
            var halfSize = new Vector2((size.x * 0.5f) / canvasH, (size.y * 0.5f) / canvasH);
            var radius = Mathf.Max(0f, cornerRadiusPx) / canvasH;
            var border = Mathf.Max(0f, borderWidthPx) / canvasH;
            var rectUVOffset = new Vector2(_rt.anchoredPosition.x / canvasW, _rt.anchoredPosition.y / canvasH);

            _bgRT = UICaptureEffectManager.Instance?.GetRenderTexture(backgroundRTName);
            _blurRT = UICaptureEffectManager.Instance?.GetBlurRenderTexture(backgroundRTName);
            KW_WITHOUT_BG = new LocalKeyword(mat.shader, "WITHOUT_UI_BG");
            if (!_bgRT || !_blurRT)
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
    }
}