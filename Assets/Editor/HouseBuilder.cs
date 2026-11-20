using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Menu: G7l6 > Build New House
///
/// Builds a 3-storey house with an attached garage into a new scene (Assets/Scenes/NewHouse.unity),
/// using the VNB Home Set modular kit (2 m grid, 3 m storeys), for a family of Mom, you, a little
/// sibling and the cat. Ground: garage (+ gym corner), mudroom, foyer, music room, powder room,
/// pantry, kitchen, dining, living room (fireplace), playroom (ball pit). First: Mom's suite with
/// fireplace, laundry, walk-in closet, your bedroom, shared bath, office, library, kid's room.
/// Top: home cinema, game room (pool table), WC, balcony. Each room has its own wall colour and floor.
/// Extra models (piano, fireplaces, pool table) live in Assets/ImportedAssets/Extra (see CREDITS.txt).
///
/// The new scene starts as a copy of HouseScene (so the player, camera, HUD, managers and cat
/// come along) with the old house removed. HouseScene itself is not changed.
/// Tasks are wired in as part of the build: homework at your desk, TVs (living room, game room, cinema)
/// + the remote on the coffee table, the kitchen oven (fire -> extinguisher in the pantry), laundry
/// (dirty clothes in the kid's room -> washer), and a leak from the top-floor WC into the library
/// (bucket + mop in the mudroom).
/// Interior only for now - the garden/outside comes later.
/// </summary>
public static class HouseBuilder
{
    const string SourceScene = "Assets/Scenes/HouseScene.unity";
    public const string TargetScene = "Assets/Scenes/NewHouse.unity";
    const string MaterialFolder = "Assets/Materials/House";
    const string CarFolder = "Assets/Models/FBX format";
    const float Storey = 3f;
    const int GroundLayer = 10, WallLayer = 9;
    /// <summary>How much bigger than the kit's real size the whole house is built (rooms, stairs, furniture).</summary>
    public static float HouseScale = 1.3f;
    /// <summary>
    /// Furniture is built at this fraction of the house scale (1.3 x 0.85 = about 1.1x real size), so the
    /// rooms are big but the furniture still fits a person - that's what keeps rooms from feeling cramped.
    /// </summary>
    const float FurnitureScale = 0.85f;
    static readonly HashSet<string> FitToWindow = new HashSet<string> { "Curtains (Open Side)", "Blinds (Open)" };

    static readonly string[] PrefabFolders =
    {
        "Assets/ImportedAssets/VNB - Home Set/Prefabs",
        "Assets/ImportedAssets/VNB - Gaming Set/Prefabs",
        "Assets/ImportedAssets/VNB - Office Set/Prefabs",
        "Assets/ImportedAssets/VNB - Office Set 2/Prefabs",
        "Assets/ImportedAssets/Furniture_FREE/Prefabs",
    };

    static readonly string[] OldHouseRoots =
    {
        "HouseTryTwo", "House", "Stairs Ramp (walkable)", "Stairs Ramp (walkable) - top", "Cat Walk Map",
        "Ceiling Leak", "Leak Flood", "Fire Extinguisher (placeholder)", "Mop (placeholder)", "Bucket (placeholder)", "NewHouse",
    };

    enum Kind { Living, Bedroom, Bath, Service, Hall, Stairs, Garage, Outdoor, Fun }
    enum Side { S, N, W, E }

    class Room
    {
        public string name;
        public int floor, x0, z0, x1, z1;
        public Kind kind;
        public bool windows;
        public Transform furniture;
        public bool Has(int x, int z) => x >= x0 && x < x1 && z >= z0 && z < z1;
        public bool Indoor => kind != Kind.Outdoor;
        public float Base => floor * Storey;
    }

    /// <summary>A wall segment on the grid: a line at a fixed x or z, 2 m long, starting at 'start'.</summary>
    struct Segment
    {
        public int floor;
        public bool alongX;   // true: runs along X at z = line; false: runs along Z at x = line
        public int line, start;
        public Vector3 Center(float y) => alongX ? new Vector3(start + 1f, y, line) : new Vector3(line, y, start + 1f);
        public float Yaw => alongX ? 0f : 90f;
    }

    static List<Room> rooms;
    static readonly Dictionary<(int, string, string), bool> links = new Dictionary<(int, string, string), bool>(); // (floor, a, b) -> open?
    static readonly List<Segment> doors = new List<Segment>();
    static readonly List<HouseLayout.WallInfo> mapWalls = new List<HouseLayout.WallInfo>();
    static readonly List<string> missing = new List<string>();
    static readonly Dictionary<string, GameObject> prefabCache = new Dictionary<string, GameObject>();
    static Transform root, structure, furnitureRoot;
    static Material wood, plaster, metal, garageDoorMat, ceilingMat;
    static int placed, skipped;
    /// <summary>What the tasks get wired onto (filled in while furnishing).</summary>
    static HouseTaskSetup.HouseTaskObjects taskObjects = new HouseTaskSetup.HouseTaskObjects();

    // Keep-clear areas in front of doors/archways, and the footprints of furniture already placed (plan metres).
    static readonly List<(int floor, Rect rect)> clearZones = new List<(int, Rect)>();
    static readonly List<(int floor, Rect rect, GameObject owner)> footprints = new List<(int, Rect, GameObject)>();
    static readonly List<(Room room, Segment seg, GameObject frame)> windowsBuilt = new List<(Room, Segment, GameObject)>();
    // In front of windows: no wall art and nothing tall (plan metres).
    static readonly List<(int floor, Rect rect)> windowZones = new List<(int, Rect)>();
    // Rooms whose windows go on specific segments of a side (e.g. either side of a fireplace).
    static readonly Dictionary<(int, string, Side), int[]> windowOverrides = new Dictionary<(int, string, Side), int[]>();
    static readonly Dictionary<string, Material> wallMats = new Dictionary<string, Material>();
    static Material brickMat;
    static readonly Dictionary<string, Material> floorMats = new Dictionary<string, Material>();
    const float Lift = 0.006f;   // furniture sits this far above the floor (no flicker with the floor)
    const float WallSink = 0.012f; // walls start this far below the floor (hides the door sill under the floor)

    // ------------------------------------------------------------------ the plan

    static void DefinePlan()
    {
        rooms = new List<Room>();
        links.Clear();

        // A family home: Mom, you (a teenager), a little sibling, and the cat.
        // Every room has one job and one focal point; no room is more than twice as long as it is wide.

        // Ground floor (house x6-20, z0-16; garage x0-6, z0-10). Front = south (z = 0).
        R(0, "Garage", 0, 0, 6, 10, Kind.Garage, false);
        R(0, "Mudroom", 6, 0, 10, 4, Kind.Service, false);
        R(0, "Foyer", 10, 0, 16, 4, Kind.Hall, true);
        R(0, "Music room", 16, 0, 20, 6, Kind.Fun, true);
        R(0, "Powder room", 6, 4, 10, 6, Kind.Bath, false);
        R(0, "Pantry", 6, 6, 10, 10, Kind.Service, false);
        R(0, "Hall", 10, 4, 12, 10, Kind.Hall, false);
        R(0, "Stairs", 12, 4, 16, 10, Kind.Stairs, false);
        R(0, "Kitchen", 16, 6, 20, 10, Kind.Living, true);
        R(0, "Playroom", 6, 10, 10, 16, Kind.Fun, true);
        R(0, "Living room", 10, 10, 16, 16, Kind.Living, true);
        R(0, "Dining", 16, 10, 20, 16, Kind.Living, true);
        Open(0, "Foyer", "Hall"); Open(0, "Foyer", "Stairs"); Open(0, "Hall", "Living room");
        Open(0, "Living room", "Dining"); Open(0, "Dining", "Kitchen");
        Door(0, "Garage", "Mudroom"); Door(0, "Mudroom", "Foyer"); Door(0, "Foyer", "Music room");
        Door(0, "Powder room", "Hall"); Door(0, "Pantry", "Hall"); Door(0, "Playroom", "Living room");
        windowOverrides[(0, "Living room", Side.N)] = new[] { 10, 14 }; // windows either side of the fireplace

        // First floor
        R(1, "Mom's bath", 0, 0, 6, 4, Kind.Bath, true);
        R(1, "Mom's bedroom", 0, 4, 6, 10, Kind.Bedroom, true);
        R(1, "Laundry", 6, 0, 10, 4, Kind.Service, true);
        R(1, "Walk-in closet", 6, 4, 10, 8, Kind.Service, false);
        R(1, "Corridor", 6, 8, 10, 10, Kind.Hall, false);
        R(1, "Landing", 10, 0, 16, 4, Kind.Hall, true);
        R(1, "Your bedroom", 16, 0, 20, 6, Kind.Bedroom, true);
        R(1, "Hall", 10, 4, 12, 10, Kind.Hall, false);
        R(1, "Stairs", 12, 4, 16, 10, Kind.Stairs, false);
        R(1, "Bathroom", 16, 6, 20, 10, Kind.Bath, true);
        R(1, "Office", 6, 10, 10, 16, Kind.Fun, true);
        R(1, "Library", 10, 10, 14, 16, Kind.Fun, true);
        R(1, "Kid's room", 14, 10, 20, 16, Kind.Bedroom, true);
        Open(1, "Landing", "Hall"); Open(1, "Landing", "Stairs"); Open(1, "Hall", "Corridor"); Open(1, "Hall", "Library");
        Door(1, "Mom's bedroom", "Mom's bath"); Door(1, "Mom's bedroom", "Corridor");
        Door(1, "Corridor", "Walk-in closet"); Door(1, "Walk-in closet", "Laundry"); Door(1, "Laundry", "Landing");
        Door(1, "Landing", "Your bedroom"); Door(1, "Your bedroom", "Bathroom"); Door(1, "Bathroom", "Kid's room");
        Door(1, "Library", "Office"); Door(1, "Library", "Kid's room");
        windowOverrides[(1, "Mom's bedroom", Side.W)] = new[] { 4, 8 }; // windows either side of her fireplace

        // Top floor (x10-20)
        R(2, "Landing", 10, 0, 16, 4, Kind.Hall, true);
        R(2, "Balcony", 16, 0, 20, 4, Kind.Outdoor, false);
        R(2, "Hall", 10, 4, 12, 12, Kind.Hall, false);
        R(2, "Stairs", 12, 4, 16, 10, Kind.Stairs, false);
        R(2, "Home cinema", 16, 4, 20, 10, Kind.Fun, false);
        R(2, "WC", 10, 12, 12, 16, Kind.Bath, true);
        R(2, "Game room", 12, 10, 20, 16, Kind.Fun, true);
        Open(2, "Landing", "Hall"); Open(2, "Landing", "Stairs"); Open(2, "Hall", "Game room");
        Door(2, "Landing", "Balcony"); Door(2, "Game room", "Home cinema"); Door(2, "Hall", "WC");
    }

    // Doors to the outside: (floor, room, side, first grid coordinate of the 2 m segment)
    static readonly (int floor, string room, Side side, int start)[] OutsideDoors =
    {
        (0, "Foyer", Side.S, 12),
        (0, "Dining", Side.N, 18),
    };
    // The garage door opening (ground floor, south wall of the garage)
    static readonly int[] GarageOpening = { 2, 4 };

    static void R(int f, string n, int x0, int z0, int x1, int z1, Kind k, bool windows) =>
        rooms.Add(new Room { floor = f, name = n, x0 = x0, z0 = z0, x1 = x1, z1 = z1, kind = k, windows = windows });

    static void Open(int f, string a, string b) => links[Key(f, a, b)] = true;
    static void Door(int f, string a, string b) => links[Key(f, a, b)] = false;
    static (int, string, string) Key(int f, string a, string b) => string.CompareOrdinal(a, b) < 0 ? (f, a, b) : (f, b, a);

    static Room RoomAt(int f, int x, int z) => rooms.FirstOrDefault(r => r.floor == f && r.Has(x, z));
    static Room Find(int f, string name) => rooms.First(r => r.floor == f && r.name == name);

    // ------------------------------------------------------------------ entry point

