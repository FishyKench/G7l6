using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.UI;

/// <summary>
/// Mom. At 6 PM she comes in through the garage and walks the house: to each thing that's still wrong (nearest first - she only
/// counts it if it's STILL wrong when she gets there, so you can keep fixing things in rooms she hasn't reached yet), and she always
/// checks two places: the kitchen (dinner) and your room (is it tidy?). Then she comes to find you - and if the day comes out below
/// a B, she smacks you (cartoon-style) before the report card.
/// Looks: the same Mom as always (purple coat, ponytail, handbag), built with joints - hips, shoulders, neck - so she really walks
/// (legs and arms swinging, feet on the floor) and has hand-made gestures: checking things, shaking her head, nodding, the slap.
/// Walks on her own walking map (MomNav), sized for a grown-up so she keeps off the walls.
/// </summary>
public class MomInspector : MonoBehaviour
{
    public float walkSpeed = 2.4f;
    public float lookTime = 1.3f;
    [Tooltip("The whole walk gives up after this many seconds (then the report card comes up).")]
    public float maxInspection = 120f;

    DayManager day;
    NavMeshAgent agent;
    Transform body, nameTag;
    TMP_FontAsset font;
    float scale = 1f;
    bool stopped;
    readonly HashSet<string> seen = new HashSet<string>();

    /// <summary>Spawns Mom in the garage (or by the front door). Null if there's no house layout.</summary>
    public static MomInspector Spawn(DayManager day)
    {
        var layout = HouseLayout.Instance;
        if (!layout) return null;
        MomNav.Bake(); // normally done at the start of the day already
        Vector3 start = layout.transform.TransformPoint(new Vector3(3f, 0.2f, 1.6f)); // garage, just inside the door
        foreach (var r in layout.rooms)
            if (r.name == "Garage" && r.floor == 0)
                start = layout.transform.TransformPoint(new Vector3(r.rect.center.x, 0.2f, r.rect.yMin + 1.4f));

        var go = new GameObject("Mom");
        go.transform.position = start;
        var mom = go.AddComponent<MomInspector>();
        mom.day = day;
        mom.walkSpeed *= Campaign.Today.momSpeed; // Friday: she is in a hurry
        mom.font = GameHUD.Instance ? GameHUD.Instance.font : null;
        mom.BuildFigure(layout.scale);
        return mom;
    }

    // ------------------------------------------------------------------ the figure

    void BuildFigure(float houseScale)
    {
        float s = Mathf.Max(0.8f, houseScale * 0.9f); // about as tall as you
        scale = houseScale;
        size = s;
        body = new GameObject("Body").transform;
        body.SetParent(transform, false);
        BuildRig(s);

        var tag = new GameObject("Name").AddComponent<TextMeshPro>();
        tag.transform.SetParent(transform, false);
        tag.transform.localPosition = Vector3.up * 2.0f * s;
        if (font) tag.font = font;
        tag.text = "MOM";
        tag.fontSize = 3f;
        tag.color = new Color(0.88f, 0.2f, 0.17f);
        tag.alignment = TextAlignmentOptions.Center;
        tag.rectTransform.sizeDelta = new Vector2(2f, 0.6f);
        nameTag = tag.transform;

        agent = gameObject.AddComponent<NavMeshAgent>();
        agent.agentTypeID = MomNav.AgentType;
        agent.radius = MomNav.Ready ? MomNav.Radius : 0.2f;
        agent.height = 1.8f * s;
        agent.speed = walkSpeed * houseScale;
        agent.angularSpeed = 420f;
        agent.acceleration = 10f;
        agent.stoppingDistance = 0.9f * houseScale;
        agent.obstacleAvoidanceType = ObstacleAvoidanceType.NoObstacleAvoidance;
        agent.autoBraking = true;
        if (MomNav.Sample(transform.position, 4f, out var hit)) agent.Warp(hit.position);
    }

    // ------------------------------------------------------------------ the figure, with joints

    Transform hips, torso, neck, legL, legR, armL, armR, bag;
    float size, phase, walkAmount, idleTime, actionWeight;
    string action;
    float actionTime = -1f, actionSpeed = 1f;

    /// <summary>Her gestures and how long each takes (seconds).</summary>
    static readonly Dictionary<string, float> Gestures = new Dictionary<string, float>
    {
        { "interact-right", 1.1f }, { "interact-left", 1.1f }, { "pick-up", 1.4f }, { "point", 1.3f },
        { "emote-no", 1.2f }, { "emote-yes", 1.0f }, { "hands-on-hips", 1.8f }, { "attack-melee-right", 1.0f },
    };

