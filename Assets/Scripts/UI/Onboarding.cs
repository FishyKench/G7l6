using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Helping a new player find their feet:
///   - Every day starts in YOUR room (upstairs), facing your PC. On the first day, markers point out your PC, your desk and your VR
///     headset, and "this is your room".
///   - The very first time the game runs: a short intro (time stands still) - the core idea of the game, then the controls.
///   - Markers on the screen point at every active task (through walls), so you always know where things are going wrong.
///   - The first time each kind of task turns up, a "how to" note says what to do (the steps, not the answer).
/// Added by the GameManager.
/// </summary>
public class Onboarding : MonoBehaviour
{
    /// <summary>The intro is on screen (the game is paused, the pause menu stays shut).</summary>
    public static bool Showing { get; private set; }
    public static Onboarding Instance { get; private set; }
    /// <summary>Show a "how to" card the first time each kind of task comes up (off while the first-day tutorial runs it).</summary>
    public static bool AutoTips = true;
    /// <summary>A card is on screen or waiting.</summary>
    public bool CardBusy => tipShowing || tips.Count > 0;

    /// <summary>An intro card for something new (the tutorial uses these). 'key' marks it seen so it isn't shown again.</summary>
    public void Intro(string title, string body, string key = null)
    {
        if (key != null) PlayerPrefs.SetInt(TipKey + key, 1);
        tips.Enqueue((title, body));
    }

    /// <summary>The intro card for this task (shown now, whether or not it's been seen).</summary>
    public void IntroTask(TaskBase t)
    {
        var tip = HowTo(t);
        if (tip.title == null) return;
        string kind = t is Spill || t is PawPrints ? "FloodHazard" : t.GetType().Name;
        PlayerPrefs.SetInt(TipKey + kind, 1);
        tips.Enqueue(tip);
    }

    static bool nextPage, skipAll;
    /// <summary>Turns the intro's page (same as a click) - for the debug panel / tests.</summary>
    public static void NextPage() => nextPage = true;
    /// <summary>Skips the rest of the intro (same as Esc).</summary>
    public static void SkipIntro() => skipAll = true;

    const string IntroKey = "G7l6.IntroSeen";
    const string TipKey = "G7l6.HowTo.";

    TMP_FontAsset font;
    RectTransform canvasRoot;
    bool firstDay;
    float roomMarkersUntil;
    readonly HashSet<TaskBase> knownActive = new HashSet<TaskBase>();
    readonly Queue<(string title, string body)> tips = new Queue<(string, string)>();
    bool tipShowing;
    float nextScan;

    void Awake() => Instance = this;

    IEnumerator Start()
    {
        font = GameHUD.Instance ? GameHUD.Instance.font : null;
        var canvas = UIFactory.CreateCanvas("Onboarding", 58);
        canvasRoot = (RectTransform)canvas.transform;
        yield return null;
        yield return null; // (the player and the house are set up)

        StartInYourRoom();
        firstDay = Campaign.Week == 1 && Campaign.DayIndex == 0;
        if (TutorialDay.Active) { PlayerPrefs.SetInt(IntroKey, 1); yield break; } // (the tutorial shows you round instead: AfterTutorial)
        if (PlayerPrefs.GetInt(IntroKey, 0) == 0)
        {
            yield return Intro();
            PlayerPrefs.SetInt(IntroKey, 1);
            PlayerPrefs.Save();
            firstDay = true;
        }
        if (firstDay)
        {
            roomMarkersUntil = Time.time + 40f;
            GameHUD.Toast("This is YOUR room: your PC, your desk and your VR headset are here.", GameHUD.Info);
        }
    }

    /// <summary>The tutorial's over: the first-day labels on your room's things, and the welcome note.</summary>
    public void AfterTutorial()
    {
        roomMarkersUntil = Time.time + 40f;
        GameHUD.Toast("This is YOUR room: your PC, your desk and your VR headset are here.", GameHUD.Info);
    }

