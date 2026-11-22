using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Something from the kitchen you carry around while cooking: an ingredient (raw, frozen, defrosted, chopped), the salt or oil, or
/// the pot / frying pan / bowl. Made by <see cref="Cooking"/>; it's a Carryable (G drops it, E picks it up again).
/// Looks: the Food Kit model where there is one (carrot, onion, tomato, meat...), otherwise a simple made-in-code one (potato,
/// cucumber, spaghetti). Frozen things are frosted blue-white; chopped things become a little bowl of pieces.
/// </summary>
public class Food : MonoBehaviour
{
    public Cooking.Ingredient ing;
    public string seasoning;     // "salt" / "oil" (not an ingredient)
    public bool isVessel;
    public Cooking.Vessel vessel;
    public bool defrosted, cut;
    public float quality = 1f;

    Transform look;
    GameObject water;

    /// <summary>The salad bowl's size, on the cookware counter and in your hands (x K).</summary>
    public const float CounterBowlSize = 0.24f;

    public bool NeedsCutting => ing != null && ing.cuts > 0 && !cut && (!ing.frozen || defrosted);
    public bool Ready => ing != null && (!ing.frozen || defrosted) && (ing.cuts == 0 || cut);

    static float K => Cooking.K;

    // ------------------------------------------------------------------ making them

    public static Food Make(Cooking.Ingredient ing, Cooking cooking)
    {
        var f = New(ing.name);
        f.ing = ing;
        f.Refresh();
        return f;
    }

    public static Food MakeSeasoning(string what, Cooking cooking)
    {
        var f = New(what);
        f.seasoning = what;
        f.SetLook(KitModel(what == "salt" ? "shaker-salt" : "bottle-oil", what == "salt" ? 0.1f : 0.24f), what == "salt" ? "salt" : "oil");
        return f;
    }

    public static Food MakeVessel(Cooking.Vessel v, Cooking cooking)
    {
        var f = New(v == Cooking.Vessel.Pot ? "pot" : v == Cooking.Vessel.Pan ? "frying pan" : "big bowl");
        f.isVessel = true;
        f.vessel = v;
        string model = v == Cooking.Vessel.Pot ? "pot" : v == Cooking.Vessel.Pan ? "frying-pan" : "bowl";
        float size = v == Cooking.Vessel.Pot ? 0.3f : v == Cooking.Vessel.Pan ? 0.4f : CounterBowlSize;
        var look = KitModel(model, size) ?? Primitive(PrimitiveType.Cylinder, new Color(0.6f, 0.62f, 0.66f), new Vector3(size, size * 0.2f, size));
        f.SetLook(look, f.name);
        return f;
    }

    static Food New(string name)
    {
        var go = new GameObject(name);
        var cam = Camera.main;
        go.transform.position = cam ? cam.transform.position + cam.transform.forward * 0.6f : Vector3.zero;
        var f = go.AddComponent<Food>();
        return f;
    }

    void SetLook(GameObject model, string display)
    {
        if (look) Destroy(look.gameObject);
        look = model.transform;
        look.SetParent(transform, false);
        look.localPosition = Vector3.zero;
        foreach (var c in model.GetComponentsInChildren<Collider>()) Destroy(c);
        // One box to point at and to carry.
        var old = GetComponent<BoxCollider>();
        if (old) Destroy(old);
        var b = Cooking.Bounds(gameObject);
        var box = gameObject.AddComponent<BoxCollider>();
        box.center = transform.InverseTransformPoint(b.center);
        box.size = new Vector3(Mathf.Max(0.05f, b.size.x), Mathf.Max(0.05f, b.size.y), Mathf.Max(0.05f, b.size.z));
        var carry = GetComponent<Carryable>();
        if (!carry) carry = gameObject.AddComponent<Carryable>(); // (not '??': Unity's missing components aren't real nulls)
        carry.type = ItemType.Food;
        carry.hasHome = false; // made in the kitchen: its "home" would be wherever it appeared (in mid-air)
        carry.displayName = display;
        carry.maxHeldSize = 0.32f;
    }

