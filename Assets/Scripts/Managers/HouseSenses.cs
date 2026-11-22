using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Lets you read the house without looking at it - the core skill of the game (step away from the match at the right moment).
///
/// Every chore / hazard has its own sound "tell" (oven timer beeps, the smoke alarm chirps, the leak goes plink, the flood sloshes,
/// TV static, the washer buzzes). A tell plays when the problem starts and again as it gets worse (half-way, then nearly too late,
/// then every few seconds). Out of VR you hear it from where it happens. In VR you hear it MUFFLED through the headset, and a
/// sticky note slides in on the left: where it is, what it is, and how close it is to going wrong.
/// Added by the GameManager; finds the tasks by itself.
/// </summary>
public class HouseSenses : MonoBehaviour
{
    const int MaxNotes = 4;
    const float NoteW = 330f, NoteH = 78f, NoteGap = 10f;

    class Tracked
    {
        public TaskBase task;
        public bool wasActive, wasRunning;
        public float maxTime, lastLeft;
        public int level;          // 0 quiet, 1 started, 2 getting bad, 3 nearly too late
        public float nextRepeat;
        public Note note;
    }

    class Note
    {
        public RectTransform rt;
        public Image paper, fill;
        public TextMeshProUGUI where, what;
        public float shown, wobble;
        public Spring slide = new Spring(-1f);
    }

    readonly List<Tracked> tracked = new List<Tracked>();
    float nextScan;
    AudioSource muffled;
    AudioLowPassFilter lowPass;
    Canvas canvas;
    RectTransform noteRoot;
    TMP_FontAsset font;

    void Start()
    {
        var ears = new GameObject("House through the headset");
        ears.transform.SetParent(transform, false);
        muffled = ears.AddComponent<AudioSource>();
        muffled.spatialBlend = 0f;
        muffled.playOnAwake = false;
        muffled.ignoreListenerPause = false;
        lowPass = ears.AddComponent<AudioLowPassFilter>();
        lowPass.cutoffFrequency = 1100f;
        font = GameHUD.Instance ? GameHUD.Instance.font : null;
        BuildCanvas();
    }

    void Update()
    {
        if (PauseMenu.IsPaused) return;
        if (Time.time >= nextScan)
        {
            nextScan = Time.time + 2f;
            foreach (var t in FindObjectsOfType<TaskBase>())
                if (!(t is MatchTask) && !(t is TypingTask) && !tracked.Exists(x => x.task == t))
                    tracked.Add(new Tracked { task = t });
        }

        bool inVR = VRHeadset.Worn;
        canvas.enabled = inVR;
        var day = DayManager.Instance;
        bool quiet = day && day.State == DayManager.Phase.Report;

        for (int i = tracked.Count - 1; i >= 0; i--)
        {
            var k = tracked[i];
            if (!k.task) { if (k.note != null) Destroy(k.note.rt.gameObject); tracked.RemoveAt(i); continue; }
            bool active = k.task.IsActive && !quiet;
            if (active && !k.wasActive) { k.level = 0; k.maxTime = 0f; k.lastLeft = 0f; }
            k.wasActive = active;
            if (!active)
            {
                k.level = 0;
                if (k.note != null) { Destroy(k.note.rt.gameObject); k.note = null; }
                continue;
            }

            float urgency = Urgency(k);
            int level = urgency >= 0.85f ? 3 : urgency >= 0.55f ? 2 : 1;
            if (level > k.level)
            {
                k.level = level;
                Tell(k, inVR);
                k.nextRepeat = Time.time + 7f;
                if (k.note != null) k.note.wobble = 1f;
            }
            else if (k.level == 3 && Time.time >= k.nextRepeat)
            {
                Tell(k, inVR);
                k.nextRepeat = Time.time + 6f;
            }

            if (k.note == null) k.note = MakeNote();
            UpdateNote(k, urgency);
        }
        LayoutNotes();
    }

    /// <summary>0 = just started, 1 = about to go wrong. Hazards (fire, flood) are always urgent.</summary>
    static float Urgency(Tracked k)
    {
        var t = k.task;
        if (t is HazardTask) return 1f;
        if (!t.DeadlineRunning) { k.wasRunning = false; return 0.3f; }
        bool restarted = !k.wasRunning || t.TimeLeft > k.lastLeft + 0.5f;
        k.wasRunning = true;
        if (restarted || k.maxTime <= 0f)
        {
            // Deadline (re)started (e.g. dinner just became ready): a new phase, so it gets a fresh tell.
            if (k.maxTime > 0f) k.level = 0;
            k.maxTime = Mathf.Max(t.TimeLeft, 1f);
        }
        k.lastLeft = t.TimeLeft;
        return Mathf.Clamp01(1f - t.TimeLeft / k.maxTime);
    }

