using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// The family cat. Wanders around, gets the zoomies, watches you, and every now and then causes
/// trouble:
///   - Steals things (the TV remote - especially while a TV is blaring static - or the dirty clothes):
///     sneaks up low, wiggles its butt, pounces, bats the thing around, then runs off with it in
///     its mouth and drops it somewhere else. Things up on furniture get knocked onto the floor first.
///   - Naps on the dirty clothes pile so you can't grab it until you shoo it away.
/// The cat only ever stands on the house's real floors (never on furniture, outside or under the house).
/// Press E on the cat to pet it (calms you down), take back what it stole, or shoo it.
///
/// The model only has idle / walk / run animations; crouching, wiggling, pouncing, napping,
/// purring and looking at you are layered on top in code.
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
public class CatBrain : MonoBehaviour, IInteractable, IPromptProvider
{
    [Header("Cat")]
    public string catName = "Kitty";
    public float walkSpeed = 1f;
    public float runSpeed = 3.6f;
    public float wanderRadius = 6f;

    [Header("Mischief")]
    public float firstMischiefDelay = 25f;
    [Tooltip("Random wait (seconds) between bits of trouble.")]
    public Vector2 timeBetweenMischief = new Vector2(35f, 70f);
    [Tooltip("Things the cat likes to run off with.")]
    public List<ItemType> stealable = new List<ItemType> { ItemType.Remote, ItemType.DirtyClothes };
    public Vector2 napLength = new Vector2(25f, 40f);
    public float stressWhenStealing = 3f;

    [Header("Petting")]
    public float petStressRelief = 4f;
    public float petCooldown = 15f;
    [Range(0f, 1f)] public float notInTheMoodChance = 0.2f;

    [Header("Bones (found automatically if empty)")]
    public Transform bodyBone;
    public Transform headBone;
    public Transform mouthBone;

    [Header("House (found automatically if empty)")]
    [Tooltip("The house root. The cat only stands on floors that belong to it.")]
    public Transform house;
    [Tooltip("The furniture root. The cat never stands on top of furniture.")]
    public Transform furniture;

    [Header("Floating words")]
    public TMP_FontAsset font;

    [Header("Test keys (editor and development builds only)")]
    public bool debugKeys = true;
    [Tooltip("Send the cat to nap on the dirty clothes right now.")]
    public KeyCode debugNapKey = KeyCode.RightBracket;
    [Tooltip("Make the cat go steal the TV remote right now.")]
    public KeyCode debugStealKey = KeyCode.LeftBracket;

    enum Mood { Idle, Walking, Stalking, Playing, Running, Napping, Purring }

    static readonly int VertParam = Animator.StringToHash("Vert");
    static readonly int StateParam = Animator.StringToHash("State");
    static readonly Color SleepyBlue = new Color(0.35f, 0.45f, 0.95f);
    static readonly RaycastHit[] probeHits = new RaycastHit[16];
    static readonly HitOrder byDistance = new HitOrder();

    Mood mood;
    NavMeshPath testPath;
    BoneTweak bodyTweak, headTweak;
    Vector3 lastGoodPosition;
    bool hasGoodPosition;
    float nextSafetyCheck, traceUntil, nextTrace;
    NavMeshAgent agent;
    Animator animator;
    Transform player;
    Carryable[] items;
    Carryable carried;
    Carryable napOn;
    NavMeshPath path;
    bool ready;
    float nextMischief, nextPet, nextWord;
    float crouch, crouchTarget, loaf, loafTarget, hop, wiggle, purr, headWeight;

    // ------------------------------------------------------------------ setup

    void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        animator = GetComponentInChildren<Animator>();
        if (animator) animator.applyRootMotion = false;
        path = new NavMeshPath();
        testPath = new NavMeshPath();

        if (!bodyBone) bodyBone = FindBone("Root");
        if (!headBone) headBone = FindBone("spine.008") ?? FindBone("spine.007");
        if (!mouthBone) mouthBone = FindBone("jaw") ?? headBone;

