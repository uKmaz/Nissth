using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor;
using Axiom.Editor.AgentBridge.Core;

namespace Axiom.Editor.AgentBridge.Diagnostics
{
    /// <summary>
    /// Reports on all project-level settings. This is what the agent reads before
    /// configuring builds, changing quality levels, or modifying project-wide behavior.
    /// </summary>
    public static class SettingsReporter
    {
        public enum SettingsReporterMode
        {
            QuickSummary,       // Mode A
            PlayerSettingsDump, // Mode B
            QualityLevels,      // Mode C
            TagsAndLayers,      // Mode D
            TimeAndPhysics,     // Mode E
            EditorSettings,     // Mode F
            InputSystem         // Mode G
        }

        /// <summary>
        /// Generates a project settings report.
        /// </summary>
        /// <param name="mode">Which settings to report.</param>
        /// <returns>File path of the generated report.</returns>
        public static string GenerateReport(SettingsReporterMode mode)
        {
            var sb = new StringBuilder();
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

            switch (mode)
            {
                case SettingsReporterMode.QuickSummary:
                    BuildQuickSummary(sb, timestamp);
                    break;
                case SettingsReporterMode.PlayerSettingsDump:
                    BuildPlayerSettingsDump(sb, timestamp);
                    break;
                case SettingsReporterMode.QualityLevels:
                    BuildQualityLevels(sb, timestamp);
                    break;
                case SettingsReporterMode.TagsAndLayers:
                    BuildTagsAndLayers(sb, timestamp);
                    break;
                case SettingsReporterMode.TimeAndPhysics:
                    BuildTimeAndPhysics(sb, timestamp);
                    break;
                case SettingsReporterMode.EditorSettings:
                    BuildEditorSettings(sb, timestamp);
                    break;
                case SettingsReporterMode.InputSystem:
                    BuildInputSystem(sb, timestamp);
                    break;
            }

            string reportName = $"settings_reporter_{mode.ToString().ToLower()}_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.md";
            return OutputWriter.WriteReport(reportName, sb.ToString());
        }

        // ─── Mode A: Quick Summary ────────────────────────────────────────────────

        private static void BuildQuickSummary(StringBuilder sb, string timestamp)
        {
            sb.AppendLine("# Settings Reporter — Mode: Quick Summary");
            sb.AppendLine();

            var buildTarget = EditorUserBuildSettings.activeBuildTarget;
            var buildTargetGroup = EditorUserBuildSettings.selectedBuildTargetGroup;

            string pipelineName = "(Legacy Built-in)";
            var rp = GraphicsSettings.currentRenderPipeline;
            if (rp != null)
                pipelineName = $"{rp.name} ({rp.GetType().Name})";

            string activeQualityName = "(none)";
            int activeQualityLevel = QualitySettings.GetQualityLevel();
            string[] qualityNames = QualitySettings.names;
            if (qualityNames != null && activeQualityLevel < qualityNames.Length)
                activeQualityName = qualityNames[activeQualityLevel];

#pragma warning disable CS0618
            string scriptingBackend = PlayerSettings.GetScriptingBackend(buildTargetGroup).ToString();
            string apiCompat = PlayerSettings.GetApiCompatibilityLevel(buildTargetGroup).ToString();
#pragma warning restore CS0618

            string[] defines = EditorUserBuildSettings.activeScriptCompilationDefines;
            string definesStr = defines != null && defines.Length > 0
                ? string.Join("; ", defines)
                : "(none)";

            sb.AppendLine("| Setting | Value |");
            sb.AppendLine("| :--- | :--- |");
            sb.AppendLine($"| Unity Version | {Application.unityVersion} |");
            sb.AppendLine($"| Product Name | {PlayerSettings.productName} |");
            sb.AppendLine($"| Company | {PlayerSettings.companyName} |");
            sb.AppendLine($"| Bundle Version | {PlayerSettings.bundleVersion} |");
            sb.AppendLine($"| Active Platform | {buildTarget} |");
            sb.AppendLine($"| Render Pipeline | {pipelineName} |");
            sb.AppendLine($"| Color Space | {PlayerSettings.colorSpace} |");
            sb.AppendLine($"| Quality Level | {activeQualityName} ({activeQualityLevel}) |");
            sb.AppendLine($"| Scripting Backend | {scriptingBackend} |");
            sb.AppendLine($"| API Compatibility | {apiCompat} |");
            sb.AppendLine($"| Scripting Defines | {definesStr} |");
            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine($"Generated: {timestamp}");
        }

