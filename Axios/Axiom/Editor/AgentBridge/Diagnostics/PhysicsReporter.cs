using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEditor;
using Axiom.Editor.AgentBridge.Core;
using Object = UnityEngine.Object;

namespace Axiom.Editor.AgentBridge.Diagnostics
{
    /// <summary>
    /// Reports on physics configuration: colliders, layer matrix, rigidbodies, triggers, joints, and global settings.
    /// </summary>
    public static class PhysicsReporter
    {
        public enum PhysicsReporterMode
        {
            ColliderCensus,       // Mode A
            LayerCollisionMatrix, // Mode B
            RigidbodyReport,      // Mode C
            TriggerMap,           // Mode D
            JointReport,          // Mode E
            PhysicsSettings,      // Mode F
            DeterminismCheck2D    // Mode G
        }

        private static readonly string[] TriggerCallbacks3D = {
            "OnTriggerEnter", "OnTriggerExit", "OnTriggerStay"
        };
        private static readonly string[] TriggerCallbacks2D = {
            "OnTriggerEnter2D", "OnTriggerExit2D", "OnTriggerStay2D"
        };
        private static readonly string[] CollisionCallbacks3D = {
            "OnCollisionEnter", "OnCollisionExit", "OnCollisionStay"
        };

        // ─────────────────────────────────────────────────────
        //  Public Entry Point
        // ─────────────────────────────────────────────────────

        public static string GenerateReport(PhysicsReporterMode mode)
        {
            var sb = new StringBuilder();
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

            switch (mode)
            {
                case PhysicsReporterMode.ColliderCensus:
                    BuildColliderCensus(sb, timestamp);
                    return OutputWriter.WriteReport("physics_reporter_colliders", sb.ToString());
                case PhysicsReporterMode.LayerCollisionMatrix:
                    BuildLayerCollisionMatrix(sb, timestamp);
                    return OutputWriter.WriteReport("physics_reporter_layermatrix", sb.ToString());
                case PhysicsReporterMode.RigidbodyReport:
                    BuildRigidbodyReport(sb, timestamp);
                    return OutputWriter.WriteReport("physics_reporter_rigidbodies", sb.ToString());
                case PhysicsReporterMode.TriggerMap:
                    BuildTriggerMap(sb, timestamp);
                    return OutputWriter.WriteReport("physics_reporter_triggers", sb.ToString());
                case PhysicsReporterMode.JointReport:
                    BuildJointReport(sb, timestamp);
                    return OutputWriter.WriteReport("physics_reporter_joints", sb.ToString());
                case PhysicsReporterMode.PhysicsSettings:
                    BuildPhysicsSettings(sb, timestamp);
                    return OutputWriter.WriteReport("physics_reporter_settings", sb.ToString());
                case PhysicsReporterMode.DeterminismCheck2D:
                    return GenerateDeterminismCheck2D();
                default:
                    return string.Empty;
            }
        }

        // ─────────────────────────────────────────────────────
        //  Mode A: Collider Census
        // ─────────────────────────────────────────────────────

