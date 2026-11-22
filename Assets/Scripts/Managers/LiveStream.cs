using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>
/// LiveLoop streaming (static, like <see cref="Wallet"/> / <see cref="ClipHub"/>): gear ladder, go-live state, the
/// viewer count, fake chat, tips and the saved list of past streams. <see cref="LiveStreamRuntime"/> ticks it every frame.
///
/// GEAR LADDER (all read from <see cref="Shop.IsInstalled"/>):
///   Webcam + mic            required to go live. Alone: LAGGY, base 4 viewers, hard cap of 25.
///   Ring light + green screen  viewers join a bit more (x1.25) and leave 40% slower. Without a graphics card the meter reads OK.
///   Graphics card upgrade   SMOOTH, base 12 viewers (x3), no cap.
///   Streaming PC + capture card  PRO, base 26 viewers.
/// Target viewers = base * (1 + ClipHub.Followers / 200) * ring * hype * a slowly changing random factor, capped.
///
/// WHILE LIVE: playing VR (headset on, in the arena) -> viewers grow toward the target; headset off after you played -> they
/// drain fast; before you play only a trickle of waiting viewers. Your kills / multi-kills / flag captures / round wins
/// (called from VRArena) bring chat and tips. Tips are paid to the wallet in batches (every 30 s and when the stream ends)
/// as "LiveLoop tips" so the Bank isn't flooded. The stream ends when you toggle it off, the ranked match is over, the day ends,
/// or everybody left after you took the headset off. Streams are saved (date, peak viewers, tips, length).
/// </summary>
public static class LiveStream
{
    public enum Quality { Laggy, OK, Smooth, Pro }
    public enum Mode { Waiting, Playing, Away }

    public struct Past { public string day; public int peak, tipCents, seconds, quality; }

    public class ChatLine { public string name, text; public Color32 colour; public float time; public bool tip; }
    public class TipPop { public string name; public int dollars; public float time; }

    public const float ChatLife = 12f, TipPopLife = 3.6f;
    public const int MaxPast = 40, MaxChat = 40;

    const string PastKey = "G7l6.LiveLoop.Past", TotalKey = "G7l6.LiveLoop.Totals";

    /// <summary>Went live / stream ended / the past list changed.</summary>
    public static event Action Changed;

    // ------------------------------------------------------------------ live state

    public static bool IsLive { get; private set; }
    public static Mode Status { get; private set; } = Mode.Waiting;
    public static int Viewers => Mathf.RoundToInt(viewersF);
    public static int Peak { get; private set; }
    /// <summary>Dollars tipped during this stream (paid out in batches).</summary>
    public static int TipsTotal { get; private set; }
    public static float Elapsed { get; private set; }
    /// <summary>Debug: act as if you were playing a match.</summary>
    public static bool DebugPlaying;

    static float viewersF, hype, noise = 1f, noiseTimer, chatTimer, payTimer, zeroFor;
    static bool everPlayed, sawMatch;
    static int pendingTips, pendingCount;
    static float matchCheck;
    static MatchTask match;

    static readonly List<ChatLine> chat = new List<ChatLine>();
    static readonly List<TipPop> pops = new List<TipPop>();
    static readonly List<(float at, string name, int dollars)> bursts = new List<(float, string, int)>();

    public static IReadOnlyList<ChatLine> ChatLines => chat;
    public static IReadOnlyList<TipPop> TipPops => pops;

    // ------------------------------------------------------------------ gear

    public static bool HasCamera => Shop.IsInstalled(Shop.Webcam);
    public static bool HasRing => Shop.IsInstalled(Shop.RingLight);
    public static bool HasGpu => Shop.IsInstalled(Shop.GraphicsCard);
    public static bool HasPro => Shop.IsInstalled(Shop.StreamingPC);

    public static Quality CurrentQuality => HasPro ? Quality.Pro : HasGpu ? Quality.Smooth : HasRing ? Quality.OK : Quality.Laggy;
    public static string QualityName(Quality q) => q == Quality.Laggy ? "Laggy" : q == Quality.OK ? "OK" : q == Quality.Smooth ? "Smooth" : "Pro";

