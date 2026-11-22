using UnityEngine;

/// <summary>
/// Keeps the screen calm: by default the HUD shows only what matters right now, and holding Tab (the "find things" key) shows
/// everything.
///   - to-do list: always every note (you need the whole picture to plan)
///   - minimap: just the round map (its task list repeats the to-do notes, so it only shows with Tab)
///   - goal note: pops up for a few seconds when the goal changes, then tucks away
///   - stress / tired card: only when one of them is worth watching (rising, or over a third)
///   - recipe card: only in the kitchen, or while you're holding something for the dinner
/// </summary>
public static class HudFocus
{
    /// <summary>Everything on screen: while Tab is held (or the game is paused).</summary>
    public static bool Full => ItemFinder.Showing || PauseMenu.IsPaused;
}
