using TMPro;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// A bot in the VR arena (a teammate or an enemy) that actually plays Domination:
///  - It SEES only what's in front of it (a 150-degree view, plus anything really close) with a clear line, and
///    takes a moment to react to someone it just spotted (longer when they're far away).
///  - It HEARS gunfire and turns towards it, and remembers where it last saw or heard an enemy.
///  - Its AIM settles while it tracks you: the first shots miss more, a long burst gets accurate. Moving, a
///    moving target and distance all make it miss more.
///  - In a fight it strafes, crouches to steady its aim, reloads, and falls back to cover when it's hurt
///    (health comes back after a few seconds), then peeks out again.
///  - Out of a fight it goes where its team needs it (the arena hands out capture points): stands in the ring to
///    capture, or holds a point it owns from nearby cover, watching where the enemy comes from.
///  - CLASSES (VRLoadout): SNIPERS take a perch with a long view of the middle and wait until their aim has
///    settled - you see their red laser on you first; they move perch after a kill. SMG RUSHERS sprint and slide,
///    flank down the side lanes and flash a point before pushing in. LMG ANCHORS sit on points they own.
///    ASSAULT bots do a bit of everything.
///  - GRENADES: a frag to flush out someone hiding behind cover they just lost sight of, or onto a crowded point;
///    a flash before pushing into a point the enemy holds. They run from live frags, and a flashbang blinds them.
/// Built from primitives - it's a VR avatar, it's allowed to look like one.
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
public class VRBot : VRFighter
{
    [Header("Skill")]
    public float fireRate = 3f;
    public float damage = 8f;
    [Tooltip("Degrees a shot can miss by once its aim has settled.")]
    public float spread = 4f;
    [Tooltip("Seconds to react to someone it just spotted (close up).")]
    public float reactionTime = 0.45f;
    [Tooltip("Seconds of tracking a target before its aim settles.")]
    public float aimSettle = 0.9f;
    public float preferredDistance = 14f;
    [Range(0f, 1f)] public float aggression = 0.5f;
    public int magazine = 24;
    public float reloadTime = 1.8f;
    public bool dummy;

    /// <summary>The capture point the team wants this bot at (handed out by the arena's commander).</summary>
    public VRFlag Objective { get; set; }
    /// <summary>Which way it goes in when attacking: -1 south lane, 0 main street, +1 north lane.</summary>
    public int Lane { get; set; }
    /// <summary>Sent after someone (you, on a kill streak): it always knows roughly where they are, until 'HuntUntil'.</summary>
    public VRFighter Hunt { get; set; }
    public float HuntUntil { get; set; }
    /// <summary>What it's doing, for debugging and team chat.</summary>
    public string Doing { get; private set; } = "";
    /// <summary>Shown after the name on the tag (e.g. "  RIVAL").</summary>
    public string title = "";

    const float ViewAngle = 75f, ViewDistance = 75f, Hearing = 38f;
    const float CombatSpeed = 4f, CrouchSpeed = 2f, SlideSpeed = 10.5f;
    float runSpeed = 6.2f;
    float RunSpeed => runSpeed;

    /// <summary>This bot's class.</summary>
    public VRRole Role { get; private set; } = VRRole.Assault;
    public bool Blinded => Time.time < blindUntil;
    bool Sliding => Time.time < slideUntil;
    /// <summary>For the replay recorder.</summary>
    public bool IsCrouched => crouched;
    public bool IsSliding => Sliding;
    int fragsPerLife = 1, flashesPerLife = 1, frags, flashes;
    float blindUntil, slideUntil, slideReady, fleeUntil, nextNade, perchUntil;
    bool slideEnding, hasPerch, flankDone;
    int laneFor = 99;
    float stageSince = -1f;
    Vector3 perch, flankPoint;
    VRFlag flankFor;
    LineRenderer laser;
    TextMeshPro nameText;
    static Material laserMaterial;

    NavMeshAgent agent;
    VRFighter target;
    bool seesTarget;
    Vector3 lastKnown;
    float lastKnownAt = -100f;
    float spotting, tracking, think, fireCooldown, burstLeft, reloadLeft, deathTime, holdUntil, coverUntil, stuckFor, repath;
    int ammo;
    bool crouched, takingCover;
    Vector3 objectiveSpot, watchDir;
    VRFlag spotFor;
    float spotPickedAt;

    Renderer[] bodyRenderers;
    Color teamColor;
    Transform visual, nameTag, gun;
    CapsuleCollider body;
    SphereCollider headCollider;
    Material flashMaterial, bodyMaterial;
    float flash, bob, lean, recoil;

    public override Vector3 AimPoint => transform.position + Vector3.up * (crouched ? 0.8f : 1.1f);
    public override Vector3 EyePoint => transform.position + Vector3.up * (crouched ? 1.15f : 1.55f);
    protected override bool CanBeHurt => Time.time >= spawnedAt + 1f;
    float spawnedAt;

    public static VRBot Create(Transform parent, string name, int team, Color color, TMP_FontAsset font)
    {
        var go = new GameObject("Bot - " + name);
        go.transform.SetParent(parent, false);
        var bot = go.AddComponent<VRBot>();
        bot.fighterName = name;
        bot.team = team;
        bot.teamColor = color;
        bot.Build(font);
        return bot;
    }

