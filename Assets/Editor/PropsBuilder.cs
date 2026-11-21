using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// G7l6 > Better Mop and Bucket: swaps the look of the mop and the bucket for nicer low-poly models
/// (a string mop with an aluminium handle, a yellow mop bucket on wheels with water in it).
/// Only their models change: where they stand, their colliders and how they're carried stay as they are.
/// </summary>
public static class PropsBuilder
{
    const string MatFolder = "Assets/Materials/House";

    [MenuItem("G7l6/Better Mop and Bucket")]
    public static void Apply()
    {
        if (EditorApplication.isPlaying) { Debug.LogWarning("[Props] Stop Play mode first."); return; }
        var scene = SceneManager.GetActiveScene();
        if (scene.path != HouseBuilder.TargetScene)
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            scene = EditorSceneManager.OpenScene(HouseBuilder.TargetScene, OpenSceneMode.Single);
        }

        var report = new List<string>();
        foreach (var c in Object.FindObjectsOfType<Carryable>(true))
        {
            if (c.type != ItemType.Mop && c.type != ItemType.Bucket) continue;
            if (c.GetComponent<Renderer>()) { report.Add($"'{c.name}' is a model of its own - left alone"); continue; }

            // Where the old model sat (local space of the item), so the new one fills the same spot.
            var old = c.GetComponentsInChildren<Renderer>(true).Where(r => !(r is ParticleSystemRenderer)).ToList();
            if (old.Count == 0) { report.Add($"'{c.name}' has no model to replace"); continue; }
            Bounds b = LocalBounds(c.transform, old);

            foreach (var r in old)
            {
                var go = r.gameObject;
                if (go == c.gameObject) continue;
                if (go.GetComponents<Component>().Any(k => !(k is Transform || k is MeshFilter || k is Renderer))) continue;
                Undo.DestroyObjectImmediate(go);
            }

            foreach (Transform child in c.transform.Cast<Transform>().ToList())
                if (child.name == "Model" && child.childCount == 0) Undo.DestroyObjectImmediate(child.gameObject);
            var model = new GameObject("Model").transform;
            Undo.RegisterCreatedObjectUndo(model.gameObject, "Prop model");
            model.SetParent(c.transform, false);
            float height;
            if (c.type == ItemType.Mop) height = BuildMop(model);
            else height = BuildBucket(model);
            float scale = b.size.y / height;
            model.localPosition = new Vector3(b.center.x, b.min.y, b.center.z);
            model.localScale = Vector3.one * scale;
            EditorUtility.SetDirty(c);
            report.Add($"{c.type}: new model in '{c.name}'");
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[Props] " + (report.Count > 0 ? string.Join("; ", report) : "no mop or bucket found") + ". Nothing else changed. The scene is saved.");
    }

    static Bounds LocalBounds(Transform root, List<Renderer> renderers)
    {
        bool any = false;
        var b = new Bounds();
        foreach (var r in renderers)
        {
            var mf = r.GetComponent<MeshFilter>();
            if (!mf || !mf.sharedMesh) continue;
            var mb = mf.sharedMesh.bounds;
            for (int i = 0; i < 8; i++)
            {
                var corner = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                var p = root.InverseTransformPoint(r.transform.TransformPoint(corner));
                if (!any) { b = new Bounds(p, Vector3.zero); any = true; }
                else b.Encapsulate(p);
            }
        }
        return b;
    }

    // ------------------------------------------------------------------ the mop (metres, pivot on the floor)