    [MenuItem("G7l6/Build New House")]
    public static void Build()
    {
        if (EditorApplication.isPlaying) { EditorUtility.DisplayDialog("Build New House", "Stop Play mode first.", "OK"); return; }
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        if (File.Exists(TargetScene) &&
            !EditorUtility.DisplayDialog("Build New House", "NewHouse.unity already exists. Rebuild it from scratch? (HouseScene is not touched.)", "Rebuild", "Cancel"))
            return;

        missing.Clear(); doors.Clear(); mapWalls.Clear(); prefabCache.Clear(); prefabIndex = null; placed = 0; skipped = 0;
        taskObjects = new HouseTaskSetup.HouseTaskObjects();
        clearZones.Clear(); footprints.Clear(); windowsBuilt.Clear(); floorMats.Clear();
        windowZones.Clear(); windowOverrides.Clear(); wallMats.Clear(); roomOfGroup.Clear(); wallArt.Clear();

        if (File.Exists(TargetScene)) AssetDatabase.DeleteAsset(TargetScene);
        if (!AssetDatabase.CopyAsset(SourceScene, TargetScene)) { EditorUtility.DisplayDialog("Build New House", "Couldn't copy HouseScene.", "OK"); return; }
        var scene = EditorSceneManager.OpenScene(TargetScene, OpenSceneMode.Single);

        try
        {
            EditorUtility.DisplayProgressBar("Building the house", "Clearing the old house", 0.05f);
            RemoveOldHouse(scene);
            Materials();
            DefinePlan();

            root = new GameObject("NewHouse").transform;
            structure = new GameObject("Structure").transform; structure.SetParent(root);
            furnitureRoot = new GameObject("Furniture").transform; furnitureRoot.SetParent(root);
            foreach (var r in rooms)
            {
                var g = new GameObject($"{FloorName(r.floor)} - {r.name}").transform;
                g.SetParent(furnitureRoot);
                r.furniture = g;
            }

            EditorUtility.DisplayProgressBar("Building the house", "Floors and ceilings", 0.15f);
            for (int f = 0; f < 3; f++) FloorsAndCeilings(f);
            EditorUtility.DisplayProgressBar("Building the house", "Walls, doors and windows", 0.3f);
            for (int f = 0; f < 3; f++) Walls(f);
            EditorUtility.DisplayProgressBar("Building the house", "Stairs", 0.45f);
            Stairs();
            Garage();
            EditorUtility.DisplayProgressBar("Building the house", "Lights", 0.5f);
            Lights();
            EditorUtility.DisplayProgressBar("Building the house", "Curtains and blinds", 0.55f);
            WindowDressing();
            EditorUtility.DisplayProgressBar("Building the house", "Furniture", 0.6f);
            Furnish();
            FillEmptyRooms();
            EditorUtility.DisplayProgressBar("Building the house", "Player, cat and walk map", 0.85f);
            WriteLayout();
            ScaleHouse();
            PlacePlayerAndCat();
            EditorUtility.DisplayProgressBar("Building the house", "Tasks", 0.9f);
            taskReport = HouseTaskSetup.SetUpTasksInNewHouse(taskObjects);
            HouseTaskSetup.PrepareCatForHouse();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AddToBuildSettings();
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        string message = $"Built NewHouse.unity: {rooms.Count(r => r.Indoor)} rooms over 3 floors + garage, {placed} pieces of furniture, at {HouseScale}x size.\n" +
                         (skipped > 0 ? $"{skipped} pieces were left out because they would have blocked a doorway or a path.\n" : "") +
                         "\nStart: the foyer (ground floor). Garage door: press E on it. Minimap: top left (M hides it).";
        if (!string.IsNullOrEmpty(taskReport)) message += "\n\n" + taskReport;
        if (missing.Count > 0) message += "\n\nCouldn't find these prefabs (skipped):\n- " + string.Join("\n- ", missing.Distinct());
        message += "\n\nThe scene is saved. Press Play to walk around.";
        Debug.Log("[Build New House]\n" + message);
        Selection.activeObject = root.gameObject;
        EditorUtility.DisplayDialog("House built", message, "OK");
    }

    static string FloorName(int f) => f == 0 ? "Ground" : f == 1 ? "First" : "Top";

    static string taskReport;

    static void RemoveOldHouse(Scene scene)
    {
        foreach (var go in scene.GetRootGameObjects())
            if (OldHouseRoots.Contains(go.name)) Object.DestroyImmediate(go);
    }

    // ------------------------------------------------------------------ floors, ceilings

    static void FloorsAndCeilings(int f)
    {
        var parent = Group(structure, $"{FloorName(f)} floor/Floors");
        float baseY = f * Storey, top = baseY + Storey;
        foreach (var r in rooms.Where(r => r.floor == f))
        {
            for (int x = r.x0; x < r.x1; x += 2)
            for (int z = r.z0; z < r.z1; z += 2)
            {
                var cell = new Vector3(x + 1f, 0f, z + 1f);
                if (!(r.kind == Kind.Stairs && f > 0))
                {
                    var tile = Piece("Floor", parent, new Vector3(cell.x, baseY, cell.z), 0f);
                    if (tile) { SetLayer(tile, GroundLayer); Paint(tile, FloorMaterial(r)); }
                }
                if (!r.Indoor) continue;

                var above = RoomAt(f + 1, x, z);
                if (above != null && above.kind == Kind.Stairs) continue;      // open stairwell
                bool roof = above == null;
                var ceiling = Piece("Floor", parent, new Vector3(cell.x, (roof ? top : top - 0.045f) - 0.04f, cell.z), 0f, flip: true);
                if (ceiling) { SetLayer(ceiling, 0); Paint(ceiling, ceilingMat); }
                if (roof)
                {
                    var cap = Piece("Floor", parent, new Vector3(cell.x, top + 0.05f, cell.z), 0f);
                    if (cap) SetLayer(cap, GroundLayer);
                }
            }
        }
    }

    // ------------------------------------------------------------------ walls, doors, windows

    static void Walls(int f)
    {
        var parent = Group(structure, $"{FloorName(f)} floor/Walls");
        float y = f * Storey;
        var floorRooms = rooms.Where(r => r.floor == f).ToList();

        // Pick the door segment for every linked pair: the middle of their shared wall.
        var shared = new Dictionary<(int, string, string), List<Segment>>();
        var edges = new List<(Room a, Room b, Segment s)>();
        foreach (var r in floorRooms)
        {
            for (int x = r.x0; x < r.x1; x += 2)
            for (int z = r.z0; z < r.z1; z += 2)
            {
                Consider(r, x, z - 2, new Segment { floor = f, alongX = true, line = z, start = x });
                Consider(r, x, z + 2, new Segment { floor = f, alongX = true, line = z + 2, start = x });
                Consider(r, x - 2, z, new Segment { floor = f, alongX = false, line = x, start = z });
                Consider(r, x + 2, z, new Segment { floor = f, alongX = false, line = x + 2, start = z });
            }
        }

        void Consider(Room r, int nx, int nz, Segment s)
        {
            var n = RoomAt(f, nx, nz);
            if (n == r) return;
            if (n != null && rooms.IndexOf(n) < rooms.IndexOf(r)) return; // each shared edge once
            edges.Add((r, n, s));
            if (n != null && links.TryGetValue(Key(f, r.name, n.name), out bool open) && !open)
            {
                var k = Key(f, r.name, n.name);
                if (!shared.TryGetValue(k, out var list)) shared[k] = list = new List<Segment>();
                list.Add(s);
            }
        }

        var doorSegments = new HashSet<(bool, int, int)>();
        foreach (var pair in shared.Values)
        {
            var sorted = pair.OrderBy(s => s.start).ToList();
            var pick = sorted[sorted.Count / 2];
            doorSegments.Add((pick.alongX, pick.line, pick.start));
            doors.Add(pick);
        }
        foreach (var d in OutsideDoors.Where(d => d.floor == f))
        {
            var room = Find(f, d.room);
            var s = SideSegment(room, d.side, d.start);
            doorSegments.Add((s.alongX, s.line, s.start));
            doors.Add(s);
        }

        // Windows: on every other outside segment of rooms that want them (corners stay plain).
        var windowSegments = new HashSet<(bool, int, int)>();
        foreach (var r in floorRooms.Where(r => r.windows))
        {
            Side? onlySide = null;
            if (r.kind == Kind.Bath)
                onlySide = System.Enum.GetValues(typeof(Side)).Cast<Side>().OrderByDescending(sd => OutsideSegments(r, sd).Count).First();
            foreach (Side side in System.Enum.GetValues(typeof(Side)))
            {
                if (onlySide.HasValue && side != onlySide.Value) continue;
                var outside = OutsideSegments(r, side);
                if (windowOverrides.TryGetValue((f, r.name, side), out var chosen))
                {
                    foreach (var o in outside.Where(o => chosen.Contains(o.start))) windowSegments.Add((o.alongX, o.line, o.start));
                    continue;
                }
                for (int i = 0; i < outside.Count; i++)
                    if (outside.Count == 1 || i % 2 == 1) windowSegments.Add((outside[i].alongX, outside[i].line, outside[i].start));
            }
        }

        foreach (var (a, b, s) in edges)
        {
            var key = (s.alongX, s.line, s.start);
            bool aIn = a.Indoor, bIn = b != null && b.Indoor;
            if (!aIn && !bIn)
            {
                if (b == null) { Railing(Group(structure, $"{FloorName(f)} floor/Railings"), s, y); MapWall(s, HouseLayout.WallType.Railing); }
                continue;
            }
            if (b != null && aIn && bIn && links.TryGetValue(Key(f, a.name, b.name), out bool open) && open) continue;
            if (f == 0 && a.name == "Garage" && b == null && s.alongX && s.line == 0 && GarageOpening.Contains(s.start)) continue;

            var indoor = aIn ? a : b;
            Vector3 at = s.Center(y - WallSink); // sunk a little: the door sill hides under the floor instead of flickering with it
            if (doorSegments.Contains(key))
            {
                var w = Piece("Wall Standard (Door)", parent, at, s.Yaw);
                MapWall(s, HouseLayout.WallType.Door);
                MeshColliders(w);
                PaintWallFaces(w, s);
                Grow(Piece("Door Frame 1", parent, s.Center(y), s.Yaw), 1.012f);
                KeepClear(f, s, 1.2f, 1.0f);
            }
            else if (windowSegments.Contains(key))
            {
                string style = indoor.kind == Kind.Living || indoor.kind == Kind.Fun ? "3" : indoor.kind == Kind.Bedroom ? "1" : "4";
                var w = Piece($"Wall Standard (Window {style})", parent, at, s.Yaw);
                MapWall(s, HouseLayout.WallType.Window);
                MeshColliders(w);
                PaintWallFaces(w, s);
                var frame = Piece($"Window {style}", parent, s.Center(y), s.Yaw);
                Grow(frame, 1.012f);
                windowsBuilt.Add((indoor, s, frame));
                KeepWindowClear(indoor, s, frame);
            }
            else
            {
                PaintWallFaces(Piece("Wall Standard", parent, at, s.Yaw), s);
                MapWall(s, HouseLayout.WallType.Wall);
            }
        }

        // Archways (open links) stay clear too, so you can walk straight through.
        foreach (var (a, b, s) in edges)
            if (b != null && links.TryGetValue(Key(f, a.name, b.name), out bool isOpen) && isOpen)
                KeepClear(f, s, 2f, 0.7f);

        // Columns where the outside wall turns a corner (hides the little gaps at corners).
        var vertices = new HashSet<(int, int)>();
        foreach (var r in floorRooms) { vertices.Add((r.x0, r.z0)); vertices.Add((r.x1, r.z0)); vertices.Add((r.x0, r.z1)); vertices.Add((r.x1, r.z1)); }
        foreach (var (vx, vz) in vertices)
        {
            int inside = 0;
            if (IsIndoor(f, vx - 2, vz - 2)) inside++;
            if (IsIndoor(f, vx, vz - 2)) inside++;
            if (IsIndoor(f, vx - 2, vz)) inside++;
            if (IsIndoor(f, vx, vz)) inside++;
            bool diagonal = inside == 2 && IsIndoor(f, vx - 2, vz - 2) == IsIndoor(f, vx, vz);
            if (inside == 1 || inside == 3 || diagonal)
            {
                // The column is a bit taller than a storey - trim it so it doesn't poke through the floor above.
                var col = Piece("Wall Column", parent, new Vector3(vx, y - WallSink, vz), 0f);
                if (col) col.transform.localScale = new Vector3(1.01f, (Storey - 0.03f) / 3.0363f, 1.01f);
            }
        }

        foreach (Transform t in parent) SetLayer(t.gameObject, WallLayer);
    }

    static bool IsIndoor(int f, int x, int z) { var r = RoomAt(f, x, z); return r != null && r.Indoor; }

    /// <summary>A keep-clear box centred on a wall segment, 'width' along it and 'depth' into both sides.</summary>
    static void MapWall(Segment s, HouseLayout.WallType type) => mapWalls.Add(new HouseLayout.WallInfo
    {
        floor = s.floor,
        a = s.alongX ? new Vector2(s.start, s.line) : new Vector2(s.line, s.start),
        b = s.alongX ? new Vector2(s.start + 2, s.line) : new Vector2(s.line, s.start + 2),
        type = type,
    });

    static void KeepClear(int f, Segment s, float width, float depth)
    {
        Vector3 c = s.Center(0f);
        var rect = s.alongX
            ? new Rect(c.x - width * 0.5f, c.z - depth, width, depth * 2f)
            : new Rect(c.x - depth, c.z - width * 0.5f, depth * 2f, width);
        clearZones.Add((f, rect));
    }

    static Segment SideSegment(Room r, Side side, int start) => side switch
    {
        Side.S => new Segment { floor = r.floor, alongX = true, line = r.z0, start = start },
        Side.N => new Segment { floor = r.floor, alongX = true, line = r.z1, start = start },
        Side.W => new Segment { floor = r.floor, alongX = false, line = r.x0, start = start },
        _ => new Segment { floor = r.floor, alongX = false, line = r.x1, start = start },
    };

    /// <summary>Segments of a room's side that face outside (or an outdoor area like the balcony).</summary>
    static List<Segment> OutsideSegments(Room r, Side side)
    {
        var list = new List<Segment>();
        bool alongX = side == Side.S || side == Side.N;
        int from = alongX ? r.x0 : r.z0, to = alongX ? r.x1 : r.z1;
        for (int a = from; a < to; a += 2)
        {
            int nx = alongX ? a : side == Side.W ? r.x0 - 2 : r.x1;
            int nz = alongX ? (side == Side.S ? r.z0 - 2 : r.z1) : a;
            var n = RoomAt(r.floor, nx, nz);
            if (n == null || !n.Indoor) list.Add(SideSegment(r, side, a));
        }
        return list;
    }

    static void Railing(Transform parent, Segment s, float y)
    {
        Vector3 c = s.Center(y);
        Box("Rail", parent, c + Vector3.up * 1.0f, s.alongX ? new Vector3(2f, 0.08f, 0.08f) : new Vector3(0.08f, 0.08f, 2f), wood, true);
        Box("Rail base", parent, c + Vector3.up * 0.05f, s.alongX ? new Vector3(2f, 0.1f, 0.1f) : new Vector3(0.1f, 0.1f, 2f), wood, false);
        for (int i = 0; i <= 4; i++)
        {
            float t = -1f + i * 0.5f;
            Vector3 p = c + (s.alongX ? new Vector3(t, 0f, 0f) : new Vector3(0f, 0f, t)) + Vector3.up * 0.5f;
            Box("Post", parent, p, new Vector3(0.05f, 1f, 0.05f), wood, true);
        }
    }

    // ------------------------------------------------------------------ stairs (U-shaped, stacked)

    static void Stairs()
    {
        var parent = Group(structure, "Stairs");
        for (int f = 0; f < 2; f++) Flight(parent, f * Storey);

        // Centre wall between the two flights, on every floor (it's also the guard on the top floor).
        for (int f = 0; f < 3; f++)
            Box($"Stair divider {f}", parent, new Vector3(14f, f * Storey + 1.49f, 5.75f), new Vector3(0.12f, 2.96f, 3.5f), plaster, true, WallLayer);

        // Top floor: rail across the drop above the last flight.
        Box("Stair guard rail", parent, new Vector3(13f, 2 * Storey + 1.0f, 4.05f), new Vector3(2f, 0.08f, 0.08f), wood, true);
        Box("Stair guard", parent, new Vector3(13f, 2 * Storey + 0.5f, 4.05f), new Vector3(2f, 1f, 0.04f), wood, true);

        // Ground floor: close off the space under the second flight.
        Box("Under-stair wall", parent, new Vector3(15f, 1.25f, 4.35f), new Vector3(2f, 2.5f, 0.12f), plaster, true, WallLayer);
    }

    /// <summary>
    /// One storey of U-shaped stairs in the core (x12-16, z4-10): flight A goes north up the west half
    /// to a half-landing, flight B comes back south up the east half to the next floor.
    /// </summary>
    static void Flight(Transform parent, float b)
    {
        const int steps = 9;
        const float rise = 1.5f / steps, run = 3.5f / steps, width = 1.86f;
        var p = Group(parent, $"Flight {b / Storey:0}");

        for (int i = 0; i < steps; i++)
        {
            // Flight A (west half, going north)
            float zA = 4f + i * run;
            Box("Step", p, new Vector3(13f - 0.03f, b + (i + 1) * rise - (rise + 0.15f) * 0.5f, zA + run * 0.5f), new Vector3(width, rise + 0.15f, run), wood, false);
            // Flight B (east half, going south)
            float zB = 7.5f - (i + 1) * run;
            Box("Step", p, new Vector3(15f + 0.03f, b + 1.5f + (i + 1) * rise - (rise + 0.15f) * 0.5f, zB + run * 0.5f), new Vector3(width, rise + 0.15f, run), wood, false);
        }

        // Half-landing (a hair below the last step so their tops never share a plane)
        Box("Half landing", p, new Vector3(14f, b + 1.5f - 0.006f - 0.15f, 8.55f), new Vector3(3.98f, 0.3f, 2.9f), wood, true, GroundLayer);

        // Smooth walkable ramps along the step edges (so the player and cat glide up).
        Ramp(p, new Vector3(13f - 0.03f, b, 4f - run), new Vector3(13f - 0.03f, b + 1.5f, 4f + (steps - 1) * run), width);
        Ramp(p, new Vector3(15f + 0.03f, b + 1.5f, 7.5f + run), new Vector3(15f + 0.03f, b + 3f, 7.5f - (steps - 1) * run), width);
        Box("Top step", p, new Vector3(15f + 0.03f, b + 3f - 0.006f - 0.15f, 4.2f), new Vector3(width - 0.01f, 0.3f, 0.5f), wood, true, GroundLayer);
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

    // ------------------------------------------------------------------ garage

    static void Garage()
    {
        var parent = Group(structure, "Garage door");
        // Header over the 4 m opening (x2-6) and the door that swings up under the ceiling.
        Box("Garage header", parent, new Vector3(4f, 2.72f, 0f), new Vector3(3.99f, 0.44f, 0.2f), plaster, true, WallLayer);
        clearZones.Add((0, new Rect(2f, 0f, 4f, 1.0f))); // keep the door's swing clear
        var door = new GameObject("Garage Door");
        door.transform.SetParent(parent, true);
        door.transform.position = new Vector3(4f, 2.5f, 0.12f);
        var hinge = new GameObject("Hinge").transform;
        hinge.SetParent(door.transform, false);
        var panel = Box("Panel", hinge, door.transform.position + Vector3.down * 1.25f, new Vector3(3.96f, 2.5f, 0.06f), garageDoorMat, true, WallLayer);
        for (int i = 1; i < 5; i++)
            Box("Groove", hinge, door.transform.position + new Vector3(0f, -0.5f * i, -0.035f), new Vector3(3.9f, 0.03f, 0.02f), metal, false);
        var gd = door.AddComponent<GarageDoor>();
        gd.hinge = hinge;
        Outline(panel);
    }

    // ------------------------------------------------------------------ lights

    static void Lights()
    {
        var parent = Group(root, "Lights");
        foreach (var r in rooms.Where(r => r.Indoor && r.kind != Kind.Stairs))
        {
            float ceiling = r.Base + Storey - 0.09f;
            float cx = (r.x0 + r.x1) * 0.5f, cz = (r.z0 + r.z1) * 0.5f;
            string lampModel = r.kind switch
            {
                Kind.Living => "Ceiling Lamp 2",
                Kind.Fun => "Ceiling Lamp 3",
                Kind.Bedroom => "Ceiling Lamp 4",
                _ => "Ceiling Lamp 1",
            };
            var lamp = Spawn(lampModel, parent) ?? Spawn("Ceiling Lamp 1", parent);
            if (lamp)
            {
                var b = Bounds(lamp);
                lamp.transform.position += new Vector3(cx, ceiling, cz) - new Vector3(b.center.x, b.max.y, b.center.z);
            }
            var light = new GameObject($"Light - {FloorName(r.floor)} {r.name}").AddComponent<Light>();
            light.transform.SetParent(parent, true);
            light.transform.position = new Vector3(cx, ceiling - 0.6f, cz);
            light.type = LightType.Point;
            light.color = r.name switch
            {
                "Home cinema" => new Color(0.72f, 0.6f, 1f),
                "Game room" => new Color(0.85f, 0.82f, 1f),
                "Bathroom" or "Mom's bath" or "WC" or "Powder room" => new Color(0.95f, 0.97f, 1f),
                _ => new Color(1f, 0.93f, 0.82f),
            };
            light.intensity = r.kind == Kind.Garage ? 0.8f : r.name == "Home cinema" ? 0.45f : 1f;
            light.range = Mathf.Max(r.x1 - r.x0, r.z1 - r.z0) * 0.85f + 1.5f;
            light.shadows = LightShadows.None;
        }
        // Stairwell
        var stairLight = new GameObject("Light - Stairwell").AddComponent<Light>();
        stairLight.transform.SetParent(parent, true);
        stairLight.transform.position = new Vector3(14f, 7.5f, 7f);
        stairLight.type = LightType.Point;
        stairLight.range = 9f;
        stairLight.intensity = 0.9f;
        stairLight.color = new Color(1f, 0.93f, 0.82f);

        var sun = new GameObject("Sun").AddComponent<Light>();
        sun.transform.SetParent(parent, true);
        sun.type = LightType.Directional;
        sun.transform.rotation = Quaternion.Euler(50f, 210f, 0f);
        sun.intensity = 0.9f;
        sun.shadows = LightShadows.Soft;
    }

    // ------------------------------------------------------------------ furniture (themed per room)
    // Order per room (interior design rule of thumb): focal point first, then the main group around it,
    // then storage, then accents (art, plants, lamps) and finally the little story props.
    // "A|B|C" = try A, and if it doesn't fit, B, then C.

    static void Furnish()
    {
        // ================= Ground floor =================

        // Garage: two cars, a workbench wall and a little home-gym corner.
        Car("suv", 0, "Garage", 0.7f, 0.72f);
        Car("sedan", 0, "Garage", 0.7f, 0.3f);
        var bench = Wall(0, "Garage", "Counter (Separated)|Counter 1", Side.W, 0.82f);
        On(bench, "Toolbox", 0.5f, 0.3f);
        On(bench, "Hammer", 0.2f, 0.6f);
        On(bench, "Wrench", 0.8f, 0.6f);
        Wall(0, "Garage", "Modern Shelves|Shelf - Box", Side.W, 0.58f);
        var gymMat = Center(0, "Garage", "Rug Tile", 0.16f, 0.24f, 0f);
        On(gymMat, "dumbbell_001", 0.3f, 0.4f);
        On(gymMat, "dumbbell_002", 0.6f, 0.4f, 30f);
        Center(0, "Garage", "training_item_001", 0.12f, 0.36f, 90f);
        Wall(0, "Garage", "training_item_002", Side.W, 0.2f);
        Wall(0, "Garage", "Metallic Ladder", Side.N, 0.12f);
        Center(0, "Garage", "Tire", 0.1f, 0.96f, 30f);

        // Mudroom: bench + shoe box, hooks shelf, the cat's scratching post by the door.
        var mudBench = Wall(0, "Mudroom", "Drawer A|Drawer B", Side.W, 0.75f);
        On(mudBench, "Small Box", 0.3f, 0.5f);
        Wall(0, "Mudroom", "Wall Shelf 2|Wall Shelf", Side.W, 0.75f, 1.6f);
        Wall(0, "Mudroom", "Plastic Basket", Side.N, 0.15f);
        Wall(0, "Mudroom", "Plant Deco 1|Plant 1", Side.S, 0.1f);
        Wall(0, "Mudroom", "Mop", Side.N, 0.45f);      // task tools: mop up floods,
        Wall(0, "Mudroom", "Bucket", Side.N, 0.7f);    // catch the ceiling leak

        // Foyer: console with an entry mirror, runner rug, a big plant.
        var console = Wall(0, "Foyer", "Bed Table|Drawer B", Side.W, 0.3f);
        On(console, "Vase 1", 0.5f, 0.5f);
        Above(console, "Mirror 2|Mirror", 0.35f);
        Center(0, "Foyer", "Rug B|Rug A", 0.5f, 0.45f, 90f);
        Wall(0, "Foyer", "Plant 3|Plant Deco 1", Side.S, 0.95f);
        Wall(0, "Foyer", "Painting Small 2|Painting (Small A)", Side.E, 0.15f, 1.45f);

        // Music room: grand piano in the middle (keys facing the door), guitar, speakers, records.
        var piano = Center(0, "Music room", "Piano", 0.5f, 0.62f, 180f);
        if (piano) Seat(piano, "Stool 1|Stool|Stool 2");
        Center(0, "Music room", "Rug A|Rug Tile", 0.5f, 0.6f, 0f);
        Wall(0, "Music room", "musical_instrument_001", Side.N, 0.1f);
        Wall(0, "Music room", "Speaker 3|Speaker 2", Side.N, 0.92f);
        Wall(0, "Music room", "Bookshelf|Library", Side.E, 0.12f);
        Wall(0, "Music room", "lounge_chair_001|Chair 2", Side.S, 0.12f);
        Wall(0, "Music room", "Lamp 2|Lamp 1", Side.S, 0.32f);
        Wall(0, "Music room", "Painting 1|Big Painting", Side.W, 0.75f, 1.5f);
        Wall(0, "Music room", "Plant 4|Plant 3", Side.E, 0.95f);

        // Powder room (navy, small and smart)
        Wall(0, "Powder room", "Toilet", Side.W, 0.5f);
        var powderSink = Wall(0, "Powder room", "Sink", Side.N, 0.3f);
        Above(powderSink, "Mirror|Mirror 2", 0.3f);
        Wall(0, "Powder room", "Towel (Wall)|Towel (Wall - Alt) Variant", Side.S, 0.3f, 1.15f);

        // Pantry: floor-to-ceiling storage.
        var pShelf = Wall(0, "Pantry", "Modern Shelves Variant|Modern Shelves", Side.W, 0.5f);
        On(pShelf, "Simple Cereal Box", 0.5f, 0.5f);
        Wall(0, "Pantry", "Simple Shelf|Shelf - Simple", Side.N, 0.5f);
        Wall(0, "Pantry", "Shelf - Box|Shelf", Side.S, 0.5f);
        Wall(0, "Pantry", "fridge_001", Side.W, 0.08f);
        Center(0, "Pantry", "Small Box", 0.45f, 0.35f, 20f);
        Wall(0, "Pantry", "Fire Extinguisher", Side.N, 0.88f);

        // Kitchen: sink under the window, fridge at the end of the run, oven on the other wall.
        Wall(0, "Kitchen", "Fridge", Side.E, 0.0f, snug: true);
        var counter = Wall(0, "Kitchen", "Counter (Connected)", Side.E, 0.4f, snug: true);
        var sink = Wall(0, "Kitchen", "Counter (Connected - Sink)", Side.E, 0.78f, snug: true);
        taskObjects.oven = Wall(0, "Kitchen", "Oven", Side.S, 0.35f, snug: true);
        var counter2 = Wall(0, "Kitchen", "Counter (Connected) 2|Counter (Connected) Variant", Side.S, 0.75f, snug: true);
        On(counter, "Coffee Machine|coffee_machine_001", 0.5f, 0.6f);
        On(sink, "dish_001", 0.2f, 0.5f);
        On(counter2, "Microwave", 0.5f, 0.6f);
        On(counter2, "Cutting Board", 0.15f, 0.5f);
        Wall(0, "Kitchen", "Wall Shelf 3|Wall Shelf", Side.W, 0.5f, 1.55f);
        Wall(0, "Kitchen", "Flower Wall Pot 3|Flower Wall Pot", Side.W, 0.15f, 1.7f);

        // Dining: round table for four under a pendant, sideboard, art.
        var table = Center(0, "Dining", "Round Table|kitchen_table_001", 0.55f, 0.5f, 0f);
        ChairsAround(table, "Chair 3|kitchen_chair_001");
        if (table) { On(table, "Vase 2|flower_001", 0.5f, 0.5f); }
        var sideboard = Wall(0, "Dining", "Drawer B|Drawer A", Side.E, 0.22f);
        On(sideboard, "Bread", 0.3f, 0.5f);
        Wall(0, "Dining", "Painting 2|Painting 3", Side.N, 0.2f, 1.45f);
        Wall(0, "Dining", "Plant 2|Plant 3", Side.E, 0.96f);

        // Living room: the fireplace is the focal point, TV above it, sofa facing it, armchairs either side.
        var fire = Wall(0, "Living room", "Fireplace", Side.N, 0.5f);
        if (fire)
        {
            FireGlow(fire);
            var livingTv = Above(fire, "TV 2 (Curved - Wall)|Painting 1", 0.25f);
            if (livingTv && livingTv.name.StartsWith("TV")) taskObjects.tvs.Add((livingTv, "Living room TV", null));
        }
        Center(0, "Living room", "Rug Tile|Rug A", 0.5f, 0.52f, 0f);
        var sofa = Center(0, "Living room", "Sofa|Sofa 1|Sofa 2", 0.5f, 0.28f, 0f);
        On(sofa, "Simple Pillow (Sofa)", 0.25f, 0.4f);
        var coffee = Center(0, "Living room", "Coffee Table|coffee_table_001", 0.5f, 0.52f, 0f);
        On(coffee, "Book Pile 1|Book", 0.3f, 0.5f);
        taskObjects.remote = On(coffee ? coffee : sofa, "Controller (TV)|Controller TV", 0.75f, 0.45f, 80f);
        Center(0, "Living room", "lounge_chair_001|Chair 2", 0.12f, 0.6f, 90f);
        Center(0, "Living room", "Chair 2|lounge_chair_001", 0.84f, 0.6f, -90f);
        Wall(0, "Living room", "scratching_post_001", Side.S, 0.62f);
        Wall(0, "Living room", "Modern Shelves|Bookshelf", Side.S, 0.95f);
        Wall(0, "Living room", "Lamp 1|Lamp 2", Side.N, 0.02f);
        Wall(0, "Living room", "Plant Deco 1|Plant 5", Side.N, 0.98f);
        Center(0, "Living room", "toy_001", 0.3f, 0.15f, 40f);

        // Playroom: a ball pit! Toys everywhere, a little table to draw at.
        BallPit(0, "Playroom", 0.32f, 0.72f, 2.0f, 2.2f);
        var kidTable = Wall(0, "Playroom", "Desk 1|Simple Desk", Side.E, 0.12f);
        if (kidTable) { On(kidTable, "Crayon", 0.4f, 0.5f); Seat(kidTable, "Chair|Stool"); }
        Center(0, "Playroom", "Rug Tile", 0.5f, 0.3f, 0f);
        Center(0, "Playroom", "Toy House", 0.3f, 0.22f, 200f);
        Center(0, "Playroom", "Toy Car 1", 0.55f, 0.32f, 30f);
        Center(0, "Playroom", "Toy Car 2", 0.45f, 0.42f, 80f);
        Center(0, "Playroom", "Toy Plane", 0.62f, 0.2f, 120f);
        Center(0, "Playroom", "Toy Figure 2", 0.4f, 0.3f, 0f);
        Center(0, "Playroom", "Puff|Puff Variant", 0.75f, 0.5f, 0f);
        Wall(0, "Playroom", "Simple Shelf|Shelf - Simple", Side.S, 0.5f);
        Wall(0, "Playroom", "Small Whiteboard|Whiteboard (Wall)", Side.S, 0.5f, 1.2f);
        Wall(0, "Playroom", "Poster 2|Poster 1", Side.E, 0.85f, 1.6f);

        // WC (top floor) - furnished before the first floor: its sink is the tap that leaks, and the spot
        // right below it in the library is kept free for the drips and the bucket.
        Wall(2, "WC", "Toilet", Side.N, 0.5f);
        var wcSink = Wall(2, "WC", "Sink", Side.W, 0.35f);
        Above(wcSink, "Mirror", 0.3f);
        if (wcSink)
        {
            taskObjects.leakSink = wcSink;
            var below = Find(1, "Library");
            var b = Bounds(wcSink);
            float x0 = Mathf.Max(below.x0 + 0.07f, b.center.x - 0.6f), x1 = Mathf.Min(below.x1 - 0.07f, b.center.x + 0.6f);
            float z0 = Mathf.Max(below.z0 + 0.07f, b.center.z - 0.6f), z1 = Mathf.Min(below.z1 - 0.07f, b.center.z + 0.6f);
            if (x1 > x0 && z1 > z0) clearZones.Add((1, Rect.MinMaxRect(x0, z0, x1, z1)));
        }

        // ================= First floor =================

        // Mom's bath: tub, vanity, toilet; aqua tiles.
        Wall(1, "Mom's bath", "Bathtub", Side.E, 0.5f);
        Wall(1, "Mom's bath", "Toilet", Side.N, 0.05f);
        var momSink = Wall(1, "Mom's bath", "Sink", Side.N, 0.95f);
        Above(momSink, "Mirror 2|Mirror", 0.3f);
        On(momSink, "Soap Dispenser", 0.2f, 0.5f);
        Wall(1, "Mom's bath", "Towel (Wall - Alt) Variant|Towel (Wall)", Side.W, 0.2f, 1.15f);
        Wall(1, "Mom's bath", "Plant 5|Plant 4", Side.S, 0.05f);

        // Mom's bedroom: bed facing a fireplace, nightstands, dresser, reading chair.
        var momBed = Wall(1, "Mom's bedroom", "2P Bed 2|2P Bed", Side.E, 0.3f);
        foreach (var stand in Beside(momBed, "Bed Table|Drawer A", -1, 1)) On(stand, "Small Lamp 2|Small Lamp 1", 0.5f, 0.5f);
        Above(momBed, "Painting 3|Big Painting", 0.35f);
        var momFire = Wall(1, "Mom's bedroom", "Fireplace (Stone)|Fireplace", Side.W, 0.5f);
        if (momFire) FireGlow(momFire);
        var dresser = Wall(1, "Mom's bedroom", "Drawer 3|Drawer 4", Side.S, 0.9f);
        On(dresser, "Perfume 1", 0.3f, 0.5f);
        Center(1, "Mom's bedroom", "Rug A|Rug B", 0.42f, 0.42f, 90f);
        Wall(1, "Mom's bedroom", "lounge_chair_001|Chair 2", Side.N, 0.12f);
        Wall(1, "Mom's bedroom", "Plant 4|Plant 1", Side.N, 0.92f);

        // Laundry
        var washer = Wall(1, "Laundry", "Washing Machine|washing_machine_001", Side.S, 0.25f, snug: true);
        taskObjects.washer = washer;
        Wall(1, "Laundry", "Dryer Machine", Side.S, 0.6f, snug: true);
        On(washer, "Washing Product", 0.5f, 0.5f);
        Wall(1, "Laundry", "Plastic Basket", Side.W, 0.75f);
        Wall(1, "Laundry", "Wall Shelf|Wall Shelf 1", Side.W, 0.45f, 1.6f);

        // Walk-in closet
        Wall(1, "Walk-in closet", "Wardrobe|closet_001", Side.W, 0.5f);
        Wall(1, "Walk-in closet", "closet_002|Wardrobe", Side.E, 0.4f);
        Center(1, "Walk-in closet", "clothes_002|clothes_001", 0.5f, 0.5f, 0f);

        // Corridor
        Wall(1, "Corridor", "Painting Small 3|Painting Small", Side.S, 0.4f, 1.45f);

        // Landing: window seat sofa + reading lamp.
        Wall(1, "Landing", "Sofa 1|Sofa Alt|Sofa", Side.S, 0.5f);
        Wall(1, "Landing", "Lamp 2|Lamp 1", Side.S, 0.05f);
        Wall(1, "Landing", "Plant 2|Plant 1", Side.S, 0.97f);

        // Your bedroom: student / streamer - homework desk, PC, green screen, posters.
        var yourBed = Wall(1, "Your bedroom", "1P Bed|1P Bed 2", Side.W, 0.98f);
        foreach (var stand in Beside(yourBed, "Bed Table", -1)) On(stand, "Small Lamp 3|Small Lamp 1", 0.5f, 0.5f);
        Above(yourBed, "Poster 1|Poster 2", 0.45f);
        var desk = Wall(1, "Your bedroom", "Desk Mechanical 1|Desk Modern|Desk 1", Side.S, 0.5f);
        taskObjects.desk = desk;
        if (desk)
        {
            On(desk, "Monitor 4 Curved", 0.5f, 0.25f);
            On(desk, "PC Keyboard 3|PC Keyboard", 0.5f, 0.62f);
            On(desk, "Headset 1", 0.9f, 0.4f);
            On(desk, "Desk Lamp", 0.06f, 0.3f);
            Seat(desk, "Gaming Chair 2|Gaming Chair 1|Office Chair");
        }
        Wall(1, "Your bedroom", "PC Case 4", Side.S, 0.05f, snug: true);
        Wall(1, "Your bedroom", "Green Screen", Side.E, 0.95f);
        Wall(1, "Your bedroom", "Speaker 2", Side.E, 0.05f);
        Center(1, "Your bedroom", "Rug Tile", 0.55f, 0.55f, 0f);

        // Bathroom (shared by you and your sibling)
        Wall(1, "Bathroom", "Bathtub-Shower|Bathtub", Side.W, 0.5f);
        Wall(1, "Bathroom", "Toilet", Side.S, 0.08f);
        var bathSink = Wall(1, "Bathroom", "Sink", Side.E, 0.2f);
        if (!taskObjects.leakSink) taskObjects.leakSink = bathSink;
        Above(bathSink, "Mirror|Mirror 2", 0.3f);
        On(bathSink, "Toothbrush", 0.3f, 0.5f);
        Wall(1, "Bathroom", "Towel (Wall)", Side.N, 0.15f, 1.15f);

        // Office: Mom works from home - desk by the window, files, whiteboard.
        var momDesk = Wall(1, "Office", "Full Desk 1|Desk 3 (Modern)|Desk Modern 2|Desk", Side.W, 0.5f);
        if (momDesk) Seat(momDesk, "Office Chair|Chair");
        Wall(1, "Office", "File Cabinet Medium|File Cabinet Small", Side.S, 0.1f);
        Wall(1, "Office", "File Cabinet Small|File Cabinet Medium", Side.S, 0.3f, snug: true);
        Wall(1, "Office", "Whiteboard (Wall)|Small Whiteboard", Side.S, 0.65f, 1.1f);
        Wall(1, "Office", "Bookshelf|Library", Side.N, 0.4f);
        Wall(1, "Office", "Plant 2|Plant 5", Side.E, 0.05f);
        Wall(1, "Office", "Lamp 2|Lamp 1", Side.N, 0.92f);

        // Library: walls of books, two armchairs facing each other, a reading lamp.
        Wall(1, "Library", "Library|Bookshelf", Side.N, 0.5f);
        Wall(1, "Library", "Bookshelf|Library", Side.W, 0.05f);
        Wall(1, "Library", "Bookshelf (Locked)|Bookshelf", Side.E, 0.05f);
        Center(1, "Library", "Rug A|Rug Tile", 0.5f, 0.55f, 0f);
        Center(1, "Library", "lounge_chair_001|Chair 2", 0.25f, 0.62f, 90f);
        Center(1, "Library", "Chair 2|lounge_chair_001", 0.75f, 0.62f, -90f);
        var libTable = Center(1, "Library", "Coffee Table|coffee_table_001", 0.5f, 0.62f, 90f);
        On(libTable, "Pile of Books|Book Pile 2", 0.5f, 0.5f);
        Wall(1, "Library", "Lamp 1|Lamp 2", Side.N, 0.05f);

        // Kid's room: bed, toys, desk, a big rug.
        var kidBed = Wall(1, "Kid's room", "1P Bed|1P Bed 2", Side.N, 0.05f);
        foreach (var stand in Beside(kidBed, "Bed Table", 1)) On(stand, "Small Lamp 1", 0.5f, 0.5f);
        Above(kidBed, "Poster 2|Painting Small 1", 0.4f);
        Wall(1, "Kid's room", "Drawer 4|Drawer A", Side.E, 0.1f);
        var kidDesk = Wall(1, "Kid's room", "Desk 1|Simple Desk", Side.S, 0.25f);
        if (kidDesk) { On(kidDesk, "Book (Open)|Book", 0.5f, 0.5f); Seat(kidDesk, "Chair|Stool"); }
        Center(1, "Kid's room", "Rug Tile", 0.5f, 0.5f, 0f);
        Center(1, "Kid's room", "Toy House", 0.62f, 0.35f, 200f);
        Center(1, "Kid's room", "Toy Car 1", 0.45f, 0.42f, 30f);
        Center(1, "Kid's room", "Toy Figure 1", 0.55f, 0.55f, 0f);
        Center(1, "Kid's room", "toy_002", 0.4f, 0.6f, 70f);
        Center(1, "Kid's room", "Puff|Puff Variant", 0.78f, 0.6f, 0f);
        Wall(1, "Kid's room", "Fan", Side.W, 0.05f);
        // The laundry pile (hidden until the laundry task starts; the cat likes to nap on it).
        taskObjects.clothes = Center(1, "Kid's room", "clothes_001|clothes_002", 0.72f, 0.28f, 25f);
        taskObjects.clothesRoom = "in the kid's room";

        // ================= Top floor =================

        // Landing: a quiet corner.
        Wall(2, "Landing", "Big Painting|Painting 2", Side.W, 0.2f, 1.4f);
        Center(2, "Landing", "Puff Variant|Puff", 0.25f, 0.35f, 0f);
        Wall(2, "Landing", "Plant 1|Plant 2", Side.S, 0.05f);

        // Balcony
        Wall(2, "Balcony", "Outside Sofa|Outside Sofa (Alt)|Chair (Outside)", Side.N, 0.5f);
        Center(2, "Balcony", "Simple Table (Outside - Small)", 0.5f, 0.45f, 0f);
        Center(2, "Balcony", "Plant 3", 0.92f, 0.12f, 0f);

        // Home cinema: big screen, two rows of sofas, speakers.
        var cinema = CinemaScreen(2, "Home cinema", Side.S, 2.9f, 1.62f, 0.75f);
        if (cinema) taskObjects.tvs.Add((cinema, "Home cinema screen", cinema.transform.Find("Screen")?.GetComponent<Renderer>()));
        Wall(2, "Home cinema", "Speaker 4|Speaker 3", Side.S, 0.0f);
        Wall(2, "Home cinema", "Speaker 4|Speaker 3", Side.S, 1.0f);
        Center(2, "Home cinema", "Sofa 2|Sofa", 0.5f, 0.5f, 180f);
        Center(2, "Home cinema", "Sofa|Sofa 1|Sofa 2", 0.42f, 0.8f, 180f);
        Center(2, "Home cinema", "Puff", 0.2f, 0.3f, 0f);
        var projectorShelf = Wall(2, "Home cinema", "Wall Shelf 4|Wall Shelf", Side.N, 0.5f, 1.9f);
        On(projectorShelf, "Projector", 0.5f, 0.5f, 180f);

        // Game room: arcade + PC corner + pool table + a snack corner.
        var pool = Center(2, "Game room", "Pool Table", 0.66f, 0.5f, 0f);
        Center(2, "Game room", "air_hockey_001", 0.22f, 0.32f, 90f);
        var gameDesk = Wall(2, "Game room", "Desk L|Desk Modern", Side.N, 0.08f);
        if (gameDesk)
        {
            On(gameDesk, "Monitor 4 Curved", 0.4f, 0.25f);
            On(gameDesk, "PC Keyboard 1|PC Keyboard", 0.4f, 0.62f);
            Seat(gameDesk, "Gaming Chair 1|Gaming Chair 2");
        }
        Wall(2, "Game room", "fridge_001|Fridge", Side.E, 0.05f);
        var snack = Wall(2, "Game room", "Counter (Separated)|Counter 1", Side.E, 0.32f, snug: true);
        On(snack, "drink_001", 0.3f, 0.5f);
        On(snack, "drink_002", 0.6f, 0.5f);
        Wall(2, "Game room", "Glass Expositor x1", Side.N, 0.6f);
        Wall(2, "Game room", "Poster 1|Poster 2", Side.W, 0.5f, 1.6f);
        var gameTv = Wall(2, "Game room", "TV 5 Mounted", Side.N, 0.35f, 1.3f);
        if (gameTv) taskObjects.tvs.Add((gameTv, "Game room TV", null));
        Center(2, "Game room", "Puff", 0.3f, 0.75f, 0f);
        Wall(2, "Game room", "Speaker 2", Side.N, 0.98f);
        if (pool) Center(2, "Game room", "Rug Tile", 0.66f, 0.5f, 0f);

    }

    // ---- filling rooms that still feel empty

    static readonly Dictionary<Room, int> wallArt = new Dictionary<Room, int>();

    /// <summary>
    /// After the hand-made layout: top up any room whose furniture covers less of the floor than a lived-in
    /// room of its kind would (storage, plants, lamps, seats in the corners and along free walls), and give
    /// bare walls some art. Logs how full each room ended up.
    /// </summary>
    static void FillEmptyRooms()
    {
        var report = new List<string>();
        foreach (var room in rooms)
        {
            if (room.kind == Kind.Stairs || room.kind == Kind.Outdoor || room.kind == Kind.Garage) continue;
            float target = room.kind switch
            {
                Kind.Hall => Mathf.Min(room.x1 - room.x0, room.z1 - room.z0) >= 4 ? 0.08f : 0.03f, // foyer/landings are rooms, corridors aren't
                Kind.Bath => 0.15f,
                Kind.Service => 0.18f,
                _ => 0.18f, // lower than a real room: the hand-made layout already has the pieces that matter
            };
            float before = Coverage(room);
            var fillers = Fillers(room);
            int start = Mathf.Abs(room.name.GetHashCode()) % 4;
            for (int i = 0; i < fillers.Length && Coverage(room) < target; i++)
                Wall(room.floor, room.name, fillers[i], (Side)((start + i) % 4), i % 2 == 0 ? 0.08f : 0.92f, quiet: true);

            var art = Art(room);
            wallArt.TryGetValue(room, out int have);
            for (int i = 0; have < art.count && i < art.prefabs.Length; i++)
                if (Wall(room.floor, room.name, art.prefabs[i], (Side)((start + 2 + i) % 4), 0.5f, 1.45f, quiet: true)) have++;

            report.Add($"{FloorName(room.floor)} - {room.name}: {before * 100f:0}% -> {Coverage(room) * 100f:0}% (target {target * 100f:0}%)");
        }
        Debug.Log("[Build New House] Room fill (furniture floor area / usable floor):\n" + string.Join("\n", report));
    }

    static float Coverage(Room room)
    {
        var area = new Rect(room.x0 + 0.1f, room.z0 + 0.1f, room.x1 - room.x0 - 0.2f, room.z1 - room.z0 - 0.2f);
        float covered = 0f;
        foreach (var (f, r, owner) in footprints)
        {
            if (f != room.floor || !owner) continue;
            float w = Mathf.Min(r.xMax, area.xMax) - Mathf.Max(r.xMin, area.xMin);
            float d = Mathf.Min(r.yMax, area.yMax) - Mathf.Max(r.yMin, area.yMin);
            if (w > 0f && d > 0f) covered += w * d;
        }
        return covered / Mathf.Max(0.01f, area.width * area.height);
    }

    static string[] Fillers(Room room) => room.name switch
    {
        "Music room" => new[] { "Speaker 2|Speaker 3", "Bookshelf", "Plant 3|Plant 2", "Chair 2|lounge_chair_001", "Lamp 1|Lamp 2" },
        "Playroom" => new[] { "Shelf - Box|Simple Shelf", "Puff Variant|Puff", "Plant 2", "Drawer A", "Toy House" },
        "Office" => new[] { "Bookshelf|Library", "File Cabinet Small", "Plant 5|Plant 2", "Chair 2", "Simple Shelf" },
        "Library" => new[] { "Bookshelf", "Library", "Plant 4|Plant 1", "Lamp 2|Lamp 1", "Puff" },
        "Home cinema" => new[] { "Speaker 3|Speaker 2", "Drawer B|Drawer A", "Puff Variant|Puff", "Plant 1" },
        "Game room" => new[] { "Shelf - Box|Shelf", "Puff Variant|Puff", "Speaker 4|Speaker 2", "Plant 2|Plant 3", "Stool 1|Stool", "Glass Expositor x1" },
        "Kitchen" => new[] { "Counter (Connected)|Counter 1", "fridge_001", "Plant 1" },
        "Dining" => new[] { "Drawer B|Drawer A", "Plant 3|Plant 2", "Simple Shelf|Shelf - Simple", "Lamp 1" },
        "Pantry" => new[] { "Shelf - Box|Shelf", "Modern Shelves Variant|Modern Shelves", "box_001|Small Box", "Plastic Basket" },
        "Foyer" => new[] { "Drawer B|Drawer A", "Plant Deco 1|Plant 3", "Chair 2|Stool 1", "Plant 2" },
        "Landing" => new[] { "Bookshelf|Simple Shelf", "Puff|Puff Variant", "Plant 3|Plant 1", "Chair 2|lounge_chair_001", "Drawer B" },
        "Mudroom" => new[] { "Shelf - Box|Shelf", "Plastic Basket", "Drawer A" },
        "Walk-in closet" => new[] { "Drawer 4|Drawer A", "Wardrobe|closet_001", "Simple Shelf", "Puff" },
        _ => room.kind switch
        {
            Kind.Living => new[] { "Plant Deco 1|Plant 3", "Bookshelf|Modern Shelves", "Drawer B|Drawer A", "Lamp 1|Lamp 2", "Chair 2|lounge_chair_001", "Plant 5|Plant 2" },
            Kind.Bedroom => new[] { "Drawer 4|Drawer A", "Wardrobe|closet_001", "Bookshelf|Simple Shelf", "Plant 4|Plant 1", "Lamp 2|Lamp 1", "Puff|Puff Variant", "Chair 2" },
            Kind.Fun => new[] { "Shelf - Box|Shelf", "Puff|Puff Variant", "Plant 2|Plant 3", "Lamp 1" },
            Kind.Bath => new[] { "Plastic Basket", "Plant 1|Plant 5", "Simple Shelf|Shelf - Simple" },
            Kind.Service => new[] { "Shelf - Box|Shelf", "Simple Shelf|Shelf - Simple", "Modern Shelves Variant|Modern Shelves", "Plastic Basket", "box_001|Small Box" },
            Kind.Hall => new[] { "Plant Deco 1|Plant 2", "Bed Table|Drawer A", "Plant 3|Plant 1" },
            _ => new string[0],
        },
    };

    static (int count, string[] prefabs) Art(Room room) => room.name switch
    {
        "Your bedroom" or "Game room" => (2, new[] { "Poster 1|Poster 2", "Poster 2|Poster 1", "Painting Small 4" }),
        "Kid's room" or "Playroom" => (2, new[] { "Poster 2|Poster 1", "Painting Small 1|Small Painting", "Painting Small 2" }),
        "Home cinema" => (1, new[] { "Poster 1|Poster 2" }),
        _ => room.kind switch
        {
            Kind.Living or Kind.Fun => (2, new[] { "Painting 2|Painting 1", "Painting Small 1|Painting Small 2", "Big Painting", "Painting Small 3" }),
            Kind.Bedroom => (2, new[] { "Painting 3|Painting 2", "Painting Small 3|Painting Small 4", "Small Painting 2" }),
            Kind.Hall => (1, new[] { "Painting Small|Painting Small Alt", "Painting Small 2" }),
            _ => (0, new string[0]),
        },
    };

    // ---- simple props built from primitives (no free model needed): the task tools

    static readonly HashSet<string> Props = new HashSet<string> { "Fire Extinguisher", "Mop", "Bucket" };

    /// <summary>A small prop made of primitives, pivot at the bottom centre, front = +Z. No colliders (placement adds a box).</summary>
    static GameObject Prop(string name, Transform parent)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var t = go.transform;
        switch (name)
        {
            case "Fire Extinguisher":
            {
                var red = Mat("Extinguisher Red", new Color(0.8f, 0.08f, 0.06f), 0.55f);
                var black = Mat("Extinguisher Black", new Color(0.08f, 0.08f, 0.08f), 0.3f);
                PropPart(PrimitiveType.Cylinder, "Body", t, new Vector3(0f, 0.25f, 0f), new Vector3(0.17f, 0.25f, 0.17f), red);
                PropPart(PrimitiveType.Sphere, "Shoulder", t, new Vector3(0f, 0.5f, 0f), new Vector3(0.17f, 0.08f, 0.17f), red);
                PropPart(PrimitiveType.Cylinder, "Valve", t, new Vector3(0f, 0.56f, 0f), new Vector3(0.06f, 0.035f, 0.06f), black);
                PropPart(PrimitiveType.Cube, "Handle", t, new Vector3(0.035f, 0.61f, 0f), new Vector3(0.14f, 0.02f, 0.035f), black);
                PropPart(PrimitiveType.Cube, "Hose", t, new Vector3(-0.08f, 0.4f, 0f), new Vector3(0.022f, 0.3f, 0.022f), black);
                PropPart(PrimitiveType.Cube, "Label", t, new Vector3(0f, 0.27f, 0.083f), new Vector3(0.09f, 0.12f, 0.005f), Mat("Extinguisher Label", new Color(0.95f, 0.93f, 0.85f), 0.1f));
                break;
            }
            case "Mop":
            {
                var wood = Mat("Mop Handle", new Color(0.55f, 0.38f, 0.2f), 0.2f);
                var head = Mat("Mop Head", new Color(0.86f, 0.86f, 0.8f), 0.05f);
                var blue = Mat("Bucket Blue", new Color(0.15f, 0.4f, 0.8f), 0.4f);
                PropPart(PrimitiveType.Cylinder, "Handle", t, new Vector3(0f, 0.66f, 0f), new Vector3(0.03f, 0.56f, 0.03f), wood);
                PropPart(PrimitiveType.Cube, "Clamp", t, new Vector3(0f, 0.1f, 0f), new Vector3(0.12f, 0.04f, 0.06f), blue);
                PropPart(PrimitiveType.Cube, "Head", t, new Vector3(0f, 0.04f, 0f), new Vector3(0.36f, 0.08f, 0.14f), head);
                break;
            }
            case "Bucket":
            {
                var blue = Mat("Bucket Blue", new Color(0.15f, 0.4f, 0.8f), 0.4f);
                var dark = Mat("Extinguisher Black", new Color(0.08f, 0.08f, 0.08f), 0.3f);
                PropPart(PrimitiveType.Cylinder, "Body", t, new Vector3(0f, 0.17f, 0f), new Vector3(0.32f, 0.17f, 0.32f), blue);
                PropPart(PrimitiveType.Cylinder, "Rim", t, new Vector3(0f, 0.34f, 0f), new Vector3(0.34f, 0.012f, 0.34f), blue);
                PropPart(PrimitiveType.Cube, "Handle", t, new Vector3(0f, 0.42f, 0f), new Vector3(0.32f, 0.015f, 0.015f), dark);
                PropPart(PrimitiveType.Cube, "Handle L", t, new Vector3(-0.16f, 0.38f, 0f), new Vector3(0.015f, 0.08f, 0.015f), dark);
                PropPart(PrimitiveType.Cube, "Handle R", t, new Vector3(0.16f, 0.38f, 0f), new Vector3(0.015f, 0.08f, 0.015f), dark);
                break;
            }
        }
        return go;
    }

    static void PropPart(PrimitiveType shape, string name, Transform parent, Vector3 localPos, Vector3 localScale, Material mat)
    {
        var part = GameObject.CreatePrimitive(shape);
        part.name = name;
        Object.DestroyImmediate(part.GetComponent<Collider>());
        part.transform.SetParent(parent, false);
        part.transform.localPosition = localPos;
        part.transform.localScale = localScale;
        part.GetComponent<MeshRenderer>().sharedMaterial = mat;
    }

    // ------------------------------------------------------------------ your room (redone on its own, without rebuilding the house)

    const string PlayerRoomName = "Your bedroom";
    static readonly HashSet<string> WindowDressingNames = new HashSet<string> { "Curtains (Open Side)", "Blinds (Open)", "Curtain rod", "Rod end" };

    /// <summary>
    /// Menu: G7l6 > Redo Your Room (Gaming Room). Turns your bedroom into the player's gaming room - battle-station
    /// desk (homework is done here), console corner, collectibles, LED lighting - in the saved NewHouse scene.
    /// Only that room changes; everything else (including your own edits) stays. The old furniture is kept,
    /// hidden, under "Old furniture (backup)" in the room. Safe to run again.
    /// </summary>
    [MenuItem("G7l6/Redo Your Room (Gaming Room)")]
    public static void RedoPlayerRoomMenu()
    {
        if (EditorUtility.DisplayDialog("Redo your room", "Turn your bedroom into the gaming room? Only that room changes; the rest of the house stays as it is.", "Do it", "Cancel"))
            RedoPlayerRoom();
    }

    public static void RedoPlayerRoom()
    {
        if (EditorApplication.isPlaying) { Debug.LogWarning("[Your room] Stop Play mode first."); return; }
        var scene = SceneManager.GetActiveScene();
        if (scene.path != TargetScene)
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            scene = EditorSceneManager.OpenScene(TargetScene, OpenSceneMode.Single);
        }
        var house = scene.GetRootGameObjects().FirstOrDefault(g => g.name == "NewHouse");
        if (!house) { Debug.LogWarning("[Your room] No 'NewHouse' in the scene."); return; }

        missing.Clear(); prefabCache.Clear(); prefabIndex = null; placed = 0; skipped = 0;
        clearZones.Clear(); footprints.Clear(); windowZones.Clear(); roomOfGroup.Clear(); wallArt.Clear();
        wallMats.Clear(); floorMats.Clear(); doors.Clear(); mapWalls.Clear();
        taskObjects = new HouseTaskSetup.HouseTaskObjects();
        Materials();
        DefinePlan();
        root = house.transform;
        structure = root.Find("Structure");
        furnitureRoot = root.Find("Furniture");
        foreach (var r in rooms) r.furniture = furnitureRoot ? furnitureRoot.Find($"{FloorName(r.floor)} - {r.name}") : null;
        var room = Find(1, PlayerRoomName);
        if (!room.furniture) { Debug.LogWarning($"[Your room] Couldn't find 'First - {PlayerRoomName}' under NewHouse/Furniture."); return; }
        var layout = house.GetComponent<HouseLayout>();

        // Work at plan size, like the builder does, then put the house scale back.
        Vector3 houseScale = root.localScale;
        root.localScale = Vector3.one;
        Physics.SyncTransforms();
        var oldLights = new HashSet<Light>(room.furniture.GetComponentsInChildren<Light>(true));
        GameObject desk = null;
        try
        {
            ClearRoomKeepingWindows(room);
            ClearancesFromLayout(room, layout);
            desk = GamingRoom(room);
            MoodLighting(room);
            if (layout) UpdateMapFurniture(layout, room);
        }
        finally
        {
            root.localScale = houseScale;
            Physics.SyncTransforms();
        }
        foreach (var l in room.furniture.GetComponentsInChildren<Light>(true))
            if (!oldLights.Contains(l)) l.range *= houseScale.x;

        string homework = HouseTaskSetup.SetUpHomeworkDesk(desk);
        HouseTaskSetup.PrepareCatForHouse();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        string message = $"Your room is now a gaming room: {placed} pieces.\n{homework}";
        if (skipped > 0) message += $"\n{skipped} pieces didn't fit (see the Console).";
        if (missing.Count > 0) message += "\nMissing prefabs: " + string.Join(", ", missing.Distinct());
        message += "\n\nThe old furniture is hidden under 'Old furniture (backup)' in the room. The scene is saved.";
        Debug.Log("[Your room] " + message);
        Selection.activeObject = room.furniture.gameObject;
    }

