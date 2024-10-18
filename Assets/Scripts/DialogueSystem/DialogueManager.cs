using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class DialogueManager : MonoBehaviour
{
    public TextMeshProUGUI npcText;
    public TextMeshProUGUI[] playerResponseTexts;
    public Button[] playerResponseButtons;

    public MonoBehaviour playerMovementScript;  // Reference to player movement script
    public MonoBehaviour cameraControlScript;  // Reference to camera control script

    private DialogueData currentDialogue;
    private bool isDialogueActive = false;

    void Update()
    {
        if (isDialogueActive && Input.GetKeyDown(KeyCode.Escape))
        {
            EndDialogue();
        }
    }

    public void StartDialogue(DialogueData startingDialogue)
    {
        currentDialogue = startingDialogue;
        DisplayDialogue();
        ShowCursor();
        isDialogueActive = true;
        DisablePlayerControls();  // Disable movement and camera controls
    }

    private void DisplayDialogue()
    {
        npcText.text = currentDialogue.dialogueLines[0];

        for (int i = 0; i < currentDialogue.playerResponses.Length; i++)
        {
            playerResponseTexts[i].text = currentDialogue.playerResponses[i];
            playerResponseButtons[i].gameObject.SetActive(true);
            int responseIndex = i;
            playerResponseButtons[i].onClick.RemoveAllListeners();
            playerResponseButtons[i].onClick.AddListener(() => OnPlayerResponse(responseIndex));
        }

        for (int i = currentDialogue.playerResponses.Length; i < playerResponseButtons.Length; i++)
        {
            playerResponseButtons[i].gameObject.SetActive(false);
        }
    }

    private void OnPlayerResponse(int responseIndex)
    {
        foreach (Button button in playerResponseButtons)
        {
            button.gameObject.SetActive(false);
        }

        if (currentDialogue.nextDialogue != null && currentDialogue.nextDialogue.Length > responseIndex)
        {
            currentDialogue = currentDialogue.nextDialogue[responseIndex];
            DisplayDialogue();
        }
        else
        {
            EndDialogue();
        }
    }

    public void EndDialogue()
    {
        npcText.text = "";
        foreach (Button button in playerResponseButtons)
        {
            button.gameObject.SetActive(false);
        }
        HideCursor();
        isDialogueActive = false;
        EnablePlayerControls();  // Enable movement and camera controls again
    }

    private void DisablePlayerControls()
    {
        playerMovementScript.enabled = false;
        cameraControlScript.enabled = false;
    }

    private void EnablePlayerControls()
    {
        playerMovementScript.enabled = true;
        cameraControlScript.enabled = true;
    }

    private void ShowCursor()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private void HideCursor()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }
}