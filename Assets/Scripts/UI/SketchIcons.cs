using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The PC desktop's app icons, drawn like the rest of the PC (the sketchbook wallpaper): a wobbly pen outline, marker colour
/// that doesn't quite stay inside the lines, a soft pencil shadow. Made in code once and kept.
///   homework - a spiral notebook and a pencil        game - a gaming headset over a trophy shield
///   browser  - a globe with a little cursor           store - a treasure chest with pixel coins
///   clips    - a film clapperboard                    cart  - a game cartridge in the game's colour (bought games)
/// </summary>
public static class SketchIcons
{
    const int N = 128;
    static readonly Dictionary<string, Texture2D> made = new Dictionary<string, Texture2D>();
    static readonly Color Ink = new Color(0.17f, 0.15f, 0.22f);

    public static Texture2D Get(string id, Color tint = default)
    {
        string key = id + (tint == default ? "" : ColorUtility.ToHtmlStringRGB(tint));
        if (made.TryGetValue(key, out var tex) && tex) return tex;
        var c = new Pad(key.GetHashCode());
        switch (id)
        {
            case "homework": Homework(c); break;
            case "game": Game(c); break;
            case "browser": Browser(c); break;
            case "store": Store(c); break;
            case "clips": Clips(c); break;
            case "circle": Circle(c); break;
            default: Cart(c, tint == default ? new Color(0.5f, 0.6f, 0.9f) : tint); break;
        }
        tex = c.Bake(key);
        made[key] = tex;
        return tex;
    }

    // ------------------------------------------------------------------ the drawings (128 x 128, y up)

    static void Homework(Pad c)
    {
        var page = Rot(new[] { V(26, 14), V(96, 14), V(96, 110), V(26, 110) }, V(61, 62), -7f);
        c.Shape(page, new Color(0.99f, 0.97f, 0.9f));
        for (int i = 0; i < 6; i++) c.Line(Rot(V(36, 92 - i * 12), V(61, 62), -7f), Rot(V(88, 92 - i * 12), V(61, 62), -7f), 1.2f, new Color(0.45f, 0.62f, 0.9f, 0.8f));
        c.Line(Rot(V(36, 18), V(61, 62), -7f), Rot(V(36, 106), V(61, 62), -7f), 1.2f, new Color(0.9f, 0.35f, 0.35f, 0.8f));
        for (int i = 0; i < 7; i++) c.Ring(Rot(V(26, 24 + i * 13), V(61, 62), -7f), 4.5f, 2f); // the spiral
        // the pencil, lying across the page
        Vector2 a = V(52, 26), b = V(112, 92), dir = (b - a).normalized, side = new Vector2(-dir.y, dir.x) * 7f;
        c.Shape(new[] { a + side, b + side, b - side, a - side }, new Color(0.98f, 0.78f, 0.2f));
        c.Shape(new[] { b + side, b + dir * 10f + side, b + dir * 10f - side, b - side }, new Color(0.95f, 0.55f, 0.65f)); // rubber
        c.Shape(new[] { a + side, a - side, a - dir * 16f }, new Color(0.93f, 0.8f, 0.62f));                         // the wood
        c.Shape(new[] { a - dir * 10f + side * 0.38f, a - dir * 10f - side * 0.38f, a - dir * 16f }, Ink);          // the lead
    }

    static void Game(Pad c)
    {
        // a shield with a star on it...
        var shield = new[] { V(34, 92), V(94, 92), V(94, 56), V(86, 38), V(64, 16), V(42, 38), V(34, 56) };
        c.Shape(shield, new Color(0.47f, 0.33f, 0.85f));
        c.Shape(Star(V(64, 60), 19f, 8f), new Color(1f, 0.82f, 0.25f));
        // ...under a gaming headset
        c.Arc(V(64, 70), 44f, 38f, 10f, 170f, 6f, new Color(0.25f, 0.24f, 0.32f));
        c.Shape(Box(V(8, 50), V(28, 88), 6f), new Color(0.95f, 0.36f, 0.5f));
        c.Shape(Box(V(100, 50), V(120, 88), 6f), new Color(0.95f, 0.36f, 0.5f));
        c.Line(V(22, 54), V(40, 32), 3f, Ink);   // the mic
        c.Ring(V(41, 31), 4f, 2.5f);
    }

