using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEditor;
using Axiom.Editor.AgentBridge.Core;

namespace Axiom.Editor.AgentBridge.Diagnostics
{
    public enum CartographerMode
    {
        FileTree,        // Mode A: directory structure with file names and extensions
        FileManifest,    // Mode B: file tree + sizes + last modified dates
        DependencyMap,   // Mode C: what references X, or what X depends on
        OrphanSearch,    // Mode D: assets not referenced by any scene or prefab in the build
        GuidRegistry,    // Mode E: asset paths with their GUIDs
        ImportSettings,  // Mode F: asset import settings (texture, model, audio)
        TypeCensus       // Mode G: count assets by type, grouped by folder
    }

    public enum DependencyDirection
    {
        DependsOn,       // "What does this asset depend on?" (forward)
        ReferencedBy     // "What references this asset?" (reverse)
    }

    /// <summary>
    /// Generates asset project reports: file tree, file manifest, dependency maps, orphan detection, and GUID registry.
    /// Replaces expensive MCP folder browsing.
    /// </summary>
    public static class ProjectCartographer
    {
        private static readonly string ProjectRoot = Path.GetDirectoryName(Application.dataPath);

        // ─────────────────────────────────────────────────────
        //  Public Entry Point
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// Generates a project asset report.
        /// </summary>
        /// <param name="mode">Detail level.</param>
        /// <param name="assetPath">Folder or file path relative to project (e.g., "Assets/Scripts/Player"). Null defaults to "Assets".</param>
        /// <param name="extension">Filter by file extension including the dot (e.g., ".cs", ".mat", ".prefab"). Null = all extensions.</param>
        /// <param name="maxDepth">Folder recursion depth. -1 = unlimited. 0 = specified folder only (no subfolders).</param>
        /// <param name="namePattern">Regex pattern for asset name matching. Null = no filtering.</param>
        /// <param name="sizeThreshold">For Mode B: only report assets larger than this many bytes. 0 = all.</param>
        /// <param name="excludePackages">Skip the Packages/ folder. Default true.</param>
        /// <param name="dependencyTarget">For Mode C: the asset path to trace dependencies for.</param>
        /// <param name="dependencyDirection">For Mode C: DependsOn or ReferencedBy.</param>
        /// <returns>File path of the generated report.</returns>
        public static string GenerateReport(
            CartographerMode mode,
            string assetPath = null,
            string extension = null,
            int maxDepth = -1,
            string namePattern = null,
            long sizeThreshold = 0,
            bool excludePackages = true,
            string dependencyTarget = null,
            DependencyDirection dependencyDirection = DependencyDirection.DependsOn)
        {
            string content;
            switch (mode)
            {
                case CartographerMode.FileTree:
                    content = BuildFileTree(assetPath ?? "Assets", extension, maxDepth, namePattern, excludePackages);
                    break;
                case CartographerMode.FileManifest:
                    content = BuildFileManifest(assetPath ?? "Assets", extension, maxDepth, namePattern, sizeThreshold, excludePackages);
                    break;
                case CartographerMode.DependencyMap:
                    content = BuildDependencyMap(dependencyTarget, dependencyDirection, assetPath, excludePackages);
                    break;
                case CartographerMode.OrphanSearch:
                    content = BuildOrphanSearch(assetPath, extension, excludePackages);
                    break;
                case CartographerMode.GuidRegistry:
                    content = BuildGuidRegistry(assetPath ?? "Assets", extension, namePattern, excludePackages);
                    break;
                case CartographerMode.ImportSettings:
                    content = BuildImportSettings(assetPath ?? "Assets", extension, excludePackages);
                    break;
                case CartographerMode.TypeCensus:
                    content = BuildTypeCensus(assetPath ?? "Assets", excludePackages);
                    break;
                default:
                    content = $"# Project Cartographer — Unknown Mode\n\nMode {mode} is not implemented.";
                    break;
            }

            return OutputWriter.WriteReport("project_cartographer", content);
        }

        // ─────────────────────────────────────────────────────
        //  Mode A: File Tree
        // ─────────────────────────────────────────────────────

