using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// The automatic cat feeder (ShopNest gadget): a low-poly tank-and-bowl standing against a kitchen wall.
/// Twice a day it drops some kibble; the cat comes to eat and stays calm (no mischief) for about a minute.
/// Built at runtime: it looks for a free spot on the kitchen floor next to a wall (away from doors).
/// </summary>
public class CatFeeder : MonoBehaviour
{
    public const float CalmSeconds = 60f;
    static readonly float[] Hours = { 15.6f, 17.0f };

    public Vector3 StandSpot { get; private set; }
    public Vector3 BowlPoint { get; private set; }

    GameObject kibble;
    Renderer led;
    Material ledGreen, ledAmber;
    int next;
    float kibbleUntil, blinkUntil;

    /// <summary>Builds the feeder in the kitchen. Null if no free spot was found.</summary>
    public static CatFeeder Create()
    {
        var layout = HouseLayout.Instance;
        if (!layout) return null;
        HouseLayout.RoomInfo room = null;
        foreach (var r in layout.rooms) if (r.floor == 0 && r.name == "Kitchen") room = r;
        if (room == null) return null;

        float scale = layout.scale;
        float floorY = room.floor * layout.storeyHeight * scale;
        Vector2 centre = room.rect.center;
        var doors = FindObjectsOfType<HouseDoor>();
        var spots = new List<(Vector3 pos, float yaw, float score)>();

        // Walk round the room along its walls, looking for floor with a wall behind it.
        float inset = 0.5f;
        var plan = new List<Vector2>();
        for (float x = room.rect.xMin + inset; x <= room.rect.xMax - inset; x += 0.4f)
        {
            plan.Add(new Vector2(x, room.rect.yMin + inset));
            plan.Add(new Vector2(x, room.rect.yMax - inset));
        }
        for (float y = room.rect.yMin + inset; y <= room.rect.yMax - inset; y += 0.4f)
        {
            plan.Add(new Vector2(room.rect.xMin + inset, y));
            plan.Add(new Vector2(room.rect.xMax - inset, y));
        }

        foreach (var p in plan)
        {
            Vector3 top = layout.transform.TransformPoint(new Vector3(p.x, 0f, p.y));
            top.y = floorY + 1.6f * scale;
            if (!ProcBuild.FloorAt(top, floorY, 0.12f, out Vector3 floor)) continue;

            Vector3 inward = layout.transform.TransformDirection(new Vector3(centre.x - p.x, 0f, centre.y - p.y));
            inward.y = 0f;
            // Snap to the nearest of the four room directions (the walls run along them).
            inward = Mathf.Abs(inward.x) > Mathf.Abs(inward.z) ? new Vector3(Mathf.Sign(inward.x), 0f, 0f) : new Vector3(0f, 0f, Mathf.Sign(inward.z));
            if (!Physics.Raycast(floor + Vector3.up * 0.25f, -inward, out RaycastHit wall, 0.9f, ~0, QueryTriggerInteraction.Ignore)) continue;
            if (Mathf.Abs(wall.normal.y) > 0.3f || wall.collider.GetComponentInParent<Carryable>()) continue;

            Vector3 pos = new Vector3(wall.point.x, floor.y, wall.point.z) + inward * (0.16f + 0.025f);
            float yaw = Mathf.Atan2(inward.x, inward.z) * Mathf.Rad2Deg;
            var rot = Quaternion.Euler(0f, yaw, 0f);
            if (!ProcBuild.FloorAt(pos + Vector3.up * 1f, floorY, 0.12f, out pos)) continue;

            // Room for the feeder and the cat in front of it?
            if (ProcBuild.Blocked(pos + inward * 0.22f, new Vector3(0.42f, 0.34f, 0.62f), rot, 0.03f)) continue;
            Vector3 stand = pos + inward * 0.62f;
            if (!NavMesh.SamplePosition(stand, out var nav, 0.5f, NavMesh.AllAreas) || Mathf.Abs(nav.position.y - floor.y) > 0.2f) continue;

            bool nearDoor = false;
            foreach (var d in doors)
            {
                Vector3 off = d.transform.position - pos;
                off.y = 0f;
                if (off.magnitude < 1.5f) nearDoor = true;
            }
            if (nearDoor) continue;
            spots.Add((pos, yaw, Random.Range(0f, 1f)));
        }
        if (spots.Count == 0)
        {
            Debug.LogWarning("[Shop] No free spot for the cat feeder in the kitchen.");
            return null;
        }
        spots.Sort((a, b) => b.score.CompareTo(a.score));
        return Build(spots[0].pos, spots[0].yaw);
    }