    void BuildRig(float s)
    {
        var coat = Mat(new Color(0.55f, 0.27f, 0.52f));
        var dark = Mat(new Color(0.16f, 0.15f, 0.2f));
        var skin = Mat(new Color(0.96f, 0.8f, 0.68f));
        var hair = Mat(new Color(0.36f, 0.22f, 0.14f));
        var bagM = Mat(new Color(0.85f, 0.62f, 0.2f));
        var sleeve = Mat(new Color(0.46f, 0.22f, 0.44f)); // a shade darker than the coat, so her arms read against it

        hips = Joint("Hips", body, new Vector3(0f, 0.84f, 0f), s);
        legL = Joint("Hip L", hips, new Vector3(-0.09f, 0f, 0f), s);
        Part(PrimitiveType.Cylinder, "Leg L", legL, new Vector3(0f, -0.42f, 0f), new Vector3(0.11f, 0.42f, 0.11f), dark, s);
        legR = Joint("Hip R", hips, new Vector3(0.09f, 0f, 0f), s);
        Part(PrimitiveType.Cylinder, "Leg R", legR, new Vector3(0f, -0.42f, 0f), new Vector3(0.11f, 0.42f, 0.11f), dark, s);

        torso = Joint("Torso", hips, Vector3.zero, s);
        Part(PrimitiveType.Capsule, "Coat", torso, new Vector3(0f, 0.28f, 0f), new Vector3(0.44f, 0.45f, 0.3f), coat, s);

        neck = Joint("Neck", torso, new Vector3(0f, 0.62f, 0f), s);
        Part(PrimitiveType.Sphere, "Head", neck, new Vector3(0f, 0.16f, 0f), new Vector3(0.26f, 0.3f, 0.26f), skin, s);
        Part(PrimitiveType.Sphere, "Hair", neck, new Vector3(0f, 0.21f, -0.04f), new Vector3(0.29f, 0.3f, 0.28f), hair, s);
        Part(PrimitiveType.Capsule, "Ponytail", neck, new Vector3(0f, 0.06f, -0.17f), new Vector3(0.1f, 0.14f, 0.1f), hair, s);
        Part(PrimitiveType.Sphere, "Eye L", neck, new Vector3(-0.055f, 0.19f, 0.12f), Vector3.one * 0.035f, dark, s);
        Part(PrimitiveType.Sphere, "Eye R", neck, new Vector3(0.055f, 0.19f, 0.12f), Vector3.one * 0.035f, dark, s);

        armL = Joint("Shoulder L", torso, new Vector3(-0.245f, 0.5f, 0f), s);
        Part(PrimitiveType.Capsule, "Sleeve L", armL, new Vector3(0f, -0.2f, 0f), new Vector3(0.1f, 0.2f, 0.1f), sleeve, s);
        Part(PrimitiveType.Sphere, "Hand L", armL, new Vector3(0f, -0.43f, 0f), Vector3.one * 0.085f, skin, s);
        armR = Joint("Shoulder R", torso, new Vector3(0.245f, 0.5f, 0f), s);
        Part(PrimitiveType.Capsule, "Sleeve R", armR, new Vector3(0f, -0.2f, 0f), new Vector3(0.1f, 0.2f, 0.1f), sleeve, s);
        Part(PrimitiveType.Sphere, "Hand R", armR, new Vector3(0f, -0.43f, 0f), Vector3.one * 0.085f, skin, s);

        // the handbag, in her left hand
        bag = Joint("Bag", armL, new Vector3(0f, -0.44f, 0f), s);
        Part(PrimitiveType.Cube, "Handbag", bag, new Vector3(-0.01f, -0.12f, 0f), new Vector3(0.08f, 0.2f, 0.28f), bagM, s);
    }

    static Transform Joint(string name, Transform parent, Vector3 pos, float s)
    {
        var t = new GameObject(name).transform;
        t.SetParent(parent, false);
        t.localPosition = pos * s;
        return t;
    }

    void Part(PrimitiveType shape, string name, Transform parent, Vector3 pos, Vector3 size, Material m, float s)
    {
        var p = GameObject.CreatePrimitive(shape);
        p.name = name;
        Destroy(p.GetComponent<Collider>());
        p.transform.SetParent(parent, false);
        p.transform.localPosition = pos * s;
        p.transform.localScale = size * s;
        p.GetComponent<MeshRenderer>().sharedMaterial = m;
    }

    static Material Mat(Color c)
    {
        var m = new Material(Shader.Find("Standard")) { color = c };
        m.SetFloat("_Glossiness", 0.15f);
        return m;
    }

