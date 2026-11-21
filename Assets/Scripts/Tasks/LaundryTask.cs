using UnityEngine;

/// <summary>
/// Laundry:  dirty clothes appear (upstairs)  ->  carry them to the washing machine  ->  it washes
/// for washTime  ->  take the laundry out. If it's left too long after it finishes, it overflows
/// and floods the room.
/// </summary>
public class LaundryTask : TaskBase, IInteractable, IPromptProvider
{
    enum Phase { WaitingForClothes, Washing, Done, PutAway }

    [Header("Laundry")]
    [Tooltip("The dirty clothes pile. Hidden until the task starts.")]
    public Carryable dirtyClothes;
    [Tooltip("Clean clothes you take out of the washer and put away in the wardrobe. Empty = taking the laundry out finishes it.")]
    public Carryable cleanClothes;
    public Wardrobe wardrobe;
    [Tooltip("Where the clothes are, for the HUD: \"Dirty clothes are ...\" (e.g. \"upstairs\", \"in the kid's room\").")]
    public string clothesLocation = "upstairs";
    public float washTime = 35f;
    [Tooltip("Seconds after washing finishes before the machine overflows.")]
    public float overflowAfter = 25f;
    public FloodHazard flood;

    [Header("'Machine is running' visuals")]
    public Light runningLight;
    [Tooltip("What shakes while washing (usually the machine itself).")]
    public Transform shakeTarget;
    public float shakeAmount = 0.004f;

    Phase phase;
    float washTimer;
    Vector3 shakeOrigin;
    bool shaking;

    void Reset()
    {
        taskName = "Do the laundry";
        timeLimit = 120f;
        stressOnFail = 12f;
        stressReliefOnComplete = 8f;
        cooldown = 45f;
    }

    void Start()
    {
        if (dirtyClothes) dirtyClothes.ReturnHome(false);
        if (cleanClothes) cleanClothes.ReturnHome(false);
        SetRunning(false);
    }

    public override bool CanStartNow => !(flood && flood.IsActive);

    public override float Progress => phase == Phase.Washing ? washTimer / washTime : phase == Phase.Done ? 1f : 0f;

    public override string Status
    {
        get
        {
            switch (phase)
            {
                case Phase.Washing: return $"Washing... {Seconds(washTime - washTimer)}";
                case Phase.Done: return "Done! Take it out before it overflows";
                case Phase.PutAway: return PlayerHolds(ItemType.CleanClothes) ? "Put the clean clothes in your wardrobe" : "The clean clothes go in your wardrobe";
                default: return PlayerHolds(ItemType.DirtyClothes) ? "Take the clothes to the washer" : "Dirty clothes are " + clothesLocation;
            }
        }
    }

    public string Prompt
    {
        get
        {
            if (!IsActive) return null;
            switch (phase)
            {
                case Phase.Washing: return $"Washing... {Seconds(washTime - washTimer)}";
                case Phase.Done: return "Take out the laundry";
                case Phase.PutAway: return null;
                default: return PlayerHolds(ItemType.DirtyClothes) ? "Load clothes and start" : "Washer - needs dirty clothes";
            }
        }
    }

    public void Interact()
    {
        if (!IsActive) return;

        switch (phase)
        {
            case Phase.WaitingForClothes:
                if (!PlayerHolds(ItemType.DirtyClothes))
                {
                    GameHUD.Toast("Bring the dirty clothes first!", GameHUD.Warn);
                    return;
                }
                var clothes = PlayerCarry.Instance.TakeHeld();
                if (clothes) clothes.ReturnHome(false);
                phase = Phase.Washing;
                washTimer = 0f;
                StopDeadline();
                SetRunning(true);
                GameHUD.Toast("Washer started.", GameHUD.Info);
                break;

            case Phase.Washing:
                GameHUD.Toast($"Still washing - {Seconds(washTime - washTimer)} left", GameHUD.Info);
                break;

            case Phase.Done:
                if (cleanClothes && wardrobe)
                {
                    // You get an armful of clean clothes: they go in the wardrobe in your room.
                    phase = Phase.PutAway;
                    StopDeadline();
                    SetRunning(false);
                    cleanClothes.ReturnHome(true);
                    if (PlayerCarry.Instance) PlayerCarry.Instance.PickUp(cleanClothes);
                    GameHUD.Toast("Clean clothes! Put them in your wardrobe.", GameHUD.Info);
                    break;
                }
                Complete("Laundry done!");
                break;
        }
    }

    /// <summary>The wardrobe says the clean clothes are in.</summary>
    public void ClothesPutAway()
    {
        if (!IsActive || phase != Phase.PutAway) return;
        Complete("Laundry done - clothes put away!");
    }

    public override Vector3 Where => phase == Phase.PutAway && wardrobe ? wardrobe.transform.position : transform.position;

    protected override void OnActivated()
    {
        phase = Phase.WaitingForClothes;
        washTimer = 0f;
        if (dirtyClothes) dirtyClothes.ReturnHome(true);
    }

    protected override void TaskUpdate()
    {
        if (phase == Phase.Washing)
        {
            washTimer += Time.deltaTime;
            if (shaking && shakeTarget)
                shakeTarget.position = shakeOrigin + Random.insideUnitSphere * shakeAmount;

            if (washTimer >= washTime)
            {
                phase = Phase.Done;
                StopShaking();
                StartDeadline(overflowAfter);
                GameHUD.Toast("The laundry is done - take it out!", GameHUD.Warn);
            }
        }
        else if (phase == Phase.Done && runningLight)
        {
            runningLight.enabled = Mathf.Repeat(Time.time, 0.8f) < 0.4f; // blink: "I'm done!"
        }
    }

    protected override void OnDeadlineReached()
    {
        if (phase != Phase.Done)
        {
            base.OnDeadlineReached(); // never brought the clothes
            return;
        }

        Fail("The washing machine overflowed!");
        if (flood && GameManager.Instance) GameManager.Instance.StartTask(flood);
    }

    protected override void OnCompleted() => ResetMachine();

    protected override void OnFailed()
    {
        ResetMachine();
        if (dirtyClothes) dirtyClothes.ReturnHome(false); // also takes them out of the player's hands
        if (cleanClothes) cleanClothes.ReturnHome(false);
    }

    void ResetMachine()
    {
        phase = Phase.WaitingForClothes;
        SetRunning(false);
    }

    void SetRunning(bool on)
    {
        if (runningLight) runningLight.enabled = on;
        if (on)
        {
            if (shakeTarget) { shakeOrigin = shakeTarget.position; shaking = true; }
        }
        else
        {
            StopShaking();
        }
    }

    void StopShaking()
    {
        if (shaking && shakeTarget) shakeTarget.position = shakeOrigin;
        shaking = false;
    }
}
