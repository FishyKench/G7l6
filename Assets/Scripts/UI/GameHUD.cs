using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// The hand-drawn HUD: a to-do list of sticky notes (top right), a stress heart and energy drop
/// on a paper card (bottom left), an "[E]" keycap hint, paper-strip messages and a luggage tag
/// for what you're carrying.
///
/// The HUD objects live in the scene (made by G7l6 > Set Up House Tasks) so you can move and
/// restyle them in the editor. If they're missing, the HUD builds itself when the game starts.
/// Art: Assets/Resources/HUD (regenerate with Tools/hud_art/make_hud_art.py or paint over it).
/// </summary>
public class GameHUD : MonoBehaviour
{
    public static GameHUD Instance { get; private set; }

    // Marker colors for messages (used by the tasks).
    public static Color Good => HudArt.MarkerGreen;
    public static Color Bad => HudArt.MarkerRed;
    public static Color Warn => HudArt.MarkerOrange;
    public static Color Info => HudArt.Ink;

    [Tooltip("Handwriting font. Leave empty for the TextMeshPro default.")]
    public TMP_FontAsset font;

    [Header("Scene objects (filled in by the setup tool)")]
    public RectTransform notesRoot;
    public TaskNote noteTemplate;
    public CanvasGroup emptyHint;
    public PromptView prompt;
    public ToastView toast;

    [Header("Layout")]
    public float noteSpacing = 140f;
    [Tooltip("Notes shrink when there are too many to fit in this height.")]
    public float notesMaxHeight = 800f;

    readonly Dictionary<TaskBase, TaskNote> notes = new Dictionary<TaskBase, TaskNote>();
    readonly List<TaskBase> ended = new List<TaskBase>();
    readonly List<TaskBase> byUrgency = new List<TaskBase>();
    TextMeshProUGUI moreTag;
    const float CompactScale = 0.66f;

    static float Urgency(TaskBase t)
    {
        if (t is HazardTask) return -1000f + t.ActivatedAt * 0.001f;           // fire, flood, leak: first
        if (t.isMainTask) return 100000f + t.ActivatedAt;                      // homework / the match: always there, so last
        return t.DeadlineRunning ? t.TimeLeft : 50000f + t.ActivatedAt;        // the rest: soonest to run out
    }

    /// <summary>Under the notes: "hold TAB: show me the way" (or, with TAB held, which numbers to press).</summary>
    void MoreTag(bool listOpen, float bottomY)
    {
        if (!moreTag)
        {
            moreTag = UIFactory.Text("More", notesRoot, 26, HudArt.Ink, TextAlignmentOptions.TopRight, font);
            moreTag.raycastTarget = false;
            moreTag.outlineWidth = 0.18f;
            moreTag.outlineColor = new Color32(255, 250, 235, 220);
            var rt = moreTag.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(1f, 1f);
            rt.sizeDelta = new Vector2(360f, 40f);
        }
        string text = byUrgency.Count == 0 ? ""
                    : listOpen ? "press <color=#D23A30>1-" + Mathf.Min(9, byUrgency.Count) + "</color>: show me the way"
                    : "<size=80%>hold TAB: show me the way</size>";
        if (moreTag.text != text) moreTag.text = text;
        moreTag.rectTransform.anchoredPosition = new Vector2(-6f, bottomY - 6f);
    }

    public bool IsBuilt => notesRoot && noteTemplate && prompt && toast;

    void Awake()
    {
        Instance = this;
        if (!IsBuilt) HudBuilder.Build(this);
        noteTemplate.gameObject.SetActive(false);
    }

    CanvasGroup screenGroup;

    /// <summary>With the VR headset on you can't see the house - the to-do list and messages fade away
    /// (you have to remember what's waiting for you).</summary>
    void LateUpdate()
    {
        if (!screenGroup)
        {
            var canvas = GetComponentInChildren<Canvas>(true);
            if (!canvas) return;
            screenGroup = canvas.GetComponent<CanvasGroup>();
            if (!screenGroup) screenGroup = canvas.gameObject.AddComponent<CanvasGroup>();
            screenGroup.blocksRaycasts = false;
        }
        screenGroup.alpha = Mathf.MoveTowards(screenGroup.alpha, VRHeadset.Worn ? 0f : 1f, Time.unscaledDeltaTime * 4f);
        UpdateVitals();
    }

