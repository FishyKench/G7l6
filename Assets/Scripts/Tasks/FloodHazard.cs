using UnityEngine;

/// <summary>
/// Water on the floor (e.g. from an overflowing washing machine). It spreads out until you mop it up - for real:
/// hold the mop, hold the left mouse button and swipe it back and forth over the water. Every swipe wipes the
/// water away where the mop goes (a couple of passes dries a patch). Get it all and the floor's dry.
/// </summary>
public class FloodHazard : HazardTask
{
    [Header("Flood")]
    [Tooltip("The puddle object. It's scaled on X/Z to spread out.")]
    public Transform puddle;
    public float startDiameter = 0.4f;
    public float maxDiameter = 3f;
    [Tooltip("Seconds for the puddle to reach full size.")]
    public float spreadTime = 10f;

    [Header("Mopping")]
    [Tooltip("How much of the water has to be mopped up (0..1).")]
    [Range(0.5f, 1f)] public float cleanTarget = 0.9f;
    [Tooltip("Size of the mop head (metres, at house scale 1).")]
    public float mopRadius = 0.3f;
    [Tooltip("How much one swipe over a spot dries it (1 = in one go).")]
    public float wipePerPass = 0.6f;
    [Tooltip("How far away you can reach with the mop (metres, at house scale 1).")]
    public float reach = 3.4f;

    const int Res = 64;
    float size;
    float[] wet, edge;
    float edgeTotal;
    Texture2D mask;
    Color32[] pixels;
    GameObject surface, cursor;
    Material surfaceMaterial, cursorMaterial;
    Renderer[] originalRenderers;
    bool mopping, hadHead, toldHow;
    Vector3 lastHead;
    float cleaned, swishCooldown, sway, stillFor;

    void Reset()
    {
        taskName = "Mop up the flood!";
        requiredItem = ItemType.Mop;
        requiredItemName = "mop";
        actionVerb = "Mop up";
        stressPerSecond = 1.5f;
        clearDuration = 3f;
        stressReliefOnComplete = 5f;
        cooldown = 0f;
    }

    protected override string ClearedMessage => "Floor's dry again.";

    float Scale => HouseLayout.Instance ? HouseLayout.Instance.scale : 1f;

    public override float Progress => Mathf.Clamp01(cleaned / Mathf.Max(0.01f, cleanTarget));

    public override string Status
    {
        get
        {
            if (!PlayerHolds(ItemType.Mop)) return cleaned > 0.02f ? $"Mopped {Mathf.RoundToInt(Progress * 100f)}% - get the mop back!" : "Get the mop!";
            return mopping ? $"Mopping... {Mathf.RoundToInt(Progress * 100f)}%" : "Hold LEFT MOUSE and swipe the mop over the water";
        }
    }

    public override string Prompt
    {
        get
        {
            if (!IsActive) return null;
            return PlayerHolds(ItemType.Mop) ? "Hold LEFT MOUSE + swipe to mop" : $"Need the {requiredItemName}";
        }
    }

    public override void Interact()
    {
        if (!IsActive) return;
        if (PlayerHolds(ItemType.Mop)) GameHUD.Toast("Hold the left mouse button and swipe the mop back and forth over the water", GameHUD.Info);
        else GameHUD.Toast($"You need the {requiredItemName}!", GameHUD.Warn);
    }

    protected override void OnHazardStarted()
    {
        size = startDiameter;
        Apply(size);
        BuildSurface();
        ResetWater();
        mopping = hadHead = toldHow = false;
    }

