using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;

/// <summary>
/// Opening the fridge, the freezer or the pantry: the real doors swing open and the view goes to what's inside - a real-looking
/// interior (glass shelves and salad crispers / frosted freezer drawers and ice / wooden pantry shelves) with everything on show. Every item has a name tag on the shelf's front lip under it; tonight's ingredients have green tags. Point at something and click to take it (it goes in your hands);
/// Esc, E or right-click closes it again. You can take anything, not only what the recipe needs.
/// </summary>
public class StorageView : MonoBehaviour, IMainTask
{
    public class Entry
    {
        public string id, label;
        public bool needed;
        public System.Func<GameObject> model;
    }

    public static bool Running { get; private set; }
    static StorageView instance;

    string title;
    Transform station;
    List<Entry> entries;
    System.Action<Entry> onTake;
    KitchenStation.Kind kind;

    Transform root;
    Camera cam;
    Canvas ui;
    readonly List<(Entry e, Transform t, Renderer tag, Vector3 basePos)> items = new List<(Entry, Transform, Renderer, Vector3)>();
    readonly List<(Transform hinge, float sign)> hinges = new List<(Transform, float)>();
    float doorOpen, doorTarget;
    bool running;
    int hover = -1, openedFrame = -1;
    Transform hingeStation;

    static readonly Vector3 Stage = new Vector3(0f, -300f, 0f); // built far below the house, filmed by its own camera

    public static void Open(KitchenStation.Kind kind, Transform station, string title, List<Entry> entries, System.Action<Entry> onTake)
    {
        if (!PlayerInteraction.Instance) return;
        if (!instance) instance = new GameObject("Storage view").AddComponent<StorageView>();
        // Still closing another store's doors? Shut them now, so this store's own doors are the ones that open.
        if (instance.hingeStation != station && instance.hinges.Count > 0)
        {
            foreach (var (h, _) in instance.hinges) if (h) h.localRotation = Quaternion.identity;
            instance.hinges.Clear();
            instance.doorOpen = 0f;
        }
        instance.kind = kind;
        instance.station = station;
        instance.title = title;
        instance.entries = entries;
        instance.onTake = onTake;
        PlayerInteraction.Instance.BeginMainTask(instance);
    }

    public bool ShouldBlockInput() => true;

    public void StartMainTask()
    {
        running = Running = true;
        openedFrame = Time.frameCount; // the E that opened it must not also close it (this Update can run later in the same frame)
        HudHider.Hide();
        Build();
        doorTarget = 1f;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    public void StopMainTask() => Close(false);

    void Close(bool released)
    {
        if (!running) return;
        running = Running = false;
        if (root) Destroy(root.gameObject);
        if (ui) Destroy(ui.gameObject);
        items.Clear();
        pads.Clear();
        if (ambientSaved)
        {
            RenderSettings.ambientMode = savedAmbientMode;
            RenderSettings.ambientLight = savedAmbient;
            RenderSettings.ambientIntensity = savedAmbientIntensity;
            ambientSaved = false;
        }
        doorTarget = 0f;
        HudHider.Show();
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        if (released && PlayerInteraction.Instance) PlayerInteraction.Instance.ReleaseMainTask(this);
    }

    // ------------------------------------------------------------------ the real doors

    void Update()
    {
        AnimateDoors();
        if (!running || PauseMenu.IsPaused) return;
        if (Time.frameCount == openedFrame) return;
        if (Input.GetKeyDown(KeyCode.E) || Input.GetMouseButtonDown(1)) { Close(true); return; }

        // Point and click.
        hover = -1;
        if (cam)
        {
            var ray = cam.ScreenPointToRay(Input.mousePosition);
            float best = float.MaxValue;
            for (int i = 0; i < items.Count; i++)
            {
                var b = Cooking.Bounds(items[i].t.gameObject);
                b.Expand(0.03f);
                if (b.IntersectRay(ray, out float d) && d < best) { best = d; hover = i; }
            }
        }
        for (int i = 0; i < items.Count; i++)
        {
            var (e, t, tag, basePos) = items[i];
            bool on = i == hover;
            t.localScale = Vector3.Lerp(t.localScale, Vector3.one * (on ? 1.12f : 1f), Time.unscaledDeltaTime * 12f);
            t.localPosition = basePos + Vector3.up * (on ? 0.015f : 0f);
            if (tag) tag.material.color = TagColour(e, on);
            if (pads.TryGetValue(t, out var pad) && pad)
            {
                pad.material.color = on ? PadHover : PadIdle;
                pad.material.SetColor("_EmissionColor", on ? new Color(1f, 0.75f, 0.2f) * 0.7f : Color.black);
            }
        }
        if (hover >= 0 && Input.GetMouseButtonDown(0))
        {
            var e = items[hover].e;
            Close(true);
            onTake?.Invoke(e);
        }
    }

    void AnimateDoors()
    {
        if (hinges.Count == 0 && doorTarget > 0f && station) SetUpHinges(station);
        doorOpen = Mathf.MoveTowards(doorOpen, doorTarget, Time.unscaledDeltaTime * 3f);
        foreach (var (h, sign) in hinges) if (h) h.localRotation = Quaternion.Euler(0f, sign * 100f * Mathf.SmoothStep(0f, 1f, doorOpen), 0f);
        if (doorOpen <= 0f && doorTarget <= 0f && hinges.Count > 0 && !running) hinges.Clear();
    }

    /// <summary>Each door turns round its outer edge (like the wardrobe's). Done once per station.</summary>
    void SetUpHinges(Transform s)
    {
        hinges.Clear();
        hingeStation = s;
        var whole = Cooking.Bounds(s.gameObject);
        Vector3 centre = s.InverseTransformPoint(whole.center);
        foreach (var t in s.GetComponentsInChildren<Transform>(true))
        {
            if (t.name.StartsWith("Hinge "))
            {
                hinges.Add((t, t.name.EndsWith("+") ? 1f : -1f));
                continue;
            }
        }
        if (hinges.Count > 0) return;
        foreach (var t in s.GetComponentsInChildren<Transform>(true))
        {
            if (t == s || !t.name.Contains("Door") || (t.parent && t.parent.name.StartsWith("Hinge "))) continue;
            var b = Cooking.Bounds(t.gameObject);
            Vector3 local = s.InverseTransformPoint(b.center);
            Vector3 ext = s.InverseTransformVector(b.extents);
            float side = local.x < centre.x ? -1f : 1f;
            float sign = side > 0f ? 1f : -1f;
            var hinge = new GameObject("Hinge " + t.name + (sign > 0 ? " +" : " -")).transform;
            hinge.SetParent(s, false);
            hinge.localPosition = new Vector3(local.x + side * Mathf.Abs(ext.x), local.y, local.z + Mathf.Abs(ext.z));
            hinge.localRotation = Quaternion.identity;
            t.SetParent(hinge, true);
            hinges.Add((hinge, sign));
        }
    }

    // ------------------------------------------------------------------ inside: a real fridge / freezer / pantry

    /// <summary>Somewhere things stand: a shelf (name tags on its front trim) or a drawer (name tags on its front's top band).</summary>
    class Bay
    {
        public float x0, x1, floorY, z0, z1, clear;   // what it can hold: across, the floor, front to back, headroom
        public float tagTop, tagZ;                    // the band the name tags clip onto: its top edge, its front face
        public int capacity;
        public bool drawer;
        public readonly List<Entry> entries = new List<Entry>();
    }

    readonly List<Bay> bays = new List<Bay>();
    readonly List<Vector3> framePoints = new List<Vector3>(); // what the camera must get in the picture
    System.Random rng;
    float W, H, D, F; // the inside (width, height, depth) and the body's frame round the opening

    const float TagH = 0.05f, TrimH = TagH + 0.008f, BandH = TagH + 0.01f;
    const float ViewTop = 0.9f, ViewBottom = 0.08f; // the screen between the title and the hint

    static readonly HashSet<string> Produce = new HashSet<string> { "carrot", "tomato", "lettuce", "cucumber", "pepper", "mushroom", "potato", "onion" };

    float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);

