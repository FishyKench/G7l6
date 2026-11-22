using TMPro;
using UnityEngine;

/// <summary>
/// Homework: sit at the desk and do the maths questions (Tuning.HomeworkQuestions of them): 2-digit adding and taking away,
/// times tables (up to 13 x 9), never dividing - made up fresh each time; they get longer with the days you've played and the gadgets
/// you've bought (see MathHomework). Type the answer, Enter to check it: right = the next question, wrong = a mistake (a little stress)
/// and try again. The cat lying on the keyboard steps on the number keys. Progress is kept if you get up (Esc).
/// The paper is also scrap paper: draw on it with the mouse to work the sum out (left = pen, right = eraser, or pick the tool at the
/// top; CLEAR wipes it). It's wiped for every new question.
/// ('pagesRequired' is the number of questions; the name stays so the rest of the game keeps working.)
/// </summary>
public class TypingTask : TaskBase, IMainTask, IPromptProvider
{
    [Header("Homework")]
    public int pagesRequired = 2;
    [Tooltip("Not used any more (the homework is maths now).")]
    public int minCharsPerPage = 170;
    [Tooltip("Stress added per wrong answer (or a key the cat steps on).")]
    public float stressPerTypo = 3f;
    [Tooltip("Optional font for the homework sheet.")]
    public TMP_FontAsset font;

    const string GoodColor = "#2E8B3E";
    const string InkColor = "#1E1E24";
    const string FaintColor = "#9B9BA5";
    const string ErrorColor = "#E0201B";

    MathHomework.Question question;
    bool hasQuestion;
    string answer = "";
    string feedback = "";
    float feedbackUntil;
    int pagesDone;
    int typos;
    bool open;
    int openedFrame;

    GameObject ui;
    TextMeshProUGUI titleText, bodyText, footerText;

    // ---- scrap paper: drawing with the mouse
    const float PaperW = 1100f, PaperH = 520f;
    const float InkLeft = 76f, InkRight = 16f, InkTop = 96f, InkBottom = 44f;
    const int InkW = (int)(PaperW - InkLeft - InkRight), InkH = (int)(PaperH - InkTop - InkBottom);
    static readonly Color32 InkColour = new Color32(28, 40, 96, 255), NoInk = new Color32(0, 0, 0, 0);
    RectTransform inkRect, penButton, eraserButton, clearButton;
    TextMeshProUGUI penLabel, eraserLabel;
    Texture2D ink;
    Color32[] inkPixels;
    bool inkDirty, eraserTool;
    Vector2? lastInk;
    PCDesktop pcDesktop;

    void Reset()
    {
        taskName = "Homework";
        isMainTask = true;
        canStartRandomly = false;
        timeLimit = 0f;
        stressOnFail = 0f;
        stressReliefOnComplete = 15f;
    }

    public override float Progress => pagesRequired <= 0 ? 1f : (float)pagesDone / pagesRequired;

    public override string Status => $"Question {Mathf.Min(pagesDone + 1, pagesRequired)}/{pagesRequired} - at the desk";

    public string Prompt => !IsActive ? null : CatBrain.OnKeyboard ? $"{CatBrain.Name} is on the keyboard - shoo her" : "Do homework";

    /// <summary>The cat is lying on the keyboard and steps on a key: a stray number in your answer (only matters while you're at it).</summary>
    public void CatMashesKeys()
    {
        if (!open || !hasQuestion || answer.Length >= 12) return;
        answer += (char)('0' + Random.Range(0, 10));
        typos++;
        if (Stress) Stress.AddStress(stressPerTypo * 0.5f);
        Say($"<color={ErrorColor}>{CatBrain.Name} stepped on the keys!</color>");
    }

    protected override void OnActivated()
    {
        pagesRequired = Mathf.Max(1, Tuning.HomeworkQuestions);
        pagesDone = 0;
        typos = 0;
        NewQuestion();
    }

    void NewQuestion()
    {
        question = MathHomework.Make(MathHomework.Level);
        hasQuestion = true;
        answer = "";
        ClearInk(); // a clean sheet for every question
    }

