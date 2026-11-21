using UnityEngine;

/// <summary>RGB lighting: the parts' glow slowly cycles through the rainbow, each a little ahead of the last (a gamer door).</summary>
public class NeonCycle : MonoBehaviour
{
    public Renderer[] parts;
    [Tooltip("Rainbow cycles per second.")]
    public float speed = 0.12f;
    public float brightness = 2.2f;

    MaterialPropertyBlock block;

    void Update()
    {
        if (parts == null) return;
        if (block == null) block = new MaterialPropertyBlock();
        for (int i = 0; i < parts.Length; i++)
        {
            if (!parts[i]) continue;
            float hue = Mathf.Repeat(Time.time * speed + i / (float)Mathf.Max(1, parts.Length), 1f);
            Color c = Color.HSVToRGB(hue, 0.85f, 1f);
            parts[i].GetPropertyBlock(block);
            block.SetColor("_Color", c * 0.5f);
            block.SetColor("_EmissionColor", c * brightness);
            parts[i].SetPropertyBlock(block);
        }
    }
}
