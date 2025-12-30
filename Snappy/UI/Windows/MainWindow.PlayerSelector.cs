using Dalamud.Game.ClientState.Objects.SubKinds;
using ECommons.GameHelpers;
using Penumbra.GameData.Structs;

namespace Snappy.UI.Windows;

public partial class MainWindow
{
    private bool _isActorLockedBySnappy;
    private bool _isActorModifiable;
    private bool _isActorSnapshottable;
    private bool _snapshotExistsForActor;
    private string currentLabel = string.Empty;
    private int? objIdxSelected;
    private ICharacter? player;
    private nint? selectedActorAddress;

    private string playerFilter = string.Empty;
    private string playerFilterLower = string.Empty;

    private void ClearSelectedActorState()
    {
        player = null;
        currentLabel = string.Empty;
        objIdxSelected = null;
        selectedActorAddress = null;

        _isActorSnapshottable = false;
        _snapshotExistsForActor = false;
        _isActorModifiable = false;
        _isActorLockedBySnappy = false;
    }

    private void UpdateSelectedActorState()
    {
        if (player == null || objIdxSelected == null)
        {
            ClearSelectedActorState();
            return;
        }

        // Don't clear state if actor becomes temporarily invalid - this can happen during transitions
        if (!player.IsValid()) return;

        // --- Update modifiable state ---
        var inGpose = PluginUtil.IsInGpose();
        if (inGpose)
        {
            _isActorModifiable = true;
        }
        else
        {
            var isLocalPlayer = player.ObjectIndex == Player.Object?.ObjectIndex;
            _isActorModifiable =
                isLocalPlayer
                && _snappy.Configuration.DisableAutomaticRevert
                && _snappy.Configuration.AllowOutsideGpose;
        }

        // --- Update snapshottable state ---
        _snapshotExistsForActor =
            _snapshotIndexService.FindSnapshotPathForActor(player) != null;

        if (inGpose)
        {
            _isActorSnapshottable = false;
        }
        else
        {
            var isSelf = Player.Object != null && player.Address == Player.Object.Address;
            var isMarePaired = _ipcManager.IsMarePairedAddress(player.Address);
            var includeTempActors = _snappy.Configuration.UseLiveSnapshotData
                                    && _snappy.Configuration.IncludeVisibleTempCollectionActors;
            var hasTempCollection = includeTempActors &&
                                    _ipcManager.PenumbraHasTemporaryCollection(player.ObjectIndex);

            _isActorSnapshottable = isSelf || isMarePaired || hasTempCollection;
        }

        // --- Update lock state ---
        _isActorLockedBySnappy = _activeSnapshotManager.IsActorLockedBySnappy(player);
    }

    private void DrawPlayerFilter()
    {
        var width = ImGui.GetContentRegionAvail().X;
        ImGui.SetNextItemWidth(width);
        if (ImUtf8.InputText("##playerFilter", ref playerFilter, "Filter Players..."))
            playerFilterLower = playerFilter.ToLowerInvariant();
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
        catch
        {
            // HomeWorld might not be available or accessible
        }

        return baseName;
    }


    private void DrawSelectable(ICharacter selectablePlayer, string label, int objIdx)
    {
        if (!selectablePlayer.IsValid())
            return;

        if (playerFilterLower.Any() && !label.ToLowerInvariant().Contains(playerFilterLower))
            return;

        var isSelected = selectedActorAddress == selectablePlayer.Address;

        var isSnowcloak = _ipcManager.IsSnowcloakAddress(selectablePlayer.Address);
        var isLightless = !isSnowcloak && _ipcManager.IsLightlessAddress(selectablePlayer.Address);
        var isPlayerSync = !isSnowcloak && !isLightless && _ipcManager.IsPlayerSyncAddress(selectablePlayer.Address);

        if (isSnowcloak)
        {
            using var _ = ImRaii.PushColor(ImGuiCol.Text, new Vector4(0.4275f, 0.6863f, 1f, 1f));
            ApplySelectableSelection(selectablePlayer, label, objIdx, isSelected);
        }
        else if (isLightless)
        {
            using var _ = ImRaii.PushColor(ImGuiCol.Text, new Vector4(0.6784f, 0.5412f, 0.9608f, 1f));
            ApplySelectableSelection(selectablePlayer, label, objIdx, isSelected);
        }
        else if (isPlayerSync)
        {
            using var _ = ImRaii.PushColor(ImGuiCol.Text, new Vector4(0.4745f, 0.8392f, 0.7569f, 1f));
            ApplySelectableSelection(selectablePlayer, label, objIdx, isSelected);
        }
        else
        {
            ApplySelectableSelection(selectablePlayer, label, objIdx, isSelected);
        }
    }

