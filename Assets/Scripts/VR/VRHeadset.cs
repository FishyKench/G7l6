using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The VR headset on your gaming desk. Press E to put it on, ESC to take it off.
/// It shows whatever your PC is running: if you've launched Ranked on the PC, you're in the arena. If not, you
/// just see your PC's screen floating in a dark void, flat and skewed (homework and all) - you have to take the
/// headset off and launch the game on the PC first.
/// Made by the GamingPC at the start (it finds the "VR Headset" on the desk).
/// </summary>
public class VRHeadset : MonoBehaviour, IMainTask, IMainTaskHost, IPromptProvider
{
    public static VRHeadset Instance { get; private set; }
    /// <summary>True while you're wearing it (in the game or not) - the house HUD hides.</summary>
    public static bool Worn => Instance && Instance.worn;

    public GamingPC pc;
    public MatchTask match;

    bool worn, inGame;
    GameObject mirror;
    RectTransform floatingScreen;
    GameObject mirrorDoc, mirrorDesktop, mirrorGame;
    CanvasGroup mirrorGroup;

    void Awake() => Instance = this;

    // ------------------------------------------------------------------ putting it on

    public string Prompt
    {
        get
        {
            if (pc && pc.Game == GamingPC.GameState.Running && match)
                return match.State == MatchTask.Phase.Round ? $"Put on VR headset - round {match.RoundNumber} is LIVE!" : "Put on VR headset";
            return "Put on VR headset";
        }
    }

    public bool ShouldBlockInput() => true; // ESC takes it off

    public void StartMainTask()
    {
        worn = true;
        if (pc && pc.Game == GamingPC.GameState.Running && match)
        {
            inGame = true;
            match.StartMainTask(); // into the arena (the match may refuse - e.g. it's over - and hand us back)
            return;
        }
        inGame = false;
        ShowMirror();
    }

    public void StopMainTask()
    {
        if (inGame && match) match.StopMainTask();
        HideMirror();
        worn = false;
        inGame = false;
    }

    public bool Hosts(IMainTask task) => inGame && ReferenceEquals(task, match);

    public void Released(IMainTask task)
    {
        worn = false;
        inGame = false;
        HideMirror();
    }

    /// <summary>Someone (Mom!) takes the headset off you, or the game closed under you.</summary>
    public static void TakeOff(string why)
    {
        if (!Worn) return;
        if (PlayerInteraction.Instance) PlayerInteraction.Instance.StopCurrentTaskFromUI();
        else Instance.StopMainTask();
        if (!string.IsNullOrEmpty(why)) GameHUD.Toast(why, GameHUD.Warn);
    }

    void Update()
    {
        // The game was closed on the PC while you were in it: you're dropped out.
        if (worn && inGame && pc && pc.Game != GamingPC.GameState.Running) TakeOff("The game closed.");
        if (mirror && mirror.activeSelf) AnimateMirror();
    }

    // ------------------------------------------------------------------ no game: the PC screen floating in the void

    void ShowMirror()
    {
        if (!mirror) BuildMirror();
        mirror.SetActive(true);
        mirrorGroup.alpha = 0f;
        string app = pc ? pc.ScreenApp : null;
        mirrorDoc.SetActive(app == "homework");
        mirrorGame.SetActive(app == "game");
        mirrorDesktop.SetActive(app != "homework" && app != "game");
    }

    void HideMirror()
    {
        if (mirror) mirror.SetActive(false);
    }

    void AnimateMirror()
    {
        mirrorGroup.alpha = Mathf.MoveTowards(mirrorGroup.alpha, 1f, Time.unscaledDeltaTime * 3f);
        float t = Time.unscaledTime;
        floatingScreen.anchoredPosition = new Vector2(Mathf.Sin(t * 0.6f) * 14f, 40f + Mathf.Sin(t * 0.9f) * 10f);
        floatingScreen.localRotation = Quaternion.Euler(Mathf.Sin(t * 0.5f) * 4f, -24f + Mathf.Sin(t * 0.4f) * 3f, 3f + Mathf.Sin(t * 0.7f) * 2f);
    }