    /// <summary>Updates the look and the name for its state (frozen / defrosted / chopped).</summary>
    public void Refresh()
    {
        if (ing == null) return;
        string state = ing.frozen && !defrosted ? "frozen " : cut ? "chopped " : "";
        name = state + ing.name;
        if (cut) { SetLook(ChoppedBowl(), name); return; }
        var model = WholeModel();
        if (ing.frozen && !defrosted)
            foreach (var r in model.GetComponentsInChildren<Renderer>())
            {
                var mats = r.materials;
                foreach (var m in mats) m.color = Color.Lerp(m.color, new Color(0.82f, 0.9f, 1f), 0.55f); // frosted
                r.materials = mats;
            }
        SetLook(model, name);
    }

    GameObject WholeModel() => WholeModelFor(ing);

    /// <summary>The whole (uncut) look of an ingredient: its Food Kit model, or a made-in-code one.</summary>
    public static GameObject WholeModelFor(Cooking.Ingredient ing)
    {
        var m = ing.model != null ? KitModel(ing.model, ing.size) : null;
        if (m) return m;
        switch (ing.id)
        {
            case "potato":
            {
                var p = Primitive(PrimitiveType.Sphere, ing.colour, new Vector3(ing.size, ing.size * 0.72f, ing.size * 0.82f));
                return p;
            }
            case "cucumber":
            {
                var c = Primitive(PrimitiveType.Capsule, ing.colour, new Vector3(ing.size * 0.22f, ing.size * 0.5f, ing.size * 0.22f));
                c.transform.GetChild(0).localRotation = Quaternion.Euler(0f, 0f, 90f); // lying down
                c.transform.GetChild(0).localPosition = new Vector3(0f, ing.size * 0.11f * K, 0f);
                return c;
            }
            case "bacon": // a few rashers: pink meat with a white fat edge
            {
                var b = new GameObject("bacon");
                for (int i = 0; i < 3; i++)
                {
                    var meat = Primitive(PrimitiveType.Cube, ing.colour, new Vector3(ing.size, 0.006f, ing.size * 0.22f));
                    meat.transform.SetParent(b.transform, false);
                    meat.transform.localPosition = new Vector3(0f, i * 0.006f * K, (i - 1) * ing.size * 0.16f * K);
                    var fat = Primitive(PrimitiveType.Cube, new Color(0.97f, 0.9f, 0.85f), new Vector3(ing.size, 0.0065f, ing.size * 0.05f));
                    fat.transform.SetParent(meat.transform, false);
                    fat.transform.localPosition = new Vector3(0f, 0f, ing.size * 0.11f * K);
                }
                return b;
            }
            case "pasta":
            {
                var box = Primitive(PrimitiveType.Cube, new Color(0.2f, 0.45f, 0.8f), new Vector3(ing.size, ing.size * 0.18f, ing.size * 0.3f));
                var band = Primitive(PrimitiveType.Cube, ing.colour, new Vector3(ing.size * 0.5f, ing.size * 0.19f, ing.size * 0.31f));
                band.transform.SetParent(box.transform, false);
                return box;
            }
        }
        return Primitive(PrimitiveType.Sphere, ing.colour, Vector3.one * ing.size);
    }

    /// <summary>A little bowl with the chopped pieces in it.</summary>
    GameObject ChoppedBowl()
    {
        var root = new GameObject("Chopped " + ing.name);
        var bowl = KitModel("bowl", 0.16f) ?? Primitive(PrimitiveType.Cylinder, Color.white, new Vector3(0.16f, 0.03f, 0.16f));
        bowl.transform.SetParent(root.transform, false);
        AddPieces(root.transform, 0.045f * K, 0.05f * K, Mathf.Max(3, ing.cuts + 1));
        return root;
    }

