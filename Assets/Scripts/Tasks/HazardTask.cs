using UnityEngine;

/// <summary>
/// Something that went wrong and keeps adding stress until it's cleaned up with the right item
/// (fire + extinguisher, flood + mop). Hazards are never picked at random - another task starts
/// them with GameManager.Instance.StartTask(hazard).
/// </summary>
public abstract class HazardTask : TaskBase, IInteractable, IPromptProvider
{
    [Header("Hazard")]
    public ItemType requiredItem;
    public string requiredItemName = "item";
    [Tooltip("Word on the prompt, e.g. 'Spray' or 'Mop up'.")]
    public string actionVerb = "Clean up";
    public float stressPerSecond = 2f;
    [Tooltip("Seconds it takes to clear once the player uses the item on it.")]
    public float clearDuration = 2f;
    [Tooltip("Everything you can see/hit (particles, puddle, colliders). Hidden while inactive.")]
    public GameObject visualRoot;

    protected bool clearing;
    protected float clearTimer;

    protected virtual void Awake()
    {
        canStartRandomly = false;
        isMainTask = false;
        timeLimit = 0f;
    }

    protected virtual void Start()
    {
        if (visualRoot) visualRoot.SetActive(false);
    }

    public override float Progress => clearing ? clearTimer / clearDuration : 0f;
    public override string Status => clearing ? "Clearing..." : $"Get the {requiredItemName}!";

    public virtual string Prompt
    {
        get
        {
            if (!IsActive || clearing) return null;
            return PlayerHolds(requiredItem) ? $"{actionVerb} ({requiredItemName})" : $"Need the {requiredItemName}";
        }
    }

    public virtual void Interact()
    {
        if (!IsActive || clearing) return;
        if (!PlayerHolds(requiredItem))
        {
            GameHUD.Toast($"You need the {requiredItemName}!", GameHUD.Warn);
            return;
        }
        clearing = true;
        clearTimer = 0f;
    }

    protected override void OnActivated()
    {
        clearing = false;
        clearTimer = 0f;
        if (visualRoot) visualRoot.SetActive(true);
        OnHazardStarted();
    }

    protected override void TaskUpdate()
    {
        if (!clearing)
        {
            if (Stress) Stress.AddStress(stressPerSecond * Time.deltaTime);
            OnHazardTick();
            return;
        }

        clearTimer += Time.deltaTime;
        OnClearing(Mathf.Clamp01(clearTimer / clearDuration));
        if (clearTimer >= clearDuration)
            Complete(ClearedMessage);
    }

    protected override void OnCompleted() => Hide();
    protected override void OnFailed() => Hide();

    void Hide()
    {
        clearing = false;
        if (visualRoot) visualRoot.SetActive(false);
        OnHazardEnded();
    }

    protected virtual string ClearedMessage => taskName + " - sorted!";
    protected virtual void OnHazardStarted() { }
    protected virtual void OnHazardTick() { }
    /// <param name="t">0..1 how far the clean-up is.</param>
    protected virtual void OnClearing(float t) { }
    protected virtual void OnHazardEnded() { }
}
