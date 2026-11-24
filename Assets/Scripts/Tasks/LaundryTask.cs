using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Laundry:  dirty clothes are scattered round your room  ->  pick them up one by one (the armful grows)
///   ->  load them all into the washer  ->  it washes for washTime  ->  take the clean clothes out and put them
///   in your wardrobe. If the finished wash is left too long, the machine overflows and floods the room.
/// (Older setups with a single 'dirtyClothes' pile still work: carry the pile to the washer.)
/// </summary>
public class LaundryTask : TaskBase, IInteractable, IPromptProvider
{
    enum Phase { WaitingForClothes, Washing, Done, PutAway }

    [Header("Laundry")]
    [Tooltip("The dirty clothes pile. Hidden until the task starts.")]
    public Carryable dirtyClothes;
    [Tooltip("Clean clothes you take out of the washer and put away in the wardrobe. Empty = taking the laundry out finishes it.")]
    public Carryable cleanClothes;
    public Wardrobe wardrobe;
    [Tooltip("Where the clothes are, for the HUD: \"Dirty clothes are ...\" (e.g. \"upstairs\", \"in the kid's room\").")]
    public string clothesLocation = "upstairs";
    [Tooltip("With no single pile set: the room (house plan name) the dirty clothes get scattered round.")]
    public string scatterRoom = "Your bedroom";
    [Tooltip("How many pieces of dirty clothing to pick up.")]
    public int pieces = 5;
    public float washTime = 35f;
    [Tooltip("Seconds after washing finishes before the machine overflows.")]
    public float overflowAfter = 25f;
    public FloodHazard flood;

    [Header("'Machine is running' visuals")]
    public Light runningLight;
    [Tooltip("What shakes while washing (usually the machine itself).")]
    public Transform shakeTarget;
    public float shakeAmount = 0.004f;

    Phase phase;
    float washTimer;
    // Scattered clothes
    readonly List<DirtyClothesPiece> scattered = new List<DirtyClothesPiece>();
    Carryable armful;
    readonly List<GameObject> armfulLumps = new List<GameObject>();
    int collected;

    bool Scattering => !dirtyClothes;
    public int PiecesLeft => scattered.Count;
    /// <summary>The washer's empty and waiting for the dirty clothes (the cat likes to climb in then).</summary>
    public bool WaitingForLoad => IsActive && phase == Phase.WaitingForClothes;
    public IReadOnlyList<DirtyClothesPiece> Pieces => scattered;
    /// <summary>(The tutorial) where it's at: WaitingForClothes, Washing, Done, PutAway.</summary>
    public string PhaseName => phase.ToString();
    public Wardrobe Wardrobe => wardrobe;
    Vector3 shakeOrigin;
    bool shaking;

    void Reset()
    {
        taskName = "Do the laundry";
        timeLimit = 120f;
        stressOnFail = 12f;
        stressReliefOnComplete = 8f;
        cooldown = 45f;
    }

    void Start()
    {
        if (dirtyClothes) dirtyClothes.ReturnHome(false);
        if (cleanClothes) cleanClothes.ReturnHome(false);
        SetRunning(false);
    }

    public override bool CanStartNow => !(flood && flood.IsActive);

    public override float Progress => phase == Phase.Washing ? washTimer / washTime : phase == Phase.Done ? 1f : 0f;

    public override string Status
    {
        get
        {
            switch (phase)
            {
                case Phase.Washing: return $"Washing... {Seconds(washTime - washTimer)}";
                case Phase.Done: return "Done! Take it out before it overflows";
                case Phase.PutAway: return PlayerHolds(ItemType.CleanClothes) ? "Put the clean clothes in your wardrobe" : "The clean clothes go in your wardrobe";
                default:
                    if (Scattering)
                    {
                        if (scattered.Count == 0) return "Got them all - load the washer";
                        return $"Dirty clothes around your room ({collected}/{collected + scattered.Count} picked up)";
                    }
                    return PlayerHolds(ItemType.DirtyClothes) ? "Take the clothes to the washer" : "Dirty clothes are " + clothesLocation;
            }
        }
    }

