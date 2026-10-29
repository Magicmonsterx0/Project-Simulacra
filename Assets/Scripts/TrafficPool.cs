using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Handles object pooling and dynamic, capacity-weighted vehicle spawning.
/// Features Dynamic Volume Control based on the TimeManager clock, and
/// scales total demand with the total building capacity (so sandbox edits change traffic volume).
/// </summary>
public class TrafficPool : MonoBehaviour
{
    [Header("Dependencies")]
    public CityDataLoader dataLoader;
    public AStarRouter router;
    public TimeManager timeManager;

    [Header("Vehicle Mix Settings")]
    public GameObject hatchbackPrefab;
    public GameObject twoWheelerPrefab;
    [Range(0f, 1f)] public float twoWheelerRatio = 0.7f;

    [Header("Traffic Volume Settings")]
    public int maxVehicles = 3000;

    [Tooltip("The absolute maximum number of cars on the road during Rush Hour (before demand scaling)")]
    public int peakActiveTraffic = 800;

    private Queue<KinematicVehicle> availableVehicles = new Queue<KinematicVehicle>();
    private readonly HashSet<KinematicVehicle> activeVehicles = new HashSet<KinematicVehicle>();
    private int activeVehicleCount = 0;

    // Total building capacity at startup; later edits are compared against this
    private float baselineWeight = 0f;

    // --- ROULETTE WHEEL SELECTION LOGIC ---
    private class WeightedZone
    {
        public List<string> nodes = new List<string>();
        public List<int> cumulativeWeights = new List<int>();
        public int totalWeight = 0;

        public void AddBuilding(string roadNodeId, int capacity)
        {
            nodes.Add(roadNodeId);
            totalWeight += Mathf.Max(0, capacity);
            cumulativeWeights.Add(totalWeight);
        }

        public string GetRandomNodeWeighted()
        {
            if (nodes.Count == 0) return null;
            if (totalWeight <= 0) return nodes[Random.Range(0, nodes.Count)];

            int roll = Random.Range(0, totalWeight);
            for (int i = 0; i < cumulativeWeights.Count; i++)
            {
                if (roll < cumulativeWeights[i]) return nodes[i];
            }
            return nodes[nodes.Count - 1];
        }
    }

    private WeightedZone residentialZone = new WeightedZone();
    private WeightedZone commercialZone = new WeightedZone();

    void Start()
    {
        if (dataLoader == null || dataLoader.cityData == null)
        {
            Debug.LogError("[TrafficPool] dataLoader is missing or the city JSON did not load.");
            enabled = false;
            return;
        }
        if (router == null)
        {
            Debug.LogError("[TrafficPool] router (AStarRouter) is not assigned.");
            enabled = false;
            return;
        }

        CategorizeBuildingsByWeight();
        baselineWeight = TotalWeight();
        InitializePool();
    }

    void Update()
    {
        // Continuously check and adjust the total number of cars on the road
        ManageTrafficVolume();
    }

    private void CategorizeBuildingsByWeight()
    {
        if (dataLoader.cityData.buildings != null)
        {
            foreach (var b in dataLoader.cityData.buildings)
            {
                if (string.IsNullOrEmpty(b.nearest_road_node)) continue;

                string type = b.type != null ? b.type.ToLower() : "general";

                if (type == "residential" || type == "apartments" || type == "house" || type == "dormitory")
                    residentialZone.AddBuilding(b.nearest_road_node, b.capacity);
                else if (type == "commercial" || type == "educational" || type == "university" || type == "retail")
                    commercialZone.AddBuilding(b.nearest_road_node, b.capacity);
                else
                {
                    residentialZone.AddBuilding(b.nearest_road_node, Mathf.Max(1, b.capacity / 2));
                    commercialZone.AddBuilding(b.nearest_road_node, Mathf.Max(1, b.capacity / 2));
                }
            }
        }

        if (residentialZone.nodes.Count == 0 || commercialZone.nodes.Count == 0)
        {
            foreach (var kvp in dataLoader.cityData.nodes)
            {
                residentialZone.AddBuilding(kvp.Key, 10);
                commercialZone.AddBuilding(kvp.Key, 10);
            }
        }
    }

    private float TotalWeight()
    {
        return residentialZone.totalWeight + commercialZone.totalWeight;
    }

    /// <summary>
    /// 1.0 at startup. Goes up when the user adds buildings, down when they remove them.
    /// </summary>
    private float DemandMultiplier()
    {
        if (baselineWeight <= 0f) return 1f;
        return Mathf.Clamp(TotalWeight() / baselineWeight, 0.25f, 3f);
    }

    private void InitializePool()
    {
        for (int i = 0; i < maxVehicles; i++)
        {
            GameObject prefabToUse = (Random.value <= twoWheelerRatio && twoWheelerPrefab != null) ? twoWheelerPrefab : hatchbackPrefab;
            if (prefabToUse == null) prefabToUse = hatchbackPrefab;

            GameObject obj = Instantiate(prefabToUse, transform);
            obj.SetActive(false);

            // NOTE: do not use "??" with Unity components - it bypasses Unity's null check.
            if (!obj.TryGetComponent(out KinematicVehicle vehicle))
                vehicle = obj.AddComponent<KinematicVehicle>();

            availableVehicles.Enqueue(vehicle);
        }
    }

