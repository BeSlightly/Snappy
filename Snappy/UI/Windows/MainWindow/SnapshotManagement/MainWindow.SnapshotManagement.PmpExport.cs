using Dalamud.Interface.Colors;

namespace Snappy.UI.Windows;

public partial class MainWindow
{
    private void DrawPmpExportTab()
    {
        if (_selectedSnapshotInfo == null || _selectedSnapshot == null)
        {
            ImUtf8.Text("Select a snapshot to export."u8);
            return;
        }

        using (var warningColor = ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.DalamudYellow))
        {
            ImUtf8.TextWrapped(
                "Warning: PMP export is highly experimental. Please report any issues on GitHub.");
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
            ImUtf8.Text("Building item list..."u8);
            return;
        }

        if (!string.IsNullOrEmpty(_pmpBuildError))
        {
            using var _ = ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.DalamudRed);
            ImUtf8.Text(_pmpBuildError);
            return;
        }

        if (_pmpChangedItems == null || _pmpChangedItems.AllItems.Count == 0)
        {
            ImUtf8.Text("No modded items found for this history entry."u8);
            return;
        }

        DrawPmpSelectionToolbar();
        ImGui.Separator();

        var footerHeight = ImGui.GetFrameHeight() * 3f + ImGui.GetStyle().ItemSpacing.Y * 2f;
        var availableHeight = ImGui.GetContentRegionAvail().Y;
        var listHeight = Math.Max(0f, availableHeight - footerHeight);

        using (var listRegion = ImUtf8.Child("PmpItemsRegion", new Vector2(0, listHeight), false,
                   ImGuiWindowFlags.NoScrollbar))
        {
            if (listRegion)
                DrawPmpCategoryTabs();
        }

        ImGui.Separator();
        ImGui.Spacing();
        DrawPmpExportButton();
    }
}
