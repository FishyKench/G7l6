using System.Collections.Generic;
using UnityEngine;

/// <summary>A damped spring: smoothly chases a target with a bit of bounce. Never explodes, even with big dt.</summary>
public struct Spring
{
    public float value;
    public float velocity;

    public Spring(float start) { value = start; velocity = 0f; }

    /// <param name="frequency">How fast it moves (oscillations per second).</param>
    /// <param name="damping">0 = wobbles forever, 1 = no overshoot.</param>
    public float Step(float target, float dt, float frequency = 4f, float damping = 0.45f)
    {
        float omega = frequency * Mathf.PI * 2f;
        float f = 1f + 2f * dt * damping * omega;
        float oo = omega * omega;
        float hoo = dt * oo;
        float hhoo = dt * hoo;
        float detInv = 1f / (f + hhoo);
        float detX = f * value + dt * velocity + hhoo * target;
        float detV = velocity + hoo * (target - value);
        value = detX * detInv;
        velocity = detV * detInv;
        return value;
    }

    public void Kick(float amount) => velocity += amount;
}

/// <summary>Loads the hand-drawn HUD sprites from Resources/HUD.</summary>
public static class HudArt
{
    static readonly Dictionary<string, Sprite> cache = new Dictionary<string, Sprite>();

    public static Sprite Get(string name)
    {
        if (cache.TryGetValue(name, out var s) && s) return s;
        s = Resources.Load<Sprite>("HUD/" + name);
        if (!s) Debug.LogWarning("Missing HUD sprite: Resources/HUD/" + name);
        cache[name] = s;
        return s;
    }

    /// <summary>name_0, name_1, name_2 ... (the hand-drawn versions used for line boil).</summary>
    public static Sprite[] Frames(string name)
    {
        var list = new List<Sprite>();
        for (int i = 0; i < 8; i++)
        {
            var s = Resources.Load<Sprite>($"HUD/{name}_{i}");
            if (!s) break;
            list.Add(s);
        }
        return list.ToArray();
    }

    // Marker palette
    public static readonly Color Ink = new Color32(34, 32, 42, 255);
    public static readonly Color InkSoft = new Color32(34, 32, 42, 175);
    public static readonly Color MarkerRed = new Color32(224, 53, 43, 255);
    public static readonly Color MarkerGreen = new Color32(46, 158, 68, 255);
    public static readonly Color MarkerOrange = new Color32(224, 123, 18, 255);
    public static readonly Color NoteYellow = new Color32(255, 232, 115, 255);
    public static readonly Color NoteBlue = new Color32(158, 216, 255, 255);
    public static readonly Color NoteCoral = new Color32(255, 140, 120, 255);
    public static readonly Color PaperWhite = new Color32(250, 247, 238, 255);
    public static readonly Color Kraft = new Color32(222, 190, 140, 255);
}
