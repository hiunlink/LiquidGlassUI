using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Unlink.LiquidGlassUI
{
    public class UICaptureSceneFeature:ScriptableRendererFeature
    {
        public UICaptureComposePerLayerFeature captureFeature;
        private CameraData _gameViewCameraData = new ();
        private int _viewportScaleOffsetId, _sceneWidthScaleId;
        public override void Create()
        {
            _viewportScaleOffsetId = Shader.PropertyToID("_UIBG_ViewportScaleOffset");
            _sceneWidthScaleId = Shader.PropertyToID("_SceneScale");
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            // 简易的 GameView屏幕uv 转 SceneView屏幕uv (orthographic) 
            if (renderingData.cameraData.isSceneViewCamera)
            {

                _gameViewCameraData = captureFeature.cameraData;
                if (_gameViewCameraData.camera == null)
                    return;

                var sceneCam = renderingData.cameraData.camera;
                var scenePos = sceneCam.transform.position;
                var gamePos = _gameViewCameraData.camera.transform.position;
                var posOffset = scenePos - gamePos;
                var sceneOrthoSize = sceneCam.orthographicSize;
                var gameOrthoSize = _gameViewCameraData.camera.orthographicSize;
                
                // 当前相机的目标RT尺寸（URP 用 cameraTargetDescriptor 最稳）
                var desc = renderingData.cameraData.cameraTargetDescriptor;
                float rtW = desc.width;
                float rtH = desc.height;
                var gameDesc = _gameViewCameraData.cameraTargetDescriptor;
                var widthScale =  (rtW / rtH) / ((float)gameDesc.width / gameDesc.height) ;
                var heightScale = 1;

                var orthoScale = sceneOrthoSize / gameOrthoSize;
                var scale  = new Vector2(widthScale, heightScale) * orthoScale;
                var uvOffset = scale - Vector2.one;
                var offset = new Vector2(posOffset.x / (gameOrthoSize * 2 * (rtW / rtH) / widthScale), posOffset.y / (gameOrthoSize * 2)) ;
                offset -= uvOffset * 0.5f;
                Shader.SetGlobalVector(_viewportScaleOffsetId, new Vector4(scale.x, scale.y, offset.x, offset.y));
                Shader.SetGlobalFloat(_sceneWidthScaleId, widthScale);
            }
        }
    }
}