using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "TaskParagraphsData", menuName = "Task/Task Paragraphs Data")]
public class TaskParagraphsData : ScriptableObject
{
    public List<string> paragraphs = new List<string>();
}