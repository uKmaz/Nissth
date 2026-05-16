using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.AI;
using UnityEditor;
using UnityEditor.AI;
using Axiom.Editor.AgentBridge.Core;

namespace Axiom.Editor.AgentBridge.Diagnostics
{
    public static class NavMeshInspector
    {
        public enum NavMeshInspectorMode { AgentTypes, SurfaceReport, ObstacleReport, LinkReport, ReachabilityTest }

        [MenuItem("Axiom/AgentBridge/NavMesh Inspector — Mode A (Agent Types)")]
        public static void ModeA() => GenerateReport(NavMeshInspectorMode.AgentTypes);

        [MenuItem("Axiom/AgentBridge/NavMesh Inspector — Mode B (Surface Report)")]
        public static void ModeB() => GenerateReport(NavMeshInspectorMode.SurfaceReport);

        [MenuItem("Axiom/AgentBridge/NavMesh Inspector — Mode C (Obstacle Report)")]
        public static void ModeC() => GenerateReport(NavMeshInspectorMode.ObstacleReport);

        [MenuItem("Axiom/AgentBridge/NavMesh Inspector — Mode D (Link Report)")]
        public static void ModeD() => GenerateReport(NavMeshInspectorMode.LinkReport);

        [MenuItem("Axiom/AgentBridge/NavMesh Inspector — Mode E (Reachability Test)")]
        public static void ModeE() => GenerateReport(NavMeshInspectorMode.ReachabilityTest);

        public static string GenerateReport(NavMeshInspectorMode mode)
        {
            var sb = new StringBuilder();

            switch (mode)
            {
                case NavMeshInspectorMode.AgentTypes:
                    BuildAgentTypes(sb);
                    return OutputWriter.WriteReport("navmesh_inspector_agenttypes", sb.ToString());
                case NavMeshInspectorMode.SurfaceReport:
                    BuildSurfaceReport(sb);
                    return OutputWriter.WriteReport("navmesh_inspector_surface", sb.ToString());
                case NavMeshInspectorMode.ObstacleReport:
                    BuildObstacleReport(sb);
                    return OutputWriter.WriteReport("navmesh_inspector_obstacles", sb.ToString());
                case NavMeshInspectorMode.LinkReport:
                    BuildLinkReport(sb);
                    return OutputWriter.WriteReport("navmesh_inspector_links", sb.ToString());
                case NavMeshInspectorMode.ReachabilityTest:
                    BuildReachabilityTest(sb);
                    return OutputWriter.WriteReport("navmesh_inspector_reachability", sb.ToString());
                default:
                    return OutputWriter.WriteReport("navmesh_inspector", "Unknown mode");
            }
        }

        // ─── Mode A ─────────────────────────────────────────────────────────────