        private static void BuildColliderCensus(StringBuilder sb, string timestamp)
        {
            sb.AppendLine("# Physics Reporter — Mode A: Collider Census");
            sb.AppendLine();

            var colliders3D = Object.FindObjectsByType<Collider>(FindObjectsSortMode.None);
            var colliders2D = Object.FindObjectsByType<Collider2D>(FindObjectsSortMode.None);

            // 3D colliders
            sb.AppendLine($"## 3D Colliders ({colliders3D.Length} total)");
            sb.AppendLine();
            if (colliders3D.Length == 0)
            {
                sb.AppendLine("_No 3D colliders found in scene._");
            }
            else
            {
                // Group by type
                var byType = colliders3D
                    .GroupBy(c => c.GetType().Name)
                    .OrderByDescending(g => g.Count())
                    .ToList();

                sb.AppendLine("| Type | Count | Layers |");
                sb.AppendLine("| :--- | :--- | :--- |");
                foreach (var grp in byType)
                {
                    var layerGroups = grp
                        .GroupBy(c => LayerMask.LayerToName(c.gameObject.layer))
                        .Select(lg => $"{lg.Key}({lg.Count()})")
                        .ToList();
                    sb.AppendLine($"| {grp.Key} | {grp.Count()} | {string.Join(", ", layerGroups)} |");
                }

                int triggerCount = colliders3D.Count(c => c.isTrigger);
                int solidCount = colliders3D.Length - triggerCount;
                sb.AppendLine();
                sb.AppendLine("### Trigger vs Solid");
                sb.AppendLine($"- Triggers: {triggerCount} ({(colliders3D.Length > 0 ? 100f * triggerCount / colliders3D.Length : 0):F1}%)");
                sb.AppendLine($"- Solid: {solidCount} ({(colliders3D.Length > 0 ? 100f * solidCount / colliders3D.Length : 0):F1}%)");
            }

            sb.AppendLine();

            // 2D colliders
            sb.AppendLine($"## 2D Colliders ({colliders2D.Length} total)");
            sb.AppendLine();
            if (colliders2D.Length == 0)
            {
                sb.AppendLine("_No 2D colliders found in scene._");
            }
            else
            {
                var byType2D = colliders2D
                    .GroupBy(c => c.GetType().Name)
                    .OrderByDescending(g => g.Count())
                    .ToList();

                sb.AppendLine("| Type | Count | Layers |");
                sb.AppendLine("| :--- | :--- | :--- |");
                foreach (var grp in byType2D)
                {
                    var layerGroups = grp
                        .GroupBy(c => LayerMask.LayerToName(c.gameObject.layer))
                        .Select(lg => $"{lg.Key}({lg.Count()})")
                        .ToList();
                    sb.AppendLine($"| {grp.Key} | {grp.Count()} | {string.Join(", ", layerGroups)} |");
                }

                int triggerCount2D = colliders2D.Count(c => c.isTrigger);
                int solidCount2D = colliders2D.Length - triggerCount2D;
                sb.AppendLine();
                sb.AppendLine("### Trigger vs Solid (2D)");
                sb.AppendLine($"- Triggers: {triggerCount2D} ({(colliders2D.Length > 0 ? 100f * triggerCount2D / colliders2D.Length : 0):F1}%)");
                sb.AppendLine($"- Solid: {solidCount2D} ({(colliders2D.Length > 0 ? 100f * solidCount2D / colliders2D.Length : 0):F1}%)");
            }

            sb.AppendLine();
            sb.AppendLine($"---");
            sb.AppendLine($"Generated: {timestamp}");
        }

        // ─────────────────────────────────────────────────────
        //  Mode B: Layer Collision Matrix
        // ─────────────────────────────────────────────────────

        private static void BuildLayerCollisionMatrix(StringBuilder sb, string timestamp)
        {
            sb.AppendLine("# Physics Reporter — Mode B: Layer Collision Matrix");
            sb.AppendLine();

            // Determine which layers are in use (have colliders or rigidbodies)
            var colliders3D = Object.FindObjectsByType<Collider>(FindObjectsSortMode.None);
            var colliders2D = Object.FindObjectsByType<Collider2D>(FindObjectsSortMode.None);

            var usedLayers3D = new HashSet<int>();
            var usedLayers2D = new HashSet<int>();

            foreach (var c in colliders3D) usedLayers3D.Add(c.gameObject.layer);
            foreach (var c in colliders2D) usedLayers2D.Add(c.gameObject.layer);

            // 3D matrix
            sb.AppendLine("## Layer Collision Matrix (3D)");
            sb.AppendLine();
            if (usedLayers3D.Count == 0)
            {
                sb.AppendLine("_No 3D colliders found. Matrix shows all 32 layers with non-default settings._");
                sb.AppendLine();
                AppendLayerMatrix3D(sb, Enumerable.Range(0, 32).ToList());
            }
            else
            {
                var layers3D = usedLayers3D.OrderBy(l => l).ToList();
                AppendLayerMatrix3D(sb, layers3D);
            }

            sb.AppendLine();

            // 2D matrix
            sb.AppendLine("## Layer Collision Matrix (2D)");
            sb.AppendLine();
            if (usedLayers2D.Count == 0)
            {
                sb.AppendLine("_No 2D colliders found. Showing matrix for layers used by 3D colliders._");
                sb.AppendLine();
                var layers2D = usedLayers3D.Count > 0
                    ? usedLayers3D.OrderBy(l => l).ToList()
                    : Enumerable.Range(0, 8).ToList();
                AppendLayerMatrix2D(sb, layers2D);
            }
            else
            {
                var layers2D = usedLayers2D.OrderBy(l => l).ToList();
                AppendLayerMatrix2D(sb, layers2D);
            }

            sb.AppendLine();
            sb.AppendLine($"---");
            sb.AppendLine($"Generated: {timestamp}");
        }

