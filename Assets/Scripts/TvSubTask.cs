using System.Collections;
using UnityEngine;

public class TvSubTask : TaskBase, IInteractable
{
    [SerializeField]
    private int stressAmount = 10;

    private StressManager stressManager;

    private bool isAddingStress = true;

    protected override void Start()
    {
        base.Start();
        stressManager = FindObjectOfType<StressManager>();
        StartCoroutine(AddStressPeriodically(1, 0.6f));
    }

    protected override void TaskFailed()
    {
        //task can not be failed xd
    }

    protected override void RewardPlayer()
    {
        // Remove half stress as a reward.
        if (stressManager != null)
        {
            stressManager.RemoveStress(10);
        }

        Debug.Log("Player rewarded for completing dummy task!");
        gameManager.CompleteTask(this);
    }

    private IEnumerator AddStressPeriodically(float interval, float stressPoints)
    {
        while (isAddingStress)
        {
            yield return new WaitForSeconds(interval);
            if (isTaskActive && stressManager != null)
            {
                stressManager.AddStress(stressPoints);
                print("momo called");
            }
        }
    }

    private void StopAddingStress()
    {
        isAddingStress = false;
        StopAllCoroutines(); 
    }

    public void Interact()
    {
        if (isTaskActive)
        {
            gameManager.CompleteTask(this);
            gameManager.taskUIManager.UpdateTaskSlots(gameManager.taskQueue);
            StopAddingStress();
            RewardPlayer();
        }
    }
}