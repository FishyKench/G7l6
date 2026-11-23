using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// One day: the clock runs from 3 PM to 6 PM (8 real minutes by default), Mom texts you along the way,
/// and at 6 she comes home through the garage and walks the house looking at whatever is still wrong.
/// Then the report card: chores done and failed, what she found, your homework and stress -> a grade.
/// If your stress maxes out first, you have a meltdown and the day ends right there.
///
/// Debug: F8 jumps the clock to 5:58 PM. On the report card: R plays the day again.
/// Created automatically by the GameManager if the scene doesn't have one.
/// </summary>
public class DayManager : MonoBehaviour
{
    public static DayManager Instance { get; private set; }

    public enum Phase { Day, MomHome, Report }

    [Header("Clock")]
    public float startHour = 15f;
    public float endHour = 18f;
    [Tooltip("How long the whole afternoon lasts in real minutes.")]
    public float realMinutes = 10f;

    [Header("Mom's texts (hour -> message)")]
    public List<Text> texts = new List<Text>
    {
        new Text { hour = 15.02f, message = "Hi sweetie! Home at 6. House clean, homework DONE. Love you" },
        new Text { hour = 16.25f, message = "How's the homework going?" },
        new Text { hour = 17f, message = "Leaving work now. Put things back where they go!" },
        new Text { hour = 17.5f, message = "30 minutes away. It better be clean." },
        new Text { hour = 17.85f, message = "Turning into our street..." },
    };

    [Header("Meltdown")]
    [Tooltip("Seconds your stress can stay maxed out before you have a meltdown (fix things or pet the cat to calm down).")]
    public float panicSeconds = 20f;

    [Header("Debug")]
    public bool debugKeys = true;
    public KeyCode skipToEveningKey = KeyCode.F8;

    [System.Serializable]
    public class Text
    {
        public float hour;
        [TextArea] public string message;
        [System.NonSerialized] public bool sent;
    }

    /// <summary>Something Mom noticed (or will, if it's still like that when she walks past).</summary>
    public class Finding
    {
        public string what;      // for the report: "TV left blaring"
        public string line;      // what Mom says when she sees it
        public int points;       // taken off the score
        public Vector3 where;
    }

    public Phase State { get; private set; }
    public float Hour => Mathf.Lerp(startHour, endHour, Mathf.Clamp01(elapsed / Mathf.Max(1f, realMinutes * 60f)));
    public float DayProgress => Mathf.Clamp01(elapsed / Mathf.Max(1f, realMinutes * 60f));
    public bool Meltdown { get; private set; }
    /// <summary>Seconds left before a meltdown while your stress is maxed out; negative when you're not panicking.</summary>
    public float PanicLeft { get; private set; } = -1f;
    public MomInspector Mom { get; private set; }

    /// <summary>What Mom actually caught on her walk.</summary>
    public readonly List<Finding> caught = new List<Finding>();
    public int ChoresDone { get; private set; }
    public int ChoresFailed { get; private set; }
    public readonly List<string> failedNames = new List<string>();
    /// <summary>Chores done really well today (perfect dinner, neatly folded laundry): bonus points on the report card.</summary>
    public readonly List<(string what, int points)> quality = new List<(string, int)>();

    /// <summary>A chore was done really well: Mom adds points on the report card.</summary>
    public static void AddQuality(string what, int points)
    {
        var d = Instance;
        if (!d || d.State == Phase.Report) return;
        d.quality.Add((what, points));
    }

    float elapsed;
    DayHud hud;

    void Awake()
    {
        Instance = this;
        TaskBase.Ended += OnTaskEnded;
        ApplyCampaign();
    }

