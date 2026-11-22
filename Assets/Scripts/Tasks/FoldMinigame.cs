using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Folding the clean laundry at the wardrobe: a little rhythm game on a sheet of paper.
/// 4 real garments (t-shirt, shirt, hoodie, jeans), 3 folds each, done the way you really fold them: one side in, the other side in,
/// then bottom up (jeans: leg over leg, then up, then down). A dashed line + arrow on the garment shows the next fold, and its key
/// (W A S D or the arrows: the direction the cloth moves) waits on the right with a ring closing in. Press it as the ring meets the
/// key: on time = a crisp fold, wrong key / too early / too late = a wrinkle. Each garment goes a bit faster.
/// You can't move while folding (the match and the house keep going!). Esc stops, and you keep the clothes.
/// Result 0..1 (the share of crisp folds) goes to the laundry task and Mom's report card.
/// </summary>
public class FoldMinigame : MonoBehaviour, IMainTask
{
    const int Garments = 4, FoldsEach = 3;
    const float Window = 0.2f;        // seconds either side of the beat that count as on time
    const float Ext = 0.55f;          // garment coordinates shown: -Ext..Ext
    const float GarmentPx = 330f;     // on-screen size of the garment drawing
    const int TexSize = 256;

    struct Fold
    {
        public bool vertical;  // true: a line at x = at (cloth moves sideways); false: a line at z = at (cloth moves up/down)
        public float at;
        public int side;       // which side of the line folds over: +1 = the x > at (or z > at) part
        public float band;     // side folds of tops: the sleeve tucks in, nothing folded lands outside |x| <= band (0 = no limit)
        public KeyCode Key => vertical ? (side > 0 ? KeyCode.A : KeyCode.D) : (side > 0 ? KeyCode.S : KeyCode.W);
    }

    static FoldMinigame instance;
    System.Action<float> onDone;
    System.Action onCancel;

    Canvas canvas;
    RectTransform sheet, garmentRt, keyRt, ringRt, hintLine, hintArrow, pileRoot;
    Image garmentImage, wrinkle, ringImage, checkImage, hintLineImage;
    TextMeshProUGUI keyText, judge, counter, combo;
    TMP_FontAsset font;

    readonly List<(string kind, Color colour)> basket = new List<(string, Color)>();
    Fold[] folds;
    int garmentIndex, foldIndex, crisp, total, streak;
    float beat, beatLength, judgeTime, foldAnim = -1f, shake, keyPunch, checkTime;
    bool running, waitingNext, lastGood;
    float nextAt;
    Sprite pendingSprite;
    readonly List<GameObject> pile = new List<GameObject>();

    static readonly Dictionary<string, Sprite> sprites = new Dictionary<string, Sprite>();
    static Sprite ringSprite, dashSprite;

    /// <summary>Starts folding (locks the player while it runs). done(quality 0..1) or cancelled().</summary>
    public static bool Begin(System.Action<float> done, System.Action cancelled)
    {
        if (!PlayerInteraction.Instance) { done?.Invoke(1f); return true; }
        if (!instance) instance = new GameObject("Fold minigame").AddComponent<FoldMinigame>();
        instance.onDone = done;
        instance.onCancel = cancelled;
        return PlayerInteraction.Instance.BeginMainTask(instance);
    }

    public bool ShouldBlockInput() => true;

    public void StartMainTask()
    {
        if (!canvas) Build();
        canvas.enabled = true;
        garmentIndex = 0; crisp = 0; total = 0; streak = 0;
        foreach (var p in pile) Destroy(p);
        pile.Clear();
        basket.Clear();
        var kinds = new List<string> { "t-shirt", "shirt", "hoodie", "trousers" };
        for (int i = 0; i < kinds.Count; i++) { int j = Random.Range(i, kinds.Count); (kinds[i], kinds[j]) = (kinds[j], kinds[i]); }
        foreach (var k in kinds) basket.Add((k, DirtyClothesPiece.RandomColour(k)));
        running = true;
        NewGarment();
    }

    public void StopMainTask()
    {
        if (!running) return;
        running = false;
        canvas.enabled = false;
        onCancel?.Invoke();
    }

