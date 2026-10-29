using UnityEngine;
using System.Collections.Generic;
using System;

public class SandboxController : MonoBehaviour
{
    public enum ToolMode { Cursor, Bulldoze, DrawRoad, PlaceHouse, PlaceCommercial, TrafficLight }
    public ToolMode currentTool = ToolMode.Cursor;

    public CityDataLoader dataLoader;
    public TrafficPool trafficPool;
    public SandboxRoads roadTool;
    public SandboxBuildings buildingTool;
    public SandboxSignals signalTool;

    public Stack<Action> undoStack = new Stack<Action>();

    void Update()
    {
        HandleInput();
    }

    private void HandleInput()
    {
        // Tool Selection
        if (Input.GetKeyDown(KeyCode.Alpha1)) currentTool = ToolMode.Cursor;
        if (Input.GetKeyDown(KeyCode.Alpha2)) currentTool = ToolMode.Bulldoze;
        if (Input.GetKeyDown(KeyCode.Alpha3)) currentTool = ToolMode.PlaceHouse;
        if (Input.GetKeyDown(KeyCode.Alpha4)) currentTool = ToolMode.PlaceCommercial;
        if (Input.GetKeyDown(KeyCode.Alpha5)) currentTool = ToolMode.DrawRoad;
        if (Input.GetKeyDown(KeyCode.Alpha6)) currentTool = ToolMode.TrafficLight;
        if (Input.GetKeyDown(KeyCode.U) && undoStack.Count > 0) undoStack.Pop().Invoke();

        // Single Click Tools
        if (Input.GetMouseButtonDown(0) && currentTool != ToolMode.DrawRoad && currentTool != ToolMode.PlaceHouse && currentTool != ToolMode.PlaceCommercial)
        {
            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out RaycastHit hit))
            {
                RouteToolClick(hit);
            }
        }

        // Drag Tools
        if (currentTool == ToolMode.DrawRoad)
        {
            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            roadTool.HandleRoadDrawing(Physics.Raycast(ray, out RaycastHit hit), hit);
        }
        else if (currentTool == ToolMode.PlaceHouse)
        {
            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            buildingTool.HandleZoning(Physics.Raycast(ray, out RaycastHit hit), hit, "residential");
        }
        else if (currentTool == ToolMode.PlaceCommercial)
        {
            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            buildingTool.HandleZoning(Physics.Raycast(ray, out RaycastHit hit), hit, "commercial");
        }
    }

    private void RouteToolClick(RaycastHit hit)
    {
        GameObject targetObj = hit.collider.gameObject;

        // 1. SMART SELECTION: If we hit a child mesh of something you built, grab the parent container.
        // If it's a native Blender object, it leaves it alone.
        if (targetObj.transform.parent != null)
        {
            string parentTag = targetObj.transform.parent.tag;
            if (parentTag == "PlayerRoad" || parentTag == "PlayerBuilding")
            {
                targetObj = targetObj.transform.parent.gameObject;
            }
        }

        switch (currentTool)
        {
            case ToolMode.Bulldoze:
                // 2. Destroy Roads
                if (targetObj.CompareTag("PlayerRoad") || targetObj.name.Contains("osm_roads") || targetObj.name.StartsWith("way_"))
                {
                    roadTool.BulldozeRoadAt(targetObj, hit.point);
                }
                // 3. Destroy Buildings
                else if (targetObj.CompareTag("PlayerBuilding") || targetObj.name.Contains("osm_buildings"))
                {
                    buildingTool.BulldozeBuilding(targetObj);
                }
                break;

            case ToolMode.TrafficLight:
                signalTool.ToggleTrafficLight(hit.point);
                break;
        }
    }

    // Restores your on-screen UI inventory
    void OnGUI()
    {
        GUIStyle style = new GUIStyle();
        style.fontSize = 24;
        style.normal.textColor = Color.white;
        GUI.Label(new Rect(20, Screen.height - 60, 500, 40), "Active Tool: " + currentTool.ToString(), style);
    }

    public string FindNearestNode(Vector3 pos, float radius)
    {
        string bestId = null;
        float bestDist = radius;
        foreach (var kvp in dataLoader.cityData.nodes)
        {
            float d = Vector3.Distance(pos, kvp.Value.Position);
            if (d < bestDist)
            {
                bestDist = d;
                bestId = kvp.Key;
            }
        }
        return bestId;
    }
}