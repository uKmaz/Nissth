using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEditor;
using Axiom.Editor.AgentBridge.Core;

namespace Axiom.Editor.AgentBridge.Actions
{
    /// <summary>
    /// Safe, undo-able operations for adding, removing, and configuring components.
    /// All serialized property changes go through SerializedObject/SerializedProperty.
    /// </summary>
    public static class ComponentActions
    {
        // ─────────────────────────────────────────────────────
        //  3.1 AddComponent
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// Adds a component to a GameObject. Undo-safe.
        /// </summary>
        public static ActionResult AddComponent(string objectPath, string componentType)
        {
            if (!PathResolver.ResolveRootPath(objectPath, out var targets) || targets.Count == 0)
                return ActionResult.Fail($"Object not found: {objectPath}");

            var go = targets[0];
            var type = TypeResolver.ResolveComponentType(componentType);
            if (type == null)
                return ActionResult.Fail($"Component type not found: {componentType}");

            if (go.GetComponent(type) != null)
            {
                if (type.GetCustomAttribute<DisallowMultipleComponent>() != null)
                    return ActionResult.Fail($"{componentType} already exists on {objectPath} and disallows multiple instances");
            }

            var component = Undo.AddComponent(go, type);
            Debug.Log($"[AgentBridge] Added {componentType} to {objectPath}");
            return ActionResult.Ok($"Added {componentType} to {objectPath}", component);
        }

        // ─────────────────────────────────────────────────────
        //  3.2 RemoveComponent
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// Removes a component from a GameObject. Respects RequireComponent dependencies.
        /// </summary>
        public static ActionResult RemoveComponent(string objectPath, string componentType)
        {
            if (!PathResolver.ResolveRootPath(objectPath, out var targets) || targets.Count == 0)
                return ActionResult.Fail($"Object not found: {objectPath}");

            var go = targets[0];
            var type = TypeResolver.ResolveComponentType(componentType);
            if (type == null)
                return ActionResult.Fail($"Component type not found: {componentType}");

            var component = go.GetComponent(type);
            if (component == null)
                return ActionResult.Fail($"{componentType} not found on {objectPath}");

            // Check for RequireComponent dependencies from other components
            var allComponents = go.GetComponents<Component>();
            foreach (var other in allComponents)
            {
                if (other == null || other == component) continue;
                var requireAttrs = other.GetType().GetCustomAttributes<RequireComponent>(true);
                foreach (var req in requireAttrs)
                {
                    if (req.m_Type0 == type || req.m_Type1 == type || req.m_Type2 == type)
                    {
                        return ActionResult.Fail(
                            $"Cannot remove {componentType}: required by {other.GetType().Name} on {objectPath}");
                    }
                }
            }

            Undo.DestroyObjectImmediate(component);
            Debug.Log($"[AgentBridge] Removed {componentType} from {objectPath}");
            return ActionResult.Ok($"Removed {componentType} from {objectPath}");
        }

        // ─────────────────────────────────────────────────────
        //  3.3 SetProperty
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// Sets a SerializedProperty value on a component. Undo-safe.
        /// </summary>
        /// <param name="value">
        /// Numeric: "5.5" | Bool: "true"/"false" | String: value |
        /// Enum: name or index | Vector: "(x, y, z)" | Color: "(r, g, b, a)" |
        /// ObjectReference: breadcrumb path to scene object or asset path
        /// </param>
        public static ActionResult SetProperty(
            string objectPath,
            string componentType,
            string propertyPath,
            string value)
        {
            if (!PathResolver.ResolveRootPath(objectPath, out var targets) || targets.Count == 0)
                return ActionResult.Fail($"Object not found: {objectPath}");
            var go = targets[0];

            var type = TypeResolver.ResolveComponentType(componentType);
            if (type == null) return ActionResult.Fail($"Component type not found: {componentType}");
            var component = go.GetComponent(type);
            if (component == null) return ActionResult.Fail($"{componentType} not found on {objectPath}");

            var serializedObject = new SerializedObject(component);
            var property = serializedObject.FindProperty(propertyPath);
            if (property == null) return ActionResult.Fail($"Property not found: {propertyPath} on {componentType}");

            bool success = SetPropertyValue(property, value);
            if (!success)
                return ActionResult.Fail($"Failed to set {propertyPath} to '{value}' (type: {property.propertyType})");

            serializedObject.ApplyModifiedProperties();
            Debug.Log($"[AgentBridge] Set {objectPath}/{componentType}.{propertyPath} = {value}");
            return ActionResult.Ok($"Set {propertyPath} = {value} on {componentType} at {objectPath}");
        }

