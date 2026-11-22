using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Chopping on the cutting board, seen from above and a little behind you - the real board in the real kitchen, in 3D.
/// The ingredient lies across in front of you with a thin white ring around it where each cut should go. A chef's knife hangs over
/// it, edge down, handle towards you, exactly as you would hold it; a glowing line on the board and around the ingredient shows where
/// the blade will land (green when it is on a cut). Move the knife with the mouse (or A / D) and chop with a click (or Space):
/// on a line = a clean cut and the piece slides off; far from every line = a miss. Every line cut = done. Esc stops (you keep the item).
/// Quality 0..1 = how close the cuts were, minus a bit for each miss.
/// </summary>
public class CuttingGame : MonoBehaviour, IMainTask
{
    public static bool Running { get; private set; }

    static CuttingGame instance;
    Transform board;
    Cooking.Ingredient ing;
    System.Action<float> onDone;
    System.Action onCancel;

    Transform root, knife, guideBoard, guideRing, guideShape;
    System.Func<float, float> profile;
    float stemUntil = float.NegativeInfinity; // a mushroom: up to here along it is stem (pale), the rest cap // the ingredient's radius along its length (x from its middle); null for a slab
    Material guideMat;
    Camera cam;
    Canvas ui;
    TextMeshProUGUI title, hint, judge, counter;
    readonly List<Transform> pieces = new List<Transform>();
    readonly List<float> pieceX0 = new List<float>(); // where each piece started (they slide apart as you cut)
    readonly List<Transform> lineMarks = new List<Transform>();
    readonly List<Renderer> hidden = new List<Renderer>();
    float[] lines;
    bool[] cutDone;
    float length, segment, radius, halfDepth, top, idleLift, knifeX, chopAnim = -1f, judgeTime, finishAt = -1f;
    readonly List<float> scores = new List<float>();
    int misses;
    bool running, slab;

    static readonly Color GuideOff = new Color(1f, 0.72f, 0.25f), GuideOn = new Color(0.35f, 1f, 0.45f);

    public static void Begin(Transform board, Cooking.Ingredient ing, System.Action<float> done, System.Action cancelled)
    {
        if (!PlayerInteraction.Instance) { done?.Invoke(1f); return; }
        if (!instance) instance = new GameObject("Cutting game").AddComponent<CuttingGame>();
        instance.board = board;
        instance.ing = ing;
        instance.onDone = done;
        instance.onCancel = cancelled;
        if (!PlayerInteraction.Instance.BeginMainTask(instance)) cancelled?.Invoke();
    }

    public bool ShouldBlockInput() => true;

    public void StartMainTask()
    {
        running = Running = true;
        HudHider.Hide();
        scores.Clear();
        misses = 0;
        finishAt = -1f;
        chopAnim = -1f;
        Build();
    }

    public void StopMainTask()
    {
        if (!running) return;
        Clear();
        onCancel?.Invoke();
    }

    void Finish()
    {
        float avg = 0f;
        foreach (var s in scores) avg += s;
        avg = scores.Count > 0 ? avg / scores.Count : 1f;
        float q = Mathf.Clamp01(avg - misses * 0.08f);
        Clear();
        if (PlayerInteraction.Instance) PlayerInteraction.Instance.ReleaseMainTask(this);
        onDone?.Invoke(q);
    }

    void Clear()
    {
        running = Running = false;
        HudHider.Show();
        foreach (var r in hidden) if (r) r.enabled = true;
        hidden.Clear();
        if (root) Destroy(root.gameObject);
        if (ui) Destroy(ui.gameObject);
        pieces.Clear();
        pieceX0.Clear();
        lineMarks.Clear();
    }

    static Material Lit(Color c, float gloss, float metal = 0f)
    {
        var m = new Material(Shader.Find("Standard")) { color = c };
        m.SetFloat("_Glossiness", gloss);
        m.SetFloat("_Metallic", metal);
        return m;
    }

    static Material Glow(Color c, float strength)
    {
        var m = new Material(Shader.Find("Standard")) { color = c };
        m.EnableKeyword("_EMISSION");
        m.SetColor("_EmissionColor", c * strength);
        return m;
    }

