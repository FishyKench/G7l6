using UnityEngine;

/// <summary>The classes in the VR arena. You pick yours with 1-4 (it applies when you spawn); every bot has one too.</summary>
public enum VRRole { Assault, SMG, Sniper, LMG }

/// <summary>
/// What a class carries and how its gun behaves:
///   ASSAULT - all-rounder rifle, a frag and a flash.
///   SMG     - fast-firing, weak at range, moves faster, two flashes. Rushers.
///   SNIPER  - bolt-action with a scope (hip fire is hopeless): a headshot kills, a body shot takes two. Slower.
///   LMG     - big magazine, steady, slow to move and reload, two frags. Anchors a point.
/// For bots: the same idea, as multipliers on the round's difficulty (see VRArena).
/// </summary>
public struct VRLoadout
{
    public VRRole role;
    public string name, blurb;
    // You
    public float fireRate, damage, reloadTime, spreadStill, spreadMoving, spreadAiming, recoil, aimFov, moveSpeed, gunLength;
    public int magazine, frags, flashes;
    // Bots
    public float botDamage, botFireRate, botSpread, botRun, botDistance, botAggression;
    public int botMagazine;

    public static readonly VRRole[] All = { VRRole.Assault, VRRole.SMG, VRRole.Sniper, VRRole.LMG };

    public static VRLoadout For(VRRole role)
    {
        switch (role)
        {
            case VRRole.SMG:
                return new VRLoadout
                {
                    role = role, name = "SMG", blurb = "fast, close range, quick on your feet",
                    fireRate = 14f, damage = 10f, magazine = 35, reloadTime = 1.1f, spreadStill = 1.1f, spreadMoving = 2.4f, spreadAiming = 0.55f,
                    recoil = 0.5f, aimFov = 62f, moveSpeed = 1.12f, gunLength = 0.72f, frags = 0, flashes = 2,
                    botDamage = 0.75f, botFireRate = 1.6f, botSpread = 1.35f, botRun = 7.1f, botDistance = 7f, botAggression = 0.9f, botMagazine = 32,
                };
            case VRRole.Sniper:
                return new VRLoadout
                {
                    role = role, name = "SNIPER", blurb = "one shot to the head, two to the body - scope with right click",
                    fireRate = 0.9f, damage = 55f, magazine = 5, reloadTime = 2.3f, spreadStill = 5f, spreadMoving = 8f, spreadAiming = 0.01f,
                    recoil = 7f, aimFov = 17f, moveSpeed = 0.92f, gunLength = 1.4f, frags = 0, flashes = 1,
                    botDamage = 9f, botFireRate = 0f, botSpread = 0.22f, botRun = 5.8f, botDistance = 35f, botAggression = 0.15f, botMagazine = 5,
                };
            case VRRole.LMG:
                return new VRLoadout
                {
                    role = role, name = "LMG", blurb = "huge magazine, holds a point - slow to reload",
                    fireRate = 11f, damage = 14f, magazine = 80, reloadTime = 3.2f, spreadStill = 0.9f, spreadMoving = 3.2f, spreadAiming = 0.3f,
                    recoil = 0.65f, aimFov = 48f, moveSpeed = 0.86f, gunLength = 1.2f, frags = 2, flashes = 0,
                    botDamage = 0.95f, botFireRate = 1.25f, botSpread = 1.1f, botRun = 5.4f, botDistance = 19f, botAggression = 0.25f, botMagazine = 70,
                };
            default:
                return new VRLoadout
                {
                    role = VRRole.Assault, name = "ASSAULT", blurb = "all-rounder rifle",
                    fireRate = 9f, damage = 15f, magazine = 30, reloadTime = 1.3f, spreadStill = 0.6f, spreadMoving = 2.4f, spreadAiming = 0.12f,
                    recoil = 0.9f, aimFov = 50f, moveSpeed = 1f, gunLength = 1f, frags = 1, flashes = 1,
                    botDamage = 1f, botFireRate = 1f, botSpread = 1f, botRun = 6.2f, botDistance = 14f, botAggression = 0.55f, botMagazine = 26,
                };
        }
    }
}
