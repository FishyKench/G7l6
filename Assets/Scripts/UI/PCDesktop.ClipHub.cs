using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// ClipHub (Browser bookmark "ClipHub"): your channel and the clips you uploaded from the Clip Studio.
///   - channel header: name, followers, total views, likes, earnings, Partner badge (or progress to 100 followers);
///   - a grid of your videos (4 per page): thumbnail, title, views, likes, upload day;
///   - a video page: big thumbnail, stats, generated comments.
/// Numbers are live: views, likes and followers trickle in as game time passes (see <see cref="ClipHub"/>).
/// </summary>
public partial class PCDesktop
{
    ClipHubPage clipHubPage;
    bool clipHubDirty = true;

    void MarkClipHubDirty() => clipHubDirty = true;

    /// <summary>Shows a video of your channel (after uploading) - the Browser must already be open on ClipHub.</summary>
    void OpenClipHubVideo(int id)
    {
        Navigate("clip");
        if (clipHubPage != null) clipHubPage.ShowVideo(id);
    }

    static string Compact(int n)
    {
        if (n < 1000) return n.ToString();
        if (n < 10000) return (n / 1000f).ToString("0.0") + "K";
        if (n < 1000000) return (n / 1000).ToString() + "K";
        return (n / 1000000f).ToString("0.0") + "M";
    }

    class ClipHubPage : SitePage
    {
        const int PerPage = 4;
        const float CardW = 296f, CardH = 258f, CardGap = 16f;
        static readonly Color Gold = new Color32(255, 200, 70, 255);
        static readonly Color Green = new Color32(110, 230, 150, 255);

        struct Card
        {
            public RectTransform rt;
            public Image bg;
            public RawImage thumb;
            public Image thumbBack;
            public TextMeshProUGUI title, meta, day, duration, score;
            public int videoId;
        }

        struct CommentRow { public RectTransform rt; public Image avatar; public TextMeshProUGUI initial, user, text, meta; }

        GameObject channelRoot, videoRoot, emptyState;
        RectTransform openStudio, prevButton, nextButton, backButton, cardsArea, partnerBar;
        Image openStudioBg, prevBg, nextBg, backBg, partnerFill;
        CanvasGroup prevGroup, nextGroup;
        TextMeshProUGUI channelName, channelHandle, followersValue, viewsValue, likesValue, earnedValue, partnerText, pageLabel, footer, emptyOpenLabel;
        Image partnerBadge;
        TextMeshProUGUI partnerBadgeText;
        RectTransform emptyButton;
        Image emptyButtonBg;
        readonly Card[] cards = new Card[PerPage];
        readonly Dictionary<int, Texture2D> textures = new Dictionary<int, Texture2D>();
        int page, openVideo;

        // video page
        RawImage vThumb;
        TextMeshProUGUI vTitle, vMeta, vTags, vDuration, vViews, vLikes, vComments, vQuality, vCommentHeader, vNoComments;
        readonly CommentRow[] commentRows = new CommentRow[5];

