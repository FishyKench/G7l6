using UnityEngine;

public enum ItemType
{
    Remote,
    DirtyClothes,
    Extinguisher,
    Mop,
    Bucket,
}

/// <summary>
/// An object the player can pick up (E) and carry around, then drop (G).
/// Tasks check what the player is holding, e.g. the fire needs the Extinguisher.
/// </summary>
public class Carryable : MonoBehaviour, IInteractable, IPromptProvider
{
    public ItemType type;
    public string displayName = "Item";
    [Tooltip("Big things are shrunk to this size (meters) while held so they don't cover the screen.")]
    public float maxHeldSize = 0.45f;
    public Vector3 heldRotation = new Vector3(0f, -20f, 0f);

    Collider[] colliders;
    bool[] colliderWasEnabled;
    Rigidbody body;
    bool bodyWasKinematic;

    Transform homeParent;
    Vector3 homePosition;
    Quaternion homeRotation;
    Vector3 homeScale;
    bool initialized;

    public bool IsHeld { get; private set; }

    void Awake() => Init();

    void Init()
    {
        if (initialized) return;
        initialized = true;
        colliders = GetComponentsInChildren<Collider>(true);
        colliderWasEnabled = new bool[colliders.Length];
        for (int i = 0; i < colliders.Length; i++) colliderWasEnabled[i] = colliders[i].enabled;
        body = GetComponent<Rigidbody>();
        if (body) bodyWasKinematic = body.isKinematic;

        homeParent = transform.parent;
        homePosition = transform.position;
        homeRotation = transform.rotation;
        homeScale = transform.localScale;
    }

    public string Prompt => IsHeld ? null : "Pick up " + displayName;

    public void Interact()
    {
        if (PlayerCarry.Instance) PlayerCarry.Instance.PickUp(this);
    }

    /// <summary>Called by PlayerCarry.</summary>
    public void AttachTo(Transform holdPoint)
    {
        Init();
        float size = WorldSize();
        float shrink = size > maxHeldSize && size > 0f ? maxHeldSize / size : 1f;
        Vector3 worldScale = transform.lossyScale * shrink;

        IsHeld = true;
        SetPhysics(false);
        transform.SetParent(holdPoint, false);
        transform.localPosition = Vector3.zero;
        transform.localRotation = Quaternion.Euler(heldRotation);

        Vector3 parentScale = holdPoint.lossyScale;
        transform.localScale = new Vector3(
            worldScale.x / Mathf.Max(0.0001f, parentScale.x),
            worldScale.y / Mathf.Max(0.0001f, parentScale.y),
            worldScale.z / Mathf.Max(0.0001f, parentScale.z));
    }

    /// <summary>Put the item down standing on the floor at this point.</summary>
    public void PlaceAt(Vector3 floorPoint, float yaw)
    {
        Init();
        IsHeld = false;
        transform.SetParent(homeParent, true);
        transform.localScale = homeScale;
        transform.rotation = Quaternion.Euler(0f, yaw, 0f) * Quaternion.Euler(0f, -homeRotation.eulerAngles.y, 0f) * homeRotation;
        transform.position = floorPoint;

        // Lift it so its bottom sits on the floor instead of sinking into it.
        if (TryGetBounds(out Bounds b))
            transform.position += Vector3.up * (floorPoint.y - b.min.y);

        SetPhysics(true);
    }

    /// <summary>Put the item back where it started (and show/hide it).</summary>
    public void ReturnHome(bool visible)
    {
        Init();
        if (IsHeld && PlayerCarry.Instance && PlayerCarry.Instance.Held == this)
            PlayerCarry.Instance.TakeHeld();

        IsHeld = false;
        transform.SetParent(homeParent, true);
        transform.SetPositionAndRotation(homePosition, homeRotation);
        transform.localScale = homeScale;
        SetPhysics(true);
        gameObject.SetActive(visible);
    }

    void SetPhysics(bool on)
    {
        for (int i = 0; i < colliders.Length; i++)
            if (colliders[i]) colliders[i].enabled = on && colliderWasEnabled[i];
        if (body) body.isKinematic = !on || bodyWasKinematic;
    }

    float WorldSize()
    {
        if (!TryGetBounds(out Bounds b)) return 0f;
        return Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
    }

    bool TryGetBounds(out Bounds bounds)
    {
        bounds = default;
        bool found = false;
        foreach (var r in GetComponentsInChildren<Renderer>())
        {
            if (!found) { bounds = r.bounds; found = true; }
            else bounds.Encapsulate(r.bounds);
        }
        return found;
    }
}
