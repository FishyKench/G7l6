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
    public IReadOnlyList<DirtyClothesPiece> Pieces => scattered;
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
    public void ClothesPutAway()
    {
        if (!IsActive || phase != Phase.PutAway) return;
        Complete("Laundry done - clothes put away!");
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
        Destroy(piece.gameObject);
        collected++;
        EnsureArmful();
        ShowLumps(collected);
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
        var colours = new[] { new Color(0.85f, 0.85f, 0.88f), new Color(0.25f, 0.35f, 0.6f), new Color(0.75f, 0.25f, 0.3f), new Color(0.3f, 0.55f, 0.4f), new Color(0.2f, 0.2f, 0.24f), new Color(0.9f, 0.7f, 0.3f) };
        for (int i = 0; i < Mathf.Max(6, pieces); i++)
        {
            var lump = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(lump.GetComponent<Collider>());
            lump.transform.SetParent(go.transform, false);
            float a = i * 2.4f, r = 0.06f + 0.035f * (i % 3);
            lump.transform.localPosition = new Vector3(Mathf.Cos(a) * r, 0.05f + i * 0.035f, Mathf.Sin(a) * r);
            lump.transform.localScale = new Vector3(0.24f, 0.1f, 0.2f);
            lump.transform.localRotation = Quaternion.Euler(Random.Range(-15f, 15f), Random.Range(0f, 360f), Random.Range(-15f, 15f));
            var m = new Material(Shader.Find("Standard")) { color = colours[i % colours.Length] };
            m.SetFloat("_Glossiness", 0.05f);
            lump.GetComponent<Renderer>().sharedMaterial = m;
            armfulLumps.Add(lump);
        }
        var box = go.AddComponent<BoxCollider>();
        box.center = new Vector3(0f, 0.15f, 0f);
        box.size = new Vector3(0.45f, 0.3f, 0.45f);
        armful = go.AddComponent<Carryable>();
        armful.type = ItemType.DirtyClothes;
        armful.displayName = "dirty clothes";
        armful.maxHeldSize = 0.55f;
        var outline = go.AddComponent<Outline>();
        outline.OutlineMode = Outline.Mode.OutlineVisible;
        outline.OutlineColor = Color.white;
        outline.OutlineWidth = 5f;
        outline.enabled = false;
        go.SetActive(false);
    }

    void ShowLumps(int count)
    {
        for (int i = 0; i < armfulLumps.Count; i++)
            if (armfulLumps[i]) armfulLumps[i].SetActive(i < Mathf.Max(1, Mathf.CeilToInt((float)count * armfulLumps.Count / Mathf.Max(1, pieces))));
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
