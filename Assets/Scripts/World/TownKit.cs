using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// The neighbourhood made of simple shapes: houses with lit windows, picket fences, roads and pavements, trees, a park full of
/// kids, a fair with a Ferris wheel, the rest of town on the horizon, clouds and a plane, people, a dog and cars - plus the
/// MenuWorld that sets it all moving. The main menu's view out of the window is built from it (MainMenuBuilder, saved into the
/// scene) and so is the world round the house in the game (AroundHouse, made when the day loads).
/// </summary>
public static class TownKit
{
    /// <summary>Makes (or finds) a material. The menu builder swaps in one that saves them as assets.</summary>
    public static System.Func<string, Color, float, Color?, Material> MaterialMaker;

    static readonly Dictionary<string, Material> runtimeMats = new Dictionary<string, Material>();

    public static Material Mat(string name, Color c, float gloss = 0.1f, Color? glow = null)
    {
        if (MaterialMaker != null && !Application.isPlaying) return MaterialMaker(name, c, gloss, glow);
        if (runtimeMats.TryGetValue(name, out var m) && m) return m;
        m = new Material(Shader.Find("Standard")) { name = name, color = c };
        m.SetFloat("_Glossiness", gloss);
        if (glow.HasValue) { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", glow.Value); m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None; }
        runtimeMats[name] = m;
        return m;
    }



    public static GameObject Prim(PrimitiveType type, Transform parent, Vector3 pos, Vector3 size, Material m, Quaternion? rot = null)
    {
        var g = GameObject.CreatePrimitive(type);
        Object.DestroyImmediate(g.GetComponent<Collider>());
        g.transform.SetParent(parent, false);
        g.transform.position = pos;
        g.transform.rotation = rot ?? Quaternion.identity;
        g.transform.localScale = size;
        g.GetComponent<Renderer>().sharedMaterial = m;
        return g;
    }

    /// <summary>
    /// What you see out of the window: a lawn and a garden below, a white picket fence, a pavement and a road, trees, and the
    /// houses across the street with their lights coming on. Built from simple shapes, once for each direction a window looks.
    /// </summary>
    public static void Neighbourhood(Vector3 centre, float groundY, float s, List<Renderer> windows, Vector3 camPos, Vector3 camForward)
    {
        var root = new GameObject("Outside (the neighbourhood)").transform;
        var world = root.gameObject.AddComponent<MenuWorld>();
        // which window the camera sees best: the park goes across the street from it, the fair on the hill through the other one
        var byView = windows.OrderBy(w => Vector3.Angle(camForward, w.bounds.center - camPos)).ToList();
        Vector3 parkDir = byView.Count > 0 ? Vector3.ProjectOnPlane(byView[0].bounds.center - camPos, Vector3.up).normalized : Vector3.forward;
        Vector3 fairDir = byView.Count > 1 ? Vector3.ProjectOnPlane(byView[1].bounds.center - camPos, Vector3.up).normalized : Quaternion.Euler(0f, 60f, 0f) * parkDir;
        // what the camera can see through a window: between these two slopes (height per metre out)
        (float low, float high) Band(Renderer w)
        {
            var b = w.bounds;
            Vector3 flat = b.center - camPos; flat.y = 0f;
            float d = Mathf.Max(1f, flat.magnitude - b.extents.magnitude * 0.25f);
            float inset = b.size.y * 0.18f; // (the frame)
            return ((b.min.y + inset - camPos.y) / d, (b.max.y - inset - camPos.y) / d);
        }
        var parkBand = byView.Count > 0 ? Band(byView[0]) : (low: -0.1f, high: 0.1f);
        float parkDist = 40f;
        Vector3 parkCentre = new Vector3(camPos.x, groundY, camPos.z) + parkDir * parkDist;
        // up on a grassy bank, so the kids (not just the treetops) show over the sill
        float parkY = Mathf.Max(groundY, camPos.y + parkBand.low * (parkDist - 3f) + 0.15f);
        parkY = Mathf.Min(parkY, camPos.y + parkBand.high * parkDist - 6f);
        parkY = Mathf.Max(groundY, parkY);
        parkCentre.y = parkY;
        var fairBand = byView.Count > 1 ? Band(byView[1]) : parkBand;
        // the Ferris wheel (about 37 m to the top) far enough off that all of it fits in the window
        float fairDist = fairBand.high > 0.02f ? Mathf.Clamp((groundY + 3f + 37f + 4f - camPos.y) / fairBand.high, 130f, 420f) : 420f;
        Debug.Log($"[Main menu] view bands: park {parkBand.low:F3}..{parkBand.high:F3} -> park {parkDist} m out, raised {parkY - groundY:F1} m; fair {fairBand.low:F3}..{fairBand.high:F3} -> {fairDist:F0} m out");
        bool InView(Vector3 p, Vector3 dir, float degrees) { Vector3 f = p - camPos; f.y = 0f; return Vector3.Angle(f, dir) < degrees; }
        var grass = Mat("Grass", new Color(0.33f, 0.47f, 0.22f));
        var road = Mat("Road", new Color(0.2f, 0.2f, 0.22f), 0.3f);
        var pavement = Mat("Pavement", new Color(0.62f, 0.6f, 0.57f));
        var fence = Mat("Fence", new Color(0.93f, 0.92f, 0.88f), 0.25f);
        var trunk = Mat("Trunk", new Color(0.35f, 0.24f, 0.16f));
        var leaves = new[] { Mat("Leaves A", new Color(0.24f, 0.42f, 0.18f)), Mat("Leaves B", new Color(0.3f, 0.48f, 0.2f)), Mat("Leaves C", new Color(0.42f, 0.5f, 0.2f)) };
        var walls = new[] { Mat("House cream", new Color(0.92f, 0.86f, 0.72f)), Mat("House blue", new Color(0.62f, 0.72f, 0.82f)), Mat("House brick", new Color(0.66f, 0.34f, 0.28f)), Mat("House sage", new Color(0.66f, 0.74f, 0.6f)) };
        var roofs = new[] { Mat("Roof dark", new Color(0.26f, 0.22f, 0.24f)), Mat("Roof red", new Color(0.55f, 0.24f, 0.2f)), Mat("Roof slate", new Color(0.33f, 0.37f, 0.42f)) };
        var lit = Mat("Window lit", new Color(1f, 0.82f, 0.5f), 0.6f, new Color(1f, 0.7f, 0.35f) * 1.6f);
        var dark = Mat("Window dark", new Color(0.18f, 0.22f, 0.3f), 0.8f);
        var flowers = new[] { Mat("Flowers pink", new Color(0.92f, 0.45f, 0.6f)), Mat("Flowers yellow", new Color(0.98f, 0.82f, 0.3f)), Mat("Flowers white", new Color(0.95f, 0.95f, 0.92f)) };
        var rnd = new System.Random(11);
        float R(float a, float b) => a + (float)rnd.NextDouble() * (b - a);

        // the ground all round
        Prim(PrimitiveType.Cube, root, new Vector3(centre.x, groundY - 0.05f, centre.z), new Vector3(1000f, 0.1f, 1000f), grass);
        if (parkY - groundY > 0.3f)
        {
            // the bank the park sits on: a flat top, soft slopes
            float rise = parkY - groundY;
            Prim(PrimitiveType.Sphere, root, new Vector3(parkCentre.x, groundY, parkCentre.z), new Vector3(52f, rise * 2f, 44f), grass, Quaternion.LookRotation(parkDir));
            Prim(PrimitiveType.Cylinder, root, new Vector3(parkCentre.x, parkY - rise * 0.5f + 0.01f, parkCentre.z), new Vector3(34f, rise * 0.5f, 30f), grass, Quaternion.LookRotation(parkDir));
        }

        var dirs = new List<Vector3>();
        foreach (var w in windows)
        {
            Vector3 d = Vector3.ProjectOnPlane(w.bounds.center - centre, Vector3.up);
            d = Mathf.Abs(d.x) > Mathf.Abs(d.z) ? new Vector3(Mathf.Sign(d.x), 0f, 0f) : new Vector3(0f, 0f, Mathf.Sign(d.z));
            if (!dirs.Any(x => Vector3.Dot(x, d) > 0.9f)) dirs.Add(d);
        }
        foreach (var dir in dirs)
        {
            Vector3 side = Vector3.Cross(Vector3.up, dir);
            Quaternion face = Quaternion.LookRotation(-dir);
            // our garden: lawn edge, flower beds, a picket fence, the pavement, the road
            float fenceAt = 9f * s;
            for (float t = -24f; t <= 24f; t += 0.5f)
            {
                Vector3 p = centre + dir * fenceAt + side * t;
                Prim(PrimitiveType.Cube, root, new Vector3(p.x, groundY + 0.5f, p.z), new Vector3(0.08f, 1f, 0.08f), fence, face);
            }
            for (int rail = 0; rail < 2; rail++)
            {
                Vector3 p = centre + dir * fenceAt;
                Prim(PrimitiveType.Cube, root, new Vector3(p.x, groundY + 0.35f + rail * 0.45f, p.z), new Vector3(48f, 0.07f, 0.05f), fence, face);
            }
            for (int f = 0; f < 18; f++)
            {
                Vector3 p = centre + dir * R(fenceAt - 2.2f, fenceAt - 0.6f) + side * R(-14f, 14f);
                Prim(PrimitiveType.Sphere, root, new Vector3(p.x, groundY + 0.18f, p.z), new Vector3(R(0.5f, 0.9f), R(0.3f, 0.5f), R(0.5f, 0.9f)), leaves[f % leaves.Length]);
                Prim(PrimitiveType.Sphere, root, new Vector3(p.x, groundY + 0.38f, p.z), Vector3.one * R(0.18f, 0.3f), flowers[f % flowers.Length]);
            }
            Vector3 pv = centre + dir * (fenceAt + 2f);
            Prim(PrimitiveType.Cube, root, new Vector3(pv.x, groundY + 0.02f, pv.z), new Vector3(80f, 0.1f, 2.4f), pavement, face);
            Vector3 rd = centre + dir * (fenceAt + 6.5f);
            Prim(PrimitiveType.Cube, root, new Vector3(rd.x, groundY, rd.z), new Vector3(80f, 0.1f, 7f), road, face);
            // trees along the pavement and in the gardens
            // (street trees past the pavement, spaced out: the houses show between them)
            for (int t = 0; t < 6; t++)
            {
                Vector3 p = centre + dir * (fenceAt + 3.4f + R(-0.3f, 0.3f)) + side * (-17.5f + t * 7f + R(-0.8f, 0.8f));
                float h = R(2.6f, 3.8f);
                Prim(PrimitiveType.Cylinder, root, new Vector3(p.x, groundY + h * 0.5f, p.z), new Vector3(0.22f, h * 0.5f, 0.22f), trunk);
                for (int k = 0; k < 3; k++)
                    Prim(PrimitiveType.Sphere, root, new Vector3(p.x + R(-0.6f, 0.6f), groundY + h + R(-0.3f, 0.6f), p.z + R(-0.6f, 0.6f)), Vector3.one * R(1.6f, 2.4f), leaves[(t + k) % leaves.Length]);
            }
            // the houses across the street
            for (int hIdx = 0; hIdx < 5; hIdx++)
            {
                Vector3 p = centre + dir * (fenceAt + 17f + R(0f, 2f)) + side * (-20f + hIdx * 10f + R(-1f, 1f));
                if (InView(p, parkDir, 26f) || InView(p, fairDir, 9f)) continue; // (keep the view of the park and the wheel open)
                float w = R(7f, 9f), d = R(6.5f, 8f), h = R(5f, 6.8f);
                var house = new GameObject("Neighbour's house").transform;
                house.SetParent(root, false);
                house.position = new Vector3(p.x, groundY, p.z);
                house.rotation = face;
                var wall = walls[(hIdx + 1) % walls.Length];
                Prim(PrimitiveType.Cube, house, house.TransformPoint(new Vector3(0f, h * 0.5f, 0f)), new Vector3(w, h, d), wall, face);
                // the roof: two tilted slabs meeting in a ridge
                var roofM = roofs[hIdx % roofs.Length];
                float pitch = 32f, half = d * 0.5f / Mathf.Cos(pitch * Mathf.Deg2Rad);
                for (int sideIdx = -1; sideIdx <= 1; sideIdx += 2)
                {
                    var slabRot = face * Quaternion.Euler(sideIdx * pitch, 0f, 0f);
                    Vector3 local = new Vector3(0f, h + Mathf.Tan(pitch * Mathf.Deg2Rad) * d * 0.25f, sideIdx * d * 0.25f);
                    Prim(PrimitiveType.Cube, house, house.TransformPoint(local), new Vector3(w + 0.6f, 0.18f, half + 0.4f), roofM, slabRot);
                }
                // windows (some lit: evening) and a door, on the side facing us
                for (int row = 0; row < 2; row++)
                    for (int col = 0; col < 3; col++)
                    {
                        if (row == 0 && col == 1) continue;
                        Vector3 wl = new Vector3(-w * 0.32f + col * w * 0.32f, h * (row == 0 ? 0.3f : 0.68f), -d * 0.5f - 0.02f);
                        Prim(PrimitiveType.Cube, house, house.TransformPoint(wl), new Vector3(1.1f, 1.1f, 0.05f), rnd.NextDouble() < 0.55 ? lit : dark, face);
                    }
                Prim(PrimitiveType.Cube, house, house.TransformPoint(new Vector3(0f, 1.05f, -d * 0.5f - 0.02f)), new Vector3(1f, 2.1f, 0.05f), roofs[(hIdx + 1) % roofs.Length], face);
            }
        }

        Park(root, world, parkCentre, parkDir, parkY, R);
        // traffic and a dog walk across the open field in the other window (in front of the fair): far enough out to show over the sill
        {
            Vector3 flatCam = new Vector3(camPos.x, groundY, camPos.z);
            float sillReach = fairBand.low < -0.01f ? (camPos.y - groundY) / -fairBand.low : 60f; // (nearer than this, the sill hides the ground)
            float roadDist = Mathf.Clamp(sillReach + 12f, 40f, 85f);
            Vector3 parkDir0 = parkDir; parkDir = fairDir; // (the road's direction out from the house)
            Vector3 across = Vector3.Cross(Vector3.up, parkDir);
            Quaternion along = Quaternion.LookRotation(across);
            Vector3 rc = flatCam + parkDir * roadDist;
            Prim(PrimitiveType.Cube, root, rc + Vector3.up * 0.03f, new Vector3(7f, 0.1f, 260f), road, along);
            Prim(PrimitiveType.Cube, root, rc - parkDir * 4.6f + Vector3.up * 0.06f, new Vector3(2.2f, 0.12f, 260f), pavement, along);
            Prim(PrimitiveType.Cube, root, rc + parkDir * 4.6f + Vector3.up * 0.06f, new Vector3(2.2f, 0.12f, 260f), pavement, along);
            var lines = Mat("Road lines", new Color(0.95f, 0.92f, 0.8f));
            for (float t = -120f; t <= 120f; t += 6f)
                Prim(PrimitiveType.Cube, root, rc + across * t + Vector3.up * 0.09f, new Vector3(0.18f, 0.02f, 2.6f), lines, along);
            // a row of street lamps, lit for the evening
            var lampPost = Mat("Lamp post", new Color(0.2f, 0.22f, 0.25f));
            var lampGlow = Mat("Lamp glow", new Color(1f, 0.85f, 0.55f), 0.5f, new Color(1f, 0.75f, 0.4f) * 2.5f);
            for (float t = -60f; t <= 60f; t += 15f)
            {
                Vector3 lp = rc - parkDir * 5.4f + across * t;
                Prim(PrimitiveType.Cylinder, root, lp + Vector3.up * 2.2f, new Vector3(0.14f, 2.2f, 0.14f), lampPost);
                Prim(PrimitiveType.Sphere, root, lp + Vector3.up * 4.5f, Vector3.one * 0.45f, lampGlow);
            }
            Vector3 road0 = rc + Vector3.up * 0.08f;
            world.roadFrom = road0 - across * 110f; world.roadTo = road0 + across * 110f; world.laneOffset = parkDir * 1.7f;
            Vector3 walk0 = rc - parkDir * 4.6f + Vector3.up * 0.12f;
            world.walkFrom = walk0 - across * 10f; world.walkTo = walk0 + across * 10f; // (about as wide as the window shows out there)
            parkDir = parkDir0;
            Debug.Log($"[Main menu] traffic road {roadDist:F0} m out in the fair window (sill hides the ground nearer than {sillReach:F0} m)");
        }
        Fair(root, world, new Vector3(camPos.x, groundY, camPos.z) + fairDir * fairDist, fairDir, groundY);
        Sky(root, world, centre, parkDir, groundY, R);
        Skyline(root, new Vector3(camPos.x, groundY, camPos.z), groundY, fairDir, walls, roofs, leaves, trunk, R);
        world.walker = Person(root, world.walkFrom, 1.75f, Mat("Shirt teal", new Color(0.2f, 0.55f, 0.6f)), false).transform;
        world.dog = Dog(root, world.walkFrom).transform;
        var cars = new List<GameObject>();
        foreach (var c in new[] { new Color(0.8f, 0.15f, 0.12f), new Color(0.15f, 0.35f, 0.75f), new Color(0.92f, 0.92f, 0.9f), new Color(0.95f, 0.75f, 0.15f) })
            cars.Add(Car(root, c));
        world.carTemplates = cars.ToArray();
    }

    public static readonly Color Skin = new Color(0.94f, 0.76f, 0.62f), SkinDark = new Color(0.55f, 0.38f, 0.28f);

    /// <summary>A simple person: legs, a body, arms, a head (kids are just shorter).</summary>
    public static GameObject Person(Transform parent, Vector3 feet, float height, Material shirt, bool kid)
    {
        var go = new GameObject(kid ? "Kid" : "Person");
        go.transform.SetParent(parent, false);
        go.transform.position = feet;
        float k = height / 1.75f;
        var legs = Mat("Trousers", new Color(0.22f, 0.24f, 0.32f));
        var skin = Mat(Random.value < 0.5f ? "Skin" : "Skin dark", Random.value < 0.5f ? Skin : SkinDark);
        var hair = Mat("Hair", new Color(0.2f, 0.14f, 0.1f));
        foreach (float x in new[] { -0.1f, 0.1f })
            Prim(PrimitiveType.Cylinder, go.transform, feet + new Vector3(x, 0.42f, 0f) * k, new Vector3(0.12f, 0.42f, 0.12f) * k, legs);
        Prim(PrimitiveType.Capsule, go.transform, feet + Vector3.up * 1.13f * k, new Vector3(0.42f, 0.36f, 0.26f) * k, shirt);
        foreach (float x in new[] { -0.27f, 0.27f })
            Prim(PrimitiveType.Capsule, go.transform, feet + new Vector3(x, 1.1f, 0f) * k, new Vector3(0.1f, 0.3f, 0.1f) * k, shirt);
        Prim(PrimitiveType.Sphere, go.transform, feet + Vector3.up * 1.58f * k, Vector3.one * 0.28f * k, skin);
        Prim(PrimitiveType.Sphere, go.transform, feet + new Vector3(0f, 1.64f, -0.03f) * k, new Vector3(0.3f, 0.2f, 0.3f) * k, hair);
        return go;
    }

    public static GameObject Dog(Transform parent, Vector3 feet)
    {
        var go = new GameObject("Dog");
        go.transform.SetParent(parent, false);
        go.transform.position = feet;
        var fur = Mat("Dog fur", new Color(0.78f, 0.55f, 0.3f));
        Prim(PrimitiveType.Capsule, go.transform, feet + Vector3.up * 0.4f, new Vector3(0.28f, 0.32f, 0.28f), fur, Quaternion.Euler(90f, 0f, 0f));
        Prim(PrimitiveType.Sphere, go.transform, feet + new Vector3(0f, 0.55f, 0.38f), Vector3.one * 0.24f, fur);
        foreach (var p in new[] { new Vector3(-0.1f, 0.15f, 0.2f), new Vector3(0.1f, 0.15f, 0.2f), new Vector3(-0.1f, 0.15f, -0.2f), new Vector3(0.1f, 0.15f, -0.2f) })
            Prim(PrimitiveType.Cylinder, go.transform, feet + p, new Vector3(0.07f, 0.15f, 0.07f), fur);
        return go;
    }

    public static GameObject Car(Transform parent, Color colour)
    {
        var go = new GameObject("Car");
        go.transform.SetParent(parent, false);
        var body = Mat("Car " + ColorUtility.ToHtmlStringRGB(colour), colour, 0.6f);
        var glass = Mat("Car glass", new Color(0.25f, 0.32f, 0.4f), 0.9f);
        var tyre = Mat("Tyre", new Color(0.08f, 0.08f, 0.09f));
        Prim(PrimitiveType.Cube, go.transform, new Vector3(0f, 0.55f, 0f), new Vector3(1.8f, 0.6f, 4.1f), body);
        Prim(PrimitiveType.Cube, go.transform, new Vector3(0f, 1.05f, -0.2f), new Vector3(1.6f, 0.5f, 2.1f), glass);
        foreach (var p in new[] { new Vector3(-0.85f, 0.33f, 1.3f), new Vector3(0.85f, 0.33f, 1.3f), new Vector3(-0.85f, 0.33f, -1.3f), new Vector3(0.85f, 0.33f, -1.3f) })
            Prim(PrimitiveType.Cylinder, go.transform, p, new Vector3(0.66f, 0.12f, 0.66f), tyre, Quaternion.Euler(0f, 0f, 90f));
        go.SetActive(false);
        return go;
    }

    /// <summary>The park across the street: a playground (swings, a slide, a see-saw, a roundabout, a sandpit), benches, kids playing.</summary>
    public static void Park(Transform root, MenuWorld world, Vector3 c, Vector3 dir, float groundY, System.Func<float, float, float> R)
    {
        var park = new GameObject("Park").transform;
        park.SetParent(root, false);
        park.position = c;
        park.rotation = Quaternion.LookRotation(-dir); // (facing the house)
        Vector3 P(float x, float y, float z) => park.TransformPoint(new Vector3(x, y, z));
        var lawn = Mat("Park lawn", new Color(0.4f, 0.58f, 0.26f));
        var path = Mat("Park path", new Color(0.82f, 0.74f, 0.58f));
        var red = Mat("Play red", new Color(0.88f, 0.22f, 0.18f), 0.4f);
        var yellow = Mat("Play yellow", new Color(0.98f, 0.8f, 0.15f), 0.4f);
        var blue = Mat("Play blue", new Color(0.2f, 0.45f, 0.85f), 0.4f);
        var metal = Mat("Play metal", new Color(0.7f, 0.72f, 0.75f), 0.6f);
        var wood = Mat("Bench wood", new Color(0.55f, 0.36f, 0.2f));
        var sand = Mat("Sand", new Color(0.92f, 0.83f, 0.6f));
        var shirts = new[] { Mat("Shirt red", new Color(0.9f, 0.25f, 0.25f)), Mat("Shirt yellow", new Color(0.98f, 0.85f, 0.25f)), Mat("Shirt green", new Color(0.3f, 0.7f, 0.35f)), Mat("Shirt purple", new Color(0.6f, 0.35f, 0.8f)), Mat("Shirt orange", new Color(0.98f, 0.55f, 0.2f)) };
        var rot = park.rotation;

        Prim(PrimitiveType.Cube, park, P(0f, 0.03f, 0f), new Vector3(26f, 0.06f, 18f), lawn, rot);
        Prim(PrimitiveType.Cube, park, P(0f, 0.07f, 6.5f), new Vector3(26f, 0.04f, 1.6f), path, rot);

        // swings: an A-frame each end, a bar, two seats with kids (the pivots swing)
        var swings = new List<Transform>();
        Vector3 sw = P(-6f, 0f, -1f);
        foreach (float x in new[] { -1.8f, 1.8f })
            foreach (float lean in new[] { -12f, 12f })
                Prim(PrimitiveType.Cylinder, park, P(-6f + x, 1.3f, -1f), new Vector3(0.12f, 1.35f, 0.12f), red, rot * Quaternion.Euler(lean, 0f, 0f));
        Prim(PrimitiveType.Cylinder, park, P(-6f, 2.6f, -1f), new Vector3(0.12f, 1.9f, 0.12f), red, rot * Quaternion.Euler(0f, 0f, 90f));
        for (int i = 0; i < 2; i++)
        {
            var pivot = new GameObject("Swing").transform;
            pivot.SetParent(park, false);
            pivot.position = P(-6.9f + i * 1.8f, 2.6f, -1f);
            pivot.rotation = rot;
            foreach (float x in new[] { -0.25f, 0.25f })
                Prim(PrimitiveType.Cube, pivot, pivot.TransformPoint(new Vector3(x, -1.05f, 0f)), new Vector3(0.03f, 2.1f, 0.03f), metal, rot);
            Prim(PrimitiveType.Cube, pivot, pivot.TransformPoint(new Vector3(0f, -2.1f, 0f)), new Vector3(0.6f, 0.06f, 0.3f), yellow, rot);
            var kid = Person(pivot, pivot.TransformPoint(new Vector3(0f, -2.75f, 0f)), 1.15f, shirts[i], true);
            kid.transform.rotation = rot;
            swings.Add(pivot);
        }
        world.swings = swings.ToArray();

        // the slide
        Prim(PrimitiveType.Cube, park, P(2f, 1.1f, -3f), new Vector3(0.8f, 2.2f, 0.8f), blue, rot);
        Prim(PrimitiveType.Cube, park, P(2f, 1.05f, -1.4f), new Vector3(0.7f, 0.08f, 3.2f), yellow, rot * Quaternion.Euler(-34f, 0f, 0f));
        Person(park, P(2f, 2.2f, -3f), 1.1f, shirts[2], true).transform.rotation = rot;

        // the see-saw
        Prim(PrimitiveType.Cube, park, P(6f, 0.3f, 0f), new Vector3(0.3f, 0.6f, 0.3f), metal, rot);
        var saw = new GameObject("See-saw").transform;
        saw.SetParent(park, false);
        saw.position = P(6f, 0.62f, 0f);
        saw.rotation = rot;
        Prim(PrimitiveType.Cube, saw, saw.TransformPoint(Vector3.zero), new Vector3(0.35f, 0.08f, 3.4f), red, rot);
        Person(saw, saw.TransformPoint(new Vector3(0f, -0.45f, 1.5f)), 1.1f, shirts[3], true).transform.rotation = rot;
        Person(saw, saw.TransformPoint(new Vector3(0f, -0.45f, -1.5f)), 1.1f, shirts[4], true).transform.rotation = rot * Quaternion.Euler(0f, 180f, 0f);
        world.seesaw = saw;

        // the roundabout
        var spin = new GameObject("Roundabout").transform;
        spin.SetParent(park, false);
        spin.position = P(-1f, 0.15f, 3f);
        Prim(PrimitiveType.Cylinder, spin, spin.position, new Vector3(2.4f, 0.06f, 2.4f), blue);
        for (int i = 0; i < 4; i++)
        {
            float a = i * 90f * Mathf.Deg2Rad;
            Prim(PrimitiveType.Cylinder, spin, spin.position + new Vector3(Mathf.Cos(a), 0.45f, Mathf.Sin(a)) * 0.9f, new Vector3(0.05f, 0.45f, 0.05f), metal);
        }
        Person(spin, spin.position + new Vector3(0.7f, 0.06f, 0f), 1.1f, shirts[0], true);
        world.roundabout = spin;

        // a sandpit, benches, a grown-up watching
        Prim(PrimitiveType.Cube, park, P(-2f, 0.1f, -4f), new Vector3(3f, 0.16f, 3f), wood, rot);
        Prim(PrimitiveType.Cube, park, P(-2f, 0.18f, -4f), new Vector3(2.7f, 0.04f, 2.7f), sand, rot);
        foreach (float x in new[] { -9f, 9f })
        {
            Prim(PrimitiveType.Cube, park, P(x, 0.45f, 5f), new Vector3(1.8f, 0.08f, 0.5f), wood, rot);
            Prim(PrimitiveType.Cube, park, P(x, 0.75f, 5.25f), new Vector3(1.8f, 0.4f, 0.06f), wood, rot);
        }
        Person(park, P(9f, 0.05f, 5f), 1.7f, Mat("Shirt blue", new Color(0.3f, 0.45f, 0.8f)), false).transform.rotation = rot * Quaternion.Euler(0f, 180f, 0f);
        // kids running about
        var runners = new List<Transform>(); var centres = new List<Vector3>(); var radii = new List<float>();
        for (int i = 0; i < 3; i++)
        {
            Vector3 cc = P(R(-4f, 4f), 0.06f, R(-1f, 3f));
            runners.Add(Person(park, cc, R(1.05f, 1.25f), shirts[(i + 1) % shirts.Length], true).transform);
            centres.Add(cc); radii.Add(R(1.6f, 3.2f));
        }
        world.runners = runners.ToArray(); world.runnerCentres = centres.ToArray(); world.runnerRadii = radii.ToArray();
        // a kickabout on the grass at the back
        var kickers = new List<Transform>();
        foreach (var k in new[] { new Vector2(-9f, -5f), new Vector2(-4f, -7.5f), new Vector2(1f, -6f), new Vector2(-5f, -3.5f) })
            kickers.Add(Person(park, P(k.x, 0.06f, k.y), R(1.1f, 1.3f), shirts[kickers.Count % shirts.Length], true).transform);
        world.kickers = kickers.ToArray();
        world.ball = Prim(PrimitiveType.Sphere, park, P(-4f, 0.2f, -5f), Vector3.one * 0.28f, Mat("Ball", new Color(0.97f, 0.97f, 0.95f))).transform;
        // and someone flying a kite
        var flyer = Person(park, P(-1.5f, 0.06f, -8f), 1.25f, shirts[1], true).transform;
        flyer.rotation = rot * Quaternion.Euler(0f, 180f, 0f);
        var kite = new GameObject("Kite").transform;
        kite.SetParent(park, false);
        kite.position = P(-3f, 5.4f, -15f);
        var kiteRed = Mat("Kite red", new Color(0.95f, 0.2f, 0.25f), 0.3f, new Color(0.3f, 0.05f, 0.05f));
        var kiteYellow = Mat("Kite yellow", new Color(1f, 0.85f, 0.2f), 0.3f, new Color(0.3f, 0.25f, 0.05f));
        Prim(PrimitiveType.Cube, kite, kite.position, new Vector3(0.9f, 0.9f, 0.04f), kiteRed, Quaternion.Euler(0f, 0f, 45f));
        Prim(PrimitiveType.Cube, kite, kite.position + Vector3.forward * -0.01f, new Vector3(0.5f, 0.5f, 0.05f), kiteYellow, Quaternion.Euler(0f, 0f, 45f));
        for (int i = 0; i < 4; i++)
            Prim(PrimitiveType.Cube, kite, kite.position + Vector3.down * (0.75f + i * 0.35f), new Vector3(0.18f, 0.12f, 0.03f), i % 2 == 0 ? kiteYellow : kiteRed);
        var line = new GameObject("Kite string").AddComponent<LineRenderer>();
        line.transform.SetParent(park, false);
        line.positionCount = 8;
        line.widthMultiplier = 0.025f;
        line.sharedMaterial = Mat("Kite string", new Color(0.95f, 0.95f, 0.95f));
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        for (int i = 0; i < 8; i++) line.SetPosition(i, Vector3.Lerp(flyer.position + Vector3.up * 1.1f, kite.position, i / 7f));
        world.kite = kite; world.kiteHand = flyer; world.kiteString = line;
        // trees round the edge
        var trunk = Mat("Trunk", new Color(0.35f, 0.24f, 0.16f));
        var leaves = Mat("Leaves B", new Color(0.3f, 0.48f, 0.2f));
        foreach (var t in new[] { new Vector3(-12f, 0f, -7f), new Vector3(12f, 0f, -7f), new Vector3(-12f, 0f, 3f), new Vector3(12f, 0f, 2f) })
        {
            Vector3 b = P(t.x, 0f, t.z);
            Prim(PrimitiveType.Cylinder, park, b + Vector3.up * 2f, new Vector3(0.35f, 2f, 0.35f), trunk);
            Prim(PrimitiveType.Sphere, park, b + Vector3.up * 4.6f, Vector3.one * 3.6f, leaves);
        }
    }

    /// <summary>A fair on a hill far off: a big Ferris wheel turning slowly, its lights on for the evening.</summary>
    public static void Fair(Transform root, MenuWorld world, Vector3 at, Vector3 dir, float groundY)
    {
        var hill = Mat("Hill", new Color(0.36f, 0.5f, 0.24f));
        Prim(PrimitiveType.Sphere, root, new Vector3(at.x, groundY - 14f, at.z), new Vector3(140f, 34f, 90f), hill, Quaternion.LookRotation(dir));
        // a few rides and tents round its feet
        var tents = new[] { Mat("Tent red", new Color(0.85f, 0.25f, 0.22f)), Mat("Tent white", new Color(0.95f, 0.93f, 0.88f)), Mat("Tent yellow", new Color(0.97f, 0.8f, 0.3f)) };
        Vector3 across = Vector3.Cross(Vector3.up, dir);
        for (int i = 0; i < 6; i++)
        {
            Vector3 p = at + across * (-26f + i * 10.5f) - dir * (i % 2 == 0 ? 10f : 4f);
            p.y = groundY + 2.4f;
            Prim(PrimitiveType.Cylinder, root, p, new Vector3(5f, 1.4f, 5f), tents[i % tents.Length]);
            Prim(PrimitiveType.Cylinder, root, p + Vector3.up * 2.2f, new Vector3(5.4f, 0.8f, 5.4f), tents[(i + 1) % tents.Length]);
            Prim(PrimitiveType.Sphere, root, p + Vector3.up * 2.9f, new Vector3(1.4f, 2.2f, 1.4f), tents[(i + 1) % tents.Length]);
        }
        var frame = Mat("Wheel frame", new Color(0.95f, 0.95f, 0.97f), 0.5f);
        var bulbs = Mat("Wheel lights", new Color(1f, 0.86f, 0.5f), 0.5f, new Color(1f, 0.8f, 0.4f) * 2.2f);
        var cabinMats = new[] { Mat("Cabin red", new Color(0.9f, 0.25f, 0.25f)), Mat("Cabin blue", new Color(0.25f, 0.5f, 0.9f)), Mat("Cabin yellow", new Color(0.98f, 0.82f, 0.25f)), Mat("Cabin green", new Color(0.3f, 0.72f, 0.4f)) };
        float hubY = groundY + 3f + 17f, radius = 13f;
        Vector3 hubPos = new Vector3(at.x, hubY, at.z);
        Quaternion face = Quaternion.LookRotation(-dir);
        // the legs
        foreach (float side in new[] { -1f, 1f })
            foreach (float lean in new[] { -18f, 18f })
            {
                var leg = Prim(PrimitiveType.Cylinder, root, hubPos + face * new Vector3(0f, -9f, side * 1.6f), new Vector3(0.6f, 9.6f, 0.6f), frame, face * Quaternion.Euler(0f, 0f, lean));
            }
        var wheel = new GameObject("Ferris wheel").transform;
        wheel.SetParent(root, false);
        wheel.position = hubPos;
        wheel.rotation = face;
        Prim(PrimitiveType.Cylinder, wheel, hubPos, new Vector3(1.4f, 0.6f, 1.4f), frame, face * Quaternion.Euler(90f, 0f, 0f));
        int n = 24;
        for (int i = 0; i < n; i++)
        {
            float a = i * Mathf.PI * 2f / n;
            Vector3 p = wheel.TransformPoint(new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * radius);
            Prim(PrimitiveType.Cube, wheel, p, new Vector3(0.35f, 2 * Mathf.PI * radius / n + 0.2f, 0.35f), frame, face * Quaternion.Euler(0f, 0f, a * Mathf.Rad2Deg));
            Prim(PrimitiveType.Sphere, wheel, p + face * new Vector3(0f, 0f, -0.3f), Vector3.one * 0.45f, bulbs);
            if (i % 3 == 0)
                Prim(PrimitiveType.Cube, wheel, wheel.TransformPoint(new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * radius * 0.5f), new Vector3(0.25f, radius, 0.25f), frame, face * Quaternion.Euler(0f, 0f, a * Mathf.Rad2Deg));
        }
        var cabins = new List<Transform>();
        for (int i = 0; i < 8; i++)
        {
            float a = i * Mathf.PI * 2f / 8f;
            var cab = new GameObject("Cabin").transform;
            cab.SetParent(wheel, false);
            cab.position = wheel.TransformPoint(new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * radius) + Vector3.down * 1.2f;
            Prim(PrimitiveType.Cube, cab, cab.position, new Vector3(1.8f, 1.6f, 1.8f), cabinMats[i % cabinMats.Length], face);
            cabins.Add(cab);
        }
        world.ferrisWheel = wheel;
        world.ferrisCabins = cabins.ToArray();
    }

    /// <summary>The rest of town on the horizon all round: roofs and clumps of trees fading into the evening haze.</summary>
    public static void Skyline(Transform root, Vector3 at, float groundY, Vector3 keepClear, Material[] walls, Material[] roofs, Material[] leaves, Material trunk, System.Func<float, float, float> R)
    {
        var town = new GameObject("Skyline").transform;
        town.SetParent(root, false);
        for (int i = 0; i < 70; i++)
        {
            float angle = i * 360f / 70f + R(-2f, 2f);
            Vector3 dir = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
            if (Vector3.Angle(dir, keepClear) < 7f) continue; // (the Ferris wheel's there)
            float dist = R(95f, 170f);
            Vector3 p = at + dir * dist; p.y = groundY;
            if (i % 3 == 0)
            {
                for (int k = 0; k < 3; k++)
                    Prim(PrimitiveType.Sphere, town, p + new Vector3(R(-4f, 4f), R(5f, 8f), R(-4f, 4f)), Vector3.one * R(6f, 9f), leaves[(i + k) % leaves.Length]);
                Prim(PrimitiveType.Cylinder, town, p + Vector3.up * 2.5f, new Vector3(0.8f, 2.5f, 0.8f), trunk);
            }
            else
            {
                float w = R(7f, 11f), d = R(6f, 9f), h = R(5f, 8f);
                Quaternion face = Quaternion.LookRotation(-dir);
                Prim(PrimitiveType.Cube, town, p + Vector3.up * h * 0.5f, new Vector3(w, h, d), walls[i % walls.Length], face);
                Prim(PrimitiveType.Cube, town, p + Vector3.up * (h + 1.2f), new Vector3(w + 0.6f, 2.4f, d * 0.75f), roofs[i % roofs.Length], face * Quaternion.Euler(0f, 0f, 0f));
            }
        }
    }

    /// <summary>Clouds drifting by, and a plane now and then.</summary>
    public static void Sky(Transform root, MenuWorld world, Vector3 centre, Vector3 dir, float groundY, System.Func<float, float, float> R)
    {
        var white = Mat("Cloud", new Color(1f, 0.96f, 0.92f), 0f, new Color(0.35f, 0.25f, 0.2f));
        var clouds = new List<Transform>();
        for (int i = 0; i < 10; i++)
        {
            var c = new GameObject("Cloud").transform;
            c.SetParent(root, false);
            c.position = centre + Quaternion.Euler(0f, R(-80f, 80f), 0f) * dir * R(110f, 190f) + Vector3.up * (groundY + R(45f, 70f));
            for (int k = 0; k < 4; k++)
                Prim(PrimitiveType.Sphere, c, c.position + new Vector3(R(-7f, 7f), R(-1.5f, 1.5f), R(-4f, 4f)), new Vector3(R(10f, 16f), R(4f, 6f), R(7f, 11f)), white);
            clouds.Add(c);
        }
        world.clouds = clouds.ToArray();
        world.cloudDrift = Vector3.Cross(Vector3.up, dir) * 1.4f;
        var plane = new GameObject("Plane").transform;
        plane.SetParent(root, false);
        var silver = Mat("Plane", new Color(0.92f, 0.92f, 0.95f), 0.7f);
        Prim(PrimitiveType.Capsule, plane, Vector3.zero, new Vector3(1.2f, 4f, 1.2f), silver, Quaternion.Euler(90f, 0f, 0f));
        Prim(PrimitiveType.Cube, plane, Vector3.zero, new Vector3(9f, 0.15f, 1.6f), silver);
        Prim(PrimitiveType.Cube, plane, new Vector3(0f, 0.9f, -3.4f), new Vector3(0.15f, 1.6f, 1.2f), silver);
        Vector3 across = Vector3.Cross(Vector3.up, dir);
        world.plane = plane;
        world.planeFrom = centre + dir * 160f - across * 220f + Vector3.up * (groundY + 95f);
        world.planeTo = centre + dir * 160f + across * 220f + Vector3.up * (groundY + 110f);
    }

    // ------------------------------------------------------------------ round the house (the game)

    /// <summary>
    /// The world round the house, so the windows look out on something: your lawn and a picket fence round it, the driveway and
    /// the front path out to the street (on the garage side), the road with cars and someone walking their dog, the neighbours'
    /// houses across the road and either side, trees, the park behind with kids playing, the fair's Ferris wheel far off, the
    /// rest of town on the horizon, clouds and a plane. Only the ground and the fence line are solid (walk out of the garage and
    /// you're on your lawn, not falling); the rest is to look at, combined into a few draws with no shadows.
    /// </summary>
    public static Transform AroundHouse(HouseLayout layout)
    {
        if (!layout) return null;
        float s = layout.scale;
        var lt = layout.transform;
        var plan = layout.PlanBounds();
        float groundY = lt.position.y;
        Vector3 P(float x, float z) { var w = lt.TransformPoint(new Vector3(x, 0f, z)); w.y = groundY; return w; }
        Vector3 centre = P(plan.center.x, plan.center.y);
        Vector3 ax = Flat(lt.right).normalized, az = Flat(lt.forward).normalized;
        float hx = Vector3.Distance(P(plan.xMin, plan.center.y), P(plan.xMax, plan.center.y)) * 0.5f;
        float hz = Vector3.Distance(P(plan.center.x, plan.yMin), P(plan.center.x, plan.yMax)) * 0.5f;

        // the front: the side with the garage door (else the front door)
        Vector3 doorAt = centre - az * hz;
        var garage = Object.FindObjectOfType<GarageDoor>();
        if (garage) doorAt = garage.transform.position;
        else { var d = GameObject.Find("Door - Outside "); if (d) doorAt = d.transform.position; }
        Vector3 toDoor = Flat(doorAt - centre);
        Vector3 front = Mathf.Abs(Vector3.Dot(toDoor, ax)) / hx > Mathf.Abs(Vector3.Dot(toDoor, az)) / hz ? ax * Mathf.Sign(Vector3.Dot(toDoor, ax)) : az * Mathf.Sign(Vector3.Dot(toDoor, az));
        Vector3 side = Vector3.Cross(Vector3.up, front);
        float halfDepth = Mathf.Abs(Vector3.Dot(front, ax)) > 0.5f ? hx : hz;   // house half-size towards the street
        float halfWidth = Mathf.Abs(Vector3.Dot(front, ax)) > 0.5f ? hz : hx;   // ...and along it
        Quaternion facing = Quaternion.LookRotation(front);

        var root = new GameObject("Around the house").transform;
        var still = new GameObject("Still").transform; still.SetParent(root, false);
        var moving = new GameObject("Moving").transform; moving.SetParent(root, false);
        var world = moving.gameObject.AddComponent<MenuWorld>();
        var rnd = new System.Random(7);
        float R(float a, float b) => a + (float)rnd.NextDouble() * (b - a);

        var grass = Mat("Grass", new Color(0.33f, 0.47f, 0.22f));
        var lawn = Mat("Lawn", new Color(0.37f, 0.53f, 0.25f));
        var road = Mat("Road", new Color(0.2f, 0.2f, 0.22f), 0.3f);
        var pavement = Mat("Pavement", new Color(0.62f, 0.6f, 0.57f));
        var concrete = Mat("Driveway", new Color(0.7f, 0.68f, 0.64f));
        var fence = Mat("Fence", new Color(0.93f, 0.92f, 0.88f), 0.25f);
        var lines = Mat("Road lines", new Color(0.95f, 0.92f, 0.8f));
        var trunk = Mat("Trunk", new Color(0.35f, 0.24f, 0.16f));
        var leaves = new[] { Mat("Leaves A", new Color(0.24f, 0.42f, 0.18f)), Mat("Leaves B", new Color(0.3f, 0.48f, 0.2f)), Mat("Leaves C", new Color(0.42f, 0.5f, 0.2f)) };
        var walls = new[] { Mat("House cream", new Color(0.92f, 0.86f, 0.72f)), Mat("House blue", new Color(0.62f, 0.72f, 0.82f)), Mat("House brick", new Color(0.66f, 0.34f, 0.28f)), Mat("House sage", new Color(0.66f, 0.74f, 0.6f)) };
        var roofs = new[] { Mat("Roof dark", new Color(0.26f, 0.22f, 0.24f)), Mat("Roof red", new Color(0.55f, 0.24f, 0.2f)), Mat("Roof slate", new Color(0.33f, 0.37f, 0.42f)) };
        var lit = Mat("Window lit", new Color(1f, 0.82f, 0.5f), 0.6f, new Color(1f, 0.7f, 0.35f) * 1.6f);
        var dark = Mat("Window dark", new Color(0.18f, 0.22f, 0.3f), 0.8f);
        var flowers = new[] { Mat("Flowers pink", new Color(0.92f, 0.45f, 0.6f)), Mat("Flowers yellow", new Color(0.98f, 0.82f, 0.3f)), Mat("Flowers white", new Color(0.95f, 0.95f, 0.92f)) };
        var lampPost = Mat("Lamp post", new Color(0.2f, 0.22f, 0.25f));
        var lampGlow = Mat("Lamp glow", new Color(1f, 0.85f, 0.55f), 0.5f, new Color(1f, 0.75f, 0.4f) * 2.5f);

        // the ground (solid: just under the house's own floors)
        var ground = Prim(PrimitiveType.Cube, still, centre + Vector3.down * 0.16f, new Vector3(1200f, 0.2f, 1200f), grass);
        ground.AddComponent<BoxCollider>();

        // our plot: a lawn, a picket fence round it with gaps for the driveway and the path
        float yard = 6f * s, frontYard = 8f * s;
        float plotFront = halfDepth + frontYard, plotBack = halfDepth + yard, plotSide = halfWidth + yard;
        Vector3 plotCentre = centre + front * (plotFront - plotBack) * 0.5f;
        Prim(PrimitiveType.Cube, still, plotCentre + Vector3.down * 0.055f, new Vector3((plotSide * 2f), 0.02f, plotFront + plotBack), lawn, facing);
        float drivewayAt = Vector3.Dot(Flat(doorAt - centre), side);
        Vector3 driveStart = centre + front * halfDepth + side * drivewayAt;
        Prim(PrimitiveType.Cube, still, driveStart + front * frontYard * 0.5f + Vector3.down * 0.04f, new Vector3(3.6f * s, 0.04f, frontYard), concrete, facing);
        // the outside door on the street side (there's one at the back too)
        GameObject mainDoor = null;
        foreach (var t in Object.FindObjectsOfType<Transform>())
            if (t.name.StartsWith("Door - Outside") && (!mainDoor || Vector3.Dot(t.position - centre, front) > Vector3.Dot(mainDoor.transform.position - centre, front))) mainDoor = t.gameObject;
        float pathAt = mainDoor ? Vector3.Dot(Flat(mainDoor.transform.position - centre), side) : drivewayAt + 5f * s;
        if (mainDoor && Vector3.Dot(Flat(mainDoor.transform.position - centre), front) > 0f && Mathf.Abs(pathAt - drivewayAt) > 3f * s)
            Prim(PrimitiveType.Cube, still, centre + front * (halfDepth + frontYard * 0.5f) + side * pathAt + Vector3.down * 0.04f, new Vector3(1.3f * s, 0.035f, frontYard), concrete, facing);
        else pathAt = float.NaN;

        void FenceLine(Vector3 a, Vector3 b, params float[] gapsAlong)
        {
            Vector3 dir = Flat(b - a); float len = dir.magnitude; dir /= len;
            Quaternion q = Quaternion.LookRotation(dir);
            for (float t = 0f; t <= len; t += 0.5f)
            {
                bool gap = false;
                foreach (var g in gapsAlong) if (!float.IsNaN(g) && Mathf.Abs(t - g) < 2.1f * s) gap = true;
                if (gap) continue;
                Vector3 p = a + dir * t;
                Prim(PrimitiveType.Cube, still, p + Vector3.up * 0.5f, new Vector3(0.08f, 1f, 0.08f), fence, q);
            }
            for (int rail = 0; rail < 2; rail++)
                Prim(PrimitiveType.Cube, still, (a + b) * 0.5f + Vector3.up * (0.35f + rail * 0.45f), new Vector3(0.05f, 0.07f, len), fence, q);
            // (an invisible wall along it: you stay in the yard)
            var wall = new GameObject("Fence wall").AddComponent<BoxCollider>();
            wall.transform.SetParent(root, false);
            wall.transform.position = (a + b) * 0.5f + Vector3.up * 1.5f;
            wall.transform.rotation = q;
            wall.size = new Vector3(0.3f, 3f, len);
        }
        Vector3 fl = centre + front * plotFront - side * plotSide, fr = centre + front * plotFront + side * plotSide;
        Vector3 bl = centre - front * plotBack - side * plotSide, br = centre - front * plotBack + side * plotSide;
        FenceLine(fl, fr, drivewayAt + plotSide, float.IsNaN(pathAt) ? float.NaN : pathAt + plotSide);
        FenceLine(fr, br); FenceLine(br, bl); FenceLine(bl, fl);
        // flower beds inside the front fence, a couple of trees in the back garden
        for (int f = 0; f < 16; f++)
        {
            float along = R(-plotSide + 1f, plotSide - 1f);
            if (Mathf.Abs(along - drivewayAt) < 2.6f * s || (!float.IsNaN(pathAt) && Mathf.Abs(along - pathAt) < 1.6f * s)) continue;
            Vector3 p = centre + front * (plotFront - R(0.6f, 1.6f)) + side * along;
            Prim(PrimitiveType.Sphere, still, p + Vector3.up * 0.18f, new Vector3(R(0.5f, 0.9f), R(0.3f, 0.5f), R(0.5f, 0.9f)), leaves[f % 3]);
            Prim(PrimitiveType.Sphere, still, p + Vector3.up * 0.38f, Vector3.one * R(0.18f, 0.3f), flowers[f % 3]);
        }
        for (int t = 0; t < 4; t++) Tree(still, centre - front * (halfDepth + R(2.5f, yard - 1.5f)) + side * R(-plotSide + 2f, plotSide - 2f), R(3.2f, 4.6f), trunk, leaves, R);

        // the street out front
        Vector3 street = centre + front * (plotFront + 6f);
        Prim(PrimitiveType.Cube, still, street + Vector3.down * 0.03f, new Vector3(320f, 0.1f, 7f), road, facing); // (long side along the street)
        foreach (float o in new[] { -4.6f, 4.6f })
            Prim(PrimitiveType.Cube, still, street + front * o + Vector3.up * 0.0f, new Vector3(320f, 0.12f, 2.2f), pavement, facing);
        for (float t = -150f; t <= 150f; t += 6f)
            Prim(PrimitiveType.Cube, still, street + side * t + Vector3.up * 0.03f, new Vector3(0.18f, 0.02f, 2.6f), lines, Quaternion.LookRotation(side));
        for (float t = -60f; t <= 60f; t += 14f)
        {
            Vector3 lp = street - front * 5.4f + side * t;
            Prim(PrimitiveType.Cylinder, still, lp + Vector3.up * 2.2f, new Vector3(0.14f, 2.2f, 0.14f), lampPost);
            Prim(PrimitiveType.Sphere, still, lp + Vector3.up * 4.5f, Vector3.one * 0.45f, lampGlow);
        }
        world.roadFrom = street - side * 140f + Vector3.up * 0.05f; world.roadTo = street + side * 140f + Vector3.up * 0.05f; world.laneOffset = front * 1.7f;
        world.walkFrom = street + front * 4.6f - side * 25f + Vector3.up * 0.1f; world.walkTo = street + front * 4.6f + side * 25f + Vector3.up * 0.1f;
        // the neighbours: across the road, and either side of us
        for (int h = -4; h <= 4; h++)
            House(still, street + front * (14f + R(0f, 2f)) + side * (h * 13f + R(-1.5f, 1.5f)), Quaternion.LookRotation(-front), R(8f, 10f), R(7f, 9f), R(5.5f, 7f), walls[(h + 8) % 4], roofs[(h + 9) % 3], lit, dark, rnd);
        foreach (float sgn in new[] { -1f, 1f })
            for (int h = 0; h < 2; h++)
                House(still, centre + side * sgn * (plotSide + 8f + h * 0f) + front * (h == 0 ? halfDepth * 0.4f : -halfDepth - 2f) + side * sgn * h * 4f, facing, R(8f, 10f), R(8f, 10f), R(5.5f, 7f), walls[h + (sgn > 0 ? 2 : 0)], roofs[h], lit, dark, rnd);
        for (int t = 0; t < 10; t++) Tree(still, street - front * 3.3f + side * (-60f + t * 13f + R(-1f, 1f)), R(2.8f, 4f), trunk, leaves, R);
        for (int t = 0; t < 10; t++) Tree(still, street + front * 8.5f + side * (-60f + t * 13f + R(-1f, 1f)), R(3f, 4.5f), trunk, leaves, R);

        // behind: a row of trees, then the park with kids playing
        Vector3 back = -front;
        for (int t = 0; t < 9; t++) Tree(still, centre + back * (plotBack + R(3f, 6f)) + side * (-24f + t * 6f), R(3.5f, 5.5f), trunk, leaves, R);
        Vector3 parkAt = centre + back * (plotBack + 22f);
        Park(moving, world, parkAt, back, groundY, R);
        // far off: the fair on its hill, the town on the horizon, the sky
        Vector3 fairDir = (back + side * 0.6f).normalized;
        Fair(moving, world, centre + fairDir * 260f, fairDir, groundY);
        Skyline(still, centre, groundY, fairDir, walls, roofs, leaves, trunk, R);
        Sky(moving, world, centre, back, groundY, R);
        world.walker = Person(moving, world.walkFrom, 1.75f, Mat("Shirt teal", new Color(0.2f, 0.55f, 0.6f)), false).transform;
        world.dog = Dog(moving, world.walkFrom).transform;
        var cars = new List<GameObject>();
        foreach (var c in new[] { new Color(0.8f, 0.15f, 0.12f), new Color(0.15f, 0.35f, 0.75f), new Color(0.92f, 0.92f, 0.9f), new Color(0.95f, 0.75f, 0.15f) })
        { var car = Car(moving, c); cars.Add(car); }
        world.carTemplates = cars.ToArray();

        // light to run: no shadows out here, and the still parts drawn as a few big batches
        foreach (var r in root.GetComponentsInChildren<Renderer>(true)) { r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false; }
        try { StaticBatchingUtility.Combine(still.gameObject); } catch (System.Exception e) { Debug.LogWarning("[Around the house] no batching: " + e.Message); }
        Debug.Log($"[Around the house] built: front {front}, plot {plotSide * 2f:F0} x {plotFront + plotBack:F0} m, {root.GetComponentsInChildren<Renderer>(true).Length} pieces");
        return root;
    }

    static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

    public static void Tree(Transform parent, Vector3 at, float h, Material trunk, Material[] leaves, System.Func<float, float, float> R)
    {
        Prim(PrimitiveType.Cylinder, parent, at + Vector3.up * h * 0.5f, new Vector3(0.24f, h * 0.5f, 0.24f), trunk);
        for (int k = 0; k < 3; k++)
            Prim(PrimitiveType.Sphere, parent, at + new Vector3(R(-0.6f, 0.6f), h + R(-0.3f, 0.6f), R(-0.6f, 0.6f)), Vector3.one * R(1.8f, 2.6f), leaves[k % leaves.Length]);
    }

    /// <summary>A neighbour's house: walls, a pitched roof, windows (some lit for the evening) and a door, facing 'face'.</summary>
    public static Transform House(Transform parent, Vector3 at, Quaternion face, float w, float d, float h, Material wall, Material roofM, Material lit, Material dark, System.Random rnd)
    {
        var house = new GameObject("Neighbour's house").transform;
        house.SetParent(parent, false);
        house.position = at;
        house.rotation = face;
        Prim(PrimitiveType.Cube, house, house.TransformPoint(new Vector3(0f, h * 0.5f, 0f)), new Vector3(w, h, d), wall, face);
        float pitch = 32f, half = d * 0.5f / Mathf.Cos(pitch * Mathf.Deg2Rad);
        for (int sideIdx = -1; sideIdx <= 1; sideIdx += 2)
        {
            var slabRot = face * Quaternion.Euler(sideIdx * pitch, 0f, 0f);
            Vector3 local = new Vector3(0f, h + Mathf.Tan(pitch * Mathf.Deg2Rad) * d * 0.25f, sideIdx * d * 0.25f);
            Prim(PrimitiveType.Cube, house, house.TransformPoint(local), new Vector3(w + 0.6f, 0.18f, half + 0.4f), roofM, slabRot);
        }
        for (int row = 0; row < 2; row++)
            for (int col = 0; col < 3; col++)
            {
                if (row == 0 && col == 1) continue;
                foreach (float zSide in new[] { 1f, -1f })
                {
                    Vector3 wl = new Vector3(-w * 0.32f + col * w * 0.32f, h * (row == 0 ? 0.3f : 0.68f), zSide * (d * 0.5f + 0.02f));
                    Prim(PrimitiveType.Cube, house, house.TransformPoint(wl), new Vector3(1.1f, 1.1f, 0.05f), rnd.NextDouble() < 0.55 ? lit : dark, face);
                }
            }
        Prim(PrimitiveType.Cube, house, house.TransformPoint(new Vector3(0f, 1.05f, d * 0.5f + 0.02f)), new Vector3(1f, 2.1f, 0.05f), roofM, face);
        return house;
    }
}
