using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The LiveLoop panel inside the VR headset (made by <see cref="VRHud"/>, drawn only while you are streaming).
/// Top-right corner, under the score band: a header with the red LIVE dot, viewer count and tips so far, then the last
/// four chat lines (one line each, "..." if long, fading with age) and little gold "tipped $2!" pop-ups below.
/// It sits where nothing else lives: the kill feed is top-left, team chat bottom-left, the match flags / score / round timer
/// are top-centre (and end at y 170), the phone, ammo and gear slots are bottom - so it never overlaps them (the canvas scales
/// from 1920x1080, anchors are on the right edge, so 16:10 and 4:3 only move it with the edge).
/// While the class cards are up only the header is shown, so the cards never sit under the chat.
/// </summary>
public class StreamOverlay
{
    const float W = 330f, HeaderH = 40f, LineH = 26f, ChatPad = 7f, Lines = 4;
    const float Top = 186f, Right = 28f;

    RectTransform root, body;
    Image dot, chatBg;
    TextMeshProUGUI live, viewers, tips;
    readonly TextMeshProUGUI[] lines = new TextMeshProUGUI[4];
    readonly RectTransform[] popRect = new RectTransform[2];
    readonly Image[] popBg = new Image[2];
    readonly TextMeshProUGUI[] popText = new TextMeshProUGUI[2];
    readonly List<LiveStream.ChatLine> temp = new List<LiveStream.ChatLine>();

    /// <summary>Builds the panel inside the HUD canvas root.</summary>
    public static StreamOverlay Create(RectTransform canvasRoot)
    {
        var o = new StreamOverlay();
        o.Build(canvasRoot);
        return o;
    }

    public RectTransform Root => root;

    static TextMeshProUGUI Label(Transform parent, float size, Color color, TextAlignmentOptions align, float x, float y, float w, float h, bool bold = false)
    {
        var t = UIFactory.Text("Text", parent, size, color, align);
        t.enableWordWrapping = false;
        t.overflowMode = TextOverflowModes.Ellipsis;
        if (bold) t.fontStyle = FontStyles.Bold;
        UIFactory.Place(t.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(x, -y), new Vector2(w, h));
        return t;
    }

    static Image Rounded(Transform parent, Color c)
    {
        var img = UIFactory.Panel("Panel", parent, c);
        img.sprite = VRHud.RoundedSprite();
        img.type = Image.Type.Sliced;
        return img;
    }

    void Build(RectTransform canvasRoot)
    {
        root = UIFactory.Rect("Stream panel", canvasRoot);
        UIFactory.Place(root, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-Right, -Top), new Vector2(W, HeaderH));

        // ---- header: red dot, LIVE, a little person + viewer count, tips
        var head = Rounded(root, new Color(0.04f, 0.05f, 0.08f, 0.78f));
        UIFactory.Fill(head.rectTransform);
        dot = UIFactory.Panel("Dot", head.transform, new Color(1f, 0.2f, 0.25f, 1f));
        dot.sprite = VRHud.RoundSprite();
        UIFactory.Place(dot.rectTransform, new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(22, 0), new Vector2(14, 14));
        live = Label(head.transform, 20, new Color(1f, 0.33f, 0.38f), TextAlignmentOptions.MidlineLeft, 36, 0, 58, HeaderH, true);
        live.text = "LIVE";
        live.characterSpacing = 2f;
        var headDot = UIFactory.Panel("Person head", head.transform, new Color(1f, 1f, 1f, 0.85f));
        headDot.sprite = VRHud.RoundSprite();
        UIFactory.Place(headDot.rectTransform, new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(112, 6), new Vector2(8, 8));
        var bodyArt = UIFactory.Panel("Person body", head.transform, new Color(1f, 1f, 1f, 0.85f));
        bodyArt.sprite = VRHud.RoundSprite();
        UIFactory.Place(bodyArt.rectTransform, new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(112, -5), new Vector2(16, 9));
        viewers = Label(head.transform, 22, Color.white, TextAlignmentOptions.MidlineLeft, 128, 0, 86, HeaderH, true);
        tips = Label(head.transform, 18, new Color(1f, 0.82f, 0.3f), TextAlignmentOptions.MidlineRight, 214, 0, 104, HeaderH, true);

