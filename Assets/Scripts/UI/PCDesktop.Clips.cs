using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Clip Studio (desktop icon "Clip Studio"): cuts real replays of the rounds you played in the VR arena and uploads
/// them to ClipHub.
///   - left: the recorded rounds (last 5) and your numbers for the selected one;
///   - middle: the live replay preview (a camera in the real arena map filming stand-in figures), play / pause / scrub,
///     and the timeline of the round: kills, multi-kills, captures, deaths and dead time - click a block to keep or cut it;
///   - right: camera (first / third / orbit), thumbnail (3 frames), quality score + expected views, and Upload.
///   - the title is typed with the keyboard while the window is in front and the title box is clicked.
/// Rendering only happens while the window is open and the arena is idle (never during a live round / in the headset).
/// </summary>
public partial class PCDesktop
{
    const float ClipW = ScreenW - 40f, ClipH = ScreenH - TaskbarH - 30f - 36f;
    const int ClipTitleMax = 36;
    const float MinClipSeconds = 3f;

    static readonly Color ClipBg = new Color32(20, 17, 34, 255);
    static readonly Color ClipAccent = new Color32(190, 80, 255, 255);
    static readonly Color ClipPanel = new Color(1f, 1f, 1f, 0.06f);

    struct ClipButton { public RectTransform rt; public Image bg; public TextMeshProUGUI text; }
    struct RoundRow { public RectTransform rt; public Image bg, bar; public TextMeshProUGUI title, sub, score; }
    class BlockUi { public RectTransform rt; public Image bg, strip; public TextMeshProUGUI label; }
    struct Seg { public float a, b; }

    Window clipsWin;
    GameObject clipsMain, clipsEmpty;
    readonly RoundRow[] roundRows = new RoundRow[Replay.MaxRounds];
    readonly List<BlockUi> blocks = new List<BlockUi>();
    readonly List<ReplayRound> clipRounds = new List<ReplayRound>();
    readonly Texture2D[] thumbs = new Texture2D[3];
    readonly float[] thumbTimes = new float[3];
    readonly RawImage[] thumbImages = new RawImage[3];
    readonly Image[] thumbFrames = new Image[3];
    readonly TextMeshProUGUI[] statValues = new TextMeshProUGUI[6];
    readonly ClipButton[] camButtons = new ClipButton[3];
    readonly TextMeshProUGUI[] breakdownTexts = new TextMeshProUGUI[6];
    ClipButton clipPlayButton, clipUploadButton, clipViewButton;
    RectTransform timeline, scrub, scrubHit, titleField, previewRect, playhead, scrubFill;
    RawImage previewImage;
    GameObject previewOverlay;
    TextMeshProUGUI previewMessage, previewTime, previewCam, timelineHint, titleText, titleCount, statusText, uploadMessage;
    TextMeshProUGUI qualityNumber, qualityWord, expectedText, expectedSub;
    Image qualityBar;
    RectTransform qualityBarFill;

    ReplayRound clipRound;
    List<ReplayMoment> moments = new List<ReplayMoment>();
    readonly List<Seg> segs = new List<Seg>();
    float keptDur, clipU, nextRender, thumbsDirtyAt = -1f;
    bool clipPlaying, needRender, clipsDirty = true, scrubbing, titleFocus, titleEdited, thumbsLoaded;
    int clipCam = 0, thumbPick = 1, uploadedId;
    string clipTitle = "";
    int quality;
    int kKills, kHeads, kCaps, kDeaths;

    // ------------------------------------------------------------------ building

    void BuildClips()
    {
        var big = new Vector2(ScreenW - 40f, ScreenH - TaskbarH - 30f);
        clipsWin = MakeWindow("clips", "Clip Studio", big, ClipBg);
        var c = clipsWin.content;
        Replay.Changed += MarkClipsDirty;

        // ---- empty state
        clipsEmpty = UIFactory.Rect("Empty", c).gameObject;
        var er = (RectTransform)clipsEmpty.transform;
        UIFactory.Fill(er);
        var glow = UIFactory.Panel("Glow", er, new Color(ClipAccent.r, ClipAccent.g, ClipAccent.b, 0.14f));
        glow.sprite = Round();
        UIFactory.Place(glow.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 20), new Vector2(760, 520));
        var film = UIFactory.Rect("Film", er);
        UIFactory.Place(film, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 120), new Vector2(64, 64));
        film.localScale = Vector3.one * 1.9f;
        DrawClips(film);
        Txt(er, "No recordings yet", 36, Color.white, TextAlignmentOptions.Center, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 20), new Vector2(800, 52), false, true);
        Txt(er, "Play a ranked match to record clips.", 24, new Color(1f, 1f, 1f, 0.85f), TextAlignmentOptions.Center, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -26), new Vector2(800, 36));
        Txt(er, "Every round you play in the VR arena is recorded - the warm-up counts too. The last 5 rounds are kept, so you can cut your best moments here and upload them to ClipHub.",
            18, new Color(1f, 1f, 1f, 0.6f), TextAlignmentOptions.Top, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 1f), new Vector2(0, -52), new Vector2(660, 80), true);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        Txt(er, "[editor] press F11 in the house to make up a test round", 15, new Color(1f, 0.8f, 0.4f, 0.8f), TextAlignmentOptions.Center, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0, 26), new Vector2(700, 24));