    /// <summary>The week campaign decides when Mom gets home and how long the day lasts (Friday: early). Also moves the day on.</summary>
    void ApplyCampaign()
    {
        Campaign.BeginDay(gameObject.scene);
        var rules = Campaign.Today;
        endHour = rules.momHomeHour;
        if (rules.realMinutes > 0f) realMinutes = rules.realMinutes;
        if (Trust.LateCurfew)
        {
            // Mom trusts you: she stays out half an hour longer (the day gets longer in real time too).
            float before = endHour - startHour;
            endHour += 0.5f;
            realMinutes *= (endHour - startHour) / Mathf.Max(0.5f, before);
        }
        if (Mathf.Abs(endHour - 18f) > 0.01f)
        {
            // Mom's "on my way" texts move with her arrival.
            float shift = 18f - endHour;
            foreach (var t in texts)
            {
                if (t.hour >= 17f) t.hour -= shift;
                if (t.message != null) t.message = t.message.Replace("Home at 6.", "Home at " + Campaign.HourText(endHour) + ".");
            }
        }
    }

    void OnDestroy()
    {
        TaskBase.Ended -= OnTaskEnded;
        if (Instance == this) Instance = null;
    }

    void Start()
    {
        Time.timeScale = 1f;
        Incidents.Clear(); // (a new day: yesterday's damage is fixed)
        Power.Reset();
        hud = gameObject.AddComponent<DayHud>();
        MomNav.Bake(); // Mom's walking map, baked in the background long before 6 PM
    }

    void OnTaskEnded(TaskBase task, bool success)
    {
        if (State == Phase.Report || task is MatchTask) return; // the match is scored on its own
        if (success) ChoresDone++;
        else
        {
            ChoresFailed++;
            failedNames.Add(task.taskName);
        }
    }

    void Update()
    {
        if (PauseMenu.IsPaused) return;
        if (State == Phase.Report)
        {
            if (Input.GetKeyDown(KeyCode.R) && !CampaignUI.SummaryOpen) PlayAgain();
            return;
        }

        if (debugKeys && Input.GetKeyDown(skipToEveningKey)) SkipToEvening();

        UpdatePanic();
        if (State != Phase.Day) return;
        elapsed += Time.deltaTime;

        foreach (var t in texts)
            if (!t.sent && Hour >= t.hour)
            {
                t.sent = true;
                hud.Text("MOM", t.message);
            }

        if (Hour >= endHour - 0.0001f) StartCoroutine(MomComesHome());
    }

    /// <summary>Maxed-out stress starts a countdown; get back under 90% to stop it, run out and it's a meltdown.</summary>
    void UpdatePanic()
    {
        var stress = StressManager.Instance;
        if (!stress) return;
        if (stress.Normalized >= 0.995f)
        {
            if (PanicLeft < 0f) PanicLeft = panicSeconds;
            PanicLeft -= Time.deltaTime;
            if (PanicLeft <= 0f) HaveMeltdown();
        }
        else if (stress.Normalized < 0.9f && PanicLeft >= 0f)
        {
            PanicLeft = -1f;
            GameHUD.Toast("Phew... breathing again.", GameHUD.Good);
        }
    }

    // ------------------------------------------------------------------ the end of the day

    /// <summary>Debug (F8 / the Mom debug panel): the clock jumps to just before 6 PM.</summary>
    public void SkipToEvening()
    {
        if (State == Phase.Day) elapsed = Mathf.Max(elapsed, (17.97f - startHour) / (endHour - startHour) * realMinutes * 60f);
    }

    IEnumerator MomComesHome()
    {
        foreach (var other in FindObjectsOfType<MomInspector>()) Destroy(other.gameObject); // (a Mom from the debug panel)
        State = Phase.MomHome;
        if (GameManager.Instance) GameManager.Instance.spawningPaused = true;
        GameHUD.Toast("Mom's home!", GameHUD.Warn);
        MatchTask.TakeOffHeadset("Mom pulls your VR headset off!");

        var garage = FindObjectOfType<GarageDoor>();
        if (garage) garage.SetOpen(true);
        yield return new WaitForSeconds(1.5f);

        Mom = MomInspector.Spawn(this);
        if (Mom)
        {
            yield return Mom.Inspect();
            // a bad day (below a B): she smacks you before the report card
            string g = PreviewGrade();
            if (!Meltdown && g.Length > 0 && (g[0] == 'C' || g[0] == 'D' || g[0] == 'F')) yield return Mom.Smack();
        }
        else
        {
            // No house to walk: she just takes one look around.
            foreach (var f in CurrentFindings()) caught.Add(f);
            yield return new WaitForSeconds(1.5f);
        }
        EndDay();
    }

