using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The PC's Browser (desktop icon "Browser"): a big window with a tab, an address bar (back / refresh) and a
/// bookmark bar for the made-up NestNet sites: Bank, ShopNest, MyMail, ClipHub, LiveLoop.
///
/// HOW TO ADD / FILL IN A SITE
///   1. Its entry already exists in <see cref="BuildBrowser"/> (id, name, host, colours, logo, tagline).
///   2. Write a page class:  class MySitePage : SitePage { Build(RectTransform root) { ... } }
///      - Build() draws the page into 'root' (PageW x PageH, clipped), once.
///      - Refresh() is called every time the page is opened / reloaded (and when you call RefreshBrowserPage()).
///      - Tick() runs every frame while the page is showing (live numbers, hover colours).
///      - Click() gets the fake-cursor click (return true if you used it).  Scroll(dir) gets the mouse wheel.
///   3. In <see cref="MakePage"/> map the site id to the new class (the others fall back to ComingSoonPage).
/// Use Over(rect) for hit tests, Round()/Shape()/Dot() for art, Txt()/TxtTL() for text (they set size, wrap and
/// ellipsis so nothing spills out of its box).
/// </summary>
public partial class PCDesktop
{
    const string BrowserHome = "bank";
    const float TabBarH = 0f, ToolBarH = 46f, BookmarkBarH = 36f, ChromeH = TabBarH + ToolBarH + BookmarkBarH;
    const float PageW = ScreenW - 40f, PageH = ScreenH - TaskbarH - 30f - 36f - ChromeH;

    static readonly Color ChromeDark = new Color32(14, 15, 22, 255);
    static readonly Color ChromeMid = new Color32(30, 32, 44, 255);
    static readonly Color ChromeBar = new Color32(24, 26, 36, 255);
    static readonly Color AddressBg = new Color32(44, 47, 62, 255);

    /// <summary>One bookmarked site: how it looks and the page that shows inside the window.</summary>
    class Site
    {
        public string id, name, host, wordmark, tagline;
        public Color accent, bg;
        public bool light;                       // light page (dark text) or dark page (light text)
        public System.Action<RectTransform> logo; // drawn in a 64x64 box
        public SitePage page;
        public RectTransform bookmark;
        public Image bookmarkBg;
    }

    abstract class SitePage
    {
        protected PCDesktop d;
        protected Site site;
        public RectTransform root;
        public void Init(PCDesktop desktop, Site s, RectTransform parent)
        {
            d = desktop;
            site = s;
            root = UIFactory.Rect(s.name + " page", parent);
            UIFactory.Fill(root);
            root.gameObject.AddComponent<RectMask2D>();
            Build(root);
            root.gameObject.SetActive(false);
        }
        protected abstract void Build(RectTransform root);
        public virtual void Refresh() { }
        public virtual void Tick() { }
        public virtual bool Click() => false;
        public virtual void Scroll(float direction) { }
    }

    Window browserWin;
    readonly List<Site> sites = new List<Site>();
    readonly List<string> browserBack = new List<string>();
    Site currentSite;
    RectTransform pageArea, backButton, refreshButton, loadBar;
    Image backBg, refreshBg, loadBarImage;
    CanvasGroup backGroup;
    TextMeshProUGUI tabTitle, urlText;
    Image tabFavicon;
    TextMeshProUGUI tabFaviconText;
    float loadStart = -10f;
    bool bankDirty = true;
    BankPage bankPage;

    // ------------------------------------------------------------------ building

