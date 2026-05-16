# Axiom — Claude Code Project Instructions

> Unity 6 workspace with the Axiom Diagnostic Bridge framework.
> Unity MCP (`com.unity.ai.assistant@2.2+`) is installed — Axiom MCP tools are available natively.
> This file is the COMPLETE reference. Do NOT re-read `.cursorrules` or `project_instructions.md` — all content is here.

---

## Diagnostic Bridge Philosophy

This project uses the **Axiom Diagnostic Bridge** — a token-efficient layer that replaces expensive MCP browsing with bespoke editor script reports. Instead of the agent searching a dark room with a flashlight (MCP browsing), it reads a neatly printed map (editor script reports).

| Approach | Token Cost | Accuracy | Multi-Turn Loops |
|:---|:---|:---|:---|
| MCP Browsing (agent crawls hierarchy) | High — raw JSON of entire scene tree | Prone to hallucinated clicks and missed objects | Many — "I see X, now let me check Y, then Z" |
| Diagnostic Bridge (agent reads report) | Low — clean formatted output, only what's needed | 100% — code-driven reflection, no guessing | Minimal — X, Y, and Z delivered in one report |

---

## Core Rules

1. **Report First, Act Second.** Before making changes, run the appropriate Axiom diagnostic tool to understand current state. Never modify blindly.
2. **Wiring via Editor Scripts.** Write temporary `[MenuItem]` editor utility scripts for assigning SerializedProperty references. Never use MCP inspector interactions for wiring.
3. **Logging Over Browsing.** Read reports from `AgentReports/` over browsing hierarchy or folders via MCP tools.
4. **Constrain Scans.** Always provide `root_path`, `object_names`, or a filter when using diagnostic tools. Never scan entire scene/project unless explicitly asked.
5. **Validate on Reload.** After script changes, wait for compilation. Run LogMirror CompilationReport to confirm CLEAN status before proceeding.
6. **Use SerializedProperties.** Always use `SerializedObject` for edits to preserve Undo and Prefab overrides.
7. **Use AssetDatabase for Moves.** Never use filesystem operations to move Unity assets.
8. **Prefer Build Profiles.** In Unity 6, use Build Profiles over global PlayerSettings when available.
9. **Modal Awareness.** Track whether editor is in Edit, Play, or Prefab Mode. Play Mode changes do not persist.
10. **State Persistence.** After completing work, update `AgentReports/StatusUpdate.md` with a summary.
11. **Respect Component Dependencies.** Before removing a component, verify no `RequireComponent` attribute on sibling components depends on it. `ComponentActions.RemoveComponent` does this automatically.
12. **Cache Reflection.** All `MethodInfo`/`FieldInfo` lookups for internal APIs must be cached as static fields, not re-reflected per call.
13. **Snapshot Before Major Changes.** Before multi-step operations, save a SceneDiff snapshot for rollback: `SceneDiff.Execute(SceneDiffOperation.Snapshot, label: "before_feature_x")`.
14. **Temp Scripts Location.** All temporary editor scripts go in `Assets/Axiom/Editor/AgentBridge/Temp/`. Delete after use.
15. **Wait for Compilation.** After code changes, wait for Unity to compile before executing any script. Never assume compilation is instant.
16. **Verify Compilation via LogMirror.** After any code change, confirm clean compilation. Proceed only if `Status: CLEAN`. Never trust IDE linter squiggles — only Unity's compiler output is authoritative.
17. **Scene Loading Awareness.** Axiom tools operate on loaded scenes only. If diagnostics return wrong scene data, load the target scene first via `scene_actions` `manage_scene` `load`.
18. **Read Tool Schema Before Unfamiliar Calls.** Do NOT guess mode names or parameter structures. The gateway validates strictly — `additionalProperties: false` means invented parameters are rejected.
19. **No Silent Deviations.** If about to take an action not covered by Axiom, STOP and inform the user. Do not self-approve deviations.