    void AddPieces(Transform parent, float y, float spread, int count)
    {
        var mat = new Material(Shader.Find("Standard")) { color = ing.colour };
        for (int i = 0; i < count; i++)
        {
            var p = GameObject.CreatePrimitive(ing.shape == "slab" ? PrimitiveType.Cube : PrimitiveType.Cylinder);
            Destroy(p.GetComponent<Collider>());
            p.transform.SetParent(parent, false);
            float a = i * 2.39f;
            p.transform.localPosition = new Vector3(Mathf.Cos(a) * spread * 0.6f, y + (i % 2) * 0.008f * K, Mathf.Sin(a) * spread * 0.6f);
            p.transform.localRotation = Quaternion.Euler(Random.Range(-20f, 20f), Random.Range(0f, 180f), Random.Range(-20f, 20f));
            p.transform.localScale = ing.shape == "slab" ? new Vector3(0.03f, 0.022f, 0.03f) * K : new Vector3(0.035f, 0.008f, 0.035f) * K;
            p.GetComponent<Renderer>().sharedMaterial = mat;
        }
    }

    /// <summary>In the pot / pan: just the pieces, no little bowl.</summary>
    public void ShowAsPieces()
    {
        if (ing == null) return;
        if (look) Destroy(look.gameObject);
        var root = new GameObject("Pieces").transform;
        root.SetParent(transform, false);
        look = root;
        if (ing.cuts > 0) AddPieces(root, 0f, 0.07f * K, ing.cuts + 1);
        else
        {
            var whole = WholeModel();
            whole.transform.SetParent(root, false);
        }
    }

    /// <summary>The middle of its body (a frying pan's handle sticks out to one side): the model's own origin.</summary>
    public Vector3 BodyCentre => look && look.childCount > 0 ? look.GetChild(0).position : transform.position;

    /// <summary>The radius of its body in the world (a pot / pan / bowl: half its narrower level size, so not the handle).</summary>
    public float BodyRadius
    {
        get
        {
            var mf = look ? look.GetComponentInChildren<MeshFilter>() : null;
            if (!mf || !mf.sharedMesh) return 0f;
            var sz = mf.sharedMesh.bounds.size;
            return Mathf.Min(sz.x, sz.z) * 0.5f * mf.transform.lossyScale.x;
        }
    }

    /// <summary>The model's longer level axis in the world (through a pot's two handles, along a pan's handle).</summary>
    public Vector3 LongAxis
    {
        get
        {
            var mf = look ? look.GetComponentInChildren<MeshFilter>() : null;
            if (!mf || !mf.sharedMesh) return transform.right;
            var s = mf.sharedMesh.bounds.size;
            return s.z > s.x ? mf.transform.forward : mf.transform.right;
        }
    }

    /// <summary>
    /// The water in a pot: a disc inside its body, in the model's own space (so it's the right size whichever way you hold it).
    /// level 0..1 = how full.
    /// </summary>
    public void ShowWater(bool on, float level = 0.7f)
    {
        if (!isVessel) return;
        var mf = look ? look.GetComponentInChildren<MeshFilter>() : null;
        if (!water)
        {
            water = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Destroy(water.GetComponent<Collider>());
            water.name = "Water";
            var m = new Material(Shader.Find("Standard")) { color = new Color(0.42f, 0.68f, 0.92f) };
            m.SetFloat("_Glossiness", 0.92f);
            water.GetComponent<Renderer>().sharedMaterial = m;
            if (mf && mf.sharedMesh)
            {
                var mb = mf.sharedMesh.bounds;
                float d = Mathf.Min(mb.size.x, mb.size.z) * 0.84f; // inside the walls
                water.transform.SetParent(mf.transform, false);
                water.transform.localRotation = Quaternion.identity;
                float thick = 0.002f / Mathf.Max(1e-4f, mf.transform.lossyScale.y);
                water.transform.localScale = new Vector3(d, thick, d);
            }
            else
            {
                water.transform.SetParent(transform, false);
                water.transform.localScale = new Vector3(0.24f * K, 0.002f, 0.24f * K);
            }
        }
        SetWaterLevel(level);
        water.SetActive(on);
    }

