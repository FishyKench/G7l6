using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The wardrobe in your room. Press E to open or close its doors (they swing on their hinges).
/// When the laundry is done, carry the clean clothes here and press E: they go in as folded stacks, and the
/// laundry is finished.
/// </summary>
public class Wardrobe : MonoBehaviour, IInteractable, IPromptProvider
{
    public float openAngle = 105f;
    public float speed = 5f;

    class Door
    {
        public Transform hinge;
        public float sign;
    }

    readonly List<Door> doors = new List<Door>();
    LaundryTask laundry;
    bool open;
    float amount, velocity;
    int stacks;

    public bool IsOpen => open;

    void Start()
    {
        laundry = FindObjectOfType<LaundryTask>();
        SetUpDoors();
    }

    /// <summary>Each door turns round its outer edge: put it under a hinge there.</summary>
    void SetUpDoors()
    {
        var all = GetComponentsInChildren<Transform>(true);
        var whole = Bounds(gameObject);
        Vector3 centre = transform.InverseTransformPoint(whole.center);
        foreach (var t in all)
        {
            if (t == transform || !t.name.Contains("Door") || t.parent && t.parent.name.StartsWith("Hinge")) continue;
            var b = Bounds(t.gameObject);
            Vector3 local = transform.InverseTransformPoint(b.center);
            float side = local.x < centre.x ? -1f : 1f;
            // The outer edge, at the front of the door.
            Vector3 extentsLocal = transform.InverseTransformVector(b.extents);
            float halfWidth = Mathf.Abs(extentsLocal.x);
            float halfDepth = Mathf.Abs(extentsLocal.z);
            var hinge = new GameObject("Hinge " + t.name).transform;
            hinge.SetParent(transform, false);
            hinge.localPosition = new Vector3(local.x + side * halfWidth, local.y, local.z + halfDepth);
            hinge.localRotation = Quaternion.identity;
            t.SetParent(hinge, true);
            // Swing outwards (towards the front, +Z): left door turns one way, right door the other.
            doors.Add(new Door { hinge = hinge, sign = side > 0f ? 1f : -1f });
        }
    }

    public string Prompt
    {
        get
        {
            bool holding = PlayerCarry.Instance && PlayerCarry.Instance.IsHolding(ItemType.CleanClothes);
            if (holding) return open ? "Put the clean clothes away" : "Open wardrobe";
            return open ? "Close wardrobe" : "Open wardrobe";
        }
    }

    public void Interact()
    {
        bool holding = PlayerCarry.Instance && PlayerCarry.Instance.IsHolding(ItemType.CleanClothes);
        if (holding && open)
        {
            // Fold them first (a little rhythm game), then they go in.
            FoldMinigame.Begin(quality =>
            {
                if (!PlayerCarry.Instance || !PlayerCarry.Instance.IsHolding(ItemType.CleanClothes)) return;
                var clothes = PlayerCarry.Instance.TakeHeld();
                if (clothes) clothes.ReturnHome(false);
                AddStack();
                if (laundry) laundry.ClothesPutAway(quality);
            }, () => GameHUD.Toast("Stopped folding - the clothes are still in your arms.", GameHUD.Info), TutorialDay.Active ? 1 : 4);
            return;
        }
        open = !open;
    }

    void Update()
    {
        if (PauseMenu.IsPaused || doors.Count == 0) return;
        amount = Mathf.SmoothDamp(amount, open ? 1f : 0f, ref velocity, 1f / speed);
        foreach (var d in doors) d.hinge.localRotation = Quaternion.Euler(0f, d.sign * openAngle * amount, 0f);
    }

    // ------------------------------------------------------------------ the clothes inside

    const int HangSlots = 6, StackSlots = 3, PerStack = 4;
    bool measured;
    Vector3 centre, right, up, forward, railPoint;
    float halfW, halfH, halfD, garmentScale, railHalf;
    bool hasRail;
    int hung, piecesStacked;
    readonly float[] stackTop = new float[StackSlots];
    readonly bool[] stackFound = new bool[StackSlots];
    Material hangerMat;