    private void ApplySelectableSelection(ICharacter selectablePlayer, string label, int objIdx, bool isSelected)
    {
        if (!ImUtf8.Selectable(label, isSelected))
            return;

        if (isSelected)
        {
            ClearSelectedActorState();
            return;
        }

        currentLabel = label;
        player = selectablePlayer;
        objIdxSelected = objIdx;
        selectedActorAddress = selectablePlayer.Address;
        UpdateSelectedActorState();
    }


    private (string Text, string Tooltip, bool IsDisabled) GetSnapshotButtonState()
    {
        if (player == null) return ("Save Snapshot", "Select an actor to save or update its snapshot.", true);

        var displayName = GetActorDisplayName(player);

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
        if (player == null || objIdxSelected == null)
            return (FontAwesomeIcon.Lock, "Select an actor to manage its lock state.", true);

        if (!_isActorLockedBySnappy) return (FontAwesomeIcon.Unlock, "This actor is not locked by Snappy.", true);

        var isLocked = _activeSnapshotManager.IsActorGlamourerLocked(player);
        if (isLocked)
            return (FontAwesomeIcon.Lock,
                $"Unlock {GetActorDisplayName(player)} (click to unlock Glamourer state only)", false);
        return (FontAwesomeIcon.Unlock, $"Lock {GetActorDisplayName(player)} (click to lock Glamourer state)",
            false);
    }

