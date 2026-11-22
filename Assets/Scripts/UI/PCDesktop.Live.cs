using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// LiveLoop, the Browser's streaming site. Left: the stream setup checklist (the gear ladder, each item's state from
/// ShopNest and what it does, a "Buy on ShopNest" button that switches the Browser to ShopNest). Right: the GO LIVE card
/// (status, explanation, the toggle, the Laggy / OK / Smooth / Pro quality meter) and the paged list of past streams with
/// totals. Everything is driven by <see cref="LiveStream"/>; while live the numbers update every frame.
/// </summary>
public partial class PCDesktop
{
    bool liveDirty = true;
    LivePage livePage;

    void MarkLiveDirty() => liveDirty = true;

    class LivePage : SitePage
    {
        const int PastPerPage = 3;
        const float LeftX = 36f, LeftW = 548f, RightX = 600f, RightW = 664f, TopY = 78f;

        static readonly Color Pink = new Color32(255, 64, 88, 255);
        static readonly Color PinkDark = new Color32(212, 40, 66, 255);
        static readonly Color Violet = new Color32(170, 110, 255, 255);
        static readonly Color Good = new Color32(80, 214, 134, 255);
        static readonly Color Amber = new Color32(255, 190, 72, 255);
        static readonly Color Blue = new Color32(96, 164, 255, 255);
        static readonly Color Muted = new Color(1f, 1f, 1f, 0.55f);
        static readonly Color Soft = new Color(1f, 1f, 1f, 0.07f);
        static readonly Color Disabled = new Color32(74, 80, 96, 255);
        static readonly Color[] TierColours = { new Color32(255, 104, 104, 255), new Color32(255, 190, 72, 255), new Color32(84, 220, 140, 255), new Color32(190, 120, 255, 255) };

        class Gear
        {
            public string id, name, desc, tag;
            public Color colour;
            public int art;
            public RectTransform rt, button;
            public Image bg, tagBg, buttonBg;
            public TextMeshProUGUI price, tagText, buttonText;
            public bool canBuy;
        }

        readonly Gear[] gear =
        {
            new Gear { id = Shop.Webcam, name = "Webcam + mic", art = 0, colour = new Color32(255, 96, 112, 255), tag = "REQUIRED",
                desc = "Required to go live. Starter kit: a laggy picture and only a few viewers." },
            new Gear { id = Shop.GraphicsCard, name = "Graphics card upgrade", art = 1, colour = new Color32(84, 220, 140, 255), tag = "x3 VIEWERS",
                desc = "A smooth, lag-free stream with about three times the viewers." },
            new Gear { id = Shop.RingLight, name = "Ring light + green screen", art = 2, colour = new Color32(255, 190, 72, 255), tag = "VIEWERS STAY",
                desc = "A pro look: more viewers join and they stay watching longer." },
            new Gear { id = Shop.StreamingPC, name = "Streaming PC + capture card", art = 3, colour = new Color32(190, 120, 255, 255), tag = "TOP TIER",
                desc = "The top tier: crisp, lag-free streams and the biggest crowd." },
        };

        class PastRow { public RectTransform rt; public Image bg; public TextMeshProUGUI when, peak, tips, length, quality; }

        TextMeshProUGUI headPill, title, text, hint, stat, statusLine, chatLine, expected, pageLabel, footer, emptyText;
        Image headPillBg, goBg, linkBg, prevBg, nextBg;
        RectTransform goButton, linkButton, prev, next;
        CanvasGroup prevGroup, nextGroup;
        TextMeshProUGUI goText, linkText;
        readonly Image[] meterBars = new Image[4];
        readonly TextMeshProUGUI[] meterLabels = new TextMeshProUGUI[4];
        readonly PastRow[] pastRows = new PastRow[PastPerPage];
        GameObject pastHeader;
        int page;
        // what the go-live card is showing (so text is only rewritten when it changes)
        string shownKey = "", shownStat = "", shownStatus = "", shownChat = "", shownPill = "";
        bool canToggle, blockedByGear;

