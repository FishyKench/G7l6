using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// The main menu: your room at golden hour (a copy of your bedroom, made by G7l6 > Build Main Menu Scene). The camera drifts and
/// leans with the mouse; the menu is handwritten notes pinned to a corkboard (Mom's note on top); the PC shows the VR arena live;
/// Kitty naps on the bed (click her); your phone buzzes; birds go past the window. PLAY pushes in to the desk, stamps 3:00 PM and
/// the day starts.
/// </summary>
public class MainMenu : MonoBehaviour
{
    [Header("Set by the menu builder")]
    public Camera cam;
    public Transform lookAt;            // the desk / PC
    public Renderer[] monitorScreens;   // a quad over each monitor at the desk: the arena feed goes on the one you have
    public Vector3[] catSpots;          // open floor for the cat, and the clear paths between them (pairs of indices)
    public int[] catLinks;
    public Vector3[] catPerchTops;      // places she jumps up to (the desk, the bed, a sill...), each with the floor spot she jumps from
    public int[] catPerchLaunch;
    public string[] catPerchKinds;
    public Transform cat;               // the cat model (napping)
    public Transform windowCentre;      // where the birds fly past
    public string gameScene = "NewHouse";
    public string arenaScene = "VRArena";
    public TMP_FontAsset font;          // the HUD's handwritten font

    const string SensitivityPref = "G7l6.mouseSensitivity", VolumePref = "G7l6.volume";

    RectTransform root, board, settingsCard, phone;
    Vector3 camHome;
    Quaternion camRot;
    float drift;
    bool leaving;
    readonly List<(RectTransform rt, float baseTilt)> notes = new List<(RectTransform, float)>();
    RenderTexture feed;
    Camera feedCam;
    /// <summary>The monitor screen the feed is on (the one your setup has).</summary>
    public Renderer MonitorShowing { get; private set; }
    float feedAngle;

    void Start()
    {
        Time.timeScale = 1f;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        if (!font) font = TMP_Settings.defaultFontAsset;
        AudioListener.volume = PlayerPrefs.GetFloat(VolumePref, 1f);
        if (!cam) cam = Camera.main;
        camHome = cam.transform.position;
        camRot = cam.transform.rotation;
        if (!FindObjectOfType<EventSystem>()) new GameObject("Event System", typeof(EventSystem), typeof(StandaloneInputModule));
        BuildUI();
        WarmGrade();
        StartCoroutine(ArenaFeed());
        StartCoroutine(Texts());
        StartCoroutine(Birds());
        if (cat) { var mc = cat.gameObject.AddComponent<MenuCat>(); mc.Routes(catSpots, catLinks, cam); mc.Perches(catPerchTops, catPerchLaunch, catPerchKinds); if (windowCentre) mc.Window(windowCentre.position); }
    }

    void Update()
    {
        // (the arena running on the monitor locks the cursor for its player: on the menu it's always free)
        if (Cursor.lockState != CursorLockMode.None) Cursor.lockState = CursorLockMode.None;
        if (!Cursor.visible) Cursor.visible = true;
        if (leaving) return;
        // the camera breathes and leans a little towards the mouse
        drift += Time.deltaTime;
        Vector2 m = new Vector2(Input.mousePosition.x / Mathf.Max(1, Screen.width) - 0.5f, Input.mousePosition.y / Mathf.Max(1, Screen.height) - 0.5f);
        Vector3 sway = new Vector3(Mathf.Sin(drift * 0.23f) * 0.06f, Mathf.Sin(drift * 0.31f) * 0.03f, 0f);
        cam.transform.position = Vector3.Lerp(cam.transform.position, camHome + cam.transform.right * (sway.x + m.x * 0.12f) + Vector3.up * (sway.y + m.y * 0.06f), Time.deltaTime * 2f);
        cam.transform.rotation = Quaternion.Slerp(cam.transform.rotation, camRot * Quaternion.Euler(-m.y * 3f, m.x * 5f, 0f), Time.deltaTime * 2f);
        // the notes sway on their pins
        for (int i = 0; i < notes.Count; i++)
        {
            var (rt, tilt) = notes[i];
            if (!rt) continue;
            float hover = rt.localScale.x > 1.01f ? 1f : 0f;
            rt.localRotation = Quaternion.Euler(0f, 0f, tilt + Mathf.Sin(Time.time * 1.3f + i) * (0.6f + hover * 1.6f));
        }
        if (feedCam) FeedCamera();
    }

    /// <summary>Golden hour: a warm colour grade and a soft vignette, just for the menu (on top of the game's own look).</summary>
    void WarmGrade()
    {
        int layer = 0;
        var ppl = cam ? cam.GetComponent<UnityEngine.Rendering.PostProcessing.PostProcessLayer>() : null;
        if (ppl) for (int i = 0; i < 32; i++) if ((ppl.volumeLayer.value & (1 << i)) != 0) { layer = i; break; }
        var go = new GameObject("Golden hour grade") { layer = layer };
        var v = go.AddComponent<UnityEngine.Rendering.PostProcessing.PostProcessVolume>();
        v.isGlobal = true;
        v.priority = 80f;
        var p = ScriptableObject.CreateInstance<UnityEngine.Rendering.PostProcessing.PostProcessProfile>();
        var g = p.AddSettings<UnityEngine.Rendering.PostProcessing.ColorGrading>();
        g.enabled.Override(true);
        g.temperature.Override(22f);
        g.tint.Override(4f);
        g.saturation.Override(14f);
        var vig = p.AddSettings<UnityEngine.Rendering.PostProcessing.Vignette>();
        vig.enabled.Override(true);
        vig.intensity.Override(0.3f);
        vig.smoothness.Override(0.5f);
        v.sharedProfile = p;
    }

    // ------------------------------------------------------------------ the corkboard

    static readonly Color Cork = new Color32(176, 128, 82, 255), CorkDark = new Color32(120, 84, 52, 255);

