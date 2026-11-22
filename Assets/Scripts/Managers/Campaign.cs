using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// The week campaign (static, saved in PlayerPrefs "G7l6.Campaign.*"): which week and day it is (Mon..Fri), your ranked rating and
/// rank, the Friday tournament and the per-day rules (how hard the day is).
///
/// The game is one day per scene load. <see cref="BeginDay"/> (called by DayManager / CampaignRuntime when a day scene wakes up,
/// once per scene load) moves to the next day when the previous day's report card was shown; a restart in the middle of a day
/// (R is only on the report card, but the pause menu's "Restart day" and editor restarts exist) keeps the same day.
/// After Friday's report the next load starts Monday of the next week (see <see cref="CampaignUI"/>'s week summary).
///
/// RANK: rating points. Bronze 0-99, Silver 100-199, Gold 200-299, Diamond 300-399, Champion 400+. You start at 50. A finished ranked
/// match moves your rating by wins*25 - losses*20 (+30 for winning the match), i.e. +105 for 3-0 down to -60 for 0-3. An unfinished
/// match (Mom came home) counts the rounds played, no bonus. The rating never goes below 0. The Tuesday scrim gives +15.
/// TOURNAMENT: on Friday, if your rating reached the gate (Gold in week 1, Diamond from week 2), the ranked match becomes the
/// Tournament: tougher bots, 30 s breaks, doubled stream viewers. Prize: won the match = 1st $150, lost with at least 2 rounds won =
/// 2nd $60, otherwise nothing. Not eligible: the normal ranked match is played and the rating still moves.
///
/// RESET: <see cref="Wallet.ResetAll"/> (Pixel Vault F10) calls <see cref="ResetAll"/>: back to Monday, week 1, rating 50.
/// </summary>
public static class Campaign
{
    public enum Rank { Bronze, Silver, Gold, Diamond, Champion }
    public enum TournamentResult { None, NotEligible, Pending, First, Second, Out, Missed }

