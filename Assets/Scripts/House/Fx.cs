using UnityEngine;

/// <summary>
/// Little effects and sounds made in code, so tools feel like they do something: clicks, swishes, the spray
/// hiss, puffs of smoke and splashes. Sounds are generated once (no audio files needed).
/// </summary>
public static class Fx
{
    // ------------------------------------------------------------------ particles

    static Material soft;
    /// <summary>Soft round particles (shader G7l6/Soft Particle; falls back to Sprites/Default).</summary>
    public static Material SoftMaterial
    {
        get
        {
            if (soft) return soft;
            var shader = Shader.Find("G7l6/Soft Particle");
            soft = new Material(shader ? shader : Shader.Find("Sprites/Default")) { name = "Soft particle" };
            return soft;
        }
    }

    /// <summary>A particle system that only emits when told (for puffs and splashes).</summary>
    public static ParticleSystem Burster(string name, float lifeMin, float lifeMax, float speedMin, float speedMax,
        float sizeMin, float sizeMax, float gravity, float grow)
    {
        var go = new GameObject(name);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.playOnAwake = false;
        main.loop = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(lifeMin, lifeMax);
        main.startSpeed = new ParticleSystem.MinMaxCurve(speedMin, speedMax);
        main.startSize = new ParticleSystem.MinMaxCurve(sizeMin, sizeMax);
        main.gravityModifier = gravity;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 800;
        var emission = ps.emission;
        emission.enabled = false;
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.05f;
        var fade = ps.colorOverLifetime;
        fade.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
        fade.color = new ParticleSystem.MinMaxGradient(g);
        if (grow > 0f)
        {
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 1f + grow));
        }
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = SoftMaterial;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        ps.Play();
        return ps;
    }

    static ParticleSystem puffs, splashes;

    /// <summary>A puff of smoke/steam (a flame going out).</summary>
    public static void Puff(Vector3 at, Color color, int count, float scale = 1f)
    {
        if (!puffs) puffs = Burster("Fx puffs", 0.8f, 1.6f, 0.3f, 1.1f, 0.25f, 0.5f, -0.08f, 2.5f);
        var p = new ParticleSystem.EmitParams { position = at, startColor = color, applyShapeToPosition = true, startSize = 0.35f * scale };
        puffs.Emit(p, count);
    }

    /// <summary>Droplets flicking up (mopping water).</summary>
    public static void Splash(Vector3 at, int count, float scale = 1f)
    {
        if (!splashes) splashes = Burster("Fx splashes", 0.25f, 0.5f, 0.8f, 2f, 0.03f, 0.06f, 1.2f, 0f);
        for (int i = 0; i < count; i++)
        {
            var p = new ParticleSystem.EmitParams
            {
                position = at + Random.insideUnitSphere * 0.08f * scale,
                velocity = (Vector3.up * Random.Range(1f, 2.2f) + Random.insideUnitSphere * 0.9f) * scale,
                startColor = new Color(0.75f, 0.88f, 1f, 0.8f),
                startSize = Random.Range(0.03f, 0.06f) * scale,
                applyShapeToPosition = false,
            };
            splashes.Emit(p, 1);
        }
    }

    // ------------------------------------------------------------------ sounds

    public static void Play(AudioClip clip, Vector3 at, float volume = 1f, float pitch = 1f)
    {
        if (!clip) return;
        var go = new GameObject("Sound - " + clip.name);
        go.transform.position = at;
        var src = go.AddComponent<AudioSource>();
        src.clip = clip;
        src.volume = volume;
        src.pitch = pitch;
        src.spatialBlend = 0.7f;
        src.minDistance = 1.5f;
        src.Play();
        Object.Destroy(go, clip.length / Mathf.Max(0.1f, pitch) + 0.1f);
    }

    /// <summary>A looping sound source (spray hiss) you turn up and down yourself.</summary>
    public static AudioSource Loop(Transform parent, AudioClip clip)
    {
        var go = new GameObject("Loop - " + clip.name);
        go.transform.SetParent(parent, false);
        var src = go.AddComponent<AudioSource>();
        src.clip = clip;
        src.loop = true;
        src.volume = 0f;
        src.spatialBlend = 0.4f;
        src.Play();
        return src;
    }

    static AudioClip click, tvOff, swish, spray, sizzle, boom, thud;

    /// <summary>A door closing: a short wooden knock.</summary>
    public static AudioClip Thud => thud ? thud : thud = Make("thud", 0.25f, (t, white, low) =>
        Mathf.Sin(t * Mathf.PI * 2f * (95f - 40f * t)) * Mathf.Exp(-t * 22f) * 0.9f + low * Mathf.Exp(-t * 45f) * 0.5f, 0.1f);

    /// <summary>An explosion: a deep thump and a rumble.</summary>
    public static AudioClip Boom => boom ? boom : boom = Make("boom", 1.4f, (t, white, low) =>
        low * Mathf.Exp(-t * 3.2f) * 1.1f + Mathf.Sin(t * Mathf.PI * 2f * (70f - 30f * t)) * Mathf.Exp(-t * 6f) * 0.9f
        + white * Mathf.Exp(-t * 40f) * 0.5f, 0.06f);

    /// <summary>A plastic button click.</summary>
    public static AudioClip Click => click ? click : click = Make("click", 0.06f, (t, white, low) =>
        white * Mathf.Exp(-t * 180f) * 0.7f + Mathf.Sin(t * Mathf.PI * 2f * 2600f) * Mathf.Exp(-t * 110f) * 0.35f);

    /// <summary>An old TV switching off: a thunk and a fading whine.</summary>
    public static AudioClip TvOff => tvOff ? tvOff : tvOff = Make("tv off", 0.7f, (t, white, low) =>
        Mathf.Sin(t * Mathf.PI * 2f * (110f - 60f * t)) * Mathf.Exp(-t * 12f) * 0.7f
        + low * Mathf.Exp(-t * 30f) * 0.6f
        + Mathf.Sin(t * Mathf.PI * 2f * 7400f) * Mathf.Exp(-t * 4f) * 0.05f);

    /// <summary>A wet mop swish.</summary>
    public static AudioClip Swish => swish ? swish : swish = Make("swish", 0.28f, (t, white, low) =>
        low * Mathf.Sin(Mathf.Clamp01(t / 0.28f) * Mathf.PI) * 0.9f, 0.18f);

    /// <summary>The extinguisher's spray (loops).</summary>
    public static AudioClip Spray => spray ? spray : spray = Make("spray", 1f, (t, white, low) =>
        (white * 0.55f + low * 0.45f) * (0.85f + 0.15f * Mathf.Sin(t * Mathf.PI * 2f * 9f)) * 0.6f, 0.45f);

    /// <summary>Fire hissing out.</summary>
    public static AudioClip Sizzle => sizzle ? sizzle : sizzle = Make("sizzle", 0.6f, (t, white, low) =>
        (white - low) * Mathf.Exp(-t * 5f) * 0.8f, 0.3f);

    /// <param name="f">(time, white noise, smoothed noise) -> sample.</param>
    static AudioClip Make(string name, float seconds, System.Func<float, float, float, float> f, float smoothing = 0.25f)
    {
        const int rate = 44100;
        int n = Mathf.CeilToInt(seconds * rate);
        var data = new float[n];
        var rng = new System.Random(name.GetHashCode());
        float low = 0f;
        for (int i = 0; i < n; i++)
        {
            float white = (float)rng.NextDouble() * 2f - 1f;
            low += (white - low) * smoothing;
            data[i] = Mathf.Clamp(f(i / (float)rate, white, low * 2.2f), -1f, 1f);
        }
        var clip = AudioClip.Create(name, n, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }

    // ------------------------------------------------------------------ meshes

    static Mesh flatQuad;
    /// <summary>A 1 x 1 square lying flat (x/z), UVs 0..1 from the -x/-z corner. Shared.</summary>
    public static Mesh FlatQuad
    {
        get
        {
            if (flatQuad) return flatQuad;
            flatQuad = new Mesh { name = "Flat quad" };
            flatQuad.vertices = new[] { new Vector3(-0.5f, 0f, -0.5f), new Vector3(0.5f, 0f, -0.5f), new Vector3(-0.5f, 0f, 0.5f), new Vector3(0.5f, 0f, 0.5f) };
            flatQuad.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) };
            flatQuad.triangles = new[] { 0, 2, 1, 1, 2, 3 };
            flatQuad.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
            flatQuad.RecalculateBounds();
            return flatQuad;
        }
    }
}
