using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// Your room grows with you (see HouseBuilder.BuildGamerRoom for what's placed where, and RoomUnlock for the keys):
///   - every piece tagged with a RoomUnlock (your own webcam, mic, second monitor, TV, LED strips, gaming chair... and the furniture
///     the tool placed: ring light, sofa, TV + consoles, trophy cabinet, big PC, fridge, air hockey...) is shown only once earned;
///   - things the kits don't have are built here under their anchors: a neon sign with your gamer tag (500 followers), acoustic
///     foam panels (1,000 followers), hex light panels (Diamond) and the Season Final trophy on a lit stand;
///   - the trophy shelf above the cabinet: rank plaque, a cup per tournament podium, follower plaques (100 / 500 / 1,000).
/// Everything updates live when you buy, win, rank up or gain followers. Added by the GameManager.
/// </summary>
public class RoomProgress : MonoBehaviour
{
    const string RoomName = "Your bedroom";
    const int WallLayer = 9;

    GamingPC pc;
    Transform room, shelfRoot;
    readonly List<RoomUnlock> unlocks = new List<RoomUnlock>();
    HouseLayout layout;
    HouseLayout.RoomInfo roomInfo;
    float k = 1.1f;                 // real-world size -> house size (the house is built a bit bigger than life)
    Vector3 shelfAt, shelfOut, shelfAlong;
    bool hasShelf;
    TMP_FontAsset font;
    bool shelfDirty, unlocksDirty;
    string shelfShown;
    float nextShelfCheck;

    IEnumerator Start()
    {
        yield return null; // the PC and the house have set themselves up
        yield return null;
        // First of all: what you haven't earned yet disappears (this must never be skipped).
        unlocks.AddRange(FindObjectsOfType<RoomUnlock>(true));
        Subscribe();
        pc = GamingPC.Instance;
        ApplyUnlocks();
        layout = HouseLayout.Instance;
        if (!pc || !layout) { Debug.LogWarning("[Room] No gaming PC or house layout - only the unlocks run."); yield break; }
        room = pc.transform.parent;
        foreach (var r in layout.rooms) if (r.name == RoomName && r.floor == 1) roomInfo = r;
        if (roomInfo == null || !room) { Debug.LogWarning("[Room] Couldn't find your bedroom in the house layout."); yield break; }
        k = layout.scale * 0.85f;
        font = GameHUD.Instance ? GameHUD.Instance.font : null;
        Physics.SyncTransforms();
        tableAnchor = AnchorNamed("Trophy table anchor");
        var anchor = AnchorNamed("Trophy shelf anchor");
        if (tableAnchor) hasShelf = false; // the trophy table replaces the wall shelf
        else if (anchor)
        {
            // Placed by the gamer room tool: right above the trophy cabinet.
            hasShelf = true;
            shelfAt = anchor.position;
            shelfOut = anchor.forward;
            shelfAlong = Vector3.Cross(Vector3.up, shelfOut);
        }
        else hasShelf = FindShelfSpot();
        try { BuildExtras(); }
        catch (System.Exception e) { Debug.LogException(e); }
        ApplyUnlocks();
        shelfDirty = true;
    }

    bool subscribed;

    void Subscribe()
    {
        if (subscribed) return;
        subscribed = true;
        Shop.Changed += MarkGear;
        Trophies.Changed += MarkShelf;
        Campaign.Changed += MarkShelf;
        ClipHub.Changed += MarkShelf;
    }

    void OnDestroy()
    {
        Shop.Changed -= MarkGear;
        Trophies.Changed -= MarkShelf;
        Campaign.Changed -= MarkShelf;
        ClipHub.Changed -= MarkShelf;
    }

    void MarkGear() => unlocksDirty = true;
    void MarkShelf() { shelfDirty = true; unlocksDirty = true; }

