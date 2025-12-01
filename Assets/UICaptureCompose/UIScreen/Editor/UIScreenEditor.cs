using UICaptureCompose.UIComponent.Editor;
using UnityEditor;
using UnityEngine;

namespace UICaptureCompose.UIScreen.Editor
{
    [CustomEditor(typeof(UIScreen))]
    public class UIScreenEditor: UnityEditor.Editor
    {
        private SerializedProperty _blurStrengthProp;
        private SerializedProperty _lowerBlurConfig;
        private void OnEnable()
        {
            _blurStrengthProp = serializedObject.FindProperty("lowerBlurStrength");
            _lowerBlurConfig = serializedObject.FindProperty("lowerCanvasBlurConfig");
        }
        
        public override void OnInspectorGUI()
        {
            if (_blurStrengthProp == null)
            {
                // 如果字段名改过了，这里能提醒一下
                EditorGUILayout.HelpBox("Property 'lowerBlurStrength' not found.", MessageType.Warning);
                DrawDefaultInspector();
                return;
            }
            if (_lowerBlurConfig == null)
            {
                // 如果字段名改过了，这里能提醒一下
                EditorGUILayout.HelpBox("Property 'lowerCanvasBlurConfig' not found.", MessageType.Warning);
                DrawDefaultInspector();
                return;
            }

            serializedObject.Update();

            // 修改前的值
            var oldStrength = _blurStrengthProp.floatValue;
            var oldBlurColor = _lowerBlurConfig.FindPropertyRelative("alphaBlendColor").colorValue;
            var oldBlurSigma = _lowerBlurConfig.FindPropertyRelative("gaussianSigma").floatValue;

            EditorGUI.BeginChangeCheck();
            // 直接调用默认 Inspector，保留你原有所有字段布局 & 属性
            DrawDefaultInspector();
            var anyChanged = EditorGUI.EndChangeCheck();

            if (anyChanged)
            {
                serializedObject.ApplyModifiedProperties();

                // 修改后的值
                var newStrength = _blurStrengthProp.floatValue;
                var newBlurColor = _lowerBlurConfig.FindPropertyRelative("alphaBlendColor").colorValue;
                var newBlurSigma = _lowerBlurConfig.FindPropertyRelative("gaussianSigma").floatValue;

                // 判断 lowerBlurStrength 是否有变化
                if (!Mathf.Approximately(oldStrength, newStrength) ||
                    !Mathf.Approximately(oldBlurSigma, newBlurSigma) || 
                    oldBlurColor != newBlurColor)
                {
                    // 发事件：交给你的渲染管线去处理具体的刷新
                    LiquidGlassUIEffectEditorHooks.OnGaussianSigmaChanged?.Invoke();
                }
            }
        }
    }
}