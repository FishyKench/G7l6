using UnityEngine;

/// <summary>
/// A TV randomly switches to loud static. Stress keeps rising until the player turns it off
/// with the TV remote (pick the remote up, then press E on the TV).
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
    GameObject overlay; // a flat panel over the screen that shows the static (see BuildOverlay)

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
    /// TV models map their screen onto a tiny corner of a shared texture sheet, so swapping the screen's material
    /// for the static one just shows one speck of noise stretched flat. Instead, lay a thin panel exactly over the
    /// screen (its own 0-1 texture space) and show the static on that.
    /// </summary>
    void BuildOverlay()
    {
        if (!screenRenderer || !staticMaterial) return;
        var filter = screenRenderer.GetComponent<MeshFilter>();
        if (!filter || !filter.sharedMesh) return;
        var mesh = filter.sharedMesh;
        Bounds b = mesh.bounds;
        if (mesh.subMeshCount > 1 && screenMaterialIndex < mesh.subMeshCount)
        {
            var sub = mesh.GetSubMesh(screenMaterialIndex).bounds;
            if (sub.size.sqrMagnitude > 0.000001f) b = sub;
        }

        // The thinnest side of the screen is its facing direction; the others are width and height.
        var t = screenRenderer.transform;
        Vector3[] axes = { Vector3.right, Vector3.up, Vector3.forward };
        Vector3 worldSize = Vector3.Scale(b.size, Abs(t.lossyScale));
        int thin = 0;
        for (int i = 1; i < 3; i++) if (worldSize[i] < worldSize[thin]) thin = i;
        int a = (thin + 1) % 3, c = (thin + 2) % 3;
        int up = Mathf.Abs(t.TransformDirection(axes[a]).y) >= Mathf.Abs(t.TransformDirection(axes[c]).y) ? a : c;
        int across = up == a ? c : a;

        // Which way the screen faces: towards the front of the TV (+Z of the TV object), or away from the body.
        Vector3 normal = axes[thin];
        float facing = Vector3.Dot(t.TransformDirection(normal), transform.forward);
        if (Mathf.Abs(facing) < 0.3f) facing = b.center[thin] - mesh.bounds.center[thin];
        if (facing < 0f) normal = -normal;

        overlay = GameObject.CreatePrimitive(PrimitiveType.Quad);
        overlay.name = "Static";
        Destroy(overlay.GetComponent<Collider>());
        overlay.transform.SetParent(t, false);
        float lift = 0.004f / Mathf.Max(0.0001f, Mathf.Abs(t.lossyScale[thin]));
        overlay.transform.localPosition = b.center + normal * (b.extents[thin] + lift);
        overlay.transform.localRotation = Quaternion.LookRotation(-normal, axes[up]); // a quad shows its face towards -Z
        var scale = Vector3.one;
        scale.x = b.size[across] * 0.97f;
        scale.y = b.size[up] * 0.97f;
        overlay.transform.localScale = scale;
        var r = overlay.GetComponent<MeshRenderer>();
        r.sharedMaterial = staticMaterial;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        overlay.SetActive(false);
    }

    static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));

    public override string Status => needsRemote && !PlayerHolds(ItemType.Remote)
        ? "Static! Find the TV remote"
        : "Static! Turn it off";

    public string Prompt
    {
        get
        {
            if (!IsActive) return null;
            return needsRemote && !PlayerHolds(ItemType.Remote) ? "TV - needs the remote" : "Turn off TV";
        }
    }

    public void Interact()
    {
        if (!IsActive) return;
        if (needsRemote && !PlayerHolds(ItemType.Remote))
        {
            GameHUD.Toast("Find the TV remote!", GameHUD.Warn);
            return;
        }
        Complete("Peace and quiet.");
    }

    protected override void OnActivated() => SetStatic(true);

    protected override void TaskUpdate()
    {
        if (Stress) Stress.AddStress(stressPerSecond * Time.deltaTime);
    }

    protected override void OnCompleted() => SetStatic(false);
    protected override void OnFailed() => SetStatic(false);

    void SetStatic(bool on)
    {
        if (overlay) overlay.SetActive(on);
        else if (screenRenderer && ValidSlot())
        {
            var mats = screenRenderer.sharedMaterials;
            mats[screenMaterialIndex] = on ? staticMaterial : (offMaterial ? offMaterial : originalMaterial);
            screenRenderer.sharedMaterials = mats;
        }
        if (staticSound)
        {
            if (on) staticSound.Play();
            else staticSound.Stop();
        }
    }

    bool ValidSlot() => screenMaterialIndex >= 0 && screenMaterialIndex < screenRenderer.sharedMaterials.Length;
}
