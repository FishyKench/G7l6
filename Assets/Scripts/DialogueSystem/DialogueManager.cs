using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class DialogueManager : MonoBehaviour
{
    public TextMeshProUGUI npcText;  // Text for NPC's dialogue
    public TextMeshProUGUI[] playerResponseTexts;  // Texts for player's response options
    public Button[] playerResponseButtons;  // Buttons for player's response options

    private DialogueData currentDialogue;  // Holds the current dialogue
    private bool isDialogueActive = false;  // Track if the dialogue is active

    void Update()
    {
        // Check if the player presses "ESC" to exit the dialogue
        if (isDialogueActive && Input.GetKeyDown(KeyCode.Escape))
        {
            EndDialogue();
        }
    }

    // Start the dialogue with the given DialogueData
    public void StartDialogue(DialogueData startingDialogue)
    {
        currentDialogue = startingDialogue;
        DisplayDialogue();
        ShowCursor();  // Show the cursor when dialogue starts
        isDialogueActive = true;  // Set dialogue to active
    }

    // Display the current NPC's dialogue and player responses
    private void DisplayDialogue()
    {
        npcText.text = currentDialogue.dialogueLines[0];  // NPC's first dialogue line

        // Display player response options
        for (int i = 0; i < currentDialogue.playerResponses.Length; i++)
        {
            playerResponseTexts[i].text = currentDialogue.playerResponses[i];  // Set response text
            playerResponseButtons[i].gameObject.SetActive(true);  // Show button
            int responseIndex = i;  // Capture the index for the listener
            playerResponseButtons[i].onClick.RemoveAllListeners();  // Clear previous listeners
            playerResponseButtons[i].onClick.AddListener(() => OnPlayerResponse(responseIndex));  // Add listener for click
        }

        // Hide unused buttons
        for (int i = currentDialogue.playerResponses.Length; i < playerResponseButtons.Length; i++)
        {
            playerResponseButtons[i].gameObject.SetActive(false);  // Hide buttons not used
        }
    }

    // Handle the player's response
    private void OnPlayerResponse(int responseIndex)
    {
        foreach (Button button in playerResponseButtons)
        {
            button.gameObject.SetActive(false);  // Hide all buttons after selection
        }

        // Move to the next dialogue based on player choice
        if (currentDialogue.nextDialogue != null && currentDialogue.nextDialogue.Length > responseIndex)
        {
            currentDialogue = currentDialogue.nextDialogue[responseIndex];  // Set the next dialogue based on response
            DisplayDialogue();  // Display the next dialogue
        }
        else
        {
            EndDialogue();  // End the conversation if no more dialogue exists
        }
    }

    // End the dialogue (cleanup or reset if needed)
    public void EndDialogue()
    {
        npcText.text = "";  // Clear the NPC dialogue text
        foreach (Button button in playerResponseButtons)
        {
            button.gameObject.SetActive(false);  // Hide all response buttons
        }
        HideCursor();  // Hide the cursor when dialogue ends immediately
        isDialogueActive = false;  // Set dialogue to inactive
    }

    // Show the cursor and lock player movement
    private void ShowCursor()
    {
        Cursor.lockState = CursorLockMode.None;  // Unlock the cursor
        Cursor.visible = true;  // Show the cursor
    }

    // Hide the cursor and resume normal gameplay
    private void HideCursor()
    {
        Cursor.lockState = CursorLockMode.Locked;  // Lock the cursor again
        Cursor.visible = false;  // Hide the cursor
    }
}
