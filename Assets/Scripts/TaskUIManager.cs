using UnityEngine;
using TMPro;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;

public class TaskUIManager : MonoBehaviour
{
    public GameObject[] taskSlots;
    public TMP_Text[] taskNameTexts;
    public Image[] taskIcons;
    public TMP_Text[] taskTimerTexts;
    private Queue<TaskBase> currentTaskQueue = new Queue<TaskBase>();

    public void UpdateTaskSlots(Queue<TaskBase> taskQueue)
    {
        StopAllCoroutines(); // Stop any previous coroutines to avoid conflicts
        currentTaskQueue = new Queue<TaskBase>(taskQueue); // Store the current queue for timer updates

        int index = 0;
        foreach (TaskBase task in taskQueue)
        {
            if (index >= taskSlots.Length) break;

            taskSlots[index].SetActive(true);
            taskNameTexts[index].text = task.taskName;
            taskIcons[index].sprite = task.taskIcon;
            taskIcons[index].enabled = true;
            taskTimerTexts[index].text = Mathf.Ceil(task.currentTime).ToString() + "s";
            index++;
        }

        for (int i = index; i < taskSlots.Length; i++)
        {
            ClearTaskSlot(i);
        }

        StartCoroutine(TaskTimerUpdate()); 
    }

    private IEnumerator TaskTimerUpdate()
    {
        while (currentTaskQueue.Count > 0)
        {
            int index = 0;
            foreach (TaskBase task in currentTaskQueue)
            {
                if (index >= taskSlots.Length) break;
                taskTimerTexts[index].text = Mathf.Ceil(task.currentTime).ToString() + "s";
                index++;
            }
            yield return new WaitForSeconds(1f); 
        }
    }

    private void ClearTaskSlot(int index)
    {
        taskSlots[index].SetActive(false);
        taskNameTexts[index].text = "";
        taskIcons[index].enabled = false;
        taskTimerTexts[index].text = "";
    }
}
