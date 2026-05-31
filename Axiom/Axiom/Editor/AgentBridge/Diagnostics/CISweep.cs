using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEditor;
using Axiom.Editor.AgentBridge.Core;

namespace Axiom.Editor.AgentBridge.Diagnostics
{
    /// <summary>
    /// Master CI orchestrator — runs a configurable set of diagnostic tools and produces
    /// a single unified health report. Enables the agent's continuous integration loop:
    /// one command, full project status.
    /// </summary>
    public static class CISweep
    {
        public enum CISweepMode
        {
            QuickHealthCheck,      // Mode A: fastest checks — "is the project broken?"
            FullProjectAudit,      // Mode B: all diagnostics with unified report
            PreReleaseChecklist    // Mode C: everything + build readiness verdict
        }

        // ─────────────────────────────────────────────────────
        //  Menu Items
        // ─────────────────────────────────────────────────────

        [MenuItem("Axiom/AgentBridge/CI Sweep — Mode A (Quick Health Check)")]
        public static void ModeA() => GenerateReport(CISweepMode.QuickHealthCheck);

        [MenuItem("Axiom/AgentBridge/CI Sweep — Mode B (Full Project Audit)")]
        public static void ModeB() => GenerateReport(CISweepMode.FullProjectAudit);

        [MenuItem("Axiom/AgentBridge/CI Sweep — Mode C (Pre-Release Checklist)")]
        public static void ModeC() => GenerateReport(CISweepMode.PreReleaseChecklist);

        // ─────────────────────────────────────────────────────
        //  Public Entry Point
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// Runs a CI sweep and saves a unified health report.
        /// </summary>
        /// <param name="mode">Detail level of the sweep.</param>
        /// <returns>File path of the generated unified report.</returns>
        public static string GenerateReport(CISweepMode mode)
        {
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            string folderTs  = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");

            string content;
            switch (mode)
            {
                case CISweepMode.QuickHealthCheck:
                    content = BuildQuickHealthCheck(timestamp);
                    break;
                case CISweepMode.FullProjectAudit:
                    content = BuildFullProjectAudit(timestamp, folderTs);
                    break;
                case CISweepMode.PreReleaseChecklist:
                    content = BuildPreReleaseChecklist(timestamp, folderTs);
                    break;
                default:
                    content = "# CI Sweep — Unknown Mode";
                    break;
            }

            return OutputWriter.WriteReport("ci_sweep", content);
        }

        // ─────────────────────────────────────────────────────
        //  Mode A: Quick Health Check
        // ─────────────────────────────────────────────────────

