using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEditor;
using UnityEditor.Compilation;
using Axiom.Editor.AgentBridge.Core;

namespace Axiom.Editor.AgentBridge.Diagnostics
{
    /// <summary>
    /// Analyzes C# scripts in the project for patterns, dependencies, assembly structure, and issues.
    /// </summary>
    public static class ScriptAnalyzer
    {
        public enum ScriptAnalyzerMode
        {
            ClassMap,              // Mode A
            DependencyGraph,       // Mode B
            AssemblyDefinitions,   // Mode C
            AttributeScan,         // Mode D
            ApiUsageAudit          // Mode E
        }

        private static readonly string[] DefaultAttributes = new[]
        {
            "ExecuteAlways", "ExecuteInEditMode",
            "RequireComponent",
            "DisallowMultipleComponent",
            "CreateAssetMenu",
            "AddComponentMenu",
            "SelectionBase",
            "Header", "Tooltip", "Space", "Range",
            "SerializeField", "SerializeReference",
            "HideInInspector",
            "ContextMenu", "ContextMenuItemAttribute",
            "MenuItem",
            "InitializeOnLoad", "InitializeOnLoadMethod",
            "CustomEditor",
            "CanEditMultipleObjects",
            "Serializable"
        };

        /// <summary>
        /// Analyzes scripts in the project.
        /// </summary>
        /// <param name="mode">Analysis type.</param>
        /// <param name="assetPath">Folder to scope analysis. Null = "Assets".</param>
        /// <param name="attributeFilter">For Mode D: specific attribute name to search for. Null = scan all known attributes.</param>
        /// <returns>File path of the generated report.</returns>
        public static string GenerateReport(
            ScriptAnalyzerMode mode,
            string assetPath = null,
            string attributeFilter = null)
        {
            var sb = new StringBuilder();
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            string searchPath = assetPath ?? "Assets";

            switch (mode)
            {
                case ScriptAnalyzerMode.ClassMap:
                    BuildClassMap(sb, searchPath, timestamp);
                    break;
                case ScriptAnalyzerMode.DependencyGraph:
                    BuildDependencyGraph(sb, searchPath, timestamp);
                    break;
                case ScriptAnalyzerMode.AssemblyDefinitions:
                    BuildAssemblyDefinitions(sb, searchPath, timestamp);
                    break;
                case ScriptAnalyzerMode.AttributeScan:
                    BuildAttributeScan(sb, searchPath, attributeFilter, timestamp);
                    break;
                case ScriptAnalyzerMode.ApiUsageAudit:
                    BuildApiUsageAudit(sb, searchPath, timestamp);
                    break;
            }

            string reportName = $"script_analyzer_{mode.ToString().ToLower()}_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.md";
            return OutputWriter.WriteReport(reportName, sb.ToString());
        }

        // ─── Mode A: Class Map ────────────────────────────────────────────────────

