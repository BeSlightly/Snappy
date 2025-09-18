namespace Snappy.Services.SnapshotManager;

public interface IActiveSnapshotManager
{
    IReadOnlyList<ActiveSnapshot> ActiveSnapshots { get; }
    bool HasActiveSnapshots { get; }
    void AddSnapshot(ActiveSnapshot snapshot);
    void RemoveAllSnapshotsForCharacter(ICharacter character);
    void RevertAllSnapshots();
    void RevertAllSnapshotsOnGposeExit();
    void RevertSnapshotForCharacter(ICharacter character);
    bool IsActorLockedBySnappy(ICharacter character);
    bool IsActorGlamourerLocked(ICharacter character);
    void LockActorGlamourer(ICharacter character);
    void UnlockActorGlamourer(ICharacter character);
    void OnGPoseEntered();
    void OnGPoseExited();
}