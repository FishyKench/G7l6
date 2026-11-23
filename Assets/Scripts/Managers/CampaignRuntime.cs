using System.Collections;
using UnityEngine;

/// <summary>
/// Runs the week campaign inside the house (added first by GameManager.Awake, one per day / scene load):
///   - applies today's <see cref="Campaign.DayRules"/> to the existing systems one frame after every Start() has run
///     (random task count / pace, ceiling leaks, oven burn time, leak speed, cat mischief, homework pages, match start time,
///     bot sharpness, tournament settings). Mom's arrival time, speed and strictness are read by DayManager / MomInspector directly,
///   - shows the day intro card,
///   - sends the day's mails (Ranked League / Jay / Mom) and a few Monday tips,
///   - the Tuesday scrim bonus (+15 RP for playing the warm-up a while),
///   - keeps <see cref="Campaign.TournamentLive"/> up to date (LiveStream doubles the viewers while it is true),
///   - debug keys (editor / development build only, see the step 6 report).
///
/// Debug keys:  PageDown = next day (reload), Shift+PageDown = previous day, Ctrl+PageDown = Friday.
///              Ctrl+Equals = +50 RP, Ctrl+Minus = -50 RP, Ctrl+Backslash = rating exactly at the tournament gate.
///              Ctrl+Slash = open the week summary now, Ctrl+Comma = show the day intro again.
///              Ctrl+End = jump to this season's Season Final (week 3, Friday), Ctrl+Period = preview the season ending now.
/// </summary>
public class CampaignRuntime : MonoBehaviour
{
    public static CampaignRuntime Instance { get; private set; }

    Campaign.DayRules rules;
    CampaignUI ui;
    MatchTask match;
    float scrimSeconds;
    bool scrimPaid, scrimHinted;
    readonly bool[] mailsSent = new bool[8];
    bool tip1, tip2, tip3;

    void Awake()
    {
        Instance = this;
        Campaign.BeginDay(gameObject.scene);
        rules = Campaign.Today;
        ui = gameObject.AddComponent<CampaignUI>();
    }

    void OnDestroy()
    {
        Campaign.TournamentLive = false;
        if (Instance == this) Instance = null;
    }

    void Start() => StartCoroutine(Setup());

    IEnumerator Setup()
    {
        yield return null; // every Start() has run, the scene's own values are in place
        ApplyRules();
        yield return new WaitForSeconds(1.2f);
        while (Onboarding.Showing) yield return null; // (the first-time intro comes first)
        yield return new WaitForSeconds(0.3f);
        ui.ShowIntro(rules);
        if (LiveStream.BannedToday)
        {
            yield return new WaitForSeconds(3f);
            GameHUD.Toast("Grounded today (that F): Mom took your streaming privileges.", GameHUD.Warn);
        }
    }

    // ------------------------------------------------------------------ the knobs

    void ApplyRules()
    {
        var gm = GameManager.Instance;
        if (gm)
        {
            gm.maxActiveTasks = Mathf.Max(1, Mathf.RoundToInt(gm.maxActiveTasks * rules.taskCountMult));
            gm.timeBetweenTasks = new Vector2(gm.timeBetweenTasks.x * rules.taskIntervalMult, gm.timeBetweenTasks.y * rules.taskIntervalMult);
        }

        var cat = CatBrain.Instance;
        if (cat && rules.catMischiefMult > 0.01f)
            cat.timeBetweenMischief = new Vector2(cat.timeBetweenMischief.x / rules.catMischiefMult, cat.timeBetweenMischief.y / rules.catMischiefMult);

        // (the homework sets its own parts when it starts: maths, typing or both)
        foreach (var oven in FindObjectsOfType<OvenTask>(true)) oven.catchFireAt *= rules.ovenBurnMult;
        foreach (var leak in FindObjectsOfType<CeilingLeakTask>(true)) leak.puddleGrowTime *= rules.leakGrowMult;

        match = FindObjectOfType<MatchTask>();
        if (match)
        {
            match.matchStartHour = rules.matchStartHour;
            float s = Mathf.Max(0.5f, rules.botSkill);
            match.enemyMissDegrees /= s;
            match.enemyReactTime /= s;
            match.enemyAimSettle /= s;
            match.enemyShotsPerSecond *= 1f + (s - 1f) * 0.5f;
            match.enemyHitDamage *= 1f + (s - 1f) * 0.3f;
            if (rules.tournament)
            {
                match.breakLength = 30f; // best of 5 has to fit before Mom gets home early
                match.taskName = Campaign.TournamentName;
            }
        }
    }

    // ------------------------------------------------------------------ the day

    void Update()
    {
        if (PauseMenu.IsPaused) return;
        var day = DayManager.Instance;
        if (!day) return;

        Campaign.TournamentLive = Campaign.TournamentToday && match && (match.State == MatchTask.Phase.Round || match.State == MatchTask.Phase.Break);

        if (day.State == DayManager.Phase.Day)
        {
            Mails(day.Hour);
            Story.Tick(day.Hour);
            Tips(day.Hour);
            Scrim(day.Hour);
        }
        DebugKeys();
    }

