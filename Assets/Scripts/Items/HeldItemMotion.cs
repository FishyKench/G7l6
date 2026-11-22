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

    // ------------------------------------------------------------------ never inside a wall
    // When something is right in front of you (a wall, a cabinet, the fridge), the hand pulls in towards your eye and the item shrinks
    // by the same amount: on screen it looks exactly the same, but it's now in front of whatever was there instead of poking through.

    float clear = 1f;
    static readonly RaycastHit[] hits = new RaycastHit[16];

    /// <summary>Back to full size / full reach at once (before an item leaves your hands or a new one arrives).</summary>
    public static void ResetClearance()
    {
        var m = PlayerCarry.Instance && PlayerCarry.Instance.holdPoint ? PlayerCarry.Instance.holdPoint.GetComponent<HeldItemMotion>() : null;
        if (!m) return;
        m.clear = 1f;
        m.Apply();
    }

    float WantClearance()
    {
        var carry = PlayerCarry.Instance;
        var item = carry ? carry.Held : null;
        var cam = transform.parent;
        if (!item || !cam) return 1f;
        float r = 0f;
        bool any = false;
        Bounds b = default;
        foreach (var rend in item.GetComponentsInChildren<Renderer>())
        {
            if (!rend.enabled) continue;
            if (!any) { b = rend.bounds; any = true; } else b.Encapsulate(rend.bounds);
        }
        if (!any) return 1f;
        r = b.extents.magnitude / Mathf.Max(0.05f, clear) * 0.9f; // its size at full scale
        Vector3 o = cam.position;
        Vector3 hold = cam.TransformPoint(basePosition + offset);
        Vector3 dir = hold - o;
        float L = dir.magnitude;
        if (L < 1e-3f) return 1f;
        dir /= L;
        float reach = L + r;
        float alpha = Mathf.Asin(Mathf.Clamp(r / Mathf.Max(L, r * 1.01f), 0f, 0.9f));
        var side = Vector3.Cross(dir, Vector3.up).sqrMagnitude > 1e-4f ? Vector3.Cross(dir, Vector3.up).normalized : Vector3.right;
        float tmin = reach;
        for (int ring = 0; ring < 3; ring++)
        {
            int n = ring == 0 ? 1 : 8;
            float a = ring == 0 ? 0f : alpha * (ring == 1 ? 0.5f : 1f);
            for (int i = 0; i < n; i++)
            {
                var d = Quaternion.AngleAxis(i * 360f / n, dir) * (Quaternion.AngleAxis(a * Mathf.Rad2Deg, side) * dir);
                int count = Physics.RaycastNonAlloc(o, d, hits, reach + 0.05f, ~0, QueryTriggerInteraction.Ignore);
                for (int k = 0; k < count; k++)
                {
                    var c = hits[k].collider;
                    if (!c || c.transform.IsChildOf(item.transform) || c.gameObject.layer == 6) continue; // the item itself, the player
                    if (c.attachedRigidbody && c.attachedRigidbody.GetComponent<PlayerMovementAdvanced>()) continue;
                    tmin = Mathf.Min(tmin, hits[k].distance);
                }
            }
        }
        return Mathf.Clamp((tmin - 0.02f) / reach, 0.08f, 1f);
    }

    void Apply()
    {
        transform.localPosition = (basePosition + offset) * clear;
        transform.localScale = Vector3.one * clear;
    }

    void LateUpdate()
    {
        bool posing = Time.frameCount - wantFrame <= 1;
        Vector3 to = posing ? wantOffset : Vector3.zero;
        Vector3 toTurn = posing ? wantTurn : Vector3.zero;
        float smooth = posing ? wantSmooth : 0.14f;
        offset = Vector3.SmoothDamp(offset, to, ref offsetVelocity, smooth);
        turn = Vector3.SmoothDamp(turn, toTurn, ref turnVelocity, smooth);
        transform.localRotation = baseRotation * Quaternion.Euler(turn);
        float want = WantClearance();
        clear = want < clear ? want : Mathf.MoveTowards(clear, want, Time.deltaTime * 2.5f); // in at once, back out smoothly
        Apply();
    }
}
