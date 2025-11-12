using UnityEngine;
using URP;

public class UICaptureEffectManager: MonoBehaviour
{
    private static UICaptureEffectManager _instance;
    public static UICaptureEffectManager Instance
    {
        get
        {
            if (_instance == null)
            {
                var go = GameObject.Find("UICaptureEffectManager");
                if (go != null) 
                    _instance = go?.GetComponent<UICaptureEffectManager>();
            }

            return _instance;
        }
    }

    [SerializeField]
    private UICaptureComposePerLayerFeature captureFeature;

    public bool IsUseBgGaussianBlur
    {
        get
        {
            return captureFeature.IsUseBgGaussianBlur;
        }
    }
}
