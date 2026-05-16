using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Axiom.Editor.AgentBridge.Core
{
    /// <summary>
    /// Shared utility for resolving component type names to System.Type.
    /// Caches results per compilation cycle.
    /// </summary>
    public static class TypeResolver
    {
        private static Dictionary<string, System.Type> _cache = new Dictionary<string, System.Type>();

        [InitializeOnLoadMethod]
        private static void ClearCache()
        {
            _cache = new Dictionary<string, System.Type>();
        }

        /// <summary>
        /// Resolves a component type name to its System.Type.
        /// Uses TypeCache for fast lookup. Returns null if not found.
        /// </summary>
        public static System.Type ResolveComponentType(string typeName)
        {
            if (string.IsNullOrEmpty(typeName)) return null;

            if (_cache.TryGetValue(typeName, out var cached)) return cached;

            foreach (var type in TypeCache.GetTypesDerivedFrom<Component>())
            {
                if (type.Name == typeName)
                {
                    _cache[typeName] = type;
                    return type;
                }
            }

            _cache[typeName] = null;
            return null;
        }
    }
}
