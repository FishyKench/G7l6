using UnityEngine;

/// <summary>
/// A door in the house (made by G7l6 > Add Doors). E opens or closes it - it swings smoothly on its hinges, with
/// a creak and a thud. It won't close on you (or the cat). The cat paws closed doors open when it wants through,
/// and the cat's walk map ignores doors.
/// </summary>
public class HouseDoor : MonoBehaviour, IInteractable, IPromptProvider
{
    [Tooltip("The part that swings (its pivot is the hinge line).")]
    public Transform leaf;
    [Tooltip("Degrees it swings when open (the sign says which way).")]
    public float openAngle = -95f;
    public bool startOpen;
    [Tooltip("What it's called on the prompt: 'front door', 'bathroom door'...")]
    public string label = "door";
    [Tooltip("Optional: a little light that shows red while closed (bathroom: occupied).")]
    public Renderer occupiedLight;
    public Material occupiedOn, occupiedOff;

    public bool IsOpen { get; private set; }

    float angle, velocity;
    static CatBrain cat;
    float catCheck;

    void Start()
    {
        IsOpen = startOpen;
        angle = IsOpen ? openAngle : 0f;
        Apply();
    }

    public string Prompt => IsOpen ? "Close the " + label : "Open the " + label;

    public void Interact()
    {
        if (IsOpen && SomethingInTheWay()) { GameHUD.Toast("Something's in the way", GameHUD.Warn); return; }
        Toggle(!IsOpen);
    }

    void Toggle(bool open)
    {
        if (open == IsOpen) return;
        IsOpen = open;
        swingTime = 0.28f;
        if (leaf) Fx.Play(Fx.Swish, leaf.position + leaf.right * 0.5f + Vector3.up, 0.35f, open ? 0.55f : 0.65f);
        closingThud = !open;
    }

    bool closingThud;
    float swingTime = 0.28f;

    // ---- for Mom, who opens closed doors on her way through (she has no collider: doors don't stop her walking map)

    /// <summary>Opens it (Mom's hand on the handle): 'smoothTime' says how fast it swings (a pulled door comes slower, as she steps back).</summary>
    public void OpenBy(float smoothTime)
    {
        Toggle(true);
        swingTime = smoothTime;
    }

    /// <summary>Closes it, unless someone's standing in the doorway.</summary>
    public void Close()
    {
        if (IsOpen && !SomethingInTheWay()) Toggle(false);
    }

    /// <summary>0 shut .. 1 wide open.</summary>
    public float OpenAmount => Mathf.Clamp01(Mathf.Abs(angle) / Mathf.Max(1f, Mathf.Abs(openAngle)));
    /// <summary>The handle (on the latch side, at hand height).</summary>
    public Vector3 Handle => transform.TransformPoint(new Vector3(side * Width * 0.85f, 1f, 0f));
    /// <summary>How long the leaf is, in metres.</summary>
    public float LeafLength => Width * Mathf.Abs(transform.lossyScale.x);
    /// <summary>Signed distance from the door's plane (+ = the side it swings into).</summary>
    public float PlaneDistance(Vector3 p) => Vector3.Dot(p - transform.position, transform.forward);
    /// <summary>Does it swing towards someone standing at 'p' (so they pull it, and step back out of its way)?</summary>
    public bool OpensTowards(Vector3 p) => PlaneDistance(p) > 0f;

    /// <summary>Does the walk from a to b go through this doorway?</summary>
    public bool Crosses(Vector3 a, Vector3 b, float margin)
    {
        Vector3 la = transform.InverseTransformPoint(a), lb = transform.InverseTransformPoint(b);
        if (la.z * lb.z > 0f || Mathf.Approximately(la.z, lb.z)) return false;
        float t = la.z / (la.z - lb.z);
        Vector3 p = Vector3.LerpUnclamped(la, lb, t);
        float x = p.x * side;
        return x > -margin && x < Width + margin && p.y > -0.8f && p.y < Height;
    }

    void Update()
    {
        if (!leaf || PauseMenu.IsPaused) return;

        // The cat paws doors open.
        catCheck -= Time.deltaTime;
        if (!IsOpen && catCheck <= 0f)
        {
            catCheck = 0.3f;
            if (!cat) cat = FindObjectOfType<CatBrain>();
            float scale = HouseLayout.Instance ? HouseLayout.Instance.scale : 1f;
            if (cat && Vector3.Distance(Flat(cat.transform.position), Flat(Centre)) < 1.4f * scale
                && Mathf.Abs(cat.transform.position.y - transform.position.y) < 1.5f * scale)
                Toggle(true);
        }

        float target = IsOpen ? openAngle : 0f;
        angle = Mathf.SmoothDampAngle(angle, target, ref velocity, swingTime);
        if (closingThud && Mathf.Abs(angle) < 2f)
        {
            closingThud = false;
            Fx.Play(Fx.Thud, Centre, 0.6f, Random.Range(0.9f, 1.1f));
        }
        Apply();
    }

    void Apply()
    {
        if (leaf) leaf.localRotation = Quaternion.Euler(0f, angle, 0f);
        if (occupiedLight) occupiedLight.sharedMaterial = IsOpen || Mathf.Abs(angle) > 5f ? occupiedOff : occupiedOn;
    }

    /// <summary>The middle of the doorway.</summary>
    Vector3 Centre => transform.TransformPoint(new Vector3(side * Width * 0.5f, 1f, 0f));

    [Tooltip("Size of the doorway (in the door's own units): set by the door builder.")]
    public float Width = 0.9f, Height = 2.05f;
    [Tooltip("+1: the leaf runs along +x from the hinge; -1: along -x (hinged on the other side).")]
    public float side = 1f;

    /// <summary>Is the player or the cat standing where the door would close?</summary>
    bool SomethingInTheWay()
    {
        Vector3 c = transform.TransformPoint(new Vector3(side * Width * 0.5f, Height * 0.5f, 0f));
        Vector3 half = Vector3.Scale(new Vector3(Width * 0.5f, Height * 0.45f, 0.12f), transform.lossyScale);
        foreach (var col in Physics.OverlapBox(c, half, transform.rotation, ~0, QueryTriggerInteraction.Ignore))
            if (col.GetComponentInParent<PlayerMovementAdvanced>() || col.GetComponentInParent<CatBrain>()) return true;
        return false;
    }

    static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }
}
