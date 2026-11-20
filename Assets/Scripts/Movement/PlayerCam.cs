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