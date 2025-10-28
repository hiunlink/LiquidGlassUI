using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class UIMaskPhaseController : MonoBehaviour
{
    [Header("层设置")]
    public LayerMask characterLayer;   // 人物：预通道写Stencil
    public LayerMask backgroundLayer;  // 背景：读Stencil (NotEqual 1)

    [Header("材质")]
    public Material stencilWriteMat;   // Hidden/CharacterStencilWrite（只写模板，ColorMask 0）

    // UI Stencil 属性（Unity UI 默认 Shader 兼容）
    static readonly int _Stencil         = Shader.PropertyToID("_Stencil");
    static readonly int _StencilComp     = Shader.PropertyToID("_StencilComp");
    static readonly int _StencilReadMask = Shader.PropertyToID("_StencilReadMask");
    static readonly int _StencilWriteMask= Shader.PropertyToID("_StencilWriteMask");
    static readonly int _ColorMask       = Shader.PropertyToID("_ColorMask");

    const int STENCIL_REF   = 1;
    const int COMP_NOTEQUAL = 6;  // CompareFunction.NotEqual
    const int MASK_ALL      = 255;
    const int COLORMASK_ALL = 15;

    struct SavedCR
    {
        public CanvasRenderer cr;
        public int originalCount;
        public Material[] originals; // 恢复用（长度=originalCount）
        public Material[] temps;     // 背景临时材质（需要销毁），人物为null
    }

    readonly List<SavedCR> _charSaved = new();
    readonly List<SavedCR> _bgSaved   = new();

    static bool InLayer(LayerMask mask, int layer) => (mask.value & (1 << layer)) != 0;

    static Material GetMaterialSafe(CanvasRenderer cr, int index)
    {
        // 有些 UI 返回 materialCount=0；访问前先保证至少为1
        int count = cr.materialCount;
        if (count <= 0) return null;
        if (index < 0 || index >= count) return null;
        return cr.GetMaterial(index);
    }

    public void BeginPhase()
    {
        _charSaved.Clear();
        _bgSaved.Clear();

        // 1) 人物：临时改“只写Stencil”材质（不写颜色）
        foreach (var g in GetComponentsInChildren<Graphic>(true))
        {
            if (!InLayer(characterLayer, g.gameObject.layer)) continue;

            var cr = g.canvasRenderer;

            // 读取原slot数量；若为0，强制设为1，避免后续SetMaterial越界
            int oldCount = cr.materialCount;
            if (oldCount <= 0) oldCount = 1;

            // 备份原材质
            var originals = new Material[oldCount];
            for (int i = 0; i < oldCount; i++)
            {
                var src = GetMaterialSafe(cr, i);
                if (src == null) src = Graphic.defaultGraphicMaterial; // ✅ 兜底
                originals[i] = src;
            }

            // 设置slot数量（用属性，而不是 SetMaterialCount）
            cr.materialCount = oldCount;

            // 全部slot替换为写Stencil材质实例（避免共享材质污染）
            for (int i = 0; i < oldCount; i++)
                cr.SetMaterial(stencilWriteMat, i);

            _charSaved.Add(new SavedCR {
                cr = cr, originalCount = oldCount, originals = originals, temps = null
            });
        }

        // 2) 背景：克隆原材质→注入Stencil读配置（NotEqual 1）
        foreach (var g in GetComponentsInChildren<Graphic>(true))
        {
            if (!InLayer(backgroundLayer, g.gameObject.layer)) continue;

            var cr = g.canvasRenderer;

            int oldCount = cr.materialCount;
            if (oldCount <= 0) oldCount = 1;

            var originals = new Material[oldCount];
            var temps     = new Material[oldCount];

            for (int i = 0; i < oldCount; i++)
            {
                var src = GetMaterialSafe(cr, i);
                if (src == null) src = Graphic.defaultGraphicMaterial; // ✅ 兜底
                originals[i] = src;

                var tmp = new Material(src);  // 克隆实例，避免修改shared
                tmp.SetInt(_Stencil,          STENCIL_REF);
                tmp.SetInt(_StencilComp,      COMP_NOTEQUAL);
                tmp.SetInt(_StencilReadMask,  MASK_ALL);
                tmp.SetInt(_StencilWriteMask, MASK_ALL);
                tmp.SetInt(_ColorMask,        COLORMASK_ALL);
                temps[i] = tmp;
            }

            cr.materialCount = oldCount;
            for (int i = 0; i < oldCount; i++)
                cr.SetMaterial(temps[i], i);

            _bgSaved.Add(new SavedCR {
                cr = cr, originalCount = oldCount, originals = originals, temps = temps
            });
        }
    }

    public void EndPhase()
    {
        // 恢复人物
        foreach (var s in _charSaved)
        {
            if (!s.cr) continue;
            s.cr.materialCount = s.originalCount;
            for (int i = 0; i < s.originalCount; i++)
                s.cr.SetMaterial(s.originals[i], i);
        }
        _charSaved.Clear();

        // 恢复背景 & 销毁临时材质
        foreach (var s in _bgSaved)
        {
            if (s.cr)
            {
                s.cr.materialCount = s.originalCount;
                for (int i = 0; i < s.originalCount; i++)
                    s.cr.SetMaterial(s.originals[i], i);
            }
            if (s.temps != null)
                for (int i = 0; i < s.temps.Length; i++)
                    if (s.temps[i]) Destroy(s.temps[i]);
        }
        _bgSaved.Clear();
    }
}
