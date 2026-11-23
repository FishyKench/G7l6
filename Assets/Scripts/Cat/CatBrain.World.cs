using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// The cat and the house around her (the rest of CatBrain is her everyday life and the older mischief):
///   Trouble:   swats drinks off tables (smash + spill), steals ingredients while you cook, eats dinner left out,
///              scratches the sofa / curtains (the scratching post saves them), hides in the washing machine.
///   Reactions: panics and hides when there's a fire, swats at the ceiling-leak drips, runs from (or rides!) the robot vacuum,
///              watches birds at the window.
///   Chain:     walks through water -> wet paw prints round the house.
/// Shoo her (E) to stop most of it.
/// </summary>
public partial class CatBrain
{
    /// <summary>True while the cat's curled up inside the washing machine (you can't load it).</summary>
    public static bool InWasher => Instance && Instance.mood == Mood.InWasher;

    float rear;                 // 0..1: up on her back legs (scratching)
    bool offMap;                // inside the washer / on the vacuum (the walk map's off)
    RobotVacuum riding;
    Vector3 rideOffset;
    float nextVacuumCheck, nextVacuumReaction, nextSoakCheck, nextWorldWord;
    int wetSteps;
    Vector3 lastPrint;
    bool printLeft;
    float dinnerOutSince = -1f;
    readonly HashSet<FireHazard> panickedAt = new HashSet<FireHazard>();
    string scratchTarget;
    Mug mugTarget;

    // ------------------------------------------------------------------ choosing

    /// <summary>The newer bits of trouble, scored against the older ones (PickMischief).</summary>
    void WorldMischief(ref IEnumerator best, ref float bestScore)
    {
        // a drink on a table she can get up to
        foreach (var mug in Mug.All)
        {
            if (!mug || mug.Falling || !SameFloor(mug.transform.position) || Flat(mug.transform.position - transform.position).magnitude > 14f) continue;
            if (!TopSpot(mug.transform.position, out _)) continue;
            float score = 2.4f * Random.Range(0.5f, 1.5f);
            if (score > bestScore) { bestScore = score; best = KnockMug(mug); }
            break;
        }

        // an ingredient left out while you cook
        var cooking = Cooking.Instance;
        if (cooking && cooking.IsActive)
            foreach (var item in items)
            {
                if (!item || item.type != ItemType.Food || !Available(item)) continue;
                var food = item.GetComponent<Food>();
                if (!food || food.isVessel || food.ing == null || !string.IsNullOrEmpty(food.seasoning)) continue;
                if (player && Vector3.Distance(player.position, item.transform.position) < 1.6f * Size) continue; // not right under your nose
                if (!CanGetTo(item)) continue;
                float score = 2.2f * Random.Range(0.5f, 1.5f);
                if (score > bestScore) { bestScore = score; best = Steal(item); }
            }

        // dinner, sitting out with nobody watching it
        if (cooking && cooking.DinnerOut)
        {
            if (dinnerOutSince < 0f) dinnerOutSince = Time.time;
            bool watched = player && Vector3.Distance(player.position, cooking.DishPoint) < 3f;
            if (Time.time - dinnerOutSince > 30f && !watched && TopSpot(cooking.DishPoint, out _))
            {
                float score = 3.2f * Random.Range(0.5f, 1.5f);
                if (score > bestScore) { bestScore = score; best = EatDinner(cooking); }
            }
        }
        else dinnerOutSince = -1f;

        // the sofa / the curtains (or the post, if you bought one)
        if (ScratchTargetNear(out var target, out var name, out var stand))
        {
            float score = 1.7f * Random.Range(0.5f, 1.5f);
            if (score > bestScore) { bestScore = score; best = Scratch(target, name, stand); }
        }

        // the washing machine, waiting to be loaded
        var laundry = FindObjectOfType<LaundryTask>();
        if (laundry && laundry.WaitingForLoad && WasherSpots(laundry, out var washerFront, out _) && SameFloor(washerFront))
        {
            float score = 2f * Random.Range(0.5f, 1.5f);
            if (score > bestScore) { bestScore = score; best = HideInWasher(laundry); }
        }
    }

    /// <summary>Calmer things to do now and then (instead of wandering): the window, the drips from the leak.</summary>
    IEnumerator WorldIdle()
    {
        foreach (var leak in FindObjectsOfType<CeilingLeakTask>())
            if (leak.IsActive && !leak.BucketUnder && SameFloor(leak.DripPoint) && Random.value < 0.45f && CanReach(leak.DripPoint))
                return SwatDrips(leak);
        if (Random.value < 0.14f && WindowSpot(out var stand, out var window)) return WatchBirds(stand, window);
        return null;
    }

    // ------------------------------------------------------------------ every frame