    /// <summary>A gesture (reaching out to check something, shaking her head, the slap...).</summary>
    void Act(string name, float speed = 1f)
    {
        if (!Gestures.ContainsKey(name)) return;
        action = name;
        actionTime = 0f;
        actionSpeed = speed;
    }

    float ActLength(string name) => Gestures.TryGetValue(name, out var len) ? len : 0.5f;

    /// <summary>An arm's turn: pitch (+ = back), roll (+ = out to her right), yaw (+ = round towards her back, for an arm held out).</summary>
    static Quaternion Arm(Vector3 a) =>
        Quaternion.AngleAxis(a.y, Vector3.up) * Quaternion.AngleAxis(a.z, Vector3.forward) * Quaternion.AngleAxis(a.x, Vector3.right);

    struct Pose
    {
        public Vector3 torso, neck, armL, armR, legL, legR; // euler (arms: pitch, yaw, roll for Arm())
        public Vector3 offset;                            // the whole body (a lunge)
    }

    void Update()
    {
        if (!agent || !body || !hips) return;
        float dt = Time.deltaTime;
        float speed = agent.velocity.magnitude;
        idleTime += dt;

        // ---- walking / standing (in step with how fast she's really going: one cycle = two steps)
        walkAmount = Mathf.MoveTowards(walkAmount, Mathf.Clamp01(speed / (0.5f * scale)), dt * 4f);
        float stride = 1.3f * size;
        phase += dt * speed / stride * Mathf.PI * 2f;
        float w = walkAmount, rest = 1f - w, sn = Mathf.Sin(phase);
        float swing = 28f * w * sn;

        var b = new Pose();
        b.legL = new Vector3(swing, 0f, 0f);
        b.legR = new Vector3(-swing, 0f, 0f);
        b.armL = new Vector3(-swing * 0.55f, 0f, -5f);          // arms swing against the legs (the bag one a little less)
        b.armR = new Vector3(swing * 0.75f, 0f, 5f);
        b.torso = new Vector3(5f * w + Mathf.Sin(idleTime * 1.7f) * 1.3f * rest, -sn * 6f * w, 0f); // leans into the walk, shoulders twist, breathes
        b.neck = new Vector3(-3f * w, sn * 4f * w + Mathf.Sin(idleTime * 0.45f) * 16f * rest * Mathf.Clamp01(Mathf.Sin(idleTime * 0.23f) * 3f), 0f); // looks about when standing
        // stiff legs: the hips dip when the legs are apart, so both feet stay on the floor
        float drop = 0.84f * size * (1f - Mathf.Cos(Mathf.Abs(swing) * Mathf.Deg2Rad));
        float sway = Mathf.Sin(phase) * 0.012f * size * w;

        // ---- a gesture on top
        var g = b;
        if (actionTime >= 0f)
        {
            float len = ActLength(action);
            actionTime += dt * actionSpeed;
            float u = Mathf.Clamp01(actionTime / len);
            bool slap = action == "attack-melee-right";
            actionWeight = Mathf.MoveTowards(actionWeight, u < (slap ? 0.85f : 0.8f) ? 1f : 0f, dt * (slap ? 12f : 6f));
            g = Gesture(action, u, b);
            if (actionTime >= len && actionWeight <= 0f) actionTime = -1f;
        }
        else actionWeight = Mathf.MoveTowards(actionWeight, 0f, dt * 6f);
        float k = Mathf.SmoothStep(0f, 1f, actionWeight);

        body.localPosition = new Vector3(sway, -drop, 0f) + g.offset * k;
        hips.localRotation = Quaternion.Euler(0f, 0f, sn * 2f * w);
        legL.localRotation = Quaternion.Slerp(Quaternion.Euler(b.legL), Quaternion.Euler(g.legL), k);
        legR.localRotation = Quaternion.Slerp(Quaternion.Euler(b.legR), Quaternion.Euler(g.legR), k);
        torso.localRotation = Quaternion.Slerp(Quaternion.Euler(b.torso), Quaternion.Euler(g.torso), k);
        neck.localRotation = Quaternion.Slerp(Quaternion.Euler(b.neck), Quaternion.Euler(g.neck), k);
        armL.localRotation = Quaternion.Slerp(Arm(b.armL), Arm(g.armL), k);
        armR.localRotation = Quaternion.Slerp(Arm(b.armR), Arm(g.armR), k);
        // the bag hangs straight down from her hand, swinging a little behind the arm
        Quaternion armWorld = torso.localRotation * armL.localRotation;
        bag.localRotation = Quaternion.Inverse(armWorld) * Quaternion.Euler(Mathf.Sin(phase - 0.8f) * 7f * w, 0f, 0f);
    }

