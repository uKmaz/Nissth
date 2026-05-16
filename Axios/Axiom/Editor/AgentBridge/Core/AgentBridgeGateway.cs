using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;
using Axiom.Editor.AgentBridge.Diagnostics;
using Axiom.Editor.AgentBridge.Actions;
// Aliases for nested enums (enums declared inside their tool class, not at namespace level)
using HierarchyMode         = Axiom.Editor.AgentBridge.Diagnostics.HierarchyLens.HierarchyMode;
using LogMirrorMode         = Axiom.Editor.AgentBridge.Diagnostics.LogMirror.LogMirrorMode;
using SettingsReporterMode  = Axiom.Editor.AgentBridge.Diagnostics.SettingsReporter.SettingsReporterMode;
using ScriptAnalyzerMode    = Axiom.Editor.AgentBridge.Diagnostics.ScriptAnalyzer.ScriptAnalyzerMode;
using PrefabAuditorMode     = Axiom.Editor.AgentBridge.Diagnostics.PrefabAuditor.PrefabAuditorMode;
using UIToolkitInspectorMode= Axiom.Editor.AgentBridge.Diagnostics.UIToolkitInspector.UIToolkitInspectorMode;
using AxiomTestRunner       = Axiom.Editor.AgentBridge.Diagnostics.TestRunner;
using TestRunnerMode        = Axiom.Editor.AgentBridge.Diagnostics.TestRunner.TestRunnerMode;
using PhysicsReporterMode   = Axiom.Editor.AgentBridge.Diagnostics.PhysicsReporter.PhysicsReporterMode;
using AnimationInspectorMode= Axiom.Editor.AgentBridge.Diagnostics.AnimationInspector.AnimationInspectorMode;
using AudioReporterMode     = Axiom.Editor.AgentBridge.Diagnostics.AudioReporter.AudioReporterMode;
using NavMeshInspectorMode  = Axiom.Editor.AgentBridge.Diagnostics.NavMeshInspector.NavMeshInspectorMode;
using ShaderAuditorMode     = Axiom.Editor.AgentBridge.Diagnostics.ShaderAuditor.ShaderAuditorMode;
using RenderAuditorMode     = Axiom.Editor.AgentBridge.Diagnostics.RenderAuditor.RenderAuditorMode;
using AccessibilityMode     = Axiom.Editor.AgentBridge.Diagnostics.AccessibilityValidator.AccessibilityMode;
using CISweepMode           = Axiom.Editor.AgentBridge.Diagnostics.CISweep.CISweepMode;
using SceneDiffMode         = Axiom.Editor.AgentBridge.Diagnostics.SceneDiff.SceneDiffMode;
using SceneDiffOperation    = Axiom.Editor.AgentBridge.Diagnostics.SceneDiff.SceneDiffOperation;