        private static string BuildQuickHealthCheck(string timestamp)
        {
            var sb     = new StringBuilder();
            var rows   = new List<(string check, string result, string details)>();
            var issues = new List<string>();

            sb.AppendLine("## CI Quick Health Check");
            sb.AppendLine();
            sb.AppendLine($"**Project:** {Application.productName} | **Unity:** {Application.unityVersion} | **Time:** {timestamp}");
            sb.AppendLine();

            // Compilation
            string compilePath  = LogMirror.GenerateReport(LogMirror.LogMirrorMode.CompilationReport);
            string compileText  = ReadReport(compilePath);
            bool   compileClean = compileText.Contains("**Status:** CLEAN");
            rows.Add(("Compilation", compileClean ? "✓ CLEAN" : "✗ ERRORS", ExtractCompilationDetails(compileText)));
            if (!compileClean)
                issues.Add("Compilation errors detected — run `LogMirror.GenerateReport(ErrorsOnly)` for details.");

            // Missing References (Scene)
            string sceneRefPath = ReferenceScanner.GenerateReport(ReferenceScannerMode.SceneReferences);
            int    sceneRefs    = CountPattern(ReadReport(sceneRefPath), "missing");
            rows.Add(("Missing References (Scene)",
                sceneRefs == 0 ? "✓ CLEAN" : "⚠ WARNING",
                sceneRefs == 0 ? "0 missing references" : $"{sceneRefs} missing reference(s)"));
            if (sceneRefs > 0)
                issues.Add("Run `ReferenceScanner.GenerateReport(SceneReferences)` for details.");

            // Missing Scripts
            string msPath       = ReferenceScanner.GenerateReport(ReferenceScannerMode.MissingScripts);
            int    missingScripts = ParseMissingScriptCount(ReadReport(msPath));
            rows.Add(("Missing Scripts",
                missingScripts == 0 ? "✓ CLEAN" : "⚠ WARNING",
                missingScripts == 0 ? "0 GameObjects with missing scripts" : $"{missingScripts} GameObject(s) with missing scripts"));
            if (missingScripts > 0)
                issues.Add("Run `ReferenceScanner.GenerateReport(MissingScripts)` for details.");

            // Shader Errors
            DateTime shaderBefore = DateTime.Now;
            ShaderAuditor.GenerateReport(ShaderAuditor.ShaderAuditorMode.CompilationStatus);
            string shaderText   = ReadReport(FindNewestReport("shader_auditor_compilation", shaderBefore));
            int    shaderErrors = ParseShaderErrors(shaderText);
            rows.Add(("Shader Errors",
                shaderErrors == 0 ? "✓ CLEAN" : "✗ ERRORS",
                shaderErrors == 0 ? "0 compilation errors" : $"{shaderErrors} shader error(s)"));
            if (shaderErrors > 0)
                issues.Add("Run `ShaderAuditor.GenerateReport(CompilationStatus)` for details.");

            // Settings
            string settingsPath = SettingsReporter.GenerateReport(SettingsReporter.SettingsReporterMode.QuickSummary);
            string settingsText = ReadReport(settingsPath);
            rows.Add(("Settings", "✓ OK", ExtractSettingsSummary(settingsText)));

            // Header and table
            string overallStatus = issues.Count == 0 ? "✓ CLEAN" : $"⚠ WARNINGS ({issues.Count} issue(s))";
            sb.AppendLine($"### Status: {overallStatus}");
            sb.AppendLine();
            sb.AppendLine("| Check | Result | Details |");
            sb.AppendLine("| :--- | :--- | :--- |");
            foreach (var (check, result, details) in rows)
                sb.AppendLine($"| {check} | {result} | {details} |");

            if (issues.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("**Quick Fix:**");
                foreach (string fix in issues)
                    sb.AppendLine($"- {fix}");
            }

            sb.AppendLine();
            sb.AppendLine("---");
            sb.Append($"Generated: {timestamp}");
            return sb.ToString();
        }

        // ─────────────────────────────────────────────────────
        //  Mode B: Full Project Audit
        // ─────────────────────────────────────────────────────

