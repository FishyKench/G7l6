using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// The robot vacuum (ShopNest gadget): a small low-poly disc that crawls slowly round the ground floor on the
/// cat's walk map (never onto furniture: the jump links are off limits). It roams, and when there is something to
/// do on its floor it goes there:
///   - a spill (FloodHazard): it drives back and forth over the water and slowly dries it (the mop is still faster);
///   - scattered dirty clothes (LaundryTask): it swallows one piece at a time, but always leaves the last one so
///     the player still has an armful to carry to the washer.
/// Keep it modest: slow (0.3 m/s), no collider (it never pushes the player), and it stays on its own floor.
/// </summary>
public class RobotVacuum : MonoBehaviour
{
    const float Speed = 0.3f, DryPerSecond = 0.3f, BrushRadius = 0.28f;
    const int JumpArea = 2; // the cat's furniture jump links

    NavMeshAgent agent;
    NavMeshPath path;
    float homeY;
    Renderer led;
    Material ledIdle, ledBusy;
    readonly HashSet<DirtyClothesPiece> skipped = new HashSet<DirtyClothesPiece>();
    float skipClear;

    /// <summary>Puts a robot on the ground floor. Null if there is no walk map or no free spot.</summary>
    public static RobotVacuum Create()
    {
        var layout = HouseLayout.Instance;
        if (!layout) return null;

        var order = new List<HouseLayout.RoomInfo>();
        foreach (var r in layout.rooms) if (r.floor == 0 && r.name == "Living room") order.Add(r);
        foreach (var r in layout.rooms) if (r.floor == 0 && r.name != "Garage" && !order.Contains(r)) order.Add(r);

        foreach (var room in order)
        {
            for (int tries = 0; tries < 40; tries++)
            {
                if (!RandomFloorSpot(layout, room, out Vector3 spot)) continue;
                return Build(spot);
            }
        }
        Debug.LogWarning("[Shop] No free spot for the robot vacuum.");
        return null;
    }

    /// <summary>A free walk-map spot on the floor of this room (nothing around it, away from doors).</summary>
    static bool RandomFloorSpot(HouseLayout layout, HouseLayout.RoomInfo room, out Vector3 spot)
    {
        spot = default;
        var p = new Vector3(Random.Range(room.rect.xMin + 0.6f, room.rect.xMax - 0.6f), 0f, Random.Range(room.rect.yMin + 0.6f, room.rect.yMax - 0.6f));
        Vector3 world = layout.transform.TransformPoint(p);
        world.y = room.floor * layout.storeyHeight * layout.scale + 0.3f;
        if (!NavMesh.SamplePosition(world, out var hit, 0.8f, NavMesh.AllAreas)) return false;
        float floorY = room.floor * layout.storeyHeight * layout.scale;
        if (Mathf.Abs(hit.position.y - floorY) > 0.15f) return false;
        if (Physics.CheckSphere(hit.position + Vector3.up * 0.25f, 0.24f, ~0, QueryTriggerInteraction.Ignore)) return false;
        foreach (var d in FindObjectsOfType<HouseDoor>())
        {
            Vector3 off = d.transform.position - hit.position;
            off.y = 0f;
            if (off.magnitude < 1.4f) return false;
        }
        spot = hit.position;
        return true;
    }

