using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The pause menu (Esc or P), in the same hand-drawn paper style as the HUD:
///   - a taped note with a wobbly "PAUSED", a boiling marker underline, the time and how long until Mom's home,
///     and the buttons (Resume, Restart day, How to play, Settings, Quit) - the one you point at gets circled
///     in red marker, a bobbing arrow and a little bounce;
///   - a sticky note next to it that shows today so far (+ a random tip), the controls, or the settings
///     (mouse sensitivity and volume, saved between plays). It flips over when you switch.
/// Everything drops in on springs and keeps gently moving. The game, its sounds and the player freeze.
/// Mouse or keyboard (W/S or arrows, Enter). Created automatically by the GameManager.
/// </summary>
public class PauseMenu : MonoBehaviour
{
    public static PauseMenu Instance { get; private set; }
    public static bool IsPaused => Instance && Instance.paused;

    public KeyCode key = KeyCode.Escape;
    public KeyCode altKey = KeyCode.P;

    const string SensitivityPref = "G7l6.mouseSensitivity";
    const string VolumePref = "G7l6.volume";

    static readonly Color Ink = new Color32(34, 32, 42, 255);
    static readonly Color InkSoft = new Color32(34, 32, 42, 160);
    static readonly Color Red = new Color32(224, 53, 43, 255);
    static readonly Color Green = new Color32(46, 158, 68, 255);
    static readonly Color Paper = new Color32(250, 247, 238, 255);
    static readonly Color Sticky = new Color32(255, 232, 115, 255);

    static readonly string[] Tips =
    {
        "Pet the cat to calm down.",
        "Put things back where you found them - Mom checks!",
        "Hold TAB to find the remote, mop and bucket.",
        "Mom only counts what's still a mess when she reaches it.",
        "Don't forget the oven - dinner burns, then it burns.",
        "Max stress? You have 20 seconds to calm down.",
        "The cat loves your keyboard. Shoo her off before typing.",
    };

    enum Side { Today, Controls, Settings }

    class Item
    {
        public string label;
        public System.Action action;
        public RectTransform rt;
        public TextMeshProUGUI text;
        public Image circle, arrow;
        public Spring scale, tilt;
        public float appearDelay;
    }

    bool paused, busyLastFrame;
    float open;              // 0 closed .. 1 open (animated)
    float openedAt;
    Side side = Side.Today;
    int selected;
    float quitArmedUntil;
    TMP_FontAsset font;

    RectTransform canvasRoot, menuRoot, mainNote, sideNote, sideContent;
    Image dim;
    TextMeshProUGUI title, subtitle;
    readonly List<Item> items = new List<Item>();
    Spring mainDrop, mainTilt, sideSlide, sideFlip;
    readonly List<Behaviour> frozen = new List<Behaviour>();
    float savedTimeScale = 1f;
    TextMeshProUGUI sensitivityValue, volumeValue;

    void Awake() => Instance = this;

    void Start()
    {
        font = GameHUD.Instance ? GameHUD.Instance.font : null;
        ApplySettings(PlayerPrefs.GetFloat(SensitivityPref, -1f), PlayerPrefs.GetFloat(VolumePref, 1f));
        Build();
        menuRoot.gameObject.SetActive(false);
    }

    void OnDestroy()
    {
        if (paused) Freeze(false);
    }

    // ------------------------------------------------------------------ opening and closing

    void Update()
    {
        bool busy = (PlayerInteraction.Instance && PlayerInteraction.Instance.InMainTask)
                    || (DayManager.Instance && DayManager.Instance.State == DayManager.Phase.Report);
        if (Input.GetKeyDown(key) || Input.GetKeyDown(altKey))
        {
            if (paused) Resume();
            else if (VRHeadset.Worn) { if (Input.GetKeyDown(altKey)) Pause(); } // in VR, Esc takes the headset off; P pauses
            else if (!busy && !busyLastFrame) Pause(); // Esc at the desk stands you up instead
        }

        if (menuRoot && menuRoot.gameObject.activeSelf) Animate();
        if (paused) Navigate();
    }

    void LateUpdate()
    {
        busyLastFrame = (PlayerInteraction.Instance && PlayerInteraction.Instance.InMainTask)
                        || (DayManager.Instance && DayManager.Instance.State == DayManager.Phase.Report);
    }

