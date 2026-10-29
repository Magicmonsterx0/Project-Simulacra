using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SandboxSignals : MonoBehaviour
{
    private SandboxController controller;

    // Global tracker so cars know WHICH incoming road currently has the green light
    public static Dictionary<string, string> intersectionGreenApproach = new Dictionary<string, string>();

    private Dictionary<string, Coroutine> activeLightCycles = new Dictionary<string, Coroutine>();
    private Dictionary<string, List<GameObject>> activePoles = new Dictionary<string, List<GameObject>>();
    private Dictionary<string, Renderer> ledRenderers = new Dictionary<string, Renderer>();

    void Awake() { controller = GetComponent<SandboxController>(); }

    public void ToggleTrafficLight(Vector3 clickPosition)
    {
        string nearestNode = controller.FindNearestNode(clickPosition, 15f);
        if (nearestNode == null) return;

        SimulacraNode node = controller.dataLoader.cityData.nodes[nearestNode];

        if (!node.hasTrafficLight)
        {
            node.hasTrafficLight = true;

            // Map the intersection: Find all incoming roads
            List<string> incomingNodes = new List<string>();
            foreach (var kvp in controller.dataLoader.cityData.adjacency_list)
            {
                foreach (var edge in kvp.Value)
                {
                    if (edge.target == nearestNode && !incomingNodes.Contains(kvp.Key))
                    {
                        incomingNodes.Add(kvp.Key);
                    }
                }
            }

            if (incomingNodes.Count > 0)
            {
                BuildSmartIntersection(nearestNode, incomingNodes);
                activeLightCycles[nearestNode] = StartCoroutine(CycleTrafficLight(nearestNode, incomingNodes));
            }
        }
        else
        {
            // Second click: Remove the entire intersection system
            node.hasTrafficLight = false;
            if (activeLightCycles.ContainsKey(nearestNode)) StopCoroutine(activeLightCycles[nearestNode]);
            if (activePoles.ContainsKey(nearestNode))
            {
                foreach (var pole in activePoles[nearestNode]) Destroy(pole);
                activePoles.Remove(nearestNode);
            }
            intersectionGreenApproach.Remove(nearestNode);
            activeLightCycles.Remove(nearestNode);
        }
    }

    private void BuildSmartIntersection(string intersectionId, List<string> incomingNodes)
    {
        activePoles[intersectionId] = new List<GameObject>();
        Vector3 centerPos = controller.dataLoader.cityData.nodes[intersectionId].Position;

        foreach (string incomingId in incomingNodes)
        {
            Vector3 inPos = controller.dataLoader.cityData.nodes[incomingId].Position;
            Vector3 dirFromIn = (centerPos - inPos).normalized;

            // Place pole at the right-side corner of the incoming road
            Vector3 polePos = centerPos - (dirFromIn * 6f) + (new Vector3(-dirFromIn.z, 0, dirFromIn.x) * 4f);

            GameObject poleGroup = new GameObject($"TrafficLight_{intersectionId}_{incomingId}");
            poleGroup.transform.position = polePos;
            poleGroup.transform.rotation = Quaternion.LookRotation(-dirFromIn); // Face the incoming traffic

            GameObject pole = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pole.transform.SetParent(poleGroup.transform);
            pole.transform.localPosition = new Vector3(0, 3f, 0);
            pole.transform.localScale = new Vector3(0.3f, 3f, 0.3f);
            pole.GetComponent<Renderer>().material.color = new Color(0.2f, 0.2f, 0.2f);
            Destroy(pole.GetComponent<Collider>());

            GameObject lightBox = GameObject.CreatePrimitive(PrimitiveType.Cube);
            lightBox.transform.SetParent(poleGroup.transform);
            lightBox.transform.localPosition = new Vector3(0, 5.5f, -0.3f);
            lightBox.transform.localScale = new Vector3(0.7f, 1.8f, 0.7f);
            lightBox.GetComponent<Renderer>().material.color = Color.black;
            Destroy(lightBox.GetComponent<Collider>());

            GameObject led = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            led.transform.SetParent(poleGroup.transform);
            led.transform.localPosition = new Vector3(0, 5.5f, -0.6f);
            led.transform.localScale = new Vector3(0.5f, 0.5f, 0.5f);
            Renderer ledRenderer = led.GetComponent<Renderer>();
            ledRenderer.material = new Material(Shader.Find("Unlit/Color"));
            Destroy(led.GetComponent<Collider>());

            ledRenderers[$"{intersectionId}_{incomingId}"] = ledRenderer;
            activePoles[intersectionId].Add(poleGroup);
        }
    }

    private IEnumerator CycleTrafficLight(string intersectionId, List<string> incomingNodes)
    {
        int currentIndex = 0;
        while (true)
        {
            string currentGreen = incomingNodes[currentIndex];
            intersectionGreenApproach[intersectionId] = currentGreen;

            foreach (string incomingId in incomingNodes)
            {
                if (ledRenderers.TryGetValue($"{intersectionId}_{incomingId}", out Renderer r))
                {
                    r.material.color = (incomingId == currentGreen) ? Color.green : Color.red;
                }
            }

            yield return new WaitForSeconds(8f); // Green for 8 seconds

            // Yellow Phase
            if (ledRenderers.TryGetValue($"{intersectionId}_{currentGreen}", out Renderer yr)) yr.material.color = Color.yellow;

            // Instantly tell cars it's red so they brake, while visually holding yellow for 3s
            intersectionGreenApproach[intersectionId] = "none";
            yield return new WaitForSeconds(3f);

            currentIndex = (currentIndex + 1) % incomingNodes.Count;
        }
    }
}