    void WorldUpdate(float dt)
    {
        // paws in water: wet paw prints
        if (Time.time >= nextSoakCheck && !offMap)
        {
            nextSoakCheck = Time.time + 0.15f;
            var gm = GameManager.Instance;
            bool inWater = false;
            if (gm)
                foreach (var t in gm.Tasks)
                    if (t is FloodHazard f && f.IsActive && f.puddle && Mathf.Abs(f.puddle.position.y - transform.position.y) < 0.35f
                        && Flat(f.puddle.position - transform.position).magnitude < f.puddle.lossyScale.x * 0.45f)
                        inWater = true;
            if (inWater) { wetSteps = 18; lastPrint = transform.position; }
            else if (wetSteps > 0 && !jumping && Flat(transform.position - lastPrint).magnitude > 0.17f * Size)
            {
                Vector3 side = transform.right * (printLeft ? -0.035f : 0.035f) * Size;
                printLeft = !printLeft;
                if (Physics.Raycast(transform.position + side + Vector3.up * 0.25f, Vector3.down, out var hit, 0.6f, ~0, QueryTriggerInteraction.Ignore)
                    && !hit.collider.transform.IsChildOf(transform) && !hit.collider.GetComponentInParent<Carryable>())
                    PawPrints.Add(hit.point, transform.eulerAngles.y, 0.075f * Size, wetSteps / 18f);
                lastPrint = transform.position;
                wetSteps--;
            }
        }

        // a fire: panic
        if (mood != Mood.Running || !panicking)
        {
            var gm = GameManager.Instance;
            if (gm)
                foreach (var t in gm.Tasks)
                    if (t is FireHazard fire && fire.IsActive && !panickedAt.Contains(fire)
                        && Mathf.Abs(fire.transform.position.y - transform.position.y) < 2.2f && Flat(fire.transform.position - transform.position).magnitude < 16f)
                    {
                        panickedAt.Add(fire);
                        React(Panic(fire));
                        return;
                    }
        }
        if (panicking && mood == Mood.Running) BumpThings();

        // the robot vacuum coming her way
        if (Time.time >= nextVacuumCheck && !offMap && !jumping && Time.time >= nextVacuumReaction
            && (mood == Mood.Idle || mood == Mood.Walking || mood == Mood.Napping || mood == Mood.Watching))
        {
            nextVacuumCheck = Time.time + 0.5f;
            var robot = RobotVacuum.Active;
            if (robot && SameFloor(robot.transform.position) && Flat(robot.transform.position - transform.position).magnitude < 0.9f * Size)
            {
                nextVacuumReaction = Time.time + 30f;
                if (robot.Moving && Random.value < 0.35f) React(RideVacuum(robot));
                else React(Spooked(robot.transform.position));
            }
        }

        if (Time.time > nextWorldWord)
        {
            if (mood == Mood.Scratching) { Say("scritch", HudArt.Ink, 1f); nextWorldWord = Time.time + 0.55f; }
            else if (mood == Mood.InWasher) { Say("mew?", HudArt.MarkerRed, 1.1f); nextWorldWord = Time.time + Random.Range(1.8f, 2.6f); }
            else if (mood == Mood.Eating) { Say("nom", HudArt.MarkerRed, 1f); nextWorldWord = Time.time + 0.8f; }
            else if (mood == Mood.Watching) { Say("ek ek ek", HudArt.Ink, 1f); nextWorldWord = Time.time + Random.Range(1.6f, 3f); }
            else if (mood == Mood.Hiding) { Say("hss", HudArt.MarkerRed, 1f); nextWorldWord = Time.time + Random.Range(3f, 5f); }
            else if (mood == Mood.Riding) { Say("!", HudArt.Ink, 1.1f); nextWorldWord = Time.time + Random.Range(2f, 3f); }
        }
    }

    /// <summary>Riding the vacuum: she goes where it goes (LateUpdate, before the bone tweaks).</summary>
    void WorldMove()
    {
        if (!riding || !offMap) return;
        transform.position = riding.TopPoint + rideOffset;
        transform.rotation = Quaternion.Slerp(transform.rotation, riding.transform.rotation, Time.deltaTime * 3f);
    }

    // ---- the paw: a front leg lifted and batted sideways (her model only has walk / run / idle: this goes on top)

    Transform pawL, pawR;
    bool pawBones;
    float pawStart = -10f, pawLength = 0.3f;
    bool pawRight, pawHigh;

    /// <summary>A swipe with a front paw: lift, bat sideways, back down ('high': reaching up, e.g. at drips).</summary>
    void PawSwipe(bool right, float length = 0.32f, bool high = false)
    {
        pawRight = right;
        pawLength = Mathf.Max(0.12f, length);
        pawHigh = high;
        pawStart = Time.time;
    }

