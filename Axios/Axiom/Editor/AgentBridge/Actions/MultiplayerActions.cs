using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEditor;
using Axiom.Editor.AgentBridge.Core;

namespace Axiom.Editor.AgentBridge.Actions
{
    /// <summary>
    /// Gives the agent control over Unity 6's Multiplayer Play Mode (MPPM).
    /// MPPM 2.0+ is built into the Unity Editor with no separate assembly —
    /// types are accessed via reflection to avoid hard compile-time dependencies.
    /// Requires com.unity.multiplayer.playmode package.
    /// </summary>
    public static class MultiplayerActions
    {
        // ─────────────────────────────────────────────────────
        //  Reflection Cache
        // ─────────────────────────────────────────────────────

#if AXIOM_HAS_MPPM
        private static bool _reflectionInit = false;
        private static Type _mppmType;           // Unity.Multiplayer.Playmode.MultiplayerPlaymode
        private static Type _virtualPlayerType;  // Unity.Multiplayer.Playmode.VirtualPlayer
        private static PropertyInfo _piPlayers;  // IEnumerable<VirtualPlayer> Players
        private static PropertyInfo _piIsRunning; // bool IsRunning
        private static PropertyInfo _piTag;       // string Tag
        private static PropertyInfo _piEnabled;   // bool Enabled / IsActivated
        private static PropertyInfo _piName;      // string Name

        private static bool InitReflection()
        {
            if (_reflectionInit) return _mppmType != null;
            _reflectionInit = true;

            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                string asmName = asm.GetName().Name;
                if (!asmName.Contains("Multiplayer") && !asmName.Contains("multiplayer") && !asmName.Contains("UnityEditor"))
                    continue;

                // Try common MPPM type paths
                foreach (string typeName in new[]
                {
                    "Unity.Multiplayer.Playmode.MultiplayerPlaymode",
                    "Unity.Multiplayer.PlayMode.MultiplayerPlayMode",
                    "UnityEditor.Multiplayer.MultiplayerPlayMode",
                    "Unity.Multiplayer.Playmode.PlayModeManager",
                })
                {
                    _mppmType = asm.GetType(typeName);
                    if (_mppmType != null) break;
                }
                if (_mppmType != null) break;
            }

            if (_mppmType == null)
            {
                // Fallback: broad search across all assemblies
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    foreach (var t in asm.GetTypes())
                    {
                        if ((t.Name == "MultiplayerPlaymode" || t.Name == "MultiplayerPlayMode" || t.Name == "PlayModeManager")
                            && t.Namespace != null && t.Namespace.Contains("Multiplayer"))
                        {
                            _mppmType = t;
                            break;
                        }
                    }
                    if (_mppmType != null) break;
                }
            }

            if (_mppmType == null) return false;

            // Discover VirtualPlayer type
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                foreach (string typeName in new[]
                {
                    "Unity.Multiplayer.Playmode.VirtualPlayer",
                    "Unity.Multiplayer.PlayMode.VirtualPlayer",
                })
                {
                    _virtualPlayerType = asm.GetType(typeName);
                    if (_virtualPlayerType != null) break;
                }
                if (_virtualPlayerType == null)
                {
                    foreach (var t in asm.GetTypes())
                    {
                        if (t.Name == "VirtualPlayer" && t.Namespace != null && t.Namespace.Contains("Multiplayer"))
                        {
                            _virtualPlayerType = t;
                            break;
                        }
                    }
                }
                if (_virtualPlayerType != null) break;
            }

            // Cache property infos if virtual player type found
            if (_virtualPlayerType != null)
            {
                foreach (string name in new[] { "Tag", "PlayerTag", "tag" })
                {
                    _piTag = _virtualPlayerType.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (_piTag != null) break;
                }
                foreach (string name in new[] { "Enabled", "IsEnabled", "IsActivated", "Active", "enabled" })
                {
                    _piEnabled = _virtualPlayerType.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (_piEnabled != null) break;
                }
                foreach (string name in new[] { "Name", "PlayerName", "name" })
                {
                    _piName = _virtualPlayerType.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (_piName == null)
                        _piName = _virtualPlayerType.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance) != null
                            ? null : null; // just check property
                    if (_piName != null) break;
                }
            }

            // Cache MPPM main type properties
            foreach (string name in new[] { "Players", "VirtualPlayers", "CurrentPlayers", "Instances" })
            {
                _piPlayers = _mppmType.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance);
                if (_piPlayers != null) break;
            }
            foreach (string name in new[] { "IsRunning", "Active", "IsActive", "Running" })
            {
                _piIsRunning = _mppmType.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance);
                if (_piIsRunning != null) break;
            }

            return true;
        }

        /// <summary>
        /// Gets the singleton or static instance of the MPPM manager.
        /// Returns null if unavailable.
        /// </summary>
        private static object GetMppmInstance()
        {
            if (_mppmType == null) return null;

            // Try static properties first
            foreach (string name in new[] { "Instance", "instance", "Current", "Singleton" })
            {
                var prop = _mppmType.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (prop != null) return prop.GetValue(null);
                var field = _mppmType.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (field != null) return field.GetValue(null);
            }
            return null;
        }
