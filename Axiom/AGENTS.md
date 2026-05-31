# Axiom — Project Instructions

> **For AI agents operating in this Unity 6 workspace.**
> Read `.cursorrules` for the full API reference. This file covers the architectural context,
> JSON command schema, and implementation template that `.cursorrules` references but does not contain.

---

## 1. Diagnostic Bridge Philosophy

This project uses the **Axiom Diagnostic Bridge** — a token-efficient layer that replaces expensive MCP browsing with bespoke editor script reports. Instead of the agent searching a dark room with a flashlight (MCP browsing), it reads a neatly printed map (editor script reports).

**Fundamental Workflow:**
```
Report → Execute → Verify
```

1. **Report:** Run a Diagnostic Bridge tool. Read the output from `AgentReports/`.
2. **Execute:** Write/edit code or modify the scene based on the report.
3. **Verify:** Run a post-flight diagnostic to confirm changes and detect regressions.

**Why this matters:**

| Approach | Token Cost | Accuracy | Multi-Turn Loops |
|:---|:---|:---|:---|
| MCP Browsing (agent crawls hierarchy) | High — raw JSON of entire scene tree | Prone to hallucinated clicks and missed objects | Many — "I see X, now let me check Y, then Z" |
| Diagnostic Bridge (agent reads report) | Low — clean formatted output, only what's needed | 100% — code-driven reflection, no guessing | Minimal — X, Y, and Z delivered in one report |

---

## 2. JSON Command Gateway

Axiom provides a JSON command gateway (`AgentBridgeGateway.cs`) that accepts structured commands and routes them to the correct tool. This is the primary interface for MCP servers and external agents.

### 2.1 How to Call the Gateway

**From any MCP server with execute_script:**

```csharp
AgentBridgeGateway.Execute(@"{
    ""tool"": ""hierarchy_lens"",
    ""mode"": ""components"",
    ""scope"": { ""root_path"": ""Player/"" },
    ""output"": { ""destination"": ""return"" }
}");
```

**Response format:**
- `destination: "file"` → returns the absolute file path written to `AgentReports/`
- `destination: "return"` → returns the report content directly as a string
- `destination: "console"` → prints to the Unity Console, returns the file path
- On error → returns a JSON error object: `{"error": "description"}`

### 2.2 Fallback: Direct C# Calls

For advanced usage or when the gateway doesn't expose a specific parameter combination, tools can still be called directly via `execute_script`:

```csharp
HierarchyLens.GenerateReport(HierarchyMode.Components, rootPath: "Player/");
```

This requires compilation of a temp script. The gateway is preferred for standard operations because it skips the compile cycle entirely.

### 2.3 Command Schema Reference

The gateway accepts commands following this schema. See `AgentBridgeGateway.cs` and `JsonCommandParser.cs` in `Assets/Axiom/Editor/AgentBridge/Core/` for the implementation.

```json
{
  "tool": "string (required) — tool name, see valid values below",
  "mode": "string — mode name or letter shortcode (e.g. 'components' or 'b')",
  "context_id": "string (optional) — state tracking across calls",
  "scope": {
    "root_path": "string — hierarchy breadcrumb path (e.g. 'Managers/PlayerSystems')",
    "object_names": ["array of string — specific GameObject names to target"],
    "tag_filter": "string",
    "layer_filter": "string",
    "component_filter": "string — component type name",
    "asset_path": "string — project-relative asset path",
    "asset_extension": "string — file extension filter (e.g. '.cs', '.mat')",
    "max_depth": "integer — recursion limit (-1 = unlimited)",
    "scene_name": "string — target scene in multi-scene setups"
  },
  "output": {
    "format": "string — 'markdown' | 'json' | 'flat_text' (default: 'markdown')",
    "destination": "string — 'file' | 'console' | 'return' (default: 'file')",
    "file_name": "string — custom filename without extension"
  }
}
```

**Note:** The `format` field is parsed and stored but the current gateway implementation
always returns reports in Markdown format regardless of this setting. The field is reserved
for future use when JSON and flat_text output formatters are added to `OutputWriter`.

### 2.4 Unity MCP Native Access

When `com.unity.ai.assistant@2.0+` is installed (currently installed), Axiom registers 5 native MCP tools
with Unity's built-in MCP bridge. External AI clients (Cursor, Claude Code, Windsurf, etc.)
see these tools automatically without any configuration.

The primary tool is `Axiom_Gateway`, which accepts the same JSON contract described in
Section 2.3 and forwards it to `AgentBridgeGateway.Execute()`.

