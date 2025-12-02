using System.Collections.Generic;
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
                if (_instance) return _instance;
                var go = GameObject.Find("UICaptureEffectManager");
                if (go) 
                    _instance = go?.GetComponent<UICaptureEffectManager>();

                return _instance;
            }
        }

        [SerializeField]
        private UICaptureComposePerLayerFeature captureFeature;

        [SerializeField] 
        private UIBGReplaceFeature replaceFeature;

        #region Public

        public RenderTexture GetRenderTexture(string textureName)
        {
            return captureFeature?.GetRenderTexture(textureName);
        }

        public RenderTexture GetBlurRenderTexture(string textureName)
        {
            return captureFeature?.GetBlurRenderTexture(textureName);
        }

        public void SetDirty(LayerMask layer, bool isDirty)
        {
            captureFeature?.SetDirty(layer, isDirty);
        }

        public void SetupLayerConfigs(List<UICaptureComposePerLayerFeature.LayerConfig> layerConfigs)
        {
            captureFeature?.SetupLayerConfigs(layerConfigs);
        }

        public void SetReplaceRtName(string rtName)
        {
            if (replaceFeature)
            {
                replaceFeature.settings.globalTextureName = rtName;
                replaceFeature.UpdateSettings();
            }
        }
        
        #endregion
                
    }
}