    static float BaseViewers => HasPro ? 26f : HasGpu ? 12f : 4f;
    /// <summary>Friday's tournament match doubles the audience (Campaign.TournamentLive).</summary>
    static float TournamentMult => Campaign.TournamentLive ? 2f : 1f;
    static float Cap => (HasGpu || HasPro ? 9999f : 25f) * TournamentMult;
    static float Followers => Mathf.Max(0, ClipHub.Followers);

    /// <summary>Typical viewers you can reach with the gear you have (before the random swings).</summary>
    public static float TargetViewers => Mathf.Min(Cap, BaseViewers * (1f + Followers / 200f) * (HasRing ? 1.25f : 1f) * TournamentMult);

    public static void ExpectedRange(out int low, out int high)
    {
        float t = TargetViewers;
        low = Mathf.Max(1, Mathf.RoundToInt(t * 0.8f));
        high = Mathf.Max(low, Mathf.RoundToInt(Mathf.Min(Cap, t * 1.3f)));
    }

    /// <summary>Mom grounded you (F on yesterday's report card): no PC / streaming today. The flag is only meaningful while the day runs.</summary>
    public static bool BannedToday
    {
        get
        {
            if (!Wallet.PcBannedTomorrow && Trust.CanStream) return false;
            var day = DayManager.Instance;
            return !day || day.State != DayManager.Phase.Report;
        }
    }

    /// <summary>Null when you can go live, otherwise the reason (shown in LiveLoop and as a toast).</summary>
    public static string CannotGoLive()
    {
        if (BannedToday) return Wallet.PcBannedTomorrow
            ? "Grounded - no streaming today. Mom took your streaming privileges after the F on your report card."
            : $"Mom says no streaming until she trusts you again (trust {Trust.Value}/{Trust.StreamNeeds}). Good report cards win it back.";
        if (!HasCamera)
        {
            switch (Shop.StateOf(Shop.Webcam))
            {
                case Shop.State.Ordered: return "Your webcam + mic arrive tomorrow morning. Once they are installed you can go live.";
                case Shop.State.Delivered: return "Your webcam + mic box is waiting by the front door. Open it to install it, then you can go live.";
                default: return "You need a webcam + mic to go live. Buy the starter kit on ShopNest - it arrives the next morning.";
            }
        }
        return null;
    }

    // ------------------------------------------------------------------ go live / end

    public static bool Toggle()
    {
        if (IsLive) { End(null); return true; }
        return GoLive();
    }

    public static bool GoLive()
    {
        if (IsLive) return true;
        string why = CannotGoLive();
        if (why != null)
        {
            GameHUD.Toast(BannedToday ? "Grounded - no streaming today." : "You need a webcam + mic to go live.", GameHUD.Warn);
            return false;
        }
        IsLive = true;
        Status = Mode.Waiting;
        viewersF = 0f; hype = 0f; noise = 1f; noiseTimer = 0f; chatTimer = 3f; payTimer = 0f; zeroFor = 0f;
        Peak = 0; TipsTotal = 0; Elapsed = 0f;
        everPlayed = false; sawMatch = false;
        pendingTips = pendingCount = 0;
        chat.Clear(); pops.Clear(); bursts.Clear();
        GameHUD.Toast("You are LIVE on LiveLoop! Put on the headset and play to get viewers.", GameHUD.Good);
        Changed?.Invoke();
        return true;
    }

    /// <summary>Ends the stream: pays the tips, saves it to the past streams and shows a summary toast.</summary>
    public static void End(string why)
    {
        if (!IsLive) return;
        FlushBursts(true);
        PayTips();
        IsLive = false;
        Status = Mode.Waiting;
        bool keep = Elapsed >= 5f;
        if (keep) SavePast(new Past { day = DayStamp(), peak = Peak, tipCents = TipsTotal * 100, seconds = Mathf.RoundToInt(Elapsed), quality = (int)CurrentQuality });
        string head = string.IsNullOrEmpty(why) ? "Stream ended" : "Stream ended - " + why;
        GameHUD.Toast(Peak > 0 || TipsTotal > 0 ? $"{head}. Peak {Peak} viewers, ${TipsTotal} in tips." : $"{head}. Nobody watched this time.", Peak > 0 ? GameHUD.Good : GameHUD.Info);
        viewersF = 0f;
        pops.Clear();
        Changed?.Invoke();
    }