    static void Browser(Pad c)
    {
        Vector2 o = V(60, 66);
        c.Ellipse(o, 46f, 46f, new Color(0.45f, 0.72f, 0.95f));
        // land
        c.Shape(new[] { V(30, 84), V(48, 98), V(62, 90), V(56, 74), V(40, 70), V(28, 72) }, new Color(0.45f, 0.78f, 0.4f), false);
        c.Shape(new[] { V(66, 58), V(86, 66), V(94, 48), V(80, 34), V(68, 40) }, new Color(0.45f, 0.78f, 0.4f), false);
        c.Ellipse(o, 46f, 46f, Color.clear, true, true); // (the outline again, on top)
        c.Ellipse(o, 20f, 46f, Color.clear, true, true);
        c.Line(V(14, 66), V(106, 66), 2f, Ink);
        c.Line(V(60, 20), V(60, 112), 2f, Ink);
        // a little mouse pointer
        c.Shape(new[] { V(92, 40), V(92, 6), V(101, 15), V(108, 2), V(114, 6), V(107, 18), V(120, 20) }, Color.white);
    }

    static void Store(Pad c)
    {
        c.Shape(Box(V(16, 14), V(112, 70), 4f), new Color(0.78f, 0.48f, 0.25f));                            // chest
        c.Shape(new[] { V(16, 70), V(112, 70), V(108, 92), V(96, 102), V(32, 102), V(20, 92) }, new Color(0.86f, 0.56f, 0.3f)); // lid
        c.Shape(Box(V(16, 64), V(112, 74), 2f), new Color(0.95f, 0.78f, 0.25f));                            // gold band
        c.Shape(Box(V(54, 40), V(74, 66), 3f), new Color(0.98f, 0.82f, 0.28f));                             // lock
        c.Ring(V(64, 52), 3.5f, 2.5f);
        foreach (var x in new[] { 40f, 64f, 88f }) c.Line(V(x - 10, 26), V(x - 10, 58), 1.4f, new Color(0.45f, 0.26f, 0.12f, 0.6f));
        // pixel coins spilling out of the top
        foreach (var p in new[] { V(36, 110), V(52, 116), V(70, 112), V(88, 118), V(98, 106) })
            c.Shape(Box(p - V(6, 6), p + V(6, 6), 0f), new Color(1f, 0.85f, 0.2f));
    }

    static void Clips(Pad c)
    {
        c.Shape(Box(V(14, 14), V(114, 76), 5f), new Color(0.25f, 0.24f, 0.3f));                // the board
        c.Shape(new[] { V(48, 58), V(48, 30), V(76, 44) }, new Color(0.95f, 0.36f, 0.4f));    // play
        var top = Rot(new[] { V(14, 78), V(114, 78), V(114, 96), V(14, 96) }, V(14, 78), 14f); // the clapper, open
        c.Shape(top, Color.white);
        for (int i = 0; i < 4; i++)
        {
            float x = 24 + i * 24;
            c.Shape(Rot(new[] { V(x, 78), V(x + 10, 78), V(x + 18, 96), V(x + 8, 96) }, V(14, 78), 14f), new Color(0.25f, 0.24f, 0.3f), false);
        }
        c.Shape(top, Color.clear, true, true);
    }

    /// <summary>A loop of red marker, as if someone circled the thing on the screen (goes round a bit more than once).</summary>
    static void Circle(Pad c)
    {
        var red = new Color(0.9f, 0.18f, 0.2f);
        Vector2 prev = Vector2.zero;
        for (int i = 0; i <= 48; i++)
        {
            float t = i / 48f, a = (110f + t * 395f) * Mathf.Deg2Rad;
            float r = 56f - 5f * t + 2.5f * Mathf.Sin(t * 9f); // (a hand never closes it exactly)
            var p = new Vector2(64f + Mathf.Cos(a) * r, 64f + Mathf.Sin(a) * r * 0.92f);
            if (i > 0) c.Line(prev, p, 4.2f, red);
            prev = p;
        }
    }

