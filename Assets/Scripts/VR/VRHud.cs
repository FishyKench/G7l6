using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Rendering.PostProcessing;

/// <summary>
/// What you see with the headset on: goggle edges, crosshair (opens up while moving), hit markers (red for
/// headshots, bigger for kills), health and ammo, the match score and round timer, big announcements, the kill
/// feed, team chat, a red flash when you're hit, and the "connecting" fade when you put the headset on or off.
/// The house HUD (clock, to-do notes, Mom's texts) still shows on top - those are your headset notifications.
/// </summary>
public class VRHud : MonoBehaviour
{
    static readonly Color Cyan = new Color32(90, 240, 220, 255);
    static readonly Color Red = new Color32(255, 85, 95, 255);

    /// <summary>0..1 black cover (set by the headset while connecting / disconnecting).</summary>
    public float fade;
    public string fadeText = "";

    VRArena arena;
    MatchTask match;
    Canvas canvas;
    RectTransform crosshair, hitMarker;
    readonly List<Image> crossBars = new List<Image>();
    readonly List<Image> hitBars = new List<Image>();
    Image hurt, cover, healthFill, reloadFill;
    TextMeshProUGUI health, ammo, topBar, announce, info, coverText;
    RectTransform feedRoot, chatRoot;
    readonly List<(TextMeshProUGUI text, float until)> feed = new List<(TextMeshProUGUI, float)>();
    readonly List<(TextMeshProUGUI text, float until)> chat = new List<(TextMeshProUGUI, float)>();
    Image redDot, heart, phone;
    TextMeshProUGUI pop, phoneText, heartText;
    RectTransform speedLines;
    readonly List<Image> lines = new List<Image>();
    float popUntil, buzzUntil;
    int unread;
    ChromaticAberration aberration;
    float baseAberration = -1f;
    string announceText;
    float announceUntil, announceLength = 1f;
    // Domination
    RectTransform domRoot;
    readonly List<(Image badge, TextMeshProUGUI letter, Image fill)> badges = new List<(Image, TextMeshProUGUI, Image)>();
    readonly List<(RectTransform root, Image diamond, TextMeshProUGUI letter, TextMeshProUGUI distance)> markers = new List<(RectTransform, Image, TextMeshProUGUI, TextMeshProUGUI)>();
    TextMeshProUGUI ourScore, theirScore, notice, capText;
    Image ourFill, theirFill, capFill;
    RectTransform capBar;
    float noticeUntil;
    // Classes, grenades, scope, flashbang
    TextMeshProUGUI classText, pickText, nadeText, pickTitle;
    RectTransform cardsRoot;
    readonly List<(RectTransform root, Image frame, Image glow, TextMeshProUGUI name)> cards = new List<(RectTransform, Image, Image, TextMeshProUGUI)>();
    /// <summary>The class cards are on screen (before / between rounds, or dead): you can pick a class, not walk around.</summary>
    public static bool CardsUp { get; private set; }

    readonly List<(GameObject root, TextMeshProUGUI text)> cardLocks = new List<(GameObject, TextMeshProUGUI)>();
    readonly List<(Image icon, TextMeshProUGUI count, Image slot)> gear = new List<(Image, TextMeshProUGUI, Image)>();
    static Sprite rounded;

    // How each class rates (0..1): damage, fire rate, range, mobility - for the class cards.
    static readonly float[][] Ratings =
    {
        new[] { 0.6f, 0.6f, 0.65f, 0.6f },   // Assault
        new[] { 0.4f, 0.95f, 0.3f, 0.9f },   // SMG
        new[] { 1f, 0.12f, 1f, 0.45f },      // Sniper
        new[] { 0.65f, 0.78f, 0.7f, 0.3f },  // LMG
    };
    static readonly Color[] ClassColours =
    {
        new Color32(90, 240, 220, 255), new Color32(255, 200, 60, 255), new Color32(255, 85, 95, 255), new Color32(170, 120, 255, 255),
    };
    RectTransform scopeRoot, scopeLens, scopeLeft, scopeRight, nadeMark;
    Image flashCover;
    static Sprite lens;
    static Sprite round, vignette;
    StreamOverlay stream; // LiveLoop panel (only drawn while streaming)

    internal static Sprite RoundedSprite() => Rounded();
    internal static Sprite RoundSprite() => Round();

    void Start()
    {
        arena = GetComponent<VRArena>();
        Build();
    }

    public void Announce(string text, float seconds, Color? color = null)
    {
        announceText = text;
        announceLength = seconds;
        announceUntil = Time.time + seconds;
        if (announce) announce.color = color ?? Color.white;
    }

    /// <summary>A mid-size line for capture-point news ("B CAPTURED", "A LOST").</summary>
    public void Notice(string text, Color color)
    {
        if (!notice) return;
        notice.text = text;
        notice.color = color;
        noticeUntil = Time.time + 2.2f;
    }

    /// <summary>A small pop under the crosshair ("HEADSHOT", "ELIMINATED").</summary>
    public void Pop(string text)
    {
        if (!pop) return;
        pop.text = text;
        popUntil = Time.time + 0.9f;
    }

    /// <summary>Your phone buzzed in your pocket - you can't read it with the headset on.</summary>
    public void Buzz()
    {
        unread++;
        buzzUntil = Time.time + 1.2f;
    }