    // ------------------------------------------------------------------ every frame

    public static void Tick(float dt)
    {
        if (!IsLive) return;
        Elapsed += dt;
        var day = DayManager.Instance;
        if (day && day.State == DayManager.Phase.Report) { End("the day is over"); return; }
        if (BannedToday) { End("grounded"); return; }
        if (!HasCamera) { End("the webcam is gone"); return; }

        var arena = VRArena.Instance;
        bool vr = DebugPlaying || (arena && arena.Player && arena.Player.controlled);
        if (vr) everPlayed = true;
        Status = vr ? Mode.Playing : everPlayed ? Mode.Away : Mode.Waiting;

        // The ranked match ending ends the stream (if it was running while you were live).
        matchCheck -= dt;
        if (matchCheck <= 0f)
        {
            matchCheck = 1f;
            if (!match) match = UnityEngine.Object.FindObjectOfType<MatchTask>();
        }
        if (match)
        {
            if (match.State == MatchTask.Phase.Round || match.State == MatchTask.Phase.Break) sawMatch = true;
            else if (match.State == MatchTask.Phase.Over && sawMatch) { End("the ranked match is over"); return; }
        }

        // ---- viewers
        noiseTimer -= dt;
        if (noiseTimer <= 0f) { noise = UnityEngine.Random.Range(0.8f, 1.25f); noiseTimer = UnityEngine.Random.Range(3f, 7f); }
        hype = Mathf.Max(0f, hype - dt * 0.08f);
        float target = Mathf.Min(Cap, TargetViewers * noise * (1f + hype));
        switch (Status)
        {
            case Mode.Playing:
                viewersF = Mathf.MoveTowards(viewersF, target, Mathf.Max(0.35f, Mathf.Abs(target - viewersF) * 0.1f) * dt);
                break;
            case Mode.Waiting:
                viewersF = Mathf.MoveTowards(viewersF, target * 0.15f, Mathf.Max(0.12f, Mathf.Abs(target * 0.15f - viewersF) * 0.05f) * dt);
                break;
            default: // headset off: they leave fast (the ring light keeps some of them a bit longer)
                viewersF = Mathf.Max(0f, viewersF - (viewersF * (HasRing ? 0.25f : 0.4f) + 0.6f) * dt);
                break;
        }
        if (viewersF > Cap) viewersF = Cap;
        if (Viewers > Peak) Peak = Viewers;
        zeroFor = Status == Mode.Away && viewersF < 0.05f ? zeroFor + dt : 0f;
        if (zeroFor > 12f) { End("everybody left"); return; }

        // ---- chat
        chatTimer -= dt;
        if (chatTimer <= 0f)
        {
            chatTimer = viewersF < 0.5f ? 4f : UnityEngine.Random.Range(1.4f, 3.6f) * Mathf.Lerp(2.2f, 0.55f, Mathf.Clamp01(viewersF / 60f));
            if (viewersF >= 0.5f) IdleChat();
        }

        // ---- tips: queued bursts show up one by one, the money is paid every 30 s
        FlushBursts(false);
        payTimer += dt;
        if (payTimer >= 30f) { payTimer = 0f; PayTips(); }
        for (int i = pops.Count - 1; i >= 0; i--) if (Time.time - pops[i].time > TipPopLife) pops.RemoveAt(i);
        while (chat.Count > MaxChat) chat.RemoveAt(0);
    }

    // ------------------------------------------------------------------ events from the match (VRArena calls these)

