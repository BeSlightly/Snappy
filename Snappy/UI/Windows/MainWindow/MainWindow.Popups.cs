using Dalamud.Interface.Colors;
using Snappy.Services.SnapshotManager;

namespace Snappy.UI.Windows;

public partial class MainWindow
{
    private void HandlePopups()
    {
        if (_historyEntryToDelete != null)
        {
            ImGui.OpenPopup("Delete History Entry");
            var historyDeletePopupWidth = 390f * ImGuiHelpers.GlobalScale;
            var parentWindowCenter = ImGui.GetWindowPos() + ImGui.GetWindowSize() / 2f;
            ImGui.SetNextWindowPos(parentWindowCenter, ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));
            ImGui.SetNextWindowSizeConstraints(
                new Vector2(historyDeletePopupWidth, 0),
                new Vector2(historyDeletePopupWidth, float.MaxValue));
        }
        if (_openDeleteSnapshotPopup)
        {
            ImGui.OpenPopup("Delete Snapshot");
            _openDeleteSnapshotPopup = false;
        }

        if (_openRenameActorPopup)
        {
            if (_selectedSnapshotInfo != null)
            {
                _tempSourceActorName = _selectedSnapshotInfo.SourceActor;
                _tempSourceWorldId = _selectedSnapshotInfo.SourceWorldId is > 0
                    ? _selectedSnapshotInfo.SourceWorldId.Value
                    : 0;
                ImGui.OpenPopup("Rename Source Actor");
            }

            _openRenameActorPopup = false;
        }

        using (
            var modal = ImRaii.PopupModal(
                "Delete History Entry",
                ImGuiWindowFlags.AlwaysAutoResize
            )
        )
        {
            if (modal)
            {
                var isGlamourerEntry = _historyEntryToDelete is GlamourerHistoryEntry;
                var deleteUniqueFiles = isGlamourerEntry
                                        && _snappy.Configuration.DeleteUniqueFilesWithGlamourerHistoryEntry;
                var entryType = isGlamourerEntry ? "Glamourer" : "Customize+";
                Im.Text($"Delete this {entryType} history entry?");
                ImGui.Spacing();

                if (deleteUniqueFiles)
                {
                    using (var warningColor = ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.DalamudYellow))
                    {
                        Im.Text("Unused snapshot files will also be deleted.");
                    }

                    using (var mutedColor = ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.DalamudGrey))
                    {
                        Im.Text("Files used by other Glamourer entries will stay.");
                        Im.Text("Turn off cleanup in Settings to keep all files.");
                    }
                }
                else
                {
                    using (var keptColor = ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.HealerGreen))
                    {
                        Im.Text("Snapshot files will stay on disk.");
                    }

