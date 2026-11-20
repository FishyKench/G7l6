using TMPro;
using UnityEngine;

/// <summary>A little word ("z", "purr", "!") that floats up from the cat, faces the camera, wobbles and fades.</summary>
public class FloatingWord : MonoBehaviour
{
    TextMeshPro text;
    Vector3 velocity;
    float life, age, wobblePhase;

    public static void Spawn(string word, Vector3 position, Color color, TMP_FontAsset font, float size = 1.6f, float lifetime = 1.6f)
    {
        var go = new GameObject("Floating " + word);
        go.transform.position = position;
        var fw = go.AddComponent<FloatingWord>();
        fw.text = go.AddComponent<TextMeshPro>();
        if (font) fw.text.font = font;
        fw.text.text = word;
        fw.text.fontSize = size;
        fw.text.color = color;
        fw.text.alignment = TextAlignmentOptions.Center;
        fw.text.enableWordWrapping = false;
        fw.text.rectTransform.sizeDelta = new Vector2(3f, 1f);
        fw.life = lifetime;
        fw.velocity = new Vector3(Random.Range(-0.1f, 0.1f), Random.Range(0.25f, 0.4f), Random.Range(-0.1f, 0.1f));
        fw.wobblePhase = Random.value * 6f;
        go.transform.localScale = Vector3.one * 0.6f;
    }

    void Update()
    {
        age += Time.deltaTime;
        float k = age / life;
        if (k >= 1f) { Destroy(gameObject); return; }

        transform.position += velocity * Time.deltaTime;
        float pop = k < 0.15f ? Mathf.Lerp(0.6f, 1.15f, k / 0.15f) : Mathf.Lerp(1.15f, 1f, (k - 0.15f) * 4f);
        transform.localScale = Vector3.one * pop;

        var cam = Camera.main;
        if (cam)
        {
            transform.rotation = Quaternion.LookRotation(transform.position - cam.transform.position, Vector3.up)
                                 * Quaternion.Euler(0f, 0f, Mathf.Sin(age * 5f + wobblePhase) * 12f);
        }

        var c = text.color;
        c.a = k > 0.6f ? Mathf.Lerp(1f, 0f, (k - 0.6f) / 0.4f) : 1f;
        text.color = c;
    }
}