    void BuildUI()
    {
        var canvas = UIFactory.CreateCanvas("Main menu", 10);
        canvas.gameObject.AddComponent<GraphicRaycaster>();
        root = (RectTransform)canvas.transform;

        // the title, top left, in marker
        var title = UIFactory.Text("Title", root, 112, Color.white, TextAlignmentOptions.TopLeft, font);
        title.text = "<b>G7L6</b>";
        title.outlineWidth = 0.18f;
        title.outlineColor = new Color32(30, 24, 30, 255);
        UIFactory.Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(70f, -40f), new Vector2(700f, 130f));
        var strip = UIFactory.Panel("Tagline strip", root, HudArt.PaperWhite);
        strip.sprite = HudArt.Get("note_paper");
        strip.type = Image.Type.Sliced;
        strip.raycastTarget = false;
        UIFactory.Place(strip.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(62f, -164f), new Vector2(640f, 48f));
        strip.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -1f);
        var tag = UIFactory.Text("Tagline", strip.transform, 25, HudArt.Ink, TextAlignmentOptions.Center, font);
        tag.text = "Keep the house alive. Win your match. Mom's home at 6.";
        UIFactory.Fill(tag.rectTransform, 12, 12, 4, 4);

        // the board
        board = UIFactory.Rect("Corkboard", root);
        UIFactory.Place(board, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(60f, -70f), new Vector2(470f, 640f));
        board.localRotation = Quaternion.Euler(0f, 0f, 1.2f);
        var frame = UIFactory.Panel("Frame", board, CorkDark);
        UIFactory.Fill(frame.rectTransform);
        var cork = UIFactory.Panel("Cork", board, Cork);
        UIFactory.Fill(cork.rectTransform, 14, 14, 14, 14);
        // a speckle of cork
        var rnd = new System.Random(3);
        for (int i = 0; i < 70; i++)
        {
            var dot = UIFactory.Panel("Speck", cork.transform, new Color(0.45f, 0.3f, 0.18f, 0.35f));
            dot.raycastTarget = false;
            UIFactory.Place(dot.rectTransform, Vector2.zero, new Vector2(0.5f, 0.5f), new Vector2((float)rnd.NextDouble() * 440f, (float)rnd.NextDouble() * 610f), Vector2.one * (2f + (float)rnd.NextDouble() * 3f));
        }

        // Mom's note
        var mom = Note(board, new Vector2(0f, -40f), new Vector2(380f, 150f), HudArt.NoteYellow, -2.5f, null);
        var momText = UIFactory.Text("Mom", mom, 25, HudArt.Ink, TextAlignmentOptions.TopLeft, font);
        momText.text = "Home at <b>6</b>.\nHouse clean. Homework <b><u>DONE</u></b>.\n<size=80%>Love you - Mom x</size>";
        UIFactory.Fill(momText.rectTransform, 20, 16, 20, 12);

        // the buttons
        bool hasSave = Campaign.Week > 1 || Campaign.DayIndex > 0 || Wallet.Money > 0.01f;
        string play = hasSave ? $"CONTINUE\n<size=55%>Week {Campaign.Week}, {Campaign.DayName}</size>" : "PLAY";
        float y = -220f;
        Button(play, ref y, HudArt.PaperWhite, 1.4f, Play);
        if (hasSave) Button("NEW GAME", ref y, HudArt.NoteBlue, -1.8f, NewGame);
        Button("SETTINGS", ref y, HudArt.NoteBlue, 2.1f, () => ShowSettings(true));
        Button("QUIT", ref y, HudArt.NoteCoral, -1.2f, Quit);
        BuildSettings();
        BuildPhone();
    }

    RectTransform Note(RectTransform parent, Vector2 at, Vector2 size, Color colour, float tilt, System.Action click)
    {
        var rt = UIFactory.Rect("Note", parent);
        UIFactory.Place(rt, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), at, size);
        var shadow = UIFactory.Panel("Shadow", rt, new Color(0f, 0f, 0f, 0.3f));
        UIFactory.Fill(shadow.rectTransform, 6, -6, 8, -8);
        shadow.raycastTarget = false;
        var paper = UIFactory.Panel("Paper", rt, colour);
        paper.sprite = HudArt.Get("note_paper");
        paper.type = Image.Type.Sliced;
        UIFactory.Fill(paper.rectTransform);
        var pin = UIFactory.Panel("Pin", rt, HudArt.MarkerRed);
        pin.sprite = HudArt.Get("circle_0");
        pin.raycastTarget = false;
        UIFactory.Place(pin.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), new Vector2(0f, -10f), new Vector2(20f, 20f));
        notes.Add((rt, tilt));
        if (click != null)
        {
            paper.raycastTarget = true; // (panels are click-through by default)
            var b = paper.gameObject.AddComponent<UnityEngine.UI.Button>();
            b.transition = Selectable.Transition.None;
            b.onClick.AddListener(() => { if (!leaving) click(); });
            var hover = paper.gameObject.AddComponent<NoteHover>();
            hover.target = rt;
        }
        return rt;
    }

    void Button(string label, ref float y, Color colour, float tilt, System.Action click)
    {
        var rt = Note(board, new Vector2(0f, y), new Vector2(330f, 82f), colour, tilt, click);
        var t = UIFactory.Text("Label", rt, 40, HudArt.Ink, TextAlignmentOptions.Center, font);
        t.text = "<b>" + label + "</b>";
        t.raycastTarget = false;
        UIFactory.Fill(t.rectTransform, 10, 10, 6, 6);
        y -= 98f;
    }

    // ------------------------------------------------------------------ settings

    TextMeshProUGUI volumeText, sensText, qualityText, screenText;

    void BuildSettings()
    {
        settingsCard = Note(root, new Vector2(0f, -1400f), new Vector2(620f, 470f), HudArt.PaperWhite, -1f, null);
        settingsCard.anchorMin = settingsCard.anchorMax = new Vector2(0.5f, 0.5f);
        settingsCard.pivot = new Vector2(0.5f, 0.5f);
        var head = UIFactory.Text("Head", settingsCard, 46, HudArt.Ink, TextAlignmentOptions.TopLeft, font);
        head.text = "<b>SETTINGS</b>";
        UIFactory.Fill(head.rectTransform, 40, 40, 30, 380);
        volumeText = Row(settingsCard, 120f, "Volume", () => Volume(-0.1f), () => Volume(0.1f));
        sensText = Row(settingsCard, 190f, "Mouse sensitivity", () => Sens(-0.1f), () => Sens(0.1f));
        qualityText = Row(settingsCard, 260f, "Graphics", () => Quality(-1), () => Quality(1));
        screenText = Row(settingsCard, 330f, "Screen", () => { Screen.fullScreen = !Screen.fullScreen; Invoke(nameof(RefreshSettings), 0.1f); }, () => { Screen.fullScreen = !Screen.fullScreen; Invoke(nameof(RefreshSettings), 0.1f); });
        var back = UIFactory.Panel("Back", settingsCard, HudArt.NoteBlue);
        back.sprite = HudArt.Get("note_paper");
        back.type = Image.Type.Sliced;
        UIFactory.Place(back.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 26f), new Vector2(200f, 56f));
        var bt = UIFactory.Text("Label", back.transform, 30, HudArt.Ink, TextAlignmentOptions.Center, font);
        bt.text = "<b>DONE</b>";
        bt.raycastTarget = false;
        UIFactory.Fill(bt.rectTransform);
        back.raycastTarget = true;
        back.gameObject.AddComponent<UnityEngine.UI.Button>().onClick.AddListener(() => ShowSettings(false));
        back.gameObject.AddComponent<NoteHover>().target = back.rectTransform;
        RefreshSettings();
    }

    TextMeshProUGUI Row(RectTransform card, float y, string label, System.Action less, System.Action more)
    {
        var l = UIFactory.Text("Label", card, 28, HudArt.Ink, TextAlignmentOptions.MidlineLeft, font);
        l.text = label;
        UIFactory.Place(l.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(44f, -y), new Vector2(280f, 50f));
        var v = UIFactory.Text("Value", card, 28, HudArt.Ink, TextAlignmentOptions.Center, font);
        UIFactory.Place(v.rectTransform, new Vector2(1f, 1f), new Vector2(0.5f, 0.5f), new Vector2(-150f, -y), new Vector2(150f, 50f));
        Arrow(card, new Vector2(-250f, -y), "<", less);
        Arrow(card, new Vector2(-50f, -y), ">", more);
        return v;
    }

    void Arrow(RectTransform card, Vector2 at, string glyph, System.Action act)
    {
        var b = UIFactory.Panel("Arrow " + glyph, card, HudArt.Kraft);
        b.sprite = HudArt.Get("keycap_0");
        UIFactory.Place(b.rectTransform, new Vector2(1f, 1f), new Vector2(0.5f, 0.5f), at, new Vector2(48f, 48f));
        var t = UIFactory.Text("Glyph", b.transform, 30, HudArt.Ink, TextAlignmentOptions.Center, font);
        t.text = "<b>" + glyph + "</b>";
        t.raycastTarget = false;
        UIFactory.Fill(t.rectTransform);
        b.raycastTarget = true;
        b.gameObject.AddComponent<UnityEngine.UI.Button>().onClick.AddListener(() => { act(); RefreshSettings(); });
        b.gameObject.AddComponent<NoteHover>().target = b.rectTransform;
    }

    void Volume(float d)
    {
        float v = Mathf.Clamp01(Mathf.Round((PlayerPrefs.GetFloat(VolumePref, 1f) + d) * 10f) / 10f);
        PlayerPrefs.SetFloat(VolumePref, v);
        AudioListener.volume = v;
    }

    void Sens(float d)
    {
        float s = PlayerPrefs.GetFloat(SensitivityPref, 1.5f);
        s = Mathf.Clamp(Mathf.Round((s + d) * 10f) / 10f, 0.2f, 6f);
        PlayerPrefs.SetFloat(SensitivityPref, s);
    }

    void Quality(int d)
    {
        int q = Mathf.Clamp(QualitySettings.GetQualityLevel() + d, 0, QualitySettings.names.Length - 1);
        QualitySettings.SetQualityLevel(q, true);
        PlayerPrefs.SetInt("G7l6.quality", q);
    }

    void RefreshSettings()
    {
        if (!volumeText) return;
        volumeText.text = Mathf.RoundToInt(PlayerPrefs.GetFloat(VolumePref, 1f) * 100f) + "%";
        sensText.text = Mathf.RoundToInt(PlayerPrefs.GetFloat(SensitivityPref, 1.5f) / 1.5f * 100f) + "%"; // (the handwriting font has no decimal point)
        qualityText.text = QualitySettings.names[QualitySettings.GetQualityLevel()];
        screenText.text = Screen.fullScreen ? "Fullscreen" : "Window";
    }

    void ShowSettings(bool on) => StartCoroutine(Slide(settingsCard, on ? new Vector2(0f, 20f) : new Vector2(0f, -1400f)));

    IEnumerator Slide(RectTransform rt, Vector2 to)
    {
        Vector2 from = rt.anchoredPosition;
        for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / 0.35f)
        {
            float e = 1f - (1f - t) * (1f - t);
            rt.anchoredPosition = Vector2.LerpUnclamped(from, to, e);
            yield return null;
        }
        rt.anchoredPosition = to;
    }

    // ------------------------------------------------------------------ play / new game / quit

    float newGameArmed;

    void NewGame()
    {
        if (Time.unscaledTime > newGameArmed)
        {
            newGameArmed = Time.unscaledTime + 3f;
            GameHUDLessToast("Click NEW GAME again to wipe your progress and start over.");
            return;
        }
        Wallet.ResetAll(); // (money, shop, mail, channel, campaign - everything back to day one)
        Trophies.ResetAll();
        PlayerPrefs.Save();
        Play();
    }

    void GameHUDLessToast(string text)
    {
        var t = UIFactory.Text("Toast", root, 26, Color.white, TextAlignmentOptions.Center, font);
        t.text = text;
        t.outlineWidth = 0.25f;
        t.outlineColor = new Color32(20, 16, 20, 230);
        UIFactory.Place(t.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 60f), new Vector2(1200f, 40f));
        Destroy(t.gameObject, 3f);
    }

    void Play()
    {
        if (leaving) return;
        leaving = true;
        StartCoroutine(Leave());
    }

    IEnumerator Leave()
    {
        // push in towards the desk, then 3:00 PM
        var black = UIFactory.Panel("Black", root, new Color(0f, 0f, 0f, 0f));
        UIFactory.Fill(black.rectTransform);
        var stamp = UIFactory.Text("Stamp", root, 150, new Color(0.95f, 0.25f, 0.2f, 0f), TextAlignmentOptions.Center, font);
        stamp.text = "<b>3:00 PM</b>";
        UIFactory.Place(stamp.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1000f, 220f));
        var load = SceneManager.LoadSceneAsync(gameScene);
        load.allowSceneActivation = false;
        Vector3 from = cam.transform.position;
        Vector3 to = lookAt ? Vector3.Lerp(from, lookAt.position + Vector3.up * 0.4f, 0.55f) : from + cam.transform.forward;
        Quaternion fromRot = cam.transform.rotation, toRot = lookAt ? Quaternion.LookRotation(lookAt.position + Vector3.up * 0.3f - from) : fromRot;
        for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / 1.1f)
        {
            float e = Mathf.SmoothStep(0f, 1f, t);
            cam.transform.position = Vector3.Lerp(from, to, e);
            cam.transform.rotation = Quaternion.Slerp(fromRot, toRot, e);
            black.color = new Color(0f, 0f, 0f, Mathf.Clamp01((t - 0.45f) * 2f));
            if (root) foreach (Transform c in root) if (c != black.transform && c != stamp.transform) c.localScale = Vector3.one * (1f - e * 0.1f);
            yield return null;
        }
        black.color = Color.black;
        for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / 0.25f)
        {
            stamp.color = new Color(0.95f, 0.25f, 0.2f, t);
            stamp.rectTransform.localScale = Vector3.one * Mathf.Lerp(2.2f, 1f, t * t);
            yield return null;
        }
        yield return new WaitForSecondsRealtime(0.7f);
        while (load.progress < 0.89f) yield return null;
        load.allowSceneActivation = true;
    }

    void Quit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // ------------------------------------------------------------------ alive: the arena on the PC, texts, birds

    IEnumerator ArenaFeed()
    {
        Renderer monitorScreen = null;
        if (monitorScreens != null) foreach (var r in monitorScreens) if (r && r.gameObject.activeInHierarchy) { monitorScreen = r; break; }
        MonitorShowing = monitorScreen;
        if (!monitorScreen || string.IsNullOrEmpty(arenaScene) || !Application.CanStreamedLevelBeLoaded(arenaScene)) yield break;
        var load = SceneManager.LoadSceneAsync(arenaScene, LoadSceneMode.Additive);
        while (!load.isDone) yield return null;
        float wait = 0f;
        while ((!VRArena.Instance || !VRArena.Instance.Ready) && wait < 10f) { wait += Time.deltaTime; yield return null; }
        var arena = VRArena.Instance;
        if (!arena) yield break;
        // the arena's own player camera and HUD stay off: we film it from a drone
        foreach (var c in arena.GetComponentsInChildren<Camera>(true)) c.enabled = false;
        foreach (var cv in arena.GetComponentsInChildren<Canvas>(true)) cv.enabled = false;
        if (arena.Hud) arena.Hud.enabled = false;
        foreach (var l in arena.GetComponentsInChildren<AudioListener>(true)) l.enabled = false;
        foreach (var a in arena.GetComponentsInChildren<AudioSource>(true)) a.mute = true;
        arena.StartPractice();
        feed = new RenderTexture(640, 400, 16);
        feedCam = new GameObject("Arena feed camera").AddComponent<Camera>();
        feedCam.targetTexture = feed;
        feedCam.fieldOfView = 55f;
        feedCam.farClipPlane = 400f;
        GraphicsTuning.Setup(feedCam);
        var mat = new Material(Shader.Find("Unlit/Texture")) { mainTexture = feed };
        monitorScreen.sharedMaterial = mat;
        monitorScreen.enabled = true;
    }

    void FeedCamera()
    {
        var arena = VRArena.Instance;
        if (!arena) return;
        // circle the middle of the map, looking at the fight over B
        Vector3 centre = arena.Flags.Count > 0 ? arena.Flags[arena.Flags.Count / 2].transform.position : arena.transform.position;
        feedAngle += Time.deltaTime * 6f;
        Vector3 offset = Quaternion.Euler(0f, feedAngle, 0f) * new Vector3(0f, 14f, -26f);
        feedCam.transform.position = centre + offset;
        feedCam.transform.LookAt(centre + Vector3.up * 1.5f);
    }

    void BuildPhone()
    {
        phone = UIFactory.Rect("Phone", root);
        UIFactory.Place(phone, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(420f, 40f), new Vector2(380f, 96f));
        var bg = UIFactory.Panel("Bg", phone, new Color32(28, 28, 34, 235));
        UIFactory.Fill(bg.rectTransform);
        var who = UIFactory.Text("From", phone, 22, Color.white, TextAlignmentOptions.TopLeft, font);
        who.name = "From";
        UIFactory.Fill(who.rectTransform, 18, 18, 10, 52);
        var msg = UIFactory.Text("Message", phone, 20, new Color(1f, 1f, 1f, 0.9f), TextAlignmentOptions.TopLeft, font);
        msg.name = "Message";
        UIFactory.Fill(msg.rectTransform, 18, 18, 40, 8);
    }

    static readonly (string from, string text)[] Messages =
    {
        ("JAY", "ranked at 4?? dont be late again"),
        ("MOM", "Home at 6. House clean, homework DONE. Love you"),
        ("JAY", "bro the enemy team is STACKED today"),
        ("MOM", "And feed the cat!!"),
        ("JAY", "u on?"),
    };

    IEnumerator Texts()
    {
        yield return new WaitForSeconds(4f);
        int i = 0;
        while (true)
        {
            var (from, text) = Messages[i++ % Messages.Length];
            phone.Find("From").GetComponent<TextMeshProUGUI>().text = "<b>" + from + "</b>  <size=70%><color=#FFFFFF88>now</color></size>";
            phone.Find("Message").GetComponent<TextMeshProUGUI>().text = text;
            yield return Slide(phone, new Vector2(-30f, 40f));
            yield return new WaitForSeconds(5f);
            yield return Slide(phone, new Vector2(420f, 40f));
            yield return new WaitForSeconds(Random.Range(8f, 13f));
        }
    }

    IEnumerator Birds()
    {
        if (!windowCentre) yield break;
        var mat = new Material(Shader.Find("Sprites/Default")) { color = new Color(0.12f, 0.1f, 0.12f) };
        while (true)
        {
            yield return new WaitForSeconds(Random.Range(5f, 11f));
            int flock = Random.Range(1, 4);
            Vector3 outward = Vector3.ProjectOnPlane(windowCentre.forward, Vector3.up).normalized;
            Vector3 across = Vector3.Cross(Vector3.up, outward);
            Vector3 start = windowCentre.position + outward * Random.Range(9f, 16f) - across * 14f + Vector3.up * Random.Range(-1f, 3f);
            for (int b = 0; b < flock; b++) StartCoroutine(Bird(mat, start + new Vector3(Random.Range(-1f, 1f), Random.Range(-0.6f, 0.6f), Random.Range(-1f, 1f)), across));
        }
    }

    IEnumerator Bird(Material mat, Vector3 start, Vector3 across)
    {
        var bird = new GameObject("Bird").transform;
        bird.position = start;
        var wings = new Transform[2];
        for (int w = 0; w < 2; w++)
        {
            var q = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(q.GetComponent<Collider>());
            q.transform.SetParent(bird, false);
            q.transform.localScale = new Vector3(0.28f, 0.02f, 0.08f);
            q.GetComponent<Renderer>().sharedMaterial = mat;
            wings[w] = q.transform;
        }
        float speed = Random.Range(3.5f, 5.5f), flap = Random.Range(9f, 13f);
        for (float t = 0f; t < 8f; t += Time.deltaTime)
        {
            bird.position += across * speed * Time.deltaTime + Vector3.up * Mathf.Sin(t * 1.7f) * 0.004f;
            bird.rotation = Quaternion.LookRotation(across);
            float a = Mathf.Sin(t * flap) * 35f;
            wings[0].localPosition = new Vector3(-0.13f, 0f, 0f); wings[0].localRotation = Quaternion.Euler(0f, 0f, a);
            wings[1].localPosition = new Vector3(0.13f, 0f, 0f); wings[1].localRotation = Quaternion.Euler(0f, 0f, -a);
            yield return null;
        }
        Destroy(bird.gameObject);
    }
}

