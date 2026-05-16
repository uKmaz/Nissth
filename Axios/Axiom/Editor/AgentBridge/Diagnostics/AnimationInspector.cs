using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using Axiom.Editor.AgentBridge.Core;
using Object = UnityEngine.Object;

namespace Axiom.Editor.AgentBridge.Diagnostics
{
    /// <summary>
    /// Inspects Animator and Animation components: controller overview, state machines,
    /// animation events, clip property bindings, avatar/bone mapping, and pool status.
    /// </summary>
    public static class AnimationInspector
    {
        public enum AnimationInspectorMode
        {
            ControllerOverview, // Mode A
            StateMachineMap,    // Mode B
            AnimationEvents,    // Mode C
            ClipPropertyAudit,  // Mode D
            AvatarBoneReport,   // Mode E
            AnimatorPoolStatus  // Mode F
        }

        // ─────────────────────────────────────────────────────
        //  Public Entry Point
        // ─────────────────────────────────────────────────────

        public static string GenerateReport(AnimationInspectorMode mode)
        {
            var sb = new StringBuilder();
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

            switch (mode)
            {
                case AnimationInspectorMode.ControllerOverview:
                    BuildControllerOverview(sb, timestamp);
                    return OutputWriter.WriteReport("animation_inspector_overview", sb.ToString());
                case AnimationInspectorMode.StateMachineMap:
                    BuildStateMachineMap(sb, timestamp);
                    return OutputWriter.WriteReport("animation_inspector_statemachine", sb.ToString());
                case AnimationInspectorMode.AnimationEvents:
                    BuildAnimationEvents(sb, timestamp);
                    return OutputWriter.WriteReport("animation_inspector_events", sb.ToString());
                case AnimationInspectorMode.ClipPropertyAudit:
                    BuildClipPropertyAudit(sb, timestamp);
                    return OutputWriter.WriteReport("animation_inspector_clipaudit", sb.ToString());
                case AnimationInspectorMode.AvatarBoneReport:
                    BuildAvatarBoneReport(sb, timestamp);
                    return OutputWriter.WriteReport("animation_inspector_avatar", sb.ToString());
                case AnimationInspectorMode.AnimatorPoolStatus:
                    BuildAnimatorPoolStatus(sb, timestamp);
                    return OutputWriter.WriteReport("animation_inspector_pool", sb.ToString());
                default:
                    return string.Empty;
            }
        }

        // ─────────────────────────────────────────────────────
        //  Mode A: Controller Overview
        // ─────────────────────────────────────────────────────

        private static void BuildControllerOverview(StringBuilder sb, string timestamp)
        {
            sb.AppendLine("# Animation Inspector — Mode A: Controller Overview");
            sb.AppendLine();

            var animators = Object.FindObjectsByType<Animator>(FindObjectsSortMode.None);
            var legacyAnimations = Object.FindObjectsByType<Animation>(FindObjectsSortMode.None);

            sb.AppendLine($"## Mecanim Animators ({animators.Length})");
            sb.AppendLine();

            if (animators.Length == 0)
            {
                sb.AppendLine("_No Animator components found in scene._");
            }
            else
            {
                sb.AppendLine("| Object | Controller | Layers | States | Parameters |");
                sb.AppendLine("| :--- | :--- | :--- | :--- | :--- |");

                int totalStates = 0;
                int totalParams = 0;
                var controllerSet = new HashSet<string>();

                foreach (var anim in animators.OrderBy(a => GetPath(a.gameObject)))
                {
                    string path = GetPath(anim.gameObject);
                    var runtimeCtrl = anim.runtimeAnimatorController;
                    if (runtimeCtrl == null)
                    {
                        sb.AppendLine($"| {path} | — | — | — | — |");
                        continue;
                    }

                    var ctrl = runtimeCtrl as AnimatorController;
                    if (ctrl == null)
                    {
                        // Could be AnimatorOverrideController
                        sb.AppendLine($"| {path} | {runtimeCtrl.name} (Override) | — | — | — |");
                        controllerSet.Add(runtimeCtrl.name);
                        continue;
                    }

                    controllerSet.Add(ctrl.name);
                    int layerCount = ctrl.layers.Length;
                    int stateCount = CountAllStates(ctrl);
                    totalStates += stateCount;
                    totalParams += ctrl.parameters.Length;

                    var paramList = ctrl.parameters
                        .Select(p => $"{p.name}({p.type})")
                        .ToList();
                    string paramStr = paramList.Count > 0 ? string.Join(", ", paramList) : "—";

                    sb.AppendLine($"| {path} | {ctrl.name} | {layerCount} | {stateCount} | {paramStr} |");
                }

                sb.AppendLine();
                sb.AppendLine($"**Total:** {animators.Length} Animators, {controllerSet.Count} unique controllers, {totalStates} states, {totalParams} parameters");
            }

            sb.AppendLine();
            sb.AppendLine($"## Legacy Animation Components ({legacyAnimations.Length})");
            sb.AppendLine();
            if (legacyAnimations.Length == 0)
            {
                sb.AppendLine("_No legacy Animation components found._");
            }
            else
            {
                sb.AppendLine("| Object | Default Clip | Clip Count |");
                sb.AppendLine("| :--- | :--- | :--- |");
                foreach (var la in legacyAnimations.OrderBy(a => GetPath(a.gameObject)))
                {
                    string path = GetPath(la.gameObject);
                    string defaultClip = la.clip != null ? la.clip.name : "—";
                    int clipCount = la.GetClipCount();
                    sb.AppendLine($"| {path} | {defaultClip} | {clipCount} |");
                }
            }

            sb.AppendLine();
            sb.AppendLine($"---");
            sb.AppendLine($"Generated: {timestamp}");
        }

