using System.Collections;
using UnityEngine;

/// <summary>
/// A TV randomly switches to loud static. Stress keeps rising until the player turns it off
/// with the TV remote (pick the remote up, then press E on the TV): you raise the remote, click it (its light
/// blinks) and the TV switches off like an old set - the picture flashes and squashes into a line, then a dot.
/// Put one on each TV - the GameManager picks which one goes off.
/// </summary>
public class TvTask : TaskBase, IInteractable, IPromptProvider
{
    [Header("TV")]
    public Renderer screenRenderer;
    [Tooltip("Which material slot on the screen renderer is the screen.")]
    public int screenMaterialIndex = 0;
    public Material staticMaterial;
    [Tooltip("Material when the TV is off. Leave empty to keep whatever the screen had.")]
    public Material offMaterial;
    public AudioSource staticSound;
    public float stressPerSecond = 0.6f;
    [Tooltip("If on, the player has to be holding the TV remote to turn it off.")]
    public bool needsRemote = true;

    Material originalMaterial;
    Material projectedStatic; // the static, projected across the whole screen (see BuildOverlay)
    bool switching;
    float soundVolume = -1f;

    void Reset()
    {
        taskName = "Turn off the TV";
        timeLimit = 0f;          // never fails, it just keeps stressing you out
        stressOnFail = 0f;
        stressReliefOnComplete = 5f;
        cooldown = 25f;
    }

    void Start()
    {
        if (screenRenderer && ValidSlot())
            originalMaterial = screenRenderer.sharedMaterials[screenMaterialIndex];
        BuildOverlay();
        SetStatic(false);
    }

    /// <summary>
    /// TV models lay their screen out on a small part of a shared texture, so the static picture can't just be
    /// swapped in. Instead the static is projected straight across the screen part of the model (see
    /// ScreenProjector) - it fills the whole screen, curved or flat.
    /// </summary>
    void BuildOverlay()
    {
        if (!screenRenderer || !staticMaterial || !ValidSlot()) return;
        projectedStatic = ScreenProjector.Make(screenRenderer, screenMaterialIndex, staticMaterial.mainTexture, transform.forward, true, out _);
    }


    public override string Status => needsRemote && !PlayerHolds(ItemType.Remote)
        ? "Static! Find the TV remote"
        : "Static! Turn it off";

    public string Prompt
    {
        get
        {
            if (!IsActive || switching) return null;
            return needsRemote && !PlayerHolds(ItemType.Remote) ? "TV - needs the remote" : "Turn off TV";
        }
    }

    public void Interact()
    {
        if (!IsActive || switching) return;
        if (needsRemote && !PlayerHolds(ItemType.Remote))
        {
            GameHUD.Toast("Find the TV remote!", GameHUD.Warn);
            return;
        }
        StartCoroutine(SwitchOff());
    }

