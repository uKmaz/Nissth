#pragma warning disable CS0618 // ShaderUtil deprecated APIs acceptable (same as ReferenceScanner)

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor;
using Axiom.Editor.AgentBridge.Core;

namespace Axiom.Editor.AgentBridge.Diagnostics
{
    public static class ShaderAuditor
    {
        public enum ShaderAuditorMode { MaterialCensus, ShaderPropertyDump, KeywordReport, CompilationStatus, GPUCompatibility, ComputeShaderAudit }

        [MenuItem("Axiom/AgentBridge/Shader Auditor — Mode A (Material Census)")]
        public static void ModeA() => GenerateReport(ShaderAuditorMode.MaterialCensus);

        [MenuItem("Axiom/AgentBridge/Shader Auditor — Mode B (Shader Property Dump)")]
        public static void ModeB() => GenerateReport(ShaderAuditorMode.ShaderPropertyDump);

        [MenuItem("Axiom/AgentBridge/Shader Auditor — Mode C (Keyword Report)")]
        public static void ModeC() => GenerateReport(ShaderAuditorMode.KeywordReport);

        [MenuItem("Axiom/AgentBridge/Shader Auditor — Mode D (Compilation Status)")]
        public static void ModeD() => GenerateReport(ShaderAuditorMode.CompilationStatus);

        [MenuItem("Axiom/AgentBridge/Shader Auditor — Mode E (GPU Compatibility)")]
        public static void ModeE() => GenerateReport(ShaderAuditorMode.GPUCompatibility);

        [MenuItem("Axiom/AgentBridge/Shader Auditor — Mode F (Compute Shader Audit)")]
        public static void ModeF() => GenerateReport(ShaderAuditorMode.ComputeShaderAudit);

        public static string GenerateReport(ShaderAuditorMode mode)
        {
            var sb = new StringBuilder();

            switch (mode)
            {
                case ShaderAuditorMode.MaterialCensus:
                    BuildMaterialCensus(sb);
                    return OutputWriter.WriteReport("shader_auditor_materialcensus", sb.ToString());
                case ShaderAuditorMode.ShaderPropertyDump:
                    BuildShaderPropertyDump(sb);
                    return OutputWriter.WriteReport("shader_auditor_shaderprops", sb.ToString());
                case ShaderAuditorMode.KeywordReport:
                    BuildKeywordReport(sb);
                    return OutputWriter.WriteReport("shader_auditor_keywords", sb.ToString());
                case ShaderAuditorMode.CompilationStatus:
                    BuildCompilationStatus(sb);
                    return OutputWriter.WriteReport("shader_auditor_compilation", sb.ToString());
                case ShaderAuditorMode.GPUCompatibility:
                    BuildGPUCompatibility(sb);
                    return OutputWriter.WriteReport("shader_auditor_gpu", sb.ToString());
                case ShaderAuditorMode.ComputeShaderAudit:
                    BuildComputeShaderAudit(sb);
                    return OutputWriter.WriteReport("shader_auditor_compute", sb.ToString());
                default:
                    return OutputWriter.WriteReport("shader_auditor", "Unknown mode");
            }
        }

        // ─── Mode A ─────────────────────────────────────────────────────────────

        static void BuildMaterialCensus(StringBuilder sb)
        {
            string[] guids = AssetDatabase.FindAssets("t:Material");
            sb.AppendLine($"# Material Census ({guids.Length} materials)");
            sb.AppendLine();

            if (guids.Length == 0)
            {
                sb.AppendLine("_No Material assets found in project._");
                sb.AppendLine();
                sb.AppendLine($"---\nGenerated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                return;
            }

            // Group by shader
            var byShader = new Dictionary<string, List<(string path, int renderQueue)>>();
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null) continue;

                string shaderName;
                if (mat.shader == null)
                    shaderName = "MISSING SHADER";
                else if (mat.shader.name == "Hidden/InternalErrorShader")
                    shaderName = "Shader Not Found";
                else
                    shaderName = mat.shader.name;

                if (!byShader.ContainsKey(shaderName))
                    byShader[shaderName] = new List<(string, int)>();
                byShader[shaderName].Add((path, mat.renderQueue));
            }

