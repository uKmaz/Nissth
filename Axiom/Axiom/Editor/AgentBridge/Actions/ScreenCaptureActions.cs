using System;
using System.IO;
using UnityEngine;
using UnityEditor;
using Axiom.Editor.AgentBridge.Core;

namespace Axiom.Editor.AgentBridge.Actions
{
    /// <summary>
    /// Gives the agent eyes — capturing screenshots from SceneView and GameView
    /// for vision-based analysis. Enables the multi-modal feedback loop where the
    /// agent can see visual glitches, z-fighting, lighting issues, or layout problems.
    /// Screenshots are saved to AgentReports/Screenshots/ as PNG files.
    /// </summary>
    public static class ScreenCaptureActions
    {
        // ─────────────────────────────────────────────────────
        //  Screenshot Output Directory
        // ─────────────────────────────────────────────────────

        private static string ScreenshotDir
        {
            get
            {
                string dir = "AgentReports/Screenshots";
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                return dir;
            }
        }

        private static string GenerateFilename(string prefix)
        {
            return $"{prefix}_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.png";
        }

        // ─────────────────────────────────────────────────────
        //  4.1 CaptureGameView
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// Captures a screenshot of the GameView.
        /// Works in both Edit Mode and Play Mode (Play Mode gives actual rendered frame).
        /// </summary>
        /// <param name="superSize">Resolution multiplier (1-4). Default 1.</param>
        public static ActionResult CaptureGameView(int superSize = 1)
        {
            superSize = Mathf.Clamp(superSize, 1, 4);

            string filename = GenerateFilename("gameview");
            string filePath = Path.Combine(ScreenshotDir, filename);

            ScreenCapture.CaptureScreenshot(filePath, superSize);

            Debug.Log($"[AgentBridge] GameView screenshot requested: {filePath} (superSize: {superSize})");

            return ActionResult.Ok(
                $"GameView screenshot requested: {filePath}\n" +
                $"SuperSize: {superSize}x\n" +
                $"Note: File is written asynchronously at end of frame. " +
                $"In Edit Mode, you may need to trigger a repaint first.");
        }

        // ─────────────────────────────────────────────────────
        //  4.2 CaptureSceneView
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// Captures a screenshot of the active SceneView using its camera.
        /// Works in Edit Mode. Renders the scene from the current SceneView camera angle.
        /// </summary>
        /// <param name="width">Output width in pixels. Default 1920.</param>
        /// <param name="height">Output height in pixels. Default 1080.</param>
        public static ActionResult CaptureSceneView(int width = 1920, int height = 1080)
        {
            var sceneView = SceneView.lastActiveSceneView;
            if (sceneView == null)
                return ActionResult.Fail("No active SceneView found. Open a Scene window.");

            var camera = sceneView.camera;
            if (camera == null)
                return ActionResult.Fail("SceneView camera is null.");

            var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            rt.antiAliasing = 4;

            var previousRT = camera.targetTexture;
            camera.targetTexture = rt;

            sceneView.Repaint();
            camera.Render();

            RenderTexture.active = rt;
            var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            tex.Apply();

            camera.targetTexture = previousRT;
            RenderTexture.active = null;

            byte[] pngData = tex.EncodeToPNG();
            string filename = GenerateFilename("sceneview");
            string filePath = Path.Combine(ScreenshotDir, filename);
            File.WriteAllBytes(filePath, pngData);

            UnityEngine.Object.DestroyImmediate(rt);
            UnityEngine.Object.DestroyImmediate(tex);

            Debug.Log($"[AgentBridge] SceneView screenshot saved: {filePath} ({width}x{height})");
            return ActionResult.Ok(
                $"SceneView screenshot saved: {filePath}\n" +
                $"Resolution: {width}x{height}\n" +
                $"Camera Position: {camera.transform.position}\n" +
                $"Camera Rotation: {camera.transform.rotation.eulerAngles}");
        }

        // ─────────────────────────────────────────────────────
        //  4.3 CaptureSceneViewFromAngle
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// Positions the SceneView camera at a specific angle, then captures.
        /// Useful for automated visual checks from known viewpoints.
        /// </summary>
        /// <param name="position">Camera world position.</param>
        /// <param name="lookAt">Point to look at.</param>
        /// <param name="width">Output width. Default 1920.</param>
        /// <param name="height">Output height. Default 1080.</param>
        public static ActionResult CaptureSceneViewFromAngle(
            Vector3 position, Vector3 lookAt,
            int width = 1920, int height = 1080)
        {
            var sceneView = SceneView.lastActiveSceneView;
            if (sceneView == null)
                return ActionResult.Fail("No active SceneView found.");

            sceneView.pivot = lookAt;
            sceneView.rotation = Quaternion.LookRotation(lookAt - position);
            sceneView.size = Vector3.Distance(position, lookAt) * 0.5f;
            sceneView.Repaint();

            return CaptureSceneView(width, height);
        }