    public const int Days = 5, Friday = 4;
    public const int PointsPerRank = 100, MaxRating = 499, StartRating = 50, ScrimBonus = 15;
    public const int Prize1st = 150, Prize2nd = 60;
    public static readonly string[] DayNames = { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday" };
    public static readonly string[] DayShort = { "Mon", "Tue", "Wed", "Thu", "Fri" };
    public static readonly string[] RankNames = { "Bronze", "Silver", "Gold", "Diamond", "Champion" };
    public static readonly Color32[] RankColours =
    {
        new Color32(205, 127, 70, 255), new Color32(196, 206, 220, 255), new Color32(255, 200, 64, 255),
        new Color32(110, 220, 255, 255), new Color32(255, 100, 200, 255),
    };

    /// <summary>Everything that changes how hard a day is. Read by the existing systems (see CampaignRuntime and the hooks listed in the step 6 report).</summary>
    public class DayRules
    {
        public string name, shortName;
        public string[] twist = new string[0];
        /// <summary>x GameManager.maxActiveTasks (rounded, min 1).</summary>
        public float taskCountMult = 1f;
        /// <summary>x the wait between random tasks (smaller = more often).</summary>
        public float taskIntervalMult = 1f;
        /// <summary>The ceiling leak may start as a random task.</summary>
        public bool leaks = true;
        /// <summary>x the oven's "catches fire at" time (bigger = more forgiving).</summary>
        public float ovenBurnMult = 1f;
        /// <summary>x the leak puddle grow time (bigger = slower flood).</summary>
        public float leakGrowMult = 1f;
        /// <summary>How much more often the cat gets up to mischief (2 = twice as often).</summary>
        public float catMischiefMult = 1f;
        public int homeworkPages = 3;
        /// <summary>x the points Mom takes off for each thing she finds.</summary>
        public float momStrict = 1f;
        public float momSpeed = 1f;
        public float momHomeHour = 18f;
        /// <summary>Real minutes the day lasts (0 = leave as set in the scene).</summary>
        public float realMinutes;
        public float matchStartHour = 16f;
        /// <summary>Enemy bot sharpness (1 = the scene's numbers, 1.35 = tournament).</summary>
        public float botSkill = 1f;
        public bool scrim, tournament;
    }

    const string P = "G7l6.Campaign.";

    // ------------------------------------------------------------------ state

    static bool loaded;
    static int week = 1, dayIndex, rating = StartRating, weekStartRating = StartRating, weekStartFollowers;
    static bool dayDone, tournamentEligible, matchRecorded, tournamentPaid;
    static int tournamentResult;
    static string tournamentScore = "";
    static readonly string[] grades = new string[Days], matches = new string[Days];
    static readonly int[] ratings = new int[Days];
    static readonly HashSet<string> sent = new HashSet<string>();
    static int beganFor = int.MinValue;

    public static int Week { get { Load(); return week; } }
    public static int DayIndex { get { Load(); return dayIndex; } }
    public static int Rating { get { Load(); return rating; } }
    public static int WeekStartRating { get { Load(); return weekStartRating; } }
    public static int WeekStartFollowers { get { Load(); return weekStartFollowers; } }
    public static bool IsFriday => DayIndex == Friday;
    public static string DayName => DayNames[DayIndex];
    public static string DayLabel => $"{DayShort[DayIndex]} (Week {Week})";
    public static string TitleLine => $"{DayName.ToUpperInvariant()}  -  Week {Week}";
    /// <summary>Fires when the rating changes (UI refresh).</summary>
    public static event Action Changed;
    /// <summary>True while the tournament match is running (Round / Break); set by CampaignRuntime. LiveStream doubles the viewers.</summary>
    public static bool TournamentLive;

    /// <summary>Today is Friday AND you reached the gate: the ranked match is the Tournament.</summary>
    public static bool TournamentToday { get { Load(); return dayIndex == Friday && tournamentEligible; } }
    public static TournamentResult TournamentOutcome { get { Load(); return (TournamentResult)tournamentResult; } }
    public static string TournamentScore { get { Load(); return tournamentScore; } }
    public static bool DayFinished { get { Load(); return dayDone; } }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        loaded = false;
        beganFor = int.MinValue;
        TournamentLive = false;
    }

    // ------------------------------------------------------------------ rank

    public static Rank RankOf(int points) => (Rank)Mathf.Clamp(points / PointsPerRank, 0, 4);
    public static Rank CurrentRank => RankOf(Rating);
    public static string RankName(Rank r) => RankNames[(int)r];
    public static Color RankColour(Rank r) => RankColours[(int)r];
    public static string RankText => $"{RankName(CurrentRank)}  {Rating} RP";

    /// <summary>The rank you need for the tournament: Gold in week 1, Diamond afterwards.</summary>
    public static Rank GateRank => Week >= 2 ? Rank.Diamond : Rank.Gold;
    public static int GateRating => (int)GateRank * PointsPerRank;
    public static bool ReachedGate => Rating >= GateRating;
    public static int PointsToGate => Mathf.Max(0, GateRating - Rating);

    /// <summary>0..1 inside the current rank (Champion is always full).</summary>
    public static float ProgressInRank
    {
        get
        {
            if (CurrentRank == Rank.Champion) return 1f;
            return (Rating % PointsPerRank) / (float)PointsPerRank;
        }
    }

    public static int PointsToNextRank => CurrentRank == Rank.Champion ? 0 : PointsPerRank - Rating % PointsPerRank;

    public static void AddRating(int delta, string why = null)
    {
        Load();
        int before = rating;
        rating = Mathf.Clamp(rating + delta, 0, MaxRating);
        if (rating == before) return;
        Save();
        Changed?.Invoke();
        var a = RankOf(before);
        var b = RankOf(rating);
        string sign = rating - before >= 0 ? "+" : "";
        string msg = (why != null ? why + ": " : "Ranked: ") + $"{sign}{rating - before} RP";
        if (b > a) GameHUD.Toast($"RANK UP! You are now {RankName(b)}  -  {rating} RP", GameHUD.Good);
        else if (b < a) GameHUD.Toast($"{msg}  -  back to {RankName(b)}", GameHUD.Bad);
        else GameHUD.Toast($"{msg}  ({RankName(b)} {rating} RP)", rating > before ? GameHUD.Good : GameHUD.Warn);
    }