    public string Prompt
    {
        get
        {
            if (!IsActive) return null;
            switch (phase)
            {
                case Phase.Washing: return $"Washing... {Seconds(washTime - washTimer)}";
                case Phase.Done: return "Take out the laundry";
                case Phase.PutAway: return null;
                default: return PlayerHolds(ItemType.DirtyClothes) ? "Load clothes and start" : "Washer - needs dirty clothes";
            }
        }
    }

    public void Interact()
    {
        if (!IsActive) return;

        if (CatBrain.InWasher)
        {
            GameHUD.Toast($"{CatBrain.Name} is inside the washer! Lift her out first (E on the cat).", GameHUD.Warn);
            return;
        }
        switch (phase)
        {
            case Phase.WaitingForClothes:
                if (!PlayerHolds(ItemType.DirtyClothes))
                {
                    GameHUD.Toast("Bring the dirty clothes first!", GameHUD.Warn);
                    return;
                }
                if (Scattering && scattered.Count > 0)
                {
                    GameHUD.Toast(scattered.Count == 1 ? "One more piece is still lying around your room!" : $"{scattered.Count} more pieces are still lying around your room!", GameHUD.Warn);
                    return;
                }
                var clothes = PlayerCarry.Instance.TakeHeld();
                if (clothes) clothes.ReturnHome(false);
                collected = 0;
                phase = Phase.Washing;
                washTimer = 0f;
                StopDeadline();
                SetRunning(true);
                GameHUD.Toast("Washer started.", GameHUD.Info);
                break;

            case Phase.Washing:
                GameHUD.Toast($"Still washing - {Seconds(washTime - washTimer)} left", GameHUD.Info);
                break;

            case Phase.Done:
                if (cleanClothes && wardrobe)
                {
                    // You get an armful of clean clothes: they go in the wardrobe in your room.
                    phase = Phase.PutAway;
                    StopDeadline();
                    SetRunning(false);
                    DressCleanClothes();
                    cleanClothes.ReturnHome(true);
                    if (PlayerCarry.Instance) PlayerCarry.Instance.PickUp(cleanClothes);
                    GameHUD.Toast("Clean clothes! Put them in your wardrobe.", GameHUD.Info);
                    break;
                }
                Complete("Laundry done!");
                break;
        }
    }

    /// <summary>The wardrobe says the clean clothes are in.</summary>
    public void ClothesPutAway(float foldQuality = 1f)
    {
        if (!IsActive || phase != Phase.PutAway) return;
        // How well it was folded: Mom notices (not the tutorial's practice wash).
        if (TutorialDay.Active) { Complete("Laundry done!"); return; }
        if (foldQuality >= 0.99f) { DayManager.AddQuality("Laundry folded perfectly", 4); Complete("Laundry done - perfectly folded!"); }
        else if (foldQuality >= 0.6f) { DayManager.AddQuality("Laundry folded neatly", 2); Complete("Laundry done - neatly folded."); }
        else Complete("Laundry done... a bit wrinkly.");
    }

    public override Vector3 Where => phase == Phase.PutAway && wardrobe ? wardrobe.transform.position : transform.position;

    protected override void OnActivated()
    {
        phase = Phase.WaitingForClothes;
        washTimer = 0f;
        if (dirtyClothes) dirtyClothes.ReturnHome(true);
        else Scatter();
    }

    // ------------------------------------------------------------------ scattered clothes

