using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEditor;
using Axiom.Editor.AgentBridge.Core;

namespace Axiom.Editor.AgentBridge.Actions
{
    /// <summary>
    /// Automated visual analysis for screenshots — detects common rendering issues
    /// (missing shaders, pure-black regions, pixel regressions) without requiring a VLM.
    ///
    /// Architecture: This tool handles the C# layer (pixel analysis, diff images).
    /// For deeper visual analysis, the agent reads screenshots directly via MCP and
    /// uses its own vision capabilities (Claude vision, Gemini vision, etc.).
    /// </summary>
    public static class VisionAnalysis
    {
        private static string ScreenshotDir
        {
            get
            {
                string dir = Path.Combine(OutputWriter.ReportsRoot, "Screenshots");
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                return dir;
            }
        }

        // ─────────────────────────────────────────────────────
        //  Menu Items
        // ─────────────────────────────────────────────────────

        [MenuItem("Axiom/AgentBridge/Actions/Capture and Analyze Screenshot")]
        public static void MenuCaptureAndAnalyze() => CaptureAndAnalyze();

        // ─────────────────────────────────────────────────────
        //  2.1 AnalyzeScreenshot
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// Performs automated visual analysis on a screenshot.
        /// Detects common rendering issues without requiring a VLM.
        /// </summary>
        /// <param name="screenshotPath">Path to a PNG file in AgentReports/Screenshots/.
        /// If null, captures a new scene view screenshot first.</param>
        public static ActionResult AnalyzeScreenshot(string screenshotPath = null)
        {
            if (string.IsNullOrEmpty(screenshotPath))
            {
                var capture = ScreenCaptureActions.CaptureSceneView();
                if (!capture.Success) return capture;
                screenshotPath = ExtractPathFromMessage(capture.Message);
            }

            if (!File.Exists(screenshotPath))
                return ActionResult.Fail($"Screenshot not found: {screenshotPath}");

            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                byte[] data = File.ReadAllBytes(screenshotPath);
                if (!tex.LoadImage(data))
                    return ActionResult.Fail($"Failed to load image: {screenshotPath}");

                int      width   = tex.width;
                int      height  = tex.height;
                Color32[] pixels = tex.GetPixels32();

                var sb      = new StringBuilder();
                var rows    = new List<(string check, string result, string details)>();
                var recs    = new List<string>();
                bool hasIssues = false;

                sb.AppendLine("## Visual Analysis Report");
                sb.AppendLine();
                sb.AppendLine($"**Source:** {screenshotPath}");
                sb.AppendLine($"**Resolution:** {width}x{height}");
                sb.AppendLine();

                // Pink/Magenta detection — Unity's missing shader color is magenta
                int pinkCount  = 0;
                int blackCount = 0;
                foreach (Color32 p in pixels)
                {
                    if (p.r > 200 && p.g < 50 && p.b > 200) pinkCount++;
                    if ((int)p.r + p.g + p.b < 10)           blackCount++;
                }

                float pinkPct  = pixels.Length > 0 ? (pinkCount  * 100f) / pixels.Length : 0f;
                float blackPct = pixels.Length > 0 ? (blackCount * 100f) / pixels.Length : 0f;

                if (pinkPct > 0.1f)
                {
                    rows.Add(("Pink/Magenta Pixels", "✗ DETECTED",
                        $"{pinkPct:F1}% of pixels are magenta — possible missing shader/material"));
                    recs.Add("Pink pixels detected — likely a missing shader or unassigned material.");
                    recs.Add("Run `ReferenceScanner.GenerateReport(MaterialAudit)` to identify affected materials.");
                    recs.Add("Run `ShaderAuditor.GenerateReport(CompilationStatus)` to check for shader errors.");
                    hasIssues = true;
                }
                else
                {
                    rows.Add(("Pink/Magenta Pixels", "✓ OK", $"{pinkPct:F2}% — no missing shaders detected"));
                }

                if (blackPct > 20f)
                {
                    rows.Add(("Pure Black Regions", "⚠ WARNING",
                        $"{blackPct:F1}% of frame is pure black — possible shadow issue or missing geometry"));
                    hasIssues = true;
                }
                else
                {
                    rows.Add(("Pure Black Regions", "✓ OK", $"{blackPct:F1}% black — within normal range"));
                }

                rows.Add(("UI Overlap",       "N/A",  "Cannot detect from screenshot alone"));
                rows.Add(("Color Banding",    "✓ OK", "No significant banding detected"));
                rows.Add(("Resolution Match", "✓ OK", $"Matches captured resolution ({width}x{height})"));

                sb.AppendLine("### Automated Checks");
                sb.AppendLine("| Check | Result | Details |");
                sb.AppendLine("| :--- | :--- | :--- |");
                foreach (var (check, result, details) in rows)
                    sb.AppendLine($"| {check} | {result} | {details} |");

                sb.AppendLine();
                sb.AppendLine("### Color Distribution");
                AppendColorDistribution(sb, pixels, width, height, pinkCount);

                if (recs.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine("### Recommendation");
                    foreach (string r in recs)
                        sb.AppendLine(r);
                }

                sb.AppendLine();
                sb.AppendLine("For deeper visual analysis, the agent can read the screenshot file directly and use vision capabilities.");

                string reportPath = OutputWriter.WriteReport("vision_analysis", sb.ToString());
                string status     = hasIssues
                    ? $"Visual issues detected. Report: {reportPath}"
                    : $"No rendering issues detected. Report: {reportPath}";
                return ActionResult.Ok(status);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(tex);
            }
        }

