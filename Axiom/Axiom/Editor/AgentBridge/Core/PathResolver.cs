using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Axiom.Editor.AgentBridge.Core
{
    /// <summary>
    /// Shared utility for resolving GameObject hierarchy paths and collecting scene roots.
    /// Eliminates duplicated root-path resolution logic across HierarchyLens and SceneDiff.
    /// </summary>
    public static class PathResolver
    {
        /// <summary>
        /// Collects all root GameObjects across all loaded scenes.
        /// </summary>
        /// <returns>List of root GameObjects from all loaded scenes.</returns>
        public static List<GameObject> GetRootGameObjects()
        {
            var result = new List<GameObject>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;
                result.AddRange(scene.GetRootGameObjects());
            }
            return result;
        }

        /// <summary>
        /// Resolves a breadcrumb path to a specific Transform in the loaded scenes.
        /// </summary>
        /// <param name="rootPath">Breadcrumb path (e.g., "Managers/AudioManager"). Can be null or empty.</param>
        /// <param name="resolvedObjects">Output: list of GameObjects to report on. If rootPath is null/empty,
        /// this contains all root GameObjects. If rootPath is valid, this contains the single resolved object.</param>
        /// <returns>True if resolution succeeded, false if the path could not be found.</returns>
        public static bool ResolveRootPath(string rootPath, out List<GameObject> resolvedObjects)
        {
            if (string.IsNullOrEmpty(rootPath))
            {
                resolvedObjects = GetRootGameObjects();
                return true;
            }

            string[] parts = rootPath.Split('/');

            // Find the root matching the first segment
            GameObject current = null;
            foreach (GameObject root in GetRootGameObjects())
            {
                if (root.name == parts[0])
                {
                    current = root;
                    break;
                }
            }

            if (current == null)
            {
                resolvedObjects = new List<GameObject>();
                return false;
            }

            // Walk each subsequent segment
            for (int i = 1; i < parts.Length; i++)
            {
                Transform child = current.transform.Find(parts[i]);
                if (child == null)
                {
                    resolvedObjects = new List<GameObject>();
                    return false;
                }
                current = child.gameObject;
            }

            resolvedObjects = new List<GameObject> { current };
            return true;
        }

        /// <summary>
        /// Builds the full hierarchy path for a Transform (e.g., "Managers/AudioManager/MusicSource").
        /// </summary>
        /// <param name="transform">The target transform.</param>
        /// <returns>Full path from the root, segments separated by "/".</returns>
        public static string GetHierarchyPath(Transform transform)
        {
            var parts = new List<string>();
            Transform current = transform;
            while (current != null)
            {
                parts.Add(current.name);
                current = current.parent;
            }
            parts.Reverse();
            return string.Join("/", parts);
        }

        /// <summary>
        /// Returns comma-separated names of all loaded scenes.
        /// </summary>
        public static string GetLoadedSceneNames()
        {
            var names = new List<string>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (scene.isLoaded)
                    names.Add(scene.name);
            }
            return names.Count > 0 ? string.Join(", ", names) : "None";
        }
    }
}
