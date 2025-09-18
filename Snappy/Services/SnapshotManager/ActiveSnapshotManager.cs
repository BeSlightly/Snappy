using ECommons.GameHelpers;
using Snappy.Common;
using Penumbra.GameData.Structs;

namespace Snappy.Services.SnapshotManager;

public class ActiveSnapshotManager : IActiveSnapshotManager
{
    private readonly List<ActiveSnapshot> _activeSnapshots = [];
    private readonly Configuration _configuration;
    private readonly IIpcManager _ipcManager;

    public ActiveSnapshotManager(IIpcManager ipcManager, Configuration configuration)
    {
        _ipcManager = ipcManager;
        _configuration = configuration;
    }

    public IReadOnlyList<ActiveSnapshot> ActiveSnapshots => _activeSnapshots;

    public bool HasActiveSnapshots => _activeSnapshots.Any();

    public void AddSnapshot(ActiveSnapshot snapshot)
    {
        _activeSnapshots.Add(snapshot);
    }

    public void RemoveAllSnapshotsForCharacter(ICharacter character)
    {
        _activeSnapshots.RemoveAll(s => IsSnapshotForCharacter(s, character));
    }

    public void RevertAllSnapshots()
    {
        PluginLog.Debug("Manual 'Revert All' triggered. Reverting all snapshots regardless of config.");
        var revertedCount = RevertInternal(false);
        if (revertedCount > 0)
            Notify.Success($"Reverted {revertedCount} active snapshot(s).");
        else
            Notify.Info("No active snapshots to revert.");
    }

    public void RevertAllSnapshotsOnGposeExit()
    {
        RevertInternal(true);
    }

    public void RevertSnapshotForCharacter(ICharacter character)
    {
        var snapshotsToRevert = _activeSnapshots.Where(s => IsSnapshotForCharacter(s, character)).ToList();
        if (!snapshotsToRevert.Any()) return;

        PluginLog.Information(
            $"Reverting {snapshotsToRevert.Count} snapshots for character {character.Name.TextValue}.");

        var indicesToRedraw = new HashSet<int>();

        foreach (var snapshot in snapshotsToRevert)
        {
            var target = Svc.Objects[snapshot.ObjectIndex];
            if (target == null && snapshot.IsOnLocalPlayer) target = Player.Object;

            if (target != null)
            {
                PluginLog.Information(
                    $"Reverting state for actor '{target.Name}' at index {target.ObjectIndex} (original index: {snapshot.ObjectIndex}).");

                _ipcManager.PenumbraRemoveTemporaryCollection(snapshot.ObjectIndex);

                if (snapshot.CustomizePlusProfileId.HasValue)
                    _ipcManager.RevertCustomizePlusScale(snapshot.CustomizePlusProfileId.Value);

                _ipcManager.UnlockGlamourerState(target);
                _ipcManager.RevertGlamourerToAutomation(target);

                indicesToRedraw.Add(target.ObjectIndex);
            }
            else
            {
                PluginLog.Warning(
                    $"Could not find a live actor at index {snapshot.ObjectIndex} to revert. Attempting to clear resources regardless.");
                _ipcManager.PenumbraRemoveTemporaryCollection(snapshot.ObjectIndex);

                if (snapshot.CustomizePlusProfileId.HasValue)
                    _ipcManager.RevertCustomizePlusScale(snapshot.CustomizePlusProfileId.Value);
            }
        }

        _activeSnapshots.RemoveAll(s => snapshotsToRevert.Contains(s));

        foreach (var index in indicesToRedraw)
            if (Svc.Objects[index] != null)
            {
                PluginLog.Debug($"Requesting redraw for reverted actor at index {index}.");
                _ipcManager.PenumbraRedraw(index);
            }

        Notify.Success($"Reverted snapshot for {character.Name.TextValue}.");
    }

    public bool IsActorLockedBySnappy(ICharacter character)
    {
        return _activeSnapshots.Any(s => IsSnapshotForCharacter(s, character));
    }