    /// <summary>A text message notification from someone (e.g. "MOM", or your friend about the match).</summary>
    public void SendText(string from, string message)
    {
        if (hud) hud.Text(from, message);
    }

    /// <summary>Called by the stress meter when it fills up.</summary>
    public void HaveMeltdown()
    {
        if (State == Phase.Report) return;
        Meltdown = true;
        StopAllCoroutines();
        if (Mom) Mom.Stop();
        foreach (var f in CurrentFindings()) caught.Add(f);
        EndDay();
    }

    void EndDay()
    {
        if (State == Phase.Report) return;
        MatchTask.TakeOffHeadset(null);
        State = Phase.Report;
        if (GameManager.Instance) GameManager.Instance.spawningPaused = true;
        var report = Grade();
        hud.ShowReport(report);
        FreezePlayer();
        Time.timeScale = 0f;
    }

    void FreezePlayer()
    {
        var interaction = PlayerInteraction.Instance;
        if (interaction) interaction.enabled = false;
        foreach (var m in FindObjectsOfType<PlayerMovementAdvanced>()) m.enabled = false;
        foreach (var c in FindObjectsOfType<PlayerCam>()) c.enabled = false;
    }

    /// <summary>R on the report card: the next day - or, after Friday, the week summary (its buttons start the next week).</summary>
    void PlayAgain()
    {
        if (Campaign.IsFriday && Campaign.DayFinished)
        {
            var runtime = CampaignRuntime.Instance;
            if (runtime) { runtime.ShowWeekSummary(); return; }
        }
        RestartDay();
    }

