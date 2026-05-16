using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEditor;
using Axiom.Editor.AgentBridge.Core;
using static Axiom.Editor.AgentBridge.Core.SerializedPropertyHelper;

namespace Axiom.Editor.AgentBridge.Diagnostics
{
    /// <summary>
    /// Generates hierarchy reports for the active scene(s), replacing expensive MCP hierarchy browsing.
    /// </summary>
    public static class HierarchyLens
    {
        /// <summary>
        /// Level of detail for the hierarchy report.
        /// </summary>
        public enum HierarchyMode
        {
            /// <summary>Names only — one line per GameObject.</summary>
            Structure,
            /// <summary>Names + component types listed below each GameObject.</summary>
            Components,
            /// <summary>Names + components + all public/serialized field values.</summary>
            ComponentState,
            /// <summary>One specified component type's values across all matching GameObjects.</summary>
            SingleComponentFocus,
            /// <summary>Names + local/world transform data.</summary>
            TransformDetail,
            /// <summary>Everything: names, active status, tag, layer, static flags, all components + all property values.</summary>
            FullInspectorDump
        }

        // ─────────────────────────────────────────────────────
        //  Public API
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// Generates a hierarchy report for the active scene(s).
        /// </summary>
        /// <param name="mode">Detail level: Structure (names only) or Components (names + component types).</param>
        /// <param name="rootPath">Optional breadcrumb path to scope the report (e.g., "Managers/PlayerSystems").
        /// If null or empty, reports on all root GameObjects in all loaded scenes.</param>
        /// <param name="maxDepth">Maximum recursion depth from the root. -1 means unlimited. 0 means root only.</param>
        /// <param name="tagFilter">If set, only include GameObjects with this tag.</param>
        /// <param name="layerFilter">If set, only include GameObjects on this layer (layer name, not number).</param>
        /// <param name="componentFilter">If set, only include GameObjects that have this component type name attached.</param>
        /// <param name="includeInactive">Whether to include inactive GameObjects. Default false.</param>
        /// <returns>The file path of the generated report.</returns>
        public static string GenerateReport(
            HierarchyMode mode,
            string rootPath = null,
            int maxDepth = -1,
            string tagFilter = null,
            string layerFilter = null,
            string componentFilter = null,
            bool includeInactive = false)
        {
            // Mode D requires componentFilter — validate early
            if (mode == HierarchyMode.SingleComponentFocus && string.IsNullOrEmpty(componentFilter))
            {
                Debug.LogWarning("[AgentBridge] HierarchyLens Mode D (SingleComponentFocus) requires componentFilter to be set.");
                string errContent = "# Hierarchy Report — Mode: Single Component Focus\n\n**ERROR:** componentFilter is required for Mode D.";
                return OutputWriter.WriteReport("hierarchy_lens", errContent);
            }

            var sb = new StringBuilder();
            int totalCount = 0;
            int layerFilterInt = -1;

            // Resolve layer filter to int once
            if (!string.IsNullOrEmpty(layerFilter))
            {
                layerFilterInt = LayerMask.NameToLayer(layerFilter);
                if (layerFilterInt == -1)
                    Debug.LogWarning($"[AgentBridge] HierarchyLens: Layer \"{layerFilter}\" not found. Filter will match nothing.");
            }

            // Build filter description for header
            var activeFilters = new List<string>();
            if (!string.IsNullOrEmpty(tagFilter))        activeFilters.Add($"Tag={tagFilter}");
            if (!string.IsNullOrEmpty(layerFilter))      activeFilters.Add($"Layer={layerFilter}");
            if (!string.IsNullOrEmpty(componentFilter))  activeFilters.Add($"Component={componentFilter}");
            if (!includeInactive)                        activeFilters.Add("ActiveOnly");
            string filtersLabel = activeFilters.Count > 0 ? string.Join(", ", activeFilters) : "None";

            string depthLabel = maxDepth < 0 ? "Unlimited" : maxDepth.ToString();
            string scopeLabel  = string.IsNullOrEmpty(rootPath) ? "ALL" : rootPath;
            string scenesLabel = PathResolver.GetLoadedSceneNames();

            // Mode D has its own report builder
            if (mode == HierarchyMode.SingleComponentFocus)
            {
                string content = BuildSingleComponentFocus(rootPath, componentFilter, maxDepth, includeInactive, scopeLabel);
                return OutputWriter.WriteReport("hierarchy_lens", content);
            }

            // Header
            sb.AppendLine($"# Hierarchy Report — Mode: {mode} | Scope: {scopeLabel} | Scene: {scenesLabel} | Depth: {depthLabel} | Filters: {filtersLabel}");
            sb.AppendLine();

            // Resolve root objects via PathResolver (handles both scoped and full-scene cases)
            if (!PathResolver.ResolveRootPath(rootPath, out var roots))
            {
                sb.AppendLine($"ERROR: Could not resolve path: {rootPath}");
            }
            else
            {
                foreach (GameObject rootGO in roots)
                {
                    WalkTransform(rootGO.transform, mode, 0, maxDepth,
                        tagFilter, layerFilterInt, componentFilter, includeInactive,
                        sb, ref totalCount);
                }
            }

            // Footer
            sb.AppendLine();
            sb.AppendLine("---");
            sb.Append($"Total GameObjects: {totalCount} | Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");

            return OutputWriter.WriteReport("hierarchy_lens", sb.ToString());
        }

