using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// The VR arena (lives in the VRArena scene, which is loaded next to the house so both run at the same time).
/// Ranked rounds are DOMINATION, 8v8: you, your friend Jay and 6 more bots against 8 enemy bots. Three capture
/// points (A, B, C - see VRFlag); every point your team holds scores points every second. First to the score
/// limit wins the round, otherwise whoever's ahead when the time runs out. Everyone respawns a few seconds after
/// dying, at their team's side of the map (the spawn furthest from the enemy).
/// The arena also hands out orders to the bots (which point to capture, attack or hold), finds cover spots for
/// them, lets them hear gunfire, and handles every shot (hits, headshots, tracers, sparks). There's also a warm-up
/// Domination too (unranked, it just keeps going round after round). Every bot has a class (VRLoadout): each team
/// is 1 sniper, 3 SMG rushers, 1 LMG anchor and assault riflemen. The match itself (schedule, best of 5) is the
/// MatchTask on your PC in the house.
/// </summary>
public class VRArena : MonoBehaviour
{
    public static VRArena Instance { get; private set; }

    /// <summary>Briefing: the few seconds before a round where you pick your class (everything waits).</summary>
    public enum Mode { Idle, Practice, Fighting, Briefing }

    /// <summary>How the round plays (enemies get better each round).</summary>
    public struct RoundSettings
    {
        public float seconds;
        public float scoreLimit;
        public float enemySpread, enemyFireRate, enemyDamage, enemyReaction, enemyAimSettle;
        public float allySpread, allyFireRate, allyDamage, allyReaction;
        /// <summary>Ranked / tournament: your rivals join the enemy team (see Rivals).</summary>
        public bool rivals, tournament;
    }

    public Mode State { get; private set; }
    /// <summary>Goes up every time a new round is set up (the replay recorder uses it to tell rounds apart).</summary>
    public int RoundSerial { get; private set; }
    /// <summary>The whole arena's bounds (found when it loaded).</summary>
    public Bounds AreaBounds => area;
    public float TimeLeft { get; private set; }
    public VRPlayer Player { get; private set; }
    public VRHud Hud { get; private set; }
    public bool Ready { get; private set; }

    [Header("Domination")]
    [Tooltip("Points per second for each capture point a team holds.")]
    public float pointsPerFlag = 1.5f;
    public float botRespawnSeconds = 4f;
    public float playerRespawnSeconds = 4f;

    /// <summary>Team scores this round: [0] = your team, [1] = the enemy.</summary>
    public readonly float[] Score = new float[2];
    public float ScoreLimit { get; private set; } = 100f;
    public IReadOnlyList<VRFlag> Flags => flags;
    /// <summary>Seconds until you respawn (negative = you're alive).</summary>
    public float PlayerRespawnIn => playerRespawnAt > 0f ? playerRespawnAt - Time.time : -1f;

    /// <summary>Raised when a round ends: true = your team won it.</summary>
    public event System.Action<bool> RoundEnded;

    public static readonly Color AllyColor = new Color(0.25f, 0.6f, 1f);
    public static readonly Color EnemyColor = new Color(1f, 0.25f, 0.32f);
    static readonly string[] EnemyNames = { "xX_Toaster_Xx", "N00bSlay3r", "GrandmaGamer", "Sweaty_Kev", "LagSwitch", "MrNoScope", "CampLord", "Zer0Chill", "TryHardTina", "Ping999" };
    public const string FriendName = "Jay";
    static readonly string[] AllyNames = { FriendName, "Nova", "Rook", "Pixel", "Echo", "Vex", "Kilo" };
    // Classes, in the same order as the names (your team has you as the 8th).
    static readonly VRRole[] AllyRoles = { VRRole.Assault, VRRole.SMG, VRRole.Sniper, VRRole.SMG, VRRole.LMG, VRRole.Assault, VRRole.SMG };
    static readonly VRRole[] EnemyRoles = { VRRole.Sniper, VRRole.SMG, VRRole.SMG, VRRole.SMG, VRRole.LMG, VRRole.Assault, VRRole.Assault, VRRole.Assault };
    /// <summary>Bots per team in a ranked round (your team: you + one fewer bot).</summary>
    public const int TeamSize = 8;

    readonly List<Transform> spawnsA = new List<Transform>(), spawnsB = new List<Transform>();
    readonly List<VRBot> bots = new List<VRBot>();
    readonly List<VRFlag> flags = new List<VRFlag>();
    readonly List<Vector3> coverSpots = new List<Vector3>();
    readonly List<(LineRenderer line, float until)> tracers = new List<(LineRenderer, float)>();
    readonly Queue<LineRenderer> tracerPool = new Queue<LineRenderer>();
    readonly List<(VRBot bot, float at)> respawns = new List<(VRBot, float)>();
    ParticleSystem sparks;
    TMP_FontAsset font;
    Bounds area;
    float playerRespawnAt = -1f, ordersIn, lastCallout, practiceAgainAt = -1f, briefingUntil;
    Mode afterBriefing;
    /// <summary>Seconds left to pick a class before the round starts anyway.</summary>
    public float BriefingLeft => State == Mode.Briefing ? Mathf.Max(0f, briefingUntil - Time.time) : 0f;
    public const float BriefingSeconds = 10f;
    readonly List<Vector3>[] perches = { new List<Vector3>(), new List<Vector3>() };

