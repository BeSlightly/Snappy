using Dalamud.Interface.Colors;
using Snappy.Services.SnapshotManager;

namespace Snappy.UI.Windows;

public partial class MainWindow
{
    private void SetHistoryEntryDescription(HistoryEntryBase entry, string newDescription)
    {
        var previousDescription = entry.Description;
        entry.Description = newDescription;
        if (!SaveHistory())
        {
            entry.Description = previousDescription;
            Notify.Error("Failed to save the renamed history entry.");
            return;
        }

        Notify.Success("History entry renamed.");
        _historyEntryToRename = null;
    }

    private void DrawHistoryList<T>(string type, List<T> entries)
        where T : HistoryEntryBase
    {
        using var child = Im.Child.Begin(
            "HistoryList" + type,
            new Vector2(0, -1),
            false,
            WindowFlags.HorizontalScrollbar
        );
        if (!child)
            return;

        var tableId = $"HistoryTable{type}";
        using var table = Im.Table.Begin(
            tableId,
            2,
            TableFlags.RowBackground | TableFlags.SizingFixedFit
        );
        if (!table)
            return;

        var spacingX = 6f * ImGuiHelpers.GlobalScale;
        var buttonSize = ImGui.GetFrameHeight();
        const int buttonCount = 5;
        var controlsWidth = buttonCount * buttonSize + (buttonCount - 1) * spacingX
                            + ImGui.GetStyle().CellPadding.X * 2
                            + 8f * ImGuiHelpers.GlobalScale;

        table.SetupColumn("Description", TableColumnFlags.WidthStretch);
        table.SetupColumn(
            "Controls",
            TableColumnFlags.WidthFixed,
            controlsWidth
        );

        // Comfortable row padding so entries don't feel stacked on top of each other.
        var rowHeight = ImGui.GetFrameHeight() + 16f * ImGuiHelpers.GlobalScale;
        var totalEntries = entries.Count;
        var activeSnapshot = TryGetSelectedActor(out var selectedActor)
            ? _activeSnapshotManager.GetSnapshotForCharacter(selectedActor)
            : null;
        var selectedSnapshotPath = _selectedSnapshot == null ? null : Path.GetFullPath(_selectedSnapshot.FullName);
        using var clipper = new Im.ListClipper(totalEntries, rowHeight);
        foreach (var row in clipper)
        {
            var i = totalEntries - 1 - row;
            var entry = entries[i];
            ImGui.TableNextRow(ImGuiTableRowFlags.None, rowHeight);
            ImGui.TableNextColumn();

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
                Im.Text(description);
            }

            ImGui.TableNextColumn();

            var buttonHeight = ImGui.GetFrameHeight();
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() + (rowHeight - buttonHeight) / 2f);
            DrawHistoryEntryControls(type, entry, activeSnapshot, selectedSnapshotPath);
        }
    }

    private void DrawHistoryEntryControls<T>(string type, T entry, ActiveSnapshot? activeSnapshot,
        string? selectedSnapshotPath)
        where T : HistoryEntryBase
    {
        if (_historyEntryToRename == entry)
            return;

        using var id = ImRaii.PushId(entry.GetHashCode());
        var spacingX = 6f * ImGuiHelpers.GlobalScale;
        var buttonSize = ImGui.GetFrameHeight();
        const int buttonCount = 5;
        var totalWidth = buttonCount * buttonSize + (buttonCount - 1) * spacingX;

        using var style = ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, new Vector2(spacingX, 0));

        var available = ImGui.GetContentRegionAvail().X;
        var startX = ImGui.GetCursorPosX() + Math.Max(0, available - totalWidth);
        ImGui.SetCursorPosX(startX);

        var isApplied = IsHistoryEntryApplied(entry, activeSnapshot, selectedSnapshotPath);
        var loadTooltip = isApplied
            ? "Currently applied to the selected actor. Click to reapply this entry."
            : "Load this entry";
        var loadClicked = false;
        if (isApplied)
        {
            // Check (not CheckCircle) stays inside the square icon button cleanly.
            using var appliedColor = ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.HealerGreen);
            loadClicked = UiHelpers.IconButton(FontAwesomeIcon.Check, loadTooltip, default,
                !_isActorModifiable);
        }
        else
        {
            loadClicked = UiHelpers.IconButton(FontAwesomeIcon.Download, loadTooltip, default,
                !_isActorModifiable);
        }

        if (loadClicked)
        {
            var selectedSnapshot = _selectedSnapshot;
            if (TryGetSelectedActor(out var selectedActor) && _objIdxSelected != null && selectedSnapshot != null)
            {
                var loadComponents = entry is CustomizeHistoryEntry
                    ? SnapshotLoadComponents.CustomizePlus
                    : SnapshotLoadComponents.All;
                _snapshotApplicationService.LoadSnapshot(
                    selectedActor,
                    _objIdxSelected.Value,
                    selectedSnapshot.FullName,
                    entry as GlamourerHistoryEntry,
                    entry as CustomizeHistoryEntry,
                    loadComponents
                );
            }
        }
        ImGui.SameLine();

        if (
            UiHelpers.IconButton(
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
                Im.Clipboard.Set(textToCopy);
                Notify.Info("Copied data to clipboard.");
            }
        }

        ImGui.SameLine();

        var pmpDisabled = _selectedSnapshot == null || _pmpExportManager.IsExporting;
        var pmpTooltip = _pmpExportManager.IsExporting
            ? "An export is already in progress..."
            : "Export this entry's state to a Penumbra Mod Pack (.pmp).";
        if (UiHelpers.IconButton(FontAwesomeIcon.BoxOpen, pmpTooltip, default, pmpDisabled))
        {
            var selectedSnapshot = _selectedSnapshot;
            if (selectedSnapshot != null)
            {
                var defaultName =
                    $"{selectedSnapshot.Name}_{PathSanitizer.SanitizeFileSystemName(entry.Description, "entry")}.pmp";
                var snapshotPath = selectedSnapshot.FullName;
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
                            _pmpExportManager.SnapshotToPMPAsync(snapshotPath, path, mapId));
                    },
                    _snappy.Configuration.WorkingDirectory);
            }
        }

        ImGui.SameLine();

        if (UiHelpers.IconButton(FontAwesomeIcon.Pen, "Rename Entry", default))
        {
            _historyEntryToRename = entry;
            _tempHistoryEntryName = entry.Description ?? "";
            ImGui.SetKeyboardFocusHere(-1);
        }

        ImGui.SameLine();

        if (UiHelpers.IconButton(FontAwesomeIcon.Trash, "Delete Entry", default)) _historyEntryToDelete = entry;
    }

    private static bool IsHistoryEntryApplied(HistoryEntryBase entry, ActiveSnapshot? activeSnapshot,
        string? selectedSnapshotPath)
    {
        if (activeSnapshot == null || selectedSnapshotPath == null)
            return false;

        return entry switch
        {
            GlamourerHistoryEntry => string.Equals(activeSnapshot.GlamourerSnapshotPath, selectedSnapshotPath,
                                         StringComparison.OrdinalIgnoreCase)
                                     && string.Equals(activeSnapshot.GlamourerHistoryTimestamp, entry.Timestamp,
                                         StringComparison.Ordinal),
            CustomizeHistoryEntry => string.Equals(activeSnapshot.CustomizeSnapshotPath, selectedSnapshotPath,
                                         StringComparison.OrdinalIgnoreCase)
                                     && string.Equals(activeSnapshot.CustomizeHistoryTimestamp, entry.Timestamp,
                                         StringComparison.Ordinal),
            _ => false
        };
    }

}
