using UnityEngine;

/// <summary>
/// Put on a thing in your room that should only be there once you've earned it (or only BEFORE you've earned something, with a
/// "!" in front of the key). RoomProgress shows / hides it. Keys:
///   webcam, ringlight, gpu, streampc            - bought on ShopNest and installed
///   setup1 (graphics card or streaming PC), setup2 (streaming PC) - the desk setup tiers
///   silver, gold, diamond                       - Ranked rank reached
///   f100, f500, f1000                           - ClipHub followers
///   cup1, cup2, final                           - trophies: 1+ cups, 2+ cups, a Season Final podium
///   always                                      - always there
///   a+b                                         - both (e.g. "gpu+!streampc" = the middle setup tier)
/// Examples: the moving boxes are "!webcam" (gone once you start streaming), the old chair is "!gold".
/// </summary>
public class RoomUnlock : MonoBehaviour
{
    public string key = "always";
    [Tooltip("Only hide its looks (renderers), keep the object and its colliders - for things other code lives on, like your desk.")]
    public bool lookOnly;

    /// <summary>Shows or hides it (the whole object, or just its renderers when lookOnly).</summary>
    public void Show(bool on)
    {
        if (!lookOnly) { if (gameObject.activeSelf != on) gameObject.SetActive(on); return; }
        foreach (var r in GetComponentsInChildren<Renderer>(true))
        {
            var owner = r.GetComponentInParent<RoomUnlock>();
            if (owner == this && r.enabled != on) r.enabled = on;
        }
    }

    /// <summary>"a+b" = both; "!a" = not a.</summary>
    public static bool Has(string key)
    {
        if (string.IsNullOrEmpty(key) || key == "always") return true;
        if (key.Contains("+"))
        {
            foreach (var part in key.Split('+')) if (!Has(part.Trim())) return false;
            return true;
        }
        if (key[0] == '!') return !Has(key.Substring(1));
        switch (key)
        {
            case "webcam": return Shop.IsInstalled(Shop.Webcam);
            case "ringlight": return Shop.IsInstalled(Shop.RingLight);
            case "gpu": return Shop.IsInstalled(Shop.GraphicsCard);
            case "streampc": return Shop.IsInstalled(Shop.StreamingPC);
            case "setup1": return Shop.IsInstalled(Shop.GraphicsCard) || Shop.IsInstalled(Shop.StreamingPC); // your gaming setup (or better)
            case "setup2": return Shop.IsInstalled(Shop.StreamingPC);                                        // the pro setup
            case "silver": return Campaign.CurrentRank >= Campaign.Rank.Silver;
            case "gold": return Campaign.CurrentRank >= Campaign.Rank.Gold;
            case "diamond": return Campaign.CurrentRank >= Campaign.Rank.Diamond;
            case "f100": return ClipHub.Followers >= 100;
            case "f500": return ClipHub.Followers >= 500;
            case "f1000": return ClipHub.Followers >= 1000;
            case "cup1": return Trophies.All.Count >= 1;
            case "cup2": return Trophies.All.Count >= 2;
            case "final":
                foreach (var t in Trophies.All) if (t.kind == Trophies.Kind.FinalCup) return true;
                return false;
        }
        return true;
    }
}
