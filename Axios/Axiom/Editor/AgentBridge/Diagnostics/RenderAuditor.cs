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
    public static class RenderAuditor
    {
        public enum RenderAuditorMode
        {
            PipelineSummary,
            GPUResidentDrawerCompatibility,
            OcclusionCullingStats,
            STPConfiguration,
            ShaderVariantReport,
            LightAndShadowAudit,
            CameraStackReport
        }

        [MenuItem("Axiom/AgentBridge/Render Auditor — Mode A (Pipeline Summary)")]
        public static void ModeA() => GenerateReport(RenderAuditorMode.PipelineSummary);

        [MenuItem("Axiom/AgentBridge/Render Auditor — Mode B (GPU Resident Drawer Compatibility)")]
        public static void ModeB() => GenerateReport(RenderAuditorMode.GPUResidentDrawerCompatibility);

        [MenuItem("Axiom/AgentBridge/Render Auditor — Mode C (Occlusion Culling Stats)")]
        public static void ModeC() => GenerateReport(RenderAuditorMode.OcclusionCullingStats);

        [MenuItem("Axiom/AgentBridge/Render Auditor — Mode D (STP Configuration)")]
        public static void ModeD() => GenerateReport(RenderAuditorMode.STPConfiguration);

        [MenuItem("Axiom/AgentBridge/Render Auditor — Mode E (Shader Variant Report)")]
        public static void ModeE() => GenerateReport(RenderAuditorMode.ShaderVariantReport);

        [MenuItem("Axiom/AgentBridge/Render Auditor — Mode F (Light and Shadow Audit)")]
        public static void ModeF() => GenerateReport(RenderAuditorMode.LightAndShadowAudit);

        [MenuItem("Axiom/AgentBridge/Render Auditor — Mode G (Camera Stack Report)")]
        public static void ModeG() => GenerateReport(RenderAuditorMode.CameraStackReport);

        public static string GenerateReport(RenderAuditorMode mode)
        {
            var sb = new StringBuilder();

            switch (mode)
            {
                case RenderAuditorMode.PipelineSummary:
                    BuildPipelineSummary(sb);
                    return OutputWriter.WriteReport("render_auditor_pipeline", sb.ToString());
                case RenderAuditorMode.GPUResidentDrawerCompatibility:
                    BuildGPUResidentDrawer(sb);
                    return OutputWriter.WriteReport("render_auditor_gpuresident", sb.ToString());
                case RenderAuditorMode.OcclusionCullingStats:
                    BuildOcclusionStats(sb);
                    return OutputWriter.WriteReport("render_auditor_occlusion", sb.ToString());
                case RenderAuditorMode.STPConfiguration:
                    BuildSTPConfiguration(sb);
                    return OutputWriter.WriteReport("render_auditor_stp", sb.ToString());
                case RenderAuditorMode.ShaderVariantReport:
                    BuildShaderVariantReport(sb);
                    return OutputWriter.WriteReport("render_auditor_variants", sb.ToString());
                case RenderAuditorMode.LightAndShadowAudit:
                    BuildLightAndShadowAudit(sb);
                    return OutputWriter.WriteReport("render_auditor_lights", sb.ToString());
                case RenderAuditorMode.CameraStackReport:
                    BuildCameraStackReport(sb);
                    return OutputWriter.WriteReport("render_auditor_cameras", sb.ToString());
                default:
                    return OutputWriter.WriteReport("render_auditor", "Unknown mode");
            }
        }

        // ─── Shared Helpers ──────────────────────────────────────────────────────

        static RenderPipelineAsset GetPipelineAsset() => GraphicsSettings.currentRenderPipeline;

        static string GetPipelineType(RenderPipelineAsset asset)
        {
            if (asset == null) return "Built-in";
            string typeName = asset.GetType().Name;
            if (typeName.Contains("Universal") || typeName.Contains("URP")) return "URP";
            if (typeName.Contains("HD") || typeName.Contains("HDRP")) return "HDRP";
            return typeName;
        }

        static string GetSerializedString(SerializedObject so, string prop)
        {
            var p = so.FindProperty(prop);
            return p != null ? p.stringValue : "—";
        }

        static float GetSerializedFloat(SerializedObject so, string prop, float fallback = 0f)
        {
            var p = so.FindProperty(prop);
            return p != null ? p.floatValue : fallback;
        }

        static int GetSerializedInt(SerializedObject so, string prop, int fallback = 0)
        {
            var p = so.FindProperty(prop);
            return p != null ? p.intValue : fallback;
        }

        static bool GetSerializedBool(SerializedObject so, string prop, bool fallback = false)
        {
            var p = so.FindProperty(prop);
            return p != null ? p.boolValue : fallback;
        }

        static string GetPath(GameObject go)
        {
            var parts = new List<string>();
            var t = go.transform;
            while (t != null) { parts.Insert(0, t.name); t = t.parent; }
            return string.Join("/", parts);
        }

        // ─── Mode A: Pipeline Summary ────────────────────────────────────────────

        static void BuildPipelineSummary(StringBuilder sb)
        {
            sb.AppendLine("# Render Pipeline Summary");
            sb.AppendLine();

            var asset = GetPipelineAsset();
            string pipelineType = GetPipelineType(asset);
            string assetName = asset != null ? asset.name : "None (Built-in)";
            string assetPath = asset != null ? AssetDatabase.GetAssetPath(asset) : "—";

            sb.AppendLine($"- Active Pipeline: {pipelineType}");
            sb.AppendLine($"- Pipeline Asset: {assetName} ({assetPath})");
            sb.AppendLine($"- Color Space: {QualitySettings.activeColorSpace}");
            sb.AppendLine($"- Quality Level: {QualitySettings.names[QualitySettings.GetQualityLevel()]} (Level {QualitySettings.GetQualityLevel()} of {QualitySettings.names.Length})");

            if (asset != null)
            {
                var so = new SerializedObject(asset);

                // GPU Resident Drawer
                var gpuDrawerProp = so.FindProperty("m_GPUResidentDrawerMode");
                string gpuDrawer = gpuDrawerProp != null
                    ? (gpuDrawerProp.intValue == 0 ? "Disabled" : $"Enabled (mode {gpuDrawerProp.intValue})")
                    : "—";
                sb.AppendLine($"- GPU Resident Drawer: {gpuDrawer}");

                // STP
                var stpProp = so.FindProperty("m_StpEnabled") ?? so.FindProperty("m_EnableSTP");
                sb.AppendLine($"- STP (Spatial Temporal Post-Processing): {(stpProp != null ? (stpProp.boolValue ? "Enabled" : "Disabled") : "—")}");

                // HDR
                var hdrProp = so.FindProperty("m_SupportsHDR") ?? so.FindProperty("m_HDR");
                sb.AppendLine($"- HDR: {(hdrProp != null ? (hdrProp.boolValue ? "On" : "Off") : "—")}");

                // MSAA
                var msaaProp = so.FindProperty("m_MSAA") ?? so.FindProperty("m_MSAASampleCount");
                string msaa = msaaProp != null ? (msaaProp.intValue == 1 ? "Off" : $"{msaaProp.intValue}x") : "—";
                sb.AppendLine($"- MSAA: {msaa}");

                // Render Scale
                var scaleProp = so.FindProperty("m_RenderScale");
                sb.AppendLine($"- Render Scale: {(scaleProp != null ? scaleProp.floatValue.ToString("F2") : "—")}");

                // Shadow Distance
                var shadowDistProp = so.FindProperty("m_ShadowDistance");
                sb.AppendLine($"- Shadow Distance: {(shadowDistProp != null ? shadowDistProp.floatValue.ToString("F0") : "—")}");

                // Shadow Resolution
                var shadowResProp = so.FindProperty("m_MainLightShadowmapResolution") ?? so.FindProperty("m_ShadowAtlasResolution");
                sb.AppendLine($"- Shadow Resolution: {(shadowResProp != null ? shadowResProp.intValue.ToString() : "—")}");

                // Shadow Cascades
                var shadowCascProp = so.FindProperty("m_ShadowCascadeCount") ?? so.FindProperty("m_ShadowCascades");
                sb.AppendLine($"- Shadow Cascades: {(shadowCascProp != null ? shadowCascProp.intValue.ToString() : "—")}");

                // Depth Texture
                var depthProp = so.FindProperty("m_RequireDepthTexture");
                sb.AppendLine($"- Depth Texture: {(depthProp != null ? (depthProp.boolValue ? "Enabled" : "Disabled") : "—")}");

                // Opaque Texture
                var opaqueProp = so.FindProperty("m_RequireOpaqueTexture");
                sb.AppendLine($"- Opaque Texture: {(opaqueProp != null ? (opaqueProp.boolValue ? "Enabled" : "Disabled") : "—")}");

                // LOD Cross Fade
                var lodProp = so.FindProperty("m_UseSRPBatcher") ?? so.FindProperty("m_EnableLODCrossFade");
                var lodCrossFadeProp = so.FindProperty("m_LODCrossFadeDitheringType");
                sb.AppendLine($"- LOD Cross Fade: {(lodCrossFadeProp != null ? "Enabled" : "—")}");

                // SRP Batcher
                var srpProp = so.FindProperty("m_UseSRPBatcher");
                sb.AppendLine($"- SRP Batcher: {(srpProp != null ? (srpProp.boolValue ? "Enabled" : "Disabled") : "—")}");
            }
            else
            {
                sb.AppendLine("- HDR: —");
                sb.AppendLine("- MSAA: —");
                sb.AppendLine($"- MSAA (QualitySettings): {QualitySettings.antiAliasing}x");
                sb.AppendLine($"- Shadow Distance: {QualitySettings.shadowDistance:F0}");
                sb.AppendLine($"- Shadow Resolution: {QualitySettings.shadowResolution}");
                sb.AppendLine($"- Shadow Cascades: {QualitySettings.shadowCascades}");
            }

            sb.AppendLine();
            sb.AppendLine($"---\nGenerated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        }

        // ─── Mode B: GPU Resident Drawer Compatibility ───────────────────────────

        static void BuildGPUResidentDrawer(StringBuilder sb)
        {
            sb.AppendLine("# GPU Resident Drawer Compatibility");
            sb.AppendLine();

            var renderers = UnityEngine.Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None);
            int total = renderers.Length;

            if (total == 0)
            {
                sb.AppendLine("_No MeshRenderers found in scene._");
                sb.AppendLine();
                sb.AppendLine($"---\nGenerated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                return;
            }

            var incompatible = new List<(string path, string reason)>();

            foreach (var r in renderers)
            {
                if (r.HasPropertyBlock())
                {
                    incompatible.Add((GetPath(r.gameObject), "MaterialPropertyBlock in use — prevents batching"));
                    continue;
                }

                var flags = GameObjectUtility.GetStaticEditorFlags(r.gameObject);
                bool isBatchingStatic = (flags & StaticEditorFlags.BatchingStatic) != 0;
                bool contributeGI = (flags & StaticEditorFlags.ContributeGI) != 0;

                if (r.motionVectorGenerationMode == MotionVectorGenerationMode.Object && !isBatchingStatic)
                {
                    incompatible.Add((GetPath(r.gameObject), "Motion vectors (Object) on non-static object"));
                }
            }

            // Also count canvas renderers
            var canvasRenderers = UnityEngine.Object.FindObjectsByType<CanvasRenderer>(FindObjectsSortMode.None);
            int canvasCount = canvasRenderers.Length;

            int compatible = total - incompatible.Count;

            sb.AppendLine("### Summary");
            sb.AppendLine($"- Total MeshRenderers: {total}");
            sb.AppendLine($"- Compatible: {compatible} ({(total > 0 ? compatible * 100 / total : 0)}%)");
            sb.AppendLine($"- Incompatible: {incompatible.Count} ({(total > 0 ? incompatible.Count * 100 / total : 0)}%)");
            if (canvasCount > 0)
                sb.AppendLine($"- Canvas Renderers (not eligible): {canvasCount}");
            sb.AppendLine();

            if (incompatible.Count > 0)
            {
                sb.AppendLine("### Incompatible Objects");
                sb.AppendLine("| Object | Reason |");
                sb.AppendLine("| :--- | :--- |");
                foreach (var (path, reason) in incompatible.OrderBy(x => x.path))
                    sb.AppendLine($"| {path} | {reason} |");
                sb.AppendLine();
            }

            // Count objects with property blocks for recommendations
            int propBlockCount = renderers.Count(r => r.HasPropertyBlock());
            var nonStaticDynGI = renderers
                .Where(r => (GameObjectUtility.GetStaticEditorFlags(r.gameObject) & StaticEditorFlags.ContributeGI) == 0
                    && (GameObjectUtility.GetStaticEditorFlags(r.gameObject) & StaticEditorFlags.BatchingStatic) == 0)
                .Count();

            if (propBlockCount > 0 || incompatible.Count > 0)
            {
                sb.AppendLine("### Recommendations");
                if (propBlockCount > 0)
                    sb.AppendLine($"- Remove MaterialPropertyBlocks from {propBlockCount} object(s) (use material instances instead)");
                if (incompatible.Count > propBlockCount)
                    sb.AppendLine($"- Review {incompatible.Count - propBlockCount} object(s) with motion vector settings");
                sb.AppendLine();
            }

            sb.AppendLine($"---\nGenerated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        }

        // ─── Mode C: Occlusion Culling Stats ─────────────────────────────────────

        static void BuildOcclusionStats(StringBuilder sb)
        {
            sb.AppendLine("# Occlusion Culling");
            sb.AppendLine();

            sb.AppendLine("### Configuration");

            // Check URP GPU occlusion setting
            var pipelineAsset = GetPipelineAsset();
            if (pipelineAsset != null)
            {
                var so = new SerializedObject(pipelineAsset);
                var occlusionProp = so.FindProperty("m_GPUResidentDrawerMode");
                string gpuOcclusion = occlusionProp != null && occlusionProp.intValue > 0 ? "Enabled (via URP GPU Resident Drawer)" : "Disabled";
                sb.AppendLine($"- GPU Occlusion Culling: {gpuOcclusion}");
            }

            // Legacy occlusion data
            long umbraSize = StaticOcclusionCulling.umbraDataSize;
            sb.AppendLine($"- Legacy Occlusion Culling Data: {(umbraSize > 0 ? $"Present (baked, {umbraSize:N0} bytes)" : "Not baked")}");
            sb.AppendLine();

            // Scene statistics
            var allRenderers = UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None);
            int staticOccluders = 0, staticOccludees = 0, dynamic = 0;

            foreach (var r in allRenderers)
            {
                var flags = GameObjectUtility.GetStaticEditorFlags(r.gameObject);
                bool isOccluder = (flags & StaticEditorFlags.OccluderStatic) != 0;
                bool isOccludee = (flags & StaticEditorFlags.OccludeeStatic) != 0;
                bool isStatic = isOccluder || isOccludee
                    || (flags & StaticEditorFlags.BatchingStatic) != 0
                    || (flags & StaticEditorFlags.ContributeGI) != 0;

                if (isOccluder) staticOccluders++;
                if (isOccludee) staticOccludees++;
                if (!isStatic) dynamic++;
            }

            sb.AppendLine("### Scene Statistics");
            sb.AppendLine($"- Total Renderers: {allRenderers.Length}");
            sb.AppendLine($"- Static Occluders (OccluderStatic): {staticOccluders}");
            sb.AppendLine($"- Static Occludees (OccludeeStatic): {staticOccludees}");
            sb.AppendLine($"- Dynamic Objects (no static flags): {dynamic}");
            sb.AppendLine();

            // Occlusion areas
            var areas = UnityEngine.Object.FindObjectsByType<OcclusionArea>(FindObjectsSortMode.None);
            if (areas.Length > 0)
            {
                sb.AppendLine("### Occlusion Areas");
                sb.AppendLine("| Area | Size | Center |");
                sb.AppendLine("| :--- | :--- | :--- |");
                foreach (var area in areas)
                {
                    string size = $"{area.size.x:F0}x{area.size.y:F0}x{area.size.z:F0}";
                    string center = $"({area.center.x:F1},{area.center.y:F1},{area.center.z:F1})";
                    sb.AppendLine($"| {area.name} | {size} | {center} |");
                }
                sb.AppendLine();
            }

            sb.AppendLine($"---\nGenerated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        }

        // ─── Mode D: STP Configuration ───────────────────────────────────────────

        static void BuildSTPConfiguration(StringBuilder sb)
        {
            sb.AppendLine("# STP Configuration");
            sb.AppendLine();

            string[] qualityNames = QualitySettings.names;
            if (qualityNames.Length == 0)
            {
                sb.AppendLine("_No quality levels defined._");
                sb.AppendLine();
                sb.AppendLine($"---\nGenerated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                return;
            }

            sb.AppendLine("| Quality Level | STP Enabled | Upscaling Filter | Render Scale |");
            sb.AppendLine("| :--- | :--- | :--- | :--- |");

            int currentLevel = QualitySettings.GetQualityLevel();

            for (int i = 0; i < qualityNames.Length; i++)
            {
                // Temporarily switch quality level to get its render pipeline asset
                QualitySettings.SetQualityLevel(i, false);
                var asset = QualitySettings.renderPipeline ?? GraphicsSettings.currentRenderPipeline;

                string stpEnabled = "—";
                string upscalingFilter = "—";
                string renderScale = "—";

                if (asset != null)
                {
                    var so = new SerializedObject(asset);

                    var stpProp = so.FindProperty("m_StpEnabled") ?? so.FindProperty("m_EnableSTP");
                    stpEnabled = stpProp != null ? (stpProp.boolValue ? "Yes" : "No") : "—";

                    var filterProp = so.FindProperty("m_UpscalingFilter");
                    if (filterProp != null)
                    {
                        upscalingFilter = filterProp.enumDisplayNames != null && filterProp.enumValueIndex >= 0
                            && filterProp.enumValueIndex < filterProp.enumDisplayNames.Length
                            ? filterProp.enumDisplayNames[filterProp.enumValueIndex]
                            : filterProp.intValue.ToString();
                    }

                    var scaleProp = so.FindProperty("m_RenderScale");
                    renderScale = scaleProp != null ? scaleProp.floatValue.ToString("F2") : "—";
                }
                else
                {
                    stpEnabled = "N/A (Built-in)";
                }

                sb.AppendLine($"| {qualityNames[i]} | {stpEnabled} | {upscalingFilter} | {renderScale} |");
            }

            // Restore original quality level
            QualitySettings.SetQualityLevel(currentLevel, false);

            sb.AppendLine();
            sb.AppendLine($"---\nGenerated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        }

        // ─── Mode E: Shader Variant Report ──────────────────────────────────────

        static readonly string[] BuiltInKeywordPrefixes = {
            "_MAIN_LIGHT_", "_ADDITIONAL_LIGHTS", "_SHADOWS_", "_LIGHT_LAYERS",
            "_MIXED_LIGHTING_SUBTRACTIVE", "_SCREEN_SPACE_OCCLUSION", "_RENDERING_",
            "_ALPHATEST_", "_ALPHAPREMULTIPLY_", "_EMISSION", "_METALLICSPECGLOSSMAP",
            "_SPECGLOSSMAP", "_NORMALMAP", "_PARALLAXMAP", "_DETAIL_MULX2",
            "_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A", "_OCCLUSIONMAP", "_SPECULARHIGHLIGHTS_OFF",
            "_ENVIRONMENTREFLECTIONS_OFF", "_RECEIVE_SHADOWS_OFF", "DIRLIGHTMAP_COMBINED",
            "LIGHTMAP_ON", "DYNAMICLIGHTMAP_ON", "LIGHTPROBE_SH", "INSTANCING_ON",
            "_SURFACE_TYPE_TRANSPARENT", "_DOUBLESIDED_ON"
        };

        static void BuildShaderVariantReport(StringBuilder sb)
        {
            sb.AppendLine("# Shader Variant Report");
            sb.AppendLine();

            string[] matGuids = AssetDatabase.FindAssets("t:Material");
            if (matGuids.Length == 0)
            {
                sb.AppendLine("_No materials found in project._");
                sb.AppendLine();
                sb.AppendLine($"---\nGenerated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                return;
            }

            // Collect keywords per shader
            var shaderKeywords = new Dictionary<string, HashSet<string>>();
            var shaderBuiltinCount = new Dictionary<string, int>();
            var shaderCustomCount = new Dictionary<string, int>();

            foreach (string guid in matGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null || mat.shader == null) continue;
                if (mat.shader.name == "Hidden/InternalErrorShader") continue;

                string shName = mat.shader.name;
                if (!shaderKeywords.ContainsKey(shName))
                {
                    shaderKeywords[shName] = new HashSet<string>();
                    shaderBuiltinCount[shName] = 0;
                    shaderCustomCount[shName] = 0;
                }

                foreach (string kw in mat.shaderKeywords)
                {
                    shaderKeywords[shName].Add(kw);
                    bool isBuiltIn = BuiltInKeywordPrefixes.Any(prefix => kw.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
                    if (isBuiltIn) shaderBuiltinCount[shName]++;
                    else shaderCustomCount[shName]++;
                }
            }

            if (shaderKeywords.Count == 0)
            {
                sb.AppendLine("_No shader keywords found across all materials._");
                sb.AppendLine();
                sb.AppendLine($"---\nGenerated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                return;
            }

            long totalVariants = 0;

            sb.AppendLine("| Shader | Variant Count (Editor) | Built-in Keywords | Custom Keywords | Risk |");
            sb.AppendLine("| :--- | :--- | :--- | :--- | :--- |");

            foreach (var kvp in shaderKeywords.OrderByDescending(k => k.Value.Count))
            {
                int n = kvp.Value.Count;
                long variants = n > 0 ? 1L << Math.Min(n, 30) : 1;
                totalVariants += variants;
                int builtin = shaderBuiltinCount[kvp.Key];
                int custom = shaderCustomCount[kvp.Key];
                string risk = n > 8 ? "⚠ High" : n > 5 ? "⚠ Medium" : "✓ Low";
                sb.AppendLine($"| {kvp.Key} | {variants:N0} | {builtin} | {custom} | {risk} |");
            }

            sb.AppendLine();
            sb.AppendLine($"### Total Estimated Variants: {totalVariants:N0}");
            sb.AppendLine("### Stripping Recommendation: Enable shader keyword stripping in Player Settings");

            sb.AppendLine();
            sb.AppendLine($"---\nGenerated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        }

        // ─── Mode F: Light and Shadow Audit ─────────────────────────────────────

        static void BuildLightAndShadowAudit(StringBuilder sb)
        {
            sb.AppendLine("# Light and Shadow Audit");
            sb.AppendLine();

            var lights = UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None);
            sb.AppendLine($"## Lights in Scene ({lights.Length})");
            sb.AppendLine();

            if (lights.Length == 0)
            {
                sb.AppendLine("_No lights found in scene._");
                sb.AppendLine();
                sb.AppendLine($"---\nGenerated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                return;
            }

            var directional = lights.Where(l => l.type == LightType.Directional).ToArray();
            var point = lights.Where(l => l.type == LightType.Point).ToArray();
            var spot = lights.Where(l => l.type == LightType.Spot).ToArray();
            var area = lights.Where(l => l.type == LightType.Rectangle || l.type == LightType.Disc).ToArray();

            if (directional.Length > 0)
            {
                sb.AppendLine($"### Directional Lights ({directional.Length})");
                sb.AppendLine("| Object | Intensity | Shadows | Shadow Res | Bias | Mode |");
                sb.AppendLine("| :--- | :--- | :--- | :--- | :--- | :--- |");
                foreach (var l in directional)
                    sb.AppendLine($"| {GetPath(l.gameObject)} | {l.intensity:F2} | {l.shadows} | {l.shadowResolution} | {l.shadowBias:F3} | {l.lightmapBakeType} |");
                sb.AppendLine();
            }

            if (point.Length > 0)
            {
                sb.AppendLine($"### Point Lights ({point.Length})");
                sb.AppendLine("| Object | Intensity | Range | Shadows | Mode |");
                sb.AppendLine("| :--- | :--- | :--- | :--- | :--- |");
                foreach (var l in point)
                    sb.AppendLine($"| {GetPath(l.gameObject)} | {l.intensity:F2} | {l.range:F1} | {l.shadows} | {l.lightmapBakeType} |");
                sb.AppendLine();
            }

            if (spot.Length > 0)
            {
                sb.AppendLine($"### Spot Lights ({spot.Length})");
                sb.AppendLine("| Object | Intensity | Range | Spot Angle | Shadows | Mode |");
                sb.AppendLine("| :--- | :--- | :--- | :--- | :--- | :--- |");
                foreach (var l in spot)
                    sb.AppendLine($"| {GetPath(l.gameObject)} | {l.intensity:F2} | {l.range:F1} | {l.spotAngle:F1}° | {l.shadows} | {l.lightmapBakeType} |");
                sb.AppendLine();
            }

            if (area.Length > 0)
            {
                sb.AppendLine($"### Area Lights ({area.Length})");
                sb.AppendLine("| Object | Intensity | Size | Mode |");
                sb.AppendLine("| :--- | :--- | :--- | :--- |");
                foreach (var l in area)
                    sb.AppendLine($"| {GetPath(l.gameObject)} | {l.intensity:F2} | {l.areaSize.x:F1}x{l.areaSize.y:F1} | {l.lightmapBakeType} |");
                sb.AppendLine();
            }

            int realtimeShadows = lights.Count(l => l.shadows != LightShadows.None && l.lightmapBakeType == LightmapBakeType.Realtime);
            int baked = lights.Count(l => l.lightmapBakeType == LightmapBakeType.Baked);
            int mixed = lights.Count(l => l.lightmapBakeType == LightmapBakeType.Mixed);

            sb.AppendLine("### Summary");
            sb.AppendLine($"- Realtime shadow-casting lights: {realtimeShadows} (performance-sensitive)");
            sb.AppendLine($"- Baked lights: {baked}");
            sb.AppendLine($"- Mixed lights: {mixed}");
            sb.AppendLine($"- Realtime lights: {lights.Count(l => l.lightmapBakeType == LightmapBakeType.Realtime)}");

            sb.AppendLine();
            sb.AppendLine($"---\nGenerated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        }

        // ─── Mode G: Camera Stack Report ────────────────────────────────────────

        static void BuildCameraStackReport(StringBuilder sb)
        {
            sb.AppendLine("# Camera Stack Report");
            sb.AppendLine();

            var cameras = UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsSortMode.None);

            if (cameras.Length == 0)
            {
                sb.AppendLine("_No cameras found in scene._");
                sb.AppendLine();
                sb.AppendLine($"---\nGenerated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                return;
            }

            // Try to get URP additional camera data type
            var urpCamDataType = TypeResolver.ResolveComponentType("UniversalAdditionalCameraData");

            var baseCams = new List<Camera>();
            var overlayCams = new List<Camera>();
            var unknownCams = new List<Camera>();

            foreach (var cam in cameras)
            {
                if (urpCamDataType != null)
                {
                    var camData = cam.GetComponent(urpCamDataType);
                    if (camData != null)
                    {
                        var so = new SerializedObject(camData);
                        var renderTypeProp = so.FindProperty("m_RenderType");
                        if (renderTypeProp != null)
                        {
                            if (renderTypeProp.intValue == 0) baseCams.Add(cam);
                            else overlayCams.Add(cam);
                            continue;
                        }
                    }
                }
                unknownCams.Add(cam);
            }

            if (baseCams.Count == 0 && unknownCams.Count > 0)
            {
                // No URP data — treat all as regular cameras
                baseCams.AddRange(unknownCams);
                unknownCams.Clear();
            }

            sb.AppendLine($"### Base Cameras ({baseCams.Count + unknownCams.Count})");
            sb.AppendLine("| Object | Depth | Clear | Culling Mask | HDR | MSAA | Post-Processing | Render Type |");
            sb.AppendLine("| :--- | :--- | :--- | :--- | :--- | :--- | :--- | :--- |");

            foreach (var cam in (baseCams.Count > 0 ? baseCams : unknownCams).OrderBy(c => c.depth))
            {
                string postProcessing = "—";
                string renderType = "Base";

                if (urpCamDataType != null)
                {
                    var camData = cam.GetComponent(urpCamDataType);
                    if (camData != null)
                    {
                        var so = new SerializedObject(camData);
                        var ppProp = so.FindProperty("m_RenderPostProcessing");
                        postProcessing = ppProp != null ? (ppProp.boolValue ? "Yes" : "No") : "—";
                        var rtProp = so.FindProperty("m_RenderType");
                        renderType = rtProp != null ? (rtProp.intValue == 0 ? "Base" : "Overlay") : "—";
                    }
                }

                string mask = cam.cullingMask == -1 ? "Everything" : cam.cullingMask == 0 ? "Nothing" : cam.cullingMask.ToString();
                sb.AppendLine($"| {GetPath(cam.gameObject)} | {cam.depth} | {cam.clearFlags} | {mask} | {(cam.allowHDR ? "On" : "Off")} | {(cam.allowMSAA ? "Use Pipeline" : "Off")} | {postProcessing} | {renderType} |");
            }

            if (overlayCams.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine($"### Overlay Cameras ({overlayCams.Count})");
                sb.AppendLine("| Object | Culling Mask | Render Type |");
                sb.AppendLine("| :--- | :--- | :--- |");
                foreach (var cam in overlayCams.OrderBy(c => c.depth))
                {
                    string mask = cam.cullingMask == -1 ? "Everything" : cam.cullingMask == 0 ? "Nothing" : cam.cullingMask.ToString();
                    sb.AppendLine($"| {GetPath(cam.gameObject)} | {mask} | Overlay |");
                }
            }

            sb.AppendLine();

            // Post-processing volumes — use TypeResolver to avoid hard URP assembly dependency
            var volumeType = TypeResolver.ResolveComponentType("Volume");
            if (volumeType != null)
            {
                var volumeObjects = UnityEngine.Object.FindObjectsByType(volumeType, FindObjectsSortMode.None);
                sb.AppendLine($"### Volumes ({volumeObjects.Length})");

                if (volumeObjects.Length > 0)
                {
                    sb.AppendLine("| Object | Mode | Priority | Profile | Effects |");
                    sb.AppendLine("| :--- | :--- | :--- | :--- | :--- |");
                    foreach (var volObj in volumeObjects)
                    {
                        var volComp = volObj as Component;
                        if (volComp == null) continue;
                        var so = new SerializedObject(volObj);

                        var isGlobalProp = so.FindProperty("isGlobal");
                        bool isGlobal = isGlobalProp != null ? isGlobalProp.boolValue : false;
                        string mode = isGlobal ? "Global" : "Local";

                        var priorityProp = so.FindProperty("priority");
                        float priority = priorityProp != null ? priorityProp.floatValue : 0f;

                        var profileProp = so.FindProperty("sharedProfile");
                        string profile = profileProp?.objectReferenceValue?.name ?? "—";

                        // Read effect names from the profile
                        string effects = "—";
                        if (profileProp?.objectReferenceValue != null)
                        {
                            var profileSo = new SerializedObject(profileProp.objectReferenceValue);
                            var componentsProp = profileSo.FindProperty("components");
                            if (componentsProp != null && componentsProp.isArray && componentsProp.arraySize > 0)
                            {
                                var effectNames = new List<string>();
                                for (int i = 0; i < componentsProp.arraySize; i++)
                                {
                                    var elem = componentsProp.GetArrayElementAtIndex(i);
                                    if (elem.objectReferenceValue != null)
                                        effectNames.Add(elem.objectReferenceValue.GetType().Name);
                                }
                                if (effectNames.Count > 0)
                                    effects = string.Join(", ", effectNames);
                            }
                        }

                        sb.AppendLine($"| {GetPath(volComp.gameObject)} | {mode} | {priority:F0} | {profile} | {effects} |");
                    }
                }
                else
                {
                    sb.AppendLine("_No post-processing volumes found in scene._");
                }
            }
            else
            {
                sb.AppendLine("### Volumes (0)");
                sb.AppendLine("_Volume component type not available (URP package may not be installed)._");
            }

            sb.AppendLine();
            sb.AppendLine($"---\nGenerated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        }
    }
}