        private static int CountAllStates(AnimatorController ctrl)
        {
            int count = 0;
            foreach (var layer in ctrl.layers)
                count += CountStatesInMachine(layer.stateMachine);
            return count;
        }

        private static int CountStatesInMachine(AnimatorStateMachine machine)
        {
            if (machine == null) return 0;
            int count = machine.states.Length;
            foreach (var sub in machine.stateMachines)
                count += CountStatesInMachine(sub.stateMachine);
            return count;
        }

        // ─────────────────────────────────────────────────────
        //  Mode B: State Machine Map
        // ─────────────────────────────────────────────────────

        private static void BuildStateMachineMap(StringBuilder sb, string timestamp)
        {
            sb.AppendLine("# Animation Inspector — Mode B: State Machine Map");
            sb.AppendLine();

            var animators = Object.FindObjectsByType<Animator>(FindObjectsSortMode.None);

            if (animators.Length == 0)
            {
                sb.AppendLine("_No Animator components found in scene._");
                sb.AppendLine();
                sb.AppendLine($"---");
                sb.AppendLine($"Generated: {timestamp}");
                return;
            }

            // Track which controllers we've already documented (avoid duplicates)
            var processed = new HashSet<string>();

            foreach (var anim in animators.OrderBy(a => GetPath(a.gameObject)))
            {
                var runtimeCtrl = anim.runtimeAnimatorController;
                if (runtimeCtrl == null) continue;

                var ctrl = runtimeCtrl as AnimatorController;
                if (ctrl == null) continue;

                if (processed.Contains(ctrl.name)) continue;
                processed.Add(ctrl.name);

                sb.AppendLine($"## Controller: {ctrl.name}");
                sb.AppendLine($"_Asset: {UnityEditor.AssetDatabase.GetAssetPath(ctrl)}_");
                sb.AppendLine();

                for (int li = 0; li < ctrl.layers.Length; li++)
                {
                    var layer = ctrl.layers[li];
                    sb.AppendLine($"### Layer {li}: {layer.name} (Weight: {layer.defaultWeight:F2})");
                    sb.AppendLine();

                    AppendStateMachineDetails(sb, layer.stateMachine, indent: "");
                    sb.AppendLine();
                }
            }

            sb.AppendLine($"---");
            sb.AppendLine($"Generated: {timestamp}");
        }