        // ─── Mode B: Player Settings Dump ────────────────────────────────────────

        private static void BuildPlayerSettingsDump(StringBuilder sb, string timestamp)
        {
            sb.AppendLine("# Settings Reporter — Mode: Player Settings Dump");
            sb.AppendLine();

            var buildTargetGroup = EditorUserBuildSettings.selectedBuildTargetGroup;

            // Identification
            sb.AppendLine("## Identification");
            sb.AppendLine("| Setting | Value |");
            sb.AppendLine("| :--- | :--- |");
            sb.AppendLine($"| Product Name | {PlayerSettings.productName} |");
            sb.AppendLine($"| Company Name | {PlayerSettings.companyName} |");
            sb.AppendLine($"| Bundle Version | {PlayerSettings.bundleVersion} |");
            sb.AppendLine($"| Application Identifier | {PlayerSettings.applicationIdentifier} |");
            sb.AppendLine();

            // Resolution & Presentation
            sb.AppendLine("## Resolution & Presentation");
            sb.AppendLine("| Setting | Value |");
            sb.AppendLine("| :--- | :--- |");
            sb.AppendLine($"| Default Screen Width | {PlayerSettings.defaultScreenWidth} |");
            sb.AppendLine($"| Default Screen Height | {PlayerSettings.defaultScreenHeight} |");
            sb.AppendLine($"| Full Screen Mode | {PlayerSettings.fullScreenMode} |");
            sb.AppendLine($"| Default Is Native Resolution | {PlayerSettings.defaultIsNativeResolution} |");
            sb.AppendLine($"| Run In Background | {PlayerSettings.runInBackground} |");
            sb.AppendLine($"| Visible In Background | {PlayerSettings.visibleInBackground} |");
            sb.AppendLine();

            // Splash Screen
            sb.AppendLine("## Splash Screen");
            sb.AppendLine("| Setting | Value |");
            sb.AppendLine("| :--- | :--- |");
            sb.AppendLine($"| Show Unity Splash Screen | {PlayerSettings.SplashScreen.show} |");
            sb.AppendLine($"| Splash Screen Style | {PlayerSettings.SplashScreen.unityLogoStyle} |");
            sb.AppendLine();

            // Icon
            sb.AppendLine("## Icon");
            sb.AppendLine("| Setting | Value |");
            sb.AppendLine("| :--- | :--- |");
            var icons = PlayerSettings.GetIconsForTargetGroup(buildTargetGroup);
            bool hasIcon = icons != null && icons.Length > 0 && icons[0] != null;
            sb.AppendLine($"| Default Icon Set | {(hasIcon ? "Yes" : "No")} |");
            sb.AppendLine();

            // Rendering
            sb.AppendLine("## Rendering");
            sb.AppendLine("| Setting | Value |");
            sb.AppendLine("| :--- | :--- |");
            sb.AppendLine($"| Color Space | {PlayerSettings.colorSpace} |");
            sb.AppendLine($"| Use 32-Bit Display Buffer | {PlayerSettings.use32BitDisplayBuffer} |");
            sb.AppendLine($"| GPU Skinning | {PlayerSettings.gpuSkinning} |");
            sb.AppendLine($"| Graphics Jobs | {PlayerSettings.graphicsJobs} |");
            sb.AppendLine($"| Graphics Job Mode | {PlayerSettings.graphicsJobMode} |");
            sb.AppendLine();

            // Scripting
            sb.AppendLine("## Scripting");
            sb.AppendLine("| Setting | Value |");
            sb.AppendLine("| :--- | :--- |");
#pragma warning disable CS0618
            sb.AppendLine($"| Scripting Backend | {PlayerSettings.GetScriptingBackend(buildTargetGroup)} |");
            sb.AppendLine($"| API Compatibility Level | {PlayerSettings.GetApiCompatibilityLevel(buildTargetGroup)} |");
#pragma warning restore CS0618
            var defines = PlayerSettings.GetScriptingDefineSymbolsForGroup(buildTargetGroup);
            sb.AppendLine($"| Scripting Define Symbols | {(string.IsNullOrEmpty(defines) ? "(none)" : defines)} |");
            sb.AppendLine($"| Allow Unsafe Code | {PlayerSettings.allowUnsafeCode} |");
            sb.AppendLine($"| GC Incremental | {PlayerSettings.gcIncremental} |");
            sb.AppendLine();

            // Optimization
            sb.AppendLine("## Optimization");
            sb.AppendLine("| Setting | Value |");
            sb.AppendLine("| :--- | :--- |");
            sb.AppendLine($"| Strip Engine Code | {PlayerSettings.stripEngineCode} |");
            sb.AppendLine($"| Managed Stripping Level | {PlayerSettings.GetManagedStrippingLevel(buildTargetGroup)} |");
            sb.AppendLine($"| Strip Unused Mesh Components | {PlayerSettings.stripUnusedMeshComponents} |");
            sb.AppendLine();

            sb.AppendLine("---");
            sb.AppendLine($"Generated: {timestamp}");
        }

