using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor;
using Axiom.Editor.AgentBridge.Core;

namespace Axiom.Editor.AgentBridge.Actions
{
    /// <summary>
    /// Safe, AssetDatabase-based operations for moving, renaming, copying, deleting,
    /// and creating assets. Also provides batch import settings modification.
    /// Never uses System.IO.File operations on assets — always uses AssetDatabase.
    /// </summary>
    public static class AssetActions
    {
        // ─────────────────────────────────────────────────────
        //  2.1 MoveAsset
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// Moves an asset to a new location. Preserves GUID references.
        /// </summary>
        public static ActionResult MoveAsset(string sourcePath, string destPath)
        {
            if (string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(sourcePath)))
                return ActionResult.Fail($"Source asset not found: {sourcePath}");

            string destDir = Path.GetDirectoryName(destPath).Replace("\\", "/");
            EnsureFolderExists(destDir);

            string error = AssetDatabase.MoveAsset(sourcePath, destPath);
            if (!string.IsNullOrEmpty(error))
                return ActionResult.Fail($"Move failed: {error}");

            Debug.Log($"[AgentBridge] Moved: {sourcePath} → {destPath}");
            return ActionResult.Ok($"Moved: {sourcePath} → {destPath}");
        }

        // ─────────────────────────────────────────────────────
        //  2.2 RenameAsset
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// Renames an asset in place. Preserves GUID references.
        /// </summary>
        /// <param name="assetPath">Current asset path.</param>
        /// <param name="newName">New filename WITHOUT extension (extension is preserved).</param>
        public static ActionResult RenameAsset(string assetPath, string newName)
        {
            if (string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(assetPath)))
                return ActionResult.Fail($"Asset not found: {assetPath}");

            string error = AssetDatabase.RenameAsset(assetPath, newName);
            if (!string.IsNullOrEmpty(error))
                return ActionResult.Fail($"Rename failed: {error}");