    /// <summary>One wash put away: two things on hangers on the rail and a few folded on a pile (until the wardrobe is full).</summary>
    void AddStack()
    {
        Measure();
        for (int i = 0; i < 2 && hung < HangSlots; i++) Hang();
        for (int i = 0; i < 3 && piecesStacked < StackSlots * PerStack; i++) Fold();
        stacks++;
    }

    float Along(Vector3 axis, Vector3 extents) => Mathf.Abs(axis.x) * extents.x + Mathf.Abs(axis.y) * extents.y + Mathf.Abs(axis.z) * extents.z;

    /// <summary>Works out the inside of the wardrobe (without the doors), the hanging rail and the shelf to pile on.</summary>
    void Measure()
    {
        if (measured) return;
        measured = true;
        right = transform.right; up = transform.up; forward = transform.forward;
        Bounds body = default;
        bool any = false;
        foreach (var r in GetComponentsInChildren<MeshRenderer>())
        {
            if (InDoor(r.transform)) continue;
            if (!any) { body = r.bounds; any = true; } else body.Encapsulate(r.bounds);
        }
        if (!any) body = Bounds(gameObject);
        centre = body.center;
        halfW = Along(right, body.extents) * 0.88f;
        halfH = Along(up, body.extents);
        halfD = Along(forward, body.extents) * 0.8f;

        // A rail already in the model? (a thin horizontal bar in the top half; with one per compartment, the widest one)
        float best = 0f;
        foreach (var r in GetComponentsInChildren<MeshRenderer>())
        {
            if (InDoor(r.transform) || r.GetComponentInParent<Wardrobe>() != this) continue;
            var e = r.bounds.extents;
            float w = Along(right, e), h = Along(up, e), d = Along(forward, e);
            float y = Vector3.Dot(r.bounds.center - centre, up);
            if (w > halfW * 0.25f && h < halfH * 0.05f && d < halfD * 0.35f && y > halfH * 0.3f && w > best)
            {
                best = w;
                hasRail = true;
                railPoint = r.bounds.center;
                railHalf = w;
            }
        }
        if (!hasRail) { railPoint = centre + up * (halfH * 0.78f); railHalf = halfW; }
        // The clothes must stay inside the compartment: never wider than the rail, never past the wardrobe's own sides.
        float sideRoom = halfW - Mathf.Abs(Vector3.Dot(railPoint - centre, right));
        railHalf = Mathf.Min(railHalf, sideRoom);
        hangerMat = new Material(Shader.Find("Standard")) { color = new Color(0.45f, 0.3f, 0.2f) };
        hangerMat.SetFloat("_Glossiness", 0.3f);

        // Piles stand on whatever is under the clothes: a shelf or the wardrobe floor. The model may have no colliders
        // inside, so give it temporary ones to find the shelves, then take them away again.
        var temp = new List<MeshCollider>();
        foreach (var mf in GetComponentsInChildren<MeshFilter>())
        {
            if (InDoor(mf.transform) || !mf.sharedMesh || mf.GetComponent<Collider>()) continue;
            var mc = mf.gameObject.AddComponent<MeshCollider>();
            mc.sharedMesh = mf.sharedMesh;
            temp.Add(mc);
        }
        Physics.SyncTransforms();

        // Clothes hang from the rail down towards whatever is under it (a shelf or the floor): as big as fits.
        float railY = Vector3.Dot(railPoint - centre, up), below = -halfH;
        foreach (var hit in Physics.RaycastAll(railPoint - up * 0.08f, -up, halfH * 2f, ~0, QueryTriggerInteraction.Ignore))
            if (hit.transform.IsChildOf(transform) && !InDoor(hit.transform)) below = Mathf.Max(below, Vector3.Dot(hit.point - centre, up));
        float drop = railY - below;
        // A t-shirt is 0.74 wide (sleeves out) at scale 1: it has to fit between the compartment's sides with a little room.
        garmentScale = Mathf.Clamp(Mathf.Min((drop * 0.82f - 0.07f) / 0.64f, railHalf * 2f * 0.8f / 0.74f), 0.3f, 1.6f);
        float lowestHem = railY - (0.07f + 0.64f * garmentScale);
        for (int i = 0; i < StackSlots; i++)
        {
            Vector3 from = centre + right * SlotX(i, StackSlots) + up * Mathf.Min(lowestHem - 0.02f, halfH * 0.2f);
            stackTop[i] = -halfH + 0.04f;
            foreach (var hit in Physics.RaycastAll(from, -up, halfH * 2f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (!hit.transform.IsChildOf(transform) || InDoor(hit.transform)) continue;
                float y = Vector3.Dot(hit.point - centre, up);
                if (!stackFound[i] || y > stackTop[i]) { stackTop[i] = y; stackFound[i] = true; }
            }
        }
        foreach (var mc in temp) DestroyImmediate(mc);
    }

    float SlotX(int i, int count) => count <= 1 ? 0f : Mathf.Lerp(-halfW * 0.62f, halfW * 0.62f, i / (float)(count - 1));

    static bool InDoor(Transform t)
    {
        for (var p = t; p; p = p.parent) if (p.name.StartsWith("Hinge")) return true;
        return false;
    }

    void Hang()
    {
        if (!hasRail && hung == 0)
        {
            var rod = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Destroy(rod.GetComponent<Collider>());
            rod.name = "Hanging rail";
            rod.transform.SetParent(transform, true);
            rod.transform.position = railPoint;
            rod.transform.rotation = Quaternion.LookRotation(forward, right); // cylinder's long axis (y) along the width
            rod.transform.localScale = Vector3.one; // reset, then size in world units
            SetWorldScale(rod.transform, new Vector3(0.025f, halfW * 1.08f, 0.025f));
            var m = new Material(Shader.Find("Standard")) { color = new Color(0.75f, 0.76f, 0.8f) };
            m.SetFloat("_Metallic", 0.7f); m.SetFloat("_Glossiness", 0.7f);
            rod.GetComponent<Renderer>().sharedMaterial = m;
        }

        string[] kinds = { "shirt", "t-shirt" };
        string kind = kinds[(hung + Random.Range(0, 2)) % kinds.Length];
        // Facing you, side by side along the rail, each one a little further back (they overlap like a real rail of clothes).
        float spread = Mathf.Max(0f, railHalf * 0.92f - 0.37f * garmentScale); // the sleeves of the outer ones still end inside
        Vector3 hook = railPoint + right * Mathf.Lerp(-spread, spread, HangSlots <= 1 ? 0.5f : hung / (float)(HangSlots - 1))
                       + forward * (hung - (HangSlots - 1) * 0.5f) * 0.022f; // every one at its own depth: no flicker where they overlap
        Vector3 across = Quaternion.AngleAxis(Random.Range(-5f, 5f), up) * right;

        var hanger = new GameObject("Hanger").transform;
        hanger.SetParent(transform, true);
        hanger.position = hook;
        float shoulders = 0.6f * garmentScale;
        Part(hanger, PrimitiveType.Cube, hook - up * 0.06f, Quaternion.LookRotation(up, Vector3.Cross(up, across)), new Vector3(shoulders, 0.012f, 0.012f), hangerMat, across);
        Part(hanger, PrimitiveType.Cylinder, hook - up * 0.025f, Quaternion.LookRotation(forward, up), new Vector3(0.008f, 0.03f, 0.008f), hangerMat, Vector3.zero);

        var g = new GameObject(kind + " (hanging)");
        g.transform.SetParent(hanger, true);
        const float top = 0.3f; // the shoulder line in garment coordinates
        g.transform.position = hook - up * (0.07f + top * garmentScale);
        Vector3 normal = Vector3.Cross(up, across);
        g.transform.rotation = Quaternion.LookRotation(up, normal);
        SetWorldScale(g.transform, Vector3.one * garmentScale);
        g.AddComponent<MeshFilter>().sharedMesh = DirtyClothesPiece.FlatGarment(kind);
        g.AddComponent<MeshRenderer>().sharedMaterials = DirtyClothesPiece.Materials(kind, DirtyClothesPiece.RandomColour(kind));
        hung++;
    }

    /// <summary>A bar from two points' worth of size: 'size' is in world units along (across, thickness, thickness).</summary>
    void Part(Transform parent, PrimitiveType type, Vector3 at, Quaternion rot, Vector3 size, Material m, Vector3 across)
    {
        var p = GameObject.CreatePrimitive(type);
        Destroy(p.GetComponent<Collider>());
        p.transform.SetParent(parent, true);
        p.transform.position = at;
        if (across != Vector3.zero) p.transform.rotation = Quaternion.LookRotation(Vector3.Cross(across, up), up);
        else p.transform.rotation = rot;
        SetWorldScale(p.transform, size);
        p.GetComponent<Renderer>().sharedMaterial = m;
    }

    void Fold()
    {
        int slot = piecesStacked / PerStack, level = piecesStacked % PerStack;
        float x = SlotX(slot, StackSlots);
        float slotW = halfW * 2f / StackSlots;
        const float thick = 0.045f;
        float w = Mathf.Min(slotW * 0.8f, 0.34f), d = Mathf.Min(halfD * 2f * 0.85f, 0.3f);
        Vector3 at = centre + right * (x + Random.Range(-0.01f, 0.01f)) + up * (stackTop[slot] + 0.003f + thick * (level + 0.5f));

        string kind = Random.value < 0.35f ? "trousers" : "t-shirt";
        var colour = DirtyClothesPiece.RandomColour(kind);
        var piece = new GameObject("Folded " + kind).transform;
        piece.SetParent(transform, true);
        piece.position = at;
        piece.rotation = Quaternion.LookRotation(forward, up) * Quaternion.Euler(0f, Random.Range(-4f, 4f), 0f);
        var mats = DirtyClothesPiece.Materials(kind, colour);
        // The folded garment: a soft block, with the folded edge (a rounded roll) at the front.
        var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Destroy(body.GetComponent<Collider>());
        body.transform.SetParent(piece, false);
        SetWorldScale(body.transform, new Vector3(w, thick * 0.86f, d));
        body.GetComponent<Renderer>().sharedMaterial = mats[0];
        var roll = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Destroy(roll.GetComponent<Collider>());
        roll.transform.SetParent(piece, false);
        roll.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
        roll.transform.position = at + forward * (d * 0.5f - 0.002f);
        SetWorldScale(roll.transform, new Vector3(thick * 0.9f, w * 0.5f * 0.98f, thick * 0.9f));
        roll.GetComponent<Renderer>().sharedMaterial = mats[0];
        // A stripe of the detail colour (collar / waistband) peeking out at the back.
        var band = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Destroy(band.GetComponent<Collider>());
        band.transform.SetParent(piece, false);
        band.transform.position = at - forward * (d * 0.5f - 0.012f) + up * 0.0015f;
        SetWorldScale(band.transform, new Vector3(w * 0.96f, thick * 0.9f, 0.022f));
        band.GetComponent<Renderer>().sharedMaterial = mats[1];
        piecesStacked++;
    }

    /// <summary>Sets a transform's scale so it ends up 'world' big, whatever its parents are scaled by.</summary>
    static void SetWorldScale(Transform t, Vector3 world)
    {
        var parent = t.parent ? t.parent.lossyScale : Vector3.one;
        t.localScale = new Vector3(world.x / Mathf.Max(1e-4f, Mathf.Abs(parent.x)), world.y / Mathf.Max(1e-4f, Mathf.Abs(parent.y)), world.z / Mathf.Max(1e-4f, Mathf.Abs(parent.z)));
    }

    static Bounds Bounds(GameObject go)
    {
        var rs = go.GetComponentsInChildren<MeshRenderer>();
        if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.one);
        var b = rs[0].bounds;
        foreach (var r in rs) b.Encapsulate(r.bounds);
        return b;
    }
}
