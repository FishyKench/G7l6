using UnityEngine;

public class PlayerInteraction : MonoBehaviour
{
    public float interactionRange = 3f;
    private Camera playerCamera;
    private IMainTask currentMainTask;
    public PlayerCam playerCam;
    private PlayerMovementAdvanced playerMovement;
    private bool isInteractingWithMainTask = false;

    void Start()
    {
        playerCamera = Camera.main;
        playerMovement = GetComponent<PlayerMovementAdvanced>();
    }

    void Update()
    {
        
        if (Input.GetKeyDown(KeyCode.Escape) && isInteractingWithMainTask)
        {
            StopCurrentMainTask();
            return;
        }

        
        if (isInteractingWithMainTask && currentMainTask != null && currentMainTask.ShouldBlockInput())
        {
            return;
        }

        if (Input.GetKeyDown(KeyCode.E))
        {
            if (isInteractingWithMainTask)
            {
                StopCurrentMainTask();
            }
            else
            {
                TryInteract();
            }
        }
    }

    void TryInteract()
    {
        if (playerCamera == null || isInteractingWithMainTask) return;

        RaycastHit hit;
        if (Physics.Raycast(playerCamera.transform.position, playerCamera.transform.forward, out hit, interactionRange))
        {
            IMainTask mainTask = hit.collider.GetComponent<IMainTask>();
            if (mainTask != null)
            {
                StartMainTask(mainTask);
                return;
            }

            TaskBase subTask = hit.collider.GetComponent<TaskBase>();
            if (subTask != null && subTask is IInteractable interactableSubTask)
            {
                interactableSubTask.Interact();
            }
        }
    }

    void StartMainTask(IMainTask mainTask)
    {
        if (isInteractingWithMainTask) return;

        currentMainTask = mainTask;
        currentMainTask.StartMainTask();
        isInteractingWithMainTask = true;

        playerMovement.canMove = false;
        playerCam.canRotate = false;
    }

    void StopCurrentMainTask()
    {
        if (currentMainTask == null)
        {
            return;
        }
        currentMainTask.StopMainTask();
        currentMainTask = null;
        isInteractingWithMainTask = false;

        EnablePlayerControl();
    }

    public void EnablePlayerControl()
    {
        playerMovement.canMove = true;
        playerCam.canRotate = true;
    }
}
