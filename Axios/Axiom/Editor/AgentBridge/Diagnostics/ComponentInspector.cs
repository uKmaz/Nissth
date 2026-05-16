using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEditor;
using Axiom.Editor.AgentBridge.Core;
using static Axiom.Editor.AgentBridge.Core.SerializedPropertyHelper;

namespace Axiom.Editor.AgentBridge.Diagnostics
{
    public enum ComponentInspectorMode
    {
        ExistenceCheck,         // Mode A: boolean per target — "does this component exist?"
        PropertyList,           // Mode B: all SerializedProperty paths and types for a component
        PropertyValues,         // Mode C: all SerializedProperty paths + current values
        CrossObjectComparison,  // Mode D: compare same component across multiple GameObjects
        PrefabOverrides,        // Mode E: which properties differ from the source prefab
        MissingReferences       // Mode F: null/missing SerializedProperty references
    }

    /// <summary>
    /// Deep component inspection — existence, properties, values, prefab overrides, missing references.
    /// </summary>
    public static class ComponentInspector
    {
        // ─────────────────────────────────────────────────────
        //  Type Resolution — delegated to Core/TypeResolver
        // ─────────────────────────────────────────────────────

        private static Type ResolveComponentType(string typeName)
        {
            return TypeResolver.ResolveComponentType(typeName);
        }

        // ─────────────────────────────────────────────────────
        //  Public Entry Point
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// Generates a component inspection report.
        /// </summary>
        /// <param name="mode">Detail level.</param>
        /// <param name="rootPath">Breadcrumb path to scope inspection. Null = all roots.</param>
        /// <param name="objectNames">Specific GameObject names to find and inspect. Null = use rootPath logic.</param>
        /// <param name="componentType">Component type name to focus on (e.g., "PlayerController").
        /// Required for modes B and C. Optional filter for modes A, E, F.</param>
        /// <param name="propertyPath">Specific property path to inspect (Mode C only). Null = all properties.</param>
        /// <param name="maxDepth">Recursion depth from root. -1 = unlimited.</param>
        /// <param name="includeInactive">Include inactive GameObjects. Default false.</param>
        /// <param name="includeUnassigned">For Mode F: also report unassigned (never-linked) references. Default false.</param>
        /// <returns>File path of the generated report.</returns>
        public static string GenerateReport(
            ComponentInspectorMode mode,
            string rootPath = null,
            string[] objectNames = null,
            string componentType = null,
            string propertyPath = null,
            int maxDepth = -1,
            bool includeInactive = false,
            bool includeUnassigned = false)
        {
            // Collect target GameObjects
            List<GameObject> targets = CollectTargets(rootPath, objectNames, maxDepth, includeInactive);

            string content;
            switch (mode)
            {
                case ComponentInspectorMode.ExistenceCheck:
                    content = BuildExistenceCheck(targets, componentType);
                    break;
                case ComponentInspectorMode.PropertyList:
                    content = BuildPropertyList(targets, componentType);
                    break;
                case ComponentInspectorMode.PropertyValues:
                    content = BuildPropertyValues(targets, componentType, propertyPath);
                    break;
                case ComponentInspectorMode.CrossObjectComparison:
                    content = BuildCrossObjectComparison(targets, componentType);
                    break;
                case ComponentInspectorMode.PrefabOverrides:
                    content = BuildPrefabOverrides(targets, componentType);
                    break;
                case ComponentInspectorMode.MissingReferences:
                    content = BuildMissingReferences(targets, componentType, includeUnassigned);
                    break;
                default:
                    content = $"# Component Inspector — Unknown Mode\n\nMode {mode} is not implemented.";
                    break;
            }

            return OutputWriter.WriteReport("component_inspector", content);
        }

        // ─────────────────────────────────────────────────────
        //  Object Collection
        // ─────────────────────────────────────────────────────