        private static void AppendLayerMatrix3D(StringBuilder sb, List<int> layers)
        {
            if (layers.Count == 0) return;

            var names = layers.Select(l => {
                string n = LayerMask.LayerToName(l);
                return string.IsNullOrEmpty(n) ? $"Layer{l}" : n;
            }).ToList();

            // Header
            sb.Append("|  |");
            foreach (var n in names) sb.Append($" {n} |");
            sb.AppendLine();

            sb.Append("| :--- |");
            foreach (var _ in names) sb.Append(" :---: |");
            sb.AppendLine();

            // Rows
            for (int i = 0; i < layers.Count; i++)
            {
                sb.Append($"| {names[i]} |");
                for (int j = 0; j < layers.Count; j++)
                {
                    bool ignored = Physics.GetIgnoreLayerCollision(layers[i], layers[j]);
                    sb.Append(ignored ? " ✗ |" : " ✓ |");
                }
                sb.AppendLine();
            }
        }

        private static void AppendLayerMatrix2D(StringBuilder sb, List<int> layers)
        {
            if (layers.Count == 0) return;

            var names = layers.Select(l => {
                string n = LayerMask.LayerToName(l);
                return string.IsNullOrEmpty(n) ? $"Layer{l}" : n;
            }).ToList();

            sb.Append("|  |");
            foreach (var n in names) sb.Append($" {n} |");
            sb.AppendLine();

            sb.Append("| :--- |");
            foreach (var _ in names) sb.Append(" :---: |");
            sb.AppendLine();

            for (int i = 0; i < layers.Count; i++)
            {
                sb.Append($"| {names[i]} |");
                for (int j = 0; j < layers.Count; j++)
                {
                    bool ignored = Physics2D.GetIgnoreLayerCollision(layers[i], layers[j]);
                    sb.Append(ignored ? " ✗ |" : " ✓ |");
                }
                sb.AppendLine();
            }
        }

        // ─────────────────────────────────────────────────────
        //  Mode C: Rigidbody Report
        // ─────────────────────────────────────────────────────

