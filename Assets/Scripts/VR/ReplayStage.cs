using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// Plays a recorded round back inside the loaded VR arena map (the VRArena scene is always loaded next to the
/// house, so the real map is there at x = 5000; nothing else has to be built): lightweight stand-in figures (collider
/// free, so they can never touch the live match) are posed from the recording and a camera films them into a
/// RenderTexture for the Clip Studio window. Everything is drawn from the time you ask for (no state), so
/// scrubbing and jumping around work. Tracers, muzzle flashes, kill pops, grenades and flag markers come from the
/// recorded events.
///
/// It only films while the arena is idle (<see cref="Available"/>): during a live round, a warm-up or while you wear
/// the headset it refuses, and the live fighters are hidden from the replay camera for the instant it renders.
/// </summary>
public class ReplayStage : MonoBehaviour
{
    public enum CamMode { First, Third, Orbit }

    public static ReplayStage Instance { get; private set; }

    public const int PreviewW = 640, PreviewH = 360, ThumbW = 256, ThumbH = 144;

    public static ReplayStage Get()
    {
        if (Instance) return Instance;
        var go = new GameObject("Replay Stage");
        Instance = go.AddComponent<ReplayStage>();
        return Instance;
    }

    class Figure
    {
        public Transform root, visual, gun, flash;
        public GameObject flashObject;
        public int team;
    }

    public RenderTexture Texture { get; private set; }
    public ReplayRound Round { get; private set; }
    public bool Loaded => Round != null;

    Camera cam;
    RenderTexture thumbTexture;
    readonly List<Figure> figures = new List<Figure>();
    Transform[] flagMarkers = new Transform[0];
    TextMeshPro[] flagTexts = new TextMeshPro[0];
    Renderer[][] flagRenderers = new Renderer[0][];
    readonly List<LineRenderer> tracers = new List<LineRenderer>();
    readonly List<Transform> fxBalls = new List<Transform>();
    readonly List<Renderer> fxBallRenderers = new List<Renderer>();
    readonly List<Renderer> hiddenLive = new List<Renderer>();
    readonly List<Renderer> scratch = new List<Renderer>();
    Transform viewGun, viewFlash;
    GameObject viewGunObject;
    static readonly RaycastHit[] castHits = new RaycastHit[24];
    Material tracerMaterial;
    int tracerUsed, ballUsed;

    float lastTouch;

    /// <summary>The Clip Studio calls this every frame it is showing; without it the stand-ins go away by themselves.</summary>
    public void Touch() => lastTouch = Time.unscaledTime;

    void Update()
    {
        if (Round == null || PauseMenu.IsPaused) return;
        if (!Available || Time.unscaledTime - lastTouch > 0.6f) Unload(); // never leave stand-ins in a map somebody plays in
    }

    // ------------------------------------------------------------------ availability

    /// <summary>The arena is loaded and nobody is using it (no round running, not in the headset).</summary>
    public static bool Available => string.IsNullOrEmpty(Unavailable);

    public static string Unavailable
    {
        get
        {
            var arena = VRArena.Instance;
            if (!arena || !arena.Ready) return "The VR arena is still loading...";
            if (arena.State != VRArena.Mode.Idle) return "A round is live - the preview comes back when it ends.";
            if (arena.Player && arena.Player.controlled) return "You are in the headset.";
            return null;
        }
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (Texture) Texture.Release();
        if (thumbTexture) thumbTexture.Release();
    }

    // ------------------------------------------------------------------ loading

    public void Load(ReplayRound round)
    {
        if (Round == round && figures.Count == round.fighters) return;
        Unload();
        Round = round;
        EnsureCamera();
        for (int i = 0; i < round.fighters; i++) figures.Add(BuildFigure(round.teams[i], i == 0));
        flagMarkers = new Transform[round.flagPos.Length];
        flagTexts = new TextMeshPro[round.flagPos.Length];
        flagRenderers = new Renderer[round.flagPos.Length][];
        for (int i = 0; i < round.flagPos.Length; i++) BuildFlagMarker(i);
    }

