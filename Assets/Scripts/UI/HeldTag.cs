using TMPro;
using UnityEngine;

/// <summary>A luggage tag hanging in the corner showing what you're carrying. Swings when it changes.</summary>
public class HeldTag : MonoBehaviour
{
    public RectTransform body;
    public TextMeshProUGUI item;
    public TextMeshProUGUI hint;
    public CanvasGroup group;

    Carryable last;
    Spring slide = new Spring(1f);
    float swing, swingVelocity;

    void Update()
    {
        float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
        float t = Time.unscaledTime;

        var carry = PlayerCarry.Instance;
        var held = carry ? carry.Held : null;
        if (held != last)
        {
            last = held;
            if (held)
            {
                item.text = held.displayName;
                hint.text = $"[{carry.dropKey}] drop";
                swingVelocity += Random.Range(160f, 240f) * (Random.value < 0.5f ? -1f : 1f);
            }
        }

        slide.Step(held ? 0f : 1f, dt, 2.4f, 0.55f);
        group.alpha = Mathf.Clamp01(1f - slide.value * 1.3f);

        // Pendulum hanging from the tag's hole, with a little breeze.
        float rest = Mathf.Sin(t * 0.7f) * 2f;
        float accel = -38f * (swing - rest) - 3.2f * swingVelocity;
        swingVelocity += accel * dt;
        swing += swingVelocity * dt;

        body.localRotation = Quaternion.Euler(0f, 0f, swing);
        body.anchoredPosition = new Vector2(0f, -slide.value * 200f);
    }
}
