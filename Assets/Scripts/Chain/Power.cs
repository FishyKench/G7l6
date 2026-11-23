using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// The house's electricity. A flood that reaches a wall socket shorts it: the fuse blows, the lights go out and so does your
/// PC (and the VR match with it) until you reset the fuse box in the garage - and it trips again straight away while the
/// socket's still standing in water.
/// </summary>
public static class Power
{
    public static bool Out { get; private set; }
    /// <summary>Where it shorted (the socket in the water).</summary>
    public static Vector3 ShortedAt { get; private set; }

    static readonly List<Light> switchedOff = new List<Light>();

    public static void Cut(Vector3 at)
    {
        if (Out) return;
        Out = true;
        ShortedAt = at;
        Sparks(at);

        // the lights (not the sun, not fire)
        switchedOff.Clear();
        foreach (var l in Object.FindObjectsOfType<Light>())
        {
            if (!l.enabled || l.type == LightType.Directional) continue;
            if (l.GetComponentInParent<FireHazard>() || l.GetComponentInParent<MomInspector>() || l.GetComponentInParent<FuseBox>()) continue;
            l.enabled = false;
            switchedOff.Add(l);
        }

        // the PC and the headset
        MatchTask.TakeOffHeadset("The power went out!");
        var pi = PlayerInteraction.Instance;
        if (pi && pi.CurrentMainTask is GamingPC) pi.ForceStopMainTask();

        if (StressManager.Instance) StressManager.Instance.AddStress(6f);
        GameHUD.Toast("BZZT - the power's out! Your phone torch is on - reset the fuse box in the garage.", GameHUD.Bad);
        Torch(true);
        var box = FuseBox.Instance ? FuseBox.Instance : FuseBox.Create();
        if (box && GameManager.Instance) GameManager.Instance.StartTask(box);
    }

    public static void Restore()
    {
        if (!Out) return;
        Out = false;
        foreach (var l in switchedOff) if (l) l.enabled = true;
        switchedOff.Clear();
        Torch(false);
        GameHUD.Toast("Power's back on.", GameHUD.Good);
    }

    /// <summary>Things that need electricity: what to say when you try to use them.</summary>
    public static bool Needs(object thing) => thing is GamingPC || thing is VRHeadset;

    public static void Sparks(Vector3 at)
    {
        float s = HouseLayout.Instance ? HouseLayout.Instance.scale : 1f;
        Fx.Puff(at, new Color(1f, 0.85f, 0.3f), 10, s * 0.6f);
        Fx.Puff(at, new Color(0.35f, 0.35f, 0.38f, 0.8f), 6, s * 0.8f);
        Fx.Play(Fx.Sizzle, at, 0.8f, 1.6f);
        Fx.Play(Fx.Click, at, 1f, 0.6f);
        FloatingWord.Spawn("BZZT!", at + Vector3.up * 0.4f * s, new Color(1f, 0.8f, 0.2f), GameHUD.Instance ? GameHUD.Instance.font : null, 2f, 1.4f);
    }

    /// <summary>A new day: the power's on.</summary>
    public static void Reset()
    {
        Out = false;
        switchedOff.Clear();
        torch = null;
    }

    static Light torch;

    /// <summary>Your phone's torch while the lights are out (so you can find your way to the fuse box).</summary>
    static void Torch(bool on)
    {
        if (!on) { if (torch) Object.Destroy(torch.gameObject); torch = null; return; }
        var cam = Camera.main;
        if (!cam || torch) return;
        float s = HouseLayout.Instance ? HouseLayout.Instance.scale : 1f;
        torch = new GameObject("Phone torch").AddComponent<Light>();
        torch.transform.SetParent(cam.transform, false);
        torch.transform.localPosition = new Vector3(0.18f, -0.12f, 0.05f);
        torch.type = LightType.Spot;
        torch.spotAngle = 75f;
        torch.innerSpotAngle = 35f;
        torch.range = 14f * s;
        torch.intensity = 1.6f;
        torch.color = new Color(1f, 0.96f, 0.88f);
        torch.shadows = LightShadows.None;
    }
}

/// <summary>
/// The fuse box on the garage wall. When the power's out it's a task: open it (E) and flip the tripped switch back (E) -
/// unless the socket that shorted is still in water, then it trips again and you have to mop first.
/// </summary>
public class FuseBox : TaskBase, IInteractable, IPromptProvider
{
    public static FuseBox Instance { get; private set; }

