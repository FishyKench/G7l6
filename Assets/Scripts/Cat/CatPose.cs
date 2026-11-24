using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Lying down like a real cat, on top of whatever the animation is doing. The model only comes with idle / walk / run, so
/// this bends the bones itself: the body sinks until the belly is on the floor (the front a touch higher, sphinx-like), the
/// back legs fold up under the hips, the front legs tuck in with just the paws peeking out under the chin, and the tail
/// curls round her side. Asleep, the head goes down too: chin tucked, turned in towards the tail.
/// Whoever drives the cat sets <see cref="lie"/> and <see cref="sleep"/> (0..1); the pose eases in and out by itself.
/// Runs after the cat's own scripts, and only touches bones they don't (not Root, not the head-look bone).
/// </summary>
[DefaultExecutionOrder(200)]
public class CatPose : MonoBehaviour
{
    [Range(0f, 1f)] public float lie;
    [Range(0f, 1f)] public float sleep;
    public int tailSide = 1;            // which way the tail curls (+1 / -1)

    /// <summary>How far down she is right now (0 standing .. 1 lying).</summary>
    public float Weight => w;

    float w, z, restHip = -1f;
    Transform hip, neck, head, jaw;
    Transform[] hindL, hindR, frontL, frontR, tail;
    readonly Dictionary<Transform, (Vector3 p, Quaternion r)> baseOf = new Dictionary<Transform, (Vector3, Quaternion)>();
    readonly Dictionary<Transform, (Vector3 p, Quaternion r)> wrote = new Dictionary<Transform, (Vector3, Quaternion)>();
    readonly List<Transform> touched = new List<Transform>();

    void Awake()
    {
        var all = new Dictionary<string, Transform>();
        foreach (var t in GetComponentsInChildren<Transform>(true)) if (!all.ContainsKey(t.name)) all[t.name] = t;
        Transform B(string n) => all.TryGetValue(n, out var t) ? t : null;
        Transform[] Chain(params string[] names) { var c = new List<Transform>(); foreach (var n in names) { var t = B(n); if (!t) return null; c.Add(t); } return c.ToArray(); }
        hip = B("spine.005");
        neck = B("spine.007");
        head = B("scull");
        jaw = B("jaw");
        hindL = Chain("thigh.L", "shin.L", "foot.L", "toe.L", "toe.L_end");
        hindR = Chain("thigh.R", "shin.R", "foot.R", "toe.R", "toe.R_end");
        frontL = Chain("front_thigh.L", "front_shin.L", "front_foot.L", "front_foot.L_end");
        frontR = Chain("front_thigh.R", "front_shin.R", "front_foot.R", "front_foot.R_end");
        tail = Chain("spine.004", "spine.003", "spine.002", "spine.001", "spine", "spine_end");
        foreach (var c in new[] { hindL, hindR, frontL, frontR, tail })
            if (c != null) for (int i = 0; i < c.Length - 1; i++) touched.Add(c[i]);
        foreach (var t in new[] { hip, neck, head }) if (t) touched.Add(t);
        tailSide = Random.value < 0.5f ? -1 : 1;
    }