    public void SetWaterLevel(float level)
    {
        if (!water) return;
        var mf = look ? look.GetComponentInChildren<MeshFilter>() : null;
        if (mf && mf.sharedMesh && water.transform.parent == mf.transform)
        {
            var mb = mf.sharedMesh.bounds;
            water.transform.localPosition = new Vector3(mb.center.x, mb.min.y + mb.size.y * Mathf.Clamp01(level), mb.center.z);
        }
        else water.transform.localPosition = new Vector3(0f, 0.12f * K * Mathf.Clamp01(level), 0f);
    }

    // ------------------------------------------------------------------ going into the pot / pan / bowl (Cooking plays these)

    /// <summary>
    /// Into the pot / pan / bowl, the way you'd do it: carried from your hands to the rim, tipped over the middle, and what's inside
    /// falls in (chopped pieces, spaghetti out of its packet, the mince, the egg) and stays there as part of the dish. The little bowl
    /// or the packet tips back and is put away. landY = the level things settle at (the water's surface in a pot of water).
    /// </summary>
    public IEnumerator PourInto(Transform contents, Bounds vessel, Vector3 eye, float landY, bool water)
    {
        if (ing != null && ing.id == "egg") { yield return CrackInto(contents, vessel, eye, landY); yield break; }
        float h = UprightHeight();
        Aim(vessel, eye, h, 110f, 0.035f * K, out var pivot, out var upright, out var tipped);
        yield return CarryTo(pivot, upright, 0.3f);
        yield return Tip(upright, tipped, 0.22f);

        Vector3 mouth = transform.position + transform.rotation * (Vector3.up * h);
        var falling = new List<Transform>();
        bool chopped = cut && look && look.childCount > 1;
        bool packet = !chopped && ing != null && ing.id == "pasta";
        if (chopped)
            for (int i = look.childCount - 1; i >= 1; i--) falling.Add(look.GetChild(i)); // the pieces, not the little bowl (child 0)
        else if (packet)
            falling.AddRange(Strands(mouth));
        else if (look)
        {
            falling.Add(look);
            look = null;
        }

        var group = new GameObject("Added " + (ing != null ? ing.name : name)).transform;
        group.SetParent(contents, true);
        float r = Mathf.Min(vessel.extents.x, vessel.extents.z) * 0.42f;
        var from = new List<Vector3>();
        var to = new List<Vector3>();
        var spin = new List<Vector3>();
        foreach (var t in falling)
        {
            t.SetParent(null, true);
            from.Add(t.position);
            var d = Random.insideUnitCircle * r;
            to.Add(new Vector3(vessel.center.x + d.x, landY, vessel.center.z + d.y));
            spin.Add(Random.onUnitSphere * Random.Range(200f, 500f));
        }
        const float fall = 0.3f, stagger = 0.025f;
        float total = fall + stagger * falling.Count;
        for (float e = 0f; e < total; e += Time.deltaTime)
        {
            for (int i = 0; i < falling.Count; i++)
            {
                if (!falling[i]) continue;
                float u = Mathf.Clamp01((e - i * stagger) / fall);
                var p = Vector3.Lerp(from[i], to[i], u);
                p.y = Mathf.Lerp(from[i].y, to[i].y, u * u); // speeding up as it drops
                falling[i].position = p;
                if (u < 1f) falling[i].Rotate(spin[i] * Time.deltaTime, Space.World);
            }
            yield return null;
        }
        // settled: the pieces lie flat-ish where they landed, and belong to the dish now
        for (int i = 0; i < falling.Count; i++)
        {
            if (!falling[i]) continue;
            falling[i].position = to[i];
            var eul = falling[i].eulerAngles;
            falling[i].rotation = Quaternion.Euler(Mathf.Clamp(Mathf.DeltaAngle(0f, eul.x), -25f, 25f), eul.y, Mathf.Clamp(Mathf.DeltaAngle(0f, eul.z), -25f, 25f));
            falling[i].SetParent(group, true);
        }
        if (ing != null && ing.id == "egg" && falling.Count == 1 && falling[0])
        {
            // cracked: the egg becomes an egg in the pan
            var cracked = KitModel("egg-half", 0.07f);
            if (cracked)
            {
                cracked.transform.SetParent(group, false);
                cracked.transform.position = to[0];
                Destroy(falling[0].gameObject);
            }
        }
        if (water) Fx.Splash(new Vector3(vessel.center.x, landY, vessel.center.z), 8, 0.35f);

        if (chopped || packet)
        {
            yield return Tip(tipped, upright, 0.15f);
            yield return Shrink(0.2f);
        }
        Destroy(gameObject);
    }

