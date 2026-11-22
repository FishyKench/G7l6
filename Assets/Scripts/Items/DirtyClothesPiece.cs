using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One piece of dirty laundry lying around (a crumpled shirt, socks, trousers, a hoodie). Press E to pick it up:
/// it goes onto the armful of dirty clothes you're carrying. Made by the LaundryTask when the laundry starts.
/// </summary>
public class DirtyClothesPiece : MonoBehaviour, IInteractable, IPromptProvider
{
    public static readonly List<DirtyClothesPiece> All = new List<DirtyClothesPiece>();

    public LaundryTask laundry;
    public string kind = "shirt";

    void OnEnable() => All.Add(this);
    void OnDisable() => All.Remove(this);

    public string Prompt => "Pick up the dirty " + kind;

    public void Interact()
    {
        if (laundry) laundry.PickUpPiece(this);
    }

    static readonly string[] Kinds = { "shirt", "socks", "trousers", "hoodie", "t-shirt" };
    static readonly Color[] Colours =
    {
        new Color(0.85f, 0.85f, 0.88f), new Color(0.25f, 0.35f, 0.6f), new Color(0.75f, 0.25f, 0.3f), new Color(0.3f, 0.55f, 0.4f),
        new Color(0.2f, 0.2f, 0.24f), new Color(0.9f, 0.7f, 0.3f), new Color(0.55f, 0.4f, 0.75f),
    };

    /// <summary>A crumpled piece of clothing lying at 'at' (made of a few squashed shapes).</summary>
    public static DirtyClothesPiece Create(Vector3 at, float scale, LaundryTask laundry, int index)
    {
        var go = new GameObject("Dirty clothes (piece)");
        go.transform.position = at;
        go.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
        go.transform.localScale = Vector3.one * scale;
        var piece = go.AddComponent<DirtyClothesPiece>();
        piece.laundry = laundry;
        piece.kind = Kinds[index % Kinds.Length];
        var colour = piece.kind == "trousers" ? Jeans[Random.Range(0, Jeans.Length)] : Colours[Random.Range(0, Colours.Length)];
        var cloth = Mat(colour);
        var accent = Mat(piece.kind == "trousers" ? new Color(0.78f, 0.6f, 0.3f) : colour * 0.68f);
        var garment = new GameObject(piece.kind);
        garment.transform.SetParent(go.transform, false);
        garment.AddComponent<MeshFilter>().sharedMesh = Garment(piece.kind, Random.Range(0, 9999));
        garment.AddComponent<MeshRenderer>().sharedMaterials = new[] { cloth, accent };

        // Easy to point at (a trigger, so you don't trip over it).
        var hit = go.AddComponent<SphereCollider>();
        hit.isTrigger = true;
        hit.center = new Vector3(0f, 0.08f, 0f);
        hit.radius = 0.28f;
        var outline = go.AddComponent<Outline>();
        outline.OutlineMode = Outline.Mode.OutlineVisible;
        outline.OutlineColor = Color.white;
        outline.OutlineWidth = 5f;
        outline.enabled = false;
        return piece;
    }

    static readonly Color[] Jeans = { new Color(0.22f, 0.32f, 0.52f), new Color(0.15f, 0.18f, 0.28f), new Color(0.35f, 0.45f, 0.62f), new Color(0.2f, 0.2f, 0.22f) };

    // ------------------------------------------------------------------ garments
    // Each one is a flat outline of the real thing (seen from above, lying on the floor), crumpled with a bit of
    // noise and some folds. Region 1 = the fabric, 2 = details (collar, cuffs, pocket, waistband...).

    static float Dist(float x, float z, float cx, float cz) => Mathf.Sqrt((x - cx) * (x - cx) + (z - cz) * (z - cz));