        private static string BuildFileTree(string assetPath, string extension, int maxDepth, string namePattern, bool excludePackages)
        {
            string depthLabel = maxDepth < 0 ? "Unlimited" : maxDepth.ToString();
            string extLabel = extension ?? "ALL";

            var sb = new StringBuilder();
            sb.AppendLine($"# Project Cartographer — Mode: File Tree | Path: {assetPath} | Depth: {depthLabel} | Extension: {extLabel}");
            sb.AppendLine();

            int totalFiles = 0;
            int totalFolders = 0;

            string fullRoot = ToFullPath(assetPath);
            if (!Directory.Exists(fullRoot))
            {
                sb.AppendLine($"ERROR: Path not found: {assetPath}");
            }
            else
            {
                sb.AppendLine($"{assetPath}/");
                WalkDirectoryTree(fullRoot, assetPath, 1, maxDepth, extension, namePattern,
                    sb, ref totalFiles, ref totalFolders, excludePackages);
            }

            sb.AppendLine();
            sb.AppendLine("---");
            sb.Append($"Total files: {totalFiles} | Total folders: {totalFolders} | Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            return sb.ToString();
        }

        private static void WalkDirectoryTree(
            string fullDir, string assetDir, int depth, int maxDepth,
            string extension, string namePattern,
            StringBuilder sb, ref int totalFiles, ref int totalFolders, bool excludePackages = false)
        {
            string indent = new string(' ', depth * 2);

            // Subdirectories first, sorted alphabetically
            string[] subDirs;
            try { subDirs = Directory.GetDirectories(fullDir); }
            catch { return; }

            Array.Sort(subDirs, StringComparer.OrdinalIgnoreCase);

            foreach (string subDir in subDirs)
            {
                string dirName = Path.GetFileName(subDir);
                if (dirName.StartsWith(".")) continue; // skip hidden

                string subAssetDir = assetDir + "/" + dirName;
                if (excludePackages && subAssetDir.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase)) continue;
                sb.AppendLine($"{indent}{dirName}/");
                totalFolders++;

                if (maxDepth < 0 || depth < maxDepth)
                {
                    WalkDirectoryTree(subDir, subAssetDir, depth + 1, maxDepth,
                        extension, namePattern, sb, ref totalFiles, ref totalFolders, excludePackages);
                }
            }

            // Files next, sorted alphabetically
            string[] files;
            try { files = Directory.GetFiles(fullDir); }
            catch { return; }

            Array.Sort(files, StringComparer.OrdinalIgnoreCase);

            foreach (string file in files)
            {
                string fileName = Path.GetFileName(file);

                if (fileName.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) continue;

                if (extension != null && !fileName.EndsWith(extension, StringComparison.OrdinalIgnoreCase)) continue;

                if (namePattern != null && !Regex.IsMatch(fileName, namePattern)) continue;

                sb.AppendLine($"{indent}{fileName}");
                totalFiles++;
            }
        }

        // ─────────────────────────────────────────────────────
        //  Mode B: File Manifest
        // ─────────────────────────────────────────────────────

