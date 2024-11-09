using System.Collections;
using UnityEngine;

public class TvSubTask : TaskBase
{
    private StressManager stressManager;
    private bool isAddingStress = true;

    public Renderer tvRenderer;
    public Material normalTvMat;
    public Material staticTvMat;
    [SerializeField] private AudioSource TvStatic;

    protected override void Start()
    {
        base.Start();
        stressManager = FindObjectOfType<StressManager>();
    }

    protected override void TaskFailed()
    {
        // This task cannot fail :)
    }

    public override void StartTask()
    {
        base.StartTask();
        StartCoroutine(AddStressPeriodically(1, 0.6f)); // Start coroutine to add stress over time
        TvStatic.Play(); 
    }

    protected override void RewardPlayer()
    {
        if (stressManager != null)
        {
            stressManager.RemoveStress(10); // Reduce stress as a reward
        }
        Debug.Log("Player rewarded for completing TV task!");
        gameManager.CompleteTask(this);
    }

    private IEnumerator AddStressPeriodically(float interval, float stressPoints)
    {
        TvStatic.Play();
        Material[] materials = tvRenderer.materials; 
        materials[1] = staticTvMat; 
        tvRenderer.materials = materials;

        while (isAddingStress && isTaskActive)
        {
            yield return new WaitForSeconds(interval);
            if (stressManager != null)
            {
                stressManager.AddStress(stressPoints); // Add stress periodically
            }
        }
    }

    public void StopAddingStress()
    {
        isAddingStress = false;
        StopAllCoroutines();
        TvStatic.Stop();
        Material[] materials = tvRenderer.materials;
        materials[1] = normalTvMat;
        tvRenderer.materials = materials;
    }

    public void CompleteTask()
    {
            RewardPlayer();
            gameManager.CompleteTask(this);
            gameManager.taskUIManager.UpdateTaskSlots(gameManager.taskQueue);
    }
}