---

## NEVER Use Shell Commands for Unity Exploration

Do NOT use PowerShell, bash, cmd, `grep`, `find`, `cat`, etc. for Unity project discovery, asset searches, scene inspection, or hierarchy browsing. This includes:
- `Get-ChildItem` / `dir` / `ls` to find assets
- `Select-String` / `grep` / `findstr` to search scene YAML files
- `Test-Path` to check asset existence
- `Get-Content` to read `.unity` / `.prefab` / `.asset` files

Unity scene YAML stores references as GUIDs and fileIDs — shell text searches cannot resolve these. **All discovery MUST go through Axiom tools or `Unity_RunCommand` with `AssetDatabase`/`EditorSceneManager` APIs.** There are zero exceptions.

If Axiom returns unexpected results, diagnose using OTHER Axiom tools — do not fall back to shell commands:
- Wrong scene data? → Load the target scene first via `scene_actions` `manage_scene` `load`
- Empty asset results? → Check filter parameters (`component_filter` vs `asset_extension` vs `assetType`)
- Tool returns an error? → Read the error message; the gateway returns descriptive errors
- Still stuck after two attempts? → Read the tool's source file in `Assets/Axiom/Editor/AgentBridge/`

---

## Use the Right Execution Path

- **One-shot logic** (find assets, remove missing scripts, set a property): Use `Unity_RunCommand` with `IRunCommand.Execute()`. Compiles and runs inline — no menu click needed.
- **Axiom diagnostic/action tools**: Use `Axiom_Gateway` (MCP) or `AgentBridgeGateway.Execute(json)` via `execute_script`. One call, no temp file.
- **Persistent utilities** (setup scripts that wire multiple references): Write a `[MenuItem]` script in `Assets/Axiom/Editor/AgentBridge/Temp/`. Requires `EditorApplication.ExecuteMenuItem()` to invoke.

Do NOT write a `[MenuItem]` temp script for one-shot operations — `Unity_RunCommand` exists for exactly this purpose.

---

## Workflow: Report → Execute → Verify

Every task follows: **Pre-Flight Diagnostic → Execution → Post-Flight Verification → Cleanup.**

1. **Report:** Run a diagnostic tool. Read from `AgentReports/`.
2. **Execute:** Write/edit code or modify the scene based on the report.
3. **Verify:** Run post-flight diagnostic to confirm changes, detect regressions.
4. **Cleanup:** Delete temp scripts, clear snapshots if unneeded, update StatusUpdate.md.

---

## Token-Lean Implementation Template

**Every implementation task MUST follow this structure.** This template forces the Report → Execute → Verify loop.

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

## 2. Execution (The "Work")
- **Scripting:** Edit `[FileName].cs` — [specific method or line description].
- **Wiring:** Write a temporary `[Feature]SetupUtility.cs` [MenuItem] script.
- **Scene Changes:** [Specific GameObjects to create/modify with exact paths].

## 3. Post-Flight Verification (The "Proof")
- **Action:** Execute `scene_diff` mode `D`, comparing snapshot with current.
- **Action:** Execute `log_mirror` mode `A` (Errors Only).
- **Success Criteria:**
  - Scene diff shows expected changes and no unexpected changes.
  - Zero errors in log mirror.
  - [Specific validation]

