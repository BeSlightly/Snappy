using Dalamud.Game.ClientState.Objects.SubKinds;
using ECommons.GameHelpers;
using Snappy.Common.Utilities;

namespace Snappy.UI.Windows;

public partial class MainWindow
{
    private bool _isActorLockedBySnappy;
    private bool _isActorModifiable;
    private bool _isActorSnapshottable;
    private bool _snapshotExistsForActor;
    private string _currentLabel = string.Empty;
    private int? _objIdxSelected;
    private ICharacter? _player;
    private nint? _selectedActorAddress;

    private string _playerFilter = string.Empty;
    private string _playerFilterLower = string.Empty;

    private void ClearSelectedActorState()
    {
        _player = null;
        _currentLabel = string.Empty;
        _objIdxSelected = null;
        _selectedActorAddress = null;

        _isActorSnapshottable = false;
        _snapshotExistsForActor = false;
        _isActorModifiable = false;
        _isActorLockedBySnappy = false;
    }

    private void UpdateSelectedActorState()
    {
        if (_player == null || _objIdxSelected == null)
        {
            ClearSelectedActorState();
            return;
        }

        // Don't clear state if actor becomes temporarily invalid - this can happen during transitions
        if (!_player.IsValid()) return;

        // --- Update modifiable state ---
        var inGpose = PluginUtil.IsInGpose();
        if (inGpose)
        {
            _isActorModifiable = true;
        }
        else
        {
            var isLocalPlayer = _player.ObjectIndex == Player.Object?.ObjectIndex;
            var allowOutside = _snappy.Configuration.AllowOutsideGpose;
            var allowOwnedPets = allowOutside && _snappy.Configuration.AllowOutsideGposeOwnedPets;
            var isOwnedPet = allowOwnedPets && ActorOwnershipUtil.IsSelfOwnedPet(_player);
            _isActorModifiable =
                _snappy.Configuration.DisableAutomaticRevert
                && ((isLocalPlayer && allowOutside) || isOwnedPet);
        }

        // --- Update snapshottable state ---
        _snapshotExistsForActor =
            _snapshotIndexService.FindSnapshotPathForActor(_player) != null;

        if (inGpose)
        {
            _isActorSnapshottable = false;
        }
        else
        {
            var isSelf = Player.Object != null && _player.Address == Player.Object.Address;
            var isMarePaired = _ipcManager.IsMarePairedAddress(_player.Address);
            var includeTempActors = _snappy.Configuration.UseLiveSnapshotData
                                    && _snappy.Configuration.IncludeVisibleTempCollectionActors;
            var hasTempCollection = includeTempActors &&
                                    _ipcManager.PenumbraHasTemporaryCollection(_player.ObjectIndex);

            _isActorSnapshottable = isSelf || isMarePaired || hasTempCollection;
        }

        // --- Update lock state ---
        _isActorLockedBySnappy = _activeSnapshotManager.IsActorLockedBySnappy(_player);
    }

    private string GetActorDisplayName(ICharacter actor)
    {
        if (PluginUtil.IsInGpose())
        {
            var brioName = _ipcManager.GetBrioActorName(actor);
            if (!string.IsNullOrEmpty(brioName)) return brioName;
        }

        return actor.Name.TextValue;
    }

    private string GetActorDisplayNameWithWorld(ICharacter actor)
    {
        var baseName = GetActorDisplayName(actor);

        try
        {
            // Cast to IPlayerCharacter to access HomeWorld (regular players only, not GPose actors)
            if (actor is IPlayerCharacter playerCharacter)
            {
                var homeWorldId = playerCharacter.HomeWorld.RowId;
                if (homeWorldId > 0 && Snappy.WorldNames.TryGetValue(homeWorldId, out var worldName))
                    return $"{baseName}@{worldName}";
            }
        }
        catch (Exception ex)
        {
            PluginLog.Verbose(
                $"Failed to resolve HomeWorld for '{baseName}' (index {actor.ObjectIndex}): {ex.Message}");
        }

        return baseName;
    }

    private (string Text, string Tooltip, bool IsDisabled) GetSnapshotButtonState()
    {
        if (_player == null) return ("Save Snapshot", "Select an actor to save or update its snapshot.", true);

        var displayName = GetActorDisplayName(_player);

        if (PluginUtil.IsInGpose())
        {
            var buttonText = _snapshotExistsForActor ? "Update Snapshot" : "Save Snapshot";
            return (
                buttonText,
                "Saving or updating snapshots is unavailable while in GPose.",
                true
            );
        }

        if (!_isActorSnapshottable)
        {
            var restriction = _snappy.Configuration.UseLiveSnapshotData
                ? _snappy.Configuration.IncludeVisibleTempCollectionActors
                    ? "Can only save snapshots of yourself, Mare-paired players, or visible actors with temporary Penumbra collections."
                    : "Can only save snapshots of yourself or Mare-paired players."
                : "Can only save snapshots of yourself, or players you are paired with in Mare Synchronos.";

            return ("Save Snapshot", restriction, true);
        }

        if (_snapshotExistsForActor)
            return (
                "Update Snapshot",
                $"Update existing snapshot for {displayName}.\n(Folder can be renamed freely)",
                false
            );

        return ("Save Snapshot", $"Save a new snapshot for {displayName}.", false);
    }

    private (FontAwesomeIcon Icon, string Tooltip, bool IsDisabled) GetLockButtonState()
    {
        if (_player == null || _objIdxSelected == null)
            return (FontAwesomeIcon.Lock, "Select an actor to manage its lock state.", true);

        if (!_isActorLockedBySnappy) return (FontAwesomeIcon.Unlock, "This actor is not locked by Snappy.", true);

        var isLocked = _activeSnapshotManager.IsActorGlamourerLocked(_player);
        if (isLocked)
            return (FontAwesomeIcon.Lock,
                $"Unlock {GetActorDisplayName(_player)} (click to unlock Glamourer state only)", false);
        return (FontAwesomeIcon.Unlock, $"Lock {GetActorDisplayName(_player)} (click to lock Glamourer state)",
            false);
    }
}
