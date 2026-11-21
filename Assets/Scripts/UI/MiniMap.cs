using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The minimap (top left), on a paper note:
///   - which floor you're on: three pills, yours in yellow; a red badge counts the tasks on each floor
///   - a round map that keeps you in the middle (north is up, your arrow and view cone turn with you)
///   - every active task as a numbered pin (blue = homework, orange = task, red = emergency) and what you
///     need for it as a small green pin with the same number. Pins further away sit on the rim, pointing
///     the way; pins on another floor are faded with an up/down arrow
///   - the room you're in, and a numbered list saying which room each task is in
/// Press M to hide/show it. Needs a HouseLayout in the scene (made by G7l6 > Build New House).
/// </summary>
public class MiniMap : MonoBehaviour
{
    public KeyCode toggleKey = KeyCode.M;
    [Tooltip("Radius of the round map, in pixels.")]
    public float radius = 110f;
    [Tooltip("How far you can see on the map, from you to the rim (house plan metres).")]
    public float viewMetres = 8f;

    const float CardWidth = 300f, RowHeight = 24f, ToolRowHeight = 19f;
    const float PillWidth = 76f, PillGap = 8f;

    static readonly Color Ink = new Color32(34, 32, 42, 255);
    static readonly Color InkFaint = new Color32(34, 32, 42, 80);
    static readonly Color WallColor = new Color32(43, 40, 51, 255);
    static readonly Color Glass = new Color32(168, 216, 244, 255);
    static readonly Color RailColor = new Color32(140, 136, 150, 255);
    static readonly Color Outside = new Color32(233, 227, 211, 255);
    static readonly Color Here = new Color32(255, 240, 166, 255);
    static readonly Color PillOn = new Color32(255, 222, 92, 255);
    static readonly Color Paper = new Color32(250, 247, 238, 255);
    static readonly Color TaskColor = new Color32(232, 128, 26, 255);
    static readonly Color HazardColor = new Color32(224, 53, 43, 255);
    static readonly Color HomeworkColor = new Color32(52, 120, 206, 255);
    static readonly Color ToolColor = new Color32(46, 150, 68, 255);
    static readonly Color MomColor = new Color32(196, 70, 160, 255);

    HouseLayout layout;
    Transform player;
    TMP_FontAsset font;
    float k; // pixels per plan metre

    RectTransform card, world, cone, arrow, pinLayer, rowLayer;
    CanvasGroup group;
    MapMesh rooms;
    TextMeshProUGUI roomText;
    readonly List<Pill> pills = new List<Pill>();
    readonly List<Pin> pinPool = new List<Pin>();
    readonly List<Row> rowPool = new List<Row>();
    readonly List<TextMeshProUGUI> roomLabels = new List<TextMeshProUGUI>();
    float listTop;

    int shownFloor = -1;
    HouseLayout.RoomInfo current;
    float targetAlpha = 1f;
    Carryable[] items = new Carryable[0];
    readonly List<Entry> entries = new List<Entry>();
    readonly int[] perFloor = new int[8];

    static Sprite small, big, ring;

    /// <summary>One task (or the thing it needs) to show.</summary>
    struct Entry
    {
        public int number;
        public string badge; // shown instead of the number (e.g. "M" for Mom)
        public Vector3 world;
        public Color color;
        public string name;
        public bool tool, pulse;
    }

    class Pill { public Image outline, fill, badge; public TextMeshProUGUI text, count; }
    class Pin { public RectTransform root; public Image body, edge, pointer, upDown; public TextMeshProUGUI number; public CanvasGroup fade; }
    class Row
    {
        public RectTransform root;
        public Image badge, upDown;
        public TextMeshProUGUI number, name, where;
    }

    void Start()
    {
        layout = HouseLayout.Instance;
        var movement = FindObjectOfType<PlayerMovementAdvanced>();
        if (movement) player = movement.transform;
        if (!layout || !player) { enabled = false; return; }
        font = GameHUD.Instance ? GameHUD.Instance.font : null;
        items = FindObjectsOfType<Carryable>(true);
        k = radius / Mathf.Max(1f, viewMetres);
        Build();
    }

    // ------------------------------------------------------------------ building the note