    Transform Prim(PrimitiveType type, string n, Transform parent, Vector3 pos, Vector3 euler, Vector3 scale, Material m)
    {
        var p = GameObject.CreatePrimitive(type);
        Destroy(p.GetComponent<Collider>());
        p.name = n;
        p.transform.SetParent(parent, false);
        p.transform.localPosition = pos;
        p.transform.localEulerAngles = euler;
        p.transform.localScale = scale;
        p.GetComponent<Renderer>().sharedMaterial = m;
        return p.transform;
    }

    // ------------------------------------------------------------------ setting up the board

    void Build()
    {
        float k = Cooking.K;
        var b = Cooking.Bounds(board.gameObject);
        // Set it up the way you stand: "away from you" is the far side of the board, the knife's handle points back at you.
        Vector3 away = Vector3.forward;
        var player = PlayerInteraction.Instance ? PlayerInteraction.Instance.transform : null;
        if (player)
        {
            Vector3 d = b.center - player.position; d.y = 0f;
            away = Mathf.Abs(d.x) > Mathf.Abs(d.z) ? new Vector3(Mathf.Sign(d.x), 0f, 0f) : new Vector3(0f, 0f, Mathf.Sign(d.z));
            // Your own body / arms would be right in front of this camera: hidden while you chop.
            foreach (var r in player.GetComponentsInChildren<Renderer>())
                if (r.enabled) { r.enabled = false; hidden.Add(r); }
        }
        root = new GameObject("Cutting board view").transform;
        root.position = new Vector3(b.center.x, b.max.y + 0.002f, b.center.z);
        root.rotation = Quaternion.LookRotation(away, Vector3.up); // local +x = along the ingredient (left to right), +z = away from you

        // ---- the ingredient, lying across in front of you
        slab = ing.shape == "slab";
        int cuts = Mathf.Max(1, ing.cuts);
        length = Mathf.Max(0.12f, ing.size * k * (ing.shape == "round" ? 1.25f : 1f));
        segment = length / (cuts + 1);
        radius = ing.shape == "long" ? Mathf.Max(0.018f, ing.size * k * 0.14f) : slab ? ing.size * k * 0.35f : ing.size * k * 0.5f;
        var skin = Lit(ing.id == "mushroom" ? new Color(0.62f, 0.47f, 0.35f) : ing.colour, 0.35f); // a mushroom's cap is browner than its stem
        var stem = Lit(new Color(0.93f, 0.9f, 0.82f), 0.3f);
        stemUntil = float.NegativeInfinity;
        var inside = Lit(Color.Lerp(ing.colour, Color.white, 0.35f), 0.2f);
        float half = length * 0.5f;
        if (slab) profile = null;
        else if (ing.shape == "long")
        {
            // cucumber: a little fatter in the middle; carrot: thick at the top, thin at the tip. Both rounded off at the ends.
            bool carrot = ing.id == "carrot";
            profile = x =>
            {
                float t = Mathf.Clamp(x / half, -1f, 1f);
                float r = carrot ? radius * Mathf.Lerp(1f, 0.3f, (t + 1f) * 0.5f) : radius * (1f - 0.15f * t * t);
                float end = Mathf.Max(0f, Mathf.Abs(t) - 0.86f) / 0.14f;
                return r * Mathf.Sqrt(Mathf.Max(0f, 1f - end * end));
            };
        }
        else if (ing.id == "mushroom")
        {
            // A mushroom lying on its side: a pale stem (left), then the cap - wide at the gills, domed to the top (right).
            profile = x =>
            {
                float t = Mathf.Clamp(x / half, -1f, 1f);
                if (t < -0.15f) return radius * (0.36f + 0.06f * (t + 1f));                  // the stem, a touch wider towards the cap
                float u = (t + 0.15f) / 1.15f;                                                // 0 at the gills .. 1 at the top of the cap
                return radius * Mathf.Sqrt(Mathf.Max(0f, 1f - u * u));
            };
            stemUntil = -half * 0.15f;
        }
        else profile = x => { float t = Mathf.Clamp(x / half, -1f, 1f); return radius * Mathf.Sqrt(Mathf.Max(0f, 1f - t * t)); };
        for (int i = 0; i <= cuts; i++)
        {
            float x0 = -half + segment * i, x1 = x0 + segment, xc = (x0 + x1) * 0.5f;
            if (slab)
            {
                float h = radius * 0.5f;
                pieces.Add(Prim(PrimitiveType.Cube, "Piece", root, new Vector3(xc, h * 0.5f, 0f), Vector3.zero, new Vector3(segment * 0.98f, h, radius * 1.4f), skin));
            }
            else
            {
                // one smooth slice of the whole shape: its skin outside, the lighter inside on the cut faces
                var go = new GameObject("Piece");
                go.transform.SetParent(root, false);
                go.transform.localPosition = new Vector3(xc, radius, 0f);
                float cx = xc, until = stemUntil;
                go.AddComponent<MeshFilter>().sharedMesh = Lathe(x0 + segment * 0.01f - xc, x1 - segment * 0.01f - xc, x => profile(x + cx), 6, 24,
                    float.IsNegativeInfinity(until) ? (System.Func<float, bool>)null : x => x + cx < until);
                go.AddComponent<MeshRenderer>().sharedMaterials = new[] { skin, inside, stem };
                pieces.Add(go.transform);
            }
            pieceX0.Add(xc);
        }
        top = slab ? radius * 0.5f : radius * 2f;
        halfDepth = slab ? radius * 0.7f : radius;

        // ---- where to cut: a thin white ring around the ingredient + short dashes on the board either side
        lines = new float[cuts];
        cutDone = new bool[cuts];
        var white = Glow(Color.white, 0.7f);
        for (int i = 0; i < cuts; i++)
        {
            lines[i] = -length * 0.5f + segment * (i + 1);
            var mark = new GameObject("Cut line").transform;
            mark.SetParent(root, false);
            mark.localPosition = new Vector3(lines[i], 0f, 0f);
            Ring(mark, RadiusAt(lines[i]), 0.0025f, white);
            for (int side = -1; side <= 1; side += 2)
                for (int d = 0; d < 2; d++)
                {
                    float z = side * (halfDepth + 0.012f + d * 0.022f + 0.007f);
                    Prim(PrimitiveType.Cube, "Dash", mark, new Vector3(0f, 0.0008f, z), Vector3.zero, new Vector3(0.0025f, 0.0012f, 0.014f), white);
                }
            lineMarks.Add(mark);
        }

        // ---- the guide: where the blade will land, following the knife
        guideMat = Glow(GuideOff, 1.1f);
        guideBoard = Prim(PrimitiveType.Cube, "Knife guide (board)", root, Vector3.zero, Vector3.zero, new Vector3(0.0022f, 0.001f, halfDepth * 2f + 0.07f), guideMat);
        guideRing = new GameObject("Knife guide (ring)").transform;
        guideRing.SetParent(root, false);
        guideShape = Ring(guideRing, radius, 0.004f, guideMat);

        // ---- the knife: a chef's knife, blade standing on its edge across the ingredient, the handle towards you
        float bladeLen = Mathf.Max(0.2f, (halfDepth * 2f + 0.04f) / 0.6f); // the straight part of the edge covers the whole ingredient
        float bladeH = 0.046f, handleLen = 0.12f;
        float heelZ = -halfDepth - 0.025f;
        knife = new GameObject("Knife").transform;
        knife.SetParent(root, false);
        knife.localRotation = Quaternion.Euler(0f, 0f, -12f);
        var model = new GameObject("Chef's knife").transform;
        model.SetParent(knife, false);
        model.localPosition = new Vector3(0f, 0f, heelZ);
        var steel = Lit(new Color(0.8f, 0.82f, 0.86f), 0.82f, 0.7f);
        var blade = new GameObject("Blade");
        blade.transform.SetParent(model, false);
        blade.AddComponent<MeshFilter>().sharedMesh = BladeMesh(bladeLen, bladeH, 0.0026f);
        blade.AddComponent<MeshRenderer>().sharedMaterial = steel;
        var handleMat = Lit(new Color(0.17f, 0.12f, 0.09f), 0.45f);
        var brass = Lit(new Color(0.86f, 0.72f, 0.4f), 0.7f, 0.8f);
        float hy = bladeH * 0.64f;
        Prim(PrimitiveType.Cube, "Bolster", model, new Vector3(0f, hy, -0.005f), Vector3.zero, new Vector3(0.008f, 0.03f, 0.012f), steel);
        Prim(PrimitiveType.Capsule, "Handle", model, new Vector3(0f, hy, -0.011f - handleLen * 0.5f), new Vector3(90f, 0f, 0f), new Vector3(0.019f, handleLen * 0.5f, 0.027f), handleMat);
        for (int r = 0; r < 3; r++)
            Prim(PrimitiveType.Cylinder, "Rivet", model, new Vector3(0f, hy, -0.011f - handleLen * (0.25f + 0.25f * r)), new Vector3(0f, 0f, 90f), new Vector3(0.0055f, 0.0101f, 0.0055f), brass);

        idleLift = top + 0.035f;
        knifeX = -length * 0.5f - segment * 0.5f;
        knife.localPosition = new Vector3(knifeX, idleLift, 0f);

        // ---- the camera: above and a little behind / left of you, looking down at the board (the real kitchen around it)
        cam = new GameObject("Cutting camera").AddComponent<Camera>();
        cam.transform.SetParent(root, false);
        cam.transform.localRotation = Quaternion.Euler(50f, 18f, 0f);
        cam.fieldOfView = 40f;
        cam.nearClipPlane = 0.02f;
        cam.farClipPlane = 60f;
        cam.depth = 50f;
        // Frame everywhere the knife can be (the blade from heel to tip, at the top of its lift) and the board marks either side of the
        // ingredient - as close as that allows, centred between the title and the hint. The handle runs off the bottom, like your hand.
        float reach = length * 0.5f + segment;
        var pts = new List<Vector3>();
        foreach (float x in new[] { -reach, reach })
        {
            pts.Add(new Vector3(x, 0f, -halfDepth - 0.04f));
            pts.Add(new Vector3(x, 0f, halfDepth + 0.05f));
            pts.Add(new Vector3(x, idleLift + bladeH, heelZ + bladeLen));
            pts.Add(new Vector3(x, idleLift + bladeH, heelZ - 0.01f));
            pts.Add(new Vector3(x, top, 0f));
        }
        Vector3 aim = Vector3.zero;
        foreach (var q in pts) aim += q;
        aim /= pts.Count;
        Quaternion look = cam.transform.localRotation;
        Vector3 fwd = look * Vector3.forward, rightDir = look * Vector3.right, upDir = look * Vector3.up;
        float tanY = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
        Vector3 best = aim - fwd * 0.8f;
        for (float dist = 2.5f; dist > 0.12f; dist -= 0.01f)
        {
            Vector3 pos = aim - fwd * dist;
            for (int it = 0; it < 3; it++)
            {
                cam.transform.localPosition = pos;
                Rect e = Extent(pts);
                pos += rightDir * (e.center.x - 0.5f) * 2f * dist * tanY * cam.aspect + upDir * (e.center.y - (ViewTop + ViewBottom) * 0.5f) * 2f * dist * tanY;
            }
            cam.transform.localPosition = pos;
            Rect f = Extent(pts);
            if (f.xMin < 0.04f || f.xMax > 0.96f || f.yMin < ViewBottom || f.yMax > ViewTop || f.width < 0f) break;
            best = pos;
        }
        cam.transform.localPosition = best;
        var light = new GameObject("Board light").AddComponent<Light>();
        light.transform.SetParent(root, false);
        light.transform.localPosition = new Vector3(-0.1f, 0.6f, -0.25f);
        light.type = LightType.Point;
        light.range = 2f;
        light.intensity = 0.9f;
        light.shadows = LightShadows.None;

        UpdateGuide();
        BuildUI();
    }

