using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Unlink.LiquidGlassUI
{
    internal abstract class BasePass : ScriptableRenderPass
    {
        protected readonly string tag;
        protected RTHandle colorRT, depthRT;

        public string Tag
        {
            get
            {
                return tag;
            }
        }

        protected BasePass(string tag, RenderPassEvent evt) { this.tag = tag; renderPassEvent = evt; }

        public void SetupTargets(RTHandle color, RTHandle depth)
        {
            colorRT = color;
            depthRT = depth; // 可为 null
        }
        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData data)
        {
            if (colorRT == null) return;
            if (depthRT != null) ConfigureTarget(colorRT, depthRT);
            else ConfigureTarget(colorRT);
            ConfigureClear(ClearFlag.None, Color.clear);
        }
    }
}