        protected override void Build(RectTransform r)
        {
            var bg = UIFactory.Panel("Background", r, site.bg);
            UIFactory.Fill(bg.rectTransform);
            var glowA = UIFactory.Panel("Glow", r, new Color(0.75f, 0.31f, 1f, 0.14f));
            glowA.sprite = Round();
            UIFactory.Place(glowA.rectTransform, new Vector2(0.12f, 0.9f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(820, 560));
            var glowB = UIFactory.Panel("Glow", r, new Color(0.3f, 0.45f, 1f, 0.1f));
            glowB.sprite = Round();
            UIFactory.Place(glowB.rectTransform, new Vector2(0.95f, 0.05f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(760, 520));

            // ---- header
            var head = UIFactory.Panel("Header", r, new Color(0f, 0f, 0f, 0.38f));
            UIFactory.Place(head.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(PageW, 64));
            var mini = UIFactory.Rect("Logo", head.transform);
            UIFactory.Place(mini, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(40, 0), new Vector2(64, 64));
            mini.localScale = Vector3.one * 0.62f;
            site.logo(mini);
            var wm = Txt(head.transform, site.wordmark, 28, Color.white, TextAlignmentOptions.MidlineLeft, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(96, 0), new Vector2(200, 40));
            wm.characterSpacing = 2f;
            var search = RPanel(head.transform, new Color(1f, 1f, 1f, 0.1f));
            UIFactory.Place(search.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(330, 0), new Vector2(420, 36));
            Dot(search.rectTransform, new Color(1f, 1f, 1f, 0.45f), new Vector2(-186, 1), 14);
            Dot(search.rectTransform, new Color32(40, 28, 66, 255), new Vector2(-186, 1), 8);
            Shape(search.rectTransform, new Color(1f, 1f, 1f, 0.45f), new Vector2(-176, -7), new Vector2(7, 2), false, 45f);
            TxtTL(search.transform, "Search clips, players and moments", 16, new Color(1f, 1f, 1f, 0.4f), TextAlignmentOptions.MidlineLeft, 46, 0, 360, 36);
            openStudioBg = RPanel(head.transform, new Color(0.75f, 0.31f, 1f, 1f));
            openStudio = openStudioBg.rectTransform;
            UIFactory.Place(openStudio, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-36, 0), new Vector2(220, 38));
            var ost = Txt(openStudio, "Open Clip Studio", 17, Color.white, TextAlignmentOptions.Center, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, false, true);
            UIFactory.Fill(ost.rectTransform, 4, 4, 0, 0);
            backBg = RPanel(head.transform, new Color(1f, 1f, 1f, 0.14f));
            backButton = backBg.rectTransform;
            UIFactory.Place(backButton, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-36, 0), new Vector2(190, 38));
            var bt = Txt(backButton, "Back to channel", 16, Color.white, TextAlignmentOptions.Center, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, false, true);
            UIFactory.Fill(bt.rectTransform, 4, 4, 0, 0);

            BuildChannel(r);
            BuildVideo(r);
        }