        private static string BuildFileManifest(
            string assetPath, string extension, int maxDepth, string namePattern, long sizeThreshold, bool excludePackages)
        {
            string sizeLabel = FormatFileSize(sizeThreshold);

            var sb = new StringBuilder();
            sb.AppendLine($"# Project Cartographer — Mode: File Manifest | Path: {assetPath} | Size \u2265 {sizeLabel}");
            sb.AppendLine();

            string fullRoot = ToFullPath(assetPath);
            if (!Directory.Exists(fullRoot))
            {
                sb.AppendLine($"ERROR: Path not found: {assetPath}");
                return sb.ToString();
            }

            var entries = new List<(string assetRelPath, long size, DateTime lastModified)>();
            CollectFilesForManifest(fullRoot, assetPath, 0, maxDepth, extension, namePattern, sizeThreshold, entries, excludePackages);

            // Sort by size descending
            entries.Sort((a, b) => b.size.CompareTo(a.size));

            sb.AppendLine("| File Path | Size | Last Modified |");
            sb.AppendLine("| :--- | :--- | :--- |");

            long totalSize = 0;
            foreach (var (relPath, size, modified) in entries)
            {
                sb.AppendLine($"| {relPath} | {FormatFileSize(size)} | {modified:yyyy-MM-dd HH:mm:ss} |");
                totalSize += size;
            }

            sb.AppendLine();
            sb.AppendLine("---");
            sb.Append($"Total files: {entries.Count} | Total size: {FormatFileSize(totalSize)} | Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            return sb.ToString();
        }

        private static void CollectFilesForManifest(
            string fullDir, string assetDir, int depth, int maxDepth,
            string extension, string namePattern, long sizeThreshold,
            List<(string, long, DateTime)> entries, bool excludePackages = false)
        {
            // Recurse into subdirectories
            if (maxDepth < 0 || depth < maxDepth)
            {
                string[] subDirs;
                try { subDirs = Directory.GetDirectories(fullDir); }
                catch { return; }

                foreach (string subDir in subDirs)
                {
                    string dirName = Path.GetFileName(subDir);
                    if (dirName.StartsWith(".")) continue;
                    string subAssetDir = assetDir + "/" + dirName;
                    if (excludePackages && subAssetDir.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase)) continue;
                    CollectFilesForManifest(subDir, subAssetDir, depth + 1, maxDepth,
                        extension, namePattern, sizeThreshold, entries, excludePackages);
                }
            }

            // Files in this directory
            string[] files;
            try { files = Directory.GetFiles(fullDir); }
            catch { return; }

            foreach (string file in files)
            {
                string fileName = Path.GetFileName(file);

                if (fileName.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) continue;
                if (extension != null && !fileName.EndsWith(extension, StringComparison.OrdinalIgnoreCase)) continue;
                if (namePattern != null && !Regex.IsMatch(fileName, namePattern)) continue;

                var info = new FileInfo(file);
                if (sizeThreshold > 0 && info.Length < sizeThreshold) continue;

                string relPath = assetDir + "/" + fileName;
                entries.Add((relPath, info.Length, info.LastWriteTime));
            }
        }

        // ─────────────────────────────────────────────────────
        //  Mode C: Dependency Map
        // ─────────────────────────────────────────────────────