    /// <summary>Old furniture goes into a hidden backup (first run) or away (later runs). Curtains and blinds stay.</summary>
    static void ClearRoomKeepingWindows(Room room)
    {
        var backup = room.furniture.Find("Old furniture (backup)");
        bool firstTime = !backup;
        if (firstTime)
        {
            backup = new GameObject("Old furniture (backup)").transform;
            backup.SetParent(room.furniture, false);
        }
        foreach (var child in room.furniture.Cast<Transform>().ToList())
        {
            if (child == backup) continue;
            if (WindowDressingNames.Contains(child.name))
            {
                if (child.name.StartsWith("Curtains"))
                {
                    var b = Bounds(child.gameObject);
                    footprints.Add((room.floor, new Rect(b.min.x, b.min.z, b.size.x, b.size.z), child.gameObject));
                }
                continue;
            }
            if (firstTime) child.SetParent(backup, true);
            else Object.DestroyImmediate(child.gameObject);
        }
        backup.gameObject.SetActive(false);
        // The old desk's homework (and anything you could pick up) must not count any more.
        foreach (var t in backup.GetComponentsInChildren<TaskBase>(true)) Object.DestroyImmediate(t);
        foreach (var c in backup.GetComponentsInChildren<Carryable>(true)) Object.DestroyImmediate(c);
    }