    protected override void TaskUpdate()
    {
        float dt = Time.deltaTime;
        bool holding = PlayerHolds(ItemType.Mop);
        if (holding && !toldHow)
        {
            toldHow = true;
            GameHUD.Toast("Hold LEFT MOUSE and swipe the mop over the water", GameHUD.Info);
        }

        Vector3 head = default;
        bool onWater = holding && Input.GetMouseButton(0) && MopHead(out head);
        if (!onWater)
        {
            mopping = false;
            hadHead = false;
            if (cursor) cursor.SetActive(holding && MopHead(out var aim) && PlaceCursor(aim, 0.25f));
            // Not being mopped: it keeps spreading and stressing you out.
            if (Stress) Stress.AddStress(stressPerSecond * dt);
            size = Mathf.MoveTowards(size, maxDiameter, (maxDiameter - startDiameter) / Mathf.Max(0.1f, spreadTime * Tuning.HazardSpread) * dt);
            Apply(size);
            return;
        }

        mopping = true;
        float moved = hadHead ? Vector3.Distance(lastHead, head) : 0f;
        if (hadHead && moved > 0.002f) Wipe(lastHead, head, moved);
        stillFor = moved > 0.01f ? 0f : stillFor + dt;

        // The mop sways with your swipes.
        var cam = Camera.main;
        float side = hadHead && cam ? Vector3.Dot(head - lastHead, cam.transform.right) / Mathf.Max(0.001f, dt) : 0f;
        sway = Mathf.Lerp(sway, Mathf.Clamp(side * 0.05f, -0.18f, 0.18f), dt * 12f);
        var motion = HeldItemMotion.Get();
        if (motion) motion.Pose(new Vector3(-0.12f + sway, -0.2f, 0.28f), new Vector3(38f, sway * 120f, -sway * 160f), 0.05f);

        if (cursor) cursor.SetActive(PlaceCursor(head, 0.45f));
        lastHead = head;
        hadHead = true;

        swishCooldown -= dt;
        if (moved / Mathf.Max(0.001f, dt) > 1.4f * Scale && swishCooldown <= 0f)
        {
            swishCooldown = 0.22f;
            Fx.Play(Fx.Swish, head, 0.55f, Random.Range(0.85f, 1.2f));
            Fx.Splash(head, 4, Scale);
        }
        if (stillFor > 1.2f && Random.value < dt) GameHUD.Toast("Swipe the mop back and forth!", GameHUD.Info);

        if (cleaned >= cleanTarget) Complete(ClearedMessage);
    }

    /// <summary>Where the mop touches the water: the floor point you're looking at, if it's on (or by) the puddle.</summary>
    bool MopHead(out Vector3 head)
    {
        head = default;
        var cam = Camera.main;
        if (!cam || !puddle) return false;
        var ray = new Ray(cam.transform.position, cam.transform.forward);
        var plane = new Plane(Vector3.up, puddle.position);
        if (!plane.Raycast(ray, out float d) || d > reach * Scale) return false;
        head = ray.GetPoint(d);
        Vector3 flat = head - puddle.position;
        flat.y = 0f;
        return flat.magnitude <= puddle.lossyScale.x * 0.5f + mopRadius * Scale;
    }

    bool PlaceCursor(Vector3 at, float alpha)
    {
        if (!cursor) return false;
        cursor.transform.position = at + Vector3.up * 0.012f;
        cursor.transform.localScale = new Vector3(mopRadius * 2.3f * Scale, 1f, mopRadius * 2.3f * Scale);
        if (cursorMaterial) cursorMaterial.color = new Color(1f, 1f, 1f, alpha);
        return true;
    }

    // ------------------------------------------------------------------ the water (a wetness map)

    void BuildSurface()
    {
        if (surface || !puddle) return;
        originalRenderers = puddle.GetComponentsInChildren<Renderer>(true);
        var source = originalRenderers.Length > 0 ? originalRenderers[0].sharedMaterial : null;

        mask = new Texture2D(Res, Res, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "Wet mask" };
        pixels = new Color32[Res * Res];
        wet = new float[Res * Res];
        edge = new float[Res * Res];

        surfaceMaterial = source ? new Material(source) : new Material(Shader.Find("Sprites/Default")) { color = new Color(0.3f, 0.55f, 0.85f, 0.55f) };
        surfaceMaterial.mainTexture = mask;
        surface = new GameObject("Water (mop map)");
        surface.transform.SetParent(puddle, false);
        surface.transform.localPosition = new Vector3(0f, 1.05f, 0f); // just above the puddle disc's top
        surface.AddComponent<MeshFilter>().sharedMesh = Fx.FlatQuad;
        var r = surface.AddComponent<MeshRenderer>();
        r.sharedMaterial = surfaceMaterial;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        foreach (var o in originalRenderers) if (o) o.enabled = false;

        cursor = new GameObject("Mop head");
        cursor.AddComponent<MeshFilter>().sharedMesh = Fx.FlatQuad;
        var cr = cursor.AddComponent<MeshRenderer>();
        cursorMaterial = new Material(Fx.SoftMaterial);
        cr.sharedMaterial = cursorMaterial;
        cr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        cursor.SetActive(false);
    }

    void ResetWater()
    {
        if (wet == null) return;
        float seed = Random.value * 100f;
        edgeTotal = 0f;
        for (int y = 0; y < Res; y++)
        for (int x = 0; x < Res; x++)
        {
            float u = (x + 0.5f) / Res - 0.5f, v = (y + 0.5f) / Res - 0.5f;
            float r = Mathf.Sqrt(u * u + v * v);
            float angle = Mathf.Atan2(v, u);
            // A wobbly edge, so it looks like spilled water and not a perfect disc.
            float rim = 0.4f + 0.07f * Mathf.PerlinNoise(seed + Mathf.Cos(angle) * 1.6f, seed + Mathf.Sin(angle) * 1.6f);
            float e = Mathf.Clamp01((rim - r) / 0.05f);
            int i = y * Res + x;
            edge[i] = e;
            wet[i] = 1f;
            edgeTotal += e;
        }
        cleaned = 0f;
        Repaint();
    }