    static int Shape(string kind, float x, float z)
    {
        float ax = Mathf.Abs(x);
        switch (kind)
        {
            case "t-shirt":
            {
                if (Dist(x, z, 0f, 0.27f) < 0.075f) return 0;                               // neck hole
                bool body = ax < 0.23f && z > -0.3f && z < 0.27f;
                bool sleeve = z < 0.27f - (ax - 0.23f) * 0.35f && z > 0.06f + (ax - 0.23f) * 0.55f && ax < 0.37f;
                if (!body && !sleeve) return 0;
                if (Dist(x, z, 0f, 0.27f) < 0.1f) return 2;                                  // collar
                if (sleeve && !body && ax > 0.34f) return 2;                                  // sleeve hems
                return 1;
            }
            case "shirt":
            {
                if (Dist(x, z, 0f, 0.29f) < 0.06f) return 0;
                bool body = ax < 0.22f && z > -0.32f && z < 0.29f;
                // long sleeves flung out at an angle
                float sx = ax - 0.2f, sz = z - 0.2f;
                float along = sx * 0.8f - sz * 0.6f, across = sx * 0.6f + sz * 0.8f;
                bool sleeve = along > 0f && along < 0.42f && Mathf.Abs(across) < 0.055f + 0.02f * (1f - along / 0.42f);
                if (!body && !sleeve) return 0;
                if (sleeve && along > 0.37f) return 2;                                       // cuffs
                if (body && (Dist(x, z, 0f, 0.29f) < 0.09f || ax < 0.012f)) return 2;        // collar + button strip
                if (body && ax < 0.03f && Mathf.Repeat(z, 0.08f) < 0.016f) return 2;           // buttons
                return 1;
            }
            case "hoodie":
            {
                bool body = ax < 0.25f && z > -0.32f && z < 0.24f;
                float sx = ax - 0.23f, sz = z - 0.18f;
                float along = sx * 0.55f - sz * 0.83f, across = sx * 0.83f + sz * 0.55f;
                bool sleeve = along > 0f && along < 0.46f && Mathf.Abs(across) < 0.07f;
                bool hood = Dist(x, z, 0f, 0.3f) < 0.15f;
                if (!body && !sleeve && !hood) return 0;
                if (hood && !body) return Dist(x, z, 0f, 0.3f) < 0.09f ? 2 : 1;            // hood, darker inside
                if (sleeve && along > 0.41f) return 2;                                       // cuffs
                if (body && z < -0.27f) return 2;                                            // waistband
                if (body && ax < 0.15f - (z + 0.2f) * 0.4f && z > -0.22f && z < -0.04f) return 2; // front pocket
                if (body && Mathf.Abs(ax - 0.04f) < 0.008f && z > 0.04f && z < 0.2f) return 2;    // drawstrings
                return 1;
            }
            case "trousers":
            {
                if (z > 0.38f || z < -0.5f) return 0;
                float half = z > 0.05f ? 0.2f : 0.2f + (0.05f - z) * 0.06f;                  // legs flare a touch
                if (ax > half) return 0;
                if (z < 0.08f && ax < 0.012f + (0.08f - z) * 0.05f) return 0;                // gap between the legs
                if (z > 0.33f) return 2;                                                     // waistband
                if (z < -0.46f) return 2;                                                    // hems
                if (ax < 0.008f && z > 0.18f) return 2;                                      // fly seam
                if (Dist(ax, z, 0.2f, 0.33f) < 0.09f && Dist(ax, z, 0.2f, 0.33f) > 0.075f) return 2; // pocket seams
                return 1;
            }
            default: // socks: a pair, one lying across the other
            {
                int a = Sock(x + 0.05f, z), b2 = Sock(-(z - 0.02f) * 0.9f - 0.02f, (x - 0.06f) * 0.9f + 0.03f);
                return Mathf.Max(a, b2);
            }
        }
    }

    static int Sock(float x, float z)
    {
        bool leg = Mathf.Abs(x) < 0.045f && z > -0.08f && z < 0.16f;
        bool foot = z > -0.13f && z < -0.04f && x > -0.045f && x < 0.15f && (x < 0.11f || Dist(x, z, 0.11f, -0.085f) < 0.045f);
        bool heel = Dist(x, z, -0.01f, -0.08f) < 0.05f;
        if (!leg && !foot && !heel) return 0;
        if (leg && z > 0.12f) return 2;                       // cuff
        if (foot && x > 0.11f) return 2;                      // toe
        if (heel && x < -0.02f && z < -0.08f) return 2;       // heel
        return 1;
    }