        // ─── Mode C: Quality Levels ───────────────────────────────────────────────

        private static void BuildQualityLevels(StringBuilder sb, string timestamp)
        {
            string[] names = QualitySettings.names;
            int currentLevel = QualitySettings.GetQualityLevel();

            string activeQualityName = currentLevel < names.Length ? names[currentLevel] : "Unknown";
            sb.AppendLine($"# Settings Reporter — Mode: Quality Levels | Active: {activeQualityName} ({currentLevel})");
            sb.AppendLine();

            // Build header
            sb.Append("| Setting |");
            foreach (var n in names)
                sb.Append($" {n} |");
            sb.AppendLine();

            sb.Append("| :--- |");
            foreach (var _ in names)
                sb.Append(" :--- |");
            sb.AppendLine();

            // Capture data per level
            var rows = new List<(string label, string[] values)>
            {
                ("Pixel Light Count", new string[names.Length]),
                ("Shadows", new string[names.Length]),
                ("Shadow Resolution", new string[names.Length]),
                ("Shadow Distance", new string[names.Length]),
                ("Anti Aliasing", new string[names.Length]),
                ("VSync Count", new string[names.Length]),
                ("LOD Bias", new string[names.Length]),
                ("Max LOD Level", new string[names.Length]),
                ("Particle Raycast Budget", new string[names.Length]),
                ("Async Upload Time Slice", new string[names.Length]),
                ("Async Upload Buffer Size", new string[names.Length]),
            };

            for (int i = 0; i < names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);

                rows[0].values[i] = QualitySettings.pixelLightCount.ToString();
                rows[1].values[i] = QualitySettings.shadows.ToString();
                rows[2].values[i] = QualitySettings.shadowResolution.ToString();
                rows[3].values[i] = QualitySettings.shadowDistance.ToString("F1");
                rows[4].values[i] = QualitySettings.antiAliasing.ToString();
                rows[5].values[i] = QualitySettings.vSyncCount.ToString();
                rows[6].values[i] = QualitySettings.lodBias.ToString("F2");
                rows[7].values[i] = QualitySettings.maximumLODLevel.ToString();
                rows[8].values[i] = QualitySettings.particleRaycastBudget.ToString();
                rows[9].values[i] = QualitySettings.asyncUploadTimeSlice.ToString();
                rows[10].values[i] = QualitySettings.asyncUploadBufferSize.ToString();
            }

            // CRITICAL: Restore original quality level
            QualitySettings.SetQualityLevel(currentLevel, false);

            foreach (var row in rows)
            {
                sb.Append($"| {row.label} |");
                foreach (var v in row.values)
                    sb.Append($" {v} |");
                sb.AppendLine();
            }

            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine($"Quality levels: {names.Length} | Active: {activeQualityName} ({currentLevel}) | Generated: {timestamp}");
        }

        // ─── Mode D: Tags and Layers ──────────────────────────────────────────────

