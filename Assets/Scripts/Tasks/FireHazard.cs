using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Fire (e.g. from a forgotten oven). It breaks out in a few spots and spreads to new ones if you leave it.
/// Put it out with the fire extinguisher: hold the left mouse button to spray and aim at each flame - a flame
/// you're hitting shrinks and goes out in a puff of smoke; one you stop spraying creeps back up.
/// </summary>
public class FireHazard : HazardTask
{
    [Header("Fire")]
    public ParticleSystem flames;
    public Light fireLight;
    public float lightIntensity = 3f;

    [Header("Spreading")]
    [Tooltip("Extra flames that break out round the first one when the fire starts.")]
    public int extraFlames = 2;
    [Tooltip("Seconds between new flames while nobody's spraying.")]
    public float spreadEvery = 7f;
    public int maxFlames = 6;

    [Header("Spraying")]
    [Tooltip("How far the spray reaches (metres, at house scale 1).")]
    public float sprayRange = 4.5f;
    [Tooltip("How far off (degrees) you can aim and still hit a flame.")]
    public float sprayAngle = 12f;
    [Tooltip("How fast a flame you're hitting goes out (1 = one second).")]
    public float knockDown = 1.1f;
    [Tooltip("How fast a flame you stopped hitting comes back.")]
    public float regrow = 0.12f;

    class Flame
    {
        public Transform t;
        public ParticleSystem ps;
        public float heat = 1f, baseRate;
        public Vector3 baseScale;
        public bool original, hit;
        public Light light;
        public RectTransform marker, arrow;
        public Image ring, fill;
        public float bornAt;
    }

    Canvas markers;
    static Sprite ringSprite, dotSprite, arrowSprite;

    readonly List<Flame> burning = new List<Flame>();
    float baseEmission = -1f;
    Vector3 flamesScale;
    float spreadTimer, hissCooldown;
    ParticleSystem spray;
    AudioSource sprayHiss;
    bool spraying, toldHow;

    void Reset()
    {
        taskName = "Put out the fire!";
        requiredItem = ItemType.Extinguisher;
        requiredItemName = "fire extinguisher";
        actionVerb = "Spray";
        stressPerSecond = 3f;
        clearDuration = 2f;
        stressReliefOnComplete = 5f;
        cooldown = 0f;
    }

    protected override string ClearedMessage => "Fire's out!";
    float Scale => HouseLayout.Instance ? HouseLayout.Instance.scale : 1f;
    int Alive { get { int n = 0; foreach (var f in burning) if (f.heat > 0f) n++; return n; } }

    public override float Progress
    {
        get
        {
            if (burning.Count == 0) return 0f;
            float heat = 0f;
            foreach (var f in burning) heat += f.heat;
            return 1f - heat / burning.Count;
        }
    }

    public override string Status
    {
        get
        {
            if (!PlayerHolds(ItemType.Extinguisher)) return "Get the fire extinguisher!";
            int n = Alive;
            return $"Hold LEFT MOUSE - spray every flame! ({n} {(n == 1 ? "flame" : "flames")} left)";
        }
    }

    public override string Prompt
    {
        get
        {
            if (!IsActive) return null;
            return PlayerHolds(ItemType.Extinguisher) ? "Hold LEFT MOUSE to spray - aim at the flames" : $"Need the {requiredItemName}";
        }
    }

    public override void Interact()
    {
        if (!IsActive) return;
        if (PlayerHolds(ItemType.Extinguisher)) GameHUD.Toast("Hold the left mouse button to spray, and aim at each flame", GameHUD.Info);
        else GameHUD.Toast($"You need the {requiredItemName}!", GameHUD.Warn);
    }

    protected override void OnHazardStarted()
    {
        ClearFlames();
        toldHow = false;
        if (flames)
        {
            var emission = flames.emission;
            if (baseEmission < 0f) baseEmission = emission.rateOverTimeMultiplier;
            emission.rateOverTimeMultiplier = baseEmission;
            if (flamesScale == Vector3.zero) flamesScale = flames.transform.localScale;
            flames.transform.localScale = flamesScale;
            flames.Play();
            var first = new Flame { t = flames.transform, ps = flames, baseRate = baseEmission, baseScale = flames.transform.localScale, original = true };
            burning.Add(first);
            Dress(first);
            for (int i = 0; i < extraFlames; i++) SpreadFlame(false);
        }
        if (fireLight) fireLight.enabled = true;
        spreadTimer = spreadEvery * Tuning.HazardSpread;
    }