        private static void AppendStateMachineDetails(StringBuilder sb, AnimatorStateMachine machine, string indent)
        {
            if (machine == null) return;

            // Default state
            if (machine.defaultState != null)
                sb.AppendLine($"{indent}_Default State: **{machine.defaultState.name}**_");

            // States table
            if (machine.states.Length > 0)
            {
                sb.AppendLine($"{indent}| State | Motion | Speed | Transitions To |");
                sb.AppendLine($"{indent}| :--- | :--- | :--- | :--- |");

                foreach (var child in machine.states)
                {
                    var state = child.state;
                    string motionDesc = GetMotionDescription(state.motion);
                    string transitions = string.Join(", ", state.transitions
                        .Where(t => t.destinationState != null)
                        .Select(t => $"→ {t.destinationState.name} [{GetConditionSummary(t)}]")
                        .Take(4));
                    if (state.transitions.Length > 4)
                        transitions += $", (+{state.transitions.Length - 4} more)";
                    if (string.IsNullOrEmpty(transitions)) transitions = "—";

                    sb.AppendLine($"{indent}| {state.name} | {motionDesc} | {state.speed:F2} | {transitions} |");
                }
                sb.AppendLine();
            }
            else
            {
                sb.AppendLine($"{indent}_No states in this state machine._");
                sb.AppendLine();
            }

            // AnyState transitions
            if (machine.anyStateTransitions.Length > 0)
            {
                sb.AppendLine($"{indent}**Any State Transitions:**");
                sb.AppendLine($"{indent}| → Target | Conditions |");
                sb.AppendLine($"{indent}| :--- | :--- |");
                foreach (var t in machine.anyStateTransitions.Where(t => t.destinationState != null))
                    sb.AppendLine($"{indent}| → {t.destinationState.name} | {GetConditionSummary(t)} |");
                sb.AppendLine();
            }

            // Sub-state machines
            foreach (var sub in machine.stateMachines)
            {
                sb.AppendLine($"{indent}#### Sub-State Machine: {sub.stateMachine.name}");
                AppendStateMachineDetails(sb, sub.stateMachine, indent + "  ");
            }

            // Blend trees
            foreach (var child in machine.states)
            {
                if (child.state.motion is BlendTree bt)
                {
                    sb.AppendLine($"{indent}**Blend Tree: {bt.name}** (Type: {bt.blendType}, Param: {bt.blendParameter})");
                    sb.AppendLine($"{indent}| Threshold | Motion | Duration |");
                    sb.AppendLine($"{indent}| :--- | :--- | :--- |");
                    foreach (var btChild in bt.children)
                    {
                        string childMotion = btChild.motion != null ? btChild.motion.name : "—";
                        float duration = btChild.motion is AnimationClip ac ? ac.length : 0f;
                        sb.AppendLine($"{indent}| {btChild.threshold:F2} | {childMotion} | {duration:F2}s |");
                    }
                    sb.AppendLine();
                }
            }
        }

        private static string GetMotionDescription(Motion motion)
        {
            if (motion == null) return "— (empty)";
            if (motion is AnimationClip clip)
                return $"{clip.name} (clip, {clip.length:F2}s)";
            if (motion is BlendTree bt)
                return $"{bt.name} (BlendTree)";
            return motion.name;
        }

        private static string GetConditionSummary(AnimatorTransitionBase t)
        {
            if (t.conditions.Length == 0)
            {
                var st = t as AnimatorStateTransition;
                if (st != null && st.hasExitTime)
                    return $"ExitTime {st.exitTime:F2}";
                return "—";
            }
            return string.Join(", ", t.conditions.Select(c =>
            {
                switch (c.mode)
                {
                    case AnimatorConditionMode.If: return c.parameter;
                    case AnimatorConditionMode.IfNot: return $"!{c.parameter}";
                    case AnimatorConditionMode.Greater: return $"{c.parameter} > {c.threshold:F2}";
                    case AnimatorConditionMode.Less: return $"{c.parameter} < {c.threshold:F2}";
                    case AnimatorConditionMode.Equals: return $"{c.parameter} = {c.threshold:F0}";
                    case AnimatorConditionMode.NotEqual: return $"{c.parameter} != {c.threshold:F0}";
                    default: return c.parameter;
                }
            }));
        }

        // ─────────────────────────────────────────────────────
        //  Mode C: Animation Events
        // ─────────────────────────────────────────────────────

