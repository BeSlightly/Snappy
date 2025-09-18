namespace Snappy.Services.SnapshotManager;

public interface ISnapshotApplicationService
{
    bool LoadSnapshot(ICharacter characterApplyTo, int objIdx, string path,
        GlamourerHistoryEntry? glamourerOverride = null, CustomizeHistoryEntry? customizeOverride = null);
}