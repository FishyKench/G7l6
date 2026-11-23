using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Problems you leave alone start new ones:
///   - a flood that spreads to a wall socket shorts it: power cut (see <see cref="Power"/>, the fuse box in the garage);
///   - the ceiling leak, left without a bucket, drips into whatever's under it - a TV under it is fried for good;
///   - (the cat tracks wet paw prints out of any water she walks through - CatBrain does that.)
/// Also puts out the drinks the cat knocks off tables. Added by the GameManager.
/// </summary>
public class ChainReactions : MonoBehaviour
{
    public static ChainReactions Instance { get; private set; }

    [Tooltip("Seconds a ceiling leak drips with no bucket under it before it ruins what's below.")]
    public float leakDamageAfter = 40f;
    [Tooltip("Drinks left out at the start of the day (for the cat to knock off).")]
    public int mugsAtStart = 3;
    [Tooltip("Seconds between new drinks appearing (you made yourself another one), if there are fewer than mugsAtStart.")]
    public float newMugEvery = 210f;

    class Socket
    {
        public Transform t;
        public FloodHazard flood;
        public float distance; // from the puddle's middle (flat)
    }

    readonly Dictionary<FloodHazard, Socket> sockets = new Dictionary<FloodHazard, Socket>();
    readonly HashSet<FloodHazard> checkedFloods = new HashSet<FloodHazard>();
    float nextCheck, nextMug;
    float Scale => HouseLayout.Instance ? HouseLayout.Instance.scale : 1f;

    void Awake() => Instance = this;

    IEnumerator Start()
    {
        Power.Reset();
        yield return new WaitForSeconds(2.5f); // (the cat's furniture map is ready by now)
        for (int i = 0; i < mugsAtStart; i++) PutOutMug(false);
        nextMug = Time.time + newMugEvery;
    }

    void Update()
    {
        if (Time.time < nextCheck) return;
        nextCheck = Time.time + 0.4f;
        var gm = GameManager.Instance;
        if (!gm) return;

        foreach (var task in new List<TaskBase>(gm.Tasks)) // (a power cut adds the fuse box task)
        {
            if (task is FloodHazard flood && !(flood is Spill)) Flood(flood);
            if (task is CeilingLeakTask leak) Leak(leak);
        }

        if (Time.time >= nextMug && Mug.All.Count < mugsAtStart)
        {
            nextMug = Time.time + newMugEvery;
            PutOutMug(true);
        }
    }

    // ------------------------------------------------------------------ floods and sockets

    void Flood(FloodHazard flood)
    {
        if (!flood.IsActive || !flood.puddle) { checkedFloods.Remove(flood); return; }
        if (checkedFloods.Add(flood) && !sockets.ContainsKey(flood)) PlaceSocket(flood);
        if (!sockets.TryGetValue(flood, out var socket) || !socket.t) return;
        float radius = flood.puddle.lossyScale.x * 0.5f;
        if (!Power.Out && radius >= socket.distance) Power.Cut(socket.t.position);
    }