    protected override void TaskUpdate()
    {
        if (!flames) { base.TaskUpdate(); return; }
        float dt = Time.deltaTime;
        bool holding = PlayerHolds(ItemType.Extinguisher);
        if (holding && !toldHow)
        {
            toldHow = true;
            GameHUD.Toast("Hold LEFT MOUSE to spray - hit every flame!", GameHUD.Info);
        }
        spraying = holding && Input.GetMouseButton(0);
        Spray(dt);

        var cam = Camera.main;
        bool anyHit = false;
        float heat = 0f;
        foreach (var f in burning)
        {
            if (f.heat <= 0f) continue;
            f.hit = spraying && cam && Hits(cam.transform, f, out float strength) && Dose(f, strength * knockDown * dt);
            if (!f.hit) f.heat = Mathf.Min(1f, f.heat + regrow * dt);
            anyHit |= f.hit;
            if (f.heat <= 0f) { GoOut(f); continue; }
            heat += f.heat;
            Look(f);
        }
        UpdateMarkers(cam);

        if (fireLight)
            fireLight.intensity = lightIntensity * Mathf.Clamp01(heat / 2f + 0.2f) * (0.75f + 0.5f * Mathf.PerlinNoise(Time.time * 8f, 0.3f));

        if (Alive == 0) { Complete(ClearedMessage); return; }

        if (Stress) Stress.AddStress(stressPerSecond * (anyHit ? 0.4f : 1f) * dt);

        // Left alone, it spreads.
        if (!anyHit) spreadTimer -= dt;
        if (spreadTimer <= 0f)
        {
            spreadTimer = spreadEvery * Tuning.HazardSpread;
            if (Alive < maxFlames && SpreadFlame(true)) GameHUD.Toast("The fire's spreading!", GameHUD.Warn);
        }
    }

    /// <summary>Is the spray hitting this flame? 'strength' is 0.5..1 (better aimed = stronger).</summary>
    bool Hits(Transform cam, Flame f, out float strength)
    {
        strength = 0f;
        Vector3 target = f.t.position + Vector3.up * 0.25f * Scale;
        Vector3 to = target - cam.position;
        float dist = to.magnitude;
        if (dist > sprayRange * Scale) return false;
        float allowed = Mathf.Max(sprayAngle, Mathf.Atan2(0.35f * Scale, dist) * Mathf.Rad2Deg);
        float angle = Vector3.Angle(cam.forward, to);
        if (angle > allowed) return false;
        // Not through walls or counters.
        if (Physics.Raycast(cam.position, to / dist, out var block, dist - 0.45f * Scale, ~0, QueryTriggerInteraction.Ignore)
            && !block.collider.GetComponentInParent<PlayerCarry>()) return false;
        strength = 1f - 0.5f * angle / allowed;
        return true;
    }

    bool Dose(Flame f, float amount)
    {
        f.heat -= amount;
        hissCooldown -= Time.deltaTime;
        if (hissCooldown <= 0f)
        {
            hissCooldown = 0.35f;
            Fx.Puff(f.t.position + Vector3.up * 0.3f * Scale, new Color(0.85f, 0.85f, 0.85f, 0.35f), 2, Scale);
        }
        return true;
    }

    void GoOut(Flame f)
    {
        f.heat = 0f;
        if (f.ps)
        {
            var emission = f.ps.emission;
            emission.rateOverTimeMultiplier = 0f;
            f.ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }
        Fx.Puff(f.t.position + Vector3.up * 0.2f * Scale, new Color(0.55f, 0.55f, 0.58f, 0.6f), 14, Scale);
        Undress(f);
        Fx.Play(Fx.Sizzle, f.t.position, 0.7f, Random.Range(0.9f, 1.15f));
        int left = Alive;
        if (left > 0) GameHUD.Toast(left == 1 ? "One more flame!" : $"{left} flames left", GameHUD.Info);
    }

