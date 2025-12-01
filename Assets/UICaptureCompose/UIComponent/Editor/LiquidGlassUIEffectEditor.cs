#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

namespace UICaptureCompose.UIComponent.Editor
{
    /// <summary>
    /// 提供编辑器侧回调，让渲染管线可以订阅 gaussianSigma 变更事件。
    /// </summary>
    public static class LiquidGlassUIEffectEditorHooks
    {
        /// <summary>
        /// 当 Inspector 中的 gaussianSigma 被修改时触发。
        /// 参数为被修改的组件实例。
        /// </summary>
        public static Action OnGaussianSigmaChanged;
    }

    /// <summary>
    /// LiquidGlassUIEffect 自定义 Inspector
    /// - 使用默认 Inspector 绘制所有字段
    /// - 额外在 gaussianSigma 变更时发事件 + 刷新视图
    /// </summary>
    [CustomEditor(typeof(LiquidGlassUIEffect))]
    public class LiquidGlassUIEffectEditor : UnityEditor.Editor
    {
        SerializedProperty _gaussianSigmaProp;

        private void OnEnable()
        {
            // 对应 LiquidGlassUIEffect 中的 public float gaussianSigma 字段名
            _gaussianSigmaProp = serializedObject.FindProperty("gaussianSigma");
        }

        public override void OnInspectorGUI()
        {
            if (_gaussianSigmaProp == null)
            {
                // 如果字段名改过了，这里能提醒一下
                EditorGUILayout.HelpBox("Property 'gaussianSigma' not found.", MessageType.Warning);
                DrawDefaultInspector();
                return;
            }

            serializedObject.Update();

            // 修改前的值
            float oldSigma = _gaussianSigmaProp.floatValue;

            EditorGUI.BeginChangeCheck();
            // 直接调用默认 Inspector，保留你原有所有字段布局 & 属性
            DrawDefaultInspector();
            bool anyChanged = EditorGUI.EndChangeCheck();

            if (anyChanged)
            {
                serializedObject.ApplyModifiedProperties();

                // 修改后的值
                float newSigma = _gaussianSigmaProp.floatValue;

                // 判断 gaussianSigma 是否有变化
                if (!Mathf.Approximately(oldSigma, newSigma))
                {
                    // 发事件：交给你的渲染管线去处理具体的刷新
                    LiquidGlassUIEffectEditorHooks.OnGaussianSigmaChanged?.Invoke();
                }
            }
        }
    }
}
#endif
