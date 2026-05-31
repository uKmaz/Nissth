using UnityEngine;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using System.IO;
using System.Collections.Generic;

namespace Axiom.Editor.Installer
{
    /// <summary>
    /// An optional package that unlocks conditional Axiom features.
    /// PackageId is the current/canonical package ID used for installation.
    /// AltPackageId handles renamed packages (e.g. com.unity.sentis → com.unity.ai.inference).
    /// </summary>
    internal readonly struct OptionalPackage
    {
        public readonly string PackageId;
        public readonly string AltPackageId;
        public readonly string DefineSymbol;
        public readonly string FriendlyName;
        public readonly string FeatureDescription;

        public OptionalPackage(string packageId, string altPackageId, string define, string name, string desc)
        {
            PackageId        = packageId;
            AltPackageId     = altPackageId;
            DefineSymbol     = define;
            FriendlyName     = name;
            FeatureDescription = desc;
        }

        public bool IsInstalled() =>
            AxiomInstaller.IsPackageInstalled(PackageId) ||
            (!string.IsNullOrEmpty(AltPackageId) && AxiomInstaller.IsPackageInstalled(AltPackageId));
    }

    /// <summary>
    /// Deploys Axiom workspace rules and handles optional package installation.
    ///
    /// Menu structure:
    ///   Tools/Axiom/Install Workspace Rules to Project Root  (100)
    ///   Tools/Axiom/Remove Workspace Rules from Project Root (101)
    ///   Tools/Axiom/Check Optional Packages                  (102)
    ///   Tools/Axiom/Verify Installation                      (200)
    /// </summary>
    public static class AxiomInstaller
    {
        private const string WorkspaceRulesPath = "Assets/Axiom/Editor/WorkspaceRules";
        private const string MenuRoot = "Tools/Axiom/";

        private static readonly (string sourceName, string targetName, string description)[] RootFiles =
        {
            ("cursorrules",          ".cursorrules",           "Agent API reference (Cursor IDE)"),
            ("project_instructions", "project_instructions.md","Command schema + implementation template"),
        };

        /// <summary>
        /// All optional packages that unlock conditional Axiom features behind #if guards.
        /// Ordered by importance / likelihood of wanting to install.
        /// </summary>
        internal static readonly OptionalPackage[] OptionalPackages =
        {
            new OptionalPackage(
                "com.unity.nuget.newtonsoft-json", null,
                "AXIOM_HAS_NEWTONSOFT",
                "Newtonsoft.Json",
                "Full JSON schema parsing in the gateway. Strongly recommended — enables richer command routing."),

            new OptionalPackage(
                "com.unity.inputsystem", null,
                "AXIOM_HAS_INPUT_SYSTEM",
                "Input System",
                "InputSimulationActions — simulate keyboard, mouse, and gamepad input in Play Mode."),

            new OptionalPackage(
                "com.unity.multiplayer.playmode", null,
                "AXIOM_HAS_MPPM",
                "Multiplayer Play Mode",
                "MultiplayerActions — configure and test multi-player virtual players via MPPM 2.0."),

            new OptionalPackage(
                "com.unity.ai.inference", "com.unity.sentis",
                "AXIOM_HAS_SENTIS",
                "AI Inference (formerly Sentis)",
                "SentisActions — run ONNX ML models from editor scripts. (Note: asmdef checks com.unity.sentis; update the version define if AXIOM_HAS_SENTIS does not fire.)"),

            new OptionalPackage(
                "com.unity.ai.assistant", null,
                "AXIOM_HAS_UNITY_ASSISTANT",
                "Unity AI Assistant",
                "Native Unity MCP bridge — registers Axiom_Gateway, Axiom_Status, Axiom_ReadReport, Axiom_Verify, Axiom_Rules as MCP tools visible to Cursor / Claude Code / Windsurf."),
        };

        // ── Package installation queue ────────────────────────────────────

        private static readonly Queue<string> s_PackageQueue = new Queue<string>();
        private static AddRequest s_AddRequest;
        private static int s_TotalToInstall;
        private static int s_InstalledCount;

