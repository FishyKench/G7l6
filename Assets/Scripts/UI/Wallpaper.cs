using UnityEngine;

/// <summary>
/// The PC's wallpaper, painted in code once: a deep navy-to-violet sky with flowing aurora ribbons (cyan, pink,
/// violet), a soft glow, a few faint stars and a gentle vignette. Shared by the desktop and the desk monitor.
/// </summary>
public static class Wallpaper
{
    static Texture2D texture;

    public static Texture2D Texture
    {
        get
        {
            if (!texture) texture = PcSketch.PaperWallpaper;
            return texture;
        }
    }

    static Texture2D Paint(int w, int h)
    {
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { name = "Wallpaper", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        var px = new Color32[w * h];
        var rng = new System.Random(7);
        Color navy = new Color(0.04f, 0.05f, 0.14f), violet = new Color(0.17f, 0.07f, 0.30f), deep = new Color(0.02f, 0.03f, 0.09f);
        Color cyan = new Color(0.22f, 0.85f, 0.95f), pink = new Color(1f, 0.32f, 0.72f), purple = new Color(0.55f, 0.38f, 1f);

        for (int y = 0; y < h; y++)
        {
            float v = (float)y / (h - 1);
            for (int x = 0; x < w; x++)
            {
                float u = (float)x / (w - 1);

                // Sky: dark at the bottom, violet towards the top left.
                Color c = Color.Lerp(deep, Color.Lerp(navy, violet, Smooth(0.1f, 1.1f, (1f - u) * 0.55f + v * 0.6f)), Smooth(-0.1f, 0.7f, v));

                // Aurora ribbons: soft bands that wave across the screen.
                float fadeSides = Smooth(0f, 0.35f, u) * Smooth(1.05f, 0.55f, u);
                c += Ribbon(v, 0.47f + 0.10f * Mathf.Sin(u * 5.0f + 0.6f) + 0.035f * Mathf.Sin(u * 13f + 1.3f), 0.045f + 0.02f * Mathf.Sin(u * 3.1f), cyan, 0.85f) * (0.35f + 0.65f * fadeSides);
                c += Ribbon(v, 0.60f + 0.08f * Mathf.Sin(u * 4.2f + 2.1f), 0.075f, pink, 0.42f) * (0.3f + 0.7f * Smooth(0.2f, 0.9f, u));
                c += Ribbon(v, 0.34f + 0.06f * Mathf.Sin(u * 6.3f + 4.0f), 0.11f, purple, 0.38f);
                // Thin bright core inside the cyan ribbon.
                c += Ribbon(v, 0.47f + 0.10f * Mathf.Sin(u * 5.0f + 0.6f) + 0.035f * Mathf.Sin(u * 13f + 1.3f), 0.008f, Color.white, 0.35f) * fadeSides;

                // A soft glow behind it all.
                float gx = u - 0.72f, gy = (v - 0.62f) * 1.6f;
                c += pink * 0.18f * Mathf.Exp(-(gx * gx + gy * gy) / 0.06f);
                float hx = u - 0.2f, hy = (v - 0.25f) * 1.6f;
                c += cyan * 0.08f * Mathf.Exp(-(hx * hx + hy * hy) / 0.05f);

                // Faint stars up top.
                if (v > 0.55f && rng.NextDouble() < 0.0012) c += Color.white * (0.25f + 0.5f * (float)rng.NextDouble()) * Smooth(0.55f, 0.9f, v);

                // Vignette and a touch of grain (stops colour banding).
                float dx = u - 0.5f, dy = v - 0.5f;
                c *= 1f - 0.55f * (dx * dx + dy * dy);
                float grain = ((float)rng.NextDouble() - 0.5f) * (2.5f / 255f);
                c.r += grain; c.g += grain; c.b += grain;
                c.a = 1f;
                px[y * w + x] = c;
            }
        }
        tex.SetPixels32(px);
        tex.Apply();
        return tex;
    }

    static Color Ribbon(float v, float centre, float width, Color colour, float strength)
    {
        float d = (v - centre) / Mathf.Max(0.001f, width);
        return colour * (strength * Mathf.Exp(-d * d));
    }

    static float Smooth(float a, float b, float x)
    {
        float t = Mathf.Clamp01((x - a) / (b - a));
        return t * t * (3f - 2f * t);
    }
}
