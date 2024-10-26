using UnityEngine;

public abstract class TaskBase : MonoBehaviour
{
    public string taskName;
    public Sprite taskIcon;
    public float taskTime = 10f;
    public bool isTaskActive = false;
    public float currentTime;

    protected GameManager gameManager; // Changed to 'protected' for subclass access

    protected virtual void Start()
    {
        gameManager = FindObjectOfType<GameManager>();
        currentTime = taskTime;
    }

    protected virtual void Update()
    {
        if (isTaskActive)
        {
            currentTime -= Time.deltaTime;

            if (currentTime <= 0)
            {
                gameManager.CompleteTask(this);
            }
        }
    }

    public virtual void StartTask()
    {
        isTaskActive = true;
        currentTime = taskTime;
    }

    public virtual void EndTask()
    {
        isTaskActive = false;
    }

    // Marked these methods as 'abstract' so they can be overridden
    protected abstract void TaskFailed();

    protected virtual void RewardPlayer()
    {
        Debug.Log("Player rewarded for completing task.");
    }
}