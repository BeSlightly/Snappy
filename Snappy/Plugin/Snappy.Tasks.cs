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
        ExecuteBackgroundTask(async () =>
        {
            await _snapshotRefreshGate.WaitAsync();
            try
            {
                SnapshotIndexService.RefreshSnapshotIndex();
            }
            finally
            {
                _snapshotRefreshGate.Release();
            }

            QueueAction(() => SnapshotsUpdated?.Invoke());
        });
    }
}
