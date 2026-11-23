using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Cooking dinner, for real (replaces the old one-button oven). Once a day, in the afternoon, Mom texts which recipe it is; a recipe
/// card shows what's left. You fetch every ingredient from where it's kept (fridge, freezer, pantry), defrost frozen meat in the
/// microwave, chop things on the cutting board (a top-down cutting game), get the right pot / pan / bowl from the cookware cupboard,
/// fill the pot at the sink, put it on the stove, add everything, season it, cook it and take it off at the right moment.
/// Leave it on too long and it burns - and the kitchen catches fire (the oven's fire hazard).
///
/// Recipes: beef &amp; potato stew, garden salad, one-pot spaghetti bolognese, veggie omelette (see <see cref="Recipes"/>).
/// How well you chop and when you take it off decide the quality: Mom adds points for a great dinner.
/// Added by the GameManager; it finds the kitchen's stations by name (see the kitchen tool, HouseBuilder.BuildBiggerKitchen).
/// </summary>
public class Cooking : TaskBase, IPromptProvider
{
    public static Cooking Instance { get; private set; }

    // ------------------------------------------------------------------ recipes

    public enum Store { Fridge, Freezer, Pantry }
    public enum Vessel { Pot, Pan, Bowl }

    public class Ingredient
    {
        public string id, name, model;
        public Store store;
        public bool frozen;
        public int cuts;            // 0 = goes in whole
        public Color colour;
        public float size;          // biggest side in metres
        public string shape;        // for the cutting board: "long", "round", "slab"
    }

    public class Recipe
    {
        public string name, momText;
        public Vessel vessel;
        public string[] ingredients;
        public string seasoning;    // "salt" or "oil"
        public float cookSeconds;   // 0 = no stove (salad: tossed at the island)
        public string dish;         // the model the finished dish shows as
    }

    public static readonly Dictionary<string, Ingredient> Ingredients = new Dictionary<string, Ingredient>
    {
        { "beef", new Ingredient { id = "beef", name = "beef", model = "meat-raw", store = Store.Freezer, frozen = true, cuts = 4, colour = new Color(0.75f, 0.25f, 0.25f), size = 0.2f, shape = "slab" } },
        // more in the freezer (not in tonight's recipes, but you can take them - the freezer isn't just the two things you need)
        { "fish", new Ingredient { id = "fish", name = "fish", model = "fish", store = Store.Freezer, frozen = true, cuts = 3, colour = new Color(0.6f, 0.66f, 0.72f), size = 0.2f, shape = "long" } },
        { "sausages", new Ingredient { id = "sausages", name = "sausages", model = "meat-sausage", store = Store.Freezer, frozen = true, cuts = 0, colour = new Color(0.76f, 0.42f, 0.36f), size = 0.14f, shape = "long" } },
        { "bacon", new Ingredient { id = "bacon", name = "bacon", model = "bacon-raw", store = Store.Freezer, frozen = true, cuts = 0, colour = new Color(0.88f, 0.5f, 0.48f), size = 0.16f, shape = "slab" } },
        { "mince", new Ingredient { id = "mince", name = "mince", model = "meat-patty", store = Store.Freezer, frozen = true, cuts = 0, colour = new Color(0.7f, 0.3f, 0.3f), size = 0.13f, shape = "slab" } },
        { "potato", new Ingredient { id = "potato", name = "potato", model = null, store = Store.Pantry, cuts = 4, colour = new Color(0.72f, 0.55f, 0.33f), size = 0.11f, shape = "round" } },
        { "carrot", new Ingredient { id = "carrot", name = "carrot", model = "carrot", store = Store.Fridge, cuts = 5, colour = new Color(0.95f, 0.5f, 0.12f), size = 0.2f, shape = "long" } },
        { "onion", new Ingredient { id = "onion", name = "onion", model = "onion", store = Store.Pantry, cuts = 3, colour = new Color(0.85f, 0.7f, 0.5f), size = 0.09f, shape = "round" } },
        { "tomato", new Ingredient { id = "tomato", name = "tomato", model = "tomato", store = Store.Fridge, cuts = 3, colour = new Color(0.88f, 0.2f, 0.15f), size = 0.08f, shape = "round" } },
        { "lettuce", new Ingredient { id = "lettuce", name = "lettuce", model = "cabbage", store = Store.Fridge, cuts = 4, colour = new Color(0.45f, 0.75f, 0.3f), size = 0.18f, shape = "round" } },
        { "cucumber", new Ingredient { id = "cucumber", name = "cucumber", model = null, store = Store.Fridge, cuts = 6, colour = new Color(0.22f, 0.5f, 0.2f), size = 0.22f, shape = "long" } },
        { "pepper", new Ingredient { id = "pepper", name = "pepper", model = "paprika", store = Store.Fridge, cuts = 3, colour = new Color(0.9f, 0.25f, 0.15f), size = 0.1f, shape = "round" } },
        { "mushroom", new Ingredient { id = "mushroom", name = "mushrooms", model = "mushroom", store = Store.Fridge, cuts = 3, colour = new Color(0.85f, 0.78f, 0.68f), size = 0.08f, shape = "round" } },
        { "cheese", new Ingredient { id = "cheese", name = "cheese", model = "cheese", store = Store.Fridge, cuts = 3, colour = new Color(0.98f, 0.82f, 0.3f), size = 0.14f, shape = "slab" } },
        { "egg", new Ingredient { id = "egg", name = "eggs", model = "egg", store = Store.Fridge, cuts = 0, colour = new Color(0.97f, 0.93f, 0.85f), size = 0.06f, shape = "round" } },
        { "pasta", new Ingredient { id = "pasta", name = "spaghetti", model = null, store = Store.Pantry, cuts = 0, colour = new Color(0.95f, 0.85f, 0.55f), size = 0.22f, shape = "long" } },
    };

    public static readonly Recipe[] Recipes =
    {
        new Recipe { name = "Beef & potato stew", vessel = Vessel.Pot, ingredients = new[] { "beef", "potato", "carrot", "onion" }, seasoning = "salt", cookSeconds = 40f, dish = "bowl-broth",
            momText = "Tonight is beef & potato stew! Beef's in the freezer (defrost it), potatoes and onions in the pantry, carrots in the fridge. Love you x" },
        new Recipe { name = "Garden salad", vessel = Vessel.Bowl, ingredients = new[] { "lettuce", "cucumber", "tomato" }, seasoning = "oil", cookSeconds = 0f, dish = "salad",
            momText = "Light dinner tonight: a garden salad. Lettuce, cucumber and tomato are in the fridge, oil's in the pantry. Use the big bowl x" },
        new Recipe { name = "Spaghetti bolognese", vessel = Vessel.Pot, ingredients = new[] { "pasta", "mince", "onion", "tomato" }, seasoning = "salt", cookSeconds = 35f, dish = "plate-deep",
            momText = "Spaghetti bolognese tonight! Mince is in the freezer, spaghetti and onions in the pantry, tomatoes in the fridge. One pot is fine x" },
        new Recipe { name = "Veggie omelette", vessel = Vessel.Pan, ingredients = new[] { "egg", "pepper", "mushroom", "cheese" }, seasoning = "oil", cookSeconds = 25f, dish = "egg-cooked",
            momText = "Omelette night! Eggs, peppers, mushrooms and cheese are in the fridge. Frying pan's in the cookware cupboard x" },
    };

    [Header("Dinner")]
    [Tooltip("When Mom texts tonight's recipe (hour of the day).")]
    public float startHour = 15.2f;

    // ------------------------------------------------------------------ state

    class Item
    {
        public Ingredient ing;
        public bool fetched, defrosted, cut, added;
        public float cutQuality = 1f;
    }

    public Recipe Today { get; private set; }
    readonly List<Item> items = new List<Item>();
    bool vesselFetched, waterIn, vesselPlaced, seasoned, seasoningFetched, cooking, done, tossed;
    float cookTimer;
    Food microwaveFood;
    float microwaveLeft;
    GameObject vesselOnStove;               // the pot / pan / bowl standing at its spot
    Transform vesselContents;
    Bounds vesselBounds;                    // the vessel where it stands (before anything went in)
    bool animating;                         // something is going into the pot right now: hands busy
    readonly Dictionary<Vessel, GameObject> onCounter = new Dictionary<Vessel, GameObject>(); // the pot / pan / bowl on show
    Light stoveLight, microwaveLight;
    FireHazard fire;
    bool started;

    readonly Dictionary<string, KitchenStation> stations = new Dictionary<string, KitchenStation>();

    protected virtual void Awake()
    {
        Instance = this;
        taskName = "Cook dinner";
        timeLimit = 0f;
        canStartRandomly = false;
        isMainTask = false;
        stressOnFail = 15f;
        stressReliefOnComplete = 8f;
        cooldown = 99999f;
    }

    IEnumerator Start()
    {
        yield return null;
        var kitchen = FindKitchen();
        if (!kitchen) { Debug.LogWarning("[Cooking] No kitchen ('Ground - Kitchen') found - cooking is off."); enabled = false; yield break; }

        // The old one-button oven steps aside; its fire stays (a burnt dinner still sets the kitchen on fire).
        foreach (var oven in FindObjectsOfType<OvenTask>(true))
        {
            if (!fire) fire = oven.fire;
            Destroy(oven);
        }

        Station(kitchen, "Fridge", KitchenStation.Kind.Fridge);
        Station(kitchen, "Freezer", KitchenStation.Kind.Freezer);
        Station(kitchen, "Pantry", KitchenStation.Kind.Pantry);
        Station(kitchen, "Cookware drawer", KitchenStation.Kind.Cookware);
        Station(kitchen, "Counter (Connected - Sink)", KitchenStation.Kind.Sink);
        Station(kitchen, "Cutting Board", KitchenStation.Kind.Board);
        Station(kitchen, "Microwave", KitchenStation.Kind.Microwave);
        Station(kitchen, "Oven", KitchenStation.Kind.Stove);
        Station(kitchen, "Serving island", KitchenStation.Kind.Island);
        SetUpCookwareCounter(kitchen);
        SolidIslandTop(kitchen);

        if (GameManager.Instance) GameManager.Instance.Register(this);
        Today = Recipes[Mathf.Abs(Campaign.Week * 5 + Campaign.DayIndex) % Recipes.Length];
        foreach (var id in Today.ingredients) items.Add(new Item { ing = Ingredients[id] });
        BuildCard();
    }