    /// <summary>Doors and windows of the room from the saved floor plan: keep the doorways walkable and the glass clear.</summary>
    static void ClearancesFromLayout(Room room, HouseLayout layout)
    {
        if (!layout) return;
        foreach (var w in layout.walls)
        {
            if (w.floor != room.floor || (w.type != HouseLayout.WallType.Door && w.type != HouseLayout.WallType.Window)) continue;
            bool alongX = Mathf.Approximately(w.a.y, w.b.y);
            int line = Mathf.RoundToInt(alongX ? w.a.y : w.a.x);
            int start = Mathf.RoundToInt(alongX ? Mathf.Min(w.a.x, w.b.x) : Mathf.Min(w.a.y, w.b.y));
            bool onEdge = alongX
                ? (line == room.z0 || line == room.z1) && start >= room.x0 && start + 2 <= room.x1
                : (line == room.x0 || line == room.x1) && start >= room.z0 && start + 2 <= room.z1;
            if (!onEdge) continue;
            var s = new Segment { floor = room.floor, alongX = alongX, line = line, start = start };
            if (w.type == HouseLayout.WallType.Door) KeepClear(room.floor, s, 1.2f, 1.0f);
            else KeepWindowClear(room, s, null);
        }
    }

    /// <summary>
    /// The player's room (4 x 6 m; door in from the landing on the west, bathroom door in the north-east corner,
    /// windows south and east). Battle-station in the south-west corner, console corner south-east,
    /// bed and collectibles along the north, LED strip round the ceiling. Returns the homework desk.
    /// </summary>
    static GameObject GamingRoom(Room room)
    {
        const int f = 1;
        const string R = PlayerRoomName;

        // Walls a deep gamer indigo (only this room uses this paint).
        Mat("Wall - Slate Blue", new Color(0.24f, 0.25f, 0.4f), 0.06f);

        // --- Battle-station: big desk against the south wall, in the corner; you sit facing the wall.
        var desk = Wall(f, R, "Desk Mechanical 2|Desk Mechanical 1|Desk Modern 2|Desk Modern", Side.S, 0f);
        if (desk)
        {
            var main = On(desk, "Monitor 4 Curved|Monitor 3", 0.42f, 0.22f);
            On(desk, "Monitor 2|Monitor 1", 0.93f, 0.25f, -28f);
            if (main) On(main, "Web Cam 1|Web Cam 2", 0.5f, 0.5f);
            On(desk, "PC Mouse Pad", 0.55f, 0.68f);
            On(desk, "PC Keyboard 3|PC Keyboard 1", 0.42f, 0.66f);
            On(desk, "PC Mouse 1|PC Mouse 2", 0.7f, 0.68f);
            On(desk, "Headset 2|Headset 1", 0.04f, 0.45f, 20f);
            On(desk, "Mic", 0.14f, 0.3f, 15f);
            On(desk, "Soda Alt 1|Soda 1", 0.97f, 0.85f);
            Seat(desk, "Gaming Chair 3|Gaming Chair 1|Gaming Chair 2");
            var shelf = Above(desk, "Wall Shelf 5|Wall Shelf 4|Wall Shelf 2", 0.55f);
            if (shelf)
            {
                On(shelf, "Small Expositor 1|Small Expositor 2", 0.2f, 0.5f);
                On(shelf, "Game Case", 0.5f, 0.5f);
                On(shelf, "Small Expositor 5|Small Expositor 3", 0.8f, 0.5f);
            }
            Underglow(desk, Glow("Neon Cyan", new Color(0.2f, 0.9f, 1f), 2.5f));
        }
        Wall(f, R, "PC Case 7|PC Case 4|PC Case 1", Side.S, 0.42f, snug: true); // the tower, RGB side out
        Wall(f, R, "Poster 3|Poster 1", Side.W, 0.12f, 1.45f);

        // --- Console corner: TV on the east wall, console on a low cabinet, bean bags.
        Wall(f, R, "TV 3 Curved Mounted|TV 4 Mounted|TV 5 Mounted", Side.E, 0.18f, 1.25f);
        var cabinet = Wall(f, R, "Mini Drawers|Shelves", Side.E, 0.18f);
        if (cabinet)
        {
            On(cabinet, "Console PolyStation 5|Console Zbox 720", 0.3f, 0.5f, 0f);
            On(cabinet, "Console PolyStation Controller|Console Zbox Controller", 0.75f, 0.6f, 25f);
        }
        Center(f, R, "Puff Variant|Puff", 0.62f, 0.13f, -90f);
        Center(f, R, "Puff|Puff Variant", 0.62f, 0.3f, -90f);
        Center(f, R, "Rug Tile|Rug A", 0.62f, 0.2f, 0f);
        Wall(f, R, "Speaker 4|Speaker 3", Side.E, 0.02f);

        // --- Bed along the north wall (head in the corner by the landing door wall), posters above.
        var bed = Wall(f, R, "1P Bed 2|1P Bed", Side.N, 0f);
        foreach (var stand in Beside(bed, "Bed Table|Mini Drawers", 1)) On(stand, "Small Lamp 3|Small Lamp 1", 0.5f, 0.5f);
        Above(bed, "Poster 1|Poster 2", 0.4f);
        Wall(f, R, "Wall Light 2|Wall Light 1", Side.W, 0.85f, 1.7f);

        // --- Collectibles and a tube light by the bathroom door wall.
        Wall(f, R, "Glass Expositor x1|Glass Expositor x1 2|Small Expositor 3", Side.N, 0.55f);
        Wall(f, R, "Tube Lamp|Ring Lamp", Side.E, 0.62f);
        Wall(f, R, "Poster 2|Poster 1", Side.E, 0.85f, 1.5f);
        Center(f, R, "Rug A|Rug Tile", 0.4f, 0.55f, 0f);
        return desk;
    }

