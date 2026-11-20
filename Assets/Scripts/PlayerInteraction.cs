using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Looks at what's under the crosshair, highlights it, shows its prompt and handles E / Esc.
/// "Main tasks" (homework desk) lock the player in place until they leave with Esc.
/// </summary>
public class PlayerInteraction : MonoBehaviour
{
    public static PlayerInteraction Instance { get; private set; }

    public float interactionRange = 3f;
    public KeyCode interactKey = KeyCode.E;
    public KeyCode exitKey = KeyCode.Escape;
    public PlayerCam playerCam;

    Camera playerCamera;
    PlayerMovementAdvanced playerMovement;
    IMainTask currentMainTask;
    Outline currentOutline;

    static readonly RaycastHit[] hits = new RaycastHit[16];
    static readonly HitDistanceComparer byDistance = new HitDistanceComparer();

    public bool InMainTask => currentMainTask != null;

    void Awake()
    {
        Instance = this;
        if (!GetComponent<PlayerCarry>()) gameObject.AddComponent<PlayerCarry>();
    }

    void Start()
    {
        playerCamera = Camera.main;
        playerMovement = GetComponent<PlayerMovementAdvanced>();
        if (!playerMovement) playerMovement = FindObjectOfType<PlayerMovementAdvanced>();
        if (!playerCam) playerCam = FindObjectOfType<PlayerCam>();
    }

    void Update()
    {
        if (InMainTask)
        {
            SetHighlight(null);
            GameHUD.SetPrompt(null);

            if (Input.GetKeyDown(exitKey))
                StopCurrentMainTask();
            else if (!currentMainTask.ShouldBlockInput() && Input.GetKeyDown(interactKey))
                StopCurrentMainTask();
            return;
        }

        FindTarget(out Collider hitCollider, out IMainTask mainTask, out IInteractable interactable);

        string prompt = null;
        if (mainTask != null) prompt = mainTask is IPromptProvider mp ? mp.Prompt : "Use";
        else if (interactable != null) prompt = interactable is IPromptProvider ip ? ip.Prompt : "Use";

        bool hasTarget = !string.IsNullOrEmpty(prompt);
        SetHighlight(hasTarget ? hitCollider.GetComponentInParent<Outline>() : null);
        GameHUD.SetPrompt(prompt);

        if (hasTarget && Input.GetKeyDown(interactKey))
        {
            if (mainTask != null) StartMainTask(mainTask);
            else interactable.Interact();
        }
    }

    /// <summary>Finds the closest interactable under the crosshair. Triggers (fire, puddles) count, but only if they're interactable.</summary>
    void FindTarget(out Collider hitCollider, out IMainTask mainTask, out IInteractable interactable)
    {
        hitCollider = null; mainTask = null; interactable = null;
        if (!playerCamera) { playerCamera = Camera.main; if (!playerCamera) return; }

        var ray = new Ray(playerCamera.transform.position, playerCamera.transform.forward);
        int count = Physics.RaycastNonAlloc(ray, hits, interactionRange, ~0, QueryTriggerInteraction.Collide);
        System.Array.Sort(hits, 0, count, byDistance);

        for (int i = 0; i < count; i++)
        {
            var col = hits[i].collider;
            if (col.transform.IsChildOf(transform)) continue; // ourselves

            mainTask = col.GetComponentInParent<IMainTask>();
            interactable = mainTask == null ? col.GetComponentInParent<IInteractable>() : null;
            if (mainTask != null || interactable != null) { hitCollider = col; return; }

            if (!col.isTrigger) return; // something solid is in the way
        }
    }

    void SetHighlight(Outline outline)
    {
        if (outline == currentOutline) return;
        if (currentOutline) currentOutline.enabled = false;
        currentOutline = outline;
        if (currentOutline) currentOutline.enabled = true;
    }

    void StartMainTask(IMainTask mainTask)
    {
        if (InMainTask) return;
        currentMainTask = mainTask;
        SetPlayerControl(false);
        currentMainTask.StartMainTask();
    }

    void StopCurrentMainTask()
    {
        if (!InMainTask) return;
        var task = currentMainTask;
        currentMainTask = null;
        task.StopMainTask();
        SetPlayerControl(true);
    }

    /// <summary>For main tasks that close themselves (e.g. homework finished).</summary>
    public void ReleaseMainTask(IMainTask task)
    {
        if (currentMainTask != task) return;
        currentMainTask = null;
        SetPlayerControl(true);
    }

    public void EnablePlayerControl() => SetPlayerControl(true);

    void SetPlayerControl(bool enabled)
    {
        if (playerMovement) playerMovement.canMove = enabled;
        if (playerCam) playerCam.canRotate = enabled;
    }

    class HitDistanceComparer : IComparer<RaycastHit>
    {
        public int Compare(RaycastHit a, RaycastHit b) => a.distance.CompareTo(b.distance);
    }
}
