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
        Neighbourhood(centre, layout.transform.position.y, s, windows, camPos, camGo.transform.forward);

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

    // ---- the view out of the window

    static readonly Dictionary<string, Material> mats = new Dictionary<string, Material>();

    static Material Mat(string name, Color c, float gloss = 0.1f, Color? glow = null)
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

    static GameObject Prim(PrimitiveType type, Transform parent, Vector3 pos, Vector3 size, Material m, Quaternion? rot = null)
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
    static void Neighbourhood(Vector3 centre, float groundY, float s, List<Renderer> windows, Vector3 camPos, Vector3 camForward)
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

    static readonly Color Skin = new Color(0.94f, 0.76f, 0.62f), SkinDark = new Color(0.55f, 0.38f, 0.28f);

    /// <summary>A simple person: legs, a body, arms, a head (kids are just shorter).</summary>
    static GameObject Person(Transform parent, Vector3 feet, float height, Material shirt, bool kid)
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

    static GameObject Dog(Transform parent, Vector3 feet)
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

    static GameObject Car(Transform parent, Color colour)
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
    static void Park(Transform root, MenuWorld world, Vector3 c, Vector3 dir, float groundY, System.Func<float, float, float> R)
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
    static void Fair(Transform root, MenuWorld world, Vector3 at, Vector3 dir, float groundY)
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
    static void Skyline(Transform root, Vector3 at, float groundY, Vector3 keepClear, Material[] walls, Material[] roofs, Material[] leaves, Material trunk, System.Func<float, float, float> R)
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
    static void Sky(Transform root, MenuWorld world, Vector3 centre, Vector3 dir, float groundY, System.Func<float, float, float> R)
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
}