    /// <summary>Sound tells are switched off (they got annoying); the sticky notes in VR still say what's going on.</summary>
    public static bool PlaySounds = false;

    void Tell(Tracked k, bool inVR)
    {
        if (!PlaySounds) return;
        var clip = ClipFor(k.task);
        float volume = 0.45f + 0.18f * k.level;
        if (inVR)
        {
            lowPass.cutoffFrequency = k.level >= 3 ? 1800f : 1100f;
            muffled.PlayOneShot(clip, volume * 0.8f);
        }
        else Fx.Play(clip, k.task.Where, volume);
    }

    // ------------------------------------------------------------------ the notes (in VR only)

    void BuildCanvas()
    {
        canvas = UIFactory.CreateCanvas("House senses (VR)", 46);
        canvas.transform.SetParent(transform, false);
        noteRoot = UIFactory.Rect("Notes", canvas.transform);
        UIFactory.Place(noteRoot, new Vector2(0f, 0.5f), new Vector2(0f, 1f), new Vector2(40, 190), new Vector2(NoteW, (NoteH + NoteGap) * MaxNotes));
        canvas.enabled = false;
    }

    Note MakeNote()
    {
        var n = new Note();
        n.rt = UIFactory.Rect("House note", noteRoot);
        UIFactory.Place(n.rt, new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero, new Vector2(NoteW, NoteH));
        var shadow = UIFactory.Panel("Shadow", n.rt, new Color(0f, 0f, 0f, 0.25f));
        shadow.sprite = HudArt.Get("note_paper");
        UIFactory.Fill(shadow.rectTransform, 5, -5, 6, -6);
        n.paper = UIFactory.Panel("Paper", n.rt, HudArt.NoteYellow);
        n.paper.sprite = HudArt.Get("note_paper");
        UIFactory.Fill(n.paper.rectTransform);
        var tape = UIFactory.Panel("Tape", n.rt, new Color(1f, 1f, 1f, 0.9f));
        tape.sprite = HudArt.Get("tape");
        UIFactory.Place(tape.rectTransform, new Vector2(0f, 1f), new Vector2(0.5f, 0.5f), new Vector2(18, -6), new Vector2(64, 20));
        tape.rectTransform.localRotation = Quaternion.Euler(0, 0, 28f);

        n.where = UIFactory.Text("Where", n.rt, 15, HudArt.InkSoft, TextAlignmentOptions.MidlineLeft, font);
        n.where.enableWordWrapping = false;
        n.where.overflowMode = TextOverflowModes.Ellipsis;
        UIFactory.Place(n.where.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(20, -8), new Vector2(NoteW - 40, 20));
        n.what = UIFactory.Text("What", n.rt, 22, HudArt.Ink, TextAlignmentOptions.MidlineLeft, font);
        n.what.enableWordWrapping = false;
        n.what.overflowMode = TextOverflowModes.Ellipsis;
        n.what.enableAutoSizing = true;
        n.what.fontSizeMin = 15f;
        n.what.fontSizeMax = 22f;
        UIFactory.Place(n.what.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(20, -28), new Vector2(NoteW - 40, 28));

        var bar = UIFactory.Panel("Bar", n.rt, new Color(0.13f, 0.13f, 0.16f, 0.15f));
        UIFactory.Place(bar.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0.5f), new Vector2(20, 14), new Vector2(NoteW - 40, 7));
        n.fill = UIFactory.Panel("Fill", bar.transform, HudArt.MarkerOrange);
        var f = n.fill.rectTransform;
        f.anchorMin = Vector2.zero; f.anchorMax = new Vector2(0f, 1f); f.offsetMin = f.offsetMax = Vector2.zero;
        n.rt.localRotation = Quaternion.Euler(0, 0, Random.Range(-2.5f, 2.5f));
        return n;
    }

    void UpdateNote(Tracked k, float urgency)
    {
        var n = k.note;
        string room = RoomOf(k.task.Where);
        n.where.text = string.IsNullOrEmpty(room) ? "SOMEWHERE IN THE HOUSE" : room.ToUpperInvariant();
        n.what.text = k.level >= 3 ? $"<b>{k.task.taskName}</b> - NOW!" : k.task.taskName;
        n.paper.color = k.level >= 3 ? HudArt.NoteCoral : k.level == 2 ? new Color32(255, 196, 120, 255) : HudArt.NoteYellow;
        n.fill.color = k.level >= 3 ? HudArt.MarkerRed : k.level == 2 ? HudArt.MarkerOrange : HudArt.MarkerGreen;
        n.fill.rectTransform.anchorMax = new Vector2(k.task is HazardTask ? 1f : urgency, 1f);
        n.wobble = Mathf.MoveTowards(n.wobble, 0f, Time.deltaTime * 1.5f);
        float shake = (k.level >= 3 ? 1.2f : 0f) + n.wobble * 3f;
        n.rt.localScale = Vector3.one * (1f + n.wobble * 0.08f);
        n.rt.localEulerAngles = new Vector3(0f, 0f, Mathf.Sin(Time.unscaledTime * 18f) * shake);
    }

    void LayoutNotes()
    {
        int i = 0;
        // Worst first.
        tracked.Sort((a, b) => b.level.CompareTo(a.level));
        foreach (var k in tracked)
        {
            if (k.note == null) continue;
            bool show = i < MaxNotes;
            k.note.rt.gameObject.SetActive(show);
            if (!show) continue;
            float x = k.note.slide.Step(0f, Time.unscaledDeltaTime, 3.5f, 0.6f);
            k.note.rt.anchoredPosition = new Vector2(x * (NoteW + 60f), -i * (NoteH + NoteGap));
            i++;
        }
    }

    static string RoomOf(Vector3 world)
    {
        var layout = HouseLayout.Instance;
        if (!layout) return null;
        var r = layout.RoomAt(world, layout.FloorAt(world.y - layout.transform.position.y));
        return r != null ? r.name : null;
    }

    // ------------------------------------------------------------------ the tells (made in code)

    static readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();

    static AudioClip ClipFor(TaskBase t)
    {
        if (t is OvenTask) return Clip("oven timer", 0.9f, OvenBeeps);
        if (t is FireHazard) return Clip("smoke alarm", 1.0f, SmokeAlarm);
        if (t is CeilingLeakTask) return Clip("drip", 0.7f, Drips);
        if (t is FloodHazard) return Clip("slosh", 0.8f, Slosh);
        if (t is TvTask) return Clip("tv static", 0.6f, Static);
        if (t is LaundryTask) return Clip("washer buzz", 0.8f, Buzzer);
        return Fx.Beep;
    }

    static AudioClip Clip(string name, float seconds, System.Func<float, System.Random, float> wave)
    {
        if (clips.TryGetValue(name, out var c) && c) return c;
        const int rate = 22050;
        int n = Mathf.CeilToInt(seconds * rate);
        var data = new float[n];
        var rng = new System.Random(name.GetHashCode());
        for (int i = 0; i < n; i++) data[i] = Mathf.Clamp(wave(i / (float)rate, rng), -1f, 1f);
        c = AudioClip.Create(name, n, 1, rate, false);
        c.SetData(data, 0);
        clips[name] = c;
        return c;
    }

    static float Env(float t, float start, float len) => t < start || t > start + len ? 0f : Mathf.Sin(Mathf.PI * (t - start) / len);
    static float Square(float t, float hz) => Mathf.Sign(Mathf.Sin(2f * Mathf.PI * hz * t));

    // Three quick kitchen-timer beeps.
    static float OvenBeeps(float t, System.Random r)
    {
        float s = 0f;
        for (int i = 0; i < 3; i++) s += Env(t, i * 0.26f, 0.16f) * Mathf.Sin(2f * Mathf.PI * 2200f * t) * 0.5f;
        return s;
    }

    // The loud triple chirp of a smoke alarm.
    static float SmokeAlarm(float t, System.Random r)
    {
        float s = 0f;
        for (int i = 0; i < 3; i++) s += (t - i * 0.3f > 0f && t - i * 0.3f < 0.2f ? 1f : 0f) * Square(t, 3100f) * 0.32f;
        return s;
    }

    // Two drops: a falling "plink".
    static float Drips(float t, System.Random r)
    {
        float s = 0f;
        foreach (float at in new[] { 0f, 0.38f })
        {
            float u = t - at;
            if (u < 0f || u > 0.25f) continue;
            float hz = 1500f - 900f * Mathf.Clamp01(u / 0.06f);
            s += Mathf.Sin(2f * Mathf.PI * hz * u) * Mathf.Exp(-u * 22f) * 0.7f;
        }
        return s;
    }

    // Water sloshing: noise swelling twice.
    static float Slosh(float t, System.Random r)
    {
        float noise = (float)(r.NextDouble() * 2.0 - 1.0);
        return noise * (Env(t, 0f, 0.4f) + Env(t, 0.35f, 0.45f) * 0.8f) * 0.35f;
    }

    static float Static(float t, System.Random r) => (float)(r.NextDouble() * 2.0 - 1.0) * Env(t, 0f, 0.6f) * 0.3f;

    // A low washing-machine buzzer.
    static float Buzzer(float t, System.Random r) => (Square(t, 180f) * 0.6f + Mathf.Sin(2f * Mathf.PI * 360f * t) * 0.4f) * Env(t, 0f, 0.75f) * 0.3f;
}