    void Update()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        // Debug: Ctrl+Y = win a tournament cup (Ctrl+Shift+Y = a Season Final cup) to see the shelf fill up.
        if ((Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) && Input.GetKeyDown(KeyCode.Y))
        {
            bool final = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            Trophies.Add(final, Random.value < 0.6f, final ? "[debug] Season Final" : "[debug] tournament");
        }
#endif
        if (unlocksDirty) { unlocksDirty = false; ApplyUnlocks(); }
        if (tableAnchor) UpdateReveal();
        // Followers change all the time: only rebuild the shelf when something on it would look different.
        if (shelfDirty && Time.time >= nextShelfCheck && room)
        {
            shelfDirty = false;
            nextShelfCheck = Time.time + 1f;
            int f = ClipHub.Followers;
            string sig = $"{Campaign.CurrentRank}|{Campaign.Rating}|{Trophies.All.Count}|{(f >= 1000 ? 3 : f >= 500 ? 2 : f >= 100 ? 1 : 0)}";
            if (sig != shelfShown)
            {
                shelfShown = sig;
                try { if (tableAnchor) BuildShowcase(); else BuildShelf(); }
                catch (System.Exception e) { Debug.LogException(e); }
            }
        }
    }

    // ================================================================== the trophy shelf

    Vector3 Plan(float x, float z, float y) { var p = layout.transform.TransformPoint(new Vector3(x, 0f, z)); p.y = y; return p; }

    /// <summary>A free piece of wall: the shelf (1 m wide) fits with nothing in front, above or below it.</summary>
    bool FindShelfSpot()
    {
        float floorY = layout.transform.TransformPoint(new Vector3(0f, roomInfo.floor * layout.storeyHeight, 0f)).y;
        var rect = roomInfo.rect;
        Vector3 desk = Bounds(pc.gameObject).center;
        float bestScore = float.MinValue;
        var roomRenderers = room.GetComponentsInChildren<Renderer>();
        foreach (float height in new[] { 1.55f, 1.4f, 1.75f })
        {
            float y = floorY + height * k;
            for (int side = 0; side < 4; side++)
            for (float t = 0.12f; t <= 0.881f; t += 0.04f)
            {
                // A point 0.6 m inside the room, looking at the wall.
                float px = side < 2 ? Mathf.Lerp(rect.xMin, rect.xMax, t) : side == 2 ? rect.xMin + 0.6f : rect.xMax - 0.6f;
                float pz = side >= 2 ? Mathf.Lerp(rect.yMin, rect.yMax, t) : side == 0 ? rect.yMin + 0.6f : rect.yMax - 0.6f;
                Vector3 planDir = side == 0 ? Vector3.back : side == 1 ? Vector3.forward : side == 2 ? Vector3.left : Vector3.right;
                Vector3 origin = Plan(px, pz, y);
                Vector3 dir = layout.transform.TransformDirection(planDir);
                dir.y = 0f; dir.Normalize();
                if (!WallHit(origin, dir, out var hit)) continue;
                Vector3 n = hit.normal; n.y = 0f;
                if (n.sqrMagnitude < 0.5f) continue;
                n.Normalize();
                Vector3 along = Vector3.Cross(Vector3.up, n);
                float half = 0.55f * k;
                // The wall must carry on under both ends of the shelf (no window, door or corner there).
                if (!WallHit(origin + along * half, dir, out var a) || !WallHit(origin - along * half, dir, out var b)) continue;
                if (Mathf.Abs(a.distance - hit.distance) > 0.06f || Mathf.Abs(b.distance - hit.distance) > 0.06f) continue;

                // Nothing in the way: colliders, or anything drawn there (wall art, curtains, shelves without colliders).
                Vector3 c = hit.point + n * 0.16f * k + Vector3.up * 0.17f * k;
                var halfBox = new Vector3(half, 0.3f * k, 0.15f * k);
                var rot = Quaternion.LookRotation(n);
                bool blocked = false;
                foreach (var col in Physics.OverlapBox(c, halfBox, rot, ~(1 << WallLayer), QueryTriggerInteraction.Ignore))
                    if (!col.transform.IsChildOf(transform) && !(PlayerInteraction.Instance && col.transform.IsChildOf(PlayerInteraction.Instance.transform))) { blocked = true; break; }
                if (blocked) continue;
                var area = new Bounds(c, new Vector3(half * 2f, 0.62f * k, half * 2f));
                foreach (var r in roomRenderers)
                    if (r && r.enabled && r.bounds.Intersects(area) && Vector3.Dot(r.bounds.center - hit.point, n) > 0.02f) { blocked = true; break; }
                if (blocked) continue;

                float score = -Vector3.Distance(Flatten(hit.point), Flatten(desk)) - Mathf.Abs(height - 1.55f) * 3f;
                if (score > bestScore)
                {
                    bestScore = score;
                    shelfAt = hit.point + n * 0.004f;
                    shelfAt.y = y;
                    shelfOut = n;
                    shelfAlong = along;
                }
            }
            if (bestScore > float.MinValue) return true;
        }
        return false;
    }

    bool WallHit(Vector3 origin, Vector3 dir, out RaycastHit hit)
    {
        if (!Physics.Raycast(origin, dir, out hit, 1.3f * k, 1 << WallLayer, QueryTriggerInteraction.Ignore)) return false;
        for (var t = hit.collider.transform; t; t = t.parent)
            if (t.name.Contains("Window") || t.name.Contains("Door")) return false;
        return true;
    }

    static Vector3 Flatten(Vector3 v) { v.y = 0f; return v; }

    void BuildShelf()
    {
        if (!hasShelf) return;
        if (shelfRoot) Destroy(shelfRoot.gameObject);
        shelfRoot = new GameObject("Trophy shelf").transform;
        shelfRoot.SetParent(transform, false);
        shelfRoot.SetPositionAndRotation(shelfAt, Quaternion.LookRotation(shelfOut));
        // shelfRoot: +z out of the wall, +x along it, +y up. All sizes below are real metres times k.

        float w = 1.1f, d = 0.24f, thick = 0.035f;
        Cube(shelfRoot, "Plank", new Vector3(0f, -thick * 0.5f, d * 0.5f), new Vector3(w, thick, d), Wood);
        foreach (float x in new[] { -0.4f, 0.4f })
        {
            Cube(shelfRoot, "Bracket", new Vector3(x, -0.07f, 0.012f), new Vector3(0.025f, 0.14f, 0.024f), Dark);
            Cube(shelfRoot, "Bracket", new Vector3(x, -thick - 0.008f, 0.1f), new Vector3(0.025f, 0.016f, 0.18f), Dark);
        }

        var items = new List<System.Action<float>>();
        items.Add(x => RankPlaque(x));
        var cups = Trophies.All;
        int firstCup = Mathf.Max(0, cups.Count - 5);
        for (int i = firstCup; i < cups.Count; i++) { var t = cups[i]; items.Add(x => Cup(x, t)); }
        int f = ClipHub.Followers;
        if (f >= 100) items.Add(x => FollowerPlaque(x, 100));
        if (f >= 500) items.Add(x => FollowerPlaque(x, 500));
        if (f >= 1000) items.Add(x => FollowerPlaque(x, 1000));
        while (items.Count > 7) items.RemoveAt(1); // keep the rank plaque and the newest things

        float usable = w - 0.14f;
        for (int i = 0; i < items.Count; i++)
        {
            float x = items.Count == 1 ? 0f : -usable * 0.5f + usable * i / (items.Count - 1);
            if (items.Count <= 3) x = (i - (items.Count - 1) * 0.5f) * 0.28f;
            items[i](x);
        }
    }

    void RankPlaque(float x)
    {
        var rank = Campaign.CurrentRank;
        var p = new GameObject("Rank plaque").transform;
        p.SetParent(shelfRoot, false);
        p.localPosition = new Vector3(x, 0f, 0.1f) * k;
        p.localRotation = Quaternion.Euler(-8f, 0f, 0f);
        Cube(p, "Frame", new Vector3(0f, 0.11f, 0f), new Vector3(0.17f, 0.22f, 0.02f), Dark);
        Cube(p, "Face", new Vector3(0f, 0.11f, 0.0115f), new Vector3(0.145f, 0.195f, 0.004f), Metal(Campaign.RankColour(rank), 0.6f));
        Label(p, $"<b>{Campaign.RankName(rank).ToUpperInvariant()}</b>\n<size=60%>{Campaign.Rating} RP</size>", new Vector3(0f, 0.11f, 0.0145f), new Vector2(0.13f, 0.12f), new Color(0.1f, 0.08f, 0.12f));
    }

    void Cup(float x, Trophies.Trophy t)
    {
        bool big = t.kind == Trophies.Kind.FinalCup;
        float s = big ? 1.45f : 1f;
        var mat = t.first ? Metal(new Color(1f, 0.78f, 0.25f), 0.85f) : Metal(new Color(0.78f, 0.8f, 0.85f), 0.85f);
        var c = new GameObject(big ? "Season Final cup" : "Tournament cup").transform;
        c.SetParent(shelfRoot, false);
        c.localPosition = new Vector3(x, 0f, 0.12f) * k;
        Cube(c, "Plinth", new Vector3(0f, 0.018f * s, 0f), new Vector3(0.09f, 0.036f, 0.09f) * s, Dark);
        var cup = new GameObject("Cup");
        cup.transform.SetParent(c, false);
        cup.transform.localPosition = new Vector3(0f, 0.036f * s, 0f) * k;
        cup.transform.localScale = Vector3.one * s * k;
        cup.AddComponent<MeshFilter>().sharedMesh = CupMesh();
        cup.AddComponent<MeshRenderer>().sharedMaterial = mat;
        if (big)
            foreach (float side in new[] { -1f, 1f })
            {
                var h = new GameObject("Handle");
                h.transform.SetParent(c, false);
                h.transform.localPosition = new Vector3(side * 0.06f * s, (0.036f + 0.115f) * s, 0f) * k;
                h.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
                h.transform.localScale = Vector3.one * s * k;
                h.AddComponent<MeshFilter>().sharedMesh = Torus(0.028f, 0.006f, 0.5f, side);
                h.AddComponent<MeshRenderer>().sharedMaterial = mat;
            }
        // A tiny engraved tag on the plinth.
        Label(c, t.first ? "1ST" : "2ND", new Vector3(0f, 0.018f * s, 0.0462f * s), new Vector2(0.08f, 0.03f) * s, t.first ? new Color(1f, 0.85f, 0.4f) : new Color(0.85f, 0.87f, 0.9f));
    }

    void FollowerPlaque(float x, int followers)
    {
        Color plate = followers >= 1000 ? new Color(0.7f, 0.9f, 1f) : followers >= 500 ? new Color(1f, 0.78f, 0.25f) : new Color(0.8f, 0.82f, 0.86f);
        var p = new GameObject($"{followers} followers plaque").transform;
        p.SetParent(shelfRoot, false);
        p.localPosition = new Vector3(x, 0f, 0.09f) * k;
        p.localRotation = Quaternion.Euler(-8f, 0f, 0f);
        Cube(p, "Frame", new Vector3(0f, 0.12f, 0f), new Vector3(0.16f, 0.24f, 0.018f), Dark);
        Cube(p, "Plate", new Vector3(0f, 0.135f, 0.0105f), new Vector3(0.13f, 0.13f, 0.004f), Metal(plate, 0.9f));
        var play = new GameObject("Play");
        play.transform.SetParent(p, false);
        play.transform.localPosition = new Vector3(0f, 0.135f, 0.0135f) * k;
        play.transform.localScale = Vector3.one * k;
        play.AddComponent<MeshFilter>().sharedMesh = Triangle(0.03f);
        play.AddComponent<MeshRenderer>().sharedMaterial = Plain(new Color(0.75f, 0.31f, 1f));
        Label(p, $"<b>{followers:N0}</b>\n<size=70%>FOLLOWERS</size>", new Vector3(0f, 0.035f, 0.0095f), new Vector2(0.13f, 0.05f), Color.white);
    }

    // ================================================================== the trophy showcase

    class Slot
    {
        public GameObject root;
        public Material ghost;          // null for a trophy you've won
        public Renderer[] ghostRenderers;
        public Outline outline;
        public TextMeshPro caption;
        public Collider look;
        public float shown;
    }

    Transform tableAnchor, showcase;
    readonly List<Slot> slots = new List<Slot>();
    readonly Dictionary<Collider, Slot> slotByCollider = new Dictionary<Collider, Slot>();
    readonly RaycastHit[] lookHits = new RaycastHit[16];

    static readonly Color Walnut = new Color(0.3f, 0.19f, 0.12f), WalnutLight = new Color(0.38f, 0.25f, 0.16f);
    static readonly Color Lacquer = new Color(0.05f, 0.05f, 0.06f), Brass = new Color(0.85f, 0.66f, 0.32f);

    /// <summary>
    /// A walnut display console with a black lacquered top and two steps behind, lit by three little spotlights on the wall above.
    /// Front: 4 tournament cups and the Season Final cup in the middle. Lower step: rank emblems (Silver, Gold, Diamond, Champion).
    /// Upper step: follower plaques (100 / 500 / 1,000). What you've won stands there for real; an empty slot holds a see-through copy
    /// of its trophy that shows - with its outline and what it takes - only while you look right at it. Look at a trophy you've won
    /// and a little caption tells you what it's for.
    /// </summary>
    void BuildShowcase()
    {
        if (showcase) Destroy(showcase.gameObject);
        slots.Clear();
        slotByCollider.Clear();
        showcase = new GameObject("Trophy showcase").transform;
        showcase.SetParent(tableAnchor, false);
        // Local space: +z out of the wall into the room, x along it, y up from the floor. Sizes in real metres (times k).

        const float W = 1.8f, D = 0.5f, H = 0.8f;
        var lacquer = Gloss(Lacquer, 0.85f);
        var brass = Metal(Brass, 0.75f);
        var walnut = Plain(Walnut);
        var walnutLight = Plain(WalnutLight);
        // the console
        Cube(showcase, "Plinth", new Vector3(0f, 0.03f, D * 0.5f), new Vector3(W - 0.06f, 0.06f, D - 0.06f), lacquer);
        Cube(showcase, "Body", new Vector3(0f, 0.06f + (H - 0.06f) * 0.5f, D * 0.5f), new Vector3(W, H - 0.06f, D), walnut);
        for (int i = -1; i <= 1; i++)
        {
            Cube(showcase, "Door", new Vector3(i * 0.59f, 0.44f, D + 0.006f), new Vector3(0.55f, 0.62f, 0.012f), walnutLight);
            Cube(showcase, "Handle", new Vector3(i * 0.59f + (i == 1 ? -0.22f : 0.22f), 0.6f, D + 0.018f), new Vector3(0.012f, 0.1f, 0.012f), brass);
        }
        Cube(showcase, "Top", new Vector3(0f, H + 0.0175f, D * 0.5f + 0.01f), new Vector3(W + 0.04f, 0.035f, D + 0.04f), lacquer);
        Cube(showcase, "Top trim", new Vector3(0f, H + 0.0175f, D + 0.031f), new Vector3(W + 0.04f, 0.008f, 0.004f), brass);
        float top = H + 0.035f;
        Cube(showcase, "Step 1", new Vector3(0f, top + 0.065f, 0.11f), new Vector3(W - 0.08f, 0.13f, 0.2f), lacquer);
        Cube(showcase, "Step 1 trim", new Vector3(0f, top + 0.127f, 0.212f), new Vector3(W - 0.08f, 0.006f, 0.004f), brass);
        Cube(showcase, "Step 2", new Vector3(0f, top + 0.13f + 0.07f, 0.055f), new Vector3(W - 0.08f, 0.14f, 0.11f), lacquer);
        Cube(showcase, "Step 2 trim", new Vector3(0f, top + 0.267f, 0.1115f), new Vector3(W - 0.08f, 0.006f, 0.004f), brass);
        // Solid: you bump into it (one box for the console, one for the steps).
        var body = new GameObject("Collision");
        body.transform.SetParent(showcase, false);
        var c1 = body.AddComponent<BoxCollider>();
        c1.center = new Vector3(0f, top * 0.5f, D * 0.5f + 0.01f) * k;
        c1.size = new Vector3(W + 0.04f, top, D + 0.04f) * k;
        var c2 = body.AddComponent<BoxCollider>();
        c2.center = new Vector3(0f, top + 0.135f, 0.1f) * k;
        c2.size = new Vector3(W - 0.08f, 0.27f, 0.2f) * k;

        // the spotlights above
        float lampY = H + 1.0f;
        Cube(showcase, "Light rail", new Vector3(0f, lampY + 0.02f, 0.03f), new Vector3(W - 0.2f, 0.025f, 0.06f), Plain(new Color(0.08f, 0.08f, 0.09f)));
        foreach (float x in new[] { -0.6f, 0f, 0.6f })
        {
            var head = Cylinder(showcase, "Spot", new Vector3(x, lampY - 0.03f, 0.09f), new Vector3(0.05f, 0.045f, 0.05f), Quaternion.Euler(35f, 0f, 0f), Plain(new Color(0.08f, 0.08f, 0.09f)));
            Cylinder(showcase, "Spot lens", new Vector3(x, lampY - 0.072f, 0.12f), new Vector3(0.042f, 0.004f, 0.042f), Quaternion.Euler(35f, 0f, 0f), Glow(new Color(1f, 0.92f, 0.75f), 2.5f));
            var l = new GameObject("Spotlight").AddComponent<Light>();
            l.transform.SetParent(showcase, false);
            l.transform.localPosition = new Vector3(x, lampY - 0.08f, 0.13f) * k;
            l.transform.localRotation = Quaternion.LookRotation(new Vector3(0f, -(lampY - top - 0.1f), 0.26f));
            l.type = LightType.Spot;
            l.spotAngle = 62f;
            l.range = 1.6f * k;
            l.intensity = 2.2f;
            l.color = new Color(1f, 0.93f, 0.8f);
            l.shadows = LightShadows.None;
        }

        // ---- front row: tournament cups, the Season Final in the middle
        var cups = new List<Trophies.Trophy>();
        Trophies.Trophy? final = null;
        foreach (var t in Trophies.All)
        {
            if (t.kind == Trophies.Kind.FinalCup) { if (final == null || t.first) final = t; }
            else cups.Add(t);
        }
        while (cups.Count > 4) cups.RemoveAt(0); // the newest four
        float[] cupX = { -0.7f, -0.38f, 0.38f, 0.7f };
        for (int i = 0; i < 4; i++)
        {
            var pos = new Vector3(cupX[i], top, 0.36f);
            if (i < cups.Count) AddSlot(TournamentCup(pos, cups[i].first ? GoldColour : SilverColour), false, (cups[i].first ? "1ST PLACE" : "2ND PLACE") + "\n" + cups[i].label.ToUpperInvariant(), pos, 0.36f);
            else AddSlot(TournamentCup(pos, GoldColour), true, "WIN A TOURNAMENT", pos, 0.36f);
        }
        var finalPos = new Vector3(0f, top, 0.34f);
        if (final != null) AddSlot(FinalCup(finalPos, final.Value.first ? GoldColour : SilverColour), false, (final.Value.first ? "SEASON CHAMPION\n" : "SEASON RUNNER-UP\n") + final.Value.label.ToUpperInvariant(), finalPos, 0.58f);
        else AddSlot(FinalCup(finalPos, GoldColour), true, "WIN THE SEASON FINAL", finalPos, 0.58f);

        // ---- lower step: rank emblems
        var rank = Campaign.CurrentRank;
        Campaign.Rank[] ranks = { Campaign.Rank.Silver, Campaign.Rank.Gold, Campaign.Rank.Diamond, Campaign.Rank.Champion };
        float[] rankX = { -0.66f, -0.22f, 0.22f, 0.66f };
        for (int i = 0; i < 4; i++)
        {
            var pos = new Vector3(rankX[i], top + 0.13f, 0.155f);
            bool have = rank >= ranks[i];
            AddSlot(RankEmblem(pos, ranks[i]), !have, have ? $"RANKED {Campaign.RankName(ranks[i]).ToUpperInvariant()}" : $"REACH {Campaign.RankName(ranks[i]).ToUpperInvariant()}", pos, 0.2f);
        }
        // ---- upper step: follower plaques
        int[] follow = { 100, 500, 1000 };
        int f = ClipHub.Followers;
        for (int i = 0; i < 3; i++)
        {
            var pos = new Vector3((i - 1) * 0.5f, top + 0.27f, 0.055f);
            bool have = f >= follow[i];
            AddSlot(FollowerPlaque(pos, follow[i]), !have, have ? $"{follow[i]:N0} FOLLOWERS" : $"REACH {follow[i]:N0} FOLLOWERS", pos, 0.24f);
        }
    }

    static readonly Color GoldColour = new Color(1f, 0.77f, 0.3f), SilverColour = new Color(0.8f, 0.82f, 0.86f);

    /// <summary>Registers a slot: a box you can look at, a caption above it, and (for an empty one) the see-through look.</summary>
    void AddSlot(GameObject item, bool empty, string caption, Vector3 pos, float height)
    {
        var s = new Slot { root = item };
        if (empty)
        {
            s.ghost = GhostMaterial();
            s.ghostRenderers = item.GetComponentsInChildren<Renderer>();
            foreach (var r in s.ghostRenderers)
            {
                r.sharedMaterial = s.ghost;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            foreach (var t in item.GetComponentsInChildren<TextMeshPro>()) t.gameObject.SetActive(false);
            s.outline = item.AddComponent<Outline>();
            s.outline.OutlineMode = Outline.Mode.OutlineVisible;
            s.outline.OutlineColor = Color.white;
            s.outline.OutlineWidth = 0f;
            s.outline.enabled = false;
        }
        var look = new GameObject("Look at me");
        look.transform.SetParent(showcase, false);
        look.transform.localPosition = (pos + Vector3.up * height * 0.5f) * k;
        var box = look.AddComponent<BoxCollider>();
        box.isTrigger = true;
        box.size = new Vector3(0.26f, height + 0.04f, 0.22f) * k;
        s.look = box;
        slotByCollider[box] = s;

        var go = new GameObject("Caption");
        go.transform.SetParent(showcase, false);
        var c = go.AddComponent<TextMeshPro>();
        if (font) c.font = font;
        c.text = caption;
        c.color = new Color(0.13f, 0.12f, 0.16f, 0f);
        c.alignment = TextAlignmentOptions.Bottom;
        c.enableWordWrapping = false;
        c.enableAutoSizing = true;
        c.fontSizeMin = 0.05f;
        c.fontSizeMax = 0.6f;
        c.rectTransform.sizeDelta = new Vector2(0.5f, 0.09f) * k;
        go.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        go.transform.localPosition = (pos + Vector3.up * (height + 0.08f) + Vector3.forward * 0.04f) * k;
        s.caption = c;
        slots.Add(s);
        ApplySlot(s);
    }

    Material GhostMaterial()
    {
        var m = new Material(Shader.Find("Standard")) { name = "Trophy ghost", color = new Color(1f, 1f, 1f, 0f) };
        m.SetFloat("_Mode", 2f); // Fade
        m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        m.SetInt("_ZWrite", 0);
        m.DisableKeyword("_ALPHATEST_ON");
        m.EnableKeyword("_ALPHABLEND_ON");
        m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        m.SetFloat("_Glossiness", 0.6f);
        m.renderQueue = 3000;
        return m;
    }

    /// <summary>Whatever slot you're looking straight at fades in (its outline, or its caption); the rest fade out.</summary>
    void UpdateReveal()
    {
        if (!showcase || !tableAnchor.gameObject.activeInHierarchy) return;
        Slot target = null;
        var cam = Camera.main;
        var player = PlayerInteraction.Instance;
        if (cam && player && !VRHeadset.Worn)
        {
            int n = Physics.RaycastNonAlloc(cam.transform.position, cam.transform.forward, lookHits, 3.2f * k, ~0, QueryTriggerInteraction.Collide);
            float best = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                var h = lookHits[i];
                if (h.collider.transform.IsChildOf(player.transform)) continue;
                if (h.distance >= best) continue;
                if (slotByCollider.TryGetValue(h.collider, out var s)) { best = h.distance; target = s; }
                else if (!h.collider.isTrigger && !h.collider.transform.IsChildOf(showcase)) { best = h.distance; target = null; } // something solid in the way
            }
        }
        foreach (var s in slots)
        {
            float before = s.shown;
            s.shown = Mathf.MoveTowards(s.shown, s == target ? 1f : 0f, Time.deltaTime * 4f);
            if (!Mathf.Approximately(before, s.shown)) ApplySlot(s);
        }
    }

    static void ApplySlot(Slot s)
    {
        float e = s.shown * s.shown * (3f - 2f * s.shown);
        // Not won yet: always there as a faint frosted-glass copy (so you can see what's missing); looking at it brightens it,
        // draws its outline and says what it takes.
        if (s.ghost) s.ghost.color = new Color(0.72f, 0.78f, 0.9f, 0.2f + 0.3f * e);
        if (s.outline)
        {
            s.outline.enabled = e > 0.02f;
            s.outline.OutlineWidth = 5f * e;
            s.outline.OutlineColor = new Color(1f, 1f, 1f, e);
        }
        if (s.caption) s.caption.color = new Color(0.13f, 0.12f, 0.16f, e);
    }

    // ------------------------------------------------------------------ the trophies themselves

    /// <summary>A classic two-handled cup on a stepped black base with a brass plate (about 26 cm).</summary>
    GameObject TournamentCup(Vector3 pos, Color colour)
    {
        var c = new GameObject("Tournament cup").transform;
        c.SetParent(showcase, false);
        c.localPosition = pos * k;
        var metal = Metal(colour, 0.82f);
        var lacquer = Gloss(Lacquer, 0.85f);
        Cube(c, "Base", new Vector3(0f, 0.014f, 0f), new Vector3(0.11f, 0.028f, 0.11f), lacquer);
        Cube(c, "Base top", new Vector3(0f, 0.043f, 0f), new Vector3(0.085f, 0.03f, 0.085f), lacquer);
        Cube(c, "Plate", new Vector3(0f, 0.043f, 0.0435f), new Vector3(0.06f, 0.018f, 0.003f), Metal(Brass, 0.75f));
        Mesh(c, "Cup", CupMesh(), new Vector3(0f, 0.058f, 0f), 1f, metal);
        foreach (float side in new[] { -1f, 1f })
            Mesh(c, "Handle", Torus(0.028f, 0.0055f, 0.5f, side), new Vector3(side * 0.046f, 0.058f + 0.1f, 0f), 1f, metal);
        return c.gameObject;
    }

    /// <summary>The Season Final cup: tall, with a lid and a star on top, big handles, on a round black base with a gold band (about 45 cm).</summary>
    GameObject FinalCup(Vector3 pos, Color colour)
    {
        var c = new GameObject("Season Final cup").transform;
        c.SetParent(showcase, false);
        c.localPosition = pos * k;
        var metal = Metal(colour, 0.85f);
        var lacquer = Gloss(Lacquer, 0.85f);
        Cylinder(c, "Base", new Vector3(0f, 0.025f, 0f), new Vector3(0.15f, 0.025f, 0.15f), Quaternion.identity, lacquer);
        Cylinder(c, "Band", new Vector3(0f, 0.035f, 0f), new Vector3(0.152f, 0.005f, 0.152f), Quaternion.identity, Metal(Brass, 0.75f));
        Cylinder(c, "Base top", new Vector3(0f, 0.065f, 0f), new Vector3(0.11f, 0.015f, 0.11f), Quaternion.identity, lacquer);
        Mesh(c, "Cup", FinalCupMesh(), new Vector3(0f, 0.08f, 0f), 1f, metal);
        foreach (float side in new[] { -1f, 1f })
            Mesh(c, "Handle", Torus(0.05f, 0.008f, 0.5f, side), new Vector3(side * 0.072f, 0.08f + 0.2f, 0f), 1f, metal);
        // the star on the lid
        for (int i = 0; i < 2; i++)
        {
            var arm = Cube(c, "Star", new Vector3(0f, 0.08f + 0.345f, 0f), new Vector3(0.05f, 0.05f, 0.012f), metal);
            arm.localRotation = Quaternion.Euler(0f, 0f, 45f * i);
        }
        return c.gameObject;
    }

    /// <summary>A rank emblem: a metal shield in the rank's colour with its initial, on a small stand.</summary>
    GameObject RankEmblem(Vector3 pos, Campaign.Rank rank)
    {
        var e = new GameObject("Rank emblem").transform;
        e.SetParent(showcase, false);
        e.localPosition = pos * k;
        Color col = Campaign.RankColour(rank);
        Cube(e, "Stand", new Vector3(0f, 0.008f, 0f), new Vector3(0.08f, 0.016f, 0.05f), Gloss(Lacquer, 0.85f));
        var shield = new GameObject("Shield").transform;
        shield.SetParent(e, false);
        shield.localPosition = new Vector3(0f, 0.016f, 0f) * k;
        shield.localRotation = Quaternion.Euler(-6f, 0f, 0f);
        Mesh(shield, "Outer", Shield(0.07f, 0.1f, 0.014f), new Vector3(0f, 0.05f, 0f), 1f, Metal(col, 0.8f));
        Mesh(shield, "Inner", Shield(0.052f, 0.075f, 0.006f), new Vector3(0f, 0.056f, 0.0125f), 1f, Metal(col * 0.72f, 0.7f));
        Label(shield, $"<b>{Campaign.RankName(rank).Substring(0, 1)}</b>", new Vector3(0f, 0.05f, 0.0195f), new Vector2(0.05f, 0.05f), Color.white);
        return e.gameObject;
    }

    /// <summary>A framed follower award: a metal plate with a play button, in a walnut frame on a little easel.</summary>
    GameObject FollowerPlaque(Vector3 pos, int followers)
    {
        Color plate = followers >= 1000 ? new Color(0.72f, 0.9f, 1f) : followers >= 500 ? GoldColour : SilverColour;
        var p = new GameObject($"{followers} followers plaque").transform;
        p.SetParent(showcase, false);
        p.localPosition = pos * k;
        p.localRotation = Quaternion.Euler(-10f, 0f, 0f);
        Cube(p, "Frame", new Vector3(0f, 0.11f, 0f), new Vector3(0.17f, 0.22f, 0.02f), Plain(Walnut));
        Cube(p, "Backing", new Vector3(0f, 0.11f, 0.0105f), new Vector3(0.146f, 0.196f, 0.003f), Plain(new Color(0.08f, 0.08f, 0.1f)));
        Cube(p, "Plate", new Vector3(0f, 0.125f, 0.0135f), new Vector3(0.11f, 0.11f, 0.004f), Metal(plate, 0.9f));
        var play = Mesh(p, "Play", Triangle(0.03f), new Vector3(0.004f, 0.125f, 0.0158f), 1f, Plain(new Color(0.75f, 0.31f, 1f)));
        Label(p, $"<b>{followers:N0}</b>", new Vector3(0f, 0.04f, 0.0125f), new Vector2(0.12f, 0.03f), Color.white);
        return p.gameObject;
    }

    Transform Mesh(Transform parent, string name, Mesh mesh, Vector3 pos, float scale, Material m)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos * k;
        go.transform.localScale = Vector3.one * scale * k;
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = m;
        return go.transform;
    }

    static Material Gloss(Color c, float smooth) => Cached("gloss" + c + smooth, () =>
    {
        var m = new Material(Shader.Find("Standard")) { color = c };
        m.SetFloat("_Glossiness", smooth);
        return m;
    });

    static Mesh finalCupMesh;

    /// <summary>The Season Final cup body: knotted stem, a tall bowl, a lid with a dome and a finial (about 37 cm).</summary>
    static Mesh FinalCupMesh()
    {
        if (finalCupMesh) return finalCupMesh;
        Vector2[] profile =
        {
            new Vector2(0f, 0f), new Vector2(0.045f, 0f), new Vector2(0.045f, 0.01f), new Vector2(0.02f, 0.022f), new Vector2(0.011f, 0.05f),
            new Vector2(0.022f, 0.062f), new Vector2(0.022f, 0.072f), new Vector2(0.011f, 0.084f), new Vector2(0.012f, 0.11f), new Vector2(0.03f, 0.125f),
            new Vector2(0.058f, 0.16f), new Vector2(0.07f, 0.21f), new Vector2(0.074f, 0.26f), new Vector2(0.08f, 0.268f), new Vector2(0.08f, 0.276f),
            new Vector2(0.068f, 0.28f), new Vector2(0.055f, 0.3f), new Vector2(0.032f, 0.318f), new Vector2(0.012f, 0.326f), new Vector2(0.014f, 0.338f),
            new Vector2(0f, 0.34f),
        };
        finalCupMesh = Lathe(profile, 32);
        return finalCupMesh;
    }

    static readonly Dictionary<string, Mesh> shields = new Dictionary<string, Mesh>();

    /// <summary>A shield shape (flat top, curved sides to a point), extruded, facing +z, centred on its middle.</summary>
    static Mesh Shield(float halfWidth, float height, float depth)
    {
        string key = halfWidth + "|" + height + "|" + depth;
        if (shields.TryGetValue(key, out var cached) && cached) return cached;
        var outline = new List<Vector2>();
        float top = height * 0.5f;
        outline.Add(new Vector2(-halfWidth, top));
        outline.Add(new Vector2(halfWidth, top));
        for (int i = 1; i <= 8; i++)
        {
            float t = i / 8f;
            outline.Add(new Vector2(halfWidth * Mathf.Cos(t * Mathf.PI * 0.5f) * (1f - 0.15f * t), top - height * (0.35f + 0.65f * Mathf.Sin(t * Mathf.PI * 0.5f))));
        }
        for (int i = 7; i >= 1; i--)
        {
            var p = outline[2 + i - 1];
            outline.Add(new Vector2(-p.x, p.y));
        }
        var v = new List<Vector3>();
        var tris = new List<int>();
        int n = outline.Count;
        v.Add(new Vector3(0f, 0f, depth));
        foreach (var p in outline) v.Add(new Vector3(p.x, p.y, depth));
        for (int i = 0; i < n; i++) tris.AddRange(new[] { 0, 1 + (i + 1) % n, 1 + i });
        for (int i = 0; i < n; i++)
        {
            int b = v.Count;
            Vector2 a = outline[i], c = outline[(i + 1) % n];
            v.Add(new Vector3(a.x, a.y, depth)); v.Add(new Vector3(c.x, c.y, depth)); v.Add(new Vector3(a.x, a.y, 0f)); v.Add(new Vector3(c.x, c.y, 0f));
            tris.AddRange(new[] { b, b + 1, b + 2, b + 1, b + 3, b + 2 });
        }
        var m = new Mesh { name = "Shield" };
        m.SetVertices(v);
        m.SetTriangles(tris, 0);
        m.RecalculateNormals();
        m.RecalculateBounds();
        shields[key] = m;
        return m;
    }

    // ================================================================== things you've earned

    /// <summary>Shows / hides every RoomUnlock piece (your own desk gear, the placed furniture, the anchors below).</summary>
    void ApplyUnlocks()
    {
        foreach (var u in unlocks)
        {
            if (!u) continue;
            try { u.Show(RoomUnlock.Has(u.key)); }
            catch (System.Exception e) { Debug.LogException(e); }
        }
        if (pc)
        {
            try { pc.RefreshMonitor(); } // the desk's monitor may have been swapped (setup upgrades)
            catch (System.Exception e) { Debug.LogException(e); }
        }
    }

    Transform AnchorNamed(string name)
    {
        foreach (var u in unlocks) if (u && u.name == name) return u.transform;
        return null;
    }

    /// <summary>The pieces the kits don't have, built once under their anchors (the anchors come and go with their unlock).</summary>
    void BuildExtras()
    {
        Build("Neon sign anchor", NeonSign);
        Build("Acoustic panels anchor", AcousticPanels);
        Build("Hex lights anchor", HexLights);
        Build("Trophy stand anchor", TrophyStand);
    }

    /// <summary>Builds under an anchor while it's switched on (text can't set itself up on a hidden object), then puts it back.</summary>
    void Build(string anchorName, System.Action<Transform> build)
    {
        var a = AnchorNamed(anchorName);
        if (!a) return;
        bool was = a.gameObject.activeSelf;
        a.gameObject.SetActive(true);
        try { build(a); }
        catch (System.Exception e) { Debug.LogException(e); }
        a.gameObject.SetActive(was);
    }

    /// <summary>Your gamer tag in pink neon on a dark acrylic board, with a cyan underline and a soft pink glow on the wall.</summary>
    void NeonSign(Transform a)
    {
        var s = new GameObject("Neon sign").transform;
        s.SetParent(a, false);
        Cube(s, "Board", new Vector3(0f, 0f, 0.012f), new Vector3(1.15f, 0.34f, 0.018f), Plain(new Color(0.06f, 0.05f, 0.09f)));
        foreach (float y in new[] { -0.155f, 0.155f }) Cube(s, "Edge", new Vector3(0f, y, 0.023f), new Vector3(1.15f, 0.012f, 0.004f), Glow(new Color(0.55f, 0.25f, 1f), 1.2f));
        var go = new GameObject("Tag");
        go.transform.SetParent(s, false);
        var t = go.AddComponent<TextMeshPro>();
        if (font) t.font = font;
        t.text = ClipHub.ChannelName.ToUpperInvariant();
        t.color = new Color(1f, 0.45f, 0.82f);
        t.fontStyle = FontStyles.Bold;
        t.alignment = TextAlignmentOptions.Center;
        t.enableWordWrapping = false;
        t.enableAutoSizing = true;
        t.fontSizeMin = 0.1f;
        t.fontSizeMax = 3f;
        t.rectTransform.sizeDelta = new Vector2(1.02f, 0.2f) * k;
        go.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        go.transform.localPosition = new Vector3(0f, 0.025f, 0.023f) * k;
        Cube(s, "Underline", new Vector3(0f, -0.095f, 0.026f), new Vector3(0.8f, 0.014f, 0.01f), Glow(new Color(0.3f, 0.95f, 1f), 2.4f));
        var light = new GameObject("Neon glow").AddComponent<Light>();
        light.transform.SetParent(s, false);
        light.transform.localPosition = new Vector3(0f, 0f, 0.35f) * k;
        light.type = LightType.Point;
        light.color = new Color(1f, 0.4f, 0.8f);
        light.intensity = 0.8f;
        light.range = 2.2f * k;
        light.shadows = LightShadows.None;
    }

    /// <summary>A 3 x 2 block of acoustic foam squares (charcoal and purple, each with raised wedges).</summary>
    void AcousticPanels(Transform a)
    {
        var s = new GameObject("Acoustic panels").transform;
        s.SetParent(a, false);
        var charcoal = Plain(new Color(0.12f, 0.12f, 0.15f));
        var purple = Plain(new Color(0.32f, 0.2f, 0.5f));
        const float size = 0.3f, gap = 0.02f;
        for (int x = 0; x < 3; x++)
        for (int y = 0; y < 2; y++)
        {
            bool even = (x + y) % 2 == 0;
            var m = even ? charcoal : purple;
            float cx = (x - 1f) * (size + gap), cy = (y - 0.5f) * (size + gap);
            Cube(s, "Panel", new Vector3(cx, cy, 0.015f), new Vector3(size, size, 0.03f), m);
            // Three ridges, turned the other way on every other tile (like real foam).
            for (int r = 0; r < 3; r++)
            {
                float o = (r - 1f) * size * 0.31f;
                var pos = even ? new Vector3(cx + o, cy, 0.04f) : new Vector3(cx, cy + o, 0.04f);
                var sz = even ? new Vector3(size * 0.16f, size * 0.92f, 0.022f) : new Vector3(size * 0.92f, size * 0.16f, 0.022f);
                Cube(s, "Ridge", pos, sz, m);
            }
        }
    }

    /// <summary>Seven hexagon light panels in a honeycomb, slowly cycling through the rainbow.</summary>
    void HexLights(Transform a)
    {
        var s = new GameObject("Hex lights").transform;
        s.SetParent(a, false);
        const float r = 0.13f;
        float w = Mathf.Sqrt(3f) * r;
        Vector2[] cells = { Vector2.zero, new Vector2(w, 0f), new Vector2(-w, 0f), new Vector2(w * 0.5f, r * 1.5f), new Vector2(-w * 0.5f, r * 1.5f), new Vector2(w * 0.5f, -r * 1.5f), new Vector2(w * 1.5f, r * 1.5f) };
        var parts = new List<Renderer>();
        var frame = Plain(new Color(0.1f, 0.1f, 0.12f));
        foreach (var c in cells)
        {
            var back = new GameObject("Hex frame");
            back.transform.SetParent(s, false);
            back.transform.localPosition = new Vector3(c.x, c.y, 0.002f) * k;
            back.transform.localScale = Vector3.one * k;
            back.AddComponent<MeshFilter>().sharedMesh = Hex(r * 0.97f, 0.016f);
            back.AddComponent<MeshRenderer>().sharedMaterial = frame;
            var face = new GameObject("Hex light");
            face.transform.SetParent(s, false);
            face.transform.localPosition = new Vector3(c.x, c.y, 0.018f) * k;
            face.transform.localScale = Vector3.one * k;
            face.AddComponent<MeshFilter>().sharedMesh = Hex(r * 0.8f, 0.008f);
            var mr = face.AddComponent<MeshRenderer>();
            mr.sharedMaterial = Glow(Color.white, 1f);
            parts.Add(mr);
        }
        var cycle = s.gameObject.AddComponent<NeonCycle>();
        cycle.parts = parts.ToArray();
        cycle.speed = 0.06f;
        cycle.brightness = 1.8f;
    }

    /// <summary>The Season Final trophy on a lit stand.</summary>
    void TrophyStand(Transform a)
    {
        var s = new GameObject("Trophy stand").transform;
        s.SetParent(a, false);
        var marble = Plain(new Color(0.9f, 0.9f, 0.93f));
        Cube(s, "Base", new Vector3(0f, 0.03f, 0f), new Vector3(0.42f, 0.06f, 0.42f), Dark);
        Cube(s, "Column", new Vector3(0f, 0.5f, 0f), new Vector3(0.3f, 0.88f, 0.3f), marble);
        Cube(s, "Top", new Vector3(0f, 0.965f, 0f), new Vector3(0.4f, 0.05f, 0.4f), Dark);
        Cube(s, "Glow strip", new Vector3(0f, 0.92f, 0.152f), new Vector3(0.3f, 0.012f, 0.004f), Glow(new Color(1f, 0.8f, 0.3f), 2.2f));
        bool gold = false;
        foreach (var t in Trophies.All) if (t.kind == Trophies.Kind.FinalCup && t.first) gold = true;
        var mat = gold ? Metal(new Color(1f, 0.78f, 0.25f), 0.85f) : Metal(new Color(0.78f, 0.8f, 0.85f), 0.85f);
        const float big = 2.4f;
        var cup = new GameObject("Season Final cup");
        cup.transform.SetParent(s, false);
        cup.transform.localPosition = new Vector3(0f, 0.99f, 0f) * k;
        cup.transform.localScale = Vector3.one * big * k;
        cup.AddComponent<MeshFilter>().sharedMesh = CupMesh();
        cup.AddComponent<MeshRenderer>().sharedMaterial = mat;
        foreach (float side in new[] { -1f, 1f })
        {
            var h = new GameObject("Handle");
            h.transform.SetParent(s, false);
            h.transform.localPosition = new Vector3(side * 0.06f * big, 0.99f + 0.115f * big, 0f) * k;
            h.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
            h.transform.localScale = Vector3.one * big * k;
            h.AddComponent<MeshFilter>().sharedMesh = Torus(0.028f, 0.006f, 0.5f, side);
            h.AddComponent<MeshRenderer>().sharedMaterial = mat;
        }
        Label(s, "SEASON FINAL\n<size=70%>" + (gold ? "CHAMPION" : "RUNNER-UP") + "</size>", new Vector3(0f, 0.62f, 0.152f), new Vector2(0.26f, 0.12f), new Color(0.15f, 0.12f, 0.1f));
        var light = new GameObject("Trophy light").AddComponent<Light>();
        light.transform.SetParent(s, false);
        light.transform.localPosition = new Vector3(0f, 1.75f, 0.25f) * k;
        light.type = LightType.Point;
        light.color = new Color(1f, 0.92f, 0.75f);
        light.intensity = 0.9f;
        light.range = 1.6f * k;
        light.shadows = LightShadows.None;
    }

    static readonly Dictionary<string, Mesh> hexMeshes = new Dictionary<string, Mesh>();

    /// <summary>A flat hexagonal prism, its face towards +z.</summary>
    static Mesh Hex(float radius, float depth)
    {
        string key = radius + "|" + depth;
        if (hexMeshes.TryGetValue(key, out var cached) && cached) return cached;
        var v = new List<Vector3> { new Vector3(0f, 0f, depth) };
        var tris = new List<int>();
        for (int i = 0; i < 6; i++)
        {
            float ang = (90f + i * 60f) * Mathf.Deg2Rad;
            v.Add(new Vector3(Mathf.Cos(ang) * radius, Mathf.Sin(ang) * radius, depth));
        }
        for (int i = 0; i < 6; i++) tris.AddRange(new[] { 0, 1 + i, 1 + (i + 1) % 6 });
        for (int i = 0; i < 6; i++)
        {
            int b = v.Count;
            Vector3 p0 = v[1 + i], p1 = v[1 + (i + 1) % 6];
            v.Add(p0); v.Add(p1); v.Add(new Vector3(p0.x, p0.y, 0f)); v.Add(new Vector3(p1.x, p1.y, 0f));
            tris.AddRange(new[] { b, b + 2, b + 1, b + 1, b + 2, b + 3 });
        }
        var m = new Mesh { name = "Hex" };
        m.SetVertices(v);
        m.SetTriangles(tris, 0);
        m.RecalculateNormals();
        m.RecalculateBounds();
        hexMeshes[key] = m;
        return m;
    }

    // ================================================================== building blocks

    static float Along(Vector3 axis, Vector3 extents) => Mathf.Abs(axis.x) * extents.x + Mathf.Abs(axis.y) * extents.y + Mathf.Abs(axis.z) * extents.z;

    static Bounds Bounds(GameObject go)
    {
        var rs = go.GetComponentsInChildren<MeshRenderer>();
        if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
        var b = rs[0].bounds;
        foreach (var r in rs) b.Encapsulate(r.bounds);
        return b;
    }

    /// <summary>A box; position and size in real metres (times k), relative to the parent.</summary>
    Transform Cube(Transform parent, string name, Vector3 pos, Vector3 size, Material m)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Destroy(go.GetComponent<Collider>());
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos * k;
        go.transform.localScale = size * k;
        go.GetComponent<Renderer>().sharedMaterial = m;
        return go.transform;
    }

    /// <summary>A cylinder: 'size' is (diameter, half height, diameter) like Unity's primitive.</summary>
    Transform Cylinder(Transform parent, string name, Vector3 pos, Vector3 size, Quaternion rot, Material m)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Destroy(go.GetComponent<Collider>());
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos * k;
        go.transform.localRotation = rot;
        go.transform.localScale = size * k;
        go.GetComponent<Renderer>().sharedMaterial = m;
        return go.transform;
    }

    void Label(Transform parent, string text, Vector3 pos, Vector2 box, Color colour)
    {
        var go = new GameObject("Label");
        go.transform.SetParent(parent, false);
        var t = go.AddComponent<TextMeshPro>();
        if (font) t.font = font;
        t.text = text;
        t.color = colour;
        t.alignment = TextAlignmentOptions.Center;
        t.enableWordWrapping = false;
        t.enableAutoSizing = true;
        t.fontSizeMin = 0.05f;
        t.fontSizeMax = 2f;
        t.rectTransform.sizeDelta = box * k;
        // TextMeshPro faces -z; turn it to face out of the wall (+z).
        go.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        go.transform.localPosition = pos * k;
    }

    static readonly Dictionary<string, Material> mats = new Dictionary<string, Material>();

    static Material Plain(Color c) => Cached("plain" + c, () => { var m = new Material(Shader.Find("Standard")) { color = c }; m.SetFloat("_Glossiness", 0.25f); return m; });
    static Material Metal(Color c, float smooth) => Cached("metal" + c + smooth, () =>
    {
        var m = new Material(Shader.Find("Standard")) { color = c };
        m.SetFloat("_Metallic", 0.85f);
        m.SetFloat("_Glossiness", smooth);
        return m;
    });
    static Material Glow(Color c, float strength) => Cached("glow" + c + strength, () =>
    {
        var m = new Material(Shader.Find("Standard")) { color = c };
        m.EnableKeyword("_EMISSION");
        m.SetColor("_EmissionColor", c * strength);
        return m;
    });
    static Material Wood => Plain(new Color(0.55f, 0.38f, 0.24f));
    static Material Dark => Plain(new Color(0.16f, 0.13f, 0.12f));

    static Material Cached(string key, System.Func<Material> make)
    {
        if (!mats.TryGetValue(key, out var m) || !m) mats[key] = m = make();
        return m;
    }

    static Mesh cupMesh;

    /// <summary>A trophy cup (lathe): foot, stem, bowl with a lip. About 0.15 m tall.</summary>
    static Mesh CupMesh()
    {
        if (cupMesh) return cupMesh;
        Vector2[] profile =
        {
            new Vector2(0f, 0f), new Vector2(0.034f, 0f), new Vector2(0.034f, 0.008f), new Vector2(0.014f, 0.016f), new Vector2(0.008f, 0.03f),
            new Vector2(0.008f, 0.055f), new Vector2(0.016f, 0.062f), new Vector2(0.036f, 0.08f), new Vector2(0.046f, 0.11f), new Vector2(0.05f, 0.145f),
            new Vector2(0.054f, 0.15f), new Vector2(0.046f, 0.15f), new Vector2(0.042f, 0.12f), new Vector2(0.02f, 0.09f), new Vector2(0f, 0.085f),
        };
        cupMesh = Lathe(profile, 28);
        return cupMesh;
    }

    static Mesh Lathe(Vector2[] profile, int segments)
    {
        var verts = new List<Vector3>();
        var tris = new List<int>();
        for (int s = 0; s <= segments; s++)
        {
            float a = s / (float)segments * Mathf.PI * 2f;
            float cs = Mathf.Cos(a), sn = Mathf.Sin(a);
            foreach (var p in profile) verts.Add(new Vector3(p.x * cs, p.y, p.x * sn));
        }
        int n = profile.Length;
        for (int s = 0; s < segments; s++)
        for (int i = 0; i < n - 1; i++)
        {
            int a = s * n + i, b = (s + 1) * n + i;
            tris.AddRange(new[] { a, a + 1, b, b, a + 1, b + 1 });
        }
        var m = new Mesh { name = "Lathe" };
        m.SetVertices(verts);
        m.SetTriangles(tris, 0);
        m.RecalculateNormals();
        m.RecalculateBounds();
        return m;
    }

    /// <summary>A torus in the XY plane (facing +z). 'portion' of the full circle (0.5 = a half ring for a cup handle), opening on 'side'.</summary>
    static Mesh Torus(float radius, float tube, float portion, float side)
    {
        const int seg = 32, ring = 10;
        var verts = new List<Vector3>();
        var tris = new List<int>();
        int segs = Mathf.Max(4, Mathf.RoundToInt(seg * portion));
        float start = portion < 1f ? (side > 0 ? -Mathf.PI * 0.5f : Mathf.PI * 0.5f) : 0f;
        for (int i = 0; i <= segs; i++)
        {
            float u = start + i / (float)segs * Mathf.PI * 2f * portion;
            Vector3 centre = new Vector3(Mathf.Cos(u), Mathf.Sin(u), 0f) * radius;
            for (int j = 0; j <= ring; j++)
            {
                float v = j / (float)ring * Mathf.PI * 2f;
                Vector3 dir = new Vector3(Mathf.Cos(u), Mathf.Sin(u), 0f) * Mathf.Cos(v) + Vector3.forward * Mathf.Sin(v);
                verts.Add(centre + dir * tube);
            }
        }
        for (int i = 0; i < segs; i++)
        for (int j = 0; j < ring; j++)
        {
            int a = i * (ring + 1) + j, b = (i + 1) * (ring + 1) + j;
            tris.AddRange(new[] { a, b, a + 1, a + 1, b, b + 1 });
        }
        var m = new Mesh { name = "Torus" };
        m.SetVertices(verts);
        m.SetTriangles(tris, 0);
        m.RecalculateNormals();
        m.RecalculateBounds();
        return m;
    }

    /// <summary>A "play" triangle facing +z.</summary>
    static Mesh Triangle(float size)
    {
        var m = new Mesh { name = "Play" };
        m.SetVertices(new List<Vector3> { new Vector3(-size * 0.5f, -size * 0.6f, 0f), new Vector3(-size * 0.5f, size * 0.6f, 0f), new Vector3(size * 0.7f, 0f, 0f) });
        m.SetTriangles(new[] { 0, 2, 1, 0, 1, 2 }, 0);
        m.RecalculateNormals();
        return m;
    }
}