        // ─────────────────────────────────────────────────────
        //  3.4 BatchSetProperties
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// Sets multiple properties at once. All changes applied as a single undo operation.
        /// </summary>
        public static ActionResult BatchSetProperties(PropertyAssignment[] assignments)
        {
            if (assignments == null || assignments.Length == 0)
                return ActionResult.Fail("No assignments provided.");

            Undo.SetCurrentGroupName("Batch Set Properties");

            int successCount = 0;
            int failCount = 0;
            var errors = new List<string>();

            foreach (var assignment in assignments)
            {
                var result = SetProperty(
                    assignment.objectPath,
                    assignment.componentType,
                    assignment.propertyPath,
                    assignment.value);

                if (result.Success) successCount++;
                else
                {
                    failCount++;
                    errors.Add(result.Message);
                }
            }

            string summary = $"Set {successCount} properties. Failures: {failCount}.";
            if (errors.Count > 0) summary += $"\nErrors:\n{string.Join("\n", errors)}";
            return failCount == 0 ? ActionResult.Ok(summary) : ActionResult.Fail(summary);
        }

        // ─────────────────────────────────────────────────────
        //  3.5 AddComponentWithProperties
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// Adds a component and immediately sets properties on it.
        /// </summary>
        public static ActionResult AddComponentWithProperties(
            string objectPath,
            string componentType,
            Dictionary<string, string> properties)
        {
            Undo.SetCurrentGroupName($"Add {componentType} with properties");

            var addResult = AddComponent(objectPath, componentType);
            if (!addResult.Success) return addResult;

            foreach (var kvp in properties)
            {
                var setResult = SetProperty(objectPath, componentType, kvp.Key, kvp.Value);
                if (!setResult.Success)
                    Debug.LogWarning($"[AgentBridge] Failed to set {kvp.Key}: {setResult.Message}");
            }

            return ActionResult.Ok($"Added {componentType} to {objectPath} with {properties.Count} properties");
        }

        // ─────────────────────────────────────────────────────
        //  Editor Menu Items (testing)
        // ─────────────────────────────────────────────────────

        [MenuItem("Axiom/AgentBridge/Actions/Add BoxCollider to Test Object")]
        public static void MenuAddComponent()
        {
            var result = AddComponent("AgentBridge_TestObject", "BoxCollider");
            Debug.Log($"[AgentBridge] {result.Message}");
        }

        [MenuItem("Axiom/AgentBridge/Actions/Set Camera Far Clip")]
        public static void MenuSetProperty()
        {
            var result = SetProperty("Main Camera", "Camera", "m_FarClipPlane", "500");
            Debug.Log($"[AgentBridge] {result.Message}");
        }

        // ─────────────────────────────────────────────────────
        //  SetPropertyValue — delegates to Core/PropertyValueParser
        // ─────────────────────────────────────────────────────

        private static bool SetPropertyValue(SerializedProperty prop, string value)
        {
            return PropertyValueParser.SetPropertyValue(prop, value);
        }
    }

    // ─────────────────────────────────────────────────────
    //  PropertyAssignment (used by BatchSetProperties)
    // ─────────────────────────────────────────────────────

    [Serializable]
    public class PropertyAssignment
    {
        public string objectPath;
        public string componentType;
        public string propertyPath;
        public string value;
    }
}
