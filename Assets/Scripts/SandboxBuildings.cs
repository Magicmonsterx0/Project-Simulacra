using System.Collections.Generic;
using UnityEngine;

public class SandboxBuildings : MonoBehaviour
{
    private SandboxController controller;

    // Store the actual GameObject now, not just the raw mesh data
    private struct BuildingTemplate
    {
        public GameObject originalObject;
        public float area;
    }
    private List<BuildingTemplate> buildingCatalog = new List<BuildingTemplate>();

    private bool isZoning = false;
    private Vector3 zoneStart;
    private LineRenderer zonePreview;

    private float currentRotationAngle = 0f;

    void Awake() { controller = GetComponent<SandboxController>(); }

    void Start()
    {
        GameObject lineObj = new GameObject("ZonePreviewLine");
        zonePreview = lineObj.AddComponent<LineRenderer>();
        zonePreview.positionCount = 5;
        zonePreview.startWidth = 0.5f;
        zonePreview.endWidth = 0.5f;
        zonePreview.material = new Material(Shader.Find("Sprites/Default"));
        zonePreview.enabled = false;

        BuildAssetCatalog();
    }

    private void BuildAssetCatalog()
    {
        GameObject visualCity = GameObject.Find("city_mesh_1x1km");
        if (visualCity == null) return;

        foreach (Transform child in visualCity.transform)
        {
            if (child.name.Contains("osm_buildings"))
            {
                MeshRenderer mr = child.GetComponent<MeshRenderer>();

                if (mr != null)
                {
                    buildingCatalog.Add(new BuildingTemplate
                    {
                        originalObject = child.gameObject,
                        area = mr.bounds.size.x * mr.bounds.size.z
                    });
                }
            }
        }
    }

    public void HandleZoning(bool hitSomething, RaycastHit hit, string type)
    {
        if (Input.GetKeyDown(KeyCode.R))
        {
            currentRotationAngle = (currentRotationAngle + 45f) % 360f;
        }

        if (!hitSomething) return;

        if (Input.GetMouseButtonDown(0))
        {
            isZoning = true;
            zoneStart = hit.point;
            zonePreview.enabled = true;

            Quaternion autoRot = GetRoadAlignment(zoneStart);
            currentRotationAngle = autoRot.eulerAngles.y;
        }

        if (isZoning && Input.GetMouseButton(0))
        {
            DrawZonePreview(zoneStart, hit.point);
        }

        if (isZoning && Input.GetMouseButtonUp(0))
        {
            isZoning = false;
            zonePreview.enabled = false;

            if (Vector3.Distance(zoneStart, hit.point) > 4f)
            {
                FinalizeZoning(zoneStart, hit.point, type);
            }
        }
    }

    private void DrawZonePreview(Vector3 start, Vector3 end)
    {
        Vector3[] corners = GetRotatedBoxCorners(start, end, currentRotationAngle);
        for (int i = 0; i < 4; i++)
        {
            zonePreview.SetPosition(i, corners[i] + (Vector3.up * 0.5f));
        }
        zonePreview.SetPosition(4, corners[0] + (Vector3.up * 0.5f));

        zonePreview.startColor = IsZoneClear(start, end, currentRotationAngle) ? new Color(0, 1, 0, 0.5f) : new Color(1, 0, 0, 0.5f);
        zonePreview.endColor = zonePreview.startColor;
    }

