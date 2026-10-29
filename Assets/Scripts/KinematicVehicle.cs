using System.Collections.Generic;
using UnityEngine;

public class KinematicVehicle : MonoBehaviour
{
    public struct Waypoint
    {
        public string NodeId;
        public Vector3 Position;
        public int Lanes;
    }

    public List<Waypoint> routeWaypoints;
    public int currentWaypointIndex = 0;

    private float currentSpeedMPS = 0f;
    private float maxTargetSpeedMPS = 10f;
    private TrafficPool poolManager;
    private bool isTwoWheeler = false;

    private int myAssignedLane = 0;

    // --- BEHAVIOR & GRIDLOCK METRICS ---
    private float frustrationTimer = 0f;
    private float lateralSqueezeOffset = 0f;
    private float squeezeDirection = 1f;

    [HideInInspector] public float timeStuck = 0f;
    public bool IsGridlocked => timeStuck > 4.0f; // DDS reads this metric

    public void AssignRoute(List<Waypoint> waypoints, float speedKmh, TrafficPool pool)
    {
        routeWaypoints = waypoints;
        poolManager = pool;
        currentWaypointIndex = 0;

        // FIXED: Restored missing multiplication (*) symbol
        maxTargetSpeedMPS = speedKmh * (1000f / 3600f);
        currentSpeedMPS = maxTargetSpeedMPS;

        frustrationTimer = 0f;
        lateralSqueezeOffset = 0f;
        timeStuck = 0f;

        isTwoWheeler = transform.localScale.x < 1.0f;
        squeezeDirection = Random.value > 0.5f ? 1f : -1f;

        if (routeWaypoints != null && routeWaypoints.Count > 0)
        {
            myAssignedLane = Random.Range(0, Mathf.Max(1, routeWaypoints[0].Lanes));

            // INSTANT TELEPORT FIX: Securely placed inside the safety block
            transform.position = CalculateLaneOffset(0);
            gameObject.SetActive(true);
        }
    }

    public bool RouteUsesEdge(string nodeA, string nodeB)
    {
        if (routeWaypoints == null) return false;
        for (int i = Mathf.Max(0, currentWaypointIndex - 1); i < routeWaypoints.Count - 1; i++)
        {
            string x = routeWaypoints[i].NodeId;
            string y = routeWaypoints[i + 1].NodeId;
            if ((x == nodeA && y == nodeB) || (x == nodeB && y == nodeA)) return true;
        }
        return false;
    }