    void BuildMirror()
    {
        var canvas = UIFactory.CreateCanvas("VR - nothing running", 45);
        canvas.transform.SetParent(transform, false);
        mirror = canvas.gameObject;
        mirrorGroup = mirror.AddComponent<CanvasGroup>();

        var voidBg = UIFactory.Panel("Void", mirror.transform, new Color32(6, 6, 10, 255));
        UIFactory.Fill(voidBg.rectTransform);
        for (int i = -8; i <= 8; i++)
        {
            var line = UIFactory.Panel("Grid", mirror.transform, new Color(0.4f, 0.6f, 1f, 0.06f));
            UIFactory.Place(line.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(i * 120f, 0f), new Vector2(2f, 380f));
            line.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -i * 6f);
        }

        // Your PC screen, flat and wonky, hanging in space.
        floatingScreen = UIFactory.Rect("Floating screen", mirror.transform);
        UIFactory.Place(floatingScreen, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 40), new Vector2(900, 520));
        var frame = UIFactory.Panel("Frame", floatingScreen, new Color32(30, 30, 36, 255));
        UIFactory.Fill(frame.rectTransform, -10, -10, -10, -10);

        mirrorDesktop = UIFactory.Panel("Desktop", floatingScreen, new Color32(40, 30, 80, 255)).gameObject;
        UIFactory.Fill(mirrorDesktop.GetComponent<RectTransform>());
        for (int i = 0; i < 8; i++)
        {
            var icon = UIFactory.Panel("Icon", mirrorDesktop.transform, new Color(1f, 1f, 1f, 0.35f));
            UIFactory.Place(icon.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(20 + (i / 4) * 80, -20 - (i % 4) * 90), new Vector2(44, 44));
        }
        var bar = UIFactory.Panel("Taskbar", mirrorDesktop.transform, new Color32(14, 14, 24, 255));
        UIFactory.Place(bar.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), Vector2.zero, new Vector2(900, 36));

        mirrorDoc = UIFactory.Panel("Homework", floatingScreen, new Color32(230, 230, 236, 255)).gameObject;
        UIFactory.Fill(mirrorDoc.GetComponent<RectTransform>());
        var page = UIFactory.Panel("Page", mirrorDoc.transform, Color.white);
        UIFactory.Fill(page.rectTransform, 120, 120, 30, 0);
        var title = UIFactory.Text("Title", page.transform, 30, new Color32(40, 40, 60, 255), TextAlignmentOptions.TopLeft);
        title.text = "Homework.docx";
        UIFactory.Fill(title.rectTransform, 30, 30, 20, 0);
        for (int i = 0; i < 8; i++)
        {
            var l = UIFactory.Panel("Line", page.transform, new Color(0.6f, 0.6f, 0.66f, 0.6f));
            UIFactory.Place(l.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(30, -90 - i * 44), new Vector2(Random.Range(380f, 560f), 12));
        }

        mirrorGame = UIFactory.Panel("Launcher", floatingScreen, new Color32(16, 12, 36, 255)).gameObject;
        UIFactory.Fill(mirrorGame.GetComponent<RectTransform>());
        var gt = UIFactory.Text("Title", mirrorGame.transform, 60, new Color32(90, 240, 220, 255), TextAlignmentOptions.Center);
        gt.text = "<b>RANKED ARENA</b>\n<size=50%>not running</size>";
        UIFactory.Fill(gt.rectTransform);

        var msg = UIFactory.Text("Message", mirror.transform, 34, Color.white, TextAlignmentOptions.Center);
        msg.text = "<b>No VR game running.</b>\n<size=75%><color=#FFFFFFAA>This is just your PC screen. Launch RANKED on your PC first.</color></size>";
        UIFactory.Place(msg.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0, 110), new Vector2(1400, 110));
        var hint = UIFactory.Text("Hint", mirror.transform, 20, new Color(1f, 1f, 1f, 0.5f), TextAlignmentOptions.Center);
        hint.text = "ESC  take the headset off";
        UIFactory.Place(hint.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0, 40), new Vector2(800, 30));
        mirror.SetActive(false);
    }
}
