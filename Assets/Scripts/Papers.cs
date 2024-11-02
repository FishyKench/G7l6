using UnityEngine;

public class Papers : MonoBehaviour
{
    public enum PaperStatus { Accepted, Rejected }
    public bool isFraudPaper;

    private DummyMainPapers mainTask;

    private void Start()
    {
        mainTask = FindObjectOfType<DummyMainPapers>();
    }

    private void OnMouseOver()
    {
        if (Input.GetMouseButtonDown(0)) // Left-click: Accept
        {
            mainTask.VerifyPaper(this, PaperStatus.Accepted);
        }
        else if (Input.GetMouseButtonDown(1)) // Right-click: Reject
        {
            mainTask.VerifyPaper(this, PaperStatus.Rejected);
        }
    }
}
