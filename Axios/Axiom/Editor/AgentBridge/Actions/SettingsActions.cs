using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using UnityEngine;
using UnityEditor;
using UnityEditor.Build;
using Axiom.Editor.AgentBridge.Core;

namespace Axiom.Editor.AgentBridge.Actions
{
    /// <summary>
    /// Write counterpart to SettingsReporter. Modifies project-level settings:
    /// PlayerSettings, QualitySettings, Tags, Layers, Scripting Defines,
    /// Physics collision matrix, and Time/Physics values.
    /// </summary>
    public static class SettingsActions
    {
        // ─────────────────────────────────────────────────────
        //  3.1 SetScriptingDefines
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// Adds or removes scripting define symbols for the active build target.
        /// Uses NamedBuildTarget (Unity 6 API).
        /// </summary>
        public static ActionResult SetScriptingDefines(
            string[] defines = null,
            string[] remove = null,
            BuildTargetGroup? buildTargetGroup = null)
        {
            var targetGroup = buildTargetGroup ?? EditorUserBuildSettings.selectedBuildTargetGroup;
            var namedTarget = NamedBuildTarget.FromBuildTargetGroup(targetGroup);

            string currentDefines = PlayerSettings.GetScriptingDefineSymbols(namedTarget);
            var defineList = new List<string>(
                currentDefines.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries));

            int addedCount = 0;
            int removedCount = 0;

            if (defines != null)
            {
                foreach (string def in defines)
                {
                    string trimmed = def.Trim();
                    if (!defineList.Contains(trimmed))
                    {
                        defineList.Add(trimmed);
                        addedCount++;
                    }
                }
            }

            if (remove != null)
            {
                foreach (string def in remove)
                {
                    string trimmed = def.Trim();
                    if (defineList.Remove(trimmed))
                        removedCount++;
                }
            }

            string newDefines = string.Join(";", defineList);
            PlayerSettings.SetScriptingDefineSymbols(namedTarget, newDefines);

            Debug.Log($"[AgentBridge] Scripting defines updated: +{addedCount} -{removedCount}. Current: {newDefines}");
            return ActionResult.Ok($"Defines updated: +{addedCount} -{removedCount}. Current: {newDefines}");
        }

        // ─────────────────────────────────────────────────────
        //  3.2 AddTag
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// Adds a custom tag to the project via TagManager.
        /// </summary>
        public static ActionResult AddTag(string tagName)
        {
            var tagManager = new SerializedObject(
                AssetDatabase.LoadMainAssetAtPath("ProjectSettings/TagManager.asset"));
            var tagsProp = tagManager.FindProperty("tags");

            for (int i = 0; i < tagsProp.arraySize; i++)
            {
                if (tagsProp.GetArrayElementAtIndex(i).stringValue == tagName)
                    return ActionResult.Ok($"Tag already exists: {tagName}");
            }

            // Find first empty slot or append
            int emptyIndex = -1;
            for (int i = 0; i < tagsProp.arraySize; i++)
            {
                if (string.IsNullOrEmpty(tagsProp.GetArrayElementAtIndex(i).stringValue))
                {
                    emptyIndex = i;
                    break;
                }
            }

            if (emptyIndex >= 0)
            {
                tagsProp.GetArrayElementAtIndex(emptyIndex).stringValue = tagName;
            }
            else
            {
                tagsProp.InsertArrayElementAtIndex(tagsProp.arraySize);
                tagsProp.GetArrayElementAtIndex(tagsProp.arraySize - 1).stringValue = tagName;
            }

            tagManager.ApplyModifiedProperties();
            Debug.Log($"[AgentBridge] Added tag: {tagName}");
            return ActionResult.Ok($"Added tag: {tagName}");
        }

        // ─────────────────────────────────────────────────────
        //  3.3 AddLayer
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// Adds a custom layer to the project via TagManager.
        /// </summary>
        public static ActionResult AddLayer(string layerName, int? layerIndex = null)
        {
            var tagManager = new SerializedObject(
                AssetDatabase.LoadMainAssetAtPath("ProjectSettings/TagManager.asset"));
            var layersProp = tagManager.FindProperty("layers");

            for (int i = 0; i < layersProp.arraySize; i++)
            {
                if (layersProp.GetArrayElementAtIndex(i).stringValue == layerName)
                    return ActionResult.Ok($"Layer already exists: {layerName} (index {i})");
            }

            if (layerIndex.HasValue)
            {
                int idx = layerIndex.Value;
                if (idx < 0 || idx > 31)
                    return ActionResult.Fail($"Layer index out of range: {idx} (must be 0-31)");
                if (idx < 8)
                    return ActionResult.Fail($"Layer index {idx} is a built-in layer and cannot be modified");

                var existing = layersProp.GetArrayElementAtIndex(idx).stringValue;
                if (!string.IsNullOrEmpty(existing))
                    return ActionResult.Fail($"Layer index {idx} already occupied by: {existing}");

                layersProp.GetArrayElementAtIndex(idx).stringValue = layerName;
            }
            else
            {
                int found = -1;
                for (int i = 8; i < 32 && i < layersProp.arraySize; i++)
                {
                    if (string.IsNullOrEmpty(layersProp.GetArrayElementAtIndex(i).stringValue))
                    {
                        found = i;
                        break;
                    }
                }

                if (found < 0)
                    return ActionResult.Fail("No empty layer slots available (layers 8-31 are all occupied)");

                layersProp.GetArrayElementAtIndex(found).stringValue = layerName;
                layerIndex = found;
            }

            tagManager.ApplyModifiedProperties();
            Debug.Log($"[AgentBridge] Added layer: {layerName} (index {layerIndex})");
            return ActionResult.Ok($"Added layer: {layerName} (index {layerIndex})");
        }

