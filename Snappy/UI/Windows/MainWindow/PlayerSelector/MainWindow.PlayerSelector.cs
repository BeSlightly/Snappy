using ECommons.GameHelpers;

namespace Snappy.UI.Windows;

public partial class MainWindow
{
    private void DrawPlayerFilter()
    {
        var width = ImGui.GetContentRegionAvail().X;
        ImGui.SetNextItemWidth(width);
        if (ImUtf8.InputText("##playerFilter", ref _playerFilter, "Filter Players..."))
            _playerFilterLower = _playerFilter.ToLowerInvariant();
    }


    private void DrawSelectable(ICharacter selectablePlayer, string label, int objIdx)
    {
        if (!selectablePlayer.IsValid())
            return;

        if (_playerFilterLower.Any() && !label.ToLowerInvariant().Contains(_playerFilterLower))
            return;

        var isSelected = _selectedActorAddress == selectablePlayer.Address;

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

        _currentLabel = label;
        _player = selectablePlayer;
        _objIdxSelected = objIdx;
        _selectedActorAddress = selectablePlayer.Address;
        UpdateSelectedActorState();
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

        if (_player != null && _objIdxSelected != null && _selectedActorAddress != null)
        {
            // Verify the selected actor is still valid and matches our stored address
            if (_player.IsValid() && _player.Address == _selectedActorAddress)
            {
                UpdateSelectedActorState();
            }
            else
            {
                // Try to find the actor again by address in case the reference changed
                var selectableActorsList = GetSelectableActors();
                var foundActor = selectableActorsList.FirstOrDefault(a => a.Address == _selectedActorAddress);
                if (foundActor != null && foundActor.IsValid())
                {
                    _player = foundActor;
                    _objIdxSelected = foundActor.ObjectIndex;
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
            if (_player == null)
            {
                ClearSelectedActorState();
            }
            else
            {
                var charToSnap = _player;
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
                    if (_isActorLockedBySnappy && _player != null)
                    {
                        var isCurrentlyLocked = _activeSnapshotManager.IsActorGlamourerLocked(_player);
                        if (isCurrentlyLocked)
                            _activeSnapshotManager.UnlockActorGlamourer(_player);
                        else
                            _activeSnapshotManager.LockActorGlamourer(_player);
                    }
            }
        }

        ImUtf8.HoverTooltip(ImGuiHoveredFlags.AllowWhenDisabled, lockTooltip);

        ImGui.EndGroup();
    }
}
