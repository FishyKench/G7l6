using System.Collections;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.UI;

/// <summary>
/// 6 PM: you follow Mom round the house while she inspects it. Your controls are taken away; the view walks behind her like
/// you're trailing her - along the floor she walks on (round corners, through doors, up the stairs, never through a wall), a
/// couple of steps back, easing in and out of the walk with a little head bob, always turning smoothly to keep her in view.
/// When she stops to look at something you stop too; if she turns back your way she steps round you.
/// It opens with a quick dip to black that puts you by the garage door as she comes in, and black bars top and bottom.
/// At the end your body is exactly where the view is, so the hand-back (her talk, the smack, the report) doesn't jump.
/// Hold SPACE to hurry her along (it all plays 3x faster while it's held).
/// Used by DayManager.MomComesHome.
/// </summary>
public class MomEscort : MonoBehaviour
{
    public static MomEscort Active { get; private set; }

    MomInspector mom;
    Transform player, holder, cam;
    Rigidbody body;
    NavMeshObstacle obstacle;
    MoveCamera moveCamera;
    PlayerCam look;
    PlayerMovementAdvanced movement;
    float scale, eyeHeight, speed, walked, bob;
    Vector3 floorPos;
    Quaternion view;
    RectTransform barTop, barBottom;
    Image dip;
    NavMeshPath path;

    void Awake() => path = new NavMeshPath(); // (not allowed in a field initializer)
    Vector3 lastMom;

    float Follow => 2.0f * scale;           // how far behind her you walk
    float MaxSpeed => 2.4f * scale;

    public static MomEscort Begin(MomInspector mom)
    {
        var interaction = PlayerInteraction.Instance;
        var cam = Camera.main;
        if (!mom || !interaction || !cam) return null;
        var go = new GameObject("Mom escort");
        var e = go.AddComponent<MomEscort>();
        e.Setup(mom, interaction.transform, cam.transform);
        Active = e;
        return e;
    }

    void Setup(MomInspector m, Transform p, Transform c)
    {
        mom = m; player = p; cam = c;
        scale = HouseLayout.Instance ? HouseLayout.Instance.scale : 1f;
        moveCamera = FindObjectOfType<MoveCamera>();
        holder = moveCamera ? moveCamera.transform : cam;
        look = FindObjectOfType<PlayerCam>();
        movement = FindObjectOfType<PlayerMovementAdvanced>();
        body = movement ? movement.GetComponent<Rigidbody>() : player.GetComponent<Rigidbody>();
        var feet = player.GetComponentInChildren<Collider>();
        float feetY = feet ? feet.bounds.min.y : player.position.y - 1f;
        eyeHeight = Mathf.Clamp(cam.position.y - feetY, 1.2f * scale, 2.2f * scale);

        // hands off: no walking, looking or using
        if (movement) movement.enabled = false;
        if (look) look.enabled = false;
        if (moveCamera) moveCamera.enabled = false;
        if (PlayerInteraction.Instance) PlayerInteraction.Instance.enabled = false;
        if (body) { body.velocity = Vector3.zero; body.isKinematic = true; }
        // Mom steps round you rather than through you
        var root = movement ? movement.transform : player;
        obstacle = root.gameObject.AddComponent<NavMeshObstacle>();
        obstacle.shape = NavMeshObstacleShape.Capsule;
        obstacle.radius = 0.35f;
        obstacle.height = 1.8f * scale;
        obstacle.carving = false;

        HudHider.Hide();
        Bars();
        floorPos = Ground(cam.position);
        view = cam.rotation;
        lastMom = mom.transform.position;
    }

    /// <summary>The dip to black that puts you by the door as she comes in (call right after she appears).</summary>
    bool arrived;

