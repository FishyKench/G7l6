using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Small helpers to build UI from code (so the HUD doesn't need to be wired up in every scene).</summary>
public static class UIFactory
{
    public static Canvas CreateCanvas(string name, int sortingOrder)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        var canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = sortingOrder;

        var scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        return canvas;
    }

    public static RectTransform Rect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = 5; // UI
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        return rt;
    }

    public static Image Panel(string name, Transform parent, Color color)
    {
        var image = Rect(name, parent).gameObject.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    public static TextMeshProUGUI Text(string name, Transform parent, float size, Color color,
        TextAlignmentOptions align = TextAlignmentOptions.TopLeft, TMP_FontAsset font = null)
    {
        var text = Rect(name, parent).gameObject.AddComponent<TextMeshProUGUI>();
        if (font) text.font = font;
        text.fontSize = size;
        text.color = color;
        text.alignment = align;
        text.raycastTarget = false;
        text.enableWordWrapping = true;
        text.richText = true;
        return text;
    }

    /// <summary>Place a rect with a fixed size at an anchor point.</summary>
    public static void Place(RectTransform rt, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size)
    {
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = pivot;
        rt.anchoredPosition = position;
        rt.sizeDelta = size;
    }

    /// <summary>Stretch a rect to fill its parent with padding (left, right, top, bottom).</summary>
    public static void Fill(RectTransform rt, float left = 0f, float right = 0f, float top = 0f, float bottom = 0f)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.offsetMin = new Vector2(left, bottom);
        rt.offsetMax = new Vector2(-right, -top);
    }

    /// <summary>A horizontal bar. Returns the fill rect; set its anchorMax.x to 0..1 to show progress.</summary>
    public static RectTransform Bar(string name, Transform parent, Color background, Color fill, out Image fillImage)
    {
        var bg = Panel(name, parent, background);
        fillImage = Panel("Fill", bg.transform, fill);
        var fillRect = fillImage.rectTransform;
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = new Vector2(0f, 1f);
        fillRect.pivot = new Vector2(0f, 0.5f);
        fillRect.offsetMin = fillRect.offsetMax = Vector2.zero;
        return fillRect;
    }

    public static void SetBar(RectTransform fill, float value)
    {
        fill.anchorMax = new Vector2(Mathf.Clamp01(value), 1f);
    }
}
