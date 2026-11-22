using UnityEngine;

/// <summary>
/// Runs ClipHub inside the house (added by the GameManager, one per day / scene load): lets game time pass so
/// views, likes and followers trickle in, pays partner earnings, and shows toasts / a MyMail note for the big moments.
/// A new day (scene load) is "overnight": a lot of views come in while you sleep.
///
/// Editor / development-build keys:  F11 = make up a recorded round (so you can test the Clip Studio without
/// playing VR),  Shift+F11 = delete all recordings,  F12 = +3 game hours on ClipHub (views trickle in),
/// Shift+F12 = +1 day (overnight) and Ctrl+F12 = forget the channel.
/// </summary>
public class ClipHubRuntime : MonoBehaviour
{
    float acc;

    void Awake()
    {
        ClipHub.Paid += OnPaid;
        ClipHub.BecamePartner += OnPartner;
        ClipHub.Milestone += OnMilestone;
    }

    System.Collections.IEnumerator Start()
    {
        yield return new WaitForSeconds(3f); // let the HUD come up so the toasts show
        if (ClipHub.VideoCount > 0 && Campaign.Once($"d{Campaign.DayIndex}.overnight")) ClipHub.Advance(ClipHub.OvernightMinutes); // a new day: views came in overnight
    }

    void OnDestroy()
    {
        ClipHub.Paid -= OnPaid;
        ClipHub.BecamePartner -= OnPartner;
        ClipHub.Milestone -= OnMilestone;
        ClipHub.Flush();
    }

    void OnApplicationPause(bool paused) { if (paused) ClipHub.Flush(); }
    void OnApplicationQuit() => ClipHub.Flush();

    float MinutesPerSecond
    {
        get
        {
            var day = DayManager.Instance;
            if (!day) return 0.3f;
            return (day.endHour - day.startHour) * 60f / Mathf.Max(1f, day.realMinutes * 60f);
        }
    }

    void Update()
    {
        if (PauseMenu.IsPaused) return;
        var day = DayManager.Instance;
        if (!day || day.State != DayManager.Phase.Report)
        {
            acc += Time.deltaTime;
            if (acc >= 1f)
            {
                ClipHub.Advance(acc * MinutesPerSecond);
                acc = 0f;
            }
        }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
        if (Input.GetKeyDown(KeyCode.F11))
        {
            if (shift) { Replay.Clear(); GameHUD.Toast("[debug] recordings deleted", GameHUD.Info); }
            else
            {
                var r = ReplaySynth.Make();
                GameHUD.Toast(r != null ? "[debug] made up a recorded round - open the Clip Studio" : "[debug] couldn't make a round (arena not ready)", GameHUD.Info);
            }
        }
        if (Input.GetKeyDown(KeyCode.F12))
        {
            if (ctrl) { ClipHub.ResetAll(); GameHUD.Toast("[debug] ClipHub channel reset", GameHUD.Info); }
            else
            {
                ClipHub.Advance(shift ? ClipHub.OvernightMinutes : 180f);
                GameHUD.Toast(shift ? "[debug] ClipHub: +1 day" : "[debug] ClipHub: +3 hours", GameHUD.Info);
            }
        }
#endif
    }

    void OnPaid(int dollars, int views)
    {
        GameHUD.Toast($"ClipHub paid you ${dollars} for {views} views", GameHUD.Good);
    }

    void OnPartner()
    {
        GameHUD.Toast("You are a ClipHub Partner! Views now pay.", GameHUD.Good);
        Mail.Send("ClipHub Team", "You are now a ClipHub Partner!",
            $"Congratulations, {ClipHub.ChannelName}! You reached {ClipHub.PartnerFollowers} followers. From now on every {ClipHub.ViewsPerDollar} views on your clips pay $1 straight into your account. Keep uploading your best Ranked Arena moments.",
            Mail.Kind.Info);
    }

    void OnMilestone(ClipHub.Video v, int views)
    {
        string t = v.title.Length > 28 ? v.title.Substring(0, 28) + "..." : v.title;
        GameHUD.Toast($"\"{t}\" hit {views:N0} views", GameHUD.Info);
    }
}
