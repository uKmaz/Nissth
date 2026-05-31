#if UNITY_6000_0_OR_NEWER

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Profile;
using Axiom.Editor.AgentBridge.Core;

namespace Axiom.Editor.AgentBridge.Actions
{
    /// <summary>
    /// Gives the agent control over the Unity 6 Build Profile system —
    /// listing, modifying, comparing, and triggering builds.
    /// Build Profiles replace global PlayerSettings as the source of truth in Unity 6.
    /// </summary>
    public static class BuildProfileActions
    {
        // ─────────────────────────────────────────────────────
        //  Reflection Helpers for BuildProfile properties
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// Gets the BuildTarget from a BuildProfile using reflection
        /// (property name varies across Unity 6 patch versions).
        /// </summary>
        private static BuildTarget GetProfileBuildTarget(BuildProfile profile)
        {
            var type = profile.GetType();
            // Try common property name candidates
            foreach (string name in new[] { "buildTarget", "BuildTarget", "m_BuildTarget" })
            {
                var prop = type.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (prop != null)
                    return (BuildTarget)prop.GetValue(profile);
                var field = type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (field != null)
                    return (BuildTarget)field.GetValue(profile);
            }
            return EditorUserBuildSettings.activeBuildTarget;
        }

        /// <summary>
        /// Gets the subtarget from a BuildProfile using reflection.
        /// Returns -1 if not found.
        /// </summary>
        private static int GetProfileSubtarget(BuildProfile profile)
        {
            var type = profile.GetType();
            foreach (string name in new[] { "subtarget", "Subtarget", "m_Subtarget" })
            {
                var prop = type.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (prop != null)
                    return Convert.ToInt32(prop.GetValue(profile));
                var field = type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (field != null)
                    return Convert.ToInt32(field.GetValue(profile));
            }
            return -1;
        }

        // ─────────────────────────────────────────────────────
        //  Shared Helper: ResolveProfile
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// Resolves a Build Profile by name or asset path.
        /// </summary>
        private static BuildProfile ResolveProfile(string nameOrPath)
        {
            if (nameOrPath.StartsWith("Assets/") || nameOrPath.EndsWith(".asset"))
                return AssetDatabase.LoadAssetAtPath<BuildProfile>(nameOrPath);

            string[] guids = AssetDatabase.FindAssets($"t:BuildProfile {nameOrPath}");

            // Exact match
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var profile = AssetDatabase.LoadAssetAtPath<BuildProfile>(path);
                if (profile != null &&
                    profile.name.Equals(nameOrPath, StringComparison.OrdinalIgnoreCase))
                    return profile;
            }

            // Partial match
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var profile = AssetDatabase.LoadAssetAtPath<BuildProfile>(path);
                if (profile != null &&
                    profile.name.IndexOf(nameOrPath, StringComparison.OrdinalIgnoreCase) >= 0)
                    return profile;
            }