    static void Cart(Pad c, Color tint)
    {
        c.Shape(new[] { V(24, 12), V(104, 12), V(104, 100), V(92, 112), V(24, 112) }, tint);
        c.Shape(Box(V(36, 46), V(92, 96), 4f), new Color(0.99f, 0.97f, 0.9f));                // label (initials go on top)
        for (int i = 0; i < 5; i++) c.Line(V(36 + i * 12, 14), V(36 + i * 12, 28), 2.4f, Ink); // contacts
    }

    // ------------------------------------------------------------------ shape helpers

    static Vector2 V(float x, float y) => new Vector2(x, y);

    static Vector2 Rot(Vector2 p, Vector2 about, float deg) => about + (Vector2)(Quaternion.Euler(0, 0, deg) * (p - about));
    static Vector2[] Rot(Vector2[] ps, Vector2 about, float deg) { var r = new Vector2[ps.Length]; for (int i = 0; i < ps.Length; i++) r[i] = Rot(ps[i], about, deg); return r; }

    static Vector2[] Box(Vector2 min, Vector2 max, float round)
    {
        if (round <= 0f) return new[] { min, V(max.x, min.y), max, V(min.x, max.y) };
        var pts = new List<Vector2>();
        Vector2[] corners = { V(max.x - round, max.y - round), V(min.x + round, max.y - round), V(min.x + round, min.y + round), V(max.x - round, min.y + round) };
        for (int k = 0; k < 4; k++)
            for (int i = 0; i <= 4; i++)
            {
                float a = (k * 90f + i * 22.5f) * Mathf.Deg2Rad;
                pts.Add(corners[k] + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * round);
            }
        return pts.ToArray();
    }

    static Vector2[] Star(Vector2 o, float r1, float r2)
    {
        var p = new Vector2[10];
        for (int i = 0; i < 10; i++) { float a = (90f + i * 36f) * Mathf.Deg2Rad; p[i] = o + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (i % 2 == 0 ? r1 : r2); }
        return p;
    }

    /// <summary>A 128x128 page to draw on: marker fills a touch off the lines, wobbly pen strokes, a pencil shadow.</summary>
    class Pad
    {
        readonly Color[] px = new Color[N * N];
        readonly float[] shade = new float[N * N];
        readonly float seed;
        public Pad(int hash) { seed = (hash & 0xffff) * 0.013f; }

        public void Shape(Vector2[] pts, Color fill, bool outline = true, bool outlineOnly = false)
        {
            if (!outlineOnly && fill.a > 0f)
            {
                Shadow(pts);
                var off = new Vector2(1.6f, -1.2f); // (the marker missed the lines a little)
                ForEach(pts, off, (i, x, y) =>
                {
                    float streak = 1f - 0.07f * Mathf.Abs(Mathf.Sin((x + y) * 0.32f + seed)); // marker streaks
                    var col = fill * streak; col.a = fill.a;
                    px[i] = Color.Lerp(px[i], col, fill.a);
                });
            }
            if (outline) for (int i = 0; i < pts.Length; i++) Line(pts[i], pts[(i + 1) % pts.Length], 2.6f, Ink);
        }

        public void Ellipse(Vector2 o, float rx, float ry, Color fill, bool outline = true, bool outlineOnly = false)
        {
            var pts = new Vector2[40];
            for (int i = 0; i < pts.Length; i++) { float a = i / (float)pts.Length * Mathf.PI * 2f; pts[i] = o + new Vector2(Mathf.Cos(a) * rx, Mathf.Sin(a) * ry); }
            Shape(pts, fill, outline, outlineOnly);
        }

        public void Ring(Vector2 o, float r, float thick)
        {
            Vector2 prev = o + new Vector2(r, 0f);
            for (int i = 1; i <= 16; i++) { float a = i / 16f * Mathf.PI * 2f; var p = o + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r; Line(prev, p, thick, Ink); prev = p; }
        }

