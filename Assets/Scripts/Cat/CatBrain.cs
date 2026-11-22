using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// The family cat.
///
/// Everyday life: wanders around, gets the zoomies, watches you, jumps up onto beds, sofas,
/// desks, tables and counters to sit for a while - and naps on the cozy ones.
///
/// Trouble (every 35-70 seconds):
///   - Steals the TV remote (much more likely while a TV is blaring static) or the dirty clothes:
///     sneaks up low, wiggles its butt, pounces, swats it around - sometimes right off the table -
///     then runs off with it in its mouth and drops it somewhere else.
///   - Naps on the dirty clothes pile so you can't grab it.
///   - Lies on your homework keyboard: you can't type, and it mashes keys.
/// Press E on the cat to pet it (calms you down), take back what it stole, or shoo it.
///
/// The model only has idle / walk / run animations; crouching, wiggling, pouncing, jumping,
/// napping, purring and looking at you are layered on top in code. Poses squash the body toward
/// the floor (instead of moving it down) so the legs never go through the floor.
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
public class CatBrain : MonoBehaviour, IInteractable, IPromptProvider
{
    public static CatBrain Instance { get; private set; }
    /// <summary>True while the cat is lying on the homework keyboard.</summary>
    public static bool OnKeyboard => Instance && Instance.mood == Mood.Keyboard;
    public static string Name => Instance ? Instance.catName : "The cat";

    [Header("Cat")]
    public string catName = "Kitty";
    public float walkSpeed = 1f;
    public float runSpeed = 3.6f;
    public float wanderRadius = 6f;

    [Header("Climbing")]
    [Tooltip("How often a quiet moment is spent up on furniture.")]
    [Range(0f, 1f)] public float perchChance = 0.3f;
    public Vector2 perchTime = new Vector2(8f, 20f);

    [Header("Mischief")]
    public float firstMischiefDelay = 25f;
    [Tooltip("Random wait (seconds) between bits of trouble.")]
    public Vector2 timeBetweenMischief = new Vector2(35f, 70f);
    [Tooltip("Things the cat likes to run off with.")]
    public List<ItemType> stealable = new List<ItemType> { ItemType.Remote, ItemType.DirtyClothes };
    public Vector2 napLength = new Vector2(25f, 40f);
    public float stressWhenStealing = 3f;
    public Vector2 keyboardTime = new Vector2(20f, 35f);
    [Tooltip("Seconds between key mashes while lying on the keyboard.")]
    public float keyMashInterval = 1.1f;

    [Header("Petting")]
    public float petStressRelief = 4f;
    public float petCooldown = 15f;
    [Range(0f, 1f)] public float notInTheMoodChance = 0.2f;

    [Header("Bones (found automatically if empty)")]
    public Transform bodyBone;
    public Transform headBone;
    public Transform mouthBone;

    [Header("House (found automatically if empty)")]
    [Tooltip("The house root. The cat only walks around on floors that belong to it.")]
    public Transform house;
    [Tooltip("The furniture root. Furniture tops are for perching, not wandering.")]
    public Transform furniture;

    [Header("Floating words")]
    public TMP_FontAsset font;

    [Header("Test keys (editor and development builds only)")]
    public bool debugKeys = true;
    [Tooltip("Steal the TV remote right now.")]
    public KeyCode debugStealKey = KeyCode.LeftBracket;
    [Tooltip("Nap on the dirty clothes right now (starts the laundry task if needed).")]
    public KeyCode debugNapKey = KeyCode.RightBracket;
    [Tooltip("Jump onto the nearest furniture right now.")]
    public KeyCode debugPerchKey = KeyCode.Semicolon;
    [Tooltip("Go lie on the homework keyboard right now.")]
    public KeyCode debugKeyboardKey = KeyCode.Quote;

    enum Mood { Idle, Walking, Stalking, Playing, Running, Napping, Purring, Perching, Keyboard }

    static readonly int VertParam = Animator.StringToHash("Vert");
    static readonly int StateParam = Animator.StringToHash("State");
    static readonly Color SleepyBlue = new Color(0.35f, 0.45f, 0.95f);
    static readonly RaycastHit[] probeHits = new RaycastHit[16];
    static readonly HitOrder byDistance = new HitOrder();