    static float Ease(float a, float b, float u) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(a, b, u));

    /// <summary>The pose for a gesture at 'u' (0..1 through it), from the walking / standing pose 'b'.</summary>
    Pose Gesture(string name, float u, Pose b)
    {
        var p = b;
        p.legL = p.legR = Vector3.zero;
        switch (name)
        {
            case "interact-right": // reaches out to it and taps it
            case "interact-left":
            {
                bool right = name == "interact-right";
                var reach = new Vector3(-58f + Mathf.Sin(u * Mathf.PI * 4f) * 6f, 0f, right ? 6f : -6f);
                if (right) p.armR = reach; else p.armL = reach;
                p.torso = new Vector3(16f, right ? -6f : 6f, 0f);
                p.neck = new Vector3(16f, 0f, 0f);
                break;
            }
            case "pick-up": // bends down to look closer
                p.torso = new Vector3(38f * Mathf.Sin(u * Mathf.PI), 0f, 0f);
                p.armR = new Vector3(-45f, 0f, 8f);
                p.armL = new Vector3(-30f, 0f, -8f);
                p.neck = new Vector3(14f, 0f, 0f);
                break;
            case "point": // "and THAT."
                p.armR = new Vector3(-86f, 0f, 4f);
                p.torso = new Vector3(3f, -10f, 0f);
                p.neck = new Vector3(-2f, 6f, 0f);
                break;
            case "emote-no": // shakes her head, arms out
                p.neck = new Vector3(4f, Mathf.Sin(u * Mathf.PI * 6f) * 24f, 0f);
                p.armR = new Vector3(-12f, 0f, 32f);
                p.armL = new Vector3(-12f, 0f, -32f);
                p.torso = new Vector3(-4f, 0f, 0f);
                break;
            case "emote-yes": // nods, thumbs up
                p.neck = new Vector3(Mathf.Abs(Mathf.Sin(u * Mathf.PI * 4f)) * 16f, 0f, 0f);
                p.armR = new Vector3(-72f, 0f, 12f);
                break;
            case "hands-on-hips": // waiting for an explanation
                p.armR = new Vector3(18f, 0f, 30f);
                p.armL = new Vector3(18f, 0f, -30f);
                p.torso = new Vector3(-5f, 0f, 0f);
                p.neck = new Vector3(-6f, 0f, 8f);
                break;
            case "attack-melee-right": // the slap: wind up, swing across at face height, follow through
            {
                float wind = Ease(0f, 0.32f, u), hit = Ease(0.32f, 0.5f, u), back = Ease(0.78f, 1f, u);
                float yaw = Mathf.Lerp(Mathf.Lerp(0f, 38f, wind), -105f, hit);
                float roll = Mathf.Lerp(0f, 112f, wind);
                p.armR = new Vector3(Mathf.Lerp(0f, -8f, wind), yaw, roll);
                p.armL = new Vector3(-10f, 0f, -18f);
                p.torso = new Vector3(Mathf.Lerp(0f, 6f, hit), Mathf.Lerp(Mathf.Lerp(0f, 24f, wind), -30f, hit), 0f);
                p.neck = new Vector3(0f, Mathf.Lerp(Mathf.Lerp(0f, -14f, wind), 12f, hit), 0f);
                p.offset = new Vector3(0f, 0f, Mathf.Lerp(Mathf.Lerp(0f, -0.04f, wind), 0.12f, hit) * (1f - back) * size);
                p.legR = new Vector3(-12f * hit * (1f - back), 0f, 0f); // steps into it
                break;
            }
        }
        return p;
    }

    void LateUpdate()
    {
        var cam = Camera.main;
        if (nameTag && cam) nameTag.rotation = Quaternion.LookRotation(nameTag.position - cam.transform.position);
    }

    public void Stop()
    {
        stopped = true;
        if (agent && agent.isOnNavMesh) agent.isStopped = true;
    }

    // ------------------------------------------------------------------ the walk

    /// <summary>A place she always looks at (the kitchen, your room), whether or not anything's wrong there.</summary>
    class Visit
    {
        public string name;
        public Vector3 where;
        public bool done;
    }

    /// <summary>The walk: nearest thing first (problems and her two visits), re-checking the house at every stop.</summary>
    public IEnumerator Inspect()
    {
        float until = Time.time + maxInspection;
        seen.Clear();
        Say("I'm home!", Color.white);
        yield return new WaitForSeconds(1.2f);

        var visits = new List<Visit>();
        var kitchen = KitchenSpot();
        if (kitchen.HasValue) visits.Add(new Visit { name = "kitchen", where = kitchen.Value });
        var room = BedroomSpot();
        if (room.HasValue) visits.Add(new Visit { name = "bedroom", where = room.Value });

        while (!stopped && Time.time < until)
        {
            var next = Nearest(day.CurrentFindings());
            Visit visit = null;
            float bestVisit = float.MaxValue;
            foreach (var v in visits)
                if (!v.done && Cost(v.where) < bestVisit) { bestVisit = Cost(v.where); visit = v; }
            if (next == null && visit == null) break;

            Vector3 target = visit != null && (next == null || bestVisit < Cost(next.where)) ? visit.where : next.where;
            bool isVisit = visit != null && target == visit.where;
            yield return WalkTo(target, until);
            if (stopped) yield break;
            yield return Look(target);

            if (isVisit)
            {
                visit.done = true;
                yield return visit.name == "kitchen" ? CheckKitchen(target) : CheckBedroom(target);
            }

            // Is it still like that now that she's standing here?
            bool any = false;
            foreach (var f in day.CurrentFindings())
            {
                if (Vector3.Distance(Flat(f.where), Flat(transform.position)) > 3.2f * scale) continue;
                if (Mathf.Abs(f.where.y - transform.position.y) > 2.6f * scale) continue;
                if (!seen.Add(f.what + Key(f.where))) continue;
                day.caught.Add(f);
                if (!any) Act("interact-right"); // reaches out to it
                any = true;
                Say(f.line, new Color(1f, 0.35f, 0.3f));
                if (GameHUD.Instance) GameHUD.Toast("Mom: \"" + f.line + "\"", GameHUD.Bad);
                yield return new WaitForSeconds(lookTime);
            }
            if (any) { Act("emote-no"); yield return new WaitForSeconds(ActLength("emote-no")); }
            seen.Add("visited" + Key(target)); // don't walk back to the same spot
            foreach (var v in visits) if (Vector3.Distance(Flat(v.where), Flat(transform.position)) < 2.5f * scale && Mathf.Abs(v.where.y - transform.position.y) < 2f * scale) v.done = true;
        }

        // Then she comes to find you.
        var player = PlayerInteraction.Instance ? PlayerInteraction.Instance.transform : null;
        if (player && !stopped && Time.time < until)
        {
            yield return WalkTo(player.position, Mathf.Min(until, Time.time + 25f));
            yield return Look(player.position);
            Say(day.caught.Count == 0 ? "Well, well, well..." : "We need to talk.", Color.white);
            yield return new WaitForSeconds(1.5f);
        }
    }

    // ---- her two visits

    Vector3? KitchenSpot()
    {
        var c = Cooking.Instance;
        if (!c) return null;
        // the pot / pan / bowl if it's out, else the stove
        var pot = c.DinnerSpot;
        return pot;
    }

    Vector3? BedroomSpot()
    {
        var layout = HouseLayout.Instance;
        if (!layout) return null;
        foreach (var r in layout.rooms)
            if (r.name == "Your bedroom")
                return layout.transform.TransformPoint(new Vector3(r.rect.center.x, r.floor * layout.storeyHeight + 0.2f, r.rect.center.y));
        return null;
    }

    IEnumerator CheckKitchen(Vector3 at)
    {
        var c = Cooking.Instance;
        Act("interact-right"); // a look in the pot
        yield return new WaitForSeconds(0.8f);
        if (c && !c.IsActive && c.LastSucceeded)
        {
            bool perfect = day.quality.Exists(q => q.what.StartsWith("Perfect"));
            bool tasty = day.quality.Exists(q => q.what.StartsWith("Tasty"));
            Say(perfect ? "Mmm! That smells AMAZING." : tasty ? "Dinner's ready? Good job." : "...Well. At least it's food.", perfect || tasty ? new Color(0.45f, 0.95f, 0.5f) : Color.white);
            if (perfect || tasty) Act("emote-yes");
            yield return new WaitForSeconds(lookTime);
        }
        // (no dinner: that's a finding at the stove - it's said and counted with the others)
    }

    IEnumerator CheckBedroom(Vector3 at)
    {
        var layout = HouseLayout.Instance;
        int floor = layout ? layout.FloorAt(at.y) : 0;
        var room = layout ? layout.RoomAt(at, floor) : null;
        int mess = 0;
        foreach (var item in FindObjectsOfType<Carryable>())
        {
            if (!item.gameObject.activeInHierarchy || item.IsHeld || item.type != ItemType.DirtyClothes) continue;
            if (room != null && layout.RoomAt(item.transform.position, layout.FloorAt(item.transform.position.y)) != room) continue;
            mess++;
        }
        Act("interact-right");
        yield return new WaitForSeconds(0.6f);
        bool laundryCounted = day.caught.Exists(f => f.what == "Laundry not done") || day.CurrentFindings().Exists(f => f.what == "Laundry not done");
        if (mess > 0 && !laundryCounted)
        {
            var f = new DayManager.Finding { what = "Your room is a mess", line = "Look at the state of your ROOM!", points = Mathf.Min(12, 3 * mess), where = at };
            day.caught.Add(f);
            Say(f.line, new Color(1f, 0.35f, 0.3f));
            if (GameHUD.Instance) GameHUD.Toast("Mom: \"" + f.line + "\"", GameHUD.Bad);
            Act("emote-no");
        }
        else if (mess == 0)
        {
            Say("Your room's tidy. Nice.", new Color(0.45f, 0.95f, 0.5f));
            Act("emote-yes");
        }
        yield return new WaitForSeconds(lookTime);
    }

    // ------------------------------------------------------------------ the smack (a bad day: below a B)

    public IEnumerator Smack()
    {
        var player = PlayerInteraction.Instance ? PlayerInteraction.Instance.transform : null;
        var cam = Camera.main;
        if (!player || !cam) yield break;
        // you stand still for this
        foreach (var m in FindObjectsOfType<PlayerMovementAdvanced>()) m.enabled = false;
        foreach (var pc in FindObjectsOfType<PlayerCam>()) pc.enabled = false;
        if (PlayerInteraction.Instance) PlayerInteraction.Instance.enabled = false;
        HudHider.Hide(); // just the two of you
        if (nameTag) nameTag.gameObject.SetActive(false);

        // up to you: a step in front of you (never onto you, even if she has to skip ahead)
        Vector3 fromYou = Flat(transform.position - player.position);
        if (fromYou.sqrMagnitude < 0.01f) fromYou = Flat(cam.transform.forward);
        Vector3 spot = player.position + fromYou.normalized * 1.05f * scale;
        if (MomNav.Sample(spot, 1.2f * scale, out var near)) spot = near.position;
        float before = agent.stoppingDistance;
        agent.stoppingDistance = 0.1f;
        yield return WalkTo(spot, Time.time + 8f);
        agent.stoppingDistance = before;
        if (Vector3.Distance(Flat(transform.position), Flat(player.position)) < 0.7f * scale) Teleport(spot);
        yield return Look(player.position);

        // you turn to face her
        Vector3 face = transform.position + Vector3.up * 1.6f * 0.9f * scale; // her face (she's ~1.75 x 0.9 x the house scale tall)
        Quaternion from = cam.transform.rotation, to = Quaternion.LookRotation(face - cam.transform.position);
        for (float t = 0f; t < 1f; t += Time.deltaTime / 0.4f) { cam.transform.rotation = Quaternion.Slerp(from, to, Mathf.SmoothStep(0f, 1f, t)); yield return null; }
        Say("You know what you did.", new Color(1f, 0.35f, 0.3f));
        yield return new WaitForSeconds(1.1f);

        // the smack
        var fx = LastSmack = SmackScreen.Create(font);
        Act("attack-melee-right", 1.2f);
        yield return new WaitForSeconds(ActLength("attack-melee-right") * 0.45f / 1.2f);
        fx.Hit();
        Quaternion hitFrom = cam.transform.rotation;
        Quaternion hitTo = Quaternion.AngleAxis(48f, Vector3.up) * hitFrom * Quaternion.Euler(0f, 0f, -18f); // your head whips round
        for (float t = 0f; t < 1f; t += Time.deltaTime / 0.09f) { cam.transform.rotation = Quaternion.Slerp(hitFrom, hitTo, t); yield return null; }
        for (float t = 0f; t < 1f; t += Time.deltaTime / 0.5f)
        {
            float shake = (1f - t) * 3f;
            cam.transform.rotation = hitTo * Quaternion.Euler(Random.Range(-shake, shake), Random.Range(-shake, shake), 0f);
            yield return null;
        }
        yield return new WaitForSeconds(0.5f);
        yield return fx.FadeToBlack(0.7f);
        yield return new WaitForSeconds(0.4f);
        HudHider.Show(); // (under the black: the report card comes up over it)
    }

    // ------------------------------------------------------------------ debug (MomDebug, F1)

    /// <summary>What she's up to, for the debug panel.</summary>
    public string Doing { get; private set; } = "standing";
    public SmackScreen LastSmack { get; private set; }
    public IEnumerable<string> ClipNames => Gestures.Keys;
    public bool Animated => true;

    /// <summary>Stops whatever she's doing (a walk, a roam) and stands still.</summary>
    public void Halt()
    {
        StopAllCoroutines();
        stopped = false;
        if (agent && agent.isOnNavMesh) agent.isStopped = true;
        Doing = "standing";
    }

    public void WarpTo(Vector3 where) => Teleport(where);
    public void Gesture(string clip) { Act(clip); Doing = clip; }
    public IEnumerator WalkHere(Vector3 where) { Doing = "walking to you"; yield return WalkTo(where, Time.time + 30f); Doing = "standing"; }

    /// <summary>Walks from room to room for ever (random rooms, any floor), with a gesture at each stop.</summary>
    public IEnumerator Roam()
    {
        var layout = HouseLayout.Instance;
        if (!layout || layout.rooms.Count == 0) yield break;
        string[] gestures = { "interact-right", "emote-no", "emote-yes", "interact-left", "pick-up" };
        string last = null;
        while (true)
        {
            var r = layout.rooms[Random.Range(0, layout.rooms.Count)];
            if (r.name == last && layout.rooms.Count > 1) continue;
            last = r.name;
            Vector3 p = layout.transform.TransformPoint(new Vector3(r.rect.center.x + Random.Range(-0.25f, 0.25f) * r.rect.width,
                r.floor * layout.storeyHeight + 0.2f, r.rect.center.y + Random.Range(-0.25f, 0.25f) * r.rect.height));
            Doing = "walking to " + r.name;
            yield return WalkTo(p, Time.time + 30f);
            yield return new WaitForSeconds(0.5f);
            var g = gestures[Random.Range(0, gestures.Length)];
            if (Gestures.ContainsKey(g)) { Doing = g + " in " + r.name; Act(g); yield return new WaitForSeconds(ActLength(g) + 0.3f); }
            Doing = "standing in " + r.name;
            yield return new WaitForSeconds(Random.Range(0.8f, 1.8f));
        }
    }

    /// <summary>After a smack that isn't the end of the day (debug): back from black, you can move again.</summary>
    public IEnumerator RecoverFromSmack()
    {
        if (LastSmack) yield return LastSmack.FadeFromBlack(0.6f);
        if (LastSmack) Destroy(LastSmack.gameObject);
        LastSmack = null;
        if (nameTag) nameTag.gameObject.SetActive(true);
        foreach (var m in FindObjectsOfType<PlayerMovementAdvanced>(true)) m.enabled = true;
        foreach (var pc in FindObjectsOfType<PlayerCam>(true)) pc.enabled = true;
        if (PlayerInteraction.Instance) PlayerInteraction.Instance.enabled = true;
        HudHider.Show();
        Doing = "standing";
    }

    // ------------------------------------------------------------------ walking

    float Cost(Vector3 where) => Vector3.Distance(Flat(where), Flat(transform.position)) + Mathf.Abs(where.y - transform.position.y) * 4f;

    DayManager.Finding Nearest(List<DayManager.Finding> findings)
    {
        DayManager.Finding best = null;
        float bestCost = float.MaxValue;
        foreach (var f in findings)
        {
            if (seen.Contains(f.what + Key(f.where)) || seen.Contains("visited" + Key(f.where))) continue;
            float cost = Cost(f.where); // stairs cost extra: finish a floor before moving on
            if (cost < bestCost) { bestCost = cost; best = f; }
        }
        return best;
    }

    IEnumerator WalkTo(Vector3 target, float until)
    {
        if (!agent.isOnNavMesh || !MomNav.Sample(target, 2.5f * scale, out var hit))
        {
            Teleport(target);
            yield break;
        }
        agent.isStopped = false;
        agent.SetDestination(hit.position);
        float stuckSince = Time.time;
        Vector3 last = transform.position;
        while (!stopped && Time.time < until)
        {
            if (!agent.pathPending && agent.remainingDistance <= agent.stoppingDistance + 0.05f) break;
            if (Vector3.Distance(transform.position, last) > 0.3f) { last = transform.position; stuckSince = Time.time; }
            if (Time.time - stuckSince > 4f) { Teleport(hit.position); break; } // can't get there: skip ahead
            yield return null;
        }
        if (agent.isOnNavMesh) agent.isStopped = true;
        // Ended up somewhere else (no path, e.g. another floor)? She gets there anyway.
        if (!stopped && Vector3.Distance(transform.position, hit.position) > 2.5f * scale) Teleport(hit.position);
    }

    void Teleport(Vector3 target)
    {
        if (agent && MomNav.Sample(target, 3f * scale, out var hit)) agent.Warp(hit.position);
        else transform.position = target;
    }

    IEnumerator Look(Vector3 at)
    {
        Vector3 d = Flat(at - transform.position);
        if (d.sqrMagnitude < 0.01f) yield break;
        Quaternion from = transform.rotation, to = Quaternion.LookRotation(d);
        for (float t = 0f; t < 1f; t += Time.deltaTime / 0.35f)
        {
            transform.rotation = Quaternion.Slerp(from, to, t);
            yield return null;
        }
        yield return new WaitForSeconds(0.4f);
    }

    void Say(string line, Color color) =>
        FloatingWord.Spawn(line, transform.position + Vector3.up * 2.3f * scale, color, font, 2.2f, 2.4f);

    static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }
    static string Key(Vector3 p) => $"{Mathf.RoundToInt(p.x)},{Mathf.RoundToInt(p.y)},{Mathf.RoundToInt(p.z)}";
}

