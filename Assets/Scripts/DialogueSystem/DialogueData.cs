using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "NewDialogue", menuName = "Dialogue/DialogueData")]
public class DialogueData : ScriptableObject
{
    public string npcName;  // NPC's name
    public string[] dialogueLines;  // NPC's lines
    public string[] playerResponses;  // Player's response options
    public DialogueData[] nextDialogue;  // Next dialogue options based on player response
}