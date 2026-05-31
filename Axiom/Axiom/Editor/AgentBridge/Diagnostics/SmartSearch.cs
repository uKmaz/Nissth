using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEditor;
using Axiom.Editor.AgentBridge.Core;

namespace Axiom.Editor.AgentBridge.Diagnostics
{
    public enum SearchDomain
    {
        Scene,    // Search GameObjects in loaded scenes
        Assets,   // Search assets in the project folder
        Both      // Search both
    }

    /// <summary>
    /// Universal "where is X?" search engine. Finds GameObjects and assets by name, type, component, tag, or label.
    /// Returns exact hierarchy paths and asset paths usable immediately for further operations.
    /// </summary>
    public static class SmartSearch
    {
        // ─────────────────────────────────────────────────────
        //  Public Entry Point
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// Searches for GameObjects and/or assets matching the given criteria.
        /// All parameters are optional filters — the more you provide, the narrower the results.
        /// At least one search parameter must be provided.
        /// </summary>
        /// <param name="domain">Where to search: Scene, Assets, or Both.</param>
        /// <param name="name">Name or partial name to match (case-insensitive contains). Null = no name filter.</param>
        /// <param name="componentType">Scene only: objects with this component type name. Null = any.</param>
        /// <param name="tag">Scene only: objects with this tag. Null = any.</param>
        /// <param name="layer">Scene only: objects on this layer name. Null = any.</param>
        /// <param name="isActive">Scene only: filter by active-in-hierarchy state. Null = both.</param>
        /// <param name="isStatic">Scene only: filter by static flags. Null = both.</param>
        /// <param name="assetType">Assets only: Unity type filter (e.g., "Material", "Prefab"). Null = any.</param>
        /// <param name="assetExtension">Assets only: file extension filter (e.g., ".cs"). Null = any.</param>
        /// <param name="assetLabel">Assets only: Unity asset label filter. Null = any.</param>
        /// <param name="assetPath">Assets only: folder to search within. Null = "Assets".</param>
        /// <param name="includeInactive">Scene only: include inactive GameObjects. Default true.</param>
        /// <param name="maxResults">Maximum results per domain. Default 50.</param>
        /// <returns>File path of the generated report, or null if no criteria provided.</returns>
        public static string Search(
            SearchDomain domain = SearchDomain.Both,
            string name = null,
            string componentType = null,
            string tag = null,
            string layer = null,
            bool? isActive = null,
            bool? isStatic = null,
            string assetType = null,
            string assetExtension = null,
            string assetLabel = null,
            string assetPath = null,
            bool includeInactive = true,
            int maxResults = 50)
        {
            // Validate: at least one criterion required
            if (name == null && componentType == null && tag == null && layer == null
                && assetType == null && assetExtension == null && assetLabel == null)
            {
                Debug.LogError("[AgentBridge] SmartSearch: At least one search criterion required.");
                return null;
            }

            // Build query description for report header
            var queryParts = new List<string>();
            queryParts.Add($"domain={domain}");
            if (name != null)          queryParts.Add($"name=\"{name}\"");
            if (componentType != null) queryParts.Add($"componentType=\"{componentType}\"");
            if (tag != null)           queryParts.Add($"tag=\"{tag}\"");
            if (layer != null)         queryParts.Add($"layer=\"{layer}\"");
            if (isActive.HasValue)     queryParts.Add($"isActive={isActive.Value}");
            if (isStatic.HasValue)     queryParts.Add($"isStatic={isStatic.Value}");
            if (assetType != null)     queryParts.Add($"assetType=\"{assetType}\"");
            if (assetExtension != null) queryParts.Add($"assetExtension=\"{assetExtension}\"");
            if (assetLabel != null)    queryParts.Add($"assetLabel=\"{assetLabel}\"");
            if (assetPath != null)     queryParts.Add($"assetPath=\"{assetPath}\"");

            var sb = new StringBuilder();
            sb.AppendLine("# Smart Search Results");
            sb.AppendLine();
            sb.AppendLine($"**Query:** {string.Join(", ", queryParts)}");
            sb.AppendLine();

            int totalScene = 0;
            int totalAssets = 0;

            // Scene search
            if (domain == SearchDomain.Scene || domain == SearchDomain.Both)
            {
                var sceneResults = RunSceneSearch(name, componentType, tag, layer, isActive, isStatic,
                    includeInactive, maxResults);
                totalScene = sceneResults.Count;
                bool capped = totalScene >= maxResults;

                string headerCount = capped ? $"{maxResults}+" : totalScene.ToString();
                sb.AppendLine($"## Scene Results ({headerCount} found)");

                if (PathResolver.GetRootGameObjects().Count == 0)
                {
                    sb.AppendLine("*No scenes loaded.*");
                }
                else if (sceneResults.Count == 0)
                {
                    sb.AppendLine("*No results found in Scene.*");
                }
                else
                {
                    sb.AppendLine("| # | Hierarchy Path | Active | Layer | Tag | Components |");
                    sb.AppendLine("| :--- | :--- | :--- | :--- | :--- | :--- |");
                    for (int i = 0; i < sceneResults.Count; i++)
                    {
                        var r = sceneResults[i];
                        sb.AppendLine($"| {i + 1} | {r.path} | {(r.active ? "Yes" : "No")} | {r.layerName} | {r.objTag} | {r.components} |");
                    }

                    if (capped)
                        sb.AppendLine($"*Results capped at {maxResults}. Narrow your search for more specific results.*");
                }
                sb.AppendLine();
            }

            // Asset search
            if (domain == SearchDomain.Assets || domain == SearchDomain.Both)
            {
                var assetResults = RunAssetSearch(name, assetType, assetExtension, assetLabel,
                    assetPath ?? "Assets", maxResults);
                totalAssets = assetResults.Count;
                bool capped = totalAssets >= maxResults;

                string headerCount = capped ? $"{maxResults}+" : totalAssets.ToString();
                sb.AppendLine($"## Asset Results ({headerCount} found)");

                if (assetResults.Count == 0)
                {
                    sb.AppendLine("*No results found in Assets.*");
                }
                else
                {
                    sb.AppendLine("| # | Asset Path | Type | Size |");
                    sb.AppendLine("| :--- | :--- | :--- | :--- |");
                    for (int i = 0; i < assetResults.Count; i++)
                    {
                        var r = assetResults[i];
                        sb.AppendLine($"| {i + 1} | {r.path} | {r.typeName} | {FormatFileSize(r.size)} |");
                    }

                    if (capped)
                        sb.AppendLine($"*Results capped at {maxResults}. Narrow your search for more specific results.*");
                }
                sb.AppendLine();
            }

            sb.AppendLine("---");
            sb.Append($"Scene results: {totalScene} | Asset results: {totalAssets} | Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");

            return OutputWriter.WriteReport("smart_search", sb.ToString());
        }