    /// <summary>Raise the remote at the TV, click, and the TV switches off.</summary>
    IEnumerator SwitchOff()
    {
        switching = true;
        var motion = PlayerHolds(ItemType.Remote) ? HeldItemMotion.Get() : null;
        var hold = PlayerCarry.Instance ? PlayerCarry.Instance.holdPoint : null;

        // 1. Raise it and point it at the screen.
        for (float t = 0f; t < 0.2f; t += Time.deltaTime)
        {
            if (motion) motion.Pose(new Vector3(-0.2f, 0.13f, 0.08f), AimAtTv(), 0.05f);
            yield return null;
        }

        // 2. Click: the thumb pushes the remote down a touch, and its light blinks.
        if (hold) Fx.Play(Fx.Click, hold.position, 0.9f, Random.Range(0.95f, 1.08f));
        if (hold) StartCoroutine(Blink(hold));
        for (float t = 0f; t < 0.1f; t += Time.deltaTime)
        {
            if (motion) motion.Pose(new Vector3(-0.2f, 0.1f, 0.1f), AimAtTv() + new Vector3(10f, 0f, 0f), 0.025f);
            yield return null;
        }

        // 3. The screen collapses like an old TV.
        Fx.Play(Fx.TvOff, screenRenderer ? screenRenderer.bounds.center : transform.position, 0.8f);
        var mat = projectedStatic;
        float startPitch = staticSound ? staticSound.pitch : 1f;
        for (float t = 0f; t < 0.42f; t += Time.deltaTime)
        {
            if (motion) motion.Pose(new Vector3(-0.2f, 0.12f, 0.08f), AimAtTv(), 0.06f);
            float squash = Mathf.Clamp01(t / 0.13f);
            float shrink = Mathf.Clamp01((t - 0.13f) / 0.17f);
            float fade = Mathf.Clamp01((t - 0.3f) / 0.12f);
            if (mat) mat.SetVector("_Collapse", new Vector4(Mathf.Lerp(1f, 0.012f, squash * squash), Mathf.Lerp(1f, 0.012f, shrink), Mathf.Min(1f, squash * 1.3f) * (1f - fade), 0f));
            if (staticSound)
            {
                staticSound.pitch = Mathf.Lerp(startPitch, 0.35f, squash);
                staticSound.volume = (soundVolume < 0f ? 1f : soundVolume) * (1f - squash);
            }
            yield return null;
        }
        switching = false;
        if (IsActive) Complete("Peace and quiet.");
    }

    /// <summary>The turn that points the held remote at the TV (from the hold point, in camera space).</summary>
    Vector3 AimAtTv()
    {
        var hold = PlayerCarry.Instance ? PlayerCarry.Instance.holdPoint : null;
        var cam = Camera.main;
        if (!hold || !cam) return Vector3.zero;
        Vector3 target = screenRenderer ? screenRenderer.bounds.center : transform.position;
        Vector3 d = cam.transform.InverseTransformDirection(target - hold.position);
        float yaw = Mathf.Clamp(Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, -35f, 35f);
        float pitch = Mathf.Clamp(-Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg, -30f, 30f);
        return new Vector3(pitch - 8f, yaw, 0f);
    }

    /// <summary>The remote's little red light blinks.</summary>
    static IEnumerator Blink(Transform hold)
    {
        var cam = Camera.main;
        var led = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Destroy(led.GetComponent<Collider>());
        led.name = "Remote light";
        led.transform.SetParent(hold, false);
        led.transform.position = hold.position + (cam ? cam.transform.forward : hold.forward) * 0.14f + Vector3.up * 0.015f;
        led.transform.localScale = Vector3.one * 0.018f / Mathf.Max(0.0001f, hold.lossyScale.x);
        var m = new Material(Shader.Find("Standard")) { color = new Color(1f, 0.15f, 0.1f) };
        m.EnableKeyword("_EMISSION");
        m.SetColor("_EmissionColor", new Color(1f, 0.1f, 0.05f) * 4f);
        var r = led.GetComponent<Renderer>();
        r.sharedMaterial = m;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        var light = led.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = new Color(1f, 0.2f, 0.1f);
        light.range = 0.5f;
        light.intensity = 3f;
        for (int i = 0; i < 2; i++)
        {
            led.SetActive(true);
            yield return new WaitForSeconds(0.06f);
            led.SetActive(false);
            yield return new WaitForSeconds(0.05f);
        }
        Destroy(led);
        Destroy(m);
    }

    protected override void OnActivated() { toldHeadphones = false; SetStatic(true); }

    protected override void TaskUpdate()
    {
        if (Stress) Stress.AddStress(stressPerSecond * Time.deltaTime * HeadphoneFactor());
    }

    bool toldHeadphones;

