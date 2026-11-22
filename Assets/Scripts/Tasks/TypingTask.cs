using System.Text;
using TMPro;
using UnityEngine;

/// <summary>
/// Homework: sit at the desk and type randomly generated pages.
/// Typing must match exactly (capitals, commas, spaces). A wrong key is marked red, adds a
/// little stress, and blocks typing until the player presses Backspace to remove it.
/// Progress is kept if the player gets up (Esc) to deal with another task.
/// </summary>
public class TypingTask : TaskBase, IMainTask, IPromptProvider
{
    [Header("Homework")]
    public int pagesRequired = 3;
    [Tooltip("Roughly how many characters each page has.")]
    public int minCharsPerPage = 170;
    [Tooltip("Stress added per wrong key. Keep it small - typos happen a lot.")]
    public float stressPerTypo = 0.75f;
    [Tooltip("Optional font for the homework sheet.")]
    public TMP_FontAsset font;

    const string TypedColor = "#2E8B3E";   // what you typed right: green
    const string CursorColor = "#1E1E24";  // the next letter to type
    const string UpcomingColor = "#9B9BA5";
    const string CursorMark = "#FFC83D66";
    const string ErrorColor = "#E0201B";
    const string ErrorMark = "#E0201B33";

    string page;
    int position;
    bool hasError;
    char wrongChar;
    int pagesDone;
    int typos;
    bool open;
    int openedFrame;

    GameObject ui;
    TextMeshProUGUI titleText, bodyText, footerText;

    void Reset()
    {
        taskName = "Homework";
        isMainTask = true;
        canStartRandomly = false;
        timeLimit = 0f;
        stressOnFail = 0f;
        stressReliefOnComplete = 15f;
    }

    public override float Progress =>
        pagesRequired <= 0 ? 1f : (pagesDone + (string.IsNullOrEmpty(page) ? 0f : (float)position / page.Length)) / pagesRequired;

    public override string Status => $"Page {Mathf.Min(pagesDone + 1, pagesRequired)}/{pagesRequired} - at the desk";

    public string Prompt => !IsActive ? null : CatBrain.OnKeyboard ? $"{CatBrain.Name} is on the keyboard - shoo her" : "Do homework";

    /// <summary>The cat is lying on the keyboard and steps on a key: a typo (only matters while you're typing).</summary>
    public void CatMashesKeys()
    {
        if (!open || hasError || string.IsNullOrEmpty(page)) return;
        const string mash = "asdfghjkl;qwertyuiopzxcvbnm";
        hasError = true;
        wrongChar = mash[Random.Range(0, mash.Length)];
        typos++;
        if (Stress) Stress.AddStress(stressPerTypo);
        Refresh();
    }

    protected override void OnActivated()
    {
        pagesDone = 0;
        typos = 0;
        NewPage();
    }

    void NewPage()
    {
        page = TypingTextGenerator.Generate(minCharsPerPage);
        position = 0;
        hasError = false;
    }

    // ---------- IMainTask ----------

    public bool ShouldBlockInput() => open; // E is typed, not used to stand up

    /// <summary>Drawn inside a window on the gaming PC's desktop (instead of its own full-screen sheet).</summary>
    public bool Hosted => hostParent;
    RectTransform hostParent;