    public IEnumerator Arrive()
    {
        yield return Dip(1f, 0.35f);
        // a couple of steps into the house from her, looking back at her
        Vector3 m = mom.transform.position;
        Vector3 into = -mom.transform.forward;
        var layout = HouseLayout.Instance;
        if (layout) { var b = layout.PlanBounds(); into = Flat(layout.transform.TransformPoint(new Vector3(b.center.x, 0f, b.center.y)) - m); }
        Vector3 spot = m + (into.sqrMagnitude > 0.01f ? into.normalized : Vector3.forward) * 2.2f * scale;
        var filter = MomNav.Ready ? MomNav.Filter : new NavMeshQueryFilter { agentTypeID = 0, areaMask = NavMesh.AllAreas };
        if (NavMesh.CalculatePath(Ground(m), Ground(spot), filter, path) && path.corners.Length > 1)
            spot = Along(path.corners, 2.2f * scale);
        else if (NavMesh.SamplePosition(spot, out var hit, 2f * scale, filter)) spot = hit.position;
        // a step to the side of her way in, so she walks past you rather than into you
        Vector3 side = Vector3.Cross(Vector3.up, Flat(spot - m).sqrMagnitude > 0.01f ? Flat(spot - m).normalized : Vector3.forward);
        foreach (float sign in new[] { 1f, -1f })
        {
            Vector3 aside = spot + side * sign * 1.0f * scale;
            if (NavMesh.SamplePosition(aside, out var ok, 0.4f * scale, filter) && !NavMesh.Raycast(spot, ok.position, out _, filter)) { spot = ok.position; break; }
        }
        floorPos = spot;
        Vector3 eye = floorPos + Vector3.up * eyeHeight;
        view = Quaternion.LookRotation(Head() - eye);
        Place(eye, view);
        lastMom = mom.transform.position;
        arrived = true;
        yield return new WaitForSeconds(0.25f);
        yield return Dip(0f, 0.45f);
    }

    TMPro.TextMeshProUGUI hint;
    float backing, sideSign;
    SphereCollider probe;
    readonly Collider[] touching = new Collider[16];

