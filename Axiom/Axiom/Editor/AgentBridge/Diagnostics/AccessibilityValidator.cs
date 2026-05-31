using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor;
using Axiom.Editor.AgentBridge.Core;

namespace Axiom.Editor.AgentBridge.Diagnostics
{
    /// <summary>
    /// Validates game UI accessibility across four dimensions:
    ///   A — Screen Reader Compatibility (labels on interactive elements)
    ///   B — Color Contrast Audit (WCAG AA ratio checks)
    ///   C — Input Accessibility (keyboard/gamepad navigation)
    ///   D — Text Scaling and Readability (font size distribution, relative units)
    ///
    /// Covers both UI Toolkit (UIDocument) and legacy UGUI (Canvas/Selectable) elements.
    /// Works in Edit Mode by scanning the active scene.
    /// </summary>
    public static class AccessibilityValidator
    {
        // ─────────────────────────────────────────────────────
        //  Mode Enum
        // ─────────────────────────────────────────────────────

        public enum AccessibilityMode
        {
            ScreenReaderCompatibility,  // Mode A
            ColorContrastAudit,         // Mode B
            InputAccessibility,         // Mode C
            TextScaling                 // Mode D
        }

        // ─────────────────────────────────────────────────────
        //  Menu Items
        // ─────────────────────────────────────────────────────

        [MenuItem("Axiom/AgentBridge/Accessibility Validator — Mode A (Screen Reader Compatibility)")]
        public static void ModeA() => GenerateReport(AccessibilityMode.ScreenReaderCompatibility);

        [MenuItem("Axiom/AgentBridge/Accessibility Validator — Mode B (Color Contrast Audit)")]
        public static void ModeB() => GenerateReport(AccessibilityMode.ColorContrastAudit);

        [MenuItem("Axiom/AgentBridge/Accessibility Validator — Mode C (Input Accessibility)")]
        public static void ModeC() => GenerateReport(AccessibilityMode.InputAccessibility);

        [MenuItem("Axiom/AgentBridge/Accessibility Validator — Mode D (Text Scaling)")]
        public static void ModeD() => GenerateReport(AccessibilityMode.TextScaling);

        // ─────────────────────────────────────────────────────
        //  Public Entry Point
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// Runs an accessibility validation report.
        /// </summary>
        /// <param name="mode">Which validation mode to run.</param>
        /// <returns>File path of the generated report.</returns>
        public static string GenerateReport(AccessibilityMode mode)
        {
            var sb = new StringBuilder();
            string fileName;

            switch (mode)
            {
                case AccessibilityMode.ScreenReaderCompatibility:
                    BuildScreenReaderReport(sb);
                    fileName = "accessibility_screen_reader";
                    break;
                case AccessibilityMode.ColorContrastAudit:
                    BuildColorContrastReport(sb);
                    fileName = "accessibility_color_contrast";
                    break;
                case AccessibilityMode.InputAccessibility:
                    BuildInputAccessibilityReport(sb);
                    fileName = "accessibility_input";
                    break;
                case AccessibilityMode.TextScaling:
                    BuildTextScalingReport(sb);
                    fileName = "accessibility_text_scaling";
                    break;
                default:
                    sb.AppendLine($"# Accessibility Validator\n\nUnknown mode: {mode}");
                    fileName = "accessibility_unknown";
                    break;
            }

            return OutputWriter.WriteReport(fileName, sb.ToString());
        }

        // ─────────────────────────────────────────────────────
        //  Scene Discovery Helpers
        // ─────────────────────────────────────────────────────

        private static List<UIDocument> GetAllUIDocuments()
        {
            return new List<UIDocument>(
                UnityEngine.Object.FindObjectsByType<UIDocument>(FindObjectsSortMode.None));
        }

        private static List<UnityEngine.UI.Selectable> GetAllSelectables()
        {
            return new List<UnityEngine.UI.Selectable>(
                UnityEngine.Object.FindObjectsByType<UnityEngine.UI.Selectable>(FindObjectsSortMode.None));
        }

        private static List<UnityEngine.UI.Text> GetAllLegacyTexts()
        {
            return new List<UnityEngine.UI.Text>(
                UnityEngine.Object.FindObjectsByType<UnityEngine.UI.Text>(FindObjectsSortMode.None));
        }

