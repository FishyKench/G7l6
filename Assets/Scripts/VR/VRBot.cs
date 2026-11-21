using TMPro;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// A bot in the VR arena (a teammate or an enemy). Picks the nearest enemy it can see, keeps a fighting
/// distance while strafing between shots, chases when it loses sight, and fires short bursts that miss by up
/// to 'spread' degrees. Practice dummies just drift about and never shoot.
/// Built entirely from primitives - it's a VR avatar, it's allowed to look like one.
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
public class VRBot : VRFighter
{
    [Header("Fighting")]
    public float fireRate = 3f;
    public float damage = 8f;
    [Tooltip("Degrees a shot can miss by.")]
    public float spread = 6f;
    public float reactionTime = 0.45f;
    public float preferredDistance = 14f;
    public bool dummy;

    NavMeshAgent agent;
    VRFighter target;
    float fireCooldown, reaction, retarget, repath, burstLeft, deathTime;
    Vector3 strafeGoal;
    Renderer[] bodyRenderers;
    Color teamColor;
    Transform visual, nameTag, gun;
    Material flashMaterial, bodyMaterial;
    float flash;

    public override Vector3 AimPoint => transform.position + Vector3.up * 1.1f;
    public override Vector3 EyePoint => transform.position + Vector3.up * 1.55f;

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
        agent.speed = 4.6f;
        agent.acceleration = 30f;
        agent.angularSpeed = 0f; // we turn it ourselves (to face the target while strafing)
        agent.updateRotation = false;
        agent.stoppingDistance = 0.3f;
        agent.obstacleAvoidanceType = ObstacleAvoidanceType.LowQualityObstacleAvoidance;
        agent.enabled = false; // switched on at its spawn point (on the walk map)

        var body = gameObject.AddComponent<CapsuleCollider>();
        body.center = new Vector3(0f, 0.85f, 0f);
        body.height = 1.7f;
        body.radius = 0.38f;

