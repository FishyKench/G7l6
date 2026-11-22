using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// "Unstuck me" (pause menu): moves the player to the closest spot of open floor - the floor under you, on your own storey, with room
/// for your whole body around it (a little to spare, so you don't land in another tight gap), preferring spots you can see from where
/// you are (so it doesn't put you on the other side of a wall). Searches in rings out to 4 m.
/// </summary>
public static class Unstuck
{
    const float MaxRadius = 4f, Step = 0.25f;

    /// <summary>Moves the player; returns what happened, for a toast.</summary>
    public static string Rescue()
    {
        var mover = Object.FindObjectOfType<PlayerMovementAdvanced>();
        if (!mover) return null;
        if (PlayerInteraction.Instance && PlayerInteraction.Instance.InMainTask) return "Finish what you're doing first.";
        var t = mover.transform;
        var rb = mover.GetComponent<Rigidbody>();
        var col = mover.GetComponentInChildren<CapsuleCollider>() as Collider ?? mover.GetComponentInChildren<Collider>();
        Bounds body = col ? col.bounds : new Bounds(t.position, new Vector3(1f, mover.playerHeight, 1f));
        float radius = Mathf.Max(0.2f, Mathf.Min(body.extents.x, body.extents.z));
        float height = Mathf.Max(radius * 2f + 0.1f, body.size.y);
        float feetY = body.min.y;
        float pivotAboveFeet = t.position.y - feetY;
        int groundMask = mover.whatIsGround.value != 0 ? mover.whatIsGround.value : Physics.DefaultRaycastLayers;
        Vector3 here = new Vector3(t.position.x, feetY, t.position.z);
        Vector3 chest = here + Vector3.up * height * 0.7f;

        bool Mine(Collider c) =>
            c.isTrigger || (rb && c.attachedRigidbody == rb) || c.transform.IsChildOf(t)
            || (PlayerCarry.Instance && PlayerCarry.Instance.Held && c.transform.IsChildOf(PlayerCarry.Instance.Held.transform));

        bool Fits(Vector3 feet, float r)
        {
            var a = feet + Vector3.up * (r + 0.05f);
            var b = feet + Vector3.up * Mathf.Max(r + 0.05f, height - r);
            foreach (var c in Physics.OverlapCapsule(a, b, r, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                if (!Mine(c)) return false;
            return true;
        }

        bool Seen(Vector3 feet)
        {
            var to = feet + Vector3.up * height * 0.7f;
            foreach (var h in Physics.RaycastAll(chest, to - chest, Vector3.Distance(chest, to), Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                if (!Mine(h.collider)) return false;
            return true;
        }

        // The floor under a point: the highest walkable surface no higher than your feet (+ a step), on this storey.
        bool Floor(Vector3 at, out Vector3 feet)
        {
            feet = default;
            float best = float.MinValue;
            var from = new Vector3(at.x, feetY + 0.5f, at.z);
            foreach (var h in Physics.RaycastAll(from, Vector3.down, 3.2f, groundMask, QueryTriggerInteraction.Ignore))
            {
                if (Mine(h.collider) || h.normal.y < 0.7f) continue;
                if (h.point.y > best) { best = h.point.y; feet = h.point; }
            }
            return best > float.MinValue;
        }

        Vector3? open = null, openHidden = null, tight = null;
        for (float r = 0f; r <= MaxRadius && open == null; r += Step)
        {
            int n = r <= 0f ? 1 : Mathf.Max(8, Mathf.RoundToInt(2f * Mathf.PI * r / Step));
            for (int i = 0; i < n; i++)
            {
                float a = i * Mathf.PI * 2f / n;
                var p = here + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * r;
                if (!Floor(p, out var feet)) continue;
                feet += Vector3.up * 0.02f;
                if (Fits(feet, radius * 1.35f))
                {
                    if (Seen(feet)) { open = feet; break; }
                    if (openHidden == null) openHidden = feet;
                }
                else if (tight == null && Fits(feet, radius)) tight = feet;
            }
        }
        var spot = open ?? openHidden ?? tight;
        if (spot == null) return "Couldn't find open floor nearby - try Restart day.";

        Vector3 pos = spot.Value + Vector3.up * pivotAboveFeet;
        if (rb)
        {
            rb.velocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.position = pos;
        }
        t.position = pos;
        Physics.SyncTransforms();
        return Vector3.Distance(here, spot.Value) < 0.1f ? "You're on open floor already." : "Unstuck!";
    }
}