    /// <summary>The garment as a crumpled mesh lying flat (submesh 0 fabric, 1 details).</summary>
    static Mesh Garment(string kind, int seed)
    {
        const float cell = 0.014f, ext = 0.52f;
        int n = Mathf.CeilToInt(ext * 2f / cell);
        var rng = new System.Random(seed);
        float ox = (float)rng.NextDouble() * 50f, oz = (float)rng.NextDouble() * 50f;
        // A couple of random folds (ridges) across the fabric.
        var folds = new List<(Vector2 dir, float at, float h)>();
        for (int i = 0; i < 3; i++)
        {
            float ang = (float)rng.NextDouble() * Mathf.PI;
            folds.Add((new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)), (float)(rng.NextDouble() - 0.5) * 0.3f, 0.01f + (float)rng.NextDouble() * 0.015f));
        }

        var region = new int[n, n];
        for (int i = 0; i < n; i++)
        for (int j = 0; j < n; j++)
            region[i, j] = Shape(kind, -ext + (i + 0.5f) * cell, -ext + (j + 0.5f) * cell);

        // Height at each grid corner: thicker inside, thin at the edges, plus wrinkles and folds.
        var verts = new List<Vector3>();
        var index = new int[n + 1, n + 1];
        for (int i = 0; i <= n; i++)
        for (int j = 0; j <= n; j++)
        {
            index[i, j] = -1;
            int inside = 0, near = 0;
            for (int di = -3; di <= 2; di++)
            for (int dj = -3; dj <= 2; dj++)
            {
                int a = i + di, c = j + dj;
                near++;
                if (a >= 0 && c >= 0 && a < n && c < n && region[a, c] > 0) inside++;
            }
            bool used = false;
            for (int di = -1; di <= 0 && !used; di++)
            for (int dj = -1; dj <= 0 && !used; dj++)
            {
                int a = i + di, c = j + dj;
                used = a >= 0 && c >= 0 && a < n && c < n && region[a, c] > 0;
            }
            if (!used) continue;
            float x = -ext + i * cell, z = -ext + j * cell;
            float body = Mathf.Clamp01(inside / (float)near * 1.6f - 0.3f);
            float h = 0.003f + 0.012f * body;
            h += body * 0.018f * Mathf.PerlinNoise(ox + x * 9f, oz + z * 9f);
            h += body * 0.006f * Mathf.PerlinNoise(ox + x * 30f, oz + z * 30f);
            foreach (var f in folds)
            {
                float d = Vector2.Dot(new Vector2(x, z), f.dir) - f.at;
                h += body * f.h * Mathf.Exp(-d * d / 0.0012f);
            }
            index[i, j] = verts.Count;
            verts.Add(new Vector3(x, h, z));
        }

        var tris = new[] { new List<int>(), new List<int>() };
        for (int i = 0; i < n; i++)
        for (int j = 0; j < n; j++)
        {
            int r = region[i, j];
            if (r == 0) continue;
            int a = index[i, j], b = index[i + 1, j], c = index[i, j + 1], d = index[i + 1, j + 1];
            var list = tris[r == 2 ? 1 : 0];
            list.AddRange(new[] { a, c, b, b, c, d });
        }
        var m = new Mesh { name = kind };
        m.SetVertices(verts);
        m.subMeshCount = 2;
        m.SetTriangles(tris[0], 0);
        m.SetTriangles(tris[1], 1);
        m.RecalculateNormals();
        m.RecalculateBounds();
        return m;
    }

    // ------------------------------------------------------------------ shared with the wardrobe / folding game

    /// <summary>0 outside, 1 fabric, 2 details - the garment's outline (x across, z from hem to collar, about -0.52..0.52).</summary>
    internal static int ShapeAt(string kind, float x, float z) => Shape(kind, x, z);

    internal static Color RandomColour(string kind) => kind == "trousers" ? Jeans[Random.Range(0, Jeans.Length)] : Colours[Random.Range(0, Colours.Length)];

    internal static Material[] Materials(string kind, Color colour) =>
        new[] { Mat(colour), Mat(kind == "trousers" ? new Color(0.78f, 0.6f, 0.3f) : colour * 0.68f) };

    /// <summary>The garment as a flat, two-sided cut-out (no crumple), lying in the XZ plane, 'thickness' thick. Submesh 0 fabric, 1 details.</summary>
    internal static Mesh FlatGarment(string kind, float thickness = 0.008f)
    {
        const float cell = 0.012f, ext = 0.52f;
        int n = Mathf.CeilToInt(ext * 2f / cell);
        var region = new int[n, n];
        for (int i = 0; i < n; i++)
        for (int j = 0; j < n; j++)
            region[i, j] = Shape(kind, -ext + (i + 0.5f) * cell, -ext + (j + 0.5f) * cell);
        var verts = new List<Vector3>();
        var top = new int[n + 1, n + 1];
        var bottom = new int[n + 1, n + 1];
        for (int i = 0; i <= n; i++)
        for (int j = 0; j <= n; j++)
        {
            top[i, j] = bottom[i, j] = -1;
            bool used = false;
            for (int di = -1; di <= 0 && !used; di++)
            for (int dj = -1; dj <= 0 && !used; dj++)
            {
                int a = i + di, c = j + dj;
                used = a >= 0 && c >= 0 && a < n && c < n && region[a, c] > 0;
            }
            if (!used) continue;
            float x = -ext + i * cell, z = -ext + j * cell;
            top[i, j] = verts.Count; verts.Add(new Vector3(x, thickness * 0.5f, z));
            bottom[i, j] = verts.Count; verts.Add(new Vector3(x, -thickness * 0.5f, z));
        }
        var tris = new[] { new List<int>(), new List<int>() };
        for (int i = 0; i < n; i++)
        for (int j = 0; j < n; j++)
        {
            int r = region[i, j];
            if (r == 0) continue;
            var list = tris[r == 2 ? 1 : 0];
            list.AddRange(new[] { top[i, j], top[i, j + 1], top[i + 1, j], top[i + 1, j], top[i, j + 1], top[i + 1, j + 1] });
            list.AddRange(new[] { bottom[i, j], bottom[i + 1, j], bottom[i, j + 1], bottom[i + 1, j], bottom[i + 1, j + 1], bottom[i, j + 1] });
        }
        var m = new Mesh { name = kind + " (flat)" };
        m.SetVertices(verts);
        m.subMeshCount = 2;
        m.SetTriangles(tris[0], 0);
        m.SetTriangles(tris[1], 1);
        m.RecalculateNormals();
        m.RecalculateBounds();
        return m;
    }

    static Material Mat(Color c)
    {
        var m = new Material(Shader.Find("Standard")) { color = c };
        m.SetFloat("_Glossiness", 0.05f);
        return m;
    }
}