    // The part of the screen the board view may use: below the title / counter / CLEAN popup, above the controls hint.
    const float ViewTop = 0.81f, ViewBottom = 0.09f;

    /// <summary>Where the points land on screen (viewport 0..1). Width &lt; 0 if any is behind the camera.</summary>
    Rect Extent(List<Vector3> pts)
    {
        float x0 = 9f, x1 = -9f, y0 = 9f, y1 = -9f;
        foreach (var p in pts)
        {
            var v = cam.WorldToViewportPoint(root.TransformPoint(p));
            if (v.z < cam.nearClipPlane * 2f) return new Rect(0f, 0f, -1f, -1f);
            x0 = Mathf.Min(x0, v.x); x1 = Mathf.Max(x1, v.x); y0 = Mathf.Min(y0, v.y); y1 = Mathf.Max(y1, v.y);
        }
        return Rect.MinMaxRect(x0, y0, x1, y1);
    }

    /// <summary>A thin disc across the cut, a little bigger than the ingredient, so only its rim shows - a line wrapped around it.</summary>
    Transform Ring(Transform parent, float r, float rim, Material m)
    {
        if (slab)
        {
            float d = radius * 1.4f;
            return Prim(PrimitiveType.Cube, "Ring", parent, new Vector3(0f, r * 0.5f, 0f), Vector3.zero, new Vector3(0.0018f, r + rim * 2f, d + rim * 2f), m);
        }
        float dia = (r + rim) * 2f;
        return Prim(PrimitiveType.Cylinder, "Ring", parent, new Vector3(0f, radius, 0f), new Vector3(0f, 0f, 90f), new Vector3(dia, 0.0009f, dia), m);
    }