            Debug.Log($"[AgentBridge] Renamed: {assetPath} → {newName}");
            return ActionResult.Ok($"Renamed: {assetPath} → {newName}");
        }

        // ─────────────────────────────────────────────────────
        //  2.3 CopyAsset
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// Copies an asset. The copy gets a new GUID.
        /// </summary>
        public static ActionResult CopyAsset(string sourcePath, string destPath)
        {
            if (string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(sourcePath)))
                return ActionResult.Fail($"Source asset not found: {sourcePath}");

            string destDir = Path.GetDirectoryName(destPath).Replace("\\", "/");
            EnsureFolderExists(destDir);

            bool success = AssetDatabase.CopyAsset(sourcePath, destPath);
            if (!success)
                return ActionResult.Fail($"Copy failed: {sourcePath} → {destPath}");

            Debug.Log($"[AgentBridge] Copied: {sourcePath} → {destPath}");
            return ActionResult.Ok($"Copied: {sourcePath} → {destPath}");
        }

        // ─────────────────────────────────────────────────────
        //  2.4 DeleteAsset
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// Deletes an asset. Moves to OS trash by default for safety.
        /// </summary>
        public static ActionResult DeleteAsset(string assetPath, bool moveToTrash = true)
        {
            if (string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(assetPath)))
                return ActionResult.Fail($"Asset not found: {assetPath}");

            bool success = moveToTrash
                ? AssetDatabase.MoveAssetToTrash(assetPath)
                : AssetDatabase.DeleteAsset(assetPath);

            if (!success)
                return ActionResult.Fail($"Delete failed: {assetPath}");

            string method = moveToTrash ? "trashed" : "deleted";
            Debug.Log($"[AgentBridge] Asset {method}: {assetPath}");
            return ActionResult.Ok($"Asset {method}: {assetPath}");
        }

        // ─────────────────────────────────────────────────────
        //  2.5 BatchMove
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// Moves multiple assets in a single AssetDatabase refresh cycle.
        /// </summary>
        public static ActionResult BatchMove(AssetMovePair[] moves)
        {
            if (moves == null || moves.Length == 0)
                return ActionResult.Fail("No moves provided.");

            AssetDatabase.StartAssetEditing();
            try
            {
                int successCount = 0;
                int failCount = 0;
                var errors = new List<string>();

                foreach (var move in moves)
                {
                    string destDir = Path.GetDirectoryName(move.destPath).Replace("\\", "/");
                    EnsureFolderExists(destDir);

                    string error = AssetDatabase.MoveAsset(move.sourcePath, move.destPath);
                    if (string.IsNullOrEmpty(error))
                        successCount++;
                    else
                    {
                        failCount++;
                        errors.Add($"{move.sourcePath}: {error}");
                    }
                }

                string summary = $"Moved {successCount}/{moves.Length} assets. Failures: {failCount}.";
                if (errors.Count > 0) summary += $"\nErrors:\n{string.Join("\n", errors)}";
                return failCount == 0 ? ActionResult.Ok(summary) : ActionResult.Fail(summary);
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                AssetDatabase.Refresh();
            }
        }

        // ─────────────────────────────────────────────────────
        //  2.6 BulkRename
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// Renames multiple assets using a regex pattern.
        /// </summary>
        public static ActionResult BulkRename(
            string folderPath,
            string extensionFilter = null,
            string searchPattern = null,
            string replacement = null,
            bool recursive = false,
            bool dryRun = false)
        {
            string searchFolder = folderPath ?? "Assets";
            string[] guids = AssetDatabase.FindAssets("", new[] { searchFolder });

            var renames = new List<(string path, string oldName, string newName)>();

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);

                if (!recursive && !IsDirectChild(path, searchFolder)) continue;
                if (AssetDatabase.IsValidFolder(path)) continue;

                if (extensionFilter != null)
                {
                    string ext = Path.GetExtension(path);
                    if (!ext.Equals(extensionFilter, StringComparison.OrdinalIgnoreCase)) continue;
                }

                string fileName = Path.GetFileNameWithoutExtension(path);

                if (searchPattern != null && replacement != null)
                {
                    string newName = Regex.Replace(fileName, searchPattern, replacement);
                    if (newName != fileName)
                        renames.Add((path, fileName, newName));
                }
            }

            if (dryRun)
            {
                var sb = new StringBuilder();
                sb.AppendLine($"# BulkRename Dry Run — {renames.Count} assets would be renamed\n");
                foreach (var (path, oldName, newName) in renames)
                    sb.AppendLine($"- {oldName} → {newName} ({path})");
                string reportPath = OutputWriter.WriteReport("bulk_rename_dryrun", sb.ToString());
                return ActionResult.Ok($"Dry run: {renames.Count} assets would be renamed. Report: {reportPath}");
            }

            int successCount = 0;
            foreach (var (path, oldName, newName) in renames)
            {
                string error = AssetDatabase.RenameAsset(path, newName);
                if (string.IsNullOrEmpty(error)) successCount++;
                else Debug.LogWarning($"[AgentBridge] Rename failed: {path}: {error}");
            }

            return ActionResult.Ok($"Renamed {successCount}/{renames.Count} assets.");
        }

        // ─────────────────────────────────────────────────────
        //  2.7 CreateFolder
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// Creates a folder in the project. Creates intermediate folders as needed.
        /// </summary>
        public static ActionResult CreateFolder(string folderPath)
        {
            if (AssetDatabase.IsValidFolder(folderPath))
                return ActionResult.Ok($"Folder already exists: {folderPath}");

            EnsureFolderExists(folderPath);
            return ActionResult.Ok($"Created folder: {folderPath}");
        }

        // ─────────────────────────────────────────────────────
        //  2.8 CreateScriptableObject
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// Creates a ScriptableObject asset of the specified type.
        /// </summary>
        public static ActionResult CreateScriptableObject(
            string typeName,
            string assetPath,
            Dictionary<string, string> properties = null)
        {
            var type = TypeCache.GetTypesDerivedFrom<ScriptableObject>()
                .FirstOrDefault(t => t.Name == typeName);

            if (type == null)
                return ActionResult.Fail($"ScriptableObject type not found: {typeName}");

            var instance = ScriptableObject.CreateInstance(type);
            if (instance == null)
                return ActionResult.Fail($"Failed to create instance of {typeName}");

            string dir = Path.GetDirectoryName(assetPath).Replace("\\", "/");
            EnsureFolderExists(dir);

            AssetDatabase.CreateAsset(instance, assetPath);

            if (properties != null && properties.Count > 0)
            {
                var so = new SerializedObject(instance);
                foreach (var kvp in properties)
                {
                    var prop = so.FindProperty(kvp.Key);
                    if (prop != null)
                        PropertyValueParser.SetPropertyValue(prop, kvp.Value);
                    else
                        Debug.LogWarning($"[AgentBridge] Property not found on {typeName}: {kvp.Key}");
                }
                so.ApplyModifiedProperties();
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"[AgentBridge] Created {typeName} at {assetPath}");
            return ActionResult.Ok($"Created {typeName} at {assetPath}", instance);
        }

        // ─────────────────────────────────────────────────────
        //  2.9 CreateMaterial
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// Creates a Material asset with a specified shader.
        /// </summary>
        public static ActionResult CreateMaterial(
            string assetPath,
            string shaderName = null,
            Dictionary<string, string> properties = null)
        {
            Shader shader;
            if (shaderName != null)
            {
                shader = Shader.Find(shaderName);
                if (shader == null)
                    return ActionResult.Fail($"Shader not found: {shaderName}");
            }
            else
            {
                shader = Shader.Find("Universal Render Pipeline/Lit")
                      ?? Shader.Find("HDRP/Lit")
                      ?? Shader.Find("Standard");

                if (shader == null)
                    return ActionResult.Fail("Could not find a default lit shader");
            }

            var material = new Material(shader);

            if (properties != null)
            {
                foreach (var kvp in properties)
                    SetMaterialProperty(material, kvp.Key, kvp.Value);
            }

            string dir = Path.GetDirectoryName(assetPath).Replace("\\", "/");
            EnsureFolderExists(dir);

            AssetDatabase.CreateAsset(material, assetPath);
            AssetDatabase.SaveAssets();

            Debug.Log($"[AgentBridge] Created Material at {assetPath} (shader: {shader.name})");
            return ActionResult.Ok($"Created Material at {assetPath} (shader: {shader.name})", material);
        }

        // ─────────────────────────────────────────────────────
        //  2.10 BatchImportSettings
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// Applies import settings to assets matching a filter.
        /// </summary>
        public static ActionResult BatchImportSettings(
            string folderPath,
            string assetType,
            Dictionary<string, string> settings,
            bool recursive = true,
            bool dryRun = false)
        {
            string typeFilter;
            switch (assetType.ToLower())
            {
                case "texture": typeFilter = "t:Texture2D"; break;
                case "model":   typeFilter = "t:Model"; break;
                case "audio":   typeFilter = "t:AudioClip"; break;
                default:
                    return ActionResult.Fail($"Unknown asset type: {assetType}. Use 'texture', 'model', or 'audio'.");
            }

            string[] guids = AssetDatabase.FindAssets(typeFilter, new[] { folderPath });
            int modifiedCount = 0;
            var report = new StringBuilder();

            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (string guid in guids)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (!recursive && !IsDirectChild(path, folderPath)) continue;

                    var importer = AssetImporter.GetAtPath(path);
                    if (importer == null) continue;

                    bool modified = false;
                    switch (assetType.ToLower())
                    {
                        case "texture":
                            modified = ApplyTextureSettings(importer as TextureImporter, settings, path, report, dryRun);
                            break;
                        case "model":
                            modified = ApplyModelSettings(importer as ModelImporter, settings, path, report, dryRun);
                            break;
                        case "audio":
                            modified = ApplyAudioSettings(importer as AudioImporter, settings, path, report, dryRun);
                            break;
                    }

                    if (modified)
                    {
                        if (!dryRun) importer.SaveAndReimport();
                        modifiedCount++;
                    }
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }

            string summary = dryRun
                ? $"Dry run: {modifiedCount}/{guids.Length} assets would be modified."
                : $"Modified {modifiedCount}/{guids.Length} assets.";

            if (report.Length > 0)
            {
                string fullReport = $"# BatchImportSettings — {assetType} in {folderPath}\n\n{report}\n---\n{summary}";
                OutputWriter.WriteReport("batch_import", fullReport);
            }

            return ActionResult.Ok(summary);
        }

        // ─────────────────────────────────────────────────────
        //  Editor Menu Items (testing)
        // ─────────────────────────────────────────────────────

        [MenuItem("Axiom/AgentBridge/Actions/Create Test Folder")]
        public static void MenuCreateFolder()
        {
            var result = CreateFolder("Assets/AgentBridgeTest");
            Debug.Log($"[AgentBridge] {result.Message}");
        }

        [MenuItem("Axiom/AgentBridge/Actions/Delete Test Folder")]
        public static void MenuDeleteFolder()
        {
            var result = DeleteAsset("Assets/AgentBridgeTest", moveToTrash: true);
            Debug.Log($"[AgentBridge] {result.Message}");
        }

        // ─────────────────────────────────────────────────────
        //  Private: Import Settings Handlers
        // ─────────────────────────────────────────────────────

        private static bool ApplyTextureSettings(
            TextureImporter importer, Dictionary<string, string> settings,
            string path, StringBuilder report, bool dryRun)
        {
            if (importer == null) return false;
            bool modified = false;

            foreach (var kvp in settings)
            {
                switch (kvp.Key.ToLower())
                {
                    case "maxsize":
                    case "maxtexturesize":
                        int maxSize = int.Parse(kvp.Value);
                        if (importer.maxTextureSize != maxSize)
                        {
                            report.AppendLine($"  {path}: maxTextureSize {importer.maxTextureSize} → {maxSize}");
                            if (!dryRun) importer.maxTextureSize = maxSize;
                            modified = true;
                        }
                        break;

                    case "texturetype":
                        var textureType = (TextureImporterType)Enum.Parse(typeof(TextureImporterType), kvp.Value, true);
                        if (importer.textureType != textureType)
                        {
                            report.AppendLine($"  {path}: textureType {importer.textureType} → {textureType}");
                            if (!dryRun) importer.textureType = textureType;
                            modified = true;
                        }
                        break;

                    case "compression":
                        var compression = (TextureImporterCompression)Enum.Parse(typeof(TextureImporterCompression), kvp.Value, true);
                        if (importer.textureCompression != compression)
                        {
                            report.AppendLine($"  {path}: compression {importer.textureCompression} → {compression}");
                            if (!dryRun) importer.textureCompression = compression;
                            modified = true;
                        }
                        break;

                    case "srgb":
                    case "srgbcolor":
                        bool sRGB = bool.Parse(kvp.Value);
                        if (importer.sRGBTexture != sRGB)
                        {
                            report.AppendLine($"  {path}: sRGB {importer.sRGBTexture} → {sRGB}");
                            if (!dryRun) importer.sRGBTexture = sRGB;
                            modified = true;
                        }
                        break;

                    case "readable":
                    case "isreadable":
                        bool readable = bool.Parse(kvp.Value);
                        if (importer.isReadable != readable)
                        {
                            report.AppendLine($"  {path}: isReadable {importer.isReadable} → {readable}");
                            if (!dryRun) importer.isReadable = readable;
                            modified = true;
                        }
                        break;

                    case "mipmaps":
                    case "mipmapsenabled":
                        bool mipmaps = bool.Parse(kvp.Value);
                        if (importer.mipmapEnabled != mipmaps)
                        {
                            report.AppendLine($"  {path}: mipmapEnabled {importer.mipmapEnabled} → {mipmaps}");
                            if (!dryRun) importer.mipmapEnabled = mipmaps;
                            modified = true;
                        }
                        break;

                    case "filtermode":
                        var filterMode = (FilterMode)Enum.Parse(typeof(FilterMode), kvp.Value, true);
                        if (importer.filterMode != filterMode)
                        {
                            report.AppendLine($"  {path}: filterMode {importer.filterMode} → {filterMode}");
                            if (!dryRun) importer.filterMode = filterMode;
                            modified = true;
                        }
                        break;

                    case "npot":
                    case "npotscale":
                        var npot = (TextureImporterNPOTScale)Enum.Parse(typeof(TextureImporterNPOTScale), kvp.Value, true);
                        if (importer.npotScale != npot)
                        {
                            report.AppendLine($"  {path}: npotScale {importer.npotScale} → {npot}");
                            if (!dryRun) importer.npotScale = npot;
                            modified = true;
                        }
                        break;

                    default:
                        Debug.LogWarning($"[AgentBridge] Unknown texture setting: {kvp.Key}");
                        break;
                }
            }

            return modified;
        }

        private static bool ApplyModelSettings(
            ModelImporter importer, Dictionary<string, string> settings,
            string path, StringBuilder report, bool dryRun)
        {
            if (importer == null) return false;
            bool modified = false;

            foreach (var kvp in settings)
            {
                switch (kvp.Key.ToLower())
                {
                    case "globalscale":
                    case "scalefactor":
                        float scale = float.Parse(kvp.Value, CultureInfo.InvariantCulture);
                        if (Math.Abs(importer.globalScale - scale) > 0.001f)
                        {
                            report.AppendLine($"  {path}: globalScale {importer.globalScale} → {scale}");
                            if (!dryRun) importer.globalScale = scale;
                            modified = true;
                        }
                        break;

                    case "importmaterials":
                    case "materialimportmode":
                        var matMode = (ModelImporterMaterialImportMode)Enum.Parse(
                            typeof(ModelImporterMaterialImportMode), kvp.Value, true);
                        if (importer.materialImportMode != matMode)
                        {
                            report.AppendLine($"  {path}: materialImportMode {importer.materialImportMode} → {matMode}");
                            if (!dryRun) importer.materialImportMode = matMode;
                            modified = true;
                        }
                        break;

                    case "meshcompression":
                        var meshComp = (ModelImporterMeshCompression)Enum.Parse(typeof(ModelImporterMeshCompression), kvp.Value, true);
                        if (importer.meshCompression != meshComp)
                        {
                            report.AppendLine($"  {path}: meshCompression {importer.meshCompression} → {meshComp}");
                            if (!dryRun) importer.meshCompression = meshComp;
                            modified = true;
                        }
                        break;

                    case "isreadable":
                    case "readable":
                        bool readable = bool.Parse(kvp.Value);
                        if (importer.isReadable != readable)
                        {
                            report.AppendLine($"  {path}: isReadable {importer.isReadable} → {readable}");
                            if (!dryRun) importer.isReadable = readable;
                            modified = true;
                        }
                        break;

                    case "importanimation":
                        bool importAnim = bool.Parse(kvp.Value);
                        if (importer.importAnimation != importAnim)
                        {
                            report.AppendLine($"  {path}: importAnimation {importer.importAnimation} → {importAnim}");
                            if (!dryRun) importer.importAnimation = importAnim;
                            modified = true;
                        }
                        break;

                    case "generatecolliders":
                        bool genColliders = bool.Parse(kvp.Value);
                        if (importer.addCollider != genColliders)
                        {
                            report.AppendLine($"  {path}: addCollider {importer.addCollider} → {genColliders}");
                            if (!dryRun) importer.addCollider = genColliders;
                            modified = true;
                        }
                        break;

                    default:
                        Debug.LogWarning($"[AgentBridge] Unknown model setting: {kvp.Key}");
                        break;
                }
            }

            return modified;
        }

        private static bool ApplyAudioSettings(
            AudioImporter importer, Dictionary<string, string> settings,
            string path, StringBuilder report, bool dryRun)
        {
            if (importer == null) return false;
            bool modified = false;

            var sampleSettings = importer.defaultSampleSettings;
            bool sampleModified = false;

            foreach (var kvp in settings)
            {
                switch (kvp.Key.ToLower())
                {
                    case "forcetomono":
                        bool mono = bool.Parse(kvp.Value);
                        if (importer.forceToMono != mono)
                        {
                            report.AppendLine($"  {path}: forceToMono {importer.forceToMono} → {mono}");
                            if (!dryRun) importer.forceToMono = mono;
                            modified = true;
                        }
                        break;

                    case "loadinbackground":
                        bool loadBg = bool.Parse(kvp.Value);
                        if (importer.loadInBackground != loadBg)
                        {
                            report.AppendLine($"  {path}: loadInBackground {importer.loadInBackground} → {loadBg}");
                            if (!dryRun) importer.loadInBackground = loadBg;
                            modified = true;
                        }
                        break;

                    case "loadtype":
                        var loadType = (AudioClipLoadType)Enum.Parse(typeof(AudioClipLoadType), kvp.Value, true);
                        if (sampleSettings.loadType != loadType)
                        {
                            report.AppendLine($"  {path}: loadType {sampleSettings.loadType} → {loadType}");
                            sampleSettings.loadType = loadType;
                            sampleModified = true;
                        }
                        break;

                    case "compressionformat":
                        var compFormat = (AudioCompressionFormat)Enum.Parse(typeof(AudioCompressionFormat), kvp.Value, true);
                        if (sampleSettings.compressionFormat != compFormat)
                        {
                            report.AppendLine($"  {path}: compressionFormat {sampleSettings.compressionFormat} → {compFormat}");
                            sampleSettings.compressionFormat = compFormat;
                            sampleModified = true;
                        }
                        break;

                    case "quality":
                        float quality = float.Parse(kvp.Value, CultureInfo.InvariantCulture);
                        report.AppendLine($"  {path}: quality → {quality}");
                        sampleSettings.quality = quality;
                        sampleModified = true;
                        break;

                    default:
                        Debug.LogWarning($"[AgentBridge] Unknown audio setting: {kvp.Key}");
                        break;
                }
            }

            if (sampleModified)
            {
                if (!dryRun) importer.defaultSampleSettings = sampleSettings;
                modified = true;
            }

            return modified;
        }

        // ─────────────────────────────────────────────────────
        //  Private: Material Property Setter
        // ─────────────────────────────────────────────────────

        private static void SetMaterialProperty(Material mat, string propName, string value)
        {
            if (!mat.HasProperty(propName))
            {
                Debug.LogWarning($"[AgentBridge] Material property not found: {propName}");
                return;
            }

            int propIndex = mat.shader.FindPropertyIndex(propName);
            if (propIndex < 0)
            {
                Debug.LogWarning($"[AgentBridge] Shader property index not found: {propName}");
                return;
            }

            var propType = mat.shader.GetPropertyType(propIndex);
            switch (propType)
            {
                case ShaderPropertyType.Color:
                    mat.SetColor(propName, PropertyValueParser.ParseColor(value));
                    break;
                case ShaderPropertyType.Float:
                case ShaderPropertyType.Range:
                    mat.SetFloat(propName, float.Parse(value, CultureInfo.InvariantCulture));
                    break;
                case ShaderPropertyType.Vector:
                    mat.SetVector(propName, PropertyValueParser.ParseVector4(value));
                    break;
                case ShaderPropertyType.Texture:
                    var tex = AssetDatabase.LoadAssetAtPath<Texture>(value);
                    if (tex != null) mat.SetTexture(propName, tex);
                    break;
                case ShaderPropertyType.Int:
                    mat.SetInt(propName, int.Parse(value));
                    break;
            }
        }

        // ─────────────────────────────────────────────────────
        //  Private: Folder Helpers
        // ─────────────────────────────────────────────────────

        private static void EnsureFolderExists(string folderPath)
        {
            if (string.IsNullOrEmpty(folderPath) || AssetDatabase.IsValidFolder(folderPath))
                return;

            string parent = Path.GetDirectoryName(folderPath).Replace("\\", "/");
            string folderName = Path.GetFileName(folderPath);

            EnsureFolderExists(parent);
            AssetDatabase.CreateFolder(parent, folderName);
        }

        private static bool IsDirectChild(string assetPath, string folderPath)
        {
            string dir = Path.GetDirectoryName(assetPath).Replace("\\", "/");
            return dir == folderPath.TrimEnd('/');
        }
    }

    // ─────────────────────────────────────────────────────
    //  AssetMovePair (used by BatchMove)
    // ─────────────────────────────────────────────────────

    [Serializable]
    public class AssetMovePair
    {
        public string sourcePath;
        public string destPath;
    }
}
