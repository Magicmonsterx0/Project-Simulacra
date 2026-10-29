using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Pure data classes for JSON deserialization. Maps the Python extraction 
/// output directly into C# memory structures.
/// </summary>

[System.Serializable]
public class Vector3Data
{
    public float x, y, z;
    public Vector3 ToUnityVector() => new Vector3(x, y, z);
}

[System.Serializable]
public class SimulacraNode
{
    public string id;
    public float x, y, z;
    public bool is_intersection;
    public bool is_signal;
    public string type;

    // Runtime RL State Variables
    [System.NonSerialized] public bool hasTrafficLight = false;
    [System.NonSerialized] public bool isGreenLight = true;

    public Vector3 Position => new Vector3(x, y, z);
}

[System.Serializable]
public class SimulacraEdge
{
    public string target;
    public float distance;
    public int speed_limit_kmh;
    public int lanes;
    public float weight_time_sec;
    public string road_type;
}

[System.Serializable]
public class SimulacraBuilding
{
    public string osm_id;
    public string type;
    public Vector3Data centroid;
    public float footprint_area_sqm;
    public int levels;
    public int capacity;
    public string nearest_road_node;
}

[System.Serializable]
public class SimulacraMasterData
{
    public Dictionary<string, float> metadata;
    public Dictionary<string, SimulacraNode> nodes;
    public Dictionary<string, List<SimulacraEdge>> adjacency_list;
    public List<SimulacraBuilding> buildings;
}