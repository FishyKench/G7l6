using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Damage that stays done: the sofa the cat shredded, the TV the leak fried, the dinner the cat ate. Not tasks (there's nothing
/// to fix today) - Mom just sees them when she gets home. Cleared when a day starts.
/// </summary>
public static class Incidents
{
    public static readonly List<DayManager.Finding> All = new List<DayManager.Finding>();

    /// <summary>Something's happened that Mom will see (once per 'what').</summary>
    public static void Add(string what, string line, int points, Vector3 where)
    {
        foreach (var f in All) if (f.what == what) return;
        All.Add(new DayManager.Finding { what = what, line = line, points = points, where = where });
    }

    public static bool Has(string what) => All.Exists(f => f.what == what);

    public static void Clear() => All.Clear();
}