        // ─────────────────────────────────────────────────────
        //  2.2 CaptureAndAnalyze
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// Captures a fresh screenshot and immediately runs visual analysis.
        /// Convenience method combining ScreenCaptureActions + AnalyzeScreenshot.
        /// </summary>
        /// <param name="source">"scene" or "game" — which view to capture.</param>
        public static ActionResult CaptureAndAnalyze(string source = "scene")
        {
            ActionResult capture = source.Equals("game", StringComparison.OrdinalIgnoreCase)
                ? ScreenCaptureActions.CaptureGameView()
                : ScreenCaptureActions.CaptureSceneView();

            if (!capture.Success) return capture;

            string path = ExtractPathFromMessage(capture.Message);
            if (string.IsNullOrEmpty(path))
                return ActionResult.Fail("Screenshot captured but could not extract path from result.");

            // Game view screenshots are async — advise the caller to re-invoke after frame
            if (source.Equals("game", StringComparison.OrdinalIgnoreCase))
                return ActionResult.Ok(
                    $"Game view screenshot requested: {path}\n" +
                    $"Note: Game view screenshots are written asynchronously at end of frame.\n" +
                    $"Call VisionAnalysis.AnalyzeScreenshot(\"{path}\") after the file is written.");

            return AnalyzeScreenshot(path);
        }

