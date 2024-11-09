using UnityEngine;

public class DummyTask : TaskBase, IInteractable
{
    [SerializeField]
    private int stressAmount = 10;

    private StressManager stressManager;

    protected override void Start()
    {
        base.Start(); 
        stressManager = FindObjectOfType<StressManager>();
    }

    protected override void TaskFailed()
    {
        // Add stress when the task fails.
        if (stressManager != null)
        {
            stressManager.AddStress(stressAmount);
        }

        Debug.Log("Dummy task failed!");
        gameManager.CompleteTask(this);
    }

    protected override void RewardPlayer()
    {
        // Remove half stress as a reward.
        if (stressManager != null)
        {
            stressManager.RemoveStress(stressAmount / 2);
        }

        Debug.Log("Player rewarded for completing dummy task!");
        gameManager.CompleteTask(this);
    }

    public void Interact()
    {
        if (isTaskActive)
        {
            RewardPlayer();
            gameManager.CompleteTask(this);
            gameManager.taskUIManager.UpdateTaskSlots(gameManager.taskQueue);
        }
    }
}
