using UnityEngine;

/// <summary>
/// Master environment clock. Evaluated by TrafficPool to drive systemic O-D flow.
/// </summary>
public class TimeManager : MonoBehaviour
{
    [Header("Time Settings")]
    public float timeScale = 60f;
    public float currentTimeInSeconds = 28800f;

    public enum TimePhase { Night, MorningRush, MidDay, EveningRush }
    public TimePhase currentPhase;

    void Update()
    {
        currentTimeInSeconds += Time.deltaTime * timeScale;
        if (currentTimeInSeconds >= 86400f) currentTimeInSeconds -= 86400f;

        int hours = Mathf.FloorToInt(currentTimeInSeconds / 3600f);

        if (hours >= 8 && hours < 11) currentPhase = TimePhase.MorningRush;
        else if (hours >= 16 && hours < 20) currentPhase = TimePhase.EveningRush;
        else if (hours >= 22 || hours < 6) currentPhase = TimePhase.Night;
        else currentPhase = TimePhase.MidDay;
    }
}