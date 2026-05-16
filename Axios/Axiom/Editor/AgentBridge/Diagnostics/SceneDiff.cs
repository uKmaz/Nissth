using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using UnityEditor;
using Axiom.Editor.AgentBridge.Core;
using static Axiom.Editor.AgentBridge.Core.SerializedPropertyHelper;

namespace Axiom.Editor.AgentBridge.Diagnostics
{
    // ─────────────────────────────────────────────────────
    //  Data Structures
    //  NOTE: JsonUtility is intentionally NOT used for snapshot serialization
    //  because it silently drops List<SomeClass> fields when the class is in a
    //  separate asmdef. We use a manual JSON builder instead — the schema is
    //  simple enough that this is safer and more transparent.
    // ─────────────────────────────────────────────────────

    internal class SceneDiffPropertyEntry
    {
        public string path;   // e.g., "m_Speed"
        public string type;   // e.g., "float"
        public string value;  // e.g., "5.5000"
    }

    internal class SceneDiffComponentEntry
    {
        public string typeName;
        public List<SceneDiffPropertyEntry> properties;
    }

    internal class SceneDiffGOEntry
    {
        public string path;
        public string name;
        public string tag;
        public int layer;
        public bool activeSelf;
        public bool activeInHierarchy;
        public List<string> components;
        public List<SceneDiffComponentEntry> componentDetails; // null for Modes A-C, populated for Mode D
    }

    internal class SceneDiffSnapshot
    {
        public string label;
        public string timestamp;
        public string rootScope;
        public string sceneNames;
        public string hash;
        public int totalGameObjects;
        public List<SceneDiffGOEntry> gameObjects;
    }

    // ─────────────────────────────────────────────────────
    //  Main Tool Class
    // ─────────────────────────────────────────────────────

    /// <summary>
    /// Prevents "brain desync" by creating deterministic snapshots of scene state and comparing them.
    /// Before any multi-step operation: snapshot. After: compare. Know exactly what changed.
    /// </summary>
    public static class SceneDiff
    {
        // ─────────────────────────────────────────────────────
        //  Public Enums
        // ─────────────────────────────────────────────────────

        /// <summary>Detail level for scene comparisons.</summary>
        public enum SceneDiffMode
        {
            HashOnly,        // Mode A: single SHA-256 hash of scene state
            ObjectCountDiff, // Mode B: counts of GameObjects and components — before vs after
            StructuralDiff,  // Mode C: hierarchy tree diff — added, removed, moved objects
            PropertyDiff     // Mode D: property value diff — which values changed
        }

        /// <summary>Which operation to perform.</summary>
        public enum SceneDiffOperation
        {
            Snapshot,        // Save current state with a label
            Compare,         // Compare two labeled snapshots
            CompareCurrent,  // Compare a saved snapshot against the live scene
            List,            // List all saved snapshots
            Clear            // Remove all snapshots
        }

        // ─────────────────────────────────────────────────────
        //  Public Entry Point
        // ─────────────────────────────────────────────────────

        /// <summary>Executes a SceneDiff operation.</summary>
        public static string Execute(
            SceneDiffOperation operation,
            SceneDiffMode mode = SceneDiffMode.HashOnly,
            string label = null,
            string labelA = null,
            string labelB = null,
            string rootPath = null)
        {
            switch (operation)
            {
                case SceneDiffOperation.Snapshot:
                    {
                        if (string.IsNullOrEmpty(label))
                        {
                            Debug.LogError("[AgentBridge] SceneDiff: Snapshot requires a non-null, non-empty label.");
                            return null;
                        }
                        bool captureProps = mode == SceneDiffMode.PropertyDiff;
                        SceneDiffSnapshot snap = CaptureSnapshot(label, rootPath, captureProps);
                        string json = SnapshotToJson(snap);
                        return OutputWriter.WriteSnapshot(label, json);
                    }

                case SceneDiffOperation.Compare:
                    {
                        if (string.IsNullOrEmpty(labelA) || string.IsNullOrEmpty(labelB))
                        {
                            Debug.LogError("[AgentBridge] SceneDiff: Compare requires both labelA and labelB.");
                            return null;
                        }
                        SceneDiffSnapshot snapA = LoadSnapshot(labelA);
                        SceneDiffSnapshot snapB = LoadSnapshot(labelB);
                        if (snapA == null || snapB == null) return null;

                        string content = RunComparison(mode, snapA, snapB);
                        return OutputWriter.WriteReport("scene_diff", content);
                    }

                case SceneDiffOperation.CompareCurrent:
                    {
                        if (string.IsNullOrEmpty(label))
                        {
                            Debug.LogError("[AgentBridge] SceneDiff: CompareCurrent requires a non-null, non-empty label.");
                            return null;
                        }
                        SceneDiffSnapshot saved = LoadSnapshot(label);
                        if (saved == null) return null;

                        bool captureProps = mode == SceneDiffMode.PropertyDiff;
                        SceneDiffSnapshot live = CaptureSnapshot("live", rootPath, captureProps);
                        string content = RunComparison(mode, saved, live);
                        return OutputWriter.WriteReport("scene_diff", content);
                    }

                case SceneDiffOperation.List:
                    {
                        string listing = OutputWriter.ListSnapshots();
                        string reportContent = "# Scene Diff — Snapshot List\n\n" + listing +
                                              "\n\n---\nGenerated: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                        return OutputWriter.WriteReport("scene_diff_list", reportContent);
                    }

                case SceneDiffOperation.Clear:
                    {
                        int count = OutputWriter.ClearSnapshots();
                        return $"Cleared {count} snapshot(s).";
                    }

                default:
                    Debug.LogError($"[AgentBridge] SceneDiff: Unknown operation {operation}.");
                    return null;
            }
        }