    public void Feed(string killer, string victim, bool ourKill, bool head)
    {
        if (!feedRoot) return;
        var t = Text(feedRoot, $"<color=#{(ourKill ? "5AF0DC" : "FF555F")}>{killer}</color>  {(head ? "<color=#FFD24A>[HS]</color> " : "")}>  {victim}", 20, Color.white, TextAlignmentOptions.TopLeft, Vector2.zero, new Vector2(520, 26));
        t.rectTransform.anchorMin = t.rectTransform.anchorMax = new Vector2(0f, 1f);
        t.rectTransform.pivot = new Vector2(0f, 1f);
        feed.Insert(0, (t, Time.time + 5f));
        Trim(feed, 5);
    }

    public void Chat(string who, string message)
    {
        if (!chatRoot) return;
        var t = Text(chatRoot, $"<color=#50AAFF>{who}:</color> {message}", 20, Color.white, TextAlignmentOptions.BottomLeft, Vector2.zero, new Vector2(520, 26));
        t.rectTransform.anchorMin = t.rectTransform.anchorMax = new Vector2(0f, 0f);
        t.rectTransform.pivot = new Vector2(0f, 0f);
        chat.Insert(0, (t, Time.time + 7f));
        Trim(chat, 5);
    }

    static void Trim(List<(TextMeshProUGUI text, float until)> list, int max)
    {
        while (list.Count > max) { Destroy(list[list.Count - 1].text.gameObject); list.RemoveAt(list.Count - 1); }
    }

    // ------------------------------------------------------------------ building

    void Build()
    {
        canvas = UIFactory.CreateCanvas("VR HUD", 40);
        canvas.transform.SetParent(transform, false);
        var root = (RectTransform)canvas.transform;

        var goggles = UIFactory.Panel("Goggles", root, new Color(0f, 0f, 0f, 0.9f));
        goggles.sprite = Vignette();
        UIFactory.Fill(goggles.rectTransform, -60, -60, -40, -40);
        hurt = UIFactory.Panel("Hurt", root, new Color(1f, 0.1f, 0.1f, 0f));
        hurt.sprite = Vignette();
        UIFactory.Fill(hurt.rectTransform);

        // Crosshair: four bars that spread apart.
        crosshair = UIFactory.Rect("Crosshair", root);
        UIFactory.Place(crosshair, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        for (int i = 0; i < 4; i++)
        {
            var bar = UIFactory.Panel("Bar", crosshair, new Color(1f, 1f, 1f, 0.9f));
            bool vertical = i < 2;
            UIFactory.Place(bar.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, vertical ? new Vector2(3, 11) : new Vector2(11, 3));
            crossBars.Add(bar);
        }
        var dot = UIFactory.Panel("Dot", crosshair, Color.white);
        dot.sprite = Round();
        UIFactory.Place(dot.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(4, 4));

        hitMarker = UIFactory.Rect("Hit", root);
        UIFactory.Place(hitMarker, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        for (int i = 0; i < 4; i++)
        {
            var bar = UIFactory.Panel("X", hitMarker, Color.white);
            float angle = 45f + 90f * i;
            Vector2 d = new Vector2(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad));
            UIFactory.Place(bar.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), d * 14f, new Vector2(12, 3));
            bar.rectTransform.localRotation = Quaternion.Euler(0, 0, angle);
            hitBars.Add(bar);
        }

        // Speed lines round the edges (show when you're fast).
        speedLines = UIFactory.Rect("Speed lines", root);
        UIFactory.Place(speedLines, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        for (int i = 0; i < 28; i++)
        {
            float angle = i / 28f * 360f + Random.Range(-4f, 4f);
            var l = UIFactory.Panel("Line", speedLines, new Color(1f, 1f, 1f, 0f));
            var rt = l.rectTransform;
            UIFactory.Place(rt, new Vector2(0.5f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(Random.Range(160f, 320f), Random.Range(2f, 4f)));
            rt.localRotation = Quaternion.Euler(0, 0, angle);
            rt.anchoredPosition = Quaternion.Euler(0, 0, angle) * Vector3.right * Random.Range(520f, 640f);
            lines.Add(l);
        }

        redDot = UIFactory.Panel("Red dot", root, new Color(1f, 0.15f, 0.15f, 0f));
        redDot.sprite = Round();
        UIFactory.Place(redDot.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(7, 7));
        pop = Text(root, "", 24, new Color(1f, 0.85f, 0.35f), TextAlignmentOptions.Center, new Vector2(0, -60), new Vector2(400, 34));
        pop.fontStyle = FontStyles.Bold;

        // Stress: no meter in VR - just your heartbeat at the edges of your vision.
        heart = UIFactory.Panel("Heartbeat", root, new Color(0.6f, 0f, 0.05f, 0f));
        heart.sprite = Vignette();
        UIFactory.Fill(heart.rectTransform, -120, -120, -80, -80);
        heartText = Text(root, "", 22, new Color(1f, 0.4f, 0.4f), TextAlignmentOptions.Center, new Vector2(0, 200), new Vector2(900, 30), bottom: true);

        // Your phone buzzing (bottom right).
        phone = UIFactory.Panel("Phone", root, new Color(0.1f, 0.1f, 0.14f, 0.9f));
        phone.sprite = Round();
        phone.type = Image.Type.Sliced;
        UIFactory.Place(phone.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-60, 130), new Vector2(230, 54));
        phoneText = Text(phone.rectTransform, "", 20, Color.white, TextAlignmentOptions.Center, Vector2.zero, new Vector2(220, 50), centre: true);
        phone.gameObject.SetActive(false);

        // Health (bottom, left of centre) and ammo (right of centre).
        health = Text(root, "100", 44, Color.white, TextAlignmentOptions.Right, new Vector2(-250, 70), new Vector2(160, 54), bottom: true);
        health.fontStyle = FontStyles.Bold;
        var hb = UIFactory.Panel("Health bar", root, new Color(1f, 1f, 1f, 0.15f));
        UIFactory.Place(hb.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 0.5f), new Vector2(-160, 70), new Vector2(150, 10));
        healthFill = UIFactory.Panel("Fill", hb.transform, Cyan);
        Stretch(healthFill.rectTransform);
        ammo = Text(root, "30 / 30", 40, Color.white, TextAlignmentOptions.Left, new Vector2(250, 70), new Vector2(220, 54), bottom: true);
        ammo.fontStyle = FontStyles.Bold;
        var rb = UIFactory.Panel("Reload bar", root, new Color(1f, 1f, 1f, 0.15f));
        UIFactory.Place(rb.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 0.5f), new Vector2(160, 40), new Vector2(150, 6));
        reloadFill = UIFactory.Panel("Fill", rb.transform, Color.white);
        Stretch(reloadFill.rectTransform);
        Text(root, "HP", 18, new Color(1f, 1f, 1f, 0.6f), TextAlignmentOptions.Right, new Vector2(-250, 40), new Vector2(160, 24), bottom: true);

        topBar = Text(root, "", 26, Color.white, TextAlignmentOptions.Center, new Vector2(0, -150), new Vector2(1100, 40), top: true);
        BuildDomination(root);
        topBar.fontStyle = FontStyles.Bold;
        // LiveLoop stream panel: top-right, above the scope / class cards but under the flashbang white-out.
        stream = StreamOverlay.Create(root);
        if (flashCover) stream.Root.SetSiblingIndex(flashCover.rectTransform.GetSiblingIndex());
        announce = Text(root, "", 84, Color.white, TextAlignmentOptions.Center, new Vector2(0, 130), new Vector2(1400, 110));
        announce.fontStyle = FontStyles.Bold;
        info = Text(root, "", 26, new Color(1f, 1f, 1f, 0.85f), TextAlignmentOptions.Center, new Vector2(0, 50), new Vector2(1200, 80));

        feedRoot = UIFactory.Rect("Kill feed", root);
        UIFactory.Place(feedRoot, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(40, -60), new Vector2(520, 160));
        chatRoot = UIFactory.Rect("Chat", root);
        UIFactory.Place(chatRoot, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(40, 230), new Vector2(520, 150));


        cover = UIFactory.Panel("Fade", root, Color.black);
        UIFactory.Fill(cover.rectTransform);
        coverText = Text(cover.rectTransform, "", 34, Cyan, TextAlignmentOptions.Center, Vector2.zero, new Vector2(900, 60), centre: true);
        canvas.enabled = false;
    }

