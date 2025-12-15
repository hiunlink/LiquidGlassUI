using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Unlink.LiquidGlassUI
{
    public class CompositePass: ScriptableRenderPass
    {
        private bool _useStencilNotEqualComposite;
        private string _tagComposite;
        private RTHandle _baseCol;
        private RTHandle _targetCol;
        private RTHandle _targetDS;
        private Material _compositeMat;
        private int _stencilVal;
        static readonly int _SourceTex  = Shader.PropertyToID("_SourceTex");
        static readonly int _StencilRef = Shader.PropertyToID("_StencilRef");

        public CompositePass( string tagPrefix, RenderPassEvent injectEvent)
        {
            renderPassEvent   = injectEvent;
            _tagComposite = string.IsNullOrEmpty(tagPrefix) ? "Composite between layers" : tagPrefix;
        }

        public void Setup(RTHandle srcRT, RTHandle targetRT, RTHandle targetDS, bool useStencilNotEqualComposite, int stencilVal)
        {
            _baseCol = srcRT;
            _targetCol = targetRT;
            _targetDS = targetDS;
            _useStencilNotEqualComposite = useStencilNotEqualComposite;
            _stencilVal = stencilVal;
        }

        public void SetMaterial(Material compositeMat)
        {
            if (!_compositeMat) 
                _compositeMat = new Material(compositeMat);
        }
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            var passIndex = _useStencilNotEqualComposite ? 1 : 0; 

            var cmdC = CommandBufferPool.Get(_tagComposite);
            if (_targetDS != null)
                cmdC.SetRenderTarget(_targetCol, _targetDS);
            else
            {
                cmdC.SetRenderTarget(_targetCol);
            }
            _compositeMat.SetTexture(_SourceTex, _baseCol);
            if (_compositeMat.HasProperty(_StencilRef))
                _compositeMat.SetInt(_StencilRef, _stencilVal);

            cmdC.DrawProcedural(Matrix4x4.identity, _compositeMat, passIndex, MeshTopology.Triangles, 3, 1);
            context.ExecuteCommandBuffer(cmdC);
            CommandBufferPool.Release(cmdC);
        }
    }
}