    static Transform FindKitchen()
    {
        foreach (var t in FindObjectsOfType<Transform>(true))
            if (t.name == "Ground - Kitchen") return t;
        return null;
    }

    void Station(Transform kitchen, string name, KitchenStation.Kind kind)
    {
        Transform found = null;
        foreach (var t in kitchen.GetComponentsInChildren<Transform>(true))
            if (t.name == name) { found = t; break; }
        if (!found) { Debug.LogWarning($"[Cooking] No '{name}' in the kitchen - that station is missing."); return; }
        if (!found.GetComponentInChildren<Collider>())
        {
            var b = Bounds(found.gameObject);
            var box = found.gameObject.AddComponent<BoxCollider>();
            box.center = found.InverseTransformPoint(b.center);
            var s = found.lossyScale;
            box.size = new Vector3(b.size.x / Mathf.Max(1e-4f, Mathf.Abs(s.x)), b.size.y / Mathf.Max(1e-4f, Mathf.Abs(s.y)), b.size.z / Mathf.Max(1e-4f, Mathf.Abs(s.z)));
        }
        if (kind == KitchenStation.Kind.Sink)
            foreach (var bs in found.GetComponentsInChildren<BucketSink>(true)) Destroy(bs); // the station empties buckets too
        var st = found.gameObject.GetComponent<KitchenStation>();
        if (!st) st = found.gameObject.AddComponent<KitchenStation>();
        st.kind = kind;
        stations[kind.ToString()] = st;
        // The highlight outline reads the mesh: only when the model allows it (the kitchen tool switches that on).
        bool readable = true;
        foreach (var mf in found.GetComponentsInChildren<MeshFilter>(true)) if (mf.sharedMesh && !mf.sharedMesh.isReadable) { readable = false; break; }
        if (readable && !found.GetComponent<Outline>())
        {
            var o = found.gameObject.AddComponent<Outline>();
            o.OutlineMode = Outline.Mode.OutlineVisible;
            o.OutlineColor = Color.white;
            o.OutlineWidth = 5f;
            o.enabled = false;
        }
    }

    // ------------------------------------------------------------------ the day

    protected override void Update()
    {
        base.Update();
        if (PauseMenu.IsPaused || Today == null) return;
        var day = DayManager.Instance;
        if (!started && day && day.State == DayManager.Phase.Day && day.Hour >= startHour && !GameManager.InGracePeriod)
        {
            started = true;
            Activate();
            day.SendText("MOM", Today.momText);
        }
        UpdateMicrowave();
        UpdateCard();
        UpdateGuide();
        PlaceDroppedVessel();
    }

    // ------------------------------------------------------------------ the kitchen guide (markers on the stations)

    /// <summary>The kitchen guide is up (you're cooking, in the kitchen): the task's own marker hides then.</summary>
    public bool GuideShowing { get; private set; }

    static string StationLabel(KitchenStation.Kind k) => k switch
    {
        KitchenStation.Kind.Fridge => "FRIDGE",
        KitchenStation.Kind.Freezer => "FREEZER",
        KitchenStation.Kind.Pantry => "PANTRY",
        KitchenStation.Kind.Cookware => "POTS & PANS",
        KitchenStation.Kind.Sink => "SINK",
        KitchenStation.Kind.Board => "CUTTING BOARD",
        KitchenStation.Kind.Microwave => "MICROWAVE",
        KitchenStation.Kind.Stove => "STOVE - COOK HERE",
        _ => "ISLAND - SERVE HERE",
    };

    /// <summary>Where the next step happens (the same order as the recipe card's "Next:").</summary>
    KitchenStation.Kind? NextStation()
    {
        var spot = Today.vessel == Vessel.Bowl ? KitchenStation.Kind.Island : KitchenStation.Kind.Stove;
        if (cooking) return KitchenStation.Kind.Stove;
        if (!vesselFetched) return KitchenStation.Kind.Cookware;
        if (Today.vessel == Vessel.Pot && !waterIn) return KitchenStation.Kind.Sink;
        if (!vesselPlaced) return spot;
        var held = HeldFood();
        if (held && held.ing != null)
        {
            if (held.ing.frozen && !held.defrosted) return KitchenStation.Kind.Microwave;
            if (held.NeedsCutting) return KitchenStation.Kind.Board;
            return spot;
        }
        if (held && held.seasoning != null) return spot;
        foreach (var i in items)
        {
            if (i.added) continue;
            if (!i.fetched) return i.ing.store == Store.Fridge ? KitchenStation.Kind.Fridge : i.ing.store == Store.Freezer ? KitchenStation.Kind.Freezer : KitchenStation.Kind.Pantry;
            if (i.ing.frozen && !i.defrosted) return KitchenStation.Kind.Microwave;
            if (i.ing.cuts > 0 && !i.cut) return KitchenStation.Kind.Board;
            return spot;
        }
        if (!seasoned) return KitchenStation.Kind.Pantry;
        return spot;
    }

    void UpdateGuide()
    {
        GuideShowing = IsActive && !done && !animating && Today != null && !VRHeadset.Worn && !CuttingGame.Running && !HudFocus.Full
                       && !(PlayerInteraction.Instance && PlayerInteraction.Instance.InMainTask)
                       && (InKitchen() || HeldFood() || HeldVessel());
        var next = GuideShowing ? NextStation() : null;
        KitchenStation target = null;
        if (next.HasValue) foreach (var kv in stations) if (kv.Value && kv.Value.kind == next.Value) { target = kv.Value; break; }
        Glow(target);
        if (!target) return;
        // just the one place that matters, with a word or two (the recipe card has the rest)
        var b = Bounds(target.gameObject);
        ScreenMarkers.Show("kitchen.next", new Vector3(b.center.x, b.max.y + 0.12f * K, b.center.z), ShortStep(next.Value), HudArt.MarkerOrange, true);
    }

    /// <summary>The next step in a word or two ("GET LETTUCE", "CHOP", "COOK").</summary>
    string ShortStep(KitchenStation.Kind at)
    {
        var held = HeldFood();
        switch (at)
        {
            case KitchenStation.Kind.Fridge:
            case KitchenStation.Kind.Freezer:
            case KitchenStation.Kind.Pantry:
                foreach (var i in items)
                    if (!i.added && !i.fetched) return "GET " + i.ing.name.ToUpperInvariant();
                return "GET " + (Today.seasoning == "salt" ? "SALT" : "OIL");
            case KitchenStation.Kind.Cookware: return "GET THE " + VesselName().ToUpperInvariant();
            case KitchenStation.Kind.Sink: return "ADD WATER";
            case KitchenStation.Kind.Board: return "CHOP";
            case KitchenStation.Kind.Microwave: return "DEFROST";
        }
        if (cooking) return cookTimer >= Today.cookSeconds ? "TAKE IT OFF!" : "COOKING...";
        if (!vesselPlaced) return "PUT IT HERE";
        if (held && held.ing != null) return "ADD " + held.ing.name.ToUpperInvariant();
        if (held && held.seasoning != null) return Today.seasoning == "salt" ? "ADD SALT" : "ADD OIL";
        if (AllIn() && seasoned) return Today.cookSeconds <= 0f ? "TOSS IT" : "COOK";
        return "HERE";
    }

    // ---- a soft orange glow on the floor round the next station (where to go, without words)

    GameObject glow;
    Material glowMat;
    KitchenStation glowOn;

