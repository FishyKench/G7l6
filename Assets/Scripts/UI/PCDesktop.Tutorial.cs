using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The tutorial's bits on the PC desktop:
///   - the first time you sit down during the tutorial, a short looping "video" plays on the screen: the pointer double-clicks
///     Homework, the window opens, "7 + 5 =" gets its 12, ENTER, "Correct!", a line gets copied letter by letter, DONE. It loops
///     until you click OK, GOT IT (or press Enter);
///   - after that, the Homework icon breathes (soft glow, a little bigger) with a bobbing arrow and "double-click!" until the
///     homework window is open.
/// See <see cref="TutorialDay"/>.
/// </summary>
public partial class PCDesktop
{
    /// <summary>The tutorial's how-to clip is up on the screen (it has the mouse).</summary>
    public static bool TutorialClipOpen { get; private set; }
    /// <summary>It's been watched and closed (OK, GOT IT).</summary>
    public static bool TutorialClipSeen { get; private set; }

    static PCDesktop tutDesk;

    public static void ResetTutorialClip() { TutorialClipOpen = false; TutorialClipSeen = false; }

    public static void CloseTutorialClip()
    {
        TutorialClipOpen = false;
        TutorialClipSeen = true;
        if (tutDesk && tutDesk.clipRoot) tutDesk.clipRoot.gameObject.SetActive(false);
    }

    /// <summary>(The tutorial's test hook) opens the homework window as a double-click would.</summary>
    public void TutorialOpenHomework() => OpenApp("homework");

    // ------------------------------------------------------------------ the clip

    const float ClipLength = 13.5f;
    static readonly Color Orange = new Color32(255, 140, 26, 255);
    static readonly Color Good = new Color32(40, 160, 80, 255);

    RectTransform clipRoot, clipVideo, clipWindowEdge, clipPointer, clipRipple, clipIconGlow, clipWindow, clipMaths, clipCopy, clipEnter, clipStamp, clipProgress, clipOk;
    TextMeshProUGUI clipSum, clipCorrect, clipTyped, clipCaption;
    Image clipEnterBg;
    float clipTime;
    const string CopyLine = "the cat sat on the warm mat";

    // (the clip's mini desktop, in the video's own space: centre (0,0), 940 x 450)
    static readonly Vector2 IconAt = new Vector2(-402f, 152f), RestAt = new Vector2(300f, -120f);

    void TutorialOnShow()
    {
        tutDesk = this;
        if (!TutorialDay.Active || TutorialClipSeen) return;
        TutorialClipOpen = true;
        clipTime = 0f;
        if (!clipRoot) BuildClip();
        clipRoot.gameObject.SetActive(true);
        clipRoot.SetAsLastSibling();
        cursor.SetAsLastSibling();
    }

