using UnityEngine;

/// <summary>
/// On the main menu's copy of your room: a piece that's only in your room once you've earned it (the same RoomUnlock key as in
/// the house - your setup tier, the webcam, your rank, followers...). Shown or hidden when the menu loads, so the menu shows the
/// room you actually have.
/// </summary>
public class MenuUnlock : MonoBehaviour
{
    public string key = "always";

    void Awake() => gameObject.SetActive(RoomUnlock.Has(key));
}
