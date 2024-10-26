using UnityEngine;
using TMPro;

public class DummyMainTask : MainTaskBase
{
    public GameObject taskPanel;
    public TMP_Text taskPromptText;
    public TMP_Text typedText;
    public TMP_InputField playerInputField;
    public string requiredText = "Write a short paragraph";
    public bool isTyping = false;

    [SerializeField]
    private string currentInputText = "";

    private void Start()
    {
        taskPanel.SetActive(false);
        playerInputField.gameObject.SetActive(false);
    }

    public override void StartMainTask()
    {
        isTyping = true;
        base.StartMainTask();
        taskPanel.SetActive(true);
        taskPromptText.text = requiredText;
        playerInputField.gameObject.SetActive(true);
        playerInputField.Select();
        playerInputField.ActivateInputField();
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    public override void StopMainTask()
    {
        playerInputField.text = ""; 
        playerInputField.gameObject.SetActive(false);
        base.StopMainTask();
        taskPanel.SetActive(false);

        isTyping = false;
    }
    public override bool ShouldBlockInput()
    {
        return isTyping;
    }

    private new void Update()
    {
        if (playerInputField.isFocused)
        {
            currentInputText = playerInputField.text;
            UpdateTypedText();
        }
        else
        {
            playerInputField.Select();
            playerInputField.ActivateInputField();
        }
    }

    private void UpdateTypedText()
    {
        string displayText = "";
        bool hasMistake = false;

        for (int i = 0; i < currentInputText.Length; i++)
        {
            if (!hasMistake && i < requiredText.Length && requiredText[i] == currentInputText[i])
            {
                displayText += $"<color=green>{currentInputText[i]}</color>";
            }
            else
            {
                displayText += $"<color=red>{currentInputText[i]}</color>";
                hasMistake = true;
            }
        }

        typedText.text = displayText;

        if (currentInputText.Equals(requiredText, System.StringComparison.OrdinalIgnoreCase))
        {
            CompleteTask();
        }
    }



    protected override void CompleteTask()
    {
        StopMainTask();
        FindObjectOfType<PlayerInteraction>().EnablePlayerControl();
        Debug.Log("Main task completed!");
    }
}
