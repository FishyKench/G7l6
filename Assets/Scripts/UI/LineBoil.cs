using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Cycles between slightly different hand-drawn versions of a sprite a few times a second
/// ("line boil", like in hand-drawn cartoons), so doodles never sit perfectly still.
/// </summary>
[RequireComponent(typeof(Image))]
public class LineBoil : MonoBehaviour
{
    public Sprite[] frames;
    public float framesPerSecond = 7f;

    Image image;
    float clock;
    int index;

    void Awake()
    {
        image = GetComponent<Image>();
        clock = Random.value; // don't let every doodle boil in sync
    }

    void Update()
    {
        if (frames == null || frames.Length < 2 || framesPerSecond <= 0f) return;
        clock += Time.unscaledDeltaTime * framesPerSecond;
        if (clock < 1f) return;
        clock -= 1f;

        // never repeat the same frame twice in a row
        int next = Random.Range(0, frames.Length - 1);
        if (next >= index) next++;
        index = next;
        if (frames[index]) image.sprite = frames[index];
    }
}
