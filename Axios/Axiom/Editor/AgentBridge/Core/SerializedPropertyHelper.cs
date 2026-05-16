using UnityEditor;

namespace Axiom.Editor.AgentBridge.Core
{
    /// <summary>
    /// Shared utility for formatting SerializedProperty values as human-readable strings.
    /// Used by HierarchyLens, ComponentInspector, and SceneDiff.
    /// </summary>
    public static class SerializedPropertyHelper
    {
        /// <summary>
        /// Returns a human-readable string representation of a SerializedProperty value.
        /// </summary>
        public static string GetPropertyValue(SerializedProperty prop)
        {
            switch (prop.propertyType)
            {
                case SerializedPropertyType.Integer:
                    return prop.intValue.ToString();
                case SerializedPropertyType.Boolean:
                    return prop.boolValue.ToString();
                case SerializedPropertyType.Float:
                    return prop.floatValue.ToString("F4");
                case SerializedPropertyType.String:
                    return $"\"{prop.stringValue}\"";
                case SerializedPropertyType.Enum:
                    return prop.enumDisplayNames != null && prop.enumValueIndex >= 0
                        && prop.enumValueIndex < prop.enumDisplayNames.Length
                        ? prop.enumDisplayNames[prop.enumValueIndex]
                        : prop.enumValueIndex.ToString();
                case SerializedPropertyType.ObjectReference:
                    if (prop.objectReferenceValue != null)
                        return $"{prop.objectReferenceValue.name} ({prop.objectReferenceValue.GetType().Name})";
                    else if (prop.objectReferenceInstanceIDValue != 0)
                        return "MISSING (was assigned but target destroyed/deleted)";
                    else
                        return "None (null)";
                case SerializedPropertyType.Vector2:
                    return prop.vector2Value.ToString("F2");
                case SerializedPropertyType.Vector3:
                    return prop.vector3Value.ToString("F2");
                case SerializedPropertyType.Vector4:
                    return prop.vector4Value.ToString("F2");
                case SerializedPropertyType.Color:
                    return prop.colorValue.ToString();
                case SerializedPropertyType.Rect:
                    return prop.rectValue.ToString();
                case SerializedPropertyType.Bounds:
                    return prop.boundsValue.ToString();
                case SerializedPropertyType.AnimationCurve:
                    return $"AnimationCurve ({prop.animationCurveValue?.length ?? 0} keys)";
                case SerializedPropertyType.LayerMask:
                    return prop.intValue.ToString();
                default:
                    if (prop.isArray)
                        return $"Array [{prop.arraySize}]";
                    return $"({prop.propertyType})";
            }
        }
    }
}
