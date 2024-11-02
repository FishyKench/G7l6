using System.Collections.Generic;
using UnityEngine;

public class DummyMainPapers : MainTaskBase
{
    private Camera mainCam;
    public Camera papersCam;
    public Texture2D stampTexture;
    public GameObject paperPrefab;
    public Transform paperOffset;

    private Vector3 pendingPos = new Vector3(0.398999989f, 0.50999999f, 0.136999995f);
    private Vector3 currentPos = new Vector3(0f, 0.50999999f, -0.125f);
    private Vector3 finishedPos = new Vector3(-0.395000011f, 0.50999999f, -0.119000003f);

    private int paperAmount = 5;
    private List<GameObject> papers = new List<GameObject>();
    private int currentPaperIndex = 0;

    private void Start()
    {
        mainCam = Camera.current;
        
    }

    private void CreatePapers()
    {
        for (int i = 0; i < paperAmount; i++)
        {
            GameObject paperInstance = Instantiate(paperPrefab, gameObject.transform);
            paperInstance.transform.position = paperOffset.position;
            paperInstance.GetComponent<Papers>().isFraudPaper = Random.value > 0.5f;
            papers.Add(paperInstance);
        }
        if (papers.Count > 0)
        {
            papers[currentPaperIndex].transform.position = currentPos;
        }
    }

    public override void StartMainTask()
    {
        CreatePapers();
        base.StartMainTask();
        papersCam.gameObject.SetActive(true);
        Camera.SetupCurrent(papersCam);
        Cursor.SetCursor(stampTexture, Vector2.zero, CursorMode.ForceSoftware);
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    public override void StopMainTask()
    {
        base.StopMainTask();
        papersCam.gameObject.SetActive(false);
        Camera.SetupCurrent(mainCam);
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
    }

    public void VerifyPaper(Papers paper, Papers.PaperStatus status)
    {
        bool isCorrect = (status == Papers.PaperStatus.Accepted && !paper.isFraudPaper) ||
                         (status == Papers.PaperStatus.Rejected && paper.isFraudPaper);

        Debug.Log(isCorrect ? "Correct choice!" : "Wrong choice!");

        papers[currentPaperIndex].transform.position = finishedPos;
        AdvanceToNextPaper();
    }

    private void AdvanceToNextPaper()
    {
        currentPaperIndex++;
        if (currentPaperIndex < papers.Count)
        {
            papers[currentPaperIndex].transform.position = currentPos;
        }
    }
}