    void Build()
    {
        root = new GameObject("Inside " + title).transform;
        root.position = Stage;
        bays.Clear();
        framePoints.Clear();
        rng = new System.Random((int)kind * 7919 + 17); // the same fridge every time you open it
        bool fridge = kind == KitchenStation.Kind.Fridge, freezer = kind == KitchenStation.Kind.Freezer;
        if (fridge || freezer) Appliance(freezer); else Pantry();
        Surroundings(fridge || freezer);
        Fill(fridge, freezer);
        Lights(fridge, freezer);
        Aim();

        var f = GameHUD.Instance ? GameHUD.Instance.font : null;
        ui = UIFactory.CreateCanvas("Storage UI", 61);
        var t1 = UIFactory.Text("Title", ui.transform, 42, Color.white, TextAlignmentOptions.Center, f);
        t1.text = "<b>" + title.ToUpperInvariant() + "</b>";
        t1.outlineWidth = 0.2f; t1.outlineColor = new Color32(20, 20, 26, 255);
        UIFactory.Place(t1.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), new Vector2(0, -60), new Vector2(900, 56));
        var t2 = UIFactory.Text("Hint", ui.transform, 20, new Color(1f, 1f, 1f, 0.9f), TextAlignmentOptions.Center, f);
        t2.text = "Click what you want to take   -   green tags = tonight's recipe   -   E, Esc or right-click to close";
        t2.outlineWidth = 0.2f; t2.outlineColor = new Color32(20, 20, 26, 255);
        UIFactory.Place(t2.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0.5f), new Vector2(0, 40), new Vector2(1400, 30));
    }

    // ------------------------------------------------------------------ the fridge / the freezer

    void Appliance(bool freezer)
    {
        W = 0.9f; H = 1.15f; D = 0.62f; F = 0.06f;
        var body = Mat(freezer ? new Color(0.21f, 0.24f, 0.33f) : new Color(0.9f, 0.91f, 0.93f), 0.3f);
        var liner = Mat(freezer ? new Color(0.88f, 0.93f, 0.98f) : new Color(0.95f, 0.96f, 0.97f), 0.3f);
        var trim = Mat(new Color(0.97f, 0.97f, 0.98f), 0.5f);
        var rubber = Mat(new Color(0.27f, 0.28f, 0.3f), 0.15f);
        var glass = Clear(new Color(0.8f, 0.92f, 1f, 0.22f), 0.95f);

        // The plastic inside: back, sides, ceiling, floor.
        Box("Back", new Vector3(0f, H * 0.5f, D + 0.005f), new Vector3(W + 0.02f, H + 0.02f, 0.01f), liner);
        foreach (float s in new[] { -1f, 1f })
            Box("Side", new Vector3(s * (W * 0.5f + 0.005f), H * 0.5f, D * 0.5f), new Vector3(0.01f, H + 0.02f, D), liner);
        Box("Ceiling", new Vector3(0f, H + 0.005f, D * 0.5f), new Vector3(W, 0.01f, D), liner);
        Box("Floor", new Vector3(0f, -0.005f, D * 0.5f), new Vector3(W, 0.01f, D), liner);
        // The appliance's body round the opening, the door seal just inside it, the kick plate underneath.
        Box("Body", new Vector3(-W * 0.5f - F * 0.5f, H * 0.5f, -0.01f), new Vector3(F, H + 2f * F, 0.04f), body);
        Box("Body", new Vector3(W * 0.5f + F * 0.5f, H * 0.5f, -0.01f), new Vector3(F, H + 2f * F, 0.04f), body);
        Box("Body", new Vector3(0f, H + F * 0.5f, -0.01f), new Vector3(W, F, 0.04f), body);
        Box("Body", new Vector3(0f, -F * 0.5f, -0.01f), new Vector3(W, F, 0.04f), body);
        Box("Kick plate", new Vector3(0f, -F - 0.04f, 0.01f), new Vector3(W + F, 0.08f, 0.03f), Mat(new Color(0.2f, 0.2f, 0.22f), 0.2f));
        const float seal = 0.012f;
        foreach (float s in new[] { -1f, 1f })
            Box("Seal", new Vector3(s * (W * 0.5f - seal * 0.5f), H * 0.5f, -0.002f), new Vector3(seal, H, 0.006f), rubber);
        Box("Seal", new Vector3(0f, H - seal * 0.5f, -0.002f), new Vector3(W, seal, 0.006f), rubber);
        Box("Seal", new Vector3(0f, seal * 0.5f, -0.002f), new Vector3(W, seal, 0.006f), rubber);
        // The light strip in the ceiling.
        Box("Light strip", new Vector3(0f, H - 0.004f, 0.07f), new Vector3(0.34f, 0.006f, 0.035f), Glow(freezer ? new Color(0.85f, 0.93f, 1f) : new Color(1f, 0.97f, 0.9f), 1.4f));

        if (!freezer)
        {
            // An air vent at the top of the back.
            var vent = Mat(new Color(0.75f, 0.77f, 0.8f), 0.3f);
            for (int i = 0; i < 6; i++) Box("Vent", new Vector3(-0.1f + i * 0.04f, H - 0.09f, D - 0.002f), new Vector3(0.026f, 0.05f, 0.006f), vent);
            // Two salad crispers at the bottom (pulled out), glass shelves above.
            var crisper = Clear(new Color(0.86f, 0.96f, 0.9f, 0.26f), 0.85f);
            Drawer("Crisper", -W * 0.25f, W * 0.5f - 0.025f, 0.012f, 0.24f, D - 0.06f, 0.17f, crisper, trim, 2);
            Drawer("Crisper", W * 0.25f, W * 0.5f - 0.025f, 0.012f, 0.24f, D - 0.06f, 0.1f, crisper, trim, 2);
            Shelf(0.32f, 0.63f, glass, trim, liner, 3);
            Shelf(0.63f, 0.91f, glass, trim, liner, 3);
            Shelf(0.91f, H, glass, trim, liner, 3);
        }
        else
        {
            // The cold plate at the back, three frosted drawers (the top two pulled out) and the ice shelf on top.
            for (int i = 0; i < 9; i++) Box("Cold plate", new Vector3(0f, 0.08f + i * 0.1f, D - 0.003f), new Vector3(W - 0.08f, 0.006f, 0.008f), liner);
            var frosted = Clear(new Color(0.92f, 0.96f, 1f, 0.32f), 0.7f);
            Drawer("Bottom drawer", 0f, W - 0.03f, 0.012f, 0.27f, D - 0.06f, 0f, frosted, trim, 3);
            Drawer("Middle drawer", 0f, W - 0.03f, 0.297f, 0.27f, D - 0.06f, 0.22f, frosted, trim, 4); // the main one, pulled out furthest
            Drawer("Top drawer", 0f, W - 0.03f, 0.582f, 0.27f, D - 0.06f, 0.1f, frosted, trim, 3); // a little: its things sit behind the middle one's tags
            Shelf(0.93f, H, glass, trim, liner, 3);
            Frost();
        }
        Door(freezer, body, liner, trim, rubber);
        framePoints.Add(new Vector3(-W * 0.5f - F, -F - 0.08f, -0.03f));
        framePoints.Add(new Vector3(W * 0.5f + F, -F - 0.08f, -0.03f));
        framePoints.Add(new Vector3(-W * 0.5f - F, H + F, -0.03f));
        framePoints.Add(new Vector3(W * 0.5f + F, H + F, -0.03f));
    }

    const float DoorAngle = 112f;

    /// <summary>
    /// The door, swung open on its left hinge towards you: the appliance's colour outside, the plastic liner and seal inside, a handle,
    /// and three bins full of the usual things (milk, juice, sauces / ice cream, ice lollies, frozen bags).
    /// </summary>
    void Door(bool freezer, Material body, Material liner, Material trim, Material rubber, bool pantry = false)
    {
        float dw = W + 2f * F, dh = H + 2f * F, thick = 0.07f;
        var door = new GameObject("Door").transform;
        door.SetParent(root, false);
        door.localPosition = new Vector3(-W * 0.5f - F, -F, -0.035f);
        door.localRotation = Quaternion.Euler(0f, DoorAngle, 0f); // the free edge swings out towards you
        Part(door, PrimitiveType.Cube, new Vector3(dw * 0.5f, dh * 0.5f, -thick * 0.5f), new Vector3(dw, dh, thick), body).name = "Door panel";
        Part(door, PrimitiveType.Cube, new Vector3(dw * 0.5f, dh * 0.5f, 0.006f), new Vector3(dw - 0.08f, dh - 0.08f, 0.012f), liner).name = "Door liner";
        const float s = 0.014f;
        Part(door, PrimitiveType.Cube, new Vector3(0.025f, dh * 0.5f, 0.004f), new Vector3(s, dh - 0.03f, 0.01f), rubber).name = "Door seal";
        Part(door, PrimitiveType.Cube, new Vector3(dw - 0.025f, dh * 0.5f, 0.004f), new Vector3(s, dh - 0.03f, 0.01f), rubber).name = "Door seal";
        Part(door, PrimitiveType.Cube, new Vector3(dw * 0.5f, 0.025f, 0.004f), new Vector3(dw - 0.03f, s, 0.01f), rubber).name = "Door seal";
        Part(door, PrimitiveType.Cube, new Vector3(dw * 0.5f, dh - 0.025f, 0.004f), new Vector3(dw - 0.03f, s, 0.01f), rubber).name = "Door seal";
        var metal = Mat(new Color(0.78f, 0.79f, 0.82f), 0.8f);
        Part(door, PrimitiveType.Cube, new Vector3(dw - 0.06f, dh * 0.55f, -thick - 0.03f), new Vector3(0.026f, dh * 0.4f, 0.026f), metal).name = "Handle";
        var clear = Clear(new Color(0.88f, 0.95f, 1f, 0.35f), 0.85f);
        if (pantry) Bin(door, dh * 0.62f, dw, trim, trim, false, PantryDoor); // a spice rack
        else foreach (float f in freezer ? new[] { 0.2f, 0.5f, 0.8f } : new[] { 0.16f, 0.46f, 0.76f })
            Bin(door, dh * f, dw, trim, clear, freezer);
        // its free edge (top and bottom) must be in the picture
        framePoints.Add(root.InverseTransformPoint(door.TransformPoint(new Vector3(dw, 0f, 0f))));
        framePoints.Add(root.InverseTransformPoint(door.TransformPoint(new Vector3(dw, dh, 0f))));
    }

    static readonly string[] FridgeDoor = { "carton", "carton-small", "soda-bottle", "bottle-ketchup", "bottle-musterd" };
    static readonly string[] FreezerDoor = { "ice-cream-cup", "popsicle", "bag-flat" };
    static readonly string[] PantryDoor = { "honey", "peanut-butter", "can-small", "soy" };

    /// <summary>A door bin: a white floor, clear sides and front lip, three or four things standing in it.</summary>
    void Bin(Transform door, float y, float dw, Material floor, Material clear, bool freezer, string[] items = null)
    {
        float x0 = 0.06f, bw = dw - 0.12f, bd = 0.1f, bh = items != null ? 0.025f : 0.07f, z0 = 0.012f; // a spice rack is just a low rail
        Part(door, PrimitiveType.Cube, new Vector3(x0 + bw * 0.5f, y, z0 + bd * 0.5f), new Vector3(bw, 0.008f, bd), floor).name = "Bin";
        Part(door, PrimitiveType.Cube, new Vector3(x0 + bw * 0.5f, y + bh * 0.5f, z0 + bd), new Vector3(bw, bh, 0.006f), clear).name = "Bin front";
        Part(door, PrimitiveType.Cube, new Vector3(x0, y + bh * 0.5f, z0 + bd * 0.5f), new Vector3(0.006f, bh, bd), clear).name = "Bin side";
        Part(door, PrimitiveType.Cube, new Vector3(x0 + bw, y + bh * 0.5f, z0 + bd * 0.5f), new Vector3(0.006f, bh, bd), clear).name = "Bin side";
        int n = 3 + rng.Next(2);
        var pool = items ?? (freezer ? FreezerDoor : FridgeDoor);
        for (int i = 0; i < n; i++)
        {
            string id = pool[rng.Next(pool.Length)];
            var kit = CookingKit.Instance ? CookingKit.Instance.Model(id) : null;
            GameObject m;
            if (kit) { m = Instantiate(kit); m.name = id; Food.UseFoodPalette(m); Cooking.Fit(m, DecorSize(id)); }
            else m = Homemade(id == "bag-flat" ? "peas" : id);
            var holder = new GameObject("In the door").transform;
            holder.SetParent(door, false);
            holder.localPosition = new Vector3(x0 + bw * (i + 0.5f) / n + R(-0.01f, 0.01f), y + 0.005f, z0 + bd * 0.5f);
            Stand(m, holder, new Vector3(bw / n * 0.8f, 0.24f, bd * 0.8f));
        }
    }

    /// <summary>A glass shelf resting on the side ribs, a white trim along its front (the name tags clip on there).</summary>
    void Shelf(float y, float nextY, Material glass, Material trim, Material liner, int capacity)
    {
        const float front = 0.03f;
        Box("Glass shelf", new Vector3(0f, y, front + (D - front) * 0.5f), new Vector3(W - 0.004f, 0.006f, D - front - 0.005f), glass);
        Box("Shelf trim", new Vector3(0f, y + 0.003f - TrimH * 0.5f, front + 0.007f), new Vector3(W - 0.004f, TrimH, 0.014f), trim);
        Box("Shelf back", new Vector3(0f, y + 0.006f, D - 0.01f), new Vector3(W - 0.004f, 0.012f, 0.012f), trim);
        foreach (float s in new[] { -1f, 1f })
            Box("Rib", new Vector3(s * (W * 0.5f - 0.006f), y - 0.007f, D * 0.5f + 0.02f), new Vector3(0.012f, 0.008f, D - 0.06f), liner);
        bays.Add(new Bay
        {
            x0 = -W * 0.5f + 0.03f, x1 = W * 0.5f - 0.03f, floorY = y + 0.003f, z0 = front + 0.03f, z1 = D - 0.03f, clear = nextY - y - 0.04f,
            tagTop = y, tagZ = front, capacity = capacity,
        });
    }

    /// <summary>A see-through plastic drawer, pulled out by 'pull': the name tags go on its white top band.</summary>
    void Drawer(string n, float cx, float w, float y0, float h, float depth, float pull, Material see, Material band, int capacity)
    {
        float fz = 0.012f - pull; // the inside of its front
        var floor = Mat(new Color(0.72f, 0.77f, 0.84f), 0.4f);
        Box(n + " floor", new Vector3(cx, y0 + 0.004f, fz + depth * 0.5f), new Vector3(w, 0.008f, depth), floor);
        foreach (float s in new[] { -1f, 1f })
            Box(n + " side", new Vector3(cx + s * (w * 0.5f - 0.003f), y0 + h * 0.42f, fz + depth * 0.5f), new Vector3(0.006f, h * 0.84f, depth), see);
        Box(n + " back", new Vector3(cx, y0 + h * 0.42f, fz + depth - 0.003f), new Vector3(w, h * 0.84f, 0.006f), see);
        Box(n + " front", new Vector3(cx, y0 + (h - BandH) * 0.5f, fz - 0.006f), new Vector3(w, h - BandH, 0.012f), see);
        Box(n + " handle", new Vector3(cx, y0 + h - BandH * 0.5f, fz - 0.007f), new Vector3(w, BandH, 0.014f), band);
        bays.Add(new Bay
        {
            x0 = cx - w * 0.5f + 0.025f, x1 = cx + w * 0.5f - 0.025f, floorY = y0 + 0.008f, z0 = fz + 0.02f, z1 = fz + depth - 0.03f, clear = h - BandH - 0.035f, // kept below the handle band
            tagTop = y0 + h - 0.002f, tagZ = fz - 0.014f, capacity = capacity, drawer = true,
        });
        if (pull > 0f)
        {
            framePoints.Add(new Vector3(cx - w * 0.5f, y0, fz - 0.014f));
            framePoints.Add(new Vector3(cx + w * 0.5f, y0, fz - 0.014f));
        }
    }

    /// <summary>Ice built up along the back of the ceiling and the tops of the side walls, and a white frosty layer on the ceiling.</summary>
    void Frost()
    {
        var ice = Glow(new Color(0.93f, 0.97f, 1f), 0.25f);
        ice.SetFloat("_Glossiness", 0.85f);
        // ridges of ice built up along the back top edge and down the top of each side wall
        var back = Part(root, PrimitiveType.Capsule, new Vector3(0f, H - 0.012f, D - 0.012f), new Vector3(0.045f, W * 0.5f, 0.04f), ice);
        back.localRotation = Quaternion.Euler(0f, 0f, 90f);
        back.name = "Ice";
        foreach (float sx in new[] { -1f, 1f })
        {
            var side = Part(root, PrimitiveType.Capsule, new Vector3(sx * (W * 0.5f - 0.01f), H - 0.012f, D * 0.55f), new Vector3(0.035f, D * 0.45f, 0.03f), ice);
            side.localRotation = Quaternion.Euler(90f, 0f, 0f);
            side.name = "Ice";
        }
        Box("Frost layer", new Vector3(0f, H - 0.0015f, D * 0.6f), new Vector3(W - 0.01f, 0.003f, D * 0.8f), Mat(new Color(0.97f, 0.99f, 1f), 0.6f));
        // crystals growing out of the ridges
        for (int i = 0; i < 46; i++)
        {
            Vector3 p = i % 2 == 0
                ? new Vector3(R(-W * 0.48f, W * 0.48f), H - R(0.005f, 0.035f), D - R(0.01f, 0.04f))
                : new Vector3((i % 4 == 1 ? -1f : 1f) * (W * 0.5f - R(0.005f, 0.03f)), H - R(0.005f, 0.04f), R(0.12f, D));
            float sz = R(0.008f, 0.018f);
            var c = Part(root, PrimitiveType.Cube, p, new Vector3(sz, sz * R(0.5f, 1.2f), sz), ice);
            c.localRotation = Quaternion.Euler(R(0f, 360f), R(0f, 360f), R(0f, 360f));
            c.name = "Ice";
        }
    }

    // ------------------------------------------------------------------ the pantry

    void Pantry()
    {
        W = 0.9f; H = 1.1f; D = 0.46f; F = 0.065f;
        var inside = Mat(new Color(0.58f, 0.43f, 0.29f), 0.2f);
        var board = Mat(new Color(0.66f, 0.5f, 0.33f), 0.25f);
        var rail = Mat(new Color(0.45f, 0.32f, 0.2f), 0.3f);
        var body = Mat(new Color(0.93f, 0.92f, 0.9f), 0.35f);
        Box("Back", new Vector3(0f, H * 0.5f, D + 0.005f), new Vector3(W + 0.02f, H + 0.02f, 0.01f), inside);
        foreach (float s in new[] { -1f, 1f })
            Box("Side", new Vector3(s * (W * 0.5f + 0.005f), H * 0.5f, D * 0.5f), new Vector3(0.01f, H + 0.02f, D), inside);
        Box("Ceiling", new Vector3(0f, H + 0.005f, D * 0.5f), new Vector3(W, 0.01f, D), inside);
        Box("Floor", new Vector3(0f, -0.005f, D * 0.5f), new Vector3(W, 0.01f, D), board);
        Box("Body", new Vector3(-W * 0.5f - F * 0.5f, H * 0.5f, -0.01f), new Vector3(F, H + 2f * F, 0.04f), body);
        Box("Body", new Vector3(W * 0.5f + F * 0.5f, H * 0.5f, -0.01f), new Vector3(F, H + 2f * F, 0.04f), body);
        Box("Body", new Vector3(0f, H + F * 0.5f, -0.01f), new Vector3(W, F, 0.04f), body);
        Box("Body", new Vector3(0f, -F * 0.5f, -0.01f), new Vector3(W, F, 0.04f), body);
        Box("Kick plate", new Vector3(0f, -F - 0.04f, 0.01f), new Vector3(W + F, 0.08f, 0.03f), Mat(new Color(0.3f, 0.28f, 0.26f), 0.2f));
        // The bottom row stands on the floor; its tags go on the body's bottom edge.
        bays.Add(new Bay { x0 = -W * 0.5f + 0.03f, x1 = W * 0.5f - 0.03f, floorY = 0f, z0 = 0.03f, z1 = D - 0.03f, clear = 0.28f - 0.05f, tagTop = -0.004f, tagZ = -0.03f, capacity = 4 });
        foreach (float y in new[] { 0.28f, 0.56f, 0.84f })
        {
            Box("Shelf", new Vector3(0f, y - 0.009f, D * 0.5f), new Vector3(W, 0.018f, D - 0.01f), board);
            Box("Label rail", new Vector3(0f, y - TrimH * 0.5f, -0.007f), new Vector3(W, TrimH, 0.014f), rail);
            bays.Add(new Bay { x0 = -W * 0.5f + 0.03f, x1 = W * 0.5f - 0.03f, floorY = y, z0 = 0.03f, z1 = D - 0.03f, clear = (y < 0.8f ? 0.28f : H - y) - 0.05f, tagTop = y - 0.003f, tagZ = -0.014f, capacity = 4 });
        }
        Door(false, body, Mat(new Color(0.9f, 0.89f, 0.86f), 0.3f), board, Mat(new Color(0.75f, 0.74f, 0.7f), 0.3f), pantry: true);
        framePoints.Add(new Vector3(-W * 0.5f - F, -F - 0.08f, -0.03f));
        framePoints.Add(new Vector3(W * 0.5f + F, -F - 0.08f, -0.03f));
        framePoints.Add(new Vector3(-W * 0.5f - F, H + F, -0.03f));
        framePoints.Add(new Vector3(W * 0.5f + F, H + F, -0.03f));
    }

    /// <summary>The kitchen round it: the wall behind and the floor it stands on.</summary>
    void Surroundings(bool appliance)
    {
        Box("Kitchen wall", new Vector3(0f, H * 0.5f, D + 0.12f), new Vector3(8f, 6f, 0.02f), Mat(new Color(0.86f, 0.82f, 0.6f), 0.1f));
        Box("Kitchen floor", new Vector3(0f, -F - 0.085f, -1f), new Vector3(8f, 0.01f, 3.4f), Mat(new Color(0.8f, 0.78f, 0.74f), 0.08f));
    }

    // ------------------------------------------------------------------ what's in it

    void Fill(bool fridge, bool freezer)
    {
        // Where each thing goes: vegetables in the crispers, the rest on the shelves (bottom up); the freezer fills its drawers from
        // the top (most pulled out) down; the pantry from the middle shelves.
        var shelves = bays.Where(b => !b.drawer).OrderBy(b => b.floorY).ToList();
        // drawers: the pulled-out ones first, lowest first (you look down into them); a closed one last
        var drawers = bays.Where(b => b.drawer).OrderBy(b => b.z0 >= 0f ? 1 : 0).ThenBy(b => b.floorY).ToList();
        if (!fridge && !freezer) shelves = shelves.OrderBy(b => Mathf.Abs(b.floorY - 0.4f)).ToList();
        if (freezer)
        {
            var pulled = drawers.Where(b => b.z0 < 0f).ToList();
            int per = Mathf.Clamp(Mathf.CeilToInt(entries.Count / (float)Mathf.Max(1, pulled.Count)), 1, 4);
            foreach (var b in pulled) b.capacity = per; // e.g. five things: three in the middle drawer, two in the top one
            foreach (var b in drawers.Where(b => b.z0 >= 0f)) b.capacity = 0; // the closed one stays empty
        }
        foreach (var e in entries.OrderBy(x => x.needed ? 0 : 1)) // tonight's things first: they get the best spot
        {
            bool veg = Produce.Contains(e.id);
            var order = freezer || (fridge && veg) ? drawers.Concat(shelves) : shelves.Concat(drawers);
            var bay = order.FirstOrDefault(b => b.entries.Count < b.capacity) ?? bays.Where(b => b.capacity > 0).OrderBy(b => b.entries.Count).First();
            bay.entries.Add(e);
        }

        var font = GameHUD.Instance ? GameHUD.Instance.font : null;
        foreach (var bay in bays)
        {
            // the freezer only holds things you can take (nothing that looks clickable but isn't); a closed drawer stays empty
            int extra = freezer ? 0 : Mathf.Min(bay.entries.Count > 0 || bay.drawer ? 1 : 2, Mathf.Max(0, bay.capacity - bay.entries.Count - (rng.NextDouble() < 0.35 ? 1 : 0)));
            int n = bay.entries.Count + extra;
            if (n == 0) continue;
            // spread the real things evenly, the made-up ones in between
            var isEntry = new bool[n];
            for (int k = 0; k < bay.entries.Count; k++) isEntry[Mathf.Clamp(Mathf.RoundToInt((k + 0.5f) * n / bay.entries.Count - 0.5f), 0, n - 1)] = true;
            float slot = (bay.x1 - bay.x0) / n;
            var room = new Vector3(slot * 0.82f, bay.clear * 0.7f, (bay.z1 - bay.z0) * 0.7f); // 0.7: even grown when pointed at, it stays under the shelf above
            int next = 0;
            for (int i = 0; i < n; i++)
            {
                float x = bay.x0 + slot * (i + 0.5f);
                if (isEntry[i] && next < bay.entries.Count)
                {
                    var e = bay.entries[next++];
                    var holder = new GameObject(e.label).transform;
                    holder.SetParent(root, false);
                    var basePos = new Vector3(x, bay.floorY + 0.002f, bay.z0 + (bay.z1 - bay.z0) * (bay.drawer ? 0.15f : 0.35f)); // in a drawer: at the front, seen through it
                    holder.localPosition = basePos;
                    var model = e.model?.Invoke();
                    if (model)
                    {
                        bool bag = freezer;
                        Stand(model, holder, bag ? room - new Vector3(0.024f, 0.016f, 0.024f) : room, grow: true);
                        if (bag) FreezerBag(model, holder);
                        pads[holder] = Pad(holder, slot * 0.88f);
                        // flat things (a steak, mince, bacon, a fish) are propped up at an angle: you see their face, not their edge
                        var fb = Cooking.Bounds(holder.gameObject);
                        if (fb.size.y < 0.5f * Mathf.Max(fb.size.x, fb.size.z))
                        {
                            holder.localRotation = Quaternion.Euler(-24f, 0f, 0f);
                            var tb = Cooking.Bounds(holder.gameObject);
                            float top = root.TransformPoint(new Vector3(0f, bay.floorY + bay.clear, 0f)).y;
                            if (tb.size.y > 1e-4f && tb.max.y - tb.min.y > top - holder.position.y)
                                model.transform.localScale *= (top - holder.position.y) / (tb.max.y - tb.min.y); // stays under the drawer's rim
                            tb = Cooking.Bounds(holder.gameObject);
                            basePos += Vector3.up * (holder.position.y - tb.min.y + 0.002f); // resting on the floor, propped up
                            holder.localPosition = basePos;
                        }
                    }
                    items.Add((e, holder, Tag(e, x, slot, bay, font), basePos));
                }
                else
                {
                    var d = Decor(fridge, freezer, bay.drawer);
                    if (!d) continue;
                    var holder = new GameObject("On show").transform;
                    holder.SetParent(root, false);
                    holder.localPosition = new Vector3(x + R(-0.1f, 0.1f) * slot, bay.floorY + 0.002f, bay.z0 + (bay.z1 - bay.z0) * R(0.35f, 0.65f));
                    holder.localRotation = Quaternion.Euler(0f, R(-30f, 30f), 0f);
                    bool bag = freezer && (d.name == "fish" || d.name == "meat-sausage");
                    Stand(d, holder, bag ? room - new Vector3(0.024f, 0.016f, 0.024f) : room);
                    if (bag) FreezerBag(d, holder);
                }
            }
        }
    }

    readonly Dictionary<Transform, Renderer> pads = new Dictionary<Transform, Renderer>();
    static readonly Color PadIdle = new Color(0.2f, 0.25f, 0.34f, 0.92f), PadHover = new Color(1f, 0.82f, 0.25f, 0.95f);

    /// <summary>A soft glowing pad under something you can take, a little bigger than it (brighter when you point at it).</summary>
    Renderer Pad(Transform holder, float maxDiameter)
    {
        var b = Cooking.Bounds(holder.gameObject);
        float d = Mathf.Min(maxDiameter, Mathf.Max(b.size.x, b.size.z) * 1.15f + 0.02f);
        var m = Clear(PadIdle, 0.3f);
        m.EnableKeyword("_EMISSION");
        m.SetColor("_EmissionColor", Color.black);
        // a dark tray under it (its things stand out against it); it lights up yellow when you point at it
        var pad = Part(holder, PrimitiveType.Cylinder, new Vector3(0f, 0.002f, 0f), new Vector3(d, 0.002f, d), m);
        pad.name = "You can take this";
        return pad.GetComponent<Renderer>();
    }

    /// <summary>
    /// Fits a model into the room it has and stands it on its holder (centred on it). 'grow': also scaled up to fill it (up to 2x) -
    /// the things you can take are never tiny.
    /// </summary>
    static void Stand(GameObject model, Transform holder, Vector3 room, bool grow = false)
    {
        model.transform.SetParent(holder, false);
        model.transform.localPosition = Vector3.zero;
        model.transform.localRotation = Quaternion.Euler(0f, 180f + Random.Range(-20f, 20f), 0f);
        foreach (var c in model.GetComponentsInChildren<Collider>()) Destroy(c);
        var b = Cooking.Bounds(model);
        if (b.size.x > 1e-4f && b.size.y > 1e-4f && b.size.z > 1e-4f)
        {
            float s = Mathf.Min(room.x / b.size.x, room.y / b.size.y, room.z / b.size.z);
            if (grow) s = Mathf.Min(s, 2f);
            if (s < 1f || grow) model.transform.localScale *= s;
        }
        b = Cooking.Bounds(model);
        model.transform.position += new Vector3(holder.position.x - b.center.x, holder.position.y - b.min.y, holder.position.z - b.center.z);
    }

    /// <summary>Frozen food in a freezer bag: frosted over, a clear bag round it with a blue zip and a white write-on label.</summary>
    void FreezerBag(GameObject model, Transform holder)
    {
        foreach (var r in model.GetComponentsInChildren<Renderer>())
        {
            var mats = r.materials;
            foreach (var m in mats) m.color = Color.Lerp(m.color, new Color(0.82f, 0.9f, 1f), 0.1f);
            r.materials = mats;
        }
        var b = Cooking.Bounds(model);
        Vector3 local = holder.InverseTransformPoint(b.center);
        Vector3 size = b.size + new Vector3(0.02f, 0.012f, 0.02f);
        var bag = Part(holder, PrimitiveType.Cube, local, size, Clear(new Color(0.95f, 0.97f, 1f, 0.08f), 0.75f));
        bag.name = "Freezer bag";
        Part(holder, PrimitiveType.Cube, local + new Vector3(0f, size.y * 0.5f - 0.002f, size.z * 0.5f - 0.004f), new Vector3(size.x, 0.004f, 0.006f), Mat(new Color(0.3f, 0.5f, 0.9f), 0.4f)).name = "Zip";
    }

    /// <summary>The name tag: a card on the bay's front band, right in front of the thing, the name inside it.</summary>
    Renderer Tag(Entry e, float x, float slot, Bay bay, TMP_FontAsset font)
    {
        float tagW = Mathf.Min(slot * 0.92f, 0.27f);
        var card = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Destroy(card.GetComponent<Collider>());
        card.name = "Name tag";
        card.transform.SetParent(root, false);
        card.transform.localPosition = new Vector3(x, bay.tagTop - TagH * 0.5f, bay.tagZ - 0.0015f);
        card.transform.localScale = new Vector3(tagW, TagH, 0.003f);
        var cardR = card.GetComponent<Renderer>();
        cardR.material = Mat(TagColour(e, false), 0.3f);
        // a thin dark edge round the card, so it stands out from a white trim
        var edge = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Destroy(edge.GetComponent<Collider>());
        edge.name = "Name tag edge";
        edge.transform.SetParent(root, false);
        edge.transform.localPosition = new Vector3(x, bay.tagTop - TagH * 0.5f, bay.tagZ - 0.0008f);
        edge.transform.localScale = new Vector3(tagW + 0.006f, TagH + 0.006f, 0.0016f);
        edge.GetComponent<Renderer>().sharedMaterial = Mat(new Color(0.22f, 0.18f, 0.14f), 0.2f);
        var label = new GameObject("Name").AddComponent<TextMeshPro>();
        label.transform.SetParent(root, false);
        label.transform.localPosition = new Vector3(x, bay.tagTop - TagH * 0.5f, bay.tagZ - 0.0035f);
        label.transform.localRotation = Quaternion.identity; // reads from the front (the camera looks +z)
        if (font) label.font = font;
        label.text = e.label.ToUpperInvariant();
        label.color = new Color(0.08f, 0.08f, 0.12f);
        label.fontStyle = FontStyles.Bold;
        label.alignment = TextAlignmentOptions.Center;
        label.enableWordWrapping = false;
        label.overflowMode = TextOverflowModes.Truncate;
        label.enableAutoSizing = true;
        label.fontSizeMin = 0.02f;
        label.fontSizeMax = 0.3f;
        label.margin = Vector4.zero;
        label.rectTransform.pivot = new Vector2(0.5f, 0.5f);
        label.rectTransform.sizeDelta = new Vector2(tagW - 0.014f, TagH - 0.008f); // inside the card with a border
        return cardR;
    }

    // ------------------------------------------------------------------ the other things in there (on show, not to take)

    static readonly string[] FridgeShelf = { "carton", "carton-small", "bottle-ketchup", "bottle-musterd", "soda-bottle", "peanut-butter", "honey" };
    static readonly string[] FridgeDrawer = { "apple", "lemon", "orange", "broccoli", "radish" };
    static readonly string[] FreezerStuff = { "ice-cream-cup", "popsicle", "fish", "meat-sausage", "peas", "ice tray" };
    static readonly string[] PantryStuff = { "can", "can-small", "soy", "honey", "peanut-butter", "loaf", "cereal" };

    GameObject Decor(bool fridge, bool freezer, bool drawer)
    {
        var pool = fridge ? (drawer ? FridgeDrawer : FridgeShelf) : freezer ? FreezerStuff : PantryStuff;
        string id = pool[rng.Next(pool.Length)];
        var kit = CookingKit.Instance ? CookingKit.Instance.Model(id) : null;
        if (kit)
        {
            var m = Instantiate(kit);
            Food.UseFoodPalette(m);
            m.name = id;
            Cooking.Fit(m, DecorSize(id));
            return m;
        }
        return Homemade(id);
    }

    static float DecorSize(string id)
    {
        switch (id)
        {
            case "carton": case "soda-bottle": case "fish": case "loaf": return 0.22f;
            case "bottle-ketchup": case "bottle-musterd": return 0.18f;
            case "carton-small": case "popsicle": case "meat-sausage": return 0.13f;
            case "apple": case "lemon": case "orange": case "radish": return 0.08f;
            case "broccoli": return 0.12f;
            default: return 0.11f;
        }
    }

    /// <summary>Made in code when the Food Kit model isn't linked (or for the freezer's own things: peas, ice trays).</summary>
    GameObject Homemade(string id)
    {
        var g = new GameObject(id).transform;
        switch (id)
        {
            case "peas": // a bag of frozen peas
                Part(g, PrimitiveType.Cube, new Vector3(0f, 0.02f, 0f), new Vector3(0.16f, 0.04f, 0.12f), Mat(new Color(0.25f, 0.6f, 0.3f), 0.5f));
                Part(g, PrimitiveType.Cube, new Vector3(0f, 0.0405f, 0f), new Vector3(0.1f, 0.002f, 0.05f), Mat(Color.white, 0.3f));
                Part(g, PrimitiveType.Cube, new Vector3(0f, 0.02f, -0.065f), new Vector3(0.16f, 0.03f, 0.01f), Mat(new Color(0.2f, 0.5f, 0.25f), 0.5f));
                break;
            case "ice tray": // a white tray of ice cubes
                Part(g, PrimitiveType.Cube, new Vector3(0f, 0.011f, 0f), new Vector3(0.1f, 0.022f, 0.24f), Mat(new Color(0.92f, 0.95f, 0.98f), 0.5f));
                var ice = Mat(new Color(0.8f, 0.92f, 1f), 0.9f);
                for (int r = 0; r < 6; r++)
                    for (int c = 0; c < 2; c++)
                        Part(g, PrimitiveType.Cube, new Vector3(-0.024f + c * 0.048f, 0.024f, -0.1f + r * 0.04f), new Vector3(0.036f, 0.012f, 0.032f), ice);
                break;
            case "cereal":
                Part(g, PrimitiveType.Cube, new Vector3(0f, 0.1f, 0f), new Vector3(0.14f, 0.2f, 0.05f), Mat(new Color(0.9f, 0.45f, 0.15f), 0.3f));
                Part(g, PrimitiveType.Cube, new Vector3(0f, 0.11f, -0.026f), new Vector3(0.1f, 0.08f, 0.002f), Mat(new Color(0.98f, 0.92f, 0.6f), 0.3f));
                break;
            case "carton": case "carton-small":
            {
                float h = id == "carton" ? 0.16f : 0.1f;
                Part(g, PrimitiveType.Cube, new Vector3(0f, h * 0.5f, 0f), new Vector3(0.07f, h, 0.07f), Mat(Color.white, 0.3f));
                Part(g, PrimitiveType.Cube, new Vector3(0f, h * 0.55f, 0f), new Vector3(0.071f, h * 0.3f, 0.071f), Mat(new Color(0.25f, 0.45f, 0.85f), 0.3f));
                var roof = Part(g, PrimitiveType.Cube, new Vector3(0f, h, 0f), new Vector3(0.0495f, 0.0495f, 0.07f), Mat(Color.white, 0.3f));
                roof.localRotation = Quaternion.Euler(0f, 0f, 45f);
                break;
            }
            case "apple": case "lemon": case "orange": case "radish":
            {
                var col = id == "apple" ? new Color(0.8f, 0.15f, 0.15f) : id == "lemon" ? new Color(0.95f, 0.85f, 0.2f) : id == "orange" ? new Color(0.95f, 0.55f, 0.1f) : new Color(0.85f, 0.2f, 0.35f);
                Part(g, PrimitiveType.Sphere, new Vector3(0f, 0.035f, 0f), Vector3.one * 0.07f, Mat(col, 0.45f));
                break;
            }
            case "can": case "can-small":
            {
                float h = id == "can" ? 0.11f : 0.07f;
                Part(g, PrimitiveType.Cylinder, new Vector3(0f, h * 0.5f, 0f), new Vector3(0.065f, h * 0.5f, 0.065f), Mat(new Color(0.78f, 0.8f, 0.83f), 0.7f));
                Part(g, PrimitiveType.Cylinder, new Vector3(0f, h * 0.5f, 0f), new Vector3(0.066f, h * 0.32f, 0.066f), Mat(new Color(0.8f, 0.2f, 0.15f), 0.4f));
                break;
            }
            case "ice-cream-cup":
                Part(g, PrimitiveType.Cylinder, new Vector3(0f, 0.035f, 0f), new Vector3(0.11f, 0.035f, 0.11f), Mat(new Color(0.95f, 0.88f, 0.75f), 0.4f));
                Part(g, PrimitiveType.Cylinder, new Vector3(0f, 0.072f, 0f), new Vector3(0.115f, 0.004f, 0.115f), Mat(new Color(0.55f, 0.3f, 0.6f), 0.5f));
                break;
            default: // a bottle / a jar
            {
                bool bottle = id.StartsWith("bottle") || id == "soda-bottle" || id == "soy";
                var col = id.Contains("ketchup") ? new Color(0.8f, 0.12f, 0.1f) : id.Contains("musterd") || id == "honey" ? new Color(0.92f, 0.72f, 0.15f) : id == "peanut-butter" ? new Color(0.6f, 0.38f, 0.2f) : new Color(0.3f, 0.6f, 0.35f);
                float h = bottle ? 0.14f : 0.08f;
                Part(g, PrimitiveType.Cylinder, new Vector3(0f, h * 0.5f, 0f), new Vector3(bottle ? 0.055f : 0.075f, h * 0.5f, bottle ? 0.055f : 0.075f), Mat(col, 0.6f));
                if (bottle) Part(g, PrimitiveType.Cylinder, new Vector3(0f, h + 0.015f, 0f), new Vector3(0.025f, 0.015f, 0.025f), Mat(col, 0.6f));
                Part(g, PrimitiveType.Cylinder, new Vector3(0f, bottle ? h + 0.034f : h + 0.006f, 0f), new Vector3(bottle ? 0.027f : 0.078f, 0.006f, bottle ? 0.027f : 0.078f), Mat(new Color(0.85f, 0.8f, 0.7f), 0.5f));
                break;
            }
        }
        return g.gameObject;
    }

    // ------------------------------------------------------------------ light and camera

    UnityEngine.Rendering.AmbientMode savedAmbientMode;
    Color savedAmbient;
    float savedAmbientIntensity;
    bool ambientSaved;

    void Lights(bool fridge, bool freezer)
    {
        // the kitchen's light: warm, from the front-right, soft shadows (the fridge stands on the floor, the door casts a shadow)
        var room = new GameObject("Room light").AddComponent<Light>();
        room.transform.SetParent(root, false);
        room.transform.localRotation = Quaternion.Euler(38f, -32f, 0f);
        room.type = LightType.Directional;
        room.color = new Color(1f, 0.95f, 0.86f);
        room.intensity = 0.5f;
        room.shadows = LightShadows.Soft;
        room.shadowStrength = 0.5f;
        if (!ambientSaved)
        {
            savedAmbientMode = RenderSettings.ambientMode;
            savedAmbient = RenderSettings.ambientLight;
            savedAmbientIntensity = RenderSettings.ambientIntensity;
            ambientSaved = true;
        }
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.3f, 0.29f, 0.28f);

        var inside = new GameObject("Inside light").AddComponent<Light>();
        inside.transform.SetParent(root, false);
        inside.transform.localPosition = new Vector3(0f, H - 0.14f, D * 0.4f);
        inside.type = LightType.Point;
        inside.range = 1.6f;
        inside.intensity = freezer ? 0.7f : fridge ? 0.9f : 0.8f;
        inside.color = freezer ? new Color(0.86f, 0.93f, 1f) : fridge ? new Color(1f, 0.98f, 0.94f) : new Color(1f, 0.9f, 0.75f);
        inside.shadows = LightShadows.Soft;
        inside.shadowStrength = 0.55f;
        var fill = new GameObject("Fill light").AddComponent<Light>();
        fill.transform.SetParent(root, false);
        fill.transform.localPosition = new Vector3(0f, H * 0.6f, -1.2f);
        fill.type = LightType.Point;
        fill.range = 4f;
        fill.intensity = 0.35f;
        fill.shadows = LightShadows.None;
        var low = new GameObject("Low fill").AddComponent<Light>();
        low.transform.SetParent(root, false);
        low.transform.localPosition = new Vector3(0f, 0.15f, -0.5f);
        low.type = LightType.Point;
        low.range = 1.5f;
        low.intensity = 0.2f;
        low.shadows = LightShadows.None;
    }

    /// <summary>Standing in front of it, looking in and a little down: the whole thing between the title and the hint.</summary>
    void Aim()
    {
        cam = new GameObject("Storage camera").AddComponent<Camera>();
        cam.transform.SetParent(root, false);
        cam.transform.localRotation = Quaternion.Euler(12f, -18f, 0f);
        cam.fieldOfView = 46f;
        cam.nearClipPlane = 0.05f;
        cam.farClipPlane = 12f;
        cam.depth = 50f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.62f, 0.58f, 0.42f);
        Vector3 aim = Vector3.zero;
        foreach (var p in framePoints) aim += p;
        aim /= Mathf.Max(1, framePoints.Count);
        var look = cam.transform.localRotation;
        Vector3 fwd = look * Vector3.forward, right = look * Vector3.right, up = look * Vector3.up;
        float tanY = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
        Vector3 best = aim - fwd * 1.8f;
        for (float dist = 4f; dist > 0.4f; dist -= 0.01f)
        {
            Vector3 pos = aim - fwd * dist;
            for (int it = 0; it < 3; it++)
            {
                cam.transform.localPosition = pos;
                var e = Extent();
                pos += right * (e.center.x - 0.5f) * 2f * dist * tanY * cam.aspect + up * (e.center.y - (ViewTop + ViewBottom) * 0.5f) * 2f * dist * tanY;
            }
            cam.transform.localPosition = pos;
            var f = Extent();
            if (f.width < 0f || f.xMin < 0.03f || f.xMax > 0.97f || f.yMin < ViewBottom || f.yMax > ViewTop) break;
            best = pos;
        }
        cam.transform.localPosition = best;
    }

    Rect Extent()
    {
        float x0 = 9f, x1 = -9f, y0 = 9f, y1 = -9f;
        foreach (var p in framePoints)
        {
            var v = cam.WorldToViewportPoint(root.TransformPoint(p));
            if (v.z < cam.nearClipPlane * 2f) return new Rect(0f, 0f, -1f, -1f);
            x0 = Mathf.Min(x0, v.x); x1 = Mathf.Max(x1, v.x); y0 = Mathf.Min(y0, v.y); y1 = Mathf.Max(y1, v.y);
        }
        return Rect.MinMaxRect(x0, y0, x1, y1);
    }

    // ------------------------------------------------------------------ bits

    /// <summary>Tonight's ingredients have green tags, the rest cream; the one you point at turns yellow.</summary>
    static Color TagColour(Entry e, bool hover) =>
        hover ? new Color(1f, 0.82f, 0.3f) : e.needed ? new Color(0.45f, 0.84f, 0.45f) : new Color(0.99f, 0.94f, 0.78f);

    void Box(string n, Vector3 pos, Vector3 size, Material m) => Part(root, PrimitiveType.Cube, pos, size, m).name = n;

    static Transform Part(Transform parent, PrimitiveType type, Vector3 pos, Vector3 scale, Material m)
    {
        var p = GameObject.CreatePrimitive(type);
        Destroy(p.GetComponent<Collider>());
        p.transform.SetParent(parent, false);
        p.transform.localPosition = pos;
        p.transform.localScale = scale;
        var r = p.GetComponent<Renderer>();
        r.sharedMaterial = m;
        if (m && m.renderQueue >= 3000) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; // glass, clear plastic
        return p.transform;
    }

    static Material Mat(Color c, float gloss)
    {
        var m = new Material(Shader.Find("Standard")) { color = c };
        m.SetFloat("_Glossiness", gloss);
        return m;
    }

    static Material Glow(Color c, float strength)
    {
        var m = Mat(c, 0.5f);
        m.EnableKeyword("_EMISSION");
        m.SetColor("_EmissionColor", c * strength);
        return m;
    }

    /// <summary>See-through (glass, clear plastic): the Standard shader in Fade mode.</summary>
    static Material Clear(Color c, float gloss)
    {
        var m = Mat(c, gloss);
        m.SetFloat("_Mode", 2f);
        m.SetOverrideTag("RenderType", "Transparent");
        m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        m.SetInt("_ZWrite", 0);
        m.DisableKeyword("_ALPHATEST_ON");
        m.EnableKeyword("_ALPHABLEND_ON");
        m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        m.renderQueue = 3000;
        return m;
    }
}

/// <summary>Hides the house HUD (notes, toasts, clock...) while a full-screen kitchen view is up, and brings it back after.</summary>
public static class HudHider
{
    static readonly List<Canvas> hidden = new List<Canvas>();

    public static void Hide()
    {
        hidden.Clear();
        foreach (var c in Object.FindObjectsOfType<Canvas>())
            if (c.isRootCanvas && c.renderMode == RenderMode.ScreenSpaceOverlay && c.sortingOrder < 60 && c.enabled) { c.enabled = false; hidden.Add(c); }
    }

    public static void Show()
    {
        foreach (var c in hidden) if (c) c.enabled = true;
        hidden.Clear();
    }
}
