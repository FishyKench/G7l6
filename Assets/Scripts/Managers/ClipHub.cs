using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// ClipHub: your channel. Clips you upload from the Clip Studio get views, likes and followers that trickle in over
/// game time (the curve comes from the quality score). From 100 followers you are a Partner: every 500 views pay $1
/// into the Wallet (Category.Views). Saved in PlayerPrefs (channel + videos, small) with thumbnails as small PNGs
/// in the persistent data path. Other systems can read <see cref="Followers"/> (LiveLoop uses it for viewers).
/// </summary>
public static class ClipHub
{
    public const string ChannelName = "NoScope_Nugget";
    public const string Handle = "@noscope.nugget";
    public const int PartnerFollowers = 100;
    public const int ViewsPerDollar = 500;
    /// <summary>Game minutes that pass overnight (every new day / scene load).</summary>
    public const float OvernightMinutes = 600f;
    const string Key = "G7l6.ClipHub";
    const int MaxVideos = 40;

    [Serializable]
    public class Video
    {
        public int id, seed;
        public string title, day, thumb;
        public float uploadMin;
        public int quality, cam, seconds, kills, heads, captures, deaths;
        public bool ranked;
        public float target, carryFollowers;
        public int views, likes;
        public int followersAtUpload;

        public string CamName => cam == 0 ? "first person" : cam == 1 ? "third person" : "orbit cam";
    }

    [Serializable]
    class Data
    {
        public int version = 1;
        public float minutes;
        public int followers, nextId = 1, unpaidViews, dollarsEarned;
        public bool partner;
        public string partnerDay = "";
        public List<Video> videos = new List<Video>();
    }

    static Data data;
    static float lastSave;
    static bool dirty;

    public static event Action Changed;
    /// <summary>Raised when views pay out: dollars, views counted.</summary>
    public static event Action<int, int> Paid;
    public static event Action BecamePartner;
    public static event Action<Video, int> Milestone;

    static Data D { get { Load(); return data; } }

    public static int Followers => D.followers;
    public static bool IsPartner => D.partner;
    public static string PartnerDay => D.partnerDay;
    public static int DollarsEarned => D.dollarsEarned;
    public static int PendingViews => D.unpaidViews;
    public static float Minutes => D.minutes;
    /// <summary>Newest first.</summary>
    public static List<Video> Videos { get { var l = new List<Video>(D.videos); l.Reverse(); return l; } }
    public static int VideoCount => D.videos.Count;

    public static int TotalViews { get { int n = 0; foreach (var v in D.videos) n += v.views; return n; } }
    public static int TotalLikes { get { int n = 0; foreach (var v in D.videos) n += v.likes; return n; } }

    public static Video Find(int id) => D.videos.Find(v => v.id == id);

    // ------------------------------------------------------------------ the maths

    /// <summary>How many views a clip of this quality is heading for (before luck), with this many followers.</summary>
    public static float BaseViews(int quality, int followers)
    {
        float q = Mathf.Clamp(quality, 0, 100);
        float viral = q >= 88f ? 1.5f : 1f;
        return (10f + q * q * 0.22f) * (1f + followers / 120f) * viral;
    }

    /// <summary>A believable range to show in the editor ("about 400 - 600").</summary>
    public static void ExpectedRange(int quality, int followers, out int low, out int high)
    {
        float b = BaseViews(quality, followers);
        low = Mathf.RoundToInt(b * 0.8f);
        high = Mathf.RoundToInt(b * 1.25f);
    }

    /// <summary>Share of viewers who follow, by quality.</summary>
    public static float FollowRate(int quality) => 0.012f + 0.0004f * Mathf.Clamp(quality, 0, 100);
    public static float LikeRate(int quality) => 0.04f + 0.0008f * Mathf.Clamp(quality, 0, 100);

    /// <summary>The share of the final views a clip has by now (1 - e^(age / 120 game minutes)).</summary>
    public static float Growth(float ageMinutes) => ageMinutes <= 0f ? 0f : 1f - Mathf.Exp(-ageMinutes / 120f);

    // ------------------------------------------------------------------ uploading