    void Build(TMP_FontAsset font)
    {
        agent = GetComponent<NavMeshAgent>();
        agent.radius = 0.4f;
        agent.height = 1.8f;
        agent.speed = RunSpeed;
        agent.acceleration = 30f;
        agent.angularSpeed = 0f; // turned by hand (to face the target while strafing)
        agent.updateRotation = false;
        agent.stoppingDistance = 0.3f;
        agent.autoBraking = true;
        agent.obstacleAvoidanceType = ObstacleAvoidanceType.MedQualityObstacleAvoidance;
        agent.avoidancePriority = Random.Range(30, 70);
        agent.enabled = false; // switched on at its spawn point (on the walk map)

        body = gameObject.AddComponent<CapsuleCollider>();
        body.center = new Vector3(0f, 0.85f, 0f);
        body.height = 1.7f;
        body.radius = 0.38f;

        visual = new GameObject("Visual").transform;
        visual.SetParent(transform, false);
        bodyMaterial = VRArena.Glow(teamColor * 0.55f, teamColor, 0.9f);
        var dark = VRArena.Solid(new Color(0.06f, 0.06f, 0.09f), 0.8f);
        Part(PrimitiveType.Capsule, "Body", new Vector3(0f, 0.8f, 0f), new Vector3(0.72f, 0.8f, 0.6f), bodyMaterial);
        Part(PrimitiveType.Cube, "Pack", new Vector3(0f, 1.0f, -0.3f), new Vector3(0.45f, 0.5f, 0.18f), dark);
        var head = Part(PrimitiveType.Sphere, "Head", new Vector3(0f, 1.62f, 0f), Vector3.one * 0.46f, bodyMaterial);
        Part(PrimitiveType.Cube, "Visor", new Vector3(0f, 1.65f, 0.17f), new Vector3(0.38f, 0.14f, 0.16f), VRArena.Glow(Color.black, Color.white, 1.4f));
        gun = Part(PrimitiveType.Cube, "Gun", new Vector3(0.28f, 1.1f, 0.45f), new Vector3(0.09f, 0.12f, 0.7f), dark).transform;
        headCollider = new GameObject("Head").AddComponent<SphereCollider>();
        headCollider.transform.SetParent(transform, false);
        headCollider.transform.localPosition = head.transform.localPosition;
        headCollider.radius = 0.25f;
        bodyRenderers = visual.GetComponentsInChildren<Renderer>();

        var tag = new GameObject("Name").AddComponent<TextMeshPro>();
        tag.transform.SetParent(transform, false);
        tag.transform.localPosition = Vector3.up * 2.25f;
        if (font) tag.font = font;
        tag.text = fighterName;
        tag.fontSize = 2.4f;
        tag.color = Color.Lerp(teamColor, Color.white, 0.4f);
        tag.alignment = TextAlignmentOptions.Center;
        tag.rectTransform.sizeDelta = new Vector2(4f, 0.6f);
        nameTag = tag.transform;
        nameText = tag;

        laser = new GameObject("Laser").AddComponent<LineRenderer>();
        laser.transform.SetParent(transform, false);
        laser.positionCount = 2;
        laser.useWorldSpace = true;
        laser.startWidth = 0.018f;
        laser.endWidth = 0.008f;
        if (!laserMaterial) laserMaterial = new Material(Shader.Find("Sprites/Default"));
        laser.sharedMaterial = laserMaterial;
        laser.startColor = new Color(teamColor.r, teamColor.g, teamColor.b, 0.7f);
        laser.endColor = new Color(teamColor.r, teamColor.g, teamColor.b, 0.25f);
        laser.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        laser.enabled = false;
    }

    /// <summary>
    /// Gives the bot a class: its gun, how it moves and where it likes to fight, on top of the round's skill level
    /// (baseSpread / baseFireRate / baseDamage are what an ASSAULT bot gets this round).
    /// </summary>
    public void ApplyRole(VRRole role, float baseSpread, float baseFireRate, float baseDamage, float reaction, float settle)
    {
        Role = role;
        var l = VRLoadout.For(role);
        spread = baseSpread * l.botSpread;
        damage = role == VRRole.Sniper ? 55f : baseDamage * l.botDamage; // sniper: two to the body, one to the head
        fireRate = role == VRRole.Sniper ? 1f : baseFireRate * l.botFireRate;
        reactionTime = reaction * (role == VRRole.SMG ? 0.85f : 1f);
        aimSettle = role == VRRole.Sniper ? settle * 1.5f : settle;
        runSpeed = l.botRun;
        preferredDistance = l.botDistance * Random.Range(0.85f, 1.15f);
        aggression = Mathf.Clamp01(l.botAggression + Random.Range(-0.1f, 0.1f));
        magazine = l.botMagazine;
        reloadTime = role == VRRole.LMG ? 3f : role == VRRole.Sniper ? 2.4f : 1.8f;
        fragsPerLife = l.frags;
        flashesPerLife = l.flashes;
        frags = fragsPerLife;
        flashes = flashesPerLife;
        ammo = magazine;
        if (agent) agent.speed = runSpeed;
        if (gun)
        {
            float len = role == VRRole.SMG ? 0.45f : role == VRRole.Sniper ? 1.1f : role == VRRole.LMG ? 0.85f : 0.7f;
            float thick = role == VRRole.LMG ? 1.3f : 1f;
            gun.localScale = new Vector3(0.09f * thick, 0.12f * thick, len);
            gun.localPosition = new Vector3(0.28f, 1.1f, 0.1f + len * 0.5f);
        }
        if (nameText)
        {
            nameText.text = $"{fighterName}{title}\n<size=60%>{l.name}</size>";
            nameText.rectTransform.sizeDelta = new Vector2(4f, 1.2f);
        }
    }

