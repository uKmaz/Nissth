using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor;
using UnityEditor.UIElements;
using Axiom.Editor.AgentBridge.Core;

namespace Axiom.Editor.AgentBridge.Diagnostics
{
    /// <summary>
    /// Inspects UI Toolkit visual trees, styles, data bindings, UXML/USS file structure,
    /// and accessibility for the agent to understand and debug UI.
    /// </summary>
    public static class UIToolkitInspector
    {
        public enum UIToolkitInspectorMode
        {
            VisualTreeStructure,    // Mode A
            StyleAudit,             // Mode B
            BindingReport,          // Mode C
            UxmlUssFileMap,         // Mode D
            AccessibilityAudit      // Mode E
        }

        /// <summary>
        /// Inspects UI Toolkit elements.
        /// </summary>
        /// <param name="mode">Inspection type.</param>
        /// <param name="targetWindow">Type name of an open EditorWindow to inspect. Null = scene UIDocuments.</param>
        /// <param name="uxmlPath">For Modes C, D: path to a specific UXML file. Null = scan all.</param>
        /// <param name="assetPath">Folder to scope file scanning. Null = "Assets".</param>
        /// <param name="maxDepth">Visual tree recursion depth. -1 = unlimited.</param>
        /// <returns>File path of the generated report.</returns>
        public static string GenerateReport(
            UIToolkitInspectorMode mode,
            string targetWindow = null,
            string uxmlPath = null,
            string assetPath = null,
            int maxDepth = -1)
        {
            var sb = new StringBuilder();
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            string searchPath = assetPath ?? "Assets";

            switch (mode)
            {
                case UIToolkitInspectorMode.VisualTreeStructure:
                    BuildVisualTreeStructure(sb, targetWindow, maxDepth, timestamp);
                    break;
                case UIToolkitInspectorMode.StyleAudit:
                    BuildStyleAudit(sb, targetWindow, maxDepth, timestamp);
                    break;
                case UIToolkitInspectorMode.BindingReport:
                    BuildBindingReport(sb, uxmlPath, searchPath, timestamp);
                    break;
                case UIToolkitInspectorMode.UxmlUssFileMap:
                    BuildUxmlUssFileMap(sb, searchPath, timestamp);
                    break;
                case UIToolkitInspectorMode.AccessibilityAudit:
                    BuildAccessibilityAudit(sb, targetWindow, maxDepth, timestamp);
                    break;
            }

            string reportName = $"uitoolkit_inspector_{mode.ToString().ToLower()}_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.md";
            return OutputWriter.WriteReport(reportName, sb.ToString());
        }

        // ─── Mode A: Visual Tree Structure ───────────────────────────────────────

        private static void BuildVisualTreeStructure(StringBuilder sb, string targetWindow, int maxDepth, string timestamp)
        {
            sb.AppendLine("# UI Toolkit Inspector — Mode: Visual Tree Structure | Source: Scene UIDocument");
            sb.AppendLine();

            int totalDocuments = 0;
            int totalElements = 0;

            if (targetWindow != null)
            {
                // Inspect a specific open EditorWindow
                var allWindows = Resources.FindObjectsOfTypeAll<EditorWindow>();
                EditorWindow found = null;
                foreach (var window in allWindows)
                {
                    if (window.GetType().Name == targetWindow)
                    {
                        found = window;
                        break;
                    }
                }

                if (found == null)
                {
                    sb.AppendLine($"*EditorWindow '{targetWindow}' is not currently open.*");
                }
                else
                {
                    var rootElement = found.rootVisualElement;
                    if (rootElement != null)
                    {
                        totalDocuments++;
                        sb.AppendLine($"## EditorWindow: {found.GetType().Name}");
                        int count = 0;
                        TraverseVisualTree(rootElement, 0, sb, maxDepth, ref count);
                        totalElements += count;
                        sb.AppendLine();
                    }
                }
            }
            else
            {
                // Find UIDocument components in the scene
                var uiDocuments = UnityEngine.Object.FindObjectsByType<UIDocument>(FindObjectsSortMode.None);

                if (uiDocuments.Length == 0)
                {
                    sb.AppendLine("*No UIDocument components found in the active scene. Use targetWindow parameter to inspect an EditorWindow.*");
                    sb.AppendLine();
                    sb.AppendLine("---");
                    sb.AppendLine($"UIDocuments found: 0 | Total elements: 0 | Generated: {timestamp}");
                    return;
                }

                foreach (var uiDoc in uiDocuments)
                {
                    var rootElement = uiDoc.rootVisualElement;
                    if (rootElement == null) continue;

                    totalDocuments++;
                    string uxmlName = uiDoc.visualTreeAsset != null
                        ? AssetDatabase.GetAssetPath(uiDoc.visualTreeAsset)
                        : "(no asset)";

                    sb.AppendLine($"## UIDocument on \"{uiDoc.gameObject.name}\" ({uxmlName})");
                    int count = 0;
                    TraverseVisualTree(rootElement, 0, sb, maxDepth, ref count);
                    totalElements += count;
                    sb.AppendLine();
                }
            }

            sb.AppendLine("---");
            sb.AppendLine($"UIDocuments found: {totalDocuments} | Total elements: {totalElements} | Generated: {timestamp}");
        }

