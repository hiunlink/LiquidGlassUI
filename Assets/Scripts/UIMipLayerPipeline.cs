using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

[DefaultExecutionOrder(-500)]
public class UIMipLayerPipeline : MonoBehaviour
{
    [System.Serializable]
    public class LayerConfig
    {
        [Tooltip("该层渲染的 UI LayerMask")]
        public LayerMask layerMask;

        [Tooltip("基底来源层（-1=清屏）")]
        public int baseLayerIndex = -1;

        [Header("是否从 base 读取模糊（读取 base 的 mip）")]
        public bool applyBlur = false;

        [Tooltip("读取 base RT 的 mip 等级（可小数）")]
        [Range(0, 8)] public float blurMip = 2.0f;

        [Tooltip("全局纹理名（空= _UI_RT_{i}）")]
        public string globalTextureNameOverride = "";
    }

    public static UIMipLayerPipeline I { get; private set; }
    // 成员引用（给到 Inspector）
    public UIMaskPhaseController maskPhase;


    [Header("单 Canvas（Screen Space - Camera）")]
    public Canvas targetCanvas;

    [Header("主相机")]
    public Camera mainCamera;

    [Header("离屏捕获相机（禁用 enabled）")]
    public Camera captureCamera;

    [Header("层配置（按顺序 0..n-1）")]
    public LayerConfig[] layers;

    [Header("分辨率缩放")]
    [Range(0.25f, 1f)] public float resolutionScale = 1f;

    [Header("首层清屏颜色（base=-1 用）")]
    public Color clearColor = new Color(0, 0, 0, 0);

    [Header("只在脏时更新（跑通后再开）")]
    public bool captureOnDirtyOnly = false;

    // —— 背景挖空（多 mask） —— //
    [Header("背景挖空（在生成 MIP 之前）")]
    public bool enableBgPunchByMasks = true;

    [Tooltip("背景层 index（通常 0）")]
    public int bgLayerIndex = 0;

    [Tooltip("作为挖空遮罩的层 index 列表（例如 人物=1；可加多个）")]
    public List<int> maskLayerIndices = new List<int>() { 1 };

    [Range(0, 1)] public float maskCutoff = 0.5f;

    // 运行时
    private RenderTexture[] _rt;
    private bool[] _dirty;
    private bool _requestThisFrame;

    // Blit & Punch
    [SerializeField] Shader blitMipShader;   // Hidden/BlitMipLerp（已修 Properties & UV）
    [SerializeField] Shader punchShader;     // Hidden/UIPunchOutByMask
    private Material _blitMipMat;
    private Material _punchMat;
    private static readonly int _MipLevelID = Shader.PropertyToID("_MipLevel");
    private static readonly int _SrcTexID   = Shader.PropertyToID("_SrcTex");
    private static readonly int _MaskTexID  = Shader.PropertyToID("_MaskTex");
    private static readonly int _CutoffID   = Shader.PropertyToID("_Cutoff");

    private RenderTexture _tmpBG;

    void Awake()
    {
        if (I && I != this) { Destroy(gameObject); return; }
        I = this;

        if (!targetCanvas) targetCanvas = FindObjectOfType<Canvas>();
        if (!mainCamera)   mainCamera   = Camera.main;

        if (!captureCamera)
        {
            var go = new GameObject("UICaptureCam");
            go.transform.SetParent(transform, false);
            captureCamera = go.AddComponent<Camera>();
            captureCamera.clearFlags = CameraClearFlags.SolidColor;
            captureCamera.backgroundColor = Color.clear;
            captureCamera.orthographic = true;
        }
        captureCamera.enabled = false;

        if (!blitMipShader) blitMipShader = Shader.Find("Hidden/BlitMipLerp");
        if (!punchShader)    punchShader  = Shader.Find("Hidden/UIPunchOutByMask");
        _blitMipMat = new Material(blitMipShader);
        _punchMat   = new Material(punchShader);

        EnsureAlloc();
    }

