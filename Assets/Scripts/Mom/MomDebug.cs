#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Editor / development builds only. F1 opens a little panel to try Mom out at any time of day: bring her to you, let her roam the
/// house, play each of her animations, watch her through a camera that follows her (with slow motion), have her walk the house
/// like at 6 PM, or smack you - and you get up again afterwards. Nothing here counts towards the day's report card.
/// </summary>
public class MomDebug : MonoBehaviour
{
    const KeyCode Key = KeyCode.F1;

    static MomDebug instance;
    bool open;
    MomInspector mom;
    Coroutine job;
    bool busySmack;
    string note = "";

    // what the panel switched off while it's open (so the mouse can click)
    readonly List<Behaviour> paused = new List<Behaviour>();
    CursorLockMode savedLock;
    bool savedVisible;

    // the watching camera
    enum View { Off, Front, Side, Behind, Top }
    View view = View.Off;
    Camera watchCam;
    float orbit, orbitGoal, zoom = 1f, clearYaw;
    float slowMo = 1f;

    Vector2 scroll;
    GUIStyle box, head, small;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (instance) return;
        var go = new GameObject("Mom debug (F1)");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<MomDebug>();
    }

    void Update()
    {
        if (Input.GetKeyDown(Key)) SetOpen(!open);
        if (open && PauseMenu.IsPaused) SetOpen(false);

        // a Mom that's gone (the real one came home, or the scene changed)
        if (!mom) { mom = null; if (view != View.Off && !RealMom()) SetView(View.Off); }
        if (!mom && RealMom()) mom = RealMom();

        if (busySmack && !mom) busySmack = false;
        if (watchCam) FollowMom();
    }

    static MomInspector RealMom() => DayManager.Instance ? DayManager.Instance.Mom : null;
    static bool CanUse => DayManager.Instance && DayManager.Instance.State == DayManager.Phase.Day && HouseLayout.Instance;

    // ------------------------------------------------------------------ the panel

    void SetOpen(bool on)
    {
        if (on == open) return;
        open = on;
        if (on)
        {
            savedLock = Cursor.lockState;
            savedVisible = Cursor.visible;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            paused.Clear();
            foreach (var b in FindObjectsOfType<PlayerCam>()) if (b.enabled) { b.enabled = false; paused.Add(b); }
            if (PlayerInteraction.Instance && PlayerInteraction.Instance.enabled) { PlayerInteraction.Instance.enabled = false; paused.Add(PlayerInteraction.Instance); }
        }
        else
        {
            if (!busySmack) foreach (var b in paused) if (b) b.enabled = true;
            paused.Clear();
            Cursor.lockState = savedLock;
            Cursor.visible = savedVisible;
        }
    }

    void Styles()
    {
        if (box != null) return;
        box = new GUIStyle(GUI.skin.box) { padding = new RectOffset(10, 10, 8, 10) };
        head = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, fontSize = 15 };
        small = new GUIStyle(GUI.skin.label) { fontSize = 12, wordWrap = true };
    }

    void OnGUI()
    {
        if (view != View.Off && watchCam)
        {
            var hint = new GUIStyle(GUI.skin.label) { fontSize = 14, alignment = TextAnchor.MiddleCenter };
            GUI.color = new Color(1f, 1f, 1f, 0.85f);
            GUI.Label(new Rect(0, Screen.height - 34, Screen.width, 26), $"WATCHING MOM ({view})  -  {(mom ? mom.Doing : "no Mom")}  -  F1: debug panel", hint);
            GUI.color = Color.white;
        }
        if (!open) return;
        Styles();
        float k = Mathf.Max(1f, Screen.height / 1080f); // readable on big screens
        GUI.matrix = Matrix4x4.Scale(new Vector3(k, k, 1f));
        float h = Mathf.Min(760f, Screen.height / k - 280f);
        GUILayout.BeginArea(new Rect(20, 250, 340, h), box);
        scroll = GUILayout.BeginScrollView(scroll);
        Panel();
        GUILayout.EndScrollView();
        GUILayout.EndArea();
        GUI.matrix = Matrix4x4.identity;
    }

    void Panel()
    {
        GUILayout.Label("MOM DEBUG  (F1 to close)", head);
        if (!CanUse)
        {
            GUILayout.Label(DayManager.Instance && DayManager.Instance.State != DayManager.Phase.Day
                ? "Mom's really home (or the day's over) - this panel works during the day."
                : "No house here.", small);
            return;
        }
        GUILayout.Label(mom ? $"Mom: {mom.Doing}" + (MomNav.Ready ? "" : "  (walking map still baking)") : "Mom isn't here yet.", small);
        if (!string.IsNullOrEmpty(note)) GUILayout.Label(note, small);
        GUI.enabled = !busySmack;

        GUILayout.Space(4);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Bring Mom to me")) BringHere();
        if (GUILayout.Button("Send her away")) SendAway();
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Roam the house")) Run(m => m.Roam(), "Roaming: random rooms, a gesture at each stop.");
        if (GUILayout.Button("Walk to me")) Run(m => m.WalkHere(Player().position), "");
        if (GUILayout.Button("Stop")) { StopJob(); note = ""; }
        GUILayout.EndHorizontal();
        if (GUILayout.Button("Close every door (to watch her open them)"))
        {
            int n = 0;
            foreach (var d in FindObjectsOfType<HouseDoor>()) if (d.IsOpen) { d.Close(); n++; }
            note = $"Closed {n} door{(n == 1 ? "" : "s")}." + (mom ? $"  Mom has opened {mom.DoorsOpened} so far." : "");
        }

        GUILayout.Space(6);
        GUILayout.Label("The evening", head);
        if (GUILayout.Button("Inspect the house now (like 6 PM)")) Run(m => Inspect(m), "Walking the house. What she catches is NOT counted today.");
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Smack me")) Smack(false);
        if (GUILayout.Button("Inspect, then smack")) Smack(true);
        GUILayout.EndHorizontal();
        if (GUILayout.Button("Real 6 PM now (the actual report card)")) { SetOpen(false); SendAway(); DayManager.Instance.SkipToEvening(); }

        GUILayout.Space(6);
        GUILayout.Label("Watch her", head);
        GUILayout.BeginHorizontal();
        foreach (View v in System.Enum.GetValues(typeof(View)))
        {
            bool on = view == v;
            if (GUILayout.Toggle(on, v.ToString(), GUI.skin.button) && !on) SetView(v);
        }
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();
        GUILayout.Label("Zoom", GUILayout.Width(70));
        zoom = GUILayout.HorizontalSlider(zoom, 0.5f, 2.2f);
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();
        GUILayout.Label("Speed", GUILayout.Width(70));
        foreach (float s in new[] { 0.25f, 0.5f, 1f })
            if (GUILayout.Toggle(Mathf.Approximately(slowMo, s), s == 1f ? "normal" : s + "x", GUI.skin.button) && !Mathf.Approximately(slowMo, s)) SetSlowMo(s);
        GUILayout.EndHorizontal();

        GUILayout.Space(6);
        GUILayout.Label("Animations", head);
        if (!mom) GUILayout.Label("(bring Mom first)", small);
        else
        {
            var names = mom.ClipNames.OrderBy(n => n).ToList();
            for (int i = 0; i < names.Count; i += 2)
            {
                GUILayout.BeginHorizontal();
                for (int j = i; j < Mathf.Min(i + 2, names.Count); j++)
                    if (GUILayout.Button(Nice(names[j]))) { StopJob(); mom.Gesture(names[j]); }
                GUILayout.EndHorizontal();
            }
            GUILayout.Label("(walking plays by itself: use Roam or Walk to me)", small);
        }
        GUI.enabled = true;
    }

    static string Nice(string gesture) =>
        gesture == "attack-melee-right" ? "slap" : gesture == "interact-right" ? "check (right hand)" : gesture == "interact-left" ? "check (left hand)"
        : gesture == "emote-no" ? "shake head" : gesture == "emote-yes" ? "nod" : gesture == "pick-up" ? "bend down" : gesture.Replace('-', ' ');

    // ------------------------------------------------------------------ actions

    static Transform Player() => PlayerInteraction.Instance ? PlayerInteraction.Instance.transform
        : FindObjectOfType<PlayerMovementAdvanced>() ? FindObjectOfType<PlayerMovementAdvanced>().transform : null;

    MomInspector Ensure()
    {
        if (mom) return mom;
        mom = MomInspector.Spawn(DayManager.Instance);
        if (mom) PutNearPlayer();
        return mom;
    }

    void PutNearPlayer()
    {
        var p = Player();
        var cam = Camera.main;
        if (!p || !mom) return;
        Vector3 fwd = cam ? cam.transform.forward : p.forward;
        fwd.y = 0f;
        if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
        float s = HouseLayout.Instance ? HouseLayout.Instance.scale : 1f;
        mom.WarpTo(p.position + fwd.normalized * 2.2f * s);
        Vector3 back = p.position - mom.transform.position;
        back.y = 0f;
        if (back.sqrMagnitude > 0.01f) mom.transform.rotation = Quaternion.LookRotation(back);
    }

    void BringHere()
    {
        StopJob();
        if (mom) PutNearPlayer(); else Ensure();
        note = mom ? "" : "Couldn't spawn Mom (no house layout?).";
    }

    void SendAway()
    {
        StopJob();
        if (mom && mom != RealMom()) Destroy(mom.gameObject);
        mom = null;
        note = "";
    }

    void StopJob()
    {
        if (job != null && mom) mom.StopCoroutine(job);
        job = null;
        if (mom) mom.Halt();
    }

    void Run(System.Func<MomInspector, IEnumerator> what, string message)
    {
        if (!Ensure()) return;
        StopJob();
        note = message;
        job = mom.StartCoroutine(what(mom));
    }

    /// <summary>Her 6 PM walk, but what she catches is taken back off the list afterwards.</summary>
    IEnumerator Inspect(MomInspector m)
    {
        var day = DayManager.Instance;
        int before = day.caught.Count;
        yield return m.Inspect();
        if (day.caught.Count > before) day.caught.RemoveRange(before, day.caught.Count - before);
        note = "Done - nothing she found counts today.";
    }

    void Smack(bool inspectFirst)
    {
        if (!Ensure()) return;
        StopJob();
        SetOpen(false);
        if (view != View.Off) SetView(View.Off); // you see it from your own eyes
        busySmack = true;
        note = "";
        job = mom.StartCoroutine(SmackJob(mom, inspectFirst));
    }

    IEnumerator SmackJob(MomInspector m, bool inspectFirst)
    {
        if (inspectFirst) yield return Inspect(m);
        yield return m.Smack();
        yield return new WaitForSeconds(0.8f); // (in the real game the report card comes up here)
        yield return m.RecoverFromSmack();
        busySmack = false;
        if (!open) { Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; }
        job = null;
    }

    // ------------------------------------------------------------------ watching

    void SetView(View v)
    {
        view = v;
        if (v == View.Off)
        {
            if (watchCam) Destroy(watchCam.gameObject);
            watchCam = null;
            return;
        }
        if (!mom && !Ensure()) { view = View.Off; return; }
        if (!watchCam)
        {
            var main = Camera.main;
            var go = new GameObject("Watching Mom");
            watchCam = go.AddComponent<Camera>();
            if (main)
            {
                watchCam.CopyFrom(main);
                watchCam.depth = main.depth + 5;
                watchCam.targetTexture = null;
            }
            watchCam.nearClipPlane = 0.05f;
            orbit = orbitGoal = Angle(v);
            clearYaw = 0f;
            if (mom) watchCam.transform.position = mom.transform.position + Vector3.up * 2f;
        }
        orbitGoal = Angle(v);
    }

    static float Angle(View v) => v == View.Front ? 0f : v == View.Side ? 90f : v == View.Behind ? 180f : 30f;

    void FollowMom()
    {
        var m = mom ? mom : RealMom();
        if (!m) return;
        float s = HouseLayout.Instance ? HouseLayout.Instance.scale : 1f;
        orbit = Mathf.LerpAngle(orbit, orbitGoal, Time.unscaledDeltaTime * 4f);
        Vector3 chest = m.transform.position + Vector3.up * 1.1f * s;
        Quaternion around = Quaternion.Euler(0f, m.transform.eulerAngles.y + orbit, 0f);
        float dist = 2.1f * s * zoom;
        Vector3 want = view == View.Top
            ? new Vector3(0f, 2.6f, 1.6f).normalized * dist * 1.2f
            : Vector3.forward * dist + Vector3.up * 0.35f * s;
        // Walls in the way? Try the angles round her for the clearest view (closest to the one you picked), and only then come
        // in closer - never into her (in a tight corridor the camera rises and looks down instead).
        float bestScore = float.MinValue, bestYaw = 0f, bestLen = 0f;
        foreach (float tryYaw in new[] { 0f, 25f, -25f, 50f, -50f, 80f, -80f, 115f, -115f, 150f, -150f, 180f })
        {
            Vector3 d = Quaternion.Euler(0f, m.transform.eulerAngles.y + orbit + clearYaw + tryYaw, 0f) * want;
            float free = Free(chest, d.normalized, d.magnitude);
            float score = Mathf.Min(free, d.magnitude) - Mathf.Abs(clearYaw + tryYaw) * 0.004f * dist - (tryYaw == 0f ? 0f : 0.05f);
            if (score > bestScore) { bestScore = score; bestYaw = tryYaw; bestLen = free; }
        }
        clearYaw = Mathf.Clamp(Mathf.DeltaAngle(0f, clearYaw + bestYaw * Mathf.Min(1f, Time.unscaledDeltaTime * 3f)), -180f, 180f);
        Vector3 dir = (Quaternion.Euler(0f, m.transform.eulerAngles.y + orbit + clearYaw, 0f) * want).normalized;
        float len = Mathf.Min(want.magnitude, Free(chest, dir, want.magnitude));
        Vector3 pos = chest + dir * len;
        if (len < 1f * s) // too close: go up over her head instead
        {
            float up = Free(chest, Vector3.up, 1.4f * s);
            pos = chest + Vector3.up * Mathf.Max(0.5f * s, up) + dir * len * 0.5f;
        }
        watchCam.transform.position = Vector3.Lerp(watchCam.transform.position, pos, Mathf.Min(1f, Time.unscaledDeltaTime * 10f));
        watchCam.transform.rotation = Quaternion.LookRotation(chest + Vector3.up * 0.15f * s - watchCam.transform.position);
    }

    /// <summary>How far the camera can go from her chest that way before something's in the way.</summary>
    static float Free(Vector3 from, Vector3 dir, float max)
    {
        float best = max;
        foreach (var hit in Physics.SphereCastAll(from, 0.18f, dir, max, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
        {
            if (hit.collider.GetComponentInParent<PlayerMovementAdvanced>() || hit.collider.GetComponentInParent<Carryable>()) continue; // (you and small things don't block the view)
            if (hit.distance > 0f) best = Mathf.Min(best, Mathf.Max(0f, hit.distance - 0.05f));
        }
        return best;
    }

    void SetSlowMo(float s)
    {
        slowMo = s;
        if (!PauseMenu.IsPaused) Time.timeScale = s;
    }

    void OnDestroy()
    {
        if (Mathf.Approximately(Time.timeScale, slowMo) && slowMo != 1f) Time.timeScale = 1f;
    }
}
#endif
