using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Runs MyMail inside the house (added by the GameManager, one per day / scene load):
///   - sends the one-off mails (welcome, Friday tournament invite),
///   - Mom's side jobs: 1-2 job mails at random times of the afternoon. Accept one in MyMail and the job's props
///     appear (a trash bag in the kitchen + the bin by the front door, or thirsty plants downstairs). Do it before
///     Mom gets home and Mom pays through the wallet (Category.Job); otherwise the mail turns to Expired,
///   - delivery notices when ShopNest orders are placed and when they arrive,
///   - a phone-style toast when mail arrives while you are not sitting at the PC.
///
/// Editor / development-build keys:  F4 = Mom sends a side job now (cycles the job types),  F3 = empty the mailbox.
/// </summary>
public class MailRuntime : MonoBehaviour
{
    public static MailRuntime Instance { get; private set; }

    class Plan { public float hour; public Mail.JobType job; public bool sent; }

    readonly List<Plan> plan = new List<Plan>();
    readonly Queue<Mail.Message> toasts = new Queue<Mail.Message>();
    readonly Dictionary<Mail.JobType, int> accepted = new Dictionary<Mail.JobType, int>(); // job type -> mail id
    readonly List<HousePlant> plants = new List<HousePlant>();
    Carryable bag;
    float clock;
    bool welcomeDone, inviteDone, dayOver;
    int debugJob;

    public static string JobTitle(Mail.JobType job)
        => job == Mail.JobType.Trash ? "Take out the trash" : job == Mail.JobType.Plants ? "Water the plants" : "Side job";

    public static string JobHint(Mail.JobType job)
        => job == Mail.JobType.Trash ? "Carry the trash bag from the kitchen to the bin by the front door."
         : job == Mail.JobType.Plants ? "Find the droopy plants downstairs and water each one (E)."
         : "";

    void Awake()
    {
        Instance = this;
        // Yesterday's offers are over.
        foreach (var m in Mail.Messages)
            if (m.MailKind == Mail.Kind.Job && (m.State == Mail.JobState.Offered || m.State == Mail.JobState.Accepted))
                Mail.SetJobState(m.id, Mail.JobState.Expired);

        Mail.Arrived += OnArrived;
        Shop.OrderPlaced += OnOrderPlaced;
        Shop.OrdersDelivered += OnDelivered;

        // 1-2 jobs at random times (different kinds), the second one later in the afternoon.
        var kinds = new List<Mail.JobType> { Mail.JobType.Trash, Mail.JobType.Plants };
        if (Random.value < 0.5f) kinds.Reverse();
        int count = Random.value < 0.55f ? 2 : 1;
        if (count == 1) plan.Add(new Plan { hour = Random.Range(15.4f, 16.7f), job = kinds[0] });
        else
        {
            plan.Add(new Plan { hour = Random.Range(15.25f, 16.2f), job = kinds[0] });
            plan.Add(new Plan { hour = Random.Range(16.5f, 17.1f), job = kinds[1] });
        }
    }

    void OnDestroy()
    {
        Mail.Arrived -= OnArrived;
        Shop.OrderPlaced -= OnOrderPlaced;
        Shop.OrdersDelivered -= OnDelivered;
        if (Instance == this) Instance = null;
    }

    void Update()
    {
        if (PauseMenu.IsPaused) return;
        var day = DayManager.Instance;
        if (!day) return;
        clock += Time.deltaTime;

        if (day.State != DayManager.Phase.Day)
        {
            if (!dayOver) EndDay();
        }
        else
        {
            if (!welcomeDone && clock > 3f)
            {
                welcomeDone = true;
                if (Mail.Once("welcome")) SendWelcome();
            }
            if (!inviteDone && day.Hour >= 15.35f)
            {
                inviteDone = true;
                if (Mail.Once("invite")) SendInvite();
            }
            foreach (var p in plan)
                if (!p.sent && day.Hour >= p.hour) { p.sent = true; if (Campaign.Once($"d{Campaign.DayIndex}.job.{p.job}")) SendJob(p.job); } // once per day: restarting the day does not hand out the same paid job again
        }

        // Phone toasts (held back for the first few seconds so the HUD is up).
        if (clock > 3.5f)
            while (toasts.Count > 0) ShowPhone(toasts.Dequeue());

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (Input.GetKeyDown(KeyCode.F4) && day.State == DayManager.Phase.Day)
        {
            SendJob(debugJob++ % 2 == 0 ? Mail.JobType.Trash : Mail.JobType.Plants);
            GameHUD.Toast("[debug] Mom sent a side job", GameHUD.Info);
        }
        if (Input.GetKeyDown(KeyCode.F3))
        {
            Mail.ResetAll();
            GameHUD.Toast("[debug] mailbox emptied", GameHUD.Info);
        }
#endif
    }