    void Update()
    {
        // first day: what's what in your room
        if (Time.time < roomMarkersUntil) RoomMarkers();

        // every active task: a marker; a new kind of task: how to do it
        if (Time.time >= nextScan)
        {
            nextScan = Time.time + 0.5f;
            var gm = GameManager.Instance;
            if (gm)
                foreach (var t in gm.Tasks)
                {
                    if (!t) continue;
                    if (t.IsActive && knownActive.Add(t)) FirstTimeTip(t);
                    else if (!t.IsActive) knownActive.Remove(t);
                }
        }
        TaskMarkers();
        if (!tipShowing && tips.Count > 0 && !Showing) StartCoroutine(ShowTip(tips.Dequeue()));
    }

    // ------------------------------------------------------------------ your room

    /// <summary>Puts you in your room at the start of the day, on open floor, facing your PC.</summary>
    void StartInYourRoom()
    {
        var layout = HouseLayout.Instance;
        var mover = FindObjectOfType<PlayerMovementAdvanced>();
        if (!layout || !mover) return;
        HouseLayout.RoomInfo room = null;
        foreach (var r in layout.rooms) if (r.name == "Your bedroom") room = r;
        if (room == null) return;
        float s = layout.scale;
        float floorY = layout.transform.position.y + room.floor * layout.storeyHeight * s;
        var col = mover.GetComponentInChildren<CapsuleCollider>();
        float radius = col ? Mathf.Max(0.25f, col.radius * mover.transform.lossyScale.x) : 0.35f;
        float height = col ? col.height * mover.transform.lossyScale.y : 1.8f;
        Vector3 centre = layout.transform.TransformPoint(new Vector3(room.rect.center.x, room.floor * layout.storeyHeight, room.rect.center.y));
        Vector3 best = Vector3.zero;
        float bestD = float.MaxValue;
        for (float x = room.rect.xMin + 0.7f; x <= room.rect.xMax - 0.7f; x += 0.35f)
            for (float z = room.rect.yMin + 0.7f; z <= room.rect.yMax - 0.7f; z += 0.35f)
            {
                Vector3 p = layout.transform.TransformPoint(new Vector3(x, room.floor * layout.storeyHeight, z));
                p.y = floorY;
                Vector3 a = p + Vector3.up * (radius + 0.08f), b = p + Vector3.up * (height - radius);
                bool blocked = false;
                foreach (var c in Physics.OverlapCapsule(a, b, radius * 1.15f, ~0, QueryTriggerInteraction.Ignore))
                    if (!c.transform.IsChildOf(mover.transform)) { blocked = true; break; }
                if (blocked) continue;
                if (!Physics.Raycast(p + Vector3.up * 0.5f, Vector3.down, 1f, ~0, QueryTriggerInteraction.Ignore)) continue; // floor under it
                float d = (p - centre).sqrMagnitude;
                if (d < bestD) { bestD = d; best = p; }
            }
        if (bestD == float.MaxValue) return;
        var rb = mover.GetComponent<Rigidbody>();
        float pivot = col ? mover.transform.position.y - col.bounds.min.y : 1f;
        Vector3 at = best + Vector3.up * (pivot + 0.05f);
        if (rb) { rb.velocity = Vector3.zero; rb.position = at; }
        mover.transform.position = at;
        var pc = FindObjectOfType<PlayerCam>();
        var target = GamingPC.Instance ? GamingPC.Instance.transform.position : centre;
        if (pc) pc.LookTowards(target + Vector3.up * 0.4f * s);
    }

    void RoomMarkers()
    {
        var layout = HouseLayout.Instance;
        if (!layout) return;
        if (GamingPC.Instance) ScreenMarkers.Show("room.pc", GamingPC.Instance.transform.position + Vector3.up * 0.9f, "YOUR PC - ranked, mail, shop", HudArt.NoteBlue);
        var hw = FindObjectOfType<TypingTask>();
        if (hw && (!GamingPC.Instance || Vector3.Distance(hw.Where, GamingPC.Instance.transform.position) > 1.2f))
            ScreenMarkers.Show("room.desk", hw.Where + Vector3.up * 0.6f, "YOUR DESK - homework", HudArt.NoteBlue);
        if (VRHeadset.Instance) ScreenMarkers.Show("room.vr", VRHeadset.Instance.transform.position + Vector3.up * 0.4f, "VR HEADSET - your match", HudArt.NoteBlue);
    }

