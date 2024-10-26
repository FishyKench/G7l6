using UnityEngine;

public class DummyTask2 : TaskBase, IInteractable
{
    protected override void TaskFailed()
    {
        Debug.Log("Dummy task failed!");
        gameManager.CompleteTask(this); // Calls GameManager to complete the task
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
            gameManager.CompleteTask(this); // Mark the task as completed upon interaction
        }
    }
}