        private static void BuildTagsAndLayers(StringBuilder sb, string timestamp)
        {
            sb.AppendLine("# Settings Reporter — Mode: Tags & Layers");
            sb.AppendLine();

            // Tags
            sb.AppendLine("## Tags");
            sb.AppendLine("| # | Tag Name |");
            sb.AppendLine("| :--- | :--- |");
            var tags = UnityEditorInternal.InternalEditorUtility.tags;
            for (int i = 0; i < tags.Length; i++)
                sb.AppendLine($"| {i + 1} | {tags[i]} |");
            sb.AppendLine();

            // Layers
            sb.AppendLine("## Layers");
            sb.AppendLine("| Index | Layer Name |");
            sb.AppendLine("| :--- | :--- |");
            var layerList = new List<(int index, string name)>();
            for (int i = 0; i < 32; i++)
            {
                string layerName = LayerMask.LayerToName(i);
                if (!string.IsNullOrEmpty(layerName))
                {
                    layerList.Add((i, layerName));
                    sb.AppendLine($"| {i} | {layerName} |");
                }
            }
            sb.AppendLine();

            // Sorting Layers
            sb.AppendLine("## Sorting Layers");
            sb.AppendLine("| # | Name | Unique ID |");
            sb.AppendLine("| :--- | :--- | :--- |");
            var sortingLayers = SortingLayer.layers;
            for (int i = 0; i < sortingLayers.Length; i++)
                sb.AppendLine($"| {i + 1} | {sortingLayers[i].name} | {sortingLayers[i].id} |");
            sb.AppendLine();

            sb.AppendLine("---");
            sb.AppendLine($"Tags: {tags.Length} | Layers: {layerList.Count} | Sorting Layers: {sortingLayers.Length} | Generated: {timestamp}");
        }

        // ─── Mode E: Time and Physics ─────────────────────────────────────────────

        private static void BuildTimeAndPhysics(StringBuilder sb, string timestamp)
        {
            sb.AppendLine("# Settings Reporter — Mode: Time & Physics");
            sb.AppendLine();

            // Time Settings
            sb.AppendLine("## Time Settings");
            sb.AppendLine("| Setting | Value |");
            sb.AppendLine("| :--- | :--- |");
            float fps = Time.fixedDeltaTime > 0 ? 1f / Time.fixedDeltaTime : 0f;
            sb.AppendLine($"| Fixed Timestep | {Time.fixedDeltaTime:F4} ({fps:F0} Hz) |");
            sb.AppendLine($"| Max Allowed Timestep | {Time.maximumDeltaTime:F4} |");
            sb.AppendLine($"| Time Scale | {Time.timeScale:F1} |");
            sb.AppendLine($"| Max Particle Timestep | {Time.maximumParticleDeltaTime:F4} |");
            sb.AppendLine();

            // Physics 3D
            sb.AppendLine("## Physics 3D");
            sb.AppendLine("| Setting | Value |");
            sb.AppendLine("| :--- | :--- |");
            sb.AppendLine($"| Gravity | ({Physics.gravity.x:F2}, {Physics.gravity.y:F2}, {Physics.gravity.z:F2}) |");
            sb.AppendLine($"| Default Solver Iterations | {Physics.defaultSolverIterations} |");
            sb.AppendLine($"| Default Solver Velocity Iterations | {Physics.defaultSolverVelocityIterations} |");
            sb.AppendLine($"| Bounce Threshold | {Physics.bounceThreshold:F1} |");
            sb.AppendLine($"| Default Contact Offset | {Physics.defaultContactOffset:F4} |");
            sb.AppendLine($"| Sleep Threshold | {Physics.sleepThreshold:F4} |");
            sb.AppendLine($"| Default Max Angular Speed | {Physics.defaultMaxAngularSpeed:F1} |");
            sb.AppendLine($"| Simulation Mode | {Physics.simulationMode} |");
            sb.AppendLine();

            // Collision matrix — only list non-default (disabled) pairs
            var disabledPairs = new List<string>();
            for (int a = 0; a < 32; a++)
            {
                string layerA = LayerMask.LayerToName(a);
                if (string.IsNullOrEmpty(layerA)) continue;
                for (int b = a; b < 32; b++)
                {
                    string layerB = LayerMask.LayerToName(b);
                    if (string.IsNullOrEmpty(layerB)) continue;
                    if (Physics.GetIgnoreLayerCollision(a, b))
                        disabledPairs.Add($"{layerA} ↔ {layerB}");
                }
            }

            if (disabledPairs.Count == 0)
            {
                sb.AppendLine("*Collision matrix: all layers collide (default).*");
            }
            else
            {
                sb.AppendLine("## Disabled Layer Collisions");
                foreach (var pair in disabledPairs)
                    sb.AppendLine($"- {pair}");
            }
            sb.AppendLine();

            // Physics 2D
            sb.AppendLine("## Physics 2D");
            sb.AppendLine("| Setting | Value |");
            sb.AppendLine("| :--- | :--- |");
            sb.AppendLine($"| Gravity | ({Physics2D.gravity.x:F2}, {Physics2D.gravity.y:F2}) |");
            sb.AppendLine($"| Default Contact Offset | {Physics2D.defaultContactOffset:F4} |");
            sb.AppendLine($"| Velocity Iterations | {Physics2D.velocityIterations} |");
            sb.AppendLine($"| Position Iterations | {Physics2D.positionIterations} |");
            sb.AppendLine();

            sb.AppendLine("---");
            sb.AppendLine($"Generated: {timestamp}");
        }

