using System.Collections.Generic;
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The PC's "sketchbook" look, applied on top of everything inside the PC screen (desktop, Browser, apps):
///  - every text uses the game's handwriting font and auto-shrinks to fit its box (no spilling / overlapping text);
///  - dark neon panels become paper, light text becomes ink, neon accents become marker colours;
///  - bigger panels get a wobbly hand-inked outline; soft "glow" blobs are removed.
/// It keeps watching, so colours set later by the apps (hover, live numbers, new list rows) are converted too.
/// </summary>
public class PcSketch : MonoBehaviour
{
    public static readonly Color Ink = new Color32(38, 34, 46, 255);

    /// <summary>The plain round sprite of the desktop (soft glows use it, unsliced).</summary>
    public Sprite roundSprite;
    public TMP_FontAsset font;

    class Seen
    {
        public Color mapped;
        public string mappedText;
    }

    readonly Dictionary<Graphic, Seen> seen = new Dictionary<Graphic, Seen>();
    readonly List<Graphic> scratch = new List<Graphic>();
    readonly List<Graphic> dead = new List<Graphic>();
    float nextScan;

    void LateUpdate()
    {
        if (Time.unscaledTime >= nextScan)
        {
            nextScan = Time.unscaledTime + 0.25f;
            Scan();
        }

        foreach (var kv in seen)
        {
            var g = kv.Key;
            if (!g) { dead.Add(g); continue; }
            if (!g.isActiveAndEnabled) continue;
            Restyle(g, kv.Value);
        }
        if (dead.Count > 0)
        {
            foreach (var g in dead) seen.Remove(g);
            dead.Clear();
        }
    }

    /// <summary>Finds graphics we haven't styled yet (apps build new rows all the time).</summary>
    public void Scan()
    {
        GetComponentsInChildren(true, scratch);
        foreach (var g in scratch)
        {
            if (seen.ContainsKey(g)) continue;
            if (g is RawImage) continue; // wallpaper, replay cameras
            if (g.GetComponentInParent<PcSketchKeep>(true)) continue; // product art keeps its own colours
            var s = new Seen { mapped = new Color(-1, -1, -1, -1) };
            seen.Add(g, s);
            FirstLook(g);
            Restyle(g, s);
        }
    }

    void FirstLook(Graphic g)
    {
        if (g is TextMeshProUGUI t)
        {
            if (t.name == "Shadow") { t.enabled = false; return; }
            if (font) t.font = font;
            float max = t.fontSize;
            t.enableAutoSizing = true;
            t.fontSizeMax = max;
            t.fontSizeMin = Mathf.Max(9f, max * 0.6f);
            t.characterSpacing = Mathf.Min(t.characterSpacing, 2f);
            if (!t.enableWordWrapping && t.overflowMode == TextOverflowModes.Overflow) t.overflowMode = TextOverflowModes.Ellipsis;
            return;
        }

        if (g is Image img)
        {
            var r = img.rectTransform.rect;
            bool round = roundSprite && img.sprite == roundSprite;
            // Soft glow blobs = the "AI neon" look. Gone.
            if (round && img.type != Image.Type.Sliced && Mathf.Min(r.width, r.height) > 150f && img.color.a < 0.6f)
            {
                img.enabled = false;
                return;
            }
            bool plain = img.sprite == null || (round && img.type == Image.Type.Sliced);
            bool rotated = Mathf.Abs(img.rectTransform.localEulerAngles.z) > 0.5f;
            if (plain && !rotated && r.width >= 120f && r.height >= 80f && img.color.a >= 0.6f && !InsideSketched(img.transform))
            {
                img.sprite = Box(Mathf.Abs(img.GetInstanceID()) % 3);
                img.type = Image.Type.Tiled;
                img.fillCenter = true;
            }
        }
    }

    /// <summary>Only the outer card gets an inked edge; boxes inside it stay flat (less clutter).</summary>
    static bool InsideSketched(Transform t)
    {
        var p = t.parent;
        for (int i = 0; i < 5 && p; i++, p = p.parent)
        {
            var im = p.GetComponent<Image>();
            if (im && im.sprite && im.sprite.name == "Sketch box") return true;
        }
        return false;
    }

    void Restyle(Graphic g, Seen s)
    {
        if (g is TextMeshProUGUI t)
        {
            if (!ReferenceEquals(t.text, s.mappedText))
            {
                string m = MapRichText(t.text);
                if (m != t.text) t.text = m;
                s.mappedText = t.text;
            }
            if (t.color != s.mapped)
            {
                var c = MapText(t.color);
                t.color = c;
                s.mapped = c;
            }
            return;
        }
        if (g.color != s.mapped)
        {
            var c = MapFill(g.color);
            g.color = c;
            s.mapped = c;
        }
    }

    // ------------------------------------------------------------------ colours

    static bool IsInk(Color c) => Mathf.Abs(c.r - Ink.r) < 0.01f && Mathf.Abs(c.g - Ink.g) < 0.01f && Mathf.Abs(c.b - Ink.b) < 0.01f;

