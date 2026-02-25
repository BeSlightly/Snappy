using Snappy.Common;
using Snappy.Features.Pmp.ChangedItems;

namespace Snappy.UI.Windows;

public partial class MainWindow
{
    private readonly record struct PmpHistoryOption(int Index, string Label, string? FileMapId, string? GlamourerBase64);

    private void DrawPmpHistorySelector()
    {
        var options = BuildPmpHistoryOptions();
        EnsurePmpHistorySelection(options);

        ImGui.AlignTextToFramePadding();
        Im.Text("Glamourer Entry"u8);
        ImGui.SameLine();

        ImGui.SetNextItemWidth(-1);
        var preview = _pmpSelectedHistoryLabel ?? "Select a Glamourer entry";
        if (ImGui.BeginCombo("##PmpHistoryEntry", preview))
        {
            foreach (var option in options)
            {
                var isSelected = _pmpSelectedHistoryIndex.HasValue && option.Index == _pmpSelectedHistoryIndex.Value;
                if (ImGui.Selectable(option.Label, isSelected))
                {
                    _pmpSelectedHistoryIndex = option.Index;
                    _pmpSelectedHistoryLabel = option.Label;
                    _pmpSelectedFileMapId = option.FileMapId;
                    _pmpSelectedGlamourerBase64 = option.GlamourerBase64;
                    _pmpNeedsRebuild = true;
                }

                if (isSelected)
                    ImGui.SetItemDefaultFocus();
            }

            ImGui.EndCombo();
        }

        Im.Tooltip.OnHover("Select a snapshot or Glamourer entry to build the export list from its file map."u8);
    }

    private IReadOnlyList<PmpHistoryOption> BuildPmpHistoryOptions()
    {
        var options = new List<PmpHistoryOption>();
        for (var i = _glamourerHistory.Entries.Count - 1; i >= 0; i--)
        {
            var entry = _glamourerHistory.Entries[i];
            var label = $"Glamourer: {HistoryEntryUtil.FormatEntryPreview(entry)}";
            options.Add(new PmpHistoryOption(i, label, entry.FileMapId, entry.GlamourerString));
        }

        return options;
    }

    private void EnsurePmpHistorySelection(IReadOnlyList<PmpHistoryOption> options)
    {
        if (options.Count == 0)
        {
            _pmpSelectedHistoryLabel = null;
            _pmpSelectedHistoryIndex = null;
            _pmpSelectedFileMapId = null;
            _pmpSelectedGlamourerBase64 = null;
            return;
        }

        var stillValid = _pmpSelectedHistoryIndex.HasValue
                         && options.Any(o => o.Index == _pmpSelectedHistoryIndex.Value);
        if (stillValid)
        {
            var match = options.First(o => o.Index == _pmpSelectedHistoryIndex);
            _pmpSelectedHistoryLabel = match.Label;
            _pmpSelectedGlamourerBase64 = match.GlamourerBase64;
            _pmpSelectedFileMapId = match.FileMapId;
            return;
        }

        var defaultOption = options.FirstOrDefault(o => !string.IsNullOrWhiteSpace(o.GlamourerBase64));
        if (string.IsNullOrEmpty(defaultOption.Label))
            defaultOption = options[0];

        _pmpSelectedHistoryIndex = defaultOption.Index;
        _pmpSelectedHistoryLabel = defaultOption.Label;
        _pmpSelectedFileMapId = defaultOption.FileMapId;
        _pmpSelectedGlamourerBase64 = defaultOption.GlamourerBase64;
        _pmpNeedsRebuild = true;
    }

    private void RequestPmpChangedItemsBuild()
    {
        if (_selectedSnapshotInfo == null)
            return;

        var fileMapId = _pmpSelectedFileMapId ?? _selectedSnapshotInfo.CurrentFileMapId;
        var resolvedFileMap = FileMapUtil.ResolveFileMapWithEmptyFallback(_selectedSnapshotInfo, fileMapId);

        var gamePaths = resolvedFileMap.Keys.ToArray();
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
                var allowedKeys = await _snapshotChangedItemService.GetEquippedItemKeysAsync(_pmpSelectedGlamourerBase64);
                var customizationFilter =
                    await _snapshotChangedItemService.GetCustomizationFilterAsync(_pmpSelectedGlamourerBase64);
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