        // ─────────────────────────────────────────────────────
        //  Editor Menu Items
        // ─────────────────────────────────────────────────────

        [MenuItem("Axiom/AgentBridge/Scene Diff — Save Snapshot")]
        public static void MenuSaveSnapshot()
        {
            string label = $"manual_{DateTime.Now:yyyyMMdd_HHmmss}";
            string path = Execute(SceneDiffOperation.Snapshot, label: label);
            Debug.Log($"[AgentBridge] Snapshot saved: {path}");
        }

        [MenuItem("Axiom/AgentBridge/Scene Diff — Compare Current vs Last Snapshot")]
        public static void MenuCompareCurrentVsLast()
        {
            string listing = OutputWriter.ListSnapshots();
            if (listing == "No snapshots found.")
            {
                Debug.LogWarning("[AgentBridge] No snapshots found. Save a snapshot first.");
                return;
            }
            string firstLine = listing.Split('\n')[0];
            string label = firstLine.Split(new[] { " | " }, StringSplitOptions.None)[0].Trim();

            string path = Execute(SceneDiffOperation.CompareCurrent, mode: SceneDiffMode.StructuralDiff, label: label);
            Debug.Log($"[AgentBridge] Diff report: {path}");
        }

        [MenuItem("Axiom/AgentBridge/Scene Diff — List Snapshots")]
        public static void MenuListSnapshots()
        {
            string path = Execute(SceneDiffOperation.List);
            Debug.Log($"[AgentBridge] Snapshot list: {path}");
        }

        [MenuItem("Axiom/AgentBridge/Scene Diff — Clear All Snapshots")]
        public static void MenuClearSnapshots()
        {
            string result = Execute(SceneDiffOperation.Clear);
            Debug.Log($"[AgentBridge] {result}");
        }

        [MenuItem("Axiom/AgentBridge/Scene Diff — Save Snapshot (with Property Values)")]
        public static void MenuSavePropertySnapshot()
        {
            string label = $"props_{DateTime.Now:yyyyMMdd_HHmmss}";
            string path = Execute(SceneDiffOperation.Snapshot, mode: SceneDiffMode.PropertyDiff, label: label);
            Debug.Log($"[AgentBridge] Property snapshot saved: {path}");
        }

        // ─────────────────────────────────────────────────────
        //  Internal: CaptureSnapshot
        // ─────────────────────────────────────────────────────

        private static SceneDiffSnapshot CaptureSnapshot(string label, string rootPath, bool capturePropertyValues = false)
        {
            var entries = new List<SceneDiffGOEntry>();

            if (!PathResolver.ResolveRootPath(rootPath, out var roots))
            {
                Debug.LogWarning($"[AgentBridge] SceneDiff: Could not resolve rootPath \"{rootPath}\". Snapshot will be empty.");
            }
            else
            {
                foreach (GameObject go in roots)
                    WalkTransform(go.transform, entries, capturePropertyValues);
            }

            // Sort deterministically by path
            entries.Sort((a, b) => string.Compare(a.path, b.path, StringComparison.Ordinal));

            // Hash the entries deterministically
            string hash = ComputeEntriesHash(entries);

            return new SceneDiffSnapshot
            {
                label = label,
                timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                rootScope = rootPath ?? "ALL",
                sceneNames = PathResolver.GetLoadedSceneNames(),
                hash = hash,
                totalGameObjects = entries.Count,
                gameObjects = entries
            };
        }