/// <summary>The smack on screen: a white flash, "SMACK!" in red with little stars, then black (the report card comes up over it).</summary>
public class SmackScreen : MonoBehaviour
{
    Image flash, black;
    TextMeshProUGUI word;
    readonly List<RectTransform> stars = new List<RectTransform>();
    float hitAt = -1f;

    public static SmackScreen Create(TMP_FontAsset font)
    {
        var canvas = UIFactory.CreateCanvas("Smack", 54); // under the report card (55), over the HUD
        var fx = canvas.gameObject.AddComponent<SmackScreen>();
        fx.black = UIFactory.Panel("Black", canvas.transform, new Color(0f, 0f, 0f, 0f));
        UIFactory.Fill(fx.black.rectTransform);
        fx.flash = UIFactory.Panel("Flash", canvas.transform, new Color(1f, 0.9f, 0.85f, 0f));
        UIFactory.Fill(fx.flash.rectTransform);
        fx.word = UIFactory.Text("SMACK", canvas.transform, 150, new Color(0.9f, 0.12f, 0.1f), TextAlignmentOptions.Center, font);
        fx.word.text = "<b>SMACK!</b>";
        fx.word.outlineWidth = 0.28f;
        fx.word.outlineColor = new Color32(255, 240, 200, 255);
        fx.word.alpha = 0f;
        UIFactory.Place(fx.word.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(120, 140), new Vector2(900, 220));
        for (int i = 0; i < 5; i++)
        {
            var st = UIFactory.Text("Star", canvas.transform, 70, new Color(1f, 0.85f, 0.2f), TextAlignmentOptions.Center, font);
            st.text = "*";
            st.alpha = 0f;
            UIFactory.Place(st.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(80, 80));
            fx.stars.Add(st.rectTransform);
        }
        return fx;
    }