    void OnDestroy()
    {
        ReleaseAll();
        if (_blitMipMat) Destroy(_blitMipMat);
        if (_punchMat)   Destroy(_punchMat);
        if (I == this) I = null;
    }

    void ReleaseAll()
    {
        if (_rt != null)
        {
            for (int i = 0; i < _rt.Length; i++)
                if (_rt[i]) _rt[i].Release();
        }
        if (_tmpBG) _tmpBG.Release();
    }

    void EnsureAlloc()
    {
        int n = Mathf.Max(0, layers != null ? layers.Length : 0);
        if (_rt == null || _rt.Length != n)
        {
            ReleaseAll();
            _rt    = new RenderTexture[n];
            _dirty = new bool[n];
            for (int i = 0; i < n; i++) _dirty[i] = true;
        }

        int w = Mathf.Max(1, Mathf.RoundToInt(Screen.width  * resolutionScale));
        int h = Mathf.Max(1, Mathf.RoundToInt(Screen.height * resolutionScale));

        for (int i = 0; i < n; i++)
        {
            bool isBG = (i == bgLayerIndex);

            if (_rt[i] && _rt[i].width == w && _rt[i].height == h) continue;

            if (_rt[i]) _rt[i].Release();
            _rt[i] = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32)
            {
                name = $"_UI_RT_{i}",
                useMipMap = true,
                // ✅ 修复点：背景层不自动生成 MIP，其他层自动
                autoGenerateMips = !isBG,
                filterMode = FilterMode.Bilinear
            };
        }

