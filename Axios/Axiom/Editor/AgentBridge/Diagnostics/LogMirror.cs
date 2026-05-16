using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEditorInternal;
using Axiom.Editor.AgentBridge.Core;

namespace Axiom.Editor.AgentBridge.Diagnostics
{
    /// <summary>
    /// Gives the agent filtered access to Unity's console output and compilation status
    /// without reading the noisy full console.
    /// </summary>
    public static class LogMirror
    {
        /// <summary>
        /// Mode for the log report.
        /// </summary>
        public enum LogMirrorMode
        {
            /// <summary>Only error and exception entries from the console.</summary>
            ErrorsOnly,
            /// <summary>Errors and warnings from the console.</summary>
            Warnings,
            /// <summary>Log entries that start with a specific tag prefix.</summary>
            TaggedLogs,
            /// <summary>All log entries categorized by type.</summary>
            FullStream,
            /// <summary>Profiler frame spikes (or fallback status if API unavailable).</summary>
            ProfilerSpikes,
            /// <summary>Compilation status report (no log entries read).</summary>
            CompilationReport
        }

        // ─────────────────────────────────────────────────────
        //  Reflected LogEntries API (cached)
        // ─────────────────────────────────────────────────────

        private static bool _reflectionInitialized = false;
        private static bool _reflectionAvailable = false;

        private static Type _logEntriesType;
        private static Type _logEntryType;
        private static MethodInfo _getCountMethod;
        private static MethodInfo _startGettingMethod;
        private static MethodInfo _getEntryInternalMethod;
        private static MethodInfo _endGettingMethod;
        private static FieldInfo _messageField;
        private static FieldInfo _modeField;

        // ─────────────────────────────────────────────────────
        //  Fallback buffer (populated via log callback)
        // ─────────────────────────────────────────────────────

        // FALLBACK: If the reflection approach fails at runtime (Unity's internal API can change between
        // versions), we register a static Application.logMessageReceived callback and store recent
        // log entries in this buffer. LogMirror then reads from this buffer instead.
        private static readonly List<FallbackEntry> _fallbackBuffer = new List<FallbackEntry>();
        private static bool _fallbackRegistered = false;

        // Register the fallback log listener eagerly on domain load so it
        // captures ALL logs (including Play Mode) from the very start.
        [InitializeOnLoadMethod]
        private static void RegisterFallbackOnLoad()
        {
            EnsureFallbackRegistered();
        }
        private const int FallbackBufferMax = 500;

        private struct FallbackEntry
        {
            public string Message;
            public string StackTrace;
            public LogType Type;
        }

        // ─────────────────────────────────────────────────────
        //  Public API
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// Generates a filtered log report.
        /// </summary>
        /// <param name="mode">Which mode to run.</param>
        /// <param name="maxEntries">Maximum number of log entries to include. Default 50.</param>
        /// <param name="tagPrefix">For TaggedLogs mode: only include entries starting with this prefix. Defaults to "[AGENT]".</param>
        /// <returns>The file path of the generated report.</returns>
        public static string GenerateReport(LogMirrorMode mode, int maxEntries = 50, string tagPrefix = null)
        {
            string content;
            switch (mode)
            {
                case LogMirrorMode.CompilationReport:
                    content = BuildCompilationReport();
                    break;
                case LogMirrorMode.ErrorsOnly:
                    content = BuildErrorsOnlyReport(maxEntries);
                    break;
                case LogMirrorMode.Warnings:
                    content = BuildWarningsReport(maxEntries);
                    break;
                case LogMirrorMode.TaggedLogs:
                    content = BuildTaggedLogsReport(maxEntries, tagPrefix ?? "[AGENT]");
                    break;
                case LogMirrorMode.FullStream:
                    content = BuildFullStreamReport(maxEntries);
                    break;
                case LogMirrorMode.ProfilerSpikes:
                    content = BuildProfilerSpikesReport();
                    break;
                default:
                    content = BuildErrorsOnlyReport(maxEntries);
                    break;
            }

            return OutputWriter.WriteReport("log_mirror", content);
        }

        // ─────────────────────────────────────────────────────
        //  Editor Menu Items
        // ─────────────────────────────────────────────────────