    void Finish()
    {
        running = false;
        canvas.enabled = false;
        float q = total > 0 ? crisp / (float)total : 1f;
        if (PlayerInteraction.Instance) PlayerInteraction.Instance.ReleaseMainTask(this);
        onDone?.Invoke(q);
    }

    // ------------------------------------------------------------------ the folds of each garment

    static Fold[] FoldsFor(string kind)
    {
        int first = Random.value < 0.5f ? 1 : -1;
        if (kind == "trousers")
            return new[]
            {
                new Fold { vertical = true, at = 0f, side = first },          // leg over leg
                new Fold { vertical = false, at = -0.06f, side = -1 },        // bottom up
                new Fold { vertical = false, at = 0.16f, side = 1 },          // top down
            };
        float body = kind == "hoodie" ? 0.25f : kind == "shirt" ? 0.22f : 0.23f;
        float line = body * 0.5f;
        return new[]
        {
            new Fold { vertical = true, at = first * line, side = first, band = line },    // one side (sleeve tucked in)
            new Fold { vertical = true, at = -first * line, side = -first, band = line },  // the other side
            new Fold { vertical = false, at = -0.02f, side = -1 },            // bottom up
        };
    }

    void NewGarment()
    {
        foldIndex = 0;
        var (kind, colour) = basket[garmentIndex];
        folds = FoldsFor(kind);
        garmentImage.sprite = GarmentSprite(kind, folds, 0);
        garmentImage.color = colour;
        garmentRt.localRotation = Quaternion.identity;
        garmentRt.localScale = Vector3.one;
        wrinkle.enabled = false;
        beatLength = Mathf.Lerp(1.3f, 0.85f, garmentIndex / (float)(Garments - 1));
        counter.text = $"{garmentIndex + 1} / {Garments}";
        NextFold();
    }

    void NextFold()
    {
        var f = folds[foldIndex];
        keyText.text = f.Key.ToString();
        beat = 0f;
        waitingNext = false;
        keyRt.gameObject.SetActive(true);

        // The hint on the garment: where the fold line is, and which way the cloth goes.
        float px = f.at / Ext * GarmentPx * 0.5f;
        hintLine.gameObject.SetActive(true);
        hintArrow.gameObject.SetActive(true);
        if (f.vertical)
        {
            hintLine.anchoredPosition = new Vector2(px, 0f);
            hintLine.sizeDelta = new Vector2(8f, GarmentPx * 0.9f);
            hintLine.localRotation = Quaternion.identity;
            hintArrow.anchoredPosition = new Vector2(px + f.side * 46f, 0f);
            hintArrow.localRotation = Quaternion.Euler(0, 0, f.side > 0 ? 90f : -90f); // the arrow sprite points up
        }
        else
        {
            hintLine.anchoredPosition = new Vector2(0f, px);
            hintLine.sizeDelta = new Vector2(8f, GarmentPx * 0.9f);
            hintLine.localRotation = Quaternion.Euler(0, 0, 90f);
            hintArrow.anchoredPosition = new Vector2(0f, px + f.side * 46f);
            hintArrow.localRotation = Quaternion.Euler(0, 0, f.side > 0 ? 180f : 0f);
        }
    }

    // ------------------------------------------------------------------ every frame

    void Update()
    {
        if (!running || PauseMenu.IsPaused) return;
        float dt = Time.unscaledDeltaTime;

        if (waitingNext)
        {
            if (Time.unscaledTime >= nextAt && foldAnim < 0f)
            {
                if (foldIndex >= FoldsEach)
                {
                    AddToPile();
                    garmentIndex++;
                    if (garmentIndex >= Garments) { Finish(); return; }
                    NewGarment();
                }
                else NextFold();
            }
        }
        else
        {
            beat += dt;
            // The ring closes in on the key and meets it on the beat.
            float s = Mathf.Max(0.55f, 1f + (beatLength - beat) / beatLength * 0.9f);
            ringRt.localScale = Vector3.one * s;
            float off = Mathf.Abs(beat - beatLength);
            ringImage.color = off <= Window ? HudArt.MarkerGreen : new Color(HudArt.Ink.r, HudArt.Ink.g, HudArt.Ink.b, Mathf.Clamp01(beat / 0.25f));
            float pulse = 0.55f + 0.45f * Mathf.Sin(Time.unscaledTime * 9f);
            hintLineImage.color = new Color(HudArt.MarkerOrange.r, HudArt.MarkerOrange.g, HudArt.MarkerOrange.b, pulse);

            KeyCode pressed = Pressed();
            if (pressed != KeyCode.None) Judge(pressed == folds[foldIndex].Key && off <= Window);
            else if (beat > beatLength + Window) Judge(false);
        }

        Animate(dt);
    }

