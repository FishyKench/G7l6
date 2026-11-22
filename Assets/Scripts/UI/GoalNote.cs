using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A sticky note above the stress / tired card (bottom left): the ONE thing you're working towards right now, with progress,
/// and Mom's Trust underneath. It picks the most important goal by itself:
///   tournament today -> win it;  rank not reached -> reach the Friday rank;  trust too low for Friday -> win Mom back;
///   no webcam -> save up for one;  under 100 followers -> grow the channel;  otherwise -> stay qualified.
/// Hidden in VR (like the rest of the house HUD).
/// </summary>
public class GoalNote : MonoBehaviour
{
    TMP_FontAsset font;
    RectTransform card, goalFill, trustFill;
    TextMeshProUGUI title, detail, trustText;
    Image goalFillImage, trustFillImage;
    GameObject goalBar;
    CanvasGroup group;
    string lastTitle;
    float pop;

    void Start()
    {
        font = GameHUD.Instance ? GameHUD.Instance.font : null;
        var canvas = UIFactory.CreateCanvas("Goal Canvas", 51);
        canvas.transform.SetParent(transform, false);
        group = canvas.gameObject.AddComponent<CanvasGroup>();
        group.blocksRaycasts = false;

        card = UIFactory.Rect("Goal", canvas.transform);
        UIFactory.Place(card, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(40, 214), new Vector2(330, 128));
        card.localRotation = Quaternion.Euler(0, 0, -1.5f);
        var shadow = UIFactory.Panel("Shadow", card, Color.white);
        shadow.sprite = HudArt.Get("note_shadow");
        UIFactory.Place(shadow.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(6, -8), new Vector2(348, 146));
        var paper = UIFactory.Panel("Paper", card, HudArt.NoteBlue);
        paper.sprite = HudArt.Get("note_paper");
        UIFactory.Fill(paper.rectTransform);
        var tape = UIFactory.Panel("Tape", card, Color.white);
        tape.sprite = HudArt.Get("tape");
        UIFactory.Place(tape.rectTransform, new Vector2(1f, 1f), new Vector2(0.5f, 0.5f), new Vector2(-30, -6), new Vector2(90, 26));
        tape.rectTransform.localRotation = Quaternion.Euler(0, 0, -24f);

        var label = Text("Label", 14, HudArt.InkSoft, new Vector2(18, -10), new Vector2(200, 18));
        label.text = "GOAL";
        label.characterSpacing = 4f;
        title = Text("Title", 22, HudArt.Ink, new Vector2(18, -28), new Vector2(294, 28));
        title.fontStyle = FontStyles.Bold;
        detail = Text("Detail", 15, HudArt.InkSoft, new Vector2(18, -56), new Vector2(294, 20));

        goalBar = Bar(new Vector2(18, -80), out goalFill, out goalFillImage).gameObject;

        trustText = Text("Trust", 15, HudArt.Ink, new Vector2(18, -94), new Vector2(172, 22));
        var tb = Bar(new Vector2(200, -104), out trustFill, out trustFillImage, 112);
        tb.sizeDelta = new Vector2(112, 8);
    }

