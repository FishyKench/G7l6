using UnityEngine;
using TMPro;

public abstract class MainTaskBase : MonoBehaviour, IMainTask
{
    public string taskName = "Main Task";
    public TMP_Text progressText;
    public float totalTimeRequired = 60f;
    private float currentProgress = 0f;
    public bool isWorkingOnTask = false;

    protected virtual void Update()
    {
        if (isWorkingOnTask)
        {
            currentProgress += Time.deltaTime;
            UpdateProgressText();

            if (currentProgress >= totalTimeRequired)
            {
                CompleteTask();
            }
        }
    }

    public virtual void StartMainTask()
    {
        isWorkingOnTask = true;
    }

    public virtual void StopMainTask()
    {
        isWorkingOnTask = false;
    }

    public virtual bool ShouldBlockInput() => false;

    protected void UpdateProgressText()
    {
        if (progressText != null)
        {
            float progressPercentage = (currentProgress / totalTimeRequired) * 100;
            progressText.text = $"{taskName}: {progressPercentage:F1}% completed";
        }
    }

    protected virtual void CompleteTask()
    {
        isWorkingOnTask = false;
        currentProgress = totalTimeRequired;
        if (progressText != null)
        {
            progressText.text = $"{taskName} completed!";
        }
        Debug.Log($"{taskName} has been completed!");
    }
}