    void Glow(KitchenStation st)
    {
        if (!st) { if (glow) glow.SetActive(false); glowOn = null; return; }
        if (!glow)
        {
            glow = new GameObject("Kitchen guide glow");
            glowMat = new Material(Shader.Find("Sprites/Default"));
            for (int i = 0; i < 4; i++)
            {
                var side = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Destroy(side.GetComponent<Collider>());
                side.transform.SetParent(glow.transform, false);
                var r = side.GetComponent<Renderer>();
                r.sharedMaterial = glowMat;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
        }
        glow.SetActive(true);
        if (glowOn != st)
        {
            glowOn = st;
            var b = Bounds(st.gameObject);
            float floorY = b.min.y;
            if (Physics.Raycast(new Vector3(b.center.x, b.min.y + 0.3f, b.center.z) + (Camera.main ? Vector3.ProjectOnPlane(Camera.main.transform.position - b.center, Vector3.up).normalized * (Mathf.Max(b.extents.x, b.extents.z) + 0.3f) : Vector3.zero),
                Vector3.down, out var hit, 2f, ~0, QueryTriggerInteraction.Ignore)) floorY = hit.point.y;
            float pad = 0.12f * K, t = 0.05f * K;
            Vector3 c = new Vector3(b.center.x, floorY + 0.006f, b.center.z);
            float sx = b.size.x + pad * 2f, sz = b.size.z + pad * 2f;
            var sides = glow.transform;
            sides.GetChild(0).position = c + Vector3.forward * sz * 0.5f; sides.GetChild(0).localScale = new Vector3(sx + t, 0.004f, t);
            sides.GetChild(1).position = c - Vector3.forward * sz * 0.5f; sides.GetChild(1).localScale = new Vector3(sx + t, 0.004f, t);
            sides.GetChild(2).position = c + Vector3.right * sx * 0.5f;   sides.GetChild(2).localScale = new Vector3(t, 0.004f, sz + t);
            sides.GetChild(3).position = c - Vector3.right * sx * 0.5f;   sides.GetChild(3).localScale = new Vector3(t, 0.004f, sz + t);
        }
        float pulse = 0.55f + 0.35f * Mathf.Sin(Time.unscaledTime * 4f);
        glowMat.color = new Color(1f, 0.55f, 0.12f, pulse);
    }

    protected override void TaskUpdate()
    {
        if (!cooking) return;
        cookTimer += Time.deltaTime;
        float t = cookTimer / Today.cookSeconds;
        if (stoveLight) stoveLight.intensity = 1.6f + 0.4f * Mathf.PerlinNoise(Time.time * 3f, 0.3f);
        if (Random.value < Time.deltaTime * 3f && vesselOnStove) Fx.Puff(vesselOnStove.transform.position + Vector3.up * 0.25f * K, new Color(1f, 1f, 1f, 0.5f), 2, 0.6f);
        if (t >= 1.9f)
        {
            cooking = false;
            SetStove(false);
            Fail($"The {Today.name.ToLowerInvariant()} burned - the stove caught fire!");
            if (fire && GameManager.Instance) GameManager.Instance.StartTask(fire);
        }
    }

    public override float Progress
    {
        get
        {
            int total = items.Count * 2 + 4, have = 0;
            foreach (var i in items) { if (i.fetched) have++; if (i.added) have++; }
            if (vesselFetched) have++;
            if (vesselPlaced) have++;
            if (seasoned) have++;
            if (cooking) have++;
            return Mathf.Clamp01(have / (float)total);
        }
    }

    public override string Status => Today == null ? "" : NextStep();

    public override Vector3 Where
    {
        get
        {
            stations.TryGetValue(KitchenStation.Kind.Stove.ToString(), out var s);
            return s ? s.transform.position : transform.position;
        }
    }

    public string Prompt => null;

    /// <summary>What to do next, in plain words (the to-do note and the recipe card).</summary>
    string NextStep()
    {
        string vessel = Today.vessel == Vessel.Pot ? "pot" : Today.vessel == Vessel.Pan ? "frying pan" : "big bowl";
        string spot = Today.vessel == Vessel.Bowl ? "the island" : "the stove";
        if (cooking)
        {
            float t = cookTimer / Today.cookSeconds;
            return t < 1f ? $"Cooking... {Mathf.CeilToInt(Today.cookSeconds - cookTimer)}s" : t < 1.35f ? "READY - take it off the heat now!" : "It's burning - take it off!";
        }
        if (!vesselFetched) return $"Get the {vessel} from the cookware cupboard";
        if (Today.vessel == Vessel.Pot && !waterIn) return "Fill the pot with water at the sink";
        if (!vesselPlaced) return $"Put the {vessel} on {spot}";
        foreach (var i in items)
        {
            if (i.added) continue;
            if (!i.fetched) return $"Get the {i.ing.name} from the {i.ing.store.ToString().ToLowerInvariant()}";
            if (i.ing.frozen && !i.defrosted) return microwaveFood && microwaveFood.ing == i.ing ? $"The {i.ing.name} is defrosting... {Mathf.CeilToInt(microwaveLeft)}s" : $"Defrost the {i.ing.name} in the microwave";
            if (i.ing.cuts > 0 && !i.cut) return $"Chop the {i.ing.name} on the cutting board";
            return $"Add the {i.ing.name} to the {vessel}";
        }
        if (!seasoned) return Today.seasoning == "salt" ? "Season it with salt (pantry)" : "Add oil from the pantry";
        if (Today.cookSeconds <= 0f) return "Toss the salad at the island";
        return "Turn on the stove";
    }

    // ------------------------------------------------------------------ stations

    public string PromptFor(KitchenStation.Kind kind)
    {
        if (!IsActive || Today == null || done || animating) return null;
        var held = HeldFood();
        var heldVessel = HeldVessel();
        switch (kind)
        {
            case KitchenStation.Kind.Fridge:
            case KitchenStation.Kind.Freezer:
            case KitchenStation.Kind.Pantry:
                if (held && StoreOf(held) == kind) return $"Put the {held.name} back";
                if (held || heldVessel) return null;
                return $"Open the {StoreName(kind)}";
            case KitchenStation.Kind.Cookware:
                return !vesselFetched && !held ? $"Take the {VesselName()}" : null;
            case KitchenStation.Kind.Sink:
                return heldVessel && Today.vessel == Vessel.Pot && !waterIn ? "Fill the pot with water" : null;
            case KitchenStation.Kind.Board:
                if (held && held.NeedsCutting) return $"Chop the {held.ing.name}";
                return held && held.ing != null && held.ing.frozen && !held.defrosted ? $"Defrost the {held.ing.name} first (microwave)" : null;
            case KitchenStation.Kind.Microwave:
                if (microwaveFood) return microwaveLeft <= 0f && !held && !heldVessel ? $"Take out the {microwaveFood.ing.name}" : $"Defrosting... {Mathf.CeilToInt(microwaveLeft)}s";
                return held && held.ing != null && held.ing.frozen && !held.defrosted ? $"Defrost the {held.ing.name}" : null;
            case KitchenStation.Kind.Stove:
            case KitchenStation.Kind.Island:
                bool here = (kind == KitchenStation.Kind.Stove) == (Today.vessel != Vessel.Bowl);
                if (!here) return null;
                if (heldVessel && !vesselPlaced) return Today.vessel == Vessel.Pot && !waterIn ? "Fill it with water first (sink)" : $"Put the {VesselName()} here";
                if (!vesselPlaced) return null;
                if (held && held.ing != null && held.Ready) return $"Add the {held.ing.name}";
                if (held && held.seasoning != null) return Today.seasoning == "salt" ? "Season with salt" : "Add the oil";
                if (AllIn() && seasoned)
                {
                    if (Today.cookSeconds <= 0f) return "Toss the salad";
                    if (!cooking) return "Turn on the stove";
                    return cookTimer >= Today.cookSeconds ? "Take it off the heat" : $"Cooking... {Mathf.CeilToInt(Today.cookSeconds - cookTimer)}s";
                }
                return null;
        }
        return null;
    }

    public void InteractWith(KitchenStation.Kind kind)
    {
        if (!IsActive || Today == null) { GameHUD.Toast("Mom hasn't said what's for dinner yet.", GameHUD.Info); return; }
        if (animating) return;
        var held = HeldFood();
        var heldVessel = HeldVessel();
        switch (kind)
        {
            case KitchenStation.Kind.Fridge:
            case KitchenStation.Kind.Freezer:
            case KitchenStation.Kind.Pantry:
            {
                if (held && StoreOf(held) == kind)
                {
                    // Put it back where it came from.
                    PlayerCarry.Instance.TakeHeld();
                    if (held.seasoning != null) seasoningFetched = false; else Lost(held);
                    Destroy(held.gameObject);
                    return;
                }
                if (held || heldVessel) { GameHUD.Toast("Your hands are full.", GameHUD.Warn); return; }
                OpenStore(kind);
                return;
            }
            case KitchenStation.Kind.Cookware:
                if (vesselFetched || held) return;
                vesselFetched = true;
                StartCoroutine(TakeVessel());
                return;
            case KitchenStation.Kind.Sink:
                if (heldVessel && Today.vessel == Vessel.Pot && !waterIn)
                    StartCoroutine(FillAtSink(heldVessel, stations[KitchenStation.Kind.Sink.ToString()].transform));
                return;
            case KitchenStation.Kind.Board:
                if (held && held.NeedsCutting) StartCutting(held);
                return;
            case KitchenStation.Kind.Microwave:
                if (microwaveFood)
                {
                    if (microwaveLeft > 0f || held || heldVessel) return;
                    var food = microwaveFood;
                    microwaveFood = null;
                    food.defrosted = true;
                    food.transform.SetParent(null, true);
                    food.Refresh();
                    food.GetComponent<Carryable>().enabled = true;
                    Give(food);
                    if (microwaveDisplay) microwaveDisplay.text = "";
                    return;
                }
                if (held && held.ing != null && held.ing.frozen && !held.defrosted)
                {
                    var f = PlayerCarry.Instance.TakeHeld().GetComponent<Food>();
                    microwaveFood = f;
                    microwaveLeft = 10f;
                    SetMicrowave(true);
                    PutInMicrowave(f);
                }
                return;
            case KitchenStation.Kind.Stove:
            case KitchenStation.Kind.Island:
            {
                bool here = (kind == KitchenStation.Kind.Stove) == (Today.vessel != Vessel.Bowl);
                if (!here) return;
                var st = stations[kind.ToString()];
                if (heldVessel && !vesselPlaced)
                {
                    if (Today.vessel == Vessel.Pot && !waterIn) { GameHUD.Toast("Fill the pot with water at the sink first.", GameHUD.Warn); return; }
                    PlaceVessel(PlayerCarry.Instance.TakeHeld().GetComponent<Food>(), st.transform);
                    return;
                }
                if (!vesselPlaced) return;
                if (held && held.ing != null)
                {
                    if (!held.Ready) { GameHUD.Toast(held.ing.frozen && !held.defrosted ? $"Defrost the {held.ing.name} first." : $"Chop the {held.ing.name} first.", GameHUD.Warn); return; }
                    var it = items.Find(i => i.ing == held.ing && !i.added);
                    if (it == null) { GameHUD.Toast($"The {held.ing.name} isn't in this recipe.", GameHUD.Warn); return; }
                    it.added = true;
                    it.cutQuality = held.quality;
                    PlayerCarry.Instance.TakeHeld();
                    StartCoroutine(Pour(held));
                    GameHUD.Toast($"Added the {held.ing.name}.", GameHUD.Good);
                    return;
                }
                if (held && held.seasoning != null)
                {
                    PlayerCarry.Instance.TakeHeld();
                    seasoned = true;
                    StartCoroutine(Season(held));
                    GameHUD.Toast(Today.seasoning == "salt" ? "Seasoned." : "Oil in.", GameHUD.Good);
                    return;
                }
                if (!AllIn() || !seasoned) { GameHUD.Toast(NextStep(), GameHUD.Info); return; }
                if (Today.cookSeconds <= 0f) { tossed = true; Finish(1f); return; }
                if (!cooking) { cooking = true; cookTimer = 0f; SetStove(true); GameHUD.Toast("Stove on. Don't walk off for too long!", GameHUD.Info); return; }
                if (cookTimer < Today.cookSeconds) { GameHUD.Toast($"Not cooked yet - {Mathf.CeilToInt(Today.cookSeconds - cookTimer)}s to go.", GameHUD.Info); return; }
                float t = cookTimer / Today.cookSeconds;
                cooking = false;
                SetStove(false);
                Finish(t <= 1.35f ? 1f : 0.45f);
                return;
            }
        }
    }

    void Finish(float timing)
    {
        float cut = 0f;
        foreach (var i in items) cut += i.cutQuality;
        cut /= Mathf.Max(1, items.Count);
        float q = cut * 0.6f + timing * 0.4f;
        ShowDish();
        done = true;
        if (q >= 0.85f) { DayManager.AddQuality($"Perfect {Today.name.ToLowerInvariant()}", 5); Complete($"{Today.name} - perfect! Mom is going to love this."); }
        else if (q >= 0.6f) { DayManager.AddQuality($"Tasty {Today.name.ToLowerInvariant()}", 2); Complete($"{Today.name} is ready. Not bad at all!"); }
        else Complete($"{Today.name} is... edible. Dinner's ready.");
    }

    static string StoreName(KitchenStation.Kind kind) => kind == KitchenStation.Kind.Fridge ? "fridge" : kind == KitchenStation.Kind.Freezer ? "freezer" : "pantry";

    static KitchenStation.Kind? StoreOf(Food f)
    {
        if (f.seasoning != null) return KitchenStation.Kind.Pantry;
        if (f.ing == null || f.cut || f.defrosted) return null;
        return f.ing.store == Store.Fridge ? KitchenStation.Kind.Fridge : f.ing.store == Store.Freezer ? KitchenStation.Kind.Freezer : KitchenStation.Kind.Pantry;
    }

    /// <summary>The fridge / freezer / pantry opens: everything that's in it, tonight's ingredients marked.</summary>
    void OpenStore(KitchenStation.Kind kind)
    {
        var store = kind == KitchenStation.Kind.Fridge ? Store.Fridge : kind == KitchenStation.Kind.Freezer ? Store.Freezer : Store.Pantry;
        var list = new List<StorageView.Entry>();
        foreach (var ing in Ingredients.Values)
        {
            if (ing.store != store) continue;
            var it = items.Find(i => i.ing == ing);
            if (it != null && it.fetched) continue; // already out
            var captured = ing;
            list.Add(new StorageView.Entry { id = ing.id, label = ing.name, needed = it != null, model = () => Food.WholeModelFor(captured) });
        }
        if (kind == KitchenStation.Kind.Pantry)
            foreach (var s in new[] { "salt", "oil" })
            {
                bool needed = Today.seasoning == s && !seasoned;
                if (needed && seasoningFetched) continue;
                string id = s;
                list.Add(new StorageView.Entry { id = "season:" + s, label = s, needed = needed, model = () => Food.KitModel(id == "salt" ? "shaker-salt" : "bottle-oil", id == "salt" ? 0.1f : 0.24f) });
            }
        stations.TryGetValue(kind.ToString(), out var st);
        StorageView.Open(kind, st ? st.transform : null, StoreName(kind), list, e =>
        {
            if (e.id.StartsWith("season:"))
            {
                string s = e.id.Substring(7);
                if (s == Today.seasoning) seasoningFetched = true;
                Give(Food.MakeSeasoning(s, this));
                return;
            }
            var ing = Ingredients[e.id];
            var it = items.Find(i => i.ing == ing);
            if (it != null) it.fetched = true;
            Give(Food.Make(ing, this));
        });
    }

    Item NextFrom(KitchenStation.Kind kind)
    {
        var store = kind == KitchenStation.Kind.Fridge ? Store.Fridge : kind == KitchenStation.Kind.Freezer ? Store.Freezer : Store.Pantry;
        return items.Find(i => !i.fetched && i.ing.store == store);
    }

    bool NeedSeasoning() => !seasoned && !seasoningFetched;
    bool AllIn() => items.TrueForAll(i => i.added);
    string VesselName() => Today.vessel == Vessel.Pot ? "pot" : Today.vessel == Vessel.Pan ? "frying pan" : "big bowl";

    Food HeldFood()
    {
        var c = PlayerCarry.Instance ? PlayerCarry.Instance.Held : null;
        var f = c ? c.GetComponent<Food>() : null;
        return f && !f.isVessel ? f : null;
    }

    Food HeldVessel()
    {
        var c = PlayerCarry.Instance ? PlayerCarry.Instance.Held : null;
        var f = c ? c.GetComponent<Food>() : null;
        return f && f.isVessel ? f : null;
    }

    void Give(Food f)
    {
        if (!f) return;
        if (PlayerCarry.Instance) PlayerCarry.Instance.PickUp(f.GetComponent<Carryable>());
    }

    /// <summary>A frozen thing back to the freezer, a dropped thing picked up again: if the player loses an item, it can be fetched again.</summary>
    public void Lost(Food f)
    {
        if (f.ing == null) return;
        var it = items.Find(i => i.ing == f.ing && !i.added);
        if (it != null) { it.fetched = false; it.defrosted = false; it.cut = false; }
    }

    public void Defrosted(Food f) { var it = items.Find(i => i.ing == f.ing && !i.added); if (it != null) it.defrosted = true; }
    public void Chopped(Food f) { var it = items.Find(i => i.ing == f.ing && !i.added); if (it != null) { it.cut = true; it.cutQuality = f.quality; } }

    // ------------------------------------------------------------------ the pot / pan / bowl at its spot

    void PlaceVessel(Food v, Transform spot)
    {
        var b = Bounds(spot.gameObject);
        v.GetComponent<Carryable>().enabled = false;
        foreach (var c in v.GetComponentsInChildren<Collider>()) c.enabled = false;
        v.transform.SetParent(spot, true);
        v.transform.rotation = Quaternion.identity;
        var vb = Bounds(v.gameObject);
        v.transform.position += new Vector3(b.center.x, b.max.y, b.center.z) - new Vector3(vb.center.x, vb.min.y, vb.center.z) + Vector3.up * 0.005f;
        bool stove = spot.GetComponent<KitchenStation>().kind == KitchenStation.Kind.Stove;
        Vector3? burner = stove ? Burner(spot, out burnerRadius) : null;
        if (burner.HasValue)
        {
            // On the front burner nearest you: a pan's handle towards you and out to the side, a pot's handles left and right.
            Orient(v, spot, burner.Value);
            Vector3 body = v.BodyCentre;
            vb = Bounds(v.gameObject);
            v.transform.position += new Vector3(burner.Value.x - body.x, burner.Value.y + 0.003f - vb.min.y, burner.Value.z - body.z);
        }
        vesselOnStove = v.gameObject;
        vesselBounds = Bounds(v.gameObject);
        // Where things go in: its body, not the handle (a pan's handle stretches its bounds off to one side).
        float br = v.BodyRadius;
        Vector3 bc = v.BodyCentre;
        if (br > 0.01f) vesselBounds = new Bounds(new Vector3(bc.x, vesselBounds.center.y, bc.z), new Vector3(br * 2f, vesselBounds.size.y, br * 2f));
        vesselContents = new GameObject("Contents").transform;
        vesselContents.SetParent(v.transform, false);
        vesselPlaced = true;
        if (spot.GetComponent<KitchenStation>().kind == KitchenStation.Kind.Stove)
        {
            stoveLight = new GameObject("Stove glow").AddComponent<Light>();
            stoveLight.transform.SetParent(spot, false);
            stoveLight.transform.position = burner.HasValue ? burner.Value + Vector3.up * 0.06f : new Vector3(b.center.x, b.max.y + 0.05f, b.center.z);
            stoveLight.type = LightType.Point;
            stoveLight.color = new Color(1f, 0.45f, 0.15f);
            stoveLight.range = 1.2f * K;
            stoveLight.enabled = false;
            if (burner.HasValue)
            {
                // the ring glows orange-red under the pot when the stove is on
                burnerGlow = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                Destroy(burnerGlow.GetComponent<Collider>());
                burnerGlow.name = "Burner glow";
                burnerGlow.transform.SetParent(spot, true);
                burnerGlow.transform.position = burner.Value + Vector3.up * 0.0015f;
                burnerGlow.transform.rotation = Quaternion.identity;
                var ls = spot.lossyScale;
                burnerGlow.transform.localScale = new Vector3(burnerRadius * 2.3f / ls.x, 0.001f / ls.y, burnerRadius * 2.3f / ls.z);
                var gm = new Material(Shader.Find("Standard")) { color = new Color(0.9f, 0.25f, 0.08f) };
                gm.EnableKeyword("_EMISSION");
                gm.SetColor("_EmissionColor", new Color(1f, 0.3f, 0.05f) * 1.6f);
                burnerGlow.GetComponent<Renderer>().sharedMaterial = gm;
                burnerGlow.SetActive(false);
            }
        }
        GameHUD.Toast($"The {VesselName()} is in place.", GameHUD.Good);
    }

    // ------------------------------------------------------------------ things going in (you see it happen)

    /// <summary>Where added things settle: on the water in a pot of water, otherwise near the bottom.</summary>
    float LandY() => waterIn && Today.vessel == Vessel.Pot
        ? vesselBounds.min.y + vesselBounds.size.y * 0.7f + 0.004f
        : vesselBounds.min.y + vesselBounds.size.y * (Today.vessel == Vessel.Pan ? 0.5f : 0.3f); // a pan is shallow: on its floor, not under it

    IEnumerator Pour(Food f)
    {
        if (!vesselOnStove || !f) { if (f) Destroy(f.gameObject); yield break; }
        animating = true;
        var eye = Camera.main ? Camera.main.transform.position : f.transform.position;
        yield return f.PourInto(vesselContents, vesselBounds, eye, LandY(), waterIn);
        animating = false;
    }

    IEnumerator Season(Food f)
    {
        if (!vesselOnStove || !f) { if (f) Destroy(f.gameObject); yield break; }
        animating = true;
        var eye = Camera.main ? Camera.main.transform.position : f.transform.position;
        yield return f.SeasonInto(vesselContents, vesselBounds, eye, LandY());
        animating = false;
    }

    /// <summary>The pot / pan / bowl comes off the cookware counter (the one on show there goes) and into your hands.</summary>
    Food fetchedVessel;

    /// <summary>Where dinner is: the pot / pan / bowl if it's out, otherwise the stove (Mom goes there to have a look).</summary>
    public Vector3? DinnerSpot
    {
        get
        {
            if (dishShown) return dishShown.transform.position; // (served: Mom looks at the plate)
            if (vesselOnStove) return vesselOnStove.transform.position;
            if (stations.TryGetValue(KitchenStation.Kind.Stove.ToString(), out var st) && st) return st.transform.position;
            return null;
        }
    }

    /// <summary>
    /// The pot / pan / bowl put down (dropped with G) on its own spot - the bowl on the island, the pan or the filled pot on the
    /// stove - counts as putting it there, the same as pressing E.
    /// </summary>
    void PlaceDroppedVessel()
    {
        if (!IsActive || done || animating || vesselPlaced || !fetchedVessel) return;
        var carry = fetchedVessel.GetComponent<Carryable>();
        if (!carry || carry.IsHeld) return;
        if (Today.vessel == Vessel.Pot && !waterIn) return;
        var kind = Today.vessel == Vessel.Bowl ? KitchenStation.Kind.Island : KitchenStation.Kind.Stove;
        if (!stations.TryGetValue(kind.ToString(), out var st) || !st) return;
        var sb = Bounds(st.gameObject);
        var p = fetchedVessel.transform.position;
        bool onIt = p.x > sb.min.x - 0.05f && p.x < sb.max.x + 0.05f && p.z > sb.min.z - 0.05f && p.z < sb.max.z + 0.05f && p.y > sb.max.y - 0.25f && p.y < sb.max.y + 0.3f;
        if (onIt) PlaceVessel(fetchedVessel, st.transform);
    }

    /// <summary>The island's wooden top has no collider of its own (only the cupboards under it): things dropped on it sank in.</summary>
    void SolidIslandTop(Transform kitchen)
    {
        foreach (var t in kitchen.GetComponentsInChildren<Transform>(true))
        {
            if (t.name != "Serving island") continue;
            var top = t.Find("Top");
            if (top && !top.GetComponent<Collider>()) top.gameObject.AddComponent<BoxCollider>();
        }
    }

    IEnumerator TakeVessel()
    {
        var v = Food.MakeVessel(Today.vessel, this);
        fetchedVessel = v;
        var hand = PlayerCarry.Instance ? PlayerCarry.Instance.holdPoint : null;
        if (onCounter.TryGetValue(Today.vessel, out var shown) && shown && hand)
        {
            var sb = Bounds(shown);
            v.transform.position = new Vector3(sb.center.x, sb.min.y, sb.center.z);
            v.transform.rotation = Quaternion.Euler(0f, shown.transform.eulerAngles.y, 0f);
            shown.SetActive(false);
            animating = true;
            yield return v.LiftTo(hand, 0.3f);
            animating = false;
        }
        Give(v);
    }

    /// <summary>
    /// The cookware counter shows what's in the cupboard: the pot and the frying pan the kitchen tool put there, and the salad bowl
    /// (added here, in the clearest free spot on the top - and the pot / pan too if they're not there).
    /// </summary>
    void SetUpCookwareCounter(Transform kitchen)
    {
        if (!stations.TryGetValue(KitchenStation.Kind.Cookware.ToString(), out var cw) || !cw) return;
        var top = Bounds(cw.gameObject);
        var names = new Dictionary<string, Vessel> { { "pot", Vessel.Pot }, { "frying-pan", Vessel.Pan }, { "bowl", Vessel.Bowl } };
        var taken = new List<Bounds>();
        foreach (var t in kitchen.GetComponentsInChildren<Transform>(true))
        {
            if (t.IsChildOf(cw.transform)) continue;
            var b = Bounds(t.gameObject);
            if (b.size == Vector3.zero) continue;
            bool onTop = b.center.x > top.min.x - 0.05f && b.center.x < top.max.x + 0.05f && b.center.z > top.min.z - 0.05f && b.center.z < top.max.z + 0.05f
                         && b.min.y > top.max.y - 0.05f && b.min.y < top.max.y + 0.1f;
            if (!onTop) continue;
            if (names.TryGetValue(t.name, out var v) && !onCounter.ContainsKey(v)) { onCounter[v] = t.gameObject; Food.UseFoodPalette(t.gameObject); }
            taken.Add(b);
        }
        // Whatever isn't on show yet (always the bowl; the pot / pan too if they were moved off) goes in the clearest free spot.
        foreach (var (v, model, size) in new[] { (Vessel.Bowl, "bowl", Food.CounterBowlSize), (Vessel.Pot, "pot", 0.27f), (Vessel.Pan, "frying-pan", 0.34f) })
        {
            if (onCounter.ContainsKey(v)) continue;
            var shown = Food.KitModel(model, size);
            if (!shown) continue;
            shown.name = model;
            shown.transform.SetParent(cw.transform.parent, true);
            var sb = Bounds(shown);
            shown.transform.position = FreeSpot(top, Mathf.Max(sb.size.x, sb.size.z), taken);
            taken.Add(Bounds(shown));
            onCounter[v] = shown;
        }
    }

    /// <summary>The point on the counter top where a round thing this wide has the most room around it (it never overlaps anything there if it can help it).</summary>
    static Vector3 FreeSpot(Bounds top, float size, List<Bounds> taken)
    {
        float half = size * 0.5f + 0.015f;
        Vector3 best = new Vector3(top.center.x, top.max.y, top.center.z);
        float bestGap = float.MinValue;
        for (float x = top.min.x + half; x <= top.max.x - half + 1e-4f; x += 0.01f)
            for (float z = top.min.z + half; z <= top.max.z - half + 1e-4f; z += 0.01f)
            {
                float gap = float.MaxValue;
                foreach (var b in taken)
                {
                    float dx = Mathf.Abs(x - b.center.x) - b.extents.x - size * 0.5f;
                    float dz = Mathf.Abs(z - b.center.z) - b.extents.z - size * 0.5f;
                    gap = Mathf.Min(gap, Mathf.Max(dx, dz)); // < 0 = overlapping
                }
                if (gap > bestGap) { bestGap = gap; best = new Vector3(x, top.max.y + 0.002f, z); }
            }
        return best;
    }

    /// <summary>
    /// Dinner's ready: it's served - plated up on a placemat at the island, with cutlery and a napkin (the pot / pan / bowl it was made
    /// in stays where it is, empty). Each recipe gets its own plate: the stew in a bowl on a plate with a spoon, the bolognese in a
    /// deep plate, the omelette on a dinner plate, the salad in its bowl.
    /// </summary>
    void ShowDish()
    {
        if (vesselContents) vesselContents.gameObject.SetActive(false);
        if (!ServeSpot(out Vector3 at, out float yaw)) return;
        float k = K;
        var root = new GameObject("Dinner - " + Today.name);
        root.transform.SetPositionAndRotation(at, Quaternion.Euler(0f, yaw, 0f));
        dishShown = root;

        // the place setting
        Block(root.transform, "Placemat", new Vector3(0f, 0.002f, 0f), new Vector3(0.5f, 0.004f, 0.36f) * k, new Color(0.78f, 0.24f, 0.2f));
        Block(root.transform, "Placemat edge", new Vector3(0f, 0.0025f, 0f), new Vector3(0.46f, 0.004f, 0.32f) * k, new Color(0.88f, 0.4f, 0.32f));
        Block(root.transform, "Napkin", new Vector3(-0.2f, 0.006f, 0.02f), new Vector3(0.07f, 0.008f, 0.16f) * k, new Color(0.96f, 0.95f, 0.9f));
        bool soup = Today.dish == "bowl-broth";
        Cutlery(root.transform, new Vector3(-0.2f, 0.012f, 0.02f) * k, "fork");
        Cutlery(root.transform, new Vector3(0.2f, 0.006f, 0.02f) * k, soup ? "spoon" : "knife");

        // the food
        GameObject Put(string id, float size, float y)
        {
            var m = CookingKit.Instance ? CookingKit.Instance.Model(id) : null;
            if (!m) return null;
            var go = Instantiate(m, root.transform);
            go.name = id;
            go.transform.localRotation = Quaternion.identity;
            Food.UseFoodPalette(go);
            Fit(go, size * k);
            var b = Bounds(go);
            go.transform.position += new Vector3(at.x, at.y + y * k, at.z) - new Vector3(b.center.x, b.min.y, b.center.z);
            return go;
        }
        switch (Today.dish)
        {
            case "bowl-broth":
                Put("plate-dinner", 0.27f, 0.004f);
                Put("bowl-broth", 0.2f, 0.016f);
                break;
            case "plate-deep":
                Put("plate-deep", 0.28f, 0.004f);
                Pasta(root.transform, at, k);
                break;
            case "egg-cooked":
                Put("plate-dinner", 0.27f, 0.004f);
                Omelette(root.transform, at, k);
                break;
            case "salad":
                Put("salad", 0.25f, 0.004f);
                break;
            default:
                Put("plate-dinner", 0.27f, 0.004f);
                Put(Today.dish, 0.2f, 0.016f);
                break;
        }
        foreach (var c in root.GetComponentsInChildren<Collider>()) Destroy(c);
        Fx.Puff(at + Vector3.up * 0.15f * k, new Color(1f, 1f, 1f, 0.6f), 6, 0.7f);
        GameHUD.Toast($"{Today.name} is served - it's on the island.", GameHUD.Good);
    }

    /// <summary>Where dinner goes: a clear bit of the island top (or by the stove if there's no island).</summary>
    bool ServeSpot(out Vector3 at, out float yaw)
    {
        at = default; yaw = 0f;
        GameObject on = null;
        if (stations.TryGetValue(KitchenStation.Kind.Island.ToString(), out var island) && island) on = island.gameObject;
        else if (vesselOnStove) { at = vesselOnStove.transform.position + Vector3.right * 0.4f * K; return true; }
        if (!on) return false;
        var top = Bounds(on);
        var taken = new List<Bounds>();
        if (vesselOnStove) taken.Add(Bounds(vesselOnStove));
        foreach (var kv in onCounter) if (kv.Value && kv.Value.activeInHierarchy) taken.Add(Bounds(kv.Value));
        taken.RemoveAll(b => b.max.x < top.min.x || b.min.x > top.max.x || b.max.z < top.min.z || b.min.z > top.max.z);
        float size = 0.52f * K;
        Vector3 spot = taken.Count == 0 ? new Vector3(top.center.x, top.max.y, top.center.z) : FreeSpot(top, size, taken);
        // the real surface under it (not something standing on it)
        at = new Vector3(spot.x, top.max.y, spot.z);
        foreach (var hit in Physics.RaycastAll(at + Vector3.up * 0.5f, Vector3.down, 1.5f, ~0, QueryTriggerInteraction.Ignore).OrderBy(h => h.distance))
        {
            if (hit.collider.GetComponentInParent<Carryable>() || hit.collider.GetComponentInParent<Food>()) continue;
            at = hit.point + Vector3.up * 0.001f;
            break;
        }
        yaw = top.size.x >= top.size.z ? 0f : 90f; // the mat along the island
        return true;
    }

    static void Block(Transform parent, string name, Vector3 localPos, Vector3 size, Color c)
    {
        var b = GameObject.CreatePrimitive(PrimitiveType.Cube);
        b.name = name;
        Destroy(b.GetComponent<Collider>());
        b.transform.SetParent(parent, false);
        b.transform.localPosition = localPos * (name.StartsWith("Placemat") || name == "Napkin" ? K : 1f);
        b.transform.localScale = size;
        var m = new Material(Shader.Find("Standard")) { color = c };
        m.SetFloat("_Glossiness", 0.12f);
        b.GetComponent<Renderer>().sharedMaterial = m;
    }

    /// <summary>A fork / knife / spoon lying flat, handle towards you.</summary>
    static void Cutlery(Transform parent, Vector3 at, string kind)
    {
        float k = K;
        var t = new GameObject(kind).transform;
        t.SetParent(parent, false);
        t.localPosition = at;
        var steel = new Material(Shader.Find("Standard")) { color = new Color(0.8f, 0.81f, 0.84f) };
        steel.SetFloat("_Metallic", 0.8f);
        steel.SetFloat("_Glossiness", 0.75f);
        void Part(PrimitiveType shape, Vector3 p, Vector3 s)
        {
            var g = GameObject.CreatePrimitive(shape);
            Destroy(g.GetComponent<Collider>());
            g.transform.SetParent(t, false);
            g.transform.localPosition = p * k;
            g.transform.localScale = s * k;
            g.GetComponent<Renderer>().sharedMaterial = steel;
        }
        Part(PrimitiveType.Cube, new Vector3(0f, 0.002f, -0.04f), new Vector3(0.012f, 0.004f, 0.1f)); // handle
        if (kind == "fork")
        {
            Part(PrimitiveType.Cube, new Vector3(0f, 0.002f, 0.025f), new Vector3(0.022f, 0.004f, 0.03f));
            for (int i = 0; i < 4; i++) Part(PrimitiveType.Cube, new Vector3(-0.009f + i * 0.006f, 0.002f, 0.055f), new Vector3(0.003f, 0.003f, 0.03f));
        }
        else if (kind == "knife") Part(PrimitiveType.Cube, new Vector3(0.002f, 0.002f, 0.045f), new Vector3(0.017f, 0.003f, 0.09f));
        else Part(PrimitiveType.Sphere, new Vector3(0f, 0.004f, 0.04f), new Vector3(0.035f, 0.008f, 0.05f)); // spoon bowl
    }

    /// <summary>Spaghetti bolognese in the deep plate: a nest of pasta, the sauce, meatballs, a bit of basil.</summary>
    static void Pasta(Transform parent, Vector3 at, float k)
    {
        Material Mat(Color c, float gloss) { var m = new Material(Shader.Find("Standard")) { color = c }; m.SetFloat("_Glossiness", gloss); return m; }
        void Blob(Material m, Vector3 p, Vector3 s)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(g.GetComponent<Collider>());
            g.transform.SetParent(parent, true);
            g.transform.position = at + p * k;
            g.transform.localScale = s * k;
            g.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
            g.GetComponent<Renderer>().sharedMaterial = m;
        }
        var pasta = Mat(new Color(0.96f, 0.84f, 0.5f), 0.35f);
        var sauce = Mat(new Color(0.72f, 0.16f, 0.1f), 0.5f);
        var meat = Mat(new Color(0.42f, 0.24f, 0.15f), 0.2f);
        var basil = Mat(new Color(0.2f, 0.55f, 0.2f), 0.3f);
        Blob(pasta, new Vector3(0f, 0.03f, 0f), new Vector3(0.17f, 0.035f, 0.17f));
        for (int i = 0; i < 6; i++) // loose strands round the edge
        {
            float a = i * 60f * Mathf.Deg2Rad;
            Blob(pasta, new Vector3(Mathf.Cos(a) * 0.06f, 0.038f, Mathf.Sin(a) * 0.06f), new Vector3(0.07f, 0.012f, 0.02f));
        }
        Blob(sauce, new Vector3(0f, 0.048f, 0f), new Vector3(0.1f, 0.022f, 0.1f));
        Blob(meat, new Vector3(0.018f, 0.06f, 0.01f), Vector3.one * 0.028f);
        Blob(meat, new Vector3(-0.022f, 0.058f, -0.012f), Vector3.one * 0.026f);
        Blob(meat, new Vector3(0.0f, 0.06f, -0.03f), Vector3.one * 0.025f);
        Blob(basil, new Vector3(0.01f, 0.072f, 0.02f), new Vector3(0.025f, 0.004f, 0.014f));
    }