    // ------------------------------------------------------------------ sending

    void SendWelcome()
    {
        Mail.Send("MyMail Team", "Welcome to MyMail!",
            "Hi there, and welcome to your NestNet browser!\n\n" +
            "Bank - your balance and every payment you make or receive.\n" +
            "ShopNest - gadgets and gear, delivered to the front door the next morning.\n" +
            "MyMail - that is here: jobs from Mom, invites and delivery notices.\n" +
            "ClipHub - upload clips of your matches. LiveLoop - stream your matches live (needs a webcam + mic).\n\n" +
            "Tip: Mom sometimes pays for small jobs around the house. Accept them here and do them before she gets home.",
            Mail.Kind.Info);
    }

    void SendInvite()
    {
        Mail.Send("Jay", "Friday tournament - you in?",
            "YO! The Ranked Arena league is running a tournament this Friday. 1st place wins $150 and 2nd place wins $60.\n\n" +
            "You need to climb the ranked ladder to qualify, so keep winning your matches this week. I am already signed up, so you better be too!\n\n- Jay",
            Mail.Kind.Invite);
    }

    void SendJob(Mail.JobType job)
    {
        int pay;
        string subject, body;
        if (job == Mail.JobType.Trash)
        {
            pay = Random.value < 0.5f ? 6 : 5;
            subject = "Quick favour: take out the trash";
            body = $"Hi sweetie! The kitchen trash is overflowing. If you carry the bag out to the bin by the front door before I get home, there is ${pay} in it for you.\n\nLove, Mom";
        }
        else
        {
            pay = Random.value < 0.5f ? 4 : 5;
            subject = "Please water the plants";
            body = $"Sweetie, the plants downstairs look so sad and droopy. Water them before I get home and I will pay you ${pay}.\n\nLove, Mom";
        }
        Mail.Send("Mom", subject, body, Mail.Kind.Job, job, pay);
    }

    void OnOrderPlaced(Shop.Item item)
    {
        Mail.Send("ShopNest", "Order confirmed - arriving tomorrow",
            $"Thanks for shopping at ShopNest!\n\nYour order:\n- {item.name} (${item.price})\n\nIt will be left at the front door tomorrow morning. Open the box (E) to install it.",
            Mail.Kind.Delivery);
    }

    void OnDelivered(List<Shop.Item> items)
    {
        if (items.Count == 0) return;
        var lines = new System.Text.StringBuilder();
        foreach (var i in items) lines.Append("- ").Append(i.name).Append('\n');
        Mail.Send("ShopNest Delivery", items.Count == 1 ? "Your package has arrived" : $"Your {items.Count} packages have arrived",
            $"Good news! We left {(items.Count == 1 ? "your package" : "your packages")} at the front door:\n{lines}\nOpen the box (E) to install it.",
            Mail.Kind.Delivery);
    }

    // ------------------------------------------------------------------ the phone toast

    void OnArrived(Mail.Message m)
    {
        if (m != null) toasts.Enqueue(m);
    }

    void ShowPhone(Mail.Message m)
    {
        var pc = GamingPC.Instance;
        if (pc && pc.Seated) return; // you are looking at the PC: the badge is enough
        var day = DayManager.Instance;
        if (!day) return;
        string from = (m.from ?? "").ToUpperInvariant();
        day.SendText(from, "New mail: " + m.subject);
        if (Camera.main) Fx.Play(Fx.Beep, Camera.main.transform.position, 0.35f, 1.6f);
    }

    // ------------------------------------------------------------------ Mom's jobs

    /// <summary>The Accept button in MyMail.</summary>
    public void AcceptJob(Mail.Message m)
    {
        if (m == null || m.MailKind != Mail.Kind.Job || m.State != Mail.JobState.Offered) return;
        var day = DayManager.Instance;
        if (!day || day.State != DayManager.Phase.Day)
        {
            Mail.SetJobState(m.id, Mail.JobState.Expired);
            return;
        }
        if (accepted.ContainsKey(m.Job)) { GameHUD.Toast("You are already doing that job!", GameHUD.Warn); return; }

        string where;
        bool ok = m.Job == Mail.JobType.Trash ? StartTrash(out where) : StartPlants(out where);
        if (!ok)
        {
            Mail.SetJobState(m.id, Mail.JobState.Expired);
            GameHUD.Toast("Mom changed her mind about that job.", GameHUD.Warn);
            return;
        }
        accepted[m.Job] = m.id;
        Mail.MarkRead(m.id);
        Mail.SetJobState(m.id, Mail.JobState.Accepted);
        GameHUD.Toast($"Job accepted (+${m.pay}): {where}", GameHUD.Info);
        Fx.Play(Fx.Click, Camera.main ? Camera.main.transform.position : Vector3.zero, 0.4f, 1.3f);
    }