/// <summary>A menu note that grows a little under the mouse and gives when clicked.</summary>
public class NoteHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
{
    public RectTransform target;
    float want = 1f;
    public void OnPointerEnter(PointerEventData e) => want = 1.07f;
    public void OnPointerExit(PointerEventData e) => want = 1f;
    public void OnPointerDown(PointerEventData e) => want = 0.95f;
    public void OnPointerUp(PointerEventData e) => want = 1.07f;
    void Update()
    {
        if (!target) return;
        float s = Mathf.Lerp(target.localScale.x, want, Time.unscaledDeltaTime * 14f);
        target.localScale = new Vector3(s, s, 1f);
    }
}

/// <summary>
/// Kitty on the menu, with a mind of her own. She pads round the room (on paths worked out from your real furniture) and picks
/// what to do: jump up on the desk or the bed for a proper nap (curled up, an ear twitching), sit on the window sill chattering at
/// the birds, wash, sniff about, stretch and yawn, or tear round with the zoomies. Click her: she stops, looks at you and purrs
/// (or hisses - she is a cat). Click the floor: a red laser dot - she drops low, wiggles... and pounces.
/// </summary>
public class MenuCat : MonoBehaviour
{
    Vector3[] spots = new Vector3[0];
    List<int>[] next = new List<int>[0];
    Vector3[] perchTops = new Vector3[0];
    int[] perchLaunch = new int[0];
    string[] perchKinds = new string[0];
    Camera view;
    Transform body, head;
    Vector3 bodyRest;
    Animator anim;
    int at = -1;
    float loaf, groom, sniff, stretch, crouch, wiggle, earTwitch, hop;
    float attentionUntil, lookUp;
    bool airborne, lyingPause, petLying;
    CatPose pose;
    bool running;
    enum Doing { Walk, Sit, Groom, Nap, Window, Stretch, Attention, Laser, Jump }
    Doing doing = Doing.Sit;
    Transform laserDot;
    Vector3 laserAt;
    bool laserOn;
    static readonly int Vert = Animator.StringToHash("Vert"), State = Animator.StringToHash("State");
    float Size => Mathf.Max(0.5f, transform.lossyScale.y);
    int upAxis = 1, fwdAxis = 2, sideAxis = 0;

