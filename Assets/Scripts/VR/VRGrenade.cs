using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A thrown grenade in the VR arena. It bounces, blinks faster as the fuse runs down, then:
///   FRAG  - blows up: heavy damage close by, falling off to nothing at 7 m, blocked by walls and cover.
///           (Enemies only - and a little to yourself if you're careless.)
///   FLASH - a blinding bang: anyone who can see it is blinded for a few seconds - longer if they were looking
///           right at it. Blinded bots can't see or shoot properly; you get a white screen and a ringing ear.
/// Bots see live frags near them and run.
/// </summary>
public class VRGrenade : MonoBehaviour
{
    public enum Kind { Frag, Flash }

    public static readonly List<VRGrenade> Live = new List<VRGrenade>();

    public Kind kind;
    public VRFighter thrower;
    public int team;
    public float FuseLeft => fuse;
    public Vector3 Position => transform.position;

    const float FragRadius = 7f, FragDamage = 135f, FlashRadius = 18f;
    float fuse;
    Light blink;
    Rigidbody body;
    static PhysicMaterial bouncy;

    public static VRGrenade Throw(Kind kind, VRFighter thrower, Vector3 from, Vector3 velocity)
    {
        var go = GameObject.CreatePrimitive(kind == Kind.Frag ? PrimitiveType.Sphere : PrimitiveType.Capsule);
        go.name = kind == Kind.Frag ? "Frag grenade" : "Flashbang";
        if (VRArena.Instance) go.transform.SetParent(VRArena.Instance.transform, true);
        go.transform.position = from;
        go.transform.localScale = kind == Kind.Frag ? Vector3.one * 0.13f : new Vector3(0.08f, 0.09f, 0.08f);
        go.GetComponent<Renderer>().sharedMaterial = kind == Kind.Frag
            ? VRArena.Glow(new Color(0.15f, 0.25f, 0.12f), new Color(0.4f, 1f, 0.3f), 0.6f)
            : VRArena.Glow(new Color(0.7f, 0.72f, 0.75f), Color.white, 0.5f);
        if (!bouncy) bouncy = new PhysicMaterial("Grenade") { bounciness = 0.35f, dynamicFriction = 0.6f, staticFriction = 0.7f, bounceCombine = PhysicMaterialCombine.Maximum };
        var col = go.GetComponent<Collider>();
        col.sharedMaterial = bouncy;
        if (thrower) foreach (var c in thrower.GetComponentsInChildren<Collider>()) Physics.IgnoreCollision(col, c);

        var g = go.AddComponent<VRGrenade>();
        g.kind = kind;
        g.thrower = thrower;
        g.team = thrower ? thrower.team : -1;
        g.fuse = kind == Kind.Frag ? 2.4f : 1.6f;
        g.body = go.AddComponent<Rigidbody>();
        g.body.mass = 0.4f;
        g.body.drag = 0.15f;
        g.body.angularDrag = 0.6f;
        g.body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        g.body.velocity = velocity;
        g.body.angularVelocity = Random.insideUnitSphere * 12f;
        g.blink = new GameObject("Blink").AddComponent<Light>();
        g.blink.transform.SetParent(go.transform, false);
        g.blink.type = LightType.Point;
        g.blink.color = kind == Kind.Frag ? new Color(1f, 0.25f, 0.15f) : new Color(0.8f, 0.9f, 1f);
        g.blink.range = 2.5f;
        g.blink.intensity = 0f;
        g.blink.shadows = LightShadows.None;
        Live.Add(g);
        ReplayRecorder.Throw(thrower, from, kind);
        return g;
    }

    /// <summary>The throw velocity to land at 'to' (an arc that takes about 'seconds').</summary>
    public static Vector3 Lob(Vector3 from, Vector3 to, float seconds)
    {
        seconds = Mathf.Max(0.3f, seconds);
        Vector3 d = to - from;
        return d / seconds - 0.5f * Physics.gravity * seconds;
    }

    void OnDestroy() => Live.Remove(this);

