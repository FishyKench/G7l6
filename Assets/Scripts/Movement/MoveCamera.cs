using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class MoveCamera : MonoBehaviour
{
    public Transform cameraPosition;

    // LateUpdate runs after the player has moved this frame, so the camera
    // never lags one frame behind the body (that lag shows up as jitter).
    private void LateUpdate()
    {
        transform.position = cameraPosition.position;
    }
}