    public static void OnPlayerKill(bool head, int streak)
    {
        if (!IsLive) return;
        hype = Mathf.Min(0.8f, hype + 0.05f + (streak >= 2 ? 0.15f : 0f));
        if (streak >= 2) { Say(Multi); if (UnityEngine.Random.value < 0.6f) Say(Multi); }
        else if (head) Say(Head);
        else if (UnityEngine.Random.value < 0.7f) Say(Kill);
        float p = TipChance();
        if (streak >= 2)
        {
            int count = Mathf.Min(5, streak);
            int tips = 0;
            for (int i = 0; i < count; i++) if (UnityEngine.Random.value < p * 2.5f) { QueueTip(tips * 0.35f + 0.2f); tips++; }
            if (tips == 0 && streak >= 3 && viewersF >= 3f) QueueTip(0.3f);
        }
        else if (UnityEngine.Random.value < p * (head ? 1.5f : 1f)) QueueTip(0.3f);
    }

    public static void OnPlayerDeath()
    {
        if (!IsLive) return;
        if (UnityEngine.Random.value < 0.75f) Say(Death);
    }

    /// <summary>A capture point changed hands. owner: 0 = your team took it, 1 = the enemy did, -1 = neutralised.</summary>
    public static void OnFlag(int owner)
    {
        if (!IsLive) return;
        if (owner == 0)
        {
            hype = Mathf.Min(0.8f, hype + 0.06f);
            Say(Capture);
            if (UnityEngine.Random.value < TipChance() * 1.2f) QueueTip(0.4f);
        }
        else if (UnityEngine.Random.value < 0.6f) Say(Lost);
    }

    public static void OnRoundEnd(bool won)
    {
        if (!IsLive) return;
        Say(won ? Win : Loss);
        if (UnityEngine.Random.value < 0.6f) Say(won ? Win : Loss);
        if (!won) return;
        hype = Mathf.Min(0.8f, hype + 0.2f);
        int count = viewersF >= 2f ? Mathf.Clamp(Mathf.RoundToInt(viewersF / 8f), 1, 5) : 0;
        for (int i = 0; i < count; i++) QueueTip(0.3f + i * 0.35f);
    }

    /// <summary>Debug: a fake kill (+ tips) so the overlay and chat can be tested without playing.</summary>
    public static void DebugEvent()
    {
        if (!IsLive) return;
        viewersF = Mathf.Max(viewersF, Mathf.Min(Cap, TargetViewers * 0.6f + 2f));
        OnPlayerKill(UnityEngine.Random.value < 0.5f, UnityEngine.Random.Range(1, 4));
        OnFlag(0);
        QueueTip(0.2f);
    }

    static float TipChance() => Mathf.Clamp(0.04f + viewersF * 0.012f, 0.04f, 0.6f);

    static readonly int[] TipAmounts = { 1, 1, 1, 2, 2, 3, 5 };

    static void QueueTip(float delay)
    {
        if (viewersF < 0.5f) return;
        bursts.Add((Time.time + delay, RandomName(), TipAmounts[UnityEngine.Random.Range(0, TipAmounts.Length)]));
    }

    static void FlushBursts(bool all)
    {
        for (int i = bursts.Count - 1; i >= 0; i--)
        {
            // (iterate backwards but queue order matters little; they are all a fraction of a second apart)
            if (!all && Time.time < bursts[i].at) continue;
            var b = bursts[i];
            bursts.RemoveAt(i);
            TipsTotal += b.dollars;
            pendingTips += b.dollars;
            pendingCount++;
            AddChat(b.name, $"tipped ${b.dollars}!", true);
            pops.Insert(0, new TipPop { name = b.name, dollars = b.dollars, time = Time.time });
            if (pops.Count > 3) pops.RemoveAt(pops.Count - 1);
        }
    }

    static void PayTips()
    {
        if (pendingTips <= 0) return;
        Wallet.Earn(pendingTips, $"LiveLoop tips ({pendingCount} tip{(pendingCount == 1 ? "" : "s")})", Wallet.Category.Tips);
        pendingTips = pendingCount = 0;
    }

    // ------------------------------------------------------------------ fake chat