        // ── Workspace rules ───────────────────────────────────────────────

        [MenuItem(MenuRoot + "Install Workspace Rules to Project Root", false, 100)]
        public static void InstallWorkspaceRules()
        {
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            int installed = 0;
            int skipped = 0;

            foreach (var (sourceName, targetName, description) in RootFiles)
            {
                string sourcePath = $"{WorkspaceRulesPath}/{sourceName}.txt";
                string targetPath = Path.Combine(projectRoot, targetName);

                TextAsset asset = AssetDatabase.LoadAssetAtPath<TextAsset>(sourcePath);
                if (asset == null)
                {
                    Debug.LogWarning($"[Axiom] Embedded file not found: {sourcePath}");
                    continue;
                }

                if (File.Exists(targetPath))
                {
                    bool overwrite = EditorUtility.DisplayDialog(
                        "Axiom — File Exists",
                        $"'{targetName}' already exists at project root.\n" +
                        $"({description})\n\nOverwrite with embedded version?",
                        "Overwrite", "Skip");

                    if (!overwrite) { skipped++; continue; }
                }

                File.WriteAllText(targetPath, asset.text);
                installed++;
                Debug.Log($"[Axiom] Installed '{targetName}' → {projectRoot}");
            }

            EditorUtility.DisplayDialog("Axiom — Install Complete",
                $"Workspace rules installed.\n\n" +
                $"  Installed: {installed}\n" +
                $"  Skipped:   {skipped}\n\n" +
                $"Location: {projectRoot}",
                "OK");
        }

        [MenuItem(MenuRoot + "Remove Workspace Rules from Project Root", false, 101)]
        public static void RemoveWorkspaceRules()
        {
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            int removed = 0;

            foreach (var (_, targetName, _) in RootFiles)
            {
                string targetPath = Path.Combine(projectRoot, targetName);
                if (File.Exists(targetPath))
                {
                    File.Delete(targetPath);
                    removed++;
                    Debug.Log($"[Axiom] Removed '{targetName}' from project root.");
                }
            }

            EditorUtility.DisplayDialog("Axiom — Removal Complete",
                $"Removed {removed} workspace rule file(s) from project root.", "OK");
        }

        // ── Optional packages ─────────────────────────────────────────────

        [MenuItem(MenuRoot + "Check Optional Packages", false, 102)]
        public static void CheckOptionalPackages()
        {
            var missing = new List<OptionalPackage>();
            foreach (var pkg in OptionalPackages)
            {
                if (!pkg.IsInstalled())
                    missing.Add(pkg);
            }

            if (missing.Count == 0)
            {
                EditorUtility.DisplayDialog(
                    "Axiom — Optional Packages",
                    "All optional packages are already installed.\n\n" +
                    BuildInstalledSummary(),
                    "OK");
                return;
            }

            // Summary dialog with three choices
            string summaryLines = "";
            foreach (var pkg in missing)
                summaryLines += $"  • {pkg.FriendlyName}  ({pkg.PackageId})\n";

            int choice = EditorUtility.DisplayDialogComplex(
                "Axiom — Optional Packages",
                $"{missing.Count} optional package(s) are not installed:\n\n" +
                summaryLines + "\n" +
                "Each unlocks specific Axiom features (see feature list below).\n" +
                "Choose how to proceed:",
                "Review Each",   // 0
                "Cancel",        // 1
                "Install All");  // 2

            if (choice == 1) return;

            var toInstall = new List<string>();

            if (choice == 2)
            {
                // Install all without prompting
                foreach (var pkg in missing)
                    toInstall.Add(pkg.PackageId);
            }
            else
            {
                // Review each one by one
                bool installAllRemaining = false;
                for (int i = 0; i < missing.Count; i++)
                {
                    var pkg = missing[i];

                    if (installAllRemaining)
                    {
                        toInstall.Add(pkg.PackageId);
                        continue;
                    }

                    int remaining = missing.Count - i;
                    string altButton = remaining > 1
                        ? $"Install All {remaining} Remaining"
                        : "";   // empty = no third button shown

                    int result = EditorUtility.DisplayDialogComplex(
                        $"Axiom — Optional Package  ({i + 1} / {missing.Count})",
                        $"Install  {pkg.FriendlyName}?\n\n" +
                        $"Package:  {pkg.PackageId}\n\n" +
                        $"Enables:  {pkg.FeatureDescription}" +
                        (remaining > 1 ? $"\n\n{remaining - 1} more package(s) after this." : ""),
                        "Install",   // 0
                        "Skip",      // 1
                        altButton);  // 2 — only shown when remaining > 1

                    if (result == 0)
                    {
                        toInstall.Add(pkg.PackageId);
                    }
                    else if (result == 1)
                    {
                        // skip
                    }
                    else // Install All Remaining
                    {
                        toInstall.Add(pkg.PackageId);
                        installAllRemaining = true;
                    }
                }
            }

            if (toInstall.Count == 0)
            {
                EditorUtility.DisplayDialog("Axiom — No Changes",
                    "No packages were selected for installation.", "OK");
                return;
            }

            StartPackageInstallation(toInstall);
        }