        private static void TraverseVisualTree(VisualElement element, int depth, StringBuilder sb, int maxDepth, ref int count)
        {
            if (maxDepth >= 0 && depth > maxDepth) return;

            count++;
            string indent = new string(' ', depth * 2);
            string typeName = element.GetType().Name;
            string name = string.IsNullOrEmpty(element.name) ? "(unnamed)" : element.name;
            var classes = element.GetClasses().ToList();
            string classStr = classes.Any() ? $" .{string.Join(" .", classes)}" : "";

            sb.AppendLine($"{indent}- [{typeName}] {name}{classStr}");

            foreach (var child in element.Children())
                TraverseVisualTree(child, depth + 1, sb, maxDepth, ref count);
        }

        // ─── Mode B: Style Audit ──────────────────────────────────────────────────

        private static void BuildStyleAudit(StringBuilder sb, string targetWindow, int maxDepth, string timestamp)
        {
            sb.AppendLine("# UI Toolkit Inspector — Mode: Style Audit | Source: Scene UIDocument");
            sb.AppendLine();

            var roots = new List<(string label, VisualElement root)>();

            if (targetWindow != null)
            {
                var allWindows = Resources.FindObjectsOfTypeAll<EditorWindow>();
                foreach (var window in allWindows)
                {
                    if (window.GetType().Name == targetWindow && window.rootVisualElement != null)
                    {
                        roots.Add(($"EditorWindow: {window.GetType().Name}", window.rootVisualElement));
                        break;
                    }
                }
            }
            else
            {
                var uiDocuments = UnityEngine.Object.FindObjectsByType<UIDocument>(FindObjectsSortMode.None);
                foreach (var doc in uiDocuments)
                {
                    if (doc.rootVisualElement != null)
                        roots.Add(($"UIDocument on \"{doc.gameObject.name}\"", doc.rootVisualElement));
                }
            }

            if (roots.Count == 0)
            {
                sb.AppendLine("*No live UI found. Style audit requires UIDocuments in the active scene or an open EditorWindow.*");
                sb.AppendLine();
                sb.AppendLine("---");
                sb.AppendLine($"Generated: {timestamp}");
                return;
            }

            int totalAudited = 0;
            var classUsageCounts = new Dictionary<string, int>();

            foreach (var (label, root) in roots)
            {
                sb.AppendLine($"## {label}");
                sb.AppendLine();

                var elements = new List<VisualElement>();
                CollectElements(root, elements, maxDepth, 0);

                foreach (var el in elements)
                {
                    var classes = el.GetClasses().ToList();
                    bool hasName = !string.IsNullOrEmpty(el.name);
                    if (!hasName && classes.Count == 0) continue; // Skip anonymous containers

                    totalAudited++;
                    foreach (var cls in classes)
                        classUsageCounts[cls] = classUsageCounts.GetValueOrDefault(cls) + 1;

                    string typeName = el.GetType().Name;
                    string nameStr = hasName ? el.name : "(unnamed)";
                    string classStr = classes.Any() ? $" .{string.Join(" .", classes)}" : "";

                    sb.AppendLine($"### [{typeName}] {nameStr}{classStr}");
                    sb.AppendLine("| Property | Value |");
                    sb.AppendLine("| :--- | :--- |");

                    var rs = el.resolvedStyle;
                    AppendStyleIfSet(sb, "Display", rs.display.ToString());
                    AppendStyleIfSet(sb, "Visibility", rs.visibility.ToString());
                    AppendStyleIfSet(sb, "Opacity", rs.opacity != 1f ? rs.opacity.ToString("F2") : null);
                    AppendStyleIfSet(sb, "Width", FormatLength(rs.width));
                    AppendStyleIfSet(sb, "Height", FormatLength(rs.height));
                    AppendStyleIfSet(sb, "Min Width", FormatLength(rs.minWidth));
                    AppendStyleIfSet(sb, "Min Height", FormatLength(rs.minHeight));
                    AppendStyleIfSet(sb, "Max Width", FormatLength(rs.maxWidth));
                    AppendStyleIfSet(sb, "Max Height", FormatLength(rs.maxHeight));
                    AppendStyleIfNonZero(sb, "Margin", rs.marginTop, rs.marginRight, rs.marginBottom, rs.marginLeft);
                    AppendStyleIfNonZero(sb, "Padding", rs.paddingTop, rs.paddingRight, rs.paddingBottom, rs.paddingLeft);
                    AppendStyleIfSet(sb, "Flex Direction", rs.flexDirection != FlexDirection.Row ? rs.flexDirection.ToString() : null);
                    AppendStyleIfSet(sb, "Flex Grow", rs.flexGrow > 0 ? rs.flexGrow.ToString("F1") : null);
                    AppendStyleIfSet(sb, "Flex Shrink", rs.flexShrink != 1f ? rs.flexShrink.ToString("F1") : null);
                    AppendStyleIfSet(sb, "Align Items", rs.alignItems != Align.Stretch ? rs.alignItems.ToString() : null);
                    AppendStyleIfSet(sb, "Justify Content", rs.justifyContent != Justify.FlexStart ? rs.justifyContent.ToString() : null);
                    AppendStyleIfSet(sb, "Position", rs.position != Position.Relative ? rs.position.ToString() : null);
                    AppendStyleIfSet(sb, "Background Color", FormatColor(rs.backgroundColor));
                    AppendStyleIfSet(sb, "Color", FormatColor(rs.color));
                    AppendStyleIfSet(sb, "Font Size", rs.fontSize > 0 ? $"{rs.fontSize}px" : null);
                    sb.AppendLine();
                }
            }

            // USS Classes summary
            if (classUsageCounts.Count > 0)
            {
                sb.AppendLine("## USS Classes in Use");
                sb.AppendLine("| Class | Used By (count) |");
                sb.AppendLine("| :--- | :--- |");
                foreach (var kvp in classUsageCounts.OrderByDescending(k => k.Value))
                    sb.AppendLine($"| .{kvp.Key} | {kvp.Value} element{(kvp.Value != 1 ? "s" : "")} |");
                sb.AppendLine();
            }

            sb.AppendLine("---");
            sb.AppendLine($"Elements audited: {totalAudited} | USS classes: {classUsageCounts.Count} | Generated: {timestamp}");
        }

