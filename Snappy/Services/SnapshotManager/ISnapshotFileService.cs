namespace Snappy.Services.SnapshotManager;

public interface ISnapshotFileService
{
    Task<string?> UpdateSnapshotAsync(ICharacter character, bool isLocalPlayer,
        Dictionary<string, HashSet<string>>? penumbraReplacements);

    void RenameSnapshot(string oldPath, string newName);

    void SaveSnapshotToDisk(string snapshotPath, SnapshotInfo info, GlamourerHistory glamourerHistory,
        CustomizeHistory customizeHistory);
}