    public bool IsActorGlamourerLocked(ICharacter character)
    {
        var snapshot = _activeSnapshots.FirstOrDefault(s => IsSnapshotForCharacter(s, character));
        return snapshot?.IsGlamourerLocked ?? false;
    }

    public void LockActorGlamourer(ICharacter character)
    {
        var snapshot = _activeSnapshots.FirstOrDefault(s => IsSnapshotForCharacter(s, character));
        if (snapshot == null)
        {
            PluginLog.Debug($"No active snapshot found for character {character.Name.TextValue} to lock.");
            return;
        }

        PluginLog.Information(
            $"Locking Glamourer state for actor '{character.Name.TextValue}' at index {character.ObjectIndex}.");

        var currentState = _ipcManager.GetGlamourerState(character);
        _ipcManager.ApplyGlamourerState(currentState, character);

        var updatedSnapshot = snapshot with { IsGlamourerLocked = true };
        var index = _activeSnapshots.IndexOf(snapshot);
        _activeSnapshots[index] = updatedSnapshot;

        Notify.Success($"Locked {character.Name.TextValue}.");
        PluginLog.Debug(
            $"Actor {character.Name.TextValue} at index {character.ObjectIndex} Glamourer state has been locked.");
    }

    public void UnlockActorGlamourer(ICharacter character)
    {
        var snapshot = _activeSnapshots.FirstOrDefault(s => IsSnapshotForCharacter(s, character));
        if (snapshot == null)
        {
            PluginLog.Debug($"No active snapshot found for character {character.Name.TextValue} to unlock.");
            return;
        }

        PluginLog.Information(
            $"Unlocking Glamourer state for actor '{character.Name.TextValue}' at index {character.ObjectIndex}.");

        _ipcManager.UnlockGlamourerState(character);

        var updatedSnapshot = snapshot with { IsGlamourerLocked = false };
        var index = _activeSnapshots.IndexOf(snapshot);
        _activeSnapshots[index] = updatedSnapshot;

        Notify.Success($"Unlocked {character.Name.TextValue}.");
        PluginLog.Debug(
            $"Actor {character.Name.TextValue} at index {character.ObjectIndex} Glamourer state has been unlocked.");
    }

    public void OnGPoseEntered()
    {
        var localPlayerSnapshot = _activeSnapshots.FirstOrDefault(s => s.IsOnLocalPlayer);
        if (localPlayerSnapshot != null)
        {
            _activeSnapshots.Remove(localPlayerSnapshot);

            var gposeSnapshot = new ActiveSnapshot(
                ObjectIndex.GPosePlayer.Index,
                localPlayerSnapshot.CustomizePlusProfileId,
                localPlayerSnapshot.IsOnLocalPlayer,
                localPlayerSnapshot.CharacterName,
                localPlayerSnapshot.IsGlamourerLocked
            );
            _activeSnapshots.Add(gposeSnapshot);

            PluginLog.Debug(
                $"Updated local player snapshot tracking from index {localPlayerSnapshot.ObjectIndex} to GPose index {ObjectIndex.GPosePlayer.Index}");
        }
    }

    public void OnGPoseExited()
    {
        var gposeSnapshot =
            _activeSnapshots.FirstOrDefault(s => s.ObjectIndex == ObjectIndex.GPosePlayer.Index && s.IsOnLocalPlayer);
        if (gposeSnapshot != null && Player.Available)
        {
            _activeSnapshots.Remove(gposeSnapshot);

            var regularSnapshot = new ActiveSnapshot(
                Player.Object.ObjectIndex,
                gposeSnapshot.CustomizePlusProfileId,
                gposeSnapshot.IsOnLocalPlayer,
                gposeSnapshot.CharacterName,
                gposeSnapshot.IsGlamourerLocked
            );
            _activeSnapshots.Add(regularSnapshot);

            PluginLog.Debug(
                $"Updated GPose snapshot tracking from index {ObjectIndex.GPosePlayer.Index} to regular player index {Player.Object.ObjectIndex}");
        }
    }

