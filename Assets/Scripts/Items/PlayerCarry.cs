using UnityEngine;

/// <summary>Lets the player hold one Carryable at a time. E on an item picks it up, G drops it.</summary>
public class PlayerCarry : MonoBehaviour
{
    public static PlayerCarry Instance { get; private set; }

    [Tooltip("Where held items sit. Created under the camera if empty.")]
    public Transform holdPoint;
    public Vector3 holdOffset = new Vector3(0.38f, -0.32f, 0.75f);
    public KeyCode dropKey = KeyCode.G;
    public float dropDistance = 1.1f;

    public Carryable Held { get; private set; }

    void Awake()
    {
        Instance = this;
    }

    void Start()
    {
        if (!holdPoint && Camera.main)
        {
            holdPoint = new GameObject("HoldPoint").transform;
            holdPoint.SetParent(Camera.main.transform, false);
            holdPoint.localPosition = holdOffset;
        }
    }

    void Update()
    {
        bool busy = PlayerInteraction.Instance && PlayerInteraction.Instance.InMainTask;
        if (Held && !busy && Input.GetKeyDown(dropKey)) Drop();
    }

    public bool IsHolding(ItemType type) => Held && Held.type == type;

    public void PickUp(Carryable item)
    {
        if (!item || item == Held || !holdPoint || item.TakenBy) return;
        if (Held) Drop(); // swap: put the current one down first
        Held = item;
        item.AttachTo(holdPoint);
    }

    public void Drop()
    {
        if (!Held) return;
        var item = Held;
        Held = null;

        var cam = Camera.main ? Camera.main.transform : transform;
        item.PlaceAt(FindDropPoint(cam), cam.eulerAngles.y);
    }

    /// <summary>Removes the held item from the player's hands without placing it (the caller decides what happens to it).</summary>
    public Carryable TakeHeld()
    {
        var item = Held;
        Held = null;
        return item;
    }

    Vector3 FindDropPoint(Transform cam)
    {
        Vector3 forward = cam.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.001f) forward = transform.forward;
        forward.Normalize();

        // Don't drop through walls.
        float distance = dropDistance;
        if (Physics.Raycast(cam.position, forward, out RaycastHit wall, dropDistance + 0.3f, ~0, QueryTriggerInteraction.Ignore))
            distance = Mathf.Max(0.2f, wall.distance - 0.3f);

        Vector3 point = cam.position + forward * distance;
        if (Physics.Raycast(point, Vector3.down, out RaycastHit floor, 6f, ~0, QueryTriggerInteraction.Ignore))
            return floor.point;
        return point + Vector3.down * 1.5f;
    }
}
