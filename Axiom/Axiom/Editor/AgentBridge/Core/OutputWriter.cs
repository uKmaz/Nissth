using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace Axiom.Editor.AgentBridge.Core
{
    /// <summary>
    /// Shared utility for writing diagnostic reports to the AgentReports directory.
    /// Every diagnostic tool routes output through this class.
    /// </summary>
    public static class OutputWriter
    {
        /// <summary>
        /// Absolute path to the AgentReports folder at the project root (next to Assets/, NOT inside it).
        /// </summary>
        public static readonly string ReportsRoot =
            Path.Combine(Path.GetDirectoryName(Application.dataPath), "AgentReports");

        /// <summary>
        /// Writes a diagnostic report to the AgentReports directory.
        /// </summary>
        /// <param name="toolName">Name of the tool generating the report (e.g., "hierarchy_lens").</param>
        /// <param name="content">The full report content string.</param>
        /// <param name="format">File extension: "md", "json", or "txt". Default "md".</param>
        /// <param name="customFileName">Optional custom filename (without extension). If null, uses toolName + timestamp.</param>
        /// <returns>The full file path of the written report.</returns>
        public static string WriteReport(string toolName, string content, string format = "md", string customFileName = null)
        {
            // Ensure the reports directory exists
            if (!Directory.Exists(ReportsRoot))
                Directory.CreateDirectory(ReportsRoot);

            // Build filename
            string fileName;
            if (customFileName != null)
                fileName = $"{customFileName}.{format}";
            else
                fileName = $"{toolName}_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.{format}";

            string filePath = Path.Combine(ReportsRoot, fileName);

            // Write with UTF-8 no BOM
            File.WriteAllText(filePath, content, new UTF8Encoding(false));

            Debug.Log($"[AgentBridge] Report written: {filePath}");
            return filePath;
        }

        /// <summary>
        /// Returns the absolute path to the AgentReports root directory.
        /// </summary>
        public static string GetReportsRoot()
        {
            return ReportsRoot;
        }

        /// <summary>
        /// Deletes all files in AgentReports/ without deleting the directory itself.
        /// </summary>
        public static void ClearReports()
        {
            if (!Directory.Exists(ReportsRoot))
            {
                Debug.Log("[AgentBridge] ClearReports: AgentReports/ directory does not exist. Nothing to clear.");
                return;
            }

            string[] files = Directory.GetFiles(ReportsRoot);
            foreach (string file in files)
                File.Delete(file);

            Debug.Log($"[AgentBridge] ClearReports: Removed {files.Length} file(s) from {ReportsRoot}");
        }

        // ─────────────────────────────────────────────────────
        //  Snapshot Storage
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// Absolute path to the Snapshots sub-directory inside AgentReports/.
        /// </summary>
        public static string SnapshotsRoot => Path.Combine(ReportsRoot, "Snapshots");

        /// <summary>
        /// Writes a snapshot file to the AgentReports/Snapshots/ directory.
        /// Overwrites any existing snapshot with the same label.
        /// </summary>
        /// <param name="label">Snapshot label (e.g., "before_player_setup"). Used as filename.</param>
        /// <param name="content">The snapshot content string.</param>
        /// <returns>The full file path of the written snapshot.</returns>
        public static string WriteSnapshot(string label, string content)
        {
            if (!Directory.Exists(SnapshotsRoot))
                Directory.CreateDirectory(SnapshotsRoot);

            string filePath = Path.Combine(SnapshotsRoot, label + ".snapshot");
            File.WriteAllText(filePath, content, new UTF8Encoding(false));
            Debug.Log($"[AgentBridge] Snapshot saved: {label} -> {filePath}");
            return filePath;
        }

        /// <summary>
        /// Reads a previously saved snapshot by label.
        /// </summary>
        /// <param name="label">The snapshot label.</param>
        /// <returns>The snapshot content string, or null if not found.</returns>
        public static string ReadSnapshot(string label)
        {
            string filePath = Path.Combine(SnapshotsRoot, label + ".snapshot");
            return File.Exists(filePath) ? File.ReadAllText(filePath) : null;
        }

        /// <summary>
        /// Lists all saved snapshot labels with their file sizes and last modified times.
        /// </summary>
        /// <returns>A formatted string listing all snapshots, or "No snapshots found." if empty.</returns>
        public static string ListSnapshots()
        {
            if (!Directory.Exists(SnapshotsRoot))
                return "No snapshots found.";

            var files = Directory.GetFiles(SnapshotsRoot, "*.snapshot")
                .OrderBy(f => new FileInfo(f).LastWriteTime)
                .ToArray();

            if (files.Length == 0)
                return "No snapshots found.";

            var sb = new StringBuilder();
            foreach (string file in files)
            {
                var info = new FileInfo(file);
                string label = Path.GetFileNameWithoutExtension(file);
                sb.AppendLine($"{label} | {info.Length} bytes | {info.LastWriteTime:yyyy-MM-dd HH:mm:ss}");
            }
            return sb.ToString().TrimEnd();
        }

        /// <summary>
        /// Deletes a specific snapshot by label.
        /// </summary>
        /// <param name="label">The snapshot label to delete.</param>
        /// <returns>True if deleted, false if not found.</returns>
        public static bool DeleteSnapshot(string label)
        {
            string filePath = Path.Combine(SnapshotsRoot, label + ".snapshot");
            if (!File.Exists(filePath)) return false;
            File.Delete(filePath);
            return true;
        }

        /// <summary>
        /// Deletes all snapshots in AgentReports/Snapshots/.
        /// </summary>
        /// <returns>Number of snapshots deleted.</returns>
        public static int ClearSnapshots()
        {
            if (!Directory.Exists(SnapshotsRoot)) return 0;

            string[] files = Directory.GetFiles(SnapshotsRoot, "*.snapshot");
            foreach (string file in files)
                File.Delete(file);

            Debug.Log($"[AgentBridge] Cleared {files.Length} snapshots.");
            return files.Length;
        }
    }
}
