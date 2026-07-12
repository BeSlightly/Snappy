namespace Snappy.Services.SnapshotManager;

public interface ISnapshotFileService
{
    Task<string?> UpdateSnapshotAsync(ICharacter character, bool isLocalPlayer);

    void RenameSnapshot(string oldPath, string newName);

    Task<HistoryEntryDeletionResult> DeleteHistoryEntryAsync(string snapshotPath, HistoryEntryBase entry,
        bool deleteUniqueGlamourerFiles);

    void SaveSnapshotToDisk(string snapshotPath, SnapshotInfo info, GlamourerHistory glamourerHistory,
        CustomizeHistory customizeHistory);
}
