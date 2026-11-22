using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// "Pixel Vault": the PC's game store and library. Buy games with your money (see Wallet); a bought game downloads for a few
/// seconds, then shows up in your Library and on the desktop. Ranked Arena is free and already yours.
/// </summary>
public partial class PCDesktop
{
    public class StoreGame
    {
        public string id, title, tagline, genre;
        public float price;
        public Color a, b; // the cover art's colours
    }

    /// <summary>Everything in the store. "arena" is the ranked game you already have.</summary>
    public static readonly StoreGame[] Catalogue =
    {
        new StoreGame { id = "arena", title = "RANKED ARENA", tagline = "8v8 Domination in VR. Capture A, B and C.", genre = "VR SHOOTER", price = 0f,
                        a = new Color32(20, 16, 48, 255), b = new Color32(90, 240, 220, 255) },
        new StoreGame { id = "hardpoint", title = "HARDPOINT RUSH", tagline = "One moving hill. Hold it, or lose it.", genre = "VR SHOOTER", price = 14.99f,
                        a = new Color32(60, 10, 20, 255), b = new Color32(255, 90, 70, 255) },
        new StoreGame { id = "racer", title = "NEON RACER", tagline = "Midnight street races through the city.", genre = "RACING", price = 9.99f,
                        a = new Color32(30, 8, 60, 255), b = new Color32(255, 70, 200, 255) },
        new StoreGame { id = "beat", title = "BEAT BLADE", tagline = "Slice the blocks to the beat. Feel calmer.", genre = "RHYTHM", price = 11.99f,
                        a = new Color32(8, 30, 60, 255), b = new Color32(80, 170, 255, 255) },
        new StoreGame { id = "deadshift", title = "DEAD SHIFT", tagline = "Survive the night shift with your friend.", genre = "CO-OP SURVIVAL", price = 19.99f,
                        a = new Color32(14, 30, 12, 255), b = new Color32(140, 230, 70, 255) },
        new StoreGame { id = "defuse", title = "DEFUSE", tagline = "One life. Plant it or stop it.", genre = "TACTICAL", price = 17.99f,
                        a = new Color32(40, 30, 8, 255), b = new Color32(255, 190, 60, 255) },
    };

    class StoreCard
    {
        public StoreGame game;
        public RectTransform rt, button;
        public Image frame, buttonBg, progress;
        public TextMeshProUGUI buttonText;
    }

    Window storeWin;
    RectTransform storePage, libraryPage, storeTabBtn, libraryTabBtn, libList, libPlay;
    TextMeshProUGUI walletText, storeMessage, libTitle, libInfo, libPlayText;
    Image storeTabLine, libraryTabLine, libArt, libArtGlow;
    readonly List<StoreCard> storeCards = new List<StoreCard>();
    readonly List<(RectTransform rt, Image bg, StoreGame game)> libRows = new List<(RectTransform, Image, StoreGame)>();
    readonly Dictionary<string, float> downloading = new Dictionary<string, float>();
    StoreGame libSelected;
    bool showLibrary;
    float messageUntil;
    const float DownloadSeconds = 4f;

    static readonly Color StoreBg = new Color32(18, 22, 32, 255);
    static readonly Color StorePanel = new Color32(28, 34, 48, 255);
    static readonly Color StoreAccent = new Color32(102, 192, 255, 255);
    static readonly Color StoreGreen = new Color32(110, 200, 60, 255);

    static StoreGame GameById(string id) => System.Array.Find(Catalogue, g => g.id == id);
    static bool Owns(StoreGame g) => g.price <= 0f || Wallet.Owns(g.id);

    // ------------------------------------------------------------------ building

