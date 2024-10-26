using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class DummyMainPapers : MainTaskBase
{
    private Camera mainCam;
    public Camera papersCam;
    public Texture2D stampTexture;
    public GameObject paperPrefab;

    private Vector3 pendingPos = new Vector3(0.398999989f, 0.50999999f, 0.136999995f);
    private Vector3 currentPos = new Vector3(0f, 0.50999999f, -0.125f);
    private Vector3 finishedPos = new Vector3(-0.395000011f, 0.50999999f, -0.119000003f);

    private int paperAmount = 5;
    private List<GameObject> papers = new List<GameObject>();

    private void Start()
    {
        mainCam = Camera.current;
        CreatePapers();
    }

    private void CreatePapers()
    {
        for (int i = 0; i < 10; i++)
        {
            GameObject paperInstance;
            paperInstance = Instantiate(paperPrefab, gameObject.transform) as GameObject;
            papers.Add(paperInstance);
            print(i);
        }
    }

    public override void StartMainTask()
    {
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
}
