using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEditor;
using Axiom.Editor.AgentBridge.Core;

#if AXIOM_HAS_SENTIS
using Unity.Sentis;
#endif

namespace Axiom.Editor.AgentBridge.Actions
{
    /// <summary>
    /// Generic scaffolding around Unity Sentis for loading and running ONNX models.
    /// Provides the infrastructure for project-specific ML workflows.
    /// Gracefully reports "not installed" when the Sentis package is absent.
    /// </summary>
    public static class SentisActions
    {
        // ─────────────────────────────────────────────────────
        //  Menu Items
        // ─────────────────────────────────────────────────────

        [MenuItem("Axiom/AgentBridge/Actions/Sentis Status")]
        public static void MenuGetSentisStatus() => GetSentisStatus();

        // ─────────────────────────────────────────────────────
        //  3.1 GetSentisStatus
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// Reports Sentis package status and lists available ONNX models in the project.
        /// </summary>
        public static ActionResult GetSentisStatus()
        {
            var sb = new StringBuilder();
            sb.AppendLine("## Sentis Status");
            sb.AppendLine();

#if AXIOM_HAS_SENTIS
            sb.AppendLine("### Package");
            sb.AppendLine("- Unity Sentis: Installed");
            sb.AppendLine($"- Backend: GPUCompute (preferred), CPU (fallback)");
            sb.AppendLine();

            // Discover ONNX model assets
            var modelGuids = new List<string>();

            // Try ModelAsset type first (Sentis registers this asset type)
            string[] typeSearch = AssetDatabase.FindAssets("t:ModelAsset");
            foreach (string guid in typeSearch)
                modelGuids.Add(guid);

            // Also find loose .onnx files not yet imported as ModelAsset
            string[] onnxSearch = AssetDatabase.FindAssets("t:DefaultAsset");
            foreach (string guid in onnxSearch)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.EndsWith(".onnx", StringComparison.OrdinalIgnoreCase) &&
                    !modelGuids.Contains(guid))
                    modelGuids.Add(guid);
            }

            if (modelGuids.Count == 0)
            {
                sb.AppendLine("### Available Models");
                sb.AppendLine("No ONNX model assets found in the project.");
                sb.AppendLine("Place .onnx files in an Assets/ folder to use them.");
            }
            else
            {
                sb.AppendLine($"### Available Models ({modelGuids.Count})");
                sb.AppendLine("| Asset | Format | Size |");
                sb.AppendLine("| :--- | :--- | :--- |");

                foreach (string guid in modelGuids)
                {
                    string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                    string fullPath  = Path.Combine(
                        Path.GetDirectoryName(Application.dataPath),
                        assetPath.Replace('/', Path.DirectorySeparatorChar));
                    long   size = File.Exists(fullPath) ? new FileInfo(fullPath).Length : 0;
                    string sizeFmt = size > 1024 * 1024
                        ? $"{size / (1024.0 * 1024.0):F1} MB"
                        : $"{size / 1024.0:F1} KB";
                    sb.AppendLine($"| {assetPath} | ONNX | {sizeFmt} |");
                }

                sb.AppendLine();
                sb.AppendLine("Use `SentisActions.RunModel(modelPath, inputData, inputShape)` to run inference.");
                sb.AppendLine("Use `SentisActions.RunImageModel(modelPath, imagePath)` for image-based models.");
            }
#else
            sb.AppendLine("### Package");
            sb.AppendLine("- Unity Sentis: **Not installed**");
            sb.AppendLine();
            sb.AppendLine("### How to Install");
            sb.AppendLine("1. Open **Window > Package Manager**");
            sb.AppendLine("2. Click **+ > Add package by name**");
            sb.AppendLine("3. Enter: `com.unity.sentis`");
            sb.AppendLine("4. After installation, `AXIOM_HAS_SENTIS` will be defined and all methods will be active.");
#endif

            sb.AppendLine();
            sb.AppendLine("---");
            sb.Append($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");

            string reportPath = OutputWriter.WriteReport("sentis_status", sb.ToString());
            return ActionResult.Ok($"Sentis status report: {reportPath}");
        }

