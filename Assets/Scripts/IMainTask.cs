public interface IMainTask
{
    bool ShouldBlockInput();
    void StartMainTask();
    void StopMainTask();
}