        // The model comes with keyboard-controlled movement scripts. The cat moves itself now.
        foreach (var mb in GetComponents<MonoBehaviour>())
            if (mb != this && mb.GetType().Namespace == "Controller") mb.enabled = false;
    }

    void Start()
    {
        var movement = FindObjectOfType<PlayerMovementAdvanced>();
        if (movement) player = movement.transform;
        items = FindObjectsOfType<Carryable>(true);
        if (!house)
        {
            var h = GameObject.Find("HouseTryTwo");
            if (h) house = h.transform;
        }
        if (!furniture && house)
        {
            foreach (var t in house.GetComponentsInChildren<Transform>(true))
                if (t.name == "Furniature" || t.name == "Furniture") { furniture = t; break; }
        }

        // Stand on the walk map before switching the agent on (the walk map loads in OnEnable).
        if (NavMesh.SamplePosition(transform.position, out var hit, 3f, NavMesh.AllAreas))
            transform.position = hit.position;
        agent.enabled = true;
        if (!agent.isOnNavMesh)
        {
            Debug.LogWarning("[Cat] The cat isn't on a walk map. Run G7l6 > Bake Cat Walk Map.");
            enabled = false;
            return;
        }

        agent.speed = walkSpeed;
        agent.angularSpeed = 420f;
        agent.acceleration = 10f;
        ready = true;
        nextMischief = Time.time + firstMischiefDelay;
        StartCoroutine(Life());
    }

    void OnDisable()
    {
        if (napOn) { napOn.BlockedBy = null; napOn = null; }
        if (carried) DropCarried();
    }

    // ------------------------------------------------------------------ every frame

    void Update()
    {
        if (!ready) return;
        float dt = Time.deltaTime;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (debugKeys && !(PlayerInteraction.Instance && PlayerInteraction.Instance.InMainTask))
        {
            if (Input.GetKeyDown(debugNapKey)) DebugNap();
            if (Input.GetKeyDown(debugStealKey)) DebugSteal(ItemType.Remote);
        }
#endif

        if (carried && carried.TakenBy != this) carried = null; // someone else took it (a task reset it)

        SafetyCheck();
        Trace();

        if (animator)
        {
            float speed = agent.enabled ? agent.velocity.magnitude : 0f;
            animator.SetFloat(VertParam, Mathf.Clamp01(speed / (walkSpeed * 0.6f)), 0.12f, dt);
            animator.SetFloat(StateParam, Mathf.InverseLerp(walkSpeed * 1.1f, runSpeed, speed), 0.12f, dt);
            float animSpeed = mood == Mood.Napping ? 0.25f : mood == Mood.Stalking ? 0.6f : mood == Mood.Purring ? 0.5f : 1f;
            animator.speed = Mathf.MoveTowards(animator.speed, animSpeed, dt * 2f);
        }

        crouch = Mathf.MoveTowards(crouch, crouchTarget, dt * 4f);
        loaf = Mathf.MoveTowards(loaf, loafTarget, dt * 1.5f);
        purr = Mathf.MoveTowards(purr, mood == Mood.Purring ? 1f : 0f, dt * 3f);

        if (Time.time > nextWord)
        {
            if (mood == Mood.Napping) { Say("z", SleepyBlue, 1.2f); nextWord = Time.time + Random.Range(1f, 1.6f); }
            else if (mood == Mood.Purring) { Say("purr", HudArt.MarkerRed, 1.1f); nextWord = Time.time + 0.9f; }
        }
    }

    /// <summary>Runs after the animation, so these tweaks sit on top of idle/walk/run.</summary>
    void LateUpdate()
    {
        if (!ready) return;

        float s = Size;
        if (bodyBone)
        {
            bodyTweak.Begin(bodyBone);
            bodyBone.position += (Vector3.down * (crouch * 0.07f + loaf * 0.12f) + Vector3.up * hop) * s;
            if (Mathf.Abs(wiggle) > 0.01f)
                bodyBone.rotation = Quaternion.AngleAxis(wiggle, transform.up) * bodyBone.rotation;
            if (purr > 0.01f)
                bodyBone.position += Random.insideUnitSphere * 0.003f * purr * s;
            bodyTweak.End(bodyBone);
        }

        if (headBone)
        {
            headTweak.Begin(headBone);
            // Turn the head to watch the player when they're close.
            bool look = player && (mood == Mood.Idle || mood == Mood.Walking || mood == Mood.Purring)
                        && (player.position - transform.position).sqrMagnitude < 25f;
            headWeight = Mathf.MoveTowards(headWeight, look ? 1f : 0f, Time.deltaTime * 2.5f);
            if (headWeight > 0.001f)
            {
                Vector3 eyes = Camera.main ? Camera.main.transform.position : player.position + Vector3.up * 1.5f;
                Quaternion turn = Quaternion.FromToRotation(transform.forward, (eyes - headBone.position).normalized);
                float angle = Quaternion.Angle(Quaternion.identity, turn);
                if (angle > 75f) turn = Quaternion.Slerp(Quaternion.identity, turn, 75f / angle);
                headBone.rotation = Quaternion.Slerp(Quaternion.identity, turn, headWeight * 0.8f) * headBone.rotation;
            }
            if (loaf > 0.01f) // tuck the head down when napping
                headBone.rotation = Quaternion.AngleAxis(loaf * 25f, transform.right) * headBone.rotation;
            headTweak.End(headBone);
        }

        if (carried && mouthBone)
        {
            carried.transform.position = mouthBone.position + (transform.forward * 0.05f + Vector3.down * 0.03f) * s;
            carried.transform.rotation = transform.rotation * Quaternion.Euler(0f, 90f, 0f);
        }
    }

    // ------------------------------------------------------------------ player interaction

    public string Prompt
    {
        get
        {
            if (!ready) return null;
            if (mood == Mood.Napping) return "Shoo " + catName;
            if (carried) return $"Take the {carried.displayName} back";
            return Time.time >= nextPet ? "Pet " + catName : null;
        }
    }

    public void Interact()
    {
        if (!ready) return;

        if (mood == Mood.Napping)
        {
            GameHUD.Toast($"{catName} grumbles and slinks off.", GameHUD.Info);
            React(RunFrom(PlayerPos(), 5f));
            return;
        }
        if (carried)
        {
            var item = carried;
            DropCarried();
            Say("!", HudArt.MarkerRed, 1.6f);
            GameHUD.Toast($"Got the {item.displayName} back!", GameHUD.Good);
            React(RunFrom(PlayerPos(), 6f));
            return;
        }
        if (Time.time >= nextPet) React(Petted());
    }

    // ------------------------------------------------------------------ test keys

    /// <summary>Test: go steal an item right now (default key: [ for the TV remote).</summary>
    public void DebugSteal(ItemType type)
    {
        if (!ready) return;
        var item = FindItem(type);
        if (!item) { DebugSay($"No {type} in the scene"); return; }
        if (item == carried) { DebugSay($"{catName} already has the {item.displayName}"); return; }
        if (item.IsHeld) { DebugSay($"Drop the {item.displayName} first (G)"); return; }
        if (!item.gameObject.activeInHierarchy) { DebugSay($"The {item.displayName} is hidden right now"); return; }

        if (carried) DropCarried();
        item.BlockedBy = null;
        BringCloseTo(item.transform.position);
        DebugSay($"{catName} is going for the {item.displayName}");
        React(Steal(item));
    }

    /// <summary>Test: go nap on the dirty clothes right now (default key: ]). Starts the laundry task if the clothes aren't out.</summary>
    public void DebugNap()
    {
        if (!ready) return;
        var clothes = FindItem(ItemType.DirtyClothes);
        if (!clothes) { DebugSay("No dirty clothes in the scene"); return; }
        if (clothes.IsHeld) { DebugSay("Drop the dirty clothes first (G)"); return; }

        if (carried) DropCarried();
        if (!clothes.gameObject.activeInHierarchy)
        {
            // The pile only shows up during the laundry task - start it so there's something to nap on.
            var laundry = FindObjectOfType<LaundryTask>();
            if (laundry && !laundry.IsActive && GameManager.Instance) GameManager.Instance.StartTask(laundry);
            if (!clothes.gameObject.activeInHierarchy) clothes.ReturnHome(true);
        }

        BringCloseTo(clothes.transform.position);
        DebugSay($"{catName} is heading for a nap");
        React(NapOn(clothes));
    }

    Carryable FindItem(ItemType type)
    {
        foreach (var item in items)
            if (item && item.type == type) return item;
        return null;
    }

    /// <summary>If the cat can't walk to a spot (e.g. it's on another floor), pop it onto the floor nearby so you can watch.</summary>
    void BringCloseTo(Vector3 target)
    {
        if (FindFloorSpot(target, 1.4f, true, out _)) return;              // she can walk there herself
        if (!FindFloorSpot(target, 1.4f, false, out Vector3 goal)) return;  // nowhere to stand near it

        for (int i = 0; i < 20; i++)
        {
            Vector2 r = Random.insideUnitCircle.normalized * Random.Range(1.5f, 3f);
            if (NavMesh.SamplePosition(goal + new Vector3(r.x, 0f, r.y), out var spot, 1f, NavMesh.AllAreas)
                && Mathf.Abs(spot.position.y - goal.y) < 0.5f
                && IsFloorSpot(spot.position)
                && Connected(spot.position, goal))
            {
                agent.Warp(spot.position);
                return;
            }
        }
        agent.Warp(goal);
    }

    void DebugSay(string message)
    {
        GameHUD.Toast("[TEST] " + message, GameHUD.Info);
        Debug.Log("[Cat test] " + message);
        traceUntil = Time.time + 15f; // log what the cat does for a bit (Console / Editor.log)
        nextTrace = 0f;
    }

    /// <summary>Twice a second after a test key: where the cat is and what it's doing (for tracking down bugs).</summary>
    void Trace()
    {
        if (Time.time > traceUntil || Time.time < nextTrace) return;
        nextTrace = Time.time + 0.5f;
        Vector3 p = transform.position;
        string body = bodyBone ? $"body {bodyBone.position.y - p.y:+0.00;-0.00}m local {bodyBone.localPosition}" : "no body bone";
        Debug.Log($"[Cat trace] {Time.time:0.0}s {mood} at {p} navmesh:{(agent.enabled && agent.isOnNavMesh)} " +
                  $"agentPos {(agent.enabled ? agent.nextPosition.ToString() : "-")} {body} floorBelow:{Physics.Raycast(p + Vector3.up * 0.3f, Vector3.down, 50f, ~0, QueryTriggerInteraction.Ignore)} " +
                  $"anim:{(animator ? animator.enabled + "/" + animator.speed.ToString("0.00") : "-")} carrying:{(carried ? carried.name : "-")}");
    }

    /// <summary>
    /// If the cat ever ends up with nothing under it (below the house), put it back where it last
    /// stood on a real floor.
    /// </summary>
    void SafetyCheck()
    {
        if (Time.time < nextSafetyCheck) return;
        nextSafetyCheck = Time.time + 0.5f;

        Vector3 p = transform.position;
        bool somethingBelow = Physics.Raycast(p + Vector3.up * 0.3f, Vector3.down, 50f, ~0, QueryTriggerInteraction.Ignore);
        if (somethingBelow && agent.enabled && agent.isOnNavMesh && IsFloorSpot(p))
        {
            lastGoodPosition = p;
            hasGoodPosition = true;
        }
        else if (!somethingBelow && hasGoodPosition)
        {
            Debug.LogWarning($"[Cat] Ended up under the house at {p} - putting it back at {lastGoodPosition}.");
            if (agent.enabled) agent.Warp(lastGoodPosition);
            else transform.position = lastGoodPosition;
        }
    }

    // ------------------------------------------------------------------ behaviour

    IEnumerator Life()
    {
        while (true)
        {
            if (Time.time >= nextMischief)
            {
                nextMischief = Time.time + Random.Range(timeBetweenMischief.x, timeBetweenMischief.y);
                var trouble = PickMischief();
                if (trouble != null) { yield return trouble; continue; }
            }

            float r = Random.value;
            if (r < 0.1f) yield return Zoomies();
            else if (r < 0.6f) yield return Wander();
            else yield return Idle(Random.Range(3f, 7f));
        }
    }

    IEnumerator PickMischief()
    {
        bool tvBlaring = false;
        foreach (var tv in FindObjectsOfType<TvTask>())
            if (tv.IsActive) tvBlaring = true;

        Carryable best = null;
        float bestScore = 0f;
        bool nap = false;
        foreach (var item in items)
        {
            if (!Available(item) || !FindFloorSpot(item.transform.position, 1.4f, true, out _)) continue;

            if (stealable.Contains(item.type))
            {
                float score = (item.type == ItemType.Remote && tvBlaring ? 6f : 1f) * Random.Range(0.5f, 1.5f);
                if (score > bestScore) { bestScore = score; best = item; nap = false; }
            }
            if (item.type == ItemType.DirtyClothes)
            {
                float score = 2.5f * Random.Range(0.5f, 1.5f);
                if (score > bestScore) { bestScore = score; best = item; nap = true; }
            }
        }
        if (!best) return null;
        return nap ? NapOn(best) : Steal(best);
    }

    IEnumerator Steal(Carryable item)
    {
        // A spot on the floor right next to it (it might be up on a table).
        if (!FindFloorSpot(item.transform.position, 1.4f, true, out Vector3 spot)) yield break;
        bool onFurniture = IsUpHigh(item);

        // Sneak over...
        mood = Mood.Walking;
        yield return WalkTo(spot, walkSpeed, 0.9f + 0.5f * Size, 20f);
        if (!Available(item)) yield break;

        // ...stalk it, low and slow (right under it if it's on furniture, a pounce away if it's on the floor)...
        mood = Mood.Stalking;
        crouchTarget = 1f;
        yield return WalkTo(spot, walkSpeed * 0.35f, onFurniture ? 0.12f : 0.5f * Size, 6f);
        if (!Available(item)) { crouchTarget = 0f; yield break; }
        yield return Face(item.transform.position, 0.4f);

        // ...butt wiggle...
        for (float t = 0f; t < 0.8f; t += Time.deltaTime)
        {
            wiggle = Mathf.Sin(t * 38f) * 10f;
            yield return null;
        }
        wiggle = 0f;
        if (!Available(item)) { crouchTarget = 0f; yield break; }

        // ...POUNCE (or jump up at it if it's on furniture), then bat it around.
        crouchTarget = 0f;
        mood = Mood.Playing;
        bool upHigh = IsUpHigh(item);
        yield return upHigh ? Pounce(transform.position, 0.35f) : Pounce(item.transform.position, 0.18f);
        int swats = Random.Range(2, 4);
        for (int i = 0; i < swats; i++)
        {
            if (!Available(item)) yield break;
            if (upHigh && i == swats - 1) yield return KnockOff(item); // cats gonna cat
            else yield return Swat(item);
            yield return new WaitForSeconds(Random.Range(0.25f, 0.6f));
        }
        if (!Available(item)) yield break;

        // Step over to it if it landed a little away.
        if (Flat(item.transform.position - transform.position).magnitude > 0.5f * Size)
            yield return WalkTo(item.transform.position, walkSpeed, 0.3f * Size, 3f);
        if (!Available(item)) yield break;

        // Grab it and run.
        item.Take(this, 0.22f * Size);
        carried = item;
        if (StressManager.Instance) StressManager.Instance.AddStress(stressWhenStealing);
        GameHUD.Toast($"{catName} ran off with the {item.displayName}!", GameHUD.Warn);
        Say("!", HudArt.MarkerRed, 1.6f);

        mood = Mood.Running;
        yield return WalkTo(FarPoint(item.transform.position, 7f), runSpeed, 0.4f, 12f);

        // Drop it and act innocent.
        DropCarried();
        yield return Idle(Random.Range(2f, 4f));
    }

    IEnumerator NapOn(Carryable clothes)
    {
        if (!FindFloorSpot(clothes.transform.position, 1.4f, true, out Vector3 spot)) yield break;

        mood = Mood.Walking;
        yield return WalkTo(spot, walkSpeed, 0.2f, 25f);
        if (!Available(clothes)) yield break;

        // Pile's up on furniture? Knock it down first - a nap is a nap.
        if (IsUpHigh(clothes))
        {
            yield return Face(clothes.transform.position, 0.3f);
            mood = Mood.Playing;
            yield return Pounce(transform.position, 0.35f);
            yield return KnockOff(clothes);
            if (!Available(clothes)) yield break;
        }

        // Climb onto the pile.
        mood = Mood.Walking;
        yield return WalkTo(clothes.transform.position, walkSpeed * 0.6f, 0.05f, 4f);
        if (!Available(clothes)) yield break;

        // Turn around once, then flop down.
        yield return Face(transform.position + Quaternion.Euler(0f, 160f, 0f) * transform.forward, 0.6f);
        mood = Mood.Napping;
        loafTarget = 1f;
        napOn = clothes;
        clothes.BlockedBy = $"{catName} is napping on them - shoo the cat first";
        GameHUD.Toast($"{catName} is napping on the dirty clothes...", GameHUD.Warn);

        float wakeAt = Time.time + Random.Range(napLength.x, napLength.y);
        while (Time.time < wakeAt && clothes && clothes.gameObject.activeInHierarchy && !clothes.IsHeld)
            yield return null;

        if (napOn) { napOn.BlockedBy = null; napOn = null; }
        loafTarget = 0f;
        yield return Idle(1.5f);
    }

    IEnumerator Petted()
    {
        mood = Mood.Idle;
        if (player) yield return Face(player.position, 0.35f);

        if (Random.value < notInTheMoodChance)
        {
            Say("hss!", HudArt.MarkerRed, 1.4f);
            GameHUD.Toast($"{catName} isn't in the mood.", GameHUD.Info);
            nextPet = Time.time + petCooldown * 0.5f;
            yield return RunFrom(PlayerPos(), 4f);
            yield break;
        }

        mood = Mood.Purring;
        nextWord = 0f;
        nextPet = Time.time + petCooldown;
        if (StressManager.Instance) StressManager.Instance.RemoveStress(petStressRelief);
        GameHUD.Toast($"{catName} purrs. You feel a bit calmer.", GameHUD.Good);
        yield return new WaitForSeconds(3f);
        mood = Mood.Idle;
    }

    IEnumerator Zoomies()
    {
        mood = Mood.Running;
        int dashes = Random.Range(3, 6);
        for (int i = 0; i < dashes; i++)
            yield return WalkTo(RandomPoint(7f), runSpeed, 0.5f, 4f);
        yield return Idle(Random.Range(2f, 3f));
    }

    IEnumerator Wander()
    {
        mood = Mood.Walking;
        yield return WalkTo(RandomPoint(wanderRadius), walkSpeed, 0.3f, 12f);
    }

    IEnumerator Idle(float seconds)
    {
        mood = Mood.Idle;
        if (agent.isOnNavMesh) agent.ResetPath();
        yield return new WaitForSeconds(seconds);
    }

    IEnumerator RunFrom(Vector3 from, float distance)
    {
        mood = Mood.Running;
        Vector3 away = transform.position - from;
        away.y = 0f;
        if (away.sqrMagnitude < 0.01f) away = -transform.forward;
        Vector3 target = transform.position + away.normalized * distance;
        if (!NavMesh.SamplePosition(target, out var hit, 3f, NavMesh.AllAreas) || !SameFloor(hit.position)
            || !IsFloorSpot(hit.position) || !CanReach(hit.position))
            target = FarPoint(from, distance);
        else
            target = hit.position;
        yield return WalkTo(target, runSpeed, 0.4f, 5f);
        mood = Mood.Idle;
    }

    // ------------------------------------------------------------------ moves

    IEnumerator WalkTo(Vector3 destination, float speed, float stopDistance, float timeout)
    {
        if (!agent.isOnNavMesh) yield break;
        if (NavMesh.SamplePosition(destination, out var hit, 1.5f, NavMesh.AllAreas)) destination = hit.position;

        agent.speed = speed;
        agent.stoppingDistance = stopDistance;
        agent.isStopped = false;
        agent.SetDestination(destination);

        float giveUp = Time.time + timeout;
        while (Time.time < giveUp)
        {
            if (!agent.pathPending && agent.remainingDistance <= stopDistance + 0.05f) break;
            yield return null;
        }
        if (agent.isOnNavMesh) agent.ResetPath();
    }

    IEnumerator Face(Vector3 target, float duration)
    {
        Vector3 dir = target - transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f) yield break;

        agent.updateRotation = false;
        Quaternion from = transform.rotation, to = Quaternion.LookRotation(dir);
        for (float t = 0f; t < duration; t += Time.deltaTime)
        {
            transform.rotation = Quaternion.Slerp(from, to, t / duration);
            yield return null;
        }
        transform.rotation = to;
        agent.updateRotation = true;
    }

    /// <summary>A hop toward the target (or straight up when the target is where the cat stands).</summary>
    IEnumerator Pounce(Vector3 target, float height)
    {
        if (agent.isOnNavMesh) agent.ResetPath();
        agent.updateRotation = false;
        Vector3 dir = Flat(target - transform.position);
        float distance = dir.magnitude < 0.05f ? 0f : Mathf.Clamp(dir.magnitude - 0.2f, 0.1f, 0.6f);
        dir = dir.sqrMagnitude > 0.0001f ? dir.normalized : transform.forward;

        const float time = 0.3f;
        for (float t = 0f; t < time; t += Time.deltaTime)
        {
            hop = Mathf.Sin(t / time * Mathf.PI) * height; // scaled by Size in LateUpdate
            if (distance > 0f) agent.Move(dir * (distance / time) * Time.deltaTime);
            yield return null;
        }
        hop = 0f;
        agent.updateRotation = true;
    }

    /// <summary>Swipes something off a table/shelf so it tumbles onto the floor in front of the cat.</summary>
    IEnumerator KnockOff(Carryable item)
    {
        Vector3 start = item.transform.position;
        Vector3 land = transform.position + transform.forward * 0.35f * Size;
        if (!NavMesh.SamplePosition(land, out var hit, 1f, NavMesh.AllAreas) || !IsFloorSpot(hit.position) || !SameFloor(hit.position))
            land = transform.position;
        else
            land = hit.position;

        crouchTarget = 0f;
        Quaternion startRotation = item.transform.rotation;
        Vector3 spinAxis = Random.onUnitSphere;
        const float time = 0.45f;
        for (float t = 0f; t < time; t += Time.deltaTime)
        {
            if (!item || item.IsHeld || item.TakenBy) yield break;
            float k = t / time;
            item.transform.position = Vector3.Lerp(start, land, k * k) + Vector3.up * Mathf.Sin(k * Mathf.PI) * 0.2f * Size;
            item.transform.rotation = Quaternion.AngleAxis(k * 420f, spinAxis) * startRotation;
            yield return null;
        }
        if (!item || item.IsHeld || item.TakenBy) yield break;
        item.PlaceAt(land, Random.Range(0f, 360f));
        FloatingWord.Spawn("THUD", land + Vector3.up * 0.3f * Size, HudArt.Ink, font, 1.3f, 1.2f);
    }

    /// <summary>A quick paw swipe: the cat dips forward and the item skitters away a little.</summary>
    IEnumerator Swat(Carryable item)
    {
        Vector3 start = item.transform.position;
        Vector3 away = start - transform.position;
        away.y = 0f;
        away = away.sqrMagnitude > 0.0001f ? away.normalized : transform.forward;
        Vector3 side = Vector3.Cross(Vector3.up, away) * Random.Range(-0.6f, 0.6f);
        Vector3 end = start + (away + side).normalized * Random.Range(0.06f, 0.14f);

        crouchTarget = 0.6f;
        for (float t = 0f; t < 0.22f; t += Time.deltaTime)
        {
            if (!Available(item)) { crouchTarget = 0f; yield break; }
            float k = t / 0.22f;
            item.transform.position = Vector3.Lerp(start, end, k) + Vector3.up * Mathf.Sin(k * Mathf.PI) * 0.1f;
            yield return null;
        }
        if (Available(item)) item.transform.position = end;
        crouchTarget = 0.2f;
        yield return Face(end, 0.15f);
    }

    // ------------------------------------------------------------------ helpers

    void React(IEnumerator reaction)
    {
        StopAllCoroutines();
        ResetPose();
        StartCoroutine(ThenCarryOn(reaction));
    }

    IEnumerator ThenCarryOn(IEnumerator first)
    {
        yield return first;
        yield return Life();
    }

    void ResetPose()
    {
        crouchTarget = 0f;
        loafTarget = 0f;
        wiggle = 0f;
        hop = 0f;
        if (napOn) { napOn.BlockedBy = null; napOn = null; }
        if (agent.isOnNavMesh) { agent.isStopped = false; agent.ResetPath(); }
        agent.updateRotation = true;
        agent.speed = walkSpeed;
        mood = Mood.Idle;
    }

    void DropCarried()
    {
        if (!carried) return;
        var item = carried;
        carried = null;
        Vector3 spot = transform.position + transform.forward * 0.3f * Size;
        if (NavMesh.SamplePosition(spot, out var hit, 1f, NavMesh.AllAreas) && IsFloorSpot(hit.position) && SameFloor(hit.position))
            spot = hit.position;
        else
            spot = transform.position;
        item.PlaceAt(spot, transform.eulerAngles.y);
    }

    bool Available(Carryable item) =>
        item && item.gameObject.activeInHierarchy && !item.IsHeld && !item.TakenBy && string.IsNullOrEmpty(item.BlockedBy);

    bool CanReach(Vector3 position)
    {
        if (!agent.isOnNavMesh || !NavMesh.SamplePosition(position, out var hit, 1.2f, NavMesh.AllAreas)) return false;
        return agent.CalculatePath(hit.position, path) && path.status == NavMeshPathStatus.PathComplete;
    }

    /// <summary>A random floor spot on the cat's current floor.</summary>
    Vector3 RandomPoint(float radius)
    {
        for (int i = 0; i < 12; i++)
        {
            Vector2 r = Random.insideUnitCircle * radius;
            if (NavMesh.SamplePosition(transform.position + new Vector3(r.x, 0f, r.y), out var hit, 1.5f, NavMesh.AllAreas)
                && SameFloor(hit.position) && IsFloorSpot(hit.position) && CanReach(hit.position))
                return hit.position;
        }
        return transform.position;
    }

    /// <summary>A floor spot at least minDistance from a point (and not right next to the player).</summary>
    Vector3 FarPoint(Vector3 from, float minDistance)
    {
        for (int i = 0; i < 25; i++)
        {
            Vector2 r = Random.insideUnitCircle * (minDistance + 5f);
            if (!NavMesh.SamplePosition(transform.position + new Vector3(r.x, 0f, r.y), out var hit, 2f, NavMesh.AllAreas)) continue;
            if (!SameFloor(hit.position) || Vector3.Distance(hit.position, from) < minDistance) continue;
            if (player && Vector3.Distance(hit.position, player.position) < 3f) continue;
            if (IsFloorSpot(hit.position) && CanReach(hit.position)) return hit.position;
        }
        return RandomPoint(wanderRadius);
    }

    // ------------------------------------------------------------------ where the cat may stand

    /// <summary>
    /// True if the point is on one of the house's real floors: not on top of furniture (tables,
    /// shelves, beds), not outside the house and not under it.
    /// </summary>
    bool IsFloorSpot(Vector3 point)
    {
        if (!house) return true;
        int count = Physics.RaycastNonAlloc(point + Vector3.up * 0.3f, Vector3.down, probeHits, 2.5f, ~0, QueryTriggerInteraction.Ignore);
        System.Array.Sort(probeHits, 0, count, byDistance);
        for (int i = 0; i < count; i++)
        {
            var col = probeHits[i].collider;
            var t = col.transform;
            if (t.IsChildOf(transform)) continue;                          // the cat itself
            if (furniture && t.IsChildOf(furniture)) continue;             // look through rugs/tables to the real floor
            if (col.GetComponentInParent<Carryable>() || col.GetComponentInParent<PlayerMovementAdvanced>()) continue;

            // First real surface below: it must be part of the house, right under the point.
            return t.IsChildOf(house) && point.y - probeHits[i].point.y < 0.35f;
        }
        return false;
    }

    /// <summary>The closest good floor spot next to a target (which may be up on furniture).</summary>
    bool FindFloorSpot(Vector3 around, float maxRadius, bool mustReach, out Vector3 spot)
    {
        spot = around;
        float best = float.MaxValue;
        bool found = false;
        float[] rings = { 0f, 0.35f, 0.7f, 1.05f, 1.4f, 2f };
        foreach (float r in rings)
        {
            if (r > maxRadius + 0.01f) break;
            int steps = r == 0f ? 1 : 8;
            for (int i = 0; i < steps; i++)
            {
                float a = i * Mathf.PI * 2f / steps;
                Vector3 probe = around + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
                if (!NavMesh.SamplePosition(probe, out var hit, 1.8f, NavMesh.AllAreas)) continue;
                float below = around.y - hit.position.y;
                if (below < -0.4f || below > 1.8f) continue;               // same storey as the target
                if (!IsFloorSpot(hit.position)) continue;
                if (mustReach && !CanReach(hit.position)) continue;
                float d = Flat(hit.position - around).magnitude;
                if (d < best) { best = d; spot = hit.position; found = true; }
            }
            if (found) return true;
        }
        return false;
    }

    bool Connected(Vector3 a, Vector3 b) =>
        NavMesh.CalculatePath(a, b, NavMesh.AllAreas, testPath) && testPath.status == NavMeshPathStatus.PathComplete;

    bool SameFloor(Vector3 point) => Mathf.Abs(point.y - transform.position.y) < 1f;

    /// <summary>Is the item sitting up on something (table, shelf) rather than on the floor by the cat?</summary>
    bool IsUpHigh(Carryable item)
    {
        float bottom = item.transform.position.y;
        bool any = false;
        foreach (var r in item.GetComponentsInChildren<Renderer>())
        {
            bottom = any ? Mathf.Min(bottom, r.bounds.min.y) : r.bounds.min.y;
            any = true;
        }
        return bottom - transform.position.y > 0.3f;
    }

    static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

    class HitOrder : IComparer<RaycastHit>
    {
        public int Compare(RaycastHit a, RaycastHit b) => a.distance.CompareTo(b.distance);
    }

    /// <summary>
    /// Keeps a code tweak on a bone (crouch, head turn...) from piling up frame after frame.
    /// Some bones (like this cat's "Root") aren't moved by the animations, so a tweak added on top
    /// would otherwise stack every frame - that's what sank the cat through the floor. If the bone
    /// still holds exactly what we wrote last frame, the animation didn't touch it: put it back first.
    /// </summary>
    struct BoneTweak
    {
        Vector3 basePosition, writtenPosition, restPosition;
        Quaternion baseRotation, writtenRotation;
        bool written, hasRest;

        public void Begin(Transform bone)
        {
            if (written && bone.localPosition == writtenPosition && bone.localRotation == writtenRotation)
            {
                bone.localPosition = basePosition;
                bone.localRotation = baseRotation;
            }
            // Safety net: if the bone has wandered far from where it started, snap it back.
            if (!hasRest) { restPosition = bone.localPosition; hasRest = true; }
            else if ((bone.localPosition - restPosition).sqrMagnitude > 0.25f)
            {
                Debug.LogWarning($"[Cat] Bone '{bone.name}' drifted to {bone.localPosition} - snapping it back.");
                bone.localPosition = restPosition;
            }
            basePosition = bone.localPosition;
            baseRotation = bone.localRotation;
        }

        public void End(Transform bone)
        {
            writtenPosition = bone.localPosition;
            writtenRotation = bone.localRotation;
            written = true;
        }
    }

    Vector3 PlayerPos() => player ? player.position : transform.position - transform.forward;

    /// <summary>How much the cat is scaled up, so the made-up moves (crouch, pounce, mouth) scale with it.</summary>
    float Size => Mathf.Max(0.01f, transform.lossyScale.y);

    void Say(string word, Color color, float size)
    {
        Vector3 at = (headBone ? headBone.position : transform.position + Vector3.up * 0.5f * Size) + Vector3.up * 0.25f * Size;
        FloatingWord.Spawn(word, at, color, font, size);
    }

    Transform FindBone(string boneName)
    {
        foreach (var t in GetComponentsInChildren<Transform>(true))
            if (t.name == boneName) return t;
        return null;
    }
}
