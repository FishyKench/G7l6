using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Your gaming PC. Sit down (E) and you're at its desktop: apps open as windows - Homework (a document you type
/// into) and the Ranked Arena launcher - plus the usual desktop apps. Switch by clicking the taskbar or TAB.
/// Everything stays as you left it when you get up (ESC): windows, what's in front, whether the game runs.
///
/// To play, launch Ranked from its window; once it's running, put on the VR headset (on the desk) to play it.
/// The monitor on the desk shows whatever is in front on the PC.
/// </summary>
public class GamingPC : MonoBehaviour, IMainTask, IMainTaskHost, IPromptProvider
{
    public static GamingPC Instance { get; private set; }

    public enum GameState { Closed, Launching, Running }
    /// <summary>Is the VR game running on the PC?</summary>
    public GameState Game { get; private set; }
    public float LaunchProgress { get; private set; }
    public float launchSeconds = 3f;

    /// <summary>What's in front on the screen: "homework", "game", or null for the desktop.</summary>
    public string ScreenApp => desktop ? desktop.FocusedApp : null;
    public TypingTask Homework => homework;
    public MatchTask Match => game;
    public bool Seated { get; private set; }

    TypingTask homework;
    MatchTask game;
    PCDesktop desktop;
    TMP_FontAsset font;

    // The monitor on the desk
    Canvas screen;
    TextMeshProUGUI deskClock;
    Camera screenCamera;
    RenderTexture screenTexture;
    GameObject docView, gameView, offView;
    TextMeshProUGUI docPage, gameTitle, gameStatus;
    RectTransform docBar;
    float nextScreenUpdate;

    void Awake() => Instance = this;

    void Start()
    {
        homework = GetComponentInChildren<TypingTask>(true);
        if (!homework) homework = FindObjectOfType<TypingTask>();
        game = GetComponentInChildren<MatchTask>(true);
        if (!game) game = FindObjectOfType<MatchTask>();
        font = GameHUD.Instance ? GameHUD.Instance.font : null;
        desktop = gameObject.AddComponent<PCDesktop>();
        desktop.pc = this;
        BuildMonitorScreen();
        SetUpHeadset();
    }

    // ------------------------------------------------------------------ sitting down

    public string Prompt => Game == GameState.Running ? "Use your PC  (Ranked is running)" : "Use your PC";

    public bool ShouldBlockInput() => Seated; // E doesn't get you up - ESC does

    public void StartMainTask()
    {
        Seated = true;
        desktop.Show();
        FocusChanged();
    }

    public void StopMainTask()
    {
        Seated = false;
        if (homework && homework.Hosted) homework.StopMainTask();
        desktop.Hide();
        UpdateScreen();
    }

    public bool Hosts(IMainTask task) => false; // apps in windows don't stand you up
    public void Released(IMainTask task) { }

    /// <summary>The desktop changed which window is in front: type into homework only while it's in front.</summary>
    public void FocusChanged()
    {
        if (homework && homework.Hosted)
        {
            if (Seated && desktop.FocusedApp == "homework") homework.StartMainTask();
            else homework.StopMainTask();
        }
        UpdateScreen();
    }

    // ------------------------------------------------------------------ the game

    public void LaunchGame()
    {
        if (Game != GameState.Closed) return;
        Game = GameState.Launching;
        LaunchProgress = 0f;
    }

    public void QuitGame()
    {
        Game = GameState.Closed;
        LaunchProgress = 0f;
        UpdateScreen();
    }

    void Update()
    {
        if (PauseMenu.IsPaused) return;
        if (Game == GameState.Launching)
        {
            LaunchProgress += Time.deltaTime / Mathf.Max(0.1f, launchSeconds);
            if (LaunchProgress >= 1f)
            {
                LaunchProgress = 1f;
                Game = GameState.Running;
                if (!Seated) GameHUD.Toast("Ranked is running - put on your VR headset.", GameHUD.Info);
            }
        }
        if (Time.time >= nextScreenUpdate) UpdateScreen();
    }