    /// <summary>Drops the dirty clothes in random spots round the room: on the floor, the bed, a chair...</summary>
    void Scatter()
    {
        ClearScattered();
        collected = 0;
        EnsureArmful();
        armful.ReturnHome(false);

        var layout = HouseLayout.Instance;
        HouseLayout.RoomInfo room = null;
        if (layout) foreach (var r in layout.rooms) if (r.name == scatterRoom) room = r;
        if (room == null) { Debug.LogWarning($"[Laundry] No room called '{scatterRoom}' to scatter the clothes in."); return; }

        float scale = layout.scale;
        float floorY = room.floor * layout.storeyHeight * scale;
        var spots = new List<Vector3>();
        for (int tries = 0; tries < 300 && spots.Count < pieces; tries++)
        {
            var plan = new Vector3(Random.Range(room.rect.xMin + 0.4f, room.rect.xMax - 0.4f), 0f, Random.Range(room.rect.yMin + 0.4f, room.rect.yMax - 0.4f));
            Vector3 top = layout.transform.TransformPoint(plan);
            top.y = floorY + 1.6f * scale;
            if (!Physics.Raycast(top, Vector3.down, out var hit, 2.2f * scale, ~0, QueryTriggerInteraction.Ignore)) continue;
            if (hit.normal.y < 0.75f || hit.point.y > floorY + 1.0f * scale) continue; // flat, and not up on a shelf
            if (hit.collider.GetComponentInParent<Carryable>() || hit.collider.GetComponentInParent<CatBrain>()) continue;
            bool onFloor = hit.point.y < floorY + 0.15f;
            if (onFloor && Physics.CheckSphere(hit.point + Vector3.up * 0.3f, 0.25f, ~0, QueryTriggerInteraction.Ignore)) continue; // tucked under something
            if (spots.Exists(s => Vector3.Distance(s, hit.point) < 1.1f * scale)) continue;
            spots.Add(hit.point);
        }
        for (int i = 0; i < spots.Count; i++)
            scattered.Add(DirtyClothesPiece.Create(spots[i] + Vector3.up * 0.005f, Mathf.Max(0.8f, scale * 0.9f), this, i));
    }

    // what went in the wash (kind, colour): it comes out as a neat folded stack
    readonly List<(string kind, Color colour)> washed = new List<(string kind, Color colour)>();
    Mesh cleanStack;

    /// <summary>The clean clothes become a neat stack of folded clothes - the ones that went in the wash, fresh.</summary>
    void DressCleanClothes()
    {
        if (!cleanClothes) return;
        var mf = cleanClothes.GetComponent<MeshFilter>();
        var mr = cleanClothes.GetComponent<MeshRenderer>();
        if (!mf || !mr) return;
        var items = washed.FindAll(w => w.kind != "socks"); // (socks are tucked away inside: too small for the pile)
        if (items.Count == 0)
            for (int i = 0; i < 3; i++) { string k = DirtyClothesPiece.KindFor(i == 1 ? 2 : i); items.Add((k, DirtyClothesPiece.RandomColour(k))); }
        // the biggest at the bottom, socks on top, at most five
        items.Sort((a, b) => Rank(a.kind).CompareTo(Rank(b.kind)));
        // a proper pile, not one lonely shirt (a short wash - the tutorial's two pieces - gets a few more from the basket)
        var fill = new[] { "t-shirt", "trousers", "shirt", "hoodie", "t-shirt" };
        for (int i = 0; items.Count < 4; i++) items.Add((fill[i % fill.Length], DirtyClothesPiece.RandomColour(fill[i % fill.Length])));
        items.Sort((a, b) => Rank(a.kind).CompareTo(Rank(b.kind)));
        if (items.Count > 5) items.RemoveRange(5, items.Count - 5);
        float s = Mathf.Max(0.01f, cleanClothes.transform.lossyScale.x);
        if (cleanStack) Destroy(cleanStack);
        cleanStack = DirtyClothesPiece.FoldedStack(items, 0.3f / s, out var mats);
        // keep it standing where the old model stood (centred, same floor)
        var oldBounds = mf.sharedMesh ? mf.sharedMesh.bounds : new Bounds();
        var v = cleanStack.vertices;
        Vector3 shift = new Vector3(oldBounds.center.x, oldBounds.min.y, oldBounds.center.z);
        for (int i = 0; i < v.Length; i++) v[i] += shift;
        cleanStack.vertices = v;
        cleanStack.RecalculateBounds();
        mf.sharedMesh = cleanStack;
        mr.sharedMaterials = mats;
        var box = cleanClothes.GetComponent<BoxCollider>();
        if (box) { box.center = cleanStack.bounds.center; box.size = cleanStack.bounds.size; }
        var mc = cleanClothes.GetComponent<MeshCollider>();
        if (mc) { mc.sharedMesh = null; mc.convex = true; mc.sharedMesh = cleanStack; }
    }

