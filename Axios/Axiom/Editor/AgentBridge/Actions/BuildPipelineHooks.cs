using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using Axiom.Editor.AgentBridge.Core;
using Axiom.Editor.AgentBridge.Diagnostics;

namespace Axiom.Editor.AgentBridge.Actions
{
    /// <summary>
    /// Automatically runs diagnostic checks before and after builds.
    /// Enable/disable via BuildPipelineHooks.SetEnabled(true/false).
    /// Configure which checks run via BuildPipelineHooks.Configure().
    /// </summary>
    public class BuildPipelineHooks : IPreprocessBuildWithReport, IPostprocessBuildWithReport
    {
        public int callbackOrder => 0;

        // ─────────────────────────────────────────────────────
        //  Static Configuration
        // ─────────────────────────────────────────────────────

        private static bool s_Enabled = false;
        private static bool s_RunReferenceScanner = true;
        private static bool s_RunShaderAuditor = true;
        private static bool s_RunMissingScripts = true;
        private static bool s_FailOnMissingReferences = false;

        // ─────────────────────────────────────────────────────
        //  2.1 SetEnabled
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// Enables or disables the automatic build validation hooks.
        /// When enabled, diagnostics run before every build.
        /// </summary>
        public static ActionResult SetEnabled(bool enabled)
        {
            s_Enabled = enabled;
            string status = enabled ? "enabled" : "disabled";
            Debug.Log($"[BuildPipelineHooks] Build validation hooks {status}.");
            return ActionResult.Ok($"Build validation hooks {status}.");
        }

        // ─────────────────────────────────────────────────────
        //  2.2 Configure
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// Configures which diagnostics run during build validation.
        /// </summary>
        /// <param name="runReferenceScanner">Check for missing references before build.</param>
        /// <param name="runShaderAuditor">Check for shader errors before build.</param>
        /// <param name="runMissingScripts">Check for missing MonoBehaviours.</param>
        /// <param name="failOnMissingReferences">If true, abort the build when missing references found.</param>
        public static ActionResult Configure(
            bool runReferenceScanner = true,
            bool runShaderAuditor = true,
            bool runMissingScripts = true,
            bool failOnMissingReferences = false)
        {
            s_RunReferenceScanner = runReferenceScanner;
            s_RunShaderAuditor = runShaderAuditor;
            s_RunMissingScripts = runMissingScripts;
            s_FailOnMissingReferences = failOnMissingReferences;

            var sb = new StringBuilder();
            sb.AppendLine("Build validation configuration updated:");
            sb.AppendLine($"  - Reference Scanner: {runReferenceScanner}");
            sb.AppendLine($"  - Shader Auditor: {runShaderAuditor}");
            sb.AppendLine($"  - Missing Scripts: {runMissingScripts}");
            sb.AppendLine($"  - Fail on Missing References: {failOnMissingReferences}");

            string msg = sb.ToString().TrimEnd();
            Debug.Log($"[BuildPipelineHooks] {msg}");
            return ActionResult.Ok(msg);
        }

        // ─────────────────────────────────────────────────────
        //  2.3 OnPreprocessBuild
        // ─────────────────────────────────────────────────────