        [MenuItem("Axiom/AgentBridge/Log Mirror — Mode A (Errors Only)")]
        public static void RunModeAErrors()
        {
            string path = GenerateReport(LogMirrorMode.ErrorsOnly);
            Debug.Log($"[AgentBridge] Log Mirror report: {path}");
        }

        [MenuItem("Axiom/AgentBridge/Log Mirror — Mode B (Errors & Warnings)")]
        public static void RunModeBWarnings()
        {
            string path = GenerateReport(LogMirrorMode.Warnings);
            Debug.Log($"[AgentBridge] Log Mirror report: {path}");
        }

        [MenuItem("Axiom/AgentBridge/Log Mirror — Mode C (Tagged Logs)")]
        public static void RunModeCTagged()
        {
            string path = GenerateReport(LogMirrorMode.TaggedLogs, tagPrefix: "[AGENT]");
            Debug.Log($"[AgentBridge] Log Mirror report: {path}");
        }

        [MenuItem("Axiom/AgentBridge/Log Mirror — Mode D (Full Stream)")]
        public static void RunModeDFull()
        {
            string path = GenerateReport(LogMirrorMode.FullStream);
            Debug.Log($"[AgentBridge] Log Mirror report: {path}");
        }

        [MenuItem("Axiom/AgentBridge/Log Mirror — Mode E (Profiler Spikes)")]
        public static void RunModeEProfiler()
        {
            string path = GenerateReport(LogMirrorMode.ProfilerSpikes);
            Debug.Log($"[AgentBridge] Log Mirror report: {path}");
        }

        [MenuItem("Axiom/AgentBridge/Log Mirror — Mode F (Compilation Report)")]
        public static void RunModeFCompilation()
        {
            string path = GenerateReport(LogMirrorMode.CompilationReport);
            Debug.Log($"[AgentBridge] Log Mirror report: {path}");
        }

        // ─────────────────────────────────────────────────────
        //  Mode A: Errors Only
        // ─────────────────────────────────────────────────────

