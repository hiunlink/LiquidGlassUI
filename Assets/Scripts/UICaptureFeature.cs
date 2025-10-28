using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class UICaptureFeature : ScriptableRendererFeature
{
    class RequestCapturePass : ScriptableRenderPass
    {
        public RequestCapturePass() {
            renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
        }
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (renderingData.cameraData.cameraType != CameraType.Game) return;
            if (UICaptureManager.Instance == null) return;
            UICaptureManager.Instance.RequestCapture(); // 仅发请求，不在 SRP 内 Render
        }
    }

    class BlurPass : ScriptableRenderPass
    {
        static readonly int _UIBG_Down_ID   = Shader.PropertyToID("_UIBG_DownQuarter");
        static readonly int _UIBG_Ping_ID   = Shader.PropertyToID("_UIBG_Ping");
        static readonly int _UIBG_Pong_ID   = Shader.PropertyToID("_UIBG_Pong");
        static readonly int _UIBG_BlurRT_ID = Shader.PropertyToID("_UIBackgroundBlurRT");

        // shader 参数 ID
        static readonly int _TexelSize_ID    = Shader.PropertyToID("_TexelSize");
        static readonly int _Mode_ID         = Shader.PropertyToID("_Mode");
        static readonly int _KawaseRadius_ID = Shader.PropertyToID("_KawaseRadius");
        static readonly int _SourceTex_ID    = Shader.PropertyToID("_SourceTex");

        Material _mat;

        public BlurPass(Shader kawaseShader)
        {
            renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
            _mat = CoreUtils.CreateEngineMaterial(kawaseShader);
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (UICaptureManager.Instance == null) return;
            var srcRT = UICaptureManager.Instance.CapturedRT;
            if (srcRT == null) return;

            var cmd = CommandBufferPool.Get("UIBackground Kawase Blur 2A");

            // -----------------------
            // 1. Downsample to 1/4 RT
            // -----------------------
            int dw = Mathf.Max(1, srcRT.width  / 4);
            int dh = Mathf.Max(1, srcRT.height / 4);

            cmd.GetTemporaryRT(_UIBG_Down_ID, dw, dh, 0, FilterMode.Bilinear, RenderTextureFormat.ARGB32);
            cmd.GetTemporaryRT(_UIBG_Ping_ID, dw, dh, 0, FilterMode.Bilinear, RenderTextureFormat.ARGB32);
            cmd.GetTemporaryRT(_UIBG_Pong_ID, dw, dh, 0, FilterMode.Bilinear, RenderTextureFormat.ARGB32);

            // 直接采样源 RT（缩小）
            _mat.SetInt(_Mode_ID, 0);
            _mat.SetVector(_TexelSize_ID, new Vector2(1f/dw, 1f/dh));
            _mat.SetTexture(_SourceTex_ID, srcRT);
            cmd.Blit(BuiltinRenderTextureType.None, _UIBG_Down_ID, _mat);

            // -----------------------
            // 2. Kawase blur pass #1
            // -----------------------
            _mat.SetInt(_Mode_ID, 1);
            _mat.SetFloat(_KawaseRadius_ID, 1.5f);
            cmd.Blit(_UIBG_Down_ID, _UIBG_Ping_ID, _mat);

            // -----------------------
            // 3. Kawase blur pass #2
            // -----------------------
            _mat.SetFloat(_KawaseRadius_ID, 2.5f);
            cmd.Blit(_UIBG_Ping_ID, _UIBG_Pong_ID, _mat);

            // -----------------------
            // 4. 推送到全局 Shader
            // -----------------------
            cmd.SetGlobalTexture(_UIBG_BlurRT_ID, _UIBG_Pong_ID);

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);

            // 释放临时纹理
            var release = CommandBufferPool.Get("UIBG Blur Release");
            release.ReleaseTemporaryRT(_UIBG_Down_ID);
            release.ReleaseTemporaryRT(_UIBG_Ping_ID);
            release.ReleaseTemporaryRT(_UIBG_Pong_ID);
            context.ExecuteCommandBuffer(release);
            CommandBufferPool.Release(release);
        }
    }

    [SerializeField] Shader kawaseShader; // 指到 Hidden/KawaseBlur
    RequestCapturePass _request;
    BlurPass _blur;

    public override void Create()
    {
        if (kawaseShader == null) kawaseShader = Shader.Find("Hidden/KawaseBlur");
        _request = new RequestCapturePass();
        _blur = new BlurPass(kawaseShader);
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        renderer.EnqueuePass(_request);
        renderer.EnqueuePass(_blur);
    }
}
