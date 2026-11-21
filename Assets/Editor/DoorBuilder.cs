using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Menu: G7l6 > Add Doors. Hangs a real door in every doorway of NewHouse, styled by where it goes:
///   - FRONT DOOR: navy, with a glass window, brass lever, deadbolt and kick plate, a house number, and a
///     welcome mat outside,
///   - PATIO / BALCONY: a white-framed glass door,
///   - GARAGE (to the mudroom): a grey steel door with a small wired window and a push plate,
///   - BATHROOMS: white, with a frosted glass strip and a little red/green "occupied" light,
///   - BEDROOMS: warm oak with raised panels (yours has a KEEP OUT sign),
///   - everything else: white panelled doors.
/// Every door sits exactly in its frame (measured from the frame itself), swings into the room on proper hinges
/// (E to open / close - see HouseDoor) and lies flat against the jamb when open. Inside doors start open,
/// bathroom and outside doors closed. The big garage door gets panels, a row of windows and a handle.
/// Running it again replaces the doors. Only doors are added - nothing else moves. Saves the scene.
/// </summary>
public static class DoorBuilder
{
    const float Storey = 3f, T = 0.045f;
    const string Folder = "Assets/Materials/House";
    static Material grooves, white, oak, navy, steel, brass, chrome, darkMetal, clearGlass, frosted, wired, red, green, sign, mat, gamerBlack, led, stickerDark;

    [MenuItem("G7l6/Add Doors")]
    public static void AddDoors()
    {
        if (EditorApplication.isPlaying) { Debug.LogWarning("[Doors] Stop Play mode first."); return; }
        var scene = SceneManager.GetActiveScene();
        if (scene.path != HouseBuilder.TargetScene)
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            scene = EditorSceneManager.OpenScene(HouseBuilder.TargetScene, OpenSceneMode.Single);
        }
        var house = scene.GetRootGameObjects().FirstOrDefault(g => g.name == "NewHouse");
        var layout = house ? house.GetComponent<HouseLayout>() : null;
        var structure = house ? house.transform.Find("Structure") : null;
        if (!layout || !structure) { Debug.LogWarning("[Doors] Couldn't find NewHouse (with its HouseLayout and Structure)."); return; }