    /// <summary>Debug.</summary>
    public static void SetRating(int value)
    {
        Load();
        rating = Mathf.Clamp(value, 0, MaxRating);
        Save();
        Changed?.Invoke();
    }

    public static int RatingDeltaFor(int wins, int losses, bool won) => wins * 25 - losses * 20 + (won ? 30 : 0);

    // ------------------------------------------------------------------ the day rules

    public static DayRules Today => Rules(DayIndex, Week);

    public static DayRules Rules(int day, int wk)
    {
        day = Mathf.Clamp(day, 0, Days - 1);
        var r = new DayRules { name = DayNames[day], shortName = DayShort[day] };
        bool tournament = day == Friday && tournamentEligible && wk == week;
        switch (day)
        {
            case 0: // Monday: gentle
                r.taskCountMult = 0.67f; r.taskIntervalMult = 1.4f; r.leaks = false; r.ovenBurnMult = 1.4f; r.leakGrowMult = 1.5f;
                r.catMischiefMult = 0.7f; r.homeworkPages = 2; r.momStrict = 0.8f; r.botSkill = 0.9f;
                r.twist = new[] { "An easy start: fewer things go wrong today.", "Win a ranked match to begin the climb." };
                break;
            case 1: // Tuesday: scrim
                r.scrim = true; r.homeworkPages = 3;
                r.twist = new[] { "Jay is hosting a scrim before ranked.", "Play the warm-up for bonus rank points." };
                break;
            case 2: // Wednesday: chaos + homework
                r.taskCountMult = 1.34f; r.taskIntervalMult = 0.8f; r.ovenBurnMult = 0.9f; r.leakGrowMult = 0.85f;
                r.catMischiefMult = 1.6f; r.homeworkPages = 5; r.botSkill = 1.05f;
                r.twist = new[] { "Big homework, more hazards, a wild cat.", "Keep your cool and push your rank." };
                break;
            case 3: // Thursday: guests
                r.taskIntervalMult = 0.9f; r.homeworkPages = 4; r.momStrict = 1.5f; r.catMischiefMult = 1.2f; r.botSkill = 1.1f;
                r.twist = new[] { "Guests are coming! Mom checks everything.", "Last day to reach " + RankName(GateRankFor(wk)) + "." };
                break;
            default: // Friday: tournament / chaos
                r.taskCountMult = 1.34f; r.taskIntervalMult = 0.7f; r.ovenBurnMult = 0.8f; r.leakGrowMult = 0.75f;
                r.catMischiefMult = 1.8f; r.homeworkPages = 4; r.momStrict = 1.25f; r.momSpeed = 1.3f;
                r.momHomeHour = 17.5f; r.realMinutes = 11f; r.botSkill = 1.1f;
                if (tournament)
                {
                    r.tournament = true; r.matchStartHour = 15.75f; r.botSkill = 1.35f;
                    r.twist = new[] { "TOURNAMENT DAY - Mom is home early!", $"1st place ${Prize1st}   -   2nd place ${Prize2nd}" };
                }
                else
                    r.twist = new[] { "No tournament: you missed " + RankName(GateRankFor(wk)) + ".", "Mom is home early. Maximum chaos!" };
                break;
        }

        // Week 2+: everything a notch harder (money, gear and followers all stay).
        float w = Mathf.Min(4, wk - 1);
        if (w > 0)
        {
            r.taskCountMult *= 1f + 0.2f * w;
            r.taskIntervalMult /= 1f + 0.15f * w;
            r.catMischiefMult *= 1f + 0.2f * w;
            r.homeworkPages += Mathf.Min(3, (int)w);
            r.momStrict *= 1f + 0.1f * w;
            r.botSkill *= 1f + 0.08f * w;
            r.ovenBurnMult /= 1f + 0.05f * w;
        }
        return r;
    }

