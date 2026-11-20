using UnityEngine;

/// <summary>
/// A TV randomly switches to loud static. Stress keeps rising until the player turns it off
/// with the TV remote (pick the remote up, then press E on the TV).
/// Put one on each TV - the GameManager picks which one goes off.
/// </summary>
public class TvTask : TaskBase, IInteractable, IPromptProvider
{
    [Header("TV")]
    public Renderer screenRenderer;
    [Tooltip("Which material slot on the screen renderer is the screen.")]
    public int screenMaterialIndex = 0;
    public Material staticMaterial;
    [Tooltip("Material when the TV is off. Leave empty to keep whatever the screen had.")]
    public Material offMaterial;
    public AudioSource staticSound;
    public float stressPerSecond = 0.6f;
    [Tooltip("If on, the player has to be holding the TV remote to turn it off.")]
    public bool needsRemote = true;

    Material originalMaterial;

    void Reset()
    {
        taskName = "Turn off the TV";
        timeLimit = 0f;          // never fails, it just keeps stressing you out
        stressOnFail = 0f;
        stressReliefOnComplete = 5f;
        cooldown = 25f;
    }

    void Start()
    {
        if (screenRenderer && ValidSlot())
            originalMaterial = screenRenderer.sharedMaterials[screenMaterialIndex];
        SetStatic(false);
    }

    public override string Status => needsRemote && !PlayerHolds(ItemType.Remote)
        ? "Static! Find the TV remote"
        : "Static! Turn it off";

    public string Prompt
    {
        get
        {
            if (!IsActive) return null;
            return needsRemote && !PlayerHolds(ItemType.Remote) ? "TV - needs the remote" : "Turn off TV";
        }
    }

    public void Interact()
    {
        if (!IsActive) return;
        if (needsRemote && !PlayerHolds(ItemType.Remote))
        {
            GameHUD.Toast("Find the TV remote!", GameHUD.Warn);
            return;
        }
        Complete("Peace and quiet.");
    }

    protected override void OnActivated() => SetStatic(true);

    protected override void TaskUpdate()
    {
        if (Stress) Stress.AddStress(stressPerSecond * Time.deltaTime);
    }

    protected override void OnCompleted() => SetStatic(false);
    protected override void OnFailed() => SetStatic(false);

    void SetStatic(bool on)
    {
        if (screenRenderer && ValidSlot())
        {
            var mats = screenRenderer.sharedMaterials;
            mats[screenMaterialIndex] = on ? staticMaterial : (offMaterial ? offMaterial : originalMaterial);
            screenRenderer.sharedMaterials = mats;
        }
        if (staticSound)
        {
            if (on) staticSound.Play();
            else staticSound.Stop();
        }
    }

    bool ValidSlot() => screenMaterialIndex >= 0 && screenMaterialIndex < screenRenderer.sharedMaterials.Length;
}
