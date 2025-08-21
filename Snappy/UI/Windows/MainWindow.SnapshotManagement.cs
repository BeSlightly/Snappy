using Dalamud.Utility;

namespace Snappy.UI.Windows;

public partial class MainWindow
{
    private void DrawSnapshotManagementPanel()
    {
        ImUtf8.Text("SNAPSHOT MANAGEMENT"u8);
        ImGui.Separator();

        DrawSnapshotHeader();
        DrawActionButtons();
        ImGui.Spacing();

        if (_selectedSnapshot != null)
            DrawHistoryTabs();
        else if (_snapshotList.Length > 0)
            ImUtf8.Text("Select a snapshot to manage."u8);
        else
            ImUtf8.Text(
                "No snapshots found. Select an actor and click 'Save Snapshot' to create one."u8
            );
    }

    private void DrawSnapshotHeader()
    {
        ImGui.AlignTextToFramePadding();
        ImUtf8.Text("SNAPSHOT:"u8);
        ImGui.SameLine();

        var buttonsDisabled = _selectedSnapshot == null;

        if (_isRenamingSnapshot)
        {
            UiHelpers.DrawInlineRename("SnapshotRename", ref _tempSnapshotName, HandleSnapshotRename,
                () => _isRenamingSnapshot = false);
        } else
        {
            var iconBarWidth = (ImGui.GetFrameHeight() + ImGui.GetStyle().ItemSpacing.X) * 3;
            var comboWidth =
                ImGui.GetContentRegionAvail().X - iconBarWidth - ImGui.GetStyle().ItemSpacing.X;

            using var disabled = ImRaii.Disabled(_snapshotList.Length == 0);

            _snapshotCombo.Draw(
                "##SnapshotSelector",
                _selectedSnapshot?.Name ?? "Select a Snapshot...",
                comboWidth
            );

            disabled.Dispose();

            if (ImGui.IsItemHovered() && ImGui.IsItemClicked(ImGuiMouseButton.Right)) ClearSnapshotSelection();
            ImUtf8.HoverTooltip("Right-click to clear selection.");

            if (ImGui.IsItemHovered() && _snapshotList.Length == 0)
                ImUtf8.HoverTooltip("No snapshots exist yet. Save one first."u8);

            ImGui.SameLine();
            if (
                ImUtf8.IconButton(
                    FontAwesomeIcon.Sync,
                    "Refresh List",
                    default,
                    false
                )
            )
                _snappy.InvokeSnapshotsUpdated();

            ImGui.SameLine();
            if (
                ImUtf8.IconButton(
                    FontAwesomeIcon.Pen,
                    buttonsDisabled
                        ? "Select a snapshot to rename"
                        : "Rename Snapshot",
                    default,
                    buttonsDisabled
                )
            )
            {
                _isRenamingSnapshot = true;
                _tempSnapshotName = _selectedSnapshot!.Name;
                ImGui.SetKeyboardFocusHere(-1);
            }

            ImGui.SameLine();
            if (
                ImUtf8.IconButton(
                    FontAwesomeIcon.Trash,
                    buttonsDisabled
                        ? "Select a snapshot to delete"
                        : "Delete Snapshot",
                    default,
                    buttonsDisabled
                )
            )
                _openDeleteSnapshotPopup = true;
        }
    }

    private void HandleSnapshotRename()
    {
        _isRenamingSnapshot = false;
        if (
            _selectedSnapshot == null
            || _tempSnapshotName == _selectedSnapshot.Name
            || string.IsNullOrWhiteSpace(_tempSnapshotName)
        )
            return;

        var oldPath = _selectedSnapshot.FullName;
        var newName = _tempSnapshotName;

        _snappy.ExecuteBackgroundTask(() => Task.Run(() => _snapshotFileService.RenameSnapshot(oldPath, newName)));

        ClearSnapshotSelection();
    }

