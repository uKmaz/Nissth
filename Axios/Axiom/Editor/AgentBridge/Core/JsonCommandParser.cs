using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
#if AXIOM_HAS_NEWTONSOFT
using Newtonsoft.Json.Linq;
#endif

namespace Axiom.Editor.AgentBridge.Core
{
    [Serializable]
    public struct BridgeCommand
    {
        public string tool;
        public string mode;
        public CommandScope scope;
        public CommandOutput output;
        public string contextId;
    }

    [Serializable]
    public struct CommandScope
    {
        public string rootPath;
        public string[] objectNames;
        public string tagFilter;
        public string layerFilter;
        public string componentFilter;
        public string assetPath;
        public string assetExtension;
        public int maxDepth;
        public string sceneName;
    }

    [Serializable]
    public struct CommandOutput
    {
        public string format;
        public string destination;
        public string fileName;
    }

    /// <summary>
    /// Parses and validates JSON command strings for the AgentBridgeGateway.
    /// Implements the command schema defined in project_instructions.md Section 2.
    /// </summary>
    public static class JsonCommandParser
    {
        private static readonly HashSet<string> s_ValidTools = new HashSet<string>(StringComparer.Ordinal)
        {
            // Diagnostic tools
            "hierarchy_lens", "log_mirror", "component_inspector", "reference_scanner",
            "project_cartographer", "scene_diff", "smart_search", "settings_reporter",
            "script_analyzer", "prefab_auditor", "ui_toolkit_inspector", "test_runner",
            "physics_reporter", "animation_inspector", "audio_reporter", "navmesh_inspector",
            "shader_auditor", "render_auditor", "accessibility_validator", "ci_sweep",
            // Action tools
            "scene_actions", "component_actions", "asset_actions", "wiring_utility",
            "settings_actions", "render_actions", "build_profile_actions",
            "package_manager_actions", "play_mode_actions", "screen_capture_actions",
            "prefab_actions", "input_simulation_actions", "build_pipeline_hooks",
            "multiplayer_actions", "vision_analysis", "sentis_actions"
        };

        /// <summary>
        /// Parses a JSON string into a BridgeCommand.
        /// Returns null if parsing fails, with error details in the out parameter.
        /// </summary>
        public static BridgeCommand? Parse(string json, out string error)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                error = "JSON string is null or empty.";
                return null;
            }

#if AXIOM_HAS_NEWTONSOFT
            try
            {
                var obj = JObject.Parse(json);
                var cmd = new BridgeCommand();

                cmd.tool = obj["tool"]?.ToString();
                if (string.IsNullOrEmpty(cmd.tool))
                {
                    error = "Missing required field: 'tool'";
                    return null;
                }

                cmd.mode      = obj["mode"]?.ToString() ?? "";
                cmd.contextId = obj["context_id"]?.ToString() ?? "";

                var scope = obj["scope"];
                if (scope != null)
                {
                    cmd.scope = new CommandScope
                    {
                        rootPath        = scope["root_path"]?.ToString() ?? "",
                        tagFilter       = scope["tag_filter"]?.ToString() ?? "",
                        layerFilter     = scope["layer_filter"]?.ToString() ?? "",
                        componentFilter = scope["component_filter"]?.ToString() ?? "",
                        assetPath       = scope["asset_path"]?.ToString() ?? "",
                        assetExtension  = scope["asset_extension"]?.ToString() ?? "",
                        maxDepth        = scope["max_depth"]?.Value<int>() ?? -1,
                        sceneName       = scope["scene_name"]?.ToString() ?? "",
                        objectNames     = scope["object_names"]?.ToObject<string[]>() ?? new string[0]
                    };
                }
                else
                {
                    cmd.scope = new CommandScope { maxDepth = -1 };
                }

                var output = obj["output"];
                if (output != null)
                {
                    cmd.output = new CommandOutput
                    {
                        format      = output["format"]?.ToString() ?? "markdown",
                        destination = output["destination"]?.ToString() ?? "file",
                        fileName    = output["file_name"]?.ToString() ?? ""
                    };
                }
                else
                {
                    cmd.output = new CommandOutput { format = "markdown", destination = "file", fileName = "" };
                }

                error = null;
                return cmd;
            }
            catch (Exception ex)
            {
                error = $"JSON parse error: {ex.Message}";
                return null;
            }
#else
            // Minimal hand-rolled extractor — handles flat tool+mode only.
            // Nested scope/output objects are not parsed in fallback mode.
            // Install com.unity.nuget.newtonsoft-json for full schema support.
            try
            {
                string tool = ExtractStringValue(json, "tool");
                if (string.IsNullOrEmpty(tool))
                {
                    error = "Missing required field: 'tool'. Note: full schema parsing requires com.unity.nuget.newtonsoft-json.";
                    return null;
                }

                var cmd = new BridgeCommand
                {
                    tool      = tool,
                    mode      = ExtractStringValue(json, "mode") ?? "",
                    contextId = ExtractStringValue(json, "context_id") ?? "",
                    scope     = new CommandScope { maxDepth = -1 },
                    output    = new CommandOutput { format = "markdown", destination = "file", fileName = "" }
                };

                // Best-effort: extract destination from flat output object
                string dest = ExtractStringValue(json, "destination");
                if (!string.IsNullOrEmpty(dest))
                    cmd.output = new CommandOutput { format = "markdown", destination = dest, fileName = "" };

                error = null;
                return cmd;
            }
            catch (Exception ex)
            {
                error = $"Fallback parse error: {ex.Message}. Install com.unity.nuget.newtonsoft-json for full schema support.";
                return null;
            }
#endif
        }

        /// <summary>
        /// Validates that the tool name is recognized by the gateway.
        /// Returns null if valid, or an error message string if not.
        /// </summary>
        public static string Validate(BridgeCommand cmd)
        {
            if (!s_ValidTools.Contains(cmd.tool))
                return $"Unknown tool: '{cmd.tool}'. Valid tools: {string.Join(", ", s_ValidTools)}";
            return null;
        }

#if !AXIOM_HAS_NEWTONSOFT
        private static string ExtractStringValue(string json, string key)
        {
            var pattern = "\"" + Regex.Escape(key) + "\"\\s*:\\s*\"([^\"]*)\"";
            var m = Regex.Match(json, pattern);
            return m.Success ? m.Groups[1].Value : null;
        }
#endif
    }
}
