using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor;
using UnityEditor.SceneManagement;
using Axiom.Editor.AgentBridge.Core;

namespace Axiom.Editor.AgentBridge.Actions
{
    /// <summary>
    /// Gives the agent the ability to enter/exit Play Mode, pause, step frames,
    /// track Play Mode state, and capture runtime snapshots.
    /// This is the "autonomous playtesting" capability.
    ///
    /// CRITICAL: Entering Play Mode triggers domain reload.
    /// Operations return immediately but the actual state change is deferred.
    /// Always call GetPlayModeState() to confirm state before proceeding.
    /// </summary>
    public static class PlayModeActions
    {
        // ─────────────────────────────────────────────────────
        //  3.1 GetPlayModeState
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// Returns the current editor play mode state.
        /// </summary>
        public static ActionResult GetPlayModeState()
        {
            bool isPlaying = EditorApplication.isPlaying;
            bool isPaused = EditorApplication.isPaused;
            bool isCompiling = EditorApplication.isCompiling;
            bool isUpdating = EditorApplication.isUpdating;

            string state;
            if (isCompiling) state = "Compiling";
            else if (!isPlaying && !isPaused) state = "EditMode";
            else if (isPlaying && !isPaused) state = "Playing";
            else if (isPlaying && isPaused) state = "Paused";
            else state = "Unknown";

            return ActionResult.Ok(
                $"State: {state}\n" +
                $"IsPlaying: {isPlaying}\n" +
                $"IsPaused: {isPaused}\n" +
                $"IsCompiling: {isCompiling}\n" +
                $"IsUpdating: {isUpdating}\n" +
                $"TimeSinceStartup: {EditorApplication.timeSinceStartup:F1}s");
        }

        // ─────────────────────────────────────────────────────
        //  3.2 EnterPlayMode
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// Enters Play Mode. The state change is deferred — this returns immediately.
        /// The agent should wait and call GetPlayModeState() to confirm.
        /// </summary>
        /// <param name="saveSceneFirst">If true, saves all open scenes before entering Play Mode.</param>
        public static ActionResult EnterPlayMode(bool saveSceneFirst = true)
        {
            if (EditorApplication.isPlaying)
                return ActionResult.Ok("Already in Play Mode.");

            if (EditorApplication.isCompiling)
                return ActionResult.Fail(
                    "Cannot enter Play Mode while compiling. " +
                    "Wait for EditorApplication.isCompiling == false.");

            if (saveSceneFirst)
            {
                EditorSceneManager.SaveOpenScenes();
                Debug.Log("[AgentBridge] Saved open scenes before Play Mode.");
            }

            Debug.Log("[AgentBridge] Entering Play Mode...");
            EditorApplication.isPlaying = true;

            return ActionResult.Ok(
                "Play Mode requested. State change is deferred to end of frame. " +
                "Call GetPlayModeState() to confirm when ready.");
        }

        // ─────────────────────────────────────────────────────
        //  3.3 ExitPlayMode
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// Exits Play Mode. State change is deferred.
        /// </summary>
        public static ActionResult ExitPlayMode()
        {
            if (!EditorApplication.isPlaying)
                return ActionResult.Ok("Already in Edit Mode.");

            Debug.Log("[AgentBridge] Exiting Play Mode...");
            EditorApplication.isPlaying = false;

            return ActionResult.Ok(
                "Exit Play Mode requested. State change is deferred. " +
                "Call GetPlayModeState() to confirm.");
        }

        // ─────────────────────────────────────────────────────
        //  3.4 PausePlayMode
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// Pauses or unpauses Play Mode.
        /// </summary>
        /// <param name="pause">True to pause, false to unpause.</param>
        public static ActionResult PausePlayMode(bool pause = true)
        {
            if (!EditorApplication.isPlaying)
                return ActionResult.Fail("Not in Play Mode. Cannot pause.");

            EditorApplication.isPaused = pause;

            string state = pause ? "Paused" : "Unpaused";
            Debug.Log($"[AgentBridge] {state} Play Mode.");
            return ActionResult.Ok($"Play Mode {state.ToLower()}.");
        }

