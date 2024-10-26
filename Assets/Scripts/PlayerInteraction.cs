using UnityEngine;

public class PlayerInteraction : MonoBehaviour
{
    public float interactionRange = 3f;
    private Camera playerCamera;
    private MainTaskBase currentMainTask;
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
        if (currentMainTask is DummyMainTask DummyMainTask && DummyMainTask.isTyping)
            return;

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
        if (playerCamera == null) return;

        RaycastHit hit;
        if (Physics.Raycast(playerCamera.transform.position, playerCamera.transform.forward, out hit, interactionRange))
        {
            MainTaskBase mainTask = hit.collider.GetComponent<MainTaskBase>();
            if (mainTask != null && !isInteractingWithMainTask)
            {
                currentMainTask = mainTask;
                currentMainTask.StartMainTask();
                isInteractingWithMainTask = true;

                playerMovement.canMove = false;
                playerCam.canRotate = false;
                return;
            }

            TaskBase subTask = hit.collider.GetComponent<TaskBase>();
            if (subTask != null && subTask is IInteractable interactableSubTask)
            {
                interactableSubTask.Interact();
            }
        }
    }

    void StopCurrentMainTask()
    {
        if (currentMainTask != null)
        {
            currentMainTask.StopMainTask();
            currentMainTask = null;
        }

        isInteractingWithMainTask = false;
        EnablePlayerControl();
    }

    public void EnablePlayerControl()
    {
        playerMovement.canMove = true;
        playerCam.canRotate = true;
    }
}
