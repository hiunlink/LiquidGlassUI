using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(Image))]
public class UISetBlurRT : MonoBehaviour
{
    public int targetLayer = 1;  // _UI_RT_x 中的 x

    Image img;
    Material mat;
    static int PropBlurTex = Shader.PropertyToID("_BlurTex");

    void Awake()
    {
        img = GetComponent<Image>();
        mat = Instantiate(img.material);  // 不污染共享材质
        img.material = mat;
    }

    void LateUpdate()
    {
        string globalName = $"_UI_RT_{targetLayer}";
        Texture rt = Shader.GetGlobalTexture(globalName);
        if (rt != null)
            mat.SetTexture(PropBlurTex, rt);
    }
}