    /// <summary>Purple LED strip round the ceiling, coloured accent lights, and a softer purple ceiling light.</summary>
    static void MoodLighting(Room room)
    {
        var neon = Glow("Neon Purple", new Color(0.62f, 0.28f, 1f), 2.4f);
        float y = room.Base + Storey - 0.16f, inset = 0.14f, t = 0.03f;
        float w = room.x1 - room.x0 - inset * 2f, d = room.z1 - room.z0 - inset * 2f;
        float cx = (room.x0 + room.x1) * 0.5f, cz = (room.z0 + room.z1) * 0.5f;
        Box("LED strip S", room.furniture, new Vector3(cx, y, room.z0 + inset), new Vector3(w, t, t), neon, false);
        Box("LED strip N", room.furniture, new Vector3(cx, y, room.z1 - inset), new Vector3(w, t, t), neon, false);
        Box("LED strip W", room.furniture, new Vector3(room.x0 + inset, y, cz), new Vector3(t, t, d), neon, false);
        Box("LED strip E", room.furniture, new Vector3(room.x1 - inset, y, cz), new Vector3(t, t, d), neon, false);

        AccentLight(room, "Purple glow (desk)", new Vector3(room.x0 + 1.1f, room.Base + 2.3f, room.z0 + 0.9f), new Color(0.65f, 0.35f, 1f), 0.9f, 3.6f);
        AccentLight(room, "Cyan glow (TV)", new Vector3(room.x1 - 0.8f, room.Base + 1.6f, room.z0 + 1.2f), new Color(0.3f, 0.85f, 1f), 0.7f, 3.2f);
        AccentLight(room, "Pink glow (bed)", new Vector3(room.x0 + 1.2f, room.Base + 2.2f, room.z1 - 0.9f), new Color(1f, 0.4f, 0.75f), 0.5f, 3f);

        // The room's ceiling light: a little dimmer and cooler, so the LEDs show.
        var lights = root.Find("Lights");
        if (!lights) return;
        foreach (var l in lights.GetComponentsInChildren<Light>(true))
            if (l.name == $"Light - {FloorName(room.floor)} {room.name}")
            {
                l.color = new Color(0.86f, 0.84f, 1f);
                l.intensity = 0.75f;
            }
    }

    static void AccentLight(Room room, string name, Vector3 at, Color color, float intensity, float range)
    {
        var light = new GameObject(name).AddComponent<Light>();
        light.transform.SetParent(room.furniture, true);
        light.transform.position = at;
        light.type = LightType.Point;
        light.color = color;
        light.intensity = intensity;
        light.range = range;
        light.shadows = LightShadows.None;
    }