        private static void BuildClassMap(StringBuilder sb, string searchPath, string timestamp)
        {
            sb.AppendLine($"# Script Analyzer — Mode: Class Map | Path: {searchPath}");
            sb.AppendLine();

            var monoBehaviours = new List<(string className, string ns, string baseClass, string filePath)>();
            var scriptableObjects = new List<(string className, string ns, string baseClass, string filePath)>();
            var editorScripts = new List<(string className, string ns, string baseClass, string filePath)>();
            var otherScripts = new List<(string className, string ns, string baseClass, string filePath)>();

            string[] allScriptGuids = AssetDatabase.FindAssets("t:MonoScript", new[] { searchPath });

            foreach (string guid in allScriptGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var script = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
                if (script == null) continue;

                System.Type scriptClass = script.GetClass();
                if (scriptClass == null) continue;

                bool isMB = typeof(MonoBehaviour).IsAssignableFrom(scriptClass) && scriptClass != typeof(MonoBehaviour);
                bool isSO = typeof(ScriptableObject).IsAssignableFrom(scriptClass) && scriptClass != typeof(ScriptableObject);
                bool isEditor = typeof(UnityEditor.Editor).IsAssignableFrom(scriptClass) && scriptClass != typeof(UnityEditor.Editor);
                bool isEditorWindow = typeof(EditorWindow).IsAssignableFrom(scriptClass) && scriptClass != typeof(EditorWindow);

                string ns = string.IsNullOrEmpty(scriptClass.Namespace) ? "(none)" : scriptClass.Namespace;
                string baseClass = scriptClass.BaseType?.Name ?? "(none)";

                var entry = (scriptClass.Name, ns, baseClass, path);

                if (isMB)
                    monoBehaviours.Add(entry);
                else if (isSO)
                    scriptableObjects.Add(entry);
                else if (isEditor || isEditorWindow)
                    editorScripts.Add(entry);
                else
                    otherScripts.Add(entry);
            }

            // MonoBehaviours
            sb.AppendLine("## MonoBehaviours");
            sb.AppendLine("| Class Name | Namespace | Base Class | File Path |");
            sb.AppendLine("| :--- | :--- | :--- | :--- |");
            foreach (var (n, ns, bc, p) in monoBehaviours)
                sb.AppendLine($"| {n} | {ns} | {bc} | {p} |");
            sb.AppendLine();

            // ScriptableObjects
            sb.AppendLine("## ScriptableObjects");
            sb.AppendLine("| Class Name | Namespace | Base Class | File Path |");
            sb.AppendLine("| :--- | :--- | :--- | :--- |");
            foreach (var (n, ns, bc, p) in scriptableObjects)
                sb.AppendLine($"| {n} | {ns} | {bc} | {p} |");
            sb.AppendLine();

            // Editor Scripts
            sb.AppendLine("## Editor Scripts");
            sb.AppendLine("| Class Name | Namespace | Base Class | File Path |");
            sb.AppendLine("| :--- | :--- | :--- | :--- |");
            foreach (var (n, ns, bc, p) in editorScripts)
                sb.AppendLine($"| {n} | {ns} | {bc} | {p} |");
            sb.AppendLine();

            // Other
            sb.AppendLine("## Other");
            sb.AppendLine("| Class Name | Namespace | Base Class | File Path |");
            sb.AppendLine("| :--- | :--- | :--- | :--- |");
            foreach (var (n, ns, bc, p) in otherScripts)
                sb.AppendLine($"| {n} | {ns} | {bc} | {p} |");
            sb.AppendLine();

            sb.AppendLine("---");
            sb.AppendLine($"MonoBehaviours: {monoBehaviours.Count} | ScriptableObjects: {scriptableObjects.Count} | Editor: {editorScripts.Count} | Other: {otherScripts.Count} | Generated: {timestamp}");
        }

        // ─── Mode B: Dependency Graph ─────────────────────────────────────────────

        private static void BuildDependencyGraph(StringBuilder sb, string searchPath, string timestamp)
        {
            sb.AppendLine($"# Script Analyzer — Mode: Dependency Graph | Path: {searchPath}");
            sb.AppendLine();

            string[] allScriptGuids = AssetDatabase.FindAssets("t:MonoScript", new[] { searchPath });

            // Collect all user-type names in scope
            var userTypeNames = new HashSet<string>();
            var scriptTypes = new Dictionary<string, System.Type>();

            foreach (string guid in allScriptGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var script = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
                if (script == null) continue;

                System.Type t = script.GetClass();
                if (t == null) continue;

                userTypeNames.Add(t.Name);
                scriptTypes[path] = t;
            }

            // For each script, find its user-type dependencies via reflection
            var dependencies = new Dictionary<string, List<string>>();

            foreach (var kvp in scriptTypes)
            {
                string path = kvp.Key;
                System.Type t = kvp.Value;
                string className = t.Name;

                var deps = new HashSet<string>();

                // Check fields
                var fields = t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
                foreach (var field in fields)
                {
                    string typeName = GetBaseTypeName(field.FieldType);
                    if (userTypeNames.Contains(typeName) && typeName != className)
                        deps.Add(typeName);
                }

                // Check properties
                var properties = t.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                foreach (var prop in properties)
                {
                    string typeName = GetBaseTypeName(prop.PropertyType);
                    if (userTypeNames.Contains(typeName) && typeName != className)
                        deps.Add(typeName);
                }

                // Check method parameters and return types
                var methods = t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly);
                foreach (var method in methods)
                {
                    string retTypeName = GetBaseTypeName(method.ReturnType);
                    if (userTypeNames.Contains(retTypeName) && retTypeName != className)
                        deps.Add(retTypeName);

                    foreach (var param in method.GetParameters())
                    {
                        string paramTypeName = GetBaseTypeName(param.ParameterType);
                        if (userTypeNames.Contains(paramTypeName) && paramTypeName != className)
                            deps.Add(paramTypeName);
                    }
                }

                dependencies[className] = deps.OrderBy(d => d).ToList();
            }

