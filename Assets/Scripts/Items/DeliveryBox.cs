using System.Collections;
using UnityEngine;

/// <summary>
/// A ShopNest delivery box waiting on the floor by the front door. Press E to open it: the gadget is installed
/// ("Leak sensor installed!"). Built from cubes at runtime (cardboard, packing tape, a shipping label); the tape and
/// label sit a few millimetres proud of the box so nothing z-fights.
/// </summary>
public class DeliveryBox : MonoBehaviour, IInteractable, IPromptProvider
{
    public Shop.Item item;
    bool opened;

    static Material cardboard, cardboardDark, tape, label, ink, orange;

    public bool Opened => opened;

    public string Prompt => opened || item == null ? null : "Open the " + item.name + " box";

    public void Interact()
    {
        if (opened || item == null) return;
        opened = true;
        foreach (var c in GetComponentsInChildren<Collider>()) c.enabled = false;
        Shop.Install(item.id);
        GameHUD.Toast(item.name + " installed!", GameHUD.Good);
        Fx.Play(Fx.Thud, transform.position, 0.5f, 1.3f);
        Fx.Play(Fx.Click, transform.position, 0.6f, 0.8f);
        Fx.Puff(transform.position + Vector3.up * item.box.y, new Color(0.85f, 0.72f, 0.5f, 0.7f), 10, 0.5f);
        StartCoroutine(Collapse());
    }

    IEnumerator Collapse()
    {
        Vector3 from = transform.localScale;
        for (float t = 0f; t < 0.22f; t += Time.deltaTime)
        {
            float k = 1f - t / 0.22f;
            transform.localScale = new Vector3(from.x * (1f + 0.15f * (1f - k)), from.y * k, from.z * (1f + 0.15f * (1f - k)));
            yield return null;
        }
        Destroy(gameObject);
    }

    static void Materials()
    {
        if (cardboard) return;
        cardboard = ProcBuild.Mat(new Color(0.74f, 0.56f, 0.34f), 0.05f);
        cardboardDark = ProcBuild.Mat(new Color(0.6f, 0.43f, 0.24f), 0.05f);
        tape = ProcBuild.Mat(new Color(0.8f, 0.68f, 0.45f), 0.55f);
        label = ProcBuild.Mat(new Color(0.96f, 0.95f, 0.92f), 0.1f);
        ink = ProcBuild.Mat(new Color(0.12f, 0.12f, 0.15f), 0.05f);
        orange = ProcBuild.Mat(new Color(1f, 0.55f, 0.16f), 0.1f);
    }

    /// <summary>The box for 'item' standing on the floor at 'floorPoint', its label side facing 'yaw' (degrees).</summary>
    public static DeliveryBox Create(Shop.Item item, Vector3 floorPoint, float yaw)
    {
        Materials();
        float w = item.box.x, h = item.box.y, d = item.box.z;
        var go = new GameObject("Delivery box - " + item.name);
        go.transform.position = floorPoint;
        go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        var t = go.transform;

        ProcBuild.Cube(t, "Cardboard", new Vector3(0f, h * 0.5f, 0f), new Vector3(w, h, d), cardboard);
        // A darker band round the bottom (a few mm bigger than the box so the faces are not coplanar).
        ProcBuild.Cube(t, "Base band", new Vector3(0f, 0.016f, 0f), new Vector3(w + 0.006f, 0.032f, d + 0.006f), cardboardDark);

        // Packing tape: over the top, and a short strip down the front.
        float tw = Mathf.Clamp(w * 0.2f, 0.04f, 0.085f);
        ProcBuild.Cube(t, "Tape (top)", new Vector3(0f, h + 0.004f, 0f), new Vector3(tw, 0.01f, d + 0.008f), tape);
        float sh = h * 0.5f;
        ProcBuild.Cube(t, "Tape (front)", new Vector3(0f, h - 0.005f - sh * 0.5f, d * 0.5f + 0.003f), new Vector3(tw, sh, 0.01f), tape);

        // Shipping label to the right of the tape.
        float left = tw * 0.5f + 0.014f, right = w * 0.5f - 0.02f;
        float lw = right - left, lh = Mathf.Min(h * 0.42f, 0.12f);
        if (lw > 0.05f)
        {
            float cx = (left + right) * 0.5f, cy = h * 0.42f, z = d * 0.5f;
            ProcBuild.Cube(t, "Label", new Vector3(cx, cy, z + 0.0035f), new Vector3(lw, lh, 0.004f), label);
            ProcBuild.Cube(t, "Label logo bar", new Vector3(cx, cy + lh * 0.3f, z + 0.0068f), new Vector3(lw * 0.82f, lh * 0.2f, 0.002f), orange);
            for (int i = 0; i < 6; i++)
            {
                float bx = cx - lw * 0.3f + i * lw * 0.12f;
                ProcBuild.Cube(t, "Barcode", new Vector3(bx, cy - lh * 0.16f, z + 0.0068f), new Vector3(i % 2 == 0 ? 0.006f : 0.003f, lh * 0.34f, 0.002f), ink);
            }
        }

        var box = go.AddComponent<BoxCollider>();
        box.center = new Vector3(0f, h * 0.5f, 0f);
        box.size = new Vector3(w, h, d);
        ProcBuild.Carve(go, box.center, box.size);
        var db = go.AddComponent<DeliveryBox>();
        db.item = item;
        ProcBuild.AddOutline(go);
        return db;
    }
}
