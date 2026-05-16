using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using NUnit.Framework;
using Axiom.Editor.AgentBridge.Core;

namespace Axiom.Editor.AgentBridge.Diagnostics
{
    /// <summary>
    /// Provides the agent with the ability to list, run, and report on EditMode and PlayMode tests.
    /// </summary>
    public static class TestRunner
    {
        public enum TestRunnerMode
        {
            TestList,          // Mode A
            RunAll,            // Mode B
            RunFiltered,       // Mode C
            CoverageReport     // Mode D
        }

        private static ResultCollector s_ActiveCollector;

        /// <summary>
        /// Interacts with the Unity Test Runner.
        /// </summary>
        /// <param name="mode">Operation type.</param>
        /// <param name="testFilter">For Mode C: name or pattern to filter tests. Null = ignored.</param>
        /// <param name="categoryFilter">For Mode C: test category to filter by. Null = ignored.</param>
        /// <param name="editMode">True = EditMode tests, False = PlayMode tests. Default true.</param>
        /// <returns>File path of the generated report, or a status message for async modes.</returns>
        public static string GenerateReport(
            TestRunnerMode mode,
            string testFilter = null,
            string categoryFilter = null,
            bool editMode = true)
        {
            switch (mode)
            {
                case TestRunnerMode.TestList:
                    return RunTestList();
                case TestRunnerMode.RunAll:
                case TestRunnerMode.RunFiltered:
                    return StartTestRun(mode, testFilter, categoryFilter, editMode);
                case TestRunnerMode.CoverageReport:
                    return RunCoverageReport();
                default:
                    return string.Empty;
            }
        }

        // ─── Mode A: Test List ────────────────────────────────────────────────────

        private static string RunTestList()
        {
            var sb = new StringBuilder();
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

            sb.AppendLine("# Test Runner — Mode: Test List");
            sb.AppendLine();

            // Find test fixture types via TypeCache
            var fixtureTypes = TypeCache.GetTypesWithAttribute<TestFixtureAttribute>();

            // Also look for types with [Test] methods that don't have [TestFixture] (NUnit 3 allows this)
            var typesWithTestMethods = new HashSet<Type>();
            foreach (var t in fixtureTypes)
                typesWithTestMethods.Add(t);

            // Scan for any type with [Test] or [UnityTest] methods
            Type unityTestAttrType = Type.GetType("UnityEngine.TestTools.UnityTestAttribute, UnityEngine.TestRunner");

            // Scan all loaded assemblies for additional test types
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                string asmName = asm.GetName().Name;
                // Skip Unity built-ins and standard library
                if (asmName.StartsWith("Unity") || asmName.StartsWith("com.unity")
                    || asmName.StartsWith("System") || asmName.StartsWith("mscorlib")
                    || asmName.StartsWith("Mono.") || asmName.StartsWith("nunit"))
                    continue;

                try
                {
                    foreach (var type in asm.GetTypes())
                    {
                        if (type.IsAbstract || !type.IsClass) continue;
                        var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance);
                        foreach (var m in methods)
                        {
                            bool hasTest = m.GetCustomAttribute<TestAttribute>() != null;
                            bool hasUnityTest = unityTestAttrType != null
                                && m.GetCustomAttribute(unityTestAttrType) != null;
                            if (hasTest || hasUnityTest)
                            {
                                typesWithTestMethods.Add(type);
                                break;
                            }
                        }
                    }
                }
                catch { /* Skip assemblies we can't inspect */ }
            }

            // Filter to user-authored types only
            var userFixtures = typesWithTestMethods
                .Where(t =>
                {
                    string asmName = t.Assembly.GetName().Name;
                    return !asmName.StartsWith("Unity")
                        && !asmName.StartsWith("com.unity")
                        && !asmName.StartsWith("nunit")
                        && !asmName.StartsWith("System")
                        && !asmName.StartsWith("mscorlib");
                })
                .OrderBy(t => t.FullName)
                .ToList();

