using UnityEngine;

public class UICaptureRuntimeBinder : MonoBehaviour
{
    public static Canvas SceneCanvas;
    public static Camera MainCamera;
    public static Camera CaptureCamera;

    public Camera UICaptureCamera;
    void Awake()
    {
        SceneCanvas = GetComponentInChildren<Canvas>(); // 或 SerializeField
        MainCamera = Camera.main;
        CaptureCamera = UICaptureCamera;
    }
}