            sb.AppendLine("| Shader | Material Count | Render Queues |");
            sb.AppendLine("| :--- | :--- | :--- |");
            foreach (var kvp in byShader.OrderByDescending(k => k.Value.Count))
            {
                string shaderDisplay = kvp.Key == "Shader Not Found" || kvp.Key == "MISSING SHADER"
                    ? $"✗ {kvp.Key}"
                    : kvp.Key;
                var queueGroups = kvp.Value
                    .GroupBy(m => RenderQueueName(m.renderQueue))
                    .Select(g => $"{g.Key}({g.Count()})")
                    .ToList();
                sb.AppendLine($"| {shaderDisplay} | {kvp.Value.Count} | {string.Join(", ", queueGroups)} |");
            }

            sb.AppendLine();

            // By folder
            var byFolder = new Dictionary<string, int>();
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string folder = Path.GetDirectoryName(path)?.Replace('\\', '/') ?? "Unknown";
                if (!byFolder.ContainsKey(folder)) byFolder[folder] = 0;
                byFolder[folder]++;
            }

            sb.AppendLine("### By Folder");
            sb.AppendLine("| Folder | Count |");
            sb.AppendLine("| :--- | :--- |");
            foreach (var kvp in byFolder.OrderByDescending(k => k.Value))
                sb.AppendLine($"| {kvp.Key} | {kvp.Value} |");