    void LateUpdate()
    {
        if (!hip) return;
        // put back last frame's bend if the animation didn't overwrite it (off screen it may not run)
        foreach (var t in touched)
            if (wrote.TryGetValue(t, out var was) && t.localPosition == was.p && t.localRotation == was.r && baseOf.TryGetValue(t, out var b))
            { t.localPosition = b.p; t.localRotation = b.r; }
        wrote.Clear();

        float hipNow = transform.InverseTransformPoint(hip.position).y;
        if (w < 0.01f) restHip = restHip < 0f ? hipNow : Mathf.Lerp(restHip, hipNow, 0.05f); // (standing height, from the animation)

        float dt = Time.deltaTime;
        w = Mathf.MoveTowards(w, lie, dt / (lie > w ? 0.8f : 0.45f));
        z = Mathf.MoveTowards(z, sleep * lie, dt / (sleep * lie > z ? 1.4f : 0.35f));
        if (w <= 0.001f || restHip <= 0f) return;

        foreach (var t in touched) baseOf[t] = (t.localPosition, t.localRotation);
        float e = Mathf.SmoothStep(0f, 1f, w), zz = Mathf.SmoothStep(0f, 1f, z);
        Vector3 up = transform.up, right = transform.right;

        // 1. down: belly on the floor, the front end lifted a little (and slow breathing when asleep)
        float breathe = Mathf.Sin(Time.time * 1.5f) * 0.012f * restHip * zz;
        float drop = (hipNow - restHip * 0.45f + breathe) * e;
        hip.position -= transform.TransformVector(new Vector3(0f, drop, 0f));
        hip.rotation = Quaternion.AngleAxis(-9f * e * (1f - zz * 0.6f), right) * hip.rotation;

        // 2. the head (first: the front legs hang off the chest bone it moves) - turned only round the cat's own sideways and
        //    upright axes, so it can never twist: awake, raised and alert; asleep, lowered onto the paws and turned in a little
        if (neck && head)
        {
            neck.rotation = Quaternion.AngleAxis(-18f * e * (1f - zz) + 30f * zz, right) * neck.rotation;
            head.rotation = Quaternion.AngleAxis(tailSide * 28f * zz, up) * Quaternion.AngleAxis(12f * zz, right) * head.rotation;
        }

        // 3. the back legs fold up under the hips: thigh forward and down, shin back along the floor, paw flat forward
        Fold(hindL, e, new[] { new Vector2(0.75f, -0.6f), new Vector2(-0.95f, -0.3f), new Vector2(1f, -0.08f), new Vector2(1f, -0.1f) });
        Fold(hindR, e, new[] { new Vector2(0.75f, -0.6f), new Vector2(-0.95f, -0.3f), new Vector2(1f, -0.08f), new Vector2(1f, -0.1f) });
        // 4. the front legs tuck in: upper arm back under the chest, forearm and paw forward along the floor
        Fold(frontL, e, new[] { new Vector2(-0.9f, -0.45f), new Vector2(1f, -0.06f), new Vector2(1f, -0.05f) });
        Fold(frontR, e, new[] { new Vector2(-0.9f, -0.45f), new Vector2(1f, -0.06f), new Vector2(1f, -0.05f) });

        // 5. the tail: down to the floor and straight back, then curled round her side a bit more at every joint
        if (tail != null)
            for (int i = 0; i < tail.Length - 1; i++)
            {
                Hinge(tail[i], tail[i + 1], new Vector2(-1f, i == 0 ? -0.5f : -0.04f), e);
                float curl = tailSide * (i == 0 ? 10f : 24f + 6f * zz);
                if (i == tail.Length - 2) curl += Mathf.Sin(Time.time * 2.2f) * 10f * (1f - zz); // (a lazy flick at the tip, awake)
                tail[i].rotation = Quaternion.AngleAxis(curl * e, up) * tail[i].rotation;
            }

        foreach (var t in touched) wrote[t] = (t.localPosition, t.localRotation);
    }

    /// <summary>Folds a leg joint by joint, each as a hinge round the cat's sideways axis (directions are (forward, up) in the
    /// cat's frame). The legs keep their own spread and never twist.</summary>
    void Fold(Transform[] chain, float weight, Vector2[] dirs)
    {
        if (chain == null) return;
        for (int i = 0; i < chain.Length - 1 && i < dirs.Length; i++) Hinge(chain[i], chain[i + 1], dirs[i], weight);
    }

    /// <summary>Turns a bone round the cat's sideways axis only, until it points along (forward, up) in the cat's frame.</summary>
    void Hinge(Transform bone, Transform child, Vector2 fwdUp, float weight)
    {
        if (weight <= 0f) return;
        Vector3 right = transform.right;
        Vector3 now = Vector3.ProjectOnPlane(child.position - bone.position, right);
        if (now.sqrMagnitude < 1e-10f) return;
        Vector3 want = transform.forward * fwdUp.x + transform.up * fwdUp.y;
        float angle = Vector3.SignedAngle(now, want, right);
        bone.rotation = Quaternion.AngleAxis(angle * weight, right) * bone.rotation;
    }
}
