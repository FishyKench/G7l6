using System.Collections;
using UnityEngine;

/// <summary>
/// How much water is in the bucket (0 = empty, 1 = full). The water you see inside rises with it.
/// A full bucket can't catch any more - empty it in a sink first (look at a sink holding it and press E).
/// </summary>
public class BucketWater : MonoBehaviour
{
    [Range(0f, 1f)] public float fill;
    public bool Full => fill >= 0.98f;
    public bool HasWater => fill > 0.02f;
    public bool Pouring { get; private set; }

    // The bucket model (PropsBuilder): water disc at 0.248 m, inner wall from r 0.15 at 0.08 m to r 0.177 at 0.37 m.
    const float DiscY = 0.248f, DiscR = 0.172f, BottomY = 0.085f, TopY = 0.345f;
    Transform water;
    Vector3 waterBase;

    public static BucketWater On(Carryable bucket)
    {
        if (!bucket) return null;
        var w = bucket.GetComponent<BucketWater>();
        return w ? w : bucket.gameObject.AddComponent<BucketWater>();
    }

    void Awake()
    {
        foreach (var t in GetComponentsInChildren<Transform>(true))
            if (t.name == "Water" && t.parent && t.parent.name == "Model") { water = t; break; }
        if (water) waterBase = water.localPosition;
        Show();
    }

    public void SetFill(float value)
    {
        fill = Mathf.Clamp01(value);
        Show();
    }

    void Show()
    {
        if (!water) return;
        water.gameObject.SetActive(HasWater);
        float y = Mathf.Lerp(BottomY, TopY, fill);
        float r = Mathf.Lerp(0.15f, 0.177f, Mathf.InverseLerp(0.08f, 0.37f, y)) - 0.004f;
        water.localPosition = waterBase + Vector3.up * (y - DiscY);
        water.localScale = new Vector3(r / DiscR, 1f, r / DiscR);
    }

    /// <summary>Tip the held bucket out into a sink.</summary>
    public void PourInto(Vector3 sink)
    {
        if (!Pouring && HasWater) StartCoroutine(Pour(sink));
    }

    IEnumerator Pour(Vector3 sink)
    {
        Pouring = true;
        float start = fill, time = 0.4f + 0.9f * start;
        Fx.Play(Fx.Swish, sink, 0.7f, 0.55f);
        float nextSplash = 0f;
        for (float t = 0f; t < time; t += Time.deltaTime)
        {
            var motion = HeldItemMotion.Get();
            if (motion) motion.Pose(new Vector3(0f, 0.06f, 0.12f), new Vector3(55f, 0f, 0f), 0.12f); // tip it forward
            if (t > 0.25f) SetFill(Mathf.Lerp(start, 0f, (t - 0.25f) / (time - 0.25f)));
            if (t > 0.25f && t >= nextSplash) { Fx.Splash(sink, 6); nextSplash = t + 0.15f; }
            yield return null;
        }
        SetFill(0f);
        Pouring = false;
        GameHUD.Toast("Bucket's empty.", GameHUD.Good);
    }
}

/// <summary>A sink you can empty the bucket into (added at runtime to every sink in the house).</summary>
public class BucketSink : MonoBehaviour, IInteractable, IPromptProvider
{
    public static BucketWater HeldBucket
    {
        get
        {
            var carry = PlayerCarry.Instance;
            if (!carry || !carry.IsHolding(ItemType.Bucket)) return null;
            return BucketWater.On(carry.Held);
        }
    }

    public string Prompt
    {
        get
        {
            var b = HeldBucket;
            return b && b.HasWater && !b.Pouring ? "Empty the bucket in the sink" : null;
        }
    }

    public void Interact() => Empty(transform);

    public static bool Empty(Transform sink)
    {
        var b = HeldBucket;
        if (!b || !b.HasWater || b.Pouring) return false;
        Vector3 at = sink.position;
        bool found = false;
        Bounds bounds = default;
        foreach (var r in sink.GetComponentsInChildren<MeshRenderer>())
        {
            if (!found) { bounds = r.bounds; found = true; } else bounds.Encapsulate(r.bounds);
        }
        if (found) at = new Vector3(bounds.center.x, bounds.max.y - 0.08f, bounds.center.z);
        b.PourInto(at);
        return true;
    }

    /// <summary>Puts a BucketSink on every sink in the scene (the leaking one handles it itself).</summary>
    public static void AddToSinks()
    {
        foreach (var t in Object.FindObjectsOfType<Transform>())
        {
            string n = t.name;
            if (!(n == "Sink" || n.StartsWith("Sink ") || n.StartsWith("Sink(") || n.Contains("- Sink"))) continue;
            if (t.GetComponentInParent<BucketSink>() || t.GetComponentInChildren<LeakTap>() || t.GetComponentInParent<LeakTap>()) continue;
            if (!t.GetComponentInChildren<Collider>()) continue;
            t.gameObject.AddComponent<BucketSink>();
        }
    }
}
