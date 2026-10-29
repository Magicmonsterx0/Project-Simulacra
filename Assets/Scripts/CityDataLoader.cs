using System.Collections.Generic;
using UnityEngine;
using Newtonsoft.Json;

public class CityDataLoader : MonoBehaviour
{
    [Header("Data Source")]
    public TextAsset simulacraJsonFile;
    [Header("Runtime Graph Data")]
    public SimulacraMasterData cityData;
    public Dictionary<string, GameObject> roadMeshes = new Dictionary<string, GameObject>();

    void Awake()
    {
        if (simulacraJsonFile != null)
        {
            cityData = JsonConvert.DeserializeObject<SimulacraMasterData>(simulacraJsonFile.text);
            if (cityData.buildings == null) cityData.buildings = new List<SimulacraBuilding>();
            MapVisualMeshes();
        }
    }

    private void MapVisualMeshes()
    {
        GameObject visualCity = GameObject.Find("city_mesh_1x1km");
        if (visualCity == null) return;

        int colliderCount = 0;
        foreach (Transform child in visualCity.transform)
        {
            if (child.GetComponent<MeshFilter>() != null && child.GetComponent<MeshCollider>() == null)
            {
                child.gameObject.AddComponent<MeshCollider>();
                colliderCount++;
            }

            // DETECT THE NEW BLENDER EXPORT NAMES
            if (child.name.StartsWith("way_") || child.name.Contains("osm_roads"))
            {
                roadMeshes[child.name] = child.gameObject;
            }
        }
        Debug.Log($"[CityDataLoader] Generated {colliderCount} colliders. Linked {roadMeshes.Count} roads.");
    }

    // ==========================================
    // DEBUG GIZMOS: Visualize Graph & Building Tethers
    // ==========================================
    void OnDrawGizmos()
    {
        if (cityData == null || cityData.nodes == null) return;

        // Draw the Turn Points (Nodes) as Cyan spheres
        Gizmos.color = Color.cyan;
        foreach (var node in cityData.nodes.Values)
        {
            Gizmos.DrawSphere(node.Position, 2f);
        }

        // Draw the Roads (Edges) as Magenta lines
        Gizmos.color = Color.magenta;
        if (cityData.adjacency_list != null)
        {
            foreach (var kvp in cityData.adjacency_list)
            {
                if (cityData.nodes.TryGetValue(kvp.Key, out var startNode))
                {
                    foreach (var edge in kvp.Value)
                    {
                        if (cityData.nodes.TryGetValue(edge.target, out var endNode))
                        {
                            Gizmos.DrawLine(startNode.Position, endNode.Position);
                        }
                    }
                }
            }
        }

        // Draw the Building Tethers as Yellow lines
        Gizmos.color = Color.yellow;
        if (cityData.buildings != null)
        {
            foreach (var building in cityData.buildings)
            {
                if (!string.IsNullOrEmpty(building.nearest_road_node) &&
                    cityData.nodes.TryGetValue(building.nearest_road_node, out var connectedNode))
                {
                    // Draws a line from the building's center directly to the road it pulls traffic from
                    Vector3 bCenter = new Vector3(building.centroid.x, building.centroid.y, building.centroid.z);
                    Gizmos.DrawLine(bCenter, connectedNode.Position);
                    Gizmos.DrawCube(bCenter, new Vector3(4f, 4f, 4f));
                }
            }
        }
    }
}