        // ─────────────────────────────────────────────────────
        //  3.4 SetQualityLevel
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// Switches the active quality level by name.
        /// </summary>
        public static ActionResult SetQualityLevel(string levelName)
        {
            string[] names = QualitySettings.names;
            int targetIndex = -1;

            for (int i = 0; i < names.Length; i++)
            {
                if (names[i].Equals(levelName, StringComparison.OrdinalIgnoreCase))
                {
                    targetIndex = i;
                    break;
                }
            }

            if (targetIndex < 0)
                return ActionResult.Fail($"Quality level not found: {levelName}. Available: {string.Join(", ", names)}");

            QualitySettings.SetQualityLevel(targetIndex, true);
            Debug.Log($"[AgentBridge] Set quality level: {levelName} (index {targetIndex})");
            return ActionResult.Ok($"Set quality level: {levelName} (index {targetIndex})");
        }

        // ─────────────────────────────────────────────────────
        //  3.5 SetPlayerSetting
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// Sets a PlayerSettings value by property name via reflection.
        /// </summary>
        public static ActionResult SetPlayerSetting(string propertyName, string value)
        {
            var type = typeof(PlayerSettings);
            var prop = type.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Static);

            if (prop == null)
                return ActionResult.Fail($"PlayerSettings property not found: {propertyName}");

            if (!prop.CanWrite)
                return ActionResult.Fail($"PlayerSettings.{propertyName} is read-only");