    static CatFeeder Build(Vector3 floor, float yaw)
    {
        var go = new GameObject("Cat feeder");
        go.transform.SetPositionAndRotation(floor, Quaternion.Euler(0f, yaw, 0f));
        var t = go.transform;

        var body = ProcBuild.Mat(new Color(0.9f, 0.9f, 0.88f), 0.3f);
        var glass = ProcBuild.Mat(new Color(0.62f, 0.78f, 0.86f), 0.7f);
        var dark = ProcBuild.Mat(new Color(0.2f, 0.22f, 0.26f), 0.25f);
        var kib = ProcBuild.Mat(new Color(0.6f, 0.36f, 0.16f), 0.1f);
        var steel = ProcBuild.Mat(new Color(0.78f, 0.8f, 0.84f), 0.6f);
        var green = ProcBuild.Mat(new Color(0.15f, 0.8f, 0.4f), 0.2f, new Color(0.1f, 0.9f, 0.35f));
        var amber = ProcBuild.Mat(new Color(1f, 0.65f, 0.1f), 0.2f, new Color(1f, 0.55f, 0.05f) * 1.4f);

        ProcBuild.Cube(t, "Base plate", new Vector3(0f, 0.015f, 0.105f), new Vector3(0.3f, 0.03f, 0.51f), body);
        ProcBuild.Cyl(t, "Tank", new Vector3(0f, 0.03f + 0.13f - 0.002f, 0f), 0.2f, 0.26f, glass);
        ProcBuild.Cyl(t, "Kibble level", new Vector3(0f, 0.03f + 0.07f, 0f), 0.206f, 0.14f, kib);
        ProcBuild.Cyl(t, "Lid", new Vector3(0f, 0.3f, 0f), 0.22f, 0.035f, dark);
        ProcBuild.Cyl(t, "Lid knob", new Vector3(0f, 0.3f + 0.0175f + 0.01f, 0f), 0.05f, 0.03f, body);
        ProcBuild.Cube(t, "Chute", new Vector3(0f, 0.105f, 0.14f), new Vector3(0.08f, 0.07f, 0.1f), dark);
        ProcBuild.Cyl(t, "Bowl", new Vector3(0f, 0.03f + 0.025f - 0.002f, 0.235f), 0.2f, 0.05f, steel);
        ProcBuild.Cyl(t, "Bowl rim", new Vector3(0f, 0.062f, 0.235f), 0.215f, 0.012f, dark);
        var kibbleGo = ProcBuild.Cyl(t, "Kibble", new Vector3(0f, 0.081f, 0.235f), 0.16f, 0.012f, kib);
        kibbleGo.SetActive(false);
        var ledGo = ProcBuild.Ball(t, "LED", new Vector3(0f, 0.2f, 0.101f), 0.022f, green);

        var col = go.AddComponent<BoxCollider>();
        col.center = new Vector3(0f, 0.16f, 0.1f);
        col.size = new Vector3(0.3f, 0.32f, 0.5f);

        ProcBuild.Carve(go, col.center, col.size);
        var feeder = go.AddComponent<CatFeeder>();
        feeder.kibble = kibbleGo;
        feeder.led = ledGo.GetComponent<Renderer>();
        feeder.ledGreen = green;
        feeder.ledAmber = amber;
        feeder.BowlPoint = go.transform.TransformPoint(new Vector3(0f, 0f, 0.235f));
        // The cat eats from in front of the bowl.
        Vector3 stand = go.transform.TransformPoint(new Vector3(0f, 0f, 0.55f));
        feeder.StandSpot = NavMesh.SamplePosition(stand, out var hit, 0.6f, NavMesh.AllAreas) ? hit.position : stand;

        // Skip meals that are already in the past when the feeder is installed mid-day.
        var day = DayManager.Instance;
        if (day) while (feeder.next < Hours.Length && day.Hour >= Hours[feeder.next]) feeder.next++;
        return feeder;
    }

    void Update()
    {
        if (PauseMenu.IsPaused) return;
        var day = DayManager.Instance;
        if (day && day.State == DayManager.Phase.Day && next < Hours.Length && day.Hour >= Hours[next])
        {
            next++;
            Dispense();
        }
        if (kibble && kibble.activeSelf && Time.time > kibbleUntil) kibble.SetActive(false);
        if (led) led.sharedMaterial = Time.time < blinkUntil && Mathf.Repeat(Time.time, 0.5f) < 0.25f ? ledAmber : ledGreen;
    }

    /// <summary>Drops the kibble and calls the cat.</summary>
    public void Dispense()
    {
        if (kibble) kibble.SetActive(true);
        kibbleUntil = Time.time + 30f;
        blinkUntil = Time.time + 4f;
        Fx.Play(Fx.Beep, transform.position + Vector3.up * 0.3f, 0.6f, 1.2f);
        GameHUD.Toast("Cat feeder: dinner time for " + CatBrain.Name + "!", GameHUD.Info);
        if (CatBrain.Instance) CatBrain.Instance.CallToFeeder(StandSpot, BowlPoint, CalmSeconds);
    }
}
