using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEditor;
using UnityEngine.SceneManagement;
using Axiom.Editor.AgentBridge.Core;

namespace Axiom.Editor.AgentBridge.Diagnostics
{
    public enum ReferenceScannerMode
    {
        SceneReferences,                // Mode A: all null/missing references in loaded scene(s)
        PrefabReferences,               // Mode B: prefab assets with missing references
        CrossSceneReferences,           // Mode C: references that cross scene boundaries
        ScriptableObjectReferences,     // Mode D: ScriptableObject assets with missing references
        MissingScripts,                 // Mode E: GameObjects with "Missing (Mono Script)" components
        MaterialAudit,                  // Mode F: materials with missing shaders or textures
        FullProjectScan                 // Mode G: all modes combined
    }

    /// <summary>
    /// Broad-sweep reference integrity scanner. Finds missing references, missing scripts, and broken materials.
    /// </summary>
    public static class ReferenceScanner
    {
        // ─────────────────────────────────────────────────────
        //  Public Entry Point
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// Runs a reference integrity scan.
        /// </summary>
        /// <param name="mode">What to scan for.</param>
        /// <param name="rootPath">For SceneReferences/MissingScripts: breadcrumb path to scope. Null = all.</param>
        /// <param name="assetPath">For MaterialAudit: project-relative folder path (e.g., "Assets/Materials").
        /// Null = scan all materials in project.</param>
        /// <param name="includeInactive">Include inactive GameObjects in scene scans. Default true.</param>
        /// <returns>File path of the generated report.</returns>
        public static string GenerateReport(
            ReferenceScannerMode mode,
            string rootPath = null,
            string assetPath = null,
            bool includeInactive = true)
        {
            string content;
            switch (mode)
            {
                case ReferenceScannerMode.SceneReferences:
                    content = BuildSceneReferences(rootPath, includeInactive);
                    break;
                case ReferenceScannerMode.PrefabReferences:
                    content = BuildPrefabReferences(assetPath);
                    break;
                case ReferenceScannerMode.CrossSceneReferences:
                    content = BuildCrossSceneReferences();
                    break;
                case ReferenceScannerMode.ScriptableObjectReferences:
                    content = BuildScriptableObjectReferences(assetPath);
                    break;
                case ReferenceScannerMode.MissingScripts:
                    content = BuildMissingScripts(rootPath, includeInactive);
                    break;
                case ReferenceScannerMode.MaterialAudit:
                    content = BuildMaterialAudit(assetPath);
                    break;
                case ReferenceScannerMode.FullProjectScan:
                    content = BuildFullProjectScan(rootPath, assetPath, includeInactive);
                    break;
                default:
                    content = $"# Reference Scanner — Unknown Mode\n\nMode {mode} is not implemented.";
                    break;
            }

            return OutputWriter.WriteReport("reference_scanner", content);
        }

        // ─────────────────────────────────────────────────────
        //  Recursive GameObject Collection
        // ─────────────────────────────────────────────────────

        private static void CollectAll(Transform t, bool includeInactive, List<GameObject> results)
        {
            if (!includeInactive && !t.gameObject.activeInHierarchy) return;
            results.Add(t.gameObject);
            for (int i = 0; i < t.childCount; i++)
                CollectAll(t.GetChild(i), includeInactive, results);
        }

        private static List<GameObject> ResolveAndCollect(string rootPath, bool includeInactive)
        {
            var results = new List<GameObject>();
            if (!PathResolver.ResolveRootPath(rootPath, out var roots))
            {
                Debug.LogWarning($"[AgentBridge] ReferenceScanner: Could not resolve rootPath \"{rootPath}\".");
                return results;
            }
            foreach (GameObject root in roots)
                CollectAll(root.transform, includeInactive, results);
            return results;
        }

        // ─────────────────────────────────────────────────────
        //  Mode A: Scene References
        // ─────────────────────────────────────────────────────