    static int Rank(string kind) => kind == "trousers" ? 0 : kind == "hoodie" ? 1 : kind == "shirt" ? 2 : kind == "t-shirt" ? 3 : 4;

    /// <summary>A piece was picked up: onto the armful (which you carry).</summary>
    public void PickUpPiece(DirtyClothesPiece piece)
    {
        if (!IsActive || phase != Phase.WaitingForClothes || !scattered.Contains(piece)) return;
        var carry = PlayerCarry.Instance;
        if (!carry) return;
        if (carry.Held && carry.Held != armful)
        {
            GameHUD.Toast("Your hands are full - drop that first (G).", GameHUD.Warn);
            return;
        }
        scattered.Remove(piece);
        var look = piece.GetComponentInChildren<MeshRenderer>();
        Material[] mats = null;
        if (look)
        {
            var own = System.Array.FindAll(look.sharedMaterials, m => m && !m.name.Contains("Outline"));
            if (own.Length >= 2) mats = new[] { own[0], own[1] };
        }
        if (collected == 0) washed.Clear();
        if (mats != null && mats.Length > 0 && mats[0]) washed.Add((piece.kind, mats[0].color));
        Destroy(piece.gameObject);
        EnsureArmful();
        int before = collected == 0 ? 0 : ShowLumps(collected);
        collected++;
        int after = ShowLumps(collected);
        // the garment you just picked up lands on top of the heap, as it was on the floor
        if (mats != null && mats.Length >= 2) Dress(Mathf.Clamp(after - 1, before, armfulLumps.Count - 1), piece.kind, mats);
        if (carry.Held != armful)
        {
            if (!armful.gameObject.activeSelf || armful.IsHeld == false) armful.ReturnHome(true);
            carry.PickUp(armful);
        }
        GameHUD.Toast(scattered.Count == 0 ? "Got them all! Now to the washer." : $"Dirty clothes {collected}/{collected + scattered.Count}", scattered.Count == 0 ? GameHUD.Good : GameHUD.Info);
    }

    /// <summary>The robot vacuum swallowed a piece. It always leaves the last one, so the player still carries an armful to the washer.</summary>
    public bool RobotTake(DirtyClothesPiece piece)
    {
        if (!IsActive || phase != Phase.WaitingForClothes || !scattered.Contains(piece) || scattered.Count <= 1) return false;
        scattered.Remove(piece);
        Destroy(piece.gameObject);
        collected++;
        EnsureArmful();
        ShowLumps(collected);
        GameHUD.Toast($"The robot vacuum tidied up a piece! {scattered.Count} left.", GameHUD.Good);
        return true;
    }