        private static void CollectElements(VisualElement el, List<VisualElement> result, int maxDepth, int depth)
        {
            if (maxDepth >= 0 && depth > maxDepth) return;
            result.Add(el);
            foreach (var child in el.Children())
                CollectElements(child, result, maxDepth, depth + 1);
        }

        private static void AppendStyleIfSet(StringBuilder sb, string label, string value)
        {
            if (!string.IsNullOrEmpty(value))
                sb.AppendLine($"| {label} | {value} |");
        }

        private static void AppendStyleIfNonZero(StringBuilder sb, string label, float top, float right, float bottom, float left)
        {
            if (top != 0 || right != 0 || bottom != 0 || left != 0)
                sb.AppendLine($"| {label} | {top}px {right}px {bottom}px {left}px |");
        }

        private static string FormatLength(float value)
        {
            return value > 0 ? $"{value}px" : null;
        }

        private static string FormatLength(StyleFloat value)
        {
            return value.value > 0 ? $"{value.value}px" : null;
        }

        private static string FormatColor(Color c)
        {
            if (c == Color.clear || c == default) return null;
            return $"rgba({(int)(c.r * 255)}, {(int)(c.g * 255)}, {(int)(c.b * 255)}, {c.a:F2})";
        }

        // ─── Mode C: Binding Report ───────────────────────────────────────────────

