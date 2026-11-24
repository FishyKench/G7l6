using UnityEngine;

/// <summary>
/// Keeps the VR arena alive: spins anything named "Spin ..." (holograms, light sweeps), pulses anything named
/// "Pulse ..." (neon, lights), breathes the floor grid's glow, and fills the air with drifting light specks.
/// Put on the arena root (the VR setup tool does that).
/// </summary>
public class VRAmbience : MonoBehaviour
{
    public float spinSpeed = 25f;
    public Color floorGlow = new Color(0.35f, 0.75f, 0.9f);

    Transform[] spinners;
    (Renderer r, Light l, float phase, float baseIntensity, Color baseEmission)[] pulsers;
    Material floor;

    void Start() => Init();

    /// <summary>Finds what to spin / pulse. (Runs again if the editor reloaded the scripts mid-play - that wipes these lists
    /// without calling Start, e.g. after alt-tabbing back in with changed code.)</summary>
    void Init()
    {
        var all = GetComponentsInChildren<Transform>(true);
        spinners = System.Array.FindAll(all, t => t.name.StartsWith("Spin"));
        var list = new System.Collections.Generic.List<(Renderer, Light, float, float, Color)>();
        foreach (var t in all)
        {
            if (!t.name.StartsWith("Pulse")) continue;
            var r = t.GetComponent<Renderer>();
            var l = t.GetComponent<Light>();
            Color e = r && r.sharedMaterial && r.sharedMaterial.HasProperty("_EmissionColor") ? r.sharedMaterial.GetColor("_EmissionColor") : Color.black;
            if (r) r.material = r.sharedMaterial; // its own copy, so it can pulse alone
            list.Add((r, l, Random.value * 10f, l ? l.intensity : 0f, e));
        }
        pulsers = list.ToArray();
        var floorObj = transform.Find("Floor");
        var floorRenderer = floorObj ? floorObj.GetComponent<Renderer>() : null;
        if (floorRenderer) floor = floorRenderer.material;
        if (!transform.Find("Light specks")) BuildSpecks();
    }

    void Update()
    {
        if (PauseMenu.IsPaused) return;
        if (spinners == null || pulsers == null) Init();
        float t = Time.time;
        for (int i = 0; i < spinners.Length; i++)
            if (spinners[i]) spinners[i].Rotate(0f, spinSpeed * (i % 2 == 0 ? 1f : -1.4f) * Time.deltaTime, 0f, Space.Self);
        foreach (var (r, l, phase, baseIntensity, emission) in pulsers)
        {
            float k = 0.65f + 0.35f * Mathf.Sin(t * 2.2f + phase);
            if (r) r.material.SetColor("_EmissionColor", emission * k);
            if (l) l.intensity = baseIntensity * (0.7f + 0.3f * k);
        }
        if (floor) floor.SetColor("_EmissionColor", floorGlow * (0.75f + 0.25f * Mathf.Sin(t * 0.9f)));
    }

    void BuildSpecks()
    {
        var go = new GameObject("Light specks");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = new Vector3(0f, 3f, 0f);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.loop = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(6f, 10f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.05f, 0.3f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.08f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.4f, 1f, 0.95f, 0.8f), new Color(1f, 0.4f, 0.85f, 0.8f));
        main.maxParticles = 500;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        var emission = ps.emission;
        emission.rateOverTime = 45f;
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(60f, 6f, 40f);
        var colour = ps.colorOverLifetime;
        colour.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.3f), new GradientAlphaKey(1f, 0.7f), new GradientAlphaKey(0f, 1f) });
        colour.color = g;
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = new Material(Shader.Find("Sprites/Default"));
        ps.Play();
    }
}