        private static string BuildFullProjectAudit(string timestamp, string folderTs)
        {
            string subfolderPath = EnsureSubfolder(folderTs);
            var    sb            = new StringBuilder();
            var    summary       = new List<(string category, string status, string issues)>();
            var    recs          = new List<string>();

            sb.AppendLine("## CI Full Project Audit");
            sb.AppendLine();
            sb.AppendLine($"**Project:** {Application.productName} | **Time:** {timestamp}");
            sb.AppendLine();

            // 1. Compilation
            string compilePath  = LogMirror.GenerateReport(LogMirror.LogMirrorMode.CompilationReport);
            CopyToSubfolder(compilePath, subfolderPath);
            bool compileClean   = ReadReport(compilePath).Contains("**Status:** CLEAN");
            summary.Add(("Compilation", compileClean ? "✓" : "✗", compileClean ? "0 errors" : "compile errors detected"));
            if (!compileClean) recs.Add("**Fix compilation errors** — run `LogMirror.GenerateReport(ErrorsOnly)` for details");

            // 2. Scene References
            string sceneRefPath = ReferenceScanner.GenerateReport(ReferenceScannerMode.SceneReferences);
            CopyToSubfolder(sceneRefPath, subfolderPath);
            int sceneRefs = CountPattern(ReadReport(sceneRefPath), "missing");
            summary.Add(("Scene References", sceneRefs == 0 ? "✓" : "⚠", $"{sceneRefs} missing"));
            if (sceneRefs > 0) recs.Add($"**Fix {sceneRefs} missing scene reference(s)** — run `ReferenceScanner Mode A` for details");

            // 3. Prefab References
            string prefabRefPath = ReferenceScanner.GenerateReport(ReferenceScannerMode.PrefabReferences);
            CopyToSubfolder(prefabRefPath, subfolderPath);
            int prefabRefs = CountPattern(ReadReport(prefabRefPath), "missing");
            summary.Add(("Prefab References", prefabRefs == 0 ? "✓" : "⚠", $"{prefabRefs} missing reference(s) in prefabs"));
            if (prefabRefs > 0) recs.Add($"**Fix {prefabRefs} missing prefab reference(s)** — run `ReferenceScanner Mode B` for details");

            // 4. ScriptableObject References
            string soRefPath = ReferenceScanner.GenerateReport(ReferenceScannerMode.ScriptableObjectReferences);
            CopyToSubfolder(soRefPath, subfolderPath);
            int soRefs = CountPattern(ReadReport(soRefPath), "missing");
            summary.Add(("ScriptableObject Refs", soRefs == 0 ? "✓" : "⚠", $"{soRefs} missing"));

            // 5. Missing Scripts
            string msPath = ReferenceScanner.GenerateReport(ReferenceScannerMode.MissingScripts);
            CopyToSubfolder(msPath, subfolderPath);
            int missingScripts = ParseMissingScriptCount(ReadReport(msPath));
            summary.Add(("Missing Scripts", missingScripts == 0 ? "✓" : "⚠", $"{missingScripts} object(s)"));
            if (missingScripts > 0) recs.Add($"**Remove {missingScripts} missing script(s)** — run `ReferenceScanner Mode E` for details");

            // 6. Material References
            string matPath = ReferenceScanner.GenerateReport(ReferenceScannerMode.MaterialAudit);
            CopyToSubfolder(matPath, subfolderPath);
            int pinkShaders = CountPattern(ReadReport(matPath), "missing shader");
            summary.Add(("Material References", pinkShaders == 0 ? "✓" : "⚠", $"{pinkShaders} pink shader(s)"));

            // 7. Shader Compilation
            DateTime shaderBefore = DateTime.Now;
            ShaderAuditor.GenerateReport(ShaderAuditor.ShaderAuditorMode.CompilationStatus);
            string shaderCompPath = FindNewestReport("shader_auditor_compilation", shaderBefore);
            CopyToSubfolder(shaderCompPath, subfolderPath);
            int shaderErrors = ParseShaderErrors(ReadReport(shaderCompPath));
            summary.Add(("Shader Compilation", shaderErrors == 0 ? "✓" : "⚠", $"{shaderErrors} errors, see report for warnings"));

            // 8. Shader Variants
            DateTime kwBefore = DateTime.Now;
            ShaderAuditor.GenerateReport(ShaderAuditor.ShaderAuditorMode.KeywordReport);
            string kwPath      = FindNewestReport("shader_auditor_keywords", kwBefore);
            CopyToSubfolder(kwPath, subfolderPath);
            string variantLine = ExtractFirstMatchingLine(ReadReport(kwPath), "variant");
            summary.Add(("Shader Variants", "—", string.IsNullOrEmpty(variantLine) ? "see keyword report" : variantLine));
            if (!string.IsNullOrEmpty(variantLine) && variantLine.Length > 40)
                recs.Add("**Consider stripping shader variants** — see shader keyword report for high variant counts");

            // 9. Physics
            string physicsPath = PhysicsReporter.GenerateReport(PhysicsReporter.PhysicsReporterMode.ColliderCensus);
            CopyToSubfolder(physicsPath, subfolderPath);
            summary.Add(("Physics Setup", "✓", ExtractPhysicsSummary(ReadReport(physicsPath))));

            // 10. Audio
            DateTime audioBefore = DateTime.Now;
            AudioReporter.GenerateReport(AudioReporter.AudioReporterMode.SourceCensus);
            string audioPath = FindNewestReport("audio_reporter_sourcecensus", audioBefore);
            CopyToSubfolder(audioPath, subfolderPath);

            DateTime clipBefore = DateTime.Now;
            AudioReporter.GenerateReport(AudioReporter.AudioReporterMode.ClipImportAudit);
            CopyToSubfolder(FindNewestReport("audio_reporter_clipaudit", clipBefore), subfolderPath);

            summary.Add(("Audio Setup", "✓", ExtractAudioSummary(ReadReport(audioPath))));

            // 11. Navigation
            DateTime navBefore = DateTime.Now;
            NavMeshInspector.GenerateReport(NavMeshInspector.NavMeshInspectorMode.AgentTypes);
            string navPath = FindNewestReport("navmesh_inspector_agenttypes", navBefore);
            CopyToSubfolder(navPath, subfolderPath);

            DateTime surfBefore = DateTime.Now;
            NavMeshInspector.GenerateReport(NavMeshInspector.NavMeshInspectorMode.SurfaceReport);
            CopyToSubfolder(FindNewestReport("navmesh_inspector_surface", surfBefore), subfolderPath);

            summary.Add(("Navigation", "✓", ExtractNavSummary(ReadReport(navPath))));

            // 12. Animation
            string animPath = AnimationInspector.GenerateReport(AnimationInspector.AnimationInspectorMode.ControllerOverview);
            CopyToSubfolder(animPath, subfolderPath);
            summary.Add(("Animation", "✓", ExtractAnimSummary(ReadReport(animPath))));

            // 13. Accessibility
            string accessPath = AccessibilityValidator.GenerateReport(AccessibilityValidator.AccessibilityMode.ScreenReaderCompatibility);
            CopyToSubfolder(accessPath, subfolderPath);
            int unlabeled = ParseAccessibilityIssues(ReadReport(accessPath));
            summary.Add(("Accessibility", unlabeled == 0 ? "✓" : "⚠", $"{unlabeled} interactive element(s) missing labels"));
            if (unlabeled > 0) recs.Add($"**Add accessibility labels** to {unlabeled} interactive UI elements");

            // 14. Render Pipeline
            DateTime renderBefore = DateTime.Now;
            RenderAuditor.GenerateReport(RenderAuditor.RenderAuditorMode.PipelineSummary);
            CopyToSubfolder(FindNewestReport("render_auditor_pipeline", renderBefore), subfolderPath);
            summary.Add(("Build Readiness", recs.Count == 0 ? "✓" : "⚠", recs.Count == 0 ? "No blocking issues" : "See recommendations"));

            // Output
            sb.AppendLine("### Summary");
            sb.AppendLine("| Category | Status | Issues |");
            sb.AppendLine("| :--- | :--- | :--- |");
            foreach (var (cat, stat, iss) in summary)
                sb.AppendLine($"| {cat} | {stat} | {iss} |");

            if (recs.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("### Recommendations (Priority Order)");
                for (int i = 0; i < recs.Count; i++)
                    sb.AppendLine($"{i + 1}. {recs[i]}");
            }

            sb.AppendLine();
            sb.AppendLine("### Individual Reports");
            sb.AppendLine($"All detailed reports saved to: AgentReports/ci_sweep_{folderTs}/");
            sb.AppendLine();
            sb.AppendLine("---");
            sb.Append($"Generated: {timestamp}");
            return sb.ToString();
        }

