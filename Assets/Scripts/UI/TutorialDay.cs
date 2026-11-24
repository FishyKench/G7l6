using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The tutorial: a quick hands-on run through the basics before your first day - not a day itself. The clock waits, no jobs
/// come up, Mom's out and Kitty behaves (mostly). It starts straight away on a new game:
///   1  homework: a big orange arrow over your PC - sit down (E), watch the little how-to clip on the screen, double-click
///      Homework, do one easy sum and copy a short line;
///   2  cooking: you're taken to the stove, where a practice stew is on (water, carrot and onion in): add the mince (defrost it)
///      and a potato (chop it), then turn the stove on. Halfway, Kitty knocks a glass of water off a counter (a short cutscene)
///      and you mop it up;
///   3  laundry: fold one shirt at your wardrobe;
/// then one card with the rest (the to-do list, the clock, Mom at 6, the match, Kitty and stress), and you're back in your room
/// as Monday starts for real (with your full homework). Hold Delete any time to skip it.
/// Runs once, on a new game's first day (Main menu > New game shows it again). Added by the GameManager.
/// </summary>
public class TutorialDay : MonoBehaviour
{
    /// <summary>The tutorial is running (the clock waits, homework is the short one, no random jobs).</summary>
    public static bool Active { get; private set; }
    /// <summary>The step it's on (for tests / the debug panel): homework, pc-clip, pc-desktop, pc-homework, cooking, cat, mop,
    /// laundry, basics, done.</summary>
    public static string Step { get; private set; } = "";
    public const string DoneKey = "G7l6.tutorial.done";

