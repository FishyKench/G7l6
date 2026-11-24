using System.Collections;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// The tutorial's little show (see <see cref="TutorialDay"/>): Kitty, up on the furniture behind a glass of water, spots it, creeps
/// up low, wiggles her bum and paws it - tap (it wobbles)... a look at you... tap (to the very edge: it teeters)... and the swipe.
/// Her front paw really reaches the glass: the leg is aimed at it on top of the animation (a one-bone reach, <see cref="WorldReach"/>),
/// and her head follows what she's looking at (<see cref="LookAtFor"/>).
/// </summary>
public partial class CatBrain
{
    // ---- where she's looking (overrides watching the player)
    Vector3 focusPoint;
    float focusUntil;

    /// <summary>Her head turns to this point for a while.</summary>
    void LookAtFor(Vector3 point, float seconds) { focusPoint = point; focusUntil = Time.time + seconds; }
    bool Focused => Time.time < focusUntil;

    // ---- the reach: a front leg aimed at a point (0 = the animation as it is, 1 = straight at it)
    Vector3 reachPoint;
    float reachWeight;
    bool reachRight = true;
    Transform tipL, tipR;

    // ---- the show runs on its own: held still till it's told to go, and nothing else cuts in
    bool inShow, showHold;

    /// <summary>(The tutorial, once the picture's up) she comes to life: the show begins.</summary>
    public void StartShow() => showHold = false;

    /// <summary>
    /// The tutorial's show: puts her up on the furniture a few steps behind this drink (on the far side from where it'll fall)
    /// and starts it. 'push' (flat) is the way it goes off; default: towards the nearest bit of floor. False if she can't
    /// (no room on top behind it, or nowhere for it to land).
    /// </summary>
    public bool KnockOverForShow(Mug mug, Vector3 push = default, float room = 10f)
    {
        if (!ready || !mug || !agent) return false;
        Vector3 m = mug.transform.position;
        if (!TopSpot(m, out var top)) return false;
        if (Flat(push).sqrMagnitude < 0.01f)
        {
            if (!FindFloorSpot(m, 1.6f, false, out var floor)) return false;
            push = floor - m;
        }
        push = Flat(push).normalized;
        if (!EdgeAlong(m, push, out var edge, out var land)) return false;
        float s = Size;
        bool OnTop(Vector3 p, out Vector3 at)
        {
            at = p;
            if (!NavMesh.SamplePosition(p, out var h, 0.15f * s, NavMesh.AllAreas) || Mathf.Abs(h.position.y - top.y) > 0.08f) return false;
            at = h.position;
            return true;
        }
        if (!OnTop(m - push * 0.3f * s, out _)) return false;
        showHold = true;
        Vector3 start = default;
        bool ok = false;
        foreach (float d in new[] { 0.85f, 0.7f, 0.55f, 0.45f })
            if (d * s <= room + 0.01f && OnTop(m - push * d * s, out start)) { ok = true; break; }
        if (!ok) return false;
        React(ShowKnock(mug, push, edge, land, start));
        return true;
    }

    /// <summary>Where the furniture top ends going this way from 'from' (at its height), and the floor just past it.</summary>
    bool EdgeAlong(Vector3 from, Vector3 dir, out Vector3 edge, out Vector3 land)
    {
        float s = Size;
        edge = land = from;
        for (float r = 0f; r < 1.6f * s; r += 0.02f * s)
        {
            Vector3 p = from + dir * r;
            bool onTop = false;
            foreach (var hit in Physics.RaycastAll(p + Vector3.up * 0.2f * s, Vector3.down, 0.4f * s, ~0, QueryTriggerInteraction.Ignore))
                if (!hit.collider.transform.IsChildOf(transform) && Mathf.Abs(hit.point.y - from.y) < 0.05f * s) { onTop = true; break; }
            if (onTop) continue;
            edge = from + dir * Mathf.Max(0f, r - 0.02f * s);
            Vector3 past = from + dir * (r + 0.22f * s);
            if (!Physics.Raycast(past + Vector3.up * 0.1f * s, Vector3.down, out var floor, 3f * s, ~0, QueryTriggerInteraction.Ignore)
                || floor.normal.y < 0.7f || floor.point.y > from.y - 0.25f * s) return false;
            land = floor.point;
            return true;
        }
        return false;
    }

