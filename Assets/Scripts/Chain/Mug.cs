using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A drink you left out on a table, desk or counter (a mug of tea, a glass of juice). The cat loves swatting them off the edge:
/// it smashes on the floor and leaves a spill to mop up. Put out at the start of the day (and now and then later on).
/// </summary>
public class Mug : MonoBehaviour
{
    public static readonly List<Mug> All = new List<Mug>();

    public bool Falling { get; private set; }
    public float Size { get; private set; }
    Color drink;

    void OnEnable() => All.Add(this);
    void OnDisable() => All.Remove(this);

    static readonly Color[] Cups = { new Color(0.93f, 0.93f, 0.9f), new Color(0.85f, 0.3f, 0.25f), new Color(0.3f, 0.45f, 0.75f), new Color(0.95f, 0.78f, 0.25f), new Color(0.35f, 0.6f, 0.4f) };
    static readonly Color[] Drinks = { new Color(0.42f, 0.24f, 0.12f), new Color(0.95f, 0.6f, 0.12f), new Color(0.55f, 0.75f, 0.95f, 0.8f), new Color(0.6f, 0.15f, 0.25f) };

    /// <summary>A mug (or glass) standing at 'at' (the top of a table).</summary>
    public static Mug Create(Vector3 at, float scale)
    {
        bool glass = Random.value < 0.35f;
        var go = new GameObject(glass ? "Glass (drink)" : "Mug (drink)");
        go.transform.position = at;
        go.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
        var m = go.AddComponent<Mug>();
        m.Size = scale;
        m.drink = Drinks[Random.Range(0, Drinks.Length)];
        float h = glass ? 0.12f : 0.1f, r = glass ? 0.035f : 0.045f;
        var cup = glass ? Mat(new Color(0.8f, 0.92f, 1f, 0.35f), true) : Mat(Cups[Random.Range(0, Cups.Length)], false);
        Part(PrimitiveType.Cylinder, go.transform, new Vector3(0f, h * 0.5f, 0f), new Vector3(r * 2f, h * 0.5f, r * 2f), cup, scale);
        Part(PrimitiveType.Cylinder, go.transform, new Vector3(0f, h * 0.82f, 0f), new Vector3(r * 1.8f, 0.004f, r * 1.8f), Mat(m.drink, m.drink.a < 1f), scale);
        if (!glass) Part(PrimitiveType.Cube, go.transform, new Vector3(r + 0.012f, h * 0.55f, 0f), new Vector3(0.018f, 0.055f, 0.012f), cup, scale);
        return m;
    }

    /// <summary>Knocked off: tumbles down to 'land', smashes, and the drink goes everywhere.</summary>
    public IEnumerator Fall(Vector3 land)
    {
        if (Falling) yield break;
        Falling = true;
        Vector3 start = transform.position;
        Quaternion from = transform.rotation;
        Vector3 axis = Random.onUnitSphere;
        float time = Mathf.Lerp(0.35f, 0.6f, Mathf.Clamp01((start.y - land.y) / 1.2f));
        for (float t = 0f; t < time; t += Time.deltaTime)
        {
            float k = t / time;
            Vector3 flat = Vector3.Lerp(start, land, k);
            transform.position = new Vector3(flat.x, Mathf.Lerp(start.y, land.y, k * k) + Mathf.Sin(k * Mathf.PI) * 0.1f * Size, flat.z);
            transform.rotation = Quaternion.AngleAxis(k * 540f, axis) * from;
            yield return null;
        }
        Smash(land);
    }

    void Smash(Vector3 at)
    {
        Fx.Play(Fx.Thud, at, 0.9f, 1.6f);
        Fx.Play(Fx.Click, at, 1f, 0.5f);
        Fx.Splash(at, 10, Size);
        FloatingWord.Spawn("SMASH!", at + Vector3.up * 0.35f * Size, new Color(0.9f, 0.2f, 0.15f), GameHUD.Instance ? GameHUD.Instance.font : null, 1.6f, 1.3f);
        // shards skitter off and fade
        var mat = GetComponentInChildren<Renderer>() ? GetComponentInChildren<Renderer>().sharedMaterial : null;
        for (int i = 0; i < 7; i++)
        {
            var shard = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(shard.GetComponent<Collider>());
            shard.transform.position = at + Vector3.up * 0.02f * Size;
            shard.transform.localScale = new Vector3(Random.Range(0.015f, 0.035f), 0.006f, Random.Range(0.015f, 0.03f)) * Size;
            shard.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
            if (mat) shard.GetComponent<Renderer>().sharedMaterial = mat;
            var sh = shard.AddComponent<Shard>();
            sh.velocity = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward * Random.Range(0.3f, 0.9f) * Size;
            sh.floorY = at.y + 0.004f * Size;
        }
        Spill.Create(at, Random.Range(0.75f, 1.0f) * Size, "spilled drink");
        Destroy(gameObject);
    }

    static void Part(PrimitiveType shape, Transform parent, Vector3 pos, Vector3 size, Material m, float s)
    {
        var p = GameObject.CreatePrimitive(shape);
        Destroy(p.GetComponent<Collider>());
        p.transform.SetParent(parent, false);
        p.transform.localPosition = pos * s;
        p.transform.localScale = size * s;
        p.GetComponent<Renderer>().sharedMaterial = m;
    }

    static Material Mat(Color c, bool seeThrough)
    {
        var m = new Material(Shader.Find("Standard")) { color = c };
        m.SetFloat("_Glossiness", 0.55f);
        if (seeThrough)
        {
            m.SetFloat("_Mode", 3f);
            m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
            m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetInt("_ZWrite", 0);
            m.EnableKeyword("_ALPHAPREMULTIPLY_ON");
            m.renderQueue = 3000;
        }
        return m;
    }

    /// <summary>A bit of broken mug sliding to a stop, then fading away.</summary>
    class Shard : MonoBehaviour
    {
        public Vector3 velocity;
        public float floorY;
        float age;

        void Update()
        {
            age += Time.deltaTime;
            transform.position += velocity * Time.deltaTime;
            velocity = Vector3.MoveTowards(velocity, Vector3.zero, Time.deltaTime * 2.5f);
            var p = transform.position;
            transform.position = new Vector3(p.x, floorY, p.z);
            if (age > 6f) transform.localScale *= 1f - Time.deltaTime * 3f;
            if (age > 7.5f) Destroy(gameObject);
        }
    }
}