        // ─── Mode F: Editor Settings ──────────────────────────────────────────────

        private static void BuildEditorSettings(StringBuilder sb, string timestamp)
        {
            sb.AppendLine("# Settings Reporter — Mode: Editor Settings");
            sb.AppendLine();

            sb.AppendLine("| Setting | Value |");
            sb.AppendLine("| :--- | :--- |");
            sb.AppendLine($"| Serialization Mode | {UnityEditor.EditorSettings.serializationMode} |");
            sb.AppendLine($"| Version Control | {UnityEditor.EditorSettings.externalVersionControl} |");
            sb.AppendLine($"| Default Behavior | {UnityEditor.EditorSettings.defaultBehaviorMode} |");
            sb.AppendLine($"| Sprite Packer Mode | {UnityEditor.EditorSettings.spritePackerMode} |");

            bool eplEnabled = UnityEditor.EditorSettings.enterPlayModeOptionsEnabled;
            string eplValue = eplEnabled
                ? $"Enabled ({UnityEditor.EditorSettings.enterPlayModeOptions})"
                : "Disabled";
            sb.AppendLine($"| Enter Play Mode Options | {eplValue} |");
            sb.AppendLine($"| Asset Pipeline Mode | {UnityEditor.EditorSettings.assetPipelineMode} |");
            sb.AppendLine($"| Cache Server Mode | {UnityEditor.EditorSettings.cacheServerMode} |");

            string prefabRegEnv = UnityEditor.EditorSettings.prefabRegularEnvironment != null
                ? UnityEditor.EditorSettings.prefabRegularEnvironment.name
                : "(none)";
            string prefabUiEnv = UnityEditor.EditorSettings.prefabUIEnvironment != null
                ? UnityEditor.EditorSettings.prefabUIEnvironment.name
                : "(none)";
            sb.AppendLine($"| Prefab Regular Environment | {prefabRegEnv} |");
            sb.AppendLine($"| Prefab UI Environment | {prefabUiEnv} |");
            sb.AppendLine();

            sb.AppendLine("---");
            sb.AppendLine($"Generated: {timestamp}");
        }

        // ─── Mode G: Input System ─────────────────────────────────────────────────

