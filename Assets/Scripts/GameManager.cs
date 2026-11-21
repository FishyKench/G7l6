using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Keeps the list of tasks in the scene and starts random ones over time.
/// Hazards (fire, flood) and anything spawned later (e.g. by the cat) call StartTask() directly,
/// which ignores the random-task limit.
/// </summary>
public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    [Header("Random tasks")]
    [Tooltip("Max random tasks active at once. Main tasks and hazards don't count.")]
    public int maxActiveTasks = 3;
    public float firstTaskDelay = 8f;
    [Tooltip("Random wait (seconds) between new tasks.")]
    public Vector2 timeBetweenTasks = new Vector2(15f, 30f);
    [Tooltip("No new random tasks while this is on (e.g. once Mom is home).")]
    public bool spawningPaused;

    readonly List<TaskBase> tasks = new List<TaskBase>();
    readonly List<TaskBase> hudTasks = new List<TaskBase>();

    public IReadOnlyList<TaskBase> Tasks => tasks;

    void Awake()
    {
        Instance = this;
        // Make sure the HUD and exhaustion exist even if the setup tool wasn't run.
        if (!FindObjectOfType<GameHUD>()) new GameObject("HUD").AddComponent<GameHUD>();
        if (!FindObjectOfType<ExhaustionManager>()) gameObject.AddComponent<ExhaustionManager>();
        if (!FindObjectOfType<DayManager>()) gameObject.AddComponent<DayManager>(); // the clock, Mom coming home, the report card
        if (!FindObjectOfType<ItemFinder>()) gameObject.AddComponent<ItemFinder>(); // hold Tab to see where things are
        if (!FindObjectOfType<PauseMenu>()) gameObject.AddComponent<PauseMenu>();   // Esc / P
    }

    void Start()
    {
        foreach (var task in FindObjectsOfType<TaskBase>(true))
            Register(task);

        foreach (var task in tasks)
            if (task.isMainTask) task.Activate();

        StartCoroutine(SpawnRandomTasks());
    }

    public void Register(TaskBase task)
    {
        if (task && !tasks.Contains(task)) tasks.Add(task);
    }

    /// <summary>Start a specific task right now (used by hazards, later by the cat).</summary>
    public void StartTask(TaskBase task)
    {
        if (!task || task.IsActive) return;
        Register(task);
        task.Activate();
        if (!task.isMainTask)
            GameHUD.Toast("New: " + task.taskName, task is HazardTask ? GameHUD.Bad : GameHUD.Warn);
    }

    IEnumerator SpawnRandomTasks()
    {
        yield return new WaitForSeconds(firstTaskDelay);
        while (true)
        {
            if (!spawningPaused && ActiveRandomTaskCount() < maxActiveTasks)
                TryStartRandomTask();
            yield return new WaitForSeconds(Random.Range(timeBetweenTasks.x, timeBetweenTasks.y));
        }
    }

    int ActiveRandomTaskCount()
    {
        int count = 0;
        foreach (var t in tasks)
            if (t.IsActive && t.canStartRandomly && !t.isMainTask) count++;
        return count;
    }

    void TryStartRandomTask()
    {
        var pool = new List<TaskBase>();
        foreach (var t in tasks)
            if (t.canStartRandomly && !t.isMainTask && !t.IsActive && t.CanStartNow && Time.time >= t.AvailableAt)
                pool.Add(t);

        if (pool.Count > 0)
            StartTask(pool[Random.Range(0, pool.Count)]);
    }

    /// <summary>Active tasks in HUD order: main tasks first, then hazards, then oldest first.</summary>
    public List<TaskBase> GetHudTasks()
    {
        hudTasks.Clear();
        foreach (var t in tasks)
            if (t && t.IsActive) hudTasks.Add(t);

        hudTasks.Sort((a, b) =>
        {
            int ra = a.isMainTask ? 0 : a is HazardTask ? 1 : 2;
            int rb = b.isMainTask ? 0 : b is HazardTask ? 1 : 2;
            return ra != rb ? ra.CompareTo(rb) : a.ActivatedAt.CompareTo(b.ActivatedAt);
        });
        return hudTasks;
    }
}
