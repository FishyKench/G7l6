using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The campaign's two screens (made by CampaignRuntime), both drawn in the same taped-paper style as the report card:
///   - the DAY INTRO CARD at the start of every day: "MONDAY - Week 1", the day's twist (two short lines), your money, rank and
///     when Mom comes home. It slides in, stays about 5 seconds and fades out; it never blocks input (no raycasts, the game runs);
///   - the WEEK SUMMARY after Friday's report card (R): a Mon-Fri strip with the grades and match results, the week's money by
///     category, rank, tournament result, ClipHub followers and your best stream, with buttons "Start Week N" and "Quit game"
///     (mouse, or Enter to start). There is no main menu scene, so quitting closes the game.
/// </summary>
public class CampaignUI : MonoBehaviour
{
    public static bool SummaryOpen { get; private set; }

    static readonly Color Ink = new Color32(34, 32, 42, 255);
    static readonly Color Soft = new Color32(34, 32, 42, 165);
    static readonly Color Paper = new Color32(250, 247, 238, 255);
    static readonly Color Green = new Color32(46, 158, 68, 255);
    static readonly Color Orange = new Color32(224, 123, 18, 255);
    static readonly Color Red = new Color32(224, 53, 43, 255);
    static readonly Color Tile = new Color(0.13f, 0.13f, 0.16f, 0.06f);

    TMP_FontAsset font;
    RectTransform canvasRoot;
    CanvasGroup introGroup;
    RectTransform intro;
    bool introShown;
    static Sprite round;

    // summary
    CanvasGroup summaryGroup;
    RectTransform startButton, quitButton;
    Image startBg, quitBg;
    float summaryOpenedAt;

    void Awake()
    {
        SummaryOpen = false;
    }

    void OnDestroy()
    {
        SummaryOpen = false;
    }

    void EnsureCanvas()
    {
        if (canvasRoot) return;
        font = GameHUD.Instance ? GameHUD.Instance.font : null;
        var canvas = UIFactory.CreateCanvas("Campaign Canvas", 57); // above the day HUD (55), below the PC desktop (62) and pause menu (80)
        canvas.transform.SetParent(transform, false);
        canvasRoot = (RectTransform)canvas.transform;
    }

    // ------------------------------------------------------------------ small layout helpers

    TextMeshProUGUI Txt(Transform parent, string text, float size, Color color, TextAlignmentOptions align, float x, float y, float w, float h, bool bold = false, bool wrap = false)
    {
        var t = UIFactory.Text("Text", parent, size, color, align, font);
        t.text = text;
        t.enableWordWrapping = wrap;
        t.overflowMode = TextOverflowModes.Ellipsis; // wrapped text too: never spill out of its box
        if (bold) t.fontStyle = FontStyles.Bold;
        UIFactory.Place(t.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(x, -y), new Vector2(w, h));
        return t;
    }

    Image Box(Transform parent, Color color, float x, float y, float w, float h, bool rounded = false)
    {
        var img = UIFactory.Panel("Box", parent, color);
        if (rounded) { img.sprite = Round(); img.type = Image.Type.Sliced; }
        UIFactory.Place(img.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(x, -y), new Vector2(w, h));
        return img;
    }

