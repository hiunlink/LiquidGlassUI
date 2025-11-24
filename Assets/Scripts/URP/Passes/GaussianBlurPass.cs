using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace URP.Passes
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

        RTHandle _srcRT;    // 输入源
        RTHandle _tmpRT;    // H 输出
        RTHandle _dstRT;    // V 输出（最终输入合成）
        RTHandle _baseCol;  // 合成目标
        RTHandle _baseDS;   // 模板缓冲

        Material _gaussianMat;
        List<Material> _gaussianVertMats = new();
        List<Material> _gaussianHorMats = new();
        Material _compositeMat;

        private int _iteration = 1;
        private float _mipMap = 0;
        float _sigma;         // 连续可调
        float _tinyThreshold; // 小阈值（<= 则跳过模糊）

        bool _useStencilNotEqual;
        bool _useStencilNotEqualComposite;
        int  _stencilVal;

        static readonly int _SourceTex  = Shader.PropertyToID("_SourceTex");
        static readonly int _TexelSize  = Shader.PropertyToID("_TexelSize");
        static readonly int _Sigma      = Shader.PropertyToID("_Sigma");
        static readonly int _MipMap      = Shader.PropertyToID("_MipMap");
        static readonly int _StencilRef = Shader.PropertyToID("_StencilRef");

        public GaussianBlurPass(
            string tagPrefix,
            RenderPassEvent injectEvent
            )
        {
            renderPassEvent   = injectEvent;

            _tagH = string.IsNullOrEmpty(tagPrefix) ? "GaussianBlur.H" : tagPrefix + ".H";
            _tagV = string.IsNullOrEmpty(tagPrefix) ? "GaussianBlur.V" : tagPrefix + ".V";
            _tagComposite = string.IsNullOrEmpty(tagPrefix) ? "GaussianBlur.Composite" : tagPrefix + ".Composite";
        }

        public void Setup(
            RTHandle srcRT,
            RTHandle tmpRT,
            RTHandle dstRT,
            RTHandle baseCol,
            RTHandle baseDS
            )
        {
            _srcRT   = srcRT;
            _tmpRT   = tmpRT;
            _dstRT   = dstRT;
            _baseCol = baseCol;
            _baseDS  = baseDS;
        }

        public void SetSharedMaterials(Material gaussianMat, Material compositeCopyMat)
        {
            if (!_gaussianMat)
                _gaussianMat  = new Material(gaussianMat);
            if (!_compositeMat)
                _compositeMat = new Material(compositeCopyMat);
        }

        public void SetParams(
            int iteration,
            float sigma,
            float mipMap,
            bool useStencilNotEqual,
            bool useStencilNotEqualComposite,
            int stencilVal)
        {
            _sigma         = Mathf.Max(0f, sigma);
            _tinyThreshold = 0;

            _useStencilNotEqual = useStencilNotEqual;
            _useStencilNotEqualComposite = useStencilNotEqualComposite;
            _stencilVal         = Mathf.Clamp(stencilVal, 0, 255);
            
            _iteration = iteration;
            _mipMap = mipMap;
        }
        

        public override void Execute(ScriptableRenderContext ctx, ref RenderingData data)
        {
            if (_srcRT == null || _baseCol == null || _gaussianMat == null || _compositeMat == null)
                return;
            
            
            int w = _srcRT.rt.width;
            int h = _srcRT.rt.height;

            // 生成mipMap
            if (_mipMap > 0)
            {
                var g = CommandBufferPool.Get(_tagComposite);
                g.GenerateMips(_srcRT);
                ctx.ExecuteCommandBuffer(g);
                CommandBufferPool.Release(g);
            }
            
            // 选择要合成的输入贴图：sigma 很小时跳过 H/V
            Texture blurInputTex = _srcRT;
            for (int it = 0; it < _iteration; it++)
            {
                if (_sigma > _tinyThreshold)
                {
                    // H: src → tmp
                    var gaussianHorMat = GetOrCreateHorMaterial(it);
                    var cmdH = CommandBufferPool.Get(_tagH);
                    cmdH.SetRenderTarget(_tmpRT);
                    gaussianHorMat.SetTexture(_SourceTex, blurInputTex);
                    gaussianHorMat.SetVector(_TexelSize, new Vector4(1.0f / w, 1.0f / h, 0, 0));
                    gaussianHorMat.SetFloat(_Sigma, _sigma);
                    gaussianHorMat.SetFloat(_MipMap, _mipMap);
                    cmdH.DrawProcedural(Matrix4x4.identity, gaussianHorMat, 0, MeshTopology.Triangles, 3,
                        1); // pass 0 = H
                    ctx.ExecuteCommandBuffer(cmdH);
                    CommandBufferPool.Release(cmdH);

                    // V: tmp → dst
                    var gaussianVertMat = GetOrCreateVertMaterial(it);
                    var cmdV = CommandBufferPool.Get(_tagV);
                    cmdV.SetRenderTarget(_dstRT);
                    gaussianVertMat.SetTexture(_SourceTex, _tmpRT);
                    gaussianVertMat.SetVector(_TexelSize, new Vector4(1.0f / w, 1.0f / h, 0, 0));
                    gaussianVertMat.SetFloat(_Sigma, _sigma);
                    gaussianVertMat.SetFloat(_MipMap, _mipMap);
                    cmdV.DrawProcedural(Matrix4x4.identity, gaussianVertMat, 1, MeshTopology.Triangles, 3,
                        1); // pass 1 = V
                    ctx.ExecuteCommandBuffer(cmdV);
                    CommandBufferPool.Release(cmdV);

                    blurInputTex = _dstRT;
                }
            }

            // 合成到 base（覆盖），Stencil 由材质 pass 决定
            int passIndex = _useStencilNotEqualComposite ? 1 : 0; 

            var cmdC = CommandBufferPool.Get(_tagComposite);
            if (_baseDS != null)
                cmdC.SetRenderTarget(_baseCol, _baseDS);
            else
            {
                cmdC.SetRenderTarget(_baseCol);
            }
            _compositeMat.SetTexture(_SourceTex, blurInputTex);
            if (_compositeMat.HasProperty(_StencilRef))
                _compositeMat.SetInt(_StencilRef, _stencilVal);

            cmdC.DrawProcedural(Matrix4x4.identity, _compositeMat, passIndex, MeshTopology.Triangles, 3, 1);
            ctx.ExecuteCommandBuffer(cmdC);
            CommandBufferPool.Release(cmdC);
        }

        private Material GetOrCreateVertMaterial(int i)
        {
            while (_gaussianVertMats.Count <= i) _gaussianVertMats.Add(null);
            var m = _gaussianVertMats[i];
            if (m == null) {m = new Material(_gaussianMat);
                _gaussianVertMats[i] = m;
            }

            return m;
        }
        private Material GetOrCreateHorMaterial(int i)
        {
            while (_gaussianHorMats.Count <= i) _gaussianHorMats.Add(null);
            var m = _gaussianHorMats[i];
            if (m == null) {m = new Material(_gaussianMat);
                _gaussianHorMats[i] = m;
            }

            return m;
        }
    }
}
