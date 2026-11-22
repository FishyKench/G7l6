using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Mom (a placeholder figure until there's a real model). At 6 PM she comes in through the garage and walks
/// to each thing that's still wrong, nearest first. She only counts it if it's STILL wrong when she gets
/// there - so you can keep fixing things in rooms she hasn't reached yet. Then she comes to find you.
/// Uses the cat's walk map to get around (stairs included).
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
    float bob;
    bool stopped;
    readonly HashSet<string> seen = new HashSet<string>();

    /// <summary>Spawns Mom in the garage (or by the front door). Null if there's no house layout.</summary>
    public static MomInspector Spawn(DayManager day)
    {
        var layout = HouseLayout.Instance;
        if (!layout) return null;
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

    void BuildFigure(float scale)
    {
        float s = Mathf.Max(0.8f, scale * 0.9f); // about as tall as you
        body = new GameObject("Body").transform;
        body.SetParent(transform, false);
        var coat = Mat(new Color(0.55f, 0.27f, 0.52f));
        var dark = Mat(new Color(0.16f, 0.15f, 0.2f));
        var skin = Mat(new Color(0.96f, 0.8f, 0.68f));
        var hair = Mat(new Color(0.36f, 0.22f, 0.14f));
        var bag = Mat(new Color(0.85f, 0.62f, 0.2f));
        Part(PrimitiveType.Cylinder, "Leg L", new Vector3(-0.09f, 0.42f, 0f), new Vector3(0.11f, 0.42f, 0.11f), dark, s);
        Part(PrimitiveType.Cylinder, "Leg R", new Vector3(0.09f, 0.42f, 0f), new Vector3(0.11f, 0.42f, 0.11f), dark, s);
        Part(PrimitiveType.Capsule, "Coat", new Vector3(0f, 1.12f, 0f), new Vector3(0.44f, 0.45f, 0.3f), coat, s);
        Part(PrimitiveType.Sphere, "Head", new Vector3(0f, 1.62f, 0f), new Vector3(0.26f, 0.3f, 0.26f), skin, s);
        Part(PrimitiveType.Sphere, "Hair", new Vector3(0f, 1.67f, -0.04f), new Vector3(0.29f, 0.3f, 0.28f), hair, s);
        Part(PrimitiveType.Capsule, "Ponytail", new Vector3(0f, 1.52f, -0.17f), new Vector3(0.1f, 0.14f, 0.1f), hair, s);
        Part(PrimitiveType.Cube, "Handbag", new Vector3(0.27f, 0.95f, 0.02f), new Vector3(0.08f, 0.22f, 0.28f), bag, s);
        Part(PrimitiveType.Sphere, "Eye L", new Vector3(-0.055f, 1.65f, 0.12f), Vector3.one * 0.035f, dark, s);
        Part(PrimitiveType.Sphere, "Eye R", new Vector3(0.055f, 1.65f, 0.12f), Vector3.one * 0.035f, dark, s);

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
        agent.agentTypeID = 0; // the cat's walk map is baked for the default agent type
        agent.radius = 0.2f;
        agent.height = 1.8f * s;
        agent.speed = walkSpeed * scale;
        agent.angularSpeed = 600f;
        agent.acceleration = 14f;
        agent.stoppingDistance = 0.9f * scale;
        agent.obstacleAvoidanceType = ObstacleAvoidanceType.NoObstacleAvoidance;
        agent.autoBraking = true;
        if (NavMesh.SamplePosition(transform.position, out var hit, 4f, NavMesh.AllAreas)) agent.Warp(hit.position);
    }

    void Part(PrimitiveType shape, string name, Vector3 pos, Vector3 size, Material m, float s)
    {
        var p = GameObject.CreatePrimitive(shape);
        p.name = name;
        Destroy(p.GetComponent<Collider>());
        p.transform.SetParent(body, false);
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

    void Update()
    {
        if (!agent || !body) return;
        // A little walking bob.
        float speed = agent.velocity.magnitude;
        bob += Time.deltaTime * speed * 4.5f;
        body.localPosition = new Vector3(0f, Mathf.Abs(Mathf.Sin(bob)) * 0.05f * Mathf.Clamp01(speed), 0f);
        body.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(bob) * 3f * Mathf.Clamp01(speed));
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

    /// <summary>The walk: nearest problem first, re-checking the house at every stop.</summary>
    public IEnumerator Inspect()
    {
        float until = Time.time + maxInspection;
        Say("I'm home!", Color.white);
        yield return new WaitForSeconds(1.2f);

        while (!stopped && Time.time < until)
        {
            var next = Nearest(day.CurrentFindings());
            if (next == null) break;
            yield return WalkTo(next.where, until);
            if (stopped) yield break;
            yield return Look(next.where);

            // Is it still like that now that she's standing here?
            foreach (var f in day.CurrentFindings())
            {
                if (Vector3.Distance(Flat(f.where), Flat(transform.position)) > 3.2f * HouseScale()) continue;
                if (Mathf.Abs(f.where.y - transform.position.y) > 2.6f * HouseScale()) continue;
                if (!seen.Add(f.what + Key(f.where))) continue;
                day.caught.Add(f);
                Say(f.line, new Color(1f, 0.35f, 0.3f));
                if (GameHUD.Instance) GameHUD.Toast("Mom: \"" + f.line + "\"", GameHUD.Bad);
                yield return new WaitForSeconds(lookTime);
            }
            seen.Add("visited" + Key(next.where)); // don't walk back to the same spot
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

    DayManager.Finding Nearest(List<DayManager.Finding> findings)
    {
        DayManager.Finding best = null;
        float bestCost = float.MaxValue;
        foreach (var f in findings)
        {
            if (seen.Contains(f.what + Key(f.where)) || seen.Contains("visited" + Key(f.where))) continue;
            // Stairs cost extra: finish a floor before moving on.
            float cost = Vector3.Distance(Flat(f.where), Flat(transform.position)) + Mathf.Abs(f.where.y - transform.position.y) * 4f;
            if (cost < bestCost) { bestCost = cost; best = f; }
        }
        return best;
    }

    IEnumerator WalkTo(Vector3 target, float until)
    {
        if (!agent.isOnNavMesh)
        {
            Teleport(target);
            yield break;
        }
        if (!NavMesh.SamplePosition(target, out var hit, 2.5f * HouseScale(), NavMesh.AllAreas))
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
        if (!stopped && Vector3.Distance(transform.position, hit.position) > 2.5f * HouseScale()) Teleport(hit.position);
    }

    void Teleport(Vector3 target)
    {
        if (NavMesh.SamplePosition(target, out var hit, 3f * HouseScale(), NavMesh.AllAreas) && agent) agent.Warp(hit.position);
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
        FloatingWord.Spawn(line, transform.position + Vector3.up * 2.3f * HouseScale(), color, font, 2.2f, 2.4f);

    static float HouseScale() => HouseLayout.Instance ? HouseLayout.Instance.scale : 1f;
    static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }
    static string Key(Vector3 p) => $"{Mathf.RoundToInt(p.x)},{Mathf.RoundToInt(p.y)},{Mathf.RoundToInt(p.z)}";
}