    void Update()
    {
        if (routeWaypoints == null || currentWaypointIndex >= routeWaypoints.Count) return;

        Vector3 baseTarget = CalculateLaneOffset(currentWaypointIndex);
        Vector3 forward = (baseTarget - transform.position).normalized;
        Vector3 right = new Vector3(forward.z, 0, -forward.x).normalized;
        Vector3 dynamicTarget = baseTarget + (right * lateralSqueezeOffset);

        // 1. SMART INFRASTRUCTURE SENSOR
        bool isRedLightAhead = false;
        string targetNodeId = routeWaypoints[currentWaypointIndex].NodeId;
        string prevNodeId = currentWaypointIndex > 0 ? routeWaypoints[currentWaypointIndex - 1].NodeId : null;

        if (poolManager != null && poolManager.dataLoader.cityData.nodes.TryGetValue(targetNodeId, out SimulacraNode targetNode))
        {
            if (targetNode.hasTrafficLight)
            {
                if (SandboxSignals.intersectionGreenApproach.TryGetValue(targetNodeId, out string greenApproach))
                {
                    if (prevNodeId != greenApproach && Vector3.Distance(transform.position, baseTarget) < 12.0f)
                    {
                        isRedLightAhead = true;
                    }
                }
                else { isRedLightAhead = true; }
            }
        }

        // 2. WHISKER SENSORS 
        bool isPathBlocked = false;
        bool isSideBlocked = false;

        Vector3 sensorOrigin = transform.position + (Vector3.up * 0.5f);
        float forwardDist = isTwoWheeler ? 2.5f : 5.0f;
        float sideDist = 3.0f;

        if (Physics.Raycast(sensorOrigin, transform.forward, out RaycastHit fHit, forwardDist))
        {
            if (fHit.collider.CompareTag("Vehicle")) isPathBlocked = true;
        }

        Vector3 leftWhisker = Quaternion.Euler(0, -35, 0) * transform.forward;
        Vector3 rightWhisker = Quaternion.Euler(0, 35, 0) * transform.forward;

        if (Physics.Raycast(sensorOrigin, leftWhisker, out RaycastHit lHit, sideDist) && lHit.collider.CompareTag("Vehicle")) isSideBlocked = true;
        if (Physics.Raycast(sensorOrigin, rightWhisker, out RaycastHit rHit, sideDist) && rHit.collider.CompareTag("Vehicle")) isSideBlocked = true;

        // 3. BRAKING & SAFE DESPERATION
        if (isRedLightAhead && !isPathBlocked)
        {
            currentSpeedMPS = Mathf.Lerp(currentSpeedMPS, 0f, Time.deltaTime * 6f);
            frustrationTimer = 0f;
            lateralSqueezeOffset = Mathf.Lerp(lateralSqueezeOffset, 0f, Time.deltaTime * 2f);
        }
        else if (isPathBlocked)
        {
            timeStuck += Time.deltaTime;
            frustrationTimer += Time.deltaTime;

            if (timeStuck > 8.0f)
            {
                float maxSqueeze = isTwoWheeler ? 3.5f : 2.0f;
                // FIXED: Restored multiplication (*) symbol
                lateralSqueezeOffset = Mathf.Lerp(lateralSqueezeOffset, maxSqueeze * squeezeDirection, Time.deltaTime);
                currentSpeedMPS = isPathBlocked ? 0.5f : 2.5f;
            }
            else if (frustrationTimer > 1.5f && !isSideBlocked)
            {
                float maxSqueeze = isTwoWheeler ? 3.0f : 1.5f;
                // FIXED: Restored multiplication (*) symbol
                lateralSqueezeOffset = Mathf.Lerp(lateralSqueezeOffset, maxSqueeze * squeezeDirection, Time.deltaTime * 0.5f);
                currentSpeedMPS = Mathf.Lerp(currentSpeedMPS, isTwoWheeler ? 3f : 1f, Time.deltaTime);
            }
            else
            {
                currentSpeedMPS = Mathf.Lerp(currentSpeedMPS, 0f, Time.deltaTime * 8f);
            }
        }
        else if (isSideBlocked && !isPathBlocked)
        {
            timeStuck += Time.deltaTime * 0.5f;
            currentSpeedMPS = Mathf.Lerp(currentSpeedMPS, 1.5f, Time.deltaTime * 2f);
        }
        else
        {
            timeStuck = 0f;
            frustrationTimer = Mathf.Max(0, frustrationTimer - Time.deltaTime);
            lateralSqueezeOffset = Mathf.Lerp(lateralSqueezeOffset, 0f, Time.deltaTime * 1f);
            currentSpeedMPS = Mathf.Lerp(currentSpeedMPS, maxTargetSpeedMPS, Time.deltaTime * 2f);
        }

        // 4. SMOOTH ROTATION FIX
        Vector3 direction = dynamicTarget - transform.position;
        direction.y = 0;

        if (direction.sqrMagnitude > 0.1f)
        {
            Quaternion targetRot = Quaternion.LookRotation(direction);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, Time.deltaTime * 8f);
        }

        transform.position = Vector3.MoveTowards(transform.position, dynamicTarget, currentSpeedMPS * Time.deltaTime);

        // 5. OVERSHOOT FORGIVENESS
        float distToTarget = Vector3.Distance(transform.position, dynamicTarget);
        Vector3 toWaypoint = dynamicTarget - transform.position;
        float dotProduct = Vector3.Dot(transform.forward, toWaypoint);

        if (distToTarget < 2.5f || dotProduct < 0f)
        {
            currentWaypointIndex++;
            if (currentWaypointIndex >= routeWaypoints.Count)
            {
                poolManager.ReturnVehicleToPool(this);
            }
            else
            {
                myAssignedLane = Mathf.Min(myAssignedLane, Mathf.Max(0, routeWaypoints[currentWaypointIndex].Lanes - 1));
            }
        }
    }

    private Vector3 CalculateLaneOffset(int index)
    {
        Vector3 currentTarget = routeWaypoints[index].Position;
        Vector3 nextTarget;

        if (index < routeWaypoints.Count - 1) nextTarget = routeWaypoints[index + 1].Position;
        else if (index > 0)
        {
            // FIXED: Restored minus (-) symbol for the array index
            Vector3 prevTarget = routeWaypoints[index - 1].Position;
            nextTarget = currentTarget + (currentTarget - prevTarget);
        }
        else return currentTarget;

        Vector3 forward = (nextTarget - currentTarget).normalized;
        Vector3 left = new Vector3(-forward.z, 0, forward.x).normalized;

        float laneWidth = 3.0f;
        float lateralShift = (myAssignedLane * laneWidth) + (laneWidth / 2f);

        return currentTarget + (left * lateralShift);
    }
}