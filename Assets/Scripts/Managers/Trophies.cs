using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The trophies you've won (saved in PlayerPrefs "G7l6.Trophies"): a cup for every tournament podium (gold for 1st, silver for 2nd)
/// and a big cup for a Season Final podium. Shown on the shelf in your room (see RoomProgress).
/// Follower plaques (100 / 500 / 1,000) and the rank plaque are worked out live, so they aren't stored here.
/// </summary>
public static class Trophies
{
    public enum Kind { Cup, FinalCup }

    public struct Trophy
    {
        public Kind kind;
        public bool first;     // gold (1st) or silver (2nd)
        public string label;   // "Season 1, week 2 tournament"
    }

    const string Key = "G7l6.Trophies";
    static List<Trophy> list;
    public static event Action Changed;

    public static IReadOnlyList<Trophy> All { get { Load(); return list; } }

    public static void Add(bool final, bool first, string label)
    {
        Load();
        list.Add(new Trophy { kind = final ? Kind.FinalCup : Kind.Cup, first = first, label = label });
        Save();
    }

    public static void ResetAll()
    {
        PlayerPrefs.DeleteKey(Key);
        list = null;
        Changed?.Invoke();
    }

    static void Load()
    {
        if (list != null) return;
        list = new List<Trophy>();
        foreach (var line in PlayerPrefs.GetString(Key, "").Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var p = line.Split('|');
            if (p.Length < 3) continue;
            list.Add(new Trophy { kind = p[0] == "F" ? Kind.FinalCup : Kind.Cup, first = p[1] == "1", label = p[2] });
        }
    }

    static void Save()
    {
        var sb = new System.Text.StringBuilder();
        foreach (var t in list) sb.Append(t.kind == Kind.FinalCup ? "F" : "C").Append('|').Append(t.first ? "1" : "2").Append('|').Append((t.label ?? "").Replace('|', '/').Replace('\n', ' ')).Append('\n');
        PlayerPrefs.SetString(Key, sb.ToString());
        PlayerPrefs.Save();
        Changed?.Invoke();
    }
}
