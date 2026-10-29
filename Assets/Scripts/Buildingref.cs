using UnityEngine;

/// <summary>
/// Links a visual building object to its record in SimulacraMasterData.buildings (by osm_id).
/// Replaces tag/name/nearest-distance guessing, which is fragile.
/// </summary>
public class BuildingRef : MonoBehaviour
{
    public string osmId;
}