    public void Pause()
    {
        if (paused) return;
        paused = true;
        Freeze(true);
        menuRoot.gameObject.SetActive(true);
        openedAt = Time.unscaledTime;
        open = 0f;
        mainDrop = new Spring(900f);
        mainTilt = new Spring(-14f);
        sideSlide = new Spring(900f);
        sideFlip = new Spring(1f);
        foreach (var it in items) { it.scale = new Spring(0f); it.tilt = new Spring(0f); }
        selected = 0;
        quitArmedUntil = 0f;
        ShowSide(Side.Today, flip: false);
    }

    public void Resume()
    {
        if (!paused) return;
        paused = false;
        Freeze(false); // the game comes back at once; the notes fly away on their own
    }

    void Freeze(bool on)
    {
        if (on)
        {
            savedTimeScale = Time.timeScale > 0f ? Time.timeScale : 1f;
            Time.timeScale = 0f;
            AudioListener.pause = true;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            frozen.Clear();
            foreach (var b in FindPlayerParts())
                if (b && b.enabled) { b.enabled = false; frozen.Add(b); }
        }
        else
        {
            Time.timeScale = savedTimeScale;
            AudioListener.pause = false;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            foreach (var b in frozen) if (b) b.enabled = true;
            frozen.Clear();
        }
    }

    static IEnumerable<Behaviour> FindPlayerParts()
    {
        foreach (var b in FindObjectsOfType<PlayerCam>()) yield return b;
        foreach (var b in FindObjectsOfType<PlayerMovementAdvanced>()) yield return b;
        foreach (var b in FindObjectsOfType<PlayerInteraction>()) yield return b;
        foreach (var b in FindObjectsOfType<PlayerCarry>()) yield return b;
    }

    // ------------------------------------------------------------------ building

    void Build()
    {
        var canvas = UIFactory.CreateCanvas("Pause Canvas", 80);
        canvas.transform.SetParent(transform, false);
        canvasRoot = (RectTransform)canvas.transform;
        menuRoot = UIFactory.Rect("Pause Menu", canvasRoot);
        UIFactory.Fill(menuRoot);

        dim = UIFactory.Panel("Dim", menuRoot, new Color(0.08f, 0.07f, 0.12f, 0f));
        UIFactory.Fill(dim.rectTransform);

        // --- the main note
        mainNote = Note("Paused note", Paper, new Vector2(-190, 10), new Vector2(480, 640), -2f);
        title = Text(mainNote, "PAUSED", 72, Ink, new Vector2(0, -92), new Vector2(420, 84));
        title.fontStyle = FontStyles.Bold;
        var underline = Doodle(mainNote, "underline", Red, new Vector2(0, -140), new Vector2(290, 26));
        underline.rectTransform.localRotation = Quaternion.Euler(0, 0, 1.5f);
        subtitle = Text(mainNote, "", 19, InkSoft, new Vector2(0, -176), new Vector2(430, 28));

        AddItem("Resume", Resume);
        AddItem("Restart day", () => { Freeze(false); paused = false; DayManager.RestartDay(); });
        AddItem("How to play", () => ShowSide(side == Side.Controls ? Side.Today : Side.Controls, flip: true));
        AddItem("Settings", () => ShowSide(side == Side.Settings ? Side.Today : Side.Settings, flip: true));
        AddItem("Quit game", Quit);
        for (int i = 0; i < items.Count; i++)
        {
            var it = items[i];
            it.rt.anchoredPosition = new Vector2(0, -250 - i * 66);
            it.appearDelay = 0.12f + i * 0.055f;
        }
        Text(mainNote, "ESC to get back to it", 15, InkSoft, new Vector2(0, -604), new Vector2(400, 22));

        // --- the sticky note on the side (its content changes)
        sideNote = Note("Side note", Sticky, new Vector2(300, -6), new Vector2(390, 540), 2.5f, sticky: true);
        sideContent = UIFactory.Rect("Content", sideNote);
        UIFactory.Fill(sideContent);
    }

    RectTransform Note(string name, Color color, Vector2 at, Vector2 size, float tilt, bool sticky = false)
    {
        var holder = UIFactory.Rect(name, menuRoot);
        UIFactory.Place(holder, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), at, size);
        var note = UIFactory.Rect("Note", holder);
        UIFactory.Fill(note);
        note.localRotation = Quaternion.Euler(0, 0, tilt);
        var breathe = note.gameObject.AddComponent<HudBreathe>();
        breathe.swayDegrees = 0.7f;
        breathe.breatheAmount = 0.006f;
        breathe.baseRotation = tilt;

