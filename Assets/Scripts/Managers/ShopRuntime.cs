using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Runs ShopNest inside the house (added by the GameManager, one per day / scene load):
///   - "next morning": when a day starts, everything ordered becomes Delivered and its box appears on the floor
///     inside the front door (see <see cref="Shop.DeliverOrders"/>); boxes of earlier deliveries still unopened
///     are put back too. Press E on a box to open it and install the gadget.
///   - builds the gadgets that need something in the house (cat feeder in the kitchen, robot vacuum downstairs)
///     and runs the door camera's texts. The other gadgets (oven timer, leak sensor, remote holder, headphones)
///     live in their own tasks and just check <see cref="Shop.IsInstalled"/>.
/// Everything is made at runtime: no scene edits.
///
/// Editor / development-build keys:  F5 feed the cat now,  F6 +$100,  F7 next morning (deliver all orders now).
/// </summary>
public class ShopRuntime : MonoBehaviour
{
    public static ShopRuntime Instance { get; private set; }

    readonly Dictionary<string, DeliveryBox> boxes = new Dictionary<string, DeliveryBox>();
    CatFeeder feeder;
    RobotVacuum robot;
    bool ready, camFar, camNear;
    int deliveredToday;

    void Awake()
    {
        Instance = this;
        if (Campaign.Once($"d{Campaign.DayIndex}.delivery")) deliveredToday = Shop.DeliverOrders(); // a new day: yesterday's orders arrive (not again on a restart of the same day)
        Shop.Changed += OnShopChanged;
    }

    void OnDestroy()
    {
        Shop.Changed -= OnShopChanged;
        if (Instance == this) Instance = null;
    }

    IEnumerator Start()
    {
        yield return null;
        yield return new WaitForSeconds(0.5f); // let physics and the walk map settle
        ready = true;
        Sync();
        if (deliveredToday > 0)
        {
            yield return new WaitForSeconds(2f);
            GameHUD.Toast(deliveredToday == 1 ? "Delivery! A ShopNest box is waiting by the front door." : $"Delivery! {deliveredToday} ShopNest boxes are waiting by the front door.", GameHUD.Good);
        }
    }

    void OnShopChanged()
    {
        if (ready) Sync();
    }

    void Sync()
    {
        SyncBoxes();
        if (Shop.IsInstalled(Shop.CatFeeder) && !feeder) feeder = CatFeeder.Create();
        if (Shop.IsInstalled(Shop.RobotVacuum) && !robot) robot = RobotVacuum.Create();
        if (Shop.IsInstalled(Shop.ScratchingPost) && !ScratchingPost.Instance) ScratchingPost.Create();
    }

    void Update()
    {
        if (PauseMenu.IsPaused) return;
        DoorCamera();

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (Input.GetKeyDown(KeyCode.F6))
        {
            Wallet.Earn(100f, "[debug] bonus", Wallet.Category.Job);
            GameHUD.Toast("[debug] +$100.00", GameHUD.Info);
        }
        if (Input.GetKeyDown(KeyCode.F7))
        {
            int n = Shop.DeliverOrders();
            Sync();
            GameHUD.Toast(n > 0 ? $"[debug] next morning: {n} box(es) delivered" : "[debug] nothing was ordered", GameHUD.Info);
        }
        if (Input.GetKeyDown(KeyCode.F5) && feeder) feeder.Dispense();
#endif
    }

    // ------------------------------------------------------------------ door camera

    void DoorCamera()
    {
        var day = DayManager.Instance;
        if (!day || day.State != DayManager.Phase.Day || !Shop.IsInstalled(Shop.DoorCamera)) return;
        float h = day.Hour;
        float early = 18f - day.endHour; // Friday: Mom is home earlier
        if (!camFar && h >= 17.6f - early)
        {
            camFar = true;
            if (h < 17.7f - early) Alert("Mom's car is on the road. She'll be home in about 25 minutes.");
        }
        if (!camNear && h >= 17.9f - early)
        {
            camNear = true;
            if (h < 17.97f - early) Alert("Mom's car just pulled up outside! She's coming in!");
        }
    }

