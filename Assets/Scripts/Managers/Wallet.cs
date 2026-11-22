using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Your money and the games you own. Saved between sessions (PlayerPrefs).
/// Money comes in as allowance (Mom's report card), ranked prizes, clip views, stream tips and jobs, and goes out
/// on purchases. Every movement is logged in a transaction history (shown in the Browser's Bank page).
/// </summary>
public static class Wallet
{
    const string MoneyKey = "G7l6.Wallet.Cents", OwnedKey = "G7l6.Wallet.Owned", HistoryKey = "G7l6.Wallet.History", BanKey = "G7l6.Wallet.PcBan";
    public const int MaxHistory = 100;

    public enum Category { Allowance, Prize, Views, Tips, Purchase, Job }

    /// <summary>One line in the bank statement. Cents is signed (+ money in, - money out).</summary>
    public class Transaction
    {
        public string day, description;
        public int cents;
        public Category category;
        public float Dollars => cents / 100f;
    }

    /// <summary>Shown on new transactions (e.g. "Day 1"); the campaign can change it as the week goes on.</summary>
    public static string DayLabel = "Day 1";
    public const int StartingCents = 3000;

    static bool loaded;
    static int cents;
    static readonly HashSet<string> owned = new HashSet<string>();
    static readonly List<Transaction> history = new List<Transaction>(); // oldest first

    /// <summary>Fires whenever the money or the owned games change.</summary>
    public static event Action Changed;

    public static float Money { get { Load(); return cents / 100f; } }
    public static string MoneyText => Format(Money);
    public static string Format(float dollars) => "$" + dollars.ToString("0.00");

    /// <summary>Newest first.</summary>
    public static List<Transaction> History
    {
        get
        {
            Load();
            var list = new List<Transaction>(history);
            list.Reverse();
            return list;
        }
    }

    /// <summary>Set when Mom's report card was an F: no PC / streaming the next day. Cleared by whoever enforces it.</summary>
    public static bool PcBannedTomorrow
    {
        get { return PlayerPrefs.GetInt(BanKey, 0) == 1; }
        set { PlayerPrefs.SetInt(BanKey, value ? 1 : 0); PlayerPrefs.Save(); }
    }

    /// <summary>Money comes in and is written to the history.</summary>
    public static void Earn(float dollars, string description, Category category)
    {
        Load();
        int c = Mathf.RoundToInt(dollars * 100f);
        if (c <= 0) return;
        cents += c;
        Log(c, description, category);
        Save();
    }

    /// <summary>Pays if you can afford it (and logs it).</summary>
    public static bool Pay(float dollars, string description, Category category = Category.Purchase)
    {
        Load();
        int c = Mathf.RoundToInt(dollars * 100f);
        if (c > cents) return false;
        cents -= c;
        if (c > 0) Log(-c, description, category);
        Save();
        return true;
    }

    /// <summary>Plain deposit (debug keys etc.), logged as a job payment.</summary>
    public static void Add(float dollars) => Earn(dollars, "Deposit", Category.Job);

    /// <summary>Pays if you can afford it; logged as a purchase.</summary>
    public static bool Spend(float dollars) => Pay(dollars, "Purchase", Category.Purchase);

    static void Log(int signedCents, string description, Category category)
    {
        string time = "";
        var day = DayManager.Instance;
        if (day)
        {
            int h = Mathf.FloorToInt(day.Hour), m = Mathf.FloorToInt((day.Hour - h) * 60f);
            time = $" - {(h > 12 ? h - 12 : h)}:{m:00} {(h >= 12 ? "PM" : "AM")}";
        }
        history.Add(new Transaction { day = DayLabel + time, description = description ?? "", cents = signedCents, category = category });
        while (history.Count > MaxHistory) history.RemoveAt(0);
    }

    public static bool Owns(string id) { Load(); return owned.Contains(id); }

    public static IEnumerable<string> Owned { get { Load(); return owned; } }

    public static void Give(string id)
    {
        Load();
        if (owned.Add(id)) Save();
    }

    /// <summary>Back to a fresh start (no games bought, starting money).</summary>
    public static void ResetAll()
    {
        PlayerPrefs.DeleteKey(MoneyKey);
        PlayerPrefs.DeleteKey(OwnedKey);
        PlayerPrefs.DeleteKey(HistoryKey);
        PlayerPrefs.DeleteKey(BanKey);
        loaded = false;
        Load();
        Shop.ResetAll();
        Mail.ResetAll();
        ClipHub.ResetAll();
        LiveStream.ResetAll();
        Campaign.ResetAll();
        Changed?.Invoke();
    }

    static void Load()
    {
        if (loaded) return;
        loaded = true;
        cents = PlayerPrefs.GetInt(MoneyKey, StartingCents);
        owned.Clear();
        history.Clear();
        foreach (var line in PlayerPrefs.GetString(HistoryKey, "").Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var p = line.Split('|');
            if (p.Length < 4 || !int.TryParse(p[1], out int c) || !int.TryParse(p[2], out int cat)) continue;
            history.Add(new Transaction { day = p[0], cents = c, category = (Category)Mathf.Clamp(cat, 0, 5), description = p[3] });
        }
        foreach (var id in PlayerPrefs.GetString(OwnedKey, "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)) owned.Add(id);
    }

    static string Clean(string s) => (s ?? "").Replace('|', '/').Replace('\n', ' ').Replace('\r', ' ');

    static void Save()
    {
        PlayerPrefs.SetInt(MoneyKey, cents);
        PlayerPrefs.SetString(OwnedKey, string.Join(",", owned));
        var sb = new System.Text.StringBuilder();
        foreach (var t in history)
            sb.Append(Clean(t.day)).Append('|').Append(t.cents).Append('|').Append((int)t.category).Append('|').Append(Clean(t.description)).Append('\n');
        PlayerPrefs.SetString(HistoryKey, sb.ToString());
        PlayerPrefs.Save();
        Changed?.Invoke();
    }
}