    /// <summary>
    /// An egg goes in the way you'd do it: held over the middle, cracked - the shell splits in two and opens - and only the egg
    /// (the white and the yolk) drops into the pan. The two halves of the shell go away; nobody cooks the shell.
    /// </summary>
    IEnumerator CrackInto(Transform contents, Bounds vessel, Vector3 eye, float landY)
    {
        float h = UprightHeight();
        Aim(vessel, eye, h, 0f, 0.07f * K, out var pivot, out var upright, out _);
        yield return CarryTo(pivot, upright, 0.3f);

        Vector3 middle = transform.position + Vector3.up * h * 0.5f;
        Vector3 toward = vessel.center - eye; toward.y = 0f;
        Vector3 side = Vector3.Cross(Vector3.up, toward.sqrMagnitude > 1e-4f ? toward.normalized : Vector3.forward);
        if (look) look.gameObject.SetActive(false);

        // the two halves of the shell, open side to open side
        var shells = new List<Transform>();
        for (int i = 0; i < 2; i++)
        {
            var half = KitModel("egg-half", ing.size * 0.95f) ?? Primitive(PrimitiveType.Sphere, new Color(0.96f, 0.92f, 0.84f), new Vector3(ing.size * 0.6f, ing.size * 0.35f, ing.size * 0.6f));
            half.name = "Egg shell";
            half.transform.position = middle;
            half.transform.rotation = Quaternion.LookRotation(toward.sqrMagnitude > 1e-4f ? toward : Vector3.forward) * Quaternion.Euler(0f, 0f, i == 0 ? 90f : -90f);
            shells.Add(half.transform);
        }
        // what's inside: the white and the yolk
        var egg = KitModel("egg-cooked", ing.size * 1.5f);
        if (!egg)
        {
            egg = new GameObject("Egg");
            var white = Primitive(PrimitiveType.Cylinder, new Color(0.97f, 0.96f, 0.92f), new Vector3(ing.size * 1.3f, 0.004f, ing.size * 1.3f));
            white.transform.SetParent(egg.transform, false);
            var yolk = Primitive(PrimitiveType.Sphere, new Color(1f, 0.78f, 0.15f), new Vector3(ing.size * 0.45f, ing.size * 0.25f, ing.size * 0.45f));
            yolk.transform.SetParent(egg.transform, false);
        }
        egg.name = "Egg (in the pan)";
        egg.transform.position = middle;
        egg.transform.localScale = Vector3.one * 0.25f;
        var group = new GameObject("Added egg").transform;
        group.SetParent(contents, true);

        const float open = 0.25f, drop = 0.3f;
        Vector3 land = new Vector3(vessel.center.x, landY + 0.003f, vessel.center.z);
        Quaternion[] start = { shells[0].rotation, shells[1].rotation };
        for (float t = 0f; t < open + drop; t += Time.deltaTime)
        {
            float u = Mathf.Clamp01(t / open);
            for (int i = 0; i < 2; i++)
            {
                float sgn = i == 0 ? 1f : -1f;
                shells[i].position = middle + side * sgn * 0.035f * u * K + Vector3.up * 0.01f * u;
                shells[i].rotation = Quaternion.AngleAxis(sgn * 70f * u, toward.sqrMagnitude > 1e-4f ? toward.normalized : Vector3.forward) * start[i];
            }
            float d = Mathf.Clamp01((t - open * 0.5f) / drop);
            egg.transform.position = Vector3.Lerp(middle, land, d * d);
            egg.transform.localScale = Vector3.one * Mathf.Lerp(0.25f, 1f, d); // it spreads out as it lands
            yield return null;
        }
        egg.transform.position = land;
        egg.transform.localScale = Vector3.one;
        egg.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
        egg.transform.SetParent(group, true);
        // the shell goes away (in the bin, not the pan)
        Vector3[] at = { shells[0].position, shells[1].position };
        for (float t = 0f; t < 1f; t += Time.deltaTime / 0.25f)
        {
            for (int i = 0; i < 2; i++)
            {
                shells[i].position = at[i] + Vector3.up * 0.06f * t;
                shells[i].localScale = Vector3.one * (1f - t * t);
            }
            yield return null;
        }
        foreach (var sh in shells) Destroy(sh.gameObject);
        Destroy(gameObject);
    }