    void Update()
    {
        if (PauseMenu.IsPaused) return;
        fuse -= Time.deltaTime;
        // Blinks faster and faster.
        float rate = Mathf.Lerp(14f, 3f, Mathf.Clamp01(fuse / 2.4f));
        if (blink) blink.intensity = Mathf.Sin(Time.time * rate * Mathf.PI) > 0.4f ? 2.5f : 0f;
        if (fuse <= 0f) Explode();
    }

    void Explode()
    {
        Vector3 at = transform.position + Vector3.up * 0.15f;
        Live.Remove(this);
        ReplayRecorder.Boom(kind, at);
        if (kind == Kind.Frag) Frag(at); else Flash(at);
        Destroy(gameObject);
    }

    void Frag(Vector3 at)
    {
        var arena = VRArena.Instance;
        if (arena)
        {
            arena.Burst(at, new Color(1f, 0.6f, 0.2f), 40);
            arena.Burst(at, new Color(1f, 0.9f, 0.5f), 20);
        }
        Fx.Puff(at, new Color(0.25f, 0.24f, 0.26f, 0.8f), 26, 2.2f);
        Fx.Play(Fx.Boom, at, 1f, Random.Range(0.9f, 1.05f));
        Blast(at, new Color(1f, 0.65f, 0.3f), 2.4f, 10f);

        foreach (var f in VRFighter.All.ToArray())
        {
            if (!f || !f.Alive) continue;
            float d = Vector3.Distance(f.AimPoint, at);
            if (d > FragRadius) continue;
            bool self = f == thrower;
            if (!self && f.team == team) continue; // no friendly fire
            if (VRArena.LineBlocked(at, f.AimPoint)) continue;
            float dmg = FragDamage * Mathf.Pow(1f - d / FragRadius, 1.2f) * (self ? 0.5f : 1f);
            f.TakeDamage(dmg, thrower ? thrower : f, false);
            if (f is VRPlayer p) p.Shake(0.5f);
        }
        if (arena && arena.Player && arena.Player.Alive)
        {
            float d = Vector3.Distance(arena.Player.transform.position, at);
            if (d < 20f) arena.Player.Shake(Mathf.Lerp(0.45f, 0.05f, d / 20f));
        }
    }

    void Flash(Vector3 at)
    {
        Fx.Play(Fx.Boom, at, 0.9f, 1.7f);
        Blast(at, Color.white, 1.4f, 16f);
        if (VRArena.Instance) VRArena.Instance.Burst(at, Color.white, 30);
        foreach (var f in VRFighter.All)
        {
            if (!f || !f.Alive || (f.team == team && f != thrower)) continue;
            Vector3 eye = f.EyePoint;
            float d = Vector3.Distance(eye, at);
            if (d > FlashRadius || VRArena.LineBlocked(at, eye)) continue;
            Vector3 looking = f is VRPlayer p && p.Cam ? p.Cam.transform.forward : f.transform.forward;
            float facing = Mathf.Clamp01((Vector3.Dot(looking, (at - eye).normalized) + 0.3f) / 1.3f);
            float seconds = 3.4f * (0.25f + 0.75f * facing) * (1f - 0.55f * d / FlashRadius);
            if (seconds > 0.35f) f.Blind(seconds);
        }
    }

    /// <summary>A bright ball and a light that grow and fade.</summary>
    static void Blast(Vector3 at, Color c, float size, float range)
    {
        var ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Destroy(ball.GetComponent<Collider>());
        ball.name = "Blast";
        ball.transform.position = at;
        ball.GetComponent<Renderer>().sharedMaterial = VRArena.Glow(c, c, 6f);
        ball.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        var light = ball.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = c;
        light.range = range;
        light.shadows = LightShadows.None;
        ball.AddComponent<BlastFade>().Run(size, light);
    }

    class BlastFade : MonoBehaviour
    {
        public void Run(float size, Light light) => StartCoroutine(Go(size, light));

        IEnumerator Go(float size, Light light)
        {
            for (float t = 0f; t < 0.3f; t += Time.deltaTime)
            {
                float k = t / 0.3f;
                transform.localScale = Vector3.one * size * Mathf.Lerp(0.3f, 1f, Mathf.Sqrt(k)) * (1f - k * 0.6f);
                light.intensity = Mathf.Lerp(9f, 0f, k);
                yield return null;
            }
            Destroy(gameObject);
        }
    }
}