    public static Video Upload(string title, int quality, int cam, int seconds, int kills, int heads, int captures, int deaths, bool ranked, Texture2D thumb)
    {
        var d = D;
        var v = new Video
        {
            id = d.nextId++,
            seed = UnityEngine.Random.Range(1, 1000000),
            title = string.IsNullOrWhiteSpace(title) ? "Untitled clip" : title.Trim(),
            day = Replay.NowStamp(),
            uploadMin = d.minutes,
            quality = Mathf.Clamp(quality, 0, 100),
            cam = cam, seconds = seconds, kills = kills, heads = heads, captures = captures, deaths = deaths, ranked = ranked,
            followersAtUpload = d.followers,
        };
        v.target = BaseViews(v.quality, d.followers) * UnityEngine.Random.Range(0.82f, 1.22f);
        v.thumb = SaveThumb(v, thumb);
        d.videos.Add(v);
        while (d.videos.Count > MaxVideos)
        {
            DeleteThumb(d.videos[0]);
            d.videos.RemoveAt(0);
        }
        Save(true);
        Changed?.Invoke();
        return v;
    }

    static string Folder => Path.Combine(Application.persistentDataPath, "ClipHub");

    public static string ThumbPath(Video v) => string.IsNullOrEmpty(v.thumb) ? null : Path.Combine(Folder, v.thumb);

    static string SaveThumb(Video v, Texture2D thumb)
    {
        if (!thumb) return "";
        try
        {
            Directory.CreateDirectory(Folder);
            string name = $"thumb_{v.id}_{v.seed}.png";
            File.WriteAllBytes(Path.Combine(Folder, name), thumb.EncodeToPNG());
            return name;
        }
        catch (Exception e)
        {
            Debug.LogWarning("[ClipHub] Couldn't save the thumbnail: " + e.Message);
            return "";
        }
    }

    static void DeleteThumb(Video v)
    {
        try { var p = ThumbPath(v); if (p != null && File.Exists(p)) File.Delete(p); } catch { /* not important */ }
    }

    // ------------------------------------------------------------------ time passing

    /// <summary>Lets game time pass: views, likes and followers trickle in; partners get paid.</summary>
    public static void Advance(float gameMinutes)
    {
        var d = D;
        d.minutes += Mathf.Max(0f, gameMinutes);
        bool any = false;
        int newViews = 0;
        foreach (var v in d.videos)
        {
            int views = Mathf.FloorToInt(v.target * Growth(d.minutes - v.uploadMin));
            if (views <= v.views) continue;
            int gain = views - v.views;
            int oldViews = v.views;
            v.views = views;
            v.likes = Mathf.FloorToInt(views * LikeRate(v.quality));
            v.carryFollowers += gain * FollowRate(v.quality);
            int f = Mathf.FloorToInt(v.carryFollowers);
            if (f > 0) { v.carryFollowers -= f; d.followers += f; }
            newViews += gain;
            any = true;
            foreach (int m in new[] { 100, 500, 1000, 5000, 10000 })
                if (oldViews < m && views >= m) Milestone?.Invoke(v, m);
        }
        if (!any) return;

        if (!d.partner && d.followers >= PartnerFollowers)
        {
            d.partner = true;
            d.partnerDay = Wallet.DayLabel;
            BecamePartner?.Invoke();
        }
        if (d.partner)
        {
            d.unpaidViews += newViews;
            int dollars = d.unpaidViews / ViewsPerDollar;
            if (dollars > 0)
            {
                d.unpaidViews -= dollars * ViewsPerDollar;
                d.dollarsEarned += dollars;
                Wallet.Earn(dollars, $"ClipHub - {dollars * ViewsPerDollar} views", Wallet.Category.Views);
                Paid?.Invoke(dollars, dollars * ViewsPerDollar);
            }
        }
        Save(false);
        Changed?.Invoke();
    }

    // ------------------------------------------------------------------ comments (made up, from the clip itself)

    static readonly string[] Names = { "Pixel", "Turbo", "Mango", "Glitch", "Waffle", "Ninja", "Cosmo", "Bonk", "Zesty", "Sir", "Captain", "Doge", "Salsa", "Yeet", "Moss", "Noodle" };
    static readonly string[] Tails = { "Gamer", "Cat", "Lord", "Fan", "Main", "Bean", "Goblin", "Dad", "Bro", "Wizard", "Toast", "Sniper", "Pilot", "Frog" };