        // ─────────────────────────────────────────────────────
        //  Mode C: Pre-Release Checklist
        // ─────────────────────────────────────────────────────

        private static string BuildPreReleaseChecklist(string timestamp, string folderTs)
        {
            string subfolderPath = EnsureSubfolder(folderTs);
            var    sb            = new StringBuilder();
            var    checklist     = new List<(int num, string check, bool pass, bool warnOnly, string note)>();
            int    n             = 1;

            sb.AppendLine("## CI Pre-Release Checklist");
            sb.AppendLine();

            sb.AppendLine("### Build Configuration");
            sb.AppendLine($"- Platform: {EditorUserBuildSettings.activeBuildTarget}");
            sb.AppendLine($"- Scripting Backend: {PlayerSettings.GetScriptingBackend(EditorUserBuildSettings.selectedBuildTargetGroup)}");
            sb.AppendLine($"- Managed Stripping: {PlayerSettings.GetManagedStrippingLevel(EditorUserBuildSettings.selectedBuildTargetGroup)}");
            sb.AppendLine();

            // 1. No compile errors
            string compilePath  = LogMirror.GenerateReport(LogMirror.LogMirrorMode.CompilationReport);
            CopyToSubfolder(compilePath, subfolderPath);
            bool compileClean   = ReadReport(compilePath).Contains("**Status:** CLEAN");
            checklist.Add((n++, "No compile errors", compileClean, false, ""));

            // 2. No missing references
            string sceneRefPath  = ReferenceScanner.GenerateReport(ReferenceScannerMode.SceneReferences);
            string prefabRefPath = ReferenceScanner.GenerateReport(ReferenceScannerMode.PrefabReferences);
            CopyToSubfolder(sceneRefPath, subfolderPath);
            CopyToSubfolder(prefabRefPath, subfolderPath);
            int totalMissing = CountPattern(ReadReport(sceneRefPath), "missing")
                             + CountPattern(ReadReport(prefabRefPath), "missing");
            checklist.Add((n++, "No missing references", totalMissing == 0, false,
                totalMissing > 0 ? $"{totalMissing} in scene/prefabs" : ""));

            // 3. No missing scripts
            string msPath        = ReferenceScanner.GenerateReport(ReferenceScannerMode.MissingScripts);
            CopyToSubfolder(msPath, subfolderPath);
            int missingScripts   = ParseMissingScriptCount(ReadReport(msPath));
            checklist.Add((n++, "No missing scripts", missingScripts == 0, false,
                missingScripts > 0 ? $"{missingScripts} objects" : ""));

            // 4. No shader errors
            DateTime shaderBefore = DateTime.Now;
            ShaderAuditor.GenerateReport(ShaderAuditor.ShaderAuditorMode.CompilationStatus);
            string shaderPath   = FindNewestReport("shader_auditor_compilation", shaderBefore);
            CopyToSubfolder(shaderPath, subfolderPath);
            int shaderErrors    = ParseShaderErrors(ReadReport(shaderPath));
            checklist.Add((n++, "No shader errors", shaderErrors == 0, false, ""));

            // 5. Build profile configured
            bool hasTarget = EditorUserBuildSettings.activeBuildTarget != BuildTarget.NoTarget;
            checklist.Add((n++, "Build Profile configured", hasTarget, false,
                hasTarget ? EditorUserBuildSettings.activeBuildTarget.ToString() : "No target set"));

            // 6. Scenes in build list
            int enabledScenes = EditorBuildSettings.scenes.Count(s => s.enabled);
            checklist.Add((n++, "Scenes in build list", enabledScenes > 0, false, $"{enabledScenes} scene(s)"));

            // 7. No orphan assets > 10 MB
            string orphanPath   = ProjectCartographer.GenerateReport(CartographerMode.OrphanSearch);
            CopyToSubfolder(orphanPath, subfolderPath);
            int largeOrphans    = ParseLargeOrphans(ReadReport(orphanPath), 10f);
            checklist.Add((n++, "No orphan assets > 10MB", largeOrphans == 0, largeOrphans > 0,
                largeOrphans > 0 ? $"{largeOrphans} unused asset(s)" : ""));

            // 8. Test suite
            string testPath     = TestRunner.GenerateReport(TestRunner.TestRunnerMode.TestList);
            CopyToSubfolder(testPath, subfolderPath);
            (int totalTests, int skipped) = ParseTestCounts(ReadReport(testPath));
            checklist.Add((n++, "Test suite passes", skipped == 0, skipped > 0,
                skipped > 0 ? $"{skipped} test(s) skipped" : $"{totalTests} test(s)"));

            // 9. GPU Resident Drawer compat
            DateTime gpuBefore  = DateTime.Now;
            RenderAuditor.GenerateReport(RenderAuditor.RenderAuditorMode.GPUResidentDrawerCompatibility);
            string gpuPath      = FindNewestReport("render_auditor_gpuresident", gpuBefore);
            CopyToSubfolder(gpuPath, subfolderPath);
            string gpuSummary   = ExtractFirstMatchingLine(ReadReport(gpuPath), "compat");
            checklist.Add((n++, "GPU Resident Drawer compat", true, false,
                string.IsNullOrEmpty(gpuSummary) ? "see GPU compat report" : gpuSummary));

            // 10. Accessibility baseline
            string accessPath   = AccessibilityValidator.GenerateReport(AccessibilityValidator.AccessibilityMode.ScreenReaderCompatibility);
            CopyToSubfolder(accessPath, subfolderPath);
            int unlabeled       = ParseAccessibilityIssues(ReadReport(accessPath));
            checklist.Add((n++, "Accessibility baseline", unlabeled == 0, unlabeled > 0,
                unlabeled > 0 ? $"{unlabeled} unlabeled interactive(s)" : ""));

            // Render checklist
            sb.AppendLine("### Checklist");
            sb.AppendLine("| # | Check | Status | Note |");
            sb.AppendLine("| :--- | :--- | :--- | :--- |");
            bool ready      = true;
            var  failedNums = new List<int>();
            foreach (var (num, check, pass, warnOnly, note) in checklist)
            {
                string status;
                if (pass)         status = "✓";
                else if (warnOnly) status = "⚠";
                else
                {
                    status = "✗ FAIL";
                    ready  = false;
                    failedNums.Add(num);
                }
                sb.AppendLine($"| {num} | {check} | {status} | {note} |");
            }

            sb.AppendLine();
            sb.AppendLine(ready
                ? "### VERDICT: READY FOR RELEASE"
                : $"### VERDICT: NOT READY — Fix items {string.Join(", ", failedNums)} before building");

            sb.AppendLine();
            sb.AppendLine("### Individual Reports");
            sb.AppendLine($"All detailed reports saved to: AgentReports/ci_sweep_{folderTs}/");
            sb.AppendLine();
            sb.AppendLine("---");
            sb.Append($"Generated: {timestamp}");
            return sb.ToString();
        }