        Materials();
        var root = house.transform;
        Vector3 scale = root.localScale;
        root.localScale = Vector3.one; // work in plan metres
        var temp = new List<Component>();
        var notes = new List<string>();
        int made = 0;
        try
        {
            var old = structure.Find("Doors");
            if (old) Undo.DestroyObjectImmediate(old.gameObject);
            // Door frames often have no collider: give them temporary ones so the doorway can be measured exactly.
            foreach (var r in house.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (!InFrame(r.transform) || r.GetComponent<Collider>()) continue;
                var mf = r.GetComponent<MeshFilter>();
                if (!mf || !mf.sharedMesh) continue;
                temp.Add(r.gameObject.AddComponent<MeshCollider>());
            }
            Physics.SyncTransforms();

            hung.Clear();
            // Every door frame's outline (each frame object as a whole).
            frames = new List<Bounds>();
            foreach (var t in house.GetComponentsInChildren<Transform>(true))
            {
                if (!t.name.StartsWith("Door Frame")) continue;
                var rs = t.GetComponentsInChildren<Renderer>();
                if (rs.Length == 0) continue;
                var fb = rs[0].bounds;
                foreach (var r in rs) fb.Encapsulate(r.bounds);
                frames.Add(fb);
            }
            var parent = new GameObject("Doors").transform;
            parent.SetParent(structure, false);
            Undo.RegisterCreatedObjectUndo(parent.gameObject, "Doors");
            var archways = Archways(layout);
            foreach (var w in layout.walls)
            {
                if (w.type != HouseLayout.WallType.Door) continue;
                if (archways.TryGetValue(w, out string why)) { notes.Add(why); continue; }
                string note = Door(parent, root, layout, w);
                if (note != null && note.StartsWith("no door")) { notes.Add(note); continue; }
                if (note != null) { made++; notes.Add(note); }
            }
            notes.Add(GarageDoor(structure));
        }
        finally
        {
            foreach (var c in temp) if (c) Object.DestroyImmediate(c);
            root.localScale = scale;
            Physics.SyncTransforms();
        }
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log($"[Doors] Hung {made} doors:\n - " + string.Join("\n - ", notes) + "\nNothing else changed. The scene is saved.");
    }

    /// <summary>
    /// Doorways too close together (two openings side by side in a corner) can't both have doors - they'd swing
    /// into each other's way. Of each such pair the less important one stays an open archway: doors into
    /// bathrooms, to the outside and from halls / landings come first.
    /// </summary>
    static Dictionary<HouseLayout.WallInfo, string> Archways(HouseLayout layout)
    {
        var result = new Dictionary<HouseLayout.WallInfo, string>();
        var ways = layout.walls.Where(w => w.type == HouseLayout.WallType.Door).ToList();
        int Rank(HouseLayout.WallInfo w)
        {
            Vector2 mid = (w.a + w.b) * 0.5f, n = new Vector2((w.b - w.a).normalized.y, -(w.b - w.a).normalized.x);
            var p = RoomAt(layout, w.floor, mid + n * 0.6f);
            var q = RoomAt(layout, w.floor, mid - n * 0.6f);
            int a = Privacy(p), b = Privacy(q);
            if (a == 0 || b == 0 || a == 6 || b == 6) return 3;
            if (a == 2 || b == 2) return 2;
            return 1;
        }
        string Between(HouseLayout.WallInfo w)
        {
            Vector2 mid = (w.a + w.b) * 0.5f, n = new Vector2((w.b - w.a).normalized.y, -(w.b - w.a).normalized.x);
            return $"{Name(RoomAt(layout, w.floor, mid - n * 0.6f))} / {Name(RoomAt(layout, w.floor, mid + n * 0.6f))}";
        }
        for (int i = 0; i < ways.Count; i++)
        for (int j = i + 1; j < ways.Count; j++)
        {
            var a = ways[i];
            var b = ways[j];
            if (a.floor != b.floor || result.ContainsKey(a) || result.ContainsKey(b)) continue;
            if (Vector2.Distance((a.a + a.b) * 0.5f, (b.a + b.b) * 0.5f) > 2.6f) continue;
            var drop = Rank(a) >= Rank(b) ? b : a;
            var keep = drop == a ? b : a;
            result[drop] = $"no door between {Between(drop)}: right next to the {Between(keep)} door - left as an open archway";
        }
        return result;
    }

    static bool InFrame(Transform t)
    {
        for (; t; t = t.parent) if (t.name.StartsWith("Door Frame")) return true;
        return false;
    }

    enum Style { Front, Patio, Garage, Bath, Bedroom, Yours, Plain }

    // ------------------------------------------------------------------ one doorway

    /// <summary>A door that's already hung (for keeping the next ones out of its way).</summary>
    class Hung { public int floor; public Vector3 hinge, latch, swing; public float width; }
    static readonly List<Hung> hung = new List<Hung>();

    static string Door(Transform parent, Transform root, HouseLayout layout, HouseLayout.WallInfo w)
    {
        Vector2 a = w.a, b = w.b, mid2 = (a + b) * 0.5f;
        Vector2 along2 = (b - a).normalized;
        Vector2 normal2 = new Vector2(along2.y, -along2.x);
        var roomP = RoomAt(layout, w.floor, mid2 + normal2 * 0.6f);
        var roomN = RoomAt(layout, w.floor, mid2 - normal2 * 0.6f);
        Vector3 along = root.TransformDirection(new Vector3(along2.x, 0f, along2.y)).normalized;
        Vector3 towardP = root.TransformDirection(new Vector3(normal2.x, 0f, normal2.y)).normalized;
        float floorY = root.TransformPoint(new Vector3(0f, w.floor * Storey, 0f)).y;
        Vector3 c = root.TransformPoint(new Vector3(mid2.x, w.floor * Storey, mid2.y));

        // Measure the opening: sideways to the jambs, up to the head, and the wall's two faces.
        Vector3 probe = c + Vector3.up * 1.0f;
        float left = Mathf.Clamp(Cast(probe, -along, 1.3f, 0.45f), 0.3f, 0.8f), right = Mathf.Clamp(Cast(probe, along, 1.3f, 0.45f), 0.3f, 0.8f);
        Vector3 open = c + along * (right - left) * 0.5f;
        open.y = floorY;
        float width = left + right;
        float height = Mathf.Clamp(Cast(open + Vector3.up * 0.5f, Vector3.up, 3f, 1.6f) + 0.5f, 1.9f, 2.6f);
        Vector3 beside = open + along * (width * 0.5f + 0.45f) + Vector3.up * 1.2f;
        float faceP = Cast(beside + towardP * 0.6f, -towardP, 1.2f, 0.5f), faceN = Cast(beside - towardP * 0.6f, towardP, 1.2f, 0.5f);
        float midP = (faceN - faceP) * 0.5f; // the wall's middle plane, measured towards room P
        // Better: the door frame sits centred on the wall - use it (rays at the wall can hit furniture next to it).
        if (FrameCentre(open, out Vector3 frame)) midP = Mathf.Clamp(Vector3.Dot(frame - open, towardP), -0.15f, 0.15f);
        else midP = Mathf.Clamp(midP, -0.15f, 0.15f);

        // Which way it swings (into the more private room, and into the house) and which side the hinges go -
        // the first arrangement that doesn't hit another door, a wall or furniture. None works: leave it open.
        bool preferP = Privacy(roomP) >= Privacy(roomN);
        var options = new[] { (preferP, false), (preferP, true), (!preferP, false), (!preferP, true) };
        bool found = false, intoP = preferP, flip = false;
        Vector3 swing = towardP, hinge = open, latch = along;
        foreach (var (p, f) in options)
        {
            Vector3 s = towardP * (p ? 1f : -1f);
            Vector3 rightDir = Quaternion.LookRotation(s, Vector3.up) * Vector3.right;
            Vector3 l = f ? -rightDir : rightDir;
            Vector3 h = open - l * (width * 0.5f) + s * ((p ? midP : -midP) + T * 0.5f);
            if (!Clear(w.floor, h, l, s, width)) continue;
            found = true; intoP = p; flip = f; swing = s; hinge = h; latch = l;
            break;
        }
        var into = intoP ? roomP : roomN;
        var other = intoP ? roomN : roomP;
        if (!found) return $"no door between {Name(roomN)} and {Name(roomP)}: no room for one to swing - left as an open doorway";
        hung.Add(new Hung { floor = w.floor, hinge = hinge, latch = latch, swing = swing, width = width });

        Style style = StyleOf(into, other);
        float W = width - 0.012f, H = height - 0.012f;

        // The door: pivot on the hinge line, its +z into the room it swings into; the leaf runs along 'latch'.
        var door = new GameObject($"Door - {Name(other)} / {Name(into)}");
        door.transform.SetParent(parent, true);
        door.transform.rotation = Quaternion.LookRotation(swing, Vector3.up);
        door.transform.position = hinge;
        var leaf = new GameObject("Leaf").transform;
        leaf.SetParent(door.transform, false);
        Build(leaf, W, H, style, out Renderer occupied);
        float side = flip ? -1f : 1f;
        if (flip)
            foreach (Transform t in leaf)
            {
                var lp = t.localPosition;
                lp.x = -lp.x;
                if (t.name == "Door model")
                {
                    // The model itself is turned round (not just moved), so it still fills the frame from the hinge.
                    t.localRotation = Quaternion.Euler(0f, 180f, 0f) * t.localRotation;
                    lp.z = -T - lp.z; // (only used for imported door models)
                }
                t.localPosition = lp;
            }

        var col = leaf.gameObject.AddComponent<BoxCollider>();
        col.center = new Vector3(side * (0.006f + W * 0.5f), 0.008f + H * 0.5f, -T * 0.5f);
        col.size = new Vector3(W, H, T + 0.01f);
        var hd = door.AddComponent<HouseDoor>();
        hd.leaf = leaf;
        hd.side = side;
        hd.openAngle = flip ? 90f : -90f;
        hd.startOpen = style == Style.Bedroom || style == Style.Yours || style == Style.Plain || style == Style.Garage;
        hd.label = style switch
        {
            Style.Front => "front door", Style.Patio => "patio door", Style.Garage => "garage door", Style.Bath => "bathroom door",
            Style.Yours => "door to your room", _ => "door",
        };
        hd.Width = width;
        hd.Height = height;
        if (occupied) { hd.occupiedLight = occupied; hd.occupiedOn = red; hd.occupiedOff = green; }
        var o = door.AddComponent<global::Outline>();
        o.OutlineMode = global::Outline.Mode.OutlineVisible;
        o.OutlineColor = Color.white;
        o.OutlineWidth = 5f;
        o.enabled = false;

        if (style == Style.Front) Welcome(door.transform, width * side);
        return $"{style} door, {Name(other)} -> {Name(into)} ({width:0.00} x {height:0.00} m{(flip ? ", hinged the other side" : "")}{(intoP != preferP ? ", swings the other way" : "")})";
    }

    /// <summary>
    /// Can a door hinged at 'hinge' (leaf running along 'latch' when shut) swing open along 'swing' without
    /// hitting a wall, furniture, or the swing of a door already hung?
    /// </summary>
    static bool Clear(int floor, Vector3 hinge, Vector3 latch, Vector3 swing, float width)
    {
        // Walls and furniture in the way of the swing (three rays across the quarter circle).
        Vector3 from = hinge + latch * 0.06f + swing * 0.02f + Vector3.up * 1.0f;
        foreach (float deg in new[] { 0f, 30f, 60f })
        {
            float r = deg * Mathf.Deg2Rad;
            Vector3 dir = (swing * Mathf.Cos(r) + latch * Mathf.Sin(r)).normalized;
            if (Cast(from, dir, width, 99f) < width - 0.04f) return false;
            if (Cast(from + Vector3.down * 0.7f, dir, width, 99f) < width - 0.04f) return false; // low furniture
        }
        // Other doors' swings.
        foreach (var d in hung)
        {
            if (d.floor != floor) continue;
            Vector3 mine = hinge + (swing + latch) * width * 0.35f, theirs = d.hinge + (d.swing + d.latch) * d.width * 0.35f;
            if (Vector3.Distance(mine, theirs) > width + d.width + 0.3f) continue;
            if (Cast(mine + Vector3.up * 1f, theirs - mine, Vector3.Distance(mine, theirs), 99f) < Vector3.Distance(mine, theirs) - 0.05f) continue; // a wall between
            for (int i = 1; i <= 4; i++)
            for (int k = 0; k <= 6; k++)
            {
                float rr = width * i / 4f, ang = k * 15f * Mathf.Deg2Rad;
                Vector3 p = hinge + (swing * Mathf.Cos(ang) + latch * Mathf.Sin(ang)) * rr;
                if (InSwing(p, d.hinge, d.latch, d.swing, d.width)) return false;
                float rr2 = d.width * i / 4f;
                Vector3 q = d.hinge + (d.swing * Mathf.Cos(ang) + d.latch * Mathf.Sin(ang)) * rr2;
                if (InSwing(q, hinge, latch, swing, width)) return false;
            }
        }
        return true;
    }

    static bool InSwing(Vector3 p, Vector3 hinge, Vector3 latch, Vector3 swing, float width)
    {
        Vector3 v = p - hinge;
        v.y = 0f;
        return v.magnitude <= width + 0.08f && Vector3.Dot(v, swing) >= -0.05f && Vector3.Dot(v, latch) >= -0.05f;
    }

    static List<Bounds> frames;

    /// <summary>The middle of the door frame model at this doorway (the frame is centred on the wall).</summary>
    static bool FrameCentre(Vector3 open, out Vector3 centre)
    {
        centre = default;
        float best = 1.0f;
        foreach (var b in frames)
        {
            float d = Vector2.Distance(new Vector2(b.center.x, b.center.z), new Vector2(open.x, open.z));
            if (d < best && Mathf.Abs(b.min.y - open.y) < 0.6f) { best = d; centre = b.center; }
        }
        return best < 1.0f;
    }

    static float Cast(Vector3 from, Vector3 dir, float max, float fallback)
    {
        foreach (var hit in Physics.RaycastAll(from, dir, max, ~0, QueryTriggerInteraction.Ignore).OrderBy(h => h.distance))
        {
            if (hit.collider.GetComponentInParent<HouseDoor>() || hit.collider.GetComponentInParent<Carryable>()
                || hit.collider.GetComponentInParent<CatBrain>() || hit.collider.GetComponentInParent<PlayerMovementAdvanced>()) continue;
            return hit.distance;
        }
        return fallback;
    }

    static HouseLayout.RoomInfo RoomAt(HouseLayout layout, int floor, Vector2 p) =>
        layout.rooms.FirstOrDefault(r => r.floor == floor && r.rect.Contains(p));

    static string Name(HouseLayout.RoomInfo r) => r == null ? "Outside" : r.name;

    static int Privacy(HouseLayout.RoomInfo r)
    {
        if (r == null || r.kind == "Outdoor") return 0;
        string n = r.name.ToLowerInvariant();
        if (r.kind == "Bath" || n.Contains("bath") || n.Contains("wc") || n.Contains("powder")) return 6;
        if (r.kind == "Bedroom") return 5;
        if (r.kind == "Garage") return 1;
        if (r.kind == "Hall" || r.kind == "Stairs" || n.Contains("landing") || n.Contains("foyer") || n.Contains("hall") || n.Contains("corridor")) return 2;
        return 4;
    }

    static Style StyleOf(HouseLayout.RoomInfo into, HouseLayout.RoomInfo other)
    {
        bool outside = other == null || other.kind == "Outdoor";
        if (outside) return into != null && into.name == "Foyer" ? Style.Front : Style.Patio;
        if (into != null && into.kind == "Garage" || other.kind == "Garage") return Style.Garage;
        int p = Privacy(into);
        if (p == 6) return Style.Bath;
        if (into != null && into.name == HouseBuilder.PlayerRoomName) return Style.Yours;
        if (p == 5) return Style.Bedroom;
        return Style.Plain;
    }

    // ------------------------------------------------------------------ the door itself (leaf space: x 0..W from the hinge, y up, z -T..0)

    static void Build(Transform leaf, float W, float H, Style style, out Renderer occupied)
    {
        occupied = null;
        float x0 = 0.006f, y0 = 0.008f;
        Material body = style switch { Style.Front => navy, Style.Garage => steel, Style.Bedroom => oak, _ => white };

        switch (style)
        {
            case Style.Front:
            {
                // Glass in the top third (with glazing bars), two raised panels below, a brass kick plate.
                Rect win = new Rect(0.16f, H - 0.78f, W - 0.32f, 0.58f);
                SlabWithHole(leaf, W, H, body, win);
                Glass(leaf, win, clearGlass);
                Part(leaf, new Vector3(win.center.x, win.center.y, 0f), new Vector3(0.035f, win.height, T), body);
                Part(leaf, new Vector3(win.center.x, win.center.y, 0f), new Vector3(win.width, 0.035f, T), body);
                FacePlates(leaf, new Vector3(W * 0.5f, 0.12f, 0f), new Vector2(W - 0.06f, 0.18f), brass);
                Lever(leaf, W, brass, true);
                Number(leaf, W, H, "12");
                break;
            }
            case Style.Patio:
            {
                Rect win = new Rect(0.09f, 0.2f, W - 0.18f, H - 0.3f);
                SlabWithHole(leaf, W, H, white, win);
                Glass(leaf, win, clearGlass);
                Part(leaf, new Vector3(W * 0.5f, 1.0f, 0f), new Vector3(W - 0.18f, 0.05f, T), white); // the bar across
                Lever(leaf, W, chrome, false);
                break;
            }
            case Style.Garage:
            {
                Rect win = new Rect(W * 0.5f - 0.13f, H - 0.62f, 0.26f, 0.36f);
                SlabWithHole(leaf, W, H, steel, win);
                Glass(leaf, win, wired);
                FacePlates(leaf, new Vector3(W * 0.5f, 0.13f, 0f), new Vector2(W - 0.08f, 0.22f), darkMetal);
                FacePlates(leaf, new Vector3(W - 0.14f, 1.25f, 0f), new Vector2(0.1f, 0.3f), chrome); // push plate
                Lever(leaf, W, darkMetal, false);
                break;
            }
            case Style.Bath:
            {
                Rect strip = new Rect(W * 0.5f - 0.1f, 0.55f, 0.2f, H - 0.85f);
                SlabWithHole(leaf, W, H, white, strip);
                Glass(leaf, strip, frosted);
                Lever(leaf, W, chrome, false);
                // The occupied light, on the hall side only.
                occupied = Part(leaf, new Vector3(W - 0.07f, 1.13f, -T - 0.0015f), new Vector3(0.035f, 0.022f, 0.003f), red, false).GetComponent<Renderer>();
                break;
            }
            case Style.Yours:
                GamerDoor(leaf, W, H);
                break;
            default:
            {
                Part(leaf, new Vector3(W * 0.5f, H * 0.5f, -T * 0.5f), new Vector3(W, H, T), body, false);
                if (style == Style.Bedroom)
                    foreach (float f in new[] { 0.22f, 0.4f, 0.58f, 0.76f })
                        FacePlates(leaf, new Vector3(W * 0.5f, H * f, 0f), new Vector2(W - 0.18f, 0.01f), grooves);
                Lever(leaf, W, style == Style.Plain ? chrome : darkMetal, false);
                break;
            }
        }
        Hinges(leaf, H);
        // Everything above was laid out from the bottom-left corner of the doorway: shift it into the gap.
        foreach (Transform t in leaf) t.localPosition += new Vector3(x0, y0, 0f);
    }

    /// <summary>A part centred at (x, y) of the door's thickness (z = -T/2), or exactly at 'pos' if centred = false.</summary>
    static GameObject Part(Transform leaf, Vector3 pos, Vector3 size, Material m, bool onFace = true)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.name = m ? m.name : "Part";
        go.transform.SetParent(leaf, false);
        go.transform.localPosition = onFace ? new Vector3(pos.x, pos.y, -T * 0.5f) : pos;
        go.transform.localScale = size;
        go.GetComponent<Renderer>().sharedMaterial = m;
        return go;
    }

    /// <summary>A thin plate on both faces of the door (kick plates, push plates, the occupied light). Returns one of them.</summary>
    static Renderer FacePlates(Transform leaf, Vector3 pos, Vector2 size, Material m)
    {
        Renderer first = null;
        foreach (float z in new[] { 0.0015f, -T - 0.0015f })
        {
            var p = Part(leaf, new Vector3(pos.x, pos.y, z), new Vector3(size.x, size.y, 0.003f), m, false);
            if (!first) first = p.GetComponent<Renderer>();
        }
        return first;
    }

    /// <summary>The door slab around a window or strip (four pieces, so the glass is see-through).</summary>
    static void SlabWithHole(Transform leaf, float W, float H, Material m, Rect hole)
    {
        Part(leaf, new Vector3(hole.xMin * 0.5f, H * 0.5f, 0f), new Vector3(hole.xMin, H, T), m);
        Part(leaf, new Vector3((hole.xMax + W) * 0.5f, H * 0.5f, 0f), new Vector3(W - hole.xMax, H, T), m);
        Part(leaf, new Vector3(hole.center.x, hole.yMin * 0.5f, 0f), new Vector3(hole.width, hole.yMin, T), m);
        Part(leaf, new Vector3(hole.center.x, (hole.yMax + H) * 0.5f, 0f), new Vector3(hole.width, H - hole.yMax, T), m);
    }

    static void Glass(Transform leaf, Rect hole, Material m)
    {
        var g = Part(leaf, new Vector3(hole.center.x, hole.center.y, 0f), new Vector3(hole.width + 0.01f, hole.height + 0.01f, 0.012f), m);
        g.name = "Glass";
        g.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    /// <summary>Raised panels (proud of both faces), 'columns' across, between heights y0 and y1.</summary>
    static void RaisedPanels(Transform leaf, float W, float y0, float y1, int columns, Material m)
    {
        const float stile = 0.12f, gap = 0.1f;
        float pw = (W - stile * 2f - gap * (columns - 1)) / columns;
        for (int i = 0; i < columns; i++)
        {
            float cx = stile + pw * 0.5f + i * (pw + gap);
            Part(leaf, new Vector3(cx, (y0 + y1) * 0.5f, 0f), new Vector3(pw, y1 - y0, T + 0.014f), m).name = "Panel";
            // A thin moulding round each panel.
            Part(leaf, new Vector3(cx, (y0 + y1) * 0.5f, 0f), new Vector3(pw + 0.03f, y1 - y0 + 0.03f, T + 0.006f), m).name = "Moulding";
        }
    }

    /// <summary>Lever handles on both sides, a rose through the door (and a deadbolt for outside doors).</summary>
    static void Lever(Transform leaf, float W, Material m, bool deadbolt)
    {
        float x = W - 0.075f, y = 1.0f;
        var rose = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Object.DestroyImmediate(rose.GetComponent<Collider>());
        rose.name = "Rose";
        rose.transform.SetParent(leaf, false);
        rose.transform.localPosition = new Vector3(x, y, -T * 0.5f);
        rose.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        rose.transform.localScale = new Vector3(0.06f, T * 0.5f + 0.012f, 0.06f);
        rose.GetComponent<Renderer>().sharedMaterial = m;
        foreach (float side in new[] { 1f, -1f })
        {
            float z = side > 0 ? 0.03f : -T - 0.03f;
            Part(leaf, new Vector3(x - 0.055f, y, z), new Vector3(0.13f, 0.02f, 0.02f), m, false).name = "Lever";
            Part(leaf, new Vector3(x, y, side > 0 ? 0.016f : -T - 0.016f), new Vector3(0.018f, 0.018f, 0.03f), m, false).name = "Lever neck";
        }
        if (deadbolt)
        {
            var bolt = Object.Instantiate(rose, leaf);
            bolt.name = "Deadbolt";
            bolt.transform.localPosition = new Vector3(x, y + 0.2f, -T * 0.5f);
            bolt.transform.localScale = new Vector3(0.05f, T * 0.5f + 0.01f, 0.05f);
        }
    }

    static void Hinges(Transform leaf, float H)
    {
        foreach (float y in new[] { 0.22f, H * 0.5f, H - 0.22f })
            Part(leaf, new Vector3(-0.002f, y, -T * 0.5f), new Vector3(0.012f, 0.1f, T * 0.6f), darkMetal, false).name = "Hinge";
    }

    /// <summary>
    /// Your door: flush matte black, a neon GAMER ZONE
    /// sign, a slap of stickers, a controller sticker and a DO NOT DISTURB hanger on the handle.
    /// Every layer sits a little further out than the one under it, so nothing flickers.
    /// </summary>
    static void GamerDoor(Transform leaf, float W, float H)
    {
        Part(leaf, new Vector3(W * 0.5f, H * 0.5f, -T * 0.5f), new Vector3(W, H, T), gamerBlack, false).name = "Door";
        const float face = -T; // the hall side
        const float layer = 0.0025f;

        // GAMER ZONE in neon at the top.
        var plate = Part(leaf, new Vector3(W * 0.5f, H - 0.2f, face - layer * 0.5f), new Vector3(W - 0.18f, 0.16f, layer), stickerDark, false);
        plate.name = "Neon plate";
        var title = Label(leaf, "GAMER ZONE", new Vector3(W * 0.5f, H - 0.2f, face - layer * 1.6f), 0.55f, new Color(0.3f, 1f, 0.95f), W - 0.2f, 0f);
        title.fontStyle = FontStyles.Bold | FontStyles.Italic;

        // Stickers: (text, background, x, y, w, h, tilt)
        var stickers = new (string text, Color bg, Color fg, float x, float y, float w, float h, float tilt)[]
        {
            ("GG", new Color(0.2f, 0.85f, 1f), Color.black, 0.2f, 1.55f, 0.14f, 0.1f, -8f),
            ("NO NOOBS\nALLOWED", new Color(1f, 0.25f, 0.3f), Color.white, 0.62f, 1.62f, 0.24f, 0.13f, 6f),
            ("AFK", new Color(1f, 0.85f, 0.2f), Color.black, 0.18f, 1.2f, 0.12f, 0.08f, 10f),
            ("1v1 ME", new Color(0.62f, 0.35f, 1f), Color.white, 0.58f, 1.3f, 0.18f, 0.08f, -5f),
            ("LVL 99", new Color(0.25f, 0.95f, 0.45f), Color.black, 0.24f, 0.62f, 0.15f, 0.08f, -12f),
            ("PRESS F", new Color(1f, 0.55f, 0.15f), Color.black, 0.6f, 0.52f, 0.17f, 0.08f, 7f),
            ("RANKED\nGRINDER", new Color(1f, 0.35f, 0.8f), Color.white, 0.44f, 0.86f, 0.2f, 0.12f, -3f),
        };
        for (int i = 0; i < stickers.Length; i++)
        {
            var s = stickers[i];
            float x = s.x * W / 0.9f, y = s.y * H / 2.05f;
            float lift = i * 0.0007f; // each sticker on its own layer, so overlapping corners never flicker
            var p = Part(leaf, new Vector3(x, y, face - layer * 0.5f - lift), new Vector3(s.w, s.h, layer), Sticker(s.bg), false);
            p.name = "Sticker";
            p.transform.localRotation = Quaternion.Euler(0f, 0f, s.tilt);
            var t = Label(leaf, s.text, new Vector3(x, y, face - layer * 1.6f - lift), 0.32f, s.fg, s.w - 0.02f, s.tilt);
            t.fontStyle = FontStyles.Bold;
        }

        // A controller sticker.
        float cx = 0.42f * W / 0.9f, cy = 1.95f * H / 2.05f - 0.45f;
        var pad = Part(leaf, new Vector3(cx, cy, face - layer * 0.5f), new Vector3(0.2f, 0.09f, layer), Sticker(new Color(0.85f, 0.87f, 0.9f)), false);
        pad.name = "Controller";
        foreach (float gx in new[] { -0.085f, 0.085f })
        {
            var grip = Part(leaf, new Vector3(cx + gx, cy - 0.035f, face - layer * 0.5f), new Vector3(0.06f, 0.08f, layer), Sticker(new Color(0.85f, 0.87f, 0.9f)), false);
            grip.transform.localRotation = Quaternion.Euler(0f, 0f, gx > 0 ? 25f : -25f);
        }
        Part(leaf, new Vector3(cx - 0.05f, cy + 0.005f, face - layer * 1.5f), new Vector3(0.035f, 0.012f, layer), stickerDark, false);
        Part(leaf, new Vector3(cx - 0.05f, cy + 0.005f, face - layer * 1.5f), new Vector3(0.012f, 0.035f, layer), stickerDark, false);
        Color[] buttons = { new Color(0.2f, 0.9f, 0.4f), new Color(1f, 0.3f, 0.3f), new Color(0.3f, 0.6f, 1f), new Color(1f, 0.85f, 0.2f) };
        Vector2[] at = { new Vector2(0.05f, -0.012f), new Vector2(0.065f, 0.004f), new Vector2(0.035f, 0.004f), new Vector2(0.05f, 0.02f) };
        for (int i = 0; i < 4; i++)
            Part(leaf, new Vector3(cx + at[i].x, cy + at[i].y, face - layer * 1.5f), new Vector3(0.012f, 0.012f, layer), Sticker(buttons[i]), false);

        // DO NOT DISTURB hanger on the handle.
        Lever(leaf, W, darkMetal, false);
        var hanger = Part(leaf, new Vector3(W - 0.075f, 0.82f, face - 0.06f), new Vector3(0.1f, 0.22f, 0.003f), Sticker(new Color(0.95f, 0.15f, 0.25f)), false);
        hanger.name = "Do not disturb";
        hanger.transform.localRotation = Quaternion.Euler(4f, 0f, 3f);
        Label(leaf, "DO NOT\nDISTURB\n<size=60%>ranked\nin progress</size>", new Vector3(W - 0.075f, 0.82f, face - 0.0625f), 0.3f, Color.white, 0.09f, 3f).fontStyle = FontStyles.Bold;

    }

    static TextMeshPro Label(Transform leaf, string text, Vector3 pos, float size, Color color, float width, float tilt)
    {
        var t = new GameObject("Text").AddComponent<TextMeshPro>();
        t.transform.SetParent(leaf, false);
        t.transform.localPosition = pos;
        t.transform.localRotation = Quaternion.Euler(0f, 0f, tilt);
        t.text = text;
        t.fontSize = size;
        t.alignment = TextAlignmentOptions.Center;
        t.enableWordWrapping = false;
        t.color = color;
        t.rectTransform.sizeDelta = new Vector2(width, 0.3f);
        return t;
    }

    static readonly Dictionary<Color, Material> stickers = new Dictionary<Color, Material>();
    static Material Sticker(Color c)
    {
        if (stickers.TryGetValue(c, out var m) && m) return m;
        m = Mat($"Door Sticker {ColorUtility.ToHtmlStringRGB(c)}", c, 0.5f);
        stickers[c] = m;
        return m;
    }

    /// <summary>The house number on the outside of the front door.</summary>
    static void Number(Transform leaf, float W, float H, string n)
    {
        var t = new GameObject("House number").AddComponent<TextMeshPro>();
        t.transform.SetParent(leaf, false);
        t.transform.localPosition = new Vector3(W * 0.5f, H - 0.1f, -T - 0.004f);
        t.text = n;
        t.fontSize = 1.1f;
        t.fontStyle = FontStyles.Bold;
        t.alignment = TextAlignmentOptions.Center;
        t.color = new Color(0.95f, 0.78f, 0.4f);
        t.rectTransform.sizeDelta = new Vector2(0.4f, 0.16f);
    }

    /// <summary>A hand-made KEEP OUT sign on the hall side of your door.</summary>
    static void KeepOut(Transform leaf, float W)
    {
        var board = Part(leaf, new Vector3(W * 0.5f, 1.45f, -T - 0.004f), new Vector3(0.34f, 0.22f, 0.006f), sign, false);
        board.name = "Keep out sign";
        board.transform.localRotation = Quaternion.Euler(0f, 0f, -3f);
        var t = new GameObject("Text").AddComponent<TextMeshPro>();
        t.transform.SetParent(board.transform.parent, false);
        t.transform.localPosition = new Vector3(W * 0.5f, 1.45f, -T - 0.009f);
        t.transform.localRotation = Quaternion.Euler(0f, 0f, -3f);
        t.text = "KEEP OUT!\n<size=55%>(this means YOU, cat)</size>";
        t.fontSize = 0.75f;
        t.fontStyle = FontStyles.Bold;
        t.alignment = TextAlignmentOptions.Center;
        t.color = new Color(0.8f, 0.1f, 0.1f);
        t.rectTransform.sizeDelta = new Vector2(0.32f, 0.2f);
    }

    /// <summary>A WELCOME mat outside the front door (on whatever's there).</summary>
    static void Welcome(Transform door, float width)
    {
        Vector3 at = door.TransformPoint(new Vector3(width * 0.5f, 0.5f, -0.62f));
        float y = Physics.Raycast(at, Vector3.down, out var hit, 2f, ~0, QueryTriggerInteraction.Ignore) ? hit.point.y : door.position.y;
        var m = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Object.DestroyImmediate(m.GetComponent<Collider>());
        m.name = "Welcome mat";
        m.transform.SetParent(door, true);
        m.transform.position = new Vector3(at.x, y + 0.008f, at.z);
        m.transform.rotation = door.rotation;
        m.transform.localScale = new Vector3(1.0f, 0.014f, 0.6f);
        m.GetComponent<Renderer>().sharedMaterial = mat;
        var t = new GameObject("Text").AddComponent<TextMeshPro>();
        t.transform.SetParent(m.transform.parent, false);
        t.transform.position = new Vector3(at.x, y + 0.017f, at.z);
        t.transform.rotation = door.rotation * Quaternion.Euler(90f, 180f, 0f);
        t.text = "WELCOME";
        t.fontSize = 1.4f;
        t.fontStyle = FontStyles.Bold;
        t.alignment = TextAlignmentOptions.Center;
        t.color = new Color(0.25f, 0.16f, 0.08f);
        t.rectTransform.sizeDelta = new Vector2(0.9f, 0.3f);
    }

    // ------------------------------------------------------------------ the big garage door

    static string GarageDoor(Transform structure)
    {
        var door = structure.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "Garage Door");
        var hinge = door ? door.Find("Hinge") : null;
        var panel = hinge ? hinge.Find("Panel") : null;
        if (!panel) return "garage door: not found (left as it was)";
        var old = hinge.Find("Garage trim");
        if (old) Undo.DestroyObjectImmediate(old.gameObject);
        var g = new GameObject("Garage trim").transform;
        g.SetParent(hinge, false);
        Vector3 size = panel.localScale;          // 3.96 x 2.5 x 0.06 (panel space)
        float w = size.x, h = size.y, d = size.z;
        var section = Mat("Garage Section", new Color(0.88f, 0.89f, 0.9f), 0.35f);
        int rows = 5; // 0.5 m rows: the gaps line up with the grooves already on the door
        float rowH = h / rows;
        for (int r = 0; r < rows; r++)
        {
            float yc = -h * 0.5f + rowH * (r + 0.5f);
            for (int i = 0; i < 4; i++)
            {
                float xc = -w * 0.5f + w / 4f * (i + 0.5f);
                bool window = r == rows - 1;
                var p = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Object.DestroyImmediate(p.GetComponent<Collider>());
                p.name = window ? "Window" : "Section";
                p.transform.SetParent(g, false);
                p.transform.position = panel.TransformPoint(new Vector3(xc / w, yc / h, 0f));
                p.transform.rotation = panel.rotation;
                p.transform.localScale = window ? new Vector3(w / 4f - 0.22f, rowH - 0.2f, d + 0.02f) : new Vector3(w / 4f - 0.14f, rowH - 0.14f, d + 0.016f);
                p.GetComponent<Renderer>().sharedMaterial = window ? clearGlass : section;
            }
        }
        var handle = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Object.DestroyImmediate(handle.GetComponent<Collider>());
        handle.name = "Handle";
        handle.transform.SetParent(g, false);
        handle.transform.position = panel.TransformPoint(new Vector3(0f, (-h * 0.5f + 0.25f) / h, 0f));
        handle.transform.rotation = panel.rotation;
        handle.transform.localScale = new Vector3(0.3f, 0.05f, d + 0.06f);
        handle.GetComponent<Renderer>().sharedMaterial = darkMetal;
        return "garage door: raised sections, a row of windows and a handle";
    }

    // ------------------------------------------------------------------ materials

    static void Materials()
    {
        white = Mat("Door White", new Color(0.95f, 0.94f, 0.92f), 0.35f);
        oak = Mat("Door Walnut", new Color(0.4f, 0.26f, 0.16f), 0.45f);
        oak.mainTexture = null;
        grooves = Mat("Door Groove", new Color(0.2f, 0.12f, 0.07f), 0.2f);
        navy = Mat("Door Navy", new Color(0.11f, 0.17f, 0.3f), 0.55f);
        steel = Mat("Door Steel", new Color(0.55f, 0.57f, 0.6f), 0.45f);
        brass = Mat("Door Brass", new Color(0.85f, 0.65f, 0.3f), 0.8f);
        brass.SetFloat("_Metallic", 0.85f);
        chrome = Mat("Door Chrome", new Color(0.8f, 0.82f, 0.85f), 0.85f);
        chrome.SetFloat("_Metallic", 0.9f);
        darkMetal = Mat("Door Black Metal", new Color(0.12f, 0.12f, 0.13f), 0.6f);
        darkMetal.SetFloat("_Metallic", 0.6f);
        sign = Mat("Door Sign Paper", new Color(1f, 0.95f, 0.55f), 0.1f);
        mat = Mat("Welcome Mat", new Color(0.55f, 0.4f, 0.25f), 0.05f);
        gamerBlack = Mat("Door Gamer Black", new Color(0.07f, 0.07f, 0.09f), 0.65f);
        stickerDark = Mat("Door Sticker Dark", new Color(0.05f, 0.05f, 0.07f), 0.3f);
        led = Glow("Door RGB LED", Color.white);
        red = Glow("Door Occupied Red", new Color(1f, 0.15f, 0.12f));
        green = Glow("Door Vacant Green", new Color(0.2f, 1f, 0.35f));
        clearGlass = Fade("Door Glass", new Color(0.7f, 0.85f, 0.95f, 0.28f), 0.95f);
        frosted = Fade("Door Frosted Glass", new Color(0.92f, 0.95f, 0.97f, 0.82f), 0.6f);
        wired = Fade("Door Wired Glass", new Color(0.75f, 0.85f, 0.85f, 0.45f), 0.8f);
        AssetDatabase.SaveAssets();
    }

    static Material Mat(string name, Color color, float gloss)
    {
        string path = $"{Folder}/{name}.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!m) { m = new Material(Shader.Find("Standard")); AssetDatabase.CreateAsset(m, path); }
        m.color = color;
        m.SetFloat("_Glossiness", gloss);
        EditorUtility.SetDirty(m);
        return m;
    }

    static Material Glow(string name, Color c)
    {
        var m = Mat(name, c, 0.5f);
        m.EnableKeyword("_EMISSION");
        m.SetColor("_EmissionColor", c * 2f);
        m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        return m;
    }

    static Material Fade(string name, Color c, float gloss)
    {
        var m = Mat(name, c, gloss);
        m.SetFloat("_Mode", 2f);
        m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        m.SetInt("_ZWrite", 0);
        m.DisableKeyword("_ALPHATEST_ON");
        m.EnableKeyword("_ALPHABLEND_ON");
        m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        m.renderQueue = 3000;
        return m;
    }
}
