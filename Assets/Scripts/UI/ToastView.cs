using TMPro;
using UnityEngine;

/// <summary>Short messages ("Fire's out!") written on a torn paper strip that drops in, wobbles, then floats away.</summary>
public class ToastView : MonoBehaviour
{
    public RectTransform body;
    public RectTransform paper;
    public TextMeshProUGUI text;
    public CanvasGroup group;

    bool visible;
    float hideAt, targetRot;
    Spring y = new Spring(160f);
    Spring rot = new Spring(0f);

    public void Show(string message, Color color)
    {
        text.text = message;
        text.color = color;
        float w = text.GetPreferredValues(message).x;
        paper.sizeDelta = new Vector2(Mathf.Clamp(w + 140f, 280f, 1200f), paper.sizeDelta.y);

        if (!visible) { y.value = 170f; y.velocity = 0f; }
        else y.Kick(-500f); // re-drop when a new message replaces the old one
        rot.value = Random.Range(-12f, 12f);
        targetRot = Random.Range(-2.5f, 2.5f);
        visible = true;
        hideAt = Time.unscaledTime + 2.3f + message.Length * 0.02f;
    }

    void Update()
    {
        float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
        float t = Time.unscaledTime;
        if (visible && t > hideAt) visible = false;

        y.Step(visible ? 0f : 60f, dt, visible ? 3.2f : 1.2f, visible ? 0.38f : 1f);
        rot.Step(visible ? targetRot + Mathf.Sin(t * 1.7f) * 1.2f : targetRot + 8f, dt, 3f, 0.35f);
        group.alpha = Mathf.MoveTowards(group.alpha, visible ? 1f : 0f, dt * (visible ? 8f : 2.5f));

        body.anchoredPosition = new Vector2(0f, y.value);
        body.localRotation = Quaternion.Euler(0f, 0f, rot.value);
    }
}
