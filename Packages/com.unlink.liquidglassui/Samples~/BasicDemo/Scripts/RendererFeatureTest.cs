using System.Collections;
using System.Collections.Generic;
using UICaptureCompose;
using UICaptureCompose.UIScreen;
using UnityEngine;

public class RendererFeatureTest : MonoBehaviour
{
    [SerializeField] private UIScreen[] screens;
    
    public void MarkAllDirty()
    {
        UIScreenManager.Instance.MarkAllDirty();
        
        Debug.Log("Mark all layers dirty");
    }

    public void RebuildUIScreens()
    {
        foreach (var uiScreen in screens)
        {
            UIScreenManager.Instance.AddUIScreen(uiScreen);
            Debug.Log($"Update UIScreen Gid: {uiScreen.Gid}");
        }
        UIScreenManager.Instance.UpdateRendererFeature(true);
    }
}
