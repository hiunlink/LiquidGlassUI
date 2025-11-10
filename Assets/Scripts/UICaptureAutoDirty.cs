using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using URP;

[DisallowMultipleComponent]
public class UICaptureAutoDirty : MonoBehaviour
{
    public UICaptureComposePerLayerFeature feature;
    public float checkInterval = 0.2f; // 检查间隔，防抖
    private float _timer;
    private int _lastHash;
    private Canvas _canvas; 
    
    static IList<Graphic> _tmpGraphics = new List<Graphic>();

    private void Awake()
    {
        _canvas = GetComponentInParent<Canvas>();
    }
    
    void OnEnable()
    {
        Canvas.willRenderCanvases += OnCanvasWillRender;
    }
    void OnDisable()
    {
        Canvas.willRenderCanvases -= OnCanvasWillRender;
    }
    
    void OnCanvasWillRender()
    {
        int hash = ComputeUIHash();
        if (hash != _lastHash)
        {
            _lastHash = hash;
            feature?.SetDirty(true);
        }
    }

    void LateUpdate0()
    {
        _timer += Time.unscaledDeltaTime;
        if (_timer < checkInterval) return;
        _timer = 0;

        int hash = ComputeUIHash();
        if (hash != _lastHash)
        {
            _lastHash = hash;
            feature?.SetDirty(true);
        }
    }

    int ComputeUIHash()
    {
        if (_canvas == null)
            return 0;
        _tmpGraphics = GraphicRegistry.GetGraphicsForCanvas(_canvas);
        unchecked
        {
            int hash = 17;
            for (var i = 0; i < _tmpGraphics.Count; i++)
            {
                var g = _tmpGraphics[i];
                if (g == null || !g.isActiveAndEnabled) continue;
                var t = g.rectTransform;
                hash = hash * 23 + g.color.GetHashCode();
                hash = hash * 23 + g.canvasRenderer.absoluteDepth;
                hash = hash * 23 + g.canvasRenderer.GetInstanceID();
                hash = hash * 23 + (t.hasChanged ? 1 : 0);
                t.hasChanged = false;
                
                var pos = t.localPosition;
                var scale = t.localScale;
                var rot = t.localRotation;
                var rect = t.rect;

                // Hash position + rotation + scale + size
                hash = hash * 23 + pos.GetHashCode();
                hash = hash * 23 + rot.GetHashCode();
                hash = hash * 23 + scale.GetHashCode();
                hash = hash * 23 + rect.width.GetHashCode();
                hash = hash * 23 + rect.height.GetHashCode();

                // Graphic color & depth 等
                hash = hash * 23 + g.color.GetHashCode();
                hash = hash * 23 + g.canvasRenderer.absoluteDepth;
                
            }
            return hash;
        }
    }
}