    void Build()
    {
        var canvas = UIFactory.CreateCanvas("Minimap Canvas", 49);
        canvas.transform.SetParent(transform, false);

        card = UIFactory.Rect("Minimap", canvas.transform);
        UIFactory.Place(card, new Vector2(0, 1), new Vector2(0, 1), new Vector2(22, -22), new Vector2(CardWidth, 400));
        card.localRotation = Quaternion.Euler(0, 0, -1f);
        group = card.gameObject.AddComponent<CanvasGroup>();
        group.blocksRaycasts = false;

        var shadow = UIFactory.Panel("Shadow", card, new Color(0, 0, 0, 0.22f));
        UIFactory.Fill(shadow.rectTransform, 7, -7, 9, -9);
        var paper = UIFactory.Panel("Paper", card, Paper);
        paper.sprite = HudArt.Get("note_paper");
        UIFactory.Fill(paper.rectTransform);
        var tape = UIFactory.Panel("Tape", card, Color.white);
        tape.sprite = HudArt.Get("tape");
        UIFactory.Place(tape.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), new Vector2(0, -2), new Vector2(100, 28));
        tape.rectTransform.localRotation = Quaternion.Euler(0, 0, 3f);

        // Floor pills
        int floors = layout.Floors;
        float rowW = floors * PillWidth + (floors - 1) * PillGap;
        for (int f = 0; f < floors; f++)
        {
            var pill = new Pill();
            Vector2 at = new Vector2(-rowW * 0.5f + PillWidth * 0.5f + f * (PillWidth + PillGap), -30);
            pill.outline = Shape("Floor " + f, card, Small(), Ink, at, new Vector2(PillWidth + 3, 27), sliced: true);
            pill.fill = Shape("Fill", pill.outline.transform, Small(), Paper, Vector2.zero, new Vector2(PillWidth - 1, 23), sliced: true);
            Centre(pill.fill.rectTransform);
            pill.text = Text("Name", pill.outline.transform, 14, Ink, font, Vector2.zero, new Vector2(PillWidth, 24));
            pill.text.text = FloorName(f);
            pill.badge = Shape("Tasks", pill.outline.transform, Small(), HazardColor, new Vector2(PillWidth * 0.5f - 3, 12), new Vector2(17, 17));
            Centre(pill.badge.rectTransform);
            pill.count = Text("Count", pill.badge.transform, 11, Color.white, null, Vector2.zero, new Vector2(17, 17));
            pill.count.fontStyle = FontStyles.Bold;
            pills.Add(pill);
        }

        // The round map
        float cy = -56 - radius;
        var rim = Shape("Map", card, Big(), Ink, new Vector2(0, cy), Vector2.one * (radius * 2 + 6));
        var disc = Shape("Map area", rim.transform, Big(), Outside, Vector2.zero, Vector2.one * radius * 2);
        Centre(disc.rectTransform);
        disc.gameObject.AddComponent<Mask>().showMaskGraphic = true;

