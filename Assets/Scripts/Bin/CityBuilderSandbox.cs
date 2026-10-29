using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Phase-2 sandbox tools. Every edit follows the same path:
/// mutate data -> update visuals -> notify traffic -> push an undo action.
/// Roads are edited at graph-edge (segment) level, not whole OSM way level.
/// </summary>
public class CityBuilderSandbox : MonoBehaviour
{
    [Header("Dependencies")]
    public CityDataLoader dataLoader;
    public TrafficPool trafficPool;

    public enum ToolMode { Cursor, Bulldoze, PlaceHouse, PlaceCommercial, PlaceTrafficLight }
    public ToolMode currentTool = ToolMode.Cursor;

    [Header("Building presets (multiples of the average existing building capacity)")]
    public float houseMultiplier = 1f;
    public float commercialMultiplier = 25f;

    [Header("Prefabs (Optional)")]
    public GameObject housePrefab;
    public GameObject commercialPrefab;
    public GameObject trafficLightPrefab;

    [Header("Picking limits (metres)")]
    public float maxRoadPickDistance = 15f;
    public float maxBuildingPickDistance = 40f;
    public float maxIntersectionPickDistance = 30f;
    public float maxBuildingAccessDistance = 80f;
    [Tooltip("Meshes bigger than this are assumed to be merged and will not be deleted.")]
    public float maxSingleBuildingSize = 250f;

    // Cached at Start(). The Inspector fields above can be changed while playing (e.g. by the Editor's own
    // Ctrl+Z), so the running logic never depends on them after startup.
    private CityDataLoader loader;
    private TrafficPool pool;

    private Camera cam;
    private float avgCapacity = 20f;
    private readonly List<string> intersectionIds = new List<string>();
    private readonly Dictionary<string, GameObject> activeTrafficLights = new Dictionary<string, GameObject>();
    private readonly Stack<System.Action> undoStack = new Stack<System.Action>();
    private string status = "";
    private float statusUntil;

    // Screen areas (IMGUI space) that must not trigger world clicks
    private static readonly Rect TimePanelRect = new Rect(10, 10, 320, 130);   // TimeUIControls panel
    private Rect ToolbarRect => new Rect(Screen.width / 2f - 300f, Screen.height - 60f, 600f, 50f);

    // ------------------------------------------------------------------ setup
    void Start()
    {
        loader = dataLoader;
        pool = trafficPool;

        if (loader == null || loader.cityData == null || pool == null)
        {
            Debug.LogError("[SANDBOX] Missing reference: assign dataLoader and trafficPool in the Inspector, " +
                           "and make sure the JSON loaded (CityDataLoader.simulacraJsonFile).");
            enabled = false;
            return;
        }

        cam = Camera.main != null ? Camera.main : FindFirstObjectByType<Camera>();
        ComputeAverageCapacity();
        BuildIntersectionList();
    }

    private void ComputeAverageCapacity()
    {
        float sum = 0f;
        int n = 0;
        foreach (var b in loader.cityData.buildings)
        {
            if (b.capacity > 0) { sum += b.capacity; n++; }
        }
        avgCapacity = n > 0 ? sum / n : 20f;
        Debug.Log($"[SANDBOX] Average building capacity = {avgCapacity:F1} ({n} buildings with capacity).");
    }

    private void BuildIntersectionList()
    {
        var data = loader.cityData;
        var neighbours = new Dictionary<string, HashSet<string>>();

        void Link(string a, string b)
        {
            if (!neighbours.TryGetValue(a, out var set)) { set = new HashSet<string>(); neighbours[a] = set; }
            set.Add(b);
        }

        foreach (var kvp in data.adjacency_list)
            foreach (var e in kvp.Value)
            {
                Link(kvp.Key, e.target);
                Link(e.target, kvp.Key);
            }

        foreach (var kvp in data.nodes)
        {
            bool deg3 = neighbours.TryGetValue(kvp.Key, out var n) && n.Count >= 3;
            if (kvp.Value.is_intersection || deg3) intersectionIds.Add(kvp.Key);
        }
        Debug.Log($"[SANDBOX] {intersectionIds.Count} intersections available for traffic lights.");
    }

