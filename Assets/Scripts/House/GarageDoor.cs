using UnityEngine;

/// <summary>An up-and-over garage door: press E to swing it up under the ceiling (or back down).</summary>
public class GarageDoor : MonoBehaviour, IInteractable, IPromptProvider
{
    [Tooltip("The hinge at the top of the opening. The door panel hangs below it.")]
    public Transform hinge;
    public float openAngle = -88f;
    public float speed = 1.4f;
    public bool startOpen;

    bool open;
    float amount;

    void Start()
    {
        open = startOpen;
        amount = open ? 1f : 0f;
        Apply();
    }

    public string Prompt => open ? "Close garage door" : "Open garage door";

    public void Interact() => open = !open;

    void Update()
    {
        float target = open ? 1f : 0f;
        if (Mathf.Approximately(amount, target)) return;
        amount = Mathf.MoveTowards(amount, target, Time.deltaTime * speed);
        Apply();
    }

    void Apply()
    {
        if (!hinge) return;
        float eased = amount * amount * (3f - 2f * amount);
        hinge.localRotation = Quaternion.Euler(openAngle * eased, 0f, 0f);
    }
}