        private static void WalkTransform(Transform t, List<SceneDiffGOEntry> entries, bool capturePropertyValues = false)
        {
            string path = PathResolver.GetHierarchyPath(t);

            var compNames = new List<string>();
            List<SceneDiffComponentEntry> compDetails = capturePropertyValues ? new List<SceneDiffComponentEntry>() : null;

            foreach (Component c in t.gameObject.GetComponents<Component>())
            {
                if (c == null)
                {
                    compNames.Add("MISSING_SCRIPT");
                    continue;
                }
                if (c.GetType() == typeof(Transform)) continue;

                compNames.Add(c.GetType().Name);

                if (capturePropertyValues)
                {
                    var compEntry = new SceneDiffComponentEntry
                    {
                        typeName = c.GetType().Name,
                        properties = new List<SceneDiffPropertyEntry>()
                    };
                    var so = new SerializedObject(c);
                    var iter = so.GetIterator();
                    bool enter = true;
                    while (iter.NextVisible(enter))
                    {
                        enter = false;
                        compEntry.properties.Add(new SceneDiffPropertyEntry
                        {
                            path = iter.propertyPath,
                            type = iter.type,
                            value = GetPropertyValue(iter.Copy())
                        });
                    }
                    compDetails.Add(compEntry);
                }
            }
            compNames.Sort();

            entries.Add(new SceneDiffGOEntry
            {
                path = path,
                name = t.gameObject.name,
                tag = t.gameObject.tag,
                layer = t.gameObject.layer,
                activeSelf = t.gameObject.activeSelf,
                activeInHierarchy = t.gameObject.activeInHierarchy,
                components = compNames,
                componentDetails = compDetails
            });

            for (int i = 0; i < t.childCount; i++)
                WalkTransform(t.GetChild(i), entries, capturePropertyValues);
        }

        // ─────────────────────────────────────────────────────
        //  Internal: JSON Serialization (manual — not JsonUtility)
        // ─────────────────────────────────────────────────────

        private static string SnapshotToJson(SceneDiffSnapshot snap)
        {
            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine($"  \"label\": {JsonStr(snap.label)},");
            sb.AppendLine($"  \"timestamp\": {JsonStr(snap.timestamp)},");
            sb.AppendLine($"  \"rootScope\": {JsonStr(snap.rootScope)},");
            sb.AppendLine($"  \"sceneNames\": {JsonStr(snap.sceneNames)},");
            sb.AppendLine($"  \"hash\": {JsonStr(snap.hash)},");
            sb.AppendLine($"  \"totalGameObjects\": {snap.totalGameObjects},");
            sb.Append("  \"gameObjects\": [");

            if (snap.gameObjects == null || snap.gameObjects.Count == 0)
            {
                sb.AppendLine("]");
            }
            else
            {
                sb.AppendLine();
                for (int i = 0; i < snap.gameObjects.Count; i++)
                {
                    var e = snap.gameObjects[i];
                    sb.AppendLine("    {");
                    sb.AppendLine($"      \"path\": {JsonStr(e.path)},");
                    sb.AppendLine($"      \"name\": {JsonStr(e.name)},");
                    sb.AppendLine($"      \"tag\": {JsonStr(e.tag)},");
                    sb.AppendLine($"      \"layer\": {e.layer},");
                    sb.AppendLine($"      \"activeSelf\": {(e.activeSelf ? "true" : "false")},");
                    sb.AppendLine($"      \"activeInHierarchy\": {(e.activeInHierarchy ? "true" : "false")},");
                    sb.Append("      \"components\": [");
                    if (e.components == null || e.components.Count == 0)
                    {
                        sb.AppendLine("]");
                    }
                    else
                    {
                        sb.Append(string.Join(", ", e.components.Select(c => JsonStr(c))));
                        sb.AppendLine("]");
                    }
                    // Serialize componentDetails if present
                    if (e.componentDetails != null)
                    {
                        sb.Append(",\n      \"componentDetails\": [");
                        if (e.componentDetails.Count == 0)
                        {
                            sb.AppendLine("]");
                        }
                        else
                        {
                            sb.AppendLine();
                            for (int ci = 0; ci < e.componentDetails.Count; ci++)
                            {
                                var cd = e.componentDetails[ci];
                                sb.AppendLine("        {");
                                sb.AppendLine($"          \"typeName\": {JsonStr(cd.typeName)},");
                                sb.Append("          \"properties\": [");
                                if (cd.properties == null || cd.properties.Count == 0)
                                {
                                    sb.AppendLine("]");
                                }
                                else
                                {
                                    sb.AppendLine();
                                    for (int pi = 0; pi < cd.properties.Count; pi++)
                                    {
                                        var p = cd.properties[pi];
                                        sb.Append($"            {{\"path\":{JsonStr(p.path)},\"type\":{JsonStr(p.type)},\"value\":{JsonStr(p.value)}}}");
                                        sb.AppendLine(pi < cd.properties.Count - 1 ? "," : "");
                                    }
                                    sb.AppendLine("          ]");
                                }
                                sb.Append(ci < e.componentDetails.Count - 1 ? "        }," : "        }");
                                sb.AppendLine();
                            }
                            sb.AppendLine("      ]");
                        }
                    }
                    sb.Append(i < snap.gameObjects.Count - 1 ? "    }," : "    }");
                    sb.AppendLine();
                }
                sb.AppendLine("  ]");
            }

            sb.Append("}");
            return sb.ToString();
        }

