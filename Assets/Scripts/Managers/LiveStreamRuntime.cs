using UnityEngine;

/// <summary>
/// Runs <see cref="LiveStream"/> inside the house (added by the GameManager, one per day / scene load): ticks the viewers,
/// chat and tips, and ends + saves a running stream when the day is over, the scene is unloaded or the game quits.
///
/// Editor / development-build keys (free keys, nothing else uses them):
///   Insert   = install all four streaming gear items instantly
///   Delete   = remove all streaming gear (and end any stream)
///   Home     = go live / end the stream from anywhere
///   PageUp   = pretend you are playing a match (viewers grow without the headset), press again to stop
///   End      = fake kill + capture + tips (test the in-VR panel and the chat)
/// </summary>
public class LiveStreamRuntime : MonoBehaviour
{
    void Update()
    {
        if (PauseMenu.IsPaused) return;
        LiveStream.Tick(Time.deltaTime);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (Input.GetKeyDown(KeyCode.Insert))
        {
            Shop.ForceInstall(Shop.Webcam); Shop.ForceInstall(Shop.RingLight); Shop.ForceInstall(Shop.GraphicsCard); Shop.ForceInstall(Shop.StreamingPC);
            GameHUD.Toast("[debug] all streaming gear installed", GameHUD.Info);
        }
        if (Input.GetKeyDown(KeyCode.Delete))
        {
            LiveStream.End("gear removed");
            Shop.ForceRemove(Shop.Webcam); Shop.ForceRemove(Shop.RingLight); Shop.ForceRemove(Shop.GraphicsCard); Shop.ForceRemove(Shop.StreamingPC);
            GameHUD.Toast("[debug] streaming gear removed", GameHUD.Info);
        }
        if (Input.GetKeyDown(KeyCode.Home))
        {
            if (!LiveStream.Toggle()) GameHUD.Toast("[debug] " + LiveStream.CannotGoLive(), GameHUD.Info);
        }
        if (Input.GetKeyDown(KeyCode.PageUp))
        {
            LiveStream.DebugPlaying = !LiveStream.DebugPlaying;
            GameHUD.Toast("[debug] pretend-playing " + (LiveStream.DebugPlaying ? "ON" : "OFF"), GameHUD.Info);
        }
        if (Input.GetKeyDown(KeyCode.End)) LiveStream.DebugEvent();
#endif
    }

    void OnDestroy()
    {
        LiveStream.DebugPlaying = false;
        LiveStream.End("leaving the house");
    }

    void OnApplicationQuit() => LiveStream.End("the game was closed");
}
