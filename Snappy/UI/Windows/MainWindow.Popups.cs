namespace Snappy.UI.Windows;

public partial class MainWindow
{
    private void HandlePopups()
    {
        if (_historyEntryToDelete != null) ImUtf8.OpenPopup("Delete History Entry");
        if (_openDeleteSnapshotPopup)
        {
            ImUtf8.OpenPopup("Delete Snapshot");
            _openDeleteSnapshotPopup = false;
        }

        if (_openRenameActorPopup)
        {
            if (_selectedSnapshotInfo != null)
            {
                _tempSourceActorName = _selectedSnapshotInfo.SourceActor;
                ImUtf8.OpenPopup("Rename Source Actor"u8);
            }

            _openRenameActorPopup = false;
        }

        using (
            var modal = ImUtf8.Modal(
                "Delete History Entry",
                ImGuiWindowFlags.AlwaysAutoResize
            )
        )
        {
            if (modal)
            {
                ImUtf8.Text(
                    "Are you sure you want to delete this history entry?\nThis action cannot be undone."
                );
                ImGui.Separator();
                if (ImUtf8.Button("Yes, Delete", new Vector2(120, 0)))
                {
                    if (_historyEntryToDelete is GlamourerHistoryEntry gEntry)
                        _glamourerHistory.Entries.Remove(gEntry);
                    else if (_historyEntryToDelete is CustomizeHistoryEntry cEntry)
                        _customizeHistory.Entries.Remove(cEntry);

                    SaveHistory();

                    Notify.Success("History entry deleted.");
                    _historyEntryToDelete = null;
                    ImGui.CloseCurrentPopup();
                }

                ImGui.SameLine();
                if (ImUtf8.Button("Cancel", new Vector2(120, 0)))
                {
                    _historyEntryToDelete = null;
                    ImGui.CloseCurrentPopup();
                }
            }
        }

        using (
            var modal = ImUtf8.Modal(
                "Delete Snapshot",
                ImGuiWindowFlags.AlwaysAutoResize
            )
        )
        {
            if (modal)
            {
                ImUtf8.Text(
                    $"Are you sure you want to permanently delete the snapshot '{_selectedSnapshot?.Name}'?\nThis will delete the entire folder and its contents.\nThis action cannot be undone."
                );
                ImGui.Separator();
                if (ImUtf8.Button("Yes, Delete Snapshot", new Vector2(180, 0)))
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
                if (ImUtf8.Button("Cancel", new Vector2(120, 0))) ImGui.CloseCurrentPopup();
            }
        }

        using (
            var modal = ImUtf8.Modal(
                "Rename Source Actor"u8,
                ImGuiWindowFlags.AlwaysAutoResize
            )
        )
        {
            if (modal)
            {
                ImUtf8.Text("Enter the new name for the Source Actor of this snapshot.");
                ImUtf8.Text("This name is used to find the snapshot when using 'Update Snapshot'.");
                ImGui.Separator();

                ImGui.SetNextItemWidth(300 * ImGuiHelpers.GlobalScale);
                var enterPressed = ImUtf8.InputText(
                    "##SourceActorName"u8,
                    ref _tempSourceActorName,
                    flags: ImGuiInputTextFlags.EnterReturnsTrue | ImGuiInputTextFlags.AutoSelectAll
                );
                if (ImGui.IsWindowAppearing()) ImGui.SetKeyboardFocusHere(-1);

                ImGui.Separator();

                var isInvalidName = string.IsNullOrWhiteSpace(_tempSourceActorName);

                using (var d = ImRaii.Disabled(isInvalidName))
                {
                    if (
                        ImUtf8.Button("Save", new Vector2(120, 0))
                        || (enterPressed && !isInvalidName)
                    )
                    {
                        SaveSourceActorName();
                        ImGui.CloseCurrentPopup();
                    }
                }

                ImGui.SameLine();
                if (ImUtf8.Button("Cancel", new Vector2(120, 0))) ImGui.CloseCurrentPopup();
            }
        }
    }
}