        private static void BuildBindingReport(StringBuilder sb, string uxmlPath, string searchPath, string timestamp)
        {
            sb.AppendLine($"# UI Toolkit Inspector — Mode: Binding Report | Path: {searchPath}");
            sb.AppendLine();

            string[] uxmlGuids = uxmlPath != null
                ? new[] { AssetDatabase.AssetPathToGUID(uxmlPath) }
                : AssetDatabase.FindAssets("t:VisualTreeAsset", new[] { searchPath });

            int filesScanned = 0;
            int totalBindings = 0;

            foreach (string guid in uxmlGuids)
            {
                if (string.IsNullOrEmpty(guid)) continue;
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string fullPath = Path.Combine(Path.GetDirectoryName(Application.dataPath), path);
                if (!File.Exists(fullPath)) continue;

                string content;
                try { content = File.ReadAllText(fullPath); }
                catch { continue; }

                filesScanned++;

                // Find binding-path attributes
                var bindingMatches = Regex.Matches(content, @"binding-path\s*=\s*""([^""]+)""");
                // Find data-source attributes (Unity 6 runtime bindings)
                var dataSourceMatches = Regex.Matches(content, @"data-source(?:-path|-type)?\s*=\s*""([^""]+)""");
                // Find element names/ids for context
                var elementMatches = Regex.Matches(content, @"name\s*=\s*""([^""]+)""[^>]*binding-path\s*=\s*""([^""]+)""");

                if (bindingMatches.Count == 0 && dataSourceMatches.Count == 0) continue;

                sb.AppendLine($"### {path}");
                sb.AppendLine("| Element | Binding Path / Source | Attribute |");
                sb.AppendLine("| :--- | :--- | :--- |");

                // Try to extract element+binding pairs from full tag context
                var tagMatches = Regex.Matches(content,
                    @"<[^>]*?(?:name\s*=\s*""([^""]*)""|binding-path\s*=\s*""([^""]*)""|data-source(?:-path|-type)?\s*=\s*""([^""]*)"")[^>]*?>",
                    RegexOptions.Singleline);

                // Simpler approach: just list all binding-path values
                foreach (Match m in bindingMatches)
                {
                    sb.AppendLine($"| (element) | {m.Groups[1].Value} | binding-path |");
                    totalBindings++;
                }
                foreach (Match m in dataSourceMatches)
                {
                    sb.AppendLine($"| (element) | {m.Groups[1].Value} | data-source |");
                    totalBindings++;
                }
                sb.AppendLine();
            }

            if (totalBindings == 0)
            {
                sb.AppendLine("*No UXML binding declarations found. Bindings may be configured in code.*");
                sb.AppendLine();
            }

            sb.AppendLine("## Summary");
            sb.AppendLine($"- UXML files scanned: {filesScanned}");
            sb.AppendLine($"- Bindings found: {totalBindings}");
            sb.AppendLine();

            sb.AppendLine("---");
            sb.AppendLine($"Generated: {timestamp}");
        }

        // ─── Mode D: UXML/USS File Map ────────────────────────────────────────────

        private static void BuildUxmlUssFileMap(StringBuilder sb, string searchPath, string timestamp)
        {
            sb.AppendLine($"# UI Toolkit Inspector — Mode: UXML/USS File Map | Path: {searchPath}");
            sb.AppendLine();

            string[] uxmlGuids = AssetDatabase.FindAssets("t:VisualTreeAsset", new[] { searchPath });
            string[] ussGuids = AssetDatabase.FindAssets("t:StyleSheet", new[] { searchPath });

            if (uxmlGuids.Length == 0 && ussGuids.Length == 0)
            {
                sb.AppendLine("*No UXML or USS files found in the specified path.*");
                sb.AppendLine();
                sb.AppendLine("---");
                sb.AppendLine($"UXML files: 0 | USS files: 0 | Orphaned USS: 0 | Generated: {timestamp}");
                return;
            }

            // Build a set of all USS asset paths for orphan detection
            var allUssPaths = new HashSet<string>();
            foreach (string guid in ussGuids)
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                if (!string.IsNullOrEmpty(p)) allUssPaths.Add(p);
            }

            // Map: USS path → list of UXML files referencing it
            var ussReferencedBy = new Dictionary<string, List<string>>();

            // Map: UXML path → list of referenced USS paths
            var uxmlToUss = new Dictionary<string, List<string>>();

