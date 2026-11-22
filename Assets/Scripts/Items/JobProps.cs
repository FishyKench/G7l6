using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// The little props Mom's side jobs (MyMail) need, built from primitives at runtime:
///   - the kitchen trash bag (a Carryable) and the outdoor trash bin by the front door,
///   - thirsty house plants you water with E.
/// Plus the free-floor search that puts them somewhere sensible (never on furniture, never in a doorway).
/// </summary>
public static class JobProps
{
    // ------------------------------------------------------------------ finding a free spot

    /// <summary>
    /// Free floor spots in a ground-floor or upstairs room, best first (next to a wall, away from doors and
    /// doorways, on the walk map). 'footprint' is the (x, y, z) box that must be empty, in metres.
    /// </summary>
    public static List<Vector3> RoomSpots(string roomName, Vector3 footprint, float margin = 0.1f, float doorClear = 1.3f)
    {
        var result = new List<Vector3>();
        var layout = HouseLayout.Instance;
        if (!layout) return result;
        HouseLayout.RoomInfo room = null;
        foreach (var r in layout.rooms) if (r.floor == 0 && r.name == roomName) room = r;
        if (room == null) return result;

        float scale = layout.scale;
        float floorY = room.floor * layout.storeyHeight * scale;
        var doors = Object.FindObjectsOfType<HouseDoor>();
        var scored = new List<(float score, Vector3 pos)>();
        Rect rect = room.rect;
        float inset = 0.55f;
        for (float x = rect.xMin + inset; x <= rect.xMax - inset; x += 0.4f)
            for (float y = rect.yMin + inset; y <= rect.yMax - inset; y += 0.4f)
            {
                Vector3 top = layout.transform.TransformPoint(new Vector3(x, 0f, y));
                top.y = floorY + 1.6f * scale;
                if (!ProcBuild.FloorAt(top, floorY, 0.12f, out Vector3 floor)) continue;
                if (ProcBuild.Blocked(floor, footprint, Quaternion.identity, margin)) continue;
                if (NearDoor(layout, doors, floor, room.floor, doorClear)) continue;
                if (!NavMesh.SamplePosition(floor, out var nav, 0.5f, NavMesh.AllAreas) || Mathf.Abs(nav.position.y - floor.y) > 0.2f) continue;
                float edge = Mathf.Min(Mathf.Min(x - rect.xMin, rect.xMax - x), Mathf.Min(y - rect.yMin, rect.yMax - y));
                scored.Add((edge + Random.Range(0f, 0.9f), floor));
            }
        scored.Sort((a, b) => a.score.CompareTo(b.score));
        foreach (var s in scored) result.Add(s.pos);
        return result;
    }

    static bool NearDoor(HouseLayout layout, HouseDoor[] doors, Vector3 p, int floor, float clear)
    {
        foreach (var d in doors)
        {
            Vector3 off = d.transform.position - p;
            off.y = 0f;
            if (off.magnitude < clear) return true;
        }
        // Open doorways (wall segments of type Door) count too.
        foreach (var w in layout.walls)
        {
            if (w.floor != floor || w.type != HouseLayout.WallType.Door) continue;
            Vector3 a = layout.transform.TransformPoint(new Vector3(w.a.x, 0f, w.a.y)), b = layout.transform.TransformPoint(new Vector3(w.b.x, 0f, w.b.y));
            if (FlatDistToSegment(p, a, b) < clear) return true;
        }
        return false;
    }

    static float FlatDistToSegment(Vector3 p, Vector3 a, Vector3 b)
    {
        Vector2 P = new Vector2(p.x, p.z), A = new Vector2(a.x, a.z), B = new Vector2(b.x, b.z);
        Vector2 ab = B - A;
        float t = ab.sqrMagnitude < 0.0001f ? 0f : Mathf.Clamp01(Vector2.Dot(P - A, ab) / ab.sqrMagnitude);
        return (P - (A + ab * t)).magnitude;
    }

