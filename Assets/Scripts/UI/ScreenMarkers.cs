using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Markers on the screen that point at places in the house (a task, the next kitchen station, your PC): a little paper tag with a
/// label and an arrow pointing down at the spot. They show through walls, so you always know which way to go. Whoever wants one
/// calls <see cref="Show"/> every frame; a marker nobody asked for this frame hides. Hidden in VR and while paused.
/// </summary>
public class ScreenMarkers : MonoBehaviour
{
    static ScreenMarkers instance;

    class Marker
    {
        public RectTransform rt;
        public Image tag, arrow;
        public TextMeshProUGUI text, distance;
        public Vector3 world;
        public int frame = -1;
        public bool big;
        public float alpha;
        public string last;
    }

    readonly Dictionary<string, Marker> markers = new Dictionary<string, Marker>();
    RectTransform root;
    Canvas canvas;
    TMP_FontAsset font;

    static ScreenMarkers Get()
    {
        if (instance) return instance;
        var canvas = UIFactory.CreateCanvas("Screen markers", 6);
        instance = canvas.gameObject.AddComponent<ScreenMarkers>();
        instance.root = (RectTransform)canvas.transform;
        instance.canvas = canvas;
        instance.font = GameHUD.Instance ? GameHUD.Instance.font : null;
        return instance;
    }

    /// <summary>Shows (this frame) a marker 'key' at 'world' with 'label'. 'big' = the one that matters most right now.</summary>
    public static void Show(string key, Vector3 world, string label, Color colour, bool big = false)
    {
        if (MomEscort.Active) return; // (no tags while you're following Mom round)
        var self = Get();
        if (!self.markers.TryGetValue(key, out var m)) m = self.markers[key] = self.Make(key);
        m.world = world;
        m.frame = Time.frameCount;
        m.big = big;
        if (m.last != label) { m.text.text = label; m.last = label; }
        m.tag.color = colour;
        m.arrow.color = colour;
    }

    Marker Make(string key)
    {
        var m = new Marker();
        m.rt = UIFactory.Rect("Marker " + key, root);
        m.rt.anchorMin = m.rt.anchorMax = Vector2.zero;
        m.rt.pivot = new Vector2(0.5f, 0f);
        m.rt.sizeDelta = new Vector2(260f, 70f);
        m.arrow = UIFactory.Panel("Arrow", m.rt, Color.white);
        m.arrow.sprite = HudArt.Get("arrow");
        m.arrow.preserveAspect = true;
        m.arrow.raycastTarget = false;
        UIFactory.Place(m.arrow.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), Vector2.zero, new Vector2(26f, 26f));
        m.arrow.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 180f); // pointing down (the sprite points up)
        m.tag = UIFactory.Panel("Tag", m.rt, Color.white);
        m.tag.sprite = HudArt.Get("note_paper");
        m.tag.type = Image.Type.Sliced;
        m.tag.raycastTarget = false;
        UIFactory.Place(m.tag.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 28f), new Vector2(240f, 40f));
        m.text = UIFactory.Text("Label", m.tag.transform, 19, HudArt.Ink, TextAlignmentOptions.Center, font);
        m.text.enableWordWrapping = false;
        m.text.fontStyle = FontStyles.Bold;
        UIFactory.Fill(m.text.rectTransform, 10, 10, 2, 2);
        m.distance = UIFactory.Text("Distance", m.rt, 14, new Color(1f, 1f, 1f, 0.85f), TextAlignmentOptions.Center, font);
        UIFactory.Place(m.distance.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 1f), new Vector2(0f, 0f), new Vector2(120f, 18f));
        m.distance.outlineWidth = 0.25f;
        m.distance.outlineColor = new Color32(0, 0, 0, 200);
        return m;
    }

    void LateUpdate()
    {
        var cam = Camera.main;
        bool hidden = !cam || VRHeadset.Worn || PauseMenu.IsPaused || Onboarding.Showing || (PlayerInteraction.Instance && PlayerInteraction.Instance.InMainTask);
        float scale = HouseLayout.Instance ? HouseLayout.Instance.scale : 1f;
        foreach (var m in markers.Values)
        {
            bool want = !hidden && m.frame >= Time.frameCount - 1;
            Vector3 sp = want ? cam.WorldToScreenPoint(m.world) : Vector3.zero;
            if (sp.z <= 0.1f) want = false;
            m.alpha = Mathf.MoveTowards(m.alpha, want ? 1f : 0f, Time.unscaledDeltaTime * 5f);
            bool on = m.alpha > 0.01f;
            if (m.rt.gameObject.activeSelf != on) m.rt.gameObject.SetActive(on);
            if (!on || sp.z <= 0.1f) continue;
            Vector2 pos = new Vector2(sp.x, sp.y) / Mathf.Max(0.01f, canvas.scaleFactor); // (anchored at the bottom left)
            float bob = Mathf.Sin(Time.unscaledTime * 3f + m.world.x) * 4f;
            m.rt.anchoredPosition = pos + new Vector2(0f, 6f + bob);
            float size = m.big ? 1.15f : 0.8f;
            m.rt.localScale = Vector3.one * size;
            // width to fit the label
            float w = Mathf.Clamp(m.text.preferredWidth + 28f, 90f, 520f);
            m.tag.rectTransform.sizeDelta = new Vector2(w, 40f);
            float dist = Vector3.Distance(cam.transform.position, m.world) / Mathf.Max(0.01f, scale);
            m.distance.text = dist > 3f ? $"{Mathf.RoundToInt(dist)} m" : "";
            var c = m.tag.color; m.tag.color = new Color(c.r, c.g, c.b, m.alpha * (m.big ? 1f : 0.85f));
            c = m.arrow.color; m.arrow.color = new Color(c.r, c.g, c.b, m.alpha);
            m.text.alpha = m.alpha;
            m.distance.alpha = m.alpha * 0.9f;
        }
    }
}
