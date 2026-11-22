using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The day's HUD (made by the DayManager): the clock at the top ("4:12 PM", a bar until Mom gets home),
/// Mom's texts popping up like phone notifications (bottom right), and the report card at the end.
/// </summary>
public class DayHud : MonoBehaviour
{
    static readonly Color Ink = new Color32(34, 32, 42, 255);
    static readonly Color Paper = new Color32(250, 247, 238, 255);
    static readonly Color Green = new Color32(46, 158, 68, 255);
    static readonly Color Orange = new Color32(232, 128, 26, 255);
    static readonly Color Red = new Color32(224, 53, 43, 255);
    static readonly Color PhoneDark = new Color32(28, 28, 34, 240);

    DayManager day;
    TMP_FontAsset font;
    RectTransform canvasRoot, clock, phone;
    TextMeshProUGUI timeText, untilText, phoneFrom, phoneMessage, phoneInitial;
    Image phoneAvatar;
    RectTransform barFill;
    Image barImage;
    readonly Queue<(string from, string message)> inbox = new Queue<(string, string)>();
    bool phoneBusy;
    static Sprite round;
    CanvasGroup screenGroup;

    void Start()
    {
        day = GetComponent<DayManager>();
        font = GameHUD.Instance ? GameHUD.Instance.font : null;
        var canvas = UIFactory.CreateCanvas("Day Canvas", 55);
        canvas.transform.SetParent(transform, false);
        canvasRoot = (RectTransform)canvas.transform;
        screenGroup = canvas.gameObject.AddComponent<CanvasGroup>();
        screenGroup.blocksRaycasts = false;
        BuildClock();
        BuildPhone();
    }

    // ------------------------------------------------------------------ clock

