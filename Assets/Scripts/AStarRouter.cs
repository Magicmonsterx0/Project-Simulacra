using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Hardware-optimized A* pathfinding. Uses mathematical edges (travel time) 
/// rather than physical distance to route vehicles dynamically.
/// </summary>
public class AStarRouter : MonoBehaviour
{
    public CityDataLoader dataLoader;

    private class PathNode
    {
        public string id;
        public float gCost;
        public float hCost;
        public float fCost => gCost + hCost;
        public PathNode parent;
    }

    public List<string> FindShortestPath(string startNodeId, string targetNodeId)
    {
        var nodes = dataLoader.cityData.nodes;
        var edges = dataLoader.cityData.adjacency_list;

        if (!nodes.ContainsKey(startNodeId) || !nodes.ContainsKey(targetNodeId))
            return null;

        List<PathNode> openList = new List<PathNode>();
        HashSet<string> closedSet = new HashSet<string>();

        PathNode startNode = new PathNode
        {
            id = startNodeId,
            gCost = 0,
            hCost = GetHeuristic(nodes[startNodeId], nodes[targetNodeId])
        };

        openList.Add(startNode);

        while (openList.Count > 0)
        {
            openList.Sort((a, b) => a.fCost.CompareTo(b.fCost));
            PathNode current = openList[0];

            if (current.id == targetNodeId)
                return RetracePath(current);

            openList.RemoveAt(0);
            closedSet.Add(current.id);

            if (!edges.ContainsKey(current.id)) continue;

            foreach (var edge in edges[current.id])
            {
                if (closedSet.Contains(edge.target)) continue;

                float tentativeGCost = current.gCost + edge.weight_time_sec;
                PathNode neighborNode = openList.Find(n => n.id == edge.target);

                if (neighborNode == null)
                {
                    neighborNode = new PathNode
                    {
                        id = edge.target,
                        gCost = tentativeGCost,
                        hCost = GetHeuristic(nodes[edge.target], nodes[targetNodeId]),
                        parent = current
                    };
                    openList.Add(neighborNode);
                }
                else if (tentativeGCost < neighborNode.gCost)
                {
                    neighborNode.gCost = tentativeGCost;
                    neighborNode.parent = current;
                }
            }
        }
        return null;
    }

    private float GetHeuristic(SimulacraNode a, SimulacraNode b)
    {
        return Vector3.Distance(a.Position, b.Position);
    }

    private List<string> RetracePath(PathNode endNode)
    {
        List<string> path = new List<string>();
        PathNode current = endNode;
        while (current != null)
        {
            path.Add(current.id);
            current = current.parent;
        }
        path.Reverse();
        return path;
    }
}