        // ─────────────────────────────────────────────────────
        //  Subfolder helpers
        // ─────────────────────────────────────────────────────

        private static string EnsureSubfolder(string folderTs)
        {
            string path = Path.Combine(OutputWriter.ReportsRoot, $"ci_sweep_{folderTs}");
            if (!Directory.Exists(path))
                Directory.CreateDirectory(path);
            return path;
        }

        private static void CopyToSubfolder(string sourcePath, string subfolderPath)
        {
            if (string.IsNullOrEmpty(sourcePath) || !File.Exists(sourcePath) || !Directory.Exists(subfolderPath)) return;
            File.Copy(sourcePath, Path.Combine(subfolderPath, Path.GetFileName(sourcePath)), overwrite: true);
        }

        private static string FindNewestReport(string prefix, DateTime notBefore)
        {
            if (!Directory.Exists(OutputWriter.ReportsRoot)) return null;
            return Directory.GetFiles(OutputWriter.ReportsRoot, $"{prefix}*.md")
                .Select(f => new FileInfo(f))
                .Where(fi => fi.LastWriteTime >= notBefore)
                .OrderByDescending(fi => fi.LastWriteTime)
                .Select(fi => fi.FullName)
                .FirstOrDefault();
        }

        private static string ReadReport(string filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) return string.Empty;
            return File.ReadAllText(filePath);
        }

