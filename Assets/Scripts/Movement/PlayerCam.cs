using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerCam : MonoBehaviour
{
    // Degrees turned per unit of mouse movement. Same value in every scene,
    // so set it here (or on the prefab) instead of per scene.
    [Range(0.1f, 10f)] public float sensitivity = 1.5f;

    public Transform orientation;

    float xRotation;
    float yRotation;

    public bool canRotate;

    /// <summary>Turns the view to look at a point (e.g. when a day starts in your room, facing your PC).</summary>
    public void LookTowards(Vector3 point)
    {
        Vector3 d = point - transform.position;
        if (d.sqrMagnitude < 0.0001f) return;
        yRotation = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
        xRotation = Mathf.Clamp(-Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg, -90f, 90f);
        transform.rotation = Quaternion.Euler(xRotation, yRotation, 0f);
        if (orientation) orientation.rotation = Quaternion.Euler(0f, yRotation, 0f);
    }

    private void Start()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        canRotate = true;
    }

    private void Update()
    {
        if (canRotate)
        {
            // Mouse axes are already "movement since last frame", so they must NOT be
            // multiplied by Time.deltaTime. Doing that made turn speed change with FPS
            // and made the view jump whenever a frame took longer.
            float mouseX = Input.GetAxisRaw("Mouse X") * sensitivity;
            float mouseY = Input.GetAxisRaw("Mouse Y") * sensitivity;

            yRotation += mouseX;

            xRotation -= mouseY;
            xRotation = Mathf.Clamp(xRotation, -90f, 90f);

            // rotate cam and orientation
            transform.rotation = Quaternion.Euler(xRotation, yRotation, 0);
            orientation.rotation = Quaternion.Euler(0, yRotation, 0);
        }

    }
}