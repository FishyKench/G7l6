using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Wet paw prints: the cat walked through water and tracked it round the house. Mop them up like a spill - hold the mop, hold the
/// left mouse button and swipe over them. One task per trail; the cat adds prints to it while her paws are wet.
/// </summary>
public class PawPrints : HazardTask
{
    /// <summary>The trail being made right now (null when there isn't one).</summary>
    public static PawPrints Current { get; private set; }

    class Print
    {
        public GameObject go;
        public Material mat;
        public float wet, alpha;
    }

    readonly List<Print> prints = new List<Print>();
    int total;
    bool toldHow;
    float sway;
    Vector3 lastHead;
    bool hadHead;

    float Scale => HouseLayout.Instance ? HouseLayout.Instance.scale : 1f;

    /// <summary>Adds a print at 'at' (on the floor), facing 'yaw'; 'wetness' 0..1 (paws dry as she walks).</summary>
    public static void Add(Vector3 at, float yaw, float size, float wetness)
    {
        if (!Current || (!Current.IsActive && Current.started))
        {
            var go = new GameObject("Wet paw prints");
            go.transform.position = at;
            var visual = new GameObject("Prints");
            visual.transform.SetParent(go.transform, false);
            Current = go.AddComponent<PawPrints>();
            Current.visualRoot = visual;
            Current.taskName = "Mop up the paw prints";
            Current.requiredItem = ItemType.Mop;
            Current.requiredItemName = "mop";
            Current.actionVerb = "Mop up";
            Current.stressPerSecond = 0.35f;
            Current.stressReliefOnComplete = 2f;
            Current.cooldown = 0f;
        }
        Current.AddPrint(at, yaw, size, wetness);
    }

    bool started;

    void AddPrint(Vector3 at, float yaw, float size, float wetness)
    {
        var go = new GameObject("Paw print");
        go.transform.SetParent(visualRoot.transform, true);
        go.transform.position = at + Vector3.up * 0.004f;
        go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        go.transform.localScale = new Vector3(size, 1f, size * 1.15f);
        go.AddComponent<MeshFilter>().sharedMesh = Fx.FlatQuad;
        var r = go.AddComponent<MeshRenderer>();
        var mat = new Material(PrintMaterial());
        float a = Mathf.Lerp(0.4f, 0.85f, wetness);
        mat.color = new Color(0.08f, 0.11f, 0.16f, a);
        r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        var box = go.AddComponent<BoxCollider>(); // (look at it for the prompt)
        box.isTrigger = true;
        box.size = new Vector3(1.6f, 0.1f, 1.6f);
        prints.Add(new Print { go = go, mat = mat, wet = 1f, alpha = a });
        total++;
        if (!started)
        {
            started = true;
            if (GameManager.Instance) GameManager.Instance.StartTask(this);
            else visualRoot.SetActive(true);
        }
    }

    protected override void Start() { } // (the prints show from the first one)

    public override Vector3 Where => prints.Count > 0 && prints[prints.Count / 2].go ? prints[prints.Count / 2].go.transform.position : transform.position;
    public int Left => prints.Count;
    public override float Progress => total > 0 ? 1f - prints.Count / (float)total : 0f;
    public override string Status => PlayerHolds(ItemType.Mop) ? $"Swipe the mop over the paw prints ({prints.Count} left)" : $"Wet paw prints - get the mop ({prints.Count} left)";

    public override string Prompt => !IsActive ? null : PlayerHolds(ItemType.Mop) ? "Hold LEFT MOUSE + swipe to mop" : "Need the mop";

    public override void Interact()
    {
        if (!IsActive) return;
        GameHUD.Toast(PlayerHolds(ItemType.Mop) ? "Hold the left mouse button and swipe the mop over the prints" : "You need the mop!", PlayerHolds(ItemType.Mop) ? GameHUD.Info : GameHUD.Warn);
    }