            if (userFixtures.Count == 0)
            {
                sb.AppendLine("*No tests found. Create test assemblies with [TestFixture] classes and [Test] methods.*");
                sb.AppendLine();
                sb.AppendLine("---");
                sb.AppendLine($"Generated: {timestamp}");

                string emptyReport = $"test_runner_list_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.md";
                return OutputWriter.WriteReport(emptyReport, sb.ToString());
            }

            sb.AppendLine("## Test Fixtures");

            int totalTests = 0;
            foreach (var fixture in userFixtures)
            {
                // Find the script asset for this type to get the file path
                string filePath = FindScriptPath(fixture.Name);

                sb.AppendLine($"### {fixture.Name} ({filePath})");
                sb.AppendLine("| # | Test Name | Categories | Type |");
                sb.AppendLine("| :--- | :--- | :--- | :--- |");

                var methods = fixture.GetMethods(BindingFlags.Public | BindingFlags.Instance);
                int testNum = 1;
                foreach (var m in methods)
                {
                    bool isTest = m.GetCustomAttribute<TestAttribute>() != null;
                    bool isUnityTest = unityTestAttrType != null
                        && m.GetCustomAttribute(unityTestAttrType) != null;

                    if (!isTest && !isUnityTest) continue;

                    string testType = isUnityTest ? "[UnityTest]" : "[Test]";
                    var categories = m.GetCustomAttributes<CategoryAttribute>()
                        .Select(c => c.Name)
                        .ToList();
                    string catStr = categories.Count > 0 ? string.Join(", ", categories) : "(none)";

                    sb.AppendLine($"| {testNum++} | {m.Name} | {catStr} | {testType} |");
                    totalTests++;
                }
                sb.AppendLine();
            }

            sb.AppendLine("## Summary");
            sb.AppendLine($"- Test fixtures: {userFixtures.Count}");
            sb.AppendLine($"- Total tests: {totalTests}");
            sb.AppendLine();

            sb.AppendLine("---");
            sb.AppendLine($"Generated: {timestamp}");

