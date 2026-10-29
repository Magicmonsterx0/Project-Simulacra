using UnityEngine;

/// <summary>
/// Provides classic isometric oversight controls for observing bottlenecks.
/// </summary>
public class RTSCamera : MonoBehaviour
{
    public float panSpeed = 50f;
    public float scrollSpeed = 500f;

    void Update()
    {
        Vector3 pos = transform.position;

        if (Input.GetKey("w")) pos.z += panSpeed * Time.deltaTime;
        if (Input.GetKey("s")) pos.z -= panSpeed * Time.deltaTime;
        if (Input.GetKey("d")) pos.x += panSpeed * Time.deltaTime;
        if (Input.GetKey("a")) pos.x -= panSpeed * Time.deltaTime;

        float scroll = Input.GetAxis("Mouse ScrollWheel");
        pos.y -= scroll * scrollSpeed * Time.deltaTime;

        pos.y = Mathf.Clamp(pos.y, 10f, 500f);

        transform.position = pos;
    }
}