    void BuildDomination(RectTransform root)
    {
        domRoot = UIFactory.Rect("Domination", root);
        UIFactory.Fill(domRoot);
        var top = UIFactory.Rect("Flags", domRoot);
        UIFactory.Place(top, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -30), new Vector2(900, 100));

        for (int i = 0; i < 3; i++)
        {
            var badge = UIFactory.Panel("Badge", top, new Color(0.85f, 0.85f, 0.9f, 0.85f));
            UIFactory.Place(badge.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), new Vector2((i - 1) * 70, -30), new Vector2(40, 40));
            badge.rectTransform.localRotation = Quaternion.Euler(0, 0, 45);
            var letter = Text(top, "", 28, Color.white, TextAlignmentOptions.Center, Vector2.zero, new Vector2(60, 40));
            letter.rectTransform.anchorMin = letter.rectTransform.anchorMax = new Vector2(0.5f, 1f);
            letter.rectTransform.anchoredPosition = new Vector2((i - 1) * 70, -30);
            letter.fontStyle = FontStyles.Bold;
            var bar = UIFactory.Panel("Capture", top, new Color(1f, 1f, 1f, 0.15f));
            UIFactory.Place(bar.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), new Vector2((i - 1) * 70, -66), new Vector2(46, 5));
            var fill = UIFactory.Panel("Fill", bar.transform, Color.white);
            Stretch(fill.rectTransform);
            badges.Add((badge, letter, fill));
        }

        ourScore = Text(top, "0", 40, Cyan, TextAlignmentOptions.Right, Vector2.zero, new Vector2(120, 50));
        ourScore.rectTransform.anchorMin = ourScore.rectTransform.anchorMax = new Vector2(0.5f, 1f);
        ourScore.rectTransform.anchoredPosition = new Vector2(-190, -26);
        ourScore.fontStyle = FontStyles.Bold;
        theirScore = Text(top, "0", 40, Red, TextAlignmentOptions.Left, Vector2.zero, new Vector2(120, 50));
        theirScore.rectTransform.anchorMin = theirScore.rectTransform.anchorMax = new Vector2(0.5f, 1f);
        theirScore.rectTransform.anchoredPosition = new Vector2(190, -26);
        theirScore.fontStyle = FontStyles.Bold;
        var ourBar = UIFactory.Panel("Our score", top, new Color(1f, 1f, 1f, 0.12f));
        UIFactory.Place(ourBar.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), new Vector2(-200, -62), new Vector2(170, 8));
        ourFill = UIFactory.Panel("Fill", ourBar.transform, Cyan);
        Stretch(ourFill.rectTransform);
        var theirBar = UIFactory.Panel("Their score", top, new Color(1f, 1f, 1f, 0.12f));
        UIFactory.Place(theirBar.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), new Vector2(200, -62), new Vector2(170, 8));
        theirFill = UIFactory.Panel("Fill", theirBar.transform, Red);
        Stretch(theirFill.rectTransform);

        // Markers over each point, out in the world.
        for (int i = 0; i < 3; i++)
        {
            var m = UIFactory.Rect("Marker", domRoot);
            UIFactory.Place(m, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(60, 70));
            var diamond = UIFactory.Panel("Diamond", m, Color.white);
            UIFactory.Place(diamond.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 8), new Vector2(26, 26));
            diamond.rectTransform.localRotation = Quaternion.Euler(0, 0, 45);
            var letter = Text(m, "", 20, Color.white, TextAlignmentOptions.Center, new Vector2(0, 8), new Vector2(40, 30));
            letter.fontStyle = FontStyles.Bold;
            var dist = Text(m, "", 15, new Color(1f, 1f, 1f, 0.8f), TextAlignmentOptions.Center, new Vector2(0, -18), new Vector2(80, 20));
            markers.Add((m, diamond, letter, dist));
        }

        // Standing in a point: what's happening and how far the capture is.
        capText = Text(domRoot, "", 26, Color.white, TextAlignmentOptions.Center, new Vector2(0, -150), new Vector2(700, 36));
        capText.fontStyle = FontStyles.Bold;
        var cb = UIFactory.Panel("Capture bar", domRoot, new Color(1f, 1f, 1f, 0.15f));
        UIFactory.Place(cb.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -178), new Vector2(280, 8));
        capBar = cb.rectTransform;
        capFill = UIFactory.Panel("Fill", cb.transform, Color.white);
        Stretch(capFill.rectTransform);

        notice = Text(domRoot, "", 36, Color.white, TextAlignmentOptions.Center, new Vector2(0, 230), new Vector2(900, 50));
        notice.fontStyle = FontStyles.Bold;
        BuildClassesAndGear(root);
    }

    void BuildClassesAndGear(RectTransform root)
    {
        // Sniper scope: a round lens with black all round it and thin cross-hairs.
        scopeRoot = UIFactory.Rect("Scope", root);
        UIFactory.Fill(scopeRoot);
        scopeLens = UIFactory.Rect("Lens", scopeRoot);
        UIFactory.Place(scopeLens, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1080, 1080));
        var ring = UIFactory.Panel("Ring", scopeLens, Color.black);
        ring.sprite = Lens();
        UIFactory.Fill(ring.rectTransform);
        var h = UIFactory.Panel("Cross H", scopeLens, new Color(0f, 0f, 0f, 0.85f));
        UIFactory.Place(h.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1000, 2));
        var v = UIFactory.Panel("Cross V", scopeLens, new Color(0f, 0f, 0f, 0.85f));
        UIFactory.Place(v.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(2, 1000));
        var dot = UIFactory.Panel("Dot", scopeLens, new Color(1f, 0.2f, 0.2f, 0.9f));
        dot.sprite = Round();
        UIFactory.Place(dot.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(5, 5));
        scopeLeft = UIFactory.Panel("Side", scopeRoot, Color.black).rectTransform;
        scopeRight = UIFactory.Panel("Side", scopeRoot, Color.black).rectTransform;
        scopeRoot.gameObject.SetActive(false);

        // Your class and grenades (next to the ammo), and the class picker (while dead / just spawned).
        BuildGear(root);
        BuildCards(root);

        // A live frag near you: a red marker round the crosshair, pointing at it.
        nadeMark = UIFactory.Rect("Grenade warning", root);
        UIFactory.Place(nadeMark, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(70, 70));
        var bg = UIFactory.Panel("Disc", nadeMark, new Color(0.85f, 0.1f, 0.1f, 0.85f));
        bg.sprite = Round();
        UIFactory.Fill(bg.rectTransform, 10, 10, 10, 10);
        nadeText = Text(nadeMark, "!", 34, Color.white, TextAlignmentOptions.Center, Vector2.zero, new Vector2(70, 70), centre: true);
        nadeText.fontStyle = FontStyles.Bold;
        nadeMark.gameObject.SetActive(false);

        // Flashbang white-out (on top of everything but the connect fade).
        flashCover = UIFactory.Panel("Flashed", root, new Color(1f, 1f, 1f, 0f));
        UIFactory.Fill(flashCover.rectTransform);
    }

    // ------------------------------------------------------------------ gear (bottom right) and class cards

    void BuildGear(RectTransform root)
    {
        classText = Text(root, "", 22, Color.white, TextAlignmentOptions.Left, new Vector2(590, 128), new Vector2(360, 30), bottom: true);
        classText.fontStyle = FontStyles.Bold;
        string[] keys = { "G", "Q" };
        for (int i = 0; i < 2; i++)
        {
            var slot = UIFactory.Panel("Gear slot", root, new Color(0f, 0f, 0f, 0.55f));
            slot.sprite = Rounded();
            slot.type = Image.Type.Sliced;
            UIFactory.Place(slot.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0.5f), new Vector2(450 + i * 92, 74), new Vector2(80, 80));
            var icon = UIFactory.Panel("Icon", slot.transform, Color.white);
            icon.sprite = i == 0 ? Round() : Rounded();
            UIFactory.Place(icon.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 2), i == 0 ? new Vector2(34, 34) : new Vector2(22, 40));
            if (i == 0)
            {
                // a frag: green body with a lever on top
                icon.color = new Color(0.35f, 0.75f, 0.3f);
                var lever = UIFactory.Panel("Lever", icon.transform, new Color(0.75f, 0.75f, 0.7f));
                UIFactory.Place(lever.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 0f), new Vector2(4, -4), new Vector2(10, 12));
            }
            else
            {
                // a flashbang: a white can with a band
                icon.color = new Color(0.92f, 0.94f, 0.96f);
                var band = UIFactory.Panel("Band", icon.transform, new Color(0.25f, 0.3f, 0.35f));
                UIFactory.Place(band.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(22, 6));
            }
            var key = UIFactory.Panel("Key", slot.transform, Color.white);
            key.sprite = Rounded();
            key.type = Image.Type.Sliced;
            UIFactory.Place(key.rectTransform, new Vector2(0f, 1f), new Vector2(0.5f, 0.5f), new Vector2(6, -6), new Vector2(28, 28));
            var keyText = Text(key.rectTransform, keys[i], 19, Color.black, TextAlignmentOptions.Center, Vector2.zero, new Vector2(28, 28), centre: true);
            keyText.fontStyle = FontStyles.Bold;
            var count = Text(slot.rectTransform, "x1", 20, Color.white, TextAlignmentOptions.BottomRight, Vector2.zero, new Vector2(74, 74), centre: true);
            count.fontStyle = FontStyles.Bold;
            Text(slot.rectTransform, i == 0 ? "FRAG" : "FLASH", 13, new Color(1f, 1f, 1f, 0.6f), TextAlignmentOptions.Center, new Vector2(0, -52), new Vector2(80, 18), centre: true);
            gear.Add((icon, count, slot));
        }
    }

    void BuildCards(RectTransform root)
    {
        cardsRoot = UIFactory.Rect("Class cards", root);
        UIFactory.Fill(cardsRoot);
        var dim = UIFactory.Panel("Dim", cardsRoot, new Color(0.01f, 0.01f, 0.04f, 0.6f));
        UIFactory.Fill(dim.rectTransform);
        pickTitle = Text(cardsRoot, "CHOOSE YOUR CLASS", 44, Color.white, TextAlignmentOptions.Center, new Vector2(0, 300), new Vector2(1400, 60));
        pickTitle.fontStyle = FontStyles.Bold;
        pickText = Text(cardsRoot, "", 22, new Color(1f, 1f, 1f, 0.75f), TextAlignmentOptions.Center, new Vector2(0, -300), new Vector2(1400, 34));
        string[] stats = { "DAMAGE", "FIRE RATE", "RANGE", "MOBILITY" };
        for (int i = 0; i < VRLoadout.All.Length; i++)
        {
            var role = VRLoadout.All[i];
            var l = VRLoadout.For(role);
            Color c = ClassColours[i];
            var card = UIFactory.Rect("Card", cardsRoot);
            UIFactory.Place(card, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2((i - 1.5f) * 330, 0), new Vector2(300, 460));
            var glow = UIFactory.Panel("Glow", card, new Color(c.r, c.g, c.b, 0f));
            glow.sprite = Rounded();
            glow.type = Image.Type.Sliced;
            UIFactory.Fill(glow.rectTransform, -10, -10, -10, -10);
            var frame = UIFactory.Panel("Frame", card, new Color(0.05f, 0.05f, 0.09f, 0.92f));
            frame.sprite = Rounded();
            frame.type = Image.Type.Sliced;
            UIFactory.Fill(frame.rectTransform);

            // The gun, turning (a live 3D picture).
            var stand = UIFactory.Panel("Stand", card, new Color(c.r, c.g, c.b, 0.12f));
            stand.sprite = Rounded();
            stand.type = Image.Type.Sliced;
            UIFactory.Place(stand.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -64), new Vector2(276, 160));
            var pic = UIFactory.Rect("Gun", stand.transform).gameObject.AddComponent<RawImage>();
            pic.raycastTarget = false;
            pic.texture = VRWeaponShowcase.Picture(role);
            UIFactory.Fill(pic.rectTransform);

            var key = UIFactory.Panel("Key", card, c);
            key.sprite = Rounded();
            key.type = Image.Type.Sliced;
            UIFactory.Place(key.rectTransform, new Vector2(0f, 1f), new Vector2(0.5f, 0.5f), new Vector2(34, -34), new Vector2(44, 44));
            Text(key.rectTransform, (i + 1).ToString(), 28, Color.black, TextAlignmentOptions.Center, Vector2.zero, new Vector2(44, 44), centre: true).fontStyle = FontStyles.Bold;
            var name = Text(card, l.name, 32, Color.white, TextAlignmentOptions.Left, Vector2.zero, new Vector2(220, 44));
            name.rectTransform.anchorMin = name.rectTransform.anchorMax = new Vector2(0f, 1f);
            name.rectTransform.pivot = new Vector2(0f, 0.5f);
            name.rectTransform.anchoredPosition = new Vector2(68, -34);
            name.fontStyle = FontStyles.Bold;

            var blurb = Text(card, l.blurb, 16, new Color(1f, 1f, 1f, 0.72f), TextAlignmentOptions.Top, new Vector2(0, -20), new Vector2(270, 46));
            blurb.enableWordWrapping = true;
            for (int s = 0; s < stats.Length; s++)
            {
                float y = -70 - s * 34;
                Text(card, stats[s], 14, new Color(1f, 1f, 1f, 0.6f), TextAlignmentOptions.Left, new Vector2(-12, y + 11), new Vector2(250, 18));
                var bar = UIFactory.Panel("Bar", card, new Color(1f, 1f, 1f, 0.12f));
                UIFactory.Place(bar.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, y - 4), new Vector2(250, 8));
                var fill = UIFactory.Panel("Fill", bar.transform, c);
                Stretch(fill.rectTransform);
                fill.rectTransform.anchorMax = new Vector2(Ratings[i][s], 1f);
            }
            string kit = (l.frags > 0 ? $"[G] FRAG x{l.frags}" : "") + (l.frags > 0 && l.flashes > 0 ? "    " : "") + (l.flashes > 0 ? $"[Q] FLASH x{l.flashes}" : "");
            Text(card, kit, 16, c, TextAlignmentOptions.Center, new Vector2(0, -205), new Vector2(270, 24)).fontStyle = FontStyles.Bold;
            cards.Add((card, frame, glow, name));

            // Locked until your rank is high enough (see VRUnlocks).
            var lockPanel = UIFactory.Panel("Locked", card, new Color(0.02f, 0.02f, 0.05f, 0.96f)); // covers the card: nothing shows through under LOCKED
            lockPanel.sprite = Rounded();
            lockPanel.type = Image.Type.Sliced;
            UIFactory.Fill(lockPanel.rectTransform);
            var lockText = Text(lockPanel.rectTransform, "", 26, new Color(1f, 0.82f, 0.3f), TextAlignmentOptions.Center, Vector2.zero, new Vector2(260, 120), centre: true);
            lockText.fontStyle = FontStyles.Bold;
            lockText.enableWordWrapping = true;
            cardLocks.Add((lockPanel.gameObject, lockText));
        }
        cardsRoot.gameObject.SetActive(false);
    }

    bool cursorFreed;

    /// <summary>While picking before a round: 1-4 or a click on a card picks it and starts the round.</summary>
    void BriefingInput()
    {
        for (int i = 0; i < VRLoadout.All.Length; i++)
            if (Input.GetKeyDown(KeyCode.Alpha1 + i) || Input.GetKeyDown(KeyCode.Keypad1 + i)) { arena.ConfirmClass(VRLoadout.All[i]); return; }
        if (!Input.GetMouseButtonDown(0)) return;
        for (int i = 0; i < cards.Count; i++)
            if (RectTransformUtility.RectangleContainsScreenPoint(cards[i].root, Input.mousePosition, null)) { arena.ConfirmClass(VRLoadout.All[i]); return; }
    }

    void FreeCursor(bool free)
    {
        if (cursorFreed == free) return;
        cursorFreed = free;
        Cursor.lockState = free ? CursorLockMode.None : CursorLockMode.Locked;
        Cursor.visible = free;
    }

    void UpdateGear(VRPlayer p)
    {
        var root = (RectTransform)canvas.transform;
        bool scoped = p.Scoped;
        scopeRoot.gameObject.SetActive(scoped);
        if (scoped)
        {
            float hgt = root.rect.height, w = root.rect.width;
            scopeLens.sizeDelta = new Vector2(hgt, hgt);
            float side = Mathf.Max(0f, (w - hgt) * 0.5f) + 4f;
            foreach (var (rt, left) in new[] { (scopeLeft, true), (scopeRight, false) })
            {
                rt.anchorMin = new Vector2(left ? 0f : 1f, 0f);
                rt.anchorMax = new Vector2(left ? 0f : 1f, 1f);
                rt.pivot = new Vector2(left ? 0f : 1f, 0.5f);
                rt.sizeDelta = new Vector2(side, 0f);
                rt.anchoredPosition = Vector2.zero;
            }
        }
        if (scoped) redDot.color = new Color(0f, 0f, 0f, 0f);

        var l = p.Loadout;
        string next = p.NextRole != p.Role ? $"  <size=75%><color=#FFD24A>next: {VRLoadout.For(p.NextRole).name}</color></size>" : "";
        classText.text = l.name + next;
        int[] counts = { p.Frags, p.Flashes };
        for (int i = 0; i < gear.Count; i++)
        {
            bool have = counts[i] > 0;
            gear[i].count.text = "x" + counts[i];
            gear[i].icon.canvasRenderer.SetAlpha(have ? 1f : 0.25f);
            gear[i].count.alpha = have ? 1f : 0.35f;
            gear[i].slot.color = new Color(0f, 0f, 0f, have ? 0.55f : 0.3f);
        }

        // Class cards: before each round (everything waits for you), while you're dead, and between rounds.
        bool briefing = arena.State == VRArena.Mode.Briefing;
        bool between = match && match.State == MatchTask.Phase.Break && arena.State != VRArena.Mode.Fighting;
        bool picking = briefing || !p.Alive || between;
        cardsRoot.gameObject.SetActive(picking);
        CardsUp = picking;
        VRWeaponShowcase.Show(picking);
        FreeCursor(briefing && p.controlled);
        if (briefing) BriefingInput();
        if (picking)
        {
            pickTitle.text = briefing ? $"CHOOSE YOUR CLASS  <color=#5AF0DC>{Mathf.CeilToInt(arena.BriefingLeft)}</color>"
                           : between ? $"NEXT ROUND IN {Mathf.CeilToInt(match.Timer)}  -  CHOOSE YOUR CLASS" : "CHOOSE YOUR CLASS";
            pickText.text = briefing ? "click a class or press 1 - 4  -  the round starts when you pick"
                          : between ? "press 1 - 4 to pick  -  you spawn with it      ESC: headset off to deal with the house (rounds start without you)"
                          : "press 1 - 4 to pick  -  you spawn with it";
            int hover = -1;
            if (briefing)
                for (int i = 0; i < cards.Count; i++)
                    if (RectTransformUtility.RectangleContainsScreenPoint(cards[i].root, Input.mousePosition, null)) hover = i;
            for (int i = 0; i < cardLocks.Count; i++)
            {
                var role = VRLoadout.All[i];
                bool locked = !VRUnlocks.Has(role);
                cardLocks[i].root.SetActive(locked);
                if (locked) cardLocks[i].text.text = $"LOCKED\n<size=70%>reach {Campaign.RankName(VRUnlocks.Needs(role)).ToUpperInvariant()} in Ranked</size>";
            }
            for (int i = 0; i < cards.Count; i++)
            {
                bool on = VRLoadout.All[i] == p.NextRole || i == hover;
                var c = ClassColours[i];
                float pulse = on ? 0.55f + 0.25f * Mathf.Sin(Time.time * 4f) : 0f;
                cards[i].glow.color = new Color(c.r, c.g, c.b, pulse);
                cards[i].frame.color = on ? new Color(0.08f, 0.08f, 0.14f, 0.95f) : new Color(0.05f, 0.05f, 0.09f, 0.8f);
                cards[i].root.localScale = Vector3.one * Mathf.Lerp(cards[i].root.localScale.x, on ? 1.07f : 0.96f, Time.unscaledDeltaTime * 12f);
                cards[i].name.color = on ? c : Color.white;
            }
        }

        // Nearest live frag within 9 m (an enemy's - or your own).
        VRGrenade near = null;
        float best = 9f;
        foreach (var g in VRGrenade.Live)
        {
            if (!g || g.kind != VRGrenade.Kind.Frag || (g.team == 0 && g.thrower != p)) continue;
            float d = Vector3.Distance(g.Position, p.transform.position);
            if (d < best) { best = d; near = g; }
        }
        nadeMark.gameObject.SetActive(near && p.Alive && p.Cam);
        if (near && p.Cam)
        {
            Vector3 local = p.Cam.transform.InverseTransformDirection(near.Position - p.Cam.transform.position);
            float angle = Mathf.Atan2(local.x, local.z);
            nadeMark.anchoredPosition = new Vector2(Mathf.Sin(angle), Mathf.Cos(angle)) * 170f;
            nadeMark.localScale = Vector3.one * (1f + 0.15f * Mathf.Sin(Time.time * 18f));
        }

        float f = p.FlashAmount;
        flashCover.color = new Color(1f, 1f, 1f, Mathf.Clamp01(f * 1.25f));
    }

    /// <summary>A rounded rectangle (sliced, so it stretches cleanly).</summary>
    static Sprite Rounded()
    {
        if (rounded) return rounded;
        const int size = 48, r = 14;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[size * size];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float dx = Mathf.Max(0f, Mathf.Max(r - x - 0.5f, x + 0.5f - (size - r))), dy = Mathf.Max(0f, Mathf.Max(r - y - 0.5f, y + 0.5f - (size - r)));
            float a = Mathf.Clamp01(r - Mathf.Sqrt(dx * dx + dy * dy) + 0.5f);
            px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
        }
        tex.SetPixels32(px);
        tex.Apply();
        rounded = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(r, r, r, r));
        return rounded;
    }

    static Sprite Lens()
    {
        if (lens) return lens;
        lens = MakeSprite(512, (x, y) =>
        {
            float d = Vector2.Distance(new Vector2(x, y), new Vector2(256f, 256f)) / 256f;
            return Mathf.Clamp01((d - 0.93f) / 0.05f);
        });
        return lens;
    }

    void UpdateDomination(VRPlayer p)
    {
        UpdateGear(p);
        bool on = (arena.State == VRArena.Mode.Fighting || arena.State == VRArena.Mode.Practice) && arena.Flags.Count > 0;
        domRoot.gameObject.SetActive(on);
        if (!on) return;

        ourScore.text = Mathf.FloorToInt(arena.Score[0]).ToString();
        theirScore.text = Mathf.FloorToInt(arena.Score[1]).ToString();
        ourFill.rectTransform.anchorMax = new Vector2(arena.Score[0] / arena.ScoreLimit, 1f);
        theirFill.rectTransform.anchorMax = new Vector2(arena.Score[1] / arena.ScoreLimit, 1f);

        var cam = p.Cam;
        var root = (RectTransform)canvas.transform;
        VRFlag standingIn = null;
        for (int i = 0; i < badges.Count; i++)
        {
            bool has = i < arena.Flags.Count;
            badges[i].badge.gameObject.SetActive(has);
            badges[i].letter.gameObject.SetActive(has);
            markers[i].root.gameObject.SetActive(false);
            if (!has) continue;
            var f = arena.Flags[i];
            Color c = VRFlag.TeamColor(f.Owner);
            float pulse = f.Contested || (f.Capturer >= 0 && f.Progress > 0f) ? 0.6f + 0.4f * Mathf.Abs(Mathf.Sin(Time.time * 6f)) : 1f;
            badges[i].badge.color = new Color(c.r, c.g, c.b, 0.85f * pulse);
            badges[i].letter.text = f.letter;
            badges[i].fill.color = VRFlag.TeamColor(f.Capturer);
            badges[i].fill.rectTransform.anchorMax = new Vector2(f.Progress, 1f);
            if (p.Alive && f.Contains(p.transform.position)) standingIn = f;

            if (!cam) continue;
            Vector3 world = f.Position + Vector3.up * 4.6f;
            Vector3 screen = cam.WorldToScreenPoint(world);
            if (screen.z <= 0.1f) continue;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(root, screen, null, out var local)) continue;
            var m = markers[i];
            m.root.gameObject.SetActive(standingIn != f);
            m.root.anchoredPosition = local;
            m.diamond.color = new Color(c.r, c.g, c.b, 0.75f * pulse);
            m.letter.text = f.letter;
            m.distance.text = Mathf.RoundToInt(Vector3.Distance(p.transform.position, f.Position)) + "m";
        }

        bool showCap = standingIn != null;
        capText.gameObject.SetActive(showCap);
        capBar.gameObject.SetActive(showCap && standingIn.Progress > 0f);
        if (showCap)
        {
            var f = standingIn;
            if (f.Contested) { capText.text = $"CONTESTED - {f.letter}"; capText.color = new Color(1f, 0.82f, 0.3f); }
            else if (f.Owner == 0 && f.Progress <= 0f) { capText.text = $"DEFENDING {f.letter}"; capText.color = Cyan; }
            else if (f.Capturer == 0) { capText.text = $"CAPTURING {f.letter}"; capText.color = Cyan; }
            else { capText.text = $"CLEARING {f.letter}"; capText.color = Color.white; }
            capFill.color = VRFlag.TeamColor(f.Capturer);
            capFill.rectTransform.anchorMax = new Vector2(f.Progress, 1f);
        }

        float left = noticeUntil - Time.time;
        notice.alpha = Mathf.Clamp01(left / 0.4f);
        notice.rectTransform.localScale = Vector3.one * (1f + Mathf.Max(0f, left - 1.9f) * 1.5f);
    }

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        rt.pivot = new Vector2(0f, 0.5f);
    }

    TextMeshProUGUI Text(RectTransform parent, string s, float size, Color color, TextAlignmentOptions align, Vector2 at, Vector2 box,
        bool top = false, bool bottom = false, bool centre = false)
    {
        var t = UIFactory.Text("Text", parent, size, color, align);
        t.text = s;
        t.enableWordWrapping = false;
        Vector2 anchor = top ? new Vector2(0.5f, 1f) : bottom ? new Vector2(0.5f, 0f) : new Vector2(0.5f, 0.5f);
        UIFactory.Place(t.rectTransform, anchor, new Vector2(0.5f, 0.5f), at, box);
        return t;
    }

    // ------------------------------------------------------------------ every frame

    void Update()
    {
        if (!canvas || !arena) return;
        var p = arena.Player;
        bool show = (p && p.controlled) || fade > 0.001f;
        if (canvas.enabled != show) canvas.enabled = show;
        if (p && !p.controlled) unread = 0; // headset off: you've read your messages
        cover.color = new Color(0f, 0f, 0f, fade);
        coverText.text = fadeText;
        coverText.alpha = fade;
        if (!show || !p) { if (cursorFreed) FreeCursor(false); VRWeaponShowcase.Show(false); return; }
        if (!match) match = FindObjectOfType<MatchTask>();

        // Crosshair spreads while moving or shooting.
        float move = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical")).magnitude;
        float gap = 8f + move * 10f + (Input.GetMouseButton(0) ? 6f : 0f);
        crossBars[0].rectTransform.anchoredPosition = new Vector2(0, gap + 4f);
        crossBars[1].rectTransform.anchoredPosition = new Vector2(0, -gap - 4f);
        crossBars[2].rectTransform.anchoredPosition = new Vector2(gap + 4f, 0);
        crossBars[3].rectTransform.anchoredPosition = new Vector2(-gap - 4f, 0);
        crosshair.gameObject.SetActive(p.Alive && p.Aim < 0.5f);
        redDot.color = new Color(1f, 0.15f, 0.15f, p.Alive ? Mathf.Clamp01((p.Aim - 0.6f) * 3f) : 0f);

        // Speed lines and colour fringing when you're fast.
        for (int i = 0; i < lines.Count; i++)
        {
            float flicker = Mathf.PerlinNoise(i * 1.7f, Time.time * 9f);
            lines[i].color = new Color(1f, 1f, 1f, p.SpeedAmount * 0.22f * flicker);
        }
        if (aberration == null)
        {
            var volume = FindObjectOfType<PostProcessVolume>();
            if (volume && volume.profile.TryGetSettings(out ChromaticAberration ca)) { aberration = ca; baseAberration = ca.intensity.value; }
        }
        if (aberration != null) aberration.intensity.value = baseAberration + p.SpeedAmount * 0.6f + (p.Sliding ? 0.2f : 0f);

        float popLeft = popUntil - Time.time;
        pop.alpha = Mathf.Clamp01(popLeft / 0.3f);
        pop.rectTransform.localScale = Vector3.one * (1f + Mathf.Max(0f, popLeft - 0.7f) * 2f);

        // Heartbeat: faster and stronger the more stressed you are (that's the only stress you see in VR).
        var stress = StressManager.Instance;
        var day = DayManager.Instance;
        float s = stress ? stress.Normalized : 0f;
        bool panic = day && day.PanicLeft >= 0f;
        float strength = Mathf.Clamp01((s - 0.55f) / 0.45f);
        float rate = Mathf.Lerp(1.1f, 2.6f, strength) * (panic ? 1.4f : 1f);
        float beat = Mathf.Pow(Mathf.Max(0f, Mathf.Sin(Time.time * rate * Mathf.PI * 2f)), 8f);
        heart.color = new Color(0.6f, 0f, 0.05f, strength * (0.25f + beat * 0.5f) + (panic ? 0.25f : 0f));
        heartText.text = panic ? "Your heart is pounding... take the headset off and breathe" : s > 0.85f ? "You feel really stressed" : "";
        heartText.alpha = panic ? 0.7f + beat * 0.3f : 0.6f;

        // Phone buzzes - you can't read it in here.
        phone.gameObject.SetActive(unread > 0);
        if (unread > 0)
        {
            bool buzzing = Time.time < buzzUntil;
            phoneText.text = buzzing ? "BZZT  BZZT" : unread == 1 ? "1 new message" : $"{unread} new messages";
            phone.rectTransform.anchoredPosition = new Vector2(-60 + (buzzing ? Mathf.Sin(Time.time * 70f) * 4f : 0f), 130);
        }

        float since = Time.time - p.lastHitTime;
        bool kill = Time.time - p.lastKillTime < 0.3f;
        float a = Mathf.Clamp01(1f - since / (kill ? 0.35f : 0.18f));
        foreach (var b in hitBars) b.color = new Color(1f, p.lastHitWasHead || kill ? 0.3f : 1f, p.lastHitWasHead || kill ? 0.3f : 1f, a);
        hitMarker.localScale = Vector3.one * (kill ? 1.5f : 1f);

        float hp = p.Health / p.maxHealth;
        float hurtFlash = Mathf.Clamp01(1f - (Time.time - p.lastHurtTime) / 0.4f) * 0.55f;
        hurt.color = new Color(1f, 0.1f, 0.1f, Mathf.Max(hurtFlash, hp < 0.35f && p.Alive ? 0.25f + Mathf.Sin(Time.time * 6f) * 0.08f : 0f));
        health.text = Mathf.CeilToInt(p.Health).ToString();
        health.color = hp < 0.35f ? Red : Color.white;
        healthFill.rectTransform.anchorMax = new Vector2(hp, 1f);
        healthFill.color = hp < 0.35f ? Red : Cyan;
        ammo.text = p.Reloading ? "<size=70%>RELOADING</size>" : $"{p.Ammo} <size=60%>/ {p.magazine}</size>";
        ammo.color = p.Ammo <= 5 && !p.Reloading ? Red : Color.white;
        reloadFill.rectTransform.anchorMax = new Vector2(p.Reloading ? p.ReloadProgress : (float)p.Ammo / p.magazine, 1f);

        // Score line and the info under the announcement.
        string score = match ? $"<color=#5AF0DC>YOU {match.Wins}</color>  :  <color=#FF555F>{match.Losses} THEM</color>" : "";
        string infoLine = "";
        UpdateDomination(p);
        if (arena.State == VRArena.Mode.Briefing)
            topBar.text = match && match.State == MatchTask.Phase.Round ? $"RANKED  -  ROUND {match.RoundNumber}  -  GET READY" : "WARM-UP  -  GET READY";
        else if (arena.State == VRArena.Mode.Fighting)
        {
            int secs = Mathf.CeilToInt(Mathf.Max(0f, arena.TimeLeft));
            topBar.text = $"DOMINATION  -  ROUND {(match ? match.RoundNumber : 1)}  ({(match ? match.Wins : 0)}:{(match ? match.Losses : 0)})  -  " + (arena.Overtime ? "<color=#FF6A5A>OVERTIME</color>" : $"{secs / 60}:{secs % 60:00}");
            if (!p.Alive) infoLine = arena.PlayerRespawnIn > 0f ? $"RESPAWN IN {Mathf.CeilToInt(arena.PlayerRespawnIn)}" : "";
        }
        else if (arena.State == VRArena.Mode.Practice)
        {
            int secs = Mathf.CeilToInt(Mathf.Max(0f, arena.TimeLeft));
            topBar.text = $"WARM-UP DOMINATION (unranked)  -  " + (arena.Overtime ? "<color=#FF6A5A>OVERTIME</color>" : $"{secs / 60}:{secs % 60:00}") + $"  -  KILLS: {p.Kills}  -  RANKED AT 4:00 PM";
            if (!p.Alive && arena.PlayerRespawnIn > 0f) infoLine = $"RESPAWN IN {Mathf.CeilToInt(arena.PlayerRespawnIn)}";
        }
        else if (match && match.State == MatchTask.Phase.Break)
        {
            topBar.text = $"RANKED   {score}";
            // (the class cards are up now: their title has the countdown and the line under them the ESC hint - nothing over the cards)
            infoLine = CardsUp ? "" : $"Next round in {Mathf.CeilToInt(match.Timer)}s\n<size=75%>Take the headset off (ESC) to deal with the house - rounds start without you</size>";
        }
        else if (match && match.State == MatchTask.Phase.Over)
        {
            topBar.text = $"MATCH OVER   {score}";
            infoLine = $"{(match.RankPoints >= 0 ? "+" : "")}{match.RankPoints} RP\n<size=75%>ESC to take the headset off</size>";
        }
        if (Time.time < announceUntil) infoLine = "";
        info.text = infoLine;

        float left = announceUntil - Time.time;
        announce.text = announceText;
        announce.alpha = Mathf.Clamp01(left / 0.35f);
        float grow = 1f + Mathf.Max(0f, left - (announceLength - 0.2f)) * 2.5f;
        announce.rectTransform.localScale = Vector3.one * grow;

        Layout(feed, -1f);
        Layout(chat, 1f);
        if (stream != null) stream.Update(cardsRoot && cardsRoot.gameObject.activeSelf);
    }

    static void Layout(List<(TextMeshProUGUI text, float until)> list, float direction)
    {
        for (int i = list.Count - 1; i >= 0; i--)
            if (Time.time > list[i].until) { Destroy(list[i].text.gameObject); list.RemoveAt(i); }
        for (int i = 0; i < list.Count; i++)
        {
            list[i].text.rectTransform.anchoredPosition = new Vector2(0f, direction * i * 28f);
            list[i].text.alpha = Mathf.Clamp01(list[i].until - Time.time);
        }
    }

    // ------------------------------------------------------------------ sprites

    static Sprite Round()
    {
        if (round) return round;
        round = MakeSprite(32, (x, y) => Mathf.Clamp01(15.5f - Vector2.Distance(new Vector2(x, y), new Vector2(16f, 16f)) + 0.5f));
        return round;
    }

    /// <summary>Clear in the middle, dark at the edges - the inside of the goggles.</summary>
    static Sprite Vignette()
    {
        if (vignette) return vignette;
        vignette = MakeSprite(128, (x, y) =>
        {
            float dx = (x - 64f) / 64f, dy = (y - 64f) / 64f;
            float d = Mathf.Sqrt(dx * dx * 0.8f + dy * dy * 1.1f);
            return Mathf.Clamp01((d - 0.72f) / 0.4f);
        });
        return vignette;
    }

    static Sprite MakeSprite(int size, System.Func<float, float, float> alpha)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[size * size];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
            px[y * size + x] = new Color32(255, 255, 255, (byte)(alpha(x + 0.5f, y + 0.5f) * 255f));
        tex.SetPixels32(px);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
    }
}