This means the same command works three ways:
1. **Unity MCP** (native): AI client calls `Axiom_Gateway` with JSON payload
2. **execute_script** (fallback): `AgentBridgeGateway.Execute(jsonString)`
3. **Direct C#** (advanced): `HierarchyLens.GenerateReport(HierarchyMode.Components, rootPath: "Player/")`

All three paths reach the same underlying tools and produce the same reports.

**Valid diagnostic tool names:**
`hierarchy_lens`, `log_mirror`, `component_inspector`, `reference_scanner`,
`project_cartographer`, `scene_diff`, `smart_search`, `settings_reporter`,
`script_analyzer`, `prefab_auditor`, `ui_toolkit_inspector`, `test_runner`,
`physics_reporter`, `animation_inspector`, `audio_reporter`, `navmesh_inspector`,
`shader_auditor`, `render_auditor`, `accessibility_validator`, `ci_sweep`

**Valid action tool names:**
`scene_actions`, `component_actions`, `asset_actions`, `wiring_utility`,
`settings_actions`, `render_actions`, `build_profile_actions`,
`package_manager_actions`, `play_mode_actions`, `screen_capture_actions`,
`prefab_actions`, `input_simulation_actions`, `build_pipeline_hooks`,
`multiplayer_actions`, `vision_analysis`, `sentis_actions`

---

## 3. Token-Lean Implementation Template

**Every implementation task MUST follow this structure.** This template forces the agent into the Report → Execute → Verify loop and prevents wasteful MCP browsing.

```markdown
# Phase [X]: [Feature Name] — Implementation Plan

## 1. Pre-Flight Diagnostic (The "Map")
- **Action:** Execute Diagnostic Bridge: `<tool>` with mode `<mode>`,
  scoped to `root_path: "<path>"`.
- **Read:** `AgentReports/<tool>_<timestamp>.md`
- **Goal:** Verify [target] exists at expected path with [component] attached.
- **If missing:** Create required GameObjects/components per Expected State below.

### Expected Hierarchy State
| Target Path | Required Components | Key Property Values |
|:---|:---|:---|
| Managers/PlayerSystems/InputHandler | PlayerInput, InputManager | InputActions: "Assets/Input/PlayerActions.inputactions" |
| Environment/TerrainStreamer | GPUStreamer, MeshRenderer (Static) | StreamRadius: 500, LODLevels: 4 |

## 2. Execution (The "Work")
- **Scripting:** Edit `[FileName].cs` — [specific method or line description].
- **Wiring:** Do NOT use MCP for inspector wiring. Write a temporary
  `[Feature]SetupUtility.cs` [MenuItem] script to link references via
  SerializedObject/SerializedProperty.
- **Scene Changes:** [Specific GameObjects to create/modify with exact paths].

## 3. Post-Flight Verification (The "Proof")
- **Action:** Execute Diagnostic Bridge: `scene_diff` mode `D` (Property Diff),
  comparing snapshot `"before_[feature]"` with current.
- **Action:** Execute `log_mirror` mode `A` (Errors Only).
- **Success Criteria:**
  - Scene diff shows expected changes and no unexpected changes.
  - Zero errors in log mirror.
  - [Specific validation, e.g., "Console must output: All References Bound: TRUE"]

## 4. Cleanup
- Delete temporary Editor scripts created in Step 2.
- Clear scene diff snapshots if no longer needed.
- Update `AgentReports/StatusUpdate.md` with summary of changes.
```

### Template Rules
1. Every implementation phase document must follow this template.
2. Every task must include an **Expected Hierarchy State** table so the agent verifies rather than discovers.
3. Every task must specify the **exact Diagnostic Bridge tool, mode, and scope** for pre-flight and post-flight checks.
4. Wiring tasks must specify **source object path**, **target object path**, **component type**, and **property name** — leaving nothing for the agent to guess.

---

## 4. Editor Mode Behavior Matrix

Tools behave differently depending on editor state. The agent must track this.

| Tool Category | Edit Mode | Play Mode | Prefab Mode |
|:---|:---|:---|:---|
| Object Search | Whole active scene | Runtime-instantiated objects | Isolated prefab hierarchy |
| Transform | Modifies .unity/.prefab YAML (persists) | Modifies runtime memory (lost on exit) | Modifies prefab source asset |
| Console | Compiler errors + import warnings | Runtime exceptions + logic logs | Prefab-specific issues |
| Physics | Visualizes colliders and settings | Queries Physics.Raycast and collisions | Not applicable |
| GPU Rendering | Audits GPU Resident Drawer, occlusion | Captures real-time GPU metrics | Not applicable |
| Build Profiles | Full read/write | Read-only | Not applicable |
| Diagnostics | Full access — all modes | Read-only (live state may differ) | Scoped to prefab contents |

---

## 5. Token Efficiency Rules