    void BuildBrowser()
    {
        var big = new Vector2(ScreenW - 40f, ScreenH - TaskbarH - 30f);
        browserWin = MakeWindow("browser", "Browser", big, ChromeDark);
        var c = browserWin.content;

        // Page area first (behind the chrome).
        pageArea = UIFactory.Rect("Page area", c);
        UIFactory.Fill(pageArea, 0, 0, ChromeH, 0);

        // Tab strip
        var tabBar = UIFactory.Panel("Tab strip", c, ChromeDark);
        UIFactory.Place(tabBar.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(big.x, TabBarH));
        var tab = RPanel(tabBar.transform, ChromeMid);
        TopLeft(tab.rectTransform, 12, 6, 260, 40);
        var tabFlat = UIFactory.Panel("Tab base", tabBar.transform, ChromeMid);
        TopLeft(tabFlat.rectTransform, 12, 24, 260, 14);
        tabFavicon = RPanel(tab.transform, Color.white);
        TopLeft(tabFavicon.rectTransform, 12, 7, 20, 20);
        tabFaviconText = Txt(tabFavicon.transform, "", 13, Color.white, TextAlignmentOptions.Center, new Vector2(0, 0), new Vector2(0, 0), Vector2.zero, Vector2.zero, false, true);
        UIFactory.Fill(tabFaviconText.rectTransform);
        tabTitle = TxtTL(tab.transform, "", 15, Color.white, TextAlignmentOptions.MidlineLeft, 40, 3, 180, 26);
        TxtTL(tab.transform, "x", 15, new Color(1f, 1f, 1f, 0.4f), TextAlignmentOptions.Center, 232, 3, 22, 26);
        tabBar.gameObject.SetActive(false); // one site at a time: the window title already says where you are

        // Toolbar: back, refresh, address bar
        var bar = UIFactory.Panel("Toolbar", c, ChromeMid);
        UIFactory.Place(bar.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -TabBarH), new Vector2(big.x, ToolBarH));
        backBg = RPanel(bar.transform, new Color(1f, 1f, 1f, 0f));
        backButton = backBg.rectTransform;
        TopLeft(backButton, 12, 8, 36, 30);
        backGroup = backButton.gameObject.AddComponent<CanvasGroup>();
        var backArt = UIFactory.Rect("Art", backButton);
        UIFactory.Place(backArt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(20, 20));
        Chevron(backArt, new Color(1f, 1f, 1f, 0.9f), -1);
        refreshBg = RPanel(bar.transform, new Color(1f, 1f, 1f, 0f));
        refreshButton = refreshBg.rectTransform;
        TopLeft(refreshButton, 54, 8, 36, 30);
        var refreshArt = UIFactory.Rect("Art", refreshButton);
        UIFactory.Place(refreshArt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(20, 20));
        Dot(refreshArt, new Color(1f, 1f, 1f, 0.9f), Vector2.zero, 17);
        Dot(refreshArt, ChromeMid, Vector2.zero, 11);
        Shape(refreshArt, ChromeMid, new Vector2(6, 6), new Vector2(8, 8));
        Shape(refreshArt, new Color(1f, 1f, 1f, 0.9f), new Vector2(5, 7), new Vector2(10, 3));
        Shape(refreshArt, new Color(1f, 1f, 1f, 0.9f), new Vector2(8.5f, 3.5f), new Vector2(3, 8));

        var address = RPanel(bar.transform, AddressBg);
        address.rectTransform.anchorMin = new Vector2(0f, 0.5f);
        address.rectTransform.anchorMax = new Vector2(1f, 0.5f);
        address.rectTransform.pivot = new Vector2(0.5f, 0.5f);
        address.rectTransform.offsetMin = new Vector2(104f, -16f);
        address.rectTransform.offsetMax = new Vector2(-16f, 16f);
        var lockArt = UIFactory.Rect("Lock", address.transform);
        UIFactory.Place(lockArt, new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(22, 0), new Vector2(20, 20));
        Dot(lockArt, new Color32(120, 200, 150, 255), new Vector2(0, 3), 10);
        Dot(lockArt, AddressBg, new Vector2(0, 3), 6);
        Shape(lockArt, new Color32(120, 200, 150, 255), new Vector2(0, -3), new Vector2(14, 10), true);
        urlText = Txt(address.transform, "", 17, Color.white, TextAlignmentOptions.MidlineLeft, new Vector2(0f, 0f), new Vector2(0f, 0.5f), Vector2.zero, Vector2.zero);
        urlText.rectTransform.anchorMax = Vector2.one;
        urlText.rectTransform.offsetMin = new Vector2(44, 0);
        urlText.rectTransform.offsetMax = new Vector2(-14, 0);

