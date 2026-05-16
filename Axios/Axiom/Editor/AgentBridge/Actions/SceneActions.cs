using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor;
using UnityEditor.SceneManagement;
using Axiom.Editor.AgentBridge.Core;

namespace Axiom.Editor.AgentBridge.Actions
{
    /// <summary>
    /// Safe, undo-able operations for creating, destroying, reparenting, and
    /// organizing GameObjects in the scene hierarchy.
    /// </summary>
    public static class SceneActions
    {
        // ─────────────────────────────────────────────────────
        //  2.1 CreateGameObject
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// Creates a new GameObject in the scene hierarchy.
        /// </summary>
        public static ActionResult CreateGameObject(
            string name,
            string parentPath = null,
            string[] components = null,
            string tag = null,
            string layer = null,
            bool? isStatic = null,
            Vector3? localPosition = null,
            Vector3? localRotation = null,
            Vector3? localScale = null)
        {
            // 1. Resolve parent
            Transform parent = null;
            if (!string.IsNullOrEmpty(parentPath))
            {
                if (!PathResolver.ResolveRootPath(parentPath, out var parentObjects) || parentObjects.Count == 0)
                    return ActionResult.Fail($"Parent path not found: {parentPath}");
                parent = parentObjects[0].transform;
            }

            // 2. Create the GameObject
            var go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, $"Create {name}");

            // 3. Parent it
            if (parent != null)
                Undo.SetTransformParent(go.transform, parent, $"Parent {name} under {parentPath}");

            // 4. Set transform
            go.transform.localPosition = localPosition ?? Vector3.zero;
            go.transform.localEulerAngles = localRotation ?? Vector3.zero;
            go.transform.localScale = localScale ?? Vector3.one;

            // 5. Add components
            if (components != null)
            {
                foreach (string compTypeName in components)
                {
                    var type = TypeResolver.ResolveComponentType(compTypeName);
                    if (type == null)
                    {
                        Debug.LogWarning($"[AgentBridge] Component type not found: {compTypeName}. Skipping.");
                        continue;
                    }
                    Undo.AddComponent(go, type);
                }
            }

            // 6. Set tag
            if (tag != null)
            {
                try { go.tag = tag; }
                catch { Debug.LogWarning($"[AgentBridge] Tag not found: {tag}"); }
            }

            // 7. Set layer
            if (layer != null)
            {
                int layerIndex = LayerMask.NameToLayer(layer);
                if (layerIndex >= 0) go.layer = layerIndex;
                else Debug.LogWarning($"[AgentBridge] Layer not found: {layer}");
            }

            // 8. Set static
            if (isStatic.HasValue)
                go.isStatic = isStatic.Value;

            // 9. Mark scene dirty
            EditorUtility.SetDirty(go);
            EditorSceneManager.MarkSceneDirty(go.scene);

            string path = PathResolver.GetHierarchyPath(go.transform);
            Debug.Log($"[AgentBridge] Created: {path}");
            return ActionResult.Ok($"Created {name} at {path}", go);
        }

        // ─────────────────────────────────────────────────────
        //  2.2 BatchCreate
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// Creates multiple GameObjects at once. Useful for setting up hierarchies.
        /// parentPath can reference objects created earlier in the same batch.
        /// </summary>
        public static ActionResult BatchCreate(GameObjectDefinition[] definitions)
        {
            if (definitions == null || definitions.Length == 0)
                return ActionResult.Fail("No definitions provided.");

            Undo.SetCurrentGroupName("Batch Create GameObjects");

            int successCount = 0;
            int errorCount = 0;
            var errors = new List<string>();

            foreach (var def in definitions)
            {
                var result = CreateGameObject(
                    def.name,
                    def.parentPath,
                    def.components,
                    def.tag,
                    def.layer);

                if (result.Success) successCount++;
                else
                {
                    errorCount++;
                    errors.Add(result.Message);
                }
            }

            string summary = $"Created {successCount} GameObjects. Errors: {errorCount}.";
            if (errors.Count > 0) summary += $"\nErrors:\n{string.Join("\n", errors)}";

            return errorCount == 0 ? ActionResult.Ok(summary) : ActionResult.Fail(summary);
        }