    void Alert(string message)
    {
        var day = DayManager.Instance;
        if (day) day.SendText("DOOR CAM", message);
        if (Camera.main) Fx.Play(Fx.Beep, Camera.main.transform.position, 0.7f, 1.1f);
    }

    // ------------------------------------------------------------------ delivery boxes

    void SyncBoxes()
    {
        foreach (var item in Shop.Items)
        {
            bool waiting = Shop.StateOf(item.id) == Shop.State.Delivered;
            boxes.TryGetValue(item.id, out var box);
            if (waiting && !box)
            {
                if (FindBoxSpot(item, out Vector3 floor, out float yaw)) boxes[item.id] = DeliveryBox.Create(item, floor, yaw);
                else Debug.LogWarning("[Shop] No free floor by the front door for the " + item.name + " box.");
            }
            else if (!waiting && box)
            {
                if (!box.Opened) Destroy(box.gameObject); // (an opened box collapses and removes itself)
                boxes.Remove(item.id);
            }
        }
    }

    public static HouseDoor FrontDoor()
    {
        HouseDoor best = null;
        foreach (var d in FindObjectsOfType<HouseDoor>())
        {
            string label = (d.label ?? "").ToLowerInvariant();
            if (label.Contains("front")) return d;
            if (best == null && (d.name.Contains("Outside / Foyer") || d.name.ToLowerInvariant().Contains("front"))) best = d;
        }
        return best;
    }

    /// <summary>A free spot on the floor just inside the front door (out of the doorway and the door's swing).</summary>
    static bool FindBoxSpot(Shop.Item item, out Vector3 floor, out float yaw)
    {
        floor = default;
        yaw = 0f;
        var door = FrontDoor();
        if (!door) return false;

        float scale = Mathf.Max(0.01f, door.transform.lossyScale.x);
        float doorWidth = door.Width * scale;
        Vector3 centre = door.transform.TransformPoint(new Vector3(door.side * door.Width * 0.5f, 0f, 0f));
        Vector3 axis = door.transform.forward;
        axis.y = 0f;
        axis.Normalize();
        Vector3 side = new Vector3(axis.z, 0f, -axis.x);

        // Which side of the door is the house? (the side with a room)
        float inside = 1f;
        var layout = HouseLayout.Instance;
        if (layout)
        {
            bool a = layout.RoomAt(centre + axis * 1.2f, 0) != null, b = layout.RoomAt(centre - axis * 1.2f, 0) != null;
            if (b && !a) inside = -1f;
        }
        Vector3 inward = axis * inside;
        float floorY = door.transform.position.y;
        float clearLateral = doorWidth * 0.5f + Mathf.Max(item.box.x, item.box.z) * 0.5f + 0.2f; // keep the way in clear

        var candidates = new List<(float cost, Vector3 pos)>();
        for (float a = 0.7f; a <= 2.8f; a += 0.45f)
            for (float l = -2.6f; l <= 2.6f; l += 0.45f)
            {
                if (Mathf.Abs(l) < clearLateral) continue;
                Vector3 p = centre + inward * a + side * l;
                Vector3 off = p - door.transform.position;
                off.y = 0f;
                if (off.magnitude < doorWidth + 0.35f) continue; // the door swings here
                candidates.Add((a + 0.45f * Mathf.Abs(l), p));
            }
        candidates.Sort((x, y) => x.cost.CompareTo(y.cost));

        float facing = Mathf.Atan2(inward.x, inward.z) * Mathf.Rad2Deg;
        foreach (var c in candidates)
        {
            if (!ProcBuild.FloorAt(c.pos + Vector3.up * (floorY + 1.8f - c.pos.y), floorY, 0.12f, out Vector3 point)) continue;
            float y = facing + Random.Range(-10f, 10f);
            if (ProcBuild.Blocked(point, item.box, Quaternion.Euler(0f, y, 0f), 0.08f)) continue;
            floor = point;
            yaw = y;
            return true;
        }
        return false;
    }
}
