using UnityEngine;

/// <summary>
/// Moves whatever you're holding: raising the remote and pressing it, swinging the mop, aiming the extinguisher.
/// Lives on the hold point under the camera. Tools call Pose() every frame they want the item somewhere else;
/// when they stop, it eases back to where it normally sits.
/// </summary>
public class HeldItemMotion : MonoBehaviour
{
    Vector3 basePosition;
    Quaternion baseRotation;
    Vector3 offset, offsetVelocity, turn, turnVelocity;
    Vector3 wantOffset, wantTurn;
    float wantSmooth = 0.08f;
    int wantFrame = -10;

    /// <summary>The motion for the player's hold point (added the first time it's needed).</summary>
    public static HeldItemMotion Get()
    {
        var carry = PlayerCarry.Instance;
        if (!carry || !carry.holdPoint) return null;
        var m = carry.holdPoint.GetComponent<HeldItemMotion>();
        if (!m) m = carry.holdPoint.gameObject.AddComponent<HeldItemMotion>();
        return m;
    }

    void Awake()
    {
        basePosition = transform.localPosition;
        baseRotation = transform.localRotation;
    }

    /// <summary>
    /// Hold the item this frame at 'move' metres from where it normally sits (camera space: x right, y up, z forward),
    /// turned by 'angles' degrees (x = tip forward/down, y = turn, z = roll).
    /// </summary>
    public void Pose(Vector3 move, Vector3 angles, float smoothTime = 0.08f)
    {
        wantOffset = move;
        wantTurn = angles;
        wantSmooth = Mathf.Max(0.01f, smoothTime);
        wantFrame = Time.frameCount;
    }

    void LateUpdate()
    {
        bool posing = Time.frameCount - wantFrame <= 1;
        Vector3 to = posing ? wantOffset : Vector3.zero;
        Vector3 toTurn = posing ? wantTurn : Vector3.zero;
        float smooth = posing ? wantSmooth : 0.14f;
        offset = Vector3.SmoothDamp(offset, to, ref offsetVelocity, smooth);
        turn = Vector3.SmoothDamp(turn, toTurn, ref turnVelocity, smooth);
        transform.localPosition = basePosition + offset;
        transform.localRotation = baseRotation * Quaternion.Euler(turn);
    }
}