    IEnumerator ShowKnock(Mug mug, Vector3 push, Vector3 edge, Vector3 land, Vector3 start)
    {
        float s = Size;
        inShow = true;
        mugTarget = mug;
        if (agent.enabled) { agent.Warp(start); agent.velocity = Vector3.zero; }
        transform.rotation = Quaternion.LookRotation(push);
        mood = Mood.Idle;
        Halt();
        if (animator) { animator.SetFloat(VertParam, 0f); animator.SetFloat(StateParam, 0f); } // (standing, not mid-stride)
        LookAtFor(mug.transform.position, 99f);
        // still as a statue till the picture's up
        float holdUntil = Time.time + 8f;
        while (showHold && Time.time < holdUntil)
        {
            Halt();
            if (agent.enabled) agent.velocity = Vector3.zero;
            yield return null;
        }
        yield return new WaitForSeconds(0.6f); // (she's spotted it)
        if (!mug || mug.Falling) { Done(); yield break; }

        // creep up to it, low
        mood = Mood.Stalking;
        crouchTarget = 0.45f;
        yield return null;
        yield return WalkTo(ReachSpot(mug, push), walkSpeed * 0.38f, 0.01f, 5f);
        if (!mug || mug.Falling) { Done(); yield break; }
        yield return Face(transform.position + push, 0.25f);
        if (!mug || mug.Falling) { Done(); yield break; }
        yield return Slide(ReachSpot(mug, push), 0.25f); // (just so: her paw's in reach)

        // the bum wiggle
        const float wiggleTime = 0.9f;
        for (float t = 0f; t < wiggleTime; t += Time.deltaTime)
        {
            float build = Mathf.SmoothStep(0f, 1f, t / 0.3f) * Mathf.SmoothStep(1f, 0f, (t - wiggleTime + 0.25f) / 0.25f);
            wiggle = Mathf.Sin(t * 11f) * 6f * build;
            yield return null;
        }
        wiggle = 0f;
        if (!mug || mug.Falling) { Done(); yield break; }

        // tap: it wobbles
        crouchTarget = 0.3f;
        yield return Tap(mug, push, 0.04f * s, 8f, false);
        if (!mug || mug.Falling) { Done(); yield break; }
        yield return new WaitForSeconds(0.45f);

        // a look at you (cats know)
        if (Camera.main) LookAtFor(Camera.main.transform.position, 1.0f);
        crouchTarget = 0.15f;
        yield return new WaitForSeconds(1.1f);
        if (!mug || mug.Falling) { Done(); yield break; }
        LookAtFor(mug.transform.position, 99f);
        crouchTarget = 0.35f;
        yield return Slide(ReachSpot(mug, push), 0.3f); // (a step after it)
        yield return new WaitForSeconds(0.2f);
        if (!mug || mug.Falling) { Done(); yield break; }

        // tap: right to the edge - it teeters
        float toEdge = Vector3.Dot(edge - mug.transform.position, push);
        yield return Tap(mug, push, Mathf.Max(0.03f * s, toEdge - 0.01f * s), 15f, false);
        if (!mug || mug.Falling) { Done(); yield break; }
        yield return new WaitForSeconds(0.75f);

        // ...and the swipe
        mood = Mood.Playing;
        crouchTarget = 0.1f;
        if (!mug || mug.Falling) { Done(); yield break; }
        yield return Slide(ReachSpot(mug, push), 0.2f);
        if (!mug || mug.Falling) { Done(); yield break; }
        yield return Tap(mug, push, 0f, 0f, true, land);
        mugTarget = null;

        // over the edge she peers, then down she hops - straight through the puddle (wet paws!) and off
        LookAtFor(land, 1.2f);
        crouchTarget = 0.55f;
        yield return new WaitForSeconds(1.0f);
        crouchTarget = 0f;
        focusUntil = 0f;
        mood = Mood.Walking;
        yield return WalkTo(land, walkSpeed, 0.05f, 6f);
        wetSteps = 8; // (she's stood right in it: a short trail)
        lastPrint = transform.position;
        yield return RunFrom(land, 4f);
        inShow = false;
        yield return Idle(2f);

        void Done() { inShow = false; showHold = false; mugTarget = null; crouchTarget = 0f; wiggle = 0f; reachWeight = 0f; focusUntil = 0f; }
    }