    /// <summary>The salt shaken in (white grains), or the oil poured in (a thin golden stream, and the oil spreading out on top).</summary>
    public IEnumerator SeasonInto(Transform contents, Bounds vessel, Vector3 eye, float landY)
    {
        bool salt = seasoning == "salt";
        float h = UprightHeight();
        Aim(vessel, eye, h, salt ? 150f : 118f, (salt ? 0.035f : 0.05f) * K, out var pivot, out var upright, out var tipped);
        yield return CarryTo(pivot, upright, 0.3f);
        yield return Tip(upright, tipped, 0.25f);
        Vector3 Mouth() => transform.position + transform.rotation * (Vector3.up * h);

        if (salt)
        {
            var white = Plain(new Color(0.97f, 0.97f, 0.97f), 0.2f);
            var grains = new List<(Transform t, float vy)>();
            float next = 0f;
            for (float e = 0f; e < 0.8f || grains.Count > 0; e += Time.deltaTime)
            {
                if (e < 0.8f)
                {
                    transform.position = pivot + Vector3.up * Mathf.Sin(e * 42f) * 0.012f * K; // shake, shake
                    if (e >= next)
                    {
                        next += 0.02f;
                        var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
                        Destroy(g.GetComponent<Collider>());
                        g.name = "Salt";
                        g.transform.position = Mouth() + Random.insideUnitSphere * 0.006f * K;
                        g.transform.localScale = Vector3.one * 0.005f * K;
                        g.GetComponent<Renderer>().sharedMaterial = white;
                        grains.Add((g.transform, -Random.Range(0.2f, 0.6f)));
                    }
                }
                else transform.position = pivot;
                for (int i = grains.Count - 1; i >= 0; i--)
                {
                    var (t, vy) = grains[i];
                    vy -= 9.8f * Time.deltaTime;
                    t.position += Vector3.up * vy * Time.deltaTime;
                    if (t.position.y <= landY) { Destroy(t.gameObject); grains.RemoveAt(i); }
                    else grains[i] = (t, vy);
                }
                if (e > 2f) break; // never hang about
                yield return null;
            }
            transform.position = pivot;
        }
        else
        {
            var gold = Plain(new Color(0.93f, 0.74f, 0.2f), 0.92f);
            var stream = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Destroy(stream.GetComponent<Collider>());
            stream.name = "Oil stream";
            stream.GetComponent<Renderer>().sharedMaterial = gold;
            var slick = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Destroy(slick.GetComponent<Collider>());
            slick.name = "Oil";
            slick.GetComponent<Renderer>().sharedMaterial = gold;
            slick.transform.SetParent(contents, true);
            slick.transform.rotation = Quaternion.identity;
            float rMax = Mathf.Min(vessel.extents.x, vessel.extents.z) * 0.55f;
            for (float e = 0f; e < 0.9f; e += Time.deltaTime)
            {
                Vector3 m = Mouth();
                float full = Mathf.Max(0.005f, m.y - landY);
                float len = full * Mathf.Clamp01(e / 0.12f);               // the stream reaches down...
                float thin = e > 0.75f ? Mathf.Clamp01(1f - (e - 0.75f) / 0.15f) : 1f; // ...and thins out at the end
                stream.transform.position = new Vector3(m.x, m.y - len * 0.5f, m.z);
                stream.transform.localScale = new Vector3(0.005f * K * thin + 0.0005f, len * 0.5f, 0.005f * K * thin + 0.0005f);
                float spread = rMax * Mathf.Clamp01((e - 0.1f) / 0.7f);
                slick.transform.position = new Vector3(vessel.center.x, landY + 0.002f, vessel.center.z);
                slick.transform.localScale = Vector3.zero;
                SetWorldScale(slick.transform, new Vector3(spread * 2f + 0.001f, 0.0008f, spread * 2f + 0.001f));
                yield return null;
            }
            Destroy(stream);
        }
        yield return Tip(tipped, upright, 0.18f);
        yield return Shrink(0.2f);
        Destroy(gameObject);
    }

