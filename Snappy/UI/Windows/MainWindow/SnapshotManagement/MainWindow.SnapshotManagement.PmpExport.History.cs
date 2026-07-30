using Snappy.Common;
using Snappy.Features.Pmp.ChangedItems;

namespace Snappy.UI.Windows;

public partial class MainWindow
{
    private void DrawPmpHistorySelector()
    {
        EnsurePmpHistorySelection();

        ImGui.AlignTextToFramePadding();
        Im.Text("Source"u8);
        ImGui.SameLine();

        var preview = _pmpHistoryCombo.Selection != null
            ? _pmpHistoryCombo.PreviewLabel
            : "Select a Glamourer entry";
        _pmpHistoryCombo.Draw("##PmpHistoryEntry", preview, ImGui.GetContentRegionAvail().X);

        Im.Tooltip.OnHover("Glamourer history entry used to build the export list and filter equipped items."u8);
    }

    private void EnsurePmpHistorySelection()
    {
        var entries = _glamourerHistory.Entries;
        if (entries.Count == 0)
        {
            _pmpHistoryCombo.SetSelection(null);
            return;
        }

        var current = _pmpHistoryCombo.Selection;
        if (current != null && entries.Any(e => ReferenceEquals(e, current)))
            return;

        var fallback = entries.LastOrDefault(e => !string.IsNullOrWhiteSpace(e.GlamourerString)) ?? entries[^1];
        _pmpHistoryCombo.SetSelection(fallback);
    }

    private void RequestPmpChangedItemsBuild()
    {
        if (_selectedSnapshotInfo == null)
            return;

        var fileMapId = PmpSelectedFileMapId ?? _selectedSnapshotInfo.CurrentFileMapId;
        var glamourerBase64 = PmpSelectedGlamourerBase64;
        var resolvedFileMap = FileMapUtil.ResolveFileMap(_selectedSnapshotInfo, fileMapId);
        var resolvedFileSwaps = FileMapUtil.ResolveFileSwaps(_selectedSnapshotInfo, fileMapId);

        var gamePaths = resolvedFileMap.Keys.Concat(resolvedFileSwaps.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var manipulations = FileMapUtil.ResolveManipulation(_selectedSnapshotInfo, fileMapId);
        var filesDirectory = _selectedSnapshot == null
            ? null
            : SnapshotPaths.From(_selectedSnapshot.FullName).FilesDirectory;
        _pmpIsBuilding = true;
        _pmpBuildError = null;
        _pmpChangedItems = null;
        _pmpItemSelection.Clear();
        var buildToken = ++_pmpBuildToken;

        _snappy.ExecuteBackgroundTask(async () =>
        {
            try
            {
                var result = await _snapshotChangedItemService.BuildChangedItemsAsync(gamePaths, manipulations,
                    resolvedFileMap, filesDirectory);
                var allowedKeys = await _snapshotChangedItemService.GetEquippedItemKeysAsync(glamourerBase64);
                var customizationFilter =
                    await _snapshotChangedItemService.GetCustomizationFilterAsync(glamourerBase64);
                var customizationOverrides =
                    await _snapshotChangedItemService.GetCustomizationKeysFromManipulationsAsync(
                        manipulations,
                        customizationFilter);
                var filtered = _snapshotChangedItemService.FilterToItemKeys(result, allowedKeys, customizationFilter,
                    customizationOverrides);
                _snappy.QueueAction(() =>
                {
                    if (buildToken != _pmpBuildToken)
                        return;

                    ApplyPmpChangedItems(filtered);
                    _pmpIsBuilding = false;
                });
            }
            catch (Exception ex)
            {
                _snappy.QueueAction(() =>
                {
                    if (buildToken != _pmpBuildToken)
                        return;

                    _pmpBuildError = $"Failed to build item list: {ex.Message}";
                    _pmpIsBuilding = false;
                });
            }
        });
    }

    private void ApplyPmpChangedItems(SnapshotChangedItemSet items)
    {
        _pmpChangedItems = items;
        _pmpItemSelection.Clear();
        foreach (var item in items.AllItems)
            _pmpItemSelection[item.Key] = false;
    }
}
