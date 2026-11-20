using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// The cat's walk map (a NavMesh sized for a cat, built from the house's colliders).
/// G7l6 > Set Up House Tasks bakes it and saves it as an asset; if it's missing, it's built
/// when the game starts. Re-bake with G7l6 > Bake Cat Walk Map after moving furniture.
/// </summary>
public class CatNavMesh : MonoBehaviour
{
    public NavMeshData data;
    [Tooltip("Area the walk map covers (used when it has to be built at runtime).")]
    public Bounds bounds = new Bounds(Vector3.zero, new Vector3(80f, 30f, 80f));

    NavMeshDataInstance instance;

    void OnEnable()
    {
        if (!data)
        {
            data = Bake(bounds);
            Debug.Log("[Cat] No saved walk map - built one at runtime. Run G7l6 > Bake Cat Walk Map to save it.");
        }
        if (data) instance = NavMesh.AddNavMeshData(data);
    }

    void OnDisable()
    {
        if (instance.valid) instance.Remove();
    }

    /// <summary>Cat-sized walk settings (uses the default agent type so NavMeshAgents find it).</summary>
    public static NavMeshBuildSettings Settings()
    {
        var s = NavMesh.GetSettingsByID(0);
        s.agentRadius = 0.18f;
        s.agentHeight = 0.45f;
        s.agentClimb = 0.3f;
        s.agentSlope = 50f;
        s.minRegionArea = 0.3f;
        return s;
    }

    public static NavMeshData Bake(Bounds area)
    {
        var sources = new List<NavMeshBuildSource>();
        int layers = ~((1 << 5) | (1 << 6)); // skip UI and the player
        NavMeshBuilder.CollectSources(area, layers, NavMeshCollectGeometry.PhysicsColliders, 0, new List<NavMeshBuildMarkup>(), sources);
        sources.RemoveAll(IsMovingThing);
        return NavMeshBuilder.BuildNavMeshData(Settings(), sources, area, Vector3.zero, Quaternion.identity);
    }

    /// <summary>Things that move around shouldn't become part of the floor.</summary>
    static bool IsMovingThing(NavMeshBuildSource source)
    {
        var col = source.component as Collider;
        if (!col) return false;
        if (col.isTrigger) return true;
        if (col.attachedRigidbody && !col.attachedRigidbody.isKinematic) return true;
        return col.GetComponentInParent<CatBrain>() || col.GetComponentInParent<PlayerMovementAdvanced>() || col.GetComponentInParent<Carryable>();
    }
}