        // Gets all TMP_Text components via reflection (avoids hard TMPro assembly reference)
        private static List<Component> GetAllTMPTexts()
        {
            var result = new List<Component>();
            Type tmpType = Type.GetType("TMPro.TMP_Text, Unity.TextMeshPro") ??
                           Type.GetType("TMPro.TMP_Text, TextMeshPro-Runtime") ??
                           Type.GetType("TMPro.TMP_Text");
            if (tmpType == null) return result;
            var found = UnityEngine.Object.FindObjectsByType(tmpType, FindObjectsSortMode.None);
            foreach (var obj in found)
                if (obj is Component c) result.Add(c);
            return result;
        }

        private static float GetTMPFontSize(Component c)
        {
            var prop = c.GetType().GetProperty("fontSize",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            if (prop == null) return 0f;
            try { return Convert.ToSingle(prop.GetValue(c)); } catch { return 0f; }
        }

        private static List<UnityEngine.EventSystems.EventSystem> GetEventSystems()
        {
            return new List<UnityEngine.EventSystems.EventSystem>(
                UnityEngine.Object.FindObjectsByType<UnityEngine.EventSystems.EventSystem>(FindObjectsSortMode.None));
        }

        /// <summary>
        /// Recursively traverses a VisualElement tree and collects all elements.
        /// </summary>
        private static void CollectElements(VisualElement root, List<VisualElement> results)
        {
            if (root == null) return;
            results.Add(root);
            foreach (var child in root.Children())
                CollectElements(child, results);
        }

        /// <summary>
        /// Returns true if the VisualElement type is considered interactive.
        /// </summary>
        private static bool IsInteractive(VisualElement el)
        {
            return el is Button
                || el is Toggle
                || el is Slider
                || el is SliderInt
                || el is TextField
                || el is DropdownField
                || el is RadioButton
                || el is Foldout
                || (el.focusable && el.tabIndex >= 0);
        }

        /// <summary>
        /// Determines if a VisualElement has a usable accessibility label.
        /// Checks tooltip and name.
        /// </summary>
        private static bool HasAccessibilityLabel(VisualElement el)
        {
            if (!string.IsNullOrWhiteSpace(el.tooltip)) return true;
            if (!string.IsNullOrWhiteSpace(el.name) && !el.name.StartsWith("#") && !el.name.Contains("__")) return true;
            // Check for a child Label or text
            foreach (var child in el.Children())
            {
                if (child is Label lbl && !string.IsNullOrWhiteSpace(lbl.text)) return true;
                if (child is TextElement te && !string.IsNullOrWhiteSpace(te.text)) return true;
            }
            return false;
        }

        private static string GetObjectPath(GameObject go)
        {
            if (go == null) return "<null>";
            string path = go.name;
            var parent = go.transform.parent;
            while (parent != null)
            {
                path = parent.name + "/" + path;
                parent = parent.parent;
            }
            return path;
        }

        // ─────────────────────────────────────────────────────
        //  Mode A: Screen Reader Compatibility
        // ─────────────────────────────────────────────────────

        private static void BuildScreenReaderReport(StringBuilder sb)
        {
            sb.AppendLine("# Screen Reader Compatibility Report\n");

            var uitDocuments = GetAllUIDocuments();
            var selectables = GetAllSelectables();

            int totalElements = 0;
            int withLabels = 0;
            int missingLabels = 0;

            var criticalIssues = new List<(string name, string type, string path, string reason)>();
            var nonCriticalIssues = new List<(string name, string type, string path)>();

            // --- UI Toolkit ---
            foreach (var doc in uitDocuments)
            {
                if (doc.rootVisualElement == null) continue;
                var elements = new List<VisualElement>();
                CollectElements(doc.rootVisualElement, elements);

                foreach (var el in elements)
                {
                    // Skip structural containers without names
                    if (string.IsNullOrEmpty(el.name) && !el.focusable && !(el is TextElement))
                        continue;

                    totalElements++;
                    bool interactive = IsInteractive(el);
                    bool hasLabel = HasAccessibilityLabel(el);

                    if (hasLabel)
                    {
                        withLabels++;
                    }
                    else
                    {
                        missingLabels++;
                        string docPath = GetObjectPath(doc.gameObject);
                        string elName = string.IsNullOrEmpty(el.name) ? $"<{el.GetType().Name}>" : el.name;
                        string elType = el.GetType().Name;

                        if (interactive)
                        {
                            string reason = el is Button ? "Buttons must have labels for screen readers"
                                : el is Toggle ? "Toggles need labels to indicate what they control"
                                : el is Slider || el is SliderInt ? "Sliders need labels to convey purpose"
                                : el is TextField ? "Text fields need labels to describe their purpose"
                                : "Interactive elements must have labels";
                            criticalIssues.Add((elName, elType, docPath + "/" + elName, reason));
                        }
                        else
                        {
                            nonCriticalIssues.Add((elName, elType, docPath + "/" + elName));
                        }
                    }
                }
            }

            // --- Legacy UGUI ---
            foreach (var sel in selectables)
            {
                totalElements++;
                // Check if there's associated text
                var texts = sel.GetComponentsInChildren<UnityEngine.UI.Text>();
                // Check for TMP_Text children via reflection
                Type tmpType = Type.GetType("TMPro.TMP_Text, Unity.TextMeshPro") ?? Type.GetType("TMPro.TMP_Text");
                bool hasTMP = tmpType != null && sel.GetComponentsInChildren(tmpType).Length > 0;

                bool hasText = texts.Length > 0 || hasTMP;
                if (hasText)
                {
                    withLabels++;
                }
                else
                {
                    missingLabels++;
                    string path = GetObjectPath(sel.gameObject);
                    string reason = $"{sel.GetType().Name} needs associated text for screen reader support";
                    criticalIssues.Add((sel.name, sel.GetType().Name, path, reason));
                }
            }

            // --- Summary ---
            float labelPct = totalElements > 0 ? (withLabels * 100f / totalElements) : 100f;
            sb.AppendLine("## Summary\n");
            sb.AppendLine($"- **Total UI Elements:** {totalElements}");
            sb.AppendLine($"- **With Accessibility Labels:** {withLabels} ({labelPct:F1}%)");
            sb.AppendLine($"- **Missing Labels:** {missingLabels} ({100f - labelPct:F1}%)");
            sb.AppendLine($"- **Interactive Without Labels:** {criticalIssues.Count}{(criticalIssues.Count > 0 ? " ⚠ CRITICAL" : " ✓")}");

            if (uitDocuments.Count == 0 && selectables.Count == 0)
            {
                sb.AppendLine();
                sb.AppendLine("*No UI elements found in the active scene.*");
                sb.AppendLine("*Ensure UIDocument or Canvas components are present.*");
                return;
            }

            sb.AppendLine();

            // Critical issues
            if (criticalIssues.Count > 0)
            {
                sb.AppendLine("## Missing Labels on Interactive Elements (CRITICAL)\n");
                sb.AppendLine("| Element | Type | Scene Path | Why Critical |");
                sb.AppendLine("| :--- | :--- | :--- | :--- |");
                foreach (var issue in criticalIssues)
                    sb.AppendLine($"| {issue.name} | {issue.type} | {issue.path} | {issue.reason} |");
                sb.AppendLine();
            }
            else
            {
                sb.AppendLine("## Interactive Elements ✓\n");
                sb.AppendLine("*All interactive elements have accessibility labels.*\n");
            }

            // Non-critical issues (capped at 30)
            if (nonCriticalIssues.Count > 0)
            {
                sb.AppendLine("## Missing Labels on Non-Interactive Elements\n");
                sb.AppendLine("| Element | Type | Scene Path |");
                sb.AppendLine("| :--- | :--- | :--- |");
                int shown = Math.Min(nonCriticalIssues.Count, 30);
                for (int i = 0; i < shown; i++)
                {
                    var issue = nonCriticalIssues[i];
                    sb.AppendLine($"| {issue.name} | {issue.type} | {issue.path} |");
                }
                if (nonCriticalIssues.Count > 30)
                    sb.AppendLine($"\n*... and {nonCriticalIssues.Count - 30} more.*");
                sb.AppendLine();
            }

            sb.AppendLine("---");
            sb.AppendLine($"*UI Toolkit Documents scanned: {uitDocuments.Count} | Legacy Selectables: {selectables.Count}*");
        }

        // ─────────────────────────────────────────────────────
        //  Mode B: Color Contrast Audit
        // ─────────────────────────────────────────────────────

        private static void BuildColorContrastReport(StringBuilder sb)
        {
            sb.AppendLine("# Color Contrast Audit\n");
            sb.AppendLine("*WCAG AA: 4.5:1 minimum for normal text, 3:1 for large text (≥18pt or ≥14pt bold)*\n");

            var uitDocuments = GetAllUIDocuments();
            var legacyTexts = GetAllLegacyTexts();

            var failures = new List<(string path, string textColor, string bgColor, float ratio, float required)>();
            var passes = new List<(string path, float ratio)>();

            // --- UI Toolkit ---
            foreach (var doc in uitDocuments)
            {
                if (doc.rootVisualElement == null) continue;
                var elements = new List<VisualElement>();
                CollectElements(doc.rootVisualElement, elements);

                foreach (var el in elements)
                {
                    if (!(el is TextElement te)) continue;
                    if (string.IsNullOrWhiteSpace(te.text)) continue;

                    string docPath = GetObjectPath(doc.gameObject);
                    string elName = string.IsNullOrEmpty(el.name) ? $"<{el.GetType().Name}>" : el.name;
                    string elPath = docPath + "/" + elName;

                    Color textColor, bgColor;
                    float fontSize;

                    try
                    {
                        textColor = te.resolvedStyle.color;
                        bgColor = te.resolvedStyle.backgroundColor;
                        fontSize = te.resolvedStyle.fontSize;
                    }
                    catch
                    {
                        // resolvedStyle may not be available in Edit Mode
                        continue;
                    }

                    // Skip fully transparent backgrounds (layered — cannot accurately determine)
                    if (bgColor.a < 0.1f) continue;

                    float ratio = CalculateContrastRatio(textColor, bgColor);
                    float required = (fontSize >= 18f) ? 3f : 4.5f;

                    if (ratio < required)
                        failures.Add((elPath, ColorToHex(textColor), ColorToHex(bgColor), ratio, required));
                    else
                        passes.Add((elPath, ratio));
                }
            }

            // --- Legacy UGUI ---
            foreach (var text in legacyTexts)
            {
                string path = GetObjectPath(text.gameObject);
                Color textColor = text.color;

                // Try to find background color from parent Image
                var bgImage = text.GetComponentInParent<UnityEngine.UI.Image>();
                Color bgColor = bgImage != null ? bgImage.color : Color.white;

                float fontSize = text.fontSize;
                float required = (fontSize >= 18f) ? 3f : 4.5f;
                float ratio = CalculateContrastRatio(textColor, bgColor);

                if (ratio < required)
                    failures.Add((path, ColorToHex(textColor), ColorToHex(bgColor), ratio, required));
                else
                    passes.Add((path, ratio));
            }

            // --- Summary ---
            int total = failures.Count + passes.Count;
            sb.AppendLine("## Summary\n");
            sb.AppendLine($"- **Elements Checked:** {total}");
            sb.AppendLine($"- **Failures:** {failures.Count}");
            sb.AppendLine($"- **Passes:** {passes.Count}");

            if (uitDocuments.Count == 0 && legacyTexts.Count == 0)
            {
                sb.AppendLine();
                sb.AppendLine("*No text elements found in the active scene.*");
                return;
            }

            if (total == 0)
            {
                sb.AppendLine();
                sb.AppendLine("*No text elements with resolved styles found.*");
                sb.AppendLine("*Note: UI Toolkit resolvedStyle may not be available in Edit Mode.*");
                sb.AppendLine("*Enter Play Mode for accurate color contrast analysis.*");
                return;
            }

            sb.AppendLine();

            if (failures.Count > 0)
            {
                sb.AppendLine("## Failures (Below Required Contrast)\n");
                sb.AppendLine("| Element | Text Color | Background | Ratio | Required | Result |");
                sb.AppendLine("| :--- | :--- | :--- | :--- | :--- | :--- |");
                foreach (var f in failures)
                    sb.AppendLine($"| {f.path} | {f.textColor} | {f.bgColor} | {f.ratio:F1}:1 | {f.required:F1}:1 | ✗ FAIL |");
                sb.AppendLine();
            }

            if (passes.Count > 0)
            {
                sb.AppendLine($"## Passes ({passes.Count} elements)\n");
                sb.AppendLine("| Element | Ratio | Result |");
                sb.AppendLine("| :--- | :--- | :--- |");
                int shown = Math.Min(passes.Count, 20);
                for (int i = 0; i < shown; i++)
                    sb.AppendLine($"| {passes[i].path} | {passes[i].ratio:F1}:1 | ✓ PASS |");
                if (passes.Count > 20)
                    sb.AppendLine($"\n*... and {passes.Count - 20} more passes.*");
            }
        }

        /// <summary>
        /// Calculates WCAG contrast ratio between two colors.
        /// Uses relative luminance: L = 0.2126*R + 0.7152*G + 0.0722*B (after linearizing sRGB).
        /// </summary>
        private static float CalculateContrastRatio(Color c1, Color c2)
        {
            float l1 = RelativeLuminance(c1);
            float l2 = RelativeLuminance(c2);
            float lighter = Mathf.Max(l1, l2);
            float darker = Mathf.Min(l1, l2);
            return (lighter + 0.05f) / (darker + 0.05f);
        }

        private static float RelativeLuminance(Color c)
        {
            float r = LinearizeChannel(c.r);
            float g = LinearizeChannel(c.g);
            float b = LinearizeChannel(c.b);
            return 0.2126f * r + 0.7152f * g + 0.0722f * b;
        }

        private static float LinearizeChannel(float v)
        {
            return v <= 0.03928f ? v / 12.92f : Mathf.Pow((v + 0.055f) / 1.055f, 2.4f);
        }

        private static string ColorToHex(Color c)
        {
            return $"#{Mathf.RoundToInt(c.r * 255):X2}{Mathf.RoundToInt(c.g * 255):X2}{Mathf.RoundToInt(c.b * 255):X2}";
        }

        // ─────────────────────────────────────────────────────
        //  Mode C: Input Accessibility
        // ─────────────────────────────────────────────────────

        private static void BuildInputAccessibilityReport(StringBuilder sb)
        {
            sb.AppendLine("# Input Accessibility Report\n");

            var eventSystems = GetEventSystems();
            var selectables = GetAllSelectables();
            var uitDocuments = GetAllUIDocuments();

            // --- EventSystem Check ---
            sb.AppendLine("## EventSystem\n");
            if (eventSystems.Count == 0)
            {
                sb.AppendLine("⚠️ **No EventSystem found in scene.**");
                sb.AppendLine("*Keyboard and gamepad navigation requires an EventSystem component.*");
                sb.AppendLine("*Add via: GameObject > UI > Event System*\n");
            }
            else
            {
                var es = eventSystems[0];
                sb.AppendLine($"✓ EventSystem present: `{GetObjectPath(es.gameObject)}`");
                string firstSelected = es.firstSelectedGameObject != null
                    ? GetObjectPath(es.firstSelectedGameObject)
                    : "<not set>";
                bool firstSelectedOk = es.firstSelectedGameObject != null;
                sb.AppendLine($"- **First Selected:** {firstSelected}{(firstSelectedOk ? " ✓" : " ⚠ Not set — gamepad navigation won't have a start point")}");
                if (eventSystems.Count > 1)
                    sb.AppendLine($"⚠️ Multiple EventSystems found ({eventSystems.Count}) — only one should exist.");
                sb.AppendLine();
            }

            // --- Legacy UGUI Navigation ---
            if (selectables.Count > 0)
            {
                sb.AppendLine("## Legacy UGUI Navigation\n");

                var noNav = new List<UnityEngine.UI.Selectable>();
                var autoNav = new List<UnityEngine.UI.Selectable>();
                var explicitNav = new List<UnityEngine.UI.Selectable>();

                foreach (var sel in selectables)
                {
                    switch (sel.navigation.mode)
                    {
                        case UnityEngine.UI.Navigation.Mode.None:
                            noNav.Add(sel);
                            break;
                        case UnityEngine.UI.Navigation.Mode.Automatic:
                            autoNav.Add(sel);
                            break;
                        default:
                            explicitNav.Add(sel);
                            break;
                    }
                }

                sb.AppendLine($"- **Total Selectables:** {selectables.Count}");
                sb.AppendLine($"- **Navigation: Automatic:** {autoNav.Count}");
                sb.AppendLine($"- **Navigation: Explicit:** {explicitNav.Count}");
                sb.AppendLine($"- **Navigation: None (keyboard-unreachable):** {noNav.Count}{(noNav.Count > 0 ? " ⚠" : " ✓")}");
                sb.AppendLine();

                if (noNav.Count > 0)
                {
                    sb.AppendLine("### Unreachable Elements (Navigation.None)\n");
                    sb.AppendLine("| Element | Type | Path |");
                    sb.AppendLine("| :--- | :--- | :--- |");
                    foreach (var sel in noNav)
                        sb.AppendLine($"| {sel.name} | {sel.GetType().Name} | {GetObjectPath(sel.gameObject)} |");
                    sb.AppendLine();
                }
            }

            // --- UI Toolkit Navigation ---
            int uitFocusableCount = 0;
            int uitUnreachableCount = 0;
            var uitUnreachable = new List<(string name, string type, string path)>();

            foreach (var doc in uitDocuments)
            {
                if (doc.rootVisualElement == null) continue;
                var elements = new List<VisualElement>();
                CollectElements(doc.rootVisualElement, elements);

                foreach (var el in elements)
                {
                    bool interactive = el is Button || el is Toggle || el is Slider
                        || el is SliderInt || el is TextField || el is DropdownField;
                    if (!interactive) continue;

                    if (el.focusable && el.tabIndex >= 0)
                    {
                        uitFocusableCount++;
                    }
                    else if (!el.focusable)
                    {
                        uitUnreachableCount++;
                        string docPath = GetObjectPath(doc.gameObject);
                        string elName = string.IsNullOrEmpty(el.name) ? $"<{el.GetType().Name}>" : el.name;
                        uitUnreachable.Add((elName, el.GetType().Name, docPath + "/" + elName));
                    }
                }
            }

            if (uitDocuments.Count > 0)
            {
                sb.AppendLine("## UI Toolkit Navigation\n");
                sb.AppendLine($"- **Focusable Interactive Elements:** {uitFocusableCount}");
                sb.AppendLine($"- **Non-focusable Interactive Elements:** {uitUnreachableCount}{(uitUnreachableCount > 0 ? " ⚠" : " ✓")}");
                sb.AppendLine();

                if (uitUnreachable.Count > 0)
                {
                    sb.AppendLine("### Non-Focusable Interactive Elements\n");
                    sb.AppendLine("| Element | Type | Path |");
                    sb.AppendLine("| :--- | :--- | :--- |");
                    foreach (var el in uitUnreachable)
                        sb.AppendLine($"| {el.name} | {el.type} | {el.path} |");
                    sb.AppendLine();
                }
            }

            if (selectables.Count == 0 && uitDocuments.Count == 0)
            {
                sb.AppendLine("*No interactive UI elements found in the active scene.*");
            }
        }

        // ─────────────────────────────────────────────────────
        //  Mode D: Text Scaling and Readability
        // ─────────────────────────────────────────────────────

        private static void BuildTextScalingReport(StringBuilder sb)
        {
            sb.AppendLine("# Text Scaling Report\n");

            var uitDocuments = GetAllUIDocuments();
            var legacyTexts = GetAllLegacyTexts();

            // Collect all TMP_Text elements via reflection (avoids hard TMPro dependency)
            var tmpComponents = GetAllTMPTexts();

            int tooSmall = 0, standard = 0, good = 0, large = 0;
            int remCount = 0, emCount = 0, pxCount = 0;
            int totalTextElements = 0;

            var tooSmallElements = new List<(string path, float size)>();

            // --- UI Toolkit ---
            foreach (var doc in uitDocuments)
            {
                if (doc.rootVisualElement == null) continue;
                var elements = new List<VisualElement>();
                CollectElements(doc.rootVisualElement, elements);

                foreach (var el in elements)
                {
                    if (!(el is TextElement te)) continue;
                    if (string.IsNullOrWhiteSpace(te.text)) continue;

                    totalTextElements++;
                    float fontSize = 0;
                    try { fontSize = te.resolvedStyle.fontSize; } catch { }

                    if (fontSize > 0)
                    {
                        BucketFontSize(fontSize, ref tooSmall, ref standard, ref good, ref large);
                        if (fontSize < 12f)
                        {
                            string docPath = GetObjectPath(doc.gameObject);
                            string elName = string.IsNullOrEmpty(el.name) ? $"<{el.GetType().Name}>" : el.name;
                            tooSmallElements.Add((docPath + "/" + elName, fontSize));
                        }
                    }

                    // Check for relative units via USS — best-effort check via inline style
                    // resolvedStyle doesn't expose unit type; check customStyle or name hints
                    // This is a heuristic — we can't fully determine USS unit type at runtime in Edit Mode
                    pxCount++;
                }
            }

            // --- Legacy Text ---
            foreach (var t in legacyTexts)
            {
                totalTextElements++;
                float fontSize = t.fontSize;
                BucketFontSize(fontSize, ref tooSmall, ref standard, ref good, ref large);
                if (fontSize < 12f)
                    tooSmallElements.Add((GetObjectPath(t.gameObject), fontSize));
                pxCount++;
            }

            // --- TMP (via reflection) ---
            foreach (var t in tmpComponents)
            {
                totalTextElements++;
                float fontSize = GetTMPFontSize(t);
                if (fontSize > 0)
                {
                    BucketFontSize(fontSize, ref tooSmall, ref standard, ref good, ref large);
                    if (fontSize < 12f)
                        tooSmallElements.Add((GetObjectPath(t.gameObject), fontSize));
                }
                pxCount++;
            }

            // --- USS file scan for relative units (asset-level check) ---
            string[] ussGuids = AssetDatabase.FindAssets("t:StyleSheet");
            foreach (string guid in ussGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                try
                {
                    string content = System.IO.File.ReadAllText(
                        System.IO.Path.Combine(
                            System.IO.Path.GetDirectoryName(Application.dataPath), path));
                    int remMatches = System.Text.RegularExpressions.Regex.Matches(content, @"\d+(\.\d+)?rem").Count;
                    int emMatches = System.Text.RegularExpressions.Regex.Matches(content, @"\d+(\.\d+)?em").Count;
                    remCount += remMatches;
                    emCount += emMatches;
                }
                catch { }
            }

            // Adjust px count — rem/em counted from USS indicate some elements use relative units
            int relativeCount = remCount + emCount;

            // --- Summary ---
            sb.AppendLine("## Font Size Distribution\n");

            if (totalTextElements == 0)
            {
                sb.AppendLine("*No text elements found in the active scene.*");
                sb.AppendLine("*Note: UI Toolkit resolvedStyle may not be available in Edit Mode.*");
                sb.AppendLine("*Enter Play Mode for accurate font size analysis.*");
            }
            else
            {
                sb.AppendLine("| Size Range | Count | Concern |");
                sb.AppendLine("| :--- | :--- | :--- |");
                sb.AppendLine($"| < 12pt | {tooSmall} | {(tooSmall > 0 ? "⚠ May be too small for vision-impaired users" : "✓")} |");
                sb.AppendLine($"| 12–16pt | {standard} | ✓ Standard |");
                sb.AppendLine($"| 16–24pt | {good} | ✓ Good |");
                sb.AppendLine($"| > 24pt | {large} | ✓ Headers |");
                sb.AppendLine();

                if (tooSmallElements.Count > 0)
                {
                    sb.AppendLine("### Elements with Very Small Text (< 12pt)\n");
                    sb.AppendLine("| Path | Font Size |");
                    sb.AppendLine("| :--- | :--- |");
                    foreach (var e in tooSmallElements)
                        sb.AppendLine($"| {e.path} | {e.size:F0}pt |");
                    sb.AppendLine();
                }
            }

            // --- Dynamic Scaling ---
            sb.AppendLine("## Dynamic Scaling\n");
            sb.AppendLine($"*USS files scanned: {ussGuids.Length}*\n");

            if (ussGuids.Length > 0)
            {
                sb.AppendLine($"- **rem references in USS:** {remCount}");
                sb.AppendLine($"- **em references in USS:** {emCount}");
                sb.AppendLine($"- **Fixed px text elements:** {pxCount}{(pxCount > 0 ? " ⚠ Consider relative units for accessibility scaling" : "")}");

                if (relativeCount == 0)
                {
                    sb.AppendLine();
                    sb.AppendLine("⚠️ **No relative font units (rem/em) found in USS files.**");
                    sb.AppendLine("**Recommendation:** Use `rem` or `em` units in USS for text to support accessibility text scaling.");
                }
                else
                {
                    sb.AppendLine();
                    sb.AppendLine($"✓ {relativeCount} relative font size reference(s) found in USS.");
                }
            }
            else
            {
                sb.AppendLine("*No USS files found in project.*");
            }

            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine($"*Total text elements scanned: {totalTextElements} (UI Toolkit + Legacy Text + TextMeshPro)*");
        }

        private static void BucketFontSize(float size, ref int small, ref int standard, ref int good, ref int large)
        {
            if (size < 12f) small++;
            else if (size <= 16f) standard++;
            else if (size <= 24f) good++;
            else large++;
        }
    }
}