        // ─────────────────────────────────────────────────────
        //  2.3 DestroyGameObject
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// Destroys a GameObject by path. Undo-safe.
        /// </summary>
        public static ActionResult DestroyGameObject(string objectPath)
        {
            if (!PathResolver.ResolveRootPath(objectPath, out var targets) || targets.Count == 0)
                return ActionResult.Fail($"Object not found: {objectPath}");

            var go = targets[0];
            Undo.DestroyObjectImmediate(go);
            Debug.Log($"[AgentBridge] Destroyed: {objectPath}");
            return ActionResult.Ok($"Destroyed: {objectPath}");
        }

        // ─────────────────────────────────────────────────────
        //  2.4 BatchDestroy
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// Destroys multiple GameObjects by paths, tag, layer, or component type.
        /// </summary>
        public static ActionResult BatchDestroy(
            string[] objectPaths = null,
            string tagFilter = null,
            string layerFilter = null,
            string componentFilter = null,
            string rootPath = null,
            bool dryRun = false)
        {
            Undo.SetCurrentGroupName("Batch Destroy");

            var toDestroy = new List<GameObject>();

            // Collect by explicit paths
            if (objectPaths != null)
            {
                foreach (var path in objectPaths)
                {
                    if (PathResolver.ResolveRootPath(path, out var resolved) && resolved.Count > 0)
                        toDestroy.Add(resolved[0]);
                    else
                        Debug.LogWarning($"[AgentBridge] BatchDestroy: object not found: {path}");
                }
            }

            // Collect by filter
            if (tagFilter != null || layerFilter != null || componentFilter != null)
            {
                List<GameObject> scope;
                if (!string.IsNullOrEmpty(rootPath))
                {
                    if (!PathResolver.ResolveRootPath(rootPath, out var rootObjs) || rootObjs.Count == 0)
                        return ActionResult.Fail($"Root path not found: {rootPath}");
                    scope = CollectAllChildren(rootObjs[0]);
                }
                else
                {
                    scope = new List<GameObject>();
                    foreach (var root in PathResolver.GetRootGameObjects())
                        scope.AddRange(CollectAllChildren(root));
                }

                int filterLayer = layerFilter != null ? LayerMask.NameToLayer(layerFilter) : -1;
                System.Type filterType = componentFilter != null ? TypeResolver.ResolveComponentType(componentFilter) : null;

                foreach (var go in scope)
                {
                    bool match = true;
                    if (tagFilter != null && go.tag != tagFilter) match = false;
                    if (filterLayer >= 0 && go.layer != filterLayer) match = false;
                    if (filterType != null && go.GetComponent(filterType) == null) match = false;
                    if (match && !toDestroy.Contains(go)) toDestroy.Add(go);
                }
            }

            if (toDestroy.Count == 0)
                return ActionResult.Ok("BatchDestroy: no matching objects found.");

            // Sort children before parents to avoid destroying parent first
            toDestroy.Sort((a, b) =>
            {
                int depthA = GetDepth(a.transform);
                int depthB = GetDepth(b.transform);
                return depthB.CompareTo(depthA);
            });

            if (dryRun)
            {
                string list = string.Join("\n  ", toDestroy.Select(g => PathResolver.GetHierarchyPath(g.transform)));
                return ActionResult.Ok($"DRY RUN: Would destroy {toDestroy.Count} objects:\n  {list}");
            }

            foreach (var go in toDestroy)
            {
                if (go != null)
                    Undo.DestroyObjectImmediate(go);
            }

            return ActionResult.Ok($"Destroyed {toDestroy.Count} GameObjects.");
        }

        // ─────────────────────────────────────────────────────
        //  2.5 Reparent
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// Moves a GameObject to a new parent. Undo-safe.
        /// </summary>
        public static ActionResult Reparent(
            string objectPath,
            string newParentPath = null,
            bool keepWorldPosition = true)
        {
            if (!PathResolver.ResolveRootPath(objectPath, out var targets) || targets.Count == 0)
                return ActionResult.Fail($"Object not found: {objectPath}");

            var go = targets[0];
            Transform newParent = null;

            if (!string.IsNullOrEmpty(newParentPath))
            {
                if (!PathResolver.ResolveRootPath(newParentPath, out var parentTargets) || parentTargets.Count == 0)
                    return ActionResult.Fail($"New parent not found: {newParentPath}");
                newParent = parentTargets[0].transform;
            }

            Undo.SetTransformParent(go.transform, newParent, $"Reparent {go.name}");

            if (!keepWorldPosition)
            {
                Undo.RecordObject(go.transform, "Reset local transform");
                go.transform.localPosition = Vector3.zero;
                go.transform.localRotation = Quaternion.identity;
                go.transform.localScale = Vector3.one;
            }

            string newPath = PathResolver.GetHierarchyPath(go.transform);
            Debug.Log($"[AgentBridge] Reparented: {objectPath} → {newPath}");
            return ActionResult.Ok($"Reparented to: {newPath}", go);
        }