    /// <summary>A flame's size and strength follow its heat.</summary>
    void Look(Flame f)
    {
        if (!f.ps) return;
        var emission = f.ps.emission;
        emission.rateOverTimeMultiplier = f.baseRate * (0.2f + 0.8f * f.heat);
        f.t.localScale = f.baseScale * (0.45f + 0.55f * f.heat) * (f.hit ? 0.92f + 0.08f * Mathf.Sin(Time.time * 40f) : 1f);
    }

    /// <summary>A new flame catches near a burning one: on the counter, the floor, whatever's there.</summary>
    bool SpreadFlame(bool announce)
    {
        var lit = burning.FindAll(f => f.heat > 0f);
        if (lit.Count == 0 || !flames) return false;
        float s = Scale;
        for (int tries = 0; tries < 16; tries++)
        {
            var from = lit[Random.Range(0, lit.Count)].t.position;
            Vector2 dir = Random.insideUnitCircle.normalized * Random.Range(0.55f, 1.25f) * s;
            Vector3 top = from + new Vector3(dir.x, 1.1f * s, dir.y);
            if (!Physics.Raycast(top, Vector3.down, out var hit, 3f * s, ~0, QueryTriggerInteraction.Ignore)) continue;
            if (hit.normal.y < 0.7f || Mathf.Abs(hit.point.y - from.y) > 1.3f * s) continue;
            if (hit.collider.GetComponentInParent<Carryable>() || hit.collider.GetComponentInParent<PlayerCarry>()) continue;
            if (burning.Exists(f => f.heat > 0f && Vector3.Distance(f.t.position, hit.point) < 0.5f * s)) continue;

            var go = Instantiate(flames.gameObject, hit.point, flames.transform.rotation, transform);
            go.name = "Flame (spread)";
            go.transform.localScale = (flamesScale == Vector3.zero ? flames.transform.localScale : flamesScale) * 0.75f;
            var ps = go.GetComponent<ParticleSystem>();
            var f = new Flame { t = go.transform, ps = ps, baseRate = baseEmission * 0.7f, baseScale = go.transform.localScale, heat = announce ? 0.6f : 1f };
            if (ps) { var e = ps.emission; e.rateOverTimeMultiplier = f.baseRate; ps.Play(); }
            burning.Add(f);
            Dress(f);
            if (announce)
            {
                // Make the new one impossible to miss: a burst of sparks and a whoosh.
                Fx.Puff(hit.point + Vector3.up * 0.2f * s, new Color(1f, 0.55f, 0.15f, 0.9f), 18, s);
                Fx.Play(Fx.Swish, hit.point, 0.8f, 0.6f);
            }
            return true;
        }
        return false;
    }

    // ------------------------------------------------------------------ seeing where it burns

    /// <summary>A flickering orange light on the flame and a marker on screen that follows it (or points to it).</summary>
    void Dress(Flame f)
    {
        f.bornAt = Time.time;
        float s = Scale;
        if (!f.light)
        {
            f.light = new GameObject("Flame glow").AddComponent<Light>();
            f.light.transform.SetParent(f.t, false);
            f.light.transform.position = f.t.position + Vector3.up * 0.35f * s;
            f.light.type = LightType.Point;
            f.light.color = new Color(1f, 0.5f, 0.15f);
            f.light.range = 3.2f * s;
            f.light.shadows = LightShadows.None;
        }
        f.light.enabled = true;

        if (!markers)
        {
            markers = UIFactory.CreateCanvas("Fire markers", 30);
            markers.transform.SetParent(transform, false);
        }
        if (!f.marker)
        {
            f.marker = UIFactory.Rect("Flame marker", markers.transform);
            f.marker.anchorMin = f.marker.anchorMax = new Vector2(0.5f, 0.5f);
            f.marker.sizeDelta = new Vector2(64, 64);
            f.ring = Pic(f.marker, "Ring", RingSprite(), new Vector2(64, 64));
            Pic(f.marker, "Core", DotSprite(), new Vector2(22, 22)).color = new Color(1f, 0.85f, 0.4f, 0.95f);
            var bar = Pic(f.marker, "Heat bg", null, new Vector2(48, 6));
            bar.rectTransform.anchoredPosition = new Vector2(0f, -42f);
            bar.color = new Color(0f, 0f, 0f, 0.55f);
            f.fill = Pic(bar.rectTransform, "Heat", null, Vector2.zero);
            f.fill.rectTransform.anchorMin = Vector2.zero;
            f.fill.rectTransform.anchorMax = Vector2.one;
            f.fill.rectTransform.offsetMin = f.fill.rectTransform.offsetMax = Vector2.zero;
            var arrow = Pic(f.marker, "Arrow", ArrowSprite(), new Vector2(26, 26));
            f.arrow = arrow.rectTransform;
        }
        f.marker.gameObject.SetActive(true);
    }

