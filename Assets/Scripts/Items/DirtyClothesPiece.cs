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
        // no cells touching only at a corner (a slanted sleeve's edge): those pinch points turn into spikes when the edge is smoothed
        for (int pass = 0; pass < 2; pass++)
        for (int i = 0; i + 1 < n; i++)
        for (int j = 0; j + 1 < n; j++)
        {
            int ra = region[i, j], rb = region[i + 1, j], rc = region[i, j + 1], rd = region[i + 1, j + 1];
            if (ra > 0 && rd > 0 && rb == 0 && rc == 0) region[i + 1, j] = ra;
            else if (rb > 0 && rc > 0 && ra == 0 && rd == 0) region[i, j] = rb;
        }

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
        // a clean, rounded cut edge (the grid's staircase reads as a ragged fringe, worst once the cloth hangs)
        var va = verts.ToArray();
        var everyTri = new List<int>(tris[0]);
        everyTri.AddRange(tris[1]);
        SmoothRim(va, everyTri, 12);
        verts = new List<Vector3>(va);
        var m = new Mesh { name = kind };
        m.SetVertices(verts);
        m.subMeshCount = 2;
        m.SetTriangles(tris[0], 0);
        m.SetTriangles(tris[1], 1);
        m.RecalculateNormals();
        m.RecalculateBounds();
        return m;
    }

    /// <summary>Rounds off a sheet's outer edge in the XZ plane (Taubin smoothing: it doesn't shrink); heights stay as they are.</summary>
    static void SmoothRim(Vector3[] v, List<int> tris, int iterations)
    {
        var edges = new Dictionary<long, int>();
        for (int t = 0; t < tris.Count; t += 3)
        for (int e = 0; e < 3; e++)
        {
            int p0 = tris[t + e], p1 = tris[t + (e + 1) % 3];
            long key = p0 < p1 ? ((long)p0 << 32) | (uint)p1 : ((long)p1 << 32) | (uint)p0;
            edges[key] = edges.TryGetValue(key, out int c) ? c + 1 : 1;
        }
        var rim = new Dictionary<int, List<int>>();
        foreach (var kv in edges)
        {
            if (kv.Value != 1) continue;
            int p0 = (int)(kv.Key >> 32), p1 = (int)(kv.Key & 0xffffffffL);
            if (!rim.TryGetValue(p0, out var l0)) rim[p0] = l0 = new List<int>();
            if (!rim.TryGetValue(p1, out var l1)) rim[p1] = l1 = new List<int>();
            l0.Add(p1); l1.Add(p0);
        }
        var nv = new Vector3[v.Length];
        for (int it = 0; it < iterations * 2; it++)
        {
            float k = it % 2 == 0 ? 0.55f : -0.57f;
            System.Array.Copy(v, nv, v.Length);
            foreach (var kv in rim)
            {
                Vector3 avg = Vector3.zero;
                foreach (int o in kv.Value) avg += v[o];
                avg /= kv.Value.Count;
                var p = v[kv.Key];
                nv[kv.Key] = new Vector3(p.x + (avg.x - p.x) * k, p.y, p.z + (avg.z - p.z) * k);
            }
            System.Array.Copy(nv, v, v.Length);
        }
    }

    // ------------------------------------------------------------------ clean laundry: a neat stack of folded clothes

    /// <summary>
    /// A stack of folded clothes (bottom at y = 0, centred), one per (kind, colour): soft puffy folded shapes with a crease, a
    /// collar on the shirts and tops, a waistband on the jeans. 'size' is the folded footprint's width in metres.
    /// Two submeshes per garment (fabric, details); 'mats' matches them.
    /// </summary>
    internal static Mesh FoldedStack(List<(string kind, Color colour)> items, float size, out Material[] mats)
    {
        var verts = new List<Vector3>();
        var subs = new List<List<int>>();
        var matList = new List<Material>();
        var rng = new System.Random(items.Count * 31 + 7);
        float y = 0f;
        for (int i = 0; i < items.Count; i++)
        {
            var (kind, colour) = items[i];
            bool jeans = kind == "trousers";
            float w = size * (jeans ? 0.86f : 1f), d = size * (jeans ? 1.12f : 1.05f), t = size * (jeans ? 0.13f : 0.11f);
            if (kind == "socks") { w = size * 0.42f; d = size * 0.36f; t = size * 0.15f; }
            float jx = ((float)rng.NextDouble() - 0.5f) * size * 0.06f, jz = ((float)rng.NextDouble() - 0.5f) * size * 0.06f;
            float yaw = ((float)rng.NextDouble() - 0.5f) * 10f;
            var rot = Quaternion.Euler(0f, yaw, 0f);
            Vector3 at = new Vector3(jx, y, jz);
            var fabric = new List<int>();
            var detail = new List<int>();
            Pillow(verts, fabric, w, d, t, rot, at, jeans);
            if (kind == "socks") { }
            else if (jeans) Band(verts, detail, w, d, t, rot, at);                 // waistband along the top fold
            else Collar(verts, detail, w, d, t, rot, at, kind == "hoodie");        // collar / hood edge
            subs.Add(fabric); subs.Add(detail);
            var bright = Color.Lerp(colour, Color.white, 0.1f);                    // fresh out of the wash
            var mm = Materials(kind, bright);
            matList.Add(mm[0]); matList.Add(mm[1]);
            y += t * 0.94f;                                                        // (each one squashes the one under it a touch)
        }
        var m = new Mesh { name = "Folded clothes" };
        m.SetVertices(verts);
        m.subMeshCount = subs.Count;
        for (int i = 0; i < subs.Count; i++) m.SetTriangles(subs[i], i);
        m.RecalculateNormals();
        m.RecalculateBounds();
        mats = matList.ToArray();
        return m;
    }

    /// <summary>A folded garment: a rounded-rectangle outline, soft rounded edges, a slightly domed top with a fold crease.</summary>
    static void Pillow(List<Vector3> verts, List<int> tris, float w, float d, float t, Quaternion rot, Vector3 at, bool jeans)
    {
        const int N = 56;   // round the outline
        const int R = 7;    // rings from the edge in to the middle
        float corner = Mathf.Min(w, d) * 0.16f;
        // outline point i (a rounded rectangle), as a direction scaled 0..1 to the edge
        Vector2 Edge(int i)
        {
            float a = i / (float)N * Mathf.PI * 2f;
            float cx = Mathf.Cos(a), cz = Mathf.Sin(a);
            // superellipse: squarish with soft corners
            const float n = 8f;
            float r = Mathf.Pow(Mathf.Pow(Mathf.Abs(cx), n) + Mathf.Pow(Mathf.Abs(cz), n), -1f / n);
            return new Vector2(cx * r * (w * 0.5f), cz * r * (d * 0.5f));
        }
        int Add(Vector3 p) { verts.Add(at + rot * p); return verts.Count - 1; }
        // the folds: one across it, and (a shirt) where the sleeves fold under, or (jeans) the seam down the middle
        float Crease(float x, float z)
        {
            float c = 0.16f * t * Mathf.Exp(-(z - d * 0.14f) * (z - d * 0.14f) / (d * d * 0.004f));
            if (jeans) c += 0.1f * t * Mathf.Exp(-x * x / (w * w * 0.0015f));
            else { float ax = Mathf.Abs(x) - w * 0.27f; c += 0.12f * t * Mathf.Exp(-ax * ax / (w * w * 0.0018f)); }
            return c;
        }
        // the sides: bottom ring (tucked in a little), a bulging middle ring, the top edge ring
        var rings = new List<int[]>();
        float[] sideY = { 0f, t * 0.45f, t * 0.84f };
        float[] sideIn = { corner * 0.35f, 0f, corner * 0.22f };
        foreach (var (sy, si) in new[] { (sideY[0], sideIn[0]), (sideY[1], sideIn[1]), (sideY[2], sideIn[2]) })
        {
            var ring = new int[N];
            for (int i = 0; i < N; i++)
            {
                var e = Edge(i);
                float len = e.magnitude;
                var p = len > 1e-5f ? e * ((len - si) / len) : e;
                ring[i] = Add(new Vector3(p.x, sy, p.y));
            }
            rings.Add(ring);
        }
        // the top: rings in towards the middle, domed, with the crease
        for (int r = 1; r <= R; r++)
        {
            float f = 1f - r / (float)R;                              // 1 at the edge, 0 at the middle
            var ring = new int[N];
            for (int i = 0; i < N; i++)
            {
                var e = Edge(i);
                float len = e.magnitude;
                float inset = sideIn[2] + (len - sideIn[2]) * (1f - f);
                var p = len > 1e-5f ? e * ((len - inset) / len) : e;
                float dome = t * (0.84f + 0.16f * Mathf.Sqrt(1f - f * f));
                ring[i] = Add(new Vector3(p.x, dome - Crease(p.x, p.y), p.y));
            }
            rings.Add(ring);
        }
        for (int k = 0; k + 1 < rings.Count; k++)
        for (int i = 0; i < N; i++)
        {
            int j = (i + 1) % N;
            int a = rings[k][i], b = rings[k][j], c = rings[k + 1][i], dd = rings[k + 1][j];
            tris.AddRange(new[] { a, c, b, b, c, dd });
        }
        // cap the middle and the bottom
        var last = rings[rings.Count - 1];
        int mid = Add(new Vector3(0f, t - Crease(0f, 0f), 0f));
        for (int i = 0; i < N; i++) tris.AddRange(new[] { last[i], mid, last[(i + 1) % N] });
        int bot = Add(Vector3.zero);
        var first = rings[0];
        for (int i = 0; i < N; i++) tris.AddRange(new[] { first[(i + 1) % N], bot, first[i] });
    }

    /// <summary>A round collar on top of a folded shirt (near its top edge): a little curved tube.</summary>
    static void Collar(List<Vector3> verts, List<int> tris, float w, float d, float t, Quaternion rot, Vector3 at, bool hood)
    {
        int segs = 16, around = 8;
        float rad = w * (hood ? 0.17f : 0.13f), tube = t * (hood ? 0.3f : 0.2f);
        Vector3 centre = new Vector3(0f, t * 0.93f, d * 0.5f - rad * 0.3f);
        int start = verts.Count;
        for (int s = 0; s <= segs; s++)
        {
            float a = Mathf.Lerp(-Mathf.PI * 0.95f, -Mathf.PI * 0.05f, s / (float)segs); // a U round the neck: open at the top edge, rounding in towards the middle
            Vector3 c = centre + new Vector3(Mathf.Cos(a) * rad, 0f, Mathf.Sin(a) * rad * 0.8f);
            Vector3 outDir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a) * 0.8f).normalized;
            for (int k = 0; k < around; k++)
            {
                float b = k / (float)around * Mathf.PI * 2f;
                Vector3 p = c + (outDir * Mathf.Cos(b) * 1.3f + Vector3.up * Mathf.Sin(b) * 0.55f) * tube;
                verts.Add(at + rot * p);
            }
        }
        for (int s = 0; s < segs; s++)
        for (int k = 0; k < around; k++)
        {
            int k2 = (k + 1) % around;
            int a = start + s * around + k, b = start + s * around + k2, c = start + (s + 1) * around + k, dd = start + (s + 1) * around + k2;
            tris.AddRange(new[] { a, b, c, b, dd, c });
        }
    }

    /// <summary>The jeans' waistband: a slightly raised strip along the top edge, with a button.</summary>
    static void Band(List<Vector3> verts, List<int> tris, float w, float d, float t, Quaternion rot, Vector3 at)
    {
        float bw = w * 0.84f, bd = d * 0.09f, h = t * 0.08f;
        Vector3 c = new Vector3(0f, t * 0.99f, d * 0.5f - bd * 0.5f - d * 0.06f);
        Box(verts, tris, c, new Vector3(bw, h * 2f, bd), rot, at);
        Box(verts, tris, c + new Vector3(bw * 0.3f, h * 1.3f, 0f), new Vector3(bd * 0.45f, h * 1.6f, bd * 0.45f), rot, at);
    }

    static void Box(List<Vector3> verts, List<int> tris, Vector3 c, Vector3 size, Quaternion rot, Vector3 at)
    {
        Vector3 h = size * 0.5f;
        var faces = new[] { (Vector3.up, Vector3.right, Vector3.forward), (Vector3.down, Vector3.forward, Vector3.right),
                            (Vector3.right, Vector3.forward, Vector3.up), (Vector3.left, Vector3.up, Vector3.forward),
                            (Vector3.forward, Vector3.up, Vector3.right), (Vector3.back, Vector3.right, Vector3.up) };
        foreach (var (n, u, v) in faces)
        {
            int s0 = verts.Count;
            Vector3 fc = c + Vector3.Scale(n, h);
            Vector3 du = Vector3.Scale(u, h), dv = Vector3.Scale(v, h);
            verts.Add(at + rot * (fc - du - dv)); verts.Add(at + rot * (fc + du - dv));
            verts.Add(at + rot * (fc + du + dv)); verts.Add(at + rot * (fc - du + dv));
            tris.AddRange(new[] { s0, s0 + 2, s0 + 1, s0, s0 + 3, s0 + 2 });
        }
    }

    // ------------------------------------------------------------------ shared with the wardrobe / folding game

    /// <summary>0 outside, 1 fabric, 2 details - the garment's outline (x across, z from hem to collar, about -0.52..0.52).</summary>
    internal static int ShapeAt(string kind, float x, float z) => Shape(kind, x, z);

    /// <summary>A garment kind for the i-th piece (as the floor pieces cycle through them).</summary>
    internal static string KindFor(int i) => Kinds[((i % Kinds.Length) + Kinds.Length) % Kinds.Length];

    /// <summary>The garment crumpled and draped over a rounded heap (like it's on the armful you're carrying): the fold
    /// runs across it, both ends hang down, the sleeves droop. Two-sided. 'size' scales the garment, 'radius' is the
    /// heap it's draped over. Submesh 0 fabric, 1 details.</summary>
    internal static Mesh DrapedGarment(string kind, int seed, float size, float radius)
    {
        var flat = Garment(kind, seed);
        var rng = new System.Random(seed + 7);
        float fold = (float)(rng.NextDouble() - 0.5) * 0.14f;
        var v = flat.vertices;
        const float quarter = Mathf.PI * 0.5f;
        for (int i = 0; i < v.Length; i++)
        {
            float x = v[i].x * size, h = v[i].y * size * 1.1f, z = (v[i].z - fold) * size;
            float r = radius + h, a = z / radius;
            Vector3 p;
            a = Mathf.Sign(a) * Mathf.Min(Mathf.Abs(a), 1.15f) + Mathf.Sign(a) * Mathf.Max(0f, Mathf.Abs(a) - 1.15f) * 0.15f; // (it lies over the pile: no flapping ends)
            if (Mathf.Abs(a) <= quarter) p = new Vector3(x, r * Mathf.Cos(a) - radius, r * Mathf.Sin(a));
            else
            {
                // past the side of the heap it hangs down, flaring out a little
                float extra = (Mathf.Abs(a) - quarter) * radius, sg = Mathf.Sign(a);
                p = new Vector3(x, -radius - extra * 0.7f, sg * (r + extra * 0.3f));
            }
            float over = Mathf.Max(0f, Mathf.Abs(x) - 0.12f);
            p.y -= over * over * 1.5f;                       // sleeves / legs droop a little
            p.x = Mathf.Sign(p.x) * (Mathf.Min(Mathf.Abs(p.x), 0.12f) + over * 0.55f); // ...and tuck in towards the heap
            v[i] = p;
        }
        var front = new Mesh();
        front.SetVertices(v);
        front.subMeshCount = 2;
        front.SetTriangles(flat.GetTriangles(0), 0);
        front.SetTriangles(flat.GetTriangles(1), 1);
        front.RecalculateNormals();
        var nrm = front.normals;
        var tris = new[] { front.GetTriangles(0), front.GetTriangles(1) };
        Object.Destroy(flat);
        Object.Destroy(front);

        // the underside: the same sheet, a hair below, facing the other way (so no holes from any angle)
        int n = v.Length;
        var verts = new List<Vector3>(v);
        for (int i = 0; i < n; i++) verts.Add(v[i] - nrm[i] * 0.004f);
        var m = new Mesh { name = kind + " (draped)" };
        m.SetVertices(verts);
        m.subMeshCount = 2;
        for (int s = 0; s < 2; s++)
        {
            var t = new List<int>(tris[s]);
            int c = t.Count;
            for (int k = 0; k < c; k += 3) { t.Add(t[k] + n); t.Add(t[k + 2] + n); t.Add(t[k + 1] + n); }
            m.SetTriangles(t, s);
        }
        m.RecalculateNormals();
        m.RecalculateBounds();
        return m;
    }

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