        private static SceneDiffSnapshot SnapshotFromJson(string json)
        {
            // Minimal JSON parser for the snapshot format we produce.
            // This is robust enough for our deterministic output format.
            if (string.IsNullOrEmpty(json)) return null;

            var snap = new SceneDiffSnapshot
            {
                gameObjects = new List<SceneDiffGOEntry>()
            };

            snap.label = JsonReadStringField(json, "label");
            snap.timestamp = JsonReadStringField(json, "timestamp");
            snap.rootScope = JsonReadStringField(json, "rootScope");
            snap.sceneNames = JsonReadStringField(json, "sceneNames");
            snap.hash = JsonReadStringField(json, "hash");
            snap.totalGameObjects = JsonReadIntField(json, "totalGameObjects");

            // Parse gameObjects array
            int goStart = json.IndexOf("\"gameObjects\"", StringComparison.Ordinal);
            if (goStart < 0) return snap;

            int arrayStart = json.IndexOf('[', goStart);
            if (arrayStart < 0) return snap;

            int depth = 0;
            int objectStart = -1;
            for (int i = arrayStart; i < json.Length; i++)
            {
                char c = json[i];
                if (c == '{')
                {
                    if (depth == 0) objectStart = i;
                    depth++;
                }
                else if (c == '}')
                {
                    depth--;
                    if (depth == 0 && objectStart >= 0)
                    {
                        string objectJson = json.Substring(objectStart, i - objectStart + 1);
                        snap.gameObjects.Add(ParseGOEntry(objectJson));
                        objectStart = -1;
                    }
                }
                else if (c == ']' && depth == 0)
                {
                    break;
                }
            }

            return snap;
        }

        private static SceneDiffGOEntry ParseGOEntry(string json)
        {
            var entry = new SceneDiffGOEntry();
            entry.path = JsonReadStringField(json, "path");
            entry.name = JsonReadStringField(json, "name");
            entry.tag = JsonReadStringField(json, "tag");
            entry.layer = JsonReadIntField(json, "layer");
            entry.activeSelf = JsonReadBoolField(json, "activeSelf");
            entry.activeInHierarchy = JsonReadBoolField(json, "activeInHierarchy");
            entry.components = new List<string>();

            int compIdx = json.IndexOf("\"components\"", StringComparison.Ordinal);
            if (compIdx >= 0)
            {
                int arrStart = json.IndexOf('[', compIdx);
                int arrEnd = json.IndexOf(']', arrStart);
                if (arrStart >= 0 && arrEnd >= 0)
                {
                    string arrContent = json.Substring(arrStart + 1, arrEnd - arrStart - 1).Trim();
                    if (!string.IsNullOrEmpty(arrContent))
                    {
                        foreach (string raw in arrContent.Split(','))
                        {
                            string trimmed = raw.Trim().Trim('"');
                            if (!string.IsNullOrEmpty(trimmed))
                                entry.components.Add(trimmed);
                        }
                    }
                }
            }

            // Parse componentDetails if present
            int cdIdx = json.IndexOf("\"componentDetails\"", StringComparison.Ordinal);
            if (cdIdx >= 0)
            {
                entry.componentDetails = new List<SceneDiffComponentEntry>();
                int cdArrStart = json.IndexOf('[', cdIdx);
                if (cdArrStart >= 0)
                {
                    // Walk to find each object in the componentDetails array
                    int depth = 0;
                    int objStart = -1;
                    for (int i = cdArrStart; i < json.Length; i++)
                    {
                        char c = json[i];
                        if (c == '{')
                        {
                            if (depth == 0) objStart = i;
                            depth++;
                        }
                        else if (c == '}')
                        {
                            depth--;
                            if (depth == 0 && objStart >= 0)
                            {
                                string cdJson = json.Substring(objStart, i - objStart + 1);
                                entry.componentDetails.Add(ParseComponentEntry(cdJson));
                                objStart = -1;
                            }
                        }
                        else if (c == ']' && depth == 0)
                        {
                            break;
                        }
                    }
                }
            }

            return entry;
        }