    // ------------------------------------------------------------------ the stress / tired card: only when it's worth watching

    CanvasGroup vitals;
    bool vitalsLooked;
    float lastStress = -1f, lastTired = -1f, vitalsChangedAt = -99f;

    void UpdateVitals()
    {
        if (!vitalsLooked)
        {
            vitalsLooked = true;
            foreach (var t in GetComponentsInChildren<Transform>(true))
                if (t.name == "Vitals") { vitals = t.GetComponent<CanvasGroup>(); if (!vitals) vitals = t.gameObject.AddComponent<CanvasGroup>(); break; }
        }
        if (!vitals) return;
        float stress = StressManager.Instance ? StressManager.Instance.Normalized : 0f;
        float tired = ExhaustionManager.Instance ? ExhaustionManager.Instance.Normalized : 0f;
        // it pops up when one of them has moved by 5% (a slow creep doesn't keep it on screen)
        if (lastStress < 0f) { lastStress = stress; lastTired = tired; }
        if (Mathf.Abs(stress - lastStress) > 0.05f || Mathf.Abs(tired - lastTired) > 0.05f)
        {
            vitalsChangedAt = Time.unscaledTime;
            lastStress = stress;
            lastTired = tired;
        }
        bool show = HudFocus.Full || stress > 0.34f || tired > 0.34f || Time.unscaledTime - vitalsChangedAt < 4f;
        vitals.alpha = Mathf.MoveTowards(vitals.alpha, show ? 1f : 0f, Time.unscaledDeltaTime * 3f);
    }



    public static void Toast(string message, Color color)
    {
        if (Instance && Instance.toast) Instance.toast.Show(message, color);
        else Debug.Log(message);
    }

    public static void SetPrompt(string text)
    {
        if (Instance && Instance.prompt) Instance.prompt.Set(text);
    }

    void Update()
    {
        // Notes whose task just ended get crossed off (or crumpled).
        ended.Clear();
        foreach (var pair in notes)
            if (!pair.Key || !pair.Key.IsActive) ended.Add(pair.Key);
        foreach (var task in ended)
        {
            var note = notes[task];
            notes.Remove(task);
            if (note && !note.gameObject.activeSelf) Destroy(note.gameObject); // it was tucked away: just goes
            else if (note) note.Finish(task && task.LastSucceeded);
        }

        var all = GameManager.Instance ? GameManager.Instance.GetHudTasks() : null;
        // most urgent first: something spreading, then whatever runs out soonest, then the long ones (homework, the match)
        byUrgency.Clear();
        if (all != null) byUrgency.AddRange(all);
        byUrgency.Sort((a, b) => Urgency(a).CompareTo(Urgency(b)));
        var tasks = byUrgency;
        int count = tasks.Count;
        // every job stays on the list (you plan with it) - but only the most urgent is a big note; the rest sit under it,
        // smaller and without the status line. Hold TAB: all full size and numbered (press one for footprints to it).
        bool listOpen = TaskGuide.ListOpen;
        int shown = count;
        if (TaskGuide.Instance) TaskGuide.Instance.SetListed(tasks);
        float want = 0f;
        for (int i = 0; i < count; i++) want += noteSpacing * (listOpen || i == 0 ? 1f : CompactScale);
        float fit = want <= 0f ? 1f : Mathf.Clamp(notesMaxHeight / want, 0.6f, 1f);
        float nextY = 0f;

        for (int i = 0; i < count; i++)
        {
            var task = tasks[i];
            float size = (listOpen || i == 0 ? 1f : CompactScale) * fit;
            float slotY = nextY;
            nextY -= noteSpacing * size;
            if (!notes.TryGetValue(task, out var note) || !note)
            {
                note = Instantiate(noteTemplate, notesRoot);
                note.name = "Note - " + task.taskName;
                note.Bind(task, slotY + 30f);
                notes[task] = note;
            }
            bool visible = i < shown;
            if (note.gameObject.activeSelf != visible) note.gameObject.SetActive(visible);
            if (visible) { note.SetSlot(slotY, size); note.SetCompact(!listOpen && i > 0); note.SetKey(listOpen && i < 9 ? i + 1 : 0); }
        }
        MoreTag(listOpen, nextY);
        if (emptyHint)
            emptyHint.alpha = Mathf.MoveTowards(emptyHint.alpha, count == 0 ? 1f : 0f, Time.unscaledDeltaTime * 3f);
    }
}