    protected override void TaskUpdate()
    {
        float dt = Time.deltaTime;
        bool holding = PlayerHolds(ItemType.Mop);
        if (Stress) Stress.AddStress(stressPerSecond * dt);
        if (!holding) { hadHead = false; return; }
        if (!toldHow && Near(4f * Scale)) { toldHow = true; GameHUD.Toast("Hold LEFT MOUSE and swipe the mop over the paw prints", GameHUD.Info); }
        if (!Input.GetMouseButton(0) || !MopHead(out var head)) { hadHead = false; return; }

        float moved = hadHead ? Vector3.Distance(head, lastHead) : 0f;
        var cam = Camera.main;
        float side = hadHead && cam ? Vector3.Dot(head - lastHead, cam.transform.right) / Mathf.Max(0.001f, dt) : 0f;
        sway = Mathf.Lerp(sway, Mathf.Clamp(side * 0.05f, -0.18f, 0.18f), dt * 12f);
        var motion = HeldItemMotion.Get();
        if (motion) motion.Pose(new Vector3(-0.12f + sway, -0.2f, 0.28f), new Vector3(38f, sway * 120f, -sway * 160f), 0.05f);
        lastHead = head;
        hadHead = true;
        if (moved < 0.003f) return;

        float radius = 0.32f * Scale;
        for (int i = prints.Count - 1; i >= 0; i--)
        {
            var p = prints[i];
            if (!p.go) { prints.RemoveAt(i); continue; }
            Vector3 d = p.go.transform.position - head;
            if (Mathf.Abs(d.y) > 0.3f * Scale) continue;
            d.y = 0f;
            if (d.magnitude > radius) continue;
            p.wet -= Mathf.Min(moved, radius) / radius * 1.4f;
            p.mat.color = new Color(p.mat.color.r, p.mat.color.g, p.mat.color.b, p.alpha * Mathf.Clamp01(p.wet));
            if (p.wet <= 0f)
            {
                Fx.Splash(p.go.transform.position, 2, Scale);
                Destroy(p.go);
                prints.RemoveAt(i);
            }
        }
        if (prints.Count == 0) Complete("Paw prints gone.");
    }

    bool Near(float range)
    {
        var cam = Camera.main;
        if (!cam) return false;
        foreach (var p in prints) if (p.go && Vector3.Distance(p.go.transform.position, cam.transform.position) < range) return true;
        return false;
    }

    /// <summary>Where the mop touches the floor (what you're looking at, within reach).</summary>
    bool MopHead(out Vector3 head)
    {
        head = default;
        var cam = Camera.main;
        if (!cam) return false;
        foreach (var h in Physics.RaycastAll(cam.transform.position, cam.transform.forward, 3.4f * Scale, ~0, QueryTriggerInteraction.Ignore))
        {
            if (h.collider.GetComponentInParent<PlayerMovementAdvanced>() || h.collider.GetComponentInParent<Carryable>()) continue;
            if (h.normal.y < 0.6f) continue;
            if (!head.Equals(default(Vector3)) && Vector3.Distance(cam.transform.position, h.point) > Vector3.Distance(cam.transform.position, head)) continue;
            head = h.point;
        }
        return !head.Equals(default(Vector3));
    }

    protected override void OnCompleted()
    {
        base.OnCompleted();
        if (Current == this) Current = null;
    }

    protected override void OnFailed()
    {
        base.OnFailed();
        if (Current == this) Current = null;
    }

    // ---- the print itself: a pad and four toes, drawn once

    static Material printMaterial;

    static Material PrintMaterial()
    {
        if (printMaterial) return printMaterial;
        const int N = 48;
        var tex = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "Paw print" };
        var px = new Color32[N * N];
        // (u, v) in 0..1; the pad low in the middle, toes in an arc above it
        Vector2[] toes = { new Vector2(0.24f, 0.62f), new Vector2(0.41f, 0.78f), new Vector2(0.59f, 0.78f), new Vector2(0.76f, 0.62f) };
        for (int y = 0; y < N; y++)
        for (int x = 0; x < N; x++)
        {
            var p = new Vector2((x + 0.5f) / N, (y + 0.5f) / N);
            var pd = new Vector2((p.x - 0.5f) / 0.24f, (p.y - 0.32f) / 0.19f);
            float a = Mathf.Clamp01((1f - pd.magnitude) * 6f);
            foreach (var t in toes) a = Mathf.Max(a, Mathf.Clamp01((1f - Vector2.Distance(p, t) / 0.1f) * 6f));
            px[y * N + x] = new Color32(255, 255, 255, (byte)(a * 255f));
        }
        tex.SetPixels32(px);
        tex.Apply();
        printMaterial = new Material(Shader.Find("Sprites/Default")) { mainTexture = tex };
        return printMaterial;
    }
}
