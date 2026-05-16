using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor;
using Axiom.Editor.AgentBridge.Core;

namespace Axiom.Editor.AgentBridge.Actions
{
    /// <summary>
    /// Gives the agent control over Unity 6 GPU-driven rendering —
    /// configuring the Render Pipeline Asset (URP/HDRP), GPU Resident Drawer,
    /// shadows, cameras, and lights.
    /// </summary>
    public static class RenderActions
    {
        // ─────────────────────────────────────────────────────
        //  Shared Helper
        // ─────────────────────────────────────────────────────

        private static int FindQualityLevelIndex(string levelName)
        {
            string[] names = QualitySettings.names;
            for (int i = 0; i < names.Length; i++)
                if (names[i].Equals(levelName, StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }

        // ─────────────────────────────────────────────────────
        //  3.1 GetActiveRenderPipeline
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// Returns information about the currently active render pipeline.
        /// Not an action — a helper for agent orientation and used by other methods.
        /// </summary>
        public static ActionResult GetActiveRenderPipeline()
        {
            var currentRP = QualitySettings.renderPipeline ?? GraphicsSettings.defaultRenderPipeline;
            if (currentRP == null)
                return ActionResult.Ok("Pipeline: Built-in Render Pipeline (no SRP active)");

            string typeName = currentRP.GetType().Name;
            string assetPath = AssetDatabase.GetAssetPath(currentRP);
            return ActionResult.Ok(
                $"Pipeline: {typeName}\nAsset: {assetPath}\nQuality Override: {(QualitySettings.renderPipeline != null ? "Yes" : "No")}",
                currentRP);
        }

        // ─────────────────────────────────────────────────────
        //  3.2 ModifyRenderPipelineAsset
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// Modifies a property on the active Render Pipeline Asset.
        /// </summary>
        /// <param name="propertyPath">SerializedProperty path (e.g., "m_ShadowDistance").</param>
        /// <param name="value">Value as string — uses PropertyValueParser.</param>
        /// <param name="qualityLevel">Target quality level. Null = default pipeline asset.</param>
        public static ActionResult ModifyRenderPipelineAsset(
            string propertyPath, string value, string qualityLevel = null)
        {
            RenderPipelineAsset pipelineAsset;
            if (qualityLevel != null)
            {
                int idx = FindQualityLevelIndex(qualityLevel);
                if (idx < 0)
                    return ActionResult.Fail($"Quality level not found: {qualityLevel}. Available: {string.Join(", ", QualitySettings.names)}");
                pipelineAsset = QualitySettings.GetRenderPipelineAssetAt(idx);
                if (pipelineAsset == null)
                    return ActionResult.Fail($"No pipeline asset on quality level: {qualityLevel}");
            }
            else
            {
                pipelineAsset = GraphicsSettings.defaultRenderPipeline;
                if (pipelineAsset == null)
                    return ActionResult.Fail("No default render pipeline asset configured");
            }

            var so = new SerializedObject(pipelineAsset);
            var prop = so.FindProperty(propertyPath);
            if (prop == null)
                return ActionResult.Fail($"Property not found on {pipelineAsset.GetType().Name}: {propertyPath}");

            Undo.RecordObject(pipelineAsset, $"Modify RenderPipeline {propertyPath}");
            PropertyValueParser.SetPropertyValue(prop, value);
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(pipelineAsset);

            string target = qualityLevel != null ? $" (quality: {qualityLevel})" : " (default)";
            Debug.Log($"[AgentBridge] Set {pipelineAsset.GetType().Name}.{propertyPath} = {value}{target}");
            return ActionResult.Ok($"Set {propertyPath} = {value}{target}", pipelineAsset);
        }

        // ─────────────────────────────────────────────────────
        //  3.3 BatchModifyRenderPipeline
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// Modifies multiple properties on the pipeline asset in one Undo group.
        /// </summary>
        public static ActionResult BatchModifyRenderPipeline(
            Dictionary<string, string> properties, string qualityLevel = null)
        {
            RenderPipelineAsset pipelineAsset;
            if (qualityLevel != null)
            {
                int idx = FindQualityLevelIndex(qualityLevel);
                if (idx < 0) return ActionResult.Fail($"Quality level not found: {qualityLevel}");
                pipelineAsset = QualitySettings.GetRenderPipelineAssetAt(idx);
            }
            else
            {
                pipelineAsset = GraphicsSettings.defaultRenderPipeline;
            }
            if (pipelineAsset == null) return ActionResult.Fail("No render pipeline asset found");

            Undo.SetCurrentGroupName("BatchModify RenderPipeline");
            Undo.RecordObject(pipelineAsset, "BatchModify RenderPipeline");
            var so = new SerializedObject(pipelineAsset);
            int successCount = 0, failCount = 0;
            var errors = new List<string>();

            foreach (var kvp in properties)
            {
                var prop = so.FindProperty(kvp.Key);
                if (prop == null)
                {
                    failCount++;
                    errors.Add($"{kvp.Key}: not found");
                    continue;
                }
                try
                {
                    PropertyValueParser.SetPropertyValue(prop, kvp.Value);
                    successCount++;
                }
                catch (Exception ex)
                {
                    failCount++;
                    errors.Add($"{kvp.Key}: {ex.Message}");
                }
            }

            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(pipelineAsset);

            string summary = $"Set {successCount}/{properties.Count} properties. Failures: {failCount}.";
            if (errors.Count > 0) summary += $"\nErrors:\n{string.Join("\n", errors)}";
            return failCount == 0 ? ActionResult.Ok(summary, pipelineAsset) : ActionResult.Fail(summary);
        }

        // ─────────────────────────────────────────────────────
        //  3.4 ListRenderPipelineProperties
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// Lists all SerializedProperty paths on the active pipeline asset. Writes report.
        /// </summary>
        public static ActionResult ListRenderPipelineProperties(string qualityLevel = null)
        {
            RenderPipelineAsset pipelineAsset;
            if (qualityLevel != null)
            {
                int idx = FindQualityLevelIndex(qualityLevel);
                if (idx < 0) return ActionResult.Fail($"Quality level not found: {qualityLevel}");
                pipelineAsset = QualitySettings.GetRenderPipelineAssetAt(idx);
            }
            else
            {
                pipelineAsset = GraphicsSettings.defaultRenderPipeline;
            }
            if (pipelineAsset == null) return ActionResult.Fail("No render pipeline asset found");

            var so = new SerializedObject(pipelineAsset);
            var sb = new StringBuilder();
            sb.AppendLine($"# Render Pipeline Properties — {pipelineAsset.GetType().Name}");
            sb.AppendLine($"Asset: {AssetDatabase.GetAssetPath(pipelineAsset)}\n");
            sb.AppendLine("| Property Path | Type | Value |");
            sb.AppendLine("| :--- | :--- | :--- |");

            var iter = so.GetIterator();
            iter.Next(true);
            do
            {
                if (iter.propertyPath == "m_Script") continue;
                string val;
                try { val = PropertyValueParser.GetValueString(iter); }
                catch { val = "(complex)"; }
                sb.AppendLine($"| {iter.propertyPath} | {iter.propertyType} | {val} |");
            }
            while (iter.NextVisible(false));

            string reportPath = OutputWriter.WriteReport("render_pipeline_properties", sb.ToString());
            return ActionResult.Ok($"Property list: {reportPath}", pipelineAsset);
        }

        // ─────────────────────────────────────────────────────
        //  3.5 ConfigureGPUResidentDrawer
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// Configures the GPU Resident Drawer on the active URP pipeline asset.
        /// </summary>
        /// <param name="enabled">True to enable GPU Resident Drawer.</param>
        /// <param name="qualityLevel">Target quality level. Null = default.</param>
        public static ActionResult ConfigureGPUResidentDrawer(bool enabled, string qualityLevel = null)
        {
            // Property path varies by URP version.
            // URP 17+ (Unity 6): m_GPUResidentDrawerMode — 0=Disabled, 1=Instanced
            string propertyPath = "m_GPUResidentDrawerMode";
            string value = enabled ? "1" : "0";
            var result = ModifyRenderPipelineAsset(propertyPath, value, qualityLevel);

            if (!result.Success)
            {
                // Try alternative path for different URP versions
                propertyPath = "m_UseGPUResidentDrawer";
                value = enabled.ToString();
                result = ModifyRenderPipelineAsset(propertyPath, value, qualityLevel);
            }

            if (!result.Success)
                return ActionResult.Fail(
                    $"GPU Resident Drawer property not found. " +
                    $"Use ListRenderPipelineProperties() to discover available paths. " +
                    $"Error: {result.Message}");

            string state = enabled ? "enabled" : "disabled";
            return ActionResult.Ok(
                $"GPU Resident Drawer {state}" +
                $"{(qualityLevel != null ? $" (quality: {qualityLevel})" : "")}");
        }

        // ─────────────────────────────────────────────────────
        //  3.6 SetShadowSettings
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// Configures shadow settings on the active pipeline asset.
        /// </summary>
        public static ActionResult SetShadowSettings(
            float? shadowDistance = null,
            int? shadowCascadeCount = null,
            int? shadowResolution = null,
            string qualityLevel = null)
        {
            var props = new Dictionary<string, string>();
            // Property paths are URP defaults. HDRP uses different paths.
            // Agent can discover via ListRenderPipelineProperties.
            if (shadowDistance.HasValue)
                props["m_ShadowDistance"] = shadowDistance.Value.ToString(CultureInfo.InvariantCulture);
            if (shadowCascadeCount.HasValue)
                props["m_ShadowCascadeCount"] = shadowCascadeCount.Value.ToString();
            if (shadowResolution.HasValue)
                props["m_MainLightShadowmapResolution"] = shadowResolution.Value.ToString();

            if (props.Count == 0)
                return ActionResult.Fail("No shadow settings specified");

            return BatchModifyRenderPipeline(props, qualityLevel);
        }

        // ─────────────────────────────────────────────────────
        //  3.7 SetCameraSettings
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// Modifies settings on a Camera component via SerializedObject.
        /// </summary>
        /// <param name="cameraPath">Breadcrumb path to the camera GameObject.</param>
        /// <param name="properties">Camera property paths and values.</param>
        public static ActionResult SetCameraSettings(
            string cameraPath, Dictionary<string, string> properties)
        {
            if (!PathResolver.ResolveRootPath(cameraPath, out var targets) || targets.Count == 0)
                return ActionResult.Fail($"GameObject not found: {cameraPath}");
            var go = targets[0];
            var camera = go.GetComponent<Camera>();
            if (camera == null) return ActionResult.Fail($"No Camera component on: {cameraPath}");

            Undo.RecordObject(camera, "SetCameraSettings");
            var so = new SerializedObject(camera);
            int successCount = 0;

            foreach (var kvp in properties)
            {
                var prop = so.FindProperty(kvp.Key);
                if (prop != null)
                {
                    PropertyValueParser.SetPropertyValue(prop, kvp.Value);
                    successCount++;
                }
                else
                {
                    Debug.LogWarning($"[AgentBridge] Camera property not found: {kvp.Key}");
                }
            }

            so.ApplyModifiedProperties();
            return ActionResult.Ok($"Set {successCount}/{properties.Count} camera properties on {cameraPath}");
        }

        // ─────────────────────────────────────────────────────
        //  3.8 SetLightSettings
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// Modifies settings on a Light component via SerializedObject.
        /// </summary>
        /// <param name="lightPath">Breadcrumb path to the light GameObject.</param>
        /// <param name="properties">Light property paths and values.</param>
        public static ActionResult SetLightSettings(
            string lightPath, Dictionary<string, string> properties)
        {
            if (!PathResolver.ResolveRootPath(lightPath, out var targets) || targets.Count == 0)
                return ActionResult.Fail($"GameObject not found: {lightPath}");
            var go = targets[0];
            var light = go.GetComponent<Light>();
            if (light == null) return ActionResult.Fail($"No Light component on: {lightPath}");

            Undo.RecordObject(light, "SetLightSettings");
            var so = new SerializedObject(light);
            int successCount = 0;

            foreach (var kvp in properties)
            {
                var prop = so.FindProperty(kvp.Key);
                if (prop != null)
                {
                    PropertyValueParser.SetPropertyValue(prop, kvp.Value);
                    successCount++;
                }
                else
                {
                    Debug.LogWarning($"[AgentBridge] Light property not found: {kvp.Key}");
                }
            }

            so.ApplyModifiedProperties();
            return ActionResult.Ok($"Set {successCount}/{properties.Count} light properties on {lightPath}");
        }

        // ─────────────────────────────────────────────────────
        //  3.9 AssignRenderPipelineAsset
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// Assigns a Render Pipeline Asset to a quality level or as the default.
        /// </summary>
        /// <param name="assetPath">Project path to the pipeline asset.</param>
        /// <param name="qualityLevel">Quality level name. Null = set as default in GraphicsSettings.</param>
        public static ActionResult AssignRenderPipelineAsset(
            string assetPath, string qualityLevel = null)
        {
            var asset = AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>(assetPath);
            if (asset == null) return ActionResult.Fail($"Pipeline asset not found: {assetPath}");

            if (qualityLevel != null)
            {
                int idx = FindQualityLevelIndex(qualityLevel);
                if (idx < 0) return ActionResult.Fail($"Quality level not found: {qualityLevel}");

                var qsAsset = AssetDatabase.LoadMainAssetAtPath("ProjectSettings/QualitySettings.asset");
                if (qsAsset == null) return ActionResult.Fail("Could not load QualitySettings");

                var so = new SerializedObject(qsAsset);
                var qArray = so.FindProperty("m_QualitySettings");
                if (idx >= qArray.arraySize) return ActionResult.Fail("Quality level index out of range");

                var rpProp = qArray.GetArrayElementAtIndex(idx)
                    .FindPropertyRelative("customRenderPipeline");
                if (rpProp == null) return ActionResult.Fail("customRenderPipeline property not found");

                rpProp.objectReferenceValue = asset;
                so.ApplyModifiedProperties();

                Debug.Log($"[AgentBridge] Assigned {asset.name} to quality: {qualityLevel}");
                return ActionResult.Ok($"Assigned {asset.name} to quality: {qualityLevel}");
            }
            else
            {
                GraphicsSettings.defaultRenderPipeline = asset;
                EditorUtility.SetDirty(GraphicsSettings.GetGraphicsSettings());

                Debug.Log($"[AgentBridge] Set default pipeline: {asset.name}");
                return ActionResult.Ok($"Set default pipeline: {asset.name}");
            }
        }

        // ─────────────────────────────────────────────────────
        //  3.10 ConfigureSTP
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// Configures Spatial Temporal Post-Processing (STP) on the active render pipeline asset.
        /// </summary>
        /// <param name="enabled">Enable or disable STP.</param>
        /// <param name="renderScale">
        /// Render scale (0.1–2.0). Only applied when <paramref name="enabled"/> is true.
        /// </param>
        /// <param name="qualityLevel">Target quality level. Null = default pipeline asset.</param>
        public static ActionResult ConfigureSTP(
            bool enabled, float renderScale = 1.0f, string qualityLevel = null)
        {
            RenderPipelineAsset pipelineAsset;
            if (qualityLevel != null)
            {
                int idx = FindQualityLevelIndex(qualityLevel);
                if (idx < 0)
                    return ActionResult.Fail(
                        $"Quality level not found: {qualityLevel}. Available: {string.Join(", ", QualitySettings.names)}");
                pipelineAsset = QualitySettings.GetRenderPipelineAssetAt(idx);
                if (pipelineAsset == null)
                    return ActionResult.Fail($"No pipeline asset on quality level: {qualityLevel}");
            }
            else
            {
                pipelineAsset = GraphicsSettings.defaultRenderPipeline;
                if (pipelineAsset == null)
                    return ActionResult.Fail("No default render pipeline asset configured");
            }

            var so = new SerializedObject(pipelineAsset);

            // Discover STP enabled property — candidate paths ordered by likelihood
            string[] stpEnabledPaths = { "m_StpEnabled", "m_Stp", "m_EnableSTP", "m_UseSTP" };
            SerializedProperty stpProp = null;
            foreach (var path in stpEnabledPaths)
            {
                stpProp = so.FindProperty(path);
                if (stpProp != null) break;
            }

            if (stpProp == null)
                return ActionResult.Fail(
                    $"STP property not found on {pipelineAsset.GetType().Name}. " +
                    "Use ListRenderPipelineProperties() to discover the correct property name.");

            Undo.RecordObject(pipelineAsset, "Configure STP");

            // Toggle STP
            if (stpProp.propertyType == SerializedPropertyType.Boolean)
                stpProp.boolValue = enabled;
            else if (stpProp.propertyType == SerializedPropertyType.Enum)
                stpProp.enumValueIndex = enabled ? 1 : 0;
            else
                stpProp.intValue = enabled ? 1 : 0;

            var parts = new List<string> { $"STP {(enabled ? "enabled" : "disabled")}" };

            // Apply render scale only when enabling STP
            if (enabled)
            {
                renderScale = Mathf.Clamp(renderScale, 0.1f, 2.0f);
                string[] scalePaths = { "m_RenderScale", "m_UpscaleRenderScale", "m_StpRenderScale" };
                SerializedProperty scaleProp = null;
                foreach (var path in scalePaths)
                {
                    scaleProp = so.FindProperty(path);
                    if (scaleProp != null) break;
                }
                if (scaleProp != null)
                {
                    scaleProp.floatValue = renderScale;
                    parts.Add($"renderScale = {renderScale}");
                }
                else
                {
                    parts.Add("(renderScale property not found — not applied)");
                }
            }

            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(pipelineAsset);

            string summary = string.Join(", ", parts);
            Debug.Log($"[AgentBridge] STP configured: {summary}");
            return ActionResult.Ok(summary, pipelineAsset);
        }

        // ─────────────────────────────────────────────────────
        //  Menu Items
        // ─────────────────────────────────────────────────────

        [MenuItem("Axiom/AgentBridge/Actions/List Render Pipeline Properties")]
        public static void MenuListProps()
        {
            Debug.Log($"[AgentBridge] {ListRenderPipelineProperties().Message}");
        }

        [MenuItem("Axiom/AgentBridge/Actions/Show Active Pipeline")]
        public static void MenuShowPipeline()
        {
            Debug.Log($"[AgentBridge] {GetActiveRenderPipeline().Message}");
        }
    }
}