    void Undress(Flame f)
    {
        if (f.light) f.light.enabled = false;
        if (f.marker) f.marker.gameObject.SetActive(false);
    }

    void UpdateMarkers(Camera cam)
    {
        if (!markers || !cam) return;
        var canvasRect = (RectTransform)markers.transform;
        Vector2 half = canvasRect.rect.size * 0.5f;
        const float edge = 60f;
        foreach (var f in burning)
        {
            if (f.heat <= 0f || !f.marker) continue;
            float flicker = 0.75f + 0.5f * Mathf.PerlinNoise(Time.time * 9f, f.bornAt);
            if (f.light) f.light.intensity = (0.6f + 1.6f * f.heat) * flicker;

            Vector3 world = f.t.position + Vector3.up * 0.3f * Scale;
            Vector3 sp = cam.WorldToScreenPoint(world);
            bool behind = sp.z < 0f;
            if (behind) sp = -sp;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, sp, null, out Vector2 local);
            bool off = behind || Mathf.Abs(local.x) > half.x - edge || Mathf.Abs(local.y) > half.y - edge;
            if (off)
            {
                // Pin it to the screen edge, pointing towards the flame.
                Vector2 dir = local.sqrMagnitude > 1f ? local.normalized : Vector2.down;
                float k = Mathf.Min((half.x - edge) / Mathf.Max(0.001f, Mathf.Abs(dir.x)), (half.y - edge) / Mathf.Max(0.001f, Mathf.Abs(dir.y)));
                local = dir * k;
                f.arrow.gameObject.SetActive(true);
                f.arrow.anchoredPosition = dir * 40f;
                f.arrow.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg - 90f);
            }
            else f.arrow.gameObject.SetActive(false);
            f.marker.anchoredPosition = local;