    /// <summary>Starts the day over (reloads the scene).</summary>
    public static void RestartDay()
    {
        Time.timeScale = 1f;
        AudioListener.pause = false;
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex >= 0 ? SceneManager.GetActiveScene().buildIndex : 0);
    }

    // ------------------------------------------------------------------ what's wrong right now

    /// <summary>Everything Mom would complain about if she saw the whole house right now.</summary>
    public List<Finding> CurrentFindings()
    {
        var list = new List<Finding>();
        var gm = GameManager.Instance;
        if (gm)
        {
            foreach (var task in gm.Tasks)
            {
                if (!task || !task.IsActive) continue;
                var f = FindingFor(task);
                if (f != null) list.Add(f);
            }
        }

        var carry = PlayerCarry.Instance;
        foreach (var item in FindObjectsOfType<Carryable>())
        {
            if (!item || item.type == ItemType.DirtyClothes || item.type == ItemType.CleanClothes || item.type == ItemType.Food || !item.gameObject.activeInHierarchy) continue;
            bool held = carry && carry.Held == item;
            if (!held && item.IsHome) continue;
            list.Add(new Finding
            {
                what = $"The {item.displayName} wasn't put back",
                line = held ? $"Why are you holding the {item.displayName}?" : $"Why is the {item.displayName} here?",
                points = 4,
                where = item.transform.position,
            });
        }
        // damage that's done (the sofa the cat shredded, the TV the leak fried, dinner the cat ate)
        foreach (var inc in Incidents.All)
            list.Add(new Finding { what = inc.what, line = inc.line, points = inc.points, where = inc.where });
        float strict = Campaign.Today.momStrict; // Thursday's guests, Friday, harder weeks
        if (Mathf.Abs(strict - 1f) > 0.01f)
            foreach (var f in list) f.points = Mathf.Max(1, Mathf.RoundToInt(f.points * strict));
        return list;
    }

    static Finding FindingFor(TaskBase task)
    {
        var f = new Finding { where = task.Where };
        switch (task)
        {
            case MatchTask _:
                return null; // Mom doesn't grade your match
            case TypingTask homework:
                f.what = "Homework not finished";
                f.line = "Homework. Not. Done.";
                f.points = Mathf.RoundToInt(30f * (1f - homework.Progress));
                if (f.points <= 0) return null;
                break;
            case FireHazard _: f.what = "The kitchen was ON FIRE"; f.line = "Is that a FIRE?!"; f.points = 25; break;
            case Spill _: f.what = "A drink spilled on the floor"; f.line = "What's this puddle?!"; f.points = 6; break;
            case FloodHazard _: f.what = "Water all over the floor"; f.line = "Why is the floor WET?!"; f.points = 15; break;
            case PawPrints _: f.what = "Wet paw prints all over the house"; f.line = "Paw prints EVERYWHERE?!"; f.points = 6; break;
            case FuseBox _: f.what = "The power was out"; f.line = "Why are all the lights off?!"; f.points = 10; break;
            case CeilingLeakTask _: f.what = "Water coming through the ceiling"; f.line = "Water from the CEILING?!"; f.points = 15; break;
            case TvTask _: f.what = $"{task.taskName} blaring static"; f.line = "Turn that TV OFF."; f.points = 8; break;
            case OvenTask oven: f.what = "Dinner not made"; f.line = oven.Progress >= 1f ? "Something's burning..." : "Where's dinner?"; f.points = 12; break;
            case Cooking _: f.what = "Dinner not made"; f.line = "Where's dinner?"; f.points = 12; break;
            case LaundryTask _: f.what = "Laundry not done"; f.line = "The laundry's still not done?"; f.points = 10; break;
            default: f.what = task.taskName + " not done"; f.line = "What's this?"; f.points = 10; break;
        }
        return f;
    }

    // ------------------------------------------------------------------ the grade

    public class Report
    {
        public bool meltdown;
        public string homework;
        public int homeworkPoints;
        public int done, failed, choreBonus, failPenalty;
        /// <summary>Bonus for chores done really well (capped at 12) and how many.</summary>
        public int qualityPoints, qualityCount;
        public List<Finding> found;
        public int stress, stressPenalty;
        public int score;
        public string grade, momSays;
        /// <summary>The ranked match: "WON 3-1", "LOST 1-3", "unfinished 1-1", "never showed up"; null if there's no match.</summary>
        public string match;
        public int rankPoints;
        public bool matchWon;
        /// <summary>Dollars Mom pays for this grade (A $25, B $15, C $8, D $3, F $0).</summary>
        public int allowance;
        /// <summary>Mom's Trust after this day, and how much this report card moved it.</summary>
        public int trust, trustDelta;
        /// <summary>"Ranked match" or "Tournament"; and the right-hand text ("+85 RP" or "1st place - $150").</summary>
        public string matchLabel = "Ranked match", matchExtra;
    }

    /// <summary>Allowance for a letter grade ("A+" counts as A).</summary>
    public static int AllowanceFor(string grade)
    {
        switch (grade != null && grade.Length > 0 ? grade[0] : 'F')
        {
            case 'A': return 25;
            case 'B': return 15;
            case 'C': return 8;
            case 'D': return 3;
            default: return 0;
        }
    }

    /// <summary>The grade as it stands right now, nothing paid or saved (Mom decides on the spot whether you've earned a smack).</summary>
    public string PreviewGrade() => Score(out _, out _).grade;

    Report Grade()
    {
        var r = Score(out var homework, out var match);
        return Settle(r, homework, match);
    }

    /// <summary>Works out the report card - no side effects (nothing paid, nothing saved).</summary>
    Report Score(out TypingTask homework, out MatchTask match)
    {
        var r = new Report { meltdown = Meltdown, found = new List<Finding>(caught) };

        homework = FindObjectOfType<TypingTask>();
        if (homework)
        {
            int pages = Mathf.Max(1, homework.pagesRequired);
            bool finished = !homework.IsActive && homework.LastSucceeded;
            int typed = finished ? pages : Mathf.Clamp(Mathf.FloorToInt(homework.Progress * pages + 0.0001f), 0, pages);
            r.homework = $"{typed}/{pages} {homework.UnitName}";
            // If Mom saw the unfinished homework it's already in 'found'; otherwise count it here.
            bool alreadyFound = r.found.Exists(f => f.what == "Homework not finished");
            r.homeworkPoints = finished || alreadyFound ? 0 : Mathf.RoundToInt(30f * (1f - (float)typed / pages));
        }
        else r.homework = "-";

        match = FindObjectOfType<MatchTask>();
        if (match)
        {
            r.rankPoints = match.RankPoints;
            r.matchWon = match.State == MatchTask.Phase.Over && match.Wins >= match.roundsToWin;
            r.match = match.State == MatchTask.Phase.Over ? $"{(r.matchWon ? "WON" : "LOST")} {match.Wins}-{match.Losses}"
                    : match.State == MatchTask.Phase.Waiting ? "never started"
                    : $"unfinished {match.Wins}-{match.Losses}";
        }

        r.done = ChoresDone;
        r.failed = ChoresFailed;
        r.choreBonus = Mathf.Min(15, ChoresDone * 3);
        foreach (var q in quality) r.qualityPoints += q.points;
        r.qualityPoints = Mathf.Min(12, r.qualityPoints);
        r.qualityCount = quality.Count;
        r.failPenalty = ChoresFailed * 6;
        var stress = StressManager.Instance;
        r.stress = stress ? Mathf.RoundToInt(stress.Normalized * 100f) : 0;
        r.stressPenalty = r.stress > 70 ? 5 : 0;

        int found = 0;
        foreach (var f in r.found) found += f.points;
        r.score = Mathf.Clamp(100 + r.choreBonus + r.qualityPoints - r.failPenalty - found - r.homeworkPoints - r.stressPenalty, 0, 115);
        if (r.meltdown) r.score = Mathf.Min(r.score, 40);

        (r.grade, r.momSays) =
            r.meltdown ? ("F", "...Sweetie? Why are you lying on the floor?") :
            r.score >= 100 ? ("A+", "Who are you and what did you do with my kid?") :
            r.score >= 90 ? ("A", "Look at this place! Pizza tonight.") :
            r.score >= 80 ? ("B", "Not bad. Not great. But not bad.") :
            r.score >= 70 ? ("C", "We need to talk about this house.") :
            r.score >= 60 ? ("D", "Did a tornado come through here?") :
            ("F", "Phone. Now. You're grounded.");

        return r;
    }

    /// <summary>The report card's consequences: allowance, tomorrow's PC ban, Mom's trust, the campaign's record.</summary>
    Report Settle(Report r, TypingTask homework, MatchTask match)
    {
        r.allowance = AllowanceFor(r.grade);
        if (r.allowance > 0) Wallet.Earn(r.allowance, $"Allowance - Mom's report card ({r.grade})", Wallet.Category.Allowance);
        Wallet.PcBannedTomorrow = r.grade == "F"; // no streaming the next day (LiveStream.BannedToday)
        bool homeworkDone = !homework || (!homework.IsActive && homework.LastSucceeded);
        r.trustDelta = Trust.EndOfDay(r.grade, ChoresFailed, homeworkDone);
        r.trust = Trust.Value;

        bool tournament = Campaign.TournamentToday;
        Campaign.OnDayEnded(r.grade, r.match, match ? match.Wins : 0, match ? match.Losses : 0, match && match.State != MatchTask.Phase.Waiting);
        if (match)
        {
            r.matchLabel = tournament ? "Tournament" : "Ranked match";
            var outcome = Campaign.TournamentOutcome;
            r.matchExtra = tournament
                ? (outcome == Campaign.TournamentResult.First ? $"1st - ${Campaign.Prize1st}" : outcome == Campaign.TournamentResult.Second ? $"2nd - ${Campaign.Prize2nd}" : "no prize")
                : !string.IsNullOrEmpty(Campaign.LastPromoText) ? Campaign.LastPromoText
                : $"{(r.rankPoints >= 0 ? "+" : "")}{r.rankPoints} RP";
        }
        return r;
    }
}