        protected override void Build(RectTransform r)
        {
            var bg = UIFactory.Panel("Background", r, site.bg);
            UIFactory.Fill(bg.rectTransform);
            var glowA = UIFactory.Panel("Glow", r, new Color(1f, 0.25f, 0.35f, 0.13f));
            glowA.sprite = Round();
            UIFactory.Place(glowA.rectTransform, new Vector2(0.1f, 0.92f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(820, 540));
            var glowB = UIFactory.Panel("Glow", r, new Color(0.55f, 0.35f, 1f, 0.12f));
            glowB.sprite = Round();
            UIFactory.Place(glowB.rectTransform, new Vector2(0.92f, 0.06f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(760, 520));

            BuildHeader(r);
            BuildSetup(r);
            BuildGoLive(r);
            BuildPast(r);
        }

        // ------------------------------------------------------------------ building

        static Image Pill(Transform parent, float x, float y, float w, float h, Color c, float size, Color textColour, out TextMeshProUGUI t)
        {
            var img = RPanel(parent, c);
            TopLeft(img.rectTransform, x, y, w, h);
            t = Txt(img.transform, "", size, textColour, TextAlignmentOptions.Center, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, false, true);
            UIFactory.Fill(t.rectTransform, 4, 4, 0, 0);
            return img;
        }

        void BuildHeader(RectTransform r)
        {
            var head = UIFactory.Panel("Header", r, new Color(0f, 0f, 0f, 0.4f));
            UIFactory.Place(head.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(PageW, 64));
            var line = UIFactory.Panel("Rule", head.transform, new Color(1f, 0.25f, 0.35f, 0.9f));
            UIFactory.Place(line.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), Vector2.zero, new Vector2(PageW, 3));
            var mini = UIFactory.Rect("Logo", head.transform);
            UIFactory.Place(mini, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(40, 0), new Vector2(64, 64));
            mini.localScale = Vector3.one * 0.62f;
            site.logo(mini);
            var wm = Txt(head.transform, site.wordmark, 30, Color.white, TextAlignmentOptions.MidlineLeft, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(96, 0), new Vector2(190, 40));
            wm.characterSpacing = 1f;
            headPillBg = Pill(head.transform, 308, 14, 168, 36, Disabled, 17, Color.white, out headPill);
            Txt(head.transform, "Go live, play, get tips", 17, Muted, TextAlignmentOptions.MidlineRight, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-40, 0), new Vector2(400, 30));
        }

        void BuildSetup(RectTransform r)
        {
            var card = RPanel(r, Soft);
            TopLeft(card.rectTransform, LeftX, TopY, LeftW, 428);
            TxtTL(card.transform, "Stream setup", 24, Color.white, TextAlignmentOptions.MidlineLeft, 20, 8, 300, 36, false, true);
            TxtTL(card.transform, "Your gear decides how many viewers you get", 14, Muted, TextAlignmentOptions.MidlineLeft, 20, 42, 508, 22);
            var rule = UIFactory.Panel("Rule", card.transform, new Color(1f, 1f, 1f, 0.1f));
            TopLeft(rule.rectTransform, 0, 68, LeftW, 2);

            for (int i = 0; i < gear.Length; i++)
            {
                var g = gear[i];
                g.bg = RPanel(card.transform, new Color(1f, 1f, 1f, 0.06f));
                g.rt = g.bg.rectTransform;
                TopLeft(g.rt, 12, 78 + i * 86, LeftW - 24, 80);
                var item = Shop.Find(g.id);
                // The same product picture as on ShopNest, so you can tell what it is at a glance.
                var tile = RPanel(g.rt, item != null ? ShopPage.TintOf(item.category) : g.colour);
                TopLeft(tile.rectTransform, 8, 6, 68, 68);
                tile.gameObject.AddComponent<PcSketchKeep>();
                var art = UIFactory.Rect("Art", tile.rectTransform);
                UIFactory.Place(art, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(128, 128));
                art.localScale = Vector3.one * 0.5f;
                if (item != null) ShopPage.DrawProduct(art, item);
                else DrawGear(art, g.art, g.colour);

                var name = TxtTL(g.rt, g.name, 18, Color.white, TextAlignmentOptions.MidlineLeft, 88, 8, 222, 26, false, true);
                name.enableAutoSizing = true;
                name.fontSizeMin = 13;
                name.fontSizeMax = 18;
                g.price = TxtTL(g.rt, item != null ? "$" + item.price : "", 17, Amber, TextAlignmentOptions.MidlineRight, 312, 8, 56, 26, false, true);
                var desc = TxtTL(g.rt, g.desc, 13, Muted, TextAlignmentOptions.TopLeft, 88, 36, 282, 36, true);
                desc.enableAutoSizing = true;
                desc.fontSizeMin = 11;
                desc.fontSizeMax = 13;
                desc.overflowMode = TextOverflowModes.Ellipsis;

                var tagCol = new Color(g.colour.r, g.colour.g, g.colour.b, 0.2f);
                g.tagBg = Pill(g.rt, 382, 9, 130, 22, tagCol, 11, g.colour, out g.tagText);
                g.tagText.text = g.tag;
                g.tagText.characterSpacing = 1.5f;
                g.buttonBg = Pill(g.rt, 382, 40, 130, 32, Pink, 14, Color.white, out g.buttonText);
                g.button = g.buttonBg.rectTransform;
            }
        }