        // ─────────────────────────────────────────────────────
        //  Metric parsers
        // ─────────────────────────────────────────────────────

        private static int CountPattern(string text, string keyword)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(keyword)) return 0;
            int count = 0, idx = 0;
            while ((idx = text.IndexOf(keyword, idx, StringComparison.OrdinalIgnoreCase)) >= 0)
            { count++; idx += keyword.Length; }
            return count;
        }

        private static string ExtractCompilationDetails(string text)
        {
            if (text.Contains("**Status:** CLEAN"))       return "0 errors";
            if (text.Contains("**Status:** HAS_ERRORS"))  return "compile errors detected";
            if (text.Contains("**Status:** COMPILING"))   return "currently compiling";
            return "unknown";
        }

        private static string ExtractSettingsSummary(string text)
        {
            if (string.IsNullOrEmpty(text)) return "—";
            string pipeline   = ExtractLineValue(text, "Render Pipeline");
            string colorSpace = ExtractLineValue(text, "Color Space");
            string backend    = ExtractLineValue(text, "Scripting Backend");
            return $"{pipeline}, {colorSpace}, {backend}";
        }

        private static string ExtractLineValue(string text, string label)
        {
            foreach (string line in text.Split('\n'))
            {
                if (!line.Contains(label)) continue;
                int colon = line.IndexOf(':');
                if (colon >= 0) return line.Substring(colon + 1).Trim().TrimEnd('\r');
            }
            return "—";
        }

        private static int ParseMissingScriptCount(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            foreach (string line in text.Split('\n'))
            {
                if (!line.Contains("missing script", StringComparison.OrdinalIgnoreCase)) continue;
                string digits = new string(line.Where(char.IsDigit).ToArray());
                if (int.TryParse(digits, out int n) && n >= 0) return n;
            }
            return CountPattern(text, "Missing Script");
        }

        private static int ParseShaderErrors(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            foreach (string line in text.Split('\n'))
            {
                bool hasError = line.IndexOf("error", StringComparison.OrdinalIgnoreCase) >= 0;
                bool hasCount = line.IndexOf("0 error", StringComparison.OrdinalIgnoreCase) >= 0
                             || line.IndexOf("Error Count", StringComparison.OrdinalIgnoreCase) >= 0
                             || line.IndexOf("Errors:", StringComparison.OrdinalIgnoreCase) >= 0;
                if (hasError && hasCount)
                {
                    string digits = new string(line.Where(char.IsDigit).ToArray());
                    if (int.TryParse(digits, out int n)) return n;
                }
            }
            return CountPattern(text, "error CS");
        }

        private static string ExtractPhysicsSummary(string text)
        {
            int count = CountPattern(text, "Collider");
            return count > 0 ? $"{count} collider(s) found" : "see physics report";
        }

        private static string ExtractAudioSummary(string text)
        {
            int count = CountPattern(text, "AudioSource");
            return count > 0 ? $"{count} source(s)" : "see audio report";
        }

        private static string ExtractNavSummary(string text)
        {
            int count = CountPattern(text, "Agent Type");
            return count > 0 ? $"{count} agent type(s)" : "see navmesh report";
        }

        private static string ExtractAnimSummary(string text)
        {
            int count = CountPattern(text, "Animator Controller");
            return count > 0 ? $"{count} controller(s)" : "see animation report";
        }

        private static int ParseAccessibilityIssues(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            return CountPattern(text, "no tooltip")
                 + CountPattern(text, "missing label")
                 + CountPattern(text, "unlabeled");
        }

        private static int ParseLargeOrphans(string text, float thresholdMB)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            int count = 0;
            foreach (string line in text.Split('\n'))
            {
                if (!line.Contains("MB")) continue;
                int mbIdx = line.IndexOf("MB", StringComparison.OrdinalIgnoreCase);
                string before  = line.Substring(0, mbIdx).TrimEnd();
                int    spaceIdx = before.LastIndexOf(' ');
                string numStr   = spaceIdx >= 0 ? before.Substring(spaceIdx + 1) : before;
                if (float.TryParse(numStr,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out float mb) && mb >= thresholdMB)
                    count++;
            }
            return count;
        }

        private static (int total, int skipped) ParseTestCounts(string text)
        {
            int total = 0, skipped = 0;
            if (string.IsNullOrEmpty(text)) return (0, 0);
            foreach (string line in text.Split('\n'))
            {
                bool isSkip  = line.IndexOf("Skipped", StringComparison.OrdinalIgnoreCase) >= 0
                            || line.IndexOf("Ignored",  StringComparison.OrdinalIgnoreCase) >= 0;
                bool isTotal = line.IndexOf("Total",    StringComparison.OrdinalIgnoreCase) >= 0
                            && line.IndexOf("test",     StringComparison.OrdinalIgnoreCase) >= 0;
                string digits = new string(line.Where(char.IsDigit).ToArray());
                if (!int.TryParse(digits, out int n)) continue;
                if (isSkip)  skipped += n;
                if (isTotal) total    = n;
            }
            return (total, skipped);
        }

        private static string ExtractFirstMatchingLine(string text, string keyword)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            foreach (string line in text.Split('\n'))
                if (line.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                    return line.Trim().TrimEnd('\r');
            return string.Empty;
        }
    }
}