            sb.AppendLine();
            sb.AppendLine($"---\nGenerated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        }

        static string RenderQueueName(int q)
        {
            if (q <= 1000) return "Background";
            if (q <= 2000) return "Geometry";
            if (q <= 2450) return "AlphaTest";
            if (q <= 3000) return "Transparent";
            return "Overlay";
        }

        // ─── Mode B ─────────────────────────────────────────────────────────────

        static void BuildShaderPropertyDump(StringBuilder sb)
        {
            // Find the most-used shader
            string[] matGuids = AssetDatabase.FindAssets("t:Material");
            var shaderCounts = new Dictionary<Shader, int>();
            foreach (string guid in matGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null || mat.shader == null) continue;
                if (mat.shader.name == "Hidden/InternalErrorShader") continue;
                if (!shaderCounts.ContainsKey(mat.shader)) shaderCounts[mat.shader] = 0;
                shaderCounts[mat.shader]++;
            }

            if (shaderCounts.Count == 0)
            {
                sb.AppendLine("# Shader Property Dump");
                sb.AppendLine("_No valid shaders found._");
                sb.AppendLine();
                sb.AppendLine($"---\nGenerated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                return;
            }

            var topShader = shaderCounts.OrderByDescending(k => k.Value).First().Key;
            sb.AppendLine($"# Shader: {topShader.name}");
            sb.AppendLine();

            int propCount = ShaderUtil.GetPropertyCount(topShader);
            sb.AppendLine($"### Properties ({propCount})");
            sb.AppendLine("| Name | Display Name | Type | Default |");
            sb.AppendLine("| :--- | :--- | :--- | :--- |");

            for (int i = 0; i < propCount; i++)
            {
                string propName = ShaderUtil.GetPropertyName(topShader, i);
                string propDesc = ShaderUtil.GetPropertyDescription(topShader, i);
                var propType = ShaderUtil.GetPropertyType(topShader, i);
                string typeStr;
                string defVal = "—";

                switch (propType)
                {
                    case ShaderUtil.ShaderPropertyType.Color:
                        typeStr = "Color";
                        defVal = topShader.GetPropertyDefaultVectorValue(i).ToString();
                        break;
                    case ShaderUtil.ShaderPropertyType.Vector:
                        typeStr = "Vector";
                        defVal = topShader.GetPropertyDefaultVectorValue(i).ToString();
                        break;
                    case ShaderUtil.ShaderPropertyType.Float:
                        typeStr = "Float";
                        defVal = topShader.GetPropertyDefaultFloatValue(i).ToString("F2");
                        break;
                    case ShaderUtil.ShaderPropertyType.Range:
                        float min = ShaderUtil.GetRangeLimits(topShader, i, 1);
                        float max = ShaderUtil.GetRangeLimits(topShader, i, 2);
                        typeStr = $"Range({min:F2},{max:F2})";
                        defVal = topShader.GetPropertyDefaultFloatValue(i).ToString("F2");
                        break;
                    case ShaderUtil.ShaderPropertyType.TexEnv:
                        typeStr = "Texture";
                        defVal = "—";
                        break;
                    default:
                        typeStr = propType.ToString();
                        break;
                }

                sb.AppendLine($"| {propName} | {propDesc} | {typeStr} | {defVal} |");
            }

            sb.AppendLine();

            // Pass info
            sb.AppendLine($"### Passes ({topShader.passCount})");
            sb.AppendLine("| Index | Name | LightMode |");
            sb.AppendLine("| :--- | :--- | :--- |");
            for (int i = 0; i < topShader.passCount; i++)
            {
                ShaderTagId passNameTag = topShader.FindPassTagValue(i, new ShaderTagId("Name"));
                string passName = string.IsNullOrEmpty(passNameTag.name) ? $"Pass {i}" : passNameTag.name;
                ShaderTagId lightModeTag = topShader.FindPassTagValue(i, new ShaderTagId("LightMode"));
                string lightMode = string.IsNullOrEmpty(lightModeTag.name) ? "—" : lightModeTag.name;
                sb.AppendLine($"| {i} | {passName} | {lightMode} |");
            }

            sb.AppendLine();
            sb.AppendLine($"---\nGenerated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        }

        // ─── Mode C ─────────────────────────────────────────────────────────────

        static void BuildKeywordReport(StringBuilder sb)
        {
            sb.AppendLine("# Shader Keywords");
            sb.AppendLine();

            // Global keywords
            var globalKws = Shader.globalKeywords;
            if (globalKws != null && globalKws.Length > 0)
            {
                sb.AppendLine("### Global Keywords Active");
                sb.AppendLine(string.Join(", ", globalKws.Select(k => k.name)));
                sb.AppendLine();
            }
            else
            {
                sb.AppendLine("### Global Keywords Active");
                sb.AppendLine("_None_");
                sb.AppendLine();
            }

            // Per-material keywords
            string[] matGuids = AssetDatabase.FindAssets("t:Material");
            if (matGuids.Length == 0)
            {
                sb.AppendLine("_No materials found._");
                sb.AppendLine();
                sb.AppendLine($"---\nGenerated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                return;
            }

            sb.AppendLine("### Per-Material Keywords");
            sb.AppendLine("| Material | Keywords | Count |");
            sb.AppendLine("| :--- | :--- | :--- |");

            // Also collect unique keywords per shader
            var keywordsPerShader = new Dictionary<string, HashSet<string>>();

            foreach (string guid in matGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null) continue;

                var kws = mat.shaderKeywords;
                if (kws != null && kws.Length > 0)
                {
                    sb.AppendLine($"| {mat.name} | {string.Join(", ", kws)} | {kws.Length} |");

                    if (mat.shader != null && mat.shader.name != "Hidden/InternalErrorShader")
                    {
                        string shName = mat.shader.name;
                        if (!keywordsPerShader.ContainsKey(shName))
                            keywordsPerShader[shName] = new HashSet<string>();
                        foreach (var kw in kws)
                            keywordsPerShader[shName].Add(kw);
                    }
                }
            }

            sb.AppendLine();

            if (keywordsPerShader.Count > 0)
            {
                sb.AppendLine("### Variant Estimate per Shader");
                sb.AppendLine("| Shader | Keywords Used | Est. Variants (2^N) | Risk |");
                sb.AppendLine("| :--- | :--- | :--- | :--- |");
                foreach (var kvp in keywordsPerShader.OrderByDescending(k => k.Value.Count))
                {
                    int n = kvp.Value.Count;
                    long variants = 1L << Math.Min(n, 30);
                    string risk = n > 8 ? "⚠ High risk" : n > 6 ? "⚠ Medium" : "✓ Low";
                    sb.AppendLine($"| {kvp.Key} | {n} unique keywords | {variants:N0} | {risk} |");
                }
                sb.AppendLine();
            }

            sb.AppendLine($"---\nGenerated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        }

        // ─── Mode D ─────────────────────────────────────────────────────────────

        static void BuildCompilationStatus(StringBuilder sb)
        {
            sb.AppendLine("# Shader Compilation Status");
            sb.AppendLine();

            string[] guids = AssetDatabase.FindAssets("t:Shader");
            if (guids.Length == 0)
            {
                sb.AppendLine("_No Shader assets found in project._");
                sb.AppendLine();
                sb.AppendLine($"---\nGenerated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                return;
            }

            sb.AppendLine($"## Shaders ({guids.Length})");
            sb.AppendLine();
            sb.AppendLine("| Shader | Compiled | Errors | Warnings | Subshaders | Passes |");
            sb.AppendLine("| :--- | :--- | :--- | :--- | :--- | :--- |");

            var errorDetails = new Dictionary<string, List<string>>();

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
                if (shader == null) continue;

                bool hasError = ShaderUtil.ShaderHasError(shader);
                var messages = ShaderUtil.GetShaderMessages(shader);
                int msgCount = messages != null ? messages.Length : 0;

                string compiled = hasError ? "✗" : "✓";
                sb.AppendLine($"| {shader.name} | {compiled} | {(hasError ? msgCount : 0)} | {(!hasError ? msgCount : 0)} | {shader.subshaderCount} | {shader.passCount} |");

                if (hasError && messages != null && messages.Length > 0)
                {
                    var errList = messages.Select(m => $"- Line {m.line}: {m.message}").ToList();
                    errorDetails[shader.name] = errList;
                }
            }

            sb.AppendLine();

            foreach (var kvp in errorDetails)
            {
                sb.AppendLine($"### Errors in {kvp.Key}");
                foreach (var err in kvp.Value)
                    sb.AppendLine(err);
                sb.AppendLine();
            }

            sb.AppendLine($"---\nGenerated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        }

        // ─── Mode E ─────────────────────────────────────────────────────────────

        static void BuildGPUCompatibility(StringBuilder sb)
        {
            sb.AppendLine("# GPU Compatibility Check");
            sb.AppendLine();

            sb.AppendLine("### SystemInfo (Current Platform)");
            sb.AppendLine($"- GPU: {SystemInfo.graphicsDeviceName}");
            sb.AppendLine($"- Graphics API: {SystemInfo.graphicsDeviceType}");
            sb.AppendLine($"- Shader Level: {SystemInfo.graphicsShaderLevel}");
            sb.AppendLine($"- Compute: {(SystemInfo.supportsComputeShaders ? "Supported" : "Not Supported")}");
            sb.AppendLine($"- Geometry Shaders: {(SystemInfo.supportsGeometryShaders ? "Supported" : "Not Supported")}");
            sb.AppendLine($"- 2D Array Textures: {(SystemInfo.supports2DArrayTextures ? "Supported" : "Not Supported")}");
            sb.AppendLine($"- Max Texture Size: {SystemInfo.maxTextureSize}");
            sb.AppendLine($"- Build Target: {EditorUserBuildSettings.activeBuildTarget}");
            sb.AppendLine();

            // Compute shaders in project
            string[] computeGuids = AssetDatabase.FindAssets("t:ComputeShader");
            if (computeGuids.Length > 0)
            {
                sb.AppendLine("| Feature | Required By | Supported? |");
                sb.AppendLine("| :--- | :--- | :--- |");
                sb.AppendLine($"| Compute Shaders | {computeGuids.Length} compute shader(s) | {(SystemInfo.supportsComputeShaders ? "✓" : "✗ NOT SUPPORTED on target")} |");
            }

            // Scan shaders for geometry shader usage
            string[] shaderGuids = AssetDatabase.FindAssets("t:Shader");
            var geomShaders = new List<string>();
            foreach (string guid in shaderGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                // Only check project shaders (not packages) to avoid lengthy scan
                if (!path.StartsWith("Assets/")) continue;
                try
                {
                    string src = File.ReadAllText(path);
                    if (src.Contains("#pragma geometry") || src.Contains("GEOMETRY"))
                    {
                        var shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
                        if (shader != null) geomShaders.Add(shader.name);
                    }
                }
                catch { }
            }

            if (geomShaders.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("### Geometry Shader Usage");
                sb.AppendLine("| Feature | Required By | Supported? |");
                sb.AppendLine("| :--- | :--- | :--- |");
                foreach (var s in geomShaders)
                    sb.AppendLine($"| Geometry Shaders | {s} | {(SystemInfo.supportsGeometryShaders ? "✓" : "✗ NOT SUPPORTED on target")} |");
            }

            sb.AppendLine();
            sb.AppendLine($"---\nGenerated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        }

        // ─── Mode F ─────────────────────────────────────────────────────────────

        static void BuildComputeShaderAudit(StringBuilder sb)
        {
            sb.AppendLine("# Compute Shaders");
            sb.AppendLine();

            string[] guids = AssetDatabase.FindAssets("t:ComputeShader");
            if (guids.Length == 0)
            {
                sb.AppendLine("_No ComputeShader assets found in project._");
                sb.AppendLine();
                sb.AppendLine($"---\nGenerated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                return;
            }

            sb.AppendLine($"## Compute Shaders ({guids.Length})");
            sb.AppendLine();
            sb.AppendLine("| Asset | Kernels | Status |");
            sb.AppendLine("| :--- | :--- | :--- |");

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var cs = AssetDatabase.LoadAssetAtPath<ComputeShader>(path);
                if (cs == null) continue;

                // Parse kernel names from source
                var kernelNames = new List<string>();
                try
                {
                    string src = File.ReadAllText(path);
                    foreach (var line in src.Split('\n'))
                    {
                        string trimmed = line.Trim();
                        if (trimmed.StartsWith("#pragma kernel"))
                        {
                            string kernelName = trimmed.Substring("#pragma kernel".Length).Trim().Split(' ')[0];
                            kernelNames.Add(kernelName);
                        }
                    }
                }
                catch { }

                string status = "✓ Compiled";
                var kernelDetails = new List<string>();
                if (kernelNames.Count > 0)
                {
                    foreach (var kernelName in kernelNames)
                    {
                        try
                        {
                            int kernelIdx = cs.FindKernel(kernelName);
                            cs.GetKernelThreadGroupSizes(kernelIdx, out uint x, out uint y, out uint z);
                            kernelDetails.Add($"{kernelName}({x},{y},{z})");
                        }
                        catch
                        {
                            kernelDetails.Add($"{kernelName}(?)");
                            status = "✗ Compile Error";
                        }
                    }
                }
                else
                {
                    status = "✗ No kernels found";
                }

                string kernelsStr = kernelDetails.Count > 0 ? string.Join(", ", kernelDetails) : "—";
                sb.AppendLine($"| {path} | {kernelsStr} | {status} |");
            }

            sb.AppendLine();
            sb.AppendLine($"---\nGenerated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        }
    }
}

#pragma warning restore CS0618
