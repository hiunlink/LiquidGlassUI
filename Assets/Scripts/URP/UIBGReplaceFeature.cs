using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace URP
{
    public class UIBGReplaceFeature : ScriptableRendererFeature
    {
        private static readonly int ID_MainTex = Shader.PropertyToID("_MainTex");

        [System.Serializable]
        public class Settings
        {
            public string globalTextureName = "_UI_BG";
            public RenderPassEvent injectEvent = RenderPassEvent.BeforeRenderingTransparents;
            public Material replaceMaterial; // Hidden/UIBGReplace
            public string cameraTagFilter = "MainCamera"; // 只对主相机
        }

        class ReplacePass : ScriptableRenderPass
        {
            private readonly Settings _s;
            private readonly int _srcID;
            private readonly string _tag;
            private readonly ProfilingSampler _profilingSampler;

            public ReplacePass(Settings s)
            {
                this._s = s;
                _tag = "UIBG Replace";
                renderPassEvent = s.injectEvent - 1;
                _srcID = Shader.PropertyToID(s.globalTextureName);
                
                _profilingSampler = new ProfilingSampler(_tag);
            }

            public override void Execute(ScriptableRenderContext ctx, ref RenderingData data)
            {
                var cam = data.cameraData.camera;
                if (!string.IsNullOrEmpty(_s.cameraTagFilter) && !cam.CompareTag(_s.cameraTagFilter)) return;
                if (_s.replaceMaterial == null) return;

                var src = Shader.GetGlobalTexture(_srcID);
                if (src == null) return;

                var cmd = CommandBufferPool.Get(_tag);
                using (new ProfilingScope(cmd, _profilingSampler))
                {
                    var dstId = data.cameraData.renderer.cameraColorTargetHandle;
                    _s.replaceMaterial.SetTexture(ID_MainTex, src);
                    cmd.SetRenderTarget(dstId);
                    cmd.DrawProcedural(Matrix4x4.identity, _s.replaceMaterial, 0, MeshTopology.Triangles, 3, 1);
                }

                ctx.ExecuteCommandBuffer(cmd);
                CommandBufferPool.Release(cmd);
            }
        }


        public Settings settings = new Settings();
        ReplacePass _pass;

        public override void Create()
        {
            _pass = new ReplacePass(settings);
        }

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
}