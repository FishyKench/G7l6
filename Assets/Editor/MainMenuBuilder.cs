using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.PostProcessing;
using UnityEngine.SceneManagement;

/// <summary>
/// G7l6 > Build Main Menu Scene: makes Assets/Scenes/MainMenu.unity - your bedroom, copied out of NewHouse (just what you see:
/// meshes, materials and lamps - no gameplay scripts), at golden hour: a low orange sun through the window, a sunset sky, the
/// house's post-processing. Kitty (the model only) naps on the bed; a screen over your monitor shows the VR arena live; the
/// MainMenu script does the rest (the corkboard menu, the drifting camera...). NewHouse itself is only read, never changed.
/// The menu goes first in the build list (the game starts there). Run it again any time you change your room.
/// Also runs once from a request file: Library/G7l6/main-menu.request.
/// </summary>
[InitializeOnLoad]
public static class MainMenuBuilder
{
    const string HousePath = "Assets/Scenes/NewHouse.unity";
    const string ScenePath = "Assets/Scenes/MainMenu.unity";
    const string SkyPath = "Assets/Scenes/MainMenu Sky.mat";
    const string Request = "Library/G7l6/main-menu.request";

    static MainMenuBuilder()
    {
        if (!File.Exists(Request)) return;
        EditorApplication.delayCall += () =>
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || !File.Exists(Request)) return;
            File.Delete(Request);
            Build();
        };
    }

    /// <summary>For batch runs: -executeMethod MainMenuBuilder.BuildFromCommandLine</summary>
    public static void BuildFromCommandLine()
    {
        EditorSceneManager.OpenScene(HousePath, OpenSceneMode.Single);
        Build();
        EditorApplication.Exit(0);
    }

    [MenuItem("G7l6/Build Main Menu Scene")]
    public static void Build()
    {
        TownKit.MaterialMaker = SavedMat; // (the menu's outdoor materials are saved with the scene)
        // The menu scene itself can't be open while it's rebuilt (Unity won't save over an open scene): close it, reopen after.
        var openMenu = SceneManager.GetSceneByPath(ScenePath);
        bool reopenMenu = openMenu.isLoaded, menuWasActive = reopenMenu && SceneManager.GetActiveScene() == openMenu;
        if (reopenMenu)
        {
            if (openMenu.isDirty && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return; // (you cancelled)
            if (SceneManager.sceneCount == 1) EditorSceneManager.OpenScene(HousePath, OpenSceneMode.Single);
            else EditorSceneManager.CloseScene(openMenu, true);
        }
        var house = SceneManager.GetSceneByPath(HousePath);
        bool openedHere = false;
        if (!house.isLoaded) { house = EditorSceneManager.OpenScene(HousePath, OpenSceneMode.Additive); openedHere = true; }
        var roots = house.GetRootGameObjects();
        var layout = roots.SelectMany(r => r.GetComponentsInChildren<HouseLayout>(true)).FirstOrDefault();
        if (!layout) { Debug.LogError("[Main menu] No HouseLayout in NewHouse."); return; }
        var room = layout.rooms.FirstOrDefault(r => r.name == "Your bedroom");
        if (room == null) { Debug.LogError("[Main menu] No 'Your bedroom' in the house plan."); return; }
        float s = layout.scale;
        float floorY = layout.transform.position.y + room.floor * layout.storeyHeight * s;
        Vector3 a = layout.transform.TransformPoint(new Vector3(room.rect.xMin, room.floor * layout.storeyHeight, room.rect.yMin));
        Vector3 b = layout.transform.TransformPoint(new Vector3(room.rect.xMax, room.floor * layout.storeyHeight, room.rect.yMax));
        var box = new Bounds((a + b) * 0.5f, Vector3.zero);
        box.Encapsulate(new Vector3(a.x, floorY - 0.3f, a.z));
        box.Encapsulate(new Vector3(b.x, floorY + layout.storeyHeight * s + 0.1f, b.z));
        box.Expand(new Vector3(0.6f, 0f, 0.6f));
        Vector3 centre = new Vector3(box.center.x, floorY, box.center.z);

        var menu = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        SceneManager.SetActiveScene(menu);

        // ---- your room: what you see of it
        var roomRoot = new GameObject("Your room (copy of NewHouse)");
        var copies = new List<(MeshRenderer src, GameObject copy)>();
        foreach (var r in roots.SelectMany(g => g.GetComponentsInChildren<MeshRenderer>()))
        {
            if (!r.enabled || !r.gameObject.activeInHierarchy || !box.Intersects(r.bounds)) continue;
            if (r.GetComponentInParent<Canvas>() || r.GetComponentInParent<CatBrain>() || r.GetComponentInParent<PlayerMovementAdvanced>() || r.GetComponent<TMP_Text>()) continue;
            if (r.GetComponentInParent<Camera>() || r.GetComponentInParent<VRArena>()) continue;
            var mf = r.GetComponent<MeshFilter>();
            if (!mf || !mf.sharedMesh) continue;
            var go = new GameObject(r.name);
            go.transform.SetPositionAndRotation(r.transform.position, r.transform.rotation);
            go.transform.localScale = r.transform.lossyScale;
            go.transform.SetParent(roomRoot.transform, true);
            go.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterials = r.sharedMaterials;
            mr.shadowCastingMode = r.shadowCastingMode;
            mr.receiveShadows = r.receiveShadows;
            Unlock(go, r.transform);
            // (solid: the cat's laser dot lands on it, she sits on it)
            if (mf.sharedMesh.isReadable && r.GetComponent<MeshCollider>()) go.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
            else if ((r.GetComponent<Collider>() || mf.sharedMesh.isReadable) && Mirrored(go.transform)) { if (mf.sharedMesh.isReadable) go.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh; } // (box colliders can't be mirrored)
            else if (r.GetComponent<Collider>() || mf.sharedMesh.isReadable) go.AddComponent<BoxCollider>();
            copies.Add((r, go));
        }
        // the lamps in it (dimmed: the sun does the work at this time of day)
        foreach (var l in roots.SelectMany(g => g.GetComponentsInChildren<Light>()))
        {
            if (!l.enabled || l.type == LightType.Directional || !box.Contains(l.transform.position)) continue;
            var go = new GameObject(l.name);
            go.transform.SetPositionAndRotation(l.transform.position, l.transform.rotation);
            go.transform.SetParent(roomRoot.transform, true);
            var nl = go.AddComponent<Light>();
            nl.type = l.type; nl.color = l.color; nl.range = l.range; nl.spotAngle = l.spotAngle;
            nl.intensity = l.intensity * 0.3f;
            nl.shadows = LightShadows.None;
            Unlock(go, l.transform);
        }

        // ---- the desk / PC, the monitor, the window
        var pc = roots.SelectMany(g => g.GetComponentsInChildren<GamingPC>(true)).FirstOrDefault();
        Vector3 deskPoint = pc ? pc.transform.position : centre;
        var lookAt = new GameObject("Desk (camera target)").transform;
        lookAt.position = deskPoint;
        var window = copies.Where(c => c.copy.name.StartsWith("Window")).Select(c => c.src).OrderBy(w => Vector3.Distance(w.bounds.center, centre)).FirstOrDefault();

        // ---- the camera: across the room, looking at the desk
        Vector3 away = Vector3.ProjectOnPlane(centre - deskPoint, Vector3.up);
        if (away.sqrMagnitude < 0.01f) away = Vector3.forward;
        Vector3 camPos = centre + away.normalized * 1.1f * s + Vector3.up * 1.55f * s;
        var camGo = new GameObject("Menu Camera") { tag = "MainCamera" };
        camGo.transform.position = camPos;
        camGo.transform.LookAt(deskPoint + Vector3.up * 0.55f * s + Vector3.ProjectOnPlane(window ? window.bounds.center - deskPoint : Vector3.zero, Vector3.up) * 0.25f);
        var cam = camGo.AddComponent<Camera>();
        cam.nearClipPlane = 0.05f;
        cam.fieldOfView = 62f;
        cam.renderingPath = RenderingPath.DeferredShading;
        // the corkboard covers the left of the screen: turn so the desk sits just right of it (on a 16:9 screen)
        {
            cam.aspect = 16f / 9f;
            float hfov = 2f * Mathf.Atan(Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * cam.aspect) * Mathf.Rad2Deg;
            Vector3 deskTop = deskPoint + Vector3.up * 0.55f * s;
            for (int i = 0; i < 12; i++)
            {
                float vx = cam.WorldToViewportPoint(deskTop).x;
                if (Mathf.Abs(vx - 0.6f) < 0.01f) break;
                camGo.transform.Rotate(0f, (vx - 0.6f) * hfov * 0.9f, 0f, Space.World);
            }
            cam.ResetAspect();
        }
        camGo.AddComponent<AudioListener>();
        var houseCam = roots.SelectMany(g => g.GetComponentsInChildren<Camera>(true)).FirstOrDefault(c => c.CompareTag("MainCamera")) ?? roots.SelectMany(g => g.GetComponentsInChildren<Camera>(true)).FirstOrDefault();
        var ppl = houseCam ? houseCam.GetComponent<PostProcessLayer>() : null;
        if (ppl && ComponentUtility.CopyComponent(ppl)) ComponentUtility.PasteComponentAsNew(camGo);
        foreach (var v in roots.SelectMany(g => g.GetComponentsInChildren<PostProcessVolume>(true)))
        {
            var vgo = new GameObject("Post-processing (" + (v.sharedProfile ? v.sharedProfile.name : "volume") + ")") { layer = v.gameObject.layer };
            if (ComponentUtility.CopyComponent(v)) ComponentUtility.PasteComponentAsNew(vgo);
        }

        // ---- the monitor screens: a quad over each monitor at the desk (the arena feed goes on the one you have)
        var screens = new List<Renderer>();
        foreach (var mon in copies.Where(c => c.copy.name.ToLowerInvariant().Contains("monitor") && Vector3.Distance(c.src.bounds.center, deskPoint) < 2.2f * s))
        {
            var monitor = mon.src;
            var mb = monitor.bounds;
            Vector3 facing = Vector3.ProjectOnPlane(camPos - mb.center, Vector3.up).normalized;
            // the screen is the monitor's broad face (its thin side is its depth), the one towards the room
            Vector3 ax = mb.size.x < mb.size.z ? new Vector3(Mathf.Sign(facing.x == 0f ? 1f : facing.x), 0f, 0f) : new Vector3(0f, 0f, Mathf.Sign(facing.z == 0f ? 1f : facing.z));
            float depth = Mathf.Abs(Vector3.Dot(mb.extents, ax));
            float width = Mathf.Abs(Vector3.Dot(mb.size, new Vector3(Mathf.Abs(ax.z), 0f, Mathf.Abs(ax.x))));
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Object.DestroyImmediate(q.GetComponent<Collider>());
            q.name = "Monitor feed";
            q.transform.position = mb.center + ax * (depth + 0.004f) + Vector3.up * mb.size.y * 0.08f;
            q.transform.rotation = Quaternion.LookRotation(-ax);
            q.transform.localScale = new Vector3(width * 0.9f, mb.size.y * 0.58f, 1f);
            var screen = q.GetComponent<Renderer>();
            screen.enabled = false; // (switched on when the feed's running)
            Unlock(q, monitor.transform);
            screens.Add(screen);
        }
        screens.Sort((x, y) => Vector3.Distance(x.bounds.center, deskPoint).CompareTo(Vector3.Distance(y.bounds.center, deskPoint)));

        // ---- Kitty: the model, napping on the bed
        Transform catCopy = null;
        Vector3[] catSpots = new Vector3[0];
        int[] catLinks = new int[0];
        Vector3[] catPerchTops = new Vector3[0];
        int[] catPerchLaunch = new int[0];
        string[] catPerchKinds = new string[0];
        var catSrc = roots.SelectMany(g => g.GetComponentsInChildren<CatBrain>(true)).FirstOrDefault();
        if (catSrc)
        {
            var c = Object.Instantiate(catSrc.gameObject);
            c.name = "Kitty (menu)";
            SceneManager.MoveGameObjectToScene(c, menu);
            c.transform.localScale = catSrc.transform.lossyScale;
            // no scripts at all (some depend on others: take them off in passes)
            for (int pass = 0; pass < 6 && c.GetComponentsInChildren<MonoBehaviour>(true).Length > 0; pass++)
                foreach (var mbh in c.GetComponentsInChildren<MonoBehaviour>(true).Reverse()) if (mbh) Object.DestroyImmediate(mbh);
            foreach (var cc in c.GetComponentsInChildren<CharacterController>(true)) Object.DestroyImmediate(cc);
            foreach (var ag in c.GetComponentsInChildren<UnityEngine.AI.NavMeshAgent>(true)) Object.DestroyImmediate(ag);
            foreach (var rb in c.GetComponentsInChildren<Rigidbody>(true)) Object.DestroyImmediate(rb);
            foreach (var col in c.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(col);
            // on the bed if there is one - otherwise curled up on your gaming chair
            var bed = copies.Where(x => x.copy.name.ToLowerInvariant().Contains("bed")).Select(x => x.src).OrderByDescending(x => x.bounds.size.x * x.bounds.size.z).FirstOrDefault();
            var seat = copies.Where(x => x.copy.name == "Seat" && x.src.transform.parent && x.src.transform.parent.name.Contains("Chair")).Select(x => x.src).FirstOrDefault();
            // in plain view: curled up on the floor halfway to the desk, a little to the right (the menu's on the left)
            Vector3 camFloor = new Vector3(camPos.x, floorY, camPos.z), deskFloor = new Vector3(deskPoint.x, floorY, deskPoint.z);
            Vector3 right = Vector3.Cross(Vector3.up, (deskFloor - camFloor).normalized);
            Vector3 spot = Vector3.Lerp(camFloor, deskFloor, 0.42f) + right * 0.55f * s;
            if (Physics.Raycast(spot + Vector3.up * 1f, Vector3.down, out var floorHit, 2f)) spot = floorHit.point;
            c.transform.position = spot;
            c.transform.rotation = Quaternion.LookRotation(Vector3.ProjectOnPlane(camPos - spot, Vector3.up).normalized) * Quaternion.Euler(0f, 50f, 0f);
            catCopy = c.transform;
            CatRoutes(room, layout, floorY, s, out catSpots, out catLinks);
            CatPerches(room, layout, floorY, s, catSpots, deskPoint, copies.Where(x => x.copy.name.StartsWith("Window")).Select(x => (Renderer)x.src).ToList(),
                out catPerchTops, out catPerchLaunch, out catPerchKinds);
        }

        // ---- outside: the garden, the fence, trees and the neighbours' houses
        var windows = copies.Where(c => c.copy.name.StartsWith("Window")).Select(c => (Renderer)c.src).ToList();
        TownKit.Neighbourhood(centre, layout.transform.position.y, s, windows, camPos, camGo.transform.forward);

        // ---- golden hour: a low sun through the window, a warm sky
        var sun = new GameObject("Sun (golden hour)").AddComponent<Light>();
        sun.type = LightType.Directional;
        sun.color = new Color(1f, 0.63f, 0.36f);
        sun.intensity = 1.7f;
        sun.shadows = LightShadows.Soft;
        sun.shadowStrength = 0.85f;
        Vector3 into = window ? Vector3.ProjectOnPlane(centre - window.bounds.center, Vector3.up).normalized : Vector3.forward;
        sun.transform.rotation = Quaternion.LookRotation(into + Vector3.down * 0.22f + Vector3.Cross(Vector3.up, into) * 0.35f);
        // a warm beam of evening sun through the window, across the floor and up the wall by the desk
        if (window)
        {
            var beam = new GameObject("Sunbeam").AddComponent<Light>();
            beam.type = LightType.Spot;
            beam.color = new Color(1f, 0.58f, 0.28f);
            beam.intensity = 3.2f;
            beam.range = 14f * s;
            beam.spotAngle = 55f;
            beam.innerSpotAngle = 30f;
            beam.shadows = LightShadows.Soft;
            Vector3 outward = -into;
            beam.transform.position = window.bounds.center + outward * 1.2f * s + Vector3.up * 0.9f * s;
            Vector3 target = Vector3.Lerp(new Vector3(deskPoint.x, floorY, deskPoint.z), centre, 0.45f);
            beam.transform.rotation = Quaternion.LookRotation(target - beam.transform.position);
            // dust drifting in the beam
            var dust = new GameObject("Dust in the sunbeam").AddComponent<ParticleSystem>();
            dust.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            Vector3 dustA = window.bounds.center + into * 0.3f * s, dustB = target + Vector3.up * 0.4f * s;
            dust.transform.position = Vector3.Lerp(dustA, dustB, 0.5f);
            dust.transform.rotation = Quaternion.LookRotation(dustB - dustA);
            var main = dust.main;
            main.loop = true; main.prewarm = true; main.playOnAwake = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(6f, 10f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.008f * s, 0.018f * s);
            main.startColor = new Color(1f, 0.85f, 0.6f, 0.55f);
            main.maxParticles = 160;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = dust.emission; em.rateOverTime = 18f;
            var shape = dust.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(0.9f * s, 0.9f * s, Vector3.Distance(dustA, dustB));
            var noise = dust.noise; noise.enabled = true; noise.strength = 0.04f; noise.frequency = 0.3f; noise.scrollSpeed = 0.1f;
            var vel = dust.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(-0.01f, 0.01f); vel.y = new ParticleSystem.MinMaxCurve(-0.012f, 0.006f); vel.z = new ParticleSystem.MinMaxCurve(-0.01f, 0.01f);
            var fade = dust.colorOverLifetime; fade.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.3f), new GradientAlphaKey(1f, 0.7f), new GradientAlphaKey(0f, 1f) });
            fade.color = g;
            var pr = dust.GetComponent<ParticleSystemRenderer>();
            pr.sharedMaterial = AssetDatabase.GetBuiltinExtraResource<Material>("Default-Particle.mat");
            pr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            pr.receiveShadows = false;
        }
        var sky = AssetDatabase.LoadAssetAtPath<Material>(SkyPath);
        if (!sky)
        {
            sky = new Material(Shader.Find("Skybox/Procedural"));
            AssetDatabase.CreateAsset(sky, SkyPath);
        }
        sky.SetFloat("_SunDisk", 2f);
        sky.SetFloat("_SunSize", 0.06f);
        sky.SetFloat("_SunSizeConvergence", 4f);
        sky.SetFloat("_AtmosphereThickness", 1.25f);
        sky.SetColor("_SkyTint", new Color(0.5f, 0.5f, 0.5f));      // (a natural sky: the low sun turns it orange by itself)
        sky.SetColor("_GroundColor", new Color(0.42f, 0.33f, 0.3f));
        sky.SetFloat("_Exposure", 1.35f);
        EditorUtility.SetDirty(sky);
        RenderSettings.skybox = sky;
        RenderSettings.sun = sun;
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.62f, 0.48f, 0.5f);
        RenderSettings.ambientEquatorColor = new Color(0.5f, 0.37f, 0.32f);
        RenderSettings.ambientGroundColor = new Color(0.2f, 0.15f, 0.14f);
        // a warm evening haze: the far-off things fade softly into the sky
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogStartDistance = 70f;
        RenderSettings.fogEndDistance = 900f;
        RenderSettings.fogColor = new Color(0.98f, 0.8f, 0.6f);
        Lightmapping.giWorkflowMode = Lightmapping.GIWorkflowMode.OnDemand;

        // ---- the menu itself
        var windowMark = new GameObject("Window (birds fly past)").transform;
        if (window) { windowMark.position = window.bounds.center; windowMark.rotation = Quaternion.LookRotation(-into); }
        var menuGo = new GameObject("Main menu");
        var mm = menuGo.AddComponent<MainMenu>();
        mm.cam = cam;
        mm.lookAt = lookAt;
        mm.monitorScreens = screens.ToArray();
        mm.cat = catCopy;
        mm.catSpots = catSpots;
        mm.catLinks = catLinks;
        mm.catPerchTops = catPerchTops;
        mm.catPerchLaunch = catPerchLaunch;
        mm.catPerchKinds = catPerchKinds;
        mm.windowCentre = window ? windowMark : null;
        var hud = roots.SelectMany(g => g.GetComponentsInChildren<GameHUD>(true)).FirstOrDefault();
        if (hud) mm.font = hud.font;

        string report = $"perches: {string.Join(", ", catPerchKinds)}; {copies.Count} pieces of your room ({copies.Count(c => c.copy.GetComponent<MenuUnlock>())} on unlocks), {screens.Count} monitor screens, cat {(catCopy ? "on " + catCopy.position.ToString("F2") + $", {catSpots.Length} spots / {catLinks.Length / 2} paths" : "none")}, window {(window ? window.name : "none")}";
        bool saved = EditorSceneManager.SaveScene(menu, ScenePath);
        SceneManager.SetActiveScene(house);
        EditorSceneManager.CloseScene(menu, true);
        if (openedHere && SceneManager.sceneCount > 1) EditorSceneManager.CloseScene(house, true);
        if (!saved) { Debug.LogError("[Main menu] Couldn't save " + ScenePath + " - nothing was changed."); return; }
        if (reopenMenu) EditorSceneManager.OpenScene(ScenePath, menuWasActive ? OpenSceneMode.Single : OpenSceneMode.Additive);

        // ---- first in the build list (the game starts at the menu)
        var list = EditorBuildSettings.scenes.Where(x => x.path != ScenePath).ToList();
        list.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
        foreach (var path in new[] { HousePath, "Assets/Scenes/VRArena.unity" })
            if (!list.Any(x => x.path == path) && File.Exists(path)) list.Add(new EditorBuildSettingsScene(path, true));
        EditorBuildSettings.scenes = list.ToArray();
        AssetDatabase.SaveAssets();
        Debug.Log($"[Main menu] Built {ScenePath}: {report}.");
    }

    /// <summary>A copied piece that's only there once earned (a RoomUnlock on it or above it) keeps that key.</summary>
    static void Unlock(GameObject copy, Transform source)
    {
        var keys = new List<string>();
        for (var t = source; t; t = t.parent)
        {
            var u = t.GetComponent<RoomUnlock>();
            if (u && !string.IsNullOrEmpty(u.key) && u.key != "always") keys.Add(u.key);
        }
        if (keys.Count == 0) return;
        copy.AddComponent<MenuUnlock>().key = string.Join("+", keys);
    }

    /// <summary>Open floor in the room for the cat (from the house's real colliders), and the straight paths between spots that are clear.</summary>
    static void CatRoutes(HouseLayout.RoomInfo room, HouseLayout layout, float floorY, float s, out Vector3[] spots, out int[] links)
    {
        var list = new List<Vector3>();
        for (float x = room.rect.xMin + 0.5f; x <= room.rect.xMax - 0.5f; x += 0.55f)
            for (float z = room.rect.yMin + 0.5f; z <= room.rect.yMax - 0.5f; z += 0.55f)
            {
                Vector3 p = layout.transform.TransformPoint(new Vector3(x, room.floor * layout.storeyHeight, z));
                p.y = floorY;
                if (!Physics.Raycast(p + Vector3.up * 0.6f, Vector3.down, out var hit, 1.2f, ~0, QueryTriggerInteraction.Ignore)) continue;
                if (hit.point.y > floorY + 0.06f) continue; // on furniture
                p = hit.point;
                if (Physics.CheckCapsule(p + Vector3.up * 0.28f, p + Vector3.up * 0.5f, 0.24f, ~0, QueryTriggerInteraction.Ignore)) continue;
                list.Add(p);
            }
        var l = new List<int>();
        for (int i = 0; i < list.Count; i++)
            for (int j = i + 1; j < list.Count; j++)
            {
                float d = Vector3.Distance(list[i], list[j]);
                if (d > 2.6f * s) continue;
                Vector3 a = list[i] + Vector3.up * 0.3f, b = list[j] + Vector3.up * 0.3f;
                if (Physics.SphereCast(a, 0.2f, (b - a).normalized, out _, d, ~0, QueryTriggerInteraction.Ignore)) continue;
                l.Add(i); l.Add(j);
            }
        spots = list.ToArray();
        links = l.ToArray();
    }

    /// <summary>
    /// Places she can jump up to (from the house's real colliders): a clear bit of the desk by the monitor, the bed, the chair, the
    /// window sills. Each with the floor spot she jumps from.
    /// </summary>
    static void CatPerches(HouseLayout.RoomInfo room, HouseLayout layout, float floorY, float s, Vector3[] floor, Vector3 desk, List<Renderer> windows,
        out Vector3[] tops, out int[] launch, out string[] kinds)
    {
        var found = new List<(Vector3 top, string kind, float room)>();
        for (float x = room.rect.xMin + 0.25f; x <= room.rect.xMax - 0.25f; x += 0.2f)
            for (float z = room.rect.yMin + 0.25f; z <= room.rect.yMax - 0.25f; z += 0.2f)
            {
                Vector3 p = layout.transform.TransformPoint(new Vector3(x, room.floor * layout.storeyHeight, z));
                p.y = floorY + 2.2f * s;
                if (!Physics.Raycast(p, Vector3.down, out var hit, 2.4f * s, ~0, QueryTriggerInteraction.Ignore)) continue;
                float h = hit.point.y - floorY;
                if (h < 0.35f * s || h > 1.15f * s || hit.normal.y < 0.85f) continue;
                // room for a cat on it: nothing above, a flat bit round it
                if (Physics.CheckCapsule(hit.point + Vector3.up * 0.22f, hit.point + Vector3.up * 0.45f, 0.16f, ~0, QueryTriggerInteraction.Ignore)) continue;
                bool flat = true;
                foreach (var o in new[] { Vector3.forward, Vector3.back, Vector3.left, Vector3.right })
                    if (!Physics.Raycast(hit.point + o * 0.16f + Vector3.up * 0.1f, Vector3.down, out var h2, 0.2f) || Mathf.Abs(h2.point.y - hit.point.y) > 0.03f) flat = false;
                if (!flat) continue;
                string n = hit.collider.name.ToLowerInvariant() + " " + (hit.collider.transform.parent ? hit.collider.transform.parent.name.ToLowerInvariant() : "");
                string kind = n.Contains("bed") ? "bed" : n.Contains("chair") || n.Contains("seat") ? "chair" : n.Contains("desk") || n.Contains("top") ? "desk" : n.Contains("window") || n.Contains("sill") ? "sill" : "furniture";
                if (windows.Any(w => Vector3.Distance(w.bounds.ClosestPoint(hit.point), hit.point) < 0.5f)) kind = "sill";
                // how much room round it (a cat turning round up there mustn't go through the monitor)
                float space = 0f;
                foreach (float r in new[] { 0.5f, 0.45f, 0.4f, 0.35f, 0.3f, 0.25f })
                    if (!Physics.CheckSphere(hit.point + Vector3.up * (r + 0.05f), r, ~0, QueryTriggerInteraction.Ignore)) { space = r; break; }
                if (space <= 0f) continue;
                found.Add((hit.point, kind, space));
            }
        // the best of each kind: the desk spot nearest the PC (but not on the keyboard), the middle of the bed, a sill...
        var chosen = new List<(Vector3 top, string kind, float room)>();
        foreach (var kind in new[] { "desk", "bed", "chair", "sill", "furniture" })
        {
            var options = found.Where(f => f.kind == kind).ToList();
            if (options.Count == 0) continue;
            int want = kind == "sill" ? 2 : 1;
            var ordered = kind == "desk" ? options.OrderByDescending(f => Mathf.Min(f.room, 0.4f)).ThenBy(f => Mathf.Abs(Vector3.Distance(f.top, desk) - 0.7f * s)).ToList()
                        : options.OrderByDescending(f => Mathf.Min(f.room, 0.4f)).ThenBy(f => Vector3.Distance(f.top, options.Aggregate(Vector3.zero, (a, b) => a + b.top) / options.Count)).ToList();
            foreach (var o in ordered)
            {
                if (chosen.Count(c => c.kind == kind) >= want) break;
                if (chosen.Any(c => Vector3.Distance(c.top, o.top) < 1f)) continue;
                chosen.Add(o);
            }
        }
        var t = new List<Vector3>(); var l = new List<int>(); var k = new List<string>();
        foreach (var c in chosen)
        {
            int best = -1; float bestD = float.MaxValue;
            for (int i = 0; i < floor.Length; i++)
            {
                Vector3 d = floor[i] - c.top; d.y = 0f;
                float dist = d.magnitude;
                if (dist > 1.3f * s || dist >= bestD) continue;
                // a clear jump: straight up from the floor to just above the top, then across onto it
                Vector3 a = floor[i] + Vector3.up * 0.15f, up = new Vector3(floor[i].x, c.top.y + 0.35f, floor[i].z), b = c.top + Vector3.up * 0.35f;
                if (Physics.Linecast(a, up, ~0, QueryTriggerInteraction.Ignore) || Physics.Linecast(up, b, ~0, QueryTriggerInteraction.Ignore)) continue;
                bestD = dist; best = i;
            }
            if (best < 0) continue;
            t.Add(c.top); l.Add(best); k.Add(c.kind);
        }
        tops = t.ToArray(); launch = l.ToArray(); kinds = k.ToArray();
    }

    // ---- the view out of the window: TownKit (runtime, shared with the game) builds it; here its materials are saved as assets

    static readonly Dictionary<string, Material> mats = new Dictionary<string, Material>();

    static Material SavedMat(string name, Color c, float gloss = 0.1f, Color? glow = null)
    {
        if (mats.TryGetValue(name, out var m) && m) return m;
        string dir = "Assets/Scenes/MainMenu Materials";
        if (!AssetDatabase.IsValidFolder(dir)) AssetDatabase.CreateFolder("Assets/Scenes", "MainMenu Materials");
        string path = dir + "/" + name + ".mat";
        m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!m) { m = new Material(Shader.Find("Standard")); AssetDatabase.CreateAsset(m, path); }
        m.color = c;
        m.SetFloat("_Glossiness", gloss);
        if (glow.HasValue) { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", glow.Value); m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None; }
        EditorUtility.SetDirty(m);
        mats[name] = m;
        return m;
    }

    static bool Mirrored(Transform t) { var l = t.lossyScale; return l.x < 0f || l.y < 0f || l.z < 0f; }

    static Material Mat(string name, Color c, float gloss = 0.1f, Color? glow = null) => TownKit.Mat(name, c, gloss, glow);
    static GameObject Prim(PrimitiveType type, Transform parent, Vector3 pos, Vector3 size, Material m, Quaternion? rot = null) => TownKit.Prim(type, parent, pos, size, m, rot);
}