        /// <summary>Little pictures on the gear tiles (46x46, drawn on the tile colour).</summary>
        static void DrawGear(RectTransform a, int kind, Color tile)
        {
            Color dark = new Color(tile.r * 0.45f, tile.g * 0.45f, tile.b * 0.45f, 1f);
            Color white = new Color(1f, 1f, 1f, 0.96f);
            switch (kind)
            {
                case 0: // webcam on a stand
                    Dot(a, white, new Vector2(0, 5), 26);
                    Dot(a, dark, new Vector2(0, 5), 17);
                    Dot(a, white, new Vector2(0, 5), 7);
                    Shape(a, white, new Vector2(0, -12), new Vector2(8, 7));
                    Shape(a, white, new Vector2(0, -16), new Vector2(24, 4), true);
                    break;
                case 1: // graphics card with two fans
                    Shape(a, white, new Vector2(0, 3), new Vector2(36, 22), true);
                    Dot(a, dark, new Vector2(-8, 3), 14);
                    Dot(a, white, new Vector2(-8, 3), 5);
                    Dot(a, dark, new Vector2(8, 3), 14);
                    Dot(a, white, new Vector2(8, 3), 5);
                    Shape(a, white, new Vector2(0, -12), new Vector2(32, 4));
                    break;
                case 2: // ring light
                    Dot(a, white, new Vector2(0, 3), 32);
                    Dot(a, tile, new Vector2(0, 3), 21);
                    Shape(a, white, new Vector2(0, -16), new Vector2(12, 5), true);
                    break;
                default: // PC tower + capture card
                    Shape(a, white, new Vector2(-4, 0), new Vector2(20, 34), true);
                    Shape(a, dark, new Vector2(-4, 9), new Vector2(12, 2));
                    Dot(a, dark, new Vector2(-4, -7), 6);
                    Shape(a, white, new Vector2(12, -4), new Vector2(11, 15), true);
                    Shape(a, dark, new Vector2(12, -2), new Vector2(5, 5), true);
                    break;
            }
        }