    static readonly Dictionary<string, Material> materials = new Dictionary<string, Material>();

    void Awake()
    {
        Instance = this;
        foreach (Transform t in GetComponentsInChildren<Transform>())
        {
            if (t.name.StartsWith("Spawn A")) spawnsA.Add(t);
            else if (t.name.StartsWith("Spawn B")) spawnsB.Add(t);
        }
        spawnsA.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
        spawnsB.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void Start()
    {
        font = GameHUD.Instance ? GameHUD.Instance.font : null;
        BakeWalkMap();
        BuildFlags();
        FindCoverSpots();
        FindPerches();
        // The arena scene can hold a ready-made camera with post effects (bloom, colour) called "VR Camera".
        Camera template = null;
        foreach (var c in GetComponentsInChildren<Camera>(true)) if (c.name == "VR Camera") template = c;
        Player = VRPlayer.Create(transform, template);
        var s = Spawn(spawnsA, 0, -1);
        Player.Respawn(s.position, s.rotation);
        Hud = gameObject.AddComponent<VRHud>();
        BuildSparks();
        gameObject.AddComponent<ReplayRecorder>(); // records every round for the Clip Studio
        Ready = true;
    }

    // ------------------------------------------------------------------ rounds

    public void StartRound(RoundSettings s)
    {
        SetUpTeams(s);
        Brief(Mode.Fighting);
        GiveOrders();
    }

    /// <summary>
    /// Warm-up before the ranked match: the same 8v8 Domination on the same map, unranked - bots a bit softer,
    /// and a new round starts by itself a few seconds after one ends.
    /// </summary>
    public void StartPractice()
    {
        Player.Kills = 0;
        SetUpTeams(new RoundSettings
        {
            seconds = 90f, scoreLimit = 100f,
            enemySpread = 2.8f, enemyFireRate = 6f, enemyDamage = 8f, enemyReaction = 0.55f, enemyAimSettle = 1f,
            allySpread = 2.8f, allyFireRate = 6f, allyDamage = 8f, allyReaction = 0.55f,
        });
        Brief(Mode.Practice);
        GiveOrders();
    }

    /// <summary>
    /// Before a round: if you're in the headset, everything waits (up to 10 s) while you pick your class - it
    /// starts the moment you pick. Not wearing it: the round just starts.
    /// </summary>
    void Brief(Mode then)
    {
        afterBriefing = then;
        if (Player && Player.controlled)
        {
            State = Mode.Briefing;
            briefingUntil = Time.time + BriefingSeconds;
        }
        else BeginPlay();
    }

    /// <summary>You picked a class on the cards: spawn with it and go.</summary>
    public void ConfirmClass(VRRole role)
    {
        if (!VRUnlocks.Has(role)) { if (Hud) Hud.Pop(VRUnlocks.LockedText(role).ToUpperInvariant()); return; }
        Player.ChooseRole(role);
        if (State != Mode.Briefing) return;
        var s = Spawn(spawnsA, 0, -1);
        Player.Respawn(s.position, s.rotation);
        BeginPlay();
    }

    void BeginPlay()
    {
        State = afterBriefing;
        if (!Hud) return;
        Hud.Announce("GO!", 1f, AllyColor);
        Hud.Notice(State == Mode.Practice ? "WARM-UP DOMINATION - CAPTURE A, B AND C" : "DOMINATION - CAPTURE A, B AND C", Color.white);
    }

    void SetUpTeams(RoundSettings s)
    {
        RoundSerial++;
        ClearBots();
        Score[0] = Score[1] = 0f;
        ScoreLimit = s.scoreLimit > 0f ? s.scoreLimit : 100f;
        foreach (var f in flags) f.ResetFlag();
        playerRespawnAt = -1f;
        practiceAgainAt = -1f;
        var spawn = Spawn(spawnsA, 0, -1);
        Player.Respawn(spawn.position, spawn.rotation);

        for (int i = 0; i < AllyNames.Length; i++)
        {
            var b = AddBot(AllyNames[i], 0, AllyColor, Spawn(spawnsA, i + 1, -1));
            bool friend = i == 0; // your friend plays about as well as the enemy
            b.ApplyRole(AllyRoles[i % AllyRoles.Length],
                friend ? s.enemySpread * 1.05f : s.allySpread,
                s.allyFireRate,
                friend ? s.enemyDamage : s.allyDamage,
                friend ? s.enemyReaction : s.allyReaction,
                friend ? s.enemyAimSettle : s.enemyAimSettle * 1.2f);
        }
        var rivals = s.rivals ? Rivals.ForMatch(s.tournament) : new List<Rivals.Rival>();
        var names = new List<string>(EnemyNames);
        if (s.rivals) names.RemoveAll(Rivals.IsRival); // in ranked, rivals only turn up when it's their turn
        for (int i = 0; i < TeamSize; i++)
        {
            if (i < rivals.Count)
            {
                // A rival: their own class, and a notch sharper than the rest.
                var r = rivals[i];
                var rb = AddBot(r.name, 1, EnemyColor, Spawn(spawnsB, i, 1));
                rb.title = Rivals.TitleFor(r);
                rb.ApplyRole(r.role, s.enemySpread * 0.68f, s.enemyFireRate, s.enemyDamage * 1.15f, s.enemyReaction * 0.7f, s.enemyAimSettle * 0.7f);
                continue;
            }
            if (names.Count == 0) names.Add("Player" + Random.Range(100, 999)); // never run out of names
            int n = Random.Range(0, names.Count);
            var b = AddBot(names[n], 1, EnemyColor, Spawn(spawnsB, i, 1));
            names.RemoveAt(n);
            // The enemy team is the elite one: tighter aim, quicker on the trigger, a bit more damage.
            b.ApplyRole(EnemyRoles[i % EnemyRoles.Length], s.enemySpread * 0.8f, s.enemyFireRate, s.enemyDamage * 1.1f, s.enemyReaction * 0.8f, s.enemyAimSettle * 0.8f);
        }
        foreach (var g in VRGrenade.Live.ToArray()) if (g) Destroy(g.gameObject);
        TimeLeft = s.seconds;
        ordersIn = 0f;
    }

    public void Stop()
    {
        State = Mode.Idle;
        playerRespawnAt = -1f;
        practiceAgainAt = -1f;
        ClearBots();
    }

    void ClearBots()
    {
        foreach (var b in bots) if (b) Destroy(b.gameObject);
        bots.Clear();
        respawns.Clear();
    }

    VRBot AddBot(string name, int team, Color color, Transform spawn)
    {
        var b = VRBot.Create(transform, name, team, color, font);
        b.Respawn(spawn.position, spawn.rotation);
        bots.Add(b);
        return b;
    }

    /// <summary>A spawn point; extra ones are made up next to the last one if the map has too few.</summary>
    Transform Spawn(List<Transform> list, int index, int side)
    {
        if (list.Count > 0)
        {
            var t = list[index % list.Count];
            if (index < list.Count) return t;
            var first = list[index % Mathf.Max(1, list.Count)];
            var extra = new GameObject("Extra spawn").transform;
            extra.SetParent(transform, false);
            extra.SetPositionAndRotation(first.position - first.forward * 2.5f + first.right * 1.2f, first.rotation);
            list.Add(extra);
            return extra;
        }
        var made = new GameObject("Spawn (made up)").transform;
        made.SetParent(transform, false);
        made.position = area.center + new Vector3(side * area.extents.x * 0.85f, 0f, (index - 1) * 3f);
        made.rotation = Quaternion.LookRotation(new Vector3(-side, 0f, 0f));
        list.Add(made);
        return made;
    }

    /// <summary>Where a team spawns (for the made-up test recording): index wraps round the spawn points.</summary>
    public Vector3 SpawnPosition(int team, int index)
    {
        var list = team == 0 ? spawnsA : spawnsB;
        if (list.Count == 0) return area.center + new Vector3((team == 0 ? -1 : 1) * area.extents.x * 0.85f, -area.extents.y, (index - 4) * 2.5f);
        return list[Mathf.Abs(index) % list.Count].position;
    }

    /// <summary>The team's spawn point furthest from living enemies (so nobody spawns into a gunfight).</summary>
    Transform SafeSpawn(int team)
    {
        var list = team == 0 ? spawnsA : spawnsB;
        if (list.Count == 0) return Spawn(list, 0, team == 0 ? -1 : 1);
        Transform best = list[0];
        float bestScore = float.MinValue;
        foreach (var s in list)
        {
            float nearest = 999f;
            foreach (var f in VRFighter.All)
                if (f && f.Alive && f.team != team) nearest = Mathf.Min(nearest, Vector3.Distance(f.transform.position, s.position));
            float score = nearest + Random.Range(0f, 4f);
            if (score > bestScore) { bestScore = score; best = s; }
        }
        return best;
    }

    void Update()
    {
        if (PauseMenu.IsPaused) return;
        float now = Time.time;
        float dt = Time.deltaTime;
        for (int i = tracers.Count - 1; i >= 0; i--)
        {
            var (line, until) = tracers[i];
            if (now >= until) { line.enabled = false; tracerPool.Enqueue(line); tracers.RemoveAt(i); continue; }
            float a = Mathf.Clamp01((until - now) / 0.08f);
            var c = line.startColor;
            c.a = a;
            line.startColor = c;
            line.endColor = new Color(c.r, c.g, c.b, a * 0.3f);
        }
        bool live = State == Mode.Practice || State == Mode.Fighting;
        for (int i = respawns.Count - 1; i >= 0; i--)
        {
            if (now < respawns[i].at) continue;
            var b = respawns[i].bot;
            respawns.RemoveAt(i);
            if (!b || !live) continue;
            var s = SafeSpawn(b.team);
            b.Respawn(s.position + new Vector3(Random.Range(-1.5f, 1.5f), 0f, Random.Range(-1.5f, 1.5f)), s.rotation);
        }
        if (live && playerRespawnAt > 0f && now >= playerRespawnAt)
        {
            playerRespawnAt = -1f;
            var s = SafeSpawn(0);
            Player.Respawn(s.position, s.rotation);
        }

        if (State == Mode.Briefing && now >= briefingUntil) BeginPlay();
        if (practiceAgainAt > 0f && now >= practiceAgainAt)
        {
            practiceAgainAt = -1f;
            if (State == Mode.Idle && Player && Player.controlled) StartPractice();
        }
        if (State != Mode.Fighting && State != Mode.Practice) return;
        TimeLeft -= dt;
        int[] held = new int[2];
        foreach (var f in flags)
        {
            f.Tick(dt);
            if (f.Owner >= 0) held[f.Owner]++;
        }
        for (int t = 0; t < 2; t++) Score[t] = Mathf.Min(ScoreLimit, Score[t] + held[t] * pointsPerFlag * dt);

        ordersIn -= dt;
        if (ordersIn <= 0f) { ordersIn = 2f; GiveOrders(); }

        if (Score[0] < ScoreLimit && Score[1] < ScoreLimit && TimeLeft > 0f) return;
        bool tie = Mathf.Abs(Score[0] - Score[1]) < 0.5f;
        bool won = tie ? held[0] > held[1] || (held[0] == held[1] && Random.value < 0.5f) : Score[0] > Score[1];
        LiveStream.OnRoundEnd(won);
        if (State == Mode.Practice)
        {
            State = Mode.Idle;
            playerRespawnAt = -1f;
            practiceAgainAt = now + 7f;
            if (Hud)
            {
                Hud.Announce(won ? "WARM-UP WON" : "WARM-UP LOST", 2.5f, won ? AllyColor : EnemyColor);
                Hud.Notice($"{Mathf.FloorToInt(Score[0])} - {Mathf.FloorToInt(Score[1])}   -   next warm-up round in 7s", Color.white);
            }
            return;
        }
        State = Mode.Idle;
        playerRespawnAt = -1f;
        RoundEnded?.Invoke(won);
    }

    // ------------------------------------------------------------------ capture points

    void BuildFlags()
    {
        var found = new List<Transform>();
        foreach (Transform t in GetComponentsInChildren<Transform>())
            if (t.name.StartsWith("Flag ")) found.Add(t);
        found.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
        if (found.Count == 0)
        {
            // An old map without markers: put A, B, C along the middle.
            string[] letters = { "A", "B", "C" };
            for (int i = 0; i < 3; i++)
            {
                var t = new GameObject("Flag " + letters[i]).transform;
                t.SetParent(transform, false);
                t.position = area.center + new Vector3((i - 1) * area.extents.x * 0.55f, -area.extents.y, 0f);
                if (NavMesh.SamplePosition(t.position, out var hit, 10f, NavMesh.AllAreas)) t.position = hit.position;
                found.Add(t);
            }
        }
        foreach (var t in found)
        {
            var flag = VRFlag.Create(transform, t.name.Substring(5).Trim(), t.position, font);
            flag.OwnerChanged += OnFlagChanged;
            flags.Add(flag);
        }
    }

    void OnFlagChanged(VRFlag flag, int owner, int old)
    {
        if ((State != Mode.Fighting && State != Mode.Practice) || !Hud) return;
        string l = flag.letter;
        LiveStream.OnFlag(owner);
        if (owner == 0) { Hud.Notice($"{l} CAPTURED", AllyColor); Callout(owner, flag); }
        else if (owner == 1) { Hud.Notice($"ENEMY CAPTURED {l}", EnemyColor); Callout(owner, flag); }
        else if (old == 0) Hud.Notice($"{l} LOST", EnemyColor);
        else Hud.Notice($"{l} NEUTRALIZED", AllyColor);
    }

    void Callout(int owner, VRFlag flag)
    {
        if (Time.time - lastCallout < 4f || !Hud) return;
        lastCallout = Time.time;
        string who = AllyNames[Random.Range(0, AllyNames.Length)];
        string l = flag.letter;
        string[] ours = { $"{l} is ours", $"got {l}", $"holding {l}, push up", $"{l} locked" };
        string[] theirs = { $"they took {l}", $"{l} is gone, retake it!", $"{l} lost, rotate!", $"they're stacking {l}" };
        Hud.Chat(who, owner == 0 ? ours[Random.Range(0, ours.Length)] : theirs[Random.Range(0, theirs.Length)]);
    }

    /// <summary>
    /// The team commander (one per team, every 2 s). It reads the game - who holds what, the score, the clock - and
    /// makes a plan, then hands every bot a job that suits its class:
    ///   - a point of ours under attack gets reinforcements first,
    ///   - the main attack goes for the best point we don't have (the middle first, then our home point, then theirs),
    ///     a smaller squad keeps pressure on a second point, and points we hold keep a defender or two,
    ///   - losing late in the round: everyone goes for one point,
    ///   - rushers (SMG) take the attack jobs and go round the side lanes (north / south), riflemen take the middle
    ///     lane, LMGs take the defending jobs, snipers watch the middle from their perches.
    /// Attackers gather at a staging spot near the point and storm it together (with a flash) - see VRBot.
    /// </summary>
    void GiveOrders()
    {
        if (flags.Count == 0) return;
        for (int team = 0; team < 2; team++)
        {
            int enemy = 1 - team;
            var squad = bots.FindAll(b => b && b.Alive && b.team == team && b.Role != VRRole.Sniper);
            foreach (var s in bots) if (s && s.Alive && s.team == team && s.Role == VRRole.Sniper) s.Objective = flags[flags.Count / 2];
            if (squad.Count == 0) continue;

            Vector3 home = Centre(team == 0 ? spawnsA : spawnsB);
            var byDistance = new List<VRFlag>(flags);
            byDistance.Sort((a, b) => Vector3.Distance(a.Position, home).CompareTo(Vector3.Distance(b.Position, home)));
            VRFlag homeFlag = byDistance[0], farFlag = byDistance[byDistance.Count - 1];
            VRFlag mid = flags.Count >= 3 ? byDistance[1] : homeFlag;

            var targets = new List<VRFlag>();
            foreach (var f in new[] { mid, homeFlag, farFlag }) if (f.Owner != team && !targets.Contains(f)) targets.Add(f);
            bool Threatened(VRFlag f) => f.Owner == team && (f.Inside[enemy] > 0 || (f.Capturer == enemy && f.Progress > 0f));
            bool lateAndLosing = TimeLeft < 25f && Score[team] < Score[enemy];

            // One entry per bot: (point, attacking?). In priority order.
            var jobs = new List<(VRFlag flag, bool attack)>();
            void Add(VRFlag f, int n, bool attack) { for (int i = 0; i < n; i++) jobs.Add((f, attack)); }
            foreach (var f in flags) if (Threatened(f)) Add(f, f == mid ? 4 : 2, false);
            VRFlag primary = targets.Count > 0 ? targets[0] : null;
            // B (the middle) is the point that wins games: most of the team goes for it whenever it isn't ours.
            int forPrimary = primary == mid ? Mathf.Max(4, Mathf.CeilToInt(squad.Count * 0.6f)) : targets.Count == 1 ? 4 : 3;
            if (primary) Add(primary, lateAndLosing ? squad.Count : forPrimary, true);
            if (targets.Count > 1 && !lateAndLosing) Add(targets[1], 2, true);
            foreach (var f in flags) if (f.Owner == team && !Threatened(f)) Add(f, f == mid ? 3 : 1, false);
            while (jobs.Count < squad.Count) Add(primary ? primary : mid, 1, primary);

            // Hand the jobs out: rushers pick attacks first, LMGs defence, then everyone else; near and steady wins.
            var order = new List<VRBot>();
            order.AddRange(squad.FindAll(b => b.Role == VRRole.SMG));
            order.AddRange(squad.FindAll(b => b.Role == VRRole.LMG));
            order.AddRange(squad.FindAll(b => b.Role != VRRole.SMG && b.Role != VRRole.LMG));
            jobs.RemoveRange(Mathf.Min(jobs.Count, squad.Count + 4), Mathf.Max(0, jobs.Count - squad.Count - 4));
            var laneCount = new Dictionary<VRFlag, int>();
            foreach (var bot in order)
            {
                int best = -1;
                float bestCost = float.MaxValue;
                for (int i = 0; i < jobs.Count; i++)
                {
                    var (f, attack) = jobs[i];
                    float cost = i * 0.6f + Vector3.Distance(bot.transform.position, f.Position) / 12f;
                    if (bot.Role == VRRole.SMG && !attack) cost += 6f;
                    if (bot.Role == VRRole.LMG && attack) cost += 4f;
                    if (bot.Objective == f) cost -= 2.5f; // keep doing what it was doing
                    if (cost < bestCost) { bestCost = cost; best = i; }
                }
                if (best < 0) break;
                var job = jobs[best];
                jobs.RemoveAt(best);
                bool changed = bot.Objective != job.flag;
                bot.Objective = job.flag;
                if (job.attack)
                {
                    // Attackers spread over the lanes: rushers round the sides, riflemen up the middle.
                    laneCount.TryGetValue(job.flag, out int k);
                    laneCount[job.flag] = k + 1;
                    int lane = bot.Role == VRRole.SMG ? (k % 2 == 0 ? 1 : -1) : (k % 3 == 2 ? (Random.value < 0.5f ? 1 : -1) : 0);
                    if (changed || bot.Lane == 0 && lane != 0 && bot.Role == VRRole.SMG) bot.Lane = lane;
                }
                else bot.Lane = 0;
            }

            if (team == 0 && Hud && primary && primary != lastPlan && Time.time - lastPlanCall > 8f)
            {
                lastPlan = primary;
                lastPlanCall = Time.time;
                string[] calls = { $"everyone on {primary.letter}, group up then push", $"{primary.letter} next - flank it from the sides", $"lets take {primary.letter}, I'll flash" };
                Hud.Chat(FriendName, calls[Random.Range(0, calls.Length)]);
            }
        }
    }

    VRFlag lastPlan;
    float lastPlanCall = -10f;

    /// <summary>The middle of a side lane (north +1 / south -1) as a z position; 0 = the main street.</summary>
    public float LaneZ(int lane)
    {
        float mid = flags.Count > 0 ? flags[flags.Count / 2].Position.z : area.center.z;
        return mid + lane * Mathf.Min(27f, area.extents.z * 0.6f);
    }

    /// <summary>Enough teammates near this bot heading for the same point to storm it together?</summary>
    public bool SquadReady(VRBot bot, VRFlag flag)
    {
        int n = 0;
        foreach (var f in VRFighter.All)
        {
            if (!f || f == bot || !f.Alive || f.team != bot.team) continue;
            bool sameJob = f is VRBot b ? b.Objective == flag : true;
            if (sameJob && Vector3.Distance(f.transform.position, bot.transform.position) < 18f) n++;
        }
        return n >= 2;
    }

    /// <summary>Which way the enemy of 'team' will come from, seen from a point (towards their side of the map).</summary>
    public Vector3 ThreatDirection(int team, Vector3 from)
    {
        Vector3 enemySide = Vector3.zero;
        var list = team == 0 ? spawnsB : spawnsA;
        foreach (var s in list) enemySide += s.position;
        enemySide = list.Count > 0 ? enemySide / list.Count : area.center;
        // A nearer point the enemy holds or is taking is where they are.
        float best = float.MaxValue;
        foreach (var f in flags)
        {
            if (f.Owner == team || Vector3.Distance(f.Position, from) < 2f) continue;
            float d = Vector3.Distance(f.Position, from);
            if (d < best && d < Vector3.Distance(enemySide, from)) { best = d; enemySide = f.Position; }
        }
        Vector3 dir = enemySide - from;
        dir.y = 0f;
        return dir.sqrMagnitude > 0.01f ? dir.normalized : Vector3.forward;
    }

    // ------------------------------------------------------------------ cover

    /// <summary>Spots next to something solid (walls, cars, crates), found once when the arena loads.</summary>
    void FindCoverSpots()
    {
        coverSpots.Clear();
        float floor = transform.position.y;
        for (float x = area.min.x; x <= area.max.x; x += 2.5f)
        for (float z = area.min.z; z <= area.max.z; z += 2.5f)
        {
            var p = new Vector3(x, floor + 0.5f, z);
            if (!NavMesh.SamplePosition(p, out var hit, 1.2f, NavMesh.AllAreas)) continue;
            if (hit.position.y > floor + 1f) continue; // roofs
            if (Physics.CheckSphere(hit.position + Vector3.up * 1.2f, 0.9f, ~0, QueryTriggerInteraction.Ignore)) coverSpots.Add(hit.position); // (kept clear of the ground)
        }
    }

    /// <summary>
    /// Sniper perches for each team: cover spots on their own half, 24-55 m from the middle point, with a clear
    /// view of it. Spread out so snipers have a few to move between.
    /// </summary>
    void FindPerches()
    {
        if (flags.Count == 0) return;
        Vector3 mid = flags[flags.Count / 2].Position + Vector3.up * 1.2f;
        for (int team = 0; team < 2; team++)
        {
            perches[team].Clear();
            Vector3 own = Centre(team == 0 ? spawnsA : spawnsB), enemy = Centre(team == 0 ? spawnsB : spawnsA);
            var candidates = new List<Vector3>();
            foreach (var c in coverSpots)
            {
                if (Vector3.Distance(c, own) > Vector3.Distance(c, enemy)) continue;
                float d = Vector3.Distance(c, mid);
                if (d < 24f || d > 55f || Blocked(c + Vector3.up * 1.4f, mid)) continue;
                candidates.Add(c);
            }
            candidates.Sort((a, b) => Vector3.Distance(b, mid).CompareTo(Vector3.Distance(a, mid)));
            foreach (var c in candidates)
            {
                if (perches[team].Exists(p => Vector3.Distance(p, c) < 7f)) continue;
                perches[team].Add(c);
                if (perches[team].Count >= 8) break;
            }
        }
    }

    static Vector3 Centre(List<Transform> list)
    {
        if (list.Count == 0) return Vector3.zero;
        Vector3 sum = Vector3.zero;
        foreach (var t in list) sum += t.position;
        return sum / list.Count;
    }

    /// <summary>A sniper perch for this team, away from 'avoid' (the one it's leaving).</summary>
    public bool Perch(int team, Vector3 avoid, out Vector3 spot)
    {
        spot = default;
        var list = perches[Mathf.Clamp(team, 0, 1)];
        if (list.Count == 0) return false;
        for (int i = 0; i < 6; i++)
        {
            spot = list[Random.Range(0, list.Count)];
            if (Vector3.Distance(spot, avoid) > 3f) return true;
        }
        return true;
    }

    /// <summary>The nearest spot within 'maxDistance' of 'near' that a shooter at 'threat' can't hit.</summary>
    public bool FindCover(Vector3 near, Vector3 threat, float maxDistance, out Vector3 spot)
    {
        spot = default;
        float best = float.MaxValue;
        int checks = 0;
        foreach (var c in coverSpots)
        {
            float d = Vector3.Distance(c, near);
            if (d > maxDistance || d >= best) continue;
            if (Vector3.Distance(c, threat) < 5f) continue;
            if (++checks > 40) break;
            if (!Blocked(threat, c + Vector3.up * 1.1f)) continue;
            best = d;
            spot = c;
        }
        return best < float.MaxValue;
    }

    static readonly RaycastHit[] blockHits = new RaycastHit[16];

    /// <summary>Is something solid (not a fighter) between a and b?</summary>
    public static bool LineBlocked(Vector3 a, Vector3 b) => Blocked(a, b);

    static bool Blocked(Vector3 from, Vector3 to)
    {
        Vector3 d = to - from;
        int n = Physics.RaycastNonAlloc(from, d.normalized, blockHits, d.magnitude, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
            if (!blockHits[i].collider.GetComponentInParent<VRFighter>()) return true;
        return false;
    }

    // ------------------------------------------------------------------ shooting

    /// <summary>A hitscan shot. Hits the first thing in the way; no friendly fire.</summary>
    public void FireShot(VRFighter shooter, Vector3 origin, Vector3 dir, float damage, Color color, Vector3? muzzle = null)
    {
        const float range = 120f;
        Vector3 end = origin + dir * range;
        VRFighter hitFighter = null;
        bool hitHead = false;
        var hits = Physics.RaycastAll(origin, dir, range, ~0, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (var hit in hits)
        {
            var f = hit.collider.GetComponentInParent<VRFighter>();
            if (f == shooter) continue;
            end = hit.point;
            if (f != null)
            {
                if (f.team == shooter.team || !f.Alive) break; // a teammate's in the way: the shot stops there
                bool head = hit.collider.name == "Head" || (f is VRPlayer victim && victim.IsHead(hit.point));
                hitFighter = f;
                hitHead = head;
                bool sniper = shooter is VRBot sb && sb.Role == VRRole.Sniper;
                float multiplier = head ? (shooter is VRPlayer p ? p.headshotMultiplier : sniper ? 2f : 1.5f) : 1f;
                f.TakeDamage(damage * multiplier, shooter, head);
                if (shooter is VRPlayer me)
                {
                    me.ShotLanded(head, !f.Alive);
                    FloatingWord.Spawn(Mathf.RoundToInt(damage * multiplier).ToString(), hit.point + Vector3.up * 0.2f,
                        head ? new Color(1f, 0.82f, 0.3f) : Color.white, font, head ? 1.6f : 1.2f, 0.6f);
                }
                Burst(hit.point, Color.white, 4);
            }
            else Burst(hit.point, color, 6);
            break;
        }
        Tracer(muzzle ?? origin, end, color);
        ReplayRecorder.Shot(shooter, muzzle ?? origin, end, hitFighter, hitHead);
        foreach (var b in bots)
            if (b && b.Alive && b.team != shooter.team) b.Hear(shooter, origin);
    }

    public void OnKill(VRFighter killer, VRFighter victim, bool head)
    {
        ReplayRecorder.Kill(killer, victim, head);
        if (State == Mode.Practice || State == Mode.Fighting)
        {
            if (victim is VRBot b) respawns.Add((b, Time.time + (State == Mode.Fighting ? botRespawnSeconds : 3f)));
            if (victim is VRPlayer) playerRespawnAt = Time.time + (State == Mode.Fighting ? playerRespawnSeconds : 2.5f);
        }
        if (killer is VRBot sniper && sniper.Role == VRRole.Sniper) sniper.MovePerch();
        if (killer is VRPlayer me && Hud)
        {
            int streak = me.RegisterKill();
            if (streak == 3 || streak == 5) SendHunters(me);
            LiveStream.OnPlayerKill(head, streak);
            string call = streak switch { 2 => "DOUBLE KILL", 3 => "TRIPLE KILL", 4 => "QUAD KILL", _ => streak >= 5 ? "UNSTOPPABLE" : null };
            bool rivalDown = State == Mode.Fighting && victim && Rivals.IsRival(victim.fighterName);
            if (call == null && rivalDown) Hud.Announce(Rivals.Nemesis != null && Rivals.Nemesis.name == victim.fighterName ? "NEMESIS DOWN" : "RIVAL DOWN", 1.4f, new Color(1f, 0.78f, 0.24f));
            else if (call != null) Hud.Announce(call, 1.3f, new Color(1f, 0.82f, 0.3f));
            else if (head) Hud.Pop("HEADSHOT");
            else Hud.Pop("ELIMINATED");
        }
        if (victim is VRPlayer) LiveStream.OnPlayerDeath();
        if (State == Mode.Fighting)
        {
            string line = Rivals.OnKill(killer, victim, out var rival, out _);
            if (Hud && rival != null && line != null) Hud.Chat(rival.name, line);
        }
        if (Hud) Hud.Feed(killer ? killer.fighterName : "?", victim.fighterName, killer && killer.team == 0, head);
        if (Hud && killer is VRPlayer && Random.value < 0.45f)
            Hud.Chat(AllyNames[Random.Range(0, AllyNames.Length)], Random.value < 0.5f ? "nice shot" : head ? "HEADSHOT lol" : "W");
        if (Hud && victim is VRPlayer && Random.value < 0.6f)
            Hud.Chat(FriendName, Random.value < 0.5f ? "unlucky" : $"{(killer ? killer.fighterName : "they")} is cracked fr");
    }

    /// <summary>You're on a streak: the two nearest enemy rushers/riflemen come after you for 20 s.</summary>
    void SendHunters(VRPlayer me)
    {
        var hunters = bots.FindAll(b => b && b.Alive && b.team != me.team && (b.Role == VRRole.SMG || b.Role == VRRole.Assault));
        hunters.Sort((a, b) => Vector3.Distance(a.transform.position, me.transform.position).CompareTo(Vector3.Distance(b.transform.position, me.transform.position)));
        for (int i = 0; i < Mathf.Min(2, hunters.Count); i++)
        {
            hunters[i].Hunt = me;
            hunters[i].HuntUntil = Time.time + 20f;
        }
        if (hunters.Count > 0 && Hud) Hud.Notice("THEY'RE HUNTING YOU", EnemyColor);
    }

    void Tracer(Vector3 from, Vector3 to, Color color)
    {
        var line = tracerPool.Count > 0 ? tracerPool.Dequeue() : NewTracer();
        line.enabled = true;
        line.SetPosition(0, from);
        line.SetPosition(1, to);
        line.startColor = color;
        line.endColor = new Color(color.r, color.g, color.b, 0.3f);
        tracers.Add((line, Time.time + 0.08f));
    }

    LineRenderer NewTracer()
    {
        var go = new GameObject("Tracer");
        go.transform.SetParent(transform, false);
        var line = go.AddComponent<LineRenderer>();
        line.positionCount = 2;
        line.useWorldSpace = true;
        line.startWidth = 0.035f;
        line.endWidth = 0.015f;
        line.sharedMaterial = TracerMaterial();
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        return line;
    }

    static Material tracerMaterial;
    static Material TracerMaterial()
    {
        if (tracerMaterial) return tracerMaterial;
        var shader = Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color");
        tracerMaterial = new Material(shader);
        return tracerMaterial;
    }

    public void Burst(Vector3 at, Color color, int count = 14)
    {
        if (!sparks) return;
        var p = new ParticleSystem.EmitParams { position = at, startColor = color, applyShapeToPosition = true };
        sparks.Emit(p, count);
    }

    void BuildSparks()
    {
        var go = new GameObject("Sparks");
        go.transform.SetParent(transform, false);
        sparks = go.AddComponent<ParticleSystem>();
        sparks.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = sparks.main;
        main.playOnAwake = false;
        main.loop = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.15f, 0.4f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(2f, 6f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.08f);
        main.gravityModifier = 1f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 600;
        var emission = sparks.emission;
        emission.enabled = false;
        var shape = sparks.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.05f;
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = TracerMaterial();
        sparks.Play();
    }

    // ------------------------------------------------------------------ the bots' walk map

    void BakeWalkMap()
    {
        var sources = new List<NavMeshBuildSource>();
        NavMeshBuilder.CollectSources(transform, ~0, NavMeshCollectGeometry.PhysicsColliders, 0, new List<NavMeshBuildMarkup>(), sources);
        bool any = false;
        foreach (var c in GetComponentsInChildren<Collider>())
        {
            if (!any) { area = c.bounds; any = true; }
            else area.Encapsulate(c.bounds);
        }
        if (!any) area = new Bounds(transform.position, new Vector3(60f, 10f, 40f));
        var walkArea = area;
        walkArea.Expand(2f);

        var settings = NavMesh.GetSettingsByID(0);
        settings.agentRadius = 0.4f;
        settings.agentHeight = 1.8f;
        settings.agentClimb = 0.45f;
        settings.agentSlope = 40f;
        var data = NavMeshBuilder.BuildNavMeshData(settings, sources, walkArea, Vector3.zero, Quaternion.identity);
        if (data) NavMesh.AddNavMeshData(data);
        else Debug.LogWarning("[VR] Couldn't build the walk map for the arena bots.");
    }

    // ------------------------------------------------------------------ materials (shared, made in code)

    /// <summary>A glowing material (neon).</summary>
    public static Material Glow(Color baseColor, Color emission, float strength)
    {
        string key = $"glow{baseColor}{emission}{strength}";
        if (materials.TryGetValue(key, out var m) && m) return m;
        m = new Material(Shader.Find("Standard")) { color = baseColor };
        m.EnableKeyword("_EMISSION");
        m.SetColor("_EmissionColor", emission * strength);
        m.SetFloat("_Glossiness", 0.5f);
        materials[key] = m;
        return m;
    }

    public static Material Solid(Color color, float gloss)
    {
        string key = $"solid{color}{gloss}";
        if (materials.TryGetValue(key, out var m) && m) return m;
        m = new Material(Shader.Find("Standard")) { color = color };
        m.SetFloat("_Glossiness", gloss);
        materials[key] = m;
        return m;
    }
}
