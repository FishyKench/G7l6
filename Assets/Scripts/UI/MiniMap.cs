using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Hand-drawn minimap (top left): the floor you're on, its rooms, you as a red arrow, and the
/// room you're in highlighted and named. Press M to hide/show it.
/// Needs a HouseLayout in the scene (made by G7l6 > Build New House).
/// </summary>
public class MiniMap : MonoBehaviour
{
    public KeyCode toggleKey = KeyCode.M;
    public float mapWidth = 250f;

    static readonly Color Outline = new Color32(34, 32, 42, 255);
    static readonly Color Here = new Color32(255, 222, 92, 255);

    HouseLayout layout;
    Transform player;
    CanvasGroup group;
    RectTransform mapArea, arrow;
    TextMeshProUGUI floorText, roomText;
    readonly List<(HouseLayout.RoomInfo info, Image fill)> drawn = new List<(HouseLayout.RoomInfo, Image)>();
    int shownFloor = -1;
    HouseLayout.RoomInfo current;
    Rect plan;
    float pixelsPerMetre, targetAlpha = 1f;
    TMP_FontAsset font;

    void Start()
    {
        layout = HouseLayout.Instance;
        var movement = FindObjectOfType<PlayerMovementAdvanced>();
        if (movement) player = movement.transform;
        if (!layout || !player) { enabled = false; return; }
        font = GameHUD.Instance ? GameHUD.Instance.font : null;
        plan = layout.PlanBounds();
        pixelsPerMetre = mapWidth / Mathf.Max(1f, plan.width);
        Build();
    }

    void Build()
    {
        var canvas = UIFactory.CreateCanvas("Minimap Canvas", 49);
        canvas.transform.SetParent(transform, false);
        float mapHeight = plan.height * pixelsPerMetre;

        var card = UIFactory.Rect("Minimap", canvas.transform);
        UIFactory.Place(card, new Vector2(0, 1), new Vector2(0, 1), new Vector2(26, -24), new Vector2(mapWidth + 40, mapHeight + 104));
        card.localRotation = Quaternion.Euler(0, 0, -1.5f);
        group = card.gameObject.AddComponent<CanvasGroup>();
        var breathe = card.gameObject.AddComponent<HudBreathe>();
        breathe.swayDegrees = 0.6f;
        breathe.breatheAmount = 0.006f;

        Img("Shadow", card, HudArt.Get("note_shadow"), new Color(1, 1, 1, 0.9f), new Vector2(6, -8), new Vector2(mapWidth + 60, mapHeight + 124));
        var paper = Img("Paper", card, HudArt.Get("note_paper"), HudArt.PaperWhite, Vector2.zero, new Vector2(mapWidth + 40, mapHeight + 104));
        var tape = Img("Tape", card, HudArt.Get("tape"), Color.white, new Vector2(0, (mapHeight + 104) * 0.5f - 4), new Vector2(110, 32));
        tape.rectTransform.localRotation = Quaternion.Euler(0, 0, 4f);

        floorText = Label("Floor", card, 26, new Vector2(0, (mapHeight + 104) * 0.5f - 34), new Vector2(mapWidth, 32));
        roomText = Label("Room", card, 20, new Vector2(0, -(mapHeight + 104) * 0.5f + 26), new Vector2(mapWidth, 28));
        roomText.color = HudArt.InkSoft;

        mapArea = UIFactory.Rect("Map", card);
        UIFactory.Place(mapArea, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 4), new Vector2(mapWidth, mapHeight));

