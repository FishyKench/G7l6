using UnityEngine;

/// <summary>
/// One place to make the whole game easier or harder. These multiply the values set on the objects in the scene (so the scene
/// stays as it is); 1 = as set in the scene. The first three days are gentler still (you're learning the house), easing
/// off day by day; from day 4 it's the normal values below.
/// </summary>
public static class Tuning
{
    /// <summary>Day number since you started playing: 1, 2, 3, ... (Monday of week 1 = day 1).</summary>
    public static int DayNumber => (Campaign.Week - 1) * Campaign.Days + Campaign.DayIndex + 1;

    /// <summary>1 on day 1, 2/3 on day 2, 1/3 on day 3, 0 from day 4: how much of the "new player" ease still applies.</summary>
    public static float NewPlayer => Mathf.Clamp01((4 - DayNumber) / 3f);

    //                                                              normal   first day
    /// <summary>Stress you gain from everything (hazards, timers running out, typos, the cat...). Below 1 = calmer.</summary>
    public static float StressGain => Mathf.Lerp(0.8f, 0.6f, NewPlayer);

    /// <summary>How fast stress drops again when nothing is stressing you. Above 1 = you calm down faster.</summary>
    public static float StressRecovery => Mathf.Lerp(1.25f, 1.6f, NewPlayer);

    /// <summary>Time between new random tasks. Above 1 = they come less often.</summary>
    public static float TaskGap => Mathf.Lerp(1.25f, 1.7f, NewPlayer);

    /// <summary>Time limit on tasks. Above 1 = longer to get them done.</summary>
    public static float TaskTime => Mathf.Lerp(1.15f, 1.4f, NewPlayer);

    /// <summary>How long fire / flood take to spread. Above 1 = they spread slower.</summary>
    public static float HazardSpread => Mathf.Lerp(1.2f, 1.5f, NewPlayer);

    /// <summary>Random tasks at once: one fewer than the scene's number during the first three days.</summary>
    public static int MaxActiveTasks(int scene) => DayNumber <= 3 ? Mathf.Max(1, scene - 1) : scene;

    /// <summary>Game minutes at the start of each day with no random tasks (the day starts at 3 PM: nothing before 3:30).</summary>
    public const float GraceMinutes = 30f;

    /// <summary>After a TV starts, seconds before another TV can (and only one TV at a time).</summary>
    public const float TvBreak = 90f;

    // ---- the VR match: the enemy team (your teammates stay as they are)
    /// <summary>How far enemy shots stray. Above 1 = they miss a little more.</summary>
    public const float EnemySpread = 1.18f;
    /// <summary>Enemy damage per hit. Below 1 = you last a little longer.</summary>
    public const float EnemyDamage = 0.88f;
    /// <summary>How long enemies take to react and settle their aim on you. Above 1 = a beat slower.</summary>
    public const float EnemyReaction = 1.15f;

    /// <summary>Homework: how many maths questions (2-digit adding / taking away, times tables up to 13, no dividing).</summary>
    public const int HomeworkQuestions = 2;
}
