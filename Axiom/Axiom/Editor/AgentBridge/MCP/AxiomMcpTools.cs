#if AXIOM_HAS_UNITY_ASSISTANT
using System;
using System.IO;
using Newtonsoft.Json.Linq;
using Unity.AI.MCP.Editor.Helpers;
using Unity.AI.MCP.Editor.ToolRegistry;
using UnityEditor;
using UnityEngine;
using Axiom.Editor.AgentBridge.Core;
using Axiom.Editor.AgentBridge.Diagnostics;

namespace Axiom.Editor.AgentBridge.Mcp
{
    /// <summary>
    /// Tiny Unity 2.0-native MCP façade for Axiom.
    ///
    /// Design goals:
    /// - keep public MCP surface minimal (5 coarse tools)
    /// - preserve Axiom's existing JSON gateway contract
    /// - route most work into reports, not raw state payloads
    /// - keep direct C# fallback for complex parameter shapes
    /// </summary>
    public static class AxiomMcpTools
    {
        [McpTool(
            "Axiom_Gateway",
            "Run a coarse Axiom command through the existing AgentBridgeGateway JSON contract. " +
            "Accepts the canonical Axiom payload with tool, mode, context_id, scope, and output. " +
            "Best default for almost all diagnostics and gateway-friendly actions. ",
            EnabledByDefault = true
        )]
        public static object AxiomGateway(JObject parameters)
        {
            if (parameters == null)
                return Fail("Parameters cannot be null.");

            try
            {
                string json = parameters.ToString();
                string result = AgentBridgeGateway.Execute(json);

                string tool = parameters["tool"]?.ToString() ?? string.Empty;
                string mode = parameters["mode"]?.ToString() ?? string.Empty;
                string destination = parameters["output"]?["destination"]?.ToString() ?? "file";

                return Ok(new
                {
                    tool,
                    mode,
                    destination,
                    result
                });
            }
            catch (Exception ex)
            {
                return Fail($"Axiom gateway execution failed: {ex.Message}");
            }
        }

        [McpTool("Axiom_Status", "Return Unity/Axiom editor state and report roots.", EnabledByDefault = true)]
        public static object AxiomStatus()
        {
            try
            {
                return Ok(new
                {
                    unityVersion = Application.unityVersion,
                    isCompiling = EditorApplication.isCompiling,
                    isPlaying = EditorApplication.isPlaying,
                    isPaused = EditorApplication.isPaused,
                    reportsRoot = OutputWriter.GetReportsRoot(),
                    snapshotsRoot = OutputWriter.SnapshotsRoot,
                    gatewayAvailable = true,
                    editorMode = GetEditorModeLabel()
                });
            }
            catch (Exception ex)
            {
                return Fail($"Failed to read Axiom status: {ex.Message}");
            }
        }

        [McpTool("Axiom_ReadReport", "Read an Axiom report from AgentReports by relative path, report name, or latest prefix.", EnabledByDefault = true)]
        public static object AxiomReadReport(AxiomReadReportParams parameters)
        {
            if (parameters == null)
                return Fail("Parameters cannot be null.");

            try
            {
                string reportsRoot = OutputWriter.GetReportsRoot();
                string targetPath = ResolveReportPath(reportsRoot, parameters);
                if (string.IsNullOrEmpty(targetPath) || !File.Exists(targetPath))
                    return Fail("Could not resolve report path.", "Provide relativePath, reportName, or latestPrefix.");

                string content = File.ReadAllText(targetPath);
                if (parameters.MaxChars > 0 && content.Length > parameters.MaxChars)
                    content = content.Substring(0, parameters.MaxChars);

                return Ok(new
                {
                    path = targetPath,
                    content
                });
            }
            catch (Exception ex)
            {
                return Fail($"Failed to read report: {ex.Message}");
            }
        }

        [McpTool("Axiom_Verify", "Run a coarse verification routine such as compilation check, log check, scene diff comparison or status_update..", EnabledByDefault = true)]
        public static object AxiomVerify(JObject parameters)
        {
            if (parameters == null)
                return Fail("Parameters cannot be null.");