    /// <summary>The armful of dirty clothes you carry (it grows with each piece).</summary>
    void EnsureArmful()
    {
        if (armful) return;
        var go = new GameObject("Dirty laundry (armful)");
        go.transform.position = transform.position + Vector3.up * 0.05f;
        // a laundry basket; the clothes you pick up pile into it (each pick-up shows more of the heap)
        var basket = new GameObject("Laundry basket");
        basket.transform.SetParent(go.transform, false);
        basket.AddComponent<MeshFilter>().sharedMesh = BasketMesh(BasketW, BasketD, BasketH);
        var plastic = new Material(Shader.Find("Standard")) { color = new Color(0.58f, 0.78f, 0.95f) };
        plastic.SetFloat("_Glossiness", 0.35f);
        var plasticDark = new Material(Shader.Find("Standard")) { color = new Color(0.4f, 0.6f, 0.82f) };
        plasticDark.SetFloat("_Glossiness", 0.3f);
        basket.AddComponent<MeshRenderer>().sharedMaterials = new[] { plastic, plasticDark };
        // underneath: a lumpy bed of clothes that fills the basket (it rises as the basket fills)
        armBed = new GameObject("Clothes in the basket").transform;
        armBed.SetParent(go.transform, false);
        armBed.gameObject.AddComponent<MeshFilter>().sharedMesh = ClothBed(BasketW * 0.94f, BasketD * 0.94f);
        var bedMat = new Material(Shader.Find("Standard")) { color = new Color(0.42f, 0.47f, 0.58f) };
        bedMat.SetFloat("_Glossiness", 0.05f);
        armBed.gameObject.AddComponent<MeshRenderer>().sharedMaterial = bedMat;
        for (int i = 0; i < Mathf.Max(8, pieces); i++)
        {
            var layer = new GameObject("Armful garment");
            layer.transform.SetParent(go.transform, false);
            layer.transform.localRotation = Quaternion.Euler(Random.Range(-6f, 6f), (i % 2) * 90f + Random.Range(-25f, 25f), Random.Range(-5f, 5f));
            layer.AddComponent<MeshFilter>();
            layer.AddComponent<MeshRenderer>();
            string kind = DirtyClothesPiece.KindFor(i == 1 ? 2 : i == 2 ? 3 : i); // (socks aren't the heap: shirt, jeans, hoodie first)
            if (kind == "socks") kind = "t-shirt";
            armfulLumps.Add(layer);
            Dress(i, kind, DirtyClothesPiece.Materials(kind, DirtyClothesPiece.RandomColour(kind)));
        }
        var box = go.AddComponent<BoxCollider>();
        box.center = new Vector3(0f, BasketH * 0.5f, 0f);
        box.size = new Vector3(BasketW * 1.05f, BasketH, BasketD * 1.05f);
        armful = go.AddComponent<Carryable>();
        armful.type = ItemType.DirtyClothes;
        armful.displayName = "dirty clothes";
        armful.maxHeldSize = 0.42f;
        armful.heldRotation = new Vector3(-24f, -15f, 0f); // (tipped towards you a little: you see what's in it)
        var outline = go.AddComponent<Outline>();
        outline.OutlineMode = Outline.Mode.OutlineVisible;
        outline.OutlineColor = Color.white;
        outline.OutlineWidth = 5f;
        outline.enabled = false;
        go.SetActive(false);
    }

    const float BasketW = 0.5f, BasketD = 0.37f, BasketH = 0.24f;

    /// <summary>
    /// A plastic laundry basket: a rounded-rectangle tub, a bit narrower at the bottom, open at the top with a rolled rim, two
    /// grooves round it and a hand-hold at each end. Submesh 0 the plastic, 1 the darker grooves / hand-holds.
    /// </summary>
    static Mesh BasketMesh(float w, float d, float h)
    {
        const int N = 64;
        var verts = new List<Vector3>();
        var main = new List<int>();
        var dark = new List<int>();
        Vector2 Edge(int i, float sc)
        {
            float a = i / (float)N * Mathf.PI * 2f;
            float cx = Mathf.Cos(a), cz = Mathf.Sin(a);
            const float n = 4f;
            float r = Mathf.Pow(Mathf.Pow(Mathf.Abs(cx), n) + Mathf.Pow(Mathf.Abs(cz), n), -1f / n);
            return new Vector2(cx * r * w * 0.5f * sc, cz * r * d * 0.5f * sc);
        }
        // the profile, from the middle of the bottom, out, up the outside, over the rim, down the inside, to the middle of the floor
        float t = 0.012f;
        var profile = new List<(float sc, float y, bool groove)>
        {
            (0f, 0f, false), (0.84f, 0f, false), (0.86f, 0.012f, false),
            (0.9f, h * 0.33f, false), (0.895f, h * 0.36f, true), (0.91f, h * 0.39f, false),   // a groove
            (0.95f, h * 0.62f, false), (0.945f, h * 0.65f, true), (0.96f, h * 0.68f, false), // another
            (1f, h, false), (1.035f, h, false), (1.035f, h + t, false), (0.965f, h + t, false),
            (0.955f, h - t * 0.5f, false), (0.82f, t, false), (0f, t, false),
        };
        var rings = new List<int[]>();
        foreach (var (sc, y, _) in profile)
        {
            var ring = new int[N];
            for (int i = 0; i < N; i++) { var e = Edge(i, sc); ring[i] = verts.Count; verts.Add(new Vector3(e.x, y, e.y)); }
            rings.Add(ring);
        }
        for (int k = 0; k + 1 < rings.Count; k++)
        {
            bool groove = profile[k].groove || profile[k + 1].groove;
            for (int i = 0; i < N; i++)
            {
                int j = (i + 1) % N;
                int a = rings[k][i], b = rings[k][j], c = rings[k + 1][i], dd = rings[k + 1][j];
                // a hand-hold: the dark slot near the top at each end (the short sides)
                Vector3 mid = (verts[a] + verts[dd]) * 0.5f;
                bool hold = k >= 6 && k <= 8 && Mathf.Abs(mid.x) > w * 0.42f && Mathf.Abs(mid.z) < d * 0.16f;
                (groove || hold ? dark : main).AddRange(new[] { a, c, b, b, c, dd });
            }
        }
        var m = new Mesh { name = "Laundry basket" };
        m.SetVertices(verts);
        m.subMeshCount = 2;
        m.SetTriangles(main, 0);
        m.SetTriangles(dark, 1);
        m.RecalculateNormals();
        m.RecalculateBounds();
        return m;
    }