    RectTransform Note(string name, Vector2 size, Vector2 at, float tilt, out CanvasGroup group)
    {
        var card = UIFactory.Rect(name, canvasRoot);
        UIFactory.Place(card, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), at, size);
        card.localRotation = Quaternion.Euler(0, 0, tilt);
        group = card.gameObject.AddComponent<CanvasGroup>();
        group.alpha = 0f;
        group.blocksRaycasts = false;
        var shadow = UIFactory.Panel("Shadow", card, new Color(0, 0, 0, 0.3f));
        UIFactory.Fill(shadow.rectTransform, 10, -10, 12, -12);
        var paper = UIFactory.Panel("Paper", card, Paper);
        paper.sprite = HudArt.Get("note_paper");
        UIFactory.Fill(paper.rectTransform);
        var tape = UIFactory.Panel("Tape", card, Color.white);
        tape.sprite = HudArt.Get("tape");
        UIFactory.Place(tape.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), new Vector2(0, -2), new Vector2(150, 40));
        return card;
    }

    // ------------------------------------------------------------------ the day intro card

    /// <summary>Shows the card (once per call; CampaignRuntime calls it a moment after the day starts).</summary>
    public void ShowIntro(Campaign.DayRules rules)
    {
        EnsureCanvas();
        if (intro) Destroy(intro.gameObject);
        StopCoroutine(nameof(IntroRoutine));
        const float W = 760f, H = 292f;
        intro = Note("Day Intro", new Vector2(W, H), new Vector2(0f, 160f), -1f, out introGroup);

        string weekTag = $"<size=48%><color=#E07B12>WEEK {Campaign.Week}</color></size>";
        var title = Txt(intro, $"{rules.name.ToUpperInvariant()}   {weekTag}", 62, Ink, TextAlignmentOptions.Center, 30, 26, W - 60, 76, true);
        title.richText = true;
        for (int i = 0; i < rules.twist.Length && i < 2; i++)
            Txt(intro, rules.twist[i], 25, i == 0 ? Ink : Soft, TextAlignmentOptions.Center, 40, 108 + i * 34, W - 80, 32);

        Box(intro, new Color(0.13f, 0.13f, 0.16f, 0.22f), 40, 190, W - 80, 2);

        float colW = (W - 80) / 3f;
        Txt(intro, "WALLET", 14, Soft, TextAlignmentOptions.Center, 40, 204, colW, 18);
        Txt(intro, Wallet.MoneyText, 28, Green, TextAlignmentOptions.Center, 40, 224, colW, 38, true);

        var rank = Campaign.CurrentRank;
        Txt(intro, "RANK", 14, Soft, TextAlignmentOptions.Center, 40 + colW, 204, colW, 18);
        var rankText = Txt(intro, Campaign.RankName(rank), 28, Ink, TextAlignmentOptions.Center, 40 + colW, 224, colW, 38, true);
        rankText.color = Darken(Campaign.RankColour(rank));
        Txt(intro, $"{Campaign.Rating} RP", 16, Soft, TextAlignmentOptions.Center, 40 + colW, 258, colW, 20);

        Txt(intro, "MOM HOME", 14, Soft, TextAlignmentOptions.Center, 40 + colW * 2, 204, colW, 18);
        Txt(intro, Campaign.HourText(rules.momHomeHour), 28, rules.momHomeHour < 17.99f ? Red : Ink, TextAlignmentOptions.Center, 40 + colW * 2, 224, colW, 38, true);
        if (rules.tournament)
            Txt(intro, "TOURNAMENT " + Campaign.HourText(rules.matchStartHour), 16, Orange, TextAlignmentOptions.Center, 40 + colW * 2 - 30, 258, colW + 60, 20, true);

        StartCoroutine(nameof(IntroRoutine));
    }

    static Color Darken(Color c) => new Color(c.r * 0.72f, c.g * 0.72f, c.b * 0.72f, 1f);

    IEnumerator IntroRoutine()
    {
        var rt = intro;
        var group = introGroup;
        Vector2 home = rt.anchoredPosition;
        for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / 0.45f)
        {
            float e = 1f - (1f - t) * (1f - t);
            group.alpha = e * VrFade();
            rt.anchoredPosition = home + new Vector2(0f, (1f - e) * 40f);
            yield return null;
        }
        rt.anchoredPosition = home;
        for (float t = 0f; t < 5f; t += Time.unscaledDeltaTime)
        {
            group.alpha = VrFade();
            yield return null;
        }
        for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / 0.7f)
        {
            group.alpha = (1f - t) * VrFade();
            yield return null;
        }
        Destroy(rt.gameObject);
        intro = null;
    }

    /// <summary>The card hides while you wear the headset.</summary>
    static float VrFade() => VRHeadset.Worn ? 0f : 1f;

    // ------------------------------------------------------------------ the week summary

    public void ShowSummary()
    {
        if (SummaryOpen) return;
        EnsureCanvas();
        SummaryOpen = true;
        summaryOpenedAt = Time.unscaledTime;
        if (intro) { Destroy(intro.gameObject); intro = null; }
        StopCoroutine(nameof(IntroRoutine));

        var dim = UIFactory.Panel("Summary Dim", canvasRoot, new Color(0f, 0f, 0f, 0.62f));
        UIFactory.Fill(dim.rectTransform);

        const float W = 1180f, H = 920f;
        var card = Note("Week Summary", new Vector2(W, H), Vector2.zero, 0f, out summaryGroup);
        dim.transform.SetAsFirstSibling();
        summaryGroup.blocksRaycasts = false;
        int week = Campaign.Week;

        Txt(card, $"WEEK {week} COMPLETE", 58, Ink, TextAlignmentOptions.Center, 60, 40, W - 120, 72, true);
        Txt(card, "Here is how your week went.", 21, Soft, TextAlignmentOptions.Center, 60, 112, W - 120, 28);

        // --- Mon-Fri strip
        const float tileW = 204f, tileH = 128f, gap = 14f;
        float x0 = (W - (tileW * 5 + gap * 4)) * 0.5f;
        for (int i = 0; i < Campaign.Days; i++)
        {
            float tx = x0 + i * (tileW + gap), ty = 154f;
            var back = Box(card, Tile, tx, ty, tileW, tileH, true);
            string grade = Campaign.GradeOf(i);
            bool played = !string.IsNullOrEmpty(grade);
            Txt(card, Campaign.DayShort[i].ToUpperInvariant(), 17, Soft, TextAlignmentOptions.Center, tx, ty + 8, tileW, 22, true);
            var g = Txt(card, played ? grade : "-", 54, played ? GradeColour(grade) : Soft, TextAlignmentOptions.Center, tx, ty + 28, tileW, 62, true);
            string match = Campaign.MatchOf(i);
            if (i == Campaign.Friday && Campaign.TournamentOutcome != Campaign.TournamentResult.None && Campaign.TournamentOutcome != Campaign.TournamentResult.NotEligible)
                match = "Tournament " + (string.IsNullOrEmpty(Campaign.TournamentScore) ? "" : Campaign.TournamentScore);
            Txt(card, string.IsNullOrEmpty(match) ? (played ? "no match" : "") : match, 15, Ink, TextAlignmentOptions.Center, tx + 6, ty + 92, tileW - 12, 20);
            Txt(card, played ? $"{Campaign.RatingAfter(i)} RP" : "", 14, Soft, TextAlignmentOptions.Center, tx, ty + 107, tileW, 18);
            back.raycastTarget = false;
            g.raycastTarget = false;
        }

        // --- money (left) and progress (right)
        float colTop = 306f, leftX = 70f, rightX = W / 2f + 30f, colW = W / 2f - 100f;
        Txt(card, "MONEY THIS WEEK", 19, Orange, TextAlignmentOptions.MidlineLeft, leftX, colTop, colW, 28, true);
        Box(card, new Color(0.13f, 0.13f, 0.16f, 0.22f), leftX, colTop + 30, colW, 2);
        Campaign.WeekMoney(out var earned, out float spent);
        float y = colTop + 42f;
        float total = 0f;
        void Money(string label, float v)
        {
            total += v;
            Row(card, label, v > 0.001f ? "+" + Wallet.Format(v) : "$0.00", v > 0.001f ? Green : Soft, leftX, y, colW);
            y += 36f;
        }
        Money("Allowance from Mom", earned[(int)Wallet.Category.Allowance]);
        Money("Mom's side jobs", earned[(int)Wallet.Category.Job]);
        Money("Ranked and tournament prizes", earned[(int)Wallet.Category.Prize]);
        Money("ClipHub views", earned[(int)Wallet.Category.Views]);
        Money("LiveLoop tips", earned[(int)Wallet.Category.Tips]);
        Row(card, "Spent in shops", spent > 0.001f ? "-" + Wallet.Format(spent) : "$0.00", spent > 0.001f ? Red : Soft, leftX, y, colW);
        y += 40f;
        Box(card, new Color(0.13f, 0.13f, 0.16f, 0.22f), leftX, y - 6, colW, 2);
        float net = total - spent;
        Row(card, "Saved this week", (net >= 0 ? "+" : "-") + Wallet.Format(Mathf.Abs(net)), net >= 0 ? Green : Red, leftX, y + 2, colW, true);

        Txt(card, "PROGRESS", 19, Orange, TextAlignmentOptions.MidlineLeft, rightX, colTop, colW, 28, true);
        Box(card, new Color(0.13f, 0.13f, 0.16f, 0.22f), rightX, colTop + 30, colW, 2);
        y = colTop + 42f;
        var rank = Campaign.CurrentRank;
        int delta = Campaign.Rating - Campaign.WeekStartRating;
        string rankHex = ColorUtility.ToHtmlStringRGB(Darken(Campaign.RankColour(rank)));
        Row(card, "Rank", $"<color=#{rankHex}>{Campaign.RankName(rank)}</color>  {Campaign.Rating} RP  <size=80%>({(delta >= 0 ? "+" : "")}{delta})</size>", Ink, rightX, y, colW, true, 0.26f);
        y += 36f;
        Row(card, "Tournament", TournamentText(out Color tc), tc, rightX, y, colW, true, 0.34f);
        y += 36f;
        int followers = ClipHub.Followers, gained = followers - Campaign.WeekStartFollowers;
        Row(card, "Followers", $"{followers:N0}  <size=80%>({(gained >= 0 ? "+" : "")}{gained})</size>", Ink, rightX, y, colW, false, 0.34f);
        y += 36f;
        int bestPeak = 0, bestTips = 0;
        string tag = $"(Week {week})";
        foreach (var p in LiveStream.PastStreams)
            if (p.day != null && p.day.Contains(tag) && p.peak >= bestPeak) { bestPeak = p.peak; bestTips = p.tipCents; }
        Row(card, "Best stream", bestPeak > 0 ? $"{bestPeak} viewers  <size=80%>(${bestTips / 100f:0.00} tips)</size>" : "no streams", bestPeak > 0 ? Ink : Soft, rightX, y, colW, false, 0.34f);
        y += 36f;
        Row(card, "Wallet", Wallet.MoneyText, Green, rightX, y, colW, true, 0.34f);

        // --- next week
        float noteY = 624f;
        Box(card, new Color(0.13f, 0.13f, 0.16f, 0.15f), 70, noteY, W - 140, 2);
        var next = Txt(card, $"WEEK {week + 1} IS TOUGHER", 22, Ink, TextAlignmentOptions.Center, 70, noteY + 14, W - 140, 30, true);
        next.richText = true;
        Txt(card, $"More chaos, a stricter Mom and sharper bots. The tournament will need {Campaign.RankName(Campaign.GateRankFor(week + 1))}. " +
                  "Your money, gear, rank and followers carry over.", 20, Soft, TextAlignmentOptions.Top, 120, noteY + 50, W - 240, 62, false, true);

        // --- buttons
        const float bw = 380f, bh = 76f;
        startBg = Box(card, Green, W / 2f - bw - 18f, 770, bw, bh, true);
        startButton = startBg.rectTransform;
        Txt(startButton, $"START WEEK {week + 1}", 32, Color.white, TextAlignmentOptions.Center, 0, 22, bw, 40, true);
        quitBg = Box(card, new Color(0.13f, 0.13f, 0.16f, 0.14f), W / 2f + 18f, 770, 300, bh, true);
        quitButton = quitBg.rectTransform;
        Txt(quitButton, "QUIT GAME", 28, Ink, TextAlignmentOptions.Center, 0, 24, 300, 40, true);
        Txt(card, "Click a button  -  or press Enter to start the next week", 16, Soft, TextAlignmentOptions.Center, 60, 862, W - 120, 22);

        StartCoroutine(SummaryIn());
    }

    IEnumerator SummaryIn()
    {
        var card = summaryGroup.transform as RectTransform;
        for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / 0.4f)
        {
            float e = 1f - (1f - t) * (1f - t);
            summaryGroup.alpha = e;
            card.localScale = Vector3.one * Mathf.Lerp(0.94f, 1f, e);
            yield return null;
        }
        summaryGroup.alpha = 1f;
        card.localScale = Vector3.one;
    }

    void Row(Transform parent, string label, string value, Color valueColour, float x, float y, float w, bool bold = false, float labelFrac = 0.66f)
    {
        Txt(parent, label, 21, Ink, TextAlignmentOptions.MidlineLeft, x, y, w * labelFrac, 30);
        var v = Txt(parent, value, 21, valueColour, TextAlignmentOptions.MidlineRight, x + w * labelFrac, y, w * (1f - labelFrac), 30, bold);
        v.richText = true;
    }

    string TournamentText(out Color colour)
    {
        string score = Campaign.TournamentScore;
        switch (Campaign.TournamentOutcome)
        {
            case Campaign.TournamentResult.First: colour = Green; return $"1st place  +${Campaign.Prize1st}";
            case Campaign.TournamentResult.Second: colour = Green; return $"2nd place  +${Campaign.Prize2nd}";
            case Campaign.TournamentResult.Out: colour = Red; return $"Out {score}  no prize";
            case Campaign.TournamentResult.Missed: colour = Red; return "Did not play";
            case Campaign.TournamentResult.NotEligible: colour = Red; return $"Not qualified";
            default: colour = Soft; return "-";
        }
    }

    static Color GradeColour(string grade)
    {
        char c = string.IsNullOrEmpty(grade) ? 'F' : grade[0];
        return c == 'A' ? Green : c == 'B' ? new Color32(70, 140, 60, 255) : c == 'C' ? Orange : Red;
    }

    // ------------------------------------------------------------------ summary input

    void Update()
    {
        if (!SummaryOpen || !startButton) return;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        bool onStart = RectTransformUtility.RectangleContainsScreenPoint(startButton, Input.mousePosition, null);
        bool onQuit = RectTransformUtility.RectangleContainsScreenPoint(quitButton, Input.mousePosition, null);
        startBg.color = onStart ? new Color32(36, 130, 56, 255) : Green;
        quitBg.color = onQuit ? new Color(0.13f, 0.13f, 0.16f, 0.26f) : new Color(0.13f, 0.13f, 0.16f, 0.14f);
        if (Time.unscaledTime - summaryOpenedAt < 0.5f) return; // no accidental click right as it opens
        if ((Input.GetMouseButtonDown(0) && onStart) || Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) StartNextWeek();
        else if (Input.GetMouseButtonDown(0) && onQuit) QuitGame();
    }

    void StartNextWeek()
    {
        SummaryOpen = false;
        Campaign.QueueNextWeek();
        DayManager.RestartDay();
    }

    static void QuitGame()
    {
        Time.timeScale = 1f;
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    /// <summary>A soft white disc; stretched (sliced) it makes rounded panels.</summary>
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