        void BuildChannel(RectTransform r)
        {
            channelRoot = UIFactory.Rect("Channel", r).gameObject;
            var cr = (RectTransform)channelRoot.transform;
            UIFactory.Fill(cr, 0, 0, 64, 0);

            // channel band (y 12..112 inside the root, i.e. page y 76..176)
            var band = RPanel(cr, new Color(1f, 1f, 1f, 0.07f));
            TopLeft(band.rectTransform, 36, 12, PageW - 72, 100);
            var avatar = Dot(band.rectTransform, new Color32(150, 50, 215, 255), Vector2.zero, 72);
            UIFactory.Place(avatar.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(20, 0), new Vector2(72, 72));
            Dot(avatar.rectTransform, new Color32(190, 80, 255, 255), Vector2.zero, 62);
            var av = Txt(avatar.transform, ClipHub.ChannelName.Substring(0, 1), 34, Color.white, TextAlignmentOptions.Center, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, false, true);
            UIFactory.Fill(av.rectTransform);
            channelName = TxtTL(band.transform, ClipHub.ChannelName, 28, Color.white, TextAlignmentOptions.MidlineLeft, 110, 10, 300, 38, false, true);
            partnerBadge = RPanel(band.transform, Gold);
            TopLeft(partnerBadge.rectTransform, 410, 16, 96, 26);
            partnerBadgeText = Txt(partnerBadge.transform, "PARTNER", 13, new Color32(60, 40, 0, 255), TextAlignmentOptions.Center, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, false, true);
            UIFactory.Fill(partnerBadgeText.rectTransform);
            partnerBadgeText.characterSpacing = 2f;
            channelHandle = TxtTL(band.transform, ClipHub.Handle, 15, new Color(1f, 1f, 1f, 0.55f), TextAlignmentOptions.MidlineLeft, 110, 48, 300, 22);
            partnerText = TxtTL(band.transform, "", 14, new Color(1f, 1f, 1f, 0.75f), TextAlignmentOptions.MidlineLeft, 110, 70, 470, 22);
            var pb = UIFactory.Bar("Partner bar", band.transform, new Color(1f, 1f, 1f, 0.12f), Gold, out partnerFill);
            partnerBar = pb;
            var pbg = (RectTransform)pb.parent;
            TopLeft(pbg, 410, 22, 170, 8);
            partnerBar = pbg;

            // four stats on the right
            float bw = 148f, gap = 8f;
            float x0 = PageW - 72 - 4 * bw - 3 * gap - 16;
            followersValue = StatBox(band.transform, x0, "FOLLOWERS", bw);
            viewsValue = StatBox(band.transform, x0 + (bw + gap), "TOTAL VIEWS", bw);
            likesValue = StatBox(band.transform, x0 + 2 * (bw + gap), "LIKES", bw);
            earnedValue = StatBox(band.transform, x0 + 3 * (bw + gap), "EARNED", bw);

            // section title + pager
            TxtTL(cr, "Your videos", 22, Color.white, TextAlignmentOptions.MidlineLeft, 36, 122, 300, 32, false, true);
            nextBg = RPanel(cr, new Color(1f, 1f, 1f, 0.1f));
            nextButton = nextBg.rectTransform;
            UIFactory.Place(nextButton, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-36, -124), new Vector2(38, 30));
            nextGroup = nextButton.gameObject.AddComponent<CanvasGroup>();
            var na = UIFactory.Rect("Art", nextButton);
            UIFactory.Place(na, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(20, 20));
            Chevron(na, Color.white, 1);
            pageLabel = Txt(cr, "", 16, new Color(1f, 1f, 1f, 0.8f), TextAlignmentOptions.Center, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-80, -124), new Vector2(80, 30));
            prevBg = RPanel(cr, new Color(1f, 1f, 1f, 0.1f));
            prevButton = prevBg.rectTransform;
            UIFactory.Place(prevButton, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-168, -124), new Vector2(38, 30));
            prevGroup = prevButton.gameObject.AddComponent<CanvasGroup>();
            var pa = UIFactory.Rect("Art", prevButton);
            UIFactory.Place(pa, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(20, 20));
            Chevron(pa, Color.white, -1);

            // cards
            cardsArea = UIFactory.Rect("Cards", cr);
            TopLeft(cardsArea, 0, 160, PageW, CardH);
            for (int i = 0; i < PerPage; i++)
            {
                var c = new Card();
                c.bg = RPanel(cardsArea, new Color(1f, 1f, 1f, 0.07f));
                c.rt = c.bg.rectTransform;
                TopLeft(c.rt, 36 + i * (CardW + CardGap), 0, CardW, CardH);
                c.thumbBack = UIFactory.Panel("Thumb back", c.rt, new Color(0.1f, 0.07f, 0.18f, 1f));
                TopLeft(c.thumbBack.rectTransform, 0, 0, CardW, 167);
                c.thumb = UIFactory.Rect("Thumb", c.rt).gameObject.AddComponent<RawImage>();
                c.thumb.raycastTarget = false;
                TopLeft(c.thumb.rectTransform, 0, 0, CardW, 167);
                var dChip = RPanel(c.rt, new Color(0f, 0f, 0f, 0.7f));
                UIFactory.Place(dChip.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-8, -133), new Vector2(50, 22));
                c.duration = Txt(dChip.transform, "", 13, Color.white, TextAlignmentOptions.Center, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, false, true);
                UIFactory.Fill(c.duration.rectTransform);
                var sChip = RPanel(c.rt, new Color(0.75f, 0.31f, 1f, 0.92f));
                TopLeft(sChip.rectTransform, 8, 8, 64, 22);
                c.score = Txt(sChip.transform, "", 13, Color.white, TextAlignmentOptions.Center, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, false, true);
                UIFactory.Fill(c.score.rectTransform);
                c.title = TxtTL(c.rt, "", 16, Color.white, TextAlignmentOptions.TopLeft, 12, 173, CardW - 24, 42, true, true);
                c.title.overflowMode = TextOverflowModes.Ellipsis;
                c.meta = TxtTL(c.rt, "", 14, new Color(1f, 1f, 1f, 0.8f), TextAlignmentOptions.MidlineLeft, 12, 216, CardW - 24, 20);
                c.day = TxtTL(c.rt, "", 13, new Color(1f, 1f, 1f, 0.5f), TextAlignmentOptions.MidlineLeft, 12, 236, CardW - 24, 18);
                cards[i] = c;
            }
            footer = TxtTL(cr, "Views, likes and followers trickle in over game time. Partners (100 followers) earn $1 per 500 views.", 14, new Color(1f, 1f, 1f, 0.45f), TextAlignmentOptions.MidlineLeft, 36, 424, PageW - 72, 22);

            // empty state
            emptyState = UIFactory.Rect("Empty", cr).gameObject;
            var er = (RectTransform)emptyState.transform;
            TopLeft(er, 0, 158, PageW, 300);
            var big = UIFactory.Rect("Big logo", er);
            UIFactory.Place(big, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -6), new Vector2(64, 64));
            big.localScale = Vector3.one * 1.2f;
            site.logo(big);
            Txt(er, "No videos yet", 30, Color.white, TextAlignmentOptions.Center, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -98), new Vector2(600, 42), false, true);
            Txt(er, "Open the Clip Studio, keep your best moments from a round you played (or play a ranked match first to record clips), pick a thumbnail and upload. Views and followers trickle in over the day.",
                18, new Color(1f, 1f, 1f, 0.65f), TextAlignmentOptions.Top, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -146), new Vector2(660, 80), true);
            emptyButtonBg = RPanel(er, new Color(0.75f, 0.31f, 1f, 1f));
            emptyButton = emptyButtonBg.rectTransform;
            UIFactory.Place(emptyButton, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -240), new Vector2(260, 44));
            emptyOpenLabel = Txt(emptyButton, "Open Clip Studio", 19, Color.white, TextAlignmentOptions.Center, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, false, true);
            UIFactory.Fill(emptyOpenLabel.rectTransform, 4, 4, 0, 0);
        }

        TextMeshProUGUI StatBox(Transform parent, float x, string label, float w)
        {
            var box = RPanel(parent, new Color(1f, 1f, 1f, 0.06f));
            TopLeft(box.rectTransform, x, 14, w, 72);
            TxtTL(box.transform, label, 12, new Color(1f, 1f, 1f, 0.5f), TextAlignmentOptions.MidlineLeft, 14, 8, w - 20, 18).characterSpacing = 2f;
            var v = TxtTL(box.transform, "", 30, Color.white, TextAlignmentOptions.MidlineLeft, 14, 28, w - 24, 40, false, true);
            v.enableAutoSizing = true;
            v.fontSizeMin = 16;
            v.fontSizeMax = 30;
            return v;
        }

        void BuildVideo(RectTransform r)
        {
            videoRoot = UIFactory.Rect("Video", r).gameObject;
            var vr = (RectTransform)videoRoot.transform;
            UIFactory.Fill(vr, 0, 0, 64, 0);
            var back = UIFactory.Panel("Thumb back", vr, new Color(0.1f, 0.07f, 0.18f, 1f));
            TopLeft(back.rectTransform, 36, 16, 600, 338);
            vThumb = UIFactory.Rect("Thumb", vr).gameObject.AddComponent<RawImage>();
            vThumb.raycastTarget = false;
            TopLeft(vThumb.rectTransform, 36, 16, 600, 338);
            var play = Dot(vr, new Color(0f, 0f, 0f, 0.55f), Vector2.zero, 76);
            UIFactory.Place(play.rectTransform, new Vector2(0f, 1f), new Vector2(0.5f, 0.5f), new Vector2(336, -185), new Vector2(76, 76));
            var pt = Txt(play.transform, ">", 40, Color.white, TextAlignmentOptions.Center, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, false, true);
            UIFactory.Fill(pt.rectTransform);
            pt.overflowMode = TextOverflowModes.Overflow;
            var dChip = RPanel(vr, new Color(0f, 0f, 0f, 0.7f));
            TopLeft(dChip.rectTransform, 36 + 600 - 66, 16 + 338 - 34, 56, 24);
            vDuration = Txt(dChip.transform, "", 14, Color.white, TextAlignmentOptions.Center, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, false, true);
            UIFactory.Fill(vDuration.rectTransform);
            vTitle = TxtTL(vr, "", 23, Color.white, TextAlignmentOptions.TopLeft, 36, 364, 600, 62, true, true);
            vTitle.overflowMode = TextOverflowModes.Ellipsis;
            vMeta = TxtTL(vr, "", 15, new Color(1f, 1f, 1f, 0.75f), TextAlignmentOptions.MidlineLeft, 36, 430, 600, 22);
            vTags = TxtTL(vr, "", 14, new Color(1f, 1f, 1f, 0.5f), TextAlignmentOptions.TopLeft, 36, 454, 600, 40, true);
            vTags.overflowMode = TextOverflowModes.Ellipsis;

            // right: stats
            string[] names = { "VIEWS", "LIKES", "COMMENTS", "QUALITY" };
            var targets = new TextMeshProUGUI[4];
            for (int i = 0; i < 4; i++)
            {
                var box = RPanel(vr, new Color(1f, 1f, 1f, 0.07f));
                TopLeft(box.rectTransform, 664 + i * 152, 16, 144, 72);
                TxtTL(box.transform, names[i], 12, new Color(1f, 1f, 1f, 0.5f), TextAlignmentOptions.MidlineLeft, 14, 8, 120, 18).characterSpacing = 2f;
                targets[i] = TxtTL(box.transform, "", 30, Color.white, TextAlignmentOptions.MidlineLeft, 14, 28, 120, 40, false, true);
                targets[i].enableAutoSizing = true;
                targets[i].fontSizeMin = 16;
                targets[i].fontSizeMax = 30;
            }
            vViews = targets[0]; vLikes = targets[1]; vComments = targets[2]; vQuality = targets[3];

            // comments
            var panel = RPanel(vr, new Color(1f, 1f, 1f, 0.06f));
            TopLeft(panel.rectTransform, 664, 98, 600, 360);
            vCommentHeader = TxtTL(panel.transform, "Comments", 19, Color.white, TextAlignmentOptions.MidlineLeft, 18, 8, 400, 30, false, true);
            vNoComments = Txt(panel.transform, "No comments yet - the views are still coming in.", 17, new Color(1f, 1f, 1f, 0.55f), TextAlignmentOptions.Center,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -10), new Vector2(480, 60), true);
            for (int i = 0; i < commentRows.Length; i++)
            {
                var row = new CommentRow();
                row.rt = UIFactory.Rect("Comment", panel.transform);
                TopLeft(row.rt, 0, 44 + i * 62, 600, 60);
                var line = UIFactory.Panel("Line", row.rt, new Color(1f, 1f, 1f, 0.06f));
                UIFactory.Place(line.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), Vector2.zero, new Vector2(560, 1));
                row.avatar = Dot(row.rt, Color.white, Vector2.zero, 34);
                UIFactory.Place(row.avatar.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(34, -26), new Vector2(34, 34));
                row.initial = Txt(row.avatar.transform, "", 16, new Color32(20, 16, 34, 255), TextAlignmentOptions.Center, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, false, true);
                UIFactory.Fill(row.initial.rectTransform);
                row.user = TxtTL(row.rt, "", 14, Color.white, TextAlignmentOptions.MidlineLeft, 62, 4, 300, 20, false, true);
                row.meta = Txt(row.rt, "", 12, new Color(1f, 1f, 1f, 0.45f), TextAlignmentOptions.MidlineRight, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-22, -4), new Vector2(200, 20));
                row.text = TxtTL(row.rt, "", 14, new Color(1f, 1f, 1f, 0.85f), TextAlignmentOptions.TopLeft, 62, 24, 516, 34, true);
                row.text.overflowMode = TextOverflowModes.Ellipsis;
                commentRows[i] = row;
            }
        }

        // ------------------------------------------------------------------ data

        public void Dispose()
        {
            foreach (var t in textures.Values) if (t) Object.Destroy(t);
            textures.Clear();
        }

        Texture2D ThumbOf(ClipHub.Video v)
        {
            if (textures.TryGetValue(v.id, out var t) && t) return t;
            string path = ClipHub.ThumbPath(v);
            if (path != null && File.Exists(path))
            {
                try
                {
                    t = new Texture2D(2, 2, TextureFormat.RGB24, false);
                    if (t.LoadImage(File.ReadAllBytes(path))) { textures[v.id] = t; return t; }
                    Object.Destroy(t);
                }
                catch { /* fall through */ }
            }
            textures[v.id] = null;
            return null;
        }

        public override void Refresh()
        {
            var list = ClipHub.Videos;
            int pages = Mathf.Max(1, Mathf.CeilToInt(list.Count / (float)PerPage));
            page = Mathf.Clamp(page, 0, pages - 1);

            channelName.text = ClipHub.ChannelName;
            followersValue.text = ClipHub.Followers.ToString("N0");
            viewsValue.text = ClipHub.TotalViews.ToString("N0");
            likesValue.text = ClipHub.TotalLikes.ToString("N0");
            earnedValue.text = "$" + ClipHub.DollarsEarned;
            bool partner = ClipHub.IsPartner;
            partnerBadge.gameObject.SetActive(partner);
            partnerBar.gameObject.SetActive(!partner);
            if (partner)
                partnerText.text = $"Partner since {ClipHub.PartnerDay}  -  {ClipHub.PendingViews}/{ClipHub.ViewsPerDollar} views to the next $1";
            else
            {
                partnerText.text = $"{ClipHub.Followers}/{ClipHub.PartnerFollowers} followers to become a Partner";
                UIFactory.SetBar(partnerFill.rectTransform, ClipHub.Followers / (float)ClipHub.PartnerFollowers);
            }

            bool empty = list.Count == 0;
            emptyState.SetActive(empty);
            cardsArea.gameObject.SetActive(!empty);
            footer.gameObject.SetActive(!empty);
            pageLabel.gameObject.SetActive(!empty);
            prevButton.gameObject.SetActive(!empty);
            nextButton.gameObject.SetActive(!empty);
            pageLabel.text = $"{page + 1} / {pages}";
            prevGroup.alpha = page > 0 ? 1f : 0.3f;
            nextGroup.alpha = page < pages - 1 ? 1f : 0.3f;

            for (int i = 0; i < PerPage; i++)
            {
                var c = cards[i];
                int index = page * PerPage + i;
                c.rt.gameObject.SetActive(index < list.Count);
                if (index >= list.Count) { cards[i].videoId = 0; continue; }
                var v = list[index];
                cards[i].videoId = v.id;
                var tex = ThumbOf(v);
                c.thumb.enabled = tex != null;
                c.thumb.texture = tex;
                c.title.text = v.title;
                c.meta.text = $"{Compact(v.views)} views  -  {Compact(v.likes)} likes";
                c.day.text = "Uploaded " + v.day;
                c.duration.text = Fmt(v.seconds);
                c.score.text = "Q " + v.quality;
            }

            bool inVideo = openVideo != 0 && ClipHub.Find(openVideo) != null;
            if (!inVideo) openVideo = 0;
            channelRoot.SetActive(!inVideo);
            videoRoot.SetActive(inVideo);
            openStudio.gameObject.SetActive(!inVideo);
            backButton.gameObject.SetActive(inVideo);
            if (inVideo) RefreshVideo(ClipHub.Find(openVideo));
        }

        void RefreshVideo(ClipHub.Video v)
        {
            var tex = ThumbOf(v);
            vThumb.enabled = tex != null;
            vThumb.texture = tex;
            vDuration.text = Fmt(v.seconds);
            vTitle.text = v.title;
            vMeta.text = $"{v.views:N0} views  -  uploaded {v.day}";
            var tags = new List<string> { v.ranked ? "Ranked" : "Warm-up", v.CamName };
            if (v.kills > 0) tags.Add($"{v.kills} kill{(v.kills == 1 ? "" : "s")}");
            if (v.heads > 0) tags.Add($"{v.heads} headshot{(v.heads == 1 ? "" : "s")}");
            if (v.captures > 0) tags.Add($"{v.captures} capture{(v.captures == 1 ? "" : "s")}");
            vTags.text = string.Join("  -  ", tags);
            vViews.text = v.views.ToString("N0");
            vLikes.text = v.likes.ToString("N0");
            int cc = ClipHub.CommentCount(v);
            vComments.text = cc.ToString("N0");
            vQuality.text = v.quality + "/100";
            vCommentHeader.text = cc > 0 ? $"Comments ({cc})" : "Comments";
            vNoComments.gameObject.SetActive(cc == 0);
            var comments = ClipHub.Comments(v, Mathf.Min(cc, commentRows.Length));
            for (int i = 0; i < commentRows.Length; i++)
            {
                var row = commentRows[i];
                row.rt.gameObject.SetActive(i < comments.Count);
                if (i >= comments.Count) continue;
                var c = comments[i];
                row.avatar.color = Color.HSVToRGB((HashOf(c.user) % 100) / 100f, 0.45f, 0.95f);
                row.initial.text = c.user.Substring(0, 1);
                row.user.text = c.user;
                row.meta.text = $"{c.ago}  -  {c.likes} likes";
                row.text.text = c.text;
            }
        }

        static int HashOf(string s)
        {
            int h = 7;
            foreach (char ch in s) h = h * 31 + ch;
            return Mathf.Abs(h % 100000);
        }

        public void ShowVideo(int id)
        {
            openVideo = id;
            Refresh();
        }

        // ------------------------------------------------------------------ input

        public override void Tick()
        {
            openStudioBg.color = d.Over(openStudio) ? new Color(0.85f, 0.45f, 1f, 1f) : new Color(0.75f, 0.31f, 1f, 1f);
            backBg.color = new Color(1f, 1f, 1f, d.Over(backButton) ? 0.26f : 0.14f);
            if (channelRoot.activeSelf)
            {
                prevBg.color = new Color(1f, 1f, 1f, page > 0 && d.Over(prevButton) ? 0.22f : 0.1f);
                nextBg.color = new Color(1f, 1f, 1f, nextGroup.alpha > 0.5f && d.Over(nextButton) ? 0.22f : 0.1f);
                emptyButtonBg.color = d.Over(emptyButton) ? new Color(0.85f, 0.45f, 1f, 1f) : new Color(0.75f, 0.31f, 1f, 1f);
                foreach (var c in cards)
                    if (c.rt.gameObject.activeSelf) c.bg.color = new Color(1f, 1f, 1f, d.Over(c.rt) ? 0.14f : 0.07f);
            }
        }

        public override bool Click()
        {
            if (openVideo != 0)
            {
                if (d.Over(backButton)) { openVideo = 0; Refresh(); Click1(); return true; }
                return false;
            }
            if (d.Over(openStudio) || (emptyState.activeSelf && d.Over(emptyButton))) { d.OpenApp("clips"); return true; }
            if (d.Over(prevButton)) { Go(-1); return true; }
            if (d.Over(nextButton)) { Go(1); return true; }
            foreach (var c in cards)
                if (c.rt.gameObject.activeSelf && d.Over(c.rt)) { ShowVideo(c.videoId); Click1(); return true; }
            return false;
        }

        public override void Scroll(float direction)
        {
            if (openVideo == 0 && d.Over(root)) Go(direction > 0f ? -1 : 1);
        }

        void Go(int delta)
        {
            int before = page;
            page += delta;
            Refresh(); // clamps
            if (page != before) Click1();
        }

        void Click1() => Fx.Play(Fx.Click, d.pc ? d.pc.transform.position : Vector3.zero, 0.2f, 1.6f);
    }
}
