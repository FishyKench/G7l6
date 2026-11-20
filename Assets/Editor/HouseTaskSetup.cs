using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// Menu: G7l6 > Set Up House Tasks
///
/// Wires every task into the open house scene in one click: adds the task components to the
/// oven, washing machine, TVs and desk, makes the remote/clothes carryable, builds the fire and
/// flood effects, creates placeholder extinguisher + mop, and hides the old task UI.
/// Safe to run again - it reuses what's already there. Undo with Ctrl+Z.
/// </summary>
public static class HouseTaskSetup
{
    const string TaskMaterials = "Assets/Materials/Tasks";
    const string StaticMaterialPath = "Assets/Materials/TvStatic.mat";
    const string ScreenOffMaterialPath = "Assets/Materials/ScreenOff.mat";
    const string StaticSoundPath = "Assets/Scripts/tv-static-6411.mp3";

    static readonly List<string> done = new List<string>();
    static readonly List<string> problems = new List<string>();
    static readonly List<Object> created = new List<Object>();

    [MenuItem("G7l6/Set Up House Tasks")]
    public static void Run()
    {
        if (EditorApplication.isPlaying)
        {
            EditorUtility.DisplayDialog("Set Up House Tasks", "Stop Play mode first.", "OK");
            return;
        }

        var scene = SceneManager.GetActiveScene();
        if (!scene.name.Contains("House") &&
            !EditorUtility.DisplayDialog("Set Up House Tasks", $"The open scene is '{scene.name}', not the house. Continue anyway?", "Continue", "Cancel"))
            return;

        done.Clear(); problems.Clear(); created.Clear();
        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Set Up House Tasks");

        EnsureFolder(TaskMaterials);
        Physics.SyncTransforms();

        SetUpManagers();
        SetUpPlayer();
        HideOldUI();
        SetUpHomework();
        SetUpRemote();
        SetUpTv("tv_wall_001", "Living room TV");
        SetUpTv("TV 5 Mounted", "Office TV");
        SetUpTools();
        SetUpOven();
        SetUpLaundry();
        SetUpCeilingLeak();
        SetUpCat();
        AddSceneToBuild(scene);

        Undo.CollapseUndoOperations(undoGroup);
        EditorSceneManager.MarkSceneDirty(scene);
        if (created.Count > 0) Selection.objects = created.ToArray();

        string message = "Done:\n- " + string.Join("\n- ", done);
        if (problems.Count > 0) message += "\n\nNeeds a look:\n- " + string.Join("\n- ", problems);
        message += "\n\nSave the scene (Ctrl+S) to keep it.";
        Debug.Log("[House Task Setup]\n" + message);
        EditorUtility.DisplayDialog("House tasks set up", message, "OK");
    }

    // ------------------------------------------------------------------ new house (called by the house builder)

    /// <summary>Everything the house builder placed that a task lives on. Missing ones are reported, not fatal.</summary>
    internal class HouseTaskObjects
    {
        public GameObject desk, remote, oven, washer, clothes, leakSink;
        public string clothesRoom;
        public readonly List<(GameObject tv, string label, Renderer screen)> tvs = new List<(GameObject, string, Renderer)>();
    }

    /// <summary>Wires every task onto the objects the builder placed. Returns a short report for the build dialog.</summary>
    internal static string SetUpTasksInNewHouse(HouseTaskObjects o)
    {
        done.Clear(); problems.Clear(); created.Clear();
        EnsureFolder(TaskMaterials);
        Physics.SyncTransforms();

        SetUpManagers();
        SetUpPlayer();
        SetUpHomework(o.desk);
        SetUpRemote(o.remote);
        foreach (var (tv, label, screen) in o.tvs) SetUpTv(tv, label, screen);
        if (o.tvs.Count == 0) problems.Add("No TV was placed, so there's no TV task");
        SetUpTools();
        SetUpOven(o.oven);
        SetUpLaundry(o.washer, o.clothes, o.clothesRoom);
        SetUpCeilingLeak(o.leakSink);

        string report = "Tasks:\n- " + string.Join("\n- ", done.Where(d => !d.Contains("HUD") && !d.Contains("Manager")));
        if (problems.Count > 0) report += "\n\nTasks that need a look:\n- " + string.Join("\n- ", problems);
        Debug.Log("[Build New House] " + report);
        return report;
    }

    // ------------------------------------------------------------------ managers, player, old UI

    static void SetUpManagers()
    {
        var gm = Object.FindObjectOfType<GameManager>();
        if (!gm)
        {
            var go = new GameObject("GameManager");
            Undo.RegisterCreatedObjectUndo(go, "Create GameManager");
            gm = Undo.AddComponent<GameManager>(go);
        }
        var host = gm.gameObject;
        if (!Object.FindObjectOfType<StressManager>()) Undo.AddComponent<StressManager>(host);
        if (!Object.FindObjectOfType<ExhaustionManager>()) Undo.AddComponent<ExhaustionManager>(host);
        done.Add("GameManager, StressManager and ExhaustionManager are in the scene");
        SetUpHud(rebuild: false);

        var oldUiManager = FindAny("TaskUIManager");
        if (oldUiManager)
        {
            Undo.DestroyObjectImmediate(oldUiManager);
            done.Add("Removed the old TaskUIManager object");
        }
    }

    const string HudFontPath = "Assets/Fonts/Pinkend SDF.asset";
    public const string KittyPrefabPath = "Assets/ithappy/Animals_FREE/Prefabs/Kitty_001.prefab";
    const string WalkMapPath = "Assets/Navigation/Cat Walk Map.asset";
    const string HouseScenePath = "Assets/Scenes/HouseScene.unity";

    // ------------------------------------------------------------------ cat

    /// <summary>Adds just the cat (and its walk map) to the house scene and saves it.</summary>
    [MenuItem("G7l6/Add Cat To House")]
    public static void AddCatToHouse()
    {
        if (EditorApplication.isPlaying) { EditorUtility.DisplayDialog("Add Cat", "Stop Play mode first.", "OK"); return; }

        var scene = SceneManager.GetActiveScene();
        if (scene.path != HouseScenePath)
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            scene = EditorSceneManager.OpenScene(HouseScenePath, OpenSceneMode.Single);
        }

        done.Clear(); problems.Clear(); created.Clear();
        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Add Cat To House");
        Physics.SyncTransforms();

        SetUpCat();

        Undo.CollapseUndoOperations(undoGroup);
        EditorSceneManager.MarkSceneDirty(scene);
        bool saved = problems.Count == 0 && EditorSceneManager.SaveScene(scene);

        if (created.Count > 0)
        {
            Selection.activeObject = created[0];
            EditorGUIUtility.PingObject(created[0]);
            SceneView.FrameLastActiveSceneView();
        }

