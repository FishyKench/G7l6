using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// The VR arena (lives in the VRArena scene, which is loaded next to the house so both run at the same time).
/// Runs rounds - you + 2 bot teammates vs 3 enemy bots - and a practice mode with target dummies; works out
/// the walk map for the bots when it loads; handles every shot (hits, headshots, tracers, sparks).
/// The ranked match itself (schedule, score) is the MatchTask on the headset in the house.
/// </summary>
public class VRArena : MonoBehaviour
{
    public static VRArena Instance { get; private set; }

    public enum Mode { Idle, Practice, Fighting }

    /// <summary>How the bots play this round (enemies get better each round).</summary>
    public struct RoundSettings
    {
        public float seconds;
        public float enemySpread, enemyFireRate, enemyDamage, enemyReaction;
        public float allySpread, allyFireRate, allyDamage;
    }

    public Mode State { get; private set; }
    public float TimeLeft { get; private set; }
    public VRPlayer Player { get; private set; }
    public VRHud Hud { get; private set; }
    public bool Ready { get; private set; }

    /// <summary>Raised when a round ends: true = your team won it.</summary>
    public event System.Action<bool> RoundEnded;

    public static readonly Color AllyColor = new Color(0.25f, 0.6f, 1f);
    public static readonly Color EnemyColor = new Color(1f, 0.25f, 0.32f);
    static readonly string[] EnemyNames = { "xX_Toaster_Xx", "N00bSlay3r", "GrandmaGamer", "Sweaty_Kev", "LagSwitch", "MrNoScope", "CampLord", "Zer0Chill" };

    readonly List<Transform> spawnsA = new List<Transform>(), spawnsB = new List<Transform>();
    readonly List<VRBot> bots = new List<VRBot>();
    readonly List<(LineRenderer line, float until)> tracers = new List<(LineRenderer, float)>();
    readonly Queue<LineRenderer> tracerPool = new Queue<LineRenderer>();
    readonly List<(VRBot bot, float at)> respawns = new List<(VRBot, float)>();
    ParticleSystem sparks;
    TMP_FontAsset font;
    Bounds area;

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
        // The arena scene can hold a ready-made camera with post effects (bloom, colour) called "VR Camera".
        Camera template = null;
        foreach (var c in GetComponentsInChildren<Camera>(true)) if (c.name == "VR Camera") template = c;
        Player = VRPlayer.Create(transform, template);
        Player.Respawn(Spawn(spawnsA, 0, -1).position, Spawn(spawnsA, 0, -1).rotation);
        Hud = gameObject.AddComponent<VRHud>();
        BuildSparks();
        Ready = true;
    }

    // ------------------------------------------------------------------ rounds

    public void StartRound(RoundSettings s)
    {
        ClearBots();
        Player.Respawn(Spawn(spawnsA, 0, -1).position, Spawn(spawnsA, 0, -1).rotation);
        var names = new List<string>(EnemyNames);
        for (int i = 0; i < 2; i++)
        {
            var b = AddBot(i == 0 ? "Jay" : "Nova", 0, AllyColor, Spawn(spawnsA, i + 1, -1));
            b.spread = s.allySpread; b.fireRate = s.allyFireRate; b.damage = s.allyDamage; b.reactionTime = 0.55f;
        }
        for (int i = 0; i < 3; i++)
        {
            int n = Random.Range(0, names.Count);
            var b = AddBot(names[n], 1, EnemyColor, Spawn(spawnsB, i, 1));
            names.RemoveAt(n);
            b.spread = s.enemySpread; b.fireRate = s.enemyFireRate; b.damage = s.enemyDamage; b.reactionTime = s.enemyReaction;
            b.preferredDistance = Random.Range(11f, 18f);
        }
        TimeLeft = s.seconds;
        State = Mode.Fighting;
    }

    /// <summary>Warm-up: a free deathmatch against 4 bots that fight back. Everyone respawns.</summary>
    public void StartPractice()
    {
        ClearBots();
        Player.Respawn(Spawn(spawnsA, 0, -1).position, Spawn(spawnsA, 0, -1).rotation);
        Player.Kills = 0;
        var names = new List<string>(EnemyNames);
        for (int i = 0; i < 4; i++)
        {
            int n = Random.Range(0, names.Count);
            var b = AddBot(names[n], 1, EnemyColor, Spawn(spawnsB, i, 1));
            names.RemoveAt(n);
            b.spread = 9f; b.fireRate = 2.4f; b.damage = 5f; b.reactionTime = 0.7f;
            b.preferredDistance = Random.Range(10f, 18f);
        }
        playerRespawnAt = -1f;
        State = Mode.Practice;
    }

    float playerRespawnAt = -1f;

    public void Stop()
    {
        State = Mode.Idle;
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
            var extra = new GameObject("Extra spawn").transform;
            extra.SetParent(transform, false);
            extra.SetPositionAndRotation(t.position + t.right * 2f * (index - list.Count + 1), t.rotation);
            list.Add(extra);
            return extra;
        }
        var made = new GameObject("Spawn (made up)").transform;
        made.SetParent(transform, false);
        made.position = area.center + new Vector3(side * area.extents.x * 0.85f, 0f, (index - 1) * 3f);
        made.rotation = Quaternion.LookRotation(new Vector3(-side, 0f, 0f));
        return made;
    }

    void Update()
    {
        if (PauseMenu.IsPaused) return;
        float now = Time.time;
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
        for (int i = respawns.Count - 1; i >= 0; i--)
        {
            if (now < respawns[i].at) continue;
            var b = respawns[i].bot;
            respawns.RemoveAt(i);
            if (b && State == Mode.Practice) b.Respawn(Spawn(spawnsB, Random.Range(0, 3), 1).position + new Vector3(Random.Range(-3f, 3f), 0f, Random.Range(-4f, 4f)), b.transform.rotation);
        }
        if (State == Mode.Practice && playerRespawnAt > 0f && now >= playerRespawnAt)
        {
            playerRespawnAt = -1f;
            var s = Spawn(spawnsA, Random.Range(0, 3), -1);
            Player.Respawn(s.position, s.rotation);
        }

        if (State != Mode.Fighting) return;
        TimeLeft -= Time.deltaTime;
        bool usAlive = false, themAlive = false;
        float us = 0f, them = 0f;
        foreach (var f in VRFighter.All)
        {
            if (!f || !f.Alive || f.transform.root != transform.root) continue;
            if (f.team == 0) { usAlive = true; us += f.Health / f.maxHealth; }
            else { themAlive = true; them += f.Health / f.maxHealth; }
        }
        if (usAlive && themAlive && TimeLeft > 0f) return;
        bool won = !themAlive && usAlive || (usAlive == themAlive && us >= them);
        State = Mode.Idle;
        RoundEnded?.Invoke(won);
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
    }

    public void OnKill(VRFighter killer, VRFighter victim, bool head)
    {
        if (State == Mode.Practice)
        {
            if (victim is VRBot b) respawns.Add((b, Time.time + 3f));
            if (victim is VRPlayer) { playerRespawnAt = Time.time + 2.5f; if (Hud) Hud.Announce("RESPAWNING", 2f, EnemyColor); }
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
        if (Hud && killer is VRPlayer && Random.value < 0.6f) Hud.Chat(Random.value < 0.5f ? "Jay" : "Nova", Random.value < 0.5f ? "nice shot" : head ? "HEADSHOT lol" : "W");
        if (Hud && victim is VRPlayer) Hud.Chat(Random.value < 0.5f ? "Jay" : "Nova", Random.value < 0.5f ? "unlucky" : "we got it, chill");
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