    void BuildStore()
    {
        var big = new Vector2(ScreenW - 40f, ScreenH - TaskbarH - 30f);
        storeWin = MakeWindow("store", "Pixel Vault - Games & Library", big, StoreBg);
        var c = storeWin.content;
        var glowA = UIFactory.Panel("Glow", c, new Color(0.45f, 0.25f, 1f, 0.16f));
        glowA.sprite = Round();
        UIFactory.Place(glowA.rectTransform, new Vector2(0f, 1f), new Vector2(0.5f, 0.5f), new Vector2(150, -120), new Vector2(900, 600));
        var glowB = UIFactory.Panel("Glow", c, new Color(0.2f, 0.85f, 1f, 0.1f));
        glowB.sprite = Round();
        UIFactory.Place(glowB.rectTransform, new Vector2(1f, 0f), new Vector2(0.5f, 0.5f), new Vector2(-150, 60), new Vector2(900, 600));

        // Top bar: logo, tabs, wallet.
        var top = UIFactory.Panel("Top bar", c, new Color32(12, 15, 22, 255));
        UIFactory.Place(top.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(big.x, 64));
        var mark = UIFactory.Rect("Logo mark", top.transform);
        UIFactory.Place(mark, new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(46, 0), new Vector2(64, 64));
        DrawPad(mark, 0.8f, new Color32(140, 90, 255, 255), new Color32(90, 240, 220, 255));
        var logo = UIFactory.Text("Logo", top.transform, 28, Color.white, TextAlignmentOptions.MidlineLeft);
        logo.text = "<b>PIXEL<color=#5AF0DC>VAULT</color></b>";
        logo.characterSpacing = 4f;
        UIFactory.Place(logo.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(84, 8), new Vector2(240, 36));
        var slogan = UIFactory.Text("Slogan", top.transform, 13, new Color(1f, 1f, 1f, 0.5f), TextAlignmentOptions.MidlineLeft);
        slogan.text = "BUY GAMES  -  PLAY YOUR LIBRARY";
        slogan.characterSpacing = 2f;
        UIFactory.Place(slogan.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(85, -16), new Vector2(260, 20));
        storeTabBtn = Tab(top.rectTransform, "STORE", 420, out storeTabLine);
        libraryTabBtn = Tab(top.rectTransform, "LIBRARY", 570, out libraryTabLine);

        var pill = UIFactory.Panel("Wallet", top.transform, new Color(1f, 1f, 1f, 0.08f));
        pill.sprite = Round();
        pill.type = Image.Type.Sliced;
        UIFactory.Place(pill.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-24, 0), new Vector2(220, 42));
        walletText = UIFactory.Text("Money", pill.transform, 20, Color.white, TextAlignmentOptions.Center);
        UIFactory.Fill(walletText.rectTransform);


        // Store page: a featured strip and a grid of game cards.
        storePage = UIFactory.Rect("Store", c);
        UIFactory.Fill(storePage, 0, 0, 64, 0);
        var head = UIFactory.Text("Heading", storePage, 22, new Color(1f, 1f, 1f, 0.85f), TextAlignmentOptions.MidlineLeft);
        head.text = "<b>FEATURED GAMES</b>  <color=#FFFFFF80><size=16>buy a game and it installs to your desktop</size></color>";
        UIFactory.Place(head.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(30, -10), new Vector2(900, 34));
        var shown = System.Array.FindAll(Catalogue, g => g.price > 0f);
        const float cardW = 400f, cardH = 222f, gap = 18f;
        for (int i = 0; i < shown.Length; i++)
        {
            int col = i % 3, row = i / 3;
            var pos = new Vector2((col - 1) * (cardW + gap), -56 - row * (cardH + gap) - cardH * 0.5f);
            storeCards.Add(Card(shown[i], pos, new Vector2(cardW, cardH)));
        }

        // Library page: your games on the left, the chosen one on the right.
        libraryPage = UIFactory.Rect("Library", c);
        UIFactory.Fill(libraryPage, 0, 0, 64, 0);
        var side = UIFactory.Panel("Games", libraryPage, new Color32(14, 18, 26, 255));
        UIFactory.Place(side.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(320, big.y - 100));
        side.rectTransform.anchorMin = new Vector2(0f, 0f);
        side.rectTransform.anchorMax = new Vector2(0f, 1f);
        side.rectTransform.offsetMin = new Vector2(0f, 0f);
        side.rectTransform.offsetMax = new Vector2(320f, 0f);
        var sideHead = UIFactory.Text("Heading", side.transform, 16, new Color(1f, 1f, 1f, 0.5f), TextAlignmentOptions.MidlineLeft);
        sideHead.text = "<b>YOUR GAMES</b>";
        UIFactory.Place(sideHead.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(20, -10), new Vector2(280, 30));
        libList = UIFactory.Rect("List", side.transform);
        UIFactory.Fill(libList, 0, 0, 46, 40);