        public void OnPreprocessBuild(BuildReport report)
        {
            if (!s_Enabled) return;

            Debug.Log("[BuildPipelineHooks] Running pre-build validation...");

            var sb = new StringBuilder();
            sb.AppendLine("# Pre-Build Validation Report\n");
            sb.AppendLine($"**Target:** {report.summary.platform}");
            sb.AppendLine($"**Time:** {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n");

            bool hasErrors = false;
            int missingRefCount = 0;
            int missingScriptCount = 0;
            int shaderErrorCount = 0;

            // Run Reference Scanner
            if (s_RunReferenceScanner)
            {
                sb.AppendLine("## Reference Scanner — Scene References\n");
                try
                {
                    string reportPath = ReferenceScanner.GenerateReport(ReferenceScannerMode.SceneReferences);
                    string reportContent = System.IO.File.ReadAllText(reportPath);

                    // Parse for missing references
                    if (reportContent.Contains("Missing") || reportContent.Contains("None"))
                    {
                        // Count occurrences of "Missing" or "None" in property values
                        missingRefCount = CountOccurrences(reportContent, "None (");
                        if (missingRefCount > 0)
                        {
                            hasErrors = true;
                            sb.AppendLine($"⚠️ **Found {missingRefCount} missing reference(s)**");
                        }
                        else
                        {
                            sb.AppendLine("✓ No missing references found");
                        }
                    }
                    else
                    {
                        sb.AppendLine("✓ No missing references found");
                    }

                    sb.AppendLine($"*See full report: {reportPath}*\n");
                }
                catch (Exception ex)
                {
                    sb.AppendLine($"❌ Reference Scanner failed: {ex.Message}\n");
                    hasErrors = true;
                }
            }

            // Run Missing Scripts Check
            if (s_RunMissingScripts)
            {
                sb.AppendLine("## Missing Scripts\n");
                try
                {
                    string reportPath = ReferenceScanner.GenerateReport(ReferenceScannerMode.MissingScripts);
                    string reportContent = System.IO.File.ReadAllText(reportPath);

                    if (reportContent.Contains("Missing (Mono Script)"))
                    {
                        missingScriptCount = CountOccurrences(reportContent, "Missing (Mono Script)");
                        hasErrors = true;
                        sb.AppendLine($"⚠️ **Found {missingScriptCount} missing script(s)**");
                    }
                    else
                    {
                        sb.AppendLine("✓ No missing scripts found");
                    }

                    sb.AppendLine($"*See full report: {reportPath}*\n");
                }
                catch (Exception ex)
                {
                    sb.AppendLine($"❌ Missing Scripts check failed: {ex.Message}\n");
                    hasErrors = true;
                }
            }

            // Run Shader Auditor
            if (s_RunShaderAuditor)
            {
                sb.AppendLine("## Shader Auditor — Compilation Status\n");
                try
                {
                    ShaderAuditor.GenerateReport(ShaderAuditor.ShaderAuditorMode.CompilationStatus);
                    // Note: ShaderAuditor doesn't return a path, it writes directly via OutputWriter
                    // For now, just log that it ran
                    sb.AppendLine("✓ Shader compilation check completed");
                    sb.AppendLine("*See AgentReports/shader_auditor_compilation report for details*\n");
                }
                catch (Exception ex)
                {
                    sb.AppendLine($"❌ Shader Auditor failed: {ex.Message}\n");
                    hasErrors = true;
                }
            }

            // Summary
            sb.AppendLine("## Summary\n");
            if (hasErrors)
            {
                sb.AppendLine($"❌ **Validation failed:** {missingRefCount} missing ref(s), {missingScriptCount} missing script(s)");

                if (s_FailOnMissingReferences)
                {
                    sb.AppendLine("\n**Build aborted due to validation failures.**");
                }
            }
            else
            {
                sb.AppendLine("✅ **All validation checks passed**");
            }

            string finalReport = sb.ToString();
            string reportFile = OutputWriter.WriteReport("pre_build_validation", finalReport);
            Debug.Log($"[BuildPipelineHooks] Pre-build validation complete. Report: {reportFile}");

            if (hasErrors && s_FailOnMissingReferences)
            {
                throw new BuildFailedException($"Pre-build validation failed. See report: {reportFile}");
            }
        }

        // ─────────────────────────────────────────────────────
        //  2.4 OnPostprocessBuild
        // ─────────────────────────────────────────────────────

        public void OnPostprocessBuild(BuildReport report)
        {
            if (!s_Enabled) return;

            Debug.Log("[BuildPipelineHooks] Running post-build analysis...");

            var sb = new StringBuilder();
            sb.AppendLine("# Post-Build Report\n");
            sb.AppendLine($"**Result:** {report.summary.result}");
            sb.AppendLine($"**Platform:** {report.summary.platform}");
            sb.AppendLine($"**Size:** {report.summary.totalSize / (1024f * 1024f):F2} MB");
            sb.AppendLine($"**Time:** {report.summary.totalTime}");
            sb.AppendLine($"**Errors:** {report.summary.totalErrors}");
            sb.AppendLine($"**Warnings:** {report.summary.totalWarnings}");
            sb.AppendLine();

            // Build profile info (Unity 6+)
#if UNITY_6000_0_OR_NEWER
            try
            {
                var profileResult = BuildProfileActions.AnalyzeBuildReport();
                sb.AppendLine("## Build Analysis\n");
                sb.AppendLine(profileResult.Success
                    ? $"✓ {profileResult.Message}"
                    : $"⚠️ {profileResult.Message}");
                sb.AppendLine();
            }
            catch (Exception ex)
            {
                sb.AppendLine($"## Build Analysis\n\n⚠️ Analysis failed: {ex.Message}\n");
            }
#else
            sb.AppendLine("## Build Analysis\n");
            sb.AppendLine("*Build analysis requires Unity 6+*\n");
#endif

            string finalReport = sb.ToString();
            string reportFile = OutputWriter.WriteReport("post_build_report", finalReport);
            Debug.Log($"[BuildPipelineHooks] Post-build analysis complete. Report: {reportFile}");
        }

        // ─────────────────────────────────────────────────────
        //  2.5 GetStatus
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// Returns current build hook configuration.
        /// </summary>
        public static ActionResult GetStatus()
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Build Pipeline Hooks — Status\n");
            sb.AppendLine($"**Enabled:** {s_Enabled}");
            sb.AppendLine($"**Run Reference Scanner:** {s_RunReferenceScanner}");
            sb.AppendLine($"**Run Shader Auditor:** {s_RunShaderAuditor}");
            sb.AppendLine($"**Run Missing Scripts:** {s_RunMissingScripts}");
            sb.AppendLine($"**Fail on Missing References:** {s_FailOnMissingReferences}");

