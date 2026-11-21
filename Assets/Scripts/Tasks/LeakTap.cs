using UnityEngine;

/// <summary>The tap upstairs that's left running during a ceiling leak. Press E to turn it off.</summary>
public class LeakTap : MonoBehaviour, IInteractable, IPromptProvider
{
    public CeilingLeakTask leak;
    [Tooltip("Water stream shown while the tap is running.")]
    public ParticleSystem runningWater;

    public string Prompt
    {
        get
        {
            if (leak && leak.IsActive) return "Turn off the tap";
            var b = BucketSink.HeldBucket;
            return b && b.HasWater && !b.Pouring ? "Empty the bucket in the sink" : null;
        }
    }

    /// <summary>Where the water comes from (middle of the sink). The leak drips straight down from here.</summary>
    public Vector3 WaterOrigin
    {
        get
        {
            bool found = false;
            Bounds b = default;
            foreach (var r in GetComponentsInChildren<MeshRenderer>())
            {
                if (!found) { b = r.bounds; found = true; }
                else b.Encapsulate(r.bounds);
            }
            return found ? b.center : transform.position;
        }
    }

    void Start() => SetRunning(false);

    public void Interact()
    {
        if (leak && leak.IsActive) { leak.TurnOffTap(); return; }
        BucketSink.Empty(transform);
    }

    public void SetRunning(bool on)
    {
        if (!runningWater) return;
        if (on) runningWater.Play();
        else runningWater.Stop(true, ParticleSystemStopBehavior.StopEmitting);
    }
}
