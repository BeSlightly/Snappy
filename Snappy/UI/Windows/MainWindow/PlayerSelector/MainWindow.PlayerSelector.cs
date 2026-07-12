using ECommons.GameHelpers;

namespace Snappy.UI.Windows;

public partial class MainWindow
{
    private void DrawPlayerFilter()
    {
        var width = ImGui.GetContentRegionAvail().X;
        ImGui.SetNextItemWidth(width);
        if (Im.Input.Text("##playerFilter", ref _playerFilter, "Filter Players..."))
        {
            _playerFilterLower = _playerFilter.ToLowerInvariant();
            _actorFilterDirty = true;
        }
    }


    private void DrawSelectable(ActorRow row)
    {
        var selectablePlayer = row.Actor;
        if (!selectablePlayer.IsValid())
            return;

        var isSelected = _selectedActorAddress == selectablePlayer.Address;

        if (row.IsSnowcloak)
        {
            using var _ = ImRaii.PushColor(ImGuiCol.Text, MareForkColors.Snowcloak);
            ApplySelectableSelection(selectablePlayer, row.Label, selectablePlayer.ObjectIndex, isSelected);
        }
        else if (row.IsLightless)
        {
            using var _ = ImRaii.PushColor(ImGuiCol.Text, MareForkColors.LightlessSync);
            ApplySelectableSelection(selectablePlayer, row.Label, selectablePlayer.ObjectIndex, isSelected);
        }
        else if (row.IsPlayerSync)
        {
            using var _ = ImRaii.PushColor(ImGuiCol.Text, MareForkColors.PlayerSync);
            ApplySelectableSelection(selectablePlayer, row.Label, selectablePlayer.ObjectIndex, isSelected);
        }
        else
        {
            ApplySelectableSelection(selectablePlayer, row.Label, selectablePlayer.ObjectIndex, isSelected);
        }
    }

    private void ApplySelectableSelection(ICharacter selectablePlayer, string label, int objIdx, bool isSelected)
    {
        if (!Im.Selectable(label, isSelected))
            return;

        if (isSelected)
        {
            ClearSelectedActorState();
            return;
        }

        _objIdxSelected = objIdx;
        _selectedActorAddress = selectablePlayer.Address;
        UpdateSelectedActorStateIfNeeded(true);
    }


    private void DrawPlayerSelector()
    {
        using var group = ImRaii.Group();
        DrawPlayerFilter();
        var rows = GetVisibleActorRows();

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
                if (rows.Count > 0)
                {
                    var lineHeight = ImGui.GetTextLineHeightWithSpacing();
                    using var clipper = new Im.ListClipper(rows.Count, lineHeight);
                    foreach (var i in clipper)
                        DrawSelectable(rows[i]);
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
            UiHelpers.ButtonEx(
                buttonText,
                tooltipText,
                saveButtonSize,
                isButtonDisabled
            )
        )
        {
            if (!TryGetSelectedActor(out var selectedActor))
            {
                ClearSelectedActorState();
            }
            else
            {
                var charToSnap = selectedActor;
                var isLocalPlayer = Player.Object != null && charToSnap.Address == Player.Object.Address;
                var actorDisplayName = GetActorDisplayName(charToSnap);
                _isSnapshotCaptureInProgress = true;
                Notify.Info($"Snapshotting {actorDisplayName} in the background...");
                _snappy.ExecuteBackgroundTask(async () =>
                {
                    try
                    {
                        var updatedSnapshotPath = await _snapshotFileService.UpdateSnapshotAsync(charToSnap, isLocalPlayer);
                        _snappy.QueueAction(() =>
                        {
                            _isSnapshotCaptureInProgress = false;
                            if (updatedSnapshotPath != null)
                                _snappy.InvokeSnapshotsUpdated();
                        });
                    }
                    catch (Exception ex)
                    {
                        PluginLog.Error($"Failed to capture snapshot for {actorDisplayName}: {ex}");
                        _snappy.QueueAction(() =>
                        {
                            _isSnapshotCaptureInProgress = false;
                            Notify.Error($"Failed to snapshot {actorDisplayName}.\n{ex.Message}");
                        });
                    }
                });
            }
        }

        ImGui.SameLine(0, spacing);
        if (UiHelpers.IconButton(lockIcon, lockTooltip, lockButtonSize, isLockDisabled))
        {
            if (_isActorLockedBySnappy && TryGetSelectedActor(out var selectedActor))
            {
                var isCurrentlyLocked = _activeSnapshotManager.IsActorGlamourerLocked(selectedActor);
                if (isCurrentlyLocked)
                    _activeSnapshotManager.UnlockActorGlamourer(selectedActor);
                else
                    _activeSnapshotManager.LockActorGlamourer(selectedActor);
            }
        }
    }
}
