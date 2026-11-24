using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// MyMail, the Browser's webmail: an inbox list on the left (6 per page, prev/next or mouse wheel) and a reading
/// pane on the right. Reading a message marks it read. Mom's job mails have an Accept button and a status chip
/// (In progress / Done - paid $X / Expired). The bookmark and the Browser's desktop icon show an unread badge.
/// All text boxes are sized for their content: single lines end with "...", the body shrinks to fit.
/// </summary>
public partial class PCDesktop
{
    bool mailDirty = true;
    MailPage mailPage;
    Image mailBookmarkBadge, mailIconBadge;
    TextMeshProUGUI mailBookmarkBadgeText, mailIconBadgeText;
    int shownUnread = -1;

    void MarkMailDirty() => mailDirty = true;

    /// <summary>Badges on the MyMail bookmark and on the Browser desktop icon (hidden at zero).</summary>
    void UpdateMailBadges()
    {
        // until 4:30 PM the browser stays quiet (the day's mail comes then): the PC's only nudge is the homework
        var dayNow = DayManager.Instance;
        bool mailTime = !dayNow || dayNow.State != DayManager.Phase.Day || dayNow.Hour >= CampaignRuntime.MailHour;
        int unread = mailTime ? Mail.Unread : 0;
        UpdateHomeworkBadge();
        if (unread == shownUnread) return;
        shownUnread = unread;
        string label = unread > 9 ? "9+" : unread.ToString();
        if (mailBookmarkBadge)
        {
            mailBookmarkBadge.gameObject.SetActive(unread > 0);
            mailBookmarkBadgeText.text = label;
        }
        if (!mailIconBadge)
        {
            var icon = icons.Find(i => i.id == "browser");
            if (icon != null)
            {
                mailIconBadge = UIFactory.Panel("Mail badge", icon.rt, new Color32(236, 56, 70, 255));
                mailIconBadge.sprite = Round();
                UIFactory.Place(mailIconBadge.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), new Vector2(38, -12), new Vector2(26, 26));
                mailIconBadgeText = Txt(mailIconBadge.transform, "", 14, Color.white, TextAlignmentOptions.Center, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, false, true);
                UIFactory.Fill(mailIconBadgeText.rectTransform);
                mailIconBadgeText.overflowMode = TextOverflowModes.Overflow;
            }
        }
        if (mailIconBadge)
        {
            mailIconBadge.gameObject.SetActive(unread > 0);
            mailIconBadgeText.text = label;
        }
    }

    Image homeworkBadge;
    TypingTask homeworkTask;
    float nextHomeworkLook;

    /// <summary>"DUE" on the Homework icon while today's homework is still to do.</summary>
    void UpdateHomeworkBadge()
    {
        if (Time.unscaledTime >= nextHomeworkLook) { nextHomeworkLook = Time.unscaledTime + 1f; if (!homeworkTask) homeworkTask = FindObjectOfType<TypingTask>(); }
        bool due = homeworkTask && homeworkTask.IsActive;
        if (!homeworkBadge)
        {
            var icon = icons.Find(i => i.id == "homework");
            if (icon == null) return;
            homeworkBadge = UIFactory.Panel("Due badge", icon.rt, new Color32(236, 56, 70, 255));
            homeworkBadge.sprite = Round();
            homeworkBadge.type = Image.Type.Sliced;
            UIFactory.Place(homeworkBadge.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), new Vector2(36, -12), new Vector2(46, 22));
            var t = Txt(homeworkBadge.transform, "DUE", 13, Color.white, TextAlignmentOptions.Center, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, false, true);
            UIFactory.Fill(t.rectTransform);
            t.overflowMode = TextOverflowModes.Overflow;
        }
        if (homeworkBadge.gameObject.activeSelf != due) homeworkBadge.gameObject.SetActive(due);
    }

    class MailPage : SitePage
    {
        const int PerPage = 6;
        const float ListX = 36f, ListY = 78f, ListW = 464f, ListH = 434f, RowH = 60f, RowsTop = 56f;
        const float ReadX = 516f, ReadW = 748f;

        static readonly Color Ink = new Color32(34, 40, 58, 255);
        static readonly Color Muted = new Color32(110, 120, 146, 255);
        static readonly Color Blue = new Color32(70, 130, 240, 255);
        static readonly Color BlueDark = new Color32(44, 98, 200, 255);
        static readonly Color Good = new Color32(38, 160, 92, 255);
        static readonly Color Amber = new Color32(214, 140, 20, 255);
        static readonly Color Grey = new Color32(150, 156, 172, 255);

        class Row
        {
            public RectTransform rt;
            public Image bg, dot, tile;
            public TextMeshProUGUI glyph, from, time, subject, tag;
            public int id;
        }

        readonly Row[] rows = new Row[PerPage];
        TextMeshProUGUI unreadPill, totalText, pageLabel, emptyText;
        Image unreadPillBg, prevBg, nextBg;
        RectTransform prev, next;
        CanvasGroup prevGroup, nextGroup;
        GameObject placeholder, detail, jobCard;
        TextMeshProUGUI dSubject, dFrom, dTo, dWhen, dBody, dAvatarText, jobTitle, jobHint, chipText, buttonText;
        Image dAvatar, chip, button, jobIcon;
        RectTransform buttonRt, bodyRt;
        int page, selected = -1;

        static void KindLook(Mail.Kind k, out Color colour, out string glyph, out string name)
        {
            switch (k)
            {
                case Mail.Kind.Job: colour = new Color32(40, 186, 176, 255); glyph = "$"; name = "Job"; break;
                case Mail.Kind.Invite: colour = new Color32(160, 110, 250, 255); glyph = "!"; name = "Invite"; break;
                case Mail.Kind.Delivery: colour = new Color32(255, 140, 40, 255); glyph = "P"; name = "Delivery"; break;
                default: colour = new Color32(70, 130, 240, 255); glyph = "i"; name = "Info"; break;
            }
        }

        protected override void Build(RectTransform r)
        {
            var bg = UIFactory.Panel("Background", r, site.bg);
            UIFactory.Fill(bg.rectTransform);
            var glowA = UIFactory.Panel("Glow", r, new Color(0.27f, 0.5f, 0.95f, 0.14f));
            glowA.sprite = Round();
            UIFactory.Place(glowA.rectTransform, new Vector2(0.12f, 0.95f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(820, 520));
            var glowB = UIFactory.Panel("Glow", r, new Color(0.4f, 0.6f, 1f, 0.1f));
            glowB.sprite = Round();
            UIFactory.Place(glowB.rectTransform, new Vector2(0.92f, 0.05f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(760, 480));

            BuildHeader(r);
            BuildList(r);
            BuildReader(r);
        }

        void BuildHeader(RectTransform r)
        {
            var head = UIFactory.Panel("Header", r, new Color(1f, 1f, 1f, 0.93f));
            UIFactory.Place(head.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(PageW, 64));
            var line = UIFactory.Panel("Rule", head.transform, new Color(0.27f, 0.5f, 0.95f, 0.9f));
            UIFactory.Place(line.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), Vector2.zero, new Vector2(PageW, 3));
            var mini = UIFactory.Rect("Logo", head.transform);
            UIFactory.Place(mini, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(40, 0), new Vector2(64, 64));
            mini.localScale = Vector3.one * 0.62f;
            site.logo(mini);
            var wm = Txt(head.transform, site.wordmark, 30, Ink, TextAlignmentOptions.MidlineLeft, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(96, 0), new Vector2(210, 40));
            wm.characterSpacing = 1f;

            unreadPillBg = RPanel(head.transform, new Color(0.27f, 0.5f, 0.95f, 0.16f));
            TopLeft(unreadPillBg.rectTransform, 330, 14, 170, 36);
            unreadPill = TxtTL(unreadPillBg.transform, "", 17, BlueDark, TextAlignmentOptions.Center, 0, 0, 170, 36, false, true);

            totalText = Txt(head.transform, "", 17, Muted, TextAlignmentOptions.MidlineRight, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-40, 0), new Vector2(380, 30));
        }

        void BuildList(RectTransform r)
        {
            var panel = RPanel(r, new Color(1f, 1f, 1f, 0.95f));
            TopLeft(panel.rectTransform, ListX, ListY, ListW, ListH);
            TxtTL(panel.transform, "Inbox", 24, Ink, TextAlignmentOptions.MidlineLeft, 20, 8, 200, 40, false, true);

            nextBg = RPanel(panel.transform, new Color(0.27f, 0.5f, 0.95f, 0.12f));
            next = nextBg.rectTransform;
            UIFactory.Place(next, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-14, -12), new Vector2(36, 32));
            nextGroup = next.gameObject.AddComponent<CanvasGroup>();
            var na = UIFactory.Rect("Art", next);
            UIFactory.Place(na, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(20, 20));
            Chevron(na, BlueDark, 1);
            pageLabel = Txt(panel.transform, "", 16, Muted, TextAlignmentOptions.Center, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-54, -12), new Vector2(80, 32));
            prevBg = RPanel(panel.transform, new Color(0.27f, 0.5f, 0.95f, 0.12f));
            prev = prevBg.rectTransform;
            UIFactory.Place(prev, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-136, -12), new Vector2(36, 32));
            prevGroup = prev.gameObject.AddComponent<CanvasGroup>();
            var pa = UIFactory.Rect("Art", prev);
            UIFactory.Place(pa, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(20, 20));
            Chevron(pa, BlueDark, -1);
            var rule = UIFactory.Panel("Rule", panel.transform, new Color(0.27f, 0.5f, 0.95f, 0.18f));
            TopLeft(rule.rectTransform, 0, 52, ListW, 2);

            for (int i = 0; i < PerPage; i++)
            {
                var row = new Row();
                row.bg = UIFactory.Panel("Row", panel.transform, new Color(1f, 1f, 1f, 0f));
                row.rt = row.bg.rectTransform;
                TopLeft(row.rt, 0, RowsTop + i * RowH, ListW, RowH);
                var sep = UIFactory.Panel("Line", row.rt, new Color(0.27f, 0.5f, 0.95f, 0.1f));
                UIFactory.Place(sep.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), Vector2.zero, new Vector2(ListW - 32, 1));
                row.dot = UIFactory.Panel("Unread dot", row.rt, Blue);
                row.dot.sprite = Round();
                TopLeft(row.dot.rectTransform, 12, 25, 10, 10);
                row.tile = RPanel(row.rt, Blue);
                TopLeft(row.tile.rectTransform, 32, 12, 36, 36);
                row.glyph = Txt(row.tile.transform, "", 19, Color.white, TextAlignmentOptions.Center, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, false, true);
                UIFactory.Fill(row.glyph.rectTransform);
                row.glyph.overflowMode = TextOverflowModes.Overflow;
                row.from = TxtTL(row.rt, "", 17, Ink, TextAlignmentOptions.MidlineLeft, 80, 6, 250, 24);
                row.time = TxtTL(row.rt, "", 13, Muted, TextAlignmentOptions.MidlineRight, ListW - 16 - 96, 8, 96, 20);
                row.subject = TxtTL(row.rt, "", 15, Muted, TextAlignmentOptions.MidlineLeft, 80, 31, 272, 22);
                row.tag = TxtTL(row.rt, "", 12, Muted, TextAlignmentOptions.MidlineRight, ListW - 16 - 92, 33, 92, 20, false, true);
                rows[i] = row;
            }

            emptyText = Txt(panel.transform, "Your inbox is empty.\nMessages from Mom, invites and delivery notices will show up here.", 17, Muted,
                TextAlignmentOptions.Center, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -10), new Vector2(380, 110), true);
            emptyText.overflowMode = TextOverflowModes.Ellipsis;
        }

        void BuildReader(RectTransform r)
        {
            var panel = RPanel(r, new Color(1f, 1f, 1f, 0.95f));
            TopLeft(panel.rectTransform, ReadX, ListY, ReadW, ListH);

            // ---- nothing selected
            placeholder = UIFactory.Rect("Placeholder", panel.transform).gameObject;
            var ph = (RectTransform)placeholder.transform;
            UIFactory.Fill(ph);
            var env = UIFactory.Rect("Envelope", ph);
            UIFactory.Place(env, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 64), new Vector2(110, 80));
            Shape(env, new Color(0.27f, 0.5f, 0.95f, 0.16f), Vector2.zero, new Vector2(100, 68), true);
            Shape(env, new Color(0.27f, 0.5f, 0.95f, 0.45f), new Vector2(-24, 12), new Vector2(54, 4), false, -30f);
            Shape(env, new Color(0.27f, 0.5f, 0.95f, 0.45f), new Vector2(24, 12), new Vector2(54, 4), false, 30f);
            Txt(ph, "Select a message to read it", 24, Ink, TextAlignmentOptions.Center, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -12), new Vector2(560, 40), false, true);
            Txt(ph, "Unread messages have a blue dot. Job mails from Mom pay real money when you finish them.", 17, Muted,
                TextAlignmentOptions.Top, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 1f), new Vector2(0, -34), new Vector2(520, 60), true).overflowMode = TextOverflowModes.Ellipsis;

            // ---- a message
            detail = UIFactory.Rect("Message", panel.transform).gameObject;
            var d0 = (RectTransform)detail.transform;
            UIFactory.Fill(d0);
            dSubject = TxtTL(d0, "", 24, Ink, TextAlignmentOptions.MidlineLeft, 28, 16, ReadW - 56, 62, true, true);
            dSubject.enableAutoSizing = true;
            dSubject.fontSizeMin = 17;
            dSubject.fontSizeMax = 24;
            dSubject.overflowMode = TextOverflowModes.Ellipsis;
            dAvatar = UIFactory.Panel("Avatar", d0, Blue);
            dAvatar.sprite = Round();
            TopLeft(dAvatar.rectTransform, 28, 88, 46, 46);
            dAvatarText = Txt(dAvatar.transform, "", 22, Color.white, TextAlignmentOptions.Center, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, false, true);
            UIFactory.Fill(dAvatarText.rectTransform);
            dAvatarText.overflowMode = TextOverflowModes.Overflow;
            dFrom = TxtTL(d0, "", 19, Ink, TextAlignmentOptions.MidlineLeft, 86, 88, 380, 26, false, true);
            dTo = TxtTL(d0, "", 14, Muted, TextAlignmentOptions.MidlineLeft, 86, 114, 380, 22);
            dWhen = TxtTL(d0, "", 14, Muted, TextAlignmentOptions.MidlineRight, ReadW - 28 - 240, 92, 240, 24);
            var rule = UIFactory.Panel("Rule", d0, new Color(0.27f, 0.5f, 0.95f, 0.18f));
            TopLeft(rule.rectTransform, 28, 148, ReadW - 56, 2);
            dBody = TxtTL(d0, "", 19, new Color32(52, 58, 78, 255), TextAlignmentOptions.TopLeft, 28, 162, ReadW - 56, 170, true);
            bodyRt = dBody.rectTransform;
            dBody.richText = false;
            dBody.enableAutoSizing = true;
            dBody.fontSizeMin = 13;
            dBody.fontSizeMax = 19;
            dBody.overflowMode = TextOverflowModes.Ellipsis;

            // ---- job card
            var card = RPanel(d0, new Color(0.15f, 0.73f, 0.69f, 0.12f));
            jobCard = card.gameObject;
            TopLeft(card.rectTransform, 28, 342, ReadW - 56, 76);
            jobIcon = RPanel(card.transform, new Color32(40, 186, 176, 255));
            TopLeft(jobIcon.rectTransform, 16, 16, 44, 44);
            var jg = Txt(jobIcon.transform, "$", 24, Color.white, TextAlignmentOptions.Center, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, false, true);
            UIFactory.Fill(jg.rectTransform);
            jg.overflowMode = TextOverflowModes.Overflow;
            jobTitle = TxtTL(card.transform, "", 18, Ink, TextAlignmentOptions.MidlineLeft, 74, 6, 380, 26, false, true);
            jobHint = TxtTL(card.transform, "", 14, Muted, TextAlignmentOptions.TopLeft, 74, 34, 380, 38, true);
            jobHint.overflowMode = TextOverflowModes.Ellipsis;

            button = RPanel(card.transform, Blue);
            buttonRt = button.rectTransform;
            UIFactory.Place(buttonRt, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-16, 0), new Vector2(190, 46));
            buttonText = Txt(button.transform, "Accept job", 19, Color.white, TextAlignmentOptions.Center, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, false, true);
            UIFactory.Fill(buttonText.rectTransform);
            chip = RPanel(card.transform, Good);
            UIFactory.Place(chip.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-16, 0), new Vector2(190, 46));
            chipText = Txt(chip.transform, "", 16, Color.white, TextAlignmentOptions.Center, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, true, true);
            UIFactory.Fill(chipText.rectTransform, 6, 6, 2, 2);
            chipText.overflowMode = TextOverflowModes.Ellipsis;
        }

        // ------------------------------------------------------------------ data -> widgets

        public override void Refresh()
        {
            var list = Mail.Messages;
            int pages = Mathf.Max(1, Mathf.CeilToInt(list.Count / (float)PerPage));
            page = Mathf.Clamp(page, 0, pages - 1);
            if (selected >= 0 && Mail.Find(selected) == null) selected = -1;

            int unread = Mail.Unread;
            unreadPill.text = unread == 0 ? "All caught up" : unread + " unread";
            totalText.text = list.Count + (list.Count == 1 ? " message" : " messages") + " in your inbox";
            pageLabel.text = $"{page + 1} / {pages}";
            pageLabel.gameObject.SetActive(list.Count > 0);
            prev.gameObject.SetActive(list.Count > 0);
            next.gameObject.SetActive(list.Count > 0);
            prevGroup.alpha = page > 0 ? 1f : 0.3f;
            nextGroup.alpha = page < pages - 1 ? 1f : 0.3f;
            emptyText.gameObject.SetActive(list.Count == 0);

            for (int i = 0; i < PerPage; i++)
            {
                int index = page * PerPage + i;
                var row = rows[i];
                row.rt.gameObject.SetActive(index < list.Count);
                if (index >= list.Count) continue;
                var m = list[index];
                row.id = m.id;
                KindLook(m.MailKind, out Color colour, out string glyph, out string kindName);
                row.dot.enabled = !m.read;
                row.tile.color = colour;
                row.glyph.text = glyph;
                row.from.text = m.from;
                row.from.fontStyle = m.read ? FontStyles.Normal : FontStyles.Bold;
                row.subject.text = m.subject;
                row.subject.color = m.read ? Muted : Ink;
                row.time.text = m.day == Wallet.DayLabel || string.IsNullOrEmpty(m.day) ? (string.IsNullOrEmpty(m.time) ? m.day : m.time) : m.day;
                StatusLook(m, kindName, out string tag, out Color tagColour);
                row.tag.text = tag;
                row.tag.color = tagColour;
            }

            var sel = selected >= 0 ? Mail.Find(selected) : null;
            placeholder.SetActive(sel == null);
            detail.SetActive(sel != null);
            if (sel != null) ShowMessage(sel);
        }

        static void StatusLook(Mail.Message m, string kindName, out string tag, out Color colour)
        {
            tag = kindName;
            colour = Muted;
            if (m.MailKind == Mail.Kind.Job)
            {
                switch (m.State)
                {
                    case Mail.JobState.Offered: tag = "$" + m.pay + " job"; colour = Amber; break;
                    case Mail.JobState.Accepted: tag = "In progress"; colour = Blue; break;
                    case Mail.JobState.Done: tag = "Done"; colour = Good; break;
                    case Mail.JobState.Expired: tag = "Expired"; colour = Grey; break;
                }
            }
            else if (m.MailKind == Mail.Kind.Invite) colour = new Color32(140, 90, 230, 255);
            else if (m.MailKind == Mail.Kind.Delivery) colour = new Color32(214, 110, 20, 255);
            else tag = "";
        }

        void ShowMessage(Mail.Message m)
        {
            KindLook(m.MailKind, out Color colour, out string glyph, out string kindName);
            dSubject.text = m.subject;
            dAvatar.color = colour;
            dAvatarText.text = string.IsNullOrEmpty(m.from) ? "?" : m.from.Substring(0, 1).ToUpperInvariant();
            dFrom.text = m.from;
            dTo.text = "to me  -  " + kindName.ToLowerInvariant();
            dWhen.text = string.IsNullOrEmpty(m.time) ? m.day : m.day + "  -  " + m.time;
            dBody.text = m.body;

            bool job = m.MailKind == Mail.Kind.Job;
            jobCard.SetActive(job);
            bodyRt.sizeDelta = new Vector2(bodyRt.sizeDelta.x, job ? 170f : 262f);
            if (!job) return;

            jobTitle.text = $"Side job - pays ${m.pay}";
            jobHint.text = MailRuntime.JobHint(m.Job);
            var state = m.State;
            button.gameObject.SetActive(state == Mail.JobState.Offered);
            chip.gameObject.SetActive(state != Mail.JobState.Offered);
            switch (state)
            {
                case Mail.JobState.Accepted: chip.color = Blue; chipText.text = "Accepted - in progress"; break;
                case Mail.JobState.Done: chip.color = Good; chipText.text = $"Done - paid ${m.pay}"; break;
                default: chip.color = Grey; chipText.text = "Expired"; break;
            }
        }

        // ------------------------------------------------------------------ input

        public override void Tick()
        {
            prevBg.color = new Color(0.27f, 0.5f, 0.95f, page > 0 && d.Over(prev) ? 0.28f : 0.12f);
            nextBg.color = new Color(0.27f, 0.5f, 0.95f, nextGroup.alpha > 0.5f && d.Over(next) ? 0.28f : 0.12f);
            foreach (var row in rows)
            {
                if (!row.rt.gameObject.activeSelf) continue;
                bool sel = row.id == selected;
                row.bg.color = sel ? new Color(0.27f, 0.5f, 0.95f, 0.14f) : d.Over(row.rt) ? new Color(0.27f, 0.5f, 0.95f, 0.06f) : new Color(1f, 1f, 1f, 0f);
            }
            if (button.gameObject.activeInHierarchy) button.color = d.Over(buttonRt) ? BlueDark : Blue;
        }

        public override bool Click()
        {
            if (d.Over(prev)) { Go(-1); return true; }
            if (d.Over(next)) { Go(1); return true; }
            if (button.gameObject.activeInHierarchy && d.Over(buttonRt))
            {
                var m = Mail.Find(selected);
                if (m != null)
                {
                    if (MailRuntime.Instance) MailRuntime.Instance.AcceptJob(m);
                    else GameHUD.Toast("You can only take jobs at home.", GameHUD.Warn);
                    Refresh();
                }
                return true;
            }
            foreach (var row in rows)
            {
                if (!row.rt.gameObject.activeSelf || !d.Over(row.rt)) continue;
                selected = row.id;
                Mail.MarkRead(row.id);
                Refresh();
                Fx.Play(Fx.Click, d.pc ? d.pc.transform.position : Vector3.zero, 0.2f, 1.5f);
                return true;
            }
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