            try
            {
                string operation = parameters["operation"]?.ToString()?.Trim().ToLowerInvariant();
                switch (operation)
                {
                    case "compilation":
                    {
                        // Trigger asset refresh so Unity detects any file changes
                        // written by the agent since the last compilation.
                        // Without this, the report reflects the PREVIOUS compile state.
                        AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);

                        // If compilation was triggered, spin-wait for it to finish
                        // (bounded by timeout to avoid hanging the MCP call).
                        if (EditorApplication.isCompiling)
                        {
                            const int timeoutMs = 30000;
                            var sw = System.Diagnostics.Stopwatch.StartNew();
                            while (EditorApplication.isCompiling && sw.ElapsedMilliseconds < timeoutMs)
                                System.Threading.Thread.Sleep(200);
                        }

                        string path = LogMirror.GenerateReport(LogMirror.LogMirrorMode.CompilationReport);
                        return Ok(new { operation, reportPath = path });
                    }
                    case "errors":
                    {
                        string path = LogMirror.GenerateReport(LogMirror.LogMirrorMode.ErrorsOnly);
                        return Ok(new { operation, reportPath = path });
                    }
                    case "scene_diff_compare_current":
                    {
                        string label = parameters["label"]?.ToString();
                        string rootPath = parameters["root_path"]?.ToString();
                        if (string.IsNullOrWhiteSpace(label))
                            return Fail("scene_diff_compare_current requires 'label'.");

                        string path = SceneDiff.Execute(
                            SceneDiff.SceneDiffOperation.CompareCurrent,
                            SceneDiff.SceneDiffMode.StructuralDiff,
                            label: label,
                            rootPath: string.IsNullOrWhiteSpace(rootPath) ? null : rootPath);

                        return Ok(new { operation, reportPath = path });
                    }
                    case "scene_diff_compare":
                    {
                        string labelA = parameters["label_a"]?.ToString();
                        string labelB = parameters["label_b"]?.ToString();

                        if (string.IsNullOrWhiteSpace(labelA) || string.IsNullOrWhiteSpace(labelB))
                            return Fail("scene_diff_compare requires 'label_a' and 'label_b'.");

                        string path = SceneDiff.Execute(
                            SceneDiff.SceneDiffOperation.Compare,
                            SceneDiff.SceneDiffMode.StructuralDiff,
                            labelA: labelA,
                            labelB: labelB);

                        return Ok(new { operation, labelA, labelB, report = path });
                    }

                    case "status_update":
                    {
                        string reportsRoot = OutputWriter.GetReportsRoot();
                        string targetPath = FindNewestFile(reportsRoot, "status_update");
                        if (string.IsNullOrWhiteSpace(targetPath))
                        {
                            string conventional = Path.Combine(reportsRoot, "StatusUpdate.md");
                            targetPath = File.Exists(conventional) ? conventional : null;
                        }

                        if (string.IsNullOrWhiteSpace(targetPath))
                        {
                            return Fail(
                                "Could not find StatusUpdate report.",
                                "Generate or update StatusUpdate.md first, or use Axiom_ReadReport with a known report path.");
                        }

                        return Ok(new { operation, report = targetPath });
                    }
                    default:
                        return Fail(
                            $"Unknown verify operation: '{operation}'.",
                            "Supported operations: compilation, errors, scene_diff_compare_current");
                }
            }
            catch (Exception ex)
            {
                return Fail($"Verification failed: {ex.Message}");
            }
        }

        [McpTool("Axiom_Rules", "Return compact Axiom operating rules for external agents.", EnabledByDefault = true)]
        public static object AxiomRules()
        {
            return Ok(new
            {
                core_loop = "Report → Execute → Verify",
                gateway_tool = "Axiom_Gateway is the canonical entry point. Send JSON with: tool, mode, scope, output.",
                rules = new[]
                {
                    "Prefer report outputs over raw browsing.",
                    "Use the narrowest scope and cheapest mode first.",
                    "Use destination=return for small outputs, destination=file for large audits.",
                    "When destination=file, use Axiom_ReadReport for follow-up reads.",
                    "After code changes, verify compilation via Axiom_Verify with operation=compilation.",
                    "Temp scripts belong under Assets/Axiom/Editor/AgentBridge/Temp/.",
                    "Wait for Unity compilation before running newly created temp scripts.",
                    "If a payload shape is too awkward for the gateway, use the exact direct C# fallback shown in the error hint.",
                    "Treat Unity compile state as authoritative — do not trust IDE linter squiggles."
                }
            });
        }

        private static string GetEditorModeLabel()
        {
            if (EditorApplication.isPlaying)
                return EditorApplication.isPaused ? "PlayModePaused" : "PlayMode";
            return "EditMode";
        }

        private static string ResolveReportPath(string reportsRoot, AxiomReadReportParams parameters)
        {
            if (!string.IsNullOrWhiteSpace(parameters.RelativePath))
                return Path.Combine(reportsRoot, parameters.RelativePath);

            if (!string.IsNullOrWhiteSpace(parameters.ReportName))
                return Path.Combine(reportsRoot, parameters.ReportName);

            if (!string.IsNullOrWhiteSpace(parameters.LatestPrefix) && Directory.Exists(reportsRoot))
            {
                var files = new DirectoryInfo(reportsRoot)
                    .GetFiles($"{parameters.LatestPrefix}*", SearchOption.AllDirectories);

                Array.Sort(files, (a, b) => b.LastWriteTimeUtc.CompareTo(a.LastWriteTimeUtc));
                if (files.Length > 0)
                    return files[0].FullName;
            }

            return null;
        }

        private static string FindNewestFile(string directory, string prefix)
        {
            if (!Directory.Exists(directory))
                return null;

            var files = new DirectoryInfo(directory).GetFiles($"{prefix}*", SearchOption.AllDirectories);
            if (files.Length == 0)
                return null;

            Array.Sort(files, (a, b) => b.LastWriteTimeUtc.CompareTo(a.LastWriteTimeUtc));
            return files[0].FullName;
        }

        private static object Ok(object data)
        {
            return Response.Success("ok", data);
        }

        private static object Fail(string error, string hint = null)
        {
            return hint != null
                ? Response.Error(error, new { hint })
                : Response.Error(error);
        }
    }

    [Serializable]
    public sealed class AxiomReadReportParams
    {
        [McpDescription("Project-relative report path inside AgentReports. Optional if reportName or latestPrefix is provided.")]
        public string RelativePath { get; set; }

        [McpDescription("Exact report file name inside AgentReports. Optional.")]
        public string ReportName { get; set; }

        [McpDescription("If provided, resolves the newest report whose file name starts with this prefix. Optional.")]
        public string LatestPrefix { get; set; }

        [McpDescription("Maximum number of characters to return.", Default = 20000)]
        public int MaxChars { get; set; } = 20000;
    }
}
#endif
