using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Your money and the games you own. Saved between sessions (PlayerPrefs).
/// How you earn money is still to come - for now you start with a little pocket money.
/// </summary>
public static class Wallet
{
    const string MoneyKey = "G7l6.Wallet.Cents", OwnedKey = "G7l6.Wallet.Owned";
    public const int StartingCents = 3000;

    static bool loaded;
    static int cents;
    static readonly HashSet<string> owned = new HashSet<string>();

    /// <summary>Fires whenever the money or the owned games change.</summary>
    public static event Action Changed;

    public static float Money { get { Load(); return cents / 100f; } }
    public static string MoneyText => Format(Money);
    public static string Format(float dollars) => "$" + dollars.ToString("0.00");

    public static void Add(float dollars)
    {
        Load();
        cents += Mathf.RoundToInt(dollars * 100f);
        Save();
    }

    /// <summary>Pays if you can afford it.</summary>
    public static bool Spend(float dollars)
    {
        Load();
        int c = Mathf.RoundToInt(dollars * 100f);
        if (c > cents) return false;
        cents -= c;
        Save();
        return true;
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
        loaded = false;
        Load();
        Changed?.Invoke();
    }

    static void Load()
    {
        if (loaded) return;
        loaded = true;
        cents = PlayerPrefs.GetInt(MoneyKey, StartingCents);
        owned.Clear();
        foreach (var id in PlayerPrefs.GetString(OwnedKey, "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)) owned.Add(id);
    }

    static void Save()
    {
        PlayerPrefs.SetInt(MoneyKey, cents);
        PlayerPrefs.SetString(OwnedKey, string.Join(",", owned));
        PlayerPrefs.Save();
        Changed?.Invoke();
    }
}