    /// <summary>Takes the stand-ins away (the window closed, or the arena is needed).</summary>
    public void Unload()
    {
        Round = null;
        foreach (var f in figures) if (f.root) Destroy(f.root.gameObject);
        figures.Clear();
        foreach (var m in flagMarkers) if (m) Destroy(m.gameObject);
        flagMarkers = new Transform[0];
        flagTexts = new TextMeshPro[0];
        flagRenderers = new Renderer[0][];
        foreach (var t in tracers) if (t) Destroy(t.gameObject);
        tracers.Clear();
        foreach (var b in fxBalls) if (b) Destroy(b.gameObject);
        fxBalls.Clear();
        fxBallRenderers.Clear();
        tracerUsed = ballUsed = 0;
        if (viewGunObject) viewGunObject.SetActive(false);
    }

    void EnsureCamera()
    {
        if (cam) return;
        var go = new GameObject("Replay Camera");
        go.transform.SetParent(transform, false);
        cam = go.AddComponent<Camera>();
        cam.enabled = false; // rendered by hand, only when the Clip Studio asks
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.07f, 0.08f, 0.14f);
        cam.nearClipPlane = 0.05f;
        cam.farClipPlane = 400f;
        cam.cullingMask = ~(1 << 5);
        cam.allowHDR = false;
        cam.allowMSAA = true;
        cam.depth = -50f;
        Texture = new RenderTexture(PreviewW, PreviewH, 24, RenderTextureFormat.ARGB32) { name = "Replay Preview", antiAliasing = 2, useMipMap = false };
        Texture.Create();
        thumbTexture = new RenderTexture(ThumbW, ThumbH, 16, RenderTextureFormat.ARGB32) { name = "Replay Thumb", antiAliasing = 2 };
        thumbTexture.Create();
        tracerMaterial = new Material(Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color"));

        // Your gun, shown in first person.
        viewGunObject = new GameObject("View gun");
        viewGunObject.transform.SetParent(cam.transform, false);
        viewGun = viewGunObject.transform;
        viewGun.localPosition = new Vector3(0.22f, -0.2f, 0.42f);
        var dark = VRArena.Solid(new Color(0.1f, 0.1f, 0.13f), 0.7f);
        var mid = VRArena.Solid(new Color(0.2f, 0.21f, 0.26f), 0.6f);
        var neon = VRArena.Glow(new Color(0.1f, 0.5f, 0.6f), new Color(0.3f, 1f, 0.9f), 2.2f);
        Part(viewGun, PrimitiveType.Cube, new Vector3(0f, 0f, 0.02f), new Vector3(0.07f, 0.09f, 0.55f), dark);
        Part(viewGun, PrimitiveType.Cube, new Vector3(0f, -0.09f, -0.07f), new Vector3(0.05f, 0.15f, 0.07f), mid);
        Part(viewGun, PrimitiveType.Cube, new Vector3(0f, -0.07f, 0.12f), new Vector3(0.045f, 0.13f, 0.08f), mid);
        Part(viewGun, PrimitiveType.Cube, new Vector3(0.0385f, 0.005f, 0.08f), new Vector3(0.004f, 0.018f, 0.3f), neon);
        Part(viewGun, PrimitiveType.Cube, new Vector3(-0.0385f, 0.005f, 0.08f), new Vector3(0.004f, 0.018f, 0.3f), neon);
        Part(viewGun, PrimitiveType.Cube, new Vector3(0f, -0.015f, 0.34f), new Vector3(0.035f, 0.035f, 0.16f), dark);
        Part(viewGun, PrimitiveType.Cube, new Vector3(0f, 0.02f, -0.27f), new Vector3(0.06f, 0.1f, 0.14f), mid);
        var f = Part(viewGun, PrimitiveType.Quad, new Vector3(0f, -0.015f, 0.46f), Vector3.one * 0.2f, VRArena.Glow(new Color(1f, 0.85f, 0.5f), new Color(1f, 0.7f, 0.3f), 8f));
        viewFlash = f.transform;
        viewFlash.gameObject.SetActive(false);
        viewGunObject.SetActive(false);
    }

    static GameObject Part(Transform parent, PrimitiveType type, Vector3 pos, Vector3 scale, Material m)
    {
        var p = GameObject.CreatePrimitive(type);
        var col = p.GetComponent<Collider>();
        if (col) Destroy(col);
        p.transform.SetParent(parent, false);
        p.transform.localPosition = pos;
        p.transform.localScale = scale;
        var r = p.GetComponent<Renderer>();
        r.sharedMaterial = m;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        return p;
    }

