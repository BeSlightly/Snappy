namespace Snappy.Services.SnapshotManager;

public interface IGPoseService : IDisposable
{
    event Action? GPoseEntered;
    event Action? GPoseExited;
}