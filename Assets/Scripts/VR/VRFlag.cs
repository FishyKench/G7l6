using TMPro;
using UnityEngine;

/// <summary>
/// A Domination capture point (A, B or C). Stand in the ring to take it: about 5 seconds alone, faster with
/// teammates; nothing happens while both teams are in it (contested). Taking an enemy point first knocks it back
/// to neutral, then makes it yours. Every point your team holds scores points over time (see VRArena).
/// Shows as a glowing ring on the ground, a light beam and a big floating letter in the owner's colour.
/// </summary>
public class VRFlag : MonoBehaviour
{
    public string letter = "A";
    public float radius = 5f;
    [Tooltip("Seconds to capture with one player in the ring.")]
    public float captureTime = 5f;

    /// <summary>-1 = nobody, 0 = your team, 1 = the enemy.</summary>
    public int Owner { get; private set; } = -1;
    /// <summary>The team whose capture is in progress (-1 = none).</summary>
    public int Capturer { get; private set; } = -1;
    /// <summary>0..1 how far the current capture is.</summary>
    public float Progress { get; private set; }
    public readonly int[] Inside = new int[2];
    public bool Contested => Inside[0] > 0 && Inside[1] > 0;

    /// <summary>(flag, new owner, old owner). New owner -1 = it was knocked back to neutral.</summary>
    public event System.Action<VRFlag, int, int> OwnerChanged;

    public Vector3 Position => transform.position;

    Material ringMaterial, arcMaterial, beamMaterial;
    Mesh arcMesh;
    TextMeshPro label;
    Light glow;
    float shownProgress = -1f;
    int shownOwner = -2, shownCapturer = -2;

    public static readonly Color Neutral = new Color(0.85f, 0.85f, 0.9f);

    public static VRFlag Create(Transform parent, string letter, Vector3 position, TMP_FontAsset font)
    {
        var go = new GameObject("Capture point " + letter);
        go.transform.SetParent(parent, true);
        go.transform.position = position;
        var flag = go.AddComponent<VRFlag>();
        flag.letter = letter;
        flag.Build(font);
        return flag;
    }

    public static Color TeamColor(int team) => team == 0 ? VRArena.AllyColor : team == 1 ? VRArena.EnemyColor : Neutral;

    void Build(TMP_FontAsset font)
    {
        ringMaterial = NewGlow();
        arcMaterial = NewGlow();
        beamMaterial = NewGlow();

        var ring = new GameObject("Ring");
        ring.transform.SetParent(transform, false);
        ring.transform.localPosition = Vector3.up * 0.04f;
        ring.AddComponent<MeshFilter>().sharedMesh = Ring(radius - 0.22f, radius, 1f, 64);
        var rr = ring.AddComponent<MeshRenderer>();
        rr.sharedMaterial = ringMaterial;
        rr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        // Inner dashes so the area reads as a zone.
        var dashes = new GameObject("Dashes");
        dashes.transform.SetParent(transform, false);
        dashes.transform.localPosition = Vector3.up * 0.035f;
        dashes.AddComponent<MeshFilter>().sharedMesh = Dashes(radius - 0.9f, radius - 0.75f, 32);
        var dr = dashes.AddComponent<MeshRenderer>();
        dr.sharedMaterial = ringMaterial;
        dr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        var arc = new GameObject("Capture arc");
        arc.transform.SetParent(transform, false);
        arc.transform.localPosition = Vector3.up * 0.05f;
        arcMesh = new Mesh { name = "Capture arc" };
        arc.AddComponent<MeshFilter>().sharedMesh = arcMesh;
        var ar = arc.AddComponent<MeshRenderer>();
        ar.sharedMaterial = arcMaterial;
        ar.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        var beam = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        beam.name = "Beam";
        Destroy(beam.GetComponent<Collider>());
        beam.transform.SetParent(transform, false);
        beam.transform.localPosition = Vector3.up * 9f;
        beam.transform.localScale = new Vector3(0.18f, 9f, 0.18f);
        var br = beam.GetComponent<Renderer>();
        br.sharedMaterial = beamMaterial;
        br.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        label = new GameObject("Letter").AddComponent<TextMeshPro>();
        label.transform.SetParent(transform, false);
        label.transform.localPosition = Vector3.up * 3.6f;
        if (font) label.font = font;
        label.text = letter;
        label.fontSize = 18f;
        label.fontStyle = FontStyles.Bold;
        label.alignment = TextAlignmentOptions.Center;
        label.rectTransform.sizeDelta = new Vector2(4f, 4f);

        glow = new GameObject("Glow").AddComponent<Light>();
        glow.transform.SetParent(transform, false);
        glow.transform.localPosition = Vector3.up * 2f;
        glow.type = LightType.Point;
        glow.range = radius * 2.2f;
        glow.intensity = 1.6f;
        glow.shadows = LightShadows.None;
        Refresh();
    }

