using UnityEngine;
using UnityEditor;
using System.IO;
using System.IO.Compression;

namespace Axiom.Editor.Installer
{
    /// <summary>
    /// Exports Axiom as a distributable package.
    ///
    /// The core problem: .unitypackage only captures files under Assets/.
    /// Root-level files (.cursorrules, project_instructions.md) must be handled separately.
    ///
    /// Solution: Store copies as TextAssets in Assets/Axiom/Editor/WorkspaceRules/.
    /// On import, AxiomPostImportCheck prompts the user to deploy them to project root.
    ///
    /// Three export modes:
    /// 1. .unitypackage — root files travel as embedded TextAssets
    /// 2. .zip bundle — .unitypackage + standalone root files + install readme
    /// 3. Folder — for Git repos or manual distribution
    /// </summary>
    public static class AxiomExporter
    {
        private const string AxiomRoot = "Assets/Axiom";
        private const string MenuRoot = "Tools/Axiom/Export/";
        private const string DefaultExportName = "Axiom";

        private static readonly (string projectRootName, string embeddedName)[] RootFiles = new[]
        {
            (".cursorrules",            "cursorrules.txt"),
            ("project_instructions.md", "project_instructions.txt"),
        };

        // ── Sync root files INTO the package ──────────────────────

        [MenuItem(MenuRoot + "1. Sync Root Files into Package", false, 50)]
        public static void SyncRootFilesIntoPackage()
        {
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            string rulesDir = Path.Combine(Application.dataPath, "Axiom", "Editor", "WorkspaceRules");

            if (!Directory.Exists(rulesDir))
                Directory.CreateDirectory(rulesDir);

            int synced = 0;
            foreach (var (rootName, embeddedName) in RootFiles)
            {
                string src = Path.Combine(projectRoot, rootName);
                string dst = Path.Combine(rulesDir, embeddedName);

                if (!File.Exists(src))
                {
                    Debug.LogWarning($"[Axiom Export] Root file not found: {src}");
                    continue;
                }

                File.Copy(src, dst, overwrite: true);
                synced++;
                Debug.Log($"[Axiom Export] Synced '{rootName}' → WorkspaceRules/{embeddedName}");
            }

            AssetDatabase.Refresh();
            EditorUtility.DisplayDialog("Axiom — Sync Complete",
                $"Synced {synced} root file(s) into Assets/Axiom/Editor/WorkspaceRules/.\n" +
                "These will now travel inside the .unitypackage.", "OK");
        }

        // ── Export .unitypackage ──────────────────────────────────

        [MenuItem(MenuRoot + "2a. Export .unitypackage", false, 60)]
        public static void ExportUnityPackage()
        {
            SyncRootFilesIntoPackage();

            string savePath = EditorUtility.SaveFilePanel(
                "Export Axiom Package", "",
                $"{DefaultExportName}.unitypackage", "unitypackage");

            if (string.IsNullOrEmpty(savePath)) return;

            AssetDatabase.ExportPackage(AxiomRoot, savePath,
                ExportPackageOptions.Recurse | ExportPackageOptions.IncludeDependencies);

            Debug.Log($"[Axiom Export] Package saved: {savePath}");
            EditorUtility.DisplayDialog("Axiom — Export Complete",
                $"Exported to:\n{savePath}\n\n" +
                "After importing in a new project, run:\n" +
                "  Tools > Axiom > Install Workspace Rules\n" +
                "to deploy .cursorrules and project_instructions.md to project root.", "OK");
        }

        // ── Export .zip bundle ────────────────────────────────────