    /// <summary>A sniper who just got a kill finds a new spot.</summary>
    public void MovePerch() => perchUntil = 0f;

    public override void Blind(float seconds)
    {
        blindUntil = Mathf.Max(blindUntil, Time.time + seconds);
        seesTarget = false;
        tracking = 0f;
        spotting = reactionTime * 2f;
        burstLeft = 0f;
        if (laser) laser.enabled = false;
    }

    GameObject Part(PrimitiveType shape, string name, Vector3 pos, Vector3 scale, Material m)
    {
        var p = GameObject.CreatePrimitive(shape);
        p.name = name;
        Destroy(p.GetComponent<Collider>());
        p.transform.SetParent(visual, false);
        p.transform.localPosition = pos;
        p.transform.localScale = scale;
        p.GetComponent<Renderer>().sharedMaterial = m;
        return p;
    }

    public override void Respawn(Vector3 position, Quaternion rotation)
    {
        if (NavMesh.SamplePosition(position, out var hit, 3f, NavMesh.AllAreas)) position = hit.position;
        base.Respawn(position, rotation);
        gameObject.SetActive(true);
        agent.enabled = true;
        if (agent.isOnNavMesh) agent.Warp(position);
        visual.localScale = Vector3.one;
        visual.localRotation = Quaternion.identity;
        visual.localPosition = Vector3.zero;
        target = null;
        seesTarget = false;
        lastKnownAt = -100f;
        ammo = magazine;
        reloadLeft = 0f;
        fireCooldown = Random.Range(0.3f, 0.8f);
        burstLeft = 0f;
        takingCover = false;
        spotFor = null;
        SetCrouch(false);
        spawnedAt = Time.time;
        frags = fragsPerLife;
        flashes = flashesPerLife;
        blindUntil = slideUntil = fleeUntil = 0f;
        slideEnding = false;
        hasPerch = false;
        flankFor = null;
        nextNade = Time.time + Random.Range(3f, 8f);
        if (laser) laser.enabled = false;
        watchDir = rotation * Vector3.forward;
    }

    // ------------------------------------------------------------------ every frame

    void Update()
    {
        if (PauseMenu.IsPaused) return;
        float dt = Time.deltaTime;
        if (nameTag && Camera.main) nameTag.rotation = Quaternion.LookRotation(nameTag.position - Camera.main.transform.position);
        Flash(dt);

        if (!Alive)
        {
            // De-rez: tip over and shrink away.
            float t = Mathf.Clamp01((Time.time - deathTime) / 0.6f);
            visual.localRotation = Quaternion.Euler(-80f * t, 0f, 0f);
            visual.localScale = Vector3.one * (1f - t * 0.9f);
            if (t >= 1f) gameObject.SetActive(false);
            return;
        }

        var arena = VRArena.Instance;
        bool fighting = arena && (arena.State == VRArena.Mode.Fighting || arena.State == VRArena.Mode.Practice);
        if (!fighting || !agent.isOnNavMesh) { if (agent.isOnNavMesh) agent.isStopped = true; if (laser) laser.enabled = false; return; }
        agent.isStopped = false;
        Regenerate(dt);

        if (dummy) { Wander(); Animate(dt); return; }

        think -= dt;
        if (think <= 0f)
        {
            think = Random.Range(0.1f, 0.16f);
            Perceive();
            Decide(arena);
        }
        if (slideEnding && !Sliding) { slideEnding = false; SetCrouch(false); }
        if (Sliding) agent.speed = SlideSpeed;
        Turn(dt);
        Shooting(dt);
        Laser();
        Unstick(dt);
        Animate(dt);
    }

    // ------------------------------------------------------------------ senses

    void Perceive()
    {
        if (Blinded) { seesTarget = false; return; }
        VRFighter best = null;
        float bestScore = float.MaxValue;
        foreach (var f in All)
        {
            if (!f || f.team == team || !f.Alive || !f.isActiveAndEnabled) continue;
            Vector3 to = f.AimPoint - EyePoint;
            float d = to.magnitude;
            if (d > ViewDistance) continue;
            bool lookingThatWay = Vector3.Angle(Flat(transform.forward), Flat(to)) < ViewAngle;
            bool remembered = f == target && Time.time - lastKnownAt < 1.2f;
            if (!lookingThatWay && d > 4f && !remembered) continue;
            if (!CanSee(f)) continue;
            float score = d * (f == target ? 0.6f : 1f) * (f.Health < f.maxHealth * 0.5f ? 0.85f : 1f) * (f is VRPlayer ? 0.75f : 1f);
            if (score < bestScore) { bestScore = score; best = f; }
        }

        if (best)
        {
            if (best != target || !seesTarget)
            {
                float d = Vector3.Distance(best.transform.position, transform.position);
                // Seen before a moment ago: quicker to react.
                bool wasExpecting = best == target && Time.time - lastKnownAt < 2f;
                spotting = reactionTime * (0.6f + d / 40f) * Random.Range(0.85f, 1.25f) * (wasExpecting ? 0.4f : 1f);
                tracking = 0f;
            }
            target = best;
            seesTarget = true;
            lastKnown = best.transform.position;
            lastKnownAt = Time.time;
        }
        else seesTarget = false;
    }