    /// <summary>Show the homework inside a window (the PC's desktop). Typing only happens while it's started.</summary>
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
            // In a window: start typing (if the cat's on the keys, the page says so and waits).
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
        EnsureUI();
        ui.SetActive(true);
        Refresh();
    }

    public void StopMainTask()
    {
        open = false;
        if (ui && !Hosted) ui.SetActive(false);
        else Refresh();
    }

    // ---------- typing ----------

    protected override void TaskUpdate()
    {
        if (!open || Time.frameCount == openedFrame) return;
        if (CatBrain.OnKeyboard)
        {
            // Can't type with a cat on the keys (Esc still works to get up and shoo her).
            if (Time.frameCount % 15 == 0) Refresh();
            return;
        }

        string input = Input.inputString;
        if (string.IsNullOrEmpty(input)) return;

        foreach (char c in input)
        {
            if (c == '\b')
            {
                if (hasError) hasError = false;
                else if (position > 0) position--;
                continue;
            }
            if (char.IsControl(c)) continue; // Enter, Esc, Tab...
            if (hasError) continue; // must Backspace first

            if (c == page[position])
            {
                position++;
                if (position >= page.Length)
                {
                    PageFinished();
                    return;
                }
            }
            else
            {
                hasError = true;
                wrongChar = c;
                typos++;
                if (Stress) Stress.AddStress(stressPerTypo);
            }
        }
        Refresh();
    }

    void PageFinished()
    {
        pagesDone++;
        if (pagesDone >= pagesRequired)
        {
            StopMainTask();
            if (!Hosted && PlayerInteraction.Instance) PlayerInteraction.Instance.ReleaseMainTask(this);
            Complete("Homework finished!");
            Refresh();
            return;
        }
        NewPage();
        GameHUD.Toast($"Page {pagesDone}/{pagesRequired} done!", GameHUD.Good);
        Refresh();
    }

    // ---------- UI ----------

    void Refresh()
    {
        if (!ui) return;
        if (!IsActive && LastSucceeded)
        {
            titleText.text = "<b>Homework</b>   <size=70%>all done!</size>";
            bodyText.text = "<color=#2E9E44>Every page is typed. Mom will be proud (for once).</color>";
            footerText.text = Hosted ? "Close the window or get up (ESC)." : "";
            return;
        }
        if (string.IsNullOrEmpty(page)) return;

        titleText.text = $"<b>Homework</b>   <size=70%>Page {pagesDone + 1} of {pagesRequired}</size>";

        var sb = new StringBuilder(page.Length + 128);
        sb.Append("<color=").Append(TypedColor).Append('>').Append(page, 0, position).Append("</color>");

        if (hasError)
        {
            // The wrong key takes the place of the letter that should be there, e.g. "HeLlo".
            string shown = wrongChar == ' ' ? "_" : wrongChar.ToString();
            sb.Append("<mark=").Append(ErrorMark).Append("><color=").Append(ErrorColor).Append("><u><noparse>")
              .Append(shown).Append("</noparse></u></color></mark>");
            if (position + 1 < page.Length)
                sb.Append("<color=").Append(UpcomingColor).Append('>').Append(page, position + 1, page.Length - position - 1).Append("</color>");
        }
        else
        {
            sb.Append("<mark=").Append(CursorMark).Append("><color=").Append(CursorColor).Append('>')
              .Append(page[position]).Append("</color></mark>");
            if (position + 1 < page.Length)
                sb.Append("<color=").Append(UpcomingColor).Append('>').Append(page, position + 1, page.Length - position - 1).Append("</color>");
        }
        bodyText.text = sb.ToString();

        if (CatBrain.OnKeyboard)
            footerText.text = $"<color={ErrorColor}><b>{CatBrain.Name} is lying on the keyboard!</b></color>  <b>Esc</b>, then shoo her.      Mistakes: {typos}";
        else
            footerText.text = hasError
                ? $"<color={ErrorColor}><b>Wrong key!</b> Press Backspace to fix it.</color>      Mistakes: {typos}"
                : !open && Hosted ? "<b>Click the page</b> to keep typing" : $"Mistakes: {typos}      <b>Backspace</b> fix      <b>Esc</b> {(Hosted ? "get up" : "stand up")}";
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

        bodyText = UIFactory.Text("Body", paper.transform, 34, new Color(0.15f, 0.15f, 0.2f), TextAlignmentOptions.TopLeft, font);
        bodyText.lineSpacing = 18f;
        bodyText.gameObject.AddComponent<PcSketchKeep>(); // the PC's ink restyle would turn the grey (to type) and green (typed) all one ink
        UIFactory.Fill(bodyText.rectTransform, 100, 60, 120, 90);

        footerText = UIFactory.Text("Footer", paper.transform, 22, new Color(0.3f, 0.3f, 0.36f), TextAlignmentOptions.BottomLeft, font);
        UIFactory.Fill(footerText.rectTransform, 100, 50, 440, 30);

        ui.SetActive(false);
    }
}