            return null;
        }

        // ─────────────────────────────────────────────────────
        //  4.1 ListProfiles
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// Lists all Build Profile assets with target platform and active status.
        /// </summary>
        public static ActionResult ListProfiles()
        {
            string[] guids = AssetDatabase.FindAssets("t:BuildProfile");
            if (guids.Length == 0)
                return ActionResult.Ok("No Build Profiles found. Using global PlayerSettings.");

            BuildProfile activeProfile = BuildProfile.GetActiveBuildProfile();
            string activeGuid = activeProfile != null
                ? AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(activeProfile))
                : "";

            var sb = new StringBuilder();
            sb.AppendLine("# Build Profiles\n");
            sb.AppendLine("| Profile | Platform | Active | Path |");
            sb.AppendLine("| :--- | :--- | :--- | :--- |");

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var profile = AssetDatabase.LoadAssetAtPath<BuildProfile>(path);
                if (profile == null) continue;
                bool isActive = guid == activeGuid;
                sb.AppendLine($"| {profile.name} | {GetProfileBuildTarget(profile)} | " +
                    $"{(isActive ? "**YES**" : "No")} | {path} |");
            }

            string reportPath = OutputWriter.WriteReport("build_profiles", sb.ToString());
            return ActionResult.Ok($"Found {guids.Length} Build Profiles. Report: {reportPath}");
        }

        // ─────────────────────────────────────────────────────
        //  4.2 GetActiveProfile
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// Returns detailed information about the active Build Profile.
        /// </summary>
        public static ActionResult GetActiveProfile()
        {
            BuildProfile active = BuildProfile.GetActiveBuildProfile();
            if (active == null)
                return ActionResult.Ok("No active Build Profile. Using global PlayerSettings.");

            string path = AssetDatabase.GetAssetPath(active);
            var sb = new StringBuilder();
            sb.AppendLine($"# Active Build Profile: {active.name}\n");
            sb.AppendLine($"- **Path:** {path}");
            sb.AppendLine($"- **Platform:** {GetProfileBuildTarget(active)}");
            sb.AppendLine($"- **Subtarget:** {GetProfileSubtarget(active)}");

            var so = new SerializedObject(active);
            var iter = so.GetIterator();
            iter.Next(true);
            sb.AppendLine("\n## Properties\n");
            sb.AppendLine("| Property | Type | Value |");
            sb.AppendLine("| :--- | :--- | :--- |");

            do
            {
                if (iter.propertyPath == "m_Script") continue;
                string val;
                try { val = PropertyValueParser.GetValueString(iter); }
                catch { val = "(complex)"; }
                sb.AppendLine($"| {iter.propertyPath} | {iter.propertyType} | {val} |");
            }
            while (iter.NextVisible(false));

            string reportPath = OutputWriter.WriteReport("active_build_profile", sb.ToString());
            return ActionResult.Ok(
                $"Active profile: {active.name} ({GetProfileBuildTarget(active)}). Report: {reportPath}",
                active);
        }

        // ─────────────────────────────────────────────────────
        //  4.3 SetActiveProfile
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// Sets the active Build Profile by name or asset path.
        /// </summary>
        public static ActionResult SetActiveProfile(string profileNameOrPath)
        {
            BuildProfile profile = ResolveProfile(profileNameOrPath);
            if (profile == null)
                return ActionResult.Fail($"Build Profile not found: {profileNameOrPath}");

            BuildProfile.SetActiveBuildProfile(profile);

            Debug.Log($"[AgentBridge] Active profile: {profile.name} ({GetProfileBuildTarget(profile)})");
            return ActionResult.Ok(
                $"Active Build Profile: {profile.name} ({GetProfileBuildTarget(profile)})", profile);
        }

        // ─────────────────────────────────────────────────────
        //  4.4 ModifyProfileDefines
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// Adds or removes scripting define symbols on a Build Profile.
        /// Preferred over global PlayerSettings defines in Unity 6.
        /// </summary>
        public static ActionResult ModifyProfileDefines(
            string profileNameOrPath,
            string[] addDefines = null,
            string[] removeDefines = null)
        {
            BuildProfile profile = ResolveProfile(profileNameOrPath);
            if (profile == null)
                return ActionResult.Fail($"Build Profile not found: {profileNameOrPath}");

            var so = new SerializedObject(profile);

            // Try to find scripting defines property — path depends on Unity 6 version
            var definesProp = so.FindProperty("m_ScriptingDefines")
                           ?? so.FindProperty("scriptingDefineSymbols");

            if (definesProp == null)
            {
                // Fallback: Use PlayerSettings with NamedBuildTarget from profile
                return ModifyProfileDefinesViaPlayerSettings(profile, addDefines, removeDefines);
            }

            // Handle string property (semicolon-separated)
            if (definesProp.propertyType == SerializedPropertyType.String)
            {
                var defineList = new List<string>(
                    definesProp.stringValue.Split(';', StringSplitOptions.RemoveEmptyEntries));

                int added = 0, removed = 0;
                if (addDefines != null)
                    foreach (string d in addDefines)
                        if (!defineList.Contains(d.Trim())) { defineList.Add(d.Trim()); added++; }
                if (removeDefines != null)
                    foreach (string d in removeDefines)
                        if (defineList.Remove(d.Trim())) removed++;

                Undo.RecordObject(profile, "ModifyProfileDefines");
                definesProp.stringValue = string.Join(";", defineList);
                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(profile);

                return ActionResult.Ok(
                    $"Profile '{profile.name}' defines: +{added} -{removed}. Current: {definesProp.stringValue}");
            }

            // Handle array of strings
            if (definesProp.isArray)
            {
                var defineList = new List<string>();
                for (int i = 0; i < definesProp.arraySize; i++)
                    defineList.Add(definesProp.GetArrayElementAtIndex(i).stringValue);

                int added = 0, removed = 0;
                if (addDefines != null)
                    foreach (string d in addDefines)
                        if (!defineList.Contains(d.Trim())) { defineList.Add(d.Trim()); added++; }
                if (removeDefines != null)
                    foreach (string d in removeDefines)
                        if (defineList.Remove(d.Trim())) removed++;

                Undo.RecordObject(profile, "ModifyProfileDefines");
                definesProp.ClearArray();
                for (int i = 0; i < defineList.Count; i++)
                {
                    definesProp.InsertArrayElementAtIndex(i);
                    definesProp.GetArrayElementAtIndex(i).stringValue = defineList[i];
                }
                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(profile);

                return ActionResult.Ok(
                    $"Profile '{profile.name}' defines: +{added} -{removed}. " +
                    $"Current: {string.Join(";", defineList)}");
            }

            return ActionResult.Fail(
                $"Unexpected property type for scripting defines: {definesProp.propertyType}");
        }

        private static ActionResult ModifyProfileDefinesViaPlayerSettings(
            BuildProfile profile, string[] addDefines, string[] removeDefines)
        {
            var namedTarget = NamedBuildTarget.FromBuildTargetGroup(
                BuildPipeline.GetBuildTargetGroup(GetProfileBuildTarget(profile)));

            string current = PlayerSettings.GetScriptingDefineSymbols(namedTarget);
            var defineList = new List<string>(
                current.Split(';', StringSplitOptions.RemoveEmptyEntries));

            int added = 0, removed = 0;
            if (addDefines != null)
                foreach (string d in addDefines)
                    if (!defineList.Contains(d.Trim())) { defineList.Add(d.Trim()); added++; }
            if (removeDefines != null)
                foreach (string d in removeDefines)
                    if (defineList.Remove(d.Trim())) removed++;

            string newDefines = string.Join(";", defineList);
            PlayerSettings.SetScriptingDefineSymbols(namedTarget, newDefines);

            return ActionResult.Ok(
                $"Profile '{profile.name}' defines (PlayerSettings fallback): " +
                $"+{added} -{removed}. Current: {newDefines}");
        }

        // ─────────────────────────────────────────────────────
        //  4.5 DiffProfiles
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// Compares two Build Profiles and reports differences.
        /// </summary>
        public static ActionResult DiffProfiles(string profileA, string profileB)
        {
            var a = ResolveProfile(profileA);
            if (a == null) return ActionResult.Fail($"Profile A not found: {profileA}");
            var b = ResolveProfile(profileB);
            if (b == null) return ActionResult.Fail($"Profile B not found: {profileB}");

            var soA = new SerializedObject(a);
            var soB = new SerializedObject(b);

            var sb = new StringBuilder();
            sb.AppendLine($"# Build Profile Diff: {a.name} vs {b.name}\n");
            sb.AppendLine($"- **A:** {a.name} ({GetProfileBuildTarget(a)}) — {AssetDatabase.GetAssetPath(a)}");
            sb.AppendLine($"- **B:** {b.name} ({GetProfileBuildTarget(b)}) — {AssetDatabase.GetAssetPath(b)}");
            sb.AppendLine();

            int diffCount = 0;
            var iterA = soA.GetIterator();
            iterA.Next(true);

            sb.AppendLine("| Property | A | B |");
            sb.AppendLine("| :--- | :--- | :--- |");

            do
            {
                if (iterA.propertyPath == "m_Script" || iterA.propertyPath == "m_Name") continue;

                var propB = soB.FindProperty(iterA.propertyPath);
                if (propB == null)
                {
                    sb.AppendLine($"| {iterA.propertyPath} | " +
                        $"{PropertyValueParser.GetValueString(iterA)} | *(missing)* |");
                    diffCount++;
                    continue;
                }

                string valA, valB;
                try { valA = PropertyValueParser.GetValueString(iterA); } catch { valA = "(complex)"; }
                try { valB = PropertyValueParser.GetValueString(propB); } catch { valB = "(complex)"; }

                if (valA != valB)
                {
                    sb.AppendLine($"| **{iterA.propertyPath}** | {valA} | {valB} |");
                    diffCount++;
                }
            }
            while (iterA.NextVisible(false));

            sb.AppendLine($"\n---\n**Differences: {diffCount}**");

            string reportPath = OutputWriter.WriteReport("build_profile_diff", sb.ToString());
            return ActionResult.Ok(
                $"Diff: {a.name} vs {b.name} — {diffCount} differences. Report: {reportPath}");
        }

        // ─────────────────────────────────────────────────────
        //  4.6 ModifyProfileProperty
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// Modifies a serialized property on a Build Profile.
        /// </summary>
        public static ActionResult ModifyProfileProperty(
            string profileNameOrPath, string propertyPath, string value)
        {
            BuildProfile profile = ResolveProfile(profileNameOrPath);
            if (profile == null)
                return ActionResult.Fail($"Build Profile not found: {profileNameOrPath}");

            var so = new SerializedObject(profile);
            var prop = so.FindProperty(propertyPath);
            if (prop == null)
                return ActionResult.Fail($"Property not found on '{profile.name}': {propertyPath}");

            Undo.RecordObject(profile, $"Modify Profile {propertyPath}");
            PropertyValueParser.SetPropertyValue(prop, value);
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();

            Debug.Log($"[AgentBridge] Set {profile.name}.{propertyPath} = {value}");
            return ActionResult.Ok($"Set {profile.name}.{propertyPath} = {value}", profile);
        }

        // ─────────────────────────────────────────────────────
        //  4.7 ModifyBuildSceneList
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// Modifies the scene list in EditorBuildSettings.
        /// </summary>
        /// <param name="addScenes">Scene paths to add.</param>
        /// <param name="removeScenes">Scene paths to remove.</param>
        /// <param name="setScenes">If provided, replaces entire list (ordered).</param>
        public static ActionResult ModifyBuildSceneList(
            string[] addScenes = null,
            string[] removeScenes = null,
            string[] setScenes = null)
        {
            var currentScenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);

            if (setScenes != null)
            {
                currentScenes.Clear();
                foreach (string path in setScenes)
                {
                    if (!System.IO.File.Exists(path))
                    {
                        Debug.LogWarning($"[AgentBridge] Scene not found: {path}");
                        continue;
                    }
                    currentScenes.Add(new EditorBuildSettingsScene(path, true));
                }
            }
            else
            {
                if (removeScenes != null)
                    foreach (string path in removeScenes)
                        currentScenes.RemoveAll(s => s.path == path);

                if (addScenes != null)
                    foreach (string path in addScenes)
                    {
                        if (currentScenes.Any(s => s.path == path)) continue;
                        if (!System.IO.File.Exists(path))
                        {
                            Debug.LogWarning($"[AgentBridge] Scene not found: {path}");
                            continue;
                        }
                        currentScenes.Add(new EditorBuildSettingsScene(path, true));
                    }
            }

            EditorBuildSettings.scenes = currentScenes.ToArray();

            var sb = new StringBuilder("Build scenes updated:");
            for (int i = 0; i < currentScenes.Count; i++)
                sb.AppendLine($"  [{i}] {currentScenes[i].path} (enabled: {currentScenes[i].enabled})");

            Debug.Log($"[AgentBridge] {sb}");
            return ActionResult.Ok($"Build scene list updated. {currentScenes.Count} scenes total.");
        }

        // ─────────────────────────────────────────────────────
        //  4.8 TriggerBuild
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// Triggers a build. Supports dry run to preview without executing.
        /// </summary>
        /// <param name="outputPath">Build output path (e.g., "Builds/Windows/MyGame.exe").</param>
        /// <param name="profileNameOrPath">Profile to use. Null = active profile/settings.</param>
        /// <param name="options">Additional BuildOptions flags.</param>
        /// <param name="dryRun">If true, report what would be built without building.</param>
        public static ActionResult TriggerBuild(
            string outputPath,
            string profileNameOrPath = null,
            BuildOptions options = BuildOptions.None,
            bool dryRun = false)
        {
            var scenes = EditorBuildSettings.scenes
                .Where(s => s.enabled)
                .Select(s => s.path)
                .ToArray();

            if (scenes.Length == 0)
                return ActionResult.Fail("No enabled scenes in Build Settings.");

            BuildTarget target = EditorUserBuildSettings.activeBuildTarget;

            if (profileNameOrPath != null)
            {
                var profile = ResolveProfile(profileNameOrPath);
                if (profile == null)
                    return ActionResult.Fail($"Build Profile not found: {profileNameOrPath}");
                target = GetProfileBuildTarget(profile);
                BuildProfile.SetActiveBuildProfile(profile);
            }

            if (dryRun)
            {
                var sb = new StringBuilder();
                sb.AppendLine("# Build Dry Run\n");
                sb.AppendLine($"- **Target:** {target}");
                sb.AppendLine($"- **Output:** {outputPath}");
                sb.AppendLine($"- **Options:** {options}");
                sb.AppendLine($"- **Scenes:** {scenes.Length}");
                foreach (string s in scenes) sb.AppendLine($"  - {s}");

                string reportPath = OutputWriter.WriteReport("build_dryrun", sb.ToString());
                return ActionResult.Ok($"Dry run complete. Report: {reportPath}");
            }

            var buildOptions = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = outputPath,
                target = target,
                options = options
            };

            var report = BuildPipeline.BuildPlayer(buildOptions);
            _lastBuildReport = report;
            var summary = report.summary;

            var resultSb = new StringBuilder();
            resultSb.AppendLine($"# Build Report — {summary.result}\n");
            resultSb.AppendLine($"- **Platform:** {summary.platform}");
            resultSb.AppendLine($"- **Output:** {summary.outputPath}");
            resultSb.AppendLine($"- **Duration:** {summary.totalTime}");
            resultSb.AppendLine($"- **Size:** {summary.totalSize / (1024f * 1024f):F1} MB");
            resultSb.AppendLine($"- **Errors:** {summary.totalErrors}");
            resultSb.AppendLine($"- **Warnings:** {summary.totalWarnings}");

            if (summary.totalErrors > 0)
            {
                resultSb.AppendLine("\n## Errors");
                foreach (var step in report.steps)
                    foreach (var msg in step.messages)
                        if (msg.type == LogType.Error)
                            resultSb.AppendLine($"- [{step.name}] {msg.content}");
            }

            string buildReportPath = OutputWriter.WriteReport("build_report", resultSb.ToString());

            if (summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded)
            {
                Debug.Log($"[AgentBridge] Build succeeded: {summary.outputPath}");
                return ActionResult.Ok($"Build succeeded. Report: {buildReportPath}");
            }
            else
            {
                Debug.LogError($"[AgentBridge] Build failed: {summary.result}");
                return ActionResult.Fail($"Build failed: {summary.result}. Report: {buildReportPath}");
            }
        }

        // ─────────────────────────────────────────────────────
        //  4.9 AnalyzeBuildReport
        // ─────────────────────────────────────────────────────

        // Stores the most recent BuildReport from TriggerBuild
        private static UnityEditor.Build.Reporting.BuildReport _lastBuildReport;

        /// <summary>
        /// Analyzes the most recent build report, breaking down sizes by category.
        /// Call TriggerBuild first to produce a report.
        /// </summary>
        public static ActionResult AnalyzeBuildReport()
        {
            if (_lastBuildReport == null)
                return ActionResult.Fail("No build report available. Run TriggerBuild first.");

            var summary = _lastBuildReport.summary;
            var sb = new StringBuilder();

            sb.AppendLine("## Build Report Analysis");
            sb.AppendLine();
            sb.AppendLine("### Summary");
            sb.AppendLine($"- Build Target: {summary.platform}");
            sb.AppendLine($"- Total Size: {summary.totalSize / (1024f * 1024f):F1} MB");
            sb.AppendLine($"- Build Time: {summary.totalTime:mm\\m\\ ss\\s}");
            sb.AppendLine($"- Result: {summary.result}");
            sb.AppendLine($"- Errors: {summary.totalErrors}");
            sb.AppendLine($"- Warnings: {summary.totalWarnings}");
            sb.AppendLine();

            // Size by category from packed assets
            if (_lastBuildReport.packedAssets != null && _lastBuildReport.packedAssets.Length > 0)
            {
                var allAssets = _lastBuildReport.packedAssets
                    .SelectMany(pa => pa.contents)
                    .ToArray();

                long total = allAssets.Sum(a => (long)a.packedSize);
                var byType = allAssets
                    .GroupBy(a => a.type?.Name ?? "Unknown")
                    .Select(g => new { Type = g.Key, Size = g.Sum(a => (long)a.packedSize) })
                    .OrderByDescending(x => x.Size)
                    .ToList();

                sb.AppendLine("### Size by Asset Type");
                sb.AppendLine("| Type | Size | % of Total |");
                sb.AppendLine("| :--- | :--- | :--- |");
                foreach (var cat in byType.Take(20))
                {
                    float pct = total > 0 ? cat.Size * 100f / total : 0f;
                    sb.AppendLine($"| {cat.Type} | {cat.Size / (1024f * 1024f):F1} MB | {pct:F1}% |");
                }
                sb.AppendLine();

                // Top 20 largest assets
                var top20 = allAssets.OrderByDescending(a => a.packedSize).Take(20).ToArray();
                sb.AppendLine("### Largest Assets (Top 20)");
                sb.AppendLine("| Asset | Size | Type |");
                sb.AppendLine("| :--- | :--- | :--- |");
                foreach (var a in top20)
                {
                    string assetPath = string.IsNullOrEmpty(a.sourceAssetPath) ? a.id.ToString() : a.sourceAssetPath;
                    string typeName = a.type?.Name ?? "Unknown";
                    sb.AppendLine($"| {assetPath} | {a.packedSize / (1024f * 1024f):F2} MB | {typeName} |");
                }
                sb.AppendLine();
            }

            // Build steps
            if (_lastBuildReport.steps != null && _lastBuildReport.steps.Length > 0)
            {
                sb.AppendLine("### Build Steps");
                sb.AppendLine("| Step | Duration |");
                sb.AppendLine("| :--- | :--- |");
                foreach (var step in _lastBuildReport.steps.OrderByDescending(s => s.duration))
                    sb.AppendLine($"| {step.name} | {step.duration:mm\\m\\ ss\\s} |");
                sb.AppendLine();
            }

            string reportPath = OutputWriter.WriteReport("build_report_analysis", sb.ToString());
            return ActionResult.Ok($"Build report analyzed. Report: {reportPath}");
        }

        // ─────────────────────────────────────────────────────
        //  4.10 CreateProfile
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// Creates a new Build Profile asset for a specified platform.
        /// </summary>
        /// <param name="profileName">Name for the new profile asset.</param>
        /// <param name="buildTarget">Target platform (e.g., "StandaloneWindows64", "Android", "iOS").</param>
        /// <param name="savePath">Asset path to save the profile (default: "Assets/Settings/BuildProfiles/").</param>
        public static ActionResult CreateProfile(string profileName, string buildTarget, string savePath = null)
        {
            if (string.IsNullOrEmpty(profileName))
                return ActionResult.Fail("profileName is required.");
            if (string.IsNullOrEmpty(buildTarget))
                return ActionResult.Fail("buildTarget is required.");

            // Parse build target
            if (!Enum.TryParse<BuildTarget>(buildTarget, true, out var target))
                return ActionResult.Fail($"Unknown build target: '{buildTarget}'. Valid values: {string.Join(", ", Enum.GetNames(typeof(BuildTarget)))}");

            // Resolve save path
            string folder = savePath ?? "Assets/Settings/BuildProfiles";
            if (!AssetDatabase.IsValidFolder(folder))
            {
                string[] parts = folder.Split('/');
                string current = parts[0];
                for (int i = 1; i < parts.Length; i++)
                {
                    string next = current + "/" + parts[i];
                    if (!AssetDatabase.IsValidFolder(next))
                        AssetDatabase.CreateFolder(current, parts[i]);
                    current = next;
                }
            }

            string assetPath = $"{folder}/{profileName}.asset";

            // Check for existing profile at path
            if (AssetDatabase.AssetPathToGUID(assetPath) != string.Empty)
                return ActionResult.Fail($"A profile already exists at: {assetPath}");

            // Create the BuildProfile instance via ScriptableObject + SerializedObject
            try
            {
                var profile = ScriptableObject.CreateInstance<BuildProfile>();
                var so = new SerializedObject(profile);

                // Try to set build target via serialized property (name varies by Unity version)
                foreach (string propName in new[] { "m_BuildTarget", "buildTarget", "m_Platform" })
                {
                    var prop = so.FindProperty(propName);
                    if (prop != null)
                    {
                        prop.intValue = (int)target;
                        break;
                    }
                }
                so.ApplyModifiedPropertiesWithoutUndo();

                AssetDatabase.CreateAsset(profile, assetPath);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                Debug.Log($"[AgentBridge] Created Build Profile: {assetPath}");
                return ActionResult.Ok($"Created Build Profile '{profileName}' for {target} at {assetPath}", profile);
            }
            catch (Exception ex)
            {
                return ActionResult.Fail($"Failed to create Build Profile: {ex.Message}");
            }
        }

        // ─────────────────────────────────────────────────────
        //  Menu Items
        // ─────────────────────────────────────────────────────

        [MenuItem("Axiom/AgentBridge/Actions/List Build Profiles")]
        public static void MenuListProfiles()
        {
            Debug.Log($"[AgentBridge] {ListProfiles().Message}");
        }

        [MenuItem("Axiom/AgentBridge/Actions/Show Active Build Profile")]
        public static void MenuShowActiveProfile()
        {
            Debug.Log($"[AgentBridge] {GetActiveProfile().Message}");
        }
    }
}