        private static string BuildSceneReferences(string rootPath, bool includeInactive)
        {
            string scopeLabel = string.IsNullOrEmpty(rootPath) ? "ALL" : rootPath;
            string sceneLabel = PathResolver.GetLoadedSceneNames();

            var missingRefs = new List<(string goPath, string compName, string propPath, string expectedType)>();
            var missingScripts = new List<string>();

            List<GameObject> targets = ResolveAndCollect(rootPath, includeInactive);
            int totalComponents = 0;

            foreach (GameObject go in targets)
            {
                string goPath = PathResolver.GetHierarchyPath(go.transform);
                Component[] components = go.GetComponents<Component>();

                for (int ci = 0; ci < components.Length; ci++)
                {
                    Component comp = components[ci];
                    if (comp == null)
                    {
                        missingScripts.Add(goPath);
                        continue;
                    }

                    totalComponents++;

                    var so = new SerializedObject(comp);
                    var iterator = so.GetIterator();
                    bool enterChildren = true;
                    while (iterator.NextVisible(enterChildren))
                    {
                        enterChildren = false;
                        if (iterator.propertyType != SerializedPropertyType.ObjectReference) continue;

                        if (iterator.objectReferenceValue == null && iterator.objectReferenceInstanceIDValue != 0)
                            missingRefs.Add((goPath, comp.GetType().Name, iterator.propertyPath, iterator.type));
                    }
                }
            }

            bool hasIssues = missingRefs.Count > 0 || missingScripts.Count > 0;

            var sb = new StringBuilder();
            sb.AppendLine($"# Reference Scanner — Mode: Scene References | Scope: {scopeLabel} | Scene: {sceneLabel}");
            sb.AppendLine();

            // Missing object references
            sb.AppendLine("## Missing Object References");
            if (missingRefs.Count == 0)
            {
                sb.AppendLine("*None.*");
            }
            else
            {
                sb.AppendLine("| # | GameObject Path | Component | Property | Expected Type |");
                sb.AppendLine("| :--- | :--- | :--- | :--- | :--- |");
                for (int i = 0; i < missingRefs.Count; i++)
                {
                    var (goPath, compName, propPath, expectedType) = missingRefs[i];
                    sb.AppendLine($"| {i + 1} | {goPath} | {compName} | {propPath} | {expectedType} |");
                }
            }
            sb.AppendLine();

            // Missing scripts
            sb.AppendLine("## Missing Scripts");
            if (missingScripts.Count == 0)
            {
                sb.AppendLine("*None.*");
            }
            else
            {
                sb.AppendLine("| # | GameObject Path |");
                sb.AppendLine("| :--- | :--- |");
                for (int i = 0; i < missingScripts.Count; i++)
                    sb.AppendLine($"| {i + 1} | {missingScripts[i]} |");
            }
            sb.AppendLine();

            // Summary
            sb.AppendLine("## Summary");
            sb.AppendLine($"**Status:** {(hasIssues ? "ISSUES FOUND" : "ALL CLEAR")}");
            sb.AppendLine($"- Missing references: {missingRefs.Count}");
            sb.AppendLine($"- Missing scripts: {missingScripts.Count}");
            sb.AppendLine($"- Total GameObjects scanned: {targets.Count}");
            sb.AppendLine($"- Total components scanned: {totalComponents}");
            sb.AppendLine();
            sb.AppendLine("---");
            sb.Append($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            return sb.ToString();
        }

        // ─────────────────────────────────────────────────────
        //  Mode E: Missing Scripts
        // ─────────────────────────────────────────────────────

        private static string BuildMissingScripts(string rootPath, bool includeInactive)
        {
            string scopeLabel = string.IsNullOrEmpty(rootPath) ? "ALL" : rootPath;
            string sceneLabel = PathResolver.GetLoadedSceneNames();

            var missingScripts = new List<(string goPath, int compIndex, bool parentActive, bool selfActive)>();

            List<GameObject> targets = ResolveAndCollect(rootPath, includeInactive);

            foreach (GameObject go in targets)
            {
                string goPath = PathResolver.GetHierarchyPath(go.transform);
                Component[] components = go.GetComponents<Component>();

                for (int i = 0; i < components.Length; i++)
                {
                    if (components[i] == null)
                    {
                        bool parentActive = go.transform.parent == null || go.transform.parent.gameObject.activeInHierarchy;
                        missingScripts.Add((goPath, i, parentActive, go.activeSelf));
                    }
                }
            }

            bool hasIssues = missingScripts.Count > 0;

            var sb = new StringBuilder();
            sb.AppendLine($"# Reference Scanner — Mode: Missing Scripts | Scope: {scopeLabel} | Scene: {sceneLabel}");
            sb.AppendLine();

            if (missingScripts.Count == 0)
            {
                sb.AppendLine("*No missing scripts found.*");
            }
            else
            {
                sb.AppendLine("| # | GameObject Path | Component Index | Parent Active | Self Active |");
                sb.AppendLine("| :--- | :--- | :--- | :--- | :--- |");
                for (int i = 0; i < missingScripts.Count; i++)
                {
                    var (goPath, idx, parentActive, selfActive) = missingScripts[i];
                    sb.AppendLine($"| {i + 1} | {goPath} | [{idx}] | {parentActive} | {selfActive} |");
                }
            }

            sb.AppendLine();
            sb.AppendLine("## Summary");
            sb.AppendLine($"**Status:** {(hasIssues ? "ISSUES FOUND" : "ALL CLEAR")}");
            sb.AppendLine($"- Missing scripts: {missingScripts.Count}");
            sb.AppendLine($"- Total GameObjects scanned: {targets.Count}");
            sb.AppendLine();
            sb.AppendLine("---");
            sb.Append($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            return sb.ToString();
        }

        // ─────────────────────────────────────────────────────
        //  Mode F: Material Audit
        // ─────────────────────────────────────────────────────

        private static string BuildMaterialAudit(string assetPath)
        {
            string searchPath = assetPath ?? "Assets";
            string pathLabel = assetPath ?? "Assets (all)";

            var brokenShaders = new List<(string matPath, string shaderName)>();
            var nullTextures = new List<(string matPath, string shaderName, string propName, string slotName)>();

            string[] materialGuids = AssetDatabase.FindAssets("t:Material", new[] { searchPath });

            foreach (string guid in materialGuids)
            {
                string matPath = AssetDatabase.GUIDToAssetPath(guid);
                Material mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                if (mat == null) continue;

                // Check shader
                if (mat.shader == null || mat.shader.name == "Hidden/InternalErrorShader")
                {
                    brokenShaders.Add((matPath, mat.shader?.name ?? "null"));
                    continue; // Skip texture check for pink materials — data is unreliable
                }

                // Check texture slots
                var shader = mat.shader;
                int propCount = ShaderUtil.GetPropertyCount(shader);
                for (int i = 0; i < propCount; i++)
                {
                    if (ShaderUtil.GetPropertyType(shader, i) != ShaderUtil.ShaderPropertyType.TexEnv) continue;

                    string propName = ShaderUtil.GetPropertyName(shader, i);
                    string slotName = ShaderUtil.GetPropertyDescription(shader, i);
                    Texture tex = mat.GetTexture(propName);

                    if (tex == null)
                        nullTextures.Add((matPath, shader.name, propName, slotName));
                }
            }

            bool hasIssues = brokenShaders.Count > 0 || nullTextures.Count > 0;

            var sb = new StringBuilder();
            sb.AppendLine($"# Reference Scanner — Mode: Material Audit | Path: {pathLabel}");
            sb.AppendLine();

            // Broken shaders
            sb.AppendLine("## Broken Shaders (ERROR — will render pink)");
            if (brokenShaders.Count == 0)
            {
                sb.AppendLine("*None.*");
            }
            else
            {
                sb.AppendLine("| # | Material Path | Shader Name |");
                sb.AppendLine("| :--- | :--- | :--- |");
                for (int i = 0; i < brokenShaders.Count; i++)
                    sb.AppendLine($"| {i + 1} | {brokenShaders[i].matPath} | {brokenShaders[i].shaderName} |");
            }
            sb.AppendLine();

            // Null texture slots
            sb.AppendLine("## Null Texture Slots (WARNING — may be intentional)");
            if (nullTextures.Count == 0)
            {
                sb.AppendLine("*None.*");
            }
            else
            {
                sb.AppendLine("| # | Material Path | Shader | Property | Slot Name |");
                sb.AppendLine("| :--- | :--- | :--- | :--- | :--- |");
                for (int i = 0; i < nullTextures.Count; i++)
                {
                    var (mp, sn, pn, sl) = nullTextures[i];
                    sb.AppendLine($"| {i + 1} | {mp} | {sn} | {pn} | {sl} |");
                }
            }
            sb.AppendLine();

            // Summary
            sb.AppendLine("## Summary");
            sb.AppendLine($"**Status:** {(hasIssues ? "ISSUES FOUND" : "ALL CLEAR")}");
            sb.AppendLine($"- Broken shaders: {brokenShaders.Count} (ERROR)");
            sb.AppendLine($"- Null texture slots: {nullTextures.Count} (WARNING)");
            sb.AppendLine($"- Total materials scanned: {materialGuids.Length}");
            sb.AppendLine();
            sb.AppendLine("---");
            sb.Append($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            return sb.ToString();
        }

        // ─────────────────────────────────────────────────────
        //  Editor Menu Items
        // ─────────────────────────────────────────────────────

        [MenuItem("Axiom/AgentBridge/Reference Scanner — Mode A (Scene References)")]
        public static void MenuModeA()
        {
            string path = GenerateReport(ReferenceScannerMode.SceneReferences);
            Debug.Log($"[AgentBridge] Reference Scanner report: {path}");
        }

        [MenuItem("Axiom/AgentBridge/Reference Scanner — Mode B (Prefab References)")]
        public static void MenuModeB()
        {
            string path = GenerateReport(ReferenceScannerMode.PrefabReferences);
            Debug.Log($"[AgentBridge] Reference Scanner report: {path}");
        }

        [MenuItem("Axiom/AgentBridge/Reference Scanner — Mode D (ScriptableObject References)")]
        public static void MenuModeD()
        {
            string path = GenerateReport(ReferenceScannerMode.ScriptableObjectReferences);
            Debug.Log($"[AgentBridge] Reference Scanner report: {path}");
        }

        [MenuItem("Axiom/AgentBridge/Reference Scanner — Mode E (Missing Scripts)")]
        public static void MenuModeE()
        {
            string path = GenerateReport(ReferenceScannerMode.MissingScripts);
            Debug.Log($"[AgentBridge] Reference Scanner report: {path}");
        }

        [MenuItem("Axiom/AgentBridge/Reference Scanner — Mode F (Material Audit)")]
        public static void MenuModeF()
        {
            string path = GenerateReport(ReferenceScannerMode.MaterialAudit);
            Debug.Log($"[AgentBridge] Reference Scanner report: {path}");
        }

        [MenuItem("Axiom/AgentBridge/Reference Scanner — Mode G (Full Project Scan)")]
        public static void MenuModeG()
        {
            string path = GenerateReport(ReferenceScannerMode.FullProjectScan);
            Debug.Log($"[AgentBridge] Reference Scanner report: {path}");
        }

        // ─────────────────────────────────────────────────────
        //  Mode B: Prefab References
        // ─────────────────────────────────────────────────────

        private static string BuildPrefabReferences(string assetPath)
        {
            string searchPath = assetPath ?? "Assets";
            string pathLabel = assetPath ?? "Assets";

            var missingRefs = new List<(string prefabPath, string goName, string compName, string propPath, string expectedType)>();
            var missingScripts = new List<(string prefabPath, string goName)>();

            string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { searchPath });

            foreach (string guid in prefabGuids)
            {
                string prefabPath = AssetDatabase.GUIDToAssetPath(guid);
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                if (prefab == null) continue;

                var allTransforms = prefab.GetComponentsInChildren<Transform>(true);
                foreach (var t in allTransforms)
                {
                    Component[] components = t.gameObject.GetComponents<Component>();
                    for (int ci = 0; ci < components.Length; ci++)
                    {
                        Component comp = components[ci];
                        if (comp == null)
                        {
                            missingScripts.Add((prefabPath, t.gameObject.name));
                            continue;
                        }
                        if (comp.GetType() == typeof(Transform)) continue;

                        var so = new SerializedObject(comp);
                        var iterator = so.GetIterator();
                        bool enterChildren = true;
                        while (iterator.NextVisible(enterChildren))
                        {
                            enterChildren = false;
                            if (iterator.propertyType != SerializedPropertyType.ObjectReference) continue;
                            if (iterator.objectReferenceValue == null && iterator.objectReferenceInstanceIDValue != 0)
                                missingRefs.Add((prefabPath, t.gameObject.name, comp.GetType().Name, iterator.propertyPath, iterator.type));
                        }
                    }
                }
            }

            bool hasIssues = missingRefs.Count > 0 || missingScripts.Count > 0;
            var sb = new StringBuilder();
            sb.AppendLine($"# Reference Scanner — Mode: Prefab References | Path: {pathLabel}");
            sb.AppendLine();

            sb.AppendLine("## Missing References in Prefabs");
            if (missingRefs.Count == 0)
            {
                sb.AppendLine("*None.*");
            }
            else
            {
                sb.AppendLine("| # | Prefab Path | GameObject | Component | Property | Expected Type |");
                sb.AppendLine("| :--- | :--- | :--- | :--- | :--- | :--- |");
                for (int i = 0; i < missingRefs.Count; i++)
                {
                    var (pp, gn, cn, prop, et) = missingRefs[i];
                    sb.AppendLine($"| {i + 1} | {pp} | {gn} | {cn} | {prop} | {et} |");
                }
            }
            sb.AppendLine();

            sb.AppendLine("## Missing Scripts in Prefabs");
            if (missingScripts.Count == 0)
            {
                sb.AppendLine("*None.*");
            }
            else
            {
                sb.AppendLine("| # | Prefab Path | GameObject |");
                sb.AppendLine("| :--- | :--- | :--- |");
                for (int i = 0; i < missingScripts.Count; i++)
                    sb.AppendLine($"| {i + 1} | {missingScripts[i].prefabPath} | {missingScripts[i].goName} |");
            }
            sb.AppendLine();

            sb.AppendLine("## Summary");
            sb.AppendLine($"**Status:** {(hasIssues ? "ISSUES FOUND" : "ALL CLEAR")}");
            sb.AppendLine($"- Prefabs scanned: {prefabGuids.Length}");
            sb.AppendLine($"- Missing references: {missingRefs.Count}");
            sb.AppendLine($"- Missing scripts: {missingScripts.Count}");
            sb.AppendLine();
            sb.AppendLine("---");
            sb.Append($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            return sb.ToString();
        }

        // ─────────────────────────────────────────────────────
        //  Mode C: Cross-Scene References
        // ─────────────────────────────────────────────────────

        private static string BuildCrossSceneReferences()
        {
            int loadedSceneCount = SceneManager.loadedSceneCount;
            var crossRefs = new List<(string srcScene, string srcObj, string compName, string propName, string tgtScene, string tgtObj)>();
            int totalComponents = 0;

            for (int si = 0; si < loadedSceneCount; si++)
            {
                var scene = SceneManager.GetSceneAt(si);
                if (!scene.isLoaded) continue;

                foreach (GameObject rootGo in scene.GetRootGameObjects())
                {
                    var allGOs = new List<GameObject>();
                    CollectAllGOs(rootGo.transform, allGOs);

                    foreach (GameObject go in allGOs)
                    {
                        string goPath = PathResolver.GetHierarchyPath(go.transform);
                        foreach (Component comp in go.GetComponents<Component>())
                        {
                            if (comp == null) continue;
                            if (comp.GetType() == typeof(Transform)) continue;
                            totalComponents++;

                            var so = new SerializedObject(comp);
                            var iter = so.GetIterator();
                            bool enter = true;
                            while (iter.NextVisible(enter))
                            {
                                enter = false;
                                if (iter.propertyType != SerializedPropertyType.ObjectReference) continue;
                                var refObj = iter.objectReferenceValue;
                                if (refObj == null) continue;

                                // Check if it's a scene object in a different scene
                                if (refObj is GameObject refGO && refGO.scene.IsValid() && refGO.scene != scene)
                                {
                                    crossRefs.Add((scene.name, goPath, comp.GetType().Name,
                                        iter.propertyPath, refGO.scene.name, refGO.name));
                                }
                                else if (refObj is Component refComp && refComp.gameObject.scene.IsValid() && refComp.gameObject.scene != scene)
                                {
                                    crossRefs.Add((scene.name, goPath, comp.GetType().Name,
                                        iter.propertyPath, refComp.gameObject.scene.name, refComp.gameObject.name));
                                }
                            }
                        }
                    }
                }
            }

            var sb = new StringBuilder();
            sb.AppendLine($"# Reference Scanner — Mode: Cross-Scene References | Scenes Loaded: {loadedSceneCount}");
            sb.AppendLine();

            if (loadedSceneCount <= 1)
            {
                sb.AppendLine("*Only one scene loaded. Cross-scene references require multiple loaded scenes.*");
            }
            else if (crossRefs.Count == 0)
            {
                sb.AppendLine("*No cross-scene references found.*");
            }
            else
            {
                sb.AppendLine("## Cross-Scene References Found");
                sb.AppendLine("| # | Source Scene | Source Object | Component | Property | Target Scene | Target Object |");
                sb.AppendLine("| :--- | :--- | :--- | :--- | :--- | :--- | :--- |");
                for (int i = 0; i < crossRefs.Count; i++)
                {
                    var (ss, so, cn, pn, ts, to) = crossRefs[i];
                    sb.AppendLine($"| {i + 1} | {ss} | {so} | {cn} | {pn} | {ts} | {to} |");
                }
            }
            sb.AppendLine();

            sb.AppendLine("## Summary");
            sb.AppendLine($"**Status:** {(crossRefs.Count > 0 ? "ISSUES FOUND" : "ALL CLEAR")}");
            sb.AppendLine($"- Cross-scene references: {crossRefs.Count}");
            sb.AppendLine($"- Scenes scanned: {loadedSceneCount}");
            sb.AppendLine($"- Total components scanned: {totalComponents}");
            sb.AppendLine();
            sb.AppendLine("---");
            sb.Append($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            return sb.ToString();
        }

        private static void CollectAllGOs(Transform t, List<GameObject> results)
        {
            results.Add(t.gameObject);
            for (int i = 0; i < t.childCount; i++)
                CollectAllGOs(t.GetChild(i), results);
        }

        // ─────────────────────────────────────────────────────
        //  Mode D: ScriptableObject References
        // ─────────────────────────────────────────────────────

        private static string BuildScriptableObjectReferences(string assetPath)
        {
            string searchPath = assetPath ?? "Assets";
            string pathLabel = assetPath ?? "Assets";

            var missingRefs = new List<(string soPath, string soType, string propPath, string expectedType)>();

            string[] soGuids = AssetDatabase.FindAssets("t:ScriptableObject", new[] { searchPath });

            foreach (string guid in soGuids)
            {
                string soPath = AssetDatabase.GUIDToAssetPath(guid);
                ScriptableObject so = AssetDatabase.LoadAssetAtPath<ScriptableObject>(soPath);
                if (so == null) continue;

                var serializedObj = new SerializedObject(so);
                var iterator = serializedObj.GetIterator();
                bool enterChildren = true;
                while (iterator.NextVisible(enterChildren))
                {
                    enterChildren = false;
                    if (iterator.propertyType != SerializedPropertyType.ObjectReference) continue;
                    if (iterator.objectReferenceValue == null && iterator.objectReferenceInstanceIDValue != 0)
                        missingRefs.Add((soPath, so.GetType().Name, iterator.propertyPath, iterator.type));
                }
            }

            bool hasIssues = missingRefs.Count > 0;
            var sb = new StringBuilder();
            sb.AppendLine($"# Reference Scanner — Mode: ScriptableObject References | Path: {pathLabel}");
            sb.AppendLine();

            sb.AppendLine("## Missing References in ScriptableObjects");
            if (missingRefs.Count == 0)
            {
                sb.AppendLine("*None.*");
            }
            else
            {
                sb.AppendLine("| # | Asset Path | SO Type | Property | Expected Type |");
                sb.AppendLine("| :--- | :--- | :--- | :--- | :--- |");
                for (int i = 0; i < missingRefs.Count; i++)
                {
                    var (sp, st, pp, et) = missingRefs[i];
                    sb.AppendLine($"| {i + 1} | {sp} | {st} | {pp} | {et} |");
                }
            }
            sb.AppendLine();

            sb.AppendLine("## Summary");
            sb.AppendLine($"**Status:** {(hasIssues ? "ISSUES FOUND" : "ALL CLEAR")}");
            sb.AppendLine($"- ScriptableObjects scanned: {soGuids.Length}");
            sb.AppendLine($"- Missing references: {missingRefs.Count}");
            sb.AppendLine();
            sb.AppendLine("---");
            sb.Append($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            return sb.ToString();
        }

        // ─────────────────────────────────────────────────────
        //  Mode G: Full Project Scan
        // ─────────────────────────────────────────────────────

        private static string BuildFullProjectScan(string rootPath, string assetPath, bool includeInactive)
        {
            try { EditorUtility.DisplayProgressBar("Full Project Scan", "Scanning scene references...", 0.05f); }
            catch { }

            string modeA, modeB, modeC, modeD, modeE, modeF;

            try { EditorUtility.DisplayProgressBar("Full Project Scan", "Mode A: Scene references...", 0.1f); }
            catch { }
            modeA = BuildSceneReferences(rootPath, includeInactive);

            try { EditorUtility.DisplayProgressBar("Full Project Scan", "Mode B: Prefab references...", 0.25f); }
            catch { }
            modeB = BuildPrefabReferences(assetPath);

            try { EditorUtility.DisplayProgressBar("Full Project Scan", "Mode C: Cross-scene references...", 0.4f); }
            catch { }
            modeC = BuildCrossSceneReferences();

            try { EditorUtility.DisplayProgressBar("Full Project Scan", "Mode D: ScriptableObject references...", 0.55f); }
            catch { }
            modeD = BuildScriptableObjectReferences(assetPath);

            try { EditorUtility.DisplayProgressBar("Full Project Scan", "Mode E: Missing scripts...", 0.7f); }
            catch { }
            modeE = BuildMissingScripts(rootPath, includeInactive);

            try { EditorUtility.DisplayProgressBar("Full Project Scan", "Mode F: Material audit...", 0.85f); }
            catch { }
            modeF = BuildMaterialAudit(assetPath);

            try { EditorUtility.ClearProgressBar(); }
            catch { }

            // Count issues in each section
            bool hasSceneIssues   = !modeA.Contains("ALL CLEAR");
            bool hasPrefabIssues  = !modeB.Contains("ALL CLEAR");
            bool hasCrossScene    = !modeC.Contains("ALL CLEAR");
            bool hasSOIssues      = !modeD.Contains("ALL CLEAR");
            bool hasMissingScript = !modeE.Contains("ALL CLEAR");
            bool hasMaterialIssues= !modeF.Contains("ALL CLEAR");

            bool anyIssues = hasSceneIssues || hasPrefabIssues || hasCrossScene || hasSOIssues || hasMissingScript || hasMaterialIssues;

            var sb = new StringBuilder();
            sb.AppendLine("# Reference Scanner — FULL PROJECT SCAN");
            sb.AppendLine();

            sb.AppendLine("## 1. Scene References (Mode A)");
            sb.AppendLine(modeA);
            sb.AppendLine();

            sb.AppendLine("## 2. Prefab References (Mode B)");
            sb.AppendLine(modeB);
            sb.AppendLine();

            sb.AppendLine("## 3. Cross-Scene References (Mode C)");
            sb.AppendLine(modeC);
            sb.AppendLine();

            sb.AppendLine("## 4. ScriptableObject References (Mode D)");
            sb.AppendLine(modeD);
            sb.AppendLine();

            sb.AppendLine("## 5. Missing Scripts (Mode E)");
            sb.AppendLine(modeE);
            sb.AppendLine();

            sb.AppendLine("## 6. Material Audit (Mode F)");
            sb.AppendLine(modeF);
            sb.AppendLine();

            sb.AppendLine("## OVERALL SUMMARY");
            sb.AppendLine($"**Status:** {(anyIssues ? "ISSUES FOUND" : "ALL CLEAR")}");
            sb.AppendLine($"- Scene missing refs: {(hasSceneIssues ? "ISSUES" : "CLEAN")}");
            sb.AppendLine($"- Prefab missing refs: {(hasPrefabIssues ? "ISSUES" : "CLEAN")}");
            sb.AppendLine($"- Cross-scene refs: {(hasCrossScene ? "ISSUES" : "CLEAN")}");
            sb.AppendLine($"- SO missing refs: {(hasSOIssues ? "ISSUES" : "CLEAN")}");
            sb.AppendLine($"- Missing scripts: {(hasMissingScript ? "ISSUES" : "CLEAN")}");
            sb.AppendLine($"- Broken materials: {(hasMaterialIssues ? "ISSUES" : "CLEAN")}");
            sb.AppendLine();
            sb.AppendLine("---");
            sb.Append($"Full scan completed | Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            return sb.ToString();
        }
    }
}