        private static List<GameObject> CollectTargets(string rootPath, string[] objectNames, int maxDepth, bool includeInactive)
        {
            var results = new List<GameObject>();

            if (objectNames != null && objectNames.Length > 0)
            {
                // Search by name across all root GameObjects
                var nameSet = new HashSet<string>(objectNames, StringComparer.Ordinal);
                foreach (GameObject root in PathResolver.GetRootGameObjects())
                    CollectByName(root.transform, nameSet, includeInactive, results);
            }
            else
            {
                if (!PathResolver.ResolveRootPath(rootPath, out var roots))
                {
                    Debug.LogWarning($"[AgentBridge] ComponentInspector: Could not resolve rootPath \"{rootPath}\".");
                    return results;
                }

                foreach (GameObject root in roots)
                    CollectRecursive(root.transform, 0, maxDepth, includeInactive, results);
            }

            return results;
        }

        private static void CollectByName(Transform t, HashSet<string> nameSet, bool includeInactive, List<GameObject> results)
        {
            if (!includeInactive && !t.gameObject.activeInHierarchy) return;
            if (nameSet.Contains(t.gameObject.name))
                results.Add(t.gameObject);
            for (int i = 0; i < t.childCount; i++)
                CollectByName(t.GetChild(i), nameSet, includeInactive, results);
        }

        private static void CollectRecursive(Transform t, int depth, int maxDepth, bool includeInactive, List<GameObject> results)
        {
            if (!includeInactive && !t.gameObject.activeInHierarchy) return;
            results.Add(t.gameObject);
            if (maxDepth >= 0 && depth >= maxDepth) return;
            for (int i = 0; i < t.childCount; i++)
                CollectRecursive(t.GetChild(i), depth + 1, maxDepth, includeInactive, results);
        }

        // ─────────────────────────────────────────────────────
        //  Mode A: Existence Check
        // ─────────────────────────────────────────────────────