            try
            {
                object converted = ConvertValue(prop.PropertyType, value);
                prop.SetValue(null, converted);
                Debug.Log($"[AgentBridge] Set PlayerSettings.{propertyName} = {value}");
                return ActionResult.Ok($"Set PlayerSettings.{propertyName} = {value}");
            }
            catch (Exception ex)
            {
                return ActionResult.Fail($"Failed to set PlayerSettings.{propertyName}: {ex.Message}");
            }
        }

        // ─────────────────────────────────────────────────────
        //  3.6 SetEditorSetting
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// Sets an EditorSettings value by property name via reflection.
        /// </summary>
        public static ActionResult SetEditorSetting(string propertyName, string value)
        {
            var type = typeof(EditorSettings);
            var prop = type.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Static);

            if (prop == null)
                return ActionResult.Fail($"EditorSettings property not found: {propertyName}");

            if (!prop.CanWrite)
                return ActionResult.Fail($"EditorSettings.{propertyName} is read-only");

            try
            {
                object converted = ConvertValue(prop.PropertyType, value);
                prop.SetValue(null, converted);
                Debug.Log($"[AgentBridge] Set EditorSettings.{propertyName} = {value}");
                return ActionResult.Ok($"Set EditorSettings.{propertyName} = {value}");
            }
            catch (Exception ex)
            {
                return ActionResult.Fail($"Failed to set EditorSettings.{propertyName}: {ex.Message}");
            }
        }

        // ─────────────────────────────────────────────────────
        //  3.7 SetLayerCollision
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// Sets whether two physics layers collide (3D or 2D).
        /// </summary>
        public static ActionResult SetLayerCollision(
            string layer1, string layer2,
            bool collide, bool is2D = false)
        {
            int idx1 = ResolveLayerIndex(layer1);
            int idx2 = ResolveLayerIndex(layer2);

            if (idx1 < 0) return ActionResult.Fail($"Layer not found: {layer1}");
            if (idx2 < 0) return ActionResult.Fail($"Layer not found: {layer2}");

            if (is2D)
                Physics2D.IgnoreLayerCollision(idx1, idx2, !collide);
            else
                Physics.IgnoreLayerCollision(idx1, idx2, !collide);

            string action = collide ? "enabled" : "disabled";
            Debug.Log($"[AgentBridge] Collision {action} between {layer1} ({idx1}) and {layer2} ({idx2})");
            return ActionResult.Ok($"Collision {action} between {layer1} ({idx1}) and {layer2} ({idx2})");
        }

        // ─────────────────────────────────────────────────────
        //  3.8 SetTimeSettings
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// Modifies Time settings (fixedDeltaTime, maximumDeltaTime, timeScale).
        /// </summary>
        public static ActionResult SetTimeSettings(
            float? fixedDeltaTime = null,
            float? maximumDeltaTime = null,
            float? timeScale = null)
        {
            var changes = new List<string>();

            if (fixedDeltaTime.HasValue)
            {
                Time.fixedDeltaTime = fixedDeltaTime.Value;
                changes.Add($"fixedDeltaTime = {fixedDeltaTime.Value}");
            }
            if (maximumDeltaTime.HasValue)
            {
                Time.maximumDeltaTime = maximumDeltaTime.Value;
                changes.Add($"maximumDeltaTime = {maximumDeltaTime.Value}");
            }
            if (timeScale.HasValue)
            {
                Time.timeScale = timeScale.Value;
                changes.Add($"timeScale = {timeScale.Value}");
            }

            if (changes.Count == 0)
                return ActionResult.Fail("No time settings specified");

            string summary = string.Join(", ", changes);
            Debug.Log($"[AgentBridge] Time settings updated: {summary}");
            return ActionResult.Ok($"Time settings updated: {summary}");
        }

        // ─────────────────────────────────────────────────────
        //  3.9 SetPhysicsSettings
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// Modifies Physics 3D settings.
        /// </summary>
        public static ActionResult SetPhysicsSettings(
            Vector3? gravity = null,
            int? defaultSolverIterations = null,
            int? defaultSolverVelocityIterations = null,
            float? bounceThreshold = null,
            bool? autoSimulation = null)
        {
            var changes = new List<string>();

            if (gravity.HasValue)
            {
                Physics.gravity = gravity.Value;
                changes.Add($"gravity = {gravity.Value}");
            }
            if (defaultSolverIterations.HasValue)
            {
                Physics.defaultSolverIterations = defaultSolverIterations.Value;
                changes.Add($"solverIterations = {defaultSolverIterations.Value}");
            }
            if (defaultSolverVelocityIterations.HasValue)
            {
                Physics.defaultSolverVelocityIterations = defaultSolverVelocityIterations.Value;
                changes.Add($"velocitySolverIterations = {defaultSolverVelocityIterations.Value}");
            }
            if (bounceThreshold.HasValue)
            {
                Physics.bounceThreshold = bounceThreshold.Value;
                changes.Add($"bounceThreshold = {bounceThreshold.Value}");
            }
            if (autoSimulation.HasValue)
            {
                // Unity 6: use simulationMode instead of deprecated autoSimulation
                Physics.simulationMode = autoSimulation.Value
                    ? SimulationMode.FixedUpdate
                    : SimulationMode.Script;
                changes.Add($"simulationMode = {Physics.simulationMode}");
            }

            if (changes.Count == 0)
                return ActionResult.Fail("No physics settings specified");

            string summary = string.Join(", ", changes);
            Debug.Log($"[AgentBridge] Physics settings updated: {summary}");
            return ActionResult.Ok($"Physics settings updated: {summary}");
        }

        // ─────────────────────────────────────────────────────
        //  Editor Menu Items (testing)
        // ─────────────────────────────────────────────────────

        [MenuItem("Axiom/AgentBridge/Actions/Add Test Tag")]
        public static void MenuAddTag()
        {
            var result = AddTag("AgentBridgeTest");
            Debug.Log($"[AgentBridge] {result.Message}");
        }

        [MenuItem("Axiom/AgentBridge/Actions/Add Test Layer")]
        public static void MenuAddLayer()
        {
            var result = AddLayer("AgentBridgeTestLayer");
            Debug.Log($"[AgentBridge] {result.Message}");
        }

        [MenuItem("Axiom/AgentBridge/Actions/Report Current Quality")]
        public static void MenuReportQuality()
        {
            Debug.Log($"[AgentBridge] Current quality level: {QualitySettings.names[QualitySettings.GetQualityLevel()]}");
        }

        // ─────────────────────────────────────────────────────
        //  Private Helpers
        // ─────────────────────────────────────────────────────

        private static object ConvertValue(Type targetType, string value)
        {
            if (targetType == typeof(string)) return value;
            if (targetType == typeof(int)) return int.Parse(value);
            if (targetType == typeof(float)) return float.Parse(value, CultureInfo.InvariantCulture);
            if (targetType == typeof(bool)) return bool.Parse(value);
            if (targetType.IsEnum) return Enum.Parse(targetType, value, true);

            throw new ArgumentException($"Unsupported type: {targetType.Name}");
        }

        private static int ResolveLayerIndex(string layer)
        {
            int idx = LayerMask.NameToLayer(layer);
            if (idx >= 0) return idx;

            if (int.TryParse(layer, out int numIdx) && numIdx >= 0 && numIdx <= 31)
                return numIdx;

            return -1;
        }
    }
}
