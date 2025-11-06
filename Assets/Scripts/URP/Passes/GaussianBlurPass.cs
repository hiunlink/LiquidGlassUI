using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace UIEffects.Passes
{
    /// <summary>
    /// 高斯分离模糊（不负责清屏 & 不负责采集层）。
    /// 输入：srcRT（外部已渲染累积的源）
    /// 过程：H(src→tmp) → V(tmp→dst)（sigma<=tinyThreshold 时跳过 H/V）
    /// 输出：以覆盖(Override)方式将 blurInputTex 合成到 baseCol（Stencil 通过材质 pass 切换）
    ///
    /// 材质约定：
    /// - _gaussianMat = Hidden/UIBG_GaussianSeparable
    ///     pass 0: Horizontal，需要 _SourceTex, _TexelSize(float4), _Sigma
    ///     pass 1: Vertical，同上
    /// - _compositeMat = Hidden/UIBGCompositeCopyStencil
    ///     pass 0: 无模板
    ///     pass 1: 模板 NotEqual（Ref = stencilVal）
    ///     需要 _SourceTex
    /// </summary>
    public class GaussianBlurPass : ScriptableRenderPass
    {
        readonly string _tagH;
        readonly string _tagV;
        readonly string _tagComposite;

        readonly RTHandle _srcRT;    // 输入源
        readonly RTHandle _tmpRT;    // H 输出
        readonly RTHandle _dstRT;    // V 输出（最终输入合成）
        readonly RTHandle _baseCol;  // 合成目标
        readonly RTHandle _baseDS;   // 模板缓冲

        readonly Material _gaussianMat;
        Material _gaussianVertMat;
        Material _gaussianHorMat;
        readonly Material _compositeMat;

        private readonly int _iteration = 4;
        readonly float _sigma;         // 连续可调
        readonly float _tinyThreshold; // 小阈值（<= 则跳过模糊）

        readonly bool _useStencilNotEqual;
        readonly int  _stencilVal;

        static readonly int _SourceTex  = Shader.PropertyToID("_SourceTex");
        static readonly int _TexelSize  = Shader.PropertyToID("_TexelSize");
        static readonly int _Sigma      = Shader.PropertyToID("_Sigma");
        static readonly int _StencilRef = Shader.PropertyToID("_StencilRef");

        public GaussianBlurPass(
            string tagPrefix,
            RenderPassEvent injectEvent,
            RTHandle srcRT,
            RTHandle tmpRT,
            RTHandle dstRT,
            RTHandle baseCol,
            RTHandle baseDS,
            Material gaussianSeparableMat,
            Material compositeCopyMat,
            float sigma,
            float tinyThreshold,
            bool useStencilNotEqual,
            int stencilVal)
        {
            renderPassEvent   = injectEvent;

            _tagH = string.IsNullOrEmpty(tagPrefix) ? "GaussianBlur.H" : tagPrefix + ".H";
            _tagV = string.IsNullOrEmpty(tagPrefix) ? "GaussianBlur.V" : tagPrefix + ".V";
            _tagComposite = string.IsNullOrEmpty(tagPrefix) ? "GaussianBlur.Composite" : tagPrefix + ".Composite";

            _srcRT   = srcRT;
            _tmpRT   = tmpRT;
            _dstRT   = dstRT;
            _baseCol = baseCol;
            _baseDS  = baseDS;

            _gaussianMat  = gaussianSeparableMat;
            _compositeMat = compositeCopyMat;

            _sigma         = Mathf.Max(0f, sigma);
            _tinyThreshold = Mathf.Max(0f, tinyThreshold);

            _useStencilNotEqual = useStencilNotEqual;
            _stencilVal         = Mathf.Clamp(stencilVal, 0, 255);
        }
        

        public override void Execute(ScriptableRenderContext ctx, ref RenderingData data)
        {
            if (_srcRT == null || _baseCol == null || _gaussianMat == null || _compositeMat == null)
                return;
            
            
            int w = _srcRT.rt.width;
            int h = _srcRT.rt.height;

            // 选择要合成的输入贴图：sigma 很小时跳过 H/V
            Texture blurInputTex = _srcRT;
            for (int it = 0; it < _iteration; it++)
            {
                if (_sigma > _tinyThreshold)
                {
                    // H: src → tmp
                    _gaussianHorMat = new Material(_gaussianMat);
                    var cmdH = CommandBufferPool.Get(_tagH);
                    cmdH.SetRenderTarget(_tmpRT);
                    _gaussianHorMat.SetTexture(_SourceTex, blurInputTex);
                    _gaussianHorMat.SetVector(_TexelSize, new Vector4(1.0f / w, 1.0f / h, 0, 0));
                    _gaussianHorMat.SetFloat(_Sigma, _sigma);
                    cmdH.DrawProcedural(Matrix4x4.identity, _gaussianHorMat, 0, MeshTopology.Triangles, 3,
                        1); // pass 0 = H
                    ctx.ExecuteCommandBuffer(cmdH);
                    CommandBufferPool.Release(cmdH);

                    // V: tmp → dst
                    _gaussianVertMat = new Material(_gaussianMat);
                    var cmdV = CommandBufferPool.Get(_tagV);
                    cmdV.SetRenderTarget(_dstRT);
                    _gaussianVertMat.SetTexture(_SourceTex, _tmpRT);
                    _gaussianVertMat.SetVector(_TexelSize, new Vector4(1.0f / w, 1.0f / h, 0, 0));
                    _gaussianVertMat.SetFloat(_Sigma, _sigma);
                    cmdV.DrawProcedural(Matrix4x4.identity, _gaussianVertMat, 1, MeshTopology.Triangles, 3,
                        1); // pass 1 = V
                    ctx.ExecuteCommandBuffer(cmdV);
                    CommandBufferPool.Release(cmdV);

                    blurInputTex = _dstRT;
                }
            }

            // 合成到 base（覆盖），Stencil 由材质 pass 决定
            int passIndex = _useStencilNotEqual ? 1 : 0;

            var cmdC = CommandBufferPool.Get(_tagComposite);
            cmdC.SetRenderTarget(_baseCol, _baseDS);
            _compositeMat.SetTexture(_SourceTex, blurInputTex);
            if (_compositeMat.HasProperty(_StencilRef))
                _compositeMat.SetInt(_StencilRef, _stencilVal);

            cmdC.DrawProcedural(Matrix4x4.identity, _compositeMat, passIndex, MeshTopology.Triangles, 3, 1);
            ctx.ExecuteCommandBuffer(cmdC);
            CommandBufferPool.Release(cmdC);
        }
    }
}
