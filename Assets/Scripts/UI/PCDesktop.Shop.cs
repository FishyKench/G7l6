using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// ShopNest, the Browser's online shop: three categories (Home gadgets, Streaming gear, PC upgrades), product cards with
/// a little drawn picture, a one-line description, the price and a Buy button. Buying pays from the wallet and puts
/// the order on its way: it arrives at the front door the next morning (see <see cref="Shop"/> / ShopRuntime).
/// A card shows its state instead of the button once you own it: Arriving tomorrow / Delivered / Installed.
/// </summary>
public partial class PCDesktop
{
    bool shopDirty = true;
    ShopPage shopPage;

    void MarkShopDirty() => shopDirty = true;

    class ShopPage : SitePage
    {
        const int Slots = 4;
        const float CardW = 292f, CardH = 326f, CardGap = 20f, CardsX = 36f, CardsY = 126f;

        static readonly Color Ink = new Color32(40, 34, 44, 255);
        static readonly Color Orange = new Color32(255, 140, 40, 255);
        static readonly Color OrangeDark = new Color32(214, 100, 14, 255);
        static readonly Color Muted = new Color32(120, 108, 112, 255);
        static readonly Color Disabled = new Color32(206, 198, 192, 255);
        static readonly Color Good = new Color32(46, 160, 92, 255);
        static readonly Color Bad = new Color32(206, 52, 56, 255);

        class Slot
        {
            public RectTransform rt, art, button;
            public GameObject shadow;
            public Image pic, buttonBg;
            public TextMeshProUGUI name, blurb, price, buttonText;
            public Shop.Item item;
            public string artFor, shownButton;
        }

        readonly Slot[] slots = new Slot[Slots];
        readonly RectTransform[] tabs = new RectTransform[3];
        readonly Image[] tabBg = new Image[3];
        readonly TextMeshProUGUI[] tabText = new TextMeshProUGUI[3];
        TextMeshProUGUI wallet, orders, pageLabel, message;
        Image messageBg;
        RectTransform prev, next;
        CanvasGroup prevGroup, nextGroup;
        Image prevBg, nextBg;
        Shop.Category category = Shop.Category.Home;
        int page;
        float messageUntil;
        List<Shop.Item> shown = new List<Shop.Item>();

        static readonly Shop.Category[] cats = { Shop.Category.Home, Shop.Category.Streaming, Shop.Category.PC };

        const string DefaultTip = "Orders arrive at the front door tomorrow morning. Open the box (E) to install it.";

        public static Color TintOf(Shop.Category c) => Tint(c);

        static Color Tint(Shop.Category c)
        {
            switch (c)
            {
                case Shop.Category.Streaming: return new Color32(200, 182, 242, 255);
                case Shop.Category.PC: return new Color32(162, 206, 238, 255);
                default: return new Color32(248, 196, 148, 255);
            }
        }