        private static void BuildAnimationEvents(StringBuilder sb, string timestamp)
        {
            sb.AppendLine("# Animation Inspector — Mode C: Animation Events");
            sb.AppendLine();

            var clips = CollectAllClips();

            if (clips.Count == 0)
            {
                sb.AppendLine("_No animation clips found in scene controllers._");
                sb.AppendLine();
                sb.AppendLine($"---");
                sb.AppendLine($"Generated: {timestamp}");
                return;
            }

            int totalEvents = 0;
            int clipsWithEvents = 0;

            foreach (var clip in clips.OrderBy(c => c.name))
            {
                var events = AnimationUtility.GetAnimationEvents(clip);
                if (events.Length == 0) continue;

                clipsWithEvents++;
                totalEvents += events.Length;

                sb.AppendLine($"### {clip.name} ({clip.length:F2}s)");
                sb.AppendLine("| Time | Function | Parameter |");
                sb.AppendLine("| :--- | :--- | :--- |");
                foreach (var evt in events.OrderBy(e => e.time))
                {
                    string param = "—";
                    if (!string.IsNullOrEmpty(evt.stringParameter)) param = $"string: \"{evt.stringParameter}\"";
                    else if (evt.intParameter != 0) param = $"int: {evt.intParameter}";
                    else if (evt.floatParameter != 0) param = $"float: {evt.floatParameter:F2}";
                    else if (evt.objectReferenceParameter != null) param = $"obj: {evt.objectReferenceParameter.name}";

                    sb.AppendLine($"| {evt.time:F3} | {evt.functionName} | {param} |");
                }
                sb.AppendLine();
            }

            if (clipsWithEvents == 0)
            {
                sb.AppendLine($"_Scanned {clips.Count} clips — no animation events found._");
            }
            else
            {
                sb.AppendLine($"**Summary:** {totalEvents} events across {clipsWithEvents}/{clips.Count} clips");
            }

            sb.AppendLine();
            sb.AppendLine($"---");
            sb.AppendLine($"Generated: {timestamp}");
        }

        // ─────────────────────────────────────────────────────
        //  Mode D: Clip Property Audit
        // ─────────────────────────────────────────────────────

        private static void BuildClipPropertyAudit(StringBuilder sb, string timestamp)
        {
            sb.AppendLine("# Animation Inspector — Mode D: Clip Property Audit");
            sb.AppendLine();

            var animators = Object.FindObjectsByType<Animator>(FindObjectsSortMode.None);

            if (animators.Length == 0)
            {
                sb.AppendLine("_No Animators found. Cannot validate clip bindings without scene context._");
                sb.AppendLine();
                sb.AppendLine($"---");
                sb.AppendLine($"Generated: {timestamp}");
                return;
            }

            // Build animator → clips mapping
            var animatorClips = new Dictionary<Animator, HashSet<AnimationClip>>();
            foreach (var anim in animators)
            {
                var clips = new HashSet<AnimationClip>();
                var runtimeCtrl = anim.runtimeAnimatorController;
                if (runtimeCtrl != null)
                {
                    foreach (var clip in runtimeCtrl.animationClips)
                        if (clip != null) clips.Add(clip);
                }
                animatorClips[anim] = clips;
            }

            int totalBindings = 0;
            int missingBindings = 0;

            foreach (var kv in animatorClips)
            {
                var anim = kv.Key;
                var clips = kv.Value;
                if (clips.Count == 0) continue;

                string animPath = GetPath(anim.gameObject);
                sb.AppendLine($"## Animator: {animPath}");
                sb.AppendLine();

                foreach (var clip in clips.OrderBy(c => c.name))
                {
                    var curveBind = AnimationUtility.GetCurveBindings(clip);
                    var objRefBind = AnimationUtility.GetObjectReferenceCurveBindings(clip);
                    var allBindings = curveBind.Concat(objRefBind).ToArray();

                    if (allBindings.Length == 0)
                    {
                        sb.AppendLine($"### {clip.name} — no curve bindings");
                        sb.AppendLine();
                        continue;
                    }

                    sb.AppendLine($"### {clip.name} (bound to: {animPath})");
                    sb.AppendLine("| Path | Property | Type | Valid? |");
                    sb.AppendLine("| :--- | :--- | :--- | :--- |");

                    foreach (var binding in allBindings)
                    {
                        totalBindings++;
                        string displayPath = string.IsNullOrEmpty(binding.path) ? "(root)" : binding.path;
                        string typeName = binding.type != null ? binding.type.Name : "Unknown";
                        string valid = "✓";

                        // Validate path
                        if (!string.IsNullOrEmpty(binding.path))
                        {
                            var resolved = anim.transform.Find(binding.path);
                            if (resolved == null)
                            {
                                valid = $"✗ MISSING — no child \"{binding.path}\"";
                                missingBindings++;
                            }
                            else if (binding.type != null && resolved.GetComponent(binding.type) == null)
                            {
                                valid = $"✗ NO COMPONENT — {typeName} not on \"{binding.path}\"";
                                missingBindings++;
                            }
                        }

                        sb.AppendLine($"| {displayPath} | {binding.propertyName} | {typeName} | {valid} |");
                    }
                    sb.AppendLine();
                }
            }

            sb.AppendLine($"**Summary:** {totalBindings} total bindings, {missingBindings} missing/invalid");
            sb.AppendLine();
            sb.AppendLine($"---");
            sb.AppendLine($"Generated: {timestamp}");
        }

