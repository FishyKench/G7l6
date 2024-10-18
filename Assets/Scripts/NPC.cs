using UnityEngine;

public class NPC : MonoBehaviour, IInteractable
{
    public DialogueData dialogue; // Assign the NPC's dialogue in the Inspector

    // Implement the Interact function from the IInteractable interface
    public void Interact()
    {
        // Find the DialogueManager and start the dialogue
        FindObjectOfType<DialogueManager>().StartDialogue(dialogue);
    }
}