        void BuildGoLive(RectTransform r)
        {
            var card = RPanel(r, Soft);
            TopLeft(card.rectTransform, RightX, TopY, RightW, 196);

            title = TxtTL(card.transform, "", 26, Color.white, TextAlignmentOptions.MidlineLeft, 22, 12, 408, 36, false, true);
            title.enableAutoSizing = true;
            title.fontSizeMin = 18;
            title.fontSizeMax = 26;
            text = TxtTL(card.transform, "", 15, new Color(1f, 1f, 1f, 0.72f), TextAlignmentOptions.TopLeft, 22, 52, 408, 70, true);
            text.enableAutoSizing = true;
            text.fontSizeMin = 12;
            text.fontSizeMax = 15;
            text.overflowMode = TextOverflowModes.Ellipsis;
            stat = TxtTL(card.transform, "", 17, Color.white, TextAlignmentOptions.MidlineLeft, 22, 52, 408, 24, false, true);
            statusLine = TxtTL(card.transform, "", 14, Muted, TextAlignmentOptions.TopLeft, 22, 78, 408, 22);
            chatLine = TxtTL(card.transform, "", 14, new Color(1f, 1f, 1f, 0.85f), TextAlignmentOptions.MidlineLeft, 22, 100, 408, 22);

            goBg = RPanel(card.transform, Pink);
            goButton = goBg.rectTransform;
            TopLeft(goButton, 446, 16, 196, 56);
            goText = Txt(goBg.transform, "GO LIVE", 24, Color.white, TextAlignmentOptions.Center, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, false, true);
            UIFactory.Fill(goText.rectTransform, 6, 6, 0, 0);
            goText.characterSpacing = 3f;
            linkBg = RPanel(card.transform, new Color(1f, 1f, 1f, 0.12f));
            linkButton = linkBg.rectTransform;
            TopLeft(linkButton, 446, 80, 196, 34);
            linkText = Txt(linkBg.transform, "Buy on ShopNest", 15, Color.white, TextAlignmentOptions.Center, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, false, true);
            UIFactory.Fill(linkText.rectTransform, 6, 6, 0, 0);
            hint = TxtTL(card.transform, "", 12, Muted, TextAlignmentOptions.TopLeft, 446, 80, 196, 38, true);
            hint.overflowMode = TextOverflowModes.Ellipsis;

            // quality meter
            var label = TxtTL(card.transform, "STREAM QUALITY", 12, Muted, TextAlignmentOptions.MidlineLeft, 22, 128, 200, 20, false, true);
            label.characterSpacing = 3f;
            expected = Txt(card.transform, "", 13, new Color(1f, 1f, 1f, 0.7f), TextAlignmentOptions.MidlineRight, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-22, -128), new Vector2(420, 20));
            string[] names = { "Laggy", "OK", "Smooth", "Pro" };
            float segW = (RightW - 44f - 3 * 8f) / 4f;
            for (int i = 0; i < 4; i++)
            {
                float x = 22 + i * (segW + 8f);
                meterBars[i] = RPanel(card.transform, new Color(1f, 1f, 1f, 0.1f));
                TopLeft(meterBars[i].rectTransform, x, 152, segW, 14);
                meterLabels[i] = TxtTL(card.transform, names[i], 13, Muted, TextAlignmentOptions.Center, x, 168, segW, 20);
            }
        }