    private void DrawActionButtons()
    {
        const ImGuiTableFlags tableFlags = ImGuiTableFlags.SizingStretchSame;

        if (ImGui.BeginTable("ActionButtonsTable", 4, tableFlags))
        {
            using var style = ImRaii.PushStyle(ImGuiStyleVar.FrameBorderSize, 0f);

            ImGui.TableNextColumn();
            var folderTooltip =
                _selectedSnapshot == null
                    ? "Select a snapshot to open its folder."
                    : "Open snapshot folder in file explorer.";
            if (
                UiHelpers.DrawStretchedIconButtonWithText(
                    FontAwesomeIcon.FolderOpen,
                    "Open Folder",
                    folderTooltip,
                    _selectedSnapshot == null
                )
            )
                if (_selectedSnapshot != null)
                    Util.OpenLink(_selectedSnapshot.FullName);

            ImGui.TableNextColumn();
            if (
                UiHelpers.DrawStretchedIconButtonWithText(
                    FontAwesomeIcon.FileImport,
                    "Import MCDF",
                    "Import a Mare Chara File (.mcdf) as a new snapshot."
                )
            )
                _snappy.FileDialogManager.OpenFileDialog(
                    "Import MCDF",
                    ".mcdf",
                    (status, path) =>
                    {
                        if (!status || !path.Any() || !File.Exists(path[0]))
                            return;
                        _mcdfManager.ImportMcdf(path[0]);
                    },
                    1,
                    _snappy.Configuration.WorkingDirectory
                );

            ImGui.TableNextColumn();
            var exportIsInProgress = _pmpExportManager.IsExporting;
            var exportDisabled = _selectedSnapshot == null || exportIsInProgress;
            string exportTooltip;
            if (exportIsInProgress)
                exportTooltip = "An export is already in progress...";
            else if (_selectedSnapshot == null)
                exportTooltip = "Select a snapshot to export it as a Penumbra Mod Pack.";
            else
                exportTooltip = "Export the selected snapshot as a Penumbra Mod Pack (.pmp).";
            if (
                UiHelpers.DrawStretchedIconButtonWithText(
                    FontAwesomeIcon.FileExport,
                    "Export to PMP",
                    exportTooltip,
                    exportDisabled
                )
            )
            {
                Notify.Info($"Starting background export for '{_selectedSnapshot!.Name}'...");
                _snappy.ExecuteBackgroundTask(() => _pmpExportManager.SnapshotToPMPAsync(_selectedSnapshot!.FullName));
            }

            ImGui.TableNextColumn();
            var renameActorDisabled = _selectedSnapshot == null;
            var renameActorTooltip = renameActorDisabled
                ? "Select a snapshot to rename its Source Actor."
                : $"Rename the Source Actor for this snapshot.\nCurrent: '{_selectedSnapshotInfo?.SourceActor ?? "Unknown"}'";
            if (
                UiHelpers.DrawStretchedIconButtonWithText(
                    FontAwesomeIcon.UserEdit,
                    "Rename Actor",
                    renameActorTooltip,
                    renameActorDisabled
                )
            )
                _openRenameActorPopup = true;

            ImGui.EndTable();
        }
    }

    private void DrawHistoryTabs()
    {
        using var tabBar = ImUtf8.TabBar("HistoryTabs"u8);
        if (!tabBar)
            return;

        using (var tab = ImUtf8.TabItem("Glamourer"u8))
        {
            if (tab)
                DrawHistoryList("Glamourer", _glamourerHistory.Entries);
        }

        using (var tab = ImUtf8.TabItem("Customize+"u8))
        {
            if (tab)
                DrawHistoryList("Customize+", _customizeHistory.Entries);
        }
    }

    private void SetHistoryEntryDescription(HistoryEntryBase entry, string newDescription)
    {
        entry.Description = newDescription;
        SaveHistory();
        Notify.Success("History entry renamed.");
        _historyEntryToRename = null;
    }