    private void FinalizeZoning(Vector3 start, Vector3 end, string type)
    {
        if (!IsZoneClear(start, end, currentRotationAngle))
        {
            Debug.LogWarning("[SANDBOX] Zone overlaps infrastructure.");
            return;
        }

        Vector3 center = (start + end) / 2f;

        // Ensure the building only connects to a road within 40 meters
        string nearestNode = controller.FindNearestNode(center, 40f);
        if (nearestNode == null)
        {
            Debug.LogWarning("[SANDBOX] No road nearby! Building must be placed closer to a street.");
            return; // Reject placement so it doesn't create an isolated dead zone
        }

        float targetArea = Mathf.Abs(start.x - end.x) * Mathf.Abs(start.z - end.z);

        // Force a massive capacity (5000) so the TrafficPool prioritizes your new commercial zone
        int capacity = type == "commercial" ? 5000 : 100;

        BuildingTemplate bestTemplate = FindBestMatchingTemplate(targetArea);
        GameObject newBuilding = CreateNativeBuilding(center, Quaternion.Euler(0, currentRotationAngle, 0), bestTemplate, type, start, end);

        var newRecord = new SimulacraBuilding
        {
            osm_id = "sb_" + System.Guid.NewGuid().ToString().Substring(0, 8),
            type = type,
            capacity = capacity,
            centroid = new Vector3Data { x = center.x, y = center.y, z = center.z },
            nearest_road_node = nearestNode
        };

        controller.dataLoader.cityData.buildings.Add(newRecord);
        controller.trafficPool.RefreshCityGraph();

        controller.undoStack.Push(() => {
            controller.dataLoader.cityData.buildings.Remove(newRecord);
            Destroy(newBuilding);
            controller.trafficPool.RefreshCityGraph();
        });
    }

    private GameObject CreateNativeBuilding(Vector3 position, Quaternion rotation, BuildingTemplate template, string type, Vector3 start, Vector3 end)
    {
        GameObject parent = new GameObject("way_sandbox_" + System.Guid.NewGuid().ToString().Substring(0, 5));
        parent.tag = "PlayerBuilding";
        parent.transform.position = position;
        parent.transform.rotation = rotation; // This is your preview box rotation

        Color tint = type == "residential" ? new Color(0.8f, 1f, 0.8f) : new Color(0.8f, 0.8f, 1f);

        GameObject visual = Instantiate(template.originalObject, parent.transform);
        visual.name = "Mesh";

        // THE FIX: Strip the random real-world Y rotation from the cloned object, 
        // but keep the X/Z rotations intact so it doesn't faceplant into the ground.
        Vector3 origRot = visual.transform.localEulerAngles;
        visual.transform.localRotation = Quaternion.Euler(origRot.x, 0, origRot.z);

        // Remove the original mesh collider so it doesn't conflict with our new bounding box
        foreach (Collider c in visual.GetComponentsInChildren<Collider>()) { Destroy(c); }

        MeshRenderer mr = visual.GetComponent<MeshRenderer>();
        mr.material = new Material(mr.sharedMaterial);
        mr.material.color = mr.material.color * tint;

        Vector3 currentGeometryCenter = mr.bounds.center;
        Vector3 offsetToCenter = parent.transform.position - currentGeometryCenter;
        visual.transform.position += offsetToCenter;

        float bottomEdge = mr.bounds.min.y;
        float yCorrection = parent.transform.position.y - bottomEdge;
        visual.transform.position += new Vector3(0, yCorrection, 0);

        float width = Mathf.Abs(start.x - end.x);
        float length = Mathf.Abs(start.z - end.z);

        BoxCollider bc = parent.AddComponent<BoxCollider>();
        bc.size = new Vector3(width, 15f, length);
        bc.center = Vector3.up * 7.5f;

        return parent;
    }

    private BuildingTemplate FindBestMatchingTemplate(float targetArea)
    {
        BuildingTemplate best = buildingCatalog[0];
        float smallestDiff = float.MaxValue;

        foreach (var template in buildingCatalog)
        {
            float diff = Mathf.Abs(template.area - targetArea);
            if (diff < smallestDiff)
            {
                smallestDiff = diff;
                best = template;
            }
        }
        return best;
    }