        world = UIFactory.Rect("House", disc.transform);
        UIFactory.Place(world, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        rooms = Mesh("Rooms", world);

        cone = UIFactory.Rect("View", disc.transform);
        UIFactory.Place(cone, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        var coneMesh = Mesh("Cone", cone);
        const int steps = 10;
        const float coneR = 46f, half = 0.55f;
        for (int i = 0; i < steps; i++)
        {
            float a0 = -half + 2 * half * i / steps, a1 = -half + 2 * half * (i + 1) / steps;
            coneMesh.Triangle(Vector2.zero, new Vector2(Mathf.Sin(a0), Mathf.Cos(a0)) * coneR, new Vector2(Mathf.Sin(a1), Mathf.Cos(a1)) * coneR,
                new Color(0.88f, 0.21f, 0.17f, 0.4f), new Color(0.88f, 0.21f, 0.17f, 0f), new Color(0.88f, 0.21f, 0.17f, 0f));
        }
        coneMesh.Apply();

        var rimLine = Shape("Rim", rim.transform, Ring(), Ink, Vector2.zero, Vector2.one * (radius * 2 + 6));
        Centre(rimLine.rectTransform);

        arrow = UIFactory.Rect("You", rim.transform);
        UIFactory.Place(arrow, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(24, 24));
        var arrowEdge = arrow.gameObject.AddComponent<Image>();
        arrowEdge.sprite = HudArt.Get("arrow");
        arrowEdge.color = Color.white;
        arrowEdge.raycastTarget = false;
        var arrowFill = Shape("Fill", arrow, HudArt.Get("arrow"), HudArt.MarkerRed, Vector2.zero, new Vector2(18, 18));
        Centre(arrowFill.rectTransform);

        pinLayer = UIFactory.Rect("Pins", rim.transform);
        UIFactory.Place(pinLayer, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);

        // Where you are + the task list
        roomText = Text("Room", card, 19, Ink, font, new Vector2(0, cy - radius - 24), new Vector2(CardWidth - 30, 26));
        roomText.fontStyle = FontStyles.Bold;
        listTop = -cy + radius + 46;
        var line = UIFactory.Panel("Line", card, new Color(0.13f, 0.13f, 0.16f, 0.18f));
        UIFactory.Place(line.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), new Vector2(0, -listTop), new Vector2(CardWidth - 36, 1.5f));
        rowLayer = UIFactory.Rect("Tasks", card);
        UIFactory.Place(rowLayer, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -listTop - 6), new Vector2(CardWidth - 32, 10));
    }

    // ------------------------------------------------------------------ every frame

    void Update()
    {
        if (!layout || !player) return;
        if (!PauseMenu.IsPaused && Input.GetKeyDown(toggleKey)) targetAlpha = targetAlpha > 0.5f ? 0f : 1f;
        group.alpha = Mathf.MoveTowards(group.alpha, VRHeadset.Worn ? 0f : targetAlpha, Time.unscaledDeltaTime * 5f); // no house map in VR

        int floor = layout.FloorAt(player.position.y - 1f);
        var room = layout.RoomAt(player.position, floor);
        if (floor != shownFloor || room != current)
        {
            bool newFloor = floor != shownFloor;
            shownFloor = floor;
            current = room;
            DrawFloor(newFloor);
        }

        // Keep you in the middle; your arrow and view cone turn with the camera.
        Vector2 p = layout.ToPlan(player.position);
        world.anchoredPosition = -p * k;
        Vector3 look = Camera.main ? Camera.main.transform.forward : player.forward;
        Vector2 fwd = layout.ToPlanDirection(look);
        if (fwd.sqrMagnitude > 0.0001f)
        {
            var turn = Quaternion.Euler(0, 0, -Mathf.Atan2(fwd.x, fwd.y) * Mathf.Rad2Deg);
            cone.localRotation = turn;
            arrow.localRotation = turn;
        }

        CollectTasks();
        UpdatePins(p);
        UpdatePills();
        UpdateRows();
    }

    // ------------------------------------------------------------------ the house

    void DrawFloor(bool newFloor)
    {
        rooms.Clear();
        int floor = shownFloor;

        foreach (var r in layout.rooms)
            if (r.floor == floor) rooms.Rect(Px(r.rect, new Vector2(3, -4)), new Color(0, 0, 0, 0.09f)); // shadow
        foreach (var r in layout.rooms)
        {
            if (r.floor != floor) continue;
            rooms.Rect(Px(r.rect, Vector2.zero), r == current ? Here : KindColor(r.kind));
            if (r.kind == "Stairs")
                for (float z = r.rect.yMin + 0.25f; z < r.rect.yMax - 0.1f; z += 0.5f)
                    rooms.Rect(Px(new Rect(r.rect.xMin + 0.2f, z, r.rect.width - 0.4f, 0.1f), Vector2.zero), new Color(0.13f, 0.13f, 0.16f, 0.16f));
        }
        foreach (var f in layout.furniture)
            if (f.floor == floor && f.height >= 0.1f) rooms.Rect(Px(f.rect, Vector2.zero), new Color(0.13f, 0.13f, 0.16f, 0.09f));

        if (layout.walls.Count > 0)
        {
            foreach (var w in layout.walls)
            {
                if (w.floor != floor) continue;
                switch (w.type)
                {
                    case HouseLayout.WallType.Wall: Line(w.a, w.b, WallColor, 3f); break;
                    case HouseLayout.WallType.Door:
                        Line(w.a, Vector2.Lerp(w.a, w.b, 0.25f), WallColor, 3f);
                        Line(Vector2.Lerp(w.a, w.b, 0.75f), w.b, WallColor, 3f);
                        break;
                    case HouseLayout.WallType.Window:
                        Line(w.a, w.b, WallColor, 3f);
                        Line(Vector2.Lerp(w.a, w.b, 0.2f), Vector2.Lerp(w.a, w.b, 0.8f), Glass, 1.6f);
                        break;
                    case HouseLayout.WallType.Railing: Line(w.a, w.b, RailColor, 1.5f); break;
                }
            }
        }
        else
        {
            // Older layout without wall data: outline the rooms.
            foreach (var r in layout.rooms)
            {
                if (r.floor != floor || r.kind == "Outdoor") continue;
                var q = r.rect;
                Line(new Vector2(q.xMin, q.yMin), new Vector2(q.xMax, q.yMin), WallColor, 2.5f);
                Line(new Vector2(q.xMax, q.yMin), new Vector2(q.xMax, q.yMax), WallColor, 2.5f);
                Line(new Vector2(q.xMax, q.yMax), new Vector2(q.xMin, q.yMax), WallColor, 2.5f);
                Line(new Vector2(q.xMin, q.yMax), new Vector2(q.xMin, q.yMin), WallColor, 2.5f);
            }
        }
        rooms.Apply();

        roomText.text = current != null ? current.name.ToUpperInvariant() : "";
        if (!newFloor) return;

        // Faint room names (halls and stairs don't need one).
        foreach (var l in roomLabels) Destroy(l.gameObject);
        roomLabels.Clear();
        foreach (var r in layout.rooms)
        {
            if (r.floor != floor || r.kind == "Stairs" || r.kind == "Hall") continue;
            var label = Text(r.name, world, 10, new Color(0.13f, 0.13f, 0.16f, 0.45f), font, r.rect.center * k, new Vector2(r.rect.width * k - 6, r.rect.height * k - 6));
            label.text = r.name.ToUpperInvariant();
            label.enableWordWrapping = true;
            roomLabels.Add(label);
        }
    }

    Rect Px(Rect plan, Vector2 offset) => new Rect(plan.x * k + offset.x, plan.y * k + offset.y, plan.width * k, plan.height * k);

    void Line(Vector2 a, Vector2 b, Color c, float width)
    {
        Vector2 pa = a * k, pb = b * k, d = (pb - pa).normalized, n = new Vector2(-d.y, d.x) * (width * 0.5f);
        pa -= d * (width * 0.5f); // square ends, so corners meet
        pb += d * (width * 0.5f);
        rooms.Add(pa - n, pb - n, pb + n, pa + n, c);
    }

    // ------------------------------------------------------------------ tasks

    void CollectTasks()
    {
        entries.Clear();
        for (int i = 0; i < perFloor.Length; i++) perFloor[i] = 0;
        var gm = GameManager.Instance;
        if (!gm) return;

        var carry = PlayerCarry.Instance;
        int number = 0;
        foreach (var task in gm.GetHudTasks())
        {
            if (!task) continue;
            number++;
            bool hazard = task is HazardTask;
            entries.Add(new Entry
            {
                number = number,
                world = task.Where,
                color = task.isMainTask ? HomeworkColor : hazard ? HazardColor : TaskColor,
                name = task.taskName,
                pulse = hazard,
            });
            int f = layout.FloorAt(task.Where.y - 0.2f);
            if (f < perFloor.Length) perFloor[f]++;

            // Dirty clothes lying around: pin the nearest piece.
            if (task is LaundryTask laundry && laundry.PiecesLeft > 0)
            {
                DirtyClothesPiece nearest = null;
                float best = float.MaxValue;
                foreach (var piece in laundry.Pieces)
                {
                    if (!piece) continue;
                    float d = (piece.transform.position - player.position).sqrMagnitude;
                    if (d < best) { best = d; nearest = piece; }
                }
                if (nearest) entries.Add(new Entry { number = number, world = nearest.transform.position, color = ToolColor, name = $"dirty clothes ({laundry.PiecesLeft} left)", tool = true });
                continue;
            }

            var need = Needs(task);
            if (need == null) continue;
            foreach (var item in items)
            {
                if (!item || item.type != need.Value || !item.gameObject.activeInHierarchy) continue;
                if (carry && carry.Held == item) break; // already holding it
                entries.Add(new Entry { number = number, world = item.transform.position, color = ToolColor, name = item.displayName, tool = true });
                break;
            }
        }

        // Holding Tab: every key item, not just the ones a task needs right now.
        if (ItemFinder.Showing)
        {
            foreach (var item in items)
            {
                if (!item || !item.gameObject.activeInHierarchy || (carry && carry.Held == item)) continue;
                if (entries.Exists(e => e.tool && e.name == item.displayName)) continue;
                entries.Add(new Entry
                {
                    badge = item.displayName.Substring(0, 1).ToUpperInvariant(),
                    world = item.transform.position, color = ToolColor, name = item.displayName, tool = true,
                });
            }
        }

        // Mom, once she's home and inspecting.
        var day = DayManager.Instance;
        if (day && day.Mom)
            entries.Insert(0, new Entry { badge = "M", world = day.Mom.transform.position, color = MomColor, name = "Mom is checking", pulse = true });
    }

    ItemType? Needs(TaskBase task) => task switch
    {
        TvTask tv when tv.needsRemote => ItemType.Remote,
        FireHazard _ => ItemType.Extinguisher,
        FloodHazard _ => ItemType.Mop,
        LaundryTask laundry when laundry.Progress <= 0f => ItemType.DirtyClothes,
        CeilingLeakTask leak when !BucketUnder(leak) => ItemType.Bucket,
        _ => null,
    };

    bool BucketUnder(CeilingLeakTask leak)
    {
        foreach (var item in items)
        {
            if (!item || item.type != ItemType.Bucket || item.IsHeld) continue;
            Vector3 d = item.transform.position - leak.transform.position;
            d.y = 0f;
            if (d.magnitude < 0.8f * layout.scale) return true;
        }
        return false;
    }

    void UpdatePins(Vector2 me)
    {
        while (pinPool.Count < entries.Count) pinPool.Add(NewPin());
        // Tools first, so the task pins draw on top of them.
        int slot = 0;
        for (int pass = 0; pass < 2; pass++)
            foreach (var e in entries)
                if (e.tool == (pass == 0)) ShowPin(pinPool[slot++], e, me);
        for (int i = slot; i < pinPool.Count; i++)
            if (pinPool[i].root.gameObject.activeSelf) pinPool[i].root.gameObject.SetActive(false);
    }

    void ShowPin(Pin pin, Entry e, Vector2 me)
    {
        if (!pin.root.gameObject.activeSelf) pin.root.gameObject.SetActive(true);
        pin.root.SetAsLastSibling();
        int floor = layout.FloorAt(e.world.y - 0.2f);
        Vector2 offset = (layout.ToPlan(e.world) - me) * k;
        float edge = radius - 10f;
        bool onRim = offset.magnitude > edge;
        if (onRim) offset = offset.normalized * (radius - 1f);
        pin.root.anchoredPosition = offset;

        float size = e.tool ? 15f : 21f;
        pin.edge.rectTransform.sizeDelta = Vector2.one * (size + 4f);
        pin.body.rectTransform.sizeDelta = Vector2.one * size;
        pin.body.color = e.color;
        pin.number.text = e.badge ?? e.number.ToString();
        pin.number.fontSize = e.tool ? 9f : 12f;

        pin.pointer.gameObject.SetActive(onRim);
        if (onRim)
        {
            Vector2 dir = offset.normalized;
            pin.pointer.color = e.color;
            pin.pointer.rectTransform.anchoredPosition = dir * (size * 0.5f + 5f);
            pin.pointer.rectTransform.localRotation = Quaternion.Euler(0, 0, -Mathf.Atan2(dir.x, dir.y) * Mathf.Rad2Deg);
        }

        bool otherFloor = floor != shownFloor;
        pin.fade.alpha = otherFloor ? 0.72f : 1f;
        pin.edge.color = otherFloor ? Color.white : Ink;
        pin.upDown.gameObject.SetActive(otherFloor);
        if (otherFloor)
        {
            bool up = floor > shownFloor;
            pin.upDown.rectTransform.anchoredPosition = new Vector2(0, (up ? 1 : -1) * (size * 0.5f + 7f));
            pin.upDown.rectTransform.localRotation = Quaternion.Euler(0, 0, up ? 0 : 180);
        }
        float s = e.pulse ? 1f + Mathf.Sin(Time.unscaledTime * 7f) * 0.12f : 1f;
        pin.root.localScale = new Vector3(s, s, 1f);
    }

    Pin NewPin()
    {
        var pin = new Pin { root = UIFactory.Rect("Pin", pinLayer) };
        UIFactory.Place(pin.root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        pin.fade = pin.root.gameObject.AddComponent<CanvasGroup>();
        pin.pointer = Shape("Points this way", pin.root, HudArt.Get("arrow"), Color.white, Vector2.zero, new Vector2(13, 13));
        Centre(pin.pointer.rectTransform);
        pin.edge = Shape("Edge", pin.root, Small(), Ink, Vector2.zero, new Vector2(25, 25));
        Centre(pin.edge.rectTransform);
        pin.body = Shape("Body", pin.root, Small(), Color.white, Vector2.zero, new Vector2(21, 21));
        Centre(pin.body.rectTransform);
        pin.number = Text("Number", pin.root, 12, Color.white, null, Vector2.zero, new Vector2(24, 20));
        pin.number.fontStyle = FontStyles.Bold;
        pin.upDown = Shape("Up or down", pin.root, HudArt.Get("arrow"), Ink, Vector2.zero, new Vector2(10, 10));
        Centre(pin.upDown.rectTransform);
        return pin;
    }

    // ------------------------------------------------------------------ floor pills and the list

    void UpdatePills()
    {
        for (int f = 0; f < pills.Count; f++)
        {
            var pill = pills[f];
            bool mine = f == shownFloor;
            pill.outline.color = mine ? Ink : InkFaint;
            pill.fill.color = mine ? PillOn : Paper;
            pill.text.color = mine ? Ink : InkFaint;
            pill.text.fontStyle = mine ? FontStyles.Bold : FontStyles.Normal;
            int n = f < perFloor.Length ? perFloor[f] : 0;
            pill.badge.gameObject.SetActive(n > 0);
            pill.count.text = n.ToString();
        }
    }

    void UpdateRows()
    {
        float y = 0f;
        int used = 0;
        foreach (var e in entries)
        {
            if (used >= rowPool.Count) rowPool.Add(NewRow());
            var row = rowPool[used++];
            if (!row.root.gameObject.activeSelf) row.root.gameObject.SetActive(true);

            float h = e.tool ? ToolRowHeight : RowHeight;
            row.root.anchoredPosition = new Vector2(0, -y - h * 0.5f);
            y += h;

            int floor = layout.FloorAt(e.world.y - 0.2f);
            var room = layout.RoomAt(e.world, floor);
            string where = room != null ? room.name.ToLowerInvariant() : FloorName(floor).ToLowerInvariant();

            float indent = e.tool ? 20f : 0f;
            float size = e.tool ? 14f : 18f;
            row.badge.color = e.color;
            row.badge.rectTransform.sizeDelta = Vector2.one * size;
            row.badge.rectTransform.anchoredPosition = new Vector2(indent + 10f, 0);
            row.number.text = e.badge ?? e.number.ToString();
            row.number.fontSize = e.tool ? 9f : 11f;
            row.number.rectTransform.anchoredPosition = new Vector2(indent + 10f, 0);
            row.name.text = e.tool ? "grab the " + e.name.ToLowerInvariant() : e.name.ToUpperInvariant();
            row.name.fontSizeMax = e.tool ? 12f : 13f;
            row.name.fontStyle = e.tool ? FontStyles.Normal : FontStyles.Bold;
            row.name.color = e.tool ? ToolColor : Ink;
            row.name.rectTransform.offsetMin = new Vector2(indent + 26f, row.name.rectTransform.offsetMin.y);
            row.where.text = where;

            bool otherFloor = floor != shownFloor;
            row.upDown.gameObject.SetActive(otherFloor);
            row.where.rectTransform.offsetMax = new Vector2(otherFloor ? -14f : 0f, row.where.rectTransform.offsetMax.y);
            if (otherFloor) row.upDown.rectTransform.localRotation = Quaternion.Euler(0, 0, floor > shownFloor ? 0 : 180);
        }
        for (int i = used; i < rowPool.Count; i++)
            if (rowPool[i].root.gameObject.activeSelf) rowPool[i].root.gameObject.SetActive(false);

        float height = listTop + (used > 0 ? y + 18f : 4f);
        if (Mathf.Abs(card.sizeDelta.y - height) > 0.5f) card.sizeDelta = new Vector2(CardWidth, height);
    }

    Row NewRow()
    {
        var row = new Row { root = UIFactory.Rect("Row", rowLayer) };
        row.root.anchorMin = new Vector2(0, 1);
        row.root.anchorMax = new Vector2(1, 1);
        row.root.pivot = new Vector2(0.5f, 0.5f);
        row.root.sizeDelta = new Vector2(0, RowHeight);

        row.badge = UIFactory.Panel("Badge", row.root, Color.white);
        row.badge.sprite = Small();
        UIFactory.Place(row.badge.rectTransform, new Vector2(0, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(10, 0), new Vector2(18, 18));
        row.number = UIFactory.Text("Number", row.root, 11, Color.white, TextAlignmentOptions.Center);
        row.number.fontStyle = FontStyles.Bold;
        row.number.enableWordWrapping = false;
        UIFactory.Place(row.number.rectTransform, new Vector2(0, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(10, 0), new Vector2(20, 18));

        row.name = UIFactory.Text("Task", row.root, 13, Ink, TextAlignmentOptions.MidlineLeft, font);
        row.name.enableWordWrapping = false;
        row.name.enableAutoSizing = true; // long names shrink a little instead of being cut
        row.name.fontSizeMin = 9f;
        row.name.overflowMode = TextOverflowModes.Ellipsis;
        UIFactory.Fill(row.name.rectTransform, 26, 100, 0, 0);

        row.where = UIFactory.Text("Where", row.root, 12, new Color(0.13f, 0.13f, 0.16f, 0.62f), TextAlignmentOptions.MidlineRight, font);
        row.where.enableWordWrapping = false;
        UIFactory.Fill(row.where.rectTransform, 172, 0, 0, 0);
        row.where.enableAutoSizing = true;
        row.where.fontSizeMin = 9f;
        row.where.fontSizeMax = 12f;

        row.upDown = UIFactory.Panel("Up or down", row.root, Ink);
        row.upDown.sprite = HudArt.Get("arrow");
        UIFactory.Place(row.upDown.rectTransform, new Vector2(1, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-5, 0), new Vector2(10, 10));
        return row;
    }

    // ------------------------------------------------------------------ helpers

    static string FloorName(int f) => f == 0 ? "GROUND" : f == 1 ? "FIRST" : f == 2 ? "TOP" : "FLOOR " + f;

    static Color KindColor(string kind) => kind switch
    {
        "Living" => new Color32(207, 230, 216, 255),
        "Bedroom" => new Color32(220, 213, 240, 255),
        "Bath" => new Color32(205, 227, 243, 255),
        "Service" => new Color32(230, 225, 214, 255),
        "Hall" => new Color32(243, 239, 230, 255),
        "Stairs" => new Color32(220, 214, 203, 255),
        "Garage" => new Color32(221, 221, 221, 255),
        "Outdoor" => new Color32(215, 234, 191, 255),
        "Fun" => new Color32(246, 220, 192, 255),
        _ => Color.white,
    };

    MapMesh Mesh(string name, RectTransform parent)
    {
        var rt = UIFactory.Rect(name, parent);
        UIFactory.Place(rt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        var m = rt.gameObject.AddComponent<MapMesh>();
        m.raycastTarget = false;
        return m;
    }

    /// <summary>An image placed at the top centre of the card (y down from the top), or centred in a parent.</summary>
    static Image Shape(string name, Transform parent, Sprite sprite, Color color, Vector2 at, Vector2 size, bool sliced = false)
    {
        var img = UIFactory.Panel(name, parent, color);
        img.sprite = sprite;
        if (sliced) img.type = Image.Type.Sliced;
        UIFactory.Place(img.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), at, size);
        return img;
    }

    static void Centre(RectTransform rt)
    {
        Vector2 at = rt.anchoredPosition;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = at;
    }

    TextMeshProUGUI Text(string name, Transform parent, float size, Color color, TMP_FontAsset withFont, Vector2 at, Vector2 box)
    {
        var t = UIFactory.Text(name, parent, size, color, TextAlignmentOptions.Center, withFont);
        t.enableWordWrapping = false;
        bool inCard = parent == card;
        UIFactory.Place(t.rectTransform, inCard ? new Vector2(0.5f, 1f) : new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), at, box);
        return t;
    }

    // Sprites made in code: a small and a big soft-edged disc, and a ring for the map's rim.
    static Sprite Small() => small ? small : small = Disc(32, 0f);
    static Sprite Big() => big ? big : big = Disc(256, 0f);
    static Sprite Ring() => ring ? ring : ring = Disc(256, 0.965f); // about a 4px line at map size

    static Sprite Disc(int size, float hole)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        var px = new Color32[size * size];
        float r = size * 0.5f - 0.5f, inner = r * hole;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(size * 0.5f, size * 0.5f));
            float a = Mathf.Clamp01(r - d + 0.5f);
            if (hole > 0f) a *= Mathf.Clamp01(d - inner + 0.5f);
            px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
        }
        tex.SetPixels32(px);
        tex.Apply();
        float border = size * 0.5f - 1f;
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(border, border, border, border));
    }
}