        // ─────────────────────────────────────────────────────
        //  Mode E: Avatar/Bone Report
        // ─────────────────────────────────────────────────────

        private static void BuildAvatarBoneReport(StringBuilder sb, string timestamp)
        {
            sb.AppendLine("# Animation Inspector — Mode E: Avatar/Bone Report");
            sb.AppendLine();

            var animators = Object.FindObjectsByType<Animator>(FindObjectsSortMode.None)
                .Where(a => a.avatar != null)
                .OrderBy(a => GetPath(a.gameObject))
                .ToArray();

            if (animators.Length == 0)
            {
                sb.AppendLine("_No Animators with Avatar assets found in scene._");
                sb.AppendLine();
                sb.AppendLine($"---");
                sb.AppendLine($"Generated: {timestamp}");
                return;
            }

            foreach (var anim in animators)
            {
                var avatar = anim.avatar;
                string path = GetPath(anim.gameObject);

                sb.AppendLine($"## Avatar: {avatar.name} (on {path})");
                sb.AppendLine($"- Is Valid: {avatar.isValid}");
                sb.AppendLine($"- Is Human: {avatar.isHuman}");
                sb.AppendLine();

                if (avatar.isHuman)
                {
                    sb.AppendLine("### Human Bone Mapping");
                    sb.AppendLine("| Bone | Transform | Status |");
                    sb.AppendLine("| :--- | :--- | :--- |");

                    int mapped = 0;
                    int total = 0;
                    var humanBones = (HumanBodyBones[])Enum.GetValues(typeof(HumanBodyBones));

                    foreach (var bone in humanBones)
                    {
                        if (bone == HumanBodyBones.LastBone) continue;
                        total++;
                        var t = anim.GetBoneTransform(bone);
                        if (t != null)
                        {
                            mapped++;
                            sb.AppendLine($"| {bone} | {t.name} | ✓ |");
                        }
                        else
                        {
                            sb.AppendLine($"| {bone} | — | ✗ UNMAPPED |");
                        }
                    }

                    sb.AppendLine();
                    sb.AppendLine($"**Mapped:** {mapped}/{total} bones");
                    sb.AppendLine();

                    // Avatar description from SerializedObject
                    string avatarPath = AssetDatabase.GetAssetPath(avatar);
                    if (!string.IsNullOrEmpty(avatarPath))
                    {
                        sb.AppendLine($"- Avatar Asset: {avatarPath}");
                        sb.AppendLine($"- Apply Root Motion: {anim.applyRootMotion}");
                        sb.AppendLine($"- Has Root Motion: {anim.hasRootMotion}");
                    }
                }
                else
                {
                    sb.AppendLine("_Non-humanoid (Generic) avatar — no bone mapping available._");
                    sb.AppendLine($"- Apply Root Motion: {anim.applyRootMotion}");
                }
                sb.AppendLine();
            }

            sb.AppendLine($"---");
            sb.AppendLine($"Generated: {timestamp}");
        }

        // ─────────────────────────────────────────────────────
        //  Mode F: Animator Pool Status
        // ─────────────────────────────────────────────────────

        private static void BuildAnimatorPoolStatus(StringBuilder sb, string timestamp)
        {
            sb.AppendLine("# Animation Inspector — Mode F: Animator Pool Status");
            sb.AppendLine();

            var animators = Object.FindObjectsByType<Animator>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);

            if (animators.Length == 0)
            {
                sb.AppendLine("_No Animator components found (including inactive)._");
                sb.AppendLine();
                sb.AppendLine($"---");
                sb.AppendLine($"Generated: {timestamp}");
                return;
            }

            sb.AppendLine("| Object | Controller | Enabled | CullingMode | UpdateMode | HasRootMotion | KeepState | ApplyRootMotion |");
            sb.AppendLine("| :--- | :--- | :--- | :--- | :--- | :--- | :--- | :--- |");

            var controllerNames = new HashSet<string>();
            int alwaysAnimate = 0, cullUpdate = 0, cullCompletely = 0, disabled = 0;