    // ---------- scrap paper ----------

    void ClearInk()
    {
        if (inkPixels == null) return;
        for (int i = 0; i < inkPixels.Length; i++) inkPixels[i] = NoInk;
        inkDirty = true;
        lastInk = null;
    }

    /// <summary>Where the pointer is on the scrap paper, in pixels (the PC's own pointer when it's on the PC).</summary>
    bool PointerOn(RectTransform rt, out Vector2 local)
    {
        local = default;
        if (!rt) return false;
        if (Hosted)
        {
            if (!pcDesktop) pcDesktop = FindObjectOfType<PCDesktop>();
            return pcDesktop && pcDesktop.PointerLocal(rt, out local);
        }
        return RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, Input.mousePosition, null, out local) && rt.rect.Contains(local);
    }

    void UpdateInk()
    {
        if (ink == null) return;
        // the tool buttons
        if (Input.GetMouseButtonDown(0))
        {
            if (PointerOn(penButton, out _)) { eraserTool = false; RefreshTools(); return; }
            if (PointerOn(eraserButton, out _)) { eraserTool = true; RefreshTools(); return; }
            if (PointerOn(clearButton, out _)) { ClearInk(); return; }
        }
        bool left = Input.GetMouseButton(0), right = Input.GetMouseButton(1);
        if ((left || right) && PointerOn(inkRect, out var local))
        {
            var r = inkRect.rect;
            var p = new Vector2((local.x - r.x) / r.width * InkW, (local.y - r.y) / r.height * InkH);
            bool erase = right || eraserTool;
            Stroke(lastInk ?? p, p, erase ? 13 : 2, erase ? NoInk : InkColour);
            lastInk = p;
        }
        else lastInk = null;
        if (inkDirty)
        {
            ink.SetPixels32(inkPixels);
            ink.Apply(false);
            inkDirty = false;
        }
    }

    /// <summary>A round brush dragged from a to b.</summary>
    void Stroke(Vector2 a, Vector2 b, int radius, Color32 colour)
    {
        int steps = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(a, b)));
        for (int s = 0; s <= steps; s++)
        {
            var c = Vector2.Lerp(a, b, s / (float)steps);
            int cx = Mathf.RoundToInt(c.x), cy = Mathf.RoundToInt(c.y);
            for (int y = -radius; y <= radius; y++)
            {
                int py = cy + y;
                if (py < 0 || py >= InkH) continue;
                for (int x = -radius; x <= radius; x++)
                {
                    int px = cx + x;
                    if (px < 0 || px >= InkW || x * x + y * y > radius * radius + radius) continue;
                    inkPixels[py * InkW + px] = colour;
                }
            }
        }
        inkDirty = true;
    }

    void RefreshTools()
    {
        if (!penLabel) return;
        penLabel.text = eraserTool ? "PEN" : "<u><b>PEN</b></u>";
        eraserLabel.text = eraserTool ? "<u><b>ERASER</b></u>" : "ERASER";
    }

    void Say(string text)
    {
        feedback = text;
        feedbackUntil = Time.unscaledTime + 2.2f;
        Refresh();
    }

    // ---------- IMainTask ----------

    public bool ShouldBlockInput() => open; // E is typed, not used to stand up

    /// <summary>Drawn inside a window on the gaming PC's desktop (instead of its own full-screen sheet).</summary>
    public bool Hosted => hostParent;
    RectTransform hostParent;

    /// <summary>Show the homework inside a window (the PC's desktop). Answering only happens while it's started.</summary>
    public void ShowIn(RectTransform parent)
    {
        if (ui && hostParent != parent) { Destroy(ui); ui = null; }
        hostParent = parent;
        EnsureUI();
        ui.SetActive(true);
        Refresh();
    }

    public void StartMainTask()
    {
        if (Hosted)
        {
            open = IsActive;
            openedFrame = Time.frameCount;
            EnsureUI();
            ui.SetActive(true);
            Refresh();
            return;
        }
        if (!IsActive || CatBrain.OnKeyboard)
        {
            if (CatBrain.OnKeyboard) GameHUD.Toast($"{CatBrain.Name} is lying on your keyboard - shoo her first!", GameHUD.Warn);
            if (PlayerInteraction.Instance) PlayerInteraction.Instance.ReleaseMainTask(this);
            return;
        }
        open = true;
        openedFrame = Time.frameCount; // ignore the "e" that opened the task
        Cursor.lockState = CursorLockMode.None; // the mouse draws on the scrap paper
        Cursor.visible = true;
        EnsureUI();
        ui.SetActive(true);
        Refresh();
    }

    public void StopMainTask()
    {
        if (open && !Hosted) { Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; }
        open = false;
        if (ui && !Hosted) ui.SetActive(false);
        else Refresh();
    }

    // ---------- answering ----------

    protected override void TaskUpdate()
    {
        if (feedbackUntil > 0f && Time.unscaledTime > feedbackUntil) { feedbackUntil = 0f; feedback = ""; Refresh(); }
        if (!open || Time.frameCount == openedFrame) return;
        if (CatBrain.OnKeyboard)
        {
            if (Time.frameCount % 15 == 0) Refresh();
            return;
        }
        if (!hasQuestion) NewQuestion();
        if (Time.frameCount % 20 == 0) Refresh(); // the blinking cursor
        UpdateInk();

        string input = Input.inputString;
        if (string.IsNullOrEmpty(input)) return;
        foreach (char c in input)
        {
            if (c == '\b') { if (answer.Length > 0) answer = answer.Substring(0, answer.Length - 1); continue; }
            if (c == '\n' || c == '\r') { Check(); if (!open) return; continue; }
            if (c >= '0' && c <= '9' && answer.Length < 12) answer += c;
        }
        Refresh();
    }

    void Check()
    {
        if (answer.Length == 0) return;
        if (long.TryParse(answer, out long given) && given == question.answer)
        {
            pagesDone++;
            if (pagesDone >= pagesRequired)
            {
                StopMainTask();
                if (!Hosted && PlayerInteraction.Instance) PlayerInteraction.Instance.ReleaseMainTask(this);
                hasQuestion = false;
                Complete("Homework finished!");
                Refresh();
                return;
            }
            NewQuestion();
            GameHUD.Toast($"Question {pagesDone}/{pagesRequired} right!", GameHUD.Good);
            Say($"<color={GoodColor}><b>Correct!</b></color> Next one.");
        }
        else
        {
            typos++;
            if (Stress) Stress.AddStress(stressPerTypo);
            answer = "";
            Say($"<color={ErrorColor}><b>Not quite.</b></color> Check your working and try again.");
        }
    }

    // ---------- UI ----------

    void Refresh()
    {
        if (!ui) return;
        if (!IsActive && LastSucceeded)
        {
            titleText.text = "<b>Homework</b>   <size=70%>all done!</size>";
            bodyText.text = $"<color={GoodColor}>Every question answered. Mom will be proud (for once).</color>";
            footerText.text = Hosted ? "Close the window or get up (ESC)." : "";
            return;
        }
        if (!hasQuestion) return;

        titleText.text = $"<b>Maths homework</b>   <size=70%>Question {pagesDone + 1} of {pagesRequired}</size>";
        string cursor = open && !CatBrain.OnKeyboard && Mathf.Repeat(Time.unscaledTime, 1f) < 0.6f ? "_" : " ";
        string shown = answer.Length > 0 ? answer : (open ? "" : "...");
        bodyText.text =
            $"<color={FaintColor}><size=70%>Work it out (no calculator!) and type the answer.</size></color>\n\n" +
            $"<size=140%><color={InkColor}><b>{question.text} = </b></color><color={GoodColor}><b>{shown}</b></color><color={InkColor}>{cursor}</color></size>\n\n" +
            (string.IsNullOrEmpty(feedback) ? "" : feedback);

        if (CatBrain.OnKeyboard)
            footerText.text = $"<color={ErrorColor}><b>{CatBrain.Name} is lying on the keyboard!</b></color>  <b>Esc</b>, then shoo her.      Mistakes: {typos}";
        else
            footerText.text = !open && Hosted
                ? "<b>Click the page</b> to keep going"
                : $"Mistakes: {typos}    <b>Enter</b> check    <b>Backspace</b> fix    <b>Mouse</b> draw    <b>Esc</b> {(Hosted ? "get up" : "stand up")}";
    }

    void EnsureUI()
    {
        if (ui) return;

        if (hostParent)
        {
            var holder = UIFactory.Rect("Homework", hostParent);
            UIFactory.Fill(holder);
            ui = holder.gameObject;
        }
        else
        {
            var canvas = UIFactory.CreateCanvas("Homework Canvas", 60);
            ui = canvas.gameObject;
            ui.transform.SetParent(transform, false);
            var dim = UIFactory.Panel("Dim", ui.transform, new Color(0f, 0f, 0f, 0.55f));
            UIFactory.Fill(dim.rectTransform);
        }

        var paper = UIFactory.Panel("Paper", ui.transform, new Color(0.97f, 0.95f, 0.89f, 1f));
        UIFactory.Place(paper.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1100f, 520f));

        var margin = UIFactory.Panel("Margin", paper.transform, new Color(0.9f, 0.35f, 0.35f, 0.6f));
        UIFactory.Place(margin.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(70f, 0f), new Vector2(3f, 520f));

        titleText = UIFactory.Text("Title", paper.transform, 40, new Color(0.15f, 0.15f, 0.2f), TextAlignmentOptions.TopLeft, font);
        UIFactory.Fill(titleText.rectTransform, 100, 50, 36, 420);

        // the scrap paper you draw on (under the writing, so the question stays readable)
        inkRect = UIFactory.Rect("Scrap paper", paper.transform);
        UIFactory.Fill(inkRect, InkLeft, InkRight, InkTop, InkBottom);
        ink = new Texture2D(InkW, InkH, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        inkPixels = new Color32[InkW * InkH];
        ClearInk();
        ink.SetPixels32(inkPixels);
        ink.Apply(false);
        var inkImage = inkRect.gameObject.AddComponent<UnityEngine.UI.RawImage>();
        inkImage.texture = ink;
        inkImage.raycastTarget = false;

        // the tools: top right of the sheet
        TextMeshProUGUI Tool(string name, float x, out RectTransform rt)
        {
            var bg = UIFactory.Panel(name, paper.transform, new Color(0.9f, 0.88f, 0.82f, 1f));
            rt = bg.rectTransform;
            UIFactory.Place(rt, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(x, -22f), new Vector2(110f, 36f));
            var t = UIFactory.Text("Label", rt, 20, new Color(0.15f, 0.15f, 0.2f), TextAlignmentOptions.Center, font);
            UIFactory.Fill(t.rectTransform);
            return t;
        }
        penLabel = Tool("Pen", -258f, out penButton);
        eraserLabel = Tool("Eraser", -140f, out eraserButton);
        Tool("Clear", -22f, out clearButton).text = "CLEAR";
        RefreshTools();

        bodyText = UIFactory.Text("Body", paper.transform, 34, new Color(0.15f, 0.15f, 0.2f), TextAlignmentOptions.TopLeft, font);
        bodyText.lineSpacing = 18f;
        bodyText.gameObject.AddComponent<PcSketchKeep>(); // the PC's ink restyle would turn the grey (to type) and green (typed) all one ink
        UIFactory.Fill(bodyText.rectTransform, 100, 60, 120, 90);

        footerText = UIFactory.Text("Footer", paper.transform, 22, new Color(0.3f, 0.3f, 0.36f), TextAlignmentOptions.BottomLeft, font);
        UIFactory.Fill(footerText.rectTransform, 100, 50, 440, 30);

        ui.SetActive(false);
    }
}
