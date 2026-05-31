using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using Axiom.Editor.AgentBridge.Core;

namespace Axiom.Editor.AgentBridge.Actions
{
    /// <summary>
    /// Safe, undo-aware operations for prefab override management and Prefab Mode staging.
    /// All mutations go through SerializedObject / PrefabUtility APIs to preserve Undo
    /// and respect Prefab Override workflows.
    /// </summary>
    public static class PrefabActions
    {
        // ─────────────────────────────────────────────────────
        //  1.1  ApplyOverrides
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// Applies all property overrides on a prefab instance back to the source prefab.
        /// </summary>
        /// <param name="objectPath">Hierarchy path to the prefab instance.</param>
        /// <param name="applyToBase">
        /// If true, walks the variant chain to the innermost base prefab before applying.
        /// If false (default), applies to the nearest source prefab.
        /// </param>
        public static ActionResult ApplyOverrides(string objectPath, bool applyToBase = false)
        {
            if (!PathResolver.ResolveRootPath(objectPath, out var targets) || targets.Count == 0)
                return ActionResult.Fail($"Object not found: {objectPath}");

            var go = targets[0];

            if (!PrefabUtility.IsPartOfPrefabInstance(go))
                return ActionResult.Fail($"{objectPath} is not a prefab instance");

            var objectOverrides  = PrefabUtility.GetObjectOverrides(go);
            var addedComponents  = PrefabUtility.GetAddedComponents(go);
            var removedComponents = PrefabUtility.GetRemovedComponents(go);
            var addedObjects     = PrefabUtility.GetAddedGameObjects(go);
            int totalOverrides   = objectOverrides.Count + addedComponents.Count
                                 + removedComponents.Count + addedObjects.Count;

            if (totalOverrides == 0)
                return ActionResult.Ok($"No overrides found on {objectPath}");

            string prefabAssetPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(go);

            if (applyToBase)
            {
                // Walk the chain to the base (non-variant) prefab
                string candidatePath = prefabAssetPath;
                while (true)
                {
                    var asset = AssetDatabase.LoadAssetAtPath<GameObject>(candidatePath);
                    if (asset == null) break;
                    string parent = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(asset);
                    if (string.IsNullOrEmpty(parent) || parent == candidatePath) break;
                    candidatePath = parent;
                }
                prefabAssetPath = candidatePath;
            }

            Undo.RegisterFullObjectHierarchyUndo(go, "Apply Prefab Overrides");
            PrefabUtility.ApplyPrefabInstance(go, InteractionMode.AutomatedAction);

            Debug.Log($"[AgentBridge] Applied {totalOverrides} override(s) on {objectPath} → {prefabAssetPath}");
            return ActionResult.Ok(
                $"Applied {totalOverrides} override(s) on {objectPath} → {prefabAssetPath}");
        }

        // ─────────────────────────────────────────────────────
        //  1.2  RevertOverrides
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// Reverts all property overrides on a prefab instance back to the source prefab values.
        /// </summary>
        /// <param name="objectPath">Hierarchy path to the prefab instance.</param>
        public static ActionResult RevertOverrides(string objectPath)
        {
            if (!PathResolver.ResolveRootPath(objectPath, out var targets) || targets.Count == 0)
                return ActionResult.Fail($"Object not found: {objectPath}");

            var go = targets[0];

            if (!PrefabUtility.IsPartOfPrefabInstance(go))
                return ActionResult.Fail($"{objectPath} is not a prefab instance");

            var objectOverrides   = PrefabUtility.GetObjectOverrides(go);
            var addedComponents   = PrefabUtility.GetAddedComponents(go);
            var removedComponents = PrefabUtility.GetRemovedComponents(go);
            var addedObjects      = PrefabUtility.GetAddedGameObjects(go);
            int totalOverrides    = objectOverrides.Count + addedComponents.Count
                                  + removedComponents.Count + addedObjects.Count;

            if (totalOverrides == 0)
                return ActionResult.Ok($"No overrides to revert on {objectPath}");

            Undo.RegisterFullObjectHierarchyUndo(go, "Revert Prefab Overrides");
            PrefabUtility.RevertPrefabInstance(go, InteractionMode.AutomatedAction);

            Debug.Log($"[AgentBridge] Reverted {totalOverrides} override(s) on {objectPath}");
            return ActionResult.Ok($"Reverted {totalOverrides} override(s) on {objectPath}");
        }

