using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Stress = a doodled heart that beats (lub-dub) faster the more stressed you are.
/// Exhaustion = a sweat drop that breathes, and pants when you're tired.
/// </summary>
public class VitalMeter : MonoBehaviour
{
    public enum Kind { Stress, Exhaustion }

    public Kind kind;
    public RectTransform icon;
    public Image fill;
    public TextMeshProUGUI value;
    public TextMeshProUGUI label;
    public Color calmColor = Color.white;
    public Color maxColor = Color.red;

    float shown, clock;
    Vector2 iconHome;
    bool homeSet;

    void Update()
    {
        if (!homeSet) { iconHome = icon.anchoredPosition; homeSet = true; }

        float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
        float t = Time.unscaledTime;

        float target = 0f;
        bool tired = false;
        if (kind == Kind.Stress && StressManager.Instance) target = StressManager.Instance.Normalized;
        if (kind == Kind.Exhaustion && ExhaustionManager.Instance)
        {
            target = ExhaustionManager.Instance.Normalized;
            tired = ExhaustionManager.Instance.IsExhausted;
        }

        shown = Mathf.Lerp(shown, target, 1f - Mathf.Exp(-5f * dt));
        fill.fillAmount = Mathf.Lerp(0.06f, 1f, shown); // always a little liquid at the bottom
        fill.color = Color.Lerp(calmColor, maxColor, shown);
        value.text = Mathf.RoundToInt(target * 100f) + "%";

        Vector2 shake = Vector2.zero;
        if (kind == Kind.Stress)
        {
            // Heartbeat: a strong "lub" then a softer "dub". 65 bpm calm -> 170 bpm panicking.
            float bpm = Mathf.Lerp(65f, 170f, shown);
            clock += dt * bpm / 60f;
            float p = clock % 1f;
            float pulse = Mathf.Exp(-p * 16f) + (p > 0.2f ? 0.6f * Mathf.Exp(-(p - 0.2f) * 16f) : 0f);
            float s = 1f + pulse * Mathf.Lerp(0.06f, 0.16f, shown);
            icon.localScale = new Vector3(s, s, 1f);
            if (shown > 0.75f)
                shake = Random.insideUnitCircle * (shown - 0.75f) * 14f;
            label.text = shown > 0.85f ? "PANIC!" : "STRESS";
            label.color = shown > 0.85f ? HudArt.MarkerRed : HudArt.Ink;
        }
        else
        {
            // Breathing: slow when rested, panting when tired.
            float rate = tired ? 2.6f : Mathf.Lerp(0.35f, 1.6f, shown);
            clock += dt * rate * Mathf.PI * 2f;
            float b = Mathf.Sin(clock);
            float amount = tired ? 0.09f : Mathf.Lerp(0.025f, 0.06f, shown);
            icon.localScale = new Vector3(1f - b * amount * 0.5f, 1f + b * amount, 1f);
            if (tired) shake = new Vector2(0f, Mathf.Abs(Mathf.Sin(clock)) * 3f);
            label.text = tired ? "TIRED!" : "TIRED";
            label.color = tired ? HudArt.MarkerRed : HudArt.Ink;
            if (tired) value.text = "!!";
        }

        icon.anchoredPosition = iconHome + shake;
    }
}