        private static void BuildRigidbodyReport(StringBuilder sb, string timestamp)
        {
            sb.AppendLine("# Physics Reporter — Mode C: Rigidbody Report");
            sb.AppendLine();

            var rbs = Object.FindObjectsByType<Rigidbody>(FindObjectsSortMode.None);
            var rbs2D = Object.FindObjectsByType<Rigidbody2D>(FindObjectsSortMode.None);

            // 3D Rigidbodies
            sb.AppendLine($"## 3D Rigidbodies ({rbs.Length})");
            sb.AppendLine();
            if (rbs.Length == 0)
            {
                sb.AppendLine("_No 3D Rigidbodies found in scene._");
            }
            else
            {
                sb.AppendLine("| Object | Mass | LinDamping | AngDamping | Kinematic | Gravity | Interpolation | CollDetect | Constraints |");
                sb.AppendLine("| :--- | :--- | :--- | :--- | :--- | :--- | :--- | :--- | :--- |");
                foreach (var rb in rbs.OrderBy(r => GetPath(r.gameObject)))
                {
                    string path = GetPath(rb.gameObject);
                    string constraints = DecodeConstraints(rb.constraints);

                    // Read linear/angular damping via SerializedObject (Unity 6 compatibility)
                    float linearDamping = rb.linearDamping;
                    float angularDamping = rb.angularDamping;

                    sb.AppendLine($"| {path} | {rb.mass:F2} | {linearDamping:F3} | {angularDamping:F3} | {(rb.isKinematic ? "Yes" : "No")} | {(rb.useGravity ? "Yes" : "No")} | {rb.interpolation} | {rb.collisionDetectionMode} | {constraints} |");
                }
            }

            sb.AppendLine();

            // 2D Rigidbodies
            sb.AppendLine($"## 2D Rigidbodies ({rbs2D.Length})");
            sb.AppendLine();
            if (rbs2D.Length == 0)
            {
                sb.AppendLine("_No 2D Rigidbodies found in scene._");
            }
            else
            {
                sb.AppendLine("| Object | Mass | LinearDamping | AngularDamping | BodyType | GravityScale | Simulated | Constraints |");
                sb.AppendLine("| :--- | :--- | :--- | :--- | :--- | :--- | :--- | :--- |");
                foreach (var rb2 in rbs2D.OrderBy(r => GetPath(r.gameObject)))
                {
                    string path = GetPath(rb2.gameObject);
                    string constraints2D = DecodeConstraints2D(rb2.constraints);
                    sb.AppendLine($"| {path} | {rb2.mass:F2} | {rb2.linearDamping:F3} | {rb2.angularDamping:F3} | {rb2.bodyType} | {rb2.gravityScale:F2} | {(rb2.simulated ? "Yes" : "No")} | {constraints2D} |");
                }
            }

            sb.AppendLine();
            sb.AppendLine($"---");
            sb.AppendLine($"Generated: {timestamp}");
        }

        private static string DecodeConstraints(RigidbodyConstraints c)
        {
            if (c == RigidbodyConstraints.None) return "None";
            if (c == RigidbodyConstraints.FreezeAll) return "FreezeAll";

            var parts = new List<string>();
            if ((c & RigidbodyConstraints.FreezePositionX) != 0) parts.Add("FreezeX");
            if ((c & RigidbodyConstraints.FreezePositionY) != 0) parts.Add("FreezeY");
            if ((c & RigidbodyConstraints.FreezePositionZ) != 0) parts.Add("FreezeZ");
            if ((c & RigidbodyConstraints.FreezeRotationX) != 0) parts.Add("FreezeRotX");
            if ((c & RigidbodyConstraints.FreezeRotationY) != 0) parts.Add("FreezeRotY");
            if ((c & RigidbodyConstraints.FreezeRotationZ) != 0) parts.Add("FreezeRotZ");
            return parts.Count > 0 ? string.Join(",", parts) : "None";
        }

        private static string DecodeConstraints2D(RigidbodyConstraints2D c)
        {
            if (c == RigidbodyConstraints2D.None) return "None";
            if (c == RigidbodyConstraints2D.FreezeAll) return "FreezeAll";

            var parts = new List<string>();
            if ((c & RigidbodyConstraints2D.FreezePositionX) != 0) parts.Add("FreezeX");
            if ((c & RigidbodyConstraints2D.FreezePositionY) != 0) parts.Add("FreezeY");
            if ((c & RigidbodyConstraints2D.FreezeRotation) != 0) parts.Add("FreezeRot");
            return parts.Count > 0 ? string.Join(",", parts) : "None";
        }

        // ─────────────────────────────────────────────────────
        //  Mode D: Trigger Map
        // ─────────────────────────────────────────────────────