    static int Dominant(Vector3 v)
    {
        v = new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));
        return v.x >= v.y && v.x >= v.z ? 0 : v.y >= v.z ? 1 : 2;
    }

    /// <summary>A body shape in the cat's terms (width, height, length), on the bone's own axes.</summary>
    Vector3 Shape(float width, float height, float length)
    {
        var v = Vector3.one;
        v[sideAxis] = width; v[upAxis] = height; v[fwdAxis] = length;
        return v;
    }

    public void Routes(Vector3[] spotList, int[] links, Camera cam)
    {
        view = cam;
        spots = spotList ?? new Vector3[0];
        next = new List<int>[spots.Length];
        for (int i = 0; i < spots.Length; i++) next[i] = new List<int>();
        var seen = new bool[spots.Length];
        for (int i = 0; i < spots.Length; i++) seen[i] = InSight(spots[i]);
        if (links != null)
            for (int i = 0; i + 1 < links.Length; i += 2)
            {
                int a = links[i], b = links[i + 1];
                if (!seen[a] || !seen[b]) continue;
                next[a].Add(b); next[b].Add(a);
            }
        // keep the biggest joined-up patch of floor she's seen on (no stranded islands)
        var group = new int[spots.Length];
        for (int i = 0; i < group.Length; i++) group[i] = -1;
        int bestGroup = -1, bestSize = 0, g = 0;
        for (int i = 0; i < spots.Length; i++)
        {
            if (group[i] >= 0 || next[i].Count == 0) continue;
            int size = 0; var q = new Queue<int>(); q.Enqueue(i); group[i] = g;
            while (q.Count > 0) { int c = q.Dequeue(); size++; foreach (int n in next[c]) if (group[n] < 0) { group[n] = g; q.Enqueue(n); } }
            if (size > bestSize) { bestSize = size; bestGroup = g; }
            g++;
        }
        for (int i = 0; i < spots.Length; i++) if (group[i] != bestGroup) next[i].Clear();
        at = Nearest(transform.position);
        if (at >= 0) transform.position = spots[at];
    }

    public void Perches(Vector3[] tops, int[] launch, string[] kinds)
    {
        var t = new List<Vector3>(); var l = new List<int>(); var k = new List<string>();
        for (int i = 0; tops != null && i < tops.Length; i++)
        {
            if (launch == null || i >= launch.Length || launch[i] < 0 || launch[i] >= next.Length || next[launch[i]].Count == 0) continue;
            if (!InSight(tops[i])) continue; // (a nap you can't see is no use on a menu)
            t.Add(tops[i]); l.Add(launch[i]); k.Add(kinds != null && i < kinds.Length ? kinds[i] : "furniture");
        }
        perchTops = t.ToArray(); perchLaunch = l.ToArray(); perchKinds = k.ToArray();
    }

    /// <summary>Can the menu camera see a cat here? (to the right of the corkboard, on screen, nothing in the way)</summary>
    bool InSight(Vector3 p)
    {
        if (!view) return true;
        Vector3 v = view.WorldToViewportPoint(p + Vector3.up * 0.15f);
        if (v.z <= 0f || v.x < 0.3f || v.x > 0.96f || v.y < 0.04f || v.y > 0.92f) return false;
        Vector3 eye = view.transform.position, to = p + Vector3.up * 0.2f;
        if (Physics.Linecast(eye, to, out var hit, ~0, QueryTriggerInteraction.Ignore) && (hit.point - to).magnitude > 0.3f && !hit.collider.transform.IsChildOf(transform)) return false;
        return true;
    }

    Vector3? windowAt;
    /// <summary>The window she watches the world from (from the floor below it when there's no sill).</summary>
    public void Window(Vector3 at) => windowAt = at;

    int Nearest(Vector3 p)
    {
        int best = -1; float bd = float.MaxValue;
        for (int i = 0; i < spots.Length; i++) { float d = (spots[i] - p).sqrMagnitude; if (d < bd && next[i].Count > 0) { bd = d; best = i; } }
        return best;
    }

    void Start()
    {
        foreach (var t in GetComponentsInChildren<Transform>(true))
        {
            if (t.name == "Root") body = t;
            if (t.name == "spine.008" || (!head && t.name == "spine.007")) head = t;
        }
        if (body)
        {
            bodyRest = body.localScale;
            // (an imported rig: the body bone's own axes aren't the cat's - find which one points up, which forward)
            upAxis = Dominant(body.InverseTransformDirection(transform.up));
            fwdAxis = Dominant(body.InverseTransformDirection(transform.forward));
            if (fwdAxis == upAxis) fwdAxis = (upAxis + 1) % 3;
            sideAxis = 3 - upAxis - fwdAxis;
        }
        anim = GetComponentInChildren<Animator>();
        foreach (var c in GetComponentsInChildren<Collider>()) Destroy(c);
        var box = gameObject.AddComponent<BoxCollider>();
        var rs = GetComponentsInChildren<Renderer>();
        if (rs.Length > 0)
        {
            var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
            box.center = transform.InverseTransformPoint(b.center);
            box.size = new Vector3(b.size.x / transform.lossyScale.x, b.size.y / transform.lossyScale.y, b.size.z / transform.lossyScale.z) * 1.3f;
        }
        // the laser dot
        var dot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Destroy(dot.GetComponent<Collider>());
        dot.name = "Laser dot";
        var m = new Material(Shader.Find("Sprites/Default")) { color = new Color(1f, 0.1f, 0.08f, 0.95f) };
        dot.GetComponent<Renderer>().sharedMaterial = m;
        dot.transform.localScale = Vector3.one * 0.05f;
        var glow = new GameObject("Glow").AddComponent<Light>();
        glow.transform.SetParent(dot.transform, false);
        glow.type = LightType.Point; glow.color = Color.red; glow.range = 0.5f; glow.intensity = 2f;
        dot.SetActive(false);
        laserDot = dot.transform;
        pose = gameObject.AddComponent<CatPose>();
        StartCoroutine(Life());
    }

    // ------------------------------------------------------------------ what she gets up to

    int lifeRun;

    IEnumerator Life()
    {
        int me = ++lifeRun; // (only ever one of her running: an older one bows out)
        yield return new WaitForSeconds(Random.Range(1f, 2.5f));
        while (me == lifeRun)
        {
            if (doing == Doing.Attention) { while (Time.time < attentionUntil) yield return null; doing = Doing.Sit; }
            if (laserOn) { yield return ChaseLaser(); continue; }
            if (at >= 0 && transform.position.y - spots[at].y > 0.2f * Size) { yield return new WaitForSeconds(Random.Range(1f, 3f)); yield return JumpTo(spots[at]); continue; } // (still up on something: hop down)
            float r = Random.value;
            int desk = Perch("desk"), bed = Perch("bed"), sill = WindowPerch();
            if (r < 0.28f && (desk >= 0 || bed >= 0)) yield return NapUp(desk >= 0 && (bed < 0 || Random.value < 0.65f) ? desk : bed);
            else if (r < 0.45f && sill >= 0) yield return WatchWindow(sill);
            else if (r < 0.55f) yield return Zoomies();
            else
            {
                yield return Wander(Random.Range(2, 5), Random.value < 0.4f);
                if (doing == Doing.Attention || laserOn) continue;
                yield return Random.value < 0.5f ? Pause(Doing.Groom, Random.Range(4f, 7f)) : Pause(Doing.Sit, Random.Range(3f, 6f));
            }
        }
    }

    int Perch(string kind)
    {
        var options = new List<int>();
        for (int i = 0; i < perchKinds.Length; i++) if (perchKinds[i] == kind && PerchTop(i, out _)) options.Add(i);
        return options.Count == 0 ? -1 : options[Random.Range(0, options.Count)];
    }

    /// <summary>The real top of a perch right now (the desk you have, not the one you might buy).</summary>
    bool PerchTop(int i, out Vector3 top)
    {
        top = perchTops[i];
        float best = float.MaxValue; bool found = false;
        foreach (var hit in Physics.RaycastAll(top + Vector3.up * 0.6f, Vector3.down, 1f, ~0, QueryTriggerInteraction.Ignore))
        {
            if (hit.collider.transform.IsChildOf(transform)) continue; // (her, sitting on it)
            if (hit.distance < best) { best = hit.distance; top = hit.point; found = true; }
        }
        return found;
    }

    IEnumerator Pause(Doing what, float seconds)
    {
        doing = what;
        float until = Time.time + seconds;
        lyingPause = seconds > 3f && Random.value < 0.55f; // a longer rest: she lies down for it
        if (view && what == Doing.Sit) yield return Face(view.transform.position, 0.6f);
        while (Time.time < until && doing == what && !laserOn) yield return null;
    }

    IEnumerator Wander(int hops, bool sniffing)
    {
        for (int h = 0; h < hops && (doing == Doing.Walk || doing == Doing.Sit || doing == Doing.Groom) && !laserOn; h++)
        {
            if (at < 0 || next[at].Count == 0) yield break;
            yield return WalkTo(next[at][Random.Range(0, next[at].Count)], sniffing ? 0.55f : 0.8f);
        }
    }

    IEnumerator Zoomies()
    {
        running = true;
        FloatingWord.Spawn("!", transform.position + Vector3.up * 0.5f * Size, HudArt.MarkerRed, null, 1.3f, 1f);
        for (int h = 0; h < 6 && !laserOn && doing != Doing.Attention; h++)
        {
            if (at < 0 || next[at].Count == 0) break;
            yield return WalkTo(next[at][Random.Range(0, next[at].Count)], 2.6f);
        }
        running = false;
        yield return Pause(Doing.Sit, 2f);
    }

    /// <summary>Up onto the desk / the bed, round once, curl up, a good long nap; then a stretch and a yawn, and down.</summary>
    IEnumerator NapUp(int perch)
    {
        if (!PerchTop(perch, out var top)) yield break;
        yield return Route(perchLaunch[perch]);
        if (doing == Doing.Attention || laserOn) yield break;
        yield return JumpTo(top);
        yield return Face(transform.position + Quaternion.Euler(0f, Random.Range(140f, 220f), 0f) * transform.forward, 0.8f); // round once
        doing = Doing.Nap;
        float until = Time.time + Random.Range(14f, 22f);
        while (Time.time < until && doing == Doing.Nap)
        {
            if (Random.value < Time.deltaTime * 0.25f) earTwitch = 1f;
            if (Random.value < Time.deltaTime * 0.12f) FloatingWord.Spawn("z", (head ? head.position : transform.position) + Vector3.up * 0.2f, new Color(0.45f, 0.55f, 0.95f), null, 1f, 1.4f);
            yield return null;
        }
        if (doing == Doing.Nap) yield return StretchYawn();
        if (doing == Doing.Attention && Time.time < attentionUntil) yield break; // (petted on the desk: she stays there a bit)
        yield return JumpTo(spots[perchLaunch[perch]]);
        at = perchLaunch[perch];
    }

    /// <summary>The perch to watch the world from: a sill, or the desk / bed / whatever stands right by a window. -1: none.</summary>
    int WindowPerch()
    {
        int sill = Perch("sill");
        if (sill >= 0 || !windowAt.HasValue) return sill;
        int best = -1; float bd = 2.4f * Size;
        for (int i = 0; i < perchTops.Length; i++)
        {
            Vector3 d = perchTops[i] - windowAt.Value; d.y = 0f;
            if (d.magnitude < bd && PerchTop(i, out _)) { bd = d.magnitude; best = i; }
        }
        return best;
    }

    /// <summary>Up by the window: sit and watch the world go by, the tail end twitching, chattering when a bird flies past.</summary>
    IEnumerator WatchWindow(int perch)
    {
        if (perch < 0 || !PerchTop(perch, out var top)) yield break;
        yield return Route(perchLaunch[perch]);
        if (doing == Doing.Attention || laserOn) yield break;
        yield return JumpTo(top);
        // face out of the window
        Vector3 roomMid = Vector3.zero; foreach (var p in spots) roomMid += p; roomMid /= Mathf.Max(1, spots.Length);
        yield return Face(windowAt.HasValue ? windowAt.Value : transform.position + (transform.position - roomMid), 0.6f);
        Vector3 w = windowAt.HasValue ? windowAt.Value : transform.position;
        lookUp = Mathf.Clamp01((w.y - transform.position.y) / (0.8f * Size)) * 0.6f;
        doing = Doing.Window;
        float until = Time.time + Random.Range(10f, 16f), nextChatter = Time.time + Random.Range(2f, 4f);
        while (Time.time < until && doing == Doing.Window && !laserOn)
        {
            if (Time.time > nextChatter) { nextChatter = Time.time + Random.Range(3f, 6f); FloatingWord.Spawn("ek ek ek", (head ? head.position : transform.position) + Vector3.up * 0.2f, HudArt.Ink, null, 1f, 1.2f); wiggle = 1f; }
            yield return null;
        }
        lookUp = 0f;
        if (doing == Doing.Attention && Time.time < attentionUntil) yield break;
        yield return JumpTo(spots[perchLaunch[perch]]);
        at = perchLaunch[perch];
    }

    IEnumerator StretchYawn()
    {
        doing = Doing.Stretch;
        for (float t = 0f; t < 1.6f; t += Time.deltaTime) { stretch = Mathf.Sin(t / 1.6f * Mathf.PI); yield return null; }
        stretch = 0f;
        FloatingWord.Spawn("*yawn*", (head ? head.position : transform.position) + Vector3.up * 0.2f, HudArt.Ink, null, 1f, 1.2f);
        yield return new WaitForSeconds(0.6f);
        doing = Doing.Sit;
    }

    IEnumerator ChaseLaser()
    {
        doing = Doing.Laser;
        Vector3 target = laserAt;
        // to the floor spot nearest the dot, then a stalk and a pounce
        int goal = Nearest(target);
        if (goal >= 0 && Vector3.Distance(transform.position, target) > 1.1f * Size) { running = true; yield return Route(goal, 2.2f); running = false; }
        if (!laserOn) { doing = Doing.Sit; yield break; }
        yield return Face(laserAt, 0.25f);
        crouch = 1f;
        for (float t = 0f; t < 1.1f && laserOn; t += Time.deltaTime) { wiggle = 1f; yield return null; }
        crouch = 0f;
        if (!laserOn) { doing = Doing.Sit; yield break; }
        // POUNCE
        Vector3 from = transform.position, to = laserAt;
        float arc = 0.22f * Size + Mathf.Max(0f, to.y - from.y);
        airborne = true;
        for (float t = 0f; t < 1f; t += Time.deltaTime / 0.34f)
        {
            Vector3 p = Vector3.Lerp(from, to, t);
            p.y += Mathf.Sin(t * Mathf.PI) * arc;
            transform.position = p;
            yield return null;
        }
        transform.position = to;
        airborne = false;
        at = Nearest(transform.position);
        FloatingWord.Spawn("!", transform.position + Vector3.up * 0.45f * Size, HudArt.MarkerRed, null, 1.2f, 0.8f);
        // the dot jumps away now and then; otherwise she sits there, proud
        laserOn = false;
        laserDot.gameObject.SetActive(false);
        yield return Pause(Doing.Sit, 2.5f);
    }

    // ------------------------------------------------------------------ moving

    /// <summary>Walks the open floor to a spot (the shortest way along the paths).</summary>
    IEnumerator Route(int goal, float speedMul = 0.8f)
    {
        if (at < 0 || goal < 0) yield break;
        // up on something? down first
        if (transform.position.y - spots[at].y > 0.2f * Size)
        {
            if (goal == at) yield break;
            yield return JumpTo(spots[at]);
        }
        var prev = new int[spots.Length];
        for (int i = 0; i < prev.Length; i++) prev[i] = -2;
        var q = new Queue<int>(); q.Enqueue(at); prev[at] = -1;
        while (q.Count > 0) { int c = q.Dequeue(); if (c == goal) break; foreach (int n in next[c]) if (prev[n] == -2) { prev[n] = c; q.Enqueue(n); } }
        if (prev[goal] == -2) yield break;
        var path = new List<int>();
        for (int c = goal; c != at && c >= 0; c = prev[c]) path.Add(c);
        path.Reverse();
        foreach (int step in path)
        {
            if (doing == Doing.Attention) yield break;
            yield return WalkTo(step, speedMul);
        }
    }

    IEnumerator WalkTo(int to, float speedMul)
    {
        var keep = doing;
        if (doing != Doing.Laser) doing = Doing.Walk;
        while (pose && pose.Weight > 0.05f) yield return null; // (up on her feet first)
        Vector3 target = spots[to];
        Vector3 dir = target - transform.position; dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f) { at = to; yield break; }
        yield return Face(target, speedMul > 1.5f ? 0.12f : 0.3f);
        float speed = speedMul * Size;
        while (doing == Doing.Walk || doing == Doing.Laser)
        {
            Vector3 p = transform.position;
            Vector3 left = target - p; left.y = 0f;
            if (left.magnitude < 0.05f) break;
            transform.position = Vector3.MoveTowards(p, new Vector3(target.x, p.y, target.z), speed * Time.deltaTime);
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(left.normalized), Time.deltaTime * 8f);
            yield return null;
        }
        if (doing == Doing.Walk || doing == Doing.Laser) at = to;
        else at = Nearest(transform.position);
        if (doing == Doing.Walk) doing = keep == Doing.Walk ? Doing.Walk : Doing.Sit;
    }

    /// <summary>A jump up or down: crouch, an arc, a soft landing.</summary>
    IEnumerator JumpTo(Vector3 to)
    {
        var keep = doing;
        doing = Doing.Jump;
        while (pose && pose.Weight > 0.05f) yield return null; // (up on her feet first)
        yield return Face(to, 0.25f);
        crouch = 1f;
        yield return new WaitForSeconds(0.35f);
        crouch = 0f;
        Vector3 from = transform.position;
        float height = Mathf.Max(0.25f, to.y - from.y + 0.25f);
        airborne = true;
        for (float t = 0f; t < 1f; t += Time.deltaTime / 0.42f)
        {
            Vector3 p = Vector3.Lerp(from, to, t);
            p.y += Mathf.Sin(t * Mathf.PI) * height * 0.6f;
            transform.position = p;
            yield return null;
        }
        transform.position = to;
        airborne = false;
        crouch = 0.6f; // (landing)
        yield return new WaitForSeconds(0.15f);
        crouch = 0f;
        doing = keep == Doing.Jump ? Doing.Sit : keep;
    }

    IEnumerator Face(Vector3 point, float time)
    {
        Vector3 d = point - transform.position; d.y = 0f;
        if (d.sqrMagnitude < 0.0001f) yield break;
        Quaternion from = transform.rotation, to = Quaternion.LookRotation(d.normalized);
        for (float t = 0f; t < 1f; t += Time.deltaTime / Mathf.Max(0.01f, time)) { transform.rotation = Quaternion.Slerp(from, to, Mathf.SmoothStep(0f, 1f, t)); yield return null; }
    }

    // ------------------------------------------------------------------ every frame

    void Update()
    {
        if (Input.GetMouseButtonDown(0) && Camera.main && !(UnityEngine.EventSystems.EventSystem.current && UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject()))
        {
            var ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out var hit, 40f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (hit.collider.transform.IsChildOf(transform)) Pet();
                else if (hit.normal.y > 0.6f && doing != Doing.Nap) Laser(hit.point);
            }
        }
        bool moving = doing == Doing.Walk || (doing == Doing.Laser && running) || running;
        if (anim)
        {
            anim.SetFloat(Vert, moving || doing == Doing.Jump ? 1f : 0f, 0.12f, Time.deltaTime);
            anim.SetFloat(State, running ? 1f : 0f, 0.12f, Time.deltaTime);
            anim.speed = Mathf.MoveTowards(anim.speed, doing == Doing.Nap ? 0.25f : 1f, Time.deltaTime * 2f);
        }
        if (pose)
        {
            // lying down properly (CatPose): napping, watching out of the window, a long rest, a fuss while she's lying there
            bool lying = doing == Doing.Nap || doing == Doing.Window || ((doing == Doing.Sit || doing == Doing.Groom) && lyingPause) || (doing == Doing.Attention && petLying);
            pose.lie = lying ? 1f : 0f;
            pose.sleep = doing == Doing.Nap ? 1f : 0f;
        }
        const float loafWant = 0f; // (no more squashing her flat - she really lies down now)
        loaf = Mathf.MoveTowards(loaf, loafWant, Time.deltaTime * 1.5f);
        groom = Mathf.MoveTowards(groom, doing == Doing.Groom ? 1f : 0f, Time.deltaTime * 2f);
        sniff = Mathf.MoveTowards(sniff, doing == Doing.Walk && !running ? 1f : 0f, Time.deltaTime * 2f);
        earTwitch = Mathf.MoveTowards(earTwitch, 0f, Time.deltaTime * 6f);
        wiggle = Mathf.MoveTowards(wiggle, 0f, Time.deltaTime * 1.5f);
        Settle();
        if (laserOn && laserDot)
        {
            // a laser dot never sits still
            laserDot.position = laserAt + new Vector3(Mathf.Sin(Time.time * 13f), 0f, Mathf.Cos(Time.time * 11f)) * 0.025f + Vector3.up * 0.01f;
        }
    }

    void LateUpdate()
    {
        if (body)
        {
            float breathe = doing == Doing.Walk || running ? 0f : Mathf.Sin(Time.time * (doing == Doing.Nap ? 1.4f : 2.6f)) * 0.03f;
            var shape = Vector3.Lerp(Vector3.one, Shape(1.18f, 0.52f, 1.04f), loaf); // a loaf: paws tucked in
            shape = Vector3.Scale(shape, Vector3.Lerp(Vector3.one, Shape(1.06f, 0.75f, 1.08f), crouch));
            shape = Vector3.Scale(shape, Vector3.Lerp(Vector3.one, Shape(0.95f, 0.8f, 1.25f), stretch)); // a long stretch
            shape = Vector3.Scale(shape, Shape(1f + breathe * 0.5f, 1f + breathe, 1f));
            body.localScale = Vector3.Scale(bodyRest, shape);
            if (hop > 0f) body.position += Vector3.up * hop;
            if (wiggle > 0.01f)
            {
                // the bum wiggle (before a pounce, at the birds): the back end sways round the shoulders
                Vector3 pivot = head ? new Vector3(head.position.x, body.position.y, head.position.z) : body.position;
                var turn = Quaternion.AngleAxis(Mathf.Sin(Time.time * 14f) * 8f * wiggle, transform.up);
                body.position = pivot + turn * (body.position - pivot);
                body.rotation = turn * body.rotation;
            }
        }
        if (!head) return;
        if ((doing == Doing.Sit || doing == Doing.Attention) && Camera.main)
        {
            var ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            Vector3 target = ray.GetPoint(2.5f);
            var turn = Quaternion.FromToRotation(transform.forward, (target - head.position).normalized);
            float angle = Quaternion.Angle(Quaternion.identity, turn);
            if (angle > 60f) turn = Quaternion.Slerp(Quaternion.identity, turn, 60f / angle);
            head.rotation = Quaternion.Slerp(Quaternion.identity, turn, 0.7f) * head.rotation;
        }
        else if (doing == Doing.Laser && laserOn)
        {
            var turn = Quaternion.FromToRotation(transform.forward, (laserAt - head.position).normalized);
            head.rotation = Quaternion.Slerp(Quaternion.identity, turn, 0.8f) * head.rotation; // eyes on the dot
        }
        else if (doing == Doing.Window)
            head.rotation = Quaternion.AngleAxis(Mathf.Sin(Time.time * 0.9f) * 20f, transform.up) * Quaternion.AngleAxis(-12f - 25f * lookUp, transform.right) * head.rotation; // following the birds
        else if (groom > 0.01f)
            head.rotation = Quaternion.AngleAxis(groom * (30f + Mathf.Sin(Time.time * 9f) * 10f), transform.right) * Quaternion.AngleAxis(groom * 35f, transform.up) * head.rotation;
        else if (stretch > 0.01f)
            head.rotation = Quaternion.AngleAxis(-stretch * 30f, transform.right) * head.rotation; // nose up: the yawn
        else if (loaf > 0.6f)
            head.rotation = Quaternion.AngleAxis(22f * loaf + earTwitch * 6f * Mathf.Sin(Time.time * 40f), transform.right) * head.rotation; // tucked in, asleep
        else if (sniff > 0.01f)
            head.rotation = Quaternion.AngleAxis(sniff * (18f + Mathf.Sin(Time.time * 7f) * 6f), transform.right) * head.rotation; // nose to the floor
    }

    /// <summary>Paws on whatever's under her - the floor, the desk - never hanging in the air.</summary>
    void Settle()
    {
        if (airborne) return;
        Vector3 from = transform.position + Vector3.up * 0.4f * Size;
        float best = float.MaxValue, y = transform.position.y;
        foreach (var h in Physics.RaycastAll(from, Vector3.down, 1.6f * Size, ~0, QueryTriggerInteraction.Ignore))
        {
            if (h.collider.transform.IsChildOf(transform)) continue;
            if (h.distance < best) { best = h.distance; y = h.point.y; }
        }
        if (best == float.MaxValue) return;
        var p = transform.position;
        p.y = Mathf.MoveTowards(p.y, y, Time.deltaTime * 5f);
        transform.position = p;
    }

    void Laser(Vector3 at)
    {
        laserAt = at;
        laserOn = true;
        laserDot.gameObject.SetActive(true);
        laserDot.position = at + Vector3.up * 0.01f;
        if (doing != Doing.Laser && doing != Doing.Jump) { StopAllCoroutines(); crouch = 0f; hop = 0f; stretch = 0f; airborne = false; StartCoroutine(Life()); }
    }

    void Pet()
    {
        bool grumpy = doing == Doing.Nap ? Random.value < 0.4f : Random.value < 0.15f;
        var was = doing;
        petLying = pose && pose.Weight > 0.5f && !grumpy; // (lying there, she stays down and enjoys it)
        doing = Doing.Attention;
        attentionUntil = Time.time + (grumpy ? 2.5f : 7f);
        if (view && was != Doing.Nap) StartCoroutine(Face(view.transform.position, 0.4f));
        FloatingWord.Spawn(grumpy ? "hss!" : "purr", (head ? head.position : transform.position) + Vector3.up * 0.25f, grumpy ? HudArt.MarkerRed : new Color(1f, 0.6f, 0.7f), null, 1.4f, 1.4f);
    }
}