    /// <summary>A free floor spot beside the front door: outside first (the porch), otherwise just inside. Null if none.</summary>
    public static bool FindBinSpot(Vector3 size, out Vector3 floor, out bool outside)
    {
        floor = default;
        outside = true;
        var door = ShopRuntime.FrontDoor();
        if (!door) return false;

        float scale = Mathf.Max(0.01f, door.transform.lossyScale.x);
        float doorWidth = door.Width * scale;
        Vector3 centre = door.transform.TransformPoint(new Vector3(door.side * door.Width * 0.5f, 0f, 0f));
        Vector3 axis = door.transform.forward;
        axis.y = 0f;
        axis.Normalize();
        Vector3 side = new Vector3(axis.z, 0f, -axis.x);
        float inside = 1f;
        var layout = HouseLayout.Instance;
        if (layout)
        {
            bool a = layout.RoomAt(centre + axis * 1.2f, 0) != null, b = layout.RoomAt(centre - axis * 1.2f, 0) != null;
            if (b && !a) inside = -1f;
        }
        float floorY = door.transform.position.y;
        float clearLateral = doorWidth * 0.5f + size.x * 0.5f + 0.25f;

        for (int pass = 0; pass < 2; pass++)
        {
            Vector3 dir = axis * (pass == 0 ? -inside : inside); // pass 0 = outside
            var candidates = new List<(float cost, Vector3 pos)>();
            for (float a = 0.9f; a <= 2.6f; a += 0.4f)
                for (float l = -2.4f; l <= 2.4f; l += 0.4f)
                {
                    if (Mathf.Abs(l) < clearLateral) continue;
                    Vector3 p = centre + dir * a + side * l;
                    Vector3 off = p - door.transform.position;
                    off.y = 0f;
                    if (off.magnitude < doorWidth + 0.35f) continue; // the door swings here
                    candidates.Add((a + 0.5f * Mathf.Abs(l), p));
                }
            candidates.Sort((x, y) => x.cost.CompareTo(y.cost));
            foreach (var c in candidates)
            {
                if (!ProcBuild.FloorAt(c.pos + Vector3.up * (floorY + 1.8f - c.pos.y), floorY, 0.12f, out Vector3 point)) continue;
                if (ProcBuild.Blocked(point, size, Quaternion.identity, 0.12f)) continue;
                floor = point;
                outside = pass == 0;
                return true;
            }
        }
        return false;
    }

    // ------------------------------------------------------------------ the trash bag

    /// <summary>The kitchen trash bag, standing on the floor at 'floor' (a Carryable: E to pick up, G to drop).</summary>
    public static Carryable BuildTrashBag(Vector3 floor)
    {
        var go = new GameObject("Trash bag");
        go.transform.position = floor;
        go.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
        var t = go.transform;
        var plastic = ProcBuild.Mat(new Color(0.07f, 0.075f, 0.09f), 0.65f);
        var tie = ProcBuild.Mat(new Color(0.9f, 0.3f, 0.2f), 0.2f);

        var body = ProcBuild.Ball(t, "Bag", new Vector3(0f, 0.19f, 0f), 1f, plastic);
        body.transform.localScale = new Vector3(0.36f, 0.38f, 0.34f);
        ProcBuild.Cyl(t, "Neck", new Vector3(0f, 0.395f, 0f), 0.07f, 0.1f, plastic);
        ProcBuild.Cube(t, "Ear A", new Vector3(-0.03f, 0.47f, 0f), new Vector3(0.045f, 0.07f, 0.02f), plastic, new Vector3(0f, 0f, 24f));
        ProcBuild.Cube(t, "Ear B", new Vector3(0.03f, 0.465f, 0f), new Vector3(0.045f, 0.065f, 0.02f), plastic, new Vector3(0f, 0f, -28f));
        ProcBuild.Cyl(t, "Tie", new Vector3(0f, 0.405f, 0f), 0.082f, 0.018f, tie);

        var box = go.AddComponent<BoxCollider>();
        box.center = new Vector3(0f, 0.22f, 0f);
        box.size = new Vector3(0.34f, 0.44f, 0.32f);
        ProcBuild.AddOutline(go);
        var c = go.AddComponent<Carryable>();
        c.type = ItemType.TrashBag;
        c.displayName = "trash bag";
        c.maxHeldSize = 0.45f;
        return c;
    }
}

/// <summary>The trash bin by the front door: put the bag in with E while you carry it.</summary>
public class TrashBin : MonoBehaviour, IInteractable, IPromptProvider
{
    public static readonly Vector3 Size = new Vector3(0.5f, 0.72f, 0.5f);
    public static TrashBin Instance { get; private set; }

