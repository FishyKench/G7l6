using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Your ranked match - the day's main objective - played in VR. It lives on the VR headset on your gaming desk.
///
/// Press E on the headset to put it on: you're in the VR arena (the VRArena scene, loaded next to the house).
/// Both worlds run at the same time - 30 seconds in VR are 30 seconds in the house. Esc takes the headset off;
/// P pauses everything. Your body stays at the desk.
///
/// At 4:00 PM a best-of-5 match starts (50 s rounds, 15 s breaks): first-person 3v3, you + 2 bots vs 3 bots.
/// Rounds start whether you're wearing the headset or not - without you, your avatar stands there AFK and your
/// team fights 2v3 (they can win, but rarely). Before 4:00 the arena has a warm-up with targets.
/// Winning rounds calms you down, losing them stresses you out. At 6:00 Mom pulls the headset off.
/// </summary>
public class MatchTask : TaskBase, IMainTask, IPromptProvider
{
    public const string ArenaScene = "VRArena";
    static MatchTask instance;
    /// <summary>True while you're wearing the headset.</summary>
    public static bool InVR => instance && instance.inVR;

    [Header("Match")]
    public float matchStartHour = 16f;
    public int roundsToWin = 3;
    public float roundSeconds = 50f;
    public float breakSeconds = 15f;
    [Tooltip("If there's no day clock: seconds after the start before the match begins.")]
    public float fallbackStartDelay = 120f;
    public string friendName = "JAY";

    [Header("Enemy bots")]
    [Tooltip("Degrees their shots can miss by in round 1 (lower = better aim).")]
    public float enemySpread = 7f;
    public float enemySpreadPerRound = 0.8f;
    public float enemyFireRate = 3f;
    public float enemyDamage = 7f;
    public float enemyReaction = 0.5f;

    [Header("Your bot teammates")]
    public float allySpread = 8.5f;
    public float allyFireRate = 2.6f;
    public float allyDamage = 6f;

    public enum Phase { Waiting, Round, Break, Over }
    public Phase State { get; private set; }
    public int Wins { get; private set; }
    public int Losses { get; private set; }
    public int RoundNumber => Wins + Losses + (State == Phase.Round ? 1 : 0);
    /// <summary>Seconds left in the break between rounds.</summary>
    public float Timer { get; private set; }
    public int RankPoints => Wins * 25 - Losses * 20 + (State == Phase.Over && Wins >= roundsToWin ? 30 : 0);

    bool inVR, warned, subscribed, saidAfk;
    float startClock, afkFor;
    Camera houseCamera;
    AudioListener houseEars;
    Coroutine transition;

    void Reset() => Defaults();

    void Awake()
    {
        instance = this;
        Defaults();
        startClock = Time.time;
    }

    void Defaults()
    {
        taskName = "Ranked match";
        isMainTask = true;
        canStartRandomly = false;
        timeLimit = 0f;
        stressOnFail = 10f;
        stressReliefOnComplete = 15f;
    }

    void Start()
    {
        houseCamera = Camera.main;
        houseEars = houseCamera ? houseCamera.GetComponent<AudioListener>() : null;
        if (!SceneManager.GetSceneByName(ArenaScene).isLoaded)
        {
            if (Application.CanStreamedLevelBeLoaded(ArenaScene)) SceneManager.LoadScene(ArenaScene, LoadSceneMode.Additive);
            else Debug.LogWarning("[VR] The VRArena scene isn't in Build Settings - run G7l6 > Set Up VR Match.");
        }
    }

    VRArena Arena
    {
        get
        {
            var a = VRArena.Instance;
            if (a && !subscribed) { a.RoundEnded += OnRoundEnded; subscribed = true; }
            return a && a.Ready ? a : null;
        }
    }

    void OnDestroy()
    {
        if (VRArena.Instance && subscribed) VRArena.Instance.RoundEnded -= OnRoundEnded;
        if (instance == this) instance = null;
    }

    // ------------------------------------------------------------------ HUD text

