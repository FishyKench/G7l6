using UnityEngine;

public class DummyTask : TaskBase, IInteractable
{
    protected override void TaskFailed()
    {
        Debug.Log("Dummy task failed!");
        gameManager.CompleteTask(this); // Complete task in GameManager on failure
    }

    protected override void RewardPlayer()
    {
        Debug.Log("Player rewarded for completing dummy task!");
    }

    public void Interact()
    {
        if (isTaskActive)
        {
            RewardPlayer();
            gameManager.CompleteTask(this);
            gameManager.taskUIManager.UpdateTaskSlots(gameManager.taskQueue); // Immediate UI update after interaction
        }
    }
}