    /// <summary>Shows as much of the heap as 'count' pieces make; returns how many garments show.</summary>
    int ShowLumps(int count)
    {
        float full = Mathf.Clamp01((float)count / Mathf.Max(1, pieces));
        int show = Mathf.Clamp(Mathf.CeilToInt(Mathf.Lerp(3f, armfulLumps.Count, full)), 1, armfulLumps.Count);
        // the bed of clothes rises with the basket's fill (and fills it wall to wall: the basket narrows to the bottom)
        float bedY = Mathf.Lerp(0.07f, 0.19f, full);
        if (armBed)
        {
            float widen = Mathf.Lerp(0.86f, 1f, bedY / BasketH);
            armBed.localPosition = new Vector3(0f, bedY, 0f);
            armBed.localScale = new Vector3(widen, Mathf.Lerp(0.7f, 1.2f, full), widen);
        }
        // the garments lie on it, spread over the whole basket, the last ones heaped up over the rim
        for (int i = 0; i < armfulLumps.Count; i++)
        {
            if (!armfulLumps[i]) continue;
            armfulLumps[i].SetActive(i < show);
            float a = i * 2.4f;
            float spread = i < 4 ? 1f : 0.45f;
            var p = new Vector3(Mathf.Cos(a) * BasketW * 0.2f * spread, bedY + 0.025f + i * 0.012f * (0.6f + full), Mathf.Sin(a) * BasketD * 0.18f * spread);
            armfulLumps[i].transform.localPosition = p;
        }
        return show;
    }

    Transform armBed;

    /// <summary>A soft, lumpy mound of clothes the size of the basket's inside (top only: it's seen from above).</summary>
    static Mesh ClothBed(float w, float d)
    {
        const int nx = 26, nz = 20;
        var verts = new List<Vector3>();
        var tris = new List<int>();
        var idx = new int[nx + 1, nz + 1];
        float ox = Random.value * 50f, oz = Random.value * 50f;
        for (int i = 0; i <= nx; i++)
        for (int j = 0; j <= nz; j++)
        {
            float u = i / (float)nx * 2f - 1f, v = j / (float)nz * 2f - 1f;
            // squarish outline (like the basket), the edge tucked down at the walls
            float r = Mathf.Pow(Mathf.Pow(Mathf.Abs(u), 4f) + Mathf.Pow(Mathf.Abs(v), 4f), 0.25f);
            float k = r > 1f ? 1f / r : 1f;
            float x = u * k * w * 0.5f, z = v * k * d * 0.5f;
            float rr = Mathf.Min(1f, r);
            float h = 0.03f * (1f - rr * rr) + 0.022f * Mathf.PerlinNoise(ox + x * 14f, oz + z * 14f) * (1f - rr * 0.6f) - 0.012f * rr * rr;
            idx[i, j] = verts.Count;
            verts.Add(new Vector3(x, h, z));
        }
        for (int i = 0; i < nx; i++)
        for (int j = 0; j < nz; j++)
        {
            int a = idx[i, j], b = idx[i + 1, j], c = idx[i, j + 1], dd = idx[i + 1, j + 1];
            tris.AddRange(new[] { a, c, b, b, c, dd });
        }
        var m = new Mesh { name = "Clothes in the basket" };
        m.SetVertices(verts);
        m.SetTriangles(tris, 0);
        m.RecalculateNormals();
        m.RecalculateBounds();
        return m;
    }