        // ─────────────────────────────────────────────────────
        //  4.4 CaptureWithAnnotations
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// Captures a SceneView screenshot with or without gizmos/annotations.
        /// Toggles gizmo visibility before capture and restores after.
        /// </summary>
        /// <param name="showGizmos">Show gizmos in the capture.</param>
        /// <param name="width">Output width. Default 1920.</param>
        /// <param name="height">Output height. Default 1080.</param>
        public static ActionResult CaptureWithAnnotations(
            bool showGizmos = true, int width = 1920, int height = 1080)
        {
            var sceneView = SceneView.lastActiveSceneView;
            if (sceneView == null)
                return ActionResult.Fail("No active SceneView found.");

            bool previousDrawGizmos = sceneView.drawGizmos;
            sceneView.drawGizmos = showGizmos;
            sceneView.Repaint();

            var result = CaptureSceneView(width, height);

            sceneView.drawGizmos = previousDrawGizmos;
            sceneView.Repaint();

            if (result.Success)
            {
                string gizmoState = showGizmos ? "with" : "without";
                return ActionResult.Ok(
                    $"SceneView captured {gizmoState} gizmos.\n{result.Message}");
            }

            return result;
        }

        // ─────────────────────────────────────────────────────
        //  4.5 ListScreenshots
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// Lists all screenshots in the AgentReports/Screenshots directory.
        /// </summary>
        public static ActionResult ListScreenshots()
        {
            if (!Directory.Exists(ScreenshotDir))
                return ActionResult.Ok("No screenshots directory found.");

            string[] files = Directory.GetFiles(ScreenshotDir, "*.png");

            if (files.Length == 0)
                return ActionResult.Ok("No screenshots found.");

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("# Screenshots\n");
            sb.AppendLine("| File | Size | Date |");
            sb.AppendLine("| :--- | :--- | :--- |");

            foreach (string file in files)
            {
                var info = new FileInfo(file);
                sb.AppendLine(
                    $"| {info.Name} | {info.Length / 1024f:F0} KB | " +
                    $"{info.LastWriteTime:yyyy-MM-dd HH:mm:ss} |");
            }

            return ActionResult.Ok($"Found {files.Length} screenshots.\n{sb}");
        }

        // ─────────────────────────────────────────────────────
        //  4.6 CleanupScreenshots
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// Deletes screenshots in the AgentReports/Screenshots directory.
        /// </summary>
        /// <param name="olderThanMinutes">Only delete files older than this. 0 = delete all.</param>
        public static ActionResult CleanupScreenshots(int olderThanMinutes = 0)
        {
            if (!Directory.Exists(ScreenshotDir))
                return ActionResult.Ok("No screenshots directory found.");

            string[] files = Directory.GetFiles(ScreenshotDir, "*.png");
            int deleted = 0;

            foreach (string file in files)
            {
                if (olderThanMinutes > 0)
                {
                    var info = new FileInfo(file);
                    if ((DateTime.Now - info.LastWriteTime).TotalMinutes < olderThanMinutes)
                        continue;
                }

                File.Delete(file);
                deleted++;
            }

            Debug.Log($"[AgentBridge] Cleaned up {deleted} screenshots.");
            return ActionResult.Ok($"Deleted {deleted} screenshots.");
        }

        // ─────────────────────────────────────────────────────
        //  Menu Items
        // ─────────────────────────────────────────────────────

        [MenuItem("Axiom/AgentBridge/Actions/Capture SceneView Screenshot")]
        public static void MenuCaptureSceneView()
        {
            var result = CaptureSceneView();
            Debug.Log($"[AgentBridge] {result.Message}");
        }

        [MenuItem("Axiom/AgentBridge/Actions/Capture GameView Screenshot")]
        public static void MenuCaptureGameView()
        {
            var result = CaptureGameView();
            Debug.Log($"[AgentBridge] {result.Message}");
        }

        [MenuItem("Axiom/AgentBridge/Actions/List Screenshots")]
        public static void MenuListScreenshots()
        {
            Debug.Log($"[AgentBridge] {ListScreenshots().Message}");
        }
    }
}