    // ------------------------------------------------------------------ input
    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.Escape)) currentTool = ToolMode.Cursor;
        if (Input.GetKeyDown(KeyCode.Alpha2)) currentTool = ToolMode.Bulldoze;
        if (Input.GetKeyDown(KeyCode.Alpha3)) currentTool = ToolMode.PlaceHouse;
        if (Input.GetKeyDown(KeyCode.Alpha4)) currentTool = ToolMode.PlaceCommercial;
        if (Input.GetKeyDown(KeyCode.Alpha5)) currentTool = ToolMode.PlaceTrafficLight;

        // NOTE: not Ctrl+Z - inside the Unity Editor that also undoes Inspector edits (and un-assigns references)
        if (Input.GetKeyDown(KeyCode.U)) Undo();

        if (Input.GetMouseButtonDown(0) && currentTool != ToolMode.Cursor && !IsPointerOverUI())
            HandleToolClick();
    }

    private bool IsPointerOverUI()
    {
        Vector2 m = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
        return ToolbarRect.Contains(m) || TimePanelRect.Contains(m);
    }

    /// Nearest hit along the mouse ray, ignoring vehicles (their colliders must never block tools).
    private bool TryPick(out RaycastHit result)
    {
        result = default;
        if (cam == null) return false;

        Ray ray = cam.ScreenPointToRay(Input.mousePosition);
        RaycastHit[] hits = Physics.RaycastAll(ray, 5000f);
        float best = float.MaxValue;
        bool found = false;

        foreach (var h in hits)
        {
            if (h.collider.GetComponentInParent<KinematicVehicle>() != null) continue;
            if (h.distance < best) { best = h.distance; result = h; found = true; }
        }
        return found;
    }

    private void HandleToolClick()
    {
        if (!TryPick(out RaycastHit hit)) { SetStatus("Nothing under the cursor."); return; }

        switch (currentTool)
        {
            case ToolMode.Bulldoze: Bulldoze(hit); break;
            case ToolMode.PlaceHouse: PlaceBuilding(hit, "residential", housePrefab, houseMultiplier); break;
            case ToolMode.PlaceCommercial: PlaceBuilding(hit, "commercial", commercialPrefab, commercialMultiplier); break;
            case ToolMode.PlaceTrafficLight: ToggleTrafficLight(hit.point); break;
        }
    }

    // ------------------------------------------------------------------ bulldozer
    private static bool IsBuildingName(GameObject go) => go.name.Contains("osm_buildings");

    private void Bulldoze(RaycastHit hit)
    {
        GameObject target = hit.collider.gameObject;

        var placed = target.GetComponentInParent<BuildingRef>();
        if (placed != null) { RemoveBuilding(placed.gameObject); return; }

        if (IsBuildingName(target)) { RemoveBuilding(target); return; }

        if (target.name.StartsWith("way_")) { RemoveRoadSegment(hit.point); return; }

        SetStatus("Nothing bulldozable here (" + target.name + ").");
    }

    private void RemoveBuilding(GameObject target)
    {
        var data = loader.cityData;
        var rend = target.GetComponent<Renderer>();

        // Safety: never hide a merged mesh (would wipe out half the city)
        if (rend != null && (rend.bounds.size.x > maxSingleBuildingSize || rend.bounds.size.z > maxSingleBuildingSize))
        {
            SetStatus("This mesh is far bigger than one building (merged?) - not deleted. Export buildings as separate objects.");
            return;
        }

        SimulacraBuilding record = null;
        var reference = target.GetComponent<BuildingRef>();
        if (reference != null)
        {
            record = data.buildings.Find(b => b.osm_id == reference.osmId);
        }
        else
        {
            // NOTE: baked meshes have transform.position (0,0,0), so use the renderer bounds instead
            Vector3 c = rend != null ? rend.bounds.center : target.transform.position;
            record = FindBuildingNear(c, maxBuildingPickDistance);
        }

        if (record == null) { SetStatus("No matching building data near this mesh - nothing deleted."); return; }

        data.buildings.Remove(record);
        target.SetActive(false);
        pool.RefreshCityGraph();
        SetStatus("Building removed (capacity " + record.capacity + ").");

        undoStack.Push(() =>
        {
            data.buildings.Add(record);
            if (target != null) target.SetActive(true);
        });
    }

    private SimulacraBuilding FindBuildingNear(Vector3 c, float maxDist)
    {
        SimulacraBuilding best = null;
        float bestSqr = maxDist * maxDist;
        foreach (var b in loader.cityData.buildings)
        {
            if (b.centroid == null) continue;
            float dx = b.centroid.x - c.x, dz = b.centroid.z - c.z;
            float d = dx * dx + dz * dz;
            if (d < bestSqr) { bestSqr = d; best = b; }
        }
        return best;
    }

    private void RemoveRoadSegment(Vector3 point)
    {
        var data = loader.cityData;
        string bestFrom = null, bestTo = null;
        float bestDist = maxRoadPickDistance;

        // Find the graph edge (segment between two nodes) closest to the click
        foreach (var kvp in data.adjacency_list)
        {
            if (!data.nodes.TryGetValue(kvp.Key, out var a)) continue;
            foreach (var e in kvp.Value)
            {
                if (!data.nodes.TryGetValue(e.target, out var b)) continue;
                float d = DistanceToSegmentXZ(point, a.Position, b.Position);
                if (d < bestDist) { bestDist = d; bestFrom = kvp.Key; bestTo = e.target; }
            }
        }

        if (bestFrom == null) { SetStatus("No graph road within " + maxRoadPickDistance + " m of the click."); return; }

        // Remove both directions, remembering the edges for undo
        var removed = new List<(string src, SimulacraEdge edge)>();
        CollectAndRemove(bestFrom, bestTo, removed);
        CollectAndRemove(bestTo, bestFrom, removed);

        Vector3 pa = data.nodes[bestFrom].Position;
        Vector3 pb = data.nodes[bestTo].Position;
        GameObject marker = CreateClosureMarker(pa, pb);

        // Vehicles currently routed over this segment are removed; the spawner replaces them with new routes
        pool.DespawnVehiclesUsingEdge(bestFrom, bestTo);
        SetStatus("Road segment closed (" + removed.Count + " directed edge(s)).");

        undoStack.Push(() =>
        {
            foreach (var r in removed) data.adjacency_list[r.src].Add(r.edge);
            if (marker != null) Destroy(marker);
        });
    }

    private void CollectAndRemove(string src, string dst, List<(string src, SimulacraEdge edge)> store)
    {
        if (!loader.cityData.adjacency_list.TryGetValue(src, out var list)) return;
        for (int i = list.Count - 1; i >= 0; i--)
        {
            if (list[i].target == dst) { store.Add((src, list[i])); list.RemoveAt(i); }
        }
    }

    private static float DistanceToSegmentXZ(Vector3 p, Vector3 a, Vector3 b)
    {
        Vector2 P = new Vector2(p.x, p.z), A = new Vector2(a.x, a.z), B = new Vector2(b.x, b.z);
        Vector2 ab = B - A;
        float len2 = ab.sqrMagnitude;
        if (len2 < 1e-6f) return Vector2.Distance(P, A);
        float t = Mathf.Clamp01(Vector2.Dot(P - A, ab) / len2);
        return Vector2.Distance(P, A + ab * t);
    }

    // The road mesh is one object per OSM way and cannot be cut, so we overlay a red marker on the closed segment
    private GameObject CreateClosureMarker(Vector3 a, Vector3 b)
    {
        var go = new GameObject("RoadClosedMarker");
        go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        var lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = true;
        lr.alignment = LineAlignment.TransformZ;
        lr.positionCount = 2;
        lr.SetPosition(0, a + Vector3.up * 1f);
        lr.SetPosition(1, b + Vector3.up * 1f);
        lr.startWidth = lr.endWidth = 3f;
        lr.material = new Material(Shader.Find("Sprites/Default"));
        lr.startColor = lr.endColor = new Color(0.9f, 0.1f, 0.1f, 0.9f);
        return go;
    }

    // ------------------------------------------------------------------ place buildings
    private void PlaceBuilding(RaycastHit hit, string type, GameObject prefab, float multiplier)
    {
        GameObject hitObj = hit.collider.gameObject;
        if (hitObj.name.StartsWith("way_")) { SetStatus("Can't build on a road."); return; }
        if (hitObj.GetComponentInParent<BuildingRef>() != null || IsBuildingName(hitObj))
        {
            SetStatus("Plot is occupied - bulldoze the building first.");
            return;
        }

        Vector3 pos = hit.point;
        string node = FindNearestRoadNode(pos, maxBuildingAccessDistance);
        if (node == null) { SetStatus("No road within " + maxBuildingAccessDistance + " m - no access."); return; }

        int capacity = Mathf.Max(1, Mathf.RoundToInt(avgCapacity * multiplier));
        string id = "sandbox_" + System.Guid.NewGuid().ToString("N").Substring(0, 8);

        GameObject go;
        if (prefab != null)
        {
            go = Instantiate(prefab, pos, Quaternion.identity);
        }
        else
        {
            bool residential = type == "residential";
            Vector3 size = residential ? new Vector3(8f, 8f, 8f) : new Vector3(20f, 14f, 20f);
            go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.transform.localScale = size;
            go.transform.position = pos + Vector3.up * (size.y / 2f);
            go.GetComponent<Renderer>().material.color = residential ? new Color(0.2f, 0.8f, 0.3f) : new Color(0.2f, 0.4f, 0.9f);
        }
        go.name = "PlacedBuilding_" + id;
        go.AddComponent<BuildingRef>().osmId = id;

        var record = new SimulacraBuilding
        {
            osm_id = id,
            type = type,
            capacity = capacity,
            levels = 1,
            centroid = new Vector3Data { x = pos.x, y = pos.y, z = pos.z },
            nearest_road_node = node
        };
        loader.cityData.buildings.Add(record);
        pool.RefreshCityGraph();
        SetStatus("Built " + type + " (capacity " + capacity + ", connected to node " + node + ").");

        undoStack.Push(() =>
        {
            loader.cityData.buildings.Remove(record);
            if (go != null) Destroy(go);
        });
    }

    private string FindNearestRoadNode(Vector3 p, float maxDist)
    {
        var data = loader.cityData;
        string best = null;
        float bestSqr = maxDist * maxDist;
        foreach (var kvp in data.nodes)
        {
            if (!data.adjacency_list.TryGetValue(kvp.Key, out var edges) || edges.Count == 0) continue;
            float dx = kvp.Value.x - p.x, dz = kvp.Value.z - p.z;
            float d = dx * dx + dz * dz;
            if (d < bestSqr) { bestSqr = d; best = kvp.Key; }
        }
        return best;
    }

    // ------------------------------------------------------------------ traffic lights
    private void ToggleTrafficLight(Vector3 point)
    {
        string id = FindNearestIntersection(point);
        if (id == null) { SetStatus("No intersection within " + maxIntersectionPickDistance + " m of the click."); return; }

        bool before = loader.cityData.nodes[id].hasTrafficLight;
        SetLight(id, !before);
        SetStatus(before ? "Traffic light removed." : "Traffic light placed (vehicles do not obey it yet - next step).");
        undoStack.Push(() => SetLight(id, before));
    }

    private string FindNearestIntersection(Vector3 p)
    {
        var nodes = loader.cityData.nodes;
        string best = null;
        float bestSqr = maxIntersectionPickDistance * maxIntersectionPickDistance;
        foreach (var id in intersectionIds)
        {
            var n = nodes[id];
            float dx = n.x - p.x, dz = n.z - p.z;
            float d = dx * dx + dz * dz;
            if (d < bestSqr) { bestSqr = d; best = id; }
        }
        return best;
    }

    private void SetLight(string id, bool on)
    {
        var node = loader.cityData.nodes[id];
        node.hasTrafficLight = on;

        if (on)
        {
            node.isGreenLight = false;
            if (!activeTrafficLights.ContainsKey(id))
            {
                GameObject lamp;
                if (trafficLightPrefab != null)
                {
                    lamp = Instantiate(trafficLightPrefab, node.Position, Quaternion.identity);
                }
                else
                {
                    lamp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    Destroy(lamp.GetComponent<Collider>());
                    lamp.transform.position = node.Position + Vector3.up * 6f;
                    lamp.transform.localScale = Vector3.one * 4f;
                    lamp.GetComponent<Renderer>().material.color = Color.red;
                }
                activeTrafficLights[id] = lamp;
            }
        }
        else if (activeTrafficLights.TryGetValue(id, out var existing))
        {
            Destroy(existing);
            activeTrafficLights.Remove(id);
        }
    }

    // ------------------------------------------------------------------ undo / UI
    private void Undo()
    {
        if (undoStack.Count == 0) { SetStatus("Nothing to undo."); return; }
        undoStack.Pop().Invoke();
        pool.RefreshCityGraph();
        SetStatus("Undone.");
    }

    private void SetStatus(string msg)
    {
        status = msg;
        statusUntil = Time.unscaledTime + 5f;
        Debug.Log("[SANDBOX] " + msg);
    }

    void OnGUI()
    {
        GUILayout.BeginArea(ToolbarRect, GUI.skin.box);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Cursor [1]", GUILayout.Height(40))) currentTool = ToolMode.Cursor;
        if (GUILayout.Button("Bulldoze [2]", GUILayout.Height(40))) currentTool = ToolMode.Bulldoze;
        if (GUILayout.Button("House [3]", GUILayout.Height(40))) currentTool = ToolMode.PlaceHouse;
        if (GUILayout.Button("Commercial [4]", GUILayout.Height(40))) currentTool = ToolMode.PlaceCommercial;
        if (GUILayout.Button("Light [5]", GUILayout.Height(40))) currentTool = ToolMode.PlaceTrafficLight;
        if (GUILayout.Button("Undo [U]", GUILayout.Height(40))) Undo();
        GUILayout.EndHorizontal();
        GUILayout.EndArea();

        if (Time.unscaledTime < statusUntil)
            GUI.Label(new Rect(ToolbarRect.x, ToolbarRect.y - 26f, 700f, 22f), status);

        Vector2 m = Event.current.mousePosition;
        GUI.Label(new Rect(m.x + 15, m.y + 15, 260, 20),
            "Tool: " + currentTool + (undoStack.Count > 0 ? "  (U = undo)" : ""));
    }
}