            foreach (string guid in uxmlGuids)
            {
                string uxmlAssetPath = AssetDatabase.GUIDToAssetPath(guid);
                string fullPath = Path.Combine(Path.GetDirectoryName(Application.dataPath), uxmlAssetPath);
                if (!File.Exists(fullPath)) continue;

                string content;
                try { content = File.ReadAllText(fullPath); }
                catch { continue; }

                // Match <Style src="..." /> or <engine:Style src="..." />
                var styleMatches = Regex.Matches(content, @"<(?:\w+:)?Style\s+src\s*=\s*""([^""]+)""");
                var referencedUss = new List<string>();

                foreach (Match m in styleMatches)
                {
                    string srcValue = m.Groups[1].Value;

                    // Normalize path: remove "project://database/" prefix
                    srcValue = Regex.Replace(srcValue, @"^project://database/", "");
                    // Remove GUID suffix if present: "Assets/foo.uss?fileID=..."
                    srcValue = Regex.Replace(srcValue, @"\?.*$", "");

                    // Resolve relative paths relative to the UXML file's directory
                    if (!srcValue.StartsWith("Assets/") && !srcValue.StartsWith("Packages/"))
                    {
                        string uxmlDir = Path.GetDirectoryName(uxmlAssetPath).Replace('\\', '/');
                        srcValue = $"{uxmlDir}/{srcValue}";
                    }

                    referencedUss.Add(srcValue);

                    if (!ussReferencedBy.ContainsKey(srcValue))
                        ussReferencedBy[srcValue] = new List<string>();
                    ussReferencedBy[srcValue].Add(Path.GetFileName(uxmlAssetPath));
                }

                uxmlToUss[uxmlAssetPath] = referencedUss;
            }

            // UXML to USS map
            sb.AppendLine("## UXML Files and Their Stylesheets");
            foreach (var kvp in uxmlToUss.OrderBy(k => k.Key))
            {
                sb.AppendLine($"### {kvp.Key}");
                if (kvp.Value.Count == 0)
                    sb.AppendLine("- *(no stylesheets referenced)*");
                else
                    foreach (var uss in kvp.Value)
                        sb.AppendLine($"- {uss}");
                sb.AppendLine();
            }

            // USS summary
            if (ussReferencedBy.Count > 0)
            {
                sb.AppendLine("## USS Files Summary");
                sb.AppendLine("| USS File Path | Referenced By |");
                sb.AppendLine("| :--- | :--- |");
                foreach (var kvp in ussReferencedBy.OrderBy(k => k.Key))
                    sb.AppendLine($"| {kvp.Key} | {string.Join(", ", kvp.Value)} |");
                sb.AppendLine();
            }

            // Orphaned USS
            var orphaned = allUssPaths
                .Where(p => !ussReferencedBy.ContainsKey(p))
                .OrderBy(p => p)
                .ToList();

            sb.AppendLine("## Orphaned USS Files (not referenced by any UXML)");
            if (orphaned.Count == 0)
            {
                sb.AppendLine("*None — all USS files are referenced.*");
            }
            else
            {
                sb.AppendLine("| USS File Path | Size |");
                sb.AppendLine("| :--- | :--- |");
                foreach (var p in orphaned)
                {
                    string fullPath = Path.Combine(Path.GetDirectoryName(Application.dataPath), p);
                    string sizeStr = File.Exists(fullPath)
                        ? $"{new FileInfo(fullPath).Length / 1024.0:F1} KB"
                        : "?";
                    sb.AppendLine($"| {p} | {sizeStr} |");
                }
            }
            sb.AppendLine();

            sb.AppendLine("---");
            sb.AppendLine($"UXML files: {uxmlGuids.Length} | USS files: {ussGuids.Length} | Orphaned USS: {orphaned.Count} | Generated: {timestamp}");
        }

        // ─── Mode E: Accessibility Audit ─────────────────────────────────────────