        private static string BuildDependencyMap(string target, DependencyDirection direction, string scopePath, bool excludePackages)
        {
            if (string.IsNullOrEmpty(target))
            {
                return "# Project Cartographer — Mode: Dependency Map\n\nERROR: dependencyTarget is required for Dependency Map mode.";
            }

            var sb = new StringBuilder();

            if (direction == DependencyDirection.DependsOn)
            {
                sb.AppendLine($"# Project Cartographer — Mode: Dependency Map (DependsOn) | Target: {target}");
                sb.AppendLine();

                string[] deps = AssetDatabase.GetDependencies(target, recursive: true);
                var filtered = deps
                    .Where(d => d != target)
                    .Where(d => !excludePackages || !d.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(d => d).ToList();

                sb.AppendLine("## Direct & Transitive Dependencies");
                if (filtered.Count == 0)
                {
                    sb.AppendLine("*No dependencies found.*");
                }
                else
                {
                    sb.AppendLine("| # | Asset Path | Type |");
                    sb.AppendLine("| :--- | :--- | :--- |");
                    for (int i = 0; i < filtered.Count; i++)
                        sb.AppendLine($"| {i + 1} | {filtered[i]} | {GetAssetTypeName(filtered[i])} |");
                }

                sb.AppendLine();
                sb.AppendLine("---");
                sb.Append($"Dependencies: {filtered.Count} | Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            }
            else
            {
                sb.AppendLine($"# Project Cartographer — Mode: Dependency Map (ReferencedBy) | Target: {target}");
                sb.AppendLine();

                string searchFolder = scopePath ?? "Assets";
                string[] allGuids = AssetDatabase.FindAssets("", new[] { searchFolder });
                var referencers = new List<string>();

                try
                {
                    for (int i = 0; i < allGuids.Length; i++)
                    {
                        string candidatePath = AssetDatabase.GUIDToAssetPath(allGuids[i]);
                        if (candidatePath == target) continue;
                        if (AssetDatabase.IsValidFolder(candidatePath)) continue;
                        if (excludePackages && candidatePath.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase)) continue;

                        EditorUtility.DisplayProgressBar("Dependency Map (ReferencedBy)",
                            $"Checking {Path.GetFileName(candidatePath)}...",
                            (float)i / allGuids.Length);

                        string[] deps = AssetDatabase.GetDependencies(candidatePath, recursive: false);
                        if (Array.IndexOf(deps, target) >= 0)
                            referencers.Add(candidatePath);
                    }
                }
                finally
                {
                    EditorUtility.ClearProgressBar();
                }

                referencers.Sort(StringComparer.OrdinalIgnoreCase);

                sb.AppendLine("## Referenced By (direct references only)");
                if (referencers.Count == 0)
                {
                    sb.AppendLine("*No direct referencers found.*");
                }
                else
                {
                    sb.AppendLine("| # | Asset Path | Type |");
                    sb.AppendLine("| :--- | :--- | :--- |");
                    for (int i = 0; i < referencers.Count; i++)
                        sb.AppendLine($"| {i + 1} | {referencers[i]} | {GetAssetTypeName(referencers[i])} |");
                }

                sb.AppendLine();
                sb.AppendLine("---");
                sb.Append($"Referencers: {referencers.Count} | Scanned: {allGuids.Length} assets | Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            }

            return sb.ToString();
        }

        // ─────────────────────────────────────────────────────
        //  Mode D: Orphan Search
        // ─────────────────────────────────────────────────────

        private static string BuildOrphanSearch(string scopePath, string extension, bool excludePackages)
        {
            string extLabel = extension ?? "ALL";

            var sb = new StringBuilder();
            sb.AppendLine($"# Project Cartographer — Mode: Orphan Search | Path: {scopePath ?? "Assets"} | Extension: {extLabel}");
            sb.AppendLine();
            sb.AppendLine("> **WARNING:** Assets loaded via Resources.Load(), Addressables, or AssetBundles");
            sb.AppendLine("> may appear as orphans. Verify before deleting.");
            sb.AppendLine();

            // 1. Collect all enabled build scenes
            var buildScenes = EditorBuildSettings.scenes
                .Where(s => s.enabled)
                .Select(s => s.path)
                .ToArray();

            // 2. Collect ALL transitive dependencies of all build scenes
            var usedAssets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string scenePath in buildScenes)
            {
                usedAssets.Add(scenePath);
                string[] deps = AssetDatabase.GetDependencies(scenePath, recursive: true);
                foreach (string dep in deps)
                    usedAssets.Add(dep);
            }

            // 3. Build scenes list section
            sb.AppendLine("## Build Scenes");
            if (buildScenes.Length == 0)
            {
                sb.AppendLine("*No scenes in Build Settings.*");
            }
            else
            {
                sb.AppendLine("| Scene | Path |");
                sb.AppendLine("| :--- | :--- |");
                foreach (string s in buildScenes)
                    sb.AppendLine($"| {Path.GetFileNameWithoutExtension(s)} | {s} |");
            }
            sb.AppendLine();

            // 4. Find all assets and filter for orphans
            string searchFolder = scopePath ?? "Assets";
            string[] allGuids = AssetDatabase.FindAssets("", new[] { searchFolder });

            var orphans = new List<(string path, long size)>();

            foreach (string guid in allGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);

                if (AssetDatabase.IsValidFolder(path)) continue;
                if (path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) continue;
                if (path.EndsWith(".asmdef", StringComparison.OrdinalIgnoreCase)) continue;
                if (path.EndsWith(".asmref", StringComparison.OrdinalIgnoreCase)) continue;
                if (path.IndexOf("/Editor/", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                if (excludePackages && path.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase)) continue;

                if (extension != null && !path.EndsWith(extension, StringComparison.OrdinalIgnoreCase)) continue;

                if (!usedAssets.Contains(path))
                {
                    long size = 0;
                    string fullPath = ToFullPath(path);
                    if (File.Exists(fullPath))
                        size = new FileInfo(fullPath).Length;
                    orphans.Add((path, size));
                }
            }

            // Sort by size descending
            orphans.Sort((a, b) => b.size.CompareTo(a.size));

            // 5. Orphan list
            sb.AppendLine("## Orphaned Assets (not referenced by any build scene)");
            if (orphans.Count == 0)
            {
                sb.AppendLine("*No orphaned assets found.*");
            }
            else
            {
                sb.AppendLine("| # | Asset Path | Size | Type |");
                sb.AppendLine("| :--- | :--- | :--- | :--- |");
                for (int i = 0; i < orphans.Count; i++)
                    sb.AppendLine($"| {i + 1} | {orphans[i].path} | {FormatFileSize(orphans[i].size)} | {GetAssetTypeName(orphans[i].path)} |");
            }
            sb.AppendLine();

            long totalOrphanSize = orphans.Sum(o => o.size);
            sb.AppendLine("## Summary");
            sb.AppendLine($"- Orphaned assets: {orphans.Count}");
            sb.AppendLine($"- Total orphan size: {FormatFileSize(totalOrphanSize)}");
            sb.AppendLine($"- Total assets scanned: {allGuids.Length}");
            sb.AppendLine($"- Build scenes: {buildScenes.Length}");
            sb.AppendLine();
            sb.AppendLine("---");
            sb.Append($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            return sb.ToString();
        }

        // ─────────────────────────────────────────────────────
        //  Mode E: GUID Registry
        // ─────────────────────────────────────────────────────

        private static string BuildGuidRegistry(string assetPath, string extension, string namePattern, bool excludePackages)
        {
            string extLabel = extension ?? "ALL";

            var sb = new StringBuilder();
            sb.AppendLine($"# Project Cartographer — Mode: GUID Registry | Path: {assetPath} | Extension: {extLabel}");
            sb.AppendLine();

            string[] guids = AssetDatabase.FindAssets("", new[] { assetPath });

            var entries = new List<(string guid, string path)>();
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);

                if (AssetDatabase.IsValidFolder(path)) continue;
                if (path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) continue;
                if (excludePackages && path.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase)) continue;

                if (extension != null && !path.EndsWith(extension, StringComparison.OrdinalIgnoreCase)) continue;

                if (namePattern != null)
                {
                    string fileName = Path.GetFileName(path);
                    if (!Regex.IsMatch(fileName, namePattern)) continue;
                }

                entries.Add((guid, path));
            }

            entries.Sort((a, b) => string.Compare(a.path, b.path, StringComparison.OrdinalIgnoreCase));

            if (entries.Count == 0)
            {
                sb.AppendLine("*No assets found matching the criteria.*");
            }
            else
            {
                sb.AppendLine("| GUID | Asset Path |");
                sb.AppendLine("| :--- | :--- |");
                foreach (var (guid, path) in entries)
                    sb.AppendLine($"| {guid} | {path} |");
            }

            sb.AppendLine();
            sb.AppendLine("---");
            sb.Append($"Total assets: {entries.Count} | Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            return sb.ToString();
        }

        // ─────────────────────────────────────────────────────
        //  Helpers
        // ─────────────────────────────────────────────────────

        private static string ToFullPath(string assetPath)
        {
            return Path.Combine(ProjectRoot, assetPath.Replace('/', Path.DirectorySeparatorChar));
        }

        private static string FormatFileSize(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            return $"{bytes / (1024.0 * 1024.0):F1} MB";
        }

        private static string GetAssetTypeName(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            switch (ext)
            {
                case ".cs":         return "MonoScript";
                case ".prefab":     return "Prefab";
                case ".unity":      return "Scene";
                case ".mat":        return "Material";
                case ".asset":      return "ScriptableObject";
                case ".png":
                case ".jpg":
                case ".jpeg":
                case ".tga":
                case ".psd":
                case ".bmp":        return "Texture";
                case ".fbx":
                case ".obj":
                case ".dae":        return "Model";
                case ".anim":       return "AnimationClip";
                case ".controller": return "AnimatorController";
                case ".wav":
                case ".mp3":
                case ".ogg":        return "AudioClip";
                case ".shader":     return "Shader";
                case ".ttf":
                case ".otf":        return "Font";
                default:            return string.IsNullOrEmpty(ext) ? "Unknown" : ext;
            }
        }

        // ─────────────────────────────────────────────────────
        //  Editor Menu Items
        // ─────────────────────────────────────────────────────

        [MenuItem("Axiom/AgentBridge/Project Cartographer — Mode A (File Tree, Assets root)")]
        public static void MenuModeA()
        {
            string path = GenerateReport(CartographerMode.FileTree, maxDepth: 2);
            Debug.Log($"[AgentBridge] Project Cartographer report: {path}");
        }

        [MenuItem("Axiom/AgentBridge/Project Cartographer — Mode B (File Manifest, Assets root)")]
        public static void MenuModeB()
        {
            string path = GenerateReport(CartographerMode.FileManifest, maxDepth: 2);
            Debug.Log($"[AgentBridge] Project Cartographer report: {path}");
        }

        [MenuItem("Axiom/AgentBridge/Project Cartographer — Mode D (Orphan Search)")]
        public static void MenuModeD()
        {
            string path = GenerateReport(CartographerMode.OrphanSearch);
            Debug.Log($"[AgentBridge] Project Cartographer report: {path}");
        }

        [MenuItem("Axiom/AgentBridge/Project Cartographer — Mode E (GUID Registry, Assets root)")]
        public static void MenuModeE()
        {
            string path = GenerateReport(CartographerMode.GuidRegistry, maxDepth: 1);
            Debug.Log($"[AgentBridge] Project Cartographer report: {path}");
        }

        [MenuItem("Axiom/AgentBridge/Project Cartographer — Mode F (Import Settings, Assets root)")]
        public static void MenuModeF()
        {
            string path = GenerateReport(CartographerMode.ImportSettings, maxDepth: 2);
            Debug.Log($"[AgentBridge] Project Cartographer report: {path}");
        }

        [MenuItem("Axiom/AgentBridge/Project Cartographer — Mode G (Type Census)")]
        public static void MenuModeG()
        {
            string path = GenerateReport(CartographerMode.TypeCensus);
            Debug.Log($"[AgentBridge] Project Cartographer report: {path}");
        }

        // ─────────────────────────────────────────────────────
        //  Mode F: Import Settings
        // ─────────────────────────────────────────────────────

        private static string BuildImportSettings(string assetPath, string extension, bool excludePackages)
        {
            string pathLabel = assetPath;
            string extLabel = extension ?? "ALL";

            var textureRows = new List<(string path, string texType, int maxSize, string compression, string format, bool mipmaps, bool sRGB)>();
            var modelRows   = new List<(string path, float scale, bool anim, bool mats, string compression)>();
            var audioRows   = new List<(string path, string loadType, string comprFormat, float quality)>();
            var otherRows   = new List<(string path, string importerType)>();

            string[] guids = AssetDatabase.FindAssets("", new[] { assetPath });

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (AssetDatabase.IsValidFolder(path)) continue;
                if (excludePackages && path.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase)) continue;
                if (extension != null && !path.EndsWith(extension, StringComparison.OrdinalIgnoreCase)) continue;

                AssetImporter importer = AssetImporter.GetAtPath(path);
                if (importer == null) continue;

                if (importer is TextureImporter tex)
                {
                    textureRows.Add((path,
                        tex.textureType.ToString(),
                        tex.maxTextureSize,
                        tex.textureCompression.ToString(),
                        tex.GetDefaultPlatformTextureSettings().format.ToString(),
                        tex.mipmapEnabled,
                        tex.sRGBTexture));
                }
                else if (importer is ModelImporter model)
                {
                    modelRows.Add((path,
                        model.globalScale,
                        model.importAnimation,
                        model.materialImportMode != ModelImporterMaterialImportMode.None,
                        model.meshCompression.ToString()));
                }
                else if (importer is AudioImporter audio)
                {
                    var settings = audio.defaultSampleSettings;
                    audioRows.Add((path,
                        settings.loadType.ToString(),
                        settings.compressionFormat.ToString(),
                        settings.quality));
                }
                else
                {
                    otherRows.Add((path, importer.GetType().Name));
                }
            }

            int total = textureRows.Count + modelRows.Count + audioRows.Count + otherRows.Count;
            var sb = new StringBuilder();
            sb.AppendLine($"# Project Cartographer — Mode: Import Settings | Path: {pathLabel} | Extension: {extLabel}");
            sb.AppendLine();

            if (textureRows.Count > 0)
            {
                sb.AppendLine("## Texture Import Settings");
                sb.AppendLine("| Asset Path | Type | Max Size | Compression | Format | Mipmaps | sRGB |");
                sb.AppendLine("| :--- | :--- | :--- | :--- | :--- | :--- | :--- |");
                foreach (var (p, tt, ms, comp, fmt, mip, srgb) in textureRows)
                    sb.AppendLine($"| {p} | {tt} | {ms} | {comp} | {fmt} | {(mip ? "Yes" : "No")} | {(srgb ? "Yes" : "No")} |");
                sb.AppendLine();
            }

            if (modelRows.Count > 0)
            {
                sb.AppendLine("## Model Import Settings");
                sb.AppendLine("| Asset Path | Scale | Animation | Materials | Mesh Compression |");
                sb.AppendLine("| :--- | :--- | :--- | :--- | :--- |");
                foreach (var (p, scale, anim, mats, comp) in modelRows)
                    sb.AppendLine($"| {p} | {scale:F3} | {anim} | {mats} | {comp} |");
                sb.AppendLine();
            }

            if (audioRows.Count > 0)
            {
                sb.AppendLine("## Audio Import Settings");
                sb.AppendLine("| Asset Path | Load Type | Compression | Quality |");
                sb.AppendLine("| :--- | :--- | :--- | :--- |");
                foreach (var (p, lt, cf, q) in audioRows)
                    sb.AppendLine($"| {p} | {lt} | {cf} | {q:F2} |");
                sb.AppendLine();
            }

            if (otherRows.Count > 0)
            {
                sb.AppendLine("## Other Assets");
                sb.AppendLine("| Asset Path | Importer Type |");
                sb.AppendLine("| :--- | :--- |");
                foreach (var (p, it) in otherRows)
                    sb.AppendLine($"| {p} | {it} |");
                sb.AppendLine();
            }

            if (total == 0)
                sb.AppendLine("*No assets found with importers in the specified scope.*");

            sb.AppendLine("---");
            sb.Append($"Assets reported: {total} | Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            return sb.ToString();
        }

        // ─────────────────────────────────────────────────────
        //  Mode G: Type Census
        // ─────────────────────────────────────────────────────

        private static string BuildTypeCensus(string assetPath, bool excludePackages = false)
        {
            var census = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);
            var totalByType = new Dictionary<string, int>(StringComparer.Ordinal);

            string[] guids = AssetDatabase.FindAssets("", new[] { assetPath });

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (AssetDatabase.IsValidFolder(path)) continue;
                if (excludePackages && path.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase)) continue;

