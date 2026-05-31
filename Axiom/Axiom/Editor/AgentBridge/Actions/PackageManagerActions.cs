using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using UnityEngine;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using Axiom.Editor.AgentBridge.Core;

namespace Axiom.Editor.AgentBridge.Actions
{
    /// <summary>
    /// Gives the agent control over Unity Package Manager (UPM) —
    /// listing, installing, removing, updating, and embedding packages,
    /// including support for private registries and git URLs.
    /// </summary>
    public static class PackageManagerActions
    {
        // ─────────────────────────────────────────────────────
        //  Synchronous Wait Helper
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// Waits for a UPM Request to complete, with a timeout.
        /// </summary>
        private static bool WaitForRequest(Request request, int timeoutMs = 30000)
        {
            int elapsed = 0;
            const int pollInterval = 100;

            while (!request.IsCompleted && elapsed < timeoutMs)
            {
                Thread.Sleep(pollInterval);
                elapsed += pollInterval;
            }

            return request.IsCompleted;
        }

        // ─────────────────────────────────────────────────────
        //  2.1 ListPackages
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// Lists all installed packages with version, source, and status.
        /// </summary>
        /// <param name="includeIndirect">Include transitive dependencies.</param>
        public static ActionResult ListPackages(bool includeIndirect = false)
        {
            var request = Client.List(includeIndirect);

            if (!WaitForRequest(request))
                return ActionResult.Fail("Package list request timed out.");

            if (request.Status == StatusCode.Failure)
                return ActionResult.Fail($"Package list failed: {request.Error?.message}");

            var sb = new StringBuilder();
            sb.AppendLine("# Installed Packages\n");
            sb.AppendLine("| Name | Version | Source | Status |");
            sb.AppendLine("| :--- | :--- | :--- | :--- |");

            int count = 0;
            foreach (var pkg in request.Result)
            {
                sb.AppendLine($"| {pkg.name} | {pkg.version} | {pkg.source} | {pkg.packageId} |");
                count++;
            }

            string reportPath = OutputWriter.WriteReport("installed_packages", sb.ToString());
            return ActionResult.Ok($"Found {count} packages. Report: {reportPath}");
        }

        // ─────────────────────────────────────────────────────
        //  2.2 SearchPackage
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// Searches for a package by name in the Unity registry.
        /// </summary>
        /// <param name="packageName">Full or partial package name.</param>
        public static ActionResult SearchPackage(string packageName)
        {
            var request = Client.Search(packageName);

            if (!WaitForRequest(request))
                return ActionResult.Fail("Package search timed out.");

            if (request.Status == StatusCode.Failure)
                return ActionResult.Fail($"Package search failed: {request.Error?.message}");

            var sb = new StringBuilder();
            sb.AppendLine($"# Package Search: {packageName}\n");
            sb.AppendLine("| Name | Latest Version | Description |");
            sb.AppendLine("| :--- | :--- | :--- |");

            int count = 0;
            foreach (var pkg in request.Result)
            {
                string desc = pkg.description?.Length > 80
                    ? pkg.description.Substring(0, 80) + "..."
                    : pkg.description ?? "";
                sb.AppendLine($"| {pkg.name} | {pkg.versions.latest} | {desc} |");
                count++;
            }

            string reportPath = OutputWriter.WriteReport("package_search", sb.ToString());
            return ActionResult.Ok($"Found {count} results for '{packageName}'. Report: {reportPath}");
        }