        private static void BuildAccessibilityAudit(StringBuilder sb, string targetWindow, int maxDepth, string timestamp)
        {
            sb.AppendLine("# UI Toolkit Inspector — Mode: Accessibility Audit | Source: Scene UIDocument");
            sb.AppendLine();

            var roots = new List<(string label, VisualElement root)>();

            if (targetWindow != null)
            {
                var allWindows = Resources.FindObjectsOfTypeAll<EditorWindow>();
                foreach (var window in allWindows)
                {
                    if (window.GetType().Name == targetWindow && window.rootVisualElement != null)
                    {
                        roots.Add(($"EditorWindow: {window.GetType().Name}", window.rootVisualElement));
                        break;
                    }
                }
            }
            else
            {
                var uiDocuments = UnityEngine.Object.FindObjectsByType<UIDocument>(FindObjectsSortMode.None);
                foreach (var doc in uiDocuments)
                {
                    if (doc.rootVisualElement != null)
                        roots.Add(($"UIDocument on \"{doc.gameObject.name}\"", doc.rootVisualElement));
                }
            }

            if (roots.Count == 0)
            {
                sb.AppendLine("*No live UI found. Accessibility audit requires UIDocuments in the active scene.*");
                sb.AppendLine("*Deploy UI to the scene and ensure UIDocument components are active.*");
                sb.AppendLine();
                sb.AppendLine("---");
                sb.AppendLine($"Generated: {timestamp}");
                return;
            }

            var missingNames = new List<(int num, string elementName, string typeName, string path)>();
            var lowContrast = new List<(int num, string elementName, string path, float ratio)>();
            var smallTargets = new List<(int num, string elementName, string typeName, string path, float w, float h)>();
            var imagesNoAlt = new List<(int num, string elementName, string path)>();

            int totalAudited = 0;

            foreach (var (label, root) in roots)
            {
                var elements = new List<VisualElement>();
                CollectElements(root, elements, maxDepth, 0);
                totalAudited += elements.Count;

                foreach (var el in elements)
                {
                    string elPath = GetElementPath(el);
                    string elName = string.IsNullOrEmpty(el.name) ? "(unnamed)" : el.name;
                    string typeName = el.GetType().Name;

                    // 1. Interactive elements without accessible names
                    bool isInteractive = el is Button || el is Toggle || el is Slider || el is TextField || el is DropdownField;
                    if (isInteractive)
                    {
                        bool hasName = !string.IsNullOrEmpty(el.name);
                        bool hasTooltip = !string.IsNullOrEmpty(el.tooltip);
                        if (!hasName && !hasTooltip)
                            missingNames.Add((missingNames.Count + 1, elName, typeName, elPath));
                    }

                    // 2. Image elements without alt text (tooltip)
                    if (el is Image img)
                    {
                        if (string.IsNullOrEmpty(el.tooltip))
                            imagesNoAlt.Add((imagesNoAlt.Count + 1, elName, elPath));
                    }

                    // 3. Color contrast for text elements
                    if (el is Label || el is Button || el is TextField)
                    {
                        var rs = el.resolvedStyle;
                        if (rs.color != Color.clear && rs.backgroundColor != Color.clear)
                        {
                            float ratio = CalculateContrastRatio(rs.color, rs.backgroundColor);
                            if (ratio < 4.5f && ratio > 1.0f) // 1.0 usually means same color / transparent
                                lowContrast.Add((lowContrast.Count + 1, elName, elPath, ratio));
                        }
                    }

                    // 4. Small touch targets
                    if (isInteractive)
                    {
                        var rs = el.resolvedStyle;
                        float w = rs.width;
                        float h = rs.height;
                        if (w > 0 && h > 0 && (w < 44f || h < 44f))
                            smallTargets.Add((smallTargets.Count + 1, elName, typeName, elPath, w, h));
                    }
                }
            }

            bool hasIssues = missingNames.Count > 0 || lowContrast.Count > 0 || smallTargets.Count > 0 || imagesNoAlt.Count > 0;

            sb.AppendLine("## Accessibility Issues Found");
            sb.AppendLine();

            // Missing accessible names
            sb.AppendLine("### Missing Accessible Names");
            if (missingNames.Count > 0)
            {
                sb.AppendLine("| # | Element | Type | Path | Issue |");
                sb.AppendLine("| :--- | :--- | :--- | :--- | :--- |");
                foreach (var (num, name, type, path) in missingNames)
                    sb.AppendLine($"| {num} | {name} | {type} | {path} | No name, tooltip, or label |");
            }
            else
            {
                sb.AppendLine("*None found.*");
            }
            sb.AppendLine();

            // Low contrast
            sb.AppendLine("### Low Contrast");
            if (lowContrast.Count > 0)
            {
                sb.AppendLine("| # | Element | Path | Contrast Ratio | Required |");
                sb.AppendLine("| :--- | :--- | :--- | :--- | :--- |");
                foreach (var (num, name, path, ratio) in lowContrast)
                    sb.AppendLine($"| {num} | {name} | {path} | {ratio:F1}:1 | ≥4.5:1 (WCAG AA) |");
            }
            else
            {
                sb.AppendLine("*None found.*");
            }
            sb.AppendLine();

            // Small touch targets
            sb.AppendLine("### Small Touch Targets");
            if (smallTargets.Count > 0)
            {
                sb.AppendLine("| # | Element | Type | Path | Size | Minimum |");
                sb.AppendLine("| :--- | :--- | :--- | :--- | :--- | :--- |");
                foreach (var (num, name, type, path, w, h) in smallTargets)
                    sb.AppendLine($"| {num} | {name} | {type} | {path} | {w:F0}x{h:F0}px | 44x44px |");
            }
            else
            {
                sb.AppendLine("*None found.*");
            }
            sb.AppendLine();

            // Images without alt text
            sb.AppendLine("### Images Without Alt Text");
            if (imagesNoAlt.Count > 0)
            {
                sb.AppendLine("| # | Element | Path |");
                sb.AppendLine("| :--- | :--- | :--- |");
                foreach (var (num, name, path) in imagesNoAlt)
                    sb.AppendLine($"| {num} | {name} | {path} |");
            }
            else
            {
                sb.AppendLine("*None found.*");
            }
            sb.AppendLine();

            // Summary
            sb.AppendLine("## Summary");
            sb.AppendLine($"**Status:** {(hasIssues ? "ISSUES FOUND" : "ALL CLEAR")}");
            sb.AppendLine($"- Total elements audited: {totalAudited}");
            sb.AppendLine($"- Missing accessible names: {missingNames.Count}");
            sb.AppendLine($"- Low contrast issues: {lowContrast.Count}");
            sb.AppendLine($"- Small touch targets: {smallTargets.Count}");
            sb.AppendLine($"- Images without alt text: {imagesNoAlt.Count}");
            sb.AppendLine();

            sb.AppendLine("---");
            sb.AppendLine($"Generated: {timestamp}");
        }