    /// <summary>A wall socket near where the water will spread to (it's what the flood shorts if you leave it).</summary>
    void PlaceSocket(FloodHazard flood)
    {
        Vector3 c = flood.puddle.position;
        float ratio = flood.puddle.localScale.x > 1e-4f ? flood.puddle.lossyScale.x / flood.puddle.localScale.x : 1f;
        float maxR = flood.maxDiameter * ratio * 0.5f;
        float bestD = float.MaxValue;
        RaycastHit best = default;
        for (int i = 0; i < 24; i++)
        {
            Vector3 dir = Quaternion.Euler(0f, i * 15f, 0f) * Vector3.forward;
            if (!Physics.Raycast(c + Vector3.up * 0.3f * Scale, dir, out var hit, maxR * 0.92f, ~0, QueryTriggerInteraction.Ignore)) continue;
            if (Mathf.Abs(hit.normal.y) > 0.3f || hit.collider.bounds.size.y < 1.5f * Scale) continue; // a wall, not a cupboard
            if (hit.collider.GetComponentInParent<HouseDoor>() || hit.collider.GetComponentInParent<Carryable>()) continue;
            if (hit.distance < 0.6f * Scale) continue; // (right by the middle: it'd short straight away)
            if (hit.distance < bestD) { bestD = hit.distance; best = hit; }
        }
        if (bestD == float.MaxValue) return; // no wall near enough: this flood can't reach a socket
        Vector3 n = new Vector3(best.normal.x, 0f, best.normal.z).normalized;
        Vector3 at = new Vector3(best.point.x, c.y + 0.3f * Scale, best.point.z) + n * 0.012f * Scale;
        var socket = BuildSocket(at, n);
        Vector3 flat = best.point - c; flat.y = 0f;
        sockets[flood] = new Socket { t = socket, flood = flood, distance = flat.magnitude };
    }

    Transform BuildSocket(Vector3 at, Vector3 normal)
    {
        var go = new GameObject("Wall socket");
        go.transform.position = at;
        go.transform.rotation = Quaternion.LookRotation(normal);
        float s = Scale;
        var white = new Material(Shader.Find("Standard")) { color = new Color(0.94f, 0.94f, 0.92f) };
        var dark = new Material(Shader.Find("Standard")) { color = new Color(0.1f, 0.1f, 0.12f) };
        Cube(go.transform, Vector3.zero, new Vector3(0.085f, 0.085f, 0.012f), white, s);
        Cube(go.transform, new Vector3(-0.016f, 0.008f, 0.0065f), new Vector3(0.008f, 0.018f, 0.002f), dark, s);
        Cube(go.transform, new Vector3(0.016f, 0.008f, 0.0065f), new Vector3(0.008f, 0.018f, 0.002f), dark, s);
        Cube(go.transform, new Vector3(0f, -0.018f, 0.0065f), new Vector3(0.008f, 0.012f, 0.002f), dark, s);
        return go.transform;
    }

    static void Cube(Transform parent, Vector3 pos, Vector3 size, Material m, float s)
    {
        var p = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Destroy(p.GetComponent<Collider>());
        p.transform.SetParent(parent, false);
        p.transform.localPosition = pos * s;
        p.transform.localScale = size * s;
        p.GetComponent<Renderer>().sharedMaterial = m;
    }

    /// <summary>Is the socket that shorted still standing in water (resetting the fuse would just trip it again)?</summary>
    public static bool SocketStillWet()
    {
        if (!Instance) return false;
        foreach (var kv in Instance.sockets)
        {
            var f = kv.Key;
            if (!f || !f.IsActive || !kv.Value.t || !f.puddle) continue;
            if (Vector3.Distance(kv.Value.t.position, Power.ShortedAt) > 0.3f) continue;
            if (f.puddle.lossyScale.x * 0.5f >= kv.Value.distance * 0.9f) return true;
        }
        return false;
    }

    // ------------------------------------------------------------------ the ceiling leak

    readonly HashSet<CeilingLeakTask> ruined = new HashSet<CeilingLeakTask>();

    void Leak(CeilingLeakTask leak)
    {
        if (!leak.IsActive || leak.BucketUnder || ruined.Contains(leak)) return;
        if (Time.time - leak.ActivatedAt < leakDamageAfter) return;
        Ruin(leak);
    }

