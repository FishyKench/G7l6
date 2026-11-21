using UnityEngine;

/// <summary>
/// Puts a picture on the screen of a TV or monitor model so it fills the whole screen - curved or flat - however
/// the model's own texture is laid out (shader: G7l6/Projected Screen). Used for TV static and the PC monitor.
/// </summary>
public static class ScreenProjector
{
    /// <summary>The screen part of a model: the material that isn't the shared colour palette.</summary>
    public static bool FindScreen(GameObject device, out MeshRenderer renderer, out int slot)
    {
        foreach (var r in device.GetComponentsInChildren<MeshRenderer>())
        {
            var mf = r.GetComponent<MeshFilter>();
            if (!mf || !mf.sharedMesh) continue;
            var mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length && i < mf.sharedMesh.subMeshCount; i++)
            {
                if (!mats[i] || mats[i].name.ToLowerInvariant().Contains("palette")) continue;
                renderer = r;
                slot = i;
                return true;
            }
        }
        renderer = null;
        slot = 0;
        return false;
    }

    /// <summary>
    /// A material showing 'picture' across the screen part (material slot) of the renderer.
    /// 'facing' is the way the screen faces in the world (towards the viewer). Null if the shader is missing.
    /// </summary>
    public static Material Make(Renderer renderer, int slot, Texture picture, Vector3 facing, bool staticNoise, out float aspect)
    {
        aspect = 16f / 9f;
        var shader = Shader.Find("G7l6/Projected Screen");
        var mf = renderer ? renderer.GetComponent<MeshFilter>() : null;
        if (!shader || !mf || !mf.sharedMesh) return null;

        var mesh = mf.sharedMesh;
        Bounds b = mesh.bounds;
        if (mesh.subMeshCount > 1 && slot < mesh.subMeshCount)
        {
            var sub = mesh.GetSubMesh(slot).bounds;
            if (sub.size.sqrMagnitude > 1e-8f) b = sub;
        }

        // The thinnest side of the screen is the way it faces; of the other two, the one nearest the world's up is "up".
        var t = renderer.transform;
        Vector3[] axes = { Vector3.right, Vector3.up, Vector3.forward };
        Vector3 worldSize = new Vector3(t.TransformVector(Vector3.right * b.size.x).magnitude,
                                        t.TransformVector(Vector3.up * b.size.y).magnitude,
                                        t.TransformVector(Vector3.forward * b.size.z).magnitude);
        int thin = 0;
        for (int i = 1; i < 3; i++) if (worldSize[i] < worldSize[thin]) thin = i;
        int a = (thin + 1) % 3, c = (thin + 2) % 3;
        int upIndex = Mathf.Abs(t.TransformDirection(axes[a]).y) >= Mathf.Abs(t.TransformDirection(axes[c]).y) ? a : c;
        int acrossIndex = upIndex == a ? c : a;

        Vector3 normal = axes[thin];
        if (Vector3.Dot(t.TransformDirection(normal), facing) < 0f) normal = -normal;
        Vector3 up = axes[upIndex];
        if (t.TransformDirection(up).y < 0f) up = -up;
        // "Right" as the viewer sees it, worked out in the world (so mirrored models still read the right way).
        Vector3 worldRight = Vector3.Cross(t.TransformDirection(up), -t.TransformDirection(normal));
        Vector3 right = axes[acrossIndex];
        if (Vector3.Dot(t.TransformDirection(right), worldRight) < 0f) right = -right;

        float cu = Vector3.Dot(b.center, right), eu = Mathf.Abs(b.extents[acrossIndex]);
        float cv = Vector3.Dot(b.center, up), ev = Mathf.Abs(b.extents[upIndex]);
        aspect = Mathf.Max(0.2f, worldSize[acrossIndex] / Mathf.Max(0.0001f, worldSize[upIndex]));

        var m = new Material(shader) { name = staticNoise ? "Screen static" : "Screen picture" };
        m.mainTexture = picture;
        m.SetVector("_AxisU", right);
        m.SetVector("_AxisV", up);
        m.SetVector("_Range", new Vector4(cu - eu, Mathf.Max(0.0001f, eu * 2f), cv - ev, Mathf.Max(0.0001f, ev * 2f)));
        m.SetVector("_Noise", staticNoise ? new Vector4(24f, 2f, 0f, 0f) : Vector4.zero);
        m.SetFloat("_Brightness", staticNoise ? 1.15f : 1.2f);
        return m;
    }
}