    // Body shapes (scale of the body around the feet): squashing keeps the feet on the floor.
    static readonly Vector3 CrouchShape = new Vector3(1.06f, 0.8f, 1.05f);
    static readonly Vector3 LoafShape = new Vector3(1.14f, 0.6f, 1.0f);
    static readonly Vector3 AnticipationShape = new Vector3(1.1f, 0.74f, 1.1f);
    static readonly Vector3 LeapShape = new Vector3(0.92f, 1.1f, 1.12f);
    static readonly Vector3 LandingShape = new Vector3(1.12f, 0.8f, 1.1f);

    Mood mood;
    NavMeshAgent agent;
    Animator animator;
    Transform player;
    Carryable[] items;
    Carryable carried;
    Carryable napOn;
    TypingTask keyboardOf;
    NavMeshPath path, testPath;
    bool ready;
    float nextMischief, nextPet, nextWord, nextKeyMash;

    // automatic cat feeder (ShopNest gadget): go and eat, then stay calm for a while
    float calmUntil;
    bool feedCall;
    Vector3 feedSpot, feedBowl;

    /// <summary>True while the cat is full and calm after eating (no mischief).</summary>
    public bool Calm => Time.time < calmUntil;

    /// <summary>The cat feeder dropped some food: go and eat at 'standSpot' (facing the bowl), then stay calm for 'calmSeconds'.</summary>
    public void CallToFeeder(Vector3 standSpot, Vector3 bowl, float calmSeconds)
    {
        feedSpot = standSpot;
        feedBowl = bowl;
        feedCall = true;
        calmUntil = Mathf.Max(calmUntil, Time.time + calmSeconds);
    }
    float crouch, crouchTarget, loaf, loafTarget, hop, wiggle, purr, headWeight;

    // jumping on/off furniture
    bool jumping;
    Vector3 jumpFrom, jumpTo, jumpShape = Vector3.one;
    float jumpClock, jumpDuration, jumpHeight, landClock;

    // pose bookkeeping and safety nets
    BoneTweak bodyTweak, headTweak;
    Vector3 bodyRestScale = Vector3.one;
    Vector3 lastGoodPosition;
    bool hasGoodPosition;
    float nextSafetyCheck, traceUntil, nextTrace;

    // ------------------------------------------------------------------ setup

    void Awake()
    {
        Instance = this;
        agent = GetComponent<NavMeshAgent>();
        animator = GetComponentInChildren<Animator>();
        if (animator) animator.applyRootMotion = false;
        path = new NavMeshPath();
        testPath = new NavMeshPath();

        if (!bodyBone) bodyBone = FindBone("Root");
        if (!headBone) headBone = FindBone("spine.008") ?? FindBone("spine.007");
        if (!mouthBone) mouthBone = FindBone("jaw") ?? headBone;
        if (bodyBone) bodyRestScale = bodyBone.localScale;

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
            var h = GameObject.Find("NewHouse") ?? GameObject.Find("HouseTryTwo");
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

        // Same size as the walk map was baked for (values are scaled by the cat's own scale).
        agent.radius = 0.13f;
        agent.height = 0.42f;
        agent.speed = walkSpeed;
        agent.angularSpeed = 420f;
        agent.acceleration = 10f;
        agent.autoTraverseOffMeshLink = false; // we animate the jumps ourselves
        ready = true;
        nextMischief = Time.time + firstMischiefDelay;
        StartCoroutine(Life());
    }

    void OnDisable()
    {
        if (napOn) { napOn.BlockedBy = null; napOn = null; }
        keyboardOf = null;
        if (carried) DropCarried();
        if (Instance == this) Instance = null;
    }

    // ------------------------------------------------------------------ every frame

