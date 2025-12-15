using UnityEngine;

namespace Unlink.LiquidGlassUI
{
    [CreateAssetMenu(menuName = "LiquidGlassUI/LiquidGlass Settings", fileName = "LiquidGlassSettings")]
    public class LiquidGlassSettings : ScriptableObject
    {
        [Header("Global Texture Names")]
        public string globalTextureName = "_UI_BG";
        public string uvScaleName = "_UIBG_UVScale";

        [Header("Default Shaders/Materials (Optional)")]
        public Shader liquidGlassShader; // UI_LiquidGlass.shader
        public Material liquidGlassMaterialTemplate; // Resources fallback
        public Material mipCompositeMat;
        public Material gaussianMat;
        public Material gaussianCompositeMat;
        public Material replaceMat;

        [Header("Capture Defaults")]
        [Range(0.25f, 1f)] public float resolutionScale = 1f;
        public bool useHDR = false;
        public Color clearColor = new Color(0, 0, 0, 0);

        [Header("Layers")] 
        public int layerStart = 6;
        public int layerEnd = 10;
        public int hiddenLayer = 3;
    }
}   