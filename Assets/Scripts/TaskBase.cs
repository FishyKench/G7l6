using UnityEngine;

/// <summary>
/// Base class for every task in the house (TV, oven, laundry, homework, hazards...).
///
/// Lifecycle:  inactive  --Activate()-->  active  --Complete() / Fail()-->  inactive (cooldown)
///
/// A task can run a deadline (a countdown shown in the HUD). When it reaches zero,
/// OnDeadlineReached() is called; by default that fails the task. Tasks with several
/// steps (oven, laundry) start new deadlines for each step with StartDeadline().
/// </summary>
public abstract class TaskBase : MonoBehaviour
{
    [Header("Task")]
    public string taskName = "Task";
    [Tooltip("Seconds the player has to start dealing with the task before it fails. 0 = never runs out.")]
    public float timeLimit = 60f;
    [Tooltip("Stress added when the task fails.")]
    public float stressOnFail = 10f;
    [Tooltip("Stress removed when the task is completed.")]
    public float stressReliefOnComplete = 5f;

    [Header("Scheduling")]
    [Tooltip("Main tasks (like homework) are active from the start and are never picked at random.")]
    public bool isMainTask = false;
    [Tooltip("Can the GameManager start this task at random?")]
    public bool canStartRandomly = true;
    [Tooltip("Seconds after the task ends before the GameManager may pick it again.")]
    public float cooldown = 20f;

    public bool IsActive { get; private set; }
    public bool DeadlineRunning { get; private set; }
    public float TimeLeft { get; private set; }
    public float ActivatedAt { get; private set; }
    public float AvailableAt { get; private set; }
    /// <summary>How the task ended last time (true = completed, false = failed).</summary>
    public bool LastSucceeded { get; private set; }

    /// <summary>0..1, drawn as the progress bar in the HUD.</summary>
    public virtual float Progress => 0f;
    /// <summary>Short line under the task name in the HUD, e.g. "Cooking... 12s".</summary>
    public virtual string Status => "";
    /// <summary>Return false to stop the GameManager starting this task right now.</summary>
    public virtual bool CanStartNow => true;

    protected StressManager Stress => StressManager.Instance;

    /// <summary>Where the task is right now (the map pin and Mom go here). Usually the object it's on.</summary>
    public virtual Vector3 Where => transform.position;

    /// <summary>Any task finishing: (task, true = completed / false = failed). The day's report card counts these.</summary>
    public static event System.Action<TaskBase, bool> Ended;

    public void Activate()
    {
        if (IsActive) return;
        IsActive = true;
        ActivatedAt = Time.time;
        StartDeadline(timeLimit);
        OnActivated();
        Activated?.Invoke(this);
    }

    /// <summary>Any task starting (the guide shows new players the way to their first chore).</summary>
    public static event System.Action<TaskBase> Activated;

    protected virtual void Update()
    {
        if (!IsActive || PauseMenu.IsPaused) return;

        if (DeadlineRunning)
        {
            TimeLeft -= Time.deltaTime;
            if (TimeLeft <= 0f)
            {
                TimeLeft = 0f;
                DeadlineRunning = false;
                OnDeadlineReached();
                if (!IsActive) return;
            }
        }

        TaskUpdate();
    }

    /// <summary>Called every frame while the task is active.</summary>
    protected virtual void TaskUpdate() { }

    protected void StartDeadline(float seconds)
    {
        TimeLeft = seconds * Tuning.TaskTime; // every step's timer gets the same little extra
        DeadlineRunning = seconds > 0f;
    }

    protected void StopDeadline() => DeadlineRunning = false;

    protected virtual void OnDeadlineReached() => Fail(taskName + " ran out of time!");

    public void Complete(string message = null)
    {
        if (!IsActive) return;
        LastSucceeded = true;
        End();
        if (Stress) Stress.RemoveStress(stressReliefOnComplete);
        OnCompleted();
        Ended?.Invoke(this, true);
        GameHUD.Toast(message ?? taskName + " done!", GameHUD.Good);
    }

    public void Fail(string message = null)
    {
        if (!IsActive) return;
        LastSucceeded = false;
        End();
        if (Stress) Stress.AddStress(stressOnFail);
        OnFailed();
        Ended?.Invoke(this, false);
        GameHUD.Toast(message ?? taskName + " failed!", GameHUD.Bad);
    }

    /// <summary>Stops it without it counting as done or failed (no message, no report card).</summary>
    protected void StopQuietly()
    {
        IsActive = false;
        DeadlineRunning = false;
    }

    void End()
    {
        IsActive = false;
        DeadlineRunning = false;
        AvailableAt = Time.time + cooldown;
    }

    protected virtual void OnActivated() { }
    protected virtual void OnCompleted() { }
    protected virtual void OnFailed() { }

    protected static string Seconds(float t) => Mathf.CeilToInt(Mathf.Max(0f, t)) + "s";
    protected static bool PlayerHolds(ItemType type) => PlayerCarry.Instance && PlayerCarry.Instance.IsHolding(type);
}