    /// <summary>
    /// Calculates how many cars should currently be on the road based on the Time of Day.
    /// </summary>
    private int GetTargetTrafficVolume()
    {
        if (timeManager == null) return peakActiveTraffic;

        switch (timeManager.currentPhase)
        {
            case TimeManager.TimePhase.MorningRush:
                return peakActiveTraffic; // 100% Capacity

            case TimeManager.TimePhase.EveningRush:
                return peakActiveTraffic; // 100% Capacity

            case TimeManager.TimePhase.MidDay:
                return Mathf.RoundToInt(peakActiveTraffic * 0.40f); // 40% Capacity

            case TimeManager.TimePhase.Night:
                return Mathf.RoundToInt(peakActiveTraffic * 0.05f); // 5% Capacity

            default:
                return peakActiveTraffic;
        }
    }

    private void ManageTrafficVolume()
    {
        int targetVolume = Mathf.Min(maxVehicles, Mathf.RoundToInt(GetTargetTrafficVolume() * DemandMultiplier()));

        // Limit spawns to 3 per frame so the A* pathfinding doesn't freeze the PC.
        int spawnsThisFrame = 0;
        while (activeVehicleCount < targetVolume && spawnsThisFrame < 3 && availableVehicles.Count > 0)
        {
            SpawnRandomTrip();
            spawnsThisFrame++;
        }
    }

    public void ReturnVehicleToPool(KinematicVehicle vehicle)
    {
        // Ignore vehicles that were already returned (prevents double counting)
        if (!activeVehicles.Remove(vehicle)) return;

        vehicle.gameObject.SetActive(false);
        availableVehicles.Enqueue(vehicle);
        activeVehicleCount--;
    }

    /// <summary>
    /// Called by the sandbox when a road segment is closed. Vehicles whose route uses it are removed;
    /// the spawner replaces them with new vehicles routed on the updated graph.
    /// </summary>
    public void DespawnVehiclesUsingEdge(string nodeA, string nodeB)
    {
        var affected = new List<KinematicVehicle>();
        foreach (var v in activeVehicles)
        {
            if (v != null && v.RouteUsesEdge(nodeA, nodeB)) affected.Add(v);
        }
        foreach (var v in affected) ReturnVehicleToPool(v);

        Debug.Log($"[TrafficPool] Despawned {affected.Count} vehicle(s) that used closed road {nodeA} <-> {nodeB}.");
    }

    private void SpawnRandomTrip()
    {
        string startNode = null;
        string endNode = null;

        if (timeManager != null)
        {
            switch (timeManager.currentPhase)
            {
                case TimeManager.TimePhase.MorningRush:
                    startNode = residentialZone.GetRandomNodeWeighted();
                    endNode = commercialZone.GetRandomNodeWeighted();
                    break;
                case TimeManager.TimePhase.EveningRush:
                    startNode = commercialZone.GetRandomNodeWeighted();
                    endNode = residentialZone.GetRandomNodeWeighted();
                    break;
                case TimeManager.TimePhase.Night:
                    startNode = Random.value > 0.5f ? commercialZone.GetRandomNodeWeighted() : residentialZone.GetRandomNodeWeighted();
                    endNode = residentialZone.GetRandomNodeWeighted();
                    break;
                default: // MidDay
                    startNode = Random.value > 0.5f ? residentialZone.GetRandomNodeWeighted() : commercialZone.GetRandomNodeWeighted();
                    endNode = Random.value > 0.5f ? commercialZone.GetRandomNodeWeighted() : residentialZone.GetRandomNodeWeighted();
                    break;
            }
        }
        else
        {
            startNode = residentialZone.GetRandomNodeWeighted();
            endNode = commercialZone.GetRandomNodeWeighted();
        }

        if (startNode == null || endNode == null) return;

        List<string> pathIds = router.FindShortestPath(startNode, endNode);

        if (pathIds != null && pathIds.Count > 1)
        {
            List<KinematicVehicle.Waypoint> routeWaypoints = new List<KinematicVehicle.Waypoint>();
            var edges = dataLoader.cityData.adjacency_list;

            for (int i = 0; i < pathIds.Count; i++)
            {
                string currentNode = pathIds[i];
                int directionalLanes = 1;

                if (i < pathIds.Count - 1)
                {
                    string nextNode = pathIds[i + 1];
                    if (edges.ContainsKey(currentNode))
                    {
                        var edgeData = edges[currentNode].Find(e => e.target == nextNode);
                        if (edgeData != null) directionalLanes = Mathf.Max(1, edgeData.lanes / 2);
                    }
                }

                routeWaypoints.Add(new KinematicVehicle.Waypoint
                {
                    NodeId = currentNode,
                    Position = dataLoader.cityData.nodes[currentNode].Position,
                    Lanes = directionalLanes
                });
            }

            KinematicVehicle car = availableVehicles.Dequeue();
            car.AssignRoute(routeWaypoints, 40f, this);

            activeVehicles.Add(car);
            activeVehicleCount++;
        }
    }

    /// <summary>
    /// Forces the AI to recalculate the Origin-Destination weights when the user edits the city.
    /// </summary>
    public void RefreshCityGraph()
    {
        residentialZone = new WeightedZone();
        commercialZone = new WeightedZone();

        CategorizeBuildingsByWeight();

        Debug.Log($"[TrafficPool] Demand updated. Residential nodes: {residentialZone.nodes.Count} | " +
                  $"Commercial nodes: {commercialZone.nodes.Count} | Demand multiplier: {DemandMultiplier():F2}x");
    }
}