    /// <summary>Gunfire nearby (called by the arena). It turns that way and remembers where.</summary>
    public void Hear(VRFighter shooter, Vector3 at)
    {
        if (!Alive || seesTarget || !shooter || shooter.team == team) return;
        if (Vector3.Distance(at, transform.position) > Hearing) return;
        target = shooter;
        lastKnown = at;
        lastKnownAt = Time.time;
    }

    protected override void OnHurt(float amount, VRFighter from, bool headshot)
    {
        flash = 1f;
        if (!from || from.team == team) return;
        if (!seesTarget || target == null)
        {
            target = from;
            lastKnown = from.transform.position;
            lastKnownAt = Time.time;
        }
        // Getting shot at: duck out of the open sooner.
        if (Health < maxHealth * 0.5f && !takingCover) coverUntil = 0f;
    }

    // ------------------------------------------------------------------ decisions

    void Decide(VRArena arena)
    {
        if (target && (!target.Alive || !target.isActiveAndEnabled)) { target = null; seesTarget = false; }

        if (Blinded)
        {
            // Staggering about, can't see a thing.
            Doing = "blinded";
            SetCrouch(false);
            repath -= 0.13f;
            if (repath <= 0f)
            {
                repath = Random.Range(0.5f, 1f);
                var r = Random.insideUnitCircle * 2.5f;
                Go(transform.position + new Vector3(r.x, 0f, r.y), CrouchSpeed + 0.5f);
                watchDir = new Vector3(r.x, 0f, r.y);
            }
            return;
        }
        if (DodgeGrenade()) return;
        if (TryGrenade(arena)) return;
        MaybeSlide();

        bool hurt = Health < maxHealth * (0.35f + 0.2f * (1f - aggression));
        bool needCover = seesTarget && (hurt || Reloading) || takingCover;
        if (needCover && Cover(arena)) return;

        if (seesTarget) { Fight(arena); return; }

        // Hunting someone (you, on a streak): it knows roughly where you are and comes for you.
        if (Hunt && Hunt.Alive && Time.time < HuntUntil)
        {
            Doing = "hunting " + Hunt.fighterName;
            SetCrouch(false);
            lastKnown = Hunt.transform.position;
            lastKnownAt = Time.time - 1f;
            target = Hunt;
            Go(Hunt.transform.position, RunSpeed);
            watchDir = Hunt.transform.position - transform.position;
            return;
        }

        // Lost sight: check where they were (unless the objective's more urgent).
        bool recent = Time.time - lastKnownAt < 3.5f && Vector3.Distance(lastKnown, transform.position) < 32f;
        bool capturing = Objective && Objective.Contains(transform.position) && Objective.Owner != team;
        bool onJob = Objective && Objective.Owner != team && Vector3.Distance(transform.position, Objective.Position) < 35f;
        if (recent && target && !capturing && !onJob)
        {
            Doing = "checking";
            SetCrouch(false);
            Go(lastKnown, CombatSpeed + 1f);
            watchDir = lastKnown - transform.position;
            return;
        }
        PlayObjective(arena);
    }

    void Fight(VRArena arena)
    {
        Doing = "fighting " + target.fighterName;
        Vector3 to = Flat(target.transform.position - transform.position);
        float dist = to.magnitude;
        Vector3 dir = dist > 0.01f ? to / dist : transform.forward;
        watchDir = dir;

        if (Role == VRRole.Sniper)
        {
            // Snipers stay put and steady (crouched), unless someone gets close - then they back off to cover.
            if (dist < 10f && Cover(arena)) return;
            SetCrouch(dist > 12f);
            Go(transform.position, CombatSpeed);
            return;
        }
        // On the way to a point: don't stop for a long-range duel - keep pushing in and shoot on the move.
        bool advancing = Objective && Objective.Owner != team && !Objective.Contains(transform.position);
        if (advancing && Health > maxHealth * 0.45f)
        {
            Doing = $"pushing {Objective.letter} under fire";
            SetCrouch(false);
            if (spotFor != Objective) { spotFor = Objective; spotPickedAt = Time.time; objectiveSpot = Objective.RandomSpot(0.6f); }
            Vector3 pushTo = !flankDone && flankFor == Objective ? flankPoint : objectiveSpot;
            Go(pushTo, Role == VRRole.SMG ? RunSpeed : CombatSpeed + 1.4f);
            return;
        }
        // Rushers close the gap and fight up close.
        bool pointDuty = Objective && Objective.Owner != team;
        if (Role == VRRole.SMG && dist > 7f && Health > maxHealth * 0.4f
            && (!pointDuty || Vector3.Distance(target.transform.position, Objective.Position) < Objective.radius + 6f))
        {
            Doing = "rushing " + target.fighterName;
            SetCrouch(false);
            Go(target.transform.position, RunSpeed);
            return;
        }
        if (Role == VRRole.LMG && dist > 10f && Random.value < 0.12f)
        {
            holdUntil = Time.time + Random.Range(1.2f, 2.2f); // plant and spray
            SetCrouch(true);
        }

        // Steadying the aim: crouch and stand still for a moment (more often at range).
        if (Time.time < holdUntil) { agent.SetDestination(transform.position); return; }
        if (dist > 12f && Random.value < 0.06f + 0.04f * (1f - aggression))
        {
            holdUntil = Time.time + Random.Range(0.8f, 1.6f);
            SetCrouch(true);
            return;
        }
        SetCrouch(false);

        repath -= 0.13f;
        if (repath > 0f && agent.hasPath && agent.remainingDistance > 0.6f) return;
        repath = Random.Range(0.6f, 1.3f);

        Vector3 goal;
        bool defendRing = Objective && Objective.Owner == team && Objective.Inside[1 - team] > 0
                          && Vector3.Distance(transform.position, Objective.Position) < Objective.radius + 12f;
        if (Objective && (Objective.Owner != team && Objective.Contains(transform.position) || defendRing))
        {
            // Stay on the point while fighting - that's how it gets captured (or kept).
            goal = Objective.RandomSpot(0.7f);
        }
        else
        {
            float want = preferredDistance * Mathf.Lerp(1.2f, 0.6f, aggression);
            Vector3 side = Vector3.Cross(Vector3.up, dir) * (Random.value < 0.5f ? -1f : 1f);
            float radial = dist > want + 4f ? 1f : dist < want - 4f ? -1f : 0f;
            goal = transform.position + side * Random.Range(2f, 4.5f) + dir * radial * Random.Range(2f, 4f);
        }
        Go(goal, CombatSpeed);
    }

