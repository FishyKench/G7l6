using UnityEngine;

/// <summary>
/// The scratching post (ShopNest gadget): a sisal post on a carpet base in the living room. With it in the house the cat
/// scratches the post most of the time instead of the sofa and the curtains. Built at runtime against a living-room wall.
/// </summary>
public class ScratchingPost : MonoBehaviour
{
    public static ScratchingPost Instance { get; private set; }

    /// <summary>Where the cat stands to scratch it (on the floor in front of it).</summary>
    public Vector3 StandSpot { get; private set; }
    public Vector3 Post => transform.position + Vector3.up * 0.35f * scale;
    float scale = 1f;

    void Awake() => Instance = this;

    public static ScratchingPost Create()
    {
        var layout = HouseLayout.Instance;
        if (!layout) return null;
        float s = layout.scale;
        var spots = JobProps.RoomSpots("Living room", new Vector3(0.45f, 0.8f, 0.45f) * s);
        if (spots.Count == 0) spots = JobProps.RoomSpots("Playroom", new Vector3(0.45f, 0.8f, 0.45f) * s);
        if (spots.Count == 0) return null;
        Vector3 at = spots[Mathf.Min(1, spots.Count - 1)];

        var go = new GameObject("Scratching post");
        go.transform.position = at;
        var post = go.AddComponent<ScratchingPost>();
        post.scale = s;
        var carpet = Mat(new Color(0.55f, 0.42f, 0.62f));
        var sisal = Mat(new Color(0.78f, 0.66f, 0.45f));
        Part(PrimitiveType.Cube, go.transform, new Vector3(0f, 0.025f, 0f), new Vector3(0.4f, 0.05f, 0.4f), carpet, s);
        Part(PrimitiveType.Cylinder, go.transform, new Vector3(0f, 0.37f, 0f), new Vector3(0.11f, 0.32f, 0.11f), sisal, s);
        Part(PrimitiveType.Cube, go.transform, new Vector3(0f, 0.71f, 0f), new Vector3(0.28f, 0.04f, 0.28f), carpet, s);
        // little rope rings
        for (int i = 0; i < 5; i++)
            Part(PrimitiveType.Cylinder, go.transform, new Vector3(0f, 0.15f + i * 0.11f, 0f), new Vector3(0.118f, 0.006f, 0.118f), Mat(new Color(0.66f, 0.54f, 0.34f)), s);
        // a dangling toy
        Part(PrimitiveType.Sphere, go.transform, new Vector3(0.12f, 0.56f, 0f), Vector3.one * 0.05f, Mat(new Color(0.95f, 0.35f, 0.3f)), s);

        // stand in front, on the walk map
        Vector3 out1 = at + Vector3.forward * 0.4f * s;
        float best = float.MaxValue;
        for (int i = 0; i < 8; i++)
        {
            Vector3 p = at + Quaternion.Euler(0f, i * 45f, 0f) * Vector3.forward * 0.38f * s;
            if (UnityEngine.AI.NavMesh.SamplePosition(p, out var hit, 0.3f * s, UnityEngine.AI.NavMesh.AllAreas) && Mathf.Abs(hit.position.y - at.y) < 0.2f)
            {
                float d = Vector3.Distance(hit.position, p);
                if (d < best) { best = d; out1 = hit.position; }
            }
        }
        post.StandSpot = out1;
        return post;
    }

    static void Part(PrimitiveType shape, Transform parent, Vector3 pos, Vector3 size, Material m, float s)
    {
        var p = GameObject.CreatePrimitive(shape);
        p.transform.SetParent(parent, false);
        p.transform.localPosition = pos * s;
        p.transform.localScale = size * s;
        p.GetComponent<Renderer>().sharedMaterial = m;
        if (shape != PrimitiveType.Cylinder || size.y < 0.1f) Destroy(p.GetComponent<Collider>());
    }

    static Material Mat(Color c)
    {
        var m = new Material(Shader.Find("Standard")) { color = c };
        m.SetFloat("_Glossiness", 0.05f);
        return m;
    }
}
