using UnityEngine;

public class UIMipLayerDebugView : MonoBehaviour
{
    [Header("按 F2 开关")]
    public KeyCode toggleKey = KeyCode.F2;

    [Header("缩略图宽度（自动等比）")]
    public float previewWidth = 200f;

    [Header("间距")]
    public float padding = 8f;

    bool _show;

    void Update()
    {
        if (Input.GetKeyDown(toggleKey))
            _show = !_show;
    }

    void OnGUI()
    {
        if (!_show) return;
        if (UIMipLayerPipeline.I == null) return;

        var pipeline = UIMipLayerPipeline.I;
        int count = pipeline.layers?.Length ?? 0;
        if (count == 0) return;

        var rtField = typeof(UIMipLayerPipeline).GetField("_rt", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (rtField == null) return;
        var rtArray = rtField.GetValue(pipeline) as RenderTexture[];
        if (rtArray == null) return;

        float x = padding;
        float y = padding;

        GUI.color = Color.white;
        GUI.depth = 0;

        for (int i = 0; i < rtArray.Length; i++)
        {
            var rt = rtArray[i];
            if (!rt) continue;

            float aspect = (float)rt.height / rt.width;
            float h = previewWidth * aspect;
            Rect r = new Rect(x, y, previewWidth, h);

            GUI.DrawTexture(r, rt, ScaleMode.ScaleToFit, false);

            // Label
            GUI.color = Color.yellow;
            GUI.Label(new Rect(x + 6, y + 6, 120, 32), $"Layer {i}");
            GUI.color = Color.white;

            y += h + padding;
        }
    }
}