    /// <summary>The headset on the desk becomes its own thing you put on (it shows what the PC is running).</summary>
    void SetUpHeadset()
    {
        Transform headset = null;
        foreach (var t in GetComponentsInChildren<Transform>(true)) if (t.name == "VR Headset") headset = t;
        if (!headset && transform.parent)
            foreach (var t in transform.parent.GetComponentsInChildren<Transform>(true)) if (t.name == "VR Headset") headset = t;
        if (!headset) return;
        if (headset.IsChildOf(transform)) headset.SetParent(transform.parent, true); // not part of the PC any more
        var go = headset.gameObject;
        if (!go.GetComponent<Collider>())
        {
            var b = Bounds(go);
            var box = go.AddComponent<BoxCollider>();
            box.center = headset.InverseTransformPoint(b.center);
            Vector3 s = headset.lossyScale;
            box.size = new Vector3(b.size.x / Mathf.Max(0.0001f, Mathf.Abs(s.x)), b.size.y / Mathf.Max(0.0001f, Mathf.Abs(s.y)), b.size.z / Mathf.Max(0.0001f, Mathf.Abs(s.z))) * 1.5f;
        }
        if (!go.GetComponent<Outline>())
        {
            var outline = go.AddComponent<Outline>();
            outline.OutlineMode = Outline.Mode.OutlineVisible;
            outline.OutlineColor = Color.white;
            outline.OutlineWidth = 5f;
            outline.enabled = false;
        }
        var vr = go.GetComponent<VRHeadset>();
        if (!vr) vr = go.AddComponent<VRHeadset>();
        vr.pc = this;
        vr.match = game;
    }

    // ------------------------------------------------------------------ the monitor on the desk

    /// <summary>Puts a little screen on the desk's monitor that shows the app you left open.</summary>
    void BuildMonitorScreen()
    {
        var monitor = FindMonitor();
        if (!monitor || !ScreenProjector.FindScreen(monitor, out var renderer, out int slot)) return;

        // The PC's screen is drawn by a little hidden camera into a texture, and that texture is put on the
        // monitor's own screen (projected across it, so it fills the curved screen edge to edge).
        ScreenProjector.Make(renderer, slot, null, monitor.transform.forward, false, out float aspect);
        int width = 1280, height = Mathf.Clamp(Mathf.RoundToInt(width / aspect), 240, 1280);
        screenTexture = new RenderTexture(width, height, 16) { name = "PC screen" };
        var camGo = new GameObject("PC Screen Camera");
        camGo.transform.SetParent(transform, false);
        camGo.transform.position = new Vector3(0f, -2000f, 0f);
        screenCamera = camGo.AddComponent<Camera>();
        screenCamera.targetTexture = screenTexture;
        screenCamera.clearFlags = CameraClearFlags.SolidColor;
        screenCamera.backgroundColor = Color.black;
        screenCamera.cullingMask = 1 << 5; // UI only
        screenCamera.orthographic = true;
        screenCamera.nearClipPlane = 0.1f;
        screenCamera.farClipPlane = 10f;
        var picture = ScreenProjector.Make(renderer, slot, screenTexture, monitor.transform.forward, false, out _);
        if (picture)
        {
            var mats = renderer.sharedMaterials;
            mats[slot] = picture;
            renderer.sharedMaterials = mats;
        }

        var go = new GameObject("PC Screen", typeof(RectTransform));
        go.layer = 5;
        go.transform.SetParent(camGo.transform, false);
        screen = go.AddComponent<Canvas>();
        screen.renderMode = RenderMode.ScreenSpaceCamera;
        screen.worldCamera = screenCamera;
        screen.planeDistance = 1f;
        var scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1000f, 1000f / aspect);
        scaler.matchWidthOrHeight = 0f;