            string report = sb.ToString().TrimEnd();
            string reportPath = OutputWriter.WriteReport("build_hooks_status", report);
            return ActionResult.Ok($"Status report: {reportPath}");
        }

        // ─────────────────────────────────────────────────────
        //  2.6 RunPreBuildValidation
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// Manually triggers the pre-build validation without actually building.
        /// Useful for the agent to check project health before triggering a build.
        /// </summary>
        public static ActionResult RunPreBuildValidation()
        {
            if (!s_Enabled)
                return ActionResult.Fail("Build validation hooks are disabled. Call SetEnabled(true) first.");

            Debug.Log("[BuildPipelineHooks] Running manual pre-build validation...");

            var sb = new StringBuilder();
            sb.AppendLine("# Manual Pre-Build Validation\n");
            sb.AppendLine($"**Time:** {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n");

            bool hasErrors = false;
            int missingRefCount = 0;
            int missingScriptCount = 0;

            // Run Reference Scanner
            if (s_RunReferenceScanner)
            {
                sb.AppendLine("## Reference Scanner — Scene References\n");
                try
                {
                    string reportPath = ReferenceScanner.GenerateReport(ReferenceScannerMode.SceneReferences);
                    string reportContent = System.IO.File.ReadAllText(reportPath);

                    missingRefCount = CountOccurrences(reportContent, "None (");
                    if (missingRefCount > 0)
                    {
                        hasErrors = true;
                        sb.AppendLine($"⚠️ **Found {missingRefCount} missing reference(s)**");
                    }
                    else
                    {
                        sb.AppendLine("✓ No missing references found");
                    }

                    sb.AppendLine($"*See full report: {reportPath}*\n");
                }
                catch (Exception ex)
                {
                    sb.AppendLine($"❌ Reference Scanner failed: {ex.Message}\n");
                    hasErrors = true;
                }
            }

            // Run Missing Scripts Check
            if (s_RunMissingScripts)
            {
                sb.AppendLine("## Missing Scripts\n");
                try
                {
                    string reportPath = ReferenceScanner.GenerateReport(ReferenceScannerMode.MissingScripts);
                    string reportContent = System.IO.File.ReadAllText(reportPath);

                    missingScriptCount = CountOccurrences(reportContent, "Missing (Mono Script)");
                    if (missingScriptCount > 0)
                    {
                        hasErrors = true;
                        sb.AppendLine($"⚠️ **Found {missingScriptCount} missing script(s)**");
                    }
                    else
                    {
                        sb.AppendLine("✓ No missing scripts found");
                    }

                    sb.AppendLine($"*See full report: {reportPath}*\n");
                }
                catch (Exception ex)
                {
                    sb.AppendLine($"❌ Missing Scripts check failed: {ex.Message}\n");
                    hasErrors = true;
                }
            }

            // Run Shader Auditor
            if (s_RunShaderAuditor)
            {
                sb.AppendLine("## Shader Auditor — Compilation Status\n");
                try
                {
                    ShaderAuditor.GenerateReport(ShaderAuditor.ShaderAuditorMode.CompilationStatus);
                    sb.AppendLine("✓ Shader compilation check completed");
                    sb.AppendLine("*See AgentReports/shader_auditor_compilation report for details*\n");
                }
                catch (Exception ex)
                {
                    sb.AppendLine($"❌ Shader Auditor failed: {ex.Message}\n");
                    hasErrors = true;
                }
            }

            // Summary
            sb.AppendLine("## Summary\n");
            if (hasErrors)
            {
                sb.AppendLine($"❌ **Validation failed:** {missingRefCount} missing ref(s), {missingScriptCount} missing script(s)");
            }
            else
            {
                sb.AppendLine("✅ **All validation checks passed**");
            }

            string finalReport = sb.ToString();
            string reportFile = OutputWriter.WriteReport("manual_pre_build_validation", finalReport);

            if (hasErrors)
                return ActionResult.Fail($"Validation found issues. See report: {reportFile}");

            return ActionResult.Ok($"Validation passed. Report: {reportFile}");
        }

        // ─────────────────────────────────────────────────────
        //  Helper Methods
        // ─────────────────────────────────────────────────────

        private static int CountOccurrences(string text, string substring)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(substring))
                return 0;

            int count = 0;
            int index = 0;
            while ((index = text.IndexOf(substring, index, StringComparison.Ordinal)) != -1)
            {
                count++;
                index += substring.Length;
            }
            return count;
        }

        // ─────────────────────────────────────────────────────
        //  Menu Items
        // ─────────────────────────────────────────────────────

        [MenuItem("Axiom/AgentBridge/Actions/Build Validation Status")]
        public static void MenuGetStatus() => GetStatus();

        [MenuItem("Axiom/AgentBridge/Actions/Run Pre-Build Validation")]
        public static void MenuRunValidation() => RunPreBuildValidation();
    }
}