        // ─────────────────────────────────────────────────────
        //  Editor Menu Items
        // ─────────────────────────────────────────────────────

        [MenuItem("Axiom/AgentBridge/Hierarchy Lens — Mode A (Structure, All)")]
        public static void RunModeAAll()
        {
            string path = GenerateReport(HierarchyMode.Structure);
            Debug.Log($"[AgentBridge] Hierarchy Lens report: {path}");
        }

        [MenuItem("Axiom/AgentBridge/Hierarchy Lens — Mode B (Components, All)")]
        public static void RunModeBAll()
        {
            string path = GenerateReport(HierarchyMode.Components);
            Debug.Log($"[AgentBridge] Hierarchy Lens report: {path}");
        }

        [MenuItem("Axiom/AgentBridge/Hierarchy Lens — Mode C (Component State, All)")]
        public static void RunModeCAll()
        {
            string path = GenerateReport(HierarchyMode.ComponentState);
            Debug.Log($"[AgentBridge] Hierarchy Lens report: {path}");
        }

        [MenuItem("Axiom/AgentBridge/Hierarchy Lens — Mode E (Transform Detail, All)")]
        public static void RunModeEAll()
        {
            string path = GenerateReport(HierarchyMode.TransformDetail);
            Debug.Log($"[AgentBridge] Hierarchy Lens report: {path}");
        }

        [MenuItem("Axiom/AgentBridge/Hierarchy Lens — Mode F (Full Inspector Dump, All)")]
        public static void RunModeFAll()
        {
            string path = GenerateReport(HierarchyMode.FullInspectorDump);
            Debug.Log($"[AgentBridge] Hierarchy Lens report: {path}");
        }

        // ─────────────────────────────────────────────────────
        //  Private Helpers
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// Recursively walks a transform, writing lines to sb and counting included GameObjects.
        /// </summary>
        private static void WalkTransform(
            Transform t,
            HierarchyMode mode,
            int depth,
            int maxDepth,
            string tagFilter,
            int layerFilterInt,
            string componentFilter,
            bool includeInactive,
            StringBuilder sb,
            ref int count)
        {
            GameObject go = t.gameObject;

            // --- Filtering ---
            if (!includeInactive && !go.activeInHierarchy)
                return;

            if (!string.IsNullOrEmpty(tagFilter) && !go.CompareTag(tagFilter))
                return;

            if (layerFilterInt >= 0 && go.layer != layerFilterInt)
                return;

            if (!string.IsNullOrEmpty(componentFilter) && mode != HierarchyMode.ComponentState
                && mode != HierarchyMode.TransformDetail && mode != HierarchyMode.FullInspectorDump)
            {
                bool found = false;
                foreach (Component c in go.GetComponents<Component>())
                {
                    if (c != null && c.GetType().Name == componentFilter)
                    {
                        found = true;
                        break;
                    }
                }
                if (!found) return;
            }

            // --- Passed all filters ---
            count++;
            string indent = new string(' ', depth * 2);

            switch (mode)
            {
                case HierarchyMode.Structure:
                    sb.AppendLine($"{indent}- [GO] {go.name}");
                    break;

                case HierarchyMode.Components:
                {
                    string layerName = LayerMask.LayerToName(go.layer);
                    string tag = go.tag;
                    sb.AppendLine($"{indent}- [GO] {go.name} (L:{layerName}, T:{tag})");

                    var compNames = new List<string>();
                    foreach (Component c in go.GetComponents<Component>())
                    {
                        if (c == null) compNames.Add("MISSING_SCRIPT");
                        else if (c.GetType() != typeof(Transform)) compNames.Add(c.GetType().Name);
                    }
                    if (compNames.Count > 0)
                        sb.AppendLine($"{indent}  > [C] {string.Join(", ", compNames)}");
                    break;
                }

                case HierarchyMode.ComponentState:
                {
                    string layerName = LayerMask.LayerToName(go.layer);
                    sb.AppendLine($"{indent}- [GO] {go.name} (L:{layerName}, T:{go.tag})");
                    foreach (Component comp in go.GetComponents<Component>())
                    {
                        if (comp == null) { sb.AppendLine($"{indent}  > [C] MISSING_SCRIPT"); continue; }
                        if (comp.GetType() == typeof(Transform)) continue;
                        sb.AppendLine($"{indent}  > [C] {comp.GetType().Name}");
                        var so = new SerializedObject(comp);
                        var iter = so.GetIterator();
                        bool enter = true;
                        while (iter.NextVisible(enter))
                        {
                            enter = false;
                            sb.AppendLine($"{indent}      {iter.propertyPath} ({iter.type}) = {GetPropertyValue(iter.Copy())}");
                        }
                    }
                    break;
                }

                case HierarchyMode.TransformDetail:
                {
                    sb.AppendLine($"{indent}- [GO] {go.name}");
                    Vector3 lp = t.localPosition;
                    Vector3 lr = t.localEulerAngles;
                    Vector3 ls = t.localScale;
                    Vector3 wp = t.position;
                    sb.AppendLine($"{indent}    Local Pos: ({lp.x:F2}, {lp.y:F2}, {lp.z:F2})");
                    sb.AppendLine($"{indent}    Local Rot: ({lr.x:F2}, {lr.y:F2}, {lr.z:F2})");
                    sb.AppendLine($"{indent}    Local Scl: ({ls.x:F2}, {ls.y:F2}, {ls.z:F2})");
                    sb.AppendLine($"{indent}    World Pos: ({wp.x:F2}, {wp.y:F2}, {wp.z:F2})");
                    break;
                }

                case HierarchyMode.FullInspectorDump:
                {
                    string layerName = LayerMask.LayerToName(go.layer);
                    string staticFlags = GameObjectUtility.GetStaticEditorFlags(go).ToString();
                    sb.AppendLine($"{indent}- [GO] {go.name}");
                    sb.AppendLine($"{indent}    Active: {go.activeInHierarchy} (self: {go.activeSelf}, hierarchy: {go.activeInHierarchy})");
                    sb.AppendLine($"{indent}    Tag: {go.tag} | Layer: {layerName}");
                    sb.AppendLine($"{indent}    Static: {staticFlags}");
                    foreach (Component comp in go.GetComponents<Component>())
                    {
                        if (comp == null) { sb.AppendLine($"{indent}  > [C] MISSING_SCRIPT"); continue; }
                        if (comp.GetType() == typeof(Transform)) continue;
                        string enabledTag = comp is Behaviour b ? (b.enabled ? "[Enabled]" : "[Disabled]") : "[N/A]";
                        sb.AppendLine($"{indent}  > [C] {comp.GetType().Name} {enabledTag}");
                        var so = new SerializedObject(comp);
                        var iter = so.GetIterator();
                        bool enter = true;
                        while (iter.NextVisible(enter))
                        {
                            enter = false;
                            sb.AppendLine($"{indent}      {iter.propertyPath} ({iter.type}) = {GetPropertyValue(iter.Copy())}");
                        }
                    }
                    break;
                }
            }

            // --- Recurse into children ---
            if (maxDepth < 0 || depth < maxDepth)
            {
                for (int i = 0; i < t.childCount; i++)
                {
                    WalkTransform(t.GetChild(i), mode, depth + 1, maxDepth,
                        tagFilter, layerFilterInt, componentFilter, includeInactive,
                        sb, ref count);
                }
            }
        }

