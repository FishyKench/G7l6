using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Mom's own walking map: baked when the day starts, for a grown-up's size (the cat's map lets things walk right along the
/// walls, which is why Mom used to clip through them). Doors and the garage door are left out (she walks through doorways
/// whether a door is shut or not), and so is anything you can carry, the player and the cat.
/// </summary>
public static class MomNav
{
    static NavMeshData data;
    static NavMeshDataInstance instance;
    static AsyncOperation baking;
    static int agentType;
    static bool started; // (agent type ids can be negative: this says whether there is one)

    /// <summary>The agent type to walk with: Mom's map once it's baked, otherwise the cat's (agent type 0).</summary>
    public static int AgentType => Ready ? agentType : 0;
    public static bool Ready => started && instance.valid && (baking == null || baking.isDone);
    public static float Radius { get; private set; }

    /// <summary>Starts baking in the background (call when the house is loaded). Safe to call more than once.</summary>
    public static void Bake()
    {
        if (started && instance.valid) return;
        var layout = HouseLayout.Instance;
        if (!layout) return;
        float s = layout.scale;

        // the whole house, from its colliders
        var root = layout.transform.root;
        var colliders = root.GetComponentsInChildren<Collider>(true);
        if (colliders.Length == 0) return;
        var bounds = colliders[0].bounds;
        foreach (var c in colliders) bounds.Encapsulate(c.bounds);
        bounds.Expand(2f);

        var settings = NavMesh.CreateSettings();
        settings.agentRadius = Radius = 0.24f * s; // a grown-up, with a little room to her shoulders
        settings.agentHeight = 1.7f * s;
        settings.agentClimb = 0.32f * s;
        settings.agentSlope = 42f;

        var sources = new List<NavMeshBuildSource>();
        NavMeshBuilder.CollectSources(bounds, ~(1 << 6), NavMeshCollectGeometry.PhysicsColliders, 0, new List<NavMeshBuildMarkup>(), sources);
        sources.RemoveAll(src => src.component && Skip(src.component));
        // A mesh collider whose mesh can't be read (most furniture models): the bake gets a box the size of it instead -
        // that's all a walking map needs, and it works in a built game too.
        for (int i = 0; i < sources.Count; i++)
        {
            var src = sources[i];
            if (src.shape != NavMeshBuildSourceShape.Mesh || !(src.sourceObject is Mesh mesh) || mesh.isReadable) continue;
            var b = src.component is Collider col ? col.bounds : new Bounds(src.transform.GetColumn(3), Vector3.one);
            src.shape = NavMeshBuildSourceShape.Box;
            src.transform = Matrix4x4.TRS(b.center, Quaternion.identity, Vector3.one);
            src.size = b.size;
            src.sourceObject = null;
            sources[i] = src;
        }

        data = new NavMeshData(settings.agentTypeID);
        instance = NavMesh.AddNavMeshData(data);
        baking = NavMeshBuilder.UpdateNavMeshDataAsync(data, settings, sources, bounds);
        agentType = settings.agentTypeID;
        started = true;
    }

    static bool Skip(Component c)
    {
        if (c is Collider col && col.isTrigger) return true;
        if (c.GetComponentInParent<Carryable>() || c.GetComponentInParent<PlayerMovementAdvanced>() || c.GetComponentInParent<MomInspector>()) return true;
        if (c.GetComponentInParent<CatBrain>()) return true;
        for (var t = c.transform; t; t = t.parent)
            if (t.name == "Doors" || t.name == "Garage door") return true; // the doors themselves (not the walls round them)
        return false;
    }

    public static NavMeshQueryFilter Filter => new NavMeshQueryFilter { agentTypeID = AgentType, areaMask = NavMesh.AllAreas };

    public static bool Sample(Vector3 near, float range, out NavMeshHit hit) => NavMesh.SamplePosition(near, out hit, range, Filter);
}
