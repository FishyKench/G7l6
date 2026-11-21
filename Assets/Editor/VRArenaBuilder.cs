using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering.PostProcessing;

/// <summary>
/// Menu: G7l6 > Set Up VR Match.
///   1. Builds the VR arena as its own scene (Assets/Scenes/VRArena.unity): the "Neon District" city map for 8v8
///      Domination (see VRMapBuilder.cs), far away from the house so both can be loaded at once. Edit it freely
///      afterwards (keep the "Spawn A/B" and "Flag A/B/C" objects); running this again rebuilds it.
///   2. Adds it to Build Settings (after the house).
///   3. Puts a VR headset on your gaming desk in NewHouse and moves the ranked match onto it. Nothing else in the
///      house changes. Both scenes are saved.
/// </summary>
public static partial class VRArenaBuilder
{
    const string ArenaPath = "Assets/Scenes/VRArena.unity";
    const string MaterialFolder = "Assets/Materials/VR";
    static readonly Vector3 ArenaOrigin = new Vector3(5000f, 0f, 0f);

    [MenuItem("G7l6/Set Up VR Match")]
    public static void SetUp()
    {
        if (EditorApplication.isPlaying) { Debug.LogWarning("[VR] Stop Play mode first."); return; }
        var house = SceneManager.GetActiveScene();
        if (house.path != HouseBuilder.TargetScene)
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            house = EditorSceneManager.OpenScene(HouseBuilder.TargetScene, OpenSceneMode.Single);
        }

