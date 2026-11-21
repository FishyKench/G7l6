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
/// deathmatch (practice). The match itself (schedule, best of 5) is the MatchTask on your PC in the house.
/// </summary>
public class VRArena : MonoBehaviour
{
    public static VRArena Instance { get; private set; }

    public enum Mode { Idle, Practice, Fighting }

    /// <summary>How the round plays (enemies get better each round).</summary>
    public struct RoundSettings
    {
        public float seconds;
        public float scoreLimit;
        public float enemySpread, enemyFireRate, enemyDamage, enemyReaction, enemyAimSettle;
        public float allySpread, allyFireRate, allyDamage, allyReaction;
    }

    public Mode State { get; private set; }
    public float TimeLeft { get; private set; }
    public VRPlayer Player { get; private set; }
    public VRHud Hud { get; private set; }
    public bool Ready { get; private set; }

    [Header("Domination")]
    [Tooltip("Points per second for each capture point a team holds.")]
    public float pointsPerFlag = 1.5f;
    public float botRespawnSeconds = 5f;
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
    float playerRespawnAt = -1f, ordersIn, lastCallout;

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
        // The arena scene can hold a ready-made camera with post effects (bloom, colour) called "VR Camera".
        Camera template = null;
        foreach (var c in GetComponentsInChildren<Camera>(true)) if (c.name == "VR Camera") template = c;
        Player = VRPlayer.Create(transform, template);
        var s = Spawn(spawnsA, 0, -1);
        Player.Respawn(s.position, s.rotation);
        Hud = gameObject.AddComponent<VRHud>();
        BuildSparks();
        Ready = true;
    }

    // ------------------------------------------------------------------ rounds

    public void StartRound(RoundSettings s)
    {
        ClearBots();
        Score[0] = Score[1] = 0f;
        ScoreLimit = s.scoreLimit > 0f ? s.scoreLimit : 100f;
        foreach (var f in flags) f.ResetFlag();
        playerRespawnAt = -1f;
        var spawn = Spawn(spawnsA, 0, -1);
        Player.Respawn(spawn.position, spawn.rotation);

        for (int i = 0; i < AllyNames.Length; i++)
        {
            var b = AddBot(AllyNames[i], 0, AllyColor, Spawn(spawnsA, i + 1, -1));
            bool friend = i == 0; // your friend plays about as well as the enemy
            b.spread = friend ? s.enemySpread * 1.05f : s.allySpread;
            b.fireRate = s.allyFireRate;
            b.damage = friend ? s.enemyDamage : s.allyDamage;
            b.reactionTime = friend ? s.enemyReaction : s.allyReaction;
            b.aimSettle = friend ? s.enemyAimSettle : s.enemyAimSettle * 1.3f;
            b.aggression = friend ? 0.65f : Random.Range(0.3f, 0.7f);
            b.preferredDistance = Random.Range(11f, 17f);
        }
        var names = new List<string>(EnemyNames);
        for (int i = 0; i < TeamSize; i++)
        {
            int n = Random.Range(0, names.Count);
            var b = AddBot(names[n], 1, EnemyColor, Spawn(spawnsB, i, 1));
            names.RemoveAt(n);
            b.spread = s.enemySpread;
            b.fireRate = s.enemyFireRate;
            b.damage = s.enemyDamage;
            b.reactionTime = s.enemyReaction;
            b.aimSettle = s.enemyAimSettle;
            b.aggression = Random.Range(0.35f, 0.85f);
            b.preferredDistance = Random.Range(10f, 18f);
        }
        TimeLeft = s.seconds;
        State = Mode.Fighting;
        ordersIn = 0f;
        GiveOrders();
    }

    /// <summary>
    /// Warm-up: a team deathmatch on the ranked map - you and your 7 teammates against 8 bots, everyone respawns.
    /// The bots roam between the capture points (nothing's scored), so there's always a fight somewhere.
    /// </summary>
    public void StartPractice()
    {
        ClearBots();
        var s = Spawn(spawnsA, 0, -1);
        Player.Respawn(s.position, s.rotation);
        Player.Kills = 0;
        foreach (var f in flags) f.ResetFlag();
        for (int i = 0; i < AllyNames.Length; i++)
        {
            var b = AddBot(AllyNames[i], 0, AllyColor, Spawn(spawnsA, i + 1, -1));
            b.spread = 3f; b.fireRate = 6f; b.damage = 8f; b.reactionTime = 0.55f; b.aimSettle = 1f;
            b.aggression = Random.Range(0.4f, 0.8f);
            b.preferredDistance = Random.Range(10f, 18f);
            if (flags.Count > 0) b.Objective = flags[Random.Range(0, flags.Count)];
        }
        var names = new List<string>(EnemyNames);
        for (int i = 0; i < TeamSize; i++)
        {
            int n = Random.Range(0, names.Count);
            var b = AddBot(names[n], 1, EnemyColor, Spawn(spawnsB, i, 1));
            names.RemoveAt(n);
            b.spread = 2.8f; b.fireRate = 6f; b.damage = 8f; b.reactionTime = 0.55f; b.aimSettle = 1f;
            b.aggression = Random.Range(0.4f, 0.8f);
            b.preferredDistance = Random.Range(10f, 18f);
            if (flags.Count > 0) b.Objective = flags[Random.Range(0, flags.Count)];
        }
        playerRespawnAt = -1f;
        practiceShuffle = 0f;
        State = Mode.Practice;
    }

    float practiceShuffle;

    public void Stop()
    {
        State = Mode.Idle;
        playerRespawnAt = -1f;
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

        if (State == Mode.Practice && flags.Count > 0)
        {
            practiceShuffle -= dt;
            if (practiceShuffle <= 0f)
            {
                practiceShuffle = 6f;
                foreach (var b in bots)
                    if (b && b.Alive && Random.value < 0.3f) b.Objective = flags[Random.Range(0, flags.Count)];
            }
        }
        if (State != Mode.Fighting) return;
        TimeLeft -= dt;
        int[] held = new int[2];
        foreach (var f in flags)
        {
            f.Tick(dt);
            if (f.Owner >= 0) held[f.Owner]++;
        }
        for (int t = 0; t < 2; t++) Score[t] = Mathf.Min(ScoreLimit, Score[t] + held[t] * pointsPerFlag * dt);

        ordersIn -= dt;
        if (ordersIn <= 0f) { ordersIn = 1.2f; GiveOrders(); }

        if (Score[0] < ScoreLimit && Score[1] < ScoreLimit && TimeLeft > 0f) return;
        bool tie = Mathf.Abs(Score[0] - Score[1]) < 0.5f;
        bool won = tie ? held[0] > held[1] || (held[0] == held[1] && Random.value < 0.5f) : Score[0] > Score[1];
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
        if (State != Mode.Fighting || !Hud) return;
        string l = flag.letter;
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
    /// Hands out orders: which capture point each bot goes for. Points the team doesn't have (or is losing) are
    /// worth more; nearer is better; the same point gets less useful the more bots already go there; bots keep
    /// their order unless another is clearly better. Aggressive bots go deeper. You count for your team.
    /// </summary>
    void GiveOrders()
    {
        if (flags.Count == 0) return;
        for (int team = 0; team < 2; team++)
        {
            var counts = new int[flags.Count];
            if (team == 0 && Player && Player.Alive && Player.controlled)
                for (int i = 0; i < flags.Count; i++)
                    if (Vector3.Distance(Player.transform.position, flags[i].Position) < flags[i].radius + 10f) counts[i]++;

            int held = 0;
            foreach (var f in flags) if (f.Owner == team) held++;
            var free = bots.FindAll(b => b && b.Alive && b.team == team);
            free.Sort((a, b) => a.aggression.CompareTo(b.aggression)); // careful bots pick first (they defend)
            foreach (var bot in free)
            {
                int best = 0;
                float bestValue = float.MinValue;
                for (int i = 0; i < flags.Count; i++)
                {
                    var f = flags[i];
                    float value;
                    bool threatened = f.Owner == team && (f.Inside[1 - team] > 0 || (f.Capturer == 1 - team && f.Progress > 0f));
                    if (f.Owner == team) value = threatened ? 2.2f : (counts[i] == 0 ? 1.1f : 0.25f);
                    else if (f.Owner < 0) value = 1.7f + (i == flags.Count / 2 ? 0.3f : 0f);
                    else value = held >= 2 ? 0.7f + bot.aggression * 0.6f : 1.4f + bot.aggression * 0.3f;
                    value -= Vector3.Distance(bot.transform.position, f.Position) / 30f;
                    value -= counts[i] * 0.5f;
                    if (bot.Objective == f) value += 0.35f;
                    if (value > bestValue) { bestValue = value; best = i; }
                }
                bot.Objective = flags[best];
                counts[best]++;
            }
        }
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
                bool head = hit.collider.name == "Head";
                float multiplier = head ? (shooter is VRPlayer p ? p.headshotMultiplier : 1.5f) : 1f;
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
        foreach (var b in bots)
            if (b && b.Alive && b.team != shooter.team) b.Hear(shooter, origin);
    }

    public void OnKill(VRFighter killer, VRFighter victim, bool head)
    {
        if (State == Mode.Practice || State == Mode.Fighting)
        {
            if (victim is VRBot b) respawns.Add((b, Time.time + (State == Mode.Fighting ? botRespawnSeconds : 3f)));
            if (victim is VRPlayer) playerRespawnAt = Time.time + (State == Mode.Fighting ? playerRespawnSeconds : 2.5f);
        }
        if (killer is VRPlayer me && Hud)
        {
            int streak = me.RegisterKill();
            string call = streak switch { 2 => "DOUBLE KILL", 3 => "TRIPLE KILL", 4 => "QUAD KILL", _ => streak >= 5 ? "UNSTOPPABLE" : null };
            if (call != null) Hud.Announce(call, 1.3f, new Color(1f, 0.82f, 0.3f));
            else if (head) Hud.Pop("HEADSHOT");
            else Hud.Pop("ELIMINATED");
        }
        if (Hud) Hud.Feed(killer ? killer.fighterName : "?", victim.fighterName, killer && killer.team == 0, head);
        if (Hud && killer is VRPlayer && Random.value < 0.45f)
            Hud.Chat(AllyNames[Random.Range(0, AllyNames.Length)], Random.value < 0.5f ? "nice shot" : head ? "HEADSHOT lol" : "W");
        if (Hud && victim is VRPlayer && Random.value < 0.6f)
            Hud.Chat(FriendName, Random.value < 0.5f ? "unlucky" : $"{(killer ? killer.fighterName : "they")} is cracked fr");
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