    /// <summary>A glowing strip under the front edge of a desk, plus a little light under it.</summary>
    static void Underglow(GameObject desk, Material glow)
    {
        var b = Bounds(desk);
        Vector3 fwd = desk.transform.forward;
        fwd.y = 0f;
        fwd = Mathf.Abs(fwd.x) > Mathf.Abs(fwd.z) ? new Vector3(Mathf.Sign(fwd.x), 0f, 0f) : new Vector3(0f, 0f, Mathf.Sign(fwd.z));
        float reach = Vector3.Dot(b.extents, Abs(fwd)) - 0.04f;
        float length = Vector3.Dot(b.size, new Vector3(Mathf.Abs(fwd.z), 0f, Mathf.Abs(fwd.x))) * 0.9f;
        Vector3 at = new Vector3(b.center.x, b.max.y - 0.07f, b.center.z) + fwd * reach;
        Vector3 size = fwd.x != 0f ? new Vector3(0.02f, 0.02f, length) : new Vector3(length, 0.02f, 0.02f);
        Box("Desk underglow", desk.transform.parent, at, size, glow, false);
        var light = new GameObject("Desk underglow light").AddComponent<Light>();
        light.transform.SetParent(desk.transform.parent, true);
        light.transform.position = new Vector3(b.center.x, b.min.y + 0.35f, b.center.z) + fwd * (reach - 0.2f);
        light.type = LightType.Point;
        light.color = new Color(0.25f, 0.85f, 1f);
        light.intensity = 1.1f;
        light.range = 1.6f;
        light.shadows = LightShadows.None;
    }

    static Material Glow(string name, Color color, float strength)
    {
        var m = Mat(name, color, 0.3f);
        m.EnableKeyword("_EMISSION");
        m.SetColor("_EmissionColor", color * strength);
        m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        EditorUtility.SetDirty(m);
        return m;
    }

    /// <summary>The minimap draws furniture from the saved layout: swap this room's old pieces for the new ones.</summary>
    static void UpdateMapFurniture(HouseLayout layout, Room room)
    {
        var area = new Rect(room.x0, room.z0, room.x1 - room.x0, room.z1 - room.z0);
        layout.furniture.RemoveAll(fi => fi.floor == room.floor && area.Contains(fi.rect.center));
        foreach (var (f, rect, owner) in footprints)
        {
            if (f != room.floor || !owner || !area.Contains(rect.center) || WindowDressingNames.Contains(owner.name)) continue;
            layout.furniture.Add(new HouseLayout.FurnitureInfo { floor = f, rect = rect, height = Bounds(owner).size.y });
        }
        EditorUtility.SetDirty(layout);
    }

    // ---- placement with clearance checks

    static readonly Dictionary<Transform, Room> roomOfGroup = new Dictionary<Transform, Room>();
    static Room RoomOf(Transform group)
    {
        if (roomOfGroup.Count == 0) foreach (var r in rooms) roomOfGroup[r.furniture] = r;
        return roomOfGroup.TryGetValue(group, out var room) ? room : null;
    }

    static float YawFor(Side side) => side == Side.S ? 0f : side == Side.N ? 180f : side == Side.W ? 90f : -90f;

    static Side SideFromYaw(float yaw)
    {
        int q = Mathf.RoundToInt(Mathf.Repeat(yaw, 360f) / 90f) % 4;
        return q == 0 ? Side.S : q == 1 ? Side.W : q == 2 ? Side.N : Side.E;
    }

    static Side SideOf(Room room, Segment s) =>
        s.alongX ? (s.line == room.z0 ? Side.S : Side.N) : (s.line == room.x0 ? Side.W : Side.E);

    static Vector3 Inward(Side side) => side == Side.S ? Vector3.forward : side == Side.N ? Vector3.back : side == Side.W ? Vector3.right : Vector3.left;

    /// <summary>Against a wall, facing into the room. Slides along the wall to the nearest free spot; tries the alternatives in "A|B" if A doesn't fit.</summary>
    static GameObject Wall(int f, string roomName, string prefabs, Side side, float t, float y = 0f, bool snug = false, float gap = 0.03f, bool quiet = false)
    {
        var room = Find(f, roomName);
        foreach (var s in SidesFrom(side))
        {
            var go = WallOn(room, prefabs, s, s == side ? t : 0.5f, y, snug, gap);
            if (go) return go;
        }
        return quiet ? null : Skipped(prefabs, roomName);
    }

    static IEnumerable<Side> SidesFrom(Side first)
    {
        yield return first;
        Side opposite = first == Side.S ? Side.N : first == Side.N ? Side.S : first == Side.W ? Side.E : Side.W;
        yield return opposite;
        foreach (Side s in new[] { Side.N, Side.E, Side.S, Side.W })
            if (s != first && s != opposite) yield return s;
    }

    static GameObject WallOn(Room room, string prefabs, Side side, float t, float y, bool snug, float gap)
    {
        foreach (var prefab in prefabs.Split('|'))
        {
            var go = Spawn(prefab, room.furniture);
            if (!go) continue;
            go.transform.rotation = Quaternion.Euler(0f, YawFor(side), 0f);
            var b = Bounds(go);
            bool alongX = side == Side.S || side == Side.N;
            float width = alongX ? b.size.x : b.size.z;
            float depth = alongX ? b.size.z : b.size.x;
            float min = (alongX ? room.x0 : room.z0) + 0.1f, max = (alongX ? room.x1 : room.z1) - 0.1f;
            float lo = min + width * 0.5f, hi = max - width * 0.5f;
            if (lo > hi) lo = hi = (min + max) * 0.5f;
            float desired = Mathf.Lerp(lo, hi, t);
            float plane = side switch
            {
                Side.S => room.z0 + 0.1f + gap + depth * 0.5f,
                Side.N => room.z1 - 0.1f - gap - depth * 0.5f,
                Side.W => room.x0 + 0.1f + gap + depth * 0.5f,
                _ => room.x1 - 0.1f - gap - depth * 0.5f,
            };
            bool mounted = y > 0.8f;
            foreach (float along in Candidates(desired, lo, hi))
            {
                Vector3 target = alongX ? new Vector3(along, room.Base + y, plane) : new Vector3(plane, room.Base + y, along);
                MoveBottomCenter(go, Bounds(go), target);
                if (Fits(room, go, snug ? 0f : 0.12f, null, mounted)) { Commit(room, go, mounted); return go; }
            }
            Object.DestroyImmediate(go);
        }
        return null;
    }

    /// <summary>Free-standing, at (u, v) of the room. Nudges around if that spot is taken.</summary>
    static GameObject Center(int f, string roomName, string prefabs, float u, float v, float yaw, float y = 0f)
    {
        var room = Find(f, roomName);
        Vector3 desired = new Vector3(Mathf.Lerp(room.x0 + 0.1f, room.x1 - 0.1f, u), room.Base + y, Mathf.Lerp(room.z0 + 0.1f, room.z1 - 0.1f, v));
        foreach (var prefab in prefabs.Split('|'))
        {
            var go = Spawn(prefab, room.furniture);
            if (!go) continue;
            go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            if (Place(room, go, desired, 0.3f)) return go;
            Object.DestroyImmediate(go);
        }
        return Skipped(prefabs, roomName);
    }

    static bool Place(Room room, GameObject go, Vector3 desired, float spacing)
    {
        foreach (var offset in Spiral())
        {
            MoveBottomCenter(go, Bounds(go), desired + offset);
            if (Fits(room, go, spacing, null, false)) { Commit(room, go, false); return true; }
        }
        return false;
    }

    /// <summary>Put something on top of another piece (u, v = where on its top, 0..1).</summary>
    static GameObject On(GameObject baseObject, string prefabs, float u, float v, float yaw = 0f)
    {
        if (!baseObject) return null;
        foreach (var prefab in prefabs.Split('|'))
        {
            var go = Spawn(prefab, baseObject.transform.parent);
            if (!go) continue;
            go.transform.rotation = baseObject.transform.rotation * Quaternion.Euler(0f, yaw, 0f);
            var top = Bounds(baseObject);
            var b = Bounds(go);
            Vector3 right = baseObject.transform.right, forward = baseObject.transform.forward;
            Vector3 halfR = right * Mathf.Max(0f, Vector3.Dot(top.extents, Abs(right)) - Vector3.Dot(b.extents, Abs(right)));
            Vector3 halfF = forward * Mathf.Max(0f, Vector3.Dot(top.extents, Abs(forward)) - Vector3.Dot(b.extents, Abs(forward)));
            Vector3 target = new Vector3(top.center.x, top.max.y, top.center.z) + halfR * (u * 2f - 1f) + halfF * (v * 2f - 1f);
            MoveBottomCenter(go, b, target);
            placed++;
            return go;
        }
        return null;
    }

    /// <summary>Wall art / a mirror / a TV centred above a piece that stands against a wall.</summary>
    static GameObject Above(GameObject piece, string prefabs, float gapAbove)
    {
        if (!piece) return null;
        var room = RoomOf(piece.transform.parent);
        if (room == null) return null;
        Side side = SideFromYaw(piece.transform.eulerAngles.y);
        bool alongX = side == Side.S || side == Side.N;
        var pb = Bounds(piece);
        foreach (var prefab in prefabs.Split('|'))
        {
            var go = Spawn(prefab, room.furniture);
            if (!go) continue;
            go.transform.rotation = Quaternion.Euler(0f, YawFor(side), 0f);
            var b = Bounds(go);
            float depth = alongX ? b.size.z : b.size.x;
            float plane = side switch
            {
                Side.S => room.z0 + 0.12f + depth * 0.5f,
                Side.N => room.z1 - 0.12f - depth * 0.5f,
                Side.W => room.x0 + 0.12f + depth * 0.5f,
                _ => room.x1 - 0.12f - depth * 0.5f,
            };
            float along = alongX ? pb.center.x : pb.center.z;
            float bottom = Mathf.Min(pb.max.y + gapAbove, room.Base + Storey - 0.15f - b.size.y);
            MoveBottomCenter(go, b, alongX ? new Vector3(along, bottom, plane) : new Vector3(plane, bottom, along));
            if (Fits(room, go, 0f, null, true)) { Commit(room, go, true); return go; }
            Object.DestroyImmediate(go);
        }
        return Skipped(prefabs, room.name);
    }

    /// <summary>Pieces snug either side of something against a wall (nightstands by a bed). sides: -1 / +1 along the wall.</summary>
    static List<GameObject> Beside(GameObject piece, string prefabs, params int[] sides)
    {
        var result = new List<GameObject>();
        if (!piece) return result;
        var room = RoomOf(piece.transform.parent);
        if (room == null) return result;
        Side side = SideFromYaw(piece.transform.eulerAngles.y);
        bool alongX = side == Side.S || side == Side.N;
        var pb = Bounds(piece);
        float pieceHalf = alongX ? pb.extents.x : pb.extents.z;
        float pieceAlong = alongX ? pb.center.x : pb.center.z;
        foreach (int k in sides)
        {
            GameObject placedHere = null;
            foreach (var prefab in prefabs.Split('|'))
            {
                var go = Spawn(prefab, room.furniture);
                if (!go) continue;
                go.transform.rotation = Quaternion.Euler(0f, YawFor(side), 0f);
                var b = Bounds(go);
                float width = alongX ? b.size.x : b.size.z, depth = alongX ? b.size.z : b.size.x;
                float plane = side switch
                {
                    Side.S => room.z0 + 0.13f + depth * 0.5f,
                    Side.N => room.z1 - 0.13f - depth * 0.5f,
                    Side.W => room.x0 + 0.13f + depth * 0.5f,
                    _ => room.x1 - 0.13f - depth * 0.5f,
                };
                float along = pieceAlong + k * (pieceHalf + width * 0.5f + 0.05f);
                MoveBottomCenter(go, b, alongX ? new Vector3(along, room.Base, plane) : new Vector3(plane, room.Base, along));
                if (Fits(room, go, 0f, piece, false)) { Commit(room, go, false); placedHere = go; break; }
                Object.DestroyImmediate(go);
            }
            if (placedHere) result.Add(placedHere); else Skipped(prefabs, room.name);
        }
        return result;
    }

    /// <summary>A chair in front of a desk (or a stool at the piano), facing it.</summary>
    static GameObject Seat(GameObject desk, string prefabs)
    {
        if (!desk) return null;
        var room = RoomOf(desk.transform.parent);
        foreach (var prefab in prefabs.Split('|'))
        {
            var go = Spawn(prefab, desk.transform.parent);
            if (!go) continue;
            go.transform.rotation = desk.transform.rotation * Quaternion.Euler(0f, 180f, 0f);
            var d = Bounds(desk);
            var b = Bounds(go);
            Vector3 forward = desk.transform.forward;
            float reach = Vector3.Dot(d.extents, Abs(forward)) + Vector3.Dot(b.extents, Abs(forward)) * 0.55f;
            MoveBottomCenter(go, b, new Vector3(d.center.x, d.min.y, d.center.z) + forward * reach);
            if (room == null) { placed++; return go; }
            if (Fits(room, go, 0.05f, desk, false)) { Commit(room, go, false); return go; }
            Object.DestroyImmediate(go);
        }
        return room != null ? Skipped(prefabs, room.name) : null;
    }

    static void ChairsAround(GameObject table, string prefabs)
    {
        if (!table) return;
        var room = RoomOf(table.transform.parent);
        var t = Bounds(table);
        var sides = new[] { (Vector3.back, 0f), (Vector3.forward, 180f), (Vector3.left, 90f), (Vector3.right, -90f) };
        foreach (var (dir, yaw) in sides)
        {
            foreach (var prefab in prefabs.Split('|'))
            {
                var go = Spawn(prefab, table.transform.parent);
                if (!go) continue;
                go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
                var b = Bounds(go);
                float reach = Vector3.Dot(t.extents, Abs(dir)) + Vector3.Dot(b.extents, Abs(dir)) * 0.4f;
                MoveBottomCenter(go, b, new Vector3(t.center.x, t.min.y, t.center.z) + dir * reach);
                if (room == null) { placed++; break; }
                if (Fits(room, go, 0.05f, table, false)) { Commit(room, go, false); break; }
                Object.DestroyImmediate(go);
            }
        }
    }

    static void Car(string model, int f, string roomName, float u, float v)
    {
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>($"{CarFolder}/{model}.fbx");
        if (!asset) { missing.Add($"{model}.fbx (Kenney Car Kit)"); return; }
        var room = Find(f, roomName);
        var go = (GameObject)PrefabUtility.InstantiatePrefab(asset, room.furniture.gameObject.scene);
        go.transform.SetParent(room.furniture, false);
        go.transform.localScale = Vector3.one * 1.4f;
        go.transform.rotation = Quaternion.identity;
        MoveBottomCenter(go, Bounds(go), new Vector3(Mathf.Lerp(room.x0, room.x1, u), room.Base, Mathf.Lerp(room.z0, room.z1, v)));
        Commit(room, go, false);
    }

    /// <summary>Inside the room, out of doorways/archways, nothing tall or hung in front of a window, and not touching other furniture.</summary>
    static bool Fits(Room room, GameObject go, float spacing, GameObject ignore, bool mounted)
    {
        var b = Bounds(go);
        var rect = new Rect(b.min.x, b.min.z, b.size.x, b.size.z);
        if (rect.xMin < room.x0 + 0.07f || rect.xMax > room.x1 - 0.07f || rect.yMin < room.z0 + 0.07f || rect.yMax > room.z1 - 0.07f) return false;
        if (b.size.y < 0.06f) return true; // rugs never block a path
        bool high = b.min.y > room.Base + 2.3f;
        bool tall = b.max.y - room.Base > 1.25f;
        if ((mounted || tall) && !high)
            foreach (var (f, zone) in windowZones)
                if (f == room.floor && rect.Overlaps(zone)) return false;
        foreach (var (f, zone) in clearZones)
            if (f == room.floor && rect.Overlaps(zone)) return mounted && b.min.y > room.Base + 2.1f; // only things high above a doorway may sit over it
        if (mounted) return true;
        var grown = new Rect(rect.xMin - spacing, rect.yMin - spacing, rect.width + spacing * 2f, rect.height + spacing * 2f);
        foreach (var (f, other, owner) in footprints)
            if (f == room.floor && owner != ignore && grown.Overlaps(other)) return false;
        return true;
    }

    static void Commit(Room room, GameObject go, bool mounted)
    {
        placed++;
        if (mounted) { wallArt.TryGetValue(room, out int n); wallArt[room] = n + 1; }
        var b = Bounds(go);
        if (!mounted && b.size.y >= 0.06f) footprints.Add((room.floor, new Rect(b.min.x, b.min.z, b.size.x, b.size.z), go));
        if (!go.GetComponentsInChildren<Collider>().Any(c => !c.isTrigger)) FitBox(go);
    }

    static GameObject Skipped(string prefabs, string roomName)
    {
        Debug.Log($"[Build New House] Left out '{prefabs}' in {roomName}: no free spot that keeps the doors, windows and paths clear.");
        skipped++;
        return null;
    }

