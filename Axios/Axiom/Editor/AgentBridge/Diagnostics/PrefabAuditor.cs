using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEditor;
using Axiom.Editor.AgentBridge.Core;

namespace Axiom.Editor.AgentBridge.Diagnostics
{
    /// <summary>
    /// Specialized tool for prefab architecture health — variant chains, overrides,
    /// unused data, nesting depth, and cross-prefab references.
    /// </summary>
    public static class PrefabAuditor
    {
        public enum PrefabAuditorMode
        {
            VariantTree,              // Mode A
            OverrideReport,           // Mode B
            UnusedOverrideCleanup,    // Mode C
            NestingDepth,             // Mode D
            CrossPrefabReferences     // Mode E
        }

        /// <summary>
        /// Audits prefab assets.
        /// </summary>
        /// <param name="mode">Audit type.</param>
        /// <param name="prefabPath">Path to a specific prefab asset for Modes A, B, C. Null = scan all prefabs in assetPath.</param>
        /// <param name="assetPath">Folder to scope scan. Null = "Assets".</param>
        /// <param name="maxNestingWarning">For Mode D: nesting depth threshold for warnings. Default 3.</param>
        /// <returns>File path of the generated report.</returns>
        public static string GenerateReport(
            PrefabAuditorMode mode,
            string prefabPath = null,
            string assetPath = null,
            int maxNestingWarning = 3)
        {
            var sb = new StringBuilder();
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            string searchPath = assetPath ?? "Assets";

            switch (mode)
            {
                case PrefabAuditorMode.VariantTree:
                    BuildVariantTree(sb, prefabPath, searchPath, timestamp);
                    break;
                case PrefabAuditorMode.OverrideReport:
                    BuildOverrideReport(sb, prefabPath, searchPath, timestamp);
                    break;
                case PrefabAuditorMode.UnusedOverrideCleanup:
                    BuildUnusedOverrideCleanup(sb, prefabPath, searchPath, timestamp);
                    break;
                case PrefabAuditorMode.NestingDepth:
                    BuildNestingDepth(sb, prefabPath, searchPath, maxNestingWarning, timestamp);
                    break;
                case PrefabAuditorMode.CrossPrefabReferences:
                    BuildCrossPrefabReferences(sb, prefabPath, searchPath, timestamp);
                    break;
            }

            string reportName = $"prefab_auditor_{mode.ToString().ToLower()}_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.md";
            return OutputWriter.WriteReport(reportName, sb.ToString());
        }

        // ─── Mode A: Variant Tree ─────────────────────────────────────────────────

        private static void BuildVariantTree(StringBuilder sb, string prefabPath, string searchPath, string timestamp)
        {
            sb.AppendLine($"# Prefab Auditor — Mode: Variant Tree | Path: {searchPath}");
            sb.AppendLine();

            string[] guids = prefabPath != null
                ? new[] { AssetDatabase.AssetPathToGUID(prefabPath) }
                : AssetDatabase.FindAssets("t:Prefab", new[] { searchPath });

            if (guids.Length == 0)
            {
                sb.AppendLine("*No prefabs found.*");
                sb.AppendLine();
                sb.AppendLine("---");
                sb.AppendLine($"Total prefabs: 0 | Originals: 0 | Variants: 0 | Standalone: 0 | Generated: {timestamp}");
                return;
            }

            // Build parent → children map
            var parentToVariants = new Dictionary<string, List<string>>();
            var variantToParent = new Dictionary<string, string>();
            var allPaths = new HashSet<string>();

            foreach (string guid in guids)
            {
                if (string.IsNullOrEmpty(guid)) continue;
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path)) continue;
                allPaths.Add(path);

                var prefabGo = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefabGo == null) continue;