                string folder = Path.GetDirectoryName(path)?.Replace("\\", "/") ?? assetPath;
                Type type = AssetDatabase.GetMainAssetTypeAtPath(path);
                string typeName = type != null ? type.Name : "Unknown";

                if (!census.ContainsKey(folder))
                    census[folder] = new Dictionary<string, int>(StringComparer.Ordinal);
                census[folder][typeName] = census[folder].TryGetValue(typeName, out int v) ? v + 1 : 1;

                totalByType[typeName] = totalByType.TryGetValue(typeName, out int tv) ? tv + 1 : 1;
            }

            // Find top 5 types by total count for table columns
            var top5 = totalByType.OrderByDescending(kv => kv.Value).Take(5).Select(kv => kv.Key).ToList();

            var sb = new StringBuilder();
            sb.AppendLine($"# Project Cartographer — Mode: Type Census | Path: {assetPath}");
            sb.AppendLine();

            // Folder table header
            sb.AppendLine("## Asset Counts by Folder");
            sb.Append("| Folder |");
            foreach (string t in top5) sb.Append($" {t} |");
            sb.AppendLine(" Other | Total |");

            sb.Append("| :--- |");
            foreach (var _ in top5) sb.Append(" :--- |");
            sb.AppendLine(" :--- | :--- |");

