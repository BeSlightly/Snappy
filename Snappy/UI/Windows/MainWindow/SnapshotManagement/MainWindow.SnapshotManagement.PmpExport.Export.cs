using Snappy.Common;
using Snappy.Features.Pmp;

namespace Snappy.UI.Windows;

public partial class MainWindow
{
    private void DrawPmpExportButton()
    {
        if (_selectedSnapshot == null || _selectedSnapshotInfo == null || _pmpChangedItems == null)
            return;

        var selectedKeys = GetSelectedPmpItemKeys();
        var exportDisabled = selectedKeys.Count == 0 || _pmpExportManager.IsExporting;
        var tooltip = _pmpExportManager.IsExporting
            ? "An export is already in progress..."
            : "Export a PMP containing only the selected items.";

        var buttonWidth = 240f * ImGuiHelpers.GlobalScale;
        var cursorX = (ImGui.GetContentRegionAvail().X - buttonWidth) * 0.5f;
        var cursorPos = ImGui.GetCursorPosX();
        ImGui.SetCursorPosX(cursorPos + Math.Max(0, cursorX));

        if (UiHelpers.DrawStretchedIconButtonWithText(FontAwesomeIcon.BoxOpen, "Export Selected to PMP",
                tooltip, exportDisabled, buttonWidth))
        {
            var snapshotPath = _selectedSnapshot.FullName;
            var snapshotName = _selectedSnapshot.Name;
            var fileMapId = _pmpSelectedFileMapId ?? _selectedSnapshotInfo.CurrentFileMapId;
            var resolvedFileMap = FileMapUtil.ResolveFileMapWithEmptyFallback(_selectedSnapshotInfo, fileMapId);

            var selectedPaths = BuildSelectedPmpGamePaths(selectedKeys);
            var filesDirectory = SnapshotPaths.From(snapshotPath).FilesDirectory;
            selectedPaths = PmpExportDependencyResolver.ExpandMtrlDependencies(
                selectedPaths,
                resolvedFileMap,
                filesDirectory);
            var filteredFileMap = resolvedFileMap
                .Where(kvp => selectedPaths.Contains(kvp.Key))
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.OrdinalIgnoreCase);
            var manipBase = FileMapUtil.ResolveManipulation(_selectedSnapshotInfo, fileMapId);

            _snappy.FileDialogManager.SaveFileDialog(
                "Export PMP",
                ".pmp",
                $"{snapshotName}.pmp",
                ".pmp",
                (status, path) =>
                {
                    if (!status || string.IsNullOrEmpty(path))
                        return;

                    Notify.Info($"Starting PMP export for '{snapshotName}'...");
                    _snappy.ExecuteBackgroundTask(async () =>
                    {
                        var filteredManip = await _snapshotChangedItemService
                            .FilterManipulationsAsync(manipBase, selectedKeys);
                        await _pmpExportManager.SnapshotToPMPAsync(snapshotPath, path, fileMapId,
                            filteredFileMap, filteredManip);
                    });
                },
                _snappy.Configuration.WorkingDirectory);
        }
    }

    private void SetAllPmpSelections(bool selected)
    {
        var keys = _pmpItemSelection.Keys.ToArray();
        foreach (var key in keys)
            _pmpItemSelection[key] = selected;
    }

    private HashSet<string> GetSelectedPmpItemKeys()
    {
        var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in _pmpItemSelection)
            if (value)
                selected.Add(key);

        return selected;
    }

    private HashSet<string> BuildSelectedPmpGamePaths(HashSet<string> selectedKeys)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (_pmpChangedItems == null)
            return paths;

        foreach (var key in selectedKeys)
        {
            if (!_pmpChangedItems.GamePathsByItemKey.TryGetValue(key, out var itemPaths))
                continue;

            foreach (var path in itemPaths)
                paths.Add(path);
        }

        return paths;
    }
}