        libArt = UIFactory.Panel("Art", libraryPage, Color.black);
        libArt.rectTransform.anchorMin = new Vector2(0f, 1f);
        libArt.rectTransform.anchorMax = new Vector2(1f, 1f);
        libArt.rectTransform.pivot = new Vector2(0.5f, 1f);
        libArt.rectTransform.offsetMin = new Vector2(320f, -300f);
        libArt.rectTransform.offsetMax = new Vector2(0f, 0f);
        libArtGlow = UIFactory.Panel("Glow", libArt.transform, Color.white);
        libArtGlow.sprite = Round();
        UIFactory.Place(libArtGlow.rectTransform, new Vector2(0.75f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(700, 520));
        libTitle = UIFactory.Text("Title", libArt.transform, 64, Color.white, TextAlignmentOptions.BottomLeft);
        UIFactory.Fill(libTitle.rectTransform, 36, 36, 30, 30);
        libTitle.fontStyle = FontStyles.Bold;

        libPlay = UIFactory.Panel("Play", libraryPage, StoreGreen).rectTransform;
        var playImg = libPlay.GetComponent<Image>();
        playImg.sprite = Round();
        playImg.type = Image.Type.Sliced;
        UIFactory.Place(libPlay, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(356, -326), new Vector2(220, 64));
        libPlayText = UIFactory.Text("Text", libPlay, 26, Color.white, TextAlignmentOptions.Center);
        libPlayText.fontStyle = FontStyles.Bold;
        UIFactory.Fill(libPlayText.rectTransform);
        libInfo = UIFactory.Text("Info", libraryPage, 20, new Color(1f, 1f, 1f, 0.75f), TextAlignmentOptions.TopLeft);
        libInfo.rectTransform.anchorMin = new Vector2(0f, 1f);
        libInfo.rectTransform.anchorMax = new Vector2(1f, 1f);
        libInfo.rectTransform.pivot = new Vector2(0f, 1f);
        libInfo.rectTransform.offsetMin = new Vector2(356f, -560f);
        libInfo.rectTransform.offsetMax = new Vector2(-36f, -410f);

        var strip = UIFactory.Panel("Message bar", c, new Color32(12, 15, 22, 235));
        UIFactory.Place(strip.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), Vector2.zero, new Vector2(big.x, 40));
        storeMessage = UIFactory.Text("Message", strip.transform, 18, new Color32(255, 200, 90, 255), TextAlignmentOptions.Center);
        UIFactory.Fill(storeMessage.rectTransform, 20, 20, 0, 0);
        Wallet.Changed += RefreshStore;
        RefreshStore();
    }

    RectTransform Tab(RectTransform bar, string label, float x, out Image line)
    {
        var t = UIFactory.Text(label, bar, 20, Color.white, TextAlignmentOptions.Center);
        t.text = "<b>" + label + "</b>";
        t.characterSpacing = 3f;
        UIFactory.Place(t.rectTransform, new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(x, 0), new Vector2(140, 64));
        line = UIFactory.Panel("Line", t.transform, StoreAccent);
        UIFactory.Place(line.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0, 6), new Vector2(90, 3));
        return t.rectTransform;
    }

    StoreCard Card(StoreGame g, Vector2 pos, Vector2 size)
    {
        var card = new StoreCard { game = g };
        card.rt = UIFactory.Rect(g.title, storePage);
        UIFactory.Place(card.rt, new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), pos, size);
        card.frame = UIFactory.Panel("Hover", card.rt, StoreAccent);
        UIFactory.Fill(card.frame.rectTransform, -3, -3, -3, -3);
        var bg = UIFactory.Panel("Card", card.rt, StorePanel);
        UIFactory.Fill(bg.rectTransform);

        var art = Cover(card.rt, g, new Vector2(size.x, size.y - 72f), 32);
        art.anchorMin = art.anchorMax = new Vector2(0.5f, 1f);
        art.pivot = new Vector2(0.5f, 1f);
        art.anchoredPosition = Vector2.zero;

        var genre = UIFactory.Text("Genre", card.rt, 14, new Color(1f, 1f, 1f, 0.5f), TextAlignmentOptions.TopLeft);
        genre.text = g.genre;
        UIFactory.Place(genre.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(16, 42), new Vector2(220, 22));
        var line = UIFactory.Text("Tagline", card.rt, 15, new Color(1f, 1f, 1f, 0.85f), TextAlignmentOptions.TopLeft);
        line.text = g.tagline;
        line.enableWordWrapping = true;
        UIFactory.Place(line.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(16, 6), new Vector2(230, 40));

        card.buttonBg = UIFactory.Panel("Buy", card.rt, StoreGreen);
        card.buttonBg.sprite = Round();
        card.buttonBg.type = Image.Type.Sliced;
        card.button = card.buttonBg.rectTransform;
        UIFactory.Place(card.button, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-14, 14), new Vector2(128, 44));
        card.progress = UIFactory.Panel("Download", card.button, new Color(1f, 1f, 1f, 0.35f));
        card.progress.rectTransform.anchorMin = Vector2.zero;
        card.progress.rectTransform.anchorMax = new Vector2(0f, 1f);
        card.progress.rectTransform.offsetMin = card.progress.rectTransform.offsetMax = Vector2.zero;
        card.buttonText = UIFactory.Text("Text", card.button, 18, Color.white, TextAlignmentOptions.Center);
        card.buttonText.fontStyle = FontStyles.Bold;
        UIFactory.Fill(card.buttonText.rectTransform);
        return card;
    }

    /// <summary>A game's cover art made from its two colours and its name.</summary>
    static RectTransform Cover(RectTransform parent, StoreGame g, Vector2 size, float fontSize)
    {
        var art = UIFactory.Panel("Cover", parent, g.a);
        art.rectTransform.sizeDelta = size;
        art.gameObject.AddComponent<RectMask2D>();
        var glow = UIFactory.Panel("Glow", art.transform, new Color(g.b.r, g.b.g, g.b.b, 0.55f));
        glow.sprite = Round();
        UIFactory.Place(glow.rectTransform, new Vector2(1f, 0f), new Vector2(0.5f, 0.5f), new Vector2(-60, 20), new Vector2(size.x * 0.9f, size.y * 1.3f));
        for (int i = 0; i < 3; i++)
        {
            var stripe = UIFactory.Panel("Stripe", art.transform, new Color(g.b.r, g.b.g, g.b.b, 0.25f - i * 0.06f));
            UIFactory.Place(stripe.rectTransform, new Vector2(0f, 0f), new Vector2(0.5f, 0.5f), new Vector2(size.x * (0.2f + i * 0.18f), size.y * 0.3f), new Vector2(18, size.y * 2f));
            stripe.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -28f);
        }
        var title = UIFactory.Text("Title", art.transform, fontSize, Color.white, TextAlignmentOptions.BottomLeft);
        title.text = "<b>" + g.title + "</b>";
        title.enableWordWrapping = true;
        UIFactory.Fill(title.rectTransform, 18, 18, 14, 12);
        var under = UIFactory.Panel("Underline", art.transform, g.b);
        UIFactory.Place(under.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(18, 8), new Vector2(60, 4));
        return art.rectTransform;
    }

    void RebuildLibrary()
    {
        foreach (var r in libRows) if (r.rt) Destroy(r.rt.gameObject);
        libRows.Clear();
        int n = 0;
        foreach (var g in Catalogue)
        {
            if (!Owns(g)) continue;
            var bg = UIFactory.Panel(g.title, libList, new Color(1f, 1f, 1f, 0f));
            UIFactory.Place(bg.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -n * 50), new Vector2(304, 46));
            var dot = UIFactory.Panel("Icon", bg.transform, g.b);
            UIFactory.Place(dot.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(14, 0), new Vector2(30, 30));
            var t = UIFactory.Text("Name", bg.transform, 17, Color.white, TextAlignmentOptions.MidlineLeft);
            t.text = g.title;
            UIFactory.Fill(t.rectTransform, 56, 8, 0, 0);
            libRows.Add((bg.rectTransform, bg, g));
            n++;
        }
        if (libSelected == null || !Owns(libSelected)) libSelected = Catalogue[0];
    }

    // ------------------------------------------------------------------ every frame

    void UpdateStore()
    {
        if (storeWin == null || !storeWin.open) return;
        walletText.text = "<color=#66C0FF>WALLET</color>  <b>" + Wallet.MoneyText + "</b>";
        if (Time.unscaledTime > messageUntil) storeMessage.text = "";

        storePage.gameObject.SetActive(!showLibrary);
        libraryPage.gameObject.SetActive(showLibrary);
        storeTabLine.enabled = !showLibrary;
        libraryTabLine.enabled = showLibrary;

#if UNITY_EDITOR
        if (Input.GetKeyDown(KeyCode.F9)) { Wallet.Add(25f); Message("[editor] +$25.00"); }
        if (Input.GetKeyDown(KeyCode.F10)) { Wallet.ResetAll(); Message("[editor] wallet reset"); }
#endif

        // Finished downloads land in the library and on the desktop.
        if (downloading.Count > 0)
        {
            var done = new List<string>();
            foreach (var d in downloading) if (Time.unscaledTime >= d.Value) done.Add(d.Key);
            foreach (var id in done)
            {
                downloading.Remove(id);
                var g = GameById(id);
                AddGameIcon(g);
                RebuildLibrary();
                Message(g.title + " is installed - it's on your desktop and in your Library.", StoreGreen);
            }
        }

        if (!showLibrary)
        {
            foreach (var c in storeCards)
            {
                bool owned = Owns(c.game), dl = downloading.TryGetValue(c.game.id, out float end);
                c.frame.enabled = Over(c.rt);
                c.progress.rectTransform.anchorMax = new Vector2(dl ? 1f - (end - Time.unscaledTime) / DownloadSeconds : 0f, 1f);
                if (dl) { c.buttonText.text = "INSTALLING"; c.buttonBg.color = new Color32(60, 100, 140, 255); }
                else if (owned) { c.buttonText.text = "IN LIBRARY"; c.buttonBg.color = new Color(1f, 1f, 1f, 0.12f); }
                else
                {
                    bool afford = Wallet.Money >= c.game.price;
                    c.buttonText.text = Wallet.Format(c.game.price);
                    c.buttonBg.color = Over(c.button) ? (afford ? new Color32(140, 230, 80, 255) : new Color32(200, 70, 70, 255))
                                                      : (afford ? StoreGreen : new Color32(90, 100, 110, 255));
                }
            }
        }
        else
        {
            foreach (var r in libRows)
                r.bg.color = r.game == libSelected ? new Color(StoreAccent.r, StoreAccent.g, StoreAccent.b, 0.25f)
                           : Over(r.rt) ? new Color(1f, 1f, 1f, 0.06f) : new Color(1f, 1f, 1f, 0f);
            var g = libSelected;
            libArt.color = g.a;
            libArtGlow.color = new Color(g.b.r, g.b.g, g.b.b, 0.5f);
            libTitle.text = g.title;
            bool playable = g.id == "arena";
            libPlayText.text = "PLAY";
            libPlay.GetComponent<Image>().color = Over(libPlay) ? new Color32(140, 230, 80, 255) : StoreGreen;
            libInfo.text = $"<b>{g.genre}</b>\n{g.tagline}\n\n" + (playable
                ? "<color=#66C0FF>Installed.</color> Ranked matches start at " + (pc && pc.Match ? pc.Match.StartTimeText : "4:00 PM") + " - PLAY opens the launcher."
                : "<color=#66C0FF>Installed.</color> Game modes for this one are still being made.");
        }
    }

    bool StoreClick()
    {
        if (Over(storeTabBtn)) { showLibrary = false; return true; }
        if (Over(libraryTabBtn)) { showLibrary = true; RebuildLibrary(); return true; }
        if (!showLibrary)
        {
            foreach (var c in storeCards)
            {
                if (!Over(c.button) || Owns(c.game) || downloading.ContainsKey(c.game.id)) continue;
                Buy(c.game);
                return true;
            }
        }
        else
        {
            foreach (var r in libRows) if (Over(r.rt)) { libSelected = r.game; return true; }
            if (Over(libPlay)) { LaunchFromLibrary(libSelected); return true; }
        }
        return false;
    }

    void Buy(StoreGame g)
    {
        if (!Wallet.Pay(g.price, "Pixel Vault - " + Title(g.title), Wallet.Category.Purchase))
        {
            Message($"Not enough money - {g.title} costs {Wallet.Format(g.price)}, you have {Wallet.MoneyText}.", new Color32(255, 110, 110, 255));
            return;
        }
        Wallet.Give(g.id);
        downloading[g.id] = Time.unscaledTime + DownloadSeconds;
        Fx.Play(Fx.Click, pc ? pc.transform.position : Vector3.zero, 0.5f, 1.3f);
        Message($"Bought {g.title}! Downloading...", StoreAccent);
    }

    void LaunchFromLibrary(StoreGame g)
    {
        if (g.id == "arena") { OpenApp("game"); return; }
        OpenApp("vapor:" + g.id);
    }

    void Message(string text, Color? colour = null)
    {
        storeMessage.text = text;
        storeMessage.color = colour ?? new Color32(255, 200, 90, 255);
        messageUntil = Time.unscaledTime + 4f;
    }

    void RefreshStore() { if (libList) RebuildLibrary(); }

    void AddGameIcon(StoreGame g)
    {
        if (g == null || g.id == "arena" || icons.Exists(i => i.id == "vapor:" + g.id)) return;
        string text = $"{g.title}\n\n{g.tagline}\n\n<size=70%>Installed from Pixel Vault. This game's modes are still being made.</size>";
        AddIcon("vapor:" + g.id, Title(g.title), text, r =>
        {
            Shape(r, g.b, Vector2.zero, new Vector2(60, 60), true);
            Shape(r, g.a, Vector2.zero, new Vector2(54, 54), true);
            var t = UIFactory.Text("Initials", r, 22, g.b, TextAlignmentOptions.Center);
            t.text = "<b>" + Initials(g.title) + "</b>";
            UIFactory.Fill(t.rectTransform);
        });
        if (windowLayer) icons[icons.Count - 1].rt.SetSiblingIndex(windowLayer.GetSiblingIndex()); // under the windows
    }

    /// <summary>A little game controller (the app's logo).</summary>
    static void DrawPad(RectTransform r, float s, Color body, Color accent)
    {
        Dot(r, body, new Vector2(-17, -9) * s, 24 * s);
        Dot(r, body, new Vector2(17, -9) * s, 24 * s);
        Shape(r, body, Vector2.zero, new Vector2(56, 30) * s, true);
        Shape(r, Color.white, new Vector2(-14, 1) * s, new Vector2(14, 4) * s);
        Shape(r, Color.white, new Vector2(-14, 1) * s, new Vector2(4, 14) * s);
        Dot(r, accent, new Vector2(12, 5) * s, 7 * s);
        Dot(r, new Color32(255, 90, 180, 255), new Vector2(19, -2) * s, 7 * s);
    }

    static string Initials(string title)
    {
        var parts = title.Split(' ');
        return parts.Length > 1 ? $"{parts[0][0]}{parts[1][0]}" : title.Substring(0, Mathf.Min(2, title.Length));
    }

    static string Title(string upper) => System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(upper.ToLowerInvariant());

    void OnDestroy() { Wallet.Changed -= RefreshStore; Wallet.Changed -= MarkBankDirty; Shop.Changed -= MarkShopDirty; Mail.Changed -= MarkMailDirty; ClipHub.Changed -= MarkClipHubDirty; Shop.Changed -= MarkLiveDirty; LiveStream.Changed -= MarkLiveDirty; ClipsDestroy(); }
}