    static readonly string[] Names =
    {
        "PixelPanda", "NoodleKing", "xXcatfanXx", "BigBadBob", "LunaPlays", "toastyboi", "zap_zap", "MangoMike", "retro_rae", "KiwiKira",
        "SirLagALot", "TinyTim99", "Pancake_Pro", "GigaGoose", "waffle_wolf", "MissMuffin", "CoolCucumber", "Bubbles42", "DJ_Dino", "FuzzyLogic",
        "SnackAttack", "RocketRaccoon", "ZenPenguin", "ChillChip", "MrBanana", "NinjaNana", "StarlightSue", "QuackAttack", "CosmicCarl", "JellyBean77",
        "HappyHippo", "TurboTurtle", "OtterOllie", "SleepyFox", "BeepBoop", "ChewyChloe", "DaisyDrift", "PepperJack", "Sir_Reginald", "tacotuesday",
    };

    static readonly Color32[] NameColours =
    {
        new Color32(255, 120, 140, 255), new Color32(255, 190, 80, 255), new Color32(120, 220, 140, 255), new Color32(100, 200, 255, 255),
        new Color32(190, 140, 255, 255), new Color32(255, 150, 220, 255), new Color32(120, 240, 220, 255), new Color32(255, 160, 90, 255),
    };

    static readonly string[] Kill = { "nice one!", "clean shot", "he never saw it", "pog", "that aim though", "wow", "oooh", "boom!", "sit down", "so smooth" };
    static readonly string[] Head = { "HEADSHOT!!", "pop!", "right in the head!", "laser aim", "ONE TAP", "bonk" };
    static readonly string[] Multi = { "DOUBLE!", "he is on fire", "clip it!!", "UNSTOPPABLE", "is this real?!", "this guy is cracked", "KILLING SPREE" };
    static readonly string[] Death = { "oof", "rip", "unlucky", "he came from behind", "press F", "respawn time", "so close", "shake it off" };
    static readonly string[] Capture = { "point secured", "nice cap", "holding the point", "flag is ours!", "good teamwork", "capture!" };
    static readonly string[] Lost = { "they took it back", "rotate!", "defend the point", "oh no, we lost it" };
    static readonly string[] Win = { "GG", "WIN WIN WIN", "that was close", "let's gooo", "GG well played", "ez round" };
    static readonly string[] Loss = { "next round!", "shake it off", "we go again", "so close!", "you got this" };
    static readonly string[] Waiting = { "hello chat", "first!", "is it starting?", "waiting for the match", "hi!", "what game today?", "love the room", "hello from far away" };
    static readonly string[] Playing = { "go go go", "watch your back", "love this map", "what rank is this?", "is that your cat?", "play the objective", "nice movement", "this is fun", "so tense" };
    static readonly string[] Away = { "where did he go?", "hello??", "brb?", "is he afk?", "come back!", "stream is dead lol" };
    static readonly string[] LaggyChat = { "so laggy...", "buffering again", "i can barely see", "my eyes... the lag", "stream is a slideshow", "get a better pc lol" };
    static readonly string[] OkChat = { "looks decent", "nice lighting", "ok quality", "better than before" };
    static readonly string[] SmoothChat = { "so smooth!", "buttery frames", "smooth stream", "no lag at all" };
    static readonly string[] ProChat = { "crispy picture", "this looks pro", "studio quality!", "the colours!", "4K energy", "best stream ever" };

    static string RandomName() => Names[UnityEngine.Random.Range(0, Names.Length)];

    static void Say(string[] pool) => AddChat(RandomName(), pool[UnityEngine.Random.Range(0, pool.Length)], false);

    static void IdleChat()
    {
        if (Status == Mode.Away) { Say(Away); return; }
        if (UnityEngine.Random.value < 0.25f)
        {
            switch (CurrentQuality)
            {
                case Quality.Laggy: Say(LaggyChat); return;
                case Quality.OK: Say(OkChat); return;
                case Quality.Smooth: Say(SmoothChat); return;
                default: Say(ProChat); return;
            }
        }
        Say(Status == Mode.Waiting ? Waiting : Playing);
    }

    static void AddChat(string name, string text, bool tip)
    {
        int h = 17;
        foreach (char c in name) h = h * 31 + c;
        chat.Add(new ChatLine { name = name, text = text, tip = tip, time = Time.time, colour = NameColours[Mathf.Abs(h) % NameColours.Length] });
        if (chat.Count > MaxChat) chat.RemoveAt(0);
    }