    // (for tests / the debug panel: what Enter, holding Delete and doing the step do)
    static bool contReq, skipReq, passReq;
    public static void Continue() => contReq = true;
    public static void Skip() => skipReq = true;
    public static void PassStep() => passReq = true;
    static bool Enter() { bool r = contReq || Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter); contReq = false; return r; }

    static bool Wanted => Campaign.Week == 1 && Campaign.DayIndex == 0 && PlayerPrefs.GetInt(DoneKey, 0) == 0;
    static float Scale => HouseLayout.Instance ? HouseLayout.Instance.scale : 1f;

    Canvas canvas;
    RectTransform card, bar, letterTop, letterBottom;
    TextMeshProUGUI cardTitle, cardBody, barTitle, barBody, skipHint, caption;
    Image fade;
    bool skipped;
    float skipHeld;
    Vector3 startPos;
    GameObject pcArrow;
    Mug glass;
    Spill spill;
    bool catShown;

    static bool forced;
    bool replay, hwWasActive;

    /// <summary>(Debug panel, F1) runs the tutorial again now, on any day: you're put at your PC, the clock waits, and
    /// afterwards the day carries on as it was (homework you'd already done stays done).</summary>
    public static void Replay()
    {
        if (Active || !GameManager.Instance) return;
        foreach (var old in FindObjectsOfType<TutorialDay>()) DestroyImmediate(old);
        forced = true;
        GameManager.Instance.gameObject.AddComponent<TutorialDay>();
    }

    void Awake()
    {
        replay = forced;
        forced = false;
        Active = replay || Wanted;
        if (Active) { Onboarding.AutoTips = false; PCDesktop.ResetTutorialClip(); }
    }

    void OnDestroy()
    {
        TaskBase.Activated -= SpillAppeared;
        if (Active) { Active = false; Onboarding.AutoTips = true; }
    }

    IEnumerator Start()
    {
        if (!Active) yield break;
        var gm = GameManager.Instance;
        var day = DayManager.Instance;
        if (!gm || !day) { Active = false; yield break; }
        gm.spawningPaused = true;
        if (CatBrain.Instance) CatBrain.Instance.KeepCalm(9999f);
        Build();
        skipHint.gameObject.SetActive(true);
        if (replay)
        {
            fade.color = Color.black;
            if (PlayerInteraction.Instance && PlayerInteraction.Instance.InMainTask) PlayerInteraction.Instance.ForceStopMainTask();
        }
        for (int i = 0; i < 6; i++) yield return null; // (the player is put in their room first)
        var mover = FindObjectOfType<PlayerMovementAdvanced>();
        if (replay && mover && GamingPC.Instance)
        {
            // (a replay: from wherever you are, back to your PC)
            Vector3 pcMid = Middle(GamingPC.Instance.transform);
            Vector3 stand = StandingSpot(mover, pcMid);
            Place(mover, stand, pcMid - stand);
            yield return null;
        }
        if (mover) startPos = mover.transform.position;
        var hwNow = FindObjectOfType<TypingTask>();
        hwWasActive = hwNow && hwNow.IsActive;
        FacePC();
        if (replay) yield return FadeTo(0f, 0.4f);
        yield return Steps();
        yield return Finish();
    }

    IEnumerator Steps()
    {
        yield return Homework();
        if (skipped) yield break;
        yield return CookingStep();
        if (skipped) yield break;
        yield return Laundry();
    }

    // ------------------------------------------------------------------ 1  homework

    IEnumerator Homework()
    {
        var gpc = GamingPC.Instance;
        var hw = gpc && gpc.Homework ? gpc.Homework : FindObjectOfType<TypingTask>();
        if (!gpc || !hw) yield break;
        if (!hw.IsActive) GameManager.Instance.StartTask(hw);
        else if (replay) hw.Regenerate(); // (today's homework becomes the short one for now)
        var desk = gpc.GetComponent<PCDesktop>();
        bool done = false;
        System.Action<TaskBase, bool> ended = (t, ok) => { if (t == hw) done = true; };
        TaskBase.Ended += ended;
        pcArrow = MakeArrow(PcTop());
        var mine = MyRoom();
        BuildBarriers(mine != null ? new List<HouseLayout.RoomInfo> { mine } : new List<HouseLayout.RoomInfo>(), "Homework first - then you can leave your room.");
        passReq = false;
        while (!done && !skipped)
        {
            bool seated = gpc.Seated;
            if (!seated) BarrierNudge();
            Step = !seated ? "homework" : PCDesktop.TutorialClipOpen ? "pc-clip" : gpc.ScreenApp == "homework" ? "pc-homework" : "pc-desktop";
            pcArrow.SetActive(!seated);
            BarAtTop(seated);
            switch (Step)
            {
                case "homework": Bar("1 / 3   HOMEWORK", "Walk to your <b>PC</b> (<b>WASD</b>) and press <b>E</b>."); break;
                case "pc-clip": Bar("1 / 3   HOMEWORK", "Watch how it's done - then click <b>OK, GOT IT</b> (or press <b>Enter</b>)."); break;
                case "pc-desktop": Bar("1 / 3   HOMEWORK", "<b>Double-click Homework</b> on the desktop (top left) with the mouse."); break;
                default:
                    Bar("1 / 3   HOMEWORK", hw.Progress < 0.5f ? "Type the answer, then press <b>Enter</b>."
                                                             : "Now copy the line - type it letter by letter.");
                    break;
            }
            if (passReq)
            {
                passReq = false;
                switch (Step)
                {
                    case "homework": if (PlayerInteraction.Instance) PlayerInteraction.Instance.BeginMainTask(gpc); break;
                    case "pc-clip": PCDesktop.CloseTutorialClip(); break;
                    case "pc-desktop": if (desk) desk.TutorialOpenHomework(); break;
                    default: hw.Complete(); break;
                }
            }
            yield return null;
        }
        TaskBase.Ended -= ended;
        if (pcArrow) Destroy(pcArrow);
        ClearBarriers();
        if (skipped) yield break;
        Bar("1 / 3   HOMEWORK", "Done!");
        yield return Wait(0.6f);
        if (PlayerInteraction.Instance && PlayerInteraction.Instance.InMainTask) PlayerInteraction.Instance.ForceStopMainTask();
        BarAtTop(false);
        yield return Nice("Homework done!");
    }

    /// <summary>The top of the PC (where the arrow floats).</summary>
    static Vector3 PcTop()
    {
        var pc = GamingPC.Instance;
        if (!pc) return Vector3.zero;
        var rs = pc.GetComponentsInChildren<Renderer>().Where(r => r.enabled && !(r is ParticleSystemRenderer)).ToArray();
        if (rs.Length == 0) return pc.transform.position + Vector3.up;
        var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
        return new Vector3(b.center.x, b.max.y, b.center.z);
    }

    void FacePC()
    {
        var pc = FindObjectOfType<PlayerCam>();
        if (pc && GamingPC.Instance) pc.LookTowards(PcTop() - Vector3.up * 0.15f * Scale);
    }

    // ------------------------------------------------------------------ your door: a see-through border until the homework's done

    readonly List<GameObject> barriers = new List<GameObject>();
    readonly List<Material> barrierMats = new List<Material>();
    float nextBarrierToast, barrierFlash;
    static Texture2D fadeUp;

    string barrierWhy = "";

    /// <summary>Your room (where the day starts).</summary>
    HouseLayout.RoomInfo MyRoom()
    {
        var layout = HouseLayout.Instance;
        var gpc = GamingPC.Instance;
        if (!layout) return null;
        if (startPos != Vector3.zero) { var r = layout.RoomAt(startPos, layout.FloorAt(startPos.y - 1f * Scale)); if (r != null) return r; }
        return gpc ? layout.RoomAt(gpc.transform.position, layout.FloorAt(gpc.transform.position.y)) : null;
    }

    struct Gap { public HouseLayout.RoomInfo other; public Vector2 a, b, inward; public int floor; }

    /// <summary>The ways out of a room: stretches of its outline you could walk through (door leaves don't count, railings
    /// do), each with the room on the other side (null: none).</summary>
    static List<Gap> WaysOut(HouseLayout layout, HouseLayout.RoomInfo room)
    {
        var gaps = new List<Gap>();
        var r = room.rect;
        float y = room.floor * layout.storeyHeight + 0.6f;
        Vector3 W(Vector2 q) => layout.transform.TransformPoint(new Vector3(q.x, y, q.y));
        Vector2[] corners = { new Vector2(r.xMin, r.yMin), new Vector2(r.xMax, r.yMin), new Vector2(r.xMax, r.yMax), new Vector2(r.xMin, r.yMax) };
        for (int e = 0; e < 4; e++)
        {
            Vector2 a = corners[e], b = corners[(e + 1) % 4];
            Vector2 along = (b - a).normalized, n = new Vector2(-along.y, along.x);
            if (Vector2.Dot(n, r.center - (a + b) * 0.5f) < 0f) n = -n; // (into the room)
            float len = (b - a).magnitude;
            bool open = false;
            Gap cur = default;
            for (float t = 0.05f; t < len; t += 0.1f)
            {
                Vector2 q = a + along * t;
                bool through = !OnRailing(layout, room.floor, q);
                HouseLayout.RoomInfo other = null;
                if (through)
                {
                    Vector3 from = W(q + n * 0.35f), to = W(q - n * 0.35f);
                    foreach (var hit in Physics.RaycastAll(from, to - from, Vector3.Distance(from, to), ~0, QueryTriggerInteraction.Ignore))
                        if (!hit.collider.GetComponentInParent<HouseDoor>() && !hit.collider.GetComponentInParent<PlayerMovementAdvanced>()
                            && !hit.collider.GetComponentInParent<Carryable>() && !hit.collider.GetComponentInParent<CatBrain>()) { through = false; break; }
                    if (through) other = layout.RoomAt(to, room.floor);
                }
                if (open && through && cur.other == other) { cur.b = q + along * 0.05f; continue; }
                if (open) gaps.Add(cur);
                open = through;
                if (through) cur = new Gap { other = other, a = q - along * 0.05f, b = q + along * 0.05f, inward = n, floor = room.floor };
            }
            if (open) gaps.Add(cur);
        }
        return gaps;
    }

    static bool OnRailing(HouseLayout layout, int floor, Vector2 q)
    {
        foreach (var w in layout.walls)
        {
            if (w.type != HouseLayout.WallType.Railing || w.floor != floor) continue;
            Vector2 d = w.b - w.a;
            float k = Mathf.Clamp01(Vector2.Dot(q - w.a, d) / Mathf.Max(1e-4f, d.sqrMagnitude));
            if ((w.a + d * k - q).magnitude < 0.15f) return true;
        }
        return false;
    }

    /// <summary>The rooms you'd walk through from one to the other (fewest rooms; never by the stairs). Null: no way.</summary>
    static List<HouseLayout.RoomInfo> RoomPath(HouseLayout layout, HouseLayout.RoomInfo from, HouseLayout.RoomInfo to)
    {
        var prev = new Dictionary<HouseLayout.RoomInfo, HouseLayout.RoomInfo> { [from] = null };
        var queue = new Queue<HouseLayout.RoomInfo>();
        queue.Enqueue(from);
        while (queue.Count > 0)
        {
            var r = queue.Dequeue();
            if (r == to) break;
            foreach (var g in WaysOut(layout, r))
                if (g.other != null && !prev.ContainsKey(g.other) && g.other.kind != "Stairs") { prev[g.other] = r; queue.Enqueue(g.other); }
        }
        if (!prev.ContainsKey(to)) return null;
        var path = new List<HouseLayout.RoomInfo>();
        for (var r = to; r != null; r = prev[r]) path.Add(r);
        return path;
    }

    /// <summary>Every way out of these rooms (except into each other) gets a low, see-through orange border and an invisible
    /// wall behind it: the stairs, the other rooms' doors... 'why' is said when you walk up to one.</summary>
    void BuildBarriers(List<HouseLayout.RoomInfo> allowed, string why)
    {
        ClearBarriers();
        barrierWhy = why;
        var layout = HouseLayout.Instance;
        if (!layout || allowed.Count == 0) return;
        if (!fadeUp)
        {
            fadeUp = new Texture2D(2, 32, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "Fade up" };
            for (int y = 0; y < 32; y++)
            {
                float k = y / 31f, a = Mathf.Pow(1f - k, 1.6f);
                fadeUp.SetPixel(0, y, new Color(1f, 1f, 1f, a)); fadeUp.SetPixel(1, y, new Color(1f, 1f, 1f, a));
            }
            fadeUp.Apply();
        }
        foreach (var room in allowed)
            foreach (var g in WaysOut(layout, room))
                if (g.other == null || !allowed.Contains(g.other)) MakeBarrier(layout, g);
    }

    void MakeBarrier(HouseLayout layout, Gap g)
    {
        float s = Scale;
        float localY = g.floor * layout.storeyHeight;
        Vector3 wa = layout.transform.TransformPoint(new Vector3(g.a.x + g.inward.x * 0.18f, localY, g.a.y + g.inward.y * 0.18f));
        Vector3 wb = layout.transform.TransformPoint(new Vector3(g.b.x + g.inward.x * 0.18f, localY, g.b.y + g.inward.y * 0.18f));
        Vector3 c = (wa + wb) * 0.5f;
        if (Physics.Raycast(c + Vector3.up * 1.2f * s, Vector3.down, out var fl, 2.5f * s, ~0, QueryTriggerInteraction.Ignore)) c.y = fl.point.y;
        float width = Vector3.Distance(wa, wb);
        Vector3 inward = layout.transform.TransformDirection(new Vector3(g.inward.x, 0f, g.inward.y)); inward.y = 0f;

        var go = new GameObject("Tutorial barrier");
        go.transform.SetPositionAndRotation(c, Quaternion.LookRotation(inward.normalized));
        var box = go.AddComponent<BoxCollider>();
        box.size = new Vector3(width, 2.6f * s, 0.12f * s);
        box.center = new Vector3(0f, 1.3f * s, 0f);
        // the border: a see-through orange band at the bottom, fading out upwards
        var band = new GameObject("Border");
        band.transform.SetParent(go.transform, false);
        float h = 0.42f * s;
        var mesh = new Mesh { name = "Tutorial border" };
        mesh.SetVertices(new List<Vector3> { new Vector3(-width / 2f, 0.01f, 0f), new Vector3(width / 2f, 0.01f, 0f), new Vector3(width / 2f, h, 0f), new Vector3(-width / 2f, h, 0f) });
        mesh.SetUVs(0, new List<Vector2> { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) });
        mesh.SetTriangles(new[] { 0, 2, 1, 0, 3, 2 }, 0);
        mesh.RecalculateNormals(); mesh.RecalculateBounds();
        band.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = band.AddComponent<MeshRenderer>();
        var mat = new Material(Shader.Find("Sprites/Default")) { mainTexture = fadeUp, color = new Color(1f, 0.55f, 0.1f, 0.55f) };
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        barriers.Add(go);
        barrierMats.Add(mat);
    }

    /// <summary>Walk up to the border: it flashes, and a word about it now and then. Also its gentle pulse.</summary>
    void BarrierNudge()
    {
        if (barriers.Count == 0) return;
        var mover = FindObjectOfType<PlayerMovementAdvanced>();
        if (mover)
            foreach (var b in barriers)
            {
                if (!b) continue;
                Vector3 local = b.transform.InverseTransformPoint(mover.transform.position);
                var box = b.GetComponent<BoxCollider>();
                if (Mathf.Abs(local.x) > box.size.x * 0.5f + 0.2f || Mathf.Abs(local.z) > 0.9f * Scale) continue;
                barrierFlash = 1f;
                if (Time.unscaledTime >= nextBarrierToast)
                {
                    nextBarrierToast = Time.unscaledTime + 3.5f;
                    GameHUD.Toast(barrierWhy, GameHUD.Warn);
                }
            }
        barrierFlash = Mathf.MoveTowards(barrierFlash, 0f, Time.unscaledDeltaTime * 1.5f);
        float a = 0.38f + 0.12f * Mathf.Sin(Time.unscaledTime * 2.5f) + 0.35f * barrierFlash;
        foreach (var m in barrierMats) if (m) m.color = new Color(1f, 0.55f, 0.1f, a);
    }

    void ClearBarriers()
    {
        foreach (var b in barriers) if (b) Destroy(b);
        barriers.Clear();
        barrierMats.Clear();
    }

    // ------------------------------------------------------------------ the arrow over the PC

    GameObject MakeArrow(Vector3 over)
    {
        var go = new GameObject("Tutorial arrow");
        go.AddComponent<MeshFilter>().sharedMesh = ArrowMesh();
        var mr = go.AddComponent<MeshRenderer>();
        var orange = new Color(1f, 0.36f, 0.02f);
        mr.sharedMaterial = ProcBuild.Mat(orange, 0.25f, new Color(0.42f, 0.12f, 0f));
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        var a = go.AddComponent<FloatingArrow>();
        a.at = over + Vector3.up * 0.28f * Scale;
        a.size = Scale;
        return go;
    }

    /// <summary>Bobs up and down, breathes, and keeps its face to the camera.</summary>
    class FloatingArrow : MonoBehaviour
    {
        public Vector3 at;
        public float size = 1f;

        void LateUpdate()
        {
            float t = Time.unscaledTime;
            transform.position = at + Vector3.up * Mathf.Abs(Mathf.Sin(t * 2.6f)) * 0.14f * size;
            transform.localScale = Vector3.one * size * (1f + Mathf.Sin(t * 4.2f) * 0.08f);
            var cam = Camera.main;
            if (!cam) return;
            Vector3 d = cam.transform.position - transform.position; d.y = 0f;
            if (d.sqrMagnitude > 0.0001f) transform.rotation = Quaternion.LookRotation(d);
        }
    }

    /// <summary>A flat, thick arrow pointing down (tip at the origin, 0.5 m tall): front, back and sides.</summary>
    static Mesh ArrowMesh()
    {
        Vector2[] o = { new Vector2(0f, 0f), new Vector2(0.17f, 0.2f), new Vector2(0.065f, 0.2f), new Vector2(0.065f, 0.5f),
                        new Vector2(-0.065f, 0.5f), new Vector2(-0.065f, 0.2f), new Vector2(-0.17f, 0.2f) };
        const float d = 0.03f;
        var v = new List<Vector3>(); var n = new List<Vector3>(); var tri = new List<int>();
        void Tri(int a, int b, int c, Vector3 normal)
        {
            if (Vector3.Dot(Vector3.Cross(v[b] - v[a], v[c] - v[a]), normal) < 0f) { int k = b; b = c; c = k; }
            tri.Add(a); tri.Add(b); tri.Add(c);
        }
        foreach (float side in new[] { 1f, -1f })
        {
            int s = v.Count;
            var normal = new Vector3(0f, 0f, side);
            foreach (var p in o) { v.Add(new Vector3(p.x, p.y, side * d)); n.Add(normal); }
            Tri(s + 0, s + 1, s + 6, normal);                                  // the head
            Tri(s + 2, s + 3, s + 4, normal); Tri(s + 2, s + 4, s + 5, normal); // the shaft
        }
        float area = 0f;
        for (int i = 0; i < o.Length; i++) { var a = o[i]; var b = o[(i + 1) % o.Length]; area += a.x * b.y - b.x * a.y; }
        for (int i = 0; i < o.Length; i++)
        {
            var a = o[i]; var b = o[(i + 1) % o.Length];
            Vector2 e = b - a;
            Vector2 out2 = (area > 0f ? new Vector2(e.y, -e.x) : new Vector2(-e.y, e.x)).normalized;
            var normal = new Vector3(out2.x, out2.y, 0f);
            int s = v.Count;
            v.Add(new Vector3(a.x, a.y, d)); v.Add(new Vector3(b.x, b.y, d)); v.Add(new Vector3(b.x, b.y, -d)); v.Add(new Vector3(a.x, a.y, -d));
            for (int k = 0; k < 4; k++) n.Add(normal);
            Tri(s, s + 1, s + 2, normal); Tri(s, s + 2, s + 3, normal);
        }
        var m = new Mesh { name = "Tutorial arrow" };
        m.SetVertices(v); m.SetNormals(n); m.SetTriangles(tri, 0);
        m.RecalculateBounds();
        return m;
    }

    // ------------------------------------------------------------------ 2  cooking (with Kitty's little show and the mop)

    IEnumerator CookingStep()
    {
        var cook = Cooking.Instance;
        if (!cook || (cook.IsActive && !cook.Practising)) yield break; // (a replay after tonight's real cooking began: no practice)
        Step = "cooking";
        passReq = false;
        HideBar();
        yield return FadeTo(1f, 0.22f);
        // (the PC opened again in the moment after the homework: it closes - you're off to the kitchen)
        if (PlayerInteraction.Instance && PlayerInteraction.Instance.InMainTask) PlayerInteraction.Instance.ForceStopMainTask();
        var mover = FindObjectOfType<PlayerMovementAdvanced>();
        if (mover) { Vector3 stand = StandingSpot(mover, cook.StovePoint); Place(mover, stand, cook.StovePoint - stand); }
        yield return null; // (the camera follows you down first)
        var cam = FindObjectOfType<PlayerCam>();
        if (cam) cam.LookTowards(cook.StovePoint + Vector3.up * 0.2f * Scale);
        // the pot goes on the stove now (under the dip); the carrot and the onion tumble in as you watch
        StartCoroutine(cook.BeginPractice());
        Bar("2 / 3   COOKING", "Practice stew: the carrot and onion are going in...");
        yield return null;
        yield return FadeTo(0f, 0.3f);
        float ready = Time.unscaledTime + 3f;
        while (!cook.Practising && Time.unscaledTime < ready) yield return null;
        if (!cook.Practising) yield break;
        while (!skipped && !cook.PracticeDone)
        {
            var pi = PlayerInteraction.Instance;
            if (!catShown && cook.PracticeAdded >= 1 && !cook.Busy && !(pi && pi.InMainTask))
            {
                yield return CatScene();
                if (skipped) yield break;
                yield return MopStep();
                if (skipped) yield break;
                Step = "cooking";
                passReq = false;
                continue;
            }
            if (cook.PracticeServing)
                Bar("2 / 3   COOKING", cook.CarryingDinner ? "Serve it: take it to the <b>dining table</b> and press <b>E</b>." : "Serve it: carry the pot to the <b>island</b> and press <b>E</b>.");
            else if (cook.PracticeReady)
                Bar("2 / 3   COOKING", "Ding! It's ready. Turn the <b>stove off</b> (<b>E</b>).");
            else if (cook.PracticeAdded < cook.PracticeItems)
                Bar("2 / 3   COOKING", $"Add the <b>mince</b> (defrost it in the microwave) and a <b>potato</b> (chop it). Follow the markers.   <b>{cook.PracticeAdded} / {cook.PracticeItems}</b> in");
            else if (!cook.PracticeCooking)
                Bar("2 / 3   COOKING", "All in! Now turn the <b>stove</b> on (<b>E</b>).");
            else
                Bar("2 / 3   COOKING", $"Cooking...  <b>{Mathf.CeilToInt(cook.PracticeLeft)}s</b>");
            if (passReq)
            {
                passReq = false;
                if (pi && pi.InMainTask) pi.ForceStopMainTask();
                if (cook.PracticeServing) cook.InteractWith(cook.CarryingDinner ? KitchenStation.Kind.Table : KitchenStation.Kind.Island);
                else if (cook.PracticeReady) cook.InteractWith(KitchenStation.Kind.Stove);
                else cook.PracticeCheat(catShown);
            }
            yield return null;
        }
        if (skipped) yield break;
        Bar("2 / 3   COOKING", "Dinner's on the table!");
        yield return Nice("Dinner's on the table!");
    }

    // ---------- Kitty knocks a glass off the counter

    class Cutscene : IMainTask
    {
        public bool stopped;
        public bool ShouldBlockInput() => true;
        public void StartMainTask() { }
        public void StopMainTask() => stopped = true; // (Esc)
    }

    Cutscene cut;
    Transform camHolder;
    MoveCamera moveCam;
    Vector3 savedHolderPos;
    Quaternion savedCamRot;
    readonly List<GameObject> hiddenForCut = new List<GameObject>();
    readonly List<Renderer> hiddenRenderers = new List<Renderer>();
    readonly List<Mug> hiddenMugs = new List<Mug>();
    Spill appeared;

    void SpillAppeared(TaskBase t) { if (t is Spill s && !appeared) appeared = s; }

    IEnumerator CatScene()
    {
        Step = "cat";
        catShown = true;
        appeared = null;
        TaskBase.Activated -= SpillAppeared;
        TaskBase.Activated += SpillAppeared;
        HideBar();
        yield return FadeTo(1f, 0.22f);
        float blackFrom = Time.unscaledTime;

        // a glass of water on a table in the living room (near an edge, open floor past it), and Kitty up behind it
        var cam = Camera.main;
        float s = Scale;
        Vector3 eye = cam ? cam.transform.position : transform.position;
        Vector3 roomMiddle = eye;
        foreach (var spot in GlassSpots())
        {
            roomMiddle = spot.middle;
            var m = Mug.Create(spot.at, s * 1.05f, glassOfWater: true);
            if (CatBrain.Instance && CatBrain.Instance.KnockOverForShow(m, spot.push, spot.back)) { glass = m; break; }
            Destroy(m.gameObject);
        }
        if (!glass)
        {
            // (no show: a glass just topples off the nearest table)
            var any = GlassSpots().FirstOrDefault();
            if (any.at != Vector3.zero)
            {
                glass = Mug.Create(any.at, s * 1.05f, glassOfWater: true);
                glass.StartCoroutine(FallSoon(glass, FloorBeside(any.at, any.at + any.push)));
                roomMiddle = any.middle;
            }
        }
        Vector3 subject = glass ? glass.transform.position : Cooking.Instance.StovePoint;
        // (any other drink standing near it is put aside for the scene: one glass, no mix-ups)
        hiddenMugs.Clear();
        if (glass)
            foreach (var o in Mug.All.ToList())
                if (o && o != glass && !o.Falling && Vector3.Distance(o.transform.position, glass.transform.position) < 2f * s) { o.gameObject.SetActive(false); hiddenMugs.Add(o); }

        // the camera: ~1.9 m from the glass, a little above it, looking down at it (from the middle of the room)
        shotSide = roomMiddle;
        BeginCut(subject);
        SetCaption($"Uh oh... {CatBrain.Name}'s eyeing that glass of water.");
        while (Time.unscaledTime - blackFrom < 0.7f && !skipped) yield return null; // (a beat of black: somewhere else now)
        yield return FadeTo(0f, 0.25f);
        if (CatBrain.Instance) CatBrain.Instance.StartShow();

        float t0 = Time.unscaledTime;
        bool forced = false;
        while (!appeared && !skipped && Time.unscaledTime - t0 < 22f)
        {
            if (!forced && Time.unscaledTime - t0 > 17f && glass && !glass.Falling)
            {
                forced = true;
                glass.StartCoroutine(glass.Fall(FloorBeside(glass.transform.position, shotSide)));
            }
            if (cut.stopped) break; // (Esc: no need to watch)
            yield return null;
        }
        if (appeared && !cut.stopped && !skipped)
        {
            // over to the puddle
            var camT = Camera.main.transform;
            Vector3 puddle = appeared.transform.position;
            SetCaption($"{CatBrain.Name} sometimes knocks things over - you clean it up.");
            Quaternion from = camT.rotation, to = Quaternion.LookRotation(puddle - camT.position);
            for (float k = 0f; k < 1f && !skipped && !cut.stopped; k += Time.unscaledDeltaTime / 0.7f)
            {
                camT.rotation = Quaternion.Slerp(from, to, Mathf.SmoothStep(0f, 1f, k));
                yield return null;
            }
            if (!cut.stopped) camT.rotation = to;
            // ...and she trots off right through it: wet paw prints (the camera stays till they're there)
            float until = Time.unscaledTime + 1.8f, most = Time.unscaledTime + 6.5f;
            bool prints = false;
            while (!skipped && !cut.stopped && Time.unscaledTime < most)
            {
                if (!prints && PawPrints.Current && PawPrints.Current.IsActive)
                {
                    prints = true;
                    until = Mathf.Max(until, Time.unscaledTime + 1.6f);
                    SetCaption($"...and now there are wet paw prints too. Mop those as well.");
                }
                if (prints && Time.unscaledTime >= until) break;
                yield return null;
            }
        }
        if (skipped) yield break; // (Finish puts the camera back)
        yield return FadeTo(1f, 0.2f);
        EndCut();
        if (appeared) PrepareMop(appeared.transform.position);
        yield return null;
        yield return FadeTo(0f, 0.25f);
    }

    struct GlassSpot { public Vector3 at, push, middle; public float score, back; }

    /// <summary>
    /// Where the glass could go, best first: on a table (or desk, sideboard...) in the living room - away from the kitchen - near
    /// one of its edges with open floor past it, and a clear run behind it for Kitty (flat things like a remote don't count).
    /// No good spot there: the other rooms (not the kitchen); then the kitchen counters in view.
    /// </summary>
    List<GlassSpot> GlassSpots()
    {
        var list = new List<GlassSpot>();
        var layout = HouseLayout.Instance;
        float s = Scale;
        var things = FindObjectsOfType<Renderer>().Where(r => r.enabled && !(r is ParticleSystemRenderer) && r.bounds.size.y < 0.8f * s
                                                              && r.bounds.size.y > 0.035f * s && !r.GetComponentInParent<CatBrain>()).ToList();
        var names = new System.Text.RegularExpressions.Regex("table|desk|counter|island|cabinet|dresser|sideboard|stand|shelf");
        Vector3 eye = Camera.main ? Camera.main.transform.position : transform.position;
        foreach (var p in CatNavMesh.Perches)
        {
            if (!names.IsMatch(p.name.ToLowerInvariant())) continue;
            var room = layout ? layout.RoomAt(p.top, layout.FloorAt(PlayerFeet())) : null;
            string rn = room != null ? room.name.ToLowerInvariant() : "";
            float rank = rn.Contains("living") ? 2f : rn.Length > 0 && !rn.Contains("kitchen") ? 1f : 0f;
            Vector3 middle;
            float floorY;
            if (room != null)
            {
                floorY = layout.transform.TransformPoint(new Vector3(0f, room.floor * layout.storeyHeight, 0f)).y;
                middle = layout.transform.TransformPoint(new Vector3(room.rect.center.x, room.floor * layout.storeyHeight + 1f, room.rect.center.y));
            }
            else { floorY = PlayerFeet(); middle = eye; }
            if (p.top.y - floorY < 0.3f * s || p.top.y - floorY > 1.4f * s) continue;
            if (rank == 0f) { float dd = Flat(p.top - eye).magnitude; if (dd < 1f * s || dd > 6f * s) continue; middle = eye; }
            // the best edge of this top for it
            Vector3 top = p.top;
            if (Physics.Raycast(top + Vector3.up * 0.3f * s, Vector3.down, out var surf, 0.6f * s, ~0, QueryTriggerInteraction.Ignore)) top = surf.point;
            GlassSpot best = default;
            best.score = float.MinValue;
            var near = things.Where(r => r && (r.bounds.center - top).sqrMagnitude < 4f * s * s).ToList();
            Vector3 square = layout ? Flat(layout.transform.forward).normalized : Vector3.forward;
            for (int i = 0; i < 4; i++)
            {
                Vector3 dir = Quaternion.Euler(0f, i * 90f, 0f) * square; // (square to the room: furniture is)
                float edge = TopReach(top, dir), behind = TopReach(top, -dir);
                if (edge < 0.12f * s || edge - 0.11f * s + behind < 0.6f * s) continue;
                Vector3 past = top + dir * (edge + 0.28f * s);
                if (!Physics.Raycast(past + Vector3.up * 0.1f * s, Vector3.down, out var fl, 3f * s, ~0, QueryTriggerInteraction.Ignore)
                    || fl.normal.y < 0.7f || fl.point.y > top.y - 0.25f * s) continue;           // (open floor past the edge)
                if (Physics.CheckSphere(fl.point + Vector3.up * 0.3f * s, 0.15f * s, ~0, QueryTriggerInteraction.Ignore)) continue;
                Vector3 at = top + dir * (edge - 0.11f * s);
                if (Mug.All.Any(o => o && Vector3.Distance(o.transform.position, at) < 0.4f * s)) continue;
                // her run along the top to it: clear of things standing there (books, a plant...)
                Vector3 side = Vector3.Cross(Vector3.up, dir) * 0.08f * s;
                float clear = 0f;
                for (float d = 0f; d <= 0.85f * s; d += 0.05f * s)
                {
                    Vector3 q = at - dir * d;
                    var cell = new Bounds(q + Vector3.up * 0.12f * s, new Vector3(0.05f * s, 0.2f * s, 0.05f * s));
                    cell.Encapsulate(q + side + Vector3.up * 0.03f * s); cell.Encapsulate(q - side + Vector3.up * 0.03f * s);
                    if (d > 0.05f * s && near.Any(r => r && r.bounds.min.y > top.y - 0.02f * s && r.bounds.Intersects(cell))) break;
                    if (TopReach(q, dir) < 0f) break; // (off the top)
                    clear = d;
                }
                clear = Mathf.Min(clear, edge - 0.11f * s + behind);
                if (clear < 0.5f * s) continue;
                // best: it goes off sideways as seen from the middle of the room (Kitty and the glass side by side in the shot)
                float score = rank * 10f + 1.5f * (1f - Mathf.Abs(Vector3.Dot(dir, Flat(middle - top).normalized))) + 0.5f * clear / s;
                if (score > best.score) best = new GlassSpot { at = at, push = dir, middle = middle, score = score, back = clear };
            }
            if (best.score > float.MinValue) list.Add(best);
        }
        return list.OrderByDescending(g => g.score).Take(6).ToList();
    }

    /// <summary>How far the top goes this way from here (to its edge).</summary>
    static float TopReach(Vector3 top, Vector3 dir)
    {
        float s = Scale;
        for (float r = 0.02f * s; r < 2f * s; r += 0.03f * s)
        {
            Vector3 p = top + dir * r;
            bool on = false;
            foreach (var hit in Physics.RaycastAll(p + Vector3.up * 0.2f * s, Vector3.down, 0.4f * s, ~0, QueryTriggerInteraction.Ignore))
                if (Mathf.Abs(hit.point.y - top.y) < 0.04f * s && !hit.collider.GetComponentInParent<CatBrain>()) { on = true; break; }
            if (!on) return r - 0.03f * s;
        }
        return 2f * s;
    }

    static IEnumerator FallSoon(Mug m, Vector3 land)
    {
        yield return new WaitForSeconds(1.5f);
        if (m && !m.Falling) yield return m.Fall(land);
    }

    Vector3 shotSide;
    float savedFov;

    void BeginCut(Vector3 subject)
    {
        cut = new Cutscene();
        if (PlayerInteraction.Instance) PlayerInteraction.Instance.BeginMainTask(cut);
        var cam = Camera.main;
        if (!cam) return;
        var camT = cam.transform;
        moveCam = FindObjectOfType<MoveCamera>();
        camHolder = moveCam && camT.IsChildOf(moveCam.transform) ? moveCam.transform : camT;
        savedHolderPos = camHolder.position;
        savedCamRot = camT.rotation;
        if (moveCam) moveCam.enabled = false;
        Vector3 catAt = CatBrain.Instance && glass ? CatBrain.Instance.transform.position + Vector3.up * 0.15f * Scale : subject;
        Vector3 shot = ShotFor(subject, shotSide, catAt);
        camHolder.position = shot - (camT.position - camHolder.position);
        camT.rotation = Quaternion.LookRotation(Vector3.Lerp(subject, catAt, 0.3f) + Vector3.up * 0.17f * Scale - camT.position); // (a bit above: her words fit in)
        savedFov = cam.fieldOfView;
        cam.fieldOfView = 42f;

        HudHider.Hide();
        hiddenForCut.Clear();
        if (PlayerCarry.Instance && PlayerCarry.Instance.holdPoint && PlayerCarry.Instance.holdPoint.gameObject.activeSelf)
        { PlayerCarry.Instance.holdPoint.gameObject.SetActive(false); hiddenForCut.Add(PlayerCarry.Instance.holdPoint.gameObject); }
        foreach (var c in cam.GetComponentsInChildren<Camera>(true))
            if (c != cam && c.gameObject.activeSelf && c.name.ToLowerInvariant().Contains("arm")) { c.gameObject.SetActive(false); hiddenForCut.Add(c.gameObject); }
        // (and you: the camera's out of your head, so your body would be in the shot)
        hiddenRenderers.Clear();
        var mover = FindObjectOfType<PlayerMovementAdvanced>();
        if (mover) foreach (var r in mover.GetComponentsInChildren<Renderer>()) if (r.enabled) { r.enabled = false; hiddenRenderers.Add(r); }
        letterTop.gameObject.SetActive(true);
        letterBottom.gameObject.SetActive(true);
    }

    void EndCut()
    {
        if (cut == null) return;
        var cam = Camera.main;
        if (cam && camHolder)
        {
            camHolder.position = savedHolderPos;
            cam.transform.rotation = savedCamRot;
            if (savedFov > 1f) cam.fieldOfView = savedFov;
        }
        if (moveCam) moveCam.enabled = true;
        foreach (var g in hiddenForCut) if (g) g.SetActive(true);
        hiddenForCut.Clear();
        foreach (var r in hiddenRenderers) if (r) r.enabled = true;
        hiddenRenderers.Clear();
        foreach (var o in hiddenMugs) if (o) o.gameObject.SetActive(true);
        hiddenMugs.Clear();
        HudHider.Show();
        letterTop.gameObject.SetActive(false);
        letterBottom.gameObject.SetActive(false);
        caption.gameObject.SetActive(false);
        var pi = PlayerInteraction.Instance;
        if (pi && ReferenceEquals(pi.CurrentMainTask, cut)) pi.ForceStopMainTask();
        cut = null;
    }

    /// <summary>A spot ~1.9 m from the subject, a bit above it, with room for the camera and a clear view (the player's side first).</summary>
    static Vector3 ShotFor(Vector3 subject, Vector3 eye, Vector3 also)
    {
        float s = Scale;
        Vector3 toEye = Flat(eye - subject).normalized;
        if (toEye.sqrMagnitude < 0.01f) toEye = Vector3.forward;
        Vector3 look = subject + Vector3.up * 0.05f * s;
        Vector3 side = Flat(also - subject);
        side = side.sqrMagnitude > 0.0025f ? side.normalized : Vector3.zero; // (Kitty beside it: look across the two, not along)
        for (int pass = 0; pass < 2; pass++)
        {
            Vector3 best = Vector3.zero;
            float bestScore = float.MinValue;
            for (float dist = 2.2f; dist >= 1.4f; dist -= 0.2f) // (metres, not scaled: a close shot)
            for (int i = 0; i < 24; i++)
            {
                Vector3 dir = Quaternion.Euler(0f, i * 15f, 0f) * toEye;
                Vector3 p = subject + dir * dist + Vector3.up * 0.5f * s;
                if (Physics.CheckSphere(p, 0.22f * s, ~0, QueryTriggerInteraction.Ignore)) continue;
                if (!Physics.Raycast(p, Vector3.down, out var floor, 2.6f * s, ~0, QueryTriggerInteraction.Ignore) || floor.normal.y < 0.7f) continue;
                if (pass == 0 && Physics.Linecast(p, look, out var hit, ~0, QueryTriggerInteraction.Ignore) && Vector3.Distance(hit.point, look) > 0.12f * s) continue;
                if (pass == 0 && Physics.Linecast(p, also, out var hit2, ~0, QueryTriggerInteraction.Ignore) && Vector3.Distance(hit2.point, also) > 0.25f * s) continue;
                float score = 0.4f * Vector3.Dot(dir, toEye) + 2f * (1f - Mathf.Abs(Vector3.Dot(dir, side))) - 0.5f * Mathf.Abs(dist - 1.9f);
                if (score > bestScore) { bestScore = score; best = p; }
            }
            if (bestScore > float.MinValue) return best;
        }
        return eye;
    }

    /// <summary>The floor just off the edge of the counter, on your side.</summary>
    static Vector3 FloorBeside(Vector3 top, Vector3 eye)
    {
        float s = Scale;
        Vector3 dir = Flat(eye - top).normalized;
        for (float r = 0.35f; r < 1.4f; r += 0.15f)
        {
            Vector3 p = top + dir * r * s;
            if (Physics.Raycast(p + Vector3.up * 0.2f * s, Vector3.down, out var hit, 2.5f * s, ~0, QueryTriggerInteraction.Ignore)
                && hit.normal.y > 0.8f && hit.point.y < top.y - 0.3f * s) return hit.point;
        }
        return new Vector3(top.x, PlayerFeet(), top.z) + dir * 0.6f * s;
    }

    static float PlayerFeet()
    {
        var mover = FindObjectOfType<PlayerMovementAdvanced>();
        if (!mover) return 0f;
        var col = mover.GetComponentInChildren<CapsuleCollider>();
        return col ? col.bounds.min.y : mover.transform.position.y - 1f;
    }

    static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

    // ---------- the mop

    static Carryable FindMop() => FindObjectsOfType<Carryable>().FirstOrDefault(c => c.type == ItemType.Mop);

    IEnumerator MopStep()
    {
        Step = "mop";
        passReq = false;
        float s = Scale;
        // the spill (it might still be on its way down)
        float t0 = Time.unscaledTime;
        while (!appeared && !skipped)
        {
            if (Time.unscaledTime - t0 > 4f && !(glass && glass.Falling))
            {
                if (glass) Destroy(glass.gameObject);
                var pc = FindObjectOfType<PlayerCam>();
                Vector3 at = pc ? pc.transform.position + Flat(pc.transform.forward).normalized * 1.5f * s : transform.position;
                if (Physics.Raycast(at, Vector3.down, out var f, 3f * s, ~0, QueryTriggerInteraction.Ignore)) at = f.point;
                Spill.Create(at, 0.9f * s, "spilled drink");
                t0 = float.MaxValue; // (once)
            }
            yield return null;
        }
        TaskBase.Activated -= SpillAppeared;
        if (skipped) yield break;
        spill = appeared;

        if (!mopPrepared) PrepareMop(spill.transform.position);
        var mop = FindMop();

        while (spill && spill.IsActive && !skipped)
        {
            bool held = PlayerCarry.Instance && PlayerCarry.Instance.IsHolding(ItemType.Mop);
            if (!held && mop)
            {
                ScreenMarkers.Show("tutorial-mop", Top(mop.gameObject) + Vector3.up * 0.1f * s, "Mop", HudArt.MarkerOrange, true);
                Bar("2 / 3   COOKING  -  OOPS", $"Grab the <b>mop</b> (look at it, <b>E</b>) - the spill's in the <b>{SpillRoom()}</b>.");
            }
            else
            {
                ScreenMarkers.Show("tutorial-spill", spill.transform.position + Vector3.up * 0.15f * s, "Spill", HudArt.MarkerOrange, true);
                Bar("2 / 3   COOKING  -  OOPS", "Hold <b>LEFT MOUSE</b> and swipe the mop across the puddle.");
            }
            if (passReq) { passReq = false; spill.Complete(); if (PawPrints.Current && PawPrints.Current.IsActive) PawPrints.Current.Complete(); }
            yield return null;
        }
        if (skipped) yield break;
        // she ran through it: wet paw prints too
        var prints = PawPrints.Current;
        while (prints && prints.IsActive && !skipped)
        {
            // each print still there glows on the floor; the marker sits on the nearest one
            var left = prints.Remaining.ToList();
            ShowPrintRings(left);
            Vector3 eye = Camera.main ? Camera.main.transform.position : transform.position;
            if (left.Count > 0)
            {
                Vector3 nearest = left.OrderBy(p => (p - eye).sqrMagnitude).First();
                ScreenMarkers.Show("tutorial-prints", nearest + Vector3.up * 0.15f * s, $"Paw prints - {left.Count} left", HudArt.MarkerOrange, true);
            }
            Bar("2 / 3   COOKING  -  OOPS", $"{CatBrain.Name} ran through it - mop up her <b>wet paw prints</b> too: <b>{left.Count}</b> left.");
            if (passReq) { passReq = false; prints.Complete(); }
            yield return null;
        }
        ClearPrintRings();
        if (skipped) yield break;
        yield return Nice("All clean!");
    }

    bool mopPrepared;

    string SpillRoom()
    {
        var layout = HouseLayout.Instance;
        var r = layout && spill ? layout.RoomAt(spill.transform.position, layout.FloorAt(spill.transform.position.y)) : null;
        return r != null ? r.name.ToLowerInvariant() : "house";
    }

    /// <summary>The mop near the mess (if it's far off), and you facing the two of them.</summary>
    void PrepareMop(Vector3 mess)
    {
        mopPrepared = true;
        float s = Scale;
        var mop = FindMop();
        if (mop && !mop.IsHeld && Vector3.Distance(mop.transform.position, mess) > 6f * s && OpenFloorNear(mess, out var floor))
            mop.PlaceAt(floor, Random.Range(0f, 360f));
        var look = FindObjectOfType<PlayerCam>();
        if (!look) return;
        bool held = PlayerCarry.Instance && PlayerCarry.Instance.IsHolding(ItemType.Mop);
        look.LookTowards(mop && !held ? Top(mop.gameObject) - Vector3.up * 0.25f * s : mess);
    }

    // ---- a soft orange ring on the floor under each paw print still to mop

    readonly List<GameObject> printRings = new List<GameObject>();
    static Texture2D ringTex;
    static Mesh ringQuad;
    Material ringMat;

    void ShowPrintRings(List<Vector3> at)
    {
        float s = Scale;
        if (!ringTex)
        {
            const int N = 64;
            ringTex = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "Print ring" };
            for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(N / 2f, N / 2f)) / (N / 2f);
                float a = Mathf.Clamp01(1f - Mathf.Abs(d - 0.78f) / 0.16f) + 0.18f * Mathf.Clamp01(1f - d / 0.7f);
                ringTex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(a)));
            }
            ringTex.Apply();
        }
        if (!ringQuad)
        {
            ringQuad = new Mesh { name = "Print ring" };
            ringQuad.SetVertices(new List<Vector3> { new Vector3(-0.5f, 0f, -0.5f), new Vector3(0.5f, 0f, -0.5f), new Vector3(0.5f, 0f, 0.5f), new Vector3(-0.5f, 0f, 0.5f) });
            ringQuad.SetUVs(0, new List<Vector2> { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) });
            ringQuad.SetTriangles(new[] { 0, 2, 1, 0, 3, 2 }, 0);
            ringQuad.RecalculateNormals(); ringQuad.RecalculateBounds();
        }
        if (!ringMat) ringMat = new Material(Shader.Find("Sprites/Default")) { mainTexture = ringTex };
        while (printRings.Count < at.Count)
        {
            var g = new GameObject("Tutorial print ring");
            g.AddComponent<MeshFilter>().sharedMesh = ringQuad;
            var mr = g.AddComponent<MeshRenderer>();
            mr.sharedMaterial = ringMat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            printRings.Add(g);
        }
        float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 4f);
        ringMat.color = new Color(1f, 0.55f, 0.1f, Mathf.Lerp(0.45f, 0.85f, pulse));
        for (int i = 0; i < printRings.Count; i++)
        {
            bool on = i < at.Count;
            printRings[i].SetActive(on);
            if (!on) continue;
            printRings[i].transform.position = at[i] + Vector3.up * 0.006f * s; // (just above the print, which sits just above the floor)
            printRings[i].transform.localScale = Vector3.one * Mathf.Lerp(0.26f, 0.31f, pulse) * s;
        }
    }

    void ClearPrintRings()
    {
        foreach (var g in printRings) if (g) Destroy(g);
        printRings.Clear();
    }

    static Vector3 Top(GameObject g)
    {
        var rs = g.GetComponentsInChildren<Renderer>();
        if (rs.Length == 0) return g.transform.position;
        var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
        return new Vector3(b.center.x, b.max.y, b.center.z);
    }

    /// <summary>A bit of clear floor 0.8-2 m from this spot (on its storey).</summary>
    static bool OpenFloorNear(Vector3 spot, out Vector3 floor)
    {
        float s = Scale;
        for (float r = 0.8f; r <= 2.01f; r += 0.3f)
            for (int i = 0; i < 16; i++)
            {
                Vector3 p = spot + Quaternion.Euler(0f, i * 22.5f, 0f) * Vector3.forward * r * s;
                if (!Physics.Raycast(p + Vector3.up * 0.5f * s, Vector3.down, out var hit, 1.2f * s, ~0, QueryTriggerInteraction.Ignore)) continue;
                if (hit.normal.y < 0.85f || Mathf.Abs(hit.point.y - spot.y) > 0.25f * s) continue;
                if (Physics.CheckCapsule(hit.point + Vector3.up * 0.3f * s, hit.point + Vector3.up * 1.2f * s, 0.25f * s, ~0, QueryTriggerInteraction.Ignore)) continue;
                floor = hit.point;
                return true;
            }
        floor = spot;
        return false;
    }

    // ------------------------------------------------------------------ 3  laundry: the real thing, small

    LaundryTask practiceWash;
    int washPieces;
    float washTime, washOverflow, washLimit;

    /// <summary>A small practice wash: two dirty things round your room, to the washer in the laundry room (a quick wash),
    /// the clean clothes back to your wardrobe, fold one. You can only go as far as the washer.</summary>
    IEnumerator Laundry()
    {
        var laundry = FindObjectOfType<LaundryTask>();
        if (!laundry || laundry.IsActive || !laundry.Wardrobe) { yield return FoldOnly(); yield break; }
        Step = "laundry";
        passReq = false;
        HideBar();
        yield return FadeTo(1f, 0.3f);
        var mover = FindObjectOfType<PlayerMovementAdvanced>();
        if (mover && startPos != Vector3.zero)
        {
            var rb = mover.GetComponent<Rigidbody>();
            if (rb) { rb.velocity = Vector3.zero; rb.position = startPos; }
            mover.transform.position = startPos;
        }
        practiceWash = laundry;
        washPieces = laundry.pieces; washTime = laundry.washTime; washOverflow = laundry.overflowAfter; washLimit = laundry.timeLimit;
        laundry.pieces = 2; laundry.washTime = 6f; laundry.overflowAfter = 9999f; laundry.timeLimit = 9999f;
        GameManager.Instance.StartTask(laundry);
        yield return null;
        var layout = HouseLayout.Instance;
        var mine = MyRoom();
        var washer = layout ? layout.RoomAt(laundry.transform.position, layout.FloorAt(laundry.transform.position.y)) : null;
        var rooms = mine != null && washer != null ? RoomPath(layout, mine, washer) : null;
        if (rooms == null) rooms = mine != null ? new List<HouseLayout.RoomInfo> { mine } : new List<HouseLayout.RoomInfo>();
        BuildBarriers(rooms, "Not that way - just your room and the laundry room for now.");
        var look = FindObjectOfType<PlayerCam>();
        if (look && laundry.Pieces.Count > 0) look.LookTowards(laundry.Pieces[0].transform.position);
        yield return FadeTo(0f, 0.3f);

        bool done = false;
        System.Action<TaskBase, bool> ended = (t, ok) => { if (t == laundry) done = true; };
        TaskBase.Ended += ended;
        var wardrobe = laundry.Wardrobe;
        float s = Scale;
        const string title = "3 / 3   LAUNDRY";
        while (!done && !skipped)
        {
            BarrierNudge();
            var pi = PlayerInteraction.Instance;
            bool folding = pi && pi.InMainTask;
            Vector3 washerAt = Top(laundry.gameObject) + Vector3.up * 0.1f * s;
            switch (laundry.PhaseName)
            {
                case "WaitingForClothes":
                    if (laundry.PiecesLeft > 0)
                    {
                        for (int i = 0; i < laundry.Pieces.Count; i++)
                            if (laundry.Pieces[i]) ScreenMarkers.Show("tutorial-clothes-" + i, laundry.Pieces[i].transform.position + Vector3.up * 0.25f * s, "Dirty clothes", HudArt.MarkerOrange, i == 0);
                        Bar(title, $"Pick up the dirty clothes round your room (<b>E</b>) - <b>{laundry.PiecesLeft}</b> left.");
                    }
                    else
                    {
                        ScreenMarkers.Show("tutorial-washer", washerAt, "Washer", HudArt.MarkerOrange, true);
                        Bar(title, "Take them to the <b>washer</b> in the laundry room and press <b>E</b>.");
                    }
                    break;
                case "Washing":
                    ScreenMarkers.Show("tutorial-washer", washerAt, "Washing...", HudArt.MarkerOrange, true);
                    Bar(title, laundry.Status);
                    break;
                case "Done":
                    ScreenMarkers.Show("tutorial-washer", washerAt, "Take it out", HudArt.MarkerOrange, true);
                    Bar(title, "Done! Take the clean clothes out (<b>E</b>).");
                    break;
                default:
                    if (folding) Bar(title, "Fold the clothes: press the arrow key shown (or <b>W A S D</b>) as the ring closes on it.");
                    else
                    {
                        if (wardrobe) ScreenMarkers.Show("tutorial-wardrobe", Top(wardrobe.gameObject) + Vector3.up * 0.1f * s, "Wardrobe", HudArt.MarkerOrange, true);
                        Bar(title, "Back to your room: open your <b>wardrobe</b> (<b>E</b>), then <b>E</b> again to put them away.");
                    }
                    break;
            }
            if (passReq)
            {
                passReq = false;
                switch (laundry.PhaseName)
                {
                    case "WaitingForClothes":
                        if (laundry.PiecesLeft > 0) foreach (var p in laundry.Pieces.ToList()) laundry.PickUpPiece(p);
                        else laundry.Interact();
                        break;
                    case "Done": laundry.Interact(); break;
                    case "PutAway":
                        if (folding)
                        {
                            pi.ForceStopMainTask();
                            if (PlayerCarry.Instance && PlayerCarry.Instance.IsHolding(ItemType.CleanClothes)) { var c = PlayerCarry.Instance.TakeHeld(); if (c) c.ReturnHome(false); }
                            laundry.ClothesPutAway(1f);
                        }
                        else if (wardrobe) { if (!wardrobe.IsOpen) wardrobe.Interact(); wardrobe.Interact(); }
                        break;
                }
            }
            yield return null;
        }
        TaskBase.Ended -= ended;
        ClearBarriers();
        RestoreLaundry();
        if (skipped) yield break;
        yield return Nice("Laundry done!");
    }

    /// <summary>The practice wash cleared away, and the washer's settings back as they were.</summary>
    void RestoreLaundry()
    {
        if (!practiceWash) return;
        practiceWash.TutorialReset();
        practiceWash.pieces = washPieces; practiceWash.washTime = washTime; practiceWash.overflowAfter = washOverflow; practiceWash.timeLimit = washLimit;
        practiceWash = null;
    }

    /// <summary>(No washer to use - e.g. a replay with the laundry already on) just fold a shirt at your wardrobe.</summary>
    IEnumerator FoldOnly()
    {
        var desk = GamingPC.Instance ? GamingPC.Instance.transform.position : startPos;
        var wardrobe = FindObjectsOfType<Wardrobe>().OrderBy(w => Vector3.Distance(w.transform.position, desk)).FirstOrDefault(); // (yours: the one in your room)
        if (!wardrobe) yield break;
        Step = "laundry";
        passReq = false;
        HideBar();
        yield return GoTo(Middle(wardrobe.transform));
        Bar("3 / 3   LAUNDRY", "Fold the clothes: press the arrow key shown (or <b>W A S D</b>) as the ring closes on it.");
        yield return Minigame(go => FoldMinigame.Begin(q => go(true), () => go(false), garments: 1));
        if (skipped) yield break;
        yield return Nice("Folded!");
    }

    /// <summary>Starts a minigame (after a moment to read what to do) and waits for it; stopped early (Esc) = it starts again.</summary>
    IEnumerator Minigame(System.Action<System.Action<bool>> begin)
    {
        yield return Wait(1.6f);
        while (!skipped)
        {
            int result = 0; // 0 running, 1 done, -1 stopped
            begin(ok => result = ok ? 1 : -1);
            while (result == 0 && !skipped)
            {
                if (passReq) { passReq = false; if (PlayerInteraction.Instance && PlayerInteraction.Instance.InMainTask) PlayerInteraction.Instance.ForceStopMainTask(); result = 1; }
                yield return null;
            }
            if (result == 1 || skipped) yield break;
            Bar(barTitle.text, barBody.text + "\n<size=80%><i>Let's try that again...</i></size>");
            yield return Wait(1.2f);
        }
    }

    // ------------------------------------------------------------------ the end (done or skipped)

    IEnumerator Finish()
    {
        Step = "basics";
        HideBar();
        skipHint.gameObject.SetActive(false);
        var pi = PlayerInteraction.Instance;
        yield return FadeTo(1f, skipped ? 0.3f : 0.25f);

        // put everything back as it was (out of sight)
        EndCut();
        if (pi && pi.InMainTask) pi.ForceStopMainTask();
        if (pcArrow) Destroy(pcArrow);
        ClearBarriers();
        RestoreLaundry();
        PCDesktop.CloseTutorialClip();
        if (Cooking.Instance) Cooking.Instance.EndPractice();
        if (glass)
        {
            float until = Time.unscaledTime + 1.2f;
            while (glass && glass.Falling && Time.unscaledTime < until) yield return null; // (mid-air: let it land first)
            if (glass) Destroy(glass.gameObject);
        }
        yield return null; yield return null; // (a spill made just now starts itself a frame later)
        TaskBase.Activated -= SpillAppeared;
        foreach (var sp in FindObjectsOfType<Spill>()) if (sp.IsActive) sp.Complete();
        if (PawPrints.Current && PawPrints.Current.IsActive) PawPrints.Current.Complete();
        ClearPrintRings();
        var mop = FindMop();
        if (mop && !mop.IsHome) mop.ReturnHome(true);
        var mover = FindObjectOfType<PlayerMovementAdvanced>();
        if (mover && startPos != Vector3.zero)
        {
            var rb = mover.GetComponent<Rigidbody>();
            if (rb) { rb.velocity = Vector3.zero; rb.position = startPos; }
            mover.transform.position = startPos;
        }
        yield return null;
        FacePC();
        BarAtTop(false);

        if (!skipped)
        {
            // the finale: you stand still (no walking, no looking round), the HUD steps aside
            var hold = new Cutscene();
            if (pi) pi.BeginMainTask(hold);
            HudHider.Hide();
            yield return Finale();
            HudHider.Show();
            if (pi && ReferenceEquals(pi.CurrentMainTask, hold)) pi.ForceStopMainTask();
            FacePC();
        }

        // the day for real: full homework, jobs, Kitty back to herself
        Active = false;
        Step = "done";
        PlayerPrefs.SetInt(DoneKey, 1);
        PlayerPrefs.Save();
        Onboarding.AutoTips = true;
        var hw = FindObjectOfType<TypingTask>();
        if (hw)
        {
            if (replay && !hwWasActive) { if (hw.IsActive) hw.Complete(); } // (a replay after today's homework was done: it stays done)
            else if (hw.IsActive) hw.Regenerate();
            else GameManager.Instance.StartTask(hw);
        }
        GameManager.Instance.spawningPaused = false;
        if (CatBrain.Instance) CatBrain.Instance.KeepCalm(0f);
        if (Onboarding.Instance) Onboarding.Instance.AfterTutorial();
        if (fade.color.a > 0f) yield return FadeTo(0f, 0.4f);
        Destroy(canvas.gameObject);
    }

    // ------------------------------------------------------------------ the finale: "that's the basics", full screen

    static readonly (string title, string key, string body, Color tint)[] Tips =
    {
        ("TO-DO LIST", "TAB", "Your jobs pop up here.\nPress a number: footprints show the way.", HudArt.NoteYellow),
        ("THE CLOCK", "3 - 6 PM", "That's your afternoon.\nAt <b>4</b> your ranked match is on the VR headset.", HudArt.NoteBlue),
        ("MOM", "6 PM", "She comes home and checks everything.\nYou follow her round.", new Color32(255, 196, 170, 255)),
        ("KITTY & STRESS", "E", "Kitty makes trouble. Too much stress = a meltdown.\nPet her to calm down.", new Color32(198, 236, 178, 255)),
    };

    /// <summary>Over your room, dimmed: the title, a DONE stamp, four notes with the rest, and Enter to start the day.
    /// Everything comes in one after the other; Enter (or Esc / Space) sends it off.</summary>
    IEnumerator Finale()
    {
        Step = "basics";
        var font = GameHUD.Instance ? GameHUD.Instance.font : null;
        var root = UIFactory.Rect("Finale", canvas.transform);
        UIFactory.Fill(root);
        root.SetSiblingIndex(fade.transform.GetSiblingIndex()); // (under the fade)
        var group = root.gameObject.AddComponent<CanvasGroup>();
        group.alpha = 0f;
        var back = UIFactory.Panel("Backdrop", root, new Color(0.04f, 0.04f, 0.07f, 0.84f));
        UIFactory.Fill(back.rectTransform);

        var title = UIFactory.Text("Title", root, 66, Color.white, TextAlignmentOptions.Center, font);
        title.text = "<b>THAT'S THE BASICS</b>";
        title.enableWordWrapping = false;
        UIFactory.Place(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), new Vector2(0f, -170f), new Vector2(1100f, 86f));
        var sub = UIFactory.Text("Sub", root, 26, new Color(1f, 1f, 1f, 0.72f), TextAlignmentOptions.Center, font);
        sub.text = "Homework, dinner, a spill and the laundry: you've done it all once. Here's the rest:";
        sub.enableWordWrapping = false;
        UIFactory.Place(sub.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), new Vector2(0f, -240f), new Vector2(1500f, 40f));

        // DONE, stamped on
        var stamp = UIFactory.Panel("Stamp", root, new Color32(210, 40, 50, 235)).rectTransform;
        UIFactory.Place(stamp, new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), new Vector2(610f, -160f), new Vector2(230f, 86f));
        stamp.localRotation = Quaternion.Euler(0f, 0f, 12f);
        var inner = UIFactory.Panel("Inner", stamp, new Color32(246, 236, 236, 255));
        UIFactory.Fill(inner.rectTransform, 7, 7, 7, 7);
        var st = UIFactory.Text("Text", stamp, 52, new Color32(210, 40, 50, 255), TextAlignmentOptions.Center, font);
        st.text = "<b>DONE</b>";
        st.enableWordWrapping = false;
        UIFactory.Fill(st.rectTransform, 8, 8, 6, 6);
        stamp.gameObject.SetActive(false);

        // the four notes
        var notes = new List<RectTransform>();
        float[] tilt = { -2.5f, 1.8f, -1.5f, 2.4f };
        for (int i = 0; i < Tips.Length; i++)
        {
            var (t, key, body, tint) = Tips[i];
            var n = Note(root, "Tip " + t, new Vector2(0.5f, 0.5f), new Vector2(-615f + i * 410f, 10f), new Vector2(380f, 320f), tint);
            n.gameObject.AddComponent<CanvasGroup>().alpha = 0f;
            n.localRotation = Quaternion.Euler(0f, 0f, tilt[i]);
            var head = Label(n, "Title", 30, new Vector2(0f, -46f), new Vector2(340f, 42f), font);
            head.text = "<b>" + t + "</b>";
            var badge = UIFactory.Panel("Key", n, HudArt.Ink).rectTransform;
            UIFactory.Place(badge, new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), new Vector2(0f, -116f), new Vector2(190f, 66f));
            var kt = UIFactory.Text("Text", badge, 32, Color.white, TextAlignmentOptions.Center, font);
            kt.text = "<b>" + key + "</b>";
            kt.enableWordWrapping = false;
            UIFactory.Fill(kt.rectTransform, 6, 6, 4, 4);
            var txt = Label(n, "Body", 22, new Vector2(0f, -226f), new Vector2(330f, 130f), font);
            txt.text = body;
            notes.Add(n);
        }

        // off you go
        var go = Note(root, "Go", new Vector2(0.5f, 0f), new Vector2(0f, 120f), new Vector2(520f, 92f), new Color32(255, 170, 70, 255));
        var goText = Label(go, "Text", 32, new Vector2(0f, -48f), new Vector2(480f, 50f), font);
        goText.text = "<b>ENTER</b>   start Monday";
        var goGroup = go.gameObject.AddComponent<CanvasGroup>();
        goGroup.alpha = 0f;

        // in: the room dims, the title drops in, the notes one by one, the stamp, then the button
        StartCoroutine(FadeTo(0f, 0.35f));
        contReq = false;
        float t0 = Time.unscaledTime;
        bool stamped = false;
        while (true)
        {
            float t = Time.unscaledTime - t0;
            group.alpha = Mathf.Clamp01(t / 0.35f);
            float k = Mathf.Clamp01(t / 0.4f);
            title.rectTransform.anchoredPosition = new Vector2(0f, -170f + 40f * (1f - Smooth(k)));
            title.rectTransform.localScale = Vector3.one * Mathf.LerpUnclamped(0.85f, 1f, Back(k));
            for (int i = 0; i < notes.Count; i++)
            {
                float u = Mathf.Clamp01((t - 0.35f - i * 0.14f) / 0.32f);
                notes[i].GetComponent<CanvasGroup>().alpha = u;
                notes[i].localScale = Vector3.one * Mathf.LerpUnclamped(0.6f, 1f, Back(u));
                notes[i].anchoredPosition = new Vector2(-615f + i * 410f, 10f + 50f * (1f - Smooth(u)));
            }
            if (t >= 1.05f)
            {
                if (!stamped) { stamped = true; stamp.gameObject.SetActive(true); }
                float u = Mathf.Clamp01((t - 1.05f) / 0.2f);
                stamp.localScale = Vector3.one * Mathf.Lerp(2.4f, 1f, Smooth(u));
            }
            goGroup.alpha = Mathf.Clamp01((t - 1.3f) / 0.3f);
            go.localScale = Vector3.one * (t > 1.6f ? 1f + Mathf.Sin((t - 1.6f) * 4f) * 0.03f : 1f);
            bool press = Enter() || Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Escape);
            if (t > 1.0f && press) break;
            yield return null;
        }

        // out: everything lifts away and the room comes back
        float o0 = Time.unscaledTime;
        while (Time.unscaledTime - o0 < 0.4f)
        {
            float u = (Time.unscaledTime - o0) / 0.4f;
            group.alpha = 1f - Smooth(u);
            root.localScale = Vector3.one * (1f + 0.04f * u);
            yield return null;
        }
        Destroy(root.gameObject);
    }

    static float Smooth(float x) { x = Mathf.Clamp01(x); return x * x * (3f - 2f * x); }
    static float Back(float x) { x = Mathf.Clamp01(x); const float c = 1.7f; return 1f + (c + 1f) * Mathf.Pow(x - 1f, 3f) + c * Mathf.Pow(x - 1f, 2f); }

    void Update()
    {
        if (!Active || skipped || Step == "basics" || Step == "done" || !skipHint) return;
        if (skipReq) { skipReq = false; skipHeld = 1f; }
        // hold Delete: skip the rest
        skipHeld = Input.GetKey(KeyCode.Delete) ? skipHeld + Time.unscaledDeltaTime : skipHeld >= 1f ? skipHeld : 0f;
        skipHint.text = skipHeld > 0f ? $"Skipping... {Mathf.CeilToInt((1f - skipHeld) * 10f) / 10f:0.0}" : "Hold <b>DELETE</b> to skip the tutorial";
        if (skipHeld >= 1f)
        {
            skipped = true;
            if (PlayerInteraction.Instance && PlayerInteraction.Instance.InMainTask && cut == null) PlayerInteraction.Instance.ForceStopMainTask();
        }
    }

    // ------------------------------------------------------------------ moving you about

    /// <summary>Fades out, puts you on the floor right in front of this spot, facing it, and fades back in.</summary>
    IEnumerator GoTo(Vector3 spot)
    {
        yield return FadeTo(1f, 0.3f);
        var mover = FindObjectOfType<PlayerMovementAdvanced>();
        if (mover)
        {
            Vector3 stand = StandingSpot(mover, spot);
            Place(mover, stand, spot - stand);
            yield return null; // (the camera follows you there first)
            var pc = FindObjectOfType<PlayerCam>();
            if (pc) pc.LookTowards(spot + Vector3.up * 0.5f);
        }
        yield return null;
        yield return FadeTo(0f, 0.3f);
    }

    /// <summary>The nearest bit of open floor round this spot that you could stand on and see it from: rings of points from 0.8 m
    /// out, each with floor under it (below the spot - never on top of the desk), room for you (nothing in the way) and a clear
    /// line to the spot.</summary>
    static Vector3 StandingSpot(PlayerMovementAdvanced mover, Vector3 spot)
    {
        var col = mover.GetComponentInChildren<CapsuleCollider>();
        float radius = col ? Mathf.Max(0.25f, col.radius * mover.transform.lossyScale.x) : 0.35f;
        float height = col ? col.height * mover.transform.lossyScale.y : 1.8f;
        float s = Scale;
        Vector3 inward = RoomCentre(spot) - spot; inward.y = 0f;
        bool hasInward = inward.sqrMagnitude > 0.04f;
        inward.Normalize();
        for (int pass = hasInward ? 0 : 1; pass < 2; pass++)
        for (float r = 0.8f * s; r <= 2.6f * s; r += 0.2f * s)
            for (int i = 0; i < 24; i++)
            {
                float a = i * Mathf.PI * 2f / 24f;
                Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                if (pass == 0 && Vector3.Dot(dir, inward) < 0.55f) continue;   // (first: only out towards the middle of the room)
                Vector3 p = spot + dir * r;
                if (!Physics.Raycast(p + Vector3.up * 0.6f * s, Vector3.down, out var floor, 2.6f * s, ~0, QueryTriggerInteraction.Ignore)) continue;
                if (floor.normal.y < 0.85f || floor.point.y > spot.y - 0.3f * s || floor.point.y < spot.y - 2.2f * s) continue; // (a floor under it, on this storey)
                Vector3 f = floor.point;
                Vector3 a0 = f + Vector3.up * (radius + 0.1f), a1 = f + Vector3.up * (height - radius);
                bool blocked = false;
                foreach (var c in Physics.OverlapCapsule(a0, a1, radius * 1.1f, ~0, QueryTriggerInteraction.Ignore))
                    if (!c.transform.IsChildOf(mover.transform)) { blocked = true; break; }
                if (blocked) continue;
                Vector3 eye = f + Vector3.up * height * 0.9f, to = spot + Vector3.up * 0.3f * s;
                if (Physics.Linecast(eye, to, out var see, ~0, QueryTriggerInteraction.Ignore) && Vector3.Distance(see.point, to) > 0.6f * s) continue; // (a wall between)
                return f;
            }
        return spot;
    }

    /// <summary>The middle of a thing (its pivot may be on the floor).</summary>
    static Vector3 Middle(Transform t)
    {
        var rs = t.GetComponentsInChildren<Renderer>();
        if (rs.Length == 0) return t.position;
        var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
        return b.center;
    }

    /// <summary>The middle of the room this spot is in (the spot itself if it isn't in one).</summary>
    static Vector3 RoomCentre(Vector3 spot)
    {
        var layout = HouseLayout.Instance;
        if (!layout) return spot;
        Vector3 local = layout.transform.InverseTransformPoint(spot);
        foreach (var r in layout.rooms)
        {
            if (!r.rect.Contains(new Vector2(local.x, local.z))) continue;
            float floorY = r.floor * layout.storeyHeight;
            if (local.y < floorY - 0.5f || local.y > floorY + layout.storeyHeight) continue;
            return layout.transform.TransformPoint(new Vector3(r.rect.center.x, local.y, r.rect.center.y));
        }
        return spot;
    }

    static void Place(PlayerMovementAdvanced mover, Vector3 floorOrPos, Vector3 facing)
    {
        var col = mover.GetComponentInChildren<CapsuleCollider>();
        float pivot = col ? mover.transform.position.y - col.bounds.min.y : 1f;
        Vector3 at = floorOrPos + Vector3.up * (pivot + 0.05f);
        var rb = mover.GetComponent<Rigidbody>();
        if (rb) { rb.velocity = Vector3.zero; rb.position = at; }
        mover.transform.position = at;
        facing.y = 0f;
        var pc = FindObjectOfType<PlayerCam>();
        if (pc && facing.sqrMagnitude > 0.01f) pc.LookTowards(at + facing.normalized * 2f + Vector3.up * 0.4f);
    }

    IEnumerator Nice(string text)
    {
        GameHUD.Toast(text, GameHUD.Good);
        yield return Wait(0.9f);
    }

    static IEnumerator Wait(float s) { float until = Time.unscaledTime + s; while (Time.unscaledTime < until) yield return null; }

    // ------------------------------------------------------------------ the screen: a card, a bar (bottom, or top at the PC),
    // letterbox bars and a caption for the cutscene, the skip hint, a fade

    void Build()
    {
        canvas = UIFactory.CreateCanvas("Tutorial", 70);
        var root = (RectTransform)canvas.transform;
        var font = GameHUD.Instance ? GameHUD.Instance.font : null;

        card = Note(root, "Card", new Vector2(0.5f, 0.5f), new Vector2(0f, -70f), new Vector2(760f, 300f), HudArt.NoteBlue);
        cardTitle = Label(card, "Title", 30, new Vector2(0f, -38f), new Vector2(700f, 44f), font);
        cardTitle.fontStyle = FontStyles.Bold;
        cardBody = Label(card, "Body", 21, new Vector2(0f, -170f), new Vector2(690f, 220f), font);
        card.gameObject.SetActive(false);

        bar = Note(root, "Bar", new Vector2(0.5f, 0f), new Vector2(0f, 150f), new Vector2(820f, 104f), HudArt.NoteYellow);
        barTitle = Label(bar, "Title", 22, new Vector2(0f, -26f), new Vector2(780f, 30f), font);
        barTitle.fontStyle = FontStyles.Bold;
        barBody = Label(bar, "Body", 19, new Vector2(0f, -66f), new Vector2(780f, 54f), font);
        barBody.enableAutoSizing = true; barBody.fontSizeMax = 19f; barBody.fontSizeMin = 14f;
        bar.gameObject.SetActive(false);

        letterTop = UIFactory.Panel("Letterbox top", root, Color.black).rectTransform;
        UIFactory.Place(letterTop, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(4000f, 130f));
        letterBottom = UIFactory.Panel("Letterbox bottom", root, Color.black).rectTransform;
        UIFactory.Place(letterBottom, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), Vector2.zero, new Vector2(4000f, 160f));
        letterTop.gameObject.SetActive(false);
        letterBottom.gameObject.SetActive(false);
        caption = UIFactory.Text("Caption", root, 30, Color.white, TextAlignmentOptions.Center, font);
        caption.enableWordWrapping = false;
        UIFactory.Place(caption.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0.5f), new Vector2(0f, 112f), new Vector2(1500f, 44f));
        caption.gameObject.SetActive(false);

        skipHint = UIFactory.Text("Skip", root, 17, new Color(1f, 1f, 1f, 0.8f), TextAlignmentOptions.Center, font);
        skipHint.outlineWidth = 0.2f; skipHint.outlineColor = new Color32(0, 0, 0, 200);
        skipHint.text = "Hold <b>DELETE</b> to skip the tutorial";
        UIFactory.Place(skipHint.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 42f), new Vector2(600f, 28f));
        skipHint.gameObject.SetActive(false);

        fade = UIFactory.Panel("Fade", root, new Color(0f, 0f, 0f, 0f));
        fade.raycastTarget = false;
        UIFactory.Fill(fade.rectTransform);
    }

    static RectTransform Note(RectTransform parent, string name, Vector2 anchor, Vector2 at, Vector2 size, Color tint)
    {
        var rt = UIFactory.Rect(name, parent);
        UIFactory.Place(rt, anchor, new Vector2(0.5f, anchor.y), at, size);
        var paper = UIFactory.Panel("Paper", rt, tint);
        paper.sprite = HudArt.Get("note_paper");
        paper.type = Image.Type.Sliced;
        paper.raycastTarget = false;
        UIFactory.Fill(paper.rectTransform);
        var tape = UIFactory.Panel("Tape", rt, Color.white);
        tape.sprite = HudArt.Get("tape");
        tape.raycastTarget = false;
        UIFactory.Place(tape.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), new Vector2(0f, -2f), new Vector2(130f, 32f));
        return rt;
    }

    static TextMeshProUGUI Label(RectTransform parent, string name, float size, Vector2 at, Vector2 box, TMP_FontAsset font)
    {
        var t = UIFactory.Text(name, parent, size, HudArt.Ink, TextAlignmentOptions.Center, font);
        t.enableWordWrapping = true;
        t.raycastTarget = false;
        UIFactory.Place(t.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), at, box);
        return t;
    }

    void ShowCard(string title, string body)
    {
        cardTitle.text = title;
        cardBody.text = body;
        card.gameObject.SetActive(true);
    }

    void HideCard() => card.gameObject.SetActive(false);

    void Bar(string title, string body)
    {
        if (barTitle.text != title) barTitle.text = title;
        if (barBody.text != body) barBody.text = body;
        bar.gameObject.SetActive(true);
    }

    void HideBar() => bar.gameObject.SetActive(false);

    /// <summary>At the PC the bar goes up top (off the monitor); everywhere else it's at the bottom.</summary>
    void BarAtTop(bool top)
    {
        float y = top ? 1f : 0f;
        if (bar.anchorMin.y == y) return;
        UIFactory.Place(bar, new Vector2(0.5f, y), new Vector2(0.5f, y), new Vector2(0f, top ? -6f : 150f), bar.sizeDelta);
        bar.localScale = Vector3.one * (top ? 0.9f : 1f); // (up there: clear of the monitor)
    }

    void SetCaption(string text)
    {
        caption.text = text;
        caption.gameObject.SetActive(true);
    }

    IEnumerator FadeTo(float alpha, float seconds)
    {
        float from = fade.color.a;
        for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / seconds)
        {
            fade.color = new Color(0f, 0f, 0f, Mathf.Lerp(from, alpha, t));
            yield return null;
        }
        fade.color = new Color(0f, 0f, 0f, alpha);
    }
}