        // 临时 BG（与 BG 同尺寸）
        if (_tmpBG && (_tmpBG.width != w || _tmpBG.height != h))
        {
            _tmpBG.Release(); _tmpBG = null;
        }
        if (!_tmpBG)
        {
            _tmpBG = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32)
            {
                name = "_UI_BG_TMP",
                useMipMap = false,
                autoGenerateMips = false,
                filterMode = FilterMode.Bilinear
            };
        }
    }

    string GetGlobalTexName(int i)
    {
        var custom = layers[i]?.globalTextureNameOverride;
        return string.IsNullOrEmpty(custom) ? $"_UI_RT_{i}" : custom;
    }

    void BindGlobals()
    {
        if (_rt == null) return;
        for (int i = 0; i < _rt.Length; i++)
        {
            if (_rt[i]) Shader.SetGlobalTexture(Shader.PropertyToID(GetGlobalTexName(i)), _rt[i]);
            Shader.SetGlobalFloat(Shader.PropertyToID($"_UI_RT_HAS_{i}"), _rt[i] ? 1f : 0f);
        }
    }

    public void RequestCapture() => _requestThisFrame = true;

    public void MarkLayerDirty(int i)
    {
        if (_dirty == null) return;
        for (int k = i; k < _dirty.Length; k++) _dirty[k] = true;
    }

    public void MarkAllDirty()
    {
        if (_dirty == null) return;
        for (int i = 0; i < _dirty.Length; i++) _dirty[i] = true;
    }

    void LateUpdate()
    {
        EnsureAlloc();
        if (!_requestThisFrame && captureOnDirtyOnly) return;
        if (!targetCanvas || !captureCamera || _rt == null || layers == null)
        { _requestThisFrame = false; return; }

        // 同步 CaptureCamera 尺寸（Scale With Screen Size）
        var scaler = targetCanvas.GetComponent<CanvasScaler>();
        if (scaler != null)
        {
            float refH = scaler.referenceResolution.y;
            captureCamera.orthographicSize = (refH * 0.5f) * resolutionScale;
        }

        BindGlobals();

        // 暂存原 worldCamera
        var origCam = targetCanvas.worldCamera;
        targetCanvas.worldCamera = captureCamera;

        // —— 逐层构建 —— //
        for (int i = 0; i < layers.Length; i++)
        {
            var cfg = layers[i];
            if (cfg == null) continue;
            if (captureOnDirtyOnly && !_dirty[i] && _rt[i]) continue;

            // —— 背景层（bgLayerIndex）使用 Early-Stencil —— //
            if (i == bgLayerIndex && enableBgPunchByMasks && maskPhase != null)
            {
                // 0) 先把本层RT清屏（或拷贝基底），如你已有基底链保持不变
                //    下行只是示例：如果你的LayerConfig有baseLayerIndex/applyBlur，请先照现有逻辑把基底写入 _rt[i]

                // 1) BeginPhase：人物=只写Stencil，背景=读Stencil
                maskPhase.BeginPhase();

                // 2) 人物模板预写（只写Stencil，不写颜色）
                var oldFlags = captureCamera.clearFlags;
                var oldMask  = captureCamera.cullingMask;

                captureCamera.clearFlags    = CameraClearFlags.Nothing;
                captureCamera.cullingMask   = maskPhase.characterLayer;
                captureCamera.targetTexture = _rt[i];
                captureCamera.Render();
                captureCamera.targetTexture = null;

                // 3) 背景渲染（材质已被注入 Stencil: NotEqual 1 → Early-Stencil 生效）
                captureCamera.clearFlags    = CameraClearFlags.Nothing;
                captureCamera.cullingMask   = maskPhase.backgroundLayer;
                captureCamera.targetTexture = _rt[i];
                captureCamera.Render();
                captureCamera.targetTexture = null;

                // 还原
                captureCamera.clearFlags  = oldFlags;
                captureCamera.cullingMask = oldMask;

                // 4) EndPhase：恢复所有 UI 材质
                maskPhase.EndPhase();

                // 5) 背景手动生成 MIP（autoGenerateMips=false）
                _rt[i].GenerateMips();

                // 6) 推全局 & 标记完成
                Shader.SetGlobalTexture(Shader.PropertyToID(GetGlobalTexName(i)), _rt[i]);
                Shader.SetGlobalFloat(Shader.PropertyToID($"_UI_RT_HAS_{i}"), 1f);
                _dirty[i] = false;

                // 背景已完成，继续进入下一层
                continue;
            }
            
            // 1) 基底（来自 base 或清屏）
            if (cfg.baseLayerIndex >= 0 && cfg.baseLayerIndex < i && _rt[cfg.baseLayerIndex] != null)
            {
                if (cfg.applyBlur)
                {
                    _blitMipMat.SetFloat(_MipLevelID, cfg.blurMip);
                    Graphics.Blit(_rt[cfg.baseLayerIndex], _rt[i], _blitMipMat);
                }
                else
                {
                    Graphics.Blit(_rt[cfg.baseLayerIndex], _rt[i]);
                }
            }
            else
            {
                var prev = RenderTexture.active;
                RenderTexture.active = _rt[i];
                GL.Clear(true, true, clearColor);
                RenderTexture.active = prev;
            }

            // 2) 渲该层 UI（不清屏叠加）d
            var oldFlags1 = captureCamera.clearFlags;
            var oldMask1  = captureCamera.cullingMask;

            captureCamera.clearFlags   = CameraClearFlags.Nothing;
            captureCamera.cullingMask  = cfg.layerMask;
            captureCamera.targetTexture = _rt[i];
            captureCamera.Render();
            captureCamera.targetTexture = null;

            captureCamera.clearFlags  = oldFlags1;
            captureCamera.cullingMask = oldMask1;

            // 3) 生成 MIP（背景层延后，其他层立刻）
            // bool isBG = (i == bgLayerIndex);
            // if (!isBG) _rt[i].GenerateMips();

            // 4) 推全局 & 标记
            Shader.SetGlobalTexture(Shader.PropertyToID(GetGlobalTexName(i)), _rt[i]);
            Shader.SetGlobalFloat(Shader.PropertyToID($"_UI_RT_HAS_{i}"), 1f);
            _dirty[i] = false;
        }

        // 还原
        targetCanvas.worldCamera = origCam;
        _requestThisFrame = false;
    }

    public RenderTexture GetRT(int index) =>
        (index >= 0 && index < (_rt?.Length ?? 0)) ? _rt[index] : null;

    public bool HasRT(int index) =>
        (index >= 0 && index < (_rt?.Length ?? 0)) && _rt[index] != null;
}
