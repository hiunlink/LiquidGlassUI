using System;
using UnityEngine;

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public class ShowIfAttribute : PropertyAttribute
{
    public string BoolField { get; private set; }
    public string EnumField { get; private set; }
    public int EnumValue { get; private set; }
    public bool HideInInspector { get; set; } = true;

    // 只判断一个 bool
    public ShowIfAttribute(string boolField)
    {
        BoolField = boolField;
    }

    // 判断 bool AND enum
    public ShowIfAttribute(string boolField, string enumField, int enumValue)
    {
        BoolField = boolField;
        EnumField = enumField;
        EnumValue = enumValue;
    }
}