    /// <summary>Noise-cancelling headphones: the static stresses you far less while you are at the PC or in VR.</summary>
    float HeadphoneFactor()
    {
        if (!Shop.IsInstalled(Shop.Headphones)) return 1f;
        bool covered = VRHeadset.Worn || (GamingPC.Instance && GamingPC.Instance.Seated);
        if (covered && !toldHeadphones)
        {
            toldHeadphones = true;
            GameHUD.Toast("Headphones on: the TV static barely bothers you.", GameHUD.Good);
        }
        return covered ? 0.2f : 1f;
    }

    protected override void OnCompleted() => SetStatic(false);
    protected override void OnFailed() { switching = false; SetStatic(false); }

    void SetStatic(bool on)
    {
        if (projectedStatic) projectedStatic.SetVector("_Collapse", new Vector4(1f, 1f, 0f, 0f));
        if (staticSound)
        {
            if (soundVolume < 0f) soundVolume = staticSound.volume;
            staticSound.volume = soundVolume;
            staticSound.pitch = 1f;
        }
        if (screenRenderer && ValidSlot())
        {
            var mats = screenRenderer.sharedMaterials;
            var staticLook = projectedStatic ? projectedStatic : staticMaterial;
            mats[screenMaterialIndex] = on ? staticLook : (offMaterial ? offMaterial : originalMaterial);
            screenRenderer.sharedMaterials = mats;
        }
        if (staticSound)
        {
            if (on) staticSound.Play();
            else staticSound.Stop();
        }
    }

    bool ValidSlot() => screenMaterialIndex >= 0 && screenMaterialIndex < screenRenderer.sharedMaterials.Length;

    // ---- broken (water dripped into it)

    /// <summary>Water got into it: it's dead for the rest of the day.</summary>
    public bool Broken { get; private set; }
    public override bool CanStartNow => !Broken;

    /// <summary>Fries it: sparks, a cracked dark screen, never comes on again today.</summary>
    public void Break()
    {
        if (Broken) return;
        Broken = true;
        if (IsActive) Fail("The TV shorted out!");
        SetStatic(false);
        if (!screenRenderer) return;
        var b = screenRenderer.bounds;
        Power.Sparks(b.center);
        var crack = new GameObject("Cracked screen");
        crack.transform.position = b.center + transform.forward * 0.02f;
        crack.transform.rotation = Quaternion.LookRotation(-transform.forward);
        float w = Mathf.Max(Mathf.Abs(Vector3.Dot(b.size, transform.right)), 0.3f), h = b.size.y;
        crack.transform.localScale = new Vector3(w * 0.95f, h * 0.9f, 1f);
        var mf = crack.AddComponent<MeshFilter>();
        mf.sharedMesh = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
        var r = crack.AddComponent<MeshRenderer>();
        r.sharedMaterial = new Material(Shader.Find("Sprites/Default")) { mainTexture = CrackTexture() };
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    static Texture2D crackTex;

    /// <summary>A dark screen with a starburst of cracks.</summary>
    static Texture2D CrackTexture()
    {
        if (crackTex) return crackTex;
        const int N = 128;
        crackTex = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[N * N];
        for (int i = 0; i < px.Length; i++) px[i] = new Color32(8, 8, 10, 235);
        var rnd = new System.Random(7);
        var c = new Vector2(N * 0.42f, N * 0.58f);
        for (int ray = 0; ray < 11; ray++)
        {
            float a = ray / 11f * Mathf.PI * 2f + (float)rnd.NextDouble() * 0.4f;
            Vector2 p = c;
            for (int step = 0; step < 90; step++)
            {
                a += ((float)rnd.NextDouble() - 0.5f) * 0.35f;
                p += new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 1.2f;
                int x = (int)p.x, y = (int)p.y;
                if (x < 1 || y < 1 || x >= N - 1 || y >= N - 1) break;
                px[y * N + x] = new Color32(200, 210, 220, 255);
                px[y * N + x + 1] = new Color32(120, 128, 136, 255);
            }
        }
        crackTex.SetPixels32(px);
        crackTex.Apply();
        return crackTex;
    }
}