        private static SceneDiffComponentEntry ParseComponentEntry(string json)
        {
            var entry = new SceneDiffComponentEntry
            {
                typeName = JsonReadStringField(json, "typeName"),
                properties = new List<SceneDiffPropertyEntry>()
            };

            int propsIdx = json.IndexOf("\"properties\"", StringComparison.Ordinal);
            if (propsIdx < 0) return entry;

            int arrStart = json.IndexOf('[', propsIdx);
            if (arrStart < 0) return entry;

            int depth = 0;
            int objStart = -1;
            for (int i = arrStart; i < json.Length; i++)
            {
                char c = json[i];
                if (c == '{')
                {
                    if (depth == 0) objStart = i;
                    depth++;
                }
                else if (c == '}')
                {
                    depth--;
                    if (depth == 0 && objStart >= 0)
                    {
                        string propJson = json.Substring(objStart, i - objStart + 1);
                        entry.properties.Add(new SceneDiffPropertyEntry
                        {
                            path = JsonReadStringField(propJson, "path"),
                            type = JsonReadStringField(propJson, "type"),
                            value = JsonReadStringField(propJson, "value")
                        });
                        objStart = -1;
                    }
                }
                else if (c == ']' && depth == 0)
                {
                    break;
                }
            }

            return entry;
        }

        // JSON helper: produce a JSON string literal
        private static string JsonStr(string s)
        {
            if (s == null) return "null";
            return "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r") + "\"";
        }

        // Minimal JSON field readers
        private static string JsonReadStringField(string json, string field)
        {
            string key = $"\"{field}\"";
            int idx = json.IndexOf(key, StringComparison.Ordinal);
            if (idx < 0) return null;
            int colon = json.IndexOf(':', idx + key.Length);
            if (colon < 0) return null;
            int q1 = json.IndexOf('"', colon + 1);
            if (q1 < 0) return null;
            int q2 = q1 + 1;
            while (q2 < json.Length)
            {
                if (json[q2] == '"' && json[q2 - 1] != '\\') break;
                q2++;
            }
            return json.Substring(q1 + 1, q2 - q1 - 1)
                       .Replace("\\\"", "\"")
                       .Replace("\\\\", "\\")
                       .Replace("\\n", "\n")
                       .Replace("\\r", "\r");
        }

        private static int JsonReadIntField(string json, string field)
        {
            string key = $"\"{field}\"";
            int idx = json.IndexOf(key, StringComparison.Ordinal);
            if (idx < 0) return 0;
            int colon = json.IndexOf(':', idx + key.Length);
            if (colon < 0) return 0;
            int start = colon + 1;
            while (start < json.Length && (json[start] == ' ' || json[start] == '\t' || json[start] == '\n' || json[start] == '\r'))
                start++;
            int end = start;
            while (end < json.Length && (char.IsDigit(json[end]) || json[end] == '-'))
                end++;
            return int.TryParse(json.Substring(start, end - start), out int result) ? result : 0;
        }

        private static bool JsonReadBoolField(string json, string field)
        {
            string key = $"\"{field}\"";
            int idx = json.IndexOf(key, StringComparison.Ordinal);
            if (idx < 0) return false;
            int colon = json.IndexOf(':', idx + key.Length);
            if (colon < 0) return false;
            int start = colon + 1;
            while (start < json.Length && (json[start] == ' ' || json[start] == '\t' || json[start] == '\n' || json[start] == '\r'))
                start++;
            return json.Length > start + 3 && json.Substring(start, 4) == "true";
        }

        // ─────────────────────────────────────────────────────
        //  Internal: SHA-256 Hash (from sorted entry data)
        // ─────────────────────────────────────────────────────