    /// <summary>The drips have been going too long: whatever's under them is ruined (debug: right now).</summary>
    public void Ruin(CeilingLeakTask leak)
    {
        if (ruined.Contains(leak)) return;
        ruined.Add(leak);
        Vector3 drip = leak.DripPoint;
        float s = Scale;

        // a TV under the drips: fried
        foreach (var tv in FindObjectsOfType<TvTask>())
        {
            if (tv.Broken || !tv.screenRenderer) continue;
            var b = tv.screenRenderer.bounds;
            Vector3 d = b.center - drip; float dy = d.y; d.y = 0f;
            if (d.magnitude > 1.6f * s || dy < -0.2f || dy > 2.4f * s) continue;
            tv.Break();
            Incidents.Add($"{tv.taskName} - fried by the leak", "The TV is RUINED! Water got in it!", 20, b.center);
            GameHUD.Toast($"The leak dripped into the {tv.taskName.ToLowerInvariant()} - it's fried!", GameHUD.Bad);
            return;
        }

        // otherwise, whatever furniture the drips land on gets soaked
        if (Physics.Raycast(drip + Vector3.up * 2.2f * s, Vector3.down, out var hit, 2.4f * s, ~0, QueryTriggerInteraction.Ignore))
        {
            var t = hit.collider.transform;
            if (hit.point.y > drip.y + 0.15f * s && !hit.collider.GetComponentInParent<Carryable>())
            {
                string name = FurnitureName(t);
                Incidents.Add($"The {name} got soaked by the leak", $"Why is the {name} SOAKING WET?!", 8, hit.point);
                GameHUD.Toast($"The leak's dripping all over the {name}!", GameHUD.Bad);
            }
        }
    }

    static string FurnitureName(Transform t)
    {
        // the piece's own name, without "(1)" and the like
        for (var p = t; p; p = p.parent)
        {
            if (p.GetComponent<Renderer>() == null && p.childCount == 0) continue;
            string n = System.Text.RegularExpressions.Regex.Replace(p.name, @"\s*\(.*?\)|\s*\d+$", "").Trim();
            if (n.Length > 2 && !n.StartsWith("Mesh") && !n.StartsWith("Collider")) return n.ToLowerInvariant();
        }
        return "furniture";
    }

    // ------------------------------------------------------------------ drinks for the cat

    /// <summary>A drink on a free bit of table / desk / counter (not beds or sofas). 'outOfSight': only where you can't see it appear.</summary>
    public Mug PutOutMug(bool outOfSight)
    {
        var options = new List<CatNavMesh.Perch>();
        foreach (var p in CatNavMesh.Perches)
        {
            if (p.cozy || p.top.y > 6.5f * Scale) continue; // (ground and first floor)
            string n = p.name.ToLowerInvariant();
            if (!(n.Contains("table") || n.Contains("desk") || n.Contains("counter") || n.Contains("drawer"))) continue;
            options.Add(p);
        }
        for (int tries = 0; tries < 12 && options.Count > 0; tries++)
        {
            var p = options[Random.Range(0, options.Count)];
            Vector2 r = Random.insideUnitCircle * 0.25f * Scale;
            Vector3 at = p.top + new Vector3(r.x, 0f, r.y);
            if (!UnityEngine.AI.NavMesh.SamplePosition(at, out var hit, 0.3f * Scale, UnityEngine.AI.NavMesh.AllAreas) || Mathf.Abs(hit.position.y - p.top.y) > 0.05f) continue;
            at = hit.position;
            bool crowded = false;
            foreach (var m in Mug.All) if (m && Vector3.Distance(m.transform.position, at) < 1.2f * Scale) crowded = true;
            if (crowded) continue;
            if (outOfSight && Camera.main)
            {
                var cam = Camera.main;
                Vector3 vp = cam.WorldToViewportPoint(at);
                if (vp.z > 0f && vp.x > -0.1f && vp.x < 1.1f && vp.y > -0.1f && vp.y < 1.1f && Vector3.Distance(cam.transform.position, at) < 12f * Scale) continue;
            }
            // (the top of the furniture, not the walk map just above it)
            if (Physics.Raycast(at + Vector3.up * 0.3f * Scale, Vector3.down, out var top, 0.6f * Scale, ~0, QueryTriggerInteraction.Ignore)) at = top.point;
            return Mug.Create(at, Scale * 1.05f);
        }
        return null;
    }
}
