using UICaptureCompose.URP;
using UnityEngine;

namespace UICaptureCompose
{
    public class UICaptureEffectManager: MonoBehaviour
    {
        private static UICaptureEffectManager _instance;
        public static UICaptureEffectManager Instance
        {
            get
            {
                if (!_instance)
                {
                    var go = GameObject.Find("UICaptureEffectManager");
                    if (go) 
                        _instance = go?.GetComponent<UICaptureEffectManager>();
                }

                return _instance;
            }
        }

        [SerializeField]
        private UICaptureComposePerLayerFeature captureFeature;

        public RenderTexture GetRenderTexture(string textureName)
        {
            return captureFeature.GetRenderTexture(textureName);
        }

        public RenderTexture GetBlurRenderTexture(string textureName)
        {
            return captureFeature.GetBlurRenderTexture(textureName);
        }
    }
}