        visual = new GameObject("Visual").transform;
        visual.SetParent(transform, false);
        bodyMaterial = VRArena.Glow(teamColor * 0.55f, teamColor, 0.9f);
        var dark = VRArena.Solid(new Color(0.06f, 0.06f, 0.09f), 0.8f);
        Part(PrimitiveType.Capsule, "Body", new Vector3(0f, 0.8f, 0f), new Vector3(0.72f, 0.8f, 0.6f), bodyMaterial);
        var head = Part(PrimitiveType.Sphere, "Head", new Vector3(0f, 1.62f, 0f), Vector3.one * 0.46f, bodyMaterial);
        Part(PrimitiveType.Cube, "Visor", new Vector3(0f, 1.65f, 0.17f), new Vector3(0.38f, 0.14f, 0.16f), VRArena.Glow(Color.black, Color.white, 1.4f));
        gun = Part(PrimitiveType.Cube, "Gun", new Vector3(0.28f, 1.1f, 0.45f), new Vector3(0.09f, 0.12f, 0.7f), dark).transform;
        // Head hitbox (headshots do double damage).
        var headCol = new GameObject("Head").AddComponent<SphereCollider>();
        headCol.transform.SetParent(transform, false);
        headCol.transform.localPosition = head.transform.localPosition;
        headCol.radius = 0.25f;
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
        target = null;
        reaction = reactionTime;
        fireCooldown = Random.Range(0.3f, 0.9f);
        burstLeft = 0f;
        strafeGoal = transform.position;
    }

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

        if (dummy) { Wander(dt); return; }

        retarget -= dt;
        if (target == null || !target.Alive || retarget <= 0f)
        {
            var old = target;
            target = PickTarget();
            retarget = Random.Range(1f, 2f);
            if (target != old) reaction = reactionTime * Random.Range(0.8f, 1.4f);
        }
        if (target == null) { Wander(dt); return; }

        bool sees = CanSee(target);
        Vector3 to = target.transform.position - transform.position;
        to.y = 0f;
        float dist = to.magnitude;
        Vector3 dir = dist > 0.01f ? to / dist : transform.forward;

        repath -= dt;
        if (!sees)
        {
            if (repath <= 0f) { agent.SetDestination(target.transform.position); repath = 0.5f; }
        }
        else if (repath <= 0f || Vector3.Distance(transform.position, strafeGoal) < 0.6f)
        {
            // Strafe sideways, drifting to keep a fighting distance.
            Vector3 side = Vector3.Cross(Vector3.up, dir) * (Random.value < 0.5f ? -1f : 1f);
            float radial = dist > preferredDistance + 3f ? 1f : dist < preferredDistance - 3f ? -1f : 0f;
            Vector3 goal = transform.position + side * Random.Range(2f, 4.5f) + dir * radial * 3f;
            if (NavMesh.SamplePosition(goal, out var hit, 2f, NavMesh.AllAreas)) strafeGoal = hit.position;
            agent.SetDestination(strafeGoal);
            repath = Random.Range(0.7f, 1.5f);
        }

        // Face the target while moving.
        Vector3 look = sees ? dir : (agent.velocity.sqrMagnitude > 0.1f ? agent.velocity.normalized : transform.forward);
        look.y = 0f;
        if (look.sqrMagnitude > 0.001f)
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(look), 540f * dt);

        if (sees) reaction -= dt;
        fireCooldown -= dt;
        if (sees && reaction <= 0f && fireCooldown <= 0f)
        {
            Shoot();
            if (burstLeft <= 0f) burstLeft = Random.Range(2, 5);
            burstLeft--;
            fireCooldown = burstLeft > 0f ? 1f / fireRate * 0.6f : Random.Range(0.6f, 1.2f) / fireRate * 2f;
        }
    }

    VRFighter PickTarget()
    {
        VRFighter best = null;
        float bestScore = float.MaxValue;
        foreach (var f in All)
        {
            if (f.team == team || !f.Alive || !f.isActiveAndEnabled) continue;
            float score = Vector3.Distance(f.transform.position, transform.position) * (CanSee(f) ? 1f : 2.5f);
            if (score < bestScore) { bestScore = score; best = f; }
        }
        return best;
    }

    void Wander(float dt)
    {
        repath -= dt;
        if (repath > 0f) return;
        repath = Random.Range(1.5f, 3f);
        var goal = transform.position + new Vector3(Random.Range(-4f, 4f), 0f, Random.Range(-4f, 4f));
        if (NavMesh.SamplePosition(goal, out var hit, 2f, NavMesh.AllAreas)) agent.SetDestination(hit.position);
        agent.speed = dummy ? 1.6f : 4.6f;
    }

    void Shoot()
    {
        Vector3 from = gun ? gun.position + gun.forward * 0.4f : EyePoint;
        Vector3 aim = (target.AimPoint + Vector3.up * Random.Range(-0.3f, 0.45f)) - from;
        Quaternion miss = Quaternion.AngleAxis(Random.Range(-spread, spread), Vector3.up) * Quaternion.AngleAxis(Random.Range(-spread, spread) * 0.5f, Vector3.right);
        Vector3 dir = miss * aim.normalized;
        if (VRArena.Instance) VRArena.Instance.FireShot(this, from, dir, damage, teamColor);
    }

    protected override void OnHurt(float amount, VRFighter from, bool headshot)
    {
        flash = 1f;
        if (from && from.Alive && from.team != team && (target == null || !CanSee(target))) target = from; // shoot back
    }

    protected override void OnDied(VRFighter killer)
    {
        deathTime = Time.time;
        if (agent.isOnNavMesh) agent.isStopped = true;
        agent.enabled = false;
        if (VRArena.Instance) VRArena.Instance.Burst(AimPoint, teamColor);
    }

    void Flash(float dt)
    {
        if (flash <= 0f) return;
        flash = Mathf.Max(0f, flash - dt * 6f);
        if (!flashMaterial) flashMaterial = VRArena.Glow(Color.white, Color.white, 2f);
        foreach (var r in bodyRenderers)
            if (r && r.name != "Visor" && r.name != "Gun") r.sharedMaterial = flash > 0.01f ? flashMaterial : bodyMaterial;
    }
}
