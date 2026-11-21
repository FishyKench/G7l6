using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// The cat's walk map (a NavMesh sized for the cat, built from the house's colliders) plus
/// "jump links" between the floor and furniture tops (beds, desks, tables, counters) so the cat
/// can hop up and down.
/// G7l6 > Set Up House Tasks bakes the walk map and saves it; if it's missing, it's built when the
/// game starts. Jump links are worked out when the game starts, so moving furniture just works
/// (but re-bake the walk map with G7l6 > Bake Cat Walk Map after moving big things).
/// </summary>
public class CatNavMesh : MonoBehaviour
{
    public struct Perch
    {
        public Vector3 top;      // a spot on top of the furniture
        public string name;      // the furniture's name
        public bool cozy;        // beds and sofas: good for naps
    }

    /// <summary>Furniture tops the cat can jump onto (filled when the game starts).</summary>
    public static readonly List<Perch> Perches = new List<Perch>();

    public NavMeshData data;
    [Tooltip("Area the walk map covers (used when it has to be built at runtime).")]
    public Bounds bounds = new Bounds(Vector3.zero, new Vector3(80f, 30f, 80f));
    [Tooltip("The furniture root (found by name if empty). Jump links are made for furniture under it.")]
    public Transform furniture;
    [Tooltip("Highest jump onto furniture for a normal-size cat (scaled with the cat).")]
    public float maxJumpUp = 0.9f;

    static readonly Regex CozyNames = new Regex("bed|sofa|couch|armchair|lounge", RegexOptions.IgnoreCase);

    NavMeshDataInstance instance;
    readonly List<NavMeshLinkInstance> links = new List<NavMeshLinkInstance>();

    void OnEnable()
    {
        float scale = CatScale();
        if (!data)
        {
            data = Bake(bounds, scale);
            Debug.Log("[Cat] No saved walk map - built one at runtime. Run G7l6 > Bake Cat Walk Map to save it.");
        }
        if (data) instance = NavMesh.AddNavMeshData(data);
        BuildJumpLinks(scale);
    }

    void OnDisable()
    {
        foreach (var l in links) if (l.valid) l.Remove();
        links.Clear();
        Perches.Clear();
        if (instance.valid) instance.Remove();
    }

    static float CatScale()
    {
        var cat = FindObjectOfType<CatBrain>(true);
        return cat ? Mathf.Max(0.1f, cat.transform.lossyScale.y) : 1f;
    }

    /// <summary>Cat-sized walk settings (uses the default agent type so NavMeshAgents find it).</summary>
    public static NavMeshBuildSettings Settings(float catScale = 1f)
    {
        var s = NavMesh.GetSettingsByID(0);
        s.agentRadius = 0.13f * catScale;
        s.agentHeight = 0.42f * catScale;
        s.agentClimb = 0.2f * catScale;
        s.agentSlope = 50f;
        s.minRegionArea = 0.2f;
        return s;
    }

    public static NavMeshData Bake(Bounds area, float catScale = 1f)
    {
        var sources = new List<NavMeshBuildSource>();
        int layers = ~((1 << 5) | (1 << 6)); // skip UI and the player
        NavMeshBuilder.CollectSources(area, layers, NavMeshCollectGeometry.PhysicsColliders, 0, new List<NavMeshBuildMarkup>(), sources);
        sources.RemoveAll(IsMovingThing);
        return NavMeshBuilder.BuildNavMeshData(Settings(catScale), sources, area, Vector3.zero, Quaternion.identity);
    }

    /// <summary>Things that move around shouldn't become part of the floor.</summary>
    static bool IsMovingThing(NavMeshBuildSource source)
    {
        var col = source.component as Collider;
        if (!col) return false;
        if (col.isTrigger) return true;
        if (col.attachedRigidbody && !col.attachedRigidbody.isKinematic) return true;
        return col.GetComponentInParent<CatBrain>() || col.GetComponentInParent<PlayerMovementAdvanced>() || col.GetComponentInParent<Carryable>()
               || col.GetComponentInParent<HouseDoor>(); // doors open and shut - the cat paws them open
    }

    // ------------------------------------------------------------------ jump links

