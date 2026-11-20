using UnityEngine;

/// <summary>
/// Ceiling leak: a tap upstairs is left running and water drips through the ceiling onto the
/// floor below.
///   1. (optional) Put a bucket under the drip - stops the stress and buys time while it fills.
///   2. Run upstairs and turn off the tap - task done.
/// If time runs out (or the bucket overflows) the room below floods and needs the mop.
///
/// The drip is placed at runtime straight below the tap, so you can move the sink anywhere.
/// </summary>
public class CeilingLeakTask : TaskBase, IInteractable, IPromptProvider
{
    [Header("Leak")]
    public LeakTap tap;
    public Carryable bucket;
    public FloodHazard flood;
    public float stressPerSecond = 0.8f;
    [Tooltip("Seconds a bucket can catch the water before it overflows.")]
    public float bucketCapacity = 60f;

    [Header("Visuals (positioned automatically)")]
    [Tooltip("Parent of the drips, puddle and hitbox. Hidden while there's no leak.")]
    public GameObject visualRoot;
    public ParticleSystem drips;
    public Transform puddle;
    public BoxCollider hitbox;
    public float puddleMaxDiameter = 1.3f;
    public float puddleGrowTime = 30f;

    bool bucketPlaced;
    float puddleSize;
    Vector3 floorPoint;

    void Reset()
    {
        taskName = "Stop the leak";
        timeLimit = 75f;
        stressOnFail = 12f;
        stressReliefOnComplete = 8f;
        cooldown = 50f;
    }

    void Start()
    {
        floorPoint = transform.position;
        if (visualRoot) visualRoot.SetActive(false);
    }

    public override bool CanStartNow => tap && !(flood && flood.IsActive);

    public override float Progress => bucketPlaced && bucketCapacity > 0f ? 1f - TimeLeft / bucketCapacity : 0f;

    public override string Status => bucketPlaced
        ? "Bucket's filling - turn off the tap upstairs!"
        : "Water through the ceiling! Bucket + tap upstairs";

    public string Prompt
    {
        get
        {
            if (!IsActive) return null;
            if (bucketPlaced) return "Bucket filling... turn off the tap upstairs";
            return PlayerHolds(ItemType.Bucket) ? "Put the bucket under the drip" : "Leak - get a bucket";
        }
    }

    public void Interact()
    {
        if (!IsActive) return;
        if (bucketPlaced)
        {
            GameHUD.Toast("Turn off the tap upstairs!", GameHUD.Warn);
            return;
        }
        if (!PlayerHolds(ItemType.Bucket))
        {
            GameHUD.Toast("You need a bucket!", GameHUD.Warn);
            return;
        }

        var held = PlayerCarry.Instance.TakeHeld();
        held.PlaceAt(floorPoint, 0f);
        CatchWithBucket("That'll hold it for a bit. Now turn off the tap!");
    }

    /// <summary>Called by the LeakTap upstairs.</summary>
    public void TurnOffTap()
    {
        if (IsActive) Complete("Tap's off - leak stopped!");
    }

    protected override void OnActivated()
    {
        bucketPlaced = false;
        PlaceUnderTap();

        puddleSize = 0.15f;
        SetPuddle(puddleSize);
        if (visualRoot) visualRoot.SetActive(true);
        if (drips) drips.Play();
        if (tap) tap.SetRunning(true);

        // Left the bucket under the drip last time? Smart.
        if (bucket && !bucket.IsHeld && bucket.gameObject.activeInHierarchy)
        {
            Vector3 d = bucket.transform.position - floorPoint;
            d.y = 0f;
            if (d.magnitude < 0.5f) CatchWithBucket("Good thing the bucket's still there!");
        }
    }

    void CatchWithBucket(string message)
    {
        bucketPlaced = true;
        StartDeadline(bucketCapacity);
        GameHUD.Toast(message, GameHUD.Good);
    }

    protected override void TaskUpdate()
    {
        if (bucketPlaced) return;

        if (Stress) Stress.AddStress(stressPerSecond * Time.deltaTime);
        puddleSize = Mathf.MoveTowards(puddleSize, puddleMaxDiameter, puddleMaxDiameter / Mathf.Max(1f, puddleGrowTime) * Time.deltaTime);
        SetPuddle(puddleSize);
    }

    protected override void OnDeadlineReached()
    {
        Fail(bucketPlaced ? "The bucket overflowed - it's flooding!" : "The ceiling gave up - it's flooding!");
        if (flood && GameManager.Instance)
        {
            flood.transform.position = floorPoint + Vector3.up * 0.012f;
            GameManager.Instance.StartTask(flood);
        }
    }

    protected override void OnCompleted() => StopLeak();
    protected override void OnFailed() => StopLeak();

    void StopLeak()
    {
        if (drips) drips.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        if (visualRoot) visualRoot.SetActive(false);
        if (tap) tap.SetRunning(false);
    }

    void SetPuddle(float diameter)
    {
        if (!puddle) return;
        var s = puddle.localScale;
        puddle.localScale = new Vector3(diameter, s.y, diameter);
    }

    /// <summary>Finds the floor below the tap's floor and the ceiling above it, and moves the drip there.</summary>
    void PlaceUnderTap()
    {
        if (!tap) return;

        Vector3 origin = tap.WaterOrigin;
        var hits = Physics.RaycastAll(origin, Vector3.down, 40f, ~0, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        bool haveUpper = false, haveGround = false;
        float upperY = 0f;
        RaycastHit ground = default;
        foreach (var h in hits)
        {
            var t = h.collider.transform;
            if (t.IsChildOf(tap.transform) || t.IsChildOf(transform)) continue;
            if (h.collider.attachedRigidbody && h.collider.attachedRigidbody.GetComponent<PlayerMovementAdvanced>()) continue;
            if (bucket && t.IsChildOf(bucket.transform)) continue;

            if (!haveUpper) { upperY = h.point.y; haveUpper = true; continue; }   // the floor the sink stands on
            if (h.point.y < upperY - 1.2f) { ground = h; haveGround = true; break; } // the floor below
        }
        if (!haveGround) return; // keep wherever the setup tool put it

        floorPoint = ground.point;
        float ceilingY = upperY - 0.2f;
        bool hitBackfaces = Physics.queriesHitBackfaces;
        Physics.queriesHitBackfaces = true; // single-sided floor meshes seen from below
        if (Physics.Raycast(floorPoint + Vector3.up * 0.05f, Vector3.up, out RaycastHit up, upperY - floorPoint.y + 0.5f, ~0, QueryTriggerInteraction.Ignore))
            ceilingY = up.point.y - 0.02f;
        Physics.queriesHitBackfaces = hitBackfaces;

        float height = Mathf.Max(0.5f, ceilingY - floorPoint.y);
        transform.position = floorPoint;

        if (puddle) puddle.position = floorPoint + Vector3.up * 0.012f;
        if (drips)
        {
            drips.transform.position = new Vector3(floorPoint.x, ceilingY, floorPoint.z);
            var main = drips.main;
            main.startLifetime = Mathf.Sqrt(2f * height / Mathf.Abs(Physics.gravity.y)); // land on the floor
        }
        if (hitbox)
        {
            hitbox.transform.position = floorPoint + Vector3.up * (height * 0.5f);
            Vector3 s = hitbox.transform.lossyScale;
            hitbox.center = Vector3.zero;
            hitbox.size = new Vector3(0.9f / Mathf.Max(0.0001f, s.x), height / Mathf.Max(0.0001f, s.y), 0.9f / Mathf.Max(0.0001f, s.z));
        }
    }
}