        string message = "Done:\n- " + string.Join("\n- ", done);
        if (problems.Count > 0) message += "\n\nNeeds a look:\n- " + string.Join("\n- ", problems);
        message += saved ? "\n\nThe scene is saved. Press Play to meet the cat." : "\n\nSave the scene (Ctrl+S) to keep it.";
        Debug.Log("[Add Cat]\n" + message);
        EditorUtility.DisplayDialog("The cat is in the house", message, "OK");
    }

    static void SetUpCat()
    {
        var brain = Object.FindObjectOfType<CatBrain>(true);
        GameObject cat = brain ? brain.gameObject : null;
        if (!cat)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(KittyPrefabPath);
            if (!prefab)
            {
                problems.Add("The cat model isn't in the project yet: import 'Animals FREE' by ithappy (Window > Package Manager > My Assets), then run this again");
                return;
            }
            cat = (GameObject)PrefabUtility.InstantiatePrefab(prefab, SceneManager.GetActiveScene());
            cat.name = "Cat";

            var room = FindActive("Lounge/LivingRoom") ?? FindActive("LivingRoom");
            if (room)
            {
                var b = WorldBounds(room);
                float floor = FloorY(new Vector3(b.center.x, b.min.y + 1f, b.center.z), room.transform, b.min.y);
                cat.transform.position = new Vector3(b.center.x, floor, b.center.z);
            }
            cat.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
            Undo.RegisterCreatedObjectUndo(cat, "Add cat");
            created.Add(cat);
            done.Add("Added the cat to the living room (it's selected - move it if you like)");
        }

        // The model ships with keyboard-controlled movement scripts - switch them off, the cat drives itself.
        foreach (var mb in cat.GetComponents<MonoBehaviour>())
        {
            if (!mb || !mb.enabled || mb.GetType().Namespace != "Controller") continue;
            Undo.RecordObject(mb, "Disable cat input");
            mb.enabled = false;
            Dirty(mb);
        }

        var agent = Ensure<NavMeshAgent>(cat);
        Undo.RecordObject(agent, "Configure cat");
        agent.agentTypeID = 0;
        agent.radius = 0.18f / Mathf.Max(0.01f, cat.transform.localScale.x); // same size as the walk map, whatever the cat's scale
        agent.height = 0.45f;
        agent.baseOffset = 0f;
        agent.speed = 1f;
        agent.angularSpeed = 420f;
        agent.acceleration = 10f;
        agent.stoppingDistance = 0.3f;
        agent.autoBraking = true;
        agent.obstacleAvoidanceType = ObstacleAvoidanceType.LowQualityObstacleAvoidance;
        agent.enabled = false; // the cat switches it on once its walk map is loaded
        Dirty(agent);

        brain = Ensure<CatBrain>(cat);
        Undo.RecordObject(brain, "Configure cat");
        brain.font = AssetDatabase.LoadAssetAtPath<TMPro.TMP_FontAsset>(HudFontPath);
        Dirty(brain);
        LinkCatToHouse();

        EnsureCollider(cat);
        EnsureOutline(cat);
        done.Add("Cat: wanders, steals the remote / clothes, naps on the laundry (E to pet, take back or shoo)");

        BakeWalkMap();
    }

    /// <summary>Scales the cat in the open house scene (keeps its walking footprint the same).</summary>
    public static void SetCatSize(float size)
    {
        if (EditorApplication.isPlaying) return;
        var brain = Object.FindObjectOfType<CatBrain>(true);
        if (!brain)
        {
            var scene = SceneManager.GetActiveScene();
            if (scene.path != HouseScenePath)
            {
                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
                EditorSceneManager.OpenScene(HouseScenePath, OpenSceneMode.Single);
                brain = Object.FindObjectOfType<CatBrain>(true);
            }
        }
        if (!brain) { Debug.LogWarning("[G7l6] No cat in the scene to resize. Run G7l6 > Add Cat To House first."); return; }

        size = Mathf.Clamp(size, 0.2f, 5f);
        var cat = brain.gameObject;
        Undo.RecordObject(cat.transform, "Resize cat");
        cat.transform.localScale = Vector3.one * size;
        Dirty(cat.transform);

        var agent = cat.GetComponent<NavMeshAgent>();
        if (agent)
        {
            Undo.RecordObject(agent, "Resize cat");
            agent.radius = 0.18f / size;
            Dirty(agent);
        }

        EditorSceneManager.MarkSceneDirty(cat.scene);
        Selection.activeObject = cat;
        EditorGUIUtility.PingObject(cat);
        SceneView.FrameLastActiveSceneView();
        Debug.Log($"[G7l6] Cat scaled to {size}x. Change it any time with the Scale on the 'Cat' object.");
        EditorUtility.DisplayDialog("Bigger cat", $"The cat is now {size}x its original size and its colors are fixed.\n\nPress Ctrl+S to save the scene.\n(Change the size any time with Scale on the 'Cat' object.)", "OK");
    }

    // ------------------------------------------------------------------ stairs

    const string RampName = "Stairs Ramp (walkable)";

    /// <summary>
    /// Menu: G7l6 > Fix Stairs. Lays an invisible, perfectly smooth ramp over the stairs (the player and
    /// the cat walk on that instead of the bumpy convex stair collider) and re-bakes the cat's walk map
    /// so the cat can use the stairs too.
    /// </summary>
    [MenuItem("G7l6/Fix Stairs")]
    public static void CatOverhaulMenu() => CatOverhaul();

    /// <summary>
    /// Menu: G7l6 > Update Cat (Climbing). Makes beds/desks/tables/sofas/counters solid where they
    /// weren't, links the cat to the house and re-bakes its walk map at the cat's real size.
    /// </summary>
    [MenuItem("G7l6/Update Cat (Climbing)")]
    public static void CatOverhaul()
    {
        if (EditorApplication.isPlaying) { EditorUtility.DisplayDialog("Update Cat", "Stop Play mode first.", "OK"); return; }
        var scene = SceneManager.GetActiveScene();
        if (scene.path != HouseScenePath)
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            scene = EditorSceneManager.OpenScene(HouseScenePath, OpenSceneMode.Single);
        }

        done.Clear(); problems.Clear(); created.Clear();
        Undo.IncrementCurrentGroup();
        int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Update Cat (Climbing)");
        Physics.SyncTransforms();
        MakeClimbableFurnitureSolid();
        Physics.SyncTransforms();
        LinkCatToHouse();
        BakeWalkMap();
        Undo.CollapseUndoOperations(group);
        EditorSceneManager.MarkSceneDirty(scene);

        string message = "Done:\n- " + string.Join("\n- ", done);
        if (problems.Count > 0) message += "\n\nNeeds a look:\n- " + string.Join("\n- ", problems);
        message += "\n\nPress Ctrl+S to save the scene, then Play.\nTest keys: [ steal remote   ] nap   ; jump on furniture   ' lie on keyboard";
        Debug.Log("[Update Cat]\n" + message);
        EditorUtility.DisplayDialog("Cat updated", message, "OK");
    }

    static readonly System.Text.RegularExpressions.Regex ClimbableNames =
        new System.Text.RegularExpressions.Regex("bed|desk|table|sofa|couch|counter|dresser|cabinet|nightstand|drawer|armchair",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    /// <summary>The cat can only jump onto things that have a collider. Give climbable furniture one if it has none.</summary>
    /// <summary>The house in the open scene: the new one if it's there, otherwise the old one.</summary>
    static GameObject HouseRoot() => FindActive("NewHouse") ?? FindActive("HouseTryTwo");

    /// <summary>Used by the house builder: solid furniture, cat linked to the house, fresh walk map.</summary>
    internal static void PrepareCatForHouse()
    {
        done.Clear(); problems.Clear(); created.Clear();
        Physics.SyncTransforms();
        MakeClimbableFurnitureSolid();
        Physics.SyncTransforms();
        LinkCatToHouse();
        BakeWalkMap();
        foreach (var p in problems) Debug.LogWarning("[Build New House] " + p);
    }

    internal static void MakeClimbableFurnitureSolid()
    {
        var house = HouseRoot();
        var furniture = house ? (FindChild(house.transform, "Furniature") ?? FindChild(house.transform, "Furniture")) : null;
        if (!furniture) { problems.Add("Couldn't find the 'Furniature' object"); return; }

        int added = 0;
        foreach (Transform room in furniture)
        {
            foreach (Transform piece in room)
            {
                if (!piece.gameObject.activeInHierarchy || !ClimbableNames.IsMatch(piece.name)) continue;
                if (piece.GetComponentInChildren<Carryable>(true)) continue;
                if (piece.GetComponentsInChildren<Collider>(true).Any(c => !c.isTrigger)) continue;
                EnsureCollider(piece.gameObject);
                added++;
            }
        }
        done.Add(added > 0
            ? $"Made {added} beds/desks/tables/sofas solid so the cat (and you) can't walk through them"
            : "All beds/desks/tables/sofas were already solid");
    }

    public static void FixStairsMenu()
    {
        if (EditorApplication.isPlaying) { EditorUtility.DisplayDialog("Fix Stairs", "Stop Play mode first.", "OK"); return; }
        var scene = SceneManager.GetActiveScene();
        if (scene.path != HouseScenePath)
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            scene = EditorSceneManager.OpenScene(HouseScenePath, OpenSceneMode.Single);
        }

        done.Clear(); problems.Clear(); created.Clear();
        Undo.IncrementCurrentGroup();
        int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Fix Stairs");
        FixStairs();
        LinkCatToHouse();
        BakeWalkMap();
        Undo.CollapseUndoOperations(group);
        EditorSceneManager.MarkSceneDirty(scene);
        if (created.Count > 0) Selection.activeObject = created[0];

        string message = "Done:\n- " + string.Join("\n- ", done);
        if (problems.Count > 0) message += "\n\nNeeds a look:\n- " + string.Join("\n- ", problems);
        message += "\n\nPress Ctrl+S to save the scene.";
        Debug.Log("[Fix Stairs]\n" + message);
        EditorUtility.DisplayDialog("Stairs fixed", message, "OK");
    }

    static void FixStairs()
    {
        // The ProBuilder staircase (the one with a mesh, not the railing pieces).
        var stairs = AllTransforms()
            .Where(t => t.name == "Stairs" && t.gameObject.activeInHierarchy && t.GetComponent<MeshFilter>())
            .Select(t => t.gameObject).FirstOrDefault();
        var mesh = stairs ? stairs.GetComponent<MeshFilter>().sharedMesh : null;
        if (!mesh) { problems.Add("Couldn't find the stairs mesh ('Stairs')"); return; }

        var t0 = stairs.transform;
        var points = mesh.vertices.Select(v => t0.TransformPoint(v)).ToArray();

        // Which way the stairs climb: along their length, towards the end whose vertices are higher.
        Vector3 run = Flat(t0.forward);
        float minS = points.Min(p => Vector3.Dot(p, run)), maxS = points.Max(p => Vector3.Dot(p, run));
        float yLow = points.Where(p => Vector3.Dot(p, run) < minS + 0.4f).Average(p => p.y);
        float yHigh = points.Where(p => Vector3.Dot(p, run) > maxS - 0.4f).Average(p => p.y);
        if (yLow > yHigh)
        {
            run = -run;
            float tmp = minS; minS = -maxS; maxS = -tmp;
        }
        Vector3 side = Vector3.Cross(Vector3.up, run).normalized;
        float minW = points.Min(p => Vector3.Dot(p, side)), maxW = points.Max(p => Vector3.Dot(p, side));
        float bottomY = points.Min(p => p.y), topY = points.Max(p => p.y);

        // Step size: along the stairs, the vertices line up at the step edges (one more edge than steps).
        // (Counting heights doesn't work - ProBuilder puts two heights ~2 cm apart on each step.)
        float minRun = minS;
        int edges = points.Select(p => Mathf.Round((Vector3.Dot(p, run) - minRun) * 50f)).Distinct().Count();
        int steps = Mathf.Max(1, edges - 1);
        float stepHeight = (topY - bottomY) / steps;
        float stepDepth = (maxS - minS) / steps;
        if (stepHeight < 0.08f || stepHeight > 0.6f) { stepHeight = 0f; stepDepth = 0f; } // couldn't tell - use the outline

        // Ramp surface: the line through the front edges of the steps (where your foot lands),
        // from where it meets the floor below up to the front edge of the top step.
        float mid = (minW + maxW) * 0.5f;
        Vector3 bottom = run * (minS - stepDepth) + side * mid; bottom.y = bottomY;
        Vector3 top = run * (maxS - stepDepth) + side * mid; top.y = topY;
        Vector3 along = top - bottom;
        float length = along.magnitude;
        Vector3 forward = along / length;
        Vector3 normal = Vector3.Cross(forward, side).normalized;
        if (normal.y < 0f) normal = -normal;
        const float thickness = 0.4f;
        float width = maxW - minW;

        var ramp = RampPiece(RampName, stairs.layer);
        ramp.transform.SetPositionAndRotation((bottom + top) * 0.5f - normal * (thickness * 0.5f), Quaternion.LookRotation(forward, normal));
        SetBox(ramp, new Vector3(width, thickness, length + 0.1f));

        // Flat landing on top of the last step, so there's no gap between the ramp and the upstairs floor.
        if (stepDepth > 0f)
        {
            var landing = RampPiece(RampName + " - top", stairs.layer);
            Vector3 landingCenter = run * (maxS - stepDepth * 0.5f) + side * mid;
            landingCenter.y = topY - thickness * 0.5f;
            landing.transform.SetPositionAndRotation(landingCenter, Quaternion.LookRotation(run, Vector3.up));
            SetBox(landing, new Vector3(width, thickness, stepDepth + 0.1f));
        }

        // The old convex collider turns the steps into a lumpy wedge - the ramp replaces it.
        var hull = stairs.GetComponent<MeshCollider>();
        if (hull && hull.enabled)
        {
            Undo.RecordObject(hull, "Disable lumpy stairs collider");
            hull.enabled = false;
            Dirty(hull);
        }

        float angle = Vector3.Angle(Vector3.up, normal);
        done.Add($"Laid a smooth invisible ramp over the stairs ({steps} steps, {angle:0}° steep, {length:0.0} m long) and switched off the old lumpy stairs collider");
    }

    /// <summary>A ramp piece at the scene root (so no stretched/rotated parent skews its box).</summary>
    static GameObject RampPiece(string name, int layer)
    {
        var existing = AllTransforms().FirstOrDefault(t => t.name == name);
        GameObject piece = existing ? existing.gameObject : null;
        if (!piece)
        {
            piece = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(piece, "Create " + name);
            created.Add(piece);
        }
        Undo.RecordObject(piece, "Place " + name);
        Undo.RecordObject(piece.transform, "Place " + name);
        piece.layer = layer; // counts as ground for the player
        piece.transform.localScale = Vector3.one;
        return piece;
    }

    static void SetBox(GameObject go, Vector3 size)
    {
        var box = go.GetComponent<BoxCollider>();
        if (!box) box = Undo.AddComponent<BoxCollider>(go);
        Undo.RecordObject(box, "Size " + go.name);
        box.center = Vector3.zero;
        box.size = size;
    }

    /// <summary>Tells the cat which objects are the house and the furniture (where it may and may not stand).</summary>
    internal static void LinkCatToHouse()
    {
        var brain = Object.FindObjectOfType<CatBrain>(true);
        if (!brain) return;
        var house = HouseRoot();
        Undo.RecordObject(brain, "Link cat to house");
        if (house)
        {
            brain.house = house.transform;
            brain.furniture = FindChild(house.transform, "Furniature") ?? FindChild(house.transform, "Furniture");
        }
        Dirty(brain);
    }

    /// <summary>Menu: G7l6 > Bake Cat Walk Map. Run it again after moving furniture or walls.</summary>
    [MenuItem("G7l6/Bake Cat Walk Map")]
    public static void BakeWalkMapMenu()
    {
        if (EditorApplication.isPlaying) { EditorUtility.DisplayDialog("Bake Cat Walk Map", "Stop Play mode first.", "OK"); return; }
        done.Clear(); problems.Clear(); created.Clear();
        BakeWalkMap();
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        string message = string.Join("\n", done.Concat(problems));
        EditorUtility.DisplayDialog("Cat walk map", message + "\n\nSave the scene (Ctrl+S) to keep it.", "OK");
    }

    internal static void BakeWalkMap()
    {
        var house = HouseRoot();
        Bounds area = house ? WorldBounds(house) : new Bounds(Vector3.zero, new Vector3(80f, 30f, 80f));
        area.Expand(4f);

        Physics.SyncTransforms();
        var catForSize = Object.FindObjectOfType<CatBrain>(true);
        float catScale = catForSize ? catForSize.transform.lossyScale.y : 1f;
        var data = CatNavMesh.Bake(area, catScale);
        if (!data) { problems.Add("Couldn't build the cat's walk map"); return; }
        data.name = "Cat Walk Map";

        EnsureFolder("Assets/Navigation");
        if (AssetDatabase.LoadAssetAtPath<NavMeshData>(WalkMapPath)) AssetDatabase.DeleteAsset(WalkMapPath);
        AssetDatabase.CreateAsset(data, WalkMapPath);

        var holder = Object.FindObjectOfType<CatNavMesh>(true);
        if (!holder)
        {
            var go = new GameObject("Cat Walk Map");
            Undo.RegisterCreatedObjectUndo(go, "Create cat walk map");
            holder = Undo.AddComponent<CatNavMesh>(go);
        }
        Undo.RecordObject(holder, "Bake cat walk map");
        holder.data = data;
        holder.bounds = area;
        if (house) holder.furniture = FindChild(house.transform, "Furniature") ?? FindChild(house.transform, "Furniture");
        Dirty(holder);
        done.Add("Baked the cat's walk map (G7l6 > Bake Cat Walk Map redoes it after you move furniture)");
    }

    /// <summary>Menu: G7l6 > Rebuild HUD. Throws away the HUD in the scene and builds a fresh one (e.g. after new art).</summary>
    [MenuItem("G7l6/Rebuild HUD")]
    public static void RebuildHud()
    {
        if (EditorApplication.isPlaying) { EditorUtility.DisplayDialog("Rebuild HUD", "Stop Play mode first.", "OK"); return; }
        if (!EditorUtility.DisplayDialog("Rebuild HUD", "This deletes the HUD in the scene (and any changes you made to it) and builds a new one. Continue?", "Rebuild", "Cancel"))
            return;

        done.Clear(); problems.Clear(); created.Clear();
        Undo.IncrementCurrentGroup();
        int group = Undo.GetCurrentGroup();
        SetUpHud(rebuild: true);
        Undo.CollapseUndoOperations(group);
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Selection.objects = created.ToArray();
    }

    /// <summary>Puts the hand-drawn HUD into the scene as real objects, so it can be moved and restyled in the editor.</summary>
    static void SetUpHud(bool rebuild)
    {
        var hud = Object.FindObjectOfType<GameHUD>();

        // Older setups put an empty GameHUD on the GameManager object - replace it.
        if (hud && (!hud.IsBuilt || rebuild))
        {
            if (hud.gameObject.GetComponents<Component>().Length <= 2 || hud.gameObject.name == "HUD")
                Undo.DestroyObjectImmediate(hud.gameObject);  // its own object: remove it all
            else
                Undo.DestroyObjectImmediate(hud);             // shares an object (GameManager): remove just the component
            hud = null;
        }
        if (hud)
        {
            done.Add("HUD is already in the scene (use G7l6 > Rebuild HUD to start it over)");
            return;
        }

        var root = new GameObject("HUD");
        hud = root.AddComponent<GameHUD>();
        hud.font = AssetDatabase.LoadAssetAtPath<TMPro.TMP_FontAsset>(HudFontPath);
        if (!hud.font) problems.Add($"Couldn't find the handwriting font at {HudFontPath} - the HUD uses the default font");
        HudBuilder.Build(hud);
        Undo.RegisterCreatedObjectUndo(root, "Create HUD");
        EditorUtility.SetDirty(hud);
        created.Add(root);
        done.Add("Hand-drawn HUD added to the scene as 'HUD' (select it to move or restyle things)");
    }

    static void SetUpPlayer()
    {
        var interaction = Object.FindObjectOfType<PlayerInteraction>();
        if (!interaction)
        {
            problems.Add("No PlayerInteraction found on the player");
            return;
        }
        Ensure<PlayerCarry>(interaction.gameObject);
        done.Add("Player can carry items (E pick up, G drop)");
    }

    static void HideOldUI()
    {
        foreach (var name in new[] { "SubTaskPanel(Keep at the bottom)", "StressMeter bar", "MainTaskPanel" })
        {
            var go = FindAny(name);
            if (go && go.activeSelf)
            {
                Undo.RecordObject(go, "Hide old UI");
                go.SetActive(false);
            }
        }
        done.Add("Hid the old task slots and stress bar (the new HUD replaces them)");
    }

    // ------------------------------------------------------------------ tasks

    /// <summary>Used when your room is redone: homework moves to the new desk.</summary>
    internal static string SetUpHomeworkDesk(GameObject desk)
    {
        done.Clear(); problems.Clear(); created.Clear();
        if (!desk) return "No desk could be placed, so homework has nowhere to go (see the Console).";
        SetUpHomework(desk);
        return problems.Count > 0 ? string.Join(System.Environment.NewLine, problems) : $"Homework is at the new desk ('{desk.name}').";
    }

    static void SetUpHomework(GameObject desk = null)
    {
        if (!desk) desk = FindActive("Desk Mechanical 1");
        if (!desk) { problems.Add("Couldn't find the homework desk ('Desk Mechanical 1')"); return; }

        var task = Ensure<TypingTask>(desk);
        Undo.RecordObject(task, "Configure homework");
        task.taskName = "Homework";
        task.isMainTask = true;
        task.canStartRandomly = false;
        task.timeLimit = 0f;
        task.stressOnFail = 0f;
        task.stressReliefOnComplete = 15f;
        task.pagesRequired = 3;
        Dirty(task);

        EnsureCollider(desk);
        EnsureOutline(desk);
        done.Add($"Homework (typing) at '{desk.name}' in {RoomOf(desk)}");
    }

    static void SetUpRemote(GameObject remote = null)
    {
        if (!remote) remote = FindActive("Controller (TV)");
        if (!remote) { problems.Add("Couldn't find the TV remote 'Controller (TV)'"); return; }

        RemoveMissingScripts(remote); // the old TvRemote script
        MakeCarryable(remote, ItemType.Remote, "TV remote", 0.25f);
        done.Add($"TV remote can be picked up ({RoomOf(remote)})");
    }

    static void SetUpTv(string objectName, string label)
    {
        var tv = FindActive(objectName);
        if (!tv) { problems.Add($"Couldn't find TV '{objectName}'"); return; }
        SetUpTv(tv, label, null);
    }

    static void SetUpTv(GameObject tv, string label, Renderer knownScreen)
    {

        var staticMat = AssetDatabase.LoadAssetAtPath<Material>(StaticMaterialPath);
        var offMat = AssetDatabase.LoadAssetAtPath<Material>(ScreenOffMaterialPath);

        var task = Ensure<TvTask>(tv);
        Undo.RecordObject(task, "Configure TV");
        task.taskName = label;
        task.timeLimit = 0f;
        task.stressOnFail = 0f;
        task.stressReliefOnComplete = 5f;
        task.cooldown = 25f;
        task.stressPerSecond = 0.6f;
        task.needsRemote = true;
        task.staticMaterial = staticMat;
        task.offMaterial = offMat;

        Renderer screen = knownScreen;
        int slot = 0;
        if (screen || FindScreenSlot(tv, staticMat, offMat, out screen, out slot))
        {
            task.screenRenderer = screen;
            task.screenMaterialIndex = slot;
        }
        else
        {
            problems.Add($"{label}: couldn't find the screen material. Drag the screen into TvTask > Screen Renderer");
        }

        var audio = tv.GetComponent<AudioSource>();
        if (!audio) audio = Undo.AddComponent<AudioSource>(tv);
        Undo.RecordObject(audio, "Configure TV audio");
        audio.clip = AssetDatabase.LoadAssetAtPath<AudioClip>(StaticSoundPath);
        audio.loop = true;
        audio.playOnAwake = false;
        audio.spatialBlend = 1f;
        audio.volume = 0.5f;
        audio.rolloffMode = AudioRolloffMode.Linear;
        audio.minDistance = 1.5f;
        audio.maxDistance = 16f;
        task.staticSound = audio;
        Dirty(audio);
        Dirty(task);

        EnsureCollider(tv);
        EnsureOutline(tv);
        done.Add($"{label} can go static");
    }

    static bool FindScreenSlot(GameObject tv, Material staticMat, Material offMat, out Renderer renderer, out int slot)
    {
        // First: a slot already using the static / screen-off material. Then: anything called "screen".
        for (int pass = 0; pass < 3; pass++)
        {
            foreach (var r in tv.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    if (!m) continue;
                    string mn = m.name.ToLowerInvariant();
                    bool match = pass == 0 ? (m == staticMat || m == offMat)
                        : pass == 1 ? mn.Contains("screen") || r.name.ToLowerInvariant().Contains("screen")
                        : !mn.Contains("palette");
                    if (match) { renderer = r; slot = i; return true; }
                }
            }
        }
        renderer = null; slot = 0;
        return false;
    }

    static void SetUpOven(GameObject oven = null)
    {
        if (!oven) oven = FindActive("Oven");
        if (!oven) { problems.Add("Couldn't find the 'Oven'"); return; }

        EnsureCollider(oven);
        EnsureOutline(oven);
        var bounds = WorldBounds(oven);
        var fire = SetUpFire(oven, bounds);

        var task = Ensure<OvenTask>(oven);
        Undo.RecordObject(task, "Configure oven");
        task.taskName = "Cook dinner";
        task.timeLimit = 60f;
        task.stressOnFail = 15f;
        task.stressReliefOnComplete = 8f;
        task.cooldown = 40f;
        task.cookTime = 40f;
        task.catchFireAt = 65f;
        task.fire = fire;

        var light = FindOrCreateChild(oven.transform, "Oven Light", out bool newLight);
        if (newLight) light.position = bounds.center + Vector3.up * 0.1f;
        var l = light.GetComponent<Light>();
        if (!l) l = Undo.AddComponent<Light>(light.gameObject);
        Undo.RecordObject(l, "Configure oven light");
        l.type = LightType.Point;
        l.color = new Color(1f, 0.5f, 0.15f);
        l.range = 2.2f;
        l.intensity = 2.5f;
        l.enabled = false;
        Dirty(l);
        task.ovenLight = l;

        var door = FindChild(oven.transform, "Oven Door");
        task.glowRenderers = door
            ? door.GetComponentsInChildren<Renderer>(true)
            : oven.GetComponentsInChildren<MeshRenderer>(true);
        Dirty(task);

        done.Add("Cook dinner on the oven (40s to cook, catches fire at 65s)");
    }

    static FireHazard SetUpFire(GameObject oven, Bounds ovenBounds)
    {
        var parent = oven.transform.parent;
        var root = FindOrCreateChild(parent, "Oven Fire", out bool isNew);
        if (isNew)
        {
            root.position = new Vector3(ovenBounds.center.x, ovenBounds.max.y - 0.05f, ovenBounds.center.z);
            root.rotation = Quaternion.identity;
            UnitScale(root);
        }

        var hazard = Ensure<FireHazard>(root.gameObject);
        var visual = FindOrCreateChild(root, "Flames", out _);
        visual.localPosition = Vector3.zero;
        visual.localRotation = Quaternion.identity;

        // Particles (pointing up)
        var particlesT = FindOrCreateChild(visual, "Particles", out bool newParticles);
        particlesT.localPosition = Vector3.zero;
        particlesT.localRotation = Quaternion.Euler(-90f, 0f, 0f);
        var ps = particlesT.GetComponent<ParticleSystem>();
        if (!ps) ps = Undo.AddComponent<ParticleSystem>(particlesT.gameObject);
        if (newParticles) ConfigureFlames(ps, ovenBounds);

        // Flickering light
        var lightT = FindOrCreateChild(visual, "Fire Light", out _);
        lightT.localPosition = Vector3.up * 0.4f;
        var light = lightT.GetComponent<Light>();
        if (!light) light = Undo.AddComponent<Light>(lightT.gameObject);
        Undo.RecordObject(light, "Configure fire light");
        light.type = LightType.Point;
        light.color = new Color(1f, 0.45f, 0.12f);
        light.range = 5f;
        light.intensity = 3f;
        Dirty(light);

        // Big trigger around the oven so looking anywhere at it hits the fire
        var hitT = FindOrCreateChild(visual, "Hitbox", out _);
        hitT.position = ovenBounds.center;
        hitT.rotation = Quaternion.identity;
        var box = hitT.GetComponent<BoxCollider>();
        if (!box) box = Undo.AddComponent<BoxCollider>(hitT.gameObject);
        Undo.RecordObject(box, "Configure fire hitbox");
        box.isTrigger = true;
        box.center = Vector3.zero;
        Vector3 s = hitT.lossyScale;
        Vector3 size = ovenBounds.size + Vector3.one * 0.4f;
        box.size = new Vector3(size.x / Mathf.Max(0.0001f, s.x), size.y / Mathf.Max(0.0001f, s.y), size.z / Mathf.Max(0.0001f, s.z));
        Dirty(box);

        Undo.RecordObject(hazard, "Configure fire");
        hazard.taskName = "Put out the fire!";
        hazard.requiredItem = ItemType.Extinguisher;
        hazard.requiredItemName = "fire extinguisher";
        hazard.actionVerb = "Spray";
        hazard.stressPerSecond = 3f;
        hazard.clearDuration = 2f;
        hazard.stressReliefOnComplete = 5f;
        hazard.cooldown = 0f;
        hazard.flames = ps;
        hazard.fireLight = light;
        hazard.visualRoot = visual.gameObject;
        Dirty(hazard);

        Undo.RecordObject(visual.gameObject, "Hide fire");
        visual.gameObject.SetActive(false);
        return hazard;
    }

    static void ConfigureFlames(ParticleSystem ps, Bounds ovenBounds)
    {
        Undo.RecordObject(ps, "Configure flames");
        var main = ps.main;
        main.duration = 1f;
        main.loop = true;
        main.playOnAwake = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, 0.9f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.6f, 1.5f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.25f, 0.55f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.85f, 0.3f), new Color(1f, 0.4f, 0.08f));
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 400;

        var emission = ps.emission;
        emission.rateOverTime = 55f;

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 12f;
        shape.radius = Mathf.Max(0.15f, Mathf.Min(ovenBounds.size.x, ovenBounds.size.z) * 0.35f);

        var color = ps.colorOverLifetime;
        color.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(new Color(1f, 0.9f, 0.45f), 0f), new GradientColorKey(new Color(1f, 0.45f, 0.1f), 0.4f), new GradientColorKey(new Color(0.45f, 0.08f, 0.04f), 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.1f), new GradientAlphaKey(0.7f, 0.55f), new GradientAlphaKey(0f, 1f) });
        color.color = new ParticleSystem.MinMaxGradient(gradient);

        var size = ps.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.3f));

        var noise = ps.noise;
        noise.enabled = true;
        noise.strength = 0.4f;
        noise.frequency = 1.5f;

        var renderer = ps.GetComponent<ParticleSystemRenderer>();
        Undo.RecordObject(renderer, "Configure flames");
        renderer.sharedMaterial = FireMaterial();
        Dirty(ps);
        Dirty(renderer);
    }

    static void SetUpLaundry(GameObject washer = null, GameObject clothesGo = null, string clothesRoom = null)
    {
        if (!washer) washer = FindActive("Washing Machine");
        if (!washer) { problems.Add("Couldn't find the 'Washing Machine'"); return; }

        EnsureCollider(washer);
        EnsureOutline(washer);
        var bounds = WorldBounds(washer);
        var flood = SetUpFlood(washer, bounds);

        Carryable clothes = null;
        if (!clothesGo) clothesGo = FindActive("clothes_001");
        if (clothesGo) clothes = MakeCarryable(clothesGo, ItemType.DirtyClothes, "dirty clothes", 0.5f);
        else problems.Add("Couldn't find 'clothes_001' (the dirty clothes pile)");

        var task = Ensure<LaundryTask>(washer);
        Undo.RecordObject(task, "Configure laundry");
        task.taskName = "Do the laundry";
        task.timeLimit = 120f;
        task.stressOnFail = 12f;
        task.stressReliefOnComplete = 8f;
        task.cooldown = 45f;
        task.washTime = 35f;
        task.overflowAfter = 25f;
        task.dirtyClothes = clothes;
        if (!string.IsNullOrEmpty(clothesRoom)) task.clothesLocation = clothesRoom;
        task.flood = flood;
        task.shakeTarget = washer.transform;

        var lightT = FindOrCreateChild(washer.transform, "Washer Light", out bool newLight);
        if (newLight) lightT.position = bounds.center + Vector3.up * 0.15f;
        var l = lightT.GetComponent<Light>();
        if (!l) l = Undo.AddComponent<Light>(lightT.gameObject);
        Undo.RecordObject(l, "Configure washer light");
        l.type = LightType.Point;
        l.color = new Color(0.55f, 0.8f, 1f);
        l.range = 1.8f;
        l.intensity = 1.5f;
        l.enabled = false;
        Dirty(l);
        task.runningLight = l;
        Dirty(task);

        done.Add($"Laundry: clothes {(string.IsNullOrEmpty(clothesRoom) ? "upstairs" : clothesRoom)} -> washer in {RoomOf(washer)} (35s), overflows 25s after it's done");
    }

    static FloodHazard SetUpFlood(GameObject washer, Bounds washerBounds) =>
        SetUpFloodHazard(washer.transform.parent, "Washer Flood",
            new Vector3(washerBounds.center.x, washerBounds.min.y + 0.012f, washerBounds.center.z));

    static FloodHazard SetUpFloodHazard(Transform parent, string name, Vector3 position)
    {
        var root = FindOrCreateChild(parent, name, out bool isNew);
        if (isNew)
        {
            root.position = position;
            root.rotation = Quaternion.identity;
            UnitScale(root);
        }
        var hazard = Ensure<FloodHazard>(root.gameObject);

        var puddle = FindChild(root, "Puddle");
        if (!puddle) puddle = CreatePuddle(root, true);

        Undo.RecordObject(hazard, "Configure flood");
        hazard.taskName = "Mop up the flood!";
        hazard.requiredItem = ItemType.Mop;
        hazard.requiredItemName = "mop";
        hazard.actionVerb = "Mop up";
        hazard.stressPerSecond = 1.5f;
        hazard.clearDuration = 3f;
        hazard.stressReliefOnComplete = 5f;
        hazard.cooldown = 0f;
        hazard.puddle = puddle;
        hazard.visualRoot = puddle.gameObject;
        Dirty(hazard);

        Undo.RecordObject(puddle.gameObject, "Hide puddle");
        puddle.gameObject.SetActive(false);
        return hazard;
    }

    static Transform CreatePuddle(Transform parent, bool withHitbox)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        go.name = "Puddle";
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = new Vector3(0.4f, 0.004f, 0.4f);
        var mr = go.GetComponent<MeshRenderer>();
        mr.sharedMaterial = WaterMaterial();
        mr.shadowCastingMode = ShadowCastingMode.Off;

        if (withHitbox)
        {
            // Thin trigger on top of the water so you can look down and mop it.
            var box = go.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.center = new Vector3(0f, 12.5f, 0f); // local units: 0..0.1m above the floor
            box.size = new Vector3(1f, 25f, 1f);
        }
        Undo.RegisterCreatedObjectUndo(go, "Create puddle");
        return go.transform;
    }

    // ------------------------------------------------------------------ ceiling leak

    static void SetUpCeilingLeak(GameObject sink = null)
    {
        var tap = SetUpUpstairsSink(sink);
        if (!tap) return;

        // The leak object. Its drip/puddle are moved under the tap when the leak starts.
        var furniture = FindActive("Furniature");
        var root = FindOrCreateChild(furniture ? furniture.transform : null, "Ceiling Leak", out bool isNew);
        if (isNew)
        {
            root.position = tap.transform.position;
            root.rotation = Quaternion.identity;
            UnitScale(root);
        }
        var task = Ensure<CeilingLeakTask>(root.gameObject);

        var visual = FindOrCreateChild(root, "Leak Visuals", out _);
        visual.localPosition = Vector3.zero;
        visual.localRotation = Quaternion.identity;

        var dripsT = FindOrCreateChild(visual, "Drips", out bool newDrips);
        dripsT.localRotation = Quaternion.identity;
        var drips = dripsT.GetComponent<ParticleSystem>();
        if (!drips) drips = Undo.AddComponent<ParticleSystem>(dripsT.gameObject);
        if (newDrips) ConfigureWater(drips, isTapStream: false);

        var puddle = FindChild(visual, "Puddle");
        if (!puddle) puddle = CreatePuddle(visual, false);

        var hitT = FindOrCreateChild(visual, "Hitbox", out _);
        var box = hitT.GetComponent<BoxCollider>();
        if (!box) box = Undo.AddComponent<BoxCollider>(hitT.gameObject);
        Undo.RecordObject(box, "Configure leak hitbox");
        box.isTrigger = true;
        box.size = new Vector3(0.9f, 3f, 0.9f);
        Dirty(box);

        var flood = SetUpFloodHazard(furniture ? furniture.transform : null, "Leak Flood", root.position);

        Carryable bucket = Object.FindObjectsOfType<Carryable>().FirstOrDefault(c => c.type == ItemType.Bucket);

        Undo.RecordObject(task, "Configure leak");
        task.taskName = "Stop the leak";
        task.timeLimit = 75f;
        task.stressOnFail = 12f;
        task.stressReliefOnComplete = 8f;
        task.cooldown = 50f;
        task.stressPerSecond = 0.8f;
        task.bucketCapacity = 60f;
        task.tap = tap;
        task.bucket = bucket;
        task.flood = flood;
        task.visualRoot = visual.gameObject;
        task.drips = drips;
        task.puddle = puddle;
        task.hitbox = box;
        Dirty(task);

        Undo.RecordObject(tap, "Link tap");
        tap.leak = task;
        Dirty(tap);

        Undo.RecordObject(visual.gameObject, "Hide leak");
        visual.gameObject.SetActive(false);

        done.Add($"Ceiling leak: the tap in {RoomOf(tap.gameObject)} runs -> drips through the ceiling right below it (bucket + turn off tap)");
    }

    /// <summary>The house has no upstairs bathroom, so copy the ground-floor bathroom sink into the master bedroom.</summary>
    static LeakTap SetUpUpstairsSink(GameObject sink = null)
    {
        if (!sink) sink = Object.FindObjectOfType<LeakTap>()?.gameObject ?? FindActive("Upstairs Sink");
        if (!sink)
        {
            var room = FindActive("MasterBedroom") ?? FindActive("bedroom");
            if (!room) { problems.Add("No upstairs room found for the leaking sink"); return null; }

            var bathroom = FindActive("F1Bathroom");
            var template = bathroom ? FindChild(bathroom.transform, "Sink")?.gameObject : null;
            sink = CreateUpstairsSink(template, room.transform);
            created.Add(sink);
            done.Add($"Copied the bathroom sink into '{room.name}' as 'Upstairs Sink' (it's selected - move/rotate it, the drip follows)");
        }

        var tap = Ensure<LeakTap>(sink);
        EnsureCollider(sink);
        EnsureOutline(sink);

        var waterT = FindOrCreateChild(sink.transform, "Tap Water", out bool newWater);
        if (newWater)
        {
            var b = WorldBounds(sink);
            waterT.position = new Vector3(b.center.x, b.center.y + b.extents.y * 0.6f, b.center.z);
            waterT.rotation = Quaternion.Euler(90f, 0f, 0f); // particles go +Z = down
        }
        var ps = waterT.GetComponent<ParticleSystem>();
        if (!ps) ps = Undo.AddComponent<ParticleSystem>(waterT.gameObject);
        if (newWater) ConfigureWater(ps, isTapStream: true);

        Undo.RecordObject(tap, "Configure tap");
        tap.runningWater = ps;
        Dirty(tap);
        return tap;
    }

    static GameObject CreateUpstairsSink(GameObject template, Transform room)
    {
        // Find the room's floor and its nearest wall (ignoring the room's furniture).
        var roomBounds = WorldBounds(room.gameObject);
        // Start below the ceiling (1m above the lowest furniture) so we hit the floor, not the roof.
        float floorY = FloorY(new Vector3(roomBounds.center.x, roomBounds.min.y + 1f, roomBounds.center.z), room, roomBounds.min.y);
        Vector3 probe = new Vector3(roomBounds.center.x, floorY + 1f, roomBounds.center.z);

        Vector3 wallDir = Vector3.forward, wallPoint = probe + Vector3.forward * 1.5f;
        float nearest = float.MaxValue;
        foreach (var dir in new[] { Vector3.forward, Vector3.back, Vector3.right, Vector3.left })
        {
            foreach (var h in Physics.RaycastAll(probe, dir, 12f, ~0, QueryTriggerInteraction.Ignore).OrderBy(h => h.distance))
            {
                if (h.collider.transform.IsChildOf(room)) continue; // furniture, not a wall
                if (h.distance < nearest) { nearest = h.distance; wallDir = dir; wallPoint = h.point; }
                break;
            }
        }

        GameObject sink;
        float heightAboveFloor = 0f;
        Quaternion rotation;
        var source = template ? PrefabUtility.GetCorrespondingObjectFromSource(template) : null;
        if (source)
        {
            sink = (GameObject)PrefabUtility.InstantiatePrefab(source, room.gameObject.scene);
            // Turn it so the side that faces the wall in the bathroom faces this wall.
            Vector3 back = BackDirection(template);
            float yaw = Vector3.SignedAngle(back, wallDir, Vector3.up);
            rotation = Quaternion.Euler(0f, yaw, 0f) * template.transform.rotation;
            var tb = WorldBounds(template);
            heightAboveFloor = template.transform.position.y - FloorY(tb.center, template.transform, tb.min.y);
        }
        else
        {
            sink = new GameObject("Upstairs Sink");
            var white = SolidMaterial("Sink White", new Color(0.93f, 0.93f, 0.95f));
            var metal = SolidMaterial("Tap Metal", new Color(0.6f, 0.62f, 0.65f));
            Part(PrimitiveType.Cube, "Cabinet", sink.transform, new Vector3(0f, 0.42f, 0f), new Vector3(0.6f, 0.84f, 0.45f), white);
            Part(PrimitiveType.Cylinder, "Tap", sink.transform, new Vector3(0f, 0.95f, -0.15f), new Vector3(0.04f, 0.1f, 0.04f), metal);
            rotation = Quaternion.LookRotation(-wallDir, Vector3.up); // back (-Z) to the wall
        }

        sink.name = "Upstairs Sink";
        Undo.RegisterCreatedObjectUndo(sink, "Create upstairs sink");
        sink.transform.SetParent(room, true);
        sink.transform.rotation = rotation;
        sink.transform.position = new Vector3(wallPoint.x, floorY + heightAboveFloor, wallPoint.z) - wallDir * 0.6f;

        // Slide it back until it touches the wall.
        var b = WorldBounds(sink);
        float edge = Vector3.Dot(b.center, wallDir) + Vector3.Dot(b.extents, new Vector3(Mathf.Abs(wallDir.x), Mathf.Abs(wallDir.y), Mathf.Abs(wallDir.z)));
        sink.transform.position += wallDir * (Vector3.Dot(wallPoint, wallDir) - edge - 0.02f);
        return sink;
    }

    /// <summary>Which horizontal side of the object is closest to a wall (its back).</summary>
    static Vector3 BackDirection(GameObject go)
    {
        var center = WorldBounds(go).center;
        var t = go.transform;
        Vector3 best = Flat(-t.forward);
        float nearest = float.MaxValue;
        foreach (var raw in new[] { t.forward, -t.forward, t.right, -t.right })
        {
            var dir = Flat(raw);
            foreach (var h in Physics.RaycastAll(center, dir, 2f, ~0, QueryTriggerInteraction.Ignore).OrderBy(h => h.distance))
            {
                if (h.collider.transform.IsChildOf(t)) continue;
                if (h.distance < nearest) { nearest = h.distance; best = dir; }
                break;
            }
        }
        return best;
    }

    static Vector3 Flat(Vector3 v)
    {
        v.y = 0f;
        return v.sqrMagnitude > 0.0001f ? v.normalized : Vector3.forward;
    }

    /// <summary>Height of the floor under a point, ignoring an object (and its children).</summary>
    static float FloorY(Vector3 from, Transform ignore, float fallback)
    {
        foreach (var h in Physics.RaycastAll(from, Vector3.down, 10f, ~0, QueryTriggerInteraction.Ignore).OrderBy(h => h.distance))
        {
            if (ignore && h.collider.transform.IsChildOf(ignore)) continue;
            return h.point.y;
        }
        return fallback;
    }

    static void ConfigureWater(ParticleSystem ps, bool isTapStream)
    {
        Undo.RecordObject(ps, "Configure water");
        var main = ps.main;
        main.loop = true;
        main.playOnAwake = !isTapStream; // the tap is started by LeakTap
        main.startSpeed = isTapStream ? 0.4f : 0f;
        main.gravityModifier = 1f;
        main.startLifetime = isTapStream ? 0.35f : 1f;
        main.startSize = isTapStream ? new ParticleSystem.MinMaxCurve(0.025f, 0.045f) : new ParticleSystem.MinMaxCurve(0.05f, 0.08f);
        main.startColor = new Color(0.6f, 0.8f, 1f, 0.85f);
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 200;

        var emission = ps.emission;
        emission.rateOverTime = isTapStream ? 70f : 6f;

        var shape = ps.shape;
        if (isTapStream)
        {
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 2f;
            shape.radius = 0.01f;
        }
        else
        {
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.12f;
        }

        var renderer = ps.GetComponent<ParticleSystemRenderer>();
        Undo.RecordObject(renderer, "Configure water");
        renderer.renderMode = ParticleSystemRenderMode.Stretch;
        renderer.lengthScale = 2.5f;
        renderer.velocityScale = 0.04f;
        renderer.sharedMaterial = AssetDatabase.GetBuiltinExtraResource<Material>("Default-ParticleSystem.mat");
        Dirty(ps);
        Dirty(renderer);
    }

    // ------------------------------------------------------------------ tools (extinguisher, mop)

    static void SetUpTools()
    {
        // Put the placeholders in the storage room next to its basket, if we can find it.
        Vector3 anchor = Vector3.zero;
        Transform parent = null;
        var storage = FindActive("StorageRoom");
        if (storage)
        {
            parent = storage.transform;
            var basket = FindChild(storage.transform, "Plastic Basket");
            anchor = basket ? WorldBounds(basket.gameObject).center : storage.transform.position;
        }
        else if (new[] { "extinguisher", "mop", "bucket" }.Any(n => !FindNameContains(n)))
            problems.Add("No 'StorageRoom' found - the extinguisher and mop were put at the scene origin");

        SetUpTool(ItemType.Extinguisher, "extinguisher", "fire extinguisher", "Fire Extinguisher (placeholder)", parent, anchor + new Vector3(0f, 0f, 0.7f));
        SetUpTool(ItemType.Mop, "mop", "mop", "Mop (placeholder)", parent, anchor + new Vector3(0f, 0f, -0.7f));
        SetUpTool(ItemType.Bucket, "bucket", "bucket", "Bucket (placeholder)", parent, anchor + new Vector3(-0.7f, 0f, 0f));
    }

    static void SetUpTool(ItemType type, string searchName, string displayName, string placeholderName, Transform parent, Vector3 near)
    {
        // Already set up?
        foreach (var c in Object.FindObjectsOfType<Carryable>())
            if (c.type == type) { done.Add($"{displayName}: using '{c.name}'"); return; }

        // A real model you added yourself, named e.g. "Fire Extinguisher" / "Mop"?
        var existing = FindNameContains(searchName);
        if (existing)
        {
            MakeCarryable(existing, type, displayName, 0.45f);
            done.Add($"{displayName}: '{existing.name}' in {RoomOf(existing)}");
            return;
        }

        var root = new GameObject(placeholderName);
        if (parent) root.transform.SetParent(parent, true);
        root.transform.position = FloorBelow(near);
        root.transform.rotation = Quaternion.identity;
        UnitScale(root.transform);

        if (type == ItemType.Extinguisher)
        {
            var red = SolidMaterial("Extinguisher Red", new Color(0.8f, 0.08f, 0.06f));
            var black = SolidMaterial("Extinguisher Black", new Color(0.08f, 0.08f, 0.08f));
            Part(PrimitiveType.Cylinder, "Body", root.transform, new Vector3(0f, 0.22f, 0f), new Vector3(0.16f, 0.22f, 0.16f), red);
            Part(PrimitiveType.Cylinder, "Valve", root.transform, new Vector3(0f, 0.47f, 0f), new Vector3(0.07f, 0.04f, 0.07f), black);
            Part(PrimitiveType.Cube, "Handle", root.transform, new Vector3(0.03f, 0.53f, 0f), new Vector3(0.13f, 0.02f, 0.03f), black);
            Part(PrimitiveType.Cube, "Hose", root.transform, new Vector3(-0.07f, 0.36f, 0f), new Vector3(0.02f, 0.22f, 0.02f), black);
        }
        else if (type == ItemType.Bucket)
        {
            var blue = SolidMaterial("Bucket Blue", new Color(0.15f, 0.4f, 0.8f));
            var grey = SolidMaterial("Extinguisher Black", new Color(0.08f, 0.08f, 0.08f));
            Part(PrimitiveType.Cylinder, "Body", root.transform, new Vector3(0f, 0.16f, 0f), new Vector3(0.3f, 0.16f, 0.3f), blue);
            Part(PrimitiveType.Cube, "Handle", root.transform, new Vector3(0f, 0.36f, 0f), new Vector3(0.3f, 0.015f, 0.015f), grey);
        }
        else
        {
            var wood = SolidMaterial("Mop Handle", new Color(0.55f, 0.38f, 0.2f));
            var head = SolidMaterial("Mop Head", new Color(0.85f, 0.85f, 0.8f));
            Part(PrimitiveType.Cylinder, "Handle", root.transform, new Vector3(0f, 0.62f, 0f), new Vector3(0.03f, 0.6f, 0.03f), wood);
            Part(PrimitiveType.Cube, "Head", root.transform, new Vector3(0f, 0.04f, 0f), new Vector3(0.34f, 0.07f, 0.14f), head);
        }

        Undo.RegisterCreatedObjectUndo(root, "Create " + placeholderName);
        MakeCarryable(root, type, displayName, 0.45f);
        created.Add(root);
        done.Add($"Placeholder {displayName} created in the storage room (it's selected - move it where you want)");
    }

    static void Part(PrimitiveType shape, string name, Transform parent, Vector3 localPos, Vector3 localScale, Material mat)
    {
        var go = GameObject.CreatePrimitive(shape);
        go.name = name;
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = localScale;
        go.GetComponent<MeshRenderer>().sharedMaterial = mat;
    }

    // ------------------------------------------------------------------ helpers

    static Carryable MakeCarryable(GameObject go, ItemType type, string displayName, float heldSize)
    {
        var c = Ensure<Carryable>(go);
        Undo.RecordObject(c, "Configure carryable");
        c.type = type;
        c.displayName = displayName;
        c.maxHeldSize = heldSize;
        Dirty(c);
        EnsureCollider(go);
        EnsureOutline(go);
        return c;
    }

    /// <summary>The room an object stands in: its furniture group's name, e.g. "Ground - Kitchen" -> "the kitchen".</summary>
    static string RoomOf(GameObject go)
    {
        for (var p = go ? go.transform.parent : null; p; p = p.parent)
        {
            int dash = p.name.IndexOf(" - ");
            if (dash > 0 && p.parent && (p.parent.name == "Furniture" || p.parent.name == "Furniature"))
                return "the " + p.name.Substring(dash + 3).ToLowerInvariant();
        }
        return go && go.transform.parent ? $"'{go.transform.parent.name}'" : "the house";
    }

    static T Ensure<T>(GameObject go) where T : Component
    {
        var c = go.GetComponent<T>();
        return c ? c : Undo.AddComponent<T>(go);
    }

    static void Dirty(Object o)
    {
        EditorUtility.SetDirty(o);
        if (PrefabUtility.IsPartOfPrefabInstance(o))
            PrefabUtility.RecordPrefabInstancePropertyModifications(o);
    }

    /// <summary>Raycasts need a solid collider. Adds a box around the model if it has none.</summary>
    static void EnsureCollider(GameObject go)
    {
        if (go.GetComponentsInChildren<Collider>(true).Any(c => !c.isTrigger)) return;

        var box = Undo.AddComponent<BoxCollider>(go);
        if (TryLocalBounds(go.transform, out Bounds local))
        {
            box.center = local.center;
            box.size = local.size;
        }
        Dirty(box);
    }

    static void EnsureOutline(GameObject go)
    {
        var outline = go.GetComponent<Outline>();
        if (!outline) outline = Undo.AddComponent<Outline>(go);
        Undo.RecordObject(outline, "Configure outline");
        outline.OutlineMode = Outline.Mode.OutlineVisible;
        outline.OutlineColor = Color.white;
        outline.OutlineWidth = 5f;
        outline.enabled = false;
        Dirty(outline);
    }

    static void RemoveMissingScripts(GameObject go)
    {
        try
        {
            Undo.RegisterCompleteObjectUndo(go, "Remove missing scripts");
            int removed = GameObjectUtility.RemoveMonoBehavioursWithMissingScript(go);
            if (removed > 0) done.Add($"Removed {removed} old script(s) from '{go.name}'");
        }
        catch (System.Exception e)
        {
            problems.Add($"'{go.name}' has an old missing script I couldn't remove: {e.Message}");
        }
    }

    static Bounds WorldBounds(GameObject go)
    {
        var renderers = go.GetComponentsInChildren<Renderer>(true).Where(r => r is MeshRenderer || r is SkinnedMeshRenderer).ToArray();
        if (renderers.Length == 0) return new Bounds(go.transform.position, Vector3.one * 0.5f);
        var b = renderers[0].bounds;
        foreach (var r in renderers) b.Encapsulate(r.bounds);
        return b;
    }

    static bool TryLocalBounds(Transform root, out Bounds local)
    {
        local = default;
        bool found = false;
        foreach (var r in root.GetComponentsInChildren<Renderer>(true))
        {
            if (!(r is MeshRenderer || r is SkinnedMeshRenderer)) continue;
            var b = r.bounds;
            for (int i = 0; i < 8; i++)
            {
                var corner = new Vector3(
                    (i & 1) == 0 ? b.min.x : b.max.x,
                    (i & 2) == 0 ? b.min.y : b.max.y,
                    (i & 4) == 0 ? b.min.z : b.max.z);
                var p = root.InverseTransformPoint(corner);
                if (!found) { local = new Bounds(p, Vector3.zero); found = true; }
                else local.Encapsulate(p);
            }
        }
        return found;
    }

    static Vector3 FloorBelow(Vector3 point)
    {
        if (Physics.Raycast(point + Vector3.up * 1.5f, Vector3.down, out RaycastHit hit, 6f, ~0, QueryTriggerInteraction.Ignore))
            return hit.point;
        return point;
    }

    static void UnitScale(Transform t)
    {
        var p = t.parent ? t.parent.lossyScale : Vector3.one;
        t.localScale = new Vector3(1f / Mathf.Max(0.0001f, p.x), 1f / Mathf.Max(0.0001f, p.y), 1f / Mathf.Max(0.0001f, p.z));
    }

    static Transform FindChild(Transform parent, string name)
    {
        if (!parent) return null;
        foreach (var t in parent.GetComponentsInChildren<Transform>(true))
            if (t != parent && t.name == name) return t;
        return null;
    }

    static Transform FindOrCreateChild(Transform parent, string name, out bool isNew)
    {
        var existing = parent ? parent.Find(name) : FindAny(name)?.transform;
        if (existing) { isNew = false; return existing; }

        var go = new GameObject(name);
        if (parent) go.transform.SetParent(parent, false);
        Undo.RegisterCreatedObjectUndo(go, "Create " + name);
        isNew = true;
        return go.transform;
    }

    static IEnumerable<Transform> AllTransforms()
    {
        foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                yield return t;
    }

    /// <summary>Finds an object that's actually in use (ignores leftovers under disabled parents like the old House).</summary>
    static GameObject FindActive(string name) =>
        AllTransforms().FirstOrDefault(t => t.name == name && t.gameObject.activeInHierarchy)?.gameObject;

    static GameObject FindAny(string name) =>
        AllTransforms().FirstOrDefault(t => t.name == name)?.gameObject;

    static GameObject FindNameContains(string part) =>
        AllTransforms().FirstOrDefault(t => t.gameObject.activeInHierarchy && t.name.ToLowerInvariant().Contains(part)
                                             && !t.name.Contains("(placeholder)"))?.gameObject;

    static void AddSceneToBuild(Scene scene)
    {
        if (string.IsNullOrEmpty(scene.path)) return;
        var scenes = EditorBuildSettings.scenes.Where(s => !string.IsNullOrEmpty(s.path) && s.path != scene.path).ToList();
        scenes.Insert(0, new EditorBuildSettingsScene(scene.path, true));
        EditorBuildSettings.scenes = scenes.ToArray();
        done.Add($"'{scene.name}' is now the first scene in Build Settings");
    }

    // ------------------------------------------------------------------ materials

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        int slash = path.LastIndexOf('/');
        string parent = path.Substring(0, slash);
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, path.Substring(slash + 1));
    }

    static Material SolidMaterial(string name, Color color)
    {
        string path = $"{TaskMaterials}/{name}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat) return mat;
        mat = new Material(Shader.Find("Standard")) { color = color };
        mat.SetFloat("_Glossiness", 0.35f);
        AssetDatabase.CreateAsset(mat, path);
        return mat;
    }

    static Material WaterMaterial()
    {
        string path = $"{TaskMaterials}/Flood Water.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat) return mat;

        mat = new Material(Shader.Find("Standard")) { color = new Color(0.3f, 0.55f, 0.85f, 0.55f) };
        // Standard shader "Fade" mode, set up by hand.
        mat.SetFloat("_Mode", 2f);
        mat.SetOverrideTag("RenderType", "Transparent");
        mat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
        mat.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
        mat.SetInt("_ZWrite", 0);
        mat.DisableKeyword("_ALPHATEST_ON");
        mat.EnableKeyword("_ALPHABLEND_ON");
        mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        mat.renderQueue = (int)RenderQueue.Transparent;
        mat.SetFloat("_Glossiness", 0.95f);
        AssetDatabase.CreateAsset(mat, path);
        return mat;
    }

    static Material FireMaterial()
    {
        string path = $"{TaskMaterials}/Fire.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat) return mat;

        var defaultParticle = AssetDatabase.GetBuiltinExtraResource<Material>("Default-ParticleSystem.mat");
        var shader = Shader.Find("Legacy Shaders/Particles/Additive");
        if (!shader) return defaultParticle;

        mat = new Material(shader);
        if (defaultParticle && defaultParticle.mainTexture) mat.mainTexture = defaultParticle.mainTexture;
        mat.SetColor("_TintColor", new Color(0.6f, 0.6f, 0.6f, 0.5f));
        AssetDatabase.CreateAsset(mat, path);
        return mat;
    }
}
