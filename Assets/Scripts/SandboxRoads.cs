using System.Collections.Generic;
using UnityEngine;

public class SandboxRoads : MonoBehaviour
{
    private SandboxController controller;
    private Material osmRoadMaterial;

    private bool isDrawingRoad = false;
    private Vector3 roadStartPos;
    private string snappedStartNodeId;
    private LineRenderer previewLine;

    void Awake()
    {
        controller = GetComponent<SandboxController>();
    }

    void Start()
    {
        GameObject lineObj = new GameObject("RoadPreviewLine");
        previewLine = lineObj.AddComponent<LineRenderer>();
        previewLine.startWidth = 6f;
        previewLine.endWidth = 6f;
        previewLine.material = new Material(Shader.Find("Sprites/Default"));
        previewLine.startColor = new Color(1f, 0.8f, 0f, 0.5f);
        previewLine.endColor = new Color(1f, 0.8f, 0f, 0.5f);
        previewLine.enabled = false;

        StealOsmRoadMaterial();
    }

    private void StealOsmRoadMaterial()
    {
        foreach (var roadObj in controller.dataLoader.roadMeshes.Values)
        {
            if (roadObj != null && roadObj.TryGetComponent(out MeshRenderer mr))
            {
                osmRoadMaterial = mr.sharedMaterial;
                return;
            }
        }
    }

    // ==========================================
    // 1. DRAWING ROADS (15m Precision Snap Radius)
    // ==========================================
    public void HandleRoadDrawing(bool hitSomething, RaycastHit hit)
    {
        if (!hitSomething) return;

        if (Input.GetMouseButtonDown(0))
        {
            isDrawingRoad = true;
            snappedStartNodeId = controller.FindNearestNode(hit.point, 15f);
            roadStartPos = snappedStartNodeId != null ? controller.dataLoader.cityData.nodes[snappedStartNodeId].Position : hit.point;

            previewLine.enabled = true;
            previewLine.SetPosition(0, roadStartPos + (Vector3.up * 0.2f));
        }

        if (isDrawingRoad && Input.GetMouseButton(0))
        {
            previewLine.SetPosition(1, hit.point + (Vector3.up * 0.2f));
        }

        if (isDrawingRoad && Input.GetMouseButtonUp(0))
        {
            isDrawingRoad = false;
            previewLine.enabled = false;

            string snappedEndNodeId = controller.FindNearestNode(hit.point, 15f);
            Vector3 roadEndPos = snappedEndNodeId != null ? controller.dataLoader.cityData.nodes[snappedEndNodeId].Position : hit.point;

            if (Vector3.Distance(roadStartPos, roadEndPos) > 5f)
            {
                BuildNewRoadSegment(roadStartPos, roadEndPos, snappedStartNodeId, snappedEndNodeId);
            }
        }
    }

    private void BuildNewRoadSegment(Vector3 start, Vector3 end, string startId, string endId)
    {
        var cityData = controller.dataLoader.cityData;

        startId = startId ?? "node_sb_" + System.Guid.NewGuid().ToString().Substring(0, 8);
        endId = endId ?? "node_sb_" + System.Guid.NewGuid().ToString().Substring(0, 8);

        if (!cityData.nodes.ContainsKey(startId)) cityData.nodes[startId] = new SimulacraNode { id = startId, x = start.x, y = start.y, z = start.z, is_intersection = true };
        if (!cityData.nodes.ContainsKey(endId)) cityData.nodes[endId] = new SimulacraNode { id = endId, x = end.x, y = end.y, z = end.z, is_intersection = true };

        float dist = Vector3.Distance(start, end);
        float timeCost = dist / 11f;

        if (!cityData.adjacency_list.ContainsKey(startId)) cityData.adjacency_list[startId] = new List<SimulacraEdge>();
        if (!cityData.adjacency_list.ContainsKey(endId)) cityData.adjacency_list[endId] = new List<SimulacraEdge>();

        cityData.adjacency_list[startId].Add(new SimulacraEdge { target = endId, distance = dist, lanes = 2, speed_limit_kmh = 40, weight_time_sec = timeCost });
        cityData.adjacency_list[endId].Add(new SimulacraEdge { target = startId, distance = dist, lanes = 2, speed_limit_kmh = 40, weight_time_sec = timeCost });

        GameObject newRoadMesh = CreateSeamlessRoadMesh(start, end);
        controller.trafficPool.RefreshCityGraph();

        // UNDO LOGIC
        controller.undoStack.Push(() => {
            cityData.adjacency_list[startId].RemoveAll(e => e.target == endId);
            cityData.adjacency_list[endId].RemoveAll(e => e.target == startId);

            foreach (var vehicle in FindObjectsOfType<KinematicVehicle>())
            {
                if (vehicle.gameObject.activeInHierarchy && vehicle.routeWaypoints != null)
                {
                    for (int i = vehicle.currentWaypointIndex; i < vehicle.routeWaypoints.Count; i++)
                    {
                        if (vehicle.routeWaypoints[i].NodeId == startId || vehicle.routeWaypoints[i].NodeId == endId)
                        {
                            vehicle.gameObject.SetActive(false);
                            break;
                        }
                    }
                }
            }

            if (newRoadMesh != null) Destroy(newRoadMesh);
            controller.trafficPool.RefreshCityGraph();
        });
    }