        // ─────────────────────────────────────────────────────
        //  3.5 StepFrame
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// Advances Play Mode by a single frame. Only works when paused.
        /// </summary>
        public static ActionResult StepFrame()
        {
            if (!EditorApplication.isPlaying)
                return ActionResult.Fail("Not in Play Mode. Cannot step.");

            if (!EditorApplication.isPaused)
            {
                // Auto-pause first, then step
                EditorApplication.isPaused = true;
            }

            EditorApplication.Step();

            Debug.Log("[AgentBridge] Stepped one frame.");
            return ActionResult.Ok("Stepped one frame in Play Mode.");
        }

        // ─────────────────────────────────────────────────────
        //  3.6 StepMultipleFrames
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// Steps multiple frames in Play Mode. Pauses after completion.
        /// </summary>
        /// <param name="frameCount">Number of frames to advance.</param>
        public static ActionResult StepMultipleFrames(int frameCount)
        {
            if (!EditorApplication.isPlaying)
                return ActionResult.Fail("Not in Play Mode. Cannot step.");

            if (frameCount <= 0 || frameCount > 1000)
                return ActionResult.Fail("Frame count must be between 1 and 1000.");

            if (!EditorApplication.isPaused)
                EditorApplication.isPaused = true;

            for (int i = 0; i < frameCount; i++)
                EditorApplication.Step();

            Debug.Log($"[AgentBridge] Stepped {frameCount} frames.");
            return ActionResult.Ok($"Stepped {frameCount} frames in Play Mode.");
        }

        // ─────────────────────────────────────────────────────
        //  3.7 CapturePlayModeState
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// Captures a snapshot of runtime state while in Play Mode.
        /// Includes active scene hierarchy, enabled components, and transform data.
        /// Useful for comparing before/after state during automated playtesting.
        /// </summary>
        /// <param name="rootPath">Optional root path to limit scope. Null = entire scene.</param>
        public static ActionResult CapturePlayModeState(string rootPath = null)
        {
            if (!EditorApplication.isPlaying)
                return ActionResult.Fail("Not in Play Mode. Cannot capture runtime state.");

            var sb = new StringBuilder();
            sb.AppendLine("# Play Mode State Capture\n");
            sb.AppendLine($"- **Time:** {Time.time:F3}s");
            sb.AppendLine($"- **Frame:** {Time.frameCount}");
            sb.AppendLine($"- **Scene:** {SceneManager.GetActiveScene().name}");
            sb.AppendLine($"- **Root Filter:** {rootPath ?? "(all)"}");
            sb.AppendLine();

            GameObject[] roots;
            if (rootPath != null)
            {
                var root = GameObject.Find(rootPath);
                if (root == null)
                    return ActionResult.Fail($"Root object not found: {rootPath}");
                roots = new[] { root };
            }
            else
            {
                roots = SceneManager.GetActiveScene().GetRootGameObjects();
            }

            sb.AppendLine("| Object | Active | Position | Components |");
            sb.AppendLine("| :--- | :--- | :--- | :--- |");

            int objectCount = 0;
            foreach (var root in roots)
            {
                CaptureObjectRecursive(root.transform, sb, "", ref objectCount);
            }

            sb.AppendLine($"\n---\n**Objects captured: {objectCount}**");

            string reportPath = OutputWriter.WriteReport("playmode_state", sb.ToString());
            return ActionResult.Ok(
                $"Captured {objectCount} objects at frame {Time.frameCount}. Report: {reportPath}");
        }

        private static void CaptureObjectRecursive(
            Transform t, StringBuilder sb, string indent, ref int count)
        {
            count++;
            var components = t.GetComponents<Component>();
            var componentNames = new List<string>();
            foreach (var c in components)
            {
                if (c == null) { componentNames.Add("(Missing)"); continue; }
                if (c is Transform) continue;
                componentNames.Add(c.GetType().Name);
            }

            sb.AppendLine(
                $"| {indent}{t.name} | {t.gameObject.activeInHierarchy} | " +
                $"{t.position:F2} | {string.Join(", ", componentNames)} |");

            for (int i = 0; i < t.childCount; i++)
                CaptureObjectRecursive(t.GetChild(i), sb, indent + "  ", ref count);
        }

        // ─────────────────────────────────────────────────────
        //  3.8 WaitForCompilation
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// Utility: Checks if the editor is currently compiling.
        /// Agent should call this before entering Play Mode or performing
        /// actions that require a stable domain state.
        /// </summary>
        public static ActionResult WaitForCompilation()
        {
            if (!EditorApplication.isCompiling)
                return ActionResult.Ok("Compilation complete. Editor is ready.");

            return ActionResult.Fail(
                "Editor is currently compiling. " +
                "Wait and call WaitForCompilation() again before proceeding. " +
                "Do NOT enter Play Mode or modify scenes during compilation.");
        }