    /// <summary>GameManager's random task pool: Monday has no ceiling leaks.</summary>
    public static bool AllowRandomTask(TaskBase t) => !(t is CeilingLeakTask) || Today.leaks;

    public static Rank GateRankFor(int wk) => wk >= 2 ? Rank.Diamond : Rank.Gold;

    // ------------------------------------------------------------------ begin a day

    /// <summary>Called by DayManager.Awake and CampaignRuntime.Awake; works once per scene load.</summary>
    public static void BeginDay(Scene scene)
    {
        Load();
        int handle = scene.handle;
        if (beganFor == handle) return;
        beganFor = handle;

        if (dayDone)
        {
            if (dayIndex >= Days - 1) StartWeek(week + 1);
            else dayIndex++;
            dayDone = false;
        }
        matchRecorded = false;
        tournamentPaid = false;
        TournamentLive = false;
        if (dayIndex == Friday)
        {
            // Decide once, on the Friday load: eligible or not. The rating can't change on Friday (the tournament is separate).
            if (tournamentResult == (int)TournamentResult.None || tournamentResult == (int)TournamentResult.Pending || tournamentResult == (int)TournamentResult.NotEligible)
            {
                tournamentEligible = Rating >= GateRating;
                tournamentResult = (int)(tournamentEligible ? TournamentResult.Pending : TournamentResult.NotEligible);
            }
            else tournamentEligible = false; // already played this Friday (restart): no second tournament
        }
        else tournamentEligible = false;
        Wallet.DayLabel = DayLabel;
        Save();
    }

    static void StartWeek(int wk)
    {
        week = wk;
        dayIndex = 0;
        weekStartRating = rating;
        weekStartFollowers = ClipHub.Followers;
        tournamentResult = (int)TournamentResult.None;
        tournamentScore = "";
        tournamentEligible = false;
        for (int i = 0; i < Days; i++) { grades[i] = ""; matches[i] = ""; ratings[i] = 0; }
        sent.RemoveWhere(k => k.StartsWith("w" + (wk - 1) + "."));
    }

    // ------------------------------------------------------------------ the ranked match

    /// <summary>The match is over (someone reached the round target). Moves the rating, or settles the tournament.</summary>
    /// <returns>True when this result counts (false after a restart of a day whose match was already counted: no second prize / rating).</returns>
    public static bool OnMatchOver(int wins, int losses, bool won)
    {
        Load();
        if (matchRecorded) return false;
        matchRecorded = true;
        if (TournamentToday) { PayTournament(wins, losses, won); return true; }
        if (!Once($"d{dayIndex}.ranked")) return false; // one ranked result per day (restarting the day can't farm rating or prize money)
        AddRating(RatingDeltaFor(wins, losses, won), won ? "Ranked win" : "Ranked loss");
        return true;
    }

    /// <summary>The day ended (Mom came home / meltdown): settle an unfinished match and store the day for the week summary.</summary>
    public static void OnDayEnded(string grade, string matchText, int wins, int losses, bool started)
    {
        Load();
        if (!matchRecorded && started)
        {
            matchRecorded = true;
            if (TournamentToday) PayTournament(wins, losses, false, finished: false);
            else if (wins + losses > 0 && Once($"d{dayIndex}.ranked")) AddRating(RatingDeltaFor(wins, losses, false), "Unfinished match");
        }
        else if (!matchRecorded && TournamentToday)
        {
            matchRecorded = true;
            tournamentResult = (int)TournamentResult.Missed;
            tournamentScore = "";
            Mail.Send("Ranked League", "Tournament: you did not show up",
                "The Ranked Arena tournament has finished and you never played a round. Better luck next season.\n\n- The Ranked League", Mail.Kind.Info);
        }
        grades[dayIndex] = grade ?? "";
        matches[dayIndex] = matchText ?? "";
        ratings[dayIndex] = rating;
        dayDone = true;
        Save();
    }

