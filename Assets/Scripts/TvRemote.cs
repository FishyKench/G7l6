using UnityEngine;

public class TvRemote : MonoBehaviour, IInteractable
{
    public TvSubTask tvSubTask; 

    private void Start()
    {

        if (tvSubTask == null)
        {
            tvSubTask = GetComponent<TvSubTask>();
        }
    }

    public void Interact()
    {
        if (tvSubTask != null && tvSubTask.isTaskActive)
        {
            tvSubTask.StopAddingStress();
            tvSubTask.CompleteTask(); 
        }
    }
}