    /// <summary>Every frame (from Update). True = the clip is up and has the mouse: the desktop does nothing else.</summary>
    bool TutorialUpdate()
    {
        tutDesk = this;
        bool clip = TutorialClipOpen && TutorialDay.Active;
        if (clipRoot && clipRoot.gameObject.activeSelf != clip) clipRoot.gameObject.SetActive(clip);
        if (clip)
        {
            if (!clipRoot) BuildClip();
            clipTime += Time.unscaledDeltaTime;
            ClipFrame(clipTime % ClipLength);
            clipOk.localScale = Vector3.one * (1f + Mathf.Sin(Time.unscaledTime * 4f) * 0.04f);
            bool over = Over(clipOk);
            clipOk.GetComponent<Image>().color = over ? new Color32(255, 170, 70, 255) : (Color)Orange;
            if ((Input.GetMouseButtonDown(0) && over) || Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
                CloseTutorialClip();
            UpdateTutorialPointer(false);
            return true;
        }
        UpdateTutorialPointer(TutorialDay.Active && TutorialClipSeen && pc && pc.Homework && pc.Homework.IsActive
                              && !(homeworkWin != null && homeworkWin.open && !homeworkWin.minimized));
        return false;
    }

    /// <summary>(Tests) shows the clip at this moment of its loop.</summary>
    public void TutorialClipAt(float t) { clipTime = t; if (clipRoot) ClipFrame(t % ClipLength); }

    void ClipFrame(float t)
    {
        // the pointer: over to Homework, double-click, then out of the way
        Vector2 p;
        if (t < 1.5f) p = Vector2.Lerp(RestAt, IconAt, Smooth(t / 1.5f));
        else if (t < 2.5f) p = IconAt;
        else p = Vector2.Lerp(IconAt, RestAt, Smooth((t - 2.5f) / 0.7f));
        clipPointer.anchoredPosition = p;

        // the two clicks: a ring each
        float click = t >= 1.6f && t < 1.9f ? (t - 1.6f) / 0.3f : t >= 1.9f && t < 2.2f ? (t - 1.9f) / 0.3f : -1f;
        clipRipple.gameObject.SetActive(click >= 0f);
        if (click >= 0f)
        {
            clipRipple.anchoredPosition = IconAt;
            clipRipple.localScale = Vector3.one * Mathf.Lerp(0.3f, 1.2f, click);
            clipRipple.GetComponent<Image>().color = new Color(1f, 0.55f, 0.1f, 1f - click);
        }
        clipIconGlow.gameObject.SetActive(t >= 1.6f && t < 2.5f);

        // the window pops open
        bool win = t >= 2.2f;
        clipWindow.gameObject.SetActive(win);
        if (win) clipWindow.localScale = Vector3.one * (t < 2.5f ? Mathf.Lerp(0.6f, 1f, Back((t - 2.2f) / 0.3f)) : 1f);
        clipWindowEdge.gameObject.SetActive(win);
        clipWindowEdge.localScale = clipWindow.localScale;

        // the sum: 1, 2, ENTER, Correct!
        clipMaths.gameObject.SetActive(win && t < 6f);
        string ans = t < 3f ? "" : t < 3.5f ? "1" : "12";
        bool caret = t < 4.5f && Mathf.Repeat(t, 0.6f) < 0.35f;
        clipSum.text = $"7 + 5 = <color=#1E64C8><b>{ans}</b></color>{(caret ? "|" : " ")}";
        bool enter = t >= 4.2f && t < 5.6f;
        clipEnter.gameObject.SetActive(enter);
        if (enter)
        {
            bool down = t >= 4.5f && t < 4.75f;
            clipEnter.localScale = Vector3.one * (t < 4.4f ? Back((t - 4.2f) / 0.2f) : down ? 0.88f : 1f);
            clipEnterBg.color = down ? new Color32(255, 200, 120, 255) : Color.white;
        }
        clipCorrect.gameObject.SetActive(t >= 5f);

        // the line, copied letter by letter
        clipCopy.gameObject.SetActive(t >= 6f);
        int n = Mathf.Clamp(Mathf.FloorToInt((t - 6.5f) / 4f * CopyLine.Length), 0, CopyLine.Length);
        bool typing = t < 10.8f && Mathf.Repeat(t, 0.6f) < 0.35f;
        clipTyped.text = CopyLine.Substring(0, n) + (typing ? "|" : "");

        // DONE
        bool stamp = t >= 10.8f;
        clipStamp.gameObject.SetActive(stamp);
        if (stamp) clipStamp.localScale = Vector3.one * Mathf.Lerp(2.2f, 1f, Smooth((t - 10.8f) / 0.22f));

        clipCaption.text = t < 2.5f ? "<b>1</b>   Double-click  <b>Homework</b>"
                         : t < 6f ? "<b>2</b>   Type the answer, then press  <b>ENTER</b>"
                         : t < 10.8f ? "<b>3</b>   Copy the line, letter by letter"
                         : "Done!  That's your homework.";
        clipProgress.anchorMax = new Vector2(t / ClipLength, 1f);
    }

    static float Smooth(float x) { x = Mathf.Clamp01(x); return x * x * (3f - 2f * x); }
    static float Back(float x) { x = Mathf.Clamp01(x); float c = 1.7f; return 1f + (c + 1f) * Mathf.Pow(x - 1f, 3f) + c * Mathf.Pow(x - 1f, 2f); }

    void BuildClip()
    {
        var font = GameHUD.Instance ? GameHUD.Instance.font : null;
        clipRoot = UIFactory.Rect("Tutorial clip", screen);
        clipRoot.gameObject.AddComponent<PcSketchKeep>();
        UIFactory.Fill(clipRoot);
        var dim = UIFactory.Panel("Dim", clipRoot, new Color(0f, 0f, 0f, 0.62f));
        UIFactory.Fill(dim.rectTransform);

        // the player: a dark frame, the video, a progress bar, a caption
        var frame = UIFactory.Panel("Player", clipRoot, new Color32(24, 24, 34, 255));
        frame.sprite = Round(); frame.type = Image.Type.Sliced;
        UIFactory.Place(frame.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 50f), new Vector2(1000f, 600f));
        var f = frame.rectTransform;
        var title = UIFactory.Text("Title", f, 20, new Color(1f, 1f, 1f, 0.9f), TextAlignmentOptions.MidlineLeft, font);
        title.text = "<b>HOW TO:</b>  your homework";
        title.enableWordWrapping = false;
        UIFactory.Place(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -8f), new Vector2(940f, 34f));
        var live = UIFactory.Text("Playing", f, 16, new Color32(255, 120, 110, 255), TextAlignmentOptions.MidlineRight, font);
        live.text = "<b>> PLAYING</b>";
        live.enableWordWrapping = false;
        UIFactory.Place(live.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -8f), new Vector2(940f, 34f));

        clipVideo = UIFactory.Rect("Video", f);
        UIFactory.Place(clipVideo, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -48f), new Vector2(940f, 450f));
        clipVideo.gameObject.AddComponent<RectMask2D>();
        BuildClipVideo(clipVideo, font);

        var barBg = UIFactory.Panel("Progress", f, new Color(1f, 1f, 1f, 0.15f));
        UIFactory.Place(barBg.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -504f), new Vector2(940f, 8f));
        var fill = UIFactory.Panel("Fill", barBg.transform, Orange);
        clipProgress = fill.rectTransform;
        clipProgress.anchorMin = Vector2.zero; clipProgress.anchorMax = new Vector2(0f, 1f);
        clipProgress.pivot = new Vector2(0f, 0.5f);
        clipProgress.offsetMin = clipProgress.offsetMax = Vector2.zero;

        clipCaption = UIFactory.Text("Caption", f, 26, Color.white, TextAlignmentOptions.Center, font);
        clipCaption.enableWordWrapping = false;
        UIFactory.Place(clipCaption.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -520f), new Vector2(940f, 44f));

        // OK, GOT IT
        var ok = UIFactory.Panel("OK", clipRoot, Orange);
        ok.sprite = Round(); ok.type = Image.Type.Sliced;
        clipOk = ok.rectTransform;
        UIFactory.Place(clipOk, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -294f), new Vector2(300f, 60f));
        var okText = UIFactory.Text("Text", clipOk, 26, new Color32(30, 20, 10, 255), TextAlignmentOptions.Center, font);
        okText.text = "<b>OK, GOT IT</b>\n<size=58%>click, or press Enter</size>";
        okText.lineSpacing = -18f;
        UIFactory.Fill(okText.rectTransform, 6, 6, 2, 2);
    }

    void BuildClipVideo(RectTransform v, TMP_FontAsset font)
    {
        var wall = UIFactory.Rect("Wallpaper", v).gameObject.AddComponent<RawImage>();
        wall.texture = PcSketch.PaperWallpaper;
        wall.raycastTarget = false;
        UIFactory.Fill(wall.rectTransform);
        var bar = UIFactory.Panel("Taskbar", v, new Color32(14, 14, 24, 240));
        UIFactory.Place(bar.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), Vector2.zero, new Vector2(940f, 30f));

        // the Homework icon (with its "selected" glow)
        var glow = UIFactory.Panel("Selected", v, new Color(0.45f, 0.7f, 1f, 0.45f));
        glow.sprite = Round(); glow.type = Image.Type.Sliced;
        clipIconGlow = glow.rectTransform;
        UIFactory.Place(clipIconGlow, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), IconAt + new Vector2(0f, -10f), new Vector2(100f, 108f));
        var icon = UIFactory.Rect("Homework icon", v).gameObject.AddComponent<RawImage>();
        icon.texture = SketchIcons.Get("homework");
        icon.raycastTarget = false;
        UIFactory.Place(icon.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), IconAt + new Vector2(0f, 4f), new Vector2(66f, 66f));
        var label = UIFactory.Text("Label", v, 15, Color.white, TextAlignmentOptions.Center, font);
        label.text = "Homework";
        label.enableWordWrapping = false;
        label.outlineWidth = 0.2f; label.outlineColor = new Color32(0, 0, 0, 200);
        UIFactory.Place(label.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), IconAt + new Vector2(0f, -44f), new Vector2(100f, 22f));

        // the homework window (with a dark edge, so it stands out from the paper)
        var edge = UIFactory.Panel("Window edge", v, new Color32(36, 36, 58, 255));
        UIFactory.Place(edge.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(70f, 10f), new Vector2(606f, 336f));
        var win = UIFactory.Panel("Window", v, new Color32(232, 232, 238, 255));
        clipWindow = win.rectTransform;
        clipWindowEdge = edge.rectTransform;
        UIFactory.Place(clipWindow, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(70f, 10f), new Vector2(600f, 330f));
        var tb = UIFactory.Panel("Title bar", clipWindow, new Color32(36, 36, 58, 255));
        UIFactory.Place(tb.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(600f, 30f));
        var tt = UIFactory.Text("Title", tb.transform, 15, Color.white, TextAlignmentOptions.MidlineLeft, font);
        tt.text = "Homework.docx - Docs";
        tt.enableWordWrapping = false;
        UIFactory.Fill(tt.rectTransform, 12, 60, 0, 0);
        var x = UIFactory.Panel("Close", tb.transform, new Color32(210, 60, 70, 255));
        UIFactory.Place(x.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), Vector2.zero, new Vector2(40f, 30f));

        var ink = new Color32(40, 40, 60, 255);
        // part 1: the sum
        clipMaths = UIFactory.Rect("Maths", clipWindow);
        UIFactory.Fill(clipMaths, 0, 0, 30, 0);
        var q = UIFactory.Text("Question", clipMaths, 15, new Color(0.25f, 0.25f, 0.35f, 0.8f), TextAlignmentOptions.Center, font);
        q.text = "QUESTION 1";
        UIFactory.Place(q.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 120f), new Vector2(300f, 24f));
        clipSum = UIFactory.Text("Sum", clipMaths, 46, ink, TextAlignmentOptions.MidlineLeft, font);
        clipSum.enableWordWrapping = false;
        UIFactory.Place(clipSum.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-70f, 60f), new Vector2(300f, 64f));
        var key = UIFactory.Panel("Enter key", clipMaths, Color.black);
        key.sprite = Round(); key.type = Image.Type.Sliced;
        clipEnter = key.rectTransform;
        UIFactory.Place(clipEnter, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(190f, 60f), new Vector2(124f, 54f));
        clipEnterBg = UIFactory.Panel("Cap", clipEnter, Color.white);
        clipEnterBg.sprite = Round(); clipEnterBg.type = Image.Type.Sliced;
        UIFactory.Fill(clipEnterBg.rectTransform, 3, 3, 3, 6);
        var kt = UIFactory.Text("Text", clipEnter, 22, ink, TextAlignmentOptions.Center, font);
        kt.text = "<b>ENTER</b>";
        kt.enableWordWrapping = false;
        UIFactory.Fill(kt.rectTransform, 4, 4, 3, 6);
        clipCorrect = UIFactory.Text("Correct", clipMaths, 34, Good, TextAlignmentOptions.Center, font);
        clipCorrect.text = "<b>Correct!</b>";
        UIFactory.Place(clipCorrect.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -30f), new Vector2(400f, 48f));

        // part 2: copy the line
        clipCopy = UIFactory.Rect("Copy", clipWindow);
        UIFactory.Fill(clipCopy, 0, 0, 30, 0);
        var hint = UIFactory.Text("Hint", clipCopy, 15, new Color(0.25f, 0.25f, 0.35f, 0.8f), TextAlignmentOptions.Center, font);
        hint.text = "COPY THIS LINE";
        UIFactory.Place(hint.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 120f), new Vector2(300f, 24f));
        var src = UIFactory.Panel("Line", clipCopy, new Color32(214, 214, 224, 255));
        UIFactory.Place(src.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 72f), new Vector2(540f, 46f));
        var st = UIFactory.Text("Text", src.transform, 24, new Color32(110, 110, 130, 255), TextAlignmentOptions.MidlineLeft, font);
        st.text = CopyLine;
        st.enableWordWrapping = false;
        UIFactory.Fill(st.rectTransform, 16, 16, 0, 0);
        var dst = UIFactory.Panel("Typed", clipCopy, Color.white);
        UIFactory.Place(dst.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 14f), new Vector2(540f, 46f));
        clipTyped = UIFactory.Text("Text", dst.transform, 24, ink, TextAlignmentOptions.MidlineLeft, font);
        clipTyped.enableWordWrapping = false;
        UIFactory.Fill(clipTyped.rectTransform, 16, 16, 0, 0);
        var stamp = UIFactory.Panel("DONE", clipCopy, new Color32(210, 40, 50, 230));
        clipStamp = stamp.rectTransform;
        UIFactory.Place(clipStamp, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(140f, -80f), new Vector2(230f, 84f));
        clipStamp.localRotation = Quaternion.Euler(0f, 0f, 10f);
        var inner = UIFactory.Panel("Inner", clipStamp, new Color32(246, 236, 236, 255));
        UIFactory.Fill(inner.rectTransform, 6, 6, 6, 6);
        var dt = UIFactory.Text("Text", clipStamp, 52, new Color32(210, 40, 50, 255), TextAlignmentOptions.Center, font);
        dt.text = "<b>DONE</b>";
        dt.enableWordWrapping = false;
        UIFactory.Fill(dt.rectTransform, 8, 8, 6, 6);

        // the pointer and its click ring (on top)
        var ring = UIFactory.Panel("Click", v, Color.white);
        ring.sprite = Round();
        clipRipple = ring.rectTransform;
        UIFactory.Place(clipRipple, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), IconAt, new Vector2(70f, 70f));
        var ptr = UIFactory.Panel("Pointer", v, Color.white);
        ptr.sprite = Arrow();
        clipPointer = ptr.rectTransform;
        UIFactory.Place(clipPointer, new Vector2(0.5f, 0.5f), new Vector2(0f, 1f), RestAt, new Vector2(34f, 34f));
    }

    // ------------------------------------------------------------------ after the clip: Homework breathes, an arrow points at it

    RectTransform tutArrow, tutGlow;
    bool tutPointing;

    void UpdateTutorialPointer(bool on)
    {
        var icon = icons.Find(i => i.id == "homework");
        if (icon == null) return;
        if (!on)
        {
            if (tutPointing)
            {
                tutPointing = false;
                icon.rt.localScale = Vector3.one;
                if (tutArrow) tutArrow.gameObject.SetActive(false);
                if (tutGlow) tutGlow.gameObject.SetActive(false);
            }
            return;
        }
        if (!tutArrow) BuildTutorialPointer(icon);
        tutPointing = true;
        tutArrow.gameObject.SetActive(true);
        tutGlow.gameObject.SetActive(true);
        tutArrow.SetAsFirstSibling(); // (under any window)

        float breathe = (Mathf.Sin(Time.unscaledTime * 3.2f) + 1f) * 0.5f;
        icon.rt.localScale = Vector3.one * (1f + breathe * 0.07f);
        tutGlow.GetComponent<Image>().color = new Color(1f, 0.6f, 0.15f, Mathf.Lerp(0.25f, 0.6f, breathe));
        tutGlow.localScale = Vector3.one * Mathf.Lerp(0.95f, 1.12f, breathe);

        // beside the icon, pointing at it, bobbing
        Vector3 right = icon.rt.TransformPoint(new Vector3(icon.rt.rect.xMax, icon.rt.rect.center.y, 0f));
        Vector2 at = windowLayer.InverseTransformPoint(right);
        tutArrow.anchoredPosition = at + new Vector2(10f + Mathf.Abs(Mathf.Sin(Time.unscaledTime * 4f)) * 16f, 0f);
    }

    void BuildTutorialPointer(Icon icon)
    {
        var font = GameHUD.Instance ? GameHUD.Instance.font : null;
        var glow = UIFactory.Panel("Tutorial glow", icon.rt, Orange);
        glow.sprite = Round();
        glow.gameObject.AddComponent<PcSketchKeep>();
        tutGlow = glow.rectTransform;
        UIFactory.Place(tutGlow, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 2f), new Vector2(136f, 136f));
        tutGlow.SetAsFirstSibling();

        tutArrow = UIFactory.Rect("Tutorial arrow", windowLayer);
        tutArrow.gameObject.AddComponent<PcSketchKeep>();
        tutArrow.anchorMin = tutArrow.anchorMax = new Vector2(0.5f, 0.5f);
        tutArrow.pivot = new Vector2(0f, 0.5f);
        tutArrow.sizeDelta = new Vector2(250f, 70f);
        var head = UIFactory.Panel("Arrow", tutArrow, Orange);
        head.sprite = HudArt.Get("arrow");
        head.preserveAspect = true;
        UIFactory.Place(head.rectTransform, new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(30f, 0f), new Vector2(60f, 60f));
        head.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 90f); // (the sprite points up: now it points left, at the icon)
        var note = UIFactory.Panel("Note", tutArrow, HudArt.NoteYellow);
        note.sprite = HudArt.Get("note_paper");
        note.type = Image.Type.Sliced;
        UIFactory.Place(note.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(64f, 0f), new Vector2(180f, 54f));
        var t = UIFactory.Text("Text", note.transform, 26, HudArt.Ink, TextAlignmentOptions.Center, font);
        t.text = "<b>double-click!</b>";
        t.enableWordWrapping = false;
        UIFactory.Fill(t.rectTransform, 8, 8, 2, 2);
        note.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 3f);
    }
}