        // ─────────────────────────────────────────────────────
        //  2.6 BatchReparent
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// Moves multiple GameObjects under a new parent.
        /// </summary>
        public static ActionResult BatchReparent(
            string[] objectPaths,
            string newParentPath = null,
            bool keepWorldPosition = true)
        {
            if (objectPaths == null || objectPaths.Length == 0)
                return ActionResult.Fail("No object paths provided.");

            Undo.SetCurrentGroupName("Batch Reparent");

            int successCount = 0;
            var errors = new List<string>();

            foreach (var path in objectPaths)
            {
                var result = Reparent(path, newParentPath, keepWorldPosition);
                if (result.Success) successCount++;
                else errors.Add(result.Message);
            }

            string summary = $"Reparented {successCount} objects. Errors: {errors.Count}.";
            if (errors.Count > 0) summary += $"\nErrors:\n{string.Join("\n", errors)}";
            return errors.Count == 0 ? ActionResult.Ok(summary) : ActionResult.Fail(summary);
        }

        // ─────────────────────────────────────────────────────
        //  2.7 Rename
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// Renames a GameObject. Undo-safe.
        /// </summary>
        public static ActionResult Rename(string objectPath, string newName)
        {
            if (!PathResolver.ResolveRootPath(objectPath, out var targets) || targets.Count == 0)
                return ActionResult.Fail($"Object not found: {objectPath}");

            var go = targets[0];
            Undo.RecordObject(go, $"Rename {go.name} to {newName}");
            go.name = newName;
            EditorUtility.SetDirty(go);
            return ActionResult.Ok($"Renamed: {objectPath} → {newName}", go);
        }

        // ─────────────────────────────────────────────────────
        //  2.8 SetState
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// Sets active/inactive state, tag, layer, or static flags on a GameObject.
        /// </summary>
        public static ActionResult SetState(
            string objectPath,
            bool? active = null,
            string tag = null,
            string layer = null,
            string staticFlags = null,
            bool applyToChildren = false)
        {
            if (!PathResolver.ResolveRootPath(objectPath, out var targets) || targets.Count == 0)
                return ActionResult.Fail($"Object not found: {objectPath}");

            var go = targets[0];
            var affectedObjects = applyToChildren ? CollectAllChildren(go) : new List<GameObject> { go };

            foreach (var obj in affectedObjects)
            {
                Undo.RecordObject(obj, "Set State");

                if (active.HasValue)
                    obj.SetActive(active.Value);

                if (tag != null)
                {
                    try { obj.tag = tag; }
                    catch { Debug.LogWarning($"[AgentBridge] Tag not found: {tag}"); }
                }

                if (layer != null)
                {
                    int layerIndex = LayerMask.NameToLayer(layer);
                    if (layerIndex >= 0) obj.layer = layerIndex;
                    else Debug.LogWarning($"[AgentBridge] Layer not found: {layer}");
                }

                if (staticFlags != null)
                {
                    StaticEditorFlags flags = ParseStaticFlags(staticFlags);
                    GameObjectUtility.SetStaticEditorFlags(obj, flags);
                }

                EditorUtility.SetDirty(obj);
            }

            EditorSceneManager.MarkSceneDirty(go.scene);
            return ActionResult.Ok($"Set state on {objectPath} (affected: {affectedObjects.Count})", go);
        }