    /// <summary>Where to stand so her right front paw just reaches the near side of the glass (measured off her own legs).</summary>
    Vector3 ReachSpot(Mug mug, Vector3 push)
    {
        float s = Size;
        Vector3 right = Vector3.Cross(Vector3.up, push);
        Vector3 contact = Contact(mug, push);
        Vector3 root = contact - push * 0.3f * s;
        if (FindPawBones() && pawR && tipR)
        {
            // her shoulder, in her own space (forward / right / up), and how long the leg is
            Vector3 sh = Quaternion.Inverse(transform.rotation) * (pawR.position - transform.position);
            float leg = Vector3.Distance(pawR.position, tipR.position);
            float dv = sh.y - (contact.y - mug.transform.position.y);
            float d = Mathf.Sqrt(Mathf.Max(0.0004f, 0.92f * leg * 0.92f * leg - dv * dv));
            root = contact - push * (d + sh.z) - right * sh.x;
        }
        root.y = mug.transform.position.y;
        if (NavMesh.SamplePosition(root, out var h, 0.12f * s, NavMesh.AllAreas) && Mathf.Abs(h.position.y - root.y) < 0.08f) root = h.position;
        return root;
    }

    static Vector3 Contact(Mug mug, Vector3 push) => mug.transform.position + Vector3.up * 0.06f * mug.Size - push * 0.035f * mug.Size;

    /// <summary>A short, smooth shuffle along the walk map (small corrections, no walking animation needed).</summary>
    IEnumerator Slide(Vector3 to, float seconds)
    {
        Vector3 from = transform.position;
        if ((to - from).sqrMagnitude < 0.0001f) yield break;
        for (float t = 0f; t < seconds; t += Time.deltaTime)
        {
            Vector3 p = Vector3.Lerp(from, to, Mathf.SmoothStep(0f, 1f, t / seconds));
            if (agent.enabled) agent.Warp(p); else transform.position = p;
            yield return null;
        }
        if (agent.enabled) agent.Warp(to); else transform.position = to;
    }

