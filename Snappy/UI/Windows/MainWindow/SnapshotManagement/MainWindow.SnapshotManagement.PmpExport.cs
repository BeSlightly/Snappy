using Dalamud.Interface.Colors;

namespace Snappy.UI.Windows;

public partial class MainWindow
{
    private void DrawPmpExportTab()
    {
        if (_selectedSnapshotInfo == null || _selectedSnapshot == null)
        {
            Im.Text("Select a snapshot to export."u8);
            return;
        }

        using (var warningColor = ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.DalamudYellow))
        {
            Im.TextWrapped(
                "PMP export is experimental — please report issues on GitHub.");
        }

        ImGui.Spacing();
        DrawPmpHistorySelector();
        ImGui.Spacing();

        if (_pmpNeedsRebuild && !_pmpIsBuilding)
        {
            _pmpNeedsRebuild = false;
            RequestPmpChangedItemsBuild();
        }

        if (_pmpIsBuilding)
        {
            Im.Text("Building item list..."u8);
            return;
        }

        if (!string.IsNullOrEmpty(_pmpBuildError))
        {
            using var _ = ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.DalamudRed);
            Im.TextWrapped(_pmpBuildError);
            return;
        }

        if (_pmpChangedItems == null || _pmpChangedItems.AllItems.Count == 0)
        {
            Im.Text("No modded items found for this history entry."u8);
            return;
        }

        DrawPmpSelectionToolbar();
        ImGui.Separator();

        // Reserve footer for separator + export button without guessing oversized multiples of FrameHeight.
        var footerHeight = UiHelpers.GetLabeledIconButtonHeight()
                           + ImGui.GetStyle().ItemSpacing.Y * 3
                           + 1f * ImGuiHelpers.GlobalScale;
        var listHeight = Math.Max(0f, ImGui.GetContentRegionAvail().Y - footerHeight);

        using (var listRegion = Im.Child.Begin("PmpItemsRegion", new Vector2(0, listHeight), false,
                   WindowFlags.NoScrollbar))
        {
            if (listRegion)
                DrawPmpCategoryTabs();
        }

        ImGui.Separator();
        ImGui.Spacing();
        DrawPmpExportButton();
    }
}