    Figure BuildFigure(int team, bool you)
    {
        var f = new Figure { team = team };
        Color c = team == 0 ? VRArena.AllyColor : VRArena.EnemyColor;
        if (you) c = new Color(0.35f, 1f, 0.9f);
        var root = new GameObject(you ? "Stand-in (you)" : "Stand-in");
        root.transform.SetParent(transform, false);
        f.root = root.transform;
        f.visual = new GameObject("Visual").transform;
        f.visual.SetParent(f.root, false);
        var body = VRArena.Glow(c * 0.55f, c, 0.9f);
        var dark = VRArena.Solid(new Color(0.06f, 0.06f, 0.09f), 0.8f);
        Part(f.visual, PrimitiveType.Capsule, new Vector3(0f, 0.8f, 0f), new Vector3(0.72f, 0.8f, 0.6f), body);
        Part(f.visual, PrimitiveType.Cube, new Vector3(0f, 1.0f, -0.3f), new Vector3(0.45f, 0.5f, 0.18f), dark);
        Part(f.visual, PrimitiveType.Sphere, new Vector3(0f, 1.62f, 0f), Vector3.one * 0.46f, body);
        Part(f.visual, PrimitiveType.Cube, new Vector3(0f, 1.65f, 0.17f), new Vector3(0.38f, 0.14f, 0.16f), VRArena.Glow(Color.black, Color.white, 1.4f));
        f.gun = Part(f.visual, PrimitiveType.Cube, new Vector3(0.28f, 1.1f, 0.45f), new Vector3(0.09f, 0.12f, 0.7f), dark).transform;
        var flash = Part(f.visual, PrimitiveType.Quad, new Vector3(0.28f, 1.1f, 0.9f), Vector3.one * 0.3f, VRArena.Glow(new Color(1f, 0.85f, 0.5f), new Color(1f, 0.7f, 0.3f), 8f));
        f.flash = flash.transform;
        f.flashObject = flash;
        flash.SetActive(false);
        return f;
    }

    void BuildFlagMarker(int i)
    {
        var root = new GameObject("Flag marker " + Round.flagLetters[i]).transform;
        root.SetParent(transform, false);
        root.position = Round.flagPos[i] + Vector3.up * 3.3f;
        var gem = Part(root, PrimitiveType.Cube, Vector3.zero, Vector3.one * 0.55f, VRArena.Glow(VRFlag.Neutral * 0.5f, VRFlag.Neutral, 1.2f));
        gem.transform.localRotation = Quaternion.Euler(45f, 45f, 0f);
        var pole = Part(root, PrimitiveType.Cylinder, new Vector3(0f, -1.65f, 0f), new Vector3(0.07f, 1.65f, 0.07f), VRArena.Glow(VRFlag.Neutral * 0.5f, VRFlag.Neutral, 1.2f));
        var tmp = new GameObject("Letter").AddComponent<TextMeshPro>();
        tmp.transform.SetParent(root, false);
        tmp.transform.localPosition = new Vector3(0f, 0.85f, 0f);
        if (GameHUD.Instance && GameHUD.Instance.font) tmp.font = GameHUD.Instance.font;
        tmp.text = Round.flagLetters[i];
        tmp.fontSize = 7f;
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.rectTransform.sizeDelta = new Vector2(2f, 1.2f);
        flagMarkers[i] = root;
        flagTexts[i] = tmp;
        flagRenderers[i] = new[] { gem.GetComponent<Renderer>(), pole.GetComponent<Renderer>() };
    }

    // ------------------------------------------------------------------ rendering

    /// <summary>Draws the moment at 'time' (seconds into the round) into the preview texture. False if it can't (arena busy).</summary>
    public bool Show(float time, CamMode mode)
    {
        if (Round == null || !Available) return false;
        Render(time, mode, Texture);
        return true;
    }

    /// <summary>A small picture of a moment (for thumbnails). Caller owns the returned texture. Null if the arena is busy.</summary>
    public Texture2D Snapshot(float time, CamMode mode)
    {
        if (Round == null || !Available) return null;
        Render(time, mode, thumbTexture);
        var prev = RenderTexture.active;
        RenderTexture.active = thumbTexture;
        var tex = new Texture2D(ThumbW, ThumbH, TextureFormat.RGB24, false) { wrapMode = TextureWrapMode.Clamp };
        tex.ReadPixels(new Rect(0, 0, ThumbW, ThumbH), 0, 0);
        tex.Apply();
        RenderTexture.active = prev;
        return tex;
    }