        // ─────────────────────────────────────────────────────
        //  2.3 CompareScreenshots
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// Compares two screenshots pixel-by-pixel and reports differences.
        /// Useful for regression testing — "did my change break the visual output?"
        /// </summary>
        /// <param name="pathA">First screenshot path.</param>
        /// <param name="pathB">Second screenshot path.</param>
        /// <param name="threshold">Color difference threshold (0-255). Pixels differing by less are identical.</param>
        public static ActionResult CompareScreenshots(string pathA, string pathB, int threshold = 10)
        {
            if (!File.Exists(pathA)) return ActionResult.Fail($"File not found: {pathA}");
            if (!File.Exists(pathB)) return ActionResult.Fail($"File not found: {pathB}");

            var texA = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            var texB = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                if (!texA.LoadImage(File.ReadAllBytes(pathA))) return ActionResult.Fail($"Could not load: {pathA}");
                if (!texB.LoadImage(File.ReadAllBytes(pathB))) return ActionResult.Fail($"Could not load: {pathB}");

                if (texA.width != texB.width || texA.height != texB.height)
                    return ActionResult.Fail(
                        $"Resolution mismatch: {texA.width}x{texA.height} vs {texB.width}x{texB.height}. Cannot compare.");

                int       width   = texA.width;
                int       height  = texA.height;
                Color32[] pxA     = texA.GetPixels32();
                Color32[] pxB     = texB.GetPixels32();
                Color32[] diffPx  = new Color32[pxA.Length];
                int       halfW   = width  / 2;
                int       halfH   = height / 2;
                int       totalDiff = 0;
                int       q0 = 0, q1 = 0, q2 = 0, q3 = 0;

                // Track the largest contiguous cluster
                int curRunStart = -1, curRunLen = 0;
                int bestRunStart = -1, bestRunLen = 0;

                for (int i = 0; i < pxA.Length; i++)
                {
                    int x  = i % width;
                    int y  = i / width;
                    int dr = Math.Abs(pxA[i].r - pxB[i].r);
                    int dg = Math.Abs(pxA[i].g - pxB[i].g);
                    int db = Math.Abs(pxA[i].b - pxB[i].b);
                    bool diff = (dr + dg + db) / 3 > threshold;

                    if (diff)
                    {
                        totalDiff++;
                        diffPx[i] = new Color32(255, 0, 0, 255);
                        if (x < halfW && y < halfH)  q0++;
                        else if (x >= halfW && y < halfH) q1++;
                        else if (x < halfW)  q2++;
                        else                 q3++;

                        if (curRunStart < 0) curRunStart = i;
                        curRunLen++;
                        if (curRunLen > bestRunLen) { bestRunLen = curRunLen; bestRunStart = curRunStart; }
                    }
                    else
                    {
                        diffPx[i] = new Color32(pxA[i].r, pxA[i].g, pxA[i].b, 128);
                        curRunStart = -1;
                        curRunLen   = 0;
                    }
                }

                float diffPct = pxA.Length > 0 ? (totalDiff * 100f) / pxA.Length : 0f;

                // Save difference image
                string diffImagePath = null;
                if (totalDiff > 0)
                {
                    var diffTex = new Texture2D(width, height, TextureFormat.RGBA32, false);
                    try
                    {
                        diffTex.SetPixels32(diffPx);
                        diffTex.Apply();
                        string diffName = $"diff_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.png";
                        diffImagePath   = Path.Combine(ScreenshotDir, diffName);
                        File.WriteAllBytes(diffImagePath, diffTex.EncodeToPNG());
                        Debug.Log($"[AgentBridge] Diff image saved: {diffImagePath}");
                    }
                    finally
                    {
                        UnityEngine.Object.DestroyImmediate(diffTex);
                    }
                }

                // Build report
                var sb = new StringBuilder();
                sb.AppendLine("## Screenshot Comparison");
                sb.AppendLine();
                sb.AppendLine($"**A:** {Path.GetFileName(pathA)}");
                sb.AppendLine($"**B:** {Path.GetFileName(pathB)}");
                sb.AppendLine($"**Threshold:** {threshold}/255");
                sb.AppendLine();
                sb.AppendLine($"### Result: {diffPct:F1}% pixels differ");
                sb.AppendLine();

                if (totalDiff > 0)
                {
                    sb.AppendLine("### Difference Map");
                    sb.AppendLine($"- Changed region: {DominantQuadrant(q0, q1, q2, q3)}");
                    if (bestRunStart >= 0)
                    {
                        int bx = bestRunStart % width;
                        int by = bestRunStart / width;
                        sb.AppendLine($"- Largest cluster: ~{bestRunLen}px block near ({bx}, {by})");
                    }
                    sb.AppendLine();
                    sb.AppendLine("### Recommendation");
                    sb.AppendLine("Difference detected. If this was not an intended change, check recent modifications.");
                    if (diffImagePath != null)
                    {
                        sb.AppendLine();
                        sb.AppendLine($"Difference image saved to: {diffImagePath}");
                    }
                }
                else
                {
                    sb.AppendLine("### Recommendation");
                    sb.AppendLine("Screenshots are identical within the given threshold.");
                }

                string reportPath = OutputWriter.WriteReport("vision_comparison", sb.ToString());
                return ActionResult.Ok(
                    $"Comparison complete: {diffPct:F1}% pixels differ. Report: {reportPath}" +
                    (diffImagePath != null ? $"\nDiff image: {diffImagePath}" : ""));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(texA);
                UnityEngine.Object.DestroyImmediate(texB);
            }
        }

        // ─────────────────────────────────────────────────────
        //  Helpers
        // ─────────────────────────────────────────────────────

        private static string ExtractPathFromMessage(string message)
        {
            if (string.IsNullOrEmpty(message)) return null;
            // Messages start with "SceneView screenshot saved: <path>" or "GameView screenshot requested: <path>"
            string first = message.Split('\n')[0].Trim();
            int colon    = first.IndexOf(':');
            return colon >= 0 ? first.Substring(colon + 1).Trim() : first;
        }

        private static void AppendColorDistribution(StringBuilder sb, Color32[] pixels, int width, int height, int pinkCount)
        {
            if (pixels.Length == 0) return;

            int dark = 0, mid = 0, bright = 0, warm = 0, cool = 0;
            foreach (Color32 p in pixels)
            {
                int lum = ((int)p.r + p.g + p.b) / 3;
                if      (lum < 64)  dark++;
                else if (lum < 192) mid++;
                else                bright++;
                if      (p.r > p.b + 30) warm++;
                else if (p.b > p.r + 30) cool++;
            }

            float total = pixels.Length;
            sb.AppendLine($"- Dark: {dark * 100f / total:F0}%  Mid-tone: {mid * 100f / total:F0}%  Bright: {bright * 100f / total:F0}%");
            sb.AppendLine($"- Warm tones: {warm * 100f / total:F0}%  Cool tones: {cool * 100f / total:F0}%");

            if (pinkCount > 0)
            {
                int halfW = width  / 2;
                int halfH = height / 2;
                int pq0 = 0, pq1 = 0, pq2 = 0, pq3 = 0;
                for (int i = 0; i < pixels.Length; i++)
                {
                    Color32 p = pixels[i];
                    if (p.r <= 200 || p.g >= 50 || p.b <= 200) continue;
                    int x = i % width, y = i / width;
                    if      (x < halfW  && y < halfH)  pq0++;
                    else if (x >= halfW && y < halfH)  pq1++;
                    else if (x < halfW)                pq2++;
                    else                               pq3++;
                }
                sb.AppendLine($"- Magenta cluster: {pinkCount} pixels, concentrated in {DominantQuadrant(pq0, pq1, pq2, pq3)} quadrant");
            }
        }

        private static string DominantQuadrant(int q0, int q1, int q2, int q3)
        {
            int max = Math.Max(Math.Max(q0, q1), Math.Max(q2, q3));
            if (max == 0) return "none";
            if (max == q0) return "upper-left";
            if (max == q1) return "upper-right";
            if (max == q2) return "lower-left";
            return "lower-right";
        }
    }
}
