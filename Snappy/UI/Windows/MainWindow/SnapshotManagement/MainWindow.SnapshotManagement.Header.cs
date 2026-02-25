using Dalamud.Utility;
using System.IO;

namespace Snappy.UI.Windows;

public partial class MainWindow
{
    private void DrawSnapshotHeader()
    {
        ImGui.AlignTextToFramePadding();
        Im.Text("SNAPSHOT:"u8);
        ImGui.SameLine();

        var buttonsDisabled = _selectedSnapshot == null;

        if (_isRenamingSnapshot)
        {
            UiHelpers.DrawInlineRename("SnapshotRename", ref _tempSnapshotName, HandleSnapshotRename,
                () => _isRenamingSnapshot = false);
        }
        else
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
            Im.Tooltip.OnHover("Right-click to clear selection.");

            if (ImGui.IsItemHovered() && _snapshotList.Length == 0)
                Im.Tooltip.OnHover("No snapshots exist yet. Save one first."u8);

            ImGui.SameLine();
            if (
                UiHelpers.IconButton(
                    FontAwesomeIcon.Sync,
                    "Refresh List",
                    default,
                    false
                )
            )
                _snappy.InvokeSnapshotsUpdated();

            ImGui.SameLine();
            if (
                UiHelpers.IconButton(
                    FontAwesomeIcon.Pen,
                    buttonsDisabled
                        ? "Select a snapshot to rename"
                        : "Rename Snapshot",
                    default,
                    buttonsDisabled
                )
            )
            {
                if (_selectedSnapshot != null)
                {
                    _isRenamingSnapshot = true;
                    _tempSnapshotName = _selectedSnapshot.Name;
                    ImGui.SetKeyboardFocusHere(-1);
                }
            }

            ImGui.SameLine();
            if (
                UiHelpers.IconButton(
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

        _snappy.ExecuteBackgroundTask(async () =>
        {
            await Task.Run(() => _snapshotFileService.RenameSnapshot(oldPath, newName));
            _snappy.QueueAction(_snappy.InvokeSnapshotsUpdated);
        });

        ClearSnapshotSelection();
    }

    private void DrawActionButtons()
    {
        const ImGuiTableFlags tableFlags = ImGuiTableFlags.SizingStretchSame;

        if (ImGui.BeginTable("ActionButtonsTable", 3, tableFlags))
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
                    "Import MCDF/PCP",
                    "Import a Mare Chara File (.mcdf) or Penumbra Character Package (.pcp) as a new snapshot."
                )
            )
                _snappy.FileDialogManager.OpenFileDialog(
                    "Import MCDF/PCP",
                    "Character Packages{.mcdf,.pcp},MCDF{.mcdf},PCP{.pcp}",
                    (status, path) =>
                    {
                        if (!status || !path.Any())
                            return;
                        var selected = path[0];
                        if (!File.Exists(selected))
                            return;

                        var ext = Path.GetExtension(selected).ToLowerInvariant();
                        if (ext == ".pcp")
                            _pcpManager.ImportPcp(selected);
                        else if (ext == ".mcdf")
                            _mcdfManager.ImportMcdf(selected);
                        else
                            Notify.Error("Unsupported file type. Please select a .mcdf or .pcp file.");
                    },
                    1,
                    _snappy.Configuration.WorkingDirectory
                );

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
}
