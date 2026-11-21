using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Hold Tab to find things: every key item you can pick up (TV remote, fire extinguisher, mop, bucket,
/// dirty clothes) glows through the walls and gets a label on screen with how far away it is. If it's off
/// screen, the label sits on the edge pointing the way; if it's on another floor, an arrow says up or down.
/// While Tab is held the minimap shows them all too. Created automatically by the GameManager.
/// </summary>
public class ItemFinder : MonoBehaviour
{
    public static ItemFinder Instance { get; private set; }
    /// <summary>True while Tab is held.</summary>
    public static bool Showing => Instance && Instance.showing;

    public KeyCode key = KeyCode.Tab;
    public Color glowColor = new Color32(120, 230, 120, 255);
    public Color labelColor = new Color32(46, 150, 68, 255);

    Carryable[] items = new Carryable[0];
    bool showing;
    RectTransform canvasRect;
    readonly List<Label> labels = new List<Label>();
    TMP_FontAsset font;
    static Sprite round;

    class Label
    {
        public RectTransform root, pill;
        public Image fill, pointer, upDown;
        public TextMeshProUGUI text;
    }

    void Awake() => Instance = this;

    IEnumerator Start()
    {
        items = FindObjectsOfType<Carryable>(true);
        font = GameHUD.Instance ? GameHUD.Instance.font : null;
        var canvas = UIFactory.CreateCanvas("Item Finder Canvas", 48);
        canvas.transform.SetParent(transform, false);
        canvasRect = (RectTransform)canvas.transform;

        yield return new WaitForSeconds(5f);
        GameHUD.Toast("Hold TAB to find the remote, mop, bucket...", GameHUD.Info);
    }

    void Update()
    {
        bool want = !PauseMenu.IsPaused && Input.GetKey(key) && !(DayManager.Instance && DayManager.Instance.State == DayManager.Phase.Report);
        if (want != showing)
        {
            showing = want;
            if (!showing) Hide();
        }
        if (showing) Show();
    }

    // ------------------------------------------------------------------ showing

    void Show()
    {
        var cam = Camera.main;
        if (!cam || !canvasRect) return;
        var carry = PlayerCarry.Instance;
        var layout = HouseLayout.Instance;
        Vector3 me = carry ? carry.transform.position : cam.transform.position;
        int myFloor = layout ? layout.FloorAt(me.y - 1f) : 0;

        int used = 0;
        foreach (var item in items)
        {
            if (!item || !item.gameObject.activeInHierarchy || (carry && carry.Held == item)) continue;
            Glow(item, true);

            if (used >= labels.Count) labels.Add(NewLabel());
            var label = labels[used++];
            label.root.gameObject.SetActive(true);

            Vector3 at = Centre(item) + Vector3.up * 0.35f;
            float metres = Vector3.Distance(me, at);
            int floor = layout ? layout.FloorAt(at.y - 0.3f) : myFloor;
            string name = item.displayName.ToUpperInvariant();
            label.text.text = item.TakenBy ? $"{name}  <size=80%>(the cat has it!)</size>" : $"{name}  <size=80%>{Mathf.RoundToInt(metres)}m</size>";

            bool otherFloor = floor != myFloor;
            label.upDown.gameObject.SetActive(otherFloor);
            if (otherFloor) label.upDown.rectTransform.localRotation = Quaternion.Euler(0, 0, floor > myFloor ? 0f : 180f);
            float textW = label.text.GetPreferredValues(label.text.text).x;
            float arrowW = otherFloor ? 16f : 0f;
            float width = textW + 26f + arrowW;
            label.pill.sizeDelta = new Vector2(width, 34f);
            label.text.rectTransform.anchoredPosition = new Vector2(arrowW * 0.5f, 0f);
            label.upDown.rectTransform.anchoredPosition = new Vector2(-width * 0.5f + 16f, 0f);

            // Where it is on screen; if it's behind you or off the edge, pin it to the edge and point.
            Vector3 sp = cam.WorldToScreenPoint(at);
            bool behind = sp.z < 0f;
            if (behind) sp = new Vector3(Screen.width - sp.x, Screen.height - sp.y, 0f);
            float margin = Mathf.Min(Screen.width, Screen.height) * 0.08f;
            var safe = new Rect(margin, margin, Screen.width - margin * 2f, Screen.height - margin * 2f);
            Vector2 centre = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            Vector2 p = sp;
            bool offScreen = behind || !safe.Contains(p);
            if (offScreen)
            {
                Vector2 d = p - centre;
                if (behind && d.sqrMagnitude < 1f) d = Vector2.down;
                float k = Mathf.Min(safe.width * 0.5f / Mathf.Max(0.001f, Mathf.Abs(d.x)), safe.height * 0.5f / Mathf.Max(0.001f, Mathf.Abs(d.y)));
                p = centre + d * Mathf.Min(1f, k);
                if (behind) p = centre + d * k;
                label.pointer.rectTransform.localRotation = Quaternion.Euler(0, 0, -Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg);
                Vector2 dir = d.normalized;
                label.pointer.rectTransform.anchoredPosition = new Vector2(dir.x * (width * 0.5f + 14f), dir.y * 31f);
            }
            label.pointer.gameObject.SetActive(offScreen);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, p, null, out var local);
            label.root.anchoredPosition = local;

            // Nearer things a bit bigger; everything faded slightly when it's on another floor.
            float s = Mathf.Lerp(1.1f, 0.85f, Mathf.InverseLerp(3f, 25f, metres));
            label.root.localScale = new Vector3(s, s, 1f);
            label.fill.color = otherFloor ? new Color(labelColor.r, labelColor.g, labelColor.b, 0.75f) : labelColor;
        }
        for (int i = used; i < labels.Count; i++)
            if (labels[i].root.gameObject.activeSelf) labels[i].root.gameObject.SetActive(false);
    }

    void Hide()
    {
        foreach (var l in labels) l.root.gameObject.SetActive(false);
        foreach (var item in items) if (item) Glow(item, false);
    }

    void Glow(Carryable item, bool on)
    {
        var outline = item.GetComponent<Outline>();
        if (!outline) return;
        if (on)
        {
            outline.OutlineMode = Outline.Mode.OutlineAll; // through walls
            outline.OutlineColor = glowColor;
            outline.OutlineWidth = 6f;
            outline.enabled = true;
        }
        else
        {
            outline.OutlineMode = Outline.Mode.OutlineVisible;
            outline.OutlineColor = Color.white;
            outline.OutlineWidth = 5f;
            outline.enabled = false;
        }
    }

    static Vector3 Centre(Carryable item)
    {
        bool found = false;
        Bounds b = default;
        foreach (var r in item.GetComponentsInChildren<Renderer>())
        {
            if (!found) { b = r.bounds; found = true; }
            else b.Encapsulate(r.bounds);
        }
        return found ? b.center : item.transform.position;
    }

    // ------------------------------------------------------------------ labels

    Label NewLabel()
    {
        var l = new Label { root = UIFactory.Rect("Item label", canvasRect) };
        UIFactory.Place(l.root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);

        // Arrow on the screen edge pointing at things you can't see.
        l.pointer = UIFactory.Panel("Points this way", l.root, labelColor);
        l.pointer.sprite = HudArt.Get("arrow");
        UIFactory.Place(l.pointer.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(22, 22));

        // The label: a white rim with the green pill inside, the name, and an up/down arrow for other floors.
        var rim = UIFactory.Panel("Label", l.root, new Color(1f, 1f, 1f, 0.95f));
        rim.sprite = Round();
        rim.type = Image.Type.Sliced;
        l.pill = rim.rectTransform;
        UIFactory.Place(l.pill, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(100, 34));
        l.fill = UIFactory.Panel("Fill", l.pill, labelColor);
        l.fill.sprite = Round();
        l.fill.type = Image.Type.Sliced;
        UIFactory.Fill(l.fill.rectTransform, 3, 3, 3, 3);

        l.text = UIFactory.Text("Name", l.pill, 17, Color.white, TextAlignmentOptions.Center, font);
        l.text.fontStyle = FontStyles.Bold;
        l.text.enableWordWrapping = false;
        UIFactory.Place(l.text.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(400, 30));

        l.upDown = UIFactory.Panel("Up or down", l.pill, Color.white);
        l.upDown.sprite = HudArt.Get("arrow");
        UIFactory.Place(l.upDown.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(13, 13));
        return l;
    }

    static Sprite Round()
    {
        if (round) return round;
        const int size = 32;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[size * size];
        float r = size * 0.5f - 0.5f;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(size * 0.5f, size * 0.5f));
            px[y * size + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(r - d + 0.5f) * 255f));
        }
        tex.SetPixels32(px);
        tex.Apply();
        round = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(15, 15, 15, 15));
        return round;
    }
}