            // Count how many times each type is referenced
            var refCounts = new Dictionary<string, int>();
            foreach (var dep in dependencies.Values)
                foreach (var d in dep)
                    refCounts[d] = refCounts.GetValueOrDefault(d) + 1;

            // Output dependency table
            sb.AppendLine("## Script Dependencies (what each script uses)");
            sb.AppendLine("| Script | Depends On |");
            sb.AppendLine("| :--- | :--- |");
            foreach (var kvp in dependencies.OrderBy(k => k.Key))
            {
                string deps = kvp.Value.Count > 0 ? string.Join(", ", kvp.Value) : "(none)";
                sb.AppendLine($"| {kvp.Key} | {deps} |");
            }
            sb.AppendLine();

            // Hotspots
            var hotspots = refCounts.Where(r => r.Value > 0).OrderByDescending(r => r.Value).ToList();
            if (hotspots.Count > 0)
            {
                sb.AppendLine("## Most Referenced Scripts (dependency hotspots)");
                sb.AppendLine("| Script | Referenced By Count |");
                sb.AppendLine("| :--- | :--- |");
                foreach (var kvp in hotspots)
                    sb.AppendLine($"| {kvp.Key} | {kvp.Value} |");
                sb.AppendLine();
            }

            sb.AppendLine("---");
            sb.AppendLine($"Scripts analyzed: {dependencies.Count} | Generated: {timestamp}");
        }

        private static string GetBaseTypeName(System.Type t)
        {
            if (t == null) return string.Empty;
            // Unwrap arrays and generics to get the element type
            if (t.IsArray) return GetBaseTypeName(t.GetElementType());
            if (t.IsGenericType)
            {
                var args = t.GetGenericArguments();
                if (args.Length > 0) return GetBaseTypeName(args[0]);
            }
            return t.Name;
        }

        // ─── Mode C: Assembly Definitions ────────────────────────────────────────

        private static void BuildAssemblyDefinitions(StringBuilder sb, string searchPath, string timestamp)
        {
            sb.AppendLine($"# Script Analyzer — Mode: Assembly Definitions | Path: {searchPath}");
            sb.AppendLine();

            // Get compiled assemblies with source file counts
            var compiledAssemblies = CompilationPipeline.GetAssemblies(AssembliesType.PlayerWithoutTestAssemblies);
            var asmSourceCounts = new Dictionary<string, int>();
            foreach (UnityEditor.Compilation.Assembly asm in compiledAssemblies)
                asmSourceCounts[asm.name] = asm.sourceFiles?.Length ?? 0;

            // Find .asmdef files
            string[] asmdefGuids = AssetDatabase.FindAssets("t:AssemblyDefinitionAsset");

            sb.AppendLine("## Assembly Definitions");
            sb.AppendLine("| Name | Root Namespace | References | Platforms | Source Files | Defines |");
            sb.AppendLine("| :--- | :--- | :--- | :--- | :--- | :--- |");

            foreach (string guid in asmdefGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string fullPath = Path.Combine(Path.GetDirectoryName(Application.dataPath), path);
                if (!File.Exists(fullPath)) continue;

                string json = File.ReadAllText(fullPath);

                string name = ExtractJsonString(json, "name") ?? Path.GetFileNameWithoutExtension(path);
                string rootNs = ExtractJsonString(json, "rootNamespace") ?? "(none)";
                if (string.IsNullOrEmpty(rootNs)) rootNs = "(none)";

                var references = ExtractJsonStringArray(json, "references");
                string refsStr = references.Count > 0 ? string.Join(", ", references) : "(none)";

                var includePlatforms = ExtractJsonStringArray(json, "includePlatforms");
                var excludePlatforms = ExtractJsonStringArray(json, "excludePlatforms");
                string platformsStr = "(all)";
                if (includePlatforms.Count > 0)
                    platformsStr = string.Join(", ", includePlatforms);
                else if (excludePlatforms.Count > 0)
                    platformsStr = $"Exclude: {string.Join(", ", excludePlatforms)}";

                var defines = ExtractJsonStringArray(json, "defineConstraints");
                string definesStr = defines.Count > 0 ? string.Join(", ", defines) : "(none)";

                int sourceCount = asmSourceCounts.TryGetValue(name, out int c) ? c : 0;

                sb.AppendLine($"| {name} | {rootNs} | {refsStr} | {platformsStr} | {sourceCount} | {definesStr} |");
            }
            sb.AppendLine();

            // Also show assemblies from compilation pipeline not in asmdef (like Assembly-CSharp)
            sb.AppendLine("## All Compiled Assemblies (from Compilation Pipeline)");
            sb.AppendLine("| Assembly Name | Source Files | References |");
            sb.AppendLine("| :--- | :--- | :--- |");

            var allAsms = CompilationPipeline.GetAssemblies();
            foreach (UnityEditor.Compilation.Assembly asm in allAsms.OrderBy(a => a.name))
            {
                var asmRefs = asm.assemblyReferences?.Select(r => r.name).ToArray() ?? new string[0];
                string refs = asmRefs.Length > 0 ? string.Join(", ", asmRefs.Take(5)) + (asmRefs.Length > 5 ? $" +{asmRefs.Length - 5} more" : "") : "(none)";
                sb.AppendLine($"| {asm.name} | {asm.sourceFiles?.Length ?? 0} | {refs} |");
            }
            sb.AppendLine();

            sb.AppendLine("---");
            sb.AppendLine($"Assembly Definitions: {asmdefGuids.Length} | Total Compiled Assemblies: {allAsms.Length} | Generated: {timestamp}");
        }

