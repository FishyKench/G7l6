using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// "Vapor": the PC's game store. Buy games with your money (see Wallet); a bought game downloads for a few
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
        storeWin = MakeWindow("store", "Vapor", big, StoreBg);
        var c = storeWin.content;

        // Top bar: logo, tabs, wallet.
        var top = UIFactory.Panel("Top bar", c, new Color32(12, 15, 22, 255));
        UIFactory.Place(top.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(big.x, 64));
        var logo = UIFactory.Text("Logo", top.transform, 30, Color.white, TextAlignmentOptions.MidlineLeft);
        logo.text = "<b>VAPOR</b>";
        logo.characterSpacing = 8f;
        UIFactory.Place(logo.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(28, 0), new Vector2(200, 50));
        var steam = UIFactory.Panel("Logo dot", top.transform, StoreAccent);
        steam.sprite = Round();
        UIFactory.Place(steam.rectTransform, new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(18, 0), new Vector2(10, 10));
        storeTabBtn = Tab(top.rectTransform, "STORE", 250, out storeTabLine);
        libraryTabBtn = Tab(top.rectTransform, "LIBRARY", 400, out libraryTabLine);

        var pill = UIFactory.Panel("Wallet", top.transform, new Color(1f, 1f, 1f, 0.08f));
        pill.sprite = Round();
        pill.type = Image.Type.Sliced;
        UIFactory.Place(pill.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-24, 0), new Vector2(220, 42));
        walletText = UIFactory.Text("Money", pill.transform, 20, Color.white, TextAlignmentOptions.Center);
        UIFactory.Fill(walletText.rectTransform);

        storeMessage = UIFactory.Text("Message", c, 20, new Color32(255, 200, 90, 255), TextAlignmentOptions.Center);
        UIFactory.Place(storeMessage.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0, 12), new Vector2(900, 30));

        // Store page: a featured strip and a grid of game cards.
        storePage = UIFactory.Rect("Store", c);
        UIFactory.Fill(storePage, 0, 0, 64, 0);
        var head = UIFactory.Text("Heading", storePage, 22, new Color(1f, 1f, 1f, 0.85f), TextAlignmentOptions.MidlineLeft);
        head.text = "<b>FEATURED & RECOMMENDED</b>";
        UIFactory.Place(head.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(30, -14), new Vector2(600, 34));
        var shown = System.Array.FindAll(Catalogue, g => g.price > 0f);
        const float cardW = 400f, cardH = 248f, gap = 22f;
        for (int i = 0; i < shown.Length; i++)
        {
            int col = i % 3, row = i / 3;
            var pos = new Vector2((col - 1) * (cardW + gap), -86 - row * (cardH + gap) - cardH * 0.5f);
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
        UIFactory.Fill(libList, 0, 0, 46, 0);

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

        var art = Cover(card.rt, g, new Vector2(size.x, size.y - 74f), 34);
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
                ? "<color=#66C0FF>Installed.</color> Ranked matches start at 4:00 PM - PLAY opens the launcher."
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
        if (!Wallet.Spend(g.price))
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
        string text = $"{g.title}\n\n{g.tagline}\n\n<size=70%>Installed from Vapor. This game's modes are still being made.</size>";
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

    static string Initials(string title)
    {
        var parts = title.Split(' ');
        return parts.Length > 1 ? $"{parts[0][0]}{parts[1][0]}" : title.Substring(0, Mathf.Min(2, title.Length));
    }

    static string Title(string upper) => System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(upper.ToLowerInvariant());

    void OnDestroy() => Wallet.Changed -= RefreshStore;
}