    static float BuildMop(Transform root)
    {
        var alu = Mat("Prop Aluminium", new Color(0.78f, 0.8f, 0.84f), 0.75f, 0.8f);
        var grip = Mat("Prop Grip Blue", new Color(0.12f, 0.36f, 0.85f), 0.35f);
        var plastic = Mat("Prop Plastic Red", new Color(0.86f, 0.2f, 0.16f), 0.45f);
        var cotton = Mat("Prop Mop Cotton", new Color(0.9f, 0.88f, 0.82f), 0.05f);
        var cotton2 = Mat("Prop Mop Cotton Grey", new Color(0.74f, 0.73f, 0.7f), 0.05f);

        const float top = 1.32f, socket = 0.2f;
        Part(root, "Handle", Lathe(new[] { V(0.0125f, socket + 0.03f), V(0.0125f, top - 0.16f) }, 10, false), alu);
        Part(root, "Grip", Lathe(new[] { V(0.0f, top - 0.17f), V(0.016f, top - 0.17f), V(0.017f, top - 0.02f), V(0.012f, top), V(0f, top) }, 10, true), grip);
        Part(root, "Hang loop", Tube(Arc(new Vector3(0f, top + 0.015f, 0f), 0.022f, 12), 0.003f, 6), grip);
        Part(root, "Socket", Lathe(new[] { V(0f, socket - 0.035f), V(0.045f, socket - 0.035f), V(0.05f, socket), V(0.022f, socket + 0.06f), V(0.014f, socket + 0.07f), V(0f, socket + 0.07f) }, 14, true), plastic);
        Part(root, "Band", Lathe(new[] { V(0.047f, socket - 0.045f), V(0.047f, socket - 0.01f) }, 14, false), plastic);

        // Strings: fall from the socket, spread out and flop on the floor, a few curling back.
        var white = new List<Mesh>(); var grey = new List<Mesh>();
        var rng = new System.Random(7);
        float R() => (float)rng.NextDouble();
        const int strands = 46;
        for (int i = 0; i < strands; i++)
        {
            float a = (i + R() * 0.6f) / strands * Mathf.PI * 2f;
            var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            float start = 0.02f + R() * 0.022f, spread = 0.1f + R() * 0.1f, curl = R() * 0.05f;
            var pts = new List<Vector3>();
            const int seg = 6;
            for (int k = 0; k <= seg; k++)
            {
                float u = k / (float)seg;
                float h = Mathf.Lerp(socket - 0.04f, 0.012f, Mathf.SmoothStep(0f, 1f, Mathf.Min(1f, u * 1.35f)));
                float r = start + spread * u * u + curl * Mathf.Max(0f, u - 0.7f) * 3f;
                pts.Add(dir * r + Vector3.up * h + Vector3.Cross(dir, Vector3.up) * Mathf.Sin(u * 3f + a * 5f) * 0.012f);
            }
            (i % 4 == 0 ? grey : white).Add(Tube(pts, 0.0065f + R() * 0.002f, 5));
        }
        Part(root, "Strings", Combine(white), cotton);
        Part(root, "Strings (worn)", Combine(grey), cotton2);
        return top + 0.04f;
    }

    // ------------------------------------------------------------------ the bucket (metres, pivot on the floor)

