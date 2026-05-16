using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEditor;
using Axiom.Editor.AgentBridge.Core;

namespace Axiom.Editor.AgentBridge.Actions
{
    /// <summary>
    /// High-level methods for connecting references between objects.
    /// Implements the "Wiring via Editor Scripts" principle from the workspace rules.
    /// </summary>
    public static class WiringUtility
    {
        // ─────────────────────────────────────────────────────
        //  4.1 WireReference
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// Wires an object reference field on a component to a target.
        /// Primary method for connecting objects together.
        /// </summary>
        /// <param name="sourcePath">Path to the object with the reference field.</param>
        /// <param name="sourceComponent">Component type name on the source.</param>
        /// <param name="propertyPath">The ObjectReference property to set.</param>
        /// <param name="targetPath">Scene breadcrumb path or asset path.</param>
        public static ActionResult WireReference(
            string sourcePath,
            string sourceComponent,
            string propertyPath,
            string targetPath)
        {
            return ComponentActions.SetProperty(sourcePath, sourceComponent, propertyPath, targetPath);
        }

        // ─────────────────────────────────────────────────────
        //  4.2 BatchWire
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// Wires multiple references at once under a single undo operation.
        /// </summary>
        public static ActionResult BatchWire(WiringDefinition[] wirings)
        {
            if (wirings == null || wirings.Length == 0)
                return ActionResult.Fail("No wiring definitions provided.");

            Undo.SetCurrentGroupName("Batch Wire References");

            int successCount = 0;
            int failCount = 0;
            var errors = new List<string>();

            foreach (var wiring in wirings)
            {
                var result = WireReference(
                    wiring.sourcePath,
                    wiring.sourceComponent,
                    wiring.propertyPath,
                    wiring.targetPath);

                if (result.Success) successCount++;
                else
                {
                    failCount++;
                    errors.Add(result.Message);
                }
            }

            string summary = $"Wired {successCount} references. Failures: {failCount}.";
            if (errors.Count > 0) summary += $"\nErrors:\n{string.Join("\n", errors)}";
            return failCount == 0 ? ActionResult.Ok(summary) : ActionResult.Fail(summary);
        }

        // ─────────────────────────────────────────────────────
        //  4.3 AutoWire
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// Automatically resolves and wires null/unassigned ObjectReference properties.
        /// Only wires when exactly one candidate is found (unambiguous).
        /// </summary>
        /// <param name="objectPath">Target object with unassigned references.</param>
        /// <param name="componentType">Component to auto-wire. Null = auto-wire all components.</param>
        /// <param name="dryRun">If true, report what WOULD be wired without doing it.</param>
        /// <param name="searchScope">Path to limit search scope. Null = entire scene.</param>
        public static ActionResult AutoWire(
            string objectPath,
            string componentType = null,
            bool dryRun = false,
            string searchScope = null)
        {
            if (!PathResolver.ResolveRootPath(objectPath, out var targets) || targets.Count == 0)
                return ActionResult.Fail($"Object not found: {objectPath}");

            var go = targets[0];

            Component[] componentsToWire;
            if (componentType != null)
            {
                var resolvedType = TypeResolver.ResolveComponentType(componentType);
                if (resolvedType == null)
                    return ActionResult.Fail($"Component type not found: {componentType}");
                var comp = go.GetComponent(resolvedType);
                if (comp == null)
                    return ActionResult.Fail($"{componentType} not found on {objectPath}");
                componentsToWire = new[] { comp };
            }
            else
            {
                componentsToWire = go.GetComponents<Component>().Where(c => c != null).ToArray();
            }

            var report = new StringBuilder();
            int wiredCount = 0;
            int skippedCount = 0;
            int ambiguousCount = 0;

            foreach (var component in componentsToWire)
            {
                var so = new SerializedObject(component);
                var iterator = so.GetIterator();
                bool enter = true;
                bool modified = false;

                while (iterator.NextVisible(enter))
                {
                    enter = false;
                    if (iterator.propertyType != SerializedPropertyType.ObjectReference) continue;
                    if (iterator.objectReferenceValue != null) continue;
                    if (iterator.propertyPath == "m_Script") continue;

                    string expectedTypeName = ExtractPPtrTypeName(iterator.type);
                    if (expectedTypeName == null) continue;

                    // Skip built-in non-component types we can't search for
                    if (expectedTypeName == "GameObject" || expectedTypeName == "Object") continue;

                    var candidateType = TypeResolver.ResolveComponentType(expectedTypeName);
                    if (candidateType == null) continue;

                    if (!typeof(Component).IsAssignableFrom(candidateType)) continue;

                    var found = UnityEngine.Object.FindObjectsByType(candidateType, FindObjectsSortMode.None);
                    var candidates = found
                        .Where(c => ((Component)c).gameObject != go)
                        .Cast<UnityEngine.Object>()
                        .ToList();

                    if (candidates.Count == 0)
                    {
                        skippedCount++;
                        report.AppendLine($"  SKIP: {component.GetType().Name}.{iterator.propertyPath} ({expectedTypeName}) — no candidates");
                    }
                    else if (candidates.Count == 1)
                    {
                        if (!dryRun)
                        {
                            iterator.objectReferenceValue = candidates[0];
                            modified = true;
                        }
                        wiredCount++;
                        string candidateName = candidates[0] is Component comp
                            ? PathResolver.GetHierarchyPath(comp.transform)
                            : candidates[0].name;
                        report.AppendLine($"  WIRE: {component.GetType().Name}.{iterator.propertyPath} → {candidateName}");
                    }
                    else
                    {
                        ambiguousCount++;
                        var names = candidates.Take(3).Select(c =>
                            c is Component comp2 ? PathResolver.GetHierarchyPath(comp2.transform) : c.name);
                        report.AppendLine($"  AMBIGUOUS: {component.GetType().Name}.{iterator.propertyPath} ({expectedTypeName}) — {candidates.Count} candidates: {string.Join(", ", names)}...");
                    }
                }

                if (!dryRun && modified)
                    so.ApplyModifiedProperties();
            }

            string summary = dryRun
                ? $"DRY RUN: Would wire {wiredCount}, skip {skippedCount}, ambiguous {ambiguousCount}"
                : $"Wired {wiredCount} references. Skipped: {skippedCount}. Ambiguous: {ambiguousCount}";

            string fullReport = $"# AutoWire Report — {objectPath}\n\n{report}\n---\n{summary}";
            OutputWriter.WriteReport("autowire", fullReport);

            return (wiredCount > 0 || dryRun)
                ? ActionResult.Ok(summary)
                : ActionResult.Fail(summary);
        }