        var shadow = UIFactory.Panel("Shadow", note, Color.white);
        shadow.sprite = HudArt.Get("note_shadow");
        UIFactory.Fill(shadow.rectTransform, -4, -18, -6, -20);
        var paper = UIFactory.Panel("Paper", note, color);
        paper.sprite = HudArt.Get("note_paper");
        UIFactory.Fill(paper.rectTransform);
        var tape = UIFactory.Panel("Tape", note, Color.white);
        tape.sprite = HudArt.Get("tape");
        UIFactory.Place(tape.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), new Vector2(sticky ? 60 : 0, -2), new Vector2(130, 36));
        tape.rectTransform.localRotation = Quaternion.Euler(0, 0, sticky ? -6f : 3f);
        return note;
    }

    void AddItem(string label, System.Action action)
    {
        var it = new Item { label = label, action = action };
        it.rt = UIFactory.Rect(label, mainNote);
        UIFactory.Place(it.rt, new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(380, 60));
        it.circle = Doodle(it.rt, "circle", Red, Vector2.zero, new Vector2(260, 70), anchorCentre: true);
        it.arrow = UIFactory.Panel("Arrow", it.rt, Red);
        it.arrow.sprite = HudArt.Get("arrow");
        UIFactory.Place(it.arrow.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(30, 30));
        it.arrow.rectTransform.localRotation = Quaternion.Euler(0, 0, -90f);
        it.text = UIFactory.Text("Label", it.rt, 38, Ink, TextAlignmentOptions.Center, font);
        it.text.text = label.ToUpperInvariant();
        it.text.enableWordWrapping = false;
        UIFactory.Fill(it.text.rectTransform);
        items.Add(it);
    }

    // ------------------------------------------------------------------ the side note

    void ShowSide(Side which, bool flip)
    {
        side = which;
        if (flip) sideFlip.Kick(-14f); // a quick squash, like the note being turned over
        foreach (Transform c in sideContent) Destroy(c.gameObject);
        sensitivityValue = volumeValue = null;
        switch (which)
        {
            case Side.Today: BuildToday(); break;
            case Side.Controls: BuildControls(); break;
            case Side.Settings: BuildSettings(); break;
        }
    }

    void BuildToday()
    {
        Heading("TODAY SO FAR");
        var day = DayManager.Instance;
        var gm = GameManager.Instance;
        int waiting = 0;
        if (gm) foreach (var t in gm.Tasks) if (t && t.IsActive && !t.isMainTask) waiting++;
        var homework = FindObjectOfType<TypingTask>();
        string pages = "-";
        bool homeworkDone = false;
        if (homework)
        {
            int total = Mathf.Max(1, homework.pagesRequired);
            homeworkDone = !homework.IsActive && homework.LastSucceeded;
            int typed = homeworkDone ? total : Mathf.FloorToInt(homework.Progress * total + 0.0001f);
            pages = $"{typed}/{total} pages";
        }
        float stress = StressManager.Instance ? StressManager.Instance.Normalized : 0f;

        float y = -120f;
        Line(ref y, "Chores done", day ? day.ChoresDone.ToString() : "-", day && day.ChoresDone > 0);
        Line(ref y, "Waiting for you", waiting.ToString(), waiting == 0);
        Line(ref y, "Homework", pages, homeworkDone);
        Line(ref y, "Stress", Mathf.RoundToInt(stress * 100f) + "%", stress < 0.5f);

        // "TIP" on its own line, the tip itself starting below it (so they never overlap).
        var tipTitle = Text(sideContent, "TIP:", 22, Red, new Vector2(-160, y - 16), new Vector2(80, 28));
        tipTitle.alignment = TextAlignmentOptions.MidlineLeft;
        tipTitle.rectTransform.pivot = new Vector2(0f, 0.5f);
        tipTitle.fontStyle = FontStyles.Bold;
        tipTitle.rectTransform.localRotation = Quaternion.Euler(0, 0, -4f);
        var tip = Text(sideContent, Tips[Random.Range(0, Tips.Length)], 21, Ink, new Vector2(-160, y - 38), new Vector2(330, 110));
        tip.rectTransform.pivot = new Vector2(0f, 1f); // grows downwards from just under "TIP:"
        tip.enableWordWrapping = true;
        tip.alignment = TextAlignmentOptions.TopLeft;
    }

    void Line(ref float y, string label, string value, bool good)
    {
        var l = Text(sideContent, label, 23, Ink, new Vector2(-30, y), new Vector2(260, 32));
        l.alignment = TextAlignmentOptions.MidlineLeft;
        // Value ends at x = 135; the tick (if it's going well) sits to the right of it, never on top.
        var v = Text(sideContent, value, 23, good ? Green : Ink, new Vector2(135, y), new Vector2(130, 32));
        v.rectTransform.pivot = new Vector2(1f, 0.5f);
        v.alignment = TextAlignmentOptions.MidlineRight;
        v.fontStyle = FontStyles.Bold;
        if (good) Doodle(sideContent, "check", Green, new Vector2(162, y + 2), new Vector2(26, 26));
        y -= 44f;
    }

    void BuildControls()
    {
        Heading("HOW TO PLAY");
        var goal = Text(sideContent, "Get it all done before Mom gets home at 6!", 19, Ink, new Vector2(0, -108), new Vector2(340, 50));
        goal.enableWordWrapping = true;
        (string key, string what)[] rows =
        {
            ("WASD", "walk"), ("SHIFT", "sprint"), ("SPACE", "jump"), ("E", "use / pick up / pet"),
            ("G", "drop"), ("TAB", "find things"), ("M", "map on / off"), ("ESC", "pause / get up"),
        };
        float y = -158f;
        foreach (var (k, what) in rows)
        {
            var cap = Doodle(sideContent, "keycap", Color.white, new Vector2(-120, y), new Vector2(k.Length > 2 ? 92 : 50, 40));
            cap.type = Image.Type.Simple;
            var kt = Text(cap.rectTransform, k, k.Length > 3 ? 14 : 18, Ink, Vector2.zero, new Vector2(90, 36), centre: true);
            kt.fontStyle = FontStyles.Bold;
            var w = Text(sideContent, what, 21, Ink, new Vector2(60, y), new Vector2(230, 32));
            w.alignment = TextAlignmentOptions.MidlineLeft;
            y -= 44f;
        }
    }

    void BuildSettings()
    {
        Heading("SETTINGS");
        sensitivityValue = Setting(-140f, "Mouse sensitivity", () => Sensitivity(-0.1f), () => Sensitivity(0.1f));
        volumeValue = Setting(-250f, "Volume", () => Volume(-0.1f), () => Volume(0.1f));
        Text(sideContent, "Saved for next time.", 18, InkSoft, new Vector2(0, -360), new Vector2(320, 26));
        RefreshSettings();
    }

    TextMeshProUGUI Setting(float y, string label, System.Action less, System.Action more)
    {
        var l = Text(sideContent, label, 24, Ink, new Vector2(0, y), new Vector2(330, 32));
        l.fontStyle = FontStyles.Bold;
        var value = Text(sideContent, "", 30, Red, new Vector2(0, y - 50), new Vector2(140, 40));
        value.fontStyle = FontStyles.Bold;
        SettingArrow(new Vector2(-110, y - 50), 90f, less);
        SettingArrow(new Vector2(110, y - 50), -90f, more);
        return value;
    }

    readonly List<(RectTransform rt, System.Action click)> arrows = new List<(RectTransform, System.Action)>();

    void SettingArrow(Vector2 at, float angle, System.Action click)
    {
        var a = UIFactory.Panel("Arrow", sideContent, Ink);
        a.sprite = HudArt.Get("arrow");
        UIFactory.Place(a.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), at, new Vector2(38, 38));
        a.rectTransform.localRotation = Quaternion.Euler(0, 0, angle);
        arrows.Add((a.rectTransform, click));
    }

    void Heading(string text)
    {
        arrows.Clear();
        var h = Text(sideContent, text, 36, Ink, new Vector2(0, -58), new Vector2(340, 44));
        h.fontStyle = FontStyles.Bold;
        var u = Doodle(sideContent, "underline", Red, new Vector2(0, -88), new Vector2(Mathf.Min(300, text.Length * 22), 20));
        u.rectTransform.localRotation = Quaternion.Euler(0, 0, -1.5f);
    }

    // ------------------------------------------------------------------ settings

    void Sensitivity(float delta)
    {
        var cam = FindObjectOfType<PlayerCam>(true);
        float now = cam ? cam.sensitivity : 1.5f;
        ApplySettings(Mathf.Clamp(Mathf.Round((now + delta) * 10f) / 10f, 0.2f, 6f), AudioListener.volume);
    }

    void Volume(float delta) =>
        ApplySettings(-1f, Mathf.Clamp01(Mathf.Round((AudioListener.volume + delta) * 10f) / 10f));

    static void ApplySettings(float sensitivity, float volume)
    {
        if (sensitivity > 0f)
        {
            foreach (var cam in FindObjectsOfType<PlayerCam>(true)) cam.sensitivity = sensitivity;
            PlayerPrefs.SetFloat(SensitivityPref, sensitivity);
        }
        AudioListener.volume = volume;
        PlayerPrefs.SetFloat(VolumePref, volume);
    }

    void RefreshSettings()
    {
        var cam = FindObjectOfType<PlayerCam>(true);
        if (sensitivityValue) sensitivityValue.text = (cam ? cam.sensitivity : 1.5f).ToString("0.0");
        if (volumeValue) volumeValue.text = Mathf.RoundToInt(AudioListener.volume * 100f) + "%";
    }

    void Quit()
    {
        if (Time.unscaledTime > quitArmedUntil)
        {
            quitArmedUntil = Time.unscaledTime + 3f;
            items[items.Count - 1].text.text = "REALLY? CLICK AGAIN";
            items[items.Count - 1].scale.Kick(6f);
            return;
        }
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // ------------------------------------------------------------------ input

    void Navigate()
    {
        if (Input.GetKeyDown(KeyCode.S) || Input.GetKeyDown(KeyCode.DownArrow)) Select((selected + 1) % items.Count);
        if (Input.GetKeyDown(KeyCode.W) || Input.GetKeyDown(KeyCode.UpArrow)) Select((selected + items.Count - 1) % items.Count);
        if (side == Side.Settings)
        {
            if (Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.A)) { Sensitivity(-0.1f); RefreshSettings(); }
            if (Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.D)) { Sensitivity(0.1f); RefreshSettings(); }
        }

        Vector2 mouse = Input.mousePosition;
        bool click = Input.GetMouseButtonDown(0);
        bool moved = MouseMoved();
        for (int i = 0; i < items.Count; i++)
            if ((moved || click) && i != selected && RectTransformUtility.RectangleContainsScreenPoint(items[i].rt, mouse, null))
                Select(i);

        if (click)
        {
            foreach (var (rt, action) in arrows)
                if (rt && RectTransformUtility.RectangleContainsScreenPoint(rt, mouse, null))
                {
                    action();
                    RefreshSettings();
                    rt.localScale = Vector3.one * 1.35f;
                    return;
                }
            if (!RectTransformUtility.RectangleContainsScreenPoint(items[selected].rt, mouse, null)) click = false;
        }
        if (click || Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) || Input.GetKeyDown(KeyCode.Space))
        {
            var it = items[selected];
            it.scale.Kick(-9f); // squish
            it.tilt.Kick(60f);
            it.action?.Invoke();
        }
    }

    Vector2 lastMouse;
    bool MouseMoved()
    {
        Vector2 m = Input.mousePosition;
        bool moved = (m - lastMouse).sqrMagnitude > 0.5f;
        lastMouse = m;
        return moved;
    }

    void Select(int i)
    {
        if (i == selected) return;
        selected = i;
        items[i].tilt.Kick(80f);
        items[i].scale.Kick(3f);
    }

    // ------------------------------------------------------------------ animation

    void Animate()
    {
        float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
        open = Mathf.MoveTowards(open, paused ? 1f : 0f, dt / (paused ? 0.25f : 0.18f));
        dim.color = new Color(0.08f, 0.07f, 0.12f, 0.6f * open);
        if (!paused && open <= 0f) { menuRoot.gameObject.SetActive(false); return; }

        float since = Time.unscaledTime - openedAt;
        // Notes drop in from above (and fly back up when closing).
        float drop = mainDrop.Step(paused ? 0f : 1000f, dt, paused ? 2.6f : 4f, 0.5f);
        float tilt = mainTilt.Step(paused ? 0f : -14f, dt, 2.2f, 0.35f);
        mainNote.parent.GetComponent<RectTransform>().anchoredPosition = new Vector2(-190, 10 + drop);
        mainNote.parent.localRotation = Quaternion.Euler(0, 0, tilt);
        float slide = sideSlide.Step(paused && since > 0.08f ? 0f : 1100f, dt, 2.4f, 0.55f);
        sideNote.parent.GetComponent<RectTransform>().anchoredPosition = new Vector2(300 + slide, -6);
        float flip = sideFlip.Step(1f, dt, 5f, 0.4f);
        sideNote.parent.localScale = new Vector3(Mathf.Clamp(flip, 0.05f, 1.3f), 1f, 1f);

        // Subtitle: the time, and how long until Mom's home.
        var day = DayManager.Instance;
        if (day)
        {
            float hour = day.Hour;
            int h = Mathf.FloorToInt(hour), m = Mathf.FloorToInt((hour - h) * 60f);
            int left = Mathf.Max(0, Mathf.CeilToInt((day.endHour - hour) * 60f));
            subtitle.text = $"{(h > 12 ? h - 12 : h)}:{m:00} {(h >= 12 ? "PM" : "AM")}  -  Mom's home in {(left >= 60 ? $"{left / 60}h {left % 60:00}m" : $"{left} min")}";
        }
        else subtitle.text = "Take a breather.";

        Wobble(title, 3.5f, 3f);

        // Buttons pop in one by one; the chosen one gets circled, an arrow, a bounce and a wiggle.
        for (int i = 0; i < items.Count; i++)
        {
            var it = items[i];
            bool chosen = i == selected && paused;
            float target = paused && since > it.appearDelay ? (chosen ? 1.12f : 1f) : 0f;
            float s = it.scale.Step(target, dt, 4f, 0.42f);
            float wig = it.tilt.Step(0f, dt, 3f, 0.3f);
            it.rt.localScale = new Vector3(s, s, 1f);
            float idle = chosen ? Mathf.Sin(Time.unscaledTime * 4f + i) * 1.6f : 0f;
            it.rt.localRotation = Quaternion.Euler(0, 0, wig * 0.08f + idle);
            it.text.color = chosen ? Red : Ink;
            if (i == items.Count - 1 && Time.unscaledTime > quitArmedUntil) it.text.text = it.label.ToUpperInvariant();

            float textW = it.text.GetPreferredValues(it.text.text).x;
            it.circle.gameObject.SetActive(chosen);
            it.circle.rectTransform.sizeDelta = new Vector2(textW + 70f, 74f);
            it.arrow.gameObject.SetActive(chosen);
            it.arrow.rectTransform.anchoredPosition = new Vector2(-textW * 0.5f - 46f + Mathf.Sin(Time.unscaledTime * 6f) * 6f, 0f);
        }

        foreach (var (rt, _) in arrows)
            if (rt) rt.localScale = Vector3.Lerp(rt.localScale, Vector3.one, dt * 10f);
    }

    /// <summary>Each letter bobs on its own little wave - the title never sits still.</summary>
    static void Wobble(TextMeshProUGUI text, float amount, float speed)
    {
        text.ForceMeshUpdate();
        var info = text.textInfo;
        for (int i = 0; i < info.characterCount; i++)
        {
            var c = info.characterInfo[i];
            if (!c.isVisible) continue;
            var verts = info.meshInfo[c.materialReferenceIndex].vertices;
            Vector3 offset = new Vector3(0f, Mathf.Sin(Time.unscaledTime * speed + i * 0.8f) * amount, 0f);
            for (int j = 0; j < 4; j++) verts[c.vertexIndex + j] += offset;
        }
        for (int m = 0; m < info.meshInfo.Length; m++)
        {
            info.meshInfo[m].mesh.vertices = info.meshInfo[m].vertices;
            text.UpdateGeometry(info.meshInfo[m].mesh, m);
        }
    }

    // ------------------------------------------------------------------ helpers

    TextMeshProUGUI Text(RectTransform parent, string text, float size, Color color, Vector2 at, Vector2 box, bool centre = false)
    {
        var t = UIFactory.Text(text, parent, size, color, TextAlignmentOptions.Center, font);
        t.text = text;
        t.enableWordWrapping = false;
        UIFactory.Place(t.rectTransform, centre ? new Vector2(0.5f, 0.5f) : new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), at, box);
        return t;
    }

    /// <summary>A hand-drawn doodle that "boils" between its drawn versions (underline, circle, check, keycap).</summary>
    static Image Doodle(RectTransform parent, string art, Color color, Vector2 at, Vector2 size, bool anchorCentre = false)
    {
        var frames = HudArt.Frames(art);
        var img = UIFactory.Panel(art, parent, color);
        img.sprite = frames.Length > 0 ? frames[0] : HudArt.Get(art);
        img.preserveAspect = false;
        UIFactory.Place(img.rectTransform, anchorCentre ? new Vector2(0.5f, 0.5f) : new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), at, size);
        if (frames.Length > 1)
        {
            var boil = img.gameObject.AddComponent<LineBoil>();
            boil.frames = frames;
            boil.framesPerSecond = 6f;
        }
        return img;
    }
}
