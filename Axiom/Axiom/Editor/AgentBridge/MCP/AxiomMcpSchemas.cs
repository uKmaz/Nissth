#if AXIOM_HAS_UNITY_ASSISTANT
using Unity.AI.MCP.Editor.ToolRegistry;

namespace Axiom.Editor.AgentBridge.Mcp
{
    /// <summary>
    /// Centralized custom schemas for flexible JObject-based MCP tools.
    /// Keep schema definitions here so the tool bodies stay focused on routing and verification.
    /// </summary>
    public static class AxiomMcpSchemas
    {
        [McpSchema("Axiom_Gateway")]
        public static object GetAxiomGatewaySchema()
        {
            return new
            {
                type = "object",
                description = "Canonical Axiom gateway payload. Mirrors the existing AgentBridgeGateway JSON contract.",
                properties = new
                {
                    tool = new
                    {
                        type = "string",
                        description = "Axiom internal tool name such as hierarchy_lens, reference_scanner, component_inspector, scene_diff, log_mirror, ci_sweep, scene_actions, component_actions, and other gateway-supported operations."
                    },
                    mode = new
                    {
                        type = "string",
                        description = "Mode or operation name for the selected internal tool."
                    },
                    context_id = new
                    {
                        type = "string",
                        description = "Optional context identifier for stateful workflows."
                    },
                    scope = new
                    {
                        type = "object",
                        description = "Scope object used to narrow the operation and reduce token cost.",
                        properties = new
                        {
                            root_path = new { type = "string", description = "Breadcrumb hierarchy path such as Managers/PlayerSystems/InputHandler." },
                            object_names = new { type = "array", items = new { type = "string" }, description = "Specific object names or operation-specific string payload slots already understood by the gateway/tool." },
                            tag_filter = new { type = "string", description = "Optional tag filter." },
                            layer_filter = new { type = "string", description = "Optional layer filter." },
                            component_filter = new { type = "string", description = "Optional component type filter or operation-specific type slot." },
                            asset_path = new { type = "string", description = "Project-relative asset or folder path." },
                            asset_extension = new { type = "string", description = "Optional asset extension filter." },
                            max_depth = new { type = "integer", description = "Optional traversal depth. -1 means unlimited in Axiom's convention." },
                            scene_name = new { type = "string", description = "Optional scene label or scene scope where supported." }
                        },
                        additionalProperties = true
                    },
                    output = new
                    {
                        type = "object",
                        description = "Output routing strategy. Prefer destination=return for small outputs and destination=file for large audits.",
                        properties = new
                        {
                            format = new
                            {
                                type = "string",
                                @enum = new[] { "markdown", "json", "flat_text" },
                                description = "Requested output format. Mirrors the gateway contract. Actual behavior still follows current gateway capabilities."
                            },
                            destination = new
                            {
                                type = "string",
                                @enum = new[] { "file", "console", "return" },
                                description = "file writes to AgentReports, console logs to Unity Console, return asks the gateway to return content directly when supported."
                            },
                            file_name = new
                            {
                                type = "string",
                                description = "Optional custom output file name."
                            }
                        },
                        additionalProperties = false
                    }
                },
                required = new[] { "tool" },
                additionalProperties = false
            };
        }

        [McpSchema("Axiom_Verify")]
        public static object GetAxiomVerifySchema()
        {
            return new
            {
                type = "object",
                description = "Coarse verification payload. Keeps verification explicit and predictable instead of turning it into a second generic dispatcher.",
                properties = new
                {
                    operation = new
                    {
                        type = "string",
                        @enum = new[]
                        {
                            "compilation",
                            "errors",
                            "scene_diff_compare_current",
                            "scene_diff_compare",
                            "status_update"
                        },
                        description = "Verification operation to run."
                    },
                    label = new
                    {
                        type = "string",
                        description = "Snapshot label for scene_diff_compare_current."
                    },
                    label_a = new
                    {
                        type = "string",
                        description = "First snapshot label for scene_diff_compare."
                    },
                    label_b = new
                    {
                        type = "string",
                        description = "Second snapshot label for scene_diff_compare."
                    },
                    root_path = new
                    {
                        type = "string",
                        description = "Optional hierarchy scope for scene diff compare-current."
                    }
                },
                required = new[] { "operation" },
                additionalProperties = false
            };
        }
    }
}
#endif