        // ─────────────────────────────────────────────────────
        //  1.3  ApplyPropertyOverride
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// Applies a single specific property override back to the source prefab.
        /// </summary>
        /// <param name="objectPath">Hierarchy path to the prefab instance.</param>
        /// <param name="componentType">Component type name containing the property.</param>
        /// <param name="propertyPath">SerializedProperty path to apply (e.g. "m_Materials").</param>
        public static ActionResult ApplyPropertyOverride(
            string objectPath, string componentType, string propertyPath)
        {
            if (!PathResolver.ResolveRootPath(objectPath, out var targets) || targets.Count == 0)
                return ActionResult.Fail($"Object not found: {objectPath}");

            var go = targets[0];

            if (!PrefabUtility.IsPartOfPrefabInstance(go))
                return ActionResult.Fail($"{objectPath} is not a prefab instance");

            var type = TypeResolver.ResolveComponentType(componentType);
            if (type == null) return ActionResult.Fail($"Component type not found: {componentType}");

            var component = go.GetComponent(type);
            if (component == null)
                return ActionResult.Fail($"{componentType} not found on {objectPath}");

            var so   = new SerializedObject(component);
            var prop = so.FindProperty(propertyPath);
            if (prop == null)
                return ActionResult.Fail($"Property not found: {propertyPath} on {componentType}");

            string assetPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(go);
            PrefabUtility.ApplyPropertyOverride(prop, assetPath, InteractionMode.AutomatedAction);

            Debug.Log($"[AgentBridge] Applied override {componentType}.{propertyPath} on {objectPath}");
            return ActionResult.Ok(
                $"Applied override {componentType}.{propertyPath} on {objectPath} → {assetPath}");
        }

        // ─────────────────────────────────────────────────────
        //  1.4  RevertPropertyOverride
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// Reverts a single specific property override back to the source prefab value.
        /// </summary>
        /// <param name="objectPath">Hierarchy path to the prefab instance.</param>
        /// <param name="componentType">Component type name containing the property.</param>
        /// <param name="propertyPath">SerializedProperty path to revert.</param>
        public static ActionResult RevertPropertyOverride(
            string objectPath, string componentType, string propertyPath)
        {
            if (!PathResolver.ResolveRootPath(objectPath, out var targets) || targets.Count == 0)
                return ActionResult.Fail($"Object not found: {objectPath}");

            var go = targets[0];

            if (!PrefabUtility.IsPartOfPrefabInstance(go))
                return ActionResult.Fail($"{objectPath} is not a prefab instance");

            var type = TypeResolver.ResolveComponentType(componentType);
            if (type == null) return ActionResult.Fail($"Component type not found: {componentType}");

            var component = go.GetComponent(type);
            if (component == null)
                return ActionResult.Fail($"{componentType} not found on {objectPath}");

            var so   = new SerializedObject(component);
            var prop = so.FindProperty(propertyPath);
            if (prop == null)
                return ActionResult.Fail($"Property not found: {propertyPath} on {componentType}");

            PrefabUtility.RevertPropertyOverride(prop, InteractionMode.AutomatedAction);

            Debug.Log($"[AgentBridge] Reverted override {componentType}.{propertyPath} on {objectPath}");
            return ActionResult.Ok($"Reverted override {componentType}.{propertyPath} on {objectPath}");
        }

        // ─────────────────────────────────────────────────────
        //  1.5  RemoveUnusedOverrides
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// Removes orphan/unused overrides from a prefab instance — properties that were
        /// once recorded but whose target field no longer exists on the component.
        /// </summary>
        /// <param name="objectPath">Hierarchy path to the prefab instance.</param>
        public static ActionResult RemoveUnusedOverrides(string objectPath)
        {
            if (!PathResolver.ResolveRootPath(objectPath, out var targets) || targets.Count == 0)
                return ActionResult.Fail($"Object not found: {objectPath}");

            var go = targets[0];

            if (!PrefabUtility.IsPartOfPrefabInstance(go))
                return ActionResult.Fail($"{objectPath} is not a prefab instance");

            var mods = PrefabUtility.GetPropertyModifications(go);
            if (mods == null || mods.Length == 0)
                return ActionResult.Ok($"No property modifications found on {objectPath}");

            var unused  = new List<PropertyModification>();
            var kept    = new List<PropertyModification>();

            foreach (var mod in mods)
            {
                if (mod.target == null)
                {
                    unused.Add(mod);
                    continue;
                }

                // Check the property still exists on the target object
                var so   = new SerializedObject(mod.target);
                var prop = so.FindProperty(mod.propertyPath);
                if (prop == null)
                    unused.Add(mod);
                else
                    kept.Add(mod);
            }

            if (unused.Count == 0)
                return ActionResult.Ok($"No unused overrides found on {objectPath}");

            Undo.RegisterFullObjectHierarchyUndo(go, "Remove Unused Prefab Overrides");
            PrefabUtility.SetPropertyModifications(go, kept.ToArray());

            Debug.Log($"[AgentBridge] Removed {unused.Count} unused override(s) from {objectPath}");
            return ActionResult.Ok($"Removed {unused.Count} unused override(s) from {objectPath}");
        }

