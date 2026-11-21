using UnityEngine;

/// <summary>
/// You, inside the VR arena: a fast first-person body with a rifle.
///   WASD move (with momentum), SHIFT sprint (wider view, stronger bob), SPACE jump - or vault over low cover
///   without losing speed when there's some in front of you, CTRL/C while sprinting slides.
///   Hold left click to fire (30 rounds, R reload), hold right click to aim down the sights (zoom, red dot,
///   tighter shots, slower moving). Headshots do double damage.
///   The camera reacts: bob, lean into strafes, shake on jumps / hard landings / shots, dip on landing.
/// Classes (1-4, applies when you spawn - or straight away in the first seconds after spawning): ASSAULT, SMG,
/// SNIPER (right click = scope), LMG - see VRLoadout. G throws a frag, Q a flashbang (counts depend on the class).
/// When you're not wearing the headset ('controlled' is off) your avatar just stands there - AFK.
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class VRPlayer : VRFighter
{
    [Header("Moving")]
    public float walkSpeed = 6.5f;
    public float sprintSpeed = 9.5f;
    public float aimingSpeed = 4.2f;
    public float slideSpeed = 12.5f;
    public float slideTime = 0.75f;
    public float groundAcceleration = 55f;
    public float airAcceleration = 12f;
    public float jumpHeight = 1.15f;
    public float gravity = -24f;

    [Header("View")]
    public float fieldOfView = 80f;
    public float sprintFieldOfView = 90f;
    public float aimFieldOfView = 50f;

    [Header("Rifle")]
    public float fireRate = 9f;
    public float damage = 15f;
    public float headshotMultiplier = 2f;
    public int magazine = 30;
    public float reloadTime = 1.3f;
    [Tooltip("Degrees of spread: standing / moving / aiming down the sights.")]
    public float spreadStill = 0.6f, spreadMoving = 2.4f, spreadAiming = 0.12f;
    public float recoilKick = 0.9f;

    public bool controlled;
    public Camera Cam { get; private set; }
    /// <summary>Your class now, and the one you'll have next time you spawn.</summary>
    public VRRole Role { get; private set; } = VRRole.Assault;
    public VRRole NextRole { get; private set; } = VRRole.Assault;
    public VRLoadout Loadout { get; private set; } = VRLoadout.For(VRRole.Assault);
    public int Frags { get; private set; }
    public int Flashes { get; private set; }
    /// <summary>0..1 how blinded you are by a flashbang.</summary>
    public float FlashAmount => Mathf.Clamp01((flashUntil - Time.time) / Mathf.Max(0.1f, flashFade));
    /// <summary>Looking through the sniper scope.</summary>
    public bool Scoped => Role == VRRole.Sniper && Aim > 0.85f && Alive;
    public float SpawnedAt { get; private set; } = -10f;
    public int Ammo { get; private set; }
    public bool Reloading => reloadLeft > 0f;
    public float ReloadProgress => Reloading ? 1f - reloadLeft / reloadTime : 1f;
    /// <summary>0 = hip fire, 1 = fully aimed down the sights.</summary>
    public float Aim { get; private set; }
    /// <summary>0..1 how fast you're going compared to a sprint (for speed lines).</summary>
    public float SpeedAmount { get; private set; }
    public bool Sprinting { get; private set; }
    public bool Sliding => slideLeft > 0f;
    public int Kills { get; set; }
    public float lastHitTime = -10f, lastKillTime = -10f, lastHurtTime = -10f;
    public bool lastHitWasHead;
    public Vector3 lastHurtFrom;
    public float invulnerableUntil;

    CharacterController cc;
    Transform head, gunHolder, gun, muzzle, flash;
    Light muzzleLight;
    float yaw, pitch, recoil, kick, verticalSpeed, fireCooldown, reloadLeft, bob, deathTilt, lean;
    Vector3 velocity;
    bool wasGrounded = true;
    float airborneSpeed, landDip, landDipVelocity, trauma, fovPulse, sensitivity = -1f;
    float slideLeft, eyeHeight = 1.62f;
    // Vaulting
    bool vaulting;
    float vaultT, vaultTime, vaultSpeed;
    Vector3 vaultFrom, vaultApex, vaultTo;
    int streak;
    float lastStreakKill = -10f;
    float flashUntil, flashFade = 1f, throwCooldown, baseWalk = -1f, baseSprint, baseAimMove;
    Transform scope;

    public override Vector3 AimPoint => transform.position + Vector3.up * (Sliding ? 0.6f : 1.15f);
    /// <summary>Is this point (where a shot hit you) your head? (The top of your body, whether standing or sliding.)</summary>
    public bool IsHead(Vector3 point) => cc && point.y > transform.position.y + cc.center.y + cc.height * 0.5f - 0.32f;
    public override Vector3 EyePoint => Cam ? Cam.transform.position : transform.position + Vector3.up * 1.6f;
    protected override bool CanBeHurt => Time.time >= invulnerableUntil;

    public static VRPlayer Create(Transform parent, Camera cameraTemplate)
    {
        var go = new GameObject("You (VR)");
        go.transform.SetParent(parent, false);
        var p = go.AddComponent<VRPlayer>();
        p.Build(cameraTemplate);
        return p;
    }

    void Build(Camera template)
    {
        fighterName = "You";
        team = 0;
        cc = GetComponent<CharacterController>();
        cc.height = 1.8f;
        cc.radius = 0.35f;
        cc.center = new Vector3(0f, 0.9f, 0f);
        cc.stepOffset = 0.4f;
        cc.slopeLimit = 50f;

        head = new GameObject("Head").transform;
        head.SetParent(transform, false);
        head.localPosition = new Vector3(0f, eyeHeight, 0f);

        // The arena scene can provide a camera with post effects (bloom, colour); otherwise make a plain one.
        if (template)
        {
            Cam = template;
            Cam.transform.SetParent(head, false);
            Cam.transform.localPosition = Vector3.zero;
            Cam.transform.localRotation = Quaternion.identity;
            Cam.gameObject.SetActive(true);
        }
        else
        {
            var camGo = new GameObject("VR Camera");
            camGo.transform.SetParent(head, false);
            Cam = camGo.AddComponent<Camera>();
            Cam.clearFlags = CameraClearFlags.Skybox;
            Cam.backgroundColor = new Color(0.03f, 0.02f, 0.07f);
        }
        Cam.fieldOfView = fieldOfView;
        Cam.nearClipPlane = 0.03f;
        Cam.enabled = false;
        var ears = Cam.GetComponent<AudioListener>();
        if (!ears) ears = Cam.gameObject.AddComponent<AudioListener>();
        ears.enabled = false;

        BuildGun();
        Ammo = magazine;
        Health = maxHealth;
    }

    void BuildGun()
    {
        gunHolder = new GameObject("Gun holder").transform;
        gunHolder.SetParent(Cam.transform, false);
        gunHolder.localPosition = HipPosition;
        gun = new GameObject("Rifle").transform;
        gun.SetParent(gunHolder, false);
        var dark = VRArena.Solid(new Color(0.1f, 0.1f, 0.13f), 0.7f);
        var mid = VRArena.Solid(new Color(0.2f, 0.21f, 0.26f), 0.6f);
        var neon = VRArena.Glow(new Color(0.1f, 0.5f, 0.6f), new Color(0.3f, 1f, 0.9f), 2.2f);
        GunPart(new Vector3(0f, 0f, 0.02f), new Vector3(0.07f, 0.09f, 0.55f), dark);          // body
        GunPart(new Vector3(0f, -0.09f, -0.07f), new Vector3(0.05f, 0.15f, 0.07f), mid);      // grip
        GunPart(new Vector3(0f, -0.07f, 0.12f), new Vector3(0.045f, 0.13f, 0.08f), mid);      // magazine
        GunPart(new Vector3(0.036f, 0.005f, 0.08f), new Vector3(0.004f, 0.018f, 0.3f), neon);   // glow strips on the sides
        GunPart(new Vector3(-0.036f, 0.005f, 0.08f), new Vector3(0.004f, 0.018f, 0.3f), neon);
        GunPart(new Vector3(0f, -0.015f, 0.34f), new Vector3(0.035f, 0.035f, 0.16f), dark);   // barrel
        GunPart(new Vector3(0f, 0.02f, -0.27f), new Vector3(0.06f, 0.1f, 0.14f), mid);        // stock
        // Red-dot sight on top: a base and two open frames you look THROUGH when aiming (the dot is on the HUD).
        GunPart(new Vector3(0f, 0.052f, -0.02f), new Vector3(0.05f, 0.012f, 0.09f), dark);
        SightFrame(new Vector3(0f, 0.115f, -0.055f), 0.056f, 0.006f, dark);
        SightFrame(new Vector3(0f, 0.115f, 0.015f), 0.06f, 0.007f, dark);

        scope = new GameObject("Scope").transform;
        scope.SetParent(gun, false);
        foreach (var (pos, size) in new[] { (new Vector3(0f, 0.125f, 0.02f), new Vector3(0.045f, 0.045f, 0.3f)), (new Vector3(0f, 0.09f, 0.02f), new Vector3(0.02f, 0.03f, 0.06f)) })
        {
            var s = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(s.GetComponent<Collider>());
            s.transform.SetParent(scope, false);
            s.transform.localPosition = pos;
            s.transform.localScale = size;
            s.GetComponent<Renderer>().sharedMaterial = dark;
            s.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
        scope.gameObject.SetActive(false);

        muzzle = new GameObject("Muzzle").transform;
        muzzle.SetParent(gun, false);
        muzzle.localPosition = new Vector3(0f, -0.015f, 0.44f);
        muzzleLight = muzzle.gameObject.AddComponent<Light>();
        muzzleLight.type = LightType.Point;
        muzzleLight.color = new Color(1f, 0.8f, 0.45f);
        muzzleLight.range = 5f;
        muzzleLight.intensity = 0f;
        var f = GameObject.CreatePrimitive(PrimitiveType.Quad);
        Destroy(f.GetComponent<Collider>());
        f.name = "Muzzle flash";
        f.transform.SetParent(muzzle, false);
        f.transform.localScale = Vector3.one * 0.22f;
        f.GetComponent<Renderer>().sharedMaterial = VRArena.Glow(new Color(1f, 0.85f, 0.5f), new Color(1f, 0.7f, 0.3f), 8f);
        flash = f.transform;
        flash.gameObject.SetActive(false);
    }

    static readonly Vector3 HipPosition = new Vector3(0.22f, -0.2f, 0.42f);
    static readonly Vector3 AimPosition = new Vector3(0f, -0.115f, 0.3f);

    /// <summary>A square ring (four thin bars) - see-through in the middle.</summary>
    void SightFrame(Vector3 centre, float size, float bar, Material m)
    {
        float h = size * 0.5f - bar * 0.5f;
        GunPart(centre + new Vector3(0f, h, 0f), new Vector3(size, bar, 0.008f), m);
        GunPart(centre + new Vector3(0f, -h, 0f), new Vector3(size, bar, 0.008f), m);
        GunPart(centre + new Vector3(h, 0f, 0f), new Vector3(bar, size, 0.008f), m);
        GunPart(centre + new Vector3(-h, 0f, 0f), new Vector3(bar, size, 0.008f), m);
        // Posts holding the frame up off the base.
        GunPart(centre + new Vector3(h, -size * 0.5f - 0.01f, 0f), new Vector3(bar, 0.02f, 0.008f), m);
        GunPart(centre + new Vector3(-h, -size * 0.5f - 0.01f, 0f), new Vector3(bar, 0.02f, 0.008f), m);
    }

    void GunPart(Vector3 pos, Vector3 size, Material m)
    {
        var p = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Destroy(p.GetComponent<Collider>());
        p.transform.SetParent(gun, false);
        p.transform.localPosition = pos;
        p.transform.localScale = size;
        var r = p.GetComponent<Renderer>();
        r.sharedMaterial = m;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    /// <summary>Pick your class (applies when you spawn - straight away if you only just spawned).</summary>
    public void ChooseRole(VRRole role)
    {
        NextRole = role;
        if (Alive && Time.time - SpawnedAt < 4f && Role != role) { ApplyLoadout(); Ammo = magazine; reloadLeft = 0f; }
    }

    void ApplyLoadout()
    {
        if (baseWalk < 0f) { baseWalk = walkSpeed; baseSprint = sprintSpeed; baseAimMove = aimingSpeed; }
        Role = NextRole;
        Loadout = VRLoadout.For(Role);
        var l = Loadout;
        fireRate = l.fireRate; damage = l.damage; magazine = l.magazine; reloadTime = l.reloadTime;
        spreadStill = l.spreadStill; spreadMoving = l.spreadMoving; spreadAiming = l.spreadAiming;
        recoilKick = l.recoil; aimFieldOfView = l.aimFov;
        walkSpeed = baseWalk * l.moveSpeed; sprintSpeed = baseSprint * l.moveSpeed; aimingSpeed = baseAimMove * l.moveSpeed;
        Frags = l.frags;
        Flashes = l.flashes;
        if (gun) gun.localScale = new Vector3(Role == VRRole.LMG ? 1.25f : 1f, Role == VRRole.LMG ? 1.15f : 1f, l.gunLength);
        if (scope) scope.gameObject.SetActive(Role == VRRole.Sniper);
    }

    public override void Blind(float seconds)
    {
        if (Time.time + seconds > flashUntil) { flashUntil = Time.time + seconds; flashFade = seconds; }
        Shake(0.2f);
    }

    /// <summary>G: frag, Q: flashbang. Thrown where you look, a bit upwards.</summary>
    void Throwing(float dt)
    {
        throwCooldown -= dt;
        if (throwCooldown > 0f || vaulting) return;
        VRGrenade.Kind? kind = null;
        if (Input.GetKeyDown(KeyCode.G) && Frags > 0) { kind = VRGrenade.Kind.Frag; Frags--; }
        else if (Input.GetKeyDown(KeyCode.Q) && Flashes > 0) { kind = VRGrenade.Kind.Flash; Flashes--; }
        if (kind == null) return;
        throwCooldown = 0.7f;
        var look = Cam.transform;
        Vector3 from = look.position + look.forward * 0.5f + look.right * 0.15f - look.up * 0.05f;
        Vector3 v = look.forward * 17f + Vector3.up * 3.5f + Flat(velocity) * 0.6f;
        VRGrenade.Throw(kind.Value, this, from, v);
        kick = 1.5f;
        Shake(0.06f);
    }

    public override void Respawn(Vector3 position, Quaternion rotation)
    {
        ApplyLoadout();
        SpawnedAt = Time.time;
        flashUntil = 0f;
        cc.enabled = false;
        base.Respawn(position, rotation);
        cc.enabled = true;
        yaw = rotation.eulerAngles.y;
        pitch = 0f;
        recoil = kick = 0f;
        verticalSpeed = 0f;
        velocity = Vector3.zero;
        deathTilt = 0f;
        Ammo = magazine;
        reloadLeft = 0f;
        slideLeft = 0f;
        vaulting = false;
        SetCrouched(false);
        invulnerableUntil = Time.time + 1.2f;
        ApplyLook(0f);
    }

    /// <summary>Camera shake: 0..1 (it decays by itself).</summary>
    public void Shake(float amount) => trauma = Mathf.Clamp01(trauma + amount);

    /// <summary>Called by the arena for your kills. Returns how many kills in a row (for "DOUBLE KILL").</summary>
    public int RegisterKill()
    {
        Kills++;
        streak = Time.time - lastStreakKill < 4f ? streak + 1 : 1;
        lastStreakKill = Time.time;
        return streak;
    }

    // ------------------------------------------------------------------ every frame

    void Update()
    {
        if (PauseMenu.IsPaused) return;
        float dt = Time.deltaTime;
        if (muzzleLight.intensity > 0f) muzzleLight.intensity = Mathf.Max(0f, muzzleLight.intensity - dt * 70f);
        if (flash.gameObject.activeSelf && muzzleLight.intensity < 1f) flash.gameObject.SetActive(false);

        if (!Alive)
        {
            if (controlled)
                for (int i = 0; i < VRLoadout.All.Length; i++)
                    if (Input.GetKeyDown(KeyCode.Alpha1 + i) || Input.GetKeyDown(KeyCode.Keypad1 + i)) NextRole = VRLoadout.All[i];
            deathTilt = Mathf.MoveTowards(deathTilt, 1f, dt * 2f);
            head.localPosition = new Vector3(0f, Mathf.Lerp(eyeHeight, 0.4f, deathTilt), 0f);
            Cam.transform.localRotation = Quaternion.Euler(pitch, 0f, 35f * deathTilt);
            gunHolder.gameObject.SetActive(false);
            Cam.fieldOfView = Mathf.Lerp(Cam.fieldOfView, fieldOfView, dt * 6f);
            return;
        }
        gunHolder.gameObject.SetActive(true);
        Regenerate(dt);
        bool briefing = VRArena.Instance && VRArena.Instance.State == VRArena.Mode.Briefing;
        if (controlled && !briefing)
        {
            for (int i = 0; i < VRLoadout.All.Length; i++)
                if (Input.GetKeyDown(KeyCode.Alpha1 + i) || Input.GetKeyDown(KeyCode.Keypad1 + i)) ChooseRole(VRLoadout.All[i]);
            Throwing(dt);
        }

        if (vaulting) { VaultStep(dt); CameraFeel(dt, 0f, true); return; }

        bool grounded = cc.isGrounded;
        Vector2 input = Vector2.zero;
        bool aimHeld = false, sprintHeld = false, jump = false, slide = false;
        if (controlled && !briefing)
        {
            if (sensitivity < 0f) sensitivity = HouseSensitivity();
            float sens = sensitivity * Mathf.Lerp(1f, Role == VRRole.Sniper ? 0.3f : 0.55f, Aim);
            yaw += Input.GetAxisRaw("Mouse X") * sens;
            pitch -= Input.GetAxisRaw("Mouse Y") * sens;
            pitch = Mathf.Clamp(pitch, -85f, 85f);
            input.x = (Input.GetKey(KeyCode.D) ? 1f : 0f) - (Input.GetKey(KeyCode.A) ? 1f : 0f);
            input.y = (Input.GetKey(KeyCode.W) ? 1f : 0f) - (Input.GetKey(KeyCode.S) ? 1f : 0f);
            aimHeld = Input.GetMouseButton(1) && !Reloading;
            sprintHeld = Input.GetKey(KeyCode.LeftShift) && input.y > 0.1f && !aimHeld;
            jump = Input.GetKeyDown(KeyCode.Space);
            slide = Input.GetKeyDown(KeyCode.LeftControl) || Input.GetKeyDown(KeyCode.C);
        }
        else sensitivity = -1f; // pick up changes from the pause menu next time

        Aim = Mathf.MoveTowards(Aim, aimHeld ? 1f : 0f, dt * 7f);
        Vector3 wish = Quaternion.Euler(0f, yaw, 0f) * new Vector3(input.x, 0f, input.y);
        if (wish.sqrMagnitude > 1f) wish.Normalize();

        // Slide: a burst of speed, low to the ground, that fades out.
        if (slide && grounded && Sprinting && !Sliding)
        {
            slideLeft = slideTime;
            velocity = Flat(wish.sqrMagnitude > 0.01f ? wish : transform.forward) * slideSpeed;
            SetCrouched(true);
            Shake(0.12f);
            fovPulse += 6f;
        }
        if (Sliding)
        {
            slideLeft -= dt;
            velocity = Vector3.MoveTowards(velocity, Vector3.zero, dt * 9f);
            if (slideLeft <= 0f || velocity.magnitude < walkSpeed * 0.6f) { slideLeft = 0f; SetCrouched(false); }
        }
        else
        {
            Sprinting = sprintHeld;
            float speed = Aim > 0.5f ? aimingSpeed : Sprinting ? sprintSpeed : walkSpeed;
            Vector3 target = wish * speed;
            float accel = grounded ? groundAcceleration : airAcceleration;
            velocity = Vector3.MoveTowards(velocity, target, accel * dt);
        }

        // Jump - or vault, if there's low cover right in front of you.
        if (jump && grounded)
        {
            if (TryVault()) { CameraFeel(dt, input.x, true); return; }
            verticalSpeed = Mathf.Sqrt(jumpHeight * -2f * gravity);
            if (Sliding) { slideLeft = 0f; SetCrouched(false); } // slide-jump keeps the speed
            Shake(0.1f);
        }

        if (grounded && verticalSpeed < 0f) verticalSpeed = -2f;
        verticalSpeed += gravity * dt;
        if (!grounded) airborneSpeed = Mathf.Min(airborneSpeed, verticalSpeed);
        cc.Move((velocity + Vector3.up * verticalSpeed) * dt);

        // Landing: a dip and a thump, bigger the harder you land.
        bool nowGrounded = cc.isGrounded;
        if (nowGrounded && !wasGrounded)
        {
            float impact = -airborneSpeed;
            if (impact > 4f)
            {
                landDipVelocity -= Mathf.Min(impact * 0.12f, 2.2f);
                Shake(Mathf.Min(impact * 0.025f, 0.45f));
            }
            airborneSpeed = 0f;
        }
        wasGrounded = nowGrounded;

        float horizontal = Flat(velocity).magnitude;
        SpeedAmount = Mathf.Clamp01((horizontal - walkSpeed * 0.8f) / (slideSpeed - walkSpeed * 0.8f));
        if (controlled && !briefing) Shooting(dt, horizontal > 1f);
        CameraFeel(dt, input.x, nowGrounded);
    }

    // ------------------------------------------------------------------ camera feel

    void CameraFeel(float dt, float strafe, bool grounded)
    {
        trauma = Mathf.MoveTowards(trauma, 0f, dt * 1.6f);
        float shake = trauma * trauma;
        float t = Time.time * 22f;
        Vector3 shakeRot = new Vector3(Mathf.PerlinNoise(t, 0.1f) - 0.5f, Mathf.PerlinNoise(0.3f, t) - 0.5f, Mathf.PerlinNoise(t, 0.7f) - 0.5f) * 9f * shake;

        // Landing dip (a little spring).
        landDipVelocity += (-landDip * 120f - landDipVelocity * 14f) * dt;
        landDip += landDipVelocity * dt;

        // Head bob and lean.
        float horizontal = Flat(velocity).magnitude;
        bool moving = grounded && horizontal > 0.5f && !Sliding;
        bob += dt * (moving ? Mathf.Lerp(7f, 12f, SpeedAmount) : 0f);
        float bobAmount = moving ? Mathf.Lerp(0.03f, 0.065f, SpeedAmount) * (1f - Aim * 0.8f) : 0f;
        float bobY = Mathf.Sin(bob * 2f) * bobAmount;
        float bobX = Mathf.Cos(bob) * bobAmount * 0.6f;
        lean = Mathf.Lerp(lean, -strafe * (Sliding ? 5f : 2.2f), dt * 8f);

        float eye = Sliding ? 0.95f : eyeHeight;
        head.localPosition = Vector3.Lerp(head.localPosition, new Vector3(0f, eye, 0f), dt * 12f);
        Cam.transform.localPosition = new Vector3(bobX, bobY + landDip, 0f);
        ApplyLook(lean, shakeRot);

        // Field of view: wider when fast, narrow when aiming, a kick on slides/vaults.
        fovPulse = Mathf.MoveTowards(fovPulse, 0f, dt * 25f);
        float targetFov = Mathf.Lerp(Mathf.Lerp(fieldOfView, sprintFieldOfView, SpeedAmount), aimFieldOfView, Aim) + fovPulse;
        Cam.fieldOfView = Mathf.Lerp(Cam.fieldOfView, targetFov, dt * 10f);

        // The gun: sways with movement, comes to the middle when aiming, kicks back when firing.
        recoil = Mathf.MoveTowards(recoil, 0f, dt * 9f);
        kick = Mathf.MoveTowards(kick, 0f, dt * 6f);
        Vector3 hold = Vector3.Lerp(HipPosition, AimPosition, Aim);
        float sway = (1f - Aim) * bobAmount * 0.6f;
        float reloadDip = Reloading ? Mathf.Sin(ReloadProgress * Mathf.PI) * 35f : 0f;
        gunHolder.localPosition = hold + new Vector3(Mathf.Cos(bob) * sway, Mathf.Abs(Mathf.Sin(bob)) * sway - SpeedAmount * 0.03f * (1f - Aim), -kick * 0.06f);
        gunHolder.localRotation = Quaternion.Euler(-kick * 4f + reloadDip + SpeedAmount * 10f * (1f - Aim), SpeedAmount * -18f * (1f - Aim), reloadDip * 0.4f + lean * 2f);
        if (Scoped) gunHolder.gameObject.SetActive(false);
    }

    void ApplyLook(float roll, Vector3 shake = default)
    {
        transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        head.localRotation = Quaternion.Euler(pitch - recoil + shake.x, shake.y, roll + shake.z);
        Cam.transform.localRotation = Quaternion.identity;
    }

    void SetCrouched(bool low)
    {
        if (!cc) return;
        cc.height = low ? 1.0f : 1.8f;
        cc.center = new Vector3(0f, low ? 0.5f : 0.9f, 0f);
    }

    // ------------------------------------------------------------------ vaulting

    bool TryVault()
    {
        Vector3 fwd = Flat(transform.forward);
        if (Vector3.Dot(Flat(velocity), fwd) < 1f) return false; // only when moving forward
        Vector3 feet = transform.position;
        RaycastHit hit;
        bool blocked = Physics.Raycast(feet + Vector3.up * 0.5f, fwd, out hit, 1.3f, ~0, QueryTriggerInteraction.Ignore)
                       || Physics.Raycast(feet + Vector3.up * 1.0f, fwd, out hit, 1.3f, ~0, QueryTriggerInteraction.Ignore);
        if (!blocked || hit.collider.GetComponentInParent<VRFighter>()) return false;

        // How tall is it?
        Vector3 over = hit.point + fwd * 0.25f;
        if (!Physics.Raycast(new Vector3(over.x, feet.y + 2.4f, over.z), Vector3.down, out var top, 2.4f, ~0, QueryTriggerInteraction.Ignore)) return false;
        float height = top.point.y - feet.y;
        if (height < 0.3f || height > 1.7f) return false;

        // Thin (go over) or deep (land on top)?
        Vector3 beyond = hit.point + fwd * 1.4f;
        float landY = top.point.y;
        bool overIt = Physics.Raycast(new Vector3(beyond.x, feet.y + 2.4f, beyond.z), Vector3.down, out var ground, 4f, ~0, QueryTriggerInteraction.Ignore)
                      && ground.point.y < top.point.y - 0.3f;
        Vector3 end = overIt ? new Vector3(beyond.x, ground.point.y, beyond.z) : new Vector3(over.x, landY, over.z) + fwd * 0.45f;
        if (Physics.CheckCapsule(end + Vector3.up * 0.45f, end + Vector3.up * 1.5f, 0.3f, ~0, QueryTriggerInteraction.Ignore)) return false; // no room there

        vaulting = true;
        vaultT = 0f;
        vaultFrom = feet;
        vaultTo = end;
        vaultApex = new Vector3((feet.x + end.x) * 0.5f, top.point.y + 0.35f, (feet.z + end.z) * 0.5f);
        vaultSpeed = Mathf.Max(Flat(velocity).magnitude, walkSpeed);
        vaultTime = Mathf.Clamp(Vector3.Distance(feet, end) / Mathf.Max(vaultSpeed, 7f), 0.22f, 0.45f);
        cc.enabled = false;
        Shake(0.12f);
        fovPulse += 5f;
        landDipVelocity -= 0.6f;
        if (Sliding) { slideLeft = 0f; SetCrouched(false); }
        return true;
    }

    void VaultStep(float dt)
    {
        vaultT += dt / vaultTime;
        float t = Mathf.Clamp01(vaultT);
        Vector3 a = Vector3.Lerp(vaultFrom, vaultApex, t), b = Vector3.Lerp(vaultApex, vaultTo, t);
        transform.position = Vector3.Lerp(a, b, t);
        if (t < 1f) return;
        vaulting = false;
        cc.enabled = true;
        // Keep the speed you came in with.
        velocity = Flat(transform.forward) * vaultSpeed;
        verticalSpeed = -1f;
        wasGrounded = true;
    }

    // ------------------------------------------------------------------ the rifle

    void Shooting(float dt, bool moving)
    {
        fireCooldown -= dt;
        if (Reloading)
        {
            reloadLeft -= dt;
            if (reloadLeft <= 0f) { reloadLeft = 0f; Ammo = magazine; }
            return;
        }
        if ((Input.GetKeyDown(KeyCode.R) && Ammo < magazine) || Ammo <= 0) { reloadLeft = reloadTime; Aim = 0f; return; }
        if ((Sprinting && Aim < 0.1f && !Sliding) || !Input.GetMouseButton(0) || fireCooldown > 0f) return;

        fireCooldown = 1f / fireRate;
        Ammo--;
        float spread = Mathf.Lerp(moving ? spreadMoving : spreadStill, spreadAiming, Aim);
        var look = Cam.transform;
        Vector3 dir = Quaternion.AngleAxis(Random.Range(-spread, spread), look.up)
                      * Quaternion.AngleAxis(Random.Range(-spread, spread), look.right) * look.forward;
        recoil = Mathf.Min(recoil + recoilKick * Mathf.Lerp(1f, 0.55f, Aim), Role == VRRole.Sniper ? 9f : 6f);
        if (Role == VRRole.Sniper) Shake(0.15f);
        kick = 1f;
        Shake(0.035f);
        muzzleLight.intensity = 3.5f;
        flash.gameObject.SetActive(true);
        flash.localRotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
        flash.localScale = Vector3.one * Random.Range(0.16f, 0.26f);
        if (VRArena.Instance) VRArena.Instance.FireShot(this, look.position, dir, damage, new Color(0.4f, 1f, 0.95f), muzzle.position);
    }

    /// <summary>Called by the arena when one of your shots lands.</summary>
    public void ShotLanded(bool head, bool killed)
    {
        lastHitTime = Time.time;
        lastHitWasHead = head;
        if (killed) lastKillTime = Time.time;
    }

    protected override void OnHurt(float amount, VRFighter from, bool headshot)
    {
        lastHurtTime = Time.time;
        if (from) lastHurtFrom = from.transform.position;
        Shake(0.12f + amount * 0.01f);
    }

    static float HouseSensitivity()
    {
        var cam = FindObjectOfType<PlayerCam>(true);
        return cam ? cam.sensitivity : 1.5f;
    }

    static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }
}