    /// <summary>Hurt or reloading in the open: get behind something, let health come back, then peek out.</summary>
    bool Cover(VRArena arena)
    {
        if (!takingCover)
        {
            if (Time.time < coverUntil) return false; // tried a moment ago and found nothing
            Vector3 threat = target ? target.EyePoint : lastKnown + Vector3.up * 1.5f;
            bool onPoint = Objective && Objective.Owner != team && Objective.Contains(transform.position);
            Vector3 near = onPoint ? Objective.Position : transform.position;
            float range = onPoint ? Objective.radius * 0.9f : 12f;
            if (!arena.FindCover(near, threat, range, out var spot))
            {
                coverUntil = Time.time + 2f;
                if (onPoint) { holdUntil = Time.time + 1f; SetCrouch(true); } // nowhere to hide on the point: get low, keep shooting
                return false;
            }
            takingCover = true;
            coverUntil = Time.time + Random.Range(2.5f, 4f);
            Go(spot, RunSpeed);
            Doing = "taking cover";
        }
        SetCrouch(agent.remainingDistance < 0.8f);
        if (lastKnownAt > 0f) watchDir = lastKnown - transform.position;
        bool healed = Health > maxHealth * 0.85f && !Reloading;
        if ((healed || Time.time > coverUntil) && !Reloading)
        {
            takingCover = false;
            coverUntil = Time.time + 1.5f;
            SetCrouch(false);
            return false;
        }
        return true;
    }

    void PlayObjective(VRArena arena)
    {
        if (Role == VRRole.Sniper && SniperPost(arena)) return;
        SetCrouch(false);
        var flag = Objective;
        if (flag && flag.Owner != team && Route(flag, arena)) return;
        if (!flag) { Doing = "roaming"; Wander(); return; }

        bool inside = flag.Contains(transform.position);
        if (flag.Owner != team)
        {
            // Capture: get in the ring and keep moving about inside it.
            Doing = (flag.Owner < 0 ? "capturing " : "attacking ") + flag.letter;
            if (spotFor != flag || Time.time - spotPickedAt > (inside ? Random.Range(1.5f, 3f) : 8f))
            {
                spotFor = flag;
                spotPickedAt = Time.time;
                objectiveSpot = flag.RandomSpot(inside ? 0.75f : 0.5f);
            }
            Go(objectiveSpot, inside ? CombatSpeed : RunSpeed);
            watchDir = inside ? arena.ThreatDirection(team, flag.Position) : (agent.velocity.sqrMagnitude > 0.5f ? agent.velocity : watchDir);
            return;
        }

        // Ours: hold it from cover near the ring, facing where the enemy will come from.
        Doing = "holding " + flag.letter;
        if (spotFor != flag || Time.time - spotPickedAt > Random.Range(7f, 11f))
        {
            spotFor = flag;
            spotPickedAt = Time.time;
            Vector3 threat = flag.Position + arena.ThreatDirection(team, flag.Position) * 20f + Vector3.up * 1.5f;
            objectiveSpot = arena.FindCover(flag.Position, threat, flag.radius + 5f, out var spot) ? spot : flag.RandomSpot(0.8f);
        }
        Go(objectiveSpot, Vector3.Distance(transform.position, objectiveSpot) > 8f ? RunSpeed : CombatSpeed);
        watchDir = arena.ThreatDirection(team, flag.Position);
        if (agent.remainingDistance < 0.6f && Random.value < 0.02f) SetCrouch(true);
    }