    private bool IsZoneClear(Vector3 start, Vector3 end, float angle)
    {
        Vector3 center = (start + end) / 2f;
        float width = Mathf.Abs(start.x - end.x);
        float length = Mathf.Abs(start.z - end.z);

        Vector3 halfExtents = new Vector3(width / 2f, 10f, length / 2f);

        Collider[] hits = Physics.OverlapBox(center + (Vector3.up * 10f), halfExtents * 0.95f, Quaternion.Euler(0, angle, 0));

        foreach (Collider hit in hits)
        {
            if (hit.name.Contains("osm_roads") || hit.name.StartsWith("way_") || hit.CompareTag("PlayerRoad") ||
                hit.CompareTag("PlayerBuilding") || hit.name.Contains("osm_buildings"))
            {
                return false;
            }
        }
        return true;
    }

    private Vector3[] GetRotatedBoxCorners(Vector3 start, Vector3 end, float angle)
    {
        Vector3 center = (start + end) / 2f;
        float width = Mathf.Abs(start.x - end.x);
        float length = Mathf.Abs(start.z - end.z);
        Vector3 halfExtents = new Vector3(width / 2f, 0, length / 2f);

        Quaternion rot = Quaternion.Euler(0, angle, 0);

        Vector3[] corners = new Vector3[4];
        corners[0] = center + rot * new Vector3(-halfExtents.x, 0, -halfExtents.z);
        corners[1] = center + rot * new Vector3(-halfExtents.x, 0, halfExtents.z);
        corners[2] = center + rot * new Vector3(halfExtents.x, 0, halfExtents.z);
        corners[3] = center + rot * new Vector3(halfExtents.x, 0, -halfExtents.z);
        return corners;
    }

    private Quaternion GetRoadAlignment(Vector3 pos)
    {
        var data = controller.dataLoader.cityData;
        Vector3 bestA = Vector3.zero, bestB = Vector3.zero;
        float bestDist = float.MaxValue;

        foreach (var kvp in data.adjacency_list)
        {
            if (!data.nodes.TryGetValue(kvp.Key, out var nodeA)) continue;
            foreach (var edge in kvp.Value)
            {
                if (!data.nodes.TryGetValue(edge.target, out var nodeB)) continue;
                float d = DistanceToSegmentXZ(pos, nodeA.Position, nodeB.Position);
                if (d < bestDist) { bestDist = d; bestA = nodeA.Position; bestB = nodeB.Position; }
            }
        }

        if (bestDist < 50f)
        {
            Vector3 roadDir = (bestB - bestA).normalized;
            Vector3 faceDir = new Vector3(-roadDir.z, 0, roadDir.x);
            if (Vector3.Dot(faceDir, ((bestA + bestB) / 2f) - pos) < 0) faceDir = -faceDir;
            return Quaternion.LookRotation(faceDir);
        }
        return Quaternion.identity;
    }

    private float DistanceToSegmentXZ(Vector3 p, Vector3 a, Vector3 b)
    {
        Vector2 P = new Vector2(p.x, p.z), A = new Vector2(a.x, a.z), B = new Vector2(b.x, b.z);
        Vector2 ab = B - A;
        float len2 = ab.sqrMagnitude;
        if (len2 < 1e-6f) return Vector2.Distance(P, A);
        float t = Mathf.Clamp01(Vector2.Dot(P - A, ab) / len2);
        return Vector2.Distance(P, A + ab * t);
    }

    public void BulldozeBuilding(GameObject target)
    {
        if (!target.CompareTag("PlayerBuilding") && !target.name.Contains("osm_buildings")) return;

        SimulacraBuilding closestData = null;
        float minDist = float.MaxValue;

        foreach (var b in controller.dataLoader.cityData.buildings)
        {
            if (b.centroid == null) continue;
            float dist = Vector3.Distance(b.centroid.ToUnityVector(), target.transform.position);
            if (dist < minDist) { minDist = dist; closestData = b; }
        }

        if (closestData != null) controller.dataLoader.cityData.buildings.Remove(closestData);
        target.SetActive(false);
        controller.trafficPool.RefreshCityGraph();
    }
}