    void Render(float time, CamMode mode, RenderTexture target)
    {
        time = Mathf.Clamp(time, 0f, Round.duration);
        EnsureCamera();
        PlaceCamera(time, mode);
        PoseFigures(time, mode);
        DrawEffects(time, mode);
        DrawFlags(time);

        // The live fighters sit in the arena too: hide them from this one render.
        hiddenLive.Clear();
        foreach (var f in VRFighter.All)
        {
            if (!f) continue;
            f.GetComponentsInChildren(true, scratch);
            foreach (var r in scratch) if (r && r.enabled) { r.enabled = false; hiddenLive.Add(r); }
        }
        cam.targetTexture = target;
        cam.Render();
        cam.targetTexture = null;
        foreach (var r in hiddenLive) if (r) r.enabled = true;
        hiddenLive.Clear();
    }

    // ------------------------------------------------------------------ camera

    void PlaceCamera(float t, CamMode mode)
    {
        var tr = cam.transform;
        bool first = mode == CamMode.First;
        viewGunObject.SetActive(first);
        if (first)
        {
            Round.SampleCamera(t, out Vector3 p, out Quaternion rot, out float fov);
            tr.SetPositionAndRotation(p, rot);
            cam.fieldOfView = fov;
            Round.Sample(t, 0, out _, out _, out byte st);
            bool alive = (st & ReplayRound.Alive) != 0;
            viewGun.gameObject.SetActive(alive);
            viewFlash.gameObject.SetActive(alive && ShotAge(0, t) < 0.05f);
            return;
        }
        Round.Sample(t, 0, out Vector3 you, out float yawNow, out _);
        cam.fieldOfView = 68f;
        if (mode == CamMode.Third)
        {
            // Behind and above you; the facing is averaged over the last half second so it doesn't jitter.
            float sx = 0f, sz = 0f;
            for (int i = 0; i < 6; i++)
            {
                Round.Sample(Mathf.Max(0f, t - i * 0.1f), 0, out _, out float y, out _);
                sx += Mathf.Sin(y * Mathf.Deg2Rad); sz += Mathf.Cos(y * Mathf.Deg2Rad);
            }
            Vector3 dir = new Vector3(sx, 0f, sz);
            dir = dir.sqrMagnitude < 0.0001f ? Quaternion.Euler(0f, yawNow, 0f) * Vector3.forward : dir.normalized;
            Vector3 head = you + Vector3.up * 1.5f;
            Vector3 want = head - dir * 3.4f + Vector3.up * 0.9f + Vector3.Cross(Vector3.up, dir) * -0.45f;
            Vector3 pos = Pull(head, want);
            tr.position = pos;
            tr.rotation = Quaternion.LookRotation((you + Vector3.up * 1.35f + dir * 3.5f) - pos, Vector3.up);
            return;
        }
        // Orbit: a slow circle round you, leaning towards the latest kill so the action stays in frame.
        Vector3 focus = Vector3.zero;
        for (int i = 0; i < 5; i++) { Round.Sample(Mathf.Max(0f, t - i * 0.12f), 0, out Vector3 q, out _, out _); focus += q / 5f; }
        float bias = 0f;
        Vector3 target = focus;
        foreach (var e in Round.events)
        {
            if (e.t > t + 1.2f) break;
            if (e.kind != ReplayEvent.Kill || t < e.t - 1.2f) continue;
            float w = 1f - Mathf.Clamp01(Mathf.Abs(t - e.t - 0.2f) / 1.4f);
            if (w > bias) { bias = w; target = new Vector3(e.p0.x, focus.y, e.p0.z); }
        }
        Vector3 centre = Vector3.Lerp(focus, target, bias * 0.5f);
        float ang = Mathf.Deg2Rad * (yawNow + 180f + 30f) + t * 0.38f;
        float radius = 6.5f;
        Vector3 aim = centre + Vector3.up * 1.2f;
        Vector3 desired = aim + new Vector3(Mathf.Sin(ang) * radius, 2.3f, Mathf.Cos(ang) * radius);
        Vector3 orbitPos = Pull(aim, desired);
        tr.position = orbitPos;
        tr.rotation = Quaternion.LookRotation(aim - orbitPos, Vector3.up);
    }

    /// <summary>Pulls a camera spot in so it never ends up inside a wall (live fighters' colliders don't count).</summary>
    static Vector3 Pull(Vector3 from, Vector3 to)
    {
        Vector3 d = to - from;
        float len = d.magnitude;
        if (len < 0.01f) return to;
        Vector3 dir = d / len;
        int n = Physics.SphereCastNonAlloc(from, 0.22f, dir, castHits, len, ~0, QueryTriggerInteraction.Ignore);
        float best = len;
        for (int i = 0; i < n; i++)
        {
            var h = castHits[i];
            if (h.distance <= 0f || h.collider.GetComponentInParent<VRFighter>()) continue;
            best = Mathf.Min(best, h.distance);
        }
        return best < len ? from + dir * Mathf.Max(0.35f, best - 0.08f) : to;
    }

