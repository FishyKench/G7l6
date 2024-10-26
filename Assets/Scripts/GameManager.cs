using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

public class GameManager : MonoBehaviour
{
    public TaskUIManager taskUIManager;
    private List<TaskBase> allTasks;
    public Queue<TaskBase> taskQueue = new Queue<TaskBase>();
    public int maxActiveTasks = 3;

    void Start()
    {
        allTasks = new List<TaskBase>(FindObjectsOfType<TaskBase>());
        StartCoroutine(RandomTaskTrigger());
    }

    IEnumerator RandomTaskTrigger()
    {
        while (true)
        {
            if (taskQueue.Count < maxActiveTasks)
            {
                yield return new WaitForSeconds(Random.Range(2f, 4f));
                ActivateRandomTask();
            }
            else
            {
                yield return null;
            }
        }
    }

    void ActivateRandomTask()
    {
        List<TaskBase> inactiveTasks = allTasks.FindAll(task => !task.isTaskActive);

        if (inactiveTasks.Count > 0)
        {
            TaskBase newTask = inactiveTasks[Random.Range(0, inactiveTasks.Count)];
            newTask.StartTask();
            taskQueue.Enqueue(newTask);
            taskUIManager.UpdateTaskSlots(taskQueue); 
        }
    }

    public void CompleteTask(TaskBase task)
    {
        if (taskQueue.Contains(task))
        {
            task.EndTask();
            taskQueue = new Queue<TaskBase>(taskQueue.Where(t => t != task));
            taskUIManager.UpdateTaskSlots(taskQueue); 
        }
    }
}
