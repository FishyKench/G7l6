using TMPro;
using UnityEngine;

/// <summary>The "[E] do something" hint under the crosshair: a keycap on a paper scrap that bobs, pops and squishes.</summary>
public class PromptView : MonoBehaviour
{
    public RectTransform body;
    public RectTransform paper;
    public RectTransform key;
    public TextMeshProUGUI text;
    public CanvasGroup group;
    public KeyCode pressKey = KeyCode.E;

    string current = "";
    bool visible;
    Spring pop = new Spring(0.8f);
    Spring squish = new Spring(1f);

    public void Set(string prompt)
    {
        if (string.IsNullOrEmpty(prompt)) { visible = false; return; }

        if (prompt != current)
        {
            current = prompt;
            text.text = prompt;
            float w = text.GetPreferredValues(prompt).x;
            paper.sizeDelta = new Vector2(Mathf.Clamp(w + 130f, 200f, 900f), paper.sizeDelta.y);
            if (visible) pop.Kick(2.5f);
            else pop.value = 0.75f;
        }
        visible = true;
    }

    void Update()
    {
        float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
        float t = Time.unscaledTime;

        group.alpha = Mathf.MoveTowards(group.alpha, visible ? 1f : 0f, dt * (visible ? 9f : 6f));
        pop.Step(visible ? 1f : 0.9f, dt, 4f, 0.35f);
        body.localScale = Vector3.one * pop.value;
        body.anchoredPosition = new Vector2(0f, Mathf.Sin(t * 2.4f) * 4f);
        body.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(t * 1.3f) * 1.3f);

        if (visible && Input.GetKeyDown(pressKey)) squish.value = 0.65f; // key gets pressed down
        squish.Step(1f, dt, 5f, 0.3f);
        key.localScale = new Vector3(1f + (1f - squish.value) * 0.5f, squish.value, 1f);
    }
}