    /// <summary>Set by MailRuntime while a trash job is running.</summary>
    public bool jobActive;
    GameObject lid;
    float lidOpenUntil;

    public static TrashBin Create(Vector3 floor, Vector3 lookAt)
    {
        var go = new GameObject("Trash bin");
        go.transform.position = floor;
        Vector3 face = lookAt - floor;
        face.y = 0f;
        if (face.sqrMagnitude > 0.01f) go.transform.rotation = Quaternion.LookRotation(face);
        var t = go.transform;
        var body = ProcBuild.Mat(new Color(0.22f, 0.4f, 0.32f), 0.25f);
        var band = ProcBuild.Mat(new Color(0.16f, 0.3f, 0.24f), 0.25f);
        var lidMat = ProcBuild.Mat(new Color(0.18f, 0.33f, 0.27f), 0.3f);
        var dark = ProcBuild.Mat(new Color(0.1f, 0.12f, 0.12f), 0.2f);

        ProcBuild.Cyl(t, "Can", new Vector3(0f, 0.31f, 0f), 0.42f, 0.62f, body);
        ProcBuild.Cyl(t, "Band low", new Vector3(0f, 0.12f, 0f), 0.428f, 0.035f, band);
        ProcBuild.Cyl(t, "Band high", new Vector3(0f, 0.5f, 0f), 0.428f, 0.035f, band);
        // The lid hinges at the back edge so it can tip up.
        var lid = new GameObject("Lid hinge");
        lid.transform.SetParent(t, false);
        lid.transform.localPosition = new Vector3(0f, 0.623f, -0.22f);
        ProcBuild.Cyl(lid.transform, "Lid", new Vector3(0f, 0.025f, 0.22f), 0.46f, 0.05f, lidMat);
        ProcBuild.Cube(lid.transform, "Handle", new Vector3(0f, 0.062f, 0.22f), new Vector3(0.14f, 0.025f, 0.04f), dark);

        var col = go.AddComponent<CapsuleCollider>();
        col.center = new Vector3(0f, 0.36f, 0f);
        col.radius = 0.23f;
        col.height = 0.72f;
        ProcBuild.Carve(go, new Vector3(0f, 0.36f, 0f), new Vector3(0.5f, 0.72f, 0.5f));
        var bin = go.AddComponent<TrashBin>();
        bin.lid = lid;
        ProcBuild.AddOutline(go);
        Instance = bin;
        return bin;
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    void Update()
    {
        // The lid tips up for a moment when the bag goes in.
        if (!lid) return;
        bool open = Time.time < lidOpenUntil;
        var target = open ? Quaternion.Euler(-70f, 0f, 0f) : Quaternion.identity;
        lid.transform.localRotation = Quaternion.Slerp(lid.transform.localRotation, target, Time.deltaTime * 12f);
    }

    public string Prompt
    {
        get
        {
            if (!jobActive) return null;
            return PlayerCarry.Instance && PlayerCarry.Instance.IsHolding(ItemType.TrashBag) ? "Throw the trash bag in the bin" : "Trash bin - bring the kitchen trash bag";
        }
    }

    public void Interact()
    {
        if (!jobActive) return;
        var carry = PlayerCarry.Instance;
        if (!carry || !carry.IsHolding(ItemType.TrashBag))
        {
            GameHUD.Toast("Bring the trash bag from the kitchen first!", GameHUD.Warn);
            return;
        }
        var bag = carry.TakeHeld();
        if (bag) Destroy(bag.gameObject);
        lidOpenUntil = Time.time + 0.7f;
        Fx.Play(Fx.Thud, transform.position + Vector3.up * 0.6f, 0.6f, 0.8f);
        Fx.Puff(transform.position + Vector3.up * 0.7f, new Color(0.7f, 0.7f, 0.7f, 0.5f), 6, 0.4f);
        if (MailRuntime.Instance) MailRuntime.Instance.TrashThrown();
    }
}

/// <summary>A house plant. While it's thirsty (a job is running) E waters it and it perks up.</summary>
public class HousePlant : MonoBehaviour, IInteractable, IPromptProvider
{
    public static readonly Vector3 Size = new Vector3(0.46f, 0.7f, 0.46f);

