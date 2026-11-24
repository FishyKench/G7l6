using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// "Where do I go?" - on request, never by itself. Hold TAB and the whole to-do list opens (the HUD normally shows just
/// the most urgent note); each note gets a number. Press that number (still holding TAB) and a trail of glowing shoe prints
/// runs along the floor from your feet to the task - round furniture, through doors, up the stairs - and fades once you
/// get there. One trail at a time.
/// The very first day, the first chore to come up gets its trail once by itself, so new players learn the trick.
/// Created by the GameManager.
/// </summary>
public class TaskGuide : MonoBehaviour
{
    public static TaskGuide Instance { get; private set; }
    /// <summary>The full to-do list is open (TAB held).</summary>
    public static bool ListOpen => ItemFinder.Showing;
    /// <summary>The task the footprints lead to right now (null: none).</summary>
    public TaskBase Guiding => prints.Count > 0 ? target : null;

    public Color chalk = new Color(1f, 0.62f, 0.25f, 0.95f);
    public float spacing = 0.55f;

    const string AutoKey = "G7l6.guide.autoShown";
    readonly List<Transform> prints = new List<Transform>();
    readonly List<TaskBase> listed = new List<TaskBase>();
    readonly HashSet<Transform> fading = new HashSet<Transform>();
    TaskBase target;
    Vector3 goal;
    float until;
    Coroutine laying;
    Transform player;
    MaterialPropertyBlock block;

    void Awake() => Instance = this;

    void Start()
    {
        var m = FindObjectOfType<PlayerMovementAdvanced>();
        if (m) player = m.transform;
        block = new MaterialPropertyBlock();
        TaskBase.Activated += OnActivated;
    }

    void OnDestroy() => TaskBase.Activated -= OnActivated;

    /// <summary>The list the HUD is showing right now, in its order (the number keys pick from it).</summary>
    public void SetListed(List<TaskBase> tasks) { listed.Clear(); listed.AddRange(tasks); }

    void OnActivated(TaskBase t)
    {
        // day one: the first chore that comes up shows its trail once by itself (then it's up to you: TAB)
        if (t.isMainTask || PlayerPrefs.GetInt(AutoKey, 0) == 1 || TutorialDay.Active) return;
        if (Campaign.Week != 1 || Campaign.DayIndex != 0) return;
        PlayerPrefs.SetInt(AutoKey, 1);
        StartCoroutine(AutoShow(t));
    }

    IEnumerator AutoShow(TaskBase t)
    {
        yield return new WaitForSeconds(1.2f);
        if (!t || !t.IsActive) yield break;
        Show(t);
        GameHUD.Toast("Follow the footprints to " + t.taskName + ". Hold TAB and press a number: they'll show you the way to any job.", GameHUD.Info);
    }

    void Update()
    {
        if (!player) { var m = FindObjectOfType<PlayerMovementAdvanced>(); if (m) player = m.transform; else return; }
        if (ListOpen)
            for (int i = 0; i < listed.Count && i < 9; i++)
                if (Input.GetKeyDown(KeyCode.Alpha1 + i) || Input.GetKeyDown(KeyCode.Keypad1 + i)) { Show(listed[i]); break; }

        if (prints.Count == 0) return;
        Vector3 feet = Feet();
        bool done = !target || !target.IsActive || Time.time > until || (Flat(feet - goal).magnitude < 1.6f && Mathf.Abs(feet.y - goal.y) < 2f);
        if (done) { Clear(); return; }
        // prints you've walked past fade away
        foreach (var p in prints)
        {
            if (!p || fading.Contains(p)) continue;
            if (Flat(p.position - feet).magnitude < 0.6f && Mathf.Abs(p.position.y - feet.y) < 1.2f) { fading.Add(p); StartCoroutine(FadeTo(p, 0f, 0.4f)); }
        }
    }

    /// <summary>Lay a trail to this task.</summary>
    public void Show(TaskBase task)
    {
        if (!task || !player) return;
        Clear();
        target = task;
        goal = task.Where;
        until = Time.time + 40f;
        laying = StartCoroutine(Lay(Route(Feet(), Ground(goal))));
    }

    void Clear()
    {
        if (laying != null) StopCoroutine(laying);
        laying = null;
        foreach (var p in prints) if (p) Destroy(p.gameObject);
        prints.Clear();
        fading.Clear();
        target = null;
    }

    List<Vector3> Route(Vector3 from, Vector3 to)
    {
        var path = new NavMeshPath();
        var filter = MomNav.Ready ? MomNav.Filter : new NavMeshQueryFilter { agentTypeID = 0, areaMask = NavMesh.AllAreas };
        if (NavMesh.SamplePosition(from, out var a, 2f, filter) && NavMesh.SamplePosition(to, out var b, 3f, filter)
            && NavMesh.CalculatePath(a.position, b.position, filter, path) && path.corners.Length > 1)
            return new List<Vector3>(path.corners);
        return new List<Vector3> { from, to }; // (no walkable route found: straight there)
    }