        private static string ComputeEntriesHash(List<SceneDiffGOEntry> entries)
        {
            // Build a stable canonical string from sorted entries
            var raw = new StringBuilder();
            foreach (var e in entries) // already sorted by caller
            {
                raw.Append(e.path).Append('|')
                   .Append(e.activeSelf).Append('|')
                   .Append(e.activeInHierarchy).Append('|')
                   .Append(e.layer).Append('|')
                   .Append(e.tag).Append('|')
                   .Append(string.Join(",", e.components))
                   .Append('\n');
            }
            using (var sha256 = SHA256.Create())
            {
                byte[] bytes = Encoding.UTF8.GetBytes(raw.ToString());
                byte[] hashBytes = sha256.ComputeHash(bytes);
                var sb = new StringBuilder(hashBytes.Length * 2);
                foreach (byte b in hashBytes) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        // ─────────────────────────────────────────────────────
        //  Internal: Load Snapshot from Disk
        // ─────────────────────────────────────────────────────

        private static SceneDiffSnapshot LoadSnapshot(string label)
        {
            string content = OutputWriter.ReadSnapshot(label);
            if (content == null)
            {
                Debug.LogError($"[AgentBridge] SceneDiff: Snapshot \"{label}\" not found.");
                return null;
            }
            return SnapshotFromJson(content);
        }

        // ─────────────────────────────────────────────────────
        //  Internal: Route Comparison by Mode
        // ─────────────────────────────────────────────────────

        private static string RunComparison(SceneDiffMode mode, SceneDiffSnapshot a, SceneDiffSnapshot b)
        {
            switch (mode)
            {
                case SceneDiffMode.HashOnly:        return CompareHashes(a, b);
                case SceneDiffMode.ObjectCountDiff: return CompareObjectCounts(a, b);
                case SceneDiffMode.StructuralDiff:  return CompareStructure(a, b);
                case SceneDiffMode.PropertyDiff:    return CompareProperties(a, b);
                default: return CompareHashes(a, b);
            }
        }

        // ─────────────────────────────────────────────────────
        //  Mode A: Hash Only
        // ─────────────────────────────────────────────────────

        private static string CompareHashes(SceneDiffSnapshot a, SceneDiffSnapshot b)
        {
            bool identical = a.hash == b.hash;

            var sb = new StringBuilder();
            sb.AppendLine("# Scene Diff — Mode: Hash Only");
            sb.AppendLine();
            sb.AppendLine($"**Snapshot A:** {a.label} ({a.timestamp})");
            sb.AppendLine($"**Snapshot B:** {b.label} ({b.timestamp})");
            sb.AppendLine($"**Scope:** {a.rootScope}");
            sb.AppendLine();
            sb.AppendLine($"**Hash A:** {a.hash}");
            sb.AppendLine($"**Hash B:** {b.hash}");
            sb.AppendLine();
            sb.AppendLine($"**Result:** {(identical ? "IDENTICAL" : "DIFFERENT")}");
            sb.AppendLine();
            sb.AppendLine("---");
            sb.Append($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            return sb.ToString();
        }

        // ─────────────────────────────────────────────────────
        //  Mode B: Object Count Diff
        // ─────────────────────────────────────────────────────

        private static string CompareObjectCounts(SceneDiffSnapshot a, SceneDiffSnapshot b)
        {
            int deltaGO = b.totalGameObjects - a.totalGameObjects;

            var countA = new Dictionary<string, int>();
            var countB = new Dictionary<string, int>();

            if (a.gameObjects != null)
                foreach (var entry in a.gameObjects)
                    if (entry.components != null)
                        foreach (var comp in entry.components)
                            countA[comp] = countA.TryGetValue(comp, out int v) ? v + 1 : 1;

            if (b.gameObjects != null)
                foreach (var entry in b.gameObjects)
                    if (entry.components != null)
                        foreach (var comp in entry.components)
                            countB[comp] = countB.TryGetValue(comp, out int v) ? v + 1 : 1;

            var allTypes = new HashSet<string>(countA.Keys);
            allTypes.UnionWith(countB.Keys);

            var sorted = allTypes.OrderByDescending(t =>
            {
                int ca = countA.TryGetValue(t, out int va) ? va : 0;
                int cb = countB.TryGetValue(t, out int vb) ? vb : 0;
                return Math.Abs(cb - ca);
            }).ThenBy(t => t).ToList();

            var sb = new StringBuilder();
            sb.AppendLine($"# Scene Diff — Mode: Object Count | A: {a.label} → B: {b.label}");
            sb.AppendLine();
            sb.AppendLine("## GameObject Count");
            string deltaSign = deltaGO >= 0 ? "+" : "";
            sb.AppendLine($"**A:** {a.totalGameObjects} | **B:** {b.totalGameObjects} | **Delta:** {deltaSign}{deltaGO}");
            sb.AppendLine();
            sb.AppendLine("## Component Type Counts");
            sb.AppendLine("| Component Type | Count in A | Count in B | Delta |");
            sb.AppendLine("| :--- | :--- | :--- | :--- |");

            foreach (string type in sorted)
            {
                int ca = countA.TryGetValue(type, out int va) ? va : 0;
                int cb = countB.TryGetValue(type, out int vb) ? vb : 0;
                int delta = cb - ca;
                string sign = delta >= 0 ? "+" : "";
                sb.AppendLine($"| {type} | {ca} | {cb} | {sign}{delta} |");
            }

            sb.AppendLine();
            sb.AppendLine("---");
            sb.Append($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            return sb.ToString();
        }

        // ─────────────────────────────────────────────────────
        //  Mode C: Structural Diff
        // ─────────────────────────────────────────────────────

        private static string CompareStructure(SceneDiffSnapshot a, SceneDiffSnapshot b)
        {
            var goA = a.gameObjects ?? new List<SceneDiffGOEntry>();
            var goB = b.gameObjects ?? new List<SceneDiffGOEntry>();

            var dictA = goA.ToDictionary(e => e.path, e => e);
            var dictB = goB.ToDictionary(e => e.path, e => e);

            var pathsA = new HashSet<string>(dictA.Keys);
            var pathsB = new HashSet<string>(dictB.Keys);

            var rawAdded = pathsB.Except(pathsA).ToList();
            var rawRemoved = pathsA.Except(pathsB).ToList();

            // Detect moves: same name, different path
            var removedByName = new Dictionary<string, List<string>>();
            foreach (string p in rawRemoved)
            {
                string n = dictA[p].name;
                if (!removedByName.ContainsKey(n)) removedByName[n] = new List<string>();
                removedByName[n].Add(p);
            }

            var moved = new List<(string oldPath, string newPath)>();
            var added = new List<string>(rawAdded);
            var removed = new List<string>(rawRemoved);

            foreach (string addedPath in rawAdded.ToList())
            {
                string name = dictB[addedPath].name;
                if (removedByName.TryGetValue(name, out List<string> candidates) && candidates.Count > 0)
                {
                    string oldPath = candidates[0];
                    candidates.RemoveAt(0);
                    moved.Add((oldPath, addedPath));
                    added.Remove(addedPath);
                    removed.Remove(oldPath);
                }
            }

            var componentChanged = new List<string>();
            foreach (string path in pathsA.Intersect(pathsB))
            {
                var compsA = dictA[path].components ?? new List<string>();
                var compsB = dictB[path].components ?? new List<string>();
                if (!compsA.SequenceEqual(compsB)) componentChanged.Add(path);
            }

            int unchangedCount = pathsA.Intersect(pathsB).Count() - componentChanged.Count;

            var sb = new StringBuilder();
            sb.AppendLine($"# Scene Diff — Mode: Structural | A: {a.label} → B: {b.label}");
            sb.AppendLine();

            // Added
            sb.AppendLine("## Added GameObjects (in B, not in A)");
            if (added.Count == 0)
            {
                sb.AppendLine("*None.*");
            }
            else
            {
                foreach (string path in added.OrderBy(p => p))
                {
                    var entry = dictB[path];
                    string layerName = LayerMask.LayerToName(entry.layer);
                    sb.AppendLine($"- [+] {path} (L:{layerName}, T:{entry.tag})");
                    if (entry.components != null && entry.components.Count > 0)
                        sb.AppendLine($"  > [C] {string.Join(", ", entry.components)}");
                }
            }
            sb.AppendLine();

            // Removed
            sb.AppendLine("## Removed GameObjects (in A, not in B)");
            if (removed.Count == 0)
            {
                sb.AppendLine("*None.*");
            }
            else
            {
                foreach (string path in removed.OrderBy(p => p))
                {
                    var entry = dictA[path];
                    string layerName = LayerMask.LayerToName(entry.layer);
                    sb.AppendLine($"- [-] {path} (L:{layerName}, T:{entry.tag})");
                    if (entry.components != null && entry.components.Count > 0)
                        sb.AppendLine($"  > [C] {string.Join(", ", entry.components)}");
                }
            }
            sb.AppendLine();

            // Moved
            sb.AppendLine("## Moved GameObjects (same name, different path)");
            if (moved.Count == 0)
            {
                sb.AppendLine("*None.*");
            }
            else
            {
                foreach (var (oldPath, newPath) in moved.OrderBy(m => m.oldPath))
                    sb.AppendLine($"- [~] {dictA[oldPath].name}: {oldPath} → {newPath}");
            }
            sb.AppendLine();

            // Component changes
            sb.AppendLine("## Component Changes (same path, different components)");
            if (componentChanged.Count == 0)
            {
                sb.AppendLine("*None.*");
            }
            else
            {
                foreach (string path in componentChanged.OrderBy(p => p))
                {
                    var compsA = dictA[path].components ?? new List<string>();
                    var compsB = dictB[path].components ?? new List<string>();
                    sb.AppendLine($"- [Δ] {path}");
                    foreach (string c in compsB.Except(compsA)) sb.AppendLine($"  Added: [+] {c}");
                    foreach (string c in compsA.Except(compsB)) sb.AppendLine($"  Removed: [-] {c}");
                }
            }
            sb.AppendLine();

            // Summary
            sb.AppendLine("## Summary");
            sb.AppendLine($"- Added: {added.Count} GameObjects");
            sb.AppendLine($"- Removed: {removed.Count} GameObjects");
            sb.AppendLine($"- Moved: {moved.Count} GameObjects");
            sb.AppendLine($"- Component changes: {componentChanged.Count} GameObjects");
            sb.AppendLine($"- Unchanged: {unchangedCount} GameObjects");
            sb.AppendLine();
            sb.AppendLine("---");
            sb.Append($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            return sb.ToString();
        }

        // ─────────────────────────────────────────────────────
        //  Mode D: Property Diff
        // ─────────────────────────────────────────────────────

        private static string CompareProperties(SceneDiffSnapshot a, SceneDiffSnapshot b)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"# Scene Diff — Mode: Property Diff | A: {a.label} → B: {b.label}");
            sb.AppendLine();

            // Validate that both snapshots have property data
            bool aHasProps = a.gameObjects != null && a.gameObjects.Count > 0 && a.gameObjects[0].componentDetails != null;
            bool bHasProps = b.gameObjects != null && b.gameObjects.Count > 0 && b.gameObjects[0].componentDetails != null;

            if (!aHasProps)
            {
                sb.AppendLine($"**ERROR:** Snapshot '{a.label}' was not captured with property values. Re-capture with Mode D.");
                return sb.ToString();
            }
            if (!bHasProps)
            {
                sb.AppendLine($"**ERROR:** Snapshot '{b.label}' was not captured with property values. Re-capture with Mode D.");
                return sb.ToString();
            }

            // Build lookup: goPath/componentType/propertyPath -> value
            var dictA = BuildPropertyDict(a);
            var dictB = BuildPropertyDict(b);

            // Find changed properties
            var changes = new List<(string goPath, string compType, string propPath, string oldVal, string newVal)>();
            foreach (var kvp in dictB)
            {
                if (dictA.TryGetValue(kvp.Key, out string oldVal) && oldVal != kvp.Value)
                {
                    var parts = kvp.Key.Split('|');
                    if (parts.Length == 3)
                        changes.Add((parts[0], parts[1], parts[2], oldVal, kvp.Value));
                }
            }

            // Also run structural comparison
            var goA = a.gameObjects ?? new List<SceneDiffGOEntry>();
            var goB = b.gameObjects ?? new List<SceneDiffGOEntry>();
            var dictGoA = goA.ToDictionary(e => e.path, e => e);
            var dictGoB = goB.ToDictionary(e => e.path, e => e);
            var pathsA = new HashSet<string>(dictGoA.Keys);
            var pathsB = new HashSet<string>(dictGoB.Keys);
            var added = pathsB.Except(pathsA).ToList();
            var removed = pathsA.Except(pathsB).ToList();
            var compChanged = pathsA.Intersect(pathsB)
                .Where(p => !( (dictGoA[p].components ?? new List<string>()).SequenceEqual(dictGoB[p].components ?? new List<string>()) ))
                .ToList();

            // Property Changes table
            sb.AppendLine("## Property Changes");
            if (changes.Count == 0)
            {
                sb.AppendLine("*No property changes.*");
            }
            else
            {
                sb.AppendLine("| GameObject Path | Component | Property | Old Value | New Value |");
                sb.AppendLine("| :--- | :--- | :--- | :--- | :--- |");
                foreach (var (goPath, compType, propPath, oldVal, newVal) in changes)
                    sb.AppendLine($"| {goPath} | {compType} | {propPath} | {oldVal} | {newVal} |");
            }
            sb.AppendLine();

            // Added/Removed GameObjects
            sb.AppendLine("## Added GameObjects");
            if (added.Count == 0) sb.AppendLine("*None.*");
            else foreach (string p in added.OrderBy(x => x)) sb.AppendLine($"- [+] {p}");
            sb.AppendLine();

            sb.AppendLine("## Removed GameObjects");
            if (removed.Count == 0) sb.AppendLine("*None.*");
            else foreach (string p in removed.OrderBy(x => x)) sb.AppendLine($"- [-] {p}");
            sb.AppendLine();

            sb.AppendLine("## Added/Removed Components");
            if (compChanged.Count == 0)
            {
                sb.AppendLine("*None.*");
            }
            else
            {
                foreach (string p in compChanged.OrderBy(x => x))
                {
                    var ca = dictGoA[p].components ?? new List<string>();
                    var cb = dictGoB[p].components ?? new List<string>();
                    sb.AppendLine($"- [Δ] {p}");
                    foreach (string c in cb.Except(ca)) sb.AppendLine($"  [+] {c}");
                    foreach (string c in ca.Except(cb)) sb.AppendLine($"  [-] {c}");
                }
            }
            sb.AppendLine();

            sb.AppendLine("## Summary");
            sb.AppendLine($"- Properties changed: {changes.Count}");
            sb.AppendLine($"- GameObjects added: {added.Count}");
            sb.AppendLine($"- GameObjects removed: {removed.Count}");
            sb.AppendLine($"- Components added/removed: {compChanged.Count}");
            sb.AppendLine();
            sb.AppendLine("---");
            sb.Append($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            return sb.ToString();
        }

        private static Dictionary<string, string> BuildPropertyDict(SceneDiffSnapshot snap)
        {
            var dict = new Dictionary<string, string>(StringComparer.Ordinal);
            if (snap.gameObjects == null) return dict;
            foreach (var go in snap.gameObjects)
            {
                if (go.componentDetails == null) continue;
                foreach (var comp in go.componentDetails)
                {
                    if (comp.properties == null) continue;
                    foreach (var prop in comp.properties)
                    {
                        string key = $"{go.path}|{comp.typeName}|{prop.path}";
                        dict[key] = prop.value ?? "";
                    }
                }
            }
            return dict;
        }
    }
}