    /// <summary>How far to move the view to get it out of anything solid it's touching (sideways only).</summary>
    Vector3 PushOut(Vector3 eye)
    {
        if (!probe)
        {
            probe = new GameObject("View probe").AddComponent<SphereCollider>();
            probe.transform.SetParent(transform, false);
            probe.isTrigger = true;
        }
        float r = 0.22f * scale;
        probe.radius = r;
        probe.transform.position = eye;
        var root = movement ? movement.transform : player;
        Vector3 total = Vector3.zero;
        int n = Physics.OverlapSphereNonAlloc(eye, r, touching, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
        {
            var c = touching[i];
            if (!c || c.transform.IsChildOf(root) || (mom && c.transform.IsChildOf(mom.transform))) continue;
            if (Physics.ComputePenetration(probe, eye, Quaternion.identity, c, c.transform.position, c.transform.rotation, out var dir, out var dist))
                total += dir * dist;
        }
        return total;
    }

    void Update()
    {
        // hold SPACE: the inspection plays faster
        float want = arrived && Input.GetKey(KeyCode.Space) && !PauseMenu.IsPaused ? 3f : 1f;
        if (!PauseMenu.IsPaused && Time.timeScale > 0f && Mathf.Abs(Time.timeScale - want) > 0.01f) Time.timeScale = Mathf.MoveTowards(Time.timeScale, want, Time.unscaledDeltaTime * 8f);
        if (hint) hint.alpha = Mathf.MoveTowards(hint.alpha, arrived ? (want > 1f ? 0.35f : 0.8f) : 0f, Time.unscaledDeltaTime * 2f);
    }

    void OnDestroy() { if (Time.timeScale > 1f) Time.timeScale = 1f; }

    void LateUpdate()
    {
        if (!mom || !arrived) return; // (until the dip has put you by the door, the view stays put)
        float dt = Time.deltaTime;
        Vector3 m = mom.transform.position;
        var filter = MomNav.Ready ? MomNav.Filter : new NavMeshQueryFilter { agentTypeID = 0, areaMask = NavMesh.AllAreas };

        // she skipped ahead (got stuck and was moved on): cut to behind her rather than fly through the walls
        if ((m - lastMom).magnitude > 1.5f * scale && !cutting) StartCoroutine(Cut());
        lastMom = m;

        // walk along her floor towards her while she's further than a couple of steps away
        float want = 0f;
        Vector3 next = floorPos;
        Vector3 here = Near(floorPos);
        if (NavMesh.CalculatePath(here, Ground(m), filter, path) && path.corners.Length > 1)
        {
            float len = Length(path.corners);
            float gap = len - Follow;
            want = gap > 0f ? Mathf.Clamp(gap * 2.2f, 0f, MaxSpeed) : 0f;
            next = path.corners[1];
            if (Flat(next - floorPos).magnitude < 0.05f && path.corners.Length > 2) next = path.corners[2];
        }
        speed = Mathf.MoveTowards(speed, want, (want > speed ? 3f : 5f) * scale * dt); // ease into and out of the walk
        if (speed > 0.001f)
        {
            Vector3 step = next - floorPos;   // (in 3D: up and down the stairs along the route itself)
            float d = step.magnitude;
            floorPos = d <= speed * dt ? next : floorPos + step / d * speed * dt;
            walked += speed * dt;
        }
        // she's coming right up to you (turned back your way): step back and to the side, as fast as she walks
        Vector3 fromHer = Flat(floorPos - m);
        float close = fromHer.magnitude;
        float wantBack = close < 1.3f * scale && Mathf.Abs(floorPos.y - m.y) < 1.5f * scale ? Mathf.InverseLerp(1.3f * scale, 0.7f * scale, close) : 0f;
        backing = Mathf.MoveTowards(backing, wantBack, dt * 4f);
        if (backing > 0.001f)
        {
            Vector3 away = close > 0.01f ? fromHer / close : -Flat(cam.forward).normalized;
            Vector3 side = Vector3.Cross(Vector3.up, away);
            if (sideSign == 0f) sideSign = NavMesh.Raycast(floorPos, floorPos + side * 0.8f * scale, out _, filter) ? -1f : 1f; // (the side with room)
            Vector3 dir = (away + side * sideSign * 0.7f).normalized;
            float back = MaxSpeed * 1.05f * backing;
            Vector3 to = floorPos + dir * back * dt;
            if (!NavMesh.Raycast(floorPos, to, out var edge, filter)) floorPos = to;
            else if (!NavMesh.Raycast(floorPos, floorPos + away * back * dt, out edge, filter)) floorPos += away * back * dt;
            else floorPos = edge.position;
            walked += back * dt;
        }
        else sideSign = 0f;
        // stay on the walkable floor (only the floor you're on - never snap to the one above or below)
        if (NavMesh.SamplePosition(floorPos, out var on, 0.3f * scale, filter) && Mathf.Abs(on.position.y - floorPos.y) < 0.3f * scale)
            floorPos = Vector3.Lerp(floorPos, on.position, 1f - Mathf.Exp(-10f * dt));

        // a gentle head bob while walking
        float walkiness = Mathf.Clamp01(speed / (1.2f * scale));
        bob = Mathf.Lerp(bob, walkiness, 1f - Mathf.Exp(-6f * dt));
        Vector3 eye = floorPos + Vector3.up * (eyeHeight + Mathf.Sin(walked / scale * 6.2f) * 0.025f * scale * bob);
        // never inside a door left open, a shelf, a chair: push the view (and you) out of it
        Vector3 push = PushOut(eye);
        if (push.sqrMagnitude > 0f) { push.y = 0f; floorPos += push; eye += push; }

        // keep her in view: turn smoothly towards her (her face when close, a bit lower when she's walking off)
        Vector3 aim = Head() - Vector3.up * 0.25f * scale;
        Quaternion want2 = Quaternion.LookRotation((aim - eye).sqrMagnitude > 0.01f ? aim - eye : cam.forward);
        var eased = Quaternion.Slerp(view, want2, 1f - Mathf.Exp(-4.5f * dt));
        view = Quaternion.RotateTowards(view, eased, 160f * dt); // (never a whip-round)
        Place(eye, view);
    }

    /// <summary>Puts the view (and your body under it) at this point.</summary>
    void Place(Vector3 eye, Quaternion rot)
    {
        holder.position = eye;
        cam.rotation = rot;
        if (holder != cam) cam.localPosition = Vector3.zero;
        var root = movement ? movement.transform : player;
        Vector3 feet = eye - Vector3.up * eyeHeight;
        var col = root.GetComponentInChildren<Collider>();
        float feetOffset = col ? root.position.y - col.bounds.min.y : 1f * scale;
        Vector3 at = new Vector3(feet.x, feet.y + feetOffset, feet.z);
        if (body) body.position = at;
        root.position = at;
    }

    /// <summary>Hands the controls back to whoever takes over next (her talk / the smack / the report), with no jump.</summary>
    public void End()
    {
        if (Active == this) Active = null;
        if (Time.timeScale > 1f) Time.timeScale = 1f;
        arrived = false;
        // body exactly under the view, so the camera's own follow lands where it already is
        if (moveCamera && moveCamera.cameraPosition)
        {
            var root = movement ? movement.transform : player;
            Vector3 off = holder.position - moveCamera.cameraPosition.position;
            root.position += off;
            if (body) body.position = root.position;
            moveCamera.enabled = true;
        }
        if (look) look.LookTowards(cam.position + cam.forward * 3f); // (the mouse look carries on from this exact view)
        if (body) body.isKinematic = false;
        if (obstacle) Destroy(obstacle);
        StartCoroutine(Outro());
    }

    IEnumerator Outro()
    {
        for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / 0.5f) { SetBars(1f - t); yield return null; }
        if (barTop) Destroy(barTop.root.gameObject);
        HudHider.Show();
        Destroy(gameObject);
    }

    bool cutting;

