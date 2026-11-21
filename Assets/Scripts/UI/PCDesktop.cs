using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The gaming PC's desktop, with a mouse cursor you move with your mouse. Apps open as windows:
///   - Homework: the homework document - type into it while its window is in front.
///   - Ranked Arena: the game's launcher - PLAY launches it (a few seconds), then put on the VR headset.
///   - the usual desktop apps (placeholders for now).
/// Double-click a desktop icon to open its app. Windows have minimise (_) and close (X); the taskbar shows
/// what's open - click it to switch, or press TAB. Everything stays as it is when you get up.
/// Made by the GamingPC.
/// </summary>
public class PCDesktop : MonoBehaviour
{
    public GamingPC pc;

    const float ScreenW = 1340f, ScreenH = 760f, TaskbarH = 52f;
    const float CursorSpeed = 13f, DoubleClick = 0.35f;
    const int Rows = 5;

    class Window
    {
        public string id, title;
        public RectTransform rt, content, taskButton;
        public Image taskBg;
        public TextMeshProUGUI taskLabel;
        public bool open, minimized;
    }

    class Icon
    {
        public string id, label, text;
        public RectTransform rt;
        public Image highlight;
    }

    GameObject root;
    RectTransform screen, windowLayer, cursor, startMenu, startButton, startGetUp, taskList;
    TextMeshProUGUI clock;
    readonly List<Icon> icons = new List<Icon>();
    readonly List<Window> windows = new List<Window>();
    Window focused, homeworkWin, gameWin, otherWin;
    int selected = -1, lastClickIcon = -1;
    float lastClickTime = -1f;
    Vector2 mouse = new Vector2(ScreenW * 0.5f, ScreenH * 0.5f);
    static Sprite round, arrow;

    // The game launcher's parts
    RectTransform playButton, quitButton, launchFill;
    Image playBg;
    TextMeshProUGUI playText, launcherStatus, launcherHint, otherBody;

    /// <summary>What's in front: "homework", "game", or null (the desktop or another app).</summary>
    public string FocusedApp => focused != null && focused.open && !focused.minimized && (focused == homeworkWin || focused == gameWin) ? focused.id : null;

    public void Show()
    {
        if (!root) Build();
        root.SetActive(true);
        startMenu.gameObject.SetActive(false);
    }

    public void Hide()
    {
        if (root) root.SetActive(false);
    }

    // ------------------------------------------------------------------ every frame

    void Update()
    {
        if (!root || !root.activeSelf || PauseMenu.IsPaused) return;

        mouse += new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y")) * CursorSpeed;
        mouse.x = Mathf.Clamp(mouse.x, 0f, ScreenW - 4f);
        mouse.y = Mathf.Clamp(mouse.y, 0f, ScreenH - 4f);
        cursor.anchoredPosition = mouse;
        cursor.SetAsLastSibling();
        clock.text = ClockText();
        UpdateLauncher();
        UpdateTaskbar();

        if (Input.GetKeyDown(KeyCode.Tab)) { CycleWindows(); return; }

        // Keyboard on the desktop itself (not while a window is in front - you might be typing).
        if (focused == null)
        {
            if (Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.Keypad1)) { OpenApp("homework"); return; }
            if (Input.GetKeyDown(KeyCode.Alpha2) || Input.GetKeyDown(KeyCode.Keypad2)) { OpenApp("game"); return; }
            if (Input.GetKeyDown(KeyCode.DownArrow)) Select(Mathf.Clamp(selected + 1, 0, icons.Count - 1));
            if (Input.GetKeyDown(KeyCode.UpArrow)) Select(Mathf.Clamp(selected - 1, 0, icons.Count - 1));
            if (Input.GetKeyDown(KeyCode.RightArrow)) Select(Mathf.Clamp(selected + Rows, 0, icons.Count - 1));
            if (Input.GetKeyDown(KeyCode.LeftArrow)) Select(Mathf.Clamp(selected - Rows, 0, icons.Count - 1));
            if ((Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) && selected >= 0) { OpenApp(icons[selected].id); return; }
        }

