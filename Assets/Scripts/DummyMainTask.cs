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

        playerInputField.text = currentInputText;
        playerInputField.gameObject.SetActive(true);

        playerInputField.Select();
        playerInputField.ActivateInputField();
        UpdateTypedText();
    }

    public override void StopMainTask()
    {
        currentInputText = playerInputField.text;
        playerInputField.gameObject.SetActive(false);
        base.StopMainTask();
        taskPanel.SetActive(false);

        isTyping = false;
    }

    public override bool ShouldBlockInput()
    {
        return isTyping;
    }

    private void Update()
    {
        if (playerInputField.isFocused)
        {
            currentInputText = playerInputField.text;
            UpdateTypedText();
        }

        if (Input.GetKeyDown(KeyCode.Escape) && isWorkingOnTask)
        {
            StopMainTask();
        }
    }

    private void UpdateTypedText()
    {
        string displayText = "";

        for (int i = 0; i < currentInputText.Length; i++)
        {
            if (i < requiredText.Length && requiredText[i] == currentInputText[i])
            {
                displayText += $"<color=green>{currentInputText[i]}</color>";
            }
            else
            {
                displayText += $"<color=red>{currentInputText[i]}</color>";
            }
        }

        typedText.text = displayText;

        if (currentInputText.Equals(requiredText, System.StringComparison.OrdinalIgnoreCase))
        {
            CompleteTask(); // Call complete task instantly if input matches
        }
    }

    protected override void CompleteTask()
    {
        StopMainTask();
        FindObjectOfType<PlayerInteraction>().EnablePlayerControl();
        Debug.Log("Main task completed!");
    }
}