        arrow = UIFactory.Rect("You", card);
        var arrowImage = arrow.gameObject.AddComponent<Image>();
        arrowImage.sprite = HudArt.Get("arrow");
        arrowImage.color = HudArt.MarkerRed;
        arrowImage.raycastTarget = false;
        arrow.sizeDelta = new Vector2(20, 20);
        paper.raycastTarget = false;
    }

    void Update()
    {
        if (!layout || !player) return;
        if (Input.GetKeyDown(toggleKey)) targetAlpha = targetAlpha > 0.5f ? 0f : 1f;
        group.alpha = Mathf.MoveTowards(group.alpha, targetAlpha, Time.unscaledDeltaTime * 5f);

        int floor = layout.FloorAt(player.position.y - 1f);
        if (floor != shownFloor) DrawFloor(floor);

        // You are here
        Vector2 p = layout.ToPlan(player.position);
        arrow.anchoredPosition = mapArea.anchoredPosition + ToMap(p);
        float yaw = Camera.main ? Camera.main.transform.eulerAngles.y : player.eulerAngles.y;
        arrow.localRotation = Quaternion.Euler(0, 0, -yaw);
        float pulse = 1f + Mathf.Sin(Time.unscaledTime * 4f) * 0.08f;
        arrow.localScale = new Vector3(pulse, pulse, 1f);

        var room = layout.RoomAt(player.position, floor);
        if (room != current)
        {
            current = room;
            foreach (var (info, fill) in drawn)
                fill.color = info == current ? Here : KindColor(info.kind);
            roomText.text = current != null ? "YOU'RE IN: " + current.name.ToUpperInvariant() : "";
        }
    }

    void DrawFloor(int floor)
    {
        shownFloor = floor;
        current = null;
        foreach (Transform child in mapArea) Destroy(child.gameObject);
        drawn.Clear();
        floorText.text = floor == 0 ? "GROUND FLOOR" : floor == 1 ? "FIRST FLOOR" : floor == 2 ? "TOP FLOOR" : $"FLOOR {floor}";

        foreach (var r in layout.rooms)
        {
            if (r.floor != floor) continue;
            Vector2 min = ToMap(new Vector2(r.rect.xMin, r.rect.yMin));
            Vector2 max = ToMap(new Vector2(r.rect.xMax, r.rect.yMax));
            Vector2 size = max - min, centre = (min + max) * 0.5f;

            var edge = UIFactory.Panel(r.name + " edge", mapArea, Outline);
            UIFactory.Place(edge.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), centre, size + new Vector2(2.5f, 2.5f));
            var fill = UIFactory.Panel(r.name, mapArea, KindColor(r.kind));
            UIFactory.Place(fill.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), centre, size - new Vector2(2.5f, 2.5f));
            drawn.Add((r, fill));

            if (Mathf.Min(r.rect.width, r.rect.height) >= 3.9f && r.kind != "Stairs")
            {
                var label = UIFactory.Text(r.name + " label", mapArea, 11, HudArt.Ink, TextAlignmentOptions.Center, font);
                label.text = r.name.ToUpperInvariant();
                label.enableWordWrapping = true;
                UIFactory.Place(label.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), centre, size - new Vector2(6, 6));
            }
        }
    }

    Vector2 ToMap(Vector2 planPoint) =>
        new Vector2((planPoint.x - plan.center.x) * pixelsPerMetre, (planPoint.y - plan.center.y) * pixelsPerMetre);

    static Color KindColor(string kind) => kind switch
    {
        "Living" => new Color32(198, 228, 214, 255),
        "Bedroom" => new Color32(214, 206, 240, 255),
        "Bath" => new Color32(196, 222, 244, 255),
        "Service" => new Color32(226, 222, 212, 255),
        "Hall" => new Color32(240, 236, 226, 255),
        "Stairs" => new Color32(190, 186, 178, 255),
        "Garage" => new Color32(214, 214, 214, 255),
        "Outdoor" => new Color32(206, 232, 180, 255),
        "Fun" => new Color32(250, 210, 170, 255),
        _ => Color.white,
    };

    Image Img(string name, Transform parent, Sprite sprite, Color color, Vector2 pos, Vector2 size)
    {
        var img = UIFactory.Panel(name, parent, color);
        img.sprite = sprite;
        UIFactory.Place(img.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), pos, size);
        return img;
    }

    TextMeshProUGUI Label(string name, Transform parent, float size, Vector2 pos, Vector2 box)
    {
        var t = UIFactory.Text(name, parent, size, HudArt.Ink, TextAlignmentOptions.Center, font);
        t.enableWordWrapping = false;
        UIFactory.Place(t.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), pos, box);
        return t;
    }
}
