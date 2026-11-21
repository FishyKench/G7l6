using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Anyone who fights in the VR arena: you (VRPlayer) and the bots (VRBot). Team 0 = your team, 1 = the enemy.
/// </summary>
public abstract class VRFighter : MonoBehaviour
{
    public static readonly List<VRFighter> All = new List<VRFighter>();

    public int team;
    public string fighterName = "Player";
    public float maxHealth = 100f;

    public float Health { get; protected set; } = 100f;
    public bool Alive => Health > 0f;

    /// <summary>Where shots aim (chest).</summary>
    public virtual Vector3 AimPoint => transform.position + Vector3.up * 1.15f;
    /// <summary>Where this fighter looks/shoots from.</summary>
    public virtual Vector3 EyePoint => transform.position + Vector3.up * 1.6f;

    protected virtual void OnEnable() { if (!All.Contains(this)) All.Add(this); }
    protected virtual void OnDisable() => All.Remove(this);

    public virtual void Respawn(Vector3 position, Quaternion rotation)
    {
        Health = maxHealth;
        transform.SetPositionAndRotation(position, rotation);
    }

    /// <summary>False while protected (e.g. just after respawning).</summary>
    protected virtual bool CanBeHurt => true;

    public void TakeDamage(float amount, VRFighter from, bool headshot)
    {
        if (!Alive || amount <= 0f || !CanBeHurt) return;
        Health = Mathf.Max(0f, Health - amount);
        OnHurt(amount, from, headshot);
        if (!Alive)
        {
            OnDied(from);
            if (VRArena.Instance) VRArena.Instance.OnKill(from, this, headshot);
        }
    }

    protected virtual void OnHurt(float amount, VRFighter from, bool headshot) { }
    protected virtual void OnDied(VRFighter killer) { }

    /// <summary>Clear line of fire from this fighter's eyes to a point (nothing solid in between except the target).</summary>
    public bool CanSee(VRFighter other)
    {
        Vector3 from = EyePoint, to = other.AimPoint;
        Vector3 d = to - from;
        foreach (var hit in Physics.RaycastAll(from, d.normalized, d.magnitude, ~0, QueryTriggerInteraction.Ignore))
        {
            var f = hit.collider.GetComponentInParent<VRFighter>();
            if (f == this || f == other) continue;
            if (f != null) continue; // other fighters don't block sight (they block bullets, though)
            return false;
        }
        return true;
    }
}