        // ─────────────────────────────────────────────────────
        //  3.2 RunModel
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// Loads and runs an ONNX model with provided input data.
        /// </summary>
        /// <param name="modelPath">Project path to the .onnx model asset.</param>
        /// <param name="inputData">Input tensor data as flat float array.</param>
        /// <param name="inputShape">Input tensor shape as comma-separated ints (e.g., "1,3,256,256").</param>
        /// <returns>ActionResult with output tensor data as a formatted report.</returns>
        public static ActionResult RunModel(string modelPath, float[] inputData, string inputShape)
        {
#if AXIOM_HAS_SENTIS
            var modelAsset = AssetDatabase.LoadAssetAtPath<ModelAsset>(modelPath);
            if (modelAsset == null)
                return ActionResult.Fail($"Model not found: {modelPath}");

            Worker worker = null;
            Tensor<float> inputTensor = null;
            try
            {
                var model = ModelLoader.Load(modelAsset);

                // Parse input shape
                int[] dims;
                try
                {
                    dims = inputShape.Split(',').Select(int.Parse).ToArray();
                }
                catch (Exception ex)
                {
                    return ActionResult.Fail($"Invalid inputShape '{inputShape}': {ex.Message}");
                }

                worker      = new Worker(model, BackendType.GPUCompute);
                inputTensor = new Tensor<float>(new TensorShape(dims), inputData);

                worker.Schedule(inputTensor);

                var outputTensor = worker.PeekOutput() as Tensor<float>;
                if (outputTensor == null)
                    return ActionResult.Fail("Model output is not a float tensor or could not be read.");

                float[] outputData = outputTensor.ToReadOnlyArray();

                var sb = new StringBuilder();
                sb.AppendLine($"## Model Output: {modelPath}");
                sb.AppendLine();
                sb.AppendLine($"**Input Shape:** [{inputShape}]");
                sb.AppendLine($"**Output Shape:** [{string.Join(",", outputTensor.shape)}]");
                sb.AppendLine($"**Output Length:** {outputData.Length} values");
                sb.AppendLine();
                sb.AppendLine("**Values (first 20):**");
                sb.AppendLine($"[{string.Join(", ", outputData.Take(20).Select(f => f.ToString("F4")))}]");

                if (outputData.Length > 20)
                    sb.AppendLine($"... ({outputData.Length - 20} more values)");

                string reportPath = OutputWriter.WriteReport("sentis_output", sb.ToString());
                return ActionResult.Ok($"Model executed. Report: {reportPath}");
            }
            catch (Exception ex)
            {
                return ActionResult.Fail($"Model execution failed: {ex.Message}");
            }
            finally
            {
                worker?.Dispose();
                inputTensor?.Dispose();
            }
#else
            return ActionResult.Fail("Unity Sentis package not installed. Install com.unity.sentis via Package Manager.");
#endif
        }

        // ─────────────────────────────────────────────────────
        //  3.3 RunImageModel
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// Convenience method: takes a screenshot path, preprocesses the image,
        /// runs it through a specified model, and returns results.
        /// Handles the Texture2D → Tensor conversion automatically.
        /// </summary>
        /// <param name="modelPath">Project path to the .onnx model.</param>
        /// <param name="imagePath">Path to input image (PNG).</param>
        /// <param name="targetWidth">Resize image to this width before inference.</param>
        /// <param name="targetHeight">Resize image to this height before inference.</param>
        public static ActionResult RunImageModel(string modelPath, string imagePath,
            int targetWidth = 256, int targetHeight = 256)
        {
#if AXIOM_HAS_SENTIS
            if (!File.Exists(imagePath))
                return ActionResult.Fail($"Image not found: {imagePath}");

            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                byte[] data = File.ReadAllBytes(imagePath);
                if (!tex.LoadImage(data))
                    return ActionResult.Fail($"Failed to load image: {imagePath}");

                // Resize to target dimensions
                var rt          = RenderTexture.GetTemporary(targetWidth, targetHeight, 0, RenderTextureFormat.ARGB32);
                var prevRT      = RenderTexture.active;
                Graphics.Blit(tex, rt);
                RenderTexture.active = rt;

                var resized = new Texture2D(targetWidth, targetHeight, TextureFormat.RGB24, false);
                try
                {
                    resized.ReadPixels(new Rect(0, 0, targetWidth, targetHeight), 0, 0);
                    resized.Apply();
                }
                finally
                {
                    RenderTexture.active = prevRT;
                    RenderTexture.ReleaseTemporary(rt);
                }

                // Convert to channel-first float tensor [1, 3, H, W], normalized 0-1
                Color32[] pixels = resized.GetPixels32();
                UnityEngine.Object.DestroyImmediate(resized);

                float[] tensorData = new float[3 * targetWidth * targetHeight];
                for (int i = 0; i < pixels.Length; i++)
                {
                    tensorData[0 * targetWidth * targetHeight + i] = pixels[i].r / 255f;
                    tensorData[1 * targetWidth * targetHeight + i] = pixels[i].g / 255f;
                    tensorData[2 * targetWidth * targetHeight + i] = pixels[i].b / 255f;
                }

                string inputShape = $"1,3,{targetHeight},{targetWidth}";
                return RunModel(modelPath, tensorData, inputShape);
            }
            catch (Exception ex)
            {
                return ActionResult.Fail($"Image preprocessing failed: {ex.Message}");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(tex);
            }
#else
            return ActionResult.Fail("Unity Sentis package not installed. Install com.unity.sentis via Package Manager.");
#endif
        }
    }
}