    void WorldPaws()
    {
        if (!pawBones) { pawL = FindBone("front_thigh.L"); pawR = FindBone("front_thigh.R"); pawBones = true; }
        if (!pawL || !pawR) return;
        Vector3 right = transform.right, up = transform.up;
        if (mood == Mood.Scratching && rear > 0.3f)
        {
            // up on her back legs: both paws raking up and down, in turn
            float t = Time.time * 11f;
            foreach (var (bone, phase) in new[] { (pawL, 0f), (pawR, Mathf.PI) })
            {
                float lift = (-55f - 30f * (0.5f + 0.5f * Mathf.Sin(t + phase))) * rear;
                bone.rotation = Quaternion.AngleAxis(lift, right) * bone.rotation;
            }
            return;
        }
        float u = (Time.time - pawStart) / pawLength;
        if (u < 0f || u > 1f) return;
        float lift01 = u < 0.35f ? Mathf.SmoothStep(0f, 1f, u / 0.35f) : u < 0.7f ? 1f : 1f - Mathf.SmoothStep(0f, 1f, (u - 0.7f) / 0.3f);
        float sweep01 = u < 0.35f ? 0f : u < 0.6f ? Mathf.SmoothStep(0f, 1f, (u - 0.35f) / 0.25f) : 1f;
        var paw = pawRight ? pawR : pawL;
        float liftAngle = -(pawHigh ? 115f : 72f) * lift01;
        float sweepAngle = (pawRight ? 1f : -1f) * 42f * sweep01 * lift01; // across, towards her middle
        paw.rotation = Quaternion.AngleAxis(sweepAngle, up) * Quaternion.AngleAxis(liftAngle, right) * paw.rotation;
    }

    /// <summary>Pose tweaks on top of the animation (in LateUpdate, inside the body bone's tweak): rearing up to scratch, the paws.</summary>
    void WorldPose(float s)
    {
        rear = Mathf.MoveTowards(rear, mood == Mood.Scratching ? 1f : 0f, Time.deltaTime * 4f);
        if (rear > 0.01f && bodyBone)
        {
            // up on the back legs: tip the body back round the hind feet, paws working up and down
            Vector3 pivot = transform.position - transform.forward * 0.16f * s;
            float angle = -rear * (38f + Mathf.Sin(Time.time * 18f) * 4f);
            var turn = Quaternion.AngleAxis(angle, transform.right);
            bodyBone.position = pivot + turn * (bodyBone.position - pivot);
            bodyBone.rotation = turn * bodyBone.rotation;
        }
        WorldPaws();
    }

    /// <summary>E on the cat during the newer trouble: she stops. True if it was handled.</summary>
    bool WorldShoo()
    {
        switch (mood)
        {
            case Mood.InWasher:
                GameHUD.Toast($"You lift {catName} out of the washer.", GameHUD.Info);
                StopAllCoroutines(); // (not React: that would drop her straight onto the floor - she hops out)
                StartCoroutine(ThenCarryOn(LeaveWasherThen(RunFrom(PlayerPos(), 4f))));
                return true;
            case Mood.Riding:
                GameHUD.Toast($"{catName} hops off the robot.", GameHUD.Info);
                StopAllCoroutines();
                StartCoroutine(ThenCarryOn(GetOffVacuum()));
                return true;
            case Mood.Scratching:
            case Mood.Eating:
            case Mood.Stalking:
                GameHUD.Toast($"{catName} scampers off.", GameHUD.Info);
                React(RunFrom(PlayerPos(), 5f));
                return true;
        }
        return false;
    }

    // ------------------------------------------------------------------ the drinks

