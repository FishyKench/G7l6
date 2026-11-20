/// <summary>Anything the player can press E on.</summary>
public interface IInteractable
{
    void Interact();
}

/// <summary>
/// Optional: text shown under the crosshair when looking at the object, e.g. "Turn on oven".
/// Return null or empty to show nothing (the object is also not highlighted then).
/// </summary>
public interface IPromptProvider
{
    string Prompt { get; }
}