        private static void BuildTriggerMap(StringBuilder sb, string timestamp)
        {
            sb.AppendLine("# Physics Reporter — Mode D: Trigger Map");
            sb.AppendLine();

            var triggers3D = Object.FindObjectsByType<Collider>(FindObjectsSortMode.None)
                .Where(c => c.isTrigger).ToArray();
            var triggers2D = Object.FindObjectsByType<Collider2D>(FindObjectsSortMode.None)
                .Where(c => c.isTrigger).ToArray();

            sb.AppendLine($"## 3D Triggers ({triggers3D.Length})");
            sb.AppendLine();
            if (triggers3D.Length == 0)
            {
                sb.AppendLine("_No 3D trigger colliders found._");
            }
            else
            {
                sb.AppendLine("| Trigger Object | Collider Type | Callback Scripts |");
                sb.AppendLine("| :--- | :--- | :--- |");
                foreach (var trig in triggers3D.OrderBy(c => GetPath(c.gameObject)))
                {
                    string path = GetPath(trig.gameObject);
                    string callbacks = FindTriggerCallbacks(trig.gameObject, TriggerCallbacks3D, TriggerCallbacks2D);
                    sb.AppendLine($"| {path} | {trig.GetType().Name} | {callbacks} |");
                }
            }

            sb.AppendLine();
            sb.AppendLine($"## 2D Triggers ({triggers2D.Length})");
            sb.AppendLine();
            if (triggers2D.Length == 0)
            {
                sb.AppendLine("_No 2D trigger colliders found._");
            }
            else
            {
                sb.AppendLine("| Trigger Object | Collider Type | Callback Scripts |");
                sb.AppendLine("| :--- | :--- | :--- |");
                foreach (var trig in triggers2D.OrderBy(c => GetPath(c.gameObject)))
                {
                    string path = GetPath(trig.gameObject);
                    string callbacks = FindTriggerCallbacks(trig.gameObject, TriggerCallbacks2D, TriggerCallbacks3D);
                    sb.AppendLine($"| {path} | {trig.GetType().Name} | {callbacks} |");
                }
            }

            sb.AppendLine();
            sb.AppendLine($"---");
            sb.AppendLine($"Generated: {timestamp}");
        }

