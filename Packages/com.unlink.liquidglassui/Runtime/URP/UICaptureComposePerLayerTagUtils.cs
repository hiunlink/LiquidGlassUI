using System.Collections.Generic;

namespace Unlink.LiquidGlassUI
{
    internal static class UICaptureComposePerLayerTagUtils
    {
        private static Dictionary<string, string> _dsStringMap = new (); 
        private static Dictionary<string, string> _blurStringMap = new ();
        private static Dictionary<int, string> _alphaOnlyTagMap = new();
        private static Dictionary<int, string> _fullTagMap = new();
        private static Dictionary<int, string> _mipsTagMap = new();
        private static Dictionary<int, string> _gaussianTagMap = new();
        private static Dictionary<int, string> _stencilPrepassTagMap = new();
        private static Dictionary<int, string> _drawDefaultTagMap = new();
        private static Dictionary<string, string> _compositeTagMap = new(); 
        
        public static string GetDsString(string textureName)
        {
            if (!_dsStringMap.ContainsKey(textureName))
                _dsStringMap[textureName] = textureName + "_DS";    
            return _dsStringMap[textureName];
            
        }
        public static string GetBlurString(string textureName)
        {
            if (!_blurStringMap.ContainsKey(textureName))
                _blurStringMap[textureName] = textureName + "_BLUR";    
            return _blurStringMap[textureName];
        }
        public static string GetAlphaOnlyTag(int layer)
        {
            if (!_alphaOnlyTagMap.ContainsKey(layer))
                _alphaOnlyTagMap[layer] = $"F.FG_AlphaOnly[{layer}]";
            return _alphaOnlyTagMap[layer];
        }
        public static string GetFullTag(int layer)
        {
            if (!_fullTagMap.ContainsKey(layer))
                _fullTagMap[layer] = $"F.FG_Full[{layer}]";
            return _fullTagMap[layer];
        }

        public static string GetGaussianTag(int layer)
        {
            if (!_gaussianTagMap.ContainsKey(layer))
                _gaussianTagMap[layer] = $"B[{layer}].Gaussian";
            return _gaussianTagMap[layer];
        }
        public static string GetMipsTag(int layer)
        {
            if (!_mipsTagMap.ContainsKey(layer))
                _mipsTagMap[layer] = $"B[{layer}].MipChain";
            return _mipsTagMap[layer];
        }

        public static string GetStencilPrepassTag(int layer, bool drawOpaque)
        {
            var index = layer * 10 + (drawOpaque ? 1 : 0);
            if (!_stencilPrepassTagMap.ContainsKey(index))
                _stencilPrepassTagMap[index] = $"S.FG_StencilPrepass[{layer}] (drawOpaque={drawOpaque})";
            return _stencilPrepassTagMap[index];
        }
        public static string GetDrawDefaultTag(int layer, bool useStencil)
        {
            var index = layer * 10 + (useStencil ? 1 : 0);
            if (!_drawDefaultTagMap.ContainsKey(index))
                _drawDefaultTagMap[index] = $"D.DrawDefault[{layer}]→base(NotEqual1={useStencil})";
            return _drawDefaultTagMap[index];
        }

        public static string GetCompositeTag(string fromRT, string toRT)
        {
            if (!_compositeTagMap.ContainsKey(fromRT))
                _compositeTagMap[fromRT] = $"Composite {fromRT} -> {toRT}";
            return _compositeTagMap[fromRT];
        }
    }
}