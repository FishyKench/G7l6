using TMPro;
using UnityEngine;

/// <summary>
/// The ranked map, "Neon District": a night-time city block about 120 x 80 m for 8v8 Domination.
///   - Your team spawns in a lot on the west side, the enemy on the east (mirrored, so it's fair).
///   - A (west) sits under a gas station canopy, C (east) under the same on the other side, B in the central plaza
///     round a fountain with a crashed bus next to it.
///   - Three lanes: the main street through the middle and a wide street to the north and south, split by building
///     blocks with alleys through them so you can rotate between lanes.
///   - Cover everywhere: parked cars, a taxi, buses, a semi truck, shipping containers, concrete barriers,
///     dumpsters, fuel pumps, planters. Street lamps, neon signs, glowing windows and a tall skyline all round.
/// Everything is made of simple shapes with the arena's materials (Assets/Materials/VR).
/// </summary>
public static partial class VRArenaBuilder
{
    static Material asphalt, sidewalk, concrete, glass, tire, metal, warm, red, white, cyanM, pinkM, violetM, amber;
    static Material[] facades, paints, boxes;
    static System.Random rng;

    static void BuildCity(Transform root)
    {
        rng = new System.Random(7);
        asphalt = Mat("VR Asphalt", new Color(0.07f, 0.075f, 0.09f), Color.black, 0f, 0.45f);
        sidewalk = Mat("VR Sidewalk", new Color(0.17f, 0.17f, 0.21f), Color.black, 0f, 0.2f);
        concrete = Mat("VR Concrete", new Color(0.42f, 0.42f, 0.46f), Color.black, 0f, 0.15f);
        glass = Mat("VR Glass", new Color(0.03f, 0.04f, 0.06f), new Color(0.1f, 0.2f, 0.35f), 0.3f, 0.95f);
        tire = Mat("VR Tire", new Color(0.03f, 0.03f, 0.035f), Color.black, 0f, 0.1f);
        metal = Mat("VR Metal", new Color(0.33f, 0.35f, 0.4f), Color.black, 0f, 0.6f);
        warm = Mat("VR Lamp", new Color(1f, 0.8f, 0.5f), new Color(1f, 0.75f, 0.45f), 3f, 0.5f);
        red = Mat("VR Tail Light", new Color(1f, 0.1f, 0.1f), new Color(1f, 0.1f, 0.1f), 2.5f, 0.5f);
        white = Mat("VR Head Light", Color.white, Color.white, 2.5f, 0.5f);
        cyanM = Mat("VR Neon Cyan", new Color(0.2f, 0.9f, 1f), new Color(0.2f, 0.9f, 1f), 2.2f, 0.5f);
        pinkM = Mat("VR Neon Pink", new Color(1f, 0.25f, 0.75f), new Color(1f, 0.25f, 0.75f), 2.2f, 0.5f);
        violetM = Mat("VR Neon Violet", new Color(0.55f, 0.3f, 1f), new Color(0.55f, 0.3f, 1f), 2.4f, 0.5f);
        amber = Mat("VR Neon Amber", new Color(1f, 0.6f, 0.15f), new Color(1f, 0.55f, 0.1f), 2.4f, 0.5f);
        facades = new[]
        {
            Mat("VR Facade 1", new Color(0.1f, 0.09f, 0.15f), Color.black, 0f, 0.3f),
            Mat("VR Facade 2", new Color(0.13f, 0.12f, 0.17f), Color.black, 0f, 0.3f),
            Mat("VR Facade 3", new Color(0.08f, 0.11f, 0.15f), Color.black, 0f, 0.3f),
            Mat("VR Facade 4", new Color(0.16f, 0.12f, 0.13f), Color.black, 0f, 0.3f),
        };
        paints = new[]
        {
            Mat("VR Paint Red", new Color(0.6f, 0.08f, 0.1f), Color.black, 0f, 0.8f),
            Mat("VR Paint Blue", new Color(0.1f, 0.2f, 0.5f), Color.black, 0f, 0.8f),
            Mat("VR Paint White", new Color(0.75f, 0.76f, 0.8f), Color.black, 0f, 0.8f),
            Mat("VR Paint Black", new Color(0.05f, 0.05f, 0.06f), Color.black, 0f, 0.85f),
            Mat("VR Paint Teal", new Color(0.08f, 0.4f, 0.42f), Color.black, 0f, 0.8f),
        };
        boxes = new[]
        {
            Mat("VR Container Orange", new Color(0.7f, 0.3f, 0.1f), Color.black, 0f, 0.3f),
            Mat("VR Container Blue", new Color(0.12f, 0.27f, 0.5f), Color.black, 0f, 0.3f),
            Mat("VR Container Green", new Color(0.17f, 0.4f, 0.27f), Color.black, 0f, 0.3f),
        };
        var taxi = Mat("VR Paint Taxi", new Color(0.95f, 0.7f, 0.1f), Color.black, 0f, 0.8f);

        // --- ground, sidewalks, the edge of the map
        Solid(root, "Street", new Vector3(0f, -0.25f, 0f), new Vector3(140f, 0.5f, 100f), asphalt);
        Solid(root, "Sidewalk N", new Vector3(0f, 0.075f, 38f), new Vector3(120f, 0.15f, 4f), sidewalk);
        Solid(root, "Sidewalk S", new Vector3(0f, 0.075f, -38f), new Vector3(120f, 0.15f, 4f), sidewalk);
        Solid(root, "Sidewalk W", new Vector3(-58f, 0.075f, 0f), new Vector3(4f, 0.15f, 72f), sidewalk);
        Solid(root, "Sidewalk E", new Vector3(58f, 0.075f, 0f), new Vector3(4f, 0.15f, 72f), sidewalk);
        Invisible(root, new Vector3(0f, 20f, 40.6f), new Vector3(130f, 40f, 1f));
        Invisible(root, new Vector3(0f, 20f, -40.6f), new Vector3(130f, 40f, 1f));
        Invisible(root, new Vector3(-60.6f, 20f, 0f), new Vector3(1f, 40f, 90f));
        Invisible(root, new Vector3(60.6f, 20f, 0f), new Vector3(1f, 40f, 90f));

        // Buildings all round the edge (they're the walls of the map).
        var edge = new GameObject("Edge buildings").transform;
        edge.SetParent(root, false);
        string[] signs = { "HOTEL", "NOODLES", "ARCADE", "24/7", "SUSHI", "CYBER CAFE", "LAUNDROMAT", "PAWN", "KARAOKE", "PHARMACY" };
        int sign = 0;
        for (int side = -1; side <= 1; side += 2)
        {
            for (float x = -66f; x < 66f;)
            {
                float w = 9f + (float)rng.NextDouble() * 9f;
                w = Mathf.Min(w, 66f - x);
                float h = 12f + (float)rng.NextDouble() * 24f;
                Building(edge, new Vector3(x + w * 0.5f, 0f, side * 45.5f), new Vector3(w, h, 11f), side > 0 ? 180f : 0f,
                    rng.NextDouble() < 0.45 ? signs[sign++ % signs.Length] : null);
                x += w;
            }
            for (float z = -40f; z < 40f;)
            {
                float w = 9f + (float)rng.NextDouble() * 9f;
                w = Mathf.Min(w, 40f - z);
                float h = 12f + (float)rng.NextDouble() * 22f;
                Building(edge, new Vector3(side * 65.5f, 0f, z + w * 0.5f), new Vector3(w, h, 11f), side > 0 ? -90f : 90f,
                    rng.NextDouble() < 0.35 ? signs[sign++ % signs.Length] : null);
                z += w;
            }
        }

        // --- the middle of the map (mirrored through the centre so both teams get the same)
        var city = new GameObject("District").transform;
        city.SetParent(root, false);
        Both(city, (p, s) =>
        {
            // Building blocks between the lanes, with an alley through each row.
            Building(p, P(s, -36.5f, 13f), new Vector3(11f, 10f, 8f), Yaw(s, 0f), s > 0 ? "ARCADE" : "NOODLES");
            Building(p, P(s, -20.5f, 13f), new Vector3(13f, 14f, 8f), Yaw(s, 0f), null);
            Building(p, P(s, -36.5f, -13f), new Vector3(11f, 13f, 8f), Yaw(s, 180f), null);
            Building(p, P(s, -20.5f, -13f), new Vector3(13f, 9f, 8f), Yaw(s, 180f), s > 0 ? "KARAOKE" : "24/7");

            // A / C: gas station - canopy over the point, fuel pumps as low cover, a little shop behind.
            var station = Group(p, "Gas station", P(s, -33f, 0f), Yaw(s, 0f));
            Solid(station, "Canopy", new Vector3(0f, 4.7f, 0f), new Vector3(9f, 0.5f, 11f), metal);
            Glow(station, new Vector3(0f, 4.7f, 5.52f), new Vector3(9.05f, 0.18f, 0.05f), s > 0 ? cyanM : pinkM);
            Glow(station, new Vector3(0f, 4.7f, -5.52f), new Vector3(9.05f, 0.18f, 0.05f), s > 0 ? cyanM : pinkM);
            Glow(station, new Vector3(0f, 4.42f, 0f), new Vector3(7f, 0.05f, 9f), warm);
            foreach (var c in new[] { new Vector2(-3.8f, -4.6f), new Vector2(3.8f, -4.6f), new Vector2(-3.8f, 4.6f), new Vector2(3.8f, 4.6f) })
                Solid(station, "Pillar", new Vector3(c.x, 2.25f, c.y), new Vector3(0.5f, 4.5f, 0.5f), concrete);
            for (int i = -1; i <= 1; i += 2)
            {
                Solid(station, "Pump island", new Vector3(i * 2f, 0.15f, 0f), new Vector3(1.4f, 0.3f, 4.2f), concrete);
                Solid(station, "Pump", new Vector3(i * 2f, 0.95f, -1f), new Vector3(0.7f, 1.3f, 0.9f), paints[2]);
                Solid(station, "Pump", new Vector3(i * 2f, 0.95f, 1f), new Vector3(0.7f, 1.3f, 0.9f), paints[2]);
                Glow(station, new Vector3(i * 2f + 0.36f * i, 1.25f, -1f), new Vector3(0.02f, 0.3f, 0.5f), cyanM);
                Glow(station, new Vector3(i * 2f + 0.36f * i, 1.25f, 1f), new Vector3(0.02f, 0.3f, 0.5f), cyanM);
            }
            Lamp(station, new Vector3(0f, 3.8f, 0f), new Color(1f, 0.85f, 0.65f), 14f, 1.3f);
            Building(p, P(s, -46.5f, 0f), new Vector3(5f, 4.5f, 9f), Yaw(s, 90f), "24/7");
            Car(p, P(s, -29.5f, 3.5f), Yaw(s, 180f), paints[(int)(s + 1) % paints.Length]);

            // Spawn lot cover.
            Container(p, P(s, -48f, 16f), Yaw(s, 0f), boxes[0]);
            Container(p, P(s, -48f, -16f), Yaw(s, 0f), boxes[1]);
            Container(p, P(s, -48f, -16f) + Vector3.up * 2.6f, Yaw(s, 0f), boxes[2]);
            Barrier(p, P(s, -42f, 7f), Yaw(s, 0f));
            Barrier(p, P(s, -42f, -7f), Yaw(s, 0f));

            // B: the plaza (one half of it).
            Solid(p, "Planter", P(s, -7.5f, 5.5f), new Vector3(3.2f, 0.9f, 1.2f), concrete);
            Glow(p, P(s, -7.5f, 5.5f) + Vector3.up * 0.47f, new Vector3(3f, 0.05f, 1f), Mat("VR Grass Glow", new Color(0.1f, 0.4f, 0.25f), new Color(0.1f, 0.6f, 0.35f), 0.8f, 0.2f));
            Solid(p, "Planter", P(s, 7.5f, 5.5f), new Vector3(3.2f, 0.9f, 1.2f), concrete);
            Bus(p, P(s, 5.5f, 13.5f), Yaw(s, 62f), paints[1]);
            Barrier(p, P(s, -10.5f, 0f), Yaw(s, 0f));
            Kiosk(p, P(s, 10f, -6.5f), Yaw(s, 180f));

            // North / south street.
            Truck(p, P(s, -11f, 27f), Yaw(s, 90f));
            Car(p, P(s, -24f, 21.5f), Yaw(s, 105f), taxi, true);
            Car(p, P(s, 3f, 24f), Yaw(s, 75f), paints[3]);
            Container(p, P(s, 24f, 24f), Yaw(s, 90f), boxes[2]);
            Container(p, P(s, 31f, 30.5f), Yaw(s, 0f), boxes[0]);
            Barrier(p, P(s, -38f, 24f), Yaw(s, 0f));
            Barrier(p, P(s, -1f, 31f), Yaw(s, 90f));
            Barrier(p, P(s, 41f, 22f), Yaw(s, 20f));
            Dumpster(p, P(s, -33f, 18.4f), Yaw(s, 0f));
            Dumpster(p, P(s, -16f, 18.4f), Yaw(s, 0f));
            Dumpster(p, P(s, -29f, 8.4f), Yaw(s, 180f));
            for (int i = 0; i < 5; i++)
                Car(p, P(s, -50f + i * 22f + (float)rng.NextDouble() * 4f, 34.6f), Yaw(s, 90f + (rng.NextDouble() < 0.5 ? 0f : 180f)), paints[rng.Next(paints.Length)]);
            Shelter(p, P(s, 12f, 37.2f), Yaw(s, 180f));
            for (int i = 0; i < 8; i++)
            {
                float x = -52f + i * 15f;
                StreetLamp(p, P(s, x, 36.6f), Yaw(s, 180f), i % 3 == 1);
            }
            // Road markings.
            for (float x = -56f; x <= 56f; x += 5f)
                Glow(p, P(s, x, 27.5f) + Vector3.up * 0.01f, new Vector3(2.2f, 0.02f, 0.18f), Mat("VR Road Paint", new Color(0.8f, 0.75f, 0.5f), new Color(0.6f, 0.55f, 0.3f), 0.4f, 0.2f), Yaw(s, 0f));
            for (int i = 0; i < 7; i++)
                Glow(p, P(s, -13f, -6f + i * 2f) + Vector3.up * 0.01f, new Vector3(2.6f, 0.02f, 0.9f), Mat("VR Crossing", new Color(0.7f, 0.7f, 0.72f), new Color(0.3f, 0.3f, 0.32f), 0.4f, 0.2f), Yaw(s, 0f));
        });

        // B: the fountain in the middle (low cover all round).
        var fountain = Group(city, "Fountain", Vector3.zero, 0f);
        Cyl(fountain, "Basin", new Vector3(0f, 0.45f, 0f), new Vector3(6.4f, 0.45f, 6.4f), concrete, true);
        Cyl(fountain, "Water", new Vector3(0f, 0.92f, 0f), new Vector3(5.8f, 0.02f, 5.8f), Mat("VR Water", new Color(0.05f, 0.3f, 0.5f), new Color(0.1f, 0.6f, 0.9f), 0.35f, 0.95f), false);
        Cyl(fountain, "Column", new Vector3(0f, 1.6f, 0f), new Vector3(0.9f, 1.6f, 0.9f), concrete, true);

        // Billboards high up on the edge buildings.
        Billboard(root, new Vector3(0f, 15f, 39.9f), 180f, "RANKED ARENA", cyanM);
        Billboard(root, new Vector3(0f, 15f, -39.9f), 0f, "NEON DISTRICT", pinkM);

        // Lights over the lanes (only a few real lights - lamps elsewhere just glow).
        Lamp(root, new Vector3(0f, 7f, 0f), new Color(0.4f, 0.9f, 1f), 24f, 1.5f);
        Lamp(root, new Vector3(-52f, 5f, 0f), new Color(0.3f, 0.55f, 1f), 16f, 1.4f);
        Lamp(root, new Vector3(52f, 5f, 0f), new Color(1f, 0.3f, 0.35f), 16f, 1.4f);
        Lamp(root, new Vector3(-22f, 6f, 0f), new Color(0.7f, 0.5f, 1f), 16f, 1.1f);
        Lamp(root, new Vector3(22f, 6f, 0f), new Color(0.7f, 0.5f, 1f), 16f, 1.1f);

        // Spawns (5 a side) and the capture points.
        for (int i = 0; i < 8; i++)
        {
            Marker($"Spawn A {i + 1}", root, new Vector3(i < 4 ? -54f : -51.5f, 0.05f, (i % 4 - 1.5f) * 3.2f), Quaternion.Euler(0f, 90f, 0f));
            Marker($"Spawn B {i + 1}", root, new Vector3(i < 4 ? 54f : 51.5f, 0.05f, (i % 4 - 1.5f) * 3.2f), Quaternion.Euler(0f, -90f, 0f));
        }
        Pad(root, new Vector3(-54f, 0.02f, 0f), Mat("VR Team Blue", new Color(0.2f, 0.5f, 1f), new Color(0.2f, 0.5f, 1f), 1.2f, 0.3f));
        Pad(root, new Vector3(54f, 0.02f, 0f), Mat("VR Team Red", new Color(1f, 0.25f, 0.3f), new Color(1f, 0.25f, 0.3f), 1.2f, 0.3f));
        Marker("Flag A", root, new Vector3(-33f, 0.02f, 0f), Quaternion.identity);
        Marker("Flag B", root, new Vector3(0f, 0.02f, 0f), Quaternion.identity);
        Marker("Flag C", root, new Vector3(33f, 0.02f, 0f), Quaternion.identity);
    }