        // ─────────────────────────────────────────────────────
        //  2.3 AddPackage
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// Installs a package by identifier. Supports:
        /// - Registry packages: "com.unity.textmeshpro"
        /// - Specific version: "com.unity.textmeshpro@3.2.0"
        /// - Git URL: "https://github.com/user/repo.git"
        /// - Git URL with revision: "https://github.com/user/repo.git#v1.0.0"
        /// - Local path: "file:../local-package"
        /// </summary>
        /// <param name="packageIdentifier">Package identifier string.</param>
        public static ActionResult AddPackage(string packageIdentifier)
        {
            Debug.Log($"[AgentBridge] Installing package: {packageIdentifier}");

            var request = Client.Add(packageIdentifier);

            if (!WaitForRequest(request, 60000))
                return ActionResult.Fail($"Package install timed out: {packageIdentifier}");

            if (request.Status == StatusCode.Failure)
                return ActionResult.Fail(
                    $"Package install failed: {packageIdentifier}\n" +
                    $"Error: {request.Error?.message}\n" +
                    $"Code: {request.Error?.errorCode}");

            var pkg = request.Result;
            Debug.Log($"[AgentBridge] Installed: {pkg.name}@{pkg.version}");
            return ActionResult.Ok(
                $"Installed {pkg.name}@{pkg.version} (source: {pkg.source})");
        }

        // ─────────────────────────────────────────────────────
        //  2.4 RemovePackage
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// Removes a package by name.
        /// </summary>
        /// <param name="packageName">Package name (e.g., "com.unity.textmeshpro").</param>
        public static ActionResult RemovePackage(string packageName)
        {
            Debug.Log($"[AgentBridge] Removing package: {packageName}");

            var request = Client.Remove(packageName);

            if (!WaitForRequest(request))
                return ActionResult.Fail($"Package remove timed out: {packageName}");

            if (request.Status == StatusCode.Failure)
                return ActionResult.Fail(
                    $"Package remove failed: {packageName}\n" +
                    $"Error: {request.Error?.message}");

            Debug.Log($"[AgentBridge] Removed: {packageName}");
            return ActionResult.Ok($"Removed package: {packageName}");
        }

        // ─────────────────────────────────────────────────────
        //  2.5 EmbedPackage
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// Embeds a registry package into the project (copies to Packages/ folder).
        /// Useful for modifying package source code.
        /// </summary>
        /// <param name="packageName">Package name to embed.</param>
        public static ActionResult EmbedPackage(string packageName)
        {
            Debug.Log($"[AgentBridge] Embedding package: {packageName}");

            var request = Client.Embed(packageName);

            if (!WaitForRequest(request, 60000))
                return ActionResult.Fail($"Package embed timed out: {packageName}");

            if (request.Status == StatusCode.Failure)
                return ActionResult.Fail(
                    $"Package embed failed: {packageName}\n" +
                    $"Error: {request.Error?.message}");

            var pkg = request.Result;
            Debug.Log($"[AgentBridge] Embedded: {pkg.name} at {pkg.resolvedPath}");
            return ActionResult.Ok(
                $"Embedded {pkg.name}@{pkg.version} at: {pkg.resolvedPath}");
        }

        // ─────────────────────────────────────────────────────
        //  2.6 GetPackageInfo
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// Gets detailed information about an installed package.
        /// </summary>
        /// <param name="packageName">Package name.</param>
        public static ActionResult GetPackageInfo(string packageName)
        {
            var request = Client.List(true);

            if (!WaitForRequest(request))
                return ActionResult.Fail("Package list timed out.");

            if (request.Status == StatusCode.Failure)
                return ActionResult.Fail($"Package list failed: {request.Error?.message}");

            UnityEditor.PackageManager.PackageInfo found = null;
            foreach (var pkg in request.Result)
            {
                if (pkg.name.Equals(packageName, StringComparison.OrdinalIgnoreCase))
                {
                    found = pkg;
                    break;
                }
            }

            if (found == null)
                return ActionResult.Fail($"Package not found: {packageName}");

            var sb = new StringBuilder();
            sb.AppendLine($"# Package: {found.displayName}\n");
            sb.AppendLine($"- **Name:** {found.name}");
            sb.AppendLine($"- **Version:** {found.version}");
            sb.AppendLine($"- **Source:** {found.source}");
            sb.AppendLine($"- **PackageId:** {found.packageId}");
            sb.AppendLine($"- **Category:** {found.category}");
            sb.AppendLine($"- **Description:** {found.description}");
            sb.AppendLine($"- **Resolved Path:** {found.resolvedPath}");

            if (found.dependencies != null && found.dependencies.Length > 0)
            {
                sb.AppendLine("\n## Dependencies\n");
                foreach (var dep in found.dependencies)
                    sb.AppendLine($"- {dep.name}@{dep.version}");
            }

            string reportPath = OutputWriter.WriteReport("package_info", sb.ToString());
            return ActionResult.Ok(
                $"{found.displayName}@{found.version} ({found.source}). Report: {reportPath}");
        }