    static KeyCode Pressed()
    {
        if (Input.GetKeyDown(KeyCode.W) || Input.GetKeyDown(KeyCode.UpArrow)) return KeyCode.W;
        if (Input.GetKeyDown(KeyCode.A) || Input.GetKeyDown(KeyCode.LeftArrow)) return KeyCode.A;
        if (Input.GetKeyDown(KeyCode.S) || Input.GetKeyDown(KeyCode.DownArrow)) return KeyCode.S;
        if (Input.GetKeyDown(KeyCode.D) || Input.GetKeyDown(KeyCode.RightArrow)) return KeyCode.D;
        return KeyCode.None;
    }

    void Judge(bool good)
    {
        total++;
        lastGood = good;
        if (good) { crisp++; streak++; } else streak = 0;
        judge.text = good ? (Mathf.Abs(beat - beatLength) < Window * 0.4f ? "PERFECT!" : "CRISP!") : "WRINKLED";
        judge.color = good ? HudArt.MarkerGreen : HudArt.MarkerRed;
        judgeTime = 0.9f;
        combo.text = streak >= 3 ? $"x{streak} in a row!" : "";
        var at = Camera.main ? Camera.main.transform.position : Vector3.zero;
        Fx.Play(good ? Fx.Swish : Fx.Thud, at, 0.55f, good ? 1f + 0.06f * Mathf.Min(streak, 6) : 0.75f);
        if (good) { keyPunch = 1f; checkTime = 0.6f; }
        else shake = 0.35f;

        // The fold itself: squash along the fold, swap to the folded drawing half-way, spring back.
        foldIndex++;
        var (kind, _) = basket[garmentIndex];
        pendingSprite = GarmentSprite(kind, folds, foldIndex);
        foldAnim = 0f;
        if (!good)
        {
            wrinkle.enabled = true;
            wrinkle.rectTransform.localRotation = Quaternion.Euler(0, 0, Random.Range(-25f, 25f));
        }
        waitingNext = true;
        nextAt = Time.unscaledTime + 0.38f;
        keyRt.gameObject.SetActive(false);
        hintLine.gameObject.SetActive(false);
        hintArrow.gameObject.SetActive(false);
    }

    void Animate(float dt)
    {
        // Fold squash
        if (foldAnim >= 0f)
        {
            foldAnim += dt / 0.28f;
            var f = folds[Mathf.Clamp(foldIndex - 1, 0, FoldsEach - 1)];
            float squash = foldAnim < 0.5f ? Mathf.Lerp(1f, 0.15f, foldAnim * 2f) : Mathf.Lerp(0.15f, 1f, (foldAnim - 0.5f) * 2f);
            if (foldAnim >= 0.5f && pendingSprite) { garmentImage.sprite = pendingSprite; pendingSprite = null; }
            garmentRt.localScale = f.vertical ? new Vector3(squash, 1f, 1f) : new Vector3(1f, squash, 1f);
            if (foldAnim >= 1f)
            {
                foldAnim = -1f;
                garmentRt.localScale = Vector3.one;
                garmentRt.localRotation = Quaternion.Euler(0, 0, lastGood ? 0f : Random.Range(-7f, 7f));
            }
        }

        // Shake the sheet on a miss
        shake = Mathf.MoveTowards(shake, 0f, dt);
        sheet.anchoredPosition = shake > 0f ? new Vector2(Mathf.Sin(Time.unscaledTime * 70f) * 10f * shake, 0f) : Vector2.zero;

        // Key punch + tick
        keyPunch = Mathf.MoveTowards(keyPunch, 0f, dt * 4f);
        keyRt.localScale = Vector3.one * (1f + 0.18f * keyPunch);
        checkTime = Mathf.MoveTowards(checkTime, 0f, dt);
        checkImage.enabled = checkTime > 0f;
        checkImage.rectTransform.localScale = Vector3.one * (0.6f + 0.6f * (1f - checkTime / 0.6f));
        checkImage.color = new Color(HudArt.MarkerGreen.r, HudArt.MarkerGreen.g, HudArt.MarkerGreen.b, Mathf.Clamp01(checkTime * 3f));

        judgeTime -= dt;
        judge.alpha = Mathf.Clamp01(judgeTime * 2.5f);
        judge.rectTransform.localScale = Vector3.one * (1f + 0.3f * Mathf.Clamp01(judgeTime - 0.6f) / 0.3f);
    }