    Transform door, tripped;
    Light warning;
    Renderer bulbRenderer;
    bool open;
    float doorAngle;

    void Awake()
    {
        Instance = this;
        canStartRandomly = false;
        isMainTask = false;
        timeLimit = 0f;
        cooldown = 0f;
        taskName = "Reset the fuse box";
        stressReliefOnComplete = 4f;
    }

    public override string Status => open ? "Flip the tripped switch back up" : "Power's out - fuse box, garage wall";
    public string Prompt => !IsActive ? null : open ? "Flip the switch back" : "Open the fuse box";

    public void Interact()
    {
        if (!IsActive) return;
        if (!open)
        {
            open = true;
            Fx.Play(Fx.Click, transform.position, 0.7f, 1.2f);
            return;
        }
        if (ChainReactions.SocketStillWet())
        {
            Power.Sparks(Power.ShortedAt);
            Fx.Play(Fx.Click, transform.position, 1f, 0.8f);
            GameHUD.Toast("It trips straight away - the socket's still in water. Mop it up first!", GameHUD.Warn);
            return;
        }
        Fx.Play(Fx.Click, transform.position, 1f, 1f);
        open = false;
        Power.Restore();
        Complete("Power's back on.");
    }

    protected override void TaskUpdate() { }

    protected override void Update()
    {
        base.Update();
        doorAngle = Mathf.MoveTowards(doorAngle, open && IsActive ? -110f : 0f, Time.deltaTime * 400f);
        if (door) door.localRotation = Quaternion.Euler(0f, doorAngle, 0f);
        if (tripped) tripped.localRotation = Quaternion.Euler(Power.Out ? 35f : -35f, 0f, 0f);
        bool blink = Power.Out && Mathf.Repeat(Time.time, 1f) < 0.6f;
        if (warning) warning.enabled = blink;
        if (bulbRenderer) bulbRenderer.enabled = Power.Out;
    }

    protected override void OnCompleted() { open = false; }
    protected override void OnFailed() { open = false; }

    /// <summary>Builds the fuse box on a garage wall (at chest height, away from doors).</summary>
    public static FuseBox Create()
    {
        var layout = HouseLayout.Instance;
        if (!layout) return null;
        HouseLayout.RoomInfo room = null;
        foreach (var r in layout.rooms) if (r.name == "Garage" && r.floor == 0) room = r;
        if (room == null) foreach (var r in layout.rooms) if (r.floor == 0) { room = r; break; }
        if (room == null) return null;
        float s = layout.scale;
        Vector3 centre = layout.transform.TransformPoint(new Vector3(room.rect.center.x, room.floor * layout.storeyHeight + 1.45f, room.rect.center.y));
        Vector3 best = Vector3.zero, normal = Vector3.forward;
        float bestScore = float.MinValue;
        var doors = FindObjectsOfType<HouseDoor>();
        for (int i = 0; i < 16; i++)
        {
            Vector3 dir = Quaternion.Euler(0f, i * 22.5f, 0f) * Vector3.forward;
            if (!Physics.Raycast(centre, dir, out var hit, 12f * s, ~0, QueryTriggerInteraction.Ignore)) continue;
            if (Mathf.Abs(hit.normal.y) > 0.3f || hit.collider.GetComponentInParent<Carryable>() || hit.collider.GetComponentInParent<HouseDoor>() || hit.collider.GetComponentInParent<GarageDoor>()) continue;
            float doorGap = float.MaxValue;
            foreach (var d in doors) doorGap = Mathf.Min(doorGap, Vector3.Distance(d.transform.position, hit.point));
            // a wall, not furniture: something long and flat
            float score = Mathf.Min(doorGap, 3f * s) - hit.distance * 0.1f + (hit.collider.bounds.size.y > 2f * s ? 2f : 0f);
            if (score > bestScore) { bestScore = score; best = hit.point; normal = new Vector3(hit.normal.x, 0f, hit.normal.z).normalized; }
        }
        if (bestScore == float.MinValue) return null;

        var go = new GameObject("Fuse box");
        go.transform.position = best + normal * 0.06f * s;
        go.transform.rotation = Quaternion.LookRotation(normal);
        var box = go.AddComponent<FuseBox>();
        box.Build(s);
        return box;
    }