    /// <summary>The vessel lifted off the counter into your hands (then it's held like anything else).</summary>
    public IEnumerator LiftTo(Transform hand, float seconds)
    {
        var carry = GetComponent<Carryable>();
        foreach (var c in GetComponentsInChildren<Collider>()) c.enabled = false;
        Vector3 p0 = transform.position;
        Quaternion r0 = transform.rotation;
        for (float t = 0f; t < 1f && hand; t += Time.deltaTime / seconds)
        {
            float e = Mathf.SmoothStep(0f, 1f, t);
            transform.position = Vector3.Lerp(p0, hand.position, e) + Vector3.up * Mathf.Sin(e * Mathf.PI) * 0.08f;
            transform.rotation = Quaternion.Slerp(r0, hand.rotation, e);
            yield return null;
        }
        foreach (var c in GetComponentsInChildren<Collider>()) c.enabled = true;
    }

    float UprightHeight()
    {
        var saved = transform.rotation;
        transform.rotation = Quaternion.identity;
        float h = Mathf.Max(0.03f, Cooking.Bounds(gameObject).size.y);
        transform.rotation = saved;
        return h;
    }

    /// <summary>Where to hold it so that, tipped by tipDeg towards the vessel, its mouth is over the middle, mouthAbove over the rim.</summary>
    static void Aim(Bounds vessel, Vector3 eye, float h, float tipDeg, float mouthAbove, out Vector3 pivot, out Quaternion upright, out Quaternion tipped)
    {
        Vector3 toward = vessel.center - eye;
        toward.y = 0f;
        if (toward.sqrMagnitude < 1e-4f) toward = Vector3.forward;
        toward.Normalize();
        upright = Quaternion.LookRotation(toward, Vector3.up);
        var tilt = Quaternion.AngleAxis(tipDeg, Vector3.Cross(Vector3.up, toward)); // tips its top towards the vessel
        tipped = tilt * upright;
        var mouth = new Vector3(vessel.center.x, vessel.max.y + mouthAbove, vessel.center.z);
        pivot = mouth - tilt * (Vector3.up * h);
    }

    /// <summary>Out of your hands to a spot (its pivot = the bottom centre), turning to the given way round.</summary>
    public IEnumerator MoveTo(Vector3 pivot, Quaternion rotation, float seconds) => CarryTo(pivot, rotation, seconds);

    IEnumerator CarryTo(Vector3 pivot, Quaternion upright, float seconds)
    {
        transform.SetParent(null, true);
        var carry = GetComponent<Carryable>();
        if (carry) carry.enabled = false;
        foreach (var c in GetComponentsInChildren<Collider>()) c.enabled = false;
        Vector3 p0 = transform.position;
        Quaternion r0 = transform.rotation;
        for (float t = 0f; t < 1f; t += Time.deltaTime / seconds)
        {
            float e = Mathf.SmoothStep(0f, 1f, t);
            transform.position = Vector3.Lerp(p0, pivot, e) + Vector3.up * Mathf.Sin(e * Mathf.PI) * 0.05f;
            transform.rotation = Quaternion.Slerp(r0, upright, e);
            yield return null;
        }
        transform.position = pivot;
        transform.rotation = upright;
    }

    IEnumerator Tip(Quaternion from, Quaternion to, float seconds)
    {
        for (float t = 0f; t < 1f; t += Time.deltaTime / seconds)
        {
            transform.rotation = Quaternion.Slerp(from, to, Mathf.SmoothStep(0f, 1f, t));
            yield return null;
        }
        transform.rotation = to;
    }