        protected override void Build(RectTransform r)
        {
            var bg = UIFactory.Panel("Background", r, site.bg);
            UIFactory.Fill(bg.rectTransform);
            var glowA = UIFactory.Panel("Glow", r, new Color(1f, 0.55f, 0.16f, 0.13f));
            glowA.sprite = Round();
            UIFactory.Place(glowA.rectTransform, new Vector2(0.12f, 0.95f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(820, 520));
            var glowB = UIFactory.Panel("Glow", r, new Color(1f, 0.7f, 0.3f, 0.1f));
            glowB.sprite = Round();
            UIFactory.Place(glowB.rectTransform, new Vector2(0.92f, 0.05f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(760, 480));

            BuildHeader(r);
            BuildTabs(r);

            for (int i = 0; i < Slots; i++) slots[i] = BuildCard(r, i);

            // Message strip along the bottom
            messageBg = RPanel(r, new Color(1f, 1f, 1f, 0.9f));
            TopLeft(messageBg.rectTransform, CardsX, 466, PageW - 2 * CardsX, 44);
            message = TxtTL(messageBg.transform, DefaultTip, 17, Muted, TextAlignmentOptions.MidlineLeft, 18, 0, PageW - 2 * CardsX - 36, 44);
        }

        void BuildHeader(RectTransform r)
        {
            var head = UIFactory.Panel("Header", r, new Color(1f, 1f, 1f, 0.93f));
            UIFactory.Place(head.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(PageW, 64));
            var line = UIFactory.Panel("Rule", head.transform, new Color(1f, 0.55f, 0.16f, 0.9f));
            UIFactory.Place(line.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), Vector2.zero, new Vector2(PageW, 3));
            var mini = UIFactory.Rect("Logo", head.transform);
            UIFactory.Place(mini, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(40, 0), new Vector2(64, 64));
            mini.localScale = Vector3.one * 0.62f;
            site.logo(mini);
            var wm = Txt(head.transform, site.wordmark, 30, Ink, TextAlignmentOptions.MidlineLeft, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(96, 0), new Vector2(210, 40));
            wm.characterSpacing = 1f;

            // "Free next-morning delivery" badge with a little van
            var badge = RPanel(head.transform, new Color(1f, 0.55f, 0.16f, 0.16f));
            TopLeft(badge.rectTransform, 330, 14, 330, 36);
            TxtTL(badge.transform, "Free next-morning delivery", 17, OrangeDark, TextAlignmentOptions.Center, 12, 0, 306, 36, false, true);

            // Orders summary and wallet on the right
            var walletBox = RPanel(head.transform, new Color(Ink.r, Ink.g, Ink.b, 0.08f));
            TopLeft(walletBox.rectTransform, PageW - 36 - 190, 10, 190, 44);
            TxtTL(walletBox.transform, "WALLET", 11, Muted, TextAlignmentOptions.MidlineLeft, 14, 3, 70, 16).characterSpacing = 2f;
            wallet = TxtTL(walletBox.transform, "", 22, Ink, TextAlignmentOptions.MidlineLeft, 14, 17, 166, 26, false, true);

            var ordersBox = RPanel(head.transform, new Color(Ink.r, Ink.g, Ink.b, 0.08f));
            TopLeft(ordersBox.rectTransform, PageW - 36 - 190 - 12 - 330, 10, 330, 44);
            TxtTL(ordersBox.transform, "YOUR ORDERS", 11, Muted, TextAlignmentOptions.MidlineLeft, 14, 3, 120, 16).characterSpacing = 2f;
            orders = TxtTL(ordersBox.transform, "", 16, Ink, TextAlignmentOptions.MidlineLeft, 14, 17, 302, 26, false, true);
        }

        void BuildTabs(RectTransform r)
        {
            for (int i = 0; i < 3; i++)
            {
                tabBg[i] = RPanel(r, Color.white);
                tabs[i] = tabBg[i].rectTransform;
                TopLeft(tabs[i], CardsX + i * 204f, 76, 194, 38);
                tabText[i] = TxtTL(tabs[i], "", 17, Ink, TextAlignmentOptions.Center, 0, 0, 194, 38, false, true);
                tabText[i].text = Shop.CategoryName(cats[i]);
            }

            nextBg = RPanel(r, new Color(Ink.r, Ink.g, Ink.b, 0.1f));
            next = nextBg.rectTransform;
            TopLeft(next, PageW - CardsX - 38, 78, 38, 34);
            nextGroup = next.gameObject.AddComponent<CanvasGroup>();
            var na = UIFactory.Rect("Art", next);
            UIFactory.Place(na, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(20, 20));
            Chevron(na, Ink, 1);
            pageLabel = TxtTL(r, "", 17, Ink, TextAlignmentOptions.Center, PageW - CardsX - 38 - 96, 78, 90, 34, false, true);
            prevBg = RPanel(r, new Color(Ink.r, Ink.g, Ink.b, 0.1f));
            prev = prevBg.rectTransform;
            TopLeft(prev, PageW - CardsX - 38 - 96 - 44, 78, 38, 34);
            prevGroup = prev.gameObject.AddComponent<CanvasGroup>();
            var pa = UIFactory.Rect("Art", prev);
            UIFactory.Place(pa, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(20, 20));
            Chevron(pa, Ink, -1);
        }

        Slot BuildCard(RectTransform r, int i)
        {
            var s = new Slot();
            float x = CardsX + i * (CardW + CardGap);
            var shadow = RPanel(r, new Color(0.3f, 0.18f, 0.05f, 0.1f));
            TopLeft(shadow.rectTransform, x + 1, CardsY + 5, CardW, CardH);
            var card = RPanel(r, Color.white);
            TopLeft(card.rectTransform, x, CardsY, CardW, CardH);
            s.rt = card.rectTransform;
            s.shadow = shadow.gameObject;

            s.pic = RPanel(card.transform, Color.white);
            TopLeft(s.pic.rectTransform, 10, 10, CardW - 20, 128);
            s.pic.gameObject.AddComponent<PcSketchKeep>(); // products keep their real colours on a coloured backdrop
            s.art = UIFactory.Rect("Art", s.pic.transform);
            UIFactory.Place(s.art, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(128, 128));

            s.name = TxtTL(card.transform, "", 19, Ink, TextAlignmentOptions.TopLeft, 16, 146, CardW - 32, 52, true, true);
            s.blurb = TxtTL(card.transform, "", 14, new Color(Ink.r, Ink.g, Ink.b, 0.72f), TextAlignmentOptions.TopLeft, 16, 198, CardW - 32, 70, true);
            s.price = TxtTL(card.transform, "", 29, OrangeDark, TextAlignmentOptions.MidlineLeft, 16, 276, 130, 40, false, true);

            s.buttonBg = RPanel(card.transform, Orange);
            s.button = s.buttonBg.rectTransform;
            TopLeft(s.button, CardW - 16 - 130, 276, 130, 40);
            s.buttonText = Txt(s.button, "Buy", 17, Color.white, TextAlignmentOptions.Center, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, false, true);
            UIFactory.Fill(s.buttonText.rectTransform, 6, 6, 0, 0);
            s.buttonText.enableAutoSizing = true;
            s.buttonText.fontSizeMin = 11;
            s.buttonText.fontSizeMax = 17;
            return s;
        }

        // ------------------------------------------------------------------ data -> UI

        public override void Refresh()
        {
            shown = new List<Shop.Item>();
            foreach (var item in Shop.Items) if (item.category == category) shown.Add(item);
            int pages = Mathf.Max(1, Mathf.CeilToInt(shown.Count / (float)Slots));
            page = Mathf.Clamp(page, 0, pages - 1);
            pageLabel.text = $"{page + 1} / {pages}";
            prev.gameObject.SetActive(pages > 1);
            next.gameObject.SetActive(pages > 1);
            pageLabel.gameObject.SetActive(pages > 1);
            prevGroup.alpha = page > 0 ? 1f : 0.3f;
            nextGroup.alpha = page < pages - 1 ? 1f : 0.3f;

            for (int i = 0; i < 3; i++)
            {
                int count = 0;
                foreach (var item in Shop.Items) if (item.category == cats[i]) count++;
                tabText[i].text = $"{Shop.CategoryName(cats[i])} ({count})";
            }

            for (int i = 0; i < Slots; i++)
            {
                var s = slots[i];
                int index = page * Slots + i;
                s.rt.gameObject.SetActive(index < shown.Count);
                s.shadow.SetActive(index < shown.Count);
                if (index >= shown.Count) { s.item = null; continue; }
                var item = shown[index];
                s.item = item;
                s.pic.color = Tint(item.category);
                if (s.artFor != item.id) DrawArt(s, item);
                s.name.text = item.name;
                s.blurb.text = item.blurb;
                // The description sits right under the name, whether that wraps to one line or two.
                float nameHeight = Mathf.Clamp(s.name.GetPreferredValues(item.name, CardW - 32f, 0f).y, 24f, 52f);
                TopLeft(s.blurb.rectTransform, 16, 146 + nameHeight + 4, CardW - 32, 70);

                var state = Shop.StateOf(item.id);
                s.shownButton = null;
                if (state == Shop.State.NotOwned)
                {
                    s.price.gameObject.SetActive(true);
                    s.price.text = Wallet.Format(item.price);
                    TopLeft(s.button, CardW - 16 - 130, 276, 130, 40);
                }
                else
                {
                    s.price.gameObject.SetActive(false);
                    TopLeft(s.button, 16, 276, CardW - 32, 40);
                    switch (state)
                    {
                        case Shop.State.Ordered: SetButton(s, "Arriving tomorrow", new Color32(255, 196, 70, 255), Ink); break;
                        case Shop.State.Delivered: SetButton(s, "Delivered - open the box", new Color32(120, 190, 255, 255), Ink); break;
                        default: SetButton(s, "Installed", Good, Color.white); break;
                    }
                }
            }

            RefreshOrders();
            wallet.text = Wallet.MoneyText;
        }

        static void SetButton(Slot s, string text, Color bg, Color fg)
        {
            s.buttonBg.color = bg;
            s.buttonText.text = text;
            s.buttonText.color = fg;
        }

        void RefreshOrders()
        {
            int ordered = Shop.Count(Shop.State.Ordered), delivered = Shop.Count(Shop.State.Delivered);
            if (ordered == 0 && delivered == 0) orders.text = "Nothing on its way";
            else if (delivered == 0) orders.text = $"{ordered} arriving tomorrow";
            else if (ordered == 0) orders.text = $"{delivered} box{(delivered == 1 ? "" : "es")} at the front door";
            else orders.text = $"{ordered} arriving tomorrow, {delivered} at the door";
        }

        void Say(string text, Color color)
        {
            message.text = text;
            message.color = color;
            messageBg.color = new Color(color.r, color.g, color.b, 0.14f);
            messageUntil = Time.unscaledTime + 5f;
        }

        public override void Tick()
        {
            wallet.text = Wallet.MoneyText;
            if (messageUntil > 0f && Time.unscaledTime > messageUntil)
            {
                messageUntil = 0f;
                message.text = DefaultTip;
                message.color = Muted;
                messageBg.color = new Color(1f, 1f, 1f, 0.9f);
            }

            for (int i = 0; i < 3; i++)
            {
                bool on = cats[i] == category;
                tabBg[i].color = on ? Orange : d.Over(tabs[i]) ? new Color32(255, 232, 205, 255) : Color.white;
                tabText[i].color = on ? Color.white : Ink;
            }
            prevBg.color = new Color(Ink.r, Ink.g, Ink.b, page > 0 && d.Over(prev) ? 0.2f : 0.1f);
            nextBg.color = new Color(Ink.r, Ink.g, Ink.b, nextGroup.alpha > 0.5f && d.Over(next) ? 0.2f : 0.1f);

            foreach (var s in slots)
            {
                if (s.item == null || Shop.StateOf(s.item.id) != Shop.State.NotOwned) continue;
                float need = s.item.price - Wallet.Money;
                bool can = need <= 0.001f;
                string label = can ? "Buy" : "Need " + Wallet.Format(need) + " more";
                if (s.shownButton != label) { s.shownButton = label; s.buttonText.text = label; }
                s.buttonText.color = can ? Color.white : new Color32(110, 100, 100, 255);
                s.buttonBg.color = !can ? Disabled : d.Over(s.button) ? OrangeDark : Orange;
            }
        }

        public override bool Click()
        {
            for (int i = 0; i < 3; i++)
                if (d.Over(tabs[i]))
                {
                    if (category != cats[i]) { category = cats[i]; page = 0; Refresh(); Click1(); }
                    return true;
                }
            if (prev.gameObject.activeInHierarchy && d.Over(prev)) { Go(-1); return true; }
            if (next.gameObject.activeInHierarchy && d.Over(next)) { Go(1); return true; }

            foreach (var s in slots)
            {
                if (s.item == null || !d.Over(s.button)) continue;
                Press(s.item);
                return true;
            }
            return false;
        }

        void Press(Shop.Item item)
        {
            switch (Shop.StateOf(item.id))
            {
                case Shop.State.Ordered: Say($"{item.name} is on its way - it arrives tomorrow morning.", OrangeDark); return;
                case Shop.State.Delivered: Say($"Your {item.name} box is waiting by the front door. Go and open it (E)!", new Color32(40, 110, 200, 255)); return;
                case Shop.State.Installed: Say($"{item.name} is already installed.", Good); return;
            }
            float need = item.price - Wallet.Money;
            if (need > 0.001f)
            {
                Say($"Not enough money for the {item.name}: you need {Wallet.Format(need)} more.", Bad);
                Fx.Play(Fx.Thud, d.pc ? d.pc.transform.position : Vector3.zero, 0.25f, 1.6f);
                return;
            }
            if (Shop.Order(item))
            {
                Say($"Ordered: {item.name} ({Wallet.Format(item.price)}). It arrives at the front door tomorrow morning.", Good);
                Fx.Play(Fx.Click, d.pc ? d.pc.transform.position : Vector3.zero, 0.5f, 1.1f);
                Refresh();
            }
        }

        public override void Scroll(float direction)
        {
            if (d.Over(root)) Go(direction > 0f ? -1 : 1);
        }

        void Go(int delta)
        {
            int before = page;
            page += delta;
            Refresh();
            if (page != before) Click1();
        }

        void Click1() => Fx.Play(Fx.Click, d.pc ? d.pc.transform.position : Vector3.zero, 0.2f, 1.6f);

        // ------------------------------------------------------------------ the little pictures (shapes only)

        void DrawArt(Slot s, Shop.Item item)
        {
            for (int i = s.art.childCount - 1; i >= 0; i--) UnityEngine.Object.Destroy(s.art.GetChild(i).gameObject);
            s.artFor = item.id;
            DrawProduct(s.art, item);
        }

        /// <summary>The product picture (128x128 box). Also used by LiveLoop's gear list.</summary>
        public static void DrawProduct(RectTransform art, Shop.Item item)
        {
            Color bg = Tint(item.category);
            Color shadow = new Color(0.25f, 0.15f, 0.05f, 0.12f);
            Shape(art, shadow, new Vector2(0, -54), new Vector2(84, 9), true);

            switch (item.id)
            {
                case Shop.OvenTimer:
                    Shape(art, new Color32(52, 54, 66, 255), new Vector2(0, -2), new Vector2(96, 66), true);
                    Shape(art, new Color32(22, 28, 30, 255), new Vector2(-12, 7), new Vector2(56, 28), true);
                    var t = Txt(art, "0:45", 21, new Color32(255, 190, 60, 255), TextAlignmentOptions.Center, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-12, 7), new Vector2(56, 28), false, true);
                    t.overflowMode = TextOverflowModes.Overflow;
                    Dot(art, new Color32(214, 218, 230, 255), new Vector2(32, 4), 26);
                    Shape(art, new Color32(90, 94, 110, 255), new Vector2(32, 9), new Vector2(3, 11));
                    Dot(art, new Color32(255, 140, 40, 255), new Vector2(-30, -20), 9);
                    Dot(art, new Color32(120, 200, 255, 255), new Vector2(-14, -20), 9);
                    Shape(art, new Color32(52, 54, 66, 255), new Vector2(-32, -38), new Vector2(14, 5));
                    Shape(art, new Color32(52, 54, 66, 255), new Vector2(32, -38), new Vector2(14, 5));
                    break;

                case Shop.LeakSensor:
                    Shape(art, new Color(0.2f, 0.5f, 0.9f, 0.3f), new Vector2(0, -56), new Vector2(70, 8), true);
                    Dot(art, new Color32(238, 241, 248, 255), Vector2.zero, 84);
                    Dot(art, new Color32(212, 220, 234, 255), Vector2.zero, 66);
                    Shape(art, new Color32(58, 150, 240, 255), new Vector2(0, 10), new Vector2(17, 17), false, 45f);
                    Dot(art, new Color32(58, 150, 240, 255), new Vector2(0, -3), 27);
                    Dot(art, new Color(1f, 1f, 1f, 0.6f), new Vector2(-5, -1), 7);
                    Dot(art, new Color32(60, 210, 120, 255), new Vector2(0, -29), 8);
                    break;

                case Shop.RobotVacuum:
                    Dot(art, new Color32(38, 40, 52, 255), Vector2.zero, 100);
                    Dot(art, new Color32(228, 231, 240, 255), Vector2.zero, 90);
                    Dot(art, new Color32(86, 90, 110, 255), Vector2.zero, 70);
                    Dot(art, Color.white, new Vector2(-8, -8), 24);
                    Dot(art, new Color32(80, 220, 255, 255), new Vector2(0, 24), 9);
                    Shape(art, new Color32(38, 40, 52, 255), new Vector2(0, 47), new Vector2(30, 6), true);
                    Shape(art, new Color(1f, 1f, 1f, 0.35f), new Vector2(-30, 22), new Vector2(26, 5), true, 40f);
                    break;

                case Shop.CatFeeder:
                    Shape(art, new Color32(232, 230, 224, 255), new Vector2(0, -46), new Vector2(104, 12), true);
                    Shape(art, new Color32(150, 198, 224, 255), new Vector2(-14, -6), new Vector2(42, 68), true);
                    Shape(art, new Color32(150, 92, 40, 255), new Vector2(-14, -22), new Vector2(42, 34), true);
                    Shape(art, new Color32(44, 48, 58, 255), new Vector2(-14, 32), new Vector2(50, 10), true);
                    Shape(art, new Color32(232, 230, 224, 255), new Vector2(-14, 40), new Vector2(14, 7), true);
                    Shape(art, new Color32(44, 48, 58, 255), new Vector2(10, -26), new Vector2(18, 12));
                    Shape(art, new Color32(196, 200, 212, 255), new Vector2(30, -38), new Vector2(44, 14), true);
                    Dot(art, new Color32(150, 92, 40, 255), new Vector2(22, -30), 9);
                    Dot(art, new Color32(150, 92, 40, 255), new Vector2(31, -32), 9);
                    Dot(art, new Color32(150, 92, 40, 255), new Vector2(39, -30), 9);
                    break;

                case Shop.RemoteHolder:
                    Shape(art, new Color32(150, 98, 58, 255), new Vector2(0, -4), new Vector2(58, 66), true);
                    Shape(art, new Color32(106, 66, 38, 255), new Vector2(0, -2), new Vector2(46, 50), true);
                    Shape(art, new Color32(30, 32, 40, 255), new Vector2(0, 8), new Vector2(18, 60), true);
                    Dot(art, new Color32(255, 80, 80, 255), new Vector2(0, 28), 8);
                    Dot(art, new Color32(150, 156, 170, 255), new Vector2(-4, 16), 6);
                    Dot(art, new Color32(150, 156, 170, 255), new Vector2(4, 16), 6);
                    Dot(art, new Color32(150, 156, 170, 255), new Vector2(0, 8), 6);
                    Shape(art, new Color32(150, 98, 58, 255), new Vector2(0, -22), new Vector2(58, 26), true);
                    Shape(art, new Color32(176, 120, 76, 255), new Vector2(0, -15), new Vector2(50, 4), true);
                    break;

                case Shop.DoorCamera:
                    Shape(art, new Color32(158, 108, 68, 255), new Vector2(-18, 0), new Vector2(58, 100), true);
                    Shape(art, new Color32(132, 88, 52, 255), new Vector2(-18, 14), new Vector2(40, 38), true);
                    Dot(art, new Color32(240, 196, 80, 255), new Vector2(-38, -6), 9);
                    Shape(art, new Color32(246, 248, 252, 255), new Vector2(26, 6), new Vector2(32, 58), true);
                    Dot(art, new Color32(34, 38, 50, 255), new Vector2(26, 20), 24);
                    Dot(art, new Color32(96, 176, 255, 255), new Vector2(26, 20), 14);
                    Dot(art, new Color(1f, 1f, 1f, 0.8f), new Vector2(23, 23), 4);
                    Dot(art, new Color32(255, 70, 84, 255), new Vector2(26, -6), 6);
                    Dot(art, new Color32(52, 56, 70, 255), new Vector2(26, -22), 12);
                    break;

                case Shop.Headphones:
                    Dot(art, new Color32(50, 52, 66, 255), new Vector2(0, 8), 90);
                    Dot(art, bg, new Vector2(0, 8), 68);
                    Shape(art, bg, new Vector2(0, -20), new Vector2(104, 50));
                    Shape(art, new Color32(255, 140, 40, 255), new Vector2(-42, -10), new Vector2(28, 48), true);
                    Shape(art, new Color32(255, 140, 40, 255), new Vector2(42, -10), new Vector2(28, 48), true);
                    Shape(art, new Color32(50, 52, 66, 255), new Vector2(-33, -10), new Vector2(11, 38), true);
                    Shape(art, new Color32(50, 52, 66, 255), new Vector2(33, -10), new Vector2(11, 38), true);
                    break;

                case Shop.Webcam:
                    Shape(art, new Color32(50, 54, 70, 255), new Vector2(-16, 8), new Vector2(54, 42), true);
                    Dot(art, new Color32(200, 210, 230, 255), new Vector2(-16, 8), 32);
                    Dot(art, new Color32(28, 30, 40, 255), new Vector2(-16, 8), 22);
                    Dot(art, new Color32(90, 160, 255, 255), new Vector2(-16, 8), 11);
                    Dot(art, new Color(1f, 1f, 1f, 0.8f), new Vector2(-19, 11), 4);
                    Shape(art, new Color32(50, 54, 70, 255), new Vector2(-16, -20), new Vector2(12, 16));
                    Shape(art, new Color32(50, 54, 70, 255), new Vector2(-16, -30), new Vector2(40, 7), true);
                    Shape(art, new Color32(186, 192, 206, 255), new Vector2(36, 8), new Vector2(18, 40), true);
                    for (int i = 0; i < 3; i++) Shape(art, new Color32(120, 126, 142, 255), new Vector2(36, 0 + i * 9), new Vector2(12, 2));
                    Shape(art, new Color32(60, 64, 80, 255), new Vector2(36, -22), new Vector2(5, 18));
                    Shape(art, new Color32(60, 64, 80, 255), new Vector2(36, -31), new Vector2(30, 6), true);
                    break;

                case Shop.RingLight:
                    Shape(art, new Color32(58, 200, 100, 255), new Vector2(0, 2), new Vector2(112, 88), true);
                    Shape(art, new Color32(44, 168, 82, 255), new Vector2(-36, 2), new Vector2(7, 88));
                    Shape(art, new Color32(44, 168, 82, 255), new Vector2(40, 2), new Vector2(5, 88));
                    Dot(art, new Color32(255, 252, 240, 255), new Vector2(0, 10), 64);
                    Dot(art, new Color32(58, 200, 100, 255), new Vector2(0, 10), 44);
                    Dot(art, new Color32(58, 64, 80, 255), new Vector2(0, 10), 9);
                    Shape(art, new Color32(58, 64, 80, 255), new Vector2(0, -26), new Vector2(6, 36));
                    Shape(art, new Color32(58, 64, 80, 255), new Vector2(0, -44), new Vector2(40, 7), true);
                    break;

                case Shop.GraphicsCard:
                    Shape(art, new Color32(204, 208, 218, 255), new Vector2(-56, 0), new Vector2(7, 54), true);
                    Shape(art, new Color32(40, 42, 54, 255), new Vector2(0, 4), new Vector2(104, 50), true);
                    Shape(art, new Color32(255, 140, 40, 255), new Vector2(0, 24), new Vector2(36, 3));
                    for (int f = -1; f <= 1; f += 2)
                    {
                        Dot(art, new Color32(74, 78, 98, 255), new Vector2(f * 24, 2), 38);
                        for (int b = 0; b < 3; b++) Shape(art, new Color32(132, 138, 164, 255), new Vector2(f * 24, 2), new Vector2(30, 5), true, b * 60f);
                        Dot(art, new Color32(28, 30, 40, 255), new Vector2(f * 24, 2), 11);
                    }
                    Shape(art, new Color32(240, 196, 70, 255), new Vector2(-4, -27), new Vector2(76, 7));
                    for (int i = 0; i < 9; i++) Shape(art, new Color32(190, 150, 40, 255), new Vector2(-36 + i * 8, -27), new Vector2(2, 7));
                    break;

                case Shop.StreamingPC:
                    Shape(art, new Color32(44, 46, 62, 255), new Vector2(-16, 0), new Vector2(56, 94), true);
                    Shape(art, new Color32(62, 66, 90, 255), new Vector2(-16, 4), new Vector2(40, 66), true);
                    Dot(art, new Color32(92, 98, 126, 255), new Vector2(-16, 14), 26);
                    Dot(art, new Color32(32, 34, 46, 255), new Vector2(-16, 14), 9);
                    Shape(art, new Color32(80, 220, 255, 255), new Vector2(-16, -14), new Vector2(30, 3));
                    Shape(art, new Color32(80, 220, 255, 255), new Vector2(-16, -20), new Vector2(20, 3));
                    Dot(art, new Color32(200, 205, 220, 255), new Vector2(-16, -38), 8);
                    Shape(art, new Color32(255, 140, 40, 255), new Vector2(32, -26), new Vector2(48, 20), true);
                    for (int i = 0; i < 3; i++) Shape(art, new Color32(44, 46, 62, 255), new Vector2(20 + i * 12, -26), new Vector2(7, 9));
                    Shape(art, new Color32(44, 46, 62, 255), new Vector2(26, -2), new Vector2(3, 38));
                    break;

                default:
                    Dot(art, new Color32(255, 140, 40, 255), Vector2.zero, 70);
                    break;
            }
        }
    }
}
