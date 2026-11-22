using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Your rivals in Ranked Arena: three named players who keep turning up on the enemy team, play a bit better than the rest, talk in
/// chat, and remember your history (kills both ways, saved in PlayerPrefs "G7l6.Rival.*").
///   Sweaty_Kev  - LMG, loud trash talker.        MrNoScope - Sniper, quiet and deadly.        TryHardTina - Assault, the Friday favourite.
/// A ranked match brings one of them (two from Gold up, taking turns day by day); the tournament brings all three.
/// The one who has killed you most (3+ more than you got them) is your NEMESIS.
/// After each ranked match one of them sends you a mail about it.
/// </summary>
public static class Rivals
{
    public class Rival
    {
        public string name, style;
        public VRRole role;
        public string[] gotYou, gotThem, mailWin, mailLoss;
    }

    public static readonly Rival[] All =
    {
        new Rival
        {
            name = "Sweaty_Kev", style = "trash talker", role = VRRole.LMG,
            gotYou = new[] { "sit DOWN", "ez", "uninstall bro", "is that your main??", "free RP" },
            gotThem = new[] { "LAG", "my cat stepped on my keyboard", "ok that was lucky", "reported" },
            mailWin = new[] { "ok you won. barely. my headset was dying the whole match btw. rematch tomorrow." },
            mailLoss = new[] { "GG EZ. maybe stick to homework lol. see you tomorrow, I'll go easy on you (I won't)." },
        },
        new Rival
        {
            name = "MrNoScope", style = "quiet sniper", role = VRRole.Sniper,
            gotYou = new[] { "head.", "patience.", "saw you peek", "too slow" },
            gotThem = new[] { "nice flank", "noted.", "good shot" },
            mailWin = new[] { "Good game. You stopped peeking the same corner. That's growth." },
            mailLoss = new[] { "GG. You peek the same corner every time. I'll be waiting there tomorrow." },
        },
        new Rival
        {
            name = "TryHardTina", style = "the Friday favourite", role = VRRole.Assault,
            gotYou = new[] { "not ready for Friday", "practice more", "cute", "that's 1-0 me" },
            gotThem = new[] { "...okay", "you're decent", "won't happen again" },
            mailWin = new[] { "Fine, you're good. Make it to the tournament on Friday and we'll see who's better." },
            mailLoss = new[] { "Don't bother showing up on Friday if that's your best. No offence." },
        },
    };

    const string P = "G7l6.Rival.";
    static readonly Dictionary<string, int> matchKills = new Dictionary<string, int>(), matchDeaths = new Dictionary<string, int>();

    public static Rival Find(string name)
    {
        foreach (var r in All) if (r.name == name) return r;
        return null;
    }

    public static bool IsRival(string name) => Find(name) != null;
    public static int KillsOn(string name) => PlayerPrefs.GetInt(P + name + ".K", 0);
    public static int DeathsTo(string name) => PlayerPrefs.GetInt(P + name + ".D", 0);

    /// <summary>The rival who has the most kills on you (3+ more than you have on them), or null.</summary>
    public static Rival Nemesis
    {
        get
        {
            Rival best = null;
            int bestLead = 2;
            foreach (var r in All)
            {
                int lead = DeathsTo(r.name) - KillsOn(r.name);
                if (lead > bestLead) { bestLead = lead; best = r; }
            }
            return best;
        }
    }

    /// <summary>Which rivals play against you today (tournament: all three).</summary>
    public static List<Rival> ForMatch(bool tournament)
    {
        var list = new List<Rival>();
        if (tournament) { list.AddRange(All); return list; }
        int count = Campaign.CurrentRank >= Campaign.Rank.Gold ? 2 : 1;
        int start = (Campaign.DayIndex + Campaign.Week * 2) % All.Length;
        for (int i = 0; i < count; i++) list.Add(All[(start + i) % All.Length]);
        return list;
    }

    /// <summary>"  RIVAL" / "  NEMESIS" after the name tag.</summary>
    public static string TitleFor(Rival r) => Nemesis == r ? "  <color=#FF4A5A>NEMESIS</color>" : "  <color=#FFC83C>RIVAL</color>";

    public static void BeginMatch()
    {
        matchKills.Clear();
        matchDeaths.Clear();
    }

    /// <summary>A kill in a ranked round: counts it if a rival was involved. Returns the chat line the rival says (or null).</summary>
    public static string OnKill(VRFighter killer, VRFighter victim, out Rival who, out bool youGotThem)
    {
        who = null;
        youGotThem = false;
        if (killer is VRPlayer && victim)
        {
            who = Find(victim.fighterName);
            if (who == null) return null;
            youGotThem = true;
            Add(P + who.name + ".K");
            matchKills[who.name] = (matchKills.TryGetValue(who.name, out int k) ? k : 0) + 1;
            return Pick(who.gotThem);
        }
        if (victim is VRPlayer && killer)
        {
            who = Find(killer.fighterName);
            if (who == null) return null;
            Add(P + who.name + ".D");
            matchDeaths[who.name] = (matchDeaths.TryGetValue(who.name, out int d) ? d : 0) + 1;
            return Pick(who.gotYou);
        }
        return null;
    }

    /// <summary>After a ranked match (once a day): the rival you clashed with most sends a mail.</summary>
    public static void EndMatch(bool won)
    {
        Rival most = null;
        int mostScore = -1;
        foreach (var r in All)
        {
            int s = (matchKills.TryGetValue(r.name, out int k) ? k : 0) + (matchDeaths.TryGetValue(r.name, out int d) ? d : 0);
            if (s > mostScore) { mostScore = s; most = r; }
        }
        if (most == null || mostScore <= 0) return;
        int you = matchKills.TryGetValue(most.name, out int a) ? a : 0, them = matchDeaths.TryGetValue(most.name, out int b) ? b : 0;
        string body = Pick(won ? most.mailWin : most.mailLoss) +
                      $"\n\nToday: you got me {you} time{(you == 1 ? "" : "s")}, I got you {them}.\nAll time: you {KillsOn(most.name)} - me {DeathsTo(most.name)}.\n\n- {most.name}";
        Mail.Send(most.name, won ? "about today's match..." : "GG (not really)", body, Mail.Kind.Info);
    }

    public static void ResetAll()
    {
        foreach (var r in All) { PlayerPrefs.DeleteKey(P + r.name + ".K"); PlayerPrefs.DeleteKey(P + r.name + ".D"); }
    }

    static void Add(string key)
    {
        PlayerPrefs.SetInt(key, PlayerPrefs.GetInt(key, 0) + 1);
        PlayerPrefs.Save();
    }

    static string Pick(string[] lines) => lines == null || lines.Length == 0 ? null : lines[Random.Range(0, lines.Length)];
}
