using UnityEngine;
using UnityEngine.UI;

public class StressManager : MonoBehaviour
{
    public Image stressImage; 
    public float currentStress = 0f;
    public float maxStress = 100f;
    public float fillSpeed = 0.5f; 
    private float targetFillAmount;

    public void AddStress(float amount)
    {
        currentStress += amount;
        currentStress = Mathf.Clamp(currentStress, 0, maxStress);
        targetFillAmount = currentStress / maxStress;
    }

    public void RemoveStress(float amount)
    {
        currentStress -= amount;
        currentStress = Mathf.Clamp(currentStress, 0, maxStress);
        targetFillAmount = currentStress / maxStress;
    }

    private void Update()
    {
        UpdateStressUI();
    }

    private void UpdateStressUI()
    {
        if (stressImage != null)
        {
            stressImage.fillAmount = Mathf.Lerp(stressImage.fillAmount, targetFillAmount, Time.deltaTime * fillSpeed);
        }
    }

    private void HandleMaxStress()
    {
        print("u deaad mayne");
    }
}
