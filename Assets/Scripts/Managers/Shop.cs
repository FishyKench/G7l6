using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>
/// ShopNest's catalogue and what you own. Saved between sessions (PlayerPrefs).
/// Every item goes through four states:  NotOwned -> Ordered (paid, arrives next morning) -> Delivered (a box
/// waits by the front door) -> Installed (the box was opened and the gadget works).
/// "Next morning" = the next time a day starts (see <see cref="ShopRuntime"/>, which calls <see cref="DeliverOrders"/>).
/// Gadget effects ask <see cref="IsInstalled"/> before they do anything.
/// </summary>
public static class Shop
{
    public enum Category { Home, Streaming, PC }
    public enum State { NotOwned, Ordered, Delivered, Installed }

    // Item ids (use these in effect code: Shop.IsInstalled(Shop.LeakSensor)).
    public const string OvenTimer = "oven_timer", LeakSensor = "leak_sensor", RobotVacuum = "robot_vacuum", CatFeeder = "cat_feeder",
        RemoteHolder = "remote_holder", DoorCamera = "door_camera", Headphones = "headphones",
        Webcam = "webcam_mic", RingLight = "ring_light", GraphicsCard = "gpu_upgrade", StreamingPC = "streaming_pc";

    /// <summary>One thing for sale.</summary>
    public class Item
    {
        public string id, name, blurb;
        public int price;
        public Category category;
        /// <summary>Size of its delivery box in metres (width, height, depth).</summary>
        public Vector3 box;
    }

    public static readonly Item[] Items =
    {
        // ---- Home gadgets
        new Item { id = OvenTimer, name = "Smart oven timer", price = 25, category = Category.Home, box = new Vector3(0.30f, 0.20f, 0.24f),
            blurb = "Beeps before dinner burns and gives you 50% more time to turn the oven off." },
        new Item { id = LeakSensor, name = "Leak sensor", price = 20, category = Category.Home, box = new Vector3(0.24f, 0.16f, 0.22f),
            blurb = "Pings you the moment water drips through the ceiling, and slows the puddle down." },
        new Item { id = RobotVacuum, name = "Robot vacuum", price = 80, category = Category.Home, box = new Vector3(0.52f, 0.22f, 0.52f),
            blurb = "A slow little robot that drives round downstairs, tidies small mess and dries up spills." },
        new Item { id = CatFeeder, name = "Automatic cat feeder", price = 35, category = Category.Home, box = new Vector3(0.36f, 0.42f, 0.32f),
            blurb = "Feeds the cat twice a day. A full tummy keeps Kitty calm for a whole minute." },
        new Item { id = RemoteHolder, name = "Remote holder", price = 15, category = Category.Home, box = new Vector3(0.24f, 0.16f, 0.20f),
            blurb = "A snug caddy for the TV remote. The cat can no longer run off with it." },
        new Item { id = DoorCamera, name = "Door camera", price = 45, category = Category.Home, box = new Vector3(0.30f, 0.20f, 0.24f),
            blurb = "Spots Mom's car coming home and texts you a warning before she walks in." },
        new Item { id = Headphones, name = "Noise-cancelling headphones", price = 60, category = Category.Home, box = new Vector3(0.34f, 0.24f, 0.30f),
            blurb = "Blocks the TV static while you are at the PC or in VR, so it stresses you far less." },

        // ---- Streaming gear
        new Item { id = Webcam, name = "Webcam + mic", price = 40, category = Category.Streaming, box = new Vector3(0.28f, 0.20f, 0.22f),
            blurb = "Lets you go live on LiveLoop. A starter kit: a bit laggy, with only a few viewers." },
        new Item { id = RingLight, name = "Ring light + green screen", price = 60, category = Category.Streaming, box = new Vector3(0.52f, 0.46f, 0.20f),
            blurb = "A pro look for your streams, so more viewers stay and keep watching." },

        // ---- PC upgrades
        new Item { id = GraphicsCard, name = "Graphics card upgrade", price = 120, category = Category.PC, box = new Vector3(0.52f, 0.18f, 0.32f),
            blurb = "A big step up for your gaming PC: smoother streams with about three times the viewers." },
        new Item { id = StreamingPC, name = "Streaming PC + capture card", price = 200, category = Category.PC, box = new Vector3(0.56f, 0.50f, 0.54f),
            blurb = "The top-tier setup: crisp, lag-free live streams that pull in the biggest crowd." },
    };