                var prefabType = PrefabUtility.GetPrefabAssetType(prefabGo);
                if (prefabType == PrefabAssetType.Variant)
                {
                    var source = PrefabUtility.GetCorrespondingObjectFromOriginalSource(prefabGo);
                    if (source != null)
                    {
                        string sourcePath = AssetDatabase.GetAssetPath(source);
                        if (!parentToVariants.ContainsKey(sourcePath))
                            parentToVariants[sourcePath] = new List<string>();
                        parentToVariants[sourcePath].Add(path);
                        variantToParent[path] = sourcePath;
                    }
                }
            }

            // Find root originals (not themselves variants of something else)
            var rootOriginals = new HashSet<string>();
            foreach (var kv in parentToVariants)
                if (!variantToParent.ContainsKey(kv.Key))
                    rootOriginals.Add(kv.Key);

            // Standalone: paths that are neither a parent nor a variant
            var standalone = allPaths
                .Where(p => !parentToVariants.ContainsKey(p) && !variantToParent.ContainsKey(p))
                .ToList();

            int originals = rootOriginals.Count;
            int variants = variantToParent.Count;

            // Output chains
            if (rootOriginals.Count > 0)
            {
                sb.AppendLine("## Variant Chains");
                int chainNum = 1;
                foreach (var root in rootOriginals.OrderBy(p => p))
                {
                    sb.AppendLine($"### Chain {chainNum++}");
                    sb.AppendLine("```");
                    PrintVariantTree(sb, root, parentToVariants, 0);
                    sb.AppendLine("```");
                    sb.AppendLine();
                }
            }
            else
            {
                sb.AppendLine("*No variant chains found.*");
                sb.AppendLine();
            }

            // Standalone
            sb.AppendLine("## Standalone Prefabs (no variants)");
            sb.AppendLine("| Prefab Path |");
            sb.AppendLine("| :--- |");
            foreach (var p in standalone.OrderBy(x => x))
                sb.AppendLine($"| {p} |");
            sb.AppendLine();

            sb.AppendLine("---");
            sb.AppendLine($"Total prefabs: {allPaths.Count} | Originals: {originals} | Variants: {variants} | Standalone: {standalone.Count} | Generated: {timestamp}");
        }

        private static void PrintVariantTree(StringBuilder sb, string path, Dictionary<string, List<string>> map, int depth)
        {
            string prefix = new string(' ', depth * 4);
            string connector = depth == 0 ? "" : "└── ";
            bool hasChildren = map.ContainsKey(path);
            string type = hasChildren ? "Original" : "Variant";
            sb.AppendLine($"{prefix}{connector}{path} ({type})");

            if (map.TryGetValue(path, out var children))
            {
                foreach (var child in children.OrderBy(c => c))
                    PrintVariantTree(sb, child, map, depth + 1);
            }
        }

        // ─── Mode B: Override Report ──────────────────────────────────────────────

        private static void BuildOverrideReport(StringBuilder sb, string prefabPath, string searchPath, string timestamp)
        {
            string scope = prefabPath ?? "Active Scene";
            sb.AppendLine($"# Prefab Auditor — Mode: Override Report | Scope: {scope}");
            sb.AppendLine();

            int instancesScanned = 0;
            int totalOverrides = 0;

            if (prefabPath != null)
            {
                // Scan overrides within a specific prefab's internal hierarchy
                var prefabGo = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                if (prefabGo == null)
                {
                    sb.AppendLine($"*Prefab not found at path: {prefabPath}*");
                    sb.AppendLine();
                    sb.AppendLine("---");
                    sb.AppendLine($"Generated: {timestamp}");
                    return;
                }

                var innerInstances = prefabGo.GetComponentsInChildren<Transform>(true)
                    .Select(t => t.gameObject)
                    .Where(go => go != prefabGo && PrefabUtility.IsAnyPrefabInstanceRoot(go))
                    .ToList();

                foreach (var instance in innerInstances)
                {
                    totalOverrides += ReportInstanceOverrides(sb, instance, ref instancesScanned);
                }
            }
            else
            {
                // Scan active scene for prefab instances
                var roots = PathResolver.GetRootGameObjects();
                foreach (var root in roots)
                {
                    var transforms = root.GetComponentsInChildren<Transform>(true);
                    foreach (var t in transforms)
                    {
                        if (!PrefabUtility.IsPartOfPrefabInstance(t.gameObject)) continue;
                        if (!PrefabUtility.IsOutermostPrefabInstanceRoot(t.gameObject)) continue;

                        totalOverrides += ReportInstanceOverrides(sb, t.gameObject, ref instancesScanned);
                    }
                }
            }

            if (instancesScanned == 0)
                sb.AppendLine("*No prefab instances found in scope.*");

            sb.AppendLine();
            sb.AppendLine("*Note: Transform position/rotation/scale overrides are included. These are usually intentional.*");
            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine($"Prefab instances scanned: {instancesScanned} | Total overrides: {totalOverrides} | Generated: {timestamp}");
        }

        private static int ReportInstanceOverrides(StringBuilder sb, GameObject instance, ref int instancesScanned)
        {
            instancesScanned++;
            int overrideCount = 0;

            string sourcePath = "(unknown)";
            var source = PrefabUtility.GetCorrespondingObjectFromSource(instance);
            if (source != null)
                sourcePath = AssetDatabase.GetAssetPath(source);

            sb.AppendLine($"## {instance.name} (instance of {sourcePath})");

            // Property modifications
            var modifications = PrefabUtility.GetPropertyModifications(instance);
            if (modifications != null && modifications.Length > 0)
            {
                sb.AppendLine($"### Property Overrides: {modifications.Length}");
                sb.AppendLine("| Target Object | Component | Property | Value |");
                sb.AppendLine("| :--- | :--- | :--- | :--- |");
                foreach (var mod in modifications)
                {
                    if (mod.target == null) continue;
                    string targetName = mod.target is Component c ? c.gameObject.name : mod.target.name;
                    string componentName = mod.target.GetType().Name;
                    string value = mod.value?.Length > 60 ? mod.value.Substring(0, 60) + "..." : mod.value ?? "";
                    sb.AppendLine($"| {targetName} | {componentName} | {mod.propertyPath} | {value} |");
                    overrideCount++;
                }
                sb.AppendLine();
            }

            // Added components
            var addedComponents = PrefabUtility.GetAddedComponents(instance);
            sb.AppendLine($"### Added Components: {addedComponents.Count}");
            foreach (var ac in addedComponents)
                sb.AppendLine($"- {ac.instanceComponent?.gameObject.name ?? "(unknown)"} → {ac.instanceComponent?.GetType().Name ?? "(unknown)"}");
            if (addedComponents.Count > 0) sb.AppendLine();
            overrideCount += addedComponents.Count;

            // Removed components
            var removedComponents = PrefabUtility.GetRemovedComponents(instance);
            sb.AppendLine($"### Removed Components: {removedComponents.Count}");
            foreach (var rc in removedComponents)
                sb.AppendLine($"- {rc.assetComponent?.gameObject.name ?? "(unknown)"} → {rc.assetComponent?.GetType().Name ?? "(unknown)"}");
            if (removedComponents.Count > 0) sb.AppendLine();
            overrideCount += removedComponents.Count;

            // Added GameObjects
            var addedObjects = PrefabUtility.GetAddedGameObjects(instance);
            sb.AppendLine($"### Added GameObjects: {addedObjects.Count}");
            foreach (var ao in addedObjects)
                sb.AppendLine($"- {ao.instanceGameObject?.name ?? "(unknown)"}");
            if (addedObjects.Count > 0) sb.AppendLine();
            overrideCount += addedObjects.Count;

            // Removed GameObjects
            var removedObjects = PrefabUtility.GetRemovedGameObjects(instance);
            sb.AppendLine($"### Removed GameObjects: {removedObjects.Count}");
            foreach (var ro in removedObjects)
                sb.AppendLine($"- {ro.assetGameObject?.name ?? "(unknown)"}");
            if (removedObjects.Count > 0) sb.AppendLine();
            overrideCount += removedObjects.Count;

            return overrideCount;
        }

        // ─── Mode C: Unused Override Cleanup ─────────────────────────────────────

        private static void BuildUnusedOverrideCleanup(StringBuilder sb, string prefabPath, string searchPath, string timestamp)
        {
            sb.AppendLine($"# Prefab Auditor — Mode: Unused Override Cleanup | Path: {searchPath}");
            sb.AppendLine();

            int prefabsScanned = 0;
            int instancesScanned = 0;
            var unusedOverrides = new List<(int num, string context, string targetType, string propertyPath, string oldValue)>();

            // Scan scene instances
            var roots = PathResolver.GetRootGameObjects();
            foreach (var root in roots)
            {
                var transforms = root.GetComponentsInChildren<Transform>(true);
                foreach (var t in transforms)
                {
                    if (!PrefabUtility.IsPartOfPrefabInstance(t.gameObject)) continue;
                    if (!PrefabUtility.IsOutermostPrefabInstanceRoot(t.gameObject)) continue;

                    instancesScanned++;
                    string instanceLabel = $"Scene: {t.gameObject.name} instance";
                    ScanUnusedOverrides(t.gameObject, instanceLabel, unusedOverrides);
                }
            }

            // Scan prefab assets
            string[] guids = prefabPath != null
                ? new[] { AssetDatabase.AssetPathToGUID(prefabPath) }
                : AssetDatabase.FindAssets("t:Prefab", new[] { searchPath });

            foreach (string guid in guids)
            {
                if (string.IsNullOrEmpty(guid)) continue;
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var prefabGo = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefabGo == null) continue;

                prefabsScanned++;
                ScanUnusedOverrides(prefabGo, path, unusedOverrides);
            }

            if (unusedOverrides.Count > 0)
            {
                sb.AppendLine("## Unused Overrides Found");
                sb.AppendLine("| # | Prefab/Instance | Target Component | Orphaned Property | Old Value |");
                sb.AppendLine("| :--- | :--- | :--- | :--- | :--- |");
                foreach (var (num, ctx, tt, pp, ov) in unusedOverrides)
                    sb.AppendLine($"| {num} | {ctx} | {tt} | {pp} | {ov} |");
                sb.AppendLine();

                sb.AppendLine("## Summary");
                sb.AppendLine($"- Unused overrides: {unusedOverrides.Count}");
                sb.AppendLine("- These can be safely removed to reduce prefab bloat.");
                sb.AppendLine("- To clean: revert unused overrides in the Inspector or via PrefabUtility.");
            }
            else
            {
                sb.AppendLine("*No unused overrides found. All property modifications reference valid properties.*");
            }
            sb.AppendLine();

            sb.AppendLine("---");
            sb.AppendLine($"Prefabs scanned: {prefabsScanned} | Instances scanned: {instancesScanned} | Generated: {timestamp}");
        }

        private static void ScanUnusedOverrides(
            GameObject root,
            string contextLabel,
            List<(int, string, string, string, string)> unusedOverrides)
        {
            var modifications = PrefabUtility.GetPropertyModifications(root);
            if (modifications == null) return;

            foreach (var mod in modifications)
            {
                if (mod.target == null)
                {
                    unusedOverrides.Add((
                        unusedOverrides.Count + 1,
                        contextLabel,
                        "(null — deleted target)",
                        mod.propertyPath,
                        mod.value ?? ""
                    ));
                    continue;
                }

                try
                {
                    var so = new SerializedObject(mod.target);
                    var prop = so.FindProperty(mod.propertyPath);
                    if (prop == null)
                    {
                        unusedOverrides.Add((
                            unusedOverrides.Count + 1,
                            contextLabel,
                            mod.target.GetType().Name,
                            mod.propertyPath,
                            mod.value ?? ""
                        ));
                    }
                }
                catch { /* Skip inaccessible targets */ }
            }
        }

        // ─── Mode D: Nesting Depth ────────────────────────────────────────────────

        private static void BuildNestingDepth(StringBuilder sb, string prefabPath, string searchPath, int maxNestingWarning, string timestamp)
        {
            sb.AppendLine($"# Prefab Auditor — Mode: Nesting Depth | Warning Threshold: {maxNestingWarning}");
            sb.AppendLine();

            string[] guids = prefabPath != null
                ? new[] { AssetDatabase.AssetPathToGUID(prefabPath) }
                : AssetDatabase.FindAssets("t:Prefab", new[] { searchPath });

            if (guids.Length == 0)
            {
                sb.AppendLine("*No prefabs found.*");
                sb.AppendLine();
                sb.AppendLine("---");
                sb.AppendLine($"Total prefabs: 0 | Max depth: 0 | Warnings (≥{maxNestingWarning}): 0 | Generated: {timestamp}");
                return;
            }

            var depthResults = new List<(string path, int depth)>();
            var visitedGuids = new HashSet<string>();

            foreach (string guid in guids)
            {
                if (string.IsNullOrEmpty(guid)) continue;
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var prefabGo = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefabGo == null) continue;

                visitedGuids.Clear();
                int depth = CalculateNestingDepth(prefabGo, 0, visitedGuids);
                depthResults.Add((path, depth));
            }

            int maxDepth = depthResults.Count > 0 ? depthResults.Max(r => r.depth) : 0;
            int warningCount = depthResults.Count(r => r.depth >= maxNestingWarning);

            sb.AppendLine("## Prefab Nesting Depths");
            sb.AppendLine("| Prefab Path | Max Depth | Status |");
            sb.AppendLine("| :--- | :--- | :--- |");
            foreach (var (path, depth) in depthResults.OrderByDescending(r => r.depth))
            {
                string status = depth >= maxNestingWarning ? "⚠ WARNING" : "OK";
                sb.AppendLine($"| {path} | {depth} | {status} |");
            }
            sb.AppendLine();

            // Detail for deep nesting
            var deepPrefabs = depthResults.Where(r => r.depth >= maxNestingWarning).ToList();
            if (deepPrefabs.Count > 0)
            {
                sb.AppendLine($"## Deep Nesting Details (depth ≥ {maxNestingWarning})");
                foreach (var (path, depth) in deepPrefabs.OrderByDescending(r => r.depth))
                {
                    sb.AppendLine($"### {path} (depth: {depth})");
                    var prefabGo = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (prefabGo != null)
                    {
                        sb.AppendLine("```");
                        PrintNestingTree(sb, prefabGo, 0);
                        sb.AppendLine("```");
                    }
                    sb.AppendLine();
                }
            }

            sb.AppendLine("---");
            sb.AppendLine($"Total prefabs: {depthResults.Count} | Max depth: {maxDepth} | Warnings (≥{maxNestingWarning}): {warningCount} | Generated: {timestamp}");
        }

        private static int CalculateNestingDepth(GameObject prefab, int currentDepth, HashSet<string> visited)
        {
            string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(prefab));
            if (!string.IsNullOrEmpty(guid) && visited.Contains(guid))
                return currentDepth; // Circular reference guard
            if (!string.IsNullOrEmpty(guid))
                visited.Add(guid);

            int maxDepth = currentDepth;
            var allTransforms = prefab.GetComponentsInChildren<Transform>(true);
            foreach (var t in allTransforms)
            {
                if (t.gameObject == prefab) continue;
                if (PrefabUtility.IsAnyPrefabInstanceRoot(t.gameObject))
                {
                    var nestedSource = PrefabUtility.GetCorrespondingObjectFromSource(t.gameObject);
                    if (nestedSource != null)
                    {
                        int nestedDepth = CalculateNestingDepth(nestedSource, currentDepth + 1, visited);
                        maxDepth = Math.Max(maxDepth, nestedDepth);
                    }
                }
            }
            return maxDepth;
        }

        private static void PrintNestingTree(StringBuilder sb, GameObject go, int depth)
        {
            string indent = new string(' ', depth * 4);
            string connector = depth == 0 ? "" : "└── ";
            string nestedLabel = "";

            if (depth > 0 && PrefabUtility.IsAnyPrefabInstanceRoot(go))
            {
                var src = PrefabUtility.GetCorrespondingObjectFromSource(go);
                if (src != null)
                {
                    string srcPath = AssetDatabase.GetAssetPath(src);
                    nestedLabel = $" (nested: {System.IO.Path.GetFileName(srcPath)})";
                }
            }

            sb.AppendLine($"{indent}{connector}{go.name}{nestedLabel}");

            // Only recurse into direct nested prefab roots to keep tree readable
            foreach (Transform child in go.transform)
            {
                if (PrefabUtility.IsAnyPrefabInstanceRoot(child.gameObject))
                {
                    var childSrc = PrefabUtility.GetCorrespondingObjectFromSource(child.gameObject);
                    if (childSrc != null)
                        PrintNestingTree(sb, childSrc, depth + 1);
                    else
                        PrintNestingTree(sb, child.gameObject, depth + 1);
                }
            }
        }

        // ─── Mode E: Cross-Prefab References ─────────────────────────────────────

        private static void BuildCrossPrefabReferences(StringBuilder sb, string prefabPath, string searchPath, string timestamp)
        {
            sb.AppendLine($"# Prefab Auditor — Mode: Cross-Prefab References | Path: {searchPath}");
            sb.AppendLine();

            string[] guids = prefabPath != null
                ? new[] { AssetDatabase.AssetPathToGUID(prefabPath) }
                : AssetDatabase.FindAssets("t:Prefab", new[] { searchPath });

            if (guids.Length == 0)
            {
                sb.AppendLine("*No prefabs found.*");
                sb.AppendLine();
                sb.AppendLine("---");
                sb.AppendLine($"Prefabs scanned: 0 | Generated: {timestamp}");
                return;
            }

            var dangerousRefs = new List<(int num, string prefabPath2, string component, string property, string refDesc)>();
            var safeAssetRefs = new List<(int num, string prefabPath2, string component, string property, string refPath)>();
            int prefabsScanned = 0;

            foreach (string guid in guids)
            {
                if (string.IsNullOrEmpty(guid)) continue;
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var prefabRoot = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefabRoot == null) continue;

                prefabsScanned++;
                var allComponents = prefabRoot.GetComponentsInChildren<Component>(true);

                foreach (var component in allComponents)
                {
                    if (component == null) continue;
                    try
                    {
                        var so = new SerializedObject(component);
                        var iterator = so.GetIterator();
                        bool enter = true;
                        while (iterator.NextVisible(enter))
                        {
                            enter = false;
                            if (iterator.propertyType != SerializedPropertyType.ObjectReference) continue;
                            if (iterator.objectReferenceValue == null) continue;

                            var refObj = iterator.objectReferenceValue;

                            // Check if internal to this prefab
                            bool isInternal = false;
                            if (refObj is GameObject refGo)
                                isInternal = refGo.transform.IsChildOf(prefabRoot.transform) || refGo == prefabRoot;
                            else if (refObj is Component refComp)
                                isInternal = refComp.transform.IsChildOf(prefabRoot.transform) || refComp.gameObject == prefabRoot;
                            else
                                isInternal = true; // Non-GO, non-Component (e.g., Material, Texture) — treat as safe

                            if (isInternal) continue;

                            // Check if asset or scene reference
                            string refAssetPath = AssetDatabase.GetAssetPath(refObj);
                            string componentName = component.GetType().Name;
                            string propPath = iterator.propertyPath;

                            if (string.IsNullOrEmpty(refAssetPath))
                            {
                                // Scene object reference — dangerous
                                dangerousRefs.Add((
                                    dangerousRefs.Count + 1,
                                    path,
                                    componentName,
                                    propPath,
                                    $"{refObj.name} (scene object)"
                                ));
                            }
                            else
                            {
                                // Asset reference — safe
                                safeAssetRefs.Add((
                                    safeAssetRefs.Count + 1,
                                    path,
                                    componentName,
                                    propPath,
                                    refAssetPath
                                ));
                            }
                        }
                    }
                    catch { /* Skip inaccessible components */ }
                }
            }

            // Dangerous references
            sb.AppendLine("## External Scene References (WILL break on instantiation)");
            if (dangerousRefs.Count > 0)
            {
                sb.AppendLine("| # | Prefab Path | Component | Property | References (scene object) |");
                sb.AppendLine("| :--- | :--- | :--- | :--- | :--- |");
                foreach (var (num, pp, comp, prop, refDesc) in dangerousRefs)
                    sb.AppendLine($"| {num} | {pp} | {comp} | {prop} | {refDesc} |");
            }
            else
            {
                sb.AppendLine("*None found.*");
            }
            sb.AppendLine();

            // Safe asset references
            sb.AppendLine("## External Asset References (OK — asset references persist)");
            if (safeAssetRefs.Count > 0)
            {
                sb.AppendLine("| # | Prefab Path | Component | Property | References (asset) |");
                sb.AppendLine("| :--- | :--- | :--- | :--- | :--- |");
                foreach (var (num, pp, comp, prop, refPath) in safeAssetRefs)
                    sb.AppendLine($"| {num} | {pp} | {comp} | {prop} | {refPath} |");
            }
            else
            {
                sb.AppendLine("*None found.*");
            }
            sb.AppendLine();

            // Summary
            string statusLine = dangerousRefs.Count > 0 ? "**Status: ISSUES FOUND**" : "**Status: ALL CLEAR**";
            sb.AppendLine("## Summary");
            sb.AppendLine(statusLine);
            sb.AppendLine($"- Dangerous scene references: {dangerousRefs.Count}");
            sb.AppendLine($"- Safe asset references: {safeAssetRefs.Count}");
            sb.AppendLine();

            sb.AppendLine("---");
            sb.AppendLine($"Prefabs scanned: {prefabsScanned} | Generated: {timestamp}");
        }

        // ─── Menu Items ───────────────────────────────────────────────────────────

        [MenuItem("Axiom/AgentBridge/Prefab Auditor — Mode A (Variant Tree)")]
        public static void MenuModeA()
        {
            string path = GenerateReport(PrefabAuditorMode.VariantTree);
            Debug.Log($"[AgentBridge] Prefab Auditor report: {path}");
        }

        [MenuItem("Axiom/AgentBridge/Prefab Auditor — Mode B (Override Report, Scene)")]
        public static void MenuModeB()
        {
            string path = GenerateReport(PrefabAuditorMode.OverrideReport);
            Debug.Log($"[AgentBridge] Prefab Auditor report: {path}");
        }

        [MenuItem("Axiom/AgentBridge/Prefab Auditor — Mode C (Unused Overrides)")]
        public static void MenuModeC()
        {
            string path = GenerateReport(PrefabAuditorMode.UnusedOverrideCleanup);
            Debug.Log($"[AgentBridge] Prefab Auditor report: {path}");
        }

        [MenuItem("Axiom/AgentBridge/Prefab Auditor — Mode D (Nesting Depth)")]
        public static void MenuModeD()
        {
            string path = GenerateReport(PrefabAuditorMode.NestingDepth);
            Debug.Log($"[AgentBridge] Prefab Auditor report: {path}");
        }

        [MenuItem("Axiom/AgentBridge/Prefab Auditor — Mode E (Cross-Prefab References)")]
        public static void MenuModeE()
        {
            string path = GenerateReport(PrefabAuditorMode.CrossPrefabReferences);
            Debug.Log($"[AgentBridge] Prefab Auditor report: {path}");
        }
    }
}