    void Update()
    {
        if (!ready) return;
        float dt = Time.deltaTime;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (debugKeys && !(PlayerInteraction.Instance && PlayerInteraction.Instance.InMainTask))
        {
            if (Input.GetKeyDown(debugStealKey)) DebugSteal(ItemType.Remote);
            if (Input.GetKeyDown(debugNapKey)) DebugNap();
            if (Input.GetKeyDown(debugPerchKey)) DebugPerch();
            if (Input.GetKeyDown(debugKeyboardKey)) DebugKeyboard();
        }
#endif

        if (carried && carried.TakenBy != this) carried = null; // a task took it back

        UpdateJump(dt);
        SafetyCheck();
        Trace();

        if (animator)
        {
            float speed = agent.enabled ? agent.velocity.magnitude : 0f;
            float vert = jumping ? 1f : Mathf.Clamp01(speed / (walkSpeed * 0.6f));
            float state = jumping ? 0.7f : Mathf.InverseLerp(walkSpeed * 1.1f, runSpeed, speed);
            animator.SetFloat(VertParam, vert, 0.1f, dt);
            animator.SetFloat(StateParam, state, 0.1f, dt);
            float animSpeed = mood switch
            {
                Mood.Napping => 0.25f,
                Mood.Stalking => 0.6f,
                Mood.Purring => 0.5f,
                Mood.Perching => 0.7f,
                Mood.Keyboard => 0.6f,
                _ => 1f,
            };
            animator.speed = Mathf.MoveTowards(animator.speed, animSpeed, dt * 2f);
        }

        crouch = Mathf.MoveTowards(crouch, crouchTarget, dt * 4f);
        loaf = Mathf.MoveTowards(loaf, loafTarget, dt * 1.5f);
        purr = Mathf.MoveTowards(purr, mood == Mood.Purring ? 1f : 0f, dt * 1.2f);

        if (Time.time > nextWord)
        {
            if (mood == Mood.Napping) { Say("z", SleepyBlue, 1.2f); nextWord = Time.time + Random.Range(1f, 1.6f); }
            else if (mood == Mood.Purring) { Say("purr", HudArt.MarkerRed, 1.1f); nextWord = Time.time + 0.9f; }
            else if (mood == Mood.Keyboard) { Say(Random.value < 0.5f ? "tap" : "jkl;", HudArt.Ink, 1f); nextWord = Time.time + Random.Range(1f, 1.8f); }
        }

        if (mood == Mood.Keyboard && keyboardOf && Time.time >= nextKeyMash)
        {
            nextKeyMash = Time.time + keyMashInterval * Random.Range(0.7f, 1.3f);
            keyboardOf.CatMashesKeys();
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
            if (hop > 0f) bodyBone.position += Vector3.up * hop * s;
            if (Mathf.Abs(wiggle) > 0.01f)
            {
                // Only the back end sways: turn round a point up by the shoulders, so the head stays on target.
                Vector3 pivot = headBone ? headBone.position : bodyBone.position + transform.forward * 0.3f * s;
                pivot.y = bodyBone.position.y;
                var turn = Quaternion.AngleAxis(wiggle, transform.up);
                bodyBone.position = pivot + turn * (bodyBone.position - pivot);
                bodyBone.rotation = turn * bodyBone.rotation;
            }
            float breathe = 0f;
            if (purr > 0.01f)
            {
                // Content, slow breathing and a gentle lean side to side - no shaking.
                float tm = Time.time;
                breathe = Mathf.Sin(tm * 2.4f) * 0.035f * purr;
                bodyBone.rotation = Quaternion.AngleAxis(Mathf.Sin(tm * 0.9f) * 2.5f * purr, transform.forward) * bodyBone.rotation;
            }

            // Squash around the feet (the Root bone sits on the floor) - never through it.
            Vector3 shape = Vector3.Scale(Vector3.Lerp(Vector3.one, CrouchShape, crouch), Vector3.Lerp(Vector3.one, LoafShape, loaf));
            shape = Vector3.Scale(shape, jumpShape);
            shape.y *= 1f + breathe;
            shape.x *= 1f + breathe * 0.6f;
            bodyBone.localScale = Vector3.Scale(bodyRestScale, shape);
            bodyTweak.End(bodyBone);
        }

        if (headBone)
        {
            headTweak.Begin(headBone);
            // Turn the head to watch the player when they're close.
            bool look = player && !jumping && (mood == Mood.Idle || mood == Mood.Walking || mood == Mood.Purring || mood == Mood.Perching || mood == Mood.Keyboard)
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
            if (loaf > 0.01f && mood == Mood.Napping) // tuck the head down when napping
                headBone.rotation = Quaternion.AngleAxis(loaf * 25f, transform.right) * headBone.rotation;
            if (purr > 0.01f) // slow, happy head rub
                headBone.rotation = Quaternion.AngleAxis(Mathf.Sin(Time.time * 1.3f) * 9f * purr, transform.forward)
                                  * Quaternion.AngleAxis(8f * purr, transform.right) * headBone.rotation;
            headTweak.End(headBone);
        }

        if (carried && mouthBone)
        {
            carried.transform.position = mouthBone.position + (transform.forward * 0.05f + Vector3.down * 0.03f) * s;
            carried.transform.rotation = transform.rotation * Quaternion.Euler(0f, 90f, 0f);
        }
    }