        // ─────────────────────────────────────────────────────
        //  2.9 ManageScene
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// Scene management: load, unload, create, save, save_as.
        /// </summary>
        public static ActionResult ManageScene(
            string operation,
            string scenePath = null,
            bool additive = true)
        {
            switch (operation.ToLower())
            {
                case "load":
                    if (string.IsNullOrEmpty(scenePath))
                        return ActionResult.Fail("scenePath required for load");
                    var mode = additive
                        ? OpenSceneMode.Additive
                        : OpenSceneMode.Single;
                    var loadedScene = EditorSceneManager.OpenScene(scenePath, mode);
                    return ActionResult.Ok($"Loaded scene: {loadedScene.name} ({(additive ? "additive" : "single")})");

                case "unload":
                    if (string.IsNullOrEmpty(scenePath))
                        return ActionResult.Fail("scenePath required for unload");
                    var sceneToUnload = SceneManager.GetSceneByPath(scenePath);
                    if (!sceneToUnload.isLoaded)
                        return ActionResult.Fail($"Scene not loaded: {scenePath}");
                    EditorSceneManager.CloseScene(sceneToUnload, true);
                    return ActionResult.Ok($"Unloaded scene: {scenePath}");

                case "create":
                    if (string.IsNullOrEmpty(scenePath))
                        return ActionResult.Fail("scenePath required for create");
                    var newScene = EditorSceneManager.NewScene(
                        NewSceneSetup.DefaultGameObjects,
                        NewSceneMode.Additive);
                    EditorSceneManager.SaveScene(newScene, scenePath);
                    return ActionResult.Ok($"Created new scene: {scenePath}");

                case "save":
                    EditorSceneManager.SaveOpenScenes();
                    return ActionResult.Ok("Saved all open scenes");

                case "save_as":
                    if (string.IsNullOrEmpty(scenePath))
                        return ActionResult.Fail("scenePath required for save_as");
                    var activeScene = SceneManager.GetActiveScene();
                    EditorSceneManager.SaveScene(activeScene, scenePath);
                    return ActionResult.Ok($"Saved active scene as: {scenePath}");

                default:
                    return ActionResult.Fail($"Unknown scene operation: {operation}");
            }
        }

        // ─────────────────────────────────────────────────────
        //  Editor Menu Items (testing)
        // ─────────────────────────────────────────────────────

        [MenuItem("Axiom/AgentBridge/Actions/Create Test GameObject")]
        public static void MenuCreateTest()
        {
            var result = CreateGameObject("AgentBridge_TestObject",
                components: new[] { "BoxCollider" },
                tag: "Untagged",
                localPosition: new Vector3(0, 1, 0));
            Debug.Log($"[AgentBridge] {result.Message}");
        }

        [MenuItem("Axiom/AgentBridge/Actions/Destroy Test GameObject")]
        public static void MenuDestroyTest()
        {
            var result = DestroyGameObject("AgentBridge_TestObject");
            Debug.Log($"[AgentBridge] {result.Message}");
        }

        [MenuItem("Axiom/AgentBridge/Actions/Save All Scenes")]
        public static void MenuSaveScenes()
        {
            var result = ManageScene("save");
            Debug.Log($"[AgentBridge] {result.Message}");
        }

        // ─────────────────────────────────────────────────────
        //  Private Helpers
        // ─────────────────────────────────────────────────────

        private static List<GameObject> CollectAllChildren(GameObject root)
        {
            var result = new List<GameObject> { root };
            foreach (Transform child in root.transform)
                result.AddRange(CollectAllChildren(child.gameObject));
            return result;
        }

        private static int GetDepth(Transform t)
        {
            int depth = 0;
            while (t.parent != null) { depth++; t = t.parent; }
            return depth;
        }

        private static StaticEditorFlags ParseStaticFlags(string value)
        {
            if (value.Equals("Everything", StringComparison.OrdinalIgnoreCase))
                return (StaticEditorFlags)~0;
            if (value.Equals("Nothing", StringComparison.OrdinalIgnoreCase))
                return 0;

            StaticEditorFlags result = 0;
            foreach (var part in value.Split(','))
            {
                string trimmed = part.Trim();
                if (Enum.TryParse<StaticEditorFlags>(trimmed, true, out var flag))
                    result |= flag;
                else
                    Debug.LogWarning($"[AgentBridge] Unknown StaticEditorFlag: {trimmed}");
            }
            return result;
        }
    }

    // ─────────────────────────────────────────────────────
    //  GameObjectDefinition (used by BatchCreate)
    // ─────────────────────────────────────────────────────

    [Serializable]
    public class GameObjectDefinition
    {
        public string name;
        public string parentPath;
        public string[] components;
        public string tag;
        public string layer;
    }
}
