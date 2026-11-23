using System.Collections;
using UnityEngine;

/// <summary>
/// A small spill that can happen anywhere during play (a drink the cat knocked off a table): a little puddle you mop up the same
/// way as a flood - it spreads a bit, but not far. Made at runtime with <see cref="Create"/>.
/// </summary>
public class Spill : FloodHazard
{
    /// <summary>Puts a spill on the floor at 'at' (it starts itself a frame later). 'diameter' = how far it spreads, in metres.</summary>
    public static Spill Create(Vector3 at, float diameter, string what = "spill")
    {
        float s = HouseLayout.Instance ? HouseLayout.Instance.scale : 1f;
        var go = new GameObject("Spill (" + what + ")");
        go.transform.position = at;

        var visual = new GameObject("Visual");
        visual.transform.SetParent(go.transform, false);
        var disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        disc.name = "Puddle";
        Destroy(disc.GetComponent<Collider>());
        disc.transform.SetParent(visual.transform, false);
        disc.transform.localPosition = Vector3.up * 0.004f;
        disc.transform.localScale = new Vector3(0.2f, 0.002f, 0.2f);
        disc.GetComponent<Renderer>().sharedMaterial = WaterMaterial();
        disc.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        var hit = disc.AddComponent<BoxCollider>(); // (so you can look at it for the prompt)
        hit.isTrigger = true;
        hit.size = new Vector3(1f, 30f, 1f);

        var spill = go.AddComponent<Spill>();
        spill.puddle = disc.transform;
        spill.visualRoot = visual;
        spill.taskName = "Mop up the " + what;
        spill.requiredItem = ItemType.Mop;
        spill.requiredItemName = "mop";
        spill.actionVerb = "Mop up";
        spill.stressPerSecond = 0.6f;
        spill.stressReliefOnComplete = 2f;
        spill.cooldown = 0f;
        spill.startDiameter = 0.25f * s;
        spill.maxDiameter = diameter;
        spill.spreadTime = 18f;
        spill.StartCoroutine(spill.Begin());
        return spill;
    }

    IEnumerator Begin()
    {
        yield return null; // (after Start, which hides the visuals until it's active)
        if (GameManager.Instance) GameManager.Instance.StartTask(this);
    }

    protected override string ClearedMessage => "Spill mopped up.";

    static Material water;

    /// <summary>The same water as the house's floods (falls back to a see-through blue).</summary>
    static Material WaterMaterial()
    {
        if (water) return water;
        foreach (var f in FindObjectsOfType<FloodHazard>(true))
        {
            if (f is Spill || !f.puddle) continue;
            var r = f.puddle.GetComponentInChildren<Renderer>(true);
            if (r && r.sharedMaterial && r.sharedMaterial.HasProperty("_MainTex")) return water = r.sharedMaterial;
        }
        return water = new Material(Shader.Find("Sprites/Default")) { color = new Color(0.3f, 0.55f, 0.85f, 0.55f) };
    }
}