    IEnumerator Lay(List<Vector3> corners)
    {
        // walk the route putting a shoe print down every step, left foot and right foot in turn
        bool left = false;
        float carry = 0.3f;
        for (int c = 0; c + 1 < corners.Count; c++)
        {
            Vector3 a = corners[c], b = corners[c + 1];
            float len = Vector3.Distance(a, b);
            Vector3 dir = len > 0.001f ? (b - a) / len : Vector3.forward;
            Vector3 flatDir = Flat(dir).sqrMagnitude > 0.0001f ? Flat(dir).normalized : Vector3.forward;
            Vector3 side = Vector3.Cross(Vector3.up, flatDir) * 0.11f;
            for (float d = carry; d < len; d += spacing)
            {
                Vector3 at = a + dir * d + (left ? -side : side);
                left = !left;
                Print(at, flatDir, !left);
                yield return new WaitForSeconds(0.03f);
            }
            carry = Mathf.Repeat(carry - len, spacing);
        }
    }

    void Print(Vector3 at, Vector3 facing, bool leftFoot)
    {
        // sit it right on the floor (or the stair) under that point
        if (Physics.Raycast(at + Vector3.up * 0.8f, Vector3.down, out var hit, 2f, ~0, QueryTriggerInteraction.Ignore)) at = hit.point;
        var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
        q.name = "Guide step";
        Destroy(q.GetComponent<Collider>());
        var r = q.GetComponent<Renderer>();
        r.sharedMaterial = ShoeMaterial();
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        q.transform.SetParent(transform, true);
        q.transform.position = at + Vector3.up * 0.015f;
        q.transform.rotation = Quaternion.LookRotation(Vector3.down, facing); // flat on the floor, toes pointing the way
        q.transform.localScale = new Vector3(leftFoot ? -0.13f : 0.13f, 0.3f, 1f); // (a left shoe is a mirrored right one)
        prints.Add(q.transform);
        StartCoroutine(FadeTo(q.transform, 1f, 0.25f));
    }

    IEnumerator FadeTo(Transform p, float to, float time)
    {
        var r = p ? p.GetComponent<Renderer>() : null;
        if (!r) yield break;
        float from = to > 0.5f ? 0f : chalk.a;
        for (float t = 0f; t < 1f && p; t += Time.deltaTime / time)
        {
            var c = chalk; c.a = Mathf.Lerp(from, to * chalk.a, t);
            block.SetColor("_Color", c);
            r.SetPropertyBlock(block);
            yield return null;
        }
        if (p && to <= 0f) p.gameObject.SetActive(false);
    }

    /// <summary>The player's feet (the body's middle is up at chest height).</summary>
    Vector3 Feet()
    {
        var cc = player.GetComponent<Collider>();
        return cc ? new Vector3(player.position.x, cc.bounds.min.y, player.position.z) : player.position + Vector3.down;
    }

    static Material shoe;

    /// <summary>A shoe print: a sole (wide at the ball of the foot, narrow waist, round heel) with tread lines across it.</summary>
    static Material ShoeMaterial()
    {
        if (shoe) return shoe;
        const int W = 48, H = 112;
        var tex = new Texture2D(W, H, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, name = "Shoe print" };
        var px = new Color32[W * H];
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                float u = (x + 0.5f) / W, v = (y + 0.5f) / H;
                // the sole's half-width along its length: heel, waist, ball, toe (a right shoe: the big-toe side bulges left)
                float half = v < 0.27f ? Mathf.Sqrt(Mathf.Max(0f, 1f - Mathf.Pow((v - 0.15f) / 0.13f, 2f))) * 0.3f
                           : v < 0.5f ? Mathf.Lerp(0.27f, 0.33f, (v - 0.27f) / 0.23f)
                           : Mathf.Sqrt(Mathf.Max(0f, 1f - Mathf.Pow((v - 0.66f) / 0.3f, 2f))) * 0.42f;
                float centre = v > 0.5f ? 0.5f - (v - 0.5f) * 0.12f : 0.5f;
                float edge = half - Mathf.Abs(u - centre);
                float a = Mathf.Clamp01(edge * 30f);
                bool heelGap = v > 0.29f && v < 0.36f;                       // the step between heel and sole
                bool tread = !heelGap && Mathf.Repeat(v * 22f, 1f) < 0.32f; // tread lines
                a *= heelGap ? 0.25f : tread ? 0.55f : 1f;
                px[y * W + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
        tex.SetPixels32(px);
        tex.Apply(true);
        shoe = new Material(Shader.Find("Sprites/Default")) { mainTexture = tex };
        return shoe;
    }

    static Vector3 Ground(Vector3 p) => Physics.Raycast(p + Vector3.up * 0.5f, Vector3.down, out var hit, 3f, ~0, QueryTriggerInteraction.Ignore) ? hit.point : p;
    static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }
}