    static Color Paper(float p, float a) => new Color(p, p * 0.985f, p * 0.935f, a);

    static Color WithA(Color c, float a) { c.a = a; return c; }

    /// <summary>Panels, shapes, bars.</summary>
    public static Color MapFill(Color c)
    {
        if (IsInk(c)) return c;
        float a = c.a;
        float lum = 0.299f * c.r + 0.587f * c.g + 0.114f * c.b;
        Color.RGBToHSV(c, out float h, out float s, out float v);
        if (s < 0.22f || v < 0.12f)
        {
            if (lum < 0.4f)
            {
                if (a < 0.7f) return WithA(Ink, a * 0.45f);           // shadows / dimmers
                return Paper(0.965f - lum * 0.28f, a);                 // dark panel -> paper (darker = brighter sheet)
            }
            if (lum > 0.75f && a < 0.6f) return WithA(Ink, a * 0.55f); // white hover overlays -> pencil shading
            if (lum > 0.75f) return Paper(Mathf.Lerp(0.9f, 0.985f, (lum - 0.75f) / 0.25f), a);
            return c;
        }
        if (v < 0.35f) return WithA(Color.HSVToRGB(h, 0.2f, 0.94f - v * 0.25f), a); // dark coloured page -> tinted paper
        if (s < 0.45f && v > 0.85f) return c;                           // already pastel
        return WithA(Color.HSVToRGB(h, Mathf.Min(s, 0.85f), Mathf.Min(v, 0.92f)), a); // neon -> marker
    }

    /// <summary>Text: everything becomes ink, bright coloured text becomes a readable marker colour.</summary>
    public static Color MapText(Color c)
    {
        Color.RGBToHSV(c, out float h, out float s, out float v);
        if (s < 0.25f || v < 0.15f) return WithA(Ink, c.a);
        float lum = 0.299f * c.r + 0.587f * c.g + 0.114f * c.b;
        if (lum > 0.48f) return WithA(Color.HSVToRGB(h, Mathf.Max(s, 0.65f), 0.55f), c.a);
        return c;
    }

    static readonly Regex ColorTag = new Regex("<color=(#[0-9A-Fa-f]{6,8}|white|black|grey|gray)>", RegexOptions.Compiled);

    static string MapRichText(string text)
    {
        if (string.IsNullOrEmpty(text) || text.IndexOf("<color", System.StringComparison.OrdinalIgnoreCase) < 0) return text;
        return ColorTag.Replace(text, m =>
        {
            if (!ColorUtility.TryParseHtmlString(m.Groups[1].Value, out var c)) return m.Value;
            var o = MapText(c);
            return "<color=#" + ColorUtility.ToHtmlStringRGBA(o) + ">";
        });
    }

    // ------------------------------------------------------------------ art

    static readonly Sprite[] boxes = new Sprite[3];
    static Texture2D paper;