        // ─────────────────────────────────────────────────────
        //  1.6  OpenPrefabStage
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// Opens a prefab asset in Prefab Mode (isolated editing stage).
        /// </summary>
        /// <param name="assetPath">Project-relative path to the .prefab asset (e.g. "Assets/Prefabs/Player.prefab").</param>
        public static ActionResult OpenPrefabStage(string assetPath)
        {
            var prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (prefabAsset == null)
                return ActionResult.Fail($"Prefab asset not found at: {assetPath}");

            if (PrefabUtility.GetPrefabAssetType(prefabAsset) == PrefabAssetType.NotAPrefab)
                return ActionResult.Fail($"Asset at {assetPath} is not a prefab");

            var stage = PrefabStageUtility.OpenPrefab(assetPath);
            if (stage == null)
                return ActionResult.Fail($"Failed to open prefab stage for: {assetPath}");

            Debug.Log($"[AgentBridge] Opened prefab stage: {assetPath}");
            return ActionResult.Ok(
                $"Opened prefab stage: {stage.prefabContentsRoot.name} ({assetPath})", prefabAsset);
        }

        // ─────────────────────────────────────────────────────
        //  1.7  ClosePrefabStage
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// Closes the current Prefab Mode and returns to the main scene.
        /// </summary>
        /// <param name="saveChanges">If true (default), saves changes before exiting.</param>
        public static ActionResult ClosePrefabStage(bool saveChanges = true)
        {
            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage == null)
                return ActionResult.Fail("Not currently in Prefab Mode");

            string prefabName = stage.prefabContentsRoot != null
                ? stage.prefabContentsRoot.name
                : "(unknown)";
            string prefabPath = stage.assetPath;

            if (saveChanges)
            {
                // Force save of the prefab stage asset before exiting
                var root = stage.prefabContentsRoot;
                if (root != null)
                    PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            }

            StageUtility.GoToMainStage();

            string action = saveChanges ? "saved and closed" : "closed without saving";
            Debug.Log($"[AgentBridge] Prefab stage {action}: {prefabName}");
            return ActionResult.Ok($"Prefab stage {action}: {prefabName} ({prefabPath})");
        }