    /// <summary>Sniper: go to a perch with a long view of the middle, crouch, watch. New perch now and then.</summary>
    bool SniperPost(VRArena arena)
    {
        if (!hasPerch || Time.time > perchUntil)
        {
            if (arena.Perch(team, hasPerch ? perch : transform.position, out var spot))
            {
                perch = spot;
                hasPerch = true;
                perchUntil = Time.time + Random.Range(25f, 40f);
            }
        }
        if (!hasPerch) return false;
        float d = Vector3.Distance(transform.position, perch);
        Doing = d > 2f ? "moving to a sniper perch" : "sniping";
        Go(perch, d > 6f ? RunSpeed : CombatSpeed);
        var mid = arena.Flags.Count > 0 ? arena.Flags[arena.Flags.Count / 2].Position : transform.position + transform.forward;
        watchDir = d > 4f && agent.velocity.sqrMagnitude > 1f ? agent.velocity : mid - transform.position;
        SetCrouch(d < 1.2f);
        return true;
    }

    /// <summary>
    /// Attacking a point: go in along its lane (round the north or south street for the flankers, up the main
    /// street for the rest) to a staging spot short of the point. If the enemy holds or is on the point, wait
    /// there (a few seconds at most) for the squad, then storm it together - with a flashbang if it has one.
    /// </summary>
    bool Route(VRFlag flag, VRArena arena)
    {
        if (flankFor != flag || laneFor != Lane)
        {
            flankFor = flag;
            laneFor = Lane;
            flankDone = true;
            stageSince = -1f;
            Vector3 to = Flat(flag.Position - transform.position);
            float d = to.magnitude;
            if (d > 20f)
            {
                // The staging spot: on the lane, most of the way there (or 13 m short of it on the main street).
                Vector3 stage = Lane != 0
                    ? new Vector3(Mathf.Lerp(transform.position.x, flag.Position.x, 0.8f), flag.Position.y, arena.LaneZ(Lane))
                    : flag.Position - to / d * 13f;
                if (NavMesh.SamplePosition(stage, out var hit, 6f, NavMesh.AllAreas))
                {
                    flankPoint = hit.position;
                    flankDone = false;
                }
            }
        }
        if (flankDone) return false;

        if (Vector3.Distance(transform.position, flankPoint) > 3.5f)
        {
            Doing = (Lane > 0 ? "flanking north to " : Lane < 0 ? "flanking south to " : "moving up to ") + flag.letter;
            Go(flankPoint, RunSpeed);
            watchDir = agent.velocity.sqrMagnitude > 1f ? agent.velocity : flag.Position - transform.position;
            return true;
        }

        // At the staging spot.
        bool hot = flag.Owner == 1 - team || flag.Inside[1 - team] > 0;
        if (hot)
        {
            if (stageSince < 0f) stageSince = Time.time;
            if (Time.time - stageSince < 2.5f && !arena.SquadReady(this, flag))
            {
                Doing = $"waiting for the squad to push {flag.letter}";
                SetCrouch(true);
                Go(transform.position, CrouchSpeed);
                watchDir = flag.Position - transform.position;
                return true;
            }
            if (flashes > 0 && Vector3.Distance(transform.position, flag.Position) < 30f) Throw(VRGrenade.Kind.Flash, flag.Position + Vector3.up * 0.4f);
        }
        flankDone = true;
        SetCrouch(false);
        return false;
    }

    void MaybeSlide()
    {
        if (Sliding || crouched || Time.time < slideReady) return;
        if (Role != VRRole.SMG && Role != VRRole.Assault) return;
        if (agent.velocity.magnitude < runSpeed * 0.85f) return;
        bool urgent = seesTarget || Time.time - lastKnownAt < 3f || (Objective && Vector3.Distance(transform.position, Objective.Position) < 25f);
        if (!urgent || Random.value > (Role == VRRole.SMG ? 0.12f : 0.05f)) return;
        slideUntil = Time.time + 0.6f;
        slideReady = Time.time + Random.Range(2.5f, 5f);
        slideEnding = true;
        SetCrouch(true);
    }

    /// <summary>A live enemy frag close by: get away from it.</summary>
    bool DodgeGrenade()
    {
        if (Time.time < fleeUntil) return true;
        foreach (var g in VRGrenade.Live)
        {
            if (!g || g.kind != VRGrenade.Kind.Frag || g.team == team || g.FuseLeft > 2.05f) continue;
            Vector3 away = Flat(transform.position - g.Position);
            float d = away.magnitude;
            if (d > 6f) continue;
            away = d > 0.1f ? away / d : Flat(Random.insideUnitSphere).normalized;
            Go(transform.position + away * 7f, RunSpeed);
            watchDir = away;
            fleeUntil = Time.time + 1.1f;
            takingCover = false;
            SetCrouch(false);
            Doing = "dodging a grenade";
            return true;
        }
        return false;
    }

    /// <summary>Frag someone hiding behind cover, frag a crowded point, or flash a point before pushing it.</summary>
    bool TryGrenade(VRArena arena)
    {
        if (Time.time < nextNade || Time.time - spawnedAt < 2f) return false;
        var flag = Objective;
        if (frags > 0 && !seesTarget && target && Time.time - lastKnownAt < 3f)
        {
            float d = Vector3.Distance(transform.position, lastKnown);
            if (d > 7f && d < 26f && Random.value < 0.35f) { Throw(VRGrenade.Kind.Frag, lastKnown); return true; }
        }
        if (flag && flag.Owner != team && flag.Inside[1 - team] > 0)
        {
            float d = Vector3.Distance(transform.position, flag.Position);
            if (d > 8f && d < 24f)
            {
                if (flashes > 0 && Random.value < 0.3f) { Throw(VRGrenade.Kind.Flash, flag.Position + Vector3.up * 0.4f); return true; }
                if (frags > 0 && flag.Inside[1 - team] >= 2 && Random.value < 0.25f) { Throw(VRGrenade.Kind.Frag, flag.Position); return true; }
            }
        }
        return false;
    }