    static IEnumerable<float> Candidates(float desired, float lo, float hi)
    {
        yield return desired;
        for (float d = 0.1f; d <= hi - lo + 0.001f; d += 0.1f)
        {
            if (desired + d <= hi + 0.001f) yield return desired + d;
            if (desired - d >= lo - 0.001f) yield return desired - d;
        }
    }

    static IEnumerable<Vector3> Spiral()
    {
        yield return Vector3.zero;
        for (float r = 0.2f; r <= 1.6f; r += 0.2f)
            for (int i = 0; i < 12; i++)
            {
                float a = i * Mathf.PI / 6f;
                yield return new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
            }
    }

    // ---- special pieces

    /// <summary>A ball pit: padded frame + a few hundred coloured balls (no colliders on the balls, you wade through them).</summary>
    static void BallPit(int f, string roomName, float u, float v, float w, float d)
    {
        var room = Find(f, roomName);
        var pit = new GameObject("Ball Pit");
        pit.transform.SetParent(room.furniture, false);
        var pad = Mat("Ball Pit Pad", new Color(0.24f, 0.52f, 0.9f), 0.2f);
        const float h = 0.38f, t = 0.12f;
        Box("Pad S", pit.transform, new Vector3(0f, h * 0.5f, -d * 0.5f + t * 0.5f), new Vector3(w, h, t), pad, true);
        Box("Pad N", pit.transform, new Vector3(0f, h * 0.5f, d * 0.5f - t * 0.5f), new Vector3(w, h, t), pad, true);
        Box("Pad W", pit.transform, new Vector3(-w * 0.5f + t * 0.5f, h * 0.5f, 0f), new Vector3(t, h, d - 2f * t), pad, true);
        Box("Pad E", pit.transform, new Vector3(w * 0.5f - t * 0.5f, h * 0.5f, 0f), new Vector3(t, h, d - 2f * t), pad, true);
        var colours = new[]
        {
            Mat("Ball Red", new Color(0.93f, 0.25f, 0.25f), 0.6f), Mat("Ball Yellow", new Color(0.98f, 0.82f, 0.2f), 0.6f),
            Mat("Ball Green", new Color(0.3f, 0.78f, 0.38f), 0.6f), Mat("Ball Blue", new Color(0.25f, 0.5f, 0.95f), 0.6f),
            Mat("Ball Pink", new Color(0.95f, 0.5f, 0.75f), 0.6f),
        };
        var rng = new System.Random(7);
        int count = Mathf.RoundToInt((w - 2f * t) * (d - 2f * t) * 70f);
        for (int i = 0; i < count; i++)
        {
            var ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            ball.name = "Ball";
            Object.DestroyImmediate(ball.GetComponent<Collider>());
            ball.transform.SetParent(pit.transform, false);
            float r = 0.075f + (float)rng.NextDouble() * 0.01f;
            ball.transform.localScale = Vector3.one * r * 2f;
            ball.transform.localPosition = new Vector3(
                ((float)rng.NextDouble() - 0.5f) * (w - 2f * t - 2f * r),
                r + (float)rng.NextDouble() * (h - r * 1.6f),
                ((float)rng.NextDouble() - 0.5f) * (d - 2f * t - 2f * r));
            ball.GetComponent<MeshRenderer>().sharedMaterial = colours[rng.Next(colours.Length)];
        }
        Vector3 desired = new Vector3(Mathf.Lerp(room.x0 + 0.1f, room.x1 - 0.1f, u), room.Base, Mathf.Lerp(room.z0 + 0.1f, room.z1 - 0.1f, v));
        if (!Place(room, pit, desired, 0.2f)) { Object.DestroyImmediate(pit); Skipped("Ball Pit", roomName); }
    }

    /// <summary>A big projector screen on a wall (screen sits a little proud of its border - no flicker).</summary>
    static GameObject CinemaScreen(int f, string roomName, Side side, float width, float height, float bottom)
    {
        var room = Find(f, roomName);
        var parent = new GameObject("Projector Screen").transform;
        parent.SetParent(room.furniture, false);
        bool alongX = side == Side.S || side == Side.N;
        Vector3 inward = Inward(side);
        float line = side == Side.S ? room.z0 : side == Side.N ? room.z1 : side == Side.W ? room.x0 : room.x1;
        Vector3 wallFace = alongX ? new Vector3((room.x0 + room.x1) * 0.5f, 0f, line) : new Vector3(line, 0f, (room.z0 + room.z1) * 0.5f);
        wallFace += inward * 0.1f + Vector3.up * (room.Base + bottom + height * 0.5f);
        Vector3 size(float w, float h, float thick) => alongX ? new Vector3(w, h, thick) : new Vector3(thick, h, w);
        Box("Screen border", parent, wallFace + inward * 0.015f, size(width + 0.14f, height + 0.14f, 0.03f), Mat("Screen Border", new Color(0.08f, 0.08f, 0.09f), 0.3f), false);
        Box("Screen", parent, wallFace + inward * 0.035f, size(width, height, 0.01f), Mat("Screen White", new Color(0.96f, 0.96f, 0.98f), 0.05f), false);
        placed++;
        return parent.gameObject;
    }

    /// <summary>Warm flickerless glow in front of a fireplace.</summary>
    static void FireGlow(GameObject fireplace)
    {
        var b = Bounds(fireplace);
        Vector3 front = fireplace.transform.forward;
        var light = new GameObject("Fire glow").AddComponent<Light>();
        light.transform.SetParent(fireplace.transform, true);
        light.transform.position = new Vector3(b.center.x, b.min.y + 0.45f, b.center.z) + front * (Vector3.Dot(b.extents, Abs(front)) + 0.35f);
        light.type = LightType.Point;
        light.color = new Color(1f, 0.55f, 0.25f);
        light.intensity = 0.9f;
        light.range = 4.5f;
        light.shadows = LightShadows.None;
    }

    // ---- downloaded models (not prefabs): scaled to real size and turned so their front faces +Z

    static readonly Dictionary<string, (string path, float scale, float width, string front, float fallbackYaw)> ExtraModels =
        new Dictionary<string, (string, float, float, string, float)>
        {
            // Piano_01: keyboard 10.6 units wide -> ~1.45 m. Its keys (white material "FFFFFF") mark the front.
            ["Piano"] = ("Assets/ImportedAssets/Extra/Piano/Piano_01.obj", 0.137f, 0f, "keys:FFFFFF", 90f),
            // Already in metres (2.1 x 1.17); a touch bigger reads better at house scale.
            ["Pool Table"] = ("Assets/ImportedAssets/Extra/PoolTable/PoolTable.obj", 1.08f, 0f, "", 0f),
            // 7.9 units wide -> 1.7 m. The fire opening marks the front.
            ["Fireplace"] = ("Assets/ImportedAssets/Extra/Fireplace/fireplace.obj", 0.215f, 0f, "opening", 0f),
            ["Fireplace (Stone)"] = ("Assets/ImportedAssets/Extra/Fireplace/Fireplace Quaternius.fbx", 0f, 1.55f, "opening", 0f),
        };

    static GameObject SpawnModel(string name, Transform parent)
    {
        var spec = ExtraModels[name];
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(spec.path);
        if (!asset) { missing.Add($"{name} ({spec.path})"); return null; }
        var wrapper = new GameObject(name);
        wrapper.transform.SetParent(parent, false);
        var inner = (GameObject)PrefabUtility.InstantiatePrefab(asset, parent.gameObject.scene);
        inner.transform.SetParent(wrapper.transform, false);
        inner.transform.localPosition = Vector3.zero;
        if (spec.scale > 0f) inner.transform.localScale *= spec.scale;
        else
        {
            var b0 = Bounds(inner);
            float size = Mathf.Max(b0.size.x, b0.size.z);
            if (size > 0.001f) inner.transform.localScale *= spec.width / size;
        }

        // Turn the model so its front faces the wrapper's +Z (the furniture convention).
        Vector3? front = null;
        if (spec.front.StartsWith("keys:")) front = FrontFromMaterial(inner, spec.front.Substring(5));
        else if (spec.front == "opening") front = FrontFromOpening(inner);
        float yaw = front.HasValue ? Vector3.SignedAngle(front.Value, Vector3.forward, Vector3.up) : spec.fallbackYaw;
        inner.transform.localRotation = Quaternion.Euler(0f, Mathf.Round(yaw / 90f) * 90f, 0f) * inner.transform.localRotation;

        var b = Bounds(inner);
        inner.transform.position += wrapper.transform.position - new Vector3(b.center.x, b.min.y, b.center.z);
        return wrapper;
    }

    /// <summary>Which way the parts using a material (e.g. a piano's white keys) sit from the model's centre.</summary>
    static Vector3? FrontFromMaterial(GameObject go, string token)
    {
        var centre = Bounds(go).center;
        Vector3 sum = Vector3.zero; int n = 0;
        foreach (var mr in go.GetComponentsInChildren<MeshRenderer>())
        {
            var mf = mr.GetComponent<MeshFilter>();
            if (!mf || !mf.sharedMesh) continue;
            var mesh = mf.sharedMesh;
            var verts = mesh.vertices;
            var mats = mr.sharedMaterials;
            for (int i = 0; i < mesh.subMeshCount && i < mats.Length; i++)
            {
                if (!mats[i] || !mats[i].name.Contains(token)) continue;
                foreach (int idx in mesh.GetIndices(i)) { sum += mf.transform.TransformPoint(verts[idx]); n++; }
            }
        }
        if (n == 0) return null;
        return SnapFlat(sum / n - centre);
    }

    /// <summary>The side with an opening (rays go deep inside), e.g. a fireplace's firebox.</summary>
    static Vector3? FrontFromOpening(GameObject go)
    {
        var added = new List<Collider>();
        foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
            if (mf.sharedMesh && !mf.GetComponent<Collider>()) { var mc = mf.gameObject.AddComponent<MeshCollider>(); mc.sharedMesh = mf.sharedMesh; added.Add(mc); }
        Physics.SyncTransforms();
        var mine = new HashSet<Collider>(go.GetComponentsInChildren<Collider>());
        var b = Bounds(go);
        Vector3 best = Vector3.forward; float bestDepth = -1f, second = -1f;
        foreach (var d in new[] { Vector3.forward, Vector3.back, Vector3.right, Vector3.left })
        {
            Vector3 lateral = Vector3.Cross(Vector3.up, d);
            float extD = Vector3.Dot(b.extents, Abs(d)), extL = Vector3.Dot(b.extents, Abs(lateral));
            float total = 0f; int samples = 0;
            foreach (float h in new[] { 0.2f, 0.3f, 0.4f })
            foreach (float l in new[] { -0.2f, -0.1f, 0f, 0.1f, 0.2f })
            {
                Vector3 origin = b.center + d * (extD + 0.5f) + lateral * (l * extL * 2f);
                origin.y = b.min.y + h * b.size.y;
                float depth = extD * 2f;
                foreach (var hit in Physics.RaycastAll(origin, -d, extD * 2f + 1f))
                    if (mine.Contains(hit.collider)) depth = Mathf.Min(depth, hit.distance - 0.5f);
                total += Mathf.Clamp(depth, 0f, extD * 2f) / Mathf.Max(0.01f, extD * 2f);
                samples++;
            }
            float avg = total / samples;
            if (avg > bestDepth) { second = bestDepth; bestDepth = avg; best = d; }
            else if (avg > second) second = avg;
        }
        foreach (var c in added) Object.DestroyImmediate(c);
        if (bestDepth < 0.05f || bestDepth < second * 1.3f) return null; // not sure - use the fallback
        return best;
    }

    static Vector3 SnapFlat(Vector3 v) =>
        Mathf.Abs(v.x) > Mathf.Abs(v.z) ? new Vector3(Mathf.Sign(v.x), 0f, 0f) : new Vector3(0f, 0f, Mathf.Sign(v.z));

    // ------------------------------------------------------------------ curtains on rods, blinds

    static void WindowDressing()
    {
        foreach (var (room, s, frame) in windowsBuilt)
        {
            if (room.kind == Kind.Garage || room.kind == Kind.Outdoor) continue;
            Side side = SideOf(room, s);
            Vector3 inward = Inward(side);
            Vector3 along = s.alongX ? Vector3.right : Vector3.forward;
            Bounds fb = frame ? Bounds(frame) : new Bounds(s.Center(room.Base + 1.55f), s.alongX ? new Vector3(1.3f, 1.3f, 0.3f) : new Vector3(0.3f, 1.3f, 1.3f));
            float frameW = Vector3.Dot(fb.size, along);
            float frameIn = Vector3.Dot(fb.extents, Abs(inward)); // how far the frame sticks into the room from the wall's centre line
            Vector3 c = new Vector3(fb.center.x, room.Base, fb.center.z);
            if (s.alongX) c.z = s.line; else c.x = s.line;

            bool fabric = room.name != "Kitchen" && (room.kind == Kind.Living || room.kind == Kind.Bedroom || room.kind == Kind.Fun);
            if (fabric)
            {
                float rodY = Mathf.Min(fb.max.y + 0.12f, room.Base + Storey - 0.14f);
                float panelDepth = 0.1f, panelW = 0.45f;
                for (int k = -1; k <= 1; k += 2)
                {
                    var go = Spawn("Curtains (Open Side)", room.furniture);
                    if (!go) break;
                    go.transform.rotation = Quaternion.Euler(0f, YawFor(side), 0f);
                    var cb = Bounds(go);
                    float h = rodY - (room.Base + 0.04f);
                    float w = Vector3.Dot(cb.size, along);
                    var sc = go.transform.localScale;
                    sc.y *= h / Mathf.Max(0.01f, cb.size.y);
                    if (w > 0.5f) sc.x *= 0.5f / w;
                    if (k > 0) sc.x = -sc.x; // mirrored pair
                    go.transform.localScale = sc;
                    cb = Bounds(go);
                    panelW = Vector3.Dot(cb.size, along);
                    panelDepth = Vector3.Dot(cb.size, Abs(inward));
                    Vector3 pos = c + along * (k * (frameW * 0.5f + panelW * 0.5f - 0.08f)) + inward * (frameIn + 0.05f + panelDepth * 0.5f);
                    pos.y = room.Base + 0.04f;
                    MoveBottomCenter(go, cb, pos);
                    placed++;
                    var nb = Bounds(go);
                    footprints.Add((room.floor, new Rect(nb.min.x, nb.min.z, nb.size.x, nb.size.z), go));
                }
                // The rod, a bit wider than the window, with end caps.
                float rodLen = frameW + panelW * 2f - 0.05f;
                Vector3 rodCentre = c + inward * (frameIn + 0.05f + panelDepth * 0.5f) + Vector3.up * (rodY - room.Base + 0.02f);
                Box("Curtain rod", room.furniture, rodCentre, s.alongX ? new Vector3(rodLen, 0.035f, 0.035f) : new Vector3(0.035f, 0.035f, rodLen), metal, false);
                for (int k = -1; k <= 1; k += 2)
                    Box("Rod end", room.furniture, rodCentre + along * (k * rodLen * 0.5f), Vector3.one * 0.07f, metal, false);
            }
            else
            {
                var go = Spawn("Blinds (Open)", room.furniture);
                if (!go) continue;
                go.transform.rotation = Quaternion.Euler(0f, YawFor(side), 0f);
                var cb = Bounds(go);
                float w = Vector3.Dot(cb.size, along);
                var sc = go.transform.localScale;
                sc.x *= (frameW * 0.98f) / Mathf.Max(0.01f, w);
                go.transform.localScale = sc;
                cb = Bounds(go);
                float depth = Vector3.Dot(cb.size, Abs(inward));
                Vector3 pos = c + inward * (frameIn + 0.02f + depth * 0.5f);
                pos.y = Mathf.Min(fb.max.y + 0.06f, room.Base + Storey - 0.1f) - cb.size.y;
                MoveBottomCenter(go, cb, pos);
                placed++;
            }
        }
    }

    /// <summary>No wall art and nothing tall in front of a window (and its curtains).</summary>
    static void KeepWindowClear(Room room, Segment s, GameObject frame)
    {
        Side side = SideOf(room, s);
        Vector3 c = s.Center(0f);
        float w = 1.4f;
        if (frame) { var b = Bounds(frame); w = s.alongX ? b.size.x : b.size.z; }
        w += 0.7f; // the curtains either side
        const float depth = 0.45f;
        Rect rect = side switch
        {
            Side.S => new Rect(c.x - w * 0.5f, s.line, w, depth),
            Side.N => new Rect(c.x - w * 0.5f, s.line - depth, w, depth),
            Side.W => new Rect(s.line, c.z - w * 0.5f, depth, w),
            _ => new Rect(s.line - depth, c.z - w * 0.5f, depth, w),
        };
        windowZones.Add((room.floor, rect));
    }