        // ─────────────────────────────────────────────────────
        //  Scene Search
        // ─────────────────────────────────────────────────────

        private struct SceneResult
        {
            public string path;
            public bool active;
            public string layerName;
            public string objTag;
            public string components;
        }

        private static List<SceneResult> RunSceneSearch(
            string name, string componentType, string tag, string layer,
            bool? isActive, bool? isStatic, bool includeInactive, int maxResults)
        {
            var results = new List<SceneResult>();

            int layerInt = -1;
            if (layer != null)
            {
                layerInt = LayerMask.NameToLayer(layer);
                if (layerInt == -1)
                    Debug.LogWarning($"[AgentBridge] SmartSearch: Layer \"{layer}\" not found.");
            }

            var allRoots = PathResolver.GetRootGameObjects();
            foreach (var root in allRoots)
            {
                // GetComponentsInChildren<Transform>(includeInactive) returns all transforms including inactive
                var transforms = root.GetComponentsInChildren<Transform>(includeInactive);
                foreach (var t in transforms)
                {
                    if (results.Count >= maxResults) goto Done;

                    GameObject go = t.gameObject;
                    if (!EvalSceneFilters(go, name, componentType, tag, layerInt, isActive, isStatic))
                        continue;

                    string goPath = PathResolver.GetHierarchyPath(t);
                    string layerName = LayerMask.LayerToName(go.layer);

                    // Collect component names (exclude Transform)
                    var compNames = new List<string>();
                    foreach (Component c in go.GetComponents<Component>())
                    {
                        if (c == null) compNames.Add("MISSING_SCRIPT");
                        else if (c.GetType() != typeof(Transform)) compNames.Add(c.GetType().Name);
                    }

                    results.Add(new SceneResult
                    {
                        path = goPath,
                        active = go.activeInHierarchy,
                        layerName = layerName,
                        objTag = go.tag,
                        components = compNames.Count > 0 ? string.Join(", ", compNames) : "—"
                    });
                }
            }

            Done:
            return results;
        }

