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

    /// <summary>The capture point the team wants this bot at (handed out by the arena).</summary>
    public VRFlag Objective { get; set; }
    /// <summary>What it's doing, for debugging and team chat.</summary>
    public string Doing { get; private set; } = "";

    const float ViewAngle = 75f, ViewDistance = 75f, Hearing = 38f;
    const float RunSpeed = 6.2f, CombatSpeed = 4f, CrouchSpeed = 2f;

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
        if (!fighting || !agent.isOnNavMesh) { if (agent.isOnNavMesh) agent.isStopped = true; return; }
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
        Turn(dt);
        Shooting(dt);
        Unstick(dt);
        Animate(dt);
    }

    // ------------------------------------------------------------------ senses

    void Perceive()
    {
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
            float score = d * (f == target ? 0.6f : 1f) * (f.Health < f.maxHealth * 0.5f ? 0.85f : 1f);
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

        bool hurt = Health < maxHealth * (0.35f + 0.2f * (1f - aggression));
        bool needCover = seesTarget && (hurt || Reloading) || takingCover;
        if (needCover && Cover(arena)) return;

        if (seesTarget) { Fight(arena); return; }

        // Lost sight: check where they were (unless the objective's more urgent).
        bool recent = Time.time - lastKnownAt < 3.5f && Vector3.Distance(lastKnown, transform.position) < 32f;
        bool capturing = Objective && Objective.Contains(transform.position) && Objective.Owner != team;
        if (recent && target && !capturing)
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
        if (Objective && Objective.Contains(transform.position) && Objective.Owner != team)
        {
            // Stay on the point while fighting - that's how it gets captured.
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
            if (!arena.FindCover(transform.position, threat, 12f, out var spot)) { coverUntil = Time.time + 2f; return false; }
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
        SetCrouch(false);
        var flag = Objective;
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
        agent.speed = crouched ? CrouchSpeed : speed;
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
        if (!seesTarget || !target) { if (ammo < magazine * 0.4f && Time.time - lastKnownAt > 2f) Reload(); return; }
        tracking += dt;
        spotting -= dt;
        if (spotting > 0f || fireCooldown > 0f) return;
        Vector3 to = target.AimPoint - EyePoint;
        if (Vector3.Angle(Flat(transform.forward), Flat(to)) > 14f) return; // still turning
        if (FriendInTheWay(to)) { fireCooldown = 0.25f; return; }

        Shoot();
        ammo--;
        if (ammo <= 0) { Reload(); return; }
        if (burstLeft <= 0f) burstLeft = Random.Range(3, 7);
        burstLeft--;
        fireCooldown = burstLeft > 0f ? 1f / fireRate : Random.Range(0.35f, 0.8f);
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
        // Misses spread less the further away the target is (a degree is a lot at range): keep it a sensible size.
        angle = Mathf.Min(angle, Mathf.Atan2(1.6f, Mathf.Max(1f, dist)) * Mathf.Rad2Deg + spread * 0.5f);

        Vector3 aim = target.AimPoint + Vector3.up * Random.Range(-0.2f, 0.4f) - from;
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
        visual.localRotation = Quaternion.Euler(Mathf.Clamp(speed * 1.5f, 0f, 8f) - recoil * 3f, 0f, lean);
        if (gun) gun.localPosition = new Vector3(0.28f, 1.1f, 0.45f - recoil * 0.06f);
    }

    protected override void OnDied(VRFighter killer)
    {
        deathTime = Time.time;
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