    TextMeshProUGUI Text(string name, float size, Color color, Vector2 at, Vector2 box)
    {
        var t = UIFactory.Text(name, card, size, color, TextAlignmentOptions.MidlineLeft, font);
        t.enableWordWrapping = false;
        t.overflowMode = TextOverflowModes.Ellipsis;
        t.enableAutoSizing = true;
        t.fontSizeMax = size;
        t.fontSizeMin = size * 0.7f;
        UIFactory.Place(t.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), at, box);
        return t;
    }

    RectTransform Bar(Vector2 at, out RectTransform fill, out Image fillImage, float width = 294f)
    {
        var bg = UIFactory.Panel("Bar", card, new Color(0.13f, 0.13f, 0.16f, 0.16f));
        UIFactory.Place(bg.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 0.5f), at, new Vector2(width, 8));
        fillImage = UIFactory.Panel("Fill", bg.transform, HudArt.MarkerGreen);
        fill = fillImage.rectTransform;
        fill.anchorMin = Vector2.zero;
        fill.anchorMax = new Vector2(0f, 1f);
        fill.offsetMin = fill.offsetMax = Vector2.zero;
        return bg.rectTransform;
    }

    void Update()
    {
        if (!card) return;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        // Debug: Ctrl+[ / Ctrl+] = Mom's trust -10 / +10.
        if (Input.GetKey(KeyCode.LeftControl) && Input.GetKeyDown(KeyCode.LeftBracket)) Trust.Set(Trust.Value - 10);
        if (Input.GetKey(KeyCode.LeftControl) && Input.GetKeyDown(KeyCode.RightBracket)) Trust.Set(Trust.Value + 10);
#endif
        var day = DayManager.Instance;
        bool hide = VRHeadset.Worn || (day && day.State == DayManager.Phase.Report);
        group.alpha = Mathf.MoveTowards(group.alpha, hide ? 0f : 1f, Time.unscaledDeltaTime * 4f);

        Current(out string t, out string d, out float progress);
        title.text = t;
        detail.text = d;
        goalBar.SetActive(progress >= 0f);
        if (progress >= 0f) goalFill.anchorMax = new Vector2(Mathf.Clamp01(progress), 1f);
        goalFillImage.color = HudArt.MarkerOrange;

        int trust = Trust.Value;
        int change = Trust.LastChange;
        string arrow = change > 0 ? $" <color=#2E9E44>+{change}</color>" : change < 0 ? $" <color=#E0352B>{change}</color>" : "";
        trustText.text = $"MOM: <b>{Trust.Mood}</b>{arrow}";
        trustFill.anchorMax = new Vector2(trust / 100f, 1f);
        trustFillImage.color = Trust.Colour;

        // A little bounce when the goal changes.
        if (lastTitle != null && lastTitle != t) pop = 1f;
        lastTitle = t;
        pop = Mathf.MoveTowards(pop, 0f, Time.unscaledDeltaTime * 2f);
        card.localScale = Vector3.one * (1f + 0.08f * Mathf.Sin(pop * Mathf.PI));
    }

    /// <summary>The most important goal right now. progress &lt; 0 = no bar.</summary>
    public static void Current(out string title, out string detail, out float progress)
    {
        string gate = Campaign.RankName(Campaign.GateRank);
        if (Campaign.TournamentToday)
        {
            title = "WIN THE TOURNAMENT";
            detail = $"1st ${Campaign.Prize1st}  -  2nd ${Campaign.Prize2nd}  -  starts {Campaign.HourText(Campaign.Today.matchStartHour)}";
            progress = -1f;
            return;
        }
        if (Campaign.IsFriday)
        {
            title = "SURVIVE FRIDAY";
            detail = Campaign.ReachedGate ? "No tournament this week - Mom said no." : $"Missed {gate} - next week, start the climb early.";
            progress = -1f;
            return;
        }
        if (Campaign.PromoPending)
        {
            title = "WIN YOUR PROMOTION MATCH";
            detail = $"Win today's ranked match to reach {Campaign.RankName(Campaign.PromoRank)}";
            progress = -1f;
            return;
        }
        if (!Campaign.ReachedGate)
        {
            title = $"REACH {gate.ToUpperInvariant()} BY FRIDAY";
            detail = $"{Campaign.Rating} / {Campaign.GateRating} RP  -  win ranked matches";
            progress = Campaign.Rating / (float)Mathf.Max(1, Campaign.GateRating);
            return;
        }
        if (!Trust.CanGoToTournament)
        {
            title = "WIN BACK MOM'S TRUST";
            detail = $"{Trust.Value} / {Trust.TournamentNeedsNow}  -  she has to drive you on Friday";
            progress = Trust.Value / (float)Trust.TournamentNeedsNow;
            return;
        }
        var cam = Shop.Find(Shop.Webcam);
        if (cam != null && Shop.StateOf(Shop.Webcam) == Shop.State.NotOwned)
        {
            title = "SAVE UP FOR A WEBCAM + MIC";
            detail = $"{Wallet.MoneyText} / ${cam.price}  -  then you can stream";
            progress = Wallet.Money / cam.price;
            return;
        }
        if (cam != null && !Shop.IsInstalled(Shop.Webcam))
        {
            title = "SET UP YOUR WEBCAM";
            detail = "Open the box by the front door (E)";
            progress = -1f;
            return;
        }
        if (ClipHub.Followers < 100)
        {
            title = "GROW YOUR CHANNEL";
            detail = $"{ClipHub.Followers} / 100 followers  -  stream and upload clips";
            progress = ClipHub.Followers / 100f;
            return;
        }
        title = "STAY QUALIFIED";
        detail = "Keep the house in shape - Friday is coming";
        progress = -1f;
    }
}
