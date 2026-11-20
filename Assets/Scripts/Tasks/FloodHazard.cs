using UnityEngine;

/// <summary>Water on the floor (e.g. from an overflowing washing machine). Spreads out, mop it up.</summary>
public class FloodHazard : HazardTask
{
    [Header("Flood")]
    [Tooltip("The puddle object. It's scaled on X/Z to spread out.")]
    public Transform puddle;
    public float startDiameter = 0.4f;
    public float maxDiameter = 3f;
    [Tooltip("Seconds for the puddle to reach full size.")]
    public float spreadTime = 10f;

    float size;

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

    protected override void OnHazardStarted()
    {
        size = startDiameter;
        Apply(size);
    }

    protected override void OnHazardTick()
    {
        size = Mathf.MoveTowards(size, maxDiameter, (maxDiameter - startDiameter) / Mathf.Max(0.1f, spreadTime) * Time.deltaTime);
        Apply(size);
    }

    protected override void OnClearing(float t) => Apply(Mathf.Lerp(size, 0.05f, t));

    void Apply(float diameter)
    {
        if (!puddle) return;
        var s = puddle.localScale;
        puddle.localScale = new Vector3(diameter, s.y, diameter);
    }
}