1. **Breadcrumb Addressing:** Always refer to GameObjects by full hierarchy path.
2. **Use the Narrowest Mode:** Start with Mode A, escalate only if needed.
3. **Batch Diagnostics:** One broader call is cheaper than many narrow MCP round trips.
4. **Avoid Full-Scene Snapshots:** Never Mode F on entire scene unless debugging unknown issues.

---

## 6. Compilation Verification Protocol

Unity compiles C# scripts internally when it detects file changes. External tools (IDE linters, language servers, terminals) do NOT reflect Unity's actual compilation state. The only reliable way to check compilation is from inside the Unity editor process. If Unity does not detect file changes automatically (common when the editor is not focused), trigger recompilation by calling AssetDatabase.Refresh() via the gateway or by switching focus to the Unity Editor.

### Required Workflow

After completing a task or step that involved code changes (before running verification or moving to the next task):

1. **Wait** for Unity to finish compiling. The MCP server may expose a way to check `EditorApplication.isCompiling` — poll it, or wait a reasonable duration (10-20 seconds for small changes, longer for large projects).

2. **Verify** by running a temp script that calls LogMirror:

    ```csharp
    // Save to: Assets/Axiom/Editor/AgentBridge/Temp/Temp_CompileCheck.cs
    using Axiom.Editor.AgentBridge.Diagnostics;
    public class Temp_CompileCheck
    {
        public static void Execute()
        {
            LogMirror.GenerateReport(LogMirror.LogMirrorMode.CompilationReport);
        }
    }
    ```

3. **Read** the report from `AgentReports/`. Confirm `Status: CLEAN`.

4. **Delete** the temp script when done.

### Temp Script Rules

- **Location:** `Assets/Axiom/Editor/AgentBridge/Temp/`
  This folder is inside the AgentBridge assembly definition scope, so scripts here compile as part of the Axiom assembly and can access all Axiom tools.

- **Naming:** Prefix with `Temp_` (e.g., `Temp_CompileCheck.cs`, `Temp_Verify.cs`, `Temp_TaskA.cs`).

- **Cleanup:** Always delete temp scripts after execution. Do not leave them in the project.

- **Never place temp scripts at:**
  - The project root — Unity cannot compile scripts outside `Assets/`.
  - Directly under `Assets/` without an asmdef — they won't have access to the `Axiom.Editor.AgentBridge` namespace.
  - Any folder outside the AgentBridge asmdef scope.

### What NOT to Trust

- IDE syntax highlighting or error squiggles (OmniSharp, C# language server)
- Terminal commands (`dotnet build`, `msbuild`)
- The absence of visible errors in any tool other than Unity's own compiler

These tools do not know about Unity's assembly definitions, `#if` preprocessor defines, version defines from packages, or the actual compilation pipeline. Code can show zero linter errors and still fail to compile in Unity.

---

## 7. Project Structure

```
ProjectRoot/
├── .cursorrules                         ← Agent API reference (Cursor IDE)
├── project_instructions.md              ← This file (schemas + templates)
├── ImplementationPlans/                 ← Phase-specific implementation MDs
├── AgentReports/                        ← Diagnostic output (auto-created)
│   ├── Snapshots/                       ← SceneDiff snapshots
│   ├── Screenshots/                     ← ScreenCaptureActions output
│   └── StatusUpdate.md                  ← Current state summary
├── Assets/
│   └── Axiom/
│       └── Editor/
│           ├── AgentBridge/
│           │   ├── Core/                ← 8 files: 6 utilities + JsonCommandParser + AgentBridgeGateway
│           │   ├── Diagnostics/         ← 20 diagnostic tools
│           │   ├── Actions/             ← 16 action tools
│           │   └── AgentBridge.asmdef
│           ├── WorkspaceRules/          ← Embedded copies for package export
│           │   ├── cursorrules.txt
│           │   └── project_instructions.txt
│           └── Installer/
│               ├── AxiomInstaller.cs
│               ├── AxiomExporter.cs
│               ├── AxiomPostImportCheck.cs
│               └── Axiom.Editor.Installer.asmdef
```

Total: 44 .cs source files + 1 asmdef (AgentBridge) + 3 installer scripts + 1 installer asmdef

---

## 8. File Roles

| File | Read By | Contains |
|:---|:---|:---|
| `.cursorrules` | Cursor IDE (auto-loaded) | Core rules + complete tool API reference with usage examples |
| `project_instructions.md` | Claude, Gemini, other agents (manual or MCP) | Command schema, implementation template, architectural context |
| `AgentReports/StatusUpdate.md` | Any agent resuming work | Current project state, last changes, verification results |
| `ImplementationPlans/*.md` | Any agent executing a phase | Step-by-step task following the Token-Lean Template |