    public static string CategoryName(Category c) => c == Category.Home ? "Home gadgets" : c == Category.Streaming ? "Streaming gear" : "PC upgrades";

    const string Key = "G7l6.Shop.States";
    static readonly Dictionary<string, State> states = new Dictionary<string, State>();
    static bool loaded;

    /// <summary>Fires when an item's state changes (ordered, delivered, installed, reset).</summary>
    public static event Action Changed;
    /// <summary>An order was just paid (MyMail sends the confirmation).</summary>
    public static event Action<Item> OrderPlaced;
    /// <summary>Orders just became boxes at the front door (MyMail sends the delivery notice).</summary>
    public static event Action<List<Item>> OrdersDelivered;

    public static Item Find(string id)
    {
        foreach (var i in Items) if (i.id == id) return i;
        return null;
    }

    public static State StateOf(string id)
    {
        Load();
        return states.TryGetValue(id, out var s) ? s : State.NotOwned;
    }

    /// <summary>The gadget is installed and working (this is what effect code checks).</summary>
    public static bool IsInstalled(string id) => StateOf(id) == State.Installed;

    public static int Count(State s)
    {
        int n = 0;
        foreach (var i in Items) if (StateOf(i.id) == s) n++;
        return n;
    }

    public static List<Item> InState(State s)
    {
        var list = new List<Item>();
        foreach (var i in Items) if (StateOf(i.id) == s) list.Add(i);
        return list;
    }

    /// <summary>Pays and orders it (arrives next morning). False if you can't afford it or already have it.</summary>
    public static bool Order(Item item)
    {
        if (item == null || StateOf(item.id) != State.NotOwned) return false;
        if (!Wallet.Pay(item.price, "ShopNest - " + item.name, Wallet.Category.Purchase)) return false;
        Set(item.id, State.Ordered);
        OrderPlaced?.Invoke(item);
        return true;
    }

    /// <summary>A new day starts: everything ordered yesterday is now waiting at the front door. Returns how many.</summary>
    public static int DeliverOrders()
    {
        Load();
        var ids = new List<string>();
        foreach (var pair in states) if (pair.Value == State.Ordered) ids.Add(pair.Key);
        foreach (var id in ids) states[id] = State.Delivered;
        if (ids.Count > 0)
        {
            Save();
            Changed?.Invoke();
            var items = new List<Item>();
            foreach (var id in ids) { var it = Find(id); if (it != null) items.Add(it); }
            OrdersDelivered?.Invoke(items);
        }
        return ids.Count;
    }

    /// <summary>The box was opened.</summary>
    public static void Install(string id)
    {
        if (StateOf(id) == State.Delivered) Set(id, State.Installed);
    }

    /// <summary>Debug: install / remove an item instantly (no order, no box).</summary>
    public static void ForceInstall(string id) { if (Find(id) != null) Set(id, State.Installed); }
    public static void ForceRemove(string id) { if (StateOf(id) != State.NotOwned) Set(id, State.NotOwned); }

    /// <summary>Debug: start over (nothing owned).</summary>
    public static void ResetAll()
    {
        PlayerPrefs.DeleteKey(Key);
        states.Clear();
        loaded = true;
        Changed?.Invoke();
    }

    static void Set(string id, State s)
    {
        Load();
        if (s == State.NotOwned) states.Remove(id); else states[id] = s;
        Save();
        Changed?.Invoke();
    }

    static void Load()
    {
        if (loaded) return;
        loaded = true;
        states.Clear();
        foreach (var part in PlayerPrefs.GetString(Key, "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var p = part.Split(':');
            if (p.Length == 2 && int.TryParse(p[1], out int s) && s > 0 && s <= (int)State.Installed && Find(p[0]) != null)
                states[p[0]] = (State)s;
        }
    }

    static void Save()
    {
        var sb = new StringBuilder();
        foreach (var pair in states) sb.Append(pair.Key).Append(':').Append((int)pair.Value).Append(',');
        PlayerPrefs.SetString(Key, sb.ToString());
        PlayerPrefs.Save();
    }
}