        // ── Verify installation ───────────────────────────────────────────

        [MenuItem(MenuRoot + "Verify Installation", false, 200)]
        public static void VerifyInstallation()
        {
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            string report = "Axiom Installation Status\n=========================\n\n";

            // Root workspace files
            report += "Root Workspace Files:\n";
            foreach (var (_, targetName, description) in RootFiles)
            {
                string targetPath = Path.Combine(projectRoot, targetName);
                bool exists = File.Exists(targetPath);
                report += $"  [{(exists ? "+" : "X")}] {targetName}";
                report += exists ? " — OK" : " — MISSING (run Install Workspace Rules)";
                report += $"  ({description})\n";
            }

            // AgentBridge source folders
            report += "\nAgentBridge Source:\n";
            var folders = new[]
            {
                ("Assets/Axiom/Editor/AgentBridge/Core",        ""),
                ("Assets/Axiom/Editor/AgentBridge/Diagnostics", ""),
                ("Assets/Axiom/Editor/AgentBridge/Actions",     ""),
                ("Assets/Axiom/Editor/AgentBridge/Mcp",         " (compiled only with com.unity.ai.assistant)"),
            };
            int totalScripts = 0;
            foreach (var (folder, note) in folders)
            {
                bool exists = AssetDatabase.IsValidFolder(folder);
                int count = 0;
                if (exists)
                {
                    count = AssetDatabase.FindAssets("t:MonoScript", new[] { folder }).Length;
                    totalScripts += count;
                }
                report += $"  [{(exists ? "+" : "X")}] {folder}";
                report += exists ? $" — {count} scripts{note}" : " — MISSING";
                report += "\n";
            }
            report += $"  Total: {totalScripts} source files" +
                      $"  (expected 44 base + 2 Mcp = 46 when ai.assistant installed)\n";

            // Assembly definition
            string asmdefPath = "Assets/Axiom/Editor/AgentBridge/AgentBridge.asmdef";
            bool asmdef = File.Exists(Path.Combine(projectRoot, asmdefPath));
            report += $"\nAssembly Definition:\n  [{(asmdef ? "+" : "X")}] {asmdefPath}\n";

            // Report output directory
            string reportsDir = Path.Combine(projectRoot, "AgentReports");
            bool reportsExist = Directory.Exists(reportsDir);
            report += $"\nReport Output:\n  [{(reportsExist ? "+" : "-")}] AgentReports/";
            report += reportsExist ? " — exists\n" : " — auto-created on first diagnostic run\n";

            // Optional packages
            report += "\nOptional Packages:\n";
            foreach (var pkg in OptionalPackages)
            {
                bool installed = pkg.IsInstalled();
                string status = installed ? "ENABLED" : "not installed";
                string altNote = !string.IsNullOrEmpty(pkg.AltPackageId)
                    ? $" (or {pkg.AltPackageId})" : "";
                report += $"  [{(installed ? "+" : "-")}] {pkg.FriendlyName}\n";
                report += $"       {pkg.PackageId}{altNote} — {status}\n";
                if (!installed)
                    report += $"       → Install to enable: {pkg.FeatureDescription}\n";
            }

            Debug.Log($"[Axiom] Installation Verification:\n{report}");
            EditorUtility.DisplayDialog("Axiom — Verification", report, "OK");
        }