    void BuildClock()
    {
        clock = UIFactory.Rect("Clock", canvasRoot);
        UIFactory.Place(clock, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -16), new Vector2(240, 92));
        clock.localRotation = Quaternion.Euler(0, 0, 1f);
        var shadow = UIFactory.Panel("Shadow", clock, new Color(0, 0, 0, 0.2f));
        UIFactory.Fill(shadow.rectTransform, 5, -5, 6, -6);
        var paper = UIFactory.Panel("Paper", clock, Paper);
        paper.sprite = HudArt.Get("note_paper");
        UIFactory.Fill(paper.rectTransform);
        var tape = UIFactory.Panel("Tape", clock, Color.white);
        tape.sprite = HudArt.Get("tape");
        UIFactory.Place(tape.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), new Vector2(0, -2), new Vector2(84, 24));

        timeText = UIFactory.Text("Time", clock, 34, Ink, TextAlignmentOptions.Center, font);
        timeText.fontStyle = FontStyles.Bold;
        timeText.enableWordWrapping = false;
        UIFactory.Place(timeText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), new Vector2(0, -32), new Vector2(220, 40));

        var bar = UIFactory.Panel("Bar", clock, new Color(0.13f, 0.13f, 0.16f, 0.15f));
        bar.sprite = Round();
        bar.type = Image.Type.Sliced;
        UIFactory.Place(bar.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), new Vector2(0, -58), new Vector2(190, 9));
        barImage = UIFactory.Panel("Fill", bar.transform, Green);
        barImage.sprite = Round();
        barImage.type = Image.Type.Sliced;
        barFill = barImage.rectTransform;
        barFill.anchorMin = Vector2.zero;
        barFill.anchorMax = new Vector2(0f, 1f);
        barFill.pivot = new Vector2(0f, 0.5f);
        barFill.offsetMin = barFill.offsetMax = Vector2.zero;

        untilText = UIFactory.Text("Until", clock, 13, new Color(0.13f, 0.13f, 0.16f, 0.65f), TextAlignmentOptions.Center, font);
        untilText.enableWordWrapping = false;
        UIFactory.Place(untilText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), new Vector2(0, -76), new Vector2(220, 18));
    }

    void Update()
    {
        // No clock or phone in VR - you lose track of time (texts wait on your phone until you take it off).
        if (screenGroup) screenGroup.alpha = Mathf.MoveTowards(screenGroup.alpha, VRHeadset.Worn && day && day.State != DayManager.Phase.Report ? 0f : 1f, Time.unscaledDeltaTime * 4f);
        if (!day || !clock) return;
        float hour = day.Hour;
        int h = Mathf.FloorToInt(hour), m = Mathf.FloorToInt((hour - h) * 60f);
        int shown = h > 12 ? h - 12 : h == 0 ? 12 : h;
        timeText.text = $"{shown}:{m:00} {(h >= 12 ? "PM" : "AM")}";

        clock.localRotation = Quaternion.Euler(0, 0, 1f);
        float p = day.DayProgress;
        barFill.anchorMax = new Vector2(p, 1f);
        barImage.color = p < 0.66f ? Green : p < 0.88f ? Orange : Red;

        if (day.Meltdown)
        {
            untilText.text = "MELTDOWN";
            untilText.color = Red;
            clock.localScale = Vector3.one;
        }
        else if (day.PanicLeft >= 0f)
        {
            untilText.text = $"PANIC! MELTDOWN IN {Mathf.CeilToInt(day.PanicLeft)}s";
            untilText.color = Red;
            float shake = Mathf.Sin(Time.unscaledTime * 40f) * 2f;
            clock.localScale = Vector3.one * (1.06f + Mathf.Sin(Time.unscaledTime * 10f) * 0.05f);
            clock.localRotation = Quaternion.Euler(0, 0, 1f + shake);
            return;
        }
        else if (day.State == DayManager.Phase.Day)
        {
            float minutesLeft = (day.endHour - hour) * 60f;
            untilText.text = minutesLeft > 60f ? "MOM HOME AT " + Campaign.HourText(day.endHour).Replace(" PM", "") : $"MOM HOME IN {Mathf.CeilToInt(minutesLeft)} MIN";
            untilText.color = minutesLeft > 30f ? new Color(0.13f, 0.13f, 0.16f, 0.65f) : Red;
            float pulse = minutesLeft <= 10f ? 1f + Mathf.Sin(Time.unscaledTime * 8f) * 0.04f : 1f;
            clock.localScale = new Vector3(pulse, pulse, 1f);
        }
        else
        {
            untilText.text = "MOM'S HOME!";
            untilText.color = Red;
            clock.localScale = Vector3.one;
        }
    }

    // ------------------------------------------------------------------ phone texts

    void BuildPhone()
    {
        phone = UIFactory.Rect("Phone", canvasRoot);
        UIFactory.Place(phone, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-30, 150), new Vector2(330, 104));
        var body = UIFactory.Panel("Body", phone, PhoneDark);
        body.sprite = Round();
        body.type = Image.Type.Sliced;
        UIFactory.Fill(body.rectTransform);

        var dot = UIFactory.Panel("Avatar", phone, new Color32(255, 140, 120, 255));
        phoneAvatar = dot;
        dot.sprite = Round();
        UIFactory.Place(dot.rectTransform, new Vector2(0f, 1f), new Vector2(0.5f, 0.5f), new Vector2(30, -28), new Vector2(34, 34));
        var initial = UIFactory.Text("M", dot.transform, 18, Color.white, TextAlignmentOptions.Center);
        initial.text = "M";
        phoneInitial = initial;
        initial.fontStyle = FontStyles.Bold;
        UIFactory.Fill(initial.rectTransform);

        phoneFrom = UIFactory.Text("From", phone, 17, Color.white, TextAlignmentOptions.MidlineLeft);
        phoneFrom.fontStyle = FontStyles.Bold;
        UIFactory.Place(phoneFrom.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(56, -22), new Vector2(200, 22));
        var now = UIFactory.Text("Now", phone, 13, new Color(1f, 1f, 1f, 0.5f), TextAlignmentOptions.MidlineRight);
        now.text = "now";
        UIFactory.Place(now.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 0.5f), new Vector2(-14, -22), new Vector2(60, 20));
        phoneMessage = UIFactory.Text("Message", phone, 16, new Color(1f, 1f, 1f, 0.92f), TextAlignmentOptions.TopLeft);
        UIFactory.Fill(phoneMessage.rectTransform, 56, 14, 38, 8);

        phone.gameObject.SetActive(false);
    }

    /// <summary>Shows a text message notification (queued if one is showing).</summary>
    public void Text(string from, string message)
    {
        if (MatchTask.InVR && VRArena.Instance && VRArena.Instance.Hud) VRArena.Instance.Hud.Buzz(); // you feel it, you can't read it
        inbox.Enqueue((from, message));
        if (!phoneBusy) StartCoroutine(ShowTexts());
    }

    IEnumerator ShowTexts()
    {
        phoneBusy = true;
        while (inbox.Count > 0)
        {
            while (VRHeadset.Worn) yield return null; // read it when you take the headset off
            var (from, message) = inbox.Dequeue();
            phoneFrom.text = from;
            phoneInitial.text = string.IsNullOrEmpty(from) ? "?" : from.Substring(0, 1);
            phoneAvatar.color = from == "MOM" ? new Color32(255, 140, 120, 255) : new Color32(90, 160, 255, 255);
            phoneMessage.text = message;
            phone.gameObject.SetActive(true);
            yield return Slide(phone, new Vector2(400, 150), new Vector2(-30, 150), 0.3f);
            yield return new WaitForSecondsRealtime(6f);
            yield return Slide(phone, new Vector2(-30, 150), new Vector2(400, 150), 0.25f);
            phone.gameObject.SetActive(false);
        }
        phoneBusy = false;
    }

    static IEnumerator Slide(RectTransform rt, Vector2 from, Vector2 to, float seconds)
    {
        for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / seconds)
        {
            float e = 1f - (1f - t) * (1f - t);
            rt.anchoredPosition = Vector2.LerpUnclamped(from, to, e);
            yield return null;
        }
        rt.anchoredPosition = to;
    }

    // ------------------------------------------------------------------ report card

    public void ShowReport(DayManager.Report r)
    {
        if (phone) phone.gameObject.SetActive(false);
        StopAllCoroutines();
        phoneBusy = false;
        StartCoroutine(Report(r));
    }

    IEnumerator Report(DayManager.Report r)
    {
        var dim = UIFactory.Panel("Dim", canvasRoot, new Color(0f, 0f, 0f, 0f));
        UIFactory.Fill(dim.rectTransform);

        var card = UIFactory.Rect("Report Card", canvasRoot);
        UIFactory.Place(card, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(660, 790));
        card.localRotation = Quaternion.Euler(0, 0, -1.2f);
        var group = card.gameObject.AddComponent<CanvasGroup>();
        group.alpha = 0f;
        var shadow = UIFactory.Panel("Shadow", card, new Color(0, 0, 0, 0.3f));
        UIFactory.Fill(shadow.rectTransform, 10, -10, 12, -12);
        var paper = UIFactory.Panel("Paper", card, Paper);
        paper.sprite = HudArt.Get("note_paper");
        UIFactory.Fill(paper.rectTransform);
        var tape = UIFactory.Panel("Tape", card, Color.white);
        tape.sprite = HudArt.Get("tape");
        UIFactory.Place(tape.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), new Vector2(0, -2), new Vector2(150, 40));

        var title = Line(card, r.meltdown ? "MELTDOWN" : "REPORT CARD", 46, Ink, new Vector2(-70, -60), new Vector2(480, 56), TextAlignmentOptions.Left);
        title.fontStyle = FontStyles.Bold;
        Line(card, r.meltdown ? $"{Campaign.DayName}, week {Campaign.Week} - your stress hit the roof." : $"{Campaign.DayName}, week {Campaign.Week} - Mom got home at {Campaign.HourText(day.endHour)}.",
            17, new Color(0.13f, 0.13f, 0.16f, 0.65f), new Vector2(-70, -100), new Vector2(480, 26), TextAlignmentOptions.Left);

        // Grade, circled in red marker.
        var circle = UIFactory.Panel("Circle", card, Red);
        circle.sprite = HudArt.Get("circle_0");
        UIFactory.Place(circle.rectTransform, new Vector2(1f, 1f), new Vector2(0.5f, 0.5f), new Vector2(-110, -96), new Vector2(150, 150));
        var grade = Line(circle.transform, r.grade, 82, Red, Vector2.zero, new Vector2(150, 120), TextAlignmentOptions.Center);
        grade.fontStyle = FontStyles.Bold;
        UIFactory.Place(grade.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 4), new Vector2(150, 120));
        Line(card, $"{r.score} / 100", 18, Red, new Vector2(-110, -184), new Vector2(150, 24), TextAlignmentOptions.Center, anchorRight: true);
        circle.gameObject.SetActive(false);

        // The match result, stamped next to Mom's grade (it doesn't count towards it).
        if (r.match != null)
        {
            var stamp = Line(card, r.matchWon ? "VICTORY" : r.match.StartsWith("LOST") ? "DEFEAT" : "NO GG", 30,
                r.matchWon ? new Color32(40, 140, 220, 255) : Red, new Vector2(-300, -150), new Vector2(200, 40), TextAlignmentOptions.Center, anchorRight: true);
            stamp.fontStyle = FontStyles.Bold;
            stamp.rectTransform.localRotation = Quaternion.Euler(0, 0, 8f);
        }

        // The breakdown
        float y = -220f;
        void Row(string label, string value, int points, bool sub = false)
        {
            var c = sub ? new Color(0.13f, 0.13f, 0.16f, 0.75f) : Ink;
            Line(card, (sub ? "    " : "") + label, sub ? 16 : 19, c, new Vector2(48, y), new Vector2(380, 26), TextAlignmentOptions.MidlineLeft, anchorLeft: true);
            if (!string.IsNullOrEmpty(value))
                Line(card, value, sub ? 16 : 18, c, new Vector2(-150, y), new Vector2(160, 26), TextAlignmentOptions.MidlineRight, anchorRight: true);
            if (points != 0)
                Line(card, points > 0 ? "+" + points : points.ToString(), 18, points > 0 ? Green : Red, new Vector2(-48, y), new Vector2(80, 26), TextAlignmentOptions.MidlineRight, anchorRight: true);
            y -= sub ? 25f : 31f;
        }

        if (r.match != null)
        {
            Row(r.matchLabel, $"{r.match}  ({r.matchExtra})", 0);
            y -= 6f;
        }
        Row("Homework", r.homework, -r.homeworkPoints);
        Row("Chores done", r.done.ToString(), r.choreBonus);
        Row("Chores failed", r.failed.ToString(), -r.failPenalty);
        Row("Mom found", r.found.Count == 0 ? "nothing!" : r.found.Count.ToString(), 0);
        int shownFindings = 0;
        foreach (var f in r.found)
        {
            if (shownFindings == 7) { Row($"...and {r.found.Count - 7} more", "", 0, true); break; }
            Row(f.what, "", -f.points, true);
            shownFindings++;
        }
        Row("Stress", r.stress + "%", -r.stressPenalty);

        // Allowance, paid into the wallet (Bank page in the PC browser).
        y -= 4f;
        var rule = UIFactory.Panel("Rule", card, new Color(0.13f, 0.13f, 0.16f, 0.25f));
        UIFactory.Place(rule.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), new Vector2(0, y + 8f), new Vector2(560, 2));
        Line(card, "Allowance", 21, Ink, new Vector2(48, y - 12f), new Vector2(300, 30), TextAlignmentOptions.MidlineLeft, anchorLeft: true).fontStyle = FontStyles.Bold;
        Line(card, r.allowance > 0 ? "+$" + r.allowance : "$0  (grounded)", 22, r.allowance > 0 ? Green : Red, new Vector2(-48, y - 12f), new Vector2(240, 30),
            TextAlignmentOptions.MidlineRight, anchorRight: true).fontStyle = FontStyles.Bold;

        var quote = Line(card, $"\"{r.momSays}\"", 24, Ink, new Vector2(0, 92), new Vector2(560, 64), TextAlignmentOptions.Center, anchorBottom: true);
        quote.fontStyle = FontStyles.Italic;
        quote.enableWordWrapping = true;
        Line(card, "- Mom", 17, new Color(0.13f, 0.13f, 0.16f, 0.6f), new Vector2(200, 58), new Vector2(160, 24), TextAlignmentOptions.Right, anchorBottom: true);
        Line(card, Campaign.IsFriday ? $"{Campaign.RankName(Campaign.CurrentRank)}  {Campaign.Rating} RP   -   Press  R  for the week summary"
            : $"{Campaign.RankName(Campaign.CurrentRank)}  {Campaign.Rating} RP   -   Press  R  for {Campaign.DayNames[Mathf.Min(Campaign.Friday, Campaign.DayIndex + 1)]}", 17, new Color(0.13f, 0.13f, 0.16f, 0.55f), new Vector2(0, 26), new Vector2(560, 24), TextAlignmentOptions.Center, anchorBottom: true);

        // Drop in, then stamp the grade.
        for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / 0.35f)
        {
            float e = 1f - (1f - t) * (1f - t);
            dim.color = new Color(0f, 0f, 0f, 0.55f * e);
            group.alpha = e;
            float s = Mathf.Lerp(0.9f, 1f, e);
            card.localScale = new Vector3(s, s, 1f);
            yield return null;
        }
        group.alpha = 1f;
        card.localScale = Vector3.one;
        yield return new WaitForSecondsRealtime(0.5f);
        circle.gameObject.SetActive(true);
        for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / 0.18f)
        {
            float s = Mathf.Lerp(2.2f, 1f, t * t);
            circle.rectTransform.localScale = new Vector3(s, s, 1f);
            yield return null;
        }
        circle.rectTransform.localScale = Vector3.one;
    }

    TextMeshProUGUI Line(Transform parent, string text, float size, Color color, Vector2 at, Vector2 box, TextAlignmentOptions align,
        bool anchorLeft = false, bool anchorRight = false, bool anchorBottom = false)
    {
        var t = UIFactory.Text("Text", parent, size, color, align, font);
        t.text = text;
        t.enableWordWrapping = false;
        Vector2 anchor = anchorBottom ? new Vector2(0.5f, 0f) : anchorLeft ? new Vector2(0f, 1f) : anchorRight ? new Vector2(1f, 1f) : new Vector2(0.5f, 1f);
        Vector2 pivot = anchorLeft ? new Vector2(0f, 0.5f) : anchorRight && align != TextAlignmentOptions.Center ? new Vector2(1f, 0.5f) : new Vector2(0.5f, 0.5f);
        UIFactory.Place(t.rectTransform, anchor, pivot, at, box);
        return t;
    }

    /// <summary>A soft white disc; stretched (sliced) it makes rounded bars and panels.</summary>
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
        round = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(15, 15, 15, 15));
        return round;
    }
}
