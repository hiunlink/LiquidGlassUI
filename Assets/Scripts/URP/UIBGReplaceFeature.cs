using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class UIBGReplaceFeature : ScriptableRendererFeature
{
    [System.Serializable] public class Settings
    {
        public string globalTextureName = "_UI_BG";
        public RenderPassEvent injectEvent = RenderPassEvent.BeforeRenderingTransparents;
        public Material replaceMaterial; // Hidden/UIBGReplace
        public string cameraTagFilter = "MainCamera"; // 只对主相机
    }

    class ReplacePass : ScriptableRenderPass
    {
        Settings s; int _srcID;
        public ReplacePass(Settings s){ this.s=s; renderPassEvent=s.injectEvent-1; _srcID=Shader.PropertyToID(s.globalTextureName); }

        public override void Execute(ScriptableRenderContext ctx, ref RenderingData data)
        {
            var cam = data.cameraData.camera;
            if (!string.IsNullOrEmpty(s.cameraTagFilter) && cam.tag != s.cameraTagFilter) return;
            if (s.replaceMaterial==null) return;

            var src = Shader.GetGlobalTexture(_srcID);
            if (src==null) return;

            var cmd = CommandBufferPool.Get("UIBG Replace");
            using (new ProfilingScope(cmd, new ProfilingSampler("UIBG Replace")))
            {
                var srcId = new RenderTargetIdentifier(src);
                var dstId = data.cameraData.renderer.cameraColorTarget;
                cmd.SetGlobalTexture("_MainTex", src);
// #if UNITY_2022_3_OR_NEWER
//                 Blitter.BlitTexture(cmd, srcId, dstId, s.replaceMaterial, 0);
// #else
                cmd.Blit(srcId, dstId, s.replaceMaterial, 0);
// #endif
            }
            ctx.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }
    }
    

    public Settings settings = new Settings();
    ReplacePass _pass;
    public override void Create(){ _pass = new ReplacePass(settings); }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData data)
    {
        // 忽略 SceneView 相机（避免拉伸或重复渲染）
        if (data.cameraData.isSceneViewCamera)
        {
            // 如果希望 SceneView 中仍能显示 UI，可直接返回，不设置全局贴图
            return;
        }
        if (settings.replaceMaterial) renderer.EnqueuePass(_pass);
    }
}