    /// <summary>A paper card with a wobbly hand-inked outline (tiled, so long edges keep wobbling).</summary>
    public static Sprite Box(int variant)
    {
        if (boxes[variant]) return boxes[variant];
        const int size = 160, border = 24;
        const float tile = size - border * 2; // 112: wobble repeats every tile, so tiles join seamlessly
        var rng = new System.Random(31 + variant * 17);
        float[] ph = new float[8];
        for (int i = 0; i < ph.Length; i++) ph[i] = (float)rng.NextDouble() * Mathf.PI * 2f;

        float Wob(float u, int edge)
        {
            float k = Mathf.PI * 2f / tile;
            return 1.1f * Mathf.Sin(u * k + ph[edge]) + 0.55f * Mathf.Sin(u * k * 3f + ph[edge + 4]);
        }

        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "Sketch box", wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[size * size];
        float c = size * 0.5f;
        const float radius = 12f;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float fx = x + 0.5f, fy = y + 0.5f;
            float dx = Mathf.Abs(fx - c), dy = Mathf.Abs(fy - c);
            int edge = dx > dy ? (fx < c ? 0 : 1) : (fy < c ? 2 : 3);
            float along = dx > dy ? fy - border : fx - border;
            float inset = 4f + Wob(along, edge);
            float half = c - inset;
            float qx = Mathf.Max(dx - (half - radius), 0f), qy = Mathf.Max(dy - (half - radius), 0f);
            float d = Mathf.Sqrt(qx * qx + qy * qy) + Mathf.Min(Mathf.Max(dx - (half - radius), dy - (half - radius)), 0f) - radius;
            float thick = 2.4f + 0.5f * Mathf.Sin(along * 0.11f + ph[edge]);
            Color32 col;
            if (d > 0f) col = new Color32(255, 255, 255, 0);
            else if (d > -thick) col = new Color32(72, 66, 80, 255);
            else col = new Color32(255, 255, 255, 255);
            // soft edge
            float aa = Mathf.Clamp01(0.5f - d);
            if (d > -0.5f) col.a = (byte)(aa * 255f);
            if (d < -thick && d > -thick - 1f)
            {
                float t = Mathf.Clamp01(-d - thick);
                col = Color32.Lerp(new Color32(72, 66, 80, 255), new Color32(255, 255, 255, 255), t);
            }
            px[y * size + x] = col;
        }
        tex.SetPixels32(px);
        tex.Apply();
        boxes[variant] = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect,
            new Vector4(border, border, border, border));
        boxes[variant].name = "Sketch box";
        return boxes[variant];
    }

    /// <summary>Notebook paper: cream sheet, blue rules, a red margin, a coffee ring and a few doodles.</summary>
    public static Texture2D PaperWallpaper
    {
        get
        {
            if (paper) return paper;
            const int w = 960, h = 540;
            paper = new Texture2D(w, h, TextureFormat.RGBA32, false) { name = "Paper wallpaper", wrapMode = TextureWrapMode.Clamp };
            var px = new Color[w * h];
            var sheet = new Color(0.97f, 0.955f, 0.9f);
            var rule = new Color(0.55f, 0.72f, 0.92f);
            var margin = new Color(0.9f, 0.42f, 0.42f);
            var ink = new Color(0.25f, 0.24f, 0.32f);
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float n = Mathf.PerlinNoise(x * 0.05f, y * 0.05f) * 0.025f + Mathf.PerlinNoise(x * 0.4f, y * 0.4f) * 0.015f;
                var col = sheet * (1f - n);
                int fromTop = h - 1 - y;
                if (fromTop > 40 && fromTop % 22 == 0) col = Color.Lerp(col, rule, 0.45f);
                if (x >= 82 && x <= 83) col = Color.Lerp(col, margin, 0.6f);
                // coffee ring (bottom right)
                float r = Vector2.Distance(new Vector2(x, y), new Vector2(w - 170, 150));
                float ring = Mathf.Abs(r - 58f + 2f * Mathf.Sin(Mathf.Atan2(y - 150, x - (w - 170)) * 3f));
                if (ring < 3f) col = Color.Lerp(col, new Color(0.62f, 0.45f, 0.28f), 0.22f * (1f - ring / 3f));
                col.a = 1f;
                px[y * w + x] = col;
            }
            // doodles: a star and a little cat face, in pen
            void Line(Vector2 a, Vector2 b, float t)
            {
                int steps = Mathf.CeilToInt(Vector2.Distance(a, b) * 2f);
                for (int i = 0; i <= steps; i++)
                {
                    var p = Vector2.Lerp(a, b, i / (float)steps);
                    for (int oy = -2; oy <= 2; oy++)
                    for (int ox = -2; ox <= 2; ox++)
                    {
                        int xx = Mathf.RoundToInt(p.x) + ox, yy = Mathf.RoundToInt(p.y) + oy;
                        if (xx < 0 || yy < 0 || xx >= w || yy >= h) continue;
                        float d = new Vector2(ox, oy).magnitude;
                        if (d > t) continue;
                        px[yy * w + xx] = Color.Lerp(px[yy * w + xx], ink, 0.7f * Mathf.Clamp01(t - d + 0.5f));
                    }
                }
            }
            var star = new Vector2(w - 120, h - 90);
            for (int i = 0; i < 5; i++)
            {
                float a0 = (90 + i * 144) * Mathf.Deg2Rad, a1 = (90 + (i + 1) * 144) * Mathf.Deg2Rad;
                Line(star + new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * 34f, star + new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * 34f, 1.4f);
            }
            var cat = new Vector2(w - 300, 120);
            Vector2 prev = cat + new Vector2(36, 0);
            for (int i = 1; i <= 40; i++)
            {
                float a = i / 40f * Mathf.PI * 2f;
                var p = cat + new Vector2(Mathf.Cos(a) * 36f, Mathf.Sin(a) * 30f);
                Line(prev, p, 1.4f);
                prev = p;
            }
            Line(cat + new Vector2(-30, 16), cat + new Vector2(-26, 50), 1.4f);
            Line(cat + new Vector2(-26, 50), cat + new Vector2(-8, 28), 1.4f);
            Line(cat + new Vector2(30, 16), cat + new Vector2(26, 50), 1.4f);
            Line(cat + new Vector2(26, 50), cat + new Vector2(8, 28), 1.4f);
            Line(cat + new Vector2(-14, 6), cat + new Vector2(-10, 6), 2f);
            Line(cat + new Vector2(10, 6), cat + new Vector2(14, 6), 2f);
            Line(cat + new Vector2(-4, -8), cat + new Vector2(4, -8), 1.4f);
            for (int s = -1; s <= 1; s += 2)
            {
                Line(cat + new Vector2(s * 18, -6), cat + new Vector2(s * 52, -2), 1f);
                Line(cat + new Vector2(s * 18, -10), cat + new Vector2(s * 52, -14), 1f);
            }
            paper.SetPixels(px);
            paper.Apply();
            return paper;
        }
    }
}

/// <summary>Put on a UI object to keep it (and its children) out of the PC's sketch recolouring.</summary>
public class PcSketchKeep : MonoBehaviour { }