    static RobotVacuum Build(Vector3 floor)
    {
        var go = new GameObject("Robot vacuum");
        go.transform.SetPositionAndRotation(floor, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
        var t = go.transform;

        var shell = ProcBuild.Mat(new Color(0.9f, 0.91f, 0.94f), 0.5f);
        var dark = ProcBuild.Mat(new Color(0.16f, 0.17f, 0.2f), 0.3f);
        var grey = ProcBuild.Mat(new Color(0.42f, 0.45f, 0.52f), 0.4f);
        var idle = ProcBuild.Mat(new Color(0.2f, 0.85f, 0.5f), 0.2f, new Color(0.1f, 0.9f, 0.4f));
        var busy = ProcBuild.Mat(new Color(0.25f, 0.6f, 1f), 0.2f, new Color(0.15f, 0.5f, 1f) * 1.3f);

        // A 30 cm disc, 7 cm tall (every layer is a different size, so no faces share a plane).
        ProcBuild.Cyl(t, "Underside", new Vector3(0f, 0.012f, 0f), 0.27f, 0.024f, dark);
        ProcBuild.Cyl(t, "Body", new Vector3(0f, 0.045f, 0f), 0.3f, 0.05f, shell);
        ProcBuild.Cyl(t, "Bumper", new Vector3(0f, 0.034f, 0f), 0.316f, 0.03f, dark);
        ProcBuild.Cyl(t, "Top plate", new Vector3(0f, 0.0715f, 0f), 0.25f, 0.011f, grey);
        ProcBuild.Cyl(t, "Button", new Vector3(0f, 0.0785f, -0.02f), 0.07f, 0.011f, shell);
        var ledGo = ProcBuild.Cyl(t, "Light", new Vector3(0f, 0.0725f, 0.095f), 0.026f, 0.015f, idle);
        // A little forward sensor bump on the bumper.
        ProcBuild.Cube(t, "Sensor", new Vector3(0f, 0.04f, 0.1575f), new Vector3(0.07f, 0.014f, 0.008f), grey);

        var agent = go.AddComponent<NavMeshAgent>();
        agent.radius = 0.13f;
        agent.height = 0.1f;
        agent.speed = Speed;
        agent.acceleration = 1.2f;
        agent.angularSpeed = 140f;
        agent.stoppingDistance = 0.05f;
        agent.autoTraverseOffMeshLink = false;
        agent.obstacleAvoidanceType = ObstacleAvoidanceType.NoObstacleAvoidance;
        agent.areaMask = NavMesh.AllAreas & ~(1 << JumpArea);

        var robot = go.AddComponent<RobotVacuum>();
        robot.agent = agent;
        robot.path = new NavMeshPath();
        robot.homeY = floor.y;
        robot.led = ledGo.GetComponent<Renderer>();
        robot.ledIdle = idle;
        robot.ledBusy = busy;
        robot.StartCoroutine(robot.Run());
        return robot;
    }

    void Update()
    {
        if (Time.time > skipClear) { skipClear = Time.time + 60f; skipped.Clear(); }
    }

    IEnumerator Run()
    {
        yield return new WaitForSeconds(Random.Range(3f, 6f));
        while (true)
        {
            while (PauseMenu.IsPaused || !agent.isOnNavMesh) yield return null;
            var flood = FindFlood();
            if (flood) { yield return Dry(flood); continue; }
            var piece = FindPiece();
            if (piece) { yield return Collect(piece); continue; }
            yield return Roam();
        }
    }

    void Busy(bool on)
    {
        if (led) led.sharedMaterial = on ? ledBusy : ledIdle;
    }

    // ------------------------------------------------------------------ jobs

    FloodHazard FindFlood()
    {
        var gm = GameManager.Instance;
        if (!gm) return null;
        foreach (var task in gm.Tasks)
            if (task is FloodHazard f && f.IsActive && f.puddle && Mathf.Abs(f.puddle.position.y - homeY) < 0.5f) return f;
        return null;
    }

    DirtyClothesPiece FindPiece()
    {
        DirtyClothesPiece best = null;
        float bestDist = float.MaxValue;
        foreach (var p in DirtyClothesPiece.All)
        {
            if (!p || skipped.Contains(p) || !p.laundry || !p.laundry.IsActive || p.laundry.PiecesLeft <= 1) continue;
            if (Mathf.Abs(p.transform.position.y - homeY) > 0.4f) continue;
            float d = (p.transform.position - transform.position).sqrMagnitude;
            if (d < bestDist) { bestDist = d; best = p; }
        }
        return best;
    }

    IEnumerator Roam()
    {
        Busy(false);
        var layout = HouseLayout.Instance;
        if (layout)
        {
            var rooms = new List<HouseLayout.RoomInfo>();
            foreach (var r in layout.rooms) if (r.floor == 0 && r.name != "Garage") rooms.Add(r);
            if (rooms.Count > 0 && RandomFloorSpot(layout, rooms[Random.Range(0, rooms.Count)], out Vector3 spot))
                yield return GoTo(spot, 0.1f, 45f, null);
        }
        yield return new WaitForSeconds(Random.Range(1.5f, 4f));
    }

    IEnumerator Dry(FloodHazard flood)
    {
        Busy(true);
        float until = Time.time + 120f;
        while (flood && flood.IsActive && Time.time < until)
        {
            float radius = flood.puddle.lossyScale.x * 0.5f;
            Vector2 r = Random.insideUnitCircle * Mathf.Max(0f, radius * 0.75f - BrushRadius * 0.5f);
            Vector3 target = flood.puddle.position + new Vector3(r.x, 0f, r.y);
            if (NavMesh.SamplePosition(target, out var hit, 0.6f, agent.areaMask)) target = hit.position;
            yield return GoTo(target, 0.08f, 25f, () =>
            {
                if (flood && flood.IsActive) flood.DryAt(transform.position, BrushRadius, DryPerSecond * Time.deltaTime);
            });
            if (flood && flood.IsActive) flood.DryAt(transform.position, BrushRadius, DryPerSecond * 1.5f);
            while (PauseMenu.IsPaused) yield return null;
        }
        Busy(false);
    }

    IEnumerator Collect(DirtyClothesPiece piece)
    {
        Busy(true);
        Vector3 dest = piece.transform.position;
        if (NavMesh.SamplePosition(dest, out var hit, 0.8f, agent.areaMask)) dest = hit.position;
        yield return GoTo(dest, 0.1f, 40f, null);
        if (piece && Vector3.Distance(Flat(piece.transform.position), Flat(transform.position)) < 0.5f)
        {
            // Sucks the piece in: it shrinks into the robot.
            Vector3 from = piece.transform.localScale, start = piece.transform.position;
            for (float t = 0f; t < 1.6f && piece; t += Time.deltaTime)
            {
                float k = Mathf.SmoothStep(0f, 1f, t / 1.6f);
                piece.transform.localScale = from * (1f - k);
                piece.transform.position = Vector3.Lerp(start, transform.position + Vector3.up * 0.05f, k);
                yield return null;
            }
            if (piece && !(piece.laundry && piece.laundry.RobotTake(piece)))
            {
                skipped.Add(piece);
                piece.transform.localScale = from;
                piece.transform.position = start;
            }
        }
        else if (piece) skipped.Add(piece);
        Busy(false);
    }

    /// <summary>Drives to 'dest' along the walk map (off the furniture). 'moving' runs every frame while it drives.</summary>
    IEnumerator GoTo(Vector3 dest, float stop, float timeout, System.Action moving)
    {
        if (!agent.isOnNavMesh) yield break;
        if (!NavMesh.SamplePosition(dest, out var hit, 0.8f, agent.areaMask) || Mathf.Abs(hit.position.y - homeY) > 0.2f) yield break;
        if (!agent.CalculatePath(hit.position, path) || path.status != NavMeshPathStatus.PathComplete) yield break;
        agent.stoppingDistance = stop;
        agent.isStopped = false;
        agent.SetPath(path);

        float giveUp = Time.time + timeout;
        while (Time.time < giveUp)
        {
            if (PauseMenu.IsPaused) { agent.isStopped = true; yield return null; continue; }
            agent.isStopped = false;
            moving?.Invoke();
            if (!agent.pathPending && agent.remainingDistance <= stop + 0.05f) break;
            yield return null;
        }
        if (agent.isOnNavMesh) agent.ResetPath();
    }

    static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }
}
