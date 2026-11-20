using UnityEngine;

/// <summary>Gentle idle motion: slow sway and breathing. Put it on anything that should feel alive.</summary>
public class HudBreathe : MonoBehaviour
{
    public float swayDegrees = 1.2f;
    public float swaySpeed = 0.8f;
    public float breatheAmount = 0.015f;
    public float breatheSpeed = 1.4f;
    public float baseRotation;

    float phase;

    void Awake()
    {
        phase = Random.value * 10f;
        baseRotation = transform.localEulerAngles.z;
        if (baseRotation > 180f) baseRotation -= 360f;
    }

    void Update()
    {
        float t = Time.unscaledTime + phase;
        transform.localRotation = Quaternion.Euler(0f, 0f, baseRotation + Mathf.Sin(t * swaySpeed) * swayDegrees);
        transform.localScale = Vector3.one * (1f + Mathf.Sin(t * breatheSpeed) * breatheAmount);
    }
}