    // ------------------------------------------------------------------ jumping (furniture links)

    void UpdateJump(float dt)
    {
        if (!jumping && agent.enabled && agent.isOnOffMeshLink) BeginJump();

        if (landClock > 0f)
        {
            landClock -= dt;
            jumpShape = Vector3.Lerp(Vector3.one, LandingShape, Mathf.Clamp01(landClock / 0.22f));
        }
        if (!jumping) { if (landClock <= 0f) jumpShape = Vector3.one; return; }

        jumpClock += dt;
        FaceInstant(jumpTo, dt * 12f);
        const float windUp = 0.14f;
        if (jumpClock < windUp)
        {
            jumpShape = Vector3.Lerp(Vector3.one, AnticipationShape, jumpClock / windUp); // crouch before the leap
            return;
        }

        float k = Mathf.Clamp01((jumpClock - windUp) / jumpDuration);
        transform.position = Vector3.Lerp(jumpFrom, jumpTo, k) + Vector3.up * (jumpHeight * 4f * k * (1f - k));
        jumpShape = Vector3.Lerp(LeapShape, Vector3.one, Mathf.Abs(k - 0.4f) * 1.6f); // stretch out mid-air

        if (k >= 1f)
        {
            jumping = false;
            transform.position = jumpTo;
            agent.CompleteOffMeshLink();
            landClock = 0.22f;
        }
    }

    void BeginJump()
    {
        var link = agent.currentOffMeshLinkData;
        jumping = true;
        jumpClock = 0f;
        jumpFrom = transform.position;
        jumpTo = link.endPos;
        float rise = jumpTo.y - jumpFrom.y;
        jumpHeight = rise > 0f ? rise * 0.6f + 0.15f * Size : 0.2f * Size;
        jumpDuration = 0.3f + 0.15f * Mathf.Abs(rise);
    }

    // ------------------------------------------------------------------ player interaction

    public string Prompt
    {
        get
        {
            if (!ready || jumping) return null;
            if (mood == Mood.Keyboard) return $"Shoo {catName} off the keyboard";
            if (mood == Mood.Napping && napOn) return "Shoo " + catName;
            if (carried) return $"Take the {carried.displayName} back";
            return Time.time >= nextPet ? "Pet " + catName : null;
        }
    }

    public void Interact()
    {
        if (!ready || jumping) return;

        if (mood == Mood.Keyboard)
        {
            GameHUD.Toast($"{catName} hops off the keyboard.", GameHUD.Info);
            React(RunFrom(PlayerPos(), 4f));
            return;
        }
        if (mood == Mood.Napping && napOn)
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
            var laundry = FindObjectOfType<LaundryTask>();
            if (laundry && !laundry.IsActive && GameManager.Instance) GameManager.Instance.StartTask(laundry);
            if (!clothes.gameObject.activeInHierarchy) clothes.ReturnHome(true);
        }