namespace Axiom.Editor.AgentBridge.Core
{
    /// <summary>
    /// The single entry point for all Axiom Diagnostic Bridge commands.
    /// Accepts a JSON command string, dispatches to the correct tool, and returns the result.
    ///
    /// This is the implementation of the master plan's Section 2 JSON Command Schema.
    /// It replaces the need for execute_script temp files — any MCP server that can send
    /// a JSON string and receive a string result can use Axiom.
    /// </summary>
    public static class AgentBridgeGateway
    {
        // ─────────────────────────────────────────────────────────────────────
        //  Primary Entry Point
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Executes a JSON command against the Axiom Diagnostic Bridge.
        /// </summary>
        /// <param name="json">JSON command following the schema in project_instructions.md Section 2.</param>
        /// <returns>
        /// On success: report content (destination "return") or file path (destination "file").
        /// On failure: JSON error object {"error": "description"}.
        /// </returns>
        public static string Execute(string json)
        {
            var cmd = JsonCommandParser.Parse(json, out string parseError);
            if (cmd == null)
                return FormatError(parseError);

            var command = cmd.Value;

            string validateError = JsonCommandParser.Validate(command);
            if (validateError != null)
                return FormatError(validateError);

            try
            {
                return Dispatch(command);
            }
            catch (Exception ex)
            {
                return FormatError($"Execution error in '{command.tool}': {ex.Message}");
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Main Dispatcher
        // ─────────────────────────────────────────────────────────────────────

        private static string Dispatch(BridgeCommand cmd)
        {
            switch (cmd.tool)
            {
                // Diagnostic tools
                case "hierarchy_lens":         return DispatchHierarchyLens(cmd);
                case "log_mirror":             return DispatchLogMirror(cmd);
                case "component_inspector":    return DispatchComponentInspector(cmd);
                case "reference_scanner":      return DispatchReferenceScanner(cmd);
                case "project_cartographer":   return DispatchProjectCartographer(cmd);
                case "scene_diff":             return DispatchSceneDiff(cmd);
                case "smart_search":           return DispatchSmartSearch(cmd);
                case "settings_reporter":      return DispatchSettingsReporter(cmd);
                case "script_analyzer":        return DispatchScriptAnalyzer(cmd);
                case "prefab_auditor":         return DispatchPrefabAuditor(cmd);
                case "ui_toolkit_inspector":   return DispatchUIToolkitInspector(cmd);
                case "test_runner":            return DispatchTestRunner(cmd);
                case "physics_reporter":       return DispatchPhysicsReporter(cmd);
                case "animation_inspector":    return DispatchAnimationInspector(cmd);
                case "audio_reporter":         return DispatchAudioReporter(cmd);
                case "navmesh_inspector":      return DispatchNavMeshInspector(cmd);
                case "shader_auditor":         return DispatchShaderAuditor(cmd);
                case "render_auditor":         return DispatchRenderAuditor(cmd);
                case "accessibility_validator":return DispatchAccessibilityValidator(cmd);
                case "ci_sweep":               return DispatchCISweep(cmd);
                // Action tools
                case "scene_actions":          return DispatchSceneActions(cmd);
                case "component_actions":      return DispatchComponentActions(cmd);
                case "asset_actions":          return DispatchAssetActions(cmd);
                case "wiring_utility":         return DispatchWiringUtility(cmd);
                case "settings_actions":       return DispatchSettingsActions(cmd);
                case "render_actions":         return DispatchRenderActions(cmd);
                case "build_profile_actions":  return DispatchBuildProfileActions(cmd);
                case "package_manager_actions":return DispatchPackageManagerActions(cmd);
                case "play_mode_actions":      return DispatchPlayModeActions(cmd);
                case "screen_capture_actions": return DispatchScreenCaptureActions(cmd);
                case "prefab_actions":         return DispatchPrefabActions(cmd);
                case "input_simulation_actions":return DispatchInputSimulationActions(cmd);
                case "build_pipeline_hooks":   return DispatchBuildPipelineHooks(cmd);
                case "multiplayer_actions":    return DispatchMultiplayerActions(cmd);
                case "vision_analysis":        return DispatchVisionAnalysis(cmd);
                case "sentis_actions":         return DispatchSentisActions(cmd);
                default:
                    return FormatError($"Unhandled tool: '{cmd.tool}'");
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Diagnostic Dispatchers
        // ─────────────────────────────────────────────────────────────────────

        private static string DispatchHierarchyLens(BridgeCommand cmd)
        {
            var mode = (cmd.mode?.ToLowerInvariant()) switch
            {
                "structure"              or "a" => HierarchyMode.Structure,
                "components"             or "b" => HierarchyMode.Components,
                "component_state"        or "c" => HierarchyMode.ComponentState,
                "single_component_focus" or "d" => HierarchyMode.SingleComponentFocus,
                "transform_detail"       or "e" => HierarchyMode.TransformDetail,
                "full_inspector_dump"    or "f" => HierarchyMode.FullInspectorDump,
                _                               => HierarchyMode.Structure
            };

            string path = HierarchyLens.GenerateReport(
                mode,
                rootPath:        NullIfEmpty(cmd.scope.rootPath),
                maxDepth:        cmd.scope.maxDepth,
                tagFilter:       NullIfEmpty(cmd.scope.tagFilter),
                layerFilter:     NullIfEmpty(cmd.scope.layerFilter),
                componentFilter: NullIfEmpty(cmd.scope.componentFilter),
                includeInactive: false);

            return HandleOutput(cmd, path);
        }

        private static string DispatchLogMirror(BridgeCommand cmd)
        {
            var mode = (cmd.mode?.ToLowerInvariant()) switch
            {
                "errors_only"        or "a" => LogMirrorMode.ErrorsOnly,
                "warnings"           or "b" => LogMirrorMode.Warnings,
                "tagged_logs"        or "c" => LogMirrorMode.TaggedLogs,
                "full_stream"        or "d" => LogMirrorMode.FullStream,
                "profiler_spikes"    or "e" => LogMirrorMode.ProfilerSpikes,
                "compilation_report" or "f" => LogMirrorMode.CompilationReport,
                _                           => LogMirrorMode.CompilationReport
            };

            string path = LogMirror.GenerateReport(mode, tagPrefix: NullIfEmpty(cmd.scope.tagFilter));
            return HandleOutput(cmd, path);
        }

        private static string DispatchComponentInspector(BridgeCommand cmd)
        {
            var mode = (cmd.mode?.ToLowerInvariant()) switch
            {
                "existence_check"       or "a" => ComponentInspectorMode.ExistenceCheck,
                "property_list"         or "b" => ComponentInspectorMode.PropertyList,
                "property_values"       or "c" => ComponentInspectorMode.PropertyValues,
                "cross_object_comparison"or "d"=> ComponentInspectorMode.CrossObjectComparison,
                "prefab_overrides"      or "e" => ComponentInspectorMode.PrefabOverrides,
                "missing_references"    or "f" => ComponentInspectorMode.MissingReferences,
                _                              => ComponentInspectorMode.PropertyValues
            };

            string path = ComponentInspector.GenerateReport(
                mode,
                rootPath:       NullIfEmpty(cmd.scope.rootPath),
                objectNames:    cmd.scope.objectNames?.Length > 0 ? cmd.scope.objectNames : null,
                componentType:  NullIfEmpty(cmd.scope.componentFilter),
                maxDepth:       cmd.scope.maxDepth,
                includeInactive: false);

            return HandleOutput(cmd, path);
        }

        private static string DispatchReferenceScanner(BridgeCommand cmd)
        {
            var mode = (cmd.mode?.ToLowerInvariant()) switch
            {
                "scene_references"           or "a" => ReferenceScannerMode.SceneReferences,
                "prefab_references"          or "b" => ReferenceScannerMode.PrefabReferences,
                "cross_scene_references"     or "c" => ReferenceScannerMode.CrossSceneReferences,
                "scriptable_object_references"or "d"=> ReferenceScannerMode.ScriptableObjectReferences,
                "missing_scripts"            or "e" => ReferenceScannerMode.MissingScripts,
                "material_audit"             or "f" => ReferenceScannerMode.MaterialAudit,
                "full_project_scan"          or "g" => ReferenceScannerMode.FullProjectScan,
                _                                   => ReferenceScannerMode.SceneReferences
            };

            string path = ReferenceScanner.GenerateReport(
                mode,
                rootPath:  NullIfEmpty(cmd.scope.rootPath),
                assetPath: NullIfEmpty(cmd.scope.assetPath));

            return HandleOutput(cmd, path);
        }

        private static string DispatchProjectCartographer(BridgeCommand cmd)
        {
            var mode = (cmd.mode?.ToLowerInvariant()) switch
            {
                "file_tree"      or "a" => CartographerMode.FileTree,
                "file_manifest"  or "b" => CartographerMode.FileManifest,
                "dependency_map" or "c" => CartographerMode.DependencyMap,
                "orphan_search"  or "d" => CartographerMode.OrphanSearch,
                "guid_registry"  or "e" => CartographerMode.GuidRegistry,
                "import_settings"or "f" => CartographerMode.ImportSettings,
                "type_census"    or "g" => CartographerMode.TypeCensus,
                _                       => CartographerMode.FileTree
            };

            string path = ProjectCartographer.GenerateReport(
                mode,
                assetPath:  NullIfEmpty(cmd.scope.assetPath),
                extension:  NullIfEmpty(cmd.scope.assetExtension),
                maxDepth:   cmd.scope.maxDepth);

            return HandleOutput(cmd, path);
        }

        private static string DispatchSceneDiff(BridgeCommand cmd)
        {
            var operation = (cmd.mode?.ToLowerInvariant()) switch
            {
                "snapshot"        => SceneDiffOperation.Snapshot,
                "compare"         => SceneDiffOperation.Compare,
                "compare_current" => SceneDiffOperation.CompareCurrent,
                "list"            => SceneDiffOperation.List,
                "clear"           => SceneDiffOperation.Clear,
                _                 => SceneDiffOperation.CompareCurrent
            };

            // scope.sceneName = label/snapshot name for Snapshot & CompareCurrent
            // scope.tagFilter = labelA, scope.layerFilter = labelB for Compare
            string path = SceneDiff.Execute(
                operation,
                label:     NullIfEmpty(cmd.scope.sceneName),
                labelA:    NullIfEmpty(cmd.scope.tagFilter),
                labelB:    NullIfEmpty(cmd.scope.layerFilter),
                rootPath:  NullIfEmpty(cmd.scope.rootPath));

            return HandleOutput(cmd, path);
        }

        private static string DispatchSmartSearch(BridgeCommand cmd)
        {
            var domain = (cmd.mode?.ToLowerInvariant()) switch
            {
                "scene"  => SearchDomain.Scene,
                "assets" => SearchDomain.Assets,
                "both"   => SearchDomain.Both,
                _        => SearchDomain.Both
            };

            string path = SmartSearch.Search(
                domain:          domain,
                name:            NullIfEmpty(cmd.scope.objectNames?.Length > 0 ? cmd.scope.objectNames[0] : null),
                componentType:   NullIfEmpty(cmd.scope.componentFilter),
                tag:             NullIfEmpty(cmd.scope.tagFilter),
                layer:           NullIfEmpty(cmd.scope.layerFilter),
                assetExtension:  NullIfEmpty(cmd.scope.assetExtension),
                assetPath:       NullIfEmpty(cmd.scope.assetPath),
                includeInactive: true);

            return HandleOutput(cmd, path);
        }

        private static string DispatchSettingsReporter(BridgeCommand cmd)
        {
            var mode = (cmd.mode?.ToLowerInvariant()) switch
            {
                "quick_summary"       or "a" => SettingsReporterMode.QuickSummary,
                "player_settings_dump"or "b" => SettingsReporterMode.PlayerSettingsDump,
                "quality_levels"      or "c" => SettingsReporterMode.QualityLevels,
                "tags_and_layers"     or "d" => SettingsReporterMode.TagsAndLayers,
                "time_and_physics"    or "e" => SettingsReporterMode.TimeAndPhysics,
                "editor_settings"     or "f" => SettingsReporterMode.EditorSettings,
                "input_system"        or "g" => SettingsReporterMode.InputSystem,
                _                            => SettingsReporterMode.QuickSummary
            };

            string path = SettingsReporter.GenerateReport(mode);
            return HandleOutput(cmd, path);
        }

        private static string DispatchScriptAnalyzer(BridgeCommand cmd)
        {
            var mode = (cmd.mode?.ToLowerInvariant()) switch
            {
                "class_map"            or "a" => ScriptAnalyzerMode.ClassMap,
                "dependency_graph"     or "b" => ScriptAnalyzerMode.DependencyGraph,
                "assembly_definitions" or "c" => ScriptAnalyzerMode.AssemblyDefinitions,
                "attribute_scan"       or "d" => ScriptAnalyzerMode.AttributeScan,
                "api_usage_audit"      or "e" => ScriptAnalyzerMode.ApiUsageAudit,
                _                             => ScriptAnalyzerMode.ClassMap
            };

            string path = ScriptAnalyzer.GenerateReport(
                mode,
                assetPath:       NullIfEmpty(cmd.scope.assetPath),
                attributeFilter: NullIfEmpty(cmd.scope.componentFilter));

            return HandleOutput(cmd, path);
        }

        private static string DispatchPrefabAuditor(BridgeCommand cmd)
        {
            var mode = (cmd.mode?.ToLowerInvariant()) switch
            {
                "variant_tree"            or "a" => PrefabAuditorMode.VariantTree,
                "override_report"         or "b" => PrefabAuditorMode.OverrideReport,
                "unused_override_cleanup" or "c" => PrefabAuditorMode.UnusedOverrideCleanup,
                "nesting_depth"           or "d" => PrefabAuditorMode.NestingDepth,
                "cross_prefab_references" or "e" => PrefabAuditorMode.CrossPrefabReferences,
                _                                => PrefabAuditorMode.OverrideReport
            };

            string path = PrefabAuditor.GenerateReport(
                mode,
                prefabPath: NullIfEmpty(cmd.scope.rootPath),
                assetPath:  NullIfEmpty(cmd.scope.assetPath));

            return HandleOutput(cmd, path);
        }

        private static string DispatchUIToolkitInspector(BridgeCommand cmd)
        {
            var mode = (cmd.mode?.ToLowerInvariant()) switch
            {
                "visual_tree_structure" or "a" => UIToolkitInspectorMode.VisualTreeStructure,
                "style_audit"           or "b" => UIToolkitInspectorMode.StyleAudit,
                "binding_report"        or "c" => UIToolkitInspectorMode.BindingReport,
                "uxml_uss_file_map"     or "d" => UIToolkitInspectorMode.UxmlUssFileMap,
                "accessibility_audit"   or "e" => UIToolkitInspectorMode.AccessibilityAudit,
                _                              => UIToolkitInspectorMode.VisualTreeStructure
            };

            string path = UIToolkitInspector.GenerateReport(
                mode,
                uxmlPath:  NullIfEmpty(cmd.scope.assetPath),
                maxDepth:  cmd.scope.maxDepth);

            return HandleOutput(cmd, path);
        }

        private static string DispatchTestRunner(BridgeCommand cmd)
        {
            var mode = (cmd.mode?.ToLowerInvariant()) switch
            {
                "test_list"       or "a" => TestRunnerMode.TestList,
                "run_all"         or "b" => TestRunnerMode.RunAll,
                "run_filtered"    or "c" => TestRunnerMode.RunFiltered,
                "coverage_report" or "d" => TestRunnerMode.CoverageReport,
                _                        => TestRunnerMode.TestList
            };

            string path = AxiomTestRunner.GenerateReport(
                mode,
                testFilter:     NullIfEmpty(cmd.scope.componentFilter),
                categoryFilter: NullIfEmpty(cmd.scope.tagFilter));

            return HandleOutput(cmd, path);
        }

        private static string DispatchPhysicsReporter(BridgeCommand cmd)
        {
            var mode = (cmd.mode?.ToLowerInvariant()) switch
            {
                "collider_census"        or "a" => PhysicsReporterMode.ColliderCensus,
                "layer_collision_matrix" or "b" => PhysicsReporterMode.LayerCollisionMatrix,
                "rigidbody_report"       or "c" => PhysicsReporterMode.RigidbodyReport,
                "trigger_map"            or "d" => PhysicsReporterMode.TriggerMap,
                "joint_report"           or "e" => PhysicsReporterMode.JointReport,
                "physics_settings"       or "f" => PhysicsReporterMode.PhysicsSettings,
                "determinism_check_2d"   or "g" => PhysicsReporterMode.DeterminismCheck2D,
                _                               => PhysicsReporterMode.ColliderCensus
            };

            string path = PhysicsReporter.GenerateReport(mode);
            return HandleOutput(cmd, path);
        }

        private static string DispatchAnimationInspector(BridgeCommand cmd)
        {
            var mode = (cmd.mode?.ToLowerInvariant()) switch
            {
                "controller_overview" or "a" => AnimationInspectorMode.ControllerOverview,
                "state_machine_map"   or "b" => AnimationInspectorMode.StateMachineMap,
                "animation_events"    or "c" => AnimationInspectorMode.AnimationEvents,
                "clip_property_audit" or "d" => AnimationInspectorMode.ClipPropertyAudit,
                "avatar_bone_report"  or "e" => AnimationInspectorMode.AvatarBoneReport,
                "animator_pool_status"or "f" => AnimationInspectorMode.AnimatorPoolStatus,
                _                            => AnimationInspectorMode.ControllerOverview
            };

            string path = AnimationInspector.GenerateReport(mode);
            return HandleOutput(cmd, path);
        }

        private static string DispatchAudioReporter(BridgeCommand cmd)
        {
            var mode = (cmd.mode?.ToLowerInvariant()) switch
            {
                "source_census"    or "a" => AudioReporterMode.SourceCensus,
                "mixer_graph"      or "b" => AudioReporterMode.MixerGraph,
                "clip_import_audit"or "c" => AudioReporterMode.ClipImportAudit,
                "spatial_audio_map"or "d" => AudioReporterMode.SpatialAudioMap,
                _                         => AudioReporterMode.SourceCensus
            };

            string path = AudioReporter.GenerateReport(mode);
            return HandleOutput(cmd, path);
        }

        private static string DispatchNavMeshInspector(BridgeCommand cmd)
        {
            var mode = (cmd.mode?.ToLowerInvariant()) switch
            {
                "agent_types"      or "a" => NavMeshInspectorMode.AgentTypes,
                "surface_report"   or "b" => NavMeshInspectorMode.SurfaceReport,
                "obstacle_report"  or "c" => NavMeshInspectorMode.ObstacleReport,
                "link_report"      or "d" => NavMeshInspectorMode.LinkReport,
                "reachability_test"or "e" => NavMeshInspectorMode.ReachabilityTest,
                _                         => NavMeshInspectorMode.AgentTypes
            };

            string path = NavMeshInspector.GenerateReport(mode);
            return HandleOutput(cmd, path);
        }

        private static string DispatchShaderAuditor(BridgeCommand cmd)
        {
            var mode = (cmd.mode?.ToLowerInvariant()) switch
            {
                "material_census"     or "a" => ShaderAuditorMode.MaterialCensus,
                "shader_property_dump"or "b" => ShaderAuditorMode.ShaderPropertyDump,
                "keyword_report"      or "c" => ShaderAuditorMode.KeywordReport,
                "compilation_status"  or "d" => ShaderAuditorMode.CompilationStatus,
                "gpu_compatibility"   or "e" => ShaderAuditorMode.GPUCompatibility,
                "compute_shader_audit"or "f" => ShaderAuditorMode.ComputeShaderAudit,
                _                            => ShaderAuditorMode.MaterialCensus
            };

            string path = ShaderAuditor.GenerateReport(mode);
            return HandleOutput(cmd, path);
        }

        private static string DispatchRenderAuditor(BridgeCommand cmd)
        {
            var mode = (cmd.mode?.ToLowerInvariant()) switch
            {
                "pipeline_summary"                  or "a" => RenderAuditorMode.PipelineSummary,
                "gpu_resident_drawer_compatibility" or "b" => RenderAuditorMode.GPUResidentDrawerCompatibility,
                "occlusion_culling_stats"           or "c" => RenderAuditorMode.OcclusionCullingStats,
                "stp_configuration"                 or "d" => RenderAuditorMode.STPConfiguration,
                "shader_variant_report"             or "e" => RenderAuditorMode.ShaderVariantReport,
                "light_and_shadow_audit"            or "f" => RenderAuditorMode.LightAndShadowAudit,
                "camera_stack_report"               or "g" => RenderAuditorMode.CameraStackReport,
                _                                          => RenderAuditorMode.PipelineSummary
            };

            string path = RenderAuditor.GenerateReport(mode);
            return HandleOutput(cmd, path);
        }

        private static string DispatchAccessibilityValidator(BridgeCommand cmd)
        {
            var mode = (cmd.mode?.ToLowerInvariant()) switch
            {
                "screen_reader_compatibility" or "a" => AccessibilityMode.ScreenReaderCompatibility,
                "color_contrast_audit"        or "b" => AccessibilityMode.ColorContrastAudit,
                "input_accessibility"         or "c" => AccessibilityMode.InputAccessibility,
                "text_scaling"                or "d" => AccessibilityMode.TextScaling,
                _                                    => AccessibilityMode.ScreenReaderCompatibility
            };

            string path = AccessibilityValidator.GenerateReport(mode);
            return HandleOutput(cmd, path);
        }

        private static string DispatchCISweep(BridgeCommand cmd)
        {
            var mode = (cmd.mode?.ToLowerInvariant()) switch
            {
                "quick_health_check"    or "a" => CISweepMode.QuickHealthCheck,
                "full_project_audit"    or "b" => CISweepMode.FullProjectAudit,
                "pre_release_checklist" or "c" => CISweepMode.PreReleaseChecklist,
                _                              => CISweepMode.QuickHealthCheck
            };

            string path = CISweep.GenerateReport(mode);
            return HandleOutput(cmd, path);
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Action Dispatchers
        // ─────────────────────────────────────────────────────────────────────

        private static string DispatchSceneActions(BridgeCommand cmd)
        {
            ActionResult result;
            string firstName = cmd.scope.objectNames?.Length > 0 ? cmd.scope.objectNames[0] : null;

            switch (cmd.mode?.ToLowerInvariant())
            {
                case "create":
                    result = SceneActions.CreateGameObject(
                        firstName ?? "NewObject",
                        parentPath: NullIfEmpty(cmd.scope.rootPath));
                    break;
                case "destroy":
                    result = SceneActions.DestroyGameObject(cmd.scope.rootPath);
                    break;
                case "reparent":
                    result = SceneActions.Reparent(
                        cmd.scope.rootPath,
                        newParentPath: NullIfEmpty(cmd.scope.assetPath));
                    break;
                case "rename":
                    result = SceneActions.Rename(cmd.scope.rootPath, firstName ?? "");
                    break;
                case "set_state":
                    result = SceneActions.SetState(cmd.scope.rootPath);
                    break;
                case "manage_scene":
                    result = SceneActions.ManageScene(
                        firstName ?? "load",
                        scenePath: NullIfEmpty(cmd.scope.assetPath));
                    break;
                case "batch_create":
                    var defs = cmd.scope.objectNames?.Select(n => new GameObjectDefinition
                    {
                        name = n,
                        parentPath = NullIfEmpty(cmd.scope.rootPath)
                    }).ToArray();
                    if (defs == null || defs.Length == 0)
                        return FormatError("batch_create requires scope.object_names array.");
                    result = SceneActions.BatchCreate(defs);
                    break;
                case "batch_destroy":
                    if (cmd.scope.objectNames == null || cmd.scope.objectNames.Length == 0)
                        return FormatError("batch_destroy requires scope.object_names array.");
                    result = SceneActions.BatchDestroy(cmd.scope.objectNames);
                    break;
                case "batch_reparent":
                    if (cmd.scope.objectNames == null || cmd.scope.objectNames.Length == 0)
                        return FormatError("batch_reparent requires scope.object_names array.");
                    result = SceneActions.BatchReparent(cmd.scope.objectNames, NullIfEmpty(cmd.scope.rootPath));
                    break;
                default:
                    return FormatError($"Unknown scene_actions mode: '{cmd.mode}'. Valid: create, destroy, reparent, rename, set_state, manage_scene, batch_create, batch_destroy, batch_reparent");
            }

            return result.Success ? result.Message : FormatError(result.Message);
        }

        private static string DispatchComponentActions(BridgeCommand cmd)
        {
            ActionResult result;

            switch (cmd.mode?.ToLowerInvariant())
            {
                case "add":
                    result = ComponentActions.AddComponent(
                        cmd.scope.rootPath,
                        cmd.scope.componentFilter);
                    break;
                case "remove":
                    result = ComponentActions.RemoveComponent(
                        cmd.scope.rootPath,
                        cmd.scope.componentFilter);
                    break;
                case "set_property":
                    // rootPath = object, componentFilter = component type,
                    // assetPath = property path, sceneName = value
                    result = ComponentActions.SetProperty(
                        cmd.scope.rootPath,
                        cmd.scope.componentFilter,
                        cmd.scope.assetPath,
                        cmd.scope.sceneName);
                    break;
                case "batch_set_properties":
                    return FormatError("batch_set_properties requires complex parameter structures. Use direct C# call: ComponentActions.BatchSetProperties(assignments)");
                case "add_with_properties":
                    return FormatError("add_with_properties requires complex parameter structures. Use direct C# call: ComponentActions.AddComponentWithProperties(...)");
                default:
                    return FormatError($"Unknown component_actions mode: '{cmd.mode}'. Valid: add, remove, set_property, batch_set_properties, add_with_properties");
            }

            return result.Success ? result.Message : FormatError(result.Message);
        }

        private static string DispatchAssetActions(BridgeCommand cmd)
        {
            ActionResult result;
            string firstName = cmd.scope.objectNames?.Length > 0 ? cmd.scope.objectNames[0] : null;

            switch (cmd.mode?.ToLowerInvariant())
            {
                case "move":
                    result = AssetActions.MoveAsset(cmd.scope.rootPath, cmd.scope.assetPath);
                    break;
                case "rename":
                    result = AssetActions.RenameAsset(cmd.scope.rootPath, firstName ?? "");
                    break;
                case "copy":
                    result = AssetActions.CopyAsset(cmd.scope.rootPath, cmd.scope.assetPath);
                    break;
                case "delete":
                    result = AssetActions.DeleteAsset(cmd.scope.rootPath);
                    break;
                case "create_folder":
                    result = AssetActions.CreateFolder(cmd.scope.rootPath);
                    break;
                case "create_material":
                    result = AssetActions.CreateMaterial(
                        cmd.scope.assetPath,
                        shaderName: NullIfEmpty(cmd.scope.componentFilter));
                    break;
                case "batch_move":
                    return FormatError("batch_move requires parallel arrays of source/destination paths. Use direct C# call: AssetActions.BatchMove(sources, destinations)");
                case "bulk_rename":
                    if (cmd.scope.objectNames == null || cmd.scope.objectNames.Length < 2)
                        return FormatError("bulk_rename requires scope.object_names[0]=find, object_names[1]=replace, scope.asset_path=folder, scope.asset_extension=filter");
                    result = AssetActions.BulkRename(
                        cmd.scope.assetPath ?? "Assets",
                        NullIfEmpty(cmd.scope.assetExtension),
                        cmd.scope.objectNames[0],
                        cmd.scope.objectNames[1],
                        dryRun: true);
                    break;
                case "create_scriptable_object":
                    if (string.IsNullOrEmpty(cmd.scope.assetPath) || string.IsNullOrEmpty(cmd.scope.componentFilter))
                        return FormatError("create_scriptable_object requires scope.asset_path and scope.component_filter (type name)");
                    result = AssetActions.CreateScriptableObject(cmd.scope.assetPath, cmd.scope.componentFilter);
                    break;
                case "batch_import_settings":
                    return FormatError("batch_import_settings requires complex parameter structures (importer type + property dictionary). Use direct C# call: AssetActions.BatchImportSettings(...)");
                default:
                    return FormatError($"Unknown asset_actions mode: '{cmd.mode}'. Valid: move, rename, copy, delete, create_folder, create_material, batch_move, bulk_rename, create_scriptable_object, batch_import_settings");
            }

            return result.Success ? result.Message : FormatError(result.Message);
        }

        private static string DispatchWiringUtility(BridgeCommand cmd)
        {
            ActionResult result;

            switch (cmd.mode?.ToLowerInvariant())
            {
                case "wire":
                    // rootPath = source, componentFilter = component, assetPath = property, sceneName = target
                    result = WiringUtility.WireReference(
                        cmd.scope.rootPath,
                        cmd.scope.componentFilter,
                        cmd.scope.assetPath,
                        cmd.scope.sceneName);
                    break;
                case "auto_wire":
                    result = WiringUtility.AutoWire(
                        cmd.scope.rootPath,
                        componentType: NullIfEmpty(cmd.scope.componentFilter));
                    break;
                case "auto_wire_dry_run":
                    result = WiringUtility.AutoWire(
                        cmd.scope.rootPath,
                        componentType: NullIfEmpty(cmd.scope.componentFilter),
                        dryRun: true);
                    break;
                case "verify":
                    result = WiringUtility.VerifyWiring(
                        cmd.scope.rootPath,
                        componentType: NullIfEmpty(cmd.scope.componentFilter));
                    break;
                case "batch_wire":
                    return FormatError("batch_wire requires an array of WireDefinition structs. Use direct C# call: WiringUtility.BatchWire(definitions)");
                default:
                    return FormatError($"Unknown wiring_utility mode: '{cmd.mode}'. Valid: wire, auto_wire, auto_wire_dry_run, verify, batch_wire");
            }

            return result.Success ? result.Message : FormatError(result.Message);
        }

        private static string DispatchSettingsActions(BridgeCommand cmd)
        {
            ActionResult result;
            string firstName = cmd.scope.objectNames?.Length > 0 ? cmd.scope.objectNames[0] : null;

            switch (cmd.mode?.ToLowerInvariant())
            {
                case "add_tag":
                    result = SettingsActions.AddTag(cmd.scope.tagFilter);
                    break;
                case "add_layer":
                    result = SettingsActions.AddLayer(cmd.scope.layerFilter);
                    break;
                case "set_quality":
                    result = SettingsActions.SetQualityLevel(cmd.scope.assetPath ?? firstName ?? "");
                    break;
                case "set_player_setting":
                    // assetPath = propertyName, sceneName = value
                    result = SettingsActions.SetPlayerSetting(cmd.scope.assetPath, cmd.scope.sceneName);
                    break;
                case "set_editor_setting":
                    result = SettingsActions.SetEditorSetting(cmd.scope.assetPath, cmd.scope.sceneName);
                    break;
                case "set_scripting_defines":
                    result = SettingsActions.SetScriptingDefines(
                        defines: cmd.scope.objectNames,
                        remove: NullIfEmpty(cmd.scope.assetExtension)?.Split(','));
                    break;
                case "set_layer_collision":
                    if (cmd.scope.objectNames == null || cmd.scope.objectNames.Length < 2)
                        return FormatError("set_layer_collision requires scope.object_names[0]=layerA, object_names[1]=layerB, scope.tag_filter='true'/'false'");
                    bool collide = cmd.scope.tagFilter?.ToLower() != "false";
                    result = SettingsActions.SetLayerCollision(
                        cmd.scope.objectNames[0], cmd.scope.objectNames[1], collide);
                    break;
                case "set_time_settings":
                    return FormatError("set_time_settings requires typed float parameters. Use direct C# call: SettingsActions.SetTimeSettings(fixedDeltaTime: 0.02f)");
                case "set_physics_settings":
                    return FormatError("set_physics_settings requires Vector3 gravity parameter. Use direct C# call: SettingsActions.SetPhysicsSettings(gravity: new Vector3(0, -9.81f, 0))");
                default:
                    return FormatError($"Unknown settings_actions mode: '{cmd.mode}'. Valid: add_tag, add_layer, set_quality, set_player_setting, set_editor_setting, set_scripting_defines, set_layer_collision, set_time_settings, set_physics_settings");
            }

            return result.Success ? result.Message : FormatError(result.Message);
        }

        private static string DispatchRenderActions(BridgeCommand cmd)
        {
            ActionResult result;

            switch (cmd.mode?.ToLowerInvariant())
            {
                case "get_pipeline":
                    result = RenderActions.GetActiveRenderPipeline();
                    break;
                case "list_properties":
                    result = RenderActions.ListRenderPipelineProperties(
                        qualityLevel: NullIfEmpty(cmd.scope.sceneName));
                    break;
                case "set_property":
                    // assetPath = propertyPath, sceneName = value, layerFilter = qualityLevel
                    result = RenderActions.ModifyRenderPipelineAsset(
                        cmd.scope.assetPath,
                        cmd.scope.sceneName,
                        qualityLevel: NullIfEmpty(cmd.scope.layerFilter));
                    break;
                case "assign_pipeline":
                    result = RenderActions.AssignRenderPipelineAsset(
                        cmd.scope.assetPath,
                        qualityLevel: NullIfEmpty(cmd.scope.sceneName));
                    break;
                case "configure_gpu_resident_drawer":
                    bool enableGPU = cmd.scope.tagFilter?.ToLower() != "false";
                    result = RenderActions.ConfigureGPUResidentDrawer(enableGPU);
                    break;
                case "set_shadow_settings":
                    return FormatError("set_shadow_settings requires typed parameters. Use direct C# call: RenderActions.SetShadowSettings(shadowDistance: 150, shadowCascadeCount: 4)");
                case "set_camera_settings":
                    return FormatError("set_camera_settings requires object path + property dictionary. Use direct C# call: RenderActions.SetCameraSettings(\"Main Camera\", props)");
                case "set_light_settings":
                    return FormatError("set_light_settings requires object path + property dictionary. Use direct C# call: RenderActions.SetLightSettings(\"Directional Light\", props)");
                case "configure_stp":
                    bool stpEnabled = cmd.scope.tagFilter?.ToLower() != "false";
                    float renderScale = 1.0f;
                    if (cmd.scope.objectNames?.Length > 0)
                        float.TryParse(cmd.scope.objectNames[0], out renderScale);
                    result = RenderActions.ConfigureSTP(stpEnabled, renderScale);
                    break;
                case "batch_modify":
                    return FormatError("batch_modify requires a property dictionary. Use direct C# call: RenderActions.BatchModifyRenderPipeline(dict)");
                default:
                    return FormatError($"Unknown render_actions mode: '{cmd.mode}'. Valid: get_pipeline, list_properties, set_property, assign_pipeline, configure_gpu_resident_drawer, set_shadow_settings, set_camera_settings, set_light_settings, configure_stp, batch_modify");
            }

            return result.Success ? result.Message : FormatError(result.Message);
        }

        private static string DispatchBuildProfileActions(BridgeCommand cmd)
        {
            ActionResult result;
            string firstName = cmd.scope.objectNames?.Length > 0 ? cmd.scope.objectNames[0] : null;

            switch (cmd.mode?.ToLowerInvariant())
            {
                case "list":
                    result = BuildProfileActions.ListProfiles();
                    break;
                case "get_active":
                    result = BuildProfileActions.GetActiveProfile();
                    break;
                case "set_active":
                    result = BuildProfileActions.SetActiveProfile(cmd.scope.assetPath ?? firstName ?? "");
                    break;
                case "create":
                    result = BuildProfileActions.CreateProfile(
                        firstName ?? "",
                        cmd.scope.componentFilter ?? "",
                        savePath: NullIfEmpty(cmd.scope.assetPath));
                    break;
                case "analyze_build_report":
                    result = BuildProfileActions.AnalyzeBuildReport();
                    break;
                case "modify_defines":
                    if (string.IsNullOrEmpty(cmd.scope.rootPath))
                        return FormatError("modify_defines requires scope.root_path = profile name");
                    result = BuildProfileActions.ModifyProfileDefines(
                        cmd.scope.rootPath,
                        addDefines: cmd.scope.objectNames,
                        removeDefines: NullIfEmpty(cmd.scope.assetExtension)?.Split(','));
                    break;
                case "diff":
                    if (cmd.scope.objectNames == null || cmd.scope.objectNames.Length < 2)
                        return FormatError("diff requires scope.object_names[0]=profileA, object_names[1]=profileB");
                    result = BuildProfileActions.DiffProfiles(cmd.scope.objectNames[0], cmd.scope.objectNames[1]);
                    break;
                case "modify_property":
                    if (string.IsNullOrEmpty(cmd.scope.rootPath) || string.IsNullOrEmpty(cmd.scope.assetPath))
                        return FormatError("modify_property requires scope.root_path=profile, scope.asset_path=property, scope.tag_filter=value");
                    result = BuildProfileActions.ModifyProfileProperty(
                        cmd.scope.rootPath, cmd.scope.assetPath, cmd.scope.tagFilter);
                    break;
                case "modify_scenes":
                    result = BuildProfileActions.ModifyBuildSceneList(addScenes: cmd.scope.objectNames);
                    break;
                case "trigger_build":
                    bool buildDryRun = cmd.scope.tagFilter?.ToLower() == "true";
                    result = BuildProfileActions.TriggerBuild(
                        NullIfEmpty(cmd.scope.assetPath) ?? "Builds/Output",
                        dryRun: buildDryRun);
                    break;
                default:
                    return FormatError($"Unknown build_profile_actions mode: '{cmd.mode}'. Valid: list, get_active, set_active, create, analyze_build_report, modify_defines, diff, modify_property, modify_scenes, trigger_build");
            }

            return result.Success ? result.Message : FormatError(result.Message);
        }

        private static string DispatchPackageManagerActions(BridgeCommand cmd)
        {
            ActionResult result;

            switch (cmd.mode?.ToLowerInvariant())
            {
                case "list":
                    result = PackageManagerActions.ListPackages();
                    break;
                case "add":
                    result = PackageManagerActions.AddPackage(cmd.scope.assetPath);
                    break;
                case "remove":
                    result = PackageManagerActions.RemovePackage(cmd.scope.assetPath);
                    break;
                case "search":
                    result = PackageManagerActions.SearchPackage(cmd.scope.assetPath);
                    break;
                case "get_info":
                    result = PackageManagerActions.GetPackageInfo(cmd.scope.assetPath);
                    break;
                case "embed":
                    result = PackageManagerActions.EmbedPackage(cmd.scope.assetPath);
                    break;
                case "resolve":
                    result = PackageManagerActions.ResolvePackages();
                    break;
                case "add_scoped_registry":
                    if (cmd.scope.objectNames == null || cmd.scope.objectNames.Length < 1 || string.IsNullOrEmpty(cmd.scope.rootPath))
                        return FormatError("add_scoped_registry requires scope.object_names[0]=registryName, scope.root_path=url, additional object_names=scopes");
                    result = PackageManagerActions.AddScopedRegistry(
                        cmd.scope.objectNames[0],
                        cmd.scope.rootPath,
                        cmd.scope.objectNames.Skip(1).ToArray());
                    break;
                default:
                    return FormatError($"Unknown package_manager_actions mode: '{cmd.mode}'. Valid: list, add, remove, search, get_info, embed, resolve, add_scoped_registry");
            }

            return result.Success ? result.Message : FormatError(result.Message);
        }

        private static string DispatchPlayModeActions(BridgeCommand cmd)
        {
            ActionResult result;

            switch (cmd.mode?.ToLowerInvariant())
            {
                case "get_state":
                    result = PlayModeActions.GetPlayModeState();
                    break;
                case "enter":
                    result = PlayModeActions.EnterPlayMode();
                    break;
                case "exit":
                    result = PlayModeActions.ExitPlayMode();
                    break;
                case "pause":
                    result = PlayModeActions.PausePlayMode(true);
                    break;
                case "unpause":
                    result = PlayModeActions.PausePlayMode(false);
                    break;
                case "step_frame":
                    result = PlayModeActions.StepFrame();
                    break;
                case "wait_compile":
                    result = PlayModeActions.WaitForCompilation();
                    break;
                case "capture_state":
                    result = PlayModeActions.CapturePlayModeState(NullIfEmpty(cmd.scope.rootPath));
                    break;
                case "step_multiple":
                    int frames = 1;
                    if (cmd.scope.objectNames?.Length > 0)
                        int.TryParse(cmd.scope.objectNames[0], out frames);
                    result = PlayModeActions.StepMultipleFrames(frames);
                    break;
                case "reset_animator_pool":
                    result = PlayModeActions.ResetAnimatorPool(
                        rootPath: NullIfEmpty(cmd.scope.rootPath),
                        dryRun: cmd.scope.tagFilter?.ToLower() == "true");
                    break;
                default:
                    return FormatError($"Unknown play_mode_actions mode: '{cmd.mode}'. Valid: get_state, enter, exit, pause, unpause, step_frame, wait_compile, capture_state, step_multiple, reset_animator_pool");
            }

            return result.Success ? result.Message : FormatError(result.Message);
        }

        private static string DispatchScreenCaptureActions(BridgeCommand cmd)
        {
            ActionResult result;

            switch (cmd.mode?.ToLowerInvariant())
            {
                case "game_view":
                    result = ScreenCaptureActions.CaptureGameView();
                    break;
                case "scene_view":
                    result = ScreenCaptureActions.CaptureSceneView();
                    break;
                case "list":
                    result = ScreenCaptureActions.ListScreenshots();
                    break;
                case "cleanup":
                    result = ScreenCaptureActions.CleanupScreenshots();
                    break;
                case "scene_view_angle":
                    if (cmd.scope.objectNames == null || cmd.scope.objectNames.Length < 6)
                        return FormatError("scene_view_angle requires scope.object_names with 6 floats: posX,posY,posZ,lookX,lookY,lookZ");
                    var anglePos = new Vector3(
                        float.Parse(cmd.scope.objectNames[0]),
                        float.Parse(cmd.scope.objectNames[1]),
                        float.Parse(cmd.scope.objectNames[2]));
                    var angleLookAt = new Vector3(
                        float.Parse(cmd.scope.objectNames[3]),
                        float.Parse(cmd.scope.objectNames[4]),
                        float.Parse(cmd.scope.objectNames[5]));
                    result = ScreenCaptureActions.CaptureSceneViewFromAngle(anglePos, angleLookAt);
                    break;
                case "with_annotations":
                    bool showGizmos = cmd.scope.tagFilter?.ToLower() != "false";
                    result = ScreenCaptureActions.CaptureWithAnnotations(showGizmos: showGizmos);
                    break;
                default:
                    return FormatError($"Unknown screen_capture_actions mode: '{cmd.mode}'. Valid: game_view, scene_view, list, cleanup, scene_view_angle, with_annotations");
            }

            return result.Success ? result.Message : FormatError(result.Message);
        }

        private static string DispatchPrefabActions(BridgeCommand cmd)
        {
            ActionResult result;

            switch (cmd.mode?.ToLowerInvariant())
            {
                case "apply":
                    result = PrefabActions.ApplyOverrides(cmd.scope.rootPath);
                    break;
                case "revert":
                    result = PrefabActions.RevertOverrides(cmd.scope.rootPath);
                    break;
                case "open_stage":
                    result = PrefabActions.OpenPrefabStage(cmd.scope.assetPath);
                    break;
                case "close_stage":
                    result = PrefabActions.ClosePrefabStage();
                    break;
                case "get_info":
                    result = PrefabActions.GetPrefabInfo(cmd.scope.rootPath);
                    break;
                case "remove_unused_overrides":
                    result = PrefabActions.RemoveUnusedOverrides(cmd.scope.rootPath);
                    break;
                case "apply_property":
                    if (string.IsNullOrEmpty(cmd.scope.rootPath) || string.IsNullOrEmpty(cmd.scope.componentFilter) || string.IsNullOrEmpty(cmd.scope.assetPath))
                        return FormatError("apply_property requires scope.root_path=objectPath, scope.component_filter=componentType, scope.asset_path=propertyPath");
                    result = PrefabActions.ApplyPropertyOverride(
                        cmd.scope.rootPath, cmd.scope.componentFilter, cmd.scope.assetPath);
                    break;
                case "revert_property":
                    if (string.IsNullOrEmpty(cmd.scope.rootPath) || string.IsNullOrEmpty(cmd.scope.componentFilter) || string.IsNullOrEmpty(cmd.scope.assetPath))
                        return FormatError("revert_property requires scope.root_path=objectPath, scope.component_filter=componentType, scope.asset_path=propertyPath");
                    result = PrefabActions.RevertPropertyOverride(
                        cmd.scope.rootPath, cmd.scope.componentFilter, cmd.scope.assetPath);
                    break;
                default:
                    return FormatError($"Unknown prefab_actions mode: '{cmd.mode}'. Valid: apply, revert, open_stage, close_stage, get_info, remove_unused_overrides, apply_property, revert_property");
            }

            return result.Success ? result.Message : FormatError(result.Message);
        }

        private static string DispatchInputSimulationActions(BridgeCommand cmd)
        {
            ActionResult result;

            switch (cmd.mode?.ToLowerInvariant())
            {
                case "key_press":
                    // assetPath used as keyName
                    result = InputSimulationActions.SimulateKeyPress(cmd.scope.assetPath ?? "");
                    break;
                case "get_info":
                    result = InputSimulationActions.GetInputSystemInfo();
                    break;
                case "mouse_click":
                    if (cmd.scope.objectNames == null || cmd.scope.objectNames.Length < 2)
                        return FormatError("mouse_click requires scope.object_names[0]=screenX, [1]=screenY. Optional [2]=button (0/1/2)");
                    float clickX = float.Parse(cmd.scope.objectNames[0]);
                    float clickY = float.Parse(cmd.scope.objectNames[1]);
                    int btn = cmd.scope.objectNames.Length > 2 ? int.Parse(cmd.scope.objectNames[2]) : 0;
                    result = InputSimulationActions.SimulateMouseClick(clickX, clickY, btn);
                    break;
                case "mouse_move":
                    if (cmd.scope.objectNames == null || cmd.scope.objectNames.Length < 2)
                        return FormatError("mouse_move requires scope.object_names[0]=screenX, [1]=screenY");
                    result = InputSimulationActions.SimulateMouseMove(
                        float.Parse(cmd.scope.objectNames[0]),
                        float.Parse(cmd.scope.objectNames[1]));
                    break;
                case "mouse_drag":
                    if (cmd.scope.objectNames == null || cmd.scope.objectNames.Length < 4)
                        return FormatError("mouse_drag requires scope.object_names: fromX, fromY, toX, toY. Optional [4]=steps, [5]=button");
                    int dragSteps = cmd.scope.objectNames.Length > 4 ? int.Parse(cmd.scope.objectNames[4]) : 10;
                    int dragBtn = cmd.scope.objectNames.Length > 5 ? int.Parse(cmd.scope.objectNames[5]) : 0;
                    result = InputSimulationActions.SimulateMouseDrag(
                        float.Parse(cmd.scope.objectNames[0]),
                        float.Parse(cmd.scope.objectNames[1]),
                        float.Parse(cmd.scope.objectNames[2]),
                        float.Parse(cmd.scope.objectNames[3]),
                        dragSteps, dragBtn);
                    break;
                case "gamepad":
                    if (cmd.scope.objectNames == null || cmd.scope.objectNames.Length < 2)
                        return FormatError("gamepad requires scope.object_names[0]=controlPath, [1]=value");
                    result = InputSimulationActions.SimulateGamepadInput(
                        cmd.scope.objectNames[0], cmd.scope.objectNames[1]);
                    break;
                case "input_action":
                    if (cmd.scope.objectNames == null || cmd.scope.objectNames.Length < 2)
                        return FormatError("input_action requires scope.object_names[0]=actionMapName, [1]=actionName. Optional [2]=value");
                    string actionValue = cmd.scope.objectNames.Length > 2 ? cmd.scope.objectNames[2] : "true";
                    result = InputSimulationActions.SimulateInputAction(
                        cmd.scope.objectNames[0], cmd.scope.objectNames[1], actionValue);
                    break;
                default:
                    return FormatError($"Unknown input_simulation_actions mode: '{cmd.mode}'. Valid: key_press, get_info, mouse_click, mouse_move, mouse_drag, gamepad, input_action");
            }

            return result.Success ? result.Message : FormatError(result.Message);
        }

        private static string DispatchBuildPipelineHooks(BridgeCommand cmd)
        {
            ActionResult result;

            switch (cmd.mode?.ToLowerInvariant())
            {
                case "get_status":
                    result = BuildPipelineHooks.GetStatus();
                    break;
                case "enable":
                    result = BuildPipelineHooks.SetEnabled(true);
                    break;
                case "disable":
                    result = BuildPipelineHooks.SetEnabled(false);
                    break;
                case "validate":
                    result = BuildPipelineHooks.RunPreBuildValidation();
                    break;
                default:
                    return FormatError($"Unknown build_pipeline_hooks mode: '{cmd.mode}'. Valid: get_status, enable, disable, validate");
            }

            return result.Success ? result.Message : FormatError(result.Message);
        }

        private static string DispatchMultiplayerActions(BridgeCommand cmd)
        {
            ActionResult result;

            switch (cmd.mode?.ToLowerInvariant())
            {
                case "get_status":
                    result = MultiplayerActions.GetMultiplayerStatus();
                    break;
                case "run_test":
                    result = MultiplayerActions.RunMultiplayerTest();
                    break;
                case "configure_players":
                    int playerCount = 2;
                    if (cmd.scope.objectNames?.Length > 0)
                        int.TryParse(cmd.scope.objectNames[0], out playerCount);
                    string[] playerTags = cmd.scope.objectNames?.Skip(1).ToArray();
                    result = MultiplayerActions.ConfigurePlayers(playerCount, playerTags?.Length > 0 ? playerTags : null);
                    break;
                case "set_player_active":
                    if (cmd.scope.objectNames == null || cmd.scope.objectNames.Length < 2)
                        return FormatError("set_player_active requires scope.object_names[0]=playerIndex, [1]='true'/'false'");
                    result = MultiplayerActions.SetPlayerActive(
                        int.Parse(cmd.scope.objectNames[0]),
                        cmd.scope.objectNames[1].ToLower() == "true");
                    break;
                case "get_player_logs":
                    int logPlayerIndex = 0;
                    if (cmd.scope.objectNames?.Length > 0)
                        int.TryParse(cmd.scope.objectNames[0], out logPlayerIndex);
                    result = MultiplayerActions.GetPlayerLogs(
                        logPlayerIndex,
                        NullIfEmpty(cmd.scope.tagFilter));
                    break;
                default:
                    return FormatError($"Unknown multiplayer_actions mode: '{cmd.mode}'. Valid: get_status, run_test, configure_players, set_player_active, get_player_logs");
            }

            return result.Success ? result.Message : FormatError(result.Message);
        }

        private static string DispatchVisionAnalysis(BridgeCommand cmd)
        {
            ActionResult result;

            switch (cmd.mode?.ToLowerInvariant())
            {
                case "capture_analyze":
                case "analyze":
                    result = VisionAnalysis.CaptureAndAnalyze(
                        source: NullIfEmpty(cmd.scope.sceneName) ?? "scene");
                    break;
                case "analyze_screenshot":
                    result = VisionAnalysis.AnalyzeScreenshot(NullIfEmpty(cmd.scope.assetPath));
                    break;
                case "compare":
                    result = VisionAnalysis.CompareScreenshots(cmd.scope.rootPath, cmd.scope.assetPath);
                    break;
                default:
                    return FormatError($"Unknown vision_analysis mode: '{cmd.mode}'. Valid: capture_analyze, analyze_screenshot, compare");
            }

            return result.Success ? result.Message : FormatError(result.Message);
        }

        private static string DispatchSentisActions(BridgeCommand cmd)
        {
            ActionResult result;

            switch (cmd.mode?.ToLowerInvariant())
            {
                case "get_status":
                    result = SentisActions.GetSentisStatus();
                    break;
                case "run_image_model":
                    result = SentisActions.RunImageModel(cmd.scope.assetPath, cmd.scope.rootPath);
                    break;
                case "run_model":
                    return FormatError("run_model requires float[] inputData and shape string. Use direct C# call: SentisActions.RunModel(modelPath, inputData, inputShape)");
                default:
                    return FormatError($"Unknown sentis_actions mode: '{cmd.mode}'. Valid: get_status, run_image_model, run_model");
            }

            return result.Success ? result.Message : FormatError(result.Message);
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Output Handling
        // ─────────────────────────────────────────────────────────────────────

        private static string HandleOutput(BridgeCommand cmd, string reportPath)
        {
            if (string.IsNullOrEmpty(reportPath))
                return FormatError("Tool produced no output.");

            switch (cmd.output.destination)
            {
                case "return":
                    if (File.Exists(reportPath))
                        return File.ReadAllText(reportPath);
                    return FormatError($"Report file not found: {reportPath}");

                case "console":
                    if (File.Exists(reportPath))
                        Debug.Log(File.ReadAllText(reportPath));
                    return reportPath;

                case "file":
                default:
                    return reportPath;
            }
        }

        /// <summary>
        /// Calls a void diagnostic tool, then returns the path of the most recently written file
        /// in AgentReports. Used for tools whose GenerateReport returns void instead of string.
        /// </summary>
        private static string CallVoidAndCapture(BridgeCommand cmd, Action action)
        {
            string reportsDir = OutputWriter.ReportsRoot;
            DateTime before   = DateTime.Now.AddMilliseconds(-200);

            action();

            if (!Directory.Exists(reportsDir))
                return FormatError("Reports directory not found after tool execution.");

            var newest = Directory.GetFiles(reportsDir)
                .Select(f => new FileInfo(f))
                .Where(f => f.LastWriteTime > before)
                .OrderByDescending(f => f.LastWriteTime)
                .FirstOrDefault();

            if (newest == null)
                return FormatError("Tool produced no output file.");

            return HandleOutput(cmd, newest.FullName);
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Helpers
        // ─────────────────────────────────────────────────────────────────────

        private static string FormatError(string message)
            => $"{{\"error\": \"{EscapeJson(message)}\"}}";

        private static string NullIfEmpty(string s)
            => string.IsNullOrEmpty(s) ? null : s;

        private static string EscapeJson(string s)
            => s?.Replace("\\", "\\\\").Replace("\"", "\\\"") ?? "";

        // ─────────────────────────────────────────────────────────────────────
        //  Menu Item — Manual Testing
        // ─────────────────────────────────────────────────────────────────────

        [MenuItem("Axiom/AgentBridge/Gateway \u2014 Test Command")]
        public static void MenuTestGateway()
        {
            string testJson = @"{
    ""tool"": ""hierarchy_lens"",
    ""mode"": ""structure"",
    ""output"": { ""destination"": ""file"" }
}";
            string result = Execute(testJson);
            Debug.Log($"[AgentBridge Gateway] Result: {result}");
        }
    }
}
