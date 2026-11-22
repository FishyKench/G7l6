using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Cook dinner:  turn the oven on  ->  it cooks for cookTime  ->  take it out.
/// Timing is the skill: right after it's ready dinner is GOLDEN (Mom loves it: bonus on the report card), then it gets CRISPY
/// (still fine), and if it stays in until catchFireAt (seconds after turning it on) it burns and a fire starts.
/// </summary>
public class OvenTask : TaskBase, IInteractable, IPromptProvider
{
    enum Phase { WaitingToStart, Cooking, Ready }

    [Header("Oven")]
    public float cookTime = 40f;
    [Tooltip("Seconds after turning the oven on before it catches fire if nobody turns it off.")]
    public float catchFireAt = 65f;
    public FireHazard fire;

    [Header("'Oven is on' visuals")]
    public Light ovenLight;
    public float lightIntensity = 2.5f;
    [Tooltip("Parts that glow while the oven is on (e.g. the oven door).")]
    public Renderer[] glowRenderers;
    public Color glowColor = new Color(1f, 0.42f, 0.1f);
    public float glowStrength = 1.2f;

    [Tooltip("Share of the ready-to-fire time that counts as golden (perfect).")]
    public float goldenShare = 0.45f;

    Phase phase;
    float cookTimer, nextWarningBeep, readyFor, readyAt;
    bool Golden => phase == Phase.Ready && Time.time - readyAt <= readyFor * goldenShare;
    bool warned;
    readonly List<Material> glowMaterials = new List<Material>();
    readonly List<Color> originalEmission = new List<Color>();
    readonly List<bool> originalKeyword = new List<bool>();

    void Reset()
    {
        taskName = "Cook dinner";
        timeLimit = 60f;
        stressOnFail = 15f;
        stressReliefOnComplete = 8f;
        cooldown = 40f;
    }

    void Start()
    {
        if (glowRenderers != null)
        {
            foreach (var r in glowRenderers)
            {
                if (!r) continue;
                foreach (var m in r.materials) // instanced, so other objects sharing the material don't glow
                {
                    glowMaterials.Add(m);
                    originalKeyword.Add(m.IsKeywordEnabled("_EMISSION"));
                    originalEmission.Add(m.HasProperty("_EmissionColor") ? m.GetColor("_EmissionColor") : Color.black);
                }
            }
        }
        SetOn(false);
    }

    public override bool CanStartNow => !(fire && fire.IsActive);

    public override float Progress => phase == Phase.Cooking ? cookTimer / cookTime : phase == Phase.Ready ? 1f : 0f;

    public override string Status
    {
        get
        {
            switch (phase)
            {
                case Phase.Cooking: return $"Cooking... {Seconds(cookTime - cookTimer)}";
                case Phase.Ready: return Golden ? "GOLDEN! Take dinner out now" : "Getting crispy - take it out before it burns!";
                default: return "Turn on the oven";
            }
        }
    }

    public string Prompt
    {
        get
        {
            if (fire && fire.IsActive) return fire.Prompt;
            if (!IsActive) return null;
            switch (phase)
            {
                case Phase.Cooking: return $"Cooking... {Seconds(cookTime - cookTimer)}";
                case Phase.Ready: return Golden ? "Take dinner out - golden brown!" : "Take dinner out - getting crispy!";
                default: return "Turn on oven";
            }
        }
    }

    public void Interact()
    {
        if (fire && fire.IsActive) { fire.Interact(); return; }
        if (!IsActive) return;

        switch (phase)
        {
            case Phase.WaitingToStart:
                phase = Phase.Cooking;
                cookTimer = 0f;
                StopDeadline();
                SetOn(true);
                GameHUD.Toast("Oven on. Don't forget it!", GameHUD.Info);
                break;
            case Phase.Cooking:
                GameHUD.Toast($"Still cooking - {Seconds(cookTime - cookTimer)} left", GameHUD.Info);
                break;
            case Phase.Ready:
                if (Golden)
                {
                    DayManager.AddQuality("Perfect golden dinner", 4);
                    Complete("Perfect - golden brown! Mom will love it.");
                }
                else Complete("Dinner's done... a little crispy.");
                break;
        }
    }

    protected override void OnActivated()
    {
        phase = Phase.WaitingToStart;
        cookTimer = 0f;
    }

    protected override void TaskUpdate()
    {
        if (phase == Phase.WaitingToStart) return;

        if (ovenLight)
            ovenLight.intensity = lightIntensity * (0.9f + 0.2f * Mathf.PerlinNoise(Time.time * 3f, 0.7f));

        if (phase == Phase.Cooking)
        {
            cookTimer += Time.deltaTime;
            if (cookTimer >= cookTime)
            {
                phase = Phase.Ready;
                bool timer = Shop.IsInstalled(Shop.OvenTimer);
                // Smart oven timer: 50% longer before it catches fire (and it beeps before that happens).
                readyFor = Mathf.Max(1f, catchFireAt - cookTime) * (timer ? 1.5f : 1f);
                readyAt = Time.time;
                StartDeadline(readyFor);
                warned = false;
                GameHUD.Toast(timer ? "Smart timer: dinner is golden - take it out!" : "Dinner is golden - take it out now for a perfect one!", GameHUD.Warn);
                if (timer) Fx.Play(Fx.Beep, transform.position, 0.8f, 1f);
            }
        }
        else if (phase == Phase.Ready && DeadlineRunning && TimeLeft <= 8f && Shop.IsInstalled(Shop.OvenTimer))
        {
            // Smart oven timer: a warning, with beeps, a few seconds before the fire.
            if (!warned)
            {
                warned = true;
                GameHUD.Toast("Smart timer: the oven is overheating - turn it OFF!", GameHUD.Bad);
            }
            if (Time.time >= nextWarningBeep)
            {
                nextWarningBeep = Time.time + 1.1f;
                Fx.Play(Fx.Beep, transform.position, 0.9f, 1f + 0.12f * (8f - TimeLeft) / 8f);
            }
        }
    }

    protected override void OnDeadlineReached()
    {
        if (phase != Phase.Ready)
        {
            base.OnDeadlineReached(); // never turned on in time
            return;
        }

        Fail("Dinner burned - the oven caught fire!");
        if (fire && GameManager.Instance) GameManager.Instance.StartTask(fire);
    }

    protected override void OnCompleted() => ResetOven();
    protected override void OnFailed() => ResetOven();

    void ResetOven()
    {
        phase = Phase.WaitingToStart;
        SetOn(false);
    }

    void SetOn(bool on)
    {
        if (ovenLight)
        {
            ovenLight.enabled = on;
            ovenLight.intensity = lightIntensity;
        }

        for (int i = 0; i < glowMaterials.Count; i++)
        {
            var m = glowMaterials[i];
            if (on)
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", glowColor * glowStrength);
            }
            else
            {
                if (!originalKeyword[i]) m.DisableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", originalEmission[i]);
            }
        }
    }
}