    private bool IsSnapshotForCharacter(ActiveSnapshot snapshot, ICharacter character)
    {
        if (!character.IsValid())
            return false;

        return IsMatchingObjectIndex(snapshot, character)
               || IsMatchingLocalPlayer(snapshot, character)
               || IsMatchingByName(snapshot, character);
    }

    private static bool IsMatchingObjectIndex(ActiveSnapshot snapshot, ICharacter character)
    {
        return snapshot.ObjectIndex == character.ObjectIndex;
    }

    private static bool IsMatchingLocalPlayer(ActiveSnapshot snapshot, ICharacter character)
    {
        if (!snapshot.IsOnLocalPlayer || !Player.Available)
            return false;

        // If snapshot is on GPose actor, check if the character is the player
        if (snapshot.ObjectIndex == ObjectIndex.GPosePlayer.Index && character.Address == Player.Object.Address)
            return true;

        // If we are not in GPose, check if the character is the player
        if (!PluginUtil.IsInGpose() && character.Address == Player.Object.Address)
            return true;

        return false;
    }

    private static bool IsMatchingByName(ActiveSnapshot snapshot, ICharacter character)
    {
        return !string.IsNullOrEmpty(snapshot.CharacterName)
               && string.Equals(snapshot.CharacterName, character.Name.TextValue, StringComparison.Ordinal);
    }

    private int RevertInternal(bool respectConfig)
    {
        if (!_activeSnapshots.Any())
            return 0;

        var snapshotsToKeep = new List<ActiveSnapshot>();
        var snapshotsToRevert = new List<ActiveSnapshot>();

        foreach (var snapshot in _activeSnapshots)
            if (respectConfig && _configuration.DisableAutomaticRevert && snapshot.IsOnLocalPlayer)
                snapshotsToKeep.Add(snapshot);
            else
                snapshotsToRevert.Add(snapshot);

        if (!snapshotsToRevert.Any())
            return 0;

        PluginLog.Information(
            $"Reverting {snapshotsToRevert.Count} snapshots. Keeping {snapshotsToKeep.Count} snapshots active.");

        var indicesToRedraw = new HashSet<int>();

        foreach (var snapshot in snapshotsToRevert)
        {
            var target = Svc.Objects[snapshot.ObjectIndex];

            if (target == null && snapshot.IsOnLocalPlayer)
            {
                PluginLog.Information(
                    $"Stale snapshot for local player (original index {snapshot.ObjectIndex}) detected. Retargeting to current player character.");
                target = Player.Object;
            }

            if (target != null)
            {
                PluginLog.Information(
                    $"Reverting state for actor '{target.Name}' at index {target.ObjectIndex} (original index: {snapshot.ObjectIndex}).");

                _ipcManager.PenumbraRemoveTemporaryCollection(snapshot.ObjectIndex);

                if (snapshot.CustomizePlusProfileId.HasValue)
                    _ipcManager.RevertCustomizePlusScale(snapshot.CustomizePlusProfileId.Value);

                _ipcManager.UnlockGlamourerState(target);
                _ipcManager.RevertGlamourerToAutomation(target);

                indicesToRedraw.Add(target.ObjectIndex);
            }
            else
            {
                PluginLog.Warning(
                    $"Could not find a live actor at index {snapshot.ObjectIndex} to revert. Attempting to clear resources regardless.");
                _ipcManager.PenumbraRemoveTemporaryCollection(snapshot.ObjectIndex);

                if (snapshot.CustomizePlusProfileId.HasValue)
                    _ipcManager.RevertCustomizePlusScale(snapshot.CustomizePlusProfileId.Value);
            }
        }

        _activeSnapshots.Clear();
        _activeSnapshots.AddRange(snapshotsToKeep);

        foreach (var index in indicesToRedraw)
            if (Svc.Objects[index] != null)
            {
                PluginLog.Debug($"Requesting redraw for reverted actor at index {index}.");
                _ipcManager.PenumbraRedraw(index);
            }

        return snapshotsToRevert.Count;
    }
}

public record ActiveSnapshot(
    int ObjectIndex,
    Guid? CustomizePlusProfileId,
    bool IsOnLocalPlayer,
    string CharacterName,
    bool IsGlamourerLocked
);