        private static void BuildInputSystem(StringBuilder sb, string timestamp)
        {
            sb.AppendLine("# Settings Reporter — Mode: Input System");
            sb.AppendLine();

            // Check if Input System package is available via reflection
            Type inputActionAssetType = Type.GetType("UnityEngine.InputSystem.InputActionAsset, Unity.InputSystem");
            if (inputActionAssetType == null)
            {
                sb.AppendLine("**Input System package not detected. Using legacy Input Manager.**");
                sb.AppendLine();
                sb.AppendLine("*Legacy Input Manager axes are configured in ProjectSettings/InputManager.asset.*");
                sb.AppendLine("*Consider migrating to the Input System package for better agent compatibility.*");
                sb.AppendLine();
                sb.AppendLine("---");
                sb.AppendLine($"Generated: {timestamp}");
                return;
            }

            // Input System is available — find all InputActionAsset files
            string[] guids = AssetDatabase.FindAssets("t:InputActionAsset");
            if (guids.Length == 0)
            {
                sb.AppendLine("*Input System package is installed, but no InputActionAsset files found.*");
                sb.AppendLine();
                sb.AppendLine("---");
                sb.AppendLine($"Generated: {timestamp}");
                return;
            }

            int totalActionMaps = 0;
            int totalActions = 0;

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath(path, inputActionAssetType);
                if (asset == null) continue;

                sb.AppendLine($"## {System.IO.Path.GetFileName(path)}");
                sb.AppendLine($"*Path: {path}*");
                sb.AppendLine();

                // Use reflection to iterate action maps
                var actionMapsProperty = inputActionAssetType.GetProperty("actionMaps");
                if (actionMapsProperty == null) continue;

                var actionMaps = actionMapsProperty.GetValue(asset) as System.Collections.IEnumerable;
                if (actionMaps == null) continue;

                foreach (var map in actionMaps)
                {
                    totalActionMaps++;
                    var mapNameProp = map.GetType().GetProperty("name");
                    string mapName = mapNameProp?.GetValue(map)?.ToString() ?? "(unnamed)";

                    sb.AppendLine($"### Action Map: {mapName}");
                    sb.AppendLine("| Action | Type | Bindings |");
                    sb.AppendLine("| :--- | :--- | :--- |");

                    var actionsProperty = map.GetType().GetProperty("actions");
                    var actions = actionsProperty?.GetValue(map) as System.Collections.IEnumerable;
                    if (actions == null) continue;

                    foreach (var action in actions)
                    {
                        totalActions++;
                        var actionType = action.GetType();
                        string actionName = actionType.GetProperty("name")?.GetValue(action)?.ToString() ?? "(unnamed)";
                        string actionTypeName = actionType.GetProperty("type")?.GetValue(action)?.ToString() ?? "?";

                        // Collect bindings
                        var bindingsProperty = actionType.GetProperty("bindings");
                        var bindings = bindingsProperty?.GetValue(action) as System.Collections.IEnumerable;
                        var bindingPaths = new List<string>();
                        if (bindings != null)
                        {
                            foreach (var binding in bindings)
                            {
                                var bindingTypeProp = binding.GetType();
                                bool isComposite = (bool)(bindingTypeProp.GetProperty("isComposite")?.GetValue(binding) ?? false);
                                bool isPartOfComposite = (bool)(bindingTypeProp.GetProperty("isPartOfComposite")?.GetValue(binding) ?? false);
                                string effectivePath = bindingTypeProp.GetProperty("effectivePath")?.GetValue(binding)?.ToString() ?? "";
                                if (!string.IsNullOrEmpty(effectivePath) && !isComposite)
                                    bindingPaths.Add(effectivePath);
                            }
                        }

                        string bindingsStr = bindingPaths.Count > 0 ? string.Join(", ", bindingPaths) : "(none)";
                        sb.AppendLine($"| {actionName} | {actionTypeName} | {bindingsStr} |");
                    }
                    sb.AppendLine();
                }
            }

            sb.AppendLine("---");
            sb.AppendLine($"Input Action Assets: {guids.Length} | Action Maps: {totalActionMaps} | Total Actions: {totalActions} | Generated: {timestamp}");
        }

        // ─── Menu Items ───────────────────────────────────────────────────────────

        [MenuItem("Axiom/AgentBridge/Settings Reporter — Mode A (Quick Summary)")]
        public static void MenuModeA()
        {
            string path = GenerateReport(SettingsReporterMode.QuickSummary);
            Debug.Log($"[AgentBridge] Settings Reporter report: {path}");
        }

        [MenuItem("Axiom/AgentBridge/Settings Reporter — Mode B (Player Settings)")]
        public static void MenuModeB()
        {
            string path = GenerateReport(SettingsReporterMode.PlayerSettingsDump);
            Debug.Log($"[AgentBridge] Settings Reporter report: {path}");
        }

        [MenuItem("Axiom/AgentBridge/Settings Reporter — Mode C (Quality Levels)")]
        public static void MenuModeC()
        {
            string path = GenerateReport(SettingsReporterMode.QualityLevels);
            Debug.Log($"[AgentBridge] Settings Reporter report: {path}");
        }

        [MenuItem("Axiom/AgentBridge/Settings Reporter — Mode D (Tags & Layers)")]
        public static void MenuModeD()
        {
            string path = GenerateReport(SettingsReporterMode.TagsAndLayers);
            Debug.Log($"[AgentBridge] Settings Reporter report: {path}");
        }

        [MenuItem("Axiom/AgentBridge/Settings Reporter — Mode E (Time & Physics)")]
        public static void MenuModeE()
        {
            string path = GenerateReport(SettingsReporterMode.TimeAndPhysics);
            Debug.Log($"[AgentBridge] Settings Reporter report: {path}");
        }

        [MenuItem("Axiom/AgentBridge/Settings Reporter — Mode F (Editor Settings)")]
        public static void MenuModeF()
        {
            string path = GenerateReport(SettingsReporterMode.EditorSettings);
            Debug.Log($"[AgentBridge] Settings Reporter report: {path}");
        }

        [MenuItem("Axiom/AgentBridge/Settings Reporter — Mode G (Input System)")]
        public static void MenuModeG()
        {
            string path = GenerateReport(SettingsReporterMode.InputSystem);
            Debug.Log($"[AgentBridge] Settings Reporter report: {path}");
        }
    }
}