        // ─────────────────────────────────────────────────────
        //  4.4 VerifyWiring
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// Verifies that ObjectReference properties on a component are assigned (non-null).
        /// Returns Success if all assigned, Fail if any are null or missing.
        /// </summary>
        public static ActionResult VerifyWiring(string objectPath, string componentType = null)
        {
            if (!PathResolver.ResolveRootPath(objectPath, out var targets) || targets.Count == 0)
                return ActionResult.Fail($"Object not found: {objectPath}");

            var go = targets[0];

            Component[] components;
            if (componentType != null)
            {
                var resolvedType = TypeResolver.ResolveComponentType(componentType);
                if (resolvedType == null)
                    return ActionResult.Fail($"Component type not found: {componentType}");
                var comp = go.GetComponent(resolvedType);
                if (comp == null)
                    return ActionResult.Fail($"{componentType} not found on {objectPath}");
                components = new[] { comp };
            }
            else
            {
                components = go.GetComponents<Component>().Where(c => c != null).ToArray();
            }

            var unassigned = new List<string>();
            var missing = new List<string>();

            foreach (var comp in components)
            {
                var so = new SerializedObject(comp);
                var iterator = so.GetIterator();
                bool enter = true;

                while (iterator.NextVisible(enter))
                {
                    enter = false;
                    if (iterator.propertyType != SerializedPropertyType.ObjectReference) continue;
                    if (iterator.propertyPath == "m_Script") continue;

                    if (iterator.objectReferenceValue == null)
                    {
                        if (iterator.objectReferenceInstanceIDValue != 0)
                            missing.Add($"{comp.GetType().Name}.{iterator.propertyPath} (MISSING — target deleted)");
                        else
                            unassigned.Add($"{comp.GetType().Name}.{iterator.propertyPath} (unassigned)");
                    }
                }
            }

            if (missing.Count == 0 && unassigned.Count == 0)
                return ActionResult.Ok($"All references verified on {objectPath}");

            string details = "";
            if (missing.Count > 0) details += $"\nMissing: {string.Join(", ", missing)}";
            if (unassigned.Count > 0) details += $"\nUnassigned: {string.Join(", ", unassigned)}";
            return ActionResult.Fail($"Wiring incomplete on {objectPath}:{details}");
        }

        // ─────────────────────────────────────────────────────
        //  Editor Menu Items (testing)
        // ─────────────────────────────────────────────────────

        [MenuItem("Axiom/AgentBridge/Actions/AutoWire All (Dry Run)")]
        public static void MenuAutoWireDryRun()
        {
            var roots = PathResolver.GetRootGameObjects();
            if (roots.Count > 0)
            {
                var result = AutoWire(roots[0].name, dryRun: true);
                Debug.Log($"[AgentBridge] {result.Message}");
            }
            else
            {
                Debug.Log("[AgentBridge] No root GameObjects found.");
            }
        }

        // ─────────────────────────────────────────────────────
        //  Private Helpers
        // ─────────────────────────────────────────────────────

        private static string ExtractPPtrTypeName(string ptrType)
        {
            // ptrType looks like "PPtr<Rigidbody>" or "PPtr<$Rigidbody>"
            if (!ptrType.StartsWith("PPtr<")) return null;
            string typeName = ptrType.Substring(5, ptrType.Length - 6);
            if (typeName.StartsWith("$")) typeName = typeName.Substring(1);
            return typeName;
        }
    }

    // ─────────────────────────────────────────────────────
    //  WiringDefinition (used by BatchWire)
    // ─────────────────────────────────────────────────────

    [Serializable]
    public class WiringDefinition
    {
        public string sourcePath;
        public string sourceComponent;
        public string propertyPath;
        public string targetPath;
    }
}