    // ------------------------------------------------------------------ figures

    float ShotAge(int fighter, float t)
    {
        float best = 99f;
        var ev = Round.events;
        for (int i = FirstEvent(t - 0.3f); i < ev.Count; i++)
        {
            var e = ev[i];
            if (e.t > t) break;
            if (e.kind == ReplayEvent.Shot && e.a == fighter) best = Mathf.Min(best, t - e.t);
        }
        return best;
    }

    /// <summary>Index of the first event at or after a time (events are sorted).</summary>
    int FirstEvent(float time)
    {
        var ev = Round.events;
        int lo = 0, hi = ev.Count;
        while (lo < hi)
        {
            int mid = (lo + hi) >> 1;
            if (ev[mid].t < time) lo = mid + 1; else hi = mid;
        }
        return lo;
    }

    void PoseFigures(float t, CamMode mode)
    {
        int tick = Mathf.RoundToInt(t / ReplayRound.Step);
        for (int i = 0; i < figures.Count; i++)
        {
            var f = figures[i];
            Round.Sample(t, i, out Vector3 p, out float yaw, out byte st);
            bool alive = (st & ReplayRound.Alive) != 0;
            bool visible = true;
            float fall = 0f;
            if (!alive)
            {
                int dead = 0;
                while (dead < 40 && tick - dead >= 0 && (Round.StateAt(tick - dead, i) & ReplayRound.Alive) == 0) dead++;
                float seconds = dead * ReplayRound.Step;
                visible = seconds < 1.6f;
                fall = Mathf.Clamp01(seconds / 0.32f);
            }
            if (i == 0 && mode == CamMode.First) visible = false; // you don't see yourself
            f.root.gameObject.SetActive(visible);
            if (!visible) continue;
            bool crouch = (st & ReplayRound.Crouch) != 0, slide = (st & ReplayRound.Slide) != 0;
            f.root.position = p;
            f.root.rotation = Quaternion.Euler(0f, yaw, 0f);
            float squat = crouch ? 0.7f : 1f;
            f.visual.localScale = new Vector3(1f, squat, 1f);
            // Falling over backwards when shot (pivot at the feet), tilting forward on a slide.
            float tilt = alive ? (slide ? -18f : 0f) : -fall * 88f;
            f.visual.localRotation = Quaternion.Euler(tilt, 0f, 0f);
            f.visual.localPosition = alive ? Vector3.zero : new Vector3(0f, 0.32f * fall, 0f); // lifted so the lying body rests on the floor instead of sinking in
            bool shooting = alive && ShotAge(i, t) < 0.06f;
            f.flashObject.SetActive(shooting);
            // The flash is a flat quad: turn it towards the camera so it reads from any side.
            if (shooting) f.flash.rotation = Quaternion.LookRotation(cam.transform.position - f.flash.position) * Quaternion.Euler(0f, 0f, (t * 997f) % 360f);
        }
    }

    // ------------------------------------------------------------------ effects

    LineRenderer Tracer()
    {
        if (tracerUsed >= tracers.Count)
        {
            var go = new GameObject("Tracer");
            go.transform.SetParent(transform, false);
            var line = go.AddComponent<LineRenderer>();
            line.positionCount = 2;
            line.useWorldSpace = true;
            line.sharedMaterial = tracerMaterial;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            tracers.Add(line);
        }
        var l = tracers[tracerUsed++];
        l.enabled = true;
        return l;
    }

    Transform Ball(Material m, Vector3 at, float size)
    {
        if (ballUsed >= fxBalls.Count)
        {
            var b = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            var col = b.GetComponent<Collider>();
            if (col) Destroy(col);
            b.name = "FX";
            b.transform.SetParent(transform, false);
            var r = b.GetComponent<Renderer>();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            fxBalls.Add(b.transform);
            fxBallRenderers.Add(r);
        }
        var t = fxBalls[ballUsed];
        fxBallRenderers[ballUsed].sharedMaterial = m;
        ballUsed++;
        t.gameObject.SetActive(true);
        t.position = at;
        t.localScale = Vector3.one * Mathf.Max(0.01f, size);
        return t;
    }

