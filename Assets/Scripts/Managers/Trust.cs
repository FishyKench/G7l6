using System;
using UnityEngine;

/// <summary>
/// Mom's Trust (0-100, saved in PlayerPrefs): the second currency next to money. Money buys things; trust is what Mom LETS you do.
///
/// It moves once per day, on the report card: the grade (A+ +12, A +9, B +5, C +1, D -6, F -12), -1 per failed chore (max -5),
/// +2 for finished homework. Restarting a day can't farm it (Campaign.Once).
///
/// What it unlocks / takes away:
///   below <see cref="HomeworkFirstBelow"/> - "homework first": Ranked won't launch on the PC until the homework is done;
///   below <see cref="StreamNeeds"/>        - no streaming on LiveLoop;
///   below <see cref="TournamentNeeds"/>    - Mom won't drive you to Friday's tournament, even if your rank qualifies;
///   at <see cref="LateCurfewAt"/>+         - Mom gets home 30 minutes later (more time to play).
/// </summary>
public static class Trust
{
    const string Key = "G7l6.Trust", LastKey = "G7l6.Trust.Last";
    public const int Start = 50, HomeworkFirstBelow = 30, StreamNeeds = 35, TournamentNeeds = 40, LateCurfewAt = 75;

    static bool loaded;
    static int value, last;

    public static event Action Changed;

    public static int Value { get { Load(); return value; } }
    /// <summary>How much the last report card moved it (for the HUD).</summary>
    public static int LastChange { get { Load(); return last; } }

    public static bool HomeworkFirst => Value < HomeworkFirstBelow;
    public static bool CanStream => Value >= StreamNeeds;
    /// <summary>The Season Final (week 3) needs more: Mom has to really trust you.</summary>
    public const int FinalNeeds = 50;
    public static int TournamentNeedsNow => Campaign.FinalWeek ? FinalNeeds : TournamentNeeds;
    public static bool CanGoToTournament => Value >= TournamentNeedsNow;
    public static bool LateCurfew => Value >= LateCurfewAt;

    public static string Mood
    {
        get
        {
            int v = Value;
            return v >= 85 ? "Proud of you" : v >= LateCurfewAt ? "Relaxed" : v >= 50 ? "Okay with you" : v >= TournamentNeeds ? "Watching you" : v >= HomeworkFirstBelow ? "Disappointed" : "Furious";
        }
    }

    public static Color Colour
    {
        get
        {
            int v = Value;
            return v >= 60 ? (Color)new Color32(46, 158, 68, 255) : v >= TournamentNeeds ? (Color)new Color32(224, 123, 18, 255) : (Color)new Color32(224, 53, 43, 255);
        }
    }

    public static int DeltaFor(string grade, int choresFailed, bool homeworkDone)
    {
        int d;
        switch (string.IsNullOrEmpty(grade) ? "F" : grade)
        {
            case "A+": d = 12; break;
            case "A": d = 9; break;
            case "B": d = 5; break;
            case "C": d = 1; break;
            case "D": d = -6; break;
            default: d = -12; break;
        }
        d -= Mathf.Min(5, Mathf.Max(0, choresFailed));
        if (homeworkDone) d += 2;
        return d;
    }

    /// <summary>The report card: moves trust once per day. Returns the change (0 on a restarted day).</summary>
    public static int EndOfDay(string grade, int choresFailed, bool homeworkDone)
    {
        Load();
        if (!Campaign.Once($"d{Campaign.DayIndex}.trust")) return 0;
        int before = value;
        value = Mathf.Clamp(value + DeltaFor(grade, choresFailed, homeworkDone), 0, 100);
        last = value - before;
        Save();
        return last;
    }

    /// <summary>Debug / testing.</summary>
    public static void Set(int v)
    {
        Load();
        last = Mathf.Clamp(v, 0, 100) - value;
        value = Mathf.Clamp(v, 0, 100);
        Save();
    }

    public static void ResetAll()
    {
        PlayerPrefs.DeleteKey(Key);
        PlayerPrefs.DeleteKey(LastKey);
        loaded = false;
        Load();
        Changed?.Invoke();
    }

    static void Load()
    {
        if (loaded) return;
        loaded = true;
        value = Mathf.Clamp(PlayerPrefs.GetInt(Key, Start), 0, 100);
        last = PlayerPrefs.GetInt(LastKey, 0);
    }

    static void Save()
    {
        PlayerPrefs.SetInt(Key, value);
        PlayerPrefs.SetInt(LastKey, last);
        PlayerPrefs.Save();
        Changed?.Invoke();
    }

    /// <summary>True when today's homework is handed in (what "homework first" waits for).</summary>
    public static bool HomeworkDoneToday()
    {
        var hw = UnityEngine.Object.FindObjectOfType<TypingTask>();
        return !hw || (!hw.IsActive && hw.LastSucceeded);
    }
}