    /// <summary>
    /// For every piece of furniture with a walkable top, link that top to the floor next to it
    /// (on each side where the jump is clear and not too high).
    /// </summary>
    void BuildJumpLinks(float catScale)
    {
        Perches.Clear();
        var root = furniture;
        if (!root)
        {
            var go = GameObject.Find("Furniature") ?? GameObject.Find("Furniture");
            if (go) root = go.transform;
        }
        if (!root) return;

        float maxUp = maxJumpUp * catScale;
        const float minUp = 0.25f;
        var seenTops = new HashSet<Vector3Int>();
        var sides = new[] { Vector3.right, Vector3.left, Vector3.forward, Vector3.back };

        foreach (var col in root.GetComponentsInChildren<Collider>())
        {
            if (!col.enabled || col.isTrigger || !col.gameObject.activeInHierarchy) continue;
            if (col.GetComponentInParent<Carryable>()) continue;
            Bounds b = col.bounds;
            if (b.size.x < 0.3f || b.size.z < 0.3f) continue;

            // Is there walk map on top of it? (Search down from the top: a bed's headboard is higher than its mattress.)
            if (!FindTop(b, out var top)) continue;
            if (!seenTops.Add(Vector3Int.RoundToInt(top.position * 2f))) continue;

            Transform piece = PieceRoot(col.transform, root);
            bool linked = false;
            foreach (var dir in sides)
            {
                float extent = Mathf.Abs(Vector3.Dot(b.extents, dir));
                Vector3 edgeProbe = new Vector3(b.center.x, top.position.y, b.center.z) + dir * Mathf.Max(0f, extent - 0.2f * catScale);
                if (!NavMesh.SamplePosition(edgeProbe, out var edge, 0.5f, NavMesh.AllAreas)) continue;
                if (Mathf.Abs(edge.position.y - top.position.y) > 0.12f) continue;

                Vector3 floorProbe = new Vector3(b.center.x, b.min.y + 0.1f, b.center.z) + dir * (extent + 0.35f * catScale);
                if (!NavMesh.SamplePosition(floorProbe, out var floor, 0.5f, NavMesh.AllAreas)) continue;

                float rise = edge.position.y - floor.position.y;
                if (rise < minUp || rise > maxUp) continue;
                Vector3 gap = edge.position - floor.position; gap.y = 0f;
                if (gap.magnitude > 1.2f) continue;
                if (Blocked(floor.position, edge.position, piece, 0.3f * catScale)) continue; // wall in the way

                var link = new NavMeshLinkData
                {
                    startPosition = floor.position,
                    endPosition = edge.position,
                    width = 0f,
                    bidirectional = true,
                    area = 2,          // built-in "Jump" area
                    costModifier = -1f,
                    agentTypeID = 0,
                };
                var inst = NavMesh.AddLink(link);
                if (inst.valid) { links.Add(inst); linked = true; }
            }

            if (linked)
                Perches.Add(new Perch { top = top.position, name = piece.name, cozy = CozyNames.IsMatch(piece.name) });
        }
        Debug.Log($"[Cat] {links.Count} jump links onto {Perches.Count} pieces of furniture.");
    }

    /// <summary>A walk-map spot on top of something (above its bottom and within its footprint).</summary>
    public static bool FindTop(Bounds b, out NavMeshHit top)
    {
        for (float y = b.max.y + 0.05f; y > b.min.y + 0.2f; y -= 0.3f)
        {
            if (!NavMesh.SamplePosition(new Vector3(b.center.x, y, b.center.z), out top, 0.4f, NavMesh.AllAreas)) continue;
            Vector3 p = top.position;
            bool above = p.y > b.min.y + 0.25f;
            bool inside = p.x > b.min.x - 0.05f && p.x < b.max.x + 0.05f && p.z > b.min.z - 0.05f && p.z < b.max.z + 0.05f;
            if (above && inside) return true;
        }
        top = default;
        return false;
    }

    /// <summary>The furniture piece a collider belongs to (the object right under its room group).</summary>
    static Transform PieceRoot(Transform t, Transform root)
    {
        while (t.parent && t.parent != root && t.parent.parent != root) t = t.parent;
        return t;
    }

    /// <summary>Is anything (other than the furniture itself) between the floor spot and the top?</summary>
    static bool Blocked(Vector3 from, Vector3 to, Transform piece, float lift)
    {
        Vector3 a = from + Vector3.up * lift, b = to + Vector3.up * lift;
        foreach (var hit in Physics.RaycastAll(a, (b - a).normalized, Vector3.Distance(a, b), ~0, QueryTriggerInteraction.Ignore))
        {
            var t = hit.collider.transform;
            if (t.IsChildOf(piece)) continue;
            if (hit.collider.GetComponentInParent<CatBrain>() || hit.collider.GetComponentInParent<Carryable>()
                || hit.collider.GetComponentInParent<PlayerMovementAdvanced>()) continue;
            return true;
        }
        return false;
    }
}