                    using (var mutedColor = ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.DalamudGrey))
                    {
                        Im.Text("Only the history entry will be removed.");
                        if (isGlamourerEntry)
                            Im.Text("Optional cleanup is available in Snappy Settings.");
                    }
                }

                ImGui.Spacing();
                using (var warningColor = ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.DalamudRed))
                {
                    Im.Text("This cannot be undone.");
                }
                ImGui.Separator();
                if (Im.Button("Delete Entry", new Vector2(120, 0)))
                {
                    var entryToDelete = _historyEntryToDelete;
                    var snapshotPath = _selectedSnapshot?.FullName;
                    _historyEntryToDelete = null;
                    ImGui.CloseCurrentPopup();

                    if (entryToDelete == null || string.IsNullOrEmpty(snapshotPath))
                    {
                        Notify.Error("Failed to delete the history entry because the snapshot is no longer selected.");
                        return;
                    }

                    _historyDeleteInProgress = true;
                    _snappy.ExecuteBackgroundTask(async () =>
                    {
                        var result = await _snapshotFileService.DeleteHistoryEntryAsync(snapshotPath, entryToDelete,
                            deleteUniqueFiles).ConfigureAwait(false);
                        _snappy.QueueAction(() => CompleteHistoryEntryDeletion(snapshotPath, result));
                    });
                }

                ImGui.SameLine();
                if (Im.Button("Cancel", new Vector2(120, 0)))
                {
                    _historyEntryToDelete = null;
                    ImGui.CloseCurrentPopup();
                }
            }
        }

        using (
            var modal = ImRaii.PopupModal(
                "Delete Snapshot",
                ImGuiWindowFlags.AlwaysAutoResize
            )
        )
        {
            if (modal)
            {
                Im.Text(
                    $"Are you sure you want to permanently delete the snapshot '{_selectedSnapshot?.Name}'?\nThis will delete the entire folder and its contents.\nThis action cannot be undone."
                );
                ImGui.Separator();
                if (Im.Button("Yes, Delete Snapshot", new Vector2(180, 0)))
                {
                    try
                    {
                        var deletedSnapshotName = _selectedSnapshot!.Name;
                        Directory.Delete(_selectedSnapshot!.FullName, true);
                        ClearSnapshotSelection();
                        _snappy.InvokeSnapshotsUpdated();
                        Notify.Success($"Snapshot '{deletedSnapshotName}' deleted successfully.");
                    }
                    catch (Exception e)
                    {
                        Notify.Error($"Could not delete snapshot directory\n{e.Message}");
                        PluginLog.Error($"Could not delete snapshot directory: {e}");
                    }

                    ImGui.CloseCurrentPopup();
                }

                ImGui.SameLine();
                if (Im.Button("Cancel", new Vector2(120, 0))) ImGui.CloseCurrentPopup();
            }
        }

        using (
            var modal = ImRaii.PopupModal(
                "Rename Source Actor",
                ImGuiWindowFlags.AlwaysAutoResize
            )
        )
        {
            if (modal)
            {
                Im.Text("Edit the source actor used for 'Update Snapshot' matching.");
                Im.Text("Name and home world identify the character in the snapshot index.");
                ImGui.Separator();

                Im.Text("Source Actor"u8);
                ImGui.SetNextItemWidth(300 * ImGuiHelpers.GlobalScale);
                var enterPressed = Im.Input.Text(
                    "##SourceActorName"u8,
                    ref _tempSourceActorName,
                    flags: InputTextFlags.EnterReturnsTrue | InputTextFlags.AutoSelectAll
                );
                if (ImGui.IsWindowAppearing()) ImGui.SetKeyboardFocusHere(-1);

                ImGui.Spacing();
                Im.Text("Source World"u8);
                ImGui.SetNextItemWidth(300 * ImGuiHelpers.GlobalScale);
                _renameSourceWorldSelector.Draw(ref _tempSourceWorldId);
                Im.Tooltip.OnHover(
                    "Home world stored on the snapshot. Used to match the right character when several share a name."u8);

                ImGui.Separator();

                var isInvalidName = string.IsNullOrWhiteSpace(_tempSourceActorName);

                using (var d = ImRaii.Disabled(isInvalidName))
                {
                    if (
                        Im.Button("Save", new Vector2(120, 0))
                        || (enterPressed && !isInvalidName)
                    )
                    {
                        SaveSourceActorName();
                        ImGui.CloseCurrentPopup();
                    }
                }

                ImGui.SameLine();
                if (Im.Button("Cancel", new Vector2(120, 0))) ImGui.CloseCurrentPopup();
            }
        }
    }

    private void CompleteHistoryEntryDeletion(string snapshotPath, HistoryEntryDeletionResult result)
    {
        _historyDeleteInProgress = false;
        if (string.Equals(_selectedSnapshot?.FullName, snapshotPath, StringComparison.OrdinalIgnoreCase))
            BeginLoadHistoryForSelectedSnapshot();

        if (!result.Success)
        {
            Notify.Error($"Failed to delete the history entry.\n{result.ErrorMessage}");
            return;
        }

        if (!string.IsNullOrEmpty(result.CleanupSkippedReason))
        {
            Notify.Warning($"History entry deleted, but its files were kept for safety: {result.CleanupSkippedReason}");
            return;
        }

        if (result.FailedFileCount > 0)
        {
            Notify.Warning(
                $"History entry deleted. Removed {result.DeletedFileCount} unique file(s), but {result.FailedFileCount} file(s) could not be removed.");
            return;
        }

        var fileMessage = result.DeletedFileCount > 0
            ? $" Removed {result.DeletedFileCount} unique file(s)."
            : string.Empty;
        Notify.Success("History entry deleted." + fileMessage);
    }
}