    static Material NewGlow()
    {
        var m = new Material(Shader.Find("Standard"));
        m.EnableKeyword("_EMISSION");
        m.SetFloat("_Glossiness", 0.3f);
        return m;
    }

    public void ResetFlag()
    {
        Owner = -1;
        Capturer = -1;
        Progress = 0f;
        Inside[0] = Inside[1] = 0;
        Refresh();
    }

    public bool Contains(Vector3 p)
    {
        Vector3 d = p - transform.position;
        if (Mathf.Abs(d.y) > 3f) return false;
        d.y = 0f;
        return d.sqrMagnitude <= radius * radius;
    }

    /// <summary>A random spot inside the ring (for bots to stand on).</summary>
    public Vector3 RandomSpot(float fraction = 0.6f)
    {
        var r = Random.insideUnitCircle * radius * fraction;
        return transform.position + new Vector3(r.x, 0f, r.y);
    }

    /// <summary>Counts who's in the ring and moves the capture along. Called by the arena every frame of a round.</summary>
    public void Tick(float dt)
    {
        Inside[0] = Inside[1] = 0;
        foreach (var f in VRFighter.All)
            if (f && f.Alive && f.isActiveAndEnabled && (f.team == 0 || f.team == 1) && Contains(f.transform.position))
                Inside[f.team]++;

        int team = Inside[0] > 0 && Inside[1] == 0 ? 0 : Inside[1] > 0 && Inside[0] == 0 ? 1 : -1;
        if (Contested) { /* frozen */ }
        else if (team < 0)
        {
            // Nobody here: a half-done capture slowly drains away.
            Progress = Mathf.MoveTowards(Progress, 0f, dt * 0.15f);
            if (Progress <= 0f) Capturer = -1;
        }
        else if (Owner == team)
        {
            // Defenders standing on their own point undo the enemy's half-capture.
            Progress = Mathf.MoveTowards(Progress, 0f, dt / captureTime * 1.5f);
            if (Progress <= 0f) Capturer = -1;
        }
        else
        {
            float rate = (1f + 0.5f * (Mathf.Min(Inside[team], 3) - 1)) / captureTime;
            if (Capturer != team && Progress > 0f)
            {
                Progress = Mathf.Max(0f, Progress - rate * dt); // first undo the other team's half-capture
            }
            else
            {
                Capturer = team;
                Progress += rate * dt;
                if (Owner >= 0 && Progress >= 0.5f)
                {
                    int old = Owner;
                    Owner = -1; // knocked back to neutral...
                    OwnerChanged?.Invoke(this, -1, old);
                }
                if (Progress >= 1f)
                {
                    int old = Owner;
                    Owner = team; // ...and taken
                    Progress = 0f;
                    Capturer = -1;
                    OwnerChanged?.Invoke(this, team, old);
                }
            }
        }
        Refresh();
    }

