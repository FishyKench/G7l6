using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The guns of the four classes, built from simple shapes, slowly turning on a little stage far below the arena,
/// each filmed by its own camera into a picture for the class cards (see VRHud). Only runs while the cards show.
/// </summary>
public class VRWeaponShowcase : MonoBehaviour
{
    static VRWeaponShowcase instance;
    readonly Dictionary<VRRole, RenderTexture> pictures = new Dictionary<VRRole, RenderTexture>();
    readonly List<(Transform gun, Camera cam)> stage = new List<(Transform, Camera)>();
    bool showing;

    /// <summary>The live picture of this class's gun.</summary>
    public static Texture Picture(VRRole role)
    {
        Ensure();
        return instance.pictures.TryGetValue(role, out var rt) ? rt : null;
    }

    /// <summary>Turn the cameras on only while someone's looking at the cards.</summary>
    public static void Show(bool on)
    {
        if (!on && !instance) return;
        Ensure();
        if (instance.showing == on) return;
        instance.showing = on;
        foreach (var s in instance.stage) s.cam.enabled = on;
    }

    static void Ensure()
    {
        if (instance) return;
        var parent = VRArena.Instance ? VRArena.Instance.transform : null;
        var go = new GameObject("Weapon showcase");
        if (parent) go.transform.SetParent(parent, false);
        go.transform.localPosition = new Vector3(0f, -600f, 0f);
        instance = go.AddComponent<VRWeaponShowcase>();
        instance.Build();
    }

    void Build()
    {
        var lamp = new GameObject("Showcase light").AddComponent<Light>();
        lamp.transform.SetParent(transform, false);
        lamp.transform.localPosition = new Vector3(15f, 8f, -6f);
        lamp.type = LightType.Point;
        lamp.range = 60f;
        lamp.intensity = 1.3f;
        lamp.color = new Color(0.9f, 0.95f, 1f);

        for (int i = 0; i < VRLoadout.All.Length; i++)
        {
            var role = VRLoadout.All[i];
            var spot = new GameObject(role + " stand").transform;
            spot.SetParent(transform, false);
            spot.localPosition = new Vector3(i * 10f, 0f, 0f);
            var gun = BuildGun(role, spot);

            var rt = new RenderTexture(512, 300, 16, RenderTextureFormat.ARGB32) { name = role + " gun", antiAliasing = 4 };
            rt.Create();
            pictures[role] = rt;
            var cam = new GameObject("Camera").AddComponent<Camera>();
            cam.transform.SetParent(spot, false);
            cam.transform.localPosition = new Vector3(0.35f, 0.32f, -1.6f);
            cam.transform.LookAt(spot.position + new Vector3(0f, 0.02f, 0f));
            cam.fieldOfView = 34f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 5f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
            cam.allowHDR = false;
            cam.targetTexture = rt;
            cam.enabled = false;
            var fill = new GameObject("Fill light").AddComponent<Light>();
            fill.transform.SetParent(spot, false);
            fill.transform.localPosition = new Vector3(-0.8f, 0.6f, -1.2f);
            fill.type = LightType.Point;
            fill.range = 4f;
            fill.intensity = 1.6f;
            stage.Add((gun, cam));
        }
    }

    void Update()
    {
        if (!showing) return;
        for (int i = 0; i < stage.Count; i++)
            stage[i].gun.localRotation = Quaternion.Euler(0f, 90f + Mathf.Sin(Time.unscaledTime * 0.7f + i) * 28f, 0f);
    }