    void Throw(VRGrenade.Kind kind, Vector3 at)
    {
        if (kind == VRGrenade.Kind.Frag) frags--; else flashes--;
        nextNade = Time.time + Random.Range(6f, 11f);
        Vector3 from = EyePoint + Flat(at - transform.position).normalized * 0.45f + Vector3.up * 0.1f;
        var miss = Random.insideUnitCircle * 1.3f;
        Vector3 aim = at + new Vector3(miss.x, 0f, miss.y);
        float d = Vector3.Distance(from, aim);
        VRGrenade.Throw(kind, this, from, VRGrenade.Lob(from, aim, Mathf.Clamp(d / 13f, 0.6f, 1.5f)));
        watchDir = at - transform.position;
        holdUntil = Time.time + 0.35f;
        recoil = 1.5f;
        Doing = kind == VRGrenade.Kind.Frag ? "throwing a frag" : "throwing a flash";
        var hud = VRArena.Instance ? VRArena.Instance.Hud : null;
        if (hud && team == 0 && Random.value < 0.6f)
            hud.Chat(fighterName, kind == VRGrenade.Kind.Frag ? "frag out!" : $"flashing {(Objective ? Objective.letter : "them")}, push!");
    }

    /// <summary>A sniper's laser sight on its target (it wobbles until the aim settles) - your warning.</summary>
    void Laser()
    {
        if (!laser) return;
        bool on = Role == VRRole.Sniper && seesTarget && target && !Blinded && !Reloading && spotting <= reactionTime * 0.5f;
        laser.enabled = on;
        if (!on) return;
        Vector3 from = gun ? gun.position + gun.forward * gun.localScale.z * 0.5f : EyePoint;
        float wobble = Mathf.Clamp01(1f - tracking / Mathf.Max(0.1f, aimSettle)) * 0.8f;
        Vector3 jitter = new Vector3(Mathf.PerlinNoise(Time.time * 2f, 1f) - 0.5f, Mathf.PerlinNoise(2f, Time.time * 2f) - 0.5f, 0f) * wobble * 2f;
        laser.SetPosition(0, from);
        laser.SetPosition(1, target.AimPoint + Vector3.up * 0.25f + jitter);
    }

    void Wander()
    {
        repath -= 0.13f;
        if (repath > 0f) return;
        repath = Random.Range(1.5f, 3f);
        var goal = transform.position + new Vector3(Random.Range(-6f, 6f), 0f, Random.Range(-6f, 6f));
        Go(goal, dummy ? 1.6f : CombatSpeed);
        watchDir = goal - transform.position;
    }

    void Go(Vector3 goal, float speed)
    {
        agent.speed = Sliding ? SlideSpeed : crouched ? CrouchSpeed : speed;
        if (NavMesh.SamplePosition(goal, out var hit, 2.5f, NavMesh.AllAreas)) goal = hit.position;
        if (!agent.hasPath || Vector3.Distance(agent.destination, goal) > 0.4f) agent.SetDestination(goal);
    }

    /// <summary>Stuck against something? Pick somewhere else.</summary>
    void Unstick(float dt)
    {
        bool shouldMove = agent.hasPath && agent.remainingDistance > 1f && !crouched;
        stuckFor = shouldMove && agent.velocity.sqrMagnitude < 0.1f ? stuckFor + dt : 0f;
        if (stuckFor < 1.5f) return;
        stuckFor = 0f;
        spotFor = null;
        takingCover = false;
        var r = Random.insideUnitCircle * 4f;
        Go(transform.position + new Vector3(r.x, 0f, r.y), CombatSpeed);
    }

    // ------------------------------------------------------------------ aiming and shooting

    bool Reloading => reloadLeft > 0f;