    /// <summary>A folded omelette (a golden half-moon) with peppers and mushroom peeking out, and a sprinkle of cheese.</summary>
    static void Omelette(Transform parent, Vector3 at, float k)
    {
        var egg = new Material(Shader.Find("Standard")) { color = new Color(0.98f, 0.8f, 0.3f) };
        egg.SetFloat("_Glossiness", 0.35f);
        var brown = new Material(Shader.Find("Standard")) { color = new Color(0.88f, 0.62f, 0.22f) };
        void Shape(Material m, Vector3 p, Vector3 s, float yaw, float roll)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(g.GetComponent<Collider>());
            g.transform.SetParent(parent, true);
            g.transform.position = at + p * k;
            g.transform.rotation = Quaternion.Euler(0f, yaw, roll);
            g.transform.localScale = s * k;
            g.GetComponent<Renderer>().sharedMaterial = m;
        }
        Shape(egg, new Vector3(0f, 0.026f, 0f), new Vector3(0.19f, 0.04f, 0.12f), 20f, 0f);      // the folded omelette
        Shape(brown, new Vector3(0.01f, 0.04f, 0.006f), new Vector3(0.15f, 0.018f, 0.085f), 20f, 0f); // a golden top
        Specks(parent, at + Vector3.up * 0.052f * k, k, new[] { new Color(0.85f, 0.2f, 0.15f), new Color(0.3f, 0.65f, 0.25f), new Color(0.95f, 0.9f, 0.6f) }, 10, 0.05f);
    }

    /// <summary>Little bits of veg scattered over a dish.</summary>
    static void Specks(Transform parent, Vector3 at, float k, Color[] colours, int count, float spread)
    {
        var mats = colours.Select(c => new Material(Shader.Find("Standard")) { color = c }).ToArray();
        for (int i = 0; i < count; i++)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(g.GetComponent<Collider>());
            g.transform.SetParent(parent, true);
            Vector2 r = Random.insideUnitCircle * spread;
            g.transform.position = at + new Vector3(r.x, 0f, r.y) * k;
            g.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
            g.transform.localScale = new Vector3(0.014f, 0.006f, 0.012f) * k;
            g.GetComponent<Renderer>().sharedMaterial = mats[i % mats.Length];
        }
    }

    GameObject dishShown;

    /// <summary>Dinner's made and sitting out (the cat's had her eye on it).</summary>
    public bool DinnerOut => done && dishShown && !Eaten;
    public Vector3 DishPoint => dishShown ? dishShown.transform.position : transform.position;
    /// <summary>The cat ate it.</summary>
    public bool Eaten { get; private set; }

    /// <summary>The cat got to dinner: it's gone (and so are the points for cooking it well).</summary>
    public void CatAteDinner()
    {
        if (!dishShown || Eaten) return;
        Eaten = true;
        Vector3 at = dishShown.transform.position;
        Destroy(dishShown);
        dishShown = null;
        if (DayManager.Instance) DayManager.Instance.quality.RemoveAll(q => q.what.StartsWith("Perfect") || q.what.StartsWith("Tasty"));
        Incidents.Add("The cat ate dinner", "Where's dinner?! Was that the CAT?!", 12, at);
        GameHUD.Toast($"{CatBrain.Name} ate dinner!", GameHUD.Bad);
    }

    void SetStove(bool on)
    {
        if (stoveLight) stoveLight.enabled = on;
        if (burnerGlow) burnerGlow.SetActive(on);
    }

    GameObject burnerGlow;
    float burnerRadius;

    // The stove's four burners, in its own model's space (measured from the VNB "Oven" mesh): the front pair, then the back pair.
    static readonly Vector3[] Burners =
    {
        new Vector3(-0.194f, 0.944f, 0.045f), new Vector3(0.194f, 0.944f, 0.045f),
        new Vector3(-0.194f, 0.944f, -0.212f), new Vector3(0.194f, 0.944f, -0.212f),
    };

    /// <summary>The top of the front burner nearest you (null if it isn't the stove model these burners were measured on).</summary>
    static Vector3? Burner(Transform oven, out float radius)
    {
        radius = 0f;
        var mf = oven.GetComponent<MeshFilter>();
        if (!mf || !mf.sharedMesh || mf.sharedMesh.name != "Oven") return null;
        var eye = Camera.main ? Camera.main.transform.position : oven.position + oven.forward;
        int best = 0;
        float bestD = float.MaxValue;
        for (int i = 0; i < 2; i++)
        {
            float d = (oven.TransformPoint(Burners[i]) - eye).sqrMagnitude;
            if (d < bestD) { bestD = d; best = i; }
        }
        radius = 0.081f * oven.lossyScale.x;
        return oven.TransformPoint(Burners[best]);
    }

    /// <summary>Turns the vessel the way you'd put it on: a pan's handle towards you and out to the side, a pot's two handles left-right.</summary>
    static void Orient(Food v, Transform oven, Vector3 burner)
    {
        var b = Bounds(v.gameObject);
        Vector3 handle = b.center - v.BodyCentre;
        handle.y = 0f;
        if (handle.magnitude > 0.02f)
        {
            Vector3 eye = Camera.main ? Camera.main.transform.position : burner + oven.forward;
            Vector3 toYou = eye - burner; toYou.y = 0f;
            Vector3 side = Vector3.Project(burner - oven.position, oven.right); side.y = 0f;
            Vector3 want = (toYou.normalized + side.normalized * 0.8f).normalized;
            v.transform.Rotate(0f, Vector3.SignedAngle(handle.normalized, want, Vector3.up), 0f, Space.World);
        }
        else
        {
            Vector3 axis = v.LongAxis; axis.y = 0f;
            if (axis.sqrMagnitude > 1e-4f) v.transform.Rotate(0f, Vector3.SignedAngle(axis.normalized, oven.right, Vector3.up), 0f, Space.World);
        }
    }

    // ------------------------------------------------------------------ filling the pot at the sink (you see it happen)

    IEnumerator FillAtSink(Food pot, Transform sink)
    {
        animating = true;
        PlayerCarry.Instance.TakeHeld();
        var sb = Bounds(sink.gameObject);
        var sinkMesh = sink.GetComponent<MeshFilter>();
        float counterTop = sinkMesh && sinkMesh.sharedMesh ? sink.TransformPoint(new Vector3(0f, sinkMesh.sharedMesh.bounds.max.y, 0f)).y : sb.max.y;
        // The spout: the faucet's end that reaches out over the basin.
        Transform faucet = null, tap = null;
        foreach (var t in sink.GetComponentsInChildren<Transform>())
        {
            if (t.name == "Faucet") faucet = t;
            else if (t.name == "Tap" && !tap) tap = t;
        }
        Vector3 toBasin = sb.center - (faucet ? faucet.position : sb.center + sink.forward);
        toBasin.y = 0f;
        toBasin = toBasin.sqrMagnitude > 1e-4f ? toBasin.normalized : sink.forward;
        Vector3 tip;
        if (faucet && faucet.GetComponent<Renderer>())
        {
            var fb = faucet.GetComponent<Renderer>().bounds;
            tip = new Vector3(fb.center.x, fb.max.y - 0.045f, fb.center.z) + toBasin * (Along(toBasin, fb.extents) - 0.015f);
        }
        else tip = new Vector3(sb.center.x, counterTop + 0.3f, sb.center.z);

        // The pot goes down into the basin, under the spout (handles along the sink).
        Vector3 axis = pot.LongAxis; axis.y = 0f;
        Quaternion upright = Quaternion.Euler(0f, pot.transform.eulerAngles.y, 0f);
        if (axis.sqrMagnitude > 1e-4f)
            upright = Quaternion.AngleAxis(Vector3.SignedAngle(axis.normalized, Vector3.Cross(toBasin, Vector3.up), Vector3.up), Vector3.up) * upright;
        // down on the basin floor (measured on the VNB sink: 0.79 in its own space; otherwise a guess)
        float floorY = sinkMesh && sinkMesh.sharedMesh && sinkMesh.sharedMesh.name == "Counter (Connected - Sink)" ? sink.TransformPoint(new Vector3(0f, 0.79f, 0f)).y + 0.004f : counterTop - 0.1f;
        Vector3 spot = new Vector3(tip.x, floorY, tip.z) + toBasin * 0.06f;
        yield return pot.MoveTo(spot, upright, 0.35f);

        // The tap turns, the water runs, the pot fills.
        Quaternion tapRest = tap ? tap.localRotation : Quaternion.identity;
        for (float t = 0f; t < 1f && tap; t += Time.deltaTime / 0.15f) { tap.localRotation = tapRest * Quaternion.Euler(0f, 90f * t, 0f); yield return null; }
        var stream = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Destroy(stream.GetComponent<Collider>());
        stream.name = "Running water";
        var wm = new Material(Shader.Find("Standard")) { color = new Color(0.75f, 0.9f, 1f) };
        wm.SetFloat("_Glossiness", 0.95f);
        wm.EnableKeyword("_EMISSION");
        wm.SetColor("_EmissionColor", new Color(0.25f, 0.35f, 0.45f)); // catches the light like running water
        stream.GetComponent<Renderer>().sharedMaterial = wm;
        pot.ShowWater(true, 0.08f);
        var pb = Bounds(pot.gameObject);
        float bottom = pb.min.y, height = pb.size.y;
        const float fill = 1.6f;
        float nextSplash = 0f;
        for (float e = 0f; e < fill + 0.2f; e += Time.deltaTime)
        {
            float level = Mathf.Lerp(0.08f, 0.7f, Mathf.Clamp01(e / fill));
            pot.SetWaterLevel(level);
            float surface = bottom + height * level;
            float top = e < fill ? tip.y : Mathf.Lerp(tip.y, surface, (e - fill) / 0.2f); // when it's full the stream runs out from the top
            float bot = e < 0.15f ? Mathf.Lerp(tip.y, surface, e / 0.15f) : surface;
            float len = Mathf.Max(0.002f, top - bot);
            stream.transform.position = new Vector3(tip.x, (top + bot) * 0.5f, tip.z);
            stream.transform.localScale = new Vector3(0.012f, len * 0.5f, 0.012f);
            if (e > 0.15f && e < fill && e >= nextSplash) { nextSplash = e + 0.18f; Fx.Splash(new Vector3(tip.x, surface, tip.z), 2, 0.25f); }
            yield return null;
        }
        Destroy(stream);
        for (float t = 0f; t < 1f && tap; t += Time.deltaTime / 0.15f) { tap.localRotation = tapRest * Quaternion.Euler(0f, 90f * (1f - t), 0f); yield return null; }
        if (tap) tap.localRotation = tapRest;

        // Back into your hands, full.
        var hand = PlayerCarry.Instance ? PlayerCarry.Instance.holdPoint : null;
        if (hand) yield return pot.LiftTo(hand, 0.3f);
        var carry = pot.GetComponent<Carryable>();
        if (carry) carry.enabled = true;
        waterIn = true;
        animating = false;
        Give(pot);
        GameHUD.Toast("Pot filled with water.", GameHUD.Good);
    }

    void SetMicrowave(bool on)
    {
        stations.TryGetValue(KitchenStation.Kind.Microwave.ToString(), out var mw);
        if (!mw) return;
        if (!microwaveLight)
        {
            var b = Bounds(mw.gameObject);
            microwaveLight = new GameObject("Microwave light").AddComponent<Light>();
            microwaveLight.transform.SetParent(mw.transform, false);
            microwaveLight.transform.position = b.center;
            microwaveLight.type = LightType.Point;
            microwaveLight.color = new Color(1f, 0.85f, 0.5f);
            microwaveLight.range = 0.9f * K;
            microwaveLight.intensity = 1.6f;
        }
        microwaveLight.enabled = on;
    }

    void UpdateMicrowave()
    {
        if (!microwaveFood) return;
        if (microwaveLeft > 0f)
        {
            microwaveFood.transform.Rotate(0f, 50f * Time.deltaTime, 0f, Space.World); // the turntable
            microwaveLeft -= Time.deltaTime;
            if (microwaveDisplay) microwaveDisplay.text = $"0:{Mathf.CeilToInt(Mathf.Max(0f, microwaveLeft)):00}";
            if (microwaveLeft > 0f) return;
            microwaveLeft = 0f;
            SetMicrowave(false);
            Defrosted(microwaveFood);
            GameHUD.Toast($"Ding! The {microwaveFood.ing.name} is defrosted.", GameHUD.Good);
        }
        if (microwaveDisplay) microwaveDisplay.text = Mathf.Repeat(Time.time, 1f) < 0.6f ? "END" : "";
    }

    TextMeshPro microwaveDisplay;

    /// <summary>The food goes in on the turntable (you can see it through the door) and the timer shows on the display.</summary>
    void PutInMicrowave(Food f)
    {
        stations.TryGetValue(KitchenStation.Kind.Microwave.ToString(), out var mw);
        if (!mw) return;
        var t = mw.transform;
        var b = Bounds(mw.gameObject);
        float w = Along(t.right, b.extents) * 2f, h = Along(t.up, b.extents) * 2f, d = Along(t.forward, b.extents) * 2f;
        f.GetComponent<Carryable>().enabled = false;
        foreach (var c in f.GetComponentsInChildren<Collider>()) c.enabled = false;
        f.transform.SetParent(t, true);
        f.transform.rotation = Quaternion.LookRotation(t.forward, Vector3.up);
        var fb = Bounds(f.gameObject);
        float big = Mathf.Max(fb.size.x, fb.size.y, fb.size.z);
        float room = Mathf.Min(w * 0.4f, h * 0.45f);
        if (big > room) f.transform.localScale *= room / big;
        fb = Bounds(f.gameObject);
        // The cavity is the left two thirds (seen from the front); the panel with the display is on the right.
        Vector3 cavity = b.center + t.right * (w * 0.1f) + t.up * (-h * 0.5f + h * 0.18f);
        float floorY = b.min.y + h * 0.16f;
        bool vnb = IsVnbMicrowave(t);
        if (vnb) { cavity = t.TransformPoint(new Vector3(0.06f, 0f, 0f)); floorY = t.TransformPoint(new Vector3(0f, 0.075f, 0f)).y; } // on the turntable
        f.transform.position += new Vector3(cavity.x, floorY, cavity.z) - new Vector3(fb.center.x, fb.min.y, fb.center.z);
        if (!microwaveDisplay)
        {
            var go = new GameObject("Microwave display");
            go.transform.SetParent(t, true);
            microwaveDisplay = go.AddComponent<TextMeshPro>();
            var font = GameHUD.Instance ? GameHUD.Instance.font : null;
            if (font) microwaveDisplay.font = font;
            microwaveDisplay.color = new Color(0.35f, 1f, 0.45f);
            microwaveDisplay.alignment = TextAlignmentOptions.Center;
            microwaveDisplay.enableWordWrapping = false;
            microwaveDisplay.enableAutoSizing = true;
            microwaveDisplay.fontSizeMin = 0.05f;
            microwaveDisplay.fontSizeMax = 1f;
            microwaveDisplay.rectTransform.sizeDelta = new Vector2(w * 0.17f, h * 0.12f);
            go.transform.position = b.center - t.right * (w * 0.36f) + t.up * (h * 0.22f) + t.forward * (d * 0.5f + 0.004f);
            go.transform.rotation = Quaternion.LookRotation(-t.forward, Vector3.up); // readable from the front
            if (vnb)
            {
                // In the model's own display window (top of the control panel, right of the door), on a dark screen.
                go.transform.SetParent(t, false);
                go.transform.localPosition = new Vector3(-0.307f, 0.335f, 0.2575f);
                go.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
                microwaveDisplay.rectTransform.sizeDelta = new Vector2(0.066f, 0.03f);
                var screen = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Destroy(screen.GetComponent<Collider>());
                screen.name = "Display screen";
                screen.transform.SetParent(t, false);
                screen.transform.localPosition = new Vector3(-0.307f, 0.335f, 0.2555f);
                screen.transform.localScale = new Vector3(0.074f, 0.036f, 0.002f);
                screen.GetComponent<Renderer>().sharedMaterial = new Material(Shader.Find("Standard")) { color = new Color(0.03f, 0.06f, 0.04f) };
            }
        }
        microwaveDisplay.text = "0:10";
    }

    /// <summary>The VNB microwave (its display and turntable were measured on that model).</summary>
    static bool IsVnbMicrowave(Transform t)
    {
        var mf = t.GetComponent<MeshFilter>();
        return mf && mf.sharedMesh && mf.sharedMesh.name == "Microwave";
    }

    static float Along(Vector3 axis, Vector3 extents) => Mathf.Abs(axis.x) * extents.x + Mathf.Abs(axis.y) * extents.y + Mathf.Abs(axis.z) * extents.z;

    // ------------------------------------------------------------------ cutting

    void StartCutting(Food f)
    {
        stations.TryGetValue(KitchenStation.Kind.Board.ToString(), out var board);
        if (!board) return;
        PlayerCarry.Instance.TakeHeld();
        f.gameObject.SetActive(false);
        CuttingGame.Begin(board.transform, f.ing, quality =>
        {
            f.gameObject.SetActive(true);
            f.cut = true;
            f.quality = quality;
            f.Refresh();
            Chopped(f);
            Give(f);
            GameHUD.Toast(quality >= 0.9f ? $"Perfectly chopped {f.ing.name}!" : quality >= 0.6f ? $"Chopped the {f.ing.name}." : $"Chopped the {f.ing.name}... roughly.", quality >= 0.6f ? GameHUD.Good : GameHUD.Warn);
        }, () =>
        {
            f.gameObject.SetActive(true);
            Give(f);
        });
    }

    // ------------------------------------------------------------------ the recipe card (on screen while dinner is on)

    RectTransform card;
    CanvasGroup cardGroup;
    TextMeshProUGUI cardTitle, cardLines;

    void BuildCard()
    {
        var font = GameHUD.Instance ? GameHUD.Instance.font : null;
        var canvas = UIFactory.CreateCanvas("Recipe card", 52);
        canvas.transform.SetParent(transform, false);
        cardGroup = canvas.gameObject.AddComponent<CanvasGroup>();
        cardGroup.alpha = 0f;
        cardGroup.blocksRaycasts = false;
        card = UIFactory.Rect("Card", canvas.transform);
        UIFactory.Place(card, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(40, 362), new Vector2(320, 270));
        card.localRotation = Quaternion.Euler(0, 0, 1.2f);
        var shadow = UIFactory.Panel("Shadow", card, Color.white);
        shadow.sprite = HudArt.Get("note_shadow");
        UIFactory.Place(shadow.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(6, -8), new Vector2(338, 288));
        var paper = UIFactory.Panel("Paper", card, HudArt.PaperWhite);
        paper.sprite = HudArt.Get("note_paper");
        UIFactory.Fill(paper.rectTransform);
        var tape = UIFactory.Panel("Tape", card, Color.white);
        tape.sprite = HudArt.Get("tape");
        UIFactory.Place(tape.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), new Vector2(0, -4), new Vector2(110, 28));
        cardTitle = UIFactory.Text("Title", card, 22, HudArt.Ink, TextAlignmentOptions.MidlineLeft, font);
        cardTitle.fontStyle = FontStyles.Bold;
        cardTitle.enableWordWrapping = false;
        cardTitle.enableAutoSizing = true;
        cardTitle.fontSizeMin = 14; cardTitle.fontSizeMax = 22;
        UIFactory.Place(cardTitle.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(18, -18), new Vector2(284, 30));
        cardLines = UIFactory.Text("Lines", card, 16, HudArt.InkSoft, TextAlignmentOptions.TopLeft, font);
        cardLines.enableWordWrapping = true;
        cardLines.overflowMode = TextOverflowModes.Ellipsis;
        cardLines.enableAutoSizing = true;
        cardLines.fontSizeMin = 11; cardLines.fontSizeMax = 16;
        cardLines.lineSpacing = -6f;
        UIFactory.Place(cardLines.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(18, -52), new Vector2(286, 204));
    }

    Bounds kitchenArea;
    bool kitchenKnown;

    /// <summary>You're in the kitchen: near its stations, on the ground floor.</summary>
    bool InKitchen()
    {
        if (!kitchenKnown)
        {
            kitchenKnown = true;
            bool any = false;
            foreach (var st in stations.Values)
            {
                if (!st) continue;
                var b = Bounds(st.gameObject);
                if (!any) { kitchenArea = b; any = true; } else kitchenArea.Encapsulate(b);
            }
            kitchenArea.Expand(new Vector3(2.4f, 4f, 2.4f));
            if (!any) kitchenKnown = false;
        }
        var eye = Camera.main ? Camera.main.transform.position : transform.position;
        return kitchenKnown && kitchenArea.Contains(eye) && eye.y < kitchenArea.center.y + 1.6f;
    }

    void UpdateCard()
    {
        if (!cardGroup) return;
        bool show = IsActive && !done && !VRHeadset.Worn && !CuttingGame.Running && DayManager.Instance && DayManager.Instance.State == DayManager.Phase.Day
                    && !HudFocus.Full && (InKitchen() || HeldFood() || HeldVessel());
        cardGroup.alpha = Mathf.MoveTowards(cardGroup.alpha, show ? 1f : 0f, Time.unscaledDeltaTime * 4f);
        if (!show || Today == null) return;
        cardTitle.text = "TONIGHT: " + Today.name.ToUpperInvariant();
        var sb = new System.Text.StringBuilder();
        string vessel = VesselName();
        Line(sb, $"{char.ToUpper(vessel[0])}{vessel.Substring(1)} ({(Today.vessel == Vessel.Bowl ? "island" : "stove")}){(Today.vessel == Vessel.Pot ? " + water" : "")}", vesselPlaced);
        foreach (var i in items)
        {
            string how = (i.ing.frozen ? "defrost, " : "") + (i.ing.cuts > 0 ? "chop" : "whole");
            Line(sb, $"{char.ToUpper(i.ing.name[0])}{i.ing.name.Substring(1)} - {i.ing.store.ToString().ToLowerInvariant()}, {how}", i.added);
        }
        Line(sb, Today.seasoning == "salt" ? "Salt (pantry)" : "Oil (pantry)", seasoned);
        Line(sb, Today.cookSeconds > 0f ? $"Cook {Mathf.RoundToInt(Today.cookSeconds)}s, take off in time" : "Toss at the island", done);
        sb.Append("\n<color=#E07B12><b>Next:</b> ").Append(NextStep()).Append("</color>");
        cardLines.text = sb.ToString();
    }

    static void Line(System.Text.StringBuilder sb, string text, bool done) =>
        sb.Append(done ? "<color=#2E9E44>v</color> <s>" + text + "</s>" : "-  " + text).Append('\n');

    // ------------------------------------------------------------------ helpers

    public static float K => HouseLayout.Instance ? HouseLayout.Instance.scale * 0.85f : 1.1f;

    public static Bounds Bounds(GameObject go)
    {
        var rs = go.GetComponentsInChildren<Renderer>();
        if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
        var b = rs[0].bounds;
        foreach (var r in rs) b.Encapsulate(r.bounds);
        return b;
    }

    /// <summary>Scales a model so its biggest side is 'size' metres.</summary>
    public static void Fit(GameObject go, float size)
    {
        var b = Bounds(go);
        float biggest = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
        if (biggest > 0.0001f) go.transform.localScale *= size / biggest;
    }
}

/// <summary>A kitchen spot you use with E (fridge, freezer, pantry, cookware cupboard, sink, cutting board, microwave, stove, island).</summary>
public class KitchenStation : MonoBehaviour, IInteractable, IPromptProvider
{
    public enum Kind { Fridge, Freezer, Pantry, Cookware, Sink, Board, Microwave, Stove, Island }
    public Kind kind;

    public string Prompt
    {
        get
        {
            if (kind == Kind.Sink)
            {
                var b = BucketSink.HeldBucket;
                if (b && b.HasWater && !b.Pouring) return "Empty the bucket in the sink";
            }
            return Cooking.Instance ? Cooking.Instance.PromptFor(kind) : null;
        }
    }

    public void Interact()
    {
        if (kind == Kind.Sink && BucketSink.Empty(transform)) return;
        if (Cooking.Instance) Cooking.Instance.InteractWith(kind);
    }
}