        // ── Helpers ───────────────────────────────────────────────────────

        /// <summary>
        /// Checks whether a package ID appears in Packages/manifest.json dependencies.
        /// Uses a simple string search — sufficient for UPM package IDs which are unique reverse-DNS names.
        /// </summary>
        internal static bool IsPackageInstalled(string packageId)
        {
            if (string.IsNullOrEmpty(packageId)) return false;

            try
            {
                string manifestPath = Path.Combine(
                    Path.GetDirectoryName(Application.dataPath),
                    "Packages", "manifest.json");

                if (!File.Exists(manifestPath)) return false;
                string json = File.ReadAllText(manifestPath);
                // Match "com.example.package": to avoid false substring hits
                return json.Contains($"\"{packageId}\":");
            }
            catch
            {
                return false;
            }
        }

        private static string BuildInstalledSummary()
        {
            string s = "Installed features:\n";
            foreach (var pkg in OptionalPackages)
                s += $"  [+] {pkg.FriendlyName}\n";
            return s;
        }

        internal static void StartPackageInstallation(List<string> packageIds)
        {
            s_PackageQueue.Clear();
            foreach (var id in packageIds)
                s_PackageQueue.Enqueue(id);
            s_TotalToInstall = packageIds.Count;
            s_InstalledCount = 0;

            string list = string.Join("\n", packageIds.ConvertAll(p => $"  • {p}"));
            Debug.Log($"[Axiom Installer] Queuing {packageIds.Count} package(s):\n{list}");

            EditorUtility.DisplayDialog(
                "Axiom — Installing Packages",
                $"Starting installation of {packageIds.Count} package(s):\n\n{list}\n\n" +
                "Unity will recompile when each package is ready.\n" +
                "You can monitor progress in Window > Package Manager.",
                "OK");

            ProcessNextPackage();
        }

        private static void ProcessNextPackage()
        {
            if (s_PackageQueue.Count == 0)
            {
                if (s_TotalToInstall > 0)
                {
                    EditorUtility.DisplayDialog(
                        "Axiom — Packages Installed",
                        $"Successfully installed {s_InstalledCount} of {s_TotalToInstall} package(s).\n\n" +
                        "Run  Tools > Axiom > Verify Installation  to confirm all features are enabled.",
                        "OK");
                }
                return;
            }

            string packageId = s_PackageQueue.Dequeue();
            s_AddRequest = Client.Add(packageId);
            Debug.Log($"[Axiom Installer] Installing: {packageId}");
            EditorApplication.update += MonitorCurrentPackage;
        }

        private static void MonitorCurrentPackage()
        {
            if (s_AddRequest == null || !s_AddRequest.IsCompleted) return;

            EditorApplication.update -= MonitorCurrentPackage;
            var req = s_AddRequest;
            s_AddRequest = null;

            if (req.Status == StatusCode.Success)
            {
                s_InstalledCount++;
                Debug.Log($"[Axiom Installer] Installed: {req.Result.packageId}");
            }
            else
            {
                string err = req.Error?.message ?? "Unknown error";
                Debug.LogWarning($"[Axiom Installer] Failed to install: {err}");
                EditorUtility.DisplayDialog(
                    "Axiom — Package Install Warning",
                    $"A package installation returned an error:\n\n{err}\n\n" +
                    "Remaining packages in the queue will still be attempted.\n" +
                    "You can install failed packages manually via Window > Package Manager.",
                    "Continue");
            }

            ProcessNextPackage();
        }
    }
}
