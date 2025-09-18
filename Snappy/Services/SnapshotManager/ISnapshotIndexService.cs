namespace Snappy.Services.SnapshotManager;

public interface ISnapshotIndexService
{
    void RefreshSnapshotIndex();
    string? FindSnapshotPathForActor(ICharacter character);
}