    public override float Progress => Mathf.Clamp01((float)Wins / Mathf.Max(1, roundsToWin));

    public override string Status
    {
        get
        {
            switch (State)
            {
                case Phase.Waiting:
                    float minutes = MinutesToStart();
                    return minutes > 15f ? "Starts at 4:00 PM - launch it on your PC" : $"Starts in {Mathf.CeilToInt(minutes)} min - launch it on your PC, then the headset!";
                case Phase.Round:
                    return inVR ? $"Round {RoundNumber} - {Wins}:{Losses}" : $"Round {RoundNumber} is LIVE - you're AFK!";
                case Phase.Break:
                    return $"{Wins}:{Losses} - next round in {Seconds(Timer)}";
                default:
                    return Wins >= roundsToWin ? "Won!" : "Lost...";
            }
        }
    }

    public string Prompt => State switch
    {
        Phase.Waiting => "Put on the VR headset (warm up)",
        Phase.Round => $"Put on the headset - round {RoundNumber} is LIVE!",
        Phase.Break => $"Put on the headset - next round in {Seconds(Timer)}",
        _ => null,
    };

    float MinutesToStart()
    {
        var day = DayManager.Instance;
        if (day) return (matchStartHour - day.Hour) * 60f;
        return (fallbackStartDelay - (Time.time - startClock)) / 2f;
    }

    // ------------------------------------------------------------------ the headset

    public bool ShouldBlockInput() => true; // E doesn't take the headset off - Esc does

    public void StartMainTask()
    {
        if (State == Phase.Over || !Arena)
        {
            if (!Arena && State != Phase.Over) GameHUD.Toast("The headset is still booting...", GameHUD.Info);
            if (PlayerInteraction.Instance) PlayerInteraction.Instance.ReleaseMainTask(this);
            return;
        }
        inVR = true;
        afkFor = 0f;
        if (transition != null) StopCoroutine(transition);
        transition = StartCoroutine(Switch(true));
    }

    public void StopMainTask()
    {
        if (!inVR) return;
        inVR = false;
        if (transition != null) StopCoroutine(transition);
        transition = StartCoroutine(Switch(false));
    }

    /// <summary>Someone (Mom!) takes the headset off you - game or not.</summary>
    public static void TakeOffHeadset(string why) => VRHeadset.TakeOff(why);

    IEnumerator Switch(bool on)
    {
        var arena = Arena;
        if (!arena) yield break;
        var hud = arena.Hud;
        var player = arena.Player;
        hud.fadeText = on ? "CONNECTING..." : "DISCONNECTING...";
        for (float t = hud.fade; t < 1f; t += Time.unscaledDeltaTime / 0.25f) { hud.fade = t; yield return null; }
        hud.fade = 1f;

        player.controlled = on;
        if (player.Cam)
        {
            player.Cam.enabled = on;
            player.Cam.tag = on ? "MainCamera" : "Untagged";
            var ears = player.Cam.GetComponent<AudioListener>();
            if (ears) ears.enabled = on;
        }
        if (houseCamera) houseCamera.enabled = !on;
        if (houseEars) houseEars.enabled = !on;
        var arenaScene = SceneManager.GetSceneByName(ArenaScene);
        if (on && arenaScene.isLoaded) SceneManager.SetActiveScene(arenaScene); // its lighting and fog
        else if (!on) SceneManager.SetActiveScene(gameObject.scene);

        if (on && State == Phase.Waiting) { arena.StartPractice(); arena.Hud.Announce("WARM-UP", 1.4f); }
        if (!on && arena.State == VRArena.Mode.Practice) arena.Stop();

        yield return new WaitForSecondsRealtime(on ? 0.35f : 0.15f);
        for (float t = 1f; t > 0f; t -= Time.unscaledDeltaTime / 0.3f) { hud.fade = t; yield return null; }
        hud.fade = 0f;
        transition = null;
    }

    // ------------------------------------------------------------------ the schedule

