using UnityEngine;

[DefaultExecutionOrder(-500)]  // 先于大多数脚本初始化
public class UICaptureManager : MonoBehaviour
{
    public static UICaptureManager Instance { get; private set; }

    [Header("目标 Canvas（Screen Space - Camera）")]
    public Canvas targetCanvas;

    [Header("主相机（渲染 UI/场景的那台）")]
    public Camera mainCamera;

    [Header("捕获相机（只渲染 UIBackground 层）")]
    public Camera captureCamera;

    [Header("要捕获的层（例：UIBackground）")]
    public LayerMask captureLayer;

    [Header("输出分辨率缩放（1=原分辨率）")]
    [Range(0.25f, 1f)]
    public float resolutionScale = 1f;

    [Header("仅当 UI 改变时才截屏（可选）")]
    public bool captureOnDirtyOnly = false;

    private RenderTexture _rt;
    private bool _requestCapture;
    private bool _dirty = true; // 若启用captureOnDirtyOnly，外部可标记为脏

    private int _hashW, _hashH;

    private static readonly int _UIBackgroundRT_ID = Shader.PropertyToID("_UIBackgroundRT");
    public RenderTexture CapturedRT => _rt;
    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        if (!targetCanvas) targetCanvas = FindObjectOfType<Canvas>();
        if (!mainCamera)   mainCamera   = Camera.main;

        if (!captureCamera)
        {
            // 自动创建一台捕获相机（也可以手动在场景里放好）
            var go = new GameObject("UICaptureCamera");
            go.transform.SetParent(transform, false);
            captureCamera = go.AddComponent<Camera>();
            captureCamera.enabled = false;                // 不参与主循环
            captureCamera.clearFlags = CameraClearFlags.SolidColor;
            captureCamera.backgroundColor = Color.clear;
            captureCamera.orthographic = true;
            captureCamera.orthographicSize = 5f;
        }

        // 捕获层与相机剔除层设置
        captureCamera.cullingMask = captureLayer;

        EnsureRenderTexture();
        Shader.SetGlobalTexture(_UIBackgroundRT_ID, _rt);

        // 基础校验
        if (targetCanvas && targetCanvas.renderMode != RenderMode.ScreenSpaceCamera)
        {
            Debug.LogWarning($"[UICaptureManager] {targetCanvas.name} 建议设为 Screen Space - Camera。当前：{targetCanvas.renderMode}");
        }
    }

    private void OnDestroy()
    {
        if (_rt) _rt.Release();
        if (Instance == this) Instance = null;
    }

    private void EnsureRenderTexture()
    {
        int w = Mathf.Max(1, Mathf.RoundToInt(Screen.width  * resolutionScale));
        int h = Mathf.Max(1, Mathf.RoundToInt(Screen.height * resolutionScale));

        if (_rt != null && (_hashW == w && _hashH == h)) return;

        if (_rt) _rt.Release();
        _rt = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32)
        {
            name = "_UIBackgroundRT",
            useMipMap = false,
            autoGenerateMips = false
        };
        _hashW = w; _hashH = h;

        Shader.SetGlobalTexture(_UIBackgroundRT_ID, _rt);
        _dirty = true;
    }

    /// <summary>由 RenderFeature 的 Pass 发起本帧捕获请求。</summary>
    public void RequestCapture()
    {
        _requestCapture = true;
    }

    /// <summary>外部 UI 状态改变时可手动标脏，减少无效截屏。</summary>
    public void MarkDirty()
    {
        _dirty = true;
    }

    private void OnEnable()
    {
        UnityEngine.SceneManagement.SceneManager.activeSceneChanged += OnSceneChanged;
    }
    private void OnDisable()
    {
        UnityEngine.SceneManagement.SceneManager.activeSceneChanged -= OnSceneChanged;
    }
    private void OnSceneChanged(UnityEngine.SceneManagement.Scene oldS, UnityEngine.SceneManagement.Scene newS)
    {
        // 场景切换后更新RT
        EnsureRenderTexture();
        _dirty = true;
    }

    private void LateUpdate()
    {
        EnsureRenderTexture();

        if (!_requestCapture) return;
        if (captureOnDirtyOnly && !_dirty)
        {
            _requestCapture = false;
            return;
        }

        if (!targetCanvas || !captureCamera) { _requestCapture = false; return; }

        // 记录原 camera
        var originalCam = targetCanvas.worldCamera;

        // 绑定到捕获相机
        targetCanvas.worldCamera = captureCamera;

        // 执行捕获（在 SRP 外部，避免递归渲染报错）
        captureCamera.targetTexture = _rt;
        captureCamera.Render();
        captureCamera.targetTexture = null;

        // 恢复
        targetCanvas.worldCamera = originalCam;

        // 推送到全局 Shader
        Shader.SetGlobalTexture(_UIBackgroundRT_ID, _rt);

        // 收尾
        _requestCapture = false;
        _dirty = false;
    }
}
