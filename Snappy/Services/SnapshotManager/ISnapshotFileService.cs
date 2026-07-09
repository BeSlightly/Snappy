namespace Snappy.Services.SnapshotManager;

public interface ISnapshotFileService
{
    Task<string?> UpdateSnapshotAsync(ICharacter character, bool isLocalPlayer);

    void RenameSnapshot(string oldPath, string newName);

    void SaveSnapshotToDisk(string snapshotPath, SnapshotInfo info, GlamourerHistory glamourerHistory,
        CustomizeHistory customizeHistory);
}