        private static string BuildExistenceCheck(List<GameObject> targets, string componentType)
        {
            var sb = new StringBuilder();

            if (string.IsNullOrEmpty(componentType))
            {
                sb.AppendLine("# Component Inspector — Mode: Existence Check");
                sb.AppendLine();
                sb.AppendLine("ERROR: componentType is required for Existence Check mode.");
                return sb.ToString();
            }

            Type resolvedType = ResolveComponentType(componentType);

            sb.AppendLine($"# Component Inspector — Mode: Existence Check | Component: {componentType}");
            sb.AppendLine();
            sb.AppendLine("| GameObject Path | Has Component |");
            sb.AppendLine("| :--- | :--- |");

            int foundCount = 0;
            foreach (GameObject go in targets)
            {
                bool has = resolvedType != null
                    ? go.GetComponent(resolvedType) != null
                    : go.GetComponent(componentType) != null;

                string path = PathResolver.GetHierarchyPath(go.transform);
                sb.AppendLine($"| {path} | {(has ? "YES" : "NO")} |");
                if (has) foundCount++;
            }

            sb.AppendLine();
            sb.AppendLine("---");
            sb.Append($"Found on: {foundCount} of {targets.Count} GameObjects | Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            return sb.ToString();
        }

        // ─────────────────────────────────────────────────────
        //  Mode B: Property List
        // ─────────────────────────────────────────────────────

        private static string BuildPropertyList(List<GameObject> targets, string componentType)
        {
            var sb = new StringBuilder();

            if (string.IsNullOrEmpty(componentType))
            {
                sb.AppendLine("# Component Inspector — Mode: Property List");
                sb.AppendLine();
                sb.AppendLine("ERROR: componentType is required for Property List mode.");
                return sb.ToString();
            }

            Type resolvedType = ResolveComponentType(componentType);
            sb.AppendLine($"# Component Inspector — Mode: Property List | Component: {componentType}");
            sb.AppendLine();

            int inspectedCount = 0;
            foreach (GameObject go in targets)
            {
                Component comp = resolvedType != null
                    ? go.GetComponent(resolvedType)
                    : go.GetComponent(componentType);

                if (comp == null) continue;
                inspectedCount++;

                string path = PathResolver.GetHierarchyPath(go.transform);
                sb.AppendLine($"## {path} → {componentType}");
                sb.AppendLine("| Property Path | Display Name | Type |");
                sb.AppendLine("| :--- | :--- | :--- |");

                var so = new SerializedObject(comp);
                var iterator = so.GetIterator();
                bool enterChildren = true;
                while (iterator.NextVisible(enterChildren))
                {
                    enterChildren = false;
                    sb.AppendLine($"| {iterator.propertyPath} | {iterator.displayName} | {iterator.type} |");
                }

                sb.AppendLine();
            }

            sb.AppendLine("---");
            sb.Append($"Inspected: {inspectedCount} GameObjects | Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            return sb.ToString();
        }

        // ─────────────────────────────────────────────────────
        //  Mode C: Property Values
        // ─────────────────────────────────────────────────────

        private static string BuildPropertyValues(List<GameObject> targets, string componentType, string filterPropertyPath)
        {
            var sb = new StringBuilder();

            if (string.IsNullOrEmpty(componentType))
            {
                sb.AppendLine("# Component Inspector — Mode: Property Values");
                sb.AppendLine();
                sb.AppendLine("ERROR: componentType is required for Property Values mode.");
                return sb.ToString();
            }

            Type resolvedType = ResolveComponentType(componentType);
            sb.AppendLine($"# Component Inspector — Mode: Property Values | Component: {componentType}");
            sb.AppendLine();

            int inspectedCount = 0;
            foreach (GameObject go in targets)
            {
                Component comp = resolvedType != null
                    ? go.GetComponent(resolvedType)
                    : go.GetComponent(componentType);

                if (comp == null) continue;
                inspectedCount++;

                string path = PathResolver.GetHierarchyPath(go.transform);
                sb.AppendLine($"## {path} → {componentType}");
                sb.AppendLine("| Property Path | Display Name | Type | Value |");
                sb.AppendLine("| :--- | :--- | :--- | :--- |");

                var so = new SerializedObject(comp);

                if (!string.IsNullOrEmpty(filterPropertyPath))
                {
                    // Single property mode
                    var prop = so.FindProperty(filterPropertyPath);
                    if (prop != null)
                        sb.AppendLine($"| {prop.propertyPath} | {prop.displayName} | {prop.type} | {GetPropertyValue(prop)} |");
                    else
                        sb.AppendLine($"| {filterPropertyPath} | — | — | NOT FOUND |");
                }
                else
                {
                    var iterator = so.GetIterator();
                    bool enterChildren = true;
                    while (iterator.NextVisible(enterChildren))
                    {
                        enterChildren = false;
                        sb.AppendLine($"| {iterator.propertyPath} | {iterator.displayName} | {iterator.type} | {GetPropertyValue(iterator)} |");
                    }
                }

                sb.AppendLine();
            }

            sb.AppendLine("---");
            sb.Append($"Inspected: {inspectedCount} GameObjects | Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            return sb.ToString();
        }

        // ─────────────────────────────────────────────────────
        //  Mode E: Prefab Overrides
        // ─────────────────────────────────────────────────────

        private static string BuildPrefabOverrides(List<GameObject> targets, string componentTypeFilter)
        {
            var sb = new StringBuilder();
            string scopeLabel = "ALL";

            sb.AppendLine($"# Component Inspector — Mode: Prefab Overrides | Scope: {scopeLabel}");
            sb.AppendLine();

            int reportedCount = 0;
            foreach (GameObject go in targets)
            {
                if (!PrefabUtility.IsPartOfPrefabInstance(go)) continue;

                string goPath = PathResolver.GetHierarchyPath(go.transform);
                string prefabName = PrefabUtility.GetCorrespondingObjectFromSource(go)?.name ?? "Unknown";

                // Property-level modifications
                var modifications = PrefabUtility.GetPropertyModifications(go);

                // Filter to just this component type if specified
                var filteredMods = new List<PropertyModification>();
                var transformMods = new List<PropertyModification>();

                if (modifications != null)
                {
                    foreach (var mod in modifications)
                    {
                        bool isTransformProp = mod.propertyPath.StartsWith("m_LocalPosition", StringComparison.Ordinal)
                            || mod.propertyPath.StartsWith("m_LocalRotation", StringComparison.Ordinal)
                            || mod.propertyPath.StartsWith("m_LocalScale", StringComparison.Ordinal);

                        if (isTransformProp)
                        {
                            transformMods.Add(mod);
                            continue;
                        }

                        if (!string.IsNullOrEmpty(componentTypeFilter) && mod.target != null
                            && mod.target.GetType().Name != componentTypeFilter)
                            continue;

                        filteredMods.Add(mod);
                    }
                }

                var addedComponents = PrefabUtility.GetAddedComponents(go);
                var removedComponents = PrefabUtility.GetRemovedComponents(go);

                if (filteredMods.Count == 0 && addedComponents.Count == 0
                    && removedComponents.Count == 0 && transformMods.Count == 0)
                    continue;

                reportedCount++;
                sb.AppendLine($"## {goPath} (Prefab: {prefabName})");
                sb.AppendLine();

                // Property overrides
                sb.AppendLine("### Property Overrides");
                if (filteredMods.Count == 0)
                {
                    sb.AppendLine("*None.*");
                }
                else
                {
                    sb.AppendLine("| Component | Property Path | Instance Value |");
                    sb.AppendLine("| :--- | :--- | :--- |");
                    // TODO: Add prefab source value comparison (requires SerializedObject on prefab asset component)
                    foreach (var mod in filteredMods)
                    {
                        string compName = mod.target != null ? mod.target.GetType().Name : "Unknown";
                        string val = mod.objectReference != null
                            ? $"{mod.objectReference.name} ({mod.objectReference.GetType().Name})"
                            : mod.value;
                        sb.AppendLine($"| {compName} | {mod.propertyPath} | {val} |");
                    }
                }
                sb.AppendLine();

                // Added components
                sb.AppendLine("### Added Components");
                if (addedComponents.Count == 0)
                    sb.AppendLine("*None.*");
                else
                    foreach (var ac in addedComponents)
                        sb.AppendLine($"- [+] {ac.instanceComponent?.GetType().Name ?? "Unknown"}");
                sb.AppendLine();

                // Removed components
                sb.AppendLine("### Removed Components");
                if (removedComponents.Count == 0)
                    sb.AppendLine("*None.*");
                else
                    foreach (var rc in removedComponents)
                        sb.AppendLine($"- [-] {rc.assetComponent?.GetType().Name ?? "Unknown"}");
                sb.AppendLine();

                // Transform overrides
                if (transformMods.Count > 0)
                {
                    sb.AppendLine("### Transform Overrides (position/rotation/scale)");
                    foreach (var mod in transformMods)
                        sb.AppendLine($"- {mod.propertyPath}: {mod.value}");
                    sb.AppendLine();
                }
            }

            if (reportedCount == 0)
                sb.AppendLine("*No prefab instances with overrides found in scope.*");

            sb.AppendLine("---");
            sb.Append($"Prefab instances with overrides: {reportedCount} | Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            return sb.ToString();
        }

        // ─────────────────────────────────────────────────────
        //  Mode F: Missing References
        // ─────────────────────────────────────────────────────

        private static string BuildMissingReferences(List<GameObject> targets, string componentTypeFilter, bool includeUnassigned)
        {
            var missingRefs = new List<(string goPath, string compName, string propPath, string expectedType)>();
            var missingScripts = new List<(string goPath, int compIndex)>();
            var unassignedRefs = new List<(string goPath, string compName, string propPath, string expectedType)>();

            foreach (GameObject go in targets)
            {
                string goPath = PathResolver.GetHierarchyPath(go.transform);
                Component[] components = go.GetComponents<Component>();

                for (int ci = 0; ci < components.Length; ci++)
                {
                    Component comp = components[ci];
                    if (comp == null)
                    {
                        missingScripts.Add((goPath, ci));
                        continue;
                    }

                    if (!string.IsNullOrEmpty(componentTypeFilter) && comp.GetType().Name != componentTypeFilter)
                        continue;

                    var so = new SerializedObject(comp);
                    var iterator = so.GetIterator();
                    bool enterChildren = true;
                    while (iterator.NextVisible(enterChildren))
                    {
                        enterChildren = false;
                        if (iterator.propertyType != SerializedPropertyType.ObjectReference) continue;

                        if (iterator.objectReferenceValue == null && iterator.objectReferenceInstanceIDValue != 0)
                        {
                            // Genuinely missing — was assigned but target was destroyed/deleted
                            missingRefs.Add((goPath, comp.GetType().Name, iterator.propertyPath, iterator.type));
                        }
                        else if (includeUnassigned && iterator.objectReferenceValue == null
                            && iterator.objectReferenceInstanceIDValue == 0)
                        {
                            // Never linked — intentionally empty, but caller wants to see it
                            unassignedRefs.Add((goPath, comp.GetType().Name, iterator.propertyPath, iterator.type));
                        }
                    }
                }
            }

            var sb = new StringBuilder();
            sb.AppendLine("# Component Inspector — Mode: Missing References | Scope: ALL");
            sb.AppendLine();

            // Missing references table
            sb.AppendLine("## Missing References (target was deleted or moved)");
            if (missingRefs.Count == 0)
            {
                sb.AppendLine("*None.*");
            }
            else
            {
                sb.AppendLine("| GameObject Path | Component | Property Path | Expected Type |");
                sb.AppendLine("| :--- | :--- | :--- | :--- |");
                foreach (var (goPath, compName, propPath, expectedType) in missingRefs)
                    sb.AppendLine($"| {goPath} | {compName} | {propPath} | {expectedType} |");
            }
            sb.AppendLine();

            // Missing scripts table
            sb.AppendLine("## Missing Scripts (component script was deleted)");
            if (missingScripts.Count == 0)
            {
                sb.AppendLine("*None.*");
            }
            else
            {
                sb.AppendLine("| GameObject Path | Component Index |");
                sb.AppendLine("| :--- | :--- |");
                foreach (var (goPath, idx) in missingScripts)
                    sb.AppendLine($"| {goPath} | Component[{idx}] |");
            }

            // Unassigned references section (only when includeUnassigned = true)
            if (includeUnassigned)
            {
                sb.AppendLine();
                sb.AppendLine("## Unassigned References (never linked — may need wiring)");
                if (unassignedRefs.Count == 0)
                {
                    sb.AppendLine("*None.*");
                }
                else
                {
                    sb.AppendLine("| GameObject Path | Component | Property Path | Expected Type |");
                    sb.AppendLine("| :--- | :--- | :--- | :--- |");
                    foreach (var (goPath, compName, propPath, expectedType) in unassignedRefs)
                        sb.AppendLine($"| {goPath} | {compName} | {propPath} | {expectedType} |");
                }
            }

            sb.AppendLine();
            sb.AppendLine("---");
            sb.Append($"Missing references: {missingRefs.Count} | Missing scripts: {missingScripts.Count} | Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            return sb.ToString();
        }

        // ─────────────────────────────────────────────────────
        //  Mode D: Cross-Object Comparison
        // ─────────────────────────────────────────────────────

        private static string BuildCrossObjectComparison(List<GameObject> targets, string componentType)
        {
            var sb = new StringBuilder();

            if (string.IsNullOrEmpty(componentType))
            {
                sb.AppendLine("# Component Inspector — Mode: Cross-Object Comparison");
                sb.AppendLine();
                sb.AppendLine("ERROR: componentType is required for Cross-Object Comparison mode.");
                return sb.ToString();
            }

            Type resolvedType = ResolveComponentType(componentType);

            // Collect all matching GameObjects and their property values
            var objectData = new List<(string path, Dictionary<string, string> values)>();

            foreach (GameObject go in targets)
            {
                Component comp = resolvedType != null
                    ? go.GetComponent(resolvedType)
                    : go.GetComponent(componentType);

                if (comp == null) continue;

                var so = new SerializedObject(comp);
                var values = new Dictionary<string, string>();
                var iterator = so.GetIterator();
                bool enter = true;
                while (iterator.NextVisible(enter))
                {
                    enter = false;
                    values[iterator.propertyPath] = GetPropertyValue(iterator.Copy());
                }
                objectData.Add((PathResolver.GetHierarchyPath(go.transform), values));
            }

            sb.AppendLine($"# Component Inspector — Mode: Cross-Object Comparison | Component: {componentType} | Objects: {objectData.Count}");
            sb.AppendLine();

            if (objectData.Count == 0)
            {
                sb.AppendLine($"*No GameObjects with component '{componentType}' found in scope.*");
                sb.AppendLine("---");
                sb.Append($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                return sb.ToString();
            }

            if (objectData.Count == 1)
            {
                sb.AppendLine("*Only one object found — cross-object comparison requires at least 2 objects.*");
                sb.AppendLine("---");
                sb.Append($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                return sb.ToString();
            }

            // Collect all property paths
            var allPaths = new LinkedList<string>();
            var pathSet = new HashSet<string>();
            foreach (var (_, values) in objectData)
            {
                foreach (string p in values.Keys)
                {
                    if (pathSet.Add(p))
                        allPaths.AddLast(p);
                }
            }

            // Classify divergent vs uniform
            var divergent = new List<string>();
            var uniform = new List<string>();
            foreach (string propPath in allPaths)
            {
                string first = null;
                bool differs = false;
                foreach (var (_, values) in objectData)
                {
                    string val = values.TryGetValue(propPath, out string v) ? v : "(missing)";
                    if (first == null) first = val;
                    else if (val != first) { differs = true; break; }
                }
                if (differs) divergent.Add(propPath);
                else uniform.Add(propPath);
            }

            bool wideFormat = objectData.Count <= 6;

            // Divergent section
            sb.AppendLine("## Divergent Properties (values differ)");
            if (divergent.Count == 0)
            {
                sb.AppendLine("*All properties are identical across all objects.*");
            }
            else if (wideFormat)
            {
                // Wide table header
                sb.Append("| Property |");
                foreach (var (path, _) in objectData)
                {
                    // Use last segment of path as column header
                    string col = path.Contains('/') ? path.Substring(path.LastIndexOf('/') + 1) : path;
                    sb.Append($" {col} |");
                }
                sb.AppendLine();
                sb.Append("| :--- |");
                foreach (var _ in objectData) sb.Append(" :--- |");
                sb.AppendLine();

                foreach (string propPath in divergent)
                {
                    sb.Append($"| {propPath} |");
                    foreach (var (_, values) in objectData)
                    {
                        string val = values.TryGetValue(propPath, out string v) ? v : "(missing)";
                        sb.Append($" {val} |");
                    }
                    sb.AppendLine();
                }
            }
            else
            {
                // Vertical format for >6 objects
                foreach (string propPath in divergent)
                {
                    sb.AppendLine($"### {propPath}");
                    sb.AppendLine("| Object | Value |");
                    sb.AppendLine("| :--- | :--- |");
                    foreach (var (path, values) in objectData)
                    {
                        string val = values.TryGetValue(propPath, out string v) ? v : "(missing)";
                        sb.AppendLine($"| {path} | {val} |");
                    }
                    sb.AppendLine();
                }
            }
            sb.AppendLine();

            // Uniform section
            sb.AppendLine("## Uniform Properties (all same)");
            if (uniform.Count == 0)
            {
                sb.AppendLine("*No uniform properties.*");
            }
            else
            {
                sb.AppendLine("| Property | Value |");
                sb.AppendLine("| :--- | :--- |");
                foreach (string propPath in uniform)
                {
                    string val = objectData[0].values.TryGetValue(propPath, out string v) ? v : "(missing)";
                    sb.AppendLine($"| {propPath} | {val} |");
                }
            }

            sb.AppendLine();
            sb.AppendLine("---");
            sb.Append($"Objects compared: {objectData.Count} | Divergent properties: {divergent.Count} | Uniform properties: {uniform.Count} | Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            return sb.ToString();
        }

        // ─────────────────────────────────────────────────────
        //  Editor Menu Items
        // ─────────────────────────────────────────────────────

        [MenuItem("Axiom/AgentBridge/Component Inspector — Mode A (Existence Check)")]
        public static void MenuModeA()
        {
            string path = GenerateReport(ComponentInspectorMode.ExistenceCheck, componentType: "Camera");
            Debug.Log($"[AgentBridge] Component Inspector report: {path}");
        }

        [MenuItem("Axiom/AgentBridge/Component Inspector — Mode B (Property List)")]
        public static void MenuModeB()
        {
            string path = GenerateReport(ComponentInspectorMode.PropertyList, componentType: "Camera");
            Debug.Log($"[AgentBridge] Component Inspector report: {path}");
        }

        [MenuItem("Axiom/AgentBridge/Component Inspector — Mode C (Property Values)")]
        public static void MenuModeC()
        {
            string path = GenerateReport(ComponentInspectorMode.PropertyValues, componentType: "Camera");
            Debug.Log($"[AgentBridge] Component Inspector report: {path}");
        }

        [MenuItem("Axiom/AgentBridge/Component Inspector — Mode F (Missing References, All)")]
        public static void MenuModeF()
        {
            string path = GenerateReport(ComponentInspectorMode.MissingReferences);
            Debug.Log($"[AgentBridge] Component Inspector report: {path}");
        }
    }
}