    static float BuildBucket(Transform root)
    {
        var yellow = Mat("Prop Bucket Yellow", new Color(1f, 0.78f, 0.1f), 0.5f);
        var dark = Mat("Prop Bucket Dark", new Color(0.13f, 0.13f, 0.15f), 0.35f);
        var metal = Mat("Prop Aluminium", new Color(0.78f, 0.8f, 0.84f), 0.75f, 0.8f);
        var water = Mat("Prop Bucket Water", new Color(0.32f, 0.52f, 0.62f), 0.95f);

        const float floorGap = 0.05f, h = 0.32f;
        float y0 = floorGap;
        // Tub with a real wall thickness, open at the top, a rolled lip.
        Part(root, "Tub", Lathe(new[]
        {
            V(0f, y0), V(0.15f, y0), V(0.158f, y0 + 0.01f), V(0.185f, y0 + h - 0.02f), V(0.2f, y0 + h - 0.012f), V(0.2f, y0 + h),
            V(0.177f, y0 + h), V(0.15f, y0 + 0.03f), V(0f, y0 + 0.03f),
        }, 28, false), yellow);
        Part(root, "Ribs", Lathe(new[] { V(0.165f, y0 + 0.09f), V(0.17f, y0 + 0.095f), V(0.17f, y0 + 0.11f), V(0.166f, y0 + 0.115f) }, 28, false), yellow);
        Part(root, "Water", Lathe(new[] { V(0f, y0 + h * 0.62f), V(0.172f, y0 + h * 0.62f) }, 28, false), water);

        // A base ring with four little castors.
        Part(root, "Base", Lathe(new[] { V(0.13f, floorGap + 0.005f), V(0.165f, floorGap - 0.005f), V(0.165f, floorGap + 0.012f), V(0.13f, floorGap + 0.012f) }, 28, false), dark);
        for (int i = 0; i < 4; i++)
        {
            float a = (i * 90f + 45f) * Mathf.Deg2Rad;
            var at = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 0.145f;
            var wheel = Part(root, "Castor", Lathe(new[] { V(0f, -0.012f), V(0.025f, -0.012f), V(0.025f, 0.012f), V(0f, 0.012f) }, 12, true), dark);
            wheel.transform.localPosition = at + Vector3.up * 0.025f;
            wheel.transform.localRotation = Quaternion.Euler(0f, -a * Mathf.Rad2Deg, 90f);
            var fork = Part(root, "Fork", Lathe(new[] { V(0f, 0.035f), V(0.008f, 0.035f), V(0.008f, floorGap + 0.01f), V(0f, floorGap + 0.01f) }, 6, true), metal);
            fork.transform.localPosition = at;
        }

        // The bail handle: a wire arc over the top, a grip in the middle, lugs on the sides.
        var arc = new List<Vector3>();
        for (int k = 0; k <= 16; k++)
        {
            float u = k / 16f * Mathf.PI;
            arc.Add(new Vector3(Mathf.Cos(u) * 0.195f, y0 + h - 0.04f + Mathf.Sin(u) * 0.16f, 0f));
        }
        Part(root, "Handle", Tube(arc, 0.004f, 6), metal);
        Part(root, "Handle grip", Tube(new List<Vector3> { new Vector3(-0.055f, y0 + h + 0.12f, 0f), new Vector3(0.055f, y0 + h + 0.12f, 0f) }, 0.012f, 10), dark);
        foreach (int side in new[] { -1, 1 })
        {
            var lug = Part(root, "Lug", Lathe(new[] { V(0f, 0f), V(0.02f, 0f), V(0.02f, 0.012f), V(0f, 0.012f) }, 10, true), yellow);
            lug.transform.localPosition = new Vector3(side * 0.19f, y0 + h - 0.04f, 0f);
            lug.transform.localRotation = Quaternion.Euler(0f, 0f, side * -90f);
        }
        return y0 + h + 0.135f;
    }

    // ------------------------------------------------------------------ meshes

    static Vector2 V(float r, float y) => new Vector2(r, y);

