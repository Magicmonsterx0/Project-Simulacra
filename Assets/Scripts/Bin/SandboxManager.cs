using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Infrastructure disruption tool. Dynamically severs graph edges during runtime 
/// to test systemic traffic adaptation.
/// </summary>
public class SandboxManager : MonoBehaviour
{
    public CityDataLoader dataLoader;

    void Update()
    {
        if (Input.GetMouseButtonDown(0))
        {
            BulldozeRoadAtMouse();
        }
    }

    private void BulldozeRoadAtMouse()
    {
        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);

        if (Physics.Raycast(ray, out RaycastHit hit))
        {
            GameObject clickedObject = hit.collider.gameObject;

            if (clickedObject.name.StartsWith("way_"))
            {
                string roadId = clickedObject.name;
                Debug.Log($"[BULLDOZER] Deleting road segment: {roadId}");

                RemoveRoadFromGraph(roadId);
                clickedObject.SetActive(false);
            }
        }
    }

    private void RemoveRoadFromGraph(string roadId)
    {
        var edges = dataLoader.cityData.adjacency_list;

        // Perform O(N) sweep to sever all inbound edges referencing this deleted segment
        foreach (var kvp in edges)
        {
            List<SimulacraEdge> outgoingEdges = kvp.Value;
            outgoingEdges.RemoveAll(edge => edge.target == roadId);
        }
    }
}