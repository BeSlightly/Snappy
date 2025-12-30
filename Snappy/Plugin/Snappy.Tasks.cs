namespace Snappy;

public sealed partial class Snappy
{
    public event Action? SnapshotsUpdated;

    public void QueueAction(Action action)
    {
        _mainThreadActions.Enqueue(action);
    }

    public void ExecuteBackgroundTask(Func<Task> task)
    {
        Task.Run(async () =>
        {
            try
            {
                await task();
            }
            catch (Exception ex)
            {
                PluginLog.Error($"A background task failed: {ex}");
            }
        });
    }

    public void InvokeSnapshotsUpdated()
    {
        SnapshotIndexService.RefreshSnapshotIndex();
        SnapshotsUpdated?.Invoke();
    }
}
