using System;
using UnityEngine;

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public class ConditionalHideAttribute : PropertyAttribute
{
    public string ConditionalSourceField { get; private set; }
    public object CompareValue { get; private set; }
    public bool HideInInspector { get; set; } = true;

    public ConditionalHideAttribute(string conditionalSourceField, object compareValue)
    {
        ConditionalSourceField = conditionalSourceField;
        CompareValue = compareValue;
    }
}