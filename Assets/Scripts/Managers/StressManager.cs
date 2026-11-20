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
    private float targetFillAmount;
    private bool maxedOut;

    public float Normalized => maxStress > 0f ? currentStress / maxStress : 0f;

    void Awake()
    {
        Instance = this;
    }

    public void AddStress(float amount)
    {
        currentStress = Mathf.Clamp(currentStress + amount, 0, maxStress);
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
        // No game over yet - just let the player know.
        GameHUD.Toast("You're completely stressed out!", GameHUD.Bad);
    }
}