    protected override void TaskUpdate()
    {
        var arena = Arena; // (also hooks up the round-end event once the arena has loaded)
        float dt = Time.deltaTime;
        switch (State)
        {
            case Phase.Waiting:
                float minutes = MinutesToStart();
                if (!warned && minutes <= 12f)
                {
                    warned = true;
                    Text(friendName, "ranked in 10 min!! dont be late again");
                }
                if (minutes <= 0f && arena) BeginMatch(arena);
                break;

            case Phase.Round:
                if (!inVR)
                {
                    afkFor += dt;
                    if (!saidAfk && afkFor > 5f && arena && arena.Hud)
                    {
                        saidAfk = true;
                        arena.Hud.Chat("Nova", Random.value < 0.5f ? "where r u??" : "bro are you AFK");
                    }
                }
                break;

            case Phase.Break:
                Timer -= dt;
                if (Timer <= 0f && arena) StartRound(arena);
                break;
        }
    }

    void BeginMatch(VRArena arena)
    {
        Text(friendName, inVR ? "lets gooo, ranked time" : "MATCH STARTED. where are you??");
        if (!inVR) GameHUD.Toast("Your ranked match started - get your headset on!", GameHUD.Warn);
        StartRound(arena);
    }

    void StartRound(VRArena arena)
    {
        State = Phase.Round;
        saidAfk = false;
        afkFor = 0f;
        arena.StartRound(new VRArena.RoundSettings
        {
            seconds = roundSeconds,
            enemySpread = Mathf.Max(2.5f, enemySpread - enemySpreadPerRound * (RoundNumber - 1)),
            enemyFireRate = enemyFireRate,
            enemyDamage = enemyDamage,
            enemyReaction = enemyReaction,
            allySpread = allySpread,
            allyFireRate = allyFireRate,
            allyDamage = allyDamage,
        });
        arena.Hud.Announce($"ROUND {RoundNumber}", 1.6f);
        arena.Hud.Chat("Jay", Random.value < 0.5f ? "gl hf" : "we got this");
        if (!inVR && RoundNumber > 1) GameHUD.Toast($"Round {RoundNumber} started without you!", GameHUD.Warn);
    }

    void OnRoundEnded(bool won)
    {
        if (State != Phase.Round) return;
        if (won) Wins++; else Losses++;
        var stress = StressManager.Instance;
        if (stress)
        {
            if (won) stress.RemoveStress(6f);
            else stress.AddStress(6f);
        }
        var hud = VRArena.Instance ? VRArena.Instance.Hud : null;
        if (hud) hud.Chat(won ? "Jay" : "Nova", won ? (Random.value < 0.5f ? "EZ" : "lets gooo") : (inVR ? "unlucky" : "we needed you man"));

        if (Wins >= roundsToWin || Losses >= roundsToWin)
        {
            State = Phase.Over;
            bool victory = Wins >= roundsToWin;
            if (hud) hud.Announce(victory ? "VICTORY" : "DEFEAT", 3f, victory ? new Color32(90, 240, 220, 255) : new Color32(255, 85, 95, 255));
            Text(friendName, victory ? $"GG!! {Wins}-{Losses} we're climbing" : $"{Wins}-{Losses}... bro you were barely on");
            if (victory) Complete($"Match won {Wins}-{Losses}! +{RankPoints} RP");
            else Fail($"Match lost {Wins}-{Losses}. {RankPoints} RP");
            if (VRArena.Instance) VRArena.Instance.Stop();
            return;
        }
        if (hud) hud.Announce(won ? "ROUND WON" : "ROUND LOST", 2f, won ? new Color32(90, 240, 220, 255) : new Color32(255, 85, 95, 255));
        if (!inVR) GameHUD.Toast(won ? $"Your team won round {Wins + Losses} without you!" : $"Lost round {Wins + Losses} - you weren't there", won ? GameHUD.Good : GameHUD.Bad);
        State = Phase.Break;
        Timer = breakSeconds;
    }

    static void Text(string from, string message)
    {
        if (DayManager.Instance) DayManager.Instance.SendText(from, message);
    }
}