    /// <summary>The mop went from a to b: dry the water it passed over.</summary>
    void Wipe(Vector3 a, Vector3 b, float moved)
    {
        Vector3 la = puddle.InverseTransformPoint(a), lb = puddle.InverseTransformPoint(b);
        Vector2 ua = new Vector2(la.x + 0.5f, la.z + 0.5f), ub = new Vector2(lb.x + 0.5f, lb.z + 0.5f);
        float brush = mopRadius * Scale / Mathf.Max(0.01f, puddle.lossyScale.x);
        // Drying is spread along the stroke: going over a spot once dries about 'wipePerPass' of it.
        float amount = Mathf.Min(moved, mopRadius * Scale * 2f) / (mopRadius * Scale * 2f) * wipePerPass;

        int x0 = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(ua.x, ub.x) - brush) * Res));
        int x1 = Mathf.Min(Res - 1, Mathf.CeilToInt((Mathf.Max(ua.x, ub.x) + brush) * Res));
        int y0 = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(ua.y, ub.y) - brush) * Res));
        int y1 = Mathf.Min(Res - 1, Mathf.CeilToInt((Mathf.Max(ua.y, ub.y) + brush) * Res));
        if (x0 > x1 || y0 > y1) return;
        Vector2 seg = ub - ua;
        float segLen2 = Mathf.Max(1e-8f, seg.sqrMagnitude);
        for (int y = y0; y <= y1; y++)
        for (int x = x0; x <= x1; x++)
        {
            var p = new Vector2((x + 0.5f) / Res, (y + 0.5f) / Res);
            float t = Mathf.Clamp01(Vector2.Dot(p - ua, seg) / segLen2);
            float d = Vector2.Distance(p, ua + seg * t) / brush;
            if (d >= 1f) continue;
            int i = y * Res + x;
            wet[i] = Mathf.Max(0f, wet[i] - amount * (1f - d * d));
        }
        Repaint();
    }

    /// <summary>The robot vacuum dries the water round a point (a disc of 'radius' metres; 'amount' is 0..1 of the water there).</summary>
    public void DryAt(Vector3 point, float radius, float amount)
    {
        if (!IsActive || wet == null || !puddle) return;
        Vector3 l = puddle.InverseTransformPoint(point);
        Vector2 u = new Vector2(l.x + 0.5f, l.z + 0.5f);
        float brush = radius / Mathf.Max(0.01f, puddle.lossyScale.x);
        int x0 = Mathf.Max(0, Mathf.FloorToInt((u.x - brush) * Res)), x1 = Mathf.Min(Res - 1, Mathf.CeilToInt((u.x + brush) * Res));
        int y0 = Mathf.Max(0, Mathf.FloorToInt((u.y - brush) * Res)), y1 = Mathf.Min(Res - 1, Mathf.CeilToInt((u.y + brush) * Res));
        if (x0 > x1 || y0 > y1) return;
        for (int y = y0; y <= y1; y++)
        for (int x = x0; x <= x1; x++)
        {
            float d = Vector2.Distance(new Vector2((x + 0.5f) / Res, (y + 0.5f) / Res), u) / brush;
            if (d >= 1f) continue;
            int i = y * Res + x;
            wet[i] = Mathf.Max(0f, wet[i] - amount * (1f - d * d));
        }
        Repaint();
        if (cleaned >= cleanTarget) Complete(ClearedMessage);
    }

    void Repaint()
    {
        float left = 0f;
        for (int i = 0; i < wet.Length; i++)
        {
            float a = wet[i] * edge[i];
            left += a;
            // Mopped spots stay a little damp-looking for a moment.
            pixels[i] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(a * 1.15f) * 255f));
        }
        cleaned = edgeTotal > 0f ? 1f - left / edgeTotal : 1f;
        mask.SetPixels32(pixels);
        mask.Apply(false);
    }

    protected override void OnHazardEnded()
    {
        mopping = false;
        if (cursor) cursor.SetActive(false);
    }

    void OnDestroy()
    {
        if (cursor) Destroy(cursor);
        if (mask) Destroy(mask);
    }

    void Apply(float diameter)
    {
        if (!puddle) return;
        var s = puddle.localScale;
        puddle.localScale = new Vector3(diameter, s.y, diameter);
    }
}
