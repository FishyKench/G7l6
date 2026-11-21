using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Menu: G7l6 > Rebuild Stairs. Replaces ONLY the stairs in NewHouse (the two U-shaped flights, their half-landings
/// and the guard on the top floor) with properly built ones:
///   - every step is a wood tread with a rounded-off nosing over a white riser, sitting on one smooth sloped
///     underside (no saw-tooth boxes hanging under the flights),
///   - the last step and the landing no longer overlap (that was the flicker), and nothing shares a surface,
///   - steps run right up to the walls and the middle wall (no gaps),
///   - a runner carpet up the middle, a handrail on brackets along the middle wall, little step lights,
///   - landings with a wood top, a plant in the corner, a glass balustrade with a wood rail on the top floor,
///   - plaster casings over the wall ends at the stairwell opening.
/// Walking is unchanged: the same smooth hidden ramps carry you (and the cat) up, so there are no bumps.
/// Nothing else in the house is touched. Saves the scene (with your own changes).
/// </summary>
public static class StairsBuilder
{
    const float Storey = 3f;
    const int GroundLayer = 10, WallLayer = 9;
    const int Steps = 9;                       // risers per flight (the 9th is the landing / the floor above)
    const float Rise = 1.5f / Steps, Run = 3.5f / Steps;
    const float FlightStartZ = 4f;
    static readonly float LandingZ = FlightStartZ + (Steps - 1) * Run; // where the landing starts (7.11)
    const float WestX0 = 11.98f, WestX1 = 13.95f, EastX0 = 14.05f, EastX1 = 16.02f; // into the walls / divider

    static Material tread, riser, soffit, carpet, rail, bracket, glow, glass, pot, leaves;

    [MenuItem("G7l6/Rebuild Stairs")]
    public static void Rebuild()
    {
        if (EditorApplication.isPlaying) { Debug.LogWarning("[Stairs] Stop Play mode first."); return; }
        var scene = SceneManager.GetActiveScene();
        if (scene.path != HouseBuilder.TargetScene)
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            scene = EditorSceneManager.OpenScene(HouseBuilder.TargetScene, OpenSceneMode.Single);
        }
        var house = scene.GetRootGameObjects().FirstOrDefault(g => g.name == "NewHouse");
        var stairs = house ? house.transform.Find("Structure/Stairs") : null;
        if (!stairs) { Debug.LogWarning("[Stairs] Couldn't find NewHouse/Structure/Stairs."); return; }

        Materials();
        var root = house.transform;
        Vector3 houseScale = root.localScale;
        root.localScale = Vector3.one; // work in plan metres, like the house builder
        Physics.SyncTransforms();
        int removed = 0;
        string lining = "", floors = "";
        try
        {
            foreach (Transform child in stairs.Cast<Transform>().ToList())
            {
                bool old = child.name.StartsWith("Flight") || child.name.StartsWith("Stair guard") || child.name.StartsWith("Stairs -")
                           || child.name == "Stair balustrade" || child.name == "Stair casings" || child.name == "Stairwell lining"
                           || child.name == "Stairwell opening" || child.name.StartsWith("Stair divider");
                if (!old) continue; // keeps the middle walls and the wall under the stairs
                Undo.DestroyObjectImmediate(child.gameObject);
                removed++;
            }
            Physics.SyncTransforms();
            stairsRoot = stairs;
            lining = Lining(stairs, root);
            for (int f = 0; f < 2; f++) Flight(stairs, f);
            Balustrade(stairs);
            Casings(root, stairs);
            Opening(stairs);
            floors = StoneFloor(root);
        }
        finally
        {
            root.localScale = houseScale;
            Physics.SyncTransforms();
        }
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log($"[Stairs] Rebuilt both flights of stairs, the landings and the top-floor balustrade (removed {removed} old parts). {lining} {floors} Nothing else changed. The scene is saved.");
    }

    // ------------------------------------------------------------------ one storey: flight up, landing, flight up

    static void Flight(Transform stairs, int floor)
    {
        float b = floor * Storey;
        var g = new GameObject($"Flight {floor}").transform;
        g.SetParent(stairs, false);

        // West flight goes north from z=4 to the landing; east flight comes back south from the landing to z=4.
        for (int i = 0; i < Steps - 1; i++)
        {
            float top = b + (i + 1) * Rise;
            Step(g, WestX0, WestX1, FlightStartZ + i * Run, +1, top, i);
            float topEast = b + Steps * Rise + (i + 1) * Rise;
            Step(g, EastX0, EastX1, LandingZ - i * Run, -1, topEast, i);
        }
        Underside(g, WestX0, WestX1, b, +1);
        Underside(g, EastX0, EastX1, b + 1.5f, -1);
        Landing(g, b + 1.5f);

        // The same smooth hidden ramps as before (they're what you actually walk on).
        Ramp(g, new Vector3((WestX0 + WestX1) * 0.5f, b, FlightStartZ - Run), new Vector3((WestX0 + WestX1) * 0.5f, b + 1.5f, LandingZ), WestX1 - WestX0 - 0.04f);
        Ramp(g, new Vector3((EastX0 + EastX1) * 0.5f, b + 1.5f, LandingZ + Run), new Vector3((EastX0 + EastX1) * 0.5f, b + 3f, FlightStartZ), EastX1 - EastX0 - 0.04f);

        // Handrails along the middle wall.
        Handrail(g, 13.94f - 0.075f, 13.94f, new Vector3(0f, b + 0.9f, FlightStartZ - 0.15f), new Vector3(0f, b + 1.5f + 0.9f, LandingZ + 0.15f));
        Handrail(g, 14.06f + 0.075f, 14.06f, new Vector3(0f, b + 1.5f + 0.9f, LandingZ + 0.15f), new Vector3(0f, b + 3f + 0.9f, FlightStartZ - 0.15f));

        // The last step up onto the floor above: a nosing over the floor's edge and a riser under it (no gap).
        float cx = (EastX0 + EastX1) * 0.5f, w = EastX1 - EastX0;
        Box(g, "Top riser", new Vector3(cx, b + 3f - 0.035f - (Rise - 0.02f) * 0.5f, FlightStartZ - 0.05f), new Vector3(w - 0.012f, Rise - 0.02f, 0.1f), riser, false);
        Box(g, "Top nosing", new Vector3(cx, b + 3f - 0.0175f, FlightStartZ + 0.025f), new Vector3(w - 0.004f, 0.035f, 0.05f), tread, false);
    }

    /// <summary>
    /// One step: the riser block (white) and the tread on top (wood, sticking out a little at the front).
    /// 'dir' is the way you walk up (+1 north, -1 south); 'front' is the z of the riser.
    /// </summary>
    static void Step(Transform g, float x0, float x1, float front, int dir, float top, int index)
    {
        float width = x1 - x0, cx = (x0 + x1) * 0.5f;
        float back = front + dir * Run;
        float zMid = (front + back) * 0.5f;
        const float treadThick = 0.035f, nosing = 0.03f;

        // Riser block: from just inside the sloped underside up to the tread. (Its bottom sits inside the underside
        // slab all along the step, so no edge ever shows below.)
        float blockTop = top - treadThick, blockBottom = top - Rise - 0.015f;
        Box(g, "Riser", new Vector3(cx, (blockTop + blockBottom) * 0.5f, zMid), new Vector3(width - 0.012f, blockTop - blockBottom, Run), riser, false);
        // Tread with its nosing over the riser.
        var t = Box(g, "Tread", new Vector3(cx, top - treadThick * 0.5f, zMid - dir * nosing * 0.5f), new Vector3(width - 0.004f, treadThick, Run + nosing), tread, false);
        t.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        // Runner carpet up the middle (on top of the tread, wrapping its front edge).
        Box(g, "Runner", new Vector3(cx, top + 0.004f, zMid - dir * nosing * 0.5f), new Vector3(0.9f, 0.008f, Run + nosing), carpet, false);
        Box(g, "Runner edge", new Vector3(cx, top - treadThick * 0.5f, front - dir * (nosing + 0.004f)), new Vector3(0.9f, treadThick + 0.016f, 0.008f), carpet, false);

        // A small step light in the middle wall, every other step.
        if (index % 2 == 1)
        {
            float wallX = dir > 0 ? 13.94f - 0.005f : 14.06f + 0.005f;
            Box(g, "Step light", new Vector3(wallX, top + 0.14f, zMid), new Vector3(0.008f, 0.035f, 0.16f), glow, false)
                .GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
    }

    /// <summary>The smooth sloped underside of a flight (what you see from below), white plaster.</summary>
    static void Underside(Transform g, float x0, float x1, float b, int dir)
    {
        // The line through the bottom corners of the risers: from (start, b) rising Rise per Run.
        float startZ = dir > 0 ? FlightStartZ : LandingZ;
        float endZ = dir > 0 ? LandingZ : FlightStartZ + 0.02f;
        float lead = dir > 0 ? (b < 0.5f ? 0.25f : 0f) : 0.12f; // ground floor: starts inside the floor; upper: at the beam
        float rise = Rise * Mathf.Abs(endZ - startZ) / Run;
        Vector3 a = new Vector3((x0 + x1) * 0.5f, b, startZ - dir * lead);
        a.y -= Rise / Run * lead;
        Vector3 c = new Vector3(a.x, b + rise, endZ);
        Vector3 along = c - a;
        Vector3 forward = along.normalized;
        Vector3 up = Vector3.Cross(forward, Vector3.Cross(Vector3.up, forward)).normalized;
        if (up.y < 0f) up = -up;
        const float thick = 0.18f;
        var slab = Box(g, "Underside", (a + c) * 0.5f - up * thick * 0.5f, new Vector3(x1 - x0, thick, along.magnitude), soffit, false);
        slab.transform.rotation = Quaternion.LookRotation(forward, up);
    }

    static void Landing(Transform g, float top)
    {
        float depth = 10.02f - LandingZ, z = (LandingZ + 10.02f) * 0.5f;
        const float boards = 0.035f, nosing = 0.03f;
        var body = Box(g, "Landing", new Vector3(14f, top - boards - 0.13f, z), new Vector3(4.03f, 0.26f, depth), soffit, true, GroundLayer);
        body.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        Box(g, "Landing boards", new Vector3(14f, top - boards * 0.5f, z - nosing * 0.5f), new Vector3(4.04f, boards, depth + nosing), tread, false);
        Box(g, "Landing rug", new Vector3(14f, top + 0.004f, z + 0.1f), new Vector3(2.6f, 0.008f, 1.6f), carpet, false);

        // A plant in the far corner.
        var plant = new GameObject("Landing plant").transform;
        plant.SetParent(g, false);
        plant.position = new Vector3(12.45f, top, 9.55f);
        var potGo = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Object.DestroyImmediate(potGo.GetComponent<Collider>());
        potGo.name = "Pot";
        potGo.transform.SetParent(plant, false);
        potGo.transform.localPosition = new Vector3(0f, 0.22f, 0f);
        potGo.transform.localScale = new Vector3(0.38f, 0.22f, 0.38f);
        potGo.GetComponent<Renderer>().sharedMaterial = pot;
        for (int i = 0; i < 4; i++)
        {
            var leaf = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.DestroyImmediate(leaf.GetComponent<Collider>());
            leaf.name = "Leaves";
            leaf.transform.SetParent(plant, false);
            float a = i * 1.7f;
            leaf.transform.localPosition = new Vector3(Mathf.Cos(a) * 0.1f, 0.62f + i * 0.13f, Mathf.Sin(a) * 0.1f);
            leaf.transform.localScale = new Vector3(0.42f, 0.36f, 0.42f) * (1f - i * 0.15f);
            leaf.GetComponent<Renderer>().sharedMaterial = leaves;
        }
        var hit = new GameObject("Plant (solid)").AddComponent<BoxCollider>();
        hit.transform.SetParent(plant, false);
        hit.center = new Vector3(0f, 0.5f, 0f);
        hit.size = new Vector3(0.4f, 1f, 0.4f);
    }

    /// <summary>A wood handrail on metal brackets along the middle wall (whose face is at wallX).</summary>
    static void Handrail(Transform g, float x, float wallX, Vector3 from, Vector3 to)
    {
        from.x = to.x = x;
        Vector3 along = to - from;
        var r = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Object.DestroyImmediate(r.GetComponent<Collider>());
        r.name = "Handrail";
        r.transform.SetParent(g, true);
        r.transform.position = (from + to) * 0.5f;
        r.transform.rotation = Quaternion.FromToRotation(Vector3.up, along.normalized);
        r.transform.localScale = new Vector3(0.05f, along.magnitude * 0.5f, 0.05f);
        r.GetComponent<Renderer>().sharedMaterial = rail;
        for (int i = 0; i <= 3; i++)
        {
            Vector3 p = Vector3.Lerp(from, to, 0.1f + i * 0.8f / 3f);
            float mid = (x + wallX) * 0.5f, reach = Mathf.Abs(wallX - x);
            Box(g, "Rail bracket", new Vector3(mid, p.y - 0.035f, p.z), new Vector3(reach, 0.015f, 0.02f), bracket, false);
            Box(g, "Rail bracket plate", new Vector3(wallX + Mathf.Sign(x - wallX) * 0.005f, p.y - 0.045f, p.z), new Vector3(0.008f, 0.06f, 0.05f), bracket, false);
        }
    }

    /// <summary>Top floor: a glass balustrade with a wood rail over the drop (replaces the old wooden board).</summary>
    static void Balustrade(Transform stairs)
    {
        var g = new GameObject("Stair balustrade").transform;
        g.SetParent(stairs, false);
        float b = 2 * Storey, z = 4.05f;
        Box(g, "Base rail", new Vector3(12.97f, b, z), new Vector3(1.94f, 0.06f, 0.08f), rail, false); // sits on the header beam
        Box(g, "Hand rail", new Vector3(12.97f, b + 1.0f, z), new Vector3(1.98f, 0.06f, 0.1f), rail, false);
        foreach (float x in new[] { 12.04f, 12.97f, 13.9f })
            Box(g, "Post", new Vector3(x, b + 0.5f, z), new Vector3(0.06f, 0.94f, 0.06f), rail, false);
        Box(g, "Glass", new Vector3(12.505f, b + 0.5f, z), new Vector3(0.87f, 0.86f, 0.012f), glass, false);
        Box(g, "Glass", new Vector3(13.435f, b + 0.5f, z), new Vector3(0.87f, 0.86f, 0.012f), glass, false);
        var guard = new GameObject("Guard (solid)").AddComponent<BoxCollider>();
        guard.transform.SetParent(g, false);
        guard.transform.position = new Vector3(12.97f, b + 0.55f, z);
        guard.size = new Vector3(1.98f, 1.1f, 0.1f);
        guard.gameObject.layer = WallLayer;
    }

    /// <summary>
    /// One smooth plaster lining over each stairwell wall (west, north, east), ground floor to the top: the wall
    /// pieces of the different floors meet halfway up the stairs with a step and a seam, and the corner columns
    /// stick out - the lining goes just in front of whatever sticks out most, so the walls read as one surface.
    /// </summary>
    static string Lining(Transform stairs, Transform house)
    {
        float top = 3 * Storey - 0.02f, bottom = 0.005f;
        float west = WallFace(house, 0), east = WallFace(house, 1), north = WallFace(house, 2);
        const float thick = 0.02f, gap = 0.004f;

        var g = new GameObject("Stairwell lining").transform;
        g.SetParent(stairs, false);
        float h = top - bottom, y = (top + bottom) * 0.5f;
        float wx = west + gap, ex = east - gap, nz = north - gap;
        Box(g, "Lining west", new Vector3(wx + thick * 0.5f, y, (FlightStartZ + nz) * 0.5f), new Vector3(thick, h, nz - FlightStartZ), soffit, false, WallLayer);
        Box(g, "Lining east", new Vector3(ex - thick * 0.5f, y, (FlightStartZ + nz) * 0.5f), new Vector3(thick, h, nz - FlightStartZ), soffit, false, WallLayer);
        Box(g, "Lining north", new Vector3((wx + ex) * 0.5f, y, nz - thick * 0.5f), new Vector3(ex - wx, h, thick), soffit, false, WallLayer);

        // One middle wall between the flights, ground to top (it was three pieces with gaps between the floors).
        Box(g, "Stair divider", new Vector3(14f, (bottom + top) * 0.5f, (FlightStartZ + 7.5f) * 0.5f), new Vector3(0.12f, h, 7.5f - FlightStartZ), soffit, true, WallLayer);
        return $"Stairwell walls lined (west {west:0.000}, east {east:0.000}, north {north:0.000}).";
    }

    /// <summary>
    /// Where a stairwell wall's inner surface is, from the wall pieces and columns themselves (their outlines):
    /// whatever sticks furthest into the stairwell. side 0 = west (x=12), 1 = east (x=16), 2 = north (z=10).
    /// </summary>
    static float WallFace(Transform house, int side)
    {
        float best = side == 0 ? 12f - 0.05f : side == 1 ? 16f + 0.05f : 10f + 0.05f;
        foreach (var r in house.GetComponentsInChildren<MeshRenderer>(true))
        {
            if (!r.enabled || r.transform.IsChildOf(stairsRoot)) continue;
            var b = r.bounds;
            if (b.max.y < 0.2f || b.min.y > 8.8f) continue;
            if (side < 2)
            {
                float line = side == 0 ? 12f : 16f;
                if (Mathf.Abs(b.center.x - line) > 0.5f || b.size.x > 0.8f) continue;
                if (b.max.z < 4.1f || b.min.z > 9.9f) continue;               // along this wall
                if (side == 0 && b.max.x < 12.6f) best = Mathf.Max(best, b.max.x);
                if (side == 1 && b.min.x > 15.4f) best = Mathf.Min(best, b.min.x);
            }
            else
            {
                if (Mathf.Abs(b.center.z - 10f) > 0.5f || b.size.z > 0.8f) continue;
                if (b.max.x < 12.1f || b.min.x > 15.9f) continue;
                if (b.min.z > 9.4f) best = Mathf.Min(best, b.min.z);
            }
        }
        return best;
    }

    static Transform stairsRoot;

    /// <summary>
    /// The ground-floor opening from the entrance: the space under the right-hand (east) flight is closed off with
    /// one flush wall from the floor to a clean header beam across the whole opening (it used to stop short, so you
    /// saw the slope of the stairs above it as a bend), and the old under-stair wall goes.
    /// </summary>
    static void Opening(Transform stairs)
    {
        var old = stairs.Find("Under-stair wall");
        if (old) Undo.DestroyObjectImmediate(old.gameObject);
        var g = new GameObject("Stairwell opening").transform;
        g.SetParent(stairs, false);
        const float beamBottom = 2.72f, front = 3.86f;
        for (int f = 0; f < 2; f++)
        {
            float b = f * Storey;
            Box(g, $"Header beam {f}", new Vector3(14f, b + (beamBottom + 2.97f) * 0.5f, (front - 0.01f + 4.04f) * 0.5f),
                new Vector3(4.02f, 2.97f - beamBottom, 4.04f - front + 0.01f), soffit, true, WallLayer);
        }
        Box(g, "Under-stair wall", new Vector3((13.925f + 16.01f) * 0.5f, beamBottom * 0.5f + 0.003f, (front + 4.05f) * 0.5f), new Vector3(16.01f - 13.925f, beamBottom - 0.002f, 4.05f - front), soffit, true, WallLayer);
    }

    /// <summary>The stairwell's ground floor gets the entrance's stone, so the entrance and the stairs are one space.</summary>
    static string StoneFloor(Transform house)
    {
        var stone = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/House/Floor - Entry Stone.mat");
        if (!stone) return "(Run 'Entrance Floor and Remote' first for the stone floor.)";
        int n = 0;
        foreach (var r in house.GetComponentsInChildren<Renderer>(true))
        {
            var c = r.bounds.center;
            if (c.x < 12f || c.x > 16f || c.z < 4f || c.z > 10f || c.y > 0.4f || c.y < -0.6f) continue;
            if (r.transform.IsChildOf(stairsRoot)) continue;
            var mats = r.sharedMaterials;
            bool floor = false;
            for (int i = 0; i < mats.Length; i++)
                if (mats[i] && mats[i].name.StartsWith("Floor - ")) { mats[i] = stone; floor = true; }
            if (!floor) continue;
            Undo.RecordObject(r, "Stairwell floor");
            r.sharedMaterials = mats;
            n++;
        }
        return $"Stone floor in the stairwell ({n} tiles).";
    }

    /// <summary>Plaster casings over the wall ends either side of the stairwell opening, on every floor.</summary>
    static void Casings(Transform house, Transform stairs)
    {
        var g = new GameObject("Stair casings").transform;
        g.SetParent(stairs, false);
        var columns = house.GetComponentsInChildren<Renderer>(true).Where(r => !r.transform.IsChildOf(stairs)
            && (r.name.Contains("Column") || r.transform.parent && r.transform.parent.name.Contains("Column"))).ToList();
        foreach (float x in new[] { 12f, 16f })
        {
            // As big as the wall columns already at that corner (plus a little), so it covers the wall ends.
            var b = new Bounds(new Vector3(x, 4.5f, 4f), new Vector3(0.26f, 1f, 0.26f));
            foreach (var c in columns)
            {
                var cb = c.bounds;
                if (Mathf.Abs(cb.center.x - x) < 0.35f && Mathf.Abs(cb.center.z - 4f) < 0.35f)
                    b.Encapsulate(new Bounds(new Vector3(cb.center.x, 4.5f, cb.center.z), new Vector3(cb.size.x, 1f, cb.size.z)));
            }
            float top = 3 * Storey - 0.02f, bottom = 0.005f;
            Box(g, "Casing", new Vector3(b.center.x, (top + bottom) * 0.5f, b.center.z), new Vector3(b.size.x + 0.14f, top - bottom, b.size.z + 0.14f), soffit, false)
                .layer = WallLayer;
        }
    }

    // ------------------------------------------------------------------ helpers

    static GameObject Box(Transform parent, string name, Vector3 center, Vector3 size, Material mat, bool collider, int layer = 0)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        if (!collider) Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(parent, true);
        go.transform.position = center;
        go.transform.localScale = size;
        go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        go.layer = layer;
        return go;
    }

    static void Ramp(Transform parent, Vector3 from, Vector3 to, float width)
    {
        var go = new GameObject("Ramp (walkable)");
        go.transform.SetParent(parent, true);
        Vector3 along = to - from;
        Vector3 forward = along.normalized;
        Vector3 normal = Vector3.Cross(forward, Vector3.Cross(Vector3.up, forward)).normalized;
        if (normal.y < 0f) normal = -normal;
        go.transform.SetPositionAndRotation((from + to) * 0.5f - normal * 0.15f, Quaternion.LookRotation(forward, normal));
        var box = go.AddComponent<BoxCollider>();
        box.size = new Vector3(width, 0.3f, along.magnitude + 0.05f);
        go.layer = GroundLayer;
    }

    static void Materials()
    {
        tread = Mat("Stair Tread Oak", new Color(0.58f, 0.4f, 0.25f), 0.35f);
        var planks = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Materials/House/Wood Planks.png");
        if (planks) { tread.mainTexture = planks; tread.mainTextureScale = new Vector2(0.5f, 1f); tread.color = new Color(0.9f, 0.72f, 0.55f); }
        riser = Mat("Stair Riser White", new Color(0.95f, 0.94f, 0.91f), 0.2f);
        soffit = Mat("Wall Plaster", new Color(0.9f, 0.88f, 0.84f), 0.1f);
        carpet = Mat("Stair Runner", new Color(0.16f, 0.22f, 0.36f), 0.05f);
        rail = Mat("Handrail Walnut", new Color(0.3f, 0.19f, 0.12f), 0.55f);
        bracket = Mat("Dark Metal", new Color(0.25f, 0.26f, 0.28f), 0.5f);
        pot = Mat("Plant Pot", new Color(0.82f, 0.8f, 0.76f), 0.3f);
        leaves = Mat("Plant Leaves", new Color(0.24f, 0.48f, 0.28f), 0.2f);
        glow = Mat("Step Light", new Color(1f, 0.85f, 0.6f), 0.3f);
        glow.EnableKeyword("_EMISSION");
        glow.SetColor("_EmissionColor", new Color(1f, 0.8f, 0.5f) * 1.6f);
        glow.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        glass = Mat("Balustrade Glass", new Color(0.75f, 0.88f, 0.92f, 0.25f), 0.95f);
        // See-through (Standard shader, Fade mode).
        glass.SetFloat("_Mode", 2f);
        glass.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        glass.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        glass.SetInt("_ZWrite", 0);
        glass.DisableKeyword("_ALPHATEST_ON");
        glass.EnableKeyword("_ALPHABLEND_ON");
        glass.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        glass.renderQueue = 3000;
        EditorUtility.SetDirty(glow);
        EditorUtility.SetDirty(glass);
        EditorUtility.SetDirty(tread);
    }

    static Material Mat(string name, Color color, float gloss)
    {
        string path = $"Assets/Materials/House/{name}.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!m) { m = new Material(Shader.Find("Standard")); AssetDatabase.CreateAsset(m, path); }
        m.color = color;
        m.SetFloat("_Glossiness", gloss);
        EditorUtility.SetDirty(m);
        return m;
    }
}