#else

namespace Axiom.Editor.AgentBridge.Actions
{
    using Axiom.Editor.AgentBridge.Core;

    public static class BuildProfileActions
    {
        public static ActionResult ListProfiles()
            => ActionResult.Fail("Build Profiles require Unity 6 (6000.0+).");
        public static ActionResult GetActiveProfile()
            => ActionResult.Fail("Build Profiles require Unity 6.");
        public static ActionResult SetActiveProfile(string p)
            => ActionResult.Fail("Build Profiles require Unity 6.");
        public static ActionResult ModifyProfileDefines(
            string p, string[] a = null, string[] r = null)
            => ActionResult.Fail("Build Profiles require Unity 6.");
        public static ActionResult DiffProfiles(string a, string b)
            => ActionResult.Fail("Build Profiles require Unity 6.");
        public static ActionResult ModifyProfileProperty(string p, string pp, string v)
            => ActionResult.Fail("Build Profiles require Unity 6.");
        public static ActionResult ModifyBuildSceneList(
            string[] a = null, string[] r = null, string[] s = null)
            => ActionResult.Fail("Build Profiles require Unity 6.");
        public static ActionResult TriggerBuild(
            string o, string p = null,
            UnityEditor.BuildOptions opts = UnityEditor.BuildOptions.None, bool d = false)
            => ActionResult.Fail("Build Profiles require Unity 6.");
        public static ActionResult AnalyzeBuildReport()
            => ActionResult.Fail("Build Profiles require Unity 6.");
        public static ActionResult CreateProfile(string n, string t, string s = null)
            => ActionResult.Fail("Build Profiles require Unity 6.");
    }
}

#endif