            foreach (var folder in census.Keys.OrderBy(f => f))
            {
                var folderTypes = census[folder];
                int folderTotal = folderTypes.Values.Sum();
                int otherCount = folderTotal;

                sb.Append($"| {folder} |");
                foreach (string t in top5)
                {
                    int cnt = folderTypes.TryGetValue(t, out int c) ? c : 0;
                    otherCount -= cnt;
                    sb.Append($" {(cnt > 0 ? cnt.ToString() : "-")} |");
                }
                sb.AppendLine($" {(otherCount > 0 ? otherCount.ToString() : "-")} | {folderTotal} |");
            }
            sb.AppendLine();

            // Project totals
            sb.AppendLine("## Project Totals");
            sb.AppendLine("| Type | Count |");
            sb.AppendLine("| :--- | :--- |");
            int grandTotal = 0;
            foreach (var kv in totalByType.OrderByDescending(kv => kv.Value))
            {
                sb.AppendLine($"| {kv.Key} | {kv.Value} |");
                grandTotal += kv.Value;
            }
            sb.AppendLine($"| **Total** | **{grandTotal}** |");
            sb.AppendLine();

            sb.AppendLine("---");
            sb.Append($"Folders: {census.Count} | Asset types: {totalByType.Count} | Total assets: {grandTotal} | Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            return sb.ToString();
        }
    }
}
