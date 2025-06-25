using System;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Utility;
using ECommons.ImGuiMethods;
using ImGuiNET;
using OtterGui.Raii;
using OtterGui.Text;
using Snappy.Models;

namespace Snappy.UI
{
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
            {
                DrawHistoryTabs();
            }
            else if (_snapshotList.Length > 0)
            {
                ImUtf8.Text("Select a snapshot to manage."u8);
            }
            else
            {
                ImUtf8.Text(
                    "No snapshots found. Select an actor and click 'Save Snapshot' to create one."u8
                );
            }
        }

        private void DrawSnapshotHeader()
        {
            ImGui.AlignTextToFramePadding();
            ImUtf8.Text("SNAPSHOT:"u8);
            ImGui.SameLine();

            bool buttonsDisabled = _selectedSnapshot == null;

            if (_isRenamingSnapshot)
            {
                var iconButtonSize = ImGui.GetFrameHeight();
                var buttonsWidth = iconButtonSize * 2 + ImGui.GetStyle().ItemSpacing.X;
                var inputWidth =
                    ImGui.GetContentRegionAvail().X
                    - buttonsWidth
                    - (ImGui.GetStyle().ItemSpacing.X * 2);

                ImGui.SetNextItemWidth(inputWidth);
                using (var color = ImRaii.PushColor(ImGuiCol.Border, new Vector4(1, 1, 0, 0.5f)))
                {
                    if (
                        ImUtf8.InputText(
                            "##SnapshotRename"u8,
                            ref _tempSnapshotName,
                            flags: ImGuiInputTextFlags.EnterReturnsTrue
                                | ImGuiInputTextFlags.AutoSelectAll
                        )
                    )
                    {
                        RenameSnapshot();
                    }
                }

                ImGui.SameLine();
                if (ImUtf8.IconButton(FontAwesomeIcon.Check, size: default))
                {
                    RenameSnapshot();
                }
                ImGui.SameLine();
                if (ImUtf8.IconButton(FontAwesomeIcon.Times, size: default))
                {
                    _isRenamingSnapshot = false;
                }
            }
            else
            {
                float iconBarWidth = (ImGui.GetFrameHeight() + ImGui.GetStyle().ItemSpacing.X) * 3;
                float comboWidth =
                    ImGui.GetContentRegionAvail().X - iconBarWidth - ImGui.GetStyle().ItemSpacing.X;

                using var disabled = ImRaii.Disabled(_snapshotList.Length == 0);

                _snapshotCombo.Draw(
                    "##SnapshotSelector",
                    _selectedSnapshot?.Name ?? "Select a Snapshot...",
                    comboWidth
                );

                disabled.Dispose();

                if (ImGui.IsItemHovered() && ImGui.IsItemClicked(ImGuiMouseButton.Right))
                {
                    ClearSnapshotSelection();
                }
                ImUtf8.HoverTooltip("Right-click to clear selection.");

                if (ImGui.IsItemHovered() && _snapshotList.Length == 0)
                {
                    ImUtf8.HoverTooltip("No snapshots exist yet. Save one first."u8);
                }

                ImGui.SameLine();
                if (
                    ImUtf8.IconButton(
                        FontAwesomeIcon.Sync,
                        tooltip: "Refresh List",
                        size: default,
                        disabled: false
                    )
                )
                {
                    Plugin.InvokeSnapshotsUpdated();
                }

                ImGui.SameLine();
                if (
                    ImUtf8.IconButton(
                        FontAwesomeIcon.Pen,
                        tooltip: buttonsDisabled
                            ? "Select a snapshot to rename"
                            : "Rename Snapshot",
                        size: default,
                        disabled: buttonsDisabled
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
                        tooltip: buttonsDisabled
                            ? "Select a snapshot to delete"
                            : "Delete Snapshot",
                        size: default,
                        disabled: buttonsDisabled
                    )
                )
                {
                    _openDeleteSnapshotPopup = true;
                }
            }
        }

        private void RenameSnapshot()
        {
            _isRenamingSnapshot = false;
            if (
                _selectedSnapshot == null
                || _tempSnapshotName == _selectedSnapshot.Name
                || string.IsNullOrWhiteSpace(_tempSnapshotName)
            )
                return;

            try
            {
                var parent = Path.GetDirectoryName(_selectedSnapshot.FullName)!;
                var newPath = Path.Combine(parent, _tempSnapshotName);
                if (Directory.Exists(newPath))
                {
                    Notify.Error("A directory with that name already exists.");
                    return;
                }
                var oldName = _selectedSnapshot.Name;
                Directory.Move(_selectedSnapshot.FullName, newPath);

                var newlySelectedSnapshot = new Snapshot(newPath);
                Notify.Success($"Snapshot '{oldName}' renamed to '{_tempSnapshotName}'.");

                Plugin.InvokeSnapshotsUpdated();
                var newLeaf = _snapshotList.FirstOrDefault(l =>
                    l.Value.FullName == newlySelectedSnapshot.FullName
                );
                if (newLeaf != null)
                {
                    _snapshotCombo.SetSelection(newLeaf);
                }
            }
            catch (Exception e)
            {
                Notify.Error($"Could not rename snapshot.\n{e.Message}");
            }
        }

        private void DrawActionButtons()
        {
            var tableFlags = ImGuiTableFlags.SizingStretchSame;

            if (ImGui.BeginTable("ActionButtonsTable", 4, tableFlags))
            {
                using var style = ImRaii.PushStyle(ImGuiStyleVar.FrameBorderSize, 0f);

                ImGui.TableNextColumn();
                string folderTooltip =
                    _selectedSnapshot == null
                        ? "Select a snapshot to open its folder."
                        : "Open snapshot folder in file explorer.";
                if (
                    DrawStretchedIconButtonWithText(
                        FontAwesomeIcon.FolderOpen,
                        "Open Folder",
                        folderTooltip,
                        _selectedSnapshot == null
                    )
                )
                {
                    if (_selectedSnapshot != null)
                        Util.OpenLink(_selectedSnapshot.FullName);
                }

                ImGui.TableNextColumn();
                if (
                    DrawStretchedIconButtonWithText(
                        FontAwesomeIcon.FileImport,
                        "Import MCDF",
                        "Import a Mare Chara File (.mcdf) as a new snapshot."
                    )
                )
                {
                    Plugin.FileDialogManager.OpenFileDialog(
                        "Import MCDF",
                        ".mcdf",
                        (status, path) =>
                        {
                            if (!status || !path.Any() || !File.Exists(path[0]))
                                return;
                            Plugin.McdfManager.ImportMcdf(path[0]);
                        },
                        1,
                        Plugin.Configuration.WorkingDirectory
                    );
                }

                ImGui.TableNextColumn();
                var exportIsInProgress = Plugin.PmpManager.IsExporting;
                var exportDisabled = _selectedSnapshot == null || exportIsInProgress;
                string exportTooltip;
                if (exportIsInProgress)
                {
                    exportTooltip = "An export is already in progress...";
                }
                else if (_selectedSnapshot == null)
                {
                    exportTooltip = "Select a snapshot to export it as a Penumbra Mod Pack.";
                }
                else
                {
                    exportTooltip = "Export the selected snapshot as a Penumbra Mod Pack (.pmp).";
                }
                if (
                    DrawStretchedIconButtonWithText(
                        FontAwesomeIcon.FileExport,
                        "Export to PMP",
                        exportTooltip,
                        exportDisabled
                    )
                )
                {
                    Notify.Info($"Starting background export for '{_selectedSnapshot!.Name}'...");
                    Task.Run(() => Plugin.PmpManager.SnapshotToPMP(_selectedSnapshot!.FullName));
                }

                ImGui.TableNextColumn();
                var renameActorDisabled = _selectedSnapshot == null;
                string renameActorTooltip = renameActorDisabled
                    ? "Select a snapshot to rename its Source Actor."
                    : $"Rename the Source Actor for this snapshot.\nCurrent: '{_selectedSnapshotInfo?.SourceActor ?? "Unknown"}'";
                if (
                    DrawStretchedIconButtonWithText(
                        FontAwesomeIcon.UserEdit,
                        "Rename Actor",
                        renameActorTooltip,
                        renameActorDisabled
                    )
                )
                {
                    _openRenameActorPopup = true;
                }

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

        private void SetHistoryEntryDescription(object entry, string newDescription)
        {
            switch (entry)
            {
                case GlamourerHistoryEntry g:
                    g.Description = newDescription;
                    break;
                case CustomizeHistoryEntry c:
                    c.Description = newDescription;
                    break;
            }
            SaveHistory();
            Notify.Success("History entry renamed.");
            _historyEntryToRename = null;
        }

        private void DrawHistoryList<T>(string type, System.Collections.Generic.List<T> entries)
            where T : class
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

            string tableId = $"HistoryTable{type}";
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

            for (int i = entries.Count - 1; i >= 0; i--)
            {
                ImGui.TableNextRow(ImGuiTableRowFlags.None, rowHeight);
                ImGui.TableNextColumn();
                var entry = entries[i];

                var initialY = ImGui.GetCursorPosY();
                var frameHeight = ImGui.GetFrameHeight();
                ImGui.SetCursorPosY(initialY + (rowHeight - frameHeight) / 2f);

                if (_historyEntryToRename == entry)
                {
                    var iconButtonSize = ImGui.GetFrameHeight();
                    var buttonsWidth = iconButtonSize * 2 + ImGui.GetStyle().ItemSpacing.X;
                    var inputWidth =
                        ImGui.GetContentRegionAvail().X
                        - buttonsWidth
                        - ImGui.GetStyle().ItemSpacing.X;

                    ImGui.SetNextItemWidth(inputWidth);
                    using (
                        var borderColor = ImRaii.PushColor(
                            ImGuiCol.Border,
                            new Vector4(1, 1, 0, 0.5f)
                        )
                    )
                    {
                        string renameId = $"##rename_{i}";
                        if (
                            ImUtf8.InputText(
                                renameId,
                                ref _tempHistoryEntryName,
                                flags: ImGuiInputTextFlags.EnterReturnsTrue
                                    | ImGuiInputTextFlags.AutoSelectAll
                            )
                        )
                        {
                            SetHistoryEntryDescription(entry, _tempHistoryEntryName);
                        }
                    }

                    ImGui.SameLine();
                    if (ImUtf8.IconButton(FontAwesomeIcon.Check, size: default))
                    {
                        SetHistoryEntryDescription(entry, _tempHistoryEntryName);
                    }
                    ImGui.SameLine();
                    if (ImUtf8.IconButton(FontAwesomeIcon.Times, size: default))
                    {
                        _historyEntryToRename = null;
                    }
                }
                else
                {
                    var description =
                        (entry as GlamourerHistoryEntry)?.Description
                        ?? (entry as CustomizeHistoryEntry)?.Description
                        ?? "Unknown Entry";
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
            where T : class
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
                    tooltip: "Load this entry",
                    size: default,
                    disabled: !_isActorModifiable
                )
            )
            {
                Plugin.SnapshotManager.LoadSnapshot(
                    player!,
                    objIdxSelected!.Value,
                    _selectedSnapshot!.FullName,
                    entry as GlamourerHistoryEntry,
                    entry as CustomizeHistoryEntry
                );
            }
            ImGui.SameLine();

            if (
                ImUtf8.IconButton(
                    FontAwesomeIcon.Copy,
                    tooltip: "Copy Data to Clipboard",
                    size: default
                )
            )
            {
                string textToCopy = string.Empty;
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

            if (ImUtf8.IconButton(FontAwesomeIcon.Pen, tooltip: "Rename Entry", size: default))
            {
                _historyEntryToRename = entry;
                _tempHistoryEntryName =
                    (entry as GlamourerHistoryEntry)?.Description
                    ?? (entry as CustomizeHistoryEntry)?.Description
                    ?? "";
                ImGui.SetKeyboardFocusHere(-1);
            }
            ImGui.SameLine();

            if (ImUtf8.IconButton(FontAwesomeIcon.Trash, tooltip: "Delete Entry", size: default))
            {
                _historyEntryToDelete = entry;
            }
        }
    }
}
