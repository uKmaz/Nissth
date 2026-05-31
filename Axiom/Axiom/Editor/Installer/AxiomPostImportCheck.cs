using UnityEditor;
using UnityEngine;
using System.IO;
using System.Collections.Generic;

namespace Axiom.Editor.Installer
{
    /// <summary>
    /// Runs once per editor session via [InitializeOnLoad].
    ///
    /// Pass 1 — Workspace rules:
    ///   If .cursorrules or project_instructions.md are missing from project root,
    ///   prompts the user to install them from the embedded WorkspaceRules/ TextAssets.
    ///
    /// Pass 2 — Optional packages (runs only after workspace rules are confirmed present):
    ///   Checks which optional Axiom packages are not installed and offers to install them.
    ///   Shows per-package dialogs with Install / Skip / Install All Remaining buttons.
    ///   Only prompts once per editor session.
    /// </summary>
    [InitializeOnLoad]
    public static class AxiomPostImportCheck
    {
        private const string WorkspaceCheckedKey  = "Axiom_WorkspaceRulesChecked";
        private const string PackagesCheckedKey   = "Axiom_OptionalPackagesChecked";

        static AxiomPostImportCheck()
        {
            if (SessionState.GetBool(WorkspaceCheckedKey, false))
                return;

            SessionState.SetBool(WorkspaceCheckedKey, true);
            EditorApplication.delayCall += RunStartupChecks;
        }

        private static void RunStartupChecks()
        {
            bool workspaceReady = CheckWorkspaceRules();

            // Only prompt for optional packages after workspace rules are confirmed
            if (workspaceReady && !SessionState.GetBool(PackagesCheckedKey, false))
            {
                SessionState.SetBool(PackagesCheckedKey, true);
                CheckOptionalPackagesOnce();
            }
        }

        // ── Pass 1: workspace rules ───────────────────────────────────────

        /// <returns>True when workspace rules are present (either already or just installed).</returns>
        private static bool CheckWorkspaceRules()
        {
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            bool hasCursorRules   = File.Exists(Path.Combine(projectRoot, ".cursorrules"));
            bool hasInstructions  = File.Exists(Path.Combine(projectRoot, "project_instructions.md"));

            if (hasCursorRules && hasInstructions)
                return true;

            string missing = "";
            if (!hasCursorRules)  missing += "  • .cursorrules  (Cursor IDE agent reference)\n";
            if (!hasInstructions) missing += "  • project_instructions.md  (command schema + templates)\n";

            bool install = EditorUtility.DisplayDialog(
                "Axiom — Workspace Rules Not Found",
                "The following workspace rule files are missing from project root:\n\n" +
                missing + "\n" +
                "These files are required for AI agents to operate correctly.\n" +
                "Install them now?",
                "Install Now",
                "Later  (Tools > Axiom > Install Workspace Rules)");

            if (install)
                AxiomInstaller.InstallWorkspaceRules();

            // Return true if install was accepted — assume success
            return install;
        }

        // ── Pass 2: optional packages ─────────────────────────────────────

        private static void CheckOptionalPackagesOnce()
        {
            var missing = new List<OptionalPackage>();
            foreach (var pkg in AxiomInstaller.OptionalPackages)
            {
                if (!pkg.IsInstalled())
                    missing.Add(pkg);
            }

            if (missing.Count == 0)
                return;  // all packages present — nothing to do

            // Intro dialog listing what's missing
            string summaryLines = "";
            foreach (var pkg in missing)
                summaryLines += $"  • {pkg.FriendlyName}  ({pkg.PackageId})\n";

            int choice = EditorUtility.DisplayDialogComplex(
                "Axiom — Optional Packages",
                $"Axiom has {missing.Count} optional package(s) that are not yet installed.\n\n" +
                summaryLines + "\n" +
                "Installing them enables additional Axiom features.\n" +
                "You can also do this later via  Tools > Axiom > Check Optional Packages.",
                "Review Each",   // 0
                "Later",         // 1
                "Install All");  // 2

            if (choice == 1)
                return;

            var toInstall = new List<string>();

            if (choice == 2)
            {
                foreach (var pkg in missing)
                    toInstall.Add(pkg.PackageId);
                TriggerInstallation(toInstall);
                return;
            }

            // Review each package one by one
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
                string altButton = remaining > 1 ? $"Install All {remaining} Remaining" : "";

                int result = EditorUtility.DisplayDialogComplex(
                    $"Axiom — Optional Package  ({i + 1} / {missing.Count})",
                    $"Install  {pkg.FriendlyName}?\n\n" +
                    $"Package:  {pkg.PackageId}\n\n" +
                    $"Enables:  {pkg.FeatureDescription}" +
                    (remaining > 1 ? $"\n\n{remaining - 1} more package(s) after this." : ""),
                    "Install",   // 0
                    "Skip",      // 1
                    altButton);  // 2 — hidden when altButton is ""

                if (result == 0)
                {
                    toInstall.Add(pkg.PackageId);
                }
                else if (result == 2)
                {
                    toInstall.Add(pkg.PackageId);
                    installAllRemaining = true;
                }
                // result == 1 → skip, do nothing
            }

            if (toInstall.Count > 0)
                TriggerInstallation(toInstall);
        }

        // Delegates actual installation to AxiomInstaller to keep the queue logic centralised.
        private static void TriggerInstallation(List<string> packageIds)
        {
            AxiomInstaller.StartPackageInstallation(packageIds);
        }
    }
}