        // ─────────────────────────────────────────────────────
        //  Mode D: Single Component Focus
        // ─────────────────────────────────────────────────────

        private static string BuildSingleComponentFocus(string rootPath, string componentFilter, int maxDepth, bool includeInactive, string scopeLabel)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"# Hierarchy Report — Mode: Single Component Focus | Component: {componentFilter} | Scope: {scopeLabel}");
            sb.AppendLine();

            if (!PathResolver.ResolveRootPath(rootPath, out var roots))
            {
                sb.AppendLine($"ERROR: Could not resolve path: {rootPath}");
                return sb.ToString();
            }

            // Collect all GameObjects
            var allGOs = new List<GameObject>();
            foreach (GameObject root in roots)
                CollectAll(root.transform, maxDepth, 0, includeInactive, allGOs);

            int matchCount = 0;
            foreach (GameObject go in allGOs)
            {
                Component comp = go.GetComponent(componentFilter);
                if (comp == null) continue;
                matchCount++;

                string goPath = PathResolver.GetHierarchyPath(go.transform);
                sb.AppendLine($"## {goPath} → {componentFilter}");
                sb.AppendLine("| Property | Type | Value |");
                sb.AppendLine("| :--- | :--- | :--- |");

                var so = new SerializedObject(comp);
                var iter = so.GetIterator();
                bool enter = true;
                while (iter.NextVisible(enter))
                {
                    enter = false;
                    sb.AppendLine($"| {iter.propertyPath} | {iter.type} | {GetPropertyValue(iter.Copy())} |");
                }
                sb.AppendLine();
            }

            if (matchCount == 0)
                sb.AppendLine($"*No GameObjects with component '{componentFilter}' found in scope.*");

            sb.AppendLine("---");
            sb.Append($"GameObjects with {componentFilter}: {matchCount} | Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            return sb.ToString();
        }

        private static void CollectAll(Transform t, int maxDepth, int depth, bool includeInactive, List<GameObject> results)
        {
            if (!includeInactive && !t.gameObject.activeInHierarchy) return;
            results.Add(t.gameObject);
            if (maxDepth >= 0 && depth >= maxDepth) return;
            for (int i = 0; i < t.childCount; i++)
                CollectAll(t.GetChild(i), maxDepth, depth + 1, includeInactive, results);
        }
    }
}