        // The desktop: the same wallpaper as on the PC, little app icons and the taskbar.
        offView = Panel(go.transform, Color.black);
        var paper = UIFactory.Rect("Wallpaper", offView.transform).gameObject.AddComponent<RawImage>();
        paper.texture = Wallpaper.Texture;
        UIFactory.Fill(paper.rectTransform);
        Color32[] iconColours =
        {
            new Color32(245, 245, 250, 255), new Color32(90, 240, 220, 255), new Color32(200, 205, 220, 255), new Color32(190, 210, 230, 255),
            new Color32(60, 150, 240, 255), new Color32(255, 120, 60, 255), new Color32(80, 210, 120, 255), new Color32(150, 210, 255, 255),
            new Color32(250, 205, 80, 255), new Color32(150, 160, 180, 255),
        };
        for (int i = 0; i < iconColours.Length; i++)
        {
            var icon = UIFactory.Panel("Icon", offView.transform, iconColours[i]);
            UIFactory.Place(icon.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(80 + (i / 5) * 78, -40 - (i % 5) * 66), new Vector2(40, 40)); // kept away from the edges: curved screens bend them away
            var label = UIFactory.Panel("Label", offView.transform, new Color(1f, 1f, 1f, 0.55f));
            UIFactory.Place(label.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(82 + (i / 5) * 78, -86 - (i % 5) * 66), new Vector2(36, 5));
        }
        var taskbar = UIFactory.Panel("Taskbar", offView.transform, new Color32(14, 14, 24, 235));
        UIFactory.Place(taskbar.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), Vector2.zero, new Vector2(2000, 34));
        var start = UIFactory.Panel("Start", taskbar.transform, new Color32(90, 200, 255, 255));
        UIFactory.Place(start.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(70, 0), new Vector2(18, 18));
        var time = Label(taskbar.transform, "", 18, Color.white, TextAlignmentOptions.MidlineRight);
        UIFactory.Fill(time.rectTransform, 0, 70, 0, 0);
        deskClock = time;

        // Homework: a document.
        docView = Panel(go.transform, new Color32(232, 232, 236, 255));
        var page = Panel(docView.transform, Color.white, 120, 120, 40, 0);
        var title = Label(page.transform, "Homework.docx", 54, new Color32(40, 40, 60, 255), TextAlignmentOptions.TopLeft);
        UIFactory.Fill(title.rectTransform, 40, 40, 30, 0);
        for (int i = 0; i < 7; i++)
        {
            var line = UIFactory.Panel("Line", page.transform, new Color(0.6f, 0.6f, 0.66f, 0.55f));
            UIFactory.Place(line.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(40, -130 - i * 48), new Vector2(Random.Range(500f, 700f), 16));
        }
        docPage = Label(docView.transform, "", 40, new Color32(60, 60, 80, 255), TextAlignmentOptions.BottomRight);
        UIFactory.Fill(docPage.rectTransform, 0, 30, 0, 14);
        var barBg = UIFactory.Panel("Bar", docView.transform, new Color(0f, 0f, 0f, 0.1f));
        UIFactory.Place(barBg.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0, 8), new Vector2(940, 10));
        var fill = UIFactory.Panel("Fill", barBg.transform, new Color32(52, 120, 206, 255));
        docBar = fill.rectTransform;
        docBar.anchorMin = Vector2.zero;
        docBar.anchorMax = new Vector2(0f, 1f);
        docBar.offsetMin = docBar.offsetMax = Vector2.zero;

        // Game: the ranked lobby.
        gameView = Panel(go.transform, new Color32(16, 12, 36, 255));
        Panel(gameView.transform, new Color32(60, 20, 90, 120), 0, 0, 0, 0);
        gameTitle = Label(gameView.transform, "RANKED ARENA", 110, new Color32(90, 240, 220, 255));
        gameTitle.fontStyle = FontStyles.Bold;
        gameTitle.rectTransform.anchoredPosition = new Vector2(0, 60);
        gameStatus = Label(gameView.transform, "", 52, new Color32(255, 110, 200, 255));
        gameStatus.rectTransform.anchoredPosition = new Vector2(0, -90);
        UpdateScreen();
    }

    void UpdateScreen()
    {
        if (!screen) return;
        nextScreenUpdate = Time.time + 0.5f;
        string show = ScreenApp;
        if (deskClock && DayManager.Instance)
        {
            float hour = DayManager.Instance.Hour;
            int h = Mathf.FloorToInt(hour), m = Mathf.FloorToInt((hour - h) * 60f);
            deskClock.text = $"{(h > 12 ? h - 12 : h)}:{m:00} {(h >= 12 ? "PM" : "AM")}";
        }
        offView.SetActive(show != "homework" && show != "game");
        docView.SetActive(show == "homework");
        gameView.SetActive(show == "game");
        if (show == "homework" && homework)
        {
            int total = Mathf.Max(1, homework.pagesRequired);
            bool done = !homework.IsActive && homework.LastSucceeded;
            int typed = done ? total : Mathf.Clamp(Mathf.FloorToInt(homework.Progress * total) + 1, 1, total);
            docPage.text = done ? "Done!" : $"Page {typed} of {total}";
            docBar.anchorMax = new Vector2(done ? 1f : homework.Progress, 1f);
        }
        if (show == "game" && game)
        {
            gameStatus.text = Game == GameState.Closed ? "Not running - press PLAY" : Game == GameState.Launching ? "Launching..." : game.State switch
            {
                MatchTask.Phase.Waiting => (MatchTask.IsTournament ? "Tournament at " : "Match at ") + game.StartTimeText,
                MatchTask.Phase.Round => $"ROUND {game.RoundNumber} LIVE   {game.Wins} : {game.Losses}",
                MatchTask.Phase.Break => $"{game.Wins} : {game.Losses}   next round soon",
                _ => game.Wins >= game.roundsToWin ? $"VICTORY  {game.Wins} : {game.Losses}" : $"DEFEAT  {game.Wins} : {game.Losses}",
            };
            gameTitle.text = MatchTask.IsTournament ? "TOURNAMENT" : "RANKED ARENA";
            float pulse = 0.85f + Mathf.Sin(Time.time * 3f) * 0.15f;
            gameTitle.alpha = pulse;
        }
    }

    /// <summary>The main monitor on the desk: the one facing the chair straight on (side monitors are angled).</summary>
    GameObject FindMonitor()
    {
        var desk = Bounds(gameObject);
        var room = transform.parent;
        if (!room) return null;
        GameObject best = null;
        float bestScore = float.MinValue;
        foreach (Transform t in room)
        {
            if (!t.name.Contains("Monitor") || !t.gameObject.activeInHierarchy) continue;
            var b = Bounds(t.gameObject);
            var c = b.center;
            if (c.x < desk.min.x - 0.2f || c.x > desk.max.x + 0.2f || c.z < desk.min.z - 0.2f || c.z > desk.max.z + 0.2f) continue;
            float facing = Mathf.Abs(Vector3.Dot(Flat(t.forward), Flat(transform.forward)));
            float score = facing * 10f + b.size.y; // straight-on first, then the bigger one
            if (score > bestScore) { bestScore = score; best = t.gameObject; }
        }
        return best;
    }

    /// <summary>The screen part of a monitor model: the material that isn't the shared colour palette.</summary>
    static bool ScreenSurface(GameObject monitor, out MeshRenderer renderer, out Bounds bounds)
    {
        foreach (var r in monitor.GetComponentsInChildren<MeshRenderer>())
        {
            var mf = r.GetComponent<MeshFilter>();
            if (!mf || !mf.sharedMesh) continue;
            var mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length && i < mf.sharedMesh.subMeshCount; i++)
            {
                if (!mats[i] || mats[i].name.ToLowerInvariant().Contains("palette")) continue;
                var b = mf.sharedMesh.GetSubMesh(i).bounds;
                if (b.size.sqrMagnitude < 0.000001f) continue;
                renderer = r;
                bounds = b;
                return true;
            }
        }
        // No separate screen part: use the whole model's front.
        var any = monitor.GetComponentInChildren<MeshRenderer>();
        renderer = any;
        bounds = any && any.GetComponent<MeshFilter>() ? any.GetComponent<MeshFilter>().sharedMesh.bounds : default;
        return any;
    }

    GameObject Panel(Transform parent, Color c, float l = 0, float r = 0, float t = 0, float b = 0)
    {
        var img = UIFactory.Panel("Panel", parent, c);
        UIFactory.Fill(img.rectTransform, l, r, t, b);
        return img.gameObject;
    }

    TextMeshProUGUI Label(Transform parent, string text, float size, Color color, TextAlignmentOptions align = TextAlignmentOptions.Center)
    {
        var t = UIFactory.Text("Text", parent, size, color, align);
        t.text = text;
        t.enableWordWrapping = false;
        UIFactory.Fill(t.rectTransform);
        return t;
    }

    static Bounds Bounds(GameObject go)
    {
        var rs = go.GetComponentsInChildren<MeshRenderer>();
        if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.one * 0.3f);
        var b = rs[0].bounds;
        foreach (var r in rs) b.Encapsulate(r.bounds);
        return b;
    }

    static Vector3 Flat(Vector3 v) { v.y = 0f; return v.sqrMagnitude > 0.0001f ? v.normalized : Vector3.forward; }
    static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));
}
