using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Audio;
using UnityEditor;
using Axiom.Editor.AgentBridge.Core;

namespace Axiom.Editor.AgentBridge.Diagnostics
{
    public static class AudioReporter
    {
        public enum AudioReporterMode { SourceCensus, MixerGraph, ClipImportAudit, SpatialAudioMap }

        [MenuItem("Axiom/AgentBridge/Audio Reporter — Mode A (Source Census)")]
        public static void ModeA() => GenerateReport(AudioReporterMode.SourceCensus);

        [MenuItem("Axiom/AgentBridge/Audio Reporter — Mode B (Mixer Graph)")]
        public static void ModeB() => GenerateReport(AudioReporterMode.MixerGraph);

        [MenuItem("Axiom/AgentBridge/Audio Reporter — Mode C (Clip Import Audit)")]
        public static void ModeC() => GenerateReport(AudioReporterMode.ClipImportAudit);

        [MenuItem("Axiom/AgentBridge/Audio Reporter — Mode D (Spatial Audio Map)")]
        public static void ModeD() => GenerateReport(AudioReporterMode.SpatialAudioMap);

        public static string GenerateReport(AudioReporterMode mode)
        {
            var sb = new StringBuilder();

            switch (mode)
            {
                case AudioReporterMode.SourceCensus:
                    BuildSourceCensus(sb);
                    return OutputWriter.WriteReport("audio_reporter_sourcecensus", sb.ToString());
                case AudioReporterMode.MixerGraph:
                    BuildMixerGraph(sb);
                    return OutputWriter.WriteReport("audio_reporter_mixergraph", sb.ToString());
                case AudioReporterMode.ClipImportAudit:
                    BuildClipImportAudit(sb);
                    return OutputWriter.WriteReport("audio_reporter_clipaudit", sb.ToString());
                case AudioReporterMode.SpatialAudioMap:
                    BuildSpatialAudioMap(sb);
                    return OutputWriter.WriteReport("audio_reporter_spatial", sb.ToString());
                default:
                    return OutputWriter.WriteReport("audio_reporter", "Unknown mode");
            }
        }

        // ─── Mode A ─────────────────────────────────────────────────────────────

