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
            var attr = (ShowIfAttribute)attribute;
            var show = ShouldShow(attr, property);

            if (!attr.HideInInspector || show)
            {
                EditorGUI.PropertyField(position, property, label, true);
            }
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            var attr = (ShowIfAttribute)attribute;
            var show = ShouldShow(attr, property);

            if (!attr.HideInInspector || show)
            {
                return EditorGUI.GetPropertyHeight(property, label);
            }
            return -EditorGUIUtility.standardVerticalSpacing;
        }

        private bool ShouldShow(ShowIfAttribute attr, SerializedProperty property)
        {
            var path = property.propertyPath;
            var result = true;

            // 检查 bool 条件
            if (!string.IsNullOrEmpty(attr.BoolField))
            {
                var boolPath = path.Replace(property.name, attr.BoolField);
                var boolProp = property.serializedObject.FindProperty(boolPath);
                if (boolProp != null && boolProp.propertyType == SerializedPropertyType.Boolean)
                {
                    result &= boolProp.boolValue;
                }
            }

            // 检查 enum 条件
            if (!string.IsNullOrEmpty(attr.EnumField))
            {
                var enumPath = path.Replace(property.name, attr.EnumField);
                var enumProp = property.serializedObject.FindProperty(enumPath);
                if (enumProp != null)
                {
                    // enum
                    if (enumProp.propertyType == SerializedPropertyType.Enum)
                        result &= (enumProp.enumValueIndex == attr.EnumValue);
                    // integer
                    else if (enumProp.propertyType == SerializedPropertyType.Integer)
                    {
                        result &= (enumProp.intValue == attr.EnumValue);                        
                    }
                }
            }

            return result;
        }
    }
}
#endif