    /// <summary>Gives garment 'i' of the heap this kind and look.</summary>
    void Dress(int i, string kind, Material[] mats)
    {
        if (i < 0 || i >= armfulLumps.Count || !armfulLumps[i]) return;
        var mf = armfulLumps[i].GetComponent<MeshFilter>();
        if (mf.sharedMesh) Destroy(mf.sharedMesh);
        // heaped in the basket: a soft dome, the ends tucked down inside it
        mf.sharedMesh = DirtyClothesPiece.DrapedGarment(kind, Random.Range(0, 9999), kind == "socks" ? 0.5f : 0.36f, 0.2f + 0.015f * i);
        armfulLumps[i].GetComponent<MeshRenderer>().sharedMaterials = mats;
    }

    void ClearScattered()
    {
        foreach (var p in scattered) if (p) Destroy(p.gameObject);
        scattered.Clear();
    }

    protected override void TaskUpdate()
    {
        if (phase == Phase.Washing)
        {
            washTimer += Time.deltaTime;
            if (shaking && shakeTarget)
                shakeTarget.position = shakeOrigin + Random.insideUnitSphere * shakeAmount;

            if (washTimer >= washTime)
            {
                phase = Phase.Done;
                StopShaking();
                StartDeadline(overflowAfter);
                GameHUD.Toast("The laundry is done - take it out!", GameHUD.Warn);
            }
        }
        else if (phase == Phase.Done && runningLight)
        {
            runningLight.enabled = Mathf.Repeat(Time.time, 0.8f) < 0.4f; // blink: "I'm done!"
        }
    }

    protected override void OnDeadlineReached()
    {
        if (phase != Phase.Done)
        {
            base.OnDeadlineReached(); // never brought the clothes
            return;
        }

        Fail("The washing machine overflowed!");
        if (flood && GameManager.Instance) GameManager.Instance.StartTask(flood);
    }

    protected override void OnCompleted() => ResetMachine();

    protected override void OnFailed()
    {
        ClearScattered();
        if (armful) armful.ReturnHome(false);
        ResetMachine();
        if (dirtyClothes) dirtyClothes.ReturnHome(false); // also takes them out of the player's hands
        if (cleanClothes) cleanClothes.ReturnHome(false);
    }

    /// <summary>(The tutorial, when it's over or skipped) the practice wash is cleared away: no clothes about, a quiet empty machine.</summary>
    public void TutorialReset()
    {
        ClearScattered();
        if (armful) armful.ReturnHome(false);
        if (dirtyClothes) dirtyClothes.ReturnHome(false);
        if (cleanClothes) cleanClothes.ReturnHome(false);
        ResetMachine();
        if (IsActive) StopQuietly();
    }

    void ResetMachine()
    {
        phase = Phase.WaitingForClothes;
        SetRunning(false);
    }

    void SetRunning(bool on)
    {
        if (runningLight) runningLight.enabled = on;
        if (on)
        {
            if (shakeTarget) { shakeOrigin = shakeTarget.position; shaking = true; }
        }
        else
        {
            StopShaking();
        }
    }

    void StopShaking()
    {
        if (shaking && shakeTarget) shakeTarget.position = shakeOrigin;
        shaking = false;
    }
}
