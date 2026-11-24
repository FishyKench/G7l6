using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One sticky note in the to-do list. Slaps onto the screen, sways like it's hanging from its
/// tape, shakes when time is running out, gets crossed off and tossed when done, or crumples
/// and falls when failed.
/// </summary>
public class TaskNote : MonoBehaviour
{
    [Tooltip("The part that rotates/scales (pivot at the tape).")]
    public RectTransform body;
    public CanvasGroup group;
    public Image paper;
    public Image barFill;
    public Image strike;
    public Image check;
    public Image circle;
    public TextMeshProUGUI title;
    public TextMeshProUGUI status;
    public TextMeshProUGUI timer;
    public RectTransform tape;

    public TaskBase Task { get; private set; }
    public bool Leaving => state != State.Live;

    enum State { Live, Completing, Failing }

    State state;
    RectTransform rect;
    Color paperColor;
    float stateTime, tilt, phase, targetY, layoutScale = 1f;
    float shownProgress, circleFill, fallSpeed, spin;
    Spring x, y, scale, rot, checkPop;

    public void Bind(TaskBase task, float startY)
    {
        rect = (RectTransform)transform;
        Task = task;
        state = State.Live;
        tilt = Random.Range(-3.5f, 3.5f);
        phase = Random.value * 10f;

        x = new Spring(0f);
        y = new Spring(startY);
        targetY = startY;
        scale = new Spring(1.75f);                              // slaps on from "close to the camera"
        rot = new Spring(tilt + Random.Range(-20f, 20f));
        group.alpha = 0f;

        paperColor = task.isMainTask ? HudArt.NoteBlue : task is HazardTask ? HudArt.NoteCoral : HudArt.NoteYellow;
        paper.color = paperColor;
        title.text = task.taskName;
        title.color = HudArt.Ink;
        barFill.color = task is HazardTask ? HudArt.MarkerRed : HudArt.InkSoft;
        strike.fillAmount = 0f;
        circle.fillAmount = 0f;
        check.gameObject.SetActive(false);
        if (tape) tape.localRotation = Quaternion.Euler(0f, 0f, Random.Range(-10f, 10f));
        shownProgress = task.Progress;

        gameObject.SetActive(true);
        RefreshContent(0f, Time.unscaledTime);
    }

    int shownKey = -1;
    bool compact;

    /// <summary>A smaller note further down the list: title, timer and bar, no status line.</summary>
    public void SetCompact(bool on)
    {
        if (compact == on) return;
        compact = on;
        if (status) status.enabled = !on;
    }

    /// <summary>The number to press (TAB held) for the paw-print trail to this job; 0 hides it.</summary>
    public void SetKey(int n)
    {
        if (n == shownKey || !Task) return;
        shownKey = n;
        title.text = n > 0 ? $"<color=#D23A30>{n}</color>  {Task.taskName}" : Task.taskName;
    }

    public void SetSlot(float slotY, float scaleFactor)
    {
        targetY = slotY;
        layoutScale = scaleFactor;
    }

    public void Finish(bool success)
    {
        if (Leaving) return;
        state = success ? State.Completing : State.Failing;
        stateTime = 0f;
        spin = Random.Range(140f, 260f) * (Random.value < 0.5f ? -1f : 1f);
        if (!success)
        {
            status.text = "FAILED";
            status.color = HudArt.MarkerRed;
            timer.text = "";
        }
    }

    void Update()
    {
        if (!Task && state == State.Live) { Destroy(gameObject); return; }

        float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
        float t = Time.unscaledTime;
        stateTime += dt;

        switch (state)
        {
            case State.Live: LiveUpdate(dt, t); break;
            case State.Completing: CompletingUpdate(dt, t); break;
            case State.Failing: FailingUpdate(dt, t); break;
        }

        rect.anchoredPosition = new Vector2(x.value, y.value);
    }

    void LiveUpdate(float dt, float t)
    {
        y.Step(targetY, dt, 3.2f, 0.6f);
        group.alpha = Mathf.MoveTowards(group.alpha, 1f, dt * 7f);

        // Breathing + swaying from the tape, a bit different for every note.
        float breathe = 1f + Mathf.Sin(t * 1.6f + phase) * 0.012f;
        float sway = Mathf.Sin(t * 0.9f + phase) * 1.0f + Mathf.Sin(t * 2.3f + phase * 2f) * 0.35f;
        if (Task is HazardTask)
            breathe += Mathf.Pow(Mathf.Max(0f, Mathf.Sin(t * 7f)), 8f) * 0.06f; // throbbing

        scale.Step(layoutScale * breathe, dt, 3.5f, 0.35f);
        rot.Step(tilt + sway, dt, 2.5f, 0.3f);

        Vector2 shake = Vector2.zero;
        if (Task.DeadlineRunning && Task.TimeLeft <= 10f)
        {
            float strength = Mathf.Lerp(2f, 7f, 1f - Task.TimeLeft / 10f);
            shake = new Vector2(Mathf.PerlinNoise(t * 20f, phase) - 0.5f, Mathf.PerlinNoise(phase, t * 20f) - 0.5f) * strength * 2f;
        }

        ApplyBody(scale.value, scale.value, rot.value, shake);
        RefreshContent(dt, t);
    }

