using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class UIBackgroundComposeReplaceFeature : ScriptableRendererFeature
{
    [System.Serializable]
    public class Settings
    {
        public RenderPassEvent passEvent = RenderPassEvent.BeforeRenderingTransparents;
        [Tooltip("使用哪一层的 UI RT（_UI_RT_{index}）")]
        public int layerIndex = 0;
        [Range(0f, 8f)] public float mipLevel = 2.0f;
        public CameraType filterCameraType = CameraType.Game;
        public Shader composeShader; // 指向 Hidden/UIBackgroundCompose_Replace
        public bool silentIfMissing = true;
    }

    class ComposePass : ScriptableRenderPass
    {
        Settings settings;
        Material mat;
        static readonly int _BlurTex  = Shader.PropertyToID("_BlurTex");
        static readonly int _MipLevel = Shader.PropertyToID("_MipLevel");

        public ComposePass(Settings s){ settings = s; renderPassEvent = s.passEvent; }
        public void Setup(Material m){ mat = m; }

        public override void Execute(ScriptableRenderContext ctx, ref RenderingData data)
        {
            if (data.cameraData.cameraType != settings.filterCameraType) return;
            if (mat == null) return;

            string globalName = $"_UI_RT_{settings.layerIndex}";
            Texture blurRT = Shader.GetGlobalTexture(globalName);
            if (blurRT == null)
            {
                if (!settings.silentIfMissing)
                    Debug.LogWarning($"[UIBG Replace] Missing global RT: {globalName}");
                return;
            }

            mat.SetTexture(_BlurTex, blurRT);
            mat.SetFloat(_MipLevel, settings.mipLevel);

            var cmd = CommandBufferPool.Get("UIBG Replace");
            var dst = data.cameraData.renderer.cameraColorTargetHandle;

            // 直接覆盖写回相机颜色：源把 dst 自己传进去即可（我们不采样源）
            Blitter.BlitCameraTexture(cmd, dst, dst, mat, 0);

            ctx.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }
    }

    public Settings settings = new Settings();
    ComposePass pass;
    Material mat;

    public override void Create()
    {
        if (settings.composeShader == null)
            settings.composeShader = Shader.Find("Hidden/UIBackgroundCompose_Replace");
        if (settings.composeShader != null)
            mat = CoreUtils.CreateEngineMaterial(settings.composeShader);

        pass = new ComposePass(settings);
        pass.Setup(mat);
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (mat != null) renderer.EnqueuePass(pass);
    }

    protected override void Dispose(bool disposing)
    {
        CoreUtils.Destroy(mat);
    }
}
