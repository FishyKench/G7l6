using UnityEngine;

/// <summary>Fire (e.g. from a forgotten oven). Put it out with the fire extinguisher.</summary>
public class FireHazard : HazardTask
{
    [Header("Fire")]
    public ParticleSystem flames;
    public Light fireLight;
    public float lightIntensity = 3f;

    float baseEmission = -1f;

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

    protected override void OnHazardStarted()
    {
        if (flames)
        {
            var emission = flames.emission;
            if (baseEmission < 0f) baseEmission = emission.rateOverTimeMultiplier;
            emission.rateOverTimeMultiplier = baseEmission;
            flames.Play();
        }
        if (fireLight) fireLight.enabled = true;
    }

    protected override void OnHazardTick()
    {
        if (fireLight)
            fireLight.intensity = lightIntensity * (0.75f + 0.5f * Mathf.PerlinNoise(Time.time * 8f, 0.3f));
    }

    protected override void OnClearing(float t)
    {
        if (flames)
        {
            var emission = flames.emission;
            emission.rateOverTimeMultiplier = baseEmission * (1f - t);
        }
        if (fireLight) fireLight.intensity = lightIntensity * (1f - t);
    }

    protected override void OnHazardEnded()
    {
        if (flames) flames.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        if (fireLight) fireLight.enabled = false;
    }
}