        void BuildPast(RectTransform r)
        {
            const float y0 = TopY + 206f; // 284
            var card = RPanel(r, Soft);
            TopLeft(card.rectTransform, RightX, y0, RightW, 222);
            TxtTL(card.transform, "Past streams", 22, Color.white, TextAlignmentOptions.MidlineLeft, 22, 8, 260, 34, false, true);

            nextBg = RPanel(card.transform, new Color(1f, 1f, 1f, 0.1f));
            next = nextBg.rectTransform;
            UIFactory.Place(next, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-16, -9), new Vector2(36, 30));
            nextGroup = next.gameObject.AddComponent<CanvasGroup>();
            var na = UIFactory.Rect("Art", next);
            UIFactory.Place(na, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(20, 20));
            Chevron(na, Color.white, 1);
            pageLabel = Txt(card.transform, "", 16, new Color(1f, 1f, 1f, 0.8f), TextAlignmentOptions.Center, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-54, -9), new Vector2(80, 30));
            prevBg = RPanel(card.transform, new Color(1f, 1f, 1f, 0.1f));
            prev = prevBg.rectTransform;
            UIFactory.Place(prev, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-136, -9), new Vector2(36, 30));
            prevGroup = prev.gameObject.AddComponent<CanvasGroup>();
            var pa = UIFactory.Rect("Art", prev);
            UIFactory.Place(pa, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(20, 20));
            Chevron(pa, Color.white, -1);

            pastHeader = UIFactory.Rect("Columns", card.transform).gameObject;
            var ph = (RectTransform)pastHeader.transform;
            TopLeft(ph, 0, 46, RightW, 20);
            ColumnLabel(ph, "WHEN", 22, 260, TextAlignmentOptions.MidlineLeft);
            ColumnLabel(ph, "PEAK", 292, 90, TextAlignmentOptions.MidlineLeft);
            ColumnLabel(ph, "TIPS", 392, 90, TextAlignmentOptions.MidlineLeft);
            ColumnLabel(ph, "LENGTH", 490, 80, TextAlignmentOptions.MidlineLeft);
            ColumnLabel(ph, "QUALITY", 574, 80, TextAlignmentOptions.MidlineLeft);

            for (int i = 0; i < PastPerPage; i++)
            {
                var row = new PastRow();
                row.bg = UIFactory.Panel("Row", card.transform, new Color(1f, 1f, 1f, 0f));
                row.rt = row.bg.rectTransform;
                TopLeft(row.rt, 0, 68 + i * 36, RightW, 36);
                var sep = UIFactory.Panel("Line", row.rt, new Color(1f, 1f, 1f, 0.07f));
                UIFactory.Place(sep.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), Vector2.zero, new Vector2(RightW - 44, 1));
                row.when = TxtTL(row.rt, "", 16, Color.white, TextAlignmentOptions.MidlineLeft, 22, 0, 262, 36);
                row.peak = TxtTL(row.rt, "", 16, Color.white, TextAlignmentOptions.MidlineLeft, 292, 0, 92, 36, false, true);
                row.tips = TxtTL(row.rt, "", 16, Amber, TextAlignmentOptions.MidlineLeft, 392, 0, 92, 36, false, true);
                row.length = TxtTL(row.rt, "", 16, new Color(1f, 1f, 1f, 0.85f), TextAlignmentOptions.MidlineLeft, 490, 0, 80, 36);
                row.quality = TxtTL(row.rt, "", 15, Color.white, TextAlignmentOptions.MidlineLeft, 574, 0, 80, 36, false, true);
                pastRows[i] = row;
            }

            emptyText = Txt(card.transform, "No streams yet. Get a webcam + mic, press GO LIVE and play a match!", 16, Muted, TextAlignmentOptions.Center,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -92), new Vector2(520, 56), true);
            emptyText.overflowMode = TextOverflowModes.Ellipsis;

            var rule = UIFactory.Panel("Rule", card.transform, new Color(1f, 1f, 1f, 0.1f));
            TopLeft(rule.rectTransform, 0, 178, RightW, 2);
            footer = TxtTL(card.transform, "", 15, new Color(1f, 1f, 1f, 0.8f), TextAlignmentOptions.MidlineLeft, 22, 184, RightW - 44, 30);
        }

        static void ColumnLabel(RectTransform parent, string s, float x, float w, TextAlignmentOptions align)
        {
            var t = TxtTL(parent, s, 12, new Color(1f, 1f, 1f, 0.4f), align, x, 0, w, 20, false, true);
            t.characterSpacing = 2f;
        }

        // ------------------------------------------------------------------ data -> widgets

        public override void Refresh()
        {
            // gear rows
            foreach (var g in gear)
            {
                var st = Shop.StateOf(g.id);
                var item = Shop.Find(g.id);
                g.canBuy = st == Shop.State.NotOwned;
                g.button.gameObject.SetActive(true);
                switch (st)
                {
                    case Shop.State.Installed: g.buttonBg.color = new Color(Good.r, Good.g, Good.b, 0.25f); g.buttonText.color = Good; g.buttonText.text = "Installed"; break;
                    case Shop.State.Delivered: g.buttonBg.color = new Color(Amber.r, Amber.g, Amber.b, 0.25f); g.buttonText.color = Amber; g.buttonText.text = "Open the box!"; break;
                    case Shop.State.Ordered: g.buttonBg.color = new Color(Blue.r, Blue.g, Blue.b, 0.25f); g.buttonText.color = Blue; g.buttonText.text = "Arriving tomorrow"; break;
                    default: g.buttonBg.color = Pink; g.buttonText.color = Color.white; g.buttonText.text = "Buy on ShopNest"; break;
                }
                if (item != null) g.price.text = "$" + item.price;
                g.price.alpha = st == Shop.State.NotOwned ? 1f : 0.35f;
            }

            RefreshPast();
            shownKey = ""; // force the go-live card to redraw
            UpdateLive();
        }

        void RefreshPast()
        {
            var list = LiveStream.PastStreams;
            int pages = Mathf.Max(1, Mathf.CeilToInt(list.Count / (float)PastPerPage));
            page = Mathf.Clamp(page, 0, pages - 1);
            bool empty = list.Count == 0;
            emptyText.gameObject.SetActive(empty);
            pastHeader.SetActive(!empty);
            pageLabel.gameObject.SetActive(!empty);
            prev.gameObject.SetActive(!empty);
            next.gameObject.SetActive(!empty);
            pageLabel.text = $"{page + 1} / {pages}";
            prevGroup.alpha = page > 0 ? 1f : 0.3f;
            nextGroup.alpha = page < pages - 1 ? 1f : 0.3f;
            for (int i = 0; i < PastPerPage; i++)
            {
                int index = page * PastPerPage + i;
                var row = pastRows[i];
                row.rt.gameObject.SetActive(index < list.Count);
                if (index >= list.Count) continue;
                var p = list[index];
                row.when.text = p.day;
                row.peak.text = p.peak.ToString("N0");
                row.tips.text = "$" + (p.tipCents / 100f).ToString("0.00");
                row.length.text = LiveStream.FormatTime(p.seconds);
                var q = (LiveStream.Quality)Mathf.Clamp(p.quality, 0, 3);
                row.quality.text = LiveStream.QualityName(q);
                row.quality.color = TierColours[(int)q];
            }
            int n = LiveStream.TotalStreams;
            footer.text = n == 0 ? "Totals will show up here after your first stream."
                : $"<b>{n}</b> stream{(n == 1 ? "" : "s")}   -   <color=#FFBE48><b>${(LiveStream.TotalTipCents / 100f).ToString("0.00")}</b></color> in tips   -   best peak <b>{LiveStream.BestPeak:N0}</b> viewers";
        }

        /// <summary>The go-live card, the quality meter and the header pill. Cheap: only rewrites text that changed.</summary>
        void UpdateLive()
        {
            bool live = LiveStream.IsLive;
            string why = live ? null : LiveStream.CannotGoLive();
            bool banned = LiveStream.BannedToday;
            blockedByGear = why != null && !banned;
            canToggle = live || why == null;
            var q = LiveStream.CurrentQuality;
            bool hasCam = LiveStream.HasCamera;

            string key = live ? "live" : banned ? "banned" : why != null ? "locked:" + why : "ready";
            key += "|" + (hasCam ? (int)q : -1) + "|" + ClipHub.Followers;
            if (key != shownKey)
            {
                shownKey = key;
                stat.gameObject.SetActive(live);
                statusLine.gameObject.SetActive(live);
                chatLine.gameObject.SetActive(live);
                text.gameObject.SetActive(!live);
                linkButton.gameObject.SetActive(blockedByGear);
                hint.gameObject.SetActive(!blockedByGear);
                if (live)
                {
                    title.text = "You are <color=#FF4058>LIVE</color>";
                    hint.text = "Ends when the match is over or the day ends.";
                }
                else if (banned)
                {
                    title.text = "<color=#FF7070>Grounded - no streaming today</color>";
                    text.text = why;
                    hint.text = "";
                }
                else if (why != null)
                {
                    title.text = "Streaming is locked";
                    text.text = why;
                }
                else
                {
                    title.text = "You are offline";
                    text.text = "Press GO LIVE, then put on the VR headset and play. Viewers join while you play, tip when you do well and leave fast if you take the headset off.";
                    hint.text = "Better gear means more viewers and more tips.";
                }
                goBg.color = canToggle ? (live ? new Color32(60, 66, 84, 255) : Pink) : Disabled;
                goText.text = live ? "END STREAM" : "GO LIVE";
                goText.color = canToggle ? Color.white : new Color(1f, 1f, 1f, 0.45f);

                for (int i = 0; i < 4; i++)
                {
                    bool on = hasCam && i <= (int)q;
                    var c = TierColours[i];
                    meterBars[i].color = on ? c : new Color(1f, 1f, 1f, 0.1f);
                    bool current = hasCam && i == (int)q;
                    meterLabels[i].color = current ? c : Muted;
                    meterLabels[i].fontStyle = current ? FontStyles.Bold : FontStyles.Normal;
                }
                if (!hasCam) expected.text = "Needs a webcam + mic";
                else
                {
                    LiveStream.ExpectedRange(out int low, out int high);
                    int f = ClipHub.Followers;
                    expected.text = $"Expected: {low}-{high} viewers   -   {f} ClipHub follower{(f == 1 ? "" : "s")} help";
                }
            }

            if (live)
            {
                string s = $"{LiveStream.Viewers:N0} viewers   -   ${LiveStream.TipsTotal} tips   -   peak {LiveStream.Peak:N0}   -   {LiveStream.FormatTime(LiveStream.Elapsed)}";
                if (s != shownStat) { shownStat = s; stat.text = s; }
                string st = LiveStream.Status == LiveStream.Mode.Playing ? "<color=#54DC8C>You are in the match - viewers are watching.</color>"
                    : LiveStream.Status == LiveStream.Mode.Away ? "<color=#FF7070>Headset off - viewers are leaving! Put it back on.</color>"
                    : "Waiting for you to play - put on the headset to get viewers.";
                if (st != shownStatus) { shownStatus = st; statusLine.text = st; }
                string chat = "";
                var lines = LiveStream.ChatLines;
                if (lines.Count > 0)
                {
                    var c = lines[lines.Count - 1];
                    chat = Time.time - c.time < LiveStream.ChatLife ? $"<color=#{ColorUtility.ToHtmlStringRGB(c.colour)}><b>{c.name}</b></color>  {c.text}" : "";
                }
                if (chat != shownChat) { shownChat = chat; chatLine.text = chat; }
            }

            string pill = live ? "LIVE  " + LiveStream.FormatTime(LiveStream.Elapsed) : "OFFLINE";
            if (pill != shownPill)
            {
                shownPill = pill;
                headPill.text = pill;
                headPillBg.color = live ? Pink : Disabled;
            }
        }

        // ------------------------------------------------------------------ input

        public override void Tick()
        {
            UpdateLive();
            bool hoverGo = canToggle && d.Over(goButton);
            if (canToggle) goBg.color = LiveStream.IsLive ? (hoverGo ? new Color32(84, 90, 112, 255) : new Color32(60, 66, 84, 255)) : (hoverGo ? PinkDark : Pink);
            if (linkButton.gameObject.activeSelf) linkBg.color = new Color(1f, 1f, 1f, d.Over(linkButton) ? 0.22f : 0.12f);
            foreach (var g in gear)
            {
                g.bg.color = new Color(1f, 1f, 1f, d.Over(g.rt) ? 0.09f : 0.06f);
                if (g.canBuy) g.buttonBg.color = d.Over(g.button) ? PinkDark : Pink;
            }
            prevBg.color = new Color(1f, 1f, 1f, page > 0 && d.Over(prev) ? 0.22f : 0.1f);
            nextBg.color = new Color(1f, 1f, 1f, nextGroup.alpha > 0.5f && d.Over(next) ? 0.22f : 0.1f);
            foreach (var row in pastRows)
                if (row.rt.gameObject.activeSelf) row.bg.color = new Color(1f, 1f, 1f, d.Over(row.rt) ? 0.05f : 0f);
        }

        public override bool Click()
        {
            if (d.Over(goButton))
            {
                if (canToggle) LiveStream.Toggle();
                else GameHUD.Toast(LiveStream.BannedToday ? "Grounded - no streaming today." : "You need a webcam + mic to go live.", GameHUD.Warn);
                Refresh();
                Fx.Play(Fx.Click, d.pc ? d.pc.transform.position : Vector3.zero, 0.3f, 1.3f);
                return true;
            }
            if (linkButton.gameObject.activeSelf && d.Over(linkButton)) { d.Navigate("shop"); return true; }
            foreach (var g in gear)
                if (d.Over(g.button)) { if (g.canBuy) d.Navigate("shop"); return true; }
            if (d.Over(prev)) { Go(-1); return true; }
            if (d.Over(next)) { Go(1); return true; }
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
            RefreshPast(); // clamps
            if (page != before) Fx.Play(Fx.Click, d.pc ? d.pc.transform.position : Vector3.zero, 0.2f, 1.6f);
        }
    }
}