    public struct Comment { public string user, text, ago; public int likes; }

    public static int CommentCount(Video v) => Mathf.Min(48, v.views / 12);

    /// <summary>The first 'count' comments of a video (always the same ones for a video).</summary>
    public static List<Comment> Comments(Video v, int count)
    {
        var list = new List<Comment>();
        var rng = new System.Random(v.seed);
        var pool = new List<string>
        {
            "ok that was actually clean", "how are you not Diamond yet", "my cat could not have done this (she tried)",
            "the camera work goes hard", "subscribed just for this", "this is why I do not play ranked",
            "bro really said no thanks", "the enemy team: Oh no", "clip of the week fr", "rewatched this 6 times",
            "I was here before it blew up", "ok but what sensitivity do you use", "that one guy never saw it coming",
            "mom is going to see you are playing again lol", "more of this please", "the audio could be better but the play is great",
        };
        if (v.kills >= 3) { pool.Add($"{v.kills} kills in one clip?? this is illegal"); pool.Add("triple kill energy, respect"); pool.Add("the streak at the end is unreal"); }
        else if (v.kills >= 1) { pool.Add("clean pick, nice timing"); pool.Add("one shot is all you need"); }
        if (v.heads >= 1) { pool.Add("that headshot made my day"); pool.Add("headshots only, I see you"); }
        if (v.captures >= 1) { pool.Add("capturing the point under fire is brave"); pool.Add("point captured, vibes immaculate"); }
        if (v.deaths >= 1) { pool.Add("the death at the end hurt me too"); pool.Add("F in the chat for that last fight"); }
        if (v.cam == 2) { pool.Add("the orbit cam is so smooth"); pool.Add("who edited this, it looks like a movie"); }
        else if (v.cam == 0) { pool.Add("first person makes me dizzy but I love it"); pool.Add("POV is the best way to watch this"); }
        else pool.Add("third person is a nice change");
        if (v.quality >= 80) { pool.Add("this needs more views"); pool.Add("absolutely elite clip"); }
        else if (v.quality < 35) { pool.Add("a bit slow at the start but ok"); pool.Add("keep going, you will get better"); }
        string[] when = { "just now", "5 min ago", "20 min ago", "1 hour ago", "2 hours ago", "3 hours ago", "yesterday", "yesterday" };
        for (int i = 0; i < count; i++)
        {
            string user = Names[rng.Next(Names.Length)] + Tails[rng.Next(Tails.Length)] + rng.Next(2, 99);
            list.Add(new Comment { user = user, text = pool[rng.Next(pool.Count)], likes = rng.Next(0, 120), ago = when[Mathf.Min(when.Length - 1, i)] });
        }
        return list;
    }

    // ------------------------------------------------------------------ saving

    static void Load()
    {
        if (data != null) return;
        try
        {
            string json = PlayerPrefs.GetString(Key, "");
            data = string.IsNullOrEmpty(json) ? new Data() : JsonUtility.FromJson<Data>(json);
        }
        catch { data = null; }
        if (data == null || data.videos == null) data = new Data();
    }

    public static void Save(bool force)
    {
        if (data == null) return;
        dirty = true;
        if (!force && Time.realtimeSinceStartup - lastSave < 5f) return;
        lastSave = Time.realtimeSinceStartup;
        dirty = false;
        PlayerPrefs.SetString(Key, JsonUtility.ToJson(data));
        PlayerPrefs.Save();
    }

    /// <summary>Writes pending changes (called when the scene goes away).</summary>
    public static void Flush() { if (dirty) Save(true); }

    /// <summary>Debug: forget the channel and its videos (recordings are kept).</summary>
    public static void ResetAll()
    {
        if (data != null) foreach (var v in data.videos) DeleteThumb(v);
        PlayerPrefs.DeleteKey(Key);
        data = null;
        dirty = false;
        Changed?.Invoke();
    }

    public static void NotifyChanged() => Changed?.Invoke();
}
