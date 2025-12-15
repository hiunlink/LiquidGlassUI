using UnityEngine;

namespace Unlink.LiquidGlassUI
{
    public static class LiquidGlassMaterialProvider
    {
        // 包内建议放这个资源：Resources/DefaultMaterials/UI_LiquidGlass_Default.mat
        private const string DefaultMatPath = "DefaultMaterials/UI_LiquidGlass_Default";

        public static Material LoadDefaultTemplate()
        {
            var mat = Resources.Load<Material>(DefaultMatPath);
            return mat;
        }

        public static Material CreateInstanceOrNull()
        {
            var template = LoadDefaultTemplate();
            if (template == null) return null;
            return new Material(template);
        }
    }
}