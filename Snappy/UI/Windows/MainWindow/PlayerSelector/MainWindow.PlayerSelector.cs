using ECommons.GameHelpers;
using Snappy.Common.Utilities;

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

        _currentLabel = label;
        _objIdxSelected = objIdx;
        _selectedActorAddress = selectablePlayer.Address;
        UpdateSelectedActorStateIfNeeded(true);
    }


    private void DrawPlayerSelector()
    {
        ImGui.BeginGroup();
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
                    var clipper = new ImGuiListClipper();
                    clipper.Begin(rows.Count, lineHeight);
                    while (clipper.Step())
                    {
                        for (var i = clipper.DisplayStart; i < clipper.DisplayEnd; i++)
                            DrawSelectable(rows[i]);
                    }

                    clipper.End();
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
                if (Im.Button(lockIcon.ToIconString(), lockButtonSize))
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

        Im.Tooltip.OnHover(HoveredFlags.AllowWhenDisabled, lockTooltip);

        ImGui.EndGroup();
    }
}