    private void DrawHistoryList<T>(string type, List<T> entries)
        where T : HistoryEntryBase
    {
        using var color = ImRaii.PushColor(
            ImGuiCol.ChildBg,
            ImGui.GetColorU32(ImGuiCol.FrameBgHovered)
        );
        using var style = ImRaii.PushStyle(
            ImGuiStyleVar.ChildRounding,
            5f * ImGuiHelpers.GlobalScale
        );
        using var child = ImUtf8.Child(
            "HistoryList" + type,
            new Vector2(0, -1),
            false,
            ImGuiWindowFlags.HorizontalScrollbar
        );
        if (!child)
            return;

        var tableId = $"HistoryTable{type}";
        using var table = ImUtf8.Table(
            tableId,
            2,
            ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingFixedFit
        );
        if (!table)
            return;

        ImUtf8.TableSetupColumn("Description", ImGuiTableColumnFlags.WidthStretch);
        ImUtf8.TableSetupColumn(
            "Controls",
            ImGuiTableColumnFlags.WidthFixed,
            120f * ImGuiHelpers.GlobalScale
        );

        var rowHeight = ImGui.GetFrameHeight() + 20f * ImGuiHelpers.GlobalScale;

        for (var i = entries.Count - 1; i >= 0; i--)
        {
            ImGui.TableNextRow(ImGuiTableRowFlags.None, rowHeight);
            ImGui.TableNextColumn();
            var entry = entries[i];

            var initialY = ImGui.GetCursorPosY();
            var frameHeight = ImGui.GetFrameHeight();
            ImGui.SetCursorPosY(initialY + (rowHeight - frameHeight) / 2f);

            if (_historyEntryToRename == entry)
            {
                var onCommit = () => SetHistoryEntryDescription(entry, _tempHistoryEntryName);
                Action onCancel = () => _historyEntryToRename = null;
                UiHelpers.DrawInlineRename($"rename_{i}", ref _tempHistoryEntryName, onCommit, onCancel);
            } else
            {
                var description = entry.Description;
                if (string.IsNullOrEmpty(description))
                    description = "Unnamed Entry";
                ImUtf8.Text(description);
            }

            ImGui.TableNextColumn();

            var buttonHeight = ImGui.GetFrameHeight();
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() + (rowHeight - buttonHeight) / 2f);
            DrawHistoryEntryControls(type, entry);
        }
    }

    private void DrawHistoryEntryControls<T>(string type, T entry)
        where T : HistoryEntryBase
    {
        if (_historyEntryToRename == entry)
            return;

        using var id = ImRaii.PushId(entry.GetHashCode());
        using var style = ImRaii.PushStyle(
            ImGuiStyleVar.ItemSpacing,
            new Vector2(6 * ImGuiHelpers.GlobalScale, 0)
        );

        if (
            ImUtf8.IconButton(
                FontAwesomeIcon.Download,
                "Load this entry",
                default,
                !_isActorModifiable
            )
        )
            _snapshotApplicationService.LoadSnapshot(
                player!,
                objIdxSelected!.Value,
                _selectedSnapshot!.FullName,
                entry as GlamourerHistoryEntry,
                entry as CustomizeHistoryEntry
            );
        ImGui.SameLine();

        if (
            ImUtf8.IconButton(
                FontAwesomeIcon.Copy,
                "Copy Data to Clipboard",
                default
            )
        )
        {
            var textToCopy = string.Empty;
            if (entry is GlamourerHistoryEntry g)
                textToCopy = g.GlamourerString;
            else if (entry is CustomizeHistoryEntry c)
                textToCopy = c.CustomizeTemplate;

            if (!string.IsNullOrEmpty(textToCopy))
            {
                ImUtf8.SetClipboardText(textToCopy);
                Notify.Info("Copied data to clipboard.");
            }
        }

        ImGui.SameLine();

        if (ImUtf8.IconButton(FontAwesomeIcon.Pen, "Rename Entry", default))
        {
            _historyEntryToRename = entry;
            _tempHistoryEntryName = entry.Description ?? "";
            ImGui.SetKeyboardFocusHere(-1);
        }

        ImGui.SameLine();

        if (ImUtf8.IconButton(FontAwesomeIcon.Trash, "Delete Entry", default)) _historyEntryToDelete = entry;
    }
}