        private static string ExtractJsonString(string json, string key)
        {
            var match = Regex.Match(json, $@"""{key}""\s*:\s*""([^""]*)""");
            return match.Success ? match.Groups[1].Value : null;
        }

        private static List<string> ExtractJsonStringArray(string json, string key)
        {
            var result = new List<string>();
            var match = Regex.Match(json, $@"""{key}""\s*:\s*\[([^\]]*)\]", RegexOptions.Singleline);
            if (!match.Success) return result;

            string arrayContent = match.Groups[1].Value;
            var items = Regex.Matches(arrayContent, @"""([^""]*)""");
            foreach (Match item in items)
                result.Add(item.Groups[1].Value);
            return result;
        }

        // ─── Mode D: Attribute Scan ───────────────────────────────────────────────

        private static void BuildAttributeScan(StringBuilder sb, string searchPath, string attributeFilter, string timestamp)
        {
            string filterDisplay = attributeFilter ?? "ALL";
            sb.AppendLine($"# Script Analyzer — Mode: Attribute Scan | Filter: {filterDisplay}");
            sb.AppendLine();

            string[] targetAttributes = attributeFilter != null
                ? new[] { attributeFilter }
                : DefaultAttributes;

            string[] allScriptGuids = AssetDatabase.FindAssets("t:MonoScript", new[] { searchPath });

            // group: attribute name → list of (className, methodName or null, extraInfo, filePath)
            var results = new Dictionary<string, List<(string className, string memberName, string extraInfo, string filePath)>>();
            foreach (var attr in targetAttributes)
                results[attr] = new List<(string, string, string, string)>();

            int totalMatches = 0;

            foreach (string guid in allScriptGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var script = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
                if (script == null) continue;

                System.Type t = script.GetClass();
                if (t == null) continue;

                // Type-level attributes
                foreach (var attr in t.GetCustomAttributes(true))
                {
                    string attrName = attr.GetType().Name.Replace("Attribute", "");
                    string attrFullName = attr.GetType().Name;

                    foreach (var target in targetAttributes)
                    {
                        if (attrName == target || attrFullName == target || attrFullName == target + "Attribute")
                        {
                            string extra = GetAttributeExtra(attr);
                            results[target].Add((t.Name, null, extra, path));
                            totalMatches++;
                        }
                    }
                }

                // Method-level attributes
                try
                {
                    var methods = t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                    foreach (var method in methods)
                    {
                        foreach (var attr in method.GetCustomAttributes(true))
                        {
                            string attrName = attr.GetType().Name.Replace("Attribute", "");
                            string attrFullName = attr.GetType().Name;

                            foreach (var target in targetAttributes)
                            {
                                if (attrName == target || attrFullName == target || attrFullName == target + "Attribute")
                                {
                                    string extra = GetAttributeExtra(attr);
                                    results[target].Add((t.Name, method.Name, extra, path));
                                    totalMatches++;
                                }
                            }
                        }
                    }
                }
                catch { /* Skip types with inaccessible methods */ }
            }

            // Output grouped by attribute
            foreach (var target in targetAttributes)
            {
                var entries = results[target];
                if (entries.Count == 0) continue;

                sb.AppendLine($"## {target}");

                // Determine if any have member names
                bool hasMember = entries.Any(e => e.memberName != null);
                if (hasMember)
                {
                    sb.AppendLine("| Class | Member | Extra Info | File Path |");
                    sb.AppendLine("| :--- | :--- | :--- | :--- |");
                    foreach (var (cn, mn, extra, fp) in entries)
                        sb.AppendLine($"| {cn} | {mn ?? "(type)"} | {extra} | {fp} |");
                }
                else
                {
                    sb.AppendLine("| Class | Extra Info | File Path |");
                    sb.AppendLine("| :--- | :--- | :--- |");
                    foreach (var (cn, _, extra, fp) in entries)
                        sb.AppendLine($"| {cn} | {extra} | {fp} |");
                }
                sb.AppendLine();
            }

            sb.AppendLine("---");
            sb.AppendLine($"Attributes scanned: {targetAttributes.Length} | Matches: {totalMatches} | Generated: {timestamp}");
        }

        private static string GetAttributeExtra(object attr)
        {
            // Try to extract meaningful info from common attribute types
            try
            {
                var t = attr.GetType();
                string typeName = t.Name.Replace("Attribute", "");

                switch (typeName)
                {
                    case "RequireComponent":
                        var r1 = t.GetProperty("m_Type0")?.GetValue(attr) as System.Type;
                        var r2 = t.GetProperty("m_Type1")?.GetValue(attr) as System.Type;
                        var r3 = t.GetProperty("m_Type2")?.GetValue(attr) as System.Type;
                        var required = new List<string>();
                        if (r1 != null) required.Add(r1.Name);
                        if (r2 != null) required.Add(r2.Name);
                        if (r3 != null) required.Add(r3.Name);
                        return required.Count > 0 ? string.Join(", ", required) : "";
                    case "CreateAssetMenu":
                        string menuName = t.GetProperty("menuName")?.GetValue(attr)?.ToString() ?? "";
                        string fileName = t.GetProperty("fileName")?.GetValue(attr)?.ToString() ?? "";
                        return $"menuName={menuName}, fileName={fileName}";
                    case "MenuItem":
                        string itemPath = t.GetProperty("menuItem")?.GetValue(attr)?.ToString() ?? "";
                        return itemPath;
                    case "CustomEditor":
                        var inspectedType = t.GetProperty("m_InspectedType")?.GetValue(attr) as System.Type;
                        return inspectedType != null ? inspectedType.Name : "";
                    case "Header":
                        return t.GetProperty("header")?.GetValue(attr)?.ToString() ?? "";
                    case "Tooltip":
                        return t.GetProperty("tooltip")?.GetValue(attr)?.ToString() ?? "";
                    case "Range":
                        float min = (float)(t.GetField("min")?.GetValue(attr) ?? 0f);
                        float max = (float)(t.GetField("max")?.GetValue(attr) ?? 0f);
                        return $"[{min}, {max}]";
                    case "AddComponentMenu":
                        return t.GetProperty("componentMenu")?.GetValue(attr)?.ToString() ?? "";
                    default:
                        return "";
                }
            }
            catch
            {
                return "";
            }
        }

        // ─── Mode E: API Usage Audit ──────────────────────────────────────────────

        private static void BuildApiUsageAudit(StringBuilder sb, string searchPath, string timestamp)
        {
            sb.AppendLine($"# Script Analyzer — Mode: API Usage Audit | Path: {searchPath}");
            sb.AppendLine();

            var deprecatedPatterns = new Dictionary<string, string>
            {
                { "OnGUI()", "Legacy IMGUI — consider UI Toolkit" },
                { "WWW ", "Deprecated — use UnityWebRequest" },
                { "Application.loadedLevel", "Deprecated — use SceneManager" },
                { "Application.LoadLevel", "Deprecated — use SceneManager.LoadScene" },
                { "GUILayout.", "IMGUI — consider UI Toolkit for editor tools" },
                { "EditorGUILayout.", "IMGUI — consider UI Toolkit for editor tools" },
                { "PlayerPrefs", "Consider ScriptableObject or custom save system" },
                { "FindObjectOfType", "Performance concern — use FindFirstObjectByType or cached reference" },
                { "FindObjectsOfType", "Performance concern — use FindObjectsByType" },
                { "GameObject.Find(", "Performance concern — use cached references or tags" },
                { "SendMessage(", "Performance concern — use direct method calls or events" },
                { "BroadcastMessage(", "Performance concern — use events" },
                { "Invoke(\"", "String-based — consider coroutines or async" },
                { "InvokeRepeating(\"", "String-based — consider coroutines or async" },
                { "StartCoroutine(\"", "String-based overload — use StartCoroutine(IEnumerator)" }
            };

            string[] allScriptGuids = AssetDatabase.FindAssets("t:MonoScript", new[] { searchPath });

            var findings = new List<(int num, string filePath, int lineNum, string usage, string recommendation)>();
            int filesScanned = 0;

            foreach (string guid in allScriptGuids)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);

                // Skip AgentBridge scripts, Packages, and Library
                if (assetPath.Contains("AgentBridge")) continue;
                if (assetPath.StartsWith("Packages/")) continue;
                if (assetPath.StartsWith("Library/")) continue;

                string fullPath = Path.Combine(Path.GetDirectoryName(Application.dataPath), assetPath);
                if (!File.Exists(fullPath)) continue;

                string[] lines;
                try { lines = File.ReadAllLines(fullPath); }
                catch { continue; }

                filesScanned++;

                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];
                    // Skip comment lines
                    string trimmed = line.TrimStart();
                    if (trimmed.StartsWith("//") || trimmed.StartsWith("*") || trimmed.StartsWith("/*")) continue;

                    foreach (var kvp in deprecatedPatterns)
                    {
                        if (line.Contains(kvp.Key))
                        {
                            findings.Add((findings.Count + 1, assetPath, i + 1, kvp.Key.Trim(), kvp.Value));
                        }
                    }
                }
            }

            sb.AppendLine("## Deprecated / Flagged API Usage");
            if (findings.Count > 0)
            {
                sb.AppendLine("| # | File Path | Line | Usage | Recommendation |");
                sb.AppendLine("| :--- | :--- | :--- | :--- | :--- |");
                foreach (var (num, fp, ln, usage, rec) in findings)
                    sb.AppendLine($"| {num} | {fp} | {ln} | {usage} | {rec} |");
            }
            else
            {
                sb.AppendLine("*No flagged API usages found.*");
            }
            sb.AppendLine();

            sb.AppendLine("## Summary");
            sb.AppendLine($"- Total flagged usages: {findings.Count}");
            sb.AppendLine($"- Files scanned: {filesScanned}");
            sb.AppendLine($"- Patterns checked: {deprecatedPatterns.Count}");
            sb.AppendLine("- **Note:** Not all flagged usages require changes. Some patterns (e.g., EditorGUILayout in Editor scripts) may be acceptable.");
            sb.AppendLine();

            sb.AppendLine("---");
            sb.AppendLine($"Generated: {timestamp}");
        }

        // ─── Menu Items ───────────────────────────────────────────────────────────

        [MenuItem("Axiom/AgentBridge/Script Analyzer — Mode A (Class Map)")]
        public static void MenuModeA()
        {
            string path = GenerateReport(ScriptAnalyzerMode.ClassMap);
            Debug.Log($"[AgentBridge] Script Analyzer report: {path}");
        }

        [MenuItem("Axiom/AgentBridge/Script Analyzer — Mode B (Dependency Graph)")]
        public static void MenuModeB()
        {
            string path = GenerateReport(ScriptAnalyzerMode.DependencyGraph);
            Debug.Log($"[AgentBridge] Script Analyzer report: {path}");
        }

        [MenuItem("Axiom/AgentBridge/Script Analyzer — Mode C (Assembly Definitions)")]
        public static void MenuModeC()
        {
            string path = GenerateReport(ScriptAnalyzerMode.AssemblyDefinitions);
            Debug.Log($"[AgentBridge] Script Analyzer report: {path}");
        }

        [MenuItem("Axiom/AgentBridge/Script Analyzer — Mode D (Attribute Scan)")]
        public static void MenuModeD()
        {
            string path = GenerateReport(ScriptAnalyzerMode.AttributeScan);
            Debug.Log($"[AgentBridge] Script Analyzer report: {path}");
        }

        [MenuItem("Axiom/AgentBridge/Script Analyzer — Mode E (API Usage Audit)")]
        public static void MenuModeE()
        {
            string path = GenerateReport(ScriptAnalyzerMode.ApiUsageAudit);
            Debug.Log($"[AgentBridge] Script Analyzer report: {path}");
        }
    }
}