    /// <summary>How thick the ingredient is at x (measured before any piece slid): its height for a slab, its radius otherwise.</summary>
    float RadiusAt(float x) => slab ? radius * 0.5f : profile(x);

    /// <summary>
    /// A slice of a round shape turned around the x axis, from x0 to x1, radius r(x): submesh 0 = the skin, submesh 1 = the flat cut faces.
    /// </summary>
    static Mesh Lathe(float x0, float x1, System.Func<float, float> r, int rings, int sides, System.Func<float, bool> second = null)
    {
        var v = new List<Vector3>();
        var skinTri = new List<int>();
        var capTri = new List<int>();
        var secondTri = new List<int>(); // skin of the second colour (a mushroom's stem)
        for (int j = 0; j <= rings; j++)
        {
            float x = Mathf.Lerp(x0, x1, j / (float)rings), rr = Mathf.Max(0.0004f, r(x));
            for (int k = 0; k < sides; k++)
            {
                float a = k * Mathf.PI * 2f / sides;
                v.Add(new Vector3(x, rr * Mathf.Cos(a), rr * Mathf.Sin(a)));
            }
        }
        for (int j = 0; j < rings; j++)
            for (int k = 0; k < sides; k++)
            {
                int a = j * sides + k, b = (j + 1) * sides + k, a2 = j * sides + (k + 1) % sides, b2 = (j + 1) * sides + (k + 1) % sides;
                bool alt = second != null && second(Mathf.Lerp(x0, x1, (j + 0.5f) / rings));
                (alt ? secondTri : skinTri).AddRange(new[] { a, b2, b, a, a2, b2 }); // clockwise seen from outside
            }
        foreach (bool far in new[] { false, true })
        {
            float x = far ? x1 : x0, rr = r(x);
            if (rr < 0.001f) continue; // the very ends of a round thing come to a point: nothing to cap
            int c = v.Count;
            v.Add(new Vector3(x, 0f, 0f));
            for (int k = 0; k < sides; k++)
            {
                float a = k * Mathf.PI * 2f / sides;
                v.Add(new Vector3(x, rr * Mathf.Cos(a), rr * Mathf.Sin(a)));
            }
            for (int k = 0; k < sides; k++)
            {
                int p0 = c + 1 + k, p1 = c + 1 + (k + 1) % sides;
                if (far) capTri.AddRange(new[] { c, p0, p1 }); // faces +x
                else capTri.AddRange(new[] { c, p1, p0 });     // faces -x
            }
        }
        var mesh = new Mesh { name = "Ingredient slice", subMeshCount = 3 };
        mesh.SetVertices(v);
        mesh.SetTriangles(skinTri, 0);
        mesh.SetTriangles(capTri, 1);
        mesh.SetTriangles(secondTri, 2);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    /// <summary>
    /// A chef's-knife blade, edge at y = 0 running from the heel (z = 0) to the tip (z = len): a straight edge that curves up for the
    /// last third, a straight spine that drops to the point, ground thin towards the edge (a bevel line you can see) and towards the tip.
    /// </summary>
    static Mesh BladeMesh(float len, float h, float thick)
    {
        const int N = 28;
        var v = new List<Vector3>();
        var tri = new List<int>();
        float Edge(float u) => u < 0.62f ? 0f : 0.3f * h * Mathf.Pow((u - 0.62f) / 0.38f, 2f);
        float Spine(float u) => u < 0.6f ? h : Mathf.Lerp(0.3f * h, h, 1f - Mathf.Pow((u - 0.6f) / 0.4f, 1.6f));
        Vector3 P(int i, int row, float side)
        {
            float u = i / (float)N;
            float e = Edge(u), s = Spine(u), t = thick * (1f - 0.65f * u) * 0.5f;
            if (row == 0) return new Vector3(0f, e, u * len);                                 // the edge
            if (row == 1) return new Vector3(side * t * 0.8f, e + (s - e) * 0.33f, u * len); // the top of the bevel
            return new Vector3(side * t, s, u * len);                                         // the spine
        }
        void Quad(Vector3 a0, Vector3 a1, Vector3 b0, Vector3 b1, bool flip)
        {
            // a = lower row, b = upper row, 0 -> 1 = towards the tip. Unity's front faces are clockwise.
            int n = v.Count;
            v.Add(a0); v.Add(a1); v.Add(b0); v.Add(b1);
            if (!flip) tri.AddRange(new[] { n, n + 2, n + 3, n, n + 3, n + 1 });
            else tri.AddRange(new[] { n, n + 3, n + 2, n, n + 1, n + 3 });
        }
        for (int i = 0; i < N; i++)
        {
            foreach (float side in new[] { 1f, -1f })
            {
                bool flip = side < 0f;
                Quad(P(i, 0, side), P(i + 1, 0, side), P(i, 1, side), P(i + 1, 1, side), flip); // the bevel
                Quad(P(i, 1, side), P(i + 1, 1, side), P(i, 2, side), P(i + 1, 2, side), flip); // the flat
            }
            // the spine on top (from the -x side to the +x side)
            Quad(P(i, 2, -1f), P(i + 1, 2, -1f), P(i, 2, 1f), P(i + 1, 2, 1f), true);
        }
        // the heel end, facing the handle
        int e0 = v.Count;
        v.Add(P(0, 0, 1f)); v.Add(P(0, 1, -1f)); v.Add(P(0, 2, -1f)); v.Add(P(0, 2, 1f)); v.Add(P(0, 1, 1f));
        tri.AddRange(new[] { e0, e0 + 1, e0 + 2, e0, e0 + 2, e0 + 3, e0, e0 + 3, e0 + 4 });
        var mesh = new Mesh { name = "Chef's knife blade" };
        mesh.SetVertices(v);
        mesh.SetTriangles(tri, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    void BuildUI()
    {
        var font = GameHUD.Instance ? GameHUD.Instance.font : null;
        ui = UIFactory.CreateCanvas("Cutting UI", 61);
        title = Label(font, 40, new Vector2(0.5f, 1f), new Vector2(0f, -70f), new Vector2(900, 56));
        title.text = $"<b>CHOP THE {ing.name.ToUpperInvariant()}</b>";
        counter = Label(font, 24, new Vector2(0.5f, 1f), new Vector2(0f, -118f), new Vector2(600, 34));
        judge = Label(font, 44, new Vector2(0.5f, 1f), new Vector2(0f, -170f), new Vector2(600, 60));
        judge.alpha = 0f;
        hint = Label(font, 20, new Vector2(0.5f, 0f), new Vector2(0f, 50f), new Vector2(1400, 30));
        hint.text = "Move the knife: mouse or A / D   -   Chop when the guide is green: click or Space   -   Esc to stop";
    }

    TextMeshProUGUI Label(TMP_FontAsset font, float size, Vector2 anchor, Vector2 at, Vector2 box)
    {
        var t = UIFactory.Text("Text", ui.transform, size, Color.white, TextAlignmentOptions.Center, font);
        t.enableWordWrapping = false;
        t.outlineWidth = 0.2f;
        t.outlineColor = new Color32(30, 25, 30, 255);
        UIFactory.Place(t.rectTransform, anchor, new Vector2(0.5f, 0.5f), at, box);
        return t;
    }

    // ------------------------------------------------------------------ play

    void Update()
    {
        if (!running || PauseMenu.IsPaused || !root) return;
        float dt = Time.unscaledDeltaTime;
        int left = 0;
        foreach (var c in cutDone) if (!c) left++;
        counter.text = $"{cutDone.Length - left} / {cutDone.Length} cuts" + (misses > 0 ? $"   <color=#FF8A7A>{misses} miss{(misses == 1 ? "" : "es")}</color>" : "");
        judgeTime -= dt;
        judge.alpha = Mathf.Clamp01(judgeTime * 2.5f);

        if (finishAt > 0f)
        {
            if (Time.unscaledTime >= finishAt) Finish();
            return;
        }

        float speed = length * 1.2f;
        float move = Input.GetAxisRaw("Mouse X") * length * 0.05f;
        if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) move -= speed * dt;
        if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) move += speed * dt;
        if (chopAnim < 0f) knifeX = Mathf.Clamp(knifeX + move, -length * 0.5f - segment, length * 0.5f + segment);

        if (chopAnim < 0f && (Input.GetMouseButtonDown(0) || Input.GetKeyDown(KeyCode.Space))) Chop();
        float lift = idleLift;
        if (chopAnim >= 0f)
        {
            // down fast to the board (through the ingredient, like a real cut), then back up
            chopAnim += dt / 0.24f;
            lift = chopAnim < 0.35f ? Mathf.Lerp(idleLift, 0.001f, chopAnim / 0.35f) : Mathf.Lerp(0.001f, idleLift, (chopAnim - 0.35f) / 0.65f);
            if (chopAnim >= 1f) chopAnim = -1f;
        }
        knife.localPosition = new Vector3(knifeX, lift, 0f);
        UpdateGuide();
    }

    /// <summary>The glowing line under the blade: across the board, and wrapped around the ingredient when the blade is over it.</summary>
    void UpdateGuide()
    {
        if (!guideBoard) return;
        guideBoard.localPosition = new Vector3(knifeX, 0.0009f, 0f);
        guideRing.localPosition = new Vector3(knifeX, 0f, 0f);
        float r = -1f;
        for (int i = 0; i < pieces.Count; i++)
        {
            float shift = pieces[i].localPosition.x - pieceX0[i];
            if (Mathf.Abs(pieces[i].localPosition.x - knifeX) <= segment * 0.5f) { r = RadiusAt(knifeX - shift); break; }
        }
        if (r < 0.002f) r = -1f; // past the rounded end: only the line on the board
        if (r > 0f && !slab) { float dia = (r + 0.004f) * 2f; guideShape.localScale = new Vector3(dia, 0.0009f, dia); }
        guideRing.gameObject.SetActive(r > 0f);

        bool on = false;
        for (int i = 0; i < lines.Length; i++) if (!cutDone[i] && Mathf.Abs(lines[i] - knifeX) <= segment * 0.3f) on = true;
        var c = on ? GuideOn : GuideOff;
        guideMat.color = c;
        guideMat.SetColor("_EmissionColor", c * 1.1f);
    }

    void Chop()
    {
        chopAnim = 0f;
        int best = -1;
        float bestD = float.MaxValue;
        for (int i = 0; i < lines.Length; i++)
        {
            if (cutDone[i]) continue;
            float d = Mathf.Abs(lines[i] - knifeX);
            if (d < bestD) { bestD = d; best = i; }
        }
        float tolerance = segment * 0.3f;
        Vector3 at = root.TransformPoint(new Vector3(knifeX, 0f, 0f));
        if (best >= 0 && bestD <= tolerance)
        {
            cutDone[best] = true;
            float score = 1f - 0.5f * bestD / tolerance;
            scores.Add(score);
            lineMarks[best].gameObject.SetActive(false);
            // Everything right of the cut slides a little apart.
            float gap = segment * 0.12f;
            for (int i = best + 1; i < pieces.Count; i++) pieces[i].localPosition += new Vector3(gap, 0f, 0f);
            for (int i = best + 1; i < lines.Length; i++) { lines[i] += gap; lineMarks[i].localPosition += new Vector3(gap, 0f, 0f); }
            Say(score > 0.85f ? "CLEAN!" : "CUT", score > 0.85f ? new Color(0.55f, 1f, 0.6f) : Color.white);
            Fx.Play(Fx.Thud, at, 0.35f, 1.4f);
            bool all = true;
            foreach (var c in cutDone) all &= c;
            if (all) { finishAt = Time.unscaledTime + 0.7f; Say("ALL CHOPPED!", new Color(1f, 0.85f, 0.4f)); }
        }
        else
        {
            misses++;
            Say("MISS", new Color(1f, 0.5f, 0.45f));
            Fx.Play(Fx.Click, at, 0.4f, 0.8f);
        }
    }

    void Say(string text, Color colour)
    {
        judge.text = text;
        judge.color = colour;
        judgeTime = 0.8f;
    }
}