        BuildArenaScene();
        SceneManager.SetActiveScene(house);
        AddToBuild();
        string headset = PlaceHeadset();
        EditorSceneManager.MarkSceneDirty(house);
        EditorSceneManager.SaveScene(house);
        Debug.Log($"[VR] Built {ArenaPath} and added it to Build Settings.\n{headset}\nBoth scenes are saved. Press Play, walk to your room and press E on the headset.");
    }

    /// <summary>
    /// Menu: G7l6 > Rebuild VR Arena Map. Rebuilds only the arena scene (the city map). The house isn't touched.
    /// </summary>
    [MenuItem("G7l6/Rebuild VR Arena Map")]
    public static void RebuildArena()
    {
        if (EditorApplication.isPlaying) { Debug.LogWarning("[VR] Stop Play mode first."); return; }
        var active = SceneManager.GetActiveScene();
        BuildArenaScene();
        if (active.IsValid() && active.isLoaded) SceneManager.SetActiveScene(active);
        AddToBuild();
        Debug.Log($"[VR] Rebuilt the arena map ({ArenaPath}): Neon District, 8v8 Domination with points A, B and C. The house wasn't changed.");
    }

    // ------------------------------------------------------------------ the arena scene

    static void BuildArenaScene()
    {
        EnsureFolder(MaterialFolder);
        var existing = SceneManager.GetSceneByPath(ArenaPath);
        if (existing.isLoaded) EditorSceneManager.CloseScene(existing, true);

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        SceneManager.SetActiveScene(scene);

        RenderSettings.skybox = Sky();
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.46f, 0.44f, 0.62f); // bright enough to see cars, cover and your gun
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogDensity = 0.0075f;
        RenderSettings.fogColor = new Color(0.04f, 0.03f, 0.1f);

        var root = new GameObject("VR Arena").transform;
        root.position = ArenaOrigin;
        root.gameObject.AddComponent<VRArena>();
        root.gameObject.AddComponent<VRAmbience>();

        var cyan = Mat("VR Neon Cyan", new Color(0.2f, 0.9f, 1f), new Color(0.2f, 0.9f, 1f), 2.2f, 0.5f);
        var pink = Mat("VR Neon Pink", new Color(1f, 0.25f, 0.75f), new Color(1f, 0.25f, 0.75f), 2.2f, 0.5f);

        BuildCity(root);

        // Lights.
        var sun = new GameObject("Arena light").AddComponent<Light>();
        sun.transform.SetParent(root, false);
        sun.type = LightType.Directional;
        sun.color = new Color(0.62f, 0.68f, 1f);
        sun.intensity = 0.75f; // moonlight over the whole map
        sun.shadows = LightShadows.Soft;
        sun.shadowStrength = 0.55f;
        sun.transform.rotation = Quaternion.Euler(55f, 30f, 0f);

        Skyline(root);
        Hologram(root, cyan, pink);
        Sweeps(root);
        PostEffects(root);
        EditorSceneManager.SaveScene(scene, ArenaPath);
        EditorSceneManager.CloseScene(scene, true);
    }

    static void Box(string name, Transform parent, Vector3 localPos, Vector3 size, Material m)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = size;
        go.GetComponent<Renderer>().sharedMaterial = m;
        go.isStatic = true;
    }

    static void Trim(Transform parent, Vector3 localPos, Vector3 size, Material m)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = "Neon trim";
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = size;
        var r = go.GetComponent<Renderer>();
        r.sharedMaterial = m;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    static void Pad(Transform parent, Vector3 localPos, Material m)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        go.name = "Spawn pad";
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = new Vector3(6f, 0.02f, 10f);
        go.GetComponent<Renderer>().sharedMaterial = m;
    }

    static void Marker(string name, Transform parent, Vector3 localPos, Quaternion rot)
    {
        var t = new GameObject(name).transform;
        t.SetParent(parent, false);
        t.localPosition = localPos;
        t.localRotation = rot;
    }

    static void Lamp(Transform parent, Vector3 localPos, Color c, float range, float intensity)
    {
        var l = new GameObject("Light").AddComponent<Light>();
        l.transform.SetParent(parent, false);
        l.transform.localPosition = localPos;
        l.type = LightType.Point;
        l.color = c;
        l.range = range;
        l.intensity = intensity;
        l.shadows = LightShadows.None;
    }

    static Material FloorMaterial()
    {
        string texPath = $"{MaterialFolder}/VR Grid.png";
        if (!File.Exists(texPath))
        {
            const int size = 256;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                bool line = x < 3 || y < 3 || x > size - 4 || y > size - 4;
                bool fine = x % 64 < 1 || y % 64 < 1;
                px[y * size + x] = line ? new Color32(80, 230, 255, 255) : fine ? new Color32(40, 70, 120, 255) : new Color32(14, 16, 30, 255);
            }
            tex.SetPixels32(px);
            File.WriteAllBytes(texPath, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(texPath);
        }
        var grid = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
        var m = Mat("VR Floor", Color.white, new Color(0.35f, 0.75f, 0.9f), 0.9f, 0.6f);
        m.mainTexture = grid;
        m.mainTextureScale = new Vector2(16f, 11f); // a 4 m grid
        m.SetTexture("_EmissionMap", grid);
        EditorUtility.SetDirty(m);
        return m;
    }

    static Material Mat(string name, Color color, Color emission, float strength, float gloss)
    {
        string path = $"{MaterialFolder}/{name}.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!m) { m = new Material(Shader.Find("Standard")); AssetDatabase.CreateAsset(m, path); }
        m.color = color;
        m.SetFloat("_Glossiness", gloss);
        if (strength > 0f)
        {
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", emission * strength);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        }
        else m.DisableKeyword("_EMISSION");
        EditorUtility.SetDirty(m);
        return m;
    }

    static void AddToBuild()
    {
        var scenes = EditorBuildSettings.scenes.ToList();
        if (!scenes.Any(s => s.path == ArenaPath)) scenes.Add(new EditorBuildSettingsScene(ArenaPath, true));
        EditorBuildSettings.scenes = scenes.ToArray();
    }

    // ------------------------------------------------------------------ the headset in the house

    // ------------------------------------------------------------------ looks: post effects, sky, skyline, motion

    static void PostEffects(Transform root)
    {
        // The camera you'll see through in VR, with post-processing (bloom makes the neon glow).
        var camGo = new GameObject("VR Camera");
        camGo.transform.SetParent(root, false);
        camGo.transform.localPosition = new Vector3(-54f, 1.6f, 0f);
        var cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.Skybox;
        cam.allowHDR = true;
        cam.fieldOfView = 80f;
        cam.nearClipPlane = 0.03f;
        cam.enabled = false;
        var layer = camGo.AddComponent<PostProcessLayer>();
        var resources = AssetDatabase.LoadAssetAtPath<PostProcessResources>("Packages/com.unity.postprocessing/PostProcessing/PostProcessResources.asset");
        if (resources) layer.Init(resources);
        else Debug.LogWarning("[VR] Couldn't find the post-processing resources - the arena will look flatter.");
        layer.volumeTrigger = camGo.transform;
        layer.volumeLayer = 1 << 0; // Default
        layer.antialiasingMode = PostProcessLayer.Antialiasing.FastApproximateAntialiasing;

        string profilePath = $"{MaterialFolder}/VR Post FX.asset";
        if (AssetDatabase.LoadAssetAtPath<PostProcessProfile>(profilePath)) AssetDatabase.DeleteAsset(profilePath);
        var profile = ScriptableObject.CreateInstance<PostProcessProfile>();
        AssetDatabase.CreateAsset(profile, profilePath);
        var bloom = profile.AddSettings<Bloom>();
        bloom.intensity.Override(1.6f);
        bloom.threshold.Override(1.25f);
        bloom.softKnee.Override(0.55f);
        bloom.diffusion.Override(5.5f);
        var grading = profile.AddSettings<ColorGrading>();
        grading.tonemapper.Override(Tonemapper.ACES);
        grading.postExposure.Override(0.2f);
        grading.saturation.Override(22f);
        grading.contrast.Override(18f);
        grading.colorFilter.Override(new Color(1f, 0.96f, 1.04f));
        var vignette = profile.AddSettings<Vignette>();
        vignette.intensity.Override(0.32f);
        vignette.smoothness.Override(0.45f);
        var aberration = profile.AddSettings<ChromaticAberration>();
        aberration.intensity.Override(0.12f);
        foreach (var s in profile.settings) AssetDatabase.AddObjectToAsset(s, profile);
        EditorUtility.SetDirty(profile);
        AssetDatabase.SaveAssets();

        var volumeGo = new GameObject("Post FX (global)");
        volumeGo.transform.SetParent(root, false);
        var volume = volumeGo.AddComponent<PostProcessVolume>();
        volume.isGlobal = true;
        volume.priority = 1f;
        volume.sharedProfile = profile;
    }

    static Material Sky()
    {
        string path = $"{MaterialFolder}/VR Sky.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!m) { m = new Material(Shader.Find("Skybox/Procedural")); AssetDatabase.CreateAsset(m, path); }
        m.SetFloat("_SunSize", 0f);
        m.SetFloat("_AtmosphereThickness", 1.6f);
        m.SetColor("_SkyTint", new Color(0.55f, 0.25f, 0.85f));
        m.SetColor("_GroundColor", new Color(0.03f, 0.02f, 0.08f));
        m.SetFloat("_Exposure", 0.45f);
        EditorUtility.SetDirty(m);
        return m;
    }

    /// <summary>A glowing city skyline all round the arena (you see it over the walls).</summary>
    static void Skyline(Transform root)
    {
        var dark = Mat("VR Tower", new Color(0.05f, 0.05f, 0.1f), Color.black, 0f, 0.6f);
        var colours = new[]
        {
            Mat("VR Neon Cyan", new Color(0.2f, 0.9f, 1f), new Color(0.2f, 0.9f, 1f), 2.2f, 0.5f),
            Mat("VR Neon Pink", new Color(1f, 0.25f, 0.75f), new Color(1f, 0.25f, 0.75f), 2.2f, 0.5f),
            Mat("VR Neon Violet", new Color(0.55f, 0.3f, 1f), new Color(0.55f, 0.3f, 1f), 2.4f, 0.5f),
        };
        var city = new GameObject("Skyline").transform;
        city.SetParent(root, false);
        var rand = new System.Random(42);
        for (int i = 0; i < 70; i++)
        {
            float angle = (float)rand.NextDouble() * Mathf.PI * 2f;
            float dist = 82f + (float)rand.NextDouble() * 70f;
            float w = 4f + (float)rand.NextDouble() * 7f, d = 4f + (float)rand.NextDouble() * 7f;
            float h = 30f + (float)rand.NextDouble() * (dist > 110f ? 70f : 40f);
            var at = new Vector3(Mathf.Cos(angle) * dist * 1.25f, h * 0.5f - 2f, Mathf.Sin(angle) * dist);
            var tower = GameObject.CreatePrimitive(PrimitiveType.Cube);
            tower.name = "Tower";
            Object.DestroyImmediate(tower.GetComponent<Collider>());
            tower.transform.SetParent(city, false);
            tower.transform.localPosition = at;
            tower.transform.localScale = new Vector3(w, h, d);
            tower.GetComponent<Renderer>().sharedMaterial = dark;
            var neon = colours[rand.Next(colours.Length)];
            // A glowing roof edge and a few window bands.
            Trim(city, at + Vector3.up * (h * 0.5f + 0.1f), new Vector3(w + 0.3f, 0.25f, d + 0.3f), neon, i % 3 == 0 ? "Pulse roof" : "Roof");
            int bands = rand.Next(1, 4);
            for (int b = 0; b < bands; b++)
            {
                float y = at.y - h * 0.5f + h * (0.25f + 0.6f * (float)rand.NextDouble());
                Trim(city, new Vector3(at.x, y, at.z), new Vector3(w + 0.06f, 0.18f, d + 0.06f), neon, "Band");
            }
        }
    }

    /// <summary>A slowly spinning hologram ring over the centre block.</summary>
    static void Hologram(Transform root, Material a, Material b)
    {
        var holo = new GameObject("Spin hologram").transform;
        holo.SetParent(root, false);
        holo.localPosition = new Vector3(0f, 6.2f, 0f);
        for (int i = 0; i < 24; i++)
        {
            float angle = i / 24f * Mathf.PI * 2f;
            Trim(holo, new Vector3(Mathf.Cos(angle) * 3f, 0f, Mathf.Sin(angle) * 3f), new Vector3(0.5f, 0.12f, 0.12f), i % 2 == 0 ? a : b, "Ring", Quaternion.Euler(0f, -angle * Mathf.Rad2Deg + 90f, 0f));
        }
        var inner = new GameObject("Spin inner").transform;
        inner.SetParent(holo, false);
        inner.localPosition = new Vector3(0f, 0.8f, 0f);
        for (int i = 0; i < 12; i++)
        {
            float angle = i / 12f * Mathf.PI * 2f;
            Trim(inner, new Vector3(Mathf.Cos(angle) * 1.6f, 0f, Mathf.Sin(angle) * 1.6f), new Vector3(0.35f, 0.08f, 0.08f), b, "Ring", Quaternion.Euler(0f, -angle * Mathf.Rad2Deg + 90f, 0f));
        }
        var glow = new GameObject("Pulse hologram light").AddComponent<Light>();
        glow.transform.SetParent(holo, false);
        glow.type = LightType.Point;
        glow.color = new Color(0.5f, 0.8f, 1f);
        glow.range = 14f;
        glow.intensity = 2f;
        glow.shadows = LightShadows.None;
    }

    /// <summary>Two big spotlights sweeping round the arena.</summary>
    static void Sweeps(Transform root)
    {
        for (int i = 0; i < 2; i++)
        {
            var pivot = new GameObject("Spin sweep").transform;
            pivot.SetParent(root, false);
            pivot.localPosition = new Vector3(i == 0 ? -14f : 14f, 14f, 0f);
            var spot = new GameObject("Sweep light").AddComponent<Light>();
            spot.transform.SetParent(pivot, false);
            spot.transform.localRotation = Quaternion.Euler(62f, 0f, 0f);
            spot.type = LightType.Spot;
            spot.spotAngle = 32f;
            spot.range = 45f;
            spot.intensity = 3.5f;
            spot.color = i == 0 ? new Color(0.35f, 0.85f, 1f) : new Color(1f, 0.35f, 0.8f);
            spot.shadows = LightShadows.None;
        }
    }

    /// <summary>Glowing stripes on the inside of the walls and lanes on the floor.</summary>
    static void Stripes(Transform root, Material cyan, Material pink)
    {
        for (float x = -28f; x <= 28f; x += 8f)
        {
            Trim(root, new Vector3(x, 2.5f, 21.95f), new Vector3(0.12f, 4.2f, 0.06f), pink, "Pulse stripe");
            Trim(root, new Vector3(x, 2.5f, -21.95f), new Vector3(0.12f, 4.2f, 0.06f), pink, "Pulse stripe");
        }
        for (float z = -16f; z <= 16f; z += 8f)
        {
            Trim(root, new Vector3(-31.95f, 2.5f, z), new Vector3(0.06f, 4.2f, 0.12f), cyan, "Pulse stripe");
            Trim(root, new Vector3(31.95f, 2.5f, z), new Vector3(0.06f, 4.2f, 0.12f), cyan, "Pulse stripe");
        }
        Trim(root, new Vector3(0f, 0.015f, 0f), new Vector3(0.15f, 0.02f, 42f), cyan, "Centre line");
        Trim(root, new Vector3(-20f, 0.015f, 0f), new Vector3(0.1f, 0.02f, 30f), cyan, "Lane");
        Trim(root, new Vector3(20f, 0.015f, 0f), new Vector3(0.1f, 0.02f, 30f), pink, "Lane");
    }

    static void Trim(Transform parent, Vector3 localPos, Vector3 size, Material m, string name, Quaternion? rotation = null)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        if (rotation.HasValue) go.transform.localRotation = rotation.Value;
        go.transform.localScale = size;
        var r = go.GetComponent<Renderer>();
        r.sharedMaterial = m;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    // ------------------------------------------------------------------ the gaming PC in the house

    /// <summary>
    /// Your gaming desk becomes the PC with both apps: homework (TypingTask) and the ranked match (MatchTask),
    /// run by a GamingPC that remembers what you had open. The VR headset sits on the desk as part of it.
    /// Homework comes off any other desk.
    /// </summary>
    internal static string SetUpGamingPC()
    {
        var house = SceneManager.GetActiveScene().GetRootGameObjects().FirstOrDefault(g => g.name == "NewHouse");
        var room = house ? house.transform.Find("Furniture/First - Your bedroom") : null;
        if (!room) return "Couldn't find your room (NewHouse/Furniture/First - Your bedroom) - no gaming PC set up.";

        var desk = room.GetComponentsInChildren<GamingPC>(true).FirstOrDefault()?.gameObject;
        if (!desk) desk = room.Cast<Transform>().FirstOrDefault(t => t.gameObject.activeInHierarchy && t.name.Contains("Desk"))?.gameObject;
        if (!desk) return "No desk in your room - no gaming PC set up.";

        // Old setups: the match on the headset or anywhere else, homework on another desk.
        foreach (var m in Object.FindObjectsOfType<MatchTask>(true)) if (m.gameObject != desk) Undo.DestroyObjectImmediate(m);
        foreach (var t in Object.FindObjectsOfType<TypingTask>(true)) if (t.gameObject != desk) Undo.DestroyObjectImmediate(t);

        HouseTaskSetup.SetUpHomeworkDesk(desk);
        var match = desk.GetComponent<MatchTask>();
        if (!match) match = Undo.AddComponent<MatchTask>(desk);
        match.taskName = "Ranked match";
        match.isMainTask = true;
        match.canStartRandomly = false;
        if (!desk.GetComponent<GamingPC>()) Undo.AddComponent<GamingPC>(desk);
        EditorUtility.SetDirty(desk);

        // The headset, on the desk to one side of the monitor (part of the PC: looking at it uses the PC).
        foreach (var old in room.GetComponentsInChildren<Transform>(true).Where(t => t.name == "VR Headset").ToList())
            Undo.DestroyObjectImmediate(old.gameObject);
        var headset = BuildHeadset(room);
        Physics.SyncTransforms();
        var b = Bounds(desk);
        Vector3 side = desk.transform.right * Vector3.Dot(b.extents, Abs(desk.transform.right)) * 0.62f;
        Vector3 front = desk.transform.forward * Vector3.Dot(b.extents, Abs(desk.transform.forward)) * 0.25f;
        Vector3 probe = b.center + side + front;
        probe.y = b.max.y + 1f;
        Vector3 spot = Physics.Raycast(probe, Vector3.down, out var hit, 3f) ? hit.point : new Vector3(probe.x, b.max.y, probe.z);
        headset.transform.rotation = desk.transform.rotation * Quaternion.Euler(0f, 160f, 0f);
        headset.transform.position = spot + Vector3.up * 0.01f;
        headset.transform.SetParent(desk.transform, true);
        Undo.RegisterCreatedObjectUndo(headset, "VR headset");
        return $"Gaming PC on '{desk.name}': homework + ranked match (VR headset on the desk). Homework removed from other desks.";
    }

    /// <summary>Kept for older callers.</summary>
    internal static string PlaceHeadset() => SetUpGamingPC();

    static GameObject BuildHeadset(Transform room)
    {
        var go = new GameObject("VR Headset");
        go.transform.SetParent(room, false);
        // Real-world size, whatever the house scale is.
        Vector3 parentScale = room.lossyScale;
        go.transform.localScale = new Vector3(1f / parentScale.x, 1f / parentScale.y, 1f / parentScale.z) * 1.1f;
        var shell = Mat("VR Headset Shell", new Color(0.12f, 0.12f, 0.15f), Color.black, 0f, 0.7f);
        var face = Mat("VR Headset Visor", new Color(0.05f, 0.05f, 0.07f), new Color(0.2f, 0.9f, 1f), 0.8f, 0.95f);
        var strap = Mat("VR Headset Strap", new Color(0.2f, 0.2f, 0.25f), Color.black, 0f, 0.2f);
        Part(go.transform, PrimitiveType.Cube, "Body", new Vector3(0f, 0.055f, 0f), new Vector3(0.19f, 0.1f, 0.11f), shell);
        Part(go.transform, PrimitiveType.Cube, "Visor", new Vector3(0f, 0.055f, 0.058f), new Vector3(0.17f, 0.075f, 0.01f), face);
        Part(go.transform, PrimitiveType.Cube, "Pad", new Vector3(0f, 0.055f, -0.06f), new Vector3(0.17f, 0.08f, 0.02f), strap);
        Part(go.transform, PrimitiveType.Cube, "Strap L", new Vector3(-0.1f, 0.05f, -0.1f), new Vector3(0.015f, 0.03f, 0.18f), strap);
        Part(go.transform, PrimitiveType.Cube, "Strap R", new Vector3(0.1f, 0.05f, -0.1f), new Vector3(0.015f, 0.03f, 0.18f), strap);
        Part(go.transform, PrimitiveType.Cube, "Strap back", new Vector3(0f, 0.05f, -0.19f), new Vector3(0.2f, 0.03f, 0.015f), strap);
        return go;
    }

    static void Part(Transform parent, PrimitiveType shape, string name, Vector3 pos, Vector3 size, Material m)
    {
        var p = GameObject.CreatePrimitive(shape);
        p.name = name;
        Object.DestroyImmediate(p.GetComponent<Collider>());
        p.transform.SetParent(parent, false);
        p.transform.localPosition = pos;
        p.transform.localScale = size;
        p.GetComponent<Renderer>().sharedMaterial = m;
    }

    static Bounds Bounds(GameObject go)
    {
        var rs = go.GetComponentsInChildren<Renderer>();
        if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.one * 0.5f);
        var b = rs[0].bounds;
        foreach (var r in rs) b.Encapsulate(r.bounds);
        return b;
    }

    static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        var parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }
}
