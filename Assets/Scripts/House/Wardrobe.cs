using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The wardrobe in your room. Press E to open or close its doors (they swing on their hinges).
/// When the laundry is done, carry the clean clothes here and press E: they go in as folded stacks, and the
/// laundry is finished.
/// </summary>
public class Wardrobe : MonoBehaviour, IInteractable, IPromptProvider
{
    public float openAngle = 105f;
    public float speed = 5f;

    class Door
    {
        public Transform hinge;
        public float sign;
    }

    readonly List<Door> doors = new List<Door>();
    LaundryTask laundry;
    bool open;
    float amount, velocity;
    int stacks;

    public bool IsOpen => open;

    void Start()
    {
        laundry = FindObjectOfType<LaundryTask>();
        SetUpDoors();
    }

    /// <summary>Each door turns round its outer edge: put it under a hinge there.</summary>
    void SetUpDoors()
    {
        var all = GetComponentsInChildren<Transform>(true);
        var whole = Bounds(gameObject);
        Vector3 centre = transform.InverseTransformPoint(whole.center);
        foreach (var t in all)
        {
            if (t == transform || !t.name.Contains("Door") || t.parent && t.parent.name.StartsWith("Hinge")) continue;
            var b = Bounds(t.gameObject);
            Vector3 local = transform.InverseTransformPoint(b.center);
            float side = local.x < centre.x ? -1f : 1f;
            // The outer edge, at the front of the door.
            Vector3 extentsLocal = transform.InverseTransformVector(b.extents);
            float halfWidth = Mathf.Abs(extentsLocal.x);
            float halfDepth = Mathf.Abs(extentsLocal.z);
            var hinge = new GameObject("Hinge " + t.name).transform;
            hinge.SetParent(transform, false);
            hinge.localPosition = new Vector3(local.x + side * halfWidth, local.y, local.z + halfDepth);
            hinge.localRotation = Quaternion.identity;
            t.SetParent(hinge, true);
            // Swing outwards (towards the front, +Z): left door turns one way, right door the other.
            doors.Add(new Door { hinge = hinge, sign = side > 0f ? 1f : -1f });
        }
    }

    public string Prompt
    {
        get
        {
            bool holding = PlayerCarry.Instance && PlayerCarry.Instance.IsHolding(ItemType.CleanClothes);
            if (holding) return open ? "Put the clean clothes away" : "Open wardrobe";
            return open ? "Close wardrobe" : "Open wardrobe";
        }
    }

    public void Interact()
    {
        bool holding = PlayerCarry.Instance && PlayerCarry.Instance.IsHolding(ItemType.CleanClothes);
        if (holding && open)
        {
            var clothes = PlayerCarry.Instance.TakeHeld();
            if (clothes) clothes.ReturnHome(false);
            AddStack();
            if (laundry) laundry.ClothesPutAway();
            return;
        }
        open = !open;
    }

    void Update()
    {
        if (PauseMenu.IsPaused || doors.Count == 0) return;
        amount = Mathf.SmoothDamp(amount, open ? 1f : 0f, ref velocity, 1f / speed);
        foreach (var d in doors) d.hinge.localRotation = Quaternion.Euler(0f, d.sign * openAngle * amount, 0f);
    }

    /// <summary>A neat pile of folded clothes on the wardrobe's floor (one more each wash).</summary>
    void AddStack()
    {
        var b = Bounds(gameObject);
        Vector3 inside = transform.InverseTransformPoint(new Vector3(b.center.x, b.min.y, b.center.z));
        Vector3 size = transform.InverseTransformVector(b.size);
        float slot = (stacks % 3 - 1) * Mathf.Abs(size.x) * 0.28f;
        float height = Mathf.Abs(size.y) * (0.12f + 0.22f * (stacks / 3 % 3));
        var pile = new GameObject("Folded clothes").transform;
        pile.SetParent(transform, false);
        pile.localPosition = inside + new Vector3(slot, height, 0f);
        Color[] colours = { new Color(0.85f, 0.85f, 0.9f), new Color(0.3f, 0.45f, 0.75f), new Color(0.85f, 0.35f, 0.4f), new Color(0.35f, 0.6f, 0.45f), new Color(0.2f, 0.2f, 0.25f) };
        float w = Mathf.Abs(size.x) * 0.24f, d = Mathf.Abs(size.z) * 0.6f;
        for (int i = 0; i < 4; i++)
        {
            var piece = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(piece.GetComponent<Collider>());
            piece.transform.SetParent(pile, false);
            piece.transform.localScale = new Vector3(w * Random.Range(0.92f, 1f), 0.06f / Mathf.Max(0.01f, transform.lossyScale.y), d);
            piece.transform.localPosition = new Vector3(Random.Range(-0.01f, 0.01f), (i + 0.5f) * piece.transform.localScale.y, 0f);
            piece.transform.localRotation = Quaternion.Euler(0f, Random.Range(-4f, 4f), 0f);
            var m = new Material(Shader.Find("Standard")) { color = colours[Random.Range(0, colours.Length)] };
            m.SetFloat("_Glossiness", 0.05f);
            piece.GetComponent<Renderer>().sharedMaterial = m;
        }
        stacks++;
    }

    static Bounds Bounds(GameObject go)
    {
        var rs = go.GetComponentsInChildren<MeshRenderer>();
        if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.one);
        var b = rs[0].bounds;
        foreach (var r in rs) b.Encapsulate(r.bounds);
        return b;
    }
}