        [MenuItem(MenuRoot + "2b. Export .zip Bundle (Recommended)", false, 61)]
        public static void ExportZipBundle()
        {
            SyncRootFilesIntoPackage();

            string savePath = EditorUtility.SaveFilePanel(
                "Export Axiom Bundle", "",
                $"{DefaultExportName}_Bundle.zip", "zip");

            if (string.IsNullOrEmpty(savePath)) return;

            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            string tempDir = Path.Combine(projectRoot, "Temp", "AxiomExport");

            try
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
                Directory.CreateDirectory(tempDir);

                // Export .unitypackage to temp
                string pkgPath = Path.Combine(tempDir, $"{DefaultExportName}.unitypackage");
                AssetDatabase.ExportPackage(AxiomRoot, pkgPath,
                    ExportPackageOptions.Recurse | ExportPackageOptions.IncludeDependencies);

                // Copy root files to temp
                foreach (var (rootName, _) in RootFiles)
                {
                    string src = Path.Combine(projectRoot, rootName);
                    if (File.Exists(src))
                        File.Copy(src, Path.Combine(tempDir, rootName), overwrite: true);
                }

                // Write install readme
                File.WriteAllText(Path.Combine(tempDir, "README_INSTALL.md"), GetInstallReadme());

                // Create zip
                if (File.Exists(savePath))
                    File.Delete(savePath);
                ZipFile.CreateFromDirectory(tempDir, savePath);

                Debug.Log($"[Axiom Export] Bundle saved: {savePath}");
                EditorUtility.DisplayDialog("Axiom — Bundle Complete",
                    $"Bundle saved to:\n{savePath}\n\n" +
                    "Contents:\n" +
                    $"  {DefaultExportName}.unitypackage\n" +
                    "  .cursorrules\n" +
                    "  project_instructions.md\n" +
                    "  README_INSTALL.md", "OK");
            }
            finally
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
            }
        }

        // ── Export to folder (Git) ────────────────────────────────

        [MenuItem(MenuRoot + "2c. Export to Folder (Git-Ready)", false, 62)]
        public static void ExportToFolder()
        {
            string folderPath = EditorUtility.SaveFolderPanel(
                "Export Axiom to Folder", "", DefaultExportName);

            if (string.IsNullOrEmpty(folderPath)) return;

            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            string axiomSrc = Path.Combine(Application.dataPath, "Axiom");

            // Copy Assets/Axiom/
            CopyDirectory(axiomSrc, Path.Combine(folderPath, "Assets", "Axiom"));

            // Copy root files
            foreach (var (rootName, _) in RootFiles)
            {
                string src = Path.Combine(projectRoot, rootName);
                if (File.Exists(src))
                    File.Copy(src, Path.Combine(folderPath, rootName), overwrite: true);
            }

            File.WriteAllText(Path.Combine(folderPath, "README_INSTALL.md"), GetInstallReadme());

            Debug.Log($"[Axiom Export] Folder export: {folderPath}");
            EditorUtility.DisplayDialog("Axiom — Folder Export Complete",
                $"Exported to:\n{folderPath}\n\nSee README_INSTALL.md for instructions.", "OK");
        }

        // ── Helpers ───────────────────────────────────────────────

        private static string GetInstallReadme()
        {
            return @"# Axiom — Installation Guide

## Quick Install (from .unitypackage)

1. Open your Unity 6 (6000.3 LTS) project.
2. Double-click `Axiom.unitypackage` or use Assets > Import Package > Custom Package.
3. Import all files.
4. A dialog will prompt you to install workspace rules — click ""Install Now"".
   (Or manually: Tools > Axiom > Install Workspace Rules to Project Root)
5. Optional: Tools > Axiom > Check Optional Packages — install feature-unlocking packages.
6. Verify: Tools > Axiom > Verify Installation.

## Manual Install (from folder/zip/Git)

1. Copy `Assets/Axiom/` into your project's `Assets/` directory.
2. Copy `.cursorrules` to your project root (same level as `Assets/`).
3. Copy `project_instructions.md` to your project root.
4. Return to Unity and let it compile.
5. Optional: Tools > Axiom > Check Optional Packages.
6. Verify: Tools > Axiom > Verify Installation.

## What Gets Installed

```
YourProject/
├── .cursorrules                    ← Agent API reference (Cursor IDE — auto-loaded)
├── project_instructions.md         ← Command schema + templates (all other agents)
├── AgentReports/                   ← Diagnostic output (auto-created on first run)
├── Assets/
│   └── Axiom/
│       └── Editor/
│           ├── AgentBridge/
│           │   ├── Core/           ← 8 shared utilities (OutputWriter, Gateway, Parser, ...)
│           │   ├── Diagnostics/    ← 20 diagnostic tools (HierarchyLens → CISweep)
│           │   ├── Actions/        ← 16 action tools (SceneActions → SentisActions)
│           │   ├── Mcp/            ← 2 Unity MCP tools (compiled only with ai.assistant)
│           │   ├── Temp/           ← Temp scripts directory (agents write here)
│           │   └── AgentBridge.asmdef
│           ├── WorkspaceRules/     ← Embedded copies of root files (for re-install)
│           └── Installer/          ← This installer + exporter (3 scripts)
```

44 base source files + 2 Mcp files (when ai.assistant installed) = 46 total + 1 asmdef.

## File Roles

- `.cursorrules` — Cursor IDE auto-loads this. Contains core agent rules + full tool API reference.
- `project_instructions.md` — For Claude, Gemini, or any non-Cursor agent. Contains the JSON
  command gateway schema, Token-Lean Implementation Template, and architectural context.
- Both files serve the same purpose for their respective consumers.

## Optional Dependencies

Install via  Tools > Axiom > Check Optional Packages  or  Window > Package Manager.

| Package                          | Enables                                               |
|:---------------------------------|:------------------------------------------------------|
| com.unity.nuget.newtonsoft-json  | Full JSON gateway parsing — strongly recommended      |
| com.unity.inputsystem            | InputSimulationActions — keyboard/mouse/gamepad sim   |
| com.unity.multiplayer.playmode   | MultiplayerActions — MPPM 2.0 virtual player control  |
| com.unity.ai.inference           | SentisActions — ONNX ML model execution in editor     |
| com.unity.ai.assistant           | Native Unity MCP tools (Axiom_Gateway, Axiom_Status,  |
|                                  |   Axiom_ReadReport, Axiom_Verify, Axiom_Rules)        |

All optional features are behind #if guards — Axiom compiles cleanly without any of them.
";
        }

        private static void CopyDirectory(string source, string target)
        {
            Directory.CreateDirectory(target);
            foreach (string file in Directory.GetFiles(source))
                File.Copy(file, Path.Combine(target, Path.GetFileName(file)), overwrite: true);
            foreach (string dir in Directory.GetDirectories(source))
                CopyDirectory(dir, Path.Combine(target, Path.GetFileName(dir)));
        }
    }
}