    /// <summary>Spins a profile (radius, height) round the Y axis. Smooth round, sharp at each profile corner.</summary>
    static Mesh Lathe(Vector2[] profile, int sides, bool closed)
    {
        var verts = new List<Vector3>(); var tris = new List<int>(); var uv = new List<Vector2>();
        for (int s = 0; s < profile.Length - 1; s++)
        {
            Vector2 a = profile[s], b = profile[s + 1];
            int start = verts.Count;
            for (int i = 0; i <= sides; i++)
            {
                float ang = i / (float)sides * Mathf.PI * 2f;
                var d = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang));
                verts.Add(d * a.x + Vector3.up * a.y);
                verts.Add(d * b.x + Vector3.up * b.y);
                uv.Add(new Vector2(i / (float)sides, 0f));
                uv.Add(new Vector2(i / (float)sides, 1f));
            }
            for (int i = 0; i < sides; i++)
            {
                int v = start + i * 2;
                tris.AddRange(new[] { v, v + 1, v + 2, v + 2, v + 1, v + 3 });
            }
        }
        var m = new Mesh { name = "Lathe" };
        m.SetVertices(verts); m.SetUVs(0, uv); m.SetTriangles(tris, 0);
        m.RecalculateNormals(); m.RecalculateBounds();
        if (!closed) return DoubleSided(m);
        return m;
    }

    /// <summary>Open shapes (the bucket's wall, the handle tube) are seen from both sides.</summary>
    static Mesh DoubleSided(Mesh m)
    {
        var v = m.vertices; var n = m.normals; var uv = m.uv; var t = m.triangles;
        int c = v.Length;
        var v2 = new Vector3[c * 2]; var n2 = new Vector3[c * 2]; var uv2 = new Vector2[c * 2]; var t2 = new int[t.Length * 2];
        for (int i = 0; i < c; i++) { v2[i] = v2[i + c] = v[i]; n2[i] = n[i]; n2[i + c] = -n[i]; uv2[i] = uv2[i + c] = uv[i]; }
        for (int i = 0; i < t.Length; i += 3)
        {
            t2[i] = t[i]; t2[i + 1] = t[i + 1]; t2[i + 2] = t[i + 2];
            t2[t.Length + i] = t[i] + c; t2[t.Length + i + 1] = t[i + 2] + c; t2[t.Length + i + 2] = t[i + 1] + c;
        }
        var d = new Mesh { name = m.name, vertices = v2, normals = n2, uv = uv2, triangles = t2 };
        d.RecalculateBounds();
        return d;
    }

    /// <summary>A round tube along a path (strings, wire handle).</summary>
    static Mesh Tube(List<Vector3> path, float radius, int sides)
    {
        var verts = new List<Vector3>(); var tris = new List<int>();
        Vector3 up = Vector3.up;
        for (int k = 0; k < path.Count; k++)
        {
            Vector3 fwd = (k < path.Count - 1 ? path[k + 1] - path[k] : path[k] - path[k - 1]).normalized;
            if (Mathf.Abs(Vector3.Dot(fwd, up)) > 0.95f) up = Vector3.right;
            Vector3 x = Vector3.Cross(fwd, up).normalized, y = Vector3.Cross(x, fwd);
            for (int i = 0; i < sides; i++)
            {
                float a = i / (float)sides * Mathf.PI * 2f;
                verts.Add(path[k] + (x * Mathf.Cos(a) + y * Mathf.Sin(a)) * radius);
            }
            if (k == 0) continue;
            int b0 = (k - 1) * sides, b1 = k * sides;
            for (int i = 0; i < sides; i++)
            {
                int j = (i + 1) % sides;
                tris.AddRange(new[] { b0 + i, b1 + i, b0 + j, b0 + j, b1 + i, b1 + j });
            }
        }
        // Cap the ends.
        verts.Add(path[0]); verts.Add(path[path.Count - 1]);
        int c0 = verts.Count - 2, c1 = verts.Count - 1, last = (path.Count - 1) * sides;
        for (int i = 0; i < sides; i++)
        {
            int j = (i + 1) % sides;
            tris.AddRange(new[] { c0, i, j, c1, last + j, last + i });
        }
        var m = new Mesh { name = "Tube" };
        m.SetVertices(verts); m.SetTriangles(tris, 0);
        m.RecalculateNormals(); m.RecalculateBounds();
        return m;
    }

    static List<Vector3> Arc(Vector3 centre, float r, int n)
    {
        var pts = new List<Vector3>();
        for (int k = 0; k <= n; k++)
        {
            float a = k / (float)n * Mathf.PI * 2f;
            pts.Add(centre + new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r, 0f));
        }
        return pts;
    }

    static Mesh Combine(List<Mesh> parts)
    {
        var m = new Mesh { name = "Strings" };
        m.CombineMeshes(parts.Select(p => new CombineInstance { mesh = p, transform = Matrix4x4.identity }).ToArray(), true, true);
        m.RecalculateBounds();
        return m;
    }

    static GameObject Part(Transform parent, string name, Mesh mesh, Material mat)
    {
        var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
        go.transform.SetParent(parent, false);
        go.GetComponent<MeshFilter>().sharedMesh = mesh;
        go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        return go;
    }

    static Material Mat(string name, Color c, float smooth, float metallic = 0f)
    {
        string path = $"{MatFolder}/{name}.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!m)
        {
            m = new Material(Shader.Find("Standard"));
            AssetDatabase.CreateAsset(m, path);
        }
        m.color = c;
        m.SetFloat("_Glossiness", smooth);
        m.SetFloat("_Metallic", metallic);
        EditorUtility.SetDirty(m);
        return m;
    }
}