        /// <summary>A thick arc (a headset band): an outlined band between two radii.</summary>
        public void Arc(Vector2 o, float rOuter, float rInner, float fromDeg, float toDeg, float unused, Color fill)
        {
            var pts = new List<Vector2>();
            for (int i = 0; i <= 20; i++) { float a = Mathf.Lerp(fromDeg, toDeg, i / 20f) * Mathf.Deg2Rad; pts.Add(o + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * rOuter); }
            for (int i = 20; i >= 0; i--) { float a = Mathf.Lerp(fromDeg, toDeg, i / 20f) * Mathf.Deg2Rad; pts.Add(o + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * rInner); }
            Shape(pts.ToArray(), fill);
        }

        public void Line(Vector2 a, Vector2 b, float thick, Color col)
        {
            float len = Vector2.Distance(a, b);
            if (len < 0.01f) return;
            Vector2 dir = (b - a) / len, perp = new Vector2(-dir.y, dir.x);
            int steps = Mathf.CeilToInt(len * 1.5f);
            int r = Mathf.CeilToInt(thick + 1f);
            for (int s = 0; s <= steps; s++)
            {
                float t = s / (float)steps;
                // a hand isn't a ruler: drift a little off the straight line, and press a little harder in the middle
                var p = Vector2.Lerp(a, b, t) + perp * (Mathf.PerlinNoise(seed + (a.x + a.y) * 0.1f, t * len * 0.05f) - 0.5f) * 1.6f;
                float w = thick * (0.85f + 0.25f * Mathf.Sin(t * Mathf.PI));
                for (int oy = -r; oy <= r; oy++)
                    for (int ox = -r; ox <= r; ox++)
                    {
                        int x = Mathf.RoundToInt(p.x) + ox, y = Mathf.RoundToInt(p.y) + oy;
                        if (x < 0 || y < 0 || x >= N || y >= N) continue;
                        float d = Vector2.Distance(new Vector2(x, y), p);
                        float k = Mathf.Clamp01(w * 0.5f - d + 0.6f) * col.a;
                        if (k <= 0f) continue;
                        int i = y * N + x;
                        px[i] = Color.Lerp(px[i], new Color(col.r, col.g, col.b, 1f), k);
                        px[i].a = Mathf.Max(px[i].a, k);
                    }
            }
        }

        void Shadow(Vector2[] pts) => ForEach(pts, new Vector2(4f, -4f), (i, x, y) => shade[i] = Mathf.Max(shade[i], 0.22f));

        void ForEach(Vector2[] pts, Vector2 off, System.Action<int, int, int> each)
        {
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            foreach (var p in pts) { minX = Mathf.Min(minX, p.x); minY = Mathf.Min(minY, p.y); maxX = Mathf.Max(maxX, p.x); maxY = Mathf.Max(maxY, p.y); }
            for (int y = Mathf.Max(0, (int)(minY + off.y)); y <= Mathf.Min(N - 1, (int)(maxY + off.y) + 1); y++)
                for (int x = Mathf.Max(0, (int)(minX + off.x)); x <= Mathf.Min(N - 1, (int)(maxX + off.x) + 1); x++)
                    if (Inside(pts, new Vector2(x - off.x + 0.5f, y - off.y + 0.5f))) each(y * N + x, x, y);
        }

        static bool Inside(Vector2[] poly, Vector2 p)
        {
            bool inside = false;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
                if ((poly[i].y > p.y) != (poly[j].y > p.y) && p.x < (poly[j].x - poly[i].x) * (p.y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x)
                    inside = !inside;
            return inside;
        }

        public Texture2D Bake(string name)
        {
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, true) { name = "Icon " + name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear };
            var outPx = new Color[N * N];
            for (int i = 0; i < outPx.Length; i++)
            {
                var c = px[i];
                if (c.a < 0.01f && shade[i] > 0f) c = new Color(Ink.r, Ink.g, Ink.b, shade[i]); // the pencil shadow, only where nothing's drawn
                outPx[i] = c;
            }
            tex.SetPixels(outPx);
            tex.Apply(true);
            return tex;
        }
    }
}
