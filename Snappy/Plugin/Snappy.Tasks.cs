using System.Threading;

namespace Snappy;

public sealed partial class Snappy
{
    public event Action? SnapshotsUpdated;

    public void QueueAction(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (Volatile.Read(ref _disposed) != 0)
            return;

        _mainThreadActions.Enqueue(action);
    }

    public void ExecuteBackgroundTask(Func<Task> task)
    {
        ArgumentNullException.ThrowIfNull(task);
        if (Volatile.Read(ref _disposed) != 0)
            return;

        _ = Task.Run(async () =>
        {
            if (Volatile.Read(ref _disposed) != 0)
                return;

            try
            {
                await task().ConfigureAwait(false);
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