    private GameObject CreateSeamlessRoadMesh(Vector3 start, Vector3 end)
    {
        string roadId = "way_sb_" + System.Guid.NewGuid().ToString().Substring(0, 8);
        GameObject roadParent = new GameObject(roadId) { tag = "PlayerRoad" };

        Material roadMat = osmRoadMaterial ?? new Material(Shader.Find("Unlit/Color")) { color = new Color(0.88f, 0.88f, 0.88f) };

        GameObject mainRoad = GameObject.CreatePrimitive(PrimitiveType.Cube);
        mainRoad.transform.SetParent(roadParent.transform);
        mainRoad.transform.position = start + (end - start) / 2f + (Vector3.up * 0.02f);
        mainRoad.transform.localScale = new Vector3(8f, 0.01f, Vector3.Distance(start, end));
        mainRoad.transform.rotation = Quaternion.LookRotation(end - start);
        mainRoad.GetComponent<MeshRenderer>().material = roadMat;
        mainRoad.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mainRoad.tag = "PlayerRoad";

        GameObject jointA = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        jointA.transform.SetParent(roadParent.transform);
        jointA.transform.position = start + (Vector3.up * 0.02f);
        jointA.transform.localScale = new Vector3(8f, 0.005f, 8f);
        jointA.GetComponent<MeshRenderer>().material = roadMat;
        jointA.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        jointA.tag = "PlayerRoad";

        GameObject jointB = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        jointB.transform.SetParent(roadParent.transform);
        jointB.transform.position = end + (Vector3.up * 0.02f);
        jointB.transform.localScale = new Vector3(8f, 0.005f, 8f);
        jointB.GetComponent<MeshRenderer>().material = roadMat;
        jointB.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        jointB.tag = "PlayerRoad";

        controller.dataLoader.roadMeshes[roadId] = roadParent;
        return roadParent;
    }

    // ==========================================
    // 2. EDGE-BASED BULLDOZING (Volumetric Scan)
    // ==========================================
    public void BulldozeRoadAt(GameObject targetRoad, Vector3 clickPoint)
    {
        var data = controller.dataLoader.cityData;

        Bounds combinedBounds = new Bounds(clickPoint, Vector3.zero);
        Renderer[] renderers = targetRoad.GetComponentsInChildren<Renderer>();
        if (renderers.Length > 0)
        {
            combinedBounds = renderers[0].bounds;
            foreach (var r in renderers) combinedBounds.Encapsulate(r.bounds);
        }

        combinedBounds.Expand(new Vector3(5f, 50f, 5f));
        HashSet<string> nodesToWipe = new HashSet<string>();

        // 1. Find and sever ALL mathematical edges
        foreach (var kvp in data.adjacency_list)
        {
            if (!data.nodes.TryGetValue(kvp.Key, out var a)) continue;

            for (int i = kvp.Value.Count - 1; i >= 0; i--)
            {
                var edge = kvp.Value[i];
                if (!data.nodes.TryGetValue(edge.target, out var b)) continue;

                Vector3 edgeMidPoint = (a.Position + b.Position) / 2f;

                if (combinedBounds.Contains(edgeMidPoint))
                {
                    nodesToWipe.Add(kvp.Key);
                    nodesToWipe.Add(edge.target);

                    kvp.Value.RemoveAt(i);
                    if (data.adjacency_list.ContainsKey(edge.target))
                    {
                        data.adjacency_list[edge.target].RemoveAll(e => e.target == kvp.Key);
                    }
                }
            }
        }

        // 2. Erase memory of cars driving toward the deleted road
        foreach (var vehicle in FindObjectsOfType<KinematicVehicle>())
        {
            if (vehicle.gameObject.activeInHierarchy && vehicle.routeWaypoints != null)
            {
                bool killCar = false;
                for (int i = vehicle.currentWaypointIndex; i < vehicle.routeWaypoints.Count; i++)
                {
                    if (nodesToWipe.Contains(vehicle.routeWaypoints[i].NodeId))
                    {
                        killCar = true;
                        break;
                    }
                }
                if (killCar) vehicle.gameObject.SetActive(false);
            }
        }

        // 3. ORPHANED NODE CLEANUP: Destroy the cyan dots so cars stop spawning on them
        foreach (string nodeId in nodesToWipe)
        {
            bool hasConnections = false;
            foreach (var kvp in data.adjacency_list)
            {
                if (kvp.Key == nodeId && kvp.Value.Count > 0) hasConnections = true;
                if (kvp.Value.Exists(e => e.target == nodeId)) hasConnections = true;
            }

            // If the node has no lines connecting to it anymore, delete it entirely
            if (!hasConnections)
            {
                data.nodes.Remove(nodeId);
                data.adjacency_list.Remove(nodeId);
            }
        }

        controller.trafficPool.RefreshCityGraph();
        Destroy(targetRoad);
    }
}