// ShowIfPropertyDrawer.cs
#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace UICaptureCompose.URP.Editor
{
    [CustomPropertyDrawer(typeof(ShowIfAttribute))]
    public class ShowIfPropertyDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            ShowIfAttribute attr = (ShowIfAttribute)attribute;
            bool show = ShouldShow(attr, property);

            if (!attr.HideInInspector || show)
            {
                EditorGUI.PropertyField(position, property, label, true);
            }
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            ShowIfAttribute attr = (ShowIfAttribute)attribute;
            bool show = ShouldShow(attr, property);

            if (!attr.HideInInspector || show)
            {
                return EditorGUI.GetPropertyHeight(property, label);
            }
            return -EditorGUIUtility.standardVerticalSpacing;
        }

        private bool ShouldShow(ShowIfAttribute attr, SerializedProperty property)
        {
            string path = property.propertyPath;
            bool result = true;

            // 检查 bool 条件
            if (!string.IsNullOrEmpty(attr.BoolField))
            {
                string boolPath = path.Replace(property.name, attr.BoolField);
                SerializedProperty boolProp = property.serializedObject.FindProperty(boolPath);
                if (boolProp != null && boolProp.propertyType == SerializedPropertyType.Boolean)
                {
                    result &= boolProp.boolValue;
                }
            }

            // 检查 enum 条件
            if (!string.IsNullOrEmpty(attr.EnumField))
            {
                string enumPath = path.Replace(property.name, attr.EnumField);
                SerializedProperty enumProp = property.serializedObject.FindProperty(enumPath);
                if (enumProp != null && enumProp.propertyType == SerializedPropertyType.Enum)
                {
                    result &= (enumProp.enumValueIndex == attr.EnumValue);
                }
            }

            return result;
        }
    }
}
#endif