    /// <summary>A class's gun, from simple shapes, pointing along +z, about life-size.</summary>
    public static Transform BuildGun(VRRole role, Transform parent)
    {
        var root = new GameObject(role + " gun").transform;
        root.SetParent(parent, false);
        var dark = VRArena.Solid(new Color(0.11f, 0.11f, 0.14f), 0.7f);
        var mid = VRArena.Solid(new Color(0.24f, 0.25f, 0.3f), 0.6f);
        var metal = VRArena.Solid(new Color(0.45f, 0.47f, 0.52f), 0.8f);
        Color c = role switch
        {
            VRRole.SMG => new Color(1f, 0.78f, 0.25f),
            VRRole.Sniper => new Color(1f, 0.3f, 0.35f),
            VRRole.LMG => new Color(0.66f, 0.47f, 1f),
            _ => new Color(0.3f, 1f, 0.9f),
        };
        var glow = VRArena.Glow(c * 0.5f, c, 2.2f);

        void P(Vector3 pos, Vector3 size, Material m, float tilt = 0f)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(g.GetComponent<Collider>());
            g.transform.SetParent(root, false);
            g.transform.localPosition = pos;
            g.transform.localRotation = Quaternion.Euler(tilt, 0f, 0f);
            g.transform.localScale = size;
            g.GetComponent<Renderer>().sharedMaterial = m;
        }
        void C(Vector3 pos, float radius, float length, Material m)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Destroy(g.GetComponent<Collider>());
            g.transform.SetParent(root, false);
            g.transform.localPosition = pos;
            g.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            g.transform.localScale = new Vector3(radius * 2f, length * 0.5f, radius * 2f);
            g.GetComponent<Renderer>().sharedMaterial = m;
        }

        switch (role)
        {
            case VRRole.SMG:
                P(new Vector3(0f, 0f, 0f), new Vector3(0.07f, 0.1f, 0.36f), dark);
                C(new Vector3(0f, 0.01f, 0.27f), 0.018f, 0.18f, metal);
                P(new Vector3(0f, -0.15f, 0.06f), new Vector3(0.045f, 0.22f, 0.06f), mid, 6f);      // long mag
                P(new Vector3(0f, -0.1f, -0.1f), new Vector3(0.045f, 0.13f, 0.06f), mid, -15f);     // grip
                P(new Vector3(0f, 0.02f, -0.3f), new Vector3(0.025f, 0.025f, 0.24f), metal);        // folding stock
                P(new Vector3(0f, -0.04f, -0.42f), new Vector3(0.03f, 0.1f, 0.025f), metal);
                P(new Vector3(0.036f, 0.01f, 0.02f), new Vector3(0.004f, 0.02f, 0.26f), glow);
                P(new Vector3(-0.036f, 0.01f, 0.02f), new Vector3(0.004f, 0.02f, 0.26f), glow);
                P(new Vector3(0f, 0.065f, 0f), new Vector3(0.04f, 0.03f, 0.08f), dark);            // sight
                break;
            case VRRole.Sniper:
                P(new Vector3(0f, 0f, 0f), new Vector3(0.07f, 0.09f, 0.6f), dark);
                C(new Vector3(0f, 0.01f, 0.58f), 0.016f, 0.6f, metal);                                // long barrel
                C(new Vector3(0f, 0.11f, 0.04f), 0.035f, 0.38f, mid);                                 // scope
                C(new Vector3(0f, 0.11f, 0.24f), 0.045f, 0.06f, mid);
                C(new Vector3(0f, 0.11f, -0.16f), 0.04f, 0.05f, mid);
                P(new Vector3(0f, 0.06f, 0.04f), new Vector3(0.03f, 0.04f, 0.04f), dark);
                P(new Vector3(0f, -0.1f, -0.14f), new Vector3(0.045f, 0.13f, 0.06f), mid, -15f);     // grip
                P(new Vector3(0f, -0.06f, 0.06f), new Vector3(0.045f, 0.08f, 0.1f), mid);           // mag
                P(new Vector3(0f, -0.02f, -0.45f), new Vector3(0.065f, 0.13f, 0.3f), dark);          // stock
                P(new Vector3(0.05f, 0.03f, -0.02f), new Vector3(0.05f, 0.012f, 0.012f), metal);     // bolt
                P(new Vector3(0.036f, 0.0f, 0.1f), new Vector3(0.004f, 0.02f, 0.4f), glow);
                P(new Vector3(-0.036f, 0.0f, 0.1f), new Vector3(0.004f, 0.02f, 0.4f), glow);
                break;
            case VRRole.LMG:
                P(new Vector3(0f, 0f, 0f), new Vector3(0.1f, 0.12f, 0.62f), dark);
                C(new Vector3(0f, 0.02f, 0.5f), 0.025f, 0.42f, metal);
                C(new Vector3(0f, -0.13f, 0.02f), 0.1f, 0.09f, mid);                                  // drum mag
                P(new Vector3(0f, 0.11f, 0.02f), new Vector3(0.025f, 0.05f, 0.2f), metal);           // carry handle
                P(new Vector3(0f, -0.11f, -0.2f), new Vector3(0.05f, 0.13f, 0.06f), mid, -15f);      // grip
                P(new Vector3(0f, -0.01f, -0.45f), new Vector3(0.08f, 0.13f, 0.3f), dark);           // stock
                P(new Vector3(0.03f, -0.12f, 0.6f), new Vector3(0.012f, 0.22f, 0.012f), metal, 20f); // bipod
                P(new Vector3(-0.03f, -0.12f, 0.6f), new Vector3(0.012f, 0.22f, 0.012f), metal, 20f);
                P(new Vector3(0.051f, 0.02f, 0.02f), new Vector3(0.004f, 0.025f, 0.5f), glow);
                P(new Vector3(-0.051f, 0.02f, 0.02f), new Vector3(0.004f, 0.025f, 0.5f), glow);
                break;
            default: // Assault rifle
                P(new Vector3(0f, 0f, 0f), new Vector3(0.07f, 0.09f, 0.55f), dark);
                C(new Vector3(0f, 0f, 0.42f), 0.02f, 0.3f, metal);
                P(new Vector3(0f, -0.12f, 0.1f), new Vector3(0.045f, 0.16f, 0.08f), mid, 12f);      // curved mag
                P(new Vector3(0f, -0.1f, -0.1f), new Vector3(0.045f, 0.13f, 0.06f), mid, -15f);     // grip
                P(new Vector3(0f, -0.005f, -0.38f), new Vector3(0.06f, 0.11f, 0.2f), mid);          // stock
                P(new Vector3(0f, 0.065f, 0.0f), new Vector3(0.045f, 0.035f, 0.1f), dark);          // red dot
                P(new Vector3(0f, 0.09f, -0.02f), new Vector3(0.045f, 0.03f, 0.008f), dark);
                P(new Vector3(0.036f, 0.01f, 0.05f), new Vector3(0.004f, 0.02f, 0.36f), glow);
                P(new Vector3(-0.036f, 0.01f, 0.05f), new Vector3(0.004f, 0.02f, 0.36f), glow);
                break;
        }
        return root;
    }
}