    void Update()
    {
        if (label && Camera.main)
        {
            label.transform.rotation = Quaternion.LookRotation(label.transform.position - Camera.main.transform.position);
            label.transform.localPosition = Vector3.up * (3.6f + Mathf.Sin(Time.time * 1.6f) * 0.15f);
        }
        if (glow) glow.intensity = (Contested ? 1.2f + Mathf.PingPong(Time.time * 6f, 1.2f) : 1.6f);
    }

    void Refresh()
    {
        if (!ringMaterial) return;
        if (shownOwner != Owner)
        {
            shownOwner = Owner;
            Color c = TeamColor(Owner);
            ringMaterial.color = c * 0.6f;
            ringMaterial.SetColor("_EmissionColor", c * (Owner < 0 ? 1.1f : 2.2f));
            beamMaterial.color = c * 0.5f;
            beamMaterial.SetColor("_EmissionColor", c * 1.6f);
            if (label) label.color = Color.Lerp(c, Color.white, 0.25f);
            if (glow) glow.color = c;
        }
        if (Mathf.Abs(shownProgress - Progress) > 0.005f || shownCapturer != Capturer)
        {
            shownProgress = Progress;
            shownCapturer = Capturer;
            Color c = TeamColor(Capturer);
            arcMaterial.color = c * 0.6f;
            arcMaterial.SetColor("_EmissionColor", c * 3f);
            Arc(arcMesh, radius - 0.6f, radius - 0.25f, Progress, 64);
        }
    }

    // ------------------------------------------------------------------ meshes

    static Mesh Ring(float inner, float outer, float fraction, int segments)
    {
        var m = new Mesh { name = "Ring" };
        Arc(m, inner, outer, fraction, segments);
        return m;
    }

    static void Arc(Mesh m, float inner, float outer, float fraction, int segments)
    {
        m.Clear();
        int n = Mathf.Max(0, Mathf.CeilToInt(segments * Mathf.Clamp01(fraction)));
        if (n == 0) return;
        var v = new Vector3[(n + 1) * 2];
        var tris = new int[n * 6];
        for (int i = 0; i <= n; i++)
        {
            float a = Mathf.PI * 0.5f - Mathf.Min(i / (float)segments, fraction) * Mathf.PI * 2f; // clockwise from the top
            var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            v[i * 2] = dir * inner;
            v[i * 2 + 1] = dir * outer;
            if (i == n) break;
            int k = i * 6, b = i * 2;
            tris[k] = b; tris[k + 1] = b + 1; tris[k + 2] = b + 2;
            tris[k + 3] = b + 1; tris[k + 4] = b + 3; tris[k + 5] = b + 2;
        }
        m.vertices = v;
        m.triangles = tris;
        var normals = new Vector3[v.Length];
        for (int i = 0; i < normals.Length; i++) normals[i] = Vector3.up;
        m.normals = normals;
        m.RecalculateBounds();
    }

    static Mesh Dashes(float inner, float outer, int count)
    {
        var m = new Mesh { name = "Dashes" };
        var v = new Vector3[count * 4];
        var tris = new int[count * 6];
        for (int i = 0; i < count; i++)
        {
            float a0 = i / (float)count * Mathf.PI * 2f, a1 = a0 + Mathf.PI * 2f / count * 0.45f;
            var d0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0));
            var d1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1));
            int b = i * 4;
            v[b] = d0 * inner; v[b + 1] = d0 * outer; v[b + 2] = d1 * inner; v[b + 3] = d1 * outer;
            int k = i * 6;
            tris[k] = b; tris[k + 1] = b + 2; tris[k + 2] = b + 1;
            tris[k + 3] = b + 1; tris[k + 4] = b + 2; tris[k + 5] = b + 3;
        }
        m.vertices = v;
        m.triangles = tris;
        var normals = new Vector3[v.Length];
        for (int i = 0; i < normals.Length; i++) normals[i] = Vector3.up;
        m.normals = normals;
        m.RecalculateBounds();
        return m;
    }
}