    void RefreshContent(float dt, float t)
    {
        status.text = Task.Status;
        status.color = HudArt.InkSoft;

        shownProgress = Mathf.Lerp(shownProgress, Task.Progress, 1f - Mathf.Exp(-8f * dt));
        barFill.fillAmount = shownProgress;

        bool urgent = Task.DeadlineRunning && Task.TimeLeft <= 10f;
        if (Task.DeadlineRunning)
        {
            timer.text = FormatTime(Task.TimeLeft);
            timer.color = urgent ? HudArt.MarkerRed : HudArt.Ink;
            float pulse = urgent ? 1f + Mathf.Pow(Mathf.Abs(Mathf.Sin(t * 6f)), 6f) * 0.25f : 1f;
            timer.rectTransform.localScale = Vector3.one * pulse;
        }
        else
        {
            timer.text = "";
        }

        // The red marker circle draws itself around the timer when it gets urgent.
        circleFill = Mathf.MoveTowards(circleFill, urgent ? 1f : 0f, dt * 3f);
        circle.fillAmount = circleFill;
    }

    void CompletingUpdate(float dt, float t)
    {
        strike.fillAmount = Mathf.Clamp01(stateTime / 0.3f);
        barFill.fillAmount = Mathf.MoveTowards(barFill.fillAmount, 1f, dt * 4f);
        timer.text = "";
        circle.fillAmount = Mathf.MoveTowards(circle.fillAmount, 0f, dt * 6f);

        if (stateTime >= 0.25f && !check.gameObject.activeSelf)
        {
            check.gameObject.SetActive(true);
            checkPop = new Spring(2.4f);
        }
        if (check.gameObject.activeSelf)
            check.rectTransform.localScale = Vector3.one * checkPop.Step(1f, dt, 5f, 0.35f);

        if (stateTime < 0.85f)
        {
            scale.Step(layoutScale * 1.06f, dt, 4f, 0.4f);
            rot.Step(tilt, dt, 3f, 0.4f);
        }
        else
        {
            // Peel it off and toss it to the right.
            x.Step(560f, dt, 1.4f, 0.9f);
            rot.Step(tilt - 28f, dt, 1.4f, 0.6f);
            group.alpha = Mathf.MoveTowards(group.alpha, 0f, dt * 2.2f);
        }

        ApplyBody(scale.value, scale.value, rot.value, Vector2.zero);
        if (stateTime > 1.5f) Destroy(gameObject);
    }

    void FailingUpdate(float dt, float t)
    {
        paper.color = Color.Lerp(paperColor, new Color(0.62f, 0.6f, 0.58f), Mathf.Clamp01(stateTime * 3f));
        title.color = HudArt.MarkerRed;
        circle.fillAmount = Mathf.MoveTowards(circle.fillAmount, 0f, dt * 6f);

        if (stateTime < 0.35f)
        {
            // Crumple: violent squash and shake.
            float wob = Mathf.Sin(stateTime * 70f);
            Vector2 shake = new Vector2(Random.Range(-1f, 1f), Random.Range(-1f, 1f)) * 9f;
            ApplyBody(layoutScale * (1f + wob * 0.07f), layoutScale * (1f - wob * 0.1f), tilt + wob * 6f, shake);
        }
        else
        {
            // Fall off the screen.
            fallSpeed += 2600f * dt;
            y.value -= fallSpeed * dt;
            rot.value += spin * dt;
            scale.Step(layoutScale * 0.85f, dt, 2f, 0.8f);
            group.alpha = Mathf.MoveTowards(group.alpha, 0f, dt * 1.2f);
            ApplyBody(scale.value, scale.value, rot.value, Vector2.zero);
        }

        if (stateTime > 1.4f) Destroy(gameObject);
    }

    void ApplyBody(float sx, float sy, float angle, Vector2 offset)
    {
        body.localScale = new Vector3(sx, sy, 1f);
        body.localRotation = Quaternion.Euler(0f, 0f, angle);
        body.anchoredPosition = offset;
    }

    static string FormatTime(float seconds)
    {
        int s = Mathf.CeilToInt(Mathf.Max(0f, seconds));
        return s >= 60 ? $"{s / 60}:{s % 60:00}" : s + "s";
    }
}
