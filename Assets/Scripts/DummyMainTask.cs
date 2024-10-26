using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class DummyMainTask : MainTaskBase
{
    public GameObject taskPanel;
    public TMP_Text taskPromptText;
    public TMP_InputField playerInputField;
    public string requiredText = "Write a short paragraph";
    public bool isTyping = false;

    private void Start()
    {
        taskPanel.SetActive(false);
    }

    public override void StartMainTask()
    {
        base.StartMainTask();
        taskPanel.SetActive(true);
        taskPromptText.text = requiredText;
        playerInputField.text = "";
        playerInputField.ActivateInputField();

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        playerInputField.onSelect.AddListener(delegate { isTyping = true; });
        playerInputField.onDeselect.AddListener(delegate { isTyping = false; });
    }

    public override void StopMainTask()
    {
        base.StopMainTask();
        taskPanel.SetActive(false);

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    public override bool ShouldBlockInput()
    {
        return isTyping;
    }

    public void CheckPlayerInput()
    {
        if (playerInputField.text.Trim().Equals(requiredText, System.StringComparison.OrdinalIgnoreCase))
        {
            CompleteTask();
        }
        else
        {
            Debug.Log("Text wrong. Please try again.");
            playerInputField.ActivateInputField();
        }
    }

    protected override void CompleteTask()
    {
        StopMainTask();
        FindObjectOfType<PlayerInteraction>().EnablePlayerControl();
        Debug.Log("Main task completed!");
    }
}