    void Mails(float hour)
    {
        int d = Campaign.DayIndex;
        if (hour < 15.08f || mailsSent[d]) return;
        mailsSent[d] = true;
        string home = Campaign.HourText(rules.momHomeHour);
        switch (d)
        {
            case 0:
                if (Campaign.Once("league"))
                    Mail.Send("Ranked League", $"Welcome to week {Campaign.Week} of the Ranked League",
                        "Every ranked match you play moves your rating: +25 for each round you win, -20 for each round you lose and +30 for winning the match.\n\n" +
                        "Ranks: Bronze, Silver, Gold, Diamond and Champion, 100 points each.\n\n" +
                        $"Reach {Campaign.RankName(Campaign.GateRank)} ({Campaign.GateRating} RP) by Thursday night to qualify for Friday's tournament: 1st place wins ${Campaign.Prize1st}, 2nd place ${Campaign.Prize2nd}.\n\n" +
                        "Mondays are quiet, so start climbing. Your ranked match begins at " + Campaign.HourText(rules.matchStartHour) + ".\n\n- The Ranked League",
                        Mail.Kind.Info);
                break;
            case 1:
                if (Campaign.Once("scrim"))
                    Mail.Send("Jay", "Scrim before ranked?",
                        "Hey! A few of us are running a scrim in the warm-up lobby before ranked starts. Jump in with your headset (launch Ranked Arena on the PC first) and play for a bit.\n\n" +
                        $"The league gives +{Campaign.ScrimBonus} rank points to anyone who plays a scrim. Easy points!\n\n- Jay",
                        Mail.Kind.Invite);
                break;
            case 2:
                if (Campaign.Once("wed"))
                    Mail.Send("Mom", "Extra homework today",
                        $"Sweetie, your teacher sent some hard maths homework this time. Please get it all done before I get home at {home}.\n\nLove, Mom",
                        Mail.Kind.Info);
                break;
            case 3:
                if (Campaign.Once("thu"))
                    Mail.Send("Mom", "Guests tonight!",
                        $"Guests are coming for dinner tonight, so the house must be SPOTLESS when I get home at {home}. I will check every room very carefully. No exceptions!\n\nLove, Mom",
                        Mail.Kind.Info);
                break;
            default:
                if (Campaign.Once("fri"))
                {
                    if (rules.tournament)
                        Mail.Send("Ranked League", "You are in: tournament today!",
                            $"You qualified with {Campaign.Rating} RP. The tournament starts at {Campaign.HourText(rules.matchStartHour)}: best of 5, tougher opponents. Win the match for 1st place (${Campaign.Prize1st}) or win at least two rounds for 2nd place (${Campaign.Prize2nd}).\n\nLaunch Ranked Arena on your PC before it starts. Good luck!\n\n- The Ranked League",
                            Mail.Kind.Invite);
                    else
                        Mail.Send("Ranked League", "Tournament: not qualified",
                            $"The tournament needed {Campaign.RankName(Campaign.GateRank)} ({Campaign.GateRating} RP) and you finished the week on {Campaign.Rating} RP. You can still play the normal ranked match today.\n\n- The Ranked League",
                            Mail.Kind.Info);
                }
                break;
        }
    }

    void Tips(float hour)
    {
        // A few gentle hints on the very first Monday only.
        if (Campaign.DayIndex != 0 || Campaign.Week != 1) return;
        if (!tip1 && hour >= 15.15f && Campaign.Once("tip1")) { tip1 = true; GameHUD.Toast("Tip: hold TAB to see where the important things are.", GameHUD.Info); }
        if (!tip2 && hour >= 15.45f && Campaign.Once("tip2")) { tip2 = true; GameHUD.Toast("Tip: sit at your PC to read mail and launch Ranked Arena.", GameHUD.Info); }
        if (!tip3 && hour >= 15.75f && Campaign.Once("tip3")) { tip3 = true; GameHUD.Toast("Tip: ranked starts at 4:00. Put things back before Mom checks!", GameHUD.Info); }
    }

    /// <summary>Tuesday: play the warm-up for 45 seconds before the ranked match and the league gives +15 RP.</summary>
    void Scrim(float hour)
    {
        if (!rules.scrim || scrimPaid || !match || match.State != MatchTask.Phase.Waiting || hour < 15.1f) return;
        if (!MatchTask.InVR) return;
        scrimSeconds += Time.deltaTime;
        if (!scrimHinted && scrimSeconds > 12f)
        {
            scrimHinted = true;
            GameHUD.Toast("Scrim: keep playing the warm-up for bonus rank points.", GameHUD.Info);
        }
        if (scrimSeconds >= 45f)
        {
            scrimPaid = true;
            if (Campaign.Once("scrimpaid"))
            {
                Campaign.AddRating(Campaign.ScrimBonus, "Scrim played");
                var day = DayManager.Instance;
                if (day) day.SendText("JAY", "nice scrim!! the league gave you bonus points");
            }
        }
    }

    // ------------------------------------------------------------------ public

    public void ShowWeekSummary() => ui.ShowSummary();

    // ------------------------------------------------------------------ debug

    void DebugKeys()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
        bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        if (Input.GetKeyDown(KeyCode.PageDown))
        {
            int target = ctrl ? Campaign.Friday : Mathf.Clamp(Campaign.DayIndex + (shift ? -1 : 1), 0, Campaign.Friday);
            Campaign.JumpTo(target);
            DayManager.RestartDay();
            return;
        }
        if (!ctrl) return;
        if (Input.GetKeyDown(KeyCode.Equals)) Campaign.AddRating(50, "[debug]");
        if (Input.GetKeyDown(KeyCode.Minus)) Campaign.AddRating(-50, "[debug]");
        if (Input.GetKeyDown(KeyCode.Backslash)) { Campaign.SetRating(Campaign.GateRating); GameHUD.Toast("[debug] rating = " + Campaign.RankText, GameHUD.Info); }
        if (Input.GetKeyDown(KeyCode.Slash)) ui.ShowSummary();
        if (Input.GetKeyDown(KeyCode.Comma)) ui.ShowIntro(rules);
        if (Input.GetKeyDown(KeyCode.Period)) ui.ShowEnding(); // preview the season ending you'd get right now
        if (Input.GetKeyDown(KeyCode.End)) { Campaign.JumpToSeasonFinal(); DayManager.RestartDay(); }
#endif
    }
}