    // ------------------------------------------------------------------ placing things

    /// <summary>Runs 'build' twice: as written (s = -1, the west half) and turned round the centre (s = +1).</summary>
    static void Both(Transform parent, System.Action<Transform, float> build)
    {
        build(parent, -1f);
        build(parent, 1f);
    }

    /// <summary>A point written for the west half, turned round the centre for the east half.</summary>
    static Vector3 P(float s, float x, float z) => s < 0f ? new Vector3(x, 0f, z) : new Vector3(-x, 0f, -z);
    static float Yaw(float s, float yaw) => s < 0f ? yaw : yaw + 180f;

    static Transform Group(Transform parent, string name, Vector3 pos, float yaw)
    {
        var t = new GameObject(name).transform;
        t.SetParent(parent, false);
        t.localPosition = pos;
        t.localRotation = Quaternion.Euler(0f, yaw, 0f);
        return t;
    }

    static GameObject Solid(Transform parent, string name, Vector3 pos, Vector3 size, Material m, float yaw = 0f)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
        go.transform.localScale = size;
        go.GetComponent<Renderer>().sharedMaterial = m;
        go.isStatic = true;
        return go;
    }

    static void Glow(Transform parent, Vector3 pos, Vector3 size, Material m, float yaw = 0f)
    {
        var go = Solid(parent, "Glow", pos, size, m, yaw);
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    static void Cyl(Transform parent, string name, Vector3 pos, Vector3 scale, Material m, bool collide, Vector3? euler = null)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        if (euler.HasValue) go.transform.localRotation = Quaternion.Euler(euler.Value);
        go.transform.localScale = scale;
        go.GetComponent<Renderer>().sharedMaterial = m;
        go.isStatic = true;
        if (!collide) Object.DestroyImmediate(go.GetComponent<Collider>());
        else
        {
            // A box hits better for cover than the cylinder's capsule.
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.AddComponent<BoxCollider>().size = new Vector3(0.9f, 2f, 0.9f);
        }
    }

    static void Invisible(Transform parent, Vector3 pos, Vector3 size)
    {
        var go = Solid(parent, "Map edge", pos, size, concrete);
        Object.DestroyImmediate(go.GetComponent<MeshRenderer>());
        Object.DestroyImmediate(go.GetComponent<MeshFilter>());
    }

    // ------------------------------------------------------------------ props

    static void Building(Transform parent, Vector3 pos, Vector3 size, float yaw, string sign)
    {
        var b = Group(parent, "Building", pos, yaw);
        var facade = facades[rng.Next(facades.Length)];
        Solid(b, "Block", new Vector3(0f, size.y * 0.5f, 0f), size, facade);
        Material[] lit = { cyanM, pinkM, violetM, amber, warm };
        var windows = lit[rng.Next(lit.Length)];
        // Rows of lit windows on the front and back.
        for (float y = 3.5f; y < size.y - 1.5f; y += 3.2f)
        {
            if (rng.NextDouble() < 0.25) continue;
            float w = size.x * (0.55f + 0.35f * (float)rng.NextDouble());
            Glow(b, new Vector3(0f, y, size.z * 0.5f + 0.03f), new Vector3(w, 0.5f, 0.04f), windows);
            Glow(b, new Vector3(0f, y, -size.z * 0.5f - 0.03f), new Vector3(w, 0.5f, 0.04f), windows);
        }
        Glow(b, new Vector3(0f, size.y + 0.1f, 0f), new Vector3(size.x + 0.2f, 0.2f, size.z + 0.2f), lit[rng.Next(3)]);
        // A shop front at street level.
        Glow(b, new Vector3(0f, 1.4f, size.z * 0.5f + 0.03f), new Vector3(size.x * 0.7f, 2.2f, 0.03f), glass);
        Glow(b, new Vector3(0f, 2.65f, size.z * 0.5f + 0.06f), new Vector3(size.x * 0.75f, 0.12f, 0.04f), windows);
        if (!string.IsNullOrEmpty(sign)) Sign(b, new Vector3(0f, Mathf.Min(size.y - 1.2f, 4.6f), size.z * 0.5f + 0.12f), sign, lit[rng.Next(lit.Length)]);
    }

    static void Sign(Transform parent, Vector3 pos, string text, Material glowMat)
    {
        var board = Group(parent, "Sign", pos, 0f);
        var t = new GameObject("Text").AddComponent<TextMeshPro>();
        t.transform.SetParent(board, false);
        t.transform.localPosition = new Vector3(0f, 0f, 0.05f);
        t.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        t.text = text;
        t.fontSize = 9f;
        t.fontStyle = FontStyles.Bold;
        t.alignment = TextAlignmentOptions.Center;
        t.color = glowMat.GetColor("_EmissionColor") * 0.6f + Color.white * 0.4f;
        t.rectTransform.sizeDelta = new Vector2(10f, 2f);
        Glow(board, new Vector3(0f, -0.75f, 0.02f), new Vector3(Mathf.Min(8f, text.Length * 0.75f), 0.08f, 0.04f), glowMat);
    }

    static void Billboard(Transform parent, Vector3 pos, float yaw, string text, Material frame)
    {
        var b = Group(parent, "Billboard", pos, yaw);
        Glow(b, Vector3.zero, new Vector3(22f, 5f, 0.3f), Mat("VR Billboard", new Color(0.04f, 0.03f, 0.08f), Color.black, 0f, 0.4f));
        Glow(b, new Vector3(0f, 2.55f, 0f), new Vector3(22.2f, 0.2f, 0.35f), frame);
        Glow(b, new Vector3(0f, -2.55f, 0f), new Vector3(22.2f, 0.2f, 0.35f), frame);
        var t = new GameObject("Text").AddComponent<TextMeshPro>();
        t.transform.SetParent(b, false);
        t.transform.localPosition = new Vector3(0f, 0f, 0.2f);
        t.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        t.text = text;
        t.fontSize = 26f;
        t.fontStyle = FontStyles.Bold;
        t.alignment = TextAlignmentOptions.Center;
        t.color = frame.GetColor("_EmissionColor") * 0.5f + Color.white * 0.5f;
        t.rectTransform.sizeDelta = new Vector2(21f, 5f);
    }

    static void Wheels(Transform v, float halfWidth, float[] zs, float radius)
    {
        foreach (float z in zs)
        for (int side = -1; side <= 1; side += 2)
            Cyl(v, "Wheel", new Vector3(side * halfWidth, radius, z), new Vector3(radius * 2f, 0.14f, radius * 2f), tire, false, new Vector3(0f, 0f, 90f));
    }

    static void Car(Transform parent, Vector3 pos, float yaw, Material paint, bool taxi = false)
    {
        var c = Group(parent, taxi ? "Taxi" : "Car", pos, yaw);
        Solid(c, "Body", new Vector3(0f, 0.65f, 0f), new Vector3(1.9f, 0.72f, 4.3f), paint);
        Solid(c, "Cabin", new Vector3(0f, 1.28f, -0.25f), new Vector3(1.7f, 0.6f, 2.3f), glass);
        Solid(c, "Roof", new Vector3(0f, 1.6f, -0.25f), new Vector3(1.62f, 0.06f, 2.1f), paint);
        Wheels(c, 0.9f, new[] { 1.35f, -1.35f }, 0.36f);
        Glow(c, new Vector3(0.6f, 0.75f, 2.16f), new Vector3(0.42f, 0.13f, 0.04f), white);
        Glow(c, new Vector3(-0.6f, 0.75f, 2.16f), new Vector3(0.42f, 0.13f, 0.04f), white);
        Glow(c, new Vector3(0.62f, 0.8f, -2.16f), new Vector3(0.4f, 0.12f, 0.04f), red);
        Glow(c, new Vector3(-0.62f, 0.8f, -2.16f), new Vector3(0.4f, 0.12f, 0.04f), red);
        if (taxi) Glow(c, new Vector3(0f, 1.72f, -0.2f), new Vector3(0.7f, 0.2f, 0.25f), amber);
    }

    static void Bus(Transform parent, Vector3 pos, float yaw, Material paint)
    {
        var b = Group(parent, "Bus (crashed)", pos, yaw);
        b.localRotation *= Quaternion.Euler(0f, 0f, 4f); // tipped a little
        Solid(b, "Body", new Vector3(0f, 1.75f, 0f), new Vector3(2.6f, 2.9f, 12f), paint);
        Glow(b, new Vector3(0f, 2.3f, 0f), new Vector3(2.64f, 0.9f, 10.8f), glass);
        Glow(b, new Vector3(0f, 2.95f, 6.02f), new Vector3(1.8f, 0.3f, 0.04f), amber);
        Glow(b, new Vector3(0.8f, 0.8f, 6.02f), new Vector3(0.4f, 0.2f, 0.04f), white);
        Glow(b, new Vector3(-0.8f, 0.8f, 6.02f), new Vector3(0.4f, 0.2f, 0.04f), white);
        Wheels(b, 1.25f, new[] { 3.8f, -3.8f }, 0.5f);
    }

    static void Truck(Transform parent, Vector3 pos, float yaw)
    {
        var t = Group(parent, "Semi truck", pos, yaw);
        Solid(t, "Cab", new Vector3(0f, 1.6f, 5.4f), new Vector3(2.5f, 2.6f, 2.6f), paints[0]);
        Glow(t, new Vector3(0f, 2.2f, 6.72f), new Vector3(2.2f, 0.8f, 0.04f), glass);
        Glow(t, new Vector3(0.9f, 0.8f, 6.72f), new Vector3(0.35f, 0.2f, 0.04f), white);
        Glow(t, new Vector3(-0.9f, 0.8f, 6.72f), new Vector3(0.35f, 0.2f, 0.04f), white);
        Solid(t, "Trailer", new Vector3(0f, 2.05f, -1f), new Vector3(2.6f, 2.9f, 9.5f), boxes[1]);
        Solid(t, "Chassis", new Vector3(0f, 0.55f, 0f), new Vector3(1.6f, 0.4f, 13f), metal);
        Wheels(t, 1.15f, new[] { 5.3f, -3.5f, -4.8f }, 0.5f);
    }

    static void Container(Transform parent, Vector3 pos, float yaw, Material m)
    {
        var c = Group(parent, "Container", pos, yaw);
        Solid(c, "Box", new Vector3(0f, 1.3f, 0f), new Vector3(2.45f, 2.6f, 6.1f), m);
        for (float z = -2.6f; z <= 2.61f; z += 0.65f)
        {
            Glow(c, new Vector3(1.24f, 1.3f, z), new Vector3(0.04f, 2.4f, 0.12f), m);
            Glow(c, new Vector3(-1.24f, 1.3f, z), new Vector3(0.04f, 2.4f, 0.12f), m);
        }
    }

    static void Barrier(Transform parent, Vector3 pos, float yaw)
    {
        var b = Group(parent, "Barrier", pos, yaw);
        Solid(b, "Base", new Vector3(0f, 0.25f, 0f), new Vector3(0.75f, 0.5f, 3.2f), concrete);
        Solid(b, "Top", new Vector3(0f, 0.75f, 0f), new Vector3(0.4f, 0.5f, 3.2f), concrete);
        Glow(b, new Vector3(0.21f, 0.82f, 0f), new Vector3(0.02f, 0.1f, 3f), amber);
        Glow(b, new Vector3(-0.21f, 0.82f, 0f), new Vector3(0.02f, 0.1f, 3f), amber);
    }

    static void Dumpster(Transform parent, Vector3 pos, float yaw)
    {
        var d = Group(parent, "Dumpster", pos, yaw);
        Solid(d, "Bin", new Vector3(0f, 0.65f, 0f), new Vector3(2f, 1.3f, 1.2f), paints[4]);
        Solid(d, "Lid", new Vector3(0f, 1.33f, -0.05f), new Vector3(2.05f, 0.08f, 1.3f), paints[3]);
    }

    static void Kiosk(Transform parent, Vector3 pos, float yaw)
    {
        var k = Group(parent, "News kiosk", pos, yaw);
        Solid(k, "Booth", new Vector3(0f, 1.25f, 0f), new Vector3(2.4f, 2.5f, 2f), paints[1]);
        Solid(k, "Roof", new Vector3(0f, 2.6f, 0.2f), new Vector3(2.8f, 0.15f, 2.6f), metal);
        Glow(k, new Vector3(0f, 1.6f, 1.01f), new Vector3(1.8f, 0.7f, 0.03f), amber);
    }

    static void Shelter(Transform parent, Vector3 pos, float yaw)
    {
        var s = Group(parent, "Bus stop", pos, yaw);
        Glow(s, new Vector3(0f, 1.3f, -0.6f), new Vector3(4f, 2.2f, 0.06f), glass);
        Solid(s, "Roof", new Vector3(0f, 2.5f, 0f), new Vector3(4.2f, 0.12f, 1.6f), metal);
        Solid(s, "Post", new Vector3(-2f, 1.25f, -0.6f), new Vector3(0.1f, 2.5f, 0.1f), metal);
        Solid(s, "Post", new Vector3(2f, 1.25f, -0.6f), new Vector3(0.1f, 2.5f, 0.1f), metal);
        Glow(s, new Vector3(1.4f, 1.3f, -0.56f), new Vector3(1f, 1.6f, 0.04f), violetM);
    }

    static void StreetLamp(Transform parent, Vector3 pos, float yaw, bool withLight)
    {
        var l = Group(parent, "Street lamp", pos, yaw);
        Solid(l, "Pole", new Vector3(0f, 3f, 0f), new Vector3(0.16f, 6f, 0.16f), metal);
        Glow(l, new Vector3(0f, 5.95f, 0.8f), new Vector3(0.12f, 0.12f, 1.6f), metal);
        Glow(l, new Vector3(0f, 5.85f, 1.5f), new Vector3(0.5f, 0.1f, 0.3f), warm);
        if (withLight) Lamp(l, new Vector3(0f, 5.5f, 1.5f), new Color(1f, 0.78f, 0.55f), 15f, 1.2f);
    }
}
