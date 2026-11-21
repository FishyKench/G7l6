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

    float elapsed;
    DayHud hud;

    void Awake()
    {
        Instance = this;
        TaskBase.Ended += OnTaskEnded;
    }

    void OnDestroy()
    {
        TaskBase.Ended -= OnTaskEnded;
        if (Instance == this) Instance = null;
    }

    void Start()
    {
        Time.timeScale = 1f;
        hud = gameObject.AddComponent<DayHud>();
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
            if (Input.GetKeyDown(KeyCode.R)) PlayAgain();
            return;
        }

        if (debugKeys && Input.GetKeyDown(skipToEveningKey) && State == Phase.Day)
            elapsed = Mathf.Max(elapsed, (17.97f - startHour) / (endHour - startHour) * realMinutes * 60f);

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

    IEnumerator MomComesHome()
    {
        State = Phase.MomHome;
        if (GameManager.Instance) GameManager.Instance.spawningPaused = true;
        GameHUD.Toast("Mom's home!", GameHUD.Warn);
        MatchTask.TakeOffHeadset("Mom pulls your VR headset off!");

        var garage = FindObjectOfType<GarageDoor>();
        if (garage) garage.SetOpen(true);
        yield return new WaitForSeconds(1.5f);

        Mom = MomInspector.Spawn(this);
        if (Mom) yield return Mom.Inspect();
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

    void PlayAgain() => RestartDay();

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
            if (!item || item.type == ItemType.DirtyClothes || item.type == ItemType.CleanClothes || !item.gameObject.activeInHierarchy) continue;
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
            case FloodHazard _: f.what = "Water all over the floor"; f.line = "Why is the floor WET?!"; f.points = 15; break;
            case CeilingLeakTask _: f.what = "Water coming through the ceiling"; f.line = "Water from the CEILING?!"; f.points = 15; break;
            case TvTask _: f.what = $"{task.taskName} blaring static"; f.line = "Turn that TV OFF."; f.points = 8; break;
            case OvenTask oven: f.what = "Dinner not made"; f.line = oven.Progress >= 1f ? "Something's burning..." : "Where's dinner?"; f.points = 12; break;
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
        public List<Finding> found;
        public int stress, stressPenalty;
        public int score;
        public string grade, momSays;
        /// <summary>The ranked match: "WON 3-1", "LOST 1-3", "unfinished 1-1", "never showed up"; null if there's no match.</summary>
        public string match;
        public int rankPoints;
        public bool matchWon;
    }

    Report Grade()
    {
        var r = new Report { meltdown = Meltdown, found = new List<Finding>(caught) };

        var homework = FindObjectOfType<TypingTask>();
        if (homework)
        {
            int pages = Mathf.Max(1, homework.pagesRequired);
            bool finished = !homework.IsActive && homework.LastSucceeded;
            int typed = finished ? pages : Mathf.Clamp(Mathf.FloorToInt(homework.Progress * pages + 0.0001f), 0, pages);
            r.homework = $"{typed}/{pages} pages";
            // If Mom saw the unfinished homework it's already in 'found'; otherwise count it here.
            bool alreadyFound = r.found.Exists(f => f.what == "Homework not finished");
            r.homeworkPoints = finished || alreadyFound ? 0 : Mathf.RoundToInt(30f * (1f - (float)typed / pages));
        }
        else r.homework = "-";

        var match = FindObjectOfType<MatchTask>();
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
        r.failPenalty = ChoresFailed * 6;
        var stress = StressManager.Instance;
        r.stress = stress ? Mathf.RoundToInt(stress.Normalized * 100f) : 0;
        r.stressPenalty = r.stress > 70 ? 5 : 0;

        int found = 0;
        foreach (var f in r.found) found += f.points;
        r.score = Mathf.Clamp(100 + r.choreBonus - r.failPenalty - found - r.homeworkPoints - r.stressPenalty, 0, 115);
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
}