    // ------------------------------------------------------------------ task markers

    void TaskMarkers()
    {
        var gm = GameManager.Instance;
        var cam = Camera.main;
        if (!gm || !cam) return;
        float s = HouseLayout.Instance ? HouseLayout.Instance.scale : 1f;
        Vector3 eye = cam.transform.position;
        bool firstDay = Campaign.Week == 1 && Campaign.DayIndex == 0;
        foreach (var t in gm.Tasks)
        {
            if (!t || !t.IsActive) continue;
            if (!firstDay && !(t is HazardTask)) continue; // (after day one only the emergencies get a tag - TAB and the footprints do the rest)
            if (TaskGuide.Instance && TaskGuide.Instance.Guiding == t) continue; // (the footprints already show it)
            if (t is Cooking c && c.GuideShowing) continue; // (in the kitchen the cooking guide does it)
            if (t is MatchTask || t is TypingTask) continue;   // (always at your headset / desk: no marker needed)
            Vector3 where = t.Where;
            bool hazard = t is HazardTask;
            if (Mathf.Abs(where.y - eye.y) > 2.6f * s) continue;   // another floor: the map shows it
            float d = Vector3.Distance(eye, where);
            if (d < 2.2f * s || d > (hazard ? 14f : 9f) * s) continue; // you're there / it's far away
            Color colour = hazard ? HudArt.NoteCoral : t.isMainTask ? HudArt.NoteBlue : HudArt.NoteYellow;
            ScreenMarkers.Show("task." + t.GetInstanceID(), where + Vector3.up * 0.7f * s, Short(t), colour, hazard);
        }
    }

    static string Short(TaskBase t)
    {
        string n = t.taskName.TrimEnd('!');
        return n.Length > 26 ? n.Substring(0, 25) + "..." : n;
    }

    // ------------------------------------------------------------------ how-to notes

    void FirstTimeTip(TaskBase t)
    {
        if (!AutoTips) return;
        string kind = t is Spill || t is PawPrints ? "FloodHazard" : t.GetType().Name;
        if (PlayerPrefs.GetInt(TipKey + kind, 0) == 1) return;
        var tip = HowTo(t);
        if (tip.title == null) return;
        PlayerPrefs.SetInt(TipKey + kind, 1);
        tips.Enqueue(tip);
    }

    static (string title, string body) HowTo(TaskBase t)
    {
        switch (t)
        {
            case TypingTask _: return ("Homework", "Sit at your desk (E). Answer the sum, copy the line. Done!");
            case MatchTask _: return ("Ranked match", "Put the VR headset on in your room (E). Esc takes it off.");
            case Cooking _: return ("Dinner", "Go to the kitchen. Follow the orange glow, one step at a time.");
            case TvTask _: return ("TV static", "Find the remote (hold Tab to see it). Then E on the TV.");
            case LaundryTask _: return ("Laundry", "Pick up the clothes (E). Washer upstairs. Then the wardrobe.");
            case FireHazard _: return ("Fire!", "Grab the extinguisher. Hold Left Mouse at the flames.");
            case FloodHazard _: case PawPrints _: return ("Water on the floor", "Grab the mop. Hold Left Mouse and sweep over it.");
            case CeilingLeakTask _: return ("Leak", "Bucket under the drip. Then turn the tap off upstairs.");
            case OvenTask _: return ("Oven", "Take it out (E) before it burns.");
            case FuseBox _: return ("Power cut", "Garage wall: fuse box (E), flip the switch. Mop any water first!");
        }
        return (null, null);
    }