#endif

        // ─────────────────────────────────────────────────────
        //  1.1 GetMultiplayerStatus
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// Reports current MPPM configuration — player count, tags, active players.
        /// </summary>
        public static ActionResult GetMultiplayerStatus()
        {
#if AXIOM_HAS_MPPM
            var sb = new StringBuilder();
            sb.AppendLine("# Multiplayer Play Mode Status\n");
            sb.AppendLine("- **Package:** com.unity.multiplayer.playmode 2.0.1");

            bool reflectionOk = InitReflection();
            if (!reflectionOk || _mppmType == null)
            {
                sb.AppendLine("- **API Access:** Not available via reflection (MPPM 2.0+ built-in editor feature)");
                sb.AppendLine();
                sb.AppendLine("## MPPM Configuration");
                sb.AppendLine();
                sb.AppendLine("*MPPM 2.0+ is a built-in Unity Editor feature. API types were not found via reflection.*");
                sb.AppendLine("*Use Window > Multiplayer Play Mode in the Unity Editor to configure virtual players.*");
                sb.AppendLine();
                sb.AppendLine("### Available Actions");
                sb.AppendLine("- Configure players via Unity Editor Menu: **Window > Multiplayer Play Mode**");
                sb.AppendLine("- Virtual player logs in: `Library/com.unity.multiplayer.playmode/`");

                string rp = OutputWriter.WriteReport("multiplayer_status", sb.ToString());
                return ActionResult.Ok($"MPPM 2.0.1 installed (built-in editor feature). Report: {rp}");
            }

            // Reflection succeeded — gather status
            object instance = GetMppmInstance();

            bool isRunning = false;
            if (_piIsRunning != null)
            {
                try { isRunning = (bool)_piIsRunning.GetValue(instance); } catch { }
            }

            sb.AppendLine($"- **State:** {(isRunning ? "Running" : "Stopped")}");

            // Enumerate players
            List<object> players = new List<object>();
            if (_piPlayers != null)
            {
                try
                {
                    var playerEnum = _piPlayers.GetValue(instance) as System.Collections.IEnumerable;
                    if (playerEnum != null)
                        foreach (var p in playerEnum) players.Add(p);
                }
                catch { }
            }

            sb.AppendLine($"- **Configured Players:** {players.Count}");
            sb.AppendLine();
            sb.AppendLine("### Player Configuration");
            sb.AppendLine();
            sb.AppendLine("| Player | Tag | Active |");
            sb.AppendLine("| :--- | :--- | :--- |");

            for (int i = 0; i < players.Count; i++)
            {
                string tag = GetStringProp(players[i], _piTag, $"Player {i + 1}");
                bool enabled = GetBoolProp(players[i], _piEnabled, true);
                string name = GetStringProp(players[i], _piName, i == 0 ? "Main Editor" : $"Virtual Player {i}");
                sb.AppendLine($"| {name} | {tag} | {enabled} |");
            }

            if (players.Count == 0)
            {
                sb.AppendLine("| Main Editor | — | Yes |");
                sb.AppendLine("*No virtual players configured.*");
            }

            string reportPath = OutputWriter.WriteReport("multiplayer_status", sb.ToString());
            return ActionResult.Ok($"MPPM status: {players.Count} player(s), {(isRunning ? "Running" : "Stopped")}. Report: {reportPath}");
#else
            return ActionResult.Fail("Multiplayer Play Mode package not installed. Install com.unity.multiplayer.playmode via Package Manager.");
#endif
        }

        // ─────────────────────────────────────────────────────
        //  1.2 ConfigurePlayers
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// Sets the number of virtual players and their tags.
        /// </summary>
        /// <param name="playerCount">Total players including main editor (2–4).</param>
        /// <param name="tags">Tags for each player (e.g., ["Server", "Client", "Client"]).</param>
        public static ActionResult ConfigurePlayers(int playerCount, string[] tags = null)
        {
#if AXIOM_HAS_MPPM
            if (playerCount < 2 || playerCount > 4)
                return ActionResult.Fail("playerCount must be between 2 and 4 (including main editor).");

            if (EditorApplication.isPlaying)
                return ActionResult.Fail("Cannot configure players while in Play Mode. Exit Play Mode first.");

            bool reflectionOk = InitReflection();
            if (!reflectionOk || _mppmType == null)
            {
                return ActionResult.Fail(
                    "MPPM API not available via reflection. " +
                    "Configure players manually via Window > Multiplayer Play Mode. " +
                    $"Requested: {playerCount} players, tags: {(tags != null ? string.Join(", ", tags) : "none")}");
            }

            return ActionResult.Fail(
                $"ConfigurePlayers requested {playerCount} player(s) but MPPM 2.0 programmatic configuration is not " +
                $"available via reflection. Use Window > Multiplayer Play Mode to configure virtual players manually.");
#else
            return ActionResult.Fail("Multiplayer Play Mode package not installed.");
#endif
        }

        // ─────────────────────────────────────────────────────
        //  1.3 SetPlayerActive
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// Enables or disables a virtual player by index.
        /// </summary>
        /// <param name="playerIndex">Player index (0 = main editor, 1+ = virtual players).</param>
        /// <param name="active">True to activate, false to deactivate.</param>
        public static ActionResult SetPlayerActive(int playerIndex, bool active)
        {
#if AXIOM_HAS_MPPM
            if (playerIndex < 0)
                return ActionResult.Fail("playerIndex must be 0 or greater.");

            if (playerIndex == 0)
                return ActionResult.Fail("Cannot activate/deactivate the main editor (index 0).");

            if (EditorApplication.isPlaying)
                return ActionResult.Fail("Cannot change player active state while in Play Mode.");

            bool reflectionOk = InitReflection();
            if (!reflectionOk || _mppmType == null)
            {
                return ActionResult.Fail(
                    $"MPPM API not available via reflection. " +
                    $"Use Window > Multiplayer Play Mode to activate/deactivate player {playerIndex} manually.");
            }

            object instance = GetMppmInstance();
            List<object> players = new List<object>();

            if (_piPlayers != null)
            {
                try
                {
                    var playerEnum = _piPlayers.GetValue(instance) as System.Collections.IEnumerable;
                    if (playerEnum != null)
                        foreach (var p in playerEnum) players.Add(p);
                }
                catch { }
            }

            if (playerIndex >= players.Count)
                return ActionResult.Fail($"Player index {playerIndex} out of range. Found {players.Count} player(s).");

            var player = players[playerIndex];

            // Try to set enabled state
            if (_piEnabled != null && _piEnabled.CanWrite)
            {
                try
                {
                    _piEnabled.SetValue(player, active);
                    return ActionResult.Ok($"Player {playerIndex} {(active ? "activated" : "deactivated")}.");
                }
                catch (Exception ex)
                {
                    return ActionResult.Fail($"Failed to set player {playerIndex} active state: {ex.Message}");
                }
            }

            // Try SetEnabled method
            var method = player.GetType().GetMethod("SetEnabled",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (method != null)
            {
                try
                {
                    method.Invoke(player, new object[] { active });
                    return ActionResult.Ok($"Player {playerIndex} {(active ? "activated" : "deactivated")}.");
                }
                catch (Exception ex)
                {
                    return ActionResult.Fail($"Failed to invoke SetEnabled on player {playerIndex}: {ex.Message}");
                }
            }

            return ActionResult.Fail(
                $"Could not find a writable Enabled property on VirtualPlayer type '{player.GetType().FullName}'. " +
                $"Use Window > Multiplayer Play Mode to manage player state manually.");
#else
            return ActionResult.Fail("Multiplayer Play Mode package not installed.");
#endif
        }

        // ─────────────────────────────────────────────────────
        //  1.4 GetPlayerLogs
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// Retrieves console logs from a specific virtual player process.
        /// Useful for debugging network desync — compare logs across players.
        /// </summary>
        /// <param name="playerIndex">Player index (0 = main editor).</param>
        /// <param name="filter">Optional filter: "errors", "warnings", or null for all.</param>
        public static ActionResult GetPlayerLogs(int playerIndex, string filter = null)
        {
#if AXIOM_HAS_MPPM
            // MPPM virtual player logs are stored in Library/com.unity.multiplayer.playmode/
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            string mppmLogDir = Path.Combine(projectRoot, "Library", "com.unity.multiplayer.playmode");

            var sb = new StringBuilder();
            sb.AppendLine($"# MPPM Player Logs — Player {playerIndex}\n");
            sb.AppendLine($"**Filter:** {filter ?? "all"}");
            sb.AppendLine($"**Log Directory:** {mppmLogDir}\n");

            if (playerIndex == 0)
            {
                sb.AppendLine("*Player 0 is the main editor. Use LogMirror for main editor console logs.*\n");
                string mainRp = OutputWriter.WriteReport("mppm_player_logs", sb.ToString());
                return ActionResult.Ok($"Player 0 is the main editor. Use LogMirror for main editor logs. Report: {mainRp}");
            }

            if (!Directory.Exists(mppmLogDir))
            {
                sb.AppendLine($"*MPPM log directory not found: `{mppmLogDir}`*\n");
                sb.AppendLine("This directory is created when virtual players have been run at least once.\n");
                sb.AppendLine("**Steps to generate logs:**");
                sb.AppendLine("1. Open Window > Multiplayer Play Mode");
                sb.AppendLine("2. Enable virtual players");
                sb.AppendLine("3. Enter Play Mode");
                sb.AppendLine("4. Exit Play Mode");
                sb.AppendLine("5. Call GetPlayerLogs() again");

                string notFoundPath = OutputWriter.WriteReport("mppm_player_logs", sb.ToString());
                return ActionResult.Ok($"MPPM log directory not found (no virtual players run yet). Report: {notFoundPath}");
            }

            // Look for player-specific log files
            string[] logFiles = Directory.GetFiles(mppmLogDir, "*.log", SearchOption.AllDirectories);
            string[] jsonFiles = Directory.GetFiles(mppmLogDir, "*.json", SearchOption.AllDirectories);

            sb.AppendLine($"**Files in MPPM directory:** {logFiles.Length + jsonFiles.Length}\n");

            // Try to find player-specific log
            string playerLogFile = null;
            string[] playerPatterns = new[]
            {
                $"*player{playerIndex}*",
                $"*Player{playerIndex}*",
                $"*virtualplayer{playerIndex}*",
                $"*VirtualPlayer{playerIndex}*",
                $"*clone{playerIndex - 1}*",
            };

            foreach (string pattern in playerPatterns)
            {
                var matches = Directory.GetFiles(mppmLogDir, pattern, SearchOption.AllDirectories);
                if (matches.Length > 0) { playerLogFile = matches[0]; break; }
            }

            if (playerLogFile != null && File.Exists(playerLogFile))
            {
                string[] lines = File.ReadAllLines(playerLogFile);
                sb.AppendLine($"**Log File:** `{playerLogFile}`");
                sb.AppendLine($"**Total Lines:** {lines.Length}\n");

                int errorCount = 0, warnCount = 0, infoCount = 0;
                var filteredLines = new List<string>();

                foreach (string line in lines)
                {
                    bool isError = line.Contains("[Error]") || line.Contains("ERROR") || line.Contains("Exception");
                    bool isWarn = line.Contains("[Warning]") || line.Contains("WARNING") || line.Contains("WARN");

                    if (isError) errorCount++;
                    else if (isWarn) warnCount++;
                    else infoCount++;

                    if (filter == null) filteredLines.Add(line);
                    else if (filter.Equals("errors", StringComparison.OrdinalIgnoreCase) && isError) filteredLines.Add(line);
                    else if (filter.Equals("warnings", StringComparison.OrdinalIgnoreCase) && (isError || isWarn)) filteredLines.Add(line);
                }

                sb.AppendLine($"**Summary:** {errorCount} error(s), {warnCount} warning(s), {infoCount} info\n");
                sb.AppendLine("### Log Entries\n");
                sb.AppendLine("```");

                int shown = Math.Min(filteredLines.Count, 100);
                for (int i = 0; i < shown; i++)
                    sb.AppendLine(filteredLines[i]);

                if (filteredLines.Count > 100)
                    sb.AppendLine($"... ({filteredLines.Count - 100} more lines)");

                sb.AppendLine("```");
            }
            else
            {
                // List all files found for diagnostics
                sb.AppendLine("*No player-specific log file found. Listing all MPPM files:*\n");
                foreach (string f in logFiles)
                    sb.AppendLine($"- `{f}`");
                foreach (string f in jsonFiles)
                    sb.AppendLine($"- `{f}`");
            }

            string reportPath = OutputWriter.WriteReport("mppm_player_logs", sb.ToString());
            return ActionResult.Ok($"MPPM player {playerIndex} logs written to: {reportPath}");
#else
            return ActionResult.Fail("Multiplayer Play Mode package not installed.");
#endif
        }

        // ─────────────────────────────────────────────────────
        //  1.5 RunMultiplayerTest
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// Enters Play Mode with all active virtual players, runs for a duration,
        /// captures logs from all players, then exits. Produces a comparison report.
        /// Note: This is an async operation — enters Play Mode and schedules cleanup
        /// via EditorApplication.update. Check AgentReports for completion.
        /// </summary>
        /// <param name="durationSeconds">How long to run the simulation.</param>
        /// <param name="captureIntervalSeconds">How often to capture state (unused in current implementation).</param>
        public static ActionResult RunMultiplayerTest(float durationSeconds = 10f, float captureIntervalSeconds = 2f)
        {
#if AXIOM_HAS_MPPM
            if (EditorApplication.isPlaying)
                return ActionResult.Fail("Already in Play Mode. Exit first.");

            if (EditorApplication.isCompiling)
                return ActionResult.Fail("Cannot run test while compiling.");

            if (durationSeconds <= 0 || durationSeconds > 300)
                return ActionResult.Fail("durationSeconds must be between 1 and 300.");

            // Schedule async test via EditorApplication.update state machine
            double startTime = EditorApplication.timeSinceStartup;
            double endTime = startTime + durationSeconds;
            bool testStarted = false;
            bool testEnded = false;

            EditorApplication.update += RunTest;

            void RunTest()
            {
                double now = EditorApplication.timeSinceStartup;

                if (!testStarted)
                {
                    UnityEditor.SceneManagement.EditorSceneManager.SaveOpenScenes();
                    EditorApplication.isPlaying = true;
                    testStarted = true;
                    Debug.Log($"[MultiplayerActions] Multiplayer test started. Duration: {durationSeconds}s");
                    return;
                }

                if (testStarted && !testEnded && now >= endTime)
                {
                    EditorApplication.isPlaying = false;
                    testEnded = true;
                    EditorApplication.update -= RunTest;

                    // Write summary report
                    var sb = new StringBuilder();
                    sb.AppendLine("# Multiplayer Test Report\n");
                    sb.AppendLine($"**Duration:** {durationSeconds}s");
                    sb.AppendLine($"**Completed:** {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                    sb.AppendLine();
                    sb.AppendLine("## Results\n");
                    sb.AppendLine("Test completed. Check individual player logs via GetPlayerLogs() for detailed output.");
                    sb.AppendLine();

                    string projectRoot = Path.GetDirectoryName(Application.dataPath);
                    string mppmLogDir = Path.Combine(projectRoot, "Library", "com.unity.multiplayer.playmode");

                    if (Directory.Exists(mppmLogDir))
                    {
                        sb.AppendLine("## MPPM Log Files\n");
                        foreach (string f in Directory.GetFiles(mppmLogDir, "*", SearchOption.AllDirectories))
                            sb.AppendLine($"- `{Path.GetFileName(f)}`");
                    }
                    else
                    {
                        sb.AppendLine("*MPPM log directory not found after test.*");
                    }

                    OutputWriter.WriteReport("multiplayer_test_report", sb.ToString());
                    Debug.Log("[MultiplayerActions] Multiplayer test complete. Report written to AgentReports/.");
                }
            }

            return ActionResult.Ok(
                $"Multiplayer test scheduled: {durationSeconds}s duration. " +
                $"Editor will enter Play Mode shortly. " +
                $"Report will appear in AgentReports/multiplayer_test_report when complete.");
#else
            return ActionResult.Fail("Multiplayer Play Mode package not installed.");
#endif
        }

        // ─────────────────────────────────────────────────────
        //  Reflection Helpers
        // ─────────────────────────────────────────────────────

        private static string GetStringProp(object obj, PropertyInfo pi, string fallback)
        {
            if (obj == null || pi == null) return fallback;
            try { return pi.GetValue(obj)?.ToString() ?? fallback; } catch { return fallback; }
        }

        private static bool GetBoolProp(object obj, PropertyInfo pi, bool fallback)
        {
            if (obj == null || pi == null) return fallback;
            try { return (bool)pi.GetValue(obj); } catch { return fallback; }
        }

        // ─────────────────────────────────────────────────────
        //  Menu Items
        // ─────────────────────────────────────────────────────

        [MenuItem("Axiom/AgentBridge/Actions/Multiplayer Status")]
        public static void MenuGetStatus() => GetMultiplayerStatus();
    }
}