    // ------------------------------------------------------------------ floor plan for the minimap

    static void WriteLayout()
    {
        var layout = root.gameObject.AddComponent<HouseLayout>();
        layout.storeyHeight = Storey;
        layout.scale = HouseScale;
        foreach (var r in rooms)
            layout.rooms.Add(new HouseLayout.RoomInfo { name = r.name, floor = r.floor, rect = new Rect(r.x0, r.z0, r.x1 - r.x0, r.z1 - r.z0), kind = r.kind.ToString() });
        layout.walls.AddRange(mapWalls);
        // Furniture footprints for the map (not the things you carry around, and not curtains).
        foreach (var (f, rect, owner) in footprints)
        {
            if (!owner || owner == taskObjects.clothes || Props.Contains(owner.name) || FitToWindow.Contains(owner.name)) continue;
            if (rect.width < 0.15f || rect.height < 0.15f) continue;
            layout.furniture.Add(new HouseLayout.FurnitureInfo { floor = f, rect = rect, height = Bounds(owner).size.y });
        }
        new GameObject("Minimap").AddComponent<MiniMap>();
    }

    // ------------------------------------------------------------------ looks: floors and wall paint per room

    const string KitMaterials = "Assets/ImportedAssets/VNB - Home Set/Materials";

    static Material FloorMaterial(Room r)
    {
        switch (r.name)
        {
            case "Garage": return Flat("Floor - Concrete", new Color(0.55f, 0.56f, 0.57f), 0.1f);
            case "Balcony": return Tinted("Wooden Floor Alt", "Floor - Deck", new Color(0.82f, 0.68f, 0.55f));
            case "Foyer": case "Mudroom": return Tinted("Tile Floor", "Floor - Terracotta Tile", new Color(1f, 0.78f, 0.64f));
            case "Kitchen": return Tinted("Tile Floor", "Floor - White Tile", Color.white);
            case "Pantry": case "Laundry": return Tinted("Tile Floor", "Floor - Grey Tile", new Color(0.82f, 0.85f, 0.88f));
            case "Music room": return Tinted("Wooden Floor Alt", "Floor - Honey Wood", new Color(1f, 0.88f, 0.72f));
            case "Library": case "Office": return Tinted("Wooden Floor Alt", "Floor - Walnut", new Color(0.7f, 0.56f, 0.48f));
            case "Mom's bedroom": return Flat("Floor - Sand Carpet", new Color(0.83f, 0.76f, 0.64f), 0.05f);
            case "Walk-in closet": return Flat("Floor - Blush Carpet", new Color(0.86f, 0.72f, 0.7f), 0.05f);
            case "Your bedroom": return Flat("Floor - Midnight Carpet", new Color(0.3f, 0.34f, 0.47f), 0.05f);
            case "Kid's room": return Flat("Floor - Sky Carpet", new Color(0.62f, 0.76f, 0.9f), 0.05f);
            case "Playroom": return Flat("Floor - Soft Mat", new Color(0.62f, 0.8f, 0.66f), 0.05f);
            case "Home cinema": return Flat("Floor - Wine Carpet", new Color(0.42f, 0.14f, 0.18f), 0.05f);
            case "Game room": return Flat("Floor - Purple Carpet", new Color(0.36f, 0.3f, 0.5f), 0.05f);
        }
        return r.kind switch
        {
            Kind.Bath => Tinted("Tile Floor", "Floor - Blue Tile", new Color(0.82f, 0.92f, 1f)),
            Kind.Hall or Kind.Stairs => Tinted("Wooden Floor Alt", "Floor - Walnut", new Color(0.7f, 0.56f, 0.48f)),
            _ => Tinted("Wooden Floor Alt", "Floor - Oak", Color.white),
        };
    }

    /// <summary>Each room gets its own wall colour - that's most of a room's character.</summary>
    static Material WallPaint(Room r)
    {
        (string name, Color c) = r.name switch
        {
            "Foyer" => ("Sage", new Color(0.74f, 0.8f, 0.7f)),
            "Music room" => ("Deep Teal", new Color(0.26f, 0.44f, 0.47f)),
            "Living room" => ("Warm Cream", new Color(0.94f, 0.89f, 0.79f)),
            "Dining" => ("Terracotta", new Color(0.82f, 0.56f, 0.45f)),
            "Kitchen" => ("Butter", new Color(0.97f, 0.91f, 0.68f)),
            "Powder room" => ("Navy", new Color(0.28f, 0.33f, 0.5f)),
            "Playroom" => ("Mint", new Color(0.7f, 0.9f, 0.8f)),
            "Mom's bedroom" => ("Dusty Rose", new Color(0.86f, 0.73f, 0.73f)),
            "Mom's bath" => ("Aqua", new Color(0.72f, 0.88f, 0.89f)),
            "Your bedroom" => ("Slate Blue", new Color(0.38f, 0.44f, 0.58f)),
            "Bathroom" => ("Aqua", new Color(0.72f, 0.88f, 0.89f)),
            "Kid's room" => ("Sunshine", new Color(0.99f, 0.86f, 0.56f)),
            "Office" => ("Olive", new Color(0.64f, 0.68f, 0.52f)),
            "Library" => ("Forest Green", new Color(0.32f, 0.45f, 0.37f)),
            "Home cinema" => ("Charcoal", new Color(0.2f, 0.2f, 0.24f)),
            "Game room" => ("Grape", new Color(0.44f, 0.37f, 0.6f)),
            "WC" => ("Aqua", new Color(0.72f, 0.88f, 0.89f)),
            "Garage" => ("Concrete", new Color(0.66f, 0.67f, 0.68f)),
            _ => r.kind switch
            {
                Kind.Service => ("Pale Blue Grey", new Color(0.8f, 0.84f, 0.88f)),
                Kind.Bath => ("Aqua", new Color(0.72f, 0.88f, 0.89f)),
                _ => ("Warm Grey", new Color(0.89f, 0.87f, 0.83f)), // halls, landings, stairs
            },
        };
        if (!wallMats.TryGetValue(name, out var m)) wallMats[name] = m = Mat("Wall - " + name, c, 0.06f);
        return m;
    }

    /// <summary>
    /// The kit's wall has two faces (brick + plaster). Paint each face for the room it faces;
    /// faces looking outside keep the brick. Fixes brick showing inside rooms.
    /// </summary>
    static void PaintWallFaces(GameObject wall, Segment s)
    {
        if (!wall) return;
        Room pos, neg;
        if (s.alongX) { pos = RoomAt(s.floor, s.start, s.line); neg = RoomAt(s.floor, s.start, s.line - 2); }
        else { pos = RoomAt(s.floor, s.line, s.start); neg = RoomAt(s.floor, s.line - 2, s.start); }
        Vector3 axis = s.alongX ? Vector3.forward : Vector3.right;
        Material For(Room r) => r == null || !r.Indoor ? brickMat : WallPaint(r);

        foreach (var mr in wall.GetComponentsInChildren<MeshRenderer>())
        {
            var mf = mr.GetComponent<MeshFilter>();
            if (!mf || !mf.sharedMesh) continue;
            var mesh = mf.sharedMesh;
            var verts = mesh.vertices;
            var mats = mr.sharedMaterials;
            bool changed = false;
            for (int i = 0; i < mesh.subMeshCount && i < mats.Length; i++)
            {
                var tris = mesh.GetTriangles(i);
                Vector3 sum = Vector3.zero;
                for (int t = 0; t + 2 < tris.Length; t += 3)
                    sum += Vector3.Cross(verts[tris[t + 1]] - verts[tris[t]], verts[tris[t + 2]] - verts[tris[t]]);
                Vector3 world = mf.transform.TransformDirection(sum);
                if (world.sqrMagnitude < 1e-8f) continue;
                float facing = Vector3.Dot(world.normalized, axis);
                if (facing > 0.3f) { mats[i] = For(pos); changed = true; }
                else if (facing < -0.3f) { mats[i] = For(neg); changed = true; }
            }
            if (changed) mr.sharedMaterials = mats;
        }
    }

    static Material Flat(string name, Color c, float gloss)
    {
        if (!floorMats.TryGetValue(name, out var m)) floorMats[name] = m = Mat(name, c, gloss);
        return m;
    }

    /// <summary>A copy of one of the kit's textured materials, tinted (wood grain / tiles stay).</summary>
    static Material Tinted(string kitMaterial, string name, Color tint)
    {
        if (floorMats.TryGetValue(name, out var cached)) return cached;
        var kit = AssetDatabase.LoadAssetAtPath<Material>($"{KitMaterials}/{kitMaterial}.mat");
        if (!kit) return Flat(name, tint * 0.75f, 0.2f);
        string path = $"{MaterialFolder}/{name}.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!m) { m = new Material(kit); AssetDatabase.CreateAsset(m, path); }
        else m.CopyPropertiesFromMaterial(kit);
        m.color = kit.color * tint;
        EditorUtility.SetDirty(m);
        floorMats[name] = m;
        return m;
    }

    static void Paint(GameObject go, Material m)
    {
        if (!go || !m) return;
        foreach (var r in go.GetComponentsInChildren<Renderer>())
        {
            var mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++) mats[i] = m;
            r.sharedMaterials = mats;
        }
    }

    /// <summary>Grow around its own centre (frames sit a hair proud of the wall hole instead of flickering with it).</summary>
    static void Grow(GameObject go, float factor)
    {
        if (!go) return;
        Vector3 before = Bounds(go).center;
        go.transform.localScale *= factor;
        go.transform.position += before - Bounds(go).center;
    }

    // ------------------------------------------------------------------ player & cat

    /// <summary>Grow the whole house around the origin. Light ranges don't scale with the object, so do those by hand.</summary>
    static void ScaleHouse()
    {
        root.localScale = Vector3.one * HouseScale;
        foreach (var light in root.GetComponentsInChildren<Light>(true))
            if (light.type != LightType.Directional) light.range *= HouseScale;
        Physics.SyncTransforms();
    }

    static void PlacePlayerAndCat()
    {
        var player = Object.FindObjectOfType<PlayerMovementAdvanced>(true);
        if (player)
        {
            player.transform.position = new Vector3(13f * HouseScale, 1.2f, 1.8f * HouseScale);
            var cam = Object.FindObjectOfType<MoveCamera>(true);
            if (cam && cam.cameraPosition) cam.transform.position = cam.cameraPosition.position;
        }
        var cat = Object.FindObjectOfType<CatBrain>(true);
        if (cat)
        {
            cat.transform.position = new Vector3(11.2f * HouseScale, 0f, 11.6f * HouseScale);
            cat.house = root;
            cat.furniture = furnitureRoot;
        }
    }

    static void AddToBuildSettings()
    {
        var list = EditorBuildSettings.scenes.ToList();
        if (list.Any(s => s.path == TargetScene)) return;
        list.Add(new EditorBuildSettingsScene(TargetScene, true));
        EditorBuildSettings.scenes = list.ToArray();
    }

    // ------------------------------------------------------------------ low-level helpers

    static Dictionary<string, string> prefabIndex;

    /// <summary>Prefab by exact file name; earlier packs in PrefabFolders win (Home Set first).</summary>
    static GameObject FindPrefab(string name)
    {
        if (prefabCache.TryGetValue(name, out var cached)) return cached;
        if (prefabIndex == null)
        {
            prefabIndex = new Dictionary<string, string>();
            foreach (var folder in PrefabFolders)
            {
                if (!AssetDatabase.IsValidFolder(folder)) continue;
                foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { folder }))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    string file = Path.GetFileNameWithoutExtension(path);
                    if (!prefabIndex.ContainsKey(file)) prefabIndex[file] = path;
                }
            }
        }
        var found = prefabIndex.TryGetValue(name, out var p) ? AssetDatabase.LoadAssetAtPath<GameObject>(p) : null;
        prefabCache[name] = found;
        return found;
    }

    static GameObject Spawn(string prefab, Transform parent)
    {
        GameObject go;
        if (ExtraModels.ContainsKey(prefab)) go = SpawnModel(prefab, parent);
        else if (Props.Contains(prefab)) go = Prop(prefab, parent);
        else
        {
            var asset = FindPrefab(prefab);
            if (!asset) { missing.Add(prefab); return null; }
            go = (GameObject)PrefabUtility.InstantiatePrefab(asset, parent.gameObject.scene);
            go.transform.SetParent(parent, false);
        }
        if (go && furnitureRoot && parent.IsChildOf(furnitureRoot) && !FitToWindow.Contains(prefab))
            go.transform.localScale *= FurnitureScale;
        return go;
    }

    /// <summary>A kit piece placed by its pivot (kit pieces have their pivot at the bottom/top centre).</summary>
    static GameObject Piece(string prefab, Transform parent, Vector3 position, float yaw, bool flip = false)
    {
        var go = Spawn(prefab, parent);
        if (!go) return null;
        go.transform.SetPositionAndRotation(position, Quaternion.Euler(flip ? 180f : 0f, yaw, 0f));
        return go;
    }

    static Bounds Bounds(GameObject go)
    {
        var renderers = go.GetComponentsInChildren<Renderer>().Where(r => r is MeshRenderer || r is SkinnedMeshRenderer).ToArray();
        if (renderers.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
        var b = renderers[0].bounds;
        foreach (var r in renderers) b.Encapsulate(r.bounds);
        return b;
    }

    static void MoveBottomCenter(GameObject go, Bounds b, Vector3 target) =>
        go.transform.position += target + Vector3.up * Lift - new Vector3(b.center.x, b.min.y, b.center.z);

    static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));

    static void FitBox(GameObject go)
    {
        var renderers = go.GetComponentsInChildren<Renderer>().Where(r => r is MeshRenderer || r is SkinnedMeshRenderer).ToArray();
        if (renderers.Length == 0) return;
        bool any = false;
        var local = new Bounds();
        foreach (var r in renderers)
        {
            var b = r.bounds;
            for (int i = 0; i < 8; i++)
            {
                var corner = new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z);
                var p = go.transform.InverseTransformPoint(corner);
                if (!any) { local = new Bounds(p, Vector3.zero); any = true; } else local.Encapsulate(p);
            }
        }
        var box = go.AddComponent<BoxCollider>();
        box.center = local.center;
        box.size = local.size;
    }

    static void MeshColliders(GameObject go)
    {
        if (!go) return;
        foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
        {
            if (mf.GetComponent<Collider>() || !mf.sharedMesh) continue;
            mf.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
        }
    }

    static void Outline(GameObject go)
    {
        if (!go || go.GetComponent<global::Outline>()) return;
        var o = go.AddComponent<global::Outline>();
        o.OutlineMode = global::Outline.Mode.OutlineVisible;
        o.OutlineColor = Color.white;
        o.OutlineWidth = 5f;
        o.enabled = false;
    }

    static GameObject Box(string name, Transform parent, Vector3 center, Vector3 size, Material mat, bool collider, int layer = 0)
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

    static Transform Group(Transform parent, string path)
    {
        Transform t = parent;
        foreach (var part in path.Split('/'))
        {
            var child = t.Find(part);
            if (!child) { child = new GameObject(part).transform; child.SetParent(t, false); }
            t = child;
        }
        return t;
    }

    static void SetLayer(GameObject go, int layer)
    {
        foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
    }

    static void Materials()
    {
        if (!AssetDatabase.IsValidFolder(MaterialFolder)) AssetDatabase.CreateFolder("Assets/Materials", "House");
        wood = Mat("Stair Wood", new Color(0.62f, 0.44f, 0.29f), 0.25f);
        plaster = Mat("Wall Plaster", new Color(0.9f, 0.88f, 0.84f), 0.1f);
        metal = Mat("Dark Metal", new Color(0.25f, 0.26f, 0.28f), 0.5f);
        garageDoorMat = Mat("Garage Door", new Color(0.82f, 0.83f, 0.85f), 0.3f);
        ceilingMat = Mat("Ceiling White", new Color(0.97f, 0.96f, 0.94f), 0.05f);
        brickMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/ImportedAssets/VNB - Home Set/Materials/Brick Wall.mat");
    }

    static Material Mat(string name, Color color, float gloss)
    {
        string path = $"{MaterialFolder}/{name}.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m)
        {
            m.color = color;
            m.SetFloat("_Glossiness", gloss);
            EditorUtility.SetDirty(m);
            return m;
        }
        m = new Material(Shader.Find("Standard")) { color = color };
        m.SetFloat("_Glossiness", gloss);
        AssetDatabase.CreateAsset(m, path);
        return m;
    }
}
