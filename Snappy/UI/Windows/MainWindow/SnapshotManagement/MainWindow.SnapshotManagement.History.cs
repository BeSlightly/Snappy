using Snappy.Services.SnapshotManager;

namespace Snappy.UI.Windows;

public partial class MainWindow
{
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
        var totalEntries = entries.Count;
        var clipper = new ImGuiListClipper();
        clipper.Begin(totalEntries, rowHeight);
        while (clipper.Step())
        {
            for (var row = clipper.DisplayStart; row < clipper.DisplayEnd; row++)
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
                    ImUtf8.Text(description);
                }

                ImGui.TableNextColumn();

                var buttonHeight = ImGui.GetFrameHeight();
                ImGui.SetCursorPosY(ImGui.GetCursorPosY() + (rowHeight - buttonHeight) / 2f);
                DrawHistoryEntryControls(type, entry);
            }
        }

        clipper.End();
    }

    private void DrawHistoryEntryControls<T>(string type, T entry)
        where T : HistoryEntryBase
    {
        if (_historyEntryToRename == entry)
            return;

        using var id = ImRaii.PushId(entry.GetHashCode());
        var spacingX = 6 * ImGuiHelpers.GlobalScale;
        var buttonSize = ImGui.GetFrameHeight();
        const int buttonCount = 5;
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