    void Build(float s)
    {
        var grey = Mat(new Color(0.62f, 0.64f, 0.66f));
        var dark = Mat(new Color(0.16f, 0.17f, 0.19f));
        var red = Mat(new Color(0.85f, 0.18f, 0.14f));
        var white = Mat(new Color(0.92f, 0.92f, 0.9f));
        var yellow = Mat(new Color(0.98f, 0.8f, 0.12f));
        Part(PrimitiveType.Cube, transform, new Vector3(0f, 0f, -0.03f), new Vector3(0.34f, 0.44f, 0.1f), grey, s, true);
        Part(PrimitiveType.Cube, transform, new Vector3(0f, 0f, 0.021f), new Vector3(0.3f, 0.4f, 0.004f), dark, s, false);
        // a row of switches, one tripped (red)
        for (int i = 0; i < 6; i++)
        {
            var sw = new GameObject("Switch").transform;
            sw.SetParent(transform, false);
            sw.localPosition = new Vector3(-0.1f + i * 0.04f, 0.06f, 0.025f) * s;
            Part(PrimitiveType.Cube, sw, new Vector3(0f, 0.012f, 0.008f), new Vector3(0.022f, 0.04f, 0.016f), i == 3 ? red : white, s, false);
            if (i == 3) tripped = sw;
        }
        Part(PrimitiveType.Cube, transform, new Vector3(0f, -0.07f, 0.024f), new Vector3(0.24f, 0.05f, 0.004f), white, s, false);
        // the door, hinged on the left
        door = new GameObject("Door").transform;
        door.SetParent(transform, false);
        door.localPosition = new Vector3(-0.17f, 0f, 0.03f) * s;
        Part(PrimitiveType.Cube, door, new Vector3(0.17f, 0f, 0f), new Vector3(0.34f, 0.44f, 0.012f), grey, s, false);
        Part(PrimitiveType.Cube, door, new Vector3(0.17f, 0.1f, 0.008f), new Vector3(0.1f, 0.09f, 0.004f), yellow, s, false); // the lightning sticker
        var label = new GameObject("Label").AddComponent<TextMeshPro>();
        label.transform.SetParent(door, false);
        label.transform.localPosition = new Vector3(0.17f, -0.08f, 0.0075f) * s;
        label.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        label.text = "FUSES";
        label.fontSize = 0.9f * s;
        label.color = new Color(0.15f, 0.15f, 0.17f);
        label.alignment = TextAlignmentOptions.Center;
        label.rectTransform.sizeDelta = new Vector2(0.3f, 0.08f) * s;
        if (GameHUD.Instance && GameHUD.Instance.font) label.font = GameHUD.Instance.font;
        // a little red warning light on top (on its own battery: it shows the way in the dark)
        var led = new GameObject("Warning light").AddComponent<Light>();
        led.transform.SetParent(transform, false);
        led.transform.localPosition = new Vector3(0f, 0.3f, 0.12f) * s;
        led.type = LightType.Point;
        led.color = new Color(1f, 0.2f, 0.12f);
        led.range = 1.6f * s;
        led.intensity = 1.2f;
        warning = led;
        var bulb = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Destroy(bulb.GetComponent<Collider>());
        bulb.transform.SetParent(transform, false);
        bulb.transform.localPosition = new Vector3(0f, 0.245f, 0.0f) * s;
        bulb.transform.localScale = Vector3.one * 0.035f * s;
        var bm = new Material(Shader.Find("Standard")) { color = new Color(0.6f, 0.05f, 0.03f) };
        bm.EnableKeyword("_EMISSION");
        bm.SetColor("_EmissionColor", new Color(1f, 0.15f, 0.08f) * 2f);
        bulb.GetComponent<Renderer>().sharedMaterial = bm;
        bulbRenderer = bulb.GetComponent<Renderer>();
        var outline = gameObject.AddComponent<Outline>();
        outline.OutlineMode = Outline.Mode.OutlineVisible;
        outline.OutlineColor = Color.white;
        outline.OutlineWidth = 5f;
        outline.enabled = false;
    }

    static void Part(PrimitiveType shape, Transform parent, Vector3 pos, Vector3 size, Material m, float s, bool keepCollider)
    {
        var p = GameObject.CreatePrimitive(shape);
        if (!keepCollider) Destroy(p.GetComponent<Collider>());
        p.transform.SetParent(parent, false);
        p.transform.localPosition = pos * s;
        p.transform.localScale = size * s;
        p.GetComponent<Renderer>().sharedMaterial = m;
    }

    static Material Mat(Color c)
    {
        var m = new Material(Shader.Find("Standard")) { color = c };
        m.SetFloat("_Glossiness", 0.3f);
        return m;
    }
}