        // Bookmark bar
        var marks = UIFactory.Panel("Bookmark bar", c, ChromeBar);
        UIFactory.Place(marks.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -(TabBarH + ToolBarH)), new Vector2(big.x, BookmarkBarH));

        // Sites: add a new one here, then map its page in MakePage().
        sites.Add(new Site
        {
            id = "bank", name = "Nest Bank", host = "bank.nestnet", wordmark = "<b>NEST</b><color=#20C4A0><b>BANK</b></color>",
            tagline = "Your money, safe and sound.", accent = new Color32(32, 196, 160, 255), bg = new Color32(12, 24, 40, 255), light = false,
            logo = r => { Shape(r, new Color32(32, 196, 160, 255), Vector2.zero, new Vector2(60, 60), true); Dot(r, Color.white, Vector2.zero, 38); LogoText(r, "$", 28, new Color32(20, 150, 124, 255)); },
        });
        sites.Add(new Site
        {
            id = "shop", name = "ShopNest", host = "shop.nestnet", wordmark = "<b>Shop</b><color=#FF8C28><b>Nest</b></color>",
            tagline = "Gadgets, streaming gear and PC parts - delivered to your front door the next morning.",
            accent = new Color32(255, 140, 40, 255), bg = new Color32(255, 245, 232, 255), light = true,
            logo = r =>
            {
                Shape(r, new Color32(255, 140, 40, 255), Vector2.zero, new Vector2(60, 60), true);
                Dot(r, Color.white, new Vector2(0, 11), 20); Dot(r, new Color32(255, 140, 40, 255), new Vector2(0, 11), 12);
                Shape(r, Color.white, new Vector2(0, -7), new Vector2(34, 26), true);
            },
        });
        sites.Add(new Site
        {
            id = "mail", name = "MyMail", host = "mail.nestnet", wordmark = "<b>My</b><color=#4682F0><b>Mail</b></color>",
            tagline = "Messages from Mom, tournament invites and delivery notices.",
            accent = new Color32(70, 130, 240, 255), bg = new Color32(236, 243, 255, 255), light = true,
            logo = r =>
            {
                Shape(r, new Color32(70, 130, 240, 255), Vector2.zero, new Vector2(60, 60), true);
                Shape(r, Color.white, Vector2.zero, new Vector2(40, 28), true);
                Shape(r, new Color32(70, 130, 240, 255), new Vector2(-8, 4), new Vector2(22, 3), false, -32f);
                Shape(r, new Color32(70, 130, 240, 255), new Vector2(8, 4), new Vector2(22, 3), false, 32f);
            },
        });
        sites.Add(new Site
        {
            id = "clip", name = "ClipHub", host = "clip.nestnet", wordmark = "<b>Clip</b><color=#BE50FF><b>Hub</b></color>",
            tagline = "Upload your best Ranked Arena moments and watch the views roll in.",
            accent = new Color32(190, 80, 255, 255), bg = new Color32(22, 14, 38, 255), light = false,
            logo = r => { Shape(r, new Color32(190, 80, 255, 255), Vector2.zero, new Vector2(60, 60), true); Shape(r, Color.white, Vector2.zero, new Vector2(38, 28), true); LogoText(r, ">", 28, new Color32(150, 50, 215, 255)); },
        });
        sites.Add(new Site
        {
            id = "live", name = "LiveLoop", host = "live.nestnet", wordmark = "<b>Live</b><color=#FF4058><b>Loop</b></color>",
            tagline = "Go live from your room, play and collect tips. You will need a webcam and a microphone.",
            accent = new Color32(255, 64, 88, 255), bg = new Color32(14, 18, 24, 255), light = false,
            logo = r =>
            {
                Shape(r, new Color32(34, 38, 48, 255), Vector2.zero, new Vector2(60, 60), true);
                Dot(r, new Color32(255, 64, 88, 255), Vector2.zero, 40); Dot(r, new Color32(34, 38, 48, 255), Vector2.zero, 28); Dot(r, new Color32(255, 64, 88, 255), Vector2.zero, 14);
            },
        });

        float x = 12f;
        foreach (var s in sites)
        {
            s.page = MakePage(s);
            s.page.Init(this, s, pageArea);

            var b = RPanel(marks.transform, new Color(1f, 1f, 1f, 0f));
            s.bookmarkBg = b;
            s.bookmark = b.rectTransform;
            TopLeft(s.bookmark, x, 4, 150, 28);
            var fav = RPanel(b.transform, s.accent);
            TopLeft(fav.rectTransform, 12, 9, 10, 10);
            TxtTL(b.transform, s.name, 16, new Color(1f, 1f, 1f, 0.9f), TextAlignmentOptions.MidlineLeft, 30, 0, s.id == "mail" ? 76 : 116, 28);
            if (s.id == "mail")
            {
                mailBookmarkBadge = RPanel(b.transform, new Color32(236, 56, 70, 255));
                TopLeft(mailBookmarkBadge.rectTransform, 112, 5, 30, 18);
                mailBookmarkBadgeText = Txt(mailBookmarkBadge.transform, "", 12, Color.white, TextAlignmentOptions.Center, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, false, true);
                UIFactory.Fill(mailBookmarkBadgeText.rectTransform);
                mailBookmarkBadgeText.overflowMode = TextOverflowModes.Overflow;
                mailBookmarkBadge.gameObject.SetActive(false);
            }
            x += 158f;
        }
        bankPage = sites.Find(s => s.id == "bank").page as BankPage;
        shopPage = sites.Find(s => s.id == "shop").page as ShopPage;
        mailPage = sites.Find(s => s.id == "mail").page as MailPage;
        clipHubPage = sites.Find(s => s.id == "clip").page as ClipHubPage;
        livePage = sites.Find(s => s.id == "live").page as LivePage;

        // Loading bar along the bottom of the toolbar
        loadBarImage = UIFactory.Panel("Loading", c, Color.white);
        loadBar = loadBarImage.rectTransform;
        loadBar.anchorMin = loadBar.anchorMax = new Vector2(0f, 1f);
        loadBar.pivot = new Vector2(0f, 1f);
        loadBar.anchoredPosition = new Vector2(0f, -(TabBarH + ToolBarH - 2f));
        loadBar.sizeDelta = new Vector2(0f, 3f);

        Wallet.Changed += MarkBankDirty;
        Shop.Changed += MarkShopDirty;
        Mail.Changed += MarkMailDirty;
        ClipHub.Changed += MarkClipHubDirty;
        Shop.Changed += MarkLiveDirty;
        LiveStream.Changed += MarkLiveDirty;
        shownUnread = -1;
    }

    SitePage MakePage(Site s)
    {
        switch (s.id)
        {
            case "bank": return new BankPage();
            case "shop": return new ShopPage();
            case "mail": return new MailPage();
            case "clip": return new ClipHubPage();
            case "live": return new LivePage();
            default: return new ComingSoonPage();
        }
    }

    void MarkBankDirty() => bankDirty = true;

    // ------------------------------------------------------------------ navigation

    void OpenBrowser()
    {
        if (!browserWin.open && currentSite == null) Navigate(BrowserHome, false);
        Focus(Opened(browserWin));
    }

    /// <summary>Shows a site in the tab. 'push' remembers the previous site for the back button.</summary>
    void Navigate(string id, bool push = true)
    {
        var site = sites.Find(s => s.id == id);
        if (site == null) return;
        if (push && currentSite != null && currentSite != site) browserBack.Add(currentSite.id);
        currentSite = site;
        foreach (var s in sites) s.page.root.gameObject.SetActive(s == site);
        tabTitle.text = site.name;
        tabFavicon.color = site.accent;
        tabFaviconText.text = site.name.Substring(0, 1);
        tabFaviconText.color = site.accent.grayscale > 0.6f ? new Color32(30, 30, 40, 255) : Color.white;
        urlText.text = "<color=#8A93A6>https://</color>" + site.host;
        loadBarImage.color = site.accent;
        loadStart = Time.unscaledTime;
        SetTitle(browserWin, "Browser - " + site.name);
        site.page.Refresh();
        Fx.Play(Fx.Click, pc ? pc.transform.position : Vector3.zero, 0.3f, 1.4f);
    }

    /// <summary>Re-draws the page that's showing (call when its data changed).</summary>
    void RefreshBrowserPage() { if (currentSite != null) currentSite.page.Refresh(); }

    // ------------------------------------------------------------------ every frame

    void UpdateBrowser()
    {
        if (browserWin == null) return;
        if (bankDirty) { bankDirty = false; if (bankPage != null) bankPage.Refresh(); }
        if (shopDirty) { shopDirty = false; if (shopPage != null) shopPage.Refresh(); }
        if (mailDirty) { mailDirty = false; if (mailPage != null) mailPage.Refresh(); }
        if (clipHubDirty) { clipHubDirty = false; if (clipHubPage != null) clipHubPage.Refresh(); }
        if (liveDirty) { liveDirty = false; if (livePage != null) livePage.Refresh(); }
        UpdateMailBadges();
        if (!browserWin.open || browserWin.minimized || currentSite == null) return;

        backGroup.alpha = browserBack.Count > 0 ? 1f : 0.3f;
        backBg.color = new Color(1f, 1f, 1f, browserBack.Count > 0 && Over(backButton) ? 0.14f : 0f);
        refreshBg.color = new Color(1f, 1f, 1f, Over(refreshButton) ? 0.14f : 0f);
        foreach (var s in sites)
        {
            float a = s == currentSite ? 0.2f : Over(s.bookmark) ? 0.1f : 0f;
            s.bookmarkBg.color = s == currentSite ? new Color(s.accent.r, s.accent.g, s.accent.b, 0.3f) : new Color(1f, 1f, 1f, a);
        }

        float t = (Time.unscaledTime - loadStart) / 0.45f;
        loadBar.sizeDelta = new Vector2(PageW * (1f - (1f - Mathf.Clamp01(t)) * (1f - Mathf.Clamp01(t))), 3f);
        loadBarImage.enabled = t < 1.25f;
        var lc = loadBarImage.color;
        lc.a = t < 1f ? 1f : Mathf.Clamp01(1.25f - t) * 4f;
        loadBarImage.color = lc;

        currentSite.page.Tick();
        if (focused == browserWin)
        {
            float wheel = Input.mouseScrollDelta.y;
            if (Mathf.Abs(wheel) > 0.01f) currentSite.page.Scroll(wheel);
        }
    }

    bool BrowserClick()
    {
        if (currentSite == null) return false;
        if (Over(backButton))
        {
            if (browserBack.Count > 0)
            {
                string id = browserBack[browserBack.Count - 1];
                browserBack.RemoveAt(browserBack.Count - 1);
                Navigate(id, false);
            }
            return true;
        }
        if (Over(refreshButton)) { Navigate(currentSite.id, false); return true; }
        foreach (var s in sites)
            if (Over(s.bookmark)) { Navigate(s.id); return true; }
        return currentSite.page.Click();
    }

    // ------------------------------------------------------------------ small helpers

    static Image RPanel(Transform parent, Color c)
    {
        var img = UIFactory.Panel("Panel", parent, c);
        img.sprite = Round();
        img.type = Image.Type.Sliced;
        return img;
    }

    /// <summary>Anchors a rect to the top-left of its parent: x from the left, y down from the top.</summary>
    static void TopLeft(RectTransform rt, float x, float y, float w, float h)
        => UIFactory.Place(rt, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(x, -y), new Vector2(w, h));

    static TextMeshProUGUI Txt(Transform parent, string text, float size, Color color, TextAlignmentOptions align, Vector2 anchor, Vector2 pivot,
        Vector2 pos, Vector2 box, bool wrap = false, bool bold = false)
    {
        var t = UIFactory.Text("Text", parent, size, color, align);
        t.text = text;
        t.enableWordWrapping = wrap;
        t.overflowMode = TextOverflowModes.Ellipsis; // wrapped text too: never spill out of its box
        if (bold) t.fontStyle = FontStyles.Bold;
        UIFactory.Place(t.rectTransform, anchor, pivot, pos, box);
        return t;
    }

    static TextMeshProUGUI TxtTL(Transform parent, string text, float size, Color color, TextAlignmentOptions align, float x, float y, float w, float h, bool wrap = false, bool bold = false)
        => Txt(parent, text, size, color, align, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(x, -y), new Vector2(w, h), wrap, bold);

    /// <summary>Centred text for a site logo.</summary>
    static void LogoText(RectTransform r, string text, float size, Color color)
    {
        var t = Txt(r, text, size, color, TextAlignmentOptions.Center, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(60, 60), false, true);
        t.overflowMode = TextOverflowModes.Overflow;
    }

    /// <summary>A "<" (dir -1) or ">" (dir +1) made of two bars, centred in a 20x20 box.</summary>
    static void Chevron(RectTransform r, Color c, int dir)
    {
        float up = dir < 0 ? 45f : -45f;
        Shape(r, c, new Vector2(0.8f * dir, 4.2f), new Vector2(12, 3), false, up);
        Shape(r, c, new Vector2(0.8f * dir, -4.2f), new Vector2(12, 3), false, -up);
    }

    // ================================================================== pages

    /// <summary>Placeholder for sites that aren't built yet: the site's own look with a "Coming soon" banner.</summary>
    class ComingSoonPage : SitePage
    {
        protected override void Build(RectTransform r)
        {
            Color ink = site.light ? new Color32(36, 34, 44, 255) : Color.white;
            Color soft = site.light ? new Color32(36, 34, 44, 255) : Color.white;
            var bg = UIFactory.Panel("Background", r, site.bg);
            UIFactory.Fill(bg.rectTransform);
            var glowA = UIFactory.Panel("Glow", r, new Color(site.accent.r, site.accent.g, site.accent.b, site.light ? 0.16f : 0.18f));
            glowA.sprite = Round();
            UIFactory.Place(glowA.rectTransform, new Vector2(0.15f, 0.9f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(820, 560));
            var glowB = UIFactory.Panel("Glow", r, new Color(site.accent.r, site.accent.g, site.accent.b, site.light ? 0.1f : 0.12f));
            glowB.sprite = Round();
            UIFactory.Place(glowB.rectTransform, new Vector2(0.9f, 0.1f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(760, 520));

            // Site header
            var head = UIFactory.Panel("Header", r, site.light ? new Color(1f, 1f, 1f, 0.9f) : new Color(0f, 0f, 0f, 0.35f));
            UIFactory.Place(head.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(PageW, 64));
            var mini = UIFactory.Rect("Logo", head.transform);
            UIFactory.Place(mini, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(40, 0), new Vector2(64, 64));
            mini.localScale = Vector3.one * 0.62f;
            site.logo(mini);
            var wm = Txt(head.transform, site.wordmark, 28, ink, TextAlignmentOptions.MidlineLeft, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(96, 0), new Vector2(300, 40));
            wm.characterSpacing = 2f;
            Txt(head.transform, site.host, 16, new Color(ink.r, ink.g, ink.b, 0.5f), TextAlignmentOptions.MidlineRight, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-40, 0), new Vector2(300, 30));

            // Centre: big logo, name, banner, blurb
            var logo = UIFactory.Rect("Big logo", r);
            UIFactory.Place(logo, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 66), new Vector2(64, 64));
            logo.localScale = Vector3.one * 1.7f;
            site.logo(logo);
            var big = Txt(r, site.wordmark, 56, ink, TextAlignmentOptions.Center, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -22), new Vector2(700, 72));
            big.characterSpacing = 2f;
            var pill = RPanel(r, site.accent);
            UIFactory.Place(pill.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -84), new Vector2(230, 40));
            var pt = Txt(pill.transform, "COMING SOON", 19, site.accent.grayscale > 0.6f ? new Color32(30, 30, 40, 255) : Color.white, TextAlignmentOptions.Center,
                Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, false, true);
            UIFactory.Fill(pt.rectTransform);
            pt.characterSpacing = 3f;
            Txt(r, site.tagline, 22, new Color(soft.r, soft.g, soft.b, 0.75f), TextAlignmentOptions.Top, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 1f), new Vector2(0, -118), new Vector2(720, 76), true);
        }
    }

    // ------------------------------------------------------------------ bank

    /// <summary>Nest Bank: the balance, an account card and the paged transaction history (newest first).</summary>
    class BankPage : SitePage
    {
        const int PerPage = 8;
        const float RowH = 48f, PanelW = 776f;
        static readonly Color Green = new Color32(98, 222, 130, 255), Red = new Color32(255, 112, 112, 255);

        struct Row { public RectTransform rt; public Image bg, icon; public TextMeshProUGUI letter, desc, sub, amount; }

        TextMeshProUGUI balance, moneyIn, moneyOut, pageLabel, emptyTitle, emptyBody, footer;
        RectTransform prevButton, nextButton;
        CanvasGroup prevGroup, nextGroup;
        Image prevBg, nextBg;
        readonly Row[] rows = new Row[PerPage];
        GameObject emptyState;
        int page;

        static void CategoryLook(Wallet.Category c, out string name, out Color colour, out string letter)
        {
            switch (c)
            {
                case Wallet.Category.Allowance: name = "Allowance"; colour = new Color32(255, 196, 70, 255); letter = "A"; break;
                case Wallet.Category.Prize: name = "Prize"; colour = new Color32(90, 160, 255, 255); letter = "R"; break;
                case Wallet.Category.Views: name = "Views"; colour = new Color32(190, 100, 255, 255); letter = "V"; break;
                case Wallet.Category.Tips: name = "Tips"; colour = new Color32(255, 100, 180, 255); letter = "T"; break;
                case Wallet.Category.Job: name = "Job"; colour = new Color32(80, 210, 200, 255); letter = "J"; break;
                default: name = "Purchase"; colour = new Color32(255, 140, 90, 255); letter = "S"; break;
            }
        }

        protected override void Build(RectTransform r)
        {
            var bg = UIFactory.Panel("Background", r, site.bg);
            UIFactory.Fill(bg.rectTransform);
            var glowA = UIFactory.Panel("Glow", r, new Color(0.13f, 0.77f, 0.63f, 0.12f));
            glowA.sprite = Round();
            UIFactory.Place(glowA.rectTransform, new Vector2(0.1f, 0.9f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(800, 560));
            var glowB = UIFactory.Panel("Glow", r, new Color(0.3f, 0.45f, 1f, 0.1f));
            glowB.sprite = Round();
            UIFactory.Place(glowB.rectTransform, new Vector2(0.95f, 0.05f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(760, 520));

            // ---- left column: balance, account card, totals
            var bal = RPanel(r, new Color(1f, 1f, 1f, 0.07f));
            TopLeft(bal.rectTransform, 36, 28, 420, 150);
            TxtTL(bal.transform, "AVAILABLE BALANCE", 14, new Color(1f, 1f, 1f, 0.55f), TextAlignmentOptions.MidlineLeft, 24, 16, 372, 22).characterSpacing = 3f;
            balance = TxtTL(bal.transform, "", 68, Color.white, TextAlignmentOptions.MidlineLeft, 24, 40, 372, 82, false, true);
            balance.enableAutoSizing = true;
            balance.fontSizeMin = 34;
            balance.fontSizeMax = 68;
            TxtTL(bal.transform, "Checking account", 15, new Color(1f, 1f, 1f, 0.5f), TextAlignmentOptions.MidlineLeft, 24, 122, 372, 22);

            var card = RPanel(r, new Color32(24, 168, 144, 255));
            TopLeft(card.rectTransform, 36, 196, 420, 236);
            var sheen = RPanel(card.transform, new Color(1f, 1f, 1f, 0.08f));
            UIFactory.Place(sheen.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-12, 0), new Vector2(190, 212));
            var cardLogo = UIFactory.Rect("Logo", card.transform);
            TopLeft(cardLogo, 22, 18, 64, 64);
            cardLogo.localScale = Vector3.one * 0.55f;
            Dot(cardLogo, Color.white, Vector2.zero, 44);
            LogoText(cardLogo, "$", 32, new Color32(20, 150, 124, 255));
            TxtTL(card.transform, "<b>NEST</b>BANK", 26, Color.white, TextAlignmentOptions.MidlineLeft, 68, 16, 220, 36).characterSpacing = 3f;
            var chip = RPanel(card.transform, new Color32(236, 198, 100, 255));
            TopLeft(chip.rectTransform, 26, 84, 54, 40);
            Shape(chip.rectTransform, new Color32(186, 146, 56, 255), Vector2.zero, new Vector2(54, 2));
            Shape(chip.rectTransform, new Color32(186, 146, 56, 255), Vector2.zero, new Vector2(2, 40));
            TxtTL(card.transform, "****  ****  ****  4821", 26, Color.white, TextAlignmentOptions.MidlineLeft, 26, 138, 380, 36, false, true).characterSpacing = 2f;
            TxtTL(card.transform, "ACCOUNT HOLDER", 11, new Color(1f, 1f, 1f, 0.65f), TextAlignmentOptions.MidlineLeft, 26, 184, 220, 16).characterSpacing = 2f;
            TxtTL(card.transform, "NEST SAVER", 18, Color.white, TextAlignmentOptions.MidlineLeft, 26, 200, 240, 26, false, true).characterSpacing = 2f;
            TxtTL(card.transform, "VALID THRU", 11, new Color(1f, 1f, 1f, 0.65f), TextAlignmentOptions.MidlineRight, 254, 184, 140, 16).characterSpacing = 2f;
            TxtTL(card.transform, "12/29", 18, Color.white, TextAlignmentOptions.MidlineRight, 254, 200, 140, 26, false, true);

            var inBox = RPanel(r, new Color(1f, 1f, 1f, 0.07f));
            TopLeft(inBox.rectTransform, 36, 446, 204, 60);
            TxtTL(inBox.transform, "MONEY IN", 12, new Color(1f, 1f, 1f, 0.5f), TextAlignmentOptions.MidlineLeft, 16, 7, 172, 18).characterSpacing = 2f;
            moneyIn = TxtTL(inBox.transform, "", 22, Green, TextAlignmentOptions.MidlineLeft, 16, 26, 172, 28, false, true);
            var outBox = RPanel(r, new Color(1f, 1f, 1f, 0.07f));
            TopLeft(outBox.rectTransform, 252, 446, 204, 60);
            TxtTL(outBox.transform, "MONEY OUT", 12, new Color(1f, 1f, 1f, 0.5f), TextAlignmentOptions.MidlineLeft, 16, 7, 172, 18).characterSpacing = 2f;
            moneyOut = TxtTL(outBox.transform, "", 22, Red, TextAlignmentOptions.MidlineLeft, 16, 26, 172, 28, false, true);

            // ---- right: transactions
            var panel = RPanel(r, new Color(1f, 1f, 1f, 0.06f));
            TopLeft(panel.rectTransform, 488, 28, PanelW, 478);
            TxtTL(panel.transform, "Transactions", 25, Color.white, TextAlignmentOptions.MidlineLeft, 22, 10, 360, 40, false, true);
            nextBg = RPanel(panel.transform, new Color(1f, 1f, 1f, 0.1f));
            nextButton = nextBg.rectTransform;
            UIFactory.Place(nextButton, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-18, -14), new Vector2(38, 32));
            nextGroup = nextButton.gameObject.AddComponent<CanvasGroup>();
            var na = UIFactory.Rect("Art", nextButton);
            UIFactory.Place(na, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(20, 20));
            Chevron(na, Color.white, 1);
            pageLabel = Txt(panel.transform, "", 17, new Color(1f, 1f, 1f, 0.8f), TextAlignmentOptions.Center, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-62, -14), new Vector2(90, 32));
            prevBg = RPanel(panel.transform, new Color(1f, 1f, 1f, 0.1f));
            prevButton = prevBg.rectTransform;
            UIFactory.Place(prevButton, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-158, -14), new Vector2(38, 32));
            prevGroup = prevButton.gameObject.AddComponent<CanvasGroup>();
            var pa = UIFactory.Rect("Art", prevButton);
            UIFactory.Place(pa, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(20, 20));
            Chevron(pa, Color.white, -1);
            var rule = UIFactory.Panel("Rule", panel.transform, new Color(1f, 1f, 1f, 0.1f));
            TopLeft(rule.rectTransform, 0, 56, PanelW, 2);

            for (int i = 0; i < PerPage; i++)
            {
                var row = new Row();
                row.bg = UIFactory.Panel("Row", panel.transform, new Color(1f, 1f, 1f, 0f));
                row.rt = row.bg.rectTransform;
                TopLeft(row.rt, 0, 60 + i * RowH, PanelW, RowH);
                var line = UIFactory.Panel("Line", row.rt, new Color(1f, 1f, 1f, 0.06f));
                UIFactory.Place(line.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), Vector2.zero, new Vector2(PanelW - 44, 1));
                row.icon = RPanel(row.rt, Color.white);
                TopLeft(row.icon.rectTransform, 22, 7, 34, 34);
                row.letter = Txt(row.icon.transform, "", 17, new Color32(20, 22, 32, 255), TextAlignmentOptions.Center, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, false, true);
                UIFactory.Fill(row.letter.rectTransform);
                row.desc = TxtTL(row.rt, "", 18, Color.white, TextAlignmentOptions.MidlineLeft, 70, 3, 500, 24);
                row.sub = TxtTL(row.rt, "", 13, new Color(1f, 1f, 1f, 0.5f), TextAlignmentOptions.MidlineLeft, 70, 26, 500, 18);
                row.amount = Txt(row.rt, "", 22, Green, TextAlignmentOptions.MidlineRight, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-24, -4), new Vector2(170, 40), false, true);
                rows[i] = row;
            }

            footer = TxtTL(panel.transform, "", 13, new Color(1f, 1f, 1f, 0.4f), TextAlignmentOptions.MidlineLeft, 22, 450, 600, 20);

            emptyState = UIFactory.Rect("Empty", panel.transform).gameObject;
            var er = (RectTransform)emptyState.transform;
            UIFactory.Fill(er, 0, 0, 58, 0);
            var coin = UIFactory.Rect("Coin", er);
            UIFactory.Place(coin, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 70), new Vector2(64, 64));
            Dot(coin, new Color(1f, 1f, 1f, 0.12f), Vector2.zero, 64);
            Dot(coin, new Color(1f, 1f, 1f, 0.12f), Vector2.zero, 44);
            LogoText(coin, "$", 30, new Color(1f, 1f, 1f, 0.5f));
            emptyTitle = Txt(er, "No transactions yet", 26, Color.white, TextAlignmentOptions.Center, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 0), new Vector2(600, 40), false, true);
            emptyBody = Txt(er, "Money you earn or spend will show up here. Mom pays your allowance when the day ends, and ranked matches pay prize money.",
                18, new Color(1f, 1f, 1f, 0.6f), TextAlignmentOptions.Top, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 1f), new Vector2(0, -28), new Vector2(560, 80), true);
        }

        public override void Refresh()
        {
            var list = Wallet.History;
            int pages = Mathf.Max(1, Mathf.CeilToInt(list.Count / (float)PerPage));
            page = Mathf.Clamp(page, 0, pages - 1);

            float inSum = 0f, outSum = 0f;
            foreach (var t in list) { if (t.cents > 0) inSum += t.Dollars; else outSum -= t.Dollars; }
            moneyIn.text = "+" + Wallet.Format(inSum);
            moneyOut.text = "-" + Wallet.Format(outSum);
            balance.text = Wallet.MoneyText;

            bool empty = list.Count == 0;
            emptyState.SetActive(empty);
            pageLabel.gameObject.SetActive(!empty);
            prevButton.gameObject.SetActive(!empty);
            nextButton.gameObject.SetActive(!empty);
            footer.text = empty ? "" : $"Newest first  -  {list.Count} transaction{(list.Count == 1 ? "" : "s")} (last {Wallet.MaxHistory} are kept)";
            pageLabel.text = $"{page + 1} / {pages}";
            prevGroup.alpha = page > 0 ? 1f : 0.3f;
            nextGroup.alpha = page < pages - 1 ? 1f : 0.3f;

            for (int i = 0; i < PerPage; i++)
            {
                int index = page * PerPage + i;
                var row = rows[i];
                row.rt.gameObject.SetActive(index < list.Count);
                if (index >= list.Count) continue;
                var t = list[index];
                CategoryLook(t.category, out string cat, out Color colour, out string letter);
                row.icon.color = colour;
                row.letter.text = letter;
                row.desc.text = t.description;
                row.sub.text = $"{t.day}  -  {cat}";
                bool plus = t.cents >= 0;
                row.amount.color = plus ? Green : Red;
                row.amount.text = (plus ? "+" : "-") + Wallet.Format(Mathf.Abs(t.Dollars));
            }
        }

        public override void Tick()
        {
            balance.text = Wallet.MoneyText;
            prevBg.color = new Color(1f, 1f, 1f, page > 0 && d.Over(prevButton) ? 0.22f : 0.1f);
            nextBg.color = new Color(1f, 1f, 1f, nextGroup.alpha > 0.5f && d.Over(nextButton) ? 0.22f : 0.1f);
            foreach (var row in rows)
                if (row.rt.gameObject.activeSelf) row.bg.color = new Color(1f, 1f, 1f, d.Over(row.rt) ? 0.05f : 0f);
        }

        public override bool Click()
        {
            if (d.Over(prevButton)) { Go(-1); return true; }
            if (d.Over(nextButton)) { Go(1); return true; }
            return false;
        }

        public override void Scroll(float direction)
        {
            if (d.Over(root)) Go(direction > 0f ? -1 : 1);
        }

        void Go(int delta)
        {
            int before = page;
            page += delta;
            Refresh(); // clamps
            if (page != before) Fx.Play(Fx.Click, d.pc ? d.pc.transform.position : Vector3.zero, 0.2f, 1.6f);
        }
    }
}