    void DrawEffects(float t, CamMode mode)
    {
        tracerUsed = 0;
        ballUsed = 0;
        var ev = Round.events;
        var white = VRArena.Glow(Color.white, Color.white, 4f);
        var gold = VRArena.Glow(new Color(1f, 0.8f, 0.2f), new Color(1f, 0.8f, 0.2f), 4f);
        var orange = VRArena.Glow(new Color(1f, 0.55f, 0.2f), new Color(1f, 0.55f, 0.2f), 5f);
        var grenade = VRArena.Glow(new Color(0.15f, 0.25f, 0.12f), new Color(0.4f, 1f, 0.3f), 0.6f);
        for (int i = FirstEvent(t - 5f); i < ev.Count; i++)
        {
            var e = ev[i];
            if (e.t > t) break;
            float age = t - e.t;
            if (e.kind == ReplayEvent.Shot)
            {
                if (age > 0.12f) continue;
                if (age < 0.09f)
                {
                    var line = Tracer();
                    bool ally = e.a >= 0 && e.a < Round.teams.Length && Round.teams[e.a] == 0;
                    Color c = e.a == 0 ? new Color(0.4f, 1f, 0.95f) : ally ? VRArena.AllyColor : VRArena.EnemyColor;
                    float a = Mathf.Clamp01((0.09f - age) / 0.08f);
                    line.startWidth = 0.035f; line.endWidth = 0.015f;
                    line.startColor = new Color(c.r, c.g, c.b, a);
                    line.endColor = new Color(c.r, c.g, c.b, a * 0.3f);
                    line.SetPosition(0, e.p0);
                    line.SetPosition(1, e.p1);
                }
                float s = 1f - age / 0.12f;
                Ball(e.extra == 2 ? gold : white, e.p1, (e.extra > 0 ? 0.28f : 0.16f) * s);
            }
            else if (e.kind == ReplayEvent.Kill)
            {
                if (age > 0.6f) continue;
                bool yours = e.a == 0;
                float s = Mathf.Sin(Mathf.Clamp01(age / 0.6f) * Mathf.PI);
                bool ally = e.b >= 0 && e.b < Round.teams.Length && Round.teams[e.b] == 0;
                Ball(yours ? gold : (ally ? VRArena.Glow(VRArena.AllyColor, VRArena.AllyColor, 4f) : VRArena.Glow(VRArena.EnemyColor, VRArena.EnemyColor, 4f)), e.p0, s * (yours ? 1.5f : 1.0f));
            }
            else if (e.kind == ReplayEvent.Grenade && e.t2 > e.t)
            {
                bool frag = e.extra == ReplayEvent.FragKind;
                if (t <= e.t2)
                {
                    float k = Mathf.Clamp01((t - e.t) / Mathf.Max(0.1f, e.t2 - e.t));
                    float dist = Vector3.Distance(e.p0, e.p1);
                    float arc = Mathf.Clamp(dist * 0.22f, 0.8f, 4f);
                    Vector3 p = Vector3.Lerp(e.p0, e.p1, k) + Vector3.up * (arc * 4f * k * (1f - k));
                    Ball(frag ? grenade : white, p, frag ? 0.2f : 0.16f);
                }
                else if (t - e.t2 < 0.45f)
                {
                    float k = (t - e.t2) / 0.45f;
                    float size = (frag ? 4.2f : 6f) * Mathf.Sqrt(k) * (1f - k * 0.7f);
                    Ball(frag ? orange : white, e.p1 + Vector3.up * 0.15f, size);
                }
            }
        }
        for (int i = tracerUsed; i < tracers.Count; i++) tracers[i].enabled = false;
        for (int i = ballUsed; i < fxBalls.Count; i++) fxBalls[i].gameObject.SetActive(false);
    }

    void DrawFlags(float t)
    {
        for (int i = 0; i < flagMarkers.Length; i++)
        {
            int owner = Round.FlagOwnerAt(i, t);
            Color c = owner == 1 ? VRArena.AllyColor : owner == 2 ? VRArena.EnemyColor : VRFlag.Neutral;
            var m = VRArena.Glow(c * 0.5f, c, 1.6f);
            foreach (var r in flagRenderers[i]) r.sharedMaterial = m;
            flagTexts[i].color = c;
            flagTexts[i].transform.rotation = cam.transform.rotation;
        }
    }
}
