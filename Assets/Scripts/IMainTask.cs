public interface IMainTask
{
    bool ShouldBlockInput();
    void StartMainTask();
    void StopMainTask();
}

/// <summary>
/// Something you use that runs another main task inside it (the gaming PC runs homework, the VR headset runs the
/// match). When the inner task closes itself it calls ReleaseMainTask with itself; the host is told so it can
/// tidy up, and you get up.
/// </summary>
public interface IMainTaskHost
{
    bool Hosts(IMainTask task);
    void Released(IMainTask task);
}