    IEnumerator KnockMug(Mug mug)
    {
        if (!TopSpot(mug.transform.position, out var spot)) yield break;
        mugTarget = mug;
        mood = Mood.Walking;
        yield return WalkTo(spot, walkSpeed, 0.35f * Size, 25f);
        if (!mug || mug.Falling || Flat(transform.position - mug.transform.position).magnitude > 0.8f * Size
            || Mathf.Abs(transform.position.y - mug.transform.position.y) > 0.3f) { mugTarget = null; yield break; }

        // where it'll land: the floor beside the furniture
        if (!FindFloorSpot(mug.transform.position, 1.6f, false, out var land)) { mugTarget = null; yield break; }
        mood = Mood.Stalking;
        crouchTarget = 0.6f;
        yield return Face(mug.transform.position, 0.4f);
        yield return new WaitForSeconds(0.6f); // the look
        // tap... tap... towards the edge
        Vector3 edge = Flat(land - mug.transform.position).normalized;
        for (int i = 0; i < 2; i++)
        {
            if (!mug || mug.Falling) { crouchTarget = 0f; mugTarget = null; yield break; }
            crouchTarget = 0.3f;
            bool rightPaw = Vector3.Dot(edge, transform.right) >= 0f;
            PawSwipe(rightPaw, 0.32f);
            yield return new WaitForSeconds(0.12f); // (the paw comes up first)
            Say("tap", HudArt.Ink, 1f);
            Vector3 from = mug ? mug.transform.position : Vector3.zero, to = from + edge * 0.05f * Size;
            for (float t = 0f; t < 0.14f; t += Time.deltaTime) { if (mug) mug.transform.position = Vector3.Lerp(from, to, t / 0.14f); yield return null; }
            crouchTarget = 0.6f;
            yield return new WaitForSeconds(Random.Range(0.5f, 0.9f));
        }
        if (!mug || mug.Falling) { crouchTarget = 0f; mugTarget = null; yield break; }
        // ...and over it goes
        crouchTarget = 0f;
        mood = Mood.Playing;
        PawSwipe(Vector3.Dot(edge, transform.right) >= 0f, 0.36f); // the big swipe
        yield return new WaitForSeconds(0.15f);
        if (mug) StartCoroutine(mug.Fall(land));
        mugTarget = null;
        yield return Face(land, 0.25f); // look down at what she did
        yield return new WaitForSeconds(1.2f);
        yield return RunFrom(land, 4f);
        yield return Idle(Random.Range(1.5f, 3f));
    }

    // ------------------------------------------------------------------ dinner

    IEnumerator EatDinner(Cooking cooking)
    {
        if (!TopSpot(cooking.DishPoint, out var spot)) yield break;
        mood = Mood.Walking;
        yield return WalkTo(spot, walkSpeed, 0.3f * Size, 25f);
        if (!cooking.DinnerOut || Flat(transform.position - cooking.DishPoint).magnitude > 0.9f * Size) yield break;
        yield return Face(cooking.DishPoint, 0.4f);
        mood = Mood.Eating;
        crouchTarget = 0.7f; // head in the dish
        nextWorldWord = 0f;
        float until = Time.time + 7f;
        while (Time.time < until && mood == Mood.Eating && cooking.DinnerOut) yield return null;
        crouchTarget = 0f;
        if (mood != Mood.Eating || !cooking.DinnerOut) yield break;
        cooking.CatAteDinner();
        Say("burp", HudArt.MarkerRed, 1.4f);
        mood = Mood.Purring;
        nextWord = 0f;
        yield return new WaitForSeconds(2.5f);
        yield return RunFrom(cooking.DishPoint, 4f);
        yield return Idle(2f);
    }

    // ------------------------------------------------------------------ scratching