        // ---- chat (hidden while the class cards are up)
        body = UIFactory.Rect("Body", root);
        UIFactory.Place(body, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0, -(HeaderH + 6)), new Vector2(W, Lines * LineH + ChatPad * 2));
        chatBg = Rounded(body, new Color(0f, 0f, 0f, 0.42f));
        UIFactory.Fill(chatBg.rectTransform);
        for (int i = 0; i < lines.Length; i++)
        {
            lines[i] = Label(body, 18, Color.white, TextAlignmentOptions.MidlineLeft, 12, ChatPad + i * LineH, W - 24, LineH);
            lines[i].richText = true;
        }

        // ---- tip pop-ups under the chat
        for (int i = 0; i < popRect.Length; i++)
        {
            popBg[i] = Rounded(body, new Color(1f, 0.78f, 0.25f, 0.94f));
            popRect[i] = popBg[i].rectTransform;
            UIFactory.Place(popRect[i], new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(0, -(Lines * LineH + ChatPad * 2 + 6 + i * 38)), new Vector2(262, 32));
            popText[i] = UIFactory.Text("Text", popBg[i].transform, 18, new Color(0.2f, 0.12f, 0.02f), TextAlignmentOptions.Center);
            popText[i].enableWordWrapping = false;
            popText[i].overflowMode = TextOverflowModes.Ellipsis;
            popText[i].fontStyle = FontStyles.Bold;
            UIFactory.Fill(popText[i].rectTransform, 8, 8, 0, 0);
            popBg[i].gameObject.SetActive(false);
        }
        root.gameObject.SetActive(false);
    }

    /// <summary>Call every frame while the HUD is up. 'picking' = the class cards are showing.</summary>
    public void Update(bool picking)
    {
        bool isLive = LiveStream.IsLive;
        if (root.gameObject.activeSelf != isLive) root.gameObject.SetActive(isLive);
        if (!isLive) return;

        float pulse = 0.55f + 0.45f * Mathf.Abs(Mathf.Sin(Time.time * 2.6f));
        dot.color = new Color(1f, 0.2f, 0.25f, pulse);
        viewers.text = LiveStream.Viewers.ToString("N0");
        tips.text = "$" + LiveStream.TipsTotal + " tips";
        if (body.gameObject.activeSelf == picking) body.gameObject.SetActive(!picking);
        if (picking) return;

        // chat: newest at the bottom, older lines fade first
        LiveStream.Recent(lines.Length, temp);
        bool any = temp.Count > 0;
        chatBg.enabled = any;
        for (int i = 0; i < lines.Length; i++)
        {
            int slot = i - (lines.Length - temp.Count); // fill from the bottom
            var t = lines[i];
            if (slot < 0) { t.text = ""; continue; }
            var c = temp[slot];
            float age = Time.time - c.time;
            string hex = ColorUtility.ToHtmlStringRGB(c.colour);
            t.text = c.tip ? $"<color=#FFD24A><b>{c.name}</b> {c.text}</color>" : $"<color=#{hex}><b>{c.name}</b></color> {c.text}";
            t.alpha = Mathf.Clamp01((LiveStream.ChatLife - age) / 2.5f) * (c.tip ? 1f : 0.95f);
        }

        // tip pop-ups: newest first, they slide in and fade out
        var pops = LiveStream.TipPops;
        for (int i = 0; i < popRect.Length; i++)
        {
            bool has = i < pops.Count && Time.time - pops[i].time < LiveStream.TipPopLife;
            if (popBg[i].gameObject.activeSelf != has) popBg[i].gameObject.SetActive(has);
            if (!has) continue;
            var p = pops[i];
            float age = Time.time - p.time;
            popText[i].text = $"{p.name} tipped ${p.dollars}!";
            float a = Mathf.Clamp01(Mathf.Min(age / 0.15f, (LiveStream.TipPopLife - age) / 0.6f));
            popBg[i].color = new Color(1f, 0.78f, 0.25f, 0.94f * a);
            popText[i].alpha = a;
            float slide = (1f - Mathf.Clamp01(age / 0.25f)) * 40f;
            popRect[i].anchoredPosition = new Vector2(slide, -(Lines * LineH + ChatPad * 2 + 6 + i * 38));
        }
    }
}
