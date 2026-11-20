using UnityEngine;
using UnityEngine.UI;

public class StressManager : MonoBehaviour
{
    public static StressManager Instance { get; private set; }

    [Tooltip("Optional old-style fill image. The new HUD draws its own stress bar.")]
    public Image stressImage;
    public float currentStress = 0f;
    public float maxStress = 100f;
    public float fillSpeed = 0.5f;
    [Tooltip("All stress you get is multiplied by this (lower = a calmer afternoon).")]
    public float gainMultiplier = 0.55f;
    [Tooltip("Stress that melts away per second while nothing is going wrong (no task stressing you out).")]
    public float calmRecoveryPerSecond = 0.6f;
    float lastGain;
    private float targetFillAmount;
    private bool maxedOut;

    public float Normalized => maxStress > 0f ? currentStress / maxStress : 0f;

    void Awake()
    {
        Instance = this;
    }

    public void AddStress(float amount)
    {
        if (amount > 0f) lastGain = Time.time;
        currentStress = Mathf.Clamp(currentStress + amount * gainMultiplier, 0, maxStress);
        targetFillAmount = Normalized;

        if (currentStress >= maxStress && !maxedOut)
        {
            maxedOut = true;
            HandleMaxStress();
        }
    }

    public void RemoveStress(float amount)
    {
        currentStress = Mathf.Clamp(currentStress - amount, 0, maxStress);
        targetFillAmount = Normalized;
        if (currentStress < maxStress) maxedOut = false;
    }

    private void Update()
    {
        // Calm down slowly when nothing has stressed you for a couple of seconds.
        if (calmRecoveryPerSecond > 0f && currentStress > 0f && Time.time - lastGain > 2f)
            RemoveStress(calmRecoveryPerSecond * Time.deltaTime);
        UpdateStressUI();
    }

    private void UpdateStressUI()
    {
        if (stressImage != null)
        {
            if (currentStress <= 90)
                stressImage.fillAmount = Mathf.Lerp(stressImage.fillAmount, targetFillAmount, Time.deltaTime * fillSpeed);
            else
                stressImage.fillAmount = Mathf.MoveTowards(stressImage.fillAmount, targetFillAmount, fillSpeed * Time.deltaTime);
        }
    }

    private void HandleMaxStress()
    {
        // The DayManager gives you a panic countdown to calm down before it becomes a meltdown.
        GameHUD.Toast("PANIC! Calm down before you melt down!", GameHUD.Bad);
    }
}