    /// <summary>
    /// One go with the right front paw: up and back, then out onto the glass (it slides 'slide' and wobbles 'wobble' degrees),
    /// a moment on it, and back down. 'swipe': fast and right through - it goes over, to 'land'.
    /// </summary>
    IEnumerator Tap(Mug mug, Vector3 push, float slide, float wobble, bool swipe, Vector3 land = default)
    {
        if (!FindPawBones() || !pawR || !tipR) { if (swipe && mug) StartCoroutine(mug.Fall(land)); yield break; }
        float s = Size;
        reachRight = true;
        float lift = swipe ? 0.16f : 0.24f, strike = swipe ? 0.07f : 0.11f, hold = swipe ? 0.12f : 0.16f, back = 0.3f;
        // up and cocked back
        for (float t = 0f; t < lift; t += Time.deltaTime)
        {
            float k = Mathf.SmoothStep(0f, 1f, t / lift);
            if (!mug) { reachWeight = 0f; yield break; }
            reachPoint = Contact(mug, push) + Vector3.up * (swipe ? 0.11f : 0.08f) * s - push * 0.05f * s;
            reachWeight = k;
            yield return null;
        }
        // out onto the glass
        Vector3 cocked = reachPoint;
        for (float t = 0f; t < strike; t += Time.deltaTime)
        {
            float k = t / strike;
            k = k * k; // (accelerating)
            if (!mug) { reachWeight = 0f; yield break; }
            reachPoint = Vector3.Lerp(cocked, Contact(mug, push) + push * (swipe ? 0.04f : 0.005f) * s, k);
            reachWeight = 1f;
            yield return null;
        }
        Say(swipe ? "swat!" : "tap", swipe ? HudArt.MarkerRed : HudArt.Ink, swipe ? 1.2f : 1f);
        if (swipe) { if (mug && !mug.Falling) StartCoroutine(mug.Fall(land)); }
        else if (mug) StartCoroutine(Nudge(mug, push, slide, wobble));
        // on it a moment (following it), or through it
        Vector3 hit = reachPoint;
        for (float t = 0f; t < hold; t += Time.deltaTime)
        {
            reachPoint = swipe ? hit + push * 0.1f * s * Mathf.SmoothStep(0f, 1f, t / hold) + Vector3.down * 0.02f * s
                               : (mug ? Contact(mug, push) : hit);
            yield return null;
        }
        // back down
        for (float t = 0f; t < back; t += Time.deltaTime)
        {
            reachWeight = 1f - Mathf.SmoothStep(0f, 1f, t / back);
            yield return null;
        }
        reachWeight = 0f;
    }

    /// <summary>The glass slides along and wobbles on its base (a damped rock, the top towards 'push').</summary>
    static IEnumerator Nudge(Mug mug, Vector3 push, float slide, float wobble)
    {
        Vector3 from = mug.transform.position, to = from + push * slide;
        Quaternion baseRot = mug.transform.rotation;
        Vector3 axis = Vector3.Cross(Vector3.up, push);
        float dur = 1.2f;
        for (float t = 0f; t < dur && mug && !mug.Falling; t += Time.deltaTime)
        {
            float slideK = 1f - Mathf.Pow(1f - Mathf.Clamp01(t / 0.22f), 3f); // (ease out)
            mug.transform.position = Vector3.Lerp(from, to, slideK);
            float rock = wobble * Mathf.Exp(-t * 4f) * Mathf.Sin(t * 22f);
            mug.transform.rotation = Quaternion.AngleAxis(rock, axis) * baseRot;
            yield return null;
        }
        if (mug && !mug.Falling) { mug.transform.position = to; mug.transform.rotation = baseRot; }
    }

    bool FindPawBones()
    {
        if (!pawBones) { pawL = FindBone("front_thigh.L"); pawR = FindBone("front_thigh.R"); pawBones = true; }
        if (pawR && !tipR) tipR = Tip(pawR);
        if (pawL && !tipL) tipL = Tip(pawL);
        return pawR || pawL;
    }

    /// <summary>The end of a leg: down its first children to the last bone.</summary>
    static Transform Tip(Transform bone)
    {
        var t = bone;
        for (int i = 0; i < 8 && t.childCount > 0; i++) t = t.GetChild(0);
        return t == bone ? null : t;
    }

    /// <summary>(LateUpdate, after the body's squash) the leg aimed at the reach point - only the shoulder turns, the leg stays as animated.</summary>
    void WorldReach()
    {
        if (reachWeight <= 0.001f) return;
        var paw = reachRight ? pawR : pawL;
        var tip = reachRight ? tipR : tipL;
        if (!paw || !tip) return;
        Vector3 a = tip.position - paw.position, b = reachPoint - paw.position;
        if (a.sqrMagnitude < 1e-6f || b.sqrMagnitude < 1e-6f) return;
        paw.rotation = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(a, b), reachWeight) * paw.rotation;
    }
}
