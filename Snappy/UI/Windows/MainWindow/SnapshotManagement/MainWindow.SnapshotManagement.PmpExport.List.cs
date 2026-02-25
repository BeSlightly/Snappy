using Dalamud.Interface.Colors;
using Penumbra.UI.Classes;
using Snappy.Features.Pmp.ChangedItems;

namespace Snappy.UI.Windows;

public partial class MainWindow
{
    private void DrawPmpSelectionToolbar()
    {
        var selectedCount = _pmpItemSelection.Count(kvp => kvp.Value);
        var totalCount = _pmpItemSelection.Count;

        if (Im.Button("Select All"u8))
            SetAllPmpSelections(true);

        ImGui.SameLine();
        if (Im.Button("Clear All"u8))
            SetAllPmpSelections(false);

        ImGui.SameLine();
        Im.Text($"Selected {selectedCount} / {totalCount} items");
    }

    private void DrawPmpCategoryTabs()
    {
        if (_pmpChangedItems == null)
            return;

        using var tabBar = Im.TabBar.Begin("PmpItemCategories"u8);
        if (!tabBar)
            return;

        foreach (var category in _pmpChangedItems.Categories)
        {
            if (category.Items.Count == 0)
                continue;

            var label = BuildPmpCategoryLabel(category.Category, category.Items.Count);
            using var tab = Im.TabBar.BeginItem(label);
            if (tab)
                DrawPmpItemList(category.Category, category.Items);
        }
    }

    private void DrawPmpItemList(ChangedItemIconFlag category, IReadOnlyList<SnapshotChangedItem> items)
    {
        var tableFlags = ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.ScrollY;
        var tableSize = new Vector2(0, Math.Max(0, ImGui.GetContentRegionAvail().Y));
        if (!ImGui.BeginTable($"PmpItemsTable_{category}", 3, tableFlags, tableSize))
            return;

        ImGui.TableSetupColumn(string.Empty, ImGuiTableColumnFlags.WidthFixed, 24f * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupColumn("Item", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Files", ImGuiTableColumnFlags.WidthFixed, 60f * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableHeadersRow();

        var clipper = new ImGuiListClipper();
        clipper.Begin(items.Count);
        while (clipper.Step())
        {
            for (var i = clipper.DisplayStart; i < clipper.DisplayEnd; i++)
            {
                var item = items[i];
                var isSelected = _pmpItemSelection.TryGetValue(item.Key, out var selected) && selected;
                ImGui.TableNextRow();

                ImGui.TableNextColumn();
                using (ImRaii.PushId(item.Key))
                {
                    if (ImGui.Checkbox("##PmpItemSelect", ref isSelected))
                        _pmpItemSelection[item.Key] = isSelected;
                }

                ImGui.TableNextColumn();
                Im.Text(item.Name);
                if (!string.IsNullOrWhiteSpace(item.AdditionalData))
                {
                    ImGui.SameLine();
                    using var color = ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.DalamudGrey);
                    Im.Text(item.AdditionalData);
                }

                ImGui.TableNextColumn();
                Im.Text(item.Count.ToString());
            }
        }

        clipper.End();
        ImGui.EndTable();
    }

    private static string BuildPmpCategoryLabel(ChangedItemIconFlag category, int count)
    {
        var name = category == ChangedItemIconFlag.Unknown
            ? "Misc / Unknown"
            : Encoding.UTF8.GetString(category.ToDescription());
        return $"{name} ({count})";
    }
}