    IEnumerator Cut()
    {
        cutting = true;
        yield return Dip(1f, 0.15f);
        Vector3 m = mom.transform.position;
        Vector3 behind = m - Flat(mom.transform.forward).normalized * Follow;
        var filter = MomNav.Ready ? MomNav.Filter : new NavMeshQueryFilter { agentTypeID = 0, areaMask = NavMesh.AllAreas };
        if (NavMesh.SamplePosition(behind, out var hit, 1.5f * scale, filter) && !NavMesh.Raycast(Ground(m), hit.position, out _, filter)) floorPos = hit.position;
        else floorPos = Ground(m) - Flat(mom.transform.forward).normalized * 0.8f * scale;
        speed = 0f;
        view = Quaternion.LookRotation(Head() - (floorPos + Vector3.up * eyeHeight));
        yield return null;
        yield return Dip(0f, 0.3f);
        cutting = false;
    }

    // ------------------------------------------------------------------ the frame: bars and the dip

    void Bars()
    {
        var canvas = UIFactory.CreateCanvas("Inspection bars", 70);
        canvas.transform.SetParent(transform, false);
        barTop = Bar(canvas.transform, true);
        barBottom = Bar(canvas.transform, false);
        hint = UIFactory.Text("Hint", barBottom, 24, new Color(1f, 1f, 1f, 0.8f), TMPro.TextAlignmentOptions.Center, GameHUD.Instance ? GameHUD.Instance.font : null);
        hint.text = "MOM'S INSPECTION     <size=80%>hold SPACE to hurry her along</size>";
        hint.raycastTarget = false;
        hint.alpha = 0f;
        UIFactory.Fill(hint.rectTransform);
        dip = UIFactory.Panel("Dip", canvas.transform, new Color(0f, 0f, 0f, 0f));
        UIFactory.Fill(dip.rectTransform);
        StartCoroutine(BarsIn());
    }

    RectTransform Bar(Transform parent, bool top)
    {
        var img = UIFactory.Panel(top ? "Top bar" : "Bottom bar", parent, Color.black);
        var rt = img.rectTransform;
        rt.anchorMin = new Vector2(0f, top ? 1f : 0f);
        rt.anchorMax = new Vector2(1f, top ? 1f : 0f);
        rt.pivot = new Vector2(0.5f, top ? 1f : 0f);
        rt.sizeDelta = new Vector2(0f, 0f);
        return rt;
    }

    IEnumerator BarsIn() { for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / 0.6f) { SetBars(Mathf.SmoothStep(0f, 1f, t)); yield return null; } SetBars(1f); }

    void SetBars(float k)
    {
        if (barTop) barTop.sizeDelta = new Vector2(0f, 95f * k);
        if (barBottom) barBottom.sizeDelta = new Vector2(0f, 95f * k);
    }

    IEnumerator Dip(float to, float time)
    {
        if (!dip) yield break;
        float from = dip.color.a;
        for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / time) { dip.color = new Color(0f, 0f, 0f, Mathf.Lerp(from, to, Mathf.SmoothStep(0f, 1f, t))); yield return null; }
        dip.color = new Color(0f, 0f, 0f, to);
    }

    // ------------------------------------------------------------------ helpers

    Vector3 Head() => mom ? mom.transform.position + Vector3.up * 1.55f * 0.9f * scale : cam.position + cam.forward;

    static float Length(Vector3[] c) { float l = 0f; for (int i = 1; i < c.Length; i++) l += Vector3.Distance(c[i - 1], c[i]); return l; }

    static Vector3 Along(Vector3[] c, float d)
    {
        for (int i = 1; i < c.Length; i++)
        {
            float seg = Vector3.Distance(c[i - 1], c[i]);
            if (d <= seg) return Vector3.Lerp(c[i - 1], c[i], d / Mathf.Max(0.0001f, seg));
            d -= seg;
        }
        return c[c.Length - 1];
    }

    /// <summary>The walkable floor right under you (a small search: on the stairs the next floor is close by).</summary>
    Vector3 Near(Vector3 p)
    {
        var filter = MomNav.Ready ? MomNav.Filter : new NavMeshQueryFilter { agentTypeID = 0, areaMask = NavMesh.AllAreas };
        if (NavMesh.SamplePosition(p, out var hit, 0.4f * scale, filter)) return hit.position;
        return Ground(p);
    }

    Vector3 Ground(Vector3 p)
    {
        var filter = MomNav.Ready ? MomNav.Filter : new NavMeshQueryFilter { agentTypeID = 0, areaMask = NavMesh.AllAreas };
        return NavMesh.SamplePosition(p, out var hit, 2.5f * scale, filter) ? hit.position : p;
    }

    static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }
}