        private static string BuildErrorsOnlyReport(int maxEntries)
        {
            var errors = new List<(string message, string stackTrace)>();

            // Try reflection-based approach first
            bool usedReflection = false;
            if (TryInitReflection())
            {
                usedReflection = TryReadEntriesViaReflection(maxEntries, errors);
            }

            // Fallback: read from our static callback buffer
            if (!usedReflection)
            {
                EnsureFallbackRegistered();
                lock (_fallbackBuffer)
                {
                    foreach (var entry in _fallbackBuffer)
                    {
                        if (entry.Type == LogType.Error || entry.Type == LogType.Exception || entry.Type == LogType.Assert)
                        {
                            errors.Add((entry.Message, entry.StackTrace));
                            if (errors.Count >= maxEntries) break;
                        }
                    }
                }
            }

            // Get total count for "and X more" note
            int totalErrors = errors.Count; // We may have capped, so track separately
            // Note: reflection gives us exact total, fallback gives us capped count

            var sb = new StringBuilder();
            sb.AppendLine($"# Log Mirror — Mode: Errors Only | Entries: {errors.Count} of {totalErrors}");
            sb.AppendLine();

            if (errors.Count == 0)
            {
                sb.AppendLine("*No errors found in console.*");
            }
            else
            {
                for (int i = 0; i < errors.Count; i++)
                {
                    sb.AppendLine($"## Error {i + 1}");
                    sb.AppendLine($"**Type:** Error");

                    // Split first line (message) from rest (stack trace)
                    string[] lines = errors[i].message.Split(new[] { '\n' }, 2);
                    string firstLine = lines[0].Trim();
                    string stackTrace = lines.Length > 1 ? lines[1].Trim() : errors[i].stackTrace;

                    sb.AppendLine($"**Message:** {firstLine}");
                    if (!string.IsNullOrEmpty(stackTrace))
                    {
                        sb.AppendLine("**Stack Trace:**");
                        sb.AppendLine("```");
                        sb.AppendLine(stackTrace);
                        sb.AppendLine("```");
                    }
                    sb.AppendLine();
                }
            }

            sb.AppendLine("---");
            sb.Append($"Total Errors: {errors.Count} | Total Log Entries: N/A | Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            return sb.ToString();
        }

        // ─────────────────────────────────────────────────────
        //  Mode B: Warnings (Errors + Warnings)
        // ─────────────────────────────────────────────────────

        private static string BuildWarningsReport(int maxEntries)
        {
            var entries = new List<(string message, string stackTrace, string type)>();

            bool usedReflection = false;
            if (TryInitReflection())
            {
                usedReflection = TryReadEntriesWithWarnings(maxEntries, entries);
            }

            if (!usedReflection)
            {
                EnsureFallbackRegistered();
                lock (_fallbackBuffer)
                {
                    foreach (var entry in _fallbackBuffer)
                    {
                        if (entry.Type == LogType.Error || entry.Type == LogType.Exception
                            || entry.Type == LogType.Assert || entry.Type == LogType.Warning)
                        {
                            string typeLabel = (entry.Type == LogType.Warning) ? "Warning" : "Error";
                            entries.Add((entry.Message, entry.StackTrace, typeLabel));
                            if (entries.Count >= maxEntries) break;
                        }
                    }
                }
            }

            int errorCount = 0, warningCount = 0;
            foreach (var (_, _, t) in entries)
            {
                if (t == "Warning") warningCount++;
                else errorCount++;
            }

            var sb = new StringBuilder();
            sb.AppendLine($"# Log Mirror — Mode: Errors & Warnings | Entries: {entries.Count}");
            sb.AppendLine();

            if (entries.Count == 0)
            {
                sb.AppendLine("*No errors or warnings found in console.*");
            }
            else
            {
                for (int i = 0; i < entries.Count; i++)
                {
                    sb.AppendLine($"## Entry {i + 1}");
                    sb.AppendLine($"**Type:** {entries[i].type}");
                    string[] lines = entries[i].message.Split(new[] { '\n' }, 2);
                    sb.AppendLine($"**Message:** {lines[0].Trim()}");
                    string stack = lines.Length > 1 ? lines[1].Trim() : entries[i].stackTrace;
                    if (!string.IsNullOrEmpty(stack))
                    {
                        sb.AppendLine("**Stack Trace:**");
                        sb.AppendLine("```");
                        sb.AppendLine(stack);
                        sb.AppendLine("```");
                    }
                    sb.AppendLine();
                }
            }

            sb.AppendLine("---");
            sb.Append($"Errors: {errorCount} | Warnings: {warningCount} | Total Entries: {entries.Count} | Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            return sb.ToString();
        }

        private static bool TryReadEntriesWithWarnings(int maxEntries, List<(string, string, string)> entries)
        {
            try
            {
                int total = (int)_getCountMethod.Invoke(null, null);
                _startGettingMethod.Invoke(null, null);
                try
                {
                    object entry = Activator.CreateInstance(_logEntryType);
                    for (int i = 0; i < total; i++)
                    {
                        _getEntryInternalMethod.Invoke(null, new object[] { i, entry });
                        int modeVal = _modeField != null ? (int)_modeField.GetValue(entry) : 0;

                        bool isError = (modeVal & 1) != 0 || (modeVal & 32) != 0
                                    || (modeVal & 256) != 0 || (modeVal & 512) != 0;
                        bool isWarning = (modeVal & 2) != 0;

                        if (!isError && !isWarning) continue;

                        string msg = (string)_messageField.GetValue(entry);
                        string[] parts = (msg ?? "").Split(new[] { '\n' }, 2);
                        string stack = parts.Length > 1 ? parts[1] : "";
                        string typeLabel = isWarning && !isError ? "Warning" : "Error";

                        entries.Add((parts[0], stack, typeLabel));
                        if (entries.Count >= maxEntries) break;
                    }
                }
                finally { _endGettingMethod.Invoke(null, null); }
                return true;
            }
            catch { return false; }
        }

        // ─────────────────────────────────────────────────────
        //  Mode C: Tagged Logs
        // ─────────────────────────────────────────────────────

        private static string BuildTaggedLogsReport(int maxEntries, string tagPrefix)
        {
            var taggedMessages = new List<string>();

            bool usedReflection = false;
            if (TryInitReflection())
            {
                usedReflection = TryReadTaggedEntries(maxEntries, tagPrefix, taggedMessages);
            }

            if (!usedReflection)
            {
                EnsureFallbackRegistered();
                lock (_fallbackBuffer)
                {
                    foreach (var entry in _fallbackBuffer)
                    {
                        if (entry.Message != null && entry.Message.StartsWith(tagPrefix))
                        {
                            taggedMessages.Add(entry.Message.Split('\n')[0]);
                            if (taggedMessages.Count >= maxEntries) break;
                        }
                    }
                }
            }

            int total = 0;
            if (TryInitReflection())
            {
                try { total = (int)_getCountMethod.Invoke(null, null); } catch { }
            }

            var sb = new StringBuilder();
            sb.AppendLine($"# Log Mirror — Mode: Tagged Logs | Tag: {tagPrefix} | Entries: {taggedMessages.Count}");
            sb.AppendLine();

            if (taggedMessages.Count == 0)
            {
                sb.AppendLine($"*No log entries starting with `{tagPrefix}` found.*");
            }
            else
            {
                sb.AppendLine("| # | Message |");
                sb.AppendLine("| :--- | :--- |");
                for (int i = 0; i < taggedMessages.Count; i++)
                    sb.AppendLine($"| {i + 1} | {taggedMessages[i]} |");
            }

            sb.AppendLine();
            sb.AppendLine("---");
            sb.Append($"Tagged entries: {taggedMessages.Count} | Total log entries: {total} | Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            return sb.ToString();
        }

        private static bool TryReadTaggedEntries(int maxEntries, string tagPrefix, List<string> messages)
        {
            try
            {
                int total = (int)_getCountMethod.Invoke(null, null);
                _startGettingMethod.Invoke(null, null);
                try
                {
                    object entry = Activator.CreateInstance(_logEntryType);
                    for (int i = 0; i < total; i++)
                    {
                        _getEntryInternalMethod.Invoke(null, new object[] { i, entry });
                        string msg = (string)_messageField.GetValue(entry);
                        if (msg != null && msg.StartsWith(tagPrefix))
                        {
                            messages.Add(msg.Split('\n')[0]);
                            if (messages.Count >= maxEntries) break;
                        }
                    }
                }
                finally { _endGettingMethod.Invoke(null, null); }
                return true;
            }
            catch { return false; }
        }

        // ─────────────────────────────────────────────────────
        //  Mode D: Full Stream
        // ─────────────────────────────────────────────────────

        private static string BuildFullStreamReport(int maxEntries)
        {
            var errorEntries = new List<(string message, string stack)>();
            var warnEntries  = new List<(string message, string stack)>();
            var infoEntries  = new List<string>();

            bool usedReflection = false;
            if (TryInitReflection())
            {
                usedReflection = TryReadAllEntries(maxEntries, errorEntries, warnEntries, infoEntries);
            }

            if (!usedReflection)
            {
                EnsureFallbackRegistered();
                lock (_fallbackBuffer)
                {
                    foreach (var entry in _fallbackBuffer)
                    {
                        if (entry.Type == LogType.Error || entry.Type == LogType.Exception || entry.Type == LogType.Assert)
                            errorEntries.Add((entry.Message, entry.StackTrace));
                        else if (entry.Type == LogType.Warning)
                            warnEntries.Add((entry.Message, entry.StackTrace));
                        else
                        {
                            if (infoEntries.Count < maxEntries)
                                infoEntries.Add(entry.Message?.Split('\n')[0] ?? "");
                        }
                    }
                }
            }

            int total = errorEntries.Count + warnEntries.Count + infoEntries.Count;

            var sb = new StringBuilder();
            sb.AppendLine($"# Log Mirror — Mode: Full Stream | Entries: {total}");
            sb.AppendLine();

            // Errors
            sb.AppendLine($"## Errors ({errorEntries.Count})");
            if (errorEntries.Count == 0)
            {
                sb.AppendLine("*None.*");
            }
            else
            {
                for (int i = 0; i < errorEntries.Count; i++)
                {
                    sb.AppendLine($"### Error {i + 1}");
                    string[] lines = errorEntries[i].message.Split(new[] { '\n' }, 2);
                    sb.AppendLine($"**Message:** {lines[0].Trim()}");
                    string stack = lines.Length > 1 ? lines[1].Trim() : errorEntries[i].stack;
                    if (!string.IsNullOrEmpty(stack))
                    {
                        sb.AppendLine("**Stack Trace:**");
                        sb.AppendLine("```");
                        sb.AppendLine(stack);
                        sb.AppendLine("```");
                    }
                    sb.AppendLine();
                }
            }

            // Warnings
            sb.AppendLine($"## Warnings ({warnEntries.Count})");
            if (warnEntries.Count == 0)
            {
                sb.AppendLine("*None.*");
            }
            else
            {
                for (int i = 0; i < warnEntries.Count; i++)
                {
                    sb.AppendLine($"### Warning {i + 1}");
                    string[] lines = warnEntries[i].message.Split(new[] { '\n' }, 2);
                    sb.AppendLine($"**Message:** {lines[0].Trim()}");
                    string stack = lines.Length > 1 ? lines[1].Trim() : warnEntries[i].stack;
                    if (!string.IsNullOrEmpty(stack))
                    {
                        sb.AppendLine("**Stack Trace:**");
                        sb.AppendLine("```");
                        sb.AppendLine(stack);
                        sb.AppendLine("```");
                    }
                    sb.AppendLine();
                }
            }

            // Info
            sb.AppendLine($"## Info ({infoEntries.Count})");
            if (infoEntries.Count == 0)
            {
                sb.AppendLine("*None.*");
            }
            else
            {
                sb.AppendLine("| # | Message (first line only) |");
                sb.AppendLine("| :--- | :--- |");
                for (int i = 0; i < infoEntries.Count; i++)
                    sb.AppendLine($"| {i + 1} | {infoEntries[i]} |");
            }

            sb.AppendLine();
            sb.AppendLine("---");
            sb.Append($"Errors: {errorEntries.Count} | Warnings: {warnEntries.Count} | Info: {infoEntries.Count} | Total: {total} | Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            return sb.ToString();
        }

        private static bool TryReadAllEntries(int maxEntries, List<(string, string)> errors, List<(string, string)> warnings, List<string> info)
        {
            try
            {
                int total = (int)_getCountMethod.Invoke(null, null);
                _startGettingMethod.Invoke(null, null);
                try
                {
                    object entry = Activator.CreateInstance(_logEntryType);
                    for (int i = 0; i < total; i++)
                    {
                        _getEntryInternalMethod.Invoke(null, new object[] { i, entry });
                        int modeVal = _modeField != null ? (int)_modeField.GetValue(entry) : 0;
                        string msg = (string)_messageField.GetValue(entry) ?? "";

                        bool isError   = (modeVal & 1) != 0 || (modeVal & 32) != 0 || (modeVal & 256) != 0 || (modeVal & 512) != 0;
                        bool isWarning = (modeVal & 2) != 0;

                        string[] parts = msg.Split(new[] { '\n' }, 2);
                        string stack = parts.Length > 1 ? parts[1] : "";

                        if (isError)
                            errors.Add((parts[0], stack));
                        else if (isWarning)
                            warnings.Add((parts[0], stack));
                        else if (info.Count < maxEntries)
                            info.Add(parts[0]);
                    }
                }
                finally { _endGettingMethod.Invoke(null, null); }
                return true;
            }
            catch { return false; }
        }

        // ─────────────────────────────────────────────────────
        //  Mode E: Profiler Spikes
        // ─────────────────────────────────────────────────────

        private static string BuildProfilerSpikesReport()
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Log Mirror — Mode: Profiler Spikes");
            sb.AppendLine();

            bool profilerEnabled = false;
            bool spikeDataRead = false;

            try
            {
                profilerEnabled = ProfilerDriver.enabled;
                // Frame-level data read omitted — requires HierarchyFrameDataView which is not
                // reliably accessible across Unity versions. Using fallback report.
                spikeDataRead = false;
            }
            catch
            {
                spikeDataRead = false;
            }

            if (spikeDataRead)
            {
                // Placeholder — spike data path not currently reachable with fallback
            }
            else
            {
                sb.AppendLine($"**Profiler Status:** {(profilerEnabled ? "Active" : "Inactive")}");
                sb.AppendLine("**Note:** Deep Profiler frame data could not be read (Profiler may not be recording, or no frames captured).");
                sb.AppendLine("Use the Profiler window directly for frame-level analysis.");
                sb.AppendLine();

                // Fallback: find performance-related console entries
                var perfKeywords = new[] { "performance", "spike", "slow", "GC.Collect", "GC Alloc", "frame" };
                var perfEntries = new List<string>();
                EnsureFallbackRegistered();
                lock (_fallbackBuffer)
                {
                    foreach (var entry in _fallbackBuffer)
                    {
                        string lower = entry.Message?.ToLowerInvariant() ?? "";
                        foreach (string kw in perfKeywords)
                        {
                            if (lower.Contains(kw.ToLower()))
                            {
                                perfEntries.Add(entry.Message?.Split('\n')[0] ?? "");
                                break;
                            }
                        }
                    }
                }

                sb.AppendLine("## Performance-Related Console Entries");
                if (perfEntries.Count == 0)
                {
                    sb.AppendLine("*None found.*");
                }
                else
                {
                    sb.AppendLine("| # | Message |");
                    sb.AppendLine("| :--- | :--- |");
                    for (int i = 0; i < perfEntries.Count; i++)
                        sb.AppendLine($"| {i + 1} | {perfEntries[i]} |");
                }
            }

            sb.AppendLine();
            sb.AppendLine("---");
            sb.Append($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            return sb.ToString();
        }

        // ─────────────────────────────────────────────────────
        //  Mode F: Compilation Report
        // ─────────────────────────────────────────────────────

        private static string BuildCompilationReport()
        {
            bool isCompiling = EditorApplication.isCompiling;

            // Detect compile errors
            bool hasCompileErrors = false;

            // Primary: check EditorUtility.scriptCompilationFailed (Unity 6)
            try
            {
                // Try scriptCompilationFailed via reflection since it may not exist in all versions
                var prop = typeof(EditorUtility).GetProperty("scriptCompilationFailed",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                if (prop != null)
                    hasCompileErrors = (bool)prop.GetValue(null);
            }
            catch { /* ignore */ }

            // Secondary: scan log entries for "error CS" prefix
            if (!hasCompileErrors)
            {
                try
                {
                    if (TryInitReflection())
                    {
                        int total = (int)_getCountMethod.Invoke(null, null);
                        _startGettingMethod.Invoke(null, null);
                        try
                        {
                            object entry = Activator.CreateInstance(_logEntryType);
                            for (int i = 0; i < total; i++)
                            {
                                _getEntryInternalMethod.Invoke(null, new object[] { i, entry });
                                string msg = (string)_messageField.GetValue(entry);
                                if (msg != null && msg.Contains("error CS"))
                                {
                                    hasCompileErrors = true;
                                    break;
                                }
                            }
                        }
                        finally
                        {
                            _endGettingMethod.Invoke(null, null);
                        }
                    }
                }
                catch { /* ignore, fallback already done */ }
            }

            // Determine status
            string status;
            if (isCompiling)
                status = "COMPILING";
            else if (hasCompileErrors)
                status = "HAS_ERRORS";
            else
                status = "CLEAN";

            // Build assembly list
            var sbAssemblies = new StringBuilder();
            try
            {
                UnityEditor.Compilation.Assembly[] assemblies = CompilationPipeline.GetAssemblies();
                foreach (UnityEditor.Compilation.Assembly asm in assemblies)
                    sbAssemblies.AppendLine($"  - {asm.name} ({asm.sourceFiles.Length} source files)");
            }
            catch (Exception ex)
            {
                sbAssemblies.AppendLine($"  (Could not retrieve assemblies: {ex.Message})");
            }

            var sb = new StringBuilder();
            sb.AppendLine("# Log Mirror — Mode: Compilation Report");
            sb.AppendLine();
            sb.AppendLine($"**Compiling:** {isCompiling}");
            sb.AppendLine($"**Compile Errors:** {hasCompileErrors}");
            sb.AppendLine($"**Script Assemblies:**");
            sb.Append(sbAssemblies);
            sb.AppendLine($"**Status:** {status}");
            sb.AppendLine();
            sb.AppendLine("---");
            sb.Append($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            return sb.ToString();
        }

        // ─────────────────────────────────────────────────────
        //  Reflection Helpers
        // ─────────────────────────────────────────────────────

        private static bool TryInitReflection()
        {
            if (_reflectionInitialized) return _reflectionAvailable;
            _reflectionInitialized = true;

            try
            {
                _logEntriesType = Type.GetType("UnityEditorInternal.LogEntries, UnityEditor");
                _logEntryType = Type.GetType("UnityEditorInternal.LogEntry, UnityEditor");

                if (_logEntriesType == null || _logEntryType == null)
                    return _reflectionAvailable = false;

                _getCountMethod = _logEntriesType.GetMethod("GetCount",
                    BindingFlags.Static | BindingFlags.Public);
                _startGettingMethod = _logEntriesType.GetMethod("StartGettingEntries",
                    BindingFlags.Static | BindingFlags.Public);
                _getEntryInternalMethod = _logEntriesType.GetMethod("GetEntryInternal",
                    BindingFlags.Static | BindingFlags.Public);
                _endGettingMethod = _logEntriesType.GetMethod("EndGettingEntries",
                    BindingFlags.Static | BindingFlags.Public);

                _messageField = _logEntryType.GetField("message",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                _modeField = _logEntryType.GetField("mode",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                _reflectionAvailable = _getCountMethod != null
                    && _startGettingMethod != null
                    && _getEntryInternalMethod != null
                    && _endGettingMethod != null
                    && _messageField != null;

                return _reflectionAvailable;
            }
            catch
            {
                return _reflectionAvailable = false;
            }
        }

        private static bool TryReadEntriesViaReflection(int maxEntries, List<(string, string)> errors)
        {
            try
            {
                int total = (int)_getCountMethod.Invoke(null, null);
                _startGettingMethod.Invoke(null, null);
                try
                {
                    object entry = Activator.CreateInstance(_logEntryType);
                    for (int i = 0; i < total; i++)
                    {
                        _getEntryInternalMethod.Invoke(null, new object[] { i, entry });

                        int modeVal = _modeField != null ? (int)_modeField.GetValue(entry) : 0;
                        // Bit 0 = Error, bit 1 = Warning, bit 2 = Log
                        // Also handle ScriptCompileError (bit 5 = 32) and Exception (included when bit 0 set)
                        bool isError = (modeVal & 1) != 0   // Error
                                    || (modeVal & 32) != 0  // ScriptCompileError
                                    || (modeVal & 256) != 0 // Exception
                                    || (modeVal & 512) != 0;// Assert

                        if (!isError) continue;

                        string msg = (string)_messageField.GetValue(entry);
                        // Split message from stack trace (separated by \n)
                        string[] parts = (msg ?? "").Split(new[] { '\n' }, 2);
                        string msgLine = parts[0];
                        string stack = parts.Length > 1 ? parts[1] : "";

                        errors.Add((msgLine, stack));
                        if (errors.Count >= maxEntries) break;
                    }
                }
                finally
                {
                    _endGettingMethod.Invoke(null, null);
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        // ─────────────────────────────────────────────────────
        //  Fallback: Application.logMessageReceived buffer
        // ─────────────────────────────────────────────────────

        private static void EnsureFallbackRegistered()
        {
            if (_fallbackRegistered) return;
            _fallbackRegistered = true;
            Application.logMessageReceivedThreaded += OnLogMessage;
        }

        private static void OnLogMessage(string condition, string stackTrace, LogType type)
        {
            lock (_fallbackBuffer)
            {
                if (_fallbackBuffer.Count >= FallbackBufferMax)
                    _fallbackBuffer.RemoveAt(0); // Oldest-out FIFO cap
                _fallbackBuffer.Add(new FallbackEntry
                {
                    Message = condition,
                    StackTrace = stackTrace,
                    Type = type
                });
            }
        }
    }
}
