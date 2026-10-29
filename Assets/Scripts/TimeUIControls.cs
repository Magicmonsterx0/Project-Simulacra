using UnityEngine;

/// <summary>
/// Lightweight editor UI. Interfaces natively with Unity's global time physics 
/// to allow for rapid simulation evaluation.
/// </summary>
public class TimeUIControls : MonoBehaviour
{
    public TimeManager timeManager;

    void OnGUI()
    {
        if (timeManager == null) return;

        GUI.Box(new Rect(10, 10, 320, 130), "Digital Twin Controls");

        int hours = Mathf.FloorToInt(timeManager.currentTimeInSeconds / 3600f);
        int minutes = Mathf.FloorToInt((timeManager.currentTimeInSeconds % 3600f) / 60f);
        string amPm = hours >= 12 ? "PM" : "AM";
        int dispHours = hours % 12;
        if (dispHours == 0) dispHours = 12;

        string timeString = string.Format("{0:00}:{1:00} {2}", dispHours, minutes, amPm);

        GUIStyle style = new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold };
        GUI.Label(new Rect(20, 35, 300, 25), $"Clock: {timeString}  [{timeManager.currentPhase}]", style);

        GUI.Label(new Rect(20, 65, 100, 20), $"Speed: {Mathf.RoundToInt(Time.timeScale)}x");
        Time.timeScale = GUI.HorizontalSlider(new Rect(120, 70, 180, 20), Time.timeScale, 0f, 20f);

        GUI.Label(new Rect(20, 95, 100, 20), "Skip To Time:");
        timeManager.currentTimeInSeconds = GUI.HorizontalSlider(new Rect(120, 100, 180, 20), timeManager.currentTimeInSeconds, 0f, 86400f);
    }
}