    /// <summary>The last few chat lines that haven't faded yet, oldest first.</summary>
    public static void Recent(int count, List<ChatLine> into)
    {
        into.Clear();
        for (int i = chat.Count - 1; i >= 0 && into.Count < count; i--)
        {
            if (Time.time - chat[i].time > ChatLife) break;
            into.Insert(0, chat[i]);
        }
    }

    // ------------------------------------------------------------------ past streams

    static readonly List<Past> past = new List<Past>(); // oldest first
    static bool loaded;
    static int totalStreams, totalTipCents, totalSeconds, bestPeak;

    public static int TotalStreams { get { Load(); return totalStreams; } }
    public static int TotalTipCents { get { Load(); return totalTipCents; } }
    public static int TotalSeconds { get { Load(); return totalSeconds; } }
    public static int BestPeak { get { Load(); return bestPeak; } }

    /// <summary>Newest first.</summary>
    public static List<Past> PastStreams
    {
        get
        {
            Load();
            var l = new List<Past>(past);
            l.Reverse();
            return l;
        }
    }

    static void SavePast(Past p)
    {
        Load();
        past.Add(p);
        while (past.Count > MaxPast) past.RemoveAt(0);
        totalStreams++;
        totalTipCents += p.tipCents;
        totalSeconds += p.seconds;
        bestPeak = Mathf.Max(bestPeak, p.peak);
        Save();
    }

    static string DayStamp()
    {
        string time = "";
        var day = DayManager.Instance;
        if (day)
        {
            int h = Mathf.FloorToInt(day.Hour), m = Mathf.FloorToInt((day.Hour - h) * 60f);
            time = $" - {(h > 12 ? h - 12 : h)}:{m:00} {(h >= 12 ? "PM" : "AM")}";
        }
        return Wallet.DayLabel + time;
    }

    public static string FormatTime(float seconds)
    {
        int s = Mathf.Max(0, Mathf.RoundToInt(seconds));
        return s >= 3600 ? $"{s / 3600}:{s / 60 % 60:00}:{s % 60:00}" : $"{s / 60}:{s % 60:00}";
    }

    public static void ResetAll()
    {
        IsLive = false;
        Status = Mode.Waiting;
        viewersF = 0f;
        pendingTips = pendingCount = 0;
        chat.Clear(); pops.Clear(); bursts.Clear();
        PlayerPrefs.DeleteKey(PastKey);
        PlayerPrefs.DeleteKey(TotalKey);
        past.Clear();
        totalStreams = totalTipCents = totalSeconds = bestPeak = 0;
        loaded = true;
        Changed?.Invoke();
    }

    static void Load()
    {
        if (loaded) return;
        loaded = true;
        past.Clear();
        foreach (var line in PlayerPrefs.GetString(PastKey, "").Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var p = line.Split('|');
            if (p.Length < 5 || !int.TryParse(p[1], out int peak) || !int.TryParse(p[2], out int tips) || !int.TryParse(p[3], out int secs) || !int.TryParse(p[4], out int q)) continue;
            past.Add(new Past { day = p[0], peak = peak, tipCents = tips, seconds = secs, quality = Mathf.Clamp(q, 0, 3) });
        }
        var t = PlayerPrefs.GetString(TotalKey, "").Split(',');
        if (t.Length == 4 && int.TryParse(t[0], out int a) && int.TryParse(t[1], out int b) && int.TryParse(t[2], out int c) && int.TryParse(t[3], out int d))
        { totalStreams = a; totalTipCents = b; totalSeconds = c; bestPeak = d; }
    }

    static void Save()
    {
        var sb = new StringBuilder();
        foreach (var p in past)
            sb.Append((p.day ?? "").Replace('|', '/').Replace('\n', ' ')).Append('|').Append(p.peak).Append('|').Append(p.tipCents).Append('|').Append(p.seconds).Append('|').Append(p.quality).Append('\n');
        PlayerPrefs.SetString(PastKey, sb.ToString());
        PlayerPrefs.SetString(TotalKey, $"{totalStreams},{totalTipCents},{totalSeconds},{bestPeak}");
        PlayerPrefs.Save();
    }
}
