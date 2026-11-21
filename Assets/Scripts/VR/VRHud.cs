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
    static Sprite round, vignette;

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

        topBar = Text(root, "", 26, Color.white, TextAlignmentOptions.Center, new Vector2(0, -140), new Vector2(1100, 40), top: true);
        topBar.fontStyle = FontStyles.Bold;
        announce = Text(root, "", 84, Color.white, TextAlignmentOptions.Center, new Vector2(0, 130), new Vector2(1400, 110));
        announce.fontStyle = FontStyles.Bold;
        info = Text(root, "", 26, new Color(1f, 1f, 1f, 0.85f), TextAlignmentOptions.Center, new Vector2(0, 50), new Vector2(1200, 80));

        feedRoot = UIFactory.Rect("Kill feed", root);
        UIFactory.Place(feedRoot, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(40, -60), new Vector2(520, 160));
        chatRoot = UIFactory.Rect("Chat", root);
        UIFactory.Place(chatRoot, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(40, 230), new Vector2(520, 150));

        Text(root, "WASD move  -  SHIFT sprint  -  CTRL slide  -  SPACE jump / vault  -  RIGHT CLICK aim  -  CLICK shoot  -  R reload  -  ESC headset off  -  P pause",
            16, new Color(1f, 1f, 1f, 0.45f), TextAlignmentOptions.Center, new Vector2(0, 14), new Vector2(1500, 24), bottom: true);

        cover = UIFactory.Panel("Fade", root, Color.black);
        UIFactory.Fill(cover.rectTransform);
        coverText = Text(cover.rectTransform, "", 34, Cyan, TextAlignmentOptions.Center, Vector2.zero, new Vector2(900, 60), centre: true);
        canvas.enabled = false;
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
        if (!show || !p) return;
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
        if (arena.State == VRArena.Mode.Fighting)
        {
            topBar.text = $"RANKED  -  ROUND {(match ? match.RoundNumber : 1)}   {score}   -   {Mathf.CeilToInt(Mathf.Max(0f, arena.TimeLeft))}s";
            if (!p.Alive) infoLine = "ELIMINATED  -  your team fights on";
        }
        else if (arena.State == VRArena.Mode.Practice)
        {
            topBar.text = $"WARM-UP DEATHMATCH  -  KILLS: {p.Kills}  -  RANKED AT 4:00 PM";
        }
        else if (match && match.State == MatchTask.Phase.Break)
        {
            topBar.text = $"RANKED   {score}";
            infoLine = $"Next round in {Mathf.CeilToInt(match.Timer)}s\n<size=75%>Take the headset off (ESC) to deal with the house - rounds start without you</size>";
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