## 4. Cleanup
- Delete temporary Editor scripts.
- Clear scene diff snapshots if no longer needed.
- Update `AgentReports/StatusUpdate.md` with summary of changes.
```

### Template Rules
1. Every implementation phase must follow this template.
2. Every task must include an **Expected Hierarchy State** table.
3. Every task must specify the **exact tool, mode, and scope** for pre-flight and post-flight checks.
4. Wiring tasks must specify **source object path**, **target object path**, **component type**, and **property name**.

---

## Editor Mode Behavior Matrix

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

## Token Efficiency Rules

1. **Breadcrumb Addressing:** Always refer to GameObjects by full hierarchy path.
2. **Use the Narrowest Mode:** Start with Mode A, escalate only if needed.
3. **Batch Diagnostics:** One broader call is cheaper than many narrow MCP round trips.
4. **Avoid Full-Scene Snapshots:** Never Mode F on entire scene unless debugging unknown issues.

---

## Compilation Verification Protocol

Unity compiles C# scripts internally when it detects file changes. External tools (IDE linters, language servers, terminals) do NOT reflect Unity's actual compilation state.

**CRITICAL:** When an external agent writes or edits a .cs file, Unity may NOT detect the change until the editor receives focus or `AssetDatabase.Refresh()` is called. Without a refresh, `Axiom_Verify` compilation check will report the PREVIOUS compile state — showing CLEAN even though new code has NOT been compiled yet.

`Axiom_Verify` with `operation: "compilation"` now handles this automatically: it calls `AssetDatabase.Refresh(ForceUpdate)` before checking, then waits up to 30s for compilation to finish. This ensures the report reflects the state AFTER the agent's changes.

### Required Workflow

After code changes:
1. **Verify** via `Axiom_Verify` with `{"operation": "compilation"}`. The tool automatically triggers `AssetDatabase.Refresh()` and waits for compilation to complete before reporting.
2. **Read** the report. Confirm `Status: CLEAN`.
3. If status is `COMPILING` (timed out), wait and call again. If `HAS_ERRORS`, read the error details and fix.

### Temp Script Rules
- **Location:** `Assets/Axiom/Editor/AgentBridge/Temp/` (inside AgentBridge asmdef scope)
- **Naming:** Prefix with `Temp_`
- **Cleanup:** Always delete after execution
- **Never place at:** Project root, directly under `Assets/`, or outside AgentBridge asmdef scope

### What NOT to Trust
- IDE syntax highlighting or error squiggles (OmniSharp, C# language server)
- Terminal commands (`dotnet build`, `msbuild`)
- The absence of visible errors in any tool other than Unity's own compiler

---

## MCP Tools (Unity AI Assistant)

5 native MCP tools registered via `Assets/Axiom/Editor/AgentBridge/Mcp/`:

| Tool | Purpose |
|:---|:---|
| `Axiom_Gateway` | Primary entry point. Forwards JSON to `AgentBridgeGateway.Execute()`. |
| `Axiom_Status` | Health probe: editor state, compile status, play mode, report paths. No params. |
| `Axiom_ReadReport` | Reads a report from `AgentReports/`. Params: `relativePath` or `reportName` or `latestPrefix`, optional `maxChars`. |
| `Axiom_Verify` | Verification: `compilation`, `errors`, `scene_diff_compare_current`. |
| `Axiom_Rules` | Returns compact Axiom operating rules. No params. |

### When MCP Tools Are NOT Available
Use the direct C# approach:
- Write temp scripts to `Assets/Axiom/Editor/AgentBridge/Temp/`
- Call tools via `AgentBridgeGateway.Execute(json)` or direct static methods
- Read reports from `AgentReports/` via file system

---

## Gateway JSON Schema

The same command works three ways:
1. **Unity MCP** (native): `Axiom_Gateway` with JSON payload
2. **execute_script** (fallback): `AgentBridgeGateway.Execute(jsonString)`
3. **Direct C#** (advanced): `HierarchyLens.GenerateReport(HierarchyMode.Components, rootPath: "Player/")`

```json
{
  "tool": "tool_name (required)",
  "mode": "mode_name_or_letter",
  "context_id": "string (optional)",
  "scope": {
    "root_path": "hierarchy path or scene path",
    "object_names": ["specific GameObjects"],
    "tag_filter": "string",
    "layer_filter": "string",
    "component_filter": "component type name",
    "asset_path": "project-relative asset path",
    "asset_extension": ".ext",
    "max_depth": -1,
    "scene_name": "target scene"
  },
  "output": {
    "format": "markdown|json|flat_text",
    "destination": "file|console|return",
    "file_name": "custom_name"
  }
}
```

Response format:
- `destination: "file"` → returns absolute file path to `AgentReports/`
- `destination: "return"` → returns report content directly
- `destination: "console"` → prints to Unity Console, returns file path
- On error → `{"error": "description"}`

Gateway usage via execute_script:
```csharp
string result = AgentBridgeGateway.Execute(@"{
    ""tool"": ""hierarchy_lens"",
    ""mode"": ""components"",
    ""scope"": { ""root_path"": ""Player/"", ""max_depth"": 3 },
    ""output"": { ""destination"": ""return"" }
}");
```

---

## Common Discovery Patterns

### "Do any [AssetType] assets exist?"
```json
{"tool": "smart_search", "mode": "assets", "scope": {"asset_extension": ".physicMaterial"}}
```

### "Are there null/missing references?"
```json
{"tool": "reference_scanner", "mode": "scene_references"}
```
```json
{"tool": "component_inspector", "mode": "missing_references", "scope": {"root_path": "Player/"}}
```

### "What components are on this GameObject?"
```json
{"tool": "hierarchy_lens", "mode": "components", "scope": {"root_path": "Player/"}}
```
```json
{"tool": "component_inspector", "mode": "property_values", "scope": {"root_path": "Player/", "component_filter": "Rigidbody"}}
```

### "What scenes exist?"
```json
{"tool": "smart_search", "mode": "assets", "scope": {"asset_extension": ".unity"}}
```

### "What's in an unloaded scene?"
```json
{"tool": "scene_actions", "mode": "manage_scene", "scope": {"root_path": "Assets/Scenes/Target.unity", "object_names": ["load"]}}
```
Then inspect with hierarchy_lens.

### "Broken shaders or pink materials?"
```json
{"tool": "reference_scanner", "mode": "material_audit", "scope": {"asset_path": "Assets/Materials"}}
```

### "Physics settings?"
```json
{"tool": "physics_reporter", "mode": "physics_settings"}
```

---

## Diagnostic Tools — Full Reference

### HierarchyLens
Modes: A=Structure (names only), B=Components (names + types), C=ComponentState (all property values), D=SingleComponentFocus (one type across all GOs), E=TransformDetail, F=FullInspectorDump
Scope: rootPath, maxDepth, tagFilter, layerFilter, componentFilter, includeInactive

### LogMirror
Modes: A=ErrorsOnly, B=Warnings, C=TaggedLogs, D=FullStream, E=ProfilerSpikes, F=CompilationReport
Note: Mode C (TaggedLogs) uses `scope.tag_filter` as the tag prefix (e.g. `"tag_filter": "[MyTag]"`). Defaults to `[AGENT]` if omitted.

### ComponentInspector
Modes: A=ExistenceCheck, B=PropertyList, C=PropertyValues, D=CrossObjectComparison, E=PrefabOverrides, F=MissingReferences
Scope: rootPath, objectNames[], componentType, propertyPath, maxDepth, includeInactive

### ReferenceScanner
Modes: A=SceneReferences, B=PrefabReferences, C=CrossSceneReferences, D=ScriptableObjectReferences, E=MissingScripts, F=MaterialAudit, G=FullProjectScan
Scope: rootPath (scene), assetPath (asset), includeInactive

### ProjectCartographer
Modes: A=FileTree, B=FileManifest, C=DependencyMap, D=OrphanSearch, E=GuidRegistry, F=ImportSettings, G=TypeCensus
Scope: assetPath, extension, maxDepth, namePattern, sizeThreshold, excludePackages
Special: Mode C requires dependencyTarget + dependencyDirection (DependsOn/ReferencedBy)

### SceneDiff
Operations: Snapshot, CompareCurrent, Compare, List, Clear
Modes: A=HashOnly, B=ObjectCountDiff, C=StructuralDiff, D=PropertyDiff

### SmartSearch
Domain: Scene, Assets, Both
Scene filters: name, componentType, tag, layer, isActive, isStatic
Asset filters: name, assetType, assetExtension, assetLabel, assetPath

### SettingsReporter
A=QuickSummary, B=PlayerSettingsDump, C=QualityLevels, D=TagsAndLayers, E=TimeAndPhysics, F=EditorSettings, G=InputSystem

### ScriptAnalyzer
A=ClassMap, B=DependencyGraph, C=AssemblyDefinitions, D=AttributeScan (attributeFilter param), E=ApiUsageAudit

### PrefabAuditor
A=VariantTree, B=OverrideReport, C=UnusedOverrideCleanup, D=NestingDepth, E=CrossPrefabReferences

### PhysicsReporter
A=ColliderCensus, B=LayerCollisionMatrix, C=RigidbodyReport, D=TriggerMap, E=JointReport, F=PhysicsSettings, G=DeterminismCheck2D

### AnimationInspector
A=ControllerOverview, B=StateMachineMap, C=AnimationEvents, D=ClipPropertyAudit, E=AvatarBoneReport, F=AnimatorPoolStatus

### AudioReporter
A=SourceCensus, B=MixerGraph, C=ClipImportAudit, D=SpatialAudioMap

### ShaderAuditor
A=MaterialCensus, B=ShaderPropertyDump, C=KeywordReport, D=CompilationStatus, E=GPUCompatibility, F=ComputeShaderAudit

### RenderAuditor
A=PipelineSummary, B=GPUResidentDrawerCompatibility, C=OcclusionCullingStats, D=STPConfiguration, E=ShaderVariantReport, F=LightAndShadowAudit, G=CameraStackReport

### UIToolkitInspector
A=VisualTreeStructure, B=StyleAudit, C=BindingReport, D=UxmlUssFileMap, E=AccessibilityAudit

### TestRunner
A=TestList, B=RunAll (async), C=RunFiltered (async), D=CoverageReport
Note: Use fully-qualified `Axiom.Editor.AgentBridge.Diagnostics.TestRunner` to avoid conflict with Unity.TestRunner.

### NavMeshInspector
A=AgentTypes, B=SurfaceReport, C=ObstacleReport, D=LinkReport, E=ReachabilityTest

### AccessibilityValidator
A=ScreenReaderCompatibility, B=ColorContrastAudit, C=InputAccessibility, D=TextScaling

### CISweep
A=QuickHealthCheck, B=FullProjectAudit, C=PreReleaseChecklist

---

## Action Tools — Full Reference

### SceneActions
Operations: CreateGameObject, BatchCreate, DestroyGameObject, BatchDestroy, Reparent, BatchReparent, Rename, SetState, ManageScene

### ComponentActions
Operations: AddComponent, RemoveComponent (validates RequireComponent), SetProperty, BatchSetProperties, AddComponentWithProperties

### WiringUtility
Operations: WireReference, BatchWire, AutoWire, VerifyWiring

### AssetActions
Operations: MoveAsset, RenameAsset, CopyAsset, DeleteAsset, BatchMove, BulkRename, CreateFolder, CreateScriptableObject, CreateMaterial, BatchImportSettings

### SettingsActions
Operations: SetScriptingDefines, AddTag, AddLayer, SetQualityLevel, SetPlayerSetting, SetEditorSetting, SetLayerCollision, SetTimeSettings, SetPhysicsSettings

### RenderActions
Operations: GetActiveRenderPipeline, ModifyRenderPipelineAsset, BatchModifyRenderPipeline, ListRenderPipelineProperties, ConfigureGPUResidentDrawer, SetShadowSettings, SetCameraSettings, SetLightSettings, AssignRenderPipelineAsset, ConfigureSTP

### BuildProfileActions
Operations: ListProfiles, GetActiveProfile, SetActiveProfile, ModifyProfileDefines, DiffProfiles, ModifyProfileProperty, ModifyBuildSceneList, TriggerBuild, AnalyzeBuildReport, CreateProfile

### PackageManagerActions
Operations: ListPackages, SearchPackage, AddPackage, RemovePackage, EmbedPackage, GetPackageInfo, ResolvePackages, AddScopedRegistry

### PlayModeActions
Operations: GetPlayModeState, EnterPlayMode, ExitPlayMode, PausePlayMode, StepFrame, StepMultipleFrames, CapturePlayModeState, WaitForCompilation, ResetAnimatorPool
WARNING: EnterPlayMode triggers domain reload — always confirm state after.

### ScreenCaptureActions
Operations: CaptureGameView, CaptureSceneView, CaptureSceneViewFromAngle, CaptureWithAnnotations, ListScreenshots, CleanupScreenshots

### PrefabActions
Operations: ApplyOverrides, RevertOverrides, ApplyPropertyOverride, RevertPropertyOverride, RemoveUnusedOverrides, OpenPrefabStage, ClosePrefabStage, GetPrefabInfo

### InputSimulationActions
Operations: SimulateKeyPress, SimulateMouseClick, SimulateMouseMove, SimulateMouseDrag, SimulateGamepadInput, SimulateInputAction, GetInputSystemInfo
WARNING: All simulation ops require Play Mode (except GetInputSystemInfo).

### BuildPipelineHooks
Operations: SetEnabled, Configure, GetStatus, RunPreBuildValidation

### MultiplayerActions
Operations: GetMultiplayerStatus, ConfigurePlayers, SetPlayerActive, GetPlayerLogs, RunMultiplayerTest
WARNING: Requires com.unity.multiplayer.playmode package.

### VisionAnalysis
Operations: AnalyzeScreenshot, CaptureAndAnalyze, CompareScreenshots

### SentisActions
Operations: GetSentisStatus, RunModel, RunImageModel
WARNING: Requires com.unity.sentis package.

---

## Project Structure

```
ProjectRoot/
├── .cursorrules                         ← Agent API reference (Cursor IDE)
├── project_instructions.md              ← Schemas + templates (other agents)
├── CLAUDE.md                            ← This file (Claude Code — complete reference)
├── ImplementationPlans/                 ← Phase-specific implementation MDs
├── AgentReports/                        ← Diagnostic output (auto-created)
│   ├── Snapshots/                       ← SceneDiff snapshots
│   ├── Screenshots/                     ← ScreenCaptureActions output
│   └── StatusUpdate.md                  ← Current state summary
├── Assets/
│   └── Axiom/
│       └── Editor/
│           ├── AgentBridge/
│           │   ├── Core/                ← 8 files (utilities + Gateway + JsonCommandParser)
│           │   ├── Diagnostics/         ← 20 diagnostic tools
│           │   ├── Actions/             ← 16 action tools
│           │   ├── Mcp/                 ← 2 files (compiled with AXIOM_HAS_UNITY_ASSISTANT)
│           │   ├── Temp/               ← Temporary editor scripts (create here, delete after)
│           │   └── AgentBridge.asmdef
│           ├── WorkspaceRules/
│           └── Installer/
```

Reports output to: `AgentReports/` (project root)
Implementation plans: `ImplementationPlans/` (project root)

### File Roles

| File | Read By | Contains |
|:---|:---|:---|
| `CLAUDE.md` | Claude Code (auto-loaded) | Complete Axiom reference (this file) |
| `.cursorrules` | Cursor IDE (auto-loaded) | Core rules + tool API reference |
| `project_instructions.md` | Other agents (manual/MCP) | Command schema, template, architecture |
| `AgentReports/StatusUpdate.md` | Any agent resuming work | Current state, last changes, verification |
| `ImplementationPlans/*.md` | Any agent executing a phase | Step-by-step task plans |