            // A new flame pulses big for a moment; one you're hitting turns white.
            float age = Time.time - f.bornAt;
            float pulse = age < 1.2f ? 1f + 0.7f * (1f - age / 1.2f) * Mathf.Abs(Mathf.Sin(age * 9f)) : 1f + 0.06f * Mathf.Sin(Time.time * 5f);
            f.marker.localScale = Vector3.one * pulse * (off ? 0.8f : 1f);
            Color c = f.hit ? new Color(0.85f, 0.95f, 1f) : Color.Lerp(new Color(1f, 0.75f, 0.2f), new Color(1f, 0.25f, 0.1f), f.heat);
            f.ring.color = new Color(c.r, c.g, c.b, 0.95f);
            f.fill.color = c;
            f.fill.rectTransform.anchorMax = new Vector2(Mathf.Clamp01(f.heat), 1f);
            f.ring.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -Time.time * (f.hit ? 360f : 60f));
        }
    }

    static Image Pic(Transform parent, string name, Sprite sprite, Vector2 size)
    {
        var rt = UIFactory.Rect(name, parent);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = size;
        var img = rt.gameObject.AddComponent<Image>();
        img.sprite = sprite;
        img.raycastTarget = false;
        return img;
    }

    /// <summary>A dashed ring (it spins, so you see it from the corner of your eye).</summary>
    static Sprite RingSprite() => ringSprite ? ringSprite : ringSprite = MakeSprite(64, (x, y, r, a) =>
        Mathf.Clamp01(1.5f - Mathf.Abs(r - 27f)) * (Mathf.Repeat(a, 45f) < 30f ? 1f : 0f));
    static Sprite DotSprite() => dotSprite ? dotSprite : dotSprite = MakeSprite(32, (x, y, r, a) => Mathf.Clamp01(15f - r));
    static Sprite ArrowSprite() => arrowSprite ? arrowSprite : arrowSprite = MakeSprite(32, (x, y, r, a) =>
        y > 4f && y < 28f && Mathf.Abs(x - 16f) < (28f - y) * 0.55f ? 1f : 0f);

    static Sprite MakeSprite(int size, System.Func<float, float, float, float, float> alpha)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[size * size];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float dx = x + 0.5f - size * 0.5f, dy = y + 0.5f - size * 0.5f;
            float r = Mathf.Sqrt(dx * dx + dy * dy), a = (Mathf.Atan2(dy, dx) * Mathf.Rad2Deg + 360f) % 360f;
            px[y * size + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(alpha(x + 0.5f, y + 0.5f, r, a)) * 255f));
        }
        tex.SetPixels32(px);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
    }

    // ------------------------------------------------------------------ the extinguisher

    void Spray(float dt)
    {
        var carry = PlayerCarry.Instance;
        var cam = Camera.main;
        if (spraying && !spray) BuildSpray();
        if (sprayHiss) sprayHiss.volume = Mathf.MoveTowards(sprayHiss.volume, spraying ? 0.55f : 0f, dt * 5f);
        if (!spray) return;
        var emission = spray.emission;
        emission.rateOverTimeMultiplier = spraying ? 160f : 0f;
        if (!spraying || !carry || !carry.holdPoint || !cam) return;

        var motion = HeldItemMotion.Get();
        float jitter = Mathf.PerlinNoise(Time.time * 25f, 0.2f) - 0.5f;
        if (motion) motion.Pose(new Vector3(-0.16f, 0.08f, 0.06f), new Vector3(-6f + jitter * 3f, -8f, jitter * 2f), 0.05f);
        spray.transform.position = carry.holdPoint.position + cam.transform.forward * 0.25f + cam.transform.up * 0.04f;
        spray.transform.rotation = Quaternion.LookRotation(cam.transform.forward);
        if (sprayHiss) sprayHiss.transform.position = spray.transform.position;
    }

    void BuildSpray()
    {
        float s = Scale;
        var go = new GameObject("Extinguisher spray");
        spray = go.AddComponent<ParticleSystem>();
        spray.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = spray.main;
        main.loop = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.6f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(6f * s, 9f * s);
        main.startSize = new ParticleSystem.MinMaxCurve(0.06f * s, 0.12f * s);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 1f, 1f, 0.55f), new Color(0.85f, 0.9f, 0.95f, 0.45f));
        main.gravityModifier = 0.15f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 600;
        var emission = spray.emission;
        emission.rateOverTimeMultiplier = 0f;
        var shape = spray.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 6f;
        shape.radius = 0.015f;
        var drag = spray.limitVelocityOverLifetime;
        drag.enabled = true;
        drag.dampen = 0.12f;
        drag.limit = 1.5f * s;
        var grow = spray.sizeOverLifetime;
        grow.enabled = true;
        grow.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 7f));
        var fade = spray.colorOverLifetime;
        fade.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.6f, 0.4f), new GradientAlphaKey(0f, 1f) });
        fade.color = new ParticleSystem.MinMaxGradient(g);
        var noise = spray.noise;
        noise.enabled = true;
        noise.strength = 0.6f * s;
        noise.frequency = 1.2f;
        var collision = spray.collision;
        collision.enabled = true;
        collision.type = ParticleSystemCollisionType.World;
        collision.quality = ParticleSystemCollisionQuality.Low;
        collision.dampen = 0.7f;
        collision.bounce = 0.1f;
        collision.lifetimeLoss = 0.1f;
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = Fx.SoftMaterial;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        spray.Play();
        sprayHiss = Fx.Loop(null, Fx.Spray);
    }

    void ClearFlames()
    {
        foreach (var f in burning) Undress(f);
        foreach (var f in burning)
            if (!f.original && f.t) Destroy(f.t.gameObject);
        burning.Clear();
    }

    protected override void OnHazardEnded()
    {
        spraying = false;
        if (spray) { var e = spray.emission; e.rateOverTimeMultiplier = 0f; }
        if (sprayHiss) sprayHiss.volume = 0f;
        if (flames)
        {
            if (flamesScale != Vector3.zero) flames.transform.localScale = flamesScale;
            flames.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
        ClearFlames();
        if (fireLight) fireLight.enabled = false;
    }

    void OnDestroy()
    {
        if (markers) Destroy(markers.gameObject);
        if (spray) Destroy(spray.gameObject);
        if (sprayHiss) Destroy(sprayHiss.gameObject);
    }
}
