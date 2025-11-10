using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace URP.Passes
{
    /// <summary>
    /// MIP 链模糊合成（不负责清屏 & 不负责采集层）。
    /// 输入：srcRT（要求外部已渲染好该层的内容；如需 MIP，请外部生成，或启用本类的可选 GenerateMips）。
    /// 输出：将 srcRT 指定 MIP 以覆盖(Override)方式合成到 baseCol（Stencil 通过材质 pass 切换）。
    ///
    /// 材质约定（_compositeMat = Hidden/UIBGCompositeStencilMip）：
    ///   - Pass 0：无模板
    ///   - Pass 1：模板 NotEqual（Ref = stencilVal）
    ///   - 需要属性：_SourceTex (Texture)、_Mip (float)
    /// </summary>
    public class MipChainBlurPass : ScriptableRenderPass
    {
        readonly string _tagComposite;

        RTHandle _srcRT;     // 来源（含 MIP）
        RTHandle _baseCol;   // 合成到此
        RTHandle _baseDS;    // 模板缓冲（用于合成材质内 Stencil）
        Material _compositeMat;
        float _mipLevel;

        bool _useStencilNotEqual;
        int  _stencilVal;

        // 可选：是否在此 pass 内对 srcRT 生成 MIP（默认 false，推荐外部统一做）
        bool _generateMipsHere;

        static readonly int _SrcTex = Shader.PropertyToID("_SourceTex");
        static readonly int _Mip    = Shader.PropertyToID("_Mip");
        static readonly int _StencilRef = Shader.PropertyToID("_StencilRef"); // 你的材质如需要，可用

        public MipChainBlurPass(
            string tag,
            RenderPassEvent injectEvent
            )
        {
            renderPassEvent   = injectEvent;
            _tagComposite     = string.IsNullOrEmpty(tag) ? "MipChainBlur.Composite" : tag;
        }

        public void Setup(
            RTHandle srcRT,
            RTHandle baseCol,
            RTHandle baseDS
        ) {
            _srcRT            = srcRT;
            _baseCol          = baseCol;
            _baseDS           = baseDS;
        }

        public void SetSharedMaterial(Material compositeMat)
        {
            _compositeMat     = new Material(compositeMat);
        }

        public void SetParams(
            float mipLevel,
            bool useStencilNotEqual,
            int stencilVal,
            bool generateMipsHere = false
            )
        {
            _mipLevel         = Mathf.Max(0f, mipLevel);

            _useStencilNotEqual = useStencilNotEqual;
            _stencilVal         = Mathf.Clamp(stencilVal, 0, 255);

            _generateMipsHere   = generateMipsHere;
        }

        public override void Execute(ScriptableRenderContext ctx, ref RenderingData data)
        {
            if (_srcRT == null || _baseCol == null || _compositeMat == null) return;

            // 可选：在此生成 MIPs（通常建议在 Feature 统一生成一次）
            if (_generateMipsHere)
            {
                var g = CommandBufferPool.Get(_tagComposite + ".GenMips");
                g.GenerateMips(_srcRT);
                ctx.ExecuteCommandBuffer(g);
                CommandBufferPool.Release(g);
            }

            // 合成（覆盖）：srcRT 的指定 MIP → baseCol
            int passIndex = _useStencilNotEqual ? 1 : 0;

            var cmd = CommandBufferPool.Get(_tagComposite);
            cmd.SetRenderTarget(_baseCol, _baseDS);
            _compositeMat.SetTexture(_SrcTex, _srcRT);
            _compositeMat.SetFloat(_Mip, _mipLevel);
            // 如果你的合成材质需要 stencil 参数，可传入（否则注释掉）
            if (_compositeMat.HasProperty(_StencilRef))
                _compositeMat.SetInt(_StencilRef, _stencilVal);

            // 画全屏三角形，Shader 内部使用 Blend One Zero（Override）、Stencil 由 pass 决定
            cmd.DrawProcedural(Matrix4x4.identity, _compositeMat, passIndex, MeshTopology.Triangles, 3, 1);
            ctx.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }
    }
}