    bool StartTrash(out string where)
    {
        where = "";
        // Bin first (it decides whether the job is possible), then the bag.
        TrashBin bin = TrashBin.Instance;
        bool madeBin = false;
        if (!bin)
        {
            if (!JobProps.FindBinSpot(TrashBin.Size, out Vector3 spot, out bool outside)) return false;
            var door = ShopRuntime.FrontDoor();
            Vector3 look = door ? door.transform.position : spot + Vector3.forward;
            bin = TrashBin.Create(spot, look);
            madeBin = true;
        }
        if (!bag)
        {
            foreach (var room in new[] { "Kitchen", "Dining", "Pantry" })
            {
                var spots = JobProps.RoomSpots(room, new Vector3(0.45f, 0.5f, 0.45f), 0.1f, 1.3f);
                if (spots.Count == 0) continue;
                bag = JobProps.BuildTrashBag(spots[0]);
                where = room == "Kitchen" ? "take the kitchen trash to the front-door bin" : $"take the trash bag ({room.ToLowerInvariant()}) to the front-door bin";
                break;
            }
            if (!bag)
            {
                if (madeBin && bin) Destroy(bin.gameObject);
                return false;
            }
        }
        else where = "take the trash bag to the front-door bin";
        bin.jobActive = true;
        return true;
    }

    bool StartPlants(out string where)
    {
        where = "";
        foreach (var old in plants) if (old) Destroy(old.gameObject); // (only happens when a debug job repeats)
        plants.Clear();
        var rooms = new List<string> { "Living room", "Dining", "Music room", "Playroom" };
        for (int i = rooms.Count - 1; i > 0; i--) { int j = Random.Range(0, i + 1); (rooms[i], rooms[j]) = (rooms[j], rooms[i]); }

        var names = new List<string>();
        foreach (var room in rooms)
        {
            if (plants.Count >= 2) break;
            var spots = JobProps.RoomSpots(room, HousePlant.Size, 0.12f, 1.4f);
            if (spots.Count == 0) continue;
            var plant = HousePlant.Create(spots[0]);
            plant.jobActive = true;
            plants.Add(plant);
            names.Add(room.ToLowerInvariant());
        }
        if (plants.Count == 0) return false;
        where = plants.Count == 1 ? $"water the plant in the {names[0]}" : $"water the plants ({names[0]} + {names[1]})";
        return true;
    }

    public void TrashThrown()
    {
        Complete(Mail.JobType.Trash);
        if (TrashBin.Instance) TrashBin.Instance.jobActive = false;
        bag = null;
    }

    public void PlantWatered(HousePlant plant)
    {
        int left = 0;
        foreach (var p in plants) if (p && !p.Watered) left++;
        if (left == 0) Complete(Mail.JobType.Plants);
        else GameHUD.Toast(left == 1 ? "Plant watered - one more to go" : $"Plant watered - {left} more to go", GameHUD.Good);
    }

    void Complete(Mail.JobType job)
    {
        if (!accepted.TryGetValue(job, out int id)) return;
        accepted.Remove(job);
        var m = Mail.Find(id);
        if (m == null) return;
        Wallet.Earn(m.pay, "Mom - " + JobTitle(job).ToLowerInvariant(), Wallet.Category.Job);
        Mail.SetJobState(id, Mail.JobState.Done);
        GameHUD.Toast($"Job done! Mom paid you ${m.pay}", GameHUD.Good);
        if (Camera.main) Fx.Play(Fx.Beep, Camera.main.transform.position, 0.5f, 1.5f);
    }

    /// <summary>Mom is home (or the day ended): whatever is not done is Expired.</summary>
    void EndDay()
    {
        dayOver = true;
        foreach (var m in Mail.Messages)
            if (m.MailKind == Mail.Kind.Job && (m.State == Mail.JobState.Offered || m.State == Mail.JobState.Accepted))
                Mail.SetJobState(m.id, Mail.JobState.Expired);
        accepted.Clear();
        foreach (var p in plants) if (p) p.jobActive = false;
        if (TrashBin.Instance) TrashBin.Instance.jobActive = false;
    }
}