        static void BuildAgentTypes(StringBuilder sb)
        {
            sb.AppendLine("# NavMesh Agent Types");
            sb.AppendLine();

            int settingsCount = NavMesh.GetSettingsCount();
            if (settingsCount == 0)
            {
                sb.AppendLine("_No NavMesh agent types defined._");
            }
            else
            {
                sb.AppendLine("| ID | Name | Radius | Height | Step Height | Max Slope |");
                sb.AppendLine("| :--- | :--- | :--- | :--- | :--- | :--- |");
                for (int i = 0; i < settingsCount; i++)
                {
                    var settings = NavMesh.GetSettingsByIndex(i);
                    string name = NavMesh.GetSettingsNameFromID(settings.agentTypeID);
                    sb.AppendLine($"| {settings.agentTypeID} | {name} | {settings.agentRadius:F2} | {settings.agentHeight:F2} | {settings.agentClimb:F2} | {settings.agentSlope:F0}° |");
                }
            }

            sb.AppendLine();

            var agents = UnityEngine.Object.FindObjectsByType<NavMeshAgent>(FindObjectsSortMode.None);
            sb.AppendLine($"## NavMesh Agents in Scene ({agents.Length})");
            sb.AppendLine();

            if (agents.Length == 0)
            {
                sb.AppendLine("_No NavMeshAgent components found in scene._");
            }
            else
            {
                sb.AppendLine("| Object | Agent Type | Speed | Stopping Dist | Auto Braking | Avoidance Priority |");
                sb.AppendLine("| :--- | :--- | :--- | :--- | :--- | :--- |");
                foreach (var agent in agents.OrderBy(a => GetPath(a.gameObject)))
                {
                    string typeName = NavMesh.GetSettingsNameFromID(agent.agentTypeID);
                    sb.AppendLine($"| {GetPath(agent.gameObject)} | {typeName} | {agent.speed:F2} | {agent.stoppingDistance:F2} | {YN(agent.autoBraking)} | {agent.avoidancePriority} |");
                }
            }

            sb.AppendLine();
            sb.AppendLine($"---\nGenerated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        }

        // ─── Mode B ─────────────────────────────────────────────────────────────

        static void BuildSurfaceReport(StringBuilder sb)
        {
            sb.AppendLine("# NavMesh Surface Report");
            sb.AppendLine();

            // Try NavMeshSurface (AI Navigation package)
            bool foundSurfaces = false;
            try
            {
                var surfaceType = TypeResolver.ResolveComponentType("NavMeshSurface");
                if (surfaceType != null)
                {
                    var surfaces = UnityEngine.Object.FindObjectsByType(surfaceType, FindObjectsSortMode.None);
                    if (surfaces != null && surfaces.Length > 0)
                    {
                        foundSurfaces = true;
                        sb.AppendLine($"## NavMesh Surfaces ({surfaces.Length})");
                        sb.AppendLine();
                        sb.AppendLine("| Object | Agent Type | Collect Objects | Use Geometry | Default Area |");
                        sb.AppendLine("| :--- | :--- | :--- | :--- | :--- |");

                        foreach (var surf in surfaces)
                        {
                            var go = (surf as Component)?.gameObject;
                            if (go == null) continue;
                            var so = new SerializedObject(surf);

                            string agentTypeName = "—";
                            var agentTypeProp = so.FindProperty("m_AgentTypeID");
                            if (agentTypeProp != null)
                                agentTypeName = NavMesh.GetSettingsNameFromID(agentTypeProp.intValue);

                            string collectObjects = GetSerializedEnumName(so, "m_CollectObjects", "CollectObjects");
                            string useGeometry = GetSerializedEnumName(so, "m_UseGeometry", "NavMeshCollectGeometry");
                            string defaultArea = GetSerializedEnumName(so, "m_DefaultArea", "NavMeshArea");

                            sb.AppendLine($"| {GetPath(go)} | {agentTypeName} | {collectObjects} | {useGeometry} | {defaultArea} |");
                        }
                        sb.AppendLine();
                    }
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine($"_NavMeshSurface lookup error: {ex.Message}_");
                sb.AppendLine();
            }

            if (!foundSurfaces)
            {
                sb.AppendLine("_NavMeshSurface components not found (AI Navigation package may not be installed or no surfaces in scene)._");
                sb.AppendLine();
            }

            // Legacy NavMesh
            sb.AppendLine("## Legacy NavMesh");
            try
            {
                var tri = NavMesh.CalculateTriangulation();
                int triCount = tri.indices != null ? tri.indices.Length / 3 : 0;
                int vertCount = tri.vertices != null ? tri.vertices.Length : 0;
                float area = EstimateNavMeshArea(tri.vertices, tri.indices);
                sb.AppendLine($"- Triangles: {triCount:N0}");
                sb.AppendLine($"- Vertices: {vertCount:N0}");
                sb.AppendLine($"- Area: ~{area:F0} m²");
            }
            catch (Exception ex)
            {
                sb.AppendLine($"_Could not calculate legacy NavMesh triangulation: {ex.Message}_");
            }

            sb.AppendLine();
            sb.AppendLine($"---\nGenerated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        }

        static float EstimateNavMeshArea(Vector3[] vertices, int[] indices)
        {
            if (vertices == null || indices == null || indices.Length < 3) return 0f;
            float area = 0f;
            for (int i = 0; i < indices.Length - 2; i += 3)
            {
                var v0 = vertices[indices[i]];
                var v1 = vertices[indices[i + 1]];
                var v2 = vertices[indices[i + 2]];
                area += Vector3.Cross(v1 - v0, v2 - v0).magnitude * 0.5f;
            }
            return area;
        }

        static string GetSerializedEnumName(SerializedObject so, string propertyName, string fallbackPrefix)
        {
            var prop = so.FindProperty(propertyName);
            if (prop == null) return "—";
            return prop.enumDisplayNames != null && prop.enumValueIndex >= 0 && prop.enumValueIndex < prop.enumDisplayNames.Length
                ? prop.enumDisplayNames[prop.enumValueIndex]
                : prop.intValue.ToString();
        }

        // ─── Mode C ─────────────────────────────────────────────────────────────

        static void BuildObstacleReport(StringBuilder sb)
        {
            sb.AppendLine("# NavMesh Obstacles");
            sb.AppendLine();

            var obstacles = UnityEngine.Object.FindObjectsByType<NavMeshObstacle>(FindObjectsSortMode.None);
            sb.AppendLine($"## NavMesh Obstacles ({obstacles.Length})");
            sb.AppendLine();

            if (obstacles.Length == 0)
            {
                sb.AppendLine("_No NavMeshObstacle components found in scene._");
            }
            else
            {
                sb.AppendLine("| Object | Shape | Size | Carving | Move Threshold | Carve Only Stationary |");
                sb.AppendLine("| :--- | :--- | :--- | :--- | :--- | :--- |");
                foreach (var obs in obstacles.OrderBy(o => GetPath(o.gameObject)))
                {
                    string shape = obs.shape.ToString();
                    string size = obs.shape == NavMeshObstacleShape.Capsule
                        ? $"r={obs.radius:F2}, h={obs.height:F2}"
                        : $"{obs.size.x:F2}x{obs.size.y:F2}x{obs.size.z:F2}";
                    string threshold = obs.carving ? obs.carvingMoveThreshold.ToString("F2") : "—";
                    string stationary = obs.carving ? YN(obs.carveOnlyStationary) : "—";
                    sb.AppendLine($"| {GetPath(obs.gameObject)} | {shape} | {size} | {YN(obs.carving)} | {threshold} | {stationary} |");
                }
            }

            sb.AppendLine();
            sb.AppendLine($"---\nGenerated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        }

        // ─── Mode D ─────────────────────────────────────────────────────────────

        static void BuildLinkReport(StringBuilder sb)
        {
            sb.AppendLine("# Navigation Links");
            sb.AppendLine();

            var legacyLinks = UnityEngine.Object.FindObjectsByType<OffMeshLink>(FindObjectsSortMode.None);

            // Try NavMeshLink (AI Navigation package)
            UnityEngine.Object[] packageLinks = null;
            try
            {
                var linkType = TypeResolver.ResolveComponentType("NavMeshLink");
                if (linkType != null)
                    packageLinks = UnityEngine.Object.FindObjectsByType(linkType, FindObjectsSortMode.None);
            }
            catch { }

            int totalLinks = legacyLinks.Length + (packageLinks?.Length ?? 0);
            sb.AppendLine($"## Navigation Links ({totalLinks})");
            sb.AppendLine();

            if (totalLinks == 0)
            {
                sb.AppendLine("_No navigation links found in scene._");
            }
            else
            {
                sb.AppendLine("| Object | Type | Start | End | Bidirectional | Area |");
                sb.AppendLine("| :--- | :--- | :--- | :--- | :--- | :--- |");

                foreach (var link in legacyLinks.OrderBy(l => GetPath(l.gameObject)))
                {
                    string start = link.startTransform != null ? V3(link.startTransform.position) : "—";
                    string end = link.endTransform != null ? V3(link.endTransform.position) : "—";
                    string area = NavMesh.GetAreaNames().Length > link.area ? NavMesh.GetAreaNames()[link.area] : link.area.ToString();
                    sb.AppendLine($"| {GetPath(link.gameObject)} | OffMeshLink | {start} | {end} | {YN(link.biDirectional)} | {area} |");
                }

                if (packageLinks != null)
                {
                    foreach (var link in packageLinks.OrderBy(l => GetPath(((Component)l).gameObject)))
                    {
                        var go = ((Component)link).gameObject;
                        var so = new SerializedObject(link);
                        string start = GetVec3SerializedProp(so, "m_StartPoint");
                        string end = GetVec3SerializedProp(so, "m_EndPoint");
                        string biDir = GetBoolSerializedProp(so, "m_Bidirectional");
                        string area = GetIntSerializedProp(so, "m_Area");
                        sb.AppendLine($"| {GetPath(go)} | NavMeshLink | {start} | {end} | {biDir} | {area} |");
                    }
                }
            }

            sb.AppendLine();
            sb.AppendLine($"---\nGenerated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        }

        // ─── Mode E ─────────────────────────────────────────────────────────────

        static void BuildReachabilityTest(StringBuilder sb)
        {
            sb.AppendLine("# Reachability Test");
            sb.AppendLine();

            var agents = UnityEngine.Object.FindObjectsByType<NavMeshAgent>(FindObjectsSortMode.None);
            if (agents.Length == 0)
            {
                sb.AppendLine("_No NavMeshAgents found in scene — reachability test requires at least one agent._");
                sb.AppendLine();
                sb.AppendLine($"---\nGenerated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                return;
            }

            sb.AppendLine("| From | To | Result | Distance | Status |");
            sb.AppendLine("| :--- | :--- | :--- | :--- | :--- |");

            const int maxPairs = 20;
            int pairs = 0;

            for (int i = 0; i < agents.Length && pairs < maxPairs; i++)
            {
                for (int j = i + 1; j < agents.Length && pairs < maxPairs; j++, pairs++)
                {
                    var from = agents[i];
                    var to = agents[j];
                    Vector3 fromPos = from.transform.position;
                    Vector3 toPos = to.transform.position;

                    var path = new NavMeshPath();
                    bool calculated = NavMesh.CalculatePath(fromPos, toPos, NavMesh.AllAreas, path);

                    string result = path.status.ToString();
                    string distance = "—";
                    string status;

                    if (path.status == NavMeshPathStatus.PathComplete)
                    {
                        float dist = CalculatePathLength(path);
                        distance = $"{dist:F1}m";
                        status = "✓ Reachable";
                    }
                    else if (path.status == NavMeshPathStatus.PathPartial)
                    {
                        float dist = CalculatePathLength(path);
                        distance = $"{dist:F1}m";
                        status = "⚠ Partial path";
                    }
                    else
                    {
                        status = "✗ No path";
                    }

                    string fromLabel = $"{GetPath(from.gameObject)} {V3(fromPos)}";
                    string toLabel = $"{GetPath(to.gameObject)} {V3(toPos)}";
                    sb.AppendLine($"| {fromLabel} | {toLabel} | {result} | {distance} | {status} |");
                }
            }

            if (pairs == 0)
            {
                sb.AppendLine("| — | — | — | — | Only 1 agent in scene — need 2+ for path test |");
            }

            sb.AppendLine();
            sb.AppendLine($"---\nGenerated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        }

        static float CalculatePathLength(NavMeshPath path)
        {
            if (path.corners == null || path.corners.Length < 2) return 0f;
            float len = 0f;
            for (int i = 1; i < path.corners.Length; i++)
                len += Vector3.Distance(path.corners[i - 1], path.corners[i]);
            return len;
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
        static string V3(Vector3 v) => $"({v.x:F1},{v.y:F1},{v.z:F1})";

        static string GetVec3SerializedProp(SerializedObject so, string propName)
        {
            var prop = so.FindProperty(propName);
            if (prop == null) return "—";
            return $"({prop.vector3Value.x:F1},{prop.vector3Value.y:F1},{prop.vector3Value.z:F1})";
        }

        static string GetBoolSerializedProp(SerializedObject so, string propName)
        {
            var prop = so.FindProperty(propName);
            return prop != null ? YN(prop.boolValue) : "—";
        }

        static string GetIntSerializedProp(SerializedObject so, string propName)
        {
            var prop = so.FindProperty(propName);
            return prop != null ? prop.intValue.ToString() : "—";
        }
    }
}