            string reportName = $"test_runner_list_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.md";
            return OutputWriter.WriteReport(reportName, sb.ToString());
        }

        private static string FindScriptPath(string typeName)
        {
            string[] guids = AssetDatabase.FindAssets($"t:MonoScript {typeName}");
            foreach (string guid in guids)
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                var script = AssetDatabase.LoadAssetAtPath<MonoScript>(p);
                if (script != null)
                {
                    var cls = script.GetClass();
                    if (cls != null && cls.Name == typeName)
                        return p;
                }
            }
            return "(path unknown)";
        }

        // ─── Modes B & C: Run Tests ───────────────────────────────────────────────

        private static string StartTestRun(TestRunnerMode mode, string testFilter, string categoryFilter, bool editMode)
        {
            var api = ScriptableObject.CreateInstance<TestRunnerApi>();

            var filter = new Filter
            {
                testMode = editMode ? TestMode.EditMode : TestMode.PlayMode
            };

            if (mode == TestRunnerMode.RunFiltered)
            {
                if (!string.IsNullOrEmpty(testFilter))
                    filter.testNames = new[] { testFilter };
                if (!string.IsNullOrEmpty(categoryFilter))
                    filter.categoryNames = new[] { categoryFilter };
            }

            s_ActiveCollector = new ResultCollector();
            api.RegisterCallbacks(s_ActiveCollector);
            api.Execute(new ExecutionSettings(filter));

            string modeLabel = editMode ? "EditMode" : "PlayMode";
            string filterInfo = mode == TestRunnerMode.RunFiltered
                ? $" (filter: {testFilter ?? categoryFilter ?? "none"})"
                : "";
            return $"[AgentBridge] Test run initiated ({modeLabel}{filterInfo}). Report will appear in AgentReports/ when complete.";
        }

        // ─── Mode D: Coverage Report ──────────────────────────────────────────────

        private static string RunCoverageReport()
        {
            var sb = new StringBuilder();
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

            sb.AppendLine("# Test Runner — Mode: Coverage Report");
            sb.AppendLine();

            // Check if Code Coverage package is installed
            var coverageType = Type.GetType(
                "UnityEditor.TestTools.CodeCoverage.CodeCoverage, Unity.TestTools.CodeCoverage.Editor");
            bool packageInstalled = coverageType != null;

            sb.AppendLine($"**Code Coverage Package:** {(packageInstalled ? "Installed" : "Not Installed")}");
            sb.AppendLine();

            if (!packageInstalled)
            {
                sb.AppendLine("To enable code coverage:");
                sb.AppendLine("1. Open Package Manager (Window > Package Manager)");
                sb.AppendLine("2. Search for \"Code Coverage\"");
                sb.AppendLine("3. Install com.unity.testtools.codecoverage");
                sb.AppendLine("4. Run tests with coverage enabled via Window > Analysis > Code Coverage");
            }
            else
            {
                // Check for coverage data
                string coveragePath = Path.Combine(
                    Path.GetDirectoryName(Application.dataPath), "CodeCoverage");
                bool dataExists = Directory.Exists(coveragePath);

                sb.AppendLine($"**Coverage Data:** {(dataExists ? $"Found at {coveragePath}" : "No data yet")}");

                if (dataExists)
                {
                    var reportFiles = Directory.GetFiles(coveragePath, "*.xml", SearchOption.AllDirectories)
                        .OrderByDescending(f => File.GetLastWriteTime(f))
                        .ToArray();

                    if (reportFiles.Length > 0)
                    {
                        string latest = reportFiles[0];
                        string relPath = latest.Replace(
                            Path.GetDirectoryName(Application.dataPath) + Path.DirectorySeparatorChar, "");
                        DateTime lastWrite = File.GetLastWriteTime(latest);
                        sb.AppendLine($"**Latest Report:** {relPath} ({lastWrite:yyyy-MM-dd HH:mm:ss})");
                        sb.AppendLine();
                        sb.AppendLine("*For detailed analysis, open Window > Analysis > Code Coverage in Unity.*");
                    }
                    else
                    {
                        sb.AppendLine();
                        sb.AppendLine("*Coverage folder exists but no XML report files found.*");
                        sb.AppendLine("*Run tests with coverage enabled to generate reports.*");
                    }
                }
                else
                {
                    sb.AppendLine();
                    sb.AppendLine("*No coverage data found. Run tests with coverage enabled to generate reports.*");
                    sb.AppendLine("*Open Window > Analysis > Code Coverage to configure.*");
                }
            }

            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine($"Generated: {timestamp}");

            string reportName = $"test_runner_coverage_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.md";
            return OutputWriter.WriteReport(reportName, sb.ToString());
        }

        // ─── Result Collector (async callback for Modes B & C) ───────────────────

        private class ResultCollector : ICallbacks
        {
            private readonly List<TestResultEntry> _results = new List<TestResultEntry>();

            public void RunStarted(ITestAdaptor testsToRun)
            {
                Debug.Log($"[AgentBridge] Test run started: {testsToRun.TestCaseCount} tests");
            }

            public void TestStarted(ITestAdaptor test) { }

            public void TestFinished(ITestResultAdaptor result)
            {
                if (!result.HasChildren)
                {
                    _results.Add(new TestResultEntry
                    {
                        Name = result.Name,
                        FullName = result.FullName,
                        Status = result.TestStatus.ToString(),
                        Duration = result.Duration,
                        Message = result.Message ?? "",
                        StackTrace = result.StackTrace ?? ""
                    });
                }
            }

            public void RunFinished(ITestResultAdaptor result)
            {
                string report = FormatResults(_results);
                string reportName = $"test_runner_results_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.md";
                string path = OutputWriter.WriteReport(reportName, report);
                Debug.Log($"[AgentBridge] Test run complete. Report: {path}");
            }

            private static string FormatResults(List<TestResultEntry> results)
            {
                var sb = new StringBuilder();
                string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

                var passed = results.Where(r => r.Status == "Passed").ToList();
                var failed = results.Where(r => r.Status == "Failed").ToList();
                var skipped = results.Where(r =>
                    r.Status == "Skipped" || r.Status == "Inconclusive").ToList();

                sb.AppendLine("# Test Runner — Run Results");
                sb.AppendLine();

                if (passed.Count > 0)
                {
                    sb.AppendLine($"## Passed ({passed.Count})");
                    sb.AppendLine("| # | Test Name | Duration |");
                    sb.AppendLine("| :--- | :--- | :--- |");
                    for (int i = 0; i < passed.Count; i++)
                        sb.AppendLine($"| {i + 1} | {passed[i].Name} | {passed[i].Duration:F3}s |");
                    sb.AppendLine();
                }

                if (failed.Count > 0)
                {
                    sb.AppendLine($"## Failed ({failed.Count})");
                    sb.AppendLine("| # | Test Name | Duration | Message |");
                    sb.AppendLine("| :--- | :--- | :--- | :--- |");
                    for (int i = 0; i < failed.Count; i++)
                    {
                        string firstLine = failed[i].Message.Split('\n')[0];
                        sb.AppendLine($"| {i + 1} | {failed[i].Name} | {failed[i].Duration:F3}s | {firstLine} |");
                    }
                    sb.AppendLine();

                    sb.AppendLine("### Failure Details");
                    foreach (var f in failed)
                    {
                        sb.AppendLine($"**{f.FullName}**");
                        sb.AppendLine("```");
                        sb.AppendLine(f.Message);
                        if (!string.IsNullOrEmpty(f.StackTrace))
                            sb.AppendLine(f.StackTrace);
                        sb.AppendLine("```");
                        sb.AppendLine();
                    }
                }

                string status = failed.Count > 0 ? "FAILURES" : "ALL PASSED";
                sb.AppendLine("## Summary");
                sb.AppendLine($"**Status:** {status}");
                sb.AppendLine($"- Passed: {passed.Count}");
                sb.AppendLine($"- Failed: {failed.Count}");
                sb.AppendLine($"- Skipped: {skipped.Count}");
                sb.AppendLine($"- Total: {results.Count}");
                double totalDuration = results.Sum(r => r.Duration);
                sb.AppendLine($"- Total Duration: {totalDuration:F3}s");
                sb.AppendLine();
                sb.AppendLine("---");
                sb.AppendLine($"Generated: {timestamp}");

                return sb.ToString();
            }
        }

        [Serializable]
        private class TestResultEntry
        {
            public string Name;
            public string FullName;
            public string Status;
            public double Duration;
            public string Message;
            public string StackTrace;
        }

        // ─── Menu Items ───────────────────────────────────────────────────────────

        [MenuItem("Axiom/AgentBridge/Test Runner — Mode A (List Tests)")]
        public static void MenuModeA()
        {
            string path = GenerateReport(TestRunnerMode.TestList);
            Debug.Log($"[AgentBridge] Test Runner report: {path}");
        }

        [MenuItem("Axiom/AgentBridge/Test Runner — Mode B (Run All EditMode)")]
        public static void MenuModeB()
        {
            string result = GenerateReport(TestRunnerMode.RunAll, editMode: true);
            Debug.Log($"[AgentBridge] {result}");
        }

        [MenuItem("Axiom/AgentBridge/Test Runner — Mode D (Coverage Report)")]
        public static void MenuModeD()
        {
            string path = GenerateReport(TestRunnerMode.CoverageReport);
            Debug.Log($"[AgentBridge] Test Runner report: {path}");
        }
    }
}