    static void PayTournament(int wins, int losses, bool won, bool finished = true)
    {
        if (tournamentPaid) return;
        tournamentPaid = true;
        tournamentScore = $"{wins}-{losses}";
        TournamentResult res;
        int prize = 0;
        if (finished && won) { res = TournamentResult.First; prize = Prize1st; }
        else if (finished && wins >= 2) { res = TournamentResult.Second; prize = Prize2nd; }
        else res = TournamentResult.Out;
        tournamentResult = (int)res;
        Save();

        string place = res == TournamentResult.First ? "1st place" : res == TournamentResult.Second ? "2nd place" : "no podium";
        if (prize > 0)
        {
            Wallet.Earn(prize, $"Ranked League - tournament {place} ({tournamentScore})", Wallet.Category.Prize);
            GameHUD.Toast($"TOURNAMENT {place.ToUpperInvariant()}!  +${prize}", GameHUD.Good);
        }
        else GameHUD.Toast(finished ? $"Tournament over: {tournamentScore}. No prize this time." : "The tournament ended before you could finish.", GameHUD.Bad);

        string subject = res == TournamentResult.First ? "You won the tournament!" : res == TournamentResult.Second ? "2nd place - prize money inside" : "Tournament results";
        string body = res == TournamentResult.First
            ? $"Congratulations! You won the Ranked Arena tournament ({tournamentScore}). Your prize of ${Prize1st} has been paid into your account."
            : res == TournamentResult.Second
                ? $"Great run! You finished 2nd in the Ranked Arena tournament ({tournamentScore}). Your prize of ${Prize2nd} has been paid into your account."
                : finished ? $"You were knocked out of the Ranked Arena tournament ({tournamentScore}). Only the top two places win prize money. Keep practising!"
                           : "Your match was cut short, so the Ranked Arena tournament could not count it. No prize this time.";
        Mail.Send("Ranked League", subject, body + "\n\n- The Ranked League", Mail.Kind.Info);
    }

    // ------------------------------------------------------------------ mails sent once per week

    /// <summary>True the first time per key (resets with the campaign).</summary>
    public static bool Once(string key)
    {
        Load();
        string k = $"w{week}.{key}";
        if (!sent.Add(k)) return false;
        Save();
        return true;
    }

    // ------------------------------------------------------------------ the week summary data

    public static string GradeOf(int day) { Load(); return day >= 0 && day < Days ? grades[day] : ""; }
    public static string MatchOf(int day) { Load(); return day >= 0 && day < Days ? matches[day] : ""; }
    public static int RatingAfter(int day) { Load(); return day >= 0 && day < Days ? ratings[day] : 0; }

    /// <summary>Dollars earned this week per category (index = Wallet.Category) and the dollars spent, from the Bank history.</summary>
    public static void WeekMoney(out float[] earned, out float spent)
    {
        earned = new float[6];
        spent = 0f;
        string tag = $"(Week {Week})";
        foreach (var t in Wallet.History)
        {
            if (t.day == null || !t.day.Contains(tag)) continue;
            if (t.cents >= 0) earned[(int)t.category] += t.cents / 100f;
            else spent += -t.cents / 100f;
        }
    }

    // ------------------------------------------------------------------ save / load / reset

