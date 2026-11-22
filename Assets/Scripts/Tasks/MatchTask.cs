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
/// At 4:00 PM a best-of-5 match starts (55 s rounds, 45 s breaks). Each round is 8v8 DOMINATION: you, your friend
/// Jay and 6 bots against 8 enemy bots, fighting over capture points A, B and C (see VRArena). The enemy gets
/// sharper every round. Rounds start whether you're wearing the headset or not - without you, your avatar stands
/// at spawn AFK and your team plays 7v8. Before 4:00 the arena runs unranked warm-up Domination rounds.
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
    public float roundLength = 55f;
    [Tooltip("Domination: points to win a round (each held point scores 1.5 a second).")]
    public float scoreLimit = 100f;
    public float breakLength = 45f; // time between rounds (pick a class, sort the house)
    [Tooltip("If there's no day clock: seconds after the start before the match begins.")]
    public float fallbackStartDelay = 120f;
    public string friendName = "JAY";

    [Header("Enemy bots")]
    [Tooltip("Degrees their shots can miss by in round 1 (lower = better aim).")]
    public float enemyMissDegrees = 2.2f;
    public float enemyMissPerRound = 0.35f;
    public float enemyShotsPerSecond = 7f;
    public float enemyHitDamage = 10f;
    public float enemyReactTime = 0.45f;
    [Tooltip("Seconds an enemy needs to track you before its aim settles.")]
    public float enemyAimSettle = 0.9f;

    [Header("Your bot teammates")]
    public float allyMissDegrees = 2.6f;
    public float allyShotsPerSecond = 6.5f;
    public float allyHitDamage = 9f;
    public float allyReactTime = 0.55f;

    public enum Phase { Waiting, Round, Break, Over }
    public Phase State { get; private set; }
    public int Wins { get; private set; }
    public int Losses { get; private set; }
    public int RoundNumber => Wins + Losses + (State == Phase.Round ? 1 : 0);
    /// <summary>Seconds left in the break between rounds.</summary>
    public float Timer { get; private set; }
    public int RankPoints => Wins * 25 - Losses * 20 + (State == Phase.Over && Wins >= roundsToWin ? 30 : 0);
    /// <summary>Friday and you qualified: this match is the Tournament (see Campaign).</summary>
    public static bool IsTournament => Campaign.TournamentToday;
    public string StartTimeText => Campaign.HourText(matchStartHour);
    public string MatchName => IsTournament ? "Tournament" : "Ranked match";

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
                    return minutes > 15f ? $"Starts at {StartTimeText} - launch it on your PC" : $"Starts in {Mathf.CeilToInt(minutes)} min - launch it on your PC, then the headset!";
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
        int r = RoundNumber - 1;
        arena.StartRound(new VRArena.RoundSettings
        {
            seconds = roundLength,
            scoreLimit = scoreLimit,
            enemySpread = Mathf.Max(1.8f, enemyMissDegrees - enemyMissPerRound * r),
            enemyFireRate = enemyShotsPerSecond,
            enemyDamage = enemyHitDamage + r * 0.5f,
            enemyReaction = Mathf.Max(0.25f, enemyReactTime - r * 0.04f),
            enemyAimSettle = Mathf.Max(0.5f, enemyAimSettle - r * 0.08f),
            allySpread = allyMissDegrees,
            allyFireRate = allyShotsPerSecond,
            allyDamage = allyHitDamage,
            allyReaction = allyReactTime,
        });
        arena.Hud.Announce(IsTournament ? $"TOURNAMENT - ROUND {RoundNumber}" : $"ROUND {RoundNumber}", 1.6f);
        arena.Hud.Notice(IsTournament ? "TOURNAMENT - CAPTURE A, B AND C" : "DOMINATION - CAPTURE A, B AND C", Color.white);
        arena.Hud.Chat("Jay", Random.value < 0.5f ? "gl hf" : "we got this");
        if (!inVR && RoundNumber > 1) GameHUD.Toast($"Round {RoundNumber} started without you!", GameHUD.Warn);
    }

    /// <summary>Prize money when the match ends: $20 for the win + $3 for every round you took.</summary>
    void PayPrize(bool victory)
    {
        if (IsTournament) return; // the tournament pays its own prize (Campaign.OnMatchOver)
        int prize = (victory ? 20 : 0) + 3 * Wins;
        if (prize <= 0) return;
        Wallet.Earn(prize, $"Ranked prize - {(victory ? "won" : "lost")} {Wins}-{Losses}", Wallet.Category.Prize);
        GameHUD.Toast($"Ranked prize: +${prize}", GameHUD.Good);
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
            if (Campaign.OnMatchOver(Wins, Losses, victory)) PayPrize(victory); // false = this day's match was already counted before a restart
            if (VRArena.Instance) VRArena.Instance.Stop();
            return;
        }
        if (hud) hud.Announce(won ? "ROUND WON" : "ROUND LOST", 2f, won ? new Color32(90, 240, 220, 255) : new Color32(255, 85, 95, 255));
        if (!inVR) GameHUD.Toast(won ? $"Your team won round {Wins + Losses} without you!" : $"Lost round {Wins + Losses} - you weren't there", won ? GameHUD.Good : GameHUD.Bad);
        State = Phase.Break;
        Timer = breakLength;
    }

    static void Text(string from, string message)
    {
        if (DayManager.Instance) DayManager.Instance.SendText(from, message);
    }
}