    IEnumerator ShowTip((string title, string body) tip)
    {
        tipShowing = true;
        const float W = 700f, H = 74f;
        var card = UIFactory.Rect("How to", canvasRoot);
        UIFactory.Place(card, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, 120f), new Vector2(W, H));
        var group = card.gameObject.AddComponent<CanvasGroup>();
        var paper = UIFactory.Panel("Paper", card, HudArt.NoteBlue);
        paper.sprite = HudArt.Get("note_paper");
        paper.type = Image.Type.Sliced;
        UIFactory.Fill(paper.rectTransform);
        var tape = UIFactory.Panel("Tape", card, Color.white);
        tape.sprite = HudArt.Get("tape");
        UIFactory.Place(tape.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), new Vector2(0f, -2f), new Vector2(120f, 32f));
        var body = UIFactory.Text("Body", card, 21, HudArt.Ink, TextAlignmentOptions.Center, font);
        body.text = $"<b>{tip.title.ToUpperInvariant()}:</b>  {tip.body}";
        body.enableWordWrapping = true;
        UIFactory.Fill(body.rectTransform, 24, 24, 12, 10);
        Vector2 home = new Vector2(0f, -205f), away = new Vector2(0f, 120f); // under the clock and the toasts
        for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / 0.35f)
        {
            float e = 1f - (1f - t) * (1f - t);
            card.anchoredPosition = Vector2.Lerp(away, home, e);
            group.alpha = VRHeadset.Worn ? 0f : 1f;
            yield return null;
        }
        float until = Time.unscaledTime + 9f;
        while (Time.unscaledTime < until && !Input.GetKeyDown(KeyCode.H))
        {
            group.alpha = VRHeadset.Worn || PauseMenu.IsPaused ? 0f : 1f;
            if (VRHeadset.Worn) until += Time.unscaledDeltaTime; // (wait till you're out of VR to read it)
            yield return null;
        }
        for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / 0.3f)
        {
            card.anchoredPosition = Vector2.Lerp(home, away, t * t);
            yield return null;
        }
        Destroy(card.gameObject);
        tipShowing = false;
    }

    // ------------------------------------------------------------------ the intro (first time the game runs)

    static readonly (string title, string body)[] Pages =
    {
        ("3:00 PM. You just got home.",
         "Mom is at work until <b>6:00 PM</b>. Before she left she said: keep the house in one piece and get your homework <b>DONE</b>.\n\n" +
         "When she walks in, she checks <b>everything</b> - and gives you a report card. A good grade means allowance. A bad one... means trouble."),
        ("But at 4:00 PM: RANKED.",
         "Your ranked <b>VR Domination</b> match starts at 4. Your team needs you - and rounds start with or without you.\n\n" +
         "Climb the ranks, grow your channel, buy better gear... if you can keep the house from falling apart."),
        ("The house WILL fall apart.",
         "TVs blare static. The washer overflows. Dinner needs cooking. Kitty knocks things over and steals the remote.\n\n" +
         "Leave a problem too long and it starts <b>new</b> ones - a flood reaches a socket and the power goes. " +
         "Watch your <b>stress</b>: if it maxes out, you melt down."),
        ("How to play",
         "<b>WASD</b> move     <b>Shift</b> run     <b>Space</b> jump\n<b>E</b> use / pick up     <b>G</b> drop     <b>Esc</b> pause\n" +
         "<b>Hold Tab</b>: your whole to-do list. Press a job's number and footprints show you the way.\n\n" +
         "The most urgent job is top right, the map top left.\nYour room is upstairs: your <b>PC</b>, your <b>desk</b> and your <b>VR headset</b>."),
    };

    IEnumerator Intro()
    {
        Showing = true;
        nextPage = skipAll = false;
        float oldScale = Time.timeScale;
        Time.timeScale = 0f;
        var cams = FindObjectsOfType<PlayerCam>();
        foreach (var c in cams) c.enabled = false;
        var oldLock = Cursor.lockState;

        var dim = UIFactory.Panel("Dim", canvasRoot, new Color(0.03f, 0.03f, 0.05f, 0.88f));
        UIFactory.Fill(dim.rectTransform);
        for (int i = 0; i < Pages.Length; i++)
        {
            const float W = 980f, H = 560f;
            var card = UIFactory.Rect("Intro card", canvasRoot);
            UIFactory.Place(card, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(W, H));
            card.localRotation = Quaternion.Euler(0f, 0f, i % 2 == 0 ? -1.2f : 1f);
            var group = card.gameObject.AddComponent<CanvasGroup>();
            var shadow = UIFactory.Panel("Shadow", card, new Color(0f, 0f, 0f, 0.35f));
            UIFactory.Fill(shadow.rectTransform, 12, -12, 14, -14);
            var paper = UIFactory.Panel("Paper", card, i == Pages.Length - 1 ? HudArt.NoteYellow : HudArt.PaperWhite);
            paper.sprite = HudArt.Get("note_paper");
            paper.type = Image.Type.Sliced;
            UIFactory.Fill(paper.rectTransform);
            var tape = UIFactory.Panel("Tape", card, Color.white);
            tape.sprite = HudArt.Get("tape");
            UIFactory.Place(tape.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), new Vector2(0f, -2f), new Vector2(170f, 44f));
            var title = UIFactory.Text("Title", card, 52, HudArt.Ink, TextAlignmentOptions.TopLeft, font);
            title.text = "<b>" + Pages[i].title + "</b>";
            title.enableWordWrapping = true;
            UIFactory.Fill(title.rectTransform, 64, 64, 56, H - 140);
            var line = UIFactory.Panel("Underline", card, HudArt.MarkerRed);
            line.sprite = HudArt.Get("underline_0");
            UIFactory.Place(line.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(64f, -138f), new Vector2(360f, 14f));
            var body = UIFactory.Text("Body", card, 26, HudArt.Ink, TextAlignmentOptions.TopLeft, font);
            body.text = Pages[i].body;
            body.enableWordWrapping = true;
            body.lineSpacing = 6f;
            UIFactory.Fill(body.rectTransform, 64, 64, 168, 86);
            var foot = UIFactory.Text("Foot", card, 20, HudArt.InkSoft, TextAlignmentOptions.BottomLeft, font);
            foot.text = $"<b>Click</b> or <b>Space</b> to continue      {i + 1} / {Pages.Length}      <b>Esc</b> skip";
            UIFactory.Fill(foot.rectTransform, 64, 64, H - 70, 34);

            for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / 0.3f)
            {
                float e = 1f - (1f - t) * (1f - t);
                group.alpha = e;
                card.localScale = Vector3.one * Mathf.Lerp(0.92f, 1f, e);
                yield return null;
            }
            group.alpha = 1f;
            card.localScale = Vector3.one;
            float shownAt = Time.unscaledTime;
            bool skip = false;
            while (true)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                if (Input.GetKeyDown(KeyCode.Escape) || skipAll) { skip = true; break; }
                if (nextPage || (Time.unscaledTime - shownAt > 0.35f && (Input.GetMouseButtonDown(0) || Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return)))) { nextPage = false; break; }
                yield return null;
            }
            for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / 0.2f)
            {
                group.alpha = 1f - t;
                card.anchoredPosition = new Vector2(0f, -t * 60f);
                yield return null;
            }
            Destroy(card.gameObject);
            if (skip) break;
        }
        for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / 0.4f)
        {
            dim.color = new Color(0.03f, 0.03f, 0.05f, 0.88f * (1f - t));
            yield return null;
        }
        Destroy(dim.gameObject);
        foreach (var c in cams) if (c) c.enabled = true;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        Time.timeScale = oldScale > 0f ? oldScale : 1f;
        Showing = false;
        yield return null;
        Input.ResetInputAxes(); // (the click that closed it isn't a click in the game)
    }

    /// <summary>Debug / for testers: the intro and the how-to notes show again (next time the day starts).</summary>
    public static void ForgetSeen()
    {
        PlayerPrefs.DeleteKey(IntroKey);
        PlayerPrefs.DeleteKey("G7l6.pc.homeworkCircled"); // (the PC circles Homework again)
        foreach (var k in new[] { "TypingTask", "MatchTask", "Cooking", "TvTask", "LaundryTask", "FireHazard", "FloodHazard", "CeilingLeakTask", "OvenTask", "FuseBox" })
            PlayerPrefs.DeleteKey(TipKey + k);
    }
}