#endif

        // ---- main editor
        clipsMain = UIFactory.Rect("Editor", c).gameObject;
        var m = (RectTransform)clipsMain.transform;
        UIFactory.Fill(m);

        // left: recordings + stats
        var left = RPanel(m, ClipPanel);
        TopLeft(left.rectTransform, 16, 14, 292, 614);
        TxtTL(left.transform, "RECORDINGS", 13, new Color(1f, 1f, 1f, 0.55f), TextAlignmentOptions.MidlineLeft, 16, 10, 240, 22).characterSpacing = 3f;
        for (int i = 0; i < roundRows.Length; i++)
        {
            var row = new RoundRow();
            row.bg = RPanel(left.transform, new Color(1f, 1f, 1f, 0f));
            row.rt = row.bg.rectTransform;
            TopLeft(row.rt, 8, 38 + i * 66, 276, 60);
            row.bar = UIFactory.Panel("Bar", row.rt, Color.white);
            TopLeft(row.bar.rectTransform, 8, 10, 5, 40);
            row.title = TxtTL(row.rt, "", 16, Color.white, TextAlignmentOptions.MidlineLeft, 22, 6, 156, 26, false, true);
            row.sub = TxtTL(row.rt, "", 13, new Color(1f, 1f, 1f, 0.55f), TextAlignmentOptions.MidlineLeft, 22, 32, 250, 22);
            row.score = Txt(row.rt, "", 15, Color.white, TextAlignmentOptions.MidlineRight, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-12, -6), new Vector2(92, 26), false, true);
            roundRows[i] = row;
        }
        var rule = UIFactory.Panel("Rule", left.transform, new Color(1f, 1f, 1f, 0.1f));
        TopLeft(rule.rectTransform, 12, 376, 268, 2);
        TxtTL(left.transform, "YOUR NUMBERS", 13, new Color(1f, 1f, 1f, 0.55f), TextAlignmentOptions.MidlineLeft, 16, 388, 240, 22).characterSpacing = 3f;
        string[] statNames = { "Kills", "Deaths", "Headshots", "Captures", "Best streak", "Length" };
        for (int i = 0; i < 6; i++)
        {
            float sx = 14 + (i % 2) * 136, sy = 418 + (i / 2) * 62;
            var cell = RPanel(left.transform, new Color(1f, 1f, 1f, 0.05f));
            TopLeft(cell.rectTransform, sx, sy, 128, 54);
            statValues[i] = TxtTL(cell.transform, "", 24, Color.white, TextAlignmentOptions.MidlineLeft, 12, 4, 108, 32, false, true);
            TxtTL(cell.transform, statNames[i], 12, new Color(1f, 1f, 1f, 0.55f), TextAlignmentOptions.MidlineLeft, 12, 34, 108, 16);
        }

        // middle: preview
        var frame = RPanel(m, new Color(0f, 0f, 0f, 0.55f));
        TopLeft(frame.rectTransform, 320, 10, 648, 368);
        previewImage = UIFactory.Rect("Preview", m).gameObject.AddComponent<RawImage>();
        previewImage.raycastTarget = false;
        previewRect = previewImage.rectTransform;
        TopLeft(previewRect, 324, 14, 640, 360);
        previewImage.color = Color.white;
        previewOverlay = UIFactory.Rect("Overlay", m).gameObject;
        var ov = (RectTransform)previewOverlay.transform;
        TopLeft(ov, 324, 14, 640, 360);
        var ovBg = UIFactory.Panel("Bg", ov, new Color(0.07f, 0.06f, 0.12f, 0.92f));
        UIFactory.Fill(ovBg.rectTransform);
        previewMessage = Txt(ov, "", 20, new Color(1f, 1f, 1f, 0.85f), TextAlignmentOptions.Center, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(520, 120), true);
        var recChip = RPanel(m, new Color(0f, 0f, 0f, 0.55f));
        TopLeft(recChip.rectTransform, 336, 26, 92, 26);
        Dot(recChip.rectTransform, new Color32(255, 70, 80, 255), new Vector2(-30, 0), 10);
        TxtTL(recChip.transform, "REPLAY", 13, Color.white, TextAlignmentOptions.MidlineLeft, 32, 0, 58, 26, false, true).characterSpacing = 2f;
        var camChip = RPanel(m, new Color(0f, 0f, 0f, 0.55f));
        TopLeft(camChip.rectTransform, 336, 338, 130, 26);
        previewCam = TxtTL(camChip.transform, "", 13, Color.white, TextAlignmentOptions.Center, 4, 0, 122, 26);
        var timeChip = RPanel(m, new Color(0f, 0f, 0f, 0.55f));
        TopLeft(timeChip.rectTransform, 850, 338, 102, 26);
        previewTime = TxtTL(timeChip.transform, "", 13, Color.white, TextAlignmentOptions.Center, 4, 0, 94, 26);

        // transport: play / pause, scrub bar
        clipPlayButton = MakeClipButton(m, "Play", 324, 386, 78, 30, new Color(1f, 1f, 1f, 0.12f), 15);
        var scrubBg = RPanel(m, new Color(1f, 1f, 1f, 0.12f));
        scrub = scrubBg.rectTransform;
        TopLeft(scrub, 418, 396, 546, 10);
        scrubHit = UIFactory.Rect("Scrub hit area", m);
        TopLeft(scrubHit, 418, 386, 546, 30);
        var fill = RPanel(scrub, ClipAccent);
        scrubFill = fill.rectTransform;
        scrubFill.anchorMin = Vector2.zero;
        scrubFill.anchorMax = new Vector2(0f, 1f);
        scrubFill.pivot = new Vector2(0f, 0.5f);
        scrubFill.offsetMin = scrubFill.offsetMax = Vector2.zero;

        // timeline
        TxtTL(m, "TIMELINE", 13, new Color(1f, 1f, 1f, 0.55f), TextAlignmentOptions.MidlineLeft, 324, 424, 90, 22).characterSpacing = 3f;
        ReplayMoment.Type[] legend = { ReplayMoment.Type.Multi, ReplayMoment.Type.Kill, ReplayMoment.Type.Capture, ReplayMoment.Type.Death, ReplayMoment.Type.Action, ReplayMoment.Type.Dead };
        float lx = 404f;
        foreach (var lt in legend)
        {
            var sw = RPanel(m, ReplayMoment.ColorOf(lt));
            TopLeft(sw.rectTransform, lx, 430, 10, 10);
            string name = ReplayMoment.NameOf(lt);
            float lw = name.Length * 6.6f + 4f;
            TxtTL(m, name, 12, new Color(1f, 1f, 1f, 0.75f), TextAlignmentOptions.MidlineLeft, lx + 15, 424, lw, 22);
            lx += 15 + lw + 10;
        }
        var strip = RPanel(m, new Color(0f, 0f, 0f, 0.4f));
        timeline = strip.rectTransform;
        TopLeft(timeline, 324, 448, 640, 52);
        for (int i = 0; i < 40; i++)
        {
            var b = new BlockUi();
            b.bg = RPanel(timeline, Color.white);
            b.rt = b.bg.rectTransform;
            b.strip = UIFactory.Panel("Strip", b.rt, Color.white);
            b.label = Txt(b.rt, "", 12, new Color32(20, 18, 32, 255), TextAlignmentOptions.Center, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, false, true);
            UIFactory.Fill(b.label.rectTransform, 2, 2, 0, 5);
            b.rt.gameObject.SetActive(false);
            blocks.Add(b);
        }
        var ph = UIFactory.Panel("Playhead", timeline, Color.white);
        playhead = ph.rectTransform;
        timelineHint = TxtTL(m, "", 13, new Color(1f, 1f, 1f, 0.6f), TextAlignmentOptions.MidlineLeft, 324, 504, 640, 20);

        // title
        TxtTL(m, "TITLE", 13, new Color(1f, 1f, 1f, 0.55f), TextAlignmentOptions.MidlineLeft, 324, 532, 90, 20).characterSpacing = 3f;
        var field = RPanel(m, new Color(1f, 1f, 1f, 0.09f));
        titleField = field.rectTransform;
        TopLeft(titleField, 324, 554, 640, 40);
        titleText = TxtTL(field.transform, "", 20, Color.white, TextAlignmentOptions.MidlineLeft, 14, 0, 540, 40);
        titleCount = Txt(field.transform, "", 13, new Color(1f, 1f, 1f, 0.45f), TextAlignmentOptions.MidlineRight, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-12, 0), new Vector2(70, 40));
        statusText = TxtTL(m, "", 14, new Color(1f, 1f, 1f, 0.7f), TextAlignmentOptions.TopLeft, 324, 600, 640, 34, true);

        // right column: camera
        TxtTL(m, "CAMERA", 13, new Color(1f, 1f, 1f, 0.55f), TextAlignmentOptions.MidlineLeft, 984, 14, 200, 20).characterSpacing = 3f;
        string[] camNames = { "First person", "Third person", "Orbit" };
        for (int i = 0; i < 3; i++) camButtons[i] = MakeClipButton(m, camNames[i], 984 + i * 102, 38, 96, 36, new Color(1f, 1f, 1f, 0.1f), 14);

        // thumbnails
        TxtTL(m, "THUMBNAIL  -  pick a frame", 13, new Color(1f, 1f, 1f, 0.55f), TextAlignmentOptions.MidlineLeft, 984, 90, 300, 20).characterSpacing = 2f;
        string[] thumbNames = { "Opening", "Best moment", "Finish" };
        for (int i = 0; i < 3; i++)
        {
            float tx = 984 + i * 102;
            thumbFrames[i] = RPanel(m, new Color(1f, 1f, 1f, 0.1f));
            TopLeft(thumbFrames[i].rectTransform, tx - 3, 112 - 3, 102, 60);
            var raw = UIFactory.Rect("Thumb", m).gameObject.AddComponent<RawImage>();
            raw.raycastTarget = false;
            TopLeft(raw.rectTransform, tx, 112, 96, 54);
            raw.enabled = false;
            thumbImages[i] = raw;
            TxtTL(m, thumbNames[i], 12, new Color(1f, 1f, 1f, 0.6f), TextAlignmentOptions.Top, tx - 4, 172, 104, 18);
        }

        // quality card
        var q = RPanel(m, ClipPanel);
        TopLeft(q.rectTransform, 984, 200, 300, 306);
        TxtTL(q.transform, "QUALITY SCORE", 13, new Color(1f, 1f, 1f, 0.55f), TextAlignmentOptions.MidlineLeft, 16, 10, 240, 22).characterSpacing = 3f;
        qualityNumber = TxtTL(q.transform, "0", 56, Color.white, TextAlignmentOptions.MidlineLeft, 16, 32, 110, 70, false, true);
        qualityNumber.overflowMode = TextOverflowModes.Overflow;
        qualityWord = TxtTL(q.transform, "", 24, ClipAccent, TextAlignmentOptions.MidlineLeft, 130, 32, 156, 70, false, true);
        qualityBarFill = UIFactory.Bar("Quality bar", q.transform, new Color(1f, 1f, 1f, 0.12f), ClipAccent, out qualityBar);
        var qbg = (RectTransform)qualityBarFill.parent;
        TopLeft(qbg, 16, 108, 268, 10);
        for (int i = 0; i < 6; i++)
        {
            breakdownTexts[i] = TxtTL(q.transform, "", 14, new Color(1f, 1f, 1f, 0.8f), TextAlignmentOptions.MidlineLeft, 16 + (i % 2) * 136, 128 + (i / 2) * 22, 132, 22);
        }
        var qrule = UIFactory.Panel("Rule", q.transform, new Color(1f, 1f, 1f, 0.1f));
        TopLeft(qrule.rectTransform, 12, 202, 276, 2);
        TxtTL(q.transform, "EXPECTED VIEWS", 13, new Color(1f, 1f, 1f, 0.55f), TextAlignmentOptions.MidlineLeft, 16, 212, 260, 22).characterSpacing = 3f;
        expectedText = TxtTL(q.transform, "", 26, Color.white, TextAlignmentOptions.MidlineLeft, 16, 236, 268, 34, false, true);
        expectedText.enableAutoSizing = true;
        expectedText.fontSizeMin = 16;
        expectedText.fontSizeMax = 26;
        expectedSub = TxtTL(q.transform, "", 13, new Color(1f, 1f, 1f, 0.55f), TextAlignmentOptions.TopLeft, 16, 272, 268, 32, true);

        // upload
        clipUploadButton = MakeClipButton(m, "UPLOAD TO CLIPHUB", 984, 516, 300, 54, ClipAccent, 20);
        uploadMessage = TxtTL(m, "", 14, new Color(1f, 1f, 1f, 0.7f), TextAlignmentOptions.TopLeft, 984, 576, 300, 54, true);
        clipViewButton = MakeClipButton(m, "View on ClipHub", 984, 600, 300, 30, new Color(1f, 1f, 1f, 0.12f), 14);
        clipViewButton.rt.gameObject.SetActive(false);
    }

    ClipButton MakeClipButton(Transform parent, string label, float x, float y, float w, float h, Color color, float size)
    {
        var b = new ClipButton();
        b.bg = RPanel(parent, color);
        b.rt = b.bg.rectTransform;
        TopLeft(b.rt, x, y, w, h);
        b.text = Txt(b.rt, label, size, Color.white, TextAlignmentOptions.Center, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, false, true);
        UIFactory.Fill(b.text.rectTransform, 4, 4, 0, 0);
        return b;
    }

    /// <summary>Desktop icon: a film strip with a play arrow.</summary>
    static void DrawClips(RectTransform r)
    {
        Shape(r, new Color32(190, 80, 255, 255), Vector2.zero, new Vector2(60, 60), true);
        Shape(r, new Color32(34, 24, 60, 255), Vector2.zero, new Vector2(52, 52), true);
        for (int i = 0; i < 4; i++)
        {
            Shape(r, new Color32(190, 80, 255, 255), new Vector2(-22, 18 - i * 12), new Vector2(5, 7));
            Shape(r, new Color32(190, 80, 255, 255), new Vector2(22, 18 - i * 12), new Vector2(5, 7));
        }
        Shape(r, new Color32(255, 255, 255, 255), Vector2.zero, new Vector2(30, 24), true);
        LogoText(r, ">", 22, new Color32(150, 50, 215, 255));
    }

    // ------------------------------------------------------------------ open / close

    void OpenClips()
    {
        Focus(Opened(clipsWin));
        clipsDirty = true;
    }

    void MarkClipsDirty() => clipsDirty = true;

    void ClipsDestroy()
    {
        Replay.Changed -= MarkClipsDirty;
        if (clipHubPage != null) clipHubPage.Dispose();
        ClearThumbs();
    }

    void ClearThumbs()
    {
        for (int i = 0; i < thumbs.Length; i++)
        {
            if (thumbs[i]) Destroy(thumbs[i]);
            thumbs[i] = null;
        }
    }

    // ------------------------------------------------------------------ selection / maths

    static string Fmt(float seconds)
    {
        int s = Mathf.Max(0, Mathf.RoundToInt(seconds));
        return $"{s / 60}:{s % 60:00}";
    }

    void RefreshClips()
    {
        clipsDirty = false;
        clipRounds.Clear();
        var all = Replay.Rounds;
        for (int i = all.Count - 1; i >= 0; i--) clipRounds.Add(all[i]); // newest first
        bool any = clipRounds.Count > 0;
        clipsMain.SetActive(any);
        clipsEmpty.SetActive(!any);
        if (!any)
        {
            clipRound = null;
            titleFocus = false;
            return;
        }
        if (clipRound == null || !clipRounds.Contains(clipRound)) SelectRound(clipRounds[0]);
        for (int i = 0; i < roundRows.Length; i++)
        {
            var row = roundRows[i];
            row.rt.gameObject.SetActive(i < clipRounds.Count);
            if (i >= clipRounds.Count) continue;
            var r = clipRounds[i];
            ReplayMoment.Stats(r, out int k, out int dth, out _, out _, out _);
            row.title.text = r.Title;
            row.sub.text = $"{r.stamp}  -  {k} kill{(k == 1 ? "" : "s")}";
            row.score.text = $"{r.scoreYou}-{r.scoreThem}";
            row.score.color = r.won ? new Color32(110, 230, 150, 255) : new Color32(255, 120, 120, 255);
            row.bar.color = r.won ? new Color32(110, 230, 150, 255) : new Color32(255, 120, 120, 255);
        }
    }

    void SelectRound(ReplayRound r)
    {
        clipRound = r;
        moments = ReplayMoment.Build(r);
        if (moments.Count > blocks.Count) moments.RemoveRange(blocks.Count, moments.Count - blocks.Count);
        clipU = 0f;
        clipPlaying = false;
        titleEdited = false;
        titleFocus = false;
        uploadedId = 0;
        thumbPick = 1;
        ClearThumbs();
        thumbsLoaded = false;
        var stage = ReplayStage.Get();
        stage.Unload();
        RecomputeClip();
        if (!titleEdited) clipTitle = AutoTitle();
        RecomputeClip();
        needRender = true;
        thumbsDirtyAt = Time.unscaledTime;
    }

    string AutoTitle()
    {
        ReplayMoment best = null;
        foreach (var m in moments)
            if (m.keep && m.Highlight && (best == null || m.type > best.type || (m.type == best.type && m.kills > best.kills))) best = m;
        string where = clipRound.ranked ? "ranked" : "warm-up";
        if (best == null) return "Ranked Arena moment";
        switch (best.type)
        {
            case ReplayMoment.Type.Multi:
                return (best.kills == 2 ? "Double kill" : best.kills == 3 ? "Triple kill" : best.kills == 4 ? "Quad kill" : "Rampage") + " in " + where;
            case ReplayMoment.Type.Kill: return (best.heads > 0 ? "Headshot" : "Clean pick") + " in " + where;
            case ReplayMoment.Type.Capture: return "Clutch capture in " + where;
            case ReplayMoment.Type.Death: return "That ended badly";
            default: return "Chaos in " + where;
        }
    }

    float ClipToRound(float u)
    {
        if (segs.Count == 0) return 0f;
        foreach (var s in segs)
        {
            float len = s.b - s.a;
            if (u <= len) return s.a + Mathf.Max(0f, u);
            u -= len;
        }
        return segs[segs.Count - 1].b;
    }

    float RoundToClip(float t)
    {
        float acc = 0f;
        foreach (var s in segs)
        {
            if (t < s.a) return acc;
            if (t <= s.b) return acc + (t - s.a);
            acc += s.b - s.a;
        }
        return acc;
    }

    /// <summary>Kept moments -> segments, stats, quality score, thumbnail frames, texts. Call after any edit.</summary>
    void RecomputeClip()
    {
        segs.Clear();
        keptDur = 0f;
        kKills = kHeads = kCaps = kDeaths = 0;
        int actionPoints = 0, keptMoments = 0;
        float deadKept = 0f;
        foreach (var m in moments)
        {
            if (!m.keep) continue;
            segs.Add(new Seg { a = m.start, b = m.end });
            keptDur += m.Length;
            if (m.Highlight)
            {
                keptMoments++;
                kKills += m.kills; kHeads += m.heads; kCaps += m.captures; kDeaths += m.deaths;
                actionPoints += m.kills * 9 + m.heads * 4 + (m.kills >= 2 ? (m.kills - 1) * 8 : 0) + m.captures * 8 + (m.deaths > 0 ? 3 : 0) + (m.type == ReplayMoment.Type.Action ? 5 : 0);
            }
            else deadKept += m.Length;
        }
        clipU = Mathf.Clamp(clipU, 0f, keptDur);

        // thumbnail frames: the opening, the best moment, the finish
        ReplayMoment bestM = null, firstM = null, lastM = null;
        foreach (var m in moments)
        {
            if (!m.keep) continue;
            if (firstM == null) firstM = m;
            lastM = m;
            if (m.Highlight && (bestM == null || m.type > bestM.type || (m.type == bestM.type && m.kills > bestM.kills))) bestM = m;
        }
        if (firstM != null)
        {
            thumbTimes[0] = Mathf.Clamp(firstM.start + Mathf.Min(1.4f, firstM.Length * 0.4f), firstM.start, firstM.end);
            thumbTimes[1] = bestM != null ? Mathf.Clamp(bestM.peak, bestM.start, bestM.end) : (firstM.start + firstM.end) * 0.5f;
            thumbTimes[2] = Mathf.Max(lastM.start, lastM.end - 0.7f);
        }

        // quality score
        int action = Mathf.Min(70, actionPoints);
        int pacing = keptDur > 0.1f ? -Mathf.RoundToInt(25f * deadKept / keptDur) : 0;
        int length = keptDur < 8f ? -Mathf.RoundToInt((8f - keptDur) * 2f) : keptDur > 45f ? -Mathf.RoundToInt((keptDur - 45f) * 0.8f) : (keptDur >= 15f ? 6 : 3);
        int titleLen = clipTitle.Trim().Length;
        int title = titleLen >= 8 ? 6 : titleLen >= 3 ? 3 : -4;
        int camera = clipCam == 2 ? 6 : clipCam == 1 ? 3 : 4;
        int thumb = 0;
        if (firstM != null)
        {
            float tt = thumbTimes[Mathf.Clamp(thumbPick, 0, 2)];
            var inMoment = moments.Find(m => m.keep && tt >= m.start - 0.01f && tt <= m.end + 0.01f);
            if (inMoment != null && (inMoment.type == ReplayMoment.Type.Kill || inMoment.type == ReplayMoment.Type.Multi || inMoment.type == ReplayMoment.Type.Capture)) thumb = 4;
        }
        quality = keptDur < 0.5f ? 0 : Mathf.Clamp(action + pacing + length + title + camera + thumb, 0, 100);
        string S(int v) => v >= 0 ? "+" + v : v.ToString();
        string[] parts = { $"Action  {S(action)}", $"Pacing  {S(pacing)}", $"Length  {S(length)}", $"Title  {S(title)}", $"Camera  {S(camera)}", $"Thumb  {S(thumb)}" };
        for (int i = 0; i < 6; i++) breakdownTexts[i].text = parts[i];
        string word = quality < 25 ? "Rough" : quality < 45 ? "Decent" : quality < 65 ? "Good" : quality < 82 ? "Great" : "Viral";
        qualityNumber.text = quality.ToString();
        qualityWord.text = word;
        UIFactory.SetBar(qualityBarFill, quality / 100f);
        ClipHub.ExpectedRange(quality, ClipHub.Followers, out int lo, out int hi);
        expectedText.text = keptDur < 0.5f ? "-" : $"{lo:N0} - {hi:N0}";
        expectedSub.text = ClipHub.IsPartner
            ? "Partner: every 500 views pay $1."
            : $"Followers {ClipHub.Followers}/{ClipHub.PartnerFollowers}. Partners earn $1 per 500 views.";

        // stats of the whole round (left panel)
        if (clipRound != null)
        {
            ReplayMoment.Stats(clipRound, out int k, out int d, out int h, out int c, out int streak);
            statValues[0].text = k.ToString(); statValues[1].text = d.ToString(); statValues[2].text = h.ToString();
            statValues[3].text = c.ToString(); statValues[4].text = streak > 0 ? "x" + streak : "-"; statValues[5].text = Fmt(clipRound.duration);
        }

        if (keptDur < MinClipSeconds) statusText.text = "<color=#FFC04A>Keep at least 3 seconds of the round to upload a clip.</color>";
        else statusText.text = $"{keptMoments} moment{(keptMoments == 1 ? "" : "s")} kept  -  {Fmt(keptDur)} long  -  {kKills} kill{(kKills == 1 ? "" : "s")}, {kHeads} headshot{(kHeads == 1 ? "" : "s")}, {kCaps} capture{(kCaps == 1 ? "" : "s")}";
        UpdateTitleText();
        LayoutTimeline();
        for (int i = 0; i < 3; i++) camButtons[i].bg.color = clipCam == i ? new Color(ClipAccent.r, ClipAccent.g, ClipAccent.b, 0.85f) : new Color(1f, 1f, 1f, 0.1f);
        previewCam.text = clipCam == 0 ? "First person" : clipCam == 1 ? "Third person" : "Orbit cam";
        needRender = true;
    }

    void MarkEdited()
    {
        uploadedId = 0;
        uploadMessage.text = "";
        clipViewButton.rt.gameObject.SetActive(false);
    }

    void LayoutTimeline()
    {
        float dur = clipRound != null ? Mathf.Max(1f, clipRound.duration) : 1f;
        const float pad = 4f, width = 632f;
        for (int i = 0; i < blocks.Count; i++)
        {
            var b = blocks[i];
            bool used = i < moments.Count;
            b.rt.gameObject.SetActive(used);
            if (!used) continue;
            var m = moments[i];
            float x = pad + m.start / dur * width;
            float w = Mathf.Max(4f, m.Length / dur * width - 2f);
            TopLeft(b.rt, x, 4, w, 44);
            Color col = ReplayMoment.ColorOf(m.type);
            b.bg.color = m.keep ? col : new Color(0.16f, 0.16f, 0.22f, 1f);
            b.strip.color = col;
            var sr = b.strip.rectTransform;
            sr.anchorMin = new Vector2(0f, 0f); sr.anchorMax = new Vector2(1f, 0f); sr.pivot = new Vector2(0.5f, 0f);
            sr.offsetMin = new Vector2(0f, 0f); sr.offsetMax = new Vector2(0f, m.keep ? 0f : 5f);
            string label = m.label;
            if (m.type == ReplayMoment.Type.Dead && w < 60f) label = "";
            b.label.text = w >= 30f ? label : "";
            b.label.color = m.keep ? new Color32(20, 18, 32, 255) : new Color(1f, 1f, 1f, 0.4f);
        }
    }

    // ------------------------------------------------------------------ every frame

    void UpdateClips()
    {
        if (clipsWin == null) return;
        bool visible = clipsWin.open && !clipsWin.minimized;
        if (!visible) { titleFocus = false; return; }
        if (clipsDirty) RefreshClips();
        if (clipRound == null) return;

        var stage = ReplayStage.Get();
        string why = ReplayStage.Unavailable;
        bool available = why == null;
        if (available)
        {
            if (!stage.Loaded || stage.Round != clipRound) { stage.Load(clipRound); needRender = true; if (!thumbsLoaded) thumbsDirtyAt = Time.unscaledTime; }
            stage.Touch();
        }
        previewOverlay.SetActive(!available);
        if (!available)
        {
            previewMessage.text = "<b>Preview paused</b>\n" + why;
            clipPlaying = false;
        }
        previewImage.texture = stage.Texture;
        previewImage.enabled = stage.Texture != null;

        HandleClipsInput();

        // playback
        if (clipPlaying && keptDur > 0.1f)
        {
            clipU += Time.unscaledDeltaTime;
            if (clipU >= keptDur) clipU = 0f;
            if (Time.unscaledTime >= nextRender) { needRender = true; nextRender = Time.unscaledTime + 1f / 30f; }
        }
        float curT = ClipToRound(clipU);
        if (available && needRender)
        {
            needRender = false;
            stage.Show(curT, (ReplayStage.CamMode)clipCam);
        }
        if (available && thumbsDirtyAt >= 0f && Time.unscaledTime >= thumbsDirtyAt && !scrubbing)
        {
            thumbsDirtyAt = -1f;
            RegenerateThumbs(stage);
        }

        // widgets
        clipPlayButton.text.text = clipPlaying ? "Pause" : "Play";
        clipPlayButton.bg.color = new Color(1f, 1f, 1f, Over(clipPlayButton.rt) ? 0.22f : 0.12f);
        UIFactory.SetBar(scrubFill, keptDur > 0.01f ? clipU / keptDur : 0f);
        previewTime.text = $"{Fmt(clipU)} / {Fmt(keptDur)}";
        float dur = Mathf.Max(1f, clipRound.duration);
        float px = 4f + curT / dur * 632f;
        playhead.anchorMin = playhead.anchorMax = new Vector2(0f, 0.5f);
        playhead.pivot = new Vector2(0.5f, 0.5f);
        playhead.sizeDelta = new Vector2(2f, 52f);
        playhead.anchoredPosition = new Vector2(px, 0f);
        playhead.gameObject.SetActive(keptDur > 0.1f);

        for (int i = 0; i < roundRows.Length; i++)
        {
            if (i >= clipRounds.Count) break;
            bool sel = clipRounds[i] == clipRound;
            roundRows[i].bg.color = new Color(1f, 1f, 1f, sel ? 0.14f : Over(roundRows[i].rt) ? 0.07f : 0f);
        }
        for (int i = 0; i < 3; i++)
        {
            if (!camButtons[i].rt) continue;
            if (clipCam != i) camButtons[i].bg.color = new Color(1f, 1f, 1f, Over(camButtons[i].rt) ? 0.2f : 0.1f);
        }
        for (int i = 0; i < 3; i++)
        {
            bool sel = thumbPick == i;
            thumbFrames[i].color = sel ? ClipAccent : new Color(1f, 1f, 1f, Over(thumbImages[i].rectTransform) ? 0.35f : 0.1f);
            thumbImages[i].enabled = thumbs[i] != null;
            thumbImages[i].texture = thumbs[i];
        }
        bool canUpload = keptDur >= MinClipSeconds && uploadedId == 0;
        clipUploadButton.bg.color = canUpload ? (Over(clipUploadButton.rt) ? new Color32(210, 120, 255, 255) : ClipAccent) : new Color(1f, 1f, 1f, 0.1f);
        clipUploadButton.text.color = canUpload ? Color.white : new Color(1f, 1f, 1f, 0.4f);
        clipUploadButton.text.text = uploadedId != 0 ? "UPLOADED" : "UPLOAD TO CLIPHUB";
        clipViewButton.bg.color = new Color(1f, 1f, 1f, Over(clipViewButton.rt) ? 0.22f : 0.12f);

        // timeline hover hint
        string hint = "Click a block to keep or cut it. Dead time is cut by default.";
        for (int i = 0; i < moments.Count; i++)
        {
            var b = blocks[i];
            bool hover = Over(b.rt);
            if (hover)
            {
                var m = moments[i];
                string what = m.type == ReplayMoment.Type.Multi ? $"Multi-kill x{m.kills}" : ReplayMoment.NameOf(m.type);
                hint = $"{what}  ({m.Length:0.0} s)  -  {(m.keep ? "kept. Click to cut it." : "cut. Click to keep it.")}";
            }
            Color col = ReplayMoment.ColorOf(moments[i].type);
            b.bg.color = moments[i].keep ? (hover ? Color.Lerp(col, Color.white, 0.35f) : col) : (hover ? new Color(0.26f, 0.26f, 0.34f, 1f) : new Color(0.16f, 0.16f, 0.22f, 1f));
        }
        timelineHint.text = hint;

        // title field look + caret
        var fieldBg = titleField.GetComponent<Image>();
        fieldBg.color = titleFocus ? new Color(1f, 1f, 1f, 0.16f) : new Color(1f, 1f, 1f, Over(titleField) ? 0.12f : 0.09f);
        UpdateTitleText();
    }

    void UpdateTitleText()
    {
        if (!titleText) return;
        bool caret = titleFocus && (int)(Time.unscaledTime * 2f) % 2 == 0;
        if (clipTitle.Length == 0 && !titleFocus) titleText.text = "<color=#FFFFFF66>Click here and type a title</color>";
        else titleText.text = clipTitle + (caret ? "|" : titleFocus ? "<alpha=#00>|" : "");
        titleCount.text = $"{clipTitle.Length}/{ClipTitleMax}";
    }

    void HandleClipsInput()
    {
        // Typing into the title (only while it is focused and the window is in front).
        if (titleFocus && focused == clipsWin)
        {
            string input = Input.inputString;
            if (!string.IsNullOrEmpty(input))
            {
                bool changed = false;
                foreach (char ch in input)
                {
                    if (ch == '\b') { if (clipTitle.Length > 0) { clipTitle = clipTitle.Substring(0, clipTitle.Length - 1); changed = true; } }
                    else if (ch == '\n' || ch == '\r') titleFocus = false;
                    else if (ch >= ' ' && ch < 127 && clipTitle.Length < ClipTitleMax && ch != '<' && ch != '>') { clipTitle += ch; changed = true; }
                }
                if (changed) { titleEdited = true; MarkEdited(); RecomputeClip(); }
            }
        }
        else if (focused == clipsWin && Input.GetKeyDown(KeyCode.Space) && keptDur > 0.1f)
        {
            TogglePlay();
        }

        // Dragging on the scrub bar.
        if (scrubbing)
        {
            if (Input.GetMouseButton(0)) ScrubTo();
            else scrubbing = false;
        }
    }

    void TogglePlay()
    {
        if (keptDur < 0.1f || !ReplayStage.Available) return;
        clipPlaying = !clipPlaying;
        if (clipPlaying && clipU >= keptDur - 0.05f) clipU = 0f;
        needRender = true;
    }

    void ScrubTo()
    {
        Vector3 world = screen.TransformPoint(new Vector3(mouse.x - ScreenW * 0.5f, mouse.y - ScreenH * 0.5f, 0f));
        Vector3 local = scrub.InverseTransformPoint(world);
        float k = Mathf.Clamp01((local.x - scrub.rect.xMin) / scrub.rect.width);
        clipU = k * keptDur;
        needRender = true;
    }

    void RegenerateThumbs(ReplayStage stage)
    {
        if (keptDur < 0.5f) return;
        for (int i = 0; i < 3; i++)
        {
            var tex = stage.Snapshot(thumbTimes[i], (ReplayStage.CamMode)clipCam);
            if (!tex) continue;
            if (thumbs[i]) Destroy(thumbs[i]);
            thumbs[i] = tex;
        }
        thumbsLoaded = true;
        needRender = true;
    }

    // ------------------------------------------------------------------ clicks

    void ClipsClick()
    {
        if (clipRound == null || !clipsMain.activeSelf) return;
        titleFocus = false; // (clicking the title box focuses it again below)

        for (int i = 0; i < clipRounds.Count && i < roundRows.Length; i++)
            if (Over(roundRows[i].rt)) { if (clipRounds[i] != clipRound) SelectRound(clipRounds[i]); Click1(); return; }

        if (Over(titleField)) { titleFocus = true; return; }
        if (Over(clipPlayButton.rt)) { TogglePlay(); Click1(); return; }
        if (Over(scrubHit))
        {
            if (keptDur > 0.1f) { scrubbing = true; ScrubTo(); }
            return;
        }
        for (int i = 0; i < moments.Count; i++)
        {
            if (!Over(blocks[i].rt)) continue;
            var m = moments[i];
            m.keep = !m.keep;
            MarkEdited();
            RecomputeClip();
            if (m.keep) clipU = RoundToClip(m.start); // jump to the moment you just kept
            thumbsDirtyAt = Time.unscaledTime + 0.3f;
            Click1();
            return;
        }
        for (int i = 0; i < 3; i++)
            if (Over(camButtons[i].rt))
            {
                if (clipCam != i) { clipCam = i; MarkEdited(); RecomputeClip(); thumbsDirtyAt = Time.unscaledTime + 0.25f; Click1(); }
                return;
            }
        for (int i = 0; i < 3; i++)
            if (Over(thumbImages[i].rectTransform) || Over(thumbFrames[i].rectTransform))
            {
                if (thumbPick != i) { thumbPick = i; MarkEdited(); RecomputeClip(); Click1(); }
                return;
            }
        if (clipViewButton.rt.gameObject.activeSelf && Over(clipViewButton.rt))
        {
            if (uploadedId != 0) { OpenBrowser(); OpenClipHubVideo(uploadedId); }
            return;
        }
        if (Over(clipUploadButton.rt)) { UploadClip(); return; }
    }

    void Click1() => Fx.Play(Fx.Click, pc ? pc.transform.position : Vector3.zero, 0.25f, 1.5f);

    void UploadClip()
    {
        if (uploadedId != 0) return;
        if (keptDur < MinClipSeconds) { uploadMessage.text = "<color=#FFC04A>Keep at least 3 seconds of the round first.</color>"; return; }
        var thumb = thumbs[Mathf.Clamp(thumbPick, 0, 2)];
        bool made = false;
        if (!thumb) { thumb = MakeFallbackThumb(); made = true; }
        string title = clipTitle.Trim().Length == 0 ? AutoTitle() : clipTitle;
        var v = ClipHub.Upload(title, quality, clipCam, Mathf.RoundToInt(keptDur), kKills, kHeads, kCaps, kDeaths, clipRound.ranked, thumb);
        if (made) Destroy(thumb);
        uploadedId = v.id;
        uploadMessage.text = "<color=#6EE696>Uploaded!</color> Views trickle in over time.";
        clipViewButton.rt.gameObject.SetActive(true);
        GameHUD.Toast($"Uploaded \"{v.title}\" to ClipHub", GameHUD.Good);
        Fx.Play(Fx.Click, pc ? pc.transform.position : Vector3.zero, 0.4f, 1.1f);
    }

    /// <summary>A plain thumbnail for when the arena was busy and no frame could be filmed.</summary>
    static Texture2D MakeFallbackThumb()
    {
        const int w = ReplayStage.ThumbW, h = ReplayStage.ThumbH;
        var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        var px = new Color32[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float k = (x / (float)w + y / (float)h) * 0.5f;
                px[y * w + x] = Color32.Lerp(new Color32(60, 30, 110, 255), new Color32(20, 120, 150, 255), k);
            }
        tex.SetPixels32(px);
        tex.Apply();
        return tex;
    }
}