        BringCloseTo(clothes.transform.position);
        DebugSay($"{catName} is heading for a nap");
        React(NapOn(clothes));
    }

    /// <summary>Test: jump onto the closest furniture right now (default key: ;).</summary>
    public void DebugPerch()
    {
        if (!ready) return;
        if (!TryPickPerch(true, out var perch)) { DebugSay("No furniture to jump on nearby"); return; }
        DebugSay($"{catName} is jumping onto the {perch.name}");
        React(PerchOn(perch, false));
    }

    /// <summary>Test: go lie on the homework keyboard right now (default key: ').</summary>
    public void DebugKeyboard()
    {
        if (!ready) return;
        var homework = FindObjectOfType<TypingTask>();
        if (!homework || !homework.IsActive) { DebugSay("No homework to lie on right now"); return; }
        if (!DeskSpot(homework, false, out var spot)) { DebugSay("Can't find a spot on the desk"); return; }
        if (!CanReach(spot)) BringCloseTo(spot);
        DebugSay($"{catName} is heading for your keyboard");
        React(SitOnKeyboard(homework));
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
        string body = bodyBone ? $"body {bodyBone.position.y - p.y:+0.00;-0.00}m scale {bodyBone.localScale}" : "no body bone";
        Debug.Log($"[Cat trace] {Time.time:0.0}s {mood}{(jumping ? " (jumping)" : "")} at {p} navmesh:{(agent.enabled && agent.isOnNavMesh)} " +
                  $"{body} floorBelow:{Physics.Raycast(p + Vector3.up * 0.3f, Vector3.down, 50f, ~0, QueryTriggerInteraction.Ignore)} " +
                  $"carrying:{(carried ? carried.name : "-")}");
    }

    /// <summary>If the cat ever ends up with nothing under it (below the house), put it back where it last stood on a real floor.</summary>
    void SafetyCheck()
    {
        if (jumping || Time.time < nextSafetyCheck) return;
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
            if (feedCall)
            {
                feedCall = false;
                yield return EatAtFeeder();
                continue;
            }

            if (Time.time >= nextMischief)
            {
                if (Calm) nextMischief = calmUntil + Random.Range(4f, 12f); // full and sleepy: no trouble for now
                else
                {
                    nextMischief = Time.time + Random.Range(timeBetweenMischief.x, timeBetweenMischief.y);
                    var trouble = PickMischief();
                    if (trouble != null) { yield return trouble; continue; }
                }
            }

            float r = Random.value;
            if (r < 0.08f) yield return Zoomies();
            else if (r < 0.08f + perchChance && TryPickPerch(false, out var perch)) yield return PerchOn(perch, false);
            else if (r < 0.75f) yield return Wander();
            else yield return Idle(Random.Range(3f, 7f));
        }
    }

    IEnumerator PickMischief()
    {
        bool tvBlaring = false;
        foreach (var tv in FindObjectsOfType<TvTask>())
            if (tv.IsActive) tvBlaring = true;

        IEnumerator best = null;
        float bestScore = 0f;
        foreach (var item in items)
        {
            if (!Available(item) || !CanGetTo(item)) continue;
            if (item.type == ItemType.Remote && Shop.IsInstalled(Shop.RemoteHolder)) continue; // the remote holder keeps it safe
            if (stealable.Contains(item.type))
            {
                float score = (item.type == ItemType.Remote && tvBlaring ? 6f : 1f) * Random.Range(0.5f, 1.5f);
                if (score > bestScore) { bestScore = score; best = Steal(item); }
            }
            if (item.type == ItemType.DirtyClothes)
            {
                float score = 2.5f * Random.Range(0.5f, 1.5f);
                if (score > bestScore) { bestScore = score; best = NapOn(item); }
            }
        }

        var homework = FindObjectOfType<TypingTask>();
        if (homework && homework.IsActive && DeskSpot(homework, true, out _))
        {
            float score = 2.5f * Random.Range(0.5f, 1.5f);
            if (score > bestScore) { bestScore = score; best = SitOnKeyboard(homework); }
        }
        return best;
    }

    /// <summary>Trots over to the feeder's bowl and eats happily (the calm period runs from when the food dropped).</summary>
    IEnumerator EatAtFeeder()
    {
        mood = Mood.Walking;
        yield return WalkTo(feedSpot, walkSpeed * 1.6f, 0.1f, 25f);
        if (Flat(transform.position - feedSpot).magnitude > 0.7f * Size) yield break; // couldn't get there
        yield return Face(feedBowl, 0.4f);

        mood = Mood.Purring;
        crouchTarget = 0.7f; // head down in the bowl
        nextWord = 0f; // (it purrs while it eats)
        yield return new WaitForSeconds(9f);
        crouchTarget = 0f;
        mood = Mood.Idle;
        yield return Idle(Random.Range(3f, 5f));
    }

    IEnumerator Steal(Carryable item)
    {
        // Get right next to it - up on the furniture if there's a way up, otherwise underneath.
        bool fromBelow;
        if (SpotAt(item, out Vector3 spot)) fromBelow = false;
        else if (FindFloorSpot(item.transform.position, 1.4f, true, out spot)) fromBelow = true;
        else yield break;

        // Sneak over...
        mood = Mood.Walking;
        yield return WalkTo(spot, walkSpeed, 0.9f + 0.5f * Size, 25f);
        if (!Available(item)) yield break;

        // ...stalk it, low and slow...
        mood = Mood.Stalking;
        crouchTarget = 1f;
        yield return WalkTo(spot, walkSpeed * 0.35f, fromBelow ? 0.12f : 0.45f * Size, 6f);
        if (!Available(item)) { crouchTarget = 0f; yield break; }
        yield return Face(item.transform.position, 0.4f);

        // ...butt wiggle...
        const float wiggleTime = 1.3f;
        for (float t = 0f; t < wiggleTime; t += Time.deltaTime)
        {
            float build = Mathf.SmoothStep(0f, 1f, t / 0.5f) * Mathf.SmoothStep(1f, 0f, (t - wiggleTime + 0.3f) / 0.3f);
            wiggle = Mathf.Sin(t * 10f) * 7f * build;
            yield return null;
        }
        wiggle = 0f;
        yield return new WaitForSeconds(0.2f); // the still beat right before the pounce
        if (!Available(item)) { crouchTarget = 0f; yield break; }

        // ...POUNCE (or jump up at it from below), then bat it around.
        crouchTarget = 0f;
        mood = Mood.Playing;
        yield return fromBelow ? Pounce(transform.position, 0.35f) : Pounce(item.transform.position, 0.18f);

        // Up on furniture with it? Sometimes it goes over the edge. From below it always comes down.
        bool knockOff = fromBelow || (IsOnFurniture(item) && Random.value < 0.5f);
        int swats = Random.Range(2, 4);
        for (int i = 0; i < swats; i++)
        {
            if (!Available(item)) yield break;
            if (knockOff && i == swats - 1) yield return KnockOff(item, fromBelow);
            else yield return Swat(item);
            yield return new WaitForSeconds(Random.Range(0.25f, 0.6f));
        }
        if (!Available(item)) yield break;

        // Go get it if it ended up somewhere else (e.g. down on the floor).
        if (Flat(item.transform.position - transform.position).magnitude > 0.5f * Size || Mathf.Abs(item.transform.position.y - transform.position.y) > 0.4f)
        {
            mood = Mood.Walking;
            Vector3 target = SpotAt(item, out var near) ? near : item.transform.position;
            yield return WalkTo(target, walkSpeed * 1.4f, 0.3f * Size, 6f);
        }
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
        bool reachable = SpotAt(clothes, out Vector3 spot);
        if (!reachable && !FindFloorSpot(clothes.transform.position, 1.4f, true, out spot)) yield break;

        mood = Mood.Walking;
        yield return WalkTo(spot, walkSpeed, 0.2f, 25f);
        if (!Available(clothes)) yield break;

        // Can't get onto the pile? Knock it down first - a nap is a nap.
        if (!reachable && IsUpHigh(clothes))
        {
            yield return Face(clothes.transform.position, 0.3f);
            mood = Mood.Playing;
            yield return Pounce(transform.position, 0.35f);
            yield return KnockOff(clothes, true);
            if (!Available(clothes)) yield break;
        }

        // Climb onto the pile.
        mood = Mood.Walking;
        yield return WalkTo(clothes.transform.position, walkSpeed * 0.6f, 0.05f, 5f);
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

    /// <summary>Jump up onto furniture and sit there for a while - or curl up for a nap on beds and sofas.</summary>
    IEnumerator PerchOn(CatNavMesh.Perch perch, bool forceNap)
    {
        mood = Mood.Walking;
        yield return WalkTo(perch.top, walkSpeed, 0.15f, 25f);
        if (Flat(transform.position - perch.top).magnitude > 0.6f || Mathf.Abs(transform.position.y - perch.top.y) > 0.3f)
            yield break; // didn't make it up there

        yield return Face(transform.position + Quaternion.Euler(0f, Random.Range(120f, 240f), 0f) * transform.forward, 0.5f);
        bool nap = forceNap || (perch.cozy && Random.value < 0.6f);
        mood = nap ? Mood.Napping : Mood.Perching;
        loafTarget = nap ? 1f : 0.35f; // curled up, or just sitting

        float until = Time.time + (nap ? Random.Range(napLength.x, napLength.y) : Random.Range(perchTime.x, perchTime.y));
        while (Time.time < until) yield return null;

        loafTarget = 0f;
        yield return Idle(1f);
    }

    /// <summary>Lie down on the homework keyboard: no typing until she's shooed, and she mashes keys.</summary>
    IEnumerator SitOnKeyboard(TypingTask homework)
    {
        if (!DeskSpot(homework, true, out var spot)) yield break;

        mood = Mood.Walking;
        yield return WalkTo(spot, walkSpeed, 0.12f, 30f);
        if (!homework || !homework.IsActive || Flat(transform.position - spot).magnitude > 0.6f || Mathf.Abs(transform.position.y - spot.y) > 0.3f)
            yield break;

        if (player) yield return Face(player.position, 0.4f);
        mood = Mood.Keyboard;
        loafTarget = 0.6f;
        keyboardOf = homework;
        nextKeyMash = Time.time + 0.5f;
        GameHUD.Toast($"{catName} is lying on your keyboard!", GameHUD.Warn);

        float until = Time.time + Random.Range(keyboardTime.x, keyboardTime.y);
        while (Time.time < until && homework && homework.IsActive) yield return null;

        keyboardOf = null;
        loafTarget = 0f;
        mood = Mood.Idle;
        yield return Idle(1f);
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
        loafTarget = 0.35f;
        yield return new WaitForSeconds(3.5f);
        loafTarget = 0f;
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
        Halt();
        yield return new WaitForSeconds(seconds);
    }

    IEnumerator RunFrom(Vector3 from, float distance)
    {
        mood = Mood.Running;
        Vector3 away = Flat(transform.position - from);
        if (away.sqrMagnitude < 0.01f) away = -transform.forward;
        Vector3 target = transform.position + away.normalized * distance;
        if (!NavMesh.SamplePosition(target, out var hit, 3f, NavMesh.AllAreas) || !SameFloor(hit.position)
            || !IsFloorSpot(hit.position) || !CanReach(hit.position))
            target = FarPoint(from, distance);
        else
            target = hit.position;
        yield return WalkTo(target, runSpeed, 0.4f, 6f);
        mood = Mood.Idle;
    }

    // ------------------------------------------------------------------ moves

    IEnumerator WalkTo(Vector3 destination, float speed, float stopDistance, float timeout)
    {
        while (jumping) yield return null;
        if (!agent.isOnNavMesh) yield break;
        if (NavMesh.SamplePosition(destination, out var hit, 1.5f, NavMesh.AllAreas)) destination = hit.position;

        agent.speed = speed;
        agent.stoppingDistance = stopDistance;
        agent.isStopped = false;
        agent.SetDestination(destination);

        float giveUp = Time.time + timeout;
        while (Time.time < giveUp || jumping)
        {
            if (!jumping && !agent.pathPending && !agent.isOnOffMeshLink && agent.remainingDistance <= stopDistance + 0.05f) break;
            yield return null;
        }
        Halt();
    }

    /// <summary>Stop walking (but never in the middle of a jump).</summary>
    void Halt()
    {
        if (!jumping && agent.enabled && agent.isOnNavMesh && !agent.isOnOffMeshLink) agent.ResetPath();
    }

    IEnumerator Face(Vector3 target, float duration)
    {
        while (jumping) yield return null;
        Vector3 dir = Flat(target - transform.position);
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

    void FaceInstant(Vector3 target, float amount)
    {
        Vector3 dir = Flat(target - transform.position);
        if (dir.sqrMagnitude > 0.0001f)
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(dir), Mathf.Clamp01(amount));
    }

    /// <summary>A hop toward the target (or straight up when the target is where the cat stands).</summary>
    IEnumerator Pounce(Vector3 target, float height)
    {
        while (jumping) yield return null;
        Halt();
        agent.updateRotation = false;
        Vector3 dir = Flat(target - transform.position);
        float distance = dir.magnitude < 0.05f ? 0f : Mathf.Clamp(dir.magnitude - 0.2f, 0.1f, 0.6f);
        dir = dir.sqrMagnitude > 0.0001f ? dir.normalized : transform.forward;

        const float time = 0.3f;
        for (float t = 0f; t < time; t += Time.deltaTime)
        {
            hop = Mathf.Sin(t / time * Mathf.PI) * height; // scaled by Size in LateUpdate
            if (distance > 0f && agent.isOnNavMesh) agent.Move(dir * (distance / time) * Time.deltaTime);
            yield return null;
        }
        hop = 0f;
        agent.updateRotation = true;
    }

    /// <summary>A quick paw swipe: the cat dips forward and the item skitters away a little.</summary>
    IEnumerator Swat(Carryable item)
    {
        Vector3 start = item.transform.position;
        Vector3 away = Flat(start - transform.position);
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

    /// <summary>Swipes something off furniture so it tumbles onto the floor ("THUD").</summary>
    IEnumerator KnockOff(Carryable item, bool catIsBelow)
    {
        Vector3 start = item.transform.position;
        Vector3 land;
        if (catIsBelow)
        {
            // Lands right in front of the cat.
            land = transform.position + transform.forward * 0.35f * Size;
            if (NavMesh.SamplePosition(land, out var hit, 1f, NavMesh.AllAreas) && IsFloorSpot(hit.position) && SameFloor(hit.position))
                land = hit.position;
            else
                land = transform.position;
        }
        else
        {
            // Pushed over the edge: lands on the floor next to the furniture.
            if (!FindFloorSpot(start, 1.6f, false, out land)) yield break;
        }

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
        if (!catIsBelow) yield return Face(land, 0.25f); // look down at what it did
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
        keyboardOf = null;
        Halt();
        if (agent.isOnNavMesh) agent.isStopped = false;
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
        if (NavMesh.SamplePosition(spot, out var hit, 1f, NavMesh.AllAreas) && Mathf.Abs(hit.position.y - transform.position.y) < 0.2f)
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

    bool CanGetTo(Carryable item) => SpotAt(item, out _) || FindFloorSpot(item.transform.position, 1.4f, true, out _);

    /// <summary>A reachable spot right at the item's level (on the floor or up on the furniture it sits on).</summary>
    bool SpotAt(Carryable item, out Vector3 spot)
    {
        spot = item.transform.position;
        float bottom = ItemBottom(item);
        if (!NavMesh.SamplePosition(new Vector3(spot.x, bottom + 0.05f, spot.z), out var hit, 0.6f, NavMesh.AllAreas)) return false;
        if (Mathf.Abs(hit.position.y - bottom) > 0.25f) return false;
        if (!CanReach(hit.position)) return false;
        spot = hit.position;
        return true;
    }

    /// <summary>A spot on the desk next to the homework (for lying on the keyboard).</summary>
    bool DeskSpot(TypingTask homework, bool mustReach, out Vector3 spot)
    {
        spot = homework.transform.position;
        Bounds b = default;
        bool any = false;
        foreach (var c in homework.GetComponentsInChildren<Collider>())
        {
            if (c.isTrigger) continue;
            if (!any) { b = c.bounds; any = true; } else b.Encapsulate(c.bounds);
        }
        if (!any) return false;
        if (!CatNavMesh.FindTop(b, out var hit)) return false; // no room on top of the desk
        if (mustReach && !CanReach(hit.position)) return false;
        spot = hit.position;
        return true;
    }

    bool TryPickPerch(bool nearest, out CatNavMesh.Perch perch)
    {
        perch = default;
        var options = new List<CatNavMesh.Perch>();
        foreach (var p in CatNavMesh.Perches)
        {
            if (Mathf.Abs(p.top.y - transform.position.y) > 1.6f) continue;   // same storey
            if (Flat(p.top - transform.position).magnitude > 12f) continue;
            options.Add(p);
        }
        if (nearest) options.Sort((a, b) => Flat(a.top - transform.position).sqrMagnitude.CompareTo(Flat(b.top - transform.position).sqrMagnitude));
        else for (int i = options.Count - 1; i > 0; i--) { int j = Random.Range(0, i + 1); (options[i], options[j]) = (options[j], options[i]); }

        int tries = 0;
        foreach (var p in options)
        {
            if (tries++ > 6) break;
            if (CanReach(p.top)) { perch = p; return true; }
        }
        return false;
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

    /// <summary>True if the point is on one of the house's real floors (not on furniture, outside or under the house).</summary>
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

    bool SameFloor(Vector3 point) => Mathf.Abs(point.y - transform.position.y) < 1.6f;

    float ItemBottom(Carryable item)
    {
        float bottom = item.transform.position.y;
        bool any = false;
        foreach (var r in item.GetComponentsInChildren<Renderer>())
        {
            bottom = any ? Mathf.Min(bottom, r.bounds.min.y) : r.bounds.min.y;
            any = true;
        }
        return bottom;
    }

    /// <summary>Is the item sitting up on something, above the cat's feet?</summary>
    bool IsUpHigh(Carryable item) => ItemBottom(item) - transform.position.y > 0.3f;

    /// <summary>Is the item on furniture (rather than on the floor)?</summary>
    bool IsOnFurniture(Carryable item) =>
        FindFloorSpot(item.transform.position, 1.6f, false, out var floor) && ItemBottom(item) - floor.y > 0.25f;

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

    static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

    class HitOrder : IComparer<RaycastHit>
    {
        public int Compare(RaycastHit a, RaycastHit b) => a.distance.CompareTo(b.distance);
    }

    /// <summary>
    /// Keeps a code tweak on a bone (head turn, wiggle...) from piling up frame after frame:
    /// if the bone still holds exactly what we wrote last frame, the animation didn't touch it,
    /// so put it back first. Also snaps a bone back if it ever wanders far from where it started.
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
}
