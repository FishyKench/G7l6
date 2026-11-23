using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.PostProcessing;
using UnityEngine.SceneManagement;

/// <summary>
/// How the game looks, and keeping it light to run.
///
/// Speed: the house has ~65 lamps. In Unity's default (forward) rendering every object is drawn again for each lamp that touches
/// it (20-40 ms of CPU a frame). Deferred rendering draws each object once and adds the lamps as cheap screen passes, all still
/// per-pixel - about 4x faster for the same picture. Applied to every 3D camera when a scene loads (the house, the VR arena).
///
/// Shadows on a budget: a lamp's shadows are drawn six ways round (a cube) every frame, so only the few lamps nearest you cast them
/// (<see cref="ShadowLamps"/>) - you always see real shadows where you are, and the cost stays fixed.
///
/// The look: a layer of post-processing on top of the scene's own profiles (they're left as they are): stronger ambient occlusion
/// (soft contact shadows in corners and under furniture), filmic (ACES) tone mapping with a little more contrast, colour and warmth,
/// a gentle bloom on lamps and windows, and a soft vignette. Screen-space reflections stay off, as before.
/// </summary>
public static class GraphicsTuning
{
    /// <summary>How many lamps (nearest you, in reach) cast shadows at once.</summary>
    public static int ShadowLamps = 3;
    /// <summary>The look layer (post-processing on top) - on by default.</summary>
    public static bool LookOn { get; private set; } = true;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        Apply();
        SceneManager.sceneLoaded -= OnLoaded;
        SceneManager.sceneLoaded += OnLoaded;
        var go = new GameObject("Graphics tuning");
        go.hideFlags = HideFlags.HideInHierarchy;
        Object.DontDestroyOnLoad(go);
        go.AddComponent<Watcher>();
    }

    static void OnLoaded(Scene scene, LoadSceneMode mode) => Apply();

    /// <summary>Cameras made after a scene loads (the VR rig, the debug camera) and the lamp shadows: kept up to date.</summary>
    class Watcher : MonoBehaviour
    {
        float next, nextShadows;
        void Update()
        {
            if (Time.unscaledTime >= next)
            {
                next = Time.unscaledTime + 2f;
                foreach (var cam in Camera.allCameras) if (cam.renderingPath != RenderingPath.DeferredShading) Setup(cam);
            }
            if (Time.unscaledTime >= nextShadows)
            {
                nextShadows = Time.unscaledTime + 0.25f;
                LampShadows();
            }
        }
    }

    /// <summary>Sets up every camera / post-processing volume that's there now (safe to call again).</summary>
    public static void Apply()
    {
        foreach (var cam in Object.FindObjectsOfType<Camera>(true)) Setup(cam);
        foreach (var volume in Object.FindObjectsOfType<PostProcessVolume>(true))
        {
            if (volume.name == LookName || !volume.sharedProfile || !volume.sharedProfile.HasSettings<ScreenSpaceReflections>()) continue;
            // (the volume's own copy - never the profile asset itself)
            if (volume.profile.TryGetSettings(out ScreenSpaceReflections ssr)) ssr.enabled.Override(false);
        }
        SetLook(LookOn);
    }

    /// <summary>A 3D camera drawing the world: deferred. (Orthographic / UI cameras are left alone.)</summary>
    public static void Setup(Camera cam)
    {
        if (!cam || cam.orthographic || cam.clearFlags == CameraClearFlags.Depth || cam.clearFlags == CameraClearFlags.Nothing) return;
        cam.renderingPath = RenderingPath.DeferredShading;
        cam.allowMSAA = false; // (deferred can't use MSAA; the post-processing FXAA does the edges)
    }

    // ------------------------------------------------------------------ shadows on a budget

    static readonly Dictionary<Light, bool> lamps = new Dictionary<Light, bool>();
    static readonly List<(Light light, float d)> inReach = new List<(Light, float)>();
    static float nextLampScan;

    static void LampShadows()
    {
        var cam = Camera.main;
        if (!cam) return;
        if (Time.unscaledTime >= nextLampScan)
        {
            nextLampScan = Time.unscaledTime + 4f;
            foreach (var l in Object.FindObjectsOfType<Light>())
            {
                if (lamps.ContainsKey(l) || l.type == LightType.Directional) continue;
                // not the fire, Mom, the fuse box's warning light or your phone torch
                bool mine = l.GetComponentInParent<FireHazard>() || l.GetComponentInParent<MomInspector>() || l.GetComponentInParent<FuseBox>() || l.GetComponentInParent<Camera>();
                lamps[l] = !mine;
            }
        }
        float scale = HouseLayout.Instance ? HouseLayout.Instance.scale : 1f;
        Vector3 eye = cam.transform.position;
        inReach.Clear();
        List<Light> gone = null;
        foreach (var kv in lamps)
        {
            var l = kv.Key;
            if (!l) { (gone ??= new List<Light>()).Add(l); continue; }
            if (!kv.Value) continue;
            float reach = l.range + 1.5f * scale;
            float d2 = (l.transform.position - eye).sqrMagnitude;
            if (l.enabled && l.intensity > 0.05f && d2 < reach * reach && Mathf.Abs(l.transform.position.y - eye.y) < 3.2f * scale)
                inReach.Add((l, d2));
            else if (l.shadows != LightShadows.None) l.shadows = LightShadows.None;
        }
        if (gone != null) foreach (var l in gone) lamps.Remove(l);
        inReach.Sort((a, b) => a.d.CompareTo(b.d));
        for (int i = 0; i < inReach.Count; i++)
        {
            var l = inReach[i].light;
            bool on = i < ShadowLamps;
            if (on && l.shadows == LightShadows.None)
            {
                l.shadows = LightShadows.Soft;
                l.shadowStrength = 0.7f;
                // (the lamp's own shade / fitting is right round the bulb: it mustn't black out the room)
                l.shadowNearPlane = Mathf.Max(l.shadowNearPlane, 0.35f * scale);
                l.shadowBias = 0.04f;
                l.shadowNormalBias = 0.35f;
            }
            else if (!on && l.shadows != LightShadows.None) l.shadows = LightShadows.None;
        }
    }

    // ------------------------------------------------------------------ the look

    const string LookName = "G7l6 look";

    /// <summary>Turns the look layer on / off (it's on in the game; off is for comparing).</summary>
    public static void SetLook(bool on)
    {
        LookOn = on;
        var scene = SceneManager.GetActiveScene();
        GameObject existing = null;
        foreach (var v in Object.FindObjectsOfType<PostProcessVolume>(true)) if (v.name == LookName) existing = v.gameObject;
        if (!on) { if (existing) Object.Destroy(existing); return; }
        if (existing) return;

        // on a layer the cameras' post-processing listens to
        int layer = 0;
        foreach (var ppl in Object.FindObjectsOfType<PostProcessLayer>())
        {
            int mask = ppl.volumeLayer.value;
            for (int i = 0; i < 32; i++) if ((mask & (1 << i)) != 0) { layer = i; break; }
            if (mask != 0) break;
        }
        var go = new GameObject(LookName) { layer = layer };
        var volume = go.AddComponent<PostProcessVolume>();
        volume.isGlobal = true;
        volume.priority = 50f;
        volume.weight = 1f;
        var profile = ScriptableObject.CreateInstance<PostProcessProfile>();
        profile.name = LookName;

        var ao = profile.AddSettings<AmbientOcclusion>();
        ao.enabled.Override(true);
        ao.mode.Override(AmbientOcclusionMode.MultiScaleVolumetricObscurance);
        ao.intensity.Override(0.55f);
        ao.thicknessModifier.Override(1.3f);
        ao.color.Override(Color.black);
        ao.ambientOnly.Override(false);

        var grade = profile.AddSettings<ColorGrading>();
        grade.enabled.Override(true);
        grade.gradingMode.Override(GradingMode.HighDefinitionRange);
        grade.tonemapper.Override(Tonemapper.ACES);
        grade.postExposure.Override(0.8f);    // (ACES darkens: this keeps rooms as bright as before)
        grade.contrast.Override(7f);
        grade.saturation.Override(16f);
        grade.temperature.Override(5f);       // a touch warmer: a lived-in house

        var bloom = profile.AddSettings<Bloom>();
        bloom.enabled.Override(true);
        bloom.intensity.Override(0.9f);
        bloom.threshold.Override(1.05f);
        bloom.softKnee.Override(0.55f);
        bloom.diffusion.Override(7f);

        var vignette = profile.AddSettings<Vignette>();
        vignette.enabled.Override(true);
        vignette.intensity.Override(0.18f);
        vignette.smoothness.Override(0.45f);
        vignette.rounded.Override(false);

        volume.sharedProfile = profile;
        if (scene.IsValid()) SceneManager.MoveGameObjectToScene(go, scene);
    }
}