        // ─────────────────────────────────────────────────────
        //  3.9 ResetAnimatorPool
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// Resets all disabled Animators using keepAnimatorStateOnDisable = false,
        /// then re-enables them. Used for pool recycling in high-object-count scenes.
        /// Works in both Edit and Play Mode.
        /// </summary>
        /// <param name="rootPath">Optional scope — only reset Animators under this path.</param>
        /// <param name="dryRun">If true, report which Animators would be reset without changing them.</param>
        public static ActionResult ResetAnimatorPool(string rootPath = null, bool dryRun = false)
        {
            // Collect all Animators in the scene
            var allAnimators = UnityEngine.Object.FindObjectsByType<Animator>(FindObjectsSortMode.None);

            // Filter by rootPath if provided
            List<Animator> targetAnimators = new List<Animator>();
            if (rootPath != null)
            {
                GameObject root = GameObject.Find(rootPath);
                if (root == null)
                    return ActionResult.Fail($"Root object not found: {rootPath}");

                var rootAnimators = root.GetComponentsInChildren<Animator>(includeInactive: true);
                targetAnimators.AddRange(rootAnimators);
            }
            else
            {
                targetAnimators.AddRange(allAnimators);
            }

            // Filter to Animators that need resetting:
            // - Disabled Animators
            // - Animators with keepAnimatorStateOnDisable == true (which need to be set to false)
            List<Animator> toReset = new List<Animator>();
            foreach (var anim in targetAnimators)
            {
                if (!anim.enabled || anim.keepAnimatorStateOnDisable)
                {
                    toReset.Add(anim);
                }
            }

            if (toReset.Count == 0)
                return ActionResult.Ok($"No Animators need resetting (checked {targetAnimators.Count} animator(s)).");

            if (dryRun)
            {
                var sb = new StringBuilder();
                sb.AppendLine($"# Animator Pool Reset (Dry Run)\n");
                sb.AppendLine($"Found {toReset.Count} animator(s) that would be reset:\n");
                sb.AppendLine("| Object | Enabled | keepAnimatorStateOnDisable |");
                sb.AppendLine("| :--- | :--- | :--- |");

                foreach (var anim in toReset)
                {
                    sb.AppendLine($"| {GetObjectPath(anim.transform)} | {anim.enabled} | {anim.keepAnimatorStateOnDisable} |");
                }

                string reportPath = OutputWriter.WriteReport("animator_pool_reset_dryrun", sb.ToString());
                return ActionResult.Ok($"Dry run complete. {toReset.Count} animator(s) would be reset. Report: {reportPath}");
            }

            // Perform the reset
            int resetCount = 0;
            foreach (var anim in toReset)
            {
                bool wasEnabled = anim.enabled;

                // Disable the Animator
                anim.enabled = false;

                // Set keepAnimatorStateOnDisable to false to clear internal state
                anim.keepAnimatorStateOnDisable = false;

                // Re-enable if it was originally enabled
                if (wasEnabled)
                {
                    anim.enabled = true;
                }

                resetCount++;
            }

            string scope = rootPath != null ? $" under {rootPath}" : "";
            return ActionResult.Ok($"Reset {resetCount} animator(s){scope}. Internal state cleared for pool recycling.");
        }

        private static string GetObjectPath(Transform t)
        {
            if (t == null) return "<null>";
            if (t.parent == null) return t.name;
            return GetObjectPath(t.parent) + "/" + t.name;
        }

        // ─────────────────────────────────────────────────────
        //  Menu Items
        // ─────────────────────────────────────────────────────

        [MenuItem("Axiom/AgentBridge/Actions/Show Play Mode State")]
        public static void MenuShowState()
        {
            Debug.Log($"[AgentBridge] {GetPlayModeState().Message}");
        }

        [MenuItem("Axiom/AgentBridge/Actions/Enter Play Mode (Agent)")]
        public static void MenuEnterPlay()
        {
            Debug.Log($"[AgentBridge] {EnterPlayMode().Message}");
        }

        [MenuItem("Axiom/AgentBridge/Actions/Exit Play Mode (Agent)")]
        public static void MenuExitPlay()
        {
            Debug.Log($"[AgentBridge] {ExitPlayMode().Message}");
        }
    }
}