    IEnumerator Shrink(float seconds)
    {
        Vector3 s0 = transform.localScale;
        for (float t = 0f; t < 1f; t += Time.deltaTime / seconds)
        {
            transform.localScale = s0 * (1f - t * t);
            yield return null;
        }
    }

    /// <summary>A handful of spaghetti coming out of the packet's mouth.</summary>
    List<Transform> Strands(Vector3 mouth)
    {
        var list = new List<Transform>();
        var mat = Plain(ing.colour, 0.3f);
        for (int i = 0; i < 14; i++)
        {
            var s = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Destroy(s.GetComponent<Collider>());
            s.name = "Spaghetti";
            s.transform.position = mouth + Random.insideUnitSphere * 0.01f * K;
            s.transform.rotation = Quaternion.Euler(Random.Range(70f, 110f), Random.Range(0f, 180f), 0f);
            s.transform.localScale = new Vector3(0.004f * K, 0.06f * K, 0.004f * K);
            s.GetComponent<Renderer>().sharedMaterial = mat;
            list.Add(s.transform);
        }
        return list;
    }

    static void SetWorldScale(Transform t, Vector3 world)
    {
        var p = t.parent ? t.parent.lossyScale : Vector3.one;
        t.localScale = new Vector3(world.x / Mathf.Max(1e-5f, p.x), world.y / Mathf.Max(1e-5f, p.y), world.z / Mathf.Max(1e-5f, p.z));
    }

    static Material Plain(Color c, float gloss)
    {
        var m = new Material(Shader.Find("Standard")) { color = c };
        m.SetFloat("_Glossiness", gloss);
        return m;
    }

    // ------------------------------------------------------------------ models

    public static GameObject KitModel(string id, float size)
    {
        var kit = CookingKit.Instance;
        var model = kit ? kit.Model(id) : null;
        if (!model) return null;
        var go = Instantiate(model);
        go.name = id;
        UseFoodPalette(go);
        Cooking.Fit(go, size * K);
        var holder = new GameObject(id);
        go.transform.SetParent(holder.transform, true);
        var b = Cooking.Bounds(go);
        go.transform.position -= new Vector3(b.center.x, b.min.y, b.center.z); // bottom centre at the holder
        return holder;
    }

    static Material foodMaterial;
    static bool paletteLooked;

    /// <summary>
    /// The Food Kit models are coloured from the Food Kit's own palette (Resources/FoodKitColormap). The one in the models folder is
    /// another Kenney kit's (they share the file name), which gave the food the wrong colours.
    /// </summary>
    public static void UseFoodPalette(GameObject go)
    {
        if (!paletteLooked)
        {
            paletteLooked = true;
            var tex = Resources.Load<Texture2D>("FoodKitColormap");
            if (tex)
            {
                foodMaterial = new Material(Shader.Find("Standard")) { name = "Food Kit", mainTexture = tex };
                foodMaterial.SetFloat("_Glossiness", 0.25f);
            }
        }
        if (!foodMaterial || !go) return;
        foreach (var r in go.GetComponentsInChildren<Renderer>(true))
        {
            var mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++) mats[i] = foodMaterial;
            r.sharedMaterials = mats;
        }
    }

    static GameObject Primitive(PrimitiveType type, Color colour, Vector3 size)
    {
        var holder = new GameObject(type.ToString());
        var p = GameObject.CreatePrimitive(type);
        Destroy(p.GetComponent<Collider>());
        p.transform.SetParent(holder.transform, false);
        p.transform.localScale = size * K;
        p.transform.localPosition = new Vector3(0f, size.y * K * (type == PrimitiveType.Cylinder || type == PrimitiveType.Capsule ? 1f : 0.5f), 0f);
        var m = new Material(Shader.Find("Standard")) { color = colour };
        m.SetFloat("_Glossiness", 0.3f);
        p.GetComponent<Renderer>().sharedMaterial = m;
        return holder;
    }
}