    private void DrawPlayerSelector()
    {
        ImGui.BeginGroup();
        DrawPlayerFilter();

        List<ICharacter>? selectableActors = null;
        List<ICharacter> GetSelectableActors()
        {
            return selectableActors ??= _actorService.GetSelectableActors();
        }

        var buttonHeight = ImGui.GetFrameHeight() + ImGui.GetStyle().ItemSpacing.Y;
        var listHeight = ImGui.GetContentRegionAvail().Y - buttonHeight;

        using (
            var child = ImRaii.Child(
                "ActorList",
                new Vector2(ImGui.GetContentRegionAvail().X, listHeight),
                false
            )
        )
        {
            if (child)
            {
                var selectableActorsList = GetSelectableActors();
                var inGpose = PluginUtil.IsInGpose();

                // Create a dictionary to track duplicate names and make them unique
                var actorLabels = new Dictionary<ICharacter, string>();
                var nameCount = new Dictionary<string, int>();

                // First pass: count duplicate base names (without GPose suffix)
                foreach (var actor in selectableActorsList)
                {
                    var baseName = GetActorDisplayName(actor);
                    if (!string.IsNullOrEmpty(baseName))
                        nameCount[baseName] = nameCount.GetValueOrDefault(baseName, 0) + 1;
                }

                // Second pass: create unique labels
                foreach (var actor in selectableActorsList)
                {
                    var baseName = GetActorDisplayName(actor);
                    if (!string.IsNullOrEmpty(baseName))
                    {
                        string displayName;

                        if (nameCount[baseName] > 1)
                        {
                            if (inGpose)
                            {
                                // GPose actors: use ObjectIndex for disambiguation
                                displayName = $"{baseName} (GPose {actor.ObjectIndex})";
                            }
                            else
                            {
                                // Regular players: try to use HomeWorld, fallback to ObjectIndex
                                var nameWithWorld = GetActorDisplayNameWithWorld(actor);
                                if (nameWithWorld != baseName)
                                    displayName = nameWithWorld;
                                else
                                    // HomeWorld not available, use ObjectIndex
                                    displayName = $"{baseName} ({actor.ObjectIndex})";
                            }
                        }
                        else
                        {
                            // Unique name - use simple format
                            displayName = inGpose ? $"{baseName} (GPose)" : baseName;
                        }

                        actorLabels[actor] = displayName;
                    }
                }

                // Draw the actors with unique labels
                foreach (var actor in selectableActorsList)
                    if (actorLabels.TryGetValue(actor, out var label))
                        DrawSelectable(actor, label, actor.ObjectIndex);
            }
        }

        if (player != null && objIdxSelected != null && selectedActorAddress != null)
        {
            // Verify the selected actor is still valid and matches our stored address
            if (player.IsValid() && player.Address == selectedActorAddress)
            {
                UpdateSelectedActorState();
            }
            else
            {
                // Try to find the actor again by address in case the reference changed
                var selectableActorsList = GetSelectableActors();
                var foundActor = selectableActorsList.FirstOrDefault(a => a.Address == selectedActorAddress);
                if (foundActor != null && foundActor.IsValid())
                {
                    player = foundActor;
                    objIdxSelected = foundActor.ObjectIndex;
                    UpdateSelectedActorState();
                }
                else
                {
                    // Actor no longer exists, clear selection
                    ClearSelectedActorState();
                }
            }
        }

        var (buttonText, tooltipText, isButtonDisabled) = GetSnapshotButtonState();
        var (lockIcon, lockTooltip, isLockDisabled) = GetLockButtonState();

        var lockButtonSize = new Vector2(ImGui.GetFrameHeight(), 0);
        var spacing = ImGui.GetStyle().ItemSpacing.X * 0.5f;
        var saveButtonWidth = ImGui.GetContentRegionAvail().X - lockButtonSize.X - spacing;
        var saveButtonSize = new Vector2(saveButtonWidth, 0);

        if (
            ImUtf8.ButtonEx(
                buttonText,
                tooltipText,
                saveButtonSize,
                isButtonDisabled
            )
        )
        {
            if (player == null)
            {
                ClearSelectedActorState();
            }
            else
            {
                var charToSnap = player;
                var isLocalPlayer = Player.Object != null && charToSnap.Address == Player.Object.Address;
                Dictionary<string, HashSet<string>>? penumbraReplacements = null;
                var useLiveData = _snappy.Configuration.UseLiveSnapshotData || isLocalPlayer;
                var useIpcResourcePaths = useLiveData && _snappy.Configuration.UsePenumbraIpcResourcePaths;
                if (useIpcResourcePaths)
                    penumbraReplacements = _ipcManager.PenumbraGetGameObjectResourcePaths(charToSnap.ObjectIndex);

                Notify.Info($"Snapshotting {GetActorDisplayName(charToSnap)} in the background...");
                _snappy.ExecuteBackgroundTask(async () =>
                {
                    var updatedSnapshotPath =
                        await _snapshotFileService.UpdateSnapshotAsync(charToSnap, isLocalPlayer, penumbraReplacements);
                    if (updatedSnapshotPath != null)
                        _snappy.QueueAction(() => _snappy.InvokeSnapshotsUpdated());
                });
            }
        }

        ImGui.SameLine(0, spacing);
        using (var disabled = ImRaii.Disabled(isLockDisabled))
        {
            using (var font = ImRaii.PushFont(UiBuilder.IconFont))
            {
                if (ImUtf8.Button(lockIcon.ToIconString(), lockButtonSize))
                    if (_isActorLockedBySnappy && player != null)
                    {
                        var isCurrentlyLocked = _activeSnapshotManager.IsActorGlamourerLocked(player);
                        if (isCurrentlyLocked)
                            _activeSnapshotManager.UnlockActorGlamourer(player);
                        else
                            _activeSnapshotManager.LockActorGlamourer(player);
                    }
            }
        }

        ImUtf8.HoverTooltip(ImGuiHoveredFlags.AllowWhenDisabled, lockTooltip);

        ImGui.EndGroup();
    }
}