    void Turn(float dt)
    {
        Vector3 look = seesTarget && target ? target.AimPoint - EyePoint : watchDir;
        if (!seesTarget && agent.velocity.sqrMagnitude > 4f && Time.time - lastKnownAt > 2f && !(Objective && Objective.Contains(transform.position)))
            look = agent.velocity; // running somewhere: look where it's going
        look.y = 0f;
        if (look.sqrMagnitude < 0.001f) return;
        float speed = seesTarget ? 420f : 260f;
        transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(look), speed * dt);
    }

    void Shooting(float dt)
    {
        fireCooldown -= dt;
        if (Reloading)
        {
            reloadLeft -= dt;
            if (reloadLeft <= 0f) ammo = magazine;
            return;
        }
        if (Blinded) return;
        if (!seesTarget || !target) { if (ammo < magazine * 0.4f && Time.time - lastKnownAt > 2f) Reload(); return; }
        tracking += dt;
        spotting -= dt;
        if (spotting > 0f || fireCooldown > 0f) return;
        if (Role == VRRole.Sniper && tracking < aimSettle * 0.8f) return; // waits for a steady shot
        Vector3 to = target.AimPoint - EyePoint;
        if (Vector3.Angle(Flat(transform.forward), Flat(to)) > 14f) return; // still turning
        if (FriendInTheWay(to)) { fireCooldown = 0.25f; return; }

        Shoot();
        ammo--;
        if (ammo <= 0) { Reload(); return; }
        if (Role == VRRole.Sniper) { fireCooldown = Random.Range(1.6f, 2.4f); tracking *= 0.6f; return; }
        if (burstLeft <= 0f) burstLeft = Role == VRRole.LMG ? Random.Range(8, 14) : Role == VRRole.SMG ? Random.Range(6, 11) : Random.Range(4, 8);
        burstLeft--;
        fireCooldown = burstLeft > 0f ? 1f / fireRate : Random.Range(0.2f, 0.45f);
    }

    void Reload()
    {
        if (Reloading || ammo >= magazine) return;
        reloadLeft = reloadTime;
        burstLeft = 0f;
    }

    bool FriendInTheWay(Vector3 to)
    {
        foreach (var hit in Physics.RaycastAll(EyePoint, to.normalized, to.magnitude, ~0, QueryTriggerInteraction.Ignore))
        {
            var f = hit.collider.GetComponentInParent<VRFighter>();
            if (f && f != this && f.team == team && f.Alive) return true;
        }
        return false;
    }

    void Shoot()
    {
        Vector3 from = gun ? gun.position + gun.forward * 0.4f : EyePoint;
        float dist = Vector3.Distance(from, target.AimPoint);
        // Miss more: before the aim settles, while moving, at a moving target, at range. Less when crouched.
        float settle = 1f + 1.1f * Mathf.Clamp01(1f - tracking / Mathf.Max(0.1f, aimSettle));
        float moving = agent.velocity.magnitude > 1f ? 1.15f : 1f;
        float targetSpeed = target is VRPlayer p ? p.SpeedAmount * 7f : (target is VRBot b && b.agent ? b.agent.velocity.magnitude * 0.6f : 0f);
        float range = 1f + Mathf.Max(0f, dist - 25f) / 50f;
        float angle = spread * settle * moving * (1f + targetSpeed / 16f) * range * (crouched ? 0.8f : 1f);
        if (Role == VRRole.Sniper && dist < 10f) angle *= 5f;
        // Misses spread less the further away the target is (a degree is a lot at range): keep it a sensible size.
        angle = Mathf.Min(angle, Mathf.Atan2(1.6f, Mathf.Max(1f, dist)) * Mathf.Rad2Deg + spread * 0.5f);

        bool settled = tracking > aimSettle;
        float height = settled && Random.value < 0.3f ? Random.Range(0.42f, 0.55f) : Random.Range(-0.2f, 0.35f); // the head, sometimes
        Vector3 aim = target.AimPoint + Vector3.up * height - from;
        Vector2 miss = Random.insideUnitCircle * Mathf.Tan(angle * Mathf.Deg2Rad);
        Quaternion basis = Quaternion.LookRotation(aim);
        Vector3 dir = (basis * new Vector3(miss.x, miss.y * 0.7f, 1f)).normalized;
        recoil = 1f;
        if (VRArena.Instance) VRArena.Instance.FireShot(this, from, dir, damage, teamColor);
    }

    // ------------------------------------------------------------------ looks

    void SetCrouch(bool on)
    {
        if (crouched == on) return;
        crouched = on;
        body.height = on ? 1.2f : 1.7f;
        body.center = new Vector3(0f, on ? 0.6f : 0.85f, 0f);
        headCollider.transform.localPosition = new Vector3(0f, on ? 1.15f : 1.62f, 0f);
    }

    void Animate(float dt)
    {
        float speed = agent ? Flat(agent.velocity).magnitude : 0f;
        bob += dt * speed * 2.2f;
        float side = agent ? Vector3.Dot(agent.velocity, transform.right) : 0f;
        lean = Mathf.Lerp(lean, Mathf.Clamp(-side * 2.5f, -10f, 10f), dt * 8f);
        recoil = Mathf.MoveTowards(recoil, 0f, dt * 8f);
        float squat = crouched ? 0.7f : 1f;
        visual.localScale = Vector3.Lerp(visual.localScale, new Vector3(1f, squat, 1f), dt * 12f);
        visual.localPosition = new Vector3(0f, Mathf.Abs(Mathf.Sin(bob)) * 0.06f * Mathf.Clamp01(speed / 3f), 0f);
        visual.localRotation = Quaternion.Euler(Sliding ? -18f : Mathf.Clamp(speed * 1.5f, 0f, 8f) - recoil * 3f, 0f, lean);
        if (gun) gun.localPosition = new Vector3(0.28f, 1.1f, 0.45f - recoil * 0.06f);
    }

    protected override void OnDied(VRFighter killer)
    {
        deathTime = Time.time;
        if (laser) laser.enabled = false;
        if (agent.isOnNavMesh) agent.isStopped = true;
        agent.enabled = false;
        SetCrouch(false);
        if (VRArena.Instance) VRArena.Instance.Burst(AimPoint, teamColor);
    }

    void Flash(float dt)
    {
        if (flash <= 0f) return;
        flash = Mathf.Max(0f, flash - dt * 6f);
        if (!flashMaterial) flashMaterial = VRArena.Glow(Color.white, Color.white, 2f);
        foreach (var r in bodyRenderers)
            if (r && (r.name == "Body" || r.name == "Head")) r.sharedMaterial = flash > 0.01f ? flashMaterial : bodyMaterial;
    }

    static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }
}