        // ─────────────────────────────────────────────────────
        //  1.8  GetPrefabInfo
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// Returns detailed information about a prefab instance (hierarchy path) or a prefab asset.
        /// </summary>
        /// <param name="objectPathOrAssetPath">
        /// Either a hierarchy breadcrumb path (e.g. "Player/Model") or
        /// a project asset path starting with "Assets/" (e.g. "Assets/Prefabs/Player.prefab").
        /// </param>
        public static ActionResult GetPrefabInfo(string objectPathOrAssetPath)
        {
            if (objectPathOrAssetPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                return GetPrefabAssetInfo(objectPathOrAssetPath);

            return GetPrefabInstanceInfo(objectPathOrAssetPath);
        }

        // ─── Private helpers for GetPrefabInfo ───

        private static ActionResult GetPrefabInstanceInfo(string objectPath)
        {
            if (!PathResolver.ResolveRootPath(objectPath, out var targets) || targets.Count == 0)
                return ActionResult.Fail($"Object not found: {objectPath}");

            var go = targets[0];

            var assetType    = PrefabUtility.GetPrefabAssetType(go);
            var instanceStatus = PrefabUtility.GetPrefabInstanceStatus(go);

            var sb = new StringBuilder();
            sb.AppendLine($"# Prefab Info — Instance: {objectPath}");
            sb.AppendLine($"- Asset Type:       {assetType}");
            sb.AppendLine($"- Instance Status:  {instanceStatus}");

            if (PrefabUtility.IsPartOfPrefabInstance(go))
            {
                var nearestRoot   = PrefabUtility.GetNearestPrefabInstanceRoot(go);
                var prefabPath    = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(go);

                sb.AppendLine($"- Nearest Root:     {(nearestRoot != null ? nearestRoot.name : "null")}");
                sb.AppendLine($"- Source Asset:     {prefabPath}");

                var objectOverrides   = PrefabUtility.GetObjectOverrides(go);
                var addedComponents   = PrefabUtility.GetAddedComponents(go);
                var removedComponents = PrefabUtility.GetRemovedComponents(go);
                var addedObjects      = PrefabUtility.GetAddedGameObjects(go);

                sb.AppendLine($"- Object Overrides: {objectOverrides.Count}");
                sb.AppendLine($"- Added Components: {addedComponents.Count}");
                sb.AppendLine($"- Removed Components: {removedComponents.Count}");
                sb.AppendLine($"- Added GameObjects: {addedObjects.Count}");
                sb.AppendLine($"- Total Overrides:  {objectOverrides.Count + addedComponents.Count + removedComponents.Count + addedObjects.Count}");
            }
            else
            {
                sb.AppendLine("- Not a prefab instance");
            }

            return ActionResult.Ok(sb.ToString(), go);
        }

        private static ActionResult GetPrefabAssetInfo(string assetPath)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (prefab == null)
                return ActionResult.Fail($"Prefab asset not found: {assetPath}");

            var assetType = PrefabUtility.GetPrefabAssetType(prefab);

            var sb = new StringBuilder();
            sb.AppendLine($"# Prefab Info — Asset: {assetPath}");
            sb.AppendLine($"- Asset Type: {assetType}");
            sb.AppendLine($"- Name:       {prefab.name}");

            // Walk the variant chain
            var chain = new List<string>();
            chain.Add(assetPath);

            var current = prefab;
            while (true)
            {
                var source = PrefabUtility.GetCorrespondingObjectFromSource(current);
                if (source == null) break;
                string sourcePath = AssetDatabase.GetAssetPath(source);
                if (string.IsNullOrEmpty(sourcePath) || chain.Contains(sourcePath)) break;
                chain.Add(sourcePath);
                current = source;
            }

            if (chain.Count > 1)
            {
                sb.AppendLine("- Variant Chain:");
                foreach (var path in chain)
                    sb.AppendLine($"    → {path}");
            }
            else
            {
                sb.AppendLine("- Base prefab (no variants above)");
            }

            int componentCount = prefab.GetComponentsInChildren<Component>(true).Length;
            sb.AppendLine($"- Components (incl. children): {componentCount}");

            return ActionResult.Ok(sb.ToString(), prefab);
        }

        // ─────────────────────────────────────────────────────
        //  Menu Items
        // ─────────────────────────────────────────────────────

        [MenuItem("Axiom/AgentBridge/Actions/Prefab Info")]
        public static void MenuPrefabInfo()
        {
            var go = Selection.activeGameObject;
            if (go == null)
            {
                Debug.Log("[AgentBridge] PrefabInfo: no GameObject selected");
                return;
            }
            var result = GetPrefabInfo(PathResolver.GetHierarchyPath(go.transform));
            Debug.Log($"[AgentBridge] {result.Message}");
        }

        [MenuItem("Axiom/AgentBridge/Actions/Apply Selected Prefab Overrides")]
        public static void MenuApplyOverrides()
        {
            var go = Selection.activeGameObject;
            if (go == null)
            {
                Debug.Log("[AgentBridge] ApplyOverrides: no GameObject selected");
                return;
            }
            var result = ApplyOverrides(PathResolver.GetHierarchyPath(go.transform));
            Debug.Log($"[AgentBridge] {result.Message}");
        }

        [MenuItem("Axiom/AgentBridge/Actions/Revert Selected Prefab Overrides")]
        public static void MenuRevertOverrides()
        {
            var go = Selection.activeGameObject;
            if (go == null)
            {
                Debug.Log("[AgentBridge] RevertOverrides: no GameObject selected");
                return;
            }
            var result = RevertOverrides(PathResolver.GetHierarchyPath(go.transform));
            Debug.Log($"[AgentBridge] {result.Message}");
        }
    }
}
