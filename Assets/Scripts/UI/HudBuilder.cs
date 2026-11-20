using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Builds the hand-drawn HUD objects under a GameHUD. Used by the editor setup tool (so the HUD
/// is saved in the scene and can be edited) and as a fallback when the game starts without one.
/// </summary>
public static class HudBuilder
{
    static TMP_FontAsset font;

    public static void Build(GameHUD hud)
    {
        font = hud.font;

        var canvas = UIFactory.CreateCanvas("HUD Canvas", 50);
        canvas.transform.SetParent(hud.transform, false);
        var root = canvas.transform;

        BuildHeader(root);
        hud.notesRoot = Node("Notes", root, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-34, -116), new Vector2(360, 820));
        hud.emptyHint = BuildEmptyHint(hud.notesRoot);
        hud.noteTemplate = BuildNoteTemplate(hud.notesRoot);
        BuildVitals(root);
        hud.prompt = BuildPrompt(root);
        hud.toast = BuildToast(root);
        BuildHeldTag(root);
    }

    // ------------------------------------------------------------------ pieces

    static void BuildHeader(Transform root)
    {
        var header = Node("To Do", root, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-56, -22), new Vector2(240, 78));
        header.localRotation = Quaternion.Euler(0, 0, -3f);
        Breathe(header, 1.5f, 0.02f);

        Stretch(Art("Paper", header, "scrap", HudArt.PaperWhite));
        var title = Label("Title", header, "TO DO", 46, HudArt.Ink, TextAlignmentOptions.Center);
        Stretch(title.rectTransform, 0, 0, 4, 10);
        Art("Underline", header, "underline", HudArt.MarkerRed, new Vector2(0.5f, 0), new Vector2(0.5f, 0.5f), new Vector2(0, 18), new Vector2(140, 16), boil: true);
    }

    static CanvasGroup BuildEmptyHint(RectTransform notes)
    {
        var hint = Node("All Clear", notes, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -4), new Vector2(300, 62));
        hint.localRotation = Quaternion.Euler(0, 0, 2f);
        Breathe(hint, 1.2f, 0.015f);
        var group = hint.gameObject.AddComponent<CanvasGroup>();
        group.alpha = 0f;

        Stretch(Art("Paper", hint, "scrap", HudArt.PaperWhite));
        Stretch(Label("Text", hint, "ALL CLEAR... FOR NOW", 24, HudArt.InkSoft, TextAlignmentOptions.Center).rectTransform, 0, 0, 2, 6);
        return group;
    }

    static TaskNote BuildNoteTemplate(RectTransform notes)
    {
        var note = Node("Note Template", notes, new Vector2(0.5f, 1), new Vector2(0.5f, 1), Vector2.zero, new Vector2(340, 130));
        var group = note.gameObject.AddComponent<CanvasGroup>();
        var view = note.gameObject.AddComponent<TaskNote>();

        // Body hangs from the tape (pivot at the top) so it sways like a real note.
        var body = Node("Body", note, new Vector2(0.5f, 1), new Vector2(0.5f, 1), Vector2.zero, new Vector2(340, 130));

        Art("Shadow", body, "note_shadow", new Color(1, 1, 1, 0.9f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(7, -9), new Vector2(360, 150));
        var paper = Art("Paper", body, "note_paper", HudArt.NoteYellow);
        Stretch(paper.rectTransform);

        var title = Label("Title", body, "TASK", 30, HudArt.Ink, TextAlignmentOptions.TopLeft);
        Place(title.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(24, -20), new Vector2(232, 40));
        SingleLine(title);

        var strike = Art("Strike", body, "strike", HudArt.MarkerRed, new Vector2(0, 1), new Vector2(0, 0.5f), new Vector2(16, -40), new Vector2(246, 20));
        Filled(strike, Image.FillMethod.Horizontal, (int)Image.OriginHorizontal.Left);

        var timer = Label("Timer", body, "", 30, HudArt.Ink, TextAlignmentOptions.Center);
        Place(timer.rectTransform, new Vector2(1, 1), new Vector2(0.5f, 0.5f), new Vector2(-50, -40), new Vector2(96, 40));
        SingleLine(timer);

        var circle = Art("Circle", body, "circle", HudArt.MarkerRed, new Vector2(1, 1), new Vector2(0.5f, 0.5f), new Vector2(-50, -40), new Vector2(100, 64), boil: true);
        Filled(circle, Image.FillMethod.Radial360, (int)Image.Origin360.Top);

        var status = Label("Status", body, "", 19, HudArt.InkSoft, TextAlignmentOptions.TopLeft);
        Place(status.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(24, -64), new Vector2(296, 28));
        SingleLine(status);

        var bar = Art("Bar", body, "bar_outline", Color.white, new Vector2(0, 0), new Vector2(0, 0), new Vector2(22, 16), new Vector2(296, 24), boil: true);
        var fill = Art("Fill", bar.transform, "bar_fill", HudArt.InkSoft);
        Stretch(fill.rectTransform);
        fill.transform.SetAsFirstSibling();
        Filled(fill, Image.FillMethod.Horizontal, (int)Image.OriginHorizontal.Left);

        var check = Art("Check", body, "check", HudArt.MarkerGreen, new Vector2(1, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-46, 2), new Vector2(68, 68), boil: true);
        check.gameObject.SetActive(false);

        var tape = Art("Tape", body, "tape", Color.white, new Vector2(0.5f, 1), new Vector2(0.5f, 0.5f), new Vector2(0, -2), new Vector2(112, 32));

        view.body = body;
        view.group = group;
        view.paper = paper;
        view.barFill = fill;
        view.strike = strike;
        view.check = check;
        view.circle = circle;
        view.title = title;
        view.status = status;
        view.timer = timer;
        view.tape = tape.rectTransform;

        note.gameObject.SetActive(false);
        return view;
    }

    static void BuildVitals(Transform root)
    {
        var card = Node("Vitals", root, new Vector2(0, 0), new Vector2(0, 0), new Vector2(36, 36), new Vector2(330, 160));
        card.localRotation = Quaternion.Euler(0, 0, 2f);
        Breathe(card, 0.7f, 0.008f);

        Art("Shadow", card, "note_shadow", new Color(1, 1, 1, 0.9f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(7, -9), new Vector2(350, 182));
        Stretch(Art("Paper", card, "note_paper", HudArt.PaperWhite).rectTransform);
        var tape = Art("Tape", card, "tape", Color.white, new Vector2(0, 1), new Vector2(0.5f, 0.5f), new Vector2(26, -8), new Vector2(100, 30));
        tape.rectTransform.localRotation = Quaternion.Euler(0, 0, 32f);

        Vital(card, VitalMeter.Kind.Stress, "STRESS", "heart", new Vector2(104, 94), true,
            new Color32(255, 157, 176, 255), new Color32(215, 25, 47, 255));
        Vital(card, VitalMeter.Kind.Exhaustion, "TIRED", "drop", new Vector2(68, 94), false,
            new Color32(159, 212, 255, 255), new Color32(47, 95, 214, 255));
    }

    static void Vital(Transform card, VitalMeter.Kind kind, string labelText, string art, Vector2 iconSize, bool left, Color calm, Color max)
    {
        var area = Node(labelText, card, new Vector2(left ? 0 : 1, 0), new Vector2(left ? 0 : 1, 0), Vector2.zero, new Vector2(165, 160));
        var meter = area.gameObject.AddComponent<VitalMeter>();

        var icon = Node("Icon", area, new Vector2(0.5f, 1), new Vector2(0.5f, 0.5f), new Vector2(0, -64), iconSize);
        var fill = Art("Fill", icon, art + "_fill", calm);
        Stretch(fill.rectTransform);
        Filled(fill, Image.FillMethod.Vertical, (int)Image.OriginVertical.Bottom);
        Stretch(Art("Outline", icon, art + "_outline", Color.white, boil: true).rectTransform);

        var value = Label("Value", icon, "0%", 24, HudArt.Ink, TextAlignmentOptions.Center);
        Place(value.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, kind == VitalMeter.Kind.Stress ? 4 : -12), new Vector2(90, 34));
        SingleLine(value);

        var label = Label("Label", area, labelText, 24, HudArt.Ink, TextAlignmentOptions.Center);
        Place(label.rectTransform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 10), new Vector2(160, 32));
        SingleLine(label);

        meter.kind = kind;
        meter.icon = icon;
        meter.fill = fill;
        meter.value = value;
        meter.label = label;
        meter.calmColor = calm;
        meter.maxColor = max;
    }

    static PromptView BuildPrompt(Transform root)
    {
        var prompt = Node("Prompt", root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -90), new Vector2(10, 10));
        var group = prompt.gameObject.AddComponent<CanvasGroup>();
        group.alpha = 0f;
        var view = prompt.gameObject.AddComponent<PromptView>();

        var body = Node("Body", prompt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(10, 10));
        var paper = Art("Paper", body, "scrap", HudArt.PaperWhite, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(360, 72));

        var key = Node("Key", paper.transform, new Vector2(0, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(48, 1), new Vector2(54, 54));
        Stretch(Art("Cap", key, "keycap", Color.white, boil: true).rectTransform);
        var letter = Label("Letter", key, "E", 30, HudArt.Ink, TextAlignmentOptions.Center);
        Stretch(letter.rectTransform, 0, 0, 0, 10);

        var text = Label("Text", paper.transform, "", 28, HudArt.Ink, TextAlignmentOptions.Left);
        Place(text.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(86, 2), new Vector2(800, 44));
        SingleLine(text);

        view.body = body;
        view.paper = paper.rectTransform;
        view.key = key;
        view.text = text;
        view.group = group;
        return view;
    }

    static ToastView BuildToast(Transform root)
    {
        var toast = Node("Message", root, new Vector2(0.5f, 1), new Vector2(0.5f, 0.5f), new Vector2(0, -160), new Vector2(10, 10));
        var group = toast.gameObject.AddComponent<CanvasGroup>();
        group.alpha = 0f;
        var view = toast.gameObject.AddComponent<ToastView>();

        var body = Node("Body", toast, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(10, 10));
        var paper = Art("Paper", body, "scrap", HudArt.PaperWhite, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(500, 80));
        var text = Label("Text", body, "", 32, HudArt.Ink, TextAlignmentOptions.Center);
        Place(text.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 3), new Vector2(1150, 50));
        SingleLine(text);

        view.body = body;
        view.paper = paper.rectTransform;
        view.text = text;
        view.group = group;
        return view;
    }

    static void BuildHeldTag(Transform root)
    {
        var holder = Node("Held Item", root, new Vector2(1, 0), new Vector2(0.5f, 0.5f), new Vector2(-300, 96), new Vector2(10, 10));
        var group = holder.gameObject.AddComponent<CanvasGroup>();
        group.alpha = 0f;
        var view = holder.gameObject.AddComponent<HeldTag>();

        // Pivot on the tag's hole so it swings from its string.
        var body = Node("Tag", holder, new Vector2(0.5f, 0.5f), new Vector2(35f / 260f, 0.5f), Vector2.zero, new Vector2(260, 65));
        Stretch(Art("Paper", body, "tag", HudArt.Kraft).rectTransform);

        var item = Label("Item", body, "", 28, HudArt.Ink, TextAlignmentOptions.Left);
        Place(item.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(62, 9), new Vector2(186, 34));
        SingleLine(item);
        var hint = Label("Hint", body, "", 18, HudArt.InkSoft, TextAlignmentOptions.Left);
        Place(hint.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(62, -17), new Vector2(186, 24));
        SingleLine(hint);

        view.body = body;
        view.item = item;
        view.hint = hint;
        view.group = group;
    }

    // ------------------------------------------------------------------ helpers

    static RectTransform Node(string name, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size)
    {
        var rt = UIFactory.Rect(name, parent);
        Place(rt, anchor, pivot, position, size);
        return rt;
    }

    static void Place(RectTransform rt, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size) =>
        UIFactory.Place(rt, anchor, pivot, position, size);

    static void Stretch(RectTransform rt, float left = 0, float right = 0, float top = 0, float bottom = 0) =>
        UIFactory.Fill(rt, left, right, top, bottom);

    static Image Stretch(Image image)
    {
        Stretch(image.rectTransform);
        return image;
    }

    static Image Art(string name, Transform parent, string art, Color color, bool boil = false)
    {
        var image = UIFactory.Rect(name, parent).gameObject.AddComponent<Image>();
        image.raycastTarget = false;
        image.color = color;
        if (boil)
        {
            var frames = HudArt.Frames(art);
            if (frames.Length > 0) image.sprite = frames[0];
            if (frames.Length > 1) image.gameObject.AddComponent<LineBoil>().frames = frames;
        }
        else
        {
            image.sprite = HudArt.Get(art);
        }
        return image;
    }

    static Image Art(string name, Transform parent, string art, Color color, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size, bool boil = false)
    {
        var image = Art(name, parent, art, color, boil);
        Place(image.rectTransform, anchor, pivot, position, size);
        return image;
    }

    static void Filled(Image image, Image.FillMethod method, int origin)
    {
        image.type = Image.Type.Filled;
        image.fillMethod = method;
        image.fillOrigin = origin;
        image.fillClockwise = true;
        image.fillAmount = 0f;
    }

    static TextMeshProUGUI Label(string name, Transform parent, string text, float size, Color color, TextAlignmentOptions align)
    {
        var label = UIFactory.Text(name, parent, size, color, align, font);
        label.text = text;
        return label;
    }

    static void SingleLine(TextMeshProUGUI text)
    {
        text.enableWordWrapping = false;
        text.overflowMode = TextOverflowModes.Ellipsis;
    }

    static void Breathe(RectTransform rt, float sway, float breathe)
    {
        var b = rt.gameObject.AddComponent<HudBreathe>();
        b.swayDegrees = sway;
        b.breatheAmount = breathe;
    }
}