        static void BuildSourceCensus(StringBuilder sb)
        {
            var sources = UnityEngine.Object.FindObjectsByType<AudioSource>(FindObjectsSortMode.None);
            sb.AppendLine($"# Audio Sources ({sources.Length})");
            sb.AppendLine();

            if (sources.Length == 0)
            {
                sb.AppendLine("_No AudioSources found in scene._");
                sb.AppendLine();
                sb.AppendLine($"---\nGenerated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                return;
            }

            sb.AppendLine("| Object | Clip | Mixer Group | Volume | Spatial Blend | Loop | PlayOnAwake |");
            sb.AppendLine("| :--- | :--- | :--- | :--- | :--- | :--- | :--- |");

            foreach (var src in sources.OrderBy(s => GetPath(s.gameObject)))
            {
                string path = GetPath(src.gameObject);
                string clip = src.clip != null ? src.clip.name : "— (runtime)";
                string group = src.outputAudioMixerGroup != null ? src.outputAudioMixerGroup.name : "—";
                string spatial = src.spatialBlend == 0f ? "0.0 (2D)"
                    : src.spatialBlend == 1f ? "1.0 (3D)"
                    : $"{src.spatialBlend:F2} (blended)";
                sb.AppendLine($"| {path} | {clip} | {group} | {src.volume:F2} | {spatial} | {YN(src.loop)} | {YN(src.playOnAwake)} |");
            }

            sb.AppendLine();
            sb.AppendLine($"---\nGenerated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        }

        // ─── Mode B ─────────────────────────────────────────────────────────────

        static void BuildMixerGraph(StringBuilder sb)
        {
            sb.AppendLine("# Audio Mixer Graph");
            sb.AppendLine();

            string[] guids = AssetDatabase.FindAssets("t:AudioMixer");
            if (guids.Length == 0)
            {
                sb.AppendLine("_No AudioMixer assets found in project._");
                sb.AppendLine();
                sb.AppendLine($"---\nGenerated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                return;
            }

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var mixer = AssetDatabase.LoadAssetAtPath<AudioMixer>(path);
                if (mixer == null) continue;

                sb.AppendLine($"## AudioMixer: {mixer.name}");
                sb.AppendLine();

                // Group hierarchy via FindMatchingGroups
                var allGroups = mixer.FindMatchingGroups("");
                if (allGroups != null && allGroups.Length > 0)
                {
                    sb.AppendLine("### Group Hierarchy");

                    // Use SerializedObject to read volume and effects
                    var so = new SerializedObject(mixer);
                    var masterGroupProp = so.FindProperty("m_MasterGroup");
                    AudioMixerGroup masterGroup = masterGroupProp != null
                        ? masterGroupProp.objectReferenceValue as AudioMixerGroup
                        : null;

                    if (masterGroup != null)
                    {
                        AppendMixerGroupTree(sb, masterGroup, 0);
                    }
                    else
                    {
                        foreach (var g in allGroups)
                            sb.AppendLine($"- {g.name}");
                    }
                    sb.AppendLine();

                    // Effects
                    sb.AppendLine("### Effects");
                    sb.AppendLine("| Group | Effects |");
                    sb.AppendLine("| :--- | :--- |");
                    foreach (var g in allGroups)
                    {
                        var gso = new SerializedObject(g);
                        var effectsProp = gso.FindProperty("m_Effects");
                        if (effectsProp != null && effectsProp.isArray && effectsProp.arraySize > 0)
                        {
                            var effectNames = new List<string>();
                            for (int i = 0; i < effectsProp.arraySize; i++)
                            {
                                var effectProp = effectsProp.GetArrayElementAtIndex(i);
                                var effectRef = effectProp.FindPropertyRelative("m_EffectID");
                                // Try to read effect name via different property paths
                                var nameProp = effectProp.FindPropertyRelative("m_Name");
                                if (nameProp != null)
                                    effectNames.Add(nameProp.stringValue);
                                else
                                    effectNames.Add($"Effect_{i}");
                            }
                            sb.AppendLine($"| {g.name} | {string.Join(", ", effectNames)} |");
                        }
                    }
                    sb.AppendLine();
                }

                // Snapshots
                var soMixer = new SerializedObject(mixer);
                var snapshotsProp = soMixer.FindProperty("m_Snapshots");
                if (snapshotsProp != null && snapshotsProp.isArray && snapshotsProp.arraySize > 0)
                {
                    sb.AppendLine("### Snapshots");
                    sb.AppendLine("| Name |");
                    sb.AppendLine("| :--- |");
                    for (int i = 0; i < snapshotsProp.arraySize; i++)
                    {
                        var snapProp = snapshotsProp.GetArrayElementAtIndex(i);
                        var snapRef = snapProp.objectReferenceValue;
                        if (snapRef != null)
                            sb.AppendLine($"| {snapRef.name} |");
                    }
                    sb.AppendLine();
                }

                sb.AppendLine("---");
                sb.AppendLine();
            }

            sb.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        }

        static void AppendMixerGroupTree(StringBuilder sb, AudioMixerGroup group, int depth)
        {
            string indent = new string(' ', depth * 2);
            var gso = new SerializedObject(group);
            sb.AppendLine($"{indent}- {group.name}");

            var childrenProp = gso.FindProperty("m_Children");
            if (childrenProp != null && childrenProp.isArray)
            {
                for (int i = 0; i < childrenProp.arraySize; i++)
                {
                    var child = childrenProp.GetArrayElementAtIndex(i).objectReferenceValue as AudioMixerGroup;
                    if (child != null)
                        AppendMixerGroupTree(sb, child, depth + 1);
                }
            }
        }

        // ─── Mode C ─────────────────────────────────────────────────────────────

        static void BuildClipImportAudit(StringBuilder sb)
        {
            sb.AppendLine("# Audio Clip Import Audit");
            sb.AppendLine();

            string[] guids = AssetDatabase.FindAssets("t:AudioClip");
            if (guids.Length == 0)
            {
                sb.AppendLine("_No AudioClip assets found in project._");
                sb.AppendLine();
                sb.AppendLine($"---\nGenerated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                return;
            }

            sb.AppendLine($"## Audio Clip Import Audit ({guids.Length} clips)");
            sb.AppendLine();
            sb.AppendLine("| Clip | Duration | Size | Load Type | Compression | Quality | Sample Rate | Channels |");
            sb.AppendLine("| :--- | :--- | :--- | :--- | :--- | :--- | :--- | :--- |");

            var longDecompressed = new List<string>();
            var stereoOn3D = new List<string>();

            // Build set of clips used on 3D sources for recommendation
            var sources3D = UnityEngine.Object.FindObjectsByType<AudioSource>(FindObjectsSortMode.None)
                .Where(s => s.spatialBlend > 0f && s.clip != null)
                .Select(s => s.clip.name)
                .ToHashSet();

            foreach (string guid in guids.OrderBy(g => AssetDatabase.GUIDToAssetPath(g)))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                if (clip == null) continue;

                var importer = AssetImporter.GetAtPath(path) as AudioImporter;
                if (importer == null) continue;

                var settings = importer.defaultSampleSettings;
                long fileSize = 0;
                try { fileSize = new FileInfo(path).Length; } catch { }

                string sizeFmt = FormatBytes(fileSize);
                string loadType = settings.loadType.ToString();
                string compression = settings.compressionFormat.ToString();
                string quality = settings.compressionFormat == AudioCompressionFormat.PCM ? "—" : $"{settings.quality * 100f:F0}%";
                string sampleRate = settings.sampleRateSetting == AudioSampleRateSetting.PreserveSampleRate
                    ? clip.frequency.ToString()
                    : settings.sampleRateOverride.ToString();
                string channels = clip.channels == 1 ? "Mono" : clip.channels == 2 ? "Stereo" : clip.channels.ToString();

                sb.AppendLine($"| {clip.name} | {clip.length:F1}s | {sizeFmt} | {loadType} | {compression} | {quality} | {sampleRate} | {channels} |");

                if (clip.length > 10f && settings.loadType == AudioClipLoadType.DecompressOnLoad)
                    longDecompressed.Add(clip.name);
                if (clip.channels == 2 && sources3D.Contains(clip.name))
                    stereoOn3D.Add(clip.name);
            }

            sb.AppendLine();

            if (longDecompressed.Count > 0 || stereoOn3D.Count > 0)
            {
                sb.AppendLine("### Recommendations");
                if (longDecompressed.Count > 0)
                    sb.AppendLine($"- {longDecompressed.Count} clip(s) > 10s use \"DecompressOnLoad\" — consider switching to \"Streaming\" to save memory: {string.Join(", ", longDecompressed)}");
                if (stereoOn3D.Count > 0)
                    sb.AppendLine($"- {stereoOn3D.Count} clip(s) are Stereo but attached to 3D AudioSources — consider Mono to halve memory: {string.Join(", ", stereoOn3D)}");
                sb.AppendLine();
            }

            sb.AppendLine($"---\nGenerated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        }

        // ─── Mode D ─────────────────────────────────────────────────────────────

        static void BuildSpatialAudioMap(StringBuilder sb)
        {
            sb.AppendLine("# Spatial Audio Map");
            sb.AppendLine();

            var allSources = UnityEngine.Object.FindObjectsByType<AudioSource>(FindObjectsSortMode.None);
            var spatialSources = allSources.Where(s => s.spatialBlend > 0f).ToArray();

            if (spatialSources.Length == 0)
            {
                sb.AppendLine("_No 3D (spatial) AudioSources found in scene._");
                sb.AppendLine();
                sb.AppendLine($"---\nGenerated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                return;
            }

            sb.AppendLine($"## Spatial Audio Sources ({spatialSources.Length})");
            sb.AppendLine();
            sb.AppendLine("| Object | Clip | Min Dist | Max Dist | Rolloff | Doppler | Spread |");
            sb.AppendLine("| :--- | :--- | :--- | :--- | :--- | :--- | :--- |");

            foreach (var src in spatialSources.OrderBy(s => GetPath(s.gameObject)))
            {
                string path = GetPath(src.gameObject);
                string clip = src.clip != null ? src.clip.name : "— (runtime)";
                string rolloff = src.rolloffMode switch
                {
                    AudioRolloffMode.Logarithmic => "Logarithmic",
                    AudioRolloffMode.Linear => "Linear",
                    AudioRolloffMode.Custom => "Custom",
                    _ => src.rolloffMode.ToString()
                };
                sb.AppendLine($"| {path} | {clip} | {src.minDistance:F1} | {src.maxDistance:F1} | {rolloff} | {src.dopplerLevel:F1} | {src.spread:F0}° |");
            }

            sb.AppendLine();
            sb.AppendLine($"---\nGenerated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        }

        // ─── Helpers ─────────────────────────────────────────────────────────────

        static string GetPath(GameObject go)
        {
            var parts = new List<string>();
            var t = go.transform;
            while (t != null)
            {
                parts.Insert(0, t.name);
                t = t.parent;
            }
            return string.Join("/", parts);
        }

        static string YN(bool v) => v ? "Yes" : "No";

        static string FormatBytes(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            return $"{bytes / (1024.0 * 1024.0):F1} MB";
        }
    }
}