    bool ScratchTargetNear(out Bounds target, out string name, out Vector3 stand)
    {
        target = default; name = null; stand = default;
        var post = ScratchingPost.Instance;
        if (post && Random.value < 0.75f && SameFloor(post.StandSpot) && CanReach(post.StandSpot))
        {
            target = new Bounds(post.Post, Vector3.one * 0.2f);
            name = "scratching post";
            stand = post.StandSpot;
            return true;
        }
        var options = new List<(Bounds b, string n)>();
        foreach (var t in ScratchThings())
        {
            if (!t) continue;
            var rs = t.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) continue;
            var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
            if (b.min.y - transform.position.y > 0.5f || transform.position.y - b.min.y > 1.2f) continue; // her floor
            if (Flat(b.center - transform.position).magnitude > 15f) continue;
            string n = t.name.ToLowerInvariant().Contains("curtain") ? "curtains" : "sofa";
            if (Incidents.Has($"The cat scratched up the {n}") && Random.value < 0.7f) continue;
            options.Add((b, n));
        }
        for (int i = 0; i < options.Count && i < 4; i++)
        {
            var (b, n) = options[Random.Range(0, options.Count)];
            Vector3 near = b.ClosestPoint(new Vector3(transform.position.x, b.min.y + 0.2f, transform.position.z));
            if (!FindFloorSpot(new Vector3(near.x, transform.position.y, near.z), 1.05f, true, out var spot)) continue;
            target = b; name = n; stand = spot;
            return true;
        }
        return false;
    }

    static List<Transform> scratchThings;

    static List<Transform> ScratchThings()
    {
        if (scratchThings != null && scratchThings.TrueForAll(t => t)) return scratchThings;
        scratchThings = new List<Transform>();
        foreach (var t in FindObjectsOfType<Transform>())
        {
            string n = t.name;
            bool sofa = (n == "Sofa" || n.StartsWith("Sofa ")) && t.GetComponentInChildren<Renderer>() && !n.Contains("Pillow");
            bool curtain = n.StartsWith("Curtains") && t.GetComponentInChildren<Renderer>();
            if (sofa || curtain) scratchThings.Add(t);
        }
        return scratchThings;
    }

    IEnumerator Scratch(Bounds target, string name, Vector3 stand)
    {
        mood = Mood.Walking;
        yield return WalkTo(stand, walkSpeed, 0.12f, 25f);
        if (Flat(transform.position - stand).magnitude > 0.5f * Size) yield break;
        Vector3 at = target.ClosestPoint(transform.position + Vector3.up * 0.3f * Size);
        yield return Face(new Vector3(target.center.x, transform.position.y, target.center.z), 0.4f); // (towards it, even right up against it)
        mood = Mood.Scratching;
        scratchTarget = name;
        nextWorldWord = 0f;
        float until = Time.time + Random.Range(6f, 8f);
        while (Time.time < until && mood == Mood.Scratching) yield return null;
        bool finished = mood == Mood.Scratching;
        scratchTarget = null;
        if (!finished) yield break;
        mood = Mood.Idle;
        if (name != "scratching post")
        {
            ScratchMarks(at, target);
            Incidents.Add($"The cat scratched up the {name}", $"Look at the {name.ToUpperInvariant()}! Shredded!", 8, at);
            GameHUD.Toast($"{catName} shredded the {name}!", GameHUD.Bad);
        }
        else Say("purr", HudArt.MarkerRed, 1.1f);
        yield return Idle(Random.Range(1.5f, 3f));
    }

    /// <summary>Claw marks where she scratched: a few pale, torn streaks on the fabric.</summary>
    void ScratchMarks(Vector3 at, Bounds b)
    {
        Vector3 toCat = Flat(transform.position - b.center);
        Vector3 n = Mathf.Abs(toCat.x) / Mathf.Max(0.01f, b.extents.x) > Mathf.Abs(toCat.z) / Mathf.Max(0.01f, b.extents.z)
            ? new Vector3(Mathf.Sign(toCat.x), 0f, 0f) : new Vector3(0f, 0f, Mathf.Sign(toCat.z));
        var mat = new Material(Shader.Find("Standard")) { color = new Color(0.86f, 0.8f, 0.7f) };
        var root = new GameObject("Claw marks").transform;
        root.position = at;
        Vector3 surface = new Vector3(n.x != 0 ? (n.x > 0 ? b.max.x : b.min.x) : at.x, 0f, n.z != 0 ? (n.z > 0 ? b.max.z : b.min.z) : at.z);
        Vector3 side = Vector3.Cross(Vector3.up, n);
        for (int i = 0; i < 4; i++)
        {
            var m = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(m.GetComponent<Collider>());
            m.transform.SetParent(root, true);
            float h = Mathf.Clamp(b.min.y + 0.32f * Size, b.min.y + 0.1f, b.max.y - 0.05f);
            m.transform.position = new Vector3(surface.x, h, surface.z) + side * (i - 1.5f) * 0.022f * Size + n * 0.004f;
            m.transform.rotation = Quaternion.LookRotation(n) * Quaternion.Euler(0f, 0f, Random.Range(-8f, 8f));
            m.transform.localScale = new Vector3(0.006f, 0.12f * Size * Random.Range(0.8f, 1.15f), 0.004f);
            m.GetComponent<Renderer>().sharedMaterial = mat;
        }
    }

    // ------------------------------------------------------------------ the washing machine

    bool WasherSpots(LaundryTask laundry, out Vector3 front, out Vector3 inside)
    {
        front = inside = default;
        var machine = laundry.shakeTarget ? laundry.shakeTarget : laundry.transform;
        var rs = machine.GetComponentsInChildren<Renderer>();
        if (rs.Length == 0) return false;
        var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
        Vector3 fwd = Flat(laundry.transform.forward).normalized;
        float depth = Mathf.Abs(Vector3.Dot(b.extents, new Vector3(Mathf.Abs(fwd.x), 0f, Mathf.Abs(fwd.z))));
        // (the machine's door is closed in the model: she's in with her back end at the door - her tail hangs out)
        inside = new Vector3(b.center.x, b.min.y + b.size.y * 0.3f, b.center.z) + fwd * (depth - 0.2f * Size);
        return FindFloorSpot(new Vector3(b.center.x, b.min.y, b.center.z) + fwd * (depth + 0.35f * Size), 1f, true, out front);
    }

    IEnumerator HideInWasher(LaundryTask laundry)
    {
        if (!WasherSpots(laundry, out var front, out var inside)) yield break;
        mood = Mood.Walking;
        yield return WalkTo(front, walkSpeed, 0.1f, 25f);
        if (!laundry.WaitingForLoad || Flat(transform.position - front).magnitude > 0.5f * Size) yield break;
        yield return Face(inside, 0.3f);
        crouchTarget = 0.8f;
        yield return new WaitForSeconds(0.4f);
        crouchTarget = 0f;
        // hop in, then turn round to peek out
        offMap = true;
        agent.enabled = false;
        yield return Hop(transform.position, inside, 0.25f * Size, 0.4f);
        transform.rotation = Quaternion.LookRotation(Flat(inside - front)); // facing in: just her tail sticks out
        mood = Mood.InWasher;
        loafTarget = 0.7f;
        nextWorldWord = Time.time + 0.6f;
        GameHUD.Toast($"{catName} climbed into the washing machine!", GameHUD.Warn);
        float until = Time.time + Random.Range(35f, 50f);
        while (Time.time < until && mood == Mood.InWasher) yield return null;
        if (mood == Mood.InWasher) yield return LeaveWasherThen(Idle(1f));
    }

    IEnumerator LeaveWasherThen(IEnumerator after)
    {
        loafTarget = 0f;
        var laundry = FindObjectOfType<LaundryTask>();
        Vector3 front = transform.position + transform.forward * 0.5f * Size;
        if (laundry && WasherSpots(laundry, out var f, out _)) front = f;
        if (offMap) yield return Hop(transform.position, front, 0.2f * Size, 0.35f);
        BackOnMap(front);
        mood = Mood.Idle;
        yield return after;
    }

    /// <summary>An arc from a to b with the walk map off (into the washer, onto the vacuum...).</summary>
    IEnumerator Hop(Vector3 a, Vector3 b, float height, float time)
    {
        for (float t = 0f; t < time; t += Time.deltaTime)
        {
            float k = t / time;
            transform.position = Vector3.Lerp(a, b, k) + Vector3.up * Mathf.Sin(k * Mathf.PI) * height;
            yield return null;
        }
        transform.position = b;
    }

    void BackOnMap(Vector3 near)
    {
        offMap = false;
        riding = null;
        if (NavMesh.SamplePosition(near, out var hit, 1.5f, NavMesh.AllAreas)) near = hit.position;
        transform.position = near;
        agent.enabled = true;
        if (agent.isOnNavMesh) agent.Warp(near);
    }

    // ------------------------------------------------------------------ reactions

    bool panicking;

    /// <summary>A fire: she yowls, bolts the other way (bumping into things), then hides until it's out.</summary>
    IEnumerator Panic(FireHazard fire)
    {
        if (offMap) yield return LeaveWasherThen(null);
        panicking = true;
        Say("MRRAOW!", HudArt.MarkerRed, 1.8f);
        GameHUD.Toast($"{catName} panics at the fire!", GameHUD.Warn);
        mood = Mood.Running;
        for (int i = 0; i < 2; i++)
            yield return WalkTo(FarPoint(fire.transform.position, 6f + i * 2f), runSpeed * 1.2f, 0.4f, 6f);
        panicking = false;
        mood = Mood.Hiding;
        crouchTarget = 1f;
        loafTarget = 0.4f;
        float until = Time.time + 40f;
        while (Time.time < until && fire && fire.IsActive) yield return null;
        crouchTarget = 0f;
        loafTarget = 0f;
        yield return Idle(2f);
    }

    /// <summary>Running in a panic: knocks into things on the floor.</summary>
    void BumpThings()
    {
        foreach (var item in items)
        {
            if (!item || !Available(item) || !item.gameObject.activeInHierarchy) continue;
            Vector3 d = item.transform.position - transform.position;
            if (Mathf.Abs(d.y) > 0.4f || Flat(d).magnitude > 0.35f * Size) continue;
            Vector3 push = transform.position + (Flat(d).normalized + transform.forward).normalized * 0.7f * Size;
            if (!NavMesh.SamplePosition(push, out var hit, 0.6f, NavMesh.AllAreas) || !IsFloorSpot(hit.position)) continue;
            item.PlaceAt(hit.position, Random.Range(0f, 360f));
            FloatingWord.Spawn("THUD", hit.position + Vector3.up * 0.3f * Size, HudArt.Ink, font, 1.2f, 1f);
        }
    }

    IEnumerator SwatDrips(CeilingLeakTask leak)
    {
        Vector3 drip = leak.DripPoint;
        mood = Mood.Walking;
        yield return WalkTo(drip, walkSpeed, 0.35f * Size, 20f);
        if (!leak.IsActive || Flat(transform.position - drip).magnitude > 0.9f * Size) yield break;
        yield return Face(drip, 0.3f);
        mood = Mood.Playing;
        for (int i = 0; i < 4 && leak.IsActive && !leak.BucketUnder; i++)
        {
            crouchTarget = 0.7f;
            yield return new WaitForSeconds(Random.Range(0.5f, 1.1f));
            crouchTarget = 0f;
            PawSwipe(i % 2 == 0, 0.4f, true);
            yield return Pounce(transform.position, 0.32f);
            Say(i % 2 == 0 ? "!" : "?", HudArt.MarkerRed, 1.2f);
        }
        yield return Idle(1.5f);
    }

    IEnumerator Spooked(Vector3 from)
    {
        Say("hss!", HudArt.MarkerRed, 1.5f);
        crouchTarget = 0.9f;
        yield return new WaitForSeconds(0.35f);
        crouchTarget = 0f;
        yield return RunFrom(from, 5f);
        yield return Idle(2f);
    }

    IEnumerator RideVacuum(RobotVacuum robot)
    {
        yield return Face(robot.transform.position, 0.25f);
        crouchTarget = 0.8f;
        yield return new WaitForSeconds(0.4f);
        crouchTarget = 0f;
        if (!robot) yield break;
        offMap = true;
        agent.enabled = false;
        yield return Hop(transform.position, robot.TopPoint, 0.25f * Size, 0.35f);
        riding = robot;
        rideOffset = Vector3.zero;
        mood = Mood.Riding;
        loafTarget = 0.5f;
        GameHUD.Toast($"{catName} is riding the robot vacuum!", GameHUD.Info);
        float until = Time.time + Random.Range(10f, 16f);
        while (Time.time < until && mood == Mood.Riding && robot && robot.Moving) yield return null;
        if (mood == Mood.Riding) yield return GetOffVacuum();
    }

    IEnumerator GetOffVacuum()
    {
        loafTarget = 0f;
        Vector3 off = transform.position + transform.right * 0.5f * Size;
        if (NavMesh.SamplePosition(off, out var hit, 1f, NavMesh.AllAreas)) off = hit.position;
        riding = null;
        if (offMap) yield return Hop(transform.position, off, 0.2f * Size, 0.3f);
        BackOnMap(off);
        mood = Mood.Idle;
        yield return Idle(1f);
    }

    /// <summary>A floor spot in front of a window on her floor (and the window).</summary>
    bool WindowSpot(out Vector3 stand, out Vector3 window)
    {
        stand = window = default;
        var candidates = new List<Bounds>();
        foreach (var t in Windows())
        {
            if (!t) continue;
            var r = t.GetComponentInChildren<Renderer>();
            if (!r) continue;
            var b = r.bounds;
            if (b.min.y < transform.position.y - 0.3f || b.min.y > transform.position.y + 2f) continue; // her floor, and not a door
            if (Flat(b.center - transform.position).magnitude > 14f) continue;
            candidates.Add(b);
        }
        for (int i = 0; i < 3 && candidates.Count > 0; i++)
        {
            var b = candidates[Random.Range(0, candidates.Count)];
            Vector3 thin = b.size.x < b.size.z ? Vector3.right : Vector3.forward;
            Vector3 inward = thin * Mathf.Sign(Vector3.Dot(transform.position - b.center, thin));
            Vector3 p = new Vector3(b.center.x, transform.position.y, b.center.z) + inward * 0.7f;
            if (!FindFloorSpot(p, 0.8f, true, out var spot)) continue;
            stand = spot;
            window = b.center;
            return true;
        }
        return false;
    }

    static List<Transform> windows;

    static List<Transform> Windows()
    {
        if (windows != null && windows.TrueForAll(t => t)) return windows;
        windows = new List<Transform>();
        foreach (var t in FindObjectsOfType<Transform>())
            if (t.name.StartsWith("Window ") && t.parent && t.parent.name == "Walls") windows.Add(t);
        return windows;
    }

    IEnumerator WatchBirds(Vector3 stand, Vector3 window)
    {
        mood = Mood.Walking;
        yield return WalkTo(stand, walkSpeed, 0.15f, 20f);
        if (Flat(transform.position - stand).magnitude > 0.6f * Size) yield break;
        yield return Face(window, 0.5f);
        mood = Mood.Watching;
        loafTarget = 0.35f;
        nextWorldWord = Time.time + 1.2f;
        float until = Time.time + Random.Range(9f, 15f);
        while (Time.time < until && mood == Mood.Watching) yield return null;
        loafTarget = 0f;
        if (mood == Mood.Watching) yield return Idle(1f);
    }

    // ------------------------------------------------------------------ debug (the F1 panel)

    /// <summary>Starts one of the newer behaviours right now. Returns what happened (for the panel).</summary>
    public string DebugDo(string what)
    {
        if (!ready) return "The cat isn't ready.";
        if (offMap) BackOnMap(transform.position);
        var gm = GameManager.Instance;
        switch (what)
        {
            case "mug":
            {
                Mug best = null; float bestD = float.MaxValue;
                foreach (var m in Mug.All)
                    if (m && !m.Falling && SameFloor(m.transform.position) && TopSpot(m.transform.position, out _))
                    {
                        float d = Vector3.Distance(m.transform.position, transform.position);
                        if (d < bestD) { bestD = d; best = m; }
                    }
                if (!best) foreach (var m in Mug.All) if (m && !m.Falling) { best = m; break; }
                if (!best) return "No drinks out (Put out a drink first).";
                DebugNear(best.transform.position);
                React(KnockMug(best));
                return "Going for the drink.";
            }
            case "food":
                foreach (var item in FindObjectsOfType<Carryable>())
                {
                    var food = item.GetComponent<Food>();
                    if (!food || food.isVessel || food.ing == null || !Available(item)) continue;
                    DebugNear(item.transform.position);
                    if (!CanGetTo(item)) continue;
                    React(Steal(item));
                    return $"Going for the {item.displayName}.";
                }
                return "No ingredient out on a counter (take one out of the fridge first).";
            case "dinner":
                if (!Cooking.Instance || !Cooking.Instance.DinnerOut) return "Dinner isn't made yet.";
                DebugNear(Cooking.Instance.DishPoint);
                if (!TopSpot(Cooking.Instance.DishPoint, out _)) return "She can't get up to where dinner is.";
                React(EatDinner(Cooking.Instance));
                return "Going for dinner!";
            case "scratch":
                if (!ScratchTargetNear(out var b, out var n, out var stand)) return "Nothing to scratch near her.";
                DebugNear(stand);
                React(Scratch(b, n, stand));
                return $"Off to scratch the {n}.";
            case "washer":
            {
                var laundry = FindObjectOfType<LaundryTask>();
                if (!laundry) return "No laundry in this house.";
                if (!laundry.IsActive && gm) gm.StartTask(laundry);
                if (!laundry.WaitingForLoad) return "The washer isn't waiting to be loaded.";
                var machine = laundry.shakeTarget ? laundry.shakeTarget : laundry.transform;
                DebugNear(machine.position + Flat(laundry.transform.forward).normalized * 0.8f);
                if (!WasherSpots(laundry, out _, out _)) return "She can't get to the washer.";
                React(HideInWasher(laundry));
                return "Climbing into the washer.";
            }
            case "window":
                if (!WindowSpot(out var spot, out var window)) return "No window she can get to on her floor.";
                DebugNear(spot);
                React(WatchBirds(spot, window));
                return "Off to watch the birds.";
            case "drips":
            {
                var leak = FindObjectOfType<CeilingLeakTask>();
                if (!leak) return "No ceiling leak in this house.";
                if (!leak.IsActive && gm) gm.StartTask(leak);
                DebugNear(leak.DripPoint);
                React(SwatDrips(leak));
                return "Going to swat the drips.";
            }
            case "fire":
            {
                FireHazard fire = null;
                foreach (var f in FindObjectsOfType<FireHazard>(true)) { fire = f; break; }
                if (!fire) return "No fire in this house.";
                if (!fire.IsActive && gm) gm.StartTask(fire);
                panickedAt.Add(fire);
                React(Panic(fire));
                return "FIRE! She panics.";
            }
            case "vacuum":
                if (!RobotVacuum.Active) return "No robot vacuum - buy it in the shop first.";
                React(RideVacuum(RobotVacuum.Active));
                return "Hopping onto the robot.";
            case "wet":
            {
                var spill = Spill.Create(transform.position, 0.9f * Size, "test water");
                React(Zoomies());
                return "Water under her paws - watch the prints.";
            }
        }
        return "?";
    }

    /// <summary>Debug: if she's far from 'target' (or on another floor), pop her a couple of metres from it so you don't wait.</summary>
    void DebugNear(Vector3 target)
    {
        if (!agent.isOnNavMesh) return;
        if (Mathf.Abs(target.y - transform.position.y) < 1.2f && Flat(target - transform.position).magnitude < 4f) return;
        if (!FindFloorSpot(target, 1.6f, false, out var goal)) return;
        for (int i = 0; i < 24; i++)
        {
            Vector2 r = Random.insideUnitCircle.normalized * Random.Range(1.2f, 2.4f);
            if (NavMesh.SamplePosition(goal + new Vector3(r.x, 0f, r.y), out var spot, 0.8f, NavMesh.AllAreas)
                && Mathf.Abs(spot.position.y - goal.y) < 0.5f && IsFloorSpot(spot.position) && Connected(spot.position, goal))
            {
                agent.Warp(spot.position);
                return;
            }
        }
        agent.Warp(goal);
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>A spot she can reach up on the furniture at the height of 'point' (next to a drink, the dinner...).</summary>
    bool TopSpot(Vector3 point, out Vector3 spot)
    {
        spot = point;
        if (!NavMesh.SamplePosition(point, out var hit, 0.6f * Size, NavMesh.AllAreas)) return false;
        if (Mathf.Abs(hit.position.y - point.y) > 0.3f) return false;
        if (!CanReach(hit.position)) return false;
        spot = hit.position;
        return true;
    }
}
