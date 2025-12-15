using UnityEngine;
using Unlink.LiquidGlassUI;

namespace Unlink.LiquidGlassUI
{
    public static class LiquidGlassSettingsLoader
    {
        const string DefaultPath = "DefaultSettings/LiquidGlassSettings_Default";

        public static LiquidGlassSettings LoadDefault()
            => Resources.Load<LiquidGlassSettings>(DefaultPath);
    }
}