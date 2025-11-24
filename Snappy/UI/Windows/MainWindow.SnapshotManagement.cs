using Dalamud.Utility;
using System.Globalization;
using Dalamud.Interface.Colors;
using System;
using System.IO;
using System.Numerics;

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

        using (var tab = ImUtf8.TabItem("PCP Export"u8))
        {
            if (tab)
                DrawPcpExportTab();
        }
    }

    private void DrawPcpExportTab()
    {
        // Content container (use default styling similar to Penumbra Effective Changes)
        using var _pcpChild = ImUtf8.Child("PcpExportContent", new Vector2(0, -1), false, ImGuiWindowFlags.None);
        if (!_pcpChild)
            return;
        // Instruction (subtle helper text)
        using (var _pcpTextGrey = ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.DalamudGrey))
        {
            ImUtf8.Text(
                "Select which Glamourer and Customize+ history entries to include in the PCP export.\nIf no selection is made, the latest entry will be used for each."u8);
        }

        ImGui.Separator();

        // Two columns for selectors
        if (ImGui.BeginTable("PcpExportSelectors", 2, ImGuiTableFlags.SizingStretchProp))
        {
            ImGui.TableSetupColumn("Left", ImGuiTableColumnFlags.WidthStretch, 0.5f);
            ImGui.TableSetupColumn("Right", ImGuiTableColumnFlags.WidthStretch, 0.5f);
            ImGui.TableSetupColumn("Left", ImGuiTableColumnFlags.WidthStretch, 0.5f);
            ImGui.TableSetupColumn("Right", ImGuiTableColumnFlags.WidthStretch, 0.5f);

            // Glamourer selector
            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            ImUtf8.Text("Glamourer Entry"u8);
            ImGui.Spacing();

            var glamourerPreview = _pcpSelectedGlamourerEntry != null
                ? FormatHistoryEntryPreview(_pcpSelectedGlamourerEntry)
                : "Use latest entry";
            ImGui.SetNextItemWidth(-1);
            if (ImGui.BeginCombo("##PcpGlamourerEntry", glamourerPreview))
            {
                var useLatestSelected = _pcpSelectedGlamourerEntry == null;
                if (ImGui.Selectable("Use latest entry", useLatestSelected))
                    _pcpSelectedGlamourerEntry = null;
                if (useLatestSelected) ImGui.SetItemDefaultFocus();

                for (var i = _glamourerHistory.Entries.Count - 1; i >= 0; i--)
                {
                    var entry = _glamourerHistory.Entries[i];
                    var label = FormatHistoryEntryPreview(entry);
                    var isSelected = ReferenceEquals(_pcpSelectedGlamourerEntry, entry);
                    if (ImGui.Selectable(label, isSelected))
                        _pcpSelectedGlamourerEntry = entry;
                    if (isSelected) ImGui.SetItemDefaultFocus();
                }

                ImGui.EndCombo();
            }

            ImUtf8.HoverTooltip(
                "Pick a specific Glamourer design to include in the PCP. If not selected, the latest design will be used.");

            // Customize+ selector
            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            ImUtf8.Text("Customize+ Entry"u8);
            ImGui.Spacing();

            var customizePreview = _pcpSelectedCustomizeEntry != null
                ? FormatHistoryEntryPreview(_pcpSelectedCustomizeEntry)
                : "Use latest entry";
            ImGui.SetNextItemWidth(-1);
            if (ImGui.BeginCombo("##PcpCustomizeEntry", customizePreview))
            {
                var useLatestSelected = _pcpSelectedCustomizeEntry == null;
                if (ImGui.Selectable("Use latest entry", useLatestSelected))
                    _pcpSelectedCustomizeEntry = null;
                if (useLatestSelected) ImGui.SetItemDefaultFocus();

                for (var i = _customizeHistory.Entries.Count - 1; i >= 0; i--)
                {
                    var entry = _customizeHistory.Entries[i];
                    var label = FormatHistoryEntryPreview(entry);
                    var isSelected = ReferenceEquals(_pcpSelectedCustomizeEntry, entry);
                    if (ImGui.Selectable(label, isSelected))
                        _pcpSelectedCustomizeEntry = entry;
                    if (isSelected) ImGui.SetItemDefaultFocus();
                }

                ImGui.EndCombo();
            }

            ImUtf8.HoverTooltip(
                "Pick a specific Customize+ template to include in the PCP. If not selected, the latest template will be used.");

            ImGui.EndTable();
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // Player details overrides
        if (ImGui.BeginTable("PcpExportPlayerDetails", 2, ImGuiTableFlags.SizingStretchProp))
        {
            ImGui.TableSetupColumn("Left", ImGuiTableColumnFlags.WidthStretch, 0.5f);
            ImGui.TableSetupColumn("Right", ImGuiTableColumnFlags.WidthStretch, 0.5f);

            // Player Name input
            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            ImUtf8.Text("Player Name"u8);
            ImGui.Spacing();
            ImGui.SetNextItemWidth(-1);
            ImUtf8.InputText(
                "##PcpPlayerName"u8,
                ref _pcpPlayerNameOverride,
                flags: ImGuiInputTextFlags.AutoSelectAll
            );
            ImUtf8.HoverTooltip(
                "Name written to PCP's character.json Actor.PlayerName. Defaults to snapshot's Source Actor."u8);

            // Homeworld selection (reusing searchable combo pattern)
            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            ImUtf8.Text("Homeworld"u8);
            ImGui.Spacing();

            // Use Lifestream-style WorldSelector grouped by Region/Data Center
            var tmpWorldId = _pcpSelectedWorldIdOverride ?? 0; // 0 means 'use snapshot'
            _pcpWorldSelector.EmptyName = _selectedSnapshotInfo?.SourceWorldId is { } swid2
                                          && Snappy.WorldNames.TryGetValue((uint)swid2, out var snapWorldName)
                ? $"Use snapshot's world ({snapWorldName})"
                : "Use snapshot's world";
            ImGui.SetNextItemWidth(-1);
            _pcpWorldSelector.Draw(ref tmpWorldId);
            _pcpSelectedWorldIdOverride = tmpWorldId == 0 ? null : tmpWorldId;
            ImUtf8.HoverTooltip("Search and select the player's Homeworld. Written to PCP's Actor.HomeWorld (ID)."u8);

            ImGui.EndTable();
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // Export button centered with icon (matching PMP style)
        var buttonWidth = 220f * ImGuiHelpers.GlobalScale;
        var cursorX = (ImGui.GetContentRegionAvail().X - buttonWidth) * 0.5f;
        var cursorPos = ImGui.GetCursorPosX();
        ImGui.SetCursorPosX(cursorPos + Math.Max(0, cursorX));

        var exportDisabled = _selectedSnapshot == null || string.IsNullOrWhiteSpace(_pcpPlayerNameOverride);
        var exportTooltip =
            "Export the selected entries to a Penumbra Character Package (.pcp). If an entry is not selected, the latest will be used.";
        if (UiHelpers.DrawStretchedIconButtonWithText(FontAwesomeIcon.FileExport, "Export Selected to PCP",
                exportTooltip, exportDisabled, buttonWidth))
            _snappy.FileDialogManager.SaveFileDialog(
                "Export PCP",
                ".pcp",
                $"{_selectedSnapshot!.Name}.pcp",
                ".pcp",
                (status, path) =>
                {
                    if (!status || string.IsNullOrEmpty(path))
                        return;

                    Notify.Info($"Starting PCP export for '{_selectedSnapshot!.Name}'...");
                    var glam = _pcpSelectedGlamourerEntry;
                    var cust = _pcpSelectedCustomizeEntry;
                    var nameOverride = _pcpPlayerNameOverride;
                    var worldOverride = _pcpSelectedWorldIdOverride;
                    _snappy.ExecuteBackgroundTask(() =>
                        _pcpManager.ExportPcp(_selectedSnapshot!.FullName, path, glam, cust, nameOverride,
                            worldOverride));
                },
                _snappy.Configuration.WorkingDirectory
            );
        ImGui.Spacing();
        using (var warningColor = ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.DalamudYellow))
        {
            var warningText = "Warning: PCP export is experimental. Please report any issues on GitHub.";
            var textWidth = ImUtf8.CalcTextSize(warningText).X;
            var availableWidth = ImGui.GetContentRegionAvail().X;

            // Calculate the starting X position to center the text
            var cursorPosX = (availableWidth - textWidth) * 0.5f;
            if (cursorPosX > 0) ImGui.SetCursorPosX(ImGui.GetCursorPosX() + cursorPosX);

            // Use ImUtf8.Text for precise centering
            ImUtf8.Text(warningText);
        }
    }


    private static string FormatHistoryEntryPreview(HistoryEntryBase entry)
    {
        DateTime.TryParse(entry.Timestamp, CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsedUtc);
        var time = parsedUtc == default ? entry.Timestamp : parsedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
        var name = string.IsNullOrWhiteSpace(entry.Description) ? "Unnamed Entry" : entry.Description;
        return $"{name}  ({time})";
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
            260f * ImGuiHelpers.GlobalScale
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
            }
            else
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
        var spacingX = 6 * ImGuiHelpers.GlobalScale;
        var buttonSize = ImGui.GetFrameHeight();
        const int buttonCount = 6;
        var totalWidth = buttonCount * buttonSize + (buttonCount - 1) * spacingX;

        using var style = ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, new Vector2(spacingX, 0));

        var available = ImGui.GetContentRegionAvail().X;
        var startX = ImGui.GetCursorPosX() + Math.Max(0, available - totalWidth);
        ImGui.SetCursorPosX(startX);

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

        var pmpDisabled = _selectedSnapshot == null || _pmpExportManager.IsExporting;
        var pmpTooltip = _pmpExportManager.IsExporting
            ? "An export is already in progress..."
            : "Export this entry's state to a Penumbra Mod Pack (.pmp).";
            if (ImUtf8.IconButton(FontAwesomeIcon.BoxOpen, pmpTooltip, default, pmpDisabled))
            {
                var defaultName =
                    $"{_selectedSnapshot!.Name}_{SanitizeForFileName(entry.Description ?? "entry")}.pmp";
                _snappy.FileDialogManager.SaveFileDialog(
                "Export PMP for Entry",
                ".pmp",
                defaultName,
                ".pmp",
                (status, path) =>
                {
                    if (!status || string.IsNullOrEmpty(path))
                        return;

                    Notify.Info($"Starting PMP export for entry '{entry.Description ?? ""}'...");
                    var mapId = entry.FileMapId ?? _selectedSnapshotInfo?.CurrentFileMapId;
                    _snappy.ExecuteBackgroundTask(() =>
                        _pmpExportManager.SnapshotToPMPAsync(_selectedSnapshot!.FullName, path, mapId));
                },
                _snappy.Configuration.WorkingDirectory);
            }

        ImGui.SameLine();

        var pcpDisabled = _selectedSnapshot == null || string.IsNullOrWhiteSpace(_pcpPlayerNameOverride);
        var pcpTooltip = pcpDisabled
            ? "Set a player name and select a snapshot to export to PCP."
            : "Export this entry's state to a PCP.";
        if (ImUtf8.IconButton(FontAwesomeIcon.FileExport, pcpTooltip, default, pcpDisabled))
        {
            var defaultName =
                $"{_selectedSnapshot!.Name}_{SanitizeForFileName(entry.Description ?? "entry")}.pcp";
            _snappy.FileDialogManager.SaveFileDialog(
                "Export PCP for Entry",
                ".pcp",
                defaultName,
                ".pcp",
                (status, path) =>
                {
                    if (!status || string.IsNullOrEmpty(path))
                        return;

                    Notify.Info($"Starting PCP export for entry '{entry.Description ?? ""}'...");
                    var glam = entry as GlamourerHistoryEntry;
                    var cust = entry as CustomizeHistoryEntry;
                    var nameOverride = _pcpPlayerNameOverride;
                    var worldOverride = _pcpSelectedWorldIdOverride;
                    _snappy.ExecuteBackgroundTask(() =>
                        _pcpManager.ExportPcp(_selectedSnapshot!.FullName, path, glam, cust, nameOverride,
                            worldOverride));
                },
                _snappy.Configuration.WorkingDirectory);
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

    private static string SanitizeForFileName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "entry";

        var sanitized = value;
        foreach (var c in Path.GetInvalidFileNameChars()) sanitized = sanitized.Replace(c, '_');
        sanitized = sanitized.Trim('.', ' ');

        if (string.IsNullOrWhiteSpace(sanitized))
            sanitized = "entry";

        return sanitized;
    }
}