        private static bool EvalSceneFilters(
            GameObject go, string name, string componentType, string tag,
            int layerInt, bool? isActive, bool? isStatic)
        {
            if (name != null && go.name.IndexOf(name, StringComparison.OrdinalIgnoreCase) < 0)
                return false;

            if (tag != null && !go.CompareTag(tag))
                return false;

            if (layerInt >= 0 && go.layer != layerInt)
                return false;

            if (isActive.HasValue && go.activeInHierarchy != isActive.Value)
                return false;

            if (isStatic.HasValue && go.isStatic != isStatic.Value)
                return false;

            if (componentType != null)
            {
                bool found = false;
                foreach (Component c in go.GetComponents<Component>())
                {
                    if (c != null && string.Equals(c.GetType().Name, componentType, StringComparison.OrdinalIgnoreCase))
                    {
                        found = true;
                        break;
                    }
                }
                if (!found) return false;
            }

            return true;
        }

        // ─────────────────────────────────────────────────────
        //  Asset Search
        // ─────────────────────────────────────────────────────

        private struct AssetResult
        {
            public string path;
            public string typeName;
            public long size;
        }

        private static List<AssetResult> RunAssetSearch(
            string name, string assetType, string assetExtension,
            string assetLabel, string searchFolder, int maxResults)
        {
            // Build FindAssets filter string
            var filterParts = new List<string>();
            if (!string.IsNullOrEmpty(name))       filterParts.Add(name);
            if (!string.IsNullOrEmpty(assetType))  filterParts.Add($"t:{assetType}");
            if (!string.IsNullOrEmpty(assetLabel)) filterParts.Add($"l:{assetLabel}");
            string filter = string.Join(" ", filterParts);

            string[] guids = AssetDatabase.FindAssets(filter, new[] { searchFolder });

            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            var results = new List<AssetResult>();

            foreach (string guid in guids)
            {
                if (results.Count >= maxResults) break;

                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (AssetDatabase.IsValidFolder(path)) continue;
                if (path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) continue;

                if (assetExtension != null && !path.EndsWith(assetExtension, StringComparison.OrdinalIgnoreCase))
                    continue;

                Type mainType = AssetDatabase.GetMainAssetTypeAtPath(path);
                string typeName = mainType != null ? mainType.Name : "Unknown";

                long fileSize = 0;
                string fullPath = Path.Combine(projectRoot, path.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(fullPath))
                    fileSize = new FileInfo(fullPath).Length;

                results.Add(new AssetResult { path = path, typeName = typeName, size = fileSize });
            }

            return results;
        }

        // ─────────────────────────────────────────────────────
        //  Helpers
        // ─────────────────────────────────────────────────────

        private static string FormatFileSize(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            return $"{bytes / (1024.0 * 1024.0):F1} MB";
        }

        // ─────────────────────────────────────────────────────
        //  Editor Menu Items
        // ─────────────────────────────────────────────────────

        [MenuItem("Axiom/AgentBridge/Smart Search — All Cameras (Scene)")]
        public static void MenuSearchCameras()
        {
            string path = Search(SearchDomain.Scene, componentType: "Camera");
            Debug.Log($"[AgentBridge] Smart Search report: {path}");
        }

        [MenuItem("Axiom/AgentBridge/Smart Search — All Scripts (Assets)")]
        public static void MenuSearchScripts()
        {
            string path = Search(SearchDomain.Assets, assetType: "MonoScript", assetPath: "Assets/Axiom");
            Debug.Log($"[AgentBridge] Smart Search report: {path}");
        }

        [MenuItem("Axiom/AgentBridge/Smart Search — All Prefabs (Assets)")]
        public static void MenuSearchPrefabs()
        {
            string path = Search(SearchDomain.Assets, assetType: "Prefab");
            Debug.Log($"[AgentBridge] Smart Search report: {path}");
        }
    }
}
