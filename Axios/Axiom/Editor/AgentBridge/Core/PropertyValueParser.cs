using System;
using System.Globalization;
using System.Linq;
using UnityEngine;
using UnityEditor;

namespace Axiom.Editor.AgentBridge.Core
{
    /// <summary>
    /// Shared utility for parsing string values into SerializedProperty types.
    /// Used by ComponentActions and AssetActions to set serialized property values.
    /// </summary>
    public static class PropertyValueParser
    {
        /// <summary>
        /// Sets a SerializedProperty to the parsed value of the given string.
        /// Handles Integer, Boolean, Float, String, Enum, Color, Vector2/3/4, Rect, LayerMask, ObjectReference.
        /// </summary>
        public static bool SetPropertyValue(SerializedProperty prop, string value)
        {
            try
            {
                switch (prop.propertyType)
                {
                    case SerializedPropertyType.Integer:
                        prop.intValue = int.Parse(value);
                        return true;

                    case SerializedPropertyType.Boolean:
                        prop.boolValue = bool.Parse(value);
                        return true;

                    case SerializedPropertyType.Float:
                        prop.floatValue = float.Parse(value, CultureInfo.InvariantCulture);
                        return true;

                    case SerializedPropertyType.String:
                        prop.stringValue = value;
                        return true;

                    case SerializedPropertyType.Enum:
                        for (int i = 0; i < prop.enumDisplayNames.Length; i++)
                        {
                            if (prop.enumDisplayNames[i].Equals(value, StringComparison.OrdinalIgnoreCase)
                                || prop.enumNames[i].Equals(value, StringComparison.OrdinalIgnoreCase))
                            {
                                prop.enumValueIndex = i;
                                return true;
                            }
                        }
                        if (int.TryParse(value, out int enumIndex))
                        {
                            prop.enumValueIndex = enumIndex;
                            return true;
                        }
                        return false;

                    case SerializedPropertyType.Color:
                        prop.colorValue = ParseColor(value);
                        return true;

                    case SerializedPropertyType.Vector2:
                        prop.vector2Value = ParseVector2(value);
                        return true;

                    case SerializedPropertyType.Vector3:
                        prop.vector3Value = ParseVector3(value);
                        return true;

                    case SerializedPropertyType.Vector4:
                        prop.vector4Value = ParseVector4(value);
                        return true;

                    case SerializedPropertyType.Rect:
                        prop.rectValue = ParseRect(value);
                        return true;

                    case SerializedPropertyType.LayerMask:
                        prop.intValue = LayerMask.GetMask(value.Split(',').Select(s => s.Trim()).ToArray());
                        return true;

                    case SerializedPropertyType.ObjectReference:
                        return SetObjectReference(prop, value);

                    default:
                        Debug.LogWarning($"[AgentBridge] Unsupported property type: {prop.propertyType}");
                        return false;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[AgentBridge] Error setting property: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Resolves an ObjectReference property value from either an asset path or a scene breadcrumb path.
        /// </summary>
        public static bool SetObjectReference(SerializedProperty prop, string value)
        {
            if (value == "null" || value == "None" || string.IsNullOrEmpty(value))
            {
                prop.objectReferenceValue = null;
                return true;
            }

            // Try as asset path first
            if (value.StartsWith("Assets/") || value.StartsWith("Packages/"))
            {
                var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(value);
                if (asset != null)
                {
                    prop.objectReferenceValue = asset;
                    return true;
                }
                Debug.LogWarning($"[AgentBridge] Asset not found at path: {value}");
                return false;
            }

            // Try as scene object breadcrumb path
            if (PathResolver.ResolveRootPath(value, out var sceneTargets) && sceneTargets.Count > 0)
            {
                var targetGo = sceneTargets[0];
                string expectedType = prop.type;

                if (expectedType.StartsWith("PPtr<") && expectedType.EndsWith(">"))
                {
                    string typeName = expectedType.Substring(5, expectedType.Length - 6);
                    if (typeName.StartsWith("$")) typeName = typeName.Substring(1);

                    if (typeName == "GameObject")
                    {
                        prop.objectReferenceValue = targetGo;
                        return true;
                    }

                    if (typeName == "Transform")
                    {
                        prop.objectReferenceValue = targetGo.transform;
                        return true;
                    }

                    var compType = TypeResolver.ResolveComponentType(typeName);
                    if (compType != null)
                    {
                        var comp = targetGo.GetComponent(compType);
                        if (comp != null)
                        {
                            prop.objectReferenceValue = comp;
                            return true;
                        }
                        Debug.LogWarning($"[AgentBridge] {typeName} not found on {value}");
                        return false;
                    }
                }

                prop.objectReferenceValue = targetGo;
                return true;
            }

            Debug.LogWarning($"[AgentBridge] Could not resolve reference: {value}");
            return false;
        }

        // ─────────────────────────────────────────────────────
        //  Read Helper
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// Converts a SerializedProperty current value to a display string.
        /// Used by RenderActions, BuildProfileActions, and diagnostics.
        /// </summary>
        public static string GetValueString(SerializedProperty prop)
        {
            if (prop == null) return "<null>";
            switch (prop.propertyType)
            {
                case SerializedPropertyType.Integer: return prop.intValue.ToString();
                case SerializedPropertyType.Boolean: return prop.boolValue.ToString();
                case SerializedPropertyType.Float: return prop.floatValue.ToString("F4");
                case SerializedPropertyType.String: return prop.stringValue;
                case SerializedPropertyType.Enum:
                    return prop.enumDisplayNames.Length > prop.enumValueIndex && prop.enumValueIndex >= 0
                        ? prop.enumDisplayNames[prop.enumValueIndex]
                        : prop.enumValueIndex.ToString();
                case SerializedPropertyType.Color: return prop.colorValue.ToString();
                case SerializedPropertyType.Vector2: return prop.vector2Value.ToString();
                case SerializedPropertyType.Vector3: return prop.vector3Value.ToString();
                case SerializedPropertyType.Vector4: return prop.vector4Value.ToString();
                case SerializedPropertyType.Rect: return prop.rectValue.ToString();
                case SerializedPropertyType.ObjectReference:
                    return prop.objectReferenceValue != null
                        ? $"{prop.objectReferenceValue.name} ({prop.objectReferenceValue.GetType().Name})"
                        : "None";
                case SerializedPropertyType.LayerMask: return LayerMask.LayerToName(prop.intValue);
                default: return $"({prop.propertyType})";
            }
        }

        // ─────────────────────────────────────────────────────
        //  Parse Helpers (public for use by AssetActions/CreateMaterial)
        // ─────────────────────────────────────────────────────

        public static float[] ParseFloats(string value)
        {
            value = value.Replace("(", "").Replace(")", "").Trim();
            return value.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => float.Parse(s.Trim(), CultureInfo.InvariantCulture))
                .ToArray();
        }

        public static Vector2 ParseVector2(string value)
        {
            var f = ParseFloats(value);
            return new Vector2(f.Length > 0 ? f[0] : 0, f.Length > 1 ? f[1] : 0);
        }

        public static Vector3 ParseVector3(string value)
        {
            var f = ParseFloats(value);
            return new Vector3(
                f.Length > 0 ? f[0] : 0,
                f.Length > 1 ? f[1] : 0,
                f.Length > 2 ? f[2] : 0);
        }

        public static Vector4 ParseVector4(string value)
        {
            var f = ParseFloats(value);
            return new Vector4(
                f.Length > 0 ? f[0] : 0,
                f.Length > 1 ? f[1] : 0,
                f.Length > 2 ? f[2] : 0,
                f.Length > 3 ? f[3] : 0);
        }

        public static Color ParseColor(string value)
        {
            var f = ParseFloats(value);
            return new Color(
                f.Length > 0 ? f[0] : 0,
                f.Length > 1 ? f[1] : 0,
                f.Length > 2 ? f[2] : 0,
                f.Length > 3 ? f[3] : 1);
        }

        public static Rect ParseRect(string value)
        {
            var f = ParseFloats(value);
            return new Rect(
                f.Length > 0 ? f[0] : 0,
                f.Length > 1 ? f[1] : 0,
                f.Length > 2 ? f[2] : 0,
                f.Length > 3 ? f[3] : 0);
        }
    }
}