    public bool jobActive;
    public bool Watered { get; private set; }

    readonly List<Transform> pivots = new List<Transform>();
    readonly List<float> yaws = new List<float>();
    Material leafMat;
    static readonly Color Thirsty = new Color(0.55f, 0.56f, 0.2f), Happy = new Color(0.14f, 0.58f, 0.22f);

    public static HousePlant Create(Vector3 floor)
    {
        var go = new GameObject("House plant");
        go.transform.position = floor;
        go.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
        var t = go.transform;
        var clay = ProcBuild.Mat(new Color(0.76f, 0.4f, 0.24f), 0.1f);
        var rim = ProcBuild.Mat(new Color(0.82f, 0.46f, 0.28f), 0.1f);
        var soil = ProcBuild.Mat(new Color(0.18f, 0.12f, 0.08f), 0.05f);
        var stem = ProcBuild.Mat(new Color(0.2f, 0.45f, 0.18f), 0.1f);
        var leaf = ProcBuild.Mat(Thirsty, 0.2f);

        ProcBuild.Cyl(t, "Pot", new Vector3(0f, 0.11f, 0f), 0.27f, 0.22f, clay);
        ProcBuild.Cyl(t, "Rim", new Vector3(0f, 0.23f, 0f), 0.3f, 0.04f, rim);
        ProcBuild.Cyl(t, "Soil", new Vector3(0f, 0.248f, 0f), 0.24f, 0.014f, soil);
        ProcBuild.Cyl(t, "Stem", new Vector3(0f, 0.3f, 0f), 0.03f, 0.12f, stem);

        var plant = go.AddComponent<HousePlant>();
        plant.leafMat = leaf;
        for (int i = 0; i < 8; i++)
        {
            var pivot = new GameObject("Leaf pivot " + i).transform;
            pivot.SetParent(t, false);
            pivot.localPosition = new Vector3(0f, 0.36f + (i % 2) * 0.03f, 0f);
            float yaw = i * 45f + (i % 2) * 12f;
            pivot.localRotation = Quaternion.Euler(30f, yaw, 0f);
            plant.yaws.Add(yaw);
            float len = 0.26f + (i % 3) * 0.03f;
            var blade = ProcBuild.Ball(pivot, "Leaf", new Vector3(0f, 0f, len * 0.5f), 1f, leaf);
            blade.transform.localScale = new Vector3(0.1f, 0.016f, len);
            plant.pivots.Add(pivot);
        }
        plant.Apply(0f);

        var col = go.AddComponent<CapsuleCollider>();
        col.center = new Vector3(0f, 0.33f, 0f);
        col.radius = 0.23f;
        col.height = 0.66f;
        ProcBuild.Carve(go, new Vector3(0f, 0.33f, 0f), new Vector3(0.4f, 0.66f, 0.4f));
        ProcBuild.AddOutline(go);
        return plant;
    }

    void Apply(float k)
    {
        for (int i = 0; i < pivots.Count; i++)
        {
            // Thirsty leaves hang 30 degrees below level (they clear the pot's rim); watered ones rise up.
            float pitch = Mathf.Lerp(30f, -50f - (i % 3) * 6f, k);
            pivots[i].localRotation = Quaternion.Euler(pitch, yaws[i], 0f);
        }
        leafMat.color = Color.Lerp(Thirsty, Happy, k);
    }

    public string Prompt
    {
        get
        {
            if (!jobActive || Watered) return null;
            return "Water the plant";
        }
    }

    public void Interact()
    {
        if (!jobActive || Watered) return;
        Watered = true;
        Fx.Splash(transform.position + Vector3.up * 0.55f, 14, 0.5f);
        Fx.Play(Fx.Spray, transform.position + Vector3.up * 0.5f, 0.35f, 1.5f);
        StartCoroutine(Perk());
        if (MailRuntime.Instance) MailRuntime.Instance.PlantWatered(this);
    }

    IEnumerator Perk()
    {
        for (float t = 0f; t < 1.2f; t += Time.deltaTime)
        {
            float k = t / 1.2f;
            Apply(k * k * (3f - 2f * k));
            yield return null;
        }
        Apply(1f);
    }
}