    public void Hit() => hitAt = Time.unscaledTime;

    void Update()
    {
        if (hitAt < 0f) return;
        float t = Time.unscaledTime - hitAt;
        flash.color = new Color(1f, 0.9f, 0.85f, Mathf.Clamp01(0.85f - t * 3f));
        word.alpha = t < 1.2f ? 1f : Mathf.Clamp01(1f - (t - 1.2f) * 3f);
        word.rectTransform.localScale = Vector3.one * (t < 0.12f ? Mathf.Lerp(2.2f, 1f, t / 0.12f) : 1f + 0.03f * Mathf.Sin(t * 30f) * Mathf.Exp(-t * 3f));
        word.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -8f);
        for (int i = 0; i < stars.Count; i++)
        {
            float a = t * 3f + i * Mathf.PI * 2f / stars.Count;
            stars[i].anchoredPosition = new Vector2(Mathf.Cos(a) * 260f, Mathf.Sin(a) * 70f + 250f);
            stars[i].GetComponent<TextMeshProUGUI>().alpha = Mathf.Clamp01(1.6f - t);
        }
    }

    public IEnumerator FadeFromBlack(float seconds)
    {
        hitAt = -1f;
        word.alpha = 0f;
        flash.color = Color.clear;
        foreach (var st in stars) st.GetComponent<TextMeshProUGUI>().alpha = 0f;
        for (float t = 0f; t < 1f; t += Time.deltaTime / seconds)
        {
            black.color = new Color(0f, 0f, 0f, 1f - t);
            yield return null;
        }
        black.color = Color.clear;
    }

    public IEnumerator FadeToBlack(float seconds)
    {
        for (float t = 0f; t < 1f; t += Time.deltaTime / seconds)
        {
            black.color = new Color(0f, 0f, 0f, t);
            yield return null;
        }
        black.color = Color.black;
    }
}
