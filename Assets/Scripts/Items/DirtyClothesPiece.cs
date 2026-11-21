using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One piece of dirty laundry lying around (a crumpled shirt, socks, trousers, a hoodie). Press E to pick it up:
/// it goes onto the armful of dirty clothes you're carrying. Made by the LaundryTask when the laundry starts.
/// </summary>
public class DirtyClothesPiece : MonoBehaviour, IInteractable, IPromptProvider
{
    public static readonly List<DirtyClothesPiece> All = new List<DirtyClothesPiece>();

    public LaundryTask laundry;
    public string kind = "shirt";

    void OnEnable() => All.Add(this);
    void OnDisable() => All.Remove(this);

    public string Prompt => "Pick up the dirty " + kind;

    public void Interact()
    {
        if (laundry) laundry.PickUpPiece(this);
    }

    static readonly string[] Kinds = { "shirt", "socks", "trousers", "hoodie", "t-shirt" };
    static readonly Color[] Colours =
    {
        new Color(0.85f, 0.85f, 0.88f), new Color(0.25f, 0.35f, 0.6f), new Color(0.75f, 0.25f, 0.3f), new Color(0.3f, 0.55f, 0.4f),
        new Color(0.2f, 0.2f, 0.24f), new Color(0.9f, 0.7f, 0.3f), new Color(0.55f, 0.4f, 0.75f),
    };

    /// <summary>A crumpled piece of clothing lying at 'at' (made of a few squashed shapes).</summary>
    public static DirtyClothesPiece Create(Vector3 at, float scale, LaundryTask laundry, int index)
    {
        var go = new GameObject("Dirty clothes (piece)");
        go.transform.position = at;
        go.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
        go.transform.localScale = Vector3.one * scale;
        var piece = go.AddComponent<DirtyClothesPiece>();
        piece.laundry = laundry;
        piece.kind = Kinds[index % Kinds.Length];
        var colour = Colours[Random.Range(0, Colours.Length)];
        var cloth = Mat(colour);
        var dark = Mat(colour * 0.75f);

        switch (piece.kind)
        {
            case "socks":
                Part(go.transform, PrimitiveType.Capsule, new Vector3(-0.06f, 0.035f, 0f), new Vector3(0.07f, 0.12f, 0.07f), Quaternion.Euler(90f, 20f, 0f), cloth);
                Part(go.transform, PrimitiveType.Capsule, new Vector3(0.06f, 0.035f, 0.03f), new Vector3(0.07f, 0.12f, 0.07f), Quaternion.Euler(90f, -35f, 0f), dark);
                break;
            case "trousers":
                Part(go.transform, PrimitiveType.Cube, new Vector3(0f, 0.025f, 0.12f), new Vector3(0.34f, 0.05f, 0.16f), Quaternion.Euler(0f, 5f, 2f), cloth);
                Part(go.transform, PrimitiveType.Cube, new Vector3(-0.09f, 0.03f, -0.14f), new Vector3(0.13f, 0.05f, 0.42f), Quaternion.Euler(3f, 12f, 0f), cloth);
                Part(go.transform, PrimitiveType.Cube, new Vector3(0.1f, 0.04f, -0.12f), new Vector3(0.13f, 0.05f, 0.4f), Quaternion.Euler(-4f, -25f, 6f), dark);
                break;
            default: // shirts and hoodies: a crumpled body with sleeves
                Part(go.transform, PrimitiveType.Sphere, new Vector3(0f, 0.05f, 0f), new Vector3(0.42f, 0.1f, 0.36f), Quaternion.Euler(0f, 0f, 4f), cloth);
                Part(go.transform, PrimitiveType.Sphere, new Vector3(0.08f, 0.09f, 0.05f), new Vector3(0.22f, 0.09f, 0.2f), Quaternion.Euler(10f, 30f, 0f), dark);
                Part(go.transform, PrimitiveType.Capsule, new Vector3(-0.22f, 0.04f, -0.08f), new Vector3(0.08f, 0.16f, 0.08f), Quaternion.Euler(90f, 60f, 0f), cloth);
                Part(go.transform, PrimitiveType.Capsule, new Vector3(0.21f, 0.04f, 0.1f), new Vector3(0.08f, 0.15f, 0.08f), Quaternion.Euler(90f, -50f, 0f), cloth);
                if (piece.kind == "hoodie") Part(go.transform, PrimitiveType.Sphere, new Vector3(0f, 0.07f, -0.17f), new Vector3(0.18f, 0.08f, 0.14f), Quaternion.identity, dark);
                break;
        }

        // Easy to point at (a trigger, so you don't trip over it).
        var hit = go.AddComponent<SphereCollider>();
        hit.isTrigger = true;
        hit.center = new Vector3(0f, 0.08f, 0f);
        hit.radius = 0.28f;
        var outline = go.AddComponent<Outline>();
        outline.OutlineMode = Outline.Mode.OutlineVisible;
        outline.OutlineColor = Color.white;
        outline.OutlineWidth = 5f;
        outline.enabled = false;
        return piece;
    }

    static void Part(Transform parent, PrimitiveType shape, Vector3 pos, Vector3 size, Quaternion rot, Material m)
    {
        var p = GameObject.CreatePrimitive(shape);
        Destroy(p.GetComponent<Collider>());
        p.transform.SetParent(parent, false);
        p.transform.localPosition = pos;
        p.transform.localRotation = rot;
        p.transform.localScale = size;
        p.GetComponent<Renderer>().sharedMaterial = m;
    }

    static Material Mat(Color c)
    {
        var m = new Material(Shader.Find("Standard")) { color = c };
        m.SetFloat("_Glossiness", 0.05f);
        return m;
    }
}