    static void Load()
    {
        if (loaded) return;
        loaded = true;
        week = Mathf.Max(1, PlayerPrefs.GetInt(P + "Week", 1));
        dayIndex = Mathf.Clamp(PlayerPrefs.GetInt(P + "Day", 0), 0, Days - 1);
        rating = Mathf.Clamp(PlayerPrefs.GetInt(P + "Rating", StartRating), 0, MaxRating);
        weekStartRating = PlayerPrefs.GetInt(P + "WeekStartRating", rating);
        weekStartFollowers = PlayerPrefs.GetInt(P + "WeekStartFollowers", 0);
        dayDone = PlayerPrefs.GetInt(P + "Done", 0) == 1;
        tournamentEligible = PlayerPrefs.GetInt(P + "Eligible", 0) == 1;
        tournamentResult = PlayerPrefs.GetInt(P + "TResult", 0);
        tournamentScore = PlayerPrefs.GetString(P + "TScore", "");
        Split(PlayerPrefs.GetString(P + "Grades", ""), grades);
        Split(PlayerPrefs.GetString(P + "Matches", ""), matches);
        var rs = PlayerPrefs.GetString(P + "Ratings", "").Split(',');
        for (int i = 0; i < Days; i++) ratings[i] = i < rs.Length && int.TryParse(rs[i], out int v) ? v : 0;
        sent.Clear();
        foreach (var k in PlayerPrefs.GetString(P + "Sent", "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)) sent.Add(k);
    }

    static void Split(string s, string[] into)
    {
        var parts = s.Split(';');
        for (int i = 0; i < Days; i++) into[i] = i < parts.Length ? parts[i] : "";
    }

    static void Save()
    {
        PlayerPrefs.SetInt(P + "Week", week);
        PlayerPrefs.SetInt(P + "Day", dayIndex);
        PlayerPrefs.SetInt(P + "Rating", rating);
        PlayerPrefs.SetInt(P + "WeekStartRating", weekStartRating);
        PlayerPrefs.SetInt(P + "WeekStartFollowers", weekStartFollowers);
        PlayerPrefs.SetInt(P + "Done", dayDone ? 1 : 0);
        PlayerPrefs.SetInt(P + "Eligible", tournamentEligible ? 1 : 0);
        PlayerPrefs.SetInt(P + "TResult", tournamentResult);
        PlayerPrefs.SetString(P + "TScore", tournamentScore ?? "");
        PlayerPrefs.SetString(P + "Grades", Clean(string.Join(";", grades)));
        PlayerPrefs.SetString(P + "Matches", Clean(string.Join(";", matches)));
        var sb = new StringBuilder();
        for (int i = 0; i < Days; i++) sb.Append(i > 0 ? "," : "").Append(ratings[i]);
        PlayerPrefs.SetString(P + "Ratings", sb.ToString());
        PlayerPrefs.SetString(P + "Sent", string.Join(",", sent));
        PlayerPrefs.Save();
    }

    static string Clean(string s) => s.Replace('\n', ' ');

    /// <summary>The week summary's "Start Week N" button: the next scene load starts Monday of the next week.</summary>
    public static void QueueNextWeek()
    {
        Load();
        dayIndex = Days - 1;
        dayDone = true;
        beganFor = int.MinValue;
        Save();
    }

    /// <summary>Debug: jump to a day of the current week (takes effect on the next scene load, see CampaignRuntime).</summary>
    public static void JumpTo(int day)
    {
        Load();
        dayIndex = Mathf.Clamp(day, 0, Days - 1);
        dayDone = false;
        tournamentResult = (int)TournamentResult.None;
        tournamentScore = "";
        tournamentEligible = false;
        beganFor = int.MinValue; // BeginDay runs again on the reload and decides the Friday tournament
        Save();
    }

    /// <summary>Back to Monday of week 1, rating 50 (Wallet.ResetAll calls this).</summary>
    public static void ResetAll()
    {
        foreach (var k in new[] { "Week", "Day", "Rating", "WeekStartRating", "WeekStartFollowers", "Done", "Eligible", "TResult", "TScore", "Grades", "Matches", "Ratings", "Sent" })
            PlayerPrefs.DeleteKey(P + k);
        loaded = false;
        beganFor = int.MinValue;
        TournamentLive = false;
        matchRecorded = false;
        tournamentPaid = false;
        Load();
        Wallet.DayLabel = DayLabel;
        Changed?.Invoke();
    }

    /// <summary>"5:30 PM" from an hour like 17.5.</summary>
    public static string HourText(float hour)
    {
        int h = Mathf.FloorToInt(hour), m = Mathf.RoundToInt((hour - h) * 60f);
        if (m == 60) { h++; m = 0; }
        return $"{(h > 12 ? h - 12 : h == 0 ? 12 : h)}:{m:00} {(h >= 12 ? "PM" : "AM")}";
    }
}
