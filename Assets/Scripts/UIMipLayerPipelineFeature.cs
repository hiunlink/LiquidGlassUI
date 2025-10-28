using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering;

public class UIMipLayerPipelineFeature : ScriptableRendererFeature
{
    class RequestPass : ScriptableRenderPass
    {
        public RequestPass(){ renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing; }
        public override void Execute(ScriptableRenderContext ctx, ref RenderingData data)
        {
            if (data.cameraData.cameraType != CameraType.Game) return;
            if (UIMipLayerPipeline.I != null) UIMipLayerPipeline.I.RequestCapture();
        }
    }

    RequestPass pass;
    public override void Create(){ pass = new RequestPass(); }
    public override void AddRenderPasses(ScriptableRenderer r, ref RenderingData d){ r.EnqueuePass(pass); }
}