        // ─────────────────────────────────────────────────────
        //  2.7 ResolvePackages
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// Forces package resolution (equivalent to clicking "Resolve" in Package Manager).
        /// Useful after manual manifest.json edits.
        /// </summary>
        public static ActionResult ResolvePackages()
        {
            Debug.Log("[AgentBridge] Resolving packages...");

            // Client.Resolve() is fire-and-forget (returns void) in Unity 6.
            // Trigger the resolution and return.
            Client.Resolve();

            Debug.Log("[AgentBridge] Package resolution triggered.");
            return ActionResult.Ok("Package resolution triggered. UPM will resolve in the background.");
        }

        // ─────────────────────────────────────────────────────
        //  2.8 AddScopedRegistry
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// Adds a scoped registry to manifest.json (e.g., OpenUPM).
        /// Uses direct JSON manipulation of Packages/manifest.json.
        /// </summary>
        /// <param name="registryName">Display name (e.g., "OpenUPM").</param>
        /// <param name="url">Registry URL (e.g., "https://package.openupm.com").</param>
        /// <param name="scopes">Package scopes to route to this registry.</param>
        public static ActionResult AddScopedRegistry(
            string registryName, string url, string[] scopes)
        {
            string manifestPath = "Packages/manifest.json";

            if (!System.IO.File.Exists(manifestPath))
                return ActionResult.Fail("manifest.json not found at Packages/manifest.json");

            try
            {
                string json = System.IO.File.ReadAllText(manifestPath);

                string registryEntry = "{\n" +
                    $"      \"name\": \"{registryName}\",\n" +
                    $"      \"url\": \"{url}\",\n" +
                    $"      \"scopes\": [{string.Join(", ", System.Array.ConvertAll(scopes, s => $"\"{s}\""))}]\n" +
                    "    }";

                if (json.Contains("\"scopedRegistries\""))
                {
                    int scopedEnd = json.IndexOf(']', json.IndexOf("\"scopedRegistries\""));
                    if (scopedEnd >= 0)
                    {
                        string before = json.Substring(0, scopedEnd);
                        string after = json.Substring(scopedEnd);

                        if (before.TrimEnd().EndsWith("["))
                        {
                            json = before + "\n    " + registryEntry + "\n  " + after;
                        }
                        else
                        {
                            json = before + ",\n    " + registryEntry + "\n  " + after;
                        }
                    }
                }
                else
                {
                    int lastBrace = json.LastIndexOf('}');
                    string before = json.Substring(0, lastBrace);
                    json = before +
                        ",\n  \"scopedRegistries\": [\n    " +
                        registryEntry +
                        "\n  ]\n}";
                }

                System.IO.File.WriteAllText(manifestPath, json);

                // Trigger resolution (fire-and-forget in Unity 6)
                Client.Resolve();

                Debug.Log($"[AgentBridge] Added scoped registry: {registryName} ({url})");
                return ActionResult.Ok(
                    $"Added scoped registry: {registryName} ({url}). " +
                    $"Scopes: {string.Join(", ", scopes)}. " +
                    $"Package resolution triggered.");
            }
            catch (Exception ex)
            {
                return ActionResult.Fail($"Failed to add scoped registry: {ex.Message}");
            }
        }

        // ─────────────────────────────────────────────────────
        //  Menu Items
        // ─────────────────────────────────────────────────────

        [MenuItem("Axiom/AgentBridge/Actions/List Installed Packages")]
        public static void MenuListPackages()
        {
            Debug.Log($"[AgentBridge] {ListPackages().Message}");
        }
    }
}