            foreach (var anim in animators.OrderBy(a => GetPath(a.gameObject)))
            {
                string path = GetPath(anim.gameObject);
                string ctrlName = anim.runtimeAnimatorController != null ? anim.runtimeAnimatorController.name : "—";
                if (ctrlName != "—") controllerNames.Add(ctrlName);

                bool enabled = anim.enabled && anim.gameObject.activeInHierarchy;
                if (!enabled) disabled++;

                switch (anim.cullingMode)
                {
                    case AnimatorCullingMode.AlwaysAnimate: alwaysAnimate++; break;
                    case AnimatorCullingMode.CullUpdateTransforms: cullUpdate++; break;
                    case AnimatorCullingMode.CullCompletely: cullCompletely++; break;
                }

                sb.AppendLine($"| {path} | {ctrlName} | {(anim.enabled ? "Yes" : "No")} | {anim.cullingMode} | {anim.updateMode} | {(anim.hasRootMotion ? "Yes" : "No")} | {(anim.keepAnimatorStateOnDisable ? "Yes" : "No")} | {(anim.applyRootMotion ? "Yes" : "No")} |");
            }

            sb.AppendLine();
            sb.AppendLine($"**Total:** {animators.Length} Animators, {controllerNames.Count} unique controllers");
            sb.AppendLine($"- AlwaysAnimate: {alwaysAnimate}{(alwaysAnimate > 0 ? " (consider CullUpdateTransforms for off-screen objects)" : "")}");
            sb.AppendLine($"- CullUpdateTransforms: {cullUpdate}");
            sb.AppendLine($"- CullCompletely: {cullCompletely} (good — fully culled when invisible)");
            sb.AppendLine($"- Disabled: {disabled}{(disabled > 0 ? " (candidates for pool recycling)" : "")}");

            sb.AppendLine();
            sb.AppendLine($"---");
            sb.AppendLine($"Generated: {timestamp}");
        }

        // ─────────────────────────────────────────────────────
        //  Utilities
        // ─────────────────────────────────────────────────────

        private static HashSet<AnimationClip> CollectAllClips()
        {
            var clips = new HashSet<AnimationClip>();
            var animators = Object.FindObjectsByType<Animator>(FindObjectsSortMode.None);
            foreach (var anim in animators)
            {
                if (anim.runtimeAnimatorController == null) continue;
                foreach (var clip in anim.runtimeAnimatorController.animationClips)
                    if (clip != null) clips.Add(clip);
            }
            var legacyAnims = Object.FindObjectsByType<Animation>(FindObjectsSortMode.None);
            foreach (var la in legacyAnims)
            {
                foreach (AnimationState state in la)
                    if (state.clip != null) clips.Add(state.clip);
            }
            return clips;
        }

        private static string GetPath(GameObject go)
        {
            if (go == null) return "—";
            var parts = new List<string>();
            var t = go.transform;
            while (t != null)
            {
                parts.Insert(0, t.name);
                t = t.parent;
            }
            return string.Join("/", parts);
        }

        // ─────────────────────────────────────────────────────
        //  Menu Items
        // ─────────────────────────────────────────────────────

        [MenuItem("Axiom/AgentBridge/Animation Inspector — Mode A (Controller Overview)")]
        public static void MenuModeA() => GenerateReport(AnimationInspectorMode.ControllerOverview);

        [MenuItem("Axiom/AgentBridge/Animation Inspector — Mode B (State Machine Map)")]
        public static void MenuModeB() => GenerateReport(AnimationInspectorMode.StateMachineMap);

        [MenuItem("Axiom/AgentBridge/Animation Inspector — Mode C (Animation Events)")]
        public static void MenuModeC() => GenerateReport(AnimationInspectorMode.AnimationEvents);

        [MenuItem("Axiom/AgentBridge/Animation Inspector — Mode D (Clip Property Audit)")]
        public static void MenuModeD() => GenerateReport(AnimationInspectorMode.ClipPropertyAudit);

        [MenuItem("Axiom/AgentBridge/Animation Inspector — Mode E (Avatar Bone Report)")]
        public static void MenuModeE() => GenerateReport(AnimationInspectorMode.AvatarBoneReport);

        [MenuItem("Axiom/AgentBridge/Animation Inspector — Mode F (Animator Pool Status)")]
        public static void MenuModeF() => GenerateReport(AnimationInspectorMode.AnimatorPoolStatus);
    }
}