        private static string GetElementPath(VisualElement el)
        {
            var parts = new List<string>();
            var current = el;
            while (current != null && current.parent != null)
            {
                string part = string.IsNullOrEmpty(current.name)
                    ? current.GetType().Name
                    : current.name;
                parts.Insert(0, part);
                current = current.parent;
            }
            return string.Join("/", parts);
        }

        private static float CalculateContrastRatio(Color a, Color b)
        {
            float lumA = RelativeLuminance(a);
            float lumB = RelativeLuminance(b);
            float lighter = Mathf.Max(lumA, lumB);
            float darker = Mathf.Min(lumA, lumB);
            return (lighter + 0.05f) / (darker + 0.05f);
        }

        private static float RelativeLuminance(Color c)
        {
            float R = c.r <= 0.03928f ? c.r / 12.92f : Mathf.Pow((c.r + 0.055f) / 1.055f, 2.4f);
            float G = c.g <= 0.03928f ? c.g / 12.92f : Mathf.Pow((c.g + 0.055f) / 1.055f, 2.4f);
            float B = c.b <= 0.03928f ? c.b / 12.92f : Mathf.Pow((c.b + 0.055f) / 1.055f, 2.4f);
            return 0.2126f * R + 0.7152f * G + 0.0722f * B;
        }

        // ─── Menu Items ───────────────────────────────────────────────────────────

        [MenuItem("Axiom/AgentBridge/UI Toolkit Inspector — Mode A (Visual Tree, Scene)")]
        public static void MenuModeA()
        {
            string path = GenerateReport(UIToolkitInspectorMode.VisualTreeStructure);
            Debug.Log($"[AgentBridge] UI Toolkit Inspector report: {path}");
        }

        [MenuItem("Axiom/AgentBridge/UI Toolkit Inspector — Mode D (UXML/USS File Map)")]
        public static void MenuModeD()
        {
            string path = GenerateReport(UIToolkitInspectorMode.UxmlUssFileMap);
            Debug.Log($"[AgentBridge] UI Toolkit Inspector report: {path}");
        }

        [MenuItem("Axiom/AgentBridge/UI Toolkit Inspector — Mode E (Accessibility Audit)")]
        public static void MenuModeE()
        {
            string path = GenerateReport(UIToolkitInspectorMode.AccessibilityAudit);
            Debug.Log($"[AgentBridge] UI Toolkit Inspector report: {path}");
        }
    }
}