    void AddToPile()
    {
        var (kind, colour) = basket[garmentIndex];
        var img = UIFactory.Panel("Folded " + kind, pileRoot, colour);
        img.sprite = GarmentSprite(kind, folds, FoldsEach);
        img.preserveAspect = true;
        UIFactory.Place(img.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0.5f), new Vector2(Random.Range(-6f, 6f), 20f + pile.Count * 20f), new Vector2(260, 260));
        img.rectTransform.localRotation = Quaternion.Euler(0, 0, garmentRt.localEulerAngles.z);
        pile.Add(img.gameObject);
    }

    // ------------------------------------------------------------------ build

    void Build()
    {
        font = GameHUD.Instance ? GameHUD.Instance.font : null;
        canvas = UIFactory.CreateCanvas("Fold minigame", 62);
        canvas.transform.SetParent(transform, false);

        var dim = UIFactory.Panel("Dim", canvas.transform, new Color(0f, 0f, 0f, 0.4f));
        UIFactory.Fill(dim.rectTransform);

        var holder = UIFactory.Rect("Holder", canvas.transform);
        UIFactory.Place(holder, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(800, 540));
        holder.localRotation = Quaternion.Euler(0, 0, -0.8f);
        sheet = UIFactory.Rect("Sheet", holder);
        UIFactory.Fill(sheet);
        var shadow = UIFactory.Panel("Shadow", sheet, Color.white);
        shadow.sprite = HudArt.Get("note_shadow");
        UIFactory.Place(shadow.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(9, -11), new Vector2(830, 570));
        var paper = UIFactory.Panel("Paper", sheet, HudArt.PaperWhite);
        paper.sprite = HudArt.Get("note_paper");
        UIFactory.Fill(paper.rectTransform);
        for (int i = 0; i < 2; i++)
        {
            var tape = UIFactory.Panel("Tape", sheet, Color.white);
            tape.sprite = HudArt.Get("tape");
            UIFactory.Place(tape.rectTransform, new Vector2(i, 1f), new Vector2(0.5f, 0.5f), new Vector2(i == 0 ? 40 : -40, -10), new Vector2(120, 32));
            tape.rectTransform.localRotation = Quaternion.Euler(0, 0, i == 0 ? 30f : -30f);
        }

        var title = Text("Title", 40, HudArt.Ink, new Vector2(-150, -48), new Vector2(440, 50));
        title.text = "<b>FOLD THE LAUNDRY</b>";
        var underline = UIFactory.Panel("Underline", sheet, HudArt.MarkerRed);
        underline.sprite = HudArt.Get("underline_0");
        UIFactory.Place(underline.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), new Vector2(-150, -78), new Vector2(300, 16));
        counter = Text("Counter", 22, HudArt.InkSoft, new Vector2(250, -48), new Vector2(160, 30));
        var help = Text("Help", 16, HudArt.InkSoft, new Vector2(0, -520), new Vector2(720, 22));
        help.text = "Follow the dashed line  -  press the key as the ring meets it  -  Esc to stop";

        // The garment (left)
        garmentRt = UIFactory.Rect("Garment", sheet);
        UIFactory.Place(garmentRt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-150, -10), new Vector2(GarmentPx, GarmentPx));
        garmentImage = garmentRt.gameObject.AddComponent<Image>();
        garmentImage.raycastTarget = false;
        wrinkle = UIFactory.Panel("Wrinkles", garmentRt, new Color(HudArt.Ink.r, HudArt.Ink.g, HudArt.Ink.b, 0.55f));
        wrinkle.sprite = HudArt.Get("strike");
        UIFactory.Place(wrinkle.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(150, 22));

        hintLineImage = UIFactory.Panel("Fold line", garmentRt, HudArt.MarkerOrange);
        hintLineImage.sprite = Dash();
        hintLineImage.type = Image.Type.Tiled;
        hintLine = hintLineImage.rectTransform;
        UIFactory.Place(hintLine, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(8, 300));
        var arrow = UIFactory.Panel("Fold arrow", garmentRt, HudArt.MarkerOrange);
        arrow.sprite = HudArt.Get("arrow");
        hintArrow = arrow.rectTransform;
        UIFactory.Place(hintArrow, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(54, 54));

        // The key and its ring (right, top)
        keyRt = UIFactory.Rect("Key", sheet);
        UIFactory.Place(keyRt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(240, 100), new Vector2(96, 96));
        var cap = UIFactory.Panel("Keycap", keyRt, Color.white);
        cap.sprite = HudArt.Get("keycap_0");
        UIFactory.Fill(cap.rectTransform);
        keyText = UIFactory.Text("Letter", keyRt, 46, HudArt.Ink, TextAlignmentOptions.Center, font);
        keyText.fontStyle = FontStyles.Bold;
        UIFactory.Fill(keyText.rectTransform, 0, 0, 0, 10);
        ringImage = UIFactory.Panel("Ring", keyRt, HudArt.Ink);
        ringImage.sprite = Ring();
        ringRt = ringImage.rectTransform;
        UIFactory.Place(ringRt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(124, 124));

        var check = UIFactory.Panel("Tick", sheet, HudArt.MarkerGreen);
        check.sprite = HudArt.Get("check_0");
        checkImage = check;
        UIFactory.Place(check.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(312, 150), new Vector2(70, 70));
        check.enabled = false;

        judge = Text("Judge", 34, HudArt.MarkerGreen, new Vector2(240, -270), new Vector2(260, 44));
        judge.fontStyle = FontStyles.Bold;
        judge.alpha = 0f;
        combo = Text("Combo", 18, HudArt.MarkerOrange, new Vector2(240, -304), new Vector2(260, 26));

        // The folded pile (right, bottom)
        pileRoot = UIFactory.Rect("Pile", sheet);
        UIFactory.Place(pileRoot, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(240, 72), new Vector2(200, 160));
        var pileLabel = Text("Pile label", 16, HudArt.InkSoft, new Vector2(240, -490), new Vector2(200, 22));
        pileLabel.text = "folded";
        canvas.enabled = false;
    }

    TextMeshProUGUI Text(string name, float size, Color c, Vector2 at, Vector2 box)
    {
        var t = UIFactory.Text(name, sheet, size, c, TextAlignmentOptions.Center, font);
        t.enableWordWrapping = false;
        t.overflowMode = TextOverflowModes.Ellipsis;
        t.enableAutoSizing = true;
        t.fontSizeMax = size;
        t.fontSizeMin = size * 0.6f;
        UIFactory.Place(t.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), at, box);
        return t;
    }

    // ------------------------------------------------------------------ drawings (made in code, cached)

    /// <summary>The garment after 'done' folds: a hand-inked drawing (white fabric, tinted by the Image colour).</summary>
    static Sprite GarmentSprite(string kind, Fold[] folds, int done)
    {
        string key = kind + "|" + done + "|" + string.Join(",", System.Array.ConvertAll(folds, f => $"{f.vertical}{f.at:0.000}{f.side}"));
        if (sprites.TryGetValue(key, out var s) && s) return s;

        int n = TexSize;
        var region = new int[n * n];
        var flips = new int[n * n];
        for (int y = 0; y < n; y++)
        for (int x = 0; x < n; x++)
        {
            float gx = -Ext + (x + 0.5f) / n * Ext * 2f;
            float gz = -Ext + (y + 0.5f) / n * Ext * 2f;
            // A little wobble, so edges look drawn by hand.
            gx += (Mathf.PerlinNoise(x * 0.05f, y * 0.05f) - 0.5f) * 0.008f;
            gz += (Mathf.PerlinNoise(x * 0.05f + 40f, y * 0.05f) - 0.5f) * 0.008f;
            region[y * n + x] = Top(kind, folds, done - 1, gx, gz, out int f);
            flips[y * n + x] = f;
        }

        var px = new Color32[n * n];
        Color32 ink = new Color32(30, 28, 38, 255), clear = new Color32(255, 255, 255, 0);
        for (int y = 0; y < n; y++)
        for (int x = 0; x < n; x++)
        {
            int i = y * n + x;
            int r = region[i];
            if (r == 0) { px[i] = clear; continue; }
            bool edge = false, crease = false;
            for (int dy = -2; dy <= 2 && !edge; dy++)
            for (int dx = -2; dx <= 2 && !edge; dx++)
            {
                int xx = x + dx, yy = y + dy;
                if (xx < 0 || yy < 0 || xx >= n || yy >= n || region[yy * n + xx] == 0) edge = true;
                else if (Mathf.Abs(dx) <= 1 && Mathf.Abs(dy) <= 1 && flips[yy * n + xx] != flips[i]) crease = true;
            }
            if (edge || crease) { px[i] = ink; continue; }
            float shade = (r == 2 ? 0.66f : 1f) * (flips[i] % 2 == 1 ? 0.84f : 1f);
            shade *= 0.96f + 0.04f * Mathf.PerlinNoise(x * 0.3f, y * 0.3f); // a bit of cloth texture
            byte b = (byte)(255f * shade);
            px[i] = new Color32(b, b, b, 255);
        }
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = key };
        tex.SetPixels32(px);
        tex.Apply();
        s = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f);
        sprites[key] = s;
        return s;
    }

    /// <summary>What you see at (x, z) after folds 0..i: the region of the top layer (0 = nothing) and how many times it was flipped.</summary>
    static int Top(string kind, Fold[] folds, int i, float x, float z, out int flips)
    {
        flips = 0;
        if (i < 0) return DirtyClothesPiece.ShapeAt(kind, x, z);
        var f = folds[i];
        float c = f.vertical ? x : z;
        if ((c - f.at) * f.side > 0f) return 0; // that part was folded away
        // The folded-over part lies on top: look there first.
        float rx = f.vertical ? 2f * f.at - x : x, rz = f.vertical ? z : 2f * f.at - z;
        if (f.band <= 0f || Mathf.Abs(x) <= f.band + 0.001f)
        {
            int r = Top(kind, folds, i - 1, rx, rz, out int fl);
            if (r > 0) { flips = fl + 1; return r; }
        }
        return Top(kind, folds, i - 1, x, z, out flips);
    }

    /// <summary>A hand-drawn ink ring.</summary>
    static Sprite Ring()
    {
        if (ringSprite) return ringSprite;
        const int n = 128;
        var px = new Color32[n * n];
        for (int y = 0; y < n; y++)
        for (int x = 0; x < n; x++)
        {
            float dx = x + 0.5f - n * 0.5f, dy = y + 0.5f - n * 0.5f;
            float a = Mathf.Atan2(dy, dx);
            float r = Mathf.Sqrt(dx * dx + dy * dy);
            float want = n * 0.43f + Mathf.Sin(a * 3f + 1f) * 1.2f + Mathf.Sin(a * 7f) * 0.6f;
            float thick = 3.2f + Mathf.Sin(a * 2f + 0.5f) * 0.9f;
            float d = Mathf.Abs(r - want);
            byte alpha = (byte)(Mathf.Clamp01(thick - d) * 255f);
            px[y * n + x] = new Color32(255, 255, 255, alpha);
        }
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        tex.SetPixels32(px);
        tex.Apply();
        ringSprite = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f);
        return ringSprite;
    }

    /// <summary>One dash of a dashed line (tiles vertically).</summary>
    static Sprite Dash()
    {
        if (dashSprite) return dashSprite;
        const int w = 8, h = 24;
        var px = new Color32[w * h];
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
            px[y * w + x] = new Color32(255, 255, 255, (byte)(y < 14 && x > 0 && x < w - 1 ? 255 : 0));
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };
        tex.SetPixels32(px);
        tex.Apply();
        dashSprite = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
        return dashSprite;
    }
}