        private static string FindTriggerCallbacks(GameObject go, string[] primaryCallbacks, string[] secondaryCallbacks)
        {
            var monos = go.GetComponents<MonoBehaviour>();
            if (monos.Length == 0) return "—";

            var results = new List<string>();
            var allCallbacks = primaryCallbacks.Concat(secondaryCallbacks).ToArray();

            foreach (var mono in monos)
            {
                if (mono == null) continue;
                var type = mono.GetType();
                var found = new List<string>();
                foreach (var cb in allCallbacks)
                {
                    var method = type.GetMethod(cb,
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (method != null && method.DeclaringType != typeof(MonoBehaviour))
                        found.Add(cb);
                }
                if (found.Count > 0)
                    results.Add($"{type.Name} ({string.Join(", ", found)})");
            }

            return results.Count > 0 ? string.Join("; ", results) : "— (no handlers)";
        }

        // ─────────────────────────────────────────────────────
        //  Mode E: Joint Report
        // ─────────────────────────────────────────────────────

        private static void BuildJointReport(StringBuilder sb, string timestamp)
        {
            sb.AppendLine("# Physics Reporter — Mode E: Joint Report");
            sb.AppendLine();

            var joints3D = Object.FindObjectsByType<Joint>(FindObjectsSortMode.None);
            var joints2D = Object.FindObjectsByType<Joint2D>(FindObjectsSortMode.None);

            sb.AppendLine($"## 3D Joints ({joints3D.Length})");
            sb.AppendLine();
            if (joints3D.Length == 0)
            {
                sb.AppendLine("_No 3D joints found in scene._");
            }
            else
            {
                sb.AppendLine("| Object | Joint Type | Connected Body | Break Force | Break Torque | Key Settings |");
                sb.AppendLine("| :--- | :--- | :--- | :--- | :--- | :--- |");
                foreach (var j in joints3D.OrderBy(j => GetPath(j.gameObject)))
                {
                    string path = GetPath(j.gameObject);
                    string connectedBody = j.connectedBody != null ? GetPath(j.connectedBody.gameObject) : "world";
                    string breakForce = j.breakForce >= float.MaxValue * 0.9f ? "Infinity" : j.breakForce.ToString("F1");
                    string breakTorque = j.breakTorque >= float.MaxValue * 0.9f ? "Infinity" : j.breakTorque.ToString("F1");
                    string keySettings = GetJointKeySettings(j);
                    sb.AppendLine($"| {path} | {j.GetType().Name} | {connectedBody} | {breakForce} | {breakTorque} | {keySettings} |");
                }
            }

            sb.AppendLine();
            sb.AppendLine($"## 2D Joints ({joints2D.Length})");
            sb.AppendLine();
            if (joints2D.Length == 0)
            {
                sb.AppendLine("_No 2D joints found in scene._");
            }
            else
            {
                sb.AppendLine("| Object | Joint Type | Connected Body | Break Force | Break Torque | Enabled |");
                sb.AppendLine("| :--- | :--- | :--- | :--- | :--- | :--- |");
                foreach (var j2 in joints2D.OrderBy(j => GetPath(j.gameObject)))
                {
                    string path = GetPath(j2.gameObject);
                    string connectedBody = j2.connectedBody != null ? GetPath(j2.connectedBody.gameObject) : "world";
                    string breakForce = j2.breakForce >= float.MaxValue * 0.9f ? "Infinity" : j2.breakForce.ToString("F1");
                    string breakTorque = j2.breakTorque >= float.MaxValue * 0.9f ? "Infinity" : j2.breakTorque.ToString("F1");
                    sb.AppendLine($"| {path} | {j2.GetType().Name} | {connectedBody} | {breakForce} | {breakTorque} | {(j2.enabled ? "Yes" : "No")} |");
                }
            }

            sb.AppendLine();
            sb.AppendLine($"---");
            sb.AppendLine($"Generated: {timestamp}");
        }

        private static string GetJointKeySettings(Joint j)
        {
            if (j is HingeJoint hj)
            {
                string limits = hj.useLimits
                    ? $"Limits: {hj.limits.min:F1}° to {hj.limits.max:F1}°"
                    : "Limits: off";
                string motor = hj.useMotor ? "Motor: on" : "Motor: off";
                return $"{limits}, {motor}";
            }
            if (j is SpringJoint sj)
                return $"Spring: {sj.spring:F1}, Damper: {sj.damper:F1}, MinDist: {sj.minDistance:F2}, MaxDist: {sj.maxDistance:F2}";
            if (j is FixedJoint)
                return "Fixed";
            if (j is ConfigurableJoint cj)
                return $"XMotion: {cj.xMotion}, YMotion: {cj.yMotion}, ZMotion: {cj.zMotion}";
            if (j is CharacterJoint charJ)
                return $"Swing1: {charJ.swing1Limit.limit:F1}°, Swing2: {charJ.swing2Limit.limit:F1}°";
            return j.GetType().Name;
        }

        // ─────────────────────────────────────────────────────
        //  Mode F: Physics Settings Summary
        // ─────────────────────────────────────────────────────

        private static void BuildPhysicsSettings(StringBuilder sb, string timestamp)
        {
            sb.AppendLine("# Physics Reporter — Mode F: Physics Settings Summary");
            sb.AppendLine();

            sb.AppendLine("## Physics Settings (3D)");
            sb.AppendLine($"- Gravity: {Physics.gravity}");
            sb.AppendLine($"- Default Solver Iterations: {Physics.defaultSolverIterations}");
            sb.AppendLine($"- Default Solver Velocity Iterations: {Physics.defaultSolverVelocityIterations}");
            sb.AppendLine($"- Bounce Threshold: {Physics.bounceThreshold}");
            sb.AppendLine($"- Default Max Depenetration Velocity: {Physics.defaultMaxDepenetrationVelocity}");
            sb.AppendLine($"- Default Contact Offset: {Physics.defaultContactOffset}");
            sb.AppendLine($"- Sleep Threshold: {Physics.sleepThreshold}");
            sb.AppendLine($"- Simulation Mode: {Physics.simulationMode}");
            sb.AppendLine($"- Auto Sync Transforms: {Physics.autoSyncTransforms}");
            sb.AppendLine($"- Queries Hit Triggers: {Physics.queriesHitTriggers}");
            sb.AppendLine($"- Queries Hit Backfaces: {Physics.queriesHitBackfaces}");

            sb.AppendLine();
            sb.AppendLine("## Physics Settings (2D)");
            sb.AppendLine($"- Gravity: {Physics2D.gravity}");
            sb.AppendLine($"- Default Contact Offset: {Physics2D.defaultContactOffset}");
            sb.AppendLine($"- Velocity Iterations: {Physics2D.velocityIterations}");
            sb.AppendLine($"- Position Iterations: {Physics2D.positionIterations}");
            sb.AppendLine($"- Simulation Mode: {Physics2D.simulationMode}");
            sb.AppendLine($"- Queries Hit Triggers: {Physics2D.queriesHitTriggers}");
            sb.AppendLine($"- Queries Start In Colliders: {Physics2D.queriesStartInColliders}");

            // Additional info from DynamicsManager (for completeness)
            sb.AppendLine();
            sb.AppendLine("## Additional 3D Physics Info");
            try
            {
                var dynAssets = UnityEditor.AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/DynamicsManager.asset");
                if (dynAssets != null && dynAssets.Length > 0 && dynAssets[0] != null)
                {
                    var so = new SerializedObject(dynAssets[0]);
                    var enableAdaptiveForce = so.FindProperty("m_EnableAdaptiveForce");
                    if (enableAdaptiveForce != null)
                        sb.AppendLine($"- Enable Adaptive Force: {enableAdaptiveForce.boolValue}");
                    var improvedPatchFriction = so.FindProperty("m_ImprovedPatchFriction");
                    if (improvedPatchFriction != null)
                        sb.AppendLine($"- Improved Patch Friction: {improvedPatchFriction.boolValue}");
                    var defaultMaxAngularSpeed = so.FindProperty("m_DefaultMaxAngularSpeed");
                    if (defaultMaxAngularSpeed != null)
                        sb.AppendLine($"- Default Max Angular Speed: {defaultMaxAngularSpeed.floatValue}");
                }
                else
                {
                    sb.AppendLine("_DynamicsManager.asset not accessible._");
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine($"_Could not read DynamicsManager.asset: {ex.Message}_");
            }

            sb.AppendLine();
            sb.AppendLine($"---");
            sb.AppendLine($"Generated: {timestamp}");
        }

        // ─────────────────────────────────────────────────────
        //  Mode G: 2D Determinism Check
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// Mode G: Runs multiple 2D physics simulation steps and compares results
        /// across runs to verify determinism. Unity 6 feature via Physics2D simulation API.
        /// Requires Rigidbody2D objects in the scene to be meaningful.
        /// </summary>
        private static string GenerateDeterminismCheck2D()
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Physics Reporter — Mode G: 2D Determinism Check\n");

            var bodies = Object.FindObjectsByType<Rigidbody2D>(FindObjectsSortMode.None);
            if (bodies.Length == 0)
            {
                sb.AppendLine("No Rigidbody2D objects found in scene. Nothing to test.");
                return OutputWriter.WriteReport("physics_reporter_determinism2d", sb.ToString());
            }

            sb.AppendLine($"Testing determinism with {bodies.Length} Rigidbody2D objects.\n");

            int simSteps = 60;
            int runs = 3;
            float timeStep = Time.fixedDeltaTime;

            var initialStates = new List<(Vector2 pos, float rot)>();
            foreach (var rb in bodies)
                initialStates.Add((rb.position, rb.rotation));

            var runResults = new List<List<(Vector2 pos, float rot)>>();

            for (int run = 0; run < runs; run++)
            {
                for (int i = 0; i < bodies.Length; i++)
                {
                    bodies[i].position = initialStates[i].pos;
                    bodies[i].rotation = initialStates[i].rot;
                    bodies[i].linearVelocity = Vector2.zero;
                    bodies[i].angularVelocity = 0f;
                }

                for (int step = 0; step < simSteps; step++)
                {
                    Physics2D.Simulate(timeStep);
                }

                var finalStates = new List<(Vector2 pos, float rot)>();
                foreach (var rb in bodies)
                    finalStates.Add((rb.position, rb.rotation));

                runResults.Add(finalStates);
            }

            for (int i = 0; i < bodies.Length; i++)
            {
                bodies[i].position = initialStates[i].pos;
                bodies[i].rotation = initialStates[i].rot;
                bodies[i].linearVelocity = Vector2.zero;
                bodies[i].angularVelocity = 0f;
            }

            bool deterministic = true;
            sb.AppendLine($"### Simulation: {simSteps} steps × {runs} runs (dt={timeStep})\n");
            sb.AppendLine("| Body | Run 1 Pos | Run 2 Pos | Run 3 Pos | Match? |");
            sb.AppendLine("| :--- | :--- | :--- | :--- | :--- |");

            for (int i = 0; i < bodies.Length; i++)
            {
                bool match = true;
                for (int run = 1; run < runs; run++)
                {
                    if (runResults[run][i].pos != runResults[0][i].pos ||
                        runResults[run][i].rot != runResults[0][i].rot)
                    {
                        match = false;
                        deterministic = false;
                    }
                }

                sb.AppendLine($"| {bodies[i].gameObject.name} | " +
                    $"({runResults[0][i].pos.x:F6}, {runResults[0][i].pos.y:F6}) | " +
                    $"({runResults[1][i].pos.x:F6}, {runResults[1][i].pos.y:F6}) | " +
                    $"({runResults[2][i].pos.x:F6}, {runResults[2][i].pos.y:F6}) | " +
                    $"{(match ? "✓" : "✗ NONDETERMINISTIC")} |");
            }

            sb.AppendLine();
            sb.AppendLine(deterministic
                ? "### Result: ✓ DETERMINISTIC — All runs produced identical results."
                : "### Result: ✗ NONDETERMINISTIC — Runs diverged. Check Rigidbody2D settings and Physics2D simulation mode.");

            sb.AppendLine();
            sb.AppendLine("**Note:** This test runs `Physics2D.Simulate()` directly in Edit Mode.");
            sb.AppendLine("For full determinism validation, test in Play Mode with your actual gameplay simulation loop.");
            sb.AppendLine("Unity 6's Box2D v3 backend is deterministic when identical inputs and fixed timestep are used.");

            return OutputWriter.WriteReport("physics_reporter_determinism2d", sb.ToString());
        }

        // ─────────────────────────────────────────────────────
        //  Utilities
        // ─────────────────────────────────────────────────────

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

        [MenuItem("Axiom/AgentBridge/Physics Reporter — Mode A (Collider Census)")]
        public static void MenuModeA() => GenerateReport(PhysicsReporterMode.ColliderCensus);

        [MenuItem("Axiom/AgentBridge/Physics Reporter — Mode B (Layer Collision Matrix)")]
        public static void MenuModeB() => GenerateReport(PhysicsReporterMode.LayerCollisionMatrix);

        [MenuItem("Axiom/AgentBridge/Physics Reporter — Mode C (Rigidbody Report)")]
        public static void MenuModeC() => GenerateReport(PhysicsReporterMode.RigidbodyReport);

        [MenuItem("Axiom/AgentBridge/Physics Reporter — Mode D (Trigger Map)")]
        public static void MenuModeD() => GenerateReport(PhysicsReporterMode.TriggerMap);

        [MenuItem("Axiom/AgentBridge/Physics Reporter — Mode E (Joint Report)")]
        public static void MenuModeE() => GenerateReport(PhysicsReporterMode.JointReport);

        [MenuItem("Axiom/AgentBridge/Physics Reporter — Mode F (Physics Settings)")]
        public static void MenuModeF() => GenerateReport(PhysicsReporterMode.PhysicsSettings);

        [MenuItem("Axiom/AgentBridge/Physics Reporter — Mode G (2D Determinism Check)")]
        public static void MenuModeG() => GenerateReport(PhysicsReporterMode.DeterminismCheck2D);
    }
}