        if (Input.GetMouseButtonDown(0)) Click();
    }

    void Click()
    {
        // Start menu
        if (Over(startButton)) { startMenu.gameObject.SetActive(!startMenu.gameObject.activeSelf); return; }
        if (startMenu.gameObject.activeSelf)
        {
            if (Over(startGetUp)) { if (PlayerInteraction.Instance) PlayerInteraction.Instance.StopCurrentTaskFromUI(); return; }
            if (Over(startMenu)) return;
            startMenu.gameObject.SetActive(false);
        }

        // Taskbar buttons: switch to that window (or minimise it if it's already in front).
        foreach (var w in windows)
        {
            if (!w.open || !Over(w.taskButton)) continue;
            if (focused == w && !w.minimized) Minimise(w);
            else Focus(w);
            return;
        }

        // Windows, front to back.
        for (int i = windowLayer.childCount - 1; i >= 0; i--)
        {
            var rt = windowLayer.GetChild(i) as RectTransform;
            var w = windows.Find(x => x.rt == rt);
            if (w == null || !w.open || w.minimized || !Over(w.rt)) continue;
            if (Over(w.rt.Find("Title bar/Close") as RectTransform)) { Close(w); return; }
            if (Over(w.rt.Find("Title bar/Minimise") as RectTransform)) { Minimise(w); return; }
            if (focused != w) Focus(w);
            if (w == gameWin) LauncherClick();
            return;
        }

        // Desktop icons: click selects, double-click opens.
        for (int i = 0; i < icons.Count; i++)
        {
            if (!Over(icons[i].rt)) continue;
            bool doubleClick = lastClickIcon == i && Time.unscaledTime - lastClickTime < DoubleClick;
            lastClickIcon = i;
            lastClickTime = Time.unscaledTime;
            Select(i);
            if (doubleClick) OpenApp(icons[i].id);
            return;
        }
        Select(-1);
        if (focused != null) { focused = null; pc.FocusChanged(); } // clicked the wallpaper: the desktop is in front
    }

    bool Over(RectTransform rt)
    {
        if (!rt || !rt.gameObject.activeInHierarchy) return false;
        Vector3 world = screen.TransformPoint(new Vector3(mouse.x - ScreenW * 0.5f, mouse.y - ScreenH * 0.5f, 0f));
        return rt.rect.Contains(rt.InverseTransformPoint(world));
    }

    void Select(int i)
    {
        selected = i;
        for (int k = 0; k < icons.Count; k++) icons[k].highlight.enabled = k == i;
    }

    // ------------------------------------------------------------------ windows

    void OpenApp(string id)
    {
        if (id == "homework")
        {
            if (!homeworkWin.open && pc.Homework) pc.Homework.ShowIn(homeworkWin.content);
            Focus(Opened(homeworkWin));
            return;
        }
        if (id == "game") { Focus(Opened(gameWin)); return; }
        var icon = icons.Find(i => i.id == id);
        if (icon == null) return;
        SetTitle(otherWin, icon.label);
        otherBody.text = icon.text;
        Focus(Opened(otherWin));
    }

    static Window Opened(Window w)
    {
        w.open = true;
        return w;
    }

    void Focus(Window w)
    {
        w.minimized = false;
        w.rt.gameObject.SetActive(true);
        w.rt.SetAsLastSibling();
        focused = w;
        pc.FocusChanged();
    }

    void Minimise(Window w)
    {
        w.minimized = true;
        w.rt.gameObject.SetActive(false);
        if (focused == w) focused = TopWindow();
        pc.FocusChanged();
    }

    void Close(Window w)
    {
        if (w == gameWin && pc.Game != GamingPC.GameState.Closed) pc.QuitGame(); // closing the launcher quits the game
        w.open = false;
        w.minimized = false;
        w.rt.gameObject.SetActive(false);
        if (focused == w) focused = TopWindow();
        pc.FocusChanged();
    }

    Window TopWindow()
    {
        for (int i = windowLayer.childCount - 1; i >= 0; i--)
        {
            var child = windowLayer.GetChild(i);
            var w = windows.Find(x => x.rt == child);
            if (w != null && w.open && !w.minimized) return w;
        }
        return null;
    }

    void CycleWindows()
    {
        var open = windows.FindAll(w => w.open);
        if (open.Count == 0) return;
        int i = focused == null ? -1 : open.IndexOf(focused);
        Focus(open[(i + 1) % open.Count]);
    }

    void SetTitle(Window w, string title)
    {
        w.title = title;
        var t = w.rt.Find("Title bar/Title")?.GetComponent<TextMeshProUGUI>();
        if (t) t.text = title;
        if (w.taskLabel) w.taskLabel.text = title;
    }

    Window MakeWindow(string id, string title, Vector2 size, Color background)
    {
        var w = new Window { id = id, title = title };
        w.rt = UIFactory.Rect(title, windowLayer);
        UIFactory.Place(w.rt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, TaskbarH * 0.5f), size);
        var shadow = UIFactory.Panel("Shadow", w.rt, new Color(0f, 0f, 0f, 0.4f));
        UIFactory.Fill(shadow.rectTransform, -8, -8, -4, -14);
        var bg = UIFactory.Panel("Background", w.rt, background);
        UIFactory.Fill(bg.rectTransform);
        var bar = UIFactory.Panel("Title bar", w.rt, new Color32(36, 36, 58, 255));
        UIFactory.Place(bar.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(size.x, 36));
        var t = UIFactory.Text("Title", bar.transform, 17, Color.white, TextAlignmentOptions.MidlineLeft);
        t.text = title;
        UIFactory.Fill(t.rectTransform, 14, 110, 0, 0);
        var min = UIFactory.Panel("Minimise", bar.transform, new Color(1f, 1f, 1f, 0.06f));
        UIFactory.Place(min.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-46, 0), new Vector2(46, 36));
        var line = UIFactory.Panel("_", min.transform, Color.white);
        UIFactory.Place(line.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -4), new Vector2(14, 2));
        var close = UIFactory.Panel("Close", bar.transform, new Color32(210, 60, 70, 255));
        UIFactory.Place(close.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), Vector2.zero, new Vector2(46, 36));
        var x = UIFactory.Text("X", close.transform, 18, Color.white, TextAlignmentOptions.Center);
        x.text = "X";
        UIFactory.Fill(x.rectTransform);
        w.content = UIFactory.Rect("Content", w.rt);
        UIFactory.Fill(w.content, 0, 0, 36, 0);
        w.rt.gameObject.SetActive(false);
        windows.Add(w);
        return w;
    }

    // ------------------------------------------------------------------ taskbar

    void UpdateTaskbar()
    {
        float x = 70f;
        foreach (var w in windows)
        {
            if (!w.taskButton) continue;
            w.taskButton.gameObject.SetActive(w.open);
            if (!w.open) continue;
            w.taskButton.anchoredPosition = new Vector2(x, 0f);
            x += 190f;
            bool front = focused == w && !w.minimized;
            w.taskBg.color = front ? new Color(1f, 1f, 1f, 0.2f) : new Color(1f, 1f, 1f, 0.07f);
            if (w == gameWin) w.taskLabel.text = pc.Game == GamingPC.GameState.Running ? "Ranked  <color=#5AF0DC>(running)</color>" : "Ranked Arena";
        }
    }

    void TaskButton(Window w)
    {
        var b = UIFactory.Panel("Task - " + w.title, taskList, new Color(1f, 1f, 1f, 0.07f));
        w.taskBg = b;
        w.taskButton = b.rectTransform;
        UIFactory.Place(w.taskButton, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(182, 40));
        w.taskLabel = UIFactory.Text("Label", w.taskButton, 15, Color.white, TextAlignmentOptions.Center);
        w.taskLabel.text = w.title;
        w.taskLabel.enableWordWrapping = false;
        UIFactory.Fill(w.taskLabel.rectTransform, 6, 6, 0, 0);
        var under = UIFactory.Panel("Running", w.taskButton, new Color32(90, 200, 255, 255));
        UIFactory.Place(under.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), Vector2.zero, new Vector2(60, 3));
    }

    // ------------------------------------------------------------------ the game launcher

    void BuildLauncher(RectTransform content)
    {
        var bg = UIFactory.Panel("Launcher", content, new Color32(16, 12, 36, 255));
        UIFactory.Fill(bg.rectTransform);
        var glow1 = UIFactory.Panel("Glow", content, new Color32(120, 40, 200, 80));
        glow1.sprite = Round();
        UIFactory.Place(glow1.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-300, 120), new Vector2(900, 600));
        var glow2 = UIFactory.Panel("Glow", content, new Color32(30, 180, 220, 60));
        glow2.sprite = Round();
        UIFactory.Place(glow2.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(340, -160), new Vector2(900, 600));

        var title = UIFactory.Text("Title", content, 92, new Color32(90, 240, 220, 255), TextAlignmentOptions.Center);
        title.text = "<b>RANKED ARENA</b>";
        UIFactory.Place(title.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 170), new Vector2(1200, 110));
        var sub = UIFactory.Text("Sub", content, 24, new Color(1f, 1f, 1f, 0.7f), TextAlignmentOptions.Center);
        sub.text = "3v3 arena shooter  -  VR";
        UIFactory.Place(sub.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 105), new Vector2(800, 34));
        launcherStatus = UIFactory.Text("Status", content, 26, new Color32(255, 120, 200, 255), TextAlignmentOptions.Center);
        UIFactory.Place(launcherStatus.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 55), new Vector2(900, 36));

        playBg = UIFactory.Panel("Play", content, new Color32(90, 240, 220, 255));
        playBg.sprite = Round();
        playBg.type = Image.Type.Sliced;
        playButton = playBg.rectTransform;
        UIFactory.Place(playButton, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -40), new Vector2(380, 84));
        var fill = UIFactory.Panel("Loading", playButton, new Color32(255, 255, 255, 90));
        launchFill = fill.rectTransform;
        launchFill.anchorMin = Vector2.zero;
        launchFill.anchorMax = new Vector2(0f, 1f);
        launchFill.offsetMin = launchFill.offsetMax = Vector2.zero;
        playText = UIFactory.Text("Text", playButton, 38, new Color32(16, 12, 36, 255), TextAlignmentOptions.Center);
        playText.fontStyle = FontStyles.Bold;
        UIFactory.Fill(playText.rectTransform);

        launcherHint = UIFactory.Text("Hint", content, 24, Color.white, TextAlignmentOptions.Center);
        UIFactory.Place(launcherHint.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -130), new Vector2(1000, 40));

        var quit = UIFactory.Panel("Quit", content, new Color(1f, 1f, 1f, 0.1f));
        quitButton = quit.rectTransform;
        UIFactory.Place(quitButton, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-24, 24), new Vector2(180, 44));
        var qt = UIFactory.Text("Text", quitButton, 18, Color.white, TextAlignmentOptions.Center);
        qt.text = "QUIT GAME";
        UIFactory.Fill(qt.rectTransform);
    }

    void UpdateLauncher()
    {
        if (gameWin == null || !gameWin.open) return;
        var game = pc.Match;
        launcherStatus.text = !game ? "" : game.State switch
        {
            MatchTask.Phase.Waiting => "Ranked match today at 4:00 PM",
            MatchTask.Phase.Round => $"Round {game.RoundNumber} is LIVE   {game.Wins} : {game.Losses}",
            MatchTask.Phase.Break => $"{game.Wins} : {game.Losses}  -  next round soon",
            _ => game.Wins >= game.roundsToWin ? $"Match won {game.Wins} : {game.Losses}" : $"Match lost {game.Wins} : {game.Losses}",
        };
        switch (pc.Game)
        {
            case GamingPC.GameState.Closed:
                playText.text = "PLAY";
                playBg.color = new Color32(90, 240, 220, 255);
                launchFill.anchorMax = new Vector2(0f, 1f);
                launcherHint.text = "";
                quitButton.gameObject.SetActive(false);
                break;
            case GamingPC.GameState.Launching:
                playText.text = "LAUNCHING...";
                playBg.color = new Color32(60, 160, 150, 255);
                launchFill.anchorMax = new Vector2(pc.LaunchProgress, 1f);
                launcherHint.text = "Starting up the VR runtime...";
                quitButton.gameObject.SetActive(true);
                break;
            default:
                playText.text = "RUNNING";
                playBg.color = new Color32(70, 210, 110, 255);
                launchFill.anchorMax = new Vector2(0f, 1f);
                int alpha = (int)((0.6f + Mathf.Sin(Time.unscaledTime * 4f) * 0.4f) * 255f);
                launcherHint.text = $"<alpha=#{alpha:X2}>Put on your VR headset to play";
                quitButton.gameObject.SetActive(true);
                break;
        }
    }

    void LauncherClick()
    {
        if (Over(playButton) && pc.Game == GamingPC.GameState.Closed) pc.LaunchGame();
        else if (Over(quitButton)) pc.QuitGame();
    }

    // ------------------------------------------------------------------ building

    void Build()
    {
        var canvas = UIFactory.CreateCanvas("PC Desktop", 62);
        canvas.transform.SetParent(transform, false);
        root = canvas.gameObject;

        var dim = UIFactory.Panel("Room", root.transform, new Color(0f, 0f, 0f, 0.82f));
        UIFactory.Fill(dim.rectTransform);
        var bezel = UIFactory.Panel("Monitor", root.transform, new Color32(18, 18, 22, 255));
        bezel.sprite = Round();
        bezel.type = Image.Type.Sliced;
        UIFactory.Place(bezel.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 14), new Vector2(ScreenW + 40, ScreenH + 40));
        var led = UIFactory.Panel("Power light", bezel.transform, new Color32(80, 240, 200, 255));
        led.sprite = Round();
        UIFactory.Place(led.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0.5f), new Vector2(0, 10), new Vector2(6, 6));

        screen = UIFactory.Rect("Screen", bezel.transform);
        UIFactory.Place(screen, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(ScreenW, ScreenH));
        screen.gameObject.AddComponent<RectMask2D>();
        Wallpaper();

        AddIcon("homework", "Homework", null, DrawHomework);
        AddIcon("game", "Ranked Arena", null, DrawGame);
        AddIcon("pc", "This PC", "Storage: 98% full.\nMostly game clips.", DrawPC);
        AddIcon("bin", "Recycle Bin", "Empty.\nYou already deleted the evidence.", DrawBin);
        AddIcon("browser", "Browser", "47 tabs open.\nClosing them would take effort.", DrawBrowser);
        AddIcon("music", "Music", "Playlist: \"focus vibes\"\n(you have never focused)", DrawMusic);
        AddIcon("messages", "Messages", "Jay: you on at 4??\nJay: dont be late\nJay: ...", DrawMessages);
        AddIcon("photos", "Photos", "1,204 photos.\n1,198 of them are the cat.", DrawPhotos);
        AddIcon("files", "Files", "Homework (final).docx\nHomework (final 2).docx\nHomework (REAL final).docx", DrawFiles);
        AddIcon("settings", "Settings", "Nothing to change.\nIt's perfect already.", DrawSettings);
        AddIcon("store", "Store", "Your wallet: $0.37", DrawStore);
        AddIcon("notes", "Notes", "TODO:\n- everything", DrawNotes);

        windowLayer = UIFactory.Rect("Windows", screen);
        UIFactory.Fill(windowLayer);
        var big = new Vector2(ScreenW - 40f, ScreenH - TaskbarH - 30f);
        homeworkWin = MakeWindow("homework", "Homework.docx - Docs", big, new Color32(232, 232, 238, 255));
        gameWin = MakeWindow("game", "Ranked Arena", big, new Color32(16, 12, 36, 255));
        BuildLauncher(gameWin.content);
        otherWin = MakeWindow("other", "App", new Vector2(560, 300), new Color32(240, 240, 246, 255));
        otherWin.rt.anchoredPosition = new Vector2(140, 60);
        otherBody = UIFactory.Text("Body", otherWin.content, 24, new Color32(40, 40, 60, 255), TextAlignmentOptions.Center);
        UIFactory.Fill(otherBody.rectTransform, 30, 30, 24, 24);

        BuildTaskbar();
        foreach (var w in windows) TaskButton(w);
        BuildStartMenu();

        cursor = UIFactory.Rect("Cursor", screen);
        cursor.anchorMin = cursor.anchorMax = Vector2.zero;
        cursor.pivot = new Vector2(0f, 1f);
        cursor.sizeDelta = new Vector2(30, 30);
        var cimg = cursor.gameObject.AddComponent<Image>();
        cimg.sprite = Arrow();
        cimg.raycastTarget = false;
        root.SetActive(false);
    }

    void Wallpaper()
    {
        var bg = UIFactory.Rect("Wallpaper", screen).gameObject.AddComponent<RawImage>();
        bg.texture = global::Wallpaper.Texture;
        bg.raycastTarget = false;
        UIFactory.Fill(bg.rectTransform);
    }

    void AddIcon(string id, string label, string text, System.Action<RectTransform> draw)
    {
        int n = icons.Count;
        int column = n / Rows, row = n % Rows;
        var cell = UIFactory.Rect(label, screen);
        cell.anchorMin = cell.anchorMax = new Vector2(0f, 1f);
        cell.pivot = new Vector2(0f, 1f);
        cell.anchoredPosition = new Vector2(18 + column * 118, -16 - row * 128);
        cell.sizeDelta = new Vector2(108, 118);
        var highlight = UIFactory.Panel("Selected", cell, new Color(0.45f, 0.7f, 1f, 0.35f));
        highlight.sprite = Round();
        highlight.type = Image.Type.Sliced;
        UIFactory.Fill(highlight.rectTransform);
        highlight.enabled = false;
        var art = UIFactory.Rect("Icon", cell);
        UIFactory.Place(art, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -10), new Vector2(64, 64));
        draw(art);
        var shadow = UIFactory.Text("Shadow", cell, 16, new Color(0f, 0f, 0f, 0.7f), TextAlignmentOptions.Top);
        shadow.text = label;
        UIFactory.Place(shadow.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(1, 4), new Vector2(104, 40));
        var t = UIFactory.Text("Label", cell, 16, Color.white, TextAlignmentOptions.Top);
        t.text = label;
        UIFactory.Place(t.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0, 5), new Vector2(104, 40));
        icons.Add(new Icon { id = id, label = label, text = text, rt = cell, highlight = highlight });
    }

    void BuildTaskbar()
    {
        var bar = UIFactory.Panel("Taskbar", screen, new Color32(14, 14, 24, 240));
        UIFactory.Place(bar.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), Vector2.zero, new Vector2(ScreenW, TaskbarH));
        startButton = UIFactory.Rect("Start", bar.transform);
        UIFactory.Place(startButton, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(10, 0), new Vector2(44, 44));
        var startBg = UIFactory.Panel("Bg", startButton, new Color(1f, 1f, 1f, 0.08f));
        startBg.sprite = Round();
        startBg.type = Image.Type.Sliced;
        UIFactory.Fill(startBg.rectTransform);
        for (int i = 0; i < 4; i++)
        {
            var q = UIFactory.Panel("Logo", startButton, new Color32(90, 200, 255, 255));
            UIFactory.Place(q.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(i % 2 == 0 ? -6 : 6, i < 2 ? 6 : -6), new Vector2(10, 10));
        }
        taskList = UIFactory.Rect("Open apps", bar.transform);
        UIFactory.Fill(taskList);
        for (int i = 0; i < 3; i++)
        {
            var w = UIFactory.Panel("Wifi", bar.transform, Color.white);
            UIFactory.Place(w.rectTransform, new Vector2(1f, 0.5f), new Vector2(0.5f, 0f), new Vector2(-150 + i * 6, -8), new Vector2(4, 6 + i * 5));
        }
        var spk = UIFactory.Panel("Speaker", bar.transform, Color.white);
        UIFactory.Place(spk.rectTransform, new Vector2(1f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-118, 0), new Vector2(8, 10));
        clock = UIFactory.Text("Clock", bar.transform, 18, Color.white, TextAlignmentOptions.Right);
        clock.enableWordWrapping = false;
        UIFactory.Place(clock.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-14, 0), new Vector2(100, 30));
        var hint = UIFactory.Text("Hint", screen, 15, new Color(1f, 1f, 1f, 0.45f), TextAlignmentOptions.Right);
        hint.text = "Double-click to open   -   TAB switch windows   -   ESC get up";
        UIFactory.Place(hint.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-14, TaskbarH + 8), new Vector2(600, 22));
    }

    void BuildStartMenu()
    {
        var m = UIFactory.Panel("Start menu", screen, new Color32(24, 24, 40, 245));
        startMenu = m.rectTransform;
        UIFactory.Place(startMenu, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(8, TaskbarH + 6), new Vector2(300, 220));
        var user = UIFactory.Text("User", startMenu, 22, Color.white, TextAlignmentOptions.TopLeft);
        user.text = "<b>You</b>\n<size=70%><color=#FFFFFF88>Ranked: Silver II</color></size>";
        UIFactory.Fill(user.rectTransform, 20, 20, 18, 100);
        var up = UIFactory.Panel("Get up", startMenu, new Color(1f, 1f, 1f, 0.08f));
        startGetUp = up.rectTransform;
        UIFactory.Place(startGetUp, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0, 14), new Vector2(270, 44));
        var t = UIFactory.Text("Text", up.transform, 18, Color.white, TextAlignmentOptions.Center);
        t.text = "Get up from the PC  (ESC)";
        UIFactory.Fill(t.rectTransform);
        startMenu.gameObject.SetActive(false);
    }

    static string ClockText()
    {
        var day = DayManager.Instance;
        if (!day) return "";
        float hour = day.Hour;
        int h = Mathf.FloorToInt(hour), m = Mathf.FloorToInt((hour - h) * 60f);
        return $"{(h > 12 ? h - 12 : h)}:{m:00} {(h >= 12 ? "PM" : "AM")}";
    }

    // ------------------------------------------------------------------ icon drawings (simple shapes)

    static Image Shape(RectTransform parent, Color c, Vector2 at, Vector2 size, bool rounded = false, float rotate = 0f)
    {
        var img = UIFactory.Panel("Shape", parent, c);
        if (rounded) { img.sprite = Round(); img.type = Image.Type.Sliced; }
        UIFactory.Place(img.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), at, size);
        if (rotate != 0f) img.rectTransform.localRotation = Quaternion.Euler(0, 0, rotate);
        return img;
    }

    static Image Dot(RectTransform parent, Color c, Vector2 at, float size)
    {
        var img = Shape(parent, c, at, new Vector2(size, size));
        img.sprite = Round();
        return img;
    }

    static void DrawHomework(RectTransform r)
    {
        Shape(r, Color.white, Vector2.zero, new Vector2(48, 60), true);
        Shape(r, new Color32(52, 120, 206, 255), new Vector2(0, 22), new Vector2(48, 12));
        for (int i = 0; i < 4; i++) Shape(r, new Color32(170, 170, 185, 255), new Vector2(-2, 6 - i * 9), new Vector2(i == 3 ? 22 : 32, 3));
    }

    static void DrawGame(RectTransform r)
    {
        Shape(r, new Color32(90, 240, 220, 255), Vector2.zero, new Vector2(60, 60), true);
        Shape(r, new Color32(20, 18, 40, 255), Vector2.zero, new Vector2(54, 54), true);
        var t = UIFactory.Text("VR", r, 24, new Color32(90, 240, 220, 255), TextAlignmentOptions.Center);
        t.text = "<b>VR</b>";
        UIFactory.Fill(t.rectTransform);
        Shape(r, new Color32(255, 90, 180, 255), new Vector2(0, -18), new Vector2(30, 3));
    }

    static void DrawPC(RectTransform r)
    {
        Shape(r, new Color32(200, 205, 220, 255), new Vector2(0, 6), new Vector2(56, 40), true);
        Shape(r, new Color32(60, 140, 230, 255), new Vector2(0, 6), new Vector2(48, 32));
        Shape(r, new Color32(200, 205, 220, 255), new Vector2(0, -20), new Vector2(10, 10));
        Shape(r, new Color32(200, 205, 220, 255), new Vector2(0, -26), new Vector2(30, 4));
    }

    static void DrawBin(RectTransform r)
    {
        Shape(r, new Color32(190, 210, 230, 255), new Vector2(0, -4), new Vector2(38, 46), true);
        Shape(r, new Color32(150, 170, 195, 255), new Vector2(0, 22), new Vector2(46, 6));
        Shape(r, new Color32(150, 170, 195, 255), new Vector2(0, 26), new Vector2(14, 5));
        for (int i = -1; i <= 1; i++) Shape(r, new Color32(150, 170, 195, 255), new Vector2(i * 10, -4), new Vector2(3, 32));
    }

    static void DrawBrowser(RectTransform r)
    {
        Dot(r, new Color32(60, 150, 240, 255), Vector2.zero, 56);
        Dot(r, new Color32(120, 200, 255, 255), new Vector2(-8, 8), 22);
        Shape(r, new Color(1f, 1f, 1f, 0.6f), Vector2.zero, new Vector2(56, 3));
        Shape(r, new Color(1f, 1f, 1f, 0.6f), Vector2.zero, new Vector2(3, 56));
    }

    static void DrawMusic(RectTransform r)
    {
        Shape(r, new Color32(255, 120, 60, 255), Vector2.zero, new Vector2(56, 56), true);
        Dot(r, Color.white, new Vector2(-8, -12), 16);
        Shape(r, Color.white, new Vector2(-1, 4), new Vector2(3, 30));
        Shape(r, Color.white, new Vector2(7, 16), new Vector2(16, 4), false, -20f);
    }

    static void DrawMessages(RectTransform r)
    {
        Shape(r, new Color32(80, 210, 120, 255), new Vector2(0, 4), new Vector2(56, 42), true);
        Shape(r, new Color32(80, 210, 120, 255), new Vector2(-14, -16), new Vector2(12, 12), false, 45f);
        for (int i = 0; i < 3; i++) Dot(r, Color.white, new Vector2(-12 + i * 12, 4), 7);
    }

    static void DrawPhotos(RectTransform r)
    {
        Shape(r, Color.white, Vector2.zero, new Vector2(58, 48), true);
        Shape(r, new Color32(150, 210, 255, 255), new Vector2(0, 2), new Vector2(50, 38));
        Shape(r, new Color32(70, 170, 90, 255), new Vector2(-6, -8), new Vector2(26, 26), false, 45f);
        Dot(r, new Color32(255, 210, 70, 255), new Vector2(13, 10), 10);
        Shape(r, Color.white, new Vector2(0, -21), new Vector2(58, 6));
    }

    static void DrawFiles(RectTransform r)
    {
        Shape(r, new Color32(240, 190, 60, 255), new Vector2(-12, 16), new Vector2(24, 10), true);
        Shape(r, new Color32(250, 205, 80, 255), new Vector2(0, -2), new Vector2(58, 40), true);
    }

    static void DrawSettings(RectTransform r)
    {
        for (int i = 0; i < 8; i++)
        {
            float a = i * 45f * Mathf.Deg2Rad;
            Shape(r, new Color32(150, 160, 180, 255), new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 22f, new Vector2(12, 12), false, i * 45f);
        }
        Dot(r, new Color32(150, 160, 180, 255), Vector2.zero, 44);
        Dot(r, new Color32(40, 44, 60, 255), Vector2.zero, 16);
    }

    static void DrawStore(RectTransform r)
    {
        Shape(r, new Color32(130, 90, 240, 255), new Vector2(0, -6), new Vector2(50, 42), true);
        Dot(r, new Color32(130, 90, 240, 255), new Vector2(0, 16), 24);
        Dot(r, Color.white, new Vector2(0, -6), 12);
    }

    static void DrawNotes(RectTransform r)
    {
        Shape(r, new Color32(255, 232, 115, 255), Vector2.zero, new Vector2(52, 56), true);
        for (int i = 0; i < 4; i++) Shape(r, new Color32(200, 170, 60, 255), new Vector2(0, 14 - i * 10), new Vector2(34, 3));
    }

    // ------------------------------------------------------------------ sprites made in code

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
        round = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(12, 12, 12, 12));
        return round;
    }

    /// <summary>A classic white mouse pointer with a black outline.</summary>
    static Sprite Arrow()
    {
        if (arrow) return arrow;
        const int size = 32;
        Vector2[] poly = { new Vector2(1, 1), new Vector2(1, 24), new Vector2(7, 18), new Vector2(11, 28), new Vector2(15, 26), new Vector2(11, 17), new Vector2(19, 17) };
        bool Inside(float x, float y)
        {
            bool inside = false;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
                if ((poly[i].y > y) != (poly[j].y > y) && x < (poly[j].x - poly[i].x) * (y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x)
                    inside = !inside;
            return inside;
        }
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
        var px = new Color32[size * size];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float cx = x + 0.5f, cy = y + 0.5f;
            Color32 c = new Color32(0, 0, 0, 0);
            if (Inside(cx, cy)) c = new Color32(255, 255, 255, 255);
            else if (Inside(cx + 1.2f, cy) || Inside(cx - 1.2f, cy) || Inside(cx, cy + 1.2f) || Inside(cx, cy - 1.2f)) c = new Color32(0, 0, 0, 255);
            px[(size - 1 - y) * size + x] = c;
        }
        tex.SetPixels32(px);
        tex.Apply();
        arrow = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0f, 1f), 100f);
        return arrow;
    }
}
