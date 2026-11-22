using UnityEngine;
using UnityEngine.AI;

/// <summary>Tiny helpers for building low-poly props from primitives at runtime (cubes and cylinders, no colliders).</summary>
public static class ProcBuild
{
    public static Material Mat(Color color, float smoothness = 0.15f, Color? emission = null)
    {
        var m = new Material(Shader.Find("Standard")) { color = color };
        m.SetFloat("_Glossiness", smoothness);
        if (emission.HasValue)
        {
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", emission.Value);
        }
        return m;
    }

    /// <summary>A box centred at 'localPos' with the given size (metres).</summary>
    public static GameObject Cube(Transform parent, string name, Vector3 localPos, Vector3 size, Material mat, Vector3? euler = null)
        => Prim(PrimitiveType.Cube, parent, name, localPos, size, mat, euler);

    /// <summary>An upright cylinder centred at 'localPos'; diameter and height in metres.</summary>
    public static GameObject Cyl(Transform parent, string name, Vector3 localPos, float diameter, float height, Material mat, Vector3? euler = null)
        => Prim(PrimitiveType.Cylinder, parent, name, localPos, new Vector3(diameter, height * 0.5f, diameter), mat, euler);

    public static GameObject Ball(Transform parent, string name, Vector3 localPos, float diameter, Material mat)
        => Prim(PrimitiveType.Sphere, parent, name, localPos, Vector3.one * diameter, mat, null);

    static GameObject Prim(PrimitiveType type, Transform parent, string name, Vector3 localPos, Vector3 scale, Material mat, Vector3? euler)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        var col = go.GetComponent<Collider>();
        if (col) Object.Destroy(col);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = scale;
        if (euler.HasValue) go.transform.localRotation = Quaternion.Euler(euler.Value);
        var r = go.GetComponent<Renderer>();
        r.sharedMaterial = mat;
        return go;
    }

    /// <summary>Makes walkers (the cat, the robot vacuum) steer round the object.</summary>
    public static void Carve(GameObject go, Vector3 center, Vector3 size)
    {
        var o = go.AddComponent<NavMeshObstacle>();
        o.shape = NavMeshObstacleShape.Box;
        o.center = center;
        o.size = size;
        o.carving = true;
        o.carveOnlyStationary = true;
    }

    /// <summary>Soft white outline shown when the crosshair is on the object (the project's Outline component).</summary>
    public static void AddOutline(GameObject go)
    {
        var outline = go.AddComponent<Outline>();
        outline.OutlineMode = Outline.Mode.OutlineVisible;
        outline.OutlineColor = Color.white;
        outline.OutlineWidth = 5f;
        outline.enabled = false;
    }

    /// <summary>
    /// Finds the floor under a spot: the point on the first solid surface below 'from' that is roughly level and
    /// within 'maxStep' of 'floorY'. False if there's furniture (or nothing) there.
    /// </summary>
    public static bool FloorAt(Vector3 from, float floorY, float maxStep, out Vector3 point)
    {
        point = default;
        var hits = Physics.RaycastAll(from, Vector3.down, 6f, ~0, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (var h in hits)
        {
            if (h.collider.GetComponentInParent<PlayerMovementAdvanced>() || h.collider.GetComponentInParent<CatBrain>()) continue;
            if (h.normal.y < 0.85f || Mathf.Abs(h.point.y - floorY) > maxStep) return false;
            point = h.point;
            return true;
        }
        return false;
    }

    /// <summary>Is anything solid inside this upright box? (the player and the cat don't count)</summary>
    public static bool Blocked(Vector3 floorPoint, Vector3 size, Quaternion rotation, float margin = 0.05f)
    {
        Vector3 centre = floorPoint + Vector3.up * (size.y * 0.5f + 0.04f);
        Vector3 half = size * 0.5f + new Vector3(margin, 0f, margin);
        half.y -= 0.02f;
        foreach (var c in Physics.OverlapBox(centre, half, rotation, ~0, QueryTriggerInteraction.Ignore))
        {
            if (c.GetComponentInParent<PlayerMovementAdvanced>() || c.GetComponentInParent<CatBrain>()) continue;
            return true;
        }
        return false;
    }
}
