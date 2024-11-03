using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class ExhaustionManager : MonoBehaviour
{
    public Image exhaustionImage;
    public float currentExhaustion = 0f;
    public float maxExhaustion = 100f;
    public float fillSpeed = 0.5f;
    private float targetFillAmount;
    private PlayerMovementAdvanced playerMovement;

    private bool isExhausted;

    private void Start()
    {
        playerMovement = FindObjectOfType<PlayerMovementAdvanced>();
    }

    public void AddExhaustion(float amount)
    {
        currentExhaustion += amount;
        currentExhaustion = Mathf.Clamp(currentExhaustion, 0, maxExhaustion);
        targetFillAmount = currentExhaustion / maxExhaustion;

        if (currentExhaustion >= maxExhaustion)
        {
            HandleMaxExhaustion();
        }
    }

    public void RemoveExhaustion(float amount)
    {
        if (isExhausted)
        {
            HandlePlayerRested();
            currentExhaustion -= amount;
            currentExhaustion = Mathf.Clamp(currentExhaustion, 0, maxExhaustion);
            targetFillAmount = currentExhaustion / maxExhaustion;
        }
        else
        {
            currentExhaustion -= amount;
            currentExhaustion = Mathf.Clamp(currentExhaustion, 0, maxExhaustion);
            targetFillAmount = currentExhaustion / maxExhaustion;
        }
    }

    private void Update()
    {
        UpdateStressUI();
    }

    private void UpdateStressUI()
    {
        if (exhaustionImage != null)
        {
            if (currentExhaustion <= 90)
            {
                exhaustionImage.fillAmount = Mathf.Lerp(exhaustionImage.fillAmount, targetFillAmount, Time.deltaTime * fillSpeed);
            }
            else
            {
                exhaustionImage.fillAmount = Mathf.MoveTowards(exhaustionImage.fillAmount, targetFillAmount, fillSpeed * Time.deltaTime);
            }
        }
    }

    public bool GetIsExhausted()
    {
        return isExhausted;
    }

    public void SetIsExhausted(bool value)
    {
        isExhausted = value;
    }

    private void HandleMaxExhaustion()
    {
        isExhausted = true;
        playerMovement.walkSpeed = 3;
        playerMovement.sprintSpeed = 3;
    }

    private void HandlePlayerRested()
    {
        isExhausted = false;
        playerMovement.walkSpeed = 5;
        